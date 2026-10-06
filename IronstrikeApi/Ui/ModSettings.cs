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

    // ------------------------------------------------------------------ choosing a control

    internal enum ControlKind { Toggle, Stepper, Choice, Text, Info }

    // What control an entry gets, worked out without drawing anything (so it can be tested).
    internal sealed class ControlSpec
    {
        public ControlKind Kind;
        public string Key, Help, Shown;
        public bool Secret, Integral;
        public double Value, Min, Max, Step;
        public string[] Options;          // Choice: labels
        public object[] Values;           // Choice: the values behind them
        public int Index;                 // Choice: the current one
        public int MaxLength;             // Text
    }

    static readonly Type[] IntegralTypes =
        { typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong) };
    static readonly Type[] RealTypes = { typeof(float), typeof(double), typeof(decimal) };

    // Steppers work in float; past this, whole numbers would be changed by the rounding.
    const double StepperLimit = 1e6;

    internal static ControlSpec Classify(ConfigEntryBase e)
    {
        var t = e.SettingType;
        var spec = new ControlSpec { Key = e.Definition.Key, Help = Help(e), Kind = ControlKind.Info, Shown = Serialized(e) };
        object value = e.BoxedValue;
        var acceptable = e.Description?.AcceptableValues;

        if (t == typeof(bool))
        {
            spec.Kind = ControlKind.Toggle;
            spec.Value = value is true ? 1 : 0;
            return spec;
        }

        // Any AcceptableValueList: a picker over exactly the allowed values.
        var listValues = ListValues(acceptable);
        if (listValues != null && listValues.Length > 0)
            return Choice(spec, listValues, value);

        if (t.IsEnum)
        {
            // A [Flags] combination has no single name; cycling names would silently drop bits.
            if (t.IsDefined(typeof(FlagsAttribute), false)) return spec;
            return Choice(spec, Enum.GetValues(t).Cast<object>().ToArray(), value);
        }

        bool integral = Array.IndexOf(IntegralTypes, t) >= 0, real = Array.IndexOf(RealTypes, t) >= 0;
        if (integral || real)
        {
            double v;
            try { v = Convert.ToDouble(value); } catch (Exception) { return spec; }
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            var (min, max) = Range(acceptable, t);
            if (min > max) (min, max) = (max, min);                 // a mod's range written backwards
            spec.Integral = integral;
            if (Math.Abs(v) > StepperLimit || (max - min) > 2 * StepperLimit && !(min == TypeMin(t) && max == TypeMax(t) && Math.Abs(v) <= StepperLimit))
            {
                // Too big to step through: type it instead.
                spec.Kind = ControlKind.Text;
                spec.MaxLength = 24;
                spec.Min = min; spec.Max = max;
                return spec;
            }
            spec.Kind = ControlKind.Stepper;
            spec.Value = v;
            spec.Min = Math.Max(min, -StepperLimit);
            spec.Max = Math.Min(max, StepperLimit);
            bool ranged = min > TypeMin(t) || max < TypeMax(t);
            spec.Step = integral ? (ranged ? Math.Max(1, Math.Round((spec.Max - spec.Min) / 20)) : 1)
                                 : ranged ? Nice((spec.Max - spec.Min) / 20) : StepFor(v, double.MinValue, double.MaxValue);
            return spec;
        }

        if (t == typeof(string))
        {
            spec.Kind = ControlKind.Text;
            spec.Secret = IsSecret(e);
            spec.MaxLength = Math.Max(120, ((string)value)?.Length ?? 0);
            return spec;
        }
        return spec;      // KeyboardShortcut, colours, anything else: shown, not edited
    }

    static ControlSpec Choice(ControlSpec spec, object[] values, object current)
    {
        int i = Array.FindIndex(values, v => Equals(v, current));
        if (i < 0)
        {
            // The current value is not one of the choices (an undefined enum value, a list the mod
            // later changed): keep it as an option rather than replacing it the moment it is shown.
            values = values.Append(current).ToArray();
            i = values.Length - 1;
        }
        spec.Kind = ControlKind.Choice;
        spec.Values = values;
        spec.Options = values.Select(v => v?.ToString() ?? "").ToArray();
        spec.Index = i;
        return spec;
    }

    static object[] ListValues(AcceptableValueBase acceptable)
    {
        if (acceptable == null) return null;
        var at = acceptable.GetType();
        if (!at.IsGenericType || at.GetGenericTypeDefinition() != typeof(AcceptableValueList<>)) return null;
        var arr = at.GetProperty("AcceptableValues")?.GetValue(acceptable) as Array;
        return arr?.Cast<object>().ToArray();
    }

    static (double Min, double Max) Range(AcceptableValueBase acceptable, Type t)
    {
        double min = TypeMin(t), max = TypeMax(t);
        if (acceptable == null) return (min, max);
        var at = acceptable.GetType();
        if (!at.IsGenericType || at.GetGenericTypeDefinition() != typeof(AcceptableValueRange<>)) return (min, max);
        try
        {
            min = Convert.ToDouble(at.GetProperty("MinValue").GetValue(acceptable));
            max = Convert.ToDouble(at.GetProperty("MaxValue").GetValue(acceptable));
        }
        catch (Exception) { }
        return (min, max);
    }

    static double TypeMin(Type t) => t == typeof(byte) || t == typeof(ushort) || t == typeof(uint) || t == typeof(ulong) ? 0
        : t == typeof(sbyte) ? sbyte.MinValue : t == typeof(short) ? short.MinValue : t == typeof(int) ? int.MinValue
        : t == typeof(long) ? long.MinValue : double.MinValue;

    static double TypeMax(Type t) => t == typeof(byte) ? byte.MaxValue : t == typeof(sbyte) ? sbyte.MaxValue
        : t == typeof(short) ? short.MaxValue : t == typeof(ushort) ? ushort.MaxValue : t == typeof(int) ? int.MaxValue
        : t == typeof(uint) ? uint.MaxValue : t == typeof(long) ? long.MaxValue : t == typeof(ulong) ? ulong.MaxValue
        : double.MaxValue;

    static (decimal Lo, decimal Hi) DecimalBounds(Type t) =>
        t == typeof(byte) ? (byte.MinValue, byte.MaxValue) : t == typeof(sbyte) ? (sbyte.MinValue, sbyte.MaxValue)
        : t == typeof(short) ? (short.MinValue, short.MaxValue) : t == typeof(ushort) ? (ushort.MinValue, ushort.MaxValue)
        : t == typeof(int) ? (int.MinValue, int.MaxValue) : t == typeof(uint) ? (uint.MinValue, uint.MaxValue)
        : t == typeof(long) ? (long.MinValue, long.MaxValue) : (ulong.MinValue, ulong.MaxValue);

    static decimal ToDecimal(double d, decimal lo, decimal hi)
    {
        if (d <= (double)lo) return lo;
        if (d >= (double)hi) return hi;
        return (decimal)d;
    }

    static string Serialized(ConfigEntryBase e)
    {
        try { return e.GetSerializedValue(); } catch (Exception) { return "?"; }
    }

    // Applies a new value from a control, converted to the entry's type and kept in range. Returns
    // false (and changes nothing) for input that does not fit: typed text that is not a number, or a
    // number the type cannot hold.
    internal static bool TrySet(ConfigEntryBase e, ControlSpec spec, object input)
    {
        try
        {
            var t = e.SettingType;
            object v;
            switch (spec.Kind)
            {
                case ControlKind.Toggle: v = input is true; break;
                case ControlKind.Choice: v = spec.Values[(int)input]; break;
                case ControlKind.Stepper:
                case ControlKind.Text when t != typeof(string):
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (spec.Integral)
                    {
                        // decimal, not double: a long typed in full must arrive exactly.
                        decimal m;
                        if (input is string str)
                        {
                            if (!decimal.TryParse(str.Trim(), System.Globalization.NumberStyles.Float, inv, out m)) return false;
                        }
                        else
                        {
                            double dd = Convert.ToDouble(input);
                            if (double.IsNaN(dd) || double.IsInfinity(dd)) return false;
                            m = (decimal)dd;
                        }
                        m = Math.Round(m);
                        // Bounds in decimal, exactly: long.MaxValue as a double rounds up past the type.
                        var (tlo, thi) = DecimalBounds(t);
                        decimal lo = Math.Max(ToDecimal(Math.Min(spec.Min, spec.Max), tlo, thi), tlo);
                        decimal hi = Math.Min(ToDecimal(Math.Max(spec.Min, spec.Max), tlo, thi), thi);
                        if (m < lo) m = lo;
                        if (m > hi) m = hi;
                        v = Convert.ChangeType(m, t, inv);
                    }
                    else
                    {
                        double d;
                        if (input is string str2) { if (!double.TryParse(str2.Trim(), System.Globalization.NumberStyles.Float, inv, out d)) return false; }
                        else d = Convert.ToDouble(input);
                        if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                        d = Math.Clamp(d, Math.Min(spec.Min, spec.Max), Math.Max(spec.Min, spec.Max));
                        v = Convert.ChangeType(d, t, inv);
                    }
                    break;
                case ControlKind.Text: v = input as string ?? ""; break;
                default: return false;
            }
            e.BoxedValue = v;
            return true;
        }
        catch (Exception) { return false; }
    }

    // Config text is shown as plain text: a description saying "x < 5" must not be read as a tag.
    internal static string Plain(string s) => string.IsNullOrEmpty(s) ? s : "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";

    static void Control(Ui.Page page, ConfigFile cfg, ConfigEntryBase e)
    {
        var spec = Classify(e);
        string key = Plain(spec.Key), help = spec.Help != null ? Plain(spec.Help) : null;
        void Refused(string what) => ApiLog.WarnOnce(null, "cfg:set:" + e.Definition, $"settings: {e.Definition}: '{what}' does not fit, unchanged");

        switch (spec.Kind)
        {
            case ControlKind.Toggle:
                page.Toggle(key, spec.Value != 0, v => TrySet(e, spec, v), help);
                break;
            case ControlKind.Choice:
                page.Choice(key, spec.Options.Select(Plain).ToArray(), spec.Index, i => TrySet(e, spec, i), help);
                break;
            case ControlKind.Stepper:
                page.Stepper(key, (float)spec.Value, (float)spec.Step, (float)spec.Min, (float)spec.Max,
                             n => TrySet(e, spec, (double)n), spec.Integral ? "0" : "0.##", help);
                break;
            case ControlKind.Text:
                string current = e.SettingType == typeof(string) ? (string)e.BoxedValue : spec.Shown;
                page.TextField(key, current == null || spec.Secret ? current : Plain(current), spec.MaxLength,
                               s => { if (!TrySet(e, spec, s)) Refused(s); }, spec.Secret, help);
                break;
            default:
                page.Info(key, Plain(spec.Shown));
                break;
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
