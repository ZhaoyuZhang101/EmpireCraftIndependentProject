using System;

namespace Orbis.Flat
{
    public enum ClimateRange { Signed, Nonnegative, Fraction }

    // Reconstruct a calendar month from its mean and its two neighbours.
    // Linear ramps meet at mid-month (or a plateau for extreme humidity).
    // Their integral recovers the saved monthly mean.
    public readonly struct FlatMonthlyForcing
    {
        internal readonly double AbsoluteMonth;
        internal readonly int Month;
        internal readonly float SeasonalMonth;
        readonly int previous, next;
        readonly bool firstHalf;
        readonly float edgeWeight;
        public const int HalfMonthDays = FlatClimateSamples.MonthDays / 2;
        public double SegmentStartDay => AbsoluteMonth * FlatClimateSamples.MonthDays + (firstHalf ? 0 : HalfMonthDays);
        public float SegmentBlend => firstHalf ? 1 - edgeWeight : edgeWeight;

        public FlatMonthlyForcing(double day)
        {
            AbsoluteMonth = Math.Floor(day / FlatClimateSamples.MonthDays);
            Month = FMath.Wrap((int)(AbsoluteMonth % 12), 12);
            previous = (Month + 11) % 12;
            next = (Month + 1) % 12;
            double phase = Math.Min(1, Math.Max(0, (day - AbsoluteMonth * FlatClimateSamples.MonthDays) / FlatClimateSamples.MonthDays));
            SeasonalMonth = (float)(Month + phase - .5);
            firstHalf = phase < .5;
            edgeWeight = (float)Math.Abs(2 * phase - 1);
        }

        public float Sample(float[] data, int start, ClimateRange range = ClimateRange.Signed) =>
            Reconstruct(data[start + previous], data[start + Month], data[start + next], range);

        public float Sample(FlatMoistureState air, float[] data, float u, float v, ClimateRange range = ClimateRange.Signed)
        {
            // Weather grids and cyclone genesis frequently sample exact column centres.
            float x = u * air.Width - .5f, y = v * air.Height - .5f;
            int col = (int)x, row = (int)y;
            if (col == x && row == y && (uint)col < (uint)air.Width && (uint)row < (uint)air.Height)
                return Sample(data, (row * air.Width + col) * 12, range);
            return Reconstruct(air.Sample(data, u, v, previous), air.Sample(data, u, v, Month), air.Sample(data, u, v, next), range);
        }

        internal float SampleBilinear(float[] data, int c00, int c10, int c01, int c11, float tx, float ty, bool exactCenter, ClimateRange range = ClimateRange.Signed)
        {
            if (exactCenter) return Sample(data, c00, range);
            float prior = FMath.Lerp(FMath.Lerp(data[c00 + previous], data[c10 + previous], tx), FMath.Lerp(data[c01 + previous], data[c11 + previous], tx), ty);
            float mean = FMath.Lerp(FMath.Lerp(data[c00 + Month], data[c10 + Month], tx), FMath.Lerp(data[c01 + Month], data[c11 + Month], tx), ty);
            float following = FMath.Lerp(FMath.Lerp(data[c00 + next], data[c10 + next], tx), FMath.Lerp(data[c01 + next], data[c11 + next], tx), ty);
            return Reconstruct(prior, mean, following, range);
        }

        static float Boundary(float a, float b, ClimateRange range)
        {
            float average = (a + b) * .5f;
            if (range == ClimateRange.Signed) return average;
            // Both faces must be <= 2*mean for a nonnegative centre. In
            // particular a dry month makes both its faces zero.
            float ceiling = 2 * Math.Min(a, b);
            if (range == ClimateRange.Nonnegative) return Math.Min(average, ceiling);
            float floor = 2 * Math.Max(a, b) - 1;
            if (floor <= ceiling) return FMath.Clamp(average, floor, ceiling);
            // Sharply different humidity fractions need a shorter ramp on
            // at least one side. Share a face weighted toward the nearer bound.
            float wa = Math.Min(a, 1-a), wb = Math.Min(b, 1-b);
            return wa + wb > 0 ? (wb*a + wa*b)/(wa + wb) : average;
        }

        float Reconstruct(float prior, float mean, float following, ClimateRange range)
        {
            float left = Boundary(prior, mean, range), right = Boundary(mean, following, range);
            float edgeMean = (left + right) * .5f;
            float middle = 2*mean - edgeMean;
            float weight = edgeWeight;
            if (range == ClimateRange.Fraction && (middle < 0 || middle > 1))
            {
                // A mean of exactly zero or one occupies the whole month.
                // Otherwise narrow both ramps around a bounded central plateau:
                // mean = middle + (edgeMean - middle) * rampWidth.
                if (mean <= 0 || mean >= 1) return mean;
                middle = FMath.Clamp(middle, 0, 1);
                float rampWidth = (mean - middle)/(edgeMean - middle);
                weight = Math.Max(0, 1 - (1 - edgeWeight) * .5f / rampWidth);
            }
            return FMath.Lerp(middle, firstHalf ? left : right, weight);
        }
    }
}
