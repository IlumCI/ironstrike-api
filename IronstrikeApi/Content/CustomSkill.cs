using System;
using System.Collections.Generic;

namespace IronstrikeApi.Content;

/// <summary>
/// A skill a mod adds to the game. The game treats it like its own: it is offered on the upgrade
/// screen between fights, levels up from I to V, is synced to other players and shows its name, text
/// and icon on the cards. Register one with <see cref="ModContent.Skill"/>.
/// </summary>
/// <remarks>
/// <para>Every hook is optional. Leave out what your skill does not use.</para>
/// <para><b>Bonuses add up.</b> The game asks every skill a fighter has for its bonus to a stat and
/// sums the answers: return <c>0.2f</c> for +20%, <c>0</c> for nothing. The "Bonus" hooks below work
/// that way, exactly like the game's own skills (verified: every base value is 0).</para>
/// <para>Hooks run on the machine named in their description. "Local" means the machine of the
/// player who owns the skill; "Synced" means every machine.</para>
/// </remarks>
public sealed class CustomSkill
{
    internal CustomSkill(string key) { Key = key; }

    /// <summary>The key you registered it under, e.g. <c>mymod.berserk</c>.</summary>
    public string Key { get; }

    /// <summary>The byte id the game knows it by. Assigned when the game starts; 0 before that.</summary>
    public int Id { get; internal set; }

    /// <summary>The skill's id as the game's own type.</summary>
    public SkillType Type => (SkillType)Id;

    // ------------------------------------------------------------------ what it is

    /// <summary>The name on the card, without the level numeral (the game adds "I", "II"...).</summary>
    public string Name { get; set; } = "Unnamed Skill";

    /// <summary>The card text for a given level (1 to 5, 6 to 10 enhanced). Plain text; the game's
    /// keyword highlighting is not available to custom skills.</summary>
    public Func<int, string> Describe { get; set; } = _ => "";

    /// <summary>The card icon. White on transparent; the game tints it.</summary>
    public Icon Icon { get; set; }

    /// <summary>Which class is offered it; null offers it to every class.</summary>
    public SkillClass? Class { get; set; }

    /// <summary>The upgrade-screen category it is offered under (Technique, Valor, Agility...).</summary>
    public SkillCategory Category { get; set; } = SkillCategory.Technique;

    /// <summary>The highest level it can be raised to; 1 to 5. Default 5.</summary>
    public int MaxLevel { get; set; } = 5;

    /// <summary>
    /// How often it is offered compared with one of the game's own skills (1 = as often). 0 never
    /// offers it; give it with <see cref="ModContent.GiveSkill"/> instead.
    /// </summary>
    public float OfferWeight { get; set; } = 1f;

    /// <summary>Whether the upgrade screen may offer it enhanced (see <see cref="SkillContext.Enhanced"/>).
    /// Off by default: say what enhanced does in <see cref="Describe"/> (levels 6 to 10) before turning it on.</summary>
    public bool AllowsEnhanced { get; set; }

    /// <summary>Skills that cannot be held together with this one (keys of custom skills).</summary>
    public List<string> ExclusiveWith { get; } = new();

    // ------------------------------------------------------------------ events

    /// <summary>The fighter gained the skill (not on later levels; read <see cref="SkillContext.Level"/>). Synced.</summary>
    public Action<SkillContext> OnAdded { get; set; }

    /// <summary>The skill was taken away. Synced.</summary>
    public Action<SkillContext> OnRemoved { get; set; }

    /// <summary>Every simulation tick while the fighter has the skill. Keep it cheap.</summary>
    public Action<SkillContext> OnTick { get; set; }

    /// <summary>A fight started. Synced.</summary>
    public Action<SkillContext> OnEncounterStart { get; set; }

    /// <summary>The fighter landed a hit. Local to the attacker.</summary>
    public Action<SkillContext, HitInfo> OnOutgoingHit { get; set; }

    /// <summary>The fighter was hit. Local to the victim.</summary>
    public Action<SkillContext, HitInfo> OnIncomingHit { get; set; }

    /// <summary>Someone died. Local.</summary>
    public Action<SkillContext, Fighter> OnFighterDeath { get; set; }

    /// <summary>The fighter started a dash. Local.</summary>
    public Action<SkillContext> OnDash { get; set; }

    /// <summary>The fighter blocked a melee attack. Local.</summary>
    public Action<SkillContext, Fighter, Skill.BlockType> OnMeleeBlock { get; set; }

    /// <summary>The fighter blocked a projectile. Local.</summary>
    public Action<SkillContext, Weapon, Projectile> OnProjectileBlock { get; set; }

    /// <summary>The fighter cast a spell. Local.</summary>
    public Action<SkillContext, Spell> OnSpellCast { get; set; }

    // ------------------------------------------------------------------ bonuses (summed with other skills)

    /// <summary>Bonus to outgoing damage for a hit, as a fraction (0.2 = +20%).</summary>
    public Func<SkillContext, HitInfo, float> OutgoingDamageBonus { get; set; }
    /// <summary>Flat damage added to outgoing hits.</summary>
    public Func<SkillContext, HitInfo, float> OutgoingDamageFlat { get; set; }
    /// <summary>Bonus to the base of outgoing damage, as a fraction.</summary>
    public Func<SkillContext, HitInfo, float> OutgoingDamageBaseBonus { get; set; }
    /// <summary>Bonus to incoming damage, as a fraction (negative reduces it: -0.2 = 20% less).</summary>
    public Func<SkillContext, HitInfo, float> IncomingDamageBonus { get; set; }
    /// <summary>Flat damage added to incoming hits (negative reduces them).</summary>
    public Func<SkillContext, HitInfo, float> IncomingDamageFlat { get; set; }
    /// <summary>Bonus to all incoming damage after everything else, as a fraction.</summary>
    public Func<SkillContext, HitInfo, float> TotalIncomingDamageBonus { get; set; }
    /// <summary>Bonus to all outgoing damage after everything else, as a fraction.</summary>
    public Func<SkillContext, HitInfo, float> TotalOutgoingDamageBonus { get; set; }
    /// <summary>Bonus to damage dealt to armour, as a fraction.</summary>
    public Func<SkillContext, HitInfo, float> OutgoingArmorDamageBonus { get; set; }
    /// <summary>Flat damage dealt to armour.</summary>
    public Func<SkillContext, HitInfo, float> OutgoingArmorDamageFlat { get; set; }
    /// <summary>Bonus to armour damage taken, as a fraction.</summary>
    public Func<SkillContext, HitInfo, float> IncomingArmorDamageBonus { get; set; }

    /// <summary>Bonus to move speed, as a fraction.</summary>
    public Func<SkillContext, float> MoveSpeedBonus { get; set; }
    /// <summary>Extra jumps.</summary>
    public Func<SkillContext, float> ExtraJumps { get; set; }
    /// <summary>Bonus to jump height, as a fraction.</summary>
    public Func<SkillContext, float> JumpVelocityBonus { get; set; }
    /// <summary>Bonus to jump distance, as a fraction.</summary>
    public Func<SkillContext, float> JumpHorizontalBonus { get; set; }
    /// <summary>Dash distance added.</summary>
    public Func<SkillContext, float> DashDistanceFlat { get; set; }
    /// <summary>Dash cooldown added, in seconds (negative shortens it).</summary>
    public Func<SkillContext, float> DashCooldownFlat { get; set; }
    /// <summary>Bonus to reload time, as a fraction (negative reloads faster).</summary>
    public Func<SkillContext, float> ReloadTimeBonus { get; set; }
    /// <summary>Bonus to arrow gravity, as a fraction.</summary>
    public Func<SkillContext, float> ArrowGravityBonus { get; set; }
    /// <summary>Extra range for ironstrikes (weakspots) to appear in.</summary>
    public Func<SkillContext, float> WeakspotRangeBonus { get; set; }
    /// <summary>Extra guard damage when heavy-blocking.</summary>
    public Func<SkillContext, float> HeavyBlockGuardDamageBonus { get; set; }
    /// <summary>Bonus to visibility, as a fraction (negative is stealthier).</summary>
    public Func<SkillContext, float> VisibilityBonus { get; set; }

    /// <summary>Mana cost discount.</summary>
    public Func<SkillContext, float> SpellDiscount { get; set; }
    /// <summary>Bonus to mana regeneration rate.</summary>
    public Func<SkillContext, float> ManaRegenRateBonus { get; set; }
    /// <summary>Flat mana regeneration.</summary>
    public Func<SkillContext, float> ManaRegenFlat { get; set; }
    /// <summary>Bonus to the mana pool threshold.</summary>
    public Func<SkillContext, float> ManaMaxBonus { get; set; }
    /// <summary>Bonus to spell cooldowns for a spell category, as a fraction (negative is faster).</summary>
    public Func<SkillContext, SpellCategory, float> SpellCooldownBonus { get; set; }
    /// <summary>Bonus to spell damage for a spell category, as a fraction.</summary>
    public Func<SkillContext, SpellCategory, float> SpellDamageBonus { get; set; }
    /// <summary>Bonus to spell duration for a spell category, as a fraction.</summary>
    public Func<SkillContext, SpellCategory, float> SpellDurationBonus { get; set; }

    /// <summary>Whether enemies may target the fighter. Leave unset unless you mean it: an untargetable
    /// player leaves the bots with nothing to do, and they stand still.</summary>
    public Func<SkillContext, bool> Targetable { get; set; }

    internal string Problem()
    {
        if (MaxLevel < 1 || MaxLevel > 5) return "MaxLevel must be 1 to 5";
        if (OfferWeight < 0 || float.IsNaN(OfferWeight)) return "OfferWeight must be 0 or more";
        if (string.IsNullOrWhiteSpace(Name)) return "Name is empty";
        return null;
    }
}

/// <summary>What a <see cref="CustomSkill"/> hook gets: the fighter, the level, and a place to keep state.</summary>
public sealed class SkillContext
{
    internal SkillContext() { }

    /// <summary>The skill's definition.</summary>
    public CustomSkill Skill { get; internal set; }

    /// <summary>The game's skill component on the fighter.</summary>
    public Skill GameSkill { get; internal set; }

    /// <summary>Who has the skill.</summary>
    public Fighter Fighter { get; internal set; }

    /// <summary>The skill's level, 1 to 5 (6 to 10 when enhanced).</summary>
    public int Level { get; internal set; }

    /// <summary>The stage, 1 to 5, whether or not it is enhanced.</summary>
    public int Stage => Level > 5 ? Level - 5 : Level;

    /// <summary>True when the skill was taken enhanced.</summary>
    public bool Enhanced => Level > 5;

    /// <summary>True if the fighter is this machine's player.</summary>
    public bool IsLocal => Gameplay.Players.IsLocal(Fighter);

    /// <summary>Your own per-fighter state for this skill; lives until the skill is removed.</summary>
    public Dictionary<string, object> State { get; } = new();

    /// <summary>Picks the value for the current level: <c>ctx.PerLevel(0.1f, 0.15f, 0.2f, 0.25f, 0.3f)</c>.
    /// Levels past the end use the last value.</summary>
    /// <param name="values">One value per level, starting at level 1.</param>
    public float PerLevel(params float[] values)
    {
        if (values == null || values.Length == 0) return 0f;
        return values[Math.Clamp(Stage, 1, values.Length) - 1];
    }
}
