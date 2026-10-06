.. _howto-porting:

*************************
Moving a mod onto the API
*************************

A mod written straight against BepInEx and Harmony usually carries its own copies of things the API
now provides. Moving onto the API removes them and gets the mod into the Mods window.

.. list-table::
   :header-rows: 1
   :widths: 45 55

   * - In your mod today
     - With the API
   * - a ``GM.Update`` postfix as the main loop
     - :cs:event:`~IronstrikeApi.GameEvents.Update`
   * - a postfix on ``Fighter.CalcSkillAndStatusEffectValue``
     - :cs:meth:`~IronstrikeApi.Gameplay.Stats.Modify` / :cs:meth:`~IronstrikeApi.Gameplay.Stats.ModifyLocal`
   * - a postfix on ``Fighter.CalculateDamage``
     - :cs:event:`~IronstrikeApi.GameEvents.Damage`
   * - postfixes on both ``Projectile.SetTypes`` overloads
     - :cs:event:`~IronstrikeApi.GameEvents.ProjectileSpawned`
   * - blocking ``PressPlay``/``PressHost`` and matchmaking
     - nothing: the API does it
   * - "am I in a private game?" bookkeeping
     - :cs:prop:`~IronstrikeApi.Safety.GameplayAllowed`, :cs:meth:`~IronstrikeApi.Safety.Check`
   * - a cloned Options window for settings
     - nothing (the Mods window), or :cs:type:`~IronstrikeApi.Ui.Window`
   * - a menu button copied from HOST
     - :cs:meth:`~IronstrikeApi.Ui.MainMenu.AddButton`
   * - reflection to find another mod
     - :cs:meth:`~IronstrikeApi.Mods.Get`, then a public API on that mod
   * - hashing the plugin list for multiplayer checks
     - :cs:prop:`~IronstrikeApi.Mods.Hash`

Steps
=====

#. Add ``[BepInDependency(ModApi.Guid)]``, ``[ModKind(...)]`` and ``[RequiresApi("0.1.0")]`` to the
   plugin class, and a reference to ``IronstrikeApi.dll`` (``Private="false"``).
#. Replace one hook at a time, and check the log for its ``PATCH LIVE`` line before moving on.
#. Delete your own public-play lockout last, once the API's is in place.
#. Ship with a note that the API is now required.

Keep your config keys: players' settings carry over, and the Mods window picks them up.
