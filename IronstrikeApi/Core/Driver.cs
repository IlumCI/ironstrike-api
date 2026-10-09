using System;
using HarmonyLib;
using UnityEngine;

namespace IronstrikeApi.Core;

// GM.Update is a Unity message called from native code, so IL2CPP cannot inline it away: the one
// reliable per-frame hook in this game. Everything the API does on its own schedule starts here.
[HarmonyPatch]
internal static class Driver
{
    static bool started, hotkeysOff;
    static float nextSlow, firstUpdateAt = -1f;
    static bool autoOpened;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GM), nameof(GM.Update))]
    static void Update()
    {
        if (!started)
        {
            started = true;
            Plugin.Log.LogInfo("HOOK CONFIRMED: GM.Update fired.");
            Safety.Announce();
            try { Mods.CheckRequirements(); } catch (Exception e) { Plugin.Log.LogError($"mod check: {e}"); }
            ModApi.Ready = true;
        }

        Step(Events.Hooks.Tick, "scene tracking");
        Step(Events.SessionHooks.Tick, "session tracking");
        Step(ModNet.Tick, "mod messages");
        GameEvents.RaiseUpdate();
        Step(Ui.Window.Tick, "windows");

        float now = Time.realtimeSinceStartup;
        if (now >= nextSlow)
        {
            nextSlow = now + 1f;
            try
            {
                Ui.Panel.Build();
                Safety.LockPublicButtons();
                Ui.MainMenu.Tick();
            }
            catch (Exception e)
            {
                nextSlow = now + 10f;
                Diag.Error($"menu upkeep: {e}");
            }
        }

        if (Plugin.C.EnableHotkeys.Value && !hotkeysOff) Hotkeys();
        MaybeAutoOpen(now);
        SelfTest.Tick(now);
        StressTest.Tick(now);
        Step(IconDump.Tick, "icon dump");
        Step(() => Content.ContentTest.Tick(now), "content test");
        Step(() => Content.SpellProbe.Tick(now), "spell probe");
        Step(Content.RuneTree.Tick, "rune grid");
        Step(Content.SpellEffects.Fade.Tick, "spell effects");
        Step(Content.SpellEffects.Later.Tick, "delayed spell actions");
    }

    static readonly System.Collections.Generic.HashSet<string> failedSteps = new();

    // One error per kind of failure: these run every frame.
    static void Step(Action a, string what)
    {
        try { a(); }
        catch (Exception e) { if (failedSteps.Add(what + e.GetType().Name)) Diag.Error($"{what}: {e}"); }
    }

    static void MaybeAutoOpen(float now)
    {
        float after = Plugin.C.AutoOpenAfter.Value;
        if (autoOpened || after <= 0f) return;
        if (firstUpdateAt < 0f) firstUpdateAt = now;
        if (now - firstUpdateAt < after || !Ui.Panel.Ready) return;
        autoOpened = true;
        Plugin.Log.LogInfo($"auto-opening the Mods window after {after}s");
        Ui.ModsWindow.Open();
    }

    static void Hotkeys()
    {
        try
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) Ui.ModsWindow.Toggle();
        }
        catch (Exception e)
        {
            hotkeysOff = true;     // legacy-input style failures: the build is Input System only
            Plugin.Log.LogWarning($"hotkeys off ({e.GetType().Name}); use the MODS button.");
        }
    }
}
