using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    /// <summary>
    /// Gleba v0.3.6 spatial grid operators: 8-neighbor chamfer BFS distances (pop-time visited),
    /// hemispheric meridional saldo, Jacobi spherical blur, and semi-Lagrangian Helmholtz
    /// advection with parallel unit-mass normalization (04 & 05 + gleba_utils::helmholtz).
    /// Supports both square Gleba N x N grids and ORBIS width x height equirectangular grids.
    /// </summary>
    public static class GlebaHelmholtzSolver
    {
        public const float EarthRadiusKm = 6371.0f;

        /// <summary>
        /// Computes DistanceToSea (waterFrac > 0), DistanceToLand (landFrac > 0), and
        /// DistanceToDeepSea (waterFrac > 0.75) in km using Gleba's pop-time visited 8-neighbor chamfer BFS.
        /// When sphericalMetric is true, east-west step lengths scale by cos(latitude) for longitude-invariant accuracy.
        /// </summary>
        public static void CalculateDistances(
            float[] landFraction, int width, int height,
            out float[] distanceToSeaKm, out float[] distanceToLandKm, out float[] distanceToDeepSeaKm,
            float planetRadiusKm = EarthRadiusKm, bool sphericalMetric = false)
        {
            int count = width * height;
            distanceToSeaKm = new float[count];
            distanceToLandKm = new float[count];
            distanceToDeepSeaKm = new float[count];

            var visSea = new bool[count];
            var visLand = new bool[count];
            var visDeep = new bool[count];
            var qSea = new Queue<int>(count * 2);
            var qLand = new Queue<int>(count * 2);
            var qDeep = new Queue<int>(count * 2);

            for (int i = 0; i < count; i++)
            {
                float land = FMath.Clamp(landFraction[i], 0f, 1f);
                float water = 1f - land;
                if (land < 1f)
                {
                    visSea[i] = true;
                    Enqueue8(qSea, i, width, height);
                }
                if (land > 0f)
                {
                    visLand[i] = true;
                    Enqueue8(qLand, i, width, height);
                }
                if (water > 0.75f)
                {
                    visDeep[i] = true;
                    Enqueue8(qDeep, i, width, height);
                }
            }

            RunChamferBfs(qSea, visSea, distanceToSeaKm, width, height, planetRadiusKm, sphericalMetric);
            RunChamferBfs(qLand, visLand, distanceToLandKm, width, height, planetRadiusKm, sphericalMetric);
            RunChamferBfs(qDeep, visDeep, distanceToDeepSeaKm, width, height, planetRadiusKm, sphericalMetric);
        }

        static void Enqueue8(Queue<int> queue, int idx, int width, int height)
        {
            int cx = idx % width;
            int cy = idx / width;
            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = cy + dy;
                if (ny < 0 || ny >= height) continue;
                int row = ny * width;
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx;
                    if (nx < 0) nx += width;
                    else if (nx >= width) nx -= width;
                    queue.Enqueue(row + nx);
                }
            }
        }

        static void RunChamferBfs(
            Queue<int> queue, bool[] visited, float[] dist,
            int width, int height, float planetRadiusKm, bool sphericalMetric)
        {
            if (queue.Count == 0) return;

            double meridionalStepKm = sphericalMetric
                ? (Math.PI * planetRadiusKm / height)
                : (planetRadiusKm * 6.2831855) / width;
            double equatorialZonalStepKm = (planetRadiusKm * 6.2831855) / width;

            var distD = new double[dist.Length];
            var rowZonalKm = new double[height];
            var rowDiagKm = new double[height];
            for (int y = 0; y < height; y++)
            {
                if (sphericalMetric)
                {
                    double lat = ((y + 0.5) / height - 0.5) * Math.PI;
                    double dxKm = Math.Max(0.08, Math.Cos(lat)) * equatorialZonalStepKm;
                    rowZonalKm[y] = dxKm;
                    rowDiagKm[y] = Math.Sqrt(dxKm * dxKm + meridionalStepKm * meridionalStepKm);
                }
                else
                {
                    rowZonalKm[y] = meridionalStepKm;
                    rowDiagKm[y] = meridionalStepKm * 1.4142135623730951;
                }
            }

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                if (visited[curr]) continue;

                int cx = curr % width;
                int cy = curr / width;
                double best = double.PositiveInfinity;
                double zonalKm = rowZonalKm[cy];
                double diagKm = rowDiagKm[cy];

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if (ny < 0 || ny >= height) continue;
                    int row = ny * width;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx;
                        if (nx < 0) nx += width;
                        else if (nx >= width) nx -= width;
                        int nbr = row + nx;
                        if (!visited[nbr]) continue;

                        double step = dx == 0 ? meridionalStepKm : (dy == 0 ? zonalKm : diagKm);
                        double cand = distD[nbr] + step;
                        if (cand < best) best = cand;
                    }
                }

                visited[curr] = true;
                distD[curr] = best;
                dist[curr] = (float)best;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = cy + dy;
                    if (ny < 0 || ny >= height) continue;
                    int row = ny * width;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx;
                        if (nx < 0) nx += width;
                        else if (nx >= width) nx -= width;
                        int nbr = row + nx;
                        if (!visited[nbr]) queue.Enqueue(nbr);
                    }
                }
            }
        }

        /// <summary>
        /// Computes Gleba's per-column meridional landmass balance (calculate_saldo, 0x180a28600):
        /// integrates |sin(lat)| * landFrac / height separately over NH (lat > 0) and SH (lat < 0).
        /// </summary>
        public static void CalculateSaldo(
            float[] landFraction, int width, int height, out float[] saldoNorth, out float[] saldoSouth)
        {
            saldoNorth = new float[width];
            saldoSouth = new float[width];
            float invH = 1f / height;

            for (int y = 0; y < height; y++)
            {
                float lat = (((y + 0.5f) * invH) - 0.5f) * 3.1415927f;
                float weight = Math.Abs((float)Math.Sin(lat)) * invH;
                if (weight <= 0f) continue;
                int row = y * width;
                if (lat > 0f)
                {
                    for (int x = 0; x < width; x++)
                        saldoNorth[x] += weight * FMath.Clamp(landFraction[row + x], 0f, 1f);
                }
                else if (lat < 0f)
                {
                    for (int x = 0; x < width; x++)
                        saldoSouth[x] += weight * FMath.Clamp(landFraction[row + x], 0f, 1f);
                }
            }
        }

        /// <summary>
        /// Periodic-longitude, pole-clamped Jacobi blur matching Gleba's 5-point blur operator.
        /// </summary>
        public static void BlurScalarField(float[] field, int width, int height, int iterations, float strength = 0.5f)
        {
            if (iterations <= 0 || strength <= 0f) return;
            var scratch = new float[field.Length];
            float blend = FMath.Clamp(strength, 0f, 1f);

            for (int iter = 0; iter < iterations; iter++)
            {
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    int south = Math.Max(0, y - 1) * width;
                    int north = Math.Min(height - 1, y + 1) * width;
                    for (int x = 0; x < width; x++)
                    {
                        int xl = x == 0 ? width - 1 : x - 1;
                        int xr = x + 1 == width ? 0 : x + 1;
                        float avg5 = 0.2f * (field[row + xl] + field[row + xr] + field[south + x] + field[north + x] + field[row + x]);
                        scratch[row + x] = FMath.Lerp(field[row + x], avg5, blend);
                    }
                }
                Buffer.BlockCopy(scratch, 0, field, 0, field.Length * sizeof(float));
            }
        }

        /// <summary>
        /// Single semi-Lagrangian backward trace step (gleba_utils::helmholtz::advect, grid_mode=2).
        /// Uses floor-based positive fractional weights so dateline wrapping at x = 0 is 100% seam-free.
        /// When sphericalCosScale is true, zonal step in grid columns scales by 1 / max(0.18, cos(lat)).
        /// </summary>
        public static void AdvectStep(
            float dt, int width, int height, float[] velEast, float[] velNorth,
            float[] dst, float[] src, bool sphericalCosScale = false)
        {
            float dtY = dt * 0.01f * height;
            float dtXBase = dt * 0.01f * width;

            for (int y = 1; y < height - 1; y++)
            {
                float dtX = dtXBase;
                if (sphericalCosScale)
                {
                    double lat = ((y + 0.5) / height - 0.5) * Math.PI;
                    // On a 2:1 equirectangular grid, 1 degree of lat == 1 degree of lon at the equator,
                    // so physical ground speed scales dx by (width / (2 * height)) / cos(lat).
                    float aspect = width / (2f * height);
                    dtX = dtY * aspect / Math.Max(0.18f, (float)Math.Cos(lat));
                }

                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = row + x;
                    double px = x - (double)velEast[idx] * dtX;
                    double py = y - (double)velNorth[idx] * dtY;

                    int i0 = (int)Math.Floor(px);
                    int j0 = (int)Math.Floor(py);
                    float s1 = (float)(px - i0);
                    float s0 = 1f - s1;
                    float t1 = (float)(py - j0);
                    float t0 = 1f - t1;

                    int ix0 = FMath.Wrap(i0, width);
                    int ix1 = ix0 + 1 == width ? 0 : ix0 + 1;
                    int jy0 = FMath.Clamp(j0, 0, height - 1) * width;
                    int jy1 = FMath.Clamp(j0 + 1, 0, height - 1) * width;

                    dst[idx] = s0 * (t0 * src[jy0 + ix0] + t1 * src[jy1 + ix0])
                             + s1 * (t0 * src[jy0 + ix1] + t1 * src[jy1 + ix1]);
                }
            }

            // Polar boundary condition (helmholtz::set_boundary, grid_mode=2)
            int lastRow = (height - 1) * width;
            int prevRow = (height - 2) * width;
            for (int x = 0; x < width; x++)
            {
                dst[x] = dst[width + x];
                dst[lastRow + x] = dst[prevRow + x];
            }
        }

        /// <summary>
        /// Runs multi-step semi-Lagrangian advection on field and normalizes by an identically advected
        /// unit field (matching Gleba's field[i] / ones[i] pattern in generate_climate).
        /// </summary>
        public static void AdvectNormalized(
            int width, int height, float[] velEast, float[] velNorth,
            float[] field, float dt, int iterations, float fallbackValue = 0f, bool sphericalCosScale = false)
        {
            int n = width * height;
            var srcField = new float[n];
            var ones = new float[n];
            var srcOnes = new float[n];
            for (int i = 0; i < n; i++)
            {
                srcField[i] = field[i];
                ones[i] = 1f;
                srcOnes[i] = 1f;
            }

            for (int iter = 0; iter < iterations; iter++)
            {
                AdvectStep(dt, width, height, velEast, velNorth, field, srcField, sphericalCosScale);
                AdvectStep(dt, width, height, velEast, velNorth, ones, srcOnes, sphericalCosScale);
                Buffer.BlockCopy(field, 0, srcField, 0, n * sizeof(float));
                Buffer.BlockCopy(ones, 0, srcOnes, 0, n * sizeof(float));
            }

            for (int i = 0; i < n; i++)
                field[i] = ones[i] > 1e-5f ? (field[i] / ones[i]) : fallbackValue;
        }

        /// <summary>
        /// Executes Gleba Pass 1 for a single month/season: computes 2D wind-advected continentality
        /// and 2D wind-advected mountain leeward rain-shadowiness.
        /// </summary>
        public static void ComputeSeasonalContinentalityAndShadow(
            int width, int height, float[] landFraction, float[] elevationMeters,
            float[] windEast, float[] windNorth, GlebaClimateConfig config,
            out float[] advectedContinentality, out float[] rainShadowiness, bool sphericalCosScale = true)
        {
            int n = width * height;
            advectedContinentality = new float[n];
            rainShadowiness = new float[n];

            float onset = config.MountainBarrierOnsetMeters;
            float full = Math.Max(onset + 1f, config.MountainBarrierFullMeters);

            for (int i = 0; i < n; i++)
            {
                advectedContinentality[i] = landFraction[i] > 0.5f ? 1f : 0f;
                rainShadowiness[i] = elevationMeters[i] > onset
                    ? Math.Min(elevationMeters[i] / full, 1f)
                    : 0f;
            }

            AdvectNormalized(width, height, windEast, windNorth, advectedContinentality,
                config.AdvectionDt, config.AdvectionIterations, 1f, sphericalCosScale);
            AdvectNormalized(width, height, windEast, windNorth, rainShadowiness,
                2f * config.AdvectionDt, config.RainShadowAdvectionIterations, 0f, sphericalCosScale);

            for (int i = 0; i < n; i++)
            {
                advectedContinentality[i] = FMath.Clamp(advectedContinentality[i], 0f, 1f);
                rainShadowiness[i] = FMath.Clamp(rainShadowiness[i], 0f, 1f);
            }
        }
    }
}
