.. _tut-events:

******
Events
******

:cs:type:`~IronstrikeApi.GameEvents` turns the game into C# events. They all fire on the main
thread, and they fire in every kind of game, public ones included: watching is always allowed.

.. list-table::
   :header-rows: 1
   :widths: 35 65

   * - Event
     - When
   * - :cs:event:`~IronstrikeApi.GameEvents.Update`
     - every frame
   * - :cs:event:`~IronstrikeApi.GameEvents.SceneChanged`
     - a scene loaded: the haven, a level, the tutorial
   * - :cs:event:`~IronstrikeApi.GameEvents.SessionStarted`, :cs:event:`~IronstrikeApi.GameEvents.SessionEnded`
     - a network session began or ended (the haven is a session too)
   * - :cs:event:`~IronstrikeApi.GameEvents.RunStarted`, :cs:event:`~IronstrikeApi.GameEvents.RunEnded`
     - the team left the haven; came back (won, lost, or otherwise)
   * - :cs:event:`~IronstrikeApi.GameEvents.LevelStarted`
     - each level of a run
   * - :cs:event:`~IronstrikeApi.GameEvents.EncounterStarted`, :cs:event:`~IronstrikeApi.GameEvents.EncounterCompleted`
     - a fight began; it was cleared
   * - :cs:event:`~IronstrikeApi.GameEvents.PlayerJoined`, :cs:event:`~IronstrikeApi.GameEvents.PlayerLeft`
     - someone connected or left (reliable on the host)
   * - :cs:event:`~IronstrikeApi.GameEvents.Damage`
     - a hit is being resolved; you may change it
   * - :cs:event:`~IronstrikeApi.GameEvents.FighterDied`
     - a player or bot died
   * - :cs:event:`~IronstrikeApi.GameEvents.ProjectileSpawned`
     - an arrow, bolt or spell projectile was fired

Counting kills
==============

.. code-block:: csharp

   int kills;

   GameEvents.RunStarted  += () => kills = 0;
   GameEvents.FighterDied += f => { if (f.faction == Faction.EnemyBots) kills++; };
   GameEvents.RunEnded    += outcome => Log.LogInfo($"{outcome}: {kills} kills");

``Fighter`` is the game's own class, used for players and bots alike. Its fields are documented in
:ref:`internals-combat`.

When a handler throws
=====================

A handler that throws does not stop the game or other mods' handlers. The API logs the exception
with your mod's name::

   [Error  :IRONSTRIKE Mod API] Hello Mod: FighterDied handler Plugin.<Load>b__3_1 threw: ...

After five failures the handler is muted for the rest of the session, so a broken per-frame
handler cannot flood the log.

Where events come from
======================

Scenes, runs and levels are read off the active scene every frame, which works the same on every
player's machine. The others come from hooks on the game's own methods. A hook on a method the
compiler inlined installs without error and never fires, so the API logs ``PATCH LIVE: <method>``
the first time each hook fires. If an event never fires for you, check for its line.
