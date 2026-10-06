using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IronstrikeApi;

/// <summary>Where the player is playing, as far as multiplayer is concerned.</summary>
public enum PlayContext
{
    /// <summary>No session yet (the game is still starting up).</summary>
    Offline = 0,
    /// <summary>Alone: the haven, the tutorial, a solo run.</summary>
    Solo = 1,
    /// <summary>A Private Match started from the game's own Private Match card.</summary>
    PrivateMatch = 2,
    /// <summary>A modded server from Ironstrike Servers: everyone in it runs the same mods.</summary>
    ModdedServer = 3,
    /// <summary>Public matchmaking. Gameplay changes are never allowed here.</summary>
    Public = 4,
}

/// <summary>
/// The rule this whole project follows, from the developer's note in the game: mods that change
/// gameplay stay in solo and private games, never in public ones. The API enforces it for every mod
/// built on it, so no mod has to get it right on its own.
/// </summary>
/// <remarks>
/// <para>Public matchmaking (Play, HOST) is locked while the API is loaded. There is no switch.</para>
/// <para>Gameplay-changing API calls (<see cref="Gameplay.Stats"/>, damage edits,
/// <see cref="Gameplay.Status"/>, <see cref="Gameplay.Loadout"/>, <see cref="Gameplay.Bots"/>) do
/// nothing unless <see cref="GameplayAllowed"/> is true. Events still fire everywhere.</para>
/// </remarks>
[HarmonyPatch]
public static class Safety
{
    const string TrainerGuid = "eu.euroswarms.ironstrike.trainer";
    const string ServersGuid = "eu.euroswarms.ironstrike.servers";

    /// <summary>The current play context.</summary>
    public static PlayContext Context { get; internal set; } = PlayContext.Offline;

    /// <summary>
    /// True when gameplay changes are allowed: solo, a private match, or a modded server. False in
    /// public matchmaking.
    /// </summary>
    public static bool GameplayAllowed => Context != PlayContext.Public;

    /// <summary>
    /// For a mod's own gameplay code: true if allowed, otherwise logs one warning naming the mod
    /// and returns false. <c>if (!Safety.Check(Log, "god mode")) return;</c>
    /// </summary>
    /// <param name="log">The calling mod's logger, or null for the API's.</param>
    /// <param name="what">What was refused, for the warning.</param>
    public static bool Check(BepInEx.Logging.ManualLogSource log, string what)
    {
        if (GameplayAllowed) return true;
        ApiLog.WarnOnce(log, "refused:" + what + ":" + Context,
            $"{what} refused: gameplay changes are off in {Context} games.");
        return false;
    }

    // Set by the Private Match card, cleared by the next solo session (the trainer's rule).
    internal static bool privateEntry;

    // ---------------------------------------------------------------- entry tracking

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressPrivateMatch))]
    static void PrivateMatchPressed() => privateEntry = true;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressSolo))]
    static void SoloPressed() => privateEntry = false;

    // ---------------------------------------------------------------- the lockout

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressPlay))]
    static bool BlockPlay() => Refuse("Play (quick match)");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressHost))]
    static bool BlockHost() => Refuse("HOST (public)");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLifecycle), nameof(NetworkLifecycle.StartMatchmaking))]
    static bool BlockMatchmaking() => Refuse("StartMatchmaking");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLifecycle), nameof(NetworkLifecycle.FindOrHostMatchmaking))]
    static bool BlockFindOrHost() => Refuse("FindOrHostMatchmaking");

    static bool Refuse(string what)
    {
        Plugin.Log.LogWarning($"public play is locked while mods are loaded: blocked {what}.");
        return false;
    }

    internal static void Announce() =>
        Plugin.Log.LogMessage("public matchmaking locked: mods stay in solo, private matches and modded servers.");

    // ---------------------------------------------------------------- the buttons

    static readonly HashSet<string> PublicMethods = new() { "PressPlay", "PressHost" };
    static readonly HashSet<IntPtr> relabelled = new();
    static bool? othersLock;

    // The trainer greys and relabels these buttons itself, and Ironstrike Servers turns them into its
    // browser buttons. Either way, leave them alone, or the two would fight every second.
    static bool OthersHandleButtons => othersLock ??= Mods.IsLoaded(TrainerGuid) || Mods.IsLoaded(ServersGuid);

    // Every second, not once: the game toggles these buttons itself.
    internal static void LockPublicButtons()
    {
        if (OthersHandleButtons) return;
        var mm = Ui.MainMenu.Find();
        if (mm == null) return;

        int changed = 0;
        foreach (var b in mm.GetComponentsInChildren<Button>(true))
        {
            if (!WiredToPublic(b)) continue;
            if (b.interactable) { b.interactable = false; changed++; }
            var cg = b.GetComponent<CanvasGroup>() ?? b.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0.35f;
            if (relabelled.Add(b.Pointer)) Relabel(b);
        }
        if (changed > 0) Plugin.Log.LogInfo($"locked {changed} public play button(s)");
    }

    static void Relabel(Button b)
    {
        var labels = Ui.MainMenu.OwnLabels(b);
        if (labels.Count == 0) return;
        TextMeshProUGUI target = labels[0];
        foreach (var t in labels) if (t.text.Length > target.text.Length) target = t;
        Ui.MainMenu.Unlocalize(target);
        target.text = labels.Count == 1 ? "LOCKED" : "Locked while modded";
        target.enableAutoSizing = true;
        target.fontSizeMax = target.fontSize;
        target.fontSizeMin = target.fontSize * 0.5f;
    }

    static bool WiredToPublic(Button b)
    {
        for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            if (PublicMethods.Contains(b.onClick.GetPersistentMethodName(i))) return true;
        return false;
    }
}
