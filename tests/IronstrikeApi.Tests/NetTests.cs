using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IronstrikeApi.Net;
using Xunit;

namespace IronstrikeApi.Tests;

// A simulated session: one host, any number of clients, some of them without the API ("vanilla").
// Packets go through a queue the test can drop, duplicate or reorder, and a clock the test moves.
sealed class SimNet
{
    public float Now;
    public readonly Random Rng = new(1234);
    public double DropRate, DupRate;
    public bool Shuffle;

    public sealed class Node : INetTransport
    {
        public SimNet Net;
        public int Id;
        public bool Vanilla;
        public NetCore Core;
        public readonly List<(uint Ch, int Origin, bool FromHost, byte[] Data)> Got = new();
        public readonly List<int> Ready = new();
        public readonly List<byte[]> Raw = new();          // everything that arrived at all
        public bool Allowed = true;

        public bool IsServer => Id == Net.HostId;
        public int LocalId => Id;
        public IReadOnlyList<int> Players => Net.Nodes.Keys.ToList();
        public void SendToPlayer(int id, byte[] packet)
        {
            Assert.True(IsServer, "only the host sends to players");
            Net.Queue.Add((Id, id, packet));
        }
        public void SendToServer(byte[] packet)
        {
            Assert.False(IsServer, "the host does not send to itself");
            Net.Queue.Add((Id, Net.HostId, packet));
        }
    }

    public int HostId = 1;
    public readonly Dictionary<int, Node> Nodes = new();
    public readonly List<(int From, int To, byte[] Packet)> Queue = new();
    public readonly HashSet<uint> Channels = new();

    public Node Add(int id, bool vanilla = false)
    {
        var n = new Node { Net = this, Id = id, Vanilla = vanilla };
        if (!vanilla)
        {
            n.Core = new NetCore
            {
                Clock = () => Now,
                Allowed = () => n.Allowed,
                HasChannel = c => Channels.Contains(c),
            };
            n.Core.Deliver = (c, o, fh, d) => n.Got.Add((c, o, fh, d));
            n.Core.PeerReady = p => n.Ready.Add(p);
        }
        Nodes[id] = n;
        if (id != HostId && Nodes.TryGetValue(HostId, out var host) && host.Core != null) host.Core.PlayerJoined(host, id);
        return n;
    }

    public void Remove(int id)
    {
        Nodes.Remove(id);
        if (Nodes.TryGetValue(HostId, out var host)) host.Core?.PlayerLeft(id);
    }

    public Node Host => Nodes[HostId];

    // Delivers what is queued (and what that causes), ticking every node's core once per round.
    public void Pump(int rounds = 20, float dt = 0.1f)
    {
        for (int r = 0; r < rounds; r++)
        {
            foreach (var n in Nodes.Values.ToList()) n.Core?.Tick(n);
            var batch = Queue.ToList();
            Queue.Clear();
            if (Shuffle) batch = batch.OrderBy(_ => Rng.Next()).ToList();
            foreach (var (from, to, packet) in batch)
            {
                if (Rng.NextDouble() < DropRate) continue;
                int copies = Rng.NextDouble() < DupRate ? 2 : 1;
                for (int c = 0; c < copies; c++) Deliver(from, to, packet);
            }
            Now += dt;
        }
    }

    public void Deliver(int from, int to, byte[] packet)
    {
        if (!Nodes.TryGetValue(to, out var n)) return;
        n.Raw.Add(packet);
        if (n.Core != null) n.Core.Receive(n, from, packet);
    }
}

public class NetTests
{
    static readonly uint Ch = NetCore.Fnv("test.chan");

    static SimNet Session(int clients, params int[] vanilla)
    {
        var net = new SimNet();
        net.Channels.Add(Ch);
        net.Add(1);
        for (int i = 0; i < clients; i++) net.Add(2 + i, vanilla.Contains(2 + i));
        net.Pump();
        return net;
    }

    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Handshake_connects_every_API_peer_and_fires_PeerReady_once()
    {
        var net = Session(3);
        Assert.Equal(3, net.Host.Core.PeerCount(net.Host));
        Assert.Equal(new[] { 2, 3, 4 }, net.Host.Ready.OrderBy(x => x));
        foreach (var id in new[] { 2, 3, 4 })
        {
            Assert.True(net.Nodes[id].Core.HostGreeted);
            Assert.Equal(new[] { 1 }, net.Nodes[id].Ready);
            Assert.Equal(1, net.Nodes[id].Core.HostId);
        }
        // Repeated greetings after the fact must not fire PeerReady again.
        net.Pump(100, 1f);
        Assert.Equal(3, net.Host.Ready.Count);
        Assert.All(new[] { 2, 3, 4 }, id => Assert.Single(net.Nodes[id].Ready));
    }

    [Fact]
    public void Vanilla_players_only_ever_receive_greetings()
    {
        var net = Session(3, vanilla: 3);
        for (int i = 0; i < 20; i++)
        {
            net.Host.Core.Send(net.Host, Ch, NetCore.ToAll, B("from host"));
            net.Nodes[2].Core.Send(net.Nodes[2], Ch, NetCore.ToAll, B("from 2"));
            net.Nodes[2].Core.Send(net.Nodes[2], Ch, 3, B("to the vanilla one"));
            net.Host.Core.Send(net.Host, Ch, 3, B("host to vanilla"));
            net.Now += 1f;
            net.Pump(2);
        }
        var vanilla = net.Nodes[3];
        Assert.NotEmpty(vanilla.Raw);                       // it was greeted
        Assert.All(vanilla.Raw, p => Assert.Equal(NetCore.THello, p[5]));
        Assert.True(vanilla.Raw.Count <= NetCore.GreetTries, $"greeted {vanilla.Raw.Count} times");
        Assert.Equal(2, net.Host.Core.PeerCount(net.Host));
    }

    [Fact]
    public void Client_broadcast_reaches_everyone_else_with_the_API_exactly_once()
    {
        var net = Session(4, vanilla: 5);
        Assert.Equal(SendOk, net.Nodes[2].Core.Send(net.Nodes[2], Ch, NetCore.ToAll, B("hi")));
        net.Pump();
        Assert.Single(net.Host.Got);
        Assert.Empty(net.Nodes[2].Got);                     // not echoed
        Assert.Single(net.Nodes[3].Got);
        Assert.Single(net.Nodes[4].Got);
        Assert.Equal(2, net.Nodes[3].Got[0].Origin);
        Assert.False(net.Nodes[3].Got[0].FromHost);
        Assert.Equal("hi", Encoding.UTF8.GetString(net.Nodes[3].Got[0].Data));
    }

    const NetCore.SendResult SendOk = NetCore.SendResult.Sent;

    [Fact]
    public void Directed_messages_reach_only_their_target()
    {
        var net = Session(3);
        net.Nodes[2].Core.Send(net.Nodes[2], Ch, 4, B("psst"));
        net.Nodes[3].Core.Send(net.Nodes[3], Ch, 1, B("to host"));
        net.Host.Core.Send(net.Host, Ch, 2, B("host to 2"));
        net.Pump();
        Assert.Single(net.Nodes[4].Got);
        Assert.Equal(2, net.Nodes[4].Got[0].Origin);
        Assert.Single(net.Host.Got);
        Assert.Equal(3, net.Host.Got[0].Origin);
        Assert.Single(net.Nodes[2].Got);
        Assert.True(net.Nodes[2].Got[0].FromHost);
        Assert.Empty(net.Nodes[3].Got);
    }

    [Fact]
    public void Host_cannot_send_to_itself_and_clients_wait_for_the_greeting()
    {
        var net = new SimNet();
        net.Channels.Add(Ch);
        net.Add(1);
        var c = net.Add(2);
        Assert.Equal(NetCore.SendResult.NotConnected, c.Core.Send(c, Ch, NetCore.ToAll, B("too early")));
        Assert.Equal(NetCore.SendResult.NoRecipient, net.Host.Core.Send(net.Host, Ch, NetCore.ToHost, B("x")));
        Assert.Equal(NetCore.SendResult.NoRecipient, net.Host.Core.Send(net.Host, Ch, 1, B("x")));
        Assert.Equal(NetCore.SendResult.NoRecipient, net.Host.Core.Send(net.Host, Ch, NetCore.ToAll, B("nobody yet")));
        net.Pump();
        Assert.Equal(NetCore.SendResult.NoRecipient, c.Core.Send(c, Ch, 2, B("to myself")));
        Assert.Equal(SendOk, c.Core.Send(c, Ch, NetCore.ToHost, B("now")));
    }

    [Fact]
    public void Forged_origins_are_replaced_by_the_real_sender()
    {
        var net = Session(3);
        // Client 3 claims to be the host, then client 2.
        foreach (int fake in new[] { 1, 2, 99, int.MinValue })
            net.Deliver(3, 1, NetCore.Build(NetCore.TData, Ch, fake, NetCore.ToAll, B("forged")));
        net.Pump();
        Assert.Equal(4, net.Host.Got.Count);
        Assert.All(net.Host.Got, g => Assert.Equal(3, g.Origin));
        Assert.All(net.Nodes[2].Got, g => { Assert.Equal(3, g.Origin); Assert.False(g.FromHost); });
        Assert.Equal(4, net.Nodes[2].Got.Count);
    }

    [Fact]
    public void A_peer_that_never_greeted_cannot_inject_data()
    {
        var net = Session(2, vanilla: 3);
        // A modified "vanilla" client crafts data without ever greeting.
        net.Deliver(3, 1, NetCore.Build(NetCore.TData, Ch, 3, NetCore.ToAll, B("sneaky")));
        net.Pump();
        Assert.Empty(net.Host.Got);
        Assert.Empty(net.Nodes[2].Got);
        Assert.True(net.Host.Core.Dropped > 0);
    }

    [Fact]
    public void Clients_ignore_data_that_did_not_come_through_a_greeted_host()
    {
        var net = new SimNet();
        net.Channels.Add(Ch);
        net.Add(1, vanilla: true);                          // a host without the API
        var c = net.Add(2);
        net.Deliver(1, 2, NetCore.Build(NetCore.TData, Ch, 1, 2, B("not greeted")));
        Assert.Empty(c.Got);
    }

    [Fact]
    public void Garbage_never_throws_and_only_ours_is_swallowed()
    {
        var net = Session(2);
        var rng = new Random(42);
        var cases = new List<byte[]>
        {
            null, Array.Empty<byte>(), new byte[] { 1 }, B("ISM"), B("ISMA"), B("ISMAx"),
            NetCore.Build(NetCore.TData, Ch, 0, 0, null)[..(NetCore.Header - 1)],
            Mutate(NetCore.Build(NetCore.TData, Ch, 2, NetCore.ToAll, B("v")), 4, 99),          // future version
            Mutate(NetCore.Build(NetCore.TData, Ch, 2, NetCore.ToAll, B("t")), 5, 77),          // unknown type
            Mutate(NetCore.Build(NetCore.TData, Ch, 2, NetCore.ToAll, B("t")), 5, 0),
        };
        for (int i = 0; i < 5000; i++)
        {
            var b = new byte[rng.Next(0, 64)];
            rng.NextBytes(b);
            if (i % 2 == 0 && b.Length >= 4) { b[0] = (byte)'I'; b[1] = (byte)'S'; b[2] = (byte)'M'; b[3] = (byte)'A'; }
            if (i % 3 == 0 && b.Length >= 6) b[4] = NetCore.Version;
            cases.Add(b);
        }
        foreach (var node in new[] { net.Host, net.Nodes[2] })
            foreach (var b in cases)
            {
                int from = node.IsServer ? 2 : 1;
                bool ours = node.Core.Receive(node, from, b);
                Assert.Equal(NetCore.IsOurs(b), ours);
            }
        // Whatever got through was well-formed and on a known channel.
        net.Pump();
        Assert.All(net.Nodes[2].Got.Concat(net.Host.Got), g => Assert.Equal(Ch, g.Ch));
    }

    static byte[] Mutate(byte[] b, int at, byte v) { b[at] = v; return b; }

    [Fact]
    public void Payload_limits_hold_both_ways()
    {
        var net = Session(2);
        var max = new byte[NetCore.MaxPayload];
        new Random(7).NextBytes(max);
        Assert.Equal(SendOk, net.Nodes[2].Core.Send(net.Nodes[2], Ch, NetCore.ToAll, max));
        Assert.Equal(NetCore.SendResult.TooBig, net.Nodes[2].Core.Send(net.Nodes[2], Ch, NetCore.ToAll, new byte[NetCore.MaxPayload + 1]));
        Assert.Equal(SendOk, net.Nodes[2].Core.Send(net.Nodes[2], Ch, NetCore.ToAll, null));     // empty is fine
        net.Pump();
        Assert.Equal(max, net.Nodes[3].Got[0].Data);
        Assert.Empty(net.Nodes[3].Got[1].Data);

        // A crafted oversized packet is dropped by the host, not relayed.
        int before = net.Nodes[3].Got.Count;
        net.Deliver(2, 1, NetCore.Build(NetCore.TData, Ch, 2, NetCore.ToAll, new byte[NetCore.MaxPayload + 100]));
        net.Pump();
        Assert.Equal(before, net.Nodes[3].Got.Count);
    }

    [Fact]
    public void Send_rate_is_limited_per_channel_and_recovers()
    {
        var net = Session(1);
        var c = net.Nodes[2];
        int sent = Enumerable.Range(0, 100).Count(_ => c.Core.Send(c, Ch, NetCore.ToHost, B("x")) == SendOk);
        Assert.Equal(NetCore.MaxPerSecond, sent);
        // Another channel has its own budget.
        uint other = NetCore.Fnv("test.other");
        net.Channels.Add(other);
        Assert.Equal(SendOk, c.Core.Send(c, other, NetCore.ToHost, B("y")));
        net.Now += 1.01f;
        Assert.Equal(SendOk, c.Core.Send(c, Ch, NetCore.ToHost, B("x")));
        // A clock that jumps backwards (a restarted timer) must not lock the channel forever.
        for (int i = 0; i < 40; i++) c.Core.Send(c, Ch, NetCore.ToHost, B("x"));
        net.Now -= 500f;
        Assert.Equal(SendOk, c.Core.Send(c, Ch, NetCore.ToHost, B("x")));
    }

    [Fact]
    public void A_flooding_client_cannot_make_the_host_relay_unboundedly()
    {
        var net = Session(3);
        for (int i = 0; i < 5000; i++)
            net.Deliver(2, 1, NetCore.Build(NetCore.TData, Ch, 2, NetCore.ToAll, B("spam")));
        Assert.True(net.Host.Got.Count <= NetCore.MaxInPerSecond, $"host accepted {net.Host.Got.Count}");
        int relayed = net.Queue.Count(q => q.To == 3);
        Assert.True(relayed <= NetCore.MaxInPerSecond, $"relayed {relayed}");
        // The flooder is throttled, not everyone: client 4 still gets through.
        net.Deliver(4, 1, NetCore.Build(NetCore.TData, Ch, 4, NetCore.ToHost, B("legit")));
        Assert.Contains(net.Host.Got, g => g.Origin == 4);
    }

    [Fact]
    public void Lossy_duplicating_reordering_network_still_connects()
    {
        for (int seed = 0; seed < 25; seed++)
        {
            var net = new SimNet { DropRate = 0.4, DupRate = 0.3, Shuffle = true };
            net.Rng.Next(seed);
            net.Channels.Add(Ch);
            net.Add(1);
            for (int i = 2; i <= 6; i++) net.Add(i);
            net.Pump(400, 0.25f);         // 100 s of simulated time
            int peers = net.Host.Core.PeerCount(net.Host);
            // With 40% loss and 10 greetings, a client can (rarely) stay unconnected; most must not.
            Assert.True(peers >= 4, $"seed {seed}: only {peers}/5 connected");
            Assert.All(net.Nodes.Values.Where(n => !n.IsServer && n.Core.HostGreeted), n => Assert.Single(n.Ready));
            Assert.True(net.Host.Ready.Count == peers && net.Host.Ready.Distinct().Count() == peers);
        }
    }

    [Fact]
    public void A_lost_reply_is_recovered_by_the_next_greeting()
    {
        var net = new SimNet();
        net.Channels.Add(Ch);
        net.Add(1);
        var c = net.Add(2);
        net.Host.Core.Tick(net.Host);                       // greeting #1 queued
        net.Pump(1);                                         // client greets back...
        net.Queue.Clear();                                   // ...and that reply is lost
        Assert.True(c.Core.HostGreeted);
        Assert.Equal(0, net.Host.Core.PeerCount(net.Host));
        net.Pump(60, 0.5f);                                  // greeting #2 a few seconds later
        Assert.Equal(1, net.Host.Core.PeerCount(net.Host));
        Assert.Single(c.Ready);
    }

    [Fact]
    public void A_reused_id_must_greet_again_even_if_the_leave_was_missed()
    {
        var net = Session(2);
        Assert.True(net.Host.Core.IsPeer(3));
        // Player 3 vanishes without a leave callback; a vanilla player later gets id 3.
        net.Nodes.Remove(3);
        net.Pump(1);
        Assert.False(net.Host.Core.IsPeer(3));
        var v = net.Add(3, vanilla: true);
        net.Host.Core.Send(net.Host, Ch, NetCore.ToAll, B("for API peers"));
        net.Pump();
        Assert.All(v.Raw, p => Assert.Equal(NetCore.THello, p[5]));

        // And with the leave callback but a rejoin by an API player, it reconnects cleanly.
        net.Remove(3);
        var again = net.Add(3);
        net.Pump();
        Assert.True(net.Host.Core.IsPeer(3));
        Assert.Single(again.Ready);
    }

    [Fact]
    public void Nothing_moves_where_mod_messages_are_not_allowed()
    {
        var net = Session(2);
        foreach (var n in net.Nodes.Values) n.Allowed = false;
        Assert.Equal(NetCore.SendResult.NotAllowed, net.Nodes[2].Core.Send(net.Nodes[2], Ch, NetCore.ToAll, B("x")));
        // Inbound mod traffic is still swallowed (the game never sees it) but not delivered.
        Assert.True(net.Host.Core.Receive(net.Host, 2, NetCore.Build(NetCore.TData, Ch, 2, NetCore.ToAll, B("x"))));
        Assert.Empty(net.Host.Got);
        // No greetings go out either.
        var late = net.Add(4);
        net.Pump();
        Assert.Empty(late.Raw);
    }

    [Fact]
    public void Unknown_channels_are_dropped_not_delivered()
    {
        var net = Session(1);
        net.Nodes[2].Core.Send(net.Nodes[2], NetCore.Fnv("nobody.listens"), NetCore.ToHost, B("x"));
        net.Pump();
        Assert.Empty(net.Host.Got);
        Assert.True(net.Host.Core.Dropped > 0);
    }

    [Fact]
    public void Reset_forgets_everyone()
    {
        var net = Session(2);
        net.Host.Core.Reset();
        net.Nodes[2].Core.Reset();
        Assert.Equal(0, net.Host.Core.PeerCount(net.Host));
        Assert.False(net.Nodes[2].Core.HostGreeted);
        Assert.Equal(NetCore.SendResult.NotConnected, net.Nodes[2].Core.Send(net.Nodes[2], Ch, NetCore.ToAll, B("x")));
    }

    [Fact]
    public void A_throwing_transport_does_not_escape()
    {
        var net = Session(1);
        var bad = new ThrowingTransport();
        net.Host.Core.PlayerJoined(bad, 5);
        net.Host.Core.Tick(bad);                            // greeting throws inside the transport
        Assert.True(net.Host.Core.Receive(bad, 5, NetCore.Build(NetCore.THello, 0, 5, NetCore.ToHost, null)));
    }

    sealed class ThrowingTransport : INetTransport
    {
        public bool IsServer => true;
        public int LocalId => 1;
        public IReadOnlyList<int> Players => new[] { 1, 5 };
        public void SendToPlayer(int id, byte[] packet) => throw new InvalidOperationException("boom");
        public void SendToServer(byte[] packet) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Channel_ids_are_stable_and_spread()
    {
        Assert.Equal(NetCore.Fnv("hellomod.chat"), NetCore.Fnv("hellomod.chat"));
        var ids = Enumerable.Range(0, 20000).Select(i => NetCore.Fnv("mod" + i + ".sync")).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(2166136261u, NetCore.Fnv(""));
        Assert.Equal(NetCore.Fnv(""), NetCore.Fnv(null));
    }
}
