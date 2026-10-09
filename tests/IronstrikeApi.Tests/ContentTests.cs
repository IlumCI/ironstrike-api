using System;
using System.Collections.Generic;
using System.Linq;
using IronstrikeApi.Content;
using IronstrikeApi.Net;
using Xunit;

namespace IronstrikeApi.Tests;

public class ContentIdTests
{
    static readonly HashSet<int> GameSkills = new(Enumerable.Range(0, 232).Where(i => i % 2 == 0));   // like the game: gaps below 232

    [Fact]
    public void Ids_come_from_the_top_and_skip_the_game()
    {
        var ids = ContentIds.Allocate(new[] { "b.two", "a.one", "c.three" }, new HashSet<int> { 255, 253 });
        Assert.Equal(254, ids["a.one"]);
        Assert.Equal(252, ids["b.two"]);
        Assert.Equal(251, ids["c.three"]);
    }

    [Fact]
    public void Registration_order_does_not_matter()
    {
        var keys = Enumerable.Range(0, 40).Select(i => $"mod{i % 3}.skill{i}").ToList();
        var a = ContentIds.Allocate(keys, GameSkills);
        var shuffled = keys.OrderBy(_ => Guid.NewGuid()).ToList();
        var b = ContentIds.Allocate(shuffled, GameSkills);
        Assert.Equal(a.OrderBy(x => x.Key), b.OrderBy(x => x.Key));
        Assert.Equal(ContentIds.Manifest(a.Select(x => ("skill", x.Key, x.Value))), ContentIds.Manifest(b.Select(x => ("skill", x.Key, x.Value))));
        Assert.All(a.Values, id => Assert.DoesNotContain(id, GameSkills));
        Assert.Equal(a.Count, a.Values.Distinct().Count());
        Assert.All(a.Values, id => Assert.InRange(id, 1, 255));
    }

    [Fact]
    public void Running_out_of_ids_is_a_clear_error()
    {
        var taken = new HashSet<int>(Enumerable.Range(0, 254));                // only 254 and 255 free
        ContentIds.Allocate(new[] { "a.x", "a.y" }, taken);
        var e = Assert.Throws<InvalidOperationException>(() => ContentIds.Allocate(new[] { "a.x", "a.y", "a.z" }, taken));
        Assert.Contains("a.z", e.Message);
    }

    [Fact]
    public void Any_difference_changes_the_manifest()
    {
        var a = new[] { ("skill", "m.a", 255), ("skill", "m.b", 254) };
        Assert.NotEqual(ContentIds.Manifest(a), ContentIds.Manifest(new[] { ("skill", "m.a", 255) }));
        Assert.NotEqual(ContentIds.Manifest(a), ContentIds.Manifest(new[] { ("skill", "m.a", 255), ("skill", "m.b", 253) }));
        Assert.NotEqual(ContentIds.Manifest(a), ContentIds.Manifest(new[] { ("skill", "m.a", 255), ("spell", "m.b", 254) }));
        Assert.Equal(8, ContentIds.Manifest(a).Length);
    }

    [Theory]
    [InlineData("mymod.berserk", true)]
    [InlineData("my-mod.blood_lust2", true)]
    [InlineData("berserk", false)]                 // no mod prefix
    [InlineData("MyMod.Berserk", false)]           // capitals would make ids depend on spelling
    [InlineData("my mod.x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("mymod.ünicode", false)]
    public void Keys_are_validated(string key, bool ok)
        => Assert.Equal(ok, ContentIds.Validate(key) == null);
}

public class OfferTests
{
    static CustomSkill Skill(int id, float weight) => new CustomSkillProbe(id, weight).Skill;

    sealed class CustomSkillProbe
    {
        public CustomSkill Skill;
        public CustomSkillProbe(int id, float w)
        {
            Skill = (CustomSkill)Activator.CreateInstance(typeof(CustomSkill), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new object[] { "t.s" + id }, null);
            Skill.Id = id;
            Skill.OfferWeight = w;
        }
    }

    [Fact]
    public void The_number_of_cards_never_changes_and_nothing_repeats()
    {
        var game = new List<int> { 1, 2, 3 };
        var rnd = new Random(3);
        for (int i = 0; i < 2000; i++)
        {
            var o = SkillPatches.Offers(game, new List<CustomSkill> { Skill(250, 1), Skill(251, 5), Skill(252, 0.01f) }, rnd);
            Assert.Equal(3, o.Count);
            Assert.Equal(3, o.Distinct().Count());
        }
    }

    [Fact]
    public void Weight_sets_how_often_a_skill_turns_up()
    {
        var game = new List<int> { 1, 2, 3 };
        var rnd = new Random(7);
        int heavy = 0, light = 0, n = 5000;
        for (int i = 0; i < n; i++)
        {
            var o = SkillPatches.Offers(game, new List<CustomSkill> { Skill(250, 3), Skill(251, 0.2f) }, rnd);
            if (o.Contains(250)) heavy++;
            if (o.Contains(251)) light++;
        }
        Assert.InRange(heavy / (double)n, 0.6, 0.98);
        Assert.InRange(light / (double)n, 0.03, 0.3);
    }

    [Fact]
    public void Nothing_to_mix_leaves_the_game_alone()
    {
        var game = new List<int> { 4, 5, 6 };
        Assert.Same(game, SkillPatches.Offers(game, new List<CustomSkill>(), new Random(1)));
        Assert.Empty(SkillPatches.Offers(new List<int>(), new List<CustomSkill> { Skill(250, 1) }, new Random(1)));
    }

    [Theory]
    [InlineData(1, "I")] [InlineData(3, "III")] [InlineData(5, "V")] [InlineData(0, "I")] [InlineData(7, "II")] [InlineData(10, "V")]
    public void Levels_read_like_the_game(int level, string numeral) => Assert.Equal(numeral, SkillPatches.Roman(level));
}

public class ManifestNetTests
{
    [Fact]
    public void Peers_with_different_content_are_noticed_and_equal_ones_are_not()
    {
        var mismatches = new List<string>();
        var net = new SimNet();
        net.Add(1);
        net.Add(2);
        net.Add(3);
        net.Host.Core.Manifest = () => new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        net.Nodes[2].Core.Manifest = () => new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        net.Nodes[3].Core.Manifest = () => new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 };
        foreach (var n in net.Nodes.Values) { var id = n.Id; n.Core.ManifestMismatch = p => mismatches.Add($"{id} saw {p}"); }
        net.Pump();
        Assert.Contains("3 saw 1", mismatches);          // the odd client notices the host
        Assert.Contains("1 saw 3", mismatches);          // and the host notices it
        Assert.DoesNotContain(mismatches, m => m.StartsWith("2 "));
        Assert.DoesNotContain("1 saw 2", mismatches);
    }

    [Fact]
    public void No_content_on_either_side_is_a_match()
    {
        bool mismatch = false;
        var net = new SimNet();
        net.Add(1); net.Add(2);
        foreach (var n in net.Nodes.Values) n.Core.ManifestMismatch = _ => mismatch = true;
        net.Pump();
        Assert.False(mismatch);
    }
}

public class IconTests
{
    [Fact]
    public void Visible_pixels_are_found()
    {
        var a = new byte[10 * 8];
        a[2 * 10 + 3] = 255;      // (3,2)
        a[6 * 10 + 7] = 200;      // (7,6)
        a[0] = 10;                // too faint to count
        Assert.Equal((3, 2, 5, 5), Icons.Visible(a, 10, 8));
        Assert.Equal((0, 0, 10, 8), Icons.Visible(new byte[80], 10, 8));
    }
}

public class OfferTwiceTests
{
    [Fact]
    public void A_list_that_already_holds_a_custom_skill_never_gets_it_twice()
    {
        var s = (CustomSkill)Activator.CreateInstance(typeof(CustomSkill), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new object[] { "t.x" }, null);
        s.Id = 250; s.OfferWeight = 1000;
        var rnd = new Random(1);
        for (int i = 0; i < 500; i++)
        {
            var o = SkillPatches.Offers(new List<int> { 250, 2, 3 }, new List<CustomSkill> { s }, rnd);
            Assert.Equal(3, o.Distinct().Count());
        }
    }
}

public class IconCanvasTests
{
    [Theory]
    [InlineData(62, 31, 100)]
    [InlineData(230, 180, 371)]
    [InlineData(1, 1, 2)]
    public void Drawings_get_the_games_margin(int w, int h, int side) => Assert.Equal(side, Icons.Canvas(w, h));
}

[Collection("ModContent")]
public class SpellRegistryTests : IDisposable
{
    public SpellRegistryTests() => ModContent.ResetForTests();
    public void Dispose() => ModContent.ResetForTests();

    static void TwoSpells(string a = "t.one", string b = "t.two")
    {
        ModContent.Spell(a, s => s.Name = "One");
        ModContent.Spell(b, s => { s.Name = "Two"; s.Targeting = SpellTargetingType.GroundCircle; });
    }

    [Fact]
    public void A_school_teaches_two_registered_spells()
    {
        TwoSpells();
        var sc = ModContent.School("t.school", s => { s.Spells.Add("t.one"); s.Spells.Add("t.two"); });
        Assert.Same(sc, ModContent.GetSpell("t.one").School);
        Assert.Same(sc, ModContent.GetSpell("t.two").School);
    }

    [Fact]
    public void School_mistakes_are_caught_at_registration()
    {
        TwoSpells();
        Assert.Throws<ArgumentException>(() => ModContent.School("t.a", s => s.Spells.Add("t.one")));                                  // one spell
        Assert.Throws<ArgumentException>(() => ModContent.School("t.b", s => { s.Spells.Add("t.one"); s.Spells.Add("t.nope"); }));     // unknown spell
        Assert.Throws<ArgumentException>(() => ModContent.School("t.c", s => { s.Spells.Add("t.one"); s.Spells.Add("t.one"); }));      // same twice
        Assert.Throws<ArgumentException>(() => ModContent.School("t.d", s => { s.Spells.Add("t.one"); s.Spells.Add("t.two"); s.Runes = SkillType.Toughness; }));
        ModContent.School("t.e", s => { s.Spells.Add("t.one"); s.Spells.Add("t.two"); });
        ModContent.Spell("t.three", s => { });
        ModContent.Spell("t.four", s => { });
        // A spell already taught by another school.
        Assert.Throws<ArgumentException>(() => ModContent.School("t.f", s => { s.Spells.Add("t.one"); s.Spells.Add("t.three"); }));
        // Two schools may prefer the same rune branch: branches are assigned per player while playing.
        ModContent.School("t.h", s => { s.Spells.Add("t.three"); s.Spells.Add("t.four"); });
    }

    [Fact]
    public void Spell_mistakes_are_caught_at_registration()
    {
        Assert.Throws<ArgumentException>(() => ModContent.Spell("t.x", s => s.Cooldown = new float[0]));
        Assert.Throws<ArgumentException>(() => ModContent.Spell("t.y", s => s.ManaCost = new[] { -1f }));
        Assert.Throws<ArgumentException>(() => ModContent.Spell("t.z", s => s.Targeting = SpellTargetingType.None));
        Assert.Throws<ArgumentException>(() => ModContent.Spell("nodot", s => { }));
        ModContent.Spell("t.ok", s => { });
        Assert.Throws<ArgumentException>(() => ModContent.Spell("t.ok", s => { }));
    }

    [Fact]
    public void Schools_share_the_skill_ids_and_spells_have_their_own()
    {
        TwoSpells();
        ModContent.School("t.school", s => { s.Spells.Add("t.one"); s.Spells.Add("t.two"); });
        ModContent.Skill("t.skill", s => { });
        ModContent.Freeze();
        var school = ModContent.GetSchool("t.school");
        var skill = ModContent.GetSkill("t.skill");
        Assert.NotEqual(school.Id, skill.Id);
        Assert.All(new[] { school.Id, skill.Id }, id => Assert.False(Enum.IsDefined(typeof(SkillType), id)));
        Assert.All(new[] { ModContent.GetSpell("t.one").Id, ModContent.GetSpell("t.two").Id }, id =>
        {
            Assert.InRange(id, 1, 255);
            Assert.False(Enum.IsDefined(typeof(SpellType), id));
        });
        Assert.Equal(8, ModContent.Manifest.Length);
        Assert.Throws<InvalidOperationException>(() => ModContent.Spell("t.late", s => { }));
    }

    [Fact]
    public void Per_level_values_repeat_the_last()
    {
        var v = new[] { 10f, 8f };
        Assert.Equal(10f, CustomSpell.At(v, 1));
        Assert.Equal(8f, CustomSpell.At(v, 2));
        Assert.Equal(8f, CustomSpell.At(v, 5));
        Assert.Equal(10f, CustomSpell.At(v, 0));
        Assert.Equal(10f, CustomSpell.At(v, 6));                                  // enhanced stage I: its plain value
        Assert.Equal(7f, CustomSpell.At(new[] { 10f, 9f, 8f, 8f, 8f, 7f }, 6));    // unless given
        Assert.Equal(8f, CustomSpell.At(v, 7));
    }
}
