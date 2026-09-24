#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Orbis.Flat;

namespace Orbis.EditorTools
{
    /// <summary>Explicit local verification. These checks were not run in the delivery environment.</summary>
    public static class FlatScrollValidation
    {
        [Serializable] sealed class Report
        {
            public string utc, scope, unityVersion, graphicsDevice, status;
            public string[] checks, failures;
        }
        static void Require(bool condition, string label)
        { if (!condition) throw new InvalidOperationException(label); }
        [MenuItem("Tools/ORBIS/Perf3.3/1 Run calendar and wrapping tests")]
        public static void Numerical()
        {
            var checks = new List<string>(); var failures = new List<string>();
            Action<string, Action> test = (name, action) => { try { action(); checks.Add(name); } catch (Exception e) { failures.Add(name + ": " + e.Message); } };
            test("20x / 1x exactly proportional", () =>
            {
                double[] days = new double[2];
                for (int mode = 0; mode < 2; mode++)
                {
                    int capture = mode; var clock = new FlatSolarClock();
                    for (int frame = 0; frame < 600; frame++) clock.Advance(1.0 / 60, mode == 0 ? 1 : 20, 120, false, d => days[capture] += d);
                    days[mode] = clock.RenderDay(days[mode]);
                }
                Require(Math.Abs(days[1] / days[0] - 20) < 1e-5, "speed ratio mismatch");
            });
            test("Pause freezes calendar; save flush preserves phase", () =>
            {
                double day = 19.99; var clock = new FlatSolarClock(); Action<float> step = d => day += d;
                clock.Advance(.037, 20, 120, false, step); double before = clock.RenderDay(day);
                clock.Advance(120, 20, 120, true, step); Require(Math.Abs(clock.RenderDay(day) - before) < 1e-10, "pause advanced time");
                clock.Flush(step); Require(Math.Abs(day - before) < 1e-7, "flush lost fractional day");
                double phase = FlatSolarClock.Phase(day); clock.Reset(); Require(Math.Abs(phase - FlatSolarClock.Phase(clock.RenderDay(day))) < 1e-8, "load changed phase");
            });
            test("Long frame retains calendar debt", () =>
            {
                double day = 0; var clock = new FlatSolarClock(); clock.Advance(7.25, 20, 30, false, d => day += d);
                Require(Math.Abs(clock.RenderDay(day) - 7.25 * 20 / 30) < 1e-6, "dropped time");
            });
            test("Longitude wrap, poles and shortest route", () =>
            {
                foreach (int w in new[] { 256, 512, 1024 })
                {
                    int h = w / 2, row = h / 2;
                    Require(FlatScrollMath.Tile(-.5, row + .5, w, h) == row * w + w - 1, "west seam");
                    Require(FlatScrollMath.Tile(w + .5, row + .5, w, h) == row * w, "east seam");
                    Require(FlatScrollMath.Tile(.5, -1, w, h) == -1 && FlatScrollMath.Tile(.5, h, w, h) == -1, "poles wrapped");
                    Require(Math.Abs(FlatScrollMath.Shortest(.5 - (w - .5), w) - 1) < 1e-9, "route crosses entire atlas");
                    for (int laps = -20; laps <= 20; laps++) Require(FlatScrollMath.Tile(13.5 + w * laps, row + .5, w, h) == row * w + 13, "lap changes ID");
                }
            });
            Save("numeric-unity", "Executed C# calendar and wrapping checks; not GPU/visual validation.", checks, failures);
        }
        [MenuItem("Tools/ORBIS/Perf3.3/2 Report shader import messages")]
        public static void ShaderMessages()
        {
            var checks = new List<string>(); var failures = new List<string>();
            foreach (string path in new[] { "ORBIS/FlatWorld/ScrollSurface", "ORBIS/Surface", "ORBIS/Clouds" })
            {
                Shader shader = Resources.Load<Shader>(path);
                if (!shader) { failures.Add(path + " missing"); continue; }
                if (!shader.isSupported) failures.Add(path + " unsupported on active device");
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                {
                    string text = path + " " + message.severity + " " + message.file + ":" + message.line + " " + message.message;
                    if (message.severity.ToString() == "Error") failures.Add(text); else checks.Add(text);
                }
                checks.Add(path + " inspected (not every keyword/platform variant)");
            }
            Save("shader-import", "Current Unity shader import messages and isSupported only; not an all-variant compilation certificate.", checks, failures);
        }
        static FlatWorldApp app; static FlatScrollNavigator nav; static Camera camera;
        static FieldInfo pausedField; static bool wasPaused;
        static int stage, frames; static double deadline; static int center;
        static List<string> playChecks, playFailures;
        [MenuItem("Tools/ORBIS/Perf3.3/3 Run viewport smoke test in Play Mode")]
        public static void PlaySmoke()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("Open Orbis2DWorld.unity and enter Play Mode first."); return; }
            app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if (!app || app.World == null || app.Busy || !app.Navigation) { Debug.LogWarning("Wait until this world's generation/import has completed."); return; }
            if (!EditorUtility.DisplayDialog("ORBIS viewport verification", "此检查会暂时暂停模拟并改变视角、平移位置和路径预览；建议在工程副本运行。不会修改地形、文明或存档。", "运行", "取消")) return;
            EditorApplication.update -= Step;
            nav = app.Navigation; camera = app.GetComponentInChildren<Camera>();
            pausedField = typeof(FlatWorldApp).GetField("paused", BindingFlags.Instance | BindingFlags.NonPublic);
            wasPaused = (bool)pausedField.GetValue(app); pausedField.SetValue(app, true);
            playChecks = new List<string>(); playFailures = new List<string>(); stage = 0; frames = 0;
            deadline = EditorApplication.timeSinceStartup + 20;
            nav.GoGlobe(app.CalendarDay); EditorApplication.update += Step;
        }
        static void Step()
        {
            try
            {
                Require(EditorApplication.isPlaying && app && nav, "Play Mode ended or world replaced");
                Require(EditorApplication.timeSinceStartup < deadline, "viewport state timed out");
                if (stage == 0 && nav.IsGlobe)
                { playChecks.Add("Globe endpoint reached"); nav.OpenAtlas(app.CalendarDay); stage = 1; }
                else if (stage == 1 && nav.IsFlat)
                {
                    Require(camera.orthographic, "flat camera is not orthographic");
                    center = nav.Pick(camera.pixelRect.center); Require(center >= 0, "center did not resolve to a tile");
                    playChecks.Add("Atlas endpoint and geographical picking"); nav.PanBy(new Vector2(app.World.Width * 3, 0)); frames = 0; stage = 2;
                }
                else if (stage == 2 && ++frames >= 4)
                {
                    Require(nav.Pick(camera.pixelRect.center) == center, "three laps changed the authoritative tile");
                    playChecks.Add("Three horizontal wraps preserve selected Tile ID");
                    nav.PanBy(new Vector2(-app.World.Width * 7, 0)); frames = 0; stage = 3;
                }
                else if (stage == 3 && ++frames >= 4)
                {
                    Require(nav.Pick(camera.pixelRect.center) == center, "negative wraps changed the tile");
                    playChecks.Add("Negative longitude wraps preserve Tile ID"); nav.GoGlobe(app.CalendarDay); stage = 4;
                }
                else if (stage == 4 && nav.Morph < .70f && nav.Morph > 0)
                { nav.Toggle(app.CalendarDay); playChecks.Add("Reverse requested during mesh transition"); stage = 5; }
                else if (stage == 5 && nav.IsFlat)
                { Require(nav.Pick(camera.pixelRect.center) == center, "reverse did not preserve region"); playChecks.Add("Reverse returns to the same region"); Finish(); }
            }
            catch (Exception e) { playFailures.Add(e.ToString()); Finish(); }
        }
        static void Finish()
        {
            EditorApplication.update -= Step;
            if (app && pausedField != null) pausedField.SetValue(app, wasPaused);
            Save("viewport-playmode", "Automated navigation API smoke test; visual quality, real mouse input, frame rate and full-atmosphere alignment still require manual inspection.", playChecks, playFailures);
        }
        static void Save(string name, string scope, List<string> checks, List<string> failures)
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Validation", "Perf3_3"); Directory.CreateDirectory(dir);
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), scope = scope, unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName, status = failures.Count == 0 ? "PASS" : "FAIL", checks = checks.ToArray(), failures = failures.ToArray() };
            string path = Path.Combine(dir, name + ".json"); File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (failures.Count == 0) Debug.Log("ORBIS " + name + " PASS within stated scope: " + path); else Debug.LogError("ORBIS " + name + " FAIL: " + path);
        }
    }
}
#endif
