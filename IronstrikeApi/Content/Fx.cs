using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace IronstrikeApi.Content;

// The engine behind SpellFx: effects built from the game's own materials and textures (its lightning
// line, glows, sparks, smoke, flares; found by name), a few shared world-space particle systems fed
// particle by particle with ParticleSystem.Emit, line renderers, point lights, and a per-frame list of
// running effects (fades, flicker, following fighters). Everything is local to this machine and
// cleared when a session ends.
internal static class Fx
{
    // ------------------------------------------------------------------ assets

    static readonly Dictionary<string, Texture2D> textures = new();
    static readonly Dictionary<string, Material> gameMaterials = new();
    static bool scanned;

    static void Scan()
    {
        if (scanned) return;
        scanned = true;
        foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
            if (t != null && !string.IsNullOrEmpty(t.name) && !textures.ContainsKey(t.name)) textures[t.name] = t;
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            if (m != null && !string.IsNullOrEmpty(m.name) && !gameMaterials.ContainsKey(m.name)) gameMaterials[m.name] = m;
        Diag.Info($"spell effects: {textures.Count} textures and {gameMaterials.Count} materials available; " +
                  $"lightning {(gameMaterials.ContainsKey("LightningLineMat") ? "yes" : "no")}, glow {(textures.ContainsKey("Glow1") ? "yes" : "no")}");
    }

    internal static Texture2D Tex(string name)
    {
        Scan();
        if (name == "ApiBand") return Band();
        return textures.TryGetValue(name, out var t) && t != null ? t : SoftDot();
    }

    static Texture2D softDot;

    // A soft round dot, for when the game's textures cannot be found.
    static Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int n = 64;
        softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
            }
        softDot.SetPixels32(px);
        softDot.Apply();
        return softDot;
    }

    static Texture2D band;

    // A soft band: bright along the middle of a line, fading to nothing at its edges.
    internal static Texture2D Band()
    {
        if (band != null) return band;
        const int w = 4, h = 64;
        band = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, name = "ApiBand" };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h * 2 - 1;
            float a = Mathf.Pow(Mathf.Clamp01(1 - Mathf.Abs(v)), 1.6f);
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        band.SetPixels32(px);
        band.Apply();
        textures["ApiBand"] = band;
        return band;
    }

    static readonly Dictionary<string, Material> made = new();

    // An additive (glowing) or alpha-blended material with a game texture.
    internal static Material Mat(string texture, bool additive)
    {
        string key = texture + (additive ? "+" : "~");
        if (made.TryGetValue(key, out var m) && m != null) return m;
        var sh = Shader.Find(additive ? "Legacy Shaders/Particles/Additive" : "Legacy Shaders/Particles/Alpha Blended")
                 ?? Shader.Find(additive ? "Mobile/Particles/Additive" : "Sprites/Default")
                 ?? Shader.Find("Sprites/Default");
        m = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, mainTexture = Tex(texture) };
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        made[key] = m;
        return m;
    }

    // One of the game's own materials (its lightning line, heal and poison beams), copied.
    internal static Material GameMat(string name, string fallbackTexture)
    {
        Scan();
        string key = "game:" + name;
        if (made.TryGetValue(key, out var m) && m != null) return m;
        m = gameMaterials.TryGetValue(name, out var g) && g != null ? new Material(g) { hideFlags = HideFlags.HideAndDontSave } : Mat(fallbackTexture, true);
        made[key] = m;
        return m;
    }

    // ------------------------------------------------------------------ particles

    internal enum Kind { Glow, Spark, Smoke, Flare, Bubble, Ember, Mote, Dust }

    static GameObject root;
    static readonly Dictionary<Kind, ParticleSystem> systems = new();

    static Transform Root()
    {
        if (root != null) return root.transform;
        root = new GameObject("IronstrikeApiFx");
        UnityEngine.Object.DontDestroyOnLoad(root);
        root.hideFlags = HideFlags.HideAndDontSave;
        return root.transform;
    }

    // The game's build strips Unity's colour- and size-over-lifetime modules, so particles are aged by
    // hand: every frame each system's live particles are read, given the alpha and size their age calls
    // for, and written back. A particle's starting size and alpha are kept in m_AxisOfRotation, which
    // only 3D-rotated particles use.
    static readonly Dictionary<Kind, Il2CppStructArray<ParticleSystem.Particle>> buffers = new();
    const int MaxParticles = 1000;
    static int dropped;

    static ParticleSystem System(Kind k)
    {
        if (systems.TryGetValue(k, out var ps) && ps != null) return ps;
        var go = new GameObject("Fx" + k);
        go.transform.SetParent(Root(), false);
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Max particles and gravity live in setters the game's build stripped; the default cap is 1000 and
        // falling sparks are aimed down by hand instead.
        try { ps.gravityModifier = k switch { Kind.Spark => 0.9f, Kind.Ember => -0.08f, Kind.Dust => 0.25f, _ => 0f }; } catch (Exception) { }
        var em = ps.emission;
        em.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = k switch
        {
            Kind.Glow => Mat("Glow1", true),
            Kind.Spark => Mat("Spark4", true),
            Kind.Smoke => Mat("Smoke19", false),
            Kind.Flare => Mat("Flare29-5", true),
            Kind.Bubble => Mat("PolygonParticles_Bubble", true),
            Kind.Ember => Mat("Glow2", true),
            Kind.Mote => Mat("PolygonParticles_Sparkle", true),
            Kind.Dust => Mat("PolygonParticles_Smoke_01", false),
            _ => Mat("Glow1", true),
        };
        if (k == Kind.Spark)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 1.5f;
        }
        r.maxParticleSize = 8f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        systems[k] = ps;
        buffers[k] = new Il2CppStructArray<ParticleSystem.Particle>(MaxParticles);
        return ps;
    }

    internal static void Emit(Kind k, Vector3 pos, Vector3 vel, float size, float life, Color c)
    {
        try
        {
            var p = new ParticleSystem.Particle();
            p.position = pos;
            p.velocity = vel;
            p.startSize = size;
            p.startLifetime = life;
            p.remainingLifetime = life;
            p.startColor = (Color32)c;
            p.randomSeed = (uint)rnd.Next();
            p.rotation3D = new Vector3(0f, 0f, R(0f, 360f));
            p.angularVelocity3D = new Vector3(0f, 0f, k == Kind.Smoke || k == Kind.Dust ? R(-40f, 40f) : 0f);
            p.m_AxisOfRotation = new Vector3(size, Mathf.Clamp01(c.a), 1f);
            var ps = System(k);
            // Never emit into a full system: doing so killed the runtime outright (verified: Raise Dead's
            // rings and pillars together, over a thousand motes a second, crashed the game).
            if (ps.particleCount >= MaxParticles - 50) { dropped++; return; }
            ps.Emit(p);
        }
        catch (Exception e) { ApiLog.WarnOnce(null, "fx:emit", $"spell effects: particles unavailable ({e.Message})"); }
    }

    static void Age()
    {
        foreach (var kv in systems)
        {
            var ps = kv.Value;
            if (ps == null || ps.particleCount == 0) continue;
            var buf = buffers[kv.Key];
            int n = ps.GetParticles(buf, MaxParticles);
            for (int i = 0; i < n; i++)
            {
                var p = buf[i];
                var stash = p.m_AxisOfRotation;
                if (stash.z != 1f) continue;                  // not one of ours
                float x = p.startLifetime > 0 ? Mathf.Clamp01(p.remainingLifetime / p.startLifetime) : 0f;   // 1 at birth, 0 at death
                float alpha, scale;
                switch (kv.Key)
                {
                    case Kind.Smoke:
                    case Kind.Dust: alpha = Mathf.Min(1f, (1f - x) / 0.15f) * x; scale = 0.5f + 1.1f * (1f - x); break;
                    case Kind.Flare: alpha = x * x; scale = 1f + 0.4f * (1f - x); break;
                    case Kind.Bubble: alpha = Mathf.Min(1f, x * 2f); scale = 0.6f + 0.4f * Mathf.Min(1f, (1f - x) * 5f); break;
                    default: alpha = Mathf.Min(1f, x * 1.6f); scale = 0.2f + 0.8f * x; break;
                }
                var c = p.m_StartColor;
                c.a = (byte)(Mathf.Clamp01(stash.y * alpha) * 255f);
                p.startColor = c;
                p.startSize = stash.x * scale;
                buf[i] = p;
            }
            ps.SetParticles(buf, n);
        }
    }

    static readonly System.Random rnd = new();
    internal static float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
    internal static Vector3 InSphere() { var v = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1)); return v.sqrMagnitude > 1 ? v.normalized : v; }
    internal static Vector3 OnSphere() { var v = InSphere(); return v.sqrMagnitude < 1e-4f ? Vector3.up : v.normalized; }

    // ------------------------------------------------------------------ lines

    internal static LineRenderer Line(Material m, Color c, float width, int points)
    {
        var go = new GameObject("FxLine");
        go.transform.SetParent(Root(), false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = m;
        lr.useWorldSpace = true;
        lr.positionCount = points;
        lr.startWidth = lr.endWidth = width;
        lr.startColor = lr.endColor = c;
        lr.numCapVertices = 4;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    internal static void Jag(LineRenderer lr, Vector3 a, Vector3 b, float jag, int seed)
    {
        int n = lr.positionCount;
        var r = new System.Random(seed);
        var dir = b - a;
        var side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.right);
        side.Normalize();
        var up = Vector3.Cross(side, dir).normalized;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            var p = Vector3.Lerp(a, b, t);
            if (i > 0 && i < n - 1)
            {
                float env = Mathf.Sin(t * Mathf.PI);       // straight at both ends
                p += (side * (float)(r.NextDouble() * 2 - 1) + up * (float)(r.NextDouble() * 2 - 1)) * jag * env;
            }
            lr.SetPosition(i, p);
        }
    }

    // Lightning's shape: a few large bends with fine crackle on top (two scales of noise), straight
    // at both ends so it leaves the wand and meets its target cleanly.
    internal static void Crackle(LineRenderer lr, Vector3 a, Vector3 b, float jag, int seed)
    {
        int n = lr.positionCount;
        var r = new System.Random(seed);
        var dir = b - a;
        var side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.right);
        side.Normalize();
        var up = Vector3.Cross(side, dir).normalized;
        // Large bends: a handful of control offsets, smoothly interpolated.
        const int k = 5;
        var big = new Vector2[k + 1];
        for (int i = 0; i <= k; i++) big[i] = new Vector2((float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1));
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            float env = Mathf.Sin(t * Mathf.PI);
            float u = t * k;
            int j = Mathf.Min((int)u, k - 1);
            float f = u - j;
            f = f * f * (3 - 2 * f);
            var bend = Vector2.Lerp(big[j], big[j + 1], f) * jag * 2.2f;
            var fine = new Vector2((float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1)) * jag * 0.45f;
            var o = (bend + fine) * env;
            lr.SetPosition(i, Vector3.Lerp(a, b, t) + side * o.x + up * o.y);
        }
    }

    // ------------------------------------------------------------------ lights

    internal static Light PointLight(Vector3 at, Color c, float intensity, float range)
    {
        var go = new GameObject("FxLight");
        go.transform.SetParent(Root(), false);
        go.transform.position = at;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }

    // ------------------------------------------------------------------ the running effects

    sealed class Running { public Func<float, float, bool> Step; public float Start; public List<GameObject> Owned; }
    static readonly List<Running> running = new();

    // step(t, age) runs every frame until it returns false or t (0..1 of length) passes 1; the
    // objects listed are destroyed at the end.
    internal static void Run(float length, Func<float, float, bool> step, params GameObject[] owned)
    {
        if (running.Count > 600) { foreach (var g in owned) if (g != null) UnityEngine.Object.Destroy(g); return; }
        running.Add(new Running { Start = Time.time, Owned = owned.ToList(), Step = (t, age) => t <= 1f && step(Mathf.Clamp01(t), age) });
        lengths.Add(Mathf.Max(0.01f, length));
    }

    static readonly List<float> lengths = new();
    static bool? noAge;

    internal static void Tick()
    {
        if (noAge == null) noAge = global::System.IO.File.Exists(global::System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "fx-no-age"));
        if (noAge == false) try { Age(); } catch (Exception e) { ApiLog.WarnOnce(null, "fx:age", $"spell effects: particle ageing failed ({e.Message})"); }
        if (running.Count == 0) return;
        float now = Time.time;
        for (int i = running.Count - 1; i >= 0; i--)
        {
            var r = running[i];
            float age = now - r.Start;
            bool keep;
            try { keep = r.Step(age / lengths[i], age); }
            catch (Exception) { keep = false; }
            if (keep) continue;
            foreach (var g in r.Owned) if (g != null) UnityEngine.Object.Destroy(g);
            running.RemoveAt(i);
            lengths.RemoveAt(i);
        }
    }

    internal static void Clear()
    {
        foreach (var r in running) foreach (var g in r.Owned) if (g != null) UnityEngine.Object.Destroy(g);
        running.Clear();
        lengths.Clear();
        foreach (var ps in systems.Values) if (ps != null) ps.Clear(true);
    }

    // ------------------------------------------------------------------ the game's own effect prefabs

    static readonly Dictionary<string, GameObject> prefabs = new();

    // A copy of one of the game's effect prefabs with everything but its visuals removed.
    internal static GameObject GamePrefab(string name, Vector3 at, Quaternion rot, float scale, float seconds)
    {
        if (!prefabs.TryGetValue(name, out var src) || src == null)
        {
            src = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g != null && g.name == name && !g.scene.IsValid() && g.transform.parent == null);
            prefabs[name] = src;
        }
        if (src == null) return null;
        // Copied inactive, so none of its gameplay scripts wake up before they are removed.
        bool was = src.activeSelf;
        src.SetActive(false);
        GameObject go;
        try { go = UnityEngine.Object.Instantiate(src, at, rot); }
        finally { src.SetActive(was); }
        go.transform.SetParent(Root(), true);
        go.transform.localScale *= scale;
        var doomed = new List<Component>();
        foreach (var c in go.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            string n = c.GetIl2CppType().Name;
            if (n is "Transform" or "ParticleSystem" or "ParticleSystemRenderer" or "MeshRenderer" or "MeshFilter" or "LineRenderer" or "TrailRenderer" or "Light") continue;
            doomed.Add(c);
        }
        // Scripts first, then anything they depended on.
        foreach (var c in doomed.Where(c => c.TryCast<MonoBehaviour>() != null)) UnityEngine.Object.DestroyImmediate(c);
        foreach (var c in doomed.Where(c => c != null)) { try { UnityEngine.Object.DestroyImmediate(c); } catch (Exception) { } }
        go.transform.SetParent(null, true);
        go.SetActive(true);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { ps.Clear(true); ps.Play(true); }
        Run(seconds, (t, a) => true, go);
        return go;
    }
}
