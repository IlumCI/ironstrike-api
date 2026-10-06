using System;
using System.Collections.Generic;
using HarmonyLib;

namespace IronstrikeApi.Gameplay;

/// <summary>Changes a stat value. Return the new value; return <paramref name="value"/> to leave it alone.</summary>
/// <param name="fighter">Whose stat is being read (a player or a bot).</param>
/// <param name="stat">Which stat.</param>
/// <param name="value">The value so far: the game's own, after any earlier modifiers.</param>
public delegate float StatModifier(Fighter fighter, SkillCalcType stat, float value);

/// <summary>
/// Stat modifiers. Every skill- and status-modifiable stat in the game (move speed, jumps, damage,
/// dash, mana, weakspot range...) is read through one function,
/// <c>Fighter.CalcSkillAndStatusEffectValue</c>; a modifier sits on its result.
/// </summary>
/// <remarks>
/// <para>Modifiers run in ascending <c>order</c>, then in the order they were added, each getting
/// the previous one's result. They apply only while <see cref="Safety.GameplayAllowed"/>.</para>
/// <para>Whether the game treats a stat as a multiplier or as a bonus percentage is not visible in
/// the binary. <see cref="Scale"/> works either way and is the safe default for "more of this".</para>
/// <para>Not every <see cref="SkillCalcType"/> is read in every situation: <c>VisibilityPercent</c>
/// was never seen being queried, for example.</para>
/// </remarks>
[HarmonyPatch]
public static class Stats
{
    internal static readonly StatEngine Engine = new();

    /// <summary>Adds a modifier for one stat, on every fighter. Dispose the result to remove it.</summary>
    /// <param name="stat">The stat to change.</param>
    /// <param name="modifier">The change.</param>
    /// <param name="order">Lower runs first. Default 0.</param>
    public static IDisposable Modify(SkillCalcType stat, StatModifier modifier, int order = 0)
        => Engine.Add(stat, modifier, order, false, modifier);

    /// <summary>
    /// Adds a modifier that only applies to the local player's own fighter:
    /// <c>Stats.ModifyLocal(SkillCalcType.MoveSpeed, v =&gt; Stats.Scale(v, 1.5f));</c>
    /// </summary>
    /// <param name="stat">The stat to change.</param>
    /// <param name="modifier">Maps the current value to the new one.</param>
    /// <param name="order">Lower runs first. Default 0.</param>
    public static IDisposable ModifyLocal(SkillCalcType stat, Func<float, float> modifier, int order = 0)
    {
        if (modifier == null) throw new ArgumentNullException(nameof(modifier));
        return Engine.Add(stat, (f, s, v) => modifier(v), order, true, modifier);
    }

    /// <summary>
    /// Scales a stat by <paramref name="factor"/>, flooring at <c>factor - 1</c> so it still has an
    /// effect when the game's own value is 0 (a bonus percentage with no skill contributing).
    /// <c>Scale(v, 1f)</c> returns <c>v</c> unchanged.
    /// </summary>
    /// <param name="value">The current value.</param>
    /// <param name="factor">1 = unchanged, 2 = double.</param>
    public static float Scale(float value, float factor)
    {
        if (float.IsNaN(factor) || float.IsInfinity(factor)) return value;
        if (factor < 0f) factor = 0f;
        if (Math.Abs(factor - 1f) < 0.0001f) return value;
        return Math.Max(value * factor, factor - 1f);
    }

    /// <summary>How many modifiers are active, over all stats.</summary>
    public static int Count => Engine.Count;

    // Same signature as the trainer's, verified live there.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Fighter), nameof(Fighter.CalcSkillAndStatusEffectValue))]
    static void Postfix(Fighter __instance, SkillCalcType type, ref float __result)
    {
        if (!Engine.Has(type)) return;
        Events.Hooks.Live("Fighter.CalcSkillAndStatusEffectValue");
        if (!Safety.GameplayAllowed) return;
        __result = Engine.Apply(type, __instance, f => Players.IsLocal(f), __result);
    }
}

// The modifier list, kept apart from the hook so it can be tested without the game.
//
// Per stat, an immutable array that is replaced on every change (copy-on-write): the stat function
// runs hundreds of times a second, so reading must not allocate, and a modifier that adds or removes
// modifiers while running must not disturb the pass in progress.
internal sealed class StatEngine
{
    internal sealed class Entry : IDisposable
    {
        public SkillCalcType Stat;
        public StatModifier Fn;
        public Delegate Source;        // what the mod passed in, for blame
        public int Order;
        public long Seq;
        public int Failures;
        public bool LocalOnly;
        public bool Removed;
        public StatEngine Owner;
        public void Dispose() => Owner.Remove(this);
    }

    public const int MaxFailures = 5;
    public const int MaxDepth = 4;     // a modifier that reads its own stat would otherwise recurse forever

    readonly Dictionary<SkillCalcType, Entry[]> mods = new();
    long seq;
    int depth;

    public int Count { get { int n = 0; foreach (var a in mods.Values) n += a.Length; return n; } }

    public bool Has(SkillCalcType t) => mods.TryGetValue(t, out var a) && a.Length > 0;

    public IDisposable Add(SkillCalcType stat, StatModifier fn, int order, bool localOnly, Delegate source)
    {
        if (fn == null) throw new ArgumentNullException(nameof(fn));
        var e = new Entry { Stat = stat, Fn = fn, Order = order, Seq = ++seq, LocalOnly = localOnly, Source = source ?? fn, Owner = this };
        var l = new List<Entry>(mods.TryGetValue(stat, out var cur) ? cur : Array.Empty<Entry>()) { e };
        l.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Seq.CompareTo(b.Seq));
        mods[stat] = l.ToArray();
        return e;
    }

    void Remove(Entry e)
    {
        if (e.Removed) return;
        e.Removed = true;
        if (!mods.TryGetValue(e.Stat, out var cur)) return;
        mods[e.Stat] = Array.FindAll(cur, x => !ReferenceEquals(x, e));
    }

    public float Apply(SkillCalcType type, Fighter f, Func<Fighter, bool> isLocal, float value)
    {
        if (!mods.TryGetValue(type, out var arr) || arr.Length == 0) return value;
        if (depth >= MaxDepth) return value;
        depth++;
        bool? local = null;
        try
        {
            foreach (var e in arr)
            {
                if (e.Removed || e.Failures >= MaxFailures) continue;
                // "is not null", not "!= null": Unity's overloaded operator calls into the engine.
                if (e.LocalOnly && !(local ??= f is not null && isLocal(f))) continue;
                try
                {
                    float v = e.Fn(f, type, value);
                    if (!float.IsNaN(v) && !float.IsInfinity(v)) value = v;
                }
                catch (Exception ex)
                {
                    e.Failures++;
                    Safe.Blame(e.Source, $"stat modifier ({type})", ex);
                }
            }
        }
        finally { depth--; }
        return value;
    }
}
