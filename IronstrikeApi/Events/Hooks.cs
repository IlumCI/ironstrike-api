using System;
using System.Collections.Generic;
using Fusion;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace IronstrikeApi.Events;

// Where GameEvents come from. Two sources, chosen per event for reliability:
//   - polling from GM.Update (scenes, runs, levels): cannot be inlined away, works on every peer;
//   - Harmony hooks on the game's own methods (encounters, deaths, players): each logs PATCH LIVE
//     on its first hit, because an IL2CPP method the compiler inlined patches cleanly and never fires.
[HarmonyPatch]
internal static class Hooks
{
    static readonly HashSet<string> live = new();

    internal static void Live(string name)
    {
        if (live.Add(name)) Plugin.Log.LogInfo($"PATCH LIVE: {name}");
    }

    internal static IReadOnlyCollection<string> LiveHooks => live;

    // ------------------------------------------------------------------ polling

    static int sceneIndex = int.MinValue;
    static bool inRun;
    static int levelInRun;
    static RunOutcome? outcome;
    static int lastEncounterStarted = -1, lastEncounterCompleted = -1;

    internal static SceneChange Scene { get; private set; }
    internal static bool InRun => inRun;
    internal static int LevelInRun => levelInRun;

    internal static void Tick()
    {
        var s = SceneManager.GetActiveScene();
        if (s.buildIndex == sceneIndex) return;
        sceneIndex = s.buildIndex;

        var change = new SceneChange
        {
            BuildIndex = s.buildIndex,
            Name = s.name,
            Level = Enum.IsDefined(typeof(LevelSceneNum), s.buildIndex) ? (LevelSceneNum)s.buildIndex : null,
        };
        Scene = change;
        GameEvents.RaiseScene(change);

        // A run is the time spent in levels between two visits to the haven. Read off the scene, so it
        // works the same on the host and on clients.
        bool level = change.Level != null && !change.IsHaven;
        if (level && !inRun)
        {
            inRun = true;
            levelInRun = 0;
            outcome = null;
            GameEvents.RaiseRunStarted();
        }
        else if (!level && inRun) EndRun();

        if (level)
        {
            levelInRun++;
            lastEncounterStarted = lastEncounterCompleted = -1;
            GameEvents.RaiseLevel(levelInRun);
        }
    }

    static void EndRun()
    {
        if (!inRun) return;
        inRun = false;
        GameEvents.RaiseRunEnded(outcome ?? RunOutcome.Ended);
        outcome = null;
    }

    // ------------------------------------------------------------------ run outcome

    // The game's own win/lose, on every peer. The run itself ends when the haven loads; this only
    // records how. (Fired once per peer even though the host also calls these for itself.)
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkGameMaster), nameof(NetworkGameMaster.WinRun_Synced))]
    static void Won() { Live("NetworkGameMaster.WinRun_Synced"); outcome ??= RunOutcome.Won; }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkGameMaster), nameof(NetworkGameMaster.LoseRun_Synced))]
    static void Lost() { Live("NetworkGameMaster.LoseRun_Synced"); outcome ??= RunOutcome.Lost; }

    // ------------------------------------------------------------------ encounters

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkGameMaster), nameof(NetworkGameMaster.TriggerEncounter_Synced))]
    static void EncounterTriggered(int encounterIndex)
    {
        Live("NetworkGameMaster.TriggerEncounter_Synced");
        if (encounterIndex == lastEncounterStarted) return;
        lastEncounterStarted = encounterIndex;
        GameEvents.RaiseEncounterStarted(encounterIndex);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkGameMaster), nameof(NetworkGameMaster.CompleteEncounter_Synced))]
    static void EncounterDone(NetworkGameMaster __instance, int reward1, int reward2, int reward3, int reward4)
    {
        Live("NetworkGameMaster.CompleteEncounter_Synced");
        int i = lastEncounterStarted;
        try { i = __instance.currentEncounterIndex; } catch (Exception) { }
        if (i == lastEncounterCompleted) return;
        lastEncounterCompleted = i;
        GameEvents.RaiseEncounterCompleted(i);
    }

    // ------------------------------------------------------------------ deaths

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SkillManager), nameof(SkillManager.OnFigherDeath_Local))]   // sic: the game's spelling
    static void Died(Fighter f)
    {
        Live("SkillManager.OnFigherDeath_Local");
        if (f != null) GameEvents.RaiseDied(f);
    }

    // ------------------------------------------------------------------ players

    static bool MainRunner(NetworkRunner r)
    {
        var main = GM.instance?.NetLifecycle?._runner;
        return r != null && main != null && r.Pointer == main.Pointer;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkLogic), nameof(NetworkLogic.OnPlayerJoined))]
    static void Joined(NetworkRunner runner, PlayerRef player)
    {
        Live("NetworkLogic.OnPlayerJoined");
        if (!MainRunner(runner)) return;     // e.g. Ironstrike Servers' browse runner
        GameEvents.RaisePlayerJoined(player);
        ModNet.OnPlayerJoined(runner, player);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkLogic), nameof(NetworkLogic.OnPlayerLeft))]
    static void Left(NetworkRunner runner, PlayerRef player)
    {
        Live("NetworkLogic.OnPlayerLeft");
        if (!MainRunner(runner)) return;
        GameEvents.RaisePlayerLeft(player);
        ModNet.OnPlayerLeft(player);
    }
}
