.. _faq:

**************************
Frequently asked questions
**************************

Why are Play and HOST locked?
=============================

The developer asked modders not to let mods reach public games (:ref:`howto-safety`). The API
locks public matchmaking while it is loaded. Play with friends through Private Match or Ironstrike
Servers. To play publicly, remove your mods.

My mod's stat change does nothing.
==================================

Check :cs:prop:`~IronstrikeApi.Safety.Context` in the Mods window's Session tab: in a public game
gameplay changes are off. Then check the log for ``PATCH LIVE: Fighter.CalcSkillAndStatusEffectValue``.
If it is there, the stat may simply not be read in that situation (``VisibilityPercent`` never is).

An event never fires.
=====================

Look for its ``PATCH LIVE`` line. If it never appears, the game method it hooks was not called, or
was inlined by a game update. Report it (:ref:`bugs`).

Do my friends need the API too?
===============================

If any of your mods uses it, yes: their mod set must match yours to join a modded server, and mod
messages only flow between players who have it.

Can mods without the API still be used?
========================================

Yes. The API is an ordinary BepInEx plugin. Other mods load alongside it, appear in the Mods window
with their settings, and count towards the mod set.

Can I use the API from a mod that does not depend on it?
========================================================

Only by reflection, which breaks silently when names change. Add the dependency instead; it costs
players one extra DLL.

Does the API send anything over the internet?
=============================================

No. :cs:type:`~IronstrikeApi.ModNet` uses the game's own session connection to the other players,
and only when a mod sends something. Nothing goes anywhere else.
