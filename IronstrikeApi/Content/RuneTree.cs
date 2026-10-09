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
// A custom school gets a copy of the branch of the school whose runes it borrows: same strokes, with
// the spell nodes pointed at the custom spells. Only one of the two branches is ever drawable, since a
// player cannot hold both schools.
internal static class RuneTree
{
    static bool done;
    static float nextTry;
    internal static readonly List<string> Report = new();

    internal static void Tick()
    {
        if (done || ModContent.Schools.Count == 0 || !SpellPatches.Ready || !ModContent.Frozen) return;
        if (Time.unscaledTime < nextTry) return;
        nextTry = Time.unscaledTime + 2f;
        var gms = GM.instance != null ? GM.instance.GetComponentInChildren<GestureMagicSystem>(true) : null;
        if (gms is null || gms.AllSpells == null || SkillManager.instance == null || SpellManager.instance == null) return;
        done = true;
        foreach (var d in ModContent.Schools)
        {
            try { Graft(gms, d); }
            catch (Exception e) { Diag.Error($"content: could not add the runes of school {d.Key}: {e.Message}"); }
        }
    }

    static void Graft(GestureMagicSystem gms, CustomSchool d)
    {
        if (d.Id == 0) return;
        var game = SkillManager.instance.GetSkill(d.Runes)?.TryCast<UnlockSpellsSkill>();
        if (game?.spell1 is null || game.spell2 is null) { Diag.Warn($"content: no rune branch for {d.Runes}"); return; }
        SpellType s1 = game.spell1.spellType, s2 = game.spell2.spellType;
        var c1 = ModContent.GetSpell(d.Spells[0]);
        var c2 = ModContent.GetSpell(d.Spells[1]);

        var all = gms.AllSpells.ToArray().Where(g => g is not null).ToList();
        var n1 = all.FirstOrDefault(g => g.Spell == s1);
        var selector = n1 is null ? null : all.FirstOrDefault(g => g.LeadsToSpells != null && g.LeadsToSpells.Contains(n1));
        var opener = selector is null ? null : all.FirstOrDefault(g => g.LeadsToSpells != null && g.LeadsToSpells.Contains(selector));
        var group = selector?.transform.parent;
        if (n1 is null || selector is null || opener is null || group is null || !n1.transform.IsChildOf(group))
        {
            Diag.Warn($"content: the rune branch of {d.Runes} is not laid out as expected; school {d.Key} cannot be drawn");
            return;
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
        foreach (var l in clone.GetComponentsInChildren<SpellLabel>(true))
        {
            if (l.skillMode && l.skillType == d.Runes) l.SetSkill(d.Type);
            else if (l.spell == s1) l.SetSpell(c1.Type);
            else if (l.spell == s2) l.SetSpell(c2.Type);
        }
        if (selClone is null) { UnityEngine.Object.Destroy(clone); Diag.Warn($"content: no selector in the {d.Runes} branch"); return; }

        opener.LeadsToSpells.Add(selClone);
        foreach (var n in nodes)
        {
            gms.AllSpells.Add(n);
            try { n.Init(); } catch (Exception e) { Diag.Warn($"content: rune node init: {e.Message}"); }
        }
        bool remapped = selClone.LeadsToSpells != null && selClone.LeadsToSpells.ToArray().All(x => x is not null && x.transform.IsChildOf(clone.transform));
        string line = $"school {d.Key}: runes of {d.Runes} copied ({nodes.Length} nodes, links inside the copy {remapped}); " +
                      $"{c1.Key} draws like {s1}, {c2.Key} like {s2}";
        Report.Add(line);
        Diag.Info("content: " + line);
    }
}
