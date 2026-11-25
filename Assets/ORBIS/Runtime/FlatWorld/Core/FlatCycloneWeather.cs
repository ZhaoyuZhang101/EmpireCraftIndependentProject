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

    /// <summary>
    /// Read-only physical 10 m surface wind, sea-level pressure deficit, and rainband precipitation sample
    /// evaluated from a <see cref="FlatCycloneTrack"/> at normalized spherical coordinates <c>(u, v)</c>.
    /// <list type="bullet">
    ///   <item><description><c>u</c> in <c>[0, 1)</c> wraps eastward (<c>lonDeg = (u - 0.5) * 360</c>); <c>v</c> in <c>[0, 1]</c> increases northward (<c>latDeg = (v - 0.5) * 180</c>).</description></item>
    ///   <item><description>Velocities (<see cref="EastMs"/>, <see cref="NorthMs"/>, <see cref="TangentialSpeedMs"/>, <see cref="RadialInflowMs"/>) are in SI <c>m/s</c>.</description></item>
    ///   <item><description><see cref="PressureDeficitHpa"/> is in <c>hPa</c>; <see cref="RainRateMmPerHour"/> is in <c>mm/h</c> (<c>kg/m^2/h</c>); <see cref="RadiusKm"/> is great-circle distance in <c>km</c>.</description></item>
    /// </list>
    /// </summary>
    public readonly struct FlatCycloneSurfaceWind
    {
        public readonly bool Active;
        public readonly float EastMs;
        public readonly float NorthMs;
        public readonly float TangentialSpeedMs;
        public readonly float SignedTangentialMs;
        public readonly float RadialInflowMs;
        public readonly float PressureDeficitHpa;
        public readonly float RainRateMmPerHour;
        public readonly float RadiusKm;

        public float SpeedMs => (float)Math.Sqrt(EastMs * EastMs + NorthMs * NorthMs);

        public FlatCycloneSurfaceWind(
            bool active, float eastMs, float northMs,
            float tangentialSpeedMs, float signedTangentialMs, float radialInflowMs,
            float pressureDeficitHpa, float rainRateMmPerHour, float radiusKm)
        {
            Active = active;
            EastMs = eastMs;
            NorthMs = northMs;
            TangentialSpeedMs = tangentialSpeedMs;
            SignedTangentialMs = signedTangentialMs;
            RadialInflowMs = radialInflowMs;
            PressureDeficitHpa = pressureDeficitHpa;
            RainRateMmPerHour = rainRateMmPerHour;
            RadiusKm = radiusKm;
        }
    }

    // Two Lagrangian lows on the existing climate grid; no second global fluid grid.
    // The only mutable state is the calendar tick and its two pairs of endpoints.
    [Serializable]
    public sealed class FlatCycloneWeather
    {
        const double StepDays = .25, SecondsPerDay = 86400;
        internal const double EarthRadius = 6371000, CoreRadiusMetres = 65000;
        internal const float ClimateWindMetresPerSecond = 9;
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

        // The display and SWE may cross a six-hour boundary while a fraction of
        // the game calendar is still pending. Predict on a copy in that case.
        public static FlatCycloneWeather ForwardStateAtTick(WorldData world, long tick)
        {
            var current = world?.Cyclones;
            if (current == null || tick <= current.StepIndex) return current;
            var forecast = current.Clone();
            forecast.AdvanceTo(world, tick * StepDays);
            return forecast;
        }

        public static FlatCycloneWeather SpinUp(WorldData world) => SpinUpAt(world, world.Day);

        public static FlatCycloneWeather SpinUpAt(WorldData world, double day)
        {
            // Integrate a preview from still air over the same spin-up window used for new worlds.
            var weather = new FlatCycloneWeather { StepIndex = StepAt(day) - SpinUpSteps };
            weather.AdvanceTo(world, day);
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

        /// <summary>
        /// Evaluates active cyclone core uplift, outer compensating subsidence moat, and trailing cold-wake SST cooling
        /// purely from serialized track endpoints at <paramref name="day"/> so cloud forcing is 100% bit-exact across save/load.
        /// </summary>
        public void SampleCloudFootprint(
            double u, double v, double day,
            out float coreConvection, out float outerSubsidence, out float wakeCoolingC, out float wakeStabilization)
        {
            coreConvection = 0f;
            outerSubsidence = 0f;
            wakeCoolingC = 0f;
            wakeStabilization = 0f;

            double cosLat = Math.Max(0.12, Math.Cos((v - 0.5) * Math.PI));
            for (int h = 0; h < 2; h++)
            {
                bool isNorth = h == 0;
                var storm = Sample(isNorth, day);
                if (!storm.Tracked || storm.Strength <= 0.02f) continue;
                var prev = isNorth ? PreviousNorth : PreviousSouth;
                var curr = isNorth ? North : South;

                double dxKm = FlatScrollMath.Shortest(u - storm.U, 1.0) * 40030.0 * cosLat;
                double dyKm = (v - storm.V) * 20015.0;
                double rKm = Math.Sqrt(dxKm * dxKm + dyKm * dyKm);
                if (rKm < 1650.0)
                {
                    float core = storm.Strength * (float)Math.Exp(-(rKm * rKm) / (460.0 * 460.0));
                    if (core > coreConvection) coreConvection = core;
                    // Compensating outer subsidence moat (700..1450 km) clears surrounding fair-weather ring
                    double ring = (rKm - 980.0) / 360.0;
                    float moat = storm.Strength * 0.55f * (float)Math.Exp(-(ring * ring));
                    if (moat > outerSubsidence) outerSubsidence = moat;
                }

                // Trailing oceanic cold-wake & stabilized boundary-layer plume behind the moving storm center
                double vxKm = prev.Tracked && curr.Tracked ? FlatScrollMath.Shortest(curr.U - prev.U, 1.0) * 40030.0 * cosLat : -120.0;
                double vyKm = prev.Tracked && curr.Tracked ? (curr.V - prev.V) * 20015.0 : 0.0;
                double vNorm = Math.Sqrt(vxKm * vxKm + vyKm * vyKm);
                double tx = vNorm > 1e-3 ? -vxKm / vNorm : 1.0;
                double ty = vNorm > 1e-3 ? -vyKm / vNorm : 0.0;
                double alongWakeKm = dxKm * tx + dyKm * ty;
                double crossWakeKm = dxKm * (-ty) + dyKm * tx;
                double wakeLenKm = Math.Min(2200.0, 320.0 + storm.AgeDays * 380.0);
                if (alongWakeKm > -180.0 && alongWakeKm < wakeLenKm && Math.Abs(crossWakeKm) < 950.0)
                {
                    double sAlong = alongWakeKm <= 0.0
                        ? Math.Exp(-(alongWakeKm * alongWakeKm) / (180.0 * 180.0))
                        : Math.Exp(-alongWakeKm / Math.Max(350.0, wakeLenKm * 0.62));
                    double sCross = Math.Exp(-(crossWakeKm * crossWakeKm) / (420.0 * 420.0));
                    float plume = (float)(sAlong * sCross);
                    float coolAmp = (float)Math.Max(storm.OceanCooling, 1.25 * storm.Strength);
                    float stabAmp = storm.Strength * (float)Unit((alongWakeKm - 220.0) / 380.0);
                    if (coolAmp * plume > wakeCoolingC) wakeCoolingC = coolAmp * plume;
                    if (stabAmp * plume > wakeStabilization) wakeStabilization = stabAmp * plume;
                }
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

        /// <summary>
        /// Evaluates physical Holland-like 10 m cyclonic tangential wind (<c>m/s</c>), 23% Ekman boundary-layer radial inflow,
        /// pressure deficit (<c>hPa</c>), and eyewall/spiral-band rainfall (<c>mm/h</c>) from an already-sampled <see cref="FlatCycloneTrack"/>.
        /// Zero allocations; strictly zero outside the storm's outer radius (~1,050 km) or when <c>!track.Tracked</c>.
        /// </summary>
        public static FlatCycloneSurfaceWind SampleSurfaceWindFromTrack(
            in FlatCycloneTrack track, int hemisphereSpin, double u, double v)
        {
            if (!track.Tracked || track.WindSpeed <= 0.25 || track.PressureDeficit <= 0.05)
                return default;

            int spin = hemisphereSpin >= 0 ? 1 : -1;
            double latRad = (FMath.Clamp((float)v, 0.001f, 0.999f) - 0.5) * Math.PI;
            double stormLatRad = (FMath.Clamp((float)track.V, 0.001f, 0.999f) - 0.5) * Math.PI;
            double cosMid = Math.Max(0.10, Math.Cos(0.5 * (latRad + stormLatRad)));
            double dxKm = FlatScrollMath.Shortest(u - track.U, 1.0) * 40030.2 * cosMid;
            double dyKm = (v - track.V) * 20015.1;
            double rKm = Math.Sqrt(dxKm * dxKm + dyKm * dyKm);

            double outerLimitKm = 620.0 + 430.0 * Math.Sqrt(track.Strength);
            if (rKm >= outerLimitKm)
                return new FlatCycloneSurfaceWind(false, 0f, 0f, 0f, 0f, 0f, 0f, 0f, (float)rKm);

            double tOuter = Unit((rKm - outerLimitKm * 0.52) / (outerLimitKm * 0.48));
            double outerGate = 1.0 - tOuter * tOuter * (3.0 - 2.0 * tOuter);

            // Radius of maximum wind contracts from ~72 km (weak depression) to ~46 km (intense typhoon/hurricane).
            double rmwKm = 72.0 - 26.0 * track.Strength;
            double x = rKm / rmwKm;
            double xPow = Math.Pow(x, 1.18);
            double profile = (2.0 * xPow) / (1.0 + xPow * xPow);
            double vTan = track.WindSpeed * profile * outerGate;
            double vInflow = 0.23 * vTan;

            double erX = 0.0, erY = 0.0;
            if (rKm > 1e-4)
            {
                double invR = 1.0 / rKm;
                erX = dxKm * invR;
                erY = dyKm * invR;
            }
            // Northern Hemisphere (spin = +1): counter-clockwise (-erY, +erX); Southern Hemisphere (spin = -1): clockwise (+erY, -erX).
            double etX = -erY * spin;
            double etY = erX * spin;
            double eastMs = etX * vTan - erX * vInflow;
            double northMs = etY * vTan - erY * vInflow;

            // Exponential pressure-deficit core (Holland B ~ 1.35) + spiral eyewall/rainband condensation rainfall (mm/h).
            double rpKm = rmwKm * 1.38;
            double localDeficitHpa = track.PressureDeficit * Math.Exp(-Math.Pow(rKm / rpKm, 1.35)) * outerGate;

            double angle = Math.Atan2(dyKm, dxKm);
            double spiral = 0.68 + 0.32 * Math.Cos(2.0 * (angle - spin * Math.Log(1.0 + x) * 1.35) - track.Phase);
            double eyewallRing = Math.Exp(-0.5 * Math.Pow((x - 0.95) / 0.78, 2.0));
            double outerBand = 0.34 * Math.Exp(-0.5 * Math.Pow((x - 2.35) / 1.35, 2.0));
            double rainMmPerHour = 48.0 * Math.Pow(Math.Max(0.0, track.WindSpeed / 48.0), 1.35)
                                 * Math.Max(eyewallRing, outerBand) * spiral * outerGate;

            bool active = vTan > 0.20 || localDeficitHpa > 0.15;
            return new FlatCycloneSurfaceWind(
                active,
                (float)eastMs,
                (float)northMs,
                (float)vTan,
                (float)(spin * vTan),
                (float)vInflow,
                (float)localDeficitHpa,
                (float)rainMmPerHour,
                (float)rKm);
        }

        /// <summary>
        /// Read-only sampler evaluating the dominant surface cyclone wind (<c>m/s</c>) at spherical <c>(u, v)</c> and <paramref name="day"/>.
        /// Never advances or mutates <see cref="FlatCycloneWeather"/>.
        /// </summary>
        public FlatCycloneSurfaceWind SampleSurfaceWind(double u, double v, double day)
        {
            var nh = SampleSurfaceWindFromTrack(Sample(true, day), +1, u, v);
            var sh = SampleSurfaceWindFromTrack(Sample(false, day), -1, u, v);
            if (!sh.Active) return nh;
            if (!nh.Active) return sh;
            return nh.TangentialSpeedMs >= sh.TangentialSpeedMs ? nh : sh;
        }

        static FlatCycloneTrack AdvanceTrack(FlatCycloneTrack track, Climate climate, int hemisphere, uint seed, long tick)
        {
            if (!track.Tracked) return FindDisturbance(climate, hemisphere, seed, tick);
            var air = climate.At(track.U, track.V, hemisphere);
            // Beta drift grows with circulation. Deep-layer 500-300 hPa steering suppresses shallow
            // surface Hadley equatorward Ekman inflow in the tropics and couples trajectories to
            // time-evolving subtropical-ridge strengths/weaknesses & passing midlatitude Rossby troughs.
            double beta = track.WindSpeed / (track.WindSpeed + 20);
            double latDegAbs = Math.Abs((track.V - .5) * 180.0);
            double deepLayerGate = air.HasDeepDiagnostics ? (1.0 - .74 * Unit((25.0 - latDegAbs) / 12.0)) : 1.0;
            double polewardBeta = air.HasDeepDiagnostics ? (1.65 * beta + .55 * air.SynopticSeed) : (1.25 * beta);
            double east = .72 * air.East - 1.5 * beta + air.SynopticSteerEast;
            double north = .72 * air.North * deepLayerGate + hemisphere * polewardBeta + air.SynopticSteerNorth;
            double translation = Math.Sqrt(east * east + north * north);
            Advect(ref track, east, north, translation);
            // Sample the moved centre too: a coast crossed in this interval removes
            // surface heat input immediately, including on small islands.
            var arrival = climate.At(track.U, track.V, hemisphere);
            double wind = track.WindSpeed;
            double sea = arrival.Sea;
            double warmPoolBuffer = arrival.HasDeepDiagnostics ? (1.0 - .42 * Unit((arrival.MpiFactor - 1.0) / .75)) : 1.0;
            double stirring = sea * .7 * warmPoolBuffer * (wind / 28) * (wind / 28);
            double flushing = .16 + .17 * translation;
            track.OceanCooling = Math.Min(12, FlatScrollMath.ExpApproach(track.OceanCooling, stirring / flushing, flushing, StepDays));
            double rotation = Unit(hemisphere * (track.V - .5) * 180 / 7);
            // Account for marine 2m air-SST enthalpy disequilibrium (~1.2 C) when Emanuel MPI diagnostics are present
            double sstOffset = arrival.HasDeepDiagnostics ? 1.25 * Unit((arrival.MpiFactor - .85) / .65) : 0.0;
            double heat = sea * Unit((arrival.Temperature + sstOffset - track.OceanCooling - 24.5) / 3.5)
                * arrival.HumidityFactor * (.42 + .58 * arrival.RainFactor) * rotation;
            // Inflow from the pressure deficit lifts moist air. Condensation warms
            // the core and lowers pressure; surface exchange grows with its winds.
            double lift = 1 - Math.Exp(-(.68 * arrival.Organization + track.PressureDeficit / 16 + wind / 29));
            double exchange = .26 + .74 * Unit((Math.Sqrt(arrival.East * arrival.East + arrival.North * arrival.North) + wind) / 28);
            double heating = 56 * arrival.MpiFactor * heat * exchange * lift / (1 + .60 * arrival.Strain + .55 * arrival.ShearVentilation);
            double filling = .37 + .85 * (1 - heat) + 1.05 * (1 - sea) + .18 * arrival.Strain + .22 * arrival.ShearVentilation;
            track.PressureDeficit = Math.Min(110, FlatScrollMath.ExpApproach(track.PressureDeficit, heating / filling, filling, StepDays));
            // Gradient-wind balance at the core radius, with boundary-layer spin-up
            // and stronger surface drag over land. Pressure is in hPa.
            double halfCoriolisRadius = arrival.PlanetaryVorticity * CoreRadiusMetres / (2 * SecondsPerDay);
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
                double sstGenOffset = air.HasDeepDiagnostics ? 1.15 * Unit((air.MpiFactor - .85) / .65) : 0.0;
                double growth = air.Sea * Unit((air.Temperature + sstGenOffset - 25.6) / 2.5)
                    * air.HumidityFactor * air.RainFactor * air.Organization * air.MpiFactor
                    / (1 + air.Strain + .65 * air.ShearVentilation);
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
            public double MpiFactor, ShearVentilation, SynopticSeed;
            public double SynopticSteerEast, SynopticSteerNorth;
            public bool HasDeepDiagnostics;
        }

        readonly struct Climate
        {
            public readonly WorldData World;
            readonly uint seed;
            readonly double day;
            readonly FlatMonthlyForcing season;
            readonly FlatClimateYear year;
            public Climate(WorldData world, uint seed, double day)
            {
                World = world;
                this.seed = seed;
                this.day = day;
                season = new FlatMonthlyForcing(day);
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
                int tile = Tile(u, v), start = tile * 12;
                double lat = (v - .5) * Math.PI, lon = (u - .5) * Turn;
                double sinLat = Math.Sin(lat), cosLat = Math.Max(.05, Math.Cos(lat));
                year.Offsets((float)(Math.Sin(lon) * cosLat), (float)sinLat, (float)(Math.Cos(lon) * cosLat), out float degrees, out float rainAnomaly);
                air.Temperature = season.Sample(World.Temperature, start) + degrees;
                double rain = season.Sample(World.Precipitation, start, ClimateRange.Nonnegative) * (1 + rainAnomaly);
                air.RainFactor = Unit((rain - 45) / 95);
                air.HumidityFactor = Unit((season.Sample(columns, columns.Humidity, (float)u, (float)v, ClimateRange.Fraction) - .53) / .32);
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
                var exo = columns.ExoPlaSim;
                if (exo != null)
                {
                    air.HasDeepDiagnostics = true;
                    int m0 = FMath.Wrap((int)Math.Floor(season.SeasonalMonth), 12), m1 = (m0 + 1) % 12;
                    float frac = (float)(season.SeasonalMonth - Math.Floor(season.SeasonalMonth));
                    float mpi = FMath.Lerp(exo.SampleMonthly(exo.MaxPotentialIntensityMs, (float)u, (float)v, m0),
                                           exo.SampleMonthly(exo.MaxPotentialIntensityMs, (float)u, (float)v, m1), frac);
                    float shear = FMath.Lerp(exo.SampleMonthly(exo.VerticalShearMs, (float)u, (float)v, m0),
                                             exo.SampleMonthly(exo.VerticalShearMs, (float)u, (float)v, m1), frac);
                    float chi = FMath.Lerp(exo.SampleMonthly(exo.EntropyDeficit, (float)u, (float)v, m0),
                                           exo.SampleMonthly(exo.EntropyDeficit, (float)u, (float)v, m1), frac);
                    float gpi = FMath.Lerp(exo.SampleMonthly(exo.GenesisPotential, (float)u, (float)v, m0),
                                           exo.SampleMonthly(exo.GenesisPotential, (float)u, (float)v, m1), frac);

                    var cfg = exo.Config ?? World.PlanetClimate;
                    float subJetDeg = cfg != null && !cfg.IsStandardModernEarth ? cfg.HadleyTerminusDeg : 30f;
                    float polJetDeg = cfg != null && !cfg.IsStandardModernEarth ? cfg.PolarFrontTerminusDeg : 62f;

                    // Propagating tropical easterly waves + intraseasonal MJO/BSISO packet + 500-250 hPa Rossby troughs/ridges.
                    FlatSynopticWeather.EvaluateTropicalWaveAndSteering(
                        seed, day, u, v, hemisphere, air.East, air.North, subJetDeg, polJetDeg,
                        out double waveEnvelope, out double synShearAdd, out double steerEastMs, out double steerNorthMs);

                    double latGate = Unit((Math.Abs(sinLat) - 0.145) / 0.09);
                    double summerSeason = Unit(hemisphere * Math.Sin((season.SeasonalMonth - 3.5) * Math.PI / 6.0) * 1.6 - 0.05);
                    double gpiActive = Unit((gpi - 0.085) / 0.14);
                    double mpiBoost = summerSeason * latGate * gpiActive * Math.Max(0.0, (mpi - 50.0) / 34.0);
                    air.MpiFactor = FMath.Clamp((float)(0.62 + 0.22 * (mpi / 68.0) + 1.18 * mpiBoost) * (float)(0.35 + 0.65 * latGate), 0.35f, 2.02f);
                    air.ShearVentilation = Math.Max(0.0, (shear - 28.0) / 32.0) * (0.35 + 0.65 * chi) + synShearAdd;

                    // Genesis organization & mid-level RH respond to propagating easterly-wave troughs / MJO active envelopes
                    // rather than locking onto a static island convergence cell all month.
                    double baseWaveSeed = summerSeason * latGate * gpiActive * Unit((mpi - 56.0) / 24.0);
                    double disturbanceMod = 0.22 + 1.24 * waveEnvelope;
                    air.SynopticSeed = baseWaveSeed * (0.45 + 0.90 * waveEnvelope);
                    air.HumidityFactor = FMath.Clamp((float)(air.HumidityFactor * (0.72 + 0.42 * waveEnvelope)), 0f, 1.12f);
                    air.Organization = (air.Organization * (0.30 + 0.70 * latGate) + 0.80 * baseWaveSeed) * disturbanceMod;
                    air.SynopticSteerEast = steerEastMs;
                    air.SynopticSteerNorth = steerNorthMs;
                }
                else
                {
                    air.MpiFactor = 1.0;
                    air.ShearVentilation = 0.0;
                }
                return air;
            }

            int Ocean(double u, double v) { return FlatTerrainAnalysis.Ocean(World.Water[Tile(u, v)]) ? 1 : 0; }
            void Wind(double u, double v, out double east, out double north)
            {
                // Saved column wind components are normalized. One unit represents
                // 9 m/s of the broad, near-surface circulation.
                var columns = World.Moisture;
                east = ClimateWindMetresPerSecond * season.Sample(columns, columns.East, (float)u, (float)v);
                north = ClimateWindMetresPerSecond * season.Sample(columns, columns.North, (float)u, (float)v);
            }
        }

        static double Unit(double x) { return Math.Max(0, Math.Min(1, x)); }
        static double Mix(double a, double b, double t) { return a + (b - a) * t; }
    }

    /// <summary>
    /// Deterministic, zero-allocation synoptic weather kinematics coupling low/upper winds, baroclinic Rossby
    /// troughs/ridges, propagating tropical easterly waves, MJO envelopes, and upwind condensation -> downwind
    /// post-precipitation rain-out &amp; dry subsidence cloud lifecycle.
    /// </summary>
    internal static class FlatSynopticWeather
    {
        const double Turn = 2.0 * Math.PI;

        static double PhaseSeed(uint seed, uint salt) => FMath.Unit(FMath.Hash(seed ^ salt)) * Turn;

        public static void EvaluateTropicalWaveAndSteering(
            uint seed, double calendarDay, double u, double v, int hemisphere,
            double surfaceEastMs, double surfaceNorthMs, float subJetDeg, float polJetDeg,
            out double waveEnvelope, out double synShearAdd, out double steerEastMs, out double steerNorthMs)
        {
            double lon = (u - 0.5) * Turn;
            double lat = (v - 0.5) * Math.PI;
            double absLatDeg = Math.Abs((v - 0.5) * 180.0);

            // 1. Intraseasonal MJO / BSISO eastward convective envelope (~36-day & ~24-day harmonics, unwrapped day)
            //    migrates basin-scale convective favorability across Indian/WestPac/EastPac/Atlantic within 2-5 weeks.
            double mjo1 = Math.Cos(lon - calendarDay * (Turn / 36.0) + PhaseSeed(seed, 0x41a7d311u));
            double mjo2 = Math.Cos(2.0 * lon - calendarDay * (Turn / 23.5) + PhaseSeed(seed, 0x7b29c503u));
            double mjo = 0.58 * mjo1 + 0.42 * mjo2;

            // 2. Propagating tropical easterly waves (k = 6, 8, 11; westward ~6.0-7.4 deg/day) + meridional ITCZ/monsoon undulation
            //    so consecutive disturbances form at genuinely different longitudes and latitudes (9..24 deg) rather than one grid row.
            uint hSalt = hemisphere > 0 ? 0x19d4e27bu : 0x6c83f109u;
            double tradeBoost = FMath.Clamp((float)(-surfaceEastMs / 8.5), 0f, 1.3f);
            double cBase = 0.108 + 0.015 * tradeBoost; // rad/day westward
            double latPhase = (absLatDeg - 15.0) * 0.16;
            double w6 = Math.Cos(6.0 * (lon + calendarDay * cBase) + latPhase + PhaseSeed(seed, hSalt ^ 0x11u));
            double w8 = Math.Cos(8.0 * (lon + calendarDay * (cBase * 1.13)) - 1.15 * latPhase + PhaseSeed(seed, hSalt ^ 0x22u));
            double w11 = Math.Cos(11.0 * (lon + calendarDay * (cBase * 0.91)) + 0.85 * latPhase + PhaseSeed(seed, hSalt ^ 0x33u));
            double wave = 0.44 * w6 + 0.36 * w8 + 0.20 * w11;

            // Preferred genesis latitude migrates between low-latitude easterly waves (~11 deg) and monsoon/subtropical gyres (~20 deg).
            double latAxisShift = 0.65 * Math.Sin(2.0 * lon - calendarDay * 0.21 + PhaseSeed(seed, hSalt ^ 0x55u))
                                + 0.35 * Math.Cos(5.0 * lon + calendarDay * 0.31 + PhaseSeed(seed, hSalt ^ 0x77u));
            double prefLatDeg = 15.2 + 4.8 * latAxisShift;
            double latBandMatch = Math.Exp(-Math.Pow((absLatDeg - prefLatDeg) / 4.8, 2.0));

            // Monsoon-trough westerly confluence bonus (low-latitude monsoon westerlies meeting trades in WPac / NIO / SPAC).
            double monsoonConfluence = surfaceEastMs > -1.5 && absLatDeg < 21.0
                ? 0.22 * FMath.Clamp((float)((surfaceEastMs + 1.5) / 5.5), 0f, 1f)
                : 0.0;

            double combined = 0.56 * wave + 0.34 * mjo + 0.34 * (latBandMatch - 0.38) + monsoonConfluence;
            waveEnvelope = Math.Max(0.0, Math.Min(1.0, (combined + 0.14) / 0.72));

            // 3. Upper-tropospheric 500-250 hPa Rossby trough / subtropical-ridge steering & synoptic wind shear.
            GlebaRossbyWaveField.SampleSynopticUpperFlow(
                seed, calendarDay, lat, lon, subJetDeg, polJetDeg,
                out float upEastMs, out float upNorthMs, out float troughDepth, out float aheadOfTrough);

            double dU = upEastMs - surfaceEastMs;
            double dV = upNorthMs - surfaceNorthMs;
            double synShearMs = Math.Sqrt(dU * dU + dV * dV);
            // Digging midlatitude troughs inject dry high-shear air; upper ridges/outflow channels lower ventilation.
            synShearAdd = Math.Max(0.0, (synShearMs - 16.5) / 26.0) * (0.35 + 0.45 * Math.Max(0.0, troughDepth));

            double subtropGate = Math.Max(0.0, Math.Min(1.0, (absLatDeg - 8.5) / 16.0));
            double recurveWesterlyGate = Math.Max(0.0, Math.Min(1.0, (absLatDeg - 19.0) / 11.0));
            double ridgeStrength = Math.Max(0.0, -troughDepth);
            double troughWeakness = Math.Max(0.0, aheadOfTrough) + 0.60 * Math.Max(0.0, troughDepth);

            // Physical WNW -> recurving steering:
            // - Strong subtropical ridge keeps storms on a low-latitude W/WNW track without unphysical equatorward diving;
            // - Passing Rossby trough / ridge break steers poleward (WNW -> NNW -> NE recurvature above ~20 deg).
            double synMeridionalWave = 0.55 * Math.Sin(4.0 * lon - calendarDay * 0.26 + PhaseSeed(seed, hSalt ^ 0x99u));
            steerEastMs = recurveWesterlyGate * (2.60 * Math.Max(0.0, troughDepth) + 0.22 * Math.Max(0f, upEastMs))
                        - (1.0 - 0.55 * recurveWesterlyGate) * 1.35 * ridgeStrength
                        + 0.75 * mjo1 * (1.0 - subtropGate);
            steerNorthMs = hemisphere * (0.55 + subtropGate * (2.65 * troughWeakness - 0.30 * ridgeStrength)
                         + (1.0 - subtropGate) * Math.Max(-0.25, 0.55 * synMeridionalWave + 0.35));
        }

        /// <summary>
        /// Evaluates synoptic vertical motion &amp; Lagrangian upwind condensation -> downwind post-rain depletion
        /// for cloud evolution. Persistent climate regimes (subtropical anticyclones, monsoon/ITCZ moisture cores)
        /// naturally buffer against artificial flipping.
        /// </summary>
        public static void EvaluateCloudLifecycle(
            uint seed, double calendarDay, double u, double v,
            float windEastUnit, float windNorthUnit, float rh, float rainMm,
            float oceanFrac, float thermalGrad300, float sthzRidge, float highP,
            float subJetDeg, float polJetDeg,
            out float moistUplift, out float postRainDissipation, out float synopticSlpDeltaHpa)
        {
            EvalInstantOmega(seed, calendarDay, u, v, windEastUnit, thermalGrad300, sthzRidge, highP, subJetDeg, polJetDeg,
                out float omegaNow, out synopticSlpDeltaHpa);

            // Back-trajectory (~15.6 hours = 0.65 days upwind along actual low-level wind, 1 wind unit = 777.6 km/day).
            const double lagDays = 0.65;
            double cosLat = Math.Max(0.18, Math.Cos((v - 0.5) * Math.PI));
            double du = (windEastUnit * 777.6 * lagDays) / (40030.0 * cosLat);
            double dv = (windNorthUnit * 777.6 * lagDays) / 20015.0;
            double uUp = FlatScrollMath.Wrap(u - du, 1.0);
            double vUp = FMath.Clamp((float)(v - dv), 0.01f, 0.99f);
            EvalInstantOmega(seed, calendarDay - lagDays, uUp, vUp, windEastUnit, thermalGrad300, sthzRidge, highP, subJetDeg, polJetDeg,
                out float omegaUpwindPrior, out _);

            // Moisture availability gates condensation growth; dry air cannot form cloud even in synoptic lift.
            float frontActive = FMath.Clamp((thermalGrad300 - 1.2f) / 4.3f, 0f, 1f);
            float moistAvail = FMath.Clamp((rh - 0.16f) / 0.58f, 0f, 1f);
            moistUplift = Math.Max(0f, omegaNow) * moistAvail;

            // Parcels that condensed & precipitated upwind ~15h ago and now enter neutral/subsiding air have depleted vapor
            // unless sustained low-latitude tropical monsoon / ITCZ surface moisture flux or active frontogenesis replenishes ascent.
            float absLatDeg = (float)Math.Abs((v - 0.5) * 180.0);
            float tropMonsoonBelt = 1f - FMath.Clamp((absLatDeg - 23f) / 7f, 0f, 1f);
            float monsoonReplenish = tropMonsoonBelt * FMath.Clamp((rainMm - 165f) / 130f, 0f, 1f) * FMath.Clamp((rh - 0.70f) / 0.20f, 0f, 1f) * (0.35f + 0.65f * oceanFrac);
            float frontShield = 1f - 0.60f * frontActive * moistAvail;
            float rainOutFallout = Math.Max(0f, omegaUpwindPrior - 0.50f * Math.Max(0f, omegaNow)) * moistAvail * (1f - 0.72f * monsoonReplenish) * frontShield;
            float drySubsidence = Math.Max(0f, -omegaNow) * (1f - 0.58f * monsoonReplenish) * frontShield;
            postRainDissipation = FMath.Clamp(0.82f * drySubsidence + 0.66f * rainOutFallout, 0f, 1f);
        }

        static void EvalInstantOmega(
            uint seed, double day, double u, double v, float windEastUnit,
            float thermalGrad300, float sthzRidge, float highP, float subJetDeg, float polJetDeg,
            out float omega, out float slpDeltaHpa)
        {
            double lon = (u - 0.5) * Turn;
            double lat = (v - 0.5) * Math.PI;
            float absLatDeg = (float)Math.Abs((v - 0.5) * 180.0);

            GlebaRossbyWaveField.SampleSynopticUpperFlow(
                seed, day, lat, lon, subJetDeg, polJetDeg,
                out _, out _, out float troughDepth, out float aheadOfTrough);

            // Baroclinic Rossby wave vertical motion in midlatitudes: thermal gradients amplify cyclogenetic ascent
            // (Sawyer-Eliassen frontogenesis) without turning an active thermal front into stronger ridge subsidence.
            float midGate = FMath.Clamp((absLatDeg - 20f) / 13f, 0f, 1f) * (1f - FMath.Clamp((absLatDeg - 70f) / 14f, 0f, 1f));
            float frontGrad = FMath.Clamp((thermalGrad300 - 1.2f) / 4.3f, 0f, 1f);
            float anticycloneBlock = 1f - 0.75f * sthzRidge * FMath.Clamp(0.35f + 0.65f * highP, 0f, 1f);
            float rawRossby = 0.72f * aheadOfTrough + 0.28f * troughDepth;
            float rossbyScale = rawRossby >= 0f ? (0.55f + 0.45f * frontGrad) : 0.68f;
            float rossbyOmega = midGate * anticycloneBlock * (rawRossby * rossbyScale + 0.22f * frontGrad);

            // Tropical easterly waves + MJO vertical motion in low latitudes, suppressed in subtropical gyre cores.
            float tropGate = 1f - FMath.Clamp((absLatDeg - 26f) / 10f, 0f, 1f);
            double cWave = 0.108 + 0.015 * FMath.Clamp(-windEastUnit, -0.5f, 1.2f);
            float ew = (float)(0.58 * Math.Cos(7.0 * (lon + day * cWave) + PhaseSeed(seed, 0x33a19bc7u))
                             + 0.42 * Math.Cos(9.0 * (lon + day * (cWave * 1.09)) + PhaseSeed(seed, 0x51f07e4du)));
            float mjo = (float)(0.62 * Math.Cos(lon - day * (Turn / 46.5) + PhaseSeed(seed, 0x41a7d311u))
                              + 0.38 * Math.Cos(2.0 * lon - day * (Turn / 35.2) + PhaseSeed(seed, 0x7b29c503u)));
            float tropOmega = tropGate * anticycloneBlock * (0.52f * ew + 0.34f * mjo);

            omega = FMath.Clamp(rossbyOmega + tropOmega, -1f, 1f);
            slpDeltaHpa = FMath.Clamp(-7.5f * midGate * anticycloneBlock * troughDepth - 2.8f * tropGate * (0.6f * ew + 0.4f * mjo), -12f, 12f);
        }
    }
}
