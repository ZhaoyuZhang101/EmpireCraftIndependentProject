using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    // Redistribute each coarse column's existing rain over an overlapping terrain
    // footprint. A column gives up exactly its precipitated water volume each month.
    internal static class FlatOrographicRainfall
    {
        const double RadiusKm = 6371.0088;
        const double ExposureLengthKm = 160;
        const double CloudExtentKm = 120;

        internal static void Distribute(WorldData world, FlatMoistureState air, ParallelOptions options)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            if (air.Width == world.Width && air.Height == world.Height)
            {
                Array.Copy(air.Rain, world.Precipitation, air.Rain.Length);
                return;
            }

            var columns = new Footprints(world.Width, air.Width, false);
            var rows = new Footprints(world.Height, air.Height, true);
            var cloudRain = CloudFootprint(air, rows.CoarseArea);
            var bed = new short[world.Count];
            for (int i = 0; i < bed.Length; i++)
                bed[i] = (short)FlatClimate.SurfaceHeight(world, i);
            double southPole = 0, northPole = 0;
            for (int x = 0; x < world.Width; x++)
            {
                southPole += bed[x];
                northPole += bed[world.Count - world.Width + x];
            }
            southPole /= world.Width;
            northPole /= world.Width;

            int stride = air.Width * 12;
            var flow = new IncidentFlow[air.Rain.Length];
            for (int i = 0; i < flow.Length; i++)
                flow[i] = new IncidentFlow(air.East[i], air.North[i], air.Humidity[i]);
            // Use the output for the transient preference factors. Only longitude
            // reductions are retained per fine row, including on the 4096 map.
            var rowSums = new double[world.Height * stride];
            Parallel.For(0, world.Height, options, y =>
            {
                var rowFlow = new IncidentFlow[stride];
                foreach (var tap in rows.Fine[y])
                    for (int k = 0; k < stride; k++)
                        rowFlow[k].Add(flow[tap.Index * stride + k], tap.Weight);

                double latitude = ((y + .5) / world.Height - .5) * Math.PI;
                var west = new SurfaceTap(world, latitude, -1, 0, southPole, northPole);
                var east = new SurfaceTap(world, latitude, 1, 0, southPole, northPole);
                var south = new SurfaceTap(world, latitude, 0, -1, southPole, northPole);
                var north = new SurfaceTap(world, latitude, 0, 1, southPole, northPole);
                for (int x = 0; x < world.Width; x++)
                {
                    int i = y * world.Width + x, offset = i * 12;
                    float z = bed[i];
                    float fromWest = z - west.Read(bed, x, world.Width), fromEast = z - east.Read(bed, x, world.Width);
                    float fromSouth = z - south.Read(bed, x, world.Width), fromNorth = z - north.Read(bed, x, world.Width);
                    // Low cloud contains less condensable water above the lifting
                    // condensation level. Deep and upper-level precipitation also
                    // crosses a mountain range without following its local slopes.
                    float columnLoss = Math.Max(0, z - 600) / 3500;
                    var taps = columns.Fine[x];
                    if (fromWest == 0 && fromEast == 0 && fromSouth == 0 && fromNorth == 0)
                    {
                        float preference = .7f + .3f * (float)Math.Exp(-columnLoss);
                        for (int m = 0; m < 12; m++) world.Precipitation[offset + m] = preference;
                    }
                    else
                    {
                        for (int m = 0; m < 12; m++)
                        {
                            float ascent = 0;
                            foreach (var tap in taps)
                                ascent += tap.Weight * rowFlow[tap.Index * 12 + m].Rise(fromWest, fromEast, fromSouth, fromNorth);
                            world.Precipitation[offset + m] = .7f + .3f * (float)Math.Exp(FMath.Clamp(ascent / 1500 - columnLoss, -2, 2));
                        }
                    }
                    foreach (var tap in taps)
                    {
                        int index = y * stride + tap.Index * 12;
                        for (int m = 0; m < 12; m++)
                            rowSums[index + m] += (double)tap.Weight * world.Precipitation[offset + m];
                    }
                }
            });

            var normalizedRain = new float[air.Rain.Length];
            Parallel.For(0, air.Height, options, cy =>
            {
                double sourceArea = rows.CoarseArea[cy] / air.Width;
                var totals = new double[stride];
                foreach (var tap in rows.Coarse[cy])
                {
                    double area = tap.Weight * rows.FineArea[tap.Index] / world.Width;
                    for (int k = 0; k < stride; k++) totals[k] += area * rowSums[tap.Index * stride + k];
                }
                for (int k = 0; k < stride; k++)
                {
                    int index = cy * stride + k;
                    normalizedRain[index] = (float)(cloudRain[index] * sourceArea / totals[k]);
                }
            });

            Parallel.For(0, world.Height, options, y =>
            {
                var rowRain = new float[stride];
                foreach (var tap in rows.Fine[y])
                    for (int k = 0; k < stride; k++)
                        rowRain[k] += tap.Weight * normalizedRain[tap.Index * stride + k];

                for (int x = 0; x < world.Width; x++)
                {
                    int offset = (y * world.Width + x) * 12;
                    var taps = columns.Fine[x];
                    for (int m = 0; m < 12; m++)
                    {
                        float rain = 0;
                        foreach (var tap in taps) rain += tap.Weight * rowRain[tap.Index * 12 + m];
                        world.Precipitation[offset + m] *= rain;
                    }
                }
            });
        }

        // A monthly condensate column covers part of the surrounding mesoscale
        // cloud deck. Split its footprint symmetrically over a ground distance.
        // Longitude is periodic, and both sides of a latitude interface exchange
        // equal areas, so a uniform flux and each donor's volume are preserved.
        static float[] CloudFootprint(FlatMoistureState air, double[] rowArea)
        {
            int width = air.Width, height = air.Height;
            double cellKm = Math.PI * RadiusKm / height;
            var zonal = new float[air.Rain.Length];
            for (int y = 0; y < height; y++)
            {
                double latitude = ((y + .5) / height - .5) * Math.PI;
                double shift = CloudExtentKm / (cellKm * Math.Cos(latitude));
                int whole = (int)shift;
                float fraction = (float)(shift - whole);
                for (int x = 0; x < width; x++)
                {
                    int center = (y * width + x) * 12;
                    int west = (y * width + FMath.Wrap(x - whole, width)) * 12;
                    int east = (y * width + FMath.Wrap(x + whole, width)) * 12;
                    int farWest = (y * width + FMath.Wrap(x - whole - 1, width)) * 12;
                    int farEast = (y * width + FMath.Wrap(x + whole + 1, width)) * 12;
                    for (int m = 0; m < 12; m++)
                        zonal[center+m] = .5f * air.Rain[center+m] + .25f *
                            (FMath.Lerp(air.Rain[west+m], air.Rain[farWest+m], fraction)
                            + FMath.Lerp(air.Rain[east+m], air.Rain[farEast+m], fraction));
                }
            }
            var result = new float[zonal.Length];
            int stride = width * 12;
            for (int y = 0; y < height; y++)
            {
                int south = Math.Max(0, y-1), north = Math.Min(height-1, y+1);
                double eachSide = .25 * CloudExtentKm / cellKm;
                double toSouth = south == y ? 0 : eachSide * Math.Min(1, rowArea[south]/rowArea[y]);
                double toNorth = north == y ? 0 : eachSide * Math.Min(1, rowArea[north]/rowArea[y]);
                for (int k = 0; k < stride; k++)
                    result[y*stride+k] = (float)((1 - toSouth - toNorth) * zonal[y*stride+k]
                        + toSouth * zonal[south*stride+k] + toNorth * zonal[north*stride+k]);
            }
            return result;
        }

        struct IncidentFlow
        {
            float fromWest, fromEast, fromSouth, fromNorth;

            internal IncidentFlow(float east, float north, float humidity)
            {
                // Air is lifted as it approaches the ridge, and continues to
                // condense while ascending its face. Descending air dries the lee.
                fromWest = humidity * (.7f * Math.Max(0, east) - .3f * Math.Max(0, -east));
                fromEast = humidity * (.7f * Math.Max(0, -east) - .3f * Math.Max(0, east));
                fromSouth = humidity * (.7f * Math.Max(0, north) - .3f * Math.Max(0, -north));
                fromNorth = humidity * (.7f * Math.Max(0, -north) - .3f * Math.Max(0, north));
            }

            internal void Add(IncidentFlow flow, float weight)
            {
                fromWest += weight * flow.fromWest;
                fromEast += weight * flow.fromEast;
                fromSouth += weight * flow.fromSouth;
                fromNorth += weight * flow.fromNorth;
            }

            internal float Rise(float west, float east, float south, float north) =>
                fromWest * west + fromEast * east + fromSouth * south + fromNorth * north;
        }

        readonly struct SurfaceTap
        {
            readonly int offset, lower, upper;
            readonly float dx, dy, polarRing, poleHeight;

            // Follow a fixed-distance great circle. The last DEM row is a ring
            // of samples around the pole; all meridians share a single polar value.
            internal SurfaceTap(WorldData world, double latitude, int east, int north, double southPole, double northPole)
            {
                double angle = ExposureLengthKm / RadiusKm, along = Math.Cos(angle), across = Math.Sin(angle);
                double sin = Math.Sin(latitude), cos = Math.Cos(latitude);
                double x = east * across, y = sin * along + cos * north * across;
                double z = cos * along - sin * north * across;
                double column = Math.Atan2(x,z) * world.Width / (2 * Math.PI);
                double v = Math.Atan2(y, Math.Sqrt(x*x + z*z)) / Math.PI + .5;
                double row = Math.Max(0, Math.Min(world.Height - 1, v * world.Height - .5));
                int left = (int)Math.Floor(column), bottom = (int)row;
                dx = (float)(column - left); dy = (float)(row - bottom);
                offset = FMath.Wrap(left, world.Width);
                lower = bottom * world.Width; upper = Math.Min(world.Height - 1, bottom + 1) * world.Width;
                polarRing = (float)FlatSurfaceSampling.PolarRingWeight(v, world.Height);
                poleHeight = (float)(v < .5 ? southPole : northPole);
            }

            internal float Read(short[] bed, int x, int width)
            {
                int a = x + offset;
                if (a >= width) a -= width;
                int b = a + 1 == width ? 0 : a + 1;
                float low = FMath.Lerp(bed[lower + a], bed[lower + b], dx);
                float high = FMath.Lerp(bed[upper + a], bed[upper + b], dx);
                return FMath.Lerp(poleHeight, FMath.Lerp(low, high, dy), polarRing);
            }
        }

        readonly struct Tap
        {
            internal readonly int Index;
            internal readonly float Weight;
            internal Tap(int index, float weight) { Index = index; Weight = weight; }
        }

        // Replace each coarse-cell edge with a linear transition centred on the
        // edge in longitude or sin(latitude). The area moved across it is equal in
        // both directions. Integrating the ramp within each output tile preserves
        // constants and volumes even when coarse and fine edges do not coincide.
        sealed class Footprints
        {
            internal readonly Tap[][] Fine, Coarse;
            internal readonly double[] FineArea, CoarseArea;

            internal Footprints(int count, int coarse, bool latitude)
            {
                double Position(int i, int n) => latitude ? Math.Sin(Math.PI * ((double)i/n - .5)) : (double)i/n;
                var starts = new double[coarse + 1];
                var radii = new double[coarse + 1];
                var inverse = new List<Tap>[coarse];
                CoarseArea = new double[coarse]; FineArea = new double[count];
                Fine = new Tap[count][]; Coarse = new Tap[coarse][];
                for (int j = 0; j <= coarse; j++) starts[j] = Position(j, coarse);
                for (int j = 0; j < coarse; j++)
                {
                    CoarseArea[j] = starts[j+1] - starts[j];
                    inverse[j] = new List<Tap>();
                }
                for (int j = 1; j < coarse; j++) radii[j] = .5 * Math.Min(CoarseArea[j-1], CoarseArea[j]);
                radii[0] = radii[coarse] = latitude ? 0 : .5/coarse;

                for (int i = 0; i < count; i++)
                {
                    double low = Position(i, count), high = Position(i+1, count);
                    FineArea[i] = high - low;
                    var taps = new List<Tap>(3);
                    int first = i * coarse / count - 1, last = ((i+1) * coarse + count-1) / count;
                    for (int j = first; j <= last; j++)
                    {
                        if (latitude && (j < 0 || j >= coarse)) continue;
                        int cell = FMath.Wrap(j, coarse);
                        double wrap = latitude ? 0 : (j - cell) / coarse;
                        double l = starts[cell] + wrap, r = starts[cell+1] + wrap;
                        double weight = (RampMean(low-l, high-l, radii[cell]) - RampMean(low-r, high-r, radii[cell+1]));
                        if (weight <= 1e-12) continue;
                        taps.Add(new Tap(cell, (float)weight));
                        inverse[cell].Add(new Tap(i, (float)weight));
                    }
                    Fine[i] = taps.ToArray();
                }
                for (int j = 0; j < coarse; j++) Coarse[j] = inverse[j].ToArray();
            }

            static double RampMean(double low, double high, double radius)
            {
                if (low >= radius) return 1;
                if (high <= -radius) return 0;
                double Primitive(double x) => x <= -radius ? 0 : x >= radius ? x : (x + radius) * (x + radius) / (4 * radius);
                return (Primitive(high) - Primitive(low)) / (high - low);
            }
        }
    }
}
