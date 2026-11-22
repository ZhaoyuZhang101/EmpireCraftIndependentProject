using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    /// <summary>
    /// Complete 12-month Gleba v0.3.6 physical supplement built on ORBIS's atmospheric grid.
    /// Provides deterministic, longitude-rotation-equivariant, hemispherically symmetric
    /// circulation bands, segmented sea-level pressure ovals, rainfall-impact anomalies,
    /// 2D wind-advected continentality, 2D wind-advected mountain rain shadows, and
    /// coastal-deflected ocean dynamic height currents.
    /// </summary>
    public sealed class GlebaClimateSupplement
    {
        const float RadToDeg = 57.29578f;

        public readonly int Width;
        public readonly int Height;
        public readonly GlebaClimateConfig Config;

        // Static geographic fields [Width * Height]
        public readonly float[] LandFraction;
        public readonly float[] OceanFraction;
        public readonly float[] MeanElevation;
        public readonly float[] PeakElevation;
        public readonly float[] DistanceToSeaKm;
        public readonly float[] DistanceToLandKm;
        public readonly float[] DistanceToDeepSeaKm;
        public readonly float[] TrueContinentality;

        // Column-wide meridional landmass balance [Width]
        public readonly float[] SaldoNorth;
        public readonly float[] SaldoSouth;

        // Monthly band latitudes in radians [Width * 12], indexed as (x * 12 + month)
        public readonly float[] ItczLatRad;
        public readonly float[] SthzNorthLatRad;
        public readonly float[] SthzSouthLatRad;
        public readonly float[] PolarNorthLatRad;
        public readonly float[] PolarSouthLatRad;

        // Monthly 2D fields [Width * Height * 12], indexed as (cell * 12 + month)
        public readonly float[] PressureHpa;
        public readonly float[] RainfallImpact;
        public readonly float[] WindEast;
        public readonly float[] WindNorth;
        public readonly GlebaShoreType[] ShoreTypes;
        public readonly float[] OceanDynamicHeight;
        public readonly float[] CurrentEast;
        public readonly float[] CurrentNorth;
        public readonly float[] WindContinentality;
        public readonly float[] MappedContinentality;
        public readonly float[] RainShadowiness;
        public readonly float[] EffectiveLatitudeDeg;

        GlebaClimateSupplement(int width, int height, GlebaClimateConfig config)
        {
            Width = width;
            Height = height;
            Config = config ?? GlebaClimateConfig.Default;

            int count = width * height;
            LandFraction = new float[count];
            OceanFraction = new float[count];
            MeanElevation = new float[count];
            PeakElevation = new float[count];
            TrueContinentality = new float[count];

            ItczLatRad = new float[width * 12];
            SthzNorthLatRad = new float[width * 12];
            SthzSouthLatRad = new float[width * 12];
            PolarNorthLatRad = new float[width * 12];
            PolarSouthLatRad = new float[width * 12];

            int count12 = count * 12;
            PressureHpa = new float[count12];
            RainfallImpact = new float[count12];
            WindEast = new float[count12];
            WindNorth = new float[count12];
            ShoreTypes = new GlebaShoreType[count12];
            OceanDynamicHeight = new float[count12];
            CurrentEast = new float[count12];
            CurrentNorth = new float[count12];
            WindContinentality = new float[count12];
            MappedContinentality = new float[count12];
            RainShadowiness = new float[count12];
            EffectiveLatitudeDeg = new float[count12];

            DistanceToSeaKm = new float[count];
            DistanceToLandKm = new float[count];
            DistanceToDeepSeaKm = new float[count];
            SaldoNorth = new float[width];
            SaldoSouth = new float[width];
        }

        public static GlebaClimateSupplement Build(
            WorldData world, CancellationToken cancel = default, int workers = 0,
            int maxColumns = 128, GlebaClimateConfig config = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            int width = Math.Min(maxColumns, world.Width);
            int height = Math.Max(4, width / 2);
            var cfg = config ?? GlebaClimateConfig.Default;
            var result = new GlebaClimateSupplement(width, height, cfg);
            var options = FlatWorkerPolicy.Options(cancel, workers);

            // Equal-area spherical cell overlap integration (matching FlatMoisture / FlatOceanCirculation)
            var sourceLatitude = new double[world.Height + 1];
            var targetLatitude = new double[height + 1];
            for (int y = 0; y <= world.Height; y++) sourceLatitude[y] = Math.Sin(((double)y / world.Height - 0.5) * Math.PI);
            for (int y = 0; y <= height; y++) targetLatitude[y] = Math.Sin(((double)y / height - 0.5) * Math.PI);

            Parallel.For(0, height, options, y =>
            {
                cancel.ThrowIfCancellationRequested();
                int endY = ((y + 1) * world.Height + height - 1) / height;
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    int endX = ((x + 1) * world.Width + width - 1) / width;
                    double waterSum = 0, oceanSum = 0, elevSum = 0;
                    float maxElev = float.MinValue;

                    for (int sy = y * world.Height / height; sy < endY; sy++)
                    {
                        double rowWeight = (Math.Min(sourceLatitude[sy + 1], targetLatitude[y + 1]) - Math.Max(sourceLatitude[sy], targetLatitude[y]))
                            / (targetLatitude[y + 1] - targetLatitude[y]);
                        if (rowWeight <= 0) continue;

                        for (int sx = x * world.Width / width; sx < endX; sx++)
                        {
                            int overlap = Math.Min((sx + 1) * width, (x + 1) * world.Width) - Math.Max(sx * width, x * world.Width);
                            if (overlap <= 0) continue;
                            double weight = rowWeight * overlap / world.Width;
                            int tile = sy * world.Width + sx;
                            float surf = FlatClimate.SurfaceHeight(world, tile);
                            elevSum += weight * surf;
                            if (surf > maxElev) maxElev = surf;
                            if (FlatTerrainAnalysis.AreaWater(world.Water[tile])) waterSum += weight;
                            if (FlatTerrainAnalysis.Ocean(world.Water[tile])) oceanSum += weight;
                        }
                    }

                    float water = FMath.Clamp((float)waterSum, 0f, 1f);
                    result.OceanFraction[i] = FMath.Clamp((float)oceanSum, 0f, water);
                    result.LandFraction[i] = 1f - water;
                    result.MeanElevation[i] = (float)elevSum;
                    result.PeakElevation[i] = maxElev == float.MinValue ? (float)elevSum : maxElev;
                }
            });

            // Distances & Saldo
            GlebaHelmholtzSolver.CalculateDistances(
                result.LandFraction, width, height,
                out float[] distSea, out float[] distLand, out float[] distDeep,
                GlebaHelmholtzSolver.EarthRadiusKm, sphericalMetric: true);
            Array.Copy(distSea, result.DistanceToSeaKm, distSea.Length);
            Array.Copy(distLand, result.DistanceToLandKm, distLand.Length);
            Array.Copy(distDeep, result.DistanceToDeepSeaKm, distDeep.Length);

            GlebaHelmholtzSolver.CalculateSaldo(
                result.LandFraction, width, height, out float[] sNorth, out float[] sSouth);
            Array.Copy(sNorth, result.SaldoNorth, width);
            Array.Copy(sSouth, result.SaldoSouth, width);

            // Initialize master circulation bands once
            var masterBands = new GlebaCirculationBands(width, height, cfg);
            masterBands.InitializeBands(result.LandFraction, result.PeakElevation, result.SaldoNorth, result.SaldoSouth);

            // Solve all 12 calendar months independently & deterministically in parallel
            int count = width * height;
            Parallel.For(0, 12, options, month =>
            {
                cancel.ThrowIfCancellationRequested();
                var monthBands = new GlebaCirculationBands(width, height, cfg);
                CopyBaseBands(masterBands, monthBands);

                // In ORBIS: month = 0 is January (t = 0/12), month = 6 is July (t = 6/12 = 0.5)
                float timeOfYear = month / 12f;
                monthBands.TickSeason(timeOfYear, result.LandFraction, result.DistanceToLandKm);

                for (int x = 0; x < width; x++)
                {
                    int bx = x * 12 + month;
                    result.ItczLatRad[bx] = monthBands.ItczLatRad[x];
                    result.SthzNorthLatRad[bx] = monthBands.SthzNorthLatRad[x];
                    result.SthzSouthLatRad[bx] = monthBands.SthzSouthLatRad[x];
                    result.PolarNorthLatRad[bx] = monthBands.PolarNorthLatRad[x];
                    result.PolarSouthLatRad[bx] = monthBands.PolarSouthLatRad[x];
                }

                GlebaHelmholtzSolver.ComputeSeasonalContinentalityAndShadow(
                    width, height, result.LandFraction, result.PeakElevation,
                    monthBands.WindEast, monthBands.WindNorth, cfg,
                    out float[] advCont, out float[] shadow, sphericalCosScale: true);

                for (int y = 0; y < height; y++)
                {
                    float latRad = (((y + 0.5f) / height) - 0.5f) * 3.1415927f;
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = row + x;
                        int mIdx = idx * 12 + month;

                        result.PressureHpa[mIdx] = monthBands.PressureHpa[idx];
                        result.RainfallImpact[mIdx] = monthBands.RainfallImpact[idx];
                        result.WindEast[mIdx] = monthBands.WindEast[idx];
                        result.WindNorth[mIdx] = monthBands.WindNorth[idx];
                        result.ShoreTypes[mIdx] = monthBands.ShoreTypes[idx];
                        result.OceanDynamicHeight[mIdx] = monthBands.OceanDynamicHeight[idx];
                        result.CurrentEast[mIdx] = monthBands.CurrentEast[idx];
                        result.CurrentNorth[mIdx] = monthBands.CurrentNorth[idx];

                        float c = advCont[idx];
                        result.WindContinentality[mIdx] = c;
                        result.MappedContinentality[mIdx] = GlebaClimateConfig.EvaluateCurve(cfg.ContinentalityFunction, c);
                        result.RainShadowiness[mIdx] = shadow[idx];
                        result.EffectiveLatitudeDeg[mIdx] = GlebaClimateConfig.MapEffectiveLatitudeDegrees(
                            latRad,
                            monthBands.PolarSouthLatRad[x],
                            monthBands.SthzSouthLatRad[x],
                            monthBands.ItczLatRad[x],
                            monthBands.SthzNorthLatRad[x],
                            monthBands.PolarNorthLatRad[x]);
                    }
                }
            });

            // Compute annual TrueContinentality & apply 5-point smoothing (matching post_process_tile_data)
            for (int i = 0; i < count; i++)
            {
                float sum = 0f;
                int baseIdx = i * 12;
                for (int m = 0; m < 12; m++) sum += result.WindContinentality[baseIdx + m];
                result.TrueContinentality[i] = sum / 12f;
            }
            GlebaHelmholtzSolver.BlurScalarField(result.TrueContinentality, width, height, 1, 1f);

            return result;
        }

        static void CopyBaseBands(GlebaCirculationBands src, GlebaCirculationBands dst)
        {
            int n = src.Width * sizeof(float);
            Buffer.BlockCopy(src.ItczBaseSummerRad, 0, dst.ItczBaseSummerRad, 0, n);
            Buffer.BlockCopy(src.ItczBaseWinterRad, 0, dst.ItczBaseWinterRad, 0, n);
            Buffer.BlockCopy(src.SthzNorthBaseSummerRad, 0, dst.SthzNorthBaseSummerRad, 0, n);
            Buffer.BlockCopy(src.SthzNorthBaseWinterRad, 0, dst.SthzNorthBaseWinterRad, 0, n);
            Buffer.BlockCopy(src.SthzSouthBaseSummerRad, 0, dst.SthzSouthBaseSummerRad, 0, n);
            Buffer.BlockCopy(src.SthzSouthBaseWinterRad, 0, dst.SthzSouthBaseWinterRad, 0, n);
            Buffer.BlockCopy(src.PolarNorthBaseSummerRad, 0, dst.PolarNorthBaseSummerRad, 0, n);
            Buffer.BlockCopy(src.PolarNorthBaseWinterRad, 0, dst.PolarNorthBaseWinterRad, 0, n);
            Buffer.BlockCopy(src.PolarSouthBaseSummerRad, 0, dst.PolarSouthBaseSummerRad, 0, n);
            Buffer.BlockCopy(src.PolarSouthBaseWinterRad, 0, dst.PolarSouthBaseWinterRad, 0, n);
        }

        /// <summary>
        /// Bilinearly samples a 12-month [Width * Height * 12] field at normalized map coordinates u, v in [0, 1].
        /// </summary>
        public float SampleMonthly(float[] field12, float u, float v, int month)
        {
            float px = u * Width - 0.5f;
            float py = FMath.Clamp(v * Height - 0.5f, 0f, Height - 1f);
            int x = (int)Math.Floor(px);
            int y = (int)py;
            float tx = px - x;
            float ty = py - y;
            int x0 = FMath.Wrap(x, Width);
            int x1 = x0 + 1 == Width ? 0 : x0 + 1;
            int y1 = Math.Min(y + 1, Height - 1);

            int a = (y * Width + x0) * 12 + month;
            int b = (y * Width + x1) * 12 + month;
            int c = (y1 * Width + x0) * 12 + month;
            int d = (y1 * Width + x1) * 12 + month;
            return FMath.Lerp(FMath.Lerp(field12[a], field12[b], tx), FMath.Lerp(field12[c], field12[d], tx), ty);
        }

        /// <summary>
        /// Bilinearly samples a static [Width * Height] field at normalized map coordinates u, v in [0, 1].
        /// </summary>
        public float SampleStatic(float[] field, float u, float v)
        {
            float px = u * Width - 0.5f;
            float py = FMath.Clamp(v * Height - 0.5f, 0f, Height - 1f);
            int x = (int)Math.Floor(px);
            int y = (int)py;
            float tx = px - x;
            float ty = py - y;
            int x0 = FMath.Wrap(x, Width);
            int x1 = x0 + 1 == Width ? 0 : x0 + 1;
            int y1 = Math.Min(y + 1, Height - 1);

            return FMath.Lerp(
                FMath.Lerp(field[y * Width + x0], field[y * Width + x1], tx),
                FMath.Lerp(field[y1 * Width + x0], field[y1 * Width + x1], tx), ty);
        }

        /// <summary>
        /// Linearly samples a 12-month longitudinal band [Width * 12] in degrees at normalized longitude u in [0, 1].
        /// </summary>
        public float SampleBandDegrees(float[] bandRad12, float u, int month)
        {
            float px = u * Width - 0.5f;
            int x = (int)Math.Floor(px);
            float tx = px - x;
            int x0 = FMath.Wrap(x, Width);
            int x1 = x0 + 1 == Width ? 0 : x0 + 1;
            return FMath.Lerp(bandRad12[x0 * 12 + month], bandRad12[x1 * 12 + month], tx) * RadToDeg;
        }
    }
}
