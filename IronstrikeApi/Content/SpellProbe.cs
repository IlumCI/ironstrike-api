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

            case 30:    // every custom school gets a free branch of its own
                Log("branches: " + string.Join(", ", ModContent.Schools.Select(sc => $"{sc.Key}->{RuneTree.HostOf(sc.Key)?.ToString() ?? "none"}")));
                foreach (var sc in ModContent.Schools.Take(2)) Loadout.GiveSkill(sc.Runes, 1);
                Log($"gave the player {string.Join(", ", ModContent.Schools.Take(2).Select(sc => sc.Runes))}");
                step = 31; at = now + 3f; return;

            case 31:    // ...and moves off a branch when the player takes its game school
                Log("branches now: " + string.Join(", ", ModContent.Schools.Select(sc => $"{sc.Key}->{RuneTree.HostOf(sc.Key)?.ToString() ?? "none"}")));
                var hosts = ModContent.Schools.Select(sc => RuneTree.HostOf(sc.Key)).Where(h => h != null).ToList();
                Log($"distinct {hosts.Distinct().Count() == hosts.Count}, none on an owned school {hosts.All(h => !Players.LocalFighter.skills.ContainsKey(h.Value))}");
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
