.. _internals-gm:

**********************
``GM``, the god object
**********************

``GM`` (source ``GM.cs``) is a ``MonoBehaviour`` singleton holding the game's configuration and a
reference to every other singleton. ``GM.instance`` is null only while the game starts.
:cs:prop:`~IronstrikeApi.Game.Main` returns it.

Singletons
==========

``LocalPlayerFighter``, ``LocalPlayerFD`` (its driver), ``NetLogic`` (``NetworkLogic``, Fusion's
callbacks), ``NetLifecycle`` (``NetworkLifecycle``, sessions), ``NetGameMaster``
(``NetworkGameMaster``, runs and encounters), ``Hivemind`` (``AIHivemind``), ``Events``
(``EventManager``), ``armoryManager``, ``MainCamera``, ``XRRig``, ``tutorial``, ``music``.

Other managers have their own ``instance``: ``SkillManager``, ``StatusManager``, ``ArmoryManager``,
``AIHivemind``, ``Modal``, ``VRKeyboard``.

Static flags
============

The developer's cheat switches, all directly settable and all **verified** working:
``CheatHighDamage``, ``CheatFastRegen``, ``CheatAllIronstrikes``, ``CheatDontSpawnIronstrikes``,
``CheatLowCooldowns``, ``CheatNoHealthbars``, ``CheatNoDamageNumbers``, ``CheatNoStatusEffecs``
(sic). Setting them does not trip the anti-tamper guard. They are gameplay changes: check
:cs:meth:`~IronstrikeApi.Safety.Check` first.

Methods worth knowing
=====================

``GivePlayerWeaponSet(WeaponSet)``, ``SpawnPlayerWeapon``, ``SpawnDummyPlayer()``,
``HurtAllBots()``, ``RevivePlayerFighter(Fighter)``, ``LoadSceneByBuildIndex(int)``,
``FadeToSceneByBuildIndex(int)``, static ``isSameTeam(Faction, Faction)`` (inlined at its call
sites; callable, not patchable).

``GM.DespawnAllBots()`` does nothing in the shipped game. ``GM.CreateDevMenu()`` returns null and
ends the current run: do not call it.

``GM.Update`` is the one per-frame hook that is guaranteed to fire.
