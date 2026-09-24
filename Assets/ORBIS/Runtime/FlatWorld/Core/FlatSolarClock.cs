using System;

namespace Orbis.Flat
{
    /// <summary>One calendar for simulation, sun, spin and HUD. No Unity or wall-clock dependency.</summary>
    public sealed class FlatSolarClock
    {
        public double PendingDays { get; private set; }
        double economySeconds;
        public double RenderDay(double committedDay) { return committedDay + PendingDays; }
        public void Reset() { PendingDays = 0; economySeconds = 0; }

        public void Advance(double realSeconds, double speed, double secondsPerDay, bool halted, Action<float> step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (!Finite(realSeconds) || realSeconds < 0 || !Finite(speed) || speed < 0 ||
                !Finite(secondsPerDay) || secondsPerDay < 1) throw new ArgumentOutOfRangeException();
            if (halted || speed == 0 || realSeconds == 0) return;
            PendingDays += realSeconds * speed / secondsPerDay;
            economySeconds += realSeconds;
            // Six economic updates per real second, independent of the selected time multiplier.
            // Uncommitted time remains in PendingDays: neither a stall nor a budget drops calendar time.
            int budget = 3;
            while (economySeconds >= 1.0 / 6.0 && PendingDays > 1e-12 && budget-- > 0)
            {
                Commit(step, .25);
                economySeconds -= 1.0 / 6.0;
            }
            if (PendingDays <= 1e-12) economySeconds = Math.Min(economySeconds, 1.0 / 6.0);
        }
        void Commit(Action<float> step, double limit)
        {
            float amount = (float)Math.Min(limit, PendingDays);
            if (amount <= 0) return;
            step(amount);
            PendingDays = Math.Max(0, PendingDays - amount);
        }
        public void Flush(Action<float> step)
        {
            int budget = 4096;
            while (PendingDays > 1e-10 && budget-- > 0) Commit(step, .25);
            if (PendingDays > 1e-8) throw new InvalidOperationException("Calendar backlog is too large to save in one frame.");
            PendingDays = 0;
            economySeconds = 0;
        }
        public static double Phase(double day) { return day - Math.Floor(day); }
        public static double SolarLongitude(double day, double offsetDegrees)
        { return Phase(day + offsetDegrees / 360.0) * Math.PI * 2.0; }
        static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }

    public static class FlatScrollMath
    {
        public static double Wrap(double x, double period)
        {
            if (period <= 0 || double.IsNaN(period) || double.IsInfinity(period) || double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentOutOfRangeException();
            return x - Math.Floor(x / period) * period;
        }
        public static double Shortest(double delta, double period) { return Wrap(delta + period * .5, period) - period * .5; }
        public static int Tile(double x, double y, int width, int height)
        {
            if (width <= 0 || height <= 0 || double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || y < 0 || y >= height) return -1;
            return (int)Math.Floor(y) * width + (int)Math.Floor(Wrap(x, width));
        }
        public static double Smooth(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
        public static double ExpApproach(double current, double target, double rate, double dt)
        { return target + (current - target) * Math.Exp(-rate * Math.Max(0, dt)); }
    }
}
