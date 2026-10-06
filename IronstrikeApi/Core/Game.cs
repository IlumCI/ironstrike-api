using System;
using Fusion;

namespace IronstrikeApi;

/// <summary>Quick answers about the game's current state.</summary>
public static class Game
{
    /// <summary>The game's main object (<c>GM</c>), or null while starting.</summary>
    public static GM Main => GM.instance;

    /// <summary>The network runner of the current session, or null.</summary>
    public static NetworkRunner Runner
    {
        get { try { return GM.instance?.NetLifecycle?._runner; } catch (Exception) { return null; } }
    }

    /// <summary>
    /// True while a session's simulation is running. Note that the haven and solo runs are
    /// sessions too, so this is not a multiplayer test: use <see cref="Safety.Context"/>.
    /// </summary>
    public static bool NetworkRunning
    {
        get { try { return GM.instance?.NetLifecycle?.NetworkIsRunning() ?? false; } catch (Exception) { return false; } }
    }

    /// <summary>True if this machine has authority over the game (the host, or anyone in solo).</summary>
    public static bool IsHost
    {
        get { var r = Runner; return r != null && r.IsRunning && r.IsServer; }
    }

    /// <summary>The active scene, or null before the first one loads.</summary>
    public static SceneChange Scene => Events.Hooks.Scene;

    /// <summary>True between leaving the haven and coming back to it.</summary>
    public static bool InRun => Events.Hooks.InRun;

    /// <summary>The current level's number within the run (1 = first), or 0 outside a run.</summary>
    public static int LevelInRun => Events.Hooks.InRun ? Events.Hooks.LevelInRun : 0;

    /// <summary>The simulation's current tick, or -1 when no simulation is running.</summary>
    public static int Tick
    {
        // TimeHelper.CurrentTick throws outside a running simulation.
        get { if (!NetworkRunning) return -1; try { return TimeHelper.CurrentTick; } catch (Exception) { return -1; } }
    }

    /// <summary>Converts seconds to simulation ticks.</summary>
    /// <param name="seconds">A duration.</param>
    public static int Ticks(float seconds) => TimeHelper.GetTicks(seconds);
}
