using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using IronstrikeApi;
using IronstrikeApi.Content;
using IronstrikeApi.Gameplay;
using UnityEngine;

namespace Arcana;

// Five magic schools for casters, ten spells, no art of their own: every spell borrows the aiming,
// preview and effects of one of the game's spells (LooksLike) and the icons come from the API's
// library. Each school uses the rune shapes of one of the game's schools and replaces it.
//
//   Ars Tempestas (runes of Ars Fulmen)   Chain Lightning, Orbital Laser
//   Necromancy    (runes of Life Magic)   Raise Dead, Life Drain
//   Ars Mentis    (runes of Mirror Magic) Telekinesis, Kinetic Ward
//   Ars Bellica   (runes of Ars Terra)    Flak Shot, Air Strike
//   Ars Pestis    (runes of Ars Alchemia) Plague Cloud, Pestilence
//
// Every OnCast runs on every machine at once; SpellCast's tools (Damage, Heal, Status, After...) know
// which of them act once and which act everywhere. Damage numbers are for level 1 and grow per level.
[BepInPlugin("eu.euroswarms.ironstrike.arcana", "Arcana", "1.0.0")]
[BepInDependency(ModApi.Guid)]
[ModKind(ModKind.Gameplay)]
[RequiresApi("0.2.0")]
public class Plugin : BasePlugin
{
    static readonly Color Storm = new(0.55f, 0.75f, 1f), Death = new(0.55f, 1f, 0.6f), Blood = new(1f, 0.25f, 0.3f),
                          Mind = new(0.8f, 0.55f, 1f), Fire = new(1f, 0.6f, 0.25f), Plague = new(0.6f, 0.9f, 0.2f);

    public override void Load()
    {
        Tempestas();
        Necromancy();
        Mentis();
        Bellica();
        Pestis();
        Log.LogInfo($"Arcana: {ModContent.Schools.Count} schools, {ModContent.Spells.Count} spells registered");
    }

    // ------------------------------------------------------------------ Ars Tempestas

    static void Tempestas()
    {
        ModContent.Spell("arcana.chain_lightning", s =>
        {
            s.Name = "Chain Lightning";
            s.Icon = Icons.Library("spell_chain_lightning");
            s.Color = Storm;
            s.Category = SpellCategory.MinorAttack;
            s.Targeting = SpellTargetingType.SingleEnemy;
            s.LooksLike = SpellType.LightningBolt;
            s.Cooldown = new[] { 9f, 8.5f, 8f, 7.5f, 7f };
            s.ManaCost = new[] { 15f };
            s.Range = new[] { 50f };
            s.Describe = l => $"Lightning strikes an enemy for {Dmg(l)} damage, then leaps to {Jumps(l)} more nearby, losing a quarter of its strength with each leap.";
            s.Amount = (c, target) => Dmg(c.Level);
            s.OnCast = (c, cast) =>
            {
                if (cast.Target is null) return;
                // Pick the whole chain now, so every machine follows the same path.
                var chain = new List<Fighter> { cast.Target };
                for (int i = 0; i < Jumps(c.Level); i++)
                {
                    var from = Body.Feet(chain[^1]);
                    var next = cast.EnemiesNear(from, 10f).FirstOrDefault(e => !chain.Contains(e));
                    if (next is null) break;
                    chain.Add(next);
                }
                Vector3 prev = cast.Origin;
                float dmg = cast.Amount;
                for (int i = 0; i < chain.Count; i++)
                {
                    var target = chain[i];
                    var start = prev;
                    float hit = dmg;
                    cast.After(i * 0.12f, x =>
                    {
                        var to = Body.Center(target);
                        SpellFx.Bolt(start, to, Storm, 0.1f, 0.4f);
                        SpellFx.Flash(to, Storm, 2f, 0.3f);
                        x.Damage(target, hit);
                    });
                    prev = Body.Center(target);
                    dmg *= 0.75f;
                }
            };
            static float Dmg(int l) => 40 + 8 * (l - 1);
            static int Jumps(int l) => 2 + (l - 1) / 2;
        });

        ModContent.Spell("arcana.orbital_laser", s =>
        {
            s.Name = "Orbital Laser";
            s.Icon = Icons.Library("spell_orbital_laser");
            s.Color = Storm;
            s.Category = SpellCategory.MajorAttack;
            s.Targeting = SpellTargetingType.GroundCircle;
            s.LooksLike = SpellType.Supernova;
            s.Cooldown = new[] { 50f, 47f, 44f, 41f, 38f };
            s.ManaCost = new[] { 60f };
            s.Range = new[] { 50f };
            s.FireDelayTicks = 0;
            s.Describe = l => $"Marks the ground. A second later a beam from the sky burns it five times, {Pulse(l)} damage each to enemies within 4 metres.";
            s.OnCast = (c, cast) =>
            {
                var p = cast.Point;
                SpellFx.Ring(p, 4f, Blood, 1.1f);
                for (int i = 0; i < 5; i++)
                {
                    cast.After(1f + i * 0.3f, x =>
                    {
                        SpellFx.Beam(p + Vector3.up * 80f, p, new Color(0.85f, 0.95f, 1f), 1.4f, 0.35f);
                        SpellFx.Ring(p, 4f, Storm, 0.4f);
                        SpellFx.Flash(p, Storm, 4f, 0.3f);
                        foreach (var e in x.EnemiesNear(p, 4f)) x.Damage(e, Pulse(c.Level));
                    });
                }
            };
            static float Pulse(int l) => 30 + 6 * (l - 1);
        });

        ModContent.School("arcana.tempestas", s =>
        {
            s.Name = "Ars Tempestas";
            s.Icon = Icons.Library("school_tempest");
            s.Category = SkillCategory.Evocation;
            s.Runes = SkillType.StormMagic;
            s.Spells.Add("arcana.chain_lightning");
            s.Spells.Add("arcana.orbital_laser");
            s.Describe = l => "Learn Chain Lightning and Orbital Laser. Replaces Ars Fulmen.";
        });
    }

    // ------------------------------------------------------------------ Necromancy

    static void Necromancy()
    {
        ModContent.Spell("arcana.raise_dead", s =>
        {
            s.Name = "Raise Dead";
            s.Icon = Icons.Library("spell_raise_dead");
            s.Color = Death;
            s.Category = SpellCategory.Buff;
            s.Targeting = SpellTargetingType.Self;
            s.LooksLike = SpellType.Levitate;
            s.Cooldown = new[] { 90f, 80f, 70f, 60f, 50f };
            s.ManaCost = new[] { 50f };
            s.Range = new[] { 25f };
            s.Describe = l => $"Brings every fallen ally within 25 metres back to the fight, and heals living allies nearby for {Heal(l)}.";
            s.OnCast = (c, cast) =>
            {
                var p = Body.Feet(cast.Caster);
                SpellFx.Ring(p, 25f, Death, 1.2f);
                SpellFx.Flash(p, Death, 3f, 0.6f);
                foreach (var f in cast.FallenAlliesNear(p, 25f))
                {
                    SpellFx.Beam(Body.Feet(f), Body.Feet(f) + Vector3.up * 12f, Death, 0.6f, 1f);
                    cast.Revive(f);
                }
                foreach (var f in cast.AlliesNear(p, 25f)) cast.Heal(f, Heal(c.Level));
            };
            static float Heal(int l) => 20 + 10 * (l - 1);
        });

        ModContent.Spell("arcana.life_drain", s =>
        {
            s.Name = "Life Drain";
            s.Icon = Icons.Library("spell_life_drain");
            s.Color = Blood;
            s.Category = SpellCategory.Curse;
            s.Targeting = SpellTargetingType.SingleEnemy;
            s.LooksLike = SpellType.PoisonSting;
            s.Cooldown = new[] { 15f, 14f, 13f, 12f, 11f };
            s.ManaCost = new[] { 20f };
            s.Range = new[] { 40f };
            s.Describe = l => $"Tears {Dmg(l)} life from an enemy and three more slivers over three seconds. You keep half of all of it.";
            s.Amount = (c, t) => Dmg(c.Level);
            s.OnCast = (c, cast) =>
            {
                var target = cast.Target;
                if (target is null) return;
                Drain(cast, target, cast.Amount);
                for (int i = 1; i <= 3; i++) cast.After(i, x => Drain(x, target, cast.Amount * 0.25f));
            };
            static float Dmg(int l) => 25 + 5 * (l - 1);
            static void Drain(SpellCast cast, Fighter target, float dmg)
            {
                if (target.dead) return;
                SpellFx.Bolt(Body.Center(target), Body.Center(cast.Caster), Blood, 0.07f, 0.5f);
                cast.Damage(target, dmg);
                cast.Heal(cast.Caster, dmg * 0.5f);
            }
        });

        ModContent.School("arcana.necromancy", s =>
        {
            s.Name = "Necromancy";
            s.Icon = Icons.Library("school_necromancy");
            s.Category = SkillCategory.Enchantment;
            s.Runes = SkillType.LifeMagic;
            s.Spells.Add("arcana.raise_dead");
            s.Spells.Add("arcana.life_drain");
            s.Describe = l => "Learn Raise Dead and Life Drain. Replaces Life Magic.";
        });
    }

    // ------------------------------------------------------------------ Ars Mentis

    static void Mentis()
    {
        ModContent.Spell("arcana.telekinesis", s =>
        {
            s.Name = "Telekinesis";
            s.Icon = Icons.Library("spell_telekinesis");
            s.Color = Mind;
            s.Category = SpellCategory.Curse;
            s.Targeting = SpellTargetingType.SingleEnemy;
            s.LooksLike = SpellType.Feedback;
            s.Cooldown = new[] { 20f, 18f, 16f, 14f, 12f };
            s.ManaCost = new[] { 25f };
            s.Range = new[] { 50f };
            s.Describe = l => $"Lifts an enemy helpless into the air for {Hold(l):0.#} seconds, then slams it down for {Slam(l)} damage, staggering everyone it lands near.";
            s.OnCast = (c, cast) =>
            {
                var target = cast.Target;
                if (target is null) return;
                float hold = Hold(c.Level);
                cast.Status(target, StatusType.Levitation, hold);
                cast.Status(target, StatusType.Stunned, hold + 0.5f);
                SpellFx.Bolt(cast.Origin, Body.Center(target), Mind, 0.05f, hold);
                cast.After(hold, x =>
                {
                    var p = Body.Feet(target);
                    SpellFx.Ring(p, 4f, Mind, 0.6f);
                    SpellFx.Flash(p, Mind, 3f, 0.4f);
                    x.Damage(target, Slam(c.Level));
                    foreach (var e in x.EnemiesNear(p, 4f)) if (e != target) x.Status(e, StatusType.Stunned, 1f);
                });
            };
            static float Hold(int l) => 1.5f + 0.25f * (l - 1);
            static float Slam(int l) => 45 + 10 * (l - 1);
        });

        ModContent.Spell("arcana.kinetic_ward", s =>
        {
            s.Name = "Kinetic Ward";
            s.Icon = Icons.Library("spell_kinetic_ward");
            s.Color = Mind;
            s.Category = SpellCategory.Buff;
            s.Targeting = SpellTargetingType.Self;
            s.LooksLike = SpellType.Levitate;
            s.Cooldown = new[] { 30f, 28f, 26f, 24f, 22f };
            s.ManaCost = new[] { 25f };
            s.Range = new[] { 6f };
            s.Describe = l => $"A shell of force turns projectiles aside for {Secs(l)} seconds, and its first pulse stuns enemies within 6 metres.";
            s.OnCast = (c, cast) =>
            {
                var p = Body.Feet(cast.Caster);
                cast.Status(cast.Caster, StatusType.MissileShield, Secs(c.Level));
                SpellFx.Ring(p, 6f, Mind, 0.7f);
                foreach (var e in cast.EnemiesNear(p, 6f)) cast.Status(e, StatusType.Stunned, 1.5f);
            };
            static float Secs(int l) => 6 + 1 * (l - 1);
        });

        ModContent.School("arcana.mentis", s =>
        {
            s.Name = "Ars Mentis";
            s.Icon = Icons.Library("school_mind");
            s.Category = SkillCategory.Enchantment;
            s.Runes = SkillType.MirrorMagic;
            s.Spells.Add("arcana.telekinesis");
            s.Spells.Add("arcana.kinetic_ward");
            s.Describe = l => "Learn Telekinesis and Kinetic Ward. Replaces Mirror Magic.";
        });
    }

    // ------------------------------------------------------------------ Ars Bellica

    static void Bellica()
    {
        ModContent.Spell("arcana.flak_shot", s =>
        {
            s.Name = "Flak Shot";
            s.Icon = Icons.Library("spell_flak_shot");
            s.Color = Fire;
            s.Category = SpellCategory.MinorAttack;
            s.Targeting = SpellTargetingType.Ray;
            s.LooksLike = SpellType.Cobbleshot;
            s.Cooldown = new[] { 9f, 8.5f, 8f, 7.5f, 7f };
            s.ManaCost = new[] { 15f };
            s.Range = new[] { 50f };
            s.Describe = l => $"Fires {Shots(l)} stones in a spread.";
            s.OnCast = (c, cast) =>
            {
                int n = Shots(c.Level);
                for (int i = 0; i < n; i++)
                {
                    float yaw = (i - (n - 1) / 2f) * 7f;
                    cast.SpawnProjectile(cast.Origin, Quaternion.AngleAxis(yaw, Vector3.up) * cast.Direction);
                }
            };
            static int Shots(int l) => 3 + (l - 1) / 2;
        });

        ModContent.Spell("arcana.air_strike", s =>
        {
            s.Name = "Air Strike";
            s.Icon = Icons.Library("spell_air_strike");
            s.Color = Fire;
            s.Category = SpellCategory.MajorAttack;
            s.Targeting = SpellTargetingType.GroundCircle;
            s.LooksLike = SpellType.Meteor;
            s.Cooldown = new[] { 50f, 47f, 44f, 41f, 38f };
            s.ManaCost = new[] { 60f };
            s.Range = new[] { 40f };
            s.FireDelayTicks = 0;
            s.Describe = l => $"Calls down {Bombs(l)} impacts across a 7 metre circle, {Dmg(l)} damage each.";
            s.OnCast = (c, cast) =>
            {
                var p = cast.Point;
                SpellFx.Ring(p, 7f, Fire, 1f);
                // The same scatter on every machine: seeded from the target point.
                var rnd = new System.Random((int)(p.x * 31 + p.z * 17));
                for (int i = 0; i < Bombs(c.Level); i++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rnd.NextDouble()) * 7f;
                    var at = p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    cast.After(0.8f + i * 0.25f, x =>
                    {
                        if (!x.SpawnArea(at, Vector3.down, Dmg(c.Level)))
                            foreach (var e in x.EnemiesNear(at, 2.5f)) x.Damage(e, Dmg(c.Level));
                        SpellFx.Flash(at, Fire, 3f, 0.4f);
                    });
                }
            };
            static int Bombs(int l) => 5 + (l - 1);
            static float Dmg(int l) => 35 + 5 * (l - 1);
        });

        ModContent.School("arcana.bellica", s =>
        {
            s.Name = "Ars Bellica";
            s.Icon = Icons.Library("school_war");
            s.Category = SkillCategory.Evocation;
            s.Runes = SkillType.EarthMagic;
            s.Spells.Add("arcana.flak_shot");
            s.Spells.Add("arcana.air_strike");
            s.Describe = l => "Learn Flak Shot and Air Strike. Replaces Ars Terra.";
        });
    }

    // ------------------------------------------------------------------ Ars Pestis

    static void Pestis()
    {
        ModContent.Spell("arcana.plague_cloud", s =>
        {
            s.Name = "Plague Cloud";
            s.Icon = Icons.Library("spell_plague_cloud");
            s.Color = Plague;
            s.Category = SpellCategory.MinorAttack;
            s.Targeting = SpellTargetingType.Ray;
            s.LooksLike = SpellType.AcidSpray;
            s.Cooldown = new[] { 9f, 8.5f, 8f, 7.5f, 7f };
            s.ManaCost = new[] { 15f };
            s.Range = new[] { 20f };
            s.Describe = l => $"Breathes a sickly cloud. Enemies caught in it take {Hit(l)} damage and are poisoned for {Dot(l)} damage a second for 4 seconds.";
            s.OnCast = (c, cast) => cast.SpawnArea(cast.Origin, cast.Direction, Hit(c.Level));
            s.OnAreaHit = (c, target, at) =>
            {
                if (GM.isSameTeam(c.Caster.faction, target.faction)) return;
                c.HitArea(target, Hit(c.Level));
                Status.Give(target, StatusType.Poison, 4f, Dot(c.Level), c.Caster);
            };
            static float Hit(int l) => 10 + 2 * (l - 1);
            static float Dot(int l) => 8 + 2 * (l - 1);
        });

        ModContent.Spell("arcana.pestilence", s =>
        {
            s.Name = "Pestilence";
            s.Icon = Icons.Library("spell_pestilence");
            s.Color = Plague;
            s.Category = SpellCategory.MajorAttack;
            s.Targeting = SpellTargetingType.GroundCircle;
            s.LooksLike = SpellType.ThornGrowth;
            s.Cooldown = new[] { 50f, 47f, 44f, 41f, 38f };
            s.ManaCost = new[] { 60f };
            s.Range = new[] { 30f };
            s.FireDelayTicks = 0;
            s.Describe = l => $"Infects every enemy within 7 metres: poisoned for {Dot(l)} a second and slowed for 8 seconds. After 4 seconds the sickness jumps to enemies near the infected.";
            s.OnCast = (c, cast) =>
            {
                var p = cast.Point;
                SpellFx.Ring(p, 7f, Plague, 1.2f);
                var infected = cast.EnemiesNear(p, 7f);
                foreach (var e in infected) Infect(cast, e, c.Level);
                cast.After(4f, x =>
                {
                    foreach (var carrier in infected.Where(f => !f.dead))
                        foreach (var e in x.EnemiesNear(Body.Feet(carrier), 4f).Where(f => !infected.Contains(f)))
                        {
                            SpellFx.Bolt(Body.Center(carrier), Body.Center(e), Plague, 0.04f, 0.5f);
                            Infect(x, e, c.Level);
                        }
                });
            };
            static float Dot(int l) => 15 + 3 * (l - 1);
            static void Infect(SpellCast cast, Fighter e, int level)
            {
                cast.Status(e, StatusType.Poison, 8f, Dot(level));
                cast.Status(e, StatusType.Slowed, 8f, 0.4f);
            }
        });

        ModContent.School("arcana.pestis", s =>
        {
            s.Name = "Ars Pestis";
            s.Icon = Icons.Library("school_plague");
            s.Category = SkillCategory.Evocation;
            s.Runes = SkillType.AlchemicalMagic;
            s.Spells.Add("arcana.plague_cloud");
            s.Spells.Add("arcana.pestilence");
            s.Describe = l => "Learn Plague Cloud and Pestilence. Replaces Ars Alchemia.";
        });
    }
}
