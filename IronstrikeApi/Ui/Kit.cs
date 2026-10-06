using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IronstrikeApi.Ui;

/// <summary>
/// Low-level drawing kit behind <see cref="Window"/> and <see cref="Page"/>: rects, labels, beveled
/// buttons, check boxes, fields and pixel icons in the classic Steam look. Use it from
/// <see cref="Page.Custom"/> to draw something the page builder does not have.
/// </summary>
/// <remarks>
/// <para>Everything is drawn at runtime (bevel frames are 8x8 textures with 2-texel 9-slice borders,
/// icons are 16x16 pixel art), so it needs no assets.</para>
/// <para>Objects are created on the window's UI layer (<see cref="Layer"/>): the game's raycasters,
/// the VR tracked-device raycaster included, filter by layer, and a fresh GameObject defaults to
/// layer 0, where it would never receive clicks.</para>
/// <para>Units are canvas units: the window is <see cref="Window.Width"/> x
/// <see cref="Window.Height"/>. Body text reads well at 34-40.</para>
/// </remarks>
public static class Kit
{
    /// <summary>The body font (the game's AgenorNeue), set when the window is first built.</summary>
    public static TMP_FontAsset Font;
    /// <summary>The UI layer new objects are put on.</summary>
    public static int Layer = 5;

    // Classic Steam ("VGUI") palette.
    /// <summary>Window and button face.</summary>
    public static readonly Color Face     = Hex(0x4C5844);    // window and buttons
    /// <summary>Lists and fields (sunken areas).</summary>
    public static readonly Color Well     = Hex(0x3E4637);    // lists, fields
    /// <summary>Alternate table row.</summary>
    public static readonly Color RowAlt   = Hex(0x434C3B);
    /// <summary>Bevel highlight.</summary>
    public static readonly Color Light    = Hex(0x889180);    // bevel highlight
    /// <summary>Bevel shadow.</summary>
    public static readonly Color Dark     = Hex(0x282E22);    // bevel shadow
    /// <summary>Selected row.</summary>
    public static readonly Color Select   = Hex(0x958831);    // selected row
    /// <summary>Normal text.</summary>
    public static readonly Color Text     = Hex(0xDEE5D7);
    /// <summary>Secondary text.</summary>
    public static readonly Color Muted    = Hex(0xA0AA95);
    /// <summary>Titles: the yellow Steam used.</summary>
    public static readonly Color Accent   = Hex(0xC4B550);    // the yellow Steam used for titles
    /// <summary>Positive status.</summary>
    public static readonly Color Good     = Hex(0x9BD47A);
    /// <summary>Negative status, errors.</summary>
    public static readonly Color Bad      = Hex(0xE07A5F);
    /// <summary>Fully transparent.</summary>
    public static readonly Color Clear    = new(0f, 0f, 0f, 0f);

    /// <summary>A colour from a 0xRRGGBB integer.</summary>
    /// <param name="rgb">The colour, e.g. 0x4C5844.</param>
    /// <param name="a">Alpha, 0-1.</param>
    public static Color Hex(int rgb, float a = 1f)
        => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    /// <summary>A colour as a TMP rich-text tag value, <c>#RRGGBBAA</c>: <c>$"&lt;color={Kit.Tag(Kit.Good)}&gt;ok&lt;/color&gt;"</c>.</summary>
    /// <param name="c">The colour.</param>
    // By hand: ColorUtility.ToHtmlStringRGBA is stripped from this build ("Method unstripping failed").
    public static string Tag(Color c)
    {
        static int B(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        return $"#{B(c.r):X2}{B(c.g):X2}{B(c.b):X2}{B(c.a):X2}";
    }

    // ------------------------------------------------------------------ drawn sprites

    /// <summary>Bevel frame: raised (buttons, window).</summary>
    public static Sprite Raised;
    /// <summary>Bevel frame: sunken (lists, fields, check boxes).</summary>
    public static Sprite Sunken;
    /// <summary>Bevel frame: pressed button.</summary>
    public static Sprite Pressed;
    /// <summary>16x16 icon: padlock.</summary>
    public static Sprite LockIcon;
    /// <summary>16x16 icon: star.</summary>
    public static Sprite StarIcon;
    /// <summary>16x16 icon: tick.</summary>
    public static Sprite CheckIcon;
    /// <summary>16x16 icon: up arrow.</summary>
    public static Sprite UpIcon;
    /// <summary>16x16 icon: down arrow.</summary>
    public static Sprite DownIcon;
    /// <summary>16x16 icon: close (X).</summary>
    public static Sprite CloseIcon;
    /// <summary>16x16 icon: dot (white; tint it).</summary>
    public static Sprite DotIcon;

    // Bevel borders are 2 texels; at this multiplier they come out ~6 canvas units, a crisp 2-3 px
    // line at normal viewing distance.
    const float BevelScale = 1f / 3f;

    /// <summary>Creates the frames and icons if they do not exist yet. Called for you by everything that draws.</summary>
    public static void EnsureSprites()
    {
        if (Raised != null) return;
        Raised  = Bevel(Face, Light, Dark);
        Pressed = Bevel(Hex(0x434C3B), Dark, Light);
        Sunken  = Bevel(Well, Dark, Light);

        LockIcon = Icon(new[]
        {
            "................",
            ".....######.....",
            "....##....##....",
            "....#......#....",
            "....#......#....",
            "....#......#....",
            "..############..",
            "..############..",
            "..#####..#####..",
            "..####....####..",
            "..#####..#####..",
            "..######.#####..",
            "..######.#####..",
            "..############..",
            "..############..",
            "................",
        }, Hex(0xE6D27A));
        StarIcon = Icon(new[]
        {
            ".......##.......",
            ".......##.......",
            "......####......",
            "......####......",
            ".....######.....",
            "################",
            ".##############.",
            "...##########...",
            "....########....",
            "....########....",
            "...####..####...",
            "...###....###...",
            "..###......###..",
            "..##........##..",
            ".#............#.",
            "................",
        }, Hex(0xF2D04B));
        CheckIcon = Icon(new[]
        {
            "................",
            "................",
            "..............##",
            ".............###",
            "............###.",
            "...........###..",
            ".##.......###...",
            ".###.....###....",
            "..###...###.....",
            "...###.###......",
            "....#####.......",
            ".....###........",
            "......#.........",
            "................",
            "................",
            "................",
        }, Text);
        UpIcon = Icon(new[]
        {
            "................", "................", "................", "................",
            ".......##.......", "......####......", ".....######.....", "....########....",
            "...##########...", "..############..", "................", "................",
            "................", "................", "................", "................",
        }, Text);
        DownIcon = Icon(new[]
        {
            "................", "................", "................", "................",
            "..############..", "...##########...", "....########....", ".....######.....",
            "......####......", ".......##.......", "................", "................",
            "................", "................", "................", "................",
        }, Text);
        CloseIcon = Icon(new[]
        {
            "................", "................", "..##........##..", "..###......###..",
            "...###....###...", "....###..###....", ".....######.....", "......####......",
            "......####......", ".....######.....", "....###..###....", "...###....###...",
            "..###......###..", "..##........##..", "................", "................",
        }, Text);
        DotIcon = Icon(new[]
        {
            "................", "................", "................", "................",
            "................", "......####......", ".....######.....", ".....######.....",
            ".....######.....", ".....######.....", "......####......", "................",
            "................", "................", "................", "................",
        }, Color.white);
    }

    static Texture2D NewTex(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        t.hideFlags = HideFlags.HideAndDontSave;     // survive scene loads and asset unloads
        return t;
    }

    // An 8x8 face with a 2-texel bevel: light on top/left, dark on bottom/right (or swapped for sunken).
    static Sprite Bevel(Color face, Color tl, Color br)
    {
        const int n = 8, b = 2;
        var tex = NewTex(n, n);
        var px = new Il2CppStructArray<Color>(n * n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // y = 0 is the bottom row.
                bool bottom = y < b, top = y >= n - b, left = x < b, right = x >= n - b;
                Color c = face;
                if (top || left) c = tl;
                if (bottom || right) c = br;
                if ((top && right && x - (n - b) < (n - 1 - y)) || (bottom && left && x < y)) c = tl;   // mitred corners
                px[y * n + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                              SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    /// <summary>
    /// Makes a pixel-art sprite. Each string is a row, top row first; <c>#</c> is drawn in
    /// <paramref name="c"/>, anything else is transparent. Keep the result: sprites survive scene loads.
    /// </summary>
    /// <param name="rows">The art, all rows the same length.</param>
    /// <param name="c">The colour.</param>
    public static Sprite Icon(string[] rows, Color c)
    {
        int h = rows.Length, w = rows[0].Length;
        var tex = NewTex(w, h);
        var px = new Il2CppStructArray<Color>(w * h);
        for (int r = 0; r < h; r++)
            for (int x = 0; x < w; x++)
                px[(h - 1 - r) * w + x] = rows[r][x] == '#' ? c : Clear;
        tex.SetPixels(px);
        tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    // ------------------------------------------------------------------ rects

    /// <summary>A new child rect anchored between (x0,y0) and (x1,y1) of the parent (0-1), inset by l/b/r/t units.</summary>
    /// <param name="parent">Where to put it.</param>
    /// <param name="name">GameObject name.</param>
    /// <param name="x0">Left anchor.</param>
    /// <param name="y0">Bottom anchor.</param>
    /// <param name="x1">Right anchor.</param>
    /// <param name="y1">Top anchor.</param>
    /// <param name="l">Left inset.</param>
    /// <param name="b">Bottom inset.</param>
    /// <param name="r">Right inset.</param>
    /// <param name="t">Top inset.</param>
    public static RectTransform Rect(Transform parent, string name, float x0, float y0, float x1, float y1,
                                       float l = 0, float b = 0, float r = 0, float t = 0)
    {
        var go = new GameObject(name);
        go.layer = Layer;
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(-r, -t);
        return rt;
    }

    /// <summary>A fixed-height strip <paramref name="y"/> units below the parent's top, full width less insets.</summary>
    /// <param name="parent">Where to put it.</param>
    /// <param name="name">GameObject name.</param>
    /// <param name="y">Distance from the top.</param>
    /// <param name="h">Height.</param>
    /// <param name="l">Left inset.</param>
    /// <param name="r">Right inset.</param>
    public static RectTransform Strip(Transform parent, string name, float y, float h, float l = 0, float r = 0)
    {
        var rt = Rect(parent, name, 0, 1, 1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(l, -y - h);
        rt.offsetMax = new Vector2(-r, -y);
        return rt;
    }

    /// <summary>A fixed-size box <paramref name="x"/> units from the parent's left (or right) edge, vertically centred.</summary>
    /// <param name="parent">Where to put it.</param>
    /// <param name="name">GameObject name.</param>
    /// <param name="x">Distance from the edge.</param>
    /// <param name="w">Width.</param>
    /// <param name="h">Height; negative fills the parent's height.</param>
    /// <param name="fromRight">Measure from the right edge.</param>
    public static RectTransform At(Transform parent, string name, float x, float w, float h = -1, bool fromRight = false)
    {
        var rt = Rect(parent, name, fromRight ? 1 : 0, h < 0 ? 0 : 0.5f, fromRight ? 1 : 0, h < 0 ? 1 : 0.5f);
        rt.pivot = new Vector2(fromRight ? 1 : 0, 0.5f);
        rt.sizeDelta = new Vector2(w, h < 0 ? 0 : h);
        rt.anchoredPosition = new Vector2(fromRight ? -x : x, 0);
        return rt;
    }

    /// <summary>Fills a rect with a flat colour.</summary>
    /// <param name="rt">The rect.</param>
    /// <param name="c">The colour.</param>
    /// <param name="raycast">Whether it catches clicks.</param>
    public static Image Fill(RectTransform rt, Color c, bool raycast = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    /// <summary>Draws a bevel frame (<see cref="Raised"/>, <see cref="Sunken"/>, <see cref="Pressed"/>) over a rect.</summary>
    /// <param name="rt">The rect.</param>
    /// <param name="bevel">Which frame.</param>
    /// <param name="raycast">Whether it catches clicks.</param>
    public static Image Frame(RectTransform rt, Sprite bevel, bool raycast = false)
    {
        EnsureSprites();
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = bevel;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = BevelScale;
        img.color = Color.white;
        img.raycastTarget = raycast;
        return img;
    }

    /// <summary>Centres an icon in a rect.</summary>
    /// <param name="parent">The rect.</param>
    /// <param name="icon">The sprite.</param>
    /// <param name="size">Edge length.</param>
    /// <param name="tint">Colour multiplier; white keeps the art's colours.</param>
    public static Image Glyph(RectTransform parent, Sprite icon, float size, Color? tint = null)
    {
        var rt = Rect(parent, "Icon", 0.5f, 0.5f, 0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = icon;
        img.preserveAspect = true;
        img.color = tint ?? Color.white;
        img.raycastTarget = false;
        return img;
    }

    // ------------------------------------------------------------------ text

    /// <summary>A single-line text label filling a rect. Rich text works.</summary>
    /// <param name="parent">The rect.</param>
    /// <param name="s">The text.</param>
    /// <param name="size">Font size.</param>
    /// <param name="c">Colour.</param>
    /// <param name="align">Alignment.</param>
    /// <param name="padL">Left padding.</param>
    /// <param name="padR">Right padding.</param>
    /// <param name="shrink">Shrink to fit (down to 70%) instead of only truncating.</param>
    public static TextMeshProUGUI Label(RectTransform parent, string s, float size, Color c,
                                          TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft,
                                          float padL = 0, float padR = 0, bool shrink = true)
    {
        var rt = Rect(parent, "Text", 0, 0, 1, 1, padL, 0, padR, 0);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (Font != null) t.font = Font;
        t.richText = true;
        t.raycastTarget = false;
        t.alignment = align;
        t.color = c;
        t.fontSize = size;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        if (shrink)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = size * 0.7f;
        }
        t.text = s ?? "";
        return t;
    }

    // ------------------------------------------------------------------ controls

    /// <summary>The parts of a control made by <see cref="Kit"/>.</summary>
    public sealed class Btn
    {
        /// <summary>The Unity button.</summary>
        public Button Button;
        /// <summary>Its background image.</summary>
        public Image Bg;
        /// <summary>Its label, if any.</summary>
        public TextMeshProUGUI Text;
    }

    // ColorBlock tints multiply the sprite, so the bevel art is drawn at full brightness and the
    // states darken or keep it.
    static void Tints(Button b, float normal = 0.92f, float hover = 1f, float press = 0.72f)
    {
        var cb = b.colors;
        cb.normalColor = new Color(normal, normal, normal, 1);
        cb.highlightedColor = new Color(hover, hover, hover, 1);
        cb.pressedColor = new Color(press, press, press, 1);
        cb.selectedColor = cb.normalColor;
        cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.55f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.05f;
        b.colors = cb;
    }

    /// <summary>A raised, beveled push button.</summary>
    /// <param name="rt">The rect to turn into a button.</param>
    /// <param name="text">Its label; null with an <paramref name="icon"/> for an icon button.</param>
    /// <param name="click">What it does.</param>
    /// <param name="size">Font (or icon) size.</param>
    /// <param name="icon">Optional icon.</param>
    /// <param name="bold">Bold label.</param>
    public static Btn Button(RectTransform rt, string text, Action click, float size, Sprite icon = null, bool bold = false)
    {
        var bg = Frame(rt, Raised, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        Tints(b);
        TextMeshProUGUI t = null;
        if (icon != null && string.IsNullOrEmpty(text)) Glyph(rt, icon, size);
        else if (text != null) t = Label(rt, bold ? $"<b>{text}</b>" : text, size, Text, TextAlignmentOptions.Center, 14, 14);
        Panel.Bind(b, click);
        return new Btn { Button = b, Bg = bg, Text = t };
    }

    /// <summary>A flat clickable area (table rows, header cells) that only shows on hover and press.</summary>
    /// <param name="rt">The rect.</param>
    /// <param name="fill">Resting colour.</param>
    /// <param name="click">What it does.</param>
    public static Btn Flat(RectTransform rt, Color fill, Action click)
    {
        var bg = Fill(rt, Color.white, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        var cb = b.colors;
        cb.normalColor = fill;
        cb.highlightedColor = Color.Lerp(fill, Light, 0.25f);
        cb.pressedColor = Color.Lerp(fill, Dark, 0.4f);
        cb.selectedColor = fill;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.05f;
        b.colors = cb;
        Panel.Bind(b, click);
        return new Btn { Button = b, Bg = bg };
    }

    /// <summary>A sunken check box with its label; the whole rect is clickable.</summary>
    /// <param name="rt">The rect.</param>
    /// <param name="text">The label.</param>
    /// <param name="on">Ticked.</param>
    /// <param name="click">What clicking does (usually: flip a setting, then refresh).</param>
    /// <param name="size">Font size.</param>
    public static Btn Check(RectTransform rt, string text, bool on, Action click, float size)
    {
        var hit = Fill(rt, Clear, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = hit;
        var cb = b.colors;
        cb.normalColor = Clear;
        cb.highlightedColor = new Color(1, 1, 1, 0.06f);
        cb.pressedColor = new Color(1, 1, 1, 0.12f);
        cb.selectedColor = Clear;
        b.colors = cb;

        float box = size * 1.25f;
        var frame = At(rt, "Box", 4, box, box);
        Frame(frame, Sunken);
        if (on) Glyph(frame, CheckIcon, box * 0.8f);
        var t = Label(rt, text, size, Text, TextAlignmentOptions.MidlineLeft, box + 18, 4);
        Panel.Bind(b, click);
        return new Btn { Button = b, Bg = hit, Text = t };
    }

    /// <summary>A sunken field showing a value; clicking it should edit it (see <see cref="TextInput.Ask"/>).</summary>
    /// <param name="rt">The rect.</param>
    /// <param name="value">What it shows.</param>
    /// <param name="click">What clicking does.</param>
    /// <param name="size">Font size.</param>
    public static Btn Field(RectTransform rt, string value, Action click, float size)
    {
        var bg = Frame(rt, Sunken, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        Tints(b, 0.95f, 1.1f, 0.85f);
        var t = Label(rt, value, size, Text, TextAlignmentOptions.MidlineLeft, 18, 18);
        Panel.Bind(b, click);
        return new Btn { Button = b, Bg = bg, Text = t };
    }

    /// <summary>A <c>[&lt;] value [&gt;]</c> control.</summary>
    /// <param name="rt">The rect.</param>
    /// <param name="value">What it shows.</param>
    /// <param name="dec">The left button.</param>
    /// <param name="inc">The right button.</param>
    /// <param name="size">Font size.</param>
    public static void Spinner(RectTransform rt, string value, Action dec, Action inc, float size)
    {
        float h = rt.rect.height > 1 ? rt.rect.height : size * 1.6f;
        var minus = At(rt, "Dec", 0, h);
        Button(minus, "<", dec, size, bold: true);
        var plus = At(rt, "Inc", 0, h, fromRight: true);
        Button(plus, ">", inc, size, bold: true);
        var val = Rect(rt, "Val", 0, 0, 1, 1, h + 6, 0, h + 6, 0);
        Frame(val, Sunken);
        Label(val, value, size, Text, TextAlignmentOptions.Center);
    }
}
