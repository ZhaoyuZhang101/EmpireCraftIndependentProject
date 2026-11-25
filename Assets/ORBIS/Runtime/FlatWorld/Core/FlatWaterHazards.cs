using System;
using System.Globalization;

namespace Orbis.Flat
{
    public enum FlatHazardKind : byte
    {
        None = 0,
        RainFlood = 1,
        TyphoonSurge = 2,
        Tsunami = 3
    }

    public enum FlatHazardPhase : byte
    {
        Idle = 0,
        Advancing = 1,
        Peak = 2,
        Receding = 3,
        Complete = 4
    }

    public readonly struct FlatHazardMetrics
    {
        public readonly FlatHazardKind Kind;
        public readonly FlatHazardPhase Phase;
        public readonly bool LiveWeatherDriven;
        public readonly int PatchX, PatchY, SpanTiles, FocusTile, Resolution;
        public readonly double CellMeters, SimSeconds, DurationSeconds;
        public readonly int InundatedDryCells, PeakInundatedDryCells;
        public readonly double InundatedAreaKm2, MaxFloodDepthMeters, MaxSurfaceAnomalyMeters, MaxSpeedMs;
        public readonly double MeanInundatedBedMeters, MeanDryAdjacentBedMeters;
        public readonly double InitialVolumeM3, CurrentVolumeM3, ExpectedVolumeM3, RelativeMassError;
        public readonly double RainInputVolumeM3, ChannelInflowVolumeM3;
        public readonly double BoundaryInflowVolumeM3, BoundaryOutflowVolumeM3, InfiltrationVolumeM3;
        public readonly double SeabedUpliftVolumeM3, SeabedSubsidenceVolumeM3;

        public FlatHazardMetrics(
            FlatHazardKind kind, FlatHazardPhase phase, bool liveWeatherDriven,
            int patchX, int patchY, int spanTiles, int focusTile, int resolution,
            double cellMeters, double simSeconds, double durationSeconds,
            int inundatedDryCells, int peakInundatedDryCells,
            double inundatedAreaKm2, double maxFloodDepthMeters, double maxSurfaceAnomalyMeters, double maxSpeedMs,
            double meanInundatedBedMeters, double meanDryAdjacentBedMeters,
            double initialVolumeM3, double currentVolumeM3, double expectedVolumeM3, double relativeMassError,
            double rainInputVolumeM3, double channelInflowVolumeM3,
            double boundaryInflowVolumeM3, double boundaryOutflowVolumeM3, double infiltrationVolumeM3,
            double seabedUpliftVolumeM3, double seabedSubsidenceVolumeM3)
        {
            Kind = kind; Phase = phase; LiveWeatherDriven = liveWeatherDriven;
            PatchX = patchX; PatchY = patchY; SpanTiles = spanTiles; FocusTile = focusTile; Resolution = resolution;
            CellMeters = cellMeters; SimSeconds = simSeconds; DurationSeconds = durationSeconds;
            InundatedDryCells = inundatedDryCells; PeakInundatedDryCells = peakInundatedDryCells;
            InundatedAreaKm2 = inundatedAreaKm2; MaxFloodDepthMeters = maxFloodDepthMeters;
            MaxSurfaceAnomalyMeters = maxSurfaceAnomalyMeters; MaxSpeedMs = maxSpeedMs;
            MeanInundatedBedMeters = meanInundatedBedMeters; MeanDryAdjacentBedMeters = meanDryAdjacentBedMeters;
            InitialVolumeM3 = initialVolumeM3; CurrentVolumeM3 = currentVolumeM3;
            ExpectedVolumeM3 = expectedVolumeM3; RelativeMassError = relativeMassError;
            RainInputVolumeM3 = rainInputVolumeM3; ChannelInflowVolumeM3 = channelInflowVolumeM3;
            BoundaryInflowVolumeM3 = boundaryInflowVolumeM3; BoundaryOutflowVolumeM3 = boundaryOutflowVolumeM3;
            InfiltrationVolumeM3 = infiltrationVolumeM3;
            SeabedUpliftVolumeM3 = seabedUpliftVolumeM3; SeabedSubsidenceVolumeM3 = seabedSubsidenceVolumeM3;
        }

        public static string KindLabel(FlatHazardKind kind)
        {
            switch (kind)
            {
                case FlatHazardKind.RainFlood: return "降雨汇流/漫堤洪水";
                case FlatHazardKind.TyphoonSurge: return "台风风暴潮兼雨洪";
                case FlatHazardKind.Tsunami: return "海床位移长波海啸";
                default: return "局部平静水面";
            }
        }

        public static string PhaseLabel(FlatHazardPhase phase)
        {
            switch (phase)
            {
                case FlatHazardPhase.Advancing: return "推进";
                case FlatHazardPhase.Peak: return "洪峰/爬高";
                case FlatHazardPhase.Receding: return "退水";
                case FlatHazardPhase.Complete: return "已退水稳定";
                default: return "待命";
            }
        }

        public string FormatStatus()
        {
            var inv = CultureInfo.InvariantCulture;
            string sourceTag = LiveWeatherDriven ? "实时天气触发" : "受控情景";
            string baseLine = string.Format(inv,
                "{0}（{1} · {2} {3:F0}/{4:F0}s） 锚定 [{5},{6}] {7}×{7}\n" +
                "淹没干地 {8} 格（峰值 {9}，{10:F2} km²）· 最大淹深 {11:F2} m · 流速 {12:F1} m/s\n" +
                "水量账：初 {13:E2} + 雨 {14:E2} + 河入 {15:E2} + 边入 {16:E2} − 边出 {17:E2} − 渗 {18:E2} m³",
                KindLabel(Kind), sourceTag, PhaseLabel(Phase), SimSeconds, DurationSeconds,
                PatchX, PatchY, SpanTiles,
                InundatedDryCells, PeakInundatedDryCells, InundatedAreaKm2, MaxFloodDepthMeters, MaxSpeedMs,
                InitialVolumeM3, RainInputVolumeM3, ChannelInflowVolumeM3,
                BoundaryInflowVolumeM3, BoundaryOutflowVolumeM3, InfiltrationVolumeM3);
            if (Kind == FlatHazardKind.Tsunami)
                baseLine += string.Format(inv, "\n海床错动体积：抬升 {0:E2} / 沉降 {1:E2} m³（不凭空造水）",
                    SeabedUpliftVolumeM3, SeabedSubsidenceVolumeM3);
            baseLine += string.Format(inv, " · 守恒相对误差 {0:E2}", RelativeMassError);
            return baseLine;
        }
    }

    // Immutable local-terrain & forcing driver around FlatShallowWater.
    // Built on the main thread from WorldData; stepped safely on a background worker thread.
    public sealed class FlatWaterHazardSession
    {
        public const int DefaultResolution = 64;
        public const int DefaultSpanTiles = 16;
        public const double DefaultCellMeters = 120.0;
        /// <summary>
        /// Hydrodynamic SWE seconds advanced per 1.0 simulated game calendar day (<c>150.0 s</c> per 6-hour synoptic tick)
        /// for <see cref="LiveWeatherDriven"/> hazards so cyclone track, 6h surface precipitation, flood peak, and recession
        /// evolve in lockstep along the shared game calendar across pause and 1x..20x speed multipliers.
        /// </summary>
        public const double LiveSweSecondsPerCalendarDay = 600.0;

        public readonly FlatHazardKind Kind;
        public readonly bool LiveWeatherDriven;
        public readonly int WorldWidth, WorldHeight;
        public readonly int PatchX, PatchY, SpanTiles, FocusTile;
        public readonly int Resolution;
        public readonly double CellMeters, DurationSeconds;
        public readonly FlatShallowWater Solver;

        readonly WorldData boundWorld;
        readonly FlatMoistureCloudBudget fallbackPrecipBudget;
        readonly bool[] isMarineCell, isRiverCell, isCatchmentSlope;
        readonly int[] upstreamEntryCells;
        readonly double[] tsunamiTargetDeltaBed;
        readonly double[] landRetention;
        readonly double seawardDirX, seawardDirY;
        readonly int hemisphereSpin;
        readonly float baseRainScale, baseRiverDischargeScale;

        double appliedRuptureFraction;
        int peakInundatedDryCells;
        double currentForcingStrength;
        double peakForcingStrength;
        bool hadSignificantStormForcing;
        double lastActiveForcingSimTime;

        public double FocusU => FlatScrollMath.Wrap(((FocusTile % WorldWidth) + 0.5) / WorldWidth, 1.0);
        public double FocusV => FMath.Clamp((float)(((FocusTile / WorldWidth) + 0.5) / WorldHeight), 0.001f, 0.999f);
        public double LastCalendarDay { get; private set; } = double.NaN;
        public long LastPrecipitationTick { get; private set; } = long.MinValue;
        public float LastSurfacePrecipKgM2Per6h { get; private set; }
        public bool LastPrecipitationFromPublishedSnapshot { get; private set; }

        FlatWaterHazardSession(
            WorldData world,
            FlatHazardKind kind, bool liveWeatherDriven,
            int worldWidth, int worldHeight, int patchX, int patchY, int spanTiles, int focusTile,
            int resolution, double cellMeters, double durationSeconds,
            FlatShallowWater solver,
            bool[] marine, bool[] river, bool[] catchmentSlope, int[] upstreamEntries,
            double[] tsunamiDeltaBed, double[] retention,
            double seaDirX, double seaDirY, int hemiSpin,
            float rainScale, float dischargeScale)
        {
            boundWorld = world;
            fallbackPrecipBudget = liveWeatherDriven && kind != FlatHazardKind.Tsunami ? new FlatMoistureCloudBudget() : null;
            Kind = kind;
            LiveWeatherDriven = liveWeatherDriven;
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            PatchX = patchX;
            PatchY = patchY;
            SpanTiles = spanTiles;
            FocusTile = focusTile;
            Resolution = resolution;
            CellMeters = cellMeters;
            DurationSeconds = durationSeconds;
            Solver = solver;
            isMarineCell = marine;
            isRiverCell = river;
            isCatchmentSlope = catchmentSlope;
            upstreamEntryCells = upstreamEntries;
            tsunamiTargetDeltaBed = tsunamiDeltaBed;
            landRetention = retention;
            seawardDirX = seaDirX;
            seawardDirY = seaDirY;
            hemisphereSpin = hemiSpin;
            baseRainScale = rainScale;
            baseRiverDischargeScale = dischargeScale;
        }

        public float ShaderTurbidity =>
            Kind == FlatHazardKind.RainFlood ? 0.78f :
            Kind == FlatHazardKind.TyphoonSurge ? 0.48f : 0.24f;

        public FlatHazardPhase CurrentPhase
        {
            get
            {
                double t = Solver.Elapsed;
                if (LiveWeatherDriven && Kind != FlatHazardKind.Tsunami)
                {
                    const double unforcedMaxSeconds = 28800.0;
                    if (t >= unforcedMaxSeconds && currentForcingStrength < 0.03)
                        return FlatHazardPhase.Complete;
                    int curDry = CountInundatedDryCells();
                    if (hadSignificantStormForcing && currentForcingStrength < 0.03)
                    {
                        if (t >= 120.0 && (curDry <= 2 || (t - lastActiveForcingSimTime) >= 2400.0))
                            return FlatHazardPhase.Complete;
                        return FlatHazardPhase.Receding;
                    }
                    if (!hadSignificantStormForcing && currentForcingStrength <= 0.02)
                        return t <= 1e-6 ? FlatHazardPhase.Idle : FlatHazardPhase.Advancing;
                    if (currentForcingStrength >= 0.42 || (peakInundatedDryCells >= 12 && curDry >= (int)(peakInundatedDryCells * 0.85)))
                        return FlatHazardPhase.Peak;
                    return currentForcingStrength < peakForcingStrength * 0.65 ? FlatHazardPhase.Receding : FlatHazardPhase.Advancing;
                }
                if (t <= 1e-6) return FlatHazardPhase.Advancing;
                if (t >= DurationSeconds) return FlatHazardPhase.Complete;
                double u = t / DurationSeconds;
                if (Kind == FlatHazardKind.Tsunami)
                    return u < 0.28 ? FlatHazardPhase.Advancing : u < 0.48 ? FlatHazardPhase.Peak : FlatHazardPhase.Receding;
                return u < 0.26 ? FlatHazardPhase.Advancing : u < 0.46 ? FlatHazardPhase.Peak : FlatHazardPhase.Receding;
            }
        }

        public static (int patchX, int patchY, int focusTile) FindBestSite(
            WorldData world, FlatHazardKind kind, int preferredTile = -1, int spanTiles = DefaultSpanTiles)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.Facts == null) FlatTerrainAnalysis.Build(world, default, false);

            int w = world.Width, h = world.Height;
            int half = spanTiles / 2;

            if (preferredTile >= 0 && preferredTile < world.Count)
            {
                int px = preferredTile % w, py = preferredTile / w;
                if (kind != FlatHazardKind.RainFlood &&
                    !FlatTerrainAnalysis.Ocean(world.Water[preferredTile]) &&
                    world.CoastDistance[preferredTile] <= 2 &&
                    world.Elevation[preferredTile] <= 95 &&
                    py >= 2 && py < h - 2)
                {
                    int seaNear = 0;
                    for (int sy = -2; sy <= 2; sy++)
                    for (int sx = -2; sx <= 2; sx++)
                    {
                        int ky = py + sy;
                        if (ky >= 0 && ky < h && FlatTerrainAnalysis.Ocean(world.Water[world.Index(px + sx, ky)]))
                            seaNear++;
                    }
                    if (seaNear >= 2)
                    {
                        int x0 = FMath.Wrap(px - half, w);
                        int y0 = FMath.Clamp(py - half, 0, Math.Max(0, h - spanTiles));
                        return (x0, y0, preferredTile);
                    }
                }

                int localRadius = Math.Max(1, Math.Min(3, w / 256));
                int bestLocalTile = -1;
                double bestLocalScore = -1.0;
                for (int dy = -localRadius; dy <= localRadius; dy++)
                for (int dx = -localRadius; dx <= localRadius; dx++)
                {
                    int ty = py + dy;
                    if (ty < 2 || ty >= h - 2) continue;
                    int tile = world.Index(px + dx, ty);
                    double s = ScoreTile(world, tile, kind, -1) - 0.08 * (dx * dx + dy * dy);
                    if (s > bestLocalScore) { bestLocalScore = s; bestLocalTile = tile; }
                }
                if (bestLocalTile >= 0 && bestLocalScore > 0.25)
                {
                    int x0 = FMath.Wrap((bestLocalTile % w) - half, w);
                    int y0 = FMath.Clamp((bestLocalTile / w) - half, 0, Math.Max(0, h - spanTiles));
                    return (x0, y0, bestLocalTile);
                }

                // If caller explicitly targeted a rugged coastline (e.g. coarse-DEM Fujian ria coast) that missed
                // the global plain heuristic, anchor to the lowest coastal tile in that immediate neighborhood
                // rather than jumping thousands of kilometres to a different continent.
                if (kind != FlatHazardKind.RainFlood)
                {
                    int fallbackRadius = Math.Max(2, localRadius);
                    int fallbackCoast = -1;
                    double fallbackScore = -1e9;
                    for (int dy = -fallbackRadius; dy <= fallbackRadius; dy++)
                    for (int dx = -fallbackRadius; dx <= fallbackRadius; dx++)
                    {
                        int ty = py + dy;
                        if (ty < 2 || ty >= h - 2) continue;
                        int tile = world.Index(px + dx, ty);
                        if (FlatTerrainAnalysis.Ocean(world.Water[tile]) || world.CoastDistance[tile] > 2) continue;
                        int seaNb = 0;
                        for (int sy = -2; sy <= 2; sy++)
                        for (int sx = -2; sx <= 2; sx++)
                        {
                            int ky = ty + sy;
                            if (ky >= 0 && ky < h && FlatTerrainAnalysis.Ocean(world.Water[world.Index(px + dx + sx, ky)]))
                                seaNb++;
                        }
                        if (seaNb < 2) continue;
                        double s = 10.0 - Math.Max(0, (int)world.Elevation[tile]) * 0.015 + seaNb * 0.25 - 0.35 * (dx * dx + dy * dy);
                        if (s > fallbackScore) { fallbackScore = s; fallbackCoast = tile; }
                    }
                    if (fallbackCoast >= 0)
                    {
                        int x0 = FMath.Wrap((fallbackCoast % w) - half, w);
                        int y0 = FMath.Clamp((fallbackCoast / w) - half, 0, Math.Max(0, h - spanTiles));
                        return (x0, y0, fallbackCoast);
                    }
                }
            }

            int avoidTile = -1;
            if (kind == FlatHazardKind.Tsunami)
            {
                // Keep automatic tsunami and typhoon sites geographically distinct when possible.
                avoidTile = FindBestTileGlobal(world, FlatHazardKind.TyphoonSurge, -1);
            }
            int chosen = FindBestTileGlobal(world, kind, avoidTile);
            if (chosen < 0) chosen = world.Index(w / 2, h / 2);
            int startX = FMath.Wrap((chosen % w) - half, w);
            int startY = FMath.Clamp((chosen / w) - half, 0, Math.Max(0, h - spanTiles));
            return (startX, startY, chosen);
        }

        static int FindBestTileGlobal(WorldData world, FlatHazardKind kind, int avoidTile)
        {
            int bestTile = -1;
            double bestScore = -1.0;
            int w = world.Width, h = world.Height;
            int marginY = Math.Min(8, h / 8);
            for (int y = marginY; y < h - marginY; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                double score = ScoreTile(world, i, kind, avoidTile);
                if (score > bestScore) { bestScore = score; bestTile = i; }
            }
            return bestTile;
        }

        static double ScoreTile(WorldData world, int tile, FlatHazardKind kind, int avoidTile)
        {
            var facts = world.Facts;
            int w = world.Width, h = world.Height;
            int tx = tile % w, ty = tile / w;
            double latAbs = Math.Abs(world.Latitude(tile));
            if (latAbs > 66.0) return -1.0;

            if (kind == FlatHazardKind.RainFlood)
            {
                if (world.Water[tile] != WaterKind.River || world.CoastDistance[tile] < 2) return -1.0;
                int lowPlain = 0, riverCount = 0, oceanCount = 0;
                for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int ny = ty + dy;
                    if (ny < 0 || ny >= h) continue;
                    int n = world.Index(tx + dx, ny);
                    if (FlatTerrainAnalysis.Ocean(world.Water[n])) oceanCount++;
                    else if (world.Water[n] == WaterKind.River) riverCount++;
                    else if (world.Water[n] == WaterKind.Land && facts.Hand[n] <= 65f && world.Elevation[n] < 900) lowPlain++;
                }
                if (oceanCount > 0 || lowPlain < 18 || riverCount < 4) return -1.0;
                double runoffScore = Math.Min(4.0, Math.Sqrt(Math.Max(0, world.Runoff[tile])) * 0.28);
                double plainScore = Math.Min(3.0, lowPlain / 18.0);
                double gentleBonus = 1.5 / (1.0 + facts.Gradient[tile] / 120.0);
                return 1.0 + runoffScore + plainScore + gentleBonus;
            }

            // Coastal hazards: TyphoonSurge & Tsunami
            if (world.CoastDistance[tile] > 2 || FlatTerrainAnalysis.Ocean(world.Water[tile]) || world.Elevation[tile] > 95) return -1.0;
            int seaCells = 0, landPlain = 0, nearLowCoast = 0, riverMouth = 0;
            int deepestOffshore = 0;
            for (int dy = -5; dy <= 5; dy++)
            for (int dx = -5; dx <= 5; dx++)
            {
                int ny = ty + dy;
                if (ny < 0 || ny >= h) continue;
                int n = world.Index(tx + dx, ny);
                if (FlatTerrainAnalysis.Ocean(world.Water[n]))
                {
                    seaCells++;
                    if (-world.Elevation[n] > deepestOffshore) deepestOffshore = -world.Elevation[n];
                }
                else
                {
                    if (world.Water[n] == WaterKind.River) riverMouth++;
                    if (world.Elevation[n] <= 140 && facts.Hand[n] <= 80f) landPlain++;
                    if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2 && world.Elevation[n] <= 65 && facts.Hand[n] <= 42f) nearLowCoast++;
                }
            }
            if (seaCells < 22 || landPlain < 18 || nearLowCoast < 3) return -1.0;

            if (kind == FlatHazardKind.TyphoonSurge)
            {
                double tropicWeight = latAbs >= 7.0 && latAbs <= 42.0 ? 2.2 : 0.6;
                double mouthBonus = Math.Min(2.8, riverMouth * 0.45);
                double plainBonus = Math.Min(3.2, (landPlain / 20.0) + nearLowCoast * 0.14) * (1.2 / (1.0 + Math.Max(0, (int)world.Elevation[tile]) / 55.0));
                double liveStormBonus = 0.0;
                if (world.Cyclones != null)
                {
                    foreach (bool north in new[] { true, false })
                    {
                        var tr = world.Cyclones.Sample(north, world.Day);
                        if (!tr.Tracked || tr.WindSpeed < 16.0) continue;
                        double dx = FlatScrollMath.Shortest(tx / (double)w - tr.U, 1.0) * w;
                        double dy = ty / (double)h - tr.V;
                        double distTiles = Math.Sqrt(dx * dx + (dy * h) * (dy * h));
                        if (distTiles < 24.0) liveStormBonus = Math.Max(liveStormBonus, 4.0 * (1.0 - distTiles / 24.0));
                    }
                }
                return 0.5 + tropicWeight + mouthBonus + plainBonus + liveStormBonus;
            }
            else
            {
                // Tsunami: exposed ocean margin with deep offshore water & coastal plain for visible runup.
                if (deepestOffshore < 80) return 0.3;
                double depthScore = Math.Min(3.5, deepestOffshore / 450.0);
                double plainBonus = Math.Min(3.6, (landPlain / 20.0) + nearLowCoast * 0.18) * (1.3 / (1.0 + Math.Max(0, (int)world.Elevation[tile]) / 50.0));
                double seismicBonus = 0.0;
                if (world.Tectonics != null && world.Tectonics.Seismicity != null && tile < world.Tectonics.Seismicity.Length)
                {
                    seismicBonus = world.Tectonics.Seismicity[tile] * 2.2;
                    if (world.Tectonics.Setting[tile] == TectonicSetting.Subduction ||
                        world.Tectonics.Setting[tile] == TectonicSetting.IslandArc)
                        seismicBonus += 1.6;
                }
                double separation = 1.0;
                if (avoidTile >= 0)
                {
                    int ax = avoidTile % w, ay = avoidTile / w;
                    int ddx = FlatTerrainAnalysis.WrappedDX(tx, ax, w), ddy = ty - ay;
                    double dist = Math.Sqrt(ddx * ddx + ddy * ddy);
                    if (dist < 22.0) separation = 0.25 + 0.75 * (dist / 22.0);
                }
                return (0.8 + depthScore + plainBonus + seismicBonus) * separation;
            }
        }

        // Checks whether real-time world weather/cyclones warrant auto-starting a local hazard.
        // Tsunami is NEVER triggered by weather; it is strictly independent.
        public static bool TryDetectLiveWeatherHazard(
            WorldData world, float viewCenterX, float viewCenterY, bool viewIsFlat,
            out FlatHazardKind kind, out int targetTile)
        {
            kind = FlatHazardKind.None;
            targetTile = -1;
            if (world == null || world.Facts == null) return false;

            // 1. Landfalling tropical cyclone -> TyphoonSurge (prefer continental mainland coasts over tiny offshore islands when in range)
            if (world.Cyclones != null)
            {
                bool[] mainlandMask = null;
                foreach (bool north in new[] { true, false })
                {
                    var track = world.Cyclones.Sample(north, world.Day);
                    if (!track.Tracked || track.WindSpeed < 24.0 || track.PressureDeficit < 16.0) continue;
                    int cx = FMath.Wrap((int)Math.Floor(track.U * world.Width), world.Width);
                    int cy = FMath.Clamp((int)Math.Floor(track.V * world.Height), 0, world.Height - 1);
                    int bestMainlandCoast = -1, bestMainlandDistSq = int.MaxValue;
                    int bestAnyCoast = -1, bestAnyDistSq = int.MaxValue;
                    int searchRadius = Math.Max(4, world.Width / 64);
                    for (int dy = -searchRadius; dy <= searchRadius; dy++)
                    for (int dx = -searchRadius; dx <= searchRadius; dx++)
                    {
                        int ny = cy + dy;
                        if (ny < 2 || ny >= world.Height - 2) continue;
                        int tile = world.Index(cx + dx, ny);
                        if (world.CoastDistance[tile] == 1 && !FlatTerrainAnalysis.Ocean(world.Water[tile]) && world.Elevation[tile] <= 180)
                        {
                            if (mainlandMask == null) mainlandMask = FlatStormScenarios.BuildContinentalMainlandMask(world);
                            int d2 = dx * dx + dy * dy;
                            if (mainlandMask[tile])
                            {
                                if (d2 < bestMainlandDistSq) { bestMainlandDistSq = d2; bestMainlandCoast = tile; }
                            }
                            if (d2 < bestAnyDistSq) { bestAnyDistSq = d2; bestAnyCoast = tile; }
                        }
                    }
                    int chosenCoast = bestMainlandCoast >= 0 ? bestMainlandCoast : bestAnyCoast;
                    if (chosenCoast >= 0)
                    {
                        kind = FlatHazardKind.TyphoonSurge;
                        targetTile = chosenCoast;
                        return true;
                    }
                }
            }

            // 2. Extreme wet-season river flood stage near flat camera view -> RainFlood
            if (viewIsFlat && world.Facts.RiverDischarge != null)
            {
                int vx = FMath.Wrap((int)Math.Floor(viewCenterX), world.Width);
                int vy = FMath.Clamp((int)Math.Floor(viewCenterY), 0, world.Height - 1);
                var year = FlatClimateYear.At(FMath.Seed(world.Seed), world.Day);
                int month = world.Month;
                int bestRiver = -1;
                float bestSeverity = 0f;
                for (int dy = -6; dy <= 6; dy++)
                for (int dx = -6; dx <= 6; dx++)
                {
                    int ny = vy + dy;
                    if (ny < 2 || ny >= world.Height - 2) continue;
                    int tile = world.Index(vx + dx, ny);
                    if (world.Water[tile] != WaterKind.River || world.CoastDistance[tile] < 2) continue;
                    float stage = world.Facts.RiverDischarge.StageRatio(tile, world.Day);
                    year.AtTile(tile, world.Width, world.Height, out _, out float rainAnom);
                    float rainMm = world.Precipitation[tile * 12 + month] * (1f + rainAnom);
                    if (stage >= 1.65f && rainMm >= 210f)
                    {
                        float sev = stage * (rainMm / 200f);
                        if (sev > bestSeverity) { bestSeverity = sev; bestRiver = tile; }
                    }
                }
                if (bestRiver >= 0)
                {
                    kind = FlatHazardKind.RainFlood;
                    targetTile = bestRiver;
                    return true;
                }
            }
            return false;
        }

        public static FlatWaterHazardSession Create(
            WorldData world, FlatHazardKind kind, int preferredTile = -1, bool liveWeatherDriven = false,
            bool applyInitialForcing = true, double initialCalendarDay = double.NaN, double initialSurfaceRainKgM2Per6h = -1.0)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (kind == FlatHazardKind.None) throw new ArgumentException("Specify a non-empty hazard kind.", nameof(kind));
            if (world.Facts == null) FlatTerrainAnalysis.Build(world, default, false);

            var (patchX, patchY, focusTile) = FindBestSite(world, kind, preferredTile, DefaultSpanTiles);
            return CreateAtPatch(world, kind, patchX, patchY, focusTile, liveWeatherDriven, applyInitialForcing, initialCalendarDay, initialSurfaceRainKgM2Per6h);
        }

        public static FlatWaterHazardSession CreateAtPatch(
            WorldData world, FlatHazardKind kind, int patchX, int patchY, int focusTile = -1,
            bool liveWeatherDriven = false, bool applyInitialForcing = true,
            double initialCalendarDay = double.NaN, double initialSurfaceRainKgM2Per6h = -1.0)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (world.Facts == null) FlatTerrainAnalysis.Build(world, default, false);

            int res = DefaultResolution, span = DefaultSpanTiles;
            int count = res * res;
            patchX = FMath.Wrap(patchX, world.Width);
            patchY = FMath.Clamp(patchY, 0, Math.Max(0, world.Height - span));
            if (focusTile < 0) focusTile = world.Index(patchX + span / 2, patchY + span / 2);

            var elevations = new float[world.Count];
            for (int i = 0; i < world.Count; i++) elevations[i] = world.Elevation[i];

            bool hasOcean = false;
            float minRiverWaterLevel = float.MaxValue, maxRiverWaterLevel = float.MinValue;
            float maxRunoff = 10f, meanMonthlyRain = 120f;
            int sampleTiles = 0;
            int month = world.Month;
            for (int ty = 0; ty < span; ty++)
            for (int tx = 0; tx < span; tx++)
            {
                int tile = world.Index(patchX + tx, patchY + ty);
                if (FlatTerrainAnalysis.Ocean(world.Water[tile])) hasOcean = true;
                if (world.Water[tile] == WaterKind.River)
                {
                    if (world.WaterLevel[tile] < minRiverWaterLevel) minRiverWaterLevel = world.WaterLevel[tile];
                    if (world.WaterLevel[tile] > maxRiverWaterLevel) maxRiverWaterLevel = world.WaterLevel[tile];
                    if (world.Runoff[tile] > maxRunoff) maxRunoff = world.Runoff[tile];
                }
                meanMonthlyRain += world.Precipitation[tile * 12 + month];
                sampleTiles++;
            }
            meanMonthlyRain /= Math.Max(1, sampleTiles);
            if (minRiverWaterLevel == float.MaxValue) { minRiverWaterLevel = 0f; maxRiverWaterLevel = 10f; }

            var bed = new double[count];
            var initDepth = new double[count];
            var marine = new bool[count];
            var river = new bool[count];
            var catchment = new bool[count];
            var retention = new double[count];

            double seaSumX = 0, seaSumY = 0;
            int seaCount = 0;
            double landSumX = 0, landSumY = 0;
            int landCount = 0;
            var survey = world.GeographicSurface;
            bool useSurvey = survey != null && survey.Prepared(world);
            double cellsPerKm = world.Width / 40030.2;

            for (int sy = 0; sy < res; sy++)
            for (int sx = 0; sx < res; sx++)
            {
                int idx = sy * res + sx;
                float wx = patchX + (sx + 0.5f) * span / res;
                float wy = patchY + (sy + 0.5f) * span / res;
                int ix = (int)Math.Floor(wx), iy = FMath.Clamp((int)Math.Floor(wy), 0, world.Height - 1);
                int tile = world.Index(ix, iy);
                float sd = FlatSurfaceSampling.SampleShore(world, wx, wy);
                bool surveyed = false;
                WaterKind basinKind = world.Water[tile];
                if (useSurvey)
                {
                    var point = new FlatSeaPoint(wx * 360.0 / world.Width - 180.0, wy * 180.0 / world.Height - 90.0);
                    var surveyedKind = survey.At(world, point, out double signedKm, out surveyed);
                    if (surveyed)
                    {
                        basinKind = surveyedKind;
                        sd = (float)(signedKm * cellsPerKm);
                    }
                }
                float rawElev = FlatSurfaceSampling.Sample(elevations, world.Width, world.Height, wx, wy);
                float rawHand = FlatSurfaceSampling.Sample(world.Facts.Hand, world.Width, world.Height, wx, wy);
                float cov = FlatWaterGeometry.Coverage(world, wx, wy, out float geomDepth);
                bool areaBasin = surveyed ? FlatTerrainAnalysis.AreaWater(basinKind) : sd > 0.02f;
                // The coarse basin coverage can include surveyed land. Only a
                // resolved river can supply an open channel on those subcells.
                bool isChannel = !areaBasin && (surveyed
                    ? FlatWaterGeometry.RiverDistance(world, tile, wx - ix, wy - iy) < 0f
                    : cov > 0.50f);
                bool surveyedOcean = surveyed && FlatTerrainAnalysis.Ocean(basinKind);
                GroundMaterial mat = world.Facts.Materials[tile];
                retention[idx] = mat == GroundMaterial.Sand ? 1.35 : mat == GroundMaterial.Gravel ? 1.15 : mat == GroundMaterial.Rock ? 0.15 : 0.65;

                if (surveyedOcean || (hasOcean && (kind != FlatHazardKind.RainFlood || FlatTerrainAnalysis.Ocean(basinKind) || sd > -1.5f)))
                {
                    WaterKind nearestBasinKind = FindNearestAreaWaterKind(world, ix, iy, out short nearestBasinLevel);
                    bool nearPerchedLake = nearestBasinKind == WaterKind.Lake && nearestBasinLevel > 0;
                    double perchedStage = nearPerchedLake ? LocalLandHeight(nearestBasinLevel, 0.0, 0.0) : 0.0;

                    if (areaBasin)
                    {
                        if (surveyed ? surveyedOcean : !nearPerchedLake)
                        {
                            marine[idx] = true;
                            seaSumX += sx; seaSumY += sy; seaCount++;
                            double rawSea = Math.Max(2.0, -rawElev);
                            double capDepth = kind == FlatHazardKind.Tsunami ? 210.0 : 72.0;
                            double deepWeight = Smooth01((sd - 0.20) / 2.6);
                            double d = Math.Min(capDepth, 0.35 + 3.2 * sd + (kind == FlatHazardKind.Tsunami ? 0.075 : 0.032) * rawSea * deepWeight);
                            bed[idx] = -d;
                            initDepth[idx] = d;
                        }
                        else
                        {
                            double d = FMath.Clamp(geomDepth, 0.6f, 14.0f);
                            bed[idx] = perchedStage - d;
                            initDepth[idx] = d;
                        }
                    }
                    else
                    {
                        landSumX += sx; landSumY += sy; landCount++;
                        double inland = Math.Max(0.0, -sd);
                        double landH = LocalCoastalLandHeight(rawElev, rawHand, inland, liveWeatherDriven && kind == FlatHazardKind.TyphoonSurge);
                        if (nearPerchedLake && sd > -1.25f)
                            landH = Math.Max(landH, perchedStage + 0.20 + inland * 0.25);
                        if (isChannel)
                        {
                            river[idx] = true;
                            double chanD = Math.Max(1.6, Math.Min(5.2, geomDepth * 0.85 + 1.1));
                            if (!nearPerchedLake && landH <= 2.2)
                            {
                                // Estuarine tidal reach in hydrostatic balance with sea level.
                                bed[idx] = -chanD;
                                initDepth[idx] = chanD;
                            }
                            else
                            {
                                bed[idx] = Math.Max(0.20, landH - chanD * 0.55);
                                initDepth[idx] = 0.0;
                            }
                        }
                        else
                        {
                            bed[idx] = landH;
                            initDepth[idx] = 0.0;
                            if (landH >= 2.2 && landH <= 18.0) catchment[idx] = true;
                        }
                    }
                }
                else
                {
                    // Inland valley / floodplain patch (normal river stage at datum 0.0 m).
                    double valleySpan = Math.Max(12.0, maxRiverWaterLevel - minRiverWaterLevel);
                    double alongDrop = Math.Max(0.0, (rawElev - minRiverWaterLevel) / valleySpan);
                    double posHand = Math.Max(0.0, rawHand);
                    if (areaBasin && basinKind == WaterKind.Lake)
                    {
                        double d = FMath.Clamp(geomDepth, 1.2f, 16.0f);
                        bed[idx] = -d;
                        initDepth[idx] = d;
                    }
                    else if (isChannel)
                    {
                        river[idx] = true;
                        double chanD = Math.Max(1.8, Math.Min(5.4, geomDepth * 0.90 + 1.25));
                        bed[idx] = -chanD;
                        initDepth[idx] = chanD;
                    }
                    else
                    {
                        double lateralDist = NearestRiverSubcellDistance(world, wx, wy);
                        double floodplain = 0.42 + 1.95 * (posHand / (posHand + 36.0)) + Math.Min(1.8, lateralDist * 0.22);
                        double hillWall = posHand > 48.0 ? Math.Min(38.0, (posHand - 48.0) * 0.095) : 0.0;
                        double gentleValleyGrade = Math.Min(1.1, alongDrop * 0.42);
                        double landH = floodplain + hillWall + gentleValleyGrade;
                        bed[idx] = landH;
                        initDepth[idx] = 0.0;
                        if (landH >= 1.4 && landH <= 16.0) catchment[idx] = true;
                    }
                }
            }

            // Ensure RainFlood always has a coherent river channel & outlet if curves were sparse on tiny test maps.
            EnsureChannelConnectivity(res, bed, initDepth, river, marine, kind);

            // Enforce exact discrete well-balanced consistency across connected wet basins and adjacent dry banks.
            EnforceHydrostaticPatchConsistency(res, bed, initDepth, marine);

            double dirX = 1.0, dirY = 0.0;
            if (seaCount > 0 && landCount > 0)
            {
                double dx = seaSumX / seaCount - landSumX / landCount;
                double dy = seaSumY / seaCount - landSumY / landCount;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 1e-4) { dirX = dx / len; dirY = dy / len; }
            }

            double roughness = kind == FlatHazardKind.Tsunami ? 0.020 : 0.030;
            double bgDrag = kind == FlatHazardKind.Tsunami ? 0.0006 : 0.0018;
            var solver = new FlatShallowWater(res, res, DefaultCellMeters, bed, initDepth, roughness, bgDrag);

            // Configure open Flather boundaries on ocean perimeter & downstream river outlets.
            var upstreamEntries = ConfigureBoundariesAndFindInlets(world, patchX, patchY, span, res, solver, marine, river, dirX, dirY);

            // Precompute coseismic fault seabed displacement field for Tsunami (zero applied at t=0 if unforced).
            var tsunamiDelta = kind == FlatHazardKind.Tsunami
                ? BuildTsunamiFaultDisplacement(res, solver, marine, dirX, dirY, seaCount > 0 ? seaSumX / seaCount : res * 0.5, seaCount > 0 ? seaSumY / seaCount : res * 0.5)
                : null;

            double duration = kind == FlatHazardKind.Tsunami ? 480.0 : 600.0;
            int spin = world.Latitude(focusTile) >= 0f ? 1 : -1;
            float rainScale = FMath.Clamp(meanMonthlyRain / 140f, 0.75f, 1.85f);
            float dischargeScale = FMath.Clamp((float)Math.Sqrt(maxRunoff / 35.0), 0.75f, 2.1f);

            var session = new FlatWaterHazardSession(
                world, kind, liveWeatherDriven, world.Width, world.Height,
                patchX, patchY, span, focusTile, res, DefaultCellMeters, duration,
                solver, marine, river, catchment, upstreamEntries, tsunamiDelta, retention,
                dirX, dirY, spin, rainScale, dischargeScale);

            if (applyInitialForcing)
            {
                double startDay = FlatSkyCalendar.ResolveDay(initialCalendarDay, world.Day);
                FlatCycloneTrack initialCyclone = default;
                FlatCycloneWeather initialStorms = null;
                if (liveWeatherDriven && world.Cyclones != null)
                {
                    initialStorms = CaptureCycloneSnapshotForDay(world, startDay);
                    initialCyclone = SampleRelevantCycloneFromState(initialStorms, world, focusTile, startDay);
                }
                double sfcRain6h = initialSurfaceRainKgM2Per6h;
                if (liveWeatherDriven && kind != FlatHazardKind.Tsunami)
                {
                    session.LastCalendarDay = startDay;
                    session.LastPrecipitationTick = FlatCycloneWeather.StepAt(startDay);
                    if (sfcRain6h >= 0.0)
                    {
                        session.LastPrecipitationFromPublishedSnapshot = true;
                        session.LastSurfacePrecipKgM2Per6h = (float)sfcRain6h;
                    }
                    else if (session.fallbackPrecipBudget != null)
                    {
                        session.fallbackPrecipBudget.Solve(world, startDay, initialStorms);
                        sfcRain6h = session.fallbackPrecipBudget.SamplePrecipitation(session.FocusU, session.FocusV).SurfacePrecipKgM2Per6h;
                        session.LastPrecipitationFromPublishedSnapshot = false;
                        session.LastSurfacePrecipKgM2Per6h = (float)sfcRain6h;
                    }
                }
                session.UpdateForcing(0.0, initialCyclone, sfcRain6h);
            }
            return session;
        }

        /// <summary>
        /// Captures an immutable, thread-safe snapshot of <paramref name="world"/>'s cyclone state at <paramref name="calendarDay"/>
        /// on the main thread (handles both uncommitted <see cref="FlatSolarClock.PendingDays"/> and calendar rewinds without mutating <paramref name="world"/>).
        /// </summary>
        public static FlatCycloneWeather CaptureCycloneSnapshotForDay(WorldData world, double calendarDay)
        {
            if (world?.Cyclones == null) return null;
            long targetStep = FlatCycloneWeather.StepAt(calendarDay);
            if (targetStep < world.Cyclones.StepIndex - 1)
                return FlatCycloneWeather.SpinUpAt(world, calendarDay);
            var clone = world.Cyclones.Clone();
            if (targetStep > clone.StepIndex)
                clone.AdvanceTo(world, calendarDay);
            return clone;
        }

        public static FlatCycloneTrack SampleRelevantCycloneFromState(FlatCycloneWeather state, WorldData world, int focusTile, double calendarDay)
        {
            if (state == null || world == null || focusTile < 0 || focusTile >= world.Count) return default;
            bool north = world.Latitude(focusTile) >= 0f;
            var primary = state.Sample(north, calendarDay);
            if (primary.Tracked) return primary;
            return state.Sample(!north, calendarDay);
        }

        static WaterKind FindNearestAreaWaterKind(WorldData world, int tx, int ty, out short level)
        {
            int center = world.Index(tx, ty);
            if (center >= 0 && FlatTerrainAnalysis.AreaWater(world.Water[center]))
            {
                level = world.WaterLevel[center];
                return world.Water[center];
            }
            int bestDist = int.MaxValue;
            WaterKind bestKind = WaterKind.ShallowOcean;
            short bestLevel = 0;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int ny = ty + dy;
                if (ny < 0 || ny >= world.Height) continue;
                int n = world.Index(tx + dx, ny);
                if (!FlatTerrainAnalysis.AreaWater(world.Water[n])) continue;
                int d2 = dx * dx + dy * dy;
                if (d2 < bestDist || (d2 == bestDist && FlatTerrainAnalysis.Ocean(world.Water[n])))
                {
                    bestDist = d2;
                    bestKind = world.Water[n];
                    bestLevel = world.WaterLevel[n];
                }
            }
            level = bestLevel;
            return bestKind;
        }

        static double NearestRiverSubcellDistance(WorldData world, float wx, float wy)
        {
            var curves = world.Facts?.RiverCurves;
            if (curves != null && curves.SegmentCount > 0)
                return Math.Max(0.0, curves.Sample(wx, wy).Distance);
            int ix = (int)Math.Floor(wx), iy = FMath.Clamp((int)Math.Floor(wy), 0, world.Height - 1);
            int tile = world.Index(ix, iy);
            return Math.Max(0.0, FlatWaterGeometry.RiverDistance(world, tile, wx - ix, wy - iy));
        }

        static double LocalLandHeight(double posElev, double posHand, double inland)
        {
            double relief = 0.60 * posElev + 0.40 * posHand;
            double plain = 2.15 * (relief / (relief + 44.0));
            double hill = relief > 58.0 ? Math.Min(42.0, (relief - 58.0) * 0.088) : 0.0;
            return 0.16 + plain + hill + Math.Min(2.2, inland * 0.32);
        }

        static double LocalCoastalLandHeight(float rawElev, float rawHand, double inland, bool riaForeshoreTaper = false)
        {
            double posElev = Math.Max(0.0, rawElev);
            double posHand = Math.Max(0.0, rawHand);
            double relief = 0.62 * posElev + 0.38 * posHand;
            double plain = 2.05 * (relief / (relief + 46.0));
            double hill = relief > 65.0 ? Math.Min(42.0, (relief - 65.0) * 0.090) : 0.0;
            if (riaForeshoreTaper && relief > 65.0)
            {
                double foreshore = Smooth01(inland / 0.65);
                plain *= 0.22 + 0.78 * foreshore;
                hill *= foreshore * foreshore;
            }
            double shoreRise = Math.Min(2.6, inland * (0.26 + 0.34 * (relief / (relief + 40.0))));
            return 0.15 + plain + hill + shoreRise;
        }

        static void EnsureChannelConnectivity(int res, double[] bed, double[] initDepth, bool[] river, bool[] marine, FlatHazardKind kind)
        {
            if (kind != FlatHazardKind.RainFlood) return;
            int riverCells = 0;
            for (int i = 0; i < river.Length; i++) if (river[i]) riverCells++;
            if (riverCells >= 24) return;

            // Carve a natural meandering thalweg along the lowest terrain trough if sub-tile river width was narrow.
            for (int sy = 0; sy < res; sy++)
            {
                int bestX = res / 2;
                double minB = double.MaxValue;
                int searchLo = Math.Max(4, res / 4), searchHi = Math.Min(res - 5, 3 * res / 4);
                for (int sx = searchLo; sx <= searchHi; sx++)
                {
                    double b = bed[sy * res + sx];
                    if (b < minB) { minB = b; bestX = sx; }
                }
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = FMath.Clamp(bestX + dx, 1, res - 2);
                    int idx = sy * res + cx;
                    if (marine[idx]) continue;
                    river[idx] = true;
                    double d = dx == 0 ? 2.6 : 1.9;
                    bed[idx] = -d;
                    initDepth[idx] = d;
                }
            }
        }

        static void EnforceHydrostaticPatchConsistency(int res, double[] bed, double[] initDepth, bool[] marine)
        {
            int count = res * res;
            var visited = new bool[count];
            var queue = new int[count];
            var component = new int[count];

            for (int start = 0; start < count; start++)
            {
                if (visited[start] || initDepth[start] <= 0.0) continue;
                int head = 0, tail = 0, compLen = 0;
                bool touchesDatumZero = false;
                bool anyMarine = false;
                double sumEta = 0.0;

                visited[start] = true;
                queue[tail++] = start;
                while (head < tail)
                {
                    int cur = queue[head++];
                    component[compLen++] = cur;
                    double eta = bed[cur] + initDepth[cur];
                    sumEta += eta;
                    if (marine[cur]) anyMarine = true;
                    if (Math.Abs(eta) <= 1e-5) touchesDatumZero = true;

                    int cx = cur % res, cy = cur / res;
                    if (cx > 0) TryEnqueue(cur - 1);
                    if (cx + 1 < res) TryEnqueue(cur + 1);
                    if (cy > 0) TryEnqueue(cur - res);
                    if (cy + 1 < res) TryEnqueue(cur + res);
                }

                double compEta = (anyMarine || touchesDatumZero) ? 0.0 : sumEta / compLen;
                for (int k = 0; k < compLen; k++)
                {
                    int c = component[k];
                    bed[c] = compEta - initDepth[c];
                }

                void TryEnqueue(int nb)
                {
                    if (!visited[nb] && initDepth[nb] > 0.0)
                    {
                        visited[nb] = true;
                        queue[tail++] = nb;
                    }
                }
            }

            // Ensure every initially dry bank subcell sits strictly above any adjacent resting water surface.
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                int i = y * res + x;
                if (initDepth[i] > 0.0) continue;
                if (x > 0 && initDepth[i - 1] > 0.0) bed[i] = Math.Max(bed[i], bed[i - 1] + initDepth[i - 1] + 0.14);
                if (x + 1 < res && initDepth[i + 1] > 0.0) bed[i] = Math.Max(bed[i], bed[i + 1] + initDepth[i + 1] + 0.14);
                if (y > 0 && initDepth[i - res] > 0.0) bed[i] = Math.Max(bed[i], bed[i - res] + initDepth[i - res] + 0.14);
                if (y + 1 < res && initDepth[i + res] > 0.0) bed[i] = Math.Max(bed[i], bed[i + res] + initDepth[i + res] + 0.14);
            }
        }

        static int[] ConfigureBoundariesAndFindInlets(
            WorldData world, int patchX, int patchY, int span, int res,
            FlatShallowWater solver, bool[] marine, bool[] river, double seaDirX, double seaDirY)
        {
            var riverPerimeter = new System.Collections.Generic.List<(int cell, float elev)>();
            for (int i = 0; i < solver.Count; i++)
            {
                int x = i % res, y = i / res;
                bool perim = x == 0 || x == res - 1 || y == 0 || y == res - 1;
                if (!perim) continue;
                if (marine[i])
                {
                    solver.SetOpenBoundary(i, true, solver.RestSurface[i]);
                }
                else if (river[i])
                {
                    float wx = patchX + (x + 0.5f) * span / res;
                    float wy = patchY + (y + 0.5f) * span / res;
                    int tile = world.Index((int)Math.Floor(wx), FMath.Clamp((int)Math.Floor(wy), 0, world.Height - 1));
                    riverPerimeter.Add((i, world.WaterLevel[tile]));
                }
            }

            var inlets = new System.Collections.Generic.List<int>();
            if (riverPerimeter.Count > 0)
            {
                float minE = float.MaxValue, maxE = float.MinValue;
                foreach (var rp in riverPerimeter)
                {
                    if (rp.elev < minE) minE = rp.elev;
                    if (rp.elev > maxE) maxE = rp.elev;
                }
                float midE = 0.5f * (minE + maxE);
                foreach (var rp in riverPerimeter)
                {
                    // Lower-elevation perimeter river crossings act as open downstream outlets; higher ones are upstream flood inlets.
                    if (rp.elev <= midE + 0.5f && (maxE > minE + 0.5f || ((rp.cell % res - res * 0.5) * seaDirX + (rp.cell / res - res * 0.5) * seaDirY > 0)))
                    {
                        solver.SetOpenBoundary(rp.cell, true, solver.RestSurface[rp.cell]);
                        // Also open immediate low floodplain shoulder next to the outlet so overbank floodwater exits cleanly.
                        int rx = rp.cell % res, ry = rp.cell / res;
                        for (int d = -6; d <= 6; d++)
                        {
                            int nx = FMath.Clamp(rx + (ry == 0 || ry == res - 1 ? d : 0), 0, res - 1);
                            int ny = FMath.Clamp(ry + (rx == 0 || rx == res - 1 ? d : 0), 0, res - 1);
                            int nb = ny * res + nx;
                            if (!river[nb] && solver.InitialDepth[nb] <= 0.0 && solver.Bed[nb] <= 2.4)
                                solver.SetOpenBoundary(nb, true, Math.Min(solver.RestSurface[rp.cell], solver.Bed[nb]));
                        }
                    }
                    else
                    {
                        inlets.Add(rp.cell);
                    }
                }
            }

            if (inlets.Count == 0)
            {
                // Interior upstream channel reach (furthest inland river cells).
                double bestInland = -1e9;
                int bestCell = -1;
                for (int i = 0; i < solver.Count; i++)
                {
                    if (!river[i]) continue;
                    int x = i % res, y = i / res;
                    if (x <= 1 || x >= res - 2 || y <= 1 || y >= res - 2) continue;
                    double inlandProj = -((x - res * 0.5) * seaDirX + (y - res * 0.5) * seaDirY);
                    if (inlandProj > bestInland) { bestInland = inlandProj; bestCell = i; }
                }
                if (bestCell >= 0)
                {
                    int bx = bestCell % res, by = bestCell / res;
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = FMath.Clamp(bx + dx, 1, res - 2), ny = FMath.Clamp(by + dy, 1, res - 2);
                        if (river[ny * res + nx]) inlets.Add(ny * res + nx);
                    }
                }
                // Guarantee at least one open downstream river boundary so flood recession drains out of the patch.
                bool anyOpen = false;
                for (int i = 0; i < solver.Count; i++)
                {
                    int x = i % res, y = i / res;
                    if ((x == 0 || x == res - 1 || y == 0 || y == res - 1) && (marine[i] || river[i]))
                    {
                        solver.SetOpenBoundary(i, true, solver.RestSurface[i]);
                        anyOpen = true;
                    }
                }
                if (!anyOpen)
                {
                    int lowestPerim = 0;
                    for (int i = 1; i < solver.Count; i++)
                    {
                        int x = i % res, y = i / res;
                        if ((x == 0 || x == res - 1 || y == 0 || y == res - 1) && solver.Bed[i] < solver.Bed[lowestPerim])
                            lowestPerim = i;
                    }
                    solver.SetOpenBoundary(lowestPerim, true, 0.0);
                }
            }
            return inlets.ToArray();
        }

        static double[] BuildTsunamiFaultDisplacement(
            int res, FlatShallowWater solver, bool[] marine,
            double seaDirX, double seaDirY, double seaCenterX, double seaCenterY)
        {
            var delta = new double[solver.Count];
            // Place submarine dip-slip fault axis in the deeper offshore third of the marine sector,
            // strike perpendicular to seawardDir (parallel to the coastline).
            double strikeX = -seaDirY, strikeY = seaDirX;
            double faultCenterX = FMath.Clamp((float)(seaCenterX + seaDirX * (res * 0.09)), res * 0.16f, res * 0.84f);
            double faultCenterY = FMath.Clamp((float)(seaCenterY + seaDirY * (res * 0.09)), res * 0.16f, res * 0.84f);
            double sigmaCross = res * 0.095;
            double sigmaAlong = res * 0.36;

            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                int i = y * res + x;
                if (!marine[i] || solver.InitialDepth[i] < 4.0) continue;
                // Taper fault displacement to zero near the outer radiation boundary so open boundaries remain clean.
                int edgeDist = Math.Min(Math.Min(x, res - 1 - x), Math.Min(y, res - 1 - y));
                double edgeMask = Smooth01((edgeDist - 2.0) / 4.5);
                if (edgeMask <= 0.0) continue;

                double rx = x - faultCenterX, ry = y - faultCenterY;
                double cross = rx * seaDirX + ry * seaDirY; // positive further offshore, negative shoreward
                double along = rx * strikeX + ry * strikeY;
                double alongEnv = Math.Exp(-0.5 * (along * along) / (sigmaAlong * sigmaAlong));
                double depthMask = Smooth01((solver.InitialDepth[i] - 4.5) / 12.0);

                // Bipolar megathrust seabed deformation: leading shoreward subsidence trough (-2.4 m)
                // followed by main coseismic uplift crest (+7.8 m) launching a long wave at c = sqrt(g*h).
                double uplift = 7.8 * Math.Exp(-0.5 * Math.Pow((cross - sigmaCross * 0.45) / sigmaCross, 2.0));
                double trough = -2.4 * Math.Exp(-0.5 * Math.Pow((cross + sigmaCross * 0.95) / (sigmaCross * 0.85), 2.0));
                delta[i] = (uplift + trough) * alongEnv * depthMask * edgeMask;
            }
            return delta;
        }

        public void Step(double dtSimSeconds, FlatCycloneTrack liveCyclone = default, int maxSubsteps = 24) =>
            Step(dtSimSeconds, liveCyclone, maxSubsteps, externalSurfaceRainKgM2Per6h: -1.0);

        /// <summary>
        /// Steps a hazard session synchronized to <paramref name="calendarDay"/>, injecting either the already-published
        /// 6-hour snapshot surface precipitation (<paramref name="publishedSurfaceRainKgM2Per6h"/> &gt;= 0) for the exact
        /// <c>(World, StepAt(calendarDay))</c>, or computing the matching 6-hour <see cref="FlatMoistureCloudBudget"/> surface
        /// precipitation on the worker thread using an immutable <paramref name="immutableStormsSnapshot"/>.
        /// </summary>
        public void StepWithCalendar(
            double dtSimSeconds, double calendarDay, FlatCycloneTrack liveCyclone,
            FlatCycloneWeather immutableStormsSnapshot = null,
            double publishedSurfaceRainKgM2Per6h = -1.0,
            int maxSubsteps = 24)
        {
            double sfcRain6h = publishedSurfaceRainKgM2Per6h;
            bool calendarDriven = LiveWeatherDriven && Kind != FlatHazardKind.Tsunami && !double.IsNaN(calendarDay);
            if (calendarDriven)
            {
                LastCalendarDay = calendarDay;
                LastPrecipitationTick = FlatCycloneWeather.StepAt(calendarDay);
                if (sfcRain6h >= 0.0)
                {
                    LastPrecipitationFromPublishedSnapshot = true;
                    LastSurfacePrecipKgM2Per6h = (float)sfcRain6h;
                }
                else if (fallbackPrecipBudget != null && boundWorld != null)
                {
                    fallbackPrecipBudget.Solve(boundWorld, calendarDay, immutableStormsSnapshot);
                    sfcRain6h = fallbackPrecipBudget.SamplePrecipitation(FocusU, FocusV).SurfacePrecipKgM2Per6h;
                    LastPrecipitationFromPublishedSnapshot = false;
                    LastSurfacePrecipKgM2Per6h = (float)sfcRain6h;
                }
            }
            Step(dtSimSeconds, liveCyclone, maxSubsteps, sfcRain6h, completeCalendarSlice: calendarDriven);
        }

        public void Step(double dtSimSeconds, FlatCycloneTrack liveCyclone, int maxSubsteps, double externalSurfaceRainKgM2Per6h) =>
            Step(dtSimSeconds, liveCyclone, maxSubsteps, externalSurfaceRainKgM2Per6h, completeCalendarSlice: false);

        void Step(double dtSimSeconds, FlatCycloneTrack liveCyclone, int maxSubsteps,
            double externalSurfaceRainKgM2Per6h, bool completeCalendarSlice)
        {
            if (dtSimSeconds <= 0) return;
            UpdateForcing(Solver.Elapsed + 0.5 * dtSimSeconds, liveCyclone, externalSurfaceRainKgM2Per6h);

            // Coseismic fault rupture occurs over the first 6 seconds of tsunami simulation time.
            if (Kind == FlatHazardKind.Tsunami && tsunamiTargetDeltaBed != null && appliedRuptureFraction < 1.0)
            {
                double nextFrac = Smooth01((Solver.Elapsed + dtSimSeconds) / 6.0);
                double dFrac = nextFrac - appliedRuptureFraction;
                if (dFrac > 0.0)
                {
                    for (int i = 0; i < Solver.Count; i++)
                        if (tsunamiTargetDeltaBed[i] != 0.0)
                            Solver.DisplaceBed(i, tsunamiTargetDeltaBed[i] * dFrac);
                    appliedRuptureFraction = nextFrac;
                }
            }

            // Live calendar slices must finish with the same forcing. Deep
            // water can exhaust a CFL substep batch before reaching the target.
            double remaining = dtSimSeconds;
            do
            {
                double before = Solver.Elapsed;
                Solver.Advance(remaining, maxSubsteps);
                peakInundatedDryCells = Math.Max(peakInundatedDryCells, CountInundatedDryCells());
                double advanced = Solver.Elapsed - before;
                remaining -= advanced;
                if (completeCalendarSlice && remaining > 1e-7 && advanced <= 0)
                    throw new InvalidOperationException("Live SWE solver did not advance its calendar slice.");
            }
            while (completeCalendarSlice && remaining > 1e-7);
        }

        public void UpdateForcing(double tSeconds, FlatCycloneTrack liveCyclone) =>
            UpdateForcing(tSeconds, liveCyclone, externalSurfaceRainKgM2Per6h: -1.0);

        public void UpdateForcing(double tSeconds, FlatCycloneTrack liveCyclone, double externalSurfaceRainKgM2Per6h)
        {
            Solver.ClearForcing();
            double u = DurationSeconds > 0 ? tSeconds / DurationSeconds : 1.0;

            if (LiveWeatherDriven && Kind != FlatHazardKind.Tsunami)
            {
                UpdateLiveWeatherForcing(liveCyclone, externalSurfaceRainKgM2Per6h);
                return;
            }

            if (Kind == FlatHazardKind.RainFlood)
            {
                // Rise & peak over u in [0, 0.40], then shut off rain/inflow so floodplain drains & recedes in [0.42, 1.0].
                double stormEnv = Math.Sin(Math.PI * FMath.Clamp((float)(u / 0.40), 0f, 1f));
                stormEnv *= stormEnv;
                if (stormEnv > 1e-4)
                {
                    double rainMs = 0.0017 * baseRainScale * stormEnv;
                    for (int i = 0; i < Solver.Count; i++)
                    {
                        if (isCatchmentSlope[i]) Solver.RainRate[i] = rainMs;
                        else if (!Solver.InitiallyWet[i] && Solver.Bed[i] < 3.8) Solver.RainRate[i] = rainMs * 0.35;
                    }
                    if (upstreamEntryCells.Length > 0)
                    {
                        double perCellInflowMs = (0.072 * baseRiverDischargeScale * stormEnv) / Math.Max(1, Math.Sqrt(upstreamEntryCells.Length));
                        foreach (int c in upstreamEntryCells)
                            Solver.ChannelInflowRate[c] = perCellInflowMs;
                    }
                }
                else if (u > 0.42)
                {
                    // Unsaturated soil infiltration assists gravity recession in floodplain swales.
                    double infBase = 0.00085;
                    for (int i = 0; i < Solver.Count; i++)
                        if (!Solver.InitiallyWet[i])
                            Solver.InfiltrationRate[i] = infBase * landRetention[i];
                }
                return;
            }

            if (Kind == FlatHazardKind.TyphoonSurge)
            {
                // Cyclone core translates onshore across the coastal shelf, peaks near u=0.35, then decays/departs for recession.
                double env = Math.Sin(Math.PI * FMath.Clamp((float)(u / 0.46), 0f, 1f));
                env = Math.Pow(Math.Max(0.0, env), 1.15);

                double maxDeficitHpa = 76.0;
                double maxWindMs = 52.0;
                if (LiveWeatherDriven && liveCyclone.Tracked && liveCyclone.WindSpeed > 10.0)
                {
                    maxDeficitHpa = Math.Max(28.0, liveCyclone.PressureDeficit);
                    maxWindMs = Math.Max(26.0, liveCyclone.WindSpeed);
                }

                double deficitNow = maxDeficitHpa * env;
                double windNow = maxWindMs * env;
                // Storm eye tracks from offshore toward the coast along -seawardDir.
                double progress = FMath.Clamp((float)(u / 0.50), 0f, 1f);
                double eyeOffset = (0.42 - 0.68 * progress) * Resolution;
                double eyeX = Resolution * 0.5 + seawardDirX * eyeOffset;
                double eyeY = Resolution * 0.5 + seawardDirY * eyeOffset;
                double coreCells = Resolution * 0.28;
                double onshoreX = -seawardDirX, onshoreY = -seawardDirY;

                // Open-ocean shelf setup on marine perimeter boundaries (inverse barometer + regional shelf wind setup).
                double shelfWindSetup = 2.65 * (windNow / 50.0) * (windNow / 50.0);
                for (int i = 0; i < Solver.Count; i++)
                {
                    int x = i % Resolution, y = i / Resolution;
                    double dx = x - eyeX, dy = y - eyeY;
                    double r = Math.Sqrt(dx * dx + dy * dy);
                    double rNorm = r / Math.Max(1.0, coreCells);
                    double localP = deficitNow * Math.Exp(-0.5 * rNorm * rNorm);
                    Solver.PressureHead[i] = FlatShallowWater.InverseBarometerMeters(localP);

                    // Holland-like cyclonic vortex + onshore shelf steering component.
                    double vTan = windNow * (2.0 * rNorm / (1.0 + rNorm * rNorm));
                    double invR = r > 1e-3 ? 1.0 / r : 0.0;
                    double radInX = -dx * invR, radInY = -dy * invR;
                    // NH (spin=+1): counter-clockwise around low; SH (spin=-1): clockwise.
                    double tanX = -radInY * hemisphereSpin, tanY = radInX * hemisphereSpin;
                    double wxMs = 0.52 * (tanX + 0.28 * radInX) * vTan + 0.62 * onshoreX * windNow;
                    double wyMs = 0.52 * (tanY + 0.28 * radInY) * vTan + 0.62 * onshoreY * windNow;
                    FlatShallowWater.ComputeWindStress(wxMs, wyMs, out double tx, out double ty);
                    // Amplify local momentum transfer across the 7.7 km coastal window to match mesoscale bay setup.
                    Solver.WindStressX[i] = tx * 6.5;
                    Solver.WindStressY[i] = ty * 6.5;

                    // Spiral eyewall & coastal rainband rainfall + river flood runoff ("风暴潮兼雨洪").
                    if (env > 1e-4)
                    {
                        double angle = Math.Atan2(dy, dx);
                        double spiral = 0.65 + 0.35 * Math.Cos(2.0 * angle - hemisphereSpin * rNorm * 2.4 + tSeconds * 0.03);
                        double eyewall = Math.Exp(-0.5 * Math.Pow((rNorm - 0.85) / 0.65, 2.0));
                        if (!isMarineCell[i] && Solver.Bed[i] < 16.0)
                            Solver.RainRate[i] = 0.0017 * baseRainScale * env * eyewall * spiral;
                    }
                    else if (u > 0.48 && !Solver.InitiallyWet[i])
                    {
                        Solver.InfiltrationRate[i] = 0.00070 * landRetention[i];
                    }

                    bool perim = x == 0 || x == Resolution - 1 || y == 0 || y == Resolution - 1;
                    if (perim && isMarineCell[i])
                    {
                        double bcSurge = Solver.PressureHead[i] + shelfWindSetup * Math.Exp(-0.25 * rNorm * rNorm);
                        Solver.SetOpenBoundary(i, true, Solver.RestSurface[i] + bcSurge);
                    }
                }

                if (env > 1e-4 && upstreamEntryCells.Length > 0)
                {
                    double chanRate = (0.060 * baseRiverDischargeScale * env) / Math.Max(1, Math.Sqrt(upstreamEntryCells.Length));
                    foreach (int c in upstreamEntryCells)
                        Solver.ChannelInflowRate[c] = chanRate;
                }
                return;
            }

            if (Kind == FlatHazardKind.Tsunami)
            {
                // Pure gravity-wave propagation after coseismic seabed displacement; gentle drainage infiltration late in backwash.
                if (u > 0.46)
                {
                    for (int i = 0; i < Solver.Count; i++)
                        if (!Solver.InitiallyWet[i])
                            Solver.InfiltrationRate[i] = 0.00055 * landRetention[i];
                }
            }
        }

        void UpdateLiveWeatherForcing(FlatCycloneTrack liveCyclone, double externalSurfaceRainKgM2Per6h)
        {
            // Convert coarse 64x32 (~620 km) 6h surface precipitation (kg/m^2/6h = mm/6h) above soil-infiltration
            // retention (1.35 mm/6h) into mesoscale rainband intensity (mm/h); light/fair background drizzle <= 1.35 mm/6h
            // produces strictly 0 flood rain so post-storm recession drains cleanly.
            bool hasPublishedSurfaceDiag = externalSurfaceRainKgM2Per6h >= 0.0;
            double synExcess6h = hasPublishedSurfaceDiag ? Math.Max(0.0, externalSurfaceRainKgM2Per6h - 1.35) : 0.0;
            double synMesoscaleRainMmH = synExcess6h > 0.0 ? Math.Min(36.0, 2.5 + synExcess6h * 3.6) : 0.0;
            bool pureVirgaDrySurface = hasPublishedSurfaceDiag && externalSurfaceRainKgM2Per6h <= 0.05;

            if (Kind == FlatHazardKind.RainFlood)
            {
                double uCenter = FlatScrollMath.Wrap((PatchX + SpanTiles * 0.5) / WorldWidth, 1.0);
                double vCenter = FMath.Clamp((float)((PatchY + SpanTiles * 0.5) / WorldHeight), 0.001f, 0.999f);
                var cycCenter = FlatCycloneWeather.SampleSurfaceWindFromTrack(in liveCyclone, hemisphereSpin, uCenter, vCenter);
                double cycRainMmH = pureVirgaDrySurface && cycCenter.RadiusKm > 180f ? 0.0 : cycCenter.RainRateMmPerHour;
                double rainMmPerHour = Math.Max(cycRainMmH, synMesoscaleRainMmH);
                double excessMmH = Math.Max(0.0, rainMmPerHour - 2.5);
                double stormStrength = FMath.Clamp((float)(excessMmH / 28.0), 0f, 1.5f);
                currentForcingStrength = stormStrength;
                if (stormStrength > peakForcingStrength) peakForcingStrength = stormStrength;
                if (stormStrength >= 0.12) hadSignificantStormForcing = true;
                if (stormStrength >= 0.03) lastActiveForcingSimTime = Solver.Elapsed;

                if (stormStrength > 1e-3)
                {
                    double rainMs = 0.0014 * baseRainScale * stormStrength;
                    for (int i = 0; i < Solver.Count; i++)
                    {
                        if (isCatchmentSlope[i]) Solver.RainRate[i] = rainMs;
                        else if (!Solver.InitiallyWet[i] && Solver.Bed[i] < 3.8) Solver.RainRate[i] = rainMs * 0.35;
                    }
                    if (upstreamEntryCells.Length > 0)
                    {
                        double perCellInflowMs = (0.064 * baseRiverDischargeScale * stormStrength) / Math.Max(1.0, Math.Sqrt(upstreamEntryCells.Length));
                        foreach (int c in upstreamEntryCells)
                            Solver.ChannelInflowRate[c] = perCellInflowMs;
                    }
                }
                else
                {
                    for (int i = 0; i < Solver.Count; i++)
                        if (!Solver.InitiallyWet[i] && Solver.Depth[i] > 0.0)
                            Solver.InfiltrationRate[i] = 0.00085 * landRetention[i];
                }
                return;
            }

            // LiveWeatherDriven TyphoonSurge:
            // Strictly driven by real cyclone position, tangential/inflow wind stress, inverse-barometer pressure deficit,
            // onshore continental-shelf wind setup, and same-world 6h surface precipitation + eyewall rainfall/runoff.
            // Zero storm & sub-threshold 6h surface rain => zero tide & zero water injection.
            double maxPatchWindMs = 0.0;
            double maxPatchDeficitHpa = 0.0;
            double sumLandRainMmH = 0.0;
            int landCells = 0;

            // Map the 64x64 coastal SWE domain onto a resolution-independent 96 km x 96 km physical shelf/bay window
            // centered on the anchored coastal FocusTile, so offshore cyclones 500+ km out at sea do not falsely
            // inject eyewall surge before approaching the continental shelf.
            const double baySpanKm = 96.0;
            const double shelfLengthOverGh = 112000.0 / (FlatShallowWater.Gravity * 15.5); // ~737 s^2/m
            double onshoreDirX = -seawardDirX, onshoreDirY = -seawardDirY;

            double uFocus = FocusU;
            double vFocus = FocusV;
            double cosFocus = Math.Max(0.15, Math.Cos((vFocus - 0.5) * Math.PI));
            double invLonKm = 1.0 / (40030.2 * cosFocus);
            const double invLatKm = 1.0 / 20015.1;
            var focusSample = FlatCycloneWeather.SampleSurfaceWindFromTrack(in liveCyclone, hemisphereSpin, uFocus, vFocus);

            for (int sy = 0; sy < Resolution; sy++)
            {
                double dyKm = ((sy + 0.5) / Resolution - 0.5) * baySpanKm;
                double vCell = FMath.Clamp((float)(vFocus + dyKm * invLatKm), 0.001f, 0.999f);
                int row = sy * Resolution;
                for (int sx = 0; sx < Resolution; sx++)
                {
                    int i = row + sx;
                    double dxKm = ((sx + 0.5) / Resolution - 0.5) * baySpanKm;
                    double uCell = FlatScrollMath.Wrap(uFocus + dxKm * invLonKm, 1.0);

                    var sample = FlatCycloneWeather.SampleSurfaceWindFromTrack(in liveCyclone, hemisphereSpin, uCell, vCell);
                    double cycRainMmH = pureVirgaDrySurface && sample.RadiusKm > 180f ? 0.0 : sample.RainRateMmPerHour;
                    double localRainMmH = cycRainMmH;
                    double vortexRainGate = sample.Active ? FMath.Clamp((float)((sample.TangentialSpeedMs - 9.0) / 14.0), 0f, 1f) : 0.0;
                    if (synMesoscaleRainMmH > 0.0 && (vortexRainGate > 0.0 || synExcess6h >= 9.5))
                        localRainMmH = Math.Max(localRainMmH, synMesoscaleRainMmH * Math.Max(vortexRainGate, synExcess6h >= 9.5 ? 1.0 : 0.0));

                    if (sample.Active)
                    {
                        if (sample.TangentialSpeedMs > maxPatchWindMs) maxPatchWindMs = sample.TangentialSpeedMs;
                        if (sample.PressureDeficitHpa > maxPatchDeficitHpa) maxPatchDeficitHpa = sample.PressureDeficitHpa;

                        Solver.PressureHead[i] = FlatShallowWater.InverseBarometerMeters(sample.PressureDeficitHpa);
                        FlatShallowWater.ComputeWindStress(sample.EastMs, sample.NorthMs, out double tauX, out double tauY);
                        double tauMag = Math.Sqrt(tauX * tauX + tauY * tauY);
                        // Direct onshore wind stress + Coriolis-Ekman 90-deg right (NH) / left (SH) coastal transport + wave radiation setup:
                        double tauDirectOnshore = tauX * onshoreDirX + tauY * onshoreDirY;
                        double tauEkmanOnshore = -hemisphereSpin * (tauY * seawardDirX - tauX * seawardDirY);
                        double tauOnshoreEff = Math.Max(0.0, tauDirectOnshore + 0.62 * tauEkmanOnshore) + 0.28 * tauMag;

                        Solver.WindStressX[i] = tauX * 5.5 + onshoreDirX * tauOnshoreEff * 4.8;
                        Solver.WindStressY[i] = tauY * 5.5 + onshoreDirY * tauOnshoreEff * 4.8;

                        bool perim = sx == 0 || sx == Resolution - 1 || sy == 0 || sy == Resolution - 1;
                        if (perim && isMarineCell[i])
                        {
                            double shelfGate = focusSample.Active
                                ? Math.Exp(-Math.Pow(Math.Max(0.0, focusSample.RadiusKm - 75.0) / 260.0, 2.0))
                                : 0.0;
                            double shelfSetup = Math.Min(3.40, tauOnshoreEff * shelfLengthOverGh * shelfGate);
                            double bcSurge = Solver.PressureHead[i] + shelfSetup;
                            Solver.SetOpenBoundary(i, true, Solver.RestSurface[i] + bcSurge);
                        }
                    }
                    else
                    {
                        bool perim = sx == 0 || sx == Resolution - 1 || sy == 0 || sy == Resolution - 1;
                        if (perim && isMarineCell[i])
                            Solver.SetOpenBoundary(i, true, Solver.RestSurface[i]);
                    }

                    if (!isMarineCell[i])
                    {
                        landCells++;
                        if (localRainMmH > 2.5 && Solver.Bed[i] < 16.0)
                        {
                            double rainStrength = Math.Min(1.6, (localRainMmH - 2.0) / 34.0);
                            Solver.RainRate[i] = 0.00125 * baseRainScale * rainStrength;
                            sumLandRainMmH += localRainMmH;
                        }
                        else if (!Solver.InitiallyWet[i] && Solver.Depth[i] > 0.0)
                        {
                            // Unsaturated coastal soil infiltration drains ponded surge/rain water once rainband departs.
                            Solver.InfiltrationRate[i] = 0.00080 * landRetention[i];
                        }
                    }
                }
            }

            double meanLandRainMmH = landCells > 0 ? sumLandRainMmH / landCells : 0.0;
            if (meanLandRainMmH > 1.5 && upstreamEntryCells.Length > 0)
            {
                double runoffStrength = Math.Min(1.6, meanLandRainMmH / 28.0);
                double chanRate = (0.052 * baseRiverDischargeScale * runoffStrength) / Math.Max(1.0, Math.Sqrt(upstreamEntryCells.Length));
                foreach (int c in upstreamEntryCells)
                    Solver.ChannelInflowRate[c] = chanRate;
            }

            double windNorm = maxPatchWindMs / 38.0;
            double dpNorm = maxPatchDeficitHpa / 36.0;
            double rainNorm = meanLandRainMmH / 26.0;
            currentForcingStrength = Math.Max(Math.Max(windNorm * windNorm, dpNorm), rainNorm);
            if (currentForcingStrength > peakForcingStrength) peakForcingStrength = currentForcingStrength;
            if (currentForcingStrength >= 0.12) hadSignificantStormForcing = true;
            if (currentForcingStrength >= 0.03) lastActiveForcingSimTime = Solver.Elapsed;
        }

        public int CountInundatedDryCells(double threshold = FlatShallowWater.VisibleFloodThreshold)
        {
            int n = 0;
            for (int i = 0; i < Solver.Count; i++)
                if (Solver.IsInundatedDryCell(i, threshold)) n++;
            return n;
        }

        public FlatHazardMetrics CaptureMetrics()
        {
            int inundated = 0, dryAdjacent = 0;
            double maxFloodDepth = 0.0, maxAnomaly = 0.0, maxSpeed = 0.0;
            double sumInundatedBed = 0.0, sumDryAdjBed = 0.0;

            for (int y = 0; y < Resolution; y++)
            for (int x = 0; x < Resolution; x++)
            {
                int i = y * Resolution + x;
                double excess = Solver.ExcessHead(i);
                if (Math.Abs(excess) > Math.Abs(maxAnomaly)) maxAnomaly = excess;
                if (Solver.IsWet(i))
                {
                    double sp = Math.Sqrt(Solver.VelocityX[i] * Solver.VelocityX[i] + Solver.VelocityY[i] * Solver.VelocityY[i]);
                    if (sp > maxSpeed) maxSpeed = sp;
                }
                if (!Solver.InitiallyWet[i])
                {
                    if (Solver.Depth[i] >= FlatShallowWater.VisibleFloodThreshold)
                    {
                        inundated++;
                        sumInundatedBed += Solver.Bed[i];
                        if (Solver.Depth[i] > maxFloodDepth) maxFloodDepth = Solver.Depth[i];
                    }
                    else
                    {
                        bool nearFlood =
                            (x > 0 && Solver.Depth[i - 1] >= FlatShallowWater.VisibleFloodThreshold) ||
                            (x + 1 < Resolution && Solver.Depth[i + 1] >= FlatShallowWater.VisibleFloodThreshold) ||
                            (y > 0 && Solver.Depth[i - Resolution] >= FlatShallowWater.VisibleFloodThreshold) ||
                            (y + 1 < Resolution && Solver.Depth[i + Resolution] >= FlatShallowWater.VisibleFloodThreshold);
                        if (nearFlood) { dryAdjacent++; sumDryAdjBed += Solver.Bed[i]; }
                    }
                }
            }
            if (inundated > peakInundatedDryCells) peakInundatedDryCells = inundated;
            double areaKm2 = inundated * CellMeters * CellMeters * 1e-6;
            double meanWetBed = inundated > 0 ? sumInundatedBed / inundated : 0.0;
            double meanDryBed = dryAdjacent > 0 ? sumDryAdjBed / dryAdjacent : meanWetBed;

            return new FlatHazardMetrics(
                Kind, CurrentPhase, LiveWeatherDriven,
                PatchX, PatchY, SpanTiles, FocusTile, Resolution,
                CellMeters, Solver.Elapsed, DurationSeconds,
                inundated, peakInundatedDryCells, areaKm2, maxFloodDepth, maxAnomaly, maxSpeed,
                meanWetBed, meanDryBed,
                Solver.InitialVolume, Solver.Volume(), Solver.ExpectedVolume, Solver.RelativeMassError(),
                Solver.RainInputVolume, Solver.ChannelInflowVolume,
                Solver.BoundaryInflowVolume, Solver.BoundaryOutflowVolume, Solver.InfiltrationVolume,
                Solver.SeabedUpliftVolume, Solver.SeabedSubsidenceVolume);
        }

        // Packs (deltaEta, velocityX, velocityY, foam) into RGBAHalf (ushort[Resolution * Resolution * 4]) on worker thread.
        public void PackHalfRgba(ushort[] targetHalf)
        {
            if (targetHalf == null || targetHalf.Length < Solver.Count * 4)
                throw new ArgumentException("Insufficient RGBAHalf buffer.", nameof(targetHalf));

            for (int y = 0; y < Resolution; y++)
            for (int x = 0; x < Resolution; x++)
            {
                int i = y * Resolution + x;
                double excess = Solver.ExcessHead(i);
                // Suppress sub-millimeter numerical noise on dry ground so rest land stays strictly 0.
                if (!Solver.InitiallyWet[i] && Solver.Depth[i] < 0.008) excess = 0.0;
                double ux = Solver.VelocityX[i], uy = Solver.VelocityY[i];
                double foam = Solver.Foam[i];

                // Emphasize crisp advancing wet-dry bore/flood front only where water actively advances onto dry ground.
                if (Solver.Depth[i] >= 0.04 && Solver.Depth[i] <= 1.4)
                {
                    double advance = 0.0;
                    if (x > 0 && Solver.Depth[i - 1] < 0.02 && -ux > advance) advance = -ux;
                    if (x + 1 < Resolution && Solver.Depth[i + 1] < 0.02 && ux > advance) advance = ux;
                    if (y > 0 && Solver.Depth[i - Resolution] < 0.02 && -uy > advance) advance = -uy;
                    if (y + 1 < Resolution && Solver.Depth[i + Resolution] < 0.02 && uy > advance) advance = uy;
                    if (advance > 0.22)
                        foam = Math.Min(1.0, foam + FMath.Clamp((float)((advance - 0.22) * 0.34), 0f, 0.68f));
                }

                int o = i * 4;
                targetHalf[o] = FloatToHalf((float)excess);
                targetHalf[o + 1] = FloatToHalf((float)ux);
                targetHalf[o + 2] = FloatToHalf((float)uy);
                targetHalf[o + 3] = FloatToHalf((float)foam);
            }
        }

        static double Smooth01(double x)
        {
            if (x <= 0.0) return 0.0;
            if (x >= 1.0) return 1.0;
            return x * x * (3.0 - 2.0 * x);
        }

        // Pure managed IEEE-754 binary32 -> binary16 conversion (worker-safe, zero engine dependencies).
        public static ushort FloatToHalf(float value)
        {
            uint bits = (uint)BitConverter.SingleToInt32Bits(value);
            uint sign = (bits >> 16) & 0x8000u;
            int exp = (int)((bits >> 23) & 0xffu) - 127 + 15;
            uint mant = bits & 0x7fffffu;
            if (exp <= 0)
            {
                if (exp < -10) return (ushort)sign;
                mant = (mant | 0x800000u) >> (1 - exp);
                return (ushort)(sign | ((mant + 0x1000u) >> 13));
            }
            if (exp >= 31)
                return (ushort)(sign | 0x7bffu);
            return (ushort)(sign | ((uint)exp << 10) | ((mant + 0x1000u) >> 13));
        }
    }
}
