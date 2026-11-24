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

        // 5. Independent triggers (Typhoon vs Tsunami), live weather detection & save-format byte identity
        TestIndependentTriggersLiveWeatherAndSave(worldContinents, report);

        string summary = string.Format(inv,
            "PASS WaterHazardsRegression: {0} assertions passed (hydrostatic rest, levee non-breach, tsunami sqrt(g*h), 3 scenarios advance/recede, mass ledger, live weather & save compatibility).",
            assertions);
        report.AppendLine(summary);
        Console.Write(report.ToString());

        string outPath = string.IsNullOrWhiteSpace(jsonReportPath)
            ? "ValidationOutput/WaterHazards/dotnet-hazards-report.txt"
            : jsonReportPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        File.WriteAllText(outPath, report.ToString(), Encoding.UTF8);
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
}
