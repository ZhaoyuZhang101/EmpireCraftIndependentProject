using System;

namespace Orbis.Flat
{
    [Serializable]
    public struct FlatCycloneTrack
    {
        public bool Tracked;
        public double U, V;                 // Longitude wraps; V increases northward.
        public double PressureDeficit;      // hPa below the environment.
        public double WindSpeed;            // Maximum sustained tangential wind, m/s.
        public double Phase;                // Outer-band angle in radians, east through north.
        public double AgeDays, OceanCooling; // Local storm-induced sea surface cooling, degrees C.
        public float Strength { get { return Tracked ? FMath.Clamp((float)(WindSpeed / 55), 0, 1) : 0; } }
    }

    // Two Lagrangian lows on the existing climate grid; no second global fluid grid.
    // The only mutable state is the calendar tick and its two pairs of endpoints.
    [Serializable]
    public sealed class FlatCycloneWeather
    {
        const double StepDays = .25, SecondsPerDay = 86400, EarthRadius = 6371000;
        const double Turn = 2 * Math.PI;
        const int SpinUpSteps = 32, SearchBudget = 64;
        public long StepIndex;
        public FlatCycloneTrack North, South, PreviousNorth, PreviousSouth;

        public FlatCycloneWeather Clone() { return (FlatCycloneWeather)MemberwiseClone(); }
        public static long StepAt(double day)
        {
            double ticks = day / StepDays;
            // FlatSimulation accepts single-precision increments (e.g. 25 x .01f).
            // At a tick boundary, allow their accumulated rounding error, capped
            // at 1/10000 of a tick even in very old worlds.
            double tolerance = Math.Min(.0001, Math.Max(1, Math.Abs(ticks)) / 16777216.0);
            return (long)Math.Floor(ticks + tolerance);
        }

        public static FlatCycloneWeather SpinUp(WorldData world)
        {
            // New worlds and old saves have no prior track. Integrate from still air
            // using the corresponding prior dates, even across the year boundary.
            var weather = new FlatCycloneWeather { StepIndex = StepAt(world.Day) - SpinUpSteps };
            weather.AdvanceTo(world, world.Day);
            return weather;
        }

        public void AdvanceTo(WorldData world, double day)
        {
            long target = StepAt(day);
            if (target <= StepIndex) return;
            uint seed = FMath.Seed(world.Seed);
            while (StepIndex < target)
            {
                var climate = new Climate(world, seed, (StepIndex + .5) * StepDays);
                PreviousNorth = North;
                PreviousSouth = South;
                North = AdvanceTrack(North, climate, 1, seed, StepIndex);
                South = AdvanceTrack(South, climate, -1, seed, StepIndex);
                StepIndex++;
            }
        }

        public FlatCycloneTrack Sample(bool north, double day)
        {
            var previous = north ? PreviousNorth : PreviousSouth;
            var current = north ? North : South;
            // Render between completed steps, with one six-hour interval of latency.
            // The camera can sample arbitrarily often without advancing the world.
            double t = Unit(day / StepDays - StepIndex);
            if (t <= 0) return previous;
            if (t >= 1) return current;
            if (!previous.Tracked) return current; // Discovery always starts at zero wind.
            if (!current.Tracked)
            {
                previous.PressureDeficit *= 1 - t;
                previous.WindSpeed *= 1 - t;
                return previous;
            }
            current.U = FlatScrollMath.Wrap(previous.U + t * FlatScrollMath.Shortest(current.U - previous.U, 1), 1);
            current.V = Mix(previous.V, current.V, t);
            current.PressureDeficit = Mix(previous.PressureDeficit, current.PressureDeficit, t);
            current.WindSpeed = Mix(previous.WindSpeed, current.WindSpeed, t);
            current.Phase = Mix(previous.Phase, current.Phase, t);
            current.AgeDays = Mix(previous.AgeDays, current.AgeDays, t);
            current.OceanCooling = Mix(previous.OceanCooling, current.OceanCooling, t);
            return current;
        }

        static FlatCycloneTrack AdvanceTrack(FlatCycloneTrack track, Climate climate, int hemisphere, uint seed, long tick)
        {
            if (!track.Tracked) return FindDisturbance(climate, hemisphere, seed, tick);
            var air = climate.At(track.U, track.V, hemisphere);
            // Beta drift grows with circulation. The environmental wind, sampled
            // over the surrounding climate cells, supplies most of the translation.
            double beta = track.WindSpeed / (track.WindSpeed + 20);
            double east = .72 * air.East - 1.5 * beta;
            double north = .72 * air.North + hemisphere * 1.25 * beta;
            double translation = Math.Sqrt(east * east + north * north);
            Advect(ref track, east, north, translation);
            // Sample the moved centre too: a coast crossed in this interval removes
            // surface heat input immediately, including on small islands.
            var arrival = climate.At(track.U, track.V, hemisphere);
            double wind = track.WindSpeed;
            double sea = arrival.Sea;
            double stirring = sea * .7 * (wind / 28) * (wind / 28);
            double flushing = .16 + .17 * translation;
            track.OceanCooling = Math.Min(12, FlatScrollMath.ExpApproach(track.OceanCooling, stirring / flushing, flushing, StepDays));
            double rotation = Unit(hemisphere * (track.V - .5) * 180 / 7);
            double heat = sea * Unit((arrival.Temperature - track.OceanCooling - 24.5) / 3.5)
                * arrival.HumidityFactor * (.42 + .58 * arrival.RainFactor) * rotation;
            // Inflow from the pressure deficit lifts moist air. Condensation warms
            // the core and lowers pressure; surface exchange grows with its winds.
            double lift = 1 - Math.Exp(-(.62 * arrival.Organization + track.PressureDeficit / 18 + wind / 32));
            double exchange = .26 + .74 * Unit((Math.Sqrt(arrival.East * arrival.East + arrival.North * arrival.North) + wind) / 30);
            double heating = 54 * heat * exchange * lift / (1 + .60 * arrival.Strain);
            double filling = .37 + .85 * (1 - heat) + 1.05 * (1 - sea) + .18 * arrival.Strain;
            track.PressureDeficit = Math.Min(110, FlatScrollMath.ExpApproach(track.PressureDeficit, heating / filling, filling, StepDays));
            // Gradient-wind balance at the core radius, with boundary-layer spin-up
            // and stronger surface drag over land. Pressure is in hPa.
            double halfCoriolisRadius = arrival.PlanetaryVorticity * 65000 / (2 * SecondsPerDay);
            double balanced = Math.Sqrt(halfCoriolisRadius * halfCoriolisRadius + 58 * track.PressureDeficit) - halfCoriolisRadius;
            double drag = 1 + 1.8 * (1 - sea);
            track.WindSpeed = Math.Min(90, FlatScrollMath.ExpApproach(wind, balanced / drag, 1.4 * drag, StepDays));
            // Outer rainbands orbit more slowly than the eyewall; the angle is
            // continuous across each turn and has opposite signs across the equator.
            track.Phase += hemisphere * StepDays * Math.Min(8, .15 * (wind + track.WindSpeed) * .5);
            track.AgeDays += StepDays;
            if (track.PressureDeficit < .15 && track.WindSpeed < 1.1 && heating < .2) return default;
            return track;
        }

        static void Advect(ref FlatCycloneTrack track, double east, double north, double speed)
        {
            if (speed == 0) return;
            double lat = (track.V - .5) * Math.PI;
            double distance = speed * SecondsPerDay * StepDays / EarthRadius;
            double sinDistance = Math.Sin(distance), cosDistance = Math.Cos(distance);
            double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
            double sinNext = Math.Max(-1, Math.Min(1, sinLat * cosDistance + cosLat * sinDistance * north / speed));
            double deltaLon = Math.Atan2(east / speed * sinDistance * cosLat, cosDistance - sinLat * sinNext);
            track.U = FlatScrollMath.Wrap(track.U + deltaLon / Turn, 1);
            track.V = .5 + Math.Asin(sinNext) / Math.PI;
        }

        static FlatCycloneTrack FindDisturbance(Climate climate, int hemisphere, uint seed, long tick)
        {
            var columns = climate.World.Moisture;
            if (columns == null) return default;
            int first = (int)Math.Ceiling((.5 + 6.0 / 180) * columns.Height - .5);
            int last = (int)Math.Floor((.5 + 35.0 / 180) * columns.Height - .5);
            int count = (last - first + 1) * columns.Width;
            if (count <= 0) return default;
            int probes = Math.Min(SearchBudget, count);
            uint basinSeed = seed ^ (hemisphere > 0 ? 0xa23c1069u : 0x2f39b017u);
            // Visit each tropical cell once per bounded survey. Rotate its order
            // between passes so vacancies do not alias a repeating five-day scan.
            // 9973 is prime and larger than the entire 128x64 climate grid.
            long start = tick * probes;
            var best = new FlatCycloneTrack();
            double bestGrowth = .075;
            for (int p = 0; p < probes; p++)
            {
                long sequence = start + p;
                int within = (int)(sequence % count);
                if (within < 0) within += count;
                long pass = (sequence - within) / count;
                uint offset = FMath.Hash(basinSeed ^ unchecked((uint)pass));
                int cell = (int)((within * 9973L + offset) % count);
                int y = first + cell / columns.Width;
                if (hemisphere < 0) y = columns.Height - 1 - y;
                double u = (cell % columns.Width + .5) / columns.Width, v = (y + .5) / columns.Height;
                int tile = climate.Tile(u, v);
                if (!FlatTerrainAnalysis.Ocean(climate.World.Water[tile])) continue;
                var air = climate.At(u, v, hemisphere);
                double growth = air.Sea * Unit((air.Temperature - 25.6) / 2.5)
                    * air.HumidityFactor * air.RainFactor * air.Organization / (1 + air.Strain);
                if (growth <= bestGrowth) continue;
                bestGrowth = growth;
                best = new FlatCycloneTrack { Tracked = true, U = u, V = v };
            }
            return best;
        }

        struct Air
        {
            public double Temperature, HumidityFactor, RainFactor, Sea;
            public double East, North, Organization, PlanetaryVorticity, Strain;
        }

        readonly struct Climate
        {
            public readonly WorldData World;
            readonly int month, next;
            readonly float blend;
            readonly FlatClimateYear year;
            public Climate(WorldData world, uint seed, double day)
            {
                World = world;
                double seasonalMonth = FlatScrollMath.Wrap(day, 360) / 30;
                month = (int)seasonalMonth;
                next = (month + 1) % 12;
                blend = (float)(seasonalMonth - month);
                year = FlatClimateYear.At(seed, day);
            }

            public int Tile(double u, double v)
            {
                return World.Index((int)Math.Floor(u * World.Width), FMath.Clamp((int)(v * World.Height), 0, World.Height - 1));
            }

            public Air At(double u, double v, int hemisphere)
            {
                var air = new Air();
                var columns = World.Moisture;
                if (columns == null) return air;
                int tile = Tile(u, v), a = tile * 12 + month, b = tile * 12 + next;
                double lat = (v - .5) * Math.PI, lon = (u - .5) * Turn;
                double sinLat = Math.Sin(lat), cosLat = Math.Max(.05, Math.Cos(lat));
                year.Offsets((float)(Math.Sin(lon) * cosLat), (float)sinLat, (float)(Math.Cos(lon) * cosLat), out float degrees, out float rainAnomaly);
                air.Temperature = FMath.Lerp(World.Temperature[a], World.Temperature[b], blend) + degrees;
                double rain = FMath.Lerp(World.Precipitation[a], World.Precipitation[b], blend) * (1 + rainAnomaly);
                air.RainFactor = Unit((rain - 45) / 95);
                air.HumidityFactor = Unit((Column(columns.Humidity, u, v) - .53) / .32);
                if (FlatTerrainAnalysis.Ocean(World.Water[tile]))
                {
                    // Surface exchange over a finite core (~100 km from centre).
                    double du = 100000 / (EarthRadius * Turn * cosLat), dv = 100000 / (EarthRadius * Math.PI);
                    air.Sea = .6 + .1 * (Ocean(u - du, v) + Ocean(u + du, v) + Ocean(u, v - dv) + Ocean(u, v + dv));
                }
                double dx = 1.0 / columns.Width, dy = 1.0 / columns.Height;
                Wind(u, v, out double e, out double n);
                Wind(u - dx, v, out double westE, out double westN);
                Wind(u + dx, v, out double eastE, out double eastN);
                Wind(u, v - dy, out double southE, out double southN);
                Wind(u, v + dy, out double northE, out double northN);
                air.East = (2 * e + westE + eastE + southE + northE) / 6;
                air.North = (2 * n + westN + eastN + southN + northN) / 6;
                // Covariant horizontal velocity gradient on the sphere, in 1/day.
                // Convergence stretches absolute vorticity (planetary + relative).
                double scale = SecondsPerDay / EarthRadius, curvature = sinLat / cosLat;
                double ee = scale * ((eastE - westE) / (2 * dx * Turn * cosLat) - n * curvature);
                double ne = scale * ((eastN - westN) / (2 * dx * Turn * cosLat) + e * curvature);
                double en = scale * (northE - southE) / (2 * dy * Math.PI);
                double nn = scale * (northN - southN) / (2 * dy * Math.PI);
                double convergence = -(ee + nn), curl = hemisphere * (ne - en);
                air.Strain = Math.Sqrt((ee - nn) * (ee - nn) + (ne + en) * (ne + en));
                air.PlanetaryVorticity = 4 * Math.PI * Math.Abs(sinLat);
                double absolute = Math.Max(0, air.PlanetaryVorticity + curl);
                double stretching = Math.Max(0, convergence) * absolute;
                if (absolute > 0)
                    air.Organization = Unit((stretching + .60 * Math.Max(0, curl - .075) - .055) / .38);
                return air;
            }

            int Ocean(double u, double v) { return FlatTerrainAnalysis.Ocean(World.Water[Tile(u, v)]) ? 1 : 0; }
            float Column(float[] field, double u, double v)
            {
                var columns = World.Moisture;
                return FMath.Lerp(columns.Sample(field, (float)u, (float)v, month), columns.Sample(field, (float)u, (float)v, next), blend);
            }
            void Wind(double u, double v, out double east, out double north)
            {
                // Saved column wind components are normalized. One unit represents
                // 9 m/s of the broad, near-surface circulation.
                east = 9 * Column(World.Moisture.East, u, v);
                north = 9 * Column(World.Moisture.North, u, v);
            }
        }

        static double Unit(double x) { return Math.Max(0, Math.Min(1, x)); }
        static double Mix(double a, double b, double t) { return a + (b - a) * t; }
    }
}
