.. _howto-bots:

*********************************
Bots, status effects and loadouts
*********************************

These helpers are host-side and gameplay-gated: they return ``false`` and do nothing in a public
game, or on a client.

Bots
====

:cs:prop:`~IronstrikeApi.Gameplay.Bots.All` lists every live bot's ``Fighter``.

.. code-block:: csharp

   foreach (var bot in Bots.All)
       bot.MaxHealth *= 2f;          // check Safety.GameplayAllowed first: this is a direct write

:cs:meth:`~IronstrikeApi.Gameplay.Bots.DespawnAll` removes them the way the game itself does, through
``NetworkGameMaster.DespawnBot``. (The game also has ``GM.DespawnAllBots``; in the shipped game it
does nothing.) :cs:meth:`~IronstrikeApi.Gameplay.Bots.HurtAll` and
:cs:meth:`~IronstrikeApi.Gameplay.Bots.SpawnDummy` call the developer's own debug helpers.

Bots belong to the faction ``EnemyBots``. ``Faction`` is ``Uninitialized``, ``LocalPlayer``,
``Allies`` or ``EnemyBots``.

Status effects
==============

Prefer a status effect to a stat hack when the game has one: the AI and every other system already
know how to react to it.

.. code-block:: csharp

   var me = Players.LocalFighter;
   Status.Give(me, StatusType.Haste, seconds: 10f);
   Status.Give(me, StatusType.Invisibility, seconds: 5f);

Effects include ``Barrier``, ``Blinded``, ``Marked``, ``Poison``, ``Slowed``, ``Jinx``, ``Ironskin``,
``Rust``, ``Vulnerability``, ``MissileShield``, ``Acid``, ``Stunned``, ``Invisibility``, ``Fortune``,
``Haste``, ``Levitation`` and ``Mettle``. What ``amount`` means differs per effect.

.. note::

   An early version of the trainer made the player invisible by denying the ``Targetable`` flag.
   The bots, left with nothing to target, froze. The Invisibility status works properly.

Skills and weapon sets
======================

.. code-block:: csharp

   Loadout.GiveSkill(SkillType.RapidFire, level: 3);

   foreach (var s in Loadout.Skills.Where(s => !s.hidden))
       Log.LogInfo($"{s.skillType} ({s.skillClass})");

   var set = Loadout.WeaponSets.First(s => s.tier == WeaponSet.Tier.Legendary);
   Loadout.GiveWeaponSet(set);

Skill levels run 1 to 5, and 6 to 10 for enhanced skills.
