using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace IronstrikeApi.Content;

// How custom skills get into the game's own skill system (all verified in game, October 2026 build):
//  - SkillDatabase.GetSkillDict holds one template per SkillType; GiveSkillToFighter clones the
//    template onto the fighter. A postfix files our templates there.
//  - Skill.GetName/GetFancyName read the game's I2 localization ("UI/<id>"), which has no entries for
//    our ids; they are not virtual, so they are patched.
//  - The upgrade screen takes its skill cards from SkillManager.PickUpgradeSkillsFor2 (the older
//    PickUpgradeSkillsFor is patched too). A postfix mixes custom skills into the offer.
[HarmonyPatch]
internal static class SkillPatches
{
    static bool injected;
    static readonly Dictionary<int, GameObject> templates = new();
    static readonly System.Random rng = new();

    internal static bool Ready => injected;

    internal static void Install()
    {
        try
        {
            using (new MscorlibTypeResolve())
                ClassInjector.RegisterTypeInIl2Cpp<Injected.ApiSkill>();
            GcScan.Enable(Il2CppInterop.Runtime.Il2CppClassPointerStore<Injected.ApiSkill>.NativeClassPtr, "ApiSkill");
            injected = true;
        }
        catch (Exception e) { Diag.Error($"content: could not register the skill class, custom skills are off: {e.Message}"); return; }
        try { Plugin.Harmony.PatchAll(typeof(SkillPatches)); }
        catch (Exception e) { Diag.Error($"content: skill patches: {e.Message}"); }
    }

    // ------------------------------------------------------------------ templates

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SkillDatabase), nameof(SkillDatabase.GetSkillDict))]
    static void FileTemplates(ref Il2CppSystem.Collections.Generic.Dictionary<SkillType, Skill> __result)
    {
        if (!injected || __result == null || !ModContent.Any) return;
        if (!ModContent.Frozen)
        {
            var existing = new List<int>();
            foreach (var kv in __result) existing.Add((int)kv.Key);
            ModContent.Freeze(existing);
        }
        foreach (var d in ModContent.Skills)
        {
            if (d.Id == 0 || __result.ContainsKey(d.Type)) continue;
            var t = Template(d);
            if (t != null) __result[d.Type] = t;
        }
        SpellPatches.FileSchools(__result);
    }

    static Skill Template(CustomSkill d)
    {
        if (templates.TryGetValue(d.Id, out var go) && go != null) return go.GetComponent<Injected.ApiSkill>();
        try
        {
            go = new GameObject("ApiSkill_" + d.Key);
            go.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            var s = go.AddComponent<Injected.ApiSkill>();
            s.skillType = d.Type;
            s.skillName = d.Name;
            s.level = 1;
            s.hidden = false;
            s.allowsEnhanced = d.AllowsEnhanced;
            s.skillCategory = d.Category;
            s.skillClass = d.Class ?? SkillClass.Fighter;
            s.mutualExclusions = new Il2CppSystem.Collections.Generic.List<SkillType>();
            s.elements = new Il2CppSystem.Collections.Generic.List<Tuner.Element>();
            s.attributes = new Il2CppSystem.Collections.Generic.List<Tuner.Attribute>();
            s.skillSprite = d.Icon?.Sprite ?? Fallback();
            templates[d.Id] = go;
            return s;
        }
        catch (Exception e) { Diag.Error($"content: could not build skill {d.Key}: {e.Message}"); return null; }
    }

    static Sprite fallback;
    static Sprite Fallback()
    {
        if (fallback != null) return fallback;
        try { fallback = Icons.Library("skill_overcharge").Sprite; } catch (Exception) { }
        return fallback;
    }

    // ------------------------------------------------------------------ names

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Skill), nameof(Skill.GetName))]
    static void Name(Skill __instance, ref string __result)
    {
        var n = CustomName(__instance);
        if (n != null) __result = n;
    }

    // A spell's skillType is its school's, so spells are told apart first.
    static string CustomName(Skill s)
    {
        if (s is null || !ModContent.Any) return null;
        var sp = s.TryCast<Spell>();
        if (sp is not null) return ModContent.SpellById((int)sp.spellType)?.Name;
        return ModContent.SkillName((int)s.skillType);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Skill), nameof(Skill.GetFancyName))]
    static void FancyName(Skill __instance, ref string __result)
    {
        var n = CustomName(__instance);
        if (n != null) __result = __instance.TryCast<Spell>() is not null ? n : n + " " + Roman(__instance.level) + (__instance.enhanced || __instance.level > 5 ? " - Enhanced" : "");
    }

    internal static string Roman(int level) => (level > 5 ? level - 5 : level) switch
    {
        <= 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
        _ => "V+",
    };

    // ------------------------------------------------------------------ offers

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SkillManager), nameof(SkillManager.PickUpgradeSkillsFor))]
    static void Offer(Fighter fighter, SkillCategory category, ref Il2CppSystem.Collections.Generic.List<SkillType> __result)
        => Mix(fighter, category, __result);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SkillManager), nameof(SkillManager.PickUpgradeSkillsFor2))]
    static void Offer2(Fighter fighter, SkillCategory category, ref Il2CppSystem.Collections.Generic.List<SkillType> __result)
        => Mix(fighter, category, __result);

    // The game's picks and the eligible custom skills compete for the same slots, weighted: each of
    // the game's picks counts 1, a custom skill its OfferWeight. The number of cards never changes.
    static void Mix(Fighter fighter, SkillCategory category, Il2CppSystem.Collections.Generic.List<SkillType> list)
    {
        if (!injected || list == null || fighter is null || !ModContent.Active || (ModContent.Skills.Count == 0 && ModContent.Schools.Count == 0)) return;
        try
        {
            var game = new List<int>();
            var owned = OwnedLevels(fighter);
            var original = new List<int>();
            for (int i = 0; i < list.Count; i++) original.Add((int)list[i]);
            game.AddRange(original);
            var candidates = ModContent.Skills.Where(d => Eligible(d, fighter, category, owned)).Select(d => (d.Id, d.OfferWeight))
                .Concat(ModContent.Schools.Where(d => EligibleSchool(d, fighter, category, owned)).Select(d => (d.Id, d.OfferWeight))).ToList();
            var offers = OffersBy(game, candidates, rng);
            if (offers.SequenceEqual(original)) return;
            list.Clear();
            foreach (var id in offers) list.Add((SkillType)id);
        }
        catch (Exception e) { ApiLog.WarnOnce(null, "content:offer", $"content: could not mix custom skills into the offer: {e.Message}"); }
    }

    static Dictionary<int, int> OwnedLevels(Fighter f)
    {
        var map = new Dictionary<int, int>();
        if (f.skills == null) return map;
        foreach (var kv in f.skills) if (kv.Value != null) map[(int)kv.Key] = kv.Value.level;
        return map;
    }

    internal static bool Eligible(CustomSkill d, Fighter f, SkillCategory category, Dictionary<int, int> owned)
    {
        if (d.Id == 0 || d.OfferWeight <= 0 || d.Category != category) return false;
        if (d.Class.HasValue && d.Class.Value != f.fighterClass) return false;
        if (owned.TryGetValue(d.Id, out int lvl) && (lvl > 5 ? lvl - 5 : lvl) >= d.MaxLevel) return false;
        foreach (var other in d.ExclusiveWith)
        {
            var o = ModContent.GetSkill(other);
            if (o != null && o.Id != 0 && owned.ContainsKey(o.Id)) return false;
        }
        foreach (var o in ModContent.Skills)
            if (o.ExclusiveWith.Contains(d.Key) && o.Id != 0 && owned.ContainsKey(o.Id)) return false;
        return true;
    }

    internal static bool EligibleSchool(CustomSchool d, Fighter f, SkillCategory category, Dictionary<int, int> owned)
    {
        if (d.Id == 0 || d.OfferWeight <= 0 || d.Category != category || f.fighterClass != SkillClass.Caster) return false;
        return !(owned.TryGetValue(d.Id, out int lvl) && (lvl > 5 ? lvl - 5 : lvl) >= d.MaxLevel);
    }

    internal static List<int> Offers(List<int> game, List<CustomSkill> candidates, System.Random random)
        => OffersBy(game, candidates.Select(c => (c.Id, c.OfferWeight)).ToList(), random);

    // Weighted draw without replacement over the game's picks (weight 1 each) and the candidates.
    internal static List<int> OffersBy(List<int> game, List<(int Id, float Weight)> candidates, System.Random random)
    {
        // The game's PickUpgradeSkillsFor2 builds on PickUpgradeSkillsFor, so one offer can pass through
        // here twice: a custom skill already in the list must not be offered again.
        candidates = candidates.Where(c => !game.Contains(c.Id)).ToList();
        if (candidates.Count == 0 || game.Count == 0) return game;
        var pool = game.Select(id => (Id: id, W: 1f)).Concat(candidates.Select(c => (Id: c.Id, W: c.Weight))).ToList();
        var picked = new List<int>();
        while (picked.Count < game.Count && pool.Count > 0)
        {
            float total = pool.Sum(p => p.W), roll = (float)random.NextDouble() * total;
            int i = 0;
            for (; i < pool.Count - 1; i++) { roll -= pool[i].W; if (roll <= 0) break; }
            picked.Add(pool[i].Id);
            pool.RemoveAt(i);
        }
        return picked;
    }
}

// Il2CppInterop builds an injected class from a zeroed Il2CppClass and never sets has_references, so
// IL2CPP allocates its instances as pointer-free memory that the GC does not scan. Every object a
// custom skill's fields point to (its lists, its owner) is then collected at the next full GC -- the
// one a level load runs -- and the next Instantiate of the template reads garbage and aborts with
// an impossible allocation (verified: "Could not allocate 18446744067552063744B" in
// GiveSkillToFighter after loading a level). Setting the bit makes IL2CPP scan instances
// conservatively, like any class without a GC descriptor.
internal static class GcScan
{
    const int HasReferencesBit = 5;      // Il2CppClass bitfield0, metadata 24.x to 29.x

    internal static void Enable(IntPtr klass, string name)
    {
        if (klass == IntPtr.Zero) throw new InvalidOperationException($"{name} has no class pointer");
        if (Il2CppInterop.Runtime.IL2CPP.il2cpp_class_has_references(klass)) return;
        int offset = Bitfield0Offset(klass);
        unsafe
        {
            byte* b = (byte*)klass + offset;
            *b |= 1 << HasReferencesBit;
        }
        if (!Il2CppInterop.Runtime.IL2CPP.il2cpp_class_has_references(klass))
        {
            unsafe { *((byte*)klass + offset) &= unchecked((byte)~(1 << HasReferencesBit)); }
            throw new InvalidOperationException($"could not mark {name} as holding references (offset {offset})");
        }
        Diag.Info($"content: {name} instances are now scanned by the GC");
    }

    // The offset of _bitfield0 in the Il2CppClass layout Il2CppInterop picked for this Unity version.
    static int Bitfield0Offset(IntPtr klass)
    {
        object wrapper;
        unsafe { wrapper = Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Wrap((Il2CppInterop.Runtime.Runtime.Il2CppClass*)klass); }
        var handler = wrapper.GetType().DeclaringType ?? throw new InvalidOperationException("no class struct handler");
        var layout = handler.GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .FirstOrDefault(t => t.IsValueType && t.Name.StartsWith("Il2CppClass_", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"no class layout in {handler.Name}");
        return System.Runtime.InteropServices.Marshal.OffsetOf(layout, "_bitfield0").ToInt32();
    }
}

// Il2CppInterop builds an injected class's vtable from every inherited virtual method and maps each
// parameter type back to a managed type with Type.GetType. For mscorlib types it uses a name with no
// assembly: "System.X" (found only if .NET's own CoreLib still has it) or "Il2CppSystem.X" (looked up
// in Il2CppInterop and CoreLib only, never in Il2Cppmscorlib). Types .NET 6 moved out of CoreLib --
// Tuner's BinaryFormatter -- are never found and registration throws. While registering, answer those
// lookups with the interop's Il2Cppmscorlib.
internal sealed class MscorlibTypeResolve : IDisposable
{
    static readonly System.Reflection.Assembly Il2CppCorlib = typeof(Il2CppSystem.Object).Assembly;
    readonly ResolveEventHandler handler = (_, e) =>
        e.Name != null && e.Name.StartsWith("Il2CppSystem.", StringComparison.Ordinal) && Il2CppCorlib.GetType(e.Name) != null
            ? Il2CppCorlib : null;

    public MscorlibTypeResolve() => AppDomain.CurrentDomain.TypeResolve += handler;
    public void Dispose() => AppDomain.CurrentDomain.TypeResolve -= handler;
}
