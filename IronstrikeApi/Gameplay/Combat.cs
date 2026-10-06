using System;
using HarmonyLib;

namespace IronstrikeApi.Gameplay;

/// <summary>
/// One hit being resolved, passed to <see cref="GameEvents.Damage"/>. Handlers see the amount left by
/// earlier handlers. Changes apply only where <see cref="Safety.GameplayAllowed"/>.
/// The object is reused for the next hit: read what you need inside the handler, do not keep it.
/// </summary>
public sealed class DamageEvent
{
    float amount;

    /// <summary>The game's hit record: weapon, weakspot, projectile, hit type.</summary>
    public HitInfo Hit { get; internal set; }
    /// <summary>Who dealt the hit, or null (environment, status effects).</summary>
    public Fighter Attacker => Hit?.attackingFighter;
    /// <summary>Who was hit.</summary>
    public Fighter Victim => Hit?.hitFighter;
    /// <summary>How the hit landed: melee quality, projectile, spell, status effect.</summary>
    public HitInfo.HitType Type => Hit != null ? Hit.hitType : HitInfo.HitType.None;
    /// <summary>The damage the game computed, before any mod.</summary>
    public float Original { get; internal set; }
    /// <summary>True if the local player dealt the hit.</summary>
    public bool ByLocalPlayer => Players.IsLocal(Attacker);
    /// <summary>True if the local player was hit.</summary>
    public bool OnLocalPlayer => Players.IsLocal(Victim);
    /// <summary>True once a handler has called <see cref="Cancel"/>.</summary>
    public bool Cancelled { get; private set; }

    /// <summary>The damage that will be dealt. Setting it is ignored where gameplay changes are off.</summary>
    public float Amount
    {
        get => amount;
        set
        {
            if (!Safety.Check(null, "changing damage")) return;
            amount = value < 0f || float.IsNaN(value) ? 0f : value;
        }
    }

    /// <summary>Deals no damage for this hit. Ignored where gameplay changes are off.</summary>
    public void Cancel()
    {
        if (!Safety.Check(null, "cancelling damage")) return;
        Cancelled = true;
        amount = 0f;
    }

    internal void Reset(HitInfo hit, float original)
    {
        Hit = hit; Original = original; amount = original; Cancelled = false;
    }
}

/// <summary>A projectile that was just spawned, passed to <see cref="GameEvents.ProjectileSpawned"/>.</summary>
/// <remarks>
/// Fields on <see cref="Projectile"/> (<c>speed</c>, <c>gravity</c>, <c>maxLifeTime</c>,
/// <c>damage</c>...) may be changed here, but check <see cref="Safety.GameplayAllowed"/> first.
/// Do not change speed or gravity on a homing projectile (<see cref="IsHoming"/>): the game steers
/// those every tick and overshoots.
/// </remarks>
public sealed class ProjectileEvent
{
    /// <summary>The projectile.</summary>
    public Projectile Projectile { get; internal set; }
    /// <summary>Who fired it.</summary>
    public Fighter Owner { get; internal set; }
    /// <summary>The spell it belongs to, or null for arrows and bolts.</summary>
    public SpellType? Spell { get; internal set; }
    /// <summary>True if the local player fired it.</summary>
    public bool ByLocalPlayer => Players.IsLocal(Owner);
    /// <summary>True if the game steers it towards a target.</summary>
    public bool IsHoming
    {
        get
        {
            var p = Projectile;
            return p != null && (p.forceSeeking || p.percentSeeking > 0f || p.target != null);
        }
    }
}

[HarmonyPatch]
internal static class DamageHooks
{
    static readonly DamageEvent shared = new();
    static bool inside;

    // Fighter.CalculateDamage is static and verified live (trainer). One event object is reused:
    // this runs for every hit.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalculateDamage))]
    static void Postfix(HitInfo hitInfo, ref float __result)
    {
        Events.Hooks.Live("Fighter.CalculateDamage");
        if (!GameEvents.AnyDamage || hitInfo == null || inside) return;
        inside = true;                  // a handler that deals damage itself must not recurse
        try
        {
            shared.Reset(hitInfo, __result);
            GameEvents.RaiseDamage(shared);
            if (Safety.GameplayAllowed) __result = shared.Cancelled ? 0f : shared.Amount;
        }
        finally { inside = false; }
    }
}

[HarmonyPatch]
internal static class ProjectileHooks
{
    static void Raise(Projectile p, Fighter parent, SpellType? spell)
    {
        Events.Hooks.Live("Projectile.SetTypes");
        if (p == null) return;
        GameEvents.RaiseProjectile(new ProjectileEvent { Projectile = p, Owner = parent, Spell = spell });
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.SetTypes), new[] { typeof(Fighter) })]
    static void Spawned(Projectile __instance, Fighter parent) => Raise(__instance, parent, null);

    // The full signature. Dropping spellType from an IL2CPP overload patch hard-crashed the game
    // on every spell projectile (trainer, AGENTS.md §7.4).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.SetTypes), new[] { typeof(Fighter), typeof(SpellType) })]
    static void SpawnedSpell(Projectile __instance, Fighter parent, SpellType spellType)
        => Raise(__instance, parent, spellType);
}
