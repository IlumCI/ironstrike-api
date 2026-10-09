using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace IronstrikeApi.Content;

// The one Skill class the API registers into the game (Il2CppInterop class injection). Every custom
// skill is a template GameObject carrying this component, filed in the game's own skill dictionary
// under its byte id; the game clones, levels, syncs and shows it like its own skills, and calls the
// virtual hooks below, which forward to the CustomSkill registered for that id.
//
// Rules this class lives by, all from IL2CPP:
//  - Never call base.X() from an override. The interop's base call dispatches virtually through the
//    Il2Cpp vtable, which leads straight back here: infinite recursion. The base implementations are
//    known (every Calc* returns 0, IsTargetable true, events do nothing), so the neutral values are
//    written out instead.
//  - A clone (Object.Instantiate) gets a fresh managed wrapper with no C# fields. The definition is
//    found from the native skillType field, which the clone keeps; per-fighter state lives in
//    SkillHost, keyed by the component's native pointer.
//
// Nested on purpose: BepInEx discovers plugins by walking the base classes of every TOP-LEVEL type
// with Cecil, and its resolver finds the game's native GameAssembly.dll (in the working directory)
// before the interop one. A top-level class deriving from a game type makes BepInEx throw
// BadImageFormatException and silently skip the whole plugin. Nested types are never examined.
internal static partial class Injected
{
    internal class ApiSkill : Skill
    {
        public ApiSkill(IntPtr ptr) : base(ptr) { }

        public ApiSkill() : base(Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorPointer<ApiSkill>())
            => Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorBody(this);

        // ---- the game's bookkeeping: nothing to do (no tuning files, no attribute tables)
        public override void Init() { }
        public override void ApplyValuesFrom(Skill other) { }
        public override void Reset() { }
        public override void ManualLateUpdate() { }

        // ---- text
        public override string GetDescription() => SkillHost.Describe(this);
        public override string GetDescription(Il2CppReferenceArray<Il2CppSystem.Object> args) => SkillHost.Describe(this);
        public override string GetDescription(bool useLevel, Il2CppReferenceArray<Il2CppSystem.Object> args) => SkillHost.Describe(this);

        // ---- events
        public override void OnAddToFighter_Synced(Fighter fighter) => SkillHost.Added(this, fighter);
        public override void OnRemoveFromFighter_Synced(Fighter fighter) => SkillHost.Removed(this, fighter);
        public override void ManualFixedUpdate() => SkillHost.Event(this, d => d.OnTick, (h, c) => h(c));
        public override void OnEncounterStart_Synced() => SkillHost.Event(this, d => d.OnEncounterStart, (h, c) => h(c));
        public override void OnOutgoingHit_LocalAuth(HitInfo hitInfo) => SkillHost.Event(this, d => d.OnOutgoingHit, (h, c) => h(c, hitInfo));
        public override void OnIncomingHit_LocalAuth(HitInfo hitInfo) => SkillHost.Event(this, d => d.OnIncomingHit, (h, c) => h(c, hitInfo));
        public override void OnFighterDeath_Local(Fighter f) => SkillHost.Event(this, d => d.OnFighterDeath, (h, c) => h(c, f));
        public override void OnDashStart_LocalAuth() => SkillHost.Event(this, d => d.OnDash, (h, c) => h(c));
        public override void OnMeleeBlock_Local(Fighter bot, Skill.BlockType type) => SkillHost.Event(this, d => d.OnMeleeBlock, (h, c) => h(c, bot, type));
        public override void OnProjectileBlock_Local(Weapon w, Projectile p) => SkillHost.Event(this, d => d.OnProjectileBlock, (h, c) => h(c, w, p));
        public override void OnSpellCast_Local(Spell s, Fighter player) => SkillHost.Event(this, d => d.OnSpellCast, (h, c) => h(c, s));

        // ---- bonuses: every base implementation returns 0
        public override float CalcOutgoingDamagePercentMultiplier(HitInfo h) => SkillHost.Hit(this, d => d.OutgoingDamageBonus, h);
        public override float CalcOutgoingDamageFlat(HitInfo h) => SkillHost.Hit(this, d => d.OutgoingDamageFlat, h);
        public override float CalcOutgoingDamagePercentBase(HitInfo h) => SkillHost.Hit(this, d => d.OutgoingDamageBaseBonus, h);
        public override float CalcIncomingDamagePercentMultiplier(HitInfo h) => SkillHost.Hit(this, d => d.IncomingDamageBonus, h);
        public override float CalcIncomingDamageFlat(HitInfo h) => SkillHost.Hit(this, d => d.IncomingDamageFlat, h);
        public override float CalcTotalIncomingDamagePercentMultiplier(HitInfo h) => SkillHost.Hit(this, d => d.TotalIncomingDamageBonus, h);
        public override float CalcTotalOutgoingDamagePercentMultiplier(HitInfo h) => SkillHost.Hit(this, d => d.TotalOutgoingDamageBonus, h);
        public override float CalcOutgoingArmorDamagePercentMultiplier(HitInfo h) => SkillHost.Hit(this, d => d.OutgoingArmorDamageBonus, h);
        public override float CalcOutgoingArmorDamageFlat(HitInfo h) => SkillHost.Hit(this, d => d.OutgoingArmorDamageFlat, h);
        public override float CalcIncomingArmorDamagePercentMultiplier(HitInfo h) => SkillHost.Hit(this, d => d.IncomingArmorDamageBonus, h);

        public override float CalcMoveSpeedMultiplier() => SkillHost.Value(this, d => d.MoveSpeedBonus);
        public override float CalcJumpsAddition() => SkillHost.Value(this, d => d.ExtraJumps);
        public override float CalcJumpVelocityMultiplier() => SkillHost.Value(this, d => d.JumpVelocityBonus);
        public override float CalcJumpHorizontalMultiplier() => SkillHost.Value(this, d => d.JumpHorizontalBonus);
        public override float CalcDashDistanceFlat() => SkillHost.Value(this, d => d.DashDistanceFlat);
        public override float CalcDashCooldownFlat() => SkillHost.Value(this, d => d.DashCooldownFlat);
        public override float CalcAmmoReloadTimeMultiplier() => SkillHost.Value(this, d => d.ReloadTimeBonus);
        public override float CalcArrowGravityPercentMultiplier() => SkillHost.Value(this, d => d.ArrowGravityBonus);
        public override float CalcWeakspotRangeAddition() => SkillHost.Value(this, d => d.WeakspotRangeBonus);
        public override float CalcHeavyBlockGuardDamageAddition() => SkillHost.Value(this, d => d.HeavyBlockGuardDamageBonus);
        public override float CalcVisibilityPercentMultiplier() => SkillHost.Value(this, d => d.VisibilityBonus);
        public override float CalcSpellDiscount() => SkillHost.Value(this, d => d.SpellDiscount);
        public override float CalcManaRegenRate() => SkillHost.Value(this, d => d.ManaRegenRateBonus);
        public override float CalcManaRegenFlat() => SkillHost.Value(this, d => d.ManaRegenFlat);
        public override float CalcManaMaxThresh() => SkillHost.Value(this, d => d.ManaMaxBonus);
        public override float CalcSpellCooldownPercent(SpellCategory c) => SkillHost.Category(this, d => d.SpellCooldownBonus, c);
        public override float CalcSpellDamagePercent(SpellCategory c) => SkillHost.Category(this, d => d.SpellDamageBonus, c);
        public override float CalcSpellDurationPercent(SpellCategory c) => SkillHost.Category(this, d => d.SpellDurationBonus, c);

        public override bool IsTargetable() => SkillHost.Targetable(this);
    }
}

// Dispatch from the injected component to the mod's definition, with one context per component, every
// call guarded: a broken skill is blamed on its mod and switched off, never allowed to break the
// game's own stat sums or hit handling.
internal static class SkillHost
{
    const int MaxFailures = 5;
    static readonly Dictionary<IntPtr, SkillContext> contexts = new();
    static readonly Dictionary<string, int> failures = new();

    static SkillContext Ctx(Skill s, CustomSkill def)
    {
        if (!contexts.TryGetValue(s.Pointer, out var c))
            contexts[s.Pointer] = c = new SkillContext { Skill = def, GameSkill = s };
        c.Fighter = s.owner;
        c.Level = Math.Max(1, s.level) + (s.enhanced ? 5 : 0);   // the game keeps the stage and an enhanced flag
        return c;
    }

    static CustomSkill Def(Skill s) => ModContent.Active ? ModContent.ById((int)s.skillType) : null;

    static bool Broken(CustomSkill d) => failures.TryGetValue(d.Key, out int n) && n >= MaxFailures;

    static void Fail(CustomSkill d, Delegate hook, string what, Exception e)
    {
        failures.TryGetValue(d.Key, out int n);
        failures[d.Key] = n + 1;
        Safe.Blame(hook, $"skill {d.Key} {what}", e);
        if (n + 1 == MaxFailures) Diag.Warn($"skill {d.Key} switched off after {MaxFailures} failures");
    }

    internal static string Describe(Skill s)
    {
        var d = ModContent.ById((int)s.skillType);
        if (d == null) return "";
        try { return d.Describe?.Invoke(Math.Max(1, s.level) + (s.enhanced ? 5 : 0)) ?? ""; }
        catch (Exception e) { Fail(d, d.Describe, "Describe", e); return ""; }
    }

    internal static void Added(Skill s, Fighter f)
    {
        var d = Def(s);
        if (d == null || Broken(d)) return;
        var c = Ctx(s, d);
        c.Fighter = f ?? s.owner;
        if (d.OnAdded == null) return;
        try { d.OnAdded(c); } catch (Exception e) { Fail(d, d.OnAdded, "OnAdded", e); }
    }

    internal static void Removed(Skill s, Fighter f)
    {
        var d = ModContent.ById((int)s.skillType);
        if (d != null && !Broken(d) && d.OnRemoved != null && contexts.TryGetValue(s.Pointer, out var c))
        {
            try { d.OnRemoved(c); } catch (Exception e) { Fail(d, d.OnRemoved, "OnRemoved", e); }
        }
        contexts.Remove(s.Pointer);
    }

    internal static void Event<T>(Skill s, Func<CustomSkill, T> pick, Action<T, SkillContext> call) where T : Delegate
    {
        var d = Def(s);
        if (d == null || Broken(d)) return;
        var h = pick(d);
        if (h == null) return;
        try { call(h, Ctx(s, d)); } catch (Exception e) { Fail(d, h, typeof(T).Name, e); }
    }

    internal static float Hit(Skill s, Func<CustomSkill, Func<SkillContext, HitInfo, float>> pick, HitInfo hit)
    {
        var d = Def(s);
        if (d == null || Broken(d)) return 0f;
        var h = pick(d);
        if (h == null) return 0f;
        try { return Finite(h(Ctx(s, d), hit)); } catch (Exception e) { Fail(d, h, "bonus", e); return 0f; }
    }

    internal static float Value(Skill s, Func<CustomSkill, Func<SkillContext, float>> pick)
    {
        var d = Def(s);
        if (d == null || Broken(d)) return 0f;
        var h = pick(d);
        if (h == null) return 0f;
        try { return Finite(h(Ctx(s, d))); } catch (Exception e) { Fail(d, h, "bonus", e); return 0f; }
    }

    internal static float Category(Skill s, Func<CustomSkill, Func<SkillContext, SpellCategory, float>> pick, SpellCategory cat)
    {
        var d = Def(s);
        if (d == null || Broken(d)) return 0f;
        var h = pick(d);
        if (h == null) return 0f;
        try { return Finite(h(Ctx(s, d), cat)); } catch (Exception e) { Fail(d, h, "bonus", e); return 0f; }
    }

    internal static bool Targetable(Skill s)
    {
        var d = Def(s);
        if (d == null || Broken(d) || d.Targetable == null) return true;
        try { return d.Targetable(Ctx(s, d)); } catch (Exception e) { Fail(d, d.Targetable, "Targetable", e); return true; }
    }

    // A NaN from one skill would poison the game's sum for every skill on the fighter.
    static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

    internal static void Clear() => contexts.Clear();
}
