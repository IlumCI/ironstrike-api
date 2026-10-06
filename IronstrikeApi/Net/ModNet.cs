using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using IronstrikeApi.Net;

namespace IronstrikeApi;

/// <summary>A message received on a <see cref="NetChannel"/>.</summary>
public sealed class NetMessage
{
    /// <summary>The channel it arrived on.</summary>
    public NetChannel Channel { get; internal set; }
    /// <summary>
    /// Who sent it. On the host this is the connection it came from, so it cannot be forged; on a
    /// client it is what the host relayed.
    /// </summary>
    public PlayerRef Sender { get; internal set; }
    /// <summary>True if the host sent it.</summary>
    public bool FromHost { get; internal set; }
    /// <summary>The payload.</summary>
    public byte[] Data { get; internal set; }
    /// <summary>The payload as UTF-8 text.</summary>
    public string Text => Encoding.UTF8.GetString(Data ?? Array.Empty<byte>());
}

/// <summary>
/// A named message channel between the same mod on different players' machines. Get one with
/// <see cref="ModNet.Channel"/>.
/// </summary>
public sealed class NetChannel
{
    internal uint Id;

    /// <summary>The channel's name, e.g. <c>"mymod.sync"</c>.</summary>
    public string Name { get; internal set; }

    /// <summary>Raised on the main thread for every message received on this channel.</summary>
    public event Action<NetMessage> Received;

    internal void Raise(NetMessage m) => Safe.Run(Received, $"{Name} message", m);

    /// <summary>Sends to the host. On the host itself, does nothing (handle it locally).</summary>
    /// <param name="data">The payload, at most <see cref="ModNet.MaxPayload"/> bytes.</param>
    public bool SendToHost(byte[] data) => ModNet.Send(this, ModNet.ToHost, data);

    /// <summary>Sends to one player. Clients' messages are relayed through the host.</summary>
    /// <param name="player">The recipient.</param>
    /// <param name="data">The payload.</param>
    public bool SendTo(PlayerRef player, byte[] data) => ModNet.Send(this, player.RawEncoded, data);

    /// <summary>Sends to every other player with the API. Not echoed back to the sender.</summary>
    /// <param name="data">The payload.</param>
    public bool Broadcast(byte[] data) => ModNet.Send(this, ModNet.ToAll, data);

    /// <summary>Sends UTF-8 text to every other player.</summary>
    /// <param name="text">The text.</param>
    public bool Broadcast(string text) => Broadcast(Encoding.UTF8.GetBytes(text ?? ""));

    /// <summary>Sends UTF-8 text to one player.</summary>
    /// <param name="player">The recipient.</param>
    /// <param name="text">The text.</param>
    public bool SendTo(PlayerRef player, string text) => SendTo(player, Encoding.UTF8.GetBytes(text ?? ""));

    /// <summary>Sends UTF-8 text to the host.</summary>
    /// <param name="text">The text.</param>
    public bool SendToHost(string text) => SendToHost(Encoding.UTF8.GetBytes(text ?? ""));
}

/// <summary>
/// Mod-to-mod messages between players, over Fusion's reliable data channel (the same connection
/// the game uses, no extra servers). The game ignores this data, so it changes nothing for anyone
/// without the API.
/// </summary>
/// <remarks>
/// <para>Topology follows the game's: everything goes through the host. A client's message to another
/// client is relayed by the host.</para>
/// <para>Peers greet each other when they connect; nothing but the greeting is sent to a peer that
/// has not greeted back, so players without the API never receive mod traffic.</para>
/// <para>Off in public games. Payloads are never logged.</para>
/// <para>Limits: <see cref="MaxPayload"/> bytes per message, <see cref="MaxPerSecond"/> messages per
/// second per channel; messages over either are dropped with a warning.</para>
/// </remarks>
[HarmonyPatch]
public static class ModNet
{
    /// <summary>The largest payload a message may carry, in bytes.</summary>
    public const int MaxPayload = NetCore.MaxPayload;

    /// <summary>How many messages a channel may send per second.</summary>
    public const int MaxPerSecond = NetCore.MaxPerSecond;

    internal const int ToAll = NetCore.ToAll, ToHost = NetCore.ToHost;

    static readonly Dictionary<uint, NetChannel> channels = new();
    internal static readonly NetCore Core = new()
    {
        Clock = () => UnityEngine.Time.realtimeSinceStartup,
        Allowed = () => Allowed,
        HasChannel = id => channels.ContainsKey(id),
        Info = s => Plugin.Log.LogInfo(s),
        Warn = s => ApiLog.WarnOnce(null, "net:" + s, s),
    };

    static ModNet()
    {
        Core.Deliver = (chId, origin, fromHost, payload) =>
        {
            if (!channels.TryGetValue(chId, out var ch)) return;
            ch.Raise(new NetMessage { Channel = ch, Data = payload, Sender = transport.Ref(origin), FromHost = fromHost });
        };
        Core.PeerReady = id => Safe.Run(PeerReady, "PeerReady", transport.Ref(id));
    }

    /// <summary>
    /// Gets (or creates) the channel with this name. Use a name prefixed with your mod, like
    /// <c>"mymod.sync"</c>: two mods using the same name share a channel.
    /// </summary>
    /// <param name="name">The channel's name.</param>
    public static NetChannel Channel(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("channel name is empty", nameof(name));
        uint id = NetCore.Fnv(name);
        if (channels.TryGetValue(id, out var c))
        {
            if (c.Name != name) throw new ArgumentException($"channel '{name}' collides with '{c.Name}'; pick another name");
            return c;
        }
        return channels[id] = new NetChannel { Name = name, Id = id };
    }

    /// <summary>
    /// Another player running the API is now reachable: on the host, a client that greeted back; on
    /// a client, the host. Send anything a newcomer needs (current state, a hello) from here: at
    /// <see cref="GameEvents.PlayerJoined"/> the greeting has not happened yet.
    /// </summary>
    public static event Action<PlayerRef> PeerReady;

    /// <summary>True when there is a session with at least one other player running the API.</summary>
    public static bool Connected => Allowed && PeerCount > 0;

    /// <summary>
    /// How many other players are known to run the API: on the host, the clients that greeted back;
    /// on a client, 1 once the host has greeted it.
    /// </summary>
    public static int PeerCount => Core.PeerCount(transport.Bind());

    static bool Allowed => Safety.Context is PlayContext.PrivateMatch or PlayContext.ModdedServer or PlayContext.Solo;

    internal static bool Send(NetChannel ch, int target, byte[] data)
    {
        var r = Core.Send(transport.Bind(), ch.Id, target, data);
        switch (r)
        {
            case NetCore.SendResult.NotAllowed:
                ApiLog.WarnOnce(null, "net:ctx:" + Safety.Context, $"mod messages are off in {Safety.Context} games"); break;
            case NetCore.SendResult.TooBig:
                ApiLog.WarnOnce(null, "net:size:" + ch.Name, $"{ch.Name}: message over {MaxPayload} bytes dropped"); break;
            case NetCore.SendResult.RateLimited:
                ApiLog.WarnOnce(null, "net:rate:" + ch.Name, $"{ch.Name}: over {MaxPerSecond} messages/s, dropping"); break;
        }
        return r == NetCore.SendResult.Sent;
    }

    internal static void OnPlayerJoined(NetworkRunner r, PlayerRef p)
    {
        if (!Allowed) return;
        Core.PlayerJoined(transport.Bind(), p.RawEncoded);
    }

    internal static void OnPlayerLeft(PlayerRef p) => Core.PlayerLeft(p.RawEncoded);

    internal static void Reset() => Core.Reset();

    internal static void Tick() => Core.Tick(transport.Bind());

    // NetworkRunner's own receive path, before the data is handed to the game's callbacks (whose
    // OnReliableDataReceived is empty). Packets that are ours stop here; anything else goes on.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkRunner), "Fusion_Simulation_ICallbacks_OnReliableData")]
    static bool Receive(NetworkRunner __instance, PlayerRef player, Il2CppStructArray<byte> dataArray)
    {
        Events.Hooks.Live("NetworkRunner.OnReliableData");
        if (dataArray == null || dataArray.Length < 4) return true;
        byte[] b = dataArray;
        if (!NetCore.IsOurs(b)) return true;
        var t = transport.Bind(__instance);
        return !Core.Receive(t, player.RawEncoded, b);
    }

    internal static uint Fnv(string s) => NetCore.Fnv(s);

    // ------------------------------------------------------------------ the Fusion transport

    static readonly RunnerTransport transport = new();

    sealed class RunnerTransport : INetTransport
    {
        NetworkRunner runner;
        readonly List<PlayerRef> refs = new();
        readonly List<int> ids = new();

        // The current session's runner, or null when there is none (sending then fails cleanly).
        public RunnerTransport Bind(NetworkRunner r = null)
        {
            runner = r ?? Game.Runner;
            refs.Clear(); ids.Clear();
            if (runner == null || !runner.IsRunning) { runner = null; return null; }
            // ActivePlayers is an Il2Cpp IEnumerable, which C#'s foreach cannot walk directly.
            var e = runner.ActivePlayers?.GetEnumerator();
            if (e != null)
            {
                var it = e.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (it.MoveNext()) { refs.Add(e.Current); ids.Add(e.Current.RawEncoded); }
            }
            return this;
        }

        public PlayerRef Ref(int id)
        {
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return refs[i];
            return PlayerRef.None;
        }

        public bool IsServer => runner.IsServer;
        public int LocalId => runner.LocalPlayer.RawEncoded;
        public IReadOnlyList<int> Players => ids;
        public void SendToPlayer(int id, byte[] packet) => runner.SendReliableDataToPlayer(Ref(id), packet);
        public void SendToServer(byte[] packet) => runner.SendReliableDataToServer(packet);
    }
}
