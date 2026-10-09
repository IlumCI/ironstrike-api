using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace IronstrikeApi.Content;

// The one Spell class the API registers into the game. Every custom spell is an object carrying this
// component under a custom school; the game finds it through the school (Fighter.GetSpell), aims it
// with the indicators copied from CustomSpell.LooksLike, sends the cast, and calls the virtual events
// below on every machine. The rules of ApiSkill apply here too (never call base.X(); definitions are
// found from the native spellType field; per-instance state is keyed by the native pointer).
internal static partial class Injected
{
    internal class ApiSpell : Spell
    {
        public ApiSpell(IntPtr ptr) : base(ptr) { }

        public ApiSpell() : base(Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorPointer<ApiSpell>())
            => Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorBody(this);

        public override void Init() => SpellHost.Init(this);
        public override void ApplyValuesFrom(Skill other) { }
        public override void Reset() { }

        public override string GetDescription() => SpellHost.Describe(this);
        public override string GetDescription(Il2CppReferenceArray<Il2CppSystem.Object> args) => SpellHost.Describe(this);
        public override string GetDescription(bool useLevel, Il2CppReferenceArray<Il2CppSystem.Object> args) => SpellHost.Describe(this);

        public override void OnRaySpellFired_Synced(CasterWeapon casterWep, Vector3 origin, Vector3 direction)
            => SpellHost.Fired(this, casterWep, null, origin, direction, origin + direction * 10f, 0f);

        public override void OnFighterSpellFired_Synced(CasterWeapon casterWep, Fighter target, Vector3 origin, float amount, bool deathblow)
            => SpellHost.Fired(this, casterWep, target, origin, target is not null ? (Gameplay.Body.Center(target) - origin).normalized : Vector3.forward,
                               target is not null ? Gameplay.Body.Feet(target) : origin, amount);

        public override void OnAreaSpellFired_Synced(CasterWeapon casterWep, Vector3 point)
        {
            var origin = casterWep is not null && casterWep.WandTip is not null ? casterWep.WandTip.position : point;
            SpellHost.Fired(this, casterWep, null, origin, (point - origin).normalized, point, 0f);
        }

        public override float GetOnShootAmount(CasterWeapon casterWep, Fighter target, Vector3 origin) => SpellHost.Amount(this, target);

        public override void OnAreaEffectHit_LocalAuth(Fighter target, Vector3 hitPointPS, Vector3 hitVelocityWS) => SpellHost.AreaHit(this, target, hitPointPS);

        public override void OnEncounterStarted_Synced() => SpellHost.Encounter(this);

        // The game's spawners, for SpellCast. They use ProjectilePrefab, copied from LooksLike.
        internal bool SpawnArea(CasterWeapon cw, Vector3 origin, Vector3 direction, float baseDamage)
        {
            if (cw is null || ProjectilePrefab is null) { SpellHost.Once(this, "area", "no area effect to borrow (LooksLike has no ProjectilePrefab)"); return false; }
            var ae = SpawnAreaEffect(cw, origin, direction);
            SpellHost.Once(this, "area", ae is null ? $"{ProjectilePrefab.name} is not an area effect" : $"area effect {ProjectilePrefab.name} spawned");
            if (ae is null) return false;
            SpellHost.Ctx(this)?.State.SetAreaDamage(baseDamage);
            ae.Init(cw.weapon, baseDamage, null);
            return true;
        }

        internal bool SpawnShot(CasterWeapon cw, Vector3 origin, Vector3 direction)
        {
            if (cw is null || ProjectilePrefab is null) { SpellHost.Once(this, "shot", "no projectile to borrow"); return false; }
            var p = SpawnProjectile(cw, origin, direction);
            SpellHost.Once(this, "shot", p is null ? $"{ProjectilePrefab.name} is not a projectile" : $"projectile {ProjectilePrefab.name} fired, damage {p.damage}");
            return p is not null;
        }

        internal void ClearPreview(CasterWeapon cw, Vector3 at)
        {
            if (cw is null) return;
            EnsureSpellPreviewReadiness(cw);
            var p = cw.spellPreview;
            if (p is null) return;
            p.MoveToPoint(at);
            p.ScaleOutAndDestroy(0.3f, 0f);
        }
    }
}

internal static class AreaDamageState
{
    const string Key = "__api.areaDamage";
    internal static void SetAreaDamage(this Dictionary<string, object> s, float v) => s[Key] = v;
    internal static float AreaDamage(this Dictionary<string, object> s) => s.TryGetValue(Key, out var v) && v is float f ? f : 0f;
}

// Dispatch from the injected spell to the mod's definition, guarded like SkillHost.
internal static class SpellHost
{
    const int MaxFailures = 5;
    static readonly Dictionary<IntPtr, SpellContext> contexts = new();
    static readonly Dictionary<string, int> failures = new();

    static readonly IntPtr SpellInit = NativeMethod(typeof(Spell), "NativeMethodInfoPtr_Init_");

    static IntPtr NativeMethod(Type t, string prefix)
    {
        foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            if (f.Name.StartsWith(prefix, StringComparison.Ordinal) && f.FieldType == typeof(IntPtr))
                return (IntPtr)f.GetValue(null);
        return IntPtr.Zero;
    }

    internal static CustomSpell Def(Spell s) => s is null ? null : ModContent.SpellById((int)s.spellType);

    internal static SpellContext Ctx(Injected.ApiSpell s)
    {
        var d = Def(s);
        if (d == null) return null;
        if (!contexts.TryGetValue(s.Pointer, out var c))
            contexts[s.Pointer] = c = new SpellContext { Spell = d, GameSpell = s };
        c.Caster = s.owner;
        c.Level = Level(s, d);
        c.Weapon = s.owner?.MainWeapon is not null && s.owner.MainWeapon.isCasterWeapon ? s.owner.MainWeapon : s.owner?.OffWeapon;
        return c;
    }

    // A spell's level is its school's: the stage, plus 5 when the school was taken enhanced (the game
    // stores an enhanced skill as its stage and a flag).
    internal static int Level(Spell s, CustomSpell d)
    {
        int lvl = Math.Max(1, s.level);
        bool enh = s.enhanced;
        var f = s.owner;
        if (d?.School != null && f?.skills != null && f.skills.TryGetValue(d.School.Type, out var school) && school != null)
        {
            lvl = Math.Max(1, school.level);
            enh = school.enhanced;
        }
        return lvl + (enh ? 5 : 0);
    }

    static bool Broken(CustomSpell d) => failures.TryGetValue(d.Key, out int n) && n >= MaxFailures;

    static void Fail(CustomSpell d, Delegate hook, string what, Exception e)
    {
        failures.TryGetValue(d.Key, out int n);
        failures[d.Key] = n + 1;
        Safe.Blame(hook, $"spell {d.Key} {what}", e);
        if (n + 1 == MaxFailures) Diag.Warn($"spell {d.Key} switched off after {MaxFailures} failures");
    }

    // The game's Spell.Init, called directly (not through the vtable, which leads back here): it sets
    // up the tuner levels and the cooldown, mana and range attributes. Then the definition's values
    // are written into those attributes and pushed into the tuner, where the game reads them.
    internal static void Init(Injected.ApiSpell s)
    {
        if (s is null || s.manaCost is not null) return;     // set up already (the field is native)
        var d = Def(s);
        try
        {
            if (SpellInit == IntPtr.Zero) throw new MissingMethodException("Spell.Init");
            unsafe
            {
                IntPtr exc = IntPtr.Zero;
                IL2CPP.il2cpp_runtime_invoke(SpellInit, s.Pointer, (void**)IntPtr.Zero, ref exc);
                Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc);
            }
            if (d != null)
            {
                Write(s.cooldown, d.Cooldown);
                Write(s.manaCost, d.ManaCost);
                Write(s.range, d.Range);
            }
        }
        catch (Exception e) { Diag.Error($"content: could not set up spell {d?.Key ?? s.spellType.ToString()}: {e.Message}"); }
    }

    static void Write(SkillAttribute a, float[] v)
    {
        if (a is null) return;
        a.level1 = CustomSpell.At(v, 1); a.level2 = CustomSpell.At(v, 2); a.level3 = CustomSpell.At(v, 3);
        a.level4 = CustomSpell.At(v, 4); a.level5 = CustomSpell.At(v, 5); a.level6 = CustomSpell.At(v, 6);
        a.level7 = CustomSpell.At(v, 7); a.level8 = CustomSpell.At(v, 8); a.level9 = CustomSpell.At(v, 9);
        a.level10 = CustomSpell.At(v, 10);
        a.Init(true);
    }

    internal static string Describe(Spell s)
    {
        var d = Def(s);
        if (d == null) return "";
        try { return d.Describe?.Invoke(Level(s, d)) ?? ""; }
        catch (Exception e) { Fail(d, d.Describe, "Describe", e); return ""; }
    }

    internal static void Fired(Injected.ApiSpell s, CasterWeapon cw, Fighter target, Vector3 origin, Vector3 dir, Vector3 point, float amount)
    {
        try { s.ClearPreview(cw, point); } catch (Exception) { }
        var d = Def(s);
        if (d == null || Broken(d) || d.OnCast == null || !ModContent.Active) return;
        var c = Ctx(s);
        var cast = new SpellCast
        {
            Wand = cw, Caster = s.owner ?? cw?.weapon?.fighter, Origin = origin, Direction = dir, Target = target,
            Point = point, Amount = amount, Context = c, Game = s,
        };
        if (cast.Caster is null) cast.Caster = c.Caster;
        c.Caster ??= cast.Caster;
        casts.TryGetValue(d.Key, out int k); casts[d.Key] = k + 1;
        try { d.OnCast(c, cast); } catch (Exception e) { Fail(d, d.OnCast, "OnCast", e); }
    }

    internal static float Amount(Injected.ApiSpell s, Fighter target)
    {
        var d = Def(s);
        if (d == null || Broken(d) || d.Amount == null || !ModContent.Active) return 0f;
        try { float v = d.Amount(Ctx(s), target); return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v; }
        catch (Exception e) { Fail(d, d.Amount, "Amount", e); return 0f; }
    }

    internal static void AreaHit(Injected.ApiSpell s, Fighter target, Vector3 at)
    {
        var d = Def(s);
        if (d == null || Broken(d) || target is null || !ModContent.Active) return;
        var c = Ctx(s);
        try
        {
            if (d.OnAreaHit != null) d.OnAreaHit(c, target, at);
            else if (!GM.isSameTeam(c.Caster.faction, target.faction)) c.HitArea(target, c.State.AreaDamage());
        }
        catch (Exception e) { Fail(d, d.OnAreaHit, "OnAreaHit", e); }
    }

    internal static void Encounter(Injected.ApiSpell s)
    {
        var d = Def(s);
        if (d == null || Broken(d) || d.OnEncounterStart == null || !ModContent.Active) return;
        try { d.OnEncounterStart(Ctx(s)); } catch (Exception e) { Fail(d, d.OnEncounterStart, "OnEncounterStart", e); }
    }

    static readonly Dictionary<string, int> casts = new();
    internal static int Casts(string key) => casts.TryGetValue(key, out int n) ? n : 0;

    static readonly HashSet<string> said = new();

    // One line per spell and kind of event, for modders reading the log.
    internal static void Once(Spell s, string what, string text)
    {
        var d = Def(s);
        if (d != null && said.Add(d.Key + ":" + what)) Diag.Info($"spell {d.Key}: {text}");
    }

    internal static int Failures(string key) => failures.TryGetValue(key, out int n) ? n : 0;

    internal static void Clear() { contexts.Clear(); }
}
