using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace IronstrikeApi.Content;

// The wand's rune grid (GM/GestureMagicSystem-v3, verified October 2026): four opener nodes, one per
// corner, each leading to the selector nodes of a few schools; a selector leads to its school's two
// spell nodes (three and four strokes). A node is drawable when the player has the skill of the spell
// it casts (GestureMagicSystem.ConsiderEnablingSpells: HasSkill(GetSpell(node.Spell).skillType)).
//
// A custom school is drawn on a copy of the branch of a game school the local player does not have:
// same strokes, spell nodes pointed at the custom spells, labels showing the custom names and icons.
// The grid only lets the player draw paths to spells they know, so a copy never clashes with the
// branch it was copied from. Branches are assigned while playing: when the player gains a custom
// school it gets a free branch (its preferred one first); if the player later gains the game school
// behind that branch, the custom school moves to another free one.
internal static class RuneTree
{
    sealed class Branch
    {
        public CustomSchool School;
        public SkillType Host;
        public GameObject Copy;
        public GestureSpell Opener, Selector;
        public GestureSpell[] Nodes;
        public List<SpellLabel> Previews = new();
    }

    static readonly Dictionary<string, Branch> branches = new();
    static readonly HashSet<string> warned = new();
    static float nextTry;
    internal static readonly List<string> Report = new();

    static GestureMagicSystem System() => GM.instance != null ? GM.instance.GetComponentInChildren<GestureMagicSystem>(true) : null;

    internal static void Tick()
    {
        if (ModContent.Schools.Count == 0 || !SpellPatches.Ready || !ModContent.Frozen) return;
        if (Time.unscaledTime < nextTry) return;
        nextTry = Time.unscaledTime + 1f;
        var gms = System();
        if (gms is null || gms.AllSpells == null || SkillManager.instance == null || SpellManager.instance == null) return;

        var owned = new HashSet<int>();
        var me = Gameplay.Players.LocalFighter;
        if (me?.skills != null) foreach (var kv in me.skills) owned.Add((int)kv.Key);

        foreach (var key in branches.Keys.ToList())
        {
            var b = branches[key];
            if (!owned.Contains(b.School.Id) || owned.Contains((int)b.Host) || b.Copy == null) Remove(gms, key);
        }
        foreach (var d in ModContent.Schools.Where(x => x.Id != 0 && owned.Contains(x.Id) && !branches.ContainsKey(x.Key)).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var used = new HashSet<SkillType>(branches.Values.Select(x => x.Host));
            SkillType? host = null;
            foreach (var h in new[] { d.Runes }.Concat(CustomSchool.RuneSchools))
                if (!owned.Contains((int)h) && !used.Contains(h)) { host = h; break; }
            if (host == null)
            {
                if (warned.Add(d.Key)) Diag.Warn($"content: no free rune branch for school {d.Key} (the player has every game school)");
                continue;
            }
            try
            {
                var b = Graft(gms, d, host.Value);
                if (b != null) branches[d.Key] = b;
            }
            catch (Exception e) { if (warned.Add(d.Key + ":graft")) Diag.Error($"content: could not add the runes of school {d.Key}: {e.Message}"); }
        }
    }

    static void Remove(GestureMagicSystem gms, string key)
    {
        var b = branches[key];
        branches.Remove(key);
        try
        {
            b.Opener?.LeadsToSpells?.Remove(b.Selector);
            foreach (var l in b.Previews)
            {
                b.Opener?.SpellLabels?.Remove(l);
                if (l != null) UnityEngine.Object.Destroy(l.gameObject);
            }
            foreach (var n in b.Nodes)
            {
                gms.AllSpells.Remove(n);
                gms.ActivelyCastingSpells?.Remove(n);
            }
        }
        catch (Exception) { }
        if (b.Copy != null) UnityEngine.Object.Destroy(b.Copy);
        Diag.Info($"content: school {key} left the rune branch of {b.Host}");
    }

    static Branch Graft(GestureMagicSystem gms, CustomSchool d, SkillType hostSchool)
    {
        var game = SkillManager.instance.GetSkill(hostSchool)?.TryCast<UnlockSpellsSkill>();
        if (game?.spell1 is null || game.spell2 is null) return null;
        SpellType s1 = game.spell1.spellType, s2 = game.spell2.spellType;
        var c1 = ModContent.GetSpell(d.Spells[0]);
        var c2 = ModContent.GetSpell(d.Spells[1]);

        var copies = new HashSet<IntPtr>(branches.Values.SelectMany(x => x.Nodes).Select(n => n.Pointer));
        var all = gms.AllSpells.ToArray().Where(g => g is not null && !copies.Contains(g.Pointer)).ToList();
        var n1 = all.FirstOrDefault(g => g.Spell == s1);
        var selector = n1 is null ? null : all.FirstOrDefault(g => g.LeadsToSpells != null && g.LeadsToSpells.Contains(n1));
        var opener = selector is null ? null : all.FirstOrDefault(g => g.LeadsToSpells != null && g.LeadsToSpells.Contains(selector));
        var group = selector?.transform.parent;
        if (n1 is null || selector is null || opener is null || group is null || !n1.transform.IsChildOf(group))
        {
            if (warned.Add(d.Key + ":" + hostSchool)) Diag.Warn($"content: the rune branch of {hostSchool} is not laid out as expected");
            return null;
        }

        var clone = UnityEngine.Object.Instantiate(group.gameObject, group.parent, false);
        clone.name = group.name + " (" + d.Key + ")";
        var nodes = clone.GetComponentsInChildren<GestureSpell>(true);
        GestureSpell selClone = null;
        foreach (var n in nodes)
        {
            if (n.Spell == s1) n.Spell = c1.Type;
            else if (n.Spell == s2) n.Spell = c2.Type;
            else if (n.Spell == SpellType.None) selClone = n;
        }
        // Labels show only while the player holds their skillType (GestureSpell.Activate: a label with
        // a skillType is switched on exactly when HasSkill(skillType)), so every label in the copy must
        // name the custom school, not the game school it was copied from.
        foreach (var l in clone.GetComponentsInChildren<SpellLabel>(true)) Retarget(l, hostSchool, s1, s2, d, c1, c2);
        if (selClone is null) { UnityEngine.Object.Destroy(clone); return null; }

        opener.LeadsToSpells.Add(selClone);
        // The corner's own preview tiles (the icons beside each corner before anything is drawn) are
        // labels on the opener, one per game spell in that corner. Copy the borrowed school's tiles
        // into the same slots for the custom spells; they show while the player holds the custom
        // school, as the originals do for theirs (and the originals stay hidden: never held here).
        var previews = new List<SpellLabel>();
        foreach (var l in opener.SpellLabels.ToArray())
        {
            if (l == null || (l.spell != s1 && l.spell != s2)) continue;
            var copy = UnityEngine.Object.Instantiate(l.gameObject, l.transform.parent, false).GetComponent<SpellLabel>();
            copy.gameObject.name = l.gameObject.name + " (" + d.Key + ")";
            copy.transform.localPosition = l.transform.localPosition;
            copy.transform.localRotation = l.transform.localRotation;
            copy.transform.localScale = l.transform.localScale;
            Retarget(copy, hostSchool, s1, s2, d, c1, c2);
            opener.SpellLabels.Add(copy);
            previews.Add(copy);
        }
        foreach (var n in nodes)
        {
            gms.AllSpells.Add(n);
            try { n.Init(); } catch (Exception e) { Diag.Warn($"content: rune node init: {e.Message}"); }
        }
        bool linked = selClone.LeadsToSpells != null && selClone.LeadsToSpells.ToArray().All(x => x is not null && x.transform.IsChildOf(clone.transform));
        string line = $"school {d.Key}: drawn on the rune branch of {hostSchool} ({nodes.Length} nodes, linked {linked}, {previews.Count} corner tiles); " +
                      $"{c1.Key} like {s1}, {c2.Key} like {s2}";
        Report.Add(line);
        Diag.Info("content: " + line);
        return new Branch { School = d, Host = hostSchool, Copy = clone, Opener = opener, Selector = selClone, Nodes = nodes, Previews = previews };
    }

    static void Retarget(SpellLabel l, SkillType hostSchool, SpellType s1, SpellType s2, CustomSchool d, CustomSpell c1, CustomSpell c2)
    {
        if (l == null) return;
        if (l.skillMode && l.skillType == hostSchool) { l.SetSkill(d.Type); return; }
        if (l.spell == s1) l.SetSpell(c1.Type);
        else if (l.spell == s2) l.SetSpell(c2.Type);
        if (l.skillType == hostSchool) l.skillType = d.Type;
    }

    // For the probe: which branch a school is drawn on now, if any.
    internal static SkillType? HostOf(string key) => branches.TryGetValue(key, out var b) ? b.Host : null;
}
