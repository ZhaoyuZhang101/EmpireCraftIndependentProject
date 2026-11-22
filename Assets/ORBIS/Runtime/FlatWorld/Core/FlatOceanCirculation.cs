using System;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    // Annual upper-ocean gyres on the climate grid. SeaAnomaly and AirAnomaly
    // are degrees C relative to a motionless mixed layer at the same latitude.
    internal sealed class FlatOceanCirculation
    {
        const double RadiusM = 6371008.8;
        const double Rotation = 7.292115e-5;
        const double WaterDensity = 1025;
        const double CurrentDepthM = 180;
        const double RelaxationSeconds = 100 * 86400;
        internal readonly int Width, Height;
        internal readonly float[] SeaAnomaly, AirAnomaly, East, North; // velocities in m/s

        FlatOceanCirculation(int width, int height)
        {
            Width = width;
            Height = height;
            SeaAnomaly = new float[width * height];
            AirAnomaly = new float[width * height];
            East = new float[width * height];
            North = new float[width * height];
        }

        internal static FlatOceanCirculation Build(WorldData world, ParallelOptions options, float[] zonalWindByRow = null)
        {
            int width = Math.Min(128, world.Width), height = width / 2;
            var ocean = new float[width * height];
            var edges = LatitudeEdges(world.Height);
            var coarse = LatitudeEdges(height);
            Parallel.For(0, height, options, y =>
            {
                var wetArea = new double[width];
                int firstY = y * world.Height / height;
                int endY = ((y + 1) * world.Height + height - 1) / height;
                for (int sy = firstY; sy < endY; sy++)
                {
                    double dy = (Math.Min(edges[sy + 1], coarse[y + 1]) - Math.Max(edges[sy], coarse[y]))
                        / (coarse[y + 1] - coarse[y]);
                    for (int sx = 0; sx < world.Width; sx++)
                    {
                        if (!FlatTerrainAnalysis.Ocean(world.Water[sy * world.Width + sx])) continue;
                        int left = sx * width, right = left + width;
                        int x = left / world.Width;
                        int split = Math.Min(right, (x + 1) * world.Width);
                        wetArea[x] += dy * (split - left) / world.Width;
                        if (split < right) wetArea[x + 1] += dy * (right - split) / world.Width;
                    }
                }
                for (int x = 0; x < width; x++) ocean[y * width + x] = FMath.Clamp((float)wetArea[x], 0, 1);
            });
            return Build(ocean, width, height, options, zonalWindByRow);
        }

        static double[] LatitudeEdges(int height)
        {
            var edges = new double[height + 1];
            for (int y = 0; y <= height; y++) edges[y] = Math.Sin(((double)y / height - .5) * Math.PI);
            return edges;
        }

        internal static FlatOceanCirculation Build(float[] oceanFraction, int width, int height, ParallelOptions options,
            float[] zonalWindByRow = null)
        {
            if (width < 2 || height < 1 || width > 256 || height > 128 || oceanFraction.Length != width * height)
                throw new ArgumentException("Expected a reduced ocean climate grid with at most 256 by 128 cells.");
            if (zonalWindByRow != null && zonalWindByRow.Length != height)
                throw new ArgumentException("Expected one normalized annual zonal wind per latitude.");
            var field = new FlatOceanCirculation(width, height);
            var winds = new float[height];
            for (int y = 0; y < height; y++)
            {
                if (zonalWindByRow != null)
                {
                    winds[y] = zonalWindByRow[y];
                    if (!FMath.Finite(winds[y]) || Math.Abs(winds[y]) > 1)
                        throw new ArgumentOutOfRangeException(nameof(zonalWindByRow), "Expected finite normalized wind in [-1,1].");
                }
                else
                {
                    float lat = (float)((2 * y + 1 - height) * (90.0 / height));
                    // Opposite seasons contribute one pair. The same wind cycle
                    // shifted by six months must have exactly the same annual curl.
                    double annual = 0;
                    for (int m = 0; m < 6; m++)
                    {
                        float phase = (float)Math.Cos((m - 6) * Math.PI / 6);
                        annual += (double)FlatWindBelts.Zonal(lat, phase) + FlatWindBelts.Zonal(lat, -phase);
                    }
                    winds[y] = (float)(annual / 12);
                }
            }
            double dy = Math.PI * RadiusM / height;
            var psi = field.StreamFunction(oceanFraction, winds, options);
            field.TransportHeat(oceanFraction, winds, psi, dy, options);
            field.MarineAir(oceanFraction, winds, options);
            return field;
        }

        // Sverdrup interior flow balances the meridional gradient of wind
        // stress against beta. Exponential western boundary return closes each
        // wet zonal section (Stommel closure). psi is in m2/s per unit depth.
        // Zero on coastal corners gives identically zero flux through land.
        double[] StreamFunction(float[] ocean, float[] winds, ParallelOptions options)
        {
            var psi = new double[Width * (Height + 1)];
            var interior = new bool[psi.Length];
            Parallel.For(1, Height, options, y =>
            {
                int row = y * Width, previous = row - Width, wall = -1;
                for (int x = 0; x < Width; x++)
                {
                    int w = x == 0 ? Width - 1 : x - 1;
                    bool wet = ocean[row + x] >= .5f && ocean[row + w] >= .5f &&
                               ocean[previous + x] >= .5f && ocean[previous + w] >= .5f;
                    interior[row + x] = wet;
                    if (!wet) wall = x;
                }
                if (wall < 0) return; // open latitude, no western wall to close a gyre
                double lat = ((double)y / Height - .5) * Math.PI, cos = Math.Cos(lat);
                double dx = 2 * Math.PI * RadiusM * cos / Width;
                double beta = 2 * Rotation * Math.Max(.38, cos) / RadiusM;
                // Integrate the stress around a spherical strip. Its north
                // and south edges have different lengths, even for uniform wind.
                double halfStep = .5 * Math.PI / Height;
                double northEdge = Math.Cos(lat + halfStep), southEdge = Math.Cos(lat - halfStep);
                double stripAreaPerRadian = 2 * RadiusM * RadiusM * Math.Sin(halfStep) * cos;
                double stressGradient = .09 * RadiusM * (winds[y] * northEdge - winds[y - 1] * southEdge) / stripAreaPerRadian;
                double ice = FMath.Clamp((float)((80 - Math.Abs(lat * 180 / Math.PI)) / 20), 0, 1);
                double returning = stressGradient * ice / (WaterDensity * CurrentDepthM * beta);
                double boundaryM = Math.Max(520000, 1.5 * dx);
                int k = 1;
                while (k < Width)
                {
                    int start = k;
                    while (k < Width && interior[row + (wall + k) % Width]) k++;
                    int length = k - start + 1;
                    if (length > 1)
                    {
                        double span = length * dx;
                        double closing = 1 - Math.Exp(-span / boundaryM);
                        for (int j = 1; j < length; j++)
                        {
                            int x = (wall + start + j - 1) % Width;
                            double western = (1 - Math.Exp(-j * dx / boundaryM)) / closing;
                            psi[row + x] = returning * span * (western - (double)j / length);
                        }
                    }
                    k++;
                }
            });
            // A short meridional stencil smooths changes in basin width around
            // islands; coastal corners stay fixed. Staggered face differences
            // therefore still close around every cell.
            var smooth = new double[psi.Length];
            Parallel.For(1, Height, options, y =>
            {
                int row = y * Width;
                for (int i = row; i < row + Width; i++)
                    if (interior[i]) smooth[i] = .5 * psi[i] + .25 * (psi[i - Width] + psi[i + Width]);
            });
            return smooth;
        }

        struct Mixing
        {
            public double W, E, S, N, Restoring;
        }

        void TransportHeat(float[] ocean, float[] winds, double[] psi, double dy, ParallelOptions options)
        {
            int count = Width * Height;
            var weights = new Mixing[count];
            var equilibrium = new double[Height];
            var temperature = new double[count];
            var next = new double[count];
            var edges = LatitudeEdges(Height);
            for (int y = 0; y < Height; y++)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                int row = y * Width;
                double lat = ((y + .5) / Height - .5) * Math.PI;
                double dx = 2 * Math.PI * RadiusM * Math.Cos(lat) / Width;
                double area = 2 * Math.PI * RadiusM * RadiusM / Width * (edges[y + 1] - edges[y]);
                double restore = area / RelaxationSeconds;
                equilibrium[y] = Math.Max(-1.8, 28 - 31 * Math.Pow(Math.Sin(lat), 2));
                bool open = true;
                for (int x = 0; x < Width; x++) open &= ocean[row + x] >= .5f;
                double through = open ? .12 * winds[y] * dy : 0;
                for (int x = 0; x < Width; x++)
                {
                    int i = row + x, e = row + (x + 1) % Width;
                    temperature[i] = equilibrium[y];
                    if (ocean[i] < .5f) { weights[i].Restoring = 1; continue; }
                    double west = psi[i] - psi[i + Width] + through;
                    double east = psi[e] - psi[e + Width] + through;
                    double south = psi[e] - psi[i];
                    double north = psi[e + Width] - psi[i + Width];
                    East[i] = (float)((west + east) / (2 * dy));
                    North[i] = (float)((north + south) / (2 * dx));
                    var w = new Mixing { W = Math.Max(0, west), E = Math.Max(0, -east),
                        S = Math.Max(0, south), N = Math.Max(0, -north) };
                    double sum = restore + w.W + w.E + w.S + w.N;
                    w.W /= sum; w.E /= sum; w.S /= sum; w.N /= sum;
                    w.Restoring = restore / sum;
                    weights[i] = w;
                }
            }
            // Stationary upwind mixed-layer budget with a 100-day relaxation
            // to the local annual radiative equilibrium. Convex weights keep
            // SST inside the range of source temperatures at every iteration.
            for (int step = 0; step < 96; step++)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                double maximumChange = 0;
                for (int y = 0; y < Height; y++)
                {
                    int row = y * Width, south = Math.Max(0, y - 1) * Width, north = Math.Min(Height - 1, y + 1) * Width;
                    for (int x = 0; x < Width; x++)
                    {
                        int i = row + x;
                        var w = weights[i];
                        double t = w.Restoring * equilibrium[y]
                            + w.W * temperature[row + (x == 0 ? Width - 1 : x - 1)]
                            + w.E * temperature[row + (x + 1 == Width ? 0 : x + 1)]
                            + w.S * temperature[south + x] + w.N * temperature[north + x];
                        next[i] = t;
                        maximumChange = Math.Max(maximumChange, Math.Abs(t - temperature[i]));
                    }
                }
                var swap = temperature; temperature = next; next = swap;
                if (maximumChange < .00001) break;
            }
            for (int i = 0; i < count; i++) SeaAnomaly[i] = (float)(temperature[i] - equilibrium[i / Width]);
        }

        void MarineAir(float[] ocean, float[] winds, ParallelOptions options)
        {
            Parallel.For(0, Height, options, y =>
            {
                int row = y * Width;
                double lat = ((y + .5) / Height - .5) * Math.PI;
                double dx = 2 * Math.PI * RadiusM * Math.Cos(lat) / Width;
                double memory = Math.Exp(-dx / 650000), half = Math.Sqrt(memory);
                double closure = 1 - Math.Pow(memory, Width);
                // Maritime air crosses the coast along the prevailing wind;
                // leave some two-way exchange for transient winds and sea breezes.
                float eastwardWeight = FMath.Clamp(.5f + .4f * winds[y], .1f, .9f);
                for (int direction = 1; direction >= -1; direction -= 2)
                {
                    int start = direction > 0 ? 0 : Width - 1;
                    double value = 0;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int k = 0, x = start; k < Width; k++, x += direction)
                        {
                            int i = row + x;
                            double source = SeaAnomaly[i] * FMath.Clamp(ocean[i], 0, 1);
                            if (pass == 1) AirAnomaly[i] += (float)((half * value + (1 - half) * source) *
                                (direction > 0 ? eastwardWeight : 1 - eastwardWeight));
                            value = memory * value + (1 - memory) * source;
                        }
                        if (pass == 0) value /= closure;
                    }
                }
            });
        }

        internal float SampleAir(float u, float v)
        {
            float px = u * Width - .5f, py = FMath.Clamp(v * Height - .5f, 0, Height - 1);
            int x = (int)Math.Floor(px), y = (int)py;
            float tx = px - x, ty = py - y;
            int w = FMath.Wrap(x, Width), e = (w + 1) % Width;
            int row = y * Width, north = Math.Min(Height - 1, y + 1) * Width;
            return FMath.Lerp(FMath.Lerp(AirAnomaly[row + w], AirAnomaly[row + e], tx),
                FMath.Lerp(AirAnomaly[north + w], AirAnomaly[north + e], tx), ty);
        }
    }
}
