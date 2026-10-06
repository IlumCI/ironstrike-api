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
    public const string Version = "0.1.0";

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
        Log.LogInfo($"{ModApi.Name} v{ModApi.Version} loaded.");
    }
}

internal sealed class Cfg
{
    public readonly ConfigEntry<bool> EnableHotkeys, MenuButton;
    public readonly ConfigEntry<float> AutoOpenAfter;
    public readonly ConfigEntry<bool> SelfTest, LogEvents;

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
        LogEvents = f.Bind("09 Debug", "LogEvents", false,
            "Debug aid: log every API event as it fires (no names, codes or ids).");
    }
}
