.. _tut-firstmod:

**************
Your first mod
**************

A BepInEx plugin is a class deriving from ``BasePlugin`` with a ``[BepInPlugin]`` attribute.
BepInEx creates it once at startup and calls ``Load()``. Everything a mod does is set up there.

.. code-block:: csharp

   using BepInEx;
   using BepInEx.Unity.IL2CPP;
   using IronstrikeApi;

   [BepInPlugin("com.example.hellomod", "Hello Mod", "1.0.0")]
   [BepInDependency(ModApi.Guid)]
   [ModKind(ModKind.Gameplay)]
   [RequiresApi("0.1.0")]
   public class Plugin : BasePlugin
   {
       public override void Load()
       {
           GameEvents.RunStarted += () => Log.LogInfo("Run started. Good luck!");
       }
   }

Line by line:

``[BepInPlugin(guid, name, version)]``
   The GUID identifies your mod everywhere: config file name, dependencies, the mod-set fingerprint
   multiplayer compares. Use a reverse domain you control, and never change it.

``[BepInDependency(ModApi.Guid)]``
   Tells BepInEx to load the API first, and to refuse to load your mod without it, with a clear
   message in the log.

``[ModKind(ModKind.Gameplay)]``
   Tells players what your mod changes. It is shown in the Mods window and, on a modded server,
   in the listing. Use :cs:field:`~IronstrikeApi.ModKind.Client` for a mod that only changes your
   own screen, :cs:field:`~IronstrikeApi.ModKind.Gameplay` for rules and balance, and
   :cs:field:`~IronstrikeApi.ModKind.Cheat` for anything that makes the game easier.

``[RequiresApi("0.1.0")]``
   The oldest API version your mod works with. A player with an older API gets a log line saying
   so, instead of a ``MissingMethodException`` from deep inside your code.

``GameEvents.RunStarted += ...``
   Subscribing to an event. The handler runs on the game's main thread every time a run starts.

Build it, copy the DLL to ``BepInEx/plugins/``, start a solo run, and look for your line in
``BepInEx/LogOutput.log``.

Changing the game
=================

Reading the game is free. Changing it goes through the API's gameplay helpers, which only apply
where the developer allows it (see :ref:`howto-safety`). A stat modifier is one line:

.. code-block:: csharp

   using IronstrikeApi.Gameplay;

   Stats.ModifyLocal(SkillCalcType.MoveSpeed, v => Stats.Scale(v, 1.25f));

Your character now moves 25% faster in solo and private games, and normally in public ones.
:cs:meth:`~IronstrikeApi.Gameplay.Stats.Scale` exists because the game's code does not reveal whether
a stat is a multiplier or a bonus percentage; it works for both.

The complete Hello Mod
======================

The rest of the tutorial explains each part of this file:

.. literalinclude:: ../../examples/HelloMod/Plugin.cs
   :language: csharp
