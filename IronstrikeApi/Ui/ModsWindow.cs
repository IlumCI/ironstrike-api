using System;
using System.Collections.Generic;
using System.Linq;

namespace IronstrikeApi.Ui;

/// <summary>
/// The API's own window: every loaded mod on the left; on the right, its details, any rows it added
/// with <see cref="ModSettings.AddSection"/>, and its settings. Opened by the MODS button on the
/// Credits card, or F8.
/// </summary>
public static class ModsWindow
{
    static Window window;

    static Window W
    {
        get
        {
            if (window != null) return window;
            window = new Window("Mods");
            window.WithSidebar(() => Mods.All.Select(m => m.Name).ToList());
            window.AddTab("Installed", Installed);
            window.AddTab("Session", Session);
            return window;
        }
    }

    /// <summary>Opens the Mods window over the main menu, or in front of the player elsewhere.</summary>
    public static void Open() => W.Open(MainMenu.MenuAnchor);

    /// <summary>Opens or closes the Mods window.</summary>
    public static void Toggle() => W.Toggle(MainMenu.MenuAnchor);

    /// <summary>Opens the Mods window on one mod's page.</summary>
    /// <param name="guid">The mod's GUID.</param>
    public static void Show(string guid)
    {
        int i = Mods.All.ToList().FindIndex(m => m.Guid == guid);
        if (i >= 0) { W.SidebarIndex = i; W.TabIndex = 0; }
        Open();
    }

    internal static void Register()
    {
        if (Plugin.C.MenuButton.Value) MainMenu.AddButton("MODS", Toggle, MenuCard.Credits);
    }

    static void Installed(Page p)
    {
        var all = Mods.All;
        W.AutoRefreshSeconds = 0f;
        W.Subtitle = $"{all.Count} loaded  |  mod set {Mods.Hash}";
        if (all.Count == 0) { p.Text("No mods loaded.", muted: true); return; }
        var m = all[Math.Clamp(W.SidebarIndex, 0, all.Count - 1)];

        p.Header(m.Name);
        p.Info("Version", m.Version);
        p.Info("Kind", Describe(m.Kind));
        p.Info("GUID", m.Guid);
        if (m.Dependencies.Count > 0)
            p.Info("Needs", string.Join(", ", m.Dependencies.Select(d => Mods.Get(d)?.Name ?? d)));
        var users = all.Where(o => o.Dependencies.Contains(m.Guid)).Select(o => o.Name).ToList();
        if (users.Count > 0) p.Info("Used by", string.Join(", ", users));

        foreach (var s in ModSettings.Sections(m.Guid))
        {
            try { s(p); }
            catch (Exception e) { Safe.Blame(s, "Mods window section", e); p.Text("(this section failed to draw)", muted: true); }
        }

        if (m.Guid == ModApi.Guid) ApiPage(p);
        ModSettings.Draw(p, m.Config);
    }

    static string Describe(ModKind k) => k switch
    {
        ModKind.Client => "Client (your screen only)",
        ModKind.Gameplay => "Gameplay (solo and private games)",
        ModKind.Cheat => "Cheat (solo and private games)",
        ModKind.Library => "Library",
        _ => "Not declared",
    };

    static void ApiPage(Page p)
    {
        p.Header("Status");
        p.Info("Play context", Safety.Context.ToString());
        p.Info("Gameplay changes", Safety.GameplayAllowed ? "allowed" : "off (public game)");
        p.Info("Public matchmaking", "locked while mods are loaded");
        p.Info("Hooks confirmed", Events.Hooks.LiveHooks.Count.ToString());
    }

    // Who is here and what they run. Names are shown on screen only, never logged.
    static void Session(Page p)
    {
        W.AutoRefreshSeconds = 2f;
        p.Header("This session");
        p.Info("Context", Safety.Context.ToString());
        p.Info("Host", Game.Runner != null && Game.Runner.IsRunning ? (Game.IsHost ? "you" : "someone else") : "-");
        p.Info("In a run", Game.InRun ? $"yes, level {Game.LevelInRun}" : "no");
        p.Info("Scene", Game.Scene?.ToString() ?? "-");
        p.Info("Players with the API", ModNet.PeerCount.ToString() + " (besides you)");

        var players = Gameplay.Players.All;
        var rows = new List<string[]>();
        foreach (var pl in players)
        {
            var f = pl.Fighter;
            string hp = f != null ? $"{f.CurrentHealth:0}/{f.MaxHealth:0}" : "-";
            rows.Add(new[] { pl.IsLocal ? pl.DisplayName + " (you)" : pl.DisplayName, hp, f != null ? f.fighterClass.ToString() : "-" });
        }
        p.Header("Players");
        p.Table(new[] { new TableColumn("Name", 3), new TableColumn("Health", 1.2f), new TableColumn("Class", 1.2f) }, rows);
    }
}
