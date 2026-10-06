using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace IronstrikeApi.Ui;

/// <summary>
/// A mod window in the game's world, drawn in the classic Steam style: a title bar, optional tabs,
/// an optional list on the left, and a scrolling page built with <see cref="Page"/>.
/// </summary>
/// <remarks>
/// <para>The window is a copy of the game's own Options window, so it is placed, raycast and
/// clicked the same way in VR and flat. One mod window is open at a time; opening another replaces
/// it.</para>
/// <para>Pages are rebuilt from your render callback whenever something changes (a control was
/// used, <see cref="Refresh"/> was called, or every <see cref="AutoRefreshSeconds"/>). Keep state in
/// your own fields or config, and draw it from there.</para>
/// <code>
/// var w = new Window("My mod");
/// w.AddTab("Settings", page =&gt; {
///     page.Toggle("Fast mode", fast, v =&gt; fast = v);
///     page.Button("Say hi", () =&gt; Log.LogInfo("hi"));
/// });
/// MainMenu.AddButton("MYMOD", () =&gt; w.Toggle(MainMenu.MenuAnchor));
/// </code>
/// </remarks>
public sealed class Window
{
    /// <summary>Window width in canvas units.</summary>
    public const float Width = Panel.Width;
    /// <summary>Window height in canvas units.</summary>
    public const float Height = Panel.Height;

    const float M = Panel.Margin;
    const float TabsY = Panel.TitleH + 6f, TabH = 64f;
    const float SideW = 620f, Gap = 18f, Bevel = 7f;
    const float TabText = 34f, SideText = 36f, SideRow = 70f;

    sealed class Tab
    {
        public string Name;
        public Action<Page> Render;
    }

    readonly List<Tab> tabs = new();
    Func<IReadOnlyList<string>> sidebar;
    float lastRender;
    int sideOffset;
    static bool rendering;      // a render (of any window) is in progress
    static bool pending;        // a redraw was asked for during it

    /// <summary>The window that is open (or was last opened), or null.</summary>
    public static Window Current { get; private set; }

    /// <summary>Title bar text.</summary>
    public string Title { get; set; }

    /// <summary>Smaller text on the right of the title bar.</summary>
    public string Subtitle { get; set; } = "";

    /// <summary>The selected tab.</summary>
    public int TabIndex { get; set; }

    /// <summary>The selected sidebar item, if the window has a sidebar.</summary>
    public int SidebarIndex { get; set; }

    /// <summary>If above 0, the page is rebuilt this often while open, for live data. Default 0.</summary>
    public float AutoRefreshSeconds { get; set; }

    /// <summary>Raised when the window closes (X button, <see cref="Close"/>, or another window opening).</summary>
    public event Action Closed;

    /// <summary>True if this window is showing.</summary>
    public bool IsOpen => Current == this && Panel.Visible;

    /// <summary>Makes a window. Nothing shows until <see cref="Open"/>.</summary>
    /// <param name="title">Title bar text.</param>
    public Window(string title) { Title = title; }

    /// <summary>Adds a tab. With one tab, no tab strip is drawn.</summary>
    /// <param name="name">Tab label.</param>
    /// <param name="render">Builds the tab's page.</param>
    public Window AddTab(string name, Action<Page> render)
    {
        tabs.Add(new Tab { Name = name, Render = render ?? throw new ArgumentNullException(nameof(render)) });
        return this;
    }

    /// <summary>
    /// Gives the window a selectable list on the left (like the Mods window's list of mods). Read
    /// <see cref="SidebarIndex"/> in your render callback.
    /// </summary>
    /// <param name="items">The item labels, asked for on every redraw.</param>
    public Window WithSidebar(Func<IReadOnlyList<string>> items)
    {
        sidebar = items;
        return this;
    }

    /// <summary>Shows the window: over <paramref name="beside"/> if given, else in front of the player.</summary>
    /// <param name="beside">A transform to open over, e.g. <see cref="MainMenu.MenuAnchor"/>.</param>
    public void Open(Transform beside = null)
    {
        if (Current != null && Current != this) Current.Closing();
        Panel.Build();
        if (!Panel.Ready) { Plugin.Log.LogWarning($"window '{Title}' cannot open yet: no Options menu in this scene"); return; }
        bool wasOpen = IsOpen;
        Current = this;
        Panel.OnBack = Close;
        Panel.Show(beside, keepPlace: wasOpen);
        Redraw(false);
    }

    /// <summary>Hides the window.</summary>
    public void Close()
    {
        if (Current != this) return;
        Panel.Hide();
        Closing();
    }

    /// <summary>Opens the window if it is closed, closes it if it is open.</summary>
    /// <param name="beside">Where to open it; see <see cref="Open"/>.</param>
    public void Toggle(Transform beside = null)
    {
        if (IsOpen) Close(); else Open(beside);
    }

    /// <summary>Rebuilds the page from the render callback, keeping the scroll position.</summary>
    public void Refresh()
    {
        if (IsOpen) Redraw(true);
    }

    // A render callback that calls Refresh or Open (directly, or through a control it fires) would
    // otherwise recurse until the game crashes. Inside a render, a redraw is only noted, and done on
    // the next frame.
    void Redraw(bool keepScroll)
    {
        if (rendering) { pending = true; return; }
        rendering = true;
        try { Render(keepScroll); }
        finally { rendering = false; }
    }

    void Closing()
    {
        Safe.Run(Closed, $"'{Title}' closed");
    }

    internal static void Tick()
    {
        var w = Current;
        if (w == null || !w.IsOpen) { pending = false; return; }
        if (pending) { pending = false; w.Redraw(true); return; }
        if (w.AutoRefreshSeconds > 0f && Time.realtimeSinceStartup - w.lastRender >= w.AutoRefreshSeconds) w.Redraw(true);
    }

    // ------------------------------------------------------------------ drawing

    void Render(bool keepScroll)
    {
        lastRender = Time.realtimeSinceStartup;
        float scroll = Panel.Scroll != null ? Panel.Scroll.verticalNormalizedPosition : 1f;
        Panel.ClearViews();
        Panel.SetTitle(Title ?? "", Subtitle ?? "");

        float top = Panel.TitleH + 6f;
        if (tabs.Count > 1) { Tabs(); top = TabsY + TabH; }
        float left = M;

        IReadOnlyList<string> items = null;
        if (sidebar != null)
        {
            try { items = sidebar() ?? Array.Empty<string>(); }
            catch (Exception e) { Safe.Blame(sidebar, "sidebar", e); items = Array.Empty<string>(); }
            if (SidebarIndex >= items.Count) SidebarIndex = Math.Max(0, items.Count - 1);
            Sidebar(items, top);
            left = M + SideW + Gap;
        }

        // The page: a sunken frame with the game's scroll view inside it.
        var frame = Kit.Rect(Panel.Box, "V_Frame", 0, 0, 1, 1, left, M, M, top);
        Kit.Frame(frame, Kit.Sunken);
        Panel.PlaceScroll(left + Bevel, M + Bevel, M + Bevel, top + Bevel);

        var page = new Page(this, Panel.Content, Width - left - M - 2 * Bevel - 60f);
        if (tabs.Count == 0) page.Text("This window has no tabs.", muted: true);
        else
        {
            TabIndex = Math.Clamp(TabIndex, 0, tabs.Count - 1);
            var t = tabs[TabIndex];
            try { t.Render(page); }
            catch (Exception e)
            {
                Safe.Blame(t.Render, $"'{Title}' page", e);
                page.Text($"<color={Kit.Tag(Kit.Bad)}>This page failed to draw: {e.GetType().Name}. See the log.</color>");
            }
        }
        page.Finish();

        if (Panel.Scroll != null) Panel.Scroll.verticalNormalizedPosition = keepScroll ? scroll : 1f;
    }

    void Tabs()
    {
        var strip = Kit.Strip(Panel.Box, "V_Tabs", TabsY, TabH, M, M);
        float x = 0;
        for (int i = 0; i < tabs.Count; i++)
        {
            int idx = i;
            bool on = i == TabIndex;
            float w = Math.Clamp(40f + (tabs[i].Name?.Length ?? 0) * 22f, 220f, 520f);
            // The active tab is taller and joins the frame below it, as Steam's did.
            var rt = Kit.Rect(strip, "Tab" + i, 0, 0, 0, 1, 0, on ? -Bevel : 0, 0, on ? 0 : 8);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            x += w + 6;
            var b = Kit.Button(rt, tabs[i].Name ?? "", () => { TabIndex = idx; Redraw(false); }, TabText, bold: on);
            if (!on && b.Text != null) b.Text.color = Kit.Muted;
        }
    }

    void Sidebar(IReadOnlyList<string> items, float top)
    {
        var frame = Kit.Rect(Panel.Box, "V_Side", 0, 0, 0, 1, M, M, 0, top);
        frame.pivot = new Vector2(0, 0.5f);
        frame.sizeDelta = new Vector2(SideW, frame.sizeDelta.y);
        frame.anchoredPosition = new Vector2(M, frame.anchoredPosition.y);
        Kit.Frame(frame, Kit.Sunken);
        Kit.Fill(Kit.Rect(frame, "Well", 0, 0, 1, 1, Bevel, Bevel, Bevel, Bevel), Kit.Well);

        // As many rows as fit; with more items, the last row becomes [^] [v] to page through them,
        // and the selected item is kept in view.
        int fit = Math.Max(2, (int)((Height - top - M - 2 * Bevel) / SideRow));
        bool paged = items.Count > fit;
        int rows = paged ? fit - 1 : items.Count;
        if (paged)
        {
            if (SidebarIndex < sideOffset) sideOffset = SidebarIndex;
            if (SidebarIndex >= sideOffset + rows) sideOffset = SidebarIndex - rows + 1;
            sideOffset = Math.Clamp(sideOffset, 0, items.Count - rows);
        }
        else sideOffset = 0;

        float y = Bevel;
        for (int i = sideOffset; i < sideOffset + rows && i < items.Count; i++)
        {
            int idx = i;
            bool on = i == SidebarIndex;
            var row = Kit.Strip(frame, "Item" + i, y, SideRow, Bevel, Bevel);
            Kit.Flat(row, on ? Kit.Select : (i % 2 == 0 ? Kit.Well : Kit.RowAlt), () => { SidebarIndex = idx; Redraw(false); });
            Kit.Label(row, items[i] ?? "", SideText, on ? Color.white : Kit.Text, TextAlignmentOptions.MidlineLeft, 20, 12);
            y += SideRow;
        }
        if (!paged) return;

        var bar = Kit.Strip(frame, "Paging", y, SideRow, Bevel, Bevel);
        int page = rows;
        Kit.Button(Kit.Rect(bar, "Up", 0, 0, 0.3f, 1, 4, 6, 4, 6), null, () => { sideOffset = Math.Max(0, sideOffset - page); Redraw(false); }, 40, Kit.UpIcon);
        Kit.Button(Kit.Rect(bar, "Down", 0.7f, 0, 1, 1, 4, 6, 4, 6), null, () => { sideOffset = Math.Min(items.Count - page, sideOffset + page); Redraw(false); }, 40, Kit.DownIcon);
        Kit.Label(Kit.Rect(bar, "Pos", 0.3f, 0, 0.7f, 1), $"{sideOffset + 1}-{Math.Min(items.Count, sideOffset + rows)} of {items.Count}", 30, Kit.Muted, TextAlignmentOptions.Center);
    }
}
