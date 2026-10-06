.. _howto-damage:

*******************
Custom damage rules
*******************

Every hit in the game goes through the static ``Fighter.CalculateDamage(HitInfo)``. The API raises
:cs:event:`~IronstrikeApi.GameEvents.Damage` from it with a
:cs:type:`~IronstrikeApi.Gameplay.DamageEvent` you can read and change.

.. code-block:: csharp

   GameEvents.Damage += e =>
   {
       // Weakspot (ironstrike) hits deal double.
       if (e.Hit.hitWeakspot != null) e.Amount *= 2f;

       // Nobody hurts the local player with a status effect.
       if (e.OnLocalPlayer && e.Type == HitInfo.HitType.StatusEffect) e.Cancel();
   };

What is in a hit
================

:cs:prop:`~IronstrikeApi.Gameplay.DamageEvent.Hit` is the game's own ``HitInfo``:

``attackingFighter``, ``hitFighter``
   Who hit whom (also :cs:prop:`~IronstrikeApi.Gameplay.DamageEvent.Attacker` and
   :cs:prop:`~IronstrikeApi.Gameplay.DamageEvent.Victim`).
``hitType``
   ``BotMelee``, ``GoodMelee``, ``OkayMelee``, ``BadMelee``, ``ZeroMelee`` (the quality of a player's
   swing), ``RangedProjectile``, ``Spell``, ``StatusEffect``, ``Other``.
``hitWeakspot``
   The weakspot (ironstrike) that was hit, or null.
``projectile``
   The projectile, for ranged hits.
``prospectiveDamage``
   The game's own figure before skills; :cs:prop:`~IronstrikeApi.Gameplay.DamageEvent.Original` is
   the final one.

Several mods
============

Handlers run one after another on the same event object, each seeing the amount the previous one
left. :cs:meth:`~IronstrikeApi.Gameplay.DamageEvent.Cancel` wins over any later change.

Where it runs
=============

The game resolves a hit on the machine with authority over it, usually the host. A rule that must
apply to everyone belongs on the host; a rule only about your own hits works anywhere your hits are
calculated. Test both as host and as client.

The event object is reused for the next hit, so read what you need inside the handler and do not
keep it.
