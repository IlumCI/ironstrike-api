using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace IronstrikeApi.Gameplay;

/// <summary>A player in the current session.</summary>
public sealed class PlayerHandle
{
    /// <summary>Fusion's id for the player.</summary>
    public PlayerRef Ref { get; internal set; }
    /// <summary>The player's networked driver (cosmetics, weapons, voice).</summary>
    public NetworkPlayerFighterDriver Driver { get; internal set; }
    /// <summary>The player's fighter (health, skills, faction), or null while spawning.</summary>
    public Fighter Fighter => Driver != null ? Driver.fighter : null;
    /// <summary>True for this machine's player.</summary>
    public bool IsLocal { get; internal set; }

    /// <summary>
    /// The player's display name (their Steam name). For showing on screen only: never write it to
    /// a log or send it anywhere (use <see cref="ApiLog.Redact"/>).
    /// </summary>
    public string DisplayName
    {
        get { try { return Driver?.DisplayName ?? ""; } catch (Exception) { return ""; } }
    }
}

/// <summary>The players in the current session.</summary>
public static class Players
{
    /// <summary>The local player's fighter, or null (main menu, loading, dead and despawned).</summary>
    public static Fighter LocalFighter
    {
        get { try { return GM.instance?.LocalPlayerFighter; } catch (Exception) { return null; } }
    }

    /// <summary>True if <paramref name="f"/> is the local player's fighter.</summary>
    /// <param name="f">Any fighter, or null.</param>
    public static bool IsLocal(Fighter f)
    {
        if (f == null) return false;
        var me = LocalFighter;
        return me != null && f.Pointer == me.Pointer;
    }

    /// <summary>Every spawned player, the local one included. Empty outside a session.</summary>
    public static IReadOnlyList<PlayerHandle> All
    {
        get
        {
            var list = new List<PlayerHandle>();
            try
            {
                var nl = GM.instance?.NetLifecycle;
                var spawned = nl?._spawnedPlayers;
                if (spawned == null) return list;
                var r = nl._runner;
                PlayerRef? local = r != null && r.IsRunning ? r.LocalPlayer : null;
                foreach (var kv in spawned)
                    list.Add(new PlayerHandle { Ref = kv.Key, Driver = kv.Value, IsLocal = local.HasValue && kv.Key == local.Value });
            }
            catch (Exception e) { ApiLog.WarnOnce(null, "players:" + e.GetType().Name, $"could not list players: {e.Message}"); }
            return list;
        }
    }

    /// <summary>The number of spawned players.</summary>
    public static int Count
    {
        get { try { return GM.instance?.NetLifecycle?.SpawnedPlayerCount ?? 0; } catch (Exception) { return 0; } }
    }

    /// <summary>The player with this id, or null.</summary>
    /// <param name="p">A Fusion player ref.</param>
    public static PlayerHandle Get(PlayerRef p)
    {
        foreach (var h in All) if (h.Ref == p) return h;
        return null;
    }

    /// <summary>Brings a downed player back. Gameplay change; host only in multiplayer.</summary>
    /// <param name="f">The fighter to revive; null means the local player.</param>
    public static bool Revive(Fighter f = null)
    {
        if (!Safety.Check(null, "Players.Revive")) return false;
        f ??= LocalFighter;
        if (f == null || GM.instance == null) return false;
        GM.instance.RevivePlayerFighter(f);
        return true;
    }
}

/// <summary>The enemy bots. Changing them is a gameplay change, and host-side.</summary>
public static class Bots
{
    /// <summary>The fighters of every live bot.</summary>
    public static IReadOnlyList<Fighter> All
    {
        get
        {
            var list = new List<Fighter>();
            try
            {
                var bots = AIHivemind.instance?.bots;
                if (bots == null) return list;
                for (int i = 0; i < bots.Count; i++)
                {
                    var f = bots[i]?.nbfd?.fighter;
                    if (f != null) list.Add(f);
                }
            }
            catch (Exception) { }
            return list;
        }
    }

    /// <summary>The number of live bots.</summary>
    public static int Count => All.Count;

    /// <summary>
    /// Removes every bot, the way the game itself does (<c>NetworkGameMaster.DespawnBot</c>).
    /// Returns how many were removed. Host only. (<c>GM.DespawnAllBots</c> is a no-op in the
    /// shipped game.)
    /// </summary>
    public static int DespawnAll()
    {
        if (!Safety.Check(null, "Bots.DespawnAll") || !Game.IsHost) return 0;
        var ngm = GM.instance?.NetGameMaster;
        var bots = AIHivemind.instance?.bots;
        if (ngm == null || bots == null) return 0;

        // Snapshot first: DespawnBot deregisters, which mutates the list.
        var drivers = new List<NetworkBotFighterDriver>();
        for (int i = 0; i < bots.Count; i++) { var d = bots[i]?.nbfd; if (d != null) drivers.Add(d); }
        int n = 0;
        foreach (var d in drivers)
        {
            try { ngm.DespawnBot(d); n++; } catch (Exception e) { Plugin.Log.LogWarning($"despawn failed: {e.Message}"); }
        }
        return n;
    }

    /// <summary>Damages every bot (the game's own debug helper). Host only.</summary>
    public static bool HurtAll()
    {
        if (!Safety.Check(null, "Bots.HurtAll") || !Game.IsHost || GM.instance == null) return false;
        GM.instance.HurtAllBots();
        return true;
    }

    /// <summary>Spawns a training dummy (the game's own debug helper). Host only.</summary>
    public static bool SpawnDummy()
    {
        if (!Safety.Check(null, "Bots.SpawnDummy") || !Game.IsHost || GM.instance == null) return false;
        GM.instance.SpawnDummyPlayer();
        return true;
    }
}

/// <summary>Status effects (Haste, Invisibility, Ironskin, Poison...), through the game's own manager.</summary>
/// <remarks>
/// Prefer a status effect over a stat hack when one exists: the AI and every other system already
/// know how to react to it. Invisibility through <see cref="SkillFlagType"/> froze the bots; the
/// Invisibility status works.
/// </remarks>
public static class Status
{
    /// <summary>Gives a status effect for a time. Returns false if refused or not possible right now.</summary>
    /// <param name="target">Who gets it.</param>
    /// <param name="type">Which effect.</param>
    /// <param name="seconds">How long.</param>
    /// <param name="amount">The effect's strength; meaning depends on the effect.</param>
    /// <param name="givenBy">Who it is from; null means the target.</param>
    public static bool Give(Fighter target, StatusType type, float seconds, float amount = 1f, Fighter givenBy = null)
    {
        if (!Safety.Check(null, "Status.Give") || target == null) return false;
        var sm = StatusManager.instance;
        if (sm == null || !Game.NetworkRunning) return false;
        int end = TimeHelper.CurrentTick + TimeHelper.GetTicks(seconds);
        sm.GiveStatusEffectToFighter(type, givenBy ?? target, target, end, amount);
        return true;
    }

    /// <summary>Removes a status effect.</summary>
    /// <param name="target">Who has it.</param>
    /// <param name="type">Which effect.</param>
    /// <param name="givenBy">Who gave it; null means the target.</param>
    public static bool Remove(Fighter target, StatusType type, Fighter givenBy = null)
    {
        if (!Safety.Check(null, "Status.Remove") || target == null) return false;
        var sm = StatusManager.instance;
        if (sm == null || !Game.NetworkRunning) return false;
        sm.RemoveStatusEffectFromFighter(type, givenBy ?? target, target);
        return true;
    }
}

/// <summary>Skills and weapon sets.</summary>
public static class Loadout
{
    /// <summary>Every skill the game defines (including hidden ones; check <c>Skill.hidden</c>).</summary>
    public static IReadOnlyList<Skill> Skills
    {
        get
        {
            var list = new List<Skill>();
            try
            {
                var all = SkillManager.instance?.GetSkills();
                if (all != null) for (int i = 0; i < all.Count; i++) list.Add(all[i]);
            }
            catch (Exception) { }
            return list;
        }
    }

    /// <summary>Every weapon set (class loadout) the game defines.</summary>
    public static IReadOnlyList<WeaponSet> WeaponSets
    {
        get
        {
            var list = new List<WeaponSet>();
            try
            {
                var sets = ArmoryManager.instance?.weaponSetDatabase?.weaponSets;
                if (sets != null) for (int i = 0; i < sets.Count; i++) list.Add(sets[i]);
            }
            catch (Exception) { }
            return list;
        }
    }

    /// <summary>Gives a skill at a level (1-5 normal, 6-10 enhanced).</summary>
    /// <param name="type">The skill.</param>
    /// <param name="level">Its level.</param>
    /// <param name="f">Who gets it; null means the local player.</param>
    public static bool GiveSkill(SkillType type, int level = 1, Fighter f = null)
    {
        if (!Safety.Check(null, "Loadout.GiveSkill")) return false;
        f ??= Players.LocalFighter;
        var sm = SkillManager.instance;
        if (f == null || sm == null) return false;
        sm.GiveSkillToFighter(type, f, Math.Clamp(level, 1, 10));
        return true;
    }

    /// <summary>Takes a skill away.</summary>
    /// <param name="type">The skill.</param>
    /// <param name="f">From whom; null means the local player.</param>
    public static bool RemoveSkill(SkillType type, Fighter f = null)
    {
        if (!Safety.Check(null, "Loadout.RemoveSkill")) return false;
        f ??= Players.LocalFighter;
        var sm = SkillManager.instance;
        if (f == null || sm == null) return false;
        sm.RemoveSkillFromFighter(type, f);
        return true;
    }

    /// <summary>Switches the local player to a weapon set.</summary>
    /// <param name="set">One of <see cref="WeaponSets"/>.</param>
    public static bool GiveWeaponSet(WeaponSet set)
    {
        if (!Safety.Check(null, "Loadout.GiveWeaponSet") || set == null || GM.instance == null) return false;
        GM.instance.GivePlayerWeaponSet(set);
        return true;
    }
}

/// <summary>
/// Where a fighter really is. A fighter's own transform is not its body (verified: a bot's transform
/// stays put while its body walks about), so use these for positions, distances and aiming.
/// </summary>
public static class Body
{
    static FighterIKRig Rig(Fighter f) => f.LogicBody ?? f.VisualBody;

    /// <summary>The point between the fighter's feet, on the ground.</summary>
    /// <param name="f">The fighter.</param>
    public static Vector3 Feet(Fighter f)
    {
        if (f == null) return Vector3.zero;
        var r = Rig(f);
        if (r != null && r.FeetNode != null) return r.FeetNode.position;
        if (r != null && r.BodyCenterNode != null) { var c = r.BodyCenterNode.position; c.y -= 1f; return c; }
        return f.transform.position;
    }

    /// <summary>The middle of the fighter's body: where to aim bolts and beams.</summary>
    /// <param name="f">The fighter.</param>
    public static Vector3 Center(Fighter f)
    {
        if (f == null) return Vector3.zero;
        var r = Rig(f);
        if (r != null && r.BodyCenterNode != null) return r.BodyCenterNode.position;
        return Feet(f) + Vector3.up;
    }

    /// <summary>The fighter's head.</summary>
    /// <param name="f">The fighter.</param>
    public static Vector3 Head(Fighter f)
    {
        if (f == null) return Vector3.zero;
        var r = Rig(f);
        if (r != null && r.Head != null) return r.Head.position;
        return Center(f) + Vector3.up * 0.6f;
    }
}
