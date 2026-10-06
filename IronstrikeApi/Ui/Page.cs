using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace IronstrikeApi.Ui;

/// <summary>A table column for <see cref="Page.Table"/>.</summary>
public sealed class TableColumn
{
    /// <summary>Header text.</summary>
    public string Name { get; }
    /// <summary>Relative width: a column with weight 2 is twice as wide as one with 1.</summary>
    public float Weight { get; }

    /// <summary>Makes a column.</summary>
    /// <param name="name">Header text.</param>
    /// <param name="weight">Relative width.</param>
    public TableColumn(string name, float weight = 1f) { Name = name; Weight = weight <= 0 ? 1f : weight; }
}

/// <summary>
/// Builds one page of a <see cref="Window"/>, top to bottom. Each call adds a row; settings rows have
/// their name on the left and the control on the right. Using a control calls your callback, then
/// redraws the page.
/// </summary>
/// <remarks>You get a <c>Page</c> in your tab's render callback. Do not keep it.</remarks>
public sealed class Page
{
    /// <summary>Height of a settings row, in canvas units.</summary>
    public const float RowHeight = 84f;

    const float T = 38f, Small = 32f, Pad = 24f, LabelShare = 0.42f;

    readonly Window window;
    readonly RectTransform content;
    float y = 16f;

    internal Page(Window w, RectTransform content, float width)
    {
        window = w;
        this.content = content;
        Width = width;
    }

    /// <summary>The usable width of the page.</summary>
    public float Width { get; }

    /// <summary>The window this page belongs to.</summary>
    public Window Window => window;

    // ------------------------------------------------------------------ text

    /// <summary>A section heading in the title colour.</summary>
    /// <param name="text">The heading.</param>
    public void Header(string text)
    {
        if (y > 20f) y += 18f;
        var r = Row(70f);
        Kit.Label(r, text, 44f, Kit.Accent, TextAlignmentOptions.BottomLeft, 0, 0);
        var line = Kit.Rect(r, "Rule", 0, 0, 1, 0, 0, -6, 0, 0);
        line.sizeDelta = new Vector2(line.sizeDelta.x, 3f);
        Kit.Fill(line, Kit.Dark);
        y += 14f;
    }

    /// <summary>A block of text. Rich text works. Long text wraps onto up to <paramref name="lines"/> lines.</summary>
    /// <param name="text">The text.</param>
    /// <param name="muted">Secondary colour.</param>
    /// <param name="lines">How many lines to make room for.</param>
    public void Text(string text, bool muted = false, int lines = 1)
    {
        lines = Math.Max(1, lines);
        var r = Row(lines * 50f + 10f);
        var t = Kit.Label(r, text, T, muted ? Kit.Muted : Kit.Text, lines > 1 ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft, 0, 0, shrink: false);
        if (lines > 1) { t.enableWordWrapping = true; t.overflowMode = TextOverflowModes.Ellipsis; }
    }

    /// <summary>A read-only setting: name on the left, value on the right.</summary>
    /// <param name="label">The name.</param>
    /// <param name="value">The value.</param>
    public void Info(string label, string value)
    {
        var (l, c) = Split(RowHeight, label, null);
        Kit.Label(c, value, T, Kit.Text, TextAlignmentOptions.MidlineLeft, 18, 0);
    }

    /// <summary>Empty space.</summary>
    /// <param name="height">How much, in canvas units.</param>
    public void Gap(float height = 24f) => y += Math.Max(0f, height);

    // ------------------------------------------------------------------ controls

    /// <summary>A push button. With a <paramref name="label"/>, it goes in the control column.</summary>
    /// <param name="text">The button's text.</param>
    /// <param name="onClick">What it does.</param>
    /// <param name="label">Optional name on the left.</param>
    public void Button(string text, Action onClick, string label = null)
    {
        RectTransform c;
        if (label != null) (_, c) = Split(RowHeight, label, null);
        else c = Row(RowHeight);
        var b = Kit.Rect(c, "Button", 0, 0, label != null ? 1 : 0, 1, label != null ? 0 : 0, 6, 0, 6);
        if (label == null) { b.pivot = new Vector2(0, 0.5f); b.sizeDelta = new Vector2(Math.Max(360f, 60f + text.Length * 22f), b.sizeDelta.y); }
        Kit.Button(b, text, Wrap(onClick), T);
    }

    /// <summary>A row of buttons sharing the width equally.</summary>
    /// <param name="buttons">Text and action of each button.</param>
    public void Buttons(params (string Text, Action OnClick)[] buttons)
    {
        if (buttons == null || buttons.Length == 0) return;
        var r = Row(RowHeight);
        float w = 1f / buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = Kit.Rect(r, "Button" + i, i * w, 0, (i + 1) * w, 1, i == 0 ? 0 : 8, 6, i == buttons.Length - 1 ? 0 : 8, 6);
            Kit.Button(b, buttons[i].Text, Wrap(buttons[i].OnClick), T);
        }
    }

    /// <summary>A check box.</summary>
    /// <param name="label">The setting's name.</param>
    /// <param name="value">Whether it is ticked.</param>
    /// <param name="onChange">Gets the new value.</param>
    /// <param name="help">Optional explanation, shown under the name.</param>
    public void Toggle(string label, bool value, Action<bool> onChange, string help = null)
    {
        var (_, c) = Split(help != null ? 120f : RowHeight, label, help);
        var box = Kit.Rect(c, "Check", 0, 0, 1, 1, 0, 0, 0, 0);
        Kit.Check(box, value ? "On" : "Off", value, Wrap(() => onChange?.Invoke(!value), onChange), T);
    }

    /// <summary>A <c>[&lt;] value [&gt;]</c> stepper for a number.</summary>
    /// <param name="label">The setting's name.</param>
    /// <param name="value">The current value.</param>
    /// <param name="step">How much one press changes it.</param>
    /// <param name="min">Smallest value.</param>
    /// <param name="max">Largest value.</param>
    /// <param name="onChange">Gets the new value.</param>
    /// <param name="format">How to show it (a .NET number format).</param>
    /// <param name="help">Optional explanation, shown under the name.</param>
    public void Stepper(string label, float value, float step, float min, float max, Action<float> onChange,
                        string format = "0.##", string help = null)
    {
        float Clamp(float v) => (float)Math.Round(Math.Clamp(v, min, max), 4);
        var (_, c) = Split(help != null ? 120f : RowHeight, label, help);
        var s = Kit.Rect(c, "Stepper", 0, 0, 0, 1, 0, 6, 0, 6);
        s.pivot = new Vector2(0, 0.5f);
        s.sizeDelta = new Vector2(Math.Min(560f, c.rect.width > 1 ? c.rect.width : 560f), s.sizeDelta.y);
        Kit.Spinner(s, value.ToString(format, CultureInfo.InvariantCulture),
                    Wrap(() => onChange?.Invoke(Clamp(value - step)), onChange),
                    Wrap(() => onChange?.Invoke(Clamp(value + step)), onChange), T);
    }

    /// <summary>A <c>[&lt;] option [&gt;]</c> picker that cycles through a list.</summary>
    /// <param name="label">The setting's name.</param>
    /// <param name="options">The choices.</param>
    /// <param name="index">The current choice.</param>
    /// <param name="onChange">Gets the new index.</param>
    /// <param name="help">Optional explanation, shown under the name.</param>
    public void Choice(string label, IReadOnlyList<string> options, int index, Action<int> onChange, string help = null)
    {
        int n = options?.Count ?? 0;
        var (_, c) = Split(help != null ? 120f : RowHeight, label, help);
        var s = Kit.Rect(c, "Choice", 0, 0, 0, 1, 0, 6, 0, 6);
        s.pivot = new Vector2(0, 0.5f);
        s.sizeDelta = new Vector2(760f, s.sizeDelta.y);
        string shown = n == 0 ? "-" : options[((index % n) + n) % n];
        Kit.Spinner(s, shown,
                    Wrap(() => { if (n > 0) onChange?.Invoke(((index - 1) % n + n) % n); }, onChange),
                    Wrap(() => { if (n > 0) onChange?.Invoke((index + 1) % n); }, onChange), T);
    }

    /// <summary>A text field. Clicking it opens the game's keyboard (<see cref="TextInput"/>).</summary>
    /// <param name="label">The setting's name.</param>
    /// <param name="value">The current text.</param>
    /// <param name="maxLength">The most characters allowed.</param>
    /// <param name="onChange">Gets the new text.</param>
    /// <param name="secret">Show dots instead of the text (passwords, codes), in case the player is streaming.</param>
    /// <param name="help">Optional explanation, shown under the name.</param>
    public void TextField(string label, string value, int maxLength, Action<string> onChange, bool secret = false, string help = null)
    {
        var (_, c) = Split(help != null ? 120f : RowHeight, label, help);
        var f = Kit.Rect(c, "Field", 0, 0, 1, 1, 0, 6, 0, 6);
        string shown = string.IsNullOrEmpty(value) ? $"<color={Kit.Tag(Kit.Muted)}>(empty)</color>"
                     : secret ? new string('*', Math.Min(value.Length, 12)) : value;
        Kit.Field(f, shown, Wrap(() => TextInput.Ask(label, maxLength, s => onChange?.Invoke(s)), onChange), T);
    }

    /// <summary>
    /// A table, like the server list. Rows are clickable when <paramref name="onClick"/> is given.
    /// Cells may use rich text.
    /// </summary>
    /// <param name="columns">The columns.</param>
    /// <param name="rows">One string per column, per row.</param>
    /// <param name="selected">Index of the highlighted row, or -1.</param>
    /// <param name="onClick">Gets the clicked row's index.</param>
    public void Table(IReadOnlyList<TableColumn> columns, IReadOnlyList<string[]> rows, int selected = -1, Action<int> onClick = null)
    {
        if (columns == null || columns.Count == 0) return;
        float total = 0; foreach (var c in columns) total += c.Weight;
        const float H = 62f;

        var head = Row(H);
        Kit.Frame(head, Kit.Raised);
        float x = 0;
        foreach (var c in columns)
        {
            float w = c.Weight / total;
            Kit.Label(Kit.Rect(head, "H", x, 0, x + w, 1), c.Name, Small, Kit.Text, TextAlignmentOptions.MidlineLeft, 14, 6);
            x += w;
        }

        rows ??= Array.Empty<string[]>();
        for (int i = 0; i < rows.Count; i++)
        {
            int idx = i;
            var r = Row(H);
            bool on = i == selected;
            Color fill = on ? Kit.Select : (i % 2 == 0 ? Kit.Well : Kit.RowAlt);
            if (onClick != null) Kit.Flat(r, fill, Wrap(() => onClick(idx), onClick));
            else Kit.Fill(r, fill);
            x = 0;
            for (int ci = 0; ci < columns.Count; ci++)
            {
                float w = columns[ci].Weight / total;
                string cell = rows[i] != null && ci < rows[i].Length ? rows[i][ci] : "";
                Kit.Label(Kit.Rect(r, "C", x, 0, x + w, 1), cell, Small, on ? Color.white : Kit.Text, TextAlignmentOptions.MidlineLeft, 14, 6, shrink: false);
                x += w;
            }
        }
        if (rows.Count == 0) Text("(nothing here)", muted: true);
        y += 10f;
    }

    /// <summary>
    /// Space of your own to draw in with <see cref="Kit"/>, full width and <paramref name="height"/>
    /// tall. Name anything you create in it however you like; it is cleared on the next redraw.
    /// </summary>
    /// <param name="height">How tall, in canvas units.</param>
    public RectTransform Custom(float height) => Row(height);

    // ------------------------------------------------------------------ layout

    RectTransform Row(float h)
    {
        var r = Kit.Strip(content, "Row", y, h, Pad, Pad);
        y += h + 6f;
        return r;
    }

    // Name (and help) on the left, the control's rect on the right.
    (RectTransform Label, RectTransform Control) Split(float h, string label, string help)
    {
        var r = Row(h);
        var left = Kit.Rect(r, "Name", 0, 0, LabelShare, 1, 0, 0, 20, 0);
        if (help == null) Kit.Label(left, label, T, Kit.Text);
        else
        {
            Kit.Label(Kit.Rect(left, "Title", 0, 0.45f, 1, 1), label, T, Kit.Text, TextAlignmentOptions.BottomLeft);
            Kit.Label(Kit.Rect(left, "Help", 0, 0, 1, 0.45f), help, Small * 0.9f, Kit.Muted, TextAlignmentOptions.TopLeft);
        }
        var control = Kit.Rect(r, "Control", LabelShare, 0, 1, 1, 0, help != null ? 18 : 0, 0, help != null ? 18 : 0);
        return (left, control);
    }

    // Controls redraw the page after their callback; a throwing callback is blamed on its mod.
    Action Wrap(Action a, Delegate blame = null) => () =>
    {
        try { a?.Invoke(); }
        catch (Exception e) { Safe.Blame(blame ?? a, "control", e); }
        window.Refresh();
    };

    internal void Finish() => content.sizeDelta = new Vector2(content.sizeDelta.x, Math.Max(y + 20f, 10f));
}
