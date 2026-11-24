using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Editor
{
    [InitializeOnLoad]
    public static class WaterHazardsVisualQA
    {
        const string ScenePath = "Assets/ORBIS/Scenes/Orbis2DWorld.unity";
        const string KeyActive = "Orbis.WaterHazardsQA.Active";
        const string KeyOut = "Orbis.WaterHazardsQA.Out";
        const string KeyStart = "Orbis.WaterHazardsQA.Start";
        const string KeyExit = "Orbis.WaterHazardsQA.Exit";

        const int RenderWidth = 1280;
        const int RenderHeight = 720;
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [Serializable]
        public sealed class FrameSnapshotSummary
        {
            public string tag;
            public string pngFile;
            public string phase;
            public double simSeconds;
            public int inundatedDryCells;
            public double inundatedAreaKm2;
            public double maxFloodDepthMeters;
            public double maxSpeedMs;
            public double relativeMassError;
            public double rainVolumeM3;
            public double channelInflowVolumeM3;
            public double boundaryInflowVolumeM3;
            public double boundaryOutflowVolumeM3;
            public double infiltrationVolumeM3;
            public double seabedUpliftVolumeM3;
            public double changedScreenFractionVsRest;
        }

        [Serializable]
        public sealed class ScenarioVisualReport
        {
            public string kind;
            public string title;
            public int patchX, patchY, focusTile;
            public bool vendorWaterActive;
            public bool cameraPanPreservedHazard;
            public double workerStepMedianMs, workerStepP95Ms;
            public double mainThreadUploadMedianMs;
            public double gpuRenderIdleMs, gpuRenderActiveMs, gpuDeltaMs;
            public double peakChangedScreenFraction, recedeRecoveredFraction;
            public string stripPngFile;
            public List<FrameSnapshotSummary> frames = new List<FrameSnapshotSummary>();
        }

        [Serializable]
        public sealed class FullVisualQaReport
        {
            public string utc;
            public string unityVersion;
            public string graphicsApi;
            public string gpuDevice;
            public string status = "STARTED";
            public bool baselineWaterRegressionsPassed;
            public bool worldSwitchCleanupVerified;
            public List<string> baselineChecks = new List<string>();
            public List<ScenarioVisualReport> scenarios = new List<ScenarioVisualReport>();
            public string message;
        }

        sealed class SessionContext
        {
            public FlatWorldApp App;
            public string OutDir;
            public bool SavedAsyncCompile;
            public FullVisualQaReport Report;
            public readonly Queue<Action> Steps = new Queue<Action>();
            public int CooldownFrames;
        }

        static SessionContext active;

        static WaterHazardsVisualQA()
        {
            if (SessionState.GetBool(KeyActive, false))
                EditorApplication.update += Tick;
            if (SessionState.GetInt(KeyExit, -1) >= 0)
                EditorApplication.update += ExitAfterPlay;
        }

        [MenuItem("Tools/ORBIS/Water Hazards/Run Full D3D11 Normal-Route Visual & Timing QA")]
        public static void RunMenu() => StartQa("ValidationOutput/WaterHazards", exitOnFinish: false);

        public static void RunBatch()
        {
            string outDir = GetArg("-orbisHazardQaOut") ?? "ValidationOutput/WaterHazards";
            StartQa(outDir, exitOnFinish: true);
        }

        static string GetArg(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }

        static void StartQa(string outDir, bool exitOnFinish)
        {
            string fullOut = Path.IsPathRooted(outDir)
                ? Path.GetFullPath(outDir)
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", outDir));
            Directory.CreateDirectory(fullOut);
            string staleErr = Path.Combine(fullOut, "error.txt");
            if (File.Exists(staleErr)) File.Delete(staleErr);
            active = null;
            SessionState.SetBool(KeyActive, true);
            SessionState.SetString(KeyOut, fullOut);
            SessionState.SetFloat(KeyStart, (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(KeyExit, exitOnFinish ? -2 : -1);

            if (!EditorApplication.isPlaying)
                EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (!SessionState.GetBool(KeyActive, false))
            {
                EditorApplication.update -= Tick;
                return;
            }
            string outDir = SessionState.GetString(KeyOut, Path.GetFullPath("ValidationOutput/WaterHazards"));
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyStart, 0f) > 2400f)
            {
                FailAndFinish(outDir, "Timed out waiting for WaterHazardsVisualQA.");
                return;
            }
            if (!EditorApplication.isPlaying) return;

            if (active == null)
            {
                var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
                if (!app || app.Busy || app.World == null) return;
                var map = app.GetComponentInChildren<FlatMapRenderer>();
                if (!map || map.PreparationPending || !map.RiverAtlasReady) return;
                try
                {
                    active = BuildPlan(app, outDir);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogException(ex);
                    FailAndFinish(outDir, ex.ToString());
                }
                return;
            }

            if (active.CooldownFrames > 0)
            {
                active.CooldownFrames--;
                return;
            }
            if (active.Steps.Count == 0)
            {
                FinishSession(outDir, 0);
                return;
            }
            var step = active.Steps.Dequeue();
            try
            {
                step();
                if (active != null) active.CooldownFrames = 1;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
                if (active?.Report != null)
                {
                    active.Report.status = "FAIL";
                    active.Report.message = ex.ToString();
                    WriteReportFiles(outDir, active.Report);
                }
                FailAndFinish(outDir, ex.ToString());
            }
        }

        static SessionContext BuildPlan(FlatWorldApp app, string outDir)
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                throw new InvalidOperationException("WaterHazardsVisualQA requires Direct3D11 (-force-d3d11), got " + SystemInfo.graphicsDeviceType);

            typeof(FlatWorldApp).GetField("paused", Private)?.SetValue(app, true);
            typeof(FlatWorldApp).GetField("screenshotMode", Private)?.SetValue(app, true);

            var ctx = new SessionContext
            {
                App = app,
                OutDir = outDir,
                SavedAsyncCompile = ShaderUtil.allowAsyncCompilation,
                Report = new FullVisualQaReport
                {
                    utc = DateTime.UtcNow.ToString("O"),
                    unityVersion = Application.unityVersion,
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                    gpuDevice = SystemInfo.graphicsDeviceName
                }
            };
            ShaderUtil.allowAsyncCompilation = false;

            // Step A: Run existing baseline water, coast, river & cloud optics regressions.
            ctx.Steps.Enqueue(() =>
            {
                WaterOpticsRegression.Run();
                ctx.Report.baselineChecks.Add("PASS WaterOpticsRegression");
                WaterCoastRegression.Run();
                ctx.Report.baselineChecks.Add("PASS WaterCoastRegression");
                RiverOpticsRegression.Run();
                ctx.Report.baselineChecks.Add("PASS RiverOpticsRegression");
                WaterCloudRegression.Run();
                ctx.Report.baselineChecks.Add("PASS WaterCloudRegression");
                CompileProductionSurfaceShaders(ctx.Report.baselineChecks);
                ctx.Report.baselineWaterRegressionsPassed = true;
            });

            // Step B: Capture time-series & GPU/CPU timings for each of the 3 hazard scenarios on normal game route.
            foreach (var kind in new[] { FlatHazardKind.RainFlood, FlatHazardKind.TyphoonSurge, FlatHazardKind.Tsunami })
            {
                var capturedKind = kind;
                ctx.Steps.Enqueue(() =>
                {
                    ctx.Report.scenarios.Add(CaptureScenarioSequence(ctx.App, capturedKind, ctx.OutDir));
                });
            }

            // Step C: Verify world-switch cleanup while an active hazard is running.
            ctx.Steps.Enqueue(() =>
            {
                VerifyWorldSwitchCleanup(ctx.App, ctx.Report);
                ctx.Report.status = "PASS";
                ctx.Report.message = "All 3 SWE hazard scenarios, normal-route visuals, camera-pan invariance, world-switch cleanup, and D3D11 timings passed.";
                WriteReportFiles(ctx.OutDir, ctx.Report);
                UnityEngine.Debug.Log("[WaterHazardsVisualQA] PASS: " + ctx.Report.message);
            });

            return ctx;
        }

        static void CompileProductionSurfaceShaders(List<string> checks)
        {
            string[] paths =
            {
                "Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatMap.shader",
                "Assets/ORBIS/Resources/ORBIS/FlatWorld/LocalSurface.shader",
                "Assets/ORBIS/Resources/ORBIS/FlatWorld/ScrollSurface.shader",
                "Assets/ORBIS/Resources/ORBIS/Surface.shader"
            };
            foreach (string path in paths)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                    throw new InvalidOperationException("Shader error or unsupported: " + path);
                var mat = new Material(shader);
                try
                {
                    for (int p = 0; p < mat.passCount; p++)
                    {
                        if (mat.GetPassName(p).Contains("DEPTH")) mat.EnableKeyword("SHADOWS_DEPTH");
                        ShaderUtil.CompilePass(mat, p, true);
                        if (!ShaderUtil.IsPassCompiled(mat, p))
                            throw new InvalidOperationException($"Uncompiled pass {p} in {path}");
                    }
                    checks.Add("PASS D3D11 compiled " + path);
                }
                finally { UnityEngine.Object.DestroyImmediate(mat); }
            }
        }

        static ScenarioVisualReport CaptureScenarioSequence(FlatWorldApp app, FlatHazardKind kind, string outDir)
        {
            var map = app.GetComponentInChildren<FlatMapRenderer>();
            var nav = app.Navigation;
            var cam = app.GetComponentInChildren<Camera>();
            map.LocalWater.AutoWeatherEvents = false;
            map.LocalWater.ClearHazard();

            int focusTile = app.TriggerHazard(kind, preferredTile: -1, focusCamera: true);
            if (focusTile < 0 || !map.LocalWater.HasActiveHazard)
                throw new InvalidOperationException("Failed to start hazard via FlatWorldApp.TriggerHazard: " + kind);

            var initialMetrics = map.LocalWater.LastMetrics;
            double lon = ((initialMetrics.PatchX + initialMetrics.SpanTiles * 0.5) / app.World.Width - 0.5) * 360.0;
            double lat = ((initialMetrics.PatchY + initialMetrics.SpanTiles * 0.5) / app.World.Height - 0.5) * 180.0;

            var oldTarget = cam.targetTexture;
            var oldRect = cam.pixelRect;
            var oldActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(RenderWidth, RenderHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(RenderWidth, RenderHeight, TextureFormat.RGBA32, false);

            var scenarioReport = new ScenarioVisualReport
            {
                kind = kind.ToString(),
                title = FlatHazardMetrics.KindLabel(kind),
                patchX = initialMetrics.PatchX,
                patchY = initialMetrics.PatchY,
                focusTile = focusTile
            };

            try
            {
                cam.targetTexture = rt;
                cam.rect = new Rect(0, 0, 1, 1);
                nav.SetViewport(new Rect(0, 0, RenderWidth, RenderHeight));
                double day = app.CalendarDay;
                if (nav.IsAtlas) nav.GoGlobe(day);
                nav.Focus(focusTile, day);
                for (int f = 0; f < 140; f++) nav.Tick(0.04f, day);
                nav.Wheel(-1.5f, cam.pixelRect.center, day);
                for (int f = 0; f < 100; f++) nav.Tick(0.04f, day);
                nav.Recenter(new PlanetLocation(lon * Math.PI / 180.0, lat * Math.PI / 180.0), day);
                if (!nav.IsFlat || nav.IsAtlas)
                    throw new InvalidOperationException($"Camera did not enter local surface view for {kind} (IsFlat={nav.IsFlat}, IsAtlas={nav.IsAtlas}, Morph={nav.Morph}, Px={nav.PixelsPerTile})");

                // Clear sky clouds/night darkening for crystal-clear water optics inspection while keeping full normal-route water settings!
                var sunVec = FlatWeatherSimulation.Direction((lon - 28.0 + 180.0) / 360.0, Math.Clamp((lat + 16.0 + 90.0) / 180.0, 0.05, 0.95));
                var sunlight = new Vector4((float)sunVec.X, (float)sunVec.Y, (float)sunVec.Z, 0f);

                void SyncNormalRouteMaterial(float waterClock)
                {
                    map.Configure(false, nav.PixelsPerTile, waterClock);
                    OrbisAssetVisuals.BindWater(map.Material);
                    map.Material.SetFloat("_ShowWater", 1f);
                    map.Material.SetFloat("_ShowBanks", 1f);
                    map.Material.SetFloat("_WaveAmount", 1f);
                    map.Material.SetFloat("_Bathymetry", 1f);
                    map.Material.SetFloat("_Relief", 1f);
                    map.Material.SetFloat("_Vegetation", 1f);
                    map.Material.SetFloat("_ShowContours", 0f);
                    map.Material.SetVector("_FlatSun", sunlight);
                    map.Material.SetFloat("_FlatDayNight", 0f);
                    map.Material.SetFloat("_FlatRain", 0f);
                    map.Material.SetFloat("_FlatCloudShadows", 0f);
                    map.Material.SetFloat("_FlatClouds", 0f);
                    map.Material.SetFloat("_WaterClock", waterClock);
                    nav.SyncMaterials();
                }

                // Measure idle GPU cost (SWE disabled on the exact same camera & material)
                map.Material.SetFloat("_SWEReady", 0f);
                SyncNormalRouteMaterial(1.5f);
                scenarioReport.vendorWaterActive = nav.SurfaceMaterial.GetFloat("_VendorWaterReady") > 0.5f;
                if (!scenarioReport.vendorWaterActive)
                    throw new InvalidOperationException("Expected SW3 _VendorWaterReady == 1 on normal game route!");
                scenarioReport.gpuRenderIdleMs = MeasureCameraRenderMedianMs(cam, rt, tex);

                // Re-enable SWE at t=0 rest and capture 4 time-series stages:
                // 0: t=0s (rest), 1: advancing, 2: peak, 3: receding
                double totalT = initialMetrics.DurationSeconds;
                double[] targetTimes = ScoutStageTimes(app.World, kind, initialMetrics.PatchX, initialMetrics.PatchY, focusTile);
                string[] tags = { "00_rest", "01_advancing", "02_peak", "03_receding" };

                var workerTimes = new List<double>();
                var uploadTimes = new List<double>();
                var stageImages = new List<Color32[]>(4);
                Color32[] restPixels = null;

                double currentT = 0.0;
                for (int stageIdx = 0; stageIdx < targetTimes.Length; stageIdx++)
                {
                    double targetT = Math.Min(totalT, targetTimes[stageIdx]);
                    while (currentT + 1e-6 < targetT)
                    {
                        double stepDt = Math.Min(10.0, targetT - currentT);
                        var stepM = map.LocalWater.StepHazardSynchronous(stepDt, day);
                        workerTimes.Add(map.LocalWater.LastWorkerStepMs);
                        uploadTimes.Add(map.LocalWater.LastUploadMs);
                        currentT = stepM.SimSeconds;

                        // Mid-hazard camera-pan invariance check right before peak!
                        if (stageIdx == 2 && Math.Abs(currentT - targetT) < 1e-5)
                        {
                            int beforePatchX = map.LocalWater.LastMetrics.PatchX;
                            double beforeSimSec = map.LocalWater.LastMetrics.SimSeconds;
                            nav.PanBy(new Vector2(7.5f, -5.0f));
                            map.LocalWater.Tick(0.016f, nav.CenterTile.x, nav.CenterTile.y, true, false, day);
                            nav.Recenter(new PlanetLocation(lon * Math.PI / 180.0, lat * Math.PI / 180.0), day);
                            scenarioReport.cameraPanPreservedHazard =
                                map.LocalWater.HasActiveHazard &&
                                map.LocalWater.ActiveKind == kind &&
                                map.LocalWater.LastMetrics.PatchX == beforePatchX &&
                                Math.Abs(map.LocalWater.LastMetrics.SimSeconds - beforeSimSec) < 1e-6;
                            if (!scenarioReport.cameraPanPreservedHazard)
                                throw new InvalidOperationException("Camera pan reset or shifted the anchored hazard patch!");
                        }
                    }

                    if (stageIdx == 0)
                        map.LocalWater.StepHazardSynchronous(0.0, day);

                    SyncNormalRouteMaterial(1.5f + stageIdx * 1.35f);
                    if (nav.SurfaceMaterial.GetFloat("_SWEReady") < 0.5f)
                        throw new InvalidOperationException("_SWEReady did not propagate through SyncMaterials() to LocalSurface!");

                    if (stageIdx == 2)
                        scenarioReport.gpuRenderActiveMs = MeasureCameraRenderMedianMs(cam, rt, tex);

                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, RenderWidth, RenderHeight), 0, 0, false);
                    tex.Apply(false, false);

                    var colors = tex.GetPixels32();
                    ValidateFrameColors(colors, kind + "_" + tags[stageIdx]);
                    if (stageIdx == 0) restPixels = colors;
                    stageImages.Add(colors);

                    double changedFrac = CountChangedPixels(restPixels, colors, thresholdSum: 18) / (double)colors.Length;
                    string relFile = $"{kind.ToString().ToLowerInvariant()}_{tags[stageIdx]}.png";
                    File.WriteAllBytes(Path.Combine(outDir, relFile), tex.EncodeToPNG());

                    var m = map.LocalWater.LastMetrics;
                    scenarioReport.frames.Add(new FrameSnapshotSummary
                    {
                        tag = tags[stageIdx],
                        pngFile = relFile,
                        phase = m.Phase.ToString(),
                        simSeconds = m.SimSeconds,
                        inundatedDryCells = m.InundatedDryCells,
                        inundatedAreaKm2 = m.InundatedAreaKm2,
                        maxFloodDepthMeters = m.MaxFloodDepthMeters,
                        maxSpeedMs = m.MaxSpeedMs,
                        relativeMassError = m.RelativeMassError,
                        rainVolumeM3 = m.RainInputVolumeM3,
                        channelInflowVolumeM3 = m.ChannelInflowVolumeM3,
                        boundaryInflowVolumeM3 = m.BoundaryInflowVolumeM3,
                        boundaryOutflowVolumeM3 = m.BoundaryOutflowVolumeM3,
                        infiltrationVolumeM3 = m.InfiltrationVolumeM3,
                        seabedUpliftVolumeM3 = m.SeabedUpliftVolumeM3,
                        changedScreenFractionVsRest = changedFrac
                    });
                }

                workerTimes.Sort();
                uploadTimes.Sort();
                scenarioReport.workerStepMedianMs = workerTimes.Count > 0 ? workerTimes[workerTimes.Count / 2] : 0;
                scenarioReport.workerStepP95Ms = workerTimes.Count > 0 ? workerTimes[(int)((workerTimes.Count - 1) * 0.95)] : 0;
                scenarioReport.mainThreadUploadMedianMs = uploadTimes.Count > 0 ? uploadTimes[uploadTimes.Count / 2] : 0;
                scenarioReport.gpuDeltaMs = scenarioReport.gpuRenderActiveMs - scenarioReport.gpuRenderIdleMs;
                scenarioReport.peakChangedScreenFraction = scenarioReport.frames[2].changedScreenFractionVsRest;
                double peakVsRecedeDiff = CountChangedPixels(stageImages[2], stageImages[3], thresholdSum: 15) / (double)restPixels.Length;
                scenarioReport.recedeRecoveredFraction = peakVsRecedeDiff;

                if (scenarioReport.peakChangedScreenFraction < 0.015)
                    throw new InvalidOperationException($"{kind} inundation/wavefront was not visibly rendered on normal route (changedFrac={scenarioReport.peakChangedScreenFraction:P3})");
                if (peakVsRecedeDiff < 0.005)
                    throw new InvalidOperationException($"{kind} showed no visible change between peak and recession frames (diff={peakVsRecedeDiff:P3})");

                string stripFile = $"{kind.ToString().ToLowerInvariant()}_sequence_strip.png";
                WriteFourPanelStrip(Path.Combine(outDir, stripFile), stageImages, RenderWidth, RenderHeight);
                scenarioReport.stripPngFile = stripFile;
                return scenarioReport;
            }
            finally
            {
                cam.targetTexture = oldTarget;
                cam.pixelRect = oldRect;
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(tex);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static double[] ScoutStageTimes(WorldData world, FlatHazardKind kind, int patchX, int patchY, int focusTile)
        {
            var scout = FlatWaterHazardSession.CreateAtPatch(world, kind, patchX, patchY, focusTile);
            int nSteps = (int)Math.Ceiling(scout.DurationSeconds / 10.0);
            var times = new List<double>(nSteps);
            var counts = new List<int>(nSteps);
            int peakCount = -1;
            double peakTime = scout.DurationSeconds * 0.4;

            for (int i = 0; i < nSteps; i++)
            {
                scout.Step(10.0, default, maxSubsteps: 32);
                int c = scout.CountInundatedDryCells();
                times.Add(scout.Solver.Elapsed);
                counts.Add(c);
                if (c > peakCount)
                {
                    peakCount = c;
                    peakTime = scout.Solver.Elapsed;
                }
            }

            double advanceTime = Math.Max(30.0, peakTime * 0.45);
            int targetAdvanceCells = Math.Max(8, (int)(peakCount * 0.38));
            for (int i = 0; i < times.Count; i++)
            {
                if (times[i] >= peakTime) break;
                if (counts[i] >= targetAdvanceCells)
                {
                    advanceTime = times[i];
                    break;
                }
            }
            if (advanceTime >= peakTime - 15.0)
                advanceTime = Math.Max(20.0, peakTime * 0.5);

            double recedeTime = scout.DurationSeconds;
            return new[] { 0.0, advanceTime, peakTime, recedeTime };
        }

        static double MeasureCameraRenderMedianMs(Camera cam, RenderTexture rt, Texture2D onePixelTarget)
        {
            var samples = new double[9];
            for (int i = 0; i < 11; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                cam.Render();
                RenderTexture.active = rt;
                onePixelTarget.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                onePixelTarget.Apply(false, false);
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                if (i >= 2) samples[i - 2] = ms;
            }
            Array.Sort(samples);
            return samples[4];
        }

        static void ValidateFrameColors(Color32[] pixels, string label)
        {
            int magenta = 0, varied = 0;
            Color32 first = pixels[0];
            for (int i = 0; i < pixels.Length; i += 5)
            {
                var c = pixels[i];
                if (c.r > 235 && c.g < 20 && c.b > 235) magenta++;
                if (Math.Abs(c.r - first.r) + Math.Abs(c.g - first.g) + Math.Abs(c.b - first.b) > 15) varied++;
            }
            if (magenta > pixels.Length / 1000 || varied < 100)
                throw new InvalidOperationException($"Invalid rendered frame for {label}: magenta={magenta}, varied={varied}");
        }

        static int CountChangedPixels(Color32[] a, Color32[] b, int thresholdSum)
        {
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int diff = Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
                if (diff >= thresholdSum) changed++;
            }
            return changed;
        }

        static void WriteFourPanelStrip(string fullPath, List<Color32[]> panels, int srcW, int srcH)
        {
            int tileW = srcW / 2, tileH = srcH / 2;
            int pad = 6;
            int outW = tileW * 4 + pad * 5, outH = tileH + pad * 2;
            var outTex = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
            var buf = new Color32[outW * outH];
            var bg = new Color32(18, 28, 32, 255);
            Array.Fill(buf, bg);

            for (int p = 0; p < 4; p++)
            {
                var src = panels[p];
                int ox = pad + p * (tileW + pad);
                int oy = pad;
                for (int y = 0; y < tileH; y++)
                {
                    int sy = Math.Min(srcH - 1, y * 2);
                    for (int x = 0; x < tileW; x++)
                    {
                        int sx = Math.Min(srcW - 1, x * 2);
                        buf[(oy + y) * outW + (ox + x)] = src[sy * srcW + sx];
                    }
                }
            }
            outTex.SetPixels32(buf);
            outTex.Apply(false, false);
            File.WriteAllBytes(fullPath, outTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(outTex);
        }

        static void VerifyWorldSwitchCleanup(FlatWorldApp app, FullVisualQaReport report)
        {
            var oldMap = app.GetComponentInChildren<FlatMapRenderer>();
            if (!oldMap.LocalWater.HasActiveHazard)
                app.TriggerHazard(FlatHazardKind.Tsunami, preferredTile: -1, focusCamera: false);
            if (!oldMap.LocalWater.HasActiveHazard)
                throw new InvalidOperationException("Expected active hazard prior to world-switch test.");

            var nextWorld = FlatGenerator.Generate(256, 128, "WORLD-SWITCH-CLEANUP", TerrainPreset.Archipelago, default);
            typeof(FlatWorldApp).GetMethod("Install", Private).Invoke(app, new object[] { nextWorld });
            var newMap = app.GetComponentInChildren<FlatMapRenderer>();
            bool cleaned = newMap != null && newMap != oldMap &&
                           !newMap.LocalWater.HasActiveHazard &&
                           newMap.Material.GetFloat("_SWEReady") < 0.5f;
            if (!cleaned)
                throw new InvalidOperationException("World switch failed to clean up active local SWE hazard!");
            report.worldSwitchCleanupVerified = true;
        }

        static void WriteReportFiles(string outDir, FullVisualQaReport report)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "unity-d3d11-report.json"), JsonUtility.ToJson(report, true), Encoding.UTF8);

            var inv = CultureInfo.InvariantCulture;
            var md = new StringBuilder(4096);
            md.AppendLine("# ORBIS Local SWE Water Hazards — Unity 6000.0.55f1 D3D11 Validation Report");
            md.AppendLine();
            md.AppendLine($"- **Status**: `{report.status}`");
            md.AppendLine($"- **Unity / Graphics API**: `{report.unityVersion}` / `{report.graphicsApi}` (`{report.gpuDevice}`)");
            md.AppendLine($"- **Baseline Water Regressions Passed**: `{report.baselineWaterRegressionsPassed}`");
            md.AppendLine($"- **World Switch Cleanup Verified**: `{report.worldSwitchCleanupVerified}`");
            md.AppendLine();
            md.AppendLine("## Scenario Time-Series & CPU/GPU Cost Summary (1280×720 Normal Game Route)");
            md.AppendLine();
            md.AppendLine("| Scenario | Site `[x,y]` | Camera-Pan Invariant | Peak Inundated Cells | Peak Flood Depth (m) | Max Speed (m/s) | Worker Step Median / P95 (ms) | Upload Median (ms) | GPU Idle / Active / Delta (ms) | Max Mass RelErr |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var s in report.scenarios)
            {
                int maxCells = 0;
                double maxDepth = 0, maxSpd = 0, worstErr = 0;
                foreach (var f in s.frames)
                {
                    if (f.inundatedDryCells > maxCells) maxCells = f.inundatedDryCells;
                    if (f.maxFloodDepthMeters > maxDepth) maxDepth = f.maxFloodDepthMeters;
                    if (f.maxSpeedMs > maxSpd) maxSpd = f.maxSpeedMs;
                    if (f.relativeMassError > worstErr) worstErr = f.relativeMassError;
                }
                md.AppendLine(string.Format(inv,
                    "| {0} (`{1}`) | `[{2},{3}]` | `{4}` | {5} | {6:F2} | {7:F2} | {8:F3} / {9:F3} | {10:F3} | {11:F2} / {12:F2} / {13:+0.00;-0.00;0.00} | `{14:E2}` |",
                    s.title, s.kind, s.patchX, s.patchY, s.cameraPanPreservedHazard,
                    maxCells, maxDepth, maxSpd,
                    s.workerStepMedianMs, s.workerStepP95Ms, s.mainThreadUploadMedianMs,
                    s.gpuRenderIdleMs, s.gpuRenderActiveMs, s.gpuDeltaMs, worstErr));
            }
            md.AppendLine();
            foreach (var s in report.scenarios)
            {
                md.AppendLine($"### {s.title} (`{s.kind}`) — Strip: `{s.stripPngFile}`");
                foreach (var f in s.frames)
                {
                    md.AppendLine(string.Format(inv,
                        "- `{0}` (`{1}`, t={2:F0}s, phase=`{3}`): inundated={4} cells ({5:F2} km²), maxDepth={6:F2} m, maxSpeed={7:F2} m/s, screenChanged={8:P2}, relMassErr=`{9:E2}` (rain={10:E2}, chanIn={11:E2}, bcIn={12:E2}, bcOut={13:E2}, inf={14:E2}, bedUp={15:E2} m³)",
                        f.tag, f.pngFile, f.simSeconds, f.phase, f.inundatedDryCells, f.inundatedAreaKm2,
                        f.maxFloodDepthMeters, f.maxSpeedMs, f.changedScreenFractionVsRest, f.relativeMassError,
                        f.rainVolumeM3, f.channelInflowVolumeM3, f.boundaryInflowVolumeM3, f.boundaryOutflowVolumeM3,
                        f.infiltrationVolumeM3, f.seabedUpliftVolumeM3));
                }
                md.AppendLine();
            }
            File.WriteAllText(Path.Combine(outDir, "SUMMARY.md"), md.ToString(), Encoding.UTF8);
        }

        static void FailAndFinish(string outDir, string error)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "error.txt"), error, Encoding.UTF8);
            FinishSession(outDir, 1);
        }

        static void FinishSession(string outDir, int exitCode)
        {
            if (active != null)
            {
                ShaderUtil.allowAsyncCompilation = active.SavedAsyncCompile;
                active = null;
            }
            SessionState.SetBool(KeyActive, false);
            EditorApplication.update -= Tick;

            int exitFlag = SessionState.GetInt(KeyExit, -1);
            if (exitFlag == -2)
            {
                SessionState.SetInt(KeyExit, exitCode);
                EditorApplication.update -= ExitAfterPlay;
                EditorApplication.update += ExitAfterPlay;
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
            }
        }

        static void ExitAfterPlay()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            int code = SessionState.GetInt(KeyExit, -1);
            SessionState.SetInt(KeyExit, -1);
            EditorApplication.update -= ExitAfterPlay;
            if (code >= 0) EditorApplication.Exit(code);
        }
    }
}
