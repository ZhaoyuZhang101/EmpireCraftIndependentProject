using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Orbis.Flat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.Editor
{
    // Run in a separate Editor with -batchmode -force-d3d11 -executeMethod
    // Orbis.Editor.GlebaClimatePlayValidation.Run [-orbisClimateEvidence <directory>].
    [InitializeOnLoad]
    public static class GlebaClimatePlayValidation
    {
        const string Key = "Orbis.GlebaClimatePlayValidation";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static IEnumerator checks;
        static string phase, runtimeError;
        static double deadline;
        static string Output => SessionState.GetString(Key, "");

        static GlebaClimatePlayValidation()
        {
            if (Output.Length > 0) Listen();
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this gate in a dedicated batch Editor.");
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-orbisClimateEvidence");
            string output = Path.GetFullPath(at >= 0 ? args[at + 1] : "ValidationOutput/GlebaClimatePlay");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "unity-climate-play.txt"), "");
            EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
            SessionState.SetString(Key, output);
            Listen();
            EditorApplication.EnterPlaymode();
        }

        static void Listen()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            Begin("entering Play Mode");
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeError ??= message + "\n" + stack;
        }

        static void Begin(string name)
        {
            phase = name;
            deadline = EditorApplication.timeSinceStartup + 600;
        }

        static void Record(string message) => File.AppendAllText(Path.Combine(Output, "unity-climate-play.txt"), message + Environment.NewLine);
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

        static void Tick()
        {
            try
            {
                Require(runtimeError == null, runtimeError);
                Require(EditorApplication.timeSinceStartup < deadline, "Timeout during " + phase);
                if (!EditorApplication.isPlaying) return;
                checks ??= Verify();
                if (checks.MoveNext()) return;
                Finish("PASS Earth climate in Play Mode", 0);
            }
            catch (Exception e) { Finish("FAIL " + phase + "\n" + e, 1); }
        }

        static void Finish(string result, int code)
        {
            Record(result);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.EraseString(Key);
            Debug.Log(result);
            EditorApplication.Exit(code);
        }

        static IEnumerator Verify()
        {
            Record($"Unity {Application.unityVersion} / {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}");
            Require(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11, "Expected Direct3D 11.");
            FlatWorldApp app;
            while (!(app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>()) || !app.GetComponentInChildren<UIDocument>()) yield return null;
            var ui = app.GetComponentInChildren<UIDocument>().rootVisualElement;
            var size = ui.Q<DropdownField>("size");
            size.index = size.choices.FindIndex(choice => choice.StartsWith("256 ", StringComparison.Ordinal));
            Require(size.index >= 0, "Earth 256 preset is missing.");
            ui.Q<DropdownField>("preset").index = (int)TerrainPreset.Earth;
            ui.Q<DropdownField>("cloudQuality").index = 0;
            typeof(FlatWorldApp).GetField("paused", Private).SetValue(app, true);
            Begin("generating Earth through the game");
            app.SendMessage("Generate", SendMessageOptions.RequireReceiver);
            while (app.Busy || app.World == null || app.World.Preset != TerrainPreset.Earth) yield return null;
            var w = app.World;
            var map = app.GetComponentInChildren<FlatMapRenderer>();
            Require(w.Width == 256 && w.Height == 128 && w.Moisture?.ExoPlaSim != null && w.Moisture.Gleba != null && w.Facts.GlebaGeography != null, "Climate supplements missing from the installed Earth.");
            Record($"Earth {w.Width}x{w.Height}, seed {w.Seed}");
            Region(w, "Indochina", 97, 109, 8, 22, 1380, false);
            Region(w, "Maritime Continent", 95, 130, -10, 10, 1950, false);
            Region(w, "Philippines", 119, 127, 5, 19, 1950, false);
            Region(w, "Sahara control", -10, 28, 20, 30, 85, true);
            int southChina = w.Index((int)(293.0 / 360 * w.Width), (int)(114.0 / 180 * w.Height));
            float january = w.Precipitation[southChina * 12], july = w.Precipitation[southChina * 12 + 6];
            Require(july > january, "South China July must be wetter than January.");
            Record($"South China annual cycle, January/July precipitation {january:F1}/{july:F1} mm");
            typeof(FlatWorldApp).GetField("selected", Private).SetValue(app, southChina);
            app.SendMessage("UpdateHUD", SendMessageOptions.RequireReceiver);
            string hud = ui.Q<Label>("tileInfo").text;
            Require(hud.Contains("[Gleba") && hud.Contains("[ExoPlaSim") && ui.Q<Label>("planetSummary").text.Contains("ppm"), "Tile or planetary climate diagnostics missing from the HUD.");

            Begin("January HP cloud bake");
            w.Day = 14;
            while (!map.Sky.Ready || map.Sky.RegionalClimateStep != (long)(w.Day * 4)) yield return null;
            var bake = (Material)typeof(FlatSkyRenderer).GetField("bake", Private).GetValue(map.Sky);
            Require(bake.GetFloat("_GlebaCloudEnable") > .5f, "Weather-scale forcing disabled.");
            var forcing = (Texture2D)bake.GetTexture("_CloudSynopticTex");
            var auxiliary = (Texture2D)bake.GetTexture("_CloudSynopticAuxTex");
            var upperWind = (Texture2D)bake.GetTexture("_UpperWindTex");
            Require(forcing && auxiliary && upperWind, "Cloud fields missing from the live sky material.");
            int liftJan = Fingerprint(forcing), auxJan = Fingerprint(auxiliary), jetJan = Fingerprint(upperWind);
            double beltJan = IndianConvergenceLatitude(forcing);
            int cloudJan = CloudFingerprint(map.Sky.ColumnTexture);
            Begin("July HP cloud bake");
            w.Day = 194;
            while (map.Sky.RegionalClimateStep != (long)(w.Day * 4)) yield return null;
            Require(liftJan != Fingerprint(forcing) && auxJan != Fingerprint(auxiliary) && jetJan != Fingerprint(upperWind), "Cloud forcing textures stayed fixed through the season change.");
            int cloudJul = CloudFingerprint(map.Sky.ColumnTexture);
            Require(cloudJan != cloudJul, "GPU cloud column did not respond to the season change.");
            double beltJul = IndianConvergenceLatitude(forcing);
            Require(beltJul - beltJan > 10.0, "The Indian monsoon cloud belt failed to migrate north.");
            Record($"Indian sector convergence latitude, January/July {beltJan:F2}/{beltJul:F2} degrees");
            Record($"January/July HP cloud GPU hashes {cloudJan:X8}/{cloudJul:X8}, all three forcing textures changed");

            var layers = ui.Q<DropdownField>("layer");
            Require(layers.choices.Count == (int)MapLayer.ExoPastaClimate + 1, "Incomplete layer selector.");
            layers.index = (int)MapLayer.Climate;
            while (map.VisibleLayer != MapLayer.Climate || map.PreparationPending) yield return null;
            CheckClimateOcean(w, map);
            for (MapLayer layer = MapLayer.SurfaceWinds; layer <= MapLayer.ExoPastaClimate; layer++)
            {
                Begin("January " + layer);
                w.Day = 14;
                layers.index = (int)layer;
                while (map.VisibleLayer != layer || map.PreparationPending) yield return null;
                CheckMap(w, map, app.Navigation.SurfaceMaterial, layer);
                if (layer == MapLayer.SurfaceWinds || layer == MapLayer.OceanCurrents || layer == MapLayer.ExoConvectiveRain)
                {
                    while (!DrawingDetailedSurface(app.Navigation)) yield return null;
                    SaveView(app, "unity-climate-" + layer + "-january.png");
                }
                bool seasonal = layer <= MapLayer.RelativeHumidity || (layer >= MapLayer.ExoConvectiveRain && layer <= MapLayer.ExoStormPotential);
                if (!seasonal) continue;
                Begin("July refresh of " + layer);
                int before = Fingerprint(map.DisplayTexture);
                w.Day = 194;
                while (map.PreparationPending || Fingerprint(map.DisplayTexture) == before) yield return null;
                CheckMap(w, map, app.Navigation.SurfaceMaterial, layer);
                if (layer == MapLayer.ExoConvectiveRain) SaveView(app, "unity-climate-" + layer + "-july.png");
            }
            Begin("local map climate");
            layers.index = (int)MapLayer.ExoConvectiveRain;
            app.Navigation.Focus(southChina, app.CalendarDay);
            while (!app.Navigation.IsFlat || map.VisibleLayer != MapLayer.ExoConvectiveRain || map.PreparationPending) yield return null;
            Require(!app.Navigation.IsAtlas, "Expected the unfolded local surface.");
            CheckMap(w, map, app.Navigation.SurfaceMaterial, MapLayer.ExoConvectiveRain);
            while (!DrawingDetailedSurface(app.Navigation)) yield return null;
            SaveView(app, "unity-climate-local-rain.png");
            Begin("strategic atlas climate");
            app.Navigation.OpenAtlas(app.CalendarDay);
            layers.index = (int)MapLayer.SurfaceWinds;
            while (!app.Navigation.IsAtlas || map.VisibleLayer != MapLayer.SurfaceWinds || map.PreparationPending) yield return null;
            CheckMap(w, map, app.Navigation.SurfaceMaterial, MapLayer.SurfaceWinds);
            while (!DrawingDetailedSurface(app.Navigation)) yield return null;
            SaveView(app, "unity-climate-atlas-winds.png");
            Record("Diagnostic map and material checked on globe, local terrain and strategic atlas");
        }

        // The green channel is the actual deep-convection forcing read by the HP weather bake.
        static double IndianConvergenceLatitude(Texture2D forcing)
        {
            var pixels = forcing.GetPixels32();
            double total = 0, weightedLatitude = 0;
            for (int y = 0; y < forcing.height; y++)
            {
                double lat = ((y + .5) / forcing.height - .5) * 180;
                if (Math.Abs(lat) > 35) continue;
                for (int x = 0; x < forcing.width; x++)
                {
                    double lon = ((x + .5) / forcing.width - .5) * 360;
                    if (lon < 75 || lon > 95) continue;
                    byte weight = pixels[y * forcing.width + x].g;
                    total += weight;
                    weightedLatitude += weight * lat;
                }
            }
            Require(total > 0, "Empty monsoon convergence field.");
            return weightedLatitude / total;
        }

        static void Region(WorldData w, string name, float west, float east, float south, float north, float rainLimit, bool dry)
        {
            int cells = 0, bw = 0, desert = 0;
            double weight = 0, rain = 0, aridArea = 0;
            for (int i = 0; i < w.Count; i++)
            {
                double lat = ((i / w.Width + .5) / w.Height - .5) * 180;
                double lon = ((i % w.Width + .5) / w.Width - .5) * 360;
                if (lat < south || lat >= north || lon < west || lon >= east || FlatTerrainAnalysis.AreaWater(w.Water[i])) continue;
                double area = Math.Cos(lat * Math.PI / 180);
                cells++; weight += area; rain += w.Facts.AnnualRain[i] * area;
                if (w.Climates[i] == Koppen.BWk || w.Climates[i] == Koppen.BWh) { bw++; aridArea += area; }
                if (w.Biomes[i] == Biome.Desert) desert++;
            }
            Require(cells > 0 && (dry ? aridArea / weight >= .85 && rain / weight < rainLimit : bw == 0 && desert == 0 && rain / weight >= rainLimit), name + " failed the regional climate gate");
            Record($"{name}: {cells} land cells, {rain / weight:F1} mm/y, BWh/BWk {bw} cells, desert biome {desert} cells");
        }

        static void CheckClimateOcean(WorldData world, FlatMapRenderer map)
        {
            var pixels = map.DisplayTexture.GetPixels32();
            var ocean = FlatPalette.Water(WaterKind.DeepOcean);
            foreach (var kind in new[] { WaterKind.DeepOcean, WaterKind.MiddleOcean, WaterKind.ShallowOcean })
            {
                int tile = Array.IndexOf(world.Water, kind);
                Require(tile >= 0 && pixels[tile].Equals(ocean) && FlatPalette.Display(world, tile, MapLayer.Climate).Equals(ocean),
                    $"Visible climate map exposes the simulation grid at {kind}.");
            }
            int land = Array.IndexOf(world.Water, WaterKind.Land);
            Require(land >= 0 && pixels[land].Equals(FlatPalette.RGB(FlatPalette.ClimateColors[(int)world.Climates[land]])),
                "Climate map lost its simulated land class.");
        }

        static void CheckMap(WorldData w, FlatMapRenderer map, Material surface, MapLayer layer)
        {
            Require(surface.GetTexture("_MainTex") == map.DisplayTexture, "Visible surface lost its diagnostic map.");
            bool chart = layer == MapLayer.Climate || layer == MapLayer.OceanCurrents || layer == MapLayer.SurfaceWinds ||
                         layer == MapLayer.RossbyJets || layer == MapLayer.ExoStormPotential;
            Require(surface.IsKeywordEnabled("ORBIS_DIAGNOSTIC") == chart && map.Material.IsKeywordEnabled("ORBIS_DIAGNOSTIC") == chart,
                $"Published map and visible surface disagree on the diagnostic variant for {layer}.");
            if (chart) Require(!surface.IsKeywordEnabled("ORBIS_RIVER_RASTER") && !surface.IsKeywordEnabled("ORBIS_RIVER_VECTORS"),
                $"Diagnostic pass still selects a river variant for {layer}.");
            var pixels = map.DisplayTexture.GetPixels32();
            for (int i = 0; i < w.Count; i += 137)
            {
                byte r, g, b, a = 255;
                if (layer >= MapLayer.ExoConvectiveRain)
                    ExoPlaSimSupplement.EvaluateMapmodeColor(w, i, w.Month, ExoPlaSimSupplement.MapLayerToExoMode(layer), out r, out g, out b);
                else GlebaGeographySupplement.PackMapmodeDisplay(w, i, w.Month, GlebaGeographySupplement.MapLayerToGlebaMode(layer), out r, out g, out b, out a);
                Require(pixels[i].r == r && pixels[i].g == g && pixels[i].b == b && pixels[i].a == a,
                    $"Stale {layer} texel at tile {i}, month {w.Month + 1}.");
            }
            if (layer == MapLayer.SurfaceWinds || layer == MapLayer.OceanCurrents || layer == MapLayer.RossbyJets || layer == MapLayer.ExoStormPotential)
            {
                string id = layer == MapLayer.OceanCurrents ? "_GlebaCurrentFlowTex" : "_GlebaWindFlowTex";
                var tex = (Texture2D)surface.GetTexture(id);
                Require(surface.GetVector("_GlebaCirculation").x > 0 && tex && tex.format == TextureFormat.RGBAHalf, "Visible surface missing half-precision circulation for " + layer);
            }
            Record($"{layer}, month {w.Month + 1}, {(w.Count + 136) / 137} terrain pixels match simulated diagnostics");
        }

        static int Fingerprint(Texture2D texture)
        {
            var data = texture.GetRawTextureData<byte>();
            int hash = 17;
            for (int i = 0; i < data.Length; i += 31) hash = unchecked(hash * 31 + data[i]);
            return hash;
        }

        static int CloudFingerprint(RenderTexture clouds)
        {
            var target = RenderTexture.GetTemporary(128, 64, 0, RenderTextureFormat.ARGB32);
            var active = RenderTexture.active;
            var image = new Texture2D(128, 64, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(clouds, target);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 128, 64), 0, 0);
                return Fingerprint(image);
            }
            finally { RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image); }
        }

        static bool DrawingDetailedSurface(FlatScrollNavigator nav)
        {
            var surface = nav.SurfaceMaterial;
            int pass = surface.FindPass("SCROLL_SURFACE");
            Require(pass >= 0, "Globe/map surface pass missing.");
            if (!ShaderUtil.IsPassCompiled(surface, pass)) return false;
            foreach (var renderer in nav.GetComponentsInChildren<MeshRenderer>())
                if (renderer.enabled && renderer.sharedMaterial == surface) return true;
            return false;
        }

        static void SaveView(FlatWorldApp app, string name)
        {
            const int width = 960, height = 540;
            var camera = app.GetComponentInChildren<Camera>();
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            var rect = camera.pixelRect;
            var surface = app.Navigation.SurfaceMaterial;
            Vector4 flow = surface.GetVector("_GlebaCirculation");
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                app.Navigation.SetViewport(new Rect(0, 0, width, height));
                app.Navigation.Tick(0, app.CalendarDay);
                app.Navigation.SyncMaterials();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
                if (flow.x > .5f)
                {
                    var visible = image.GetPixels32();
                    // Only toggle readiness; the mode also controls base-map filtering.
                    surface.SetVector("_GlebaCirculation", new Vector4(flow.x, flow.y, flow.z, 0f));
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    var hidden = image.GetPixels32();
                    int changed = 0;
                    for (int i = 0; i < visible.Length; i++)
                        if (Math.Abs(visible[i].r - hidden[i].r) + Math.Abs(visible[i].g - hidden[i].g) + Math.Abs(visible[i].b - hidden[i].b) >= 6) changed++;
                    Require(changed > 30, "No visible circulation in " + name);
                    Record($"{name}: {changed} screen pixels respond to the circulation toggle at fixed camera/time");
                }
            }
            finally
            {
                surface.SetVector("_GlebaCirculation", flow);
                camera.targetTexture = previous; app.Navigation.SetViewport(rect); RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
