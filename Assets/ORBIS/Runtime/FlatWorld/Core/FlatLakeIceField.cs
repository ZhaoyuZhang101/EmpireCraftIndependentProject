using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    // A 25-day exponential average of local air temperature estimates sustained
    // cooling at the lake surface. Store it over land as well: a surveyed lake
    // may lie entirely inside a dry simulation tile. Only the renderer knows
    // the final lake footprint and its reconstructed depth.
    public sealed class FlatLakeIceField
    {
        const int MaxWidth = 1024;
        const double MonthDuration = FlatClimateSamples.MonthDays / 25.0;
        static readonly double MonthRetention = Math.Exp(-MonthDuration);
        readonly uint seed;
        public int Width { get; }
        public int Height { get; }
        // Four months per RGBA panel, in geographical south-to-north order.
        public byte[] Packed { get; }

        internal FlatLakeIceField(WorldData world, CancellationToken cancel, int workers)
        {
            int stride = 1;
            while (world.Width / stride > MaxWidth) stride *= 2;
            Width = world.Width / stride;
            Height = world.Height / stride;
            Packed = new byte[Width * Height * 12];
            seed = FMath.Seed(world.Seed);
            double sampleWeight = 1.0 / (stride * stride);
            double annualRetention = Math.Pow(MonthRetention, 12);
            Parallel.For(0, Height, FlatWorkerPolicy.Options(cancel, workers), y =>
            {
                var monthly = new double[12];
                for (int x = 0; x < Width; x++)
                {
                    Array.Clear(monthly, 0, monthly.Length);
                    for (int dy = 0; dy < stride; dy++)
                    {
                        int start = ((y * stride + dy) * world.Width + x * stride) * 12;
                        for (int dx = 0; dx < stride; dx++, start += 12)
                            for (int m = 0; m < 12; m++) monthly[m] += world.Temperature[start + m];
                    }
                    // Solve the periodic boundary analytically: January 1st
                    // carries the preceding December, including on a new save.
                    double filtered = 0;
                    for (int m = 0; m < 12; m++)
                    {
                        monthly[m] *= sampleWeight;
                        filtered = MonthRetention * filtered + (1 - MonthRetention) * monthly[m];
                    }
                    filtered /= 1 - annualRetention;
                    for (int m = 0; m < 12; m++)
                    {
                        int offset = ((m / 4 * Height + y) * Width + x) * 4 + m % 4;
                        Packed[offset] = (byte)Math.Max(0, Math.Min(255, Math.Round(128 + 4 * filtered)));
                        filtered = MonthRetention * filtered + (1 - MonthRetention) * monthly[m];
                    }
                }
            });
        }

        // Interpolating with this fraction follows the same thermal response
        // at any day within the month and reaches the next stored boundary.
        public static float MonthBlend(float phase) =>
            (float)((1 - Math.Exp(-MonthDuration * phase)) / (1 - MonthRetention));

        public float FilteredAirTemperature(float u, float v, double day)
        {
            float x = (u - (float)Math.Floor(u)) * Width - .5f;
            float y = FMath.Clamp(v * Height - .5f, 0, Height - 1);
            int left = (int)Math.Floor(x), bottom = (int)y;
            int x0 = FMath.Wrap(left, Width), x1 = (x0 + 1) % Width, y1 = Math.Min(bottom + 1, Height - 1);
            float ClimateMonth(int month)
            {
                int panel = month / 4 * Width * Height, channel = month % 4;
                float Read(int col, int row) => Packed[(panel + row * Width + col) * 4 + channel];
                float a = FMath.Lerp(Read(x0, bottom), Read(x1, bottom), x - left);
                float b = FMath.Lerp(Read(x0, y1), Read(x1, y1), x - left);
                return (FMath.Lerp(a, b, y - bottom) - 128) * .25f;
            }
            double period = day / FlatClimateSamples.MonthDays, whole = Math.Floor(period);
            int m = FMath.Wrap((int)(whole % 12), 12);
            float filtered = FMath.Lerp(ClimateMonth(m), ClimateMonth((m + 1) % 12), MonthBlend((float)(period - whole)));
            double lon = (u - .5) * 2 * Math.PI, lat = (FMath.Clamp(v, 0, 1) - .5) * Math.PI;
            double cos = Math.Cos(lat);
            FlatClimateYear.At(seed, day).Offsets((float)(Math.Sin(lon) * cos), (float)Math.Sin(lat),
                (float)(Math.Cos(lon) * cos), out float localHeat, out _);
            return filtered + localHeat;
        }
    }
}
