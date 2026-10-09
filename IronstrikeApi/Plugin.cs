using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace IronstrikeApi;

/// <summary>
/// Identity of the API plugin. Dependent mods put
/// <c>[BepInDependency(ModApi.Guid)]</c> on their plugin class so BepInEx loads the API first.
/// </summary>
public static class ModApi
{
    /// <summary>The API's BepInEx plugin GUID.</summary>
    public const string Guid = "eu.euroswarms.ironstrike.api";

    /// <summary>Display name of the API plugin.</summary>
    public const string Name = "IRONSTRIKE Mod API";

    /// <summary>The API version, <c>major.minor.patch</c>. Minor bumps add; major bumps break.</summary>
    public const string Version = "0.2.1";

    /// <summary>
    /// True once the game's main loop is running and every hook has had its chance to install.
    /// Most calls work before this; game objects (players, menus) do not exist yet.
    /// </summary>
    public static bool Ready { get; internal set; }
}

/// <summary>The BepInEx entry point. Mods never need to touch this class.</summary>
[BepInPlugin(ModApi.Guid, ModApi.Name, ModApi.Version)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static Cfg C;
    internal static Harmony Harmony;

    /// <summary>Called by BepInEx. Installs the API's hooks.</summary>
    public override void Load()
    {
        Log = base.Log;
        C = new Cfg(Config);
        Harmony = new Harmony(ModApi.Guid);

        // Each class patches on its own, so one hook the game has inlined or renamed costs that one
        // feature, not the whole API.
        foreach (var t in new[]
                 {
                     typeof(Core.Driver), typeof(Safety), typeof(Events.Hooks), typeof(Events.SessionHooks),
                     typeof(Gameplay.Stats), typeof(Gameplay.DamageHooks), typeof(Gameplay.ProjectileHooks),
                     typeof(ModNet),
                 })
        {
            try { Harmony.PatchAll(t); }
            catch (Exception e) { Log.LogError($"could not install {t.Name}: {e.Message}"); }
        }

        Ui.ModsWindow.Register();
        Core.SelfTest.Register();
        Core.StressTest.Register();
        Content.ContentTest.Register();
        Content.SpellProbe.Register();
        Content.SkillPatches.Install();
        Content.SpellPatches.Install();
        Log.LogInfo($"{ModApi.Name} v{ModApi.Version} loaded.");
    }
}

internal sealed class Cfg
{
    public readonly ConfigEntry<bool> EnableHotkeys, MenuButton;
    public readonly ConfigEntry<float> AutoOpenAfter;
    public readonly ConfigEntry<bool> SelfTest, StressTest, ContentTest, SpellProbe, DumpIcons, LogEvents;

    public Cfg(ConfigFile f)
    {
        EnableHotkeys = f.Bind("01 General", "EnableHotkeys", true, "F8 opens the Mods window.");
        MenuButton = f.Bind("01 General", "MenuButton", true, "Add a MODS button to the main menu's Credits card.");

        AutoOpenAfter = f.Bind("09 Debug", "AutoOpenAfterSeconds", 0f, new ConfigDescription(
            "Debug aid: open the Mods window this many seconds after the game starts. 0 disables.",
            null, ModSettings.Hidden));
        SelfTest = f.Bind("09 Debug", "SelfTest", false, new ConfigDescription(
            "Debug aid: exercise the API's hooks without input (solo run, bots, a loopback message).\n"
            + "Turn off afterwards.", null, ModSettings.Hidden));
        StressTest = f.Bind("09 Debug", "StressTest", false, new ConfigDescription(
            "Debug aid: run the in-game edge-case suite (windows, controls, hooks, a scene change, damage).\n"
            + "Logs 'stress: PASS/FAIL' per case. Turn off afterwards.", null, ModSettings.Hidden));
        ContentTest = f.Bind("09 Debug", "ContentTest", false, new ConfigDescription(
            "Debug aid: register test skills and drive them through the upgrade screen and a fight (solo run).\n"
            + "Writes screenshots to BepInEx/debug-shots. Turn off afterwards.", null, ModSettings.Hidden));
        SpellProbe = f.Bind("09 Debug", "SpellProbe", false, new ConfigDescription(
            "Debug aid: dump the game's spells and rune tree to BepInEx/spell-probe.txt and cast one from code (solo).",
            null, ModSettings.Hidden));
        DumpIcons = f.Bind("09 Debug", "DumpIcons", false, new ConfigDescription(
            "Debug aid: write every skill and spell icon to BepInEx/debug-shots/icons as PNG.", null, ModSettings.Hidden));
        LogEvents = f.Bind("09 Debug", "LogEvents", false,
            "Debug aid: log every API event as it fires (no names, codes or ids).");
    }
}
