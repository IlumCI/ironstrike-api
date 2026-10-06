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
        (log ?? Plugin.Log).LogWarning(message);
    }

    internal static void Once(string key, string message)
    {
        lock (once) if (!once.Add(key)) return;
        Plugin.Log.LogInfo(message);
    }
}

// Event dispatch that one broken mod cannot take down: every subscriber runs in its own try/catch,
// and a failure names the mod that owns the handler. A handler that keeps throwing is muted after a
// few failures, so a per-frame event cannot flood the log.
internal static class Safe
{
    const int MuteAfter = 5;
    static readonly Dictionary<Delegate, int> failures = new();

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

    internal static void Blame(Delegate h, string what, Exception e)
    {
        failures.TryGetValue(h, out int n);
        failures[h] = ++n;
        var owner = Mods.Owner(h.Method.DeclaringType);
        string who = owner != null ? owner.Name : h.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
        Plugin.Log.LogError($"{who}: {what} handler {h.Method.DeclaringType?.Name}.{h.Method.Name} threw: {e}" +
                            (n >= MuteAfter ? $"\n(muted after {MuteAfter} failures)" : ""));
    }
}
