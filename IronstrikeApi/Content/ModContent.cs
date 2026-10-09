using System;
using System.Collections.Generic;
using System.Linq;
using IronstrikeApi.Content;

namespace IronstrikeApi;

/// <summary>
/// New skills (and, later, spells and weapons) that become part of the game: offered on the upgrade
/// screen, levelled, synced, shown with their own name, text and icon.
/// </summary>
/// <remarks>
/// <para>Register content in your plugin's <c>Load()</c>, unconditionally. Every player in a session
/// must register exactly the same content: ids are handed out from the sorted list of keys, and
/// players compare a fingerprint of it when their mods greet each other. Content registered later,
/// or only when a setting is on, gets a different fingerprint and switches custom content off for
/// that session.</para>
/// <para>Custom content works in solo games and on modded servers (Ironstrike Servers), where every
/// player has the same mods. In a vanilla Private Match it is off: a player without your mod could
/// not show, sync or even load it.</para>
/// </remarks>
public static class ModContent
{
    static readonly Dictionary<string, CustomSkill> skills = new(StringComparer.Ordinal);
    static readonly Dictionary<int, CustomSkill> skillsById = new();
    static bool frozen;
    static byte[] manifest = Array.Empty<byte>();

    /// <summary>
    /// Registers a skill. <c>configure</c> fills in its name, text, icon, class, category and hooks.
    /// Returns the definition, whose <see cref="CustomSkill.Id"/> is set once the game starts.
    /// </summary>
    /// <param name="key">A unique key with your mod's prefix: lower-case letters, digits, '.', '_', '-'.</param>
    /// <param name="configure">Sets the skill up.</param>
    public static CustomSkill Skill(string key, Action<CustomSkill> configure)
    {
        string problem = ContentIds.Validate(key);
        if (problem != null) throw new ArgumentException($"skill key '{key}': {problem}", nameof(key));
        if (frozen) throw new InvalidOperationException($"skill '{key}' registered after the game started; register in your plugin's Load()");
        if (skills.ContainsKey(key)) throw new ArgumentException($"skill '{key}' is already registered", nameof(key));
        var s = new CustomSkill(key);
        configure?.Invoke(s);
        problem = s.Problem();
        if (problem != null) throw new ArgumentException($"skill '{key}': {problem}");
        skills[key] = s;
        return s;
    }

    /// <summary>Every registered skill.</summary>
    public static IReadOnlyCollection<CustomSkill> Skills => skills.Values;

    /// <summary>The skill registered under a key, or null.</summary>
    /// <param name="key">Its key.</param>
    public static CustomSkill GetSkill(string key) => key != null && skills.TryGetValue(key, out var s) ? s : null;

    /// <summary>
    /// True while custom content is active: solo, or a modded server where every player has the same
    /// content. False in Private Matches and public games, and after a content mismatch.
    /// </summary>
    public static bool Active =>
        !Mismatch && Safety.Context is PlayContext.Offline or PlayContext.Solo or PlayContext.ModdedServer;

    /// <summary>True if a player in this session has different content; content is off until it ends.</summary>
    public static bool Mismatch { get; internal set; }

    /// <summary>Gives a fighter a custom skill at a level (the host's call in multiplayer).</summary>
    /// <param name="fighter">Who gets it; null means the local player.</param>
    /// <param name="key">The skill's key.</param>
    /// <param name="level">1 to the skill's MaxLevel.</param>
    public static bool GiveSkill(Fighter fighter, string key, int level = 1)
    {
        var s = GetSkill(key) ?? throw new ArgumentException($"no skill '{key}'", nameof(key));
        if (!Active) { ApiLog.WarnOnce(null, "content:inactive:give", "custom content is off in this game; skill not given"); return false; }
        Freeze();
        fighter ??= Gameplay.Players.LocalFighter;
        if (fighter is null || SkillManager.instance == null) return false;
        SkillManager.instance.GiveSkillToFighter(s.Type, fighter, Math.Clamp(level, 1, s.MaxLevel));
        return true;
    }

    /// <summary>Takes a custom skill away.</summary>
    /// <param name="fighter">From whom; null means the local player.</param>
    /// <param name="key">The skill's key.</param>
    public static bool RemoveSkill(Fighter fighter, string key)
    {
        var s = GetSkill(key) ?? throw new ArgumentException($"no skill '{key}'", nameof(key));
        fighter ??= Gameplay.Players.LocalFighter;
        if (fighter is null || SkillManager.instance == null) return false;
        SkillManager.instance.RemoveSkillFromFighter(s.Type, fighter);
        return true;
    }

    /// <summary>The level a fighter has a custom skill at, or 0.</summary>
    /// <param name="fighter">The fighter; null means the local player.</param>
    /// <param name="key">The skill's key.</param>
    public static int SkillLevel(Fighter fighter, string key)
    {
        var s = GetSkill(key);
        fighter ??= Gameplay.Players.LocalFighter;
        if (s == null || fighter is null || s.Id == 0 || fighter.skills == null) return 0;
        return fighter.skills.TryGetValue(s.Type, out var k) && k != null ? k.level : 0;
    }

    // ------------------------------------------------------------------ internals

    internal static CustomSkill ById(int id) => skillsById.TryGetValue(id, out var s) ? s : null;

    internal static byte[] Manifest => manifest;

    internal static bool Frozen => frozen;

    // Ids are handed out once, when the game first needs them, from the full list of registrations.
    // existing: ids already in the game's skill dictionary (from the GetSkillDict postfix, which must
    // not call GetSkillDict again).
    internal static void Freeze(IEnumerable<int> existing = null)
    {
        if (frozen) return;
        frozen = true;
        var taken = new HashSet<int>(Enum.GetValues(typeof(SkillType)).Cast<object>().Select(v => Convert.ToInt32(v)));
        if (existing != null) taken.UnionWith(existing);
        var ids = ContentIds.Allocate(skills.Keys, taken);
        foreach (var kv in ids)
        {
            skills[kv.Key].Id = kv.Value;
            skillsById[kv.Value] = skills[kv.Key];
        }
        manifest = skills.Count == 0 ? Array.Empty<byte>() : ContentIds.Manifest(skills.Values.Select(s => ("skill", s.Key, s.Id)));
        if (skills.Count > 0)
            Diag.Info($"content: {skills.Count} custom skill(s) registered, ids {string.Join(", ", skills.Values.OrderBy(s => s.Id).Select(s => $"{s.Key}={s.Id}"))}");
    }

    // For the unit tests.
    internal static void ResetForTests()
    {
        skills.Clear(); skillsById.Clear(); frozen = false; manifest = Array.Empty<byte>(); Mismatch = false;
    }
}
