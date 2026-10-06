.. _howto-safety:

*****************
Rules for modders
*****************

The developer's request
=======================

IRONSTRIKE is made by one developer. The game's binary contains this note, addressed to modders:

   "Please don't offer cheats that can transfer to public multiplayer games. It ruins the challenge
   for people who don't want cheats on their team. I'm a solo indie developer (and busy taking care
   of a new baby), and I know I can't win an arms race with modders, so I appeal to you personally.
   Please limit cheats to private games with you and your friends."

The game's network code trusts clients a great deal. It could not stop a determined cheater, and the
developer says so. The restraint therefore has to live in the mods. This API puts it in one place,
so that no mod built on it has to get it right alone.

What the API enforces
=====================

Public matchmaking is locked.
   While the API is loaded, the main menu's **Play** and **HOST** buttons (public quick match and
   public hosting) are refused, and so is the matchmaking behind them. There is no setting to turn
   this off. Modded players play solo, in Private Matches, or on modded servers from
   `Ironstrike Servers <https://github.com/IlumCI/ironstrike-servers>`_.

Gameplay changes only apply in private games.
   :cs:prop:`~IronstrikeApi.Safety.Context` says what kind of game is running. The gameplay helpers
   (:cs:type:`~IronstrikeApi.Gameplay.Stats`, damage edits through
   :cs:type:`~IronstrikeApi.Gameplay.DamageEvent`, :cs:type:`~IronstrikeApi.Gameplay.Status`,
   :cs:type:`~IronstrikeApi.Gameplay.Loadout`, :cs:type:`~IronstrikeApi.Gameplay.Bots`,
   :cs:meth:`~IronstrikeApi.Gameplay.Players.Revive`) do nothing in a
   :cs:field:`~IronstrikeApi.PlayContext.Public` game, and log one warning saying so.

Mod messages stay private.
   :cs:type:`~IronstrikeApi.ModNet` is off in public games.

What is left to you
===================

The API cannot see what you do with the game's objects directly. If your mod writes to a
``Fighter``, a ``Projectile`` or a ``GM`` flag itself, check first:

.. code-block:: csharp

   if (!Safety.Check(Log, "giant arrows")) return;
   projectile.collisionRadius *= 3f;

:cs:meth:`~IronstrikeApi.Safety.Check` returns false in public games and logs one warning per
feature.

The other lines this project does not cross
===========================================

* **Currency and cosmetics.** No Shards, Geodes, Essence, Animus or Gems, no unlocks, no prices.
* **The developer's own debug menu.** ``GM.CreateDevMenu`` refuses to open in the shipped game and
  ends the current run if you force it. Build your own window instead (:ref:`tut-windows`).
* **Private data in logs.** ``BepInEx/LogOutput.log`` is the file players attach to bug reports.
  Never log a Steam name, an account id or a join code; :cs:meth:`~IronstrikeApi.ApiLog.Redact` turns
  one into a harmless fingerprint.
* **Save files.** Do not edit them. PlayerPrefs keys are hashed and values obfuscated.
