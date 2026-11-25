using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace Orbis.Flat
{
    // Bounded 64x64 local shallow-water & hazard presentation controller.
    // Runs ~10 Hz background worker tasks with main-thread RGBAHalf texture upload.
    // Anchors active hazards to fixed world coordinates (camera motion never resets them)
    // and cleans up immediately across world switches or terrain rebuilds.
    public sealed class FlatLocalWater : MonoBehaviour
    {
        const int Resolution = FlatWaterHazardSession.DefaultResolution;
        const int Span = FlatWaterHazardSession.DefaultSpanTiles;
        const float SimSecondsPerRealSecond = 18.0f;

        WorldData world;
        int boundTerrainRevision = -1;
        Material material;
        Texture2D texture, previousTexture;
        bool useHalfFormat;
        float presentationBlend = 1f;
        bool published, hasPrevious;

        FlatWaterHazardSession activeSession;
        FlatShallowWater passiveSolver;
        Task<Frame> pending;
        int sessionEpoch;

        int startX = -1000, startY = -1000;
        float age, weatherCheckAge;
        double lastAutoTriggerDay = -1000.0;
        bool stopped, ripple;

        readonly ushort[] workerHalfBuffer = new ushort[Resolution * Resolution * 4];
        readonly float[] uploadFloatFallback = new float[Resolution * Resolution * 4];
        double clock;

        public bool AutoWeatherEvents { get; set; } = true;
        public bool HasActiveHazard => activeSession != null;
        public FlatHazardKind ActiveKind => activeSession != null ? activeSession.Kind : FlatHazardKind.None;
        public int ActiveFocusTile => activeSession != null ? activeSession.FocusTile : -1;
        public FlatHazardMetrics LastMetrics { get; private set; }
        public double LastWorkerStepMs { get; private set; }
        public double LastUploadMs { get; private set; }
        public string Status { get; private set; } = "局部 SWE 64×64：待命（可在地形页触发洪水/风暴潮/海啸，或等待实时台风登陆）";

        sealed class Frame
        {
            public int Epoch;
            public FlatWaterHazardSession HazardSession;
            public FlatShallowWater PassiveSolver;
            public FlatHazardMetrics Metrics;
            public ushort[] HalfRGBA;
            public int X, Y;
            public double MassError, WorkerMs;
        }

        public void Initialize(WorldData source, Material target)
        {
            sessionEpoch++;
            world = source;
            boundTerrainRevision = source != null ? source.TerrainRevision : -1;
            material = target;
            activeSession = null;
            passiveSolver = null;
            published = false;
            hasPrevious = false;
            stopped = false;
            lastAutoTriggerDay = -1000.0;

            useHalfFormat = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf);
            TextureFormat fmt = useHalfFormat ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat;
            if (!SystemInfo.SupportsTextureFormat(fmt))
                throw new NotSupportedException("Local SWE requires RGBAHalf or RGBAFloat texture support.");

            if (texture) Destroy(texture);
            if (previousTexture) Destroy(previousTexture);
            texture = new Texture2D(Resolution, Resolution, fmt, false, true)
            {
                name = "SWE local flood depth, velocity and foam",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            previousTexture = new Texture2D(Resolution, Resolution, fmt, false, true)
            {
                name = "SWE previous world patch",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            BindDisabledState();
        }

        void BindDisabledState()
        {
            if (!material) return;
            if (texture) material.SetTexture("_SWEState", texture);
            if (previousTexture) material.SetTexture("_SWEPreviousState", previousTexture);
            material.SetFloat("_SWEReady", 0f);
            material.SetFloat("_SWEPreviousReady", 0f);
            material.SetFloat("_SWEBlend", 1f);
            material.SetVector("_SWEParams", Vector4.zero);
        }

        public void RequestRipple()
        {
            ripple = true;
            stopped = false;
        }

        public int StartHazard(FlatHazardKind kind, int preferredTile = -1, bool liveWeather = false)
        {
            if (world == null || kind == FlatHazardKind.None) return -1;
            sessionEpoch++;
            stopped = false;
            boundTerrainRevision = world.TerrainRevision;
            activeSession = FlatWaterHazardSession.Create(world, kind, preferredTile, liveWeather, applyInitialForcing: true);
            passiveSolver = null;
            LastMetrics = activeSession.CaptureMetrics();
            startX = activeSession.PatchX;
            startY = activeSession.PatchY;
            published = false;
            hasPrevious = false;
            presentationBlend = 1f;
            age = 0.12f; // Trigger worker publication on the very next tick.

            // Upload clean initial t=0 state immediately so the anchored rect is coherent right away.
            var initialHalf = new ushort[Resolution * Resolution * 4];
            activeSession.PackHalfRgba(initialHalf);
            UploadHalfTexture(texture, initialHalf);
            published = true;
            if (material)
            {
                material.SetTexture("_SWEState", texture);
                material.SetVector("_SWERect", new Vector4(startX, startY, Span, Span));
                material.SetVector("_SWEParams", new Vector4((int)activeSession.Kind, activeSession.ShaderTurbidity, (float)activeSession.CellMeters, 1f));
                material.SetFloat("_SWEReady", 1f);
                material.SetFloat("_SWEPreviousReady", 0f);
                material.SetFloat("_SWEBlend", 1f);
            }
            Status = LastMetrics.FormatStatus();
            return activeSession.FocusTile;
        }

        public void ClearHazard()
        {
            sessionEpoch++;
            activeSession = null;
            passiveSolver = null;
            LastMetrics = default;
            published = false;
            hasPrevious = false;
            stopped = false;
            BindDisabledState();
            Status = "局部 SWE 已清理并恢复基准河湖海状态。";
        }

        // Synchronous stepping entry for deterministic PlayMode/Editor multi-frame QA & timing benchmarks.
        public FlatHazardMetrics StepHazardSynchronous(double simSeconds, double calendarDay = double.NaN)
        {
            if (world == null || activeSession == null) return default;
            long t0 = Stopwatch.GetTimestamp();
            FlatCycloneTrack track = SampleRelevantCyclone(world, activeSession.FocusTile, double.IsNaN(calendarDay) ? world.Day : calendarDay);
            if (simSeconds > 0)
                activeSession.Step(simSeconds, track, maxSubsteps: 48);
            var metrics = activeSession.CaptureMetrics();
            activeSession.PackHalfRgba(workerHalfBuffer);
            LastWorkerStepMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;

            long u0 = Stopwatch.GetTimestamp();
            UploadHalfTexture(texture, workerHalfBuffer);
            startX = activeSession.PatchX;
            startY = activeSession.PatchY;
            published = true;
            hasPrevious = false;
            presentationBlend = 1f;
            if (material)
            {
                material.SetTexture("_SWEState", texture);
                material.SetVector("_SWERect", new Vector4(startX, startY, Span, Span));
                material.SetVector("_SWEParams", new Vector4((int)activeSession.Kind, activeSession.ShaderTurbidity, (float)activeSession.CellMeters, 1f));
                material.SetFloat("_SWEReady", 1f);
                material.SetFloat("_SWEPreviousReady", 0f);
                material.SetFloat("_SWEBlend", 1f);
            }
            LastUploadMs = (Stopwatch.GetTimestamp() - u0) * 1000.0 / Stopwatch.Frequency;
            LastMetrics = metrics;
            Status = metrics.FormatStatus();
            return metrics;
        }

        void UploadHalfTexture(Texture2D target, ushort[] halfData)
        {
            if (useHalfFormat)
            {
                target.SetPixelData(halfData, 0);
            }
            else
            {
                for (int i = 0; i < halfData.Length; i++)
                    uploadFloatFallback[i] = Mathf.HalfToFloat(halfData[i]);
                target.SetPixelData(uploadFloatFallback, 0);
            }
            target.Apply(false, false);
        }

        static FlatCycloneTrack SampleRelevantCyclone(WorldData w, int focusTile, double day)
        {
            if (w?.Cyclones == null || focusTile < 0 || focusTile >= w.Count) return default;
            bool north = w.Latitude(focusTile) >= 0f;
            var primary = w.Cyclones.Sample(north, day);
            if (primary.Tracked) return primary;
            return w.Cyclones.Sample(!north, day);
        }

        public void Tick(float dt, float focusX, float focusY, bool flatWaterVisible, bool animate, double calendarDay = double.NaN)
        {
            if (!material || world == null) return;

            // Clean up immediately if the underlying world terrain was rebuilt or replaced.
            if (boundTerrainRevision != world.TerrainRevision)
            {
                boundTerrainRevision = world.TerrainRevision;
                ClearHazard();
            }

            if (animate) clock += Math.Min(0.1, dt);
            material.SetFloat("_WaterClock", (float)clock);
            presentationBlend = Mathf.Min(1f, presentationBlend + Mathf.Max(0f, dt) / 0.22f);

            // Poll completed worker frame; discard stale results if epoch changed mid-flight.
            if (pending != null && pending.IsCompleted)
            {
                var completed = pending;
                pending = null;
                try
                {
                    Frame f = completed.GetAwaiter().GetResult();
                    if (f.Epoch == sessionEpoch)
                    {
                        LastWorkerStepMs = f.WorkerMs;
                        long u0 = Stopwatch.GetTimestamp();
                        bool moved = !published || f.X != startX || f.Y != startY;
                        if (moved && f.HazardSession == null)
                        {
                            var swap = previousTexture; previousTexture = texture; texture = swap;
                            hasPrevious = published; presentationBlend = 0f;
                            material.SetTexture("_SWEState", texture);
                            material.SetTexture("_SWEPreviousState", previousTexture);
                            material.SetVector("_SWEPreviousRect", new Vector4(startX, startY, Span, Span));
                        }
                        UploadHalfTexture(texture, f.HalfRGBA);
                        startX = f.X; startY = f.Y; published = true;
                        material.SetTexture("_SWEState", texture);
                        material.SetVector("_SWERect", new Vector4(startX, startY, Span, Span));

                        if (f.HazardSession != null)
                        {
                            activeSession = f.HazardSession;
                            LastMetrics = f.Metrics;
                            material.SetVector("_SWEParams", new Vector4((int)activeSession.Kind, activeSession.ShaderTurbidity, (float)activeSession.CellMeters, 1f));
                            Status = f.Metrics.FormatStatus();
                            // Auto-retire live weather hazards once fully receded so future storms can anchor cleanly.
                            if (activeSession.LiveWeatherDriven && f.Metrics.Phase == FlatHazardPhase.Complete && f.Metrics.InundatedDryCells <= 2)
                            {
                                ClearHazard();
                            }
                        }
                        else
                        {
                            passiveSolver = f.PassiveSolver;
                            material.SetVector("_SWEParams", new Vector4(0f, 0f, 25f, 0f));
                            Status = "局部 SWE 64×64 / 10Hz；相对水量误差 " + f.MassError.ToString("E2") + "（可视波动，不改全球水量）";
                        }
                        LastUploadMs = (Stopwatch.GetTimestamp() - u0) * 1000.0 / Stopwatch.Frequency;
                    }
                }
                catch (Exception e)
                {
                    stopped = true;
                    Status = "局部 SWE 已安全降级为基准水面：" + e.Message;
                    UnityEngine.Debug.LogWarning(Status);
                }
            }

            // Optional live weather/cyclone auto-detection (never triggers Tsunami).
            if (activeSession == null && AutoWeatherEvents && animate && !stopped)
            {
                weatherCheckAge += dt;
                double nowDay = double.IsNaN(calendarDay) ? world.Day : calendarDay;
                if (weatherCheckAge >= 1.0f && Math.Abs(nowDay - lastAutoTriggerDay) >= 0.5)
                {
                    weatherCheckAge = 0f;
                    if (FlatWaterHazardSession.TryDetectLiveWeatherHazard(world, focusX, focusY, flatWaterVisible, out var liveKind, out int liveTile))
                    {
                        lastAutoTriggerDay = nowDay;
                        StartHazard(liveKind, liveTile, liveWeather: true);
                    }
                }
            }

            bool sw3Ready = material.GetFloat("_VendorWaterReady") > 0.5f;
            bool showPatch = !stopped && published && flatWaterVisible && (activeSession != null || !sw3Ready || ripple);
            material.SetFloat("_SWEReady", showPatch ? 1f : 0f);
            material.SetFloat("_SWEPreviousReady", showPatch && hasPrevious && activeSession == null ? 1f : 0f);
            material.SetFloat("_SWEBlend", presentationBlend);

            if (stopped) return;

            if (activeSession != null)
            {
                // Keep advancing or holding the anchored hazard regardless of camera pan!
                if (!flatWaterVisible)
                {
                    Status = LastMetrics.FormatStatus() + "\n（当前在星球视角；返回二维地表即可查看锚定灾区）";
                }
                if (pending != null || !animate) return;
                if (activeSession.CurrentPhase == FlatHazardPhase.Complete) return;
                age += dt;
                if (age < 0.095f) return;
                float realStep = Mathf.Min(0.14f, Mathf.Max(0.03f, age));
                age = 0f;
                double simStep = realStep * SimSecondsPerRealSecond;
                int capturedEpoch = sessionEpoch;
                var sessionRef = activeSession;
                double sampleDay = double.IsNaN(calendarDay) ? world.Day : calendarDay;
                FlatCycloneTrack cyclone = SampleRelevantCyclone(world, sessionRef.FocusTile, sampleDay);
                pending = Task.Run(() => SolveHazardStep(capturedEpoch, sessionRef, simStep, cyclone, workerHalfBuffer));
                return;
            }

            // Passive ripple mode (used when SW3 vendor water is absent or explicit ripple requested).
            if (!flatWaterVisible || (sw3Ready && !ripple && passiveSolver == null))
            {
                if (flatWaterVisible && sw3Ready)
                    Status = "局部 SWE 64×64：待命（可在「地形」页触发洪水 / 台风风暴潮 / 海啸，或开启实时天气触发）";
                else
                    Status = flatWaterVisible ? "局部 SWE 暂停" : "局部 SWE：星球视图待命";
                return;
            }

            if (pending != null || !animate || presentationBlend < 1f) return;
            int x = FMath.Wrap(Mathf.FloorToInt((focusX - Span * 0.5f) / 4f) * 4, world.Width);
            int y = Mathf.Clamp(Mathf.FloorToInt((focusY - Span * 0.5f) / 4f) * 4, 0, world.Height - Span);
            age += dt;
            bool changed = x != startX || y != startY || passiveSolver == null;
            if (age < 0.1f && !changed && !ripple) return;
            float step = Mathf.Min(0.12f, Mathf.Max(0.025f, age));
            age = 0f;
            bool disturbance = ripple;
            ripple = false;
            var current = changed ? null : passiveSolver;
            WorldData source = world;
            int passiveEpoch = sessionEpoch;
            pending = Task.Run(() => SolvePassiveStep(passiveEpoch, source, current, x, y, step, disturbance, workerHalfBuffer));
        }

        static Frame SolveHazardStep(int epoch, FlatWaterHazardSession session, double simDt, FlatCycloneTrack cyclone, ushort[] halfOut)
        {
            long t0 = Stopwatch.GetTimestamp();
            session.Step(simDt, cyclone, maxSubsteps: 24);
            var metrics = session.CaptureMetrics();
            session.PackHalfRgba(halfOut);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            return new Frame
            {
                Epoch = epoch,
                HazardSession = session,
                Metrics = metrics,
                HalfRGBA = halfOut,
                X = session.PatchX,
                Y = session.PatchY,
                MassError = metrics.RelativeMassError,
                WorkerMs = ms
            };
        }

        static Frame SolvePassiveStep(int epoch, WorldData world, FlatShallowWater current, int x0, int y0, float dt, bool ripple, ushort[] halfOut)
        {
            long t0 = Stopwatch.GetTimestamp();
            if (current == null)
            {
                var depths = new float[Resolution * Resolution];
                for (int y = 0; y < Resolution; y++)
                for (int x = 0; x < Resolution; x++)
                {
                    float mask = FlatWaterGeometry.Coverage(world, x0 + (x + 0.5f) * Span / Resolution, y0 + (y + 0.5f) * Span / Resolution, out float depth);
                    depths[y * Resolution + x] = mask > 0.52f ? FMath.Clamp(depth, 0.05f, 12f) : 0f;
                }
                current = new FlatShallowWater(Resolution, Resolution, 25.0, depths);
            }
            if (ripple)
            {
                int nearest = -1, best = int.MaxValue;
                for (int y = 0; y < Resolution; y++)
                for (int x = 0; x < Resolution; x++)
                {
                    int i = y * Resolution + x, d = (x - 32) * (x - 32) + (y - 32) * (y - 32);
                    if (current.IsWet(i) && d < best) { nearest = i; best = d; }
                }
                if (nearest >= 0) current.Ripple(nearest % Resolution, nearest / Resolution, 0.055);
            }
            current.Advance(dt);

            for (int i = 0; i < current.Count; i++)
            {
                int o = i * 4;
                halfOut[o] = FlatWaterHazardSession.FloatToHalf((float)current.ExcessHead(i));
                halfOut[o + 1] = FlatWaterHazardSession.FloatToHalf((float)current.VelocityX[i]);
                halfOut[o + 2] = FlatWaterHazardSession.FloatToHalf((float)current.VelocityY[i]);
                halfOut[o + 3] = FlatWaterHazardSession.FloatToHalf((float)current.Foam[i]);
            }
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            return new Frame
            {
                Epoch = epoch,
                PassiveSolver = current,
                HalfRGBA = halfOut,
                X = x0,
                Y = y0,
                MassError = current.RelativeMassError(),
                WorkerMs = ms
            };
        }

        void OnDisable()
        {
            if (material)
            {
                material.SetFloat("_SWEReady", 0f);
                material.SetFloat("_SWEPreviousReady", 0f);
            }
        }

        void OnDestroy()
        {
            sessionEpoch++;
            if (pending != null)
                pending.ContinueWith(t => { if (t.IsFaulted) { var _ = t.Exception; } }, TaskScheduler.Default);
            if (texture) Destroy(texture);
            if (previousTexture) Destroy(previousTexture);
        }
    }
}
