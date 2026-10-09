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
    static readonly Dictionary<string, CustomSpell> spells = new(StringComparer.Ordinal);
    static readonly Dictionary<int, CustomSpell> spellsById = new();
    static readonly Dictionary<string, CustomSchool> schools = new(StringComparer.Ordinal);
    static readonly Dictionary<int, CustomSchool> schoolsById = new();
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
        if (skills.ContainsKey(key) || schools.ContainsKey(key)) throw new ArgumentException($"skill '{key}' is already registered", nameof(key));
        var s = new CustomSkill(key);
        configure?.Invoke(s);
        problem = s.Problem();
        if (problem != null) throw new ArgumentException($"skill '{key}': {problem}");
        skills[key] = s;
        return s;
    }

    /// <summary>
    /// Registers a spell. It reaches players through a <see cref="School"/> that lists it.
    /// </summary>
    /// <param name="key">A unique key with your mod's prefix.</param>
    /// <param name="configure">Sets the spell up: name, icon, aiming, costs, and what it does.</param>
    public static CustomSpell Spell(string key, Action<CustomSpell> configure)
    {
        string problem = ContentIds.Validate(key);
        if (problem != null) throw new ArgumentException($"spell key '{key}': {problem}", nameof(key));
        if (frozen) throw new InvalidOperationException($"spell '{key}' registered after the game started; register in your plugin's Load()");
        if (spells.ContainsKey(key)) throw new ArgumentException($"spell '{key}' is already registered", nameof(key));
        var s = new CustomSpell(key);
        configure?.Invoke(s);
        problem = s.Problem();
        if (problem != null) throw new ArgumentException($"spell '{key}': {problem}");
        spells[key] = s;
        return s;
    }

    /// <summary>
    /// Registers a magic school: an upgrade for casters that teaches two spells registered with
    /// <see cref="Spell"/> (register those first).
    /// </summary>
    /// <param name="key">A unique key with your mod's prefix.</param>
    /// <param name="configure">Sets the school up: name, icon, rune shapes and its two spells.</param>
    public static CustomSchool School(string key, Action<CustomSchool> configure)
    {
        string problem = ContentIds.Validate(key);
        if (problem != null) throw new ArgumentException($"school key '{key}': {problem}", nameof(key));
        if (frozen) throw new InvalidOperationException($"school '{key}' registered after the game started; register in your plugin's Load()");
        if (schools.ContainsKey(key) || skills.ContainsKey(key)) throw new ArgumentException($"school '{key}' is already registered", nameof(key));
        var s = new CustomSchool(key);
        configure?.Invoke(s);
        problem = s.Problem();
        if (problem != null) throw new ArgumentException($"school '{key}': {problem}");
        foreach (var k in s.Spells)
        {
            var sp = GetSpell(k) ?? throw new ArgumentException($"school '{key}': no spell '{k}' (register spells before their school)");
            if (sp.School != null) throw new ArgumentException($"school '{key}': spell '{k}' is already taught by '{sp.School.Key}'");
        }
        if (s.Spells[0] == s.Spells[1]) throw new ArgumentException($"school '{key}': its two spells must differ");
        foreach (var k in s.Spells) spells[k].School = s;
        schools[key] = s;
        return s;
    }

    /// <summary>Every registered spell.</summary>
    public static IReadOnlyCollection<CustomSpell> Spells => spells.Values;

    /// <summary>Every registered school.</summary>
    public static IReadOnlyCollection<CustomSchool> Schools => schools.Values;

    /// <summary>The spell registered under a key, or null.</summary>
    /// <param name="key">Its key.</param>
    public static CustomSpell GetSpell(string key) => key != null && spells.TryGetValue(key, out var s) ? s : null;

    /// <summary>The school registered under a key, or null.</summary>
    /// <param name="key">Its key.</param>
    public static CustomSchool GetSchool(string key) => key != null && schools.TryGetValue(key, out var s) ? s : null;

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

    /// <summary>Gives a fighter a custom magic school (and so its two spells) at a level.</summary>
    /// <param name="fighter">Who gets it; null means the local player.</param>
    /// <param name="key">The school's key.</param>
    /// <param name="level">The stage, 1 to the school's MaxLevel.</param>
    /// <param name="enhanced">Give it enhanced, as an enhanced upgrade card does.</param>
    public static bool GiveSchool(Fighter fighter, string key, int level = 1, bool enhanced = false)
    {
        var s = GetSchool(key) ?? throw new ArgumentException($"no school '{key}'", nameof(key));
        if (!Active) { ApiLog.WarnOnce(null, "content:inactive:give", "custom content is off in this game; school not given"); return false; }
        Freeze();
        fighter ??= Gameplay.Players.LocalFighter;
        if (fighter is null || SkillManager.instance == null) return false;
        SkillManager.instance.GiveSkillToFighter(s.Type, fighter, Math.Clamp(level, 1, s.MaxLevel) + (enhanced ? 5 : 0));
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

    /// <summary>The level a fighter has a custom skill or school at, or 0.</summary>
    /// <param name="fighter">The fighter; null means the local player.</param>
    /// <param name="key">The skill's key.</param>
    public static int SkillLevel(Fighter fighter, string key)
    {
        int id = GetSkill(key)?.Id ?? GetSchool(key)?.Id ?? 0;
        fighter ??= Gameplay.Players.LocalFighter;
        if (id == 0 || fighter is null || fighter.skills == null) return 0;
        return fighter.skills.TryGetValue((SkillType)id, out var k) && k != null ? k.level : 0;
    }

    // ------------------------------------------------------------------ internals

    internal static CustomSkill ById(int id) => skillsById.TryGetValue(id, out var s) ? s : null;
    internal static CustomSpell SpellById(int id) => spellsById.TryGetValue(id, out var s) ? s : null;
    internal static CustomSchool SchoolById(int id) => schoolsById.TryGetValue(id, out var s) ? s : null;

    // The name a skill id shows on cards: a custom skill's or school's; null for the game's own.
    internal static string SkillName(int id) => ById(id)?.Name ?? SchoolById(id)?.Name;

    internal static byte[] Manifest => manifest;

    internal static bool Frozen => frozen;

    // Ids are handed out once, when the game first needs them, from the full list of registrations.
    // existing: ids already in the game's skill dictionary (from the GetSkillDict postfix, which must
    // not call GetSkillDict again).
    internal static void Freeze(IEnumerable<int> existing = null)
    {
        if (frozen) return;
        frozen = true;
        var takenSkills = new HashSet<int>(Enum.GetValues(typeof(SkillType)).Cast<object>().Select(v => Convert.ToInt32(v)));
        if (existing != null) takenSkills.UnionWith(existing);
        // Skills and schools share the game's skill ids; spells have their own.
        var skillIds = ContentIds.Allocate(skills.Keys.Concat(schools.Keys), takenSkills);
        foreach (var kv in skillIds)
        {
            if (skills.TryGetValue(kv.Key, out var sk)) { sk.Id = kv.Value; skillsById[kv.Value] = sk; }
            else { schools[kv.Key].Id = kv.Value; schoolsById[kv.Value] = schools[kv.Key]; }
        }
        var takenSpells = new HashSet<int>(Enum.GetValues(typeof(SpellType)).Cast<object>().Select(v => Convert.ToInt32(v)));
        foreach (var kv in ContentIds.Allocate(spells.Keys, takenSpells))
        {
            spells[kv.Key].Id = kv.Value;
            spellsById[kv.Value] = spells[kv.Key];
        }
        var entries = skills.Values.Select(s => ("skill", s.Key, s.Id))
            .Concat(schools.Values.Select(s => ("school", s.Key, s.Id)))
            .Concat(spells.Values.Select(s => ("spell", s.Key, s.Id))).ToList();
        manifest = entries.Count == 0 ? Array.Empty<byte>() : ContentIds.Manifest(entries);
        if (entries.Count > 0)
            Diag.Info($"content: {skills.Count} skill(s), {schools.Count} school(s), {spells.Count} spell(s); ids " +
                      string.Join(", ", entries.OrderBy(e => e.Item1).ThenBy(e => e.Item3).Select(e => $"{e.Item2}={e.Item3}")));
    }

    internal static bool Any => skills.Count + schools.Count + spells.Count > 0;

    // For the unit tests.
    internal static void ResetForTests()
    {
        skills.Clear(); skillsById.Clear(); spells.Clear(); spellsById.Clear(); schools.Clear(); schoolsById.Clear(); frozen = false; manifest = Array.Empty<byte>(); Mismatch = false;
    }
}
