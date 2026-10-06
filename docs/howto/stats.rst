.. _howto-stats:

**************
Stat modifiers
**************

Every stat a skill or status effect can change is read through one function,
``Fighter.CalcSkillAndStatusEffectValue(SkillCalcType)``. The API puts one hook on it and runs every
mod's modifiers in order, so mods that change the same stat stack instead of overwriting each other.

The stats
=========

.. list-table::
   :header-rows: 1
   :widths: 10 40 50

   * - #
     - ``SkillCalcType``
     - Notes
   * - 0
     - ``OutgoingDamageBase``
     -
   * - 1
     - ``IncomingDamagePercent``
     -
   * - 2
     - ``OutgoingDamagePercent``
     -
   * - 3
     - ``OutgoingDamageFlat``
     -
   * - 4
     - ``IncomingDamageFlat``
     -
   * - 5
     - ``WeakspotRange``
     - how often ironstrikes (weakspots) appear
   * - 6, 7, 8
     - ``Jumps``, ``JumpVelocity``, ``JumpHorizontal``
     -
   * - 9
     - ``MoveSpeed``
     -
   * - 10
     - ``AmmoReloadTime``
     -
   * - 11, 12
     - ``OutgoingArmorDamagePercent``, ``IncomingArmorDamagePercent``
     -
   * - 13, 14
     - ``TotalIncomingDamagePercent``, ``TotalOutgoingDamagePercent``
     -
   * - 15
     - ``VisibilityPercent``
     - never seen being read; use the Invisibility status instead
   * - 16
     - ``ArrowGravity``
     -
   * - 17
     - ``HeavyBlockGuardDamage``
     -
   * - 18, 19
     - ``DashDistance``, ``DashCooldown``
     -
   * - 20 -- 26
     - ``SpellDiscount``, ``ManaRegenRate``, ``ManaMaxThresh``, ``SpellDurationPercent``,
       ``SpellDamagePercent``, ``SpellCooldownPercent``, ``ManaRegenFlat``
     -
   * - 27, 28
     - ``OutgoingArmorDamageFlat``, ``AmmoRegenRate``
     -

Multipliers or percentages?
===========================

**Verified:** in the hook, the local player's ``MoveSpeed`` arrives as **0** when no skill or status
effect changes it. It is a bonus, not a multiplier, so ``v * 2`` does nothing at all; use
:cs:meth:`~IronstrikeApi.Gameplay.Stats.Scale`, or add to it.

The shipped game is compiled with IL2CPP, which keeps the shape of every method but not its body.
So whether ``MoveSpeed`` is a multiplier (1.0 = normal) or a bonus percentage (0 = normal) cannot be
read from the binary. :cs:meth:`~IronstrikeApi.Gameplay.Stats.Scale` handles both: it multiplies,
and floors the result at ``factor - 1``, so a factor of 1.5 gives at least +0.5 even where the
game's value was 0.

Just the local player, or everyone
==================================

:cs:meth:`~IronstrikeApi.Gameplay.Stats.ModifyLocal` only touches your own fighter. For anything
else, :cs:meth:`~IronstrikeApi.Gameplay.Stats.Modify` gets the fighter:

.. code-block:: csharp

   // Bots move at half speed.
   Stats.Modify(SkillCalcType.MoveSpeed, (fighter, stat, v) =>
       fighter.faction == Faction.EnemyBots ? v * 0.5f : v);

Bots are simulated on the host, so a change to bots only matters on the host's machine.

Order and removal
=================

Modifiers run in ascending ``order`` (default 0), then in the order they were added. Both methods
return an ``IDisposable``; dispose it to remove the modifier:

.. code-block:: csharp

   IDisposable haste = Stats.ModifyLocal(SkillCalcType.MoveSpeed, v => v * 2f);
   ...
   haste.Dispose();

A modifier that throws five times is switched off, and the log names your mod.
