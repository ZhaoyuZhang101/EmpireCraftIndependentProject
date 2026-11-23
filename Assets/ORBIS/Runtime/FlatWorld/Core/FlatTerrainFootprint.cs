using System;

namespace Orbis.Flat
{
    // A 5x5 tangent footprint with one equatorial cell of ground spacing.
    // Rotational symmetry lets every longitude in a row reuse these interpolation weights.
    internal sealed class FlatTerrainFootprint
    {
        struct Tap
        {
            public int Column, LowerRow, UpperRow;
            public float EastWeight, NorthWeight, RingWeight, PoleContribution;
            public bool Cardinal;
        }

        readonly Tap[] taps = new Tap[24];
        readonly int width, rowStart;

        internal FlatTerrainFootprint(WorldData world, int row, double southBed, double northBed)
        {
            width = world.Width;
            rowStart = row * width;
            double step = 2 * Math.PI / width;
            double latitude = ((row + .5) / world.Height - .5) * Math.PI;
            double sinLat = Math.Sin(latitude), cosLat = Math.Cos(latitude);
            int index = 0;
            for (int north = -2; north <= 2; north++)
            for (int east = -2; east <= 2; east++)
            {
                if (east == 0 && north == 0) continue;
                double length = Math.Sqrt(east * east + north * north);
                double along = Math.Cos(step * length), across = Math.Sin(step * length) / length;
                double x = east * across, y = sinLat * along + cosLat * north * across;
                double z = cosLat * along - sinLat * north * across;
                double column = Math.Atan2(x, z) / step;
                double target = (Math.Atan2(y, Math.Sqrt(x*x + z*z)) / Math.PI + .5) * world.Height;
                double ring = FlatSurfaceSampling.PolarRingWeight(target / world.Height, world.Height);
                double address = Math.Max(0, Math.Min(world.Height - 1, target - .5));
                int lower = (int)address;
                int left = (int)Math.Floor(column);
                taps[index++] = new Tap {
                    Column = FMath.Wrap(left, width), EastWeight = (float)(column - left),
                    LowerRow = lower * width, UpperRow = Math.Min(world.Height - 1, lower + 1) * width,
                    NorthWeight = (float)(address - lower), RingWeight = (float)ring,
                    PoleContribution = (float)((1-ring) * (target < world.Height * .5 ? southBed : northBed)),
                    Cardinal = Math.Abs(east) + Math.Abs(north) == 1
                };
            }
        }

        internal void Measure(short[] bed, int x, short centre, out float relief, out float gradient,
                              out float landRelief, out float uphill)
        {
            // The immediate east/west samples lie inside one ground step. Keep
            // narrow cliffs that a farther geodesic tap could skip at high latitude.
            short west = bed[rowStart + (x == 0 ? width - 1 : x - 1)];
            short east = bed[rowStart + (x + 1 == width ? 0 : x + 1)];
            float low = Math.Min(centre, Math.Min(west, east));
            float high = Math.Max(centre, Math.Max(west, east));
            gradient = Math.Max(high - centre, centre - low);
            uphill = high - centre;
            foreach (var tap in taps)
            {
                int left = x + tap.Column;
                if (left >= width) left -= width;
                int right = left + 1 == width ? 0 : left + 1;
                int i = tap.LowerRow + left, j = tap.UpperRow + left;
                float lower = bed[i] + (bed[tap.LowerRow + right] - bed[i]) * tap.EastWeight;
                float upper = bed[j] + (bed[tap.UpperRow + right] - bed[j]) * tap.EastWeight;
                float value = (lower + (upper - lower) * tap.NorthWeight) * tap.RingWeight + tap.PoleContribution;
                low = Math.Min(low, value);
                high = Math.Max(high, value);
                if (tap.Cardinal)
                {
                    gradient = Math.Max(gradient, Math.Abs(value - centre));
                    uphill = Math.Max(uphill, value - centre);
                }
            }
            relief = high - low;
            landRelief = Math.Max(0, high) - Math.Max(0, low);
        }
    }
}
