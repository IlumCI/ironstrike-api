.. _reference-index:

###############
 API reference
###############

This part describes every public type in ``IronstrikeApi.dll``. It is generated from the assembly
and its documentation comments, so it always matches the release it was built with. The
:ref:`tutorial-index` is the better place to start.

All types are in five namespaces:

``IronstrikeApi``
   The core: the mod registry (:cs:type:`~IronstrikeApi.Mods`), the play context
   (:cs:type:`~IronstrikeApi.Safety`), game events (:cs:type:`~IronstrikeApi.GameEvents`), game
   state (:cs:type:`~IronstrikeApi.Game`), settings (:cs:type:`~IronstrikeApi.ModSettings`) and
   networking (:cs:type:`~IronstrikeApi.ModNet`).

``IronstrikeApi.Gameplay``
   Changing the game: :cs:type:`~IronstrikeApi.Gameplay.Stats`,
   :cs:type:`~IronstrikeApi.Gameplay.DamageEvent`, :cs:type:`~IronstrikeApi.Gameplay.Players`,
   :cs:type:`~IronstrikeApi.Gameplay.Bots`, :cs:type:`~IronstrikeApi.Gameplay.Status`,
   :cs:type:`~IronstrikeApi.Gameplay.Loadout`, :cs:type:`~IronstrikeApi.Gameplay.Body`.

``IronstrikeApi.Content``
   New content: skills (:cs:type:`~IronstrikeApi.Content.CustomSkill`), magic schools
   (:cs:type:`~IronstrikeApi.Content.CustomSchool`) and spells
   (:cs:type:`~IronstrikeApi.Content.CustomSpell`), registered through
   :cs:type:`~IronstrikeApi.ModContent`, and their icons (:cs:type:`~IronstrikeApi.Content.Icons`).

``IronstrikeApi.Ui``
   Windows and the main menu: :cs:type:`~IronstrikeApi.Ui.Window`,
   :cs:type:`~IronstrikeApi.Ui.Page`, :cs:type:`~IronstrikeApi.Ui.Kit`,
   :cs:type:`~IronstrikeApi.Ui.MainMenu`, :cs:type:`~IronstrikeApi.Ui.TextInput`.

Types from the game itself (``Fighter``, ``SkillCalcType``, ``HitInfo``...) are described in
:ref:`internals-index`.

Versioning
==========

The API follows semantic versioning. Within a major version, releases only add: a mod built against
0.1 runs on any later 0.x. Declare the oldest version you need with
:cs:type:`~IronstrikeApi.RequiresApiAttribute`.

.. toctree::
   :maxdepth: 1

   api/index.rst
