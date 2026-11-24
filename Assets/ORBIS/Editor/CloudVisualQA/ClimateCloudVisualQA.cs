using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbis.Editor.CloudVisualQA
{
    /// <summary>
    /// Independent Unity Editor acceptance entry point for climate-driven cloud visual QA.
    /// Reuses existing shared regressions (ClimateCloudRegression, HPCloudHeightRegression,
    /// WaterCloudRegression, LightningRegression) and NasaUpgrade3Col 79e6fa2 left-column baselines
    /// without modifying shared regression scripts, runtime code, or production shaders.
    ///
    /// Executes as a frame-paced EditorApplication.update queue so Unity destroys and releases
    /// each world's GPU RenderTextures and 3D noise caches between captures (zero VRAM buildup).
    ///
    /// Unity batch CLI:
    ///   Unity.exe -batchmode -force-d3d11 -projectPath &lt;repo&gt; \
    ///     -executeMethod Orbis.Editor.CloudVisualQA.ClimateCloudVisualQA.RunBatch \
    ///     -orbisCloudQaOut ValidationOutput/CloudVisualQA \
    ///     -orbisCloudQaMode full -orbisCloudQaQuality 2 -orbisCloudQaEarthWidth 512
    /// </summary>
    [InitializeOnLoad]
    public static class ClimateCloudVisualQA
    {
        const string ScenePath = "Assets/ORBIS/Scenes/Orbis2DWorld.unity";
        const string KeyActive = "Orbis.CloudVisualQA.Active";
        const string KeyOut = "Orbis.CloudVisualQA.Out";
        const string KeyMode = "Orbis.CloudVisualQA.Mode";
        const string KeyQuality = "Orbis.CloudVisualQA.Quality";
        const string KeyEarthWidth = "Orbis.CloudVisualQA.EarthWidth";
        const string KeyRunShared = "Orbis.CloudVisualQA.RunShared";
        const string KeyStart = "Orbis.CloudVisualQA.Start";
        const string KeyExit = "Orbis.CloudVisualQA.Exit";

        const int PairRenderWidth = 960;
        const int PairRenderHeight = 540;
        const int BaselineRenderWidth = 1600;
        const int BaselineRenderHeight = 900;
        const float CameraFovDeg = 42f;
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        sealed class PairedRunRecord
        {
            public PairedScenarioSpec Spec;
            public CoreForcingAuditSummary CoreAudit;
            public Float4Grid WeatherA, ColumnA, WeatherB, ColumnB, WeatherBPhaseShift;
            public double PurePhaseShiftRad;
            public RgbaImage ImageA, ImageB;
            public SkyColumnSummary SkyA, SkyB;
            public FieldDeltaStats ForcingDelta, SkyLowDelta, SkyConvDelta, ColumnDelta;
            public double ForcingToColumnR;
            public ScreenRoiDeltaStats ScreenStats;
            public double LatShiftForcing, LatShiftColumn, LatShiftScreen;
            public double DipoleForcing, DipoleColumn, DipoleScreen;
            public CycloneEvolutionMetrics Cyclone;
            public AntiNoiseBindingVerdict Verdict;
            public string SheetPngRelPath;
            public string ImageAPngRelPath;
            public string ImageBPngRelPath;
        }

        sealed class BaselineRunRecord
        {
            public Nasa79e6fa2ViewSpec Spec;
            public BaselineComparisonVerdict Verdict;
            public string SheetPngRelPath;
            public string CandidatePngRelPath;
            public double CandidateCloudOnOffMeanDn;
        }

        sealed class RunContext
        {
            public FlatWorldApp App;
            public string OutDir, Mode;
            public int Quality, EarthWidth;
            public bool RunShared;
            public bool SavedAsyncCompile;
            public WorldData BaseEarth;
            public NaturalCyclonePairResult PeakStorm;
            public readonly List<string> SharedResults = new List<string>();
            public bool SharedAllPassed = true;
            public readonly List<PairedRunRecord> PairRecords = new List<PairedRunRecord>();
            public readonly List<BaselineRunRecord> BaselineRecords = new List<BaselineRunRecord>();
            public readonly Queue<Action> Steps = new Queue<Action>();
            public int CooldownFrames;
        }

        static RunContext activeRun;

        static ClimateCloudVisualQA()
        {
            if (SessionState.GetBool(KeyActive, false))
                EditorApplication.update += Step;
            if (SessionState.GetInt(KeyExit, -1) >= 0)
                EditorApplication.update += ExitAfterPlay;
        }

        [MenuItem("Tools/ORBIS/Cloud Visual QA/Run Full Paired & 79e6fa2 Baseline Acceptance")]
        public static void RunFullMenu() => StartSession("ValidationOutput/CloudVisualQA", "full", 2, 512, true);

        [MenuItem("Tools/ORBIS/Cloud Visual QA/Run Paired Climate-Cloud Views Only")]
        public static void RunPairsMenu() => StartSession("ValidationOutput/CloudVisualQA_Pairs", "pairs", 2, 512, false);

        [MenuItem("Tools/ORBIS/Cloud Visual QA/Run 79e6fa2 Visual Baseline Audit Only")]
        public static void RunBaselineMenu() => StartSession("ValidationOutput/CloudVisualQA_Baseline", "baseline", 2, 512, false);

        public static void RunBatch()
        {
            string outDir = Arg("-orbisCloudQaOut") ?? "ValidationOutput/CloudVisualQA";
            string mode = (Arg("-orbisCloudQaMode") ?? "full").ToLowerInvariant();
            int quality = ParseIntArg("-orbisCloudQaQuality", 2, 0, 2);
            int earthW = ParseIntArg("-orbisCloudQaEarthWidth", 512, 256, 1024);
            bool runShared = ParseBoolArg("-orbisCloudQaRunSharedRegressions", mode == "full");
            StartSession(outDir, mode, quality, earthW, runShared);
        }

        public static void RunPairsBatch()
        {
            string outDir = Arg("-orbisCloudQaOut") ?? "ValidationOutput/CloudVisualQA_Pairs";
            int quality = ParseIntArg("-orbisCloudQaQuality", 2, 0, 2);
            int earthW = ParseIntArg("-orbisCloudQaEarthWidth", 512, 256, 1024);
            bool runShared = ParseBoolArg("-orbisCloudQaRunSharedRegressions", false);
            StartSession(outDir, "pairs", quality, earthW, runShared);
        }

        public static void RunBaselineBatch()
        {
            string outDir = Arg("-orbisCloudQaOut") ?? "ValidationOutput/CloudVisualQA_Baseline";
            int quality = ParseIntArg("-orbisCloudQaQuality", 2, 0, 2);
            int earthW = ParseIntArg("-orbisCloudQaEarthWidth", 512, 256, 1024);
            StartSession(outDir, "baseline", quality, earthW, false);
        }

        static string ResolveProjectPath(string path)
        {
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }

        static void StartSession(string outDir, string mode, int quality, int earthWidth, bool runShared)
        {
            string fullOut = ResolveProjectPath(outDir);
            Directory.CreateDirectory(fullOut);
            activeRun = null;
            SessionState.SetBool(KeyActive, true);
            SessionState.SetString(KeyOut, fullOut);
            SessionState.SetString(KeyMode, mode);
            SessionState.SetInt(KeyQuality, quality);
            SessionState.SetInt(KeyEarthWidth, earthWidth);
            SessionState.SetBool(KeyRunShared, runShared);
            SessionState.SetFloat(KeyStart, (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(KeyExit, -1);

            if (!EditorApplication.isPlaying)
                EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Step;
            EditorApplication.update += Step;
            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static int ParseIntArg(string name, int fallback, int min, int max)
        {
            string raw = Arg(name);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? Math.Max(min, Math.Min(max, v)) : fallback;
        }

        static bool ParseBoolArg(string name, bool fallback)
        {
            string raw = Arg(name);
            if (string.IsNullOrEmpty(raw)) return fallback;
            return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1";
        }

        static void Step()
        {
            if (!SessionState.GetBool(KeyActive, false))
            {
                EditorApplication.update -= Step;
                return;
            }
            string outDir = SessionState.GetString(KeyOut, Path.GetFullPath("ValidationOutput/CloudVisualQA"));
            double elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyStart, 0f);
            if (elapsed > 900.0)
            {
                CleanupRun();
                Finish(outDir, "Timed out during CloudVisualQA execution.", 2);
                return;
            }
            if (!EditorApplication.isPlaying) return;

            if (activeRun == null)
            {
                var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
                if (!app) return;
                typeof(FlatWorldApp).GetField("paused", Private)?.SetValue(app, true);
                typeof(FlatWorldApp).GetField("screenshotMode", Private)?.SetValue(app, true);
                if (app.World == null || app.Busy) return;
                var map = app.GetComponentInChildren<FlatMapRenderer>();
                if (!map || !map.Sky || !map.Sky.Ready || map.PreparationPending) return;

                try
                {
                    activeRun = BuildRunPlan(app, outDir,
                        SessionState.GetString(KeyMode, "full"),
                        SessionState.GetInt(KeyQuality, 2),
                        SessionState.GetInt(KeyEarthWidth, 512),
                        SessionState.GetBool(KeyRunShared, true));
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    File.WriteAllText(Path.Combine(outDir, "error.txt"), ex.ToString());
                    CleanupRun();
                    Finish(outDir, "ERROR: " + ex.Message, 2);
                }
                return;
            }

            if (activeRun.CooldownFrames > 0)
            {
                activeRun.CooldownFrames--;
                return;
            }

            if (activeRun.Steps.Count == 0)
            {
                CleanupRun();
                return;
            }

            var nextStep = activeRun.Steps.Dequeue();
            try
            {
                nextStep();
                // Yield one Editor tick after each GPU capture step so Unity flushes Destroy() and frees VRAM cleanly.
                if (activeRun != null) activeRun.CooldownFrames = 1;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                File.WriteAllText(Path.Combine(outDir, "error.txt"), ex.ToString());
                CleanupRun();
                Finish(outDir, "ERROR: " + ex.Message, 2);
            }
        }

        static void CleanupRun()
        {
            if (activeRun != null)
            {
                ShaderUtil.allowAsyncCompilation = activeRun.SavedAsyncCompile;
                activeRun = null;
            }
        }

        static RunContext BuildRunPlan(FlatWorldApp app, string outDir, string mode, int quality, int earthWidth, bool runShared)
        {
            var ctx = new RunContext
            {
                App = app,
                OutDir = outDir,
                Mode = mode,
                Quality = quality,
                EarthWidth = earthWidth,
                RunShared = runShared,
                SavedAsyncCompile = ShaderUtil.allowAsyncCompilation
            };
            ShaderUtil.allowAsyncCompilation = false;

            if (runShared)
            {
                ctx.Steps.Enqueue(() =>
                {
                    ctx.SharedAllPassed &= RunSharedCheck("ClimateCloudRegression.Run", () => ClimateCloudRegression.Run(), ctx.SharedResults);
                    ctx.SharedAllPassed &= RunSharedCheck("HPCloudHeightRegression.Run", () => HPCloudHeightRegression.Run(), ctx.SharedResults);
                    ctx.SharedAllPassed &= RunSharedCheck("WaterCloudRegression.Run", () => WaterCloudRegression.Run(), ctx.SharedResults);
                    ctx.SharedAllPassed &= RunSharedCheck("LightningRegression.Run", () => LightningRegression.Run(), ctx.SharedResults);
                });
            }

            ctx.Steps.Enqueue(() =>
            {
                var demAsset = Resources.Load<TextAsset>("ORBIS/FlatWorld/Earth" + earthWidth);
                var waterAsset = Resources.Load<TextAsset>("ORBIS/FlatWorld/EarthWater" + earthWidth);
                var riverAsset = Resources.Load<TextAsset>("ORBIS/FlatWorld/EarthRivers");
                if (!demAsset || !waterAsset || !riverAsset)
                    throw new InvalidOperationException($"Missing Earth{earthWidth} / EarthWater{earthWidth} / EarthRivers resources.");

                ctx.BaseEarth = ClimateCloudQaScenarios.BuildRealEarth(
                    earthWidth, demAsset.bytes, waterAsset.bytes, riverAsset.bytes, ClimateCloudQaScenarios.DefaultSeed);

                if (mode == "full" || mode == "pairs")
                {
                    Directory.CreateDirectory(Path.Combine(outDir, "pairs"));
                    var scenarios = ClimateCloudQaScenarios.BuildAllPairedScenarios(ctx.BaseEarth);
                    int skyWidth = quality == 0 ? 512 : 1024;
                    foreach (var spec in scenarios)
                    {
                        var rec = new PairedRunRecord
                        {
                            Spec = spec,
                            CoreAudit = ClimateCloudQaScenarios.AuditCoreForcing(spec)
                        };
                        ctx.PairRecords.Add(rec);
                        double calDayA = spec.HasNaturalCyclone ? spec.WorldA.Day + 0.25 : spec.WorldA.Day;
                        double calDayB = spec.HasNaturalCyclone ? spec.WorldB.Day + 0.25 : spec.WorldB.Day;

                        // Frame-paced: A, B, optional pure-phase check, then finalize/write sheet
                        ctx.Steps.Enqueue(() =>
                        {
                            Debug.Log($"[CloudVisualQA] Capturing {spec.Id} State A...");
                            CaptureWorldState(ctx.App, spec.WorldA, spec.Camera, calDayA, quality, skyWidth,
                                PairRenderWidth, PairRenderHeight, out rec.WeatherA, out rec.ColumnA, out rec.ImageA, out _);
                        });
                        ctx.Steps.Enqueue(() =>
                        {
                            Debug.Log($"[CloudVisualQA] Capturing {spec.Id} State B...");
                            CaptureWorldState(ctx.App, spec.WorldB, spec.Camera, calDayB, quality, skyWidth,
                                PairRenderWidth, PairRenderHeight, out rec.WeatherB, out rec.ColumnB, out rec.ImageB, out _);
                        });
                        if (spec.HasNaturalCyclone && spec.WorldB.Cyclones != null)
                        {
                            ctx.Steps.Enqueue(() =>
                            {
                                bool nh = spec.NaturalTrack1.V >= 0.5;
                                int spin = nh ? 1 : -1;
                                rec.PurePhaseShiftRad = spin * (Math.PI / 3.0);
                                var shifted = ClimateCloudQaScenarios.DeepCloneWorld(spec.WorldB);
                                if (nh)
                                {
                                    shifted.Cyclones.North.Phase += rec.PurePhaseShiftRad;
                                    shifted.Cyclones.PreviousNorth.Phase += rec.PurePhaseShiftRad;
                                }
                                else
                                {
                                    shifted.Cyclones.South.Phase += rec.PurePhaseShiftRad;
                                    shifted.Cyclones.PreviousSouth.Phase += rec.PurePhaseShiftRad;
                                }
                                shifted.TerrainRevision++;
                                InstallAndPublishSky(ctx.App, shifted, spec.Camera.FixedVisualClock, skyWidth, calDayB,
                                    out var mapS, out _, out _, out _, out _, out _);
                                rec.WeatherBPhaseShift = ReadRenderTextureGrid(mapS.Sky.Texture);
                            });
                        }
                        ctx.Steps.Enqueue(() => FinalizePairedRecord(rec, outDir));
                    }
                }

                if (mode == "full" || mode == "baseline")
                {
                    Directory.CreateDirectory(Path.Combine(outDir, "baseline_79e6fa2"));
                    var views = ClimateCloudQaScenarios.Build79e6fa2BaselineViews();
                    foreach (var view in views)
                    {
                        var capturedView = view;
                        ctx.Steps.Enqueue(() =>
                        {
                            if (capturedView.UseNaturalStormPeak && ctx.PeakStorm == null)
                                ctx.PeakStorm = ClimateCloudQaScenarios.FindNaturalCyclonePair(ctx.BaseEarth);
                            Debug.Log($"[CloudVisualQA] Capturing 79e6fa2 baseline view: {capturedView.Id}...");
                            ctx.BaselineRecords.Add(RunBaselineView(ctx.App, ctx.BaseEarth, capturedView, ctx.PeakStorm, outDir, quality));
                        });
                    }
                }

                ctx.Steps.Enqueue(() =>
                {
                    bool pairsPassed = true;
                    foreach (var p in ctx.PairRecords) pairsPassed &= p.Verdict.Passed;
                    bool baselinePassed = true;
                    foreach (var b in ctx.BaselineRecords) baselinePassed &= b.Verdict.Passed;
                    WriteReports(outDir, mode, quality, earthWidth, ctx.SharedResults, ctx.SharedAllPassed, ctx.PairRecords, ctx.BaselineRecords);

                    bool allOk = ctx.SharedAllPassed && pairsPassed && baselinePassed;
                    Debug.Log($"[CloudVisualQA] Suite finished: shared={ctx.SharedAllPassed} pairs={pairsPassed} ({ctx.PairRecords.Count}) baseline79={baselinePassed} ({ctx.BaselineRecords.Count}) -> {(allOk ? "PASS" : "FAIL")}");
                    CleanupRun();
                    Finish(outDir, allOk ? "PASS" : "FAIL", allOk ? 0 : 1);
                });
            });

            return ctx;
        }

        static bool RunSharedCheck(string label, Action action, List<string> log)
        {
            try
            {
                action();
                log.Add("PASS " + label);
                return true;
            }
            catch (Exception ex)
            {
                log.Add("FAIL " + label + " : " + ex.Message);
                Debug.LogException(ex);
                return false;
            }
        }

        static void FinalizePairedRecord(PairedRunRecord rec, string outDir)
        {
            var spec = rec.Spec;
            ClimateCloudQaScenarios.BuildForcingGrids(spec.WorldA, out var forcingA, out var auxA);
            ClimateCloudQaScenarios.BuildForcingGrids(spec.WorldB, out var forcingB, out var auxB);
            var primForcingA = spec.UseAuxForForcing ? auxA : forcingA;
            var primForcingB = spec.UseAuxForForcing ? auxB : forcingB;

            rec.SkyA = ClimateCloudQaMetrics.SummarizeSkyColumn(rec.WeatherA, rec.ColumnA, spec.TargetRoi);
            rec.SkyB = ClimateCloudQaMetrics.SummarizeSkyColumn(rec.WeatherB, rec.ColumnB, spec.TargetRoi);

            rec.ForcingDelta = ClimateCloudQaMetrics.ComputeFieldDelta(primForcingA, primForcingB, spec.PrimaryForcingChannel, spec.TargetRoi, spec.ControlRoi);
            rec.SkyLowDelta = ClimateCloudQaMetrics.ComputeFieldDelta(rec.WeatherA, rec.WeatherB, 0, spec.TargetRoi, spec.ControlRoi);
            rec.SkyConvDelta = ClimateCloudQaMetrics.ComputeFieldDelta(rec.WeatherA, rec.WeatherB, 2, spec.TargetRoi, spec.ControlRoi);
            rec.ColumnDelta = ClimateCloudQaMetrics.ComputeFieldDelta(rec.ColumnA, rec.ColumnB, 0, spec.TargetRoi, spec.ControlRoi);

            rec.ForcingToColumnR = ClimateCloudQaMetrics.ComputeDeltaPearsonR(
                primForcingA, primForcingB, spec.PrimaryForcingChannel, rec.ColumnA, rec.ColumnB, 0, spec.TargetRoi);

            rec.ScreenStats = EvaluateScreenRoiPair(rec.ImageA, rec.ImageB, spec.Camera, spec.TargetRoi,
                spec.HasSecondaryRoi ? (GeoBox?)spec.SecondaryRoi : null, spec.ControlRoi);

            if (spec.Expectation == PairExpectationKind.OrographicDipole && spec.HasSecondaryRoi)
            {
                var eastForc = ClimateCloudQaMetrics.ComputeFieldDelta(primForcingA, primForcingB, spec.PrimaryForcingChannel, spec.SecondaryRoi);
                var eastCol = ClimateCloudQaMetrics.ComputeFieldDelta(rec.ColumnA, rec.ColumnB, 0, spec.SecondaryRoi);
                rec.DipoleForcing = (rec.ForcingDelta.MeanA - rec.ForcingDelta.MeanB) - (eastForc.MeanA - eastForc.MeanB);
                rec.DipoleColumn = (rec.ColumnDelta.MeanA - rec.ColumnDelta.MeanB) - (eastCol.MeanA - eastCol.MeanB);
                rec.DipoleScreen = rec.ScreenStats.DipoleLumaDeltaDn;
            }
            else if (spec.Expectation == PairExpectationKind.NorthwardMigration)
            {
                double fLatA = ClimateCloudQaMetrics.ComputeLatitudeCentroid(forcingA, (r, g, b, a) => g, spec.TargetRoi, false);
                double fLatB = ClimateCloudQaMetrics.ComputeLatitudeCentroid(forcingB, (r, g, b, a) => g, spec.TargetRoi, false);
                double cLatA = ClimateCloudQaMetrics.ComputeLatitudeCentroid(rec.ColumnA, (r, g, b, a) => r + 0.5f * g, spec.TargetRoi, false);
                double cLatB = ClimateCloudQaMetrics.ComputeLatitudeCentroid(rec.ColumnB, (r, g, b, a) => r + 0.5f * g, spec.TargetRoi, false);
                rec.LatShiftForcing = fLatB - fLatA;
                rec.LatShiftColumn = cLatB - cLatA;
                rec.LatShiftScreen = rec.ScreenStats.LatCentroidDegB - rec.ScreenStats.LatCentroidDegA;
            }
            else if (spec.Expectation == PairExpectationKind.PolewardExpansion)
            {
                double fLatA = ClimateCloudQaMetrics.ComputeLatitudeCentroid(forcingA, (r, g, b, a) => g, spec.TargetRoi, true);
                double fLatB = ClimateCloudQaMetrics.ComputeLatitudeCentroid(forcingB, (r, g, b, a) => g, spec.TargetRoi, true);
                double cLatA = ClimateCloudQaMetrics.ComputeLatitudeCentroid(rec.WeatherA, (r, g, b, a) => r * b, spec.TargetRoi, true);
                double cLatB = ClimateCloudQaMetrics.ComputeLatitudeCentroid(rec.WeatherB, (r, g, b, a) => r * b, spec.TargetRoi, true);
                rec.LatShiftForcing = fLatB - fLatA;
                rec.LatShiftColumn = cLatB - cLatA;
                rec.LatShiftScreen = rec.ScreenStats.LatCentroidDegB - rec.ScreenStats.LatCentroidDegA;
            }
            else if (spec.Expectation == PairExpectationKind.CycloneEvolution && spec.HasNaturalCyclone)
            {
                rec.Cyclone = ClimateCloudQaMetrics.MeasureCycloneCloudEvolution(
                    rec.WeatherA, rec.WeatherB,
                    spec.NaturalTrack0.U, spec.NaturalTrack0.V, spec.NaturalTrack0.WindSpeed, spec.NaturalTrack0.Strength, spec.NaturalTrack0.Phase,
                    spec.NaturalTrack1.U, spec.NaturalTrack1.V, spec.NaturalTrack1.WindSpeed, spec.NaturalTrack1.Strength, spec.NaturalTrack1.Phase,
                    rec.WeatherBPhaseShift, rec.PurePhaseShiftRad);
                rec.Cyclone.ScreenShiftAlignmentCos = EvaluateScreenCycloneShiftAlignment(
                    rec.ImageA, rec.ImageB, spec.Camera,
                    rec.Cyclone.Track0LonDeg, rec.Cyclone.Track0LatDeg,
                    rec.Cyclone.Track1LonDeg, rec.Cyclone.Track1LatDeg);
            }

            rec.Verdict = ClimateCloudQaMetrics.EvaluateAntiNoiseBinding(
                spec.Expectation,
                rec.ForcingDelta, rec.SkyLowDelta, rec.SkyConvDelta, rec.ColumnDelta,
                rec.ForcingToColumnR, rec.ScreenStats,
                rec.LatShiftForcing, rec.LatShiftColumn, rec.LatShiftScreen,
                rec.DipoleForcing, rec.DipoleColumn, rec.DipoleScreen,
                rec.Cyclone);

            if (spec.EvaluateConvectionTop)
            {
                if (spec.Id == "01_moist_warm_vs_dry_stable")
                {
                    double dTop = rec.SkyA.CloudWeightedTopMeters - rec.SkyB.CloudWeightedTopMeters;
                    rec.Verdict.AddCheck(dTop > 600.0 && rec.SkyA.Convection > rec.SkyB.Convection + 0.04,
                        "MISSING_CONVECTIVE_TOP_RESPONSE",
                        $"moist/warm convective cloud top={rec.SkyA.CloudWeightedTopMeters:F0} m vs dry/stable={rec.SkyB.CloudWeightedTopMeters:F0} m (dTop=+{dTop:F0} m >600 m, conv {rec.SkyA.Convection:F3} vs {rec.SkyB.Convection:F3})");
                    rec.Verdict.Passed = rec.Verdict.Failures.Count == 0;
                }
                else if (spec.Id == "03_sst_cold_vs_warm_marine_sc")
                {
                    rec.Verdict.AddCheck(rec.SkyA.MarineScTierFraction > rec.SkyB.MarineScTierFraction + 0.05 ||
                                         rec.SkyA.Convection < rec.SkyB.Convection - 0.015,
                        "MISSING_MARINE_SC_ALTITUDE_TIER",
                        $"cold SST marine-Sc low tier frac={rec.SkyA.MarineScTierFraction:F3} vs warm={rec.SkyB.MarineScTierFraction:F3}, mean conv={rec.SkyA.Convection:F3} vs {rec.SkyB.Convection:F3} (top {rec.SkyA.EstimatedTopMeters:F0} vs {rec.SkyB.EstimatedTopMeters:F0} m)");
                    rec.Verdict.Passed = rec.Verdict.Failures.Count == 0;
                }
            }

            string relA = $"pairs/{spec.Id}_A.png";
            string relB = $"pairs/{spec.Id}_B.png";
            string relSheet = $"pairs/{spec.Id}_sheet.png";
            File.WriteAllBytes(Path.Combine(outDir, relA), OrbisRasterCodec.RGBA8(rec.ImageA.Width, rec.ImageA.Height, rec.ImageA.Rgba));
            File.WriteAllBytes(Path.Combine(outDir, relB), OrbisRasterCodec.RGBA8(rec.ImageB.Width, rec.ImageB.Height, rec.ImageB.Rgba));
            var sheet = ClimateCloudQaMetrics.ComposePairSheet(rec.ImageA, rec.ImageB, rec.Verdict.Passed);
            File.WriteAllBytes(Path.Combine(outDir, relSheet), OrbisRasterCodec.RGBA8(sheet.Width, sheet.Height, sheet.Rgba));
            rec.ImageAPngRelPath = relA;
            rec.ImageBPngRelPath = relB;
            rec.SheetPngRelPath = relSheet;

            // Release intermediate buffers immediately to keep managed memory flat
            rec.WeatherA = rec.ColumnA = rec.WeatherB = rec.ColumnB = rec.WeatherBPhaseShift = null;
            rec.ImageA = rec.ImageB = null;
        }

        static BaselineRunRecord RunBaselineView(
            FlatWorldApp app, WorldData baseEarth, Nasa79e6fa2ViewSpec view,
            NaturalCyclonePairResult peakStorm, string outDir, int quality)
        {
            WorldData world;
            double camLon = view.CenterLonDeg, camLat = view.CenterLatDeg, day = view.CalendarDay;
            double stormNormX = 0.0, stormNormY = 0.18;

            if (view.UseNaturalStormPeak && peakStorm != null && peakStorm.Found)
            {
                world = ClimateCloudQaScenarios.DeepCloneWorld(peakStorm.WorldT1);
                double sLon = (peakStorm.Track1.U - 0.5) * 360.0;
                double sLat = (peakStorm.Track1.V - 0.5) * 180.0;
                camLon = sLon;
                camLat = Math.Max(-55.0, Math.Min(55.0, sLat + (sLat >= 0 ? 8.0 : -8.0)));
                double fracDay = Wrap01(0.5 - (camLon + view.SunLonOffsetDeg) / 360.0);
                day = Math.Floor(peakStorm.Day1) + fracDay;

                double dLatRad = (sLat - camLat) * Math.PI / 180.0;
                double denom = (view.OrbitalDistance - Math.Cos(dLatRad)) * Math.Tan(CameraFovDeg * 0.5 * Math.PI / 180.0) * 0.88;
                stormNormX = 0.0;
                stormNormY = denom > 1e-4 ? -Math.Sin(dLatRad) / denom : (sLat >= 0 ? 0.18 : -0.18);
            }
            else
            {
                world = ClimateCloudQaScenarios.DeepCloneWorld(baseEarth);
                world.Day = day;
                world.Cyclones = FlatCycloneWeather.SpinUp(world);
            }

            var camCfg = new LockedViewConfig
            {
                CenterLonDeg = camLon,
                CenterLatDeg = camLat,
                OrbitalDistance = view.OrbitalDistance,
                UseFixedSunVector = false, // Production FlatSkyRenderer.Sun(180f, day, true) matching NasaUpgrade3Col
                FixedVisualClock = ClimateCloudQaScenarios.DefaultVisualClock,
                FixedExposure = ClimateCloudQaScenarios.DefaultExposure,
                NoiseSeed = baseEarth.Seed,
                LockedSpinDay = day
            };

            int skyWidth = quality == 0 ? 512 : 1024;
            double stormSampleDay = view.UseNaturalStormPeak ? world.Day + 0.25 : day;
            CaptureWorldState(app, world, camCfg, stormSampleDay, quality, skyWidth,
                BaselineRenderWidth, BaselineRenderHeight, out _, out _, out var full16x9, out double cloudOnOffMeanDn);

            int sq = Math.Min(full16x9.Width, full16x9.Height);
            var candidateSq = full16x9.Crop((full16x9.Width - sq) / 2, (full16x9.Height - sq) / 2, sq, sq)
                .ResizeBilinear(ClimateCloudQaMetrics.NasaFullDiskSize, ClimateCloudQaMetrics.NasaFullDiskSize);

            string refPath = ResolveProjectPath(view.ReferencePngRelPath);
            if (!File.Exists(refPath))
                throw new FileNotFoundException("Missing NasaUpgrade3Col 79e6fa2 reference PNG: " + refPath);
            var refSheet = OrbisRasterCodec.Read(File.ReadAllBytes(refPath));
            var refImg = new RgbaImage(refSheet.width, refSheet.height, refSheet.rgba);
            var ref79Left = refImg.Crop(
                ClimateCloudQaMetrics.NasaFullDiskLeftX,
                ClimateCloudQaMetrics.NasaFullDiskTopY,
                ClimateCloudQaMetrics.NasaFullDiskSize,
                ClimateCloudQaMetrics.NasaFullDiskSize);

            var metrics79 = ClimateCloudQaMetrics.AnalyzeFullDisk(ref79Left, view.Id + "_79e6fa2", view.CheckStormReadability, 0.0, 0.21);
            var metricsCand = ClimateCloudQaMetrics.AnalyzeFullDisk(candidateSq, view.Id + "_candidate", view.CheckStormReadability, stormNormX, stormNormY);
            var verdict = ClimateCloudQaMetrics.CompareAgainst79e6fa2(view.Id, metrics79, metricsCand, view.CheckStormReadability);

            string relCand = $"baseline_79e6fa2/{view.Id}_candidate.png";
            string relSheet = $"baseline_79e6fa2/{view.Id}_vs_79e6fa2.png";
            File.WriteAllBytes(Path.Combine(outDir, relCand), OrbisRasterCodec.RGBA8(candidateSq.Width, candidateSq.Height, candidateSq.Rgba));
            var sheet = ClimateCloudQaMetrics.ComposeBaselineSheet(ref79Left, candidateSq, verdict.Passed);
            File.WriteAllBytes(Path.Combine(outDir, relSheet), OrbisRasterCodec.RGBA8(sheet.Width, sheet.Height, sheet.Rgba));

            return new BaselineRunRecord
            {
                Spec = view,
                Verdict = verdict,
                CandidatePngRelPath = relCand,
                SheetPngRelPath = relSheet,
                CandidateCloudOnOffMeanDn = cloudOnOffMeanDn
            };
        }

        static void InstallAndPublishSky(
            FlatWorldApp app, WorldData world, double fixedVisualClock, int skyWidth, double cycloneCalendarDay,
            out FlatMapRenderer map, out FlatGlobePresenter globe, out FlatScrollNavigator nav,
            out Camera camera, out OrbisCelestialLens lens, out OrbisAtmosphereRenderer atmosphere)
        {
            typeof(FlatWorldApp).GetMethod("Install", Private).Invoke(app, new object[] { world });
            typeof(FlatWorldApp).GetField("paused", Private)?.SetValue(app, true);
            typeof(FlatWorldApp).GetField("screenshotMode", Private)?.SetValue(app, true);

            camera = typeof(FlatWorldApp).GetField("viewCamera", Private).GetValue(app) as Camera;
            map = typeof(FlatWorldApp).GetField("map", Private).GetValue(app) as FlatMapRenderer;
            globe = typeof(FlatWorldApp).GetField("globe", Private).GetValue(app) as FlatGlobePresenter;
            nav = app.Navigation;
            lens = camera.GetComponent<OrbisCelestialLens>();
            var targetView = globe.View;
            atmosphere = Array.Find(camera.GetComponents<OrbisAtmosphereRenderer>(), a => a.enabled && a.view == targetView);

            var sky = map.Sky;
            map.SetLayer(MapLayer.Natural);
            var clockProp = typeof(FlatSkyRenderer).GetProperty("Clock", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            clockProp?.SetValue(sky, fixedVisualClock);
            var pendingStripField = typeof(FlatSkyRenderer).GetField("pendingStrip", Private);

            long expectedStep = (long)Math.Floor(world.Day * 4.0);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 20.0)
            {
                map.Flush();
                clockProp?.SetValue(sky, fixedVisualClock);
                sky.Tick(0f, true, false, false, skyWidth, cycloneCalendarDay);
                int strip = pendingStripField != null ? (int)pendingStripField.GetValue(sky) : -1;
                if (sky.Ready && !map.PreparationPending && strip < 0 &&
                    sky.RegionalClimateStep.HasValue && sky.RegionalClimateStep.Value == expectedStep &&
                    Math.Abs(sky.RenderClock - fixedVisualClock) < 1e-3)
                    break;
                Thread.Sleep(2);
            }

            if (!sky.Ready)
                throw new InvalidOperationException("Production FlatSkyRenderer did not publish a ready weather/column pair.");
            if (Math.Abs(sky.Clock - fixedVisualClock) > 1e-3 || Math.Abs(sky.RenderClock - fixedVisualClock) > 1e-3)
                throw new InvalidOperationException($"Visual clock lock violated: Clock={sky.Clock}, RenderClock={sky.RenderClock}, expected={fixedVisualClock}.");
        }

        static void CaptureWorldState(
            FlatWorldApp app, WorldData world, LockedViewConfig cam, double cycloneCalendarDay,
            int quality, int skyWidth, int renderW, int renderH,
            out Float4Grid weatherGrid, out Float4Grid columnGrid, out RgbaImage renderedImage, out double cloudOnOffMeanDn)
        {
            InstallAndPublishSky(app, world, cam.FixedVisualClock, skyWidth, cycloneCalendarDay,
                out var map, out var globe, out var nav, out var camera, out var lens, out var atmosphere);

            var sky = map.Sky;
            weatherGrid = ReadRenderTextureGrid(sky.Texture);
            columnGrid = ReadRenderTextureGrid(sky.ColumnTexture);

            var oldTarget = camera.targetTexture;
            var oldRect = camera.pixelRect;
            var oldActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(renderW, renderH, 24, RenderTextureFormat.ARGB32);
            var readTex = new Texture2D(renderW, renderH, TextureFormat.RGBA32, false);
            try
            {
                double spinDay = cam.LockedSpinDay;
                var loc = PlanetCoordinateSystem.FromMapUV((cam.CenterLonDeg + 180.0) / 360.0, (cam.CenterLatDeg + 90.0) / 180.0);
                nav.Home(spinDay);
                nav.Tick(3f, spinDay);
                nav.Recenter(loc, spinDay);
                nav.SetViewport(new Rect(0, 0, renderW, renderH));
                nav.Tick(0f, spinDay);

                Vector3 localDir = SurfaceNormalLocal(cam.CenterLonDeg, cam.CenterLatDeg);
                Vector3 localNorth = NorthTangentLocal(cam.CenterLonDeg, cam.CenterLatDeg);
                Quaternion spinQ = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                Vector3 worldNormal = (spinQ * ReflectZ(localDir)).normalized;
                Vector3 worldNorth = (spinQ * ReflectZ(localNorth)).normalized;

                camera.targetTexture = rt;
                camera.orthographic = false;
                camera.fieldOfView = CameraFovDeg;
                camera.nearClipPlane = 0.002f;
                camera.farClipPlane = 80f;
                camera.ResetProjectionMatrix();
                camera.transform.SetPositionAndRotation(worldNormal * cam.OrbitalDistance, Quaternion.LookRotation(-worldNormal, worldNorth));

                var settings = globe.Snapshot.settings;
                settings.exposure = cam.FixedExposure;
                settings.cloudQuality = quality;
                settings.cloudCover = 1f;
                settings.cloudDensity = 1f;
                settings.clouds = true;
                settings.atmosphere = true;
                settings.night = true;
                if (lens) lens.strength = 0f;
                if (atmosphere) { atmosphere.fullResolutionReference = false; atmosphere.referenceCloudIntegration = false; }

                Vector3 sunLocal = cam.UseFixedSunVector
                    ? SurfaceNormalLocal(cam.SunLonDeg, cam.SunLatDeg)
                    : sky.Sun(180f, spinDay, true);

                map.Configure(false, nav.PixelsPerTile, 0f);
                map.SetVegetationDay(world.Day);
                sky.Bind(map.Material, sunLocal, true, true, true, true, false, false, true, 0f);
                map.Material.SetFloat("_WaterClock", 0f);
                nav.SyncMaterials();
                globe.TickScroll(0f, cam.FixedVisualClock, world.Day, nav.DisplaySpin, 0f, true, true, true, true, sunLocal, true);

                if (globe.View.CloudMaterial.GetTexture("_ScrollWeatherTex") != sky.Texture ||
                    globe.View.CloudMaterial.GetTexture("_ScrollCloudColumn") != sky.ColumnTexture)
                    throw new InvalidOperationException("Volume CloudMaterial is not bound to production FlatSkyRenderer outputs.");

                camera.Render();
                RenderTexture.active = rt;
                readTex.ReadPixels(new Rect(0, 0, renderW, renderH), 0, 0);
                readTex.Apply(false, false);
                var onPixels = readTex.GetPixels32();
                renderedImage = FlipToNorthFirstRgba(renderW, renderH, onPixels);

                var volMat = globe.View.CloudMaterial;
                float prevClouds = volMat.GetFloat("_Clouds"), prevShafts = volMat.GetFloat("_GodRays");
                volMat.SetFloat("_Clouds", 0f);
                volMat.SetFloat("_GodRays", 0f);
                camera.Render();
                RenderTexture.active = rt;
                readTex.ReadPixels(new Rect(0, 0, renderW, renderH), 0, 0);
                readTex.Apply(false, false);
                var offPixels = readTex.GetPixels32();
                volMat.SetFloat("_Clouds", prevClouds);
                volMat.SetFloat("_GodRays", prevShafts);

                double diffSum = 0;
                for (int i = 0; i < onPixels.Length; i++)
                {
                    double la = 0.2126 * onPixels[i].r + 0.7152 * onPixels[i].g + 0.0722 * onPixels[i].b;
                    double lb = 0.2126 * offPixels[i].r + 0.7152 * offPixels[i].g + 0.0722 * offPixels[i].b;
                    diffSum += Math.Abs(la - lb);
                }
                cloudOnOffMeanDn = diffSum / Math.Max(1, onPixels.Length);
            }
            finally
            {
                camera.targetTexture = oldTarget;
                nav.SetViewport(oldRect);
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(readTex);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static Float4Grid ReadRenderTextureGrid(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            bool oldSrgb = GL.sRGBWrite;
            var tmp = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                GL.sRGBWrite = false;
                Graphics.Blit(rt, tmp);
                RenderTexture.active = tmp;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply(false, false);
                var pixels = tex.GetPixels();
                var grid = new Float4Grid(rt.width, rt.height);
                for (int i = 0; i < pixels.Length; i++)
                {
                    grid.R[i] = pixels[i].r;
                    grid.G[i] = pixels[i].g;
                    grid.B[i] = pixels[i].b;
                    grid.A[i] = pixels[i].a;
                }
                return grid;
            }
            finally
            {
                RenderTexture.active = prev;
                GL.sRGBWrite = oldSrgb;
                RenderTexture.ReleaseTemporary(tmp);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        static RgbaImage FlipToNorthFirstRgba(int w, int h, Color32[] bottomUp)
        {
            var dst = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int srcRow = (h - 1 - y) * w;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    var c = bottomUp[srcRow + x];
                    int p = (dstRow + x) * 4;
                    dst[p] = c.r; dst[p + 1] = c.g; dst[p + 2] = c.b; dst[p + 3] = 255;
                }
            }
            return new RgbaImage(w, h, dst);
        }

        static ScreenRoiDeltaStats EvaluateScreenRoiPair(
            RgbaImage imgA, RgbaImage imgB, LockedViewConfig cam,
            GeoBox targetRoi, GeoBox? secondaryRoi, GeoBox controlRoi)
        {
            var stats = new ScreenRoiDeltaStats();
            int w = imgA.Width, h = imgA.Height;
            Vector3 upDir = SurfaceNormalLocal(cam.CenterLonDeg, cam.CenterLatDeg);
            Vector3 northDir = NorthTangentLocal(cam.CenterLonDeg, cam.CenterLatDeg);
            Vector3 eastDir = Vector3.Cross(northDir, upDir).normalized;
            Vector3 camOrigin = upDir * cam.OrbitalDistance;
            float tanHalfY = Mathf.Tan(CameraFovDeg * 0.5f * Mathf.Deg2Rad);
            float tanHalfX = tanHalfY * ((float)w / h);

            double sumLA = 0, sumLB = 0, absL = 0, sumCsA = 0, sumCsB = 0;
            int brightA = 0, brightB = 0;
            double latNumA = 0, latDenA = 0, latNumB = 0, latDenB = 0;
            double secLA = 0, secLB = 0; int secN = 0;
            double ctrlAbs = 0;

            for (int y = 0; y < h; y++)
            {
                float ndcY = 1f - 2f * ((y + 0.5f) / h);
                for (int x = 0; x < w; x++)
                {
                    float ndcX = 2f * ((x + 0.5f) / w) - 1f;
                    Vector3 rd = (-upDir + eastDir * (ndcX * tanHalfX) + northDir * (ndcY * tanHalfY)).normalized;
                    float b = Vector3.Dot(camOrigin, rd);
                    float disc = b * b - (camOrigin.sqrMagnitude - 1f);
                    if (disc < 0f) continue;
                    float t = -b - Mathf.Sqrt(disc);
                    if (t <= 0f) continue;
                    Vector3 hit = (camOrigin + rd * t).normalized;
                    double lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, hit.y))) * 180.0 / Math.PI;
                    double lon = Math.Atan2(hit.x, hit.z) * 180.0 / Math.PI;

                    int p = (y * w + x) * 4;
                    double la = 0.2126 * imgA.Rgba[p] + 0.7152 * imgA.Rgba[p + 1] + 0.0722 * imgA.Rgba[p + 2];
                    double lb = 0.2126 * imgB.Rgba[p] + 0.7152 * imgB.Rgba[p + 1] + 0.0722 * imgB.Rgba[p + 2];
                    float csa = ClimateCloudQaMetrics.PixelCloudScore(imgA.Rgba[p], imgA.Rgba[p + 1], imgA.Rgba[p + 2]);
                    float csb = ClimateCloudQaMetrics.PixelCloudScore(imgB.Rgba[p], imgB.Rgba[p + 1], imgB.Rgba[p + 2]);

                    if (targetRoi.Contains(lon, lat))
                    {
                        stats.TargetPixels++;
                        sumLA += la; sumLB += lb; absL += Math.Abs(lb - la);
                        sumCsA += csa; sumCsB += csb;
                        if (csa >= 0.22f) brightA++;
                        if (csb >= 0.22f) brightB++;
                        latNumA += csa * lat; latDenA += csa;
                        latNumB += csb * lat; latDenB += csb;
                    }
                    if (secondaryRoi.HasValue && secondaryRoi.Value.Contains(lon, lat))
                    {
                        secN++; secLA += la; secLB += lb;
                    }
                    if (controlRoi.Contains(lon, lat))
                    {
                        stats.ControlPixels++;
                        ctrlAbs += Math.Abs(lb - la);
                    }
                }
            }

            if (stats.TargetPixels > 0)
            {
                stats.MeanLumaA = sumLA / stats.TargetPixels;
                stats.MeanLumaB = sumLB / stats.TargetPixels;
                stats.SignedLumaDeltaDn = stats.MeanLumaB - stats.MeanLumaA;
                stats.MeanAbsLumaDeltaDn = absL / stats.TargetPixels;
                stats.DirectionalCoherence = Math.Abs(stats.SignedLumaDeltaDn) / (stats.MeanAbsLumaDeltaDn + 1e-6);
                stats.BrightFractionA = (double)brightA / stats.TargetPixels;
                stats.BrightFractionB = (double)brightB / stats.TargetPixels;
                stats.CloudScoreA = sumCsA / stats.TargetPixels;
                stats.CloudScoreB = sumCsB / stats.TargetPixels;
                stats.SignedCloudScoreDelta = stats.CloudScoreB - stats.CloudScoreA;
                stats.LatCentroidDegA = latDenA > 1e-6 ? latNumA / latDenA : 0;
                stats.LatCentroidDegB = latDenB > 1e-6 ? latNumB / latDenB : 0;
            }
            if (secN > 0 && stats.TargetPixels > 0)
            {
                double secDelta = (secLB - secLA) / secN;
                double priDelta = stats.MeanLumaA - stats.MeanLumaB;
                stats.DipoleLumaDeltaDn = priDelta + secDelta;
            }
            if (stats.ControlPixels > 0)
            {
                stats.ControlMeanAbsLumaDeltaDn = ctrlAbs / stats.ControlPixels;
                stats.ControlLeakageRatio = stats.ControlMeanAbsLumaDeltaDn / (stats.MeanAbsLumaDeltaDn + 1e-6);
            }
            return stats;
        }

        static double EvaluateScreenCycloneShiftAlignment(
            RgbaImage imgA, RgbaImage imgB, LockedViewConfig cam,
            double lon0, double lat0, double lon1, double lat1)
        {
            int w = imgA.Width, h = imgA.Height;
            Vector3 upDir = SurfaceNormalLocal(cam.CenterLonDeg, cam.CenterLatDeg);
            Vector3 northDir = NorthTangentLocal(cam.CenterLonDeg, cam.CenterLatDeg);
            Vector3 eastDir = Vector3.Cross(northDir, upDir).normalized;
            Vector3 camOrigin = upDir * cam.OrbitalDistance;
            float tanHalfY = Mathf.Tan(CameraFovDeg * 0.5f * Mathf.Deg2Rad);
            float tanHalfX = tanHalfY * ((float)w / h);

            double sumLonA = 0, sumLatA = 0, wtA = 0;
            double sumLonB = 0, sumLatB = 0, wtB = 0;
            for (int y = 0; y < h; y += 2)
            {
                float ndcY = 1f - 2f * ((y + 0.5f) / h);
                for (int x = 0; x < w; x += 2)
                {
                    float ndcX = 2f * ((x + 0.5f) / w) - 1f;
                    Vector3 rd = (-upDir + eastDir * (ndcX * tanHalfX) + northDir * (ndcY * tanHalfY)).normalized;
                    float b = Vector3.Dot(camOrigin, rd);
                    float disc = b * b - (camOrigin.sqrMagnitude - 1f);
                    if (disc < 0f) continue;
                    float t = -b - Mathf.Sqrt(disc);
                    if (t <= 0f) continue;
                    Vector3 hit = (camOrigin + rd * t).normalized;
                    double lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, hit.y))) * 180.0 / Math.PI;
                    double lon = Math.Atan2(hit.x, hit.z) * 180.0 / Math.PI;

                    int p = (y * w + x) * 4;
                    float csa = ClimateCloudQaMetrics.PixelCloudScore(imgA.Rgba[p], imgA.Rgba[p + 1], imgA.Rgba[p + 2]);
                    float csb = ClimateCloudQaMetrics.PixelCloudScore(imgB.Rgba[p], imgB.Rgba[p + 1], imgB.Rgba[p + 2]);
                    double d0 = ClimateCloudQaMetrics.SphericalDistanceDeg(lon, lat, lon0, lat0);
                    double d1 = ClimateCloudQaMetrics.SphericalDistanceDeg(lon, lat, lon1, lat1);
                    if (d0 <= 9.0)
                    {
                        double wt = csa * csa * Math.Exp(-(d0 * d0) / 40.0);
                        sumLonA += wt * ClimateCloudQaMetrics.WrapDeltaDeg(lon - lon0);
                        sumLatA += wt * (lat - lat0);
                        wtA += wt;
                    }
                    if (d1 <= 9.0)
                    {
                        double wt = csb * csb * Math.Exp(-(d1 * d1) / 40.0);
                        sumLonB += wt * ClimateCloudQaMetrics.WrapDeltaDeg(lon - lon1);
                        sumLatB += wt * (lat - lat1);
                        wtB += wt;
                    }
                }
            }
            double obsLon0 = lon0 + (wtA > 1e-6 ? sumLonA / wtA : 0);
            double obsLat0 = lat0 + (wtA > 1e-6 ? sumLatA / wtA : 0);
            double obsLon1 = lon1 + (wtB > 1e-6 ? sumLonB / wtB : 0);
            double obsLat1 = lat1 + (wtB > 1e-6 ? sumLatB / wtB : 0);
            double cosM = Math.Cos(0.5 * (lat0 + lat1) * Math.PI / 180.0);
            double ox = ClimateCloudQaMetrics.WrapDeltaDeg(obsLon1 - obsLon0) * cosM, oy = obsLat1 - obsLat0;
            double tx = ClimateCloudQaMetrics.WrapDeltaDeg(lon1 - lon0) * cosM, ty = lat1 - lat0;
            double on = Math.Sqrt(ox * ox + oy * oy), tn = Math.Sqrt(tx * tx + ty * ty);
            return (on > 1e-4 && tn > 1e-4) ? (ox * tx + oy * ty) / (on * tn) : 0.0;
        }

        static Vector3 SurfaceNormalLocal(double lonDeg, double latDeg)
        {
            double lon = lonDeg * Math.PI / 180.0, lat = latDeg * Math.PI / 180.0;
            double c = Math.Cos(lat);
            return new Vector3((float)(c * Math.Sin(lon)), (float)Math.Sin(lat), (float)(c * Math.Cos(lon))).normalized;
        }

        static Vector3 NorthTangentLocal(double lonDeg, double latDeg)
        {
            double lon = lonDeg * Math.PI / 180.0, lat = latDeg * Math.PI / 180.0;
            double s = Math.Sin(lon), c = Math.Cos(lon), sl = Math.Sin(lat), cl = Math.Cos(lat);
            return new Vector3((float)(-sl * s), (float)cl, (float)(-sl * c)).normalized;
        }

        static Vector3 ReflectZ(Vector3 v) => new Vector3(v.x, v.y, -v.z);
        static double Wrap01(double v) => v - Math.Floor(v);

        static void WriteReports(
            string outDir, string mode, int quality, int earthWidth,
            List<string> sharedResults, bool sharedPassed,
            List<PairedRunRecord> pairs, List<BaselineRunRecord> baselines)
        {
            var inv = CultureInfo.InvariantCulture;
            var md = new StringBuilder(8192);
            md.AppendLine("# ORBIS Climate-Cloud Visual QA Report");
            md.AppendLine();
            md.AppendLine($"- **Unity**: `{Application.unityVersion}` · `{SystemInfo.graphicsDeviceType}` · `{SystemInfo.graphicsDeviceName}`");
            md.AppendLine($"- **Mode**: `{mode}` · **Quality**: `q={quality}` · **Earth Grid**: `{earthWidth}x{earthWidth / 2}` · **Visual Clock**: `{ClimateCloudQaScenarios.DefaultVisualClock:F1}` (locked)");
            md.AppendLine($"- **Timestamp (UTC)**: `{DateTime.UtcNow:O}`");
            md.AppendLine();

            if (sharedResults.Count > 0)
            {
                md.AppendLine("## 1. Reused Shared GPU Regressions");
                foreach (var line in sharedResults) md.AppendLine("- " + line);
                md.AppendLine();
            }

            if (pairs.Count > 0)
            {
                md.AppendLine("## 2. Controlled Paired Views (Production `FlatSkyRenderer` + Live Camera)");
                md.AppendLine("| Scenario | Coverage | Col Opacity A -> B | Conv Top (m) A -> B | Screen Luma Delta (DN) | Dir. Coherence | r(dForc,dCol) | Verdict | Sheet |");
                md.AppendLine("| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :--- |");
                foreach (var r in pairs)
                {
                    md.AppendFormat(inv,
                        "| `{0}` | `{1}` | `{2:F3} -> {3:F3}` | `{4:F0} -> {5:F0}` | `{6:+0.00;-0.00}` | `{7:F2}` | `{8:F2}` | **{9}** | `{10}` |\n",
                        r.Spec.Id, r.Spec.CoverageLevel,
                        r.SkyA.ColumnLowOpacity, r.SkyB.ColumnLowOpacity,
                        r.SkyA.CloudWeightedTopMeters, r.SkyB.CloudWeightedTopMeters,
                        r.ScreenStats.SignedLumaDeltaDn,
                        r.ColumnDelta.DirectionalCoherence,
                        r.ForcingToColumnR,
                        r.Verdict.Passed ? "PASS" : "FAIL (" + r.Verdict.FailureCode + ")",
                        r.SheetPngRelPath);
                }
                md.AppendLine();
                foreach (var r in pairs)
                {
                    md.AppendLine($"### `{r.Spec.Id}` — {r.Spec.Title}");
                    md.AppendLine($"- **Physical Axis**: {r.Spec.PhysicalAxis}");
                    md.AppendLine($"- **E2E Coverage**: `{r.Spec.CoverageLevel}` — {r.Spec.CoverageNote}");
                    foreach (var c in r.Verdict.Checks) md.AppendLine($"- {c}");
                    md.AppendLine();
                }
            }

            if (baselines.Count > 0)
            {
                md.AppendLine("## 3. Full-Disk Visual Baseline Audit (`79e6fa2` Left Column in `NasaUpgrade3Col`)");
                md.AppendLine("| View | Group | Clear (79 -> Cand) | Thin Veil (79 -> Cand) | Top3 Span (79 -> Cand) | Speckles (79 -> Cand) | Sharpness (79 -> Cand) | Verdict | Sheet |");
                md.AppendLine("| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :--- |");
                foreach (var b in baselines)
                {
                    var m79 = b.Verdict.Baseline79;
                    var mc = b.Verdict.Candidate;
                    md.AppendFormat(inv,
                        "| `{0}` | {1} | `{2:F3} -> {3:F3}` | `{4:F3} -> {5:F3}` | `{6:F3} -> {7:F3}` | `{8} -> {9}` | `{10:F4} -> {11:F4}` | **{12}** | `{13}` |\n",
                        b.Spec.Id, b.Spec.HemisphereGroup,
                        m79.ClearSkyFraction, mc.ClearSkyFraction,
                        m79.ThinVeilFraction, mc.ThinVeilFraction,
                        m79.Top3MeanSpan, mc.Top3MeanSpan,
                        m79.SpeckleCount, mc.SpeckleCount,
                        m79.TransitionEdgeSharpness, mc.TransitionEdgeSharpness,
                        b.Verdict.Passed ? "PASS" : "FAIL",
                        b.SheetPngRelPath);
                }
                md.AppendLine();
                foreach (var b in baselines)
                {
                    md.AppendLine($"### `{b.Spec.Id}` — {b.Spec.Title}");
                    foreach (var c in b.Verdict.Checks) md.AppendLine($"- {c}");
                    md.AppendLine();
                }
            }

            File.WriteAllText(Path.Combine(outDir, "cloud-visual-qa-report.md"), md.ToString(), Encoding.UTF8);

            var js = new StringBuilder(8192);
            js.Append("{\n");
            js.AppendFormat(inv, "  \"unityVersion\": \"{0}\",\n", ClimateCloudQaMetrics.EscapeJson(Application.unityVersion));
            js.AppendFormat(inv, "  \"graphicsApi\": \"{0}\",\n", ClimateCloudQaMetrics.EscapeJson(SystemInfo.graphicsDeviceType.ToString()));
            js.AppendFormat(inv, "  \"gpu\": \"{0}\",\n", ClimateCloudQaMetrics.EscapeJson(SystemInfo.graphicsDeviceName));
            js.AppendFormat(inv, "  \"mode\": \"{0}\",\n", ClimateCloudQaMetrics.EscapeJson(mode));
            js.AppendFormat(inv, "  \"cloudQuality\": {0},\n", quality);
            js.AppendFormat(inv, "  \"earthWidth\": {0},\n", earthWidth);
            js.AppendFormat(inv, "  \"lockedVisualClock\": {0:F3},\n", ClimateCloudQaScenarios.DefaultVisualClock);
            js.AppendFormat(inv, "  \"sharedRegressionsPassed\": {0},\n", sharedPassed ? "true" : "false");
            js.Append("  \"pairs\": [\n");
            for (int i = 0; i < pairs.Count; i++)
            {
                var r = pairs[i];
                js.Append("    {\n");
                js.AppendFormat(inv, "      \"id\": \"{0}\", \"passed\": {1}, \"failureCode\": \"{2}\", \"coverage\": \"{3}\",\n",
                    ClimateCloudQaMetrics.EscapeJson(r.Spec.Id), r.Verdict.Passed ? "true" : "false",
                    ClimateCloudQaMetrics.EscapeJson(r.Verdict.FailureCode), r.Spec.CoverageLevel);
                js.AppendFormat(inv, "      \"columnOpacityA\": {0:F5}, \"columnOpacityB\": {1:F5}, \"columnDelta\": {2:F5}, \"columnCoherence\": {3:F4}, \"controlLeakage\": {4:F4},\n",
                    r.SkyA.ColumnLowOpacity, r.SkyB.ColumnLowOpacity, r.ColumnDelta.SignedDelta, r.ColumnDelta.DirectionalCoherence, r.ColumnDelta.ControlLeakageRatio);
                js.AppendFormat(inv, "      \"convectiveTopMetersA\": {0:F1}, \"convectiveTopMetersB\": {1:F1}, \"forcingToColumnR\": {2:F4},\n",
                    r.SkyA.CloudWeightedTopMeters, r.SkyB.CloudWeightedTopMeters, r.ForcingToColumnR);
                js.AppendFormat(inv, "      \"screenLumaDeltaDn\": {0:F3}, \"screenCoherence\": {1:F4}, \"sheetPng\": \"{2}\"\n",
                    r.ScreenStats.SignedLumaDeltaDn, r.ScreenStats.DirectionalCoherence, ClimateCloudQaMetrics.EscapeJson(r.SheetPngRelPath));
                js.Append(i + 1 < pairs.Count ? "    },\n" : "    }\n");
            }
            js.Append("  ],\n  \"baseline79e6fa2\": [\n");
            for (int i = 0; i < baselines.Count; i++)
            {
                var b = baselines[i];
                var m79 = b.Verdict.Baseline79;
                var mc = b.Verdict.Candidate;
                js.Append("    {\n");
                js.AppendFormat(inv, "      \"id\": \"{0}\", \"group\": \"{1}\", \"passed\": {2},\n",
                    ClimateCloudQaMetrics.EscapeJson(b.Spec.Id), ClimateCloudQaMetrics.EscapeJson(b.Spec.HemisphereGroup),
                    b.Verdict.Passed ? "true" : "false");
                js.AppendFormat(inv, "      \"clear79\": {0:F4}, \"clearCand\": {1:F4}, \"thin79\": {2:F4}, \"thinCand\": {3:F4},\n",
                    m79.ClearSkyFraction, mc.ClearSkyFraction, m79.ThinVeilFraction, mc.ThinVeilFraction);
                js.AppendFormat(inv, "      \"top3Span79\": {0:F4}, \"top3SpanCand\": {1:F4}, \"speckles79\": {2}, \"specklesCand\": {3},\n",
                    m79.Top3MeanSpan, mc.Top3MeanSpan, m79.SpeckleCount, mc.SpeckleCount);
                js.AppendFormat(inv, "      \"sharpness79\": {0:F4}, \"sharpnessCand\": {1:F4}, \"stormProminenceCand\": {2:F4}, \"sheetPng\": \"{3}\"\n",
                    m79.TransitionEdgeSharpness, mc.TransitionEdgeSharpness, mc.StormProminence, ClimateCloudQaMetrics.EscapeJson(b.SheetPngRelPath));
                js.Append(i + 1 < baselines.Count ? "    },\n" : "    }\n");
            }
            js.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(outDir, "cloud-visual-qa-report.json"), js.ToString(), Encoding.UTF8);
        }

        static void Finish(string outDir, string status, int exitCode)
        {
            EditorApplication.update -= Step;
            SessionState.SetBool(KeyActive, false);
            File.WriteAllText(Path.Combine(outDir, "status.txt"), status + Environment.NewLine, Encoding.UTF8);
            Debug.Log("[CloudVisualQA] " + status + " (exit=" + exitCode + ")");
            if (Application.isBatchMode)
            {
                SessionState.SetInt(KeyExit, exitCode);
                EditorApplication.update -= ExitAfterPlay;
                EditorApplication.update += ExitAfterPlay;
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                else EditorApplication.Exit(exitCode);
            }
            else if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
        }

        static void ExitAfterPlay()
        {
            int code = SessionState.GetInt(KeyExit, -1);
            if (code < 0 || EditorApplication.isPlaying) return;
            SessionState.SetInt(KeyExit, -1);
            EditorApplication.update -= ExitAfterPlay;
            EditorApplication.Exit(code);
        }
    }
}
