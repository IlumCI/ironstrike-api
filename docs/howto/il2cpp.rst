.. _howto-il2cpp:

***************************
IL2CPP and Harmony pitfalls
***************************

IRONSTRIKE is compiled with IL2CPP: the game's C# was turned into C++ and then machine code. BepInEx
generates *interop* assemblies that look like the original classes and call into that machine code.
Harmony patches on IL2CPP replace native functions. Most of what works in a normal Unity mod works
here, with the exceptions below. Every one of them was hit while building the mods this API comes
from.

Patching
========

**Inlined methods patch cleanly and never fire.**
   No error, no effect. ``GM.InitScene`` and ``GM.isSameTeam`` are inlined. Always log the first hit
   of a patch and check for it; the API does this for its own hooks (``PATCH LIVE``).

**Inlined does not mean gone.**
   ``GM.isSameTeam`` cannot be patched, but you can still call it: only its call sites were inlined.

**Unity message methods are safe.**
   ``GM.Update`` is called by the engine from native code and is never inlined. That is why the
   API's :cs:event:`~IronstrikeApi.GameEvents.Update` is built on it.

**Overloads need the full signature.**
   Patching ``Projectile.SetTypes(Fighter, SpellType)`` with a patch method that only declared
   ``(Projectile __instance, Fighter parent)`` hard-crashed the game on every spell projectile.
   Managed Harmony forgives a missing trailing parameter; the native detour does not. Name the
   overload's parameter types in ``[HarmonyPatch]`` and declare them all.

**ref and out parameters of game methods are broken.**
   ``ref __result`` is fine.

**Constructors** are unreliable to patch. **Transpilers** do not work at all. **Generic methods**
are patched per instantiation. **Burst-compiled code** cannot be patched.

**Value types passed by pointer.**
   ``StartGameArgs`` is a struct the game passes by pointer. Whether a prefix sees that memory or a
   copy depends on the interop. Ironstrike Servers edits a copy and re-issues the call itself.

**Reading a Nullable<T> from inside such a struct throws.**
   ``args.PlayerCount`` dies in ``Il2CppSystem.ValueType..ctor``. Setting one with
   ``new Il2CppSystem.Nullable<int>(4)`` works.

Objects and delegates
=====================

**Keep Il2Cpp delegates alive.**
   A ``DelegateSupport.ConvertDelegate<UnityAction>(...)`` result handed to Unity must also be
   referenced from managed code, or the garbage collector frees its trampoline and the next click
   crashes. The API's window does this for you.

**onClick.RemoveAllListeners() leaves editor-wired listeners.**
   A cloned button keeps calling its original handler. Replace the whole event:
   ``button.onClick = new Button.ButtonClickedEvent();``.

**Object.Destroy is deferred to the end of the frame.** A just-cloned hierarchy has not run
``Start()`` either. Find template objects by child names, not by component checks.

**Resources.FindObjectsOfTypeAll<T>() returns prefab assets too.** Filter with
``x.gameObject.scene.IsValid()``.

**Il2Cpp collections are not .NET collections.**
   ``foreach`` works on ``Il2CppSystem.Collections.Generic.List<T>`` and ``Dictionary``, but not on
   an ``Il2CppSystem`` ``IEnumerable<T>``: walk its enumerator by hand.

**Private fields and methods are public in the interop.**
   ``AIHivemind.instance.bots`` is private in the game and accessible to you.

The game's UI
=============

**I2 Localize components overwrite text.** Destroy them on any label you change, and set the
text after the destroy has happened.

**The legacy UnityEngine.Input throws.** The game uses the Input System only:
``UnityEngine.InputSystem.Keyboard.current.f8Key.wasPressedThisFrame``.

**New objects must be on the UI layer.** The game's raycasters, the VR one included, filter by
layer, and a new ``GameObject`` starts on layer 0, where it never gets clicks.

**A world-space canvas kept across scenes stops taking clicks.** Rebuild windows per scene.

**Some engine methods are stripped.** ``ColorUtility.ToHtmlStringRGBA`` fails with "Method
unstripping failed". :cs:meth:`~IronstrikeApi.Ui.Kit.Tag` does the same by hand.

**The game's font lacks some glyphs**, such as ``·``. Text stops at the first missing glyph.

**Injecting rows into the real Options menu breaks its layout.** Copy the window instead; that is
what :cs:type:`~IronstrikeApi.Ui.Window` does.
