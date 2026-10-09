using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronstrikeApi.Content;

/// <summary>
/// A spell a mod adds to the game. Casters get it from a <see cref="CustomSchool"/>, draw it on the
/// wand's rune grid like the game's own spells, aim it with the game's indicators and cast it with a
/// flick. Register one with <see cref="ModContent.Spell"/>.
/// </summary>
/// <remarks>
/// <para><b>Where the code runs.</b> <see cref="OnCast"/> runs on every machine at the same moment
/// (the game's "synced" spell events), so damage, healing and status effects applied through
/// <see cref="SpellCast"/> happen everywhere alike. <see cref="Amount"/> runs once, on the caster's
/// machine, before the cast is sent.</para>
/// <para><b>Looks.</b> A custom spell borrows the aiming indicators, preview and projectile or area
/// effect of one of the game's spells (<see cref="LooksLike"/>), so it looks at home without any art.</para>
/// </remarks>
public sealed class CustomSpell
{
    internal CustomSpell(string key) { Key = key; }

    /// <summary>The key you registered it under, e.g. <c>mymod.chain_lightning</c>.</summary>
    public string Key { get; }

    /// <summary>The byte id the game knows it by. Assigned when the game starts; 0 before that.</summary>
    public int Id { get; internal set; }

    /// <summary>The spell's id as the game's own type.</summary>
    public SpellType Type => (SpellType)Id;

    /// <summary>The school that teaches it; set when a <see cref="CustomSchool"/> lists it.</summary>
    public CustomSchool School { get; internal set; }

    // ------------------------------------------------------------------ what it is

    /// <summary>The name shown on the rune grid.</summary>
    public string Name { get; set; } = "Unnamed Spell";

    /// <summary>The description for a given level (1 to 5).</summary>
    public Func<int, string> Describe { get; set; } = _ => "";

    /// <summary>The icon on the rune grid. White on transparent; the game tints it.</summary>
    public Icon Icon { get; set; }

    /// <summary>Its colour on the rune grid and in the casting effects.</summary>
    public Color Color { get => color ?? new Color(0.6f, 0.8f, 1f, 1f); set => color = value; }
    Color? color;     // Unity's Color is IL2CPP-backed: never built until the game asks for it

    /// <summary>What kind of spell it is, for skills that boost a kind (MinorAttack, MajorAttack, Buff, Curse).</summary>
    public SpellCategory Category { get; set; } = SpellCategory.MinorAttack;

    /// <summary>How it is aimed: along the wand (Ray), at the ground (GroundCircle), at yourself (Self),
    /// or at one fighter (SingleEnemy, SingleAlly, SingleNonSelf).</summary>
    public SpellTargetingType Targeting { get; set; } = SpellTargetingType.Ray;

    /// <summary>The game spell whose indicators, preview and projectile or area effect it borrows.</summary>
    public SpellType LooksLike { get; set; } = SpellType.Fireball;

    /// <summary>Seconds between casts, per level (the last value repeats). Entries 6 to 10, if given, are the
    /// enhanced stages; otherwise an enhanced stage uses its plain value.</summary>
    public float[] Cooldown { get; set; } = { 10f };

    /// <summary>Mana per cast, per level (the casting bar holds 100).</summary>
    public float[] ManaCost { get; set; } = { 20f };

    /// <summary>How far it reaches, in metres, per level.</summary>
    public float[] Range { get; set; } = { 50f };

    /// <summary>Simulation ticks between the flick and the effect (the game uses 0 to 9).</summary>
    public int FireDelayTicks { get; set; } = 6;

    // ------------------------------------------------------------------ what it does

    /// <summary>
    /// For spells aimed at a fighter: the amount the cast carries (usually its damage), worked out on
    /// the caster's machine and sent with the cast. Reach it as <see cref="SpellCast.Amount"/>.
    /// </summary>
    public Func<SpellContext, Fighter, float> Amount { get; set; }

    /// <summary>The spell went off. Runs on every machine; do the spell's work here.</summary>
    public Action<SpellContext, SpellCast> OnCast { get; set; }

    /// <summary>The area effect it spawned (<see cref="SpellCast.SpawnArea"/>) touched a fighter. Runs
    /// on the machine with authority over the hit; apply damage with <see cref="SpellContext.HitArea"/>.</summary>
    public Action<SpellContext, Fighter, Vector3> OnAreaHit { get; set; }

    /// <summary>A fight started while the caster knows the spell. Synced.</summary>
    public Action<SpellContext> OnEncounterStart { get; set; }

    internal string Problem()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Name is empty";
        if (!Valid(Cooldown) || !Valid(ManaCost) || !Valid(Range)) return "Cooldown, ManaCost and Range need at least one value, none negative";
        if (FireDelayTicks < 0 || FireDelayTicks > 60) return "FireDelayTicks must be 0 to 60";
        if (Targeting == SpellTargetingType.None) return "Targeting cannot be None";
        return null;
    }

    static bool Valid(float[] v)
    {
        if (v == null || v.Length == 0) return false;
        foreach (var x in v) if (x < 0 || float.IsNaN(x)) return false;
        return true;
    }

    // Levels 1-5 are the stages; 6-10 the same stages enhanced. An array that stops before an enhanced
    // level gives that stage's plain value.
    internal static float At(float[] v, int level)
    {
        if (level > 5 && v.Length < level) level -= 5;
        return v[Math.Clamp(level, 1, v.Length) - 1];
    }
}

/// <summary>
/// A magic school: an upgrade casters pick on the upgrade screen, which teaches two spells. It is the
/// game's own kind of school skill, so it levels, syncs and shows like Ars Ignis or Shadow Magic.
/// Register one with <see cref="ModContent.School"/>.
/// </summary>
/// <remarks>
/// <para><b>Runes.</b> The rune grid has a fixed set of hand-made shapes, one branch per game school,
/// and only paths whose spells the player knows can be drawn. A custom school is drawn on the branch of
/// a game school <i>the player does not have</i>: its first spell with that school's first shape
/// (three strokes), its second with the second (four), and its own names and icons on the grid. The
/// branch is picked per player while playing, preferring <see cref="Runes"/>; if the player later
/// takes that game school, the custom school moves to another free branch. Nothing is replaced and
/// any schools can be held together.</para>
/// </remarks>
public sealed class CustomSchool
{
    internal CustomSchool(string key) { Key = key; }

    /// <summary>The key you registered it under.</summary>
    public string Key { get; }

    /// <summary>Its skill id. Assigned when the game starts; 0 before that.</summary>
    public int Id { get; internal set; }

    /// <summary>Its id as the game's own type.</summary>
    public SkillType Type => (SkillType)Id;

    /// <summary>The name on the upgrade card (the game adds the level numeral).</summary>
    public string Name { get; set; } = "Unnamed School";

    /// <summary>The card text for a level; by default a line naming its spells.</summary>
    public Func<int, string> Describe { get; set; }

    /// <summary>The card icon.</summary>
    public Icon Icon { get; set; }

    /// <summary>The upgrade-screen category: Evocation (attack schools) or Enchantment (buffs and curses).</summary>
    public SkillCategory Category { get; set; } = SkillCategory.Evocation;

    /// <summary>The game school whose rune branch it prefers to be drawn on, e.g. <c>SkillType.StormMagic</c>.
    /// Used when the player does not have that school; otherwise another free branch is used.</summary>
    public SkillType Runes { get; set; } = SkillType.StormMagic;

    /// <summary>How often it is offered compared with a game skill (1 = as often; 0 = never).</summary>
    public float OfferWeight { get; set; } = 1f;

    /// <summary>The highest stage; 1 to 5 (the game's schools have 3).</summary>
    public int MaxLevel { get; set; } = 5;

    /// <summary>Whether the upgrade screen may offer it enhanced, as it does the game's skills
    /// ("Lightning Magic II - Enhanced"). Its spells then see <see cref="SpellContext.Enhanced"/>.</summary>
    public bool AllowsEnhanced { get; set; } = true;

    /// <summary>The keys of its two spells, first (three-stroke) then second (four-stroke).</summary>
    public List<string> Spells { get; } = new();

    internal static readonly SkillType[] RuneSchools =
    {
        SkillType.EarthMagic, SkillType.StormMagic, SkillType.FireMagic, SkillType.IceMagic, SkillType.LightMagic,
        SkillType.AlchemicalMagic, SkillType.ForestMagic, SkillType.StarMagic, SkillType.ShadowMagic, SkillType.DivineMagic,
        SkillType.LuckMagic, SkillType.IronMagic, SkillType.MirrorMagic, SkillType.WindMagic, SkillType.LifeMagic, SkillType.CrystalMagic,
    };

    internal string Problem()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Name is empty";
        if (MaxLevel < 1 || MaxLevel > 5) return "MaxLevel must be 1 to 5";
        if (OfferWeight < 0 || float.IsNaN(OfferWeight)) return "OfferWeight must be 0 or more";
        if (Spells.Count != 2) return "a school teaches exactly two spells";
        if (Array.IndexOf(RuneSchools, Runes) < 0) return $"Runes must be one of the game's magic schools, not {Runes}";
        return null;
    }
}

/// <summary>What a <see cref="CustomSpell"/> hook gets: the caster, the level, and a place for state.</summary>
public sealed class SpellContext
{
    internal SpellContext() { }

    /// <summary>The spell's definition.</summary>
    public CustomSpell Spell { get; internal set; }

    /// <summary>The game's spell component on the caster.</summary>
    public Spell GameSpell { get; internal set; }

    /// <summary>Who knows the spell.</summary>
    public Fighter Caster { get; internal set; }

    /// <summary>The spell's level: its school's, 1 to 5, or 6 to 10 when enhanced.</summary>
    public int Level { get; internal set; }

    /// <summary>The stage, 1 to 5, whether or not it is enhanced.</summary>
    public int Stage => Level > 5 ? Level - 5 : Level;

    /// <summary>True when the school was taken enhanced (an enhanced card on the upgrade screen).</summary>
    public bool Enhanced => Level > 5;

    /// <summary>True if the caster is this machine's player.</summary>
    public bool IsLocal => Gameplay.Players.IsLocal(Caster);

    /// <summary>Your own state for this spell on this caster.</summary>
    public Dictionary<string, object> State { get; } = new();

    /// <summary>Picks the value for the current stage (stages past the end use the last value).</summary>
    /// <param name="values">One value per stage.</param>
    public float PerLevel(params float[] values)
        => values == null || values.Length == 0 ? 0f : values[Math.Clamp(Stage, 1, values.Length) - 1];

    internal Weapon Weapon;

    /// <summary>
    /// Damages a fighter hit by this spell's area effect (from <see cref="CustomSpell.OnAreaHit"/>).
    /// Goes through the game's damage rules, so armour, skills and damage numbers apply.
    /// </summary>
    /// <param name="target">The fighter.</param>
    /// <param name="damage">Base damage before skills and armour.</param>
    public void HitArea(Fighter target, float damage) => SpellEffects.Hit(Caster, Weapon, target, damage, Spell.Type);
}
