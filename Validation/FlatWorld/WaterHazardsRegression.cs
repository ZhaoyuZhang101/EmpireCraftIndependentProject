using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Orbis.Flat;

public static class WaterHazardsRegression
{
    static int assertions;
    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("WaterHazardsRegression FAIL: " + message);
    }

    public static void Run(string jsonReportPath = null)
    {
        assertions = 0;
        var report = new StringBuilder(4096);
        var inv = CultureInfo.InvariantCulture;

        report.AppendLine("=== ORBIS Local SWE Hazards Numerical & Physical Validation ===");

        // 1. Hydrostatic lake-at-rest over irregular bed + dry islands + multi-basin levee separation
        TestHydrostaticRestAndMultiBasin(report);

        // 2. Wet-dry levee non-breach (sub-crest flood wave) & controlled saddle overtopping
        TestLeveeNonBreachAndSaddleOvertop(report);

        // 3. Long-wave coseismic tsunami phase speed vs theoretical sqrt(g * h) across 4 depths
        TestTsunamiWaveSpeedMatchesSqrtGH(report);

        // 4. Three hazard scenarios on real procedural & surveyed Earth worlds:
        //    terrain-guided advance, post-event recession, non-negativity, mass ledger & CPU worker budget
        var worldContinents = FlatGenerator.Generate(512, 256, "ORBIS-2D-2026", TerrainPreset.Continents, default);
        TestUnforcedHazardSessionsAtRest(worldContinents, report);
        TestThreeScenariosOnWorld(worldContinents, "Continents-512x256 (ORBIS-2D-2026)", report);

        const string resFolder = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        var rawEarth = File.ReadAllBytes(resFolder + "Earth256.bytes");
        var waterEarth = File.ReadAllBytes(resFolder + "EarthWater256.bytes");
        var riversEarth = File.ReadAllBytes(resFolder + "EarthRivers.bytes");
        var worldEarth = FlatGenerator.Generate(256, 128, "ORBIS-EARTH-HAZARD", TerrainPreset.Earth, default, rawEarth, waterEarth, riversEarth);
        TestThreeScenariosOnWorld(worldEarth, "SurveyedEarth-256x128", report);

        TestSurveyedStormSurgeShoreline(worldEarth, resFolder, report);

        // 5. Independent triggers (Typhoon vs Tsunami), live weather detection & save-format byte identity
        TestIndependentTriggersLiveWeatherAndSave(worldContinents, report);

        // 6. Reproducible Earth storm scenarios (EastChinaSea -> Mainland China & NorthAtlantic -> Mainland North America)
        //    coupled end-to-end with real-time cyclone position/intensity/rainfall 64x64 SWE (pre-landfall -> peak -> recession, no-storm rest)
        TestReproducibleEarthStormScenariosAndLiveSwe(resFolder, report);

        // 7. Same-world 6h regional snapshot surface precipitation -> SWE injection & shared FlatSolarClock calendar lockstep (1x, 20x, pause)
        TestSameWorldSnapshotPrecipitationAndSharedCalendar(resFolder, report);

        string summary = string.Format(inv,
            "PASS WaterHazardsRegression: {0} assertions passed (hydrostatic rest, levee non-breach, tsunami sqrt(g*h), 3 scenarios advance/recede, ECS & North Atlantic real-time storm surge/flood coupling, 6h snapshot precip & calendar lockstep, mass ledger & save compatibility).",
            assertions);
        report.AppendLine(summary);
        Console.Write(report.ToString());

        string outPath = string.IsNullOrWhiteSpace(jsonReportPath)
            ? "ValidationOutput/WaterHazards/dotnet-hazards-report.txt"
            : jsonReportPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllText(outPath, report.ToString(), Encoding.UTF8);
    }

    static void TestSurveyedStormSurgeShoreline(WorldData world, string folder, StringBuilder log)
    {
        byte[] Asset(string name) => File.ReadAllBytes(folder + name + ".bytes");
        var surface = FlatGeographicSurface.Read(Asset("EarthGlobalStraitShore"), Asset("EarthOceanShore"),
            Asset("EarthMajorLakes"), Asset("EarthLakeDetail"), Asset("EarthPolarCoast"));
        surface.Attach(world);

        // Aegean and the Turkish Straits include both surveyed dry banks and
        // navigable water over land cells at this simulation resolution.
        var session = FlatWaterHazardSession.CreateAtPatch(world, FlatHazardKind.TyphoonSurge,
            137, 84, applyInitialForcing: false);
        int corrected = 0;
        int resolution = session.Resolution;
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float wx = session.PatchX + (x + .5f) * session.SpanTiles / resolution;
            float wy = session.PatchY + (y + .5f) * session.SpanTiles / resolution;
            var point = new FlatSeaPoint(wx * 360.0 / world.Width - 180, wy * 180.0 / world.Height - 90);
            var kind = surface.At(world, point, out _, out bool covered);
            if (!covered || kind == WaterKind.River) continue;

            int ix = (int)Math.Floor(wx), iy = (int)Math.Floor(wy);
            int tile = world.Index(ix, iy);
            if (FlatWaterGeometry.RiverDistance(world, tile, wx - ix, wy - iy) <= 0f) continue;

            bool wet = FlatTerrainAnalysis.AreaWater(kind);
            if (wet != (FlatSurfaceSampling.SampleShore(world, wx, wy) > .02f)) corrected++;
            Check(session.Solver.InitiallyWet[y * resolution + x] == wet,
                $"Surveyed Aegean shoreline disagrees with the resting surge grid at {point.Longitude:F4}, {point.Latitude:F4}.");
        }
        Check(corrected > 0, "The Aegean regression did not sample any survey corrections to the coarse grid.");
        log.AppendLine($"PASS surveyed Aegean surge shoreline: {corrected} subcells differ from and correct the coarse-grid mask.");
    }

    static void TestHydrostaticRestAndMultiBasin(StringBuilder log)
    {
        const int w = 48, h = 32;
        var bed = new double[w * h];
        var depth = new double[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            double shoal = 0.65 * Math.Sin(x * 0.41) * Math.Cos(y * 0.37);
            if (x == 22)
            {
                // Continuous dry levee separating an elevated inland basin (+4.2 m) from sea level (0.0 m)
                bed[i] = 5.5;
                depth[i] = 0.0;
            }
            else if (x < 22)
            {
                // Elevated lake/river basin at eta = +4.2 m with an emergent island around (10, 16)
                bool island = (x - 10) * (x - 10) + (y - 16) * (y - 16) <= 9;
                bed[i] = island ? 5.1 + shoal * 0.2 : 1.4 + shoal;
                depth[i] = island ? 0.0 : 4.2 - bed[i];
            }
            else
            {
                // Sea-level basin at eta = 0.0 m with variable bathymetry and dry beach on x >= 40
                if (x >= 40)
                {
                    bed[i] = 0.4 + (x - 40) * 0.55 + Math.Max(0, shoal * 0.2);
                    depth[i] = 0.0;
                }
                else
                {
                    bed[i] = -3.5 + shoal;
                    depth[i] = -bed[i];
                }
            }
        }

        var swe = new FlatShallowWater(w, h, 50.0, bed, depth);
        // Open sea boundary on x == 0? No, x == 0 is upper basin (+4.2 m); open sea on south/north of sea side:
        for (int y = 0; y < h; y++)
            swe.SetOpenBoundary(y * w, true, 4.2);

        double maxEtaErr = 0.0, maxVel = 0.0;
        for (int step = 0; step < 80; step++)
            swe.Advance(3.0, 24);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            Check(swe.Depth[i] >= 0.0 && !double.IsNaN(swe.Depth[i]), "Non-negative depth at rest");
            maxEtaErr = Math.Max(maxEtaErr, Math.Abs(swe.Depth[i] - depth[i]));
            maxVel = Math.Max(maxVel, Math.Max(Math.Abs(swe.VelocityX[i]), Math.Abs(swe.VelocityY[i])));
            if (depth[i] == 0.0) Check(swe.Depth[i] == 0.0, "Dry island/levee/beach remains strictly dry at rest");
        }
        Check(maxEtaErr < 1e-12 && maxVel < 1e-12, $"Well-balanced multi-basin rest violated: dEta={maxEtaErr:E3}, vel={maxVel:E3}");
        Check(swe.RelativeMassError() < 1e-12, $"Rest mass error={swe.RelativeMassError():E3}");
        log.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "PASS [1] Multi-basin & uneven-bed hydrostatic rest: max |dEta|={0:E2} m, max |u|={1:E2} m/s, relMassErr={2:E2}",
            maxEtaErr, maxVel, swe.RelativeMassError()));
    }

    static void TestUnforcedHazardSessionsAtRest(WorldData world, StringBuilder log)
    {
        foreach (var kind in new[] { FlatHazardKind.RainFlood, FlatHazardKind.TyphoonSurge, FlatHazardKind.Tsunami })
        {
            var unforced = FlatWaterHazardSession.Create(world, kind, preferredTile: -1, liveWeatherDriven: false, applyInitialForcing: false);
            for (int k = 0; k < 20; k++) unforced.Solver.Advance(4.0, 24);
            var m = unforced.CaptureMetrics();
            Check(m.InundatedDryCells == 0 && Math.Abs(m.MaxSurfaceAnomalyMeters) < 1e-10 && m.RelativeMassError < 1e-12,
                $"Unforced {kind} session moved from rest: inundated={m.InundatedDryCells}, maxAnom={m.MaxSurfaceAnomalyMeters:E3}");
        }
        log.AppendLine("PASS [1b] Unforced real-terrain hazard patches (RainFlood / TyphoonSurge / Tsunami) hold exact hydrostatic rest.");
    }

    static void TestLeveeNonBreachAndSaddleOvertop(StringBuilder log)
    {
        // 64 x 24 domain: river/flood side x < 30 (bed = 0.0 m), sloping western levee toe x=28..29 (bed 0.9, 1.8 m),
        // levee crown x = 30 (bed = 3.2 m), protected low polder x > 30 (bed = -0.5 m, dry!).
        const int w = 64, h = 24, leveeX = 30;
        var bed = new double[w * h];
        var init = new double[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            if (x < 28) { bed[i] = 0.0; init[i] = 1.2; }
            else if (x == 28) { bed[i] = 1.0; init[i] = 0.2; }
            else if (x == 29) { bed[i] = 1.95; init[i] = 0.0; }
            else if (x == leveeX) { bed[i] = 3.20; init[i] = 0.0; }
            else { bed[i] = -0.50 + (x - 31) * 0.04; init[i] = 0.0; }
        }

        const double crownHeight = 4.00;
        for (int y = 0; y < h; y++)
            bed[y * w + leveeX] = crownHeight;

        var swe = new FlatShallowWater(w, h, 10.0, bed, init, manningRoughness: 0.022);
        double peakEverWestEta = 0.0;
        // Inject flood inflow raising west basin stage to ~2.65 m (transient wall clapotis ~3.4 m < 4.0 m levee crown).
        for (int step = 0; step < 160; step++)
        {
            Array.Clear(swe.ChannelInflowRate, 0, swe.Count);
            if (step < 50)
            {
                for (int y = 0; y < h; y++)
                for (int x = 2; x <= 10; x++)
                    swe.ChannelInflowRate[y * w + x] = 0.062;
            }
            swe.Advance(1.5, 24);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < leveeX; x++)
                peakEverWestEta = Math.Max(peakEverWestEta, swe.SurfaceElevation(y * w + x));
        }

        double maxToeDepth = 0.0, maxCrownOrLeeDepth = 0.0;
        for (int y = 0; y < h; y++)
        {
            maxToeDepth = Math.Max(maxToeDepth, swe.Depth[y * w + 29]);
            for (int x = leveeX; x < w; x++)
                maxCrownOrLeeDepth = Math.Max(maxCrownOrLeeDepth, swe.Depth[y * w + x]);
        }
        Check(maxToeDepth > 0.45 && peakEverWestEta < crownHeight,
            $"Expected flood wave to wet levee slope below crest: peakWestEta={peakEverWestEta:F2} vs crown={crownHeight:F2}, toeDepth={maxToeDepth:F2}");
        Check(maxCrownOrLeeDepth == 0.0, $"Levee leaked onto crown or protected polder! maxLeeDepth={maxCrownOrLeeDepth:E3}");
        Check(swe.RelativeMassError() < 1e-12, $"Levee non-breach mass error={swe.RelativeMassError():E3}");

        // Now lower a 2-cell low saddle at y=11..12 on x=leveeX to 1.80 m (< west stage ~2.6 m) while keeping rest of crown at 4.00 m.
        swe.Bed[11 * w + leveeX] = 1.80;
        swe.Bed[12 * w + leveeX] = 1.80;
        Array.Clear(swe.ChannelInflowRate, 0, swe.Count);
        for (int step = 0; step < 50; step++) swe.Advance(1.5, 24);

        int polderWet = 0;
        double intactCrownDepth = 0.0;
        for (int y = 0; y < h; y++)
        {
            if (y < 9 || y > 14) intactCrownDepth = Math.Max(intactCrownDepth, swe.Depth[y * w + leveeX]);
            for (int x = leveeX + 1; x < w; x++)
                if (swe.Depth[y * w + x] > 0.02) polderWet++;
        }
        Check(polderWet >= 20 && intactCrownDepth == 0.0,
            $"Saddle overtopping failed or intact levee crown leaked: polderWet={polderWet}, intactCrown={intactCrownDepth:E3}");
        Check(swe.RelativeMassError() < 1e-12, $"Saddle overtop mass error={swe.RelativeMassError():E3}");
        log.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "PASS [2] Wet-dry levee non-breach & saddle spill: west transient peak eta={0:F2} m < {1:F2} m crest, lee leakage=0.0 m, post-saddle polder wet={2} cells, relMassErr={3:E2}",
            peakEverWestEta, crownHeight, polderWet, swe.RelativeMassError()));
    }

    static void TestTsunamiWaveSpeedMatchesSqrtGH(StringBuilder log)
    {
        double[] depths = { 10.0, 40.0, 160.0, 640.0 };
        var measured = new double[depths.Length];
        const int nx = 240, ny = 4;
        const double dx = 50.0;
        const int xSource = 24, xGauge1 = 68, xGauge2 = 188;
        double gaugeDist = (xGauge2 - xGauge1) * dx; // 6000 m

        for (int k = 0; k < depths.Length; k++)
        {
            double H = depths[k];
            double cTheory = Math.Sqrt(FlatShallowWater.Gravity * H);
            var bed = new double[nx * ny];
            var init = new double[nx * ny];
            for (int i = 0; i < bed.Length; i++) { bed[i] = -H; init[i] = H; }

            // Frictionless long-wave benchmark channel with open radiation right boundary
            var swe = new FlatShallowWater(nx, ny, dx, bed, init, manningRoughness: 0.0, backgroundDrag: 0.0);
            for (int y = 0; y < ny; y++)
            {
                swe.SetOpenBoundary(y * nx, true, 0.0);
                swe.SetOpenBoundary(y * nx + nx - 1, true, 0.0);
            }

            // Coseismic seafloor uplift (small amplitude dB/H <= 1.5% for linear long-wave regime, zero water created)
            double amp = Math.Min(0.25, H * 0.015);
            for (int y = 0; y < ny; y++)
            for (int x = 6; x <= 42; x++)
            {
                double dist = (x - xSource) / 4.5;
                swe.DisplaceBed(y * nx + x, amp * Math.Exp(-0.5 * dist * dist));
            }
            Check(swe.SeabedUpliftVolume > 0.0 && Math.Abs(swe.Volume() - swe.InitialVolume) < 1e-9,
                "Coseismic seabed uplift must not invent water mass");

            double dtSample = Math.Min(0.25, 0.15 * dx / cTheory);
            double maxTime = (gaugeDist + 80 * dx) / cTheory;
            double peak1Val = -1.0, peak1Time = 0.0;
            double peak2Val = -1.0, peak2Time = 0.0;
            int rowMid = (ny / 2) * nx;

            while (swe.Elapsed < maxTime)
            {
                swe.Advance(dtSample, 8);
                double eta1 = swe.ExcessHead(rowMid + xGauge1);
                double eta2 = swe.ExcessHead(rowMid + xGauge2);
                if (eta1 > peak1Val) { peak1Val = eta1; peak1Time = swe.Elapsed; }
                if (eta2 > peak2Val) { peak2Val = eta2; peak2Time = swe.Elapsed; }
            }

            Check(peak1Val > amp * 0.30 && peak2Val > amp * 0.25 && peak2Time > peak1Time,
                $"Tsunami crest did not reach both gauges cleanly at H={H} m");
            double cMeas = gaugeDist / (peak2Time - peak1Time);
            double relErr = Math.Abs(cMeas - cTheory) / cTheory;
            measured[k] = cMeas;
            Check(relErr < 0.035, $"Tsunami phase speed mismatch at H={H} m: measured={cMeas:F2} m/s vs sqrt(gh)={cTheory:F2} m/s (err={relErr:P2})");
            Check(swe.RelativeMassError() < 1e-12, $"Tsunami channel mass error={swe.RelativeMassError():E3}");

            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "PASS [3] Tsunami wave speed H={0,5:F0} m: measured={1,6:F2} m/s, sqrt(g*h)={2,6:F2} m/s, error={3:P2}, relMassErr={4:E2}",
                H, cMeas, cTheory, relErr, swe.RelativeMassError()));
        }

        for (int k = 1; k < depths.Length; k++)
        {
            double ratio = measured[k] / measured[k - 1];
            Check(Math.Abs(ratio - 2.0) < 0.06, $"4x depth scaling expected 2.0x wave speed, got {ratio:F3}x");
        }
    }

    static void TestThreeScenariosOnWorld(WorldData world, string worldLabel, StringBuilder log)
    {
        foreach (var kind in new[] { FlatHazardKind.RainFlood, FlatHazardKind.TyphoonSurge, FlatHazardKind.Tsunami })
        {
            var session = FlatWaterHazardSession.Create(world, kind, preferredTile: -1, liveWeatherDriven: false, applyInitialForcing: true);
            int totalChunks = 30;
            double chunkDt = session.DurationSeconds / totalChunks;
            var stepTimesMs = new List<double>(totalChunks);

            int peakInundated = 0;
            double peakTime = 0.0, peakFloodDepth = 0.0, peakMeanWetBed = 0.0, peakMeanDryAdjBed = 0.0;
            double sumDryLandWaterAtPeak = 0.0;
            FlatHazardMetrics finalMetrics = default;

            for (int s = 0; s < totalChunks; s++)
            {
                long t0 = Stopwatch.GetTimestamp();
                session.Step(chunkDt, default, maxSubsteps: 48);
                stepTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);

                var solver = session.Solver;
                Check(solver.LastSubsteps <= 48, "Worker substep budget exceeded");
                double dryCellsWater = 0.0;
                for (int i = 0; i < solver.Count; i++)
                {
                    double d = solver.Depth[i];
                    Check(!double.IsNaN(d) && !double.IsInfinity(d) && d >= 0.0, $"Invalid depth in {kind} at step {s}");
                    Check(!double.IsNaN(solver.VelocityX[i]) && !double.IsNaN(solver.VelocityY[i]), $"NaN velocity in {kind}");
                    if (!solver.InitiallyWet[i]) dryCellsWater += d;
                }

                var m = session.CaptureMetrics();
                Check(m.RelativeMassError < 1e-11, $"{worldLabel} {kind} mass error={m.RelativeMassError:E3} at t={m.SimSeconds:F0}s");
                if (m.InundatedDryCells > peakInundated)
                {
                    peakInundated = m.InundatedDryCells;
                    peakTime = m.SimSeconds;
                    peakFloodDepth = m.MaxFloodDepthMeters;
                    peakMeanWetBed = m.MeanInundatedBedMeters;
                    peakMeanDryAdjBed = m.MeanDryAdjacentBedMeters;
                    sumDryLandWaterAtPeak = dryCellsWater;
                }
                else if (dryCellsWater > sumDryLandWaterAtPeak)
                {
                    sumDryLandWaterAtPeak = dryCellsWater;
                }
                finalMetrics = m;
            }

            double finalDryLandWater = 0.0;
            for (int i = 0; i < session.Solver.Count; i++)
                if (!session.Solver.InitiallyWet[i]) finalDryLandWater += session.Solver.Depth[i];

            // Verify render buffer packing produces finite values.
            var packedHalf = new ushort[session.Resolution * session.Resolution * 4];
            session.PackHalfRgba(packedHalf);

            // Physical & topographic assertions:
            Check(peakInundated >= 24 && peakFloodDepth >= 0.35,
                $"{worldLabel} {kind} insufficient inundation: peakCells={peakInundated}, maxDepth={peakFloodDepth:F2} m");
            Check(peakMeanWetBed < peakMeanDryAdjBed,
                $"{worldLabel} {kind} flood did not follow lower terrain: wetBed={peakMeanWetBed:F2} m vs dryBorderBed={peakMeanDryAdjBed:F2} m");
            Check(finalDryLandWater < sumDryLandWaterAtPeak * 0.68 && finalMetrics.InundatedDryCells < peakInundated,
                $"{worldLabel} {kind} failed to recede: peakCells={peakInundated} -> finalCells={finalMetrics.InundatedDryCells}, dryVolRatio={finalDryLandWater / Math.Max(1e-6, sumDryLandWaterAtPeak):P1}");

            // Source attribution assertions:
            if (kind == FlatHazardKind.RainFlood)
            {
                Check(finalMetrics.RainInputVolumeM3 > 0 && finalMetrics.ChannelInflowVolumeM3 > 0 && finalMetrics.SeabedUpliftVolumeM3 == 0,
                    $"{worldLabel} RainFlood source ledger mismatch");
            }
            else if (kind == FlatHazardKind.TyphoonSurge)
            {
                Check(finalMetrics.RainInputVolumeM3 > 0 && finalMetrics.BoundaryInflowVolumeM3 > 0 && finalMetrics.SeabedUpliftVolumeM3 == 0,
                    $"{worldLabel} TyphoonSurge source ledger mismatch");
            }
            else if (kind == FlatHazardKind.Tsunami)
            {
                Check(finalMetrics.SeabedUpliftVolumeM3 > 0 && finalMetrics.RainInputVolumeM3 == 0 &&
                      finalMetrics.ChannelInflowVolumeM3 == 0 && finalMetrics.BoundaryOutflowVolumeM3 > 0,
                    $"{worldLabel} Tsunami source ledger mismatch");
            }

            stepTimesMs.Sort();
            double medianMs = stepTimesMs[stepTimesMs.Count / 2];
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "PASS [4] {0} | {1,-12}: site=[{2},{3}] peakInund={4} cells ({5:F2} m @ {6:F0}s, bed {7:F2}<{8:F2}m) -> recede={9} cells (vol -{10:P0}), relMassErr={11:E2}, stepCPU={12:F2} ms",
                worldLabel, kind, session.PatchX, session.PatchY,
                peakInundated, peakFloodDepth, peakTime, peakMeanWetBed, peakMeanDryAdjBed,
                finalMetrics.InundatedDryCells, 1.0 - finalDryLandWater / Math.Max(1e-6, sumDryLandWaterAtPeak),
                finalMetrics.RelativeMassError, medianMs));
        }
    }

    static void TestIndependentTriggersLiveWeatherAndSave(WorldData world, StringBuilder log)
    {
        var copy = world.Clone();
        var sim = new FlatSimulation(copy);
        sim.SeedNations(2);
        sim.Step(1.0f);
        byte[] baselineSave = FlatSave.Encode(copy);

        // Run all three hazards on copy; global authoritative WorldData & save bytes must remain 100% identical & valid.
        var s1 = FlatWaterHazardSession.Create(copy, FlatHazardKind.RainFlood);
        s1.Step(60.0);
        var s2 = FlatWaterHazardSession.Create(copy, FlatHazardKind.TyphoonSurge);
        s2.Step(60.0);
        var s3 = FlatWaterHazardSession.Create(copy, FlatHazardKind.Tsunami);
        s3.Step(60.0);

        Check(s2.FocusTile != s3.FocusTile || s2.Kind != s3.Kind, "Typhoon and Tsunami must be independent hazard types");
        FlatSave.Validate(copy);
        byte[] duringSave = FlatSave.Encode(copy);
        Check(baselineSave.Length == duringSave.Length && baselineSave.AsSpan().SequenceEqual(duringSave),
            "Local SWE hazard mutated global WorldData or broke save byte-identity!");
        var decoded = FlatSave.Decode(duringSave);
        FlatSave.Validate(decoded);

        // Verify live cyclone detection triggers TyphoonSurge near landfall coast and NEVER triggers Tsunami.
        int coastTile = Array.FindIndex(copy.CoastDistance, d => d == 1);
        int inlandIdx = copy.Index(coastTile % copy.Width, coastTile / copy.Width);
        for (int i = 0; i < copy.Count; i++)
            if (copy.CoastDistance[i] == 1 && !FlatTerrainAnalysis.Ocean(copy.Water[i]) && copy.Elevation[i] <= 120 && Math.Abs(copy.Latitude(i)) < 45)
            { inlandIdx = i; break; }

        copy.Cyclones.North = copy.Cyclones.PreviousNorth = new FlatCycloneTrack
        {
            Tracked = true,
            U = (inlandIdx % copy.Width + 0.5) / copy.Width,
            V = (inlandIdx / copy.Width + 0.5) / copy.Height,
            PressureDeficit = 62.0,
            WindSpeed = 46.0,
            Phase = 1.2
        };
        bool detected = FlatWaterHazardSession.TryDetectLiveWeatherHazard(copy, 0, 0, true, out var liveKind, out int liveTile);
        Check(detected && liveKind == FlatHazardKind.TyphoonSurge && liveTile >= 0,
            $"Expected live landfalling cyclone to trigger TyphoonSurge, got detected={detected}, kind={liveKind}");
        Check(liveKind != FlatHazardKind.Tsunami, "Live weather must never trigger Tsunami");

        // Measure 10 Hz worker tick budget (simDt = 1.8 s per 0.1 s real frame)
        var liveSession = FlatWaterHazardSession.Create(copy, liveKind, liveTile, liveWeatherDriven: true);
        var tickTimes = new double[25];
        var halfBuf = new ushort[64 * 64 * 4];
        for (int k = 0; k < tickTimes.Length; k++)
        {
            long t0 = Stopwatch.GetTimestamp();
            liveSession.Step(1.8, copy.Cyclones.North, maxSubsteps: 24);
            liveSession.CaptureMetrics();
            liveSession.PackHalfRgba(halfBuf);
            tickTimes[k] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }
        Array.Sort(tickTimes);
        Check(tickTimes[12] < 5.0, $"10 Hz worker tick too slow: median={tickTimes[12]:F3} ms");
        log.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "PASS [5] Independent triggers, live cyclone coupling & save byte-identity verified; 64x64 10Hz worker tick median={0:F3} ms (p95={1:F3} ms)",
            tickTimes[12], tickTimes[23]));
    }

    static void TestReproducibleEarthStormScenariosAndLiveSwe(string resFolder, StringBuilder log)
    {
        var inv = CultureInfo.InvariantCulture;
        byte[] Asset(string name) => File.ReadAllBytes(resFolder + name + ".bytes");
        var surface = FlatGeographicSurface.Read(
            Asset("EarthGlobalStraitShore"), Asset("EarthOceanShore"),
            Asset("EarthMajorLakes"), Asset("EarthLakeDetail"), Asset("EarthPolarCoast"));
        var riversBytes = Asset("EarthRivers");

        foreach (int width in new[] { 256, 512 })
        {
            var campaignEarth = FlatGenerator.Generate(
                width, width / 2, FlatStormScenarios.CanonicalEarthSeed, TerrainPreset.Earth, default,
                Asset("Earth" + width), Asset("EarthWater" + width), riversBytes);
            surface.Attach(campaignEarth);
            campaignEarth.Day = 412.75; // Pre-existing player campaign date that must NEVER be mutated by demo preview!
            campaignEarth.Cyclones = FlatCycloneWeather.SpinUpAt(campaignEarth, campaignEarth.Day);
            byte[] campaignSaveBefore = FlatSave.Encode(campaignEarth);

            // Verify non-Earth preset cleanly rejects Earth geographic storm scenarios without mutating world
            var nonEarth = FlatGenerator.Generate(64, 32, "NON-EARTH-CHECK", TerrainPreset.Continents, default);
            double nonEarthDay = nonEarth.Day;
            Check(!FlatStormScenarios.TryStart(nonEarth, FlatStormScenario.EastChinaSeaLandfall, out int badTile) && badTile == -1 && nonEarth.Day == nonEarthDay,
                "Non-Earth world must cleanly reject Earth geographic scenario without mutating Day");

            foreach (var scenario in new[] { FlatStormScenario.EastChinaSeaLandfall, FlatStormScenario.NorthAtlanticLandfall })
            {
                // 6a. Isolated scenario world creation must leave campaignEarth (Day 412.75 & save bytes) 100% untouched
                bool created = FlatStormScenarios.TryCreateScenarioWorld(
                    campaignEarth, scenario, out var simWorld, out int coastalTile, out var plan);
                Check(created && simWorld != null && coastalTile >= 0, $"Failed to create {scenario} on Earth {width}");
                Check(campaignEarth.Day == 412.75 && campaignSaveBefore.AsSpan().SequenceEqual(FlatSave.Encode(campaignEarth)),
                    $"TryCreateScenarioWorld mutated source campaign world during {scenario}!");

                // Verify save/load byte-exactness of installed scenario world
                FlatSave.Validate(simWorld);
                byte[] scenarioSave = FlatSave.Encode(simWorld);
                var decodedScenario = FlatSave.Decode(scenarioSave);
                FlatSave.Validate(decodedScenario);
                Check(scenarioSave.AsSpan().SequenceEqual(FlatSave.Encode(decodedScenario)),
                    $"Scenario world save round-trip mismatch on {scenario}");

                // 6b. Verify surface tangential wind m/s contract at StartDay (0 at eye center, ~WindSpeed near RMW, 0 in far field)
                var initTrack = simWorld.Cyclones.North;
                var eyeSample = FlatStormScenarios.SampleSurfaceWind(simWorld, initTrack.U, initTrack.V, simWorld.Day);
                double cosLat = Math.Cos((initTrack.V - 0.5) * Math.PI);
                double duRmw = (58.0 / (40030.2 * Math.Max(0.2, cosLat)));
                var rmwEastSample = FlatStormScenarios.SampleSurfaceWind(simWorld, FlatScrollMath.Wrap(initTrack.U + duRmw, 1.0), initTrack.V, simWorld.Day);
                var farSample = FlatStormScenarios.SampleSurfaceWind(simWorld, FlatScrollMath.Wrap(initTrack.U + 0.35, 1.0), initTrack.V, simWorld.Day);
                Check(eyeSample.TangentialSpeedMs < 1.0f && eyeSample.PressureDeficitHpa > 15.0f,
                    $"Eye center wind/pressure sample invalid: vTan={eyeSample.TangentialSpeedMs:F2}, dP={eyeSample.PressureDeficitHpa:F2}");
                Check(rmwEastSample.Active && rmwEastSample.TangentialSpeedMs > initTrack.WindSpeed * 0.75 && rmwEastSample.NorthMs > 0f,
                    $"NH east-eyewall cyclonic wind must blow northward (+NorthMs): vTan={rmwEastSample.TangentialSpeedMs:F2}, N={rmwEastSample.NorthMs:F2}");
                Check(!farSample.Active && farSample.TangentialSpeedMs == 0f && farSample.PressureDeficitHpa == 0f && farSample.RainRateMmPerHour == 0f,
                    "Far-field surface cyclone wind sample must be strictly zero");

                // 6c. No-storm control on the exact same anchored coastal 64x64 patch: strictly zero fake tide or continuous water injection!
                var calmWorld = simWorld.Clone();
                surface.Attach(calmWorld);
                calmWorld.Cyclones = new FlatCycloneWeather { StepIndex = FlatCycloneWeather.StepAt(calmWorld.Day) };
                var calmSession = FlatWaterHazardSession.Create(calmWorld, FlatHazardKind.TyphoonSurge, coastalTile, liveWeatherDriven: true, applyInitialForcing: true);
                for (int k = 0; k < 25; k++)
                    calmSession.Step(24.0, default, maxSubsteps: 32, externalSurfaceRainKgM2Per6h: 0.0);
                var calmMetrics = calmSession.CaptureMetrics();
                Check(calmMetrics.InundatedDryCells == 0 && Math.Abs(calmMetrics.MaxSurfaceAnomalyMeters) < 1e-9 &&
                      calmMetrics.RainInputVolumeM3 == 0.0 && calmMetrics.ChannelInflowVolumeM3 == 0.0 &&
                      calmMetrics.BoundaryInflowVolumeM3 < 1e-9 && calmMetrics.RelativeMassError < 1e-12,
                    $"LiveWeatherDriven session invented fake tide/flood without a storm! inund={calmMetrics.InundatedDryCells}, anom={calmMetrics.MaxSurfaceAnomalyMeters:E3}");

                // 6d. End-to-end coupled forward evolution: pure FlatSimulation.Step(0.25f) + real-time 64x64 SWE + FlatMoistureCloudBudget surface rain!
                var mainlandMask = FlatStormScenarios.BuildContinentalMainlandMask(simWorld);
                var sim = new FlatSimulation(simWorld);
                var liveSwe = FlatWaterHazardSession.Create(simWorld, FlatHazardKind.TyphoonSurge, coastalTile, liveWeatherDriven: true, applyInitialForcing: true);
                var rainBudget = new FlatMoistureCloudBudget();
                double uPatch = FlatScrollMath.Wrap((liveSwe.PatchX + liveSwe.SpanTiles * 0.5) / simWorld.Width, 1.0);
                double vPatch = FMath.Clamp((float)((liveSwe.PatchY + liveSwe.SpanTiles * 0.5) / simWorld.Height), 0.01f, 0.99f);

                int preLandfallInundated = -1;
                double preLandfallDepth = 0.0;
                int peakInundated = 0;
                double peakDepth = 0.0, peakAreaKm2 = 0.0, peakMeanWetBed = 0.0, peakMeanDryAdjBed = 0.0, peakDryWaterSum = 0.0;
                double worstRelMassErr = 0.0;
                bool hitMainland = false;
                double actualLandfallDay = -1.0, actualLandfallLon = 0.0, actualLandfallLat = 0.0;
                double windAtStart = initTrack.WindSpeed, windPeakOcean = initTrack.WindSpeed, windPreLandfall = initTrack.WindSpeed, windAtLandfall = 0.0, windPost24h = 999.0;
                int oceanTicks = 0, postLandfallTicks = 0;

                // Advance 18 six-hour weather steps (4.5 days total: offshore approach -> mainland landfall -> inland dissipation & coastal surge recession)
                const int totalWeatherTicks = 18;
                for (int tick = 0; tick <= totalWeatherTicks; tick++)
                {
                    if (tick > 0) sim.Step(0.25f);
                    double nowDay = simWorld.Day;
                    var tr = simWorld.Cyclones.Sample(true, nowDay);
                    double lon = (tr.U - 0.5) * 360.0, lat = (tr.V - 0.5) * 180.0;
                    if (tr.Tracked)
                    {
                        FlatStormScenarios.ClassifyEarthSurface(simWorld, lon, lat, mainlandMask, out _, out bool isOcean, out bool isMain, out _, out _);
                        if (!hitMainland)
                        {
                            if (isOcean)
                            {
                                oceanTicks++;
                                if (tr.WindSpeed > windPeakOcean) windPeakOcean = tr.WindSpeed;
                                windPreLandfall = tr.WindSpeed;
                            }
                            else if (isMain)
                            {
                                hitMainland = true;
                                actualLandfallDay = nowDay;
                                actualLandfallLon = lon;
                                actualLandfallLat = lat;
                                windAtLandfall = tr.WindSpeed;
                            }
                        }
                        else
                        {
                            postLandfallTicks++;
                            if (postLandfallTicks == 4) windPost24h = tr.WindSpeed;
                        }
                    }
                    else if (hitMainland && postLandfallTicks < 4)
                    {
                        windPost24h = 0.0;
                    }

                    rainBudget.Solve(simWorld, nowDay, simWorld.Cyclones);
                    var sfcDiag = rainBudget.SamplePrecipitation(uPatch, vPatch);

                    // Step local 64x64 SWE over 150 hydrodynamic seconds per 6h synoptic frame (matching ~8s real gameplay at 18x SWE rate
                    // so shelf surge waves at c=sqrt(g*h)~12 m/s physically propagate across the 7.7 km coastal bay during approach & recession)
                    if (tick == 0)
                    {
                        var startTrack = simWorld.Cyclones.Sample(true, nowDay);
                        for (int sub = 0; sub < 3; sub++)
                            liveSwe.Step(20.0, startTrack, maxSubsteps: 32, externalSurfaceRainKgM2Per6h: sfcDiag.SurfacePrecipKgM2Per6h);
                    }
                    else
                    {
                        for (int sub = 0; sub < 5; sub++)
                        {
                            double subDay = nowDay - 0.25 + (sub + 1) * (0.25 / 5.0);
                            var subTrack = simWorld.Cyclones.Sample(true, subDay);
                            liveSwe.Step(30.0, subTrack, maxSubsteps: 32, externalSurfaceRainKgM2Per6h: sfcDiag.SurfacePrecipKgM2Per6h);
                        }
                    }

                    var m = liveSwe.CaptureMetrics();
                    if (m.RelativeMassError > worstRelMassErr) worstRelMassErr = m.RelativeMassError;
                    Check(m.RelativeMassError < 1e-11, $"{scenario} Earth{width} SWE mass error={m.RelativeMassError:E3} at day={nowDay:F2}");

                    double dryWater = 0.0;
                    for (int ci = 0; ci < liveSwe.Solver.Count; ci++)
                        if (!liveSwe.Solver.InitiallyWet[ci]) dryWater += liveSwe.Solver.Depth[ci];

                    if (tick == 0)
                    {
                        preLandfallInundated = m.InundatedDryCells;
                        preLandfallDepth = m.MaxFloodDepthMeters;
                    }
                    if (m.InundatedDryCells > peakInundated || (m.InundatedDryCells == peakInundated && m.MaxFloodDepthMeters > peakDepth))
                    {
                        peakInundated = m.InundatedDryCells;
                        peakDepth = m.MaxFloodDepthMeters;
                        peakAreaKm2 = m.InundatedAreaKm2;
                        peakMeanWetBed = m.MeanInundatedBedMeters;
                        peakMeanDryAdjBed = m.MeanDryAdjacentBedMeters;
                    }
                    if (dryWater > peakDryWaterSum) peakDryWaterSum = dryWater;
                }

                var endMetrics = liveSwe.CaptureMetrics();
                double endDryWater = 0.0;
                for (int ci = 0; ci < liveSwe.Solver.Count; ci++)
                    if (!liveSwe.Solver.InitiallyWet[ci]) endDryWater += liveSwe.Solver.Depth[ci];

                // Trajectory & mainland landfall assertions:
                Check(hitMainland && oceanTicks >= 3 && windPreLandfall >= 33.0 && windPost24h <= windPreLandfall * 0.35,
                    $"{scenario} Earth{width} failed mainland landfall or post-landfall decay: hit={hitMainland}, oceanTicks={oceanTicks}, preW={windPreLandfall:F1}, post24hW={windPost24h:F1}");
                if (scenario == FlatStormScenario.EastChinaSeaLandfall)
                {
                    Check(actualLandfallLon >= 117.0 && actualLandfallLon <= 122.9 && actualLandfallLat >= 24.0 && actualLandfallLat <= 33.0,
                        $"ECS scenario failed China Mainland target: ({actualLandfallLon:F2},{actualLandfallLat:F2})");
                }
                else
                {
                    Check(actualLandfallLon >= -97.5 && actualLandfallLon <= -78.5 && actualLandfallLat >= 25.2 && actualLandfallLat <= 35.5,
                        $"North Atlantic scenario failed North American Mainland target: ({actualLandfallLon:F2},{actualLandfallLat:F2})");
                }

                // Coupled SWE 3-stage assertions (pre-landfall < peak, real flood depth/area, lower-bed guidance, post-storm recession):
                Check(preLandfallInundated < peakInundated && peakInundated >= 18 && peakDepth >= 0.45,
                    $"{scenario} Earth{width} SWE failed pre-landfall -> peak surge progression: pre={preLandfallInundated} ({preLandfallDepth:F2}m) -> peak={peakInundated} ({peakDepth:F2}m)");
                Check(peakMeanWetBed < peakMeanDryAdjBed,
                    $"{scenario} Earth{width} SWE flood violated terrain bed ordering: wetBed={peakMeanWetBed:F2}m >= dryAdj={peakMeanDryAdjBed:F2}m");
                Check(endDryWater < peakDryWaterSum * 0.65 && endMetrics.InundatedDryCells < peakInundated && endMetrics.BoundaryOutflowVolumeM3 > 0.0,
                    $"{scenario} Earth{width} SWE failed post-storm recession: peakCells={peakInundated} -> endCells={endMetrics.InundatedDryCells}, dryVolRatio={endDryWater / Math.Max(1e-6, peakDryWaterSum):P1}");

                    log.AppendLine(string.Format(inv,
                    "PASS [6] Earth{0} {1,-22}: Start D{2:F2}({3:F2},{4:F2} {5:F1}m/s) -> OceanPeak {6:F1}m/s -> MainlandLandfall D{7:F2}({8:F2},{9:F2} tile={10}) {11:F1}->{12:F1}->24h:{13:F1}m/s | " +
                    "SWE Pre={14}c({15:F2}m) -> Peak={16}c({17:F2}km2, {18:F2}m) -> Recede={19}c(vol -{20:P0}) | NoStorm=0c | MaxRelMassErr={21:E2}",
                    width, scenario, plan.StartDay, plan.StartLonDeg, plan.StartLatDeg, windAtStart,
                    windPeakOcean, actualLandfallDay, actualLandfallLon, actualLandfallLat, coastalTile,
                    windPreLandfall, windAtLandfall, windPost24h,
                    preLandfallInundated, preLandfallDepth, peakInundated, peakAreaKm2, peakDepth,
                    endMetrics.InundatedDryCells, 1.0 - endDryWater / Math.Max(1e-6, peakDryWaterSum), worstRelMassErr));
            }
        }
    }

    static void TestSameWorldSnapshotPrecipitationAndSharedCalendar(string resFolder, StringBuilder log)
    {
        var inv = CultureInfo.InvariantCulture;
        byte[] Asset(string name) => File.ReadAllBytes(resFolder + name + ".bytes");
        var surface = FlatGeographicSurface.Read(
            Asset("EarthGlobalStraitShore"), Asset("EarthOceanShore"),
            Asset("EarthMajorLakes"), Asset("EarthLakeDetail"), Asset("EarthPolarCoast"));
        var baseEarth = FlatGenerator.Generate(
            512, 256, FlatStormScenarios.CanonicalEarthSeed, TerrainPreset.Earth, default,
            Asset("Earth512"), Asset("EarthWater512"), Asset("EarthRivers"));
        surface.Attach(baseEarth);

        // A. Verify FlatRegionalSnapshotPool strictly enforces same-world reference, exact 6h tick, and cyclone state
        FlatStormScenarios.TryCreateScenarioWorld(baseEarth, FlatStormScenario.EastChinaSeaLandfall, out var worldA, out int coastTile, out _);
        var otherWorldClone = worldA.Clone(); // Shares worldA.Moisture and TerrainRevision, but is a distinct WorldData instance!
        var pool = new FlatRegionalSnapshotPool();
        var budget = new FlatMoistureCloudBudget();
        var halfBuf = new ushort[FlatMoistureCloudBudget.Count * 4];
        var dummyCloud = new byte[FlatMoistureCloudBudget.Count * 4];

        double testDay = worldA.Day + 0.75; // Landfall 6h window D262.75
        var simProbe = new FlatSimulation(worldA.Clone());
        for (int k = 0; k < 3; k++) simProbe.Step(0.25f);
        long tickLf = FlatCycloneWeather.StepAt(testDay);
        budget.Solve(simProbe.World, testDay, simProbe.World.Cyclones);
        budget.PackPrecipitationHalf(halfBuf, out float sR, out float sS, out float nR, out float nS);

        int slotIdx = pool.AcquireUploadSlot();
        var slot = pool.GetSlot(slotIdx);
        Buffer.BlockCopy(halfBuf, 0, slot.PrecipHalf, 0, halfBuf.Length * sizeof(ushort));
        pool.CommitUpload(
            simProbe.World, slotIdx, tickLf, simProbe.World.TerrainRevision, simProbe.World.Moisture,
            true, false,
            in simProbe.World.Cyclones.North, in simProbe.World.Cyclones.South,
            in simProbe.World.Cyclones.PreviousNorth, in simProbe.World.Cyclones.PreviousSouth,
            0f, 0f, 0f, 0f, sR, sS, nR, nS);
        pool.BeginStageForStrip(simProbe.World, tickLf);
        pool.CommitStagedStrip();

        double uFocus = ((coastTile % 512) + 0.5) / 512.0;
        double vFocus = ((coastTile / 512) + 0.5) / 256.0;
        Check(pool.TrySamplePrecipitationForTick(simProbe.World, tickLf, uFocus, vFocus, out var exactDiag) && exactDiag.SurfacePrecipKgM2Per6h > 1.5f,
            "Expected published 6h snapshot to serve positive surface precipitation at landfall tick");
        Check(!pool.TrySamplePrecipitationForTick(otherWorldClone, tickLf, uFocus, vFocus, out _),
            "Snapshot pool must reject precipitation query from a different WorldData instance");
        Check(!pool.TrySamplePrecipitationForTick(simProbe.World, tickLf + 1, uFocus, vFocus, out _) &&
              !pool.TrySamplePrecipitationForTick(simProbe.World, tickLf - 1, uFocus, vFocus, out _),
            "Snapshot pool must reject stale or future 6h tick queries");

        // Verify actual 6h snapshot surface precipitation increases SWE rain/channel ledger vs dry-virga (0 kg/m^2/6h) snapshot
        var sweWithSnap = FlatWaterHazardSession.Create(simProbe.World, FlatHazardKind.RainFlood, -1, liveWeatherDriven: true, applyInitialForcing: true,
            initialCalendarDay: testDay, initialSurfaceRainKgM2Per6h: 5.2);
        var sweVirgaZero = FlatWaterHazardSession.Create(simProbe.World, FlatHazardKind.RainFlood, -1, liveWeatherDriven: true, applyInitialForcing: true,
            initialCalendarDay: testDay, initialSurfaceRainKgM2Per6h: 0.0);
        sweWithSnap.StepWithCalendar(120.0, testDay, default, null, publishedSurfaceRainKgM2Per6h: 5.2, maxSubsteps: 32);
        sweVirgaZero.StepWithCalendar(120.0, testDay, default, null, publishedSurfaceRainKgM2Per6h: 0.0, maxSubsteps: 32);
        var mWet = sweWithSnap.CaptureMetrics();
        var mDry = sweVirgaZero.CaptureMetrics();
        Check(sweWithSnap.LastPrecipitationFromPublishedSnapshot && sweWithSnap.LastPrecipitationTick == tickLf,
            "StepWithCalendar failed to record published 6h snapshot tick");
        Check(mWet.RainInputVolumeM3 > 0.0 && mWet.ChannelInflowVolumeM3 > 0.0 && mDry.RainInputVolumeM3 == 0.0 && mDry.ChannelInflowVolumeM3 == 0.0,
            $"6h surface precipitation vs virga=0 ledger mismatch: wetRain={mWet.RainInputVolumeM3:E2}, dryRain={mDry.RainInputVolumeM3:E2}");

        // A catch-up task may receive a whole six-hour slice. The CFL limit
        // in a deep coastal patch can require several batches of substeps.
        foreach (var scenario in new[] { FlatStormScenario.EastChinaSeaLandfall, FlatStormScenario.NorthAtlanticLandfall })
        {
            Check(FlatStormScenarios.TryCreateScenarioWorld(baseEarth, scenario, out var catchUpWorld, out int catchUpTile, out _),
                $"Cannot initialize {scenario} for a six-hour SWE catch-up");
            var catchUp = FlatWaterHazardSession.Create(catchUpWorld, FlatHazardKind.TyphoonSurge, catchUpTile, liveWeatherDriven: true);
            var sim = new FlatSimulation(catchUpWorld);
            const double secondsPerSixHours = .25 * FlatWaterHazardSession.LiveSweSecondsPerCalendarDay;
            for (int tick = 0; tick < 4; tick++)
            {
                sim.Step(.25f);
                double before = catchUp.Solver.Elapsed;
                var storm = catchUpWorld.Cyclones.Sample(true, catchUpWorld.Day);
                catchUp.StepWithCalendar(secondsPerSixHours, catchUpWorld.Day, storm, catchUpWorld.Cyclones, maxSubsteps: 48);
                double advanced = catchUp.Solver.Elapsed - before;
                Check(Math.Abs(advanced - secondsPerSixHours) < 1e-5,
                    $"{scenario} six-hour catch-up advanced {advanced:F3}s of {secondsPerSixHours:F3}s");
            }
        }

        // B. Shared FlatSolarClock calendar progression across 1x speed, pause, and 20x speed (3.0 simulated days):
        //    cyclone track, 6h snapshot tick, rainfall ledger, flood peak, and recession must advance along the same CalendarDay.
        (int peakCells, int endCells, double rainVol, double endElapsed, long endTick) RunCalendarCoupled(double speedMultiplier)
        {
            FlatStormScenarios.TryCreateScenarioWorld(baseEarth, FlatStormScenario.EastChinaSeaLandfall, out var wRun, out int cTile, out _);
            var simRun = new FlatSimulation(wRun);
            var clock = new FlatSolarClock();
            var snapPool = new FlatRegionalSnapshotPool();
            var snapBudget = new FlatMoistureCloudBudget();
            var snapHalf = new ushort[FlatMoistureCloudBudget.Count * 4];
            long uploadedTick = long.MinValue;

            void PublishSnapshotIfNeeded(double calDay)
            {
                long t = FlatCycloneWeather.StepAt(calDay);
                if (t == uploadedTick) return;
                var stormsSnap = FlatWaterHazardSession.CaptureCycloneSnapshotForDay(wRun, calDay);
                snapBudget.Solve(wRun, calDay, stormsSnap);
                snapBudget.PackPrecipitationHalf(snapHalf, out float sr, out float ss, out float nr, out float ns);
                int idx = snapPool.AcquireUploadSlot();
                Buffer.BlockCopy(snapHalf, 0, snapPool.GetSlot(idx).PrecipHalf, 0, snapHalf.Length * sizeof(ushort));
                FlatCycloneTrack nTr = stormsSnap != null ? stormsSnap.North : default;
                FlatCycloneTrack sTr = stormsSnap != null ? stormsSnap.South : default;
                FlatCycloneTrack pnTr = stormsSnap != null ? stormsSnap.PreviousNorth : default;
                FlatCycloneTrack psTr = stormsSnap != null ? stormsSnap.PreviousSouth : default;
                bool spin = stormsSnap != null && wRun.Cyclones != null && stormsSnap.StepIndex != wRun.Cyclones.StepIndex;
                // Bind to current wRun state at tick t
                snapPool.CommitUpload(wRun, idx, t, wRun.TerrainRevision, wRun.Moisture, stormsSnap != null, spin,
                    in nTr, in sTr, in pnTr, in psTr, 0f, 0f, 0f, 0f, sr, ss, nr, ns);
                snapPool.BeginStageForStrip(wRun, t);
                snapPool.CommitStagedStrip();
                uploadedTick = t;
            }

            double startCalDay = clock.RenderDay(wRun.Day);
            PublishSnapshotIfNeeded(startCalDay);
            snapPool.TrySamplePrecipitationForTick(wRun, FlatCycloneWeather.StepAt(startCalDay), uFocus, vFocus, out var d0);
            var swe = FlatWaterHazardSession.Create(wRun, FlatHazardKind.TyphoonSurge, cTile, liveWeatherDriven: true,
                applyInitialForcing: true, initialCalendarDay: startCalDay, initialSurfaceRainKgM2Per6h: d0.SurfacePrecipKgM2Per6h);

            double lastCalDay = startCalDay;
            double pendingDays = 0.0;
            int peak = 0;
            const double targetSimDays = 3.0;
            const double realDt = 0.10; // 10 Hz worker cadence
            int totalRealTicks = (int)Math.Ceiling(targetSimDays * 120.0 / (speedMultiplier * realDt));

            for (int r = 0; r < totalRealTicks; r++)
            {
                // Insert a 10-frame pause mid-storm to prove zero drift across cyclone, precip tick, and SWE!
                if (r == totalRealTicks / 4)
                {
                    double pausedDayBefore = clock.RenderDay(wRun.Day);
                    double pausedVolBefore = swe.Solver.Volume();
                    long pausedTickBefore = swe.LastPrecipitationTick;
                    for (int p = 0; p < 10; p++)
                        clock.Advance(realDt, speedMultiplier, 120.0, halted: true, simRun.Step);
                    Check(Math.Abs(clock.RenderDay(wRun.Day) - pausedDayBefore) < 1e-12 &&
                          Math.Abs(swe.Solver.Volume() - pausedVolBefore) < 1e-12 &&
                          swe.LastPrecipitationTick == pausedTickBefore,
                        "Pause failed to hold calendar, precipitation tick, and SWE state identically");
                }

                clock.Advance(realDt, speedMultiplier, 120.0, halted: false, simRun.Step);
                double calNow = clock.RenderDay(wRun.Day);
                pendingDays += calNow - lastCalDay;
                lastCalDay = calNow;

                while (pendingDays > 1e-7)
                {
                    double slice = Math.Min(0.25, pendingDays);
                    pendingDays -= slice;
                    double stepDay = calNow - pendingDays;
                    long stepTick = FlatCycloneWeather.StepAt(stepDay);
                    PublishSnapshotIfNeeded(stepDay);
                    var stormsSnap = FlatWaterHazardSession.CaptureCycloneSnapshotForDay(wRun, stepDay);
                    var cyc = FlatWaterHazardSession.SampleRelevantCycloneFromState(stormsSnap, wRun, swe.FocusTile, stepDay);
                    double pubRain = snapPool.TrySamplePrecipitationForTick(wRun, stepTick, swe.FocusU, swe.FocusV, out var pd)
                        ? pd.SurfacePrecipKgM2Per6h : -1.0;
                    swe.StepWithCalendar(slice * FlatWaterHazardSession.LiveSweSecondsPerCalendarDay, stepDay, cyc, stormsSnap, pubRain, maxSubsteps: 48);
                    Check(swe.LastPrecipitationTick == stepTick, $"SWE precipitation tick {swe.LastPrecipitationTick} lagged calendar tick {stepTick}");
                    var m = swe.CaptureMetrics();
                    Check(m.RelativeMassError < 1e-11, $"Calendar-coupled mass error={m.RelativeMassError:E3} at {speedMultiplier}x");
                    if (m.InundatedDryCells > peak) peak = m.InundatedDryCells;
                }
            }
            var endM = swe.CaptureMetrics();
            return (peak, endM.InundatedDryCells, endM.RainInputVolumeM3, swe.Solver.Elapsed, swe.LastPrecipitationTick);
        }

        var run1x = RunCalendarCoupled(1.0);
        var run20x = RunCalendarCoupled(20.0);
        Check(Math.Abs(run1x.endElapsed - 1800.0) < 0.05 && Math.Abs(run20x.endElapsed - 1800.0) < 0.05 && run1x.endTick == run20x.endTick,
            $"1x vs 20x calendar hydrodynamic time mismatch: 1x={run1x.endElapsed:F4}s (tick {run1x.endTick}) vs 20x={run20x.endElapsed:F4}s (tick {run20x.endTick})");
        Check(run1x.peakCells >= 120 && run20x.peakCells >= 120 && run1x.endCells <= run1x.peakCells / 4 && run20x.endCells <= run20x.peakCells / 4,
            $"1x/20x calendar-coupled surge or recession failed: 1x({run1x.peakCells}->{run1x.endCells}), 20x({run20x.peakCells}->{run20x.endCells})");
        Check(run1x.rainVol > 1e4 && run20x.rainVol > 1e4,
            $"Expected positive 6h surface rainfall volume in both 1x ({run1x.rainVol:E2}) and 20x ({run20x.rainVol:E2})");

        log.AppendLine(string.Format(inv,
            "PASS [7] Same-world 6h snapshot precip -> SWE & shared calendar lockstep: 1x({0}->{1}c, rain={2:E2}m3, {3:F0}s) vs 20x({4}->{5}c, rain={6:E2}m3, {7:F0}s), pause drift=0",
            run1x.peakCells, run1x.endCells, run1x.rainVol, run1x.endElapsed,
            run20x.peakCells, run20x.endCells, run20x.rainVol, run20x.endElapsed));
    }
}
