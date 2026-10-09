using System;
using System.Collections.Generic;
using System.Linq;
using IronstrikeApi.Core;
using IronstrikeApi.Gameplay;
using UnityEngine;

namespace IronstrikeApi.Content;

/// <summary>
/// One cast of a <see cref="CustomSpell"/>, as <see cref="CustomSpell.OnCast"/> sees it: where it was
/// aimed and what it hit, plus the tools to do the spell's work.
/// </summary>
/// <remarks>
/// <para><see cref="CustomSpell.OnCast"/> runs on every machine. The tools here know that:
/// <see cref="Damage"/> and <see cref="Heal"/> are sent once, from the caster's machine, through the
/// game's own messages; <see cref="Status"/>, the effects and the spawners act on every machine
/// alike, as the game's own spells do.</para>
/// </remarks>
public sealed class SpellCast
{
    internal SpellCast() { }

    /// <summary>The caster's wand.</summary>
    public CasterWeapon Wand { get; internal set; }

    /// <summary>The caster.</summary>
    public Fighter Caster { get; internal set; }

    /// <summary>Where the spell left the wand.</summary>
    public Vector3 Origin { get; internal set; }

    /// <summary>The direction it was aimed (for Ray spells).</summary>
    public Vector3 Direction { get; internal set; }

    /// <summary>The fighter it was cast on (Single... and Self spells), or null.</summary>
    public Fighter Target { get; internal set; }

    /// <summary>The ground point it was cast at (GroundCircle spells).</summary>
    public Vector3 Point { get; internal set; }

    /// <summary>What <see cref="CustomSpell.Amount"/> returned on the caster's machine.</summary>
    public float Amount { get; internal set; }

    /// <summary>True on the caster's own machine.</summary>
    public bool IsCaster => Players.IsLocal(Caster);

    internal SpellContext Context;
    internal Injected.ApiSpell Game;

    /// <summary>
    /// Damages a fighter with this spell. Goes through the game's own spell-hit message, so armour,
    /// skills, damage numbers and kills work as for the game's spells. Sent once, by the caster.
    /// </summary>
    /// <param name="target">Who is hit.</param>
    /// <param name="damage">The damage.</param>
    public void Damage(Fighter target, float damage)
    {
        if (target is null || damage <= 0) return;
        if (!IsCaster) { if (Game is not null) SpellHost.Once(Game, "dmg-remote", "Damage is sent by the caster's machine; skipped here"); return; }
        bool sent = SpellEffects.SendHit(Caster, Wand, target, damage, Game?.spellType ?? SpellType.None, Origin);
        if (Game is not null) SpellHost.Once(Game, "dmg", sent ? $"damage sent ({damage:0})" : "damage could not be sent (no network id or event manager)");
    }

    /// <summary>Heals a player. Sent once, by the caster. Bots are healed by the host.</summary>
    /// <param name="target">Who is healed.</param>
    /// <param name="amount">Health restored.</param>
    public void Heal(Fighter target, float amount)
    {
        if (target is null || amount <= 0) return;
        SpellEffects.Heal(Caster, target, amount, IsCaster);
    }

    /// <summary>Gives a status effect (Poison, Slowed, Stunned, Levitation, Barrier...).</summary>
    /// <param name="target">Who gets it.</param>
    /// <param name="type">The effect.</param>
    /// <param name="seconds">How long.</param>
    /// <param name="amount">Its strength (damage per second for Poison and Acid).</param>
    public void Status(Fighter target, StatusType type, float seconds, float amount = 1f)
        => SpellEffects.Status(Caster, target, type, seconds, amount);

    /// <summary>Living enemies of the caster within a radius of a point, nearest first.</summary>
    /// <param name="center">The point.</param>
    /// <param name="radius">Metres.</param>
    public List<Fighter> EnemiesNear(Vector3 center, float radius)
    {
        var l = SpellEffects.Near(Caster, center, radius, enemies: true);
        if (Game is not null) SpellHost.Once(Game, "near", $"first area query: {l.Count} enemies within {radius:0.#} m of the point, caster local {IsCaster}");
        return l;
    }

    /// <summary>Living allies of the caster (the caster included) within a radius, nearest first.</summary>
    /// <param name="center">The point.</param>
    /// <param name="radius">Metres.</param>
    public List<Fighter> AlliesNear(Vector3 center, float radius) => SpellEffects.Near(Caster, center, radius, enemies: false);

    /// <summary>Fallen allied players within a radius (for spells that bring them back), nearest first.</summary>
    /// <param name="center">The point.</param>
    /// <param name="radius">Metres.</param>
    public List<Fighter> FallenAlliesNear(Vector3 center, float radius) => SpellEffects.Fallen(Caster, center, radius);

    /// <summary>Brings a fallen allied player back, the way the game revives players. Runs everywhere.</summary>
    /// <param name="player">The fallen player.</param>
    public void Revive(Fighter player)
    {
        if (player is null || !player.dead || GM.instance == null || SpellEffects.IsBot(player)) return;
        GM.instance.RevivePlayerFighter(player);
    }

    /// <summary>
    /// Runs something later, on every machine (for salvos, delayed slams, damage over time). Dropped
    /// when the session ends. The action gets the same cast.
    /// </summary>
    /// <param name="seconds">How long to wait.</param>
    /// <param name="action">What to do.</param>
    public void After(float seconds, Action<SpellCast> action)
    {
        if (action == null) return;
        var me = this;
        SpellEffects.Later.Add(Mathf.Max(0f, seconds), () => action(me), Context?.Spell?.Key, action);
    }

    /// <summary>
    /// Spawns the area effect of the spell named in <see cref="CustomSpell.LooksLike"/> (Balefire's
    /// flames, Cometfall's impact, Acid Spray's cloud...) at a point; fighters it touches are passed to
    /// <see cref="CustomSpell.OnAreaHit"/>. Only works when that spell has an area effect.
    /// </summary>
    /// <param name="origin">Where.</param>
    /// <param name="direction">Facing.</param>
    /// <param name="baseDamage">Damage the effect carries (handed to OnAreaHit's default).</param>
    public bool SpawnArea(Vector3 origin, Vector3 direction, float baseDamage)
        => Game != null && Game.SpawnArea(Wand, origin, direction, baseDamage);

    /// <summary>
    /// Fires the projectile of the spell named in <see cref="CustomSpell.LooksLike"/> (Fireball,
    /// Cobbleshot, Icicles, Starfire). It flies and hits like that spell's.
    /// </summary>
    /// <param name="origin">Where from.</param>
    /// <param name="direction">Which way.</param>
    public bool SpawnProjectile(Vector3 origin, Vector3 direction)
        => Game != null && Game.SpawnShot(Wand, origin, direction);
}

/// <summary>Short-lived visual effects for spells. Local to this machine; call them from synced code.</summary>
public static class SpellFx
{
    /// <summary>A jagged lightning bolt between two points that fades out.</summary>
    /// <param name="from">Start.</param>
    /// <param name="to">End.</param>
    /// <param name="color">Colour.</param>
    /// <param name="width">Width in metres.</param>
    /// <param name="seconds">How long it shows.</param>
    public static void Bolt(Vector3 from, Vector3 to, Color color, float width = 0.08f, float seconds = 0.35f)
        => SpellEffects.Line(from, to, color, width, seconds, jag: 0.35f);

    /// <summary>A straight beam between two points that fades out.</summary>
    /// <param name="from">Start.</param>
    /// <param name="to">End.</param>
    /// <param name="color">Colour.</param>
    /// <param name="width">Width in metres.</param>
    /// <param name="seconds">How long it shows.</param>
    public static void Beam(Vector3 from, Vector3 to, Color color, float width = 0.5f, float seconds = 0.6f)
        => SpellEffects.Line(from, to, color, width, seconds, jag: 0f);

    /// <summary>A ring on the ground that fades out (an area's edge).</summary>
    /// <param name="center">Centre.</param>
    /// <param name="radius">Metres.</param>
    /// <param name="color">Colour.</param>
    /// <param name="seconds">How long it shows.</param>
    public static void Ring(Vector3 center, float radius, Color color, float seconds = 0.8f)
        => SpellEffects.Ring(center, radius, color, seconds);

    /// <summary>A flash of coloured light lighting up the scene, like the game's lightning.</summary>
    /// <param name="at">Where.</param>
    /// <param name="color">Colour.</param>
    /// <param name="intensity">Brightness (the game uses about 1 to 4).</param>
    /// <param name="seconds">Fade time.</param>
    public static void Flash(Vector3 at, Color color, float intensity = 2f, float seconds = 0.4f)
        => SpellEffects.Flash(at, color, intensity, seconds);
}

// The plumbing behind SpellCast and SpellFx.
internal static class SpellEffects
{
    internal static bool SendHit(Fighter attacker, CasterWeapon wand, Fighter target, float damage, SpellType spell, Vector3 origin)
    {
        var ev = GM.instance?.Events;
        var a = attacker?.networkBehavior?.Object;
        var t = target?.networkBehavior?.Object;
        if (ev == null || a == null || t == null || target.dead) return false;
        var dir = Body.Center(target) - origin;
        bool offhand = wand != null && attacker.OffWeapon != null && wand.weapon == attacker.OffWeapon;
        bool deathblow = damage >= target.CurrentHealth;
        ev.RPC_AreaEffectHitFighter(a.Id, offhand, t.Id, origin, dir.sqrMagnitude > 0 ? dir.normalized : Vector3.forward, damage, spell, deathblow);
        return true;
    }

    // From an area effect's local-authority hit: the same message.
    internal static void Hit(Fighter attacker, Weapon weapon, Fighter target, float damage, SpellType spell)
    {
        var wand = weapon is not null ? weapon.CasterStuff : null;
        SendHit(attacker, wand, target, damage, spell, attacker is not null ? Body.Center(attacker) : Vector3.zero);
    }

    internal static void Heal(Fighter caster, Fighter target, float amount, bool isCaster)
    {
        if (target.dead) return;
        if (IsBot(target))
        {
            if (Game.IsHost) target.CurrentHealth = Mathf.Min(target.MaxHealth, target.CurrentHealth + amount);
            return;
        }
        if (!isCaster) return;
        var ev = GM.instance?.Events;
        var c = caster?.networkBehavior?.Object;
        var t = target.networkBehavior?.Object;
        if (ev != null && c != null && t != null) ev.RPC_HealPlayer(c.Id, t.Id, amount);
    }

    internal static bool IsBot(Fighter f) => f.faction == Faction.EnemyBots || (f.nfd != null && f.nfd.TryCast<NetworkBotFighterDriver>() != null);

    internal static void Status(Fighter caster, Fighter target, StatusType type, float seconds, float amount)
    {
        var sm = StatusManager.instance;
        if (sm == null || target is null || target.dead || !Game.NetworkRunning) return;
        int end = TimeHelper.CurrentTick + TimeHelper.GetTicks(seconds);
        sm.GiveStatusEffectToFighter(type, caster ?? target, target, end, amount);
    }

    // Every fighter in the session: the bots (AIHivemind) and the players. GM.Fighters is not it: it
    // does not hold the bots (verified: area spells found nobody through it).
    internal static List<Fighter> AllFighters()
    {
        var list = new List<Fighter>(Bots.All);
        foreach (var p in Players.All)
            if (p.Fighter is not null && !list.Any(f => f.Pointer == p.Fighter.Pointer)) list.Add(p.Fighter);
        var me = Players.LocalFighter;
        if (me is not null && !list.Any(f => f.Pointer == me.Pointer)) list.Add(me);
        return list;
    }

    internal static List<Fighter> Near(Fighter caster, Vector3 center, float radius, bool enemies)
    {
        var list = new List<Fighter>();
        var all = AllFighters();
        if (caster is null) return list;
        float r2 = radius * radius;
        foreach (var f in all)
        {
            if (f is null || f.dead || f.CurrentHealth <= 0) continue;
            bool same = GM.isSameTeam(caster.faction, f.faction);
            if (same == enemies) continue;
            if ((Body.Feet(f) - center).sqrMagnitude > r2 && (Body.Center(f) - center).sqrMagnitude > r2) continue;
            list.Add(f);
        }
        // Same order on every machine: by distance, ties by network id.
        return list.OrderBy(f => Mathf.Round((Body.Feet(f) - center).sqrMagnitude * 100f))
                   .ThenBy(f => f.networkBehavior?.Object != null ? f.networkBehavior.Object.Id.Raw : 0u).ToList();
    }

    internal static List<Fighter> Fallen(Fighter caster, Vector3 center, float radius)
    {
        var list = new List<Fighter>();
        var all = AllFighters();
        if (caster is null) return list;
        float r2 = radius * radius;
        foreach (var f in all)
        {
            if (f is null || !f.dead || IsBot(f) || !GM.isSameTeam(caster.faction, f.faction)) continue;
            if ((Body.Feet(f) - center).sqrMagnitude <= r2) list.Add(f);
        }
        return list.OrderBy(f => (Body.Feet(f) - center).sqrMagnitude).ToList();
    }

    // Delayed spell work, run from the API's per-frame tick on simulation time.
    internal static class Later
    {
        sealed class Job { public float At; public Action Run; public string Key; public Delegate Owner; }
        static readonly List<Job> jobs = new();

        internal static void Add(float seconds, Action run, string key, Delegate owner)
        {
            if (jobs.Count > 512) { ApiLog.WarnOnce(null, "content:later", "too many delayed spell actions; dropping new ones"); return; }
            jobs.Add(new Job { At = Time.time + seconds, Run = run, Key = key, Owner = owner });
        }

        internal static void Tick()
        {
            if (jobs.Count == 0) return;
            float now = Time.time;
            var due = jobs.Where(j => j.At <= now).ToList();
            if (due.Count == 0) return;
            jobs.RemoveAll(j => j.At <= now);
            foreach (var j in due)
            {
                if (!ModContent.Active) continue;
                try { j.Run(); } catch (Exception e) { Safe.Blame(j.Owner, $"spell {j.Key} delayed action", e); }
            }
        }

        internal static void Clear() => jobs.Clear();
    }

    // ------------------------------------------------------------------ visuals

    static Material lineMat;

    static Material LineMaterial()
    {
        if (lineMat != null) return lineMat;
        foreach (var name in new[] { "Sprites/Default", "Legacy Shaders/Particles/Additive", "Particles/Standard Unlit", "Unlit/Color" })
        {
            var sh = Shader.Find(name);
            if (sh != null) { lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave }; Diag.Info($"spell effects: lines drawn with {name}"); return lineMat; }
        }
        foreach (var tr in UnityEngine.Object.FindObjectsOfType<TrailRenderer>())
            if (tr.sharedMaterial != null) { lineMat = new Material(tr.sharedMaterial) { hideFlags = HideFlags.HideAndDontSave }; Diag.Info($"spell effects: lines drawn with {tr.sharedMaterial.shader?.name}"); return lineMat; }
        ApiLog.WarnOnce(null, "content:fx", "spell effects: no shader for lines; SpellFx lines are off");
        return null;
    }

    internal static void Line(Vector3 from, Vector3 to, Color color, float width, float seconds, float jag)
    {
        var mat = LineMaterial();
        if (mat == null) return;
        var go = new GameObject("ApiSpellFx");
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.startColor = lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width * (jag > 0 ? 0.5f : 1f);
        lr.useWorldSpace = true;
        int n = jag > 0 ? Mathf.Clamp((int)(Vector3.Distance(from, to) / 0.8f), 4, 24) : 2;
        lr.positionCount = n;
        var rnd = new System.Random(unchecked((int)(from.x * 1000 + to.z * 7)));
        var side = Vector3.Cross(to - from, Vector3.up).normalized;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            var p = Vector3.Lerp(from, to, t);
            if (jag > 0 && i > 0 && i < n - 1)
                p += side * (float)(rnd.NextDouble() - 0.5) * jag * 2f + Vector3.up * (float)(rnd.NextDouble() - 0.5) * jag;
            lr.SetPosition(i, p);
        }
        Fade.Start(go, lr, null, color, seconds);
    }

    internal static void Ring(Vector3 center, float radius, Color color, float seconds)
    {
        var mat = LineMaterial();
        if (mat == null) return;
        var go = new GameObject("ApiSpellFxRing");
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.startColor = lr.endColor = color;
        lr.startWidth = lr.endWidth = 0.12f;
        lr.loop = true;
        lr.useWorldSpace = true;
        const int n = 48;
        lr.positionCount = n;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0.1f, Mathf.Sin(a) * radius));
        }
        Fade.Start(go, lr, null, color, seconds);
    }

    static GlobalLighting lighting;

    internal static void Flash(Vector3 at, Color color, float intensity, float seconds)
    {
        if (lighting == null) lighting = UnityEngine.Object.FindObjectOfType<GlobalLighting>();
        try { lighting?.FlashCustomLight(at, intensity, seconds, color, 1f); } catch (Exception) { }
    }

    // Fades effects out from the API's per-frame tick (no MonoBehaviour needed).
    internal static class Fade
    {
        sealed class Item { public GameObject Go; public LineRenderer Line; public Color Color; public float Start, Length; }
        static readonly List<Item> items = new();

        internal static void Start(GameObject go, LineRenderer lr, object _, Color c, float seconds)
            => items.Add(new Item { Go = go, Line = lr, Color = c, Start = Time.time, Length = Mathf.Max(0.05f, seconds) });

        internal static void Tick()
        {
            if (items.Count == 0) return;
            float now = Time.time;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                float t = (now - it.Start) / it.Length;
                if (it.Go == null || t >= 1f)
                {
                    if (it.Go != null) UnityEngine.Object.Destroy(it.Go);
                    items.RemoveAt(i);
                    continue;
                }
                var c = it.Color;
                c.a *= 1f - t;
                it.Line.startColor = it.Line.endColor = c;
            }
        }

        internal static void Clear()
        {
            foreach (var it in items) if (it.Go != null) UnityEngine.Object.Destroy(it.Go);
            items.Clear();
        }
    }
}
