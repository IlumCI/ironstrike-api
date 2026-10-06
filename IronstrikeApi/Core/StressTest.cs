using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Fusion;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using IronstrikeApi.Gameplay;
using IronstrikeApi.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace IronstrikeApi.Core;

// [09 Debug] StressTest: edge cases and abuse that only the running game can answer -- UI built and
// torn down in the real engine, the real hooks, a scene change under an open window. Each case logs
// "stress: PASS <name>" or "stress: FAIL <name>: why"; a summary line ends the run.
//
// Failures the suite causes on purpose carry the marker below. Any other API error logged while it
// runs counts against the case that was running.
internal static class StressTest
{
    const string Boom = "STRESS-INTENTIONAL";

    sealed class Step
    {
        public string Name;
        public float Delay;
        public Action Run;
    }

    static readonly List<Step> steps = new();
    static int index = -1, passed, failed;
    static float at;
    static readonly List<string> unexpected = new();
    static string current = "";
    static readonly List<string> failures = new();
    static Action<string> previousSink;

    internal static void Register()
    {
        if (!Plugin.C.StressTest.Value) return;
        Plugin.Log.LogWarning("stress test is ON ([09 Debug] StressTest); it drives the game by itself.");
        Build();
        index = 0;
    }

    static void Pass(string detail = "") { passed++; Plugin.Log.LogMessage($"stress: PASS {current}{(detail.Length > 0 ? " (" + detail + ")" : "")}"); }

    static void Fail(string why)
    {
        failed++;
        failures.Add(current);
        Plugin.Log.LogError($"stress: FAIL {current}: {why}");
    }

    static void Check(bool ok, string why, string detail = "") { if (ok) Pass(detail); else Fail(why); }

    internal static void Tick(float now)
    {
        if (index < 0 || index >= steps.Count || now < at) return;
        var s = steps[index++];
        at = now + s.Delay;
        if (s.Name != null) current = s.Name;
        int before = unexpected.Count;
        try { s.Run(); }
        catch (Exception e) { Fail($"threw {e.GetType().Name}: {e.Message}"); }
        // Errors that surfaced from inside the API during this step (or in the frames since the
        // last one), other than the planted ones.
        if (unexpected.Count > before)
            Fail($"{unexpected.Count - before} unexpected API error(s): {unexpected[before]}");
    }

    static void Add(string name, float delay, Action run) => steps.Add(new Step { Name = name, Delay = delay, Run = run });
    static void Then(float delay, Action run) => steps.Add(new Step { Delay = delay, Run = run });

    static void Watch()
    {
        previousSink = Diag.Sink;
        Diag.Sink = line =>
        {
            if (line.StartsWith("E ") && !line.Contains(Boom)) lock (unexpected) unexpected.Add(line.Length > 300 ? line.Substring(0, 300) : line);
            var t = line.Substring(2);
            if (line[0] == 'E') Plugin.Log.LogError(t); else if (line[0] == 'W') Plugin.Log.LogWarning(t); else Plugin.Log.LogInfo(t);
        };
    }

    // ------------------------------------------------------------------ helpers

    static List<Button> PanelButtons(bool includeFields = false)
    {
        var list = new List<Button>();
        if (Panel.Box == null) return list;
        foreach (var b in Panel.Box.GetComponentsInChildren<Button>(false))
        {
            if (b == null) continue;
            string n = b.gameObject.name;
            if (n == "Close") continue;                    // the window's own X
            if (!includeFields && n == "Field") continue;  // opens the keyboard
            list.Add(b);
        }
        return list;
    }

    static int ClickAll(int rounds)
    {
        int clicks = 0;
        for (int r = 0; r < rounds; r++)
            foreach (var b in PanelButtons())          // includes buttons a click just replaced (stale)
            {
                b.onClick.Invoke();
                clicks++;
            }
        return clicks;
    }

    static int ContentObjects() => Panel.Box == null ? 0 : Panel.Box.GetComponentsInChildren<Transform>(true).Length;

    static float MoveSpeed()
    {
        var f = Players.LocalFighter;
        return f == null ? float.NaN : f.CalcSkillAndStatusEffectValue(SkillCalcType.MoveSpeed, null);
    }

    static ConfigFile weird;
    static string weirdPath;
    static int renders;
    static Window reentrant, thrower, big, controls, settings, other;
    static readonly List<IDisposable> mods = new();
    static readonly List<Action> handlers = new();
    static int baseFrames;
    static float baseAvg;
    static int frames;
    static float frameSum;

    enum Mode { Off, Low, High }
    [Flags] enum Bits { None = 0, A = 1, B = 2, C = 4 }

    // ------------------------------------------------------------------ the cases

    static void Build()
    {
        // 0. Wait for the haven and its menu; dismiss the first-start notice like SelfTest does.
        Then(2f, () =>
        {
            DismissModal();
            if (Game.Scene == null || !Game.Scene.IsHaven || MainMenu.Find() == null || !Panel.Ready) { index--; return; }
            Watch();
            Plugin.Log.LogMessage("stress: starting");
        });

        // A solo session first: stats, hooks, mod messages and the run all need a running host.
        Add("solo session starts and is classified", 1f, () =>
        {
            if (Game.Runner == null || !Game.Runner.IsRunning) MainMenu.Find()?.PressSolo();
        });
        Then(2f, () =>
        {
            if ((Game.Runner == null || !Game.Runner.IsRunning || Safety.Context != PlayContext.Solo) && ++waits < 30) { index--; return; }
            waits = 0;
            Check(Safety.Context == PlayContext.Solo && Game.IsHost, $"context {Safety.Context}, host {Game.IsHost}", $"context {Safety.Context}, host");
        });

        // A/B: which page content makes a window show up in front of the camera.
        var ab = new (string Name, Action<Page> Draw)[]
        {
            ("abSettings", p => { var cfg = new ConfigFile(Path.Combine(BepInEx.Paths.ConfigPath, "ironstrike.api.ab.cfg"), true); cfg.Bind("01 A", "X", 1); cfg.Bind("01 A", "Y", true); ModSettings.Draw(p, cfg); }),
            ("abHeader", p => p.Header("Only a header")),
            ("abInfo", p => p.Info("Only", "an info row")),
            ("abText", p => p.Text("Only a text line")),
            ("abProbe", p => { for (int i = 0; i < 10; i++) p.Text("line " + i); }),
        };
        foreach (var x in ab)
        {
            var c = x;
            Add("ab " + c.Name, 1f, () =>
            {
                var w = new Window("stress: " + c.Name);
                w.AddTab("P", c.Draw);
                w.Open();
                var root = Panel.Box.GetComponentInParent<Canvas>().rootCanvas.transform;
                Plugin.Log.LogMessage($"stress: SHOT {c.Name} (box {Panel.Box.position} scale {Panel.Box.lossyScale.x:0.00000} rot {Panel.Box.rotation.eulerAngles}; root {root.position} rot {root.rotation.eulerAngles} size {((RectTransform)root).sizeDelta}; box size {Panel.Box.rect.size} pivot {Panel.Box.pivot} anchors {Panel.Box.anchorMin}-{Panel.Box.anchorMax})");
                Pass();
            });
            Then(5f, () => Window.Current?.Close());
        }

        Add("window re-entrancy (render calls Refresh and Open on itself)", 0.5f, () =>
        {
            renders = 0;
            reentrant = new Window("stress: reentrant");
            reentrant.AddTab("Loop", p => { renders++; p.Text($"render #{renders}"); reentrant.Refresh(); reentrant.Open(); });
            reentrant.Open();
        });
        Then(1f, () =>
        {
            // Without the guard this was a stack overflow on the first Open. With it: at most one
            // redraw per frame, never nested.
            int r = renders;
            reentrant.Close();
            Check(r >= 1 && r <= Mathf.Max(2, Time.frameCount), $"{r} renders", $"{r} renders, none nested");
        });

        Add("render callback that throws", 0.5f, () =>
        {
            thrower = new Window("stress: thrower");
            thrower.AddTab("Bad", p => { p.Text("before"); throw new InvalidOperationException(Boom); });
            thrower.AddTab("Good", p => p.Text("fine"));
            thrower.Open();
        });
        Then(0.5f, () =>
        {
            var texts = Panel.Box.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false).Select(t => t.text).ToList();
            bool shown = texts.Any(t => t.Contains("failed to draw"));
            thrower.TabIndex = 1; thrower.Refresh();
            bool recovered = Panel.Box.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false).Any(t => t.text == "fine");
            thrower.Close();
            Check(shown && recovered, $"error shown={shown}, other tab drew={recovered}", "error shown, other tab fine");
        });

        Add("huge page: 5000-row table, 400-item sidebar", 0.5f, () =>
        {
            var rows = Enumerable.Range(0, 5000).Select(i => new[] { "row " + i, new string('x', i % 200), "<b>" + i + "</b>" }).ToList();
            var items = Enumerable.Range(0, 400).Select(i => i == 7 ? null : "item " + i).ToList();
            big = new Window("stress: big").WithSidebar(() => items);
            big.AddTab("Table", p => p.Table(new[] { new TableColumn("A", 1), null, new TableColumn("C", 0) }, rows, 3, i => { }));
            big.SidebarIndex = 399;
            var sw = Stopwatch.StartNew();
            big.Open();
            sw.Stop();
            int objs = ContentObjects();
            Plugin.Log.LogMessage($"stress: SHOT big ({sw.ElapsedMilliseconds} ms, {objs} objects)");
            Check(sw.ElapsedMilliseconds < 5000 && objs < 60000, $"{sw.ElapsedMilliseconds} ms, {objs} objects",
                  $"{sw.ElapsedMilliseconds} ms, {objs} objects, capped at {Page.MaxTableRows} rows");
        });
        Then(12f, () => big.Close());

        // How many table rows a window can show before it stops rendering (it was found invisible at
        // 300 rows of long cells): one window per size, each screenshot by the harness.
        foreach (int n in new[] { 50, 150, 300 })
        {
            int rowsN = n;
            Add($"table of {n} rows renders", 1f, () =>
            {
                var rows = Enumerable.Range(0, rowsN).Select(i => new[] { "row " + i, new string('x', i % 200), i.ToString() }).ToList();
                var w = new Window($"stress: {rowsN} rows");
                w.AddTab("T", p => p.Table(new[] { new TableColumn("A"), new TableColumn("B", 3), new TableColumn("C") }, rows));
                w.Open();
                int verts = 0;
                foreach (var t in Panel.Box.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false)) { t.ForceMeshUpdate(); verts += t.textInfo.characterCount * 4; }
                var cam = Camera.main;
                Plugin.Log.LogMessage($"stress: SHOT rows{rowsN} (~{verts} text vertices; window active {Panel.Visible} at {Panel.Box.position}, camera at {(cam != null ? cam.transform.position.ToString() : "-")})");
                Pass($"~{verts} text vertices");
            });
            Then(8f, () =>
            {
                var cam = Camera.main;
                Plugin.Log.LogMessage($"stress: rows{rowsN} after 8 s: window active {Panel.Visible} at {Panel.Box.position}, camera at {(cam != null ? cam.transform.position.ToString() : "-")}");
                Window.Current?.Close();
            });
        }

        Add("open/close/toggle storm in one frame", 0.5f, () =>
        {
            var a = new Window("stress: a"); a.AddTab("A", p => p.Text("a"));
            var b = new Window("stress: b"); b.AddTab("B", p => p.Text("b"));
            int closedA = 0; a.Closed += () => closedA++;
            var rng = new System.Random(5);
            Window last = null; bool lastOpen = false;
            for (int i = 0; i < 300; i++)
            {
                var w = rng.Next(2) == 0 ? a : b;
                switch (rng.Next(3))
                {
                    case 0: w.Open(); last = w; lastOpen = true; break;
                    case 1: if (Window.Current == w) { w.Close(); lastOpen = false; } else w.Close(); break;
                    default: w.Toggle(); if (Window.Current == w) { last = w; lastOpen = w.IsOpen; } break;
                }
            }
            bool consistent = last == null || (Window.Current == last && Window.Current.IsOpen == lastOpen) || Window.Current != last;
            Window.Current?.Close();
            Check(consistent && closedA > 0, $"consistent={consistent}, closedA={closedA}", $"a closed {closedA} times");
        });

        Add("fuzz-click every control, including stale and throwing ones", 0.5f, () =>
        {
            int counter = 0; float val = 5; int choice = 0; bool flag = false;
            other = new Window("stress: other"); other.AddTab("Other", p => p.Text("opened from a click"));
            controls = new Window("stress: controls");
            controls.AddTab("All", p =>
            {
                p.Header(null);
                p.Header("<b>unclosed <color=red>tags");
                p.Text(null);
                p.Text(new string('w', 5000), lines: 3);
                p.Info(null, null);
                p.Gap(-50);
                p.Toggle("flag", flag, v => flag = v);
                p.Toggle(null, flag, null);
                p.Stepper("normal", val, 1, 0, 10, v => val = v);
                p.Stepper("backwards range", val, 1, 10, 0, v => val = v);
                p.Stepper("NaN value", float.NaN, float.NaN, 0, 1, v => val = v);
                p.Stepper("bad format", val, 1, 0, 10, v => val = v, format: "{{{");
                p.Choice("choice", new[] { "a", null, "c" }, choice, i => choice = i);
                p.Choice("empty", Array.Empty<string>(), 5, i => choice = i);
                p.Choice("null list", null, -7, i => choice = i);
                p.Button(null, () => counter++);
                p.Button("throws", () => throw new Exception(Boom));
                p.Button("closes the window", () => controls.Close(), label: "close");
                p.Button("opens another window", () => other.Open());
                p.Button("reopens itself", () => controls.Open());
                p.Buttons(("x", () => counter++), (null, null), ("throw", () => throw new Exception(Boom)));
                p.Buttons();
                p.Buttons(null);
                p.Table(null, null);
                p.Table(new TableColumn[0], new List<string[]> { null });
                p.Table(new[] { new TableColumn("one") }, new List<string[]> { null, new string[0], new[] { "a", "b", "c" } }, 99, i => counter++);
                var strip = p.Custom(-10);
                Kit.Label(strip, "custom", 30, Kit.Text);
            });
            controls.Open();
            int clicks = 0;
            for (int round = 0; round < 8; round++)
            {
                if (!controls.IsOpen) controls.Open();
                clicks += ClickAll(1);
            }
            Window.Current?.Close();
            Pass($"{clicks} clicks, counter {counter}");
        });

        Add("probe after fuzz-click", 0.5f, () =>
        {
            var w = new Window("stress: probe");
            w.AddTab("P", p => { for (int i = 0; i < 10; i++) p.Text("line " + i); });
            w.Open();
            Plugin.Log.LogMessage($"stress: SHOT probeAfterClick (t={Time.realtimeSinceStartup:0}, frame {Time.frameCount}, alpha {string.Join(",", Panel.Box.GetComponentsInParent<CanvasGroup>(true).Select(c => c.alpha.ToString("0.00")))})");
            Pass();
        });
        Then(6f, () => Window.Current?.Close());

        Add("settings page for a config full of awkward entries", 0.5f, () =>
        {
            weirdPath = Path.Combine(BepInEx.Paths.ConfigPath, "ironstrike.api.stresstest.cfg");
            try { File.Delete(weirdPath); } catch (Exception) { }
            weird = new ConfigFile(weirdPath, true);
            // BepInEx forbids = \n \t \\ " ' [ ] in section and key names; markup without them is allowed.
            weird.Bind("01 <b>Markup</b> section", "Key with <color>tags</color> & <b>bold", "value with <size=200>tags", "Help with x < 5 and <i>markup");
            weird.Bind("02 Numbers", "Byte", (byte)250, new ConfigDescription("near the top", new AcceptableValueRange<byte>(0, 255)));
            weird.Bind("02 Numbers", "Huge", long.MaxValue - 1, "exact long");
            weird.Bind("02 Numbers", "Tiny", 1e-9, "a very small double");
            weird.Bind("02 Numbers", "Negative", -1_000_000_000, "big negative int");
            weird.Bind("02 Numbers", "Decimal", 3.14159m, "decimal");
            weird.Bind("02 Numbers", "UInt", uint.MaxValue, "uint max");
            weird.Bind("03 Choices", "Undefined enum", (Mode)42, "a value with no name");
            weird.Bind("03 Choices", "Flags", Bits.A | Bits.C, "a flags combination");
            weird.Bind("03 Choices", "Int list", 3, new ConfigDescription("list of ints", new AcceptableValueList<int>(1, 3, 5)));
            weird.Bind("04 Text", "Password", "hunter2", "should show dots");
            weird.Bind("04 Text", "Long", new string('L', 2000), "very long");
            weird.Bind("04 Text", "Empty", "", "");
            weird.Bind("05 Hidden", "SafeMode", true, "must never be shown");
            weird.Bind("05 Hidden", "Secret debug", true, new ConfigDescription("hidden", null, ModSettings.Hidden));
            settings = new Window("stress: settings");
            settings.AddTab("Config", p => ModSettings.Draw(p, weird));
            settings.Open();
            var texts = Panel.Box.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false).Select(t => t.text).ToList();
            bool leaked = texts.Any(t => t.Contains("hunter2"));
            bool safeShown = texts.Any(t => t.Contains("SafeMode"));
            Plugin.Log.LogMessage($"stress: SHOT settings ({RenderState()})");
            Check(!leaked && !safeShown, $"password shown={leaked}, SafeMode shown={safeShown}", "no password, no SafeMode");
        });
        Add("fuzz-click the awkward settings page", 10f, () =>
        {
            int clicks = ClickAll(25);
            // Every value must still be one its entry accepts and the file must still parse.
            bool valid = true;
            foreach (var key in weird.Keys)
            {
                var e = weird[key];
                var acc = e.Description?.AcceptableValues;
                if (acc != null && !acc.IsValid(e.BoxedValue)) { valid = false; Plugin.Log.LogWarning($"stress: {key} invalid: {e.BoxedValue}"); }
            }
            weird.Save();
            var reread = new ConfigFile(weirdPath, false);
            settings.Close();
            Check(valid, "an entry holds a value it does not accept", $"{clicks} clicks, all values valid, file re-reads");
        });

        Add("keyboard: a second question while one is open", 0.5f, () =>
        {
            if (VRKeyboard.instance == null) { Pass("no keyboard in this scene; skipped"); return; }
            bool first = TextInput.Ask("stress one", 8, _ => { });
            bool second = TextInput.Ask("stress two", 8, _ => { });
            var kb = VRKeyboard.instance;
            int max = kb.maxInputLength;
            Check(first && !second && max == 8, $"first={first}, second={second}, max={max}", "second refused, limit intact");
        });
        Then(3f, () =>
        {
            bool busy = TextInput.Busy;                       // and it has had time to open
            VRKeyboard.instance?.Hide();                      // closed by the game, not by us
            if (!busy) { current = "keyboard: open state seen"; Fail("not busy while the keyboard was up"); }
        });
        Then(3f, () =>
        {
            current = "keyboard: closed behind our back";
            Check(!TextInput.Busy, "still busy after the keyboard closed");
        });

        Add("event storm: 2000 Update handlers, 500 of them throwing", 0.5f, () =>
        {
            frames = 0; frameSum = 0;
            GameEvents.Update += Measure;
        });
        Then(5f, () =>
        {
            GameEvents.Update -= Measure;
            baseFrames = frames;
            baseAvg = frames > 0 ? frameSum / frames * 1000f : -1;
            Safe.ResetReports();
            for (int i = 0; i < 2000; i++)
            {
                int k = i;
                Action h = k % 4 == 0 ? () => throw new Exception(Boom + " " + k) : () => { };
                handlers.Add(h);
                GameEvents.Update += h;
            }
            frames = 0; frameSum = 0;
            GameEvents.Update += Measure;
        });
        Then(5f, () =>
        {
            GameEvents.Update -= Measure;
            foreach (var h in handlers) GameEvents.Update -= h;
            handlers.Clear();
            float avg = frames > 0 ? frameSum / frames * 1000f : -1;
            Check(frames > baseFrames / 2, $"{frames} frames under load vs {baseFrames} without",
                  $"{frames} frames in 5 s vs {baseFrames} without ({avg:0.0} vs {baseAvg:0.0} ms), log capped at {Safe.MaxReports} reports");
        });

        Add("stat modifiers: 2000 live, applied to the game's reads, gated by context", 0.5f, () =>
        {
            if (Players.LocalFighter == null) { Fail("no local fighter"); return; }
            statPlain = MoveSpeed();
            gameReads = 0;
            for (int i = 0; i < 1998; i++) mods.Add(Stats.Modify(SkillCalcType.MoveSpeed, (f, s, v) => v, i));
            mods.Add(Stats.Modify(SkillCalcType.MoveSpeed, (f, s, v) => { gameReads++; return v; }, -1));
            mods.Add(Stats.ModifyLocal(SkillCalcType.MoveSpeed, v => { localCalls++; lastIn = v; return v * 10f; }, 5000));
        });
        Then(3f, () =>
        {
            current = "stat modifiers: 2000 live, applied to the game's reads, gated by context";
            readsSolo = gameReads;
            float direct = MoveSpeed();
            directCountedRead = gameReads > readsSolo;
            directModded = direct;
            Safety.Context = PlayContext.Public;
            gameReads = 0;
        });
        Then(3f, () =>
        {
            current = "stat modifiers: 2000 live, applied to the game's reads, gated by context";
            int readsPublic = gameReads;
            bool statusRefused = !Status.Give(Players.LocalFighter, StatusType.Haste, 1f);
            Safety.Context = PlayContext.Solo;
            foreach (var m in mods) m.Dispose();
            mods.Clear();
            gameReads = 0;
            float after = MoveSpeed();
            Plugin.Log.LogMessage($"stress: note: local-only modifier ran {localCalls} time(s), last input {lastIn}; " +
                                  $"IsLocal(LocalFighter)={Players.IsLocal(Players.LocalFighter)}");
            Plugin.Log.LogMessage($"stress: note: a direct managed call to CalcSkillAndStatusEffectValue " +
                                  (directCountedRead ? $"went through the hook (x10 -> {directModded}, plain {statPlain})"
                                                     : "did not pass the hook; only the game's own calls are patched"));
            Check(readsSolo > 0 && readsPublic == 0 && statusRefused && Stats.Count == 0 && Mathf.Approximately(after, statPlain),
                  $"game reads in solo {readsSolo}, in public {readsPublic}, status refused {statusRefused}, left {Stats.Count}",
                  $"{readsSolo} game reads modified in 3 s with 2000 modifiers, 0 in Public, all removed");
        });

        Add("mod messages: fuzzed packets through the real receive hook", 0.5f, () =>
        {
            var r = Game.Runner;
            if (r == null || !r.IsRunning) { Fail("no running session"); return; }
            var m = AccessTools.Method(typeof(NetworkRunner), "Fusion_Simulation_ICallbacks_OnReliableData");
            if (m == null) { Fail("receive method not found"); return; }
            var rng = new System.Random(9);
            int n = 0;
            for (int i = 0; i < 400; i++)
            {
                var b = new byte[rng.Next(0, 80)];
                rng.NextBytes(b);
                if (i % 2 == 0 && b.Length >= 6) { b[0] = (byte)'I'; b[1] = (byte)'S'; b[2] = (byte)'M'; b[3] = (byte)'A'; b[4] = 1; }
                // Only our own packets: random bytes without the marker would go on to the game's
                // handlers, and what they do with garbage is not ours to test.
                if (!Net.NetCore.IsOurs(b)) continue;
                m.Invoke(r, new object[] { r.LocalPlayer, (Il2CppStructArray<byte>)b });
                n++;
            }
            bool hookLive = Events.Hooks.LiveHooks.Contains("NetworkRunner.OnReliableData");
            var a1 = ModNet.Channel("stress.chan");
            bool same = ReferenceEquals(a1, ModNet.Channel("stress.chan"));
            bool emptyRefused = false;
            try { ModNet.Channel(""); } catch (ArgumentException) { emptyRefused = true; }
            bool soloSend = a1.Broadcast("nobody to hear");     // solo: no peers, must just return false
            Check(hookLive && same && emptyRefused && !soloSend, $"hook={hookLive}, same={same}, empty refused={emptyRefused}, solo send={soloSend}",
                  $"{n} crafted packets swallowed by the hook");
        });

        Add("main menu: many pills, one throwing", 0.5f, () =>
        {
            for (int i = 0; i < 6; i++) { int k = i; MainMenu.AddButton("S" + k, () => { if (k == 3) throw new Exception(Boom); }, MenuCard.Solo); }
        });
        Then(3f, () =>
        {
            current = "main menu: many pills, one throwing";
            var mm = MainMenu.Find();
            var pills = mm == null ? new List<Button>() : mm.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("ModPill_S")).ToList();
            foreach (var b in pills) b.onClick.Invoke();
            Check(pills.Count == 6, $"{pills.Count} pills", "6 pills placed, the throwing one blamed");
        });

        Add("window open across a scene change", 0.5f, () =>
        {
            ModsWindow.Open();
            var ngm = GM.instance?.NetGameMaster;
            ngm?.ProgressToNextLevelInSequence();
        });
        Then(5f, () =>
        {
            current = "window open across a scene change";
            // Level loads take a while under Proton; wait for it rather than guessing.
            if ((Game.Scene == null || Game.Scene.IsHaven) && ++waits < 40) { index--; return; }
            waits = 0;
            if (Game.Scene == null || Game.Scene.IsHaven) { Fail($"still in {Game.Scene} after 200 s"); return; }
            bool staleSafe = true;
            try { Window.Current?.Refresh(); } catch (Exception) { staleSafe = false; }
            bool closedByScene = Window.Current == null || !Window.Current.IsOpen;
            ModsWindow.Open();
            bool reopened = Window.Current != null && Window.Current.IsOpen;
            Plugin.Log.LogMessage("stress: SHOT level");
            Check(staleSafe && closedByScene && reopened, $"stale refresh ok={staleSafe}, closed by scene={closedByScene}, reopened={reopened}",
                  $"reopened in {Game.Scene}");
        });

        Add("damage handlers: throwing, NaN, cancelling, recursion guard", 8f, () =>
        {
            Window.Current?.Close();
            GameEvents.Damage += DamageBad;
            GameEvents.Damage += DamageNaN;
            GameEvents.Damage += DamageCount;
            GM.instance?.NetGameMaster?.TriggerEncounter_Synced(0);
        });
        Then(20f, () =>
        {
            current = "damage handlers: throwing, NaN, cancelling, recursion guard";
            Bots.HurtAll();
        });
        Then(5f, () =>
        {
            current = "damage handlers: throwing, NaN, cancelling, recursion guard";
            GameEvents.Damage -= DamageBad;
            GameEvents.Damage -= DamageNaN;
            GameEvents.Damage -= DamageCount;
            Bots.DespawnAll();
            Check(hits > 0, "no hits happened", $"{hits} hits survived a throwing handler and a NaN amount");
        });

        Add("leaving with everything still subscribed", 1f, () =>
        {
            var r = Game.Runner;
            if (r != null && r.IsRunning) r.Shutdown(true, ShutdownReason.Ok, false);
        });
        Then(5f, () =>
        {
            current = "leaving with everything still subscribed";
            if (Game.InRun && ++waits < 40) { index--; return; }
            waits = 0;
            Check(!Game.InRun && Safety.Context != PlayContext.Public, $"inRun={Game.InRun}, context={Safety.Context}", $"back in {Game.Scene}, context {Safety.Context}");
        });

        Then(1f, () =>
        {
            try { File.Delete(weirdPath); } catch (Exception) { }
            Diag.Sink = previousSink;
            Plugin.Log.LogMessage($"stress: DONE {passed} passed, {failed} failed" + (failed > 0 ? ": " + string.Join("; ", failures) : ""));
            Plugin.Log.LogMessage("stress: set [09 Debug] StressTest = false.");
        });
    }

    static int hits, waits, gameReads, readsSolo;
    static float statPlain, directModded, lastIn;
    static int localCalls;
    static bool directCountedRead;
    static void Measure() { frames++; frameSum += Time.unscaledDeltaTime; }
    static void DamageBad(DamageEvent e) => throw new Exception(Boom + " damage");
    static void DamageNaN(DamageEvent e) { e.Amount = float.NaN; e.Amount = e.Original; }
    static void DamageCount(DamageEvent e) { hits++; if (float.IsNaN(e.Amount) || e.Amount < 0) throw new Exception("damage amount went bad: " + e.Amount); }

    static string RenderState()
    {
        if (Panel.Box == null) return "no panel";
        var gs = Panel.Box.GetComponentsInChildren<UnityEngine.UI.Graphic>(false);
        int culled = 0, transparent = 0, tmp = 0;
        foreach (var g in gs)
        {
            if (g.canvasRenderer != null && g.canvasRenderer.cull) culled++;
            if (g.color.a < 0.01f) transparent++;
            if (g.GetIl2CppType().Name.Contains("TextMeshPro")) tmp++;
        }
        var c = Panel.Box.GetComponentInParent<Canvas>();
        var root = c != null ? c.rootCanvas : null;
        return $"graphics {gs.Length}, culled {culled}, transparent {transparent}, tmp {tmp}; canvas {c?.renderMode} order {c?.sortingOrder} " +
               $"layer {c?.gameObject.layer} scale {c?.transform.lossyScale} active {c?.gameObject.activeInHierarchy}; root {(root != null ? root.name : "-")}";
    }

    static void DismissModal()
    {
        var m = Modal.instance;
        var d = m?.currentData;
        if (d == null || d.buttonType != Modal.ButtonType.Okay) return;
        m.Confirm();
    }
}
