using System;
using Fusion;
using IronstrikeApi.Gameplay;

namespace IronstrikeApi;

/// <summary>A scene load, as reported by <see cref="GameEvents.SceneChanged"/>.</summary>
public sealed class SceneChange
{
    /// <summary>Unity build index of the new scene.</summary>
    public int BuildIndex { get; internal set; }
    /// <summary>Unity scene name.</summary>
    public string Name { get; internal set; }
    /// <summary>The game's level for this scene, or null for scenes that are not levels.</summary>
    public LevelSceneNum? Level { get; internal set; }
    /// <summary>True for the haven, the hub between runs.</summary>
    public bool IsHaven => Level == LevelSceneNum.Haven;
    /// <inheritdoc/>
    public override string ToString() => Level?.ToString() ?? Name;
}

/// <summary>A multiplayer session starting or ending, as reported by <see cref="GameEvents"/>.</summary>
public sealed class SessionChange
{
    /// <summary>What kind of game this is.</summary>
    public PlayContext Context { get; internal set; }
    /// <summary>The Fusion mode the session runs in (Single for solo, Host, Client).</summary>
    public GameMode Mode { get; internal set; }
    /// <summary>True if this machine is the host (always true in solo).</summary>
    public bool IsHost => Mode != GameMode.Client;
    /// <summary>On <see cref="GameEvents.SessionEnded"/>, why Fusion shut the session down.</summary>
    public ShutdownReason? Reason { get; internal set; }
    /// <inheritdoc/>
    public override string ToString() => $"{Context} ({Mode})";
}

/// <summary>How a run ended.</summary>
public enum RunOutcome
{
    /// <summary>The run ended some other way: left, disconnected, quit to the haven.</summary>
    Ended = 0,
    /// <summary>The team won the run.</summary>
    Won = 1,
    /// <summary>The team lost the run.</summary>
    Lost = 2,
}

/// <summary>
/// The game, as a set of C# events. Subscribe from your plugin's <c>Load()</c>:
/// <c>GameEvents.RunStarted += () => Log.LogInfo("go!");</c>
/// </summary>
/// <remarks>
/// <para>Every event fires on the Unity main thread. A handler that throws is logged with your mod's
/// name and does not stop other mods' handlers; one that keeps throwing is muted.</para>
/// <para>Events fire in every <see cref="PlayContext"/>, public included: reading is always fine.
/// Changing the game is gated separately (<see cref="Safety.GameplayAllowed"/>).</para>
/// <para>"Synced" events (runs, levels, encounters) fire on every player's machine. Host-only
/// events say so.</para>
/// </remarks>
public static class GameEvents
{
    /// <summary>Every frame, from the game's own main loop (<c>GM.Update</c>).</summary>
    public static event Action Update;

    /// <summary>A new scene became active: the haven, a level, the tutorial.</summary>
    public static event Action<SceneChange> SceneChanged;

    /// <summary>A session started. The haven and solo runs are sessions too (<see cref="PlayContext.Solo"/>).</summary>
    public static event Action<SessionChange> SessionStarted;

    /// <summary>The session's network runner shut down.</summary>
    public static event Action<SessionChange> SessionEnded;

    /// <summary>A run began (the team left the haven).</summary>
    public static event Action RunStarted;

    /// <summary>A run is over.</summary>
    public static event Action<RunOutcome> RunEnded;

    /// <summary>A level of the run started; the argument is the level number within the run.</summary>
    public static event Action<int> LevelStarted;

    /// <summary>An encounter (a fight) started; the argument is its index in the level.</summary>
    public static event Action<int> EncounterStarted;

    /// <summary>An encounter was cleared; the argument is its index in the level.</summary>
    public static event Action<int> EncounterCompleted;

    /// <summary>A player connected. Reliable on the host; clients may only hear about themselves.</summary>
    public static event Action<PlayerRef> PlayerJoined;

    /// <summary>A player disconnected. Reliable on the host.</summary>
    public static event Action<PlayerRef> PlayerLeft;

    /// <summary>
    /// A hit is being resolved. Read <see cref="DamageEvent.Amount"/>; set it, or call
    /// <see cref="DamageEvent.Cancel"/>, to change the outcome (only where gameplay changes are allowed).
    /// </summary>
    public static event Action<DamageEvent> Damage;

    /// <summary>A fighter (player or bot) died on this machine.</summary>
    public static event Action<Fighter> FighterDied;

    /// <summary>A projectile (arrow, bolt, spell) was spawned and given its owner.</summary>
    public static event Action<ProjectileEvent> ProjectileSpawned;

    // ------------------------------------------------------------------ raising

    internal static bool AnyDamage => Damage != null;

    static void Trace(string what)
    {
        if (Plugin.C.LogEvents.Value) Plugin.Log.LogInfo("event: " + what);
    }

    internal static void RaiseUpdate() => Safe.Run(Update, "Update");
    internal static void RaiseScene(SceneChange s) { Trace($"SceneChanged {s}"); Safe.Run(SceneChanged, "SceneChanged", s); }
    internal static void RaiseSessionStarted(SessionChange s) { Trace($"SessionStarted {s}"); Safe.Run(SessionStarted, "SessionStarted", s); }
    internal static void RaiseSessionEnded(SessionChange s) { Trace($"SessionEnded {s} {s.Reason}"); Safe.Run(SessionEnded, "SessionEnded", s); }
    internal static void RaiseRunStarted() { Trace("RunStarted"); Safe.Run(RunStarted, "RunStarted"); }
    internal static void RaiseRunEnded(RunOutcome o) { Trace($"RunEnded {o}"); Safe.Run(RunEnded, "RunEnded", o); }
    internal static void RaiseLevel(int n) { Trace($"LevelStarted {n}"); Safe.Run(LevelStarted, "LevelStarted", n); }
    internal static void RaiseEncounterStarted(int i) { Trace($"EncounterStarted {i}"); Safe.Run(EncounterStarted, "EncounterStarted", i); }
    internal static void RaiseEncounterCompleted(int i) { Trace($"EncounterCompleted {i}"); Safe.Run(EncounterCompleted, "EncounterCompleted", i); }
    internal static void RaisePlayerJoined(PlayerRef p) { Trace($"PlayerJoined #{p.PlayerId}"); Safe.Run(PlayerJoined, "PlayerJoined", p); }
    internal static void RaisePlayerLeft(PlayerRef p) { Trace($"PlayerLeft #{p.PlayerId}"); Safe.Run(PlayerLeft, "PlayerLeft", p); }
    internal static void RaiseDamage(DamageEvent e) => Safe.Run(Damage, "Damage", e);
    internal static void RaiseDied(Fighter f) { Trace($"FighterDied {f?.faction}"); Safe.Run(FighterDied, "FighterDied", f); }
    internal static void RaiseProjectile(ProjectileEvent e) => Safe.Run(ProjectileSpawned, "ProjectileSpawned", e);
}
