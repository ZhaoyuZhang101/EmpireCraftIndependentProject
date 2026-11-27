using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace EmpireCraft.EditorTools
{
    // Review shots of the civilization HUD over the real 2D world:
    //   Unity.exe -batchmode -projectPath <p> -executeMethod EmpireCraft.EditorTools.EmpireHudCapture.Run
    //             -ecHudOut <dir> [-ecHudThemes star,atlas,slate] [-ecHudShots observe,nations,shape,world,globe]
    // The camera renders into one texture and the UI Toolkit panel into another
    // (PanelSettings.targetTexture); the two are composited on the CPU, since a
    // batch-mode editor has no screen to grab.
    [InitializeOnLoad]
    public static class EmpireHudCapture
    {
        const string Flag = "EmpireHudCapture.Out";
        const int Width = 1440, Height = 900;
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly string[] ThemeKeys = { "star", "atlas", "slate" };

        static EmpireHudCapture() { if (!string.IsNullOrEmpty(SessionState.GetString(Flag, ""))) EditorApplication.update += Step; }

        public static void Run()
        {
            string output = Arg("-ecHudOut") ?? Path.GetFullPath("ValidationOutput/Hud");
            Directory.CreateDirectory(output);
            SessionState.SetString(Flag, output);
            SessionState.SetString("EmpireHudCapture.Themes", Arg("-ecHudThemes") ?? "star,atlas,slate");
            SessionState.SetString("EmpireHudCapture.Shots", Arg("-ecHudShots") ?? "observe,nations,shape,world,globe");
            SessionState.SetFloat("EmpireHudCapture.Start", (float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
            EditorApplication.update += Step;
            EditorApplication.EnterPlaymode();
        }

        static string Arg(string name) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return null; }

        static double lastStatus;
        static List<(int theme, string shot)> plan;
        static int index = -1, waited, tile = -1;
        static float surfaceZoom, globeZoom;
        static RenderTexture uiTarget;

        static void Step()
        {
            string output = SessionState.GetString(Flag, "");
            if (string.IsNullOrEmpty(output)) { EditorApplication.update -= Step; return; }
            double elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat("EmpireHudCapture.Start", 0);
            if (elapsed > 10800) { Finish(output, "timeout", 3); return; }
            if (!EditorApplication.isPlaying) return;
            var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            var map = app ? Get<FlatMapRenderer>(app, "map") : null;
            var nav = app ? Get<FlatScrollNavigator>(app, "navigation") : null;
            var camera = app ? Get<Camera>(app, "viewCamera") : null;
            if (elapsed - lastStatus > 120)
            {
                lastStatus = elapsed;
                Debug.Log($"EmpireHudCapture waiting {elapsed:F0}s world={(app && app.World != null)} atlas={(map && map.RiverAtlasReady)} frame={Time.frameCount} compiling={ShaderUtil.anythingCompiling}");
            }
            if (!app || app.World == null || !map || !nav || !camera || !map.RiverAtlasReady || map.PreparationPending) return;
            try
            {
                if (index < 0) { Begin(app, nav, map); return; }
                if (waited++ < 45) return;
                var (theme, shot) = plan[index];
                Render(app, camera, nav, Path.Combine(output, $"hud_{ThemeKeys[theme]}_{shot}.png"));
                waited = 0;
                if (++index >= plan.Count) { Finish(output, "done", 0); return; }
                Setup(app, nav, map);
            }
            catch (Exception e) { Debug.LogException(e); Finish(output, "error " + e.Message, 2); }
        }

        static void Begin(FlatWorldApp app, FlatScrollNavigator nav, FlatMapRenderer map)
        {
            plan = new List<(int, string)>();
            foreach (var t in SessionState.GetString("EmpireHudCapture.Themes", "star").Split(','))
            {
                int theme = Array.IndexOf(ThemeKeys, t.Trim());
                if (theme < 0) continue;
                foreach (var s in SessionState.GetString("EmpireHudCapture.Shots", "observe").Split(',')) plan.Add((theme, s.Trim()));
            }
            Set(app, "screenshotMode", true);
            Set(app, "paused", true);
            var w = app.World;
            tile = PickTile(w);
            // Morning sun over the chosen tile so relief and clouds read clearly.
            double lon = ((tile % w.Width + .5) / w.Width - .5) * Math.PI * 2;
            double fraction = (-lon + 35 * Math.PI / 180) / (Math.PI * 2) - .5; fraction -= Math.Floor(fraction);
            w.Day = Math.Floor(w.Day) + fraction;
            nav.Home(app.CalendarDay); globeZoom = (float)Field(nav, "zoom");
            nav.Focus(tile, app.CalendarDay); surfaceZoom = (float)Field(nav, "targetZoom");
            var panel = Get<PanelSettings>(app, "panel");
            uiTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "EmpireHudCapture UI" };
            panel.targetTexture = uiTarget;
            panel.clearColor = true;
            panel.colorClearValue = new Color(0, 0, 0, 0);
            index = 0; waited = 0;
            Setup(app, nav, map);
        }

        static void Setup(FlatWorldApp app, FlatScrollNavigator nav, FlatMapRenderer map)
        {
            var (theme, shot) = plan[index];
            Call(app, "CloseHudWindow");
            Call(app, "ResetHudLayout", false);
            Call(app, "ApplyHudTheme", theme);
            double day = app.CalendarDay;
            var location = PlanetCoordinateSystem.FromMapUV((tile % app.World.Width + .5) / app.World.Width, (tile / app.World.Width + .5) / app.World.Height);
            if (shot == "globe" || shot == "globe_far")
            {
                float z = shot == "globe_far" ? (float)PlanetScrollNavigationMath.ZoomFor(PlanetScrollNavigationMath.MaxAltitude) : globeZoom;
                nav.GoGlobe(day); nav.Recenter(location, day); SetZoom(nav, z, day);
            }
            else { nav.Focus(tile, day); SetZoom(nav, surfaceZoom, day); }
            Set(app, "selected", tile);
            map.Select(tile);
            switch (shot)
            {
                case "observe": Call(app, "SelectHudMode", 0); Call(app, "ShowHudCategory", 1); break;
                case "nations": Call(app, "SelectHudMode", 2); Call(app, "ShowHudCategory", 0); Call(app, "OpenHudNations"); break;
                case "shape":
                    Call(app, "SelectHudMode", 1); Call(app, "ShowHudCategory", 0);
                    var tool = Get<DropdownField>(app, "toolField"); if (tool != null) tool.index = 3;
                    break;
                case "world": Call(app, "SelectHudMode", 2); Call(app, "OpenHudWorld", 0); break;
                case "globe":
                case "globe_far": Call(app, "SelectHudMode", 0); break;
            }
            Call(app, "UpdateHUD");
            Call(app, "TickEmpireHud");
        }

        static void Render(FlatWorldApp app, Camera camera, FlatScrollNavigator nav, string path)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            try
            {
                camera.targetTexture = rt; nav.SetViewport(new Rect(0, 0, Width, Height));
                nav.Tick(0, app.CalendarDay); nav.SyncMaterials(); camera.Render();
                var world = Read(rt);
                var ui = Read(uiTarget);
                var a = world.GetPixels32(); var b = ui.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                {
                    // UI Toolkit writes premultiplied colour over a transparent clear.
                    int k = 255 - b[i].a;
                    a[i] = new Color32((byte)Math.Min(255, b[i].r + a[i].r * k / 255), (byte)Math.Min(255, b[i].g + a[i].g * k / 255),
                        (byte)Math.Min(255, b[i].b + a[i].b * k / 255), 255);
                }
                world.SetPixels32(a); world.Apply();
                File.WriteAllBytes(path, world.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(world); UnityEngine.Object.DestroyImmediate(ui);
                Debug.Log("EmpireHudCapture wrote " + path);
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); }
        }

        static Texture2D Read(RenderTexture source)
        {
            RenderTexture.active = source;
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            return image;
        }

        static int PickTile(WorldData w)
        {
            // A river mouth in the mid-latitudes: coast, river and land in one frame.
            int best = -1; float flow = 0;
            for (int i = 0; i < w.Count; i++)
            {
                if (w.Water[i] != WaterKind.River || w.CoastDistance[i] > 2) continue;
                float lat = Math.Abs(w.Latitude(i)); if (lat < 15 || lat > 50) continue;
                if (w.Runoff[i] > flow) { flow = w.Runoff[i]; best = i; }
            }
            return best < 0 ? w.Count / 2 : best;
        }

        static T Get<T>(object o, string name) where T : class => o.GetType().GetField(name, Private)?.GetValue(o) as T;
        static object Field(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);
        static void Set(object o, string name, object value) => o.GetType().GetField(name, Private)?.SetValue(o, value);
        static void Call(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethod(name, Private);
            if (m == null) throw new MissingMethodException(o.GetType().Name, name);
            m.Invoke(o, args);
        }
        static void SetZoom(FlatScrollNavigator nav, float z, double day)
        {
            var t = typeof(FlatScrollNavigator);
            t.GetField("zoom", Private).SetValue(nav, z); t.GetField("targetZoom", Private).SetValue(nav, z);
            t.GetField("zoomVelocity", Private)?.SetValue(nav, 0f); nav.Tick(0, day);
        }

        static void Finish(string output, string result, int code)
        {
            EditorApplication.update -= Step; SessionState.EraseString(Flag);
            File.WriteAllText(Path.Combine(output, "result.txt"), result);
            Debug.Log("EmpireHudCapture " + result);
            if (Application.isBatchMode) EditorApplication.Exit(code); else EditorApplication.ExitPlaymode();
        }
    }
}
