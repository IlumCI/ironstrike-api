using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace IronstrikeApi;

/// <summary>
/// Logging helpers. <c>BepInEx/LogOutput.log</c> is the file players attach to bug reports, so it
/// must never contain a Steam name, an account id or a join code. Route anything that could hold
/// one through <see cref="Redact"/>.
/// </summary>
public static class ApiLog
{
    static readonly HashSet<string> once = new();

    /// <summary>
    /// A loggable stand-in for a private string: its length and a short hash, enough to tell two
    /// values apart in a log without revealing either.
    /// </summary>
    /// <param name="s">Any private value (name, code, id).</param>
    public static string Redact(string s)
    {
        if (string.IsNullOrEmpty(s)) return "<empty>";
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return $"<len{s.Length}:{h & 0xFFFFFF:x6}>";
    }

    /// <summary>Logs a warning the first time a given key is seen, and never again this session.</summary>
    /// <param name="log">The mod's logger.</param>
    /// <param name="key">What makes this warning distinct.</param>
    /// <param name="message">The text.</param>
    public static void WarnOnce(ManualLogSource log, string key, string message)
    {
        lock (once) if (!once.Add(key)) return;
        if (log != null) log.LogWarning(message); else Diag.Warn(message);
    }

    internal static void Once(string key, string message)
    {
        lock (once) if (!once.Add(key)) return;
        Diag.Info(message);
    }
}

// The API's own log lines. Before the plugin has loaded (and in the unit tests) there is no
// BepInEx log yet; nothing that reports a problem may itself throw for that reason.
internal static class Diag
{
    internal static Action<string> Sink;          // tests capture output here

    internal static void Info(string s) { if (Sink != null) Sink("I " + s); else if (Plugin.Log != null) Plugin.Log.LogInfo(s); }
    internal static void Warn(string s) { if (Sink != null) Sink("W " + s); else if (Plugin.Log != null) Plugin.Log.LogWarning(s); }
    internal static void Error(string s) { if (Sink != null) Sink("E " + s); else if (Plugin.Log != null) Plugin.Log.LogError(s); else Console.Error.WriteLine(s); }
}

// Event dispatch that one broken mod cannot take down: every subscriber runs in its own try/catch,
// and a failure names the mod that owns the handler. A handler that keeps throwing is muted after a
// few failures, so a per-frame event cannot flood the log.
internal static class Safe
{
    const int MuteAfter = 5;
    // Across all handlers: many broken handlers at once (a mod subscribing in a loop) must not flood
    // the log either. Past this, failures are only counted, with a summary now and then.
    internal const int MaxReports = 100;
    static readonly Dictionary<Delegate, int> failures = new();
    static int reports, suppressed;

    internal static void Run(Delegate d, string what, params object[] args)
    {
        if (d == null) return;
        foreach (var h in d.GetInvocationList())
        {
            if (failures.TryGetValue(h, out int n) && n >= MuteAfter) continue;
            try { h.DynamicInvoke(args); }
            catch (Exception e) { Blame(h, what, e is System.Reflection.TargetInvocationException t ? t.InnerException : e); }
        }
    }

    // Typed fast paths for the per-frame and per-hit events; DynamicInvoke is slow.
    internal static void Run(Action d, string what)
    {
        if (d == null) return;
        foreach (Action h in d.GetInvocationList())
        {
            if (failures.TryGetValue(h, out int n) && n >= MuteAfter) continue;
            try { h(); } catch (Exception e) { Blame(h, what, e); }
        }
    }

    internal static void Run<T>(Action<T> d, string what, T a)
    {
        if (d == null) return;
        foreach (Action<T> h in d.GetInvocationList())
        {
            if (failures.TryGetValue(h, out int n) && n >= MuteAfter) continue;
            try { h(a); } catch (Exception e) { Blame(h, what, e); }
        }
    }

    static bool IsPowerOfTen(int n) { while (n >= 10 && n % 10 == 0) n /= 10; return n == 1; }

    internal static void ResetReports() { reports = 0; suppressed = 0; }

    internal static int Failures(Delegate h) => h != null && failures.TryGetValue(h, out int n) ? n : 0;

    internal static void Blame(Delegate h, string what, Exception e)
    {
        int n = 1;
        string who = "?", where = "?";
        try
        {
            if (h != null)
            {
                failures.TryGetValue(h, out n);
                failures[h] = ++n;
                where = $"{h.Method.DeclaringType?.Name}.{h.Method.Name}";
                who = h.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
                who = Mods.Owner(h.Method.DeclaringType)?.Name ?? who;
            }
        }
        catch (Exception) { }     // the mod registry may not exist yet; the assembly name will do
        // Only the first failures are written out in full; a handler failing every frame must not
        // flood the log before it is muted.
        if (n > MuteAfter) return;
        if (reports >= MaxReports)
        {
            if (IsPowerOfTen(++suppressed))
                Diag.Error($"handler errors: {MaxReports} reported in full, {suppressed} more since (only counted)");
            return;
        }
        reports++;
        Diag.Error($"{who}: {what} handler {where} threw: {e}" + (n == MuteAfter ? $"\n(muted after {MuteAfter} failures)" : ""));
    }
}
