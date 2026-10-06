.. _internals-combat:

***************************
Fighters, skills and combat
***************************

Fighter
=======

``Fighter`` is used for players and bots alike.

Fields: ``CurrentHealth``, ``BaseHealth``, ``MaxHealth``, ``invulnerable`` (the game clears it, so
god modes set it every frame), ``faction``, ``fighterClass`` (``Ranger``, ``Fighter``, ``Caster``),
``skills``, ``MainWeapon``, ``OffWeapon``, ``Weakspots``.

``CalcSkillAndStatusEffectValue(SkillCalcType)`` is the central stat query: every skill- and
status-modifiable stat passes through it (**verified**, not inlined). ``CalcSkillAndStatusEffectFlag``
does the same for flags. Static ``CalculateDamage(HitInfo)`` resolves every hit.

Players are driven by ``NetworkPlayerFighterDriver`` (``DisplayName``, weapons, cosmetics), bots by
``NetworkBotFighterDriver``; both have a ``fighter`` field.

Skills and status effects
=========================

``SkillManager.instance``: ``GetSkills()``, ``GiveSkillToFighter(SkillType, Fighter, int level)``,
``RemoveSkillFromFighter``. Levels 1 to 5, enhanced 6 to 10.

``StatusManager.instance``: ``GiveStatusEffectToFighter(StatusType, Fighter givenBy, Fighter target,
int endTick, float amount)``, ``RemoveStatusEffectFromFighter``. Times are in simulation ticks:
``TimeHelper.CurrentTick``, ``TimeHelper.GetTicks(seconds)``. ``CurrentTick`` throws outside a
running simulation.

Weapons
=======

``Weapon``: ``baseDamage``, ``WeakspotBaseInterval`` (how often it creates ironstrikes; smaller is
more often), ``isShield``, ``isRangedWeapon``, ``isCasterWeapon``. Melee reach is collider geometry;
scaling the weapon's transform makes it reach further, visibly.

``WeaponSet``: ``setName``, ``tier`` (``Common``, ``Rare``, ``Legendary``), ``fighterClass``,
``mainHand``, ``offHand``. All sets are in ``ArmoryManager.instance.weaponSetDatabase.weaponSets``.

Projectiles
===========

``Projectile``: ``speed``, ``gravity``, ``damage``, ``maxLifeTime``, ``collisionRadius``,
``explodeRadius``, ``percentSeeking``, ``forceSeeking``, ``target``, ``owner``. Spawned through two
``SetTypes`` overloads (see :ref:`howto-il2cpp` for why both need full signatures). Homing
projectiles are steered every tick; changing their speed or gravity makes them overshoot.

Bots
====

``AIHivemind.instance.bots`` lists the ``AIBot`` components; ``bot.nbfd.fighter`` is the
fighter. ``NetworkGameMaster.DespawnBot(driver)`` removes one (host only; it changes the list, so
copy it first). Denying ``SkillFlagType.Targetable`` leaves the AI with nothing to score and freezes
every bot.

Runs and encounters
===================

``NetworkGameMaster`` runs the level sequence: ``StartLevel_Synced``, ``ReadyEncounter``,
``TriggerEncounter_Synced(int)``, ``CompleteEncounter_Synced(...)``, ``WinRun_Synced``,
``LoseRun_Synced``, ``currentEncounterIndex``, ``spawnedBots``. Levels are scenes:
``LevelSceneNum`` gives their build indices (``Haven`` = 1, ``Meadow`` = 2 ... ``Snowstorm`` = 14).
