using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using EmpireCraft.Core;
using EmpireCraft.UI;
using Orbis.Flat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Action = System.Action;

namespace EmpireCraft.EditorTools
{
    // Command-line capture of the civilization HUD on the EmpireCraft core:
    //   Unity.exe -batchmode -projectPath <p> -executeMethod EmpireCraft.EditorTools.EmpireCoreHudCapture.Run
    //             -ecCaptureOut <dir> [-ecCaptureYears 40]
    // Enters Play mode in Orbis2DWorld, lets the core run ahead the given years, then
    // shows the political map and the 列国 / 国务 / 战争 / 宗族 / 史书 windows and writes
    // each as a PNG (map camera with the HUD panel laid over it), plus the text of every
    // window in windows.txt. Exit code 0 = all views written.
    [InitializeOnLoad]
    public static class EmpireCoreHudCapture
    {
        const string Flag = "EmpireCoreHudCapture.Out";
        const string ScenePath = "Assets/ORBIS/Scenes/Orbis2DWorld.unity";
        const int Width = 1600, Height = 900;
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        static EmpireCoreHudCapture()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(Flag, ""))) EditorApplication.update += Step;
        }

        public static void Run()
        {
            string output = Argument("-ecCaptureOut") ?? Path.GetFullPath("ValidationOutput/EmpireHud");
            Directory.CreateDirectory(output);
            SessionState.SetString(Flag, output);
            SessionState.SetInt("EmpireCoreHudCapture.Years", int.TryParse(Argument("-ecCaptureYears"), out int years) ? years : 40);
            SessionState.SetFloat("EmpireCoreHudCapture.Start", (float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update += Step;
            EditorApplication.EnterPlaymode();
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        sealed class View { public string Name; public Action Setup; public int Frames = 75; }
        static List<View> views; static int viewIndex = -1, waited; static double ahead;
        static RenderTexture ui; static FlatWorldApp app; static StringBuilder texts;

        static object Field(string name) => typeof(FlatWorldApp).GetField(name, Private).GetValue(app);
        static void Call(string name, params object[] args)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i].GetType();
            var method = typeof(FlatWorldApp).GetMethod(name, Private, null, types, null);
            if (method == null) throw new MissingMethodException("FlatWorldApp." + name);
            method.Invoke(app, args);
        }
        static IHudWindow Window => Field("hudWindow") as IHudWindow;

        static void Step()
        {
            string output = SessionState.GetString(Flag, "");
            if (string.IsNullOrEmpty(output)) { EditorApplication.update -= Step; return; }
            double started = SessionState.GetFloat("EmpireCoreHudCapture.Start", 0);
            if (EditorApplication.timeSinceStartup - started > 2400) { Finish(output, "timeout", 3); return; }
            if (!EditorApplication.isPlaying) return;
            app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if (!app || app.World == null || app.Busy) return;
            var map = Field("map") as FlatMapRenderer;
            var nav = Field("navigation") as FlatScrollNavigator;
            var camera = Field("viewCamera") as Camera;
            var driver = EmpireCraft.Presentation.EmpireCoreDriver.Instance;
            if (!map || !nav || !camera || !driver || driver.World == null || !ReferenceEquals(driver.World.Map, app.World)) return;
            if (!map.RiverAtlasReady || map.PreparationPending) return;
            var core = driver.World;

            if (views == null)
            {
                // The HUD panel draws into a texture of its own, laid over the map camera.
                var panel = Field("panel") as PanelSettings;
                ui = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "HUD capture" };
                panel.targetTexture = ui; panel.clearColor = true; panel.colorClearValue = new Color(0, 0, 0, 0);
                texts = new StringBuilder();
                Realm largest = null;
                Func<Realm> Largest = () =>
                {
                    if (largest == null || !largest.Alive)
                        foreach (var r in core.LiveRealms) if (r.Alive && (largest == null || r.Cities.Count > largest.Cities.Count)) largest = r;
                    return largest;
                };
                views = new List<View>
                {
                    // The map alone first: the whole political atlas, then the largest realm close up, political and natural.
                    new View { Name = "0a_political_atlas", Frames = 120, Setup = () => { Call("ChooseLayer", MapLayer.Nations); nav.OpenAtlas(app.CalendarDay); } },
                    new View { Name = "0b_political_capital", Frames = 220, Setup = () => Call("FocusCoreTile", Largest().Capital.Tile) },
                    new View { Name = "1_nations", Setup = () => { Call("ChooseLayer", MapLayer.Nations); nav.OpenAtlas(app.CalendarDay); Call("OpenHudNations"); } },
                    new View { Name = "2_state_overview", Frames = 220, Setup = () => { Call("CloseHudWindow"); Call("FocusCoreRealm", Largest()); Call("OpenHudState"); } },
                    new View { Name = "3_state_finance", Frames = 30, Setup = () => Window.Show(1) },
                    new View { Name = "4_state_cities", Frames = 30, Setup = () => Window.Show(2) },
                    new View { Name = "5_state_lineage", Frames = 30, Setup = () => Window.Show(3) },
                    new View { Name = "6_state_foreign", Frames = 30, Setup = () => Window.Show(4) },
                    new View { Name = "7_state_culture", Frames = 30, Setup = () => Window.Show(5) },
                    new View { Name = "8_war_overview", Setup = () => { nav.OpenAtlas(app.CalendarDay); Call("OpenHudWar"); } },
                    new View { Name = "9_war_sides", Frames = 30, Setup = () => Window.Show(1) },
                    new View { Name = "10_war_reports", Frames = 30, Setup = () => Window.Show(2) },
                    new View { Name = "11_war_list", Frames = 30, Setup = () => Window.Show(3) },
                    new View { Name = "12_clan_overview", Setup = () => Call("OpenHudClans", 0) },
                    new View { Name = "13_clan_members", Frames = 30, Setup = () => Window.Show(1) },
                    new View { Name = "14_clan_kin", Frames = 30, Setup = () => Window.Show(2) },
                    new View { Name = "15_clan_houses", Frames = 30, Setup = () => Window.Show(3) },
                    new View { Name = "16_annals", Setup = () => Call("OpenCoreAnnals") },
                    new View { Name = "17_annals_news", Frames = 30, Setup = () => Window.Show(1) },
                    new View { Name = "10_globe_political", Frames = 150, Setup = () =>
                        {
                            // Daylight over the largest realm (the same sun placement as the HUD review shots).
                            var w = app.World; int tile = Largest().Capital.Tile;
                            double lon = ((tile % w.Width + .5) / w.Width - .5) * Math.PI * 2;
                            double fraction = (-lon + 35 * Math.PI / 180) / (Math.PI * 2) - .5; fraction -= Math.Floor(fraction);
                            w.Day = Math.Floor(w.Day) + fraction;
                            Call("CloseHudWindow"); nav.Home(app.CalendarDay);
                        } },
                };
            }

            // Run the core ahead: up to a year of months per frame, the way it catches up after a pause.
            int years = SessionState.GetInt("EmpireCoreHudCapture.Years", 40);
            if (core.Year < core.StartYear + years)
            {
                ahead += 360;
                driver.Runner?.Frame(app.World.Day + ahead);
                if (driver.Runner == null) { Finish(output, "core stopped while running ahead", 5); return; }
                return;
            }

            if (viewIndex < 0 || waited >= views[viewIndex].Frames)
            {
                if (viewIndex >= 0) Render(camera, nav, Path.Combine(output, views[viewIndex].Name + ".png"), views[viewIndex].Name);
                viewIndex++; waited = 0;
                if (viewIndex >= views.Count)
                {
                    int cities = 0; foreach (var c in core.Cities) if (c.Alive) cities++;
                    int land = 0, held = 0;
                    for (int t = 0; t < core.TileCity.Length; t++) { if (core.Settleable(t)) land++; if (core.TileCity[t] >= 0) held++; }
                    texts.Insert(0, "tiles held by cities=" + held + " of " + land + " settleable land tiles\n");
                    File.WriteAllText(Path.Combine(output, "windows.txt"),
                        "seed=" + app.World.Seed + " size=" + app.World.Width + "x" + app.World.Height + " year=" + core.Year +
                        " realms=" + core.LiveRealms.Count + " cities=" + cities + " wars=" + core.LiveWars.Count + "/" + core.Wars.Count +
                        " news=" + core.News.Posted + " annals=" + core.Annals.Entries.Count + "\n\n" + texts);
                    Finish(output, "done", 0); return;
                }
                try { views[viewIndex].Setup(); }
                catch (Exception e) { Debug.LogException(e); Finish(output, "setup failed: " + (e.InnerException ?? e).Message, 4); return; }
            }
            waited++;
        }

        static void Render(Camera camera, FlatScrollNavigator nav, string path, string name)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            try
            {
                camera.targetTexture = rt; nav.SetViewport(new Rect(0, 0, Width, Height)); nav.Tick(0, app.CalendarDay); nav.SyncMaterials();
                camera.Render();
                var scene = Read(rt); var hud = Read(ui);
                var a = scene.GetPixels32(); var b = hud.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                {
                    // The panel writes premultiplied colour: over = hud + scene × (1 − α).
                    float k = 1f - b[i].a / 255f;
                    a[i] = new Color32((byte)Mathf.Min(255, b[i].r + a[i].r * k), (byte)Mathf.Min(255, b[i].g + a[i].g * k), (byte)Mathf.Min(255, b[i].b + a[i].b * k), 255);
                }
                scene.SetPixels32(a); scene.Apply();
                File.WriteAllBytes(path, scene.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(scene); UnityEngine.Object.DestroyImmediate(hud);
                var window = Window;
                texts.Append("== ").Append(name).Append('\n');
                if (window != null) window.Root.Query<Label>().ForEach(l => { if (!string.IsNullOrWhiteSpace(l.text)) texts.Append(l.text.Replace('\n', ' ')).Append('\n'); });
                texts.Append('\n');
                Debug.Log("EmpireCoreHudCapture wrote " + path);
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); }
        }

        static Texture2D Read(RenderTexture source)
        {
            RenderTexture.active = source;
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply();
            return image;
        }

        static void Finish(string output, string result, int code)
        {
            EditorApplication.update -= Step; SessionState.EraseString(Flag);
            File.WriteAllText(Path.Combine(output, "result.txt"), result);
            Debug.Log("EmpireCoreHudCapture " + result);
            if (Application.isBatchMode) EditorApplication.Exit(code); else EditorApplication.ExitPlaymode();
        }
    }
}
