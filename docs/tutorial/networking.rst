.. _tut-networking:

************************
Talking to other players
************************

When everyone in a private game or modded server runs your mod, the copies can talk to each other.
:cs:type:`~IronstrikeApi.ModNet` gives you named channels over the game's own connection, the same
Photon Fusion session the game uses. Nothing else is involved: no servers of ours, no extra ports.

.. code-block:: csharp

   var chat = ModNet.Channel("hellomod.chat");
   chat.Received += m => Log.LogInfo($"player #{m.Sender.PlayerId} says {m.Text}");

   ModNet.PeerReady += p => chat.SendTo(p, "hello!");

Channels
========

A channel is identified by its name, so the same name on two machines is the same channel. Prefix it
with your mod's name; two mods using the same name share a channel.

* :cs:meth:`~IronstrikeApi.NetChannel.Broadcast` sends to every other player with the API.
* :cs:meth:`~IronstrikeApi.NetChannel.SendTo` sends to one player.
* :cs:meth:`~IronstrikeApi.NetChannel.SendToHost` sends to the host.

Payloads are bytes; the ``string`` overloads send UTF-8 text. Serialize anything else however you
like: ``BitConverter``, ``System.Text.Json``, your own format.

How it travels
==============

The game is host-authoritative: every client is connected to the host and to nobody else. ModNet
follows that. A client's message goes to the host, which delivers it or relays it on. On the host,
:cs:prop:`~IronstrikeApi.NetMessage.Sender` is the connection the message actually came in on, so it
cannot be forged by the sender.

When a player connects, the host greets them. A player with the API greets back, and from then on
they are a *peer*: :cs:event:`~IronstrikeApi.ModNet.PeerReady` fires, and messages flow. A player
without the API receives one small greeting, which the game ignores, and never anything else.

Limits
======

* Off in public games, along with every other gameplay change.
* :cs:field:`~IronstrikeApi.ModNet.MaxPayload` bytes per message (16 KiB) and
  :cs:field:`~IronstrikeApi.ModNet.MaxPerSecond` messages per second per channel. Over that,
  messages are dropped with a warning. For game state, send changes, not whole snapshots every
  frame.
* Delivery is reliable and ordered per connection, as Fusion's reliable data is.
* Payloads are never logged. Do not put Steam names or join codes in your own log lines either.
