using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace IronstrikeApi.Content;

// How custom spells and schools get into the game (all from the October 2026 build):
//  - A magic school is an UnlockSpellsSkill whose GameObject holds its two spells as children
//    (spell1, spell2). Fighter.GetSpell(type) takes skills[spell.skillType] and returns whichever of
//    the two matches; the rune grid unlocks a spell when the fighter has spell.skillType.
//  - So a custom school is a clone of a game school (its preferred rune branch's), with its two spell children
//    replaced by ApiSpell objects. It goes into SkillDatabase's dictionary, the spells into
//    SpellDatabase's (SpellManager.GetSpell reads that one).
//  - Templates live under one inactive, never-unloaded holder, so cloning them runs no Awake.
[HarmonyPatch]
internal static class SpellPatches
{
    static bool injected;
    static GameObject holder;
    static readonly Dictionary<int, Injected.ApiSpell> spellTemplates = new();
    static readonly Dictionary<int, UnlockSpellsSkill> schoolTemplates = new();

    internal static bool Ready => injected;

    internal static void Install()
    {
        try
        {
            using (new MscorlibTypeResolve())
                ClassInjector.RegisterTypeInIl2Cpp<Injected.ApiSpell>();
            GcScan.Enable(Il2CppInterop.Runtime.Il2CppClassPointerStore<Injected.ApiSpell>.NativeClassPtr, "ApiSpell");
            injected = true;
        }
        catch (Exception e) { Diag.Error($"content: could not register the spell class, custom spells are off: {e.Message}"); return; }
        try { Plugin.Harmony.PatchAll(typeof(SpellPatches)); }
        catch (Exception e) { Diag.Error($"content: spell patches: {e.Message}"); }
    }

    static Transform Holder()
    {
        if (holder != null) return holder.transform;
        holder = new GameObject("IronstrikeApiContent");
        holder.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(holder);
        holder.hideFlags = HideFlags.HideAndDontSave;
        return holder.transform;
    }

    // ------------------------------------------------------------------ spell templates

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpellDatabase), nameof(SpellDatabase.GetSpellDict))]
    static void FileSpells(ref Il2CppSystem.Collections.Generic.Dictionary<SpellType, Spell> __result)
    {
        if (!injected || __result == null || ModContent.Spells.Count == 0) return;
        ModContent.Freeze();
        foreach (var d in ModContent.Spells)
        {
            if (d.Id == 0 || d.School == null || __result.ContainsKey(d.Type)) continue;
            var t = SpellTemplate(d, __result);
            if (t is not null) __result[d.Type] = t;
        }
    }

    static Injected.ApiSpell SpellTemplate(CustomSpell d, Il2CppSystem.Collections.Generic.Dictionary<SpellType, Spell> gameSpells)
    {
        if (spellTemplates.TryGetValue(d.Id, out var t) && t != null) return t;
        if (gameSpells == null) return null;
        try
        {
            var go = new GameObject("ApiSpell_" + d.Key);
            go.transform.SetParent(Holder(), false);
            var s = go.AddComponent<Injected.ApiSpell>();
            Spell look = gameSpells.ContainsKey(d.LooksLike) ? gameSpells[d.LooksLike] : null;
            s.spellType = d.Type;
            s.spellName = d.Name;
            s.spellCategory = d.Category;
            s.targetingType = d.Targeting;
            s.effectType = look is not null ? look.effectType : SpellEffectType.DirectEffect;
            s.stage = SpellStage.None;
            s.spellIcon = d.Icon?.Sprite ?? (look is not null ? look.spellIcon : null);
            s.spellIconColor = d.Color;
            s.fireDelayTicks = d.FireDelayTicks;
            if (look is not null)
            {
                s.ProjectilePrefab = look.ProjectilePrefab;
                s.SpellPreviewPrefab = look.SpellPreviewPrefab;
                s.TargetingIndicator = look.TargetingIndicator;
                s.TargetRadiusIndicator = look.TargetRadiusIndicator;
                s.TargetFighterIndicator = look.TargetFighterIndicator;
                s.TargetGroundIndicator = look.TargetGroundIndicator;
            }
            s.skillType = d.School.Type;
            s.skillName = d.Name;
            s.skillClass = SkillClass.Caster;
            s.skillCategory = d.School.Category;
            s.skillSprite = s.spellIcon;
            s.level = 1;
            s.hidden = true;
            s.mutualExclusions = new Il2CppSystem.Collections.Generic.List<SkillType>();
            s.elements = new Il2CppSystem.Collections.Generic.List<Tuner.Element>();
            s.attributes = new Il2CppSystem.Collections.Generic.List<Tuner.Attribute>();
            SpellHost.Init(s);
            spellTemplates[d.Id] = s;
            return s;
        }
        catch (Exception e) { Diag.Error($"content: could not build spell {d.Key}: {e.Message}"); return null; }
    }

    // ------------------------------------------------------------------ school templates

    // Called from the skill dictionary postfix (SkillPatches.FileTemplates).
    internal static void FileSchools(Il2CppSystem.Collections.Generic.Dictionary<SkillType, Skill> skillDict)
    {
        if (!injected || ModContent.Schools.Count == 0 || SpellManager.instance == null) return;
        Il2CppSystem.Collections.Generic.Dictionary<SpellType, Spell> spellDict = null;
        foreach (var d in ModContent.Schools)
        {
            if (d.Id == 0 || skillDict.ContainsKey(d.Type)) continue;
            spellDict ??= SpellDatabase.GetSpellDict(SpellManager.instance.spellDatabase, false);
            var t = SchoolTemplate(d, skillDict, spellDict);
            if (t is not null) skillDict[d.Type] = t;
        }
    }

    static UnlockSpellsSkill SchoolTemplate(CustomSchool d, Il2CppSystem.Collections.Generic.Dictionary<SkillType, Skill> skillDict,
                                            Il2CppSystem.Collections.Generic.Dictionary<SpellType, Spell> spellDict)
    {
        if (schoolTemplates.TryGetValue(d.Id, out var t) && t != null) return t;
        if (!skillDict.ContainsKey(d.Runes)) return null;
        var game = skillDict[d.Runes]?.TryCast<UnlockSpellsSkill>();
        if (game is null) { Diag.Warn($"content: {d.Runes} is not a magic school; school {d.Key} is off"); return null; }
        var spells = d.Spells.Select(ModContent.GetSpell).ToList();
        var made = spells.Select(sp => SpellTemplate(sp, spellDict)).ToList();
        if (made.Any(m => m is null)) return null;
        try
        {
            var go = UnityEngine.Object.Instantiate(game.gameObject, Holder(), false);
            go.name = "ApiSchool_" + d.Key;
            // The game school's own spells come along as children: replace them with ours.
            var old = new List<GameObject>();
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var ch = go.transform.GetChild(i).gameObject;
                if (ch.GetComponent<Spell>() is not null) old.Add(ch);
            }
            foreach (var ch in old) UnityEngine.Object.DestroyImmediate(ch);
            var us = go.GetComponent<UnlockSpellsSkill>();
            var first = UnityEngine.Object.Instantiate(made[0].gameObject, go.transform, false).GetComponent<Injected.ApiSpell>();
            var second = UnityEngine.Object.Instantiate(made[1].gameObject, go.transform, false).GetComponent<Injected.ApiSpell>();
            first.gameObject.name = made[0].gameObject.name;
            second.gameObject.name = made[1].gameObject.name;
            SpellHost.Init(first);
            SpellHost.Init(second);
            us.spell1 = first;
            us.spell2 = second;
            us.skillType = d.Type;
            us.skillName = d.Name;
            us.skillCategory = d.Category;
            us.skillClass = SkillClass.Caster;
            us.level = 1;
            us.hidden = false;
            us.allowsEnhanced = d.AllowsEnhanced;
            var icon = d.Icon?.Sprite;
            if (icon != null) { us.skillSprite = icon; us.skillIcon = icon; }
            us.mutualExclusions = new Il2CppSystem.Collections.Generic.List<SkillType>();
            schoolTemplates[d.Id] = us;
            return us;
        }
        catch (Exception e) { Diag.Error($"content: could not build school {d.Key}: {e.Message}"); return null; }
    }

    // ------------------------------------------------------------------ the game's school class

    // A fighter gained a school: our spells need their cooldown, mana and range set up (clones carry
    // only native fields; the attributes are made by Spell.Init).
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnlockSpellsSkill), nameof(UnlockSpellsSkill.OnAddToFighter_Synced))]
    static void SchoolAdded(UnlockSpellsSkill __instance)
    {
        if (!injected || __instance is null || ModContent.SchoolById((int)__instance.skillType) == null) return;
        try
        {
            if (__instance.spell1?.TryCast<Injected.ApiSpell>() is { } a) SpellHost.Init(a);
            if (__instance.spell2?.TryCast<Injected.ApiSpell>() is { } b) SpellHost.Init(b);
        }
        catch (Exception e) { ApiLog.WarnOnce(null, "content:school-add", $"content: school spells: {e.Message}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnlockSpellsSkill), nameof(UnlockSpellsSkill.GetDescription), new Type[0])]
    static void SchoolText(UnlockSpellsSkill __instance, ref string __result)
    {
        if (__instance is null) return;
        var d = ModContent.SchoolById((int)__instance.skillType);
        if (d == null) return;
        try
        {
            __result = d.Describe != null ? d.Describe(Math.Max(1, __instance.level) + (__instance.enhanced ? 5 : 0))
                : $"Teaches two spells: {ModContent.GetSpell(d.Spells[0])?.Name} and {ModContent.GetSpell(d.Spells[1])?.Name}.";
        }
        catch (Exception e) { Safe.Blame(d.Describe, $"school {d.Key} Describe", e); }
    }

    internal static void Clear() { }
}
