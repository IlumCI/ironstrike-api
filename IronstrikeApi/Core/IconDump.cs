using System;
using System.IO;
using UnityEngine;

namespace IronstrikeApi.Core;

// [09 Debug] DumpIcons: writes every skill and spell icon the game ships to BepInEx/debug-shots/icons,
// as PNGs, so new icons can be drawn to match. Sprites live in GPU atlases that are not readable from
// the CPU, so each is drawn into a render texture first and read back from there.
internal static class IconDump
{
    static bool done;

    internal static void Tick()
    {
        if (done || !Plugin.C.DumpIcons.Value || SkillManager.instance == null || SpellManager.instance == null) return;
        done = true;
        string dir = Path.Combine(Shots.Folder, "icons");
        Directory.CreateDirectory(dir);
        int n = 0;
        foreach (var kv in SkillDatabase.GetSkillDict(SkillManager.instance.skillDatabase, false))
        {
            var s = kv.Value;
            if (s == null || s.skillSprite == null) continue;
            n += Save(s.skillSprite, Path.Combine(dir, $"skill_{(int)kv.Key:000}_{kv.Key}_{s.skillCategory}_{s.skillClass}.png"));
        }
        foreach (var kv in SpellDatabase.GetSpellDict(SpellManager.instance.spellDatabase, false))
        {
            var s = kv.Value;
            if (s == null) continue;
            if (s.spellIcon != null) n += Save(s.spellIcon, Path.Combine(dir, $"spell_{(int)kv.Key:00}_{kv.Key}_icon.png"));
            if (s.skillSprite != null) n += Save(s.skillSprite, Path.Combine(dir, $"spell_{(int)kv.Key:00}_{kv.Key}_skill.png"));
        }
        Plugin.Log.LogMessage($"icon dump: {n} icon(s) written to debug-shots/icons");
    }

    static int Save(Sprite sprite, string path)
    {
        RenderTexture rt = null;
        try
        {
            var tex = sprite.texture;
            var r = sprite.textureRect;
            rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var outTex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
            // Direct3D (also under Proton) puts a render texture's origin at the top left, while sprite
            // rects count from the bottom left: flip the rect, then the image, so it comes out upright.
            bool topLeft = SystemInfo.graphicsUVStartsAtTop;
            float y = topLeft ? tex.height - r.y - r.height : r.y;
            outTex.ReadPixels(new Rect(r.x, y, r.width, r.height), 0, 0);
            outTex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(outTex));
            UnityEngine.Object.Destroy(outTex);
            return 1;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"icon dump: {Path.GetFileName(path)}: {e.Message}"); return 0; }
        finally { if (rt != null) RenderTexture.ReleaseTemporary(rt); }
    }
}
