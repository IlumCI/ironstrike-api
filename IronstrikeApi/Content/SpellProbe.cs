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
                DumpGeometry();
                if (System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spell-probe-vfx-only"))) { DumpVfx(); step = 99; return; }
                if (System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spell-probe-fx-gallery")))
                {
                    Ui.MainMenu.Find()?.PressSolo();
                    waits = 0; step = 200; at = now + 10f; return;
                }
                if (System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spell-probe-geometry-only"))) { step = 99; return; }
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
                foreach (var sc in ModContent.Schools) ModContent.GiveSchool(null, sc.Key, 1);
                step = 30; at = now + 3f; return;

            case 30:    // the user's own situation: game schools in the top corners, custom ones beside them
                foreach (var t in new[] { SkillType.FireMagic, SkillType.StormMagic, SkillType.LightMagic, SkillType.DivineMagic }) Loadout.GiveSkill(t, 1);
                step = 32; at = now + 3f; return;

            case 32:
                Log("branches: " + string.Join(", ", ModContent.Schools.Select(sc => $"{sc.Key}->{RuneTree.HostOf(sc.Key)?.ToString() ?? "none"}")));
                if (System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spell-probe-grid"))) { step = 400; at = now + 1f; return; }
                GridAB();
                foreach (var sc in ModContent.Schools.Take(2)) Loadout.GiveSkill(sc.Runes, 1);
                Log($"gave the player {string.Join(", ", ModContent.Schools.Take(2).Select(sc => sc.Runes))}");
                step = 31; at = now + 3f; return;

            case 33:
                step = 31; return;

            case 31:    // ...and moves off a branch when the player takes its game school
                Log("branches now: " + string.Join(", ", ModContent.Schools.Select(sc => $"{sc.Key}->{RuneTree.HostOf(sc.Key)?.ToString() ?? "none"}")));
                var hosts = ModContent.Schools.Select(sc => RuneTree.HostOf(sc.Key)).Where(h => h != null).ToList();
                Log($"distinct {hosts.Distinct().Count() == hosts.Count}, none on an owned school {hosts.All(h => !Players.LocalFighter.skills.ContainsKey(h.Value))}");
                // What the grid itself decides (its unlock pass is private): with only custom schools held,
                // every corner holding one of them must light up.
                var gms31 = GM.instance.GetComponentInChildren<GestureMagicSystem>(true);
                var consider = typeof(GestureMagicSystem).GetMethod("ConsiderEnablingSpells", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                consider?.Invoke(gms31, null);
                foreach (var op in gms31.StartingSpells.ToArray())
                    Log($"opener {op.transform.parent.name}: unlocked {op.Unlocked}, reaches {op.PotentialSpells?.Count} spells " +
                        $"({op.PotentialSpells?.ToArray().Count(x => x != null && ModContent.SpellById((int)x.Spell) != null)} custom)");
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
                foreach (var sc in ModContent.Schools) ModContent.GiveSchool(null, sc.Key, sc.MaxLevel, enhanced: sc.AllowsEnhanced);
                GM.instance?.NetGameMaster?.TriggerEncounter_Synced(0);
                queue = ModContent.Spells.Where(x => x.School != null).Select(x => x.Key).ToList();
                var only = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spell-probe-only");
                if (System.IO.File.Exists(only))
                {
                    var keep = System.IO.File.ReadAllText(only).Split(new[] { ' ', '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                    queue = queue.Where(keep.Contains).ToList();
                }
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
                // Face the bot, so the screenshots show the spell.
                var rig = GM.instance?.XRRig;
                if (rig != null)
                {
                    var look = Vector3.ProjectOnPlane(Body.Feet(bot) - Body.Feet(me11), Vector3.up);
                    if (look.sqrMagnitude > 0.01f) rig.transform.rotation = Quaternion.LookRotation(look.normalized) * Quaternion.Inverse(Quaternion.Euler(0f, Camera.main.transform.localEulerAngles.y, 0f));
                }
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
                Shots.Take("cast-" + current.Replace('.', '-') + "-a");
                step = 14; at = now + 1.4f; return;

            case 14:
                Shots.Take("cast-" + current.Replace('.', '-') + "-b");
                step = 13; at = now + 2.1f; return;

            case 13:
            {
                var changes = Bots.All.Where(f => f is not null && watched.ContainsKey(f.Pointer) && Math.Abs(watched[f.Pointer] - f.CurrentHealth) > 0.01f)
                    .Select(f => $"{watched[f.Pointer]:0}->{f.CurrentHealth:0}{(f.dead ? " dead" : "")}").ToList();
                var alive = new System.Collections.Generic.HashSet<IntPtr>(Bots.All.Where(f => f is not null && !f.dead).Select(f => f.Pointer));
                int killed = watched.Keys.Count(k => !alive.Contains(k));
                int poisoned = Bots.All.Count(f => f is not null && f.HasStatus(StatusType.Poison));
                int stunned = Bots.All.Count(f => f is not null && f.HasStatus(StatusType.Stunned));
                Log($"cast {current}: bots hurt {changes.Count} [{string.Join(", ", changes)}], killed {killed}, poisoned {poisoned}, stunned {stunned}, " +
                    $"level {ModContent.SkillLevel(null, ModContent.GetSpell(current).School.Key)}, name '{Players.LocalFighter?.skills[ModContent.GetSpell(current).School.Type]?.GetFancyName()}', casts {SpellHost.Casts(current)}, new failures {SpellHost.Failures(current) - currentFails}, my health {Players.LocalFighter?.CurrentHealth:0}");
                step = 11; at = now + 1.5f; return;
            }

            // ---- the rune grid, drawn on the flat screen and photographed
            case 400:
                GridStart();
                step = 401; at = now + 2f; return;
            case 401: GridShot("1-grid"); step = 402; at = now + 1.5f; return;
            case 402: GridStep("game corner", topCorner); step = 403; at = now + 1.5f; return;
            case 403: GridShot("2-game-corner-open"); step = 404; at = now + 1.5f; return;
            case 404: GridStep("game selector", gameSelector); step = 405; at = now + 1.5f; return;
            case 405: GridShot("3-game-school-spells"); step = 406; at = now + 1.5f; return;
            case 406: GridStart(); step = 407; at = now + 2f; return;
            case 407: GridStep("custom corner", customCorner); step = 408; at = now + 1.5f; return;
            case 408: GridShot("4-custom-corner-open"); step = 409; at = now + 1.5f; return;
            case 409: GridStep("custom selector", customSelector); step = 410; at = now + 1.5f; return;
            case 410: GridShot("5-custom-school-spells"); step = 411; at = now + 1.5f; return;
            case 411:
                if (shotCam != null) UnityEngine.Object.Destroy(shotCam.gameObject);
                Grid().enabled = true;
                step = 99; at = now + 1f; return;

            case 200:   // the effects gallery: a solo session, a dummy to aim at
                if ((Safety.Context != PlayContext.Solo || Players.LocalFighter == null) && ++waits < 30) { at = now + 3f; return; }
                Bots.SpawnDummy();
                gallery = Gallery();
                step = 201; at = now + 6f; return;

            case 201:
            {
                if (gallery.Count == 0) { step = 99; return; }
                var (name, run, shot) = gallery[0];
                gallery.RemoveAt(0);
                var onlyG = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "spell-probe-only");
                if (System.IO.File.Exists(onlyG) && !System.IO.File.ReadAllText(onlyG).Contains(name)) { at = now; return; }
                try { run(); Log($"fx {name}"); } catch (Exception e) { Log($"fx {name} threw {e.Message}"); }
                pendingShot = name;
                step = 202; at = now + shot; return;
            }

            case 202:
                Shots.Take("fx-" + pendingShot);
                step = 201; at = now + 3.5f; return;

            default:
                Log("done. Set [09 Debug] SpellProbe = false.");
                Flush();
                step = -1; return;
        }
    }

    // What the game has to make effects from: shaders, particle prefabs and their materials.
    static void DumpVfx()
    {
        var sb = new StringBuilder();
        foreach (var n in new[] { "Sprites/Default", "Legacy Shaders/Particles/Additive", "Legacy Shaders/Particles/Alpha Blended", "Particles/Standard Unlit",
                                  "Particles/Standard Surface", "Unlit/Color", "Unlit/Texture", "Unlit/Transparent", "UI/Default", "Standard", "Mobile/Particles/Additive",
                                  "Legacy Shaders/Particles/Additive (Soft)", "Hidden/Internal-Colored", "TextMeshPro/Distance Field" })
            sb.AppendLine($"shader {n}: {(Shader.Find(n) != null)}");
        var shaders = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var r in Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>())
        {
            var m = r.sharedMaterial;
            string sh = m?.shader?.name ?? "-";
            shaders[sh] = shaders.TryGetValue(sh, out var k) ? k + 1 : 1;
        }
        foreach (var kv in shaders.OrderByDescending(k => k.Value)) sb.AppendLine($"particle shader {kv.Key} x{kv.Value}");
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
        {
            if (ps == null || ps.gameObject.scene.IsValid()) continue;
            var root = ps.transform.root;
            if (!seen.Add(root.name)) continue;
            var parts = root.GetComponentsInChildren<ParticleSystem>(true);
            var mats = string.Join("|", root.GetComponentsInChildren<ParticleSystemRenderer>(true).Select(r => $"{N(r.sharedMaterial)}:{r.sharedMaterial?.shader?.name}:{N(r.sharedMaterial?.mainTexture)}").Distinct().Take(6));
            var comps = string.Join(",", root.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).Where(x => x != "Transform"));
            int lines = root.GetComponentsInChildren<LineRenderer>(true).Length, lights = root.GetComponentsInChildren<Light>(true).Length;
            sb.AppendLine($"prefab {root.name}: systems {parts.Length}, lines {lines}, lights {lights}, comps [{comps}], mats {mats}");
        }
        foreach (var lr in Resources.FindObjectsOfTypeAll<LineRenderer>())
            if (lr != null && !lr.gameObject.scene.IsValid() && seen.Add("line:" + lr.transform.root.name))
                sb.AppendLine($"line prefab {lr.transform.root.name}/{lr.name}: mat {N(lr.sharedMaterial)}:{lr.sharedMaterial?.shader?.name}:{N(lr.sharedMaterial?.mainTexture)} width {lr.startWidth}");
        var lb = SpellManager.instance.GetSpell(SpellType.LightningBolt)?.TryCast<LightningBoltSpell>();
        sb.AppendLine($"lightning bolt LineFX: {N(lb?.LineFX)} children {lb?.LineFX?.transform.childCount}");
        foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>().Where(t => t != null && t.width <= 512 && (t.name.ToLower().Contains("glow") || t.name.ToLower().Contains("spark") || t.name.ToLower().Contains("smoke") || t.name.ToLower().Contains("flare") || t.name.ToLower().Contains("lightning") || t.name.ToLower().Contains("circle") || t.name.ToLower().Contains("ring") || t.name.ToLower().Contains("beam") || t.name.ToLower().Contains("particle"))).Take(80))
            sb.AppendLine($"texture {tex.name} {tex.width}x{tex.height}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "vfx-dump.txt"), sb.ToString());
        Log("vfx dump written");
    }

    // Every rune node's strokes as segments in the rune system's own space, for laying out new branches.
    static void DumpGeometry()
    {
        var g = GM.instance.GetComponentInChildren<GestureMagicSystem>(true);
        var sb = new StringBuilder();
        string V(Vector3 v) => $"{v.x:0.###},{v.y:0.###},{v.z:0.###}";
        foreach (var gs in g.AllSpells)
        {
            if (gs == null) continue;
            bool start = g.StartingSpells.Contains(gs);
            var leads = gs.LeadsToSpells == null ? "" : string.Join("|", gs.LeadsToSpells.ToArray().Select(x => x == null ? "-" : x.GetInstanceID().ToString()));
            sb.Append($"node {gs.GetInstanceID()} {gs.transform.parent.name}/{gs.name} spell={gs.Spell} start={start} leads={leads} strokes=");
            foreach (var mg in gs.Gestures)
            {
                var a = g.transform.InverseTransformPoint(mg.Point1.position);
                var b = g.transform.InverseTransformPoint(mg.Point2.position);
                sb.Append($"[{V(a)};{V(b)};swap={mg.PointsShouldBeSwapped};p1parent={mg.Point1.parent?.name}]");
            }
            sb.AppendLine();
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "rune-geometry.txt"), sb.ToString());
        Log($"rune geometry written ({g.AllSpells.Count} nodes)");
    }

    static System.Collections.Generic.List<(string, Action, float)> gallery = new();
    static string pendingShot = "";

    // Each effect in front of the camera; the float is when to take the screenshot.
    static System.Collections.Generic.List<(string, Action, float)> Gallery()
    {
        var me = Players.LocalFighter;
        var cam = Camera.main.transform;
        var fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        var ground = Body.Feet(me);
        var p = new Vector3(cam.position.x, ground.y, cam.position.z) + fwd * 8f;
        var right = Vector3.Cross(Vector3.up, fwd);
        var dummy = Bots.All.FirstOrDefault() ?? Players.All.Select(h => h.Fighter).FirstOrDefault(f => f is not null && f.Pointer != me.Pointer);
        Color storm = new(0.55f, 0.75f, 1f), blood = new(1f, 0.25f, 0.3f), mind = new(0.8f, 0.55f, 1f), fire = new(1f, 0.6f, 0.25f), plague = new(0.6f, 0.9f, 0.2f), death = new(0.55f, 1f, 0.6f);
        return new()
        {
            ("pillars10", () => { for (int i = 0; i < 10; i++) { float a = i * 0.628f; SpellFx.Pillar(p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 6f, death, 20f, 2.2f); } }, 1f),
            ("clouds10", () => { for (int i = 0; i < 10; i++) { float a = i * 0.628f; SpellFx.Cloud(p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 6f, 1.5f, 0.4f, new Color(0.06f, 0.14f, 0.08f), 2.5f); } }, 1.5f),
            ("rings", () => { SpellFx.Ring(p, 30f, death, 3f); SpellFx.Ring(p, 18f, death, 3f); SpellFx.Ring(p, 3f, death, 3f); SpellFx.Shockwave(p, 30f, death, 1.2f); }, 0.8f),
            ("self-motes", () => SpellFx.Aura(me, death, 2.5f, AuraStyle.Motes), 1f),
            ("bolt", () => SpellFx.Bolt(p - right * 4f + Vector3.up * 1.5f, p + right * 3f + Vector3.up, storm), 0.12f),
            ("sky-bolt", () => SpellFx.Bolt(p + Vector3.up * 25f, p, storm, 0.15f, 0.4f), 0.1f),
            ("beam", () => SpellFx.Beam(p + Vector3.up * 60f, p, new Color(0.85f, 0.95f, 1f), 1.4f, 1f), 0.35f),
            ("explosion", () => SpellFx.Explosion(p, fire, 1f), 0.15f),
            ("telegraph", () => SpellFx.Telegraph(p, 4f, blood, 2f), 1.2f),
            ("shockwave", () => SpellFx.Shockwave(p, 6f, mind, 0.6f), 0.2f),
            ("pillar", () => SpellFx.Pillar(p, death, 8f, 2f), 0.6f),
            ("storm-cloud", () => SpellFx.Cloud(p, 6f, 9f, new Color(0.2f, 0.22f, 0.28f), 5f, true), 2.5f),
            ("plague-cloud", () => SpellFx.Cloud(p, 5f, 1.2f, new Color(0.35f, 0.6f, 0.1f), 5f), 2.5f),
            ("burst", () => SpellFx.Burst(p + Vector3.up, plague, 3f), 0.1f),
            ("aura-motes", () => SpellFx.Aura(me, mind, 3f, AuraStyle.Motes), 1f),
            ("aura-bubbles", () => { if (dummy != null) SpellFx.Aura(dummy, plague, 3f, AuraStyle.Bubbles); }, 1.5f),
            ("aura-crackle", () => { if (dummy != null) SpellFx.Aura(dummy, storm, 3f, AuraStyle.Crackle); }, 1f),
            ("aura-shell", () => SpellFx.Aura(dummy ?? me, mind, 3f, AuraStyle.Shell), 1f),
            ("tether", () => { if (dummy != null) SpellFx.Tether(dummy, me, blood, 3f); }, 1f),
            ("game-cometfall", () => SpellFx.GameEffect("CometfallEffect", p, 1f, 3f), 0.4f),
            ("game-frostnova", () => SpellFx.GameEffect("FrostNovaEffect", p, 1f, 3f), 0.4f),
            ("game-staticfield", () => SpellFx.GameEffect("StaticFieldEffect", p, 1f, 3f), 0.4f),
            ("game-smoke", () => SpellFx.GameEffect("SmokeBombExplodeFX", p, 1f, 3f), 0.4f),
            ("game-flame", () => SpellFx.GameEffect("GoutOfFlame", p + Vector3.up, 1f, 3f), 0.4f),
        };
    }

    static Camera shotCam;
    static GestureSpell topCorner, gameSelector, customCorner, customSelector;

    static GestureMagicSystem Grid() => GM.instance.GetComponentInChildren<GestureMagicSystem>(true);

    static string NodeName(GestureSpell x) => x == null ? "-" : $"{x.transform.parent.name}/{(x.Spell == SpellType.None ? x.name : (ModContent.SpellById((int)x.Spell)?.Key ?? x.Spell.ToString()))}";

    static void GridStart()
    {
        var g = Grid();
        try { g.ManualStart(); } catch (Exception e) { Log($"grid: ManualStart threw {e.InnerException?.Message ?? e.Message}"); }
        if (!g.gameObject.activeSelf) g.gameObject.SetActive(true);
        g.enabled = true;
        try { g.Expand(); } catch (Exception e) { Log($"grid: Expand threw {e.InnerException?.Message ?? e.Message}"); }
        // Fully open, then hold still: with no hand on the wand (flat), the grid's own LateUpdate would
        // fold it away before the picture is taken. Its stroke fades run on their own components.
        try { typeof(GestureMagicSystem).GetMethod("SetExpansion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(g, new object[] { 1f }); }
        catch (Exception e) { Log($"grid: SetExpansion threw {e.InnerException?.Message ?? e.Message}"); }
        g.enabled = false;
        var openers = g.StartingSpells.ToArray().ToList();
        topCorner = openers.FirstOrDefault(o => o.transform.parent.name == "TopRight");
        // A bottom corner holding a custom branch and no school the player owns.
        customCorner = openers.FirstOrDefault(o => o.LeadsToSpells.ToArray().Any(x => x != null && x.transform.parent.name.Contains("(arcana.")) && o.transform.parent.name.StartsWith("Bottom"))
                       ?? openers.FirstOrDefault(o => o.LeadsToSpells.ToArray().Any(x => x != null && x.transform.parent.name.Contains("(arcana.")));
        gameSelector = topCorner?.LeadsToSpells.ToArray().FirstOrDefault(x => x != null && x.transform.parent.name == "Fire");
        customSelector = customCorner?.LeadsToSpells.ToArray().FirstOrDefault(x => x != null && x.transform.parent.name.Contains("(arcana."));
        Log($"grid: active {g.gameObject.activeInHierarchy}, at {g.transform.position}, scale {g.transform.lossyScale}, nodes {g.AllSpells.Count}, " +
            $"live [{string.Join(", ", g.ActivelyCastingSpells.ToArray().Select(NodeName))}]; game corner {NodeName(topCorner)}, custom corner {NodeName(customCorner)}, " +
            $"game selector {NodeName(gameSelector)}, custom selector {NodeName(customSelector)}");
    }

    static void GridStep(string what, GestureSpell node)
    {
        var g = Grid();
        if (node == null) { Log($"grid: no {what} to draw"); return; }
        var done = typeof(GestureMagicSystem).GetMethod("DoSpellCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        try { done.Invoke(g, new object[] { node }); } catch (Exception e) { Log($"grid: drawing {what} threw {e.InnerException?.Message ?? e.Message}"); }
        Log($"grid: drew the {what} {NodeName(node)} -> live [{string.Join(", ", g.ActivelyCastingSpells.ToArray().Select(NodeName))}]");
    }

    // Photographs the grid with a camera of our own (the flat camera belongs to the game), and
    // records what each live node actually shows: objects on, renderers on, label alpha and text.
    static void GridShot(string name)
    {
        var g = Grid();
        var rends = g.GetComponentsInChildren<Renderer>(false).Where(r => r != null && r.enabled).ToList();
        if (rends.Count == 0) { Log($"grid shot {name}: nothing renders under the grid"); }
        var b = rends.Count > 0 ? rends[0].bounds : new Bounds(g.transform.position, Vector3.one);
        foreach (var r in rends) b.Encapsulate(r.bounds);
        if (shotCam == null)
        {
            var go = new GameObject("ApiProbeCamera");
            shotCam = go.AddComponent<Camera>();
            shotCam.depth = 100;
            shotCam.nearClipPlane = 0.01f;
            shotCam.fieldOfView = 60f;
            shotCam.clearFlags = CameraClearFlags.SolidColor;
            shotCam.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
        }
        var main = Camera.main;
        var toViewer = main != null ? (main.transform.position - b.center) : -g.transform.forward;
        toViewer = Vector3.ProjectOnPlane(toViewer, Vector3.up).sqrMagnitude > 0.01f ? toViewer.normalized : -g.transform.forward;
        float dist = Mathf.Max(0.6f, b.extents.magnitude * 2.2f);
        shotCam.transform.position = b.center + toViewer * dist;
        shotCam.transform.LookAt(b.center);
        Shots.Take("grid-" + name);
        // The numbers behind the picture.
        foreach (var x in g.ActivelyCastingSpells.ToArray().Where(x => x != null))
        {
            var rs = x.GetComponentsInChildren<Renderer>(true);
            int on = rs.Count(r => r.enabled && r.gameObject.activeInHierarchy);
            var labels = (x.SpellLabels?.ToArray() ?? new SpellLabel[0]).Where(l => l != null)
                .Select(l => $"{(l.gameObject.activeInHierarchy ? "on" : "off")} a={(l.canvasGroup != null ? l.canvasGroup.alpha : -1):0.00} '{l.SpellNameText?.text}'/'{l.LabelText?.text}' icon={(l.IconSprite?.sprite != null ? l.IconSprite.sprite.name : "-")}");
            var gest = x.Gestures?.ToArray().Where(m => m != null).Select(m => $"{(m.gameObject.activeInHierarchy ? "on" : "off")} fade={m.fadeAlpha:0.00}") ?? Enumerable.Empty<string>();
            Log($"grid shot {name}: {NodeName(x)} active {x.gameObject.activeInHierarchy}, renderers on {on}/{rs.Length}, unlocked {x.Unlocked}, viable {x.isCurrentlyViablePath()}, " +
                $"strokes [{string.Join(" ", gest)}], labels [{string.Join("; ", labels)}]");
        }
        Log($"grid shot {name}: grid bounds {b.center} size {b.size}, {rends.Count} renderers on, camera at {shotCam.transform.position}");
    }

    // A/B in one run: the release's state (grafted spells missing from the openers' PotentialSpells)
    // against the fix, each driven through the grid's own start-up and completion steps.
    static void GridAB()
    {
        var g = GM.instance.GetComponentInChildren<GestureMagicSystem>(true);
        var ours = g.AllSpells.ToArray().Where(x => x != null && ModContent.SpellById((int)x.Spell) != null).ToList();
        var openers = g.StartingSpells.ToArray().ToList();
        var removed = new System.Collections.Generic.List<(GestureSpell Op, GestureSpell N)>();
        foreach (var op in openers)
            foreach (var n in ours)
                if (op.PotentialSpells != null && op.PotentialSpells.Contains(n)) { op.PotentialSpells.Remove(n); removed.Add((op, n)); }
        Log($"grid A (as released): took {removed.Count} grafted spells back out of the openers' lists");
        Chain("A as released");
        foreach (var (op, n) in removed) op.PotentialSpells.Add(n);
        Log($"grid B (fixed): put {removed.Count} back");
        Chain("B fixed");
    }

    static void Chain(string label)
    {
        var g = GM.instance.GetComponentInChildren<GestureMagicSystem>(true);
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var done = typeof(GestureMagicSystem).GetMethod("DoSpellCompleted", flags);
        string Active() => string.Join(", ", g.ActivelyCastingSpells.ToArray().Where(x => x != null).Select(x => $"{x.transform.parent.name}/{(x.Spell == SpellType.None ? x.name : (ModContent.SpellById((int)x.Spell)?.Key ?? x.Spell.ToString()))}"));
        try { g.ManualStart(); } catch (Exception e) { Log($"grid {label}: ManualStart threw {e.InnerException?.Message ?? e.Message}"); }
        Log($"grid {label}: after the grid's start-up, active: [{Active()}]");
        foreach (var op in g.StartingSpells.ToArray())
            Log($"grid {label}: corner {op.transform.parent.name}: unlocked {op.Unlocked}, viable {op.isCurrentlyViablePath()}, active {g.ActivelyCastingSpells.Contains(op)}");
        // Walk one custom school whose corner holds no game school, through the game's own completion step.
        var school = ModContent.Schools.FirstOrDefault(sc => RuneTree.HostOf(sc.Key) is SkillType h && h is SkillType.EarthMagic or SkillType.AlchemicalMagic or SkillType.MirrorMagic or SkillType.StarMagic or SkillType.LifeMagic or SkillType.ForestMagic or SkillType.IronMagic or SkillType.ShadowMagic or SkillType.CrystalMagic)
                     ?? ModContent.Schools.First();
        var nodes = g.AllSpells.ToArray().Where(x => x != null && x.transform.parent != null && x.transform.parent.name.Contains("(" + school.Key + ")")).ToList();
        var sel = nodes.FirstOrDefault(x => x.Spell == SpellType.None);
        var opener = g.StartingSpells.ToArray().FirstOrDefault(o => sel != null && o.LeadsToSpells.Contains(sel));
        var spell = nodes.FirstOrDefault(x => x.Spell != SpellType.None);
        Log($"grid {label}: walking {school.Key} (corner {opener?.transform.parent.name}, runes of {RuneTree.HostOf(school.Key)})");
        if (opener == null || sel == null || spell == null) { Log($"grid {label}: branch incomplete"); return; }
        try
        {
            done.Invoke(g, new object[] { opener });
            Log($"grid {label}: opener drawn -> active [{Active()}]; selector viable {sel.isCurrentlyViablePath()}");
            done.Invoke(g, new object[] { sel });
            Log($"grid {label}: selector drawn -> active [{Active()}]; spell viable {spell.isCurrentlyViablePath()}, labels [" +
                string.Join(",", (spell.SpellLabels?.ToArray() ?? new SpellLabel[0]).Select(l => $"{l?.SpellNameText?.text}/{(l?.IconSprite?.sprite != null ? l.IconSprite.sprite.name : "-")}")) + "]");
            done.Invoke(g, new object[] { spell });
            var cw = Caster();
            Log($"grid {label}: spell drawn -> casting {cw?.CastingSpellType}, local {cw?.CastingSpellLocal?.spellType} ({ModContent.SpellById((int)(cw?.CastingSpellType ?? 0))?.Key}), stage {cw?.CastingSpellLocal?.stage}");
            cw?.ClearSpell();
        }
        catch (Exception e) { Log($"grid {label}: walking threw {e.InnerException?.Message ?? e.Message}"); }
    }

    // The VR casting path, checked through the grid's own code: its unlock pass, its per-frame
    // viability check, the labels, and finishing a spell's runes.
    static void GridCheck(string when)
    {
        var g = GM.instance.GetComponentInChildren<GestureMagicSystem>(true);
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(GestureMagicSystem).GetMethod("ConsiderEnablingSpells", flags)?.Invoke(g, null);
        var me = Players.LocalFighter;
        var held = new System.Collections.Generic.List<string>();
        foreach (var kv in me.skills) held.Add(ModContent.SchoolById((int)kv.Key)?.Key ?? kv.Key.ToString());
        Log($"grid [{when}]: player holds {string.Join(", ", held)}");
        foreach (var op in g.StartingSpells.ToArray())
        {
            var pot = op.PotentialSpells?.ToArray() ?? new GestureSpell[0];
            int custom = pot.Count(x => x != null && ModContent.SpellById((int)x.Spell) != null);
            int customUnlocked = pot.Count(x => x != null && x.Unlocked && ModContent.SpellById((int)x.Spell) != null);
            int gameUnlocked = pot.Count(x => x != null && x.Unlocked && x.Spell != SpellType.None && ModContent.SpellById((int)x.Spell) == null);
            Log($"grid [{when}]: corner {op.transform.parent.name}: unlocked {op.Unlocked}, viable {op.isCurrentlyViablePath()}; reaches {pot.Length} " +
                $"({custom} custom, {customUnlocked} custom unlocked, {gameUnlocked} game unlocked)");
        }
        foreach (var sc in ModContent.Schools)
        {
            var nodes = g.AllSpells.ToArray().Where(x => x != null && x.transform.parent != null && x.transform.parent.name.Contains("(" + sc.Key + ")")).ToList();
            var sel = nodes.FirstOrDefault(x => x.Spell == SpellType.None);
            var opener = g.StartingSpells.ToArray().FirstOrDefault(o => sel != null && o.LeadsToSpells.Contains(sel));
            if (sel == null) { Log($"grid [{when}]: {sc.Key}: no branch"); continue; }
            Log($"grid [{when}]: {sc.Key}: corner {opener?.transform.parent.name} unlocked {opener?.Unlocked} viable {opener?.isCurrentlyViablePath()}, " +
                $"selector unlocked {sel.Unlocked} viable {sel.isCurrentlyViablePath()}, spells " +
                string.Join(" | ", nodes.Where(x => x.Spell != SpellType.None).Select(x =>
                    $"{ModContent.SpellById((int)x.Spell)?.Key} unlocked {x.Unlocked} viable {x.isCurrentlyViablePath()} labels [" +
                    string.Join(",", (x.SpellLabels?.ToArray() ?? new SpellLabel[0]).Select(l => $"{l?.SpellNameText?.text}/{(l?.IconSprite?.sprite != null ? l.IconSprite.sprite.name : "-")}")) + "]")));
        }
        // Finishing a custom spell's runes: what the grid does when the last stroke is drawn.
        var target = g.AllSpells.ToArray().FirstOrDefault(x => x != null && ModContent.SpellById((int)x.Spell) != null && x.Unlocked);
        var cw = Caster();
        if (target != null && cw != null)
        {
            try
            {
                typeof(GestureMagicSystem).GetMethod("DoSpellCompleted", flags)?.Invoke(g, new object[] { target });
                Log($"grid [{when}]: finished the runes of {ModContent.SpellById((int)target.Spell).Key} -> casting {cw.CastingSpellType}, local {cw.CastingSpellLocal?.spellType}, stage {cw.CastingSpellLocal?.stage}");
                cw.LocalPlayerStartCasting(target.Spell);
                Log($"grid [{when}]: LocalPlayerStartCasting({ModContent.SpellById((int)target.Spell).Key}) -> casting {cw.CastingSpellType}, local {cw.CastingSpellLocal?.spellType}, stage {cw.CastingSpellLocal?.stage}");
                cw.ClearSpell();
            }
            catch (Exception e) { Log($"grid [{when}]: finishing runes threw {e.InnerException?.Message ?? e.Message}"); }
        }
    }

    static void DumpTree()
    {
        var g = GM.instance.GetComponentInChildren<GestureMagicSystem>(true);
        Log($"rune grid: {g.AllSpells?.Count} nodes");
        foreach (var gs in g.AllSpells)
        {
            if (gs == null) continue;
            bool custom = ModContent.SpellById((int)gs.Spell) != null;
            if (!custom) continue;
            var leads = gs.LeadsToSpells == null ? "" : string.Join(",", gs.LeadsToSpells.ToArray().Select(x => x == null ? "-" : $"{x.Spell}@{x.transform.parent.name}"));
            Log($"  node {N(gs.gameObject)} under {N(gs.transform.parent?.gameObject)} spell {gs.Spell} unlocked {gs.Unlocked} strokes {gs.Gestures?.Count} leads [{leads}] labels {gs.SpellLabels?.Count}");
        }
    }
}
