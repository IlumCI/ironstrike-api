using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;

namespace IronstrikeApi;

/// <summary>
/// Settings in the Mods window. Every loaded mod gets a page there, with a control for each entry of
/// its BepInEx config file, without doing anything. This class lets a mod tune that page.
/// </summary>
/// <remarks>
/// <para>Controls are chosen by type: <c>bool</c> becomes a check box; numbers a stepper (using the
/// entry's <c>AcceptableValueRange</c> if it has one); enums and <c>AcceptableValueList</c> strings
/// a picker; other strings a text field using the game's keyboard.</para>
/// <para>Hidden automatically: entries tagged <see cref="Hidden"/>, entries whose description starts
/// with "Managed by the mod", and any <c>SafeMode</c> switch (public play stays locked).</para>
/// <para>Strings whose key looks private (password, code, id, token) are shown as dots.</para>
/// </remarks>
public static class ModSettings
{
    /// <summary>
    /// Tag for a config entry that should not appear in the Mods window:
    /// <c>Config.Bind("Debug", "Trace", false, new ConfigDescription("...", null, ModSettings.Hidden))</c>.
    /// </summary>
    public static readonly object Hidden = new HiddenTag();

    /// <summary>Tag for a string entry to show as dots, whatever its key is called.</summary>
    public static readonly object Secret = new SecretTag();

    sealed class HiddenTag { public override string ToString() => "Hidden"; }
    sealed class SecretTag { public override string ToString() => "Secret"; }

    static readonly Dictionary<string, List<Action<Ui.Page>>> extras = new();
    static readonly Regex PrivateKey = new("pass|code|token|secret|installid|banned|favorite|recent", RegexOptions.IgnoreCase);

    /// <summary>
    /// Adds your own rows (buttons, info, anything a <see cref="Ui.Page"/> can draw) to your mod's
    /// page in the Mods window, above its config entries.
    /// </summary>
    /// <param name="guid">Your plugin's GUID.</param>
    /// <param name="render">Draws the rows.</param>
    public static void AddSection(string guid, Action<Ui.Page> render)
    {
        if (render == null) throw new ArgumentNullException(nameof(render));
        if (!extras.TryGetValue(guid, out var l)) extras[guid] = l = new List<Action<Ui.Page>>();
        l.Add(render);
    }

    internal static IEnumerable<Action<Ui.Page>> Sections(string guid)
        => extras.TryGetValue(guid, out var l) ? l : Enumerable.Empty<Action<Ui.Page>>();

    internal static bool Shown(ConfigEntryBase e)
    {
        var tags = e.Description?.Tags;
        if (tags != null && tags.Contains(Hidden)) return false;
        if ((e.Description?.Description ?? "").StartsWith("Managed by the mod", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(e.Definition.Key, "SafeMode", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    static bool IsSecret(ConfigEntryBase e)
    {
        var tags = e.Description?.Tags;
        return (tags != null && tags.Contains(Secret)) || PrivateKey.IsMatch(e.Definition.Key);
    }

    // One control per entry, grouped under the config file's own section headers.
    internal static void Draw(Ui.Page page, ConfigFile cfg)
    {
        if (cfg == null) { page.Text("This mod has no config file.", muted: true); return; }
        var entries = cfg.Keys.Select(k => cfg[k]).Where(Shown)
                         .OrderBy(e => e.Definition.Section, StringComparer.Ordinal).ToList();
        if (entries.Count == 0) { page.Text("No settings.", muted: true); return; }

        string section = null;
        foreach (var e in entries)
        {
            if (e.Definition.Section != section)
            {
                section = e.Definition.Section;
                page.Header(Pretty(section));
            }
            try { Control(page, cfg, e); }
            catch (Exception ex)
            {
                page.Info(e.Definition.Key, "(cannot edit here)");
                ApiLog.WarnOnce(null, "cfg:" + e.Definition, $"settings: {e.Definition}: {ex.Message}");
            }
        }
    }

    // "03 Host" -> "Host": the numbers only keep the file in order.
    static string Pretty(string section) => Regex.Replace(section ?? "", @"^\d+\s+", "");

    static string Help(ConfigEntryBase e)
    {
        string d = (e.Description?.Description ?? "").Split('\n')[0].Trim();
        return d.Length == 0 ? null : d.Length > 90 ? d.Substring(0, 87) + "..." : d;
    }

    static void Control(Ui.Page page, ConfigFile cfg, ConfigEntryBase e)
    {
        string key = e.Definition.Key, help = Help(e);
        var t = e.SettingType;
        var acceptable = e.Description?.AcceptableValues;

        void Set(object v) { e.BoxedValue = v; }

        if (t == typeof(bool))
        {
            page.Toggle(key, (bool)e.BoxedValue, v => Set(v), help);
        }
        else if (t.IsEnum)
        {
            var names = Enum.GetNames(t);
            int i = Array.IndexOf(names, e.BoxedValue.ToString());
            page.Choice(key, names, Math.Max(0, i), n => Set(Enum.Parse(t, names[n])), help);
        }
        else if (t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long))
        {
            double v = Convert.ToDouble(e.BoxedValue);
            double min = double.MinValue, max = double.MaxValue;
            if (acceptable != null && acceptable.GetType().IsGenericType &&
                acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueRange<>))
            {
                min = Convert.ToDouble(acceptable.GetType().GetProperty("MinValue").GetValue(acceptable));
                max = Convert.ToDouble(acceptable.GetType().GetProperty("MaxValue").GetValue(acceptable));
            }
            bool integral = t == typeof(int) || t == typeof(long);
            double step = integral ? 1 : StepFor(v, min, max);
            if (integral && min > double.MinValue && max < double.MaxValue) step = Math.Max(1, Math.Round((max - min) / 20));
            page.Stepper(key, (float)v, (float)step, (float)Math.Max(min, -1e9), (float)Math.Min(max, 1e9),
                         n => Set(Convert.ChangeType(integral ? Math.Round(n) : n, t)), integral ? "0" : "0.##", help);
        }
        else if (t == typeof(string) && acceptable is AcceptableValueList<string> list)
        {
            var opts = list.AcceptableValues;
            int i = Array.IndexOf(opts, (string)e.BoxedValue);
            page.Choice(key, opts, Math.Max(0, i), n => Set(opts[n]), help);
        }
        else if (t == typeof(string))
        {
            page.TextField(key, (string)e.BoxedValue, 120, s => Set(s), IsSecret(e), help);
        }
        else
        {
            page.Info(key, e.GetSerializedValue());
        }
    }

    // About 20 presses across a range, or a tenth of the value's size without one.
    static double StepFor(double v, double min, double max)
    {
        if (min > double.MinValue && max < double.MaxValue) return Nice((max - min) / 20);
        double m = Math.Abs(v);
        return m >= 10 ? 1 : m >= 1 ? 0.1 : 0.05;
    }

    static double Nice(double s)
    {
        if (s <= 0) return 0.1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(s)));
        double f = s / p;
        return (f < 1.5 ? 1 : f < 3.5 ? 2.5 : f < 7.5 ? 5 : 10) * p;
    }
}
