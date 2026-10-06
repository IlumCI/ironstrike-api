using System;
using System.Reflection;
using Fusion;
using HarmonyLib;

namespace IronstrikeApi.Events;

// Session tracking, which also decides Safety.Context.
//
// Every session start in the game goes through NetworkRunner.StartGame (verified across all call
// sites while building Ironstrike Servers). The prefix only records what it sees; the session is
// classified on the next frame. Ironstrike Servers re-issues that same call with its lobby and token
// from inside its own prefix, so the first sight of a modded start can be the unmodified args;
// waiting a frame means the context is decided from the final ones.
[HarmonyPatch]
internal static class SessionHooks
{
    const string ModdedLobbyPrefix = "ism-";

    sealed class Seen
    {
        public IntPtr Runner;
        public GameMode Mode;
        public bool ModdedLobby;
        public bool Raised;
        public SessionChange Change;
    }

    static Seen current;

    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.StartGame))]
    static void StartGame(NetworkRunner __instance, StartGameArgs args)
    {
        if (args == null || __instance == null) return;
        Hooks.Live("NetworkRunner.StartGame (session tracking)");
        try
        {
            if (IsBrowseRunner(__instance)) return;
            var mode = args.GameMode;
            string lobby = null;
            try { lobby = args.CustomLobbyName; } catch (Exception) { }
            bool modded = lobby != null && lobby.StartsWith(ModdedLobbyPrefix, StringComparison.Ordinal);

            if (current != null && current.Runner == __instance.Pointer && !current.Raised)
            {
                current.Mode = mode;                          // the re-issued, final args
                current.ModdedLobby |= modded;
                return;
            }
            if (current != null && current.Raised) End(null);  // a new session replaces the old one
            current = new Seen { Runner = __instance.Pointer, Mode = mode, ModdedLobby = modded };
            if (mode == GameMode.Single) Safety.privateEntry = false;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"session tracking: {e.Message}"); }
    }

    // Ironstrike Servers lists its lobby from a second runner built from the game's prefab. It is not
    // a session the player is in.
    static bool IsBrowseRunner(NetworkRunner r)
    {
        try { return r.gameObject != null && r.gameObject.name.Contains("BrowseRunner"); }
        catch (Exception) { return false; }
    }

    internal static void Tick()
    {
        var s = current;
        if (s == null || s.Raised) return;
        s.Raised = true;

        var ctx = Classify(s.Mode, s.ModdedLobby, ServersSaysModded(), Safety.privateEntry);
        Safety.Context = ctx;
        s.Change = new SessionChange { Context = ctx, Mode = s.Mode };
        Plugin.Log.LogInfo($"session: {ctx} ({s.Mode}); gameplay changes {(Safety.GameplayAllowed ? "allowed" : "OFF")}");
        GameEvents.RaiseSessionStarted(s.Change);
    }

    // Anything not positively known to be solo, private or modded is treated as public: a session
    // must earn the right to gameplay changes, never get it by default.
    internal static PlayContext Classify(GameMode mode, bool moddedLobby, bool serversSaysModded, bool privateEntry)
    {
        if (mode == GameMode.Single) return PlayContext.Solo;
        if (moddedLobby || serversSaysModded) return PlayContext.ModdedServer;
        if (privateEntry && (mode == GameMode.Host || mode == GameMode.Client || mode == GameMode.AutoHostOrClient))
            return PlayContext.PrivateMatch;
        return PlayContext.Public;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkLogic), nameof(NetworkLogic.OnShutdown))]
    static void Shutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Hooks.Live("NetworkLogic.OnShutdown");
        var s = current;
        if (s == null || runner == null || runner.Pointer != s.Runner) return;
        End(shutdownReason);
    }

    static void End(ShutdownReason? reason)
    {
        var s = current;
        current = null;
        if (s == null || !s.Raised) return;
        s.Change.Reason = reason;
        Safety.Context = PlayContext.Offline;
        ModNet.Reset();
        GameEvents.RaiseSessionEnded(s.Change);
    }

    static PropertyInfo serversInModded;
    static bool lookedForServers;

    static bool ServersSaysModded()
    {
        try
        {
            if (!lookedForServers)
            {
                lookedForServers = true;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = a.GetType("IronstrikeServers.Api", false);
                    if (t != null) { serversInModded = t.GetProperty("InModdedSession", BindingFlags.Public | BindingFlags.Static); break; }
                }
            }
            return serversInModded?.GetValue(null) is true;
        }
        catch (Exception) { return false; }
    }
}
