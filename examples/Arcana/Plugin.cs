using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using IronstrikeApi;
using IronstrikeApi.Content;
using IronstrikeApi.Gameplay;
using UnityEngine;

namespace Arcana;

// Five new magic schools for casters, ten spells. They are additions: any of them can be held next to
// any game school. Each school has three stages (I, II, III); every spell grows with them in reach,
// duration and power, and every one of them is meant to be seen from across the battlefield.
//
//   Lightning Magic   Chain Lightning (force lightning into every enemy in reach), Thunderstorm
//   Sky Magic         Air Strike, Orbital Laser
//   Death Magic       Raise Dead, Life Drain
//   Mind Magic        Telekinesis, Kinetic Ward
//   Plague Magic      Plague Cloud, Pestilence
//
// Every OnCast runs on every machine at once. SpellCast's Damage and Heal are sent once, by the
// caster; everything visual (SpellFx) plays on every machine, so every player sees the show.
[BepInPlugin("eu.euroswarms.ironstrike.arcana", "Arcana", "1.0.0")]
[BepInDependency(ModApi.Guid)]
[ModKind(ModKind.Gameplay)]
[RequiresApi("0.2.0")]
public class Plugin : BasePlugin
{
    static readonly Color Storm = new(0.55f, 0.72f, 1f), StormCloud = new(0.13f, 0.14f, 0.2f), Death = new(0.45f, 1f, 0.55f),
                          Blood = new(1f, 0.15f, 0.22f), Psi = new(0.78f, 0.45f, 1f), Fire = new(1f, 0.55f, 0.2f),
                          Laser = new(1f, 0.35f, 0.25f), Plague = new(0.55f, 0.95f, 0.15f), PlagueFog = new(0.3f, 0.5f, 0.08f);

    // The value for a school stage: T(level, I, II, III). Levels 6 to 8 are the enhanced stages I to III.
    static float T(int level, float a, float b, float c) { int s = level > 5 ? level - 5 : level; return s <= 1 ? a : s == 2 ? b : c; }

    // Enhanced (taken from an enhanced upgrade card): every spell gets something extra.
    static bool E(int level) => level > 5;
    static string Plus(int level, string extra) => E(level) ? " Enhanced: " + extra : "";

    public override void Load()
    {
        LightningMagic();
        SkyMagic();
        DeathMagic();
        MindMagic();
        PlagueMagic();
        Log.LogInfo($"Arcana: {ModContent.Schools.Count} schools, {ModContent.Spells.Count} spells registered");
    }

    static Vector3 WandTip(SpellCast cast)
    {
        var tip = cast.Wand != null ? cast.Wand.WandTip : null;
        return tip != null ? tip.position : Body.Center(cast.Caster) + Vector3.up * 0.4f;
    }

    static void School(string key, string name, string icon, SkillCategory cat, SkillType runes, string first, string second)
        => ModContent.School(key, s =>
        {
            s.Name = name;
            s.Icon = Icons.Library(icon);
            s.Category = cat;
            s.Runes = runes;
            s.MaxLevel = 3;
            s.Spells.Add(first);
            s.Spells.Add(second);
        });

    // ================================================================== Lightning Magic

    static void LightningMagic()
    {
        ModContent.Spell("arcana.chain_lightning", s =>
        {
            s.Name = "Chain Lightning";
            s.Icon = Icons.Library("spell_chain_lightning");
            s.Color = Storm;
            s.Category = SpellCategory.MinorAttack;
            s.Targeting = SpellTargetingType.Self;            // no aiming: it finds every enemy in reach
            s.LooksLike = SpellType.StaticField;               // whose circle shows the reach while casting
            s.FireDelayTicks = 0;
            s.Cooldown = new[] { 12f, 11f, 10f };
            s.ManaCost = new[] { 25f, 25f, 25f };
            s.Range = new[] { 10f, 14f, 18f };
            s.Describe = l => $"Lightning pours from your wand for {Secs(l)} seconds, locking onto every enemy within {Reach(l)} metres: {Dps(l)} damage a second to each." +
                              Plus(l, "the storm leaps between everything it strikes, and enemies just outside its reach are caught too.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                float secs = Secs(lvl), reach = Reach(lvl);
                SpellFx.Glow(() => WandTip(cast), Storm, 0.7f, secs);
                SpellFx.Flash(WandTip(cast), Storm, 3f, 0.5f);
                SpellFx.Shockwave(Body.Feet(cast.Caster), reach, Storm, 0.6f);
                SpellFx.Aura(cast.Caster, Storm, secs, AuraStyle.Crackle);
                var struck = new HashSet<Fighter>();
                const float tick = 0.25f;
                for (int i = 0; i * tick < secs; i++)
                {
                    cast.After(i * tick, x =>
                    {
                        var foes = x.EnemiesNear(Body.Center(x.Caster), reach);
                        if (foes.Count == 0)
                        {
                            // Nothing in reach: the lightning lashes out at the air around you.
                            var dir = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up) * Vector3.forward;
                            SpellFx.Bolt(WandTip(x), WandTip(x) + dir * reach * 0.4f + Vector3.up * UnityEngine.Random.Range(-1f, 1.5f), Storm, 0.05f, 0.2f);
                            return;
                        }
                        foreach (var e in foes)
                        {
                            var target = e;
                            SpellFx.Channel(() => WandTip(x), () => target == null || target.dead ? null : Body.Center(target), Storm, tick + 0.08f, 0.09f);
                            x.Damage(target, Dps(lvl) * tick);
                            if (struck.Add(target)) SpellFx.Aura(target, Storm, secs - i * tick, AuraStyle.Crackle);
                        }
                        if (E(lvl))
                        {
                            // Enhanced: arcs between the struck, and on to enemies just beyond reach.
                            for (int k = 1; k < foes.Count; k++) SpellFx.Bolt(Body.Center(foes[k - 1]), Body.Center(foes[k]), Storm, 0.06f, tick + 0.05f);
                            foreach (var near in foes.SelectMany(f => x.EnemiesNear(Body.Feet(f), 6f)).Distinct().Where(o => !foes.Contains(o)))
                            {
                                var from = foes.OrderBy(f => (Body.Feet(f) - Body.Feet(near)).sqrMagnitude).First();
                                SpellFx.Bolt(Body.Center(from), Body.Center(near), Storm, 0.06f, tick + 0.05f);
                                x.Damage(near, Dps(lvl) * tick * 0.5f);
                            }
                        }
                    });
                }
            };
            static float Secs(int l) => T(l, 3f, 4f, 5f) + (E(l) ? 1f : 0f);
            static float Reach(int l) => T(l, 10f, 14f, 18f);
            static float Dps(int l) => T(l, 30f, 40f, 55f) * (E(l) ? 1.3f : 1f);
        });

        ModContent.Spell("arcana.thunderstorm", s =>
        {
            s.Name = "Thunderstorm";
            s.Icon = Icons.Library("spell_thunderstorm");
            s.Color = Storm;
            s.Category = SpellCategory.MajorAttack;
            s.Targeting = SpellTargetingType.GroundCircle;
            s.LooksLike = SpellType.Supernova;
            s.FireDelayTicks = 0;
            s.Cooldown = new[] { 50f, 45f, 40f };
            s.ManaCost = new[] { 60f, 60f, 60f };
            s.Range = new[] { 40f, 45f, 50f };
            s.Describe = l => $"A black storm boils up over {Radius(l)} metres of ground. For {Secs(l)} seconds lightning crashes down on the enemies beneath it, " +
                              $"{Strike(l)} damage a strike, arcing on to their neighbours." + Plus(l, "every strike is a double bolt that stuns.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                var p = cast.Point;
                float r = Radius(lvl), secs = Secs(lvl);
                SpellFx.Cloud(p, r, 14f, StormCloud, secs + 1.5f, flicker: true);
                SpellFx.Telegraph(p, r, Storm, 1f);
                SpellFx.Flash(p + Vector3.up * 14f, Storm, 4f, 1f);
                float every = T(lvl, 0.45f, 0.35f, 0.28f);
                int n = 0;
                for (float at = 1f; at < 1f + secs; at += every)
                {
                    int k = n++;
                    cast.After(at, x =>
                    {
                        var foes = x.EnemiesNear(p, r);
                        Vector3 hit;
                        Fighter target = null;
                        if (foes.Count > 0) { target = foes[k % foes.Count]; hit = Body.Feet(target); }
                        else { var d = new Vector2(Mathf.Sin(k * 2.4f), Mathf.Cos(k * 3.7f)) * r * 0.8f; hit = p + new Vector3(d.x, 0f, d.y); }
                        var sky = hit + Vector3.up * 14f + new Vector3(Mathf.Sin(k), 0f, Mathf.Cos(k)) * 2f;
                        SpellFx.Bolt(sky, hit, Storm, 0.22f, 0.45f);
                        SpellFx.Bolt(sky, hit + new Vector3(Mathf.Cos(k * 1.3f), 0f, Mathf.Sin(k * 1.3f)) * 3f, Storm, 0.08f, 0.3f);
                        SpellFx.Flash(hit + Vector3.up, Storm, 4f, 0.35f);
                        SpellFx.Shockwave(hit, 3.5f, Storm, 0.4f);
                        SpellFx.Burst(hit + Vector3.up * 0.3f, Storm, 2.5f);
                        if (E(lvl))
                        {
                            SpellFx.Bolt(sky + new Vector3(3f, 0f, -2f), hit, Storm, 0.18f, 0.4f);
                            SpellFx.Shockwave(hit, 6f, Color.white, 0.5f);
                        }
                        if (target == null) return;
                        x.Damage(target, Strike(lvl) * (E(lvl) ? 2f : 1f));
                        if (E(lvl)) x.Status(target, StatusType.Stunned, 0.8f);
                        foreach (var e in x.EnemiesNear(hit, 5f).Where(e => e != target).Take(2))
                        {
                            SpellFx.Bolt(Body.Center(target), Body.Center(e), Storm, 0.07f, 0.3f);
                            x.Damage(e, Strike(lvl) * 0.5f);
                        }
                    });
                }
            };
            static float Radius(int l) => T(l, 10f, 12f, 14f);
            static float Secs(int l) => T(l, 5f, 6f, 7f);
            static float Strike(int l) => T(l, 35f, 45f, 60f);
        });

        School("arcana.lightning", "Lightning Magic", "school_tempest", SkillCategory.Evocation, SkillType.StormMagic, "arcana.chain_lightning", "arcana.thunderstorm");
    }

    // ================================================================== Sky Magic

    static void SkyMagic()
    {
        ModContent.Spell("arcana.air_strike", s =>
        {
            s.Name = "Air Strike";
            s.Icon = Icons.Library("spell_air_strike");
            s.Color = Fire;
            s.Category = SpellCategory.MinorAttack;
            s.Targeting = SpellTargetingType.GroundCircle;
            s.LooksLike = SpellType.Meteor;
            s.FireDelayTicks = 0;
            s.Cooldown = new[] { 14f, 12f, 10f };
            s.ManaCost = new[] { 25f, 25f, 25f };
            s.Range = new[] { 40f, 45f, 50f };
            s.Describe = l => $"Marks the ground. {Shells(l)} shells scream down from the sky across {Radius(l)} metres, {Dmg(l)} damage each." +
                              Plus(l, "a carpet of twice the shells, and the ground burns with napalm for 4 seconds.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                var p = cast.Point;
                float r = Radius(lvl);
                SpellFx.Telegraph(p, r, Fire, 1.2f);
                // The same scatter on every machine: seeded from the target point.
                var rnd = new System.Random((int)(p.x * 31 + p.z * 17));
                var from = p + new Vector3(-30f, 45f, -20f);
                for (int i = 0; i < Shells(lvl); i++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)rnd.NextDouble()) * r;
                    var at = p + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    var src = from + new Vector3(i * 1.5f, 0f, 0f);
                    float land = 1.2f + i * 0.22f;
                    const float fall = 0.55f;
                    // The shell lands exactly when the blast goes off: both are timed from the same moment.
                    cast.After(land - fall, x => SpellFx.Streak(src, at, Fire, fall));
                    cast.After(land, x =>
                    {
                        Impact(at, lvl);
                        foreach (var e in x.EnemiesNear(at, BlastRadius(lvl))) x.Damage(e, Dmg(lvl));
                        if (E(lvl))
                        {
                            // Napalm: flames cling to the ground and burn whoever stands in them.
                            SpellFx.GameEffect("GoutOfFlame", at, 1.2f, 4f);
                            SpellFx.Ring(at, BlastRadius(lvl), Fire, 4f);
                            for (int t = 1; t <= 4; t++)
                                x.After(t, y => { foreach (var e in y.EnemiesNear(at, BlastRadius(lvl))) { y.Damage(e, Dmg(lvl) * 0.25f); SpellFx.Burst(Body.Center(e), Fire, 0.6f); } });
                        }
                    });
                }
            };
            static int Shells(int l) => (int)T(l, 4, 6, 8) * (E(l) ? 2 : 1);
            static float BlastRadius(int l) => T(l, 3f, 3.5f, 4f);
            // A shell going off: a fireball, flame jets, a column of smoke and embers raining back down.
            static void Impact(Vector3 at, int l)
            {
                float size = T(l, 1.8f, 2.1f, 2.4f);
                SpellFx.Explosion(at, Fire, size);
                SpellFx.Explosion(at + Vector3.up * size, new Color(1f, 0.8f, 0.3f), size * 0.6f);
                SpellFx.Flash(at + Vector3.up * 3f, Fire, 5f, 0.7f);
                SpellFx.GameEffect("GoutOfFlame", at, size, 2f);
                SpellFx.Cloud(at, size * 0.8f, size * 1.5f, new Color(0.12f, 0.09f, 0.08f), 2.5f);
            }
            static float Radius(int l) => T(l, 5f, 6f, 7f);
            static float Dmg(int l) => T(l, 25f, 32f, 40f);
        });

        ModContent.Spell("arcana.orbital_laser", s =>
        {
            s.Name = "Orbital Laser";
            s.Icon = Icons.Library("spell_orbital_laser");
            s.Color = Laser;
            s.Category = SpellCategory.MajorAttack;
            s.Targeting = SpellTargetingType.GroundCircle;
            s.LooksLike = SpellType.Supernova;
            s.FireDelayTicks = 0;
            s.Cooldown = new[] { 55f, 50f, 45f };
            s.ManaCost = new[] { 60f, 60f, 60f };
            s.Range = new[] { 50f, 55f, 60f };
            s.Describe = l => $"Calls a beam down from orbit. After a moment's charge it burns for {Secs(l)} seconds, hunting the nearest enemy, " +
                              $"{Dps(l)} damage a second to everything within {Width(l) * 1.5f:0} metres, and ends in a blast." + Plus(l, "a second beam joins the first, hunting a different enemy.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                var p = cast.Point;
                float secs = Secs(lvl), w = Width(lvl);
                const float charge = 1.5f;
                // Charge: the sky takes aim.
                SpellFx.Telegraph(p, w * 2f, Laser, charge);
                SpellFx.Beam(p + Vector3.up * 120f, p, Laser, 0.15f, charge);
                // The beam: a fresh segment every tick, following its prey.
                var focus = p;
                var focus2 = p + new Vector3(3f, 0f, 0f);
                const float tick = 0.1f;
                for (int i = 0; i * tick < secs; i++)
                {
                    int k = i;
                    cast.After(charge + i * tick, x =>
                    {
                        var prey = x.EnemiesNear(focus, 12f).FirstOrDefault();
                        if (prey != null) focus = Vector3.MoveTowards(focus, Body.Feet(prey), 6f * tick);
                        SpellFx.Beam(focus + Vector3.up * 120f, focus, new Color(1f, 0.85f, 0.8f), w, tick * 2.5f);
                        if (k % 3 == 0)
                        {
                            SpellFx.Shockwave(focus, w * 3f, Laser, 0.5f);
                            SpellFx.Explosion(focus, Laser, 0.5f);
                        }
                        foreach (var e in x.EnemiesNear(focus, w * 1.5f)) x.Damage(e, Dps(lvl) * tick);
                        if (E(lvl))
                        {
                            // Enhanced: a twin beam, hunting the enemy the first one is not.
                            var other = x.EnemiesNear(focus2, 15f).FirstOrDefault(e => (Body.Feet(e) - focus).sqrMagnitude > w * w * 4f);
                            if (other != null) focus2 = Vector3.MoveTowards(focus2, Body.Feet(other), 6f * tick);
                            SpellFx.Beam(focus2 + Vector3.up * 120f, focus2, new Color(1f, 0.6f, 0.9f), w * 0.8f, tick * 2.5f);
                            if (k % 3 == 1) SpellFx.Shockwave(focus2, w * 2.5f, Laser, 0.5f);
                            foreach (var e in x.EnemiesNear(focus2, w * 1.2f)) x.Damage(e, Dps(lvl) * tick * 0.8f);
                        }
                    });
                }
                cast.After(charge + secs, x =>
                {
                    SpellFx.Explosion(focus, Laser, 2.5f);
                    SpellFx.Shockwave(focus, 12f, Laser, 0.8f);
                    SpellFx.Pillar(focus, Laser, 30f, 1f);
                    foreach (var e in x.EnemiesNear(focus, 6f)) x.Damage(e, Dps(lvl));
                });
            };
            static float Secs(int l) => T(l, 3f, 4f, 5f);
            static float Width(int l) => T(l, 2.5f, 3f, 3.5f);
            static float Dps(int l) => T(l, 45f, 55f, 70f);
        });

        School("arcana.sky", "Sky Magic", "school_war", SkillCategory.Evocation, SkillType.StarMagic, "arcana.air_strike", "arcana.orbital_laser");
    }

    // ================================================================== Death Magic

    static void DeathMagic()
    {
        ModContent.Spell("arcana.raise_dead", s =>
        {
            s.Name = "Raise Dead";
            s.Icon = Icons.Library("spell_raise_dead");
            s.Color = Death;
            s.Category = SpellCategory.Buff;
            s.Targeting = SpellTargetingType.Self;
            s.LooksLike = SpellType.Levitate;
            s.Cooldown = new[] { 90f, 75f, 60f };
            s.ManaCost = new[] { 50f, 50f, 50f };
            s.Range = new[] { 20f, 25f, 30f };
            s.Describe = l => $"Tears open the veil: every fallen ally within {Reach(l)} metres rises, and living allies are healed for {Heal(l)}." +
                              Plus(l, "the risen and the healed are wrapped in a barrier of bone for 6 seconds.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                var p = Body.Feet(cast.Caster);
                float reach = Reach(lvl);
                // A sigil of rings on the ground and a circle of soul pillars around you (never on you: you would see nothing).
                SpellFx.Ring(p, reach, Death, 3f);
                SpellFx.Ring(p, reach * 0.6f, Death, 3f);
                SpellFx.Ring(p, 3f, Death, 3f);
                SpellFx.Shockwave(p, reach, Death, 1.2f);
                SpellFx.Flash(p + Vector3.up * 2f, Death, 4f, 1.5f);
                int pillars = (int)T(lvl, 6, 8, 10);
                for (int i = 0; i < pillars; i++)
                {
                    float a = i * Mathf.PI * 2f / pillars;
                    var at = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * reach * 0.6f;
                    cast.After(i * 0.08f, x => { SpellFx.Pillar(at, Death, 20f, 2.2f); SpellFx.Cloud(at, 1.5f, 0.4f, new Color(0.06f, 0.14f, 0.08f), 2.5f); });
                }
                foreach (var f in cast.FallenAlliesNear(p, reach))
                {
                    var fallen = f;
                    SpellFx.Pillar(Body.Feet(fallen), Death, 18f, 2.5f);
                    cast.After(0.8f, x =>
                    {
                        x.Revive(fallen);
                        SpellFx.Explosion(Body.Feet(fallen), Death, 0.8f);
                        SpellFx.Aura(fallen, Death, 4f, AuraStyle.Motes);
                        if (E(lvl)) { x.Status(fallen, StatusType.Barrier, 6f, 50f); SpellFx.Aura(fallen, Death, 6f, AuraStyle.Shell); }
                    });
                }
                foreach (var f in cast.AlliesNear(p, reach))
                {
                    cast.Heal(f, Heal(lvl));
                    SpellFx.Aura(f, Death, 2.5f, AuraStyle.Motes);
                    if (E(lvl)) { cast.Status(f, StatusType.Barrier, 6f, 50f); SpellFx.Aura(f, Death, 6f, AuraStyle.Shell); }
                }
            };
            static float Reach(int l) => T(l, 20f, 25f, 30f);
            static float Heal(int l) => T(l, 30f, 50f, 80f);
        });

        ModContent.Spell("arcana.life_drain", s =>
        {
            s.Name = "Life Drain";
            s.Icon = Icons.Library("spell_life_drain");
            s.Color = Blood;
            s.Category = SpellCategory.Curse;
            s.Targeting = SpellTargetingType.SingleEnemy;
            s.LooksLike = SpellType.PoisonSting;
            s.FireDelayTicks = 0;
            s.Cooldown = new[] { 15f, 13f, 11f };
            s.ManaCost = new[] { 25f, 25f, 25f };
            s.Range = new[] { 35f, 40f, 45f };
            s.Describe = l => $"Hooks an enemy and drinks from it for {Secs(l)} seconds: {Dps(l)} damage a second, half of it flowing back to you as health." +
                              Plus(l, "the hooks spread to two more enemies near the first.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                if (cast.Target is null) return;
                float secs = Secs(lvl);
                var victims = new List<Fighter> { cast.Target };
                if (E(lvl)) victims.AddRange(cast.EnemiesNear(Body.Feet(cast.Target), 8f).Where(e => e != cast.Target).Take(2));
                SpellFx.Aura(cast.Caster, Blood, secs, AuraStyle.Embers);
                foreach (var v in victims)
                {
                    var target = v;
                    SpellFx.Tether(() => target == null || target.dead ? null : Body.Center(target), () => WandTip(cast), Blood, secs);
                    SpellFx.Aura(target, Blood, secs, AuraStyle.Smoke);
                    SpellFx.Burst(Body.Center(target), Blood, 2f);
                    for (int i = 0; i < secs * 2; i++)
                        cast.After(i * 0.5f, x =>
                        {
                            if (target.dead) return;
                            x.Damage(target, Dps(lvl) * 0.5f);
                            x.Heal(x.Caster, Dps(lvl) * 0.25f);
                            SpellFx.Burst(Body.Center(target), Blood, 0.8f);
                        });
                }
            };
            static float Secs(int l) => T(l, 2f, 3f, 4f);
            static float Dps(int l) => T(l, 30f, 38f, 48f);
        });

        School("arcana.death", "Death Magic", "school_necromancy", SkillCategory.Enchantment, SkillType.LifeMagic, "arcana.raise_dead", "arcana.life_drain");
    }

    // ================================================================== Mind Magic

    static void MindMagic()
    {
        ModContent.Spell("arcana.telekinesis", s =>
        {
            s.Name = "Telekinesis";
            s.Icon = Icons.Library("spell_telekinesis");
            s.Color = Psi;
            s.Category = SpellCategory.Curse;
            s.Targeting = SpellTargetingType.SingleEnemy;
            s.LooksLike = SpellType.Feedback;
            s.FireDelayTicks = 0;
            s.Cooldown = new[] { 20f, 17f, 14f };
            s.ManaCost = new[] { 30f, 30f, 30f };
            s.Range = new[] { 40f, 45f, 50f };
            s.Describe = l => $"Seizes an enemy with your mind and holds it helpless in the air for {Hold(l)} seconds, then hurls it into the ground: " +
                              $"{Slam(l)} damage, and everything within {Quake(l)} metres is stunned." + Plus(l, "every enemy within 5 metres of it is torn up into the air with it, and slammed down too.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                if (cast.Target is null) return;
                float hold = Hold(lvl);
                var seized = new List<Fighter> { cast.Target };
                if (E(lvl)) seized.AddRange(cast.EnemiesNear(Body.Feet(cast.Target), 5f).Where(e => e != cast.Target));
                foreach (var v in seized)
                {
                    var target = v;
                    cast.Lift(target, T(lvl, 4f, 5.5f, 7f), hold);
                    cast.Status(target, StatusType.Stunned, hold + 1.2f);
                    SpellFx.Tether(() => WandTip(cast), () => target == null || target.dead ? null : Body.Center(target), Psi, hold);
                    SpellFx.Aura(target, Psi, hold, AuraStyle.Motes);
                    SpellFx.Aura(target, Psi, hold, AuraStyle.Shell);
                    SpellFx.Shockwave(Body.Feet(target), 3f, Psi, 0.5f);
                    cast.After(hold, x =>
                    {
                        var p = Body.Feet(target);
                        SpellFx.Explosion(p, Psi, 1.6f);
                        SpellFx.Shockwave(p, Quake(lvl), Psi, 0.7f);
                        SpellFx.Shockwave(p, Quake(lvl) * 0.6f, Color.white, 0.5f);
                        SpellFx.Flash(p + Vector3.up, Psi, 4f, 0.6f);
                        x.Damage(target, Slam(lvl));
                        foreach (var e in x.EnemiesNear(p, Quake(lvl))) if (!seized.Contains(e)) x.Status(e, StatusType.Stunned, T(lvl, 1f, 1.5f, 2f));
                    });
                }
            };
            static float Hold(int l) => T(l, 2f, 2.5f, 3f);   // the slam lands as the game's flight brings it down
            static float Slam(int l) => T(l, 50f, 70f, 95f);
            static float Quake(int l) => T(l, 4f, 6f, 8f);
        });

        ModContent.Spell("arcana.kinetic_ward", s =>
        {
            s.Name = "Kinetic Ward";
            s.Icon = Icons.Library("spell_kinetic_ward");
            s.Color = Psi;
            s.Category = SpellCategory.Buff;
            s.Targeting = SpellTargetingType.Self;
            s.LooksLike = SpellType.Levitate;
            s.Cooldown = new[] { 30f, 26f, 22f };
            s.ManaCost = new[] { 30f, 30f, 30f };
            s.Range = new[] { 6f, 8f, 10f };
            s.Describe = l => $"A wall of force bursts out from you, stunning every enemy within {Push(l)} metres, and wraps you in a ward that turns projectiles aside for {Secs(l)} seconds." +
                              Plus(l, "the ward pulses out again every 2 seconds while it lasts.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                var p = Body.Feet(cast.Caster);
                cast.Status(cast.Caster, StatusType.MissileShield, Secs(lvl));
                SpellFx.Aura(cast.Caster, Psi, Secs(lvl), AuraStyle.Shell);
                SpellFx.Shockwave(p, Push(lvl), Psi, 0.5f);
                SpellFx.Shockwave(p, Push(lvl) * 0.7f, Color.white, 0.4f);
                SpellFx.Flash(p + Vector3.up, Psi, 3f, 0.5f);
                SpellFx.Burst(p + Vector3.up, Psi, 3f);
                Pulse(cast, lvl);
                if (E(lvl))
                    for (float t = 2f; t < Secs(lvl); t += 2f)
                        cast.After(t, x => { SpellFx.Shockwave(Body.Feet(x.Caster), Push(lvl), Psi, 0.5f); Pulse(x, lvl); });
            };
            static float Push(int l) => T(l, 6f, 8f, 10f);
            static float Secs(int l) => T(l, 6f, 8f, 10f);
            static void Pulse(SpellCast cast, int lvl)
            {
                foreach (var e in cast.EnemiesNear(Body.Feet(cast.Caster), Push(lvl)))
                {
                    cast.Status(e, StatusType.Stunned, T(lvl, 1.5f, 2f, 2.5f));
                    SpellFx.Bolt(WandTip(cast), Body.Center(e), Psi, 0.05f, 0.25f);
                }
            }
        });

        School("arcana.mind", "Mind Magic", "school_mind", SkillCategory.Enchantment, SkillType.MirrorMagic, "arcana.telekinesis", "arcana.kinetic_ward");
    }

    // ================================================================== Plague Magic

    static void PlagueMagic()
    {
        ModContent.Spell("arcana.plague_cloud", s =>
        {
            s.Name = "Plague Cloud";
            s.Icon = Icons.Library("spell_plague_cloud");
            s.Color = Plague;
            s.Category = SpellCategory.MinorAttack;
            s.Targeting = SpellTargetingType.Ray;
            s.LooksLike = SpellType.AcidSpray;
            s.FireDelayTicks = 0;
            s.Cooldown = new[] { 10f, 9f, 8f };
            s.ManaCost = new[] { 20f, 20f, 20f };
            s.Range = new[] { 20f, 24f, 28f };
            s.Describe = l => $"Breathes a rolling sickness {Reach(l)} metres ahead. Enemies caught in it take {Hit(l)} damage and are poisoned for {Dot(l)} a second for 5 seconds." +
                              Plus(l, "the clouds hang in the air for 5 seconds, sickening anyone who walks in.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                var aim = Vector3.ProjectOnPlane(cast.Direction, Vector3.up).normalized;
                var start = Body.Feet(cast.Caster);
                cast.SpawnArea(cast.Origin, cast.Direction, 0f);
                int puffs = (int)T(lvl, 4, 5, 6);
                for (int i = 1; i <= puffs; i++)
                {
                    var at = start + aim * (Reach(lvl) * i / puffs);
                    float wide = 1.5f + i * 0.6f;
                    if (E(lvl))
                        for (int t = 1; t <= 5; t++)
                            cast.After(i * 0.12f + t, y => { foreach (var e in y.EnemiesNear(at, wide + 0.5f)) if (!e.HasStatus(StatusType.Poison)) { y.Status(e, StatusType.Poison, 5f, Dot(lvl)); SpellFx.Aura(e, Plague, 5f, AuraStyle.Bubbles); } });
                    cast.After(i * 0.12f, x =>
                    {
                        SpellFx.Cloud(at, wide, 1f, PlagueFog, E(lvl) ? 6f : 3f);
                        foreach (var e in x.EnemiesNear(at, wide + 0.5f))
                        {
                            x.Damage(e, Hit(lvl) / puffs);
                            x.Status(e, StatusType.Poison, 5f, Dot(lvl));
                            SpellFx.Aura(e, Plague, 5f, AuraStyle.Bubbles);
                        }
                    });
                }
            };
            s.OnAreaHit = (c, target, at) => { };    // the rolling cloud above does the damage
            static float Reach(int l) => T(l, 12f, 16f, 20f);
            static float Hit(int l) => T(l, 15f, 20f, 26f);
            static float Dot(int l) => T(l, 8f, 11f, 15f);
        });

        ModContent.Spell("arcana.pestilence", s =>
        {
            s.Name = "Pestilence";
            s.Icon = Icons.Library("spell_pestilence");
            s.Color = Plague;
            s.Category = SpellCategory.MajorAttack;
            s.Targeting = SpellTargetingType.GroundCircle;
            s.LooksLike = SpellType.ThornGrowth;
            s.FireDelayTicks = 0;
            s.Cooldown = new[] { 50f, 45f, 40f };
            s.ManaCost = new[] { 60f, 60f, 60f };
            s.Range = new[] { 30f, 35f, 40f };
            s.Describe = l => $"A choking fog swallows {Radius(l)} metres. Everything inside is poisoned for {Dot(l)} a second and slowed for 8 seconds, " +
                              $"and every {Every(l)} seconds the sickness leaps to enemies near the infected." + Plus(l, "it leaps twice as far, and the infected take more damage from everything.");
            s.OnCast = (c, cast) =>
            {
                int lvl = c.Level;
                var p = cast.Point;
                float r = Radius(lvl);
                SpellFx.Cloud(p, r, 1.5f, PlagueFog, 8f);
                SpellFx.Cloud(p, r * 0.7f, 4f, new Color(0.2f, 0.3f, 0.05f), 8f);
                SpellFx.Ring(p, r, Plague, 8f);
                SpellFx.Shockwave(p, r, Plague, 0.8f);
                SpellFx.Flash(p + Vector3.up * 2f, Plague, 3f, 1f);
                var infected = new HashSet<Fighter>(cast.EnemiesNear(p, r));
                foreach (var e in infected) Infect(cast, e, lvl);
                for (float at = Every(lvl); at <= 8f; at += Every(lvl))
                    cast.After(at, x =>
                    {
                        foreach (var carrier in infected.Where(f => f != null && !f.dead).ToList())
                            foreach (var e in x.EnemiesNear(Body.Feet(carrier), E(lvl) ? 10f : 5f).Where(f => !infected.Contains(f)))
                            {
                                infected.Add(e);
                                SpellFx.Bolt(Body.Center(carrier), Body.Center(e), Plague, 0.05f, 0.4f);
                                Infect(x, e, lvl);
                            }
                        foreach (var e in x.EnemiesNear(p, r)) if (infected.Add(e)) Infect(x, e, lvl);
                    });
            };
            static float Radius(int l) => T(l, 7f, 9f, 11f);
            static float Dot(int l) => T(l, 15f, 20f, 27f);
            static float Every(int l) => T(l, 3f, 2f, 1.5f);
            static void Infect(SpellCast cast, Fighter e, int level)
            {
                cast.Status(e, StatusType.Poison, 8f, Dot(level));
                cast.Status(e, StatusType.Slowed, 8f, 0.4f);
                if (E(level)) cast.Status(e, StatusType.Vulnerability, 8f, 0.25f);
                SpellFx.Aura(e, Plague, 8f, AuraStyle.Bubbles);
                SpellFx.Burst(Body.Center(e), Plague, 1.2f);
            }
        });

        School("arcana.plague", "Plague Magic", "school_plague", SkillCategory.Evocation, SkillType.AlchemicalMagic, "arcana.plague_cloud", "arcana.pestilence");
    }
}
