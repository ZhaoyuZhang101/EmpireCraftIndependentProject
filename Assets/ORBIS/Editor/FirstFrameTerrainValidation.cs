using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Orbis.Flat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Orbis.Editor
{
    /// <summary>
    /// End-to-end Unity 6000 D3D11 Play-mode first-frame terrain verification across:
    ///   1. Generate new 512x256 Natural world
    ///   2. Load saved world
    ///   3. StartEarthStormScenario(NorthAtlanticLandfall)
    ///   4. ExitIsolatedDemoWorld ("返回原局")
    /// Captures world-ready frame 0, frame 1, and settled frame without wheel scrolling or layer toggling,
    /// then verifies smooth subsequent wheel zoom without synchronous compile hitches.
    ///
    /// Command line:
    ///   Unity.exe -batchmode -force-d3d11 -projectPath &lt;p&gt;
    ///     -executeMethod Orbis.Editor.FirstFrameTerrainValidation.Run
    ///     -firstFrameOut ValidationOutput/FirstFrame/After
    ///     [-firstFrameAllowFail]
    /// </summary>
    [InitializeOnLoad]
    public static class FirstFrameTerrainValidation
    {
        const string KeyOut = "OrbisFirstFrameValidation.Out";
        const string KeyAllowFail = "OrbisFirstFrameValidation.AllowFail";
        const string KeyStart = "OrbisFirstFrameValidation.Start";
        const string ScenePath = "Assets/ORBIS/Scenes/Orbis2DWorld.unity";
        const int CaptureW = 1600, CaptureH = 900;
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        static FirstFrameTerrainValidation()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(KeyOut, "")))
                EditorApplication.update += Step;
        }

        public static void Run()
        {
            string outDir = Argument("-firstFrameOut") ?? Path.GetFullPath("ValidationOutput/FirstFrame/Run");
            bool allowFail = HasArgument("-firstFrameAllowFail");
            Directory.CreateDirectory(outDir);
            SessionState.SetString(KeyOut, outDir);
            SessionState.SetBool(KeyAllowFail, allowFail);
            SessionState.SetFloat(KeyStart, (float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update += Step;
            EditorApplication.EnterPlaymode();
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static bool HasArgument(string name) => Environment.GetCommandLineArgs().Contains(name);

        enum Stage
        {
            WaitInitialWorld,
            CaptureGenerateF0,
            CaptureGenerateF1,
            WaitGenerateSettled,
            WheelTestGenerate,
            TriggerLoadSave,
            WaitLoadReady,
            CaptureLoadF0,
            CaptureLoadF1,
            WaitLoadSettled,
            TriggerNorthAtlantic,
            WaitNorthAtlanticReady,
            CaptureNorthAtlanticF0,
            CaptureNorthAtlanticF1,
            WaitNorthAtlanticLocal,
            WaitNorthAtlanticSettled,
            TriggerReturnOriginal,
            WaitReturnReady,
            CaptureReturnF0,
            CaptureReturnF1,
            WaitReturnSettled,
            WheelTestReturn,
            Done
        }

        static Stage stage = Stage.WaitInitialWorld;
        static int waitFrames;
        static double stageStart;
        static bool logHooked;
        static readonly List<string> report = new List<string>();
        static readonly List<string> failures = new List<string>();
        static readonly List<string> newErrors = new List<string>();
        static int cloudContractErrors;
        static string saveBackupPath;
        static bool hadOriginalSave;
        static WorldData initialWorldRef;
        static WorldData demoWorldRef;
        static bool savedAsyncSetting;
        static bool asyncSettingCaptured;
        static int warmupFramesObserved;

        static int PickSunlitRiverCoastTile(WorldData world, double day)
        {
            double sunU = ((0.5 - FlatSolarClock.Phase(day)) % 1.0 + 1.0) % 1.0;
            int best = -1;
            float bestScore = -1e9f;
            for (int i = 0; i < world.Count; i++)
            {
                if (world.Water[i] != WaterKind.Land) continue;
                int x = i % world.Width, y = i / world.Width;
                float latNorm = (y + 0.5f) / world.Height - 0.5f;
                if (Mathf.Abs(latNorm) > 0.35f) continue;
                double u = (x + 0.5) / world.Width;
                double du = Math.Abs(u - sunU);
                if (du > 0.5) du = 1.0 - du;
                if (du > 0.22) continue; // sunlit hemisphere near midday
                int ocean = 0, river = 0;
                for (int d = 0; d < 4; d++)
                {
                    int n = world.Neighbor(i, d);
                    if (n < 0) continue;
                    if (FlatTerrainAnalysis.Ocean(world.Water[n])) ocean++;
                    if (world.Water[n] == WaterKind.River) river++;
                }
                if (ocean == 0 && river == 0) continue;
                float relief = world.Facts != null ? world.Facts.Relief[i] : 0f;
                float score = river * 500f + ocean * 140f + Mathf.Min(relief, 1200f) * 0.3f - (float)(du * 600.0);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best >= 0 ? best : (world.Height / 2) * world.Width + (int)(sunU * world.Width);
        }

        static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition != null && condition.Contains("Material 'ORBIS/Clouds' with Shader 'ORBIS/Clouds' doesn't have a float"))
            {
                cloudContractErrors++;
                return;
            }
            newErrors.Add($"[{type}] {condition}");
        }

        static void Step()
        {
            string outDir = SessionState.GetString(KeyOut, "");
            if (string.IsNullOrEmpty(outDir)) { EditorApplication.update -= Step; return; }
            double started = SessionState.GetFloat(KeyStart, 0);
            if (EditorApplication.timeSinceStartup - started > 900)
            {
                Finish(outDir, "TIMEOUT after 900s at stage=" + stage, 3);
                return;
            }
            if (!EditorApplication.isPlaying) return;

            if (!logHooked)
            {
                Application.logMessageReceived += OnLogMessage;
                logHooked = true;
            }
            if (!asyncSettingCaptured)
            {
                savedAsyncSetting = EditorSettings.asyncShaderCompilation;
                EditorSettings.asyncShaderCompilation = true;
                asyncSettingCaptured = true;
                report.Add($"ENV Unity={Application.unityVersion} GPU={SystemInfo.graphicsDeviceName} API={SystemInfo.graphicsDeviceType} asyncShaderCompilation={EditorSettings.asyncShaderCompilation}");
            }

            var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if (!app) return;

            var appType = typeof(FlatWorldApp);
            var map = appType.GetField("map", Private)?.GetValue(app) as FlatMapRenderer;
            var nav = app.Navigation;
            var camera = appType.GetField("viewCamera", Private)?.GetValue(app) as Camera;
            var loadingCover = appType.GetField("loadingCover", Private)?.GetValue(app) as UnityEngine.UIElements.VisualElement;
            if (app.Busy && loadingCover != null && !loadingCover.ClassListContains("is-hidden"))
                warmupFramesObserved++;

            try
            {
                switch (stage)
                {
                    case Stage.WaitInitialWorld:
                        if (app.World != null && !app.Busy && map && nav && camera)
                        {
                            initialWorldRef = app.World;
                            typeof(FlatWorldApp).GetField("screenshotMode", Private)?.SetValue(app, true);
                            stage = Stage.CaptureGenerateF0;
                            waitFrames = 0;
                        }
                        break;

                    case Stage.CaptureGenerateF0:
                        CaptureAndRecord(outDir, "01_generate_frame0", app, map, nav, camera, requireFullFirstFrame: true);
                        stage = Stage.CaptureGenerateF1;
                        break;

                    case Stage.CaptureGenerateF1:
                        CaptureAndRecord(outDir, "01_generate_frame1", app, map, nav, camera, requireFullFirstFrame: true);
                        stage = Stage.WaitGenerateSettled;
                        stageStart = EditorApplication.timeSinceStartup;
                        waitFrames = 0;
                        break;

                    case Stage.WaitGenerateSettled:
                        waitFrames++;
                        if (IsSettled(map, nav) || waitFrames >= 90 || EditorApplication.timeSinceStartup - stageStart > 220)
                        {
                            CaptureAndRecord(outDir, "01_generate_settled", app, map, nav, camera, requireFullFirstFrame: false);
                            stage = Stage.WheelTestGenerate;
                            waitFrames = 0;
                        }
                        break;

                    case Stage.WheelTestGenerate:
                        // Smooth wheel zoom into local view on a sunlit coastal-river landmark without synchronous compile hitches.
                        {
                            int sunlitTile = PickSunlitRiverCoastTile(app.World, app.CalendarDay);
                            var loc = Orbis.Planet.PlanetCoordinateSystem.FromMapUV(
                                (sunlitTile % app.World.Width + 0.5) / app.World.Width,
                                (sunlitTile / app.World.Width + 0.5) / app.World.Height);
                            nav.Recenter(loc, app.CalendarDay);
                            nav.Tick(0, app.CalendarDay);
                        }
                        MeasureWheelBurst(nav, camera, app, "generate_wheel_in", -3f, 16);
                        // Also zoom to flat local detail so subsequent LoadSave starts from wasFlat == true on sunlit terrain.
                        for (int k = 0; k < 20 && !nav.IsFlat; k++)
                        {
                            nav.Wheel(-3f, new Vector2(CaptureW * 0.5f, CaptureH * 0.5f), app.CalendarDay);
                            nav.Tick(1f / 30f, app.CalendarDay);
                        }
                        nav.Tick(1f, app.CalendarDay);
                        nav.SyncMaterials();
                        CaptureAndRecord(outDir, "01_generate_local_after_wheel", app, map, nav, camera, requireFullFirstFrame: true);
                        stage = Stage.TriggerLoadSave;
                        break;

                    case Stage.TriggerLoadSave:
                    {
                        string savePath = app.SavePath;
                        hadOriginalSave = File.Exists(savePath);
                        if (hadOriginalSave)
                        {
                            saveBackupPath = savePath + ".ffval.bak";
                            File.Copy(savePath, saveBackupPath, true);
                        }
                        FlatSave.WriteAtomic(savePath, app.World);
                        appType.GetMethod("Load", Private).Invoke(app, null);
                        stage = Stage.WaitLoadReady;
                        stageStart = EditorApplication.timeSinceStartup;
                        waitFrames = 0;
                        break;
                    }

                    case Stage.WaitLoadReady:
                        waitFrames++;
                        if (!app.Busy && app.World != null && !ReferenceEquals(app.World, initialWorldRef) && map && nav && camera)
                        {
                            initialWorldRef = app.World;
                            stage = Stage.CaptureLoadF0;
                            waitFrames = 0;
                        }
                        break;

                    case Stage.CaptureLoadF0:
                        CaptureAndRecord(outDir, "02_load_frame0", app, map, nav, camera, requireFullFirstFrame: true);
                        stage = Stage.CaptureLoadF1;
                        break;

                    case Stage.CaptureLoadF1:
                        CaptureAndRecord(outDir, "02_load_frame1", app, map, nav, camera, requireFullFirstFrame: true);
                        stage = Stage.WaitLoadSettled;
                        stageStart = EditorApplication.timeSinceStartup;
                        waitFrames = 0;
                        break;

                    case Stage.WaitLoadSettled:
                        waitFrames++;
                        // Allow Focus animation (from wasFlat) to reach IsFlat without touching wheel!
                        if ((IsSettled(map, nav) && nav.IsFlat && waitFrames >= 10) || waitFrames >= 180 || EditorApplication.timeSinceStartup - stageStart > 220)
                        {
                            CaptureAndRecord(outDir, "02_load_settled", app, map, nav, camera, requireFullFirstFrame: false);
                            stage = Stage.TriggerNorthAtlantic;
                        }
                        break;

                    case Stage.TriggerNorthAtlantic:
                    {
                        bool ok = app.StartEarthStormScenario(FlatStormScenario.NorthAtlanticLandfall);
                        if (!ok) throw new InvalidOperationException("StartEarthStormScenario(NorthAtlanticLandfall) returned false");
                        stage = Stage.WaitNorthAtlanticReady;
                        stageStart = EditorApplication.timeSinceStartup;
                        waitFrames = 0;
                        break;
                    }

                    case Stage.WaitNorthAtlanticReady:
                        waitFrames++;
                        if (!app.Busy && app.IsDemoWorldActive && app.World != null && map && nav && camera)
                        {
                            demoWorldRef = app.World;
                            stage = Stage.CaptureNorthAtlanticF0;
                            waitFrames = 0;
                        }
                        break;

                    case Stage.CaptureNorthAtlanticF0:
                        CaptureAndRecord(outDir, "03_natl_frame0", app, map, nav, camera, requireFullFirstFrame: true);
                        stage = Stage.CaptureNorthAtlanticF1;
                        break;

                    case Stage.CaptureNorthAtlanticF1:
                        CaptureAndRecord(outDir, "03_natl_frame1", app, map, nav, camera, requireFullFirstFrame: true);
                        stage = Stage.WaitNorthAtlanticLocal;
                        stageStart = EditorApplication.timeSinceStartup;
                        waitFrames = 0;
                        break;

                    case Stage.WaitNorthAtlanticLocal:
                        waitFrames++;
                        // Wait for natural Focus() spring to enter local flat surface (NO wheel, NO layer click!).
                        if (nav.IsFlat || waitFrames >= 160)
                        {
                            CaptureAndRecord(outDir, "03_natl_local_frame0", app, map, nav, camera, requireFullFirstFrame: true);
                            stage = Stage.WaitNorthAtlanticSettled;
                            stageStart = EditorApplication.timeSinceStartup;
                            waitFrames = 0;
                        }
                        break;

                    case Stage.WaitNorthAtlanticSettled:
                        waitFrames++;
                        if ((IsSettled(map, nav) && waitFrames >= 15) || waitFrames >= 120 || EditorApplication.timeSinceStartup - stageStart > 220)
                        {
                            CaptureAndRecord(outDir, "03_natl_settled", app, map, nav, camera, requireFullFirstFrame: false);
                            stage = Stage.TriggerReturnOriginal;
                        }
                        break;

                    case Stage.TriggerReturnOriginal:
                    {
                        bool exited = app.ExitIsolatedDemoWorld();
                        if (!exited) throw new InvalidOperationException("ExitIsolatedDemoWorld() returned false");
                        stage = Stage.WaitReturnReady;
                        stageStart = EditorApplication.timeSinceStartup;
                        waitFrames = 0;
                        break;
                    }

                    case Stage.WaitReturnReady:
                        waitFrames++;
                        if (!app.Busy && !app.IsDemoWorldActive && app.World != null && !ReferenceEquals(app.World, demoWorldRef) && map && nav && camera)
                        {
                            stage = Stage.CaptureReturnF0;
                            waitFrames = 0;
                        }
                        break;

                    case Stage.CaptureReturnF0:
                        CaptureAndRecord(outDir, "04_return_frame0", app, map, nav, camera, requireFullFirstFrame: true);
                        stage = Stage.CaptureReturnF1;
                        break;

                    case Stage.CaptureReturnF1:
                        CaptureAndRecord(outDir, "04_return_frame1", app, map, nav, camera, requireFullFirstFrame: true);
                        stage = Stage.WaitReturnSettled;
                        stageStart = EditorApplication.timeSinceStartup;
                        waitFrames = 0;
                        break;

                    case Stage.WaitReturnSettled:
                        waitFrames++;
                        if ((IsSettled(map, nav) && (nav.IsFlat || waitFrames >= 30)) || waitFrames >= 160 || EditorApplication.timeSinceStartup - stageStart > 220)
                        {
                            CaptureAndRecord(outDir, "04_return_settled", app, map, nav, camera, requireFullFirstFrame: false);
                            stage = Stage.WheelTestReturn;
                        }
                        break;

                    case Stage.WheelTestReturn:
                        MeasureWheelBurst(nav, camera, app, "return_wheel_out_in", +3f, 8);
                        MeasureWheelBurst(nav, camera, app, "return_wheel_in", -3f, 8);
                        stage = Stage.Done;
                        break;

                    case Stage.Done:
                    {
                        bool allowFail = SessionState.GetBool(KeyAllowFail, false);
                        report.Add($"SUMMARY failures={failures.Count} warmupCoverFrames={warmupFramesObserved} newUnityErrors={newErrors.Count} preExistingCloudContractErrors={cloudContractErrors}");
                        foreach (var e in newErrors) report.Add("UNITY_ERROR " + e);
                        if (newErrors.Count > 0) failures.Add($"Encountered {newErrors.Count} new Unity errors");
                        int exitCode = (!allowFail && failures.Count > 0) ? 2 : 0;
                        Finish(outDir, failures.Count == 0 ? "PASS" : ("FAIL (" + failures.Count + " issues)"), exitCode);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                failures.Add("EXCEPTION " + stage + ": " + ex.Message);
                Finish(outDir, "EXCEPTION: " + ex.Message, 4);
            }
        }

        static bool IsSettled(FlatMapRenderer map, FlatScrollNavigator nav)
        {
            if (!map || !nav) return false;
            var terrain = typeof(FlatScrollNavigator).GetField("terrain", Private)?.GetValue(nav) as MeshRenderer;
            bool preview = nav.UsingShaderPreview || (terrain && terrain.sharedMaterial && terrain.sharedMaterial.IsKeywordEnabled("ORBIS_PREVIEW"));
            return !preview && map.RiverAtlasReady && !map.PreparationPending;
        }

        static void MeasureWheelBurst(FlatScrollNavigator nav, Camera camera, FlatWorldApp app, string tag, float deltaPerStep, int steps)
        {
            var times = new List<double>();
            bool sawPreview = false;
            for (int i = 0; i < steps; i++)
            {
                var sw = Stopwatch.StartNew();
                nav.Wheel(deltaPerStep, new Vector2(CaptureW * 0.5f, CaptureH * 0.5f), app.CalendarDay);
                nav.Tick(1f / 60f, app.CalendarDay);
                nav.SyncMaterials();
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
                if (nav.UsingShaderPreview) sawPreview = true;
            }
            double maxMs = times.Max();
            double avgMs = times.Average();
            bool ok = !sawPreview && maxMs < 250.0;
            report.Add($"{(ok ? "PASS" : "FAIL")} wheel {tag} avgMs={avgMs:F2} maxMs={maxMs:F2} sawPreview={sawPreview} morph={nav.Morph:F3} px={nav.PixelsPerTile:F2}");
            if (!ok) failures.Add($"Wheel hitch or preview fallback in {tag}: maxMs={maxMs:F2}, sawPreview={sawPreview}");
        }

        static void CaptureAndRecord(string outDir, string name, FlatWorldApp app, FlatMapRenderer map, FlatScrollNavigator nav, Camera camera, bool requireFullFirstFrame)
        {
            var terrain = typeof(FlatScrollNavigator).GetField("terrain", Private)?.GetValue(nav) as MeshRenderer;
            Material drawnMat = terrain ? terrain.sharedMaterial : null;
            Material surfaceMat = nav.SurfaceMaterial;
            int depthPass = surfaceMat ? surfaceMat.FindPass("SCROLL_DEPTH") : -1;
            int surfacePass = surfaceMat ? surfaceMat.FindPass("SCROLL_SURFACE") : -1;
            bool depthCompiled = surfaceMat && depthPass >= 0 && ShaderUtil.IsPassCompiled(surfaceMat, depthPass);
            bool surfaceCompiled = surfaceMat && surfacePass >= 0 && ShaderUtil.IsPassCompiled(surfaceMat, surfacePass);
            bool usingPreview = nav.UsingShaderPreview || (drawnMat && drawnMat.IsKeywordEnabled("ORBIS_PREVIEW"));
            bool kwRaster = drawnMat && drawnMat.IsKeywordEnabled("ORBIS_RIVER_RASTER");
            bool kwVectors = drawnMat && drawnMat.IsKeywordEnabled("ORBIS_RIVER_VECTORS");
            bool kwDiag = drawnMat && drawnMat.IsKeywordEnabled("ORBIS_DIAGNOSTIC");
            float fieldReady = drawnMat ? drawnMat.GetFloat("_FieldReady") : -1f;
            float riverReady = drawnMat ? drawnMat.GetFloat("_RiverAtlasReady") : -1f;
            float riverPreviewReady = drawnMat ? drawnMat.GetFloat("_RiverPreviewReady") : -1f;
            float riverOverviewReady = drawnMat ? drawnMat.GetFloat("_RiverOverviewReady") : -1f;

            var rt = RenderTexture.GetTemporary(CaptureW, CaptureH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(CaptureW, CaptureH, TextureFormat.RGB24, false);
            var prevTarget = camera.targetTexture;
            var prevActive = RenderTexture.active;
            var prevRect = camera.pixelRect;
            double meanLuma = 0, stdLuma = 0;
            try
            {
                camera.targetTexture = rt;
                nav.SetViewport(new Rect(0, 0, CaptureW, CaptureH));
                nav.Tick(0, app.CalendarDay);
                nav.SyncMaterials();
                camera.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, CaptureW, CaptureH), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());

                // Sample central 200x200 patch luminance variance for texture detail telemetry.
                double sum = 0, sumSq = 0; int cnt = 0;
                int x0 = CaptureW / 2 - 100, y0 = CaptureH / 2 - 100;
                for (int y = y0; y < y0 + 200; y += 2)
                for (int x = x0; x < x0 + 200; x += 2)
                {
                    Color c = tex.GetPixel(x, y);
                    double l = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    sum += l; sumSq += l * l; cnt++;
                }
                meanLuma = sum / Math.Max(1, cnt);
                stdLuma = Math.Sqrt(Math.Max(0, sumSq / Math.Max(1, cnt) - meanLuma * meanLuma));
            }
            finally
            {
                camera.targetTexture = prevTarget;
                camera.pixelRect = prevRect;
                RenderTexture.active = prevActive;
                UnityEngine.Object.DestroyImmediate(tex);
                RenderTexture.ReleaseTemporary(rt);
            }

            int publishedRev = (int)typeof(FlatMapRenderer).GetField("dataRevision", Private).GetValue(map);
            var loadingCover = typeof(FlatWorldApp).GetField("loadingCover", Private)?.GetValue(app) as UnityEngine.UIElements.VisualElement;
            bool coverHidden = loadingCover == null || loadingCover.ClassListContains("is-hidden");
            bool skyReady = map.Sky != null && map.Sky.Ready;
            bool riverAvailable = riverReady > 0.5f || riverPreviewReady > 0.5f;
            bool expectedKeywordOk = app.World.Preset == TerrainPreset.Earth ? kwVectors : kwRaster;
            bool frameOk = !usingPreview && depthCompiled && surfaceCompiled && fieldReady > 0.5f &&
                           riverAvailable && expectedKeywordOk && coverHidden && map.VisibleLayer == map.Layer &&
                           publishedRev == app.World.TerrainRevision;
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0} {1} | world={2}x{3} {4} | layer={5}/{6} rev={7}/{8} | usingPreview={9} depthComp={10} surfComp={11} coverHidden={12} skyReady={13} | kw=(raster:{14},vec:{15},diag:{16}) | river=(atlas:{17},prev:{18},ov:{19}) field={20} | morph={21:F3} px={22:F2} detailW={23:F3} | centerLuma={24:F3} std={25:F4}",
                (frameOk || !requireFullFirstFrame) ? "PASS" : "FAIL",
                name, app.World.Width, app.World.Height, app.World.Preset,
                map.VisibleLayer, map.Layer, publishedRev, app.World.TerrainRevision,
                usingPreview, depthCompiled, surfaceCompiled, coverHidden, skyReady,
                kwRaster, kwVectors, kwDiag,
                riverReady, riverPreviewReady, riverOverviewReady, fieldReady,
                nav.Morph, nav.PixelsPerTile, nav.DetailWeight, meanLuma, stdLuma);
            report.Add(line);
            Debug.Log("[FirstFrameVal] " + line);
            if (requireFullFirstFrame && !frameOk)
                failures.Add(line);
        }

        static void Finish(string outDir, string summary, int code)
        {
            EditorApplication.update -= Step;
            if (logHooked)
            {
                Application.logMessageReceived -= OnLogMessage;
                logHooked = false;
            }
            if (asyncSettingCaptured)
            {
                EditorSettings.asyncShaderCompilation = savedAsyncSetting;
                asyncSettingCaptured = false;
            }
            var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if (app != null)
            {
                string savePath = app.SavePath;
                try
                {
                    if (hadOriginalSave && !string.IsNullOrEmpty(saveBackupPath) && File.Exists(saveBackupPath))
                    {
                        File.Copy(saveBackupPath, savePath, true);
                        File.Delete(saveBackupPath);
                    }
                    else if (!hadOriginalSave && File.Exists(savePath))
                    {
                        File.Delete(savePath);
                    }
                }
                catch (Exception e) { Debug.LogWarning("Save restore cleanup: " + e.Message); }
            }
            SessionState.EraseString(KeyOut);
            SessionState.EraseBool(KeyAllowFail);
            report.Add("RESULT: " + summary);
            File.WriteAllLines(Path.Combine(outDir, "report.txt"), report, new UTF8Encoding(false));
            Debug.Log("FirstFrameTerrainValidation " + summary);
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }
}
