using System;
using System.Linq;
using System.Reflection;
using System.Text;
using IronstrikeApi.Core;
using IronstrikeApi.Gameplay;
using UnityEngine;

namespace IronstrikeApi.Content;

// [09 Debug] SpellProbe: drives custom spells through the game with no input. Registers a test
// school (runes of Fire) with a fighter-targeted spell and a self-centred area spell, then in a solo
// session: caster loadout, the school, checks (HasSpell, GetSpell, cooldown/mana/range), the rune
// grid copy, a cast that goes the whole way through the wand (self spell), the rune-path start, and
// in a fight the fighter spell's synced event against a bot. Log lines "spell-probe: ...", the rune
// grid and spell list in BepInEx/spell-probe.txt, screenshots in BepInEx/debug-shots.
internal static class SpellProbe
{
    static int step = -1, waits;
    static float at;
    static readonly StringBuilder dump = new();
    static int zaps, novas, novaHits, currentFails;
    static System.Collections.Generic.List<string> queue = new();
    static System.Collections.Generic.Dictionary<IntPtr, float> watched = new();
    static string current = "";

    internal static void Register()
    {
        if (!Plugin.C.SpellProbe.Value) return;
        ModContent.Spell("apitest.zap", s =>
        {
            s.Name = "Test Zap";
            s.Icon = Icons.Library("spell_overclock");
            s.Category = SpellCategory.MinorAttack;
            s.Targeting = SpellTargetingType.SingleEnemy;
            s.LooksLike = SpellType.LightningBolt;
            s.Cooldown = new[] { 9f }; s.ManaCost = new[] { 15f, 14f, 13f }; s.Range = new[] { 50f };
            s.Describe = l => $"Zaps an enemy for {20 + 5 * l} damage.";
            s.Amount = (c, t) => 20 + 5 * c.Level;
            s.OnCast = (c, cast) =>
            {
                zaps++;
                SpellFx.Bolt(cast.Origin, cast.Target is not null ? cast.Target.transform.position + Vector3.up : cast.Point, new Color(0.6f, 0.8f, 1f));
                if (cast.Target is not null) cast.Damage(cast.Target, cast.Amount);
            };
        });
        ModContent.Spell("apitest.nova", s =>
        {
            s.Name = "Test Nova";
            s.Icon = Icons.Library("spell_singularity");
            s.Category = SpellCategory.MajorAttack;
            s.Targeting = SpellTargetingType.Self;
            s.LooksLike = SpellType.FrostNova;
            s.Cooldown = new[] { 30f }; s.ManaCost = new[] { 40f }; s.Range = new[] { 8f };
            s.OnCast = (c, cast) =>
            {
                novas++;
                var p = cast.Caster.transform.position;
                SpellFx.Ring(p, 8f, new Color(0.7f, 0.4f, 1f));
                SpellFx.Flash(p, new Color(0.7f, 0.4f, 1f), 3f);
                foreach (var e in cast.EnemiesNear(p, 8f)) { novaHits++; cast.Damage(e, 30f); cast.Status(e, StatusType.Slowed, 3f, 0.5f); }
            };
        });
        ModContent.School("apitest.tempest", s =>
        {
            s.Name = "Test Tempest";
            s.Icon = Icons.Library("school_time");
            s.Runes = SkillType.FireMagic;
            s.Spells.Add("apitest.zap");
            s.Spells.Add("apitest.nova");
            s.OfferWeight = 50f;
        });
        Plugin.Log.LogWarning("spell-probe is ON ([09 Debug] SpellProbe); it will start a solo session by itself.");
        step = 0;
    }

    static void Log(string s) { Plugin.Log.LogMessage("spell-probe: " + s); dump.AppendLine(s); }

    internal static void Tick(float now)
    {
        if (step < 0 || now < at) return;
        try { Step(now); }
        catch (Exception e) { Plugin.Log.LogError($"spell-probe: step {step} threw: {e}"); Flush(); step = 99; at = now + 1f; }
    }

    static void Flush()
    {
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spell-probe.txt"), dump.ToString()); }
        catch (Exception) { }
    }

    static string N(UnityEngine.Object o) => o == null ? "-" : o.name;

    static CasterWeapon Caster()
    {
        var me = Players.LocalFighter;
        return me?.MainWeapon?.CasterStuff ?? me?.OffWeapon?.CasterStuff;
    }

    static void Step(float now)
    {
        var zap = ModContent.GetSpell("apitest.zap");
        var nova = ModContent.GetSpell("apitest.nova");
        switch (step)
        {
            case 0:
                var m = Modal.instance; var d = m?.currentData;
                if (d != null && d.buttonType == Modal.ButtonType.Okay) m.Confirm();
                if (Game.Scene == null || !Game.Scene.IsHaven || Ui.MainMenu.Find() == null || SpellManager.instance == null) { at = now + 2f; return; }
                var sd = SpellDatabase.GetSpellDict(SpellManager.instance.spellDatabase, false);
                var kd = SkillDatabase.GetSkillDict(SkillManager.instance.skillDatabase, false);
                Log($"ids: school {ModContent.GetSchool("apitest.tempest").Id}, zap {zap.Id}, nova {nova.Id}; in spell dict {sd.ContainsKey(zap.Type)}/{sd.ContainsKey(nova.Type)}, " +
                    $"school in skill dict {kd.ContainsKey(ModContent.GetSchool("apitest.tempest").Type)}");
                foreach (var r in RuneTree.Report) Log("rune grid: " + r);
                Ui.MainMenu.Find()?.PressSolo();
                waits = 0; step = 1; at = now + 10f; return;

            case 1:
                if ((Safety.Context != PlayContext.Solo || Players.LocalFighter == null) && ++waits < 30) { at = now + 3f; return; }
                var set = Loadout.WeaponSets.FirstOrDefault(s => s != null && s.fighterClass == SkillClass.Caster && s.tier == WeaponSet.Tier.Common)
                          ?? Loadout.WeaponSets.FirstOrDefault(s => s != null && s.fighterClass == SkillClass.Caster);
                Log($"caster set: {(set != null ? set.setName : "none")} -> {Loadout.GiveWeaponSet(set)}");
                step = 2; at = now + 6f; return;

            case 2:
                var me = Players.LocalFighter;
                Log($"GiveSchool(tempest, 2) -> {ModContent.GiveSchool(null, "apitest.tempest", 2)}; HasSpell zap {me.HasSpell(zap.Type)}, nova {me.HasSpell(nova.Type)}, storm {me.HasSpell(SpellType.LightningBolt)}");
                foreach (var cs in new[] { zap, nova })
                {
                    var sp = me.GetSpell(cs.Type);
                    string vals;
                    try { vals = $"cd {sp.CooldownDuration}, mana {sp.ManaCost}, range {sp.Range}"; } catch (Exception e) { vals = "values threw " + e.Message; }
                    Log($"GetSpell({cs.Key}) -> {(sp is null ? "null" : sp.GetIl2CppType().Name)} level {sp?.level}, name '{sp?.GetName()}', text '{sp?.GetDescription()}', {vals}, " +
                        $"target {sp?.targetingType}, preview {N(sp?.SpellPreviewPrefab)}, ind {N(sp?.TargetingIndicator)}");
                }
                var school = me.skills[ModContent.GetSchool("apitest.tempest").Type];
                Log($"school card: '{school.GetFancyName()}' — '{school.GetDescription()}'");
                step = 3; at = now + 4f; return;

            case 3:
                DumpTree();
                step = 4; at = now + 2f; return;

            case 4:     // the rune path: what drawing the nova's runes ends in
                var gms = GM.instance.GetComponentInChildren<GestureMagicSystem>(true);
                var node = gms.AllSpells.ToArray().FirstOrDefault(g => g != null && g.Spell == nova.Type);
                Log($"rune node for nova: {N(node?.gameObject)} unlocked {node?.Unlocked}");
                var cw0 = Caster();
                var done = typeof(GestureMagicSystem).GetMethod("DoSpellCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (node is not null && done != null)
                {
                    try { done.Invoke(gms, new object[] { node }); Log($"DoSpellCompleted(nova node) -> casting {cw0?.CastingSpellLocal?.spellType} stage {cw0?.CastingSpellLocal?.stage}"); }
                    catch (Exception e) { Log($"DoSpellCompleted threw {e.InnerException?.Message ?? e.Message}"); }
                }
                else Log($"DoSpellCompleted reachable: {done != null}");
                step = 5; at = now + 2f; return;

            case 5:     // the whole cast through the wand: the nova, aimed at yourself
                var cw = Caster();
                if (cw.CastingSpellLocal?.spellType != nova.Type) cw.LocalPlayerStartCasting(nova.Type);
                cw.SetCastingSpellProgress(1f);
                Log($"mana before {cw.Mana}");
                Shots.Take("spell-nova-ready");
                step = 6; at = now + 1.5f; return;

            case 6:
                var c6 = Caster();
                c6.TriggerSpell();
                Log($"TriggerSpell: stage {c6.CastingSpellLocal?.stage}, mana {c6.Mana}");
                step = 7; at = now + 0.5f; return;

            case 7:
                Shots.Take("spell-nova-cast");
                step = 8; at = now + 2f; return;

            case 8:
                Log($"nova casts {novas} (hits {novaHits}); cooled down {Players.LocalFighter.GetSpell(nova.Type)?.IsCooledDown()}; starting the run");
                GM.instance?.NetGameMaster?.ProgressToNextLevelInSequence();
                waits = 0; step = 9; at = now + 12f; return;

            case 9:
                if (!Game.InRun && ++waits < 40) { at = now + 5f; return; }
                // A new level brings a new fighter: the loadout and school again.
                var set2 = Loadout.WeaponSets.FirstOrDefault(s => s != null && s.fighterClass == SkillClass.Caster);
                if (Players.LocalFighter?.fighterClass != SkillClass.Caster) Loadout.GiveWeaponSet(set2);
                step = 10; at = now + 4f; return;

            case 10:
                foreach (var sc in ModContent.Schools) if (ModContent.SkillLevel(null, sc.Key) == 0) ModContent.GiveSchool(null, sc.Key, 2);
                GM.instance?.NetGameMaster?.TriggerEncounter_Synced(0);
                queue = ModContent.Spells.Where(x => x.School != null).Select(x => x.Key).ToList();
                step = 11; at = now + 20f; return;

            case 11:    // every custom spell's synced event, as the network would deliver it
            {
                var me11 = Players.LocalFighter;
                if (me11 is not null) me11.invulnerable = true;
                if (queue.Count == 0) { step = 99; return; }
                var key = queue[0]; queue.RemoveAt(0);
                var sp = ModContent.GetSpell(key);
                var bot = Bots.All.Where(f => f is not null && !f.dead).OrderBy(f => (Body.Feet(f) - Body.Feet(me11)).sqrMagnitude).FirstOrDefault();
                var c11 = Caster();
                if (bot is null || c11 is null) { Log($"{key}: no bot ({Bots.Count}) or wand; respawning the encounter"); GM.instance?.NetGameMaster?.TriggerEncounter_Synced(0); queue.Insert(0, key); at = now + 15f; return; }
                watched = Bots.All.Where(f => f is not null).ToDictionary(f => f.Pointer, f => f.CurrentHealth);
                var origin = c11.WandTip.position;
                var dir = (Body.Center(bot) - origin).normalized;
                var gs = me11.GetSpell(sp.Type);
                int fails = SpellHost.Failures(key);
                switch (sp.Targeting)
                {
                    case SpellTargetingType.Ray: c11.OnFireRaySpell_Synced(origin, dir, sp.Type); break;
                    case SpellTargetingType.GroundCircle: c11.OnFireAreaSpell_Synced(Body.Feet(bot), sp.Type); break;
                    case SpellTargetingType.Self:
                    case SpellTargetingType.SingleAlly: c11.OnFireSpellOnFighter_Synced(me11, sp.Type, origin, gs.GetOnShootAmount(c11, me11, origin), false); break;
                    default: c11.OnFireSpellOnFighter_Synced(bot, sp.Type, origin, gs.GetOnShootAmount(c11, bot, origin), false); break;
                }
                current = key; currentFails = fails;
                step = 12; at = now + 0.6f; return;
            }

            case 12:
                Shots.Take("cast-" + current.Replace('.', '-'));
                step = 13; at = now + 3.5f; return;

            case 13:
            {
                var changes = Bots.All.Where(f => f is not null && watched.ContainsKey(f.Pointer) && Math.Abs(watched[f.Pointer] - f.CurrentHealth) > 0.01f)
                    .Select(f => $"{watched[f.Pointer]:0}->{f.CurrentHealth:0}{(f.dead ? " dead" : "")}").ToList();
                int poisoned = Bots.All.Count(f => f is not null && f.HasStatus(StatusType.Poison));
                int stunned = Bots.All.Count(f => f is not null && f.HasStatus(StatusType.Stunned));
                Log($"cast {current}: bots hurt {changes.Count} [{string.Join(", ", changes)}], poisoned {poisoned}, stunned {stunned}, " +
                    $"casts {SpellHost.Casts(current)}, new failures {SpellHost.Failures(current) - currentFails}, my health {Players.LocalFighter?.CurrentHealth:0}");
                step = 11; at = now + 1.5f; return;
            }

            default:
                Log("done. Set [09 Debug] SpellProbe = false.");
                Flush();
                step = -1; return;
        }
    }

    static void DumpTree()
    {
        var g = GM.instance.GetComponentInChildren<GestureMagicSystem>(true);
        Log($"rune grid: {g.AllSpells?.Count} nodes");
        foreach (var gs in g.AllSpells)
        {
            if (gs == null) continue;
            bool custom = ModContent.SpellById((int)gs.Spell) != null || gs.transform.parent.name.Contains("apitest");
            if (!custom && gs.Spell != SpellType.StaticField && gs.Spell != SpellType.LightningBolt) continue;
            var leads = gs.LeadsToSpells == null ? "" : string.Join(",", gs.LeadsToSpells.ToArray().Select(x => x == null ? "-" : $"{x.Spell}@{x.transform.parent.name}"));
            Log($"  node {N(gs.gameObject)} under {N(gs.transform.parent?.gameObject)} spell {gs.Spell} unlocked {gs.Unlocked} strokes {gs.Gestures?.Count} leads [{leads}] labels {gs.SpellLabels?.Count}");
        }
    }
}
