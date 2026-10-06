using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace IronstrikeApi;

/// <summary>What a mod changes, as the mod declares it with <see cref="ModKindAttribute"/>.</summary>
public enum ModKind
{
    /// <summary>The mod did not say.</summary>
    Unknown = 0,
    /// <summary>Only affects your own screen: UI, visuals, sounds, quality of life.</summary>
    Client = 1,
    /// <summary>Changes how the game plays (rules, stats, enemies). Allowed only in solo and private games.</summary>
    Gameplay = 2,
    /// <summary>A cheat. Allowed only in solo and private games; listed as such to other players.</summary>
    Cheat = 3,
    /// <summary>A library other mods build on, like this API.</summary>
    Library = 4,
}

/// <summary>
/// Declares what a mod changes. Put it on the plugin class:
/// <c>[ModKind(ModKind.Gameplay)]</c>. Shown in the Mods window, and to other players in a
/// modded server's listing.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ModKindAttribute : Attribute
{
    /// <summary>The declared kind.</summary>
    public ModKind Kind { get; }

    /// <summary>Declares the kind of mod.</summary>
    /// <param name="kind">What the mod changes.</param>
    public ModKindAttribute(ModKind kind) => Kind = kind;
}

/// <summary>
/// Declares the oldest API version a mod works with: <c>[RequiresApi("0.1.0")]</c>. The API logs a
/// clear error for mods that need a newer API than the one installed, instead of letting them fail
/// with a missing-method exception.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresApiAttribute : Attribute
{
    /// <summary>The minimum API version.</summary>
    public Version Minimum { get; }

    /// <summary>Declares the minimum API version.</summary>
    /// <param name="minimum">A version string such as <c>"0.1.0"</c>.</param>
    public RequiresApiAttribute(string minimum) => Minimum = Version.Parse(minimum);
}

/// <summary>One loaded BepInEx plugin, as the API sees it.</summary>
public sealed class ModInfo
{
    /// <summary>The plugin GUID, e.g. <c>eu.euroswarms.ironstrike.trainer</c>.</summary>
    public string Guid { get; internal set; }
    /// <summary>Display name from <c>[BepInPlugin]</c>.</summary>
    public string Name { get; internal set; }
    /// <summary>Version from <c>[BepInPlugin]</c>, e.g. <c>1.0.3</c>.</summary>
    public string Version { get; internal set; }
    /// <summary>Declared kind; <see cref="ModKind.Unknown"/> if the mod has no <see cref="ModKindAttribute"/>.</summary>
    public ModKind Kind { get; internal set; }
    /// <summary>GUIDs from the plugin's <c>[BepInDependency]</c> attributes.</summary>
    public IReadOnlyList<string> Dependencies { get; internal set; } = Array.Empty<string>();
    /// <summary>True if the mod declares a dependency on this API.</summary>
    public bool UsesApi => Dependencies.Contains(ModApi.Guid);
    /// <summary>The plugin's own config file, or null before the plugin has loaded.</summary>
    public ConfigFile Config { get; internal set; }
    /// <summary>The plugin instance, or null before it has loaded.</summary>
    public BepInEx.Unity.IL2CPP.BasePlugin Instance { get; internal set; }
    /// <summary>The assembly the plugin lives in, or null before it has loaded.</summary>
    public Assembly Assembly { get; internal set; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} {Version}";
}

/// <summary>The registry of every loaded mod, and the mod-set fingerprint multiplayer uses.</summary>
public static class Mods
{
    static List<ModInfo> all;
    static string hash;

    /// <summary>Every loaded BepInEx plugin, this API included, sorted by name.</summary>
    public static IReadOnlyList<ModInfo> All
    {
        get
        {
            // Rebuilt until every plugin has an instance: BepInEx loads them one by one, and a mod
            // that asks during its own Load() would otherwise freeze a half-filled list.
            if (all == null || all.Any(m => m.Instance == null)) all = Scan();
            return all;
        }
    }

    /// <summary>The mod with this GUID, or null.</summary>
    /// <param name="guid">A plugin GUID.</param>
    public static ModInfo Get(string guid) => All.FirstOrDefault(m => m.Guid == guid);

    /// <summary>True if a plugin with this GUID is loaded.</summary>
    /// <param name="guid">A plugin GUID.</param>
    public static bool IsLoaded(string guid) => IL2CPPChainloader.Instance.Plugins.ContainsKey(guid);

    /// <summary>The mod that owns a type, found by assembly; null for the game's own types.</summary>
    /// <param name="t">Any type.</param>
    public static ModInfo Owner(Type t)
    {
        if (t == null) return null;
        var asm = t.Assembly;
        return All.FirstOrDefault(m => m.Assembly == asm);
    }

    /// <summary>
    /// A 16-hex-digit fingerprint of the installed mod set: SHA-256 over the sorted
    /// <c>GUID@version</c> of every plugin. Two players with the same value run the same mods.
    /// Ironstrike Servers computes the same value for its join check.
    /// </summary>
    public static string Hash => hash ??= Fingerprint(All.Select(m => $"{m.Guid}@{m.Version}"));

    internal static string Fingerprint(IEnumerable<string> items)
    {
        var l = items.ToList();
        l.Sort(StringComparer.Ordinal);
        using var sha = SHA256.Create();
        var d = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", l)));
        var sb = new StringBuilder();
        for (int i = 0; i < 8; i++) sb.Append(d[i].ToString("x2"));
        return sb.ToString();
    }

    static List<ModInfo> Scan()
    {
        var list = new List<ModInfo>();
        foreach (var kv in IL2CPPChainloader.Instance.Plugins)
        {
            var pi = kv.Value;
            var md = pi.Metadata;
            var inst = pi.Instance as BepInEx.Unity.IL2CPP.BasePlugin;
            var type = inst?.GetType();
            var deps = pi.Dependencies?.Select(d => d.DependencyGUID).ToArray() ?? Array.Empty<string>();
            var kind = type?.GetCustomAttribute<ModKindAttribute>()?.Kind
                       ?? (md.GUID == ModApi.Guid ? ModKind.Library : KnownKind(md.GUID));
            list.Add(new ModInfo
            {
                Guid = md.GUID, Name = md.Name, Version = md.Version.ToString(), Kind = kind, Dependencies = deps,
                Config = inst?.Config, Instance = inst, Assembly = type?.Assembly,
            });
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    // Mods that predate the attribute and are known to this project.
    static ModKind KnownKind(string guid) => guid switch
    {
        "eu.euroswarms.ironstrike.trainer" => ModKind.Cheat,
        "eu.euroswarms.ironstrike.servers" => ModKind.Client,
        _ => ModKind.Unknown,
    };

    // Run once the main loop starts, when every plugin has loaded.
    internal static void CheckRequirements()
    {
        var have = Version.Parse(ModApi.Version);
        foreach (var m in All)
        {
            var need = m.Instance?.GetType().GetCustomAttribute<RequiresApiAttribute>()?.Minimum;
            if (need == null) continue;
            if (need.Major != have.Major || need > have)
                Plugin.Log.LogError($"{m.Name} needs {ModApi.Name} {need} or newer within {need.Major}.x; " +
                                    $"{have} is installed. It may misbehave until the API is updated.");
        }
        int api = All.Count(m => m.UsesApi);
        Plugin.Log.LogInfo($"mods: {All.Count} loaded, {api} built on the API; mod set {Hash}");
    }
}
