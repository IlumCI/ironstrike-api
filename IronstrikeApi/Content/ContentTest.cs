using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using IronstrikeApi.Core;
using IronstrikeApi.Gameplay;

namespace IronstrikeApi.Content;

// [09 Debug] ContentTest: registers a test skill in every upgrade category (each with a library icon)
// and drives them through the game with no input: solo session, ModContent.GiveSkill, the real upgrade
// screen (screenshots), a run and a fight, then removal. Logs "content-test: ..." lines with counts.
internal static class ContentTest
{
    static readonly Dictionary<string, int> calls = new();
    static int step = -1, waits;
    static float at;

    static void Count(string what)
    {
        calls.TryGetValue(what, out int n);
        calls[what] = n + 1;
    }

    static readonly (SkillCategory Cat, string Icon, string Name)[] Tests =
    {
        (SkillCategory.Technique, "skill_bloodlust", "Bloodlust"),
        (SkillCategory.Infusion, "skill_vampiric_strike", "Vampiric Strike"),
        (SkillCategory.Agility, "skill_momentum", "Momentum"),
        (SkillCategory.Valor, "skill_executioner", "Executioner"),
        (SkillCategory.Tactics, "skill_hunters_focus", "Hunter's Focus"),
        (SkillCategory.Fortitude, "skill_fortify", "Fortify"),
        (SkillCategory.Mana, "skill_overcharge", "Overcharge"),
        (SkillCategory.Evocation, "skill_frenzy", "Frenzy"),
        (SkillCategory.Enchantment, "skill_second_wind", "Second Wind"),
    };

    internal static void Register()
    {
        if (!Plugin.C.ContentTest.Value) return;
        foreach (var t in Tests)
        {
            string key = "apitest." + t.Icon.Substring(6);
            ModContent.Skill(key, s =>
            {
                s.Name = t.Name;
                s.Category = t.Cat;
                s.Icon = Icons.Library(t.Icon);
                s.OfferWeight = 50f;          // so the test reliably sees them on the cards
                s.Describe = lvl => $"API test skill. +{lvl * 10}% damage dealt.";
                s.OnAdded = c => Count("OnAdded");
                s.OnRemoved = c => Count("OnRemoved");
                s.OnTick = c => Count("OnTick");
                s.OnEncounterStart = c => Count("OnEncounterStart");
                s.OnOutgoingHit = (c, h) => Count("OnOutgoingHit");
                s.OnIncomingHit = (c, h) => Count("OnIncomingHit");
                s.OnFighterDeath = (c, f) => Count("OnFighterDeath");
                s.OutgoingDamageBonus = (c, h) => { Count("OutgoingDamageBonus"); return c.Level * 0.1f; };
                s.IncomingDamageBonus = (c, h) => { Count("IncomingDamageBonus"); return 0f; };
                s.MoveSpeedBonus = c => { Count("MoveSpeedBonus"); return 0f; };
                s.ExtraJumps = c => { Count("ExtraJumps"); return 0f; };
            });
        }
        Plugin.Log.LogWarning("content-test is ON ([09 Debug] ContentTest); it will start a solo run by itself.");
        step = 0;
    }

    static void Log(string s) => Plugin.Log.LogMessage("content-test: " + s);

    static void Calls(string when) =>
        Log($"[{when}] hooks called: " + (calls.Count == 0 ? "none" : string.Join(", ", calls.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))));

    internal static void Tick(float now)
    {
        if (step < 0 || now < at) return;
        try { Step(now); }
        catch (Exception e) { Plugin.Log.LogError($"content-test: step {step} threw: {e}"); step = -1; }
    }

    static void Step(float now)
    {
        switch (step)
        {
            case 0:     // haven + menu; dismiss the first-start notice
                var m = Modal.instance; var d = m?.currentData;
                if (d != null && d.buttonType == Modal.ButtonType.Okay) m.Confirm();
                if (Game.Scene == null || !Game.Scene.IsHaven || Ui.MainMenu.Find() == null || SkillManager.instance == null) { at = now + 2f; return; }
                step = 1; at = now + 3f; return;

            case 1:
                var dict = SkillDatabase.GetSkillDict(SkillManager.instance.skillDatabase, false);
                int filed = ModContent.Skills.Count(s => s.Id != 0 && dict.ContainsKey(s.Type));
                Log($"skill class injected {SkillPatches.Ready}, frozen {ModContent.Frozen}, {filed}/{ModContent.Skills.Count} filed in the game's dictionary");
                Ui.MainMenu.Find()?.PressSolo();
                waits = 0; step = 2; at = now + 10f; return;

            case 2:     // solo session with a local fighter
                if ((Safety.Context != PlayContext.Solo || Players.LocalFighter == null) && ++waits < 30) { at = now + 3f; return; }
                Log($"context {Safety.Context}, content active {ModContent.Active}");
                bool given = ModContent.GiveSkill(null, "apitest.bloodlust", 2);
                Log($"GiveSkill(bloodlust, 2) -> {given}, level now {ModContent.SkillLevel(null, "apitest.bloodlust")}");
                var me = Players.LocalFighter;
                Skill has = null;
                if (me?.skills != null) me.skills.TryGetValue(ModContent.GetSkill("apitest.bloodlust").Type, out has);
                Log($"name '{has?.GetName()}', fancy '{has?.GetFancyName()}', text '{has?.GetDescription()}', icon {(has?.skillSprite != null)}");
                Calls("after give");
                // What a level load does: a full GC, then the game clones a template again.
                Il2CppSystem.GC.Collect();
                Log($"after a full GC: GiveSkill(fortify) -> {ModContent.GiveSkill(null, "apitest.fortify", 1)}, level {ModContent.SkillLevel(null, "apitest.fortify")}");
                step = 3; at = now + 4f; return;

            case 3:     // the real upgrade screen
                SkillManager.instance.ShowSkillChoice(false);
                step = 4; at = now + 5f; return;

            case 4:
                Shots.Take("content-categories");
                step = 5; at = now + 2f; return;

            case 5:
                var ui = SkillManager.instance.skillChooserUI;
                var first = ui?.currentCards != null && ui.currentCards.Count > 0 ? ui.currentCards[0] : null;
                if (first == null) { Log("no category cards to open"); step = 7; return; }
                ui.TransitionToSkills(first);
                step = 6; at = now + 6f; return;

            case 6:
                var cards = SkillManager.instance.skillChooserUI?.currentCards;
                int n = cards?.Count ?? 0;
                Log($"skill cards shown: {n}");
                for (int i = 0; i < n; i++)
                {
                    var c = cards[i];
                    var sp = c.categoryIcon?.sprite;
                    string tr;
                    try { tr = sp != null ? sp.textureRect.ToString() : "-"; } catch (Exception e) { tr = e.GetType().Name; }
                    var rt = c.categoryIcon?.rectTransform;
                    Log($"card {i}: skill {(int)(c.skill?.skillType ?? 0)}, sprite rect {sp?.rect}, textureRect {tr}, ppu {sp?.pixelsPerUnit}, " +
                        $"packed {sp?.packed}, box {rt?.rect.size}, preserveAspect {c.categoryIcon?.preserveAspect}, type {c.categoryIcon?.type}");
                }
                Shots.Take("content-skills");
                step = 7; at = now + 2f; return;

            case 7:
                var chooser = SkillManager.instance.skillChooserUI;
                try { chooser?.Cleanup(); chooser?.Hide(); } catch (Exception) { }
                Log("starting the run and a fight");
                GM.instance?.NetGameMaster?.ProgressToNextLevelInSequence();
                waits = 0; step = 8; at = now + 10f; return;

            case 8:
                if (!Game.InRun && ++waits < 40) { at = now + 5f; return; }
                // A level load gives the player a new fighter; give the skill again in the level.
                if (ModContent.SkillLevel(null, "apitest.bloodlust") == 0) ModContent.GiveSkill(null, "apitest.bloodlust", 3);
                ModContent.GiveSkill(null, "apitest.fortify", 1);
                GM.instance?.NetGameMaster?.TriggerEncounter_Synced(0);
                step = 9; at = now + 25f; return;

            case 9:
                Bots.HurtAll();
                step = 10; at = now + 8f; return;

            case 10:
                Bots.HurtAll();
                Calls("in the fight");
                step = 11; at = now + 5f; return;

            case 11:
                Log($"RemoveSkill -> {ModContent.RemoveSkill(null, "apitest.bloodlust")}, level now {ModContent.SkillLevel(null, "apitest.bloodlust")}");
                Calls("after remove");
                var r = Game.Runner;
                if (r != null && r.IsRunning) r.Shutdown(true, ShutdownReason.Ok, false);
                step = 12; at = now + 30f; return;

            default:
                Log("done. Set [09 Debug] ContentTest = false.");
                step = -1; return;
        }
    }
}
