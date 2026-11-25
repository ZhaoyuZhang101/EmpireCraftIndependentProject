using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Orbis.Flat;

/// <summary>
/// Controlled fixed-location, fixed-time, fixed-seed regression suite proving end-to-end climate-to-volumetric-cloud
/// wiring across both Earth and a procedural fictional world:
///   1. NOAA zonal calibration & Earth regional baseline + daily synoptic build performance
///   2. Synoptic vertical-motion restoration (low-P / convergence vs high-P / divergence + dry-air safety gate)
///   3. Humidity / CAPE / CIN / land soil moisture -> deep convection, anvil high cloud, and cloud-top altitude
///   4. Surface wind, orographic windward/leeward contrast, 3D cloud drift velocity, and baroclinic thermal fronts
///   5. Cold vs warm SST anomalies -> marine stratocumulus inversion deck vs warm convective destabilization
///   6. Seasonal migration & 1.0x vs 0.45x planetary rotation cloud-belt, jet, and wind-belt shift
///   7. Sky cache refresh cadence, seasonal/cross-year continuity, regional statistics, cyclone state, and FlatSave compatibility
/// </summary>
public static class CloudForcingRegression
{
    const int GridW = 256, GridH = 128;

    readonly struct BakedCloudSample
    {
        public readonly float Low, High, Convection, Storm, BaseM, TopM;
        public BakedCloudSample(float low, float high, float convection, float storm, float baseM, float topM)
        {
            Low = low; High = high; Convection = convection; Storm = storm; BaseM = baseM; TopM = topM;
        }
    }

    public static void Run()
    {
        var earth = BuildEarth256();
        var fictional = FlatGenerator.Generate(GridW, GridH, "ORBIS-CLOUD-FORCING-2026", TerrainPreset.Continents, default);

        VerifyEarthBaselineAndPerf(earth);
        VerifySynopticLiftRestoration(earth, "Earth", lonDeg: -30f, latDeg: 48f, day: 195.0);
        VerifySynopticLiftRestoration(fictional, "Fictional-Continents", FindMoistMidlatSite(fictional, out float fLonM, out float fLatM), fLonM, fLatM, day: 195.0);

        VerifyHumidityCapeAndLandMoisture(earth, "Earth", lonDeg: -60f, latDeg: -3f, day: 195.0);
        VerifyHumidityCapeAndLandMoisture(fictional, "Fictional-Continents", FindWarmLandSite(fictional, out float fLonL, out float fLatL), fLonL, fLatL, day: 195.0);

        VerifyWindOrographyAndFronts(earth, "Earth", day: 195.0);
        VerifyWindOrographyAndFronts(fictional, "Fictional-Continents", day: 195.0);

        VerifyColdWarmSstMarineLowCloud(earth, "Earth", lonDeg: -79f, latDeg: -18f, day: 195.0);
        VerifyColdWarmSstMarineLowCloud(fictional, "Fictional-Continents", FindSubtropOceanSite(fictional, out float fLonS, out float fLatS), fLonS, fLatS, day: 195.0);

        VerifySeasonAndRotationBelts(earth, "Earth", day: 195.0);
        VerifySeasonAndRotationBelts(fictional, "Fictional-Continents", day: 195.0);

        VerifyCacheContinuityRegionalCycloneAndSave(earth, fictional);
        VerifyAsyncSchedulerPerformanceAndRaces(earth, fictional);

        Console.WriteLine("PASS cloud-forcing regression: Earth + fictional world synoptic dynamics, CAPE/humidity/soil convection, wind/front/orography, cold/warm SST Sc, 1x vs 0.45x rotation belts, cache cadence, seasonal/interannual continuity, regional/cyclone wiring, save compatibility, and async background forcing scheduler (zero-alloc, coalescing, race safety).");
    }

    static WorldData BuildEarth256()
    {
        const string folder = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        var raw = File.ReadAllBytes(folder + "Earth256.bytes");
        var water = File.ReadAllBytes(folder + "EarthWater256.bytes");
        return FlatGenerator.Generate(GridW, GridH, "EARTH-CLOUD-FORCING", TerrainPreset.Earth, default, raw, water);
    }

    static void VerifyEarthBaselineAndPerf(WorldData earth)
    {
        var f = new byte[GridW * GridH * 4];
        var a = new byte[GridW * GridH * 4];
        var u = new ushort[GridW * GridH * 4];

        // Warmup + measure daily synoptic build time (forcing+aux only, as run on 1-day cadence)
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 195.0, GridW, GridH, f, a, u, 195.0);
        var sw = Stopwatch.StartNew();
        const int runs = 5;
        for (int i = 0; i < runs; i++)
            GlebaGeographySupplement.BuildCloudSynopticFields(earth, 195.0 + i, GridW, GridH, f, a, null, 195.0);
        sw.Stop();
        double dailyMs = sw.Elapsed.TotalMilliseconds / runs;

        // 12-month NOAA 1991-2020 zonal check
        double[] noaaZonal18 = { 24.3, 46.6, 62.9, 63.0, 59.3, 51.4, 48.1, 53.9, 57.9, 55.8, 44.6, 44.6, 48.1, 52.7, 53.8, 47.6, 43.6, 43.8 };
        var simZonal18 = new double[18];
        var simCnt = new int[18];
        for (int m = 0; m < 12; m++)
        {
            GlebaGeographySupplement.BuildCloudSynopticFields(earth, m * 30.0 + 15.0, GridW, GridH, f, a, null);
            for (int y = 0; y < GridH; y++)
            {
                float lat = ((y + 0.5f) / GridH - 0.5f) * 180f;
                int bin = FMath.Clamp((int)Math.Floor((lat + 85.0) / 10.0), 0, 17);
                for (int x = 0; x < GridW; x++)
                {
                    simZonal18[bin] += (f[(y * GridW + x) * 4] / 255.0) * 100.0;
                    simCnt[bin]++;
                }
            }
        }
        double wSum = 0, mae = 0, mObs = 0, mSim = 0;
        var wArr = new double[18];
        for (int b = 0; b < 18; b++)
        {
            simZonal18[b] /= Math.Max(1, simCnt[b]);
            double midLat = Math.Clamp(-80.0 + b * 10.0, -88.0, 88.0);
            wArr[b] = Math.Cos(midLat * Math.PI / 180.0);
            wSum += wArr[b];
            mae += wArr[b] * Math.Abs(simZonal18[b] - noaaZonal18[b]);
            mObs += wArr[b] * noaaZonal18[b];
            mSim += wArr[b] * simZonal18[b];
        }
        mObs /= wSum; mSim /= wSum;
        double cov = 0, vO = 0, vS = 0;
        for (int b = 0; b < 18; b++)
        {
            double dO = noaaZonal18[b] - mObs, dS = simZonal18[b] - mSim;
            cov += wArr[b] * dO * dS; vO += wArr[b] * dO * dO; vS += wArr[b] * dS * dS;
        }
        double consistency = 100.0 - mae / wSum;
        double r = cov / Math.Sqrt(Math.Max(1e-9, vO * vS));

        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 195.0, GridW, GridH, f, a, null);
        double peruSc = RegionMean(a, 0, -84f, -75f, -24f, -10f);
        double benguelaSc = RegionMean(a, 0, 6f, 13f, -26f, -14f);
        double kuroshioSc = RegionMean(a, 0, 130f, 142f, 24f, 32f);
        double subGyreSc = RegionMean(a, 0, 150f, 178f, 18f, 28f);

        Console.WriteLine($"[1. Baseline & Perf] Daily BuildCloudSynopticFields(256x128)={dailyMs:F1} ms | NOAA Mean={mSim:F1}% (Obs={mObs:F1}%), Consistency={consistency:F2}%, r={r:F4}");
        Console.Write("  Zonal 18-bin Sim vs Obs: ");
        for (int b = 0; b < 18; b++)
            Console.Write($"{(-80 + b * 10):+00;-00}:{simZonal18[b]:F0}/{noaaZonal18[b]:F0} ");
        Console.WriteLine();
        Console.WriteLine($"  Earth Jul MarineSc: Peru={peruSc:F3}, Benguela={benguelaSc:F3} (scProtect>0.38) vs Kuroshio={kuroshioSc:F3}, WestPacGyre={subGyreSc:F3}");

        Check(dailyMs < 250.0, $"Daily synoptic cloud build too slow: {dailyMs:F1} ms");
        Check(consistency >= 92.0 && r >= 0.89, $"NOAA calibration degraded: consistency={consistency:F2}%, r={r:F4}");
        Check(peruSc > 0.39 && benguelaSc > 0.39 && kuroshioSc < 0.22 && subGyreSc < 0.35,
            $"Marine Sc threshold separation failed: Peru={peruSc:F3}, Benguela={benguelaSc:F3}, Kuroshio={kuroshioSc:F3}, Gyre={subGyreSc:F3}");
    }

    static void VerifySynopticLiftRestoration(WorldData baseWorld, string label, int dummyTile, float lonDeg, float latDeg, double day)
        => VerifySynopticLiftRestoration(baseWorld, label, lonDeg, latDeg, day);

    static void VerifySynopticLiftRestoration(WorldData baseWorld, string label, float lonDeg, float latDeg, double day)
    {
        int cx = LonToX(lonDeg), cy = LatToY(latDeg);
        BakedCloudSample lowOut, highOut, dryOut;
        (float r, float g, float b, float a) fLow, fHigh, fDry, aDry;

        using (var snap = new ClimateSnapshot(baseWorld))
        {
            SetPatch(baseWorld, cx, cy, 3, rainMm: 85f, tempC: 16f, rh: 0.72f, cond: 2.2f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 982f, rainImpact: 0.38f, convSign: +1f);
            lowOut = BuildAndSample(baseWorld, day, cx, cy, regionalX: 0f, regionalY: 0f, out fLow, out _);

            snap.Restore();
            SetPatch(baseWorld, cx, cy, 3, rainMm: 85f, tempC: 16f, rh: 0.72f, cond: 2.2f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 1034f, rainImpact: -0.38f, convSign: -1f);
            highOut = BuildAndSample(baseWorld, day, cx, cy, regionalX: 0f, regionalY: 0f, out fHigh, out _);

            snap.Restore();
            SetPatch(baseWorld, cx, cy, 3, rainMm: 0f, tempC: 16f, rh: 0f, cond: 0f, cape: 2200f, cin: 0.10f, deepSoil: 0f, shallowSoil: 0f, convRain: 0f, stratRain: 0f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 978f, rainImpact: 0.45f, convSign: +1f);
            // Note: RegionalCloudResponse.From multiplies positive cover by rh (=0 here); pass regionalY=+1 to also prove storm=0 without wet.
            dryOut = BuildAndSample(baseWorld, day, cx, cy, regionalX: 0f, regionalY: 1f, out fDry, out aDry);
        }

        Console.WriteLine($"[2. Synoptic Lift Fix | {label} ({lonDeg:F1},{latDeg:F1}) day={day:F0}] " +
            $"LowP+Conv lift={fLow.r:F3} low={lowOut.Low:F3} | HighP+Div lift={fHigh.r:F3} low={highOut.Low:F3} (dLift={fLow.r - fHigh.r:+0.000}, dLow={lowOut.Low - highOut.Low:+0.000}) | " +
            $"Dry+LowP lift={fDry.r:F3} lee={aDry.b:F3} track={fDry.b:F3} moist={fDry.a:F3} low={dryOut.Low:F3} storm={dryOut.Storm:F3}");

        Check(fLow.r > fHigh.r + 0.14f && lowOut.Low > highOut.Low + 0.08f,
            $"{label}: synopticLift overwrite still suppressing low-P/convergence contrast: lowP={fLow.r:F3} vs highP={fHigh.r:F3}, bakedLow={lowOut.Low:F3} vs {highOut.Low:F3}");
        Check(fDry.r <= 0.06f && fDry.g <= 0.02f && fDry.b <= 0.02f && fDry.a <= 0.02f && aDry.b >= 0.90f && dryOut.Storm <= 1e-4f && dryOut.Low < lowOut.Low * 0.35f,
            $"{label}: dry air produced fake cloud/rain despite zero moisture: lift={fDry.r:F3}, lee={aDry.b:F3}, low={dryOut.Low:F3}, storm={dryOut.Storm:F3}");
    }

    static void VerifyHumidityCapeAndLandMoisture(WorldData baseWorld, string label, int dummyTile, float lonDeg, float latDeg, double day)
        => VerifyHumidityCapeAndLandMoisture(baseWorld, label, lonDeg, latDeg, day);

    static void VerifyHumidityCapeAndLandMoisture(WorldData baseWorld, string label, float lonDeg, float latDeg, double day)
    {
        int cx = LonToX(lonDeg), cy = LatToY(latDeg);
        BakedCloudSample bCap, bDeep, bIsoLo, bIsoHi;
        (float r, float g, float b, float a) fCap, fDeep, fIsoLo, fIsoHi;

        using (var snap = new ClimateSnapshot(baseWorld))
        {
            SetPatch(baseWorld, cx, cy, 3, rainMm: 18f, tempC: 27f, rh: 0.26f, cond: 0.3f, cape: 120f, cin: 0.90f, deepSoil: 0.06f, shallowSoil: 0.05f, convRain: 4f, stratRain: 14f, virga: 38f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 1014f, rainImpact: -0.05f, convSign: 0f);
            bCap = BuildAndSample(baseWorld, day, cx, cy, 0f, 0f, out fCap, out _);

            snap.Restore();
            SetPatch(baseWorld, cx, cy, 3, rainMm: 220f, tempC: 27f, rh: 0.88f, cond: 7.0f, cape: 2200f, cin: 0.14f, deepSoil: 0.86f, shallowSoil: 0.80f, convRain: 195f, stratRain: 25f, virga: 1f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 1004f, rainImpact: 0.25f, convSign: +0.6f);
            bDeep = BuildAndSample(baseWorld, day, cx, cy, 0f, 0f, out fDeep, out _);

            snap.Restore();
            SetPatch(baseWorld, cx, cy, 3, rainMm: 90f, tempC: 25.5f, rh: 0.64f, cond: 2.5f, cape: 120f, cin: 0.92f, deepSoil: 0.08f, shallowSoil: 0.08f, convRain: 10f, stratRain: 80f, virga: 30f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 1009f, rainImpact: 0.05f, convSign: 0f);
            bIsoLo = BuildAndSample(baseWorld, day, cx, cy, 0f, 0f, out fIsoLo, out _);

            snap.Restore();
            SetPatch(baseWorld, cx, cy, 3, rainMm: 90f, tempC: 25.5f, rh: 0.64f, cond: 2.5f, cape: 2200f, cin: 0.14f, deepSoil: 0.88f, shallowSoil: 0.82f, convRain: 82f, stratRain: 8f, virga: 0f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 1009f, rainImpact: 0.05f, convSign: 0f);
            bIsoHi = BuildAndSample(baseWorld, day, cx, cy, 0f, 0f, out fIsoHi, out _);
        }

        Console.WriteLine($"[3. Humidity/CAPE/Soil | {label} ({lonDeg:F1},{latDeg:F1}) day={day:F0}] " +
            $"Dry/LowCAPE: itcz={fCap.g:F3} moist={fCap.a:F3} low={bCap.Low:F3} high={bCap.High:F3} type={bCap.Convection:F3} top={bCap.TopM:F0}m | " +
            $"Humid/HighCAPE: itcz={fDeep.g:F3} moist={fDeep.a:F3} low={bDeep.Low:F3} high={bDeep.High:F3} type={bDeep.Convection:F3} storm={bDeep.Storm:F3} top={bDeep.TopM:F0}m");
        Console.WriteLine($"  Isolated CAPE/CIN/Soil (fixed RH/Rain): itcz {fIsoLo.g:F3}->{fIsoHi.g:F3}, lift {fIsoLo.r:F3}->{fIsoHi.r:F3}, moist {fIsoLo.a:F3}->{fIsoHi.a:F3}, type {bIsoLo.Convection:F3}->{bIsoHi.Convection:F3}, top {bIsoLo.TopM:F0}m->{bIsoHi.TopM:F0}m");

        Check(fDeep.g > fCap.g + 0.45f && fDeep.a > fCap.a + 0.45f && bDeep.High > bCap.High + 0.25f && bDeep.Convection > bCap.Convection + 0.35f && bDeep.TopM > bCap.TopM + 4000f && bDeep.Storm > 0.15f,
            $"{label}: Humidity/CAPE deep convection failed to lift cloud type/anvil/top: top {bCap.TopM:F0}->{bDeep.TopM:F0}m, type {bCap.Convection:F3}->{bDeep.Convection:F3}");
        Check(fIsoHi.g > fIsoLo.g + 0.15f && fIsoHi.r > fIsoLo.r + 0.08f && bIsoHi.Convection > bIsoLo.Convection + 0.10f && bIsoHi.TopM > bIsoLo.TopM + 1200f,
            $"{label}: Isolated ExoPlaSim CAPE/CIN/Soil did not independently drive convection/altitude: itcz {fIsoLo.g:F3}->{fIsoHi.g:F3}, top {bIsoLo.TopM:F0}->{bIsoHi.TopM:F0}m");
    }

    static void VerifyWindOrographyAndFronts(WorldData baseWorld, string label, double day)
    {
        int ridgeX, ridgeY;
        float dhDx, dhDy;
        FindSteepSlopeSite(baseWorld, out ridgeX, out ridgeY, out dhDx, out dhDy);
        float slopeLen = (float)Math.Sqrt(dhDx * dhDx + dhDy * dhDy);
        float upE = dhDx / slopeLen * 0.90f, upN = dhDy / slopeLen * 0.90f;

        BakedCloudSample bWind, bLee, bFlat, bFront;
        (float r, float g, float b, float a) fWind, aWind, fLee, aLee, fFlat, fFront;
        var season = new FlatMonthlyForcing(day);
        var wRgbaA = new float[GridW * GridH * 4];
        var wRgbaB = new float[GridW * GridH * 4];
        var velA = new float[GridW * GridH * 4];
        var velB = new float[GridW * GridH * 4];

        using (var snap = new ClimateSnapshot(baseWorld))
        {
            SetPatch(baseWorld, ridgeX, ridgeY, 4, rainMm: 80f, tempC: 14f, rh: 0.68f, cond: 2.0f, windE: upE, windN: upN);
            SetSynopticPressureAndConvergence(baseWorld, ridgeX, ridgeY, slpHpa: 1010f, rainImpact: 0f, convSign: 0f, overrideWind: false);
            bWind = BuildAndSample(baseWorld, day, ridgeX, ridgeY, 0f, 0f, out fWind, out aWind);
            FlatSkyForcingCache.PackSeasonalWindRgba(baseWorld, season, GridW, GridH, wRgbaA);
            FlatSkyForcingCache.PackCloudDriftVelocityRgba(wRgbaA, season.SegmentBlend, GridW, GridH, velA);

            snap.Restore();
            SetPatch(baseWorld, ridgeX, ridgeY, 4, rainMm: 80f, tempC: 14f, rh: 0.68f, cond: 2.0f, windE: -upE, windN: -upN);
            SetSynopticPressureAndConvergence(baseWorld, ridgeX, ridgeY, slpHpa: 1010f, rainImpact: 0f, convSign: 0f, overrideWind: false);
            bLee = BuildAndSample(baseWorld, day, ridgeX, ridgeY, 0f, 0f, out fLee, out aLee);
            FlatSkyForcingCache.PackSeasonalWindRgba(baseWorld, season, GridW, GridH, wRgbaB);
            FlatSkyForcingCache.PackCloudDriftVelocityRgba(wRgbaB, season.SegmentBlend, GridW, GridH, velB);

            snap.Restore();
            int fx = LonToX(label == "Earth" ? -35f : 40f), fy = LatToY(40f);
            SetPatch(baseWorld, fx, fy, 4, rainMm: 45f, tempC: 14f, rh: 0.68f, cond: 1.4f, windE: 0.45f, windN: 0.10f);
            SetSynopticPressureAndConvergence(baseWorld, fx, fy, slpHpa: 1012f, rainImpact: 0f, convSign: 0f, overrideWind: false);
            bFlat = BuildAndSample(baseWorld, day, fx, fy, 0f, 0f, out fFlat, out _);

            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int ty = FMath.Clamp(fy + dy, 0, GridH - 1);
                int tx = FMath.Wrap(fx + dx, GridW);
                float tVal = 14f - dy * 4.8f;
                int o12 = (ty * GridW + tx) * 12;
                for (int m = 0; m < 12; m++) baseWorld.Temperature[o12 + m] = tVal;
            }
            bFront = BuildAndSample(baseWorld, day, fx, fy, 0f, 0f, out fFront, out _);
        }

        int vp = (ridgeY * GridW + ridgeX) * 4;
        float speedA = (float)Math.Sqrt(velA[vp] * velA[vp] + velA[vp + 1] * velA[vp + 1] + velA[vp + 2] * velA[vp + 2]);
        float speedB = (float)Math.Sqrt(velB[vp] * velB[vp] + velB[vp + 1] * velB[vp + 1] + velB[vp + 2] * velB[vp + 2]);
        float driftCos = (velA[vp] * velB[vp] + velA[vp + 1] * velB[vp + 1] + velA[vp + 2] * velB[vp + 2]) / Math.Max(1e-7f, speedA * speedB);

        float rLon = ((ridgeX + 0.5f) / GridW - 0.5f) * 360f, rLat = ((ridgeY + 0.5f) / GridH - 0.5f) * 180f;
        Console.WriteLine($"[4a. Orographic Windward/Leeward | {label} ({rLon:F1},{rLat:F1}) day={day:F0}] " +
            $"Windward: orog={aWind.g:F3} lee={aWind.b:F3} lift={fWind.r:F3} low={bWind.Low:F3} type={bWind.Convection:F3} | " +
            $"Leeward: orog={aLee.g:F3} lee={aLee.b:F3} lift={fLee.r:F3} low={bLee.Low:F3} | DriftCos={driftCos:F3} spd={speedA:F4}");

        Check(aWind.g > aLee.g + 0.25f && aLee.b > aWind.b + 0.18f && fWind.r > fLee.r + 0.14f && bWind.Low > bLee.Low + 0.05f,
            $"{label}: Orographic wind reversal contrast too weak: orog {aWind.g:F3}/{aLee.g:F3}, lee {aWind.b:F3}/{aLee.b:F3}, lift {fWind.r:F3}/{fLee.r:F3}, low {bWind.Low:F3}/{bLee.Low:F3}");
        Check(speedA > 0.005f && driftCos < -0.95f,
            $"{label}: 3D cloud drift velocity failed to reverse with wind: speed={speedA:F5}, cos={driftCos:F3}");

        Console.WriteLine($"[4b. Baroclinic Front | {label} (lat=40N) day={day:F0}] " +
            $"Calm: stormTrack={fFlat.b:F3} lift={fFlat.r:F3} low={bFlat.Low:F3} high={bFlat.High:F3} | " +
            $"Front: stormTrack={fFront.b:F3} lift={fFront.r:F3} low={bFront.Low:F3} high={bFront.High:F3} (dTrack={fFront.b - fFlat.b:+0.000}, dLift={fFront.r - fFlat.r:+0.000}, dHigh={bFront.High - bFlat.High:+0.000})");

        Check(fFront.b > fFlat.b + 0.06f && fFront.r > fFlat.r + 0.04f && bFront.High > bFlat.High + 0.02f && (bFront.Low + bFront.High) > (bFlat.Low + bFlat.High) + 0.03f,
            $"{label}: Baroclinic thermal front failed to strengthen stormTrack/lift/cloud: track {fFlat.b:F3}->{fFront.b:F3}, lift {fFlat.r:F3}->{fFront.r:F3}, low {bFlat.Low:F3}->{bFront.Low:F3}, high {bFlat.High:F3}->{bFront.High:F3}");
    }

    static void VerifyColdWarmSstMarineLowCloud(WorldData baseWorld, string label, int dummyTile, float lonDeg, float latDeg, double day)
        => VerifyColdWarmSstMarineLowCloud(baseWorld, label, lonDeg, latDeg, day);

    static void VerifyColdWarmSstMarineLowCloud(WorldData baseWorld, string label, float lonDeg, float latDeg, double day)
    {
        int cx = LonToX(lonDeg), cy = LatToY(latDeg);
        BakedCloudSample bCold, bWarm, bPC, bPW;
        (float r, float g, float b, float a) fCold, aCold, fWarm, aWarm, fPC, aPC, fPW, aPW;

        using (var snap = new ClimateSnapshot(baseWorld))
        {
            SetPatch(baseWorld, cx, cy, 4, rainMm: 22f, tempC: 18.5f, rh: 0.78f, cond: 0.8f, cape: 140f, cin: 0.72f, sstAnom: -2.4f, curNorth: latDeg < 0 ? 0.14f : -0.14f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 1020f, rainImpact: -0.12f, convSign: -0.2f);
            bCold = BuildAndSample(baseWorld, day, cx, cy, 0f, 0f, out fCold, out aCold);

            snap.Restore();
            SetPatch(baseWorld, cx, cy, 4, rainMm: 145f, tempC: 25.5f, rh: 0.82f, cond: 4.5f, cape: 1650f, cin: 0.22f, sstAnom: +2.4f, curNorth: latDeg < 0 ? -0.14f : 0.14f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 1007f, rainImpact: 0.18f, convSign: +0.3f);
            bWarm = BuildAndSample(baseWorld, day, cx, cy, 0f, 0f, out fWarm, out aWarm);

            snap.Restore();
            SetPatch(baseWorld, cx, cy, 4, rainMm: 65f, tempC: 21.5f, rh: 0.76f, cond: 1.6f, cape: 450f, cin: 0.55f, sstAnom: -2.4f, curNorth: latDeg < 0 ? 0.12f : -0.12f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 1018f, rainImpact: 0f, convSign: 0f);
            bPC = BuildAndSample(baseWorld, day, cx, cy, 0f, 0f, out fPC, out aPC);

            snap.Restore();
            SetPatch(baseWorld, cx, cy, 4, rainMm: 65f, tempC: 21.5f, rh: 0.76f, cond: 1.6f, cape: 450f, cin: 0.55f, sstAnom: +2.4f, curNorth: latDeg < 0 ? -0.12f : 0.12f);
            SetSynopticPressureAndConvergence(baseWorld, cx, cy, slpHpa: 1018f, rainImpact: 0f, convSign: 0f);
            bPW = BuildAndSample(baseWorld, day, cx, cy, 0f, 0f, out fPW, out aPW);
        }

        Console.WriteLine($"[5. Cold vs Warm SST | {label} ({lonDeg:F1},{latDeg:F1}) day={day:F0}] " +
            $"ColdSST(-2.4C): marineSc={aCold.r:F3} type={bCold.Convection:F3} base={bCold.BaseM:F0}m top={bCold.TopM:F0}m high={bCold.High:F3} | " +
            $"WarmSST(+2.4C): marineSc={aWarm.r:F3} itcz={fWarm.g:F3} type={bWarm.Convection:F3} base={bWarm.BaseM:F0}m top={bWarm.TopM:F0}m high={bWarm.High:F3}");
        Console.WriteLine($"  Pure SST-only (-2.4C vs +2.4C): marineSc {aPC.r:F3}->{aPW.r:F3}, itcz {fPC.g:F3}->{fPW.g:F3}, type {bPC.Convection:F3}->{bPW.Convection:F3}, top {bPC.TopM:F0}m->{bPW.TopM:F0}m");

        Check(aCold.r > 0.42f && aWarm.r < 0.16f && bCold.Convection < 0.18f && bWarm.Convection > bCold.Convection + 0.16f && bWarm.TopM > bCold.TopM + 900f && bWarm.High > bCold.High + 0.25f,
            $"{label}: Cold/Warm SST marine Sc vs convective contrast too weak: Sc {aCold.r:F3}/{aWarm.r:F3}, type {bCold.Convection:F3}/{bWarm.Convection:F3}, top {bCold.TopM:F0}/{bWarm.TopM:F0}m, high {bCold.High:F3}/{bWarm.High:F3}");
        Check(aPC.r > 0.40f && aPW.r < 0.18f && bPW.Convection > bPC.Convection + 0.07f && bPW.TopM > bPC.TopM + 300f,
            $"{label}: Pure SST anomaly failed to switch ScProtect (>0.38) and cloud top height: Sc {aPC.r:F3}->{aPW.r:F3}, type {bPC.Convection:F3}->{bPW.Convection:F3}, top {bPC.TopM:F0}->{bPW.TopM:F0}m");
    }

    static void VerifySeasonAndRotationBelts(WorldData baseWorld, string label, double day)
    {
        // 6a. Seasonal ITCZ migration (Jan day=15 vs Jul day=195)
        var fJan = new byte[GridW * GridH * 4]; var aJan = new byte[GridW * GridH * 4];
        var fJul = new byte[GridW * GridH * 4]; var aJul = new byte[GridW * GridH * 4];
        GlebaGeographySupplement.BuildCloudSynopticFields(baseWorld, 15.0, GridW, GridH, fJan, aJan, null);
        GlebaGeographySupplement.BuildCloudSynopticFields(baseWorld, 195.0, GridW, GridH, fJul, aJul, null);
        double itczJan = TropicalConvectionCentroidLat(fJan);
        double itczJul = TropicalConvectionCentroidLat(fJul);

        // 6b. 1.0x (EarthModern) vs 0.45x (SlowRotatorDoubleCell) on IDENTICAL terrain & seed
        var rot1x = RebuildWithPlanetConfig(baseWorld, ExoPlaSimPlanetConfig.CreatePreset(ExoPlaSimPreset.EarthModern));
        var rot045x = RebuildWithPlanetConfig(baseWorld, ExoPlaSimPlanetConfig.CreatePreset(ExoPlaSimPreset.SlowRotatorDoubleCell));

        GlebaGeographySupplement.GetCloudCirculationParams(rot1x, out float had1, out float pol1, out float k1, out float reg1);
        GlebaGeographySupplement.GetCloudCirculationParams(rot045x, out float had045, out float pol045, out float k045, out float reg045);

        var f1 = new byte[GridW * GridH * 4]; var a1 = new byte[GridW * GridH * 4]; var u1 = new ushort[GridW * GridH * 4];
        var f045 = new byte[GridW * GridH * 4]; var a045 = new byte[GridW * GridH * 4]; var u045 = new ushort[GridW * GridH * 4];
        GlebaGeographySupplement.BuildCloudSynopticFields(rot1x, day, GridW, GridH, f1, a1, u1, day);
        GlebaGeographySupplement.BuildCloudSynopticFields(rot045x, day, GridW, GridH, f045, a045, u045, day);

        double stormPeak1 = HemisphericBeltCentroidAbsLat(f1, 2, 20f, 82f);
        double stormPeak045 = HemisphericBeltCentroidAbsLat(f045, 2, 20f, 82f);
        double scPeak1 = HemisphericBeltCentroidAbsLat(a1, 0, 8f, 65f);
        double scPeak045 = HemisphericBeltCentroidAbsLat(a045, 0, 8f, 65f);
        double polNorm1 = GlobalMeanChannel(a1, 3);
        double polNorm045 = GlobalMeanChannel(a045, 3);
        double jetPeak1 = UpperJetPeakAbsLat(u1);
        double jetPeak045 = UpperJetPeakAbsLat(u045);

        var season = new FlatMonthlyForcing(day);
        var w1 = new float[GridW * GridH * 4]; var w045 = new float[GridW * GridH * 4];
        FlatSkyForcingCache.PackSeasonalWindRgba(rot1x, season, GridW, GridH, w1);
        FlatSkyForcingCache.PackSeasonalWindRgba(rot045x, season, GridW, GridH, w045);
        double midEast1 = MeanZonalSurfaceWind(w1, 32f, 38f);
        double midEast045 = MeanZonalSurfaceWind(w045, 32f, 38f);

        Console.WriteLine($"[6. Season & Rotation | {label}] Season ITCZ Jan={itczJan:+0.0;-0.0}deg -> Jul={itczJul:+0.0;-0.0}deg (shift={itczJul - itczJan:+0.0}deg)");
        Console.WriteLine($"  1.0x vs 0.45x (day={day:F0}): Hadley={had1 * 57.2958f:F1}->{had045 * 57.2958f:F1}deg, Polar={pol1 * 57.2958f:F1}->{pol045 * 57.2958f:F1}deg, RossbyK={k1:F1}->{k045:F1}, " +
            $"polNorm={polNorm1:F3}->{polNorm045:F3}, stormBelt={stormPeak1:F1}->{stormPeak045:F1}deg, scBelt={scPeak1:F1}->{scPeak045:F1}deg, upperJet={jetPeak1:F1}->{jetPeak045:F1}deg, 35degU={midEast1:+0.000;-0.000}->{midEast045:+0.000;-0.000}");

        Check(itczJul > itczJan + 3.0, $"{label}: Seasonal ITCZ failed to shift north in July: Jan={itczJan:F1}, Jul={itczJul:F1}");
        Check(had045 > had1 + 0.15f && pol045 > pol1 + 0.08f && k045 < k1 && reg045 == 1f && reg1 == 0f,
            $"{label}: Circulation params failed to reflect 0.45x DoubleCell preset");
        Check(polNorm045 > polNorm1 + 0.12 && stormPeak045 > stormPeak1 + 5.0 && scPeak045 > scPeak1 + 4.0 && jetPeak045 > jetPeak1 + 4.0,
            $"{label}: 0.45x cloud belts / Rossby jet failed to expand poleward: storm {stormPeak1:F1}->{stormPeak045:F1}, Sc {scPeak1:F1}->{scPeak045:F1}, jet {jetPeak1:F1}->{jetPeak045:F1}");
        Check(midEast045 < midEast1 - 0.08,
            $"{label}: 0.45x surface wind belt at 32-38deg did not shift poleward/easterly: 1x={midEast1:F3} vs 0.45x={midEast045:F3}");
    }

    static void VerifyCacheContinuityRegionalCycloneAndSave(WorldData earth, WorldData fictional)
    {
        // 7a. Cache refresh cadence & mutation detection
        var testWorld = earth;
        using (var cacheSnap = new ClimateSnapshot(testWorld))
        {
        var cache = new FlatSkyForcingCache();
        var s0 = new FlatMonthlyForcing(0.0);
        var p0 = cache.Evaluate(testWorld, 0.0, cloudsNeeded: true);
        Check(p0.UploadWind && p0.UploadClimateSegment && p0.UploadSynopticForcing && p0.StageCloudWind, "Initial cache evaluation must request all uploads");
        cache.MarkWindUploaded(testWorld, s0);
        cache.MarkClimateSegmentUploaded(testWorld, s0, 0.0);
        cache.MarkCloudWindPublished(0.0, testWorld.TerrainRevision, cache.WindUploadGeneration);

        var pQuarter = cache.Evaluate(testWorld, 0.25, cloudsNeeded: true);
        Check(!pQuarter.UploadWind && !pQuarter.UploadClimateSegment && !pQuarter.UploadSynopticForcing && pQuarter.StageCloudWind,
            "Quarter-day tick must not re-upload 15-day or 1-day forcing, only restage drift wind if day changed");
        cache.MarkCloudWindPublished(0.25, testWorld.TerrainRevision, cache.WindUploadGeneration);

        var pDay1 = cache.Evaluate(testWorld, 1.0, cloudsNeeded: true);
        Check(!pDay1.UploadWind && !pDay1.UploadClimateSegment && pDay1.UploadSynopticForcing,
            "1-day tick must refresh synoptic forcing/aux without re-uploading 15-day climate/wind segment");
        cache.MarkSynopticUploaded(1.0);
        cache.MarkCloudWindPublished(1.0, testWorld.TerrainRevision, cache.WindUploadGeneration);

        var pDay15 = cache.Evaluate(testWorld, 15.0, cloudsNeeded: true);
        Check(pDay15.UploadWind && pDay15.UploadClimateSegment && pDay15.UploadSynopticForcing && pDay15.StageCloudWind,
            "15-day half-month crossing must refresh wind, climate segment, and synoptic forcing");
        var s15 = new FlatMonthlyForcing(15.0);
        cache.MarkWindUploaded(testWorld, s15);
        cache.MarkClimateSegmentUploaded(testWorld, s15, 15.0);
        cache.MarkCloudWindPublished(15.0, testWorld.TerrainRevision, cache.WindUploadGeneration);

        // Wind update while clouds are hidden must force StageCloudWind when clouds become visible again
        var pHidden = cache.Evaluate(testWorld, 30.0, cloudsNeeded: false);
        Check(pHidden.UploadWind && !pHidden.UploadClimateSegment, "Hidden clouds must still refresh seasonal wind on segment crossing");
        cache.MarkWindUploaded(testWorld, new FlatMonthlyForcing(30.0));
        var pResume = cache.Evaluate(testWorld, 30.0, cloudsNeeded: true);
        Check(!pResume.UploadWind && pResume.UploadClimateSegment && pResume.StageCloudWind,
            "Resuming clouds after hidden wind upload must stage new cloud wind via WindUploadGeneration");
        cache.MarkClimateSegmentUploaded(testWorld, new FlatMonthlyForcing(30.0), 30.0);
        cache.MarkCloudWindPublished(30.0, testWorld.TerrainRevision, cache.WindUploadGeneration);

        // In-place mutations (Humidity, SST, CAPE, Wind, PlanetClimate) must invalidate cache without TerrainRevision++
        testWorld.Moisture.Humidity[0] = Math.Max(0f, Math.Min(1f, testWorld.Moisture.Humidity[0] + 0.15f));
        Check(cache.Evaluate(testWorld, 30.0, true).UploadClimateSegment, "In-place Humidity mutation missed by cache fingerprint");
        cache.MarkClimateSegmentUploaded(testWorld, new FlatMonthlyForcing(30.0), 30.0);

        testWorld.Moisture.OceanCurrents.SeaAnomaly[0] += 1.5f;
        Check(cache.Evaluate(testWorld, 30.0, true).UploadClimateSegment, "In-place SST SeaAnomaly mutation missed by cache fingerprint");
        cache.MarkClimateSegmentUploaded(testWorld, new FlatMonthlyForcing(30.0), 30.0);

        testWorld.Moisture.ExoPlaSim.CapeJkg[0] += 800f;
        Check(cache.Evaluate(testWorld, 30.0, true).UploadClimateSegment, "In-place ExoPlaSim CAPE mutation missed by cache fingerprint");
        cache.MarkClimateSegmentUploaded(testWorld, new FlatMonthlyForcing(30.0), 30.0);

        testWorld.Moisture.East[0] += 0.25f;
        var pWindMut = cache.Evaluate(testWorld, 30.0, true);
        Check(pWindMut.UploadWind && pWindMut.UploadClimateSegment && pWindMut.StageCloudWind, "In-place Wind mutation missed by cache fingerprint");
        cache.MarkWindUploaded(testWorld, new FlatMonthlyForcing(30.0));
        cache.MarkClimateSegmentUploaded(testWorld, new FlatMonthlyForcing(30.0), 30.0);

        testWorld.PlanetClimate = ExoPlaSimPlanetConfig.CreatePreset(ExoPlaSimPreset.SlowRotatorDoubleCell);
        var pPlanetMut = cache.Evaluate(testWorld, 30.0, true);
        Check(pPlanetMut.UploadWind && pPlanetMut.UploadClimateSegment, "PlanetClimate preset swap missed by cache");
        }

        // 7b. Seasonal & Cross-Year Continuity (Day 359 -> 360 -> 361) + Interannual Variation (Year 1 vs 2 vs 3)
        var f359 = new byte[GridW * GridH * 4]; var a359 = new byte[GridW * GridH * 4];
        var f360 = new byte[GridW * GridH * 4]; var a360 = new byte[GridW * GridH * 4];
        var f361 = new byte[GridW * GridH * 4]; var a361 = new byte[GridW * GridH * 4];
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 359.0, GridW, GridH, f359, a359, null);
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 360.0, GridW, GridH, f360, a360, null);
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 361.0, GridW, GridH, f361, a361, null);
        double stepWrapA = MeanByteDiff(f359, f360);
        double stepWrapB = MeanByteDiff(f360, f361);

        var c359 = new byte[GridW * GridH * 4]; var c360 = new byte[GridW * GridH * 4];
        FlatSkyForcingCache.PackMonthlyClimateRgba(earth, new FlatMonthlyForcing(359.0), GridW, GridH, c359);
        FlatSkyForcingCache.PackMonthlyClimateRgba(earth, new FlatMonthlyForcing(360.0), GridW, GridH, c360);
        double climWrap = MeanByteDiff(c359, c360);

        var fY1 = new byte[GridW * GridH * 4]; var aY1 = new byte[GridW * GridH * 4];
        var fY2 = new byte[GridW * GridH * 4]; var aY2 = new byte[GridW * GridH * 4];
        var fY3 = new byte[GridW * GridH * 4]; var aY3 = new byte[GridW * GridH * 4];
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 195.0, GridW, GridH, fY1, aY1, null);
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 195.0 + 360.0, GridW, GridH, fY2, aY2, null);
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 195.0 + 720.0, GridW, GridH, fY3, aY3, null);
        double interannualForcing12 = MeanByteDiff(fY1, fY2);
        double interannualForcing23 = MeanByteDiff(fY2, fY3);

        // Daily MJO continuity inside a 15-day segment (day 195 -> 196 -> 197 vs old 15-day freeze)
        var f196 = new byte[GridW * GridH * 4]; var a196 = new byte[GridW * GridH * 4];
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 196.0, GridW, GridH, f196, a196, null);
        double dailyIntraSegmentDiff = MeanByteDiff(fY1, f196);

        // 7c. Regional statistics (bounded modulation + dry-air safety) & Cyclone track change detection
        var regStats = new FlatClimateStatistics(earth, default, 64, atmosphereOnly: true);
        regStats.Refresh(195.0, earth.Cyclones, default);
        double minC = 1, maxC = -1, maxS = 0;
        for (int i = 0; i < regStats.Cells.Length; i++)
        {
            var resp = RegionalCloudResponse.From(regStats.Cells[i]);
            if (resp.Cover < minC) minC = resp.Cover;
            if (resp.Cover > maxC) maxC = resp.Cover;
            if (resp.Convection > maxS) maxS = resp.Convection;
        }
        Check(minC < -0.05 && maxC > 0.05 && minC >= -1.0 && maxC <= 1.0 && maxS > 0.05 && maxS <= 1.0,
            $"RegionalCloudResponse out of expected bounded bidirectional range: cover=[{minC:F3},{maxC:F3}], stormMax={maxS:F3}");

        var tA = new FlatCycloneTrack { Tracked = true, U = 0.25, V = 0.62, PressureDeficit = 42, WindSpeed = 48, Phase = 1.2 };
        var tB = tA; tB.U += 0.01;
        Check(FlatSkyForcingCache.SameCycloneTrack(tA, tA) && !FlatSkyForcingCache.SameCycloneTrack(tA, tB),
            "SameCycloneTrack failed to distinguish moved cyclone");

        // 7d. FlatSave encode/decode round-trip compatibility on fictional world
        byte[] savedBytes = FlatSave.Encode(fictional);
        var decoded = FlatSave.Decode(savedBytes);
        byte[] reencoded = FlatSave.Encode(decoded);
        Check(savedBytes.AsSpan().SequenceEqual(reencoded), "FlatSave canonical round-trip bytes differed");
        var fOrig = new byte[GridW * GridH * 4]; var aOrig = new byte[GridW * GridH * 4]; var uOrig = new ushort[GridW * GridH * 4];
        var fDec = new byte[GridW * GridH * 4]; var aDec = new byte[GridW * GridH * 4]; var uDec = new ushort[GridW * GridH * 4];
        GlebaGeographySupplement.BuildCloudSynopticFields(fictional, 195.0, GridW, GridH, fOrig, aOrig, uOrig, 195.0);
        GlebaGeographySupplement.BuildCloudSynopticFields(decoded, 195.0, GridW, GridH, fDec, aDec, uDec, 195.0);
        Check(fOrig.AsSpan().SequenceEqual(fDec) && aOrig.AsSpan().SequenceEqual(aDec) && uOrig.AsSpan().SequenceEqual(uDec),
            "Decoded save did not reproduce identical cloud forcing/aux/upperWind textures");

        Console.WriteLine($"[7. Cache, Continuity & Save] Wrap359->360->361 forcing={stepWrapA:F4}/{stepWrapB:F4}, climate={climWrap:F4} | " +
            $"DailyIntraSeg={dailyIntraSegmentDiff:F4} | Interannual Y1-Y2={interannualForcing12:F4}, Y2-Y3={interannualForcing23:F4} | " +
            $"RegionalCover=[{minC:F2},{maxC:F2}] StormMax={maxS:F2} | SaveBytes={savedBytes.Length:N0} (exact match)");

        Check(stepWrapA < 0.025 && stepWrapB < 0.025 && climWrap < 0.035,
            $"Year-boundary discontinuity detected: forcing {stepWrapA:F4}/{stepWrapB:F4}, climate {climWrap:F4}");
        Check(dailyIntraSegmentDiff > 0.0005 && dailyIntraSegmentDiff < 0.025,
            $"Intra-segment daily synoptic/MJO evolution missing or too jumpy: {dailyIntraSegmentDiff:F5}");
        Check(interannualForcing12 > 0.0015 && interannualForcing23 > 0.0015,
            $"Interannual FlatClimateYear signal did not reach cloud synoptic forcing: Y1-Y2={interannualForcing12:F5}, Y2-Y3={interannualForcing23:F5}");
    }

    static void VerifyAsyncSchedulerPerformanceAndRaces(WorldData earth, WorldData fictional)
    {
        // 8a. Bit-exactness & zero-allocation proof for reusable CloudSynopticWorkspace
        var ws = new GlebaGeographySupplement.CloudSynopticWorkspace();
        var refF = new byte[GridW * GridH * 4];
        var refA = new byte[GridW * GridH * 4];
        var refU = new ushort[GridW * GridH * 4];
        var wsF = new byte[GridW * GridH * 4];
        var wsA = new byte[GridW * GridH * 4];
        var wsU = new ushort[GridW * GridH * 4];

        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 195.0, GridW, GridH, refF, refA, refU, 195.0);
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 195.0, GridW, GridH, wsF, wsA, wsU, 195.0, ws, CancellationToken.None);
        Check(refF.AsSpan().SequenceEqual(wsF) && refA.AsSpan().SequenceEqual(wsA) && refU.AsSpan().SequenceEqual(wsU),
            "Cold CloudSynopticWorkspace output differed from reference BuildCloudSynopticFields");

        // Warm daily refreshes (days 196..205): verify 0 large allocations & exact match on final day
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var swDaily = Stopwatch.StartNew();
        for (int d = 0; d < 10; d++)
            GlebaGeographySupplement.BuildCloudSynopticFields(earth, 196.0 + d, GridW, GridH, wsF, wsA, null, 195.0, ws, CancellationToken.None);
        swDaily.Stop();
        long allocWarm10 = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        double warmDailyMs = swDaily.Elapsed.TotalMilliseconds / 10.0;

        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 205.0, GridW, GridH, refF, refA, null, 195.0);
        Check(refF.AsSpan().SequenceEqual(wsF) && refA.AsSpan().SequenceEqual(wsA),
            "Warm CloudSynopticWorkspace daily output drifted from stateless BuildCloudSynopticFields");
        Check(allocWarm10 <= 2048,
            $"Warm daily BuildCloudSynopticFields allocated unexpected memory: {allocWarm10:N0} bytes over 10 days");

        // Helper: poll scheduler until a payload commits for the exact target (day, segment, revision)
        FlatSkyForcingPayload WaitForCommit(FlatSkyForcingScheduler sched, WorldData w, double day, double expectedSeg, out double maxMainMs)
        {
            maxMainMs = 0;
            var swWait = Stopwatch.StartNew();
            FlatSkyForcingPayload last = null;
            while (swWait.Elapsed.TotalSeconds < 5.0)
            {
                long t0 = Stopwatch.GetTimestamp();
                bool committed = sched.Poll(w, day, cloudsNeeded: true, allowCommit: true, out var payload, out _);
                double dtMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                if (dtMs > maxMainMs) maxMainMs = dtMs;
                if (committed && payload != null)
                {
                    last = payload;
                    if (Math.Abs(payload.SynopticDay - day) < 1e-6 &&
                        Math.Abs(payload.SegmentStartDay - expectedSeg) < 1e-6 &&
                        payload.TerrainRevision == w.TerrainRevision)
                        return payload;
                }
                Thread.Sleep(1);
            }
            throw new Exception($"Timed out waiting for scheduler commit at day={day}, seg={expectedSeg}, rev={w.TerrainRevision} (last={(last == null ? "none" : $"{last.SynopticDay}/{last.SegmentStartDay}")})");
        }

        // Helper: compare committed payload against ground-truth stateless builders
        var simGpuForcing = new byte[GridW * GridH * 4];
        var simGpuAux = new byte[GridW * GridH * 4];
        var simGpuClimate = new byte[GridW * GridH * 4];
        var simGpuUpper = new ushort[GridW * GridH * 4];
        void AssertPayloadExact(WorldData w, FlatSkyForcingPayload p)
        {
            var eF = new byte[GridW * GridH * 4];
            var eA = new byte[GridW * GridH * 4];
            var eU = p.HasClimateSegment ? new ushort[GridW * GridH * 4] : null;
            GlebaGeographySupplement.BuildCloudSynopticFields(w, p.SynopticDay, GridW, GridH, eF, eA, eU, p.SegmentStartDay);
            Check(p.ForcingRgba.AsSpan().SequenceEqual(eF) && p.AuxRgba.AsSpan().SequenceEqual(eA),
                $"Committed forcing/aux did not match stateless reference at day={p.SynopticDay}");
            if (p.HasClimateSegment)
            {
                var eC = new byte[GridW * GridH * 4];
                FlatSkyForcingCache.PackMonthlyClimateRgba(w, new FlatMonthlyForcing(p.SegmentStartDay), GridW, GridH, eC);
                Check(p.MonthlyClimateRgba.AsSpan().SequenceEqual(eC),
                    $"Committed monthly climate did not match reference at seg={p.SegmentStartDay}");
                Check(p.UpperWindHalf.AsSpan().SequenceEqual(eU),
                    $"Committed upper wind did not match reference at seg={p.SegmentStartDay}");
            }
        }

        // 8b. Normal playback (Day 0 -> 31, then drain at day 32 after a full refresh cadence).
        const int normalPlayTicks = 31 * 4;
        double mainPollIdleMax = 0, mainPollIdleSum = 0; int mainPollIdleCnt = 0;
        double mainSubmitMax = 0, mainSubmitSum = 0; int mainSubmitCnt = 0;
        double bgDailySum = 0, bgDailyMax = 0; int bgDailyCnt = 0;
        double bgSegSum = 0, bgSegMax = 0; int bgSegCnt = 0;

        using (var sched = new FlatSkyForcingScheduler(GridW, GridH))
        {
            sched.Bind(earth);
            sched.MarkInitialWindUploaded(earth, new FlatMonthlyForcing(0.0));
            sched.Prefetch(earth, 0.0);

            var initial = WaitForCommit(sched, earth, 0.0, 0.0, out double initPollMax);
            AssertPayloadExact(earth, initial);
            bgSegSum += initial.WorkerDurationMs; bgSegMax = Math.Max(bgSegMax, initial.WorkerDurationMs); bgSegCnt++;

            long steadyAllocStart = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 1; tick <= normalPlayTicks; tick++)
            {
                double day = tick * 0.25;
                long t0 = Stopwatch.GetTimestamp();
                bool committed = sched.Poll(earth, day, cloudsNeeded: true, allowCommit: true, out var payload, out _);
                if (committed && payload != null)
                {
                    // Simulate main-thread GPU texture upload (LoadRawTextureData memory copy)
                    if (payload.HasClimateSegment)
                    {
                        Buffer.BlockCopy(payload.MonthlyClimateRgba, 0, simGpuClimate, 0, simGpuClimate.Length);
                        Buffer.BlockCopy(payload.UpperWindHalf, 0, simGpuUpper, 0, simGpuUpper.Length * sizeof(ushort));
                    }
                    Buffer.BlockCopy(payload.ForcingRgba, 0, simGpuForcing, 0, simGpuForcing.Length);
                    Buffer.BlockCopy(payload.AuxRgba, 0, simGpuAux, 0, simGpuAux.Length);
                    double dtSubmit = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    mainSubmitSum += dtSubmit; if (dtSubmit > mainSubmitMax) mainSubmitMax = dtSubmit; mainSubmitCnt++;
                    if (payload.HasClimateSegment)
                    {
                        bgSegSum += payload.WorkerDurationMs; if (payload.WorkerDurationMs > bgSegMax) bgSegMax = payload.WorkerDurationMs; bgSegCnt++;
                    }
                    else
                    {
                        bgDailySum += payload.WorkerDurationMs; if (payload.WorkerDurationMs > bgDailyMax) bgDailyMax = payload.WorkerDurationMs; bgDailyCnt++;
                    }
                }
                else
                {
                    double dtIdle = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    mainPollIdleSum += dtIdle; if (dtIdle > mainPollIdleMax) mainPollIdleMax = dtIdle; mainPollIdleCnt++;
                }

                // Pace simulated playback (~16 ms frame budget) only when a background job is running so daily jobs finish naturally
                if (sched.IsComputing)
                {
                    var swWait = Stopwatch.StartNew();
                    while (sched.IsComputing && swWait.ElapsedMilliseconds < 25) Thread.Sleep(1);
                }
            }
            // Leave enough time for a new payload even if the last worker
            // published a late fractional day within the same segment.
            double finalDay = normalPlayTicks * 0.25 + FlatSkyForcingCache.SynopticRefreshCadenceDays;
            var finalNorm = WaitForCommit(sched, earth, finalDay, 30.0, out _);
            long steadyAllocBytes = GC.GetAllocatedBytesForCurrentThread() - steadyAllocStart;
            AssertPayloadExact(earth, finalNorm);

            Check(bgDailyCnt >= 20 && bgSegCnt >= 3,
                $"Normal playback did not execute expected daily ({bgDailyCnt}) and segment ({bgSegCnt}) updates");
            Check(mainPollIdleMax < 3.0 && mainSubmitMax < 5.0,
                $"Main-thread Poll/Submit latency exceeded non-blocking budget: idleMax={mainPollIdleMax:F3} ms, submitMax={mainSubmitMax:F3} ms");
            Check(steadyAllocBytes < 65536,
                $"Main-thread steady-state {normalPlayTicks} ticks allocated large buffers: {steadyAllocBytes:N0} B");
            Check(sched.JobsDiscardedStale == 0 && sched.LastError == null,
                $"Normal playback had stale discards ({sched.JobsDiscardedStale}) or error ({sched.LastError})");

            Console.WriteLine($"[8a-b. Async Perf & Normal Play] WarmWorkspaceDaily={warmDailyMs:F2} ms (alloc/10d={allocWarm10} B) | " +
                $"BgDaily={bgDailySum / Math.Max(1, bgDailyCnt):F2} ms (max {bgDailyMax:F2} ms, n={bgDailyCnt}), " +
                $"BgSegment={bgSegSum / Math.Max(1, bgSegCnt):F2} ms (max {bgSegMax:F2} ms, n={bgSegCnt}) | " +
                $"MainPollIdle={mainPollIdleSum / Math.Max(1, mainPollIdleCnt):F3} ms (max {mainPollIdleMax:F3} ms), " +
                $"MainCommit+Upload={mainSubmitSum / Math.Max(1, mainSubmitCnt):F3} ms (max {mainSubmitMax:F3} ms) | " +
                $"MainThreadSteadyAlloc({normalPlayTicks} ticks)={steadyAllocBytes:N0} B");

            // 8c. Continuous accelerated time: deterministic intra-segment & cross-segment coalescing
            using var entered1 = new ManualResetEventSlim(false);
            using var release1 = new ManualResetEventSlim(false);
            int hookHits = 0;
            sched.BeforeWorkerComputeHook = cancel =>
            {
                if (Interlocked.Increment(ref hookHits) == 1)
                {
                    entered1.Set();
                    release1.Wait(cancel);
                }
            };

            int jobsBeforeAccel = sched.JobsStarted;
            // Inside Seg 30 (currently published Day 32): advance rapidly Day 33..43 while Day 33 is held in-flight
            sched.Poll(earth, 33.0, true, true, out _, out _);
            Check(entered1.Wait(2000), "Intra-segment acceleration test worker did not start");
            for (int d = 34; d <= 43; d++)
            {
                long t0 = Stopwatch.GetTimestamp();
                sched.Poll(earth, (double)d, true, true, out var mid, out _);
                double dt = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                Check(mid == null && dt < 2.0, $"Accelerated Poll should be non-blocking without premature commit (dt={dt:F3} ms)");
            }
            Check(sched.JobsStarted == jobsBeforeAccel + 1 && sched.RequestsCoalesced >= 10,
                $"Accelerated intra-segment ticks failed to coalesce: jobs={sched.JobsStarted - jobsBeforeAccel}, coalesced={sched.RequestsCoalesced}");
            release1.Set();
            var accelCommit = WaitForCommit(sched, earth, 43.0, 30.0, out double accelMaxMainMs);
            AssertPayloadExact(earth, accelCommit);
            Check(sched.JobsStarted <= jobsBeforeAccel + 2,
                $"Coalescing spawned extra intermediate jobs instead of jumping to Day 43: {sched.JobsStarted - jobsBeforeAccel}");

            // Cross-segment acceleration: hold Seg 30 Day 44 mid-flight while jumping across Seg 45 into Seg 60 (Day 64)
            using var entered2 = new ManualResetEventSlim(false);
            using var release2 = new ManualResetEventSlim(false);
            hookHits = 0;
            sched.BeforeWorkerComputeHook = cancel =>
            {
                if (Interlocked.Increment(ref hookHits) == 1)
                {
                    entered2.Set();
                    release2.Wait(cancel);
                }
            };
            int cancelledBeforeCross = sched.JobsCancelled;
            sched.Poll(earth, 44.0, true, true, out _, out _);
            Check(entered2.Wait(2000), "Cross-segment acceleration worker did not start");
            sched.Poll(earth, 50.0, true, true, out var midSeg1, out _);
            sched.Poll(earth, 64.0, true, true, out var midSeg2, out _);
            Check(midSeg1 == null && midSeg2 == null && !sched.IsSegmentCurrent(earth, 64.0),
                "Cross-segment acceleration must hold publish until target segment completes");
            Check(sched.JobsCancelled > cancelledBeforeCross, "Obsolete segment job should be cancelled on segment crossing");
            release2.Set();
            sched.BeforeWorkerComputeHook = null;
            var crossCommit = WaitForCommit(sched, earth, 64.0, 60.0, out _);
            AssertPayloadExact(earth, crossCommit);
            Check(sched.IsSegmentCurrent(earth, 64.0), "Target segment 60 must be current after commit");

            // 8d. Paused month jump (Day 64 -> July Day 195, fixed day polling while paused)
            long jumpT0 = Stopwatch.GetTimestamp();
            bool immediateJump = sched.Poll(earth, 195.0, true, true, out _, out _);
            double pausedJumpMainMs = (Stopwatch.GetTimestamp() - jumpT0) * 1000.0 / Stopwatch.Frequency;
            Check(!immediateJump && !sched.IsSegmentCurrent(earth, 195.0) && pausedJumpMainMs < 2.0,
                $"Paused month jump blocked main thread or published prematurely: dt={pausedJumpMainMs:F3} ms");
            var julyCommit = WaitForCommit(sched, earth, 195.0, 195.0, out double pausedWaitMainMax);
            AssertPayloadExact(earth, julyCommit);
            Check(julyCommit.HasClimateSegment && julyCommit.HasSeasonalWind && julyCommit.HasSynopticForcing,
                "Paused month jump must publish atomic segment + wind + synoptic bundle");

            // 8e. Rewind race (1: mid-flight cancellation; 2: already-completed future job discarded on rewind)
            using var enteredRew = new ManualResetEventSlim(false);
            using var releaseRew = new ManualResetEventSlim(false);
            hookHits = 0;
            sched.BeforeWorkerComputeHook = cancel =>
            {
                if (Interlocked.Increment(ref hookHits) == 1)
                {
                    enteredRew.Set();
                    releaseRew.Wait(cancel);
                }
            };
            sched.Poll(earth, 200.0, true, true, out _, out _);
            Check(enteredRew.Wait(2000), "Rewind race worker did not start");
            // Rewind to Jan Day 15 while Day 200 is running
            sched.Poll(earth, 15.0, true, true, out var rewMid, out _);
            Check(rewMid == null && !sched.IsSegmentCurrent(earth, 15.0), "Rewind must not commit future Day 200 payload");
            releaseRew.Set();
            sched.BeforeWorkerComputeHook = null;
            var rewoundCommit = WaitForCommit(sched, earth, 15.0, 15.0, out _);
            AssertPayloadExact(earth, rewoundCommit);
            Check(Math.Abs(sched.PublishedSynopticDay - 15.0) < 1e-6, "Published day must equal rewound Day 15");

            // Already-completed-before-rewind race: let Day 16 finish completely before main thread rewinds to Day 15.25
            sched.Poll(earth, 16.0, true, true, out _, out _);
            var swFin = Stopwatch.StartNew();
            while (sched.IsComputing && swFin.ElapsedMilliseconds < 2000) Thread.Sleep(1);
            int cancelledBeforeLateRew = sched.JobsCancelled;
            sched.Poll(earth, 15.25, true, true, out var lateRewMid, out _);
            Check(lateRewMid == null && sched.JobsCancelled > cancelledBeforeLateRew,
                "Already-completed future Day 16 job must be rejected on rewind to Day 15.25");
            var lateRewCommit = WaitForCommit(sched, earth, 15.25, 15.0, out _);
            AssertPayloadExact(earth, lateRewCommit);

            // 8f. Terrain edit mid-flight race (TerrainRevision++ + mutated climate)
            var editWorld = earth.Clone();
            editWorld.Moisture = earth.Moisture.Clone();
            WaitForCommit(sched, editWorld, 195.0, 195.0, out _);

            using var enteredEdit = new ManualResetEventSlim(false);
            using var releaseEdit = new ManualResetEventSlim(false);
            hookHits = 0;
            sched.BeforeWorkerComputeHook = cancel =>
            {
                if (Interlocked.Increment(ref hookHits) == 1)
                {
                    enteredEdit.Set();
                    releaseEdit.Wait(cancel);
                }
            };
            sched.Poll(editWorld, 196.0, true, true, out _, out _);
            Check(enteredEdit.Wait(2000), "Terrain-edit race worker did not start");
            // Edit terrain & bump TerrainRevision while old revision job is still running
            int editedRev = editWorld.TerrainRevision + 1;
            for (int i = 0; i < editWorld.Count; i += 7) editWorld.Elevation[i] = (short)Math.Min(3800, editWorld.Elevation[i] + 600);
            for (int i = 0; i < editWorld.Moisture.Humidity.Length; i += 11) editWorld.Moisture.Humidity[i] = 0.15f;
            editWorld.TerrainRevision = editedRev;
            Check(sched.ShouldAbortPendingStrip(editWorld, 196.0), "Terrain edit must signal strip abort");
            sched.Poll(editWorld, 196.0, true, true, out var editMid, out _);
            Check(editMid == null && !sched.IsSegmentCurrent(editWorld, 196.0), "Old terrain revision job must never publish over new revision");
            releaseEdit.Set();
            sched.BeforeWorkerComputeHook = null;
            var editedCommit = WaitForCommit(sched, editWorld, 196.0, 195.0, out _);
            Check(editedCommit.TerrainRevision == editedRev, "Committed payload must carry updated TerrainRevision");
            AssertPayloadExact(editWorld, editedCommit);

            // Already-completed stale revision race: let Day 197 complete on editedRev before bumping to editedRev+1
            sched.Poll(editWorld, 197.0, true, true, out _, out _);
            var swStale = Stopwatch.StartNew();
            while (sched.IsComputing && swStale.ElapsedMilliseconds < 2000) Thread.Sleep(1);
            int staleBefore = sched.JobsDiscardedStale;
            editWorld.TerrainRevision = editedRev + 1;
            sched.Poll(editWorld, 197.0, true, true, out var staleMid, out _);
            Check(staleMid == null && sched.JobsDiscardedStale > staleBefore,
                "Already-completed old TerrainRevision job must be discarded as stale without committing");
            var editedCommit2 = WaitForCommit(sched, editWorld, 197.0, 195.0, out _);
            Check(editedCommit2.TerrainRevision == editedRev + 1, "Second terrain edit revision must commit cleanly");
            AssertPayloadExact(editWorld, editedCommit2);

            // 8g. World switch mid-flight race (earth -> fictional)
            WaitForCommit(sched, earth, 195.0, 195.0, out _);
            using var enteredSw = new ManualResetEventSlim(false);
            using var releaseSw = new ManualResetEventSlim(false);
            hookHits = 0;
            sched.BeforeWorkerComputeHook = cancel =>
            {
                if (Interlocked.Increment(ref hookHits) == 1)
                {
                    enteredSw.Set();
                    releaseSw.Wait(cancel);
                }
            };
            sched.Poll(earth, 196.0, true, true, out _, out _);
            Check(enteredSw.Wait(2000), "World-switch race worker did not start");
            sched.Poll(fictional, 195.0, true, true, out var swMid, out _);
            Check(swMid == null && !sched.IsSegmentCurrent(fictional, 195.0), "Old Earth worker must never overwrite switched Fictional world");
            releaseSw.Set();
            sched.BeforeWorkerComputeHook = null;
            var ficCommit = WaitForCommit(sched, fictional, 195.0, 195.0, out _);
            AssertPayloadExact(fictional, ficCommit);

            // 8h. Hide/resume clouds mid-flight race + mid-strip allowCommit=false protection
            using var enteredHide = new ManualResetEventSlim(false);
            using var releaseHide = new ManualResetEventSlim(false);
            hookHits = 0;
            sched.BeforeWorkerComputeHook = cancel =>
            {
                if (Interlocked.Increment(ref hookHits) == 1)
                {
                    enteredHide.Set();
                    releaseHide.Wait(cancel);
                }
            };
            sched.Poll(fictional, 196.0, true, true, out _, out _);
            Check(enteredHide.Wait(2000), "Hide/resume race worker did not start");
            // Hide clouds, advance across segment boundary to Day 212 while hidden, then resume
            sched.Poll(fictional, 196.0, cloudsNeeded: false, allowCommit: false, out _, out _);
            sched.Poll(fictional, 212.0, cloudsNeeded: false, allowCommit: false, out _, out bool hiddenWindSync);
            Check(hiddenWindSync, "Crossing 15-day segment while clouds hidden must still signal vegetation wind sync");
            sched.Poll(fictional, 212.0, cloudsNeeded: true, allowCommit: true, out var resumeMid, out _);
            Check(resumeMid == null && !sched.IsSegmentCurrent(fictional, 212.0), "Pre-hide Day 196 worker must never overwrite resumed Day 212");
            releaseHide.Set();
            sched.BeforeWorkerComputeHook = null;
            var resumeCommit = WaitForCommit(sched, fictional, 212.0, 210.0, out _);
            AssertPayloadExact(fictional, resumeCommit);

            // Mid-strip allowCommit=false guard: daily job Day 213 finishes while allowCommit=false, must wait to commit until allowCommit=true
            sched.Poll(fictional, 213.0, true, allowCommit: false, out var stripHold1, out _);
            Check(stripHold1 == null, "Mid-strip Poll must not commit while allowCommit=false");
            var swStrip = Stopwatch.StartNew();
            while (sched.IsComputing && swStrip.ElapsedMilliseconds < 2000) Thread.Sleep(1);
            sched.Poll(fictional, 213.0, true, allowCommit: false, out var stripHold2, out _);
            Check(stripHold2 == null && Math.Abs(sched.PublishedSynopticDay - 212.0) < 1e-6,
                "Completed daily job must remain buffered while allowCommit=false (mid-strip)");
            bool committedAfterStrip = sched.Poll(fictional, 213.0, true, allowCommit: true, out var stripRelease, out _);
            Check(committedAfterStrip && stripRelease != null && Math.Abs(stripRelease.SynopticDay - 213.0) < 1e-6,
                "Buffered daily job must commit immediately once allowCommit becomes true");
            AssertPayloadExact(fictional, stripRelease);

            Console.WriteLine($"[8c-h. Coalescing & Race Safety] AccelCoalesced={sched.RequestsCoalesced} (maxMain={accelMaxMainMs:F3} ms) | " +
                $"PausedMonthJumpMain={pausedJumpMainMs:F3} ms (pollMax={pausedWaitMainMax:F3} ms) | " +
                $"JobsStarted={sched.JobsStarted}, Accepted={sched.JobsAccepted}, Cancelled={sched.JobsCancelled}, DiscardedStale={sched.JobsDiscardedStale} | " +
                $"Rewind/TerrainEdit/WorldSwitch/HideResume/MidStripGuard: ALL EXACT");
        }
    }

    // ---- Helpers: World Perturbation, Site Selection & Faithful CPU Mirror of FlatSkyBake + OrbisCloudVerticalSpan ----

    sealed class ClimateSnapshot : IDisposable
    {
        readonly WorldData w;
        readonly ExoPlaSimPlanetConfig savedPlanet;
        readonly float[] precip, temp;
        readonly float[] hum, cond, east, north, seaAnom;
        readonly float[] press, impact, shadow, curN;
        readonly float[] convRain, stratRain, virga, shallowSoil, deepSoil, cape, cin;

        public ClimateSnapshot(WorldData world)
        {
            w = world;
            savedPlanet = w.PlanetClimate;
            precip = Copy(w.Precipitation);
            temp = Copy(w.Temperature);
            var m = w.Moisture;
            if (m != null)
            {
                hum = Copy(m.Humidity); cond = Copy(m.Condensation); east = Copy(m.East); north = Copy(m.North);
                seaAnom = Copy(m.OceanCurrents?.SeaAnomaly);
                if (m.Gleba != null)
                {
                    press = Copy(m.Gleba.PressureHpa); impact = Copy(m.Gleba.RainfallImpact);
                    shadow = Copy(m.Gleba.RainShadowiness); curN = Copy(m.Gleba.CurrentNorth);
                }
                if (m.ExoPlaSim != null)
                {
                    convRain = Copy(m.ExoPlaSim.ConvectiveRain); stratRain = Copy(m.ExoPlaSim.StratiformRain);
                    virga = Copy(m.ExoPlaSim.VirgaReevap); shallowSoil = Copy(m.ExoPlaSim.ShallowSoil);
                    deepSoil = Copy(m.ExoPlaSim.DeepSoil); cape = Copy(m.ExoPlaSim.CapeJkg);
                    cin = Copy(m.ExoPlaSim.EntropyDeficit);
                }
            }
        }

        public void Restore()
        {
            w.PlanetClimate = savedPlanet;
            CopyBack(precip, w.Precipitation);
            CopyBack(temp, w.Temperature);
            var m = w.Moisture;
            if (m != null)
            {
                CopyBack(hum, m.Humidity); CopyBack(cond, m.Condensation); CopyBack(east, m.East); CopyBack(north, m.North);
                CopyBack(seaAnom, m.OceanCurrents?.SeaAnomaly);
                if (m.Gleba != null)
                {
                    CopyBack(press, m.Gleba.PressureHpa); CopyBack(impact, m.Gleba.RainfallImpact);
                    CopyBack(shadow, m.Gleba.RainShadowiness); CopyBack(curN, m.Gleba.CurrentNorth);
                }
                if (m.ExoPlaSim != null)
                {
                    CopyBack(convRain, m.ExoPlaSim.ConvectiveRain); CopyBack(stratRain, m.ExoPlaSim.StratiformRain);
                    CopyBack(virga, m.ExoPlaSim.VirgaReevap); CopyBack(shallowSoil, m.ExoPlaSim.ShallowSoil);
                    CopyBack(deepSoil, m.ExoPlaSim.DeepSoil); CopyBack(cape, m.ExoPlaSim.CapeJkg);
                    CopyBack(cin, m.ExoPlaSim.EntropyDeficit);
                }
            }
        }

        public void Dispose() => Restore();
        static float[] Copy(float[] a) => a == null ? null : (float[])a.Clone();
        static void CopyBack(float[] src, float[] dst) { if (src != null && dst != null) Array.Copy(src, dst, src.Length); }
    }

    static WorldData RebuildWithPlanetConfig(WorldData src, ExoPlaSimPlanetConfig cfg)
    {
        var w = src.CloneForTerrainRebuild();
        w.PlanetClimate = cfg;
        FlatGenerator.Rebuild(w, default);
        return w;
    }

    static void SetPatch(
        WorldData w, int cx, int cy, int radius,
        float? rainMm = null, float? tempC = null, float? rh = null, float? cond = null,
        float? cape = null, float? cin = null, float? deepSoil = null, float? shallowSoil = null,
        float? convRain = null, float? stratRain = null, float? virga = null,
        float? sstAnom = null, float? curNorth = null, float? windE = null, float? windN = null)
    {
        var m = w.Moisture;
        var g = m?.Gleba;
        var e = m?.ExoPlaSim;
        var oc = m?.OceanCurrents;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int x = FMath.Wrap(cx + dx, w.Width);
            int y = FMath.Clamp(cy + dy, 0, w.Height - 1);
            int tile = y * w.Width + x;
            int o12 = tile * 12;
            for (int mo = 0; mo < 12; mo++)
            {
                if (rainMm.HasValue) w.Precipitation[o12 + mo] = rainMm.Value;
                if (tempC.HasValue) w.Temperature[o12 + mo] = tempC.Value;
            }
            float u = (x + 0.5f) / w.Width, v = (y + 0.5f) / w.Height;
            if (m != null)
            {
                int mx = FMath.Wrap((int)(u * m.Width), m.Width), my = FMath.Clamp((int)(v * m.Height), 0, m.Height - 1);
                int mo12 = (my * m.Width + mx) * 12;
                for (int mo = 0; mo < 12; mo++)
                {
                    if (rh.HasValue) m.Humidity[mo12 + mo] = rh.Value;
                    if (cond.HasValue) m.Condensation[mo12 + mo] = cond.Value;
                    if (windE.HasValue) m.East[mo12 + mo] = windE.Value;
                    if (windN.HasValue) m.North[mo12 + mo] = windN.Value;
                }
            }
            if (oc != null && sstAnom.HasValue)
            {
                int ox = FMath.Wrap((int)(u * oc.Width), oc.Width), oy = FMath.Clamp((int)(v * oc.Height), 0, oc.Height - 1);
                oc.SeaAnomaly[oy * oc.Width + ox] = sstAnom.Value;
            }
            if (g != null && curNorth.HasValue)
            {
                int gx = FMath.Wrap((int)(u * g.Width), g.Width), gy = FMath.Clamp((int)(v * g.Height), 0, g.Height - 1);
                int go12 = (gy * g.Width + gx) * 12;
                for (int mo = 0; mo < 12; mo++) g.CurrentNorth[go12 + mo] = curNorth.Value;
            }
            if (e != null)
            {
                int ex = FMath.Wrap((int)(u * e.Width), e.Width), ey = FMath.Clamp((int)(v * e.Height), 0, e.Height - 1);
                int eo12 = (ey * e.Width + ex) * 12;
                for (int mo = 0; mo < 12; mo++)
                {
                    if (cape.HasValue) e.CapeJkg[eo12 + mo] = cape.Value;
                    if (cin.HasValue) e.EntropyDeficit[eo12 + mo] = cin.Value;
                    if (deepSoil.HasValue) e.DeepSoil[eo12 + mo] = deepSoil.Value;
                    if (shallowSoil.HasValue) e.ShallowSoil[eo12 + mo] = shallowSoil.Value;
                    if (convRain.HasValue) e.ConvectiveRain[eo12 + mo] = convRain.Value;
                    if (stratRain.HasValue) e.StratiformRain[eo12 + mo] = stratRain.Value;
                    if (virga.HasValue) e.VirgaReevap[eo12 + mo] = virga.Value;
                }
            }
        }
    }

    static void SetSynopticPressureAndConvergence(WorldData w, int cx, int cy, float slpHpa, float rainImpact, float convSign, bool overrideWind = true)
    {
        var m = w.Moisture;
        var g = m?.Gleba;
        for (int dy = -3; dy <= 3; dy++)
        for (int dx = -3; dx <= 3; dx++)
        {
            int x = FMath.Wrap(cx + dx, w.Width);
            int y = FMath.Clamp(cy + dy, 0, w.Height - 1);
            float u = (x + 0.5f) / w.Width, v = (y + 0.5f) / w.Height;
            if (g != null)
            {
                int gx = FMath.Wrap((int)(u * g.Width), g.Width), gy = FMath.Clamp((int)(v * g.Height), 0, g.Height - 1);
                int go12 = (gy * g.Width + gx) * 12;
                for (int mo = 0; mo < 12; mo++)
                {
                    g.PressureHpa[go12 + mo] = slpHpa;
                    g.RainfallImpact[go12 + mo] = rainImpact;
                    g.RainShadowiness[go12 + mo] = 0f;
                }
            }
            if (overrideWind && m != null)
            {
                int mx = FMath.Wrap((int)(u * m.Width), m.Width), my = FMath.Clamp((int)(v * m.Height), 0, m.Height - 1);
                int mo12 = (my * m.Width + mx) * 12;
                // convSign > 0 => inward converging flow (-div V > 0); convSign < 0 => outward diverging flow
                float we = -convSign * dx * 0.22f;
                float wn = -convSign * dy * 0.22f;
                for (int mo = 0; mo < 12; mo++)
                {
                    m.East[mo12 + mo] = we;
                    m.North[mo12 + mo] = wn;
                }
            }
        }
    }

    static BakedCloudSample BuildAndSample(
        WorldData w, double day, int cx, int cy, float regionalX, float regionalY,
        out (float r, float g, float b, float a) forcingOut, out (float r, float g, float b, float a) auxOut)
    {
        var f = new byte[GridW * GridH * 4];
        var a = new byte[GridW * GridH * 4];
        var clim = new byte[GridW * GridH * 4];
        var season = new FlatMonthlyForcing(day);
        FlatSkyForcingCache.PackMonthlyClimateRgba(w, season, GridW, GridH, clim);
        GlebaGeographySupplement.BuildCloudSynopticFields(w, day, GridW, GridH, f, a, null, season.SegmentStartDay);
        GlebaGeographySupplement.GetCloudCirculationParams(w, out float hadleyRad, out _, out _, out _);

        int p4 = (cy * GridW + cx) * 4;
        forcingOut = (f[p4] / 255f, f[p4 + 1] / 255f, f[p4 + 2] / 255f, f[p4 + 3] / 255f);
        auxOut = (a[p4] / 255f, a[p4 + 1] / 255f, a[p4 + 2] / 255f, a[p4 + 3] / 255f);
        float wet = FMath.Lerp(clim[p4] / 255f, clim[p4 + 1] / 255f, season.SegmentBlend);
        float latRad = ((cy + 0.5f) / GridH - 0.5f) * (float)Math.PI;
        return EvaluateSkyBakeMirror(forcingOut, auxOut, wet, regionalX, regionalY, hadleyRad, Math.Abs((float)Math.Sin(latRad)));
    }

    /// <summary>
    /// Exact pure-C# mirror of FlatSkyBake.shader lines 401-533 + OrbisCloudVerticalSpan (OrbisHPVolumeCloud.cginc lines 42-57),
    /// evaluated across a deterministic 5-phase morphology ensemble at fixed seed/time to measure cloud amount, type, and height.
    /// </summary>
    static BakedCloudSample EvaluateSkyBakeMirror(
        (float r, float g, float b, float a) forcing, (float r, float g, float b, float a) aux,
        float wet, float respX, float respY, float hadleyRad, float absSinLat)
    {
        // Five representative noise morphology phases spanning clear background to active convective/frontal core
        var phases = new (float synCluster, float planetary, float baseTex, float flowBands, float mesoCells, float fineGrain,
                          float curvedBand, float curvedWisp, float tradeStreets, float honeycomb, float mcsCluster, float anvilShield,
                          float frontalLow, float postFrontCells, float frontalMid, float frontalDry, float wcbCirrus, float polarCarve, float wisps)[]
        {
            (0.28f, 0.28f, 0.36f, 0.35f, 0.32f, 0.38f, 0.14f, 0.12f, 0.38f, 0.34f, 0.14f, 0.12f, 0.06f, 0.08f, 0.12f, 0.24f, 0.10f, 0.10f, 0.38f),
            (0.42f, 0.42f, 0.46f, 0.45f, 0.44f, 0.46f, 0.24f, 0.20f, 0.46f, 0.44f, 0.28f, 0.26f, 0.14f, 0.16f, 0.24f, 0.12f, 0.22f, 0.06f, 0.46f),
            (0.54f, 0.52f, 0.54f, 0.54f, 0.52f, 0.54f, 0.36f, 0.32f, 0.54f, 0.56f, 0.50f, 0.46f, 0.30f, 0.18f, 0.42f, 0.06f, 0.38f, 0.03f, 0.54f),
            (0.66f, 0.64f, 0.62f, 0.62f, 0.64f, 0.60f, 0.50f, 0.44f, 0.62f, 0.66f, 0.72f, 0.68f, 0.52f, 0.14f, 0.58f, 0.02f, 0.58f, 0.02f, 0.62f),
            (0.80f, 0.78f, 0.72f, 0.70f, 0.76f, 0.68f, 0.66f, 0.56f, 0.70f, 0.76f, 0.88f, 0.84f, 0.82f, 0.08f, 0.72f, 0.01f, 0.78f, 0.01f, 0.72f),
        };

        float synLift = forcing.r, itczConv = forcing.g, stormTrack = forcing.b, moistEnv = forcing.a;
        float marineSc = aux.r, orogCrest = aux.g, leeShadow = aux.b;
        float hadleySin0 = (float)Math.Sin(FMath.Clamp(hadleyRad * 0.38f, 0.12f, 0.72f));
        float hadleySin1 = (float)Math.Sin(FMath.Clamp(hadleyRad * 0.92f, 0.28f, 0.96f));
        float tropBelt = 1.0f - Smooth(hadleySin0, hadleySin1, absSinLat);
        float effWet = wet * (1.0f - 0.72f * leeShadow);
        float scProtect = Smooth(0.38f, 0.74f, marineSc);
        float lowLee = leeShadow * (1.0f - 0.82f * scProtect);
        float highShare = Sat(0.58f * itczConv + 0.52f * stormTrack + 0.26f * moistEnv) * (1.0f - 0.78f * marineSc) * (1.0f - 0.66f * leeShadow);
        float climTarget = Sat(synLift + 0.11f * respX * (1.0f - 0.50f * lowLee));
        float lowTarget = Sat(climTarget * (1.0f - 0.35f * highShare));
        float highTarget = Sat((climTarget - lowTarget) / Math.Max(0.14f, 1.0f - lowTarget) + 0.22f * highShare * climTarget);
        float auxPolarRad = (30.0f + 58.0f * aux.a) * 0.01745329f;
        float midSin0 = (float)Math.Sin(FMath.Clamp(hadleyRad * 0.74f, 0.28f, 0.88f));
        float midSin1 = (float)Math.Sin(FMath.Clamp(hadleyRad * 1.24f, 0.52f, 0.96f));
        float polCap0 = (float)Math.Sin(FMath.Clamp(auxPolarRad * 1.08f, 1.10f, 1.46f));
        float polCap1 = (float)Math.Sin(FMath.Clamp(auxPolarRad * 1.20f, 1.32f, 1.54f));
        float midMoistGate = Smooth(0.16f, 0.48f, moistEnv) * Smooth(0.14f, 0.42f, climTarget);
        float midLatBoost = Smooth(midSin0, midSin1, absSinLat) * (1.0f - Smooth(polCap0, polCap1, absSinLat)) * midMoistGate;
        float stormWeight = Sat(Smooth(0.08f, 0.48f, stormTrack + 0.18f * midLatBoost) * (1.0f - 0.75f * leeShadow));

        double sLow = 0, sHigh = 0, sConv = 0, sStorm = 0, sBase = 0, sTop = 0;
        for (int i = 0; i < phases.Length; i++)
        {
            var m = phases[i];
            float clusterBias = (m.synCluster - 0.495f) * 0.18f;
            float zBase = (m.baseTex - 0.496f) * 5.4f + clusterBias;
            float zRibbon = (m.baseTex - 0.498f) * 3.4f + clusterBias * 0.45f + 3.80f * (m.curvedBand - 0.275f) + 1.08f * (m.curvedWisp - 0.235f);
            float zStreets = (m.tradeStreets - 0.496f) * 5.6f + clusterBias * 0.35f + 2.25f * (m.curvedBand - 0.26f);
            float zHoney = (m.honeycomb - 0.46f) * 5.4f + clusterBias * 0.30f + 1.15f * (m.curvedBand - 0.26f) + 0.75f * scProtect;
            float zMcs = 0.45f * zRibbon + 0.21f * zStreets + 2.60f * (m.mcsCluster - 0.185f) + clusterBias * 0.25f;
            float zFront = 0.24f * zRibbon + 5.45f * (m.frontalLow - 0.15f) - 5.30f * m.frontalDry - 1.15f * m.polarCarve;

            float zLow = FMath.Lerp(zBase, zRibbon, 0.74f);
            zLow = FMath.Lerp(zLow, zStreets, 0.38f * tropBelt);
            zLow = FMath.Lerp(zLow, zMcs, Sat(tropBelt * (0.30f + 0.70f * itczConv)));
            zLow = FMath.Lerp(zLow, zHoney, Sat(scProtect * 0.92f));
            zLow = FMath.Lerp(zLow, zFront, stormWeight);
            float orogMoist = Smooth(0.16f, 0.48f, moistEnv);
            zLow += 0.82f * orogCrest * (0.30f + 0.70f * Smooth(0.22f, 0.78f, m.baseTex)) * orogMoist;
            float uLow = 1.0f / (1.0f + (float)Math.Exp(-1.19f * FMath.Clamp(zLow, -5.5f, 5.5f)));

            float sharpOrg = Sat(Math.Max(stormWeight * m.frontalLow * 1.06f, itczConv * m.mcsCluster));
            float dryGuard = Smooth(0.10f, 0.35f, climTarget + 0.16f * scProtect) * (1.0f - 0.84f * lowLee);
            float polarDryCarve = Sat(stormWeight * m.frontalDry * Smooth(midSin0, midSin1, absSinLat) + m.polarCarve * 0.38f);
            float blanketRelief = Smooth(0.68f, 0.92f, lowTarget) * (1.0f - Smooth(0.26f, 0.66f, m.synCluster * 0.35f + m.planetary * 0.65f)) * (1.0f - 0.90f * sharpOrg);
            float effLowTarget = Sat(Math.Min(0.86f, lowTarget * (1.0f - 0.58f * polarDryCarve - 0.08f * blanketRelief) + 0.14f * scProtect * Smooth(0.24f, 0.64f, m.synCluster * 0.30f + m.planetary * 0.70f) + 0.18f * orogCrest * orogMoist * (1.0f - 0.70f * leeShadow)) + 0.045f * Smooth(0.24f, 0.60f, climTarget) * (1.0f - lowLee));
            float veilTarget = Sat(Math.Min(0.86f, effLowTarget * 1.28f + 0.102f * dryGuard) * (1.0f - 0.62f * polarDryCarve));
            float wVeil = Math.Max(1e-4f, Math.Min(0.44f, Math.Min(veilTarget, 1.0f - veilTarget) + 0.09f));
            float rawVeil = Smooth(1.0f - veilTarget - wVeil, 1.0f - veilTarget + wVeil, uLow);
            float wCore = Math.Max(1e-4f, Math.Min(0.24f, Math.Min(effLowTarget, 1.0f - effLowTarget)));
            float rawCore = Smooth(1.0f - effLowTarget - wCore, 1.0f - effLowTarget + wCore, uLow);

            float silkTex = m.flowBands * 0.54f + m.fineGrain * 0.30f + m.mesoCells * 0.16f;
            float veilLayer = rawVeil * FMath.Lerp(0.31f, 0.56f, Smooth(0.16f, 0.84f, silkTex)) * dryGuard;
            float bodyLayer = rawCore * (0.64f + 0.36f * Smooth(0.18f, 0.82f, silkTex));
            float denseCore = Smooth(0.44f, 0.82f, rawCore) * (sharpOrg * 0.88f + 0.12f) * (0.92f + 0.08f * Smooth(0.22f, 0.78f, m.mesoCells));
            float frontDirect = stormWeight * Smooth(0.24f, 0.72f, m.frontalLow) * (1.0f - 0.85f * m.frontalDry) * Smooth(0.10f, 0.38f, climTarget + 0.12f * midLatBoost) * 0.84f;
            float postWakeLace = stormWeight * m.postFrontCells * Smooth(0.08f, 0.40f, climTarget + 0.12f * midLatBoost) * 0.82f;
            float scDeckDirect = scProtect * Smooth(0.20f, 0.62f, m.honeycomb) * Smooth(0.18f, 0.52f, moistEnv + 0.28f * scProtect) * (1.0f - 0.72f * leeShadow * (1.0f - 0.55f * scProtect)) * 0.80f;
            float oceanTradeGate = Smooth(0.16f, 0.42f, climTarget) * Smooth(0.28f, 0.58f, moistEnv) * (1.0f - 0.88f * lowLee);
            float tradePatchEnv = Smooth(0.34f, 0.67f, m.planetary * 0.48f + m.curvedBand * 0.52f);
            float tradeFineField = Smooth(0.40f, 0.68f, m.tradeStreets * 0.64f + m.honeycomb * 0.36f);
            float marineTapestry = oceanTradeGate * tradePatchEnv * tradeFineField * FMath.Lerp(0.40f, 0.64f, Sat(marineSc + 0.38f * tropBelt));
            float itczDirectLift = Smooth(0.12f, 0.50f, itczConv) * Smooth(0.36f, 0.80f, m.mcsCluster) * (1.0f - 0.82f * leeShadow) * 0.82f;
            float orogDirectCap = orogCrest * Smooth(0.16f, 0.52f, moistEnv) * (1.0f - 0.78f * leeShadow) * (0.44f + 0.56f * Smooth(0.20f, 0.78f, m.flowBands * 0.55f + m.mesoCells * 0.45f)) * 0.76f;
            float low = Sat(Math.Max(Math.Max(veilLayer, bodyLayer), Math.Max(Math.Max(Math.Max(denseCore, frontDirect), Math.Max(postWakeLace, orogDirectCap)), Math.Max(Math.Max(marineTapestry, scDeckDirect), itczDirectLift))) +
                            0.04f * respX * Smooth(0.02f, 0.18f, rawVeil + 0.15f * climTarget));

            float postOpenShare = Sat(postWakeLace * 1.15f * (1.0f - Smooth(0.30f, 0.65f, m.frontalLow)));
            float scShare = Sat(marineSc * 0.92f - Math.Max(stormWeight * m.frontalLow, itczConv * m.mcsCluster) * 0.92f);
            float midShare = Sat(Math.Max(stormWeight * m.frontalMid * (1.0f - 0.55f * Smooth(0.65f, 0.95f, m.frontalLow)),
                                          (0.35f * itczConv + 0.45f * orogCrest) * (1.0f - Smooth(0.55f, 0.88f, m.mcsCluster))) * (1.0f - scShare) * (1.0f - postOpenShare));
            float corePeak = Smooth(0.46f, 0.86f, m.planetary * 0.45f + m.mesoCells * 0.55f);
            float deepShare = Sat(Math.Max(itczConv * Smooth(0.45f, 0.88f, uLow) * (0.45f + 0.55f * m.mcsCluster),
                                           stormWeight * Smooth(0.50f, 0.90f, m.frontalLow) * 0.88f));
            float deepTrigger = Smooth(0.20f, 0.68f, deepShare * (0.52f + 0.48f * corePeak) + 0.32f * synLift * orogCrest + 0.20f * itczConv * Smooth(0.55f, 0.90f, effWet));
            float storm = Sat(Smooth(0.22f, 0.76f, effWet) * Smooth(0.60f, 0.95f, low) * deepTrigger * (1.0f + 0.35f * respY) * (1.0f - 0.92f * scShare));

            float tradeType = 0.20f + 0.11f * Smooth(0.20f, 0.75f, m.flowBands);
            float midType = 0.40f + 0.13f * m.frontalMid;
            float deepType = Sat(0.64f + 0.20f * deepShare + 0.22f * storm);
            float convection = tradeType;
            convection = FMath.Lerp(convection, midType, Sat(midShare * 1.15f));
            convection = FMath.Lerp(convection, deepType, Sat(deepTrigger * 1.10f + storm * 0.60f));
            convection = FMath.Lerp(convection, 0.15f + 0.04f * m.mesoCells, postOpenShare * 0.85f);
            convection = FMath.Lerp(convection, 0.06f + 0.05f * m.honeycomb, scShare);

            float curvedCirrus = Sat(m.curvedWisp * 0.88f);
            float zJet = (m.baseTex - 0.49f) * 2.6f + (m.wisps - 0.48f) * 3.0f + 3.75f * (curvedCirrus - 0.24f) + 0.95f * (uLow - 0.50f);
            float zAnvil = 2.65f * (m.anvilShield - 0.18f) + (m.wisps - 0.48f) * 2.8f + 2.85f * (curvedCirrus - 0.24f) + 0.85f * (uLow - 0.45f);
            float zWcb = 0.28f * zJet + 3.70f * (m.wcbCirrus - 0.14f) + 2.40f * (curvedCirrus - 0.24f) + (m.wisps - 0.48f) * 2.0f - 1.05f * m.polarCarve;
            float zHigh = FMath.Lerp(zJet, zAnvil, Sat(itczConv * 1.15f));
            zHigh = FMath.Lerp(zHigh, zWcb, stormWeight);
            float uHigh = 1.0f / (1.0f + (float)Math.Exp(-1.16f * FMath.Clamp(zHigh, -5.5f, 5.5f)));
            float highEnvTarget = Sat(highTarget * 1.20f + 0.045f * dryGuard) * (1.0f - 0.48f * polarDryCarve);
            float wHigh = Math.Max(1e-4f, Math.Min(0.42f, Math.Min(highEnvTarget, 1.0f - highEnvTarget) + 0.06f));
            float rawHigh = Smooth(1.0f - highEnvTarget - wHigh, 1.0f - highEnvTarget + wHigh, uHigh);
            float highSilk = 0.34f + 0.66f * Smooth(0.18f, 0.84f, m.wisps * 0.64f + m.fineGrain * 0.36f);
            float anvilOutflow = Smooth(0.16f, 0.56f, itczConv) * m.anvilShield * 0.70f;
            float high = Smooth(0.05f, 0.92f, Sat(Math.Max(rawHigh * highSilk * 1.13f, anvilOutflow))) * (1.0f - 0.78f * tropBelt * leeShadow);

            OrbisCloudVerticalSpan(convection, low, out float baseM, out float topM);
            sLow += low; sHigh += high; sConv += convection; sStorm += storm; sBase += baseM; sTop += topM;
        }
        float inv = 1.0f / phases.Length;
        return new BakedCloudSample((float)(sLow * inv), (float)(sHigh * inv), (float)(sConv * inv), (float)(sStorm * inv), (float)(sBase * inv), (float)(sTop * inv));
    }

    static void OrbisCloudVerticalSpan(float type, float coverage, out float baseM, out float topM)
    {
        float sc = 1.0f - Smooth(0.05f, 0.18f, type);
        float cu = Smooth(0.10f, 0.22f, type) * (1.0f - Smooth(0.48f, 0.70f, type));
        float cb = Smooth(0.50f, 0.96f, type);
        baseM = FMath.Lerp(760.0f, 560.0f, sc);
        baseM = FMath.Lerp(baseM, 1020.0f, cu * 0.88f);
        baseM = FMath.Lerp(baseM, 510.0f, cb);

        float lowReach = FMath.Lerp(0.76f, 1.0f, Smooth(0.14f, 0.82f, coverage));
        topM = FMath.Lerp(2200.0f, 3150.0f, Smooth(0.06f, 0.34f, type));
        topM = FMath.Lerp(topM, 5200.0f, Smooth(0.34f, 0.60f, type));
        topM = FMath.Lerp(topM, 12350.0f, cb);
        topM = baseM + (topM - baseM) * FMath.Lerp(lowReach, 1.0f, cb * 0.55f);
    }

    static int FindMoistMidlatSite(WorldData w, out float lonDeg, out float latDeg)
    {
        for (int y = LatToY(40f); y <= LatToY(56f); y++)
        for (int x = 0; x < w.Width; x++)
        {
            int idx = y * w.Width + x;
            if (FlatTerrainAnalysis.Ocean(w.Water[idx]))
            {
                lonDeg = ((x + 0.5f) / w.Width - 0.5f) * 360f;
                latDeg = ((y + 0.5f) / w.Height - 0.5f) * 180f;
                return idx;
            }
        }
        lonDeg = 0f; latDeg = 48f; return LatToY(48f) * w.Width;
    }

    static int FindWarmLandSite(WorldData w, out float lonDeg, out float latDeg)
    {
        int best = -1; float bestScore = -1e9f;
        for (int y = LatToY(-18f); y <= LatToY(18f); y++)
        for (int x = 0; x < w.Width; x++)
        {
            int idx = y * w.Width + x;
            if (FlatTerrainAnalysis.AreaWater(w.Water[idx]) || w.Elevation[idx] > 1200) continue;
            // Prefer interior land cells (so 3-cell patch stays predominantly land)
            int landN = 0;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (!FlatTerrainAnalysis.AreaWater(w.Water[FMath.Clamp(y + dy, 0, w.Height - 1) * w.Width + FMath.Wrap(x + dx, w.Width)])) landN++;
            float score = landN * 10f - Math.Abs(((y + 0.5f) / w.Height - 0.5f) * 180f);
            if (score > bestScore) { bestScore = score; best = idx; }
        }
        Check(best >= 0, "Could not locate warm tropical land site on fictional world");
        int bx = best % w.Width, by = best / w.Width;
        lonDeg = ((bx + 0.5f) / w.Width - 0.5f) * 360f;
        latDeg = ((by + 0.5f) / w.Height - 0.5f) * 180f;
        return best;
    }

    static int FindSubtropOceanSite(WorldData w, out float lonDeg, out float latDeg)
    {
        int best = -1; int bestScore = -1;
        for (int y = LatToY(-24f); y <= LatToY(-14f); y++)
        for (int x = 0; x < w.Width; x++)
        {
            int idx = y * w.Width + x;
            if (!FlatTerrainAnalysis.Ocean(w.Water[idx])) continue;
            int seaN = 0;
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
                if (FlatTerrainAnalysis.Ocean(w.Water[FMath.Clamp(y + dy, 0, w.Height - 1) * w.Width + FMath.Wrap(x + dx, w.Width)])) seaN++;
            if (seaN > bestScore) { bestScore = seaN; best = idx; }
        }
        Check(best >= 0, "Could not locate subtropical ocean site on fictional world");
        int bx = best % w.Width, by = best / w.Width;
        lonDeg = ((bx + 0.5f) / w.Width - 0.5f) * 360f;
        latDeg = ((by + 0.5f) / w.Height - 0.5f) * 180f;
        return best;
    }

    static void FindSteepSlopeSite(WorldData w, out int bestX, out int bestY, out float bestDhDx, out float bestDhDy)
    {
        bestX = 0; bestY = 0; bestDhDx = 1f; bestDhDy = 0f;
        float bestMag = 0f;
        float cellKm = 40030f / w.Width;
        for (int y = LatToY(-50f); y <= LatToY(50f); y++)
        {
            float latRad = ((y + 0.5f) / w.Height - 0.5f) * (float)Math.PI;
            float cosLat = Math.Max(0.25f, (float)Math.Cos(latRad));
            for (int x = 0; x < w.Width; x++)
            {
                int idx = y * w.Width + x;
                if (FlatTerrainAnalysis.AreaWater(w.Water[idx])) continue;
                int xW = x == 0 ? w.Width - 1 : x - 1, xE = x + 1 == w.Width ? 0 : x + 1;
                float eW = Math.Max(0, (int)w.Elevation[y * w.Width + xW]);
                float eE = Math.Max(0, (int)w.Elevation[y * w.Width + xE]);
                float eS = Math.Max(0, (int)w.Elevation[Math.Max(0, y - 1) * w.Width + x]);
                float eN = Math.Max(0, (int)w.Elevation[Math.Min(w.Height - 1, y + 1) * w.Width + x]);
                float gx = (eE - eW) / (2f * cosLat * cellKm);
                float gy = (eN - eS) / (2f * cellKm);
                float mag = (float)Math.Sqrt(gx * gx + gy * gy);
                if (mag > bestMag)
                {
                    bestMag = mag; bestX = x; bestY = y; bestDhDx = gx; bestDhDy = gy;
                }
            }
        }
        Check(bestMag > 2.0f, $"No steep mountain slope found (max={bestMag:F2} m/km)");
    }

    static double RegionMean(byte[] buf, int ch, float lon0, float lon1, float lat0, float lat1)
    {
        double sum = 0; int cnt = 0;
        for (int y = 0; y < GridH; y++)
        {
            float lat = ((y + 0.5f) / GridH - 0.5f) * 180f;
            if (lat < lat0 || lat > lat1) continue;
            for (int x = 0; x < GridW; x++)
            {
                float lon = ((x + 0.5f) / GridW - 0.5f) * 360f;
                if (lon < lon0 || lon > lon1) continue;
                sum += buf[(y * GridW + x) * 4 + ch] / 255.0;
                cnt++;
            }
        }
        return cnt > 0 ? sum / cnt : 0.0;
    }

    static double TropicalConvectionCentroidLat(byte[] forcing)
    {
        double num = 0, den = 1e-9;
        for (int y = 0; y < GridH; y++)
        {
            float lat = ((y + 0.5f) / GridH - 0.5f) * 180f;
            if (Math.Abs(lat) > 32f) continue;
            double cosW = Math.Cos(lat * Math.PI / 180.0);
            for (int x = 0; x < GridW; x++)
            {
                double w = (forcing[(y * GridW + x) * 4 + 1] / 255.0) * cosW;
                num += lat * w; den += w;
            }
        }
        return num / den;
    }

    static double HemisphericBeltCentroidAbsLat(byte[] buf, int ch, float minAbsLat, float maxAbsLat)
    {
        double num = 0, den = 1e-9;
        for (int y = 0; y < GridH; y++)
        {
            float absLat = Math.Abs(((y + 0.5f) / GridH - 0.5f) * 180f);
            if (absLat < minAbsLat || absLat > maxAbsLat) continue;
            for (int x = 0; x < GridW; x++)
            {
                double val = buf[(y * GridW + x) * 4 + ch] / 255.0;
                double w = val * val;
                num += absLat * w; den += w;
            }
        }
        return num / den;
    }

    static double GlobalMeanChannel(byte[] buf, int ch)
    {
        double sum = 0;
        for (int i = 0; i < GridW * GridH; i++) sum += buf[i * 4 + ch] / 255.0;
        return sum / (GridW * GridH);
    }

    static double UpperJetPeakAbsLat(ushort[] upperHalf)
    {
        double num = 0, den = 1e-9;
        for (int y = 0; y < GridH; y++)
        {
            float absLat = Math.Abs(((y + 0.5f) / GridH - 0.5f) * 180f);
            if (absLat < 12f || absLat > 84f) continue;
            double rowSpeed = 0;
            for (int x = 0; x < GridW; x++)
            {
                int p4 = (y * GridW + x) * 4;
                float u = HalfToFloat(upperHalf[p4]);
                float v = HalfToFloat(upperHalf[p4 + 1]);
                rowSpeed += Math.Sqrt(u * u + v * v);
            }
            rowSpeed /= GridW;
            double w = rowSpeed * rowSpeed * rowSpeed;
            num += absLat * w; den += w;
        }
        return num / den;
    }

    static double MeanZonalSurfaceWind(float[] windRgba, float absLat0, float absLat1)
    {
        double sum = 0; int cnt = 0;
        for (int y = 0; y < GridH; y++)
        {
            float absLat = Math.Abs(((y + 0.5f) / GridH - 0.5f) * 180f);
            if (absLat < absLat0 || absLat > absLat1) continue;
            for (int x = 0; x < GridW; x++)
            {
                sum += windRgba[(y * GridW + x) * 4];
                cnt++;
            }
        }
        return cnt > 0 ? sum / cnt : 0.0;
    }

    static double MeanByteDiff(byte[] a, byte[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]) / 255.0;
        return sum / a.Length;
    }

    static float HalfToFloat(ushort h)
    {
        int s = (h >> 15) & 1;
        int e = (h >> 10) & 31;
        int m = h & 1023;
        float val;
        if (e == 0) val = (float)(m * Math.Pow(2.0, -24));
        else if (e == 31) val = m == 0 ? float.PositiveInfinity : float.NaN;
        else val = (float)((1.0 + m / 1024.0) * Math.Pow(2.0, e - 15));
        return s != 0 ? -val : val;
    }

    static int LonToX(float lonDeg) => FMath.Wrap((int)Math.Floor((lonDeg / 360f + 0.5f) * GridW), GridW);
    static int LatToY(float latDeg) => FMath.Clamp((int)Math.Floor((latDeg / 180f + 0.5f) * GridH), 0, GridH - 1);
    static float Sat(float x) => FMath.Clamp(x, 0f, 1f);
    static float Smooth(float e0, float e1, float x)
    {
        float t = FMath.Clamp((x - e0) / Math.Max(1e-6f, e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    static void Check(bool cond, string message)
    {
        if (!cond) throw new Exception(message);
    }
}
