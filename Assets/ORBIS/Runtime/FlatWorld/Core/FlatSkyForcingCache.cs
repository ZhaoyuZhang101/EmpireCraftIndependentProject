using System;

namespace Orbis.Flat
{
    /// <summary>
    /// Pure-C# (no Unity dependency) climate-to-sky texture packing and cache-refresh coordinator.
    /// Keeps FlatSkyRenderer's existing GPU texture channels synchronized with seasonal ramps,
    /// interannual FlatClimateYear dipoles, daily synoptic/MJO evolution, moisture/wind/SST updates,
    /// planetary circulation presets, and cyclone track updates.
    /// </summary>
    public sealed class FlatSkyForcingCache
    {
        public const int DefaultClimateWidth = 256;
        public const int DefaultClimateHeight = 128;
        /// <summary>
        /// Refresh synoptic forcing/aux textures once per simulated day (4 six-hour weather ticks)
        /// so MJO (45-day wave), seasonal band migration, and interannual climate transitions advance
        /// smoothly without 15-day step jumps while keeping GPU uploads lightweight.
        /// </summary>
        public const double SynopticRefreshCadenceDays = 1.0;

        public readonly struct RefreshPlan
        {
            public readonly bool UploadWind;
            public readonly bool UploadClimateSegment;
            public readonly bool UploadSynopticForcing;
            public readonly bool StageCloudWind;
            public readonly bool AnySkyInputChanged;

            public RefreshPlan(bool uploadWind, bool uploadClimateSegment, bool uploadSynopticForcing, bool stageCloudWind)
            {
                UploadWind = uploadWind;
                UploadClimateSegment = uploadClimateSegment;
                UploadSynopticForcing = uploadSynopticForcing;
                StageCloudWind = stageCloudWind;
                AnySkyInputChanged = uploadWind || uploadClimateSegment || uploadSynopticForcing || stageCloudWind;
            }
        }

        public double ClimateSegmentStartDay { get; private set; } = double.NaN;
        public double WindSegmentStartDay { get; private set; } = double.NaN;
        public double SynopticDay { get; private set; } = double.NaN;
        public double VisibleWindDay { get; private set; } = double.NaN;
        public int ClimateRevision { get; private set; } = -1;
        public int WindRevision { get; private set; } = -1;
        public int VisibleWindRevision { get; private set; } = -1;
        public int WindUploadGeneration { get; private set; }
        public int VisibleWindGeneration { get; private set; } = -1;

        FlatMoistureState lastClimateMoisture, lastWindMoisture;
        FlatOceanCirculation lastOceanCurrents;
        ExoPlaSimPlanetConfig lastClimatePlanetConfig, lastWindPlanetConfig;
        ulong lastClimateFingerprint, lastWindFingerprint;

        public void InvalidateClimate()
        {
            ClimateSegmentStartDay = double.NaN;
            SynopticDay = double.NaN;
            ClimateRevision = -1;
            lastClimatePlanetConfig = null;
            lastClimateFingerprint = 0;
        }

        public void ResetAll()
        {
            InvalidateClimate();
            WindSegmentStartDay = double.NaN;
            VisibleWindDay = double.NaN;
            WindRevision = -1;
            VisibleWindRevision = -1;
            VisibleWindGeneration = -1;
            lastClimateMoisture = null;
            lastWindMoisture = null;
            lastOceanCurrents = null;
            lastWindPlanetConfig = null;
            lastWindFingerprint = 0;
        }

        /// <summary>
        /// Evaluates which cached textures need refreshing for the current world state and calendar day.
        /// Detects 15-day half-month segment crossings, daily synoptic cadence, TerrainRevision increments,
        /// replaced Moisture/OceanCurrents/PlanetClimate instances, and in-place climate data mutations.
        /// </summary>
        public RefreshPlan Evaluate(WorldData world, double day, bool cloudsNeeded)
        {
            if (world == null) return default;
            var season = new FlatMonthlyForcing(day);
            ulong windFp = ComputeWindFingerprint(world);
            bool windSourceChanged =
                WindRevision != world.TerrainRevision ||
                !ReferenceEquals(lastWindMoisture, world.Moisture) ||
                !ReferenceEquals(lastWindPlanetConfig, world.PlanetClimate) ||
                windFp != lastWindFingerprint;
            bool uploadWind = windSourceChanged || WindSegmentStartDay != season.SegmentStartDay;

            if (!cloudsNeeded)
                return new RefreshPlan(uploadWind, false, false, false);

            ulong climateFp = ComputeClimateFingerprint(world);
            bool climateSourceChanged =
                ClimateRevision != world.TerrainRevision ||
                !ReferenceEquals(lastClimateMoisture, world.Moisture) ||
                !ReferenceEquals(lastOceanCurrents, world.Moisture?.OceanCurrents) ||
                !ReferenceEquals(lastClimatePlanetConfig, world.PlanetClimate) ||
                climateFp != lastClimateFingerprint;
            bool uploadClimateSegment = climateSourceChanged || ClimateSegmentStartDay != season.SegmentStartDay;
            bool uploadSynopticForcing = uploadClimateSegment ||
                double.IsNaN(SynopticDay) ||
                Math.Abs(day - SynopticDay) >= SynopticRefreshCadenceDays - 1e-6;
            bool stageCloudWind = uploadWind ||
                day != VisibleWindDay ||
                world.TerrainRevision != VisibleWindRevision ||
                WindUploadGeneration != VisibleWindGeneration;

            return new RefreshPlan(uploadWind, uploadClimateSegment, uploadSynopticForcing, stageCloudWind);
        }

        public void MarkWindUploaded(WorldData world, FlatMonthlyForcing season)
        {
            WindSegmentStartDay = season.SegmentStartDay;
            WindRevision = world.TerrainRevision;
            lastWindMoisture = world.Moisture;
            lastWindPlanetConfig = world.PlanetClimate;
            lastWindFingerprint = ComputeWindFingerprint(world);
            WindUploadGeneration++;
        }

        public void MarkClimateSegmentUploaded(WorldData world, FlatMonthlyForcing season, double synopticDay)
        {
            ClimateSegmentStartDay = season.SegmentStartDay;
            SynopticDay = synopticDay;
            ClimateRevision = world.TerrainRevision;
            lastClimateMoisture = world.Moisture;
            lastOceanCurrents = world.Moisture?.OceanCurrents;
            lastClimatePlanetConfig = world.PlanetClimate;
            lastClimateFingerprint = ComputeClimateFingerprint(world);
        }

        public void MarkSynopticUploaded(double synopticDay)
        {
            SynopticDay = synopticDay;
        }

        public void MarkCloudWindPublished(double windDay, int terrainRevision, int windGeneration)
        {
            VisibleWindDay = windDay;
            VisibleWindRevision = terrainRevision;
            VisibleWindGeneration = windGeneration;
        }

        /// <summary>
        /// Cheap deterministic O(1) sampled fingerprint over climate/moisture/SST/CAPE/rotation fields so
        /// controlled or runtime mutations without a full TerrainRevision bump still invalidate the sky cache.
        /// </summary>
        public static ulong ComputeClimateFingerprint(WorldData world)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                void MixFloat(float v)
                {
                    ulong bits = (ulong)BitConverter.DoubleToInt64Bits(v);
                    h = (h ^ bits) * 1099511628211UL;
                }
                void MixArray(float[] arr)
                {
                    if (arr == null || arr.Length == 0) { h = (h ^ 0x55AAUL) * 1099511628211UL; return; }
                    int len = arr.Length;
                    h = (h ^ (uint)len) * 1099511628211UL;
                    // Coprime to 2 and 3 (hence coprime to 12 months, 128 rows, 256 cols) so samples sweep all longitudes/latitudes/months.
                    int stride = Math.Max(1, len / 192);
                    if ((stride & 1) == 0) stride++;
                    if (stride % 3 == 0) stride += 2;
                    for (int i = 0; i < len; i += stride) MixFloat(arr[i]);
                    MixFloat(arr[len - 1]);
                }

                h = (h ^ (uint)world.TerrainRevision) * 1099511628211UL;
                h = (h ^ FMath.Seed(world.Seed)) * 1099511628211UL;
                MixArray(world.Precipitation);
                MixArray(world.Temperature);
                var m = world.Moisture;
                if (m != null)
                {
                    MixArray(m.Humidity);
                    MixArray(m.Condensation);
                    MixArray(m.East);
                    MixArray(m.North);
                    if (m.OceanCurrents != null) MixArray(m.OceanCurrents.SeaAnomaly);
                    if (m.Gleba != null)
                    {
                        MixArray(m.Gleba.PressureHpa);
                        MixArray(m.Gleba.RainfallImpact);
                        MixArray(m.Gleba.RainShadowiness);
                        MixArray(m.Gleba.CurrentNorth);
                    }
                    if (m.ExoPlaSim != null)
                    {
                        MixArray(m.ExoPlaSim.CapeJkg);
                        MixArray(m.ExoPlaSim.EntropyDeficit);
                        MixArray(m.ExoPlaSim.ConvectiveRain);
                        MixArray(m.ExoPlaSim.StratiformRain);
                        MixArray(m.ExoPlaSim.VirgaReevap);
                        MixArray(m.ExoPlaSim.DeepSoil);
                        MixArray(m.ExoPlaSim.ShallowSoil);
                    }
                }
                var cfg = world.Moisture?.ExoPlaSim?.Config ?? world.PlanetClimate;
                if (cfg != null)
                {
                    MixFloat(cfg.RotationRateRatio);
                    MixFloat(cfg.ObliquityDeg);
                    MixFloat(cfg.Co2Ppm);
                    MixFloat(cfg.SolarConstantWm2);
                    MixFloat(cfg.TidallyLocked ? 1f : 0f);
                    MixFloat(cfg.SubstellarLongitudeDeg);
                }
                return h;
            }
        }

        public static ulong ComputeWindFingerprint(WorldData world)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                void MixFloat(float v)
                {
                    ulong bits = (ulong)BitConverter.DoubleToInt64Bits(v);
                    h = (h ^ bits) * 1099511628211UL;
                }
                void MixArray(float[] arr)
                {
                    if (arr == null || arr.Length == 0) { h = (h ^ 0x33CCUL) * 1099511628211UL; return; }
                    int len = arr.Length;
                    h = (h ^ (uint)len) * 1099511628211UL;
                    int stride = Math.Max(1, len / 192);
                    if ((stride & 1) == 0) stride++;
                    if (stride % 3 == 0) stride += 2;
                    for (int i = 0; i < len; i += stride) MixFloat(arr[i]);
                    MixFloat(arr[len - 1]);
                }

                h = (h ^ (uint)world.TerrainRevision) * 1099511628211UL;
                var m = world.Moisture;
                if (m != null)
                {
                    MixArray(m.East);
                    MixArray(m.North);
                }
                var cfg = world.Moisture?.ExoPlaSim?.Config ?? world.PlanetClimate;
                if (cfg != null)
                {
                    MixFloat(cfg.RotationRateRatio);
                    MixFloat(cfg.ObliquityDeg);
                    MixFloat(cfg.TidallyLocked ? 1f : 0f);
                }
                return h;
            }
        }

        /// <summary>
        /// Packs `_ClimateTex` RGBA32 bytes for a 15-day half-month segment:
        /// R = start wet, G = end wet, B = start temperature, A = end temperature (all with FlatClimateYear).
        /// Also returns the averaged south and north pole RGBA values in [0, 1].
        /// </summary>
        public static void PackMonthlyClimateRgba(
            WorldData world, FlatMonthlyForcing season, int width, int height,
            byte[] dstRgba, float[] southPoleRgba = null, float[] northPoleRgba = null)
        {
            if (world == null || dstRgba == null || dstRgba.Length < width * height * 4) return;
            var start = new FlatMonthlyForcing(season.SegmentStartDay);
            double endDay = season.SegmentStartDay + FlatMonthlyForcing.HalfMonthDays;
            var end = new FlatMonthlyForcing(endDay);
            uint seed = FMath.Seed(world.Seed);
            var currentYear = FlatClimateYear.At(seed, season.SegmentStartDay);
            var followingYear = FlatClimateYear.At(seed, endDay);

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int sx = (int)((x + .5f) * world.Width / width);
                int sy = (int)((y + .5f) * world.Height / height);
                float rain = 0f, rainNext = 0f, temp = 0f, tempNext = 0f;
                for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int offset = world.Index(sx + dx, FMath.Clamp(sy + dy, 0, world.Height - 1)) * 12;
                    rain += start.Sample(world.Precipitation, offset, ClimateRange.Nonnegative);
                    rainNext += end.Sample(world.Precipitation, offset, ClimateRange.Nonnegative);
                    temp += start.Sample(world.Temperature, offset);
                    tempNext += end.Sample(world.Temperature, offset);
                }
                var p = FlatWeatherSimulation.Direction((x + .5) / width, (y + .5) / height);
                currentYear.Offsets((float)p.X, (float)p.Y, (float)p.Z, out float degrees, out float rainDelta);
                followingYear.Offsets((float)p.X, (float)p.Y, (float)p.Z, out float nextDegrees, out float nextRainDelta);

                int p4 = (y * width + x) * 4;
                dstRgba[p4] = ToByte(FMath.Clamp(rain * (1f + rainDelta) / 880f, 0f, 1f));
                dstRgba[p4 + 1] = ToByte(FMath.Clamp(rainNext * (1f + nextRainDelta) / 880f, 0f, 1f));
                dstRgba[p4 + 2] = ToByte(FMath.Clamp((temp * .25f + degrees + 25f) / 65f, 0f, 1f));
                dstRgba[p4 + 3] = ToByte(FMath.Clamp((tempNext * .25f + nextDegrees + 25f) / 65f, 0f, 1f));
            }

            if (southPoleRgba != null && southPoleRgba.Length >= 4 && northPoleRgba != null && northPoleRgba.Length >= 4)
            {
                double sr = 0, sg = 0, sb = 0, sa = 0, nr = 0, ng = 0, nb = 0, na = 0;
                int topRow = (height - 1) * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int b0 = x * 4, b1 = topRow + x * 4;
                    sr += dstRgba[b0]; sg += dstRgba[b0 + 1]; sb += dstRgba[b0 + 2]; sa += dstRgba[b0 + 3];
                    nr += dstRgba[b1]; ng += dstRgba[b1 + 1]; nb += dstRgba[b1 + 2]; na += dstRgba[b1 + 3];
                }
                double inv = 1.0 / (255.0 * width);
                southPoleRgba[0] = (float)(sr * inv); southPoleRgba[1] = (float)(sg * inv);
                southPoleRgba[2] = (float)(sb * inv); southPoleRgba[3] = (float)(sa * inv);
                northPoleRgba[0] = (float)(nr * inv); northPoleRgba[1] = (float)(ng * inv);
                northPoleRgba[2] = (float)(nb * inv); northPoleRgba[3] = (float)(na * inv);
            }
        }

        /// <summary>
        /// Packs `_ClimateWindTex` RGBA floats (R=eastStart, G=northStart, B=eastEnd, A=northEnd)
        /// for a 15-day segment. Falls back to planetary FlatWindBelts when Moisture is unavailable.
        /// </summary>
        public static void PackSeasonalWindRgba(
            WorldData world, FlatMonthlyForcing season, int width, int height, float[] dstRgba)
        {
            if (world == null || dstRgba == null || dstRgba.Length < width * height * 4) return;
            var state = world.Moisture;
            var start = new FlatMonthlyForcing(season.SegmentStartDay);
            var end = new FlatMonthlyForcing(season.SegmentStartDay + FlatMonthlyForcing.HalfMonthDays);
            var planet = world.PlanetClimate;
            float seasonStart = (float)Math.Cos((start.SeasonalMonth - 6f) * Math.PI / 6.0);
            float seasonEnd = (float)Math.Cos((end.SeasonalMonth - 6f) * Math.PI / 6.0);

            for (int y = 0; y < height; y++)
            {
                float v = (y + .5f) / height;
                float latDeg = (v - .5f) * 180f;
                FlatWindBelts beltsStart = state == null ? new FlatWindBelts(latDeg, seasonStart, planet) : default;
                FlatWindBelts beltsEnd = state == null ? new FlatWindBelts(latDeg, seasonEnd, planet) : default;
                float fallbackE0 = 0f, fallbackN0 = 0f, fallbackE1 = 0f, fallbackN1 = 0f;
                if (state == null)
                {
                    beltsStart.Surface(6f * seasonStart, 0.7f, out fallbackE0, out fallbackN0);
                    beltsEnd.Surface(6f * seasonEnd, 0.7f, out fallbackE1, out fallbackN1);
                    fallbackE0 /= 0.32f; fallbackN0 /= 0.32f;
                    fallbackE1 /= 0.32f; fallbackN1 /= 0.32f;
                }
                for (int x = 0; x < width; x++)
                {
                    float u = (x + .5f) / width;
                    float east = state == null ? fallbackE0 : start.Sample(state, state.East, u, v);
                    float north = state == null ? fallbackN0 : start.Sample(state, state.North, u, v);
                    float nextEast = state == null ? fallbackE1 : end.Sample(state, state.East, u, v);
                    float nextNorth = state == null ? fallbackN1 : end.Sample(state, state.North, u, v);
                    int p4 = (y * width + x) * 4;
                    dstRgba[p4] = east;
                    dstRgba[p4 + 1] = north;
                    dstRgba[p4 + 2] = nextEast;
                    dstRgba[p4 + 3] = nextNorth;
                }
            }
        }

        /// <summary>
        /// Projects interpolated surface wind (east, north) onto 3D unit-sphere tangent velocity for `_ScrollCloudWindTex`,
        /// matching FlatSkyRenderer.StageCloudWind (scale 0.014, clamp 0.025, pole-sealed).
        /// </summary>
        public static void PackCloudDriftVelocityRgba(
            float[] seasonalWindRgba, float windBlend, int width, int height, float[] dstVelocityRgba)
        {
            if (seasonalWindRgba == null || dstVelocityRgba == null) return;
            int count = width * height;
            if (seasonalWindRgba.Length < count * 4 || dstVelocityRgba.Length < count * 4) return;

            var sinLon = new float[width];
            var cosLon = new float[width];
            for (int x = 0; x < width; x++)
            {
                double lon = ((x + .5) / width - .5) * 2.0 * Math.PI;
                sinLon[x] = (float)Math.Sin(lon);
                cosLon[x] = (float)Math.Cos(lon);
            }
            for (int y = 0; y < height; y++)
            {
                double lat = ((y + .5) / height - .5) * Math.PI;
                float sinLat = (float)Math.Sin(lat), cosLat = (float)Math.Cos(lat);
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int p4 = (row + x) * 4;
                    float east = FMath.Lerp(seasonalWindRgba[p4], seasonalWindRgba[p4 + 2], windBlend);
                    float north = FMath.Lerp(seasonalWindRgba[p4 + 1], seasonalWindRgba[p4 + 3], windBlend);
                    float vx = (east * cosLon[x] - north * sinLat * sinLon[x]) * .014f;
                    float vy = (north * cosLat) * .014f;
                    float vz = (-east * sinLon[x] - north * sinLat * cosLon[x]) * .014f;
                    float len = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    if (len > .025f)
                    {
                        float s = .025f / len;
                        vx *= s; vy *= s; vz *= s;
                    }
                    dstVelocityRgba[p4] = vx;
                    dstVelocityRgba[p4 + 1] = vy;
                    dstVelocityRgba[p4 + 2] = vz;
                    dstVelocityRgba[p4 + 3] = 0f;
                }
            }
            SealPoleRgba(dstVelocityRgba, width, height);
        }

        static void SealPoleRgba(float[] rgba, int width, int height)
        {
            for (int side = 0; side < 2; side++)
            {
                int rowStart = (side == 0 ? 0 : (height - 1) * width) * 4;
                double mx = 0, my = 0, mz = 0, mw = 0;
                for (int x = 0; x < width; x++)
                {
                    int p4 = rowStart + x * 4;
                    mx += rgba[p4]; my += rgba[p4 + 1]; mz += rgba[p4 + 2]; mw += rgba[p4 + 3];
                }
                float fx = (float)(mx / width), fy = (float)(my / width), fz = (float)(mz / width), fw = (float)(mw / width);
                for (int x = 0; x < width; x++)
                {
                    int p4 = rowStart + x * 4;
                    rgba[p4] = fx; rgba[p4 + 1] = fy; rgba[p4 + 2] = fz; rgba[p4 + 3] = fw;
                }
            }
        }

        /// <summary>
        /// Returns true when two cyclone tracks have identical serialized kinematic state.
        /// </summary>
        public static bool SameCycloneTrack(in FlatCycloneTrack a, in FlatCycloneTrack b) =>
            a.Tracked == b.Tracked && a.U == b.U && a.V == b.V &&
            a.PressureDeficit == b.PressureDeficit && a.WindSpeed == b.WindSpeed &&
            a.Phase == b.Phase;

        static byte ToByte(float unit) => (byte)FMath.Clamp((int)Math.Round(unit * 255f), 0, 255);
    }
}
