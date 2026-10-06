using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Configuration;
using Fusion;
using IronstrikeApi.Gameplay;
using Xunit;

namespace IronstrikeApi.Tests;

// Tests that capture the API's own log output share this, one at a time.
[CollectionDefinition("log", DisableParallelization = true)]
public class LogCollection { }

public sealed class LogCapture : IDisposable
{
    public readonly List<string> Lines = new();
    public LogCapture() => Diag.Sink = s => { lock (Lines) Lines.Add(s); };
    public void Dispose() => Diag.Sink = null;
}

[Collection("log")]
public class StatEngineTests
{
    static float Run(StatEngine e, float v, bool local = true) => e.Apply(SkillCalcType.MoveSpeed, null, _ => local, v);

    [Fact]
    public void Order_then_insertion_decides_the_chain()
    {
        var e = new StatEngine();
        var trace = new List<string>();
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => { trace.Add("b0"); return v + 1; }, 0, false, null);
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => { trace.Add("a-5"); return v * 10; }, -5, false, null);
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => { trace.Add("c0"); return v + 2; }, 0, false, null);
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => { trace.Add("d9"); return v / 2; }, 9, false, null);
        Assert.Equal(((1f * 10) + 1 + 2) / 2, Run(e, 1f));
        Assert.Equal(new[] { "a-5", "b0", "c0", "d9" }, trace);
    }

    [Fact]
    public void Other_stats_are_untouched()
    {
        var e = new StatEngine();
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => 99, 0, false, null);
        Assert.Equal(3f, e.Apply(SkillCalcType.Jumps, null, _ => true, 3f));
        Assert.False(e.Has(SkillCalcType.Jumps));
    }

    [Fact]
    public void Dispose_is_idempotent_and_takes_effect_mid_pass()
    {
        var e = new StatEngine();
        IDisposable later = null, self = null;
        self = e.Add(SkillCalcType.MoveSpeed, (f, s, v) => { later.Dispose(); self.Dispose(); return v + 1; }, 0, false, null);
        later = e.Add(SkillCalcType.MoveSpeed, (f, s, v) => v + 100, 1, false, null);
        Assert.Equal(2f, Run(e, 1f));         // "later" removed before its turn
        Assert.Equal(1f, Run(e, 1f));         // both gone
        self.Dispose(); later.Dispose();      // twice is fine
        Assert.Equal(0, e.Count);
    }

    [Fact]
    public void Added_mid_pass_waits_for_the_next_pass()
    {
        var e = new StatEngine();
        bool added = false;
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) =>
        {
            if (!added) { added = true; e.Add(SkillCalcType.MoveSpeed, (f2, s2, v2) => v2 * 3, 99, false, null); }
            return v + 1;
        }, 0, false, null);
        Assert.Equal(2f, Run(e, 1f));
        Assert.Equal(6f, Run(e, 1f));
    }

    [Fact]
    public void A_throwing_modifier_is_blamed_muted_and_does_not_stop_the_rest()
    {
        Safe.ResetReports();
        using var log = new LogCapture();
        var e = new StatEngine();
        StatModifier bad = (f, s, v) => throw new InvalidOperationException("broken mod");
        e.Add(SkillCalcType.MoveSpeed, bad, 0, false, bad);
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => v + 1, 1, false, null);
        for (int i = 0; i < 1000; i++) Assert.Equal(2f, Run(e, 1f));
        int errors = log.Lines.Count(l => l.StartsWith("E ") && l.Contains("broken mod"));
        Assert.Equal(StatEngine.MaxFailures, errors);
        Assert.Contains(log.Lines, l => l.Contains("muted"));
    }

    [Fact]
    public void Non_finite_results_are_ignored()
    {
        var e = new StatEngine();
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => float.NaN, 0, false, null);
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => float.PositiveInfinity, 1, false, null);
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => float.NegativeInfinity, 2, false, null);
        Assert.Equal(5f, Run(e, 5f));
    }

    [Fact]
    public void Local_only_modifiers_skip_other_fighters_and_ask_once()
    {
        var e = new StatEngine();
        int asked = 0;
        for (int i = 0; i < 10; i++) e.Add(SkillCalcType.MoveSpeed, (f, s, v) => v + 1, 0, true, null);
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => v * 2, 5, false, null);
        // A null fighter is never "local".
        Assert.Equal(2f, e.Apply(SkillCalcType.MoveSpeed, null, _ => { asked++; return true; }, 1f));
        Assert.Equal(0, asked);
    }

    [Fact]
    public void A_modifier_that_reads_its_own_stat_does_not_recurse_forever()
    {
        var e = new StatEngine();
        int calls = 0;
        e.Add(SkillCalcType.MoveSpeed, (f, s, v) => { calls++; return e.Apply(s, f, _ => true, v) + 1; }, 0, false, null);
        float r = Run(e, 0f);
        Assert.Equal(StatEngine.MaxDepth, calls);
        Assert.Equal(StatEngine.MaxDepth, r);
        Assert.Equal(1f, Run(e, 0f) - (StatEngine.MaxDepth - 1));   // depth counter fully unwound
    }

    [Fact]
    public void Ten_thousand_modifiers_stay_cheap_and_allocation_free()
    {
        var e = new StatEngine();
        for (int i = 0; i < 10000; i++) e.Add(SkillCalcType.MoveSpeed, (f, s, v) => v + 0.0001f, i % 7, false, null);
        Run(e, 0f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) Run(e, 0f);
        sw.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms for 1M modifier calls");
        Assert.True(allocated < 64 * 1024, $"{allocated} bytes allocated reading stats");
    }

    [Theory]
    [InlineData(2f, 1.5f, 3f)]
    [InlineData(0f, 1.5f, 0.5f)]      // the floor: a 0 bonus still gets +0.5
    [InlineData(4f, 1f, 4f)]
    [InlineData(4f, 0f, 0f)]
    [InlineData(4f, -3f, 0f)]          // negative factors count as 0
    [InlineData(4f, float.NaN, 4f)]
    [InlineData(4f, float.PositiveInfinity, 4f)]
    [InlineData(-2f, 2f, 1f)]
    public void Scale_handles_odd_inputs(float v, float factor, float expected)
        => Assert.Equal(expected, Stats.Scale(v, factor), 3);
}

[Collection("log")]
public class DispatchTests
{
    [Fact]
    public void One_broken_handler_does_not_stop_the_others()
    {
        Safe.ResetReports();
        using var log = new LogCapture();
        int good = 0;
        Action<int> d = null;
        d += _ => good++;
        d += _ => throw new Exception("bad handler");
        d += _ => good++;
        for (int i = 0; i < 20; i++) Safe.Run(d, "Test", i);
        Assert.Equal(40, good);
        Assert.Equal(5, log.Lines.Count(l => l.Contains("bad handler")));
    }

    [Fact]
    public void Handlers_may_unsubscribe_and_subscribe_while_being_called()
    {
        var calls = new List<string>();
        Action evt = null;
        Action a = null, b = () => calls.Add("b");
        a = () => { calls.Add("a"); evt -= a; evt += () => calls.Add("new"); };
        evt += a; evt += b;
        Safe.Run(evt, "Test");
        Assert.Equal(new[] { "a", "b" }, calls);       // the snapshot runs unchanged
        calls.Clear();
        Safe.Run(evt, "Test");
        Assert.Equal(new[] { "b", "new" }, calls);
    }

    [Fact]
    public void Null_and_empty_are_fine()
    {
        Safe.Run((Action)null, "Test");
        Safe.Run((Action<int>)null, "Test", 1);
        Safe.Run((Delegate)null, "Test");
    }

    [Fact]
    public void Blame_works_before_any_mod_registry_exists()
    {
        Safe.ResetReports();
        using var log = new LogCapture();
        Safe.Blame(null, "early", new Exception("x"));
        Action h = () => { };
        Safe.Blame(h, "early", new Exception("y"));
        Assert.Equal(2, log.Lines.Count);
        Assert.Contains("IronstrikeApi.Tests", log.Lines[1]);   // the assembly name stands in for the mod
    }

    [Fact]
    public void Events_with_throwing_handlers_and_no_config_do_not_throw()
    {
        Safe.ResetReports();
        using var log = new LogCapture();
        Action<RunOutcome> bad = _ => throw new Exception("handler");
        GameEvents.RunEnded += bad;
        try { for (int i = 0; i < 10; i++) GameEvents.RaiseRunEnded(RunOutcome.Won); }
        finally { GameEvents.RunEnded -= bad; }
        Assert.Equal(5, log.Lines.Count(l => l.Contains("handler")));
    }
}

public class SessionTests
{
    [Theory]
    [InlineData(GameMode.Single, false, false, false, PlayContext.Solo)]
    [InlineData(GameMode.Single, true, true, true, PlayContext.Solo)]
    [InlineData(GameMode.Host, true, false, false, PlayContext.ModdedServer)]
    [InlineData(GameMode.Client, false, true, false, PlayContext.ModdedServer)]
    [InlineData(GameMode.AutoHostOrClient, false, false, true, PlayContext.PrivateMatch)]
    [InlineData(GameMode.Host, false, false, true, PlayContext.PrivateMatch)]
    [InlineData(GameMode.Client, false, false, true, PlayContext.PrivateMatch)]
    [InlineData(GameMode.Host, false, false, false, PlayContext.Public)]
    [InlineData(GameMode.AutoHostOrClient, false, false, false, PlayContext.Public)]
    [InlineData(GameMode.Shared, false, false, true, PlayContext.Public)]      // never used by the game: not trusted
    [InlineData(GameMode.Server, false, false, true, PlayContext.Public)]      // dedicated server path
    [InlineData((GameMode)0, false, false, true, PlayContext.Public)]         // garbage
    [InlineData((GameMode)99, false, false, false, PlayContext.Public)]
    public void Only_known_private_sessions_allow_gameplay(GameMode mode, bool lobby, bool servers, bool privateEntry, PlayContext expected)
    {
        Assert.Equal(expected, Events.SessionHooks.Classify(mode, lobby, servers, privateEntry));
    }

    [Fact]
    public void Public_is_the_only_disallowed_context()
    {
        foreach (PlayContext c in Enum.GetValues(typeof(PlayContext)))
        {
            Safety.Context = c;
            Assert.Equal(c != PlayContext.Public, Safety.GameplayAllowed);
        }
        Safety.Context = PlayContext.Offline;
    }
}

public class FingerprintTests
{
    // The Ironstrike Servers formula, written out independently.
    static string Servers(IEnumerable<string> mods)
    {
        var l = mods.ToList();
        l.Sort(StringComparer.Ordinal);
        var d = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", l)));
        return Convert.ToHexString(d, 0, 8).ToLowerInvariant();
    }

    [Fact]
    public void Matches_Ironstrike_Servers_and_ignores_load_order()
    {
        var set = new[] { "eu.euroswarms.ironstrike.trainer@1.0.3", "eu.euroswarms.ironstrike.api@0.1.0", "eu.euroswarms.ironstrike.servers@0.1.0" };
        Assert.Equal(Servers(set), Mods.Fingerprint(set));
        Assert.Equal(Mods.Fingerprint(set), Mods.Fingerprint(set.Reverse()));
        Assert.Equal(16, Mods.Fingerprint(set).Length);
    }

    [Fact]
    public void Any_difference_changes_it()
    {
        var a = new[] { "a@1.0.0", "b@1.0.0" };
        Assert.NotEqual(Mods.Fingerprint(a), Mods.Fingerprint(new[] { "a@1.0.0", "b@1.0.1" }));
        Assert.NotEqual(Mods.Fingerprint(a), Mods.Fingerprint(new[] { "a@1.0.0" }));
        Assert.NotEqual(Mods.Fingerprint(a), Mods.Fingerprint(new[] { "a@1.0.0", "b@1.0.0", "b@1.0.0" }));
        Assert.Equal(Servers(Array.Empty<string>()), Mods.Fingerprint(Array.Empty<string>()));
        // Ordinal, not culture, sorting: case matters and is stable everywhere.
        Assert.Equal(Servers(new[] { "B@1", "a@1" }), Mods.Fingerprint(new[] { "a@1", "B@1" }));
    }
}

public class RedactTests
{
    [Fact]
    public void Never_contains_the_value_and_is_stable()
    {
        foreach (var s in new[] { "12345678", "SteamName", "a", "ÜñíçødÉ 名前", new string('x', 10000) })
        {
            var r = ApiLog.Redact(s);
            Assert.DoesNotContain(s, r);
            Assert.Equal(r, ApiLog.Redact(s));
            Assert.Matches(@"^<len\d+:[0-9a-f]{6}>$", r);
        }
        Assert.Equal("<empty>", ApiLog.Redact(null));
        Assert.Equal("<empty>", ApiLog.Redact(""));
        Assert.NotEqual(ApiLog.Redact("12345678"), ApiLog.Redact("12345679"));
    }
}

[Collection("log")]
public class SettingsTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), "ism-test-" + Guid.NewGuid() + ".cfg");
    readonly ConfigFile cfg;

    public SettingsTests() => cfg = new ConfigFile(path, true);
    public void Dispose() { try { File.Delete(path); } catch { } }

    enum Mode { Off, Low, High }
    [Flags] enum Bits { None = 0, A = 1, B = 2, C = 4 }

    ModSettings.ControlSpec Spec<T>(string key, T value, AcceptableValueBase acc = null, string desc = "d", params object[] tags)
        => ModSettings.Classify(cfg.Bind("S", key, value, new ConfigDescription(desc, acc, tags)));

    [Fact]
    public void Common_types_get_the_obvious_controls()
    {
        Assert.Equal(ModSettings.ControlKind.Toggle, Spec("b", true).Kind);
        Assert.Equal(ModSettings.ControlKind.Choice, Spec("e", Mode.Low).Kind);
        Assert.Equal(ModSettings.ControlKind.Stepper, Spec("i", 4).Kind);
        Assert.Equal(ModSettings.ControlKind.Stepper, Spec("f", 1.25f).Kind);
        Assert.Equal(ModSettings.ControlKind.Text, Spec("s", "hello").Kind);
    }

    [Fact]
    public void Ranges_set_steps_and_bounds()
    {
        var s = Spec("speed", 1.25f, new AcceptableValueRange<float>(0.5f, 3f));
        Assert.Equal(0.5, s.Min, 4); Assert.Equal(3, s.Max, 4);
        Assert.InRange(s.Step, 0.1, 0.25);
        var i = Spec("players", 4, new AcceptableValueRange<int>(2, 8));
        Assert.Equal(1, i.Step);
        var wide = Spec("wide", 50, new AcceptableValueRange<int>(0, 1000));
        Assert.Equal(50, wide.Step);
    }

    [Fact]
    public void Steppers_clamp_and_convert()
    {
        var e = cfg.Bind("S", "clamp", 4, new ConfigDescription("d", new AcceptableValueRange<int>(2, 8)));
        var s = ModSettings.Classify(e);
        Assert.True(ModSettings.TrySet(e, s, 100.0)); Assert.Equal(8, e.Value);
        Assert.True(ModSettings.TrySet(e, s, -100.0)); Assert.Equal(2, e.Value);
        Assert.True(ModSettings.TrySet(e, s, 4.6)); Assert.Equal(5, e.Value);
        Assert.False(ModSettings.TrySet(e, s, double.NaN)); Assert.Equal(5, e.Value);
    }

    [Fact]
    public void Odd_number_types_are_editable_and_never_overflow()
    {
        var b = cfg.Bind("S", "byte", (byte)10);
        var sb = ModSettings.Classify(b);
        Assert.Equal(ModSettings.ControlKind.Stepper, sb.Kind);
        Assert.True(ModSettings.TrySet(b, sb, 300.0)); Assert.Equal(255, b.Value);
        Assert.True(ModSettings.TrySet(b, sb, -5.0)); Assert.Equal(0, b.Value);

        var u = cfg.Bind("S", "uint", 7u);
        Assert.True(ModSettings.TrySet(u, ModSettings.Classify(u), -1.0)); Assert.Equal(0u, u.Value);

        var d = cfg.Bind("S", "dec", 1.5m);
        var sd = ModSettings.Classify(d);
        Assert.Equal(ModSettings.ControlKind.Stepper, sd.Kind);
        Assert.True(ModSettings.TrySet(d, sd, 2.5)); Assert.Equal(2.5m, d.Value);
    }

    [Fact]
    public void Huge_whole_numbers_are_typed_exactly_not_stepped()
    {
        var e = cfg.Bind("S", "seed", 9_007_199_254_740_993L);   // not representable as a double
        var s = ModSettings.Classify(e);
        Assert.Equal(ModSettings.ControlKind.Text, s.Kind);
        Assert.True(ModSettings.TrySet(e, s, "9007199254740995"));
        Assert.Equal(9_007_199_254_740_995L, e.Value);
        Assert.False(ModSettings.TrySet(e, s, "not a number"));
        Assert.False(ModSettings.TrySet(e, s, ""));
        Assert.Equal(9_007_199_254_740_995L, e.Value);
        Assert.True(ModSettings.TrySet(e, s, "99999999999999999999999"));   // clamps, never throws
        Assert.Equal(long.MaxValue, e.Value);
    }

    [Fact]
    public void Typed_floats_use_invariant_culture_and_reject_junk()
    {
        var e = cfg.Bind("S", "big", 5e7f);
        var s = ModSettings.Classify(e);
        Assert.Equal(ModSettings.ControlKind.Text, s.Kind);
        Assert.True(ModSettings.TrySet(e, s, " 1.5e3 "));
        Assert.Equal(1500f, e.Value);
        Assert.False(ModSettings.TrySet(e, s, "NaN"));
        Assert.False(ModSettings.TrySet(e, s, "Infinity"));
        Assert.False(ModSettings.TrySet(e, s, "1,5"));
    }

    [Fact]
    public void Enums_keep_values_that_have_no_name()
    {
        var e = cfg.Bind("S", "mode", (Mode)42);
        var s = ModSettings.Classify(e);
        Assert.Equal(ModSettings.ControlKind.Choice, s.Kind);
        Assert.Equal(4, s.Options.Length);
        Assert.Equal("42", s.Options[s.Index]);
        Assert.True(ModSettings.TrySet(e, s, 1));
        Assert.Equal(Mode.Low, e.Value);
    }

    [Fact]
    public void Flag_combinations_are_shown_not_mangled()
    {
        var e = cfg.Bind("S", "bits", Bits.A | Bits.C);
        Assert.Equal(ModSettings.ControlKind.Info, ModSettings.Classify(e).Kind);
    }

    [Fact]
    public void Any_acceptable_value_list_becomes_a_picker()
    {
        var s = Spec("level", 3, new AcceptableValueList<int>(1, 3, 5));
        Assert.Equal(ModSettings.ControlKind.Choice, s.Kind);
        Assert.Equal(new[] { "1", "3", "5" }, s.Options);
        Assert.Equal(1, s.Index);
        var r = Spec("region", "eu", new AcceptableValueList<string>("us", "eu", "asia"));
        Assert.Equal(1, r.Index);
    }

    [Fact]
    public void Visibility_and_secrecy_rules()
    {
        Assert.False(ModSettings.Shown(cfg.Bind("S", "SafeMode", true)));
        Assert.False(ModSettings.Shown(cfg.Bind("S", "safemode2", true, new ConfigDescription("x", null, ModSettings.Hidden))));
        Assert.False(ModSettings.Shown(cfg.Bind("S", "Favorites", "", "Managed by the mod. Join codes.")));
        Assert.True(ModSettings.Shown(cfg.Bind("S", "Visible", 1)));

        Assert.True(Spec("Password", "hunter2").Secret);
        Assert.True(Spec("SavedCode", "1234").Secret);
        Assert.True(Spec("ApiToken", "x").Secret);
        Assert.True(Spec("Motto", "x", null, "d", ModSettings.Secret).Secret);
        Assert.False(Spec("ServerName", "x").Secret);
    }

    [Fact]
    public void Config_text_is_never_read_as_markup()
    {
        Assert.Equal("<noparse>x < 5 <color=red></noparse>", ModSettings.Plain("x < 5 <color=red>"));
        Assert.Equal("<noparse>a</noparse>", ModSettings.Plain("a</noparse>"));     // cannot be closed early
        Assert.Null(ModSettings.Plain(null));
    }

    [Fact]
    public void Help_is_one_short_line()
    {
        var s = Spec("h", 1, null, new string('w', 300) + "\nsecond line");
        Assert.True(s.Help.Length <= 90);
        Assert.DoesNotContain("second", s.Help);
        Assert.Null(Spec("h2", 1, null, "   ").Help);
    }

    [Fact]
    public void Long_strings_are_not_truncated_by_editing()
    {
        var s = Spec("long", new string('a', 500));
        Assert.True(s.MaxLength >= 500);
    }
}

[Collection("log")]
public class FloodTests
{
    [Fact]
    public void Many_broken_handlers_cannot_flood_the_log()
    {
        Safe.ResetReports();
        Safe.ResetReports();
        using var log = new LogCapture();
        Action d = null;
        for (int i = 0; i < 2000; i++) { int k = i; d += () => throw new Exception("broken " + k); }
        for (int frame = 0; frame < 50; frame++) Safe.Run(d, "Update");
        int full = log.Lines.Count(l => l.Contains("broken "));
        Assert.Equal(Safe.MaxReports, full);
        Assert.True(log.Lines.Count <= Safe.MaxReports + 5, $"{log.Lines.Count} log lines");
        Safe.ResetReports();
    }
}
