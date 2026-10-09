using System;
using System.Collections.Generic;
using IronstrikeApi.Gameplay;
using UnityEngine;
using K = IronstrikeApi.Content.Fx.Kind;

namespace IronstrikeApi.Content;

/// <summary>How an <see cref="SpellFx.Aura"/> looks.</summary>
public enum AuraStyle
{
    /// <summary>Glittering motes circling the fighter.</summary>
    Motes,
    /// <summary>Bubbles rising off the fighter (poison, disease).</summary>
    Bubbles,
    /// <summary>Small arcs of lightning crawling over the fighter.</summary>
    Crackle,
    /// <summary>A shimmering sphere around the fighter (wards, shields).</summary>
    Shell,
    /// <summary>Smoke curling off the fighter.</summary>
    Smoke,
    /// <summary>Embers drifting up from the fighter.</summary>
    Embers,
}

/// <summary>
/// Visual effects for spells, built from the game's own materials and textures (its lightning,
/// glows, sparks, smoke, flares) so they look at home. Local to this machine: call them from
/// <see cref="CustomSpell.OnCast"/>, which runs on every machine, and every player sees them.
/// </summary>
/// <remarks>
/// Colours are tints: pass the spell's colour and the effects build their bright cores, glows and
/// sparks from it. Every effect cleans up after itself, and all are cleared when the session ends.
/// </remarks>
public static class SpellFx
{
    static Color Hot(Color c) => Color.Lerp(c, Color.white, 0.65f);
    static Color A(Color c, float a) { c.a = a; return c; }

    /// <summary>A forked, flickering lightning bolt between two points, with sparks where it lands.</summary>
    /// <param name="from">Start.</param>
    /// <param name="to">End.</param>
    /// <param name="color">Tint.</param>
    /// <param name="width">Core width in metres.</param>
    /// <param name="seconds">How long it crackles.</param>
    public static void Bolt(Vector3 from, Vector3 to, Color color, float width = 0.12f, float seconds = 0.35f)
    {
        float len = Vector3.Distance(from, to);
        int n = Mathf.Clamp((int)(len / 0.3f), 12, 64);
        float jag = Mathf.Clamp(len * 0.06f, 0.15f, 1.6f);
        var glow = Fx.Line(Fx.Mat("ApiBand", true), A(color, 0.55f), width * 7f, n);
        var core = Fx.Line(Fx.GameMat("LightningLineMat", "Lightning10"), Hot(color), width * 2.2f, n);
        var hot = Fx.Line(Fx.Mat("ApiBand", true), Color.white, width * 0.7f, n);
        // Forks: thinner bolts leaving the main one and dying out.
        var forks = new List<(LineRenderer Line, float T, Vector3 Off)>();
        int nf = Mathf.Clamp((int)(len / 4f), 1, 4);
        for (int i = 0; i < nf; i++)
            forks.Add((Fx.Line(Fx.GameMat("LightningLineMat", "Lightning10"), Hot(color), width * 1.1f, 6), Fx.R(0.2f, 0.8f), Fx.OnSphere() * Fx.R(0.8f, 2.2f)));
        int seed = Environment.TickCount;
        float nextJag = -1f;
        Burst(to, color, 1f);
        Fx.Emit(K.Flare, to, Vector3.zero, 2.2f, 0.25f, A(color, 1f));
        Fx.Emit(K.Flare, from, Vector3.zero, 1.2f, 0.18f, A(color, 1f));
        var light = Fx.PointLight(to, color, 6f, 9f);
        var objs = new List<GameObject> { glow.gameObject, core.gameObject, hot.gameObject, light.gameObject };
        foreach (var f in forks) objs.Add(f.Line.gameObject);
        Fx.Run(seconds, (t, age) =>
        {
            if (age >= nextJag)
            {
                nextJag = age + 0.045f;
                seed++;
                Fx.Crackle(core, from, to, jag * 0.6f, seed);
                Fx.Crackle(glow, from, to, jag * 0.6f, seed);
                Fx.Crackle(hot, from, to, jag * 0.6f, seed);
                foreach (var f in forks)
                {
                    int k = Mathf.Clamp((int)(f.T * (n - 1)), 1, n - 2);
                    var s = core.GetPosition(k);
                    Fx.Jag(f.Line, s, s + f.Off + (to - from).normalized * 0.6f, 0.25f, seed * 7 + k);
                }
            }
            float a = t < 0.6f ? (Fx.R(0f, 1f) < 0.15f ? 0.35f : 1f) : 1f - (t - 0.6f) / 0.4f;   // flicker, then fade
            glow.startColor = glow.endColor = A(color, 0.55f * a);
            core.startColor = core.endColor = A(Hot(color), a);
            hot.startColor = hot.endColor = A(Color.white, a);
            foreach (var f in forks) f.Line.startColor = f.Line.endColor = A(Hot(color), a * 0.8f);
            light.intensity = 6f * a;
            return true;
        }, objs.ToArray());
    }

    /// <summary>A searing beam between two points: a soft glow, a coloured body and a white-hot core,
    /// pulsing, with a flare, sparks and a shockwave where it lands.</summary>
    /// <param name="from">Start (e.g. high in the sky).</param>
    /// <param name="to">End.</param>
    /// <param name="color">Tint.</param>
    /// <param name="width">Body width in metres.</param>
    /// <param name="seconds">How long it burns.</param>
    public static void Beam(Vector3 from, Vector3 to, Color color, float width = 0.6f, float seconds = 0.6f)
    {
        var outer = Fx.Line(Fx.Mat("ApiBand", true), A(color, 0.35f), width * 4f, 2);
        var body = Fx.Line(Fx.Mat("ApiBand", true), A(Hot(color), 0.9f), width * 1.4f, 2);
        var core = Fx.Line(Fx.Mat("ApiBand", true), Color.white, width * 0.45f, 2);
        foreach (var l in new[] { outer, body, core }) { l.SetPosition(0, from); l.SetPosition(1, to); }
        var light = Fx.PointLight(to + Vector3.up * 0.5f, color, 10f, 16f);
        Shockwave(to, width * 6f, color, 0.5f);
        Fx.Emit(K.Flare, to, Vector3.zero, width * 7f, 0.35f, A(Hot(color), 1f));
        var dir = (to - from).normalized;
        float sparkAcc = 0f;
        float last = 0f;
        Fx.Run(seconds, (t, age) =>
        {
            float grow = Mathf.Clamp01(age / 0.07f);
            float fade = t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f;
            float pulse = 1f + Mathf.Sin(age * 45f) * 0.12f + Fx.R(-0.05f, 0.05f);
            float w = width * grow * fade * pulse;
            outer.startWidth = outer.endWidth = w * 4f;
            body.startWidth = body.endWidth = w * 1.4f;
            core.startWidth = core.endWidth = w * 0.45f;
            light.intensity = 10f * fade * pulse;
            float dt = age - last; last = age;
            sparkAcc += dt * 160f;
            while (sparkAcc >= 1f)
            {
                sparkAcc -= 1f;
                var v = Vector3.ProjectOnPlane(Fx.OnSphere(), dir).normalized * Fx.R(3f, 9f) - dir * Fx.R(0f, 3f);
                Fx.Emit(K.Spark, to + Vector3.up * 0.1f, v, Fx.R(0.06f, 0.14f), Fx.R(0.25f, 0.6f), Hot(color));
                if (Fx.R(0, 1) < 0.3f) Fx.Emit(K.Ember, Vector3.Lerp(from, to, Fx.R(0.6f, 1f)) + Fx.InSphere() * w, Fx.InSphere(), Fx.R(0.15f, 0.35f), Fx.R(0.4f, 0.8f), color);
                if (Fx.R(0, 1) < 0.08f) Fx.Emit(K.Smoke, to + Fx.InSphere() * w, Vector3.up * Fx.R(0.5f, 1.5f), Fx.R(1f, 2f), Fx.R(1f, 2f), new Color(0.15f, 0.13f, 0.12f, 0.5f));
            }
            return true;
        }, outer.gameObject, body.gameObject, core.gameObject, light.gameObject);
    }

    /// <summary>A glowing ring on the ground, with motes rising from it.</summary>
    /// <param name="center">Centre.</param>
    /// <param name="radius">Metres.</param>
    /// <param name="color">Tint.</param>
    /// <param name="seconds">How long it shows.</param>
    public static void Ring(Vector3 center, float radius, Color color, float seconds = 0.8f)
    {
        const int n = 72;
        var glow = Fx.Line(Fx.Mat("ApiBand", true), A(color, 0.6f), 0.5f, n);
        var line = Fx.Line(Fx.Mat("ApiBand", true), A(Hot(color), 1f), 0.12f, n);
        glow.loop = line.loop = true;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            var p = center + new Vector3(Mathf.Cos(a) * radius, 0.08f, Mathf.Sin(a) * radius);
            glow.SetPosition(i, p);
            line.SetPosition(i, p);
        }
        float acc = 0f, last = 0f;
        Fx.Run(seconds, (t, age) =>
        {
            float a = Mathf.Clamp01(age / 0.12f) * (t > 0.75f ? 1f - (t - 0.75f) / 0.25f : 1f);
            float pulse = 0.8f + 0.2f * Mathf.Sin(age * 8f);
            glow.startColor = glow.endColor = A(color, 0.6f * a * pulse);
            line.startColor = line.endColor = A(Hot(color), a);
            acc += (age - last) * Mathf.Min(radius * 14f, 140f); last = age;
            while (acc >= 1f)
            {
                acc -= 1f;
                float ang = Fx.R(0f, Mathf.PI * 2f);
                var p = center + new Vector3(Mathf.Cos(ang) * radius, 0.1f, Mathf.Sin(ang) * radius);
                Fx.Emit(K.Mote, p, Vector3.up * Fx.R(0.6f, 1.8f), Fx.R(0.08f, 0.2f), Fx.R(0.5f, 1.1f), A(Hot(color), a));
            }
            return true;
        }, glow.gameObject, line.gameObject);
    }

    /// <summary>A ring that races outward across the ground, kicking up dust.</summary>
    /// <param name="center">Centre.</param>
    /// <param name="radius">How far it travels, in metres.</param>
    /// <param name="color">Tint.</param>
    /// <param name="seconds">How long it takes.</param>
    public static void Shockwave(Vector3 center, float radius, Color color, float seconds = 0.5f)
    {
        const int n = 64;
        var glow = Fx.Line(Fx.Mat("ApiBand", true), A(color, 0.7f), 1f, n);
        var line = Fx.Line(Fx.Mat("ApiBand", true), A(Hot(color), 1f), 0.25f, n);
        glow.loop = line.loop = true;
        for (int i = 0; i < 24; i++)
        {
            float ang = i * Mathf.PI * 2f / 24;
            var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            Fx.Emit(K.Dust, center + d * 0.3f + Vector3.up * 0.2f, d * radius / seconds * 0.6f + Vector3.up * Fx.R(0.2f, 1f), Fx.R(0.6f, 1.2f), Fx.R(0.5f, 0.9f), new Color(0.55f, 0.5f, 0.45f, 0.5f));
        }
        Fx.Run(seconds, (t, age) =>
        {
            float e = 1f - (1f - t) * (1f - t);
            float r = Mathf.Max(0.05f, radius * e);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var p = center + new Vector3(Mathf.Cos(a) * r, 0.12f, Mathf.Sin(a) * r);
                glow.SetPosition(i, p);
                line.SetPosition(i, p);
            }
            float fade = 1f - t;
            glow.startWidth = glow.endWidth = 1.2f * fade + 0.1f;
            line.startWidth = line.endWidth = 0.3f * fade + 0.03f;
            glow.startColor = glow.endColor = A(color, 0.7f * fade);
            line.startColor = line.endColor = A(Hot(color), fade);
            return true;
        }, glow.gameObject, line.gameObject);
    }

    /// <summary>A flash of coloured light: a real light at the point, a flare, and the game's sky flash.</summary>
    /// <param name="at">Where.</param>
    /// <param name="color">Colour.</param>
    /// <param name="intensity">Brightness (1 to 4 is the game's range).</param>
    /// <param name="seconds">Fade time.</param>
    public static void Flash(Vector3 at, Color color, float intensity = 2f, float seconds = 0.4f)
    {
        SpellEffects.SkyFlash(at, color, intensity, seconds);
        Fx.Emit(K.Flare, at, Vector3.zero, 1.5f + intensity, seconds * 0.6f, A(Hot(color), 1f));
        var l = Fx.PointLight(at, color, intensity * 4f, 6f + intensity * 3f);
        Fx.Run(seconds, (t, age) => { l.intensity = intensity * 4f * (1f - t) * (1f - t); return true; }, l.gameObject);
    }

    /// <summary>A spray of sparks and a puff of glow.</summary>
    /// <param name="at">Where.</param>
    /// <param name="color">Tint.</param>
    /// <param name="amount">1 is a hit; 3 is a big impact.</param>
    public static void Burst(Vector3 at, Color color, float amount = 1f)
    {
        int sparks = (int)(18 * amount);
        for (int i = 0; i < sparks; i++)
            Fx.Emit(K.Spark, at, Fx.OnSphere() * Fx.R(3f, 8f) * Mathf.Sqrt(amount) + Vector3.up * 2f, Fx.R(0.05f, 0.12f), Fx.R(0.25f, 0.6f), Hot(color));
        for (int i = 0; i < (int)(5 * amount); i++)
            Fx.Emit(K.Glow, at + Fx.InSphere() * 0.3f * amount, Fx.InSphere() * 1.5f, Fx.R(0.6f, 1.2f) * amount, Fx.R(0.2f, 0.4f), A(color, 0.9f));
    }

    /// <summary>A full explosion: flash, fireball, sparks, embers, smoke and a shockwave.</summary>
    /// <param name="at">Where.</param>
    /// <param name="color">Tint (orange for fire, green for acid...).</param>
    /// <param name="size">1 is about a three-metre blast.</param>
    public static void Explosion(Vector3 at, Color color, float size = 1f)
    {
        Flash(at + Vector3.up * 0.5f, color, 3f * size, 0.5f);
        Fx.Emit(K.Flare, at + Vector3.up * 0.4f, Vector3.zero, 5f * size, 0.3f, Color.white);
        for (int i = 0; i < 14 * size; i++)
            Fx.Emit(K.Glow, at + Vector3.up * 0.6f * size + Fx.InSphere() * 0.8f * size, Fx.OnSphere() * Fx.R(1f, 4f) * size + Vector3.up, Fx.R(1.2f, 2.4f) * size, Fx.R(0.25f, 0.5f), A(Color.Lerp(color, Color.white, Fx.R(0f, 0.5f)), 1f));
        for (int i = 0; i < 45 * size; i++)
            Fx.Emit(K.Spark, at + Vector3.up * 0.3f, (Fx.OnSphere() + Vector3.up * 0.7f) * Fx.R(5f, 13f) * Mathf.Sqrt(size), Fx.R(0.06f, 0.15f), Fx.R(0.4f, 1f), Hot(color));
        for (int i = 0; i < 16 * size; i++)
            Fx.Emit(K.Ember, at + Fx.InSphere() * size, (Fx.OnSphere() + Vector3.up) * Fx.R(1f, 4f), Fx.R(0.1f, 0.25f), Fx.R(0.8f, 1.8f), color);
        for (int i = 0; i < 12 * size; i++)
            Fx.Emit(K.Smoke, at + Vector3.up * 0.8f * size + Fx.InSphere() * size, (Fx.InSphere() + Vector3.up * 1.2f) * Fx.R(0.5f, 2f), Fx.R(1.5f, 3f) * size, Fx.R(1.2f, 2.5f), new Color(0.12f, 0.1f, 0.1f, 0.65f));
        Shockwave(at, 3.5f * size, color, 0.45f);
    }

    /// <summary>A pulsing warning on the ground where something is about to strike, with light gathering inward.</summary>
    /// <param name="center">Centre.</param>
    /// <param name="radius">Metres.</param>
    /// <param name="color">Tint.</param>
    /// <param name="seconds">How long until the strike.</param>
    public static void Telegraph(Vector3 center, float radius, Color color, float seconds = 1f)
    {
        Ring(center, radius, color, seconds + 0.2f);
        const int n = 64;
        var inner = Fx.Line(Fx.Mat("ApiBand", true), A(color, 0.8f), 0.35f, n);
        inner.loop = true;
        float acc = 0f, last = 0f;
        Fx.Run(seconds, (t, age) =>
        {
            float r = radius * (1f - (age * 1.6f % 1f));
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                inner.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r));
            }
            inner.startColor = inner.endColor = A(color, 0.3f + 0.6f * t);
            acc += (age - last) * (30f + 120f * t); last = age;
            while (acc >= 1f)
            {
                acc -= 1f;
                float ang = Fx.R(0f, Mathf.PI * 2f);
                var p = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius * Fx.R(0.7f, 1.1f) + Vector3.up * Fx.R(0.1f, 1.5f);
                Fx.Emit(K.Mote, p, (center + Vector3.up * 0.3f - p) * 1.6f, Fx.R(0.1f, 0.25f), 0.6f, Hot(color));
            }
            return true;
        }, inner.gameObject);
    }

    /// <summary>An effect that clings to a fighter for a time.</summary>
    /// <param name="fighter">Who.</param>
    /// <param name="color">Tint.</param>
    /// <param name="seconds">How long.</param>
    /// <param name="style">What it looks like.</param>
    public static void Aura(Fighter fighter, Color color, float seconds, AuraStyle style = AuraStyle.Motes)
    {
        if (fighter is null) return;
        float acc = 0f, last = 0f, nextArc = 0f;
        bool local = Players.IsLocal(fighter);
        float spread = local ? 1.8f : 1f;
        LineRenderer shell = null;
        if (style == AuraStyle.Shell) { shell = Fx.Line(Fx.Mat("ApiBand", true), A(color, 0.5f), 0.25f, 40); shell.loop = true; }
        Fx.Run(seconds, (t, age) =>
        {
            if (fighter == null || fighter.dead) return false;
            // On this machine's own player, keep it below the eyes and wider, so it never fills the view.
            var c = local ? Body.Feet(fighter) + Vector3.up * 0.55f : Body.Center(fighter);
            float fade = t > 0.85f ? 1f - (t - 0.85f) / 0.15f : Mathf.Clamp01(age / 0.2f);
            float dt = age - last; last = age;
            switch (style)
            {
                case AuraStyle.Motes:
                    acc += dt * 50f;
                    while (acc >= 1f)
                    {
                        acc -= 1f;
                        float ang = Fx.R(0f, Mathf.PI * 2f), h = Fx.R(-0.9f, 0.9f);
                        var off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Fx.R(0.5f, 0.9f) * spread;
                        Fx.Emit(K.Mote, c + off + Vector3.up * h, Vector3.Cross(Vector3.up, off).normalized * 2.2f + Vector3.up * 0.4f, Fx.R(0.08f, 0.2f), Fx.R(0.4f, 0.8f), A(Hot(color), fade));
                    }
                    break;
                case AuraStyle.Bubbles:
                    acc += dt * 22f;
                    while (acc >= 1f) { acc -= 1f; Fx.Emit(K.Bubble, c + Fx.InSphere() * 0.6f, Vector3.up * Fx.R(0.4f, 1.2f), Fx.R(0.1f, 0.25f), Fx.R(0.6f, 1.2f), A(color, fade)); }
                    acc += 0f;
                    if (Fx.R(0, 1) < dt * 6f) Fx.Emit(K.Smoke, c + Fx.InSphere() * 0.5f, Vector3.up * 0.5f, Fx.R(0.6f, 1.1f), 1.2f, A(color * 0.6f, 0.35f * fade));
                    break;
                case AuraStyle.Crackle:
                    if (age >= nextArc)
                    {
                        nextArc = age + Fx.R(0.06f, 0.16f);
                        var a = c + Fx.OnSphere() * 0.6f * spread;
                        Bolt(a, a + Fx.OnSphere() * Fx.R(0.4f, 0.9f), color, 0.025f, 0.12f);
                    }
                    break;
                case AuraStyle.Shell:
                    float r = 1.3f * spread + Mathf.Sin(age * 4f) * 0.05f;
                    for (int i = 0; i < 40; i++)
                    {
                        float a2 = i * Mathf.PI * 2f / 40 + age * 0.8f;
                        shell.SetPosition(i, c + new Vector3(Mathf.Cos(a2) * r, Mathf.Sin(a2 * 3f + age * 2f) * 0.25f, Mathf.Sin(a2) * r));
                    }
                    shell.startColor = shell.endColor = A(color, 0.45f * fade);
                    acc += dt * 60f;
                    while (acc >= 1f) { acc -= 1f; Fx.Emit(K.Glow, c + Fx.OnSphere() * r, Vector3.zero, Fx.R(0.15f, 0.35f), Fx.R(0.2f, 0.4f), A(color, 0.5f * fade)); }
                    break;
                case AuraStyle.Smoke:
                    acc += dt * 14f;
                    while (acc >= 1f) { acc -= 1f; Fx.Emit(K.Smoke, c + Fx.InSphere() * 0.5f * spread, Vector3.up * Fx.R(0.3f, 0.9f), Fx.R(0.8f, 1.5f), Fx.R(1f, 1.8f), A(color, (local ? 0.2f : 0.5f) * fade)); }
                    break;
                case AuraStyle.Embers:
                    acc += dt * 35f;
                    while (acc >= 1f) { acc -= 1f; Fx.Emit(K.Ember, c + Fx.InSphere() * 0.6f, Vector3.up * Fx.R(0.8f, 2f) + Fx.InSphere() * 0.4f, Fx.R(0.08f, 0.18f), Fx.R(0.6f, 1.2f), A(color, fade)); }
                    break;
            }
            return true;
        }, shell != null ? new[] { shell.gameObject } : Array.Empty<GameObject>());
    }

    /// <summary>A wavering beam joining two fighters for a time, with light flowing from the first to the second.</summary>
    /// <param name="from">Where the flow starts (the drained enemy...).</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="color">Tint.</param>
    /// <param name="seconds">How long it holds.</param>
    public static void Tether(Fighter from, Fighter to, Color color, float seconds)
    {
        if (from is null || to is null) return;
        Tether(() => from == null || from.dead ? null : Body.Center(from), () => to == null || to.dead ? null : Body.Center(to), color, seconds);
    }

    /// <summary>A wavering beam between two moving points (a wand tip and a foe), with light flowing from the first to the second.
    /// Either end returning null ends it.</summary>
    /// <param name="from">Start, asked every frame.</param>
    /// <param name="to">End, asked every frame.</param>
    /// <param name="color">Tint.</param>
    /// <param name="seconds">How long it holds.</param>
    public static void Tether(Func<Vector3?> from, Func<Vector3?> to, Color color, float seconds)
    {
        const int n = 24;
        var glow = Fx.Line(Fx.Mat("ApiBand", true), A(color, 0.5f), 0.45f, n);
        var core = Fx.Line(Fx.Mat("ApiBand", true), A(Hot(color), 1f), 0.12f, n);
        float acc = 0f, last = 0f;
        Fx.Run(seconds, (t, age) =>
        {
            Vector3? aq, bq;
            try { aq = from(); bq = to(); } catch (Exception) { return false; }
            if (aq == null || bq == null) return false;
            Vector3 a = aq.Value, b = bq.Value;
            var mid = (a + b) / 2f + Vector3.up * Mathf.Min(2f, Vector3.Distance(a, b) * 0.12f);
            var side = Vector3.Cross(b - a, Vector3.up).normalized;
            float fade = Mathf.Clamp01(age / 0.15f) * (t > 0.85f ? 1f - (t - 0.85f) / 0.15f : 1f);
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                var p = (1 - u) * (1 - u) * a + 2 * (1 - u) * u * mid + u * u * b;
                p += side * Mathf.Sin(u * 9f + age * 12f) * 0.12f * Mathf.Sin(u * Mathf.PI);
                glow.SetPosition(i, p);
                core.SetPosition(i, p);
            }
            glow.startWidth = 0.15f; glow.endWidth = 0.6f;      // thin where it leaves the wand
            core.startWidth = 0.04f; core.endWidth = 0.16f;
            glow.startColor = glow.endColor = A(color, 0.5f * fade);
            core.startColor = core.endColor = A(Hot(color), fade);
            acc += (age - last) * 45f; last = age;
            while (acc >= 1f)
            {
                acc -= 1f;
                float u = Fx.R(0.1f, 0.9f);
                var p = (1 - u) * (1 - u) * a + 2 * (1 - u) * u * mid + u * u * b;
                Fx.Emit(K.Glow, p + Fx.InSphere() * 0.12f, (b - a).normalized * Fx.R(4f, 8f), Fx.R(0.12f, 0.25f), 0.35f, A(Hot(color), fade));
            }
            return true;
        }, glow.gameObject, core.gameObject);
    }

    /// <summary>A churning cloud hanging over an area (a storm, a plague).</summary>
    /// <param name="center">The ground point under it.</param>
    /// <param name="radius">Metres.</param>
    /// <param name="height">How high above the ground it hangs.</param>
    /// <param name="color">Tint: dark grey for a storm, sickly green for a plague.</param>
    /// <param name="seconds">How long it stays.</param>
    /// <param name="flicker">True for lightning glowing inside it.</param>
    public static void Cloud(Vector3 center, float radius, float height, Color color, float seconds, bool flicker = false)
    {
        float acc = 0f, last = 0f, nextFlick = 0f;
        Fx.Run(seconds, (t, age) =>
        {
            float dt = age - last; last = age;
            float rate = t < 0.8f ? 40f + radius * 12f : 0f;
            acc += dt * rate;
            while (acc >= 1f)
            {
                acc -= 1f;
                var d = UnityEngine.Random.insideUnitCircle * radius;
                var p = center + new Vector3(d.x, height + Fx.R(-0.6f, 0.6f), d.y);
                var swirl = Vector3.Cross(Vector3.up, p - center).normalized * Fx.R(0.3f, 1.2f);
                Fx.Emit(K.Smoke, p, swirl + Vector3.up * Fx.R(-0.1f, 0.2f), Fx.R(4f, 7.5f), Fx.R(2f, 3.2f), A(color, 0.85f));
            }
            if (flicker && age >= nextFlick && t < 0.85f)
            {
                nextFlick = age + Fx.R(0.08f, 0.35f);
                var d = UnityEngine.Random.insideUnitCircle * radius * 0.8f;
                var p = center + new Vector3(d.x, height, d.y);
                Fx.Emit(K.Glow, p, Vector3.zero, Fx.R(5f, 10f), 0.14f, new Color(0.7f, 0.8f, 1f, 0.9f));
                Fx.Emit(K.Flare, p, Vector3.zero, Fx.R(4f, 8f), 0.1f, new Color(0.8f, 0.9f, 1f, 0.6f));
            }
            return true;
        });
    }

    /// <summary>A column of light rising from the ground, with motes streaming up it.</summary>
    /// <param name="at">The ground point.</param>
    /// <param name="color">Tint.</param>
    /// <param name="height">Metres.</param>
    /// <param name="seconds">How long it shines.</param>
    public static void Pillar(Vector3 at, Color color, float height = 8f, float seconds = 1.2f)
    {
        var glow = Fx.Line(Fx.Mat("ApiBand", true), A(color, 0.4f), 2.2f, 2);
        var core = Fx.Line(Fx.Mat("ApiBand", true), A(Hot(color), 0.8f), 0.6f, 2);
        glow.SetPosition(0, at); glow.SetPosition(1, at + Vector3.up * height);
        core.SetPosition(0, at); core.SetPosition(1, at + Vector3.up * height);
        glow.endColor = A(color, 0f);
        Ring(at, 1.4f, color, seconds);
        var light = Fx.PointLight(at + Vector3.up, color, 5f, 8f);
        float acc = 0f, last = 0f;
        Fx.Run(seconds, (t, age) =>
        {
            float a = Mathf.Clamp01(age / 0.15f) * (t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f);
            glow.startColor = A(color, 0.45f * a);
            core.startColor = A(Hot(color), 0.85f * a);
            core.endColor = A(Hot(color), 0f);
            light.intensity = 5f * a;
            acc += (age - last) * 80f; last = age;
            while (acc >= 1f)
            {
                acc -= 1f;
                var d = UnityEngine.Random.insideUnitCircle * 0.9f;
                Fx.Emit(K.Mote, at + new Vector3(d.x, Fx.R(0f, 0.5f), d.y), Vector3.up * Fx.R(3f, 7f), Fx.R(0.1f, 0.25f), Fx.R(0.6f, 1.2f), A(Hot(color), a));
            }
            return true;
        }, glow.gameObject, core.gameObject, light.gameObject);
    }

    /// <summary>A pulsing ball of light that follows a moving point (a wand tip, a hand) for a time.</summary>
    /// <param name="at">Where it is, asked every frame.</param>
    /// <param name="color">Tint.</param>
    /// <param name="size">Metres across.</param>
    /// <param name="seconds">How long.</param>
    public static void Glow(Func<Vector3> at, Color color, float size, float seconds)
    {
        var light = Fx.PointLight(at(), color, 4f, 6f);
        float acc = 0f, last = 0f;
        Fx.Run(seconds, (t, age) =>
        {
            Vector3 p;
            try { p = at(); } catch (Exception) { return false; }
            light.transform.position = p;
            float a = Mathf.Clamp01(age / 0.1f) * (t > 0.85f ? 1f - (t - 0.85f) / 0.15f : 1f);
            light.intensity = (4f + Fx.R(-1f, 1.5f)) * a;
            acc += (age - last) * 90f; last = age;
            while (acc >= 1f)
            {
                acc -= 1f;
                Fx.Emit(K.Glow, p + Fx.InSphere() * size * 0.15f, Fx.InSphere() * 0.3f, size * Fx.R(0.5f, 0.9f), 0.12f, A(color, 0.7f * a));
                if (Fx.R(0, 1) < 0.25f) Fx.Emit(K.Spark, p, Fx.OnSphere() * Fx.R(2f, 5f), Fx.R(0.03f, 0.07f), Fx.R(0.15f, 0.3f), Hot(color));
            }
            return true;
        }, light.gameObject);
    }

    /// <summary>A glowing shell streaking from one point to another (incoming bombardment), with a smoke trail.</summary>
    /// <param name="from">Start.</param>
    /// <param name="to">End; it arrives after <paramref name="seconds"/>.</param>
    /// <param name="color">Tint.</param>
    /// <param name="seconds">Flight time.</param>
    public static void Streak(Vector3 from, Vector3 to, Color color, float seconds)
    {
        var trail = Fx.Line(Fx.Mat("ApiBand", true), A(Hot(color), 1f), 0.35f, 2);
        trail.endColor = A(color, 0f);
        float last = 0f;
        Fx.Run(seconds, (t, age) =>
        {
            var head = Vector3.Lerp(from, to, t * t);
            var tail = Vector3.Lerp(from, to, Mathf.Max(0f, t * t - 0.12f));
            trail.SetPosition(0, head);
            trail.SetPosition(1, tail);
            float dt = age - last; last = age;
            Fx.Emit(K.Glow, head, Vector3.zero, 1.4f, 0.08f, A(Hot(color), 1f));
            if (Fx.R(0, 1) < dt * 40f) Fx.Emit(K.Smoke, tail, Fx.InSphere() * 0.3f, Fx.R(0.6f, 1.1f), Fx.R(0.8f, 1.4f), new Color(0.2f, 0.18f, 0.17f, 0.45f));
            return true;
        }, trail.gameObject);
    }

    /// <summary>
    /// A sustained, writhing stream of lightning between two moving points (force lightning from a wand
    /// to a foe), re-forked every few frames, with sparks where it bites.
    /// </summary>
    /// <param name="from">Start, asked every frame.</param>
    /// <param name="to">End, asked every frame; return null to end the stream.</param>
    /// <param name="color">Tint.</param>
    /// <param name="seconds">How long.</param>
    /// <param name="width">Core width.</param>
    public static void Channel(Func<Vector3> from, Func<Vector3?> to, Color color, float seconds, float width = 0.1f)
    {
        const int n = 44;
        var strands = new List<(LineRenderer Glow, LineRenderer Core)>();
        for (int i = 0; i < 3; i++)
            strands.Add((Fx.Line(Fx.Mat("ApiBand", true), A(color, 0.4f), width * (i == 0 ? 7f : 4f), n),
                         Fx.Line(Fx.Mat("ApiBand", true), A(Hot(color), 1f), width * (i == 0 ? 1.6f : 0.9f), n)));
        var light = Fx.PointLight(from(), color, 5f, 8f);
        int seed = Environment.TickCount;
        float next = 0f, last = 0f;
        var objs = new List<GameObject> { light.gameObject };
        foreach (var s2 in strands) { objs.Add(s2.Glow.gameObject); objs.Add(s2.Core.gameObject); }
        Fx.Run(seconds, (t, age) =>
        {
            Vector3 a; Vector3? bq;
            try { a = from(); bq = to(); } catch (Exception) { return false; }
            if (bq == null) return false;
            var b = bq.Value;
            float fade = Mathf.Clamp01(age / 0.06f) * (t > 0.9f ? 1f - (t - 0.9f) / 0.1f : 1f);
            float len = Vector3.Distance(a, b);
            if (age >= next)
            {
                next = age + 0.04f;
                for (int i = 0; i < strands.Count; i++)
                {
                    seed++;
                    float jag = Mathf.Clamp(len * (i == 0 ? 0.035f : 0.055f), 0.08f, 0.9f);
                    Fx.Crackle(strands[i].Core, a, b, jag, seed * 3 + i);
                    Fx.Crackle(strands[i].Glow, a, b, jag, seed * 3 + i);
                }
            }
            for (int i = 0; i < strands.Count; i++)
            {
                float f = fade * (Fx.R(0, 1) < 0.1f ? 0.35f : 1f) * (i == 0 ? 1f : 0.65f);
                strands[i].Glow.startColor = strands[i].Glow.endColor = A(color, 0.4f * f);
                strands[i].Core.startColor = strands[i].Core.endColor = A(Hot(color), f);
            }
            light.transform.position = b;
            light.intensity = 6f * fade;
            float dt = age - last; last = age;
            if (Fx.R(0, 1) < dt * 30f) Burst(b, color, 0.3f);
            return true;
        }, objs.ToArray());
    }

    /// <summary>
    /// Plays one of the game's own effects, visuals only: <c>CometfallEffect</c>, <c>FrostNovaEffect</c>,
    /// <c>StaticFieldEffect</c>, <c>AcidSprayEffect</c>, <c>SmokeBombExplodeFX</c>, <c>GoutOfFlame</c>,
    /// <c>VFX-TestHit</c>... Returns false if the game has no effect by that name.
    /// </summary>
    /// <param name="name">The effect's name.</param>
    /// <param name="at">Where.</param>
    /// <param name="scale">Size, 1 as the game uses it.</param>
    /// <param name="seconds">When to remove it.</param>
    public static bool GameEffect(string name, Vector3 at, float scale = 1f, float seconds = 3f)
    {
        try { return Fx.GamePrefab(name, at, Quaternion.identity, scale, seconds) != null; }
        catch (Exception e) { ApiLog.WarnOnce(null, "fx:prefab:" + name, $"spell effects: could not play {name}: {e.Message}"); return false; }
    }
}
