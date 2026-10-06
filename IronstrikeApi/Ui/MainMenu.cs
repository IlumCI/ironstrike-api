using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IronstrikeApi.Ui;

/// <summary>The main menu's cards, which mods can hang small buttons on.</summary>
public enum MenuCard
{
    /// <summary>The Options card.</summary>
    Options,
    /// <summary>The Credits card.</summary>
    Credits,
    /// <summary>The Private Match card.</summary>
    PrivateMatch,
    /// <summary>The Solo card.</summary>
    Solo,
}

/// <summary>
/// Buttons on the game's main menu. The menu is a grid of cards with no free cell, so a mod's
/// button is a small pill (a copy of the game's own HOST pill) in a card's top-right corner; more
/// pills on the same card line up to the left.
/// </summary>
public static class MainMenu
{
    sealed class Pill
    {
        public string Label;
        public MenuCard Card;
        public Action Click;
        public GameObject Go;
        public TextMeshProUGUI Text;
    }

    static readonly List<Pill> pills = new();
    static MainMenuUI menu;
    static bool warned;

    /// <summary>
    /// Adds a pill button to a main menu card. Call once, from your plugin's <c>Load()</c>; the
    /// button is recreated whenever the menu is. Labels are short: 4-8 capital letters.
    /// </summary>
    /// <param name="label">The text, e.g. <c>"MYMOD"</c>.</param>
    /// <param name="onClick">What it does. <see cref="MenuAnchor"/> is where to open a window.</param>
    /// <param name="card">Which card it goes on.</param>
    public static void AddButton(string label, Action onClick, MenuCard card = MenuCard.Credits)
    {
        if (onClick == null) throw new ArgumentNullException(nameof(onClick));
        pills.Add(new Pill { Label = label ?? "", Click = onClick, Card = card });
    }

    /// <summary>The main menu's canvas, for opening a window over it; null when there is no menu.</summary>
    public static Transform MenuAnchor
    {
        get
        {
            var mm = Find();
            if (mm == null) return null;
            var canvas = mm.GetComponentInChildren<Canvas>(true);
            return canvas != null ? canvas.transform : mm.transform;
        }
    }

    internal static void Tick()
    {
        if (pills.Count == 0) return;
        var mm = Find();
        if (mm == null) return;
        if (menu == null || menu.Pointer != mm.Pointer)
        {
            menu = mm;                      // new scene, new menu
            foreach (var p in pills) { p.Go = null; p.Text = null; }
        }

        var slots = new Dictionary<MenuCard, int>();
        foreach (var p in pills)
        {
            slots.TryGetValue(p.Card, out int slot);
            slots[p.Card] = slot + 1;
            if (p.Go == null) Make(mm, p, slot);
            else if (p.Text != null && p.Text.text != p.Label) p.Text.text = p.Label;   // I2 localisation
        }
    }

    static string Method(MenuCard c) => c switch
    {
        MenuCard.Options => "PressOptions",
        MenuCard.Credits => "PressCredits",
        MenuCard.PrivateMatch => "PressPrivateMatch",
        _ => "PressSolo",
    };

    static void Make(MainMenuUI mm, Pill p, int slot)
    {
        Button card = Wired(mm, Method(p.Card), false);
        // Ironstrike Servers rebinds HOST, after which it is no longer wired to PressHost.
        Button host = Wired(mm, "PressHost", true) ?? Wired(mm, "PressHost", false) ?? Named(mm, "HostButton");
        if (card == null || host == null)
        {
            if (!warned) Plugin.Log.LogWarning("main menu: could not find the cards or a HOST pill to copy");
            warned = true;
            p.Go = new GameObject("ModPillUnavailable");   // stop retrying until the next menu
            return;
        }

        var go = UnityEngine.Object.Instantiate(host.gameObject, card.transform);
        go.name = "ModPill_" + p.Label;
        Panel.StripBehaviour(go);
        var cg = go.GetComponent<CanvasGroup>();        // undo any lockout greying on the HOST we copied
        if (cg != null) { cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }
        var btn = go.GetComponent<Button>();
        btn.interactable = true;

        var rt = go.GetComponent<RectTransform>();
        CopyCornerInset(host.GetComponent<RectTransform>(), host.transform.parent.GetComponent<RectTransform>(), rt);
        // The trainer's TRAINER pill takes the Options card's corner; start one slot left of it there.
        int s = slot + (p.Card == MenuCard.Options && Mods.IsLoaded("eu.euroswarms.ironstrike.trainer") ? 1 : 0)
                     + (p.Card == MenuCard.PrivateMatch && Mods.IsLoaded("eu.euroswarms.ironstrike.servers") ? 1 : 0);
        rt.anchoredPosition -= new Vector2(s * (rt.sizeDelta.x + 16f), 0f);

        p.Text = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (p.Text != null)
        {
            Unlocalize(p.Text);
            p.Text.text = p.Label;
            Panel.Fit(p.Text);
        }
        var click = p.Click;
        Panel.BindPermanent(btn, click);
        p.Go = go;
        Plugin.Log.LogInfo($"main menu: {p.Label} pill added to the {p.Card} card for {Mods.Owner(p.Click.Method.DeclaringType)?.Name ?? "?"}");
    }

    // Same inset from the card's top-right corner as the HOST pill has in its own card, measured in
    // world space so it does not matter how HOST was anchored.
    static void CopyCornerInset(RectTransform src, RectTransform srcCard, RectTransform dst)
    {
        var a = new Il2CppStructArray<Vector3>(4); src.GetWorldCorners(a);
        var c = new Il2CppStructArray<Vector3>(4); srcCard.GetWorldCorners(c);
        Vector3 inset = srcCard.InverseTransformPoint(a[2]) - srcCard.InverseTransformPoint(c[2]);   // 2 = top-right

        dst.anchorMin = dst.anchorMax = new Vector2(1f, 1f);
        dst.pivot = new Vector2(1f, 1f);
        dst.sizeDelta = src.rect.size;
        dst.anchoredPosition = new Vector2(inset.x, inset.y);
    }

    // ------------------------------------------------------------------ helpers shared with Safety

    internal static MainMenuUI Find()
    {
        foreach (var m in Resources.FindObjectsOfTypeAll<MainMenuUI>())
            if (m != null && m.gameObject.scene.IsValid()) return m;   // skip the prefab asset
        return null;
    }

    static Button Wired(MainMenuUI mm, string method, bool visibleOnly)
    {
        foreach (var b in mm.GetComponentsInChildren<Button>(true))
        {
            if (visibleOnly && !b.gameObject.activeInHierarchy) continue;
            if (b.name.StartsWith("ModPill_") || b.name == "TrainerButton" || b.name == "ServersButton") continue;
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                if (b.onClick.GetPersistentMethodName(i) == method) return b;
        }
        return null;
    }

    static Button Named(MainMenuUI mm, string name)
    {
        foreach (var b in mm.GetComponentsInChildren<Button>(true))
            if (b.name == name && b.gameObject.activeInHierarchy) return b;
        foreach (var b in mm.GetComponentsInChildren<Button>(true))
            if (b.name == name) return b;
        return null;
    }

    // Labels belonging to this button, not to a button nested inside it (Play contains HOST).
    internal static List<TextMeshProUGUI> OwnLabels(Button b)
    {
        var list = new List<TextMeshProUGUI>();
        foreach (var t in b.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            // Walked by hand: GetComponentInParent<T>(bool) fails an Il2CppInterop constraint check.
            Button owner = null;
            for (var p = t.transform; p != null && owner == null; p = p.parent) owner = p.GetComponent<Button>();
            if (owner != null && owner.Pointer == b.Pointer) list.Add(t);
        }
        return list;
    }

    // I2's Localize component rewrites the text; it has to go before a label can be changed.
    internal static void Unlocalize(TextMeshProUGUI t)
    {
        foreach (var c in t.GetComponents<Component>())
            if (c != null && c.GetIl2CppType().Name == "Localize") UnityEngine.Object.Destroy(c);
    }
}
