using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

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
    internal int SentThisSecond;
    internal float SecondStart;

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
    public const int MaxPayload = 16 * 1024;

    /// <summary>How many messages a channel may send per second.</summary>
    public const int MaxPerSecond = 30;

    internal const int ToAll = -1, ToHost = -2;

    // "ISMA" | version | type | channel id (4) | origin (4) | target (4) | payload
    static readonly byte[] Magic = { (byte)'I', (byte)'S', (byte)'M', (byte)'A' };
    const byte Version = 1;
    const byte THello = 1, TData = 2;
    const int Header = 4 + 1 + 1 + 4 + 4 + 4;

    static readonly Dictionary<uint, NetChannel> channels = new();
    static readonly HashSet<int> greeted = new();       // host: clients that greeted back
    static readonly Dictionary<int, float> greetAt = new(); // host: when to re-greet a client
    static bool hostGreeted;                              // client: the host greeted us

    /// <summary>
    /// Gets (or creates) the channel with this name. Use a name prefixed with your mod, like
    /// <c>"mymod.sync"</c>: two mods using the same name share a channel.
    /// </summary>
    /// <param name="name">The channel's name.</param>
    public static NetChannel Channel(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("channel name is empty", nameof(name));
        uint id = Fnv(name);
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
    public static bool Connected => Allowed && (Game.IsHost ? greeted.Count > 0 : hostGreeted);

    /// <summary>
    /// How many other players are known to run the API: on the host, the clients that greeted back;
    /// on a client, 1 once the host has greeted it.
    /// </summary>
    public static int PeerCount => Game.IsHost ? greeted.Count : hostGreeted ? 1 : 0;

    static bool Allowed => Safety.Context is PlayContext.PrivateMatch or PlayContext.ModdedServer or PlayContext.Solo;

    // ------------------------------------------------------------------ sending

    internal static bool Send(NetChannel ch, int target, byte[] data)
    {
        data ??= Array.Empty<byte>();
        if (!Allowed) { ApiLog.WarnOnce(null, "net:ctx:" + Safety.Context, $"mod messages are off in {Safety.Context} games"); return false; }
        if (data.Length > MaxPayload) { ApiLog.WarnOnce(null, "net:size:" + ch.Name, $"{ch.Name}: message over {MaxPayload} bytes dropped"); return false; }
        if (!RateOk(ch)) { ApiLog.WarnOnce(null, "net:rate:" + ch.Name, $"{ch.Name}: over {MaxPerSecond} messages/s, dropping"); return false; }

        var r = Game.Runner;
        if (r == null || !r.IsRunning) return false;
        int self = r.LocalPlayer.RawEncoded;
        var packet = Build(TData, ch.Id, self, target, data);

        if (r.IsServer)
        {
            if (target == ToHost || target == self) return false;
            int n = 0;
            foreach (var p in Active(r))
            {
                int id = p.RawEncoded;
                if (id == self || !greeted.Contains(id)) continue;
                if (target != ToAll && id != target) continue;
                r.SendReliableDataToPlayer(p, packet);
                n++;
            }
            return n > 0;
        }

        if (!hostGreeted) return false;
        r.SendReliableDataToServer(packet);
        return true;
    }

    static bool RateOk(NetChannel ch)
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now - ch.SecondStart >= 1f) { ch.SecondStart = now; ch.SentThisSecond = 0; }
        return ++ch.SentThisSecond <= MaxPerSecond;
    }

    static Il2CppStructArray<byte> Build(byte type, uint channel, int origin, int target, byte[] payload)
    {
        var b = new byte[Header + payload.Length];
        Buffer.BlockCopy(Magic, 0, b, 0, 4);
        b[4] = Version;
        b[5] = type;
        BitConverter.TryWriteBytes(new Span<byte>(b, 6, 4), channel);
        BitConverter.TryWriteBytes(new Span<byte>(b, 10, 4), origin);
        BitConverter.TryWriteBytes(new Span<byte>(b, 14, 4), target);
        Buffer.BlockCopy(payload, 0, b, Header, payload.Length);
        return b;
    }

    static void Greet(NetworkRunner r, PlayerRef p)
    {
        try { r.SendReliableDataToPlayer(p, Build(THello, 0, r.LocalPlayer.RawEncoded, p.RawEncoded, Array.Empty<byte>())); }
        catch (Exception e) { ApiLog.WarnOnce(null, "net:greet", $"could not greet a player: {e.Message}"); }
    }

    // ------------------------------------------------------------------ session plumbing

    internal static void OnPlayerJoined(NetworkRunner r, PlayerRef p)
    {
        if (!Allowed || r == null || !r.IsServer || p == r.LocalPlayer) return;
        greetAt[p.RawEncoded] = UnityEngine.Time.realtimeSinceStartup;   // greeted from Tick
    }

    internal static void OnPlayerLeft(PlayerRef p)
    {
        greeted.Remove(p.RawEncoded);
        greetAt.Remove(p.RawEncoded);
    }

    internal static void Reset()
    {
        greeted.Clear();
        greetAt.Clear();
        hostGreeted = false;
        hostId = int.MinValue;
        tries.Clear();
    }

    // The host greets new clients, and again every few seconds for half a minute in case the first
    // greeting went out before the client was listening.
    const float GreetEvery = 3f;
    const int GreetTries = 10;
    static readonly Dictionary<int, int> tries = new();

    internal static void Tick()
    {
        if (greetAt.Count == 0) return;
        var r = Game.Runner;
        if (r == null || !r.IsRunning || !r.IsServer || !Allowed) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        foreach (var p in Active(r))
        {
            int id = p.RawEncoded;
            if (!greetAt.TryGetValue(id, out float at) || now < at) continue;
            tries.TryGetValue(id, out int n);
            if (greeted.Contains(id) || n >= GreetTries) { greetAt.Remove(id); tries.Remove(id); continue; }
            tries[id] = n + 1;
            greetAt[id] = now + GreetEvery;
            Greet(r, p);
        }
    }

    // ------------------------------------------------------------------ receiving

    static int dropped;

    // NetworkRunner's own receive path, before the data is handed to the game's callbacks (whose
    // OnReliableDataReceived is empty). Packets that are ours stop here; anything else goes on.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkRunner), "Fusion_Simulation_ICallbacks_OnReliableData")]
    static bool Receive(NetworkRunner __instance, PlayerRef player, Il2CppStructArray<byte> dataArray)
    {
        Events.Hooks.Live("NetworkRunner.OnReliableData");
        if (dataArray == null || dataArray.Length < Header) return true;
        byte[] b = dataArray;
        if (b[0] != Magic[0] || b[1] != Magic[1] || b[2] != Magic[2] || b[3] != Magic[3]) return true;

        try { Handle(__instance, player, b); }
        catch (Exception e) { ApiLog.WarnOnce(null, "net:recv:" + e.GetType().Name, $"bad mod message dropped: {e.Message}"); }
        return false;
    }

    static void Handle(NetworkRunner r, PlayerRef from, byte[] b)
    {
        if (b[4] != Version || !Allowed) { dropped++; return; }
        byte type = b[5];
        uint chId = BitConverter.ToUInt32(b, 6);
        int origin = BitConverter.ToInt32(b, 10);
        int target = BitConverter.ToInt32(b, 14);

        if (type == THello)
        {
            if (r.IsServer)
            {
                if (greeted.Add(from.RawEncoded))
                {
                    Plugin.Log.LogInfo($"mod messages: player #{from.PlayerId} has the API");
                    Safe.Run(PeerReady, "PeerReady", from);
                }
            }
            else if (!hostGreeted)
            {
                hostGreeted = true;
                hostId = origin;
                Plugin.Log.LogInfo("mod messages: the host has the API");
                r.SendReliableDataToServer(Build(THello, 0, r.LocalPlayer.RawEncoded, ToHost, Array.Empty<byte>()));
                Safe.Run(PeerReady, "PeerReady", from);
            }
            return;
        }
        if (type != TData) { dropped++; return; }

        var payload = new byte[b.Length - Header];
        Buffer.BlockCopy(b, Header, payload, 0, payload.Length);

        if (r.IsServer)
        {
            // Never trust a client's claimed origin: it is the connection it came in on.
            origin = from.RawEncoded;
            if (!greeted.Contains(origin)) { dropped++; return; }
            int self = r.LocalPlayer.RawEncoded;
            if (target == ToAll || (target != ToHost && target != self)) Relay(r, chId, origin, target, payload);
            if (target != ToAll && target != ToHost && target != self) return;   // for someone else
        }

        if (!channels.TryGetValue(chId, out var ch)) { dropped++; return; }
        ch.Raise(new NetMessage
        {
            Channel = ch,
            Data = payload,
            Sender = Find(r, origin),
            FromHost = !r.IsServer && origin == HostId(r),
        });
    }

    static void Relay(NetworkRunner r, uint chId, int origin, int target, byte[] payload)
    {
        var packet = Build(TData, chId, origin, target, payload);
        int self = r.LocalPlayer.RawEncoded;
        foreach (var p in Active(r))
        {
            int id = p.RawEncoded;
            if (id == self || id == origin || !greeted.Contains(id)) continue;
            if (target != ToAll && id != target) continue;
            r.SendReliableDataToPlayer(p, packet);
        }
    }

    static PlayerRef Find(NetworkRunner r, int raw)
    {
        foreach (var p in Active(r)) if (p.RawEncoded == raw) return p;
        return PlayerRef.None;
    }

    // Client side: the host's player id, learned from its greeting (which carries the sender's own id).
    static int hostId = int.MinValue;
    static int HostId(NetworkRunner r) => hostId;

    // ActivePlayers is an Il2Cpp IEnumerable, which C#'s foreach cannot walk directly.
    static List<PlayerRef> Active(NetworkRunner r)
    {
        var list = new List<PlayerRef>();
        var e = r.ActivePlayers?.GetEnumerator();
        if (e == null) return list;
        var it = e.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (it.MoveNext()) list.Add(e.Current);
        return list;
    }

    internal static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (byte c in Encoding.UTF8.GetBytes(s)) { h ^= c; h *= 16777619; }
        return h;
    }
}
