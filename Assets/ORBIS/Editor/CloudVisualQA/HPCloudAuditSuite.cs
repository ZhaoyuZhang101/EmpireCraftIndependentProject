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
    /// Targeted D3D11 PlayMode audit for HPVolumeCloud:
    ///   1) Proves whether any visible 2D sky cloud path exists across q=1/q=2/q=0, top-down, upward, inside-cloud/upper-rim,
    ///      local unfold transition, 2D atlas, far-distance perf fallback, and startup/resize unready-cache frames
    ///      using camera-motion parallax, MRT Radiance/Transmittance + Density capture, and phi_fwd (_CloudMS) A/B switch.
    ///   2) Quantifies near-ground curved-arc banding, camera-motion temporal flicker (8-step pan), and GPU ms across
    ///      top-down, upward cumulus, side cumulus, and typhoon eyewall (before &amp; after minimal fix).
    ///   3) Checks pause -> fast date advance and 4-season 6-ROI CPU vs GPU consistency.
    /// </summary>
    [InitializeOnLoad]
    public static class HPCloudAuditSuite
    {
        const string ScenePath = "Assets/ORBIS/Scenes/Orbis2DWorld.unity";
        const string KeyActive = "Orbis.HPCloudAudit.Active";
        const string KeyOut = "Orbis.HPCloudAudit.Out";
        const string KeyExit = "Orbis.HPCloudAudit.Exit";
        const string KeyTag = "Orbis.HPCloudAudit.Tag";
        const string KeyMode = "Orbis.HPCloudAudit.Mode";
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        const float CameraFovDeg = 42f;

        // Captures exact ARGBFloat MRT0 (Radiance) and MRT1 (Transmittance) from CloudMaterial Pass 0 during OnRenderImage.
        sealed class MrtProbeHook : MonoBehaviour
        {
            public Material CloudMaterial;
            public bool Arm;
            public int Width = 640, Height = 360;
            public Color[] Radiance, Transmittance;
            public bool Captured;
            readonly RenderBuffer[] buffers = new RenderBuffer[2];

            void OnRenderImage(RenderTexture src, RenderTexture dst)
            {
                Graphics.Blit(src, dst);
                if (!Arm || !CloudMaterial) return;
                Arm = false;
                var prevActive = RenderTexture.active;
                bool prevSRGB = GL.sRGBWrite;
                RenderTexture radRt = null, trRt = null;
                Texture2D readTex = null;
                try
                {
                    radRt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    trRt = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    buffers[0] = radRt.colorBuffer;
                    buffers[1] = trRt.colorBuffer;
                    GL.sRGBWrite = false;
                    Graphics.SetRenderTarget(buffers, radRt.depthBuffer);
                    CloudMaterial.SetTexture("_MainTex", src);
                    CloudMaterial.SetVector("_VolumeTexelSize", new Vector4(1f / Width, 1f / Height, Width, Height));
                    GL.PushMatrix();
                    try
                    {
                        GL.LoadOrtho();
                        if (CloudMaterial.SetPass(0))
                        {
                            GL.Begin(GL.QUADS);
                            GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0.1f);
                            GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0.1f);
                            GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0.1f);
                            GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0.1f);
                            GL.End();
                        }
                    }
                    finally { GL.PopMatrix(); }

                    readTex = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
                    RenderTexture.active = radRt;
                    readTex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    readTex.Apply(false, false);
                    Radiance = readTex.GetPixels();
                    RenderTexture.active = trRt;
                    readTex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    readTex.Apply(false, false);
                    Transmittance = readTex.GetPixels();
                    Captured = true;
                }
                finally
                {
                    RenderTexture.active = prevActive;
                    GL.sRGBWrite = prevSRGB;
                    if (readTex) UnityEngine.Object.DestroyImmediate(readTex);
                    if (radRt) RenderTexture.ReleaseTemporary(radRt);
                    if (trRt) RenderTexture.ReleaseTemporary(trRt);
                }
            }
        }

        static HPCloudAuditSuite()
        {
            if (SessionState.GetBool(KeyActive, false))
                EditorApplication.update += StepPlayMode;
            if (SessionState.GetInt(KeyExit, -1) >= 0)
                EditorApplication.update += ExitAfterPlay;
        }

        [MenuItem("Tools/ORBIS/Cloud Visual QA/Run HPVolumeCloud Audit Suite (D3D11)")]
        public static void RunMenu() => StartBatch("ValidationOutput/CloudAudit_20261125", "before", "full");

        [MenuItem("Tools/ORBIS/Cloud Visual QA/Run Cloud Optics Separation Audit (D3D11)")]
        public static void RunOpticsMenu() => StartBatch("ValidationOutput/CloudOptics_20261126", "after", "optics");

        public static void RunBatch()
        {
            string outDir = Arg("-orbisCloudAuditOut") ?? "ValidationOutput/CloudAudit_20261125";
            string tag = Arg("-orbisCloudAuditTag") ?? "before";
            string mode = Arg("-orbisCloudAuditMode") ?? "full";
            StartBatch(outDir, tag, mode);
        }

        public static void RunOpticsBatch()
        {
            string outDir = Arg("-orbisCloudAuditOut") ?? "ValidationOutput/CloudOptics_20261126";
            string tag = Arg("-orbisCloudAuditTag") ?? "after";
            StartBatch(outDir, tag, "optics");
        }

        public static void RunSharedRegressionsBatch()
        {
            string outDir = Path.GetFullPath("ValidationOutput/CloudAudit_20261125");
            Directory.CreateDirectory(outDir);
            var results = new List<string>();
            bool allOk = true;
            var suites = new (string name, Action run)[]
            {
                ("ClimateCloudRegression.Run", ClimateCloudRegression.Run),
                ("HPCloudHeightRegression.Run", HPCloudHeightRegression.Run),
                ("WaterCloudRegression.Run", WaterCloudRegression.Run),
                ("LightningRegression.Run", LightningRegression.Run),
                ("StormWindEffectsRegression.Run", StormWindEffectsRegression.Run),
            };
            bool savedAsync = ShaderUtil.allowAsyncCompilation;
            bool savedEditorAsync = EditorSettings.asyncShaderCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            EditorSettings.asyncShaderCompilation = false;
            try
            {
                foreach (var s in suites)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        s.run();
                        string line = $"PASS {s.name} ({sw.Elapsed.TotalSeconds:F2}s)";
                        results.Add(line);
                        Debug.Log("[HPCloudAudit] " + line);
                    }
                    catch (Exception ex)
                    {
                        allOk = false;
                        string line = $"FAIL {s.name} ({sw.Elapsed.TotalSeconds:F2}s): {ex.Message}";
                        results.Add(line);
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                EditorSettings.asyncShaderCompilation = savedEditorAsync;
                ShaderUtil.allowAsyncCompilation = savedAsync;
                File.WriteAllLines(Path.Combine(outDir, "shared_regressions.txt"), results, new UTF8Encoding(false));
            }
            if (!allOk) throw new InvalidOperationException(string.Join("\n", results));
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static void StartBatch(string outDir, string tag, string mode = "full")
        {
            string fullOut = Path.IsPathRooted(outDir)
                ? Path.GetFullPath(outDir)
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", outDir));
            Directory.CreateDirectory(fullOut);
            SessionState.SetBool(KeyActive, true);
            SessionState.SetString(KeyOut, fullOut);
            SessionState.SetString(KeyTag, tag);
            SessionState.SetString(KeyMode, mode ?? "full");
            SessionState.SetInt(KeyExit, -1);

            if (!EditorApplication.isPlaying)
                EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= StepPlayMode;
            EditorApplication.update += StepPlayMode;
            if (!EditorApplication.isPlaying)
                EditorApplication.EnterPlaymode();
        }

        static void ExitAfterPlay()
        {
            int code = SessionState.GetInt(KeyExit, -1);
            if (code < 0) { EditorApplication.update -= ExitAfterPlay; return; }
            if (EditorApplication.isPlaying) return;
            SessionState.SetInt(KeyExit, -1);
            EditorApplication.update -= ExitAfterPlay;
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void StepPlayMode()
        {
            if (!SessionState.GetBool(KeyActive, false))
            {
                EditorApplication.update -= StepPlayMode;
                return;
            }
            if (!EditorApplication.isPlaying) return;
            var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if (!app || app.Busy || app.World == null) return;

            SessionState.SetBool(KeyActive, false);
            EditorApplication.update -= StepPlayMode;

            string outDir = SessionState.GetString(KeyOut, Path.GetFullPath("ValidationOutput/CloudAudit_20261125"));
            string tag = SessionState.GetString(KeyTag, "before");
            string mode = SessionState.GetString(KeyMode, "full");
            int exitCode = 0;
            try
            {
                if (string.Equals(mode, "optics", StringComparison.OrdinalIgnoreCase))
                    ExecuteOpticsSeparationAudit(app, Path.Combine(outDir, tag));
                else
                    ExecuteAudit(app, Path.Combine(outDir, tag));
            }
            catch (Exception ex)
            {
                exitCode = 1;
                Debug.LogException(ex);
                File.WriteAllText(Path.Combine(outDir, $"error_{tag}.txt"), ex.ToString(), Encoding.UTF8);
            }
            finally
            {
                SessionState.SetInt(KeyExit, exitCode);
                EditorApplication.update -= ExitAfterPlay;
                EditorApplication.update += ExitAfterPlay;
                EditorApplication.ExitPlaymode();
            }
        }

        static Vector3 SurfaceNormalLocal(double lonDeg, double latDeg)
        {
            double lon = lonDeg * Math.PI / 180.0;
            double lat = latDeg * Math.PI / 180.0;
            return new Vector3(
                (float)(Math.Sin(lon) * Math.Cos(lat)),
                (float)Math.Sin(lat),
                (float)(Math.Cos(lon) * Math.Cos(lat))).normalized;
        }

        static Vector3 NorthTangentLocal(double lonDeg, double latDeg)
        {
            double lon = lonDeg * Math.PI / 180.0;
            double lat = latDeg * Math.PI / 180.0;
            return new Vector3(
                (float)(-Math.Sin(lon) * Math.Sin(lat)),
                (float)Math.Cos(lat),
                (float)(-Math.Cos(lon) * Math.Sin(lat))).normalized;
        }

        static Vector3 ReflectZ(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        static double FindSunDayForElevation(FlatSkyRenderer sky, Vector3 localNormal, double baseDay, float targetElevDeg, bool afternoon)
        {
            double bestDay = baseDay, bestErr = 1e9;
            float targetSin = Mathf.Sin(targetElevDeg * Mathf.Deg2Rad);
            for (int k = 0; k < 360; k++)
            {
                double cand = Math.Floor(baseDay) + k / 360.0;
                Vector3 s0 = sky.Sun(180f, cand, true).normalized;
                Vector3 s1 = sky.Sun(180f, cand + 0.004, true).normalized;
                float sin0 = Vector3.Dot(localNormal, s0);
                float dSin = Vector3.Dot(localNormal, s1) - sin0;
                if (afternoon ? (dSin < 0f) : (dSin > 0f))
                {
                    double err = Math.Abs(sin0 - targetSin);
                    if (err < bestErr) { bestErr = err; bestDay = cand; }
                }
            }
            return bestDay;
        }

        static WorldData LoadEarth512()
        {
            var dem = Resources.Load<TextAsset>("ORBIS/FlatWorld/Earth512");
            var water = Resources.Load<TextAsset>("ORBIS/FlatWorld/EarthWater512");
            var rivers = Resources.Load<TextAsset>("ORBIS/FlatWorld/EarthRivers");
            if (!dem || !water || !rivers) throw new InvalidOperationException("Missing Earth512 resources.");
            return FlatGenerator.Generate(512, 256, FlatStormScenarios.CanonicalEarthSeed, TerrainPreset.Earth,
                CancellationToken.None, dem.bytes, water.bytes, rivers.bytes);
        }

        static WorldData CreateCat5TyphoonWorld(WorldData baseEarth, double day, double lonDeg, double latDeg, double phaseRad)
        {
            var w = ClimateCloudQaScenarios.DeepCloneWorld(baseEarth);
            w.Day = day;
            double u = FlatScrollMath.Wrap(lonDeg / 360.0 + 0.5, 1.0);
            double v = Math.Max(0.02, Math.Min(0.98, latDeg / 180.0 + 0.5));
            var tr = new FlatCycloneTrack
            {
                Tracked = true,
                U = u,
                V = v,
                WindSpeed = 54.0,
                PressureDeficit = 88.0,
                Phase = phaseRad,
                AgeDays = 5.0,
                OceanCooling = 1.2
            };
            w.Cyclones = new FlatCycloneWeather
            {
                StepIndex = FlatCycloneWeather.StepAt(day),
                North = tr,
                PreviousNorth = tr
            };
            return w;
        }

        static void InstallWorldSync(FlatWorldApp app, WorldData world)
        {
            var appType = typeof(FlatWorldApp);
            appType.GetField("paused", Private).SetValue(app, true);
            appType.GetField("screenshotMode", Private).SetValue(app, true);
            appType.GetMethod("Install", Private).Invoke(app, new object[] { world });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!ReferenceEquals(app.World, world))
            {
                var pending = appType.GetField("pending", Private).GetValue(app) as System.Threading.Tasks.Task<WorldData>;
                if (pending == null) throw new InvalidOperationException("Install did not queue or finish.");
                if (sw.Elapsed.TotalSeconds > 60.0) throw new TimeoutException("Install timed out.");
                pending.Wait(TimeSpan.FromMilliseconds(50));
                appType.GetMethod("Update", Private).Invoke(app, null);
            }
        }

        static void PublishSkyExact(FlatMapRenderer map, WorldData world, double visualClock, int skyWidth, double day, bool animate = false)
        {
            var sky = map.Sky;
            map.SetLayer(MapLayer.Natural);
            var clockProp = typeof(FlatSkyRenderer).GetProperty("CaptureWeatherTimeOverride",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            clockProp?.SetValue(sky, visualClock);
            var pendingStripField = typeof(FlatSkyRenderer).GetField("pendingStrip", Private);
            var schedulerField = typeof(FlatSkyRenderer).GetField("forcingScheduler", Private);
            var pendingBakeField = typeof(FlatSkyRenderer).GetField("pendingForcingBake", Private);
            var scheduler = schedulerField?.GetValue(sky) as FlatSkyForcingScheduler;
            long expectedStep = (long)Math.Floor(Math.Min(world.Day, day) * 4.0);
            double expectedSyn = FlatSkyCalendar.CanonicalSynopticDay(day);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 25.0)
            {
                map.Flush();
                clockProp?.SetValue(sky, visualClock);
                sky.Tick(animate ? 0.1f : 0f, true, animate, false, skyWidth, day);
                int strip = pendingStripField != null ? (int)pendingStripField.GetValue(sky) : -1;
                bool pendingBake = pendingBakeField != null && (bool)pendingBakeField.GetValue(sky);
                bool schedReady = scheduler == null ||
                    (scheduler.HasPublished && scheduler.IsSegmentCurrent(world, day) &&
                     !scheduler.HasInFlightWork && Math.Abs(scheduler.PublishedSynopticDay - expectedSyn) < 1e-6);
                if (sky.Ready && !map.PreparationPending && strip < 0 && !pendingBake && schedReady &&
                    sky.RegionalClimateStep.HasValue && sky.RegionalClimateStep.Value == expectedStep &&
                    Math.Abs(sky.RenderClock - visualClock) < 1e-3)
                    break;
                Thread.Sleep(5);
            }
            if (!sky.Ready) throw new InvalidOperationException($"Sky failed to reach ready state for day={day:F3}");
        }

        static Color[] ReadRtPixels(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false, false);
            var px = tex.GetPixels();
            RenderTexture.active = prev;
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        static void SaveFloatFieldPng(Color[] data, int w, int h, string path, Func<Color, Color> mapColor)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var outPx = new Color[w * h];
            for (int i = 0; i < outPx.Length; i++) outPx[i] = mapColor(data[i]);
            tex.SetPixels(outPx);
            tex.Apply(false, false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static double MeasureGpuMs(Camera cam, RenderTexture rt, Texture2D read1, int warmup = 2, int samples = 9)
        {
            var times = new double[samples];
            var prevActive = RenderTexture.active;
            for (int k = 0; k < warmup + samples; k++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                cam.Render();
                RenderTexture.active = rt;
                read1.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                read1.Apply(false, false);
                sw.Stop();
                if (k >= warmup) times[k - warmup] = sw.Elapsed.TotalMilliseconds;
            }
            RenderTexture.active = prevActive;
            Array.Sort(times);
            return times[samples / 2];
        }

        static double MeanRgbDiff(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return -1;
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b)) / 3.0;
            return sum / a.Length;
        }

        static double ComputeHighFreqLaplacian(Color32[] px, int w, int h, int x0, int y0, int x1, int y1)
        {
            double sum = 0;
            int n = 0;
            for (int y = Math.Max(1, y0); y < Math.Min(h - 1, y1); y++)
            for (int x = Math.Max(1, x0); x < Math.Min(w - 1, x1); x++)
            {
                double L(int xx, int yy)
                {
                    var c = px[yy * w + xx];
                    return 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                }
                double c0 = L(x, y);
                sum += Math.Abs(4.0 * c0 - L(x - 1, y) - L(x + 1, y) - L(x, y - 1) - L(x, y + 1));
                n++;
            }
            return n > 0 ? sum / n : 0;
        }

        static (double meanHfDiff, double p95HfDiff, double maxHfDiff, double meanLumaStep) ComputeMotionFlicker(
            List<Color32[]> frames, int w, int h, int x0, int y0, int x1, int y1)
        {
            // Measures high-frequency temporal flicker during smooth camera motion:
            // compares frame-to-frame delta of local spatial high-pass (pixel - 3x3 mean) so smooth global pan cancels out,
            // isolating stepping/popping/shimmer flicker strictly!
            var diffs = new List<double>();
            double sumStep = 0;
            int stepN = 0;
            for (int f = 1; f < frames.Count; f++)
            {
                var a = frames[f - 1];
                var b = frames[f];
                for (int y = Math.Max(1, y0); y < Math.Min(h - 1, y1); y += 2)
                for (int x = Math.Max(1, x0); x < Math.Min(w - 1, x1); x += 2)
                {
                    double L(Color32[] arr, int xx, int yy)
                    {
                        var c = arr[yy * w + xx];
                        return 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    }
                    double Hp(Color32[] arr, int xx, int yy)
                    {
                        double c0 = L(arr, xx, yy);
                        double avg = (L(arr, xx - 1, yy) + L(arr, xx + 1, yy) + L(arr, xx, yy - 1) + L(arr, xx, yy + 1)) * 0.25;
                        return c0 - avg;
                    }
                    double d = Math.Abs(Hp(b, x, y) - Hp(a, x, y));
                    diffs.Add(d);
                    sumStep += Math.Abs(L(b, x, y) - L(a, x, y));
                    stepN++;
                }
            }
            if (diffs.Count == 0) return (0, 0, 0, 0);
            diffs.Sort();
            double sum = 0;
            foreach (var v in diffs) sum += v;
            return (sum / diffs.Count, diffs[(int)(diffs.Count * 0.95)], diffs[diffs.Count - 1], sumStep / Math.Max(1, stepN));
        }

        static double ComputeCrossCorrelationShiftX(Color32[] imgA, Color32[] imgB, int w, int h, int y0, int y1, int x0, int x1, int maxLag)
        {
            // Normalized cross-correlation over horizontal lags to measure parallax shift in pixels
            double bestScore = -1e9;
            int bestLag = 0;
            for (int lag = -maxLag; lag <= maxLag; lag++)
            {
                double num = 0, denA = 0, denB = 0;
                for (int y = y0; y < y1; y += 2)
                for (int x = Math.Max(x0, x0 - lag); x < Math.Min(x1, x1 - lag); x += 2)
                {
                    var ca = imgA[y * w + x];
                    var cb = imgB[y * w + x + lag];
                    double la = 0.2126 * ca.r + 0.7152 * ca.g + 0.0722 * ca.b - 128.0;
                    double lb = 0.2126 * cb.r + 0.7152 * cb.g + 0.0722 * cb.b - 128.0;
                    num += la * lb;
                    denA += la * la;
                    denB += lb * lb;
                }
                double score = num / Math.Max(1e-6, Math.Sqrt(denA * denB));
                if (score > bestScore) { bestScore = score; bestLag = lag; }
            }
            return bestLag;
        }

        static void ExecuteAudit(FlatWorldApp app, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(Path.Combine(outDir, "showcase"));
            Directory.CreateDirectory(Path.Combine(outDir, "views"));
            Directory.CreateDirectory(Path.Combine(outDir, "mrt_phifwd"));
            Directory.CreateDirectory(Path.Combine(outDir, "parallax"));
            Directory.CreateDirectory(Path.Combine(outDir, "states"));

            var inv = CultureInfo.InvariantCulture;
            var log = new List<string>();
            void Log(string s) { log.Add(s); Debug.Log("[HPCloudAudit] " + s); }

            bool savedAsync = ShaderUtil.allowAsyncCompilation;
            bool savedEditorAsync = EditorSettings.asyncShaderCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            EditorSettings.asyncShaderCompilation = false;

            var baseEarth = LoadEarth512();
            var cuWorld = ClimateCloudQaScenarios.DeepCloneWorld(baseEarth);
            cuWorld.Day = 195.067;
            cuWorld.Cyclones = FlatCycloneWeather.SpinUp(cuWorld);
            InstallWorldSync(app, cuWorld);

            var appType = typeof(FlatWorldApp);
            var camera = appType.GetField("viewCamera", Private).GetValue(app) as Camera;
            var map = appType.GetField("map", Private).GetValue(app) as FlatMapRenderer;
            var globe = appType.GetField("globe", Private).GetValue(app) as FlatGlobePresenter;
            var nav = app.Navigation;
            var lens = camera.GetComponent<OrbisCelestialLens>();
            var atmo = Array.Find(camera.GetComponents<OrbisAtmosphereRenderer>(), a => a.enabled && a.view == globe.View);
            var mrtHook = camera.gameObject.AddComponent<MrtProbeHook>();

            var oldTarget = camera.targetTexture;
            var oldRect = camera.pixelRect;
            var oldActive = RenderTexture.active;
            const int W = 1280, H = 720;
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var readTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var read1 = new Texture2D(1, 1, TextureFormat.RGB24, false);

            Color32[] CapturePng(string relPath, bool captureMrt = false)
            {
                if (captureMrt)
                {
                    mrtHook.CloudMaterial = globe.View.CloudMaterial;
                    mrtHook.Captured = false;
                    mrtHook.Arm = true;
                }
                camera.targetTexture = rt;
                camera.pixelRect = new Rect(0, 0, W, H);
                camera.Render();
                RenderTexture.active = rt;
                readTex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                readTex.Apply(false, false);
                var px = readTex.GetPixels32();
                if (!string.IsNullOrEmpty(relPath))
                    File.WriteAllBytes(Path.Combine(outDir, relPath), readTex.EncodeToPNG());
                return px;
            }

            void SetAltitudePose(WorldData w, double lonDeg, double latDeg, float altM, Vector3 lookW, double spinDay, double sunDay, int q, bool godRays, bool msOn = true)
            {
                var loc = PlanetCoordinateSystem.FromMapUV((lonDeg + 180.0) / 360.0, (latDeg + 90.0) / 180.0);
                nav.Home(spinDay);
                nav.Tick(3f, spinDay);
                nav.Recenter(loc, spinDay);
                nav.SetViewport(new Rect(0, 0, W, H));
                nav.Tick(0f, spinDay);

                Vector3 localDir = SurfaceNormalLocal(lonDeg, latDeg);
                Quaternion spinQ = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                Vector3 worldNormal = (spinQ * ReflectZ(localDir)).normalized;
                float altR = altM * OrbisWeatherField.HeightScale;
                camera.targetTexture = rt;
                camera.orthographic = false;
                camera.fieldOfView = CameraFovDeg;
                camera.nearClipPlane = Mathf.Clamp(altR * 0.25f, 0.00004f, 0.002f);
                camera.farClipPlane = 80f;
                camera.ResetProjectionMatrix();
                camera.transform.SetPositionAndRotation(worldNormal * (1f + altR), Quaternion.LookRotation(lookW.normalized, worldNormal));

                var s = globe.Snapshot.settings;
                s.exposure = ClimateCloudQaScenarios.DefaultExposure;
                s.cloudQuality = q;
                s.cloudCover = 1f;
                s.cloudDensity = 1f;
                s.clouds = true;
                s.cloudShadows = true;
                s.godRays = godRays;
                s.atmosphere = true;
                s.night = true;
                s.multipleScattering = msOn;
                if (lens) lens.strength = 0f;
                atmo.fullResolutionReference = false;
                atmo.referenceCloudIntegration = false;
                atmo.cloudDebug = 0;

                Vector3 sunLocal = map.Sky.Sun(180f, sunDay, true);
                map.Configure(false, nav.PixelsPerTile, 0f);
                map.SetVegetationDay(w.Day);
                map.Sky.Bind(map.Material, sunLocal, true, true, true, true, false, false, true, 0f);
                map.Material.SetFloat("_WaterClock", 0f);
                nav.SyncMaterials();
                globe.TickScroll(0f, ClimateCloudQaScenarios.DefaultVisualClock, w.Day, nav.DisplaySpin, 0f, true, true, true, true, sunLocal, true);
            }

            void SetOrbitPose(WorldData w, double lonDeg, double latDeg, float dist, double day, int q, float tiltDeg = 0f, bool msOn = true)
            {
                var loc = PlanetCoordinateSystem.FromMapUV((lonDeg + 180.0) / 360.0, (latDeg + 90.0) / 180.0);
                nav.Home(day);
                nav.Tick(3f, day);
                nav.Recenter(loc, day);
                nav.SetViewport(new Rect(0, 0, W, H));
                nav.Tick(0f, day);

                Vector3 localDir = SurfaceNormalLocal(lonDeg, latDeg);
                Vector3 localNorth = NorthTangentLocal(lonDeg, latDeg);
                Quaternion spinQ = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                Vector3 worldNormal = (spinQ * ReflectZ(localDir)).normalized;
                Vector3 worldNorth = (spinQ * ReflectZ(localNorth)).normalized;
                Vector3 lookDir = (-worldNormal * Mathf.Cos(tiltDeg * Mathf.Deg2Rad) + worldNorth * Mathf.Sin(tiltDeg * Mathf.Deg2Rad)).normalized;

                camera.targetTexture = rt;
                camera.orthographic = false;
                camera.fieldOfView = CameraFovDeg;
                camera.nearClipPlane = 0.001f;
                camera.farClipPlane = 80f;
                camera.ResetProjectionMatrix();
                camera.transform.SetPositionAndRotation(worldNormal * dist, Quaternion.LookRotation(lookDir, worldNorth));

                var s = globe.Snapshot.settings;
                s.exposure = ClimateCloudQaScenarios.DefaultExposure;
                s.cloudQuality = q;
                s.cloudCover = 1f;
                s.cloudDensity = 1f;
                s.clouds = true;
                s.cloudShadows = true;
                s.godRays = true;
                s.atmosphere = true;
                s.night = true;
                s.multipleScattering = msOn;
                if (lens) lens.strength = 0f;
                atmo.fullResolutionReference = false;
                atmo.referenceCloudIntegration = false;
                atmo.cloudDebug = 0;

                Vector3 sunLocal = map.Sky.Sun(180f, day, true);
                map.Configure(false, nav.PixelsPerTile, 0f);
                map.SetVegetationDay(w.Day);
                map.Sky.Bind(map.Material, sunLocal, true, true, true, true, false, false, true, 0f);
                map.Material.SetFloat("_WaterClock", 0f);
                nav.SyncMaterials();
                globe.TickScroll(0f, ClimateCloudQaScenarios.DefaultVisualClock, w.Day, nav.DisplaySpin, 0f, true, true, true, true, sunLocal, true);
            }

            void LogBindings(string viewTag, double cloudMs)
            {
                var cm = globe.View.CloudMaterial;
                var sm = map.Material;
                var cloudObj = typeof(OrbisPlanetView).GetField("cloudObject", Private)?.GetValue(globe.View) as GameObject;
                Log(string.Format(inv,
                    "VIEW | {0,-24} | q={1} morph={2:F2} atlas={3} flat={4} | scs={5:F0} lw={6:F0} volW={7:F2} viewBlend={8:F2} localMap={9:F0} | mapCloudOp={10:F2} cloudObj={11} | texOk={12} colOk={13} | cloudMs={14:F2}",
                    viewTag, globe.Snapshot.settings.cloudQuality, nav.Morph, nav.IsAtlas ? 1 : 0, nav.IsFlat ? 1 : 0,
                    cm.GetFloat("_ScrollCloudSource"), cm.GetFloat("_LayeredWeather"),
                    atmo.scrollVolumeWeight, cm.GetFloat("_CloudViewBlend"), cm.GetFloat("_LocalCloudMap"),
                    sm.GetFloat("_CloudOpacity"), cloudObj != null && cloudObj.activeSelf ? 1 : 0,
                    cm.GetTexture("_ScrollWeatherTex") == map.Sky.Texture ? 1 : 0,
                    cm.GetTexture("_ScrollCloudColumn") == map.Sky.ColumnTexture ? 1 : 0,
                    cloudMs));
            }

            double BenchCloudMs()
            {
                atmo.enabled = false;
                double noVol = MeasureGpuMs(camera, rt, read1, 2, 7);
                atmo.enabled = true;
                double vol = MeasureGpuMs(camera, rt, read1, 2, 7);
                return Math.Max(0.0, vol - noVol);
            }

            try
            {
                // ============================================================
                // 1. UNREADY CACHE CHECK (Sky.Ready == false) ON GLOBE & ATLAS
                // ============================================================
                Log("=== 1. STARTUP BEFORE CACHE READY (Sky.Ready == false) ===");
                {
                    SetOrbitPose(cuWorld, 124.0, 25.0, 2.2f, 195.067, 1);
                    var cm = globe.View.CloudMaterial;
                    float scsG = cm.GetFloat("_ScrollCloudSource"), lwG = cm.GetFloat("_LayeredWeather"), clG = cm.GetFloat("_Clouds");
                    var pxGOn = CapturePng("states/unready_globe_on.png");
                    globe.Snapshot.settings.clouds = false; cm.SetFloat("_Clouds", 0f);
                    var pxGOff = CapturePng("states/unready_globe_off.png");
                    Log(string.Format(inv, "UNREADY_GLOBE | Sky.Ready={0} | scs={1:F0} lw={2:F0} cl={3:F0} | onVsOffDN={4:F3}",
                        map.Sky.Ready, scsG, lwG, clG, MeanRgbDiff(pxGOn, pxGOff)));

                    nav.OpenAtlas(195.067); nav.SetViewport(new Rect(0, 0, W, H)); nav.Tick(0f, 195.067);
                    Vector3 sunL = map.Sky.Sun(180f, 195.067, true);
                    map.Sky.Bind(map.Material, sunL, true, true, true, true, false, false, true, 0f);
                    nav.SyncMaterials();
                    globe.TickScroll(0f, ClimateCloudQaScenarios.DefaultVisualClock, cuWorld.Day, nav.DisplaySpin, nav.Morph, true, true, true, true, sunL, true);
                    float scsBefore = cm.GetFloat("_ScrollCloudSource");
                    var pxAOn = CapturePng("states/unready_atlas_on.png");
                    float scsAfter = cm.GetFloat("_ScrollCloudSource");
                    globe.Snapshot.settings.clouds = false; cm.SetFloat("_Clouds", 0f);
                    var pxAOff = CapturePng("states/unready_atlas_off.png");
                    Log(string.Format(inv, "UNREADY_ATLAS | Sky.Ready={0} | scsBefore={1:F0} scsAfterRender={2:F0} | onVsOffDN={3:F3}",
                        map.Sky.Ready, scsBefore, scsAfter, MeanRgbDiff(pxAOn, pxAOff)));
                    nav.GoGlobe(195.067); nav.Tick(3f, 195.067);
                }

                // Publish exact steady Day 195.067 sky at 1024
                PublishSkyExact(map, cuWorld, ClimateCloudQaScenarios.DefaultVisualClock, 1024, cuWorld.Day + 0.25);

                // ============================================================
                // 2. 2D-SKY VISIBILITY MATRIX + MRT + PHI_FWD ON/OFF (q=1, q=2, q=0)
                // ============================================================
                Log("=== 2. VIEW MATRIX, MRT DENSITY/TRANSMITTANCE & PHI_FWD SWITCH ===");
                const double upLon = 130.8, upLat = 19.4;
                Vector3 upNormLocal = SurfaceNormalLocal(upLon, upLat);
                double upSunDay = FindSunDayForElevation(map.Sky, upNormLocal, 195.0, 36f, afternoon: true);
                nav.Home(upSunDay); nav.Tick(3f, upSunDay);
                Quaternion upSpin = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                Vector3 upNormW = (upSpin * ReflectZ(upNormLocal)).normalized;
                Vector3 upSunW = (upSpin * ReflectZ(map.Sky.Sun(180f, upSunDay, true))).normalized;
                Vector3 upToSunH = Vector3.ProjectOnPlane(upSunW, upNormW).normalized;
                Vector3 upRightH = Vector3.Cross(upNormW, upToSunH).normalized;
                Vector3 upHoriz = (upToSunH * Mathf.Cos(62f * Mathf.Deg2Rad) + upRightH * Mathf.Sin(62f * Mathf.Deg2Rad)).normalized;
                Vector3 upLook = (upHoriz * Mathf.Cos(18f * Mathf.Deg2Rad) + upNormW * Mathf.Sin(18f * Mathf.Deg2Rad)).normalized;

                const double cuLon = 131.8, cuLat = 19.0;
                Vector3 cuNormLocal = SurfaceNormalLocal(cuLon, cuLat);
                Vector3 cuTargetLocal = SurfaceNormalLocal(127.5, 19.8);
                double sideSunDay = FindSunDayForElevation(map.Sky, cuNormLocal, 195.0, 40f, afternoon: true);
                nav.Home(sideSunDay); nav.Tick(3f, sideSunDay);
                Quaternion sideSpin = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                Vector3 sideNormW = (sideSpin * ReflectZ(cuNormLocal)).normalized;
                Vector3 sideTargetW = (sideSpin * ReflectZ(cuTargetLocal)).normalized;
                Vector3 sideHoriz = Vector3.ProjectOnPlane(sideTargetW - sideNormW, sideNormW).normalized;
                Vector3 sideLook = (sideHoriz * Mathf.Cos(16f * Mathf.Deg2Rad) - sideNormW * Mathf.Sin(16f * Mathf.Deg2Rad)).normalized;

                // 2A. Top-down (俯视) q=1, q=2, plus MRT + Density + phi_fwd A/B
                SetOrbitPose(cuWorld, 124.0, 24.5, 1.55f, 195.067, 1, tiltDeg: 0f, msOn: true);
                var pxTopQ1On = CapturePng("showcase/01_topdown_q1.png", captureMrt: true);
                var mrtTopRadOn = mrtHook.Radiance; var mrtTopTrOn = mrtHook.Transmittance;
                int mw = mrtHook.Width, mh = mrtHook.Height;
                double topQ1Ms = BenchCloudMs();
                LogBindings("topdown_q1", topQ1Ms);

                if (mrtHook.Captured)
                {
                    SaveFloatFieldPng(mrtTopRadOn, mw, mh, Path.Combine(outDir, "mrt_phifwd/topdown_mrt_radiance.png"),
                        c => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1));
                    SaveFloatFieldPng(mrtTopTrOn, mw, mh, Path.Combine(outDir, "mrt_phifwd/topdown_mrt_transmittance.png"),
                        c => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1));
                }
                atmo.cloudDebug = 1;
                CapturePng("mrt_phifwd/topdown_debug_density.png");
                atmo.cloudDebug = 0;

                // phi_fwd OFF on top-down
                SetOrbitPose(cuWorld, 124.0, 24.5, 1.55f, 195.067, 1, tiltDeg: 0f, msOn: false);
                var pxTopQ1Off = CapturePng("mrt_phifwd/topdown_q1_phifwd_off.png", captureMrt: true);
                double topMaxTrDiff = 0;
                if (mrtHook.Captured && mrtTopTrOn != null && mrtHook.Transmittance.Length == mrtTopTrOn.Length)
                {
                    for (int i = 0; i < mrtTopTrOn.Length; i++)
                        topMaxTrDiff = Math.Max(topMaxTrDiff, Math.Abs(mrtTopTrOn[i].r - mrtHook.Transmittance[i].r));
                }
                Log(string.Format(inv, "PHI_FWD_AUDIT | topdown_q1 | dispMeanDiffDN={0:F2} | mrtMaxTransDiff={1:F7}",
                    MeanRgbDiff(pxTopQ1On, pxTopQ1Off), topMaxTrDiff));

                SetOrbitPose(cuWorld, 124.0, 24.5, 1.55f, 195.067, 2, tiltDeg: 0f, msOn: true);
                CapturePng("showcase/01_topdown_q2.png");
                LogBindings("topdown_q2", BenchCloudMs());

                // 2B. Far-distance performance fallback (远距性能回退 q=0, dist=3.2)
                SetOrbitPose(cuWorld, 124.0, 24.5, 3.20f, 195.067, 0, tiltDeg: 0f, msOn: true);
                CapturePng("views/far_perf_q0.png");
                LogBindings("far_perf_q0", BenchCloudMs());

                // 2C. Low-altitude upward (低空仰视 alt=260m) q=1 & q=2 + MRT + phi_fwd A/B + density
                SetAltitudePose(cuWorld, upLon, upLat, 260f, upLook, upSunDay, upSunDay, 1, true, msOn: true);
                var pxUpQ1 = CapturePng("showcase/02_upward_q1.png", captureMrt: true);
                var mrtUpTrOn = mrtHook.Transmittance;
                double upQ1Ms = BenchCloudMs();
                double upQ1Lap = ComputeHighFreqLaplacian(pxUpQ1, W, H, W / 8, H / 4, W * 7 / 8, H * 7 / 8);
                LogBindings("upward_q1", upQ1Ms);
                if (mrtHook.Captured)
                {
                    SaveFloatFieldPng(mrtHook.Radiance, mrtHook.Width, mrtHook.Height, Path.Combine(outDir, "mrt_phifwd/upward_mrt_radiance.png"),
                        c => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1));
                    SaveFloatFieldPng(mrtHook.Transmittance, mrtHook.Width, mrtHook.Height, Path.Combine(outDir, "mrt_phifwd/upward_mrt_transmittance.png"),
                        c => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1));
                }
                atmo.cloudDebug = 1; CapturePng("mrt_phifwd/upward_debug_density.png"); atmo.cloudDebug = 0;

                SetAltitudePose(cuWorld, upLon, upLat, 260f, upLook, upSunDay, upSunDay, 1, true, msOn: false);
                var pxUpQ1MsOff = CapturePng("mrt_phifwd/upward_q1_phifwd_off.png", captureMrt: true);
                double upMaxTrDiff = 0;
                if (mrtHook.Captured && mrtUpTrOn != null && mrtHook.Transmittance.Length == mrtUpTrOn.Length)
                {
                    for (int i = 0; i < mrtUpTrOn.Length; i++)
                        upMaxTrDiff = Math.Max(upMaxTrDiff, Math.Abs(mrtUpTrOn[i].r - mrtHook.Transmittance[i].r));
                }
                Log(string.Format(inv, "PHI_FWD_AUDIT | upward_q1  | dispMeanDiffDN={0:F2} | mrtMaxTransDiff={1:F7} | hfLap={2:F3}",
                    MeanRgbDiff(pxUpQ1, pxUpQ1MsOff), upMaxTrDiff, upQ1Lap));

                SetAltitudePose(cuWorld, upLon, upLat, 260f, upLook, upSunDay, upSunDay, 2, true, msOn: true);
                var pxUpQ2 = CapturePng("showcase/02_upward_q2.png");
                double upQ2Ms = BenchCloudMs();
                LogBindings("upward_q2", upQ2Ms);

                // 2D. Inside cloud (云内 alt=2200m) & Upper rim (上缘 alt=11500m side)
                Vector3 flyLook = (upHoriz * Mathf.Cos(3f * Mathf.Deg2Rad) + upNormW * Mathf.Sin(3f * Mathf.Deg2Rad)).normalized;
                SetAltitudePose(cuWorld, upLon, upLat, 2200f, flyLook, upSunDay, upSunDay, 1, true, msOn: true);
                CapturePng("views/inside_cloud_q1.png", captureMrt: true);
                LogBindings("inside_cloud_q1", BenchCloudMs());
                if (mrtHook.Captured)
                    SaveFloatFieldPng(mrtHook.Transmittance, mrtHook.Width, mrtHook.Height, Path.Combine(outDir, "mrt_phifwd/inside_cloud_mrt_transmittance.png"),
                        c => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1));

                SetAltitudePose(cuWorld, cuLon, cuLat, 11500f, sideLook, sideSunDay, sideSunDay, 1, true, msOn: true);
                var pxSideQ1 = CapturePng("showcase/03_side_upper_rim_q1.png");
                double sideQ1Ms = BenchCloudMs();
                LogBindings("side_upper_rim_q1", sideQ1Ms);

                SetAltitudePose(cuWorld, cuLon, cuLat, 11500f, sideLook, sideSunDay, sideSunDay, 2, true, msOn: true);
                CapturePng("showcase/03_side_upper_rim_q2.png");
                LogBindings("side_upper_rim_q2", BenchCloudMs());

                // 2E. Local unfold transition (局部展开过渡 morph ~0.55 & morph=1.0) + 2D Atlas + Round-Trip
                {
                    int focusTile = FlatScrollMath.Tile((124.0 + 180.0) / 360.0 * cuWorld.Width, (24.5 + 90.0) / 180.0 * cuWorld.Height, cuWorld.Width, cuWorld.Height);
                    var focusLoc = PlanetCoordinateSystem.FromMapUV((124.0 + 180.0) / 360.0, (24.5 + 90.0) / 180.0);
                    nav.Home(195.067); nav.Tick(3f, 195.067);
                    nav.Recenter(focusLoc, 195.067); nav.Tick(0f, 195.067);
                    nav.Focus(focusTile, 195.067); nav.SetViewport(new Rect(0, 0, W, H));
                    for (int k = 0; k < 40 && nav.Morph < 0.50f; k++) nav.Tick(0.025f, 195.067);
                    Vector3 sunL = map.Sky.Sun(180f, 195.067, true);
                    map.Configure(false, nav.PixelsPerTile, 0f);
                    map.Sky.Bind(map.Material, sunL, true, true, true, true, true, false, true, 0f);
                    nav.SyncMaterials();
                    globe.TickScroll(0f, ClimateCloudQaScenarios.DefaultVisualClock, cuWorld.Day, nav.DisplaySpin, nav.Morph, true, true, true, true, sunL, true);
                    CapturePng("views/unfold_mid_55pct.png");
                    LogBindings("unfold_mid_55pct", BenchCloudMs());

                    for (int k = 0; k < 40 && !nav.IsFlat; k++) nav.Tick(0.15f, 195.067);
                    nav.Tick(0.05f, 195.067);
                    map.Configure(false, nav.PixelsPerTile, 0f);
                    map.Sky.Bind(map.Material, sunL, true, true, true, true, true, false, true, 0f);
                    nav.SyncMaterials();
                    globe.TickScroll(0f, ClimateCloudQaScenarios.DefaultVisualClock, cuWorld.Day, nav.DisplaySpin, nav.Morph, true, true, true, true, sunL, true);
                    CapturePng("views/unfold_flat_100pct.png");
                    LogBindings("unfold_flat_100pct", BenchCloudMs());

                    nav.OpenAtlas(195.067); nav.SetViewport(new Rect(0, 0, W, H)); nav.Tick(0.05f, 195.067);
                    map.Configure(false, nav.PixelsPerTile, 0f);
                    map.Sky.Bind(map.Material, sunL, true, true, true, true, false, false, true, 0f);
                    nav.SyncMaterials();
                    globe.TickScroll(0f, ClimateCloudQaScenarios.DefaultVisualClock, cuWorld.Day, nav.DisplaySpin, nav.Morph, true, true, true, true, sunL, true);
                    CapturePng("views/atlas_2d_strategic.png");
                    LogBindings("atlas_2d_strategic", BenchCloudMs());

                    // Round-trip back to exact top-down pose
                    SetOrbitPose(cuWorld, 124.0, 24.5, 1.55f, 195.067, 1, tiltDeg: 0f, msOn: true);
                    var pxTopRT = CapturePng("views/roundtrip_topdown.png");
                    Log(string.Format(inv, "ROUNDTRIP_DIFF | topdown_before_vs_after_DN={0:F4}", MeanRgbDiff(pxTopQ1On, pxTopRT)));
                }

                // ============================================================
                // 3. SAME-DAY SAME-ANCHOR CAMERA-MOTION PARALLAX PROOF
                // ============================================================
                Log("=== 3. SAME-DAY CAMERA-MOTION PARALLAX PROOF (3D VOLUMETRIC VS 2D SHEET) ===");
                {
                    // Aim two laterally displaced cameras at the SAME Taiwan coastal ground point (121.5E, 24.2N) on Day 195.067.
                    // Compare ground shift (~0 px at screen center) vs cloud layer shift (measurable parallax in pixels!).
                    const double aLon = 121.5, aLat = 24.2;
                    double pDay = 195.067;
                    nav.Home(pDay); nav.Tick(3f, pDay);
                    Quaternion pSpin = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                    Vector3 anchorW = (pSpin * ReflectZ(SurfaceNormalLocal(aLon, aLat))).normalized;
                    Vector3 sunL = map.Sky.Sun(180f, pDay, true);

                    Color32[] RenderParallaxPose(double camLon, double camLat, float dist, bool cloudsOn, string fn)
                    {
                        Vector3 camDirW = (pSpin * ReflectZ(SurfaceNormalLocal(camLon, camLat))).normalized;
                        Vector3 camPos = camDirW * dist;
                        Vector3 lookW = (anchorW - camPos).normalized;
                        camera.targetTexture = rt;
                        camera.orthographic = false;
                        camera.fieldOfView = CameraFovDeg;
                        camera.nearClipPlane = 0.0002f;
                        camera.farClipPlane = 80f;
                        camera.ResetProjectionMatrix();
                        camera.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(lookW, camDirW));

                        var s = globe.Snapshot.settings;
                        s.cloudQuality = 1; s.clouds = cloudsOn; s.atmosphere = true; s.godRays = true; s.multipleScattering = true;
                        map.Sky.Bind(map.Material, sunL, cloudsOn, cloudsOn, true, true, false, false, true, 0f);
                        nav.SyncMaterials();
                        globe.TickScroll(0f, ClimateCloudQaScenarios.DefaultVisualClock, cuWorld.Day, nav.DisplaySpin, 0f, cloudsOn, true, true, cloudsOn, sunL, true);
                        return CapturePng(fn);
                    }

                    // Two close positions (+-0.6 deg) aimed at same ground anchor -> ground stays centered while clouds at 2-10km shift visibly!
                    var pxParL = RenderParallaxPose(aLon - 0.8, aLat - 0.5, 1.055f, true, "parallax/01_left_clouds_on.png");
                    var pxParR = RenderParallaxPose(aLon + 0.8, aLat - 0.5, 1.055f, true, "parallax/02_right_clouds_on.png");
                    var pxParLOff = RenderParallaxPose(aLon - 0.8, aLat - 0.5, 1.055f, false, "parallax/03_left_clouds_off.png");
                    var pxParROff = RenderParallaxPose(aLon + 0.8, aLat - 0.5, 1.055f, false, "parallax/04_right_clouds_off.png");

                    // Isolate pure cloud luminance residual (cloudsOn - cloudsOff) for cross-correlation parallax lag
                    Color32[] Residual(Color32[] on, Color32[] off)
                    {
                        var r = new Color32[on.Length];
                        for (int i = 0; i < on.Length; i++)
                        {
                            byte dr = (byte)Mathf.Clamp(128 + (on[i].r - off[i].r), 0, 255);
                            byte dg = (byte)Mathf.Clamp(128 + (on[i].g - off[i].g), 0, 255);
                            byte db = (byte)Mathf.Clamp(128 + (on[i].b - off[i].b), 0, 255);
                            r[i] = new Color32(dr, dg, db, 255);
                        }
                        return r;
                    }
                    double groundShiftPx = ComputeCrossCorrelationShiftX(pxParLOff, pxParROff, W, H, H * 3 / 8, H * 5 / 8, W * 3 / 8, W * 5 / 8, 48);
                    double cloudShiftPx = ComputeCrossCorrelationShiftX(Residual(pxParL, pxParLOff), Residual(pxParR, pxParROff), W, H, H * 3 / 8, H * 5 / 8, W * 3 / 8, W * 5 / 8, 48);
                    Log(string.Format(inv, "PARALLAX_SHIFT | groundCenterLagPx={0:F1} | cloudResidualLagPx={1:F1} | parallaxDeltaPx={2:F1}",
                        groundShiftPx, cloudShiftPx, cloudShiftPx - groundShiftPx));
                }

                // ============================================================
                // 4. CAMERA-MOTION TEMPORAL FLICKER QUANTIFICATION (8-STEP PAN)
                // ============================================================
                Log("=== 4. CAMERA-MOTION FLICKER (8-STEP SMOOTH YAW/PITCH PAN, q=1) ===");
                {
                    var upFrames = new List<Color32[]>();
                    var sideFrames = new List<Color32[]>();
                    for (int k = 0; k < 8; k++)
                    {
                        float yawDeg = (-1.05f + k * 0.30f);
                        Vector3 uLook = Quaternion.AngleAxis(yawDeg, upNormW) * upLook;
                        SetAltitudePose(cuWorld, upLon, upLat, 260f, uLook, upSunDay, upSunDay, 1, true, msOn: true);
                        upFrames.Add(CapturePng(null));

                        Vector3 sLook = Quaternion.AngleAxis(yawDeg, sideNormW) * sideLook;
                        SetAltitudePose(cuWorld, cuLon, cuLat, 11500f, sLook, sideSunDay, sideSunDay, 1, true, msOn: true);
                        sideFrames.Add(CapturePng(null));
                    }
                    var uf = ComputeMotionFlicker(upFrames, W, H, W / 8, H / 4, W * 7 / 8, H * 7 / 8);
                    var sf = ComputeMotionFlicker(sideFrames, W, H, W / 8, H / 4, W * 7 / 8, H * 3 / 4);
                    Log(string.Format(inv, "MOTION_FLICKER | upward_q1 | hfMean={0:F3} hfP95={1:F3} hfMax={2:F2} lumaStep={3:F3}",
                        uf.meanHfDiff, uf.p95HfDiff, uf.maxHfDiff, uf.meanLumaStep));
                    Log(string.Format(inv, "MOTION_FLICKER | side_q1   | hfMean={0:F3} hfP95={1:F3} hfMax={2:F2} lumaStep={3:F3}",
                        sf.meanHfDiff, sf.p95HfDiff, sf.maxHfDiff, sf.meanLumaStep));
                }

                // ============================================================
                // 5. TYPHOON EYEWALL SHOWCASE (q=1, q=2, phi_fwd A/B, GPU ms)
                // ============================================================
                Log("=== 5. TYPHOON EYEWALL STADIUM SHOWCASE (q=1 / q=2) ===");
                {
                    var tyWorld = CreateCat5TyphoonWorld(baseEarth, 228.068, 127.2, 19.2, 4.15);
                    InstallWorldSync(app, tyWorld);
                    map = appType.GetField("map", Private).GetValue(app) as FlatMapRenderer;
                    globe = appType.GetField("globe", Private).GetValue(app) as FlatGlobePresenter;
                    nav = app.Navigation;
                    atmo = Array.Find(camera.GetComponents<OrbisAtmosphereRenderer>(), a => a.enabled && a.view == globe.View);
                    PublishSkyExact(map, tyWorld, ClimateCloudQaScenarios.DefaultVisualClock, 1024, tyWorld.Day + 0.25);

                    const double tyLon = 127.42, tyLat = 19.32;
                    Vector3 tyNormLocal = SurfaceNormalLocal(tyLon, tyLat);
                    double tySunDay = FindSunDayForElevation(map.Sky, tyNormLocal, 228.0, 38f, afternoon: true);
                    nav.Home(tySunDay); nav.Tick(3f, tySunDay);
                    Quaternion tySpin = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                    Vector3 tyNormW = (tySpin * ReflectZ(tyNormLocal)).normalized;
                    Vector3 tyEyeW = (tySpin * ReflectZ(SurfaceNormalLocal(127.2, 19.2))).normalized;
                    Vector3 tyHoriz = Vector3.ProjectOnPlane(tyEyeW - tyNormW, tyNormW).normalized;
                    Vector3 tyLook = (tyHoriz * Mathf.Cos(2f * Mathf.Deg2Rad) + tyNormW * Mathf.Sin(2f * Mathf.Deg2Rad)).normalized;

                    SetAltitudePose(tyWorld, tyLon, tyLat, 8800f, tyLook, tySunDay, tySunDay, 1, true, msOn: true);
                    var pxTyQ1 = CapturePng("showcase/04_typhoon_eyewall_q1.png");
                    double tyQ1Ms = BenchCloudMs();
                    LogBindings("typhoon_eyewall_q1", tyQ1Ms);

                    SetAltitudePose(tyWorld, tyLon, tyLat, 8800f, tyLook, tySunDay, tySunDay, 2, true, msOn: true);
                    CapturePng("showcase/04_typhoon_eyewall_q2.png");
                    LogBindings("typhoon_eyewall_q2", BenchCloudMs());

                    // Top-down typhoon view
                    SetOrbitPose(tyWorld, 127.2, 19.2, 1.38f, tySunDay, 1, tiltDeg: 0f, msOn: true);
                    CapturePng("showcase/05_typhoon_topdown_q1.png");
                    LogBindings("typhoon_topdown_q1", BenchCloudMs());

                    // Typhoon camera-motion flicker
                    var tyFrames = new List<Color32[]>();
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 tLook = Quaternion.AngleAxis(-1.05f + k * 0.30f, tyNormW) * tyLook;
                        SetAltitudePose(tyWorld, tyLon, tyLat, 8800f, tLook, tySunDay, tySunDay, 1, true, msOn: true);
                        tyFrames.Add(CapturePng(null));
                    }
                    var tf = ComputeMotionFlicker(tyFrames, W, H, W / 8, H / 4, W * 7 / 8, H * 7 / 8);
                    Log(string.Format(inv, "MOTION_FLICKER | typhoon_q1| hfMean={0:F3} hfP95={1:F3} hfMax={2:F2} lumaStep={3:F3}",
                        tf.meanHfDiff, tf.p95HfDiff, tf.maxHfDiff, tf.meanLumaStep));
                }

                // ============================================================
                // 6. PAUSE -> FAST DATE ADVANCE + QUALITY RESIZE CHECK
                // ============================================================
                Log("=== 6. PAUSE FAST DATE ADVANCE & QUALITY RESIZE ===");
                {
                    var pWorld = ClimateCloudQaScenarios.DeepCloneWorld(baseEarth);
                    pWorld.Day = 195.067;
                    pWorld.Cyclones = FlatCycloneWeather.SpinUp(pWorld);
                    InstallWorldSync(app, pWorld);
                    map = appType.GetField("map", Private).GetValue(app) as FlatMapRenderer;
                    globe = appType.GetField("globe", Private).GetValue(app) as FlatGlobePresenter;
                    nav = app.Navigation;
                    atmo = Array.Find(camera.GetComponents<OrbisAtmosphereRenderer>(), a => a.enabled && a.view == globe.View);

                    PublishSkyExact(map, pWorld, FlatSkyCalendar.ToWeatherTime(pWorld.Day), 1024, pWorld.Day, animate: false);
                    SetOrbitPose(pWorld, 124.0, 25.0, 2.2f, 195.067, 1);
                    var pxP195 = CapturePng("states/pause_d195.png");
                    long? st195 = map.Sky.RegionalClimateStep;

                    // Quality resize q=1 (1024) -> q=0 (512): check immediate unready frame!
                    map.Sky.Tick(0f, true, false, false, 512, pWorld.Day);
                    if (!map.Sky.Ready)
                    {
                        SetOrbitPose(pWorld, 124.0, 25.0, 2.2f, 195.067, 0);
                        var cm = globe.View.CloudMaterial;
                        var pxResizeOn = CapturePng("states/qresize_unready_on.png");
                        globe.Snapshot.settings.clouds = false; cm.SetFloat("_Clouds", 0f);
                        var pxResizeOff = CapturePng("states/qresize_unready_off.png");
                        Log(string.Format(inv, "QRESIZE_UNREADY | Sky.Ready=False | scs={0:F0} lw={1:F0} | onVsOffDN={2:F3}",
                            cm.GetFloat("_ScrollCloudSource"), cm.GetFloat("_LayeredWeather"), MeanRgbDiff(pxResizeOn, pxResizeOff)));
                    }

                    // Jump +90 days while paused (animate=false)
                    pWorld.Day = 285.067;
                    pWorld.Cyclones = FlatCycloneWeather.SpinUp(pWorld);
                    PublishSkyExact(map, pWorld, FlatSkyCalendar.ToWeatherTime(pWorld.Day), 1024, pWorld.Day, animate: false);
                    SetOrbitPose(pWorld, 124.0, 25.0, 2.2f, 195.067, 1);
                    var pxP285 = CapturePng("states/pause_d285.png");
                    Log(string.Format(inv, "PAUSE_JUMP | step195={0} -> step285={1} | pubDay={2:F3} | d195_vs_d285_DN={3:F2}",
                        st195, map.Sky.RegionalClimateStep, map.Sky.PublishedCalendarDay, MeanRgbDiff(pxP195, pxP285)));
                }
            }
            finally
            {
                if (mrtHook) UnityEngine.Object.DestroyImmediate(mrtHook);
                camera.targetTexture = oldTarget;
                nav.SetViewport(oldRect);
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(readTex);
                UnityEngine.Object.DestroyImmediate(read1);
                EditorSettings.asyncShaderCompilation = savedEditorAsync;
                ShaderUtil.allowAsyncCompilation = savedAsync;
                File.WriteAllLines(Path.Combine(outDir, "audit_report.txt"), log, new UTF8Encoding(false));
            }
        }

        static void ExecuteOpticsSeparationAudit(FlatWorldApp app, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(Path.Combine(outDir, "unfold_36_55"));
            Directory.CreateDirectory(Path.Combine(outDir, "low_angle"));
            Directory.CreateDirectory(Path.Combine(outDir, "inside_cloud"));
            Directory.CreateDirectory(Path.Combine(outDir, "mrt_separation"));
            Directory.CreateDirectory(Path.Combine(outDir, "motion"));
            Directory.CreateDirectory(Path.Combine(outDir, "preserve"));

            var inv = CultureInfo.InvariantCulture;
            var log = new List<string>();
            void Log(string s) { log.Add(s); Debug.Log("[HPCloudOptics] " + s); }

            bool savedAsync = ShaderUtil.allowAsyncCompilation;
            bool savedEditorAsync = EditorSettings.asyncShaderCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            EditorSettings.asyncShaderCompilation = false;

            var baseEarth = LoadEarth512();
            var world = ClimateCloudQaScenarios.DeepCloneWorld(baseEarth);
            world.Day = 195.067;
            world.Cyclones = FlatCycloneWeather.SpinUp(world);
            InstallWorldSync(app, world);

            var appType = typeof(FlatWorldApp);
            var camera = appType.GetField("viewCamera", Private).GetValue(app) as Camera;
            var map = appType.GetField("map", Private).GetValue(app) as FlatMapRenderer;
            var globe = appType.GetField("globe", Private).GetValue(app) as FlatGlobePresenter;
            var nav = app.Navigation;
            var lens = camera.GetComponent<OrbisCelestialLens>();
            var atmo = Array.Find(camera.GetComponents<OrbisAtmosphereRenderer>(), a => a.enabled && a.view == globe.View);
            var mrtHook = camera.gameObject.AddComponent<MrtProbeHook>();

            var oldTarget = camera.targetTexture;
            var oldRect = camera.pixelRect;
            var oldActive = RenderTexture.active;
            const int W = 1280, H = 720;
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var readTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var read1 = new Texture2D(1, 1, TextureFormat.RGB24, false);

            Color32[] CapturePng(string relPath, bool captureMrt = false)
            {
                if (captureMrt)
                {
                    mrtHook.CloudMaterial = globe.View.CloudMaterial;
                    mrtHook.Captured = false;
                    mrtHook.Arm = true;
                }
                camera.targetTexture = rt;
                camera.pixelRect = new Rect(0, 0, W, H);
                camera.Render();
                RenderTexture.active = rt;
                readTex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                readTex.Apply(false, false);
                var px = readTex.GetPixels32();
                if (!string.IsNullOrEmpty(relPath))
                    File.WriteAllBytes(Path.Combine(outDir, relPath), readTex.EncodeToPNG());
                return px;
            }

            void SaveMrtSet(string prefix)
            {
                if (!mrtHook.Captured || mrtHook.Radiance == null || mrtHook.Transmittance == null) return;
                int mw = mrtHook.Width, mh = mrtHook.Height;
                SaveFloatFieldPng(mrtHook.Radiance, mw, mh, Path.Combine(outDir, $"mrt_separation/{prefix}_mrt_radiance.png"),
                    c => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1));
                SaveFloatFieldPng(mrtHook.Transmittance, mw, mh, Path.Combine(outDir, $"mrt_separation/{prefix}_mrt_transmittance.png"),
                    c => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1));
                // Pure-volume tone-mapped over black sky (ACES + sRGB) so volumetric scattering & soft edges
                // are directly readable with zero ocean/terrain/water reflection underneath.
                static float AcesSrgb(float x)
                {
                    x = Mathf.Max(0f, x);
                    float t = Mathf.Clamp01((x * (2.51f * x + 0.03f)) / (x * (2.43f * x + 0.59f) + 0.14f));
                    return Mathf.LinearToGammaSpace(t);
                }
                SaveFloatFieldPng(mrtHook.Radiance, mw, mh, Path.Combine(outDir, $"mrt_separation/{prefix}_pure_volume.png"),
                    c => new Color(AcesSrgb(c.r), AcesSrgb(c.g), AcesSrgb(c.b), 1f));
            }

            void SetUnfoldPose(WorldData w, double visualClock, double lonDeg, double latDeg, float targetMorph, double day, int q, bool fullRes, bool cloudsOn, bool msOn, bool godRays)
            {
                var loc = PlanetCoordinateSystem.FromMapUV((lonDeg + 180.0) / 360.0, (latDeg + 90.0) / 180.0);
                nav.Home(day); nav.Tick(3f, day);
                nav.Recenter(loc, day);
                nav.SetViewport(new Rect(0, 0, W, H));
                nav.Tick(0f, day);
                float targetAlt = 0.60f - Mathf.Clamp01(targetMorph) * 0.38f;
                float homeZoom = Mathf.Log(2.1f / 0.22f) / 0.075f;
                float targetZoom = Mathf.Log(Mathf.Max(0.22f, targetAlt) / 0.22f) / 0.075f;
                float deltaZoom = targetZoom - homeZoom;
                while (Mathf.Abs(deltaZoom) > 0.01f)
                {
                    float step = Mathf.Clamp(deltaZoom, -20f, 20f);
                    nav.Wheel(step, camera.pixelRect.center, day);
                    nav.Tick(3f, day);
                    deltaZoom -= step;
                }
                nav.Tick(3f, day);

                var s = globe.Snapshot.settings;
                s.exposure = ClimateCloudQaScenarios.DefaultExposure;
                s.cloudQuality = q;
                s.cloudCover = 1f;
                s.cloudDensity = 1f;
                s.clouds = cloudsOn;
                s.cloudShadows = true;
                s.godRays = godRays;
                s.atmosphere = true;
                s.night = true;
                s.multipleScattering = msOn;
                if (lens) lens.strength = 0f;
                atmo.enabled = true;
                atmo.fullResolutionReference = fullRes;
                atmo.referenceCloudIntegration = false;
                atmo.cloudDebug = 0;

                Vector3 sunL = map.Sky.Sun(180f, day, true);
                map.Configure(false, nav.PixelsPerTile, 0f);
                map.SetVegetationDay(w.Day);
                map.Sky.Bind(map.Material, sunL, cloudsOn, true, true, true, false, false, true, 0f);
                map.Material.SetFloat("_WaterClock", 0f);
                nav.SyncMaterials();
                globe.TickScroll(0f, visualClock, w.Day, nav.DisplaySpin, nav.Morph, cloudsOn, true, true, cloudsOn, sunL, true);
            }

            void SetAltitudePose(WorldData w, double lonDeg, double latDeg, float altM, Vector3 lookW, double spinDay, double sunDay, int q, bool fullRes, bool godRays, bool msOn)
            {
                var loc = PlanetCoordinateSystem.FromMapUV((lonDeg + 180.0) / 360.0, (latDeg + 90.0) / 180.0);
                nav.Home(spinDay); nav.Tick(3f, spinDay);
                nav.Recenter(loc, spinDay);
                nav.SetViewport(new Rect(0, 0, W, H));
                nav.Tick(0f, spinDay);

                Vector3 localDir = SurfaceNormalLocal(lonDeg, latDeg);
                Quaternion spinQ = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                Vector3 worldNormal = (spinQ * ReflectZ(localDir)).normalized;
                float altR = altM * OrbisWeatherField.HeightScale;
                camera.targetTexture = rt;
                camera.orthographic = false;
                camera.fieldOfView = CameraFovDeg;
                camera.nearClipPlane = Mathf.Clamp(altR * 0.25f, 0.00004f, 0.002f);
                camera.farClipPlane = 80f;
                camera.ResetProjectionMatrix();
                camera.transform.SetPositionAndRotation(worldNormal * (1f + altR), Quaternion.LookRotation(lookW.normalized, worldNormal));

                var s = globe.Snapshot.settings;
                s.exposure = ClimateCloudQaScenarios.DefaultExposure;
                s.cloudQuality = q;
                s.cloudCover = 1f;
                s.cloudDensity = 1f;
                s.clouds = true;
                s.cloudShadows = true;
                s.godRays = godRays;
                s.atmosphere = true;
                s.night = true;
                s.multipleScattering = msOn;
                if (lens) lens.strength = 0f;
                atmo.enabled = true;
                atmo.fullResolutionReference = fullRes;
                atmo.referenceCloudIntegration = false;
                atmo.cloudDebug = 0;

                Vector3 sunL = map.Sky.Sun(180f, sunDay, true);
                map.Configure(false, nav.PixelsPerTile, 0f);
                map.SetVegetationDay(w.Day);
                map.Sky.Bind(map.Material, sunL, true, true, true, true, false, false, true, 0f);
                map.Material.SetFloat("_WaterClock", 0f);
                nav.SyncMaterials();
                globe.TickScroll(0f, ClimateCloudQaScenarios.DefaultVisualClock, w.Day, nav.DisplaySpin, 0f, true, true, true, true, sunL, true);
            }

            try
            {
                PublishSkyExact(map, world, ClimateCloudQaScenarios.DefaultVisualClock, 1024, world.Day + 0.25);

                // ============================================================
                // A. 36% / 45% / 55% UNFOLD FACTOR SEPARATION (q=0, q=1, q=2)
                //    Separates: (1) Multi-pass (no-volume), (2) Single-volume
                //    (pure MRT + debug density/direct/multiple + phi_fwd A/B),
                //    (3) Upsampling (0.68x MRT vs 1.0x FullResReference).
                // ============================================================
                Log("=== A. 36%-55% UNFOLD SEPARATION MATRIX (MULTI-PASS / VOLUME / UPSAMPLE) ===");
                const double uLon = 124.0, uLat = 24.5;
                foreach (float targetMorph in new[] { 0.36f, 0.45f, 0.55f })
                {
                    int pct = Mathf.RoundToInt(targetMorph * 100f);
                    SetUnfoldPose(world, ClimateCloudQaScenarios.DefaultVisualClock, uLon, uLat, targetMorph, 195.067, 1, fullRes: false, cloudsOn: true, msOn: true, godRays: true);
                    var pxMrt = CapturePng($"unfold_36_55/unfold_{pct}pct_q1_mrt068.png", captureMrt: true);
                    var mrtTrOn = mrtHook.Transmittance;
                    SaveMrtSet($"unfold_{pct}pct_q1");
                    double lapMrt = ComputeHighFreqLaplacian(pxMrt, W, H, W / 8, H / 8, W * 7 / 8, H * 7 / 8);

                    // Debug channels (density / direct / multiple)
                    atmo.cloudDebug = 1; CapturePng($"mrt_separation/unfold_{pct}pct_q1_debug_density.png");
                    atmo.cloudDebug = 2; CapturePng($"mrt_separation/unfold_{pct}pct_q1_debug_direct.png");
                    atmo.cloudDebug = 3; CapturePng($"mrt_separation/unfold_{pct}pct_q1_debug_multiple.png");
                    atmo.cloudDebug = 0;

                    // Upsampling isolation: Full-res 1.0x reference vs 0.68x MRT
                    SetUnfoldPose(world, ClimateCloudQaScenarios.DefaultVisualClock, uLon, uLat, targetMorph, 195.067, 1, fullRes: true, cloudsOn: true, msOn: true, godRays: true);
                    var pxFull = CapturePng($"unfold_36_55/unfold_{pct}pct_q1_fullres.png");
                    double lapFull = ComputeHighFreqLaplacian(pxFull, W, H, W / 8, H / 8, W * 7 / 8, H * 7 / 8);

                    // Multi-pass isolation: No-volume (clouds off, atmosphere off via atmo.enabled=false)
                    SetUnfoldPose(world, ClimateCloudQaScenarios.DefaultVisualClock, uLon, uLat, targetMorph, 195.067, 1, fullRes: false, cloudsOn: false, msOn: true, godRays: false);
                    var pxNoCloudVol = CapturePng($"unfold_36_55/unfold_{pct}pct_q1_no_cloud_volume.png");
                    atmo.enabled = false;
                    var pxNoAtmo = CapturePng($"unfold_36_55/unfold_{pct}pct_q1_no_volume_surface_only.png");
                    atmo.enabled = true;

                    // phi_fwd invariance on 36-55% unfold
                    SetUnfoldPose(world, ClimateCloudQaScenarios.DefaultVisualClock, uLon, uLat, targetMorph, 195.067, 1, fullRes: false, cloudsOn: true, msOn: false, godRays: true);
                    var pxMsOff = CapturePng($"mrt_separation/unfold_{pct}pct_q1_phifwd_off.png", captureMrt: true);
                    double maxTrDiff = 0;
                    if (mrtHook.Captured && mrtTrOn != null && mrtHook.Transmittance.Length == mrtTrOn.Length)
                        for (int i = 0; i < mrtTrOn.Length; i++)
                            maxTrDiff = Math.Max(maxTrDiff, Math.Abs(mrtTrOn[i].r - mrtHook.Transmittance[i].r));

                    Log(string.Format(inv,
                        "UNFOLD_{0}PCT_Q1 | actualMorph={1:F3} | mrtVsFullDN={2:F2} | cloudContributionDN={3:F2} | atmoVsSurfaceDN={4:F2} | phiFwdDN={5:F2} | mrtMaxTrDiff={6:F7} | hfLapMrt={7:F2} hfLapFull={8:F2}",
                        pct, nav.Morph, MeanRgbDiff(pxMrt, pxFull), MeanRgbDiff(pxMrt, pxNoCloudVol),
                        MeanRgbDiff(pxNoCloudVol, pxNoAtmo), MeanRgbDiff(pxMrt, pxMsOff), maxTrDiff, lapMrt, lapFull));

                    if (pct == 36)
                    {
                        foreach (int qTier in new[] { 0, 2 })
                        {
                            SetUnfoldPose(world, ClimateCloudQaScenarios.DefaultVisualClock, uLon, uLat, targetMorph, 195.067, qTier, fullRes: false, cloudsOn: true, msOn: true, godRays: true);
                            CapturePng($"unfold_36_55/unfold_36pct_q{qTier}.png");
                        }
                    }
                }

                // Exact user-feedback date/time repro (Year 1 Jan 5 05:49, Day=4.2423611, lon=45.7E lat=-8.2N, morph=36% & 55%, Balanced q=1)
                {
                    var jan5World = ClimateCloudQaScenarios.DeepCloneWorld(baseEarth);
                    jan5World.Day = 4.0 + (5.0 * 60.0 + 49.0) / 1440.0;
                    jan5World.Cyclones = FlatCycloneWeather.SpinUp(jan5World);
                    InstallWorldSync(app, jan5World);
                    map = appType.GetField("map", Private).GetValue(app) as FlatMapRenderer;
                    globe = appType.GetField("globe", Private).GetValue(app) as FlatGlobePresenter;
                    nav = app.Navigation;
                    atmo = Array.Find(camera.GetComponents<OrbisAtmosphereRenderer>(), a => a.enabled && a.view == globe.View);
                    double jan5Clock = FlatSkyCalendar.ToWeatherTime(jan5World.Day);
                    PublishSkyExact(map, jan5World, jan5Clock, 1024, jan5World.Day, animate: false);
                    foreach (float tm in new[] { 0.36f, 0.55f })
                    {
                        int pct = Mathf.RoundToInt(tm * 100f);
                        SetUnfoldPose(jan5World, jan5Clock, 45.7, -8.2, tm, jan5World.Day, 1, fullRes: false, cloudsOn: true, msOn: true, godRays: true);
                        var pxJ5Mrt = CapturePng($"unfold_36_55/user_repro_jan5_{pct}pct_q1_mrt068.png", captureMrt: true);
                        SaveMrtSet($"user_repro_jan5_{pct}pct_q1");
                        SetUnfoldPose(jan5World, jan5Clock, 45.7, -8.2, tm, jan5World.Day, 1, fullRes: true, cloudsOn: true, msOn: true, godRays: true);
                        var pxJ5Full = CapturePng($"unfold_36_55/user_repro_jan5_{pct}pct_q1_fullres.png");
                        SetUnfoldPose(jan5World, jan5Clock, 45.7, -8.2, tm, jan5World.Day, 1, fullRes: false, cloudsOn: false, msOn: true, godRays: false);
                        var pxJ5NoCloud = CapturePng($"unfold_36_55/user_repro_jan5_{pct}pct_q1_no_cloud.png");
                        Log(string.Format(inv, "USER_REPRO_JAN5_{0}PCT | actualMorph={1:F3} | mrtVsFullDN={2:F2} | cloudDN={3:F2}",
                            pct, nav.Morph, MeanRgbDiff(pxJ5Mrt, pxJ5Full), MeanRgbDiff(pxJ5Mrt, pxJ5NoCloud)));
                    }
                    // Restore primary Day 195.067 world for wheel-scroll, low-angle, and inside-cloud checks
                    InstallWorldSync(app, world);
                    map = appType.GetField("map", Private).GetValue(app) as FlatMapRenderer;
                    globe = appType.GetField("globe", Private).GetValue(app) as FlatGlobePresenter;
                    nav = app.Navigation;
                    atmo = Array.Find(camera.GetComponents<OrbisAtmosphereRenderer>(), a => a.enabled && a.view == globe.View);
                    PublishSkyExact(map, world, ClimateCloudQaScenarios.DefaultVisualClock, 1024, world.Day + 0.25);
                }

                // ============================================================
                // B. WHEEL-SCROLL MOTION STABILITY (36% -> 55% UNFOLD)
                //    Directly separates 0.68x MRT upsampling shimmer vs 1.0x full-res!
                // ============================================================
                Log("=== B. WHEEL-SCROLL MOTION STABILITY (36% -> 55% UNFOLD, q=1) ===");
                foreach (bool fullRes in new[] { false, true })
                {
                    SetUnfoldPose(world, ClimateCloudQaScenarios.DefaultVisualClock, uLon, uLat, 0.34f, 195.067, 1, fullRes: fullRes, cloudsOn: true, msOn: true, godRays: true);
                    var frames = new List<Color32[]>();
                    Vector3 sunL = map.Sky.Sun(180f, 195.067, true);
                    for (int k = 0; k < 10; k++)
                    {
                        nav.Wheel(-0.42f, camera.pixelRect.center, 195.067);
                        nav.Tick(1f / 30f, 195.067);
                        map.Configure(false, nav.PixelsPerTile, 0f);
                        map.Sky.Bind(map.Material, sunL, true, true, true, true, false, false, true, 0f);
                        nav.SyncMaterials();
                        globe.TickScroll(0f, ClimateCloudQaScenarios.DefaultVisualClock, world.Day, nav.DisplaySpin, nav.Morph, true, true, true, true, sunL, true);
                        string snap = (k == 0 || k == 5 || k == 9) ? $"motion/wheel_{(fullRes ? "fullres" : "mrt068")}_f{k:D2}_m{Mathf.RoundToInt(nav.Morph * 100)}.png" : null;
                        frames.Add(CapturePng(snap));
                    }
                    var mf = ComputeMotionFlicker(frames, W, H, W / 8, H / 8, W * 7 / 8, H * 7 / 8);
                    Log(string.Format(inv, "WHEEL_FLICKER | {0,-8} | endMorph={1:F3} | hfMean={2:F3} hfP95={3:F3} hfMax={4:F2} lumaStep={5:F3}",
                        fullRes ? "fullres" : "mrt068", nav.Morph, mf.meanHfDiff, mf.p95HfDiff, mf.maxHfDiff, mf.meanLumaStep));
                }

                // ============================================================
                // C. LOW-ANGLE BACKLIT (迎光 + 穿云间隙光) & FRONTLIT (背光)
                // ============================================================
                Log("=== C. LOW-ANGLE BACKLIT (CREPUSCULAR SHAFTS) & FRONTLIT ===");
                const double lowLon = 130.8, lowLat = 19.4;
                Vector3 lowNormLocal = SurfaceNormalLocal(lowLon, lowLat);
                double lowSunDay = FindSunDayForElevation(map.Sky, lowNormLocal, 195.0, 22f, afternoon: true);
                nav.Home(lowSunDay); nav.Tick(3f, lowSunDay);
                Quaternion lowSpin = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                Vector3 lowNormW = (lowSpin * ReflectZ(lowNormLocal)).normalized;
                Vector3 lowSunW = (lowSpin * ReflectZ(map.Sky.Sun(180f, lowSunDay, true))).normalized;
                Vector3 toSunH = Vector3.ProjectOnPlane(lowSunW, lowNormW).normalized;
                Vector3 rightH = Vector3.Cross(lowNormW, toSunH).normalized;

                // Backlit: 14 deg azimuth off sun, +14 deg elevation (looking into sunlit cloud gaps & silver linings)
                Vector3 backHoriz = (toSunH * Mathf.Cos(14f * Mathf.Deg2Rad) + rightH * Mathf.Sin(14f * Mathf.Deg2Rad)).normalized;
                Vector3 backLook = (backHoriz * Mathf.Cos(14f * Mathf.Deg2Rad) + lowNormW * Mathf.Sin(14f * Mathf.Deg2Rad)).normalized;
                // Frontlit: 155 deg away from sun, +16 deg elevation (sun behind camera illuminating fluffy white billows)
                Vector3 frontHoriz = (toSunH * Mathf.Cos(155f * Mathf.Deg2Rad) + rightH * Mathf.Sin(155f * Mathf.Deg2Rad)).normalized;
                Vector3 frontLook = (frontHoriz * Mathf.Cos(16f * Mathf.Deg2Rad) + lowNormW * Mathf.Sin(16f * Mathf.Deg2Rad)).normalized;

                SetAltitudePose(world, lowLon, lowLat, 260f, backLook, lowSunDay, lowSunDay, 1, false, godRays: true, msOn: true);
                var pxBackOn = CapturePng("low_angle/01_backlit_260m_q1_godrays_on.png", captureMrt: true);
                SaveMrtSet("low_backlit_260m_q1");
                SetAltitudePose(world, lowLon, lowLat, 260f, backLook, lowSunDay, lowSunDay, 1, false, godRays: false, msOn: true);
                var pxBackNoShaft = CapturePng("low_angle/01_backlit_260m_q1_godrays_off.png");
                SetAltitudePose(world, lowLon, lowLat, 260f, backLook, lowSunDay, lowSunDay, 2, false, godRays: true, msOn: true);
                CapturePng("low_angle/01_backlit_260m_q2.png");
                Log(string.Format(inv, "LOW_BACKLIT_260M | godRayShaftDeltaDN={0:F2} | hfLap={1:F2}",
                    MeanRgbDiff(pxBackOn, pxBackNoShaft), ComputeHighFreqLaplacian(pxBackOn, W, H, W / 8, H / 4, W * 7 / 8, H * 7 / 8)));

                SetAltitudePose(world, lowLon, lowLat, 260f, frontLook, lowSunDay, lowSunDay, 1, false, godRays: true, msOn: true);
                var pxFrontOn = CapturePng("low_angle/02_frontlit_260m_q1.png", captureMrt: true);
                SaveMrtSet("low_frontlit_260m_q1");
                SetAltitudePose(world, lowLon, lowLat, 260f, frontLook, lowSunDay, lowSunDay, 2, false, godRays: true, msOn: true);
                CapturePng("low_angle/02_frontlit_260m_q2.png");

                // Low-angle backlit yaw flicker
                var backFrames = new List<Color32[]>();
                for (int k = 0; k < 8; k++)
                {
                    Vector3 bLook = Quaternion.AngleAxis(-1.05f + k * 0.30f, lowNormW) * backLook;
                    SetAltitudePose(world, lowLon, lowLat, 260f, bLook, lowSunDay, lowSunDay, 1, false, godRays: true, msOn: true);
                    backFrames.Add(CapturePng(null));
                }
                var bf = ComputeMotionFlicker(backFrames, W, H, W / 8, H / 4, W * 7 / 8, H * 7 / 8);
                Log(string.Format(inv, "LOW_BACKLIT_FLICKER | hfMean={0:F3} hfP95={1:F3} hfMax={2:F2}", bf.meanHfDiff, bf.p95HfDiff, bf.maxHfDiff));

                // ============================================================
                // D. INSIDE CLOUD (云内, alt=2200m, BACKLIT & FRONTLIT)
                // ============================================================
                Log("=== D. INSIDE CLOUD (alt=2200m, BACKLIT & FRONTLIT) ===");
                Vector3 inBackLook = (backHoriz * Mathf.Cos(3f * Mathf.Deg2Rad) + lowNormW * Mathf.Sin(3f * Mathf.Deg2Rad)).normalized;
                Vector3 inFrontLook = (frontHoriz * Mathf.Cos(3f * Mathf.Deg2Rad) + lowNormW * Mathf.Sin(3f * Mathf.Deg2Rad)).normalized;
                SetAltitudePose(world, lowLon, lowLat, 2200f, inBackLook, lowSunDay, lowSunDay, 1, false, godRays: true, msOn: true);
                var pxInBack = CapturePng("inside_cloud/01_inside_backlit_2200m_q1.png", captureMrt: true);
                SaveMrtSet("inside_backlit_2200m_q1");
                SetAltitudePose(world, lowLon, lowLat, 2200f, inFrontLook, lowSunDay, lowSunDay, 1, false, godRays: true, msOn: true);
                CapturePng("inside_cloud/02_inside_frontlit_2200m_q1.png", captureMrt: true);
                SaveMrtSet("inside_frontlit_2200m_q1");
                var inFrames = new List<Color32[]>();
                for (int k = 0; k < 8; k++)
                {
                    Vector3 iLook = Quaternion.AngleAxis(-1.05f + k * 0.30f, lowNormW) * inBackLook;
                    SetAltitudePose(world, lowLon, lowLat, 2200f, iLook, lowSunDay, lowSunDay, 1, false, godRays: true, msOn: true);
                    inFrames.Add(CapturePng(null));
                }
                var inf = ComputeMotionFlicker(inFrames, W, H, W / 8, H / 4, W * 7 / 8, H * 7 / 8);
                Log(string.Format(inv, "INSIDE_CLOUD_FLICKER | hfMean={0:F3} hfP95={1:F3} hfMax={2:F2}", inf.meanHfDiff, inf.p95HfDiff, inf.maxHfDiff));

                // ============================================================
                // E. PRESERVATION CHECKS (TYPHOON EYE + HIGH CIRRUS + SHADOWS)
                // ============================================================
                Log("=== E. PRESERVATION CHECKS (TYPHOON EYE & CLOUD SHADOWS) ===");
                var tyWorld = CreateCat5TyphoonWorld(baseEarth, 228.068, 127.2, 19.2, 4.15);
                InstallWorldSync(app, tyWorld);
                map = appType.GetField("map", Private).GetValue(app) as FlatMapRenderer;
                globe = appType.GetField("globe", Private).GetValue(app) as FlatGlobePresenter;
                nav = app.Navigation;
                atmo = Array.Find(camera.GetComponents<OrbisAtmosphereRenderer>(), a => a.enabled && a.view == globe.View);
                PublishSkyExact(map, tyWorld, ClimateCloudQaScenarios.DefaultVisualClock, 1024, tyWorld.Day + 0.25);

                const double tyLon = 127.42, tyLat = 19.32;
                Vector3 tyNormLocal = SurfaceNormalLocal(tyLon, tyLat);
                double tySunDay = FindSunDayForElevation(map.Sky, tyNormLocal, 228.0, 38f, afternoon: true);
                nav.Home(tySunDay); nav.Tick(3f, tySunDay);
                Quaternion tySpin = Quaternion.AngleAxis(nav.DisplaySpin * Mathf.Rad2Deg, Vector3.up);
                Vector3 tyNormW = (tySpin * ReflectZ(tyNormLocal)).normalized;
                Vector3 tyEyeW = (tySpin * ReflectZ(SurfaceNormalLocal(127.2, 19.2))).normalized;
                Vector3 tyHoriz = Vector3.ProjectOnPlane(tyEyeW - tyNormW, tyNormW).normalized;
                Vector3 tyLook = (tyHoriz * Mathf.Cos(2f * Mathf.Deg2Rad) + tyNormW * Mathf.Sin(2f * Mathf.Deg2Rad)).normalized;
                SetAltitudePose(tyWorld, tyLon, tyLat, 8800f, tyLook, tySunDay, tySunDay, 1, false, godRays: true, msOn: true);
                CapturePng("preserve/01_typhoon_eyewall_q1.png", captureMrt: true);
                SaveMrtSet("typhoon_eyewall_q1");
            }
            finally
            {
                if (mrtHook) UnityEngine.Object.DestroyImmediate(mrtHook);
                camera.targetTexture = oldTarget;
                nav.SetViewport(oldRect);
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(readTex);
                UnityEngine.Object.DestroyImmediate(read1);
                EditorSettings.asyncShaderCompilation = savedEditorAsync;
                ShaderUtil.allowAsyncCompilation = savedAsync;
                File.WriteAllLines(Path.Combine(outDir, "optics_separation_report.txt"), log, new UTF8Encoding(false));
            }
        }
    }
}
