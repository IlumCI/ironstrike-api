.. _glossary:

********
Glossary
********

.. glossary::

   API peer
      Another player in the session who runs the Mod API and has completed
      :cs:type:`~IronstrikeApi.ModNet`'s greeting.

   BepInEx
      The mod loader. IRONSTRIKE needs BepInEx 6, IL2CPP edition, build be.788.

   Fighter
      The game's class for anything that fights: players and bots.

   Gameplay change
      Anything that changes how the game plays for anyone: stats, damage, status effects, skills,
      bots. Allowed only in solo, private matches and modded servers.

   Harmony
      The library mods use to patch the game's methods (prefixes run before a method, postfixes
      after).

   Haven
      The hub between runs. A ``Single`` session, like a solo run.

   Host
      The player whose machine runs the session. Clients connect to the host only.

   IL2CPP
      Unity's ahead-of-time compiler. The game's code is native machine code; mods see it through
      interop assemblies.

   Interop assemblies
      C# assemblies BepInEx generates from the game's metadata, in ``BepInEx/interop``. They have
      every class and signature, but no method bodies.

   Ironstrike
      A weakspot that appears on an enemy; hitting it deals bonus damage. ``WeakspotRange`` and
      ``Weapon.WeakspotBaseInterval`` control how often they appear.

   Mod set
      The installed plugins and their versions. :cs:prop:`~IronstrikeApi.Mods.Hash` fingerprints it.

   Modded server
      A server from Ironstrike Servers: listed in its own lobby, joinable only with the same mod
      set.

   Play context
      What kind of game is running: :cs:type:`~IronstrikeApi.PlayContext`.

   Private Match
      The game's own invite-by-code multiplayer.

   Run
      The time between leaving the haven and coming back: a sequence of levels, each with
      encounters.

   Session
      A Photon Fusion session. The haven, solo runs, private matches and modded servers are all
      sessions.
