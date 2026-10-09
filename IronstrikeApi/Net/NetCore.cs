using System;
using System.Collections.Generic;
using System.Text;

namespace IronstrikeApi.Net;

// The transport NetCore sends through: in the game, a Fusion NetworkRunner; in the tests, a
// simulated network. Player ids are Fusion's PlayerRef.RawEncoded values.
internal interface INetTransport
{
    bool IsServer { get; }
    int LocalId { get; }
    IReadOnlyList<int> Players { get; }          // active players, the local one included
    void SendToPlayer(int id, byte[] packet);    // host only
    void SendToServer(byte[] packet);            // client only
}

// ModNet's protocol, free of Unity and Il2Cpp so it can be tested on its own.
//
// Wire format: "ISMA" | version | type | channel (4) | origin (4) | target (4) | payload
//   origin: the sender's id (the host overwrites it with the real connection on receipt)
//   target: a player id, ToAll (-1) or ToHost (-2)
//
// Everything goes through the host, as the game's own traffic does. Peers greet each other first;
// nothing but a greeting is ever sent to a player who has not greeted back.
internal sealed class NetCore
{
    public const int MaxPayload = 16 * 1024;
    public const int MaxPerSecond = 30;            // per channel, sending
    public const int MaxInPerSecond = 120;         // per sender, at the host (relay amplification)
    public const int ToAll = -1, ToHost = -2;
    public const byte Version = 1;
    public const byte THello = 1, TData = 2;
    public const int Header = 4 + 1 + 1 + 4 + 4 + 4;
    public const float GreetEvery = 3f;
    public const int GreetTries = 10;

    static readonly byte[] Magic = { (byte)'I', (byte)'S', (byte)'M', (byte)'A' };

    readonly HashSet<int> greeted = new();                 // host: clients that greeted back
    readonly HashSet<int> sameContent = new();             // host: clients whose content fingerprint matches ours
    bool hostSameContent;                                  // client: the host's fingerprint matches ours
    readonly Dictionary<int, float> greetAt = new();       // host: next greeting due
    readonly Dictionary<int, int> tries = new();
    readonly Dictionary<uint, (float Start, int Count)> sendRate = new();
    readonly Dictionary<int, (float Start, int Count)> recvRate = new();
    bool hostGreeted;                                      // client: the host greeted us
    int hostId = int.MinValue;

    public Func<float> Clock = () => 0f;
    public Func<bool> Allowed = () => true;
    public Func<uint, bool> HasChannel = _ => true;
    public Action<uint, int, bool, byte[]> Deliver;       // channel, origin, fromHost, payload
    public Action<int> PeerReady;
    public Func<byte[]> Manifest = () => Array.Empty<byte>();   // carried in the greeting
    public Action<int> ManifestMismatch;                         // a peer's content differs from ours
    public Action<string> Info = _ => { }, Warn = _ => { };

    public int Dropped { get; private set; }

    // Host: every other player in the session greeted with the same content fingerprint (true when
    // alone). A player without the API never greets, so it never counts as the same.
    // Client: the host greeted with the same fingerprint (whether everyone else did is the host's to say).
    public bool SameContentEverywhere(INetTransport t)
    {
        if (t == null) return false;
        if (!t.IsServer) return hostGreeted && hostSameContent;
        foreach (int id in t.Players) if (id != t.LocalId && !sameContent.Contains(id)) return false;
        return true;
    }
    public int PeerCount(INetTransport t) => t != null && t.IsServer ? greeted.Count : hostGreeted ? 1 : 0;
    public bool IsPeer(int id) => greeted.Contains(id);
    public bool HostGreeted => hostGreeted;
    public int HostId => hostId;

    // ------------------------------------------------------------------ framing

    public static byte[] Build(byte type, uint channel, int origin, int target, byte[] payload)
    {
        payload ??= Array.Empty<byte>();
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

    // True if the packet is ours (the game must not see it), whether or not it was valid.
    public static bool IsOurs(byte[] b)
        => b != null && b.Length >= 4 && b[0] == Magic[0] && b[1] == Magic[1] && b[2] == Magic[2] && b[3] == Magic[3];

    public static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (byte c in Encoding.UTF8.GetBytes(s ?? "")) { h ^= c; h *= 16777619; }
        return h;
    }

    // ------------------------------------------------------------------ sending

    public enum SendResult { Sent, NotAllowed, TooBig, RateLimited, NotConnected, NoRecipient }

    public SendResult Send(INetTransport t, uint channel, int target, byte[] data)
    {
        data ??= Array.Empty<byte>();
        if (!Allowed()) return SendResult.NotAllowed;
        if (data.Length > MaxPayload) return SendResult.TooBig;
        if (t == null) return SendResult.NotConnected;
        if (!Take(sendRate, channel, MaxPerSecond)) return SendResult.RateLimited;

        int self = t.LocalId;
        var packet = Build(TData, channel, self, target, data);

        if (t.IsServer)
        {
            if (target == ToHost || target == self) return SendResult.NoRecipient;
            int n = 0;
            foreach (int id in t.Players)
            {
                if (id == self || !greeted.Contains(id)) continue;
                if (target != ToAll && id != target) continue;
                t.SendToPlayer(id, packet);
                n++;
            }
            return n > 0 ? SendResult.Sent : SendResult.NoRecipient;
        }

        if (!hostGreeted) return SendResult.NotConnected;
        if (target == self) return SendResult.NoRecipient;
        t.SendToServer(packet);
        return SendResult.Sent;
    }

    bool Take<TKey>(Dictionary<TKey, (float Start, int Count)> rate, TKey key, int max)
    {
        float now = Clock();
        rate.TryGetValue(key, out var r);
        if (now - r.Start >= 1f || now < r.Start) r = (now, 0);
        r.Count++;
        rate[key] = r;
        return r.Count <= max;
    }

    // ------------------------------------------------------------------ session plumbing

    public void PlayerJoined(INetTransport t, int id)
    {
        // A rejoining (or reused) id starts over: it has to greet back again.
        greeted.Remove(id);
        sameContent.Remove(id);
        recvRate.Remove(id);
        if (t == null || !t.IsServer || id == t.LocalId) return;
        greetAt[id] = Clock();
        tries.Remove(id);
    }

    public void PlayerLeft(int id)
    {
        greeted.Remove(id);
        sameContent.Remove(id);
        greetAt.Remove(id);
        tries.Remove(id);
        recvRate.Remove(id);
    }

    public void Reset()
    {
        greeted.Clear();
        sameContent.Clear();
        greetAt.Clear();
        tries.Clear();
        recvRate.Clear();
        hostGreeted = false;
        hostSameContent = false;
        hostId = int.MinValue;
    }

    // The host greets new clients, and again every few seconds for half a minute: the first greeting
    // can arrive before the client has classified its session and is ready to answer.
    public void Tick(INetTransport t)
    {
        if (t == null || !t.IsServer) return;
        // Forget anyone who is no longer here, even if the leave callback never came.
        if (greeted.Count > 0 || greetAt.Count > 0)
        {
            var present = new HashSet<int>(t.Players);
            greeted.RemoveWhere(id => !present.Contains(id));
            foreach (var id in new List<int>(greetAt.Keys)) if (!present.Contains(id)) { greetAt.Remove(id); tries.Remove(id); }
        }
        if (greetAt.Count == 0 || !Allowed()) return;

        float now = Clock();
        foreach (var id in new List<int>(greetAt.Keys))
        {
            if (now < greetAt[id]) continue;
            tries.TryGetValue(id, out int n);
            if (greeted.Contains(id) || n >= GreetTries) { greetAt.Remove(id); tries.Remove(id); continue; }
            tries[id] = n + 1;
            greetAt[id] = now + GreetEvery;
            try { t.SendToPlayer(id, Build(THello, 0, t.LocalId, id, Manifest())); }
            catch (Exception e) { Warn($"could not greet a player: {e.Message}"); }
        }
    }

    // ------------------------------------------------------------------ receiving

    // Returns true if the packet was ours (and must not reach the game), valid or not.
    public bool Receive(INetTransport t, int from, byte[] b)
    {
        if (!IsOurs(b)) return false;
        try { Handle(t, from, b); }
        catch (Exception e) { Dropped++; Warn($"bad mod message dropped: {e.GetType().Name}"); }
        return true;
    }

    void Handle(INetTransport t, int from, byte[] b)
    {
        if (t == null || b.Length < Header || b[4] != Version || !Allowed()) { Dropped++; return; }
        if (b.Length - Header > MaxPayload) { Dropped++; return; }
        byte type = b[5];
        uint chId = BitConverter.ToUInt32(b, 6);
        int origin = BitConverter.ToInt32(b, 10);
        int target = BitConverter.ToInt32(b, 14);

        if (t.IsServer)
        {
            // Only the connection is trusted, never the claimed origin; and nobody may flood the host
            // into relaying for them.
            origin = from;
            if (from == t.LocalId || !Present(t, from)) { Dropped++; return; }
            if (!Take(recvRate, from, MaxInPerSecond)) { Dropped++; return; }
        }

        if (type == THello)
        {
            var theirs = new byte[b.Length - Header];
            Buffer.BlockCopy(b, Header, theirs, 0, theirs.Length);
            var ours = Manifest() ?? Array.Empty<byte>();
            bool same = theirs.AsSpan().SequenceEqual(ours);
            if (!same) ManifestMismatch?.Invoke(t.IsServer ? from : origin);
            if (t.IsServer) { if (same) sameContent.Add(from); else sameContent.Remove(from); }
            else hostSameContent = same;
            if (t.IsServer)
            {
                if (greeted.Add(from))
                {
                    greetAt.Remove(from);
                    tries.Remove(from);
                    Info($"mod messages: player #{from} has the API");
                    PeerReady?.Invoke(from);
                }
            }
            else
            {
                // Answer every greeting, so a lost answer is retried with the host's next greeting.
                t.SendToServer(Build(THello, 0, t.LocalId, ToHost, Manifest()));
                if (!hostGreeted)
                {
                    hostGreeted = true;
                    hostId = origin;
                    Info("mod messages: the host has the API");
                    PeerReady?.Invoke(origin);
                }
            }
            return;
        }
        if (type != TData) { Dropped++; return; }

        var payload = new byte[b.Length - Header];
        Buffer.BlockCopy(b, Header, payload, 0, payload.Length);

        bool forMe = true;
        if (t.IsServer)
        {
            if (!greeted.Contains(origin)) { Dropped++; return; }
            int self = t.LocalId;
            bool toOthers = target == ToAll || (target != ToHost && target != self);
            if (toOthers) Relay(t, chId, origin, target, payload);
            forMe = target == ToAll || target == ToHost || target == self;
        }
        else if (!hostGreeted) { Dropped++; return; }        // only the host talks to clients

        if (!forMe) return;
        if (!HasChannel(chId)) { Dropped++; return; }
        Deliver?.Invoke(chId, origin, !t.IsServer && origin == hostId, payload);
    }

    void Relay(INetTransport t, uint chId, int origin, int target, byte[] payload)
    {
        var packet = Build(TData, chId, origin, target, payload);
        int self = t.LocalId;
        foreach (int id in t.Players)
        {
            if (id == self || id == origin || !greeted.Contains(id)) continue;
            if (target != ToAll && id != target) continue;
            t.SendToPlayer(id, packet);
        }
    }

    static bool Present(INetTransport t, int id)
    {
        foreach (int p in t.Players) if (p == id) return true;
        return false;
    }
}
