using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Orbis.Flat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbis.Editor
{
    // Run in a separate Editor with -batchmode -force-d3d11 -executeMethod Orbis.Editor.SolarLensRegression.Run
    // and optionally -orbisLensOutput <directory>. The driver exits after the generated world is ready.
    [InitializeOnLoad]
    public static class SolarLensRegression
    {
        const string OutputKey = "Orbis.SolarLens.Output";
        const string ExitKey = "Orbis.SolarLens.Exit";
        const int Width = 960, Height = 540;
        const float Day = 0;
        enum SceneSetup { Orbit, BehindCamera, Offscreen, Thematic, BelowCloudDeck, ClearSky, Foreground }
        static readonly List<string> results = new List<string>();
        static string output;
        static FlatWorldApp app;
        static Camera camera;
        static FlatGlobePresenter globe;
        static OrbisAtmosphereRenderer atmosphere;
        static OrbisCelestialLens lens;
        static FlatScrollNavigator nav;
        static FlatMapRenderer map;
        static bool failed;

        static SolarLensRegression()
        {
            if (SessionState.GetString(OutputKey, "") != "") EditorApplication.update += WaitForWorld;
            if (SessionState.GetInt(ExitKey, -1) >= 0) EditorApplication.update += ExitAfterPlay;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run the solar lens regression in a separate batch Editor.");
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-orbisLensOutput");
            output = index >= 0 ? args[index + 1] : Path.GetFullPath("ValidationOutput/SolarLens");
            Directory.CreateDirectory(output);
            SessionState.SetString(OutputKey, output);
            SessionState.SetFloat(OutputKey + ".start", (float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
            EditorApplication.update -= WaitForWorld;
            EditorApplication.update += WaitForWorld;
            EditorApplication.EnterPlaymode();
        }

        static void WaitForWorld()
        {
            output = SessionState.GetString(OutputKey, "");
            if (output == "") return;
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(OutputKey + ".start", 0) > 240)
            {
                Finish("Timed out waiting for the world and regional weather.", 2);
                return;
            }
            if (!EditorApplication.isPlaying) return;
            app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if (!app) return;
            typeof(FlatWorldApp).GetField("paused", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(app, true);
            if (app.World == null) return;
            map = app.GetComponentInChildren<FlatMapRenderer>();
            if (!map || !map.Sky.Ready || !map.Sky.RegionalClimateStep.HasValue || map.PreparationPending) return;
            EditorApplication.update -= WaitForWorld;
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                BindWorld();
                results.Add(Application.unityVersion + " " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
                results.Add("seed=" + app.World.Seed + " weatherDay=" + app.World.Day + " size=" + app.World.Width + "x" + app.World.Height);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-orbisVolumeLight") >= 0)
                    VolumeLightRegression.Capture(camera, globe, nav, map, atmosphere, lens, output);
                CheckCamera();
                // World replacement reuses the lens but installs a new atmosphere on the same camera.
                nav.Home(Day); nav.Tick(3, Day);
                double original = Pair("original-clear-sky", 1.17f, false, false, SceneSetup.ClearSky);
                typeof(FlatWorldApp).GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(app, new object[] {app.World});
                BindWorld();
                var rebuilt = Pair("rebuilt-clear-sky", 1.17f, false, false, SceneSetup.ClearSky);
                Check(original > .0005 && Math.Abs(rebuilt / original - 1) < .15, "regenerated clear sky preserves lens response", rebuilt / original);
                Finish("Completed", failed ? 1 : 0);
            }
            catch (Exception e) { Finish(e.ToString(), 2); }
            finally { ShaderUtil.allowAsyncCompilation = asynchronous; }
        }

        static void BindWorld()
        {
            camera = app.GetComponentInChildren<Camera>();
            lens = camera.GetComponent<OrbisCelestialLens>();
            globe = lens.view.GetComponent<FlatGlobePresenter>();
            atmosphere = Array.Find(camera.GetComponents<OrbisAtmosphereRenderer>(), a => a.enabled && a.view == lens.view);
            nav = app.Navigation;
            map = app.GetComponentInChildren<FlatMapRenderer>();
            nav.Home(Day);
            nav.Tick(3, Day);
            nav.Tick(3, Day);
            globe.Snapshot.settings.cloudQuality = 0;
            globe.TickScroll(0, 0, Day, nav.DisplaySpin, nav.Morph, true, true, true, true, Vector3.right, true);
        }

        static void CheckCamera()
        {
            foreach (bool full in new[] {false, true})
            {
                atmosphere.fullResolutionReference = full;
                string mode = full ? "full" : "scaled";
                double sky = Pair(mode + "-sky", 1.17f, false, !full);
                double behind = Pair(mode + "-hidden", .92f, false, false);
                double limb = Pair(mode + "-limb", 1.006f, false, !full);
                double clear = Pair(mode + "-clear", 1.013f, false, false);
                double cloud = Pair(mode + "-cloud", 1.013f, true, !full);
                double gap = Pair(mode + "-clear-column", 0, false, false, SceneSetup.BelowCloudDeck);
                double deck = Pair(mode + "-under-deck", 0, true, false, SceneSetup.BelowCloudDeck);
                Check(sky > .0005, mode + " off-axis sun produces glare", sky);
                Check(behind < .00002, mode + " planet fully occludes glare", behind);
                Check(limb > behind && limb < sky, mode + " grazing sun partially attenuated", limb);
                Check(clear > .0001 && cloud > 0 && cloud < clear * .65, mode + " partly clouded limb / clear limb", cloud / Math.Max(.000001, clear));
                Check(gap > .0005 && deck < gap * .04, mode + " opaque HP deck / clear column", deck / Math.Max(.000001, gap));
            }
            atmosphere.fullResolutionReference = false;
            ExpectHidden("behind-camera", 0, SceneSetup.BehindCamera);
            ExpectHidden("outside", 0, SceneSetup.Offscreen);
            ExpectHidden("thematic", 1.17f, SceneSetup.Thematic);
            ExpectHidden("foreground-occluder", 1.17f, SceneSetup.Foreground);
            double portrait = Pair("portrait", 1.17f, false, false, width: 432);
            Check(portrait > .0005, "portrait viewport", portrait);
            nav.OpenAtlas(Day);
            ExpectHidden("atlas", 1.17f, SceneSetup.Orbit);
            nav.GoGlobe(Day);
            nav.Focus(app.World.Count / 2, Day);
            nav.Tick(3, Day);
            Check(nav.IsFlat && Pair("local", 1.17f, false, false) < .00002, "unfolded local map", nav.Morph);
        }

        static void ExpectHidden(string name, float impact, SceneSetup mode)
        {
            double difference = Pair(name, impact, false, false, mode);
            Check(difference < .00002, name, difference);
        }

        // Compare paired renders during one update, with identical weather, clocks, camera and exposure.
        // The seeded HP fixture uses the production density function and an optically thick weather column.
        static double Pair(string name, float impact, bool storm, bool save, SceneSetup mode = SceneSetup.Orbit, int width = Width, int height = Height)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var active = RenderTexture.active;
            var previous = camera.targetTexture;
            int mask = camera.cullingMask;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            GameObject blocker = null;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                nav.SetViewport(new Rect(0, 0, width, height));
                nav.Tick(0, Day);
                Vector3 inward = (globe.transform.position - camera.transform.position).normalized;
                Vector3 sideways = (camera.transform.right * .72f + camera.transform.up * .69f).normalized;
                float angle = Mathf.Asin(Mathf.Clamp01(impact / Vector3.Distance(camera.transform.position, globe.transform.position)));
                Vector3 direction = inward * Mathf.Cos(angle) + sideways * Mathf.Sin(angle);
                if (mode == SceneSetup.BehindCamera) direction = -camera.transform.forward;
                if (mode == SceneSetup.Offscreen) direction = (camera.transform.forward * .15f + camera.transform.right).normalized;
                if (mode == SceneSetup.ClearSky) camera.cullingMask = 0;
                if (mode == SceneSetup.BelowCloudDeck)
                {
                    // Looking out through the entire HP layer. Every part of the solar disc is behind it.
                    camera.cullingMask = 0;
                    camera.transform.SetPositionAndRotation(globe.transform.position - inward * 1.0004f,
                        Quaternion.LookRotation(-inward, camera.transform.up));
                    direction = camera.ViewportPointToRay(new Vector3(.73f, .69f)).direction;
                }
                if (mode == SceneSetup.Foreground)
                {
                    blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    blocker.transform.position = camera.transform.position + direction * .7f;
                    blocker.transform.localScale = Vector3.one * .045f;
                }
                var sun = globe.transform.InverseTransformVector(direction).normalized;
                bool volume = mode != SceneSetup.ClearSky;
                globe.TickScroll(0, 0, Day, nav.DisplaySpin, nav.Morph, volume, volume, true, true, sun, mode != SceneSetup.Thematic);
                // Both pictures use the same world and surface lighting.
                map.Sky.Bind(map.Material, sun, true, true, true, true, false, false, true, 0);
                map.Material.SetFloat("_WaterClock", 0);
                nav.SyncMaterials();
                var clouds = globe.View.CloudMaterial;
                if (mode == SceneSetup.BelowCloudDeck) clouds.SetFloat("_ScrollHeightScale", 0);
                if (storm)
                {
                    clouds.SetTexture("_ScrollWeatherTex", Texture2D.whiteTexture);
                    clouds.SetFloat("_ScrollCloudSource", 1);
                    clouds.SetFloat("_Density", 45);
                    clouds.SetFloat("_Cover", 1);
                }
                else if (impact < 1.1f) clouds.SetFloat("_Clouds", 0);
                lens.strength = 0;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                var off = image.GetPixels32();
                lens.strength = 1.15f;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                if (save) File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
                var on = image.GetPixels32();
                double difference = 0;
                for (int i = 0; i < on.Length; ++i)
                    difference += Math.Abs(on[i].r - off[i].r) + Math.Abs(on[i].g - off[i].g) + Math.Abs(on[i].b - off[i].b);
                difference /= on.Length * 765.0;
                results.Add(name + " mean absolute lens difference=" + difference.ToString("F7"));
                return difference;
            }
            finally
            {
                camera.targetTexture = previous;
                camera.cullingMask = mask;
                camera.transform.SetPositionAndRotation(position, rotation);
                if (blocker) UnityEngine.Object.DestroyImmediate(blocker);
                RenderTexture.active = active;
                UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static void Check(bool ok, string description, double value)
        {
            failed |= !ok;
            results.Add((ok ? "PASS " : "FAIL ") + description + "  " + value.ToString("G5"));
        }

        static void Finish(string status, int code)
        {
            EditorApplication.update -= WaitForWorld;
            SessionState.EraseString(OutputKey);
            results.Add(status);
            File.WriteAllLines(Path.Combine(output, "results.txt"), results);
            SessionState.SetInt(ExitKey, code);
            EditorApplication.update += ExitAfterPlay;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        static void ExitAfterPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            EditorApplication.update -= ExitAfterPlay;
            int code = SessionState.GetInt(ExitKey, 2);
            SessionState.EraseInt(ExitKey);
            EditorApplication.Exit(code);
        }
    }
}
