using System;

namespace Orbis.Flat
{
    // Bounded adjustments to the existing cloud parameterization, not rainfall rates.
    // Cover in [-1, 1] (R = +Cover, G = -Cover), Convection in [0, 1] (B),
    // Evaporation in [0, 1] (A: 0 = no extra dissipation, >0 = subsidence warming / dry-intrusion / rain-out cloud evaporation).
    public readonly struct RegionalCloudResponse
    {
        public readonly float Cover, Convection, Evaporation;

        RegionalCloudResponse(double cover, double convection, double evaporation = 0.0)
        {
            Cover = (float)cover;
            Convection = (float)convection;
            Evaporation = (float)evaporation;
        }

        public static RegionalCloudResponse From(in ClimateTileDiagnostics cell)
        {
            var reference = cell.WeatherReference;
            var score = cell.Weather;
            if (!score.Valid || reference == null || reference.ActiveDimensions < 1) return default;
            ref readonly var air = ref cell.Observation;
            double rh = air.Humidity;
            if (!(rh >= 0 && rh <= 1)) return default;

            // The joint distance sets the strength. Its sign comes from the local
            // moisture departures; heat or wind alone has no prescribed cloud sign.
            double weight = score.Reliability * Math.Min(1, score.Distance / (2 * Math.Sqrt(reference.ActiveDimensions)));
            double vapor = reference.Standardized(ClimateFeature.Humidity, air);
            double rain = reference.Standardized(ClimateFeature.Precipitation, air);
            double moisture = Math.Min(1, Math.Max(-1, (vapor + rain) * .25));
            double cover = weight * moisture * (moisture > 0 ? rh : 1 - rh);
            double low = Math.Min(1, Math.Max(0, -.5 * reference.Standardized(ClimateFeature.Pressure, air)));
            double convection = Math.Max(0, cover) * low;
            double evap = cover < 0 ? Math.Min(1.0, -cover * (1.0 - 0.5 * rh)) : 0.0;
            return new RegionalCloudResponse(cover, convection, evap);
        }

        /// <summary>
        /// 6-hourly regional cloud response driven by a pre-solved <see cref="FlatMoistureCloudBudget"/>
        /// (mass-conserving low/upper wind advection, Clausius-Clapeyron saturation condensation, precipitation fallout,
        /// and adiabatic subsidence warming evaporation) plus cloned 6h cyclone state. Zero allocations.
        /// </summary>
        public static RegionalCloudResponse FromSynoptic(
            in ClimateTileDiagnostics cell, int budgetCellIndex, FlatMoistureCloudBudget budget,
            FlatCycloneWeather storms, double day)
        {
            var baseResp = From(in cell);
            ref readonly var air = ref cell.Observation;
            double rh = air.Humidity;
            if (!(rh >= 0 && rh <= 1) || budget == null || (uint)budgetCellIndex >= FlatMoistureCloudBudget.Count)
                return baseResp;

            float cloudNorm = budget.CloudWaterNorm[budgetCellIndex];
            float vapAnom = budget.VaporAnomaly[budgetCellIndex];
            float budEvap = budget.Evaporation[budgetCellIndex];
            float wMotion = budget.VerticalMotion[budgetCellIndex];
            float oceanFrac = budget.OceanMask[budgetCellIndex];

            double u = (budgetCellIndex % FlatMoistureCloudBudget.Width + 0.5) / FlatMoistureCloudBudget.Width;
            double v = (budgetCellIndex / FlatMoistureCloudBudget.Width + 0.5) / FlatMoistureCloudBudget.Height;
            float cycCore = 0f, cycMoat = 0f, cycWakeCoolC = 0f, cycWakeStab = 0f;
            if (storms != null)
                storms.SampleCloudFootprint(u, v, day, out cycCore, out cycMoat, out cycWakeCoolC, out cycWakeStab);

            double wakeSuppress = (0.30 * cycWakeStab + 0.16 * Math.Min(1.0f, cycWakeCoolC / 2.5f)) * oceanFrac;
            double growth = (0.52 * cloudNorm + 0.24 * Math.Max(0f, vapAnom) + 0.72 * cycCore) * rh;
            double decay = (0.58 * budEvap + 0.28 * Math.Max(0f, -vapAnom) + 0.48 * cycMoat + wakeSuppress) * (1.0 - 0.30 * rh);
            double cover = Math.Max(-1.0, Math.Min(1.0, 0.55 * baseResp.Cover + growth - decay));

            double low = cell.WeatherReference != null && cell.Weather.Valid
                ? Math.Min(1.0, Math.Max(0.0, -0.5 * cell.WeatherReference.Standardized(ClimateFeature.Pressure, air)))
                : 0.0;
            double updraft = Math.Max(low, 0.65 * Math.Max(0f, wMotion) * cloudNorm + 0.88 * cycCore);
            double convection = rh > 0.02
                ? Math.Max(0.0, Math.Min(1.0, Math.Max(baseResp.Convection * (1.0 - 0.75 * budEvap), Math.Max(0.0, cover + 0.25 * cloudNorm) * updraft)))
                : 0.0;

            double evapOut = Math.Max(0.0, Math.Min(1.0,
                (budEvap + 0.45 * cycMoat + 0.35 * wakeSuppress) * (1.0 - 0.85 * cycCore)));
            return new RegionalCloudResponse(cover, convection, evapOut);
        }

        /// <summary>
        /// Convenience overload evaluating a single cell (allocates a temporary solver only if caller has none;
        /// production hot paths should reuse a <see cref="FlatMoistureCloudBudget"/> instance).
        /// </summary>
        public static RegionalCloudResponse FromSynoptic(
            in ClimateTileDiagnostics cell, WorldData world, double day, FlatCycloneWeather storms, FlatMoistureCloudBudget reusableBudget = null)
        {
            if (world == null || cell.Tile < 0 || cell.Tile >= world.Count) return From(in cell);
            var b = reusableBudget ?? new FlatMoistureCloudBudget();
            b.Solve(world, day, storms);
            int tx = cell.Tile % world.Width, ty = cell.Tile / world.Width;
            int bx = FMath.Wrap((int)((tx + 0.5) / world.Width * FlatMoistureCloudBudget.Width), FlatMoistureCloudBudget.Width);
            int by = FMath.Clamp((int)((ty + 0.5) / world.Height * FlatMoistureCloudBudget.Height), 0, FlatMoistureCloudBudget.Height - 1);
            return FromSynoptic(in cell, by * FlatMoistureCloudBudget.Width + bx, b, storms, day);
        }
    }

    /// <summary>
    /// Stateless, mass-conserving 2-layer (low/mid water vapor &amp; liquid cloud + upper ice/anvil cloud) synoptic
    /// moisture-saturation-condensation-fallout-evaporation solver on the 64x32 global spherical grid.
    /// Integrates deterministically over the trailing 5-day (20 x 6h) residence window using
    /// <see cref="OrbisAtmosphereCore.Saturation"/> so calendar day, pause, 1x/20x speed, rewind, and V3-V6 saves
    /// are 100% bit-exact with zero unserialized persistent state and zero warm heap allocations.
    /// </summary>
    public sealed class FlatMoistureCloudBudget
    {
        public const int Width = 64, Height = 32, Count = Width * Height;
        const int SpinSteps = 10;
        const double StepDays = 0.25;
        const double SecondsPerStep = StepDays * 86400.0;
        const double EarthRadiusM = 6371000.0;
        const float AirColumnMassKgM2 = 3600f;
        const double MidLayerPressurePa = 82000.0;

        static readonly float[] PosX = new float[Count], PosY = new float[Count], PosZ = new float[Count];
        static readonly float[] EastX = new float[Count], EastZ = new float[Count];
        static readonly float[] NorthX = new float[Count], NorthY = new float[Count], NorthZ = new float[Count];
        static readonly float[] LatRad = new float[Count], LonRad = new float[Count], CosLat = new float[Count];
        static readonly double[] CellArea = new double[Count];
        static readonly int[] WestIdx = new int[Count], EastIdx = new int[Count], SouthIdx = new int[Count], NorthIdx = new int[Count];
        static readonly double TotalSphereArea;

        static FlatMoistureCloudBudget()
        {
            double areaSum = 0.0;
            for (int y = 0; y < Height; y++)
            {
                double v = (y + 0.5) / Height;
                double lat = (v - 0.5) * Math.PI;
                double latS = ((double)y / Height - 0.5) * Math.PI;
                double latN = ((double)(y + 1) / Height - 0.5) * Math.PI;
                double dArea = (2.0 * Math.PI / Width) * (Math.Sin(latN) - Math.Sin(latS));
                double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
                int yS = Math.Max(0, y - 1), yN = Math.Min(Height - 1, y + 1);
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    double u = (x + 0.5) / Width;
                    double lon = (u - 0.5) * 2.0 * Math.PI;
                    double sinLon = Math.Sin(lon), cosLon = Math.Cos(lon);
                    PosX[i] = (float)(sinLon * cosLat);
                    PosY[i] = (float)sinLat;
                    PosZ[i] = (float)(cosLon * cosLat);
                    EastX[i] = (float)cosLon;
                    EastZ[i] = (float)(-sinLon);
                    NorthX[i] = (float)(-sinLat * sinLon);
                    NorthY[i] = (float)cosLat;
                    NorthZ[i] = (float)(-sinLat * cosLon);
                    LatRad[i] = (float)lat;
                    LonRad[i] = (float)lon;
                    CosLat[i] = (float)Math.Max(0.14, cosLat);
                    CellArea[i] = dArea;
                    areaSum += dArea;
                    WestIdx[i] = y * Width + (x == 0 ? Width - 1 : x - 1);
                    EastIdx[i] = y * Width + (x + 1 == Width ? 0 : x + 1);
                    SouthIdx[i] = yS * Width + x;
                    NorthIdx[i] = yN * Width + x;
                }
            }
            TotalSphereArea = areaSum;
        }

        readonly float[] qv = new float[Count], qcLow = new float[Count], qcHigh = new float[Count];
        readonly float[] qvTmp = new float[Count], qcLowTmp = new float[Count], qcHighTmp = new float[Count];
        readonly float[] sfcTempK = new float[Count], slpTempC = new float[Count], rhClim = new float[Count], rainClim = new float[Count];
        readonly float[] slpClim = new float[Count], zonalSlp = new float[Height], baseWindE = new float[Count], baseWindN = new float[Count];
        readonly float[] soilAvail = new float[Count], elevM = new float[Count], scShield = new float[Count], qSatNeutralArr = new float[Count], qvClimEquil = new float[Count];
        readonly float[] eddyNx = new float[Count], eddyNy = new float[Count];
        readonly float[] npLatCol = new float[Width], npSlopeCol = new float[Width], spLatCol = new float[Width], spSlopeCol = new float[Width];
        readonly float[] nsLatCol = new float[Width], nsSlopeCol = new float[Width], ssLatCol = new float[Width], ssSlopeCol = new float[Width];
        readonly float[] ridgeCol = new float[Width], mjoCol = new float[Width];
        readonly float[] slpSyn = new float[Count], uEddyMs = new float[Count], vEddyMs = new float[Count], uLowMs = new float[Count], vLowMs = new float[Count], uUpMs = new float[Count], vUpMs = new float[Count];

        public readonly float[] CloudWaterNorm = new float[Count];
        public readonly float[] EddyCondensate = new float[Count];
        public readonly float[] HighCloudNorm = new float[Count];
        public readonly float[] VaporAnomaly = new float[Count];
        public readonly float[] Evaporation = new float[Count];
        public readonly float[] TransientDrying = new float[Count];
        public readonly float[] VerticalMotion = new float[Count];
        public readonly float[] OceanMask = new float[Count];

        public double WaterBudgetErrorKgM2 { get; private set; }
        public double InitialWaterKgM2 { get; private set; }
        public double FinalWaterKgM2 { get; private set; }
        public double CumulativeEvapKgM2 { get; private set; }
        public double CumulativePrecipKgM2 { get; private set; }

        WorldData cachedWorld;
        int cachedRevision = -1;
        ulong cachedClimateFp;
        long cachedTick = long.MinValue;
        bool cachedHadStorms;
        FlatCycloneTrack cachedN, cachedS, cachedPN, cachedPS;
        GlebaRossbyWaveField cachedRossby;
        uint cachedRossbySeed;

        public void Solve(WorldData world, double calendarDay, FlatCycloneWeather storms = null)
        {
            if (world == null) return;
            long targetTick = FlatCycloneWeather.StepAt(calendarDay);
            ulong climFp = FlatSkyForcingCache.ComputeClimateFingerprint(world);
            bool hasStorms = storms != null;
            if (ReferenceEquals(cachedWorld, world) &&
                cachedRevision == world.TerrainRevision &&
                cachedClimateFp == climFp &&
                cachedTick == targetTick &&
                cachedHadStorms == hasStorms &&
                (!hasStorms || (FlatSkyForcingCache.SameCycloneTrack(storms.North, cachedN) &&
                                FlatSkyForcingCache.SameCycloneTrack(storms.South, cachedS) &&
                                FlatSkyForcingCache.SameCycloneTrack(storms.PreviousNorth, cachedPN) &&
                                FlatSkyForcingCache.SameCycloneTrack(storms.PreviousSouth, cachedPS))))
            {
                return;
            }

            double endDay = targetTick * StepDays;
            double midDay = endDay - 0.5 * SpinSteps * StepDays;
            var season = new FlatMonthlyForcing(midDay);
            uint seed = FMath.Seed(world.Seed);
            var year = FlatClimateYear.At(seed, midDay);
            var moisture = world.Moisture;
            var gleba = moisture?.Gleba;
            var exo = moisture?.ExoPlaSim;
            var oceanCur = moisture?.OceanCurrents;
            var cfg = exo?.Config ?? world.PlanetClimate;
            bool customPlanet = cfg != null && !cfg.IsStandardModernEarth;
            float subJetDeg = customPlanet ? cfg.HadleyTerminusDeg : 30f;
            float polJetDeg = customPlanet ? cfg.PolarFrontTerminusDeg : 65f;
            bool tidallyLocked = customPlanet && cfg.TidallyLocked;

            int m0 = FMath.Wrap((int)Math.Floor(season.SeasonalMonth), 12), m1 = (m0 + 1) % 12;
            float mFrac = (float)(season.SeasonalMonth - Math.Floor(season.SeasonalMonth));

            // 1. Sample climate boundary fields on 64x32 and initialize column water vapor in hydrostatic equilibrium.
            double initWaterSum = 0.0;
            for (int y = 0; y < Height; y++)
            {
                double slpSum = 0.0;
                float v = (y + 0.5f) / Height;
                float absLatDeg = Math.Abs(v - 0.5f) * 180f;
                int wy = FMath.Clamp((int)(v * world.Height), 0, world.Height - 1);
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    float u = (x + 0.5f) / Width;
                    int wx = FMath.Wrap((int)(u * world.Width), world.Width);
                    int tile = wy * world.Width + wx;
                    int o12 = tile * 12;

                    year.Offsets(PosX[i], PosY[i], PosZ[i], out float dTemp, out float dRain);
                    float tC = season.Sample(world.Temperature, o12) + dTemp;
                    float rMm = Math.Max(0f, season.Sample(world.Precipitation, o12, ClimateRange.Nonnegative) * (1f + dRain));
                    float el = Math.Max(0f, (float)world.Elevation[tile]);
                    float isSea = FlatTerrainAnalysis.Ocean(world.Water[tile]) ? 1f : 0f;
                    if (gleba != null) isSea = gleba.SampleStatic(gleba.OceanFraction, u, v);

                    float rh = moisture != null
                        ? season.Sample(moisture, moisture.Humidity, u, v, ClimateRange.Fraction)
                        : FMath.Clamp((isSea > 0.5f ? 0.75f : 0.30f) + rMm / 240f, 0f, 1f);
                    rh = FMath.Clamp(rh * (1f + 0.25f * dRain), 0f, 1f);

                    float we = moisture != null ? season.Sample(moisture, moisture.East, u, v) : 0f;
                    float wn = moisture != null ? season.Sample(moisture, moisture.North, u, v) : 0f;
                    float slp = gleba != null
                        ? FMath.Lerp(gleba.SampleMonthly(gleba.PressureHpa, u, v, m0), gleba.SampleMonthly(gleba.PressureHpa, u, v, m1), mFrac)
                        : 1013f;

                    float sstAnom = 0f;
                    if (oceanCur?.SeaAnomaly != null && isSea > 0.1f)
                    {
                        int ox = FMath.Wrap((int)(u * oceanCur.Width), oceanCur.Width);
                        int oy = FMath.Clamp((int)(v * oceanCur.Height), 0, oceanCur.Height - 1);
                        sstAnom = oceanCur.SeaAnomaly[oy * oceanCur.Width + ox];
                    }
                    float deepSoil = exo != null ? FMath.Lerp(exo.SampleMonthly(exo.DeepSoil, u, v, m0), exo.SampleMonthly(exo.DeepSoil, u, v, m1), mFrac) : 0.35f;
                    float shallowSoil = exo != null ? FMath.Lerp(exo.SampleMonthly(exo.ShallowSoil, u, v, m0), exo.SampleMonthly(exo.ShallowSoil, u, v, m1), mFrac) : 0.35f;

                    sfcTempK[i] = FMath.Clamp(tC + 273.15f, 200f, 325f);
                    slpTempC[i] = tC + 0.0065f * el;
                    rhClim[i] = rh;
                    rainClim[i] = rMm;
                    slpClim[i] = slp;
                    slpSum += slp;
                    baseWindE[i] = we * FlatCycloneWeather.ClimateWindMetresPerSecond;
                    baseWindN[i] = wn * FlatCycloneWeather.ClimateWindMetresPerSecond;
                    elevM[i] = el;
                    OceanMask[i] = isSea;
                    soilAvail[i] = isSea > 0.5f
                        ? FMath.Clamp(1f + 0.14f * FMath.Clamp(sstAnom, -1.5f, 2.5f), 0.75f, 1.35f)
                        : FMath.Clamp(0.10f + 0.55f * (0.45f * shallowSoil + 0.55f * deepSoil) + 0.45f * FMath.Clamp(rMm / 160f, 0f, 1f), 0.02f, 0.95f) * FMath.Clamp(rh / 0.22f, 0f, 1f);

                    // Cold eastern-boundary marine stratocumulus inversion shield
                    float scLat = FMath.Clamp((absLatDeg - 7f) / 8f, 0f, 1f) * (1f - FMath.Clamp((absLatDeg - 33f) / 10f, 0f, 1f));
                    float coolSst = FMath.Clamp((0.25f - sstAnom) / 1.9f, 0f, 1f);
                    float highLid = FMath.Clamp((slp - 1012f) / 12f, 0f, 1f);
                    scShield[i] = isSea * scLat * coolSst * (0.35f + 0.65f * highLid) * (1f - FMath.Clamp((rMm - 95f) / 90f, 0f, 1f));

                    double colTempK = sfcTempK[i] - 8.5;
                    float qSat0 = AirColumnMassKgM2 * (float)OrbisAtmosphereCore.Saturation(colTempK, MidLayerPressurePa);
                    qSatNeutralArr[i] = qSat0;
                    // Map column-mean Humidity + rainfall moisture supply onto boundary-layer LCL saturation fraction:
                    float blSatFrac = rh <= 0.02f
                        ? 0f
                        : FMath.Clamp(0.76f * FMath.Clamp(rh / 0.58f, 0f, 1.18f) + 0.18f * FMath.Clamp(rMm / 180f, 0f, 1f), 0.02f, 0.96f);
                    qvClimEquil[i] = qSat0 * blSatFrac;
                    qv[i] = qvClimEquil[i];
                    qcLow[i] = 0f;
                    qcHigh[i] = 0f;
                    initWaterSum += qv[i] * CellArea[i];
                }
                zonalSlp[y] = (float)(slpSum / Width);
            }

            InitialWaterKgM2 = initWaterSum / TotalSphereArea;
            double totalEvapArea = 0.0, totalPrecipArea = 0.0;
            const double invCellLonDistM = Width / (2.0 * Math.PI * EarthRadiusM);
            const double invCellLatDistM = Height / (Math.PI * EarthRadiusM);

            // Sample slowly-evolving 3D Rossby eddy perturbation on 32x16 and bilinearly expand to 64x32 (4x faster, stateless & exact)
            float subRad = FMath.Clamp(subJetDeg, 15f, 72f) * ((float)Math.PI / 180f);
            float polRad = FMath.Clamp(polJetDeg, 35f, 82f) * ((float)Math.PI / 180f);
            if (!tidallyLocked)
            {
                if (cachedRossby == null || cachedRossbySeed != seed)
                {
                    cachedRossby = new GlebaRossbyWaveField(seed);
                    cachedRossbySeed = seed;
                }
                cachedRossby.RossbyTimeRadians = midDay * 0.085;
                var bgRossby = cachedRossby;
                for (int y2 = 0; y2 < Height; y2 += 2)
                {
                    int r0 = y2 * Width;
                    for (int x2 = 0; x2 < Width; x2 += 2)
                    {
                        int c = r0 + x2;
                        bgRossby.GetBaseRossbyWinds(LatRad[c], LonRad[c], out float nx, out float ny);
                        eddyNx[c] = nx;
                        eddyNy[c] = ny;
                    }
                    for (int x2 = 0; x2 < Width; x2 += 2)
                    {
                        int xNext = (x2 + 2) & (Width - 1);
                        eddyNx[r0 + x2 + 1] = 0.5f * (eddyNx[r0 + x2] + eddyNx[r0 + xNext]);
                        eddyNy[r0 + x2 + 1] = 0.5f * (eddyNy[r0 + x2] + eddyNy[r0 + xNext]);
                    }
                }
                for (int y2 = 0; y2 < Height; y2 += 2)
                {
                    int r0 = y2 * Width, r1 = (y2 + 1) * Width, r2 = Math.Min(y2 + 2, Height - 2) * Width;
                    for (int x = 0; x < Width; x++)
                    {
                        eddyNx[r1 + x] = 0.5f * (eddyNx[r0 + x] + eddyNx[r2 + x]);
                        eddyNy[r1 + x] = 0.5f * (eddyNy[r0 + x] + eddyNy[r2 + x]);
                    }
                }
            }
            double phRidgeA = Phase(seed, 0x61c88647u), phRidgeB = Phase(seed, 0x9e3779b9u);
            double phEwA = Phase(seed, 0x33a19bc7u), phEwB = Phase(seed, 0x51f07e4du);
            double phMjoA = Phase(seed, 0x41a7d311u), phMjoB = Phase(seed, 0x7b29c503u);

            // 2. Integrate SpinSteps six-hour steps of coupled transport, surface exchange, saturation condensation & rain-out.
            for (int step = 0; step < SpinSteps; step++)
            {
                double stepDay = (targetTick - SpinSteps + 1 + step) * StepDays;
                bool isFinalStep = step == SpinSteps - 1;

                // 2a. Precompute 1D longitude-dependent Rossby jet meanders, WPSH/subtropical ridge waves, and MJO once per column (64 vs 2048!).
                if (!tidallyLocked)
                {
                    double synTimeRad = -stepDay * 0.120427718;
                    double subTimeRad = synTimeRad * 0.82;
                    for (int x = 0; x < Width; x++)
                    {
                        double lon = LonRad[x];
                        EvalJetColumn(GlebaRossbyWaveField.DefaultNorthPolarWaves, polRad, lon, synTimeRad, out npLatCol[x], out npSlopeCol[x]);
                        EvalJetColumn(GlebaRossbyWaveField.DefaultSouthPolarWaves, -polRad, lon, synTimeRad, out spLatCol[x], out spSlopeCol[x]);
                        EvalJetColumn(GlebaRossbyWaveField.DefaultNorthSubtropicalWaves, subRad, lon, subTimeRad, out nsLatCol[x], out nsSlopeCol[x]);
                        EvalJetColumn(GlebaRossbyWaveField.DefaultSouthSubtropicalWaves, -subRad, lon, subTimeRad, out ssLatCol[x], out ssSlopeCol[x]);
                        ridgeCol[x] = (float)(0.60 * Math.Cos(2.0 * lon - stepDay * (2.0 * Math.PI / 11.2) + phRidgeA)
                                            + 0.40 * Math.Cos(3.0 * lon + stepDay * (2.0 * Math.PI / 7.8) + phRidgeB));
                        mjoCol[x] = (float)(0.62 * Math.Cos(lon - stepDay * (2.0 * Math.PI / 42.0) + phMjoA)
                                          + 0.38 * Math.Cos(2.0 * lon - stepDay * (2.0 * Math.PI / 29.0) + phMjoB));
                    }
                }

                // Diagnose synoptic pressure anomalies & upper-level jet flow (in physical m/s).
                for (int y = 0; y < Height; y++)
                {
                    float lat = LatRad[y * Width];
                    float absLat = Math.Abs(lat);
                    float absLatDeg = absLat * (180f / (float)Math.PI);
                    int hemi = lat >= 0f ? 1 : -1;
                    float midGate = FMath.Clamp((absLatDeg - 18f) / 11f, 0f, 1f) * (1f - FMath.Clamp((absLatDeg - 74f) / 12f, 0f, 1f));
                    float subRidgeBelt = (float)Math.Exp(-Math.Pow((absLatDeg - subJetDeg * 0.92f) / 9.5, 2.0));
                    float tropGate = 1f - FMath.Clamp((absLatDeg - 26f) / 10f, 0f, 1f);
                    int rowOff = y * Width;

                    for (int x = 0; x < Width; x++)
                    {
                        int i = rowOff + x;
                        if (tidallyLocked)
                        {
                            slpSyn[i] = 0f;
                            uUpMs[i] = baseWindE[i] * 1.4f;
                            vUpMs[i] = baseWindN[i] * 0.5f;
                            continue;
                        }

                        float pLat = hemi > 0 ? npLatCol[x] : spLatCol[x];
                        float pSlope = hemi > 0 ? npSlopeCol[x] : spSlopeCol[x];
                        float sLat = hemi > 0 ? nsLatCol[x] : ssLatCol[x];
                        float sSlope = hemi > 0 ? nsSlopeCol[x] : ssSlopeCol[x];
                        float dPol = (absLat - Math.Abs(pLat)) * (1f / 0.24f);
                        float dSub = (absLat - Math.Abs(sLat)) * (1f / 0.22f);
                        float dPol2 = dPol * dPol, dSub2 = dSub * dSub;
                        float wPol = dPol2 < 9f ? 1f / (1f + dPol2 + 0.48f * dPol2 * dPol2 + 0.15f * dPol2 * dPol2 * dPol2) : 0f;
                        float wSub = dSub2 < 9f ? 1f / (1f + dSub2 + 0.48f * dSub2 * dSub2 + 0.15f * dSub2 * dSub2 * dSub2) : 0f;

                        float pDisp = -hemi * (pLat - (hemi > 0 ? polRad : -polRad));
                        float sDisp = -hemi * (sLat - (hemi > 0 ? subRad : -subRad));
                        float normDisp = (wPol * pDisp + 0.85f * wSub * sDisp) * (1f / 0.16f);
                        float rawAhead = hemi * (wPol * pSlope + 0.85f * wSub * sSlope) * (1f / 0.55f);
                        float troughDepth = FMath.Clamp(normDisp + 0.28f * eddyNx[i], -1.25f, 1.25f);
                        float aheadOfTrough = FMath.Clamp(rawAhead + 0.25f * eddyNy[i], -1.25f, 1.25f);

                        float uJetMs = (26f * wPol + 16f * wSub) + 4.5f * eddyNx[i] - 3.2f * Math.Max(0f, -troughDepth) * wSub;
                        float vJetMs = hemi * aheadOfTrough * (9.5f * wPol + 6.8f * wSub) + 3.8f * eddyNy[i];

                        float ew = 0f;
                        if (tropGate > 0.001f)
                        {
                            double lon = LonRad[x];
                            double cEw = 0.110 + 0.014 * FMath.Clamp(-baseWindE[i] / 9f, -0.4f, 1.2f);
                            ew = (float)(0.58 * Math.Cos(7.0 * (lon + stepDay * cEw) + phEwA)
                                       + 0.42 * Math.Cos(9.0 * (lon + stepDay * cEw * 1.09) + phEwB));
                        }

                        // Quasi-geostrophic omega forcing: pre-trough PVA (aheadOfTrough > 0) drives cyclonic ascent;
                        // post-trough NVA (aheadOfTrough < 0) + WPSH subtropical ridge extension drives subsidence & dry-slot clearing.
                        float pAnom = -8.5f * midGate * (0.50f * troughDepth + 0.50f * aheadOfTrough)
                                    + 6.8f * subRidgeBelt * ridgeCol[x]
                                    - 3.6f * tropGate * (0.58f * ew + 0.42f * mjoCol[x]);
                        slpSyn[i] = FMath.Clamp(pAnom, -16f, 16f);

                        float itczProxy = tropGate * FMath.Clamp((rainClim[i] - 90f) / 150f, 0f, 1f);
                        uUpMs[i] = uJetMs - 4.95f * itczProxy;
                        vUpMs[i] = vJetMs + 0.12f * baseWindN[i] * itczProxy;
                    }
                }

                // 2b. Compute total low-level wind = prevailing climate wind + synoptic geostrophic/cross-isobaric eddy wind.
                //     Behind passing troughs, northerly/NW eddy winds physically advect dry continental air into coastal seas!
                for (int i = 0; i < Count; i++)
                {
                    int iW = WestIdx[i], iE = EastIdx[i], iS = SouthIdx[i], iN = NorthIdx[i];
                    float dPdx = (slpSyn[iE] - slpSyn[iW]) * 0.5f / CosLat[i]; // hPa per lon-cell
                    float dPdy = (slpSyn[iN] - slpSyn[iS]) * 0.5f;             // hPa per lat-cell
                    float hemi = PosY[i] >= 0f ? 1f : -1f;
                    float coriolisScale = 0.35f + 0.65f * Math.Min(1f, Math.Abs(PosY[i]) * 2.5f);
                    // Geostrophic tangent + 28% cross-isobaric boundary-layer friction inflow/outflow
                    float ue = FMath.Clamp((-hemi * dPdy * 1.65f - 0.52f * dPdx) / coriolisScale, -11f, 11f);
                    float ve = FMath.Clamp((hemi * dPdx * 1.65f - 0.52f * dPdy) / coriolisScale, -11f, 11f);
                    uEddyMs[i] = ue;
                    vEddyMs[i] = ve;
                    uLowMs[i] = FMath.Clamp(baseWindE[i] + ue, -28f, 28f);
                    vLowMs[i] = FMath.Clamp(baseWindN[i] + ve, -28f, 28f);
                }

                // 2c. Mass-conserving spherical semi-Lagrangian horizontal advection of (Qv, QcLow) by low wind and QcHigh by upper wind.
                AdvectPairConserving(qv, qcLow, qvTmp, qcLowTmp, uLowMs, vLowMs);
                AdvectSingleConserving(qcHigh, qcHighTmp, uUpMs, vUpMs);
                Array.Copy(qvTmp, qv, Count);
                Array.Copy(qcLowTmp, qcLow, Count);
                Array.Copy(qcHighTmp, qcHigh, Count);

                // 2d. Column dynamics, bulk surface evaporation, Clausius-Clapeyron saturation adjustment & precipitation fallout.
                for (int i = 0; i < Count; i++)
                {
                    int y = i / Width;
                    int iW = WestIdx[i], iE = EastIdx[i], iS = SouthIdx[i], iN = NorthIdx[i];

                    // Separate synoptic eddy convergence (-div V_eddy) from total convergence (-div V_tot):
                    double duTot = (uLowMs[iE] - uLowMs[iW]) * 0.5 * invCellLonDistM / CosLat[i];
                    double dvTot = (vLowMs[iN] * CosLat[iN] - vLowMs[iS] * CosLat[iS]) * 0.5 * invCellLatDistM / CosLat[i];
                    float convTot = FMath.Clamp((float)(-(duTot + dvTot) * 7.5e4), -1.0f, 1.0f);

                    double duEd = (uEddyMs[iE] - uEddyMs[iW]) * 0.5 * invCellLonDistM / CosLat[i];
                    double dvEd = (vEddyMs[iN] * CosLat[iN] - vEddyMs[iS] * CosLat[iS]) * 0.5 * invCellLatDistM / CosLat[i];
                    float convEddy = FMath.Clamp((float)(-(duEd + dvEd) * 8.5e4), -1.0f, 1.0f);

                    double dTdx = (slpTempC[iE] - slpTempC[iW]) * 0.5 * invCellLonDistM / CosLat[i];
                    double dTdy = (slpTempC[iN] - slpTempC[iS]) * 0.5 * invCellLatDistM;
                    float warmAdv = FMath.Clamp((float)(-(uLowMs[i] * dTdx + vLowMs[i] * dTdy) * 3.6e4), -0.85f, 0.85f);
                    float eddyWarmAdv = FMath.Clamp((float)(-(uEddyMs[i] * dTdx + vEddyMs[i] * dTdy) * 4.2e4), -0.85f, 0.85f);
                    float frontGrad = FMath.Clamp((float)(Math.Sqrt(dTdx * dTdx + dTdy * dTdy) * 1.1e5 - 0.35), 0f, 1f);

                    double dhDx = (elevM[iE] - elevM[iW]) * 0.5 * invCellLonDistM / CosLat[i];
                    double dhDy = (elevM[iN] - elevM[iS]) * 0.5 * invCellLatDistM;
                    float orogW = FMath.Clamp((float)((uLowMs[i] * dhDx + vLowMs[i] * dhDy) * 14.0), -0.75f, 0.85f);

                    float slpClimEddy = slpClim[i] - zonalSlp[y];
                    float climSubside = FMath.Clamp((slpClim[i] - 1015.5f) / 15f, 0f, 0.80f) + FMath.Clamp(slpClimEddy / 24f, 0f, 0.50f);
                    float synTroughLift = FMath.Clamp(-slpSyn[i] / 8.0f, -1.15f, 1.15f);

                    float cycCore = 0f, cycMoat = 0f;
                    if (isFinalStep && storms != null)
                    {
                        double uCoord = (i % Width + 0.5) / Width, vCoord = (y + 0.5) / Height;
                        storms.SampleCloudFootprint(uCoord, vCoord, calendarDay, out cycCore, out cycMoat, out _, out _);
                    }

                    // Pure zero-mean synoptic vertical velocity (troughs/ridges/WPSH pulses/eddy warm-cold advection)
                    float wTransient = FMath.Clamp(
                        0.60f * synTroughLift + 0.36f * convEddy + 0.30f * eddyWarmAdv + 0.85f * cycCore - 0.62f * cycMoat,
                        -1.10f, 1.15f);
                    // Full vertical velocity for prognostic moisture condensation (synoptic waves + eddy/orographic convergence - mild static high lid)
                    float wSyn = FMath.Clamp(
                        wTransient + 0.22f * convTot + 0.16f * warmAdv
                        + 0.14f * frontGrad * Math.Max(0f, convTot + 0.25f)
                        + 0.28f * orogW - 0.18f * climSubside,
                        -1.10f, 1.15f);

                    // Bulk aerodynamic surface evaporation (~2.2-day marine recharge timescale; heavy rain-out P > E genuinely depletes Qv):
                    float qSatNeutral = qSatNeutralArr[i];
                    float qvEquil = qvClimEquil[i];
                    float windSpdMs = (float)Math.Sqrt(uLowMs[i] * uLowMs[i] + vLowMs[i] * vLowMs[i] + 9.0);
                    float evapTransfer = (OceanMask[i] > 0.5f ? 0.122f : 0.078f) * (windSpdMs / 8.5f) * soilAvail[i] * (1f - 0.62f * Math.Max(0f, -wTransient));
                    float evapKgM2 = Math.Max(0f, qvEquil - qv[i]) * FMath.Clamp(evapTransfer, 0f, 0.30f);
                    if (wSyn > 0.05f && rhClim[i] > 0.28f)
                    {
                        // Moisture convergence recharge scales down once column condensate is heavy (rain-out downdrafts stabilize boundary layer)
                        float precipStabilize = 1f - 0.68f * FMath.Clamp((qcLow[i] + qcHigh[i]) / Math.Max(0.55f, 0.048f * qSatNeutral), 0f, 1f);
                        float convFlux = qSatNeutral * 0.081f * wSyn * soilAvail[i] * precipStabilize * FMath.Clamp((rhClim[i] - 0.24f) / 0.48f, 0f, 1f);
                        evapKgM2 += convFlux;
                    }
                    qv[i] += evapKgM2;
                    totalEvapArea += evapKgM2 * CellArea[i];

                    // Clausius-Clapeyron Saturation Adjustment via fast Pade-2 exponential of 0.066 * dTAdiab:
                    // ascending air cools (qSatParcel < qSatNeutral) and condenses; subsiding air warms (qSatParcel > qSatNeutral)
                    // and, together with rain-out / dry-advection vapor deficit (qv < qvEquil), evaporates cloud condensate.
                    float dTAdiab = wSyn >= 0f ? -7.3f * wSyn : -8.3f * wSyn;
                    float xCc = FMath.Clamp(0.066f * dTAdiab, -0.75f, 0.85f);
                    float ccScale = (1f + 0.5f * xCc + 0.12f * xCc * xCc) / Math.Max(0.35f, 1f - 0.5f * xCc + 0.12f * xCc * xCc);
                    float qSatParcel = qSatNeutral * ccScale;

                    // Pure synoptic adiabatic warming (from zero-mean wTransient, avoiding double-counting static Hadley highs)
                    // + prognostic vapor depletion from precipitation fallout & dry-air advection:
                    float dTTrans = wTransient >= 0f ? -7.3f * wTransient : -8.6f * wTransient;
                    float xTrans = FMath.Clamp(0.066f * dTTrans, -0.75f, 0.85f);
                    float ccTrans = (1f + 0.5f * xTrans + 0.12f * xTrans * xTrans) / Math.Max(0.35f, 1f - 0.5f * xTrans + 0.12f * xTrans * xTrans);
                    float qSatTrans = qSatNeutral * ccTrans;
                    float excessSubSat = Math.Max(0f, (qSatTrans - qSatNeutral) + 1.08f * Math.Max(0f, qvEquil - qv[i])) / Math.Max(0.35f, qSatNeutral);

                    float evapSignal = 0f;
                    if (qv[i] > qSatParcel)
                    {
                        float condense = (qv[i] - qSatParcel) * 0.79f;
                        qv[i] -= condense;
                        float highShare = FMath.Clamp(0.18f + 0.48f * FMath.Clamp((sfcTempK[i] - 285f) / 18f, 0f, 1f) * wSyn + 0.24f * frontGrad, 0.12f, 0.65f);
                        qcLow[i] += condense * (1f - highShare);
                        qcHigh[i] += condense * highShare;
                    }
                    else
                    {
                        float protect = scShield[i];
                        float subDeficit = (qSatParcel - qv[i]) / Math.Max(0.25f, qSatParcel);
                        float evapStrength = FMath.Clamp((0.18f + 1.95f * excessSubSat + 0.50f * subDeficit + 0.54f * Math.Max(0f, -wTransient)) * (1f - 0.78f * protect), 0f, 0.95f);
                        float dLow = qcLow[i] * evapStrength;
                        float dHigh = qcHigh[i] * FMath.Clamp(evapStrength * 0.85f, 0f, 0.93f);
                        qcLow[i] -= dLow;
                        qcHigh[i] -= dHigh;
                        qv[i] += dLow + dHigh;

                        // Extra evaporation / subsidence dissipation [0, 1] (0 = undisturbed equilibrium or saturated ascent):
                        evapSignal = FMath.Clamp(
                            (1.90f * FMath.Clamp((excessSubSat - 0.022f) / 0.22f, 0f, 1f)
                            + 0.58f * FMath.Clamp((-wTransient - 0.055f) / 0.39f, 0f, 1f))
                            * (1f - 0.84f * protect),
                            0f, 1f);
                    }

                    // Bergeron-Findeisen temperature-scaled autoconversion & precipitation fallout:
                    // removes condensed water from column (preventing cold-ocean ice pile-up and creating genuine post-rain drying).
                    float warmLiqScale = FMath.Clamp(qSatNeutral / 46f, 0.12f, 1.10f);
                    float qCritLow = 0.68f * warmLiqScale + 2.25f * scShield[i];
                    float qCritHigh = 0.38f * warmLiqScale;
                    float rainLow = Math.Max(0f, qcLow[i] - qCritLow) * (0.66f + 0.22f * Math.Max(0f, wSyn));
                    float rainHigh = Math.Max(0f, qcHigh[i] - qCritHigh) * 0.55f;
                    qcLow[i] -= rainLow;
                    qcHigh[i] -= rainHigh;
                    totalPrecipArea += (rainLow + rainHigh) * CellArea[i];

                    if (isFinalStep)
                    {
                        float totCond = qcLow[i] + qcHigh[i];
                        float optScale = 0.38f / Math.Max(0.45f, (float)Math.Sqrt(warmLiqScale));
                        CloudWaterNorm[i] = 1f - (float)Math.Exp(-totCond * optScale);
                        HighCloudNorm[i] = 1f - (float)Math.Exp(-qcHigh[i] * optScale * 1.45);
                        float qvDepletion = qvEquil > 0.35f ? FMath.Clamp((qvEquil - qv[i]) / (0.24f * qvEquil), -1f, 1f) : 0f;
                        VaporAnomaly[i] = FMath.Clamp(-0.68f * qvDepletion + 0.52f * CloudWaterNorm[i] - 0.38f * evapSignal, -1f, 1f);
                        Evaporation[i] = CloudWaterNorm[i] > 0.58f && wTransient > 0.12f ? 0f : evapSignal;
                        TransientDrying[i] = FMath.Clamp(Evaporation[i] * (1f - 0.78f * CloudWaterNorm[i]), 0f, 1f);
                        VerticalMotion[i] = FMath.Clamp(wTransient, -1f, 1f);
                    }
                }
            }

            double finalWaterSum = 0.0;
            for (int y = 0; y < Height; y++)
            {
                float rowCloudSum = 0f;
                int rowOff = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int i = rowOff + x;
                    finalWaterSum += (qv[i] + qcLow[i] + qcHigh[i]) * CellArea[i];
                    rowCloudSum += CloudWaterNorm[i];
                }
                float rowBase = (rowCloudSum / Width) * 0.52f;
                for (int x = 0; x < Width; x++)
                {
                    int i = rowOff + x;
                    EddyCondensate[i] = FMath.Clamp((CloudWaterNorm[i] - rowBase) * 1.25f, 0f, 1f);
                }
            }

            FinalWaterKgM2 = finalWaterSum / TotalSphereArea;
            CumulativeEvapKgM2 = totalEvapArea / TotalSphereArea;
            CumulativePrecipKgM2 = totalPrecipArea / TotalSphereArea;
            WaterBudgetErrorKgM2 = Math.Abs(FinalWaterKgM2 + CumulativePrecipKgM2 - InitialWaterKgM2 - CumulativeEvapKgM2);

            cachedWorld = world;
            cachedRevision = world.TerrainRevision;
            cachedClimateFp = climFp;
            cachedTick = targetTick;
            cachedHadStorms = hasStorms;
            if (hasStorms)
            {
                cachedN = storms.North; cachedS = storms.South;
                cachedPN = storms.PreviousNorth; cachedPS = storms.PreviousSouth;
            }
        }

        public void SampleBilinear(double u, double v, out float cloudNorm, out float vaporAnom, out float evap, out float vertMotion)
        {
            double px = u * Width - 0.5;
            double py = FMath.Clamp((float)(v * Height - 0.5), 0f, Height - 1f);
            int ix = (int)Math.Floor(px), iy = (int)py;
            float tx = (float)(px - ix), ty = (float)(py - iy);
            int x0 = FMath.Wrap(ix, Width), x1 = x0 + 1 == Width ? 0 : x0 + 1;
            int y0 = iy * Width, y1 = Math.Min(iy + 1, Height - 1) * Width;
            int c00 = y0 + x0, c10 = y0 + x1, c01 = y1 + x0, c11 = y1 + x1;

            cloudNorm = Bilerp(EddyCondensate, c00, c10, c01, c11, tx, ty);
            vaporAnom = Bilerp(VaporAnomaly, c00, c10, c01, c11, tx, ty);
            evap = Bilerp(TransientDrying, c00, c10, c01, c11, tx, ty);
            vertMotion = Bilerp(VerticalMotion, c00, c10, c01, c11, tx, ty);
        }

        static float Bilerp(float[] a, int c00, int c10, int c01, int c11, float tx, float ty)
            => FMath.Lerp(FMath.Lerp(a[c00], a[c10], tx), FMath.Lerp(a[c01], a[c11], tx), ty);

        // Fast trig-free tangent-grid semi-Lagrangian advection with exact spherical area-mass conservation.
        static void AdvectPairConserving(float[] srcA, float[] srcB, float[] dstA, float[] dstB, float[] uMs, float[] vMs)
        {
            const float cellsPerMeterLon = (float)(SecondsPerStep * Width / (2.0 * Math.PI * EarthRadiusM));
            const float cellsPerMeterLat = (float)(SecondsPerStep * Height / (Math.PI * EarthRadiusM));
            double beforeA = 0.0, afterA = 0.0, beforeB = 0.0, afterB = 0.0;
            for (int y = 0; y < Height; y++)
            {
                float invCos = 1f / CosLat[y * Width];
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    double wArea = CellArea[i];
                    beforeA += srcA[i] * wArea;
                    beforeB += srcB[i] * wArea;

                    float px = x - FMath.Clamp(uMs[i] * cellsPerMeterLon * invCos, -14f, 14f);
                    float py = FMath.Clamp(y - vMs[i] * cellsPerMeterLat, 0f, Height - 1f);
                    int ix = (int)Math.Floor(px), iy = (int)py;
                    float tx = px - ix, ty = py - iy;
                    int x0 = FMath.Wrap(ix, Width), x1 = x0 + 1 == Width ? 0 : x0 + 1;
                    int y0 = iy * Width, y1 = Math.Min(iy + 1, Height - 1) * Width;
                    int c00 = y0 + x0, c10 = y0 + x1, c01 = y1 + x0, c11 = y1 + x1;

                    float valA = Bilerp(srcA, c00, c10, c01, c11, tx, ty);
                    float valB = Bilerp(srcB, c00, c10, c01, c11, tx, ty);
                    dstA[i] = valA;
                    dstB[i] = valB;
                    afterA += valA * wArea;
                    afterB += valB * wArea;
                }
            }
            if (afterA > 1e-12 && beforeA > 0.0)
            {
                float scaleA = (float)(beforeA / afterA);
                for (int i = 0; i < Count; i++) dstA[i] *= scaleA;
            }
            if (afterB > 1e-12 && beforeB > 0.0)
            {
                float scaleB = (float)(beforeB / afterB);
                for (int i = 0; i < Count; i++) dstB[i] *= scaleB;
            }
        }

        static void AdvectSingleConserving(float[] src, float[] dst, float[] uMs, float[] vMs)
        {
            const float cellsPerMeterLon = (float)(SecondsPerStep * Width / (2.0 * Math.PI * EarthRadiusM));
            const float cellsPerMeterLat = (float)(SecondsPerStep * Height / (Math.PI * EarthRadiusM));
            double before = 0.0, after = 0.0;
            for (int y = 0; y < Height; y++)
            {
                float invCos = 1f / CosLat[y * Width];
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    double wArea = CellArea[i];
                    before += src[i] * wArea;

                    float px = x - FMath.Clamp(uMs[i] * cellsPerMeterLon * invCos, -18f, 18f);
                    float py = FMath.Clamp(y - vMs[i] * cellsPerMeterLat, 0f, Height - 1f);
                    int ix = (int)Math.Floor(px), iy = (int)py;
                    float tx = px - ix, ty = py - iy;
                    int x0 = FMath.Wrap(ix, Width), x1 = x0 + 1 == Width ? 0 : x0 + 1;
                    int y0 = iy * Width, y1 = Math.Min(iy + 1, Height - 1) * Width;
                    float val = Bilerp(src, y0 + x0, y0 + x1, y1 + x0, y1 + x1, tx, ty);
                    dst[i] = val;
                    after += val * wArea;
                }
            }
            if (after > 1e-12 && before > 0.0)
            {
                float scale = (float)(before / after);
                for (int i = 0; i < Count; i++) dst[i] *= scale;
            }
        }

        static void EvalJetColumn(GlebaRossbyWaveField.RossbyHarmonic[] waves, float baseLatRad, double lonRad, double rossbyTimeRad, out float jetLat, out float slope)
        {
            double j = baseLatRad, s = 0.0;
            for (int i = 0; i < waves.Length; i++)
            {
                var w = waves[i];
                double k = w.WaveNumber - 1;
                double phase = k * (w.PhaseRadians + rossbyTimeRad + lonRad);
                j += w.AmplitudeRadians * Math.Cos(phase);
                s -= w.AmplitudeRadians * k * Math.Sin(phase);
            }
            jetLat = (float)j;
            slope = (float)s;
        }

        static double Phase(uint seed, uint salt) => FMath.Unit(FMath.Hash(seed ^ salt)) * (2.0 * Math.PI);
    }
}
