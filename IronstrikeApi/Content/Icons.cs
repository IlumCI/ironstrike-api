using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace IronstrikeApi.Content;

/// <summary>
/// An icon for custom content: one of the API's own, a PNG of yours, or one of the game's. It is a
/// description, turned into a sprite the first time the game needs it (mods register content before
/// the game has started, when no sprite can be made yet).
/// </summary>
/// <remarks>
/// <para>The game's icons are white silhouettes that the interface tints: draw yours white on a
/// transparent background and they will match. Any size and margin works: the API centres the
/// drawing on a square and gives it the same margin as the game's own icons.</para>
/// <para>The API ships a library drawn in the game's style; see <see cref="Icons.LibraryNames"/>.</para>
/// </remarks>
public sealed class Icon
{
    readonly Func<Sprite> make;
    Sprite made;
    bool tried;

    internal Icon(string description, Func<Sprite> make)
    {
        Description = description;
        this.make = make;
    }

    /// <summary>Where the icon comes from, for logs and the Mods window.</summary>
    public string Description { get; }

    /// <summary>The sprite, made on first use; null if it could not be made.</summary>
    public Sprite Sprite
    {
        get
        {
            if (made != null || tried) return made;
            tried = true;
            try { made = make(); }
            catch (Exception e) { Diag.Warn($"icon {Description}: {e.Message}"); }
            if (made != null) made.hideFlags = HideFlags.HideAndDontSave;
            return made;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Description;
}

/// <summary>Ways to get an <see cref="Icon"/>.</summary>
public static class Icons
{
    const string Prefix = "IronstrikeApi.Icons.";

    /// <summary>
    /// One of the API's own icons, drawn to match the game's: magic schools (<c>school_blood</c>,
    /// <c>school_void</c>, <c>school_tide</c>...), spells (<c>spell_blood_lance</c>...) and skills
    /// (<c>skill_bloodlust</c>...). The full list is <see cref="LibraryNames"/>.
    /// </summary>
    /// <param name="name">The icon's name.</param>
    public static Icon Library(string name)
    {
        string res = Prefix + name + ".png";
        if (typeof(Icons).Assembly.GetManifestResourceInfo(res) == null)
            throw new ArgumentException($"no library icon '{name}'; see Icons.LibraryNames", nameof(name));
        return new Icon("library:" + name, () =>
        {
            using var s = typeof(Icons).Assembly.GetManifestResourceStream(res);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return FromBytes(ms.ToArray(), name);
        });
    }

    /// <summary>The names <see cref="Library"/> accepts.</summary>
    public static IReadOnlyList<string> LibraryNames =>
        typeof(Icons).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.Ordinal))
            .Select(n => n.Substring(Prefix.Length, n.Length - Prefix.Length - 4))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>A PNG file, white on transparent. Relative paths are relative to your plugin's DLL.</summary>
    /// <param name="path">The file.</param>
    public static Icon FromFile(string path)
    {
        var caller = Assembly.GetCallingAssembly();
        return new Icon("file:" + Path.GetFileName(path), () =>
        {
            string full = Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(caller.Location) ?? "", path);
            return FromBytes(File.ReadAllBytes(full), Path.GetFileNameWithoutExtension(path));
        });
    }

    /// <summary>A PNG embedded in your own assembly as a resource.</summary>
    /// <param name="assembly">Your assembly, e.g. <c>typeof(Plugin).Assembly</c>.</param>
    /// <param name="resourceName">The full manifest resource name.</param>
    public static Icon FromResource(Assembly assembly, string resourceName) => new("resource:" + resourceName, () =>
    {
        using var s = assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException(resourceName);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return FromBytes(ms.ToArray(), resourceName);
    });

    /// <summary>The icon of one of the game's own skills.</summary>
    /// <param name="skill">The skill.</param>
    public static Icon FromSkill(SkillType skill) => new("skill:" + skill, () =>
        SkillManager.instance?.GetSkill(skill)?.skillSprite);

    /// <summary>The icon of one of the game's own spells.</summary>
    /// <param name="spell">The spell.</param>
    public static Icon FromSpell(SpellType spell) => new("spell:" + spell, () =>
        SpellManager.instance?.GetSpell(spell)?.spellIcon);

    /// <summary>A sprite you made yourself.</summary>
    /// <param name="sprite">The sprite.</param>
    public static Icon FromSprite(Sprite sprite) => new("sprite:" + (sprite != null ? sprite.name : "null"), () => sprite);

    // The game's skill icons are 256 x 256 sprites whose drawing covers about 60% of the square
    // (measured: sprite rect 256 x 256, packed area 150 to 240 px), and the card stretches the whole
    // sprite over a square box without keeping its aspect. So a PNG is placed on a square canvas,
    // centred, with its visible pixels covering this share of the side, whatever its own size.
    internal const float Coverage = 0.62f;

    static Sprite FromBytes(byte[] png, string name)
    {
        var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(src, png)) throw new InvalidDataException("not a PNG");
        var px = src.GetPixels32();
        var alpha = new byte[px.Length];
        for (int i = 0; i < px.Length; i++) alpha[i] = px[i].a;
        var (x, y, w, h) = Visible(alpha, src.width, src.height);
        int side = Canvas(w, h);
        var outPx = new Color32[side * side];
        int ox = (side - w) / 2, oy = (side - h) / 2;
        for (int row = 0; row < h; row++)
            Array.Copy(px, (y + row) * src.width + x, outPx, (oy + row) * side + ox, w);
        UnityEngine.Object.Destroy(src);

        var tex = new Texture2D(side, side, TextureFormat.RGBA32, false) { name = name };
        tex.SetPixels32(outPx);
        tex.Apply(false, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        var s = Sprite.Create(tex, new Rect(0, 0, side, side), new Vector2(0.5f, 0.5f), 100f);
        s.name = name;
        return s;
    }

    // The side of the square canvas for a drawing of w x h visible pixels.
    internal static int Canvas(int w, int h) => Math.Max(Math.Max(w, h), (int)Math.Ceiling(Math.Max(w, h) / Coverage));

    // The smallest rectangle holding every pixel more than faintly visible; the whole image if none is.
    internal static (int X, int Y, int W, int H) Visible(byte[] alpha, int width, int height, byte threshold = 16)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (alpha[y * width + x] <= threshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        return maxX < 0 ? (0, 0, width, height) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

}
