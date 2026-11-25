using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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

        public void InvalidateSynoptic()
        {
            SynopticDay = double.NaN;
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
            ulong windFp = ComputeWindFingerprint(world);
            ulong climateFp = cloudsNeeded ? ComputeClimateFingerprint(world) : 0UL;
            return Evaluate(world, day, cloudsNeeded, windFp, climateFp);
        }

        internal RefreshPlan Evaluate(WorldData world, double day, bool cloudsNeeded, ulong windFp, ulong climateFp)
        {
            if (world == null) return default;
            var season = new FlatMonthlyForcing(day);
            bool windSourceChanged =
                WindRevision != world.TerrainRevision ||
                !ReferenceEquals(lastWindMoisture, world.Moisture) ||
                !ReferenceEquals(lastWindPlanetConfig, world.PlanetClimate) ||
                windFp != lastWindFingerprint;
            bool uploadWind = windSourceChanged || WindSegmentStartDay != season.SegmentStartDay;

            if (!cloudsNeeded)
                return new RefreshPlan(uploadWind, false, false, false);

            bool climateSourceChanged =
                ClimateRevision != world.TerrainRevision ||
                !ReferenceEquals(lastClimateMoisture, world.Moisture) ||
                !ReferenceEquals(lastOceanCurrents, world.Moisture?.OceanCurrents) ||
                !ReferenceEquals(lastClimatePlanetConfig, world.PlanetClimate) ||
                climateFp != lastClimateFingerprint;
            bool uploadClimateSegment = climateSourceChanged || ClimateSegmentStartDay != season.SegmentStartDay;
            bool uploadSynopticForcing = uploadClimateSegment ||
                double.IsNaN(SynopticDay) ||
                day < SynopticDay - 1e-6 ||
                (day - SynopticDay) >= SynopticRefreshCadenceDays - 1e-6;
            bool stageCloudWind = uploadWind ||
                day != VisibleWindDay ||
                world.TerrainRevision != VisibleWindRevision ||
                WindUploadGeneration != VisibleWindGeneration;

            return new RefreshPlan(uploadWind, uploadClimateSegment, uploadSynopticForcing, stageCloudWind);
        }

        public void MarkWindUploaded(WorldData world, FlatMonthlyForcing season)
            => MarkWindUploaded(world, season, ComputeWindFingerprint(world));

        internal void MarkWindUploaded(WorldData world, FlatMonthlyForcing season, ulong windFp)
        {
            WindSegmentStartDay = season.SegmentStartDay;
            WindRevision = world.TerrainRevision;
            lastWindMoisture = world.Moisture;
            lastWindPlanetConfig = world.PlanetClimate;
            lastWindFingerprint = windFp;
            WindUploadGeneration++;
        }

        public void MarkClimateSegmentUploaded(WorldData world, FlatMonthlyForcing season, double synopticDay)
            => MarkClimateSegmentUploaded(world, season, synopticDay, ComputeClimateFingerprint(world));

        internal void MarkClimateSegmentUploaded(WorldData world, FlatMonthlyForcing season, double synopticDay, ulong climateFp)
        {
            ClimateSegmentStartDay = season.SegmentStartDay;
            SynopticDay = synopticDay;
            ClimateRevision = world.TerrainRevision;
            lastClimateMoisture = world.Moisture;
            lastOceanCurrents = world.Moisture?.OceanCurrents;
            lastClimatePlanetConfig = world.PlanetClimate;
            lastClimateFingerprint = climateFp;
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
            => PackMonthlyClimateRgba(world, season, width, height, dstRgba, southPoleRgba, northPoleRgba, null, CancellationToken.None);

        internal static void PackMonthlyClimateRgba(
            WorldData world, FlatMonthlyForcing season, int width, int height,
            byte[] dstRgba, float[] southPoleRgba, float[] northPoleRgba,
            GlebaGeographySupplement.CloudSynopticWorkspace workspace, CancellationToken cancel)
        {
            if (world == null || dstRgba == null || dstRgba.Length < width * height * 4) return;
            cancel.ThrowIfCancellationRequested();
            var start = new FlatMonthlyForcing(season.SegmentStartDay);
            double endDay = season.SegmentStartDay + FlatMonthlyForcing.HalfMonthDays;
            var end = new FlatMonthlyForcing(endDay);
            uint seed = FMath.Seed(world.Seed);
            var currentYear = FlatClimateYear.At(seed, season.SegmentStartDay);
            var followingYear = FlatClimateYear.At(seed, endDay);
            bool hasTrig = workspace != null && workspace.Width == width && workspace.Height == height;
            int worldW = world.Width, worldH = world.Height;
            var precip = world.Precipitation;
            var tempArr = world.Temperature;

            for (int y = 0; y < height; y++)
            {
                if ((y & 15) == 0) cancel.ThrowIfCancellationRequested();
                int sy = (int)((y + .5f) * worldH / height);
                int sy1 = Math.Min(sy + 1, worldH - 1);
                double sinLat = hasTrig ? workspace.SinLatRow[y] : Math.Sin(((y + .5) / height - .5) * Math.PI);
                double cosLat = hasTrig ? workspace.CosLatRow[y] : Math.Cos(((y + .5) / height - .5) * Math.PI);
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int sx = (int)((x + .5f) * worldW / width);
                    int sx0 = FMath.Wrap(sx, worldW);
                    int sx1 = sx0 + 1 == worldW ? 0 : sx0 + 1;
                    int o00 = (sy * worldW + sx0) * 12;
                    int o10 = (sy * worldW + sx1) * 12;
                    int o01 = (sy1 * worldW + sx0) * 12;
                    int o11 = (sy1 * worldW + sx1) * 12;
                    float rain = start.Sample(precip, o00, ClimateRange.Nonnegative)
                               + start.Sample(precip, o10, ClimateRange.Nonnegative)
                               + start.Sample(precip, o01, ClimateRange.Nonnegative)
                               + start.Sample(precip, o11, ClimateRange.Nonnegative);
                    float rainNext = end.Sample(precip, o00, ClimateRange.Nonnegative)
                                   + end.Sample(precip, o10, ClimateRange.Nonnegative)
                                   + end.Sample(precip, o01, ClimateRange.Nonnegative)
                                   + end.Sample(precip, o11, ClimateRange.Nonnegative);
                    float temp = start.Sample(tempArr, o00) + start.Sample(tempArr, o10)
                               + start.Sample(tempArr, o01) + start.Sample(tempArr, o11);
                    float tempNext = end.Sample(tempArr, o00) + end.Sample(tempArr, o10)
                                   + end.Sample(tempArr, o01) + end.Sample(tempArr, o11);
                    float px, py, pz;
                    if (hasTrig)
                    {
                        px = (float)(workspace.SinLonCol[x] * cosLat);
                        py = (float)sinLat;
                        pz = (float)(workspace.CosLonCol[x] * cosLat);
                    }
                    else
                    {
                        var p = FlatWeatherSimulation.Direction((x + .5) / width, (y + .5) / height);
                        px = (float)p.X; py = (float)p.Y; pz = (float)p.Z;
                    }
                    currentYear.Offsets(px, py, pz, out float degrees, out float rainDelta);
                    followingYear.Offsets(px, py, pz, out float nextDegrees, out float nextRainDelta);

                    int p4 = (row + x) * 4;
                    dstRgba[p4] = ToByte(FMath.Clamp(rain * (1f + rainDelta) / 880f, 0f, 1f));
                    dstRgba[p4 + 1] = ToByte(FMath.Clamp(rainNext * (1f + nextRainDelta) / 880f, 0f, 1f));
                    dstRgba[p4 + 2] = ToByte(FMath.Clamp((temp * .25f + degrees + 25f) / 65f, 0f, 1f));
                    dstRgba[p4 + 3] = ToByte(FMath.Clamp((tempNext * .25f + nextDegrees + 25f) / 65f, 0f, 1f));
                }
            }

            ComputeRgbaPoles(dstRgba, width, height, southPoleRgba, northPoleRgba);
        }

        public static void ComputeRgbaPoles(byte[] rgba, int width, int height, float[] southPoleRgba, float[] northPoleRgba)
        {
            if (rgba == null || southPoleRgba == null || northPoleRgba == null || southPoleRgba.Length < 4 || northPoleRgba.Length < 4) return;
            double sr = 0, sg = 0, sb = 0, sa = 0, nr = 0, ng = 0, nb = 0, na = 0;
            int topRow = (height - 1) * width * 4;
            for (int x = 0; x < width; x++)
            {
                int b0 = x * 4, b1 = topRow + x * 4;
                sr += rgba[b0]; sg += rgba[b0 + 1]; sb += rgba[b0 + 2]; sa += rgba[b0 + 3];
                nr += rgba[b1]; ng += rgba[b1 + 1]; nb += rgba[b1 + 2]; na += rgba[b1 + 3];
            }
            double inv = 1.0 / (255.0 * width);
            southPoleRgba[0] = (float)(sr * inv); southPoleRgba[1] = (float)(sg * inv);
            southPoleRgba[2] = (float)(sb * inv); southPoleRgba[3] = (float)(sa * inv);
            northPoleRgba[0] = (float)(nr * inv); northPoleRgba[1] = (float)(ng * inv);
            northPoleRgba[2] = (float)(nb * inv); northPoleRgba[3] = (float)(na * inv);
        }

        /// <summary>
        /// Packs `_ClimateWindTex` RGBA floats (R=eastStart, G=northStart, B=eastEnd, A=northEnd)
        /// for a 15-day segment. Falls back to planetary FlatWindBelts when Moisture is unavailable.
        /// </summary>
        public static void PackSeasonalWindRgba(
            WorldData world, FlatMonthlyForcing season, int width, int height, float[] dstRgba)
            => PackSeasonalWindRgba(world, season, width, height, dstRgba, CancellationToken.None);

        internal static void PackSeasonalWindRgba(
            WorldData world, FlatMonthlyForcing season, int width, int height, float[] dstRgba, CancellationToken cancel)
        {
            if (world == null || dstRgba == null || dstRgba.Length < width * height * 4) return;
            cancel.ThrowIfCancellationRequested();
            var state = world.Moisture;
            var start = new FlatMonthlyForcing(season.SegmentStartDay);
            var end = new FlatMonthlyForcing(season.SegmentStartDay + FlatMonthlyForcing.HalfMonthDays);
            var planet = world.PlanetClimate;
            float seasonStart = (float)Math.Cos((start.SeasonalMonth - 6f) * Math.PI / 6.0);
            float seasonEnd = (float)Math.Cos((end.SeasonalMonth - 6f) * Math.PI / 6.0);

            for (int y = 0; y < height; y++)
            {
                if ((y & 15) == 0) cancel.ThrowIfCancellationRequested();
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

    /// <summary>
    /// Reusable pre-allocated container for a completed background sky-forcing build.
    /// Owned exclusively by the worker while in flight and swapped to the main thread upon commit.
    /// </summary>
    public sealed class FlatSkyForcingPayload
    {
        public readonly int Width, Height;
        public readonly byte[] MonthlyClimateRgba;
        public readonly float[] ClimatePoleSouth = new float[4], ClimatePoleNorth = new float[4];
        public readonly byte[] ForcingRgba, AuxRgba;
        public readonly float[] ForcingPoleSouth = new float[4], ForcingPoleNorth = new float[4];
        public readonly ushort[] UpperWindHalf;
        public readonly float[] SeasonalWindRgba;

        public float HadleyRad, PolarRad, RossbyWaveK, RegimeCode;
        public bool HasClimateSegment, HasSynopticForcing, HasSeasonalWind;
        public double SegmentStartDay, SynopticDay;
        public double WorkerDurationMs;

        internal WorldData World;
        internal int TerrainRevision;
        internal FlatMoistureState Moisture;
        internal FlatOceanCirculation OceanCurrents;
        internal ExoPlaSimPlanetConfig PlanetConfig;
        internal ulong ClimateFingerprint, WindFingerprint;
        internal long Epoch, RequestId;

        public FlatSkyForcingPayload(int width, int height)
        {
            Width = width;
            Height = height;
            int rgbaLen = width * height * 4;
            MonthlyClimateRgba = new byte[rgbaLen];
            ForcingRgba = new byte[rgbaLen];
            AuxRgba = new byte[rgbaLen];
            UpperWindHalf = new ushort[rgbaLen];
            SeasonalWindRgba = new float[rgbaLen];
        }
    }

    /// <summary>
    /// Non-blocking single-worker background producer for FlatSkyRenderer's daily synoptic forcing and
    /// 15-day climate/wind segments. Reuses two output buffers and one CloudSynopticWorkspace (zero per-refresh
    /// large allocations), coalesces superseded fast-forward requests, and rejects stale completions across
    /// terrain edits, world switches, date rewinds, and cloud hide/resume transitions.
    /// </summary>
    public sealed class FlatSkyForcingScheduler : IDisposable
    {
        readonly struct JobSpec
        {
            public readonly WorldData World;
            public readonly int TerrainRevision;
            public readonly FlatMoistureState Moisture;
            public readonly FlatOceanCirculation OceanCurrents;
            public readonly ExoPlaSimPlanetConfig PlanetConfig;
            public readonly ulong ClimateFingerprint, WindFingerprint;
            public readonly long Epoch, RequestId;
            public readonly double SegmentStartDay, SynopticDay;
            public readonly bool IncludeClimateSegment, IncludeSynopticForcing, IncludeSeasonalWind;

            public JobSpec(
                WorldData world, ulong climateFp, ulong windFp, long epoch, long requestId,
                double segmentStartDay, double synopticDay,
                bool includeClimateSegment, bool includeSynopticForcing, bool includeSeasonalWind)
            {
                World = world;
                TerrainRevision = world.TerrainRevision;
                Moisture = world.Moisture;
                OceanCurrents = world.Moisture?.OceanCurrents;
                PlanetConfig = world.PlanetClimate;
                ClimateFingerprint = climateFp;
                WindFingerprint = windFp;
                Epoch = epoch;
                RequestId = requestId;
                SegmentStartDay = segmentStartDay;
                SynopticDay = synopticDay;
                IncludeClimateSegment = includeClimateSegment;
                IncludeSynopticForcing = includeSynopticForcing;
                IncludeSeasonalWind = includeSeasonalWind;
            }
        }

        public readonly int Width, Height;
        readonly FlatSkyForcingCache cache = new FlatSkyForcingCache();
        readonly GlebaGeographySupplement.CloudSynopticWorkspace workspace = new GlebaGeographySupplement.CloudSynopticWorkspace();
        FlatSkyForcingPayload mainBuffer, workerBuffer;

        WorldData boundWorld;
        double lastObservedDay = double.NaN;
        double lastCoalescedDay = double.NaN;
        long epoch, nextRequestId;
        bool cloudsWereHidden;
        bool disposed;

        Task activeTask;
        CancellationTokenSource activeCts;
        JobSpec activeSpec;
        Task drainingTask;

        // Optional deterministic test hook executed inside the worker before compute (null in production).
        internal Action<CancellationToken> BeforeWorkerComputeHook { get; set; }

        public FlatSkyForcingCache Cache => cache;
        public bool HasPublished { get; private set; }
        public bool IsComputing => activeTask != null && !activeTask.IsCompleted;
        public bool IsDraining => drainingTask != null && !drainingTask.IsCompleted;
        public bool HasInFlightWork => activeTask != null || (drainingTask != null && !drainingTask.IsCompleted);
        public double PublishedSynopticDay => cache.SynopticDay;
        public double PublishedSegmentStartDay => cache.ClimateSegmentStartDay;

        /// <summary>
        /// True when the already-uploaded GPU climate/forcing segment belongs to the current world,
        /// terrain revision, and 15-day half-month segment (so FlatSkyBake will never mix mismatched segments).
        /// </summary>
        public bool IsSegmentCurrent(WorldData world, double day)
        {
            if (!HasPublished || world == null || !ReferenceEquals(world, boundWorld)) return false;
            if (cache.ClimateRevision != world.TerrainRevision) return false;
            var season = new FlatMonthlyForcing(day);
            return cache.ClimateSegmentStartDay == season.SegmentStartDay;
        }
        public long CurrentEpoch => epoch;
        public int JobsStarted { get; private set; }
        public int JobsCompleted { get; private set; }
        public int JobsAccepted { get; private set; }
        public int JobsDiscardedStale { get; private set; }
        public int JobsCancelled { get; private set; }
        public int RequestsCoalesced { get; private set; }
        public double LastWorkerMs { get; private set; }
        public double MaxWorkerMs { get; private set; }
        public Exception LastError { get; private set; }

        public FlatSkyForcingScheduler(int width = FlatSkyForcingCache.DefaultClimateWidth, int height = FlatSkyForcingCache.DefaultClimateHeight)
        {
            Width = width;
            Height = height;
            mainBuffer = new FlatSkyForcingPayload(width, height);
            workerBuffer = new FlatSkyForcingPayload(width, height);
        }

        public void Bind(WorldData world)
        {
            CancelActiveAndBumpEpoch();
            cache.ResetAll();
            boundWorld = world;
            lastObservedDay = double.NaN;
            lastCoalescedDay = double.NaN;
            HasPublished = false;
            cloudsWereHidden = false;
            LastError = null;
        }

        public void MarkInitialWindUploaded(WorldData world, FlatMonthlyForcing season)
        {
            if (world == null) return;
            cache.MarkWindUploaded(world, season);
        }

        public void MarkCloudWindPublished(double windDay, int terrainRevision, int windGeneration)
            => cache.MarkCloudWindPublished(windDay, terrainRevision, windGeneration);

        public void NotifyCloudsHidden()
        {
            if (!cloudsWereHidden)
            {
                cloudsWereHidden = true;
                CancelActiveAndBumpEpoch();
            }
        }

        public void NotifyCloudsResumed()
        {
            if (cloudsWereHidden)
            {
                cloudsWereHidden = false;
                CancelActiveAndBumpEpoch();
            }
        }

        /// <summary>
        /// Proactively schedules the first background forcing job (e.g. during Initialize) so initial
        /// cloud textures warm up off-thread without stalling the first visible frame.
        /// </summary>
        public void Prefetch(WorldData world, double day)
        {
            if (disposed || world == null) return;
            Poll(world, day, cloudsNeeded: true, allowCommit: false, out _, out _);
        }

        /// <summary>
        /// Checks whether an in-flight multi-frame column strip bake must be aborted because the world,
        /// terrain/climate provenance, 15-day segment, or calendar direction changed.
        /// Normal intra-segment daily refreshes do NOT abort an in-flight strip.
        /// </summary>
        public bool ShouldAbortPendingStrip(WorldData world, double day)
        {
            if (world == null) return false;
            if (!ReferenceEquals(world, boundWorld)) return true;
            if (!double.IsNaN(lastObservedDay) && day < lastObservedDay - 1e-9) return true;
            var season = new FlatMonthlyForcing(day);
            if (!double.IsNaN(cache.ClimateSegmentStartDay) && cache.ClimateSegmentStartDay != season.SegmentStartDay) return true;
            if (HasPublished && cache.ClimateRevision != world.TerrainRevision) return true;
            ulong windFp = FlatSkyForcingCache.ComputeWindFingerprint(world);
            ulong climateFp = FlatSkyForcingCache.ComputeClimateFingerprint(world);
            var plan = cache.Evaluate(world, day, cloudsNeeded: true, windFp, climateFp);
            return plan.UploadClimateSegment;
        }

        /// <summary>
        /// Main-thread non-blocking tick:
        /// - Detects world switch, rewind, terrain/climate mutations, and segment crossings.
        /// - Validates and commits any completed background result when <paramref name="allowCommit"/> is true (zero copies).
        /// - Coalesces superseded fast-forward requests and schedules at most one background worker.
        /// </summary>
        public bool Poll(
            WorldData world, double day, bool cloudsNeeded, bool allowCommit,
            out FlatSkyForcingPayload readyPayload, out bool hiddenWindNeedsSync)
        {
            readyPayload = null;
            hiddenWindNeedsSync = false;
            if (disposed || world == null) return false;

            if (drainingTask != null && drainingTask.IsCompleted)
                drainingTask = null;

            if (!ReferenceEquals(world, boundWorld))
                Bind(world);

            var season = new FlatMonthlyForcing(day);
            ulong windFp = FlatSkyForcingCache.ComputeWindFingerprint(world);

            if (!cloudsNeeded)
            {
                NotifyCloudsHidden();
                lastObservedDay = day;
                var hiddenPlan = cache.Evaluate(world, day, cloudsNeeded: false, windFp, 0UL);
                hiddenWindNeedsSync = hiddenPlan.UploadWind;
                return false;
            }

            if (cloudsWereHidden)
                NotifyCloudsResumed();

            bool rewound = !double.IsNaN(lastObservedDay) && day < lastObservedDay - 1e-9;
            lastObservedDay = day;
            if (rewound)
            {
                CancelActiveAndBumpEpoch();
                cache.InvalidateSynoptic();
            }

            ulong climateFp = FlatSkyForcingCache.ComputeClimateFingerprint(world);

            // Check if an in-flight job's provenance or 15-day segment has become obsolete.
            if (activeTask != null && !IsSpecStillValid(activeSpec, world, season.SegmentStartDay, windFp, climateFp))
            {
                if (activeTask.IsCompleted)
                {
                    ConsumeAndDiscardCompletedActive();
                }
                else
                {
                    CancelActiveAndBumpEpoch();
                }
            }

            // Re-check draining after potential cancellation if the worker already finished.
            if (drainingTask != null && drainingTask.IsCompleted)
                drainingTask = null;

            // Commit a completed, provenance-verified result when caller is safe to upload textures.
            if (activeTask != null && activeTask.IsCompleted && allowCommit)
            {
                if (TryHarvestActive(world, season.SegmentStartDay, windFp, climateFp, out readyPayload))
                {
                    HasPublished = true;
                }
            }

            // Schedule or coalesce remaining demand for the current day/world.
            var plan = cache.Evaluate(world, day, cloudsNeeded: true, windFp, climateFp);
            bool needWork = plan.UploadClimateSegment || plan.UploadSynopticForcing || plan.UploadWind;
            if (needWork)
            {
                if (activeTask != null)
                {
                    // Same segment & provenance is already computing in background: coalesce intermediate days.
                    if (double.IsNaN(lastCoalescedDay) || Math.Abs(day - lastCoalescedDay) >= 1e-6)
                    {
                        RequestsCoalesced++;
                        lastCoalescedDay = day;
                    }
                }
                else if (drainingTask == null)
                {
                    LaunchWorker(world, day, season.SegmentStartDay, windFp, climateFp, plan);
                }
            }

            return readyPayload != null;
        }

        bool IsSpecStillValid(in JobSpec spec, WorldData world, double currentSegmentStartDay, ulong liveWindFp, ulong liveClimateFp)
        {
            if (spec.Epoch != epoch) return false;
            if (!ReferenceEquals(spec.World, world)) return false;
            if (spec.TerrainRevision != world.TerrainRevision) return false;
            if (!ReferenceEquals(spec.Moisture, world.Moisture)) return false;
            if (!ReferenceEquals(spec.OceanCurrents, world.Moisture?.OceanCurrents)) return false;
            if (!ReferenceEquals(spec.PlanetConfig, world.PlanetClimate)) return false;
            if (spec.ClimateFingerprint != liveClimateFp) return false;
            if (spec.IncludeSeasonalWind && spec.WindFingerprint != liveWindFp) return false;
            if (spec.SegmentStartDay != currentSegmentStartDay) return false;
            if (!double.IsNaN(cache.SynopticDay) && spec.SynopticDay < cache.SynopticDay - 1e-6) return false;
            return true;
        }

        void ConsumeAndDiscardCompletedActive()
        {
            var task = activeTask;
            var cts = activeCts;
            activeTask = null;
            activeCts = null;
            try { task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LastError = ex; }
            finally { cts?.Dispose(); }
            JobsCompleted++;
            JobsDiscardedStale++;
        }

        bool TryHarvestActive(WorldData world, double currentSegmentStartDay, ulong liveWindFp, ulong liveClimateFp, out FlatSkyForcingPayload readyPayload)
        {
            readyPayload = null;
            var task = activeTask;
            var cts = activeCts;
            var spec = activeSpec;
            activeTask = null;
            activeCts = null;

            bool succeeded = false;
            try
            {
                task.GetAwaiter().GetResult();
                succeeded = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LastError = ex; }
            finally { cts?.Dispose(); }

            JobsCompleted++;
            if (!succeeded || !IsSpecStillValid(spec, world, currentSegmentStartDay, liveWindFp, liveClimateFp))
            {
                JobsDiscardedStale++;
                return false;
            }

            // Atomic zero-copy buffer swap between worker output and main-thread upload buffer.
            var completed = workerBuffer;
            workerBuffer = mainBuffer;
            mainBuffer = completed;

            LastWorkerMs = completed.WorkerDurationMs;
            if (completed.WorkerDurationMs > MaxWorkerMs) MaxWorkerMs = completed.WorkerDurationMs;

            var segSeason = new FlatMonthlyForcing(completed.SegmentStartDay);
            if (completed.HasSeasonalWind)
                cache.MarkWindUploaded(world, segSeason, completed.WindFingerprint);
            if (completed.HasClimateSegment)
                cache.MarkClimateSegmentUploaded(world, segSeason, completed.SynopticDay, completed.ClimateFingerprint);
            else if (completed.HasSynopticForcing)
                cache.MarkSynopticUploaded(completed.SynopticDay);

            JobsAccepted++;
            readyPayload = mainBuffer;
            return true;
        }

        void LaunchWorker(
            WorldData world, double day, double segmentStartDay,
            ulong windFp, ulong climateFp, in FlatSkyForcingCache.RefreshPlan plan)
        {
            bool includeClimateSegment = plan.UploadClimateSegment;
            bool includeSynoptic = plan.UploadSynopticForcing || includeClimateSegment;
            bool includeWind = plan.UploadWind;

            var spec = new JobSpec(
                world, climateFp, windFp, epoch, ++nextRequestId,
                segmentStartDay, day, includeClimateSegment, includeSynoptic, includeWind);

            activeSpec = spec;
            activeCts = new CancellationTokenSource();
            var token = activeCts.Token;
            var dst = workerBuffer;
            var ws = workspace;
            var hook = BeforeWorkerComputeHook;

            JobsStarted++;
            lastCoalescedDay = day;
            activeTask = Task.Run(() => RunWorker(spec, dst, ws, hook, token), token);
        }

        static void RunWorker(
            JobSpec spec, FlatSkyForcingPayload dst,
            GlebaGeographySupplement.CloudSynopticWorkspace ws,
            Action<CancellationToken> hook, CancellationToken cancel)
        {
            long startStamp = Stopwatch.GetTimestamp();
            hook?.Invoke(cancel);
            cancel.ThrowIfCancellationRequested();

            int w = dst.Width, h = dst.Height;
            ws.EnsureSize(w, h);
            var segForcing = new FlatMonthlyForcing(spec.SegmentStartDay);

            dst.World = spec.World;
            dst.TerrainRevision = spec.TerrainRevision;
            dst.Moisture = spec.Moisture;
            dst.OceanCurrents = spec.OceanCurrents;
            dst.PlanetConfig = spec.PlanetConfig;
            dst.ClimateFingerprint = spec.ClimateFingerprint;
            dst.WindFingerprint = spec.WindFingerprint;
            dst.Epoch = spec.Epoch;
            dst.RequestId = spec.RequestId;
            dst.SegmentStartDay = spec.SegmentStartDay;
            dst.SynopticDay = spec.SynopticDay;
            dst.HasClimateSegment = spec.IncludeClimateSegment;
            dst.HasSynopticForcing = spec.IncludeSynopticForcing;
            dst.HasSeasonalWind = spec.IncludeSeasonalWind;

            if (spec.IncludeSeasonalWind)
            {
                FlatSkyForcingCache.PackSeasonalWindRgba(spec.World, segForcing, w, h, dst.SeasonalWindRgba, cancel);
            }

            if (spec.IncludeClimateSegment)
            {
                FlatSkyForcingCache.PackMonthlyClimateRgba(
                    spec.World, segForcing, w, h, dst.MonthlyClimateRgba,
                    dst.ClimatePoleSouth, dst.ClimatePoleNorth, ws, cancel);
            }

            if (spec.IncludeSynopticForcing)
            {
                GlebaGeographySupplement.BuildCloudSynopticFields(
                    spec.World, spec.SynopticDay, w, h,
                    dst.ForcingRgba, dst.AuxRgba,
                    spec.IncludeClimateSegment ? dst.UpperWindHalf : null,
                    spec.SegmentStartDay, ws, cancel);
                FlatSkyForcingCache.ComputeRgbaPoles(dst.ForcingRgba, w, h, dst.ForcingPoleSouth, dst.ForcingPoleNorth);
                GlebaGeographySupplement.GetCloudCirculationParams(
                    spec.World, out dst.HadleyRad, out dst.PolarRad, out dst.RossbyWaveK, out dst.RegimeCode);
            }

            cancel.ThrowIfCancellationRequested();
            dst.WorkerDurationMs = (Stopwatch.GetTimestamp() - startStamp) * 1000.0 / Stopwatch.Frequency;
        }

        void CancelActiveAndBumpEpoch()
        {
            epoch++;
            lastCoalescedDay = double.NaN;
            if (activeTask == null) return;
            var oldTask = activeTask;
            var oldCts = activeCts;
            activeTask = null;
            activeCts = null;
            JobsCancelled++;
            try { oldCts?.Cancel(); } catch (ObjectDisposedException) { }
            var priorDrain = drainingTask;
            drainingTask = oldTask.ContinueWith(t =>
            {
                if (t.IsFaulted) { _ = t.Exception; }
                oldCts?.Dispose();
                if (priorDrain != null && priorDrain.IsFaulted) { _ = priorDrain.Exception; }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CancelActiveAndBumpEpoch();
            boundWorld = null;
        }
    }
}
