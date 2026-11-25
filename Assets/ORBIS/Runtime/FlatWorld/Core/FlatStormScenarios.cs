using System;
using System.Collections.Generic;
using System.Globalization;

namespace Orbis.Flat
{
    /// <summary>
    /// Proactively-launchable, reproducible Earth tropical-cyclone landfall disaster scenarios.
    /// </summary>
    public enum FlatStormScenario : byte
    {
        EastChinaSeaLandfall = 0,
        NorthAtlanticLandfall = 1
    }

    /// <summary>
    /// Single 6-hour trajectory sample from <see cref="FlatCycloneWeather.AdvanceTo"/> for scenario verification and HUD telemetry.
    /// </summary>
    public readonly struct FlatStormTrackPoint
    {
        public readonly double CalendarDay;
        public readonly double LongitudeDeg, LatitudeDeg;
        public readonly double WindSpeedMs, PressureDeficitHpa, OceanCoolingC;
        public readonly WaterKind SurfaceKind;
        public readonly bool IsOcean, IsContinentalMainland;
        public readonly double SignedShoreKm;
        public readonly int Tile;

        public FlatStormTrackPoint(
            double calendarDay, double lonDeg, double latDeg,
            double windSpeedMs, double pressureDeficitHpa, double oceanCoolingC,
            WaterKind surfaceKind, bool isOcean, bool isContinentalMainland, double signedShoreKm, int tile)
        {
            CalendarDay = calendarDay;
            LongitudeDeg = lonDeg;
            LatitudeDeg = latDeg;
            WindSpeedMs = windSpeedMs;
            PressureDeficitHpa = pressureDeficitHpa;
            OceanCoolingC = oceanCoolingC;
            SurfaceKind = surfaceKind;
            IsOcean = isOcean;
            IsContinentalMainland = isContinentalMainland;
            SignedShoreKm = signedShoreKm;
            Tile = tile;
        }
    }

    /// <summary>
    /// Verified initial state and natural forward-evolution summary for a reproducible Earth storm scenario.
    /// After installation at <see cref="StartDay"/>, evolution is driven 100% by unmodified <see cref="FlatCycloneWeather.AdvanceTo"/>.
    /// </summary>
    public readonly struct FlatStormScenarioPlan
    {
        public readonly FlatStormScenario Scenario;
        public readonly double StartDay;
        public readonly double ExpectedLandfallDay;
        public readonly double ExpectedDecayDay;
        public readonly FlatCycloneTrack InitialTrack;
        public readonly int StartOceanTile;
        public readonly int LandfallCoastalTile;
        public readonly double StartLonDeg, StartLatDeg;
        public readonly double LandfallLonDeg, LandfallLatDeg;
        public readonly double StartWindMs, PeakOceanWindMs, PreLandfallWindMs, LandfallStepWindMs, PostLandfall12hWindMs;
        public readonly int OceanStepsBeforeLandfall, IslandCrossingsIgnored;

        public FlatStormScenarioPlan(
            FlatStormScenario scenario,
            double startDay, double expectedLandfallDay, double expectedDecayDay,
            FlatCycloneTrack initialTrack,
            int startOceanTile, int landfallCoastalTile,
            double startLonDeg, double startLatDeg,
            double landfallLonDeg, double landfallLatDeg,
            double startWindMs, double peakOceanWindMs, double preLandfallWindMs,
            double landfallStepWindMs, double postLandfall12hWindMs,
            int oceanStepsBeforeLandfall, int islandCrossingsIgnored)
        {
            Scenario = scenario;
            StartDay = startDay;
            ExpectedLandfallDay = expectedLandfallDay;
            ExpectedDecayDay = expectedDecayDay;
            InitialTrack = initialTrack;
            StartOceanTile = startOceanTile;
            LandfallCoastalTile = landfallCoastalTile;
            StartLonDeg = startLonDeg;
            StartLatDeg = startLatDeg;
            LandfallLonDeg = landfallLonDeg;
            LandfallLatDeg = landfallLatDeg;
            StartWindMs = startWindMs;
            PeakOceanWindMs = peakOceanWindMs;
            PreLandfallWindMs = preLandfallWindMs;
            LandfallStepWindMs = landfallStepWindMs;
            PostLandfall12hWindMs = postLandfall12hWindMs;
            OceanStepsBeforeLandfall = oceanStepsBeforeLandfall;
            IslandCrossingsIgnored = islandCrossingsIgnored;
        }

        public FlatCycloneWeather CreateInitialWeather()
        {
            return new FlatCycloneWeather
            {
                StepIndex = FlatCycloneWeather.StepAt(StartDay),
                North = InitialTrack,
                PreviousNorth = InitialTrack
            };
        }

        public string FormatSummary()
        {
            var inv = CultureInfo.InvariantCulture;
            string title = Scenario == FlatStormScenario.EastChinaSeaLandfall
                ? "东海台风登陆中国大陆"
                : "北大西洋飓风登陆北美沿岸";
            return string.Format(inv,
                "{0}：D{1:F2} ({2:F2}°, {3:F2}°, {4:F1} m/s) → 海上推进 {5}×6h（峰值 {6:F1} m/s）→ " +
                "D{7:F2} 大陆登陆 ({8:F2}°, {9:F2}°, Tile {10}) 临岸/登陆/12h后 {11:F1}/{12:F1}/{13:F1} m/s",
                title, StartDay, StartLonDeg, StartLatDeg, StartWindMs,
                OceanStepsBeforeLandfall, PeakOceanWindMs,
                ExpectedLandfallDay, LandfallLonDeg, LandfallLatDeg, LandfallCoastalTile,
                PreLandfallWindMs, LandfallStepWindMs, PostLandfall12hWindMs);
        }
    }

    /// <summary>
    /// Shared Core C# entry point for reproducible Earth meteorological storm scenarios and read-only surface cyclone wind sampling.
    /// <para>
    /// Contract with Presentation / UI:
    /// <list type="bullet">
    ///   <item><description><see cref="TryCreateScenarioWorld"/> clones <see cref="WorldData"/> (preserving <see cref="FlatGeographicSurface"/>) before setting <c>Day</c> and <c>Cyclones</c>, so viewing a storm demo never mutates the player's active/saved campaign date.</description></item>
    ///   <item><description><see cref="TryStart(WorldData, FlatStormScenario, out int)"/> installs the scenario start state directly onto a target <see cref="WorldData"/> and outputs the target continental <c>coastalTile</c> for camera focus and local 64×64 SWE anchoring.</description></item>
    ///   <item><description><see cref="SampleSurfaceWind"/> provides zero-allocation, read-only 10 m cyclonic tangential/inflow wind (<c>m/s</c>), pressure deficit (<c>hPa</c>), and rain rate (<c>mm/h</c>) in normalized spherical <c>(u, v)</c> at <c>calendarDay</c>.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public static class FlatStormScenarios
    {
        public const string CanonicalEarthSeed = "ORBIS-2D-2026";

        public static string ScenarioLabel(FlatStormScenario scenario) =>
            scenario == FlatStormScenario.EastChinaSeaLandfall
                ? "东海台风（近海加强并越过中国大陆海岸）"
                : "北大西洋飓风（洋面推进并登陆北美沿岸）";

        /// <summary>
        /// Recommended UI/demo entry point: creates an isolated scenario copy of <paramref name="sourceWorld"/>
        /// (preserving surveyed shorelines, rivers, and water references) and installs <paramref name="scenario"/>
        /// without altering <paramref name="sourceWorld"/>'s saved <see cref="WorldData.Day"/> or <see cref="WorldData.Cyclones"/>.
        /// </summary>
        public static bool TryCreateScenarioWorld(
            WorldData sourceWorld, FlatStormScenario scenario,
            out WorldData scenarioWorld, out int coastalTile, out FlatStormScenarioPlan plan)
        {
            scenarioWorld = null;
            coastalTile = -1;
            plan = default;
            if (sourceWorld == null || sourceWorld.Preset != TerrainPreset.Earth || sourceWorld.Moisture == null)
                return false;

            var isolated = sourceWorld.Clone();
            sourceWorld.GeographicSurface?.Attach(isolated);
            if (!TryStart(isolated, scenario, out coastalTile, out plan))
            {
                // If sourceWorld used a custom seed whose synoptic Rossby phase blocked every candidate window,
                // evaluate on canonical seed on the isolated demo copy (never touching sourceWorld).
                isolated.Seed = CanonicalEarthSeed;
                if (!TryStart(isolated, scenario, out coastalTile, out plan))
                    return false;
            }
            scenarioWorld = isolated;
            return true;
        }

        /// <summary>
        /// Installs the reproducible initial disturbance and start calendar day for <paramref name="scenario"/> onto <paramref name="world"/>,
        /// returning <c>true</c> and the continental mainland <paramref name="coastalTile"/> where natural <see cref="FlatCycloneWeather"/> evolution makes landfall.
        /// Note: modifies <c>world.Day</c> and <c>world.Cyclones</c>; call <see cref="TryCreateScenarioWorld"/> when previewing from an active campaign.
        /// </summary>
        public static bool TryStart(WorldData world, FlatStormScenario scenario, out int coastalTile) =>
            TryStart(world, scenario, out coastalTile, out _);

        public static bool TryStart(
            WorldData world, FlatStormScenario scenario,
            out int coastalTile, out FlatStormScenarioPlan plan)
        {
            coastalTile = -1;
            plan = default;
            if (!TryResolvePlan(world, scenario, out plan))
                return false;

            world.Day = plan.StartDay;
            world.Cyclones = plan.CreateInitialWeather();
            world.Revision++;
            coastalTile = plan.LandfallCoastalTile;
            return coastalTile >= 0;
        }

        /// <summary>
        /// Read-only evaluation of the scenario plan on <paramref name="world"/> without mutating <paramref name="world"/>.
        /// </summary>
        public static bool TryResolvePlan(
            WorldData world, FlatStormScenario scenario, out FlatStormScenarioPlan plan)
        {
            plan = default;
            if (world == null || world.Preset != TerrainPreset.Earth || world.Moisture == null)
                return false;
            if (world.Facts == null)
                FlatTerrainAnalysis.Build(world, default, false);

            var mainlandMask = BuildContinentalMainlandMask(world);

            if (scenario == FlatStormScenario.EastChinaSeaLandfall)
            {
                // Baseline-verified canonical trajectory on unmodified FlatCycloneWeather (Earth512 & Earth256, ORBIS-2D-2026):
                // D262.00 (123.50E, 25.50N) -> crosses southern East China Sea / north of Taiwan -> D262.75 (119.22E, 25.93N) Fujian Mainland China landfall -> inland decay.
                var candidates = new (double day, double lon, double lat, double wind, double dp, double age, double cool)[]
                {
                    (262.00, 123.50, 25.50, 43.0, 45.0, 2.0, 0.45),
                    (258.00, 124.00, 25.20, 44.0, 46.0, 2.0, 0.45),
                    (248.00, 123.80, 25.60, 44.0, 46.0, 2.0, 0.45),
                    (236.00, 123.60, 25.40, 44.0, 46.0, 2.0, 0.45)
                };
                foreach (var c in candidates)
                {
                    if (EvaluateCandidate(world, mainlandMask, scenario,
                        c.day, c.lon, c.lat, c.wind, c.dp, c.age, c.cool,
                        minLandLon: 116.5, maxLandLon: 122.9, minLandLat: 23.0, maxLandLat: 33.2,
                        minOceanSteps: 2, minLandfallApproachWind: 30.0, out plan))
                        return true;
                }
                for (double day = 180.0; day <= 268.0; day += 2.0)
                foreach (var start in new[] { (123.5, 25.5, 43.0, 45.0), (124.2, 26.2, 44.0, 46.0), (122.8, 24.8, 42.0, 44.0) })
                {
                    if (EvaluateCandidate(world, mainlandMask, scenario,
                        day, start.Item1, start.Item2, start.Item3, start.Item4, 2.0, 0.45,
                        minLandLon: 116.5, maxLandLon: 122.9, minLandLat: 23.0, maxLandLat: 33.2,
                        minOceanSteps: 2, minLandfallApproachWind: 28.0, out plan))
                        return true;
                }
                return false;
            }
            else if (scenario == FlatStormScenario.NorthAtlanticLandfall)
            {
                // Baseline-verified canonical trajectory on unmodified FlatCycloneWeather (Earth512 & Earth256, ORBIS-2D-2026):
                // D229.00 (75.50W, 25.50N) -> crosses Bahamas & Gulf Stream -> D229.75 (80.07W, 26.74N) Florida Mainland landfall -> inland decay.
                var candidates = new (double day, double lon, double lat, double wind, double dp, double age, double cool)[]
                {
                    (229.00, -75.50, 25.50, 43.0, 45.0, 2.0, 0.45),
                    (225.00, -75.80, 25.40, 43.0, 45.0, 2.0, 0.45),
                    (233.00, -75.20, 25.60, 44.0, 46.0, 2.0, 0.45),
                    (218.00, -84.50, 24.80, 43.0, 45.0, 2.0, 0.45)
                };
                foreach (var c in candidates)
                {
                    if (EvaluateCandidate(world, mainlandMask, scenario,
                        c.day, c.lon, c.lat, c.wind, c.dp, c.age, c.cool,
                        minLandLon: -98.0, maxLandLon: -75.0, minLandLat: 25.0, maxLandLat: 36.5,
                        minOceanSteps: 2, minLandfallApproachWind: 30.0, out plan))
                        return true;
                }
                for (double day = 180.0; day <= 264.0; day += 2.0)
                foreach (var start in new[] { (-75.5, 25.5, 43.0, 45.0), (-76.5, 26.2, 43.0, 45.0), (-85.0, 25.5, 43.0, 45.0) })
                {
                    if (EvaluateCandidate(world, mainlandMask, scenario,
                        day, start.Item1, start.Item2, start.Item3, start.Item4, 2.0, 0.45,
                        minLandLon: -98.0, maxLandLon: -75.0, minLandLat: 25.0, maxLandLat: 36.5,
                        minOceanSteps: 2, minLandfallApproachWind: 28.0, out plan))
                        return true;
                }
                return false;
            }
            return false;
        }

        /// <summary>
        /// Simulates the natural 6-hour trajectory of an installed scenario from its current state without mutating <paramref name="world"/>.
        /// </summary>
        public static FlatStormTrackPoint[] TraceTrajectory(WorldData world, int steps = 20)
        {
            if (world == null || world.Cyclones == null || steps < 0) return Array.Empty<FlatStormTrackPoint>();
            var mainland = BuildContinentalMainlandMask(world);
            var cyc = world.Cyclones.Clone();
            double baseDay = cyc.StepIndex * 0.25;
            var list = new List<FlatStormTrackPoint>(steps + 1);
            for (int s = 0; s <= steps; s++)
            {
                double d = baseDay + s * 0.25;
                if (s > 0) cyc.AdvanceTo(world, d);
                var tr = cyc.North;
                if (!tr.Tracked) break;
                double lon = (tr.U - 0.5) * 360.0;
                double lat = (tr.V - 0.5) * 180.0;
                ClassifyEarthSurface(world, lon, lat, mainland, out var kind, out bool isOcean, out bool isMain, out double shoreKm, out int tile);
                list.Add(new FlatStormTrackPoint(d, lon, lat, tr.WindSpeed, tr.PressureDeficit, tr.OceanCooling, kind, isOcean, isMain, shoreKm, tile));
            }
            return list.ToArray();
        }

        /// <summary>
        /// Lightweight read-only 10 m surface cyclone wind sampler (<c>m/s</c>, <c>hPa</c>, <c>mm/h</c>) shared by local 64×64 SWE and visual presentation.
        /// <para>
        /// Coordinates: <paramref name="u"/> in <c>[0, 1)</c> (<c>lonDeg / 360 + 0.5</c>), <paramref name="v"/> in <c>[0, 1]</c> (<c>latDeg / 180 + 0.5</c>).
        /// Time: <paramref name="calendarDay"/> (defaults to <c>world.Day</c> when <c>NaN</c>). Never mutates <paramref name="world"/>.
        /// </para>
        /// </summary>
        public static FlatCycloneSurfaceWind SampleSurfaceWind(WorldData world, double u, double v, double calendarDay = double.NaN)
        {
            if (world?.Cyclones == null) return default;
            double day = double.IsNaN(calendarDay) ? world.Day : calendarDay;
            var state = world.Cyclones;
            long targetStep = FlatCycloneWeather.StepAt(day);
            if (targetStep < state.StepIndex - 1)
                state = FlatCycloneWeather.SpinUpAt(world, day);
            return state.SampleSurfaceWind(u, v, day);
        }

        /// <summary>
        /// Convenience read-only query returning unsigned cyclonic tangential speed (<c>m/s</c>) and 10 m vector wind (<c>eastMs, northMs</c>).
        /// Returns <c>false</c> (all outputs <c>0</c>) when no active cyclone influences <c>(u, v)</c> at <paramref name="calendarDay"/>.
        /// </summary>
        public static bool TrySampleSurfaceTangentialWindMs(
            WorldData world, double u, double v, double calendarDay,
            out float tangentialSpeedMs, out float eastMs, out float northMs)
        {
            var s = SampleSurfaceWind(world, u, v, calendarDay);
            tangentialSpeedMs = s.TangentialSpeedMs;
            eastMs = s.EastMs;
            northMs = s.NorthMs;
            return s.Active;
        }

        /// <summary>
        /// Flood-fills non-ocean landmasses on <paramref name="world"/> and marks only continental-scale landmasses
        /// (&gt;= 1.5% of planetary cells, i.e., Eurasia-Africa, North America, South America, Australia, Antarctica),
        /// strictly excluding offshore islands (Ryukyu, Taiwan, Hainan, Bahamas, Cuba, Hispaniola, Japan, Philippines).
        /// </summary>
        public static bool[] BuildContinentalMainlandMask(WorldData world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            int count = world.Count;
            int minMainlandTiles = Math.Max(16, count / 64);
            var mainland = new bool[count];
            var visited = new bool[count];
            var queue = new int[count];
            var component = new List<int>(Math.Min(count, 8192));

            for (int start = 0; start < count; start++)
            {
                if (visited[start] || FlatTerrainAnalysis.Ocean(world.Water[start])) continue;
                component.Clear();
                int head = 0, tail = 0;
                visited[start] = true;
                queue[tail++] = start;
                while (head < tail)
                {
                    int cur = queue[head++];
                    component.Add(cur);
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = world.Neighbor(cur, d);
                        if (nb >= 0 && !visited[nb] && !FlatTerrainAnalysis.Ocean(world.Water[nb]))
                        {
                            visited[nb] = true;
                            queue[tail++] = nb;
                        }
                    }
                }
                if (component.Count >= minMainlandTiles)
                {
                    for (int k = 0; k < component.Count; k++)
                        mainland[component[k]] = true;
                }
            }
            return mainland;
        }

        public static void ClassifyEarthSurface(
            WorldData world, double lonDeg, double latDeg, bool[] mainlandMask,
            out WaterKind surfaceKind, out bool isOcean, out bool isContinentalMainland,
            out double signedShoreKm, out int nearestTile)
        {
            double u = FlatScrollMath.Wrap(lonDeg / 360.0 + 0.5, 1.0);
            double v = FMath.Clamp((float)(latDeg / 180.0 + 0.5), 0.0001f, 0.9999f);
            int tx = FMath.Wrap((int)Math.Floor(u * world.Width), world.Width);
            int ty = FMath.Clamp((int)Math.Floor(v * world.Height), 0, world.Height - 1);
            nearestTile = world.Index(tx, ty);

            var survey = world.GeographicSurface;
            if (survey != null && survey.Prepared(world))
            {
                var kind = survey.At(world, new FlatSeaPoint(lonDeg, latDeg), out signedShoreKm, out bool covered);
                if (covered)
                {
                    surfaceKind = kind;
                    isOcean = FlatTerrainAnalysis.Ocean(kind);
                    if (isOcean)
                    {
                        isContinentalMainland = false;
                        return;
                    }
                    // Surveyed land/river/lake: check whether it sits on or immediately abuts a continental mainland tile
                    // (within 1 coarse cell of shoreline, where sub-tile surveyed peninsulas like Florida/Zhejiang project slightly into a coarse sea cell).
                    bool nearMainland = mainlandMask != null && IsOnOrAdjacentToMainland(world, mainlandMask, tx, ty);
                    isContinentalMainland = nearMainland;
                    return;
                }
            }

            surfaceKind = world.Water[nearestTile];
            isOcean = FlatTerrainAnalysis.Ocean(surfaceKind);
            double cellKm = 40030.2 / world.Width;
            float sd = FlatSurfaceSampling.SampleShore(world, (float)(u * world.Width), (float)(v * world.Height));
            signedShoreKm = sd * cellKm;
            isContinentalMainland = !isOcean && mainlandMask != null && mainlandMask[nearestTile];
        }

        static bool IsOnOrAdjacentToMainland(WorldData world, bool[] mainlandMask, int tx, int ty)
        {
            int c = world.Index(tx, ty);
            if (c >= 0 && mainlandMask[c]) return true;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int ny = ty + dy;
                if (ny < 0 || ny >= world.Height) continue;
                int nb = world.Index(tx + dx, ny);
                if (nb >= 0 && mainlandMask[nb]) return true;
            }
            return false;
        }

        static bool EvaluateCandidate(
            WorldData world, bool[] mainlandMask, FlatStormScenario scenario,
            double startDay, double lon0, double lat0, double wind0, double dp0, double age0, double cool0,
            double minLandLon, double maxLandLon, double minLandLat, double maxLandLat,
            int minOceanSteps, double minLandfallApproachWind,
            out FlatStormScenarioPlan plan)
        {
            plan = default;
            ClassifyEarthSurface(world, lon0, lat0, mainlandMask, out _, out bool startIsOcean, out _, out double startShoreKm, out int startTile);
            if (!startIsOcean || startShoreKm < 12.0) return false;

            var initTrack = new FlatCycloneTrack
            {
                Tracked = true,
                U = FlatScrollMath.Wrap(lon0 / 360.0 + 0.5, 1.0),
                V = FMath.Clamp((float)(lat0 / 180.0 + 0.5), 0.01f, 0.99f),
                WindSpeed = wind0,
                PressureDeficit = dp0,
                Phase = 0.6,
                AgeDays = age0,
                OceanCooling = cool0
            };

            var cyc = new FlatCycloneWeather
            {
                StepIndex = FlatCycloneWeather.StepAt(startDay),
                North = initTrack,
                PreviousNorth = initTrack
            };

            double peakOceanWind = wind0;
            double preLandfallWind = wind0;
            double landfallStepWind = 0.0;
            double post12hWind = 999.0;
            double landfallDay = -1.0;
            double decayDay = -1.0;
            double lfLon = 0.0, lfLat = 0.0;
            int oceanSteps = 0, islandCrossings = 0, postSteps = 0;
            bool landedOnMainland = false;

            for (int step = 1; step <= 28; step++)
            {
                double d = startDay + step * 0.25;
                cyc.AdvanceTo(world, d);
                var tr = cyc.North;
                if (!tr.Tracked)
                {
                    if (landedOnMainland)
                    {
                        if (postSteps < 2) post12hWind = 0.0;
                        if (decayDay < 0.0) decayDay = d;
                    }
                    break;
                }

                double lon = (tr.U - 0.5) * 360.0;
                double lat = (tr.V - 0.5) * 180.0;
                ClassifyEarthSurface(world, lon, lat, mainlandMask, out _, out bool isOcean, out bool isMainland, out _, out _);

                if (!landedOnMainland)
                {
                    if (isOcean)
                    {
                        oceanSteps++;
                        if (tr.WindSpeed > peakOceanWind) peakOceanWind = tr.WindSpeed;
                        preLandfallWind = tr.WindSpeed;
                    }
                    else if (!isMainland)
                    {
                        // Offshore island (e.g., Ryukyu, Taiwan, Bahamas): record & continue without falsely declaring mainland landfall.
                        islandCrossings++;
                    }
                    else if (lon >= minLandLon && lon <= maxLandLon && lat >= minLandLat && lat <= maxLandLat)
                    {
                        landedOnMainland = true;
                        landfallDay = d;
                        lfLon = lon;
                        lfLat = lat;
                        landfallStepWind = tr.WindSpeed;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    postSteps++;
                    if (postSteps == 2) post12hWind = tr.WindSpeed;
                    if (decayDay < 0.0 && tr.WindSpeed <= Math.Min(14.0, preLandfallWind * 0.40))
                        decayDay = d;
                }
            }

            if (!landedOnMainland || oceanSteps < minOceanSteps || preLandfallWind < minLandfallApproachWind)
                return false;
            if (post12hWind > preLandfallWind * 0.62)
                return false;
            if (decayDay < 0.0)
                decayDay = landfallDay + 1.25;

            // Locate the exact mainland coastal tile right along the storm's landfall crossing for camera & 64x64 SWE anchoring.
            double seaLon = (cyc.PreviousNorth.U - 0.5) * 360.0;
            int coastalTile = FindMainlandLandfallCoastTile(world, mainlandMask, lfLon, lfLat);
            if (coastalTile < 0) return false;
            _ = seaLon;

            plan = new FlatStormScenarioPlan(
                scenario,
                startDay, landfallDay, decayDay,
                initTrack,
                startTile, coastalTile,
                lon0, lat0, lfLon, lfLat,
                wind0, peakOceanWind, preLandfallWind, landfallStepWind, post12hWind,
                oceanSteps, islandCrossings);
            return true;
        }

        static int FindMainlandLandfallCoastTile(WorldData world, bool[] mainlandMask, double lfLonDeg, double lfLatDeg)
        {
            int w = world.Width, h = world.Height;
            int cx = FMath.Wrap((int)Math.Floor((lfLonDeg / 360.0 + 0.5) * w), w);
            int cy = FMath.Clamp((int)Math.Floor((lfLatDeg / 180.0 + 0.5) * h), 0, h - 1);

            int bestTile = -1;
            double bestScore = -1e9;
            int radius = Math.Max(4, w / 64);
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int ny = cy + dy;
                if (ny < 2 || ny >= h - 2) continue;
                int tile = world.Index(cx + dx, ny);
                if (FlatTerrainAnalysis.Ocean(world.Water[tile])) continue;
                if (mainlandMask != null && !mainlandMask[tile]) continue;
                if (world.CoastDistance[tile] > 2) continue;

                // Prefer CoastDistance == 1 low-lying coastal plain / estuary tiles with adjacent open ocean for SWE storm surge.
                int seaNeighbors = 0, lowPlain = 0;
                for (int sy = -3; sy <= 3; sy++)
                for (int sx = -3; sx <= 3; sx++)
                {
                    int ky = ny + sy;
                    if (ky < 0 || ky >= h) continue;
                    int nb = world.Index(cx + dx + sx, ky);
                    if (FlatTerrainAnalysis.Ocean(world.Water[nb])) seaNeighbors++;
                    else if (world.Elevation[nb] <= 120) lowPlain++;
                }
                if (seaNeighbors < 3 || lowPlain < 4) continue;

                double distPenalty = 0.45 * Math.Sqrt(dx * dx + dy * dy);
                double elevBonus = 2.0 / (1.0 + Math.Max(0, (int)world.Elevation[tile]) / 45.0);
                double riverBonus = world.Water[tile] == WaterKind.River ? 0.85 : 0.0;
                double coastBonus = world.CoastDistance[tile] == 1 ? 1.2 : 0.4;
                double score = coastBonus + elevBonus + riverBonus + Math.Min(1.8, seaNeighbors * 0.08) - distPenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTile = tile;
                }
            }

            if (bestTile >= 0) return bestTile;
            int center = world.Index(cx, cy);
            return center >= 0 && !FlatTerrainAnalysis.Ocean(world.Water[center]) ? center : -1;
        }
    }
}
