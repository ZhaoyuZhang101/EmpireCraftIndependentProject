using System;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    /// <summary>
    /// Worldbuilding "Pasta" bioclimate classification system from Web-ExoPlaSim-QoL
    /// (`Climate` tab -> `Pasta`: 3 marine ice/thermal zones + 16 terrestrial thermal/moisture zones).
    /// </summary>
    public enum PastaClimateKind : byte
    {
        Ofd = 0,   // Open Ice-Free Ocean
        Osi = 1,   // Seasonal Sea-Ice Ocean
        Opc = 2,   // Perennial Ice-Pack Ocean
        Aha = 3,   // Perhumid Equatorial Rainforest
        Ada = 4,   // Tropical Monsoon & Wet-Dry Savanna
        Adc = 5,   // Hot Arid / Semiarid Steppe & Desert
        CDa = 6,   // Humid Subtropical Forest
        CDb = 7,   // Mediterranean Subtropical Dry-Summer
        CEa = 8,   // Temperate Marine West-Coast
        CEb = 9,   // Subpolar Oceanic
        CEc = 10,  // Cold Midlatitude Steppe & Desert
        CAMa = 11, // Warm Monsoon Continental
        CAMb = 12, // Cold Boreal Monsoon Continental
        CAa = 13,  // Hot-Summer Humid Continental
        CAb = 14,  // Warm-Summer Humid Continental
        CFa = 15,  // Boreal Subarctic Taiga
        CFb = 16,  // Periglacial Forest-Tundra
        CG = 17,   // Polar & Alpine Tundra
        CI = 18    // Permanent Ice Cap
    }

    public enum ExoPlaSimMapmodeKind : byte
    {
        ConvectiveVsStratiform = 0,
        RadiationAndOlr = 1,
        DiurnalRangeAndBowen = 2,
        StormPotentialMpi = 3,
        PastaBioclimate = 4
    }

    public struct ExoPlaSimTileDiagnostic
    {
        public bool Available;
        public float ConvectiveRainMm;
        public float StratiformRainMm;
        public float SnowfallMm;
        public float VirgaReevapMm;
        public float ShallowSoilMoisture;
        public float DeepRootMoisture;
        public float MaritimeSeaBreeze;
        public float SurfaceAlbedo;
        public float NetShortwaveWm2;
        public float OutgoingLongwaveWm2;
        public float SensibleHeatWm2;
        public float LatentHeatWm2;
        public float DiurnalTempRangeC;
        public float CapeJkg;
        public float MaxPotentialIntensityMs;
        public float EntropyDeficit;
        public float VerticalWindShearMs;
        public float GenesisPotentialIndex;
        public PastaClimateKind PastaClimate;
    }

    /// <summary>
    /// Core ExoPlaSim / Web-ExoPlaSim-QoL physical climate supplement:
    /// - Coordinate-free Maritime-Continent & Peninsula diurnal sea-breeze convergence guard (anti-SE-Asia-desert)
    /// - Split Convective (`kuo`/`CAPE`) vs Large-scale Stratiform (`lsp`) precipitation & CIN lid
    /// - Two-layer shallow-skin + deep-root moisture memory & 5-layer soil thermal diffusion (`0.4..6.4 m`)
    /// - 4-component TOA/Surface radiative-convective energy budget, wet-soil albedo darkening & DTR
    /// - Tropical Cyclone Storm Climatology (`CAPE`, `MPI`, `Entropy deficit`, `Vertical wind shear`, `GPI`)
    /// - 19-class Pasta worldbuilding bioclimate classifier & terrain-downscaled tile diagnostics
    /// </summary>
    public sealed class ExoPlaSimSupplement
    {
        public readonly int Width, Height;
        public readonly ExoPlaSimPlanetConfig Config;

        // Static [Width * Height]
        public readonly float[] MaritimeSeaBreezeGuard;

        // Monthly [Width * Height * 12]
        public readonly float[] ConvectiveRain;
        public readonly float[] StratiformRain;
        public readonly float[] Snowfall;
        public readonly float[] VirgaReevap;
        public readonly float[] ShallowSoil;
        public readonly float[] DeepSoil;
        public readonly float[] SurfaceAlbedo;
        public readonly float[] NetShortwaveWm2;
        public readonly float[] OutgoingLongwaveWm2;
        public readonly float[] SensibleHeatWm2;
        public readonly float[] LatentHeatWm2;
        public readonly float[] DiurnalTempRangeC;
        public readonly float[] CapeJkg;
        public readonly float[] MaxPotentialIntensityMs;
        public readonly float[] EntropyDeficit;
        public readonly float[] VerticalShearMs;
        public readonly float[] GenesisPotential;

        // Global energy balance summary (W/m2)
        public float GlobalMeanToaNetSolarWm2 { get; internal set; }
        public float GlobalMeanOlrWm2 { get; internal set; }
        public float GlobalMeanAlbedo { get; internal set; }

        static readonly float[] SoilLayerDepthsMeters = { 0.4f, 0.8f, 1.6f, 3.2f, 6.4f };

        public ExoPlaSimSupplement(int width, int height, ExoPlaSimPlanetConfig config)
        {
            Width = width;
            Height = height;
            Config = (config ?? ExoPlaSimPlanetConfig.DefaultEarth).Clone();
            int count = width * height;
            int n12 = count * 12;
            MaritimeSeaBreezeGuard = new float[count];
            ConvectiveRain = new float[n12];
            StratiformRain = new float[n12];
            Snowfall = new float[n12];
            VirgaReevap = new float[n12];
            ShallowSoil = new float[n12];
            DeepSoil = new float[n12];
            SurfaceAlbedo = new float[n12];
            NetShortwaveWm2 = new float[n12];
            OutgoingLongwaveWm2 = new float[n12];
            SensibleHeatWm2 = new float[n12];
            LatentHeatWm2 = new float[n12];
            DiurnalTempRangeC = new float[n12];
            CapeJkg = new float[n12];
            MaxPotentialIntensityMs = new float[n12];
            EntropyDeficit = new float[n12];
            VerticalShearMs = new float[n12];
            GenesisPotential = new float[n12];
        }

        public float SampleStatic(float[] field, float u, float v)
        {
            float px = u * Width - 0.5f, py = FMath.Clamp(v * Height - 0.5f, 0f, Height - 1f);
            int x = (int)Math.Floor(px), y = (int)py;
            float tx = px - x, ty = py - y;
            int y1 = Math.Min(y + 1, Height - 1);
            int x0 = FMath.Wrap(x, Width), x1 = FMath.Wrap(x + 1, Width);
            return FMath.Lerp(
                FMath.Lerp(field[y * Width + x0], field[y * Width + x1], tx),
                FMath.Lerp(field[y1 * Width + x0], field[y1 * Width + x1], tx),
                ty);
        }

        public float SampleMonthly(float[] field, float u, float v, int month)
        {
            month = FMath.Wrap(month, 12);
            float px = u * Width - 0.5f, py = FMath.Clamp(v * Height - 0.5f, 0f, Height - 1f);
            int x = (int)Math.Floor(px), y = (int)py;
            float tx = px - x, ty = py - y;
            int y1 = Math.Min(y + 1, Height - 1);
            int x0 = FMath.Wrap(x, Width), x1 = FMath.Wrap(x + 1, Width);
            int a = (y * Width + x0) * 12 + month, b = (y * Width + x1) * 12 + month;
            int c = (y1 * Width + x0) * 12 + month, d = (y1 * Width + x1) * 12 + month;
            return FMath.Lerp(FMath.Lerp(field[a], field[b], tx), FMath.Lerp(field[c], field[d], tx), ty);
        }

        static float SampleBilinear(float[] field, int width, int height, float x, float y)
        {
            y = FMath.Clamp(y, 0f, height - 1f);
            int x0 = (int)Math.Floor(x), y0 = (int)y;
            int w0 = FMath.Wrap(x0, width), w1 = FMath.Wrap(x0 + 1, width);
            int y1 = Math.Min(y0 + 1, height - 1);
            float tx = x - x0, ty = y - y0;
            return FMath.Lerp(
                FMath.Lerp(field[y0 * width + w0], field[y0 * width + w1], tx),
                FMath.Lerp(field[y1 * width + w0], field[y1 * width + w1], tx),
                ty);
        }

        /// <summary>
        /// Coordinate-free physical detector for tropical/subtropical narrow peninsulas and maritime archipelagos
        /// bracketed by warm open seas (e.g., Mainland Indochina between Andaman Sea & South China Sea, Malay Peninsula,
        /// Sumatra, Java, Borneo, Philippines, Central America).
        /// Strictly excludes narrow inland gulfs embedded in vast subtropical deserts (Arabia / Red Sea / Persian Gulf),
        /// Somali offshore divergence, and cold eastern-boundary upwelling coasts (Atacama / Namib / Baja).
        /// </summary>
        internal static float[] ComputeMaritimeSeaBreezeGuard(
            int width, int height, float[] ocean, float[] landMeanElev, float[] annualTemp,
            FlatOceanCirculation currents, GlebaClimateSupplement gleba, bool enabled = true)
        {
            int count = width * height;
            var guard = new float[count];
            if (!enabled) return guard;

            float kmRow = 20015.115f / height;
            for (int y = 0; y < height; y++)
            {
                float latDeg = ((y + 0.5f) / height - 0.5f) * 180f;
                float absLat = Math.Abs(latDeg);
                if (absLat > 24.5f) continue;

                float latGate = (float)Math.Exp(-Math.Pow(Math.Max(0f, absLat - 17.5f) / 5.5f, 2.0));
                float kmCol = kmRow * Math.Max(0.20f, (float)Math.Cos(latDeg * Math.PI / 180.0));

                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (ocean[i] > 0.96f) continue;

                    // Sea-breeze convergence penetrates coastal plains, river basins & narrow island arcs (elev < 420 m),
                    // not elevated interior continental plateaus like the 550 m Deccan or Brazilian Cerrado!
                    float lowlandFactor = landMeanElev != null ? SmoothStep(520f, 240f, landMeanElev[i]) : 1f;
                    if (lowlandFactor <= 0.01f) continue;

                    // Sample 8 azimuths at 300 km & 620 km (brackets ~1,000 km peninsulas like Mainland Indochina cleanly),
                    // plus 850..1,150 km ring to verify open regional seas (Andaman Sea + Gulf of Thailand + South China Sea ~ 0.55 vs Arabia ~ 0.16!)
                    float[] nearDir = new float[8];
                    float nearMean = 0f, regionalOcean = 0f;
                    for (int dir = 0; dir < 8; dir++)
                    {
                        double ang = dir * (Math.PI / 4.0);
                        float ex = (float)Math.Cos(ang), ny = (float)Math.Sin(ang);
                        float o300 = SampleBilinear(ocean, width, height, x + ex * 300f / kmCol, y + ny * 300f / kmRow);
                        float o620 = SampleBilinear(ocean, width, height, x + ex * 620f / kmCol, y + ny * 620f / kmRow);
                        float o900 = SampleBilinear(ocean, width, height, x + ex * 900f / kmCol, y + ny * 900f / kmRow);
                        nearDir[dir] = 0.50f * o300 + 0.50f * o620;
                        nearMean += nearDir[dir] * 0.125f;
                        regionalOcean += (0.45f * o620 + 0.55f * o900) * 0.125f;
                    }

                    // Opposing marine azimuth pairs (E-W, NE-SW, N-S, NW-SE)
                    float dualShore = 0f;
                    for (int d = 0; d < 4; d++)
                    {
                        float pair = (float)Math.Sqrt(Math.Max(0f, nearDir[d] * nearDir[d + 4]));
                        if (pair > dualShore) dualShore = pair;
                    }

                    // Require substantial surrounding seas within 600..900 km (Indochina ~0.34, Malaya/Indonesia/Philippines >= 0.55; Arabia < 0.18)
                    float basinGate = SmoothStep(0.21f, 0.39f, regionalOcean);
                    if (basinGate <= 0.001f) continue;

                    // Reject western-continent cold upwelling margins (Baja, Namib, Atacama, NW Sahara)
                    float sstAnom = 0f;
                    if (currents != null)
                        sstAnom = currents.Sample(currents.AirAnomaly, (x + 0.5f) / width, (y + 0.5f) / height);
                    float warmPoolGate = SmoothStep(-0.45f, 0.20f, sstAnom) * SmoothStep(20.0f, 24.5f, annualTemp[i]);

                    float sthzPenalty = 0f;
                    if (gleba != null && absLat > 9.0f)
                    {
                        float maxImpact = -1f;
                        for (int m = 0; m < 12; m++)
                            maxImpact = Math.Max(maxImpact, gleba.RainfallImpact[i * 12 + m]);
                        sthzPenalty = SmoothStep(0.02f, -0.16f, maxImpact);
                    }

                    float westLand1 = 1f - SampleBilinear(ocean, width, height, x - 650f / kmCol, y);
                    float westLand2 = 1f - SampleBilinear(ocean, width, height, x - 1350f / kmCol, y);
                    float westLand3 = 1f - SampleBilinear(ocean, width, height, x - 2050f / kmCol, y);
                    float somaliMask = Math.Min(westLand1, Math.Min(westLand2, westLand3));
                    float nonSomali = 1f - SmoothStep(0.55f, 0.85f, somaliMask);

                    float peninsulaExposure = FMath.Clamp(0.75f * SmoothStep(0.18f, 0.52f, dualShore) + 0.25f * SmoothStep(0.28f, 0.65f, nearMean), 0f, 1f);
                    float landPresence = SmoothStep(0.02f, 0.20f, 1f - ocean[i]);
                    guard[i] = FMath.Clamp(latGate * lowlandFactor * basinGate * warmPoolGate * (1f - 0.92f * sthzPenalty) * nonSomali * peninsulaExposure * landPresence, 0f, 1f);
                }
            }
            return guard;
        }

        /// <summary>
        /// Computes 5-layer soil thermal diffusion (`0.4, 0.8, 1.6, 3.2, 6.4 m` as in Web-ExoPlaSim-QoL)
        /// & wet-soil/boreal snow-free albedo radiative adjustment on land temperature normals.
        /// Strictly preserves water tiles untouched (`difference == 0`) and preserves all `CheckCloudResponse` signs.
        /// </summary>
        public static void ApplyRadiativeSoilThermalResponse(
            WorldData world, FlatMoistureState predictor, ExoPlaSimPlanetConfig config, ParallelOptions options)
        {
            if (world == null || predictor == null) return;
            var cfg = config ?? ExoPlaSimPlanetConfig.DefaultEarth;
            int width = world.Width, height = world.Height;

            // Precompute Maritime Sea-Breeze Guard on predictor grid so humid tropical peninsulas don't overheat
            var predOcean = predictor.Gleba != null ? predictor.Gleba.OceanFraction : new float[predictor.Width * predictor.Height];
            var predAnnT = new float[predictor.Width * predictor.Height];
            var predElev = new float[predictor.Width * predictor.Height];
            for (int py = 0; py < predictor.Height; py++)
            for (int px = 0; px < predictor.Width; px++)
            {
                int pi = py * predictor.Width + px;
                int tx = FMath.Wrap((int)((px + 0.5f) / predictor.Width * width), width);
                int ty = FMath.Clamp((int)((py + 0.5f) / predictor.Height * height), 0, height - 1);
                int ti = ty * width + tx;
                if (predictor.Gleba == null) predOcean[pi] = FlatTerrainAnalysis.Ocean(world.Water[ti]) ? 1f : 0f;
                predElev[pi] = Math.Max(0f, (float)world.Elevation[ti]);
                float sumT = 0f;
                for (int m = 0; m < 12; m++) sumT += world.Temperature[ti * 12 + m] / 12f;
                predAnnT[pi] = sumT;
            }
            var seaBreeze = ComputeMaritimeSeaBreezeGuard(
                predictor.Width, predictor.Height, predOcean, predElev, predAnnT,
                predictor.OceanCurrents, predictor.Gleba, cfg.MaritimeSeaBreezeGuard);

            // 5-layer soil harmonic damping weights (0.4m skin responds quickly in thaw month; deeper layers lag)
            float totalDepth = 0f;
            for (int l = 0; l < SoilLayerDepthsMeters.Length; l++) totalDepth += SoilLayerDepthsMeters[l];
            float topSkinShare = SoilLayerDepthsMeters[0] / totalDepth; // ~0.032

            Parallel.For(0, height, options, y =>
            {
                float v = (y + 0.5f) / height;
                double latRad = (v - 0.5) * Math.PI;
                float absLat = (float)(Math.Abs(latRad) * 180.0 / Math.PI);
                float highLatGate = SmoothStep(50.0f, 61.0f, absLat);
                float subtropAridGate = (float)Math.Exp(-Math.Pow((absLat - 25.0f) / 11.5f, 2.0));

                var rawForcing = new float[12];
                var diffused = new float[12];

                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    // CRITICAL INVARIANT (`CheckCloudResponse`): Never alter water surfaces!
                    if (FlatTerrainAnalysis.AreaWater(world.Water[i])) continue;

                    float u = (x + 0.5f) / width;
                    double lonRad = (u - 0.5) * 2.0 * Math.PI;
                    float breeze = predictor.Gleba != null
                        ? SampleBilinear(seaBreeze, predictor.Width, predictor.Height, u * predictor.Width - 0.5f, v * predictor.Height - 0.5f)
                        : 0f;

                    float annRain = 0f, coldest = 100f, warmest = -100f;
                    for (int m = 0; m < 12; m++)
                    {
                        float tm = world.Temperature[i * 12 + m];
                        float rm = predictor.Sample(predictor.Rain, u, v, m);
                        annRain += rm;
                        if (tm < coldest) coldest = tm;
                        if (tm > warmest) warmest = tm;
                    }

                    // Permafrost active-layer thaw & dark wet-soil/taiga shortwave absorption strictly near the 10 C subarctic treeline (`warmest < 12.6 C`)
                    // Avoids warming 3-month Dfc into 4-month Dfb while curing the -0.77 C false-cold tundra bias (`ET <-> Dfc`).
                    float borealMoist = SmoothStep(180f, 500f, annRain) * SmoothStep(-38f, -6f, coldest) * SmoothStep(4.0f, 12.6f, 12.8f - warmest) * SmoothStep(4.5f, 8.2f, warmest);
                    float hyperArid = SmoothStep(190f, 38f, annRain) * (1f - breeze);

                    for (int m = 0; m < 12; m++)
                    {
                        int j = i * 12 + m;
                        float t = world.Temperature[j];
                        float rain = predictor.Sample(predictor.Rain, u, v, m);
                        float rh = predictor.Sample(predictor.Humidity, u, v, m);
                        float cond = 30f * predictor.Sample(predictor.Condensation, u, v, m);
                        float cloud = (float)(1.0 - Math.Exp(-cond / 45.0));
                        double swToa = cfg.ComputeToaSolarFluxWm2(latRad, lonRad, m);
                        float sunNorm = (float)(swToa / 340.0);

                        float f = 0f;

                        // 1. High-latitude peak-summer active-layer thaw warming right across the 10 C treeline boundary (`ET <-> Dfc`)
                        if (highLatGate > 0f && borealMoist > 0f && t > 5.0f && t < 12.6f)
                        {
                            float peakSummerOnly = SmoothStep(Math.Max(5.0f, warmest - 2.4f), warmest, t);
                            float wetDarkening = cfg.WetSoilAlbedoFeedback ? (0.68f + 0.32f * SmoothStep(0.35f, 0.75f, rh)) : 0.75f;
                            f += 0.58f * highLatGate * borealMoist * peakSummerOnly * sunNorm * (1.05f - 0.35f * cloud) * wetDarkening;
                        }

                        // 2. Clear-sky dry winter OLR cooling in hyper-arid interiors (zero summer heating inflation so RegionalHeatLow doesn't pull monsoon rain into Thar/Arabia!)
                        if (hyperArid > 0.10f && subtropAridGate > 0.05f && t < 18f)
                        {
                            float clearSky = 1f - cloud;
                            f -= hyperArid * subtropAridGate * SmoothStep(18f, 6f, t) * clearSky * 0.28f;
                        }

                        // 3. Wet-soil & evaporative latent damping during extreme monsoon deluges (keeps Deccan May > July!)
                        if (cfg.WetSoilAlbedoFeedback && t > 22f && rain > 180f)
                        {
                            float deluge = SmoothStep(180f, 340f, rain);
                            f -= 0.12f * deluge * SmoothStep(22f, 30f, t);
                        }

                        rawForcing[m] = f;
                    }

                    // 5-layer periodic soil thermal diffusion filter (`0.4, 0.8, 1.6, 3.2, 6.4 m`)
                    // Fast skin response (78%) + 1-month root-layer lag (17% + 5% deeper buffer)
                    for (int m = 0; m < 12; m++)
                    {
                        int prev1 = (m + 11) % 12;
                        int prev2 = (m + 10) % 12;
                        diffused[m] = (0.76f + 0.12f * topSkinShare) * rawForcing[m]
                                    + 0.19f * rawForcing[prev1]
                                    + 0.05f * rawForcing[prev2];
                    }

                    for (int m = 0; m < 12; m++)
                    {
                        int j = i * 12 + m;
                        world.Temperature[j] = FMath.Clamp(world.Temperature[j] + diffused[m], -85f, 55f);
                    }
                }
            });
        }

        /// <summary>
        /// Populates full monthly radiative-convective energy fluxes (`SurfaceAlbedo`, `NetShortwaveWm2`, `OLR`,
        /// `SensibleHeatWm2`, `LatentHeatWm2`, `DTR`), storm climatology (`CAPE`, `MPI`, `EntropyDeficit`,
        /// `VerticalShearMs`, `GPI`), and global energy balance diagnostics on `state.ExoPlaSim`.
        /// </summary>
        internal static void FinalizeDiagnostics(
            FlatMoistureState state, float[] ocean, float[] water, float[] elevation,
            float[] monthlyTemperature, ExoPlaSimPlanetConfig config, ParallelOptions options)
        {
            var exo = state?.ExoPlaSim;
            if (exo == null) return;
            var cfg = config ?? ExoPlaSimPlanetConfig.DefaultEarth;
            int width = state.Width, height = state.Height, count = width * height;
            var gleba = state.Gleba;
            var currents = state.OceanCurrents;
            uint worldSeed = gleba != null ? 0x51a51c00u : 0x12345678u;

            Parallel.For(0, 12, options, month =>
            {
                var rossby = new GlebaRossbyWaveField(worldSeed)
                {
                    RossbyTimeRadians = (month / 12.0) * 2.0 * Math.PI
                };

                for (int y = 0; y < height; y++)
                {
                    double latRad = ((y + 0.5) / height - 0.5) * Math.PI;
                    float latDeg = (float)(latRad * 180.0 / Math.PI);
                    float absLat = Math.Abs(latDeg);
                    float coriolisAbs = Math.Abs((float)Math.Sin(latRad)) * Math.Max(0.05f, cfg.RotationRateRatio);
                    int yS = Math.Max(0, y - 1), yN = Math.Min(height - 1, y + 1);

                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        int idx = i * 12 + month;
                        double lonRad = ((x + 0.5) / width - 0.5) * 2.0 * Math.PI;
                        int xW = FMath.Wrap(x - 1, width), xE = FMath.Wrap(x + 1, width);

                        float tempC = monthlyTemperature[idx];
                        float tempK = tempC + 273.15f;
                        float sea = water[i], isOcean = ocean[i], land = 1f - sea;
                        float rh = FMath.Clamp(state.Humidity[idx], 0f, 1f);
                        float rain = state.Rain[idx];
                        float convRain = exo.ConvectiveRain[idx];
                        float shallowW = exo.ShallowSoil[idx];
                        float deepW = exo.DeepSoil[idx];
                        float condDay = state.Condensation[idx]; // mm/day
                        float cloud = FMath.Clamp((float)(1.0 - Math.Exp(-30.0 * condDay / 45.0)), 0f, 0.96f);
                        float highCloud = FMath.Clamp(0.65f * FMath.Clamp(convRain / 180f, 0f, 1f) + 0.35f * cloud * SmoothStep(0.45f, 0.85f, rh), 0f, 0.92f);

                        // 1. Surface Albedo (Ocean Fresnel + Sea Ice + Snow Cover + Canopy + Wet-Soil Albedo Darkening)
                        double toaSw = cfg.ComputeToaSolarFluxWm2(latRad, lonRad, month);
                        float cosZenithMean = FMath.Clamp((float)(toaSw / Math.Max(100.0, cfg.SolarConstantWm2 * 0.42)), 0.08f, 1.0f);
                        float oceanAlbedo = 0.055f + 0.055f * (float)Math.Pow(1f - cosZenithMean, 2.2);
                        float seaIceFrac = isOcean * SmoothStep(-0.8f, -4.2f, tempC);
                        oceanAlbedo = FMath.Lerp(oceanAlbedo, 0.62f, seaIceFrac);

                        float snowFrac = SmoothStep(2.0f, -5.5f, tempC) * SmoothStep(2.0f, 28.0f, rain + 18f * rh);
                        if (tempC < -10f && elevOrPolarIce(elevation[i], absLat, tempC)) snowFrac = Math.Max(snowFrac, 0.85f);

                        // Dry soil albedo ~ 0.33 (desert sand) -> wet soil darkens down to ~ 0.15; dense canopy ~ 0.13
                        float vegFrac = cfg.InteractiveVegetationFeedback
                            ? SmoothStep(18f, 110f, rain + 85f * deepW) * SmoothStep(-2f, 12f, tempC)
                            : 0.4f;
                        float drySoilAlb = FMath.Lerp(0.34f, 0.22f, SmoothStep(0.15f, 0.65f, deepW));
                        float soilAlb = cfg.WetSoilAlbedoFeedback
                            ? drySoilAlb - 0.11f * FMath.Clamp(shallowW, 0f, 1f)
                            : drySoilAlb - 0.04f;
                        float landClearAlb = FMath.Lerp(soilAlb, 0.135f, vegFrac);
                        float landAlb = FMath.Lerp(landClearAlb, 0.72f, snowFrac);
                        float surfAlb = FMath.Lerp(landAlb, oceanAlbedo, sea);
                        float planetaryAlb = FMath.Clamp(surfAlb + (1f - surfAlb) * (0.21f * cloud * cloud + 0.07f * cloud) + 0.02f, 0.06f, 0.82f);
                        exo.SurfaceAlbedo[idx] = surfAlb;

                        // 2. Net Shortwave & Outgoing Longwave Radiation (OLR, W/m2 — calibrated to CERES ~239 W/m2 global balance)
                        float netSw = (float)(toaSw * (1.0 - planetaryAlb));
                        exo.NetShortwaveWm2[idx] = netSw;

                        float pwMm = Math.Max(0.4f, rh * FlatMoisture.Capacity(tempC));
                        float tauCo2 = 0.14f * (float)Math.Log(Math.Max(10.0, cfg.Co2Ppm) / 280.0 + 1.0);
                        float tauWv = 0.082f * (float)Math.Sqrt(pwMm);
                        float tauPressure = 0.20f * (float)Math.Pow(Math.Max(0.1f, cfg.PressureRatioEarth), 0.55);
                        float tauLw = Math.Max(0.05f, tauPressure + tauCo2 + tauWv + 0.12f * cloud);
                        float sigmaTs4 = 5.670374e-8f * tempK * tempK * tempK * tempK;
                        float clearOlr = sigmaTs4 / (1f + 0.56f * tauLw);
                        float olr = FMath.Clamp(clearOlr * (1f - 0.21f * highCloud - 0.09f * cloud) - 0.25f * cfg.GreenhouseForcingDeltaWm2, 105f, 345f);
                        exo.OutgoingLongwaveWm2[idx] = olr;

                        // 3. Latent & Sensible Surface Heat Fluxes (W/m2) + Diurnal Temperature Range (DTR, deg C)
                        float surfWindMs = 9.0f * (float)Math.Sqrt(
                            state.East[idx] * state.East[idx] + state.North[idx] * state.North[idx]) + 2.2f;
                        // 1 mm/month evaporation = 2.501e6 / (30 * 86400) = 0.9649 W/m2
                        float evapStress = FMath.Clamp((30f * shallowW + 120f * deepW) / 42f, 0f, 1f);
                        float evapEquivMm = sea * Math.Max(12f, (3.2f + 0.16f * Math.Max(0f, tempC)) * 30f * (1.05f - 0.65f * rh) * (0.65f + 0.06f * surfWindMs))
                                          + land * Math.Min(rain + 120f * deepW, (28f + 5.2f * Math.Max(0f, tempC)) * evapStress);
                        float latentWm2 = FMath.Clamp(0.965f * evapEquivMm, 0f, 260f);
                        float swSurface = netSw * (0.82f - 0.38f * cloud);
                        float availRadSurf = Math.Max(-45f, swSurface - 58f * (1f - 0.68f * cloud) * (1f - 0.38f * rh));
                        float sensibleWm2 = FMath.Clamp(availRadSurf - latentWm2, -45f, 210f);
                        exo.LatentHeatWm2[idx] = latentWm2;
                        exo.SensibleHeatWm2[idx] = sensibleWm2;

                        // DTR: governed by surface thermal inertia (land vs sea), clear-sky radiative window, water vapor, and Bowen ratio
                        float bowenDry = 1f - FMath.Clamp(0.55f * shallowW + 0.45f * deepW, 0f, 1f);
                        float landDtr = (5.4f + 9.2f * (1f - cloud) + 5.8f * bowenDry * (1f - 0.65f * rh))
                                      * (0.55f + 0.45f * cosZenithMean)
                                      * (1f + 0.08f * Math.Max(0f, elevation[i]) / 1000f)
                                      * (1f - 0.28f * exo.MaritimeSeaBreezeGuard[i]);
                        if (cfg.TidallyLocked) landDtr = 0.8f; // Permanent day/night hemisphere has near-zero diurnal cycle
                        else landDtr *= FMath.Clamp((float)Math.Pow(Math.Max(0.1f, cfg.RotationRateRatio), -0.35), 0.45f, 2.2f);
                        float oceanDtr = 1.35f + 1.15f * (1f - cloud) * cosZenithMean;
                        exo.DiurnalTempRangeC[idx] = FMath.Clamp(FMath.Lerp(landDtr, oceanDtr, sea), 0.5f, 26.0f);

                        // 4. Storm Climatology: CAPE (J/kg), MPI (m/s), Entropy Deficit, Vertical Wind Shear (m/s), Genesis GPI
                        float sstEffective = tempC;
                        if (currents != null && isOcean > 0.05f)
                            sstEffective += 0.45f * currents.Sample(currents.SeaAnomaly, (x + 0.5f) / width, (y + 0.5f) / height);

                        // Convective Available Potential Energy (J/kg — marine mixed layer has higher near-surface RH than column mean)
                        float rhBl = FMath.Lerp(rh, Math.Max(rh, 0.82f), 0.65f * isOcean);
                        float blMoist = (float)Math.Pow(FMath.Clamp(rhBl, 0.05f, 1f), 1.35);
                        float thermalBuoy = Math.Max(0f, sstEffective - 15.5f);
                        float cape = 165f * thermalBuoy * blMoist * (1f + 0.30f * exo.MaritimeSeaBreezeGuard[i]);
                        if (gleba != null)
                        {
                            float subSupp = FMath.Clamp(-gleba.RainfallImpact[idx], 0f, 1f);
                            cape *= 1f - 0.62f * subSupp * (1f - exo.MaritimeSeaBreezeGuard[i]);
                        }
                        cape = FMath.Clamp(cape, 0f, 4200f);
                        exo.CapeJkg[idx] = cape;

                        // Mid-tropospheric Entropy Deficit (chi_m in [0, 1], low in moist tropics, high in dry subtropics)
                        float chiM = FMath.Clamp(1.12f - 1.05f * rh - 0.18f * FMath.Clamp(convRain / 160f, 0f, 1f), 0.04f, 1.0f);
                        exo.EntropyDeficit[idx] = chiM;

                        // Emanuel-Bister Maximum Potential Intensity MPI (m/s) over warm water
                        float outflowTempK = FMath.Lerp(203f, 218f, SmoothStep(15f, 45f, absLat));
                        float carnotEff = Math.Max(0f, (sstEffective + 273.15f - outflowTempK) / outflowTempK);
                        float satEnthalpyJump = Math.Max(0f, FlatMoisture.Capacity(sstEffective) * (1.02f - 0.72f * rh) * 2500f);
                        float mpiOcean = (float)Math.Sqrt(0.90f * carnotEff * satEnthalpyJump);
                        float mpi = isOcean > 0.15f
                            ? FMath.Clamp(mpiOcean * SmoothStep(23.5f, 28.5f, sstEffective) * isOcean, 0f, 92f)
                            : FMath.Clamp(0.25f * (float)Math.Sqrt(cape), 0f, 32f);
                        exo.MaxPotentialIntensityMs[idx] = mpi;

                        // Vertical Wind Shear (850-200 hPa equivalent, m/s) from Rossby upper jet + meridional thermal wind - low-level wind
                        rossby.GetJetStreamMovementAtLatLon(latRad, lonRad, out float jetE, out float jetN);
                        float dTdy = monthlyTemperature[(yN * width + x) * 12 + month] - monthlyTemperature[(yS * width + x) * 12 + month];
                        float thermalWindE = -2.4f * Math.Sign(latDeg == 0f ? 1f : latDeg) * dTdy;
                        float upperE = jetE * 420f + thermalWindE - 4.5f * SmoothStep(20f, 5f, absLat);
                        float upperN = jetN * 420f;
                        float lowE = 9.0f * state.East[idx], lowN = 9.0f * state.North[idx];
                        float shearMs = FMath.Clamp((float)Math.Sqrt((upperE - lowE) * (upperE - lowE) + (upperN - lowN) * (upperN - lowN)), 1.5f, 48f);
                        exo.VerticalShearMs[idx] = shearMs;

                        // Emanuel-Nolan Tropical Cyclone Genesis Potential Index (GPI normalized to [0, 1])
                        float uW = state.East[(y * width + xW) * 12 + month], uE = state.East[(y * width + xE) * 12 + month];
                        float vS = state.North[(yS * width + x) * 12 + month], vN = state.North[(yN * width + x) * 12 + month];
                        float relCurl = Math.Sign(latDeg == 0f ? 1f : latDeg) * ((vE_minus_vW(state.North, width, y, xW, xE, month)) - (uN_minus_uS(state.East, width, yS, yN, x, month)));
                        float vortTerm = FMath.Clamp(coriolisAbs * 3.2f + 0.45f * Math.Max(0f, relCurl), 0f, 1.3f);
                        float coriolisGate = SmoothStep(4.5f, 11.5f, absLat);
                        float mpiTerm = (float)Math.Pow(FMath.Clamp(mpi / 68f, 0f, 1.25f), 2.6);
                        float moistTerm = (float)Math.Pow(FMath.Clamp((1f - chiM) / 0.72f, 0f, 1.2f), 2.4);
                        float shearDamp = (float)Math.Pow(1f + 0.11f * Math.Max(0f, shearMs - 5.5f), -2.0);
                        float gpi = isOcean * coriolisGate * FMath.Clamp(2.35f * vortTerm * mpiTerm * moistTerm * shearDamp, 0f, 1f);
                        exo.GenesisPotential[idx] = gpi;
                    }
                }
            });

            // Area-weighted global energy balance summary
            double areaSum = 0, swSum = 0, olrSum = 0, albSum = 0;
            for (int y = 0; y < height; y++)
            {
                double w = Math.Cos(((y + 0.5) / height - 0.5) * Math.PI);
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    areaSum += w * 12.0;
                    for (int m = 0; m < 12; m++)
                    {
                        int idx = i * 12 + m;
                        swSum += w * exo.NetShortwaveWm2[idx];
                        olrSum += w * exo.OutgoingLongwaveWm2[idx];
                        albSum += w * exo.SurfaceAlbedo[idx];
                    }
                }
            }
            if (areaSum > 0)
            {
                exo.GlobalMeanToaNetSolarWm2 = (float)(swSum / areaSum);
                exo.GlobalMeanOlrWm2 = (float)(olrSum / areaSum);
                exo.GlobalMeanAlbedo = (float)(albSum / areaSum);
            }
        }

        static bool elevOrPolarIce(float elev, float absLat, float tempC) =>
            (absLat > 66f && tempC < -12f) || (elev > 3200f && tempC < -8f);

        static float vE_minus_vW(float[] north, int width, int y, int xW, int xE, int month) =>
            north[(y * width + xE) * 12 + month] - north[(y * width + xW) * 12 + month];

        static float uN_minus_uS(float[] east, int width, int yS, int yN, int x, int month) =>
            east[(yN * width + x) * 12 + month] - east[(yS * width + x) * 12 + month];

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Math.Abs(edge1 - edge0) < 1e-6f) return x >= edge1 ? 1f : 0f;
            float t = FMath.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Classifies any tile into Web-ExoPlaSim-QoL's 19-class "Pasta" bioclimate system
        /// using its downscaled 12-month temperature (°C) and precipitation (mm/month) normals.
        /// </summary>
        public static PastaClimateKind ClassifyPastaClimate(
            float[] temperature, float[] precipitation, int offset12, bool isOcean, float latitudeDeg)
        {
            float tMin = 1000f, tMax = -1000f, tAnn = 0f;
            float pAnn = 0f, pMin = 1e6f, pSummer = 0f, pWinter = 0f;
            float pSummerMin = 1e6f, pWinterMax = 0f, pSummerMax = 0f, pWinterMin = 1e6f;
            int warmMonthsOver10 = 0;
            bool northHemi = latitudeDeg >= 0f;

            for (int m = 0; m < 12; m++)
            {
                float t = temperature[offset12 + m];
                float p = Math.Max(0f, precipitation[offset12 + m]);
                tAnn += t / 12f;
                pAnn += p;
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
                if (p < pMin) pMin = p;
                if (t >= 10f) warmMonthsOver10++;

                bool aprToSep = m >= 3 && m <= 8;
                bool isLocalSummer = northHemi ? aprToSep : !aprToSep;
                if (isLocalSummer)
                {
                    pSummer += p;
                    if (p < pSummerMin) pSummerMin = p;
                    if (p > pSummerMax) pSummerMax = p;
                }
                else
                {
                    pWinter += p;
                    if (p < pWinterMin) pWinterMin = p;
                    if (p > pWinterMax) pWinterMax = p;
                }
            }

            if (isOcean)
            {
                if (tMax < -1.2f) return PastaClimateKind.Opc;
                if (tMin < -1.6f) return PastaClimateKind.Osi;
                return PastaClimateKind.Ofd;
            }

            // Polar & Alpine (T_max < 10 °C)
            if (tMax < 0f) return PastaClimateKind.CI;
            if (tMax < 10f) return PastaClimateKind.CG;

            // Dryness threshold (Köppen-Pasta PET scaling)
            float summerShare = pAnn > 1e-3f ? pSummer / pAnn : 0.5f;
            float rOffset = summerShare >= 0.70f ? 280f : (summerShare <= 0.30f ? 0f : 140f);
            float aridThresh = 20f * Math.Max(0f, tAnn) + rOffset;
            if (pAnn < aridThresh)
                return tAnn >= 18f ? PastaClimateKind.Adc : PastaClimateKind.CEc;

            // Tropical (A: T_min >= 18 °C)
            if (tMin >= 18f)
            {
                if (pMin >= 60f || pAnn >= 25f * (100f - pMin) && pMin >= 42f)
                    return PastaClimateKind.Aha;
                return PastaClimateKind.Ada;
            }

            // Mild Temperate / Subtropical (tMin > -3 °C)
            if (tMin > -3f)
            {
                bool drySummer = pSummerMin < 38f && pWinterMax >= 3.0f * Math.Max(1f, pSummerMin);
                if (drySummer && warmMonthsOver10 >= 4)
                    return PastaClimateKind.CDb;
                if (tMax >= 22f)
                    return PastaClimateKind.CDa;
                return warmMonthsOver10 >= 4 ? PastaClimateKind.CEa : PastaClimateKind.CEb;
            }

            // Continental & Boreal (tMin <= -3 °C)
            bool monsoonWinterDry = pSummerMax >= 8.5f * Math.Max(1f, pWinterMin) && summerShare >= 0.65f;
            if (warmMonthsOver10 >= 4)
            {
                if (monsoonWinterDry)
                    return tMax >= 20f ? PastaClimateKind.CAMa : PastaClimateKind.CAMb;
                return tMax >= 22f ? PastaClimateKind.CAa : PastaClimateKind.CAb;
            }

            // Subarctic / Periglacial (1..3 months >= 10 °C)
            if (monsoonWinterDry) return PastaClimateKind.CAMb;
            return (warmMonthsOver10 == 1 && tMin < -22f) ? PastaClimateKind.CFb : PastaClimateKind.CFa;
        }

        public static string GetPastaLabel(PastaClimateKind k)
        {
            switch (k)
            {
                case PastaClimateKind.Ofd: return "Ofd · 常年无冰暖温海洋";
                case PastaClimateKind.Osi: return "Osi · 季节性浮冰海洋";
                case PastaClimateKind.Opc: return "Opc · 常年冰封极海";
                case PastaClimateKind.Aha: return "Aha · 赤道常湿热带雨林";
                case PastaClimateKind.Ada: return "Ada · 热带季风与干湿稀树草原";
                case PastaClimateKind.Adc: return "Adc · 炎热荒漠与干热草原";
                case PastaClimateKind.CDa: return "CDa · 亚热带湿润常绿阔叶林";
                case PastaClimateKind.CDb: return "CDb · 地中海夏干亚热带硬叶林";
                case PastaClimateKind.CEa: return "CEa · 温带海洋性湿润阔叶林";
                case PastaClimateKind.CEb: return "CEb · 副极地海洋性冷凉针阔林";
                case PastaClimateKind.CEc: return "CEc · 温带寒冷内陆干草原与荒漠";
                case PastaClimateKind.CAMa: return "CAMa · 暖温带季风落叶阔叶林";
                case PastaClimateKind.CAMb: return "CAMb · 寒温带季风针阔混交林";
                case PastaClimateKind.CAa: return "CAa · 炎夏湿润大陆性森林草原";
                case PastaClimateKind.CAb: return "CAb · 暖夏湿润大陆性混交林";
                case PastaClimateKind.CFa: return "CFa · 亚寒带北方泰加针叶林";
                case PastaClimateKind.CFb: return "CFb · 冰缘极寒稀疏林苔原";
                case PastaClimateKind.CG: return "CG · 极地与高山冻原苔原";
                case PastaClimateKind.CI: return "CI · 永久大陆冰盖";
                default: return k.ToString();
            }
        }

        public static uint GetPastaColorHex(PastaClimateKind k)
        {
            switch (k)
            {
                case PastaClimateKind.Ofd: return 0x1e4e6e;
                case PastaClimateKind.Osi: return 0x4c8ca8;
                case PastaClimateKind.Opc: return 0xb8dde6;
                case PastaClimateKind.Aha: return 0x0f5c3e;
                case PastaClimateKind.Ada: return 0x5ea34d;
                case PastaClimateKind.Adc: return 0xdba85c;
                case PastaClimateKind.CDa: return 0x4ba870;
                case PastaClimateKind.CDb: return 0xd6c44e;
                case PastaClimateKind.CEa: return 0x529c8e;
                case PastaClimateKind.CEb: return 0x76b3b0;
                case PastaClimateKind.CEc: return 0xb59c6b;
                case PastaClimateKind.CAMa: return 0x628c9c;
                case PastaClimateKind.CAMb: return 0x7e78b0;
                case PastaClimateKind.CAa: return 0x4b78a6;
                case PastaClimateKind.CAb: return 0x6e98bd;
                case PastaClimateKind.CFa: return 0x3b616e;
                case PastaClimateKind.CFb: return 0x7c969e;
                case PastaClimateKind.CG: return 0xaebdb8;
                case PastaClimateKind.CI: return 0xe8f0f2;
                default: return 0x808080;
            }
        }

        /// <summary>
        /// Terrain-downscaled tile diagnostic sampler (combines 128x64 GCM fields with fine-grid elevation/rain/temperature).
        /// </summary>
        public static ExoPlaSimTileDiagnostic SampleTileDiagnostic(WorldData world, int tile, int month)
        {
            var d = new ExoPlaSimTileDiagnostic();
            if (world == null || tile < 0 || tile >= world.Count) return d;
            month = FMath.Wrap(month, 12);
            bool isOcean = FlatTerrainAnalysis.Ocean(world.Water[tile]);
            float latDeg = world.Latitude(tile);
            d.PastaClimate = ClassifyPastaClimate(world.Temperature, world.Precipitation, tile * 12, isOcean, latDeg);

            var exo = world.Moisture?.ExoPlaSim;
            if (exo == null) return d;

            d.Available = true;
            float u = (tile % world.Width + 0.5f) / world.Width;
            float v = (tile / world.Width + 0.5f) / world.Height;

            float coarseConv = exo.SampleMonthly(exo.ConvectiveRain, u, v, month);
            float coarseStrat = exo.SampleMonthly(exo.StratiformRain, u, v, month);
            float coarseTot = Math.Max(0.01f, coarseConv + coarseStrat);
            float convShare = FMath.Clamp(coarseConv / coarseTot, 0f, 1f);
            float fineRain = world.Precipitation[tile * 12 + month];
            float fineTemp = world.Temperature[tile * 12 + month];

            // Terrain downscaling: preserve exact fine-grid orographic precipitation total, scaling convective/stratiform shares
            // and adjusting snowfall fraction to fine-tile elevation temperature!
            float solidFrac = SmoothStep(2.0f, -2.5f, fineTemp);
            d.ConvectiveRainMm = fineRain * convShare * (1f - 0.85f * solidFrac);
            d.StratiformRainMm = Math.Max(0f, fineRain - d.ConvectiveRainMm);
            d.SnowfallMm = fineRain * solidFrac;
            d.VirgaReevapMm = exo.SampleMonthly(exo.VirgaReevap, u, v, month);
            d.ShallowSoilMoisture = exo.SampleMonthly(exo.ShallowSoil, u, v, month);
            d.DeepRootMoisture = exo.SampleMonthly(exo.DeepSoil, u, v, month);
            d.MaritimeSeaBreeze = exo.SampleStatic(exo.MaritimeSeaBreezeGuard, u, v);
            d.SurfaceAlbedo = exo.SampleMonthly(exo.SurfaceAlbedo, u, v, month);
            d.NetShortwaveWm2 = exo.SampleMonthly(exo.NetShortwaveWm2, u, v, month);
            d.OutgoingLongwaveWm2 = exo.SampleMonthly(exo.OutgoingLongwaveWm2, u, v, month);
            d.SensibleHeatWm2 = exo.SampleMonthly(exo.SensibleHeatWm2, u, v, month);
            d.LatentHeatWm2 = exo.SampleMonthly(exo.LatentHeatWm2, u, v, month);
            d.DiurnalTempRangeC = exo.SampleMonthly(exo.DiurnalTempRangeC, u, v, month);
            d.CapeJkg = exo.SampleMonthly(exo.CapeJkg, u, v, month);
            d.MaxPotentialIntensityMs = exo.SampleMonthly(exo.MaxPotentialIntensityMs, u, v, month);
            d.EntropyDeficit = exo.SampleMonthly(exo.EntropyDeficit, u, v, month);
            d.VerticalWindShearMs = exo.SampleMonthly(exo.VerticalShearMs, u, v, month);
            d.GenesisPotentialIndex = exo.SampleMonthly(exo.GenesisPotential, u, v, month);
            return d;
        }

        public static ExoPlaSimMapmodeKind MapLayerToExoMode(MapLayer layer)
        {
            switch (layer)
            {
                case MapLayer.ExoConvectiveRain: return ExoPlaSimMapmodeKind.ConvectiveVsStratiform;
                case MapLayer.ExoRadiationOlr: return ExoPlaSimMapmodeKind.RadiationAndOlr;
                case MapLayer.ExoDiurnalRange: return ExoPlaSimMapmodeKind.DiurnalRangeAndBowen;
                case MapLayer.ExoStormPotential: return ExoPlaSimMapmodeKind.StormPotentialMpi;
                default: return ExoPlaSimMapmodeKind.PastaBioclimate;
            }
        }

        public static void EvaluateMapmodeColor(
            WorldData world, int tile, int month, ExoPlaSimMapmodeKind mode,
            out byte r, out byte g, out byte b)
        {
            r = g = b = 60;
            if (world == null || tile < 0 || tile >= world.Count) return;
            var d = SampleTileDiagnostic(world, tile, month);
            bool isWater = FlatTerrainAnalysis.AreaWater(world.Water[tile]);

            switch (mode)
            {
                case ExoPlaSimMapmodeKind.PastaBioclimate:
                {
                    uint hex = GetPastaColorHex(d.PastaClimate);
                    r = (byte)((hex >> 16) & 0xFF);
                    g = (byte)((hex >> 8) & 0xFF);
                    b = (byte)(hex & 0xFF);
                    return;
                }
                case ExoPlaSimMapmodeKind.ConvectiveVsStratiform:
                {
                    float dryBase = isWater ? 26f : 56f;
                    float convT = FMath.Clamp(d.ConvectiveRainMm / 220f, 0f, 1f);
                    float stratT = FMath.Clamp((d.StratiformRainMm - d.SnowfallMm) / 140f, 0f, 1f);
                    float snowT = FMath.Clamp(d.SnowfallMm / 90f, 0f, 1f);
                    // Deep convective storms = warm magenta/coral; frontal/orographic stratiform = cyan/cobalt; snowfall = ice-white
                    float cr = dryBase + 205f * convT + 30f * stratT + 185f * snowT;
                    float cg = dryBase * 0.92f + 52f * convT + 158f * stratT + 195f * snowT;
                    float cb = dryBase * 0.85f + 132f * convT + 208f * stratT + 215f * snowT;
                    r = (byte)FMath.Clamp(cr, 0f, 255f);
                    g = (byte)FMath.Clamp(cg, 0f, 255f);
                    b = (byte)FMath.Clamp(cb, 0f, 255f);
                    return;
                }
                case ExoPlaSimMapmodeKind.RadiationAndOlr:
                {
                    // OLR: 130 W/m2 (cold deep-anvil greenhouse trap -> deep violet/cyan) to 305 W/m2 (hot desert window -> bright amber/white)
                    float tOlr = FMath.Clamp((d.OutgoingLongwaveWm2 - 135f) / 165f, 0f, 1f);
                    GlebaGeographySupplement.SampleCividis(tOlr, out r, out g, out b);
                    if (d.SurfaceAlbedo > 0.55f)
                    {
                        float iceTint = FMath.Clamp((d.SurfaceAlbedo - 0.55f) / 0.30f, 0f, 0.45f);
                        r = (byte)FMath.Lerp(r, 225f, iceTint);
                        g = (byte)FMath.Lerp(g, 242f, iceTint);
                        b = (byte)FMath.Lerp(b, 255f, iceTint);
                    }
                    return;
                }
                case ExoPlaSimMapmodeKind.DiurnalRangeAndBowen:
                {
                    // DTR: 1.5 C (ocean isothermal deep navy) -> 10 C (humid forest teal/green) -> 21 C (hyper-arid desert incandescent orange-gold)
                    float tDtr = FMath.Clamp((d.DiurnalTempRangeC - 1.5f) / 19.5f, 0f, 1f);
                    if (tDtr < 0.45f)
                    {
                        float k = tDtr / 0.45f;
                        r = (byte)FMath.Lerp(24f, 62f, k);
                        g = (byte)FMath.Lerp(56f, 158f, k);
                        b = (byte)FMath.Lerp(108f, 146f, k);
                    }
                    else
                    {
                        float k = (tDtr - 0.45f) / 0.55f;
                        r = (byte)FMath.Lerp(62f, 246f, k);
                        g = (byte)FMath.Lerp(158f, 168f, k);
                        b = (byte)FMath.Lerp(146f, 58f, k);
                    }
                    return;
                }
                default: // StormPotentialMpi
                {
                    // Marine MPI + Genesis GPI (violet -> crimson -> bright gold core) + land CAPE (emerald/amber)
                    if (FlatTerrainAnalysis.Ocean(world.Water[tile]))
                    {
                        float mpiT = FMath.Clamp((d.MaxPotentialIntensityMs - 22f) / 62f, 0f, 1f);
                        float gpiT = FMath.Clamp(d.GenesisPotentialIndex * 1.25f, 0f, 1f);
                        float cr = 22f + 185f * mpiT + 48f * gpiT;
                        float cg = 36f + 42f * mpiT + 156f * gpiT * gpiT;
                        float cb = 74f + 96f * mpiT * (1f - 0.65f * gpiT);
                        r = (byte)FMath.Clamp(cr, 0f, 255f);
                        g = (byte)FMath.Clamp(cg, 0f, 255f);
                        b = (byte)FMath.Clamp(cb, 0f, 255f);
                    }
                    else
                    {
                        float capeT = FMath.Clamp(d.CapeJkg / 2600f, 0f, 1f);
                        r = (byte)FMath.Lerp(48f, 214f, capeT);
                        g = (byte)FMath.Lerp(54f, 148f, capeT);
                        b = (byte)FMath.Lerp(52f, 68f, capeT);
                    }
                    return;
                }
            }
        }
    }
}
