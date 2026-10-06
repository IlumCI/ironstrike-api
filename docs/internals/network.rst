.. _internals-network:

*******************
Networking and bans
*******************

Topology (verified)
===================

* Photon Fusion 2 in **Host** mode. The host spawns everything and simulates the bots. There is no
  host migration: when the host leaves, the session ends.
* Every session start, the haven's included, goes through ``NetworkRunner.StartGame``. The main menu
  reaches it through ``NetworkLifecycle.FadeToStartGame``; the haven and solo runs are ``Single``
  sessions.
* Fusion's ``GameMode`` is 1-based in this build: ``Single`` = 1, ``Shared`` = 2, ``Server`` = 3,
  ``Host`` = 4, ``Client`` = 5, ``AutoHostOrClient`` = 6. Use the names.
* ``NetworkLogic.OnConnectRequest`` is the host's admission check.
* Session properties are one-letter keys (``o``, ``t``, ``v``, ``a``, ``afk``, ``k``, ``r``,
  ``public``). Fusion allows at most **ten** custom properties per session; more fails the start
  with ``InvalidArguments``.
* ``StartGameArgs.CustomLobbyName`` and ``ConnectionToken`` are never set by the game. Ironstrike
  Servers uses them to keep modded servers in their own lobby and to check joiners' mods.
* Fusion's reliable data channel (``SendReliableDataToServer``, ``SendReliableDataToPlayer``) is
  unused by the game; its receive callback is empty. :cs:type:`~IronstrikeApi.ModNet` uses it.

Trust
=====

Of about 45 RPCs, only 7 are restricted to the state authority and 2 to the input authority. The
rest accept calls from anyone, including skill grants, healing and damage with a caller-supplied
amount. Clients also report their own health. The game cannot defend itself against a modified
client, which is exactly why the developer asks modders for restraint, and why this API enforces it.

Changing RPC signatures, ``[Networked]`` properties or the input struct breaks compatibility with
everyone else (Fusion's weaver hashes them). Do not.

Bans and telemetry (verified by audit and capture)
==================================================

* Ban lists are plain text files on the developer's site, fetched over HTTP, and enforced on the
  client by display name. A ban needs a person to add a name.
* The game uploads nothing: no ``UnityWebRequest.Post``, no upload handlers; a live capture saw
  only the developer's site and DNS. The Unity Analytics and Services libraries ship but are never
  contacted.
* The developer's debug menu guard (``pleaseDontHackImAPoorDevWithAFamilyToSupport``) shows the
  developer's note and ends the run; nothing is reported anywhere.

So the risk to a modded player is being seen in a public game. With public play locked, there is
none.
