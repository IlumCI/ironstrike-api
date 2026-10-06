using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using IronstrikeApi.Gameplay;
using UnityEngine;

namespace IronstrikeApi.Core;

// [09 Debug] SelfTest: drives the game far enough to see each hook fire, with no input. Solo only:
// it waits for the haven, opens the Mods window, spawns a dummy, starts a solo run, hurts the bots,
// then leaves. Results go to the log as "self-test: ..." lines with counts only.
internal static class SelfTest
{
    static readonly Dictionary<string, int> seen = new();
    static int step;
    static float at, statCalls;
    static IDisposable statMod;

    static void Count(string what)
    {
        seen.TryGetValue(what, out int n);
        seen[what] = n + 1;
    }

    internal static void Register()
    {
        if (!Plugin.C.SelfTest.Value) return;
        GameEvents.SceneChanged += s => Count("SceneChanged");
        GameEvents.SessionStarted += s => Count("SessionStarted:" + s.Context);
        GameEvents.SessionEnded += s => Count("SessionEnded");
        GameEvents.RunStarted += () => Count("RunStarted");
        GameEvents.RunEnded += o => Count("RunEnded:" + o);
        GameEvents.LevelStarted += n => Count("LevelStarted");
        GameEvents.EncounterStarted += i => Count("EncounterStarted");
        GameEvents.EncounterCompleted += i => Count("EncounterCompleted");
        GameEvents.PlayerJoined += p => Count("PlayerJoined");
        GameEvents.PlayerLeft += p => Count("PlayerLeft");
        GameEvents.Damage += e => Count("Damage");
        GameEvents.FighterDied += f => Count("FighterDied");
        GameEvents.ProjectileSpawned += e => Count("ProjectileSpawned");
        Plugin.Log.LogWarning("self-test is ON ([09 Debug] SelfTest); it will start a solo run by itself.");
        step = 1;
    }

    static void Report(string when)
    {
        Plugin.Log.LogMessage($"self-test [{when}]: events " +
            (seen.Count == 0 ? "none" : string.Join(", ", seen.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))));
        Plugin.Log.LogMessage($"self-test [{when}]: live hooks {string.Join(", ", Events.Hooks.LiveHooks)}");
        Plugin.Log.LogMessage($"self-test [{when}]: context {Safety.Context}, scene {Game.Scene}, inRun {Game.InRun}, " +
                              $"players {Players.Count}, bots {Bots.Count}, move-speed reads {statCalls}");
    }

    internal static void Tick(float now)
    {
        if (step == 0 || now < at) return;
        try { Step(now); }
        catch (Exception e) { Plugin.Log.LogError($"self-test step {step}: {e}"); step = 0; }
    }

    static void Step(float now)
    {
        switch (step)
        {
            case 1:     // wait for the haven and its main menu; the haven session may wait for a click
                DismissModal();
                if (Game.Scene == null || !Game.Scene.IsHaven || Ui.MainMenu.Find() == null) { at = now + 2f; return; }
                if (Safety.Context != PlayContext.Solo && ++waits < 10) { at = now + 2f; return; }
                waits = 0;
                at = now + 5f; step = 2; return;

            case 2:
                Plugin.Log.LogMessage($"self-test: in the haven; mods {Mods.All.Count}, mod set {Mods.Hash}");
                try
                {
                    var modes = NetworkProjectConfig.Global?.Network?.ReliableDataTransferModes;
                    Plugin.Log.LogMessage($"self-test: Fusion ReliableDataTransferModes = {modes}");
                }
                catch (Exception e) { Plugin.Log.LogWarning($"self-test: could not read ReliableDataTransferModes: {e.Message}"); }
                statMod = Stats.ModifyLocal(SkillCalcType.MoveSpeed, v => { statCalls++; return v; });
                Ui.ModsWindow.Open();
                Plugin.Log.LogMessage("self-test: SHOT mods-installed");
                at = now + 15f; step = 3; return;

            case 3:
                Ui.ModsWindow.Show(ModApi.Guid);
                Plugin.Log.LogMessage("self-test: SHOT mods-api");
                at = now + 15f; step = 4; return;

            case 4:
                Ui.Window.Current.TabIndex = 1;
                Ui.Window.Current.Refresh();
                Plugin.Log.LogMessage("self-test: SHOT mods-session");
                at = now + 15f; step = 5; return;

            case 5:
                Ui.Window.Current?.Close();
                var mm = Ui.MainMenu.Find();
                if (mm == null) { Plugin.Log.LogWarning("self-test: no main menu to start a solo session from"); step = 99; return; }
                Plugin.Log.LogMessage("self-test: starting a solo session (MainMenuUI.PressSolo)");
                mm.PressSolo();
                waits = 0;
                at = now + 10f; step = 6; return;

            case 6:     // the solo haven session
                if (Safety.Context != PlayContext.Solo || !Game.IsHost) { at = now + 5f; if (++waits > 24) { Plugin.Log.LogWarning("self-test: no solo session"); step = 99; } return; }
                Plugin.Log.LogMessage($"self-test: spawn dummy -> {Bots.SpawnDummy()}");
                at = now + 8f; step = 7; return;

            case 7:
                Report("solo haven");
                // What the haven's portal leads to: the next level of the (solo) run.
                var ngm = GM.instance?.NetGameMaster;
                Plugin.Log.LogMessage("self-test: starting the run (NetworkGameMaster.ProgressToNextLevelInSequence)");
                ngm?.ProgressToNextLevelInSequence();
                waits = 0;
                at = now + 15f; step = 8; return;

            case 8:
                if (!Game.InRun) { at = now + 10f; if (++waits > 18) { Plugin.Log.LogWarning("self-test: the run never started"); step = 99; } return; }
                Report("level start");
                at = now + 20f; step = 12; return;

            case 12:    // what walking into the first fight would do
                Plugin.Log.LogMessage("self-test: triggering encounter 0 (NetworkGameMaster.TriggerEncounter_Synced)");
                GM.instance?.NetGameMaster?.TriggerEncounter_Synced(0);
                at = now + 25f; step = 9; return;

            case 9:
                Plugin.Log.LogMessage($"self-test: hurting {Bots.Count} bot(s) -> {Bots.HurtAll()}");
                at = now + 5f; step = 10; return;

            case 10:
                Plugin.Log.LogMessage($"self-test: hurting {Bots.Count} bot(s) again -> {Bots.HurtAll()}; despawning -> {Bots.DespawnAll()}");
                at = now + 15f; step = 11; return;

            case 11:
                Report("in run");
                Plugin.Log.LogMessage("self-test: leaving the run (runner shutdown)");
                var r = Game.Runner;
                if (r != null && r.IsRunning) r.Shutdown(true, ShutdownReason.Ok, false);
                at = now + 60f; step = 99; return;

            default:
                statMod?.Dispose();
                Report("done");
                Plugin.Log.LogMessage("self-test: done. Set [09 Debug] SelfTest = false.");
                step = 0; return;
        }
    }

    static int waits;

    // The first start shows the game's "play safely" notice, and the haven session waits for its OK.
    // Confirm() is what that OK button calls.
    static bool dismissed;

    static void DismissModal()
    {
        if (dismissed) return;
        var m = Modal.instance;
        var d = m?.currentData;
        if (d == null || d.buttonType != Modal.ButtonType.Okay) return;
        Plugin.Log.LogMessage($"self-test: pressing OK on the '{d.title}' notice");
        dismissed = true;
        m.Confirm();
    }
}
