using System;
using System.Collections.Generic;

namespace Orbis.Flat
{
    public enum GlebaShoreType : byte
    {
        OnShore = 0,
        OffShore = 1,
        NotApplicable = 2
    }

    /// <summary>
    /// Gleba v0.3.6 atmospheric circulation bands (ITCZ, STHZ, PolarFront), Rossby wave harmonics,
    /// segmented sea-level pressure / rainfall-impact ovals (SeaLevelPressureField), and Sverdrup-like
    /// coastal-deflected ocean dynamic height currents (OceanCurrentsField).
    /// Generalized to width x height equirectangular grids with exact periodic longitude symmetry.
    /// </summary>
    public sealed class GlebaCirculationBands
    {
        const float DegToRad = 0.017453292f;
        const float RadToDeg = 57.29578f;
        const float EarthCircumferenceKm = 40030.176f;

        public readonly int Width;
        public readonly int Height;
        public readonly GlebaClimateConfig Config;

        // Base winter (t=0, Jan) and summer (t=0.5, Jul) band latitudes in radians [length = Width]
        public readonly float[] ItczBaseSummerRad;
        public readonly float[] ItczBaseWinterRad;
        public readonly float[] SthzNorthBaseSummerRad;
        public readonly float[] SthzNorthBaseWinterRad;
        public readonly float[] SthzSouthBaseSummerRad;
        public readonly float[] SthzSouthBaseWinterRad;
        public readonly float[] PolarNorthBaseSummerRad;
        public readonly float[] PolarNorthBaseWinterRad;
        public readonly float[] PolarSouthBaseSummerRad;
        public readonly float[] PolarSouthBaseWinterRad;

        // Active seasonal band latitudes in radians [length = Width]
        public readonly float[] ItczLatRad;
        public readonly float[] SthzNorthLatRad;
        public readonly float[] SthzSouthLatRad;
        public readonly float[] PolarNorthLatRad;
        public readonly float[] PolarSouthLatRad;

        // Active 2D fields [length = Width * Height]
        public readonly float[] PressureHpa;
        public readonly float[] RainfallImpact;
        public readonly float[] WindEast;
        public readonly float[] WindNorth;
        public readonly GlebaShoreType[] ShoreTypes;

        public readonly float[] OceanDynamicHeight;
        public readonly float[] CurrentEast;
        public readonly float[] CurrentNorth;

        readonly double[] pressureAccum;
        readonly double[] rainfallAccum;
        readonly double[] pressureCounting;
        readonly double[] rainfallCounting;
        readonly int[] stampQueue;
        readonly int[] visitedStamp;
        int currentStampId;

        static GlebaClimateConfig.CurvePoint P(float x, float y) => new GlebaClimateConfig.CurvePoint(x, y);

        // Prevailing planetary zonal wind u_prev (03_pressure_winds_currents.asm @ 0x1822dcfe8)
        static readonly GlebaClimateConfig.CurvePoint[] PrevailingZonalWindCurve = {
            P(-90f, -1f), P(-65f, -1f), P(-55f, 1f), P(-35f, 1f), P(-25f, -1f),
            P(25f, -1f),  P(35f, 1f),   P(55f, 1f),  P(65f, -1f), P(90f, -1f)
        };

        // Tropical ocean gyre vs equatorial countercurrent curve (03 @ 0x1822dce50)
        static readonly GlebaClimateConfig.CurvePoint[] OceanItczCurve = {
            P(0.00f, 0.00f), P(0.65f, 0.10f), P(0.70f, 0.50f), P(0.75f, 0.90f), P(1.00f, 1.00f)
        };

        public GlebaCirculationBands(int width, int height, GlebaClimateConfig config = null)
        {
            if (width < 4 || height < 4) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            Config = config ?? GlebaClimateConfig.Default;

            ItczBaseSummerRad = new float[width];
            ItczBaseWinterRad = new float[width];
            SthzNorthBaseSummerRad = new float[width];
            SthzNorthBaseWinterRad = new float[width];
            SthzSouthBaseSummerRad = new float[width];
            SthzSouthBaseWinterRad = new float[width];
            PolarNorthBaseSummerRad = new float[width];
            PolarNorthBaseWinterRad = new float[width];
            PolarSouthBaseSummerRad = new float[width];
            PolarSouthBaseWinterRad = new float[width];

            ItczLatRad = new float[width];
            SthzNorthLatRad = new float[width];
            SthzSouthLatRad = new float[width];
            PolarNorthLatRad = new float[width];
            PolarSouthLatRad = new float[width];

            int count = width * height;
            PressureHpa = new float[count];
            RainfallImpact = new float[count];
            WindEast = new float[count];
            WindNorth = new float[count];
            ShoreTypes = new GlebaShoreType[count];

            OceanDynamicHeight = new float[count];
            CurrentEast = new float[count];
            CurrentNorth = new float[count];

            pressureAccum = new double[count];
            rainfallAccum = new double[count];
            pressureCounting = new double[count];
            rainfallCounting = new double[count];
            stampQueue = new int[count];
            visitedStamp = new int[count];
        }

        /// <summary>
        /// Initializes all base summer/winter circulation bands from terrain landFraction, peakElevation,
        /// and meridional saldo arrays (matching Itcz::initialize, Sthz::initialize, PolarFront::initialize).
        /// </summary>
        public void InitializeBands(float[] landFraction, float[] peakElevationMeters, float[] saldoNorth, float[] saldoSouth)
        {
            // 1. ITCZ base summer (+4 -> +32.5) & winter (-4 -> -32.5)
            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                ItczBaseSummerRad, +1, +Config.ItczSeaSummerPosDegrees, +Config.ItczLandSummerPosDegrees);
            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                ItczBaseWinterRad, -1, -Config.ItczSeaSummerPosDegrees, -Config.ItczLandSummerPosDegrees);
            BlurBandLongitudes(ItczBaseSummerRad);
            BlurBandLongitudes(ItczBaseWinterRad);

            // 2. STHZ base arrays with exact hemispheric seasonal symmetry:
            // July (t=0.5) is NH local summer (+30..+33) and SH local winter (-28..-30);
            // Jan  (t=0.0) is NH local winter (+28..+30) and SH local summer (-30..-33).
            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                SthzNorthBaseSummerRad, +1, +Config.SthzSeaSummerPosDegrees, +Config.SthzLandSummerPosDegrees);
            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                SthzNorthBaseWinterRad, +1, +Config.SthzSeaWinterPosDegrees, +Config.SthzLandWinterPosDegrees);
            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                SthzSouthBaseSummerRad, -1, -Config.SthzSeaWinterPosDegrees, -Config.SthzLandWinterPosDegrees);
            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                SthzSouthBaseWinterRad, -1, -Config.SthzSeaSummerPosDegrees, -Config.SthzLandSummerPosDegrees);

            BlurBandLongitudes(SthzNorthBaseSummerRad);
            BlurBandLongitudes(SthzNorthBaseWinterRad);
            BlurBandLongitudes(SthzSouthBaseSummerRad);
            BlurBandLongitudes(SthzSouthBaseWinterRad);

            // 3. PolarFront derived from mean STHZ base latitudes
            float meanNS = MeanDegrees(SthzNorthBaseSummerRad);
            float meanNW = MeanDegrees(SthzNorthBaseWinterRad);
            float meanSS = MeanDegrees(SthzSouthBaseSummerRad);
            float meanSW = MeanDegrees(SthzSouthBaseWinterRad);

            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                PolarNorthBaseSummerRad, +1, 0.5f * (meanNS + 90f), (meanNS + 135f) / 2.5f);
            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                PolarNorthBaseWinterRad, +1, 0.5f * (meanNW + 90f), (meanNW + 135f) / 2.5f);
            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                PolarSouthBaseSummerRad, -1, 0.5f * (meanSS - 90f), (meanSS - 135f) / 2.5f);
            MarchBand(landFraction, peakElevationMeters, saldoNorth, saldoSouth,
                PolarSouthBaseWinterRad, -1, 0.5f * (meanSW - 90f), (meanSW - 135f) / 2.5f);

            BlurBandLongitudes(PolarNorthBaseSummerRad);
            BlurBandLongitudes(PolarNorthBaseWinterRad);
            BlurBandLongitudes(PolarSouthBaseSummerRad);
            BlurBandLongitudes(PolarSouthBaseWinterRad);
        }

        float MeanDegrees(float[] rads)
        {
            double sum = 0;
            for (int x = 0; x < Width; x++) sum += rads[x] * RadToDeg;
            return (float)(sum / Width);
        }

        static float CellLatRad(int y, int height)
            => (float)((y - (height - 1) * 0.5) * (Math.PI / height));

        static int LatToRowSymmetric(float latRad, int height)
        {
            double c = (height - 1) * 0.5;
            double offset = (latRad / Math.PI) * height;
            if (latRad >= 0f)
                return FMath.Clamp((int)Math.Floor(c + offset + 0.5), 0, height - 1);
            return height - 1 - FMath.Clamp((int)Math.Floor(c - offset + 0.5), 0, height - 1);
        }

        void MarchBand(
            float[] landFraction, float[] peakElevationMeters, float[] saldoNorth, float[] saldoSouth,
            float[] outputRad, int dir, float seaPosDeg, float landPosDeg)
        {
            float seaRad = seaPosDeg * DegToRad;
            int y0 = LatToRowSymmetric(seaRad, Height);

            for (int x = 0; x < Width; x++)
            {
                float outRad = seaRad;
                // Note: saldoSouth in CalculateSaldo is stored >= 0; Gleba's signed saldo sum is (saldoNorth - saldoSouth).
                float signedSaldoSum = saldoNorth[x] - saldoSouth[x];
                if (!MarchSingleColumn(x, y0 + dir, dir, seaPosDeg, landPosDeg, landFraction, peakElevationMeters, signedSaldoSum, ref outRad))
                    MarchSingleColumn(x, y0 - dir, dir, seaPosDeg, landPosDeg, landFraction, peakElevationMeters, signedSaldoSum, ref outRad);
                outputRad[x] = outRad;
            }
        }

        bool MarchSingleColumn(
            int x, int startY, int step, float seaPosDeg, float landPosDeg,
            float[] landFraction, float[] peakElevationMeters, float signedSaldoSum, ref float outRad)
        {
            if (startY < 0 || startY >= Height)
            {
                outRad += FMath.Clamp(0.5f * signedSaldoSum, -1f, 1f) * 3.5f * DegToRad;
                return false;
            }

            float maxSpanDeg = Math.Abs(landPosDeg - seaPosDeg);
            int y = startY;
            bool foundLand = false;
            float latStartRad = 0f;
            float maxElev = 0f;

            // Pure-land threshold (>= 0.999f so area-averaged pure-land cells match Gleba's water_tiles == 0)
            const float PureLandThreshold = 0.999f;

            while (y >= 0 && y < Height)
            {
                float phiRad = CellLatRad(y, Height);
                float phiDeg = phiRad * RadToDeg;
                if (Math.Abs(phiDeg - seaPosDeg) > maxSpanDeg) break;

                int idx = y * Width + x;
                if (landFraction[idx] >= PureLandThreshold)
                {
                    if (!foundLand)
                    {
                        foundLand = true;
                        latStartRad = phiRad;
                        maxElev = Math.Max(0f, peakElevationMeters[idx]);
                        outRad = landPosDeg * DegToRad;
                    }
                    else
                    {
                        maxElev = Math.Max(maxElev, peakElevationMeters[idx]);
                    }
                }
                else if (foundLand)
                {
                    // Narrow island / peninsula early exit at chord midpoint
                    outRad = 0.5f * (latStartRad + phiRad);
                    return true;
                }
                y += step;
            }

            if (!foundLand)
            {
                outRad += FMath.Clamp(0.5f * signedSaldoSum, -1f, 1f) * 3.5f * DegToRad;
                return false;
            }

            // Continue scanning across deep continent to find poleward coastline
            while (y >= 0 && y < Height)
            {
                int idx = y * Width + x;
                if (landFraction[idx] >= PureLandThreshold)
                {
                    maxElev = Math.Max(maxElev, peakElevationMeters[idx]);
                }
                else
                {
                    float phiOcean2 = CellLatRad(y, Height);
                    float phiOneThird = (2f * latStartRad + phiOcean2) / 3f;
                    float sign = outRad >= 0f ? 1f : -1f;
                    outRad = sign * Math.Min(Math.Abs(outRad), Math.Abs(phiOneThird));
                    break;
                }
                y += step;
            }

            float fElev = FMath.Clamp(maxElev / 40f, 0f, 1f);
            fElev *= fElev;
            float outDeg = outRad * RadToDeg;
            outRad = (seaPosDeg + (outDeg - seaPosDeg) * fElev) * DegToRad;
            return true;
        }

        void BlurBandLongitudes(float[] slice)
        {
            // Scale iteration count quadratically with Width relative to Gleba's W=201 so physical
            // longitudinal Gaussian smoothing width (in degrees/km) is resolution-independent.
            int iterations = Math.Max(8, (int)Math.Round(2.0 * Width * Width / 201.0));
            var old = new float[Width];
            for (int iter = 0; iter < iterations; iter++)
            {
                Buffer.BlockCopy(slice, 0, old, 0, Width * sizeof(float));
                for (int x = 0; x < Width; x++)
                {
                    int left = x == 0 ? Width - 1 : x - 1;
                    int right = x + 1 == Width ? 0 : x + 1;
                    slice[x] = 0.90f * old[x] + 0.05f * (old[left] + old[right]);
                }
            }
        }

        /// <summary>
        /// Updates active circulation bands, sea-level pressure / rainfall-impact ovals, surface winds,
        /// shore types, and coastal-deflected ocean dynamic height currents for timeOfYear in [0, 1].
        /// </summary>
        public void TickSeason(float timeOfYear, float[] landFraction, float[] distanceToLandKm)
        {
            float triangle = FMath.Clamp(1f - Math.Abs(1f - 2f * timeOfYear), 0f, 1f);
            float itczSummerWeight = GlebaClimateConfig.QuinticSmoothstep(triangle);

            for (int x = 0; x < Width; x++)
            {
                ItczLatRad[x] = FMath.Lerp(ItczBaseWinterRad[x], ItczBaseSummerRad[x], itczSummerWeight);
                SthzNorthLatRad[x] = FMath.Lerp(SthzNorthBaseWinterRad[x], SthzNorthBaseSummerRad[x], triangle);
                SthzSouthLatRad[x] = FMath.Lerp(SthzSouthBaseWinterRad[x], SthzSouthBaseSummerRad[x], triangle);
                PolarNorthLatRad[x] = FMath.Lerp(PolarNorthBaseWinterRad[x], PolarNorthBaseSummerRad[x], triangle);
                PolarSouthLatRad[x] = FMath.Lerp(PolarSouthBaseWinterRad[x], PolarSouthBaseSummerRad[x], triangle);

                // Ensure strict symmetric meridional ordering at extreme continental geometries
                ItczLatRad[x] = FMath.Clamp(ItczLatRad[x], SthzSouthLatRad[x] + 0.02f, SthzNorthLatRad[x] - 0.02f);
                PolarSouthLatRad[x] = Math.Min(SthzSouthLatRad[x] - 0.03f, PolarSouthLatRad[x]);
                PolarNorthLatRad[x] = Math.Max(SthzNorthLatRad[x] + 0.03f, PolarNorthLatRad[x]);
            }

            TickSeaLevelPressureAndWinds(timeOfYear, landFraction);
            TickOceanCurrents(distanceToLandKm);
        }

        struct BandSubsection
        {
            public bool IsLand;
            public bool IsCircumpolar;
            public List<int> Columns; // column indices x in continuous West-to-East order
        }

        float SampleBandLandFraction(float[] landFraction, int x, float latRad)
        {
            double py = FMath.Clamp((float)((latRad / Math.PI + 0.5) * Height - 0.5), 0f, Height - 1f);
            int y0 = (int)Math.Floor(py);
            int y1 = Math.Min(Height - 1, y0 + 1);
            float ty = (float)(py - y0);
            return FMath.Lerp(landFraction[y0 * Width + x], landFraction[y1 * Width + x], ty);
        }

        List<BandSubsection> SplitIntoSubsections(float[] bandLatRad, float[] landFraction)
        {
            float cellKm = EarthCircumferenceKm / Width;
            int maxGapCells = Math.Max(1, (int)Math.Ceiling(750f / cellKm));

            var landAtX = new bool[Width];
            for (int x = 0; x < Width; x++)
                landAtX[x] = SampleBandLandFraction(landFraction, x, bandLatRad[x]) > 0.5f;

            // Anchor circular scan at a physical Land -> Ocean transition so subsections are 100% independent of x = 0!
            int startX = -1;
            for (int x = 0; x < Width; x++)
            {
                int prev = x == 0 ? Width - 1 : x - 1;
                if (landAtX[prev] && !landAtX[x]) { startX = x; break; }
            }

            if (startX < 0)
            {
                var ring = new List<int>(Width);
                for (int x = 0; x < Width; x++) ring.Add(x);
                return new List<BandSubsection>(1)
                {
                    new BandSubsection { IsLand = landAtX[0], IsCircumpolar = true, Columns = ring }
                };
            }

            var rawRuns = new List<BandSubsection>(8);
            for (int k = 0; k < Width; k++)
            {
                int x = (startX + k) % Width;
                bool isLand = landAtX[x];
                if (rawRuns.Count == 0 || rawRuns[rawRuns.Count - 1].IsLand != isLand)
                {
                    rawRuns.Add(new BandSubsection
                    {
                        IsLand = isLand,
                        IsCircumpolar = false,
                        Columns = new List<int>(16) { x }
                    });
                }
                else
                {
                    rawRuns[rawRuns.Count - 1].Columns.Add(x);
                }
            }

            int firstWideOcean = -1;
            for (int i = 0; i < rawRuns.Count; i++)
            {
                if (!rawRuns[i].IsLand && rawRuns[i].Columns.Count > maxGapCells)
                {
                    firstWideOcean = i;
                    break;
                }
            }

            if (firstWideOcean < 0)
            {
                var ring = new List<int>(Width);
                for (int i = 0; i < rawRuns.Count; i++) ring.AddRange(rawRuns[i].Columns);
                return new List<BandSubsection>(1)
                {
                    new BandSubsection { IsLand = true, IsCircumpolar = true, Columns = ring }
                };
            }

            var merged = new List<BandSubsection>(rawRuns.Count);
            for (int step = 0; step < rawRuns.Count; step++)
            {
                var run = rawRuns[(firstWideOcean + step) % rawRuns.Count];
                if (!run.IsLand && run.Columns.Count <= maxGapCells)
                {
                    merged[merged.Count - 1].Columns.AddRange(run.Columns);
                }
                else if (run.IsLand && merged.Count > 0 && merged[merged.Count - 1].IsLand)
                {
                    merged[merged.Count - 1].Columns.AddRange(run.Columns);
                }
                else
                {
                    var copy = new List<int>(run.Columns.Count);
                    copy.AddRange(run.Columns);
                    merged.Add(new BandSubsection { IsLand = run.IsLand, IsCircumpolar = false, Columns = copy });
                }
            }

            return merged;
        }

        void TickSeaLevelPressureAndWinds(float timeOfYear, float[] landFraction)
        {
            const float HalfPi = 1.57079633f;

            // Stage 1: Zonal baseline pressure & counting initialization
            for (int y = 0; y < Height; y++)
            {
                float latRad = CellLatRad(y, Height);
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    float pfS = PolarSouthLatRad[x];
                    float sthzS = SthzSouthLatRad[x];
                    float itcz = ItczLatRad[x];
                    float sthzN = SthzNorthLatRad[x];
                    float pfN = PolarNorthLatRad[x];

                    float pZonal;
                    if (latRad < pfS)
                        pZonal = FMath.Lerp(1100f, 975f, SafeSpan(latRad + HalfPi, pfS + HalfPi));
                    else if (latRad < sthzS)
                        pZonal = FMath.Lerp(975f, 1025f, SafeSpan(latRad - pfS, sthzS - pfS));
                    else if (latRad < itcz)
                        pZonal = FMath.Lerp(1025f, 975f, SafeSpan(latRad - sthzS, itcz - sthzS));
                    else if (latRad < sthzN)
                        pZonal = FMath.Lerp(975f, 1025f, SafeSpan(latRad - itcz, sthzN - itcz));
                    else if (latRad < pfN)
                        pZonal = FMath.Lerp(1025f, 975f, SafeSpan(latRad - sthzN, pfN - sthzN));
                    else
                        pZonal = FMath.Lerp(975f, 1100f, SafeSpan(latRad - pfN, HalfPi - pfN));

                    int idx = row + x;
                    pressureAccum[idx] = pZonal * 0.15;
                    pressureCounting[idx] = 0.15;
                    rainfallAccum[idx] = 0.0;
                    rainfallCounting[idx] = 0.05;
                }
            }

            // Stage 2: Seasonal weights & band subsection splitting
            float nhWinter = FMath.Clamp(Math.Abs(2f * timeOfYear - 1f), 0f, 1f); // 1 in Jan (SH summer), 0 in Jul
            float nhSummer = 1f - nhWinter;                                      // 1 in Jul (NH summer), 0 in Jan

            var secItcz = SplitIntoSubsections(ItczLatRad, landFraction);
            var secSthzN = SplitIntoSubsections(SthzNorthLatRad, landFraction);
            var secSthzS = SplitIntoSubsections(SthzSouthLatRad, landFraction);
            var secPfN = SplitIntoSubsections(PolarNorthLatRad, landFraction);
            var secPfS = SplitIntoSubsections(PolarSouthLatRad, landFraction);

            // Stage 3: 9 Pressure stamps (hPa)
            DoSections(secItcz, ItczLatRad, pressureAccum, pressureCounting, true, 0f, 0.75f, 0f, 0f, false, 900f, 950f);
            DoSections(secSthzN, SthzNorthLatRad, pressureAccum, pressureCounting, false, 0f, 0.75f, 0f, 0f, false, 1100f - 200f * nhSummer, 1050f);
            DoSections(secSthzS, SthzSouthLatRad, pressureAccum, pressureCounting, false, 0f, 0.75f, 0f, 0f, false, 1100f - 200f * nhWinter, 1050f);
            DoSections(secSthzN, SthzNorthLatRad, pressureAccum, pressureCounting, false, 0f, 0.25f, 2500f, 1000f, true, 1100f, 1050f);
            DoSections(secSthzS, SthzSouthLatRad, pressureAccum, pressureCounting, false, 0f, 0.25f, 2500f, 1000f, true, 1100f, 1050f);
            DoSections(secSthzN, SthzNorthLatRad, pressureAccum, pressureCounting, false, 0f, 0.00f, 2500f, 1000f, true, 1100f, 1050f);
            DoSections(secSthzS, SthzSouthLatRad, pressureAccum, pressureCounting, false, 0f, 0.00f, 2500f, 1000f, true, 1100f, 1050f);
            DoSections(secPfN, PolarNorthLatRad, pressureAccum, pressureCounting, false, 0f, 0.75f, 0f, 0f, false, 1050f - 100f * nhSummer, 900f);
            DoSections(secPfS, PolarSouthLatRad, pressureAccum, pressureCounting, false, 0f, 0.75f, 0f, 0f, false, 1050f - 100f * nhWinter, 900f);

            // Stage 4: 9 Rainfall-impact stamps ([-1, +1])
            DoSections(secItcz, ItczLatRad, rainfallAccum, rainfallCounting, true, 0f, 0.75f, 0f, 0f, false, +1.0f, +0.5f);
            DoSections(secSthzN, SthzNorthLatRad, rainfallAccum, rainfallCounting, false, 0f, 0.75f, 0f, 0f, false, 1.5f * nhSummer - 1.0f, -1.0f);
            DoSections(secSthzS, SthzSouthLatRad, rainfallAccum, rainfallCounting, false, 0f, 0.75f, 0f, 0f, false, 1.5f * nhWinter - 1.0f, -1.0f);
            DoSections(secSthzN, SthzNorthLatRad, rainfallAccum, rainfallCounting, false, 0f, 0.25f, 2500f, 1000f, true, -1.0f, -1.0f);
            DoSections(secSthzS, SthzSouthLatRad, rainfallAccum, rainfallCounting, false, 0f, 0.25f, 2500f, 1000f, true, -1.0f, -1.0f);
            DoSections(secSthzN, SthzNorthLatRad, rainfallAccum, rainfallCounting, false, 0f, 0.00f, 2500f, 1000f, true, -1.0f, -1.0f);
            DoSections(secSthzS, SthzSouthLatRad, rainfallAccum, rainfallCounting, false, 0f, 0.00f, 2500f, 1000f, true, -1.0f, -1.0f);
            DoSections(secPfN, PolarNorthLatRad, rainfallAccum, rainfallCounting, false, 0f, 0.75f, 0f, 0f, false, +0.5f, +1.0f);
            DoSections(secPfS, PolarSouthLatRad, rainfallAccum, rainfallCounting, false, 0f, 0.75f, 0f, 0f, false, +0.5f, +1.0f);

            // Stage 5: Normalize & 20x 4-neighbor Jacobi blur on PressureHpa
            int count = Width * Height;
            for (int i = 0; i < count; i++)
            {
                PressureHpa[i] = (float)(pressureAccum[i] / Math.Max(1e-9, pressureCounting[i]));
                RainfallImpact[i] = (float)(rainfallAccum[i] / Math.Max(1e-9, rainfallCounting[i]));
            }
            Blur4Neighbor(PressureHpa, 20, 0.25f);

            // Stage 6: Surface Wind Synthesis & ShoreType Classification
            float wGeo = Config.GeostrophicWindWeight;
            float wPg = Config.PressureGradientWindWeight;
            float wPrev = Config.PrevailingWindWeight;
            // Scale central-difference pressure step so grid resolution changes preserve physical wind magnitude
            float gradScale = Width / 201f;

            for (int y = 0; y < Height; y++)
            {
                float latRad = CellLatRad(y, Height);
                float latDeg = latRad * RadToDeg;
                float absLatDeg = Math.Abs(latDeg);
                float wGeoEff = absLatDeg < 2.5f ? 0f : FMath.Clamp((absLatDeg - 2.5f) / 5f, 0f, 1f) * wGeo;
                float uPrev = GlebaClimateConfig.EvaluateCurve(PrevailingZonalWindCurve, latDeg);
                float wSum = Math.Max(1e-5f, wGeoEff + wPg + wPrev);
                float prevTerm = (wPrev * uPrev) / wSum;
                float latSpeed = 0.5f + 0.5f * (absLatDeg / 30f);

                int row = y * Width;
                int south = Math.Max(0, y - 1) * Width;
                int north = Math.Min(Height - 1, y + 1) * Width;

                for (int x = 0; x < Width; x++)
                {
                    int idx = row + x;
                    int xe = x + 1 == Width ? 0 : x + 1;
                    int xw = x == 0 ? Width - 1 : x - 1;

                    float pE = PressureHpa[row + xe];
                    float pW = PressureHpa[row + xw];
                    float pN = PressureHpa[north + x];
                    float pS = PressureHpa[south + x];

                    float pgX = (pW - pE) * gradScale;
                    float pgY = (pS - pN) * gradScale * (2f * Height / Width);

                    float geoX, geoY;
                    if (latRad >= 0f) { geoX = +pgY; geoY = -pgX; }
                    else              { geoX = -pgY; geoY = +pgX; }

                    float rawX = wGeoEff * geoX + wPg * pgX + prevTerm;
                    float rawY = wGeoEff * geoY + wPg * pgY;

                    float mag = (float)Math.Sqrt(rawX * rawX + rawY * rawY);
                    float normX = mag > 1e-6f ? rawX / mag : 0f;
                    float normY = mag > 1e-6f ? rawY / mag : 0f;

                    WindEast[idx] = (0.5f * rawX + 0.5f * normX) * latSpeed;
                    WindNorth[idx] = (0.5f * rawY + 0.5f * normY) * latSpeed;
                }
            }

            // ShoreType classification (2-cell upwind/downwind ocean probe)
            for (int y = 0; y < Height; y++)
            {
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int idx = row + x;
                    if (landFraction[idx] <= 0.01f)
                    {
                        ShoreTypes[idx] = GlebaShoreType.NotApplicable;
                        continue;
                    }
                    float vx = WindEast[idx], vy = WindNorth[idx];
                    float magSq = vx * vx + vy * vy;
                    if (magSq <= 0.001f)
                    {
                        ShoreTypes[idx] = GlebaShoreType.NotApplicable;
                        continue;
                    }
                    float inv = 2f / (float)Math.Sqrt(magSq);
                    float dx = vx * inv, dy = vy * inv;

                    int ux = FMath.Wrap((int)Math.Round(x - dx), Width);
                    int uy = FMath.Clamp((int)Math.Round(y - dy), 0, Height - 1);
                    if (1f - landFraction[uy * Width + ux] > 0.9f)
                    {
                        ShoreTypes[idx] = GlebaShoreType.OnShore;
                        continue;
                    }

                    int dxCell = FMath.Wrap((int)Math.Round(x + dx), Width);
                    int dyCell = FMath.Clamp((int)Math.Round(y + dy), 0, Height - 1);
                    ShoreTypes[idx] = (1f - landFraction[dyCell * Width + dxCell] > 0.9f)
                        ? GlebaShoreType.OffShore
                        : GlebaShoreType.NotApplicable;
                }
            }
        }

        void DoSections(
            List<BandSubsection> sections, float[] bandLatRad, double[] field, double[] counting,
            bool hasOverrideLat, float overrideLatRad, float centerFrac,
            float minWidthKm, float minPrevWidthKm, bool landOnly, float landVal, float seaVal)
        {
            if (sections.Count == 0) return;
            float cellKm = EarthCircumferenceKm / Width;

            for (int i = 0; i < sections.Count; i++)
            {
                var sec = sections[i];
                if (landOnly && !sec.IsLand) continue;
                int len = sec.Columns.Count;
                if (len == 0) continue;

                if (sec.IsCircumpolar)
                {
                    if (minPrevWidthKm > 0f) continue;
                    float valRing = sec.IsLand ? landVal : seaVal;
                    float rLatRing = (10f / 180f) * Height;
                    StampCircumpolarBand(sec.Columns, bandLatRad, field, counting, hasOverrideLat, overrideLatRad, rLatRing, 1.0f, valRing);
                    StampCircumpolarBand(sec.Columns, bandLatRad, field, counting, hasOverrideLat, overrideLatRad, 2f * rLatRing, 0.1f, valRing);
                    continue;
                }

                float widthKm = len * cellKm;
                if (widthKm < minWidthKm) continue;

                int prevIdx = i == 0 ? sections.Count - 1 : i - 1;
                float prevWidthKm = sections[prevIdx].Columns.Count * cellKm;
                if (prevWidthKm < minPrevWidthKm) continue;

                int k = FMath.Clamp((int)(centerFrac * len), 0, len - 1);
                int cx = sec.Columns[k];
                double latRad = bandLatRad[cx];
                if (hasOverrideLat)
                    latRad = latRad * 0.666 + overrideLatRad * 0.334;
                double cy = (latRad / Math.PI + 0.5) * Height - 0.5;

                float val = sec.IsLand ? landVal : seaVal;
                float rLonCells = 0.25f * len;
                float deg = (rLonCells / Width) * 90f;
                float rLatCells = (deg / 180f) * Height;
                if (deg > 10f)
                {
                    rLonCells *= 10f / deg;
                    rLatCells *= 10f / deg;
                }
                float strength = Math.Min(deg / 10f, 1f) * 0.9f + 0.1f;

                StampOval(field, counting, cx, cy, rLonCells, rLatCells, strength, val);
                StampOval(field, counting, cx, cy, 2f * rLonCells, 2f * rLatCells, 0.1f * strength, val);
            }
        }

        void StampCircumpolarBand(
            List<int> columns, float[] bandLatRad, double[] field, double[] counting,
            bool hasOverrideLat, float overrideLatRad, float rLatCells, float strength, float val)
        {
            if (rLatCells <= 0.01f || strength <= 0f) return;
            int radiusY = (int)Math.Ceiling(rLatCells) + 1;
            double invRLat = 1.0 / rLatCells;

            for (int k = 0; k < columns.Count; k++)
            {
                int cx = columns[k];
                double latRad = bandLatRad[cx];
                if (hasOverrideLat)
                    latRad = latRad * 0.666 + overrideLatRad * 0.334;
                double cy = (latRad / Math.PI + 0.5) * Height - 0.5;

                int yCenter = (int)Math.Floor(cy);
                int yMin = Math.Max(0, yCenter - radiusY);
                int yMax = Math.Min(Height - 1, yCenter + radiusY);
                for (int ny = yMin; ny <= yMax; ny++)
                {
                    double dNorm = Math.Abs(ny - cy) * invRLat;
                    if (dNorm >= 1.0) continue;
                    double w = (1.0 - Math.Sqrt(dNorm)) * strength;
                    int idx = ny * Width + cx;
                    field[idx] += val * w;
                    counting[idx] += w;
                }
            }
        }

        void StampOval(
            double[] field, double[] counting, int cx, double cy,
            float rLonCells, float rLatCells, float strength, float val)
        {
            if (rLonCells <= 0.01f || rLatCells <= 0.01f || strength <= 0f) return;

            currentStampId++;
            if (currentStampId == int.MaxValue)
            {
                Array.Clear(visitedStamp, 0, visitedStamp.Length);
                currentStampId = 1;
            }
            int stamp = currentStampId;

            double invRLon = 1.0 / rLonCells;
            double invRLat = 1.0 / rLatCells;

            // Seed both rows bracketing continuous cy so y = 31.5 seeds both row 31 and row 32 identically!
            int ySeed0 = FMath.Clamp((int)Math.Floor(cy), 0, Height - 1);
            int ySeed1 = FMath.Clamp((int)Math.Ceiling(cy), 0, Height - 1);
            int head = 0, tail = 0;

            for (int sy = ySeed0; sy <= ySeed1; sy++)
            {
                int seed = sy * Width + cx;
                if (visitedStamp[seed] == stamp) continue;
                double normY0 = (sy - cy) * invRLat;
                double dNormSq0 = normY0 * normY0;
                if (dNormSq0 >= 1.0) continue;
                visitedStamp[seed] = stamp;
                double w0 = (1.0 - Math.Sqrt(Math.Sqrt(dNormSq0))) * strength;
                field[seed] += val * w0;
                counting[seed] += w0;
                stampQueue[tail++] = seed;
            }

            while (head < tail)
            {
                int curr = stampQueue[head++];
                int px = curr % Width;
                int py = curr / Width;

                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = px, ny = py;
                    if (dir == 0) nx = px + 1 == Width ? 0 : px + 1;
                    else if (dir == 1) nx = px == 0 ? Width - 1 : px - 1;
                    else if (dir == 2) ny = Math.Min(Height - 1, py + 1);
                    else ny = Math.Max(0, py - 1);

                    int nb = ny * Width + nx;
                    if (visitedStamp[nb] == stamp) continue;

                    double dx = nx - cx;
                    if (dx > 0.5 * Width) dx -= Width;
                    else if (dx < -0.5 * Width) dx += Width;

                    double scaleX = dx >= 0.0 ? 1.0 : 0.25;
                    double normX = dx * invRLon;
                    double normY = (ny - cy) * invRLat;
                    double dNormSq = scaleX * normX * normX + normY * normY;

                    if (dNormSq >= 1.0) continue;

                    visitedStamp[nb] = stamp;
                    double w = (1.0 - Math.Sqrt(Math.Sqrt(Math.Max(0.0, Math.Min(1.0, dNormSq))))) * strength;
                    field[nb] += val * w;
                    counting[nb] += w;
                    stampQueue[tail++] = nb;
                }
            }
        }

        void TickOceanCurrents(float[] distanceToLandKm)
        {
            const float HalfPi = 1.57079633f;

            for (int y = 0; y < Height; y++)
            {
                float latRad = CellLatRad(y, Height);
                int row = y * Width;

                for (int x = 0; x < Width; x++)
                {
                    int idx = row + x;
                    float pfS = PolarSouthLatRad[x];
                    float sthzS = SthzSouthLatRad[x];
                    float itcz = ItczLatRad[x];
                    float sthzN = SthzNorthLatRad[x];
                    float pfN = PolarNorthLatRad[x];

                    float uPolar = 0f, uPf = 0f, uSthz = 0f, uItcz = 0f;
                    if (latRad < pfS)
                    {
                        uPf = SafeSpan(latRad + HalfPi, pfS + HalfPi);
                        uPolar = 1f - uPf;
                    }
                    else if (latRad < sthzS)
                    {
                        uSthz = SafeSpan(latRad - pfS, sthzS - pfS);
                        uPf = 1f - uSthz;
                    }
                    else if (latRad < itcz)
                    {
                        uItcz = SafeSpan(latRad - sthzS, itcz - sthzS);
                        uSthz = 1f - uItcz;
                    }
                    else if (latRad < sthzN)
                    {
                        uSthz = SafeSpan(latRad - itcz, sthzN - itcz);
                        uItcz = 1f - uSthz;
                    }
                    else if (latRad < pfN)
                    {
                        uPf = SafeSpan(latRad - sthzN, pfN - sthzN);
                        uSthz = 1f - uPf;
                    }
                    else
                    {
                        uPolar = SafeSpan(latRad - pfN, HalfPi - pfN);
                        uPf = 1f - uPolar;
                    }

                    float sPolar = Sigmoid50(uPolar);
                    float sPf = Sigmoid50(uPf);
                    float sSthz, sItcz;
                    if (uItcz <= 0f)
                    {
                        sSthz = Sigmoid50(uSthz);
                        sItcz = 0f;
                    }
                    else
                    {
                        sItcz = GlebaClimateConfig.EvaluateCurve(OceanItczCurve, uItcz);
                        sSthz = 1f - sItcz;
                    }

                    float distKm = Math.Max(0f, distanceToLandKm[idx]);
                    float wCoast = 0.1f * (1f - Math.Min(distKm, 500f) / 500f);
                    float hCoast;
                    if (sItcz > sSthz) hCoast = 1750f;
                    else if (sSthz > sPf) hCoast = 250f;
                    else if (sPf > sPolar) hCoast = 1750f;
                    else hCoast = 250f;

                    float hNum = 1000f
                        + 1100f * (0.25f * uPolar + sPolar)
                        + 900f * (0.25f * uPf + sPf)
                        + 1100f * (0.25f * uSthz + sSthz)
                        + 900f * (0.25f * uItcz + sItcz)
                        + hCoast * wCoast;
                    float hDen = 1f + sPolar + sPf + sSthz + sItcz
                        + 0.25f * (uPolar + uPf + uSthz + uItcz) + wCoast;

                    OceanDynamicHeight[idx] = hNum / hDen;
                }
            }

            Blur4Neighbor(OceanDynamicHeight, 20, 0.25f);

            float gradScale = Width / 201f;
            for (int y = 0; y < Height; y++)
            {
                float latRad = CellLatRad(y, Height);
                float absLatDeg = Math.Abs(latRad * RadToDeg);
                float wGeoEff = absLatDeg < 2.5f ? 0f : FMath.Clamp((absLatDeg - 2.5f) / 5f, 0f, 1f);

                int row = y * Width;
                int south = Math.Max(0, y - 1) * Width;
                int north = Math.Min(Height - 1, y + 1) * Width;

                for (int x = 0; x < Width; x++)
                {
                    int idx = row + x;
                    int xe = x + 1 == Width ? 0 : x + 1;
                    int xw = x == 0 ? Width - 1 : x - 1;

                    float hE = OceanDynamicHeight[row + xe];
                    float hW = OceanDynamicHeight[row + xw];
                    float hN = OceanDynamicHeight[north + x];
                    float hS = OceanDynamicHeight[south + x];

                    float hgX = (hW - hE) * gradScale;
                    float hgY = (hS - hN) * gradScale * (2f * Height / Width);

                    float geoX, geoY;
                    if (latRad >= 0f) { geoX = +hgY; geoY = -hgX; }
                    else              { geoX = -hgY; geoY = +hgX; }

                    float rawX = wGeoEff * geoX + 0.01f * hgX;
                    float rawY = wGeoEff * geoY + 0.01f * hgY;

                    float mag = (float)Math.Sqrt(rawX * rawX + rawY * rawY);
                    float normX = mag > 1e-6f ? rawX / mag : 0f;
                    float normY = mag > 1e-6f ? rawY / mag : 0f;

                    CurrentEast[idx] = 0.005f * (0.5f * rawX + 0.5f * normX);
                    CurrentNorth[idx] = 0.005f * (0.5f * rawY + 0.5f * normY);
                }
            }
        }

        void Blur4Neighbor(float[] field, int baseIterations, float strength)
        {
            var old = new float[field.Length];
            float keep = 1f - strength;
            float share = 0.25f * strength;

            for (int iter = 0; iter < baseIterations; iter++)
            {
                Buffer.BlockCopy(field, 0, old, 0, field.Length * sizeof(float));
                for (int y = 0; y < Height; y++)
                {
                    int row = y * Width;
                    int south = Math.Max(0, y - 1) * Width;
                    int north = Math.Min(Height - 1, y + 1) * Width;
                    for (int x = 0; x < Width; x++)
                    {
                        int xe = x + 1 == Width ? 0 : x + 1;
                        int xw = x == 0 ? Width - 1 : x - 1;
                        field[row + x] = keep * old[row + x]
                            + share * ((old[row + xe] + old[row + xw]) + (old[north + x] + old[south + x]));
                    }
                }
            }
        }

        static float Sigmoid50(float u)
        {
            double z = -50.0 * (u - 0.5);
            if (z > 40.0) return 0f;
            if (z < -40.0) return 1f;
            return (float)(1.0 / (1.0 + Math.Exp(z)));
        }

        static float SafeSpan(float num, float den)
        {
            if (den <= 1e-6f) return 0.5f;
            return FMath.Clamp(num / den, 0f, 1f);
        }
    }

    /// <summary>
    /// Gleba v0.3.6 GlobalRossbyWaveData (0x1808bb970, 0x1808ba8b0, 0x1808bae60):
    /// 4-jet (North/South Polar & Subtropical) planetary Rossby wave harmonics (wavenumbers 4..8, k = n - 1)
    /// blended with 3D unit-sphere synoptic background winds.
    /// </summary>
    public sealed class GlebaRossbyWaveField
    {
        public readonly struct RossbyHarmonic
        {
            public readonly int WaveNumber;
            public readonly double PhaseRadians;
            public readonly float AmplitudeRadians;
            public RossbyHarmonic(int waveNumber, double phaseRadians, float amplitudeRadians)
            {
                WaveNumber = waveNumber;
                PhaseRadians = phaseRadians;
                AmplitudeRadians = amplitudeRadians;
            }
        }

        public static readonly RossbyHarmonic[] DefaultNorthPolarWaves = {
            new RossbyHarmonic(4, 0.18483000993728638, 0.04363323f),
            new RossbyHarmonic(5, 2.123500108718872,   0.04363323f),
            new RossbyHarmonic(6, 0.0,                 0.17453292f),
            new RossbyHarmonic(7, 2.4310338497161865,  0.04363323f),
            new RossbyHarmonic(8, 0.5340707302093506,  0.04363323f)
        };
        public static readonly RossbyHarmonic[] DefaultSouthPolarWaves = {
            new RossbyHarmonic(4, 1.1848300695419312,  0.017453292f),
            new RossbyHarmonic(5, 12.123500108718872,  0.034906585f),
            new RossbyHarmonic(6, 0.15609999001026154, 0.08726646f),
            new RossbyHarmonic(7, 893.1208999156952,   0.017453292f),
            new RossbyHarmonic(8, 0.12895599007606506, 0.017453292f)
        };
        public static readonly RossbyHarmonic[] DefaultNorthSubtropicalWaves = {
            new RossbyHarmonic(4, 1.1848300695419312,  0.07853982f),
            new RossbyHarmonic(5, 6.123500108718872,   0.04363323f),
            new RossbyHarmonic(6, 0.12895000010728836, 0.08726646f),
            new RossbyHarmonic(7, 4.178399980068207,   0.04363323f),
            new RossbyHarmonic(8, 3.12809419631958,    0.10471976f)
        };
        public static readonly RossbyHarmonic[] DefaultSouthSubtropicalWaves = {
            new RossbyHarmonic(4, 0.5218009948730469,  0.034906585f),
            new RossbyHarmonic(5, 31.2389999628067,    0.05235988f),
            new RossbyHarmonic(6, 1.481909990310669,   0.017453292f),
            new RossbyHarmonic(7, 8954.12399995327,    0.034906585f),
            new RossbyHarmonic(8, 0.45389099419116974, 0.034906585f)
        };

        public readonly uint Seed;
        public double RossbyTimeRadians;
        public float BaseNorthPolarJetLatRad = 1.0471976f;       // +60 deg
        public float BaseSouthPolarJetLatRad = -1.0471976f;      // -60 deg
        public float BaseNorthSubtropicalJetLatRad = 0.5235988f; // +30 deg
        public float BaseSouthSubtropicalJetLatRad = -0.5235988f;// -30 deg

        public GlebaRossbyWaveField(uint worldSeed, double rossbyTimeRadians = 0.0)
        {
            Seed = worldSeed;
            RossbyTimeRadians = rossbyTimeRadians;
        }

        static double SmoothNoise3D(uint seed, double x, double y, double z)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
            float fx = (float)(x - ix), fy = (float)(y - iy), fz = (float)(z - iz);
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);
            float Cell(int cx, int cy, int cz)
            {
                unchecked { return FMath.Unit((uint)cx * 73856093u ^ (uint)cy * 19349663u ^ (uint)cz * 83492791u ^ seed) * 2f - 1f; }
            }
            float c00 = FMath.Lerp(Cell(ix, iy, iz), Cell(ix + 1, iy, iz), fx);
            float c10 = FMath.Lerp(Cell(ix, iy + 1, iz), Cell(ix + 1, iy + 1, iz), fx);
            float c01 = FMath.Lerp(Cell(ix, iy, iz + 1), Cell(ix + 1, iy, iz + 1), fx);
            float c11 = FMath.Lerp(Cell(ix, iy + 1, iz + 1), Cell(ix + 1, iy + 1, iz + 1), fx);
            return FMath.Lerp(FMath.Lerp(c00, c10, fy), FMath.Lerp(c01, c11, fy), fz);
        }

        public void GetBaseRossbyWinds(double latRad, double lonRad, out float vx, out float vy)
        {
            double colat = Math.PI * 0.5 - latRad;
            double sinColat = Math.Sin(colat);
            double px = 2.8215 * sinColat * Math.Cos(lonRad);
            double py = 2.8215 * Math.Cos(colat);
            double pz = -2.8215 * sinColat * Math.Sin(lonRad);
            uint fbmSeed = unchecked(Seed + 0x2515u);
            double t = RossbyTimeRadians;
            double nx = SmoothNoise3D(fbmSeed, px + t, py + t, pz + t);
            double ny = SmoothNoise3D(unchecked(fbmSeed ^ 0x9e3779b9u), px + t + 17.31, py + t - 9.47, pz + t + 5.83);
            double nm = SmoothNoise3D(unchecked(fbmSeed ^ 0x85ebca6bu), px - 11.19, py + 23.71, pz - 7.13);
            double len = Math.Sqrt(nx * nx + ny * ny);
            double ux = len > 1e-9 ? nx / len : 0.0;
            double uy = len > 1e-9 ? ny / len : 0.0;
            double mag = Math.Abs(nm) * 0.95 + 0.05;
            vx = (float)(ux * mag);
            vy = (float)(uy * mag);
        }

        static void EvalJet(RossbyHarmonic[] waves, float baseLatRad, double lonRad, double rossbyTimeRad, out double jetLat, out double slope)
        {
            jetLat = baseLatRad;
            slope = 0.0;
            for (int i = 0; i < waves.Length; i++)
            {
                var w = waves[i];
                double k = w.WaveNumber - 1; // Exact Gleba v0.3.6 decrement
                double phase = k * (w.PhaseRadians + rossbyTimeRad + lonRad);
                jetLat += w.AmplitudeRadians * Math.Cos(phase);
                slope -= w.AmplitudeRadians * k * Math.Sin(phase);
            }
        }

        public void GetJetStreamMovementAtLatLon(double latRad, double lonRad, out float vx, out float vy)
        {
            EvalJet(DefaultNorthPolarWaves, BaseNorthPolarJetLatRad, lonRad, RossbyTimeRadians, out double npLat, out double npSlope);
            EvalJet(DefaultSouthPolarWaves, BaseSouthPolarJetLatRad, lonRad, RossbyTimeRadians, out double spLat, out double spSlope);
            EvalJet(DefaultNorthSubtropicalWaves, BaseNorthSubtropicalJetLatRad, lonRad, RossbyTimeRadians, out double nsLat, out double nsSlope);
            EvalJet(DefaultSouthSubtropicalWaves, BaseSouthSubtropicalJetLatRad, lonRad, RossbyTimeRadians, out double ssLat, out double ssSlope);

            double h = FMath.Clamp((float)(latRad / (Math.PI / 12.0)), -1f, 1f) * 0.5 + 0.5;
            double wNp = 1.0 - FMath.Clamp((float)(Math.Abs(latRad - npLat) / 0.15), 0f, 1f);
            double wSp = 1.0 - FMath.Clamp((float)(Math.Abs(latRad - spLat) / 0.15), 0f, 1f);
            double wNs = 1.0 - FMath.Clamp((float)(Math.Abs(latRad - nsLat) / 0.15), 0f, 1f);
            double wSs = 1.0 - FMath.Clamp((float)(Math.Abs(latRad - ssLat) / 0.15), 0f, 1f);

            double wPolar = wSp + h * (wNp - wSp);
            double sPolar = spSlope + h * (npSlope - spSlope);
            double wSub = wSs + h * (wNs - wSs);
            double sSub = ssSlope + h * (nsSlope - ssSlope);

            double jetVx = wPolar + 0.33 * wSub;
            double jetVy = wPolar * sPolar + 0.33 * wSub * sSub;

            double wTot = FMath.Clamp((float)(wPolar + wSub), 0f, 1f);
            double bgBlend = FMath.Clamp((float)(1.0 - 2.0 * wTot), 0f, 1f);
            GetBaseRossbyWinds(latRad, lonRad, out float bgVx, out float bgVy);

            vx = (float)((1.0 - bgBlend) * jetVx + bgBlend * (0.2 * bgVx));
            vy = (float)((1.0 - bgBlend) * jetVy + bgBlend * (0.2 * bgVy));
        }
    }
}
