using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orbis.Flat
{
    public enum GlebaRockKind : byte
    {
        Sandstone = 0,
        Conglomerate = 1,
        Siltstone = 2,
        Mudstone = 3,
        Claystone = 4,
        Shale = 5,
        Chalk = 6,
        Limestone = 7,
        Dolostone = 8,
        Chert = 9,
        Halite = 10,
        Granite = 11,
        Diorite = 12,
        Gabbro = 13,
        Basalt = 14,
        Andesite = 15,
        Rhyolite = 16,
        Slate = 17,
        Schist = 18,
        Gneiss = 19
    }

    public enum GlebaWaterbodyType : byte
    {
        Land = 0,
        PrimaryOcean = 1,
        InlandSea = 2,
        SaltwaterLake = 3,
        SweetwaterLake = 4
    }

    public enum GlebaLakeOrigin : byte
    {
        None = 0,
        Endorheic = 1,
        Stochastic = 2,
        Glacial = 3,
        Rifting = 4
    }

    public enum GlebaTerrainDomain : byte
    {
        MajorRiverValley = 0,
        Glacier = 1,
        HighOrogen = 2,
        HighPlateau = 3,
        TectonicUpland = 4,
        LowlandBasin = 5,
        ContinentalShield = 6,
        Water = 7
    }

    [Flags]
    public enum GlebaGeoFlags : byte
    {
        None = 0,
        CoralReef = 1 << 0,
        CoralCay = 1 << 1,
        FjordCoast = 1 << 2,
        LgmGlaciated = 1 << 3,
        ArtesianAquifer = 1 << 4
    }

    public enum GlebaMapmodeKind : byte
    {
        ClimateSurfaceWinds = 0,
        ClimateOceanCurrents = 1,
        RossbyJetStream = 2,
        ClimateItcz = 3,
        ClimateSthz = 4,
        ClimatePolarFront = 5,
        ClimateDistance = 6,
        ClimateLandVsSea = 7,
        RainShadow = 8,
        Continentality = 9,
        Bedrock = 10,
        Aquifers = 11,
        CoralReefsAndFjords = 12,
        Inaccessibility = 13,
        GeographicRegions = 14
    }

    public readonly struct GlebaRockProperties
    {
        public readonly GlebaRockKind Kind;
        public readonly float Sand;
        public readonly float Silt;
        public readonly float Clay;
        public readonly float WeatheringRate;
        public readonly float MineralRichness;
        public readonly float Porosity;
        public readonly bool CoralReefBedrock;
        public readonly byte MapR, MapG, MapB;

        public GlebaRockProperties(
            GlebaRockKind kind, float sand, float silt, float clay,
            float weatheringRate, float mineralRichness, float porosity,
            bool coralReefBedrock, byte r, byte g, byte b)
        {
            Kind = kind;
            Sand = sand;
            Silt = silt;
            Clay = clay;
            WeatheringRate = weatheringRate;
            MineralRichness = mineralRichness;
            Porosity = porosity;
            CoralReefBedrock = coralReefBedrock;
            MapR = r;
            MapG = g;
            MapB = b;
        }
    }

    /// <summary>
    /// Cached Gleba geographic, hydrogeological, oceanographic, and physiographic facts per world tile.
    /// Derived deterministically without mutating WorldData elevations or serialized save payloads.
    /// </summary>
    public sealed class GlebaGeographyFacts
    {
        public readonly int Count;
        public readonly float CellKm;
        public readonly GlebaRockKind[] Bedrock;
        public readonly GlebaWaterbodyType[] WaterbodyTypes;
        public readonly GlebaLakeOrigin[] LakeOrigins;
        public readonly GlebaGeoFlags[] Flags;
        public readonly ushort[] InaccessibilityHops;
        public readonly short[] AquiferId;
        public readonly ushort[] AquiferDepthMeters;
        public readonly ushort[] AquiferThicknessTenths;
        public readonly GlebaTerrainDomain[] Domains;
        public readonly byte[] BiogeographicRealms;
        public readonly int[] AreaId;
        public int[] AreaToRegion;
        public int[] RegionToSuperRegion;
        public int[] SuperRegionToSubcontinent;
        public int AquiferCount;
        public int CoralReefTileCount;
        public int FjordCoastTileCount;
        public int AreaCount;
        public int RegionCount;
        public int SuperRegionCount;
        public int SubcontinentCount;

        public GlebaGeographyFacts(int count, float cellKm)
        {
            Count = count;
            CellKm = cellKm;
            Bedrock = new GlebaRockKind[count];
            WaterbodyTypes = new GlebaWaterbodyType[count];
            LakeOrigins = new GlebaLakeOrigin[count];
            Flags = new GlebaGeoFlags[count];
            InaccessibilityHops = new ushort[count];
            AquiferId = new short[count];
            AquiferDepthMeters = new ushort[count];
            AquiferThicknessTenths = new ushort[count];
            Domains = new GlebaTerrainDomain[count];
            BiogeographicRealms = new byte[count];
            AreaId = new int[count];
            for (int i = 0; i < count; i++)
            {
                AquiferId[i] = -1;
                AreaId[i] = -1;
            }
            AreaToRegion = Array.Empty<int>();
            RegionToSuperRegion = Array.Empty<int>();
            SuperRegionToSubcontinent = Array.Empty<int>();
        }

        public float InaccessibilityKm(int tile) => InaccessibilityHops[tile] * CellKm;
        public float AquiferThicknessMeters(int tile) => AquiferThicknessTenths[tile] * 0.1f;
        public bool HasFlag(int tile, GlebaGeoFlags flag) => (Flags[tile] & flag) != 0;

        public int GetRegionId(int tile)
        {
            int a = AreaId[tile];
            return (uint)a < (uint)AreaToRegion.Length ? AreaToRegion[a] : -1;
        }

        public int GetSuperRegionId(int tile)
        {
            int r = GetRegionId(tile);
            return (uint)r < (uint)RegionToSuperRegion.Length ? RegionToSuperRegion[r] : -1;
        }

        public int GetSubcontinentId(int tile)
        {
            int s = GetSuperRegionId(tile);
            return (uint)s < (uint)SuperRegionToSubcontinent.Length ? SuperRegionToSubcontinent[s] : -1;
        }

        public GlebaGeographyFacts Clone()
        {
            var c = new GlebaGeographyFacts(Count, CellKm)
            {
                AquiferCount = AquiferCount,
                CoralReefTileCount = CoralReefTileCount,
                FjordCoastTileCount = FjordCoastTileCount,
                AreaCount = AreaCount,
                RegionCount = RegionCount,
                SuperRegionCount = SuperRegionCount,
                SubcontinentCount = SubcontinentCount,
                AreaToRegion = (int[])AreaToRegion.Clone(),
                RegionToSuperRegion = (int[])RegionToSuperRegion.Clone(),
                SuperRegionToSubcontinent = (int[])SuperRegionToSubcontinent.Clone()
            };
            Array.Copy(Bedrock, c.Bedrock, Count);
            Array.Copy(WaterbodyTypes, c.WaterbodyTypes, Count);
            Array.Copy(LakeOrigins, c.LakeOrigins, Count);
            Array.Copy(Flags, c.Flags, Count);
            Array.Copy(InaccessibilityHops, c.InaccessibilityHops, Count);
            Array.Copy(AquiferId, c.AquiferId, Count);
            Array.Copy(AquiferDepthMeters, c.AquiferDepthMeters, Count);
            Array.Copy(AquiferThicknessTenths, c.AquiferThicknessTenths, Count);
            Array.Copy(Domains, c.Domains, Count);
            Array.Copy(BiogeographicRealms, c.BiogeographicRealms, Count);
            Array.Copy(AreaId, c.AreaId, Count);
            return c;
        }
    }

    /// <summary>
    /// Port of Gleba v0.3.6 remaining geographic, oceanographic, hydrogeological, erosion, and mapmode systems:
    /// - Bedrock & porosity registry (`rock_types.lua`)
    /// - Deep confined/unconfined groundwater aquifers (`aquifer_gen::generate_aquifers`)
    /// - Tropical coral reefs & emergent coral cays (`coral_reefs::generate_coral_reefs`)
    /// - Glacial fjords & LGM snowline mass balance (`fjords::generate_fjords`)
    /// - Endorheic saltwater vs outflowing freshwater lakes (`lakes::generate_*_lakes`)
    /// - Global ocean inaccessibility, 8-domain 4-tier geographic hierarchy & biogeographic realms
    /// - 12-month seasonal ocean surface currents & 11-stop Cividis / flowfield mapmode evaluators
    /// - Thermal angle-of-repose & sorted stream-power hydraulic erosion operators
    /// </summary>
    public static class GlebaGeographySupplement
    {
        const double EarthRadiusKm = 6371.0088;
        const double NominalPlanetAreaKm2 = 510064472.0;

        static readonly GlebaRockProperties[] RockTable = new GlebaRockProperties[]
        {
            new GlebaRockProperties(GlebaRockKind.Sandstone,    0.70f, 0.20f, 0.10f, 0.50f, 0.25f, 0.20f, false, 214, 186, 115),
            new GlebaRockProperties(GlebaRockKind.Conglomerate, 0.95f, 0.05f, 0.00f, 0.40f, 0.20f, 0.15f, false, 190, 155, 165),
            new GlebaRockProperties(GlebaRockKind.Siltstone,    0.25f, 0.50f, 0.25f, 1.00f, 0.20f, 0.30f, false, 166, 131,  17),
            new GlebaRockProperties(GlebaRockKind.Mudstone,     0.25f, 0.20f, 0.55f, 1.00f, 0.40f, 0.20f, false, 163,  78,  30),
            new GlebaRockProperties(GlebaRockKind.Claystone,    0.20f, 0.10f, 0.70f, 1.00f, 0.44f, 0.10f, false, 160,  40,  46),
            new GlebaRockProperties(GlebaRockKind.Shale,        0.20f, 0.40f, 0.40f, 1.00f, 0.50f, 0.50f, false, 239, 228, 176),
            new GlebaRockProperties(GlebaRockKind.Chalk,        0.25f, 0.45f, 0.30f, 0.30f, 0.20f, 0.25f, false, 195, 220, 195),
            new GlebaRockProperties(GlebaRockKind.Limestone,    0.35f, 0.40f, 0.25f, 0.25f, 0.27f, 0.50f, true,   82, 242,  77),
            new GlebaRockProperties(GlebaRockKind.Dolostone,    0.30f, 0.35f, 0.35f, 0.20f, 0.27f, 0.40f, false,  34, 177,  76),
            new GlebaRockProperties(GlebaRockKind.Chert,        0.35f, 0.40f, 0.25f, 0.50f, 0.22f, 0.40f, false,  30, 147,  56),
            new GlebaRockProperties(GlebaRockKind.Halite,       0.40f, 0.30f, 0.30f, 0.20f, 1.00f, 0.20f, false, 255, 235, 235),
            new GlebaRockProperties(GlebaRockKind.Granite,      0.70f, 0.10f, 0.20f, 0.50f, 0.53f, 0.05f, false, 190, 100, 220),
            new GlebaRockProperties(GlebaRockKind.Diorite,      0.60f, 0.10f, 0.30f, 0.75f, 0.80f, 0.03f, false, 150,  60, 180),
            new GlebaRockProperties(GlebaRockKind.Gabbro,       0.45f, 0.15f, 0.40f, 0.90f, 0.92f, 0.02f, false,  90,  45, 120),
            new GlebaRockProperties(GlebaRockKind.Basalt,       0.45f, 0.15f, 0.40f, 0.90f, 0.90f, 0.45f, false,  70,  75,  90),
            new GlebaRockProperties(GlebaRockKind.Andesite,     0.55f, 0.20f, 0.25f, 0.75f, 0.78f, 0.40f, false, 115,  95, 125),
            new GlebaRockProperties(GlebaRockKind.Rhyolite,     0.65f, 0.15f, 0.20f, 0.55f, 0.55f, 0.30f, false, 165, 120, 145),
            new GlebaRockProperties(GlebaRockKind.Slate,        0.25f, 0.45f, 0.30f, 0.45f, 0.35f, 0.30f, false, 110, 120, 135),
            new GlebaRockProperties(GlebaRockKind.Schist,       0.45f, 0.35f, 0.20f, 0.40f, 0.45f, 0.20f, false, 130, 135, 150),
            new GlebaRockProperties(GlebaRockKind.Gneiss,       0.65f, 0.20f, 0.15f, 0.35f, 0.48f, 0.15f, false, 155, 130, 165)
        };

        // 11-stop Cividis colormap (.rdata@0x1822f89b0 in gleba_godot.dll)
        static readonly byte[] CividisR = { 0, 8, 53, 79, 102, 125, 149, 175, 201, 229, 254 };
        static readonly byte[] CividisG = { 34, 51, 69, 87, 105, 124, 143, 163, 184, 207, 232 };
        static readonly byte[] CividisB = { 78, 112, 108, 108, 112, 120, 119, 113, 102, 82, 55 };

        public static GlebaRockProperties GetRockProperties(GlebaRockKind kind) => RockTable[(int)kind];

        struct WyRand
        {
            ulong state;
            public WyRand(ulong seed) { state = seed == 0 ? 0xa0761d6478bd642fUL : seed; }
            public ulong NextU64()
            {
                unchecked
                {
                    state += 0xa0761d6478bd642fUL;
                    ulong x = state;
                    ulong y = x ^ 0xe7037ed1a0b428dbUL;
                    // 64x64 -> 128-bit fold
                    ulong xLo = (uint)x, xHi = x >> 32, yLo = (uint)y, yHi = y >> 32;
                    ulong p0 = xLo * yLo;
                    ulong p1 = xLo * yHi;
                    ulong p2 = xHi * yLo;
                    ulong p3 = xHi * yHi;
                    ulong cy = ((p0 >> 32) + (uint)p1 + (uint)p2) >> 32;
                    ulong lo = p0 + (p1 << 32) + (p2 << 32);
                    ulong hi = p3 + (p1 >> 32) + (p2 >> 32) + cy;
                    return lo ^ hi;
                }
            }
            public float NextFloat() => (NextU64() >> 40) * (1f / 16777216f);
            public int NextRange(int minInclusive, int maxExclusive)
            {
                if (maxExclusive <= minInclusive) return minInclusive;
                return minInclusive + (int)(NextU64() % (ulong)(maxExclusive - minInclusive));
            }
        }

        sealed class MaxHeap
        {
            struct Node { public int Id; public float Priority; }
            Node[] nodes = new Node[256];
            public int Count { get; private set; }
            public void Clear() => Count = 0;

            static bool Higher(Node a, Node b) => a.Priority > b.Priority || (a.Priority == b.Priority && a.Id < b.Id);

            public void Push(int id, float priority)
            {
                if (Count == nodes.Length) Array.Resize(ref nodes, nodes.Length * 2);
                var n = new Node { Id = id, Priority = priority };
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (!Higher(n, nodes[p])) break;
                    nodes[i] = nodes[p];
                    i = p;
                }
                nodes[i] = n;
            }

            public int Pop()
            {
                int res = nodes[0].Id;
                int last = --Count;
                if (last == 0) return res;
                var n = nodes[last];
                int i = 0;
                while ((i << 1) + 1 < last)
                {
                    int j = (i << 1) + 1;
                    if (j + 1 < last && Higher(nodes[j + 1], nodes[j])) j++;
                    if (!Higher(nodes[j], n)) break;
                    nodes[i] = nodes[j];
                    i = j;
                }
                nodes[i] = n;
                return res;
            }
        }

        static float Smooth(float edge0, float edge1, float x)
        {
            float t = FMath.Clamp((x - edge0) / Math.Max(1e-5f, edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        static float Hash3D(int ix, int iy, int iz, uint seed)
        {
            unchecked
            {
                uint h = seed ^ (uint)(ix * 73856093) ^ (uint)(iy * 19349663) ^ (uint)(iz * 83492791);
                return FMath.Unit(h) * 2f - 1f;
            }
        }

        static float ValueNoise3D(float x, float y, float z, uint seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
            float fx = x - ix, fy = y - iy, fz = z - iz;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);

            float c000 = Hash3D(ix, iy, iz, seed);
            float c100 = Hash3D(ix + 1, iy, iz, seed);
            float c010 = Hash3D(ix, iy + 1, iz, seed);
            float c110 = Hash3D(ix + 1, iy + 1, iz, seed);
            float c001 = Hash3D(ix, iy, iz + 1, seed);
            float c101 = Hash3D(ix + 1, iy, iz + 1, seed);
            float c011 = Hash3D(ix, iy + 1, iz + 1, seed);
            float c111 = Hash3D(ix + 1, iy + 1, iz + 1, seed);

            float x00 = FMath.Lerp(c000, c100, fx);
            float x10 = FMath.Lerp(c010, c110, fx);
            float x01 = FMath.Lerp(c001, c101, fx);
            float x11 = FMath.Lerp(c011, c111, fx);
            return FMath.Lerp(FMath.Lerp(x00, x10, fy), FMath.Lerp(x01, x11, fy), fz);
        }

        // 6-octave 3D spherical FBM matching Gleba's lacunarity = 2*pi/3 and 64/63 normalization
        internal static float SphericalFbm6(float ux, float uy, float uz, float baseScale, uint seed)
        {
            const float Lacunarity = 2.0943951f;
            float sum = 0f, amp = 0.5f, freq = baseScale;
            for (int k = 0; k < 6; k++)
            {
                sum += amp * ValueNoise3D(ux * freq, uy * freq, uz * freq, unchecked(seed + (uint)(k * 0x9e3779b9u)));
                amp *= 0.5f;
                freq *= Lacunarity;
            }
            return sum * (64f / 63f);
        }

        static float GreatCircleKm(float uxA, float uyA, float uzA, float uxB, float uyB, float uzB)
        {
            float dot = FMath.Clamp(uxA * uxB + uyA * uyB + uzA * uzB, -1f, 1f);
            return (float)(Math.Acos(dot) * EarthRadiusKm);
        }

        /// <summary>
        /// Evaluates LGM grounded glacier ice thickness (meters) accounting for high-latitude polar amplification,
        /// above-sea mountain snowline cooling, and maritime vs hyper-continental snowfall mass balance.
        /// </summary>
        public static float ComputeLgmIceMeters(
            bool isLand, float elevationMeters, float aboveSeaReliefMeters, float absLatitudeDeg,
            float meanAnnualTempC, float warmestMonthTempC, float annualRainMm)
        {
            if (!isLand || elevationMeters < 0f) return 0f;
            // Valley glacier tongues cannot descend into warm subtropical/tropical lowlands (mean T > 11 C at terminus)
            if (meanAnnualTempC > 11.0f && elevationMeters < 2200f) return 0f;
            float lgmCool = 4.5f + 10.0f * Smooth(38f, 62f, absLatitudeDeg);
            float snowLineBonus = FMath.Clamp((annualRainMm - 350f) / 240f, -2.5f, 5.5f);
            float tongueCooling = meanAnnualTempC <= 11.0f
                ? Math.Min(5.0f, 0.0035f * Math.Max(0f, aboveSeaReliefMeters))
                : 0f;
            float tAblationLgm = (0.55f * warmestMonthTempC + 0.45f * meanAnnualTempC)
                                 - tongueCooling - lgmCool - snowLineBonus;
            if (tAblationLgm >= 0f) return 0f;
            return GlebaEcologySupplement.ComputeGroundedIceMeters(
                true, elevationMeters, tAblationLgm, annualRainMm / 12f);
        }

        /// <summary>
        /// Builds complete Gleba geography, lake origin/salinity, coral reef, fjord coast, deep aquifer,
        /// ocean inaccessibility, 4-tier geographic hierarchy, and biogeographic realm facts for a world.
        /// </summary>
        public static GlebaGeographyFacts Build(
            WorldData w, FlatTerrainFacts f, int[] lakeRoots, CancellationToken cancel, int workers = 0)
        {
            int n = w.Count, width = w.Width, height = w.Height;
            float cellKm = 40030f / width;
            float tileAreaKm2 = (float)(NominalPlanetAreaKm2 / n);
            var geo = new GlebaGeographyFacts(n, cellKm);
            uint baseSeed = FMath.Seed(w.Seed) ^ 0x671eba50u;
            var options = FlatWorkerPolicy.Options(cancel, workers);

            // 1. Precompute 3D unit-sphere vectors & spherical 6-octave FBM noise
            var ux = new float[n];
            var uy = new float[n];
            var uz = new float[n];
            var fbmNoise = new float[n];
            var lgmIce = new float[n];

            Parallel.For(0, height, options, y =>
            {
                cancel.ThrowIfCancellationRequested();
                double lat = ((y + 0.5) / height - 0.5) * Math.PI;
                float cosLat = (float)Math.Cos(lat), sinLat = (float)Math.Sin(lat);
                float absLatDeg = (float)(Math.Abs(lat) * (180.0 / Math.PI));
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    double lon = ((x + 0.5) / width) * 2.0 * Math.PI - Math.PI;
                    int i = row + x;
                    float vx = cosLat * (float)Math.Cos(lon);
                    float vy = cosLat * (float)Math.Sin(lon);
                    float vz = sinLat;
                    ux[i] = vx; uy[i] = vy; uz[i] = vz;
                    fbmNoise[i] = SphericalFbm6(vx, vy, vz, 2.43728f, baseSeed ^ 0x13579bdfu);

                    bool isLand = !FlatTerrainAnalysis.AreaWater(w.Water[i]);
                    float warmest = -1000f;
                    int tBase = i * 12;
                    for (int m = 0; m < 12; m++)
                        if (w.Temperature[tBase + m] > warmest) warmest = w.Temperature[tBase + m];

                    float maxLandNb = Math.Max(0f, (float)w.Elevation[i]);
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nb = w.Index(x + dx, y + dy);
                        if (nb >= 0 && w.Elevation[nb] > maxLandNb) maxLandNb = w.Elevation[nb];
                    }
                    float aboveSeaRelief = Math.Max(0f, maxLandNb - Math.Max(0f, (float)w.Elevation[i]));

                    float lgm = ComputeLgmIceMeters(
                        isLand, w.Elevation[i], aboveSeaRelief, absLatDeg,
                        f.MeanTemperature[i], warmest, f.AnnualRain[i]);
                    lgmIce[i] = lgm;
                    if (lgm > 0f) geo.Flags[i] |= GlebaGeoFlags.LgmGlaciated;

                    geo.Bedrock[i] = ClassifyTileBedrock(w, f, i, vx, vy, vz, fbmNoise[i], baseSeed);
                }
            });

            // 2. Ocean waterbodies, PrimaryOcean vs InlandSea, Lake salinity/origin & Inaccessibility BFS
            ClassifyWaterbodiesAndInaccessibility(w, f, geo, lakeRoots, lgmIce, cancel);

            // 3. Glacial Fjord Coast evaluation (Stage 32 criteria: LGM ice > 10m + coastal orogeny/mountain relief)
            EvaluateFjordCoasts(w, f, geo, lgmIce);

            // 4. Tropical Coral Reefs (Stage 51: T_min >= 15.5 C, warm shallow shelf/seamounts, best-first growth)
            GenerateCoralReefs(w, geo, ux, uy, uz, fbmNoise, tileAreaKm2, baseSeed);

            // 5. Deep Groundwater Aquifers (Stage 50: porous >= 0.16 unglaciated land, +-750m structural band)
            GenerateAquifers(w, f, geo, ux, uy, uz, fbmNoise, tileAreaKm2, baseSeed, cancel);

            // 6. Physiographic Domains, 4-Tier Geographic Hierarchy & Biogeographic Realms
            BuildDomainsRealmsAndHierarchy(w, f, geo, ux, uy, uz, tileAreaKm2, baseSeed, cancel);

            return geo;
        }

        static GlebaRockKind ClassifyTileBedrock(
            WorldData w, FlatTerrainFacts f, int i,
            float vx, float vy, float vz, float fbmMain, uint seed)
        {
            float elev = w.Elevation[i];
            float lithNoise = SphericalFbm6(vx, vy, vz, 3.85f, seed ^ 0x2468ace0u);
            float chemNoise = SphericalFbm6(vx, vy, vz, 6.10f, seed ^ 0x97531fdbu);
            bool ocean = FlatTerrainAnalysis.Ocean(w.Water[i]);

            if (ocean)
            {
                if (elev < -2200f)
                    return chemNoise > 0.35f ? GlebaRockKind.Chert : (lithNoise > 0.2f ? GlebaRockKind.Gabbro : GlebaRockKind.Basalt);
                if (elev < -350f)
                    return lithNoise > 0.15f ? GlebaRockKind.Shale : GlebaRockKind.Mudstone;
                if (f.MeanTemperature[i] > 16f && chemNoise > -0.15f)
                    return chemNoise > 0.3f ? GlebaRockKind.Limestone : GlebaRockKind.Chalk;
                return lithNoise > 0f ? GlebaRockKind.Sandstone : GlebaRockKind.Shale;
            }

            var tec = w.Tectonics;
            if (tec != null)
            {
                var setting = tec.Setting[i];
                if (setting == TectonicSetting.Subduction || setting == TectonicSetting.IslandArc)
                    return lithNoise > 0.25f ? GlebaRockKind.Rhyolite : (lithNoise > -0.25f ? GlebaRockKind.Andesite : GlebaRockKind.Diorite);
                if (setting == TectonicSetting.Rift || setting == TectonicSetting.Ridge)
                    return lithNoise > 0.1f ? GlebaRockKind.Basalt : GlebaRockKind.Sandstone;
                if (setting == TectonicSetting.Collision && elev > 1400f)
                    return lithNoise > 0.2f ? GlebaRockKind.Granite : (chemNoise > 0f ? GlebaRockKind.Gneiss : GlebaRockKind.Schist);
            }

            // Arid endorheic evaporite basins
            if ((w.Downstream[i] < 0 || f.Forms[i] == Landform.Depression) && f.AnnualRain[i] < 260f)
            {
                if (chemNoise > 0.28f) return GlebaRockKind.Halite;
                if (chemNoise > -0.05f) return GlebaRockKind.Dolostone;
                return GlebaRockKind.Sandstone;
            }

            // High relief mountain belts & crystalline shields vs porous sedimentary basins
            if (elev > 2200f || f.Relief[i] > 1100f)
            {
                if (lithNoise > 0.22f) return GlebaRockKind.Granite;
                if (lithNoise > -0.15f) return GlebaRockKind.Gneiss;
                return chemNoise > 0.1f ? GlebaRockKind.Andesite : GlebaRockKind.Schist;
            }

            if (f.Relief[i] > 650f && elev > 800f)
            {
                if (lithNoise > 0.30f) return GlebaRockKind.Conglomerate;
                return lithNoise > -0.10f ? GlebaRockKind.Slate : GlebaRockKind.Granite;
            }

            // Continental platforms, plains, river basins & karst provinces (mostly porous >= 0.16, with crystalline shield domes)
            if (fbmMain > 0.42f) return GlebaRockKind.Granite;   // Crystalline shield dome (porosity 0.05, aquitard)
            if (fbmMain > 0.30f) return GlebaRockKind.Gneiss;    // Metamorphic basement (porosity 0.15, aquitard)
            if (chemNoise > 0.28f) return f.MeanTemperature[i] > 8f ? GlebaRockKind.Limestone : GlebaRockKind.Dolostone;
            if (chemNoise > 0.12f && elev < 500f) return GlebaRockKind.Chalk;
            if (lithNoise > 0.18f) return GlebaRockKind.Sandstone;
            if (lithNoise > -0.12f) return GlebaRockKind.Siltstone;
            if (lithNoise > -0.34f) return GlebaRockKind.Shale;
            return chemNoise > 0f ? GlebaRockKind.Mudstone : GlebaRockKind.Basalt;
        }

        static void ClassifyWaterbodiesAndInaccessibility(
            WorldData w, FlatTerrainFacts f, GlebaGeographyFacts geo,
            int[] lakeRoots, float[] lgmIce, CancellationToken cancel)
        {
            int n = w.Count;
            var compId = new int[n];
            for (int i = 0; i < n; i++) compId[i] = -1;
            var queue = new int[n];
            int compCount = 0, largestOceanComp = -1, largestOceanSize = -1;

            // Connected components of Ocean tiles (Stage 43 initialize_ocean_waterbodies)
            for (int start = 0; start < n; start++)
            {
                if (compId[start] >= 0 || !FlatTerrainAnalysis.Ocean(w.Water[start])) continue;
                int cid = compCount++;
                int head = 0, tail = 0;
                compId[start] = cid;
                queue[tail++] = start;
                while (head < tail)
                {
                    int u = queue[head++];
                    for (int d = 0; d < 4; d++)
                    {
                        int v = w.SphereNeighbor(u, d);
                        if (compId[v] < 0 && FlatTerrainAnalysis.Ocean(w.Water[v]))
                        {
                            compId[v] = cid;
                            queue[tail++] = v;
                        }
                    }
                }
                if (tail > largestOceanSize)
                {
                    largestOceanSize = tail;
                    largestOceanComp = cid;
                }
            }

            // Assign WaterbodyTypes & LakeOrigins (Stage 45 rules)
            for (int i = 0; i < n; i++)
            {
                var wk = w.Water[i];
                if (FlatTerrainAnalysis.Ocean(wk))
                {
                    geo.WaterbodyTypes[i] = compId[i] == largestOceanComp
                        ? GlebaWaterbodyType.PrimaryOcean
                        : GlebaWaterbodyType.InlandSea;
                }
                else if (wk == WaterKind.Lake)
                {
                    int root = lakeRoots != null ? lakeRoots[i] : FindLakeRootFallback(w, i);
                    bool spillsOut = root >= 0 && w.Downstream[root] >= 0;
                    if (!spillsOut)
                    {
                        geo.WaterbodyTypes[i] = GlebaWaterbodyType.SaltwaterLake;
                        geo.LakeOrigins[i] = GlebaLakeOrigin.Endorheic;
                    }
                    else
                    {
                        geo.WaterbodyTypes[i] = GlebaWaterbodyType.SweetwaterLake;
                        bool rift = (w.Tectonics != null && w.Tectonics.Setting[i] == TectonicSetting.Rift) ||
                                    (f.Depth[i] >= 180f && f.Relief[i] >= 450f);
                        bool glacial = lgmIce[i] > 0f || (Math.Abs(w.Latitude(i)) >= 42f && w.WaterLevel[i] < 600);
                        geo.LakeOrigins[i] = rift ? GlebaLakeOrigin.Rifting
                            : glacial ? GlebaLakeOrigin.Glacial
                            : GlebaLakeOrigin.Stochastic;
                    }
                }
                else
                {
                    geo.WaterbodyTypes[i] = GlebaWaterbodyType.Land;
                }
            }

            // Stage 60 calculate_inaccessibility: Multi-source BFS from PrimaryOcean across Land + Lakes + InlandSeas
            for (int i = 0; i < n; i++) geo.InaccessibilityHops[i] = ushort.MaxValue;
            int bHead = 0, bTail = 0;
            if (largestOceanComp >= 0)
            {
                for (int i = 0; i < n; i++)
                {
                    if (geo.WaterbodyTypes[i] == GlebaWaterbodyType.PrimaryOcean)
                    {
                        geo.InaccessibilityHops[i] = 0;
                        continue;
                    }
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(i, d);
                        if (geo.WaterbodyTypes[nb] == GlebaWaterbodyType.PrimaryOcean)
                        {
                            geo.InaccessibilityHops[i] = 1;
                            queue[bTail++] = i;
                            break;
                        }
                    }
                }
                while (bHead < bTail)
                {
                    if ((bHead & 4095) == 0) cancel.ThrowIfCancellationRequested();
                    int u = queue[bHead++];
                    ushort nextD = (ushort)Math.Min(65534, geo.InaccessibilityHops[u] + 1);
                    for (int d = 0; d < 4; d++)
                    {
                        int v = w.SphereNeighbor(u, d);
                        if (geo.InaccessibilityHops[v] == ushort.MaxValue &&
                            geo.WaterbodyTypes[v] != GlebaWaterbodyType.PrimaryOcean)
                        {
                            geo.InaccessibilityHops[v] = nextD;
                            queue[bTail++] = v;
                        }
                    }
                }
            }
            for (int i = 0; i < n; i++)
                if (geo.InaccessibilityHops[i] == ushort.MaxValue) geo.InaccessibilityHops[i] = 0;
        }

        internal static int FindLakeRootFallback(WorldData w, int lakeTile)
        {
            int curr = lakeTile;
            short level = w.WaterLevel[lakeTile];
            for (int steps = 0; steps < w.Count; steps++)
            {
                int next = w.Downstream[curr];
                if (next < 0 || w.Water[next] != WaterKind.Lake || w.WaterLevel[next] != level)
                    return curr;
                curr = next;
            }
            return curr;
        }

        static void EvaluateFjordCoasts(WorldData w, FlatTerrainFacts f, GlebaGeographyFacts geo, float[] lgmIce)
        {
            int count = 0, width = w.Width;
            var tec = w.Tectonics;
            for (int i = 0; i < w.Count; i++)
            {
                if (FlatTerrainAnalysis.AreaWater(w.Water[i])) continue;
                if (w.CoastDistance[i] > 1) continue;
                if (lgmIce[i] <= 10.0f) continue;

                // Measure strictly above-sea-level coastal mountain crest (excluding submarine trench depth!)
                int cx = i % width, cy = i / width;
                float landCrest = Math.Max(0f, (float)w.Elevation[i]);
                bool touchesOcean = false;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nb = w.Index(cx + dx, cy + dy);
                    if (nb < 0) continue;
                    if (FlatTerrainAnalysis.Ocean(w.Water[nb]) && (dx == 0 || dy == 0)) touchesOcean = true;
                    if (!FlatTerrainAnalysis.AreaWater(w.Water[nb]) && w.Elevation[nb] > landCrest)
                        landCrest = w.Elevation[nb];
                }
                if (!touchesOcean) continue;

                bool orogenicCoast = landCrest >= 420f;
                if (!orogenicCoast && tec != null && landCrest >= 220f)
                {
                    var s = tec.Setting[i];
                    orogenicCoast = s == TectonicSetting.Collision || s == TectonicSetting.Subduction || s == TectonicSetting.IslandArc;
                }
                if (!orogenicCoast) continue;

                geo.Flags[i] |= GlebaGeoFlags.FjordCoast;
                count++;
                for (int d = 0; d < 4; d++)
                {
                    int nb = w.SphereNeighbor(i, d);
                    if (FlatTerrainAnalysis.Ocean(w.Water[nb]) && (geo.Flags[nb] & GlebaGeoFlags.FjordCoast) == 0)
                    {
                        geo.Flags[nb] |= GlebaGeoFlags.FjordCoast;
                        count++;
                    }
                }
            }
            geo.FjordCoastTileCount = count;
        }

        static float MinMonthlyTemp(WorldData w, int tile)
        {
            int b = tile * 12;
            float minT = w.Temperature[b];
            for (int m = 1; m < 12; m++)
                if (w.Temperature[b + m] < minT) minT = w.Temperature[b + m];
            return minT;
        }

        static void GenerateCoralReefs(
            WorldData w, GlebaGeographyFacts geo,
            float[] ux, float[] uy, float[] uz, float[] fbmNoise,
            float tileAreaKm2, uint seed)
        {
            // Stage 51: Ocean water, coldest-month temperature >= 15.5 C, shallow photic/seamount slope [-750m, 0m)
            float maxSeedElev = w.Width >= 1024 ? -15f : -1f;
            var candidates = new List<int>();
            var eligibleNb = new bool[w.Count];

            for (int t = 0; t < w.Count; t++)
            {
                if (!FlatTerrainAnalysis.Ocean(w.Water[t])) continue;
                float elev = w.Elevation[t];
                if (elev < -750f || elev >= 0f) continue;
                if (MinMonthlyTemp(w, t) < 15.5f) continue;
                eligibleNb[t] = true;
                if (elev <= maxSeedElev) candidates.Add(t);
            }
            if (candidates.Count == 0) return;

            var rng = new WyRand(seed ^ 0x51c0a1eef5UL);
            // Scale Gleba's 500 km2 tile count to physical shelf area so reef provinces are resolution-consistent
            double totalCandAreaKm2 = candidates.Count * (double)tileAreaKm2;
            int numReefs = FMath.Clamp((int)Math.Round(totalCandAreaKm2 / (3000.0 * 500.0) * 6.0), 1, candidates.Count);

            var visited = new int[w.Count];
            int stamp = 0;
            var heap = new MaxHeap();
            int reefTiles = 0;

            for (int r = 0; r < numReefs && candidates.Count > 0; r++)
            {
                int pick = rng.NextRange(0, candidates.Count);
                int seedTile = candidates[pick];
                candidates[pick] = candidates[candidates.Count - 1];
                candidates.RemoveAt(candidates.Count - 1);
                if ((geo.Flags[seedTile] & GlebaGeoFlags.CoralReef) != 0) continue;

                // Gleba budget: 0..600 Goldberg tiles (0..300,000 km2)
                float budgetKm2 = rng.NextRange(15000, 300000);
                int budget = Math.Max(1, (int)Math.Ceiling(budgetKm2 / tileAreaKm2));
                float pIsland = 0.10f * rng.NextFloat() * (float)Math.Sqrt(FMath.Clamp(1f - budgetKm2 / 300000f, 0f, 1f));

                float seedElev = w.Elevation[seedTile];
                stamp++;
                heap.Clear();
                heap.Push(seedTile, 0f);
                int grown = 0;

                while (heap.Count > 0 && grown < budget)
                {
                    int curr = heap.Pop();
                    if (visited[curr] == stamp) continue;
                    visited[curr] = stamp;
                    grown++;

                    if ((geo.Flags[curr] & GlebaGeoFlags.CoralReef) == 0)
                    {
                        geo.Flags[curr] |= GlebaGeoFlags.CoralReef;
                        geo.Bedrock[curr] = GlebaRockKind.Limestone;
                        reefTiles++;

                        bool nearLand = w.CoastDistance[curr] <= 1;
                        float targetDepth = 4.0f * rng.NextFloat() - 5.0f;
                        float shoaledElev = targetDepth + 0.025f * (w.Elevation[curr] - targetDepth);
                        if (!nearLand && shoaledElev > -10.0f && rng.NextFloat() < pIsland)
                            geo.Flags[curr] |= GlebaGeoFlags.CoralCay;
                    }

                    if (grown >= budget) break;
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(curr, d);
                        if (visited[nb] == stamp || !eligibleNb[nb]) continue;
                        float distKm = GreatCircleKm(ux[seedTile], uy[seedTile], uz[seedTile], ux[nb], uy[nb], uz[nb]);
                        float priority = 2.1f * Math.Abs(seedElev - w.Elevation[nb]) - distKm + 2.2f * fbmNoise[nb];
                        heap.Push(nb, priority);
                    }
                }
            }
            geo.CoralReefTileCount = reefTiles;
        }

        static void GenerateAquifers(
            WorldData w, FlatTerrainFacts f, GlebaGeographyFacts geo,
            float[] ux, float[] uy, float[] uz, float[] fbmNoise,
            float tileAreaKm2, uint seed, CancellationToken cancel)
        {
            int n = w.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            var rng = new WyRand(seed ^ 0xa901fe50UL);
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.NextRange(0, i + 1);
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }

            var visited = new int[n];
            int stamp = 0;
            var heap = new MaxHeap();
            var filled = new List<int>(256);
            int aqCount = 0;
            var tec = w.Tectonics;

            for (int idx = 0; idx < n && aqCount < short.MaxValue; idx++)
            {
                if ((idx & 4095) == 0) cancel.ThrowIfCancellationRequested();
                int seedTile = order[idx];
                if (FlatTerrainAnalysis.AreaWater(w.Water[seedTile])) continue;
                if (geo.AquiferId[seedTile] >= 0) continue;
                if (f.GroundedIceMeters[seedTile] > 0f) continue;

                var seedProps = RockTable[(int)geo.Bedrock[seedTile]];
                if (seedProps.Porosity < 0.16f) continue;

                float areaKm2 = 50000f + 950000f * rng.NextFloat();
                float effAreaKm2 = areaKm2 * Math.Max(rng.NextFloat(), 0.1f);
                if (effAreaKm2 < 100000f && rng.NextFloat() < 0.25f) continue;
                int maxTiles = Math.Max(1, (int)Math.Ceiling(effAreaKm2 / tileAreaKm2));

                stamp++;
                filled.Clear();
                heap.Clear();
                heap.Push(seedTile, 0f);

                float seedElev = w.Elevation[seedTile];
                int seedPlate = tec != null ? tec.Plate[seedTile] : 0;
                GlebaRockKind seedRock = geo.Bedrock[seedTile];

                while (heap.Count > 0 && filled.Count < maxTiles)
                {
                    int curr = heap.Pop();
                    if (visited[curr] == stamp) continue;
                    visited[curr] = stamp;
                    filled.Add(curr);
                    if (filled.Count >= maxTiles) break;

                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(curr, d);
                        if (visited[nb] == stamp) continue;
                        if (FlatTerrainAnalysis.AreaWater(w.Water[nb])) continue;
                        if (geo.AquiferId[nb] >= 0) continue;
                        if (f.GroundedIceMeters[nb] > 0f) continue;

                        var nbProps = RockTable[(int)geo.Bedrock[nb]];
                        if (nbProps.Porosity <= 0.16f) continue;

                        float dElev = seedElev - w.Elevation[nb];
                        if (Math.Abs(dElev) >= 750f) continue;

                        float distKm = GreatCircleKm(ux[seedTile], uy[seedTile], uz[seedTile], ux[nb], uy[nb], uz[nb]);
                        float weight = (tec != null && tec.Plate[nb] == seedPlate ? 3000f : 0f)
                                     + (geo.Bedrock[nb] == seedRock ? 5000f : 0f)
                                     + 150f * fbmNoise[nb]
                                     - distKm
                                     - 25000f * Math.Abs(seedProps.Porosity - nbProps.Porosity)
                                     - 0.10f * Math.Abs(dElev)
                                     + 0.05f * dElev;
                        heap.Push(nb, weight);
                    }
                }

                if (filled.Count == 0) continue;

                // Accumulate recharge from surface runoff + soil texture permeability
                float acc = rng.NextFloat() * (250f * seedProps.Porosity) * rng.NextFloat() + filled.Count;
                for (int k = 0; k < filled.Count; k++)
                {
                    int t = filled[k];
                    var rp = RockTable[(int)geo.Bedrock[t]];
                    float sf = 1.0f;
                    if (rp.Sand > 0.15f) sf += (rp.Sand - 0.15f) * 0.8f / 0.85f;
                    if (rp.Silt > 0.85f) sf += (rp.Silt - 0.85f) * (-0.1f) / 0.15f;
                    if (rp.Clay > 0.20f) sf += (rp.Clay - 0.20f) * (-0.5f) / 0.80f;
                    sf = Math.Max(0.2f, sf);
                    acc += (float)Math.Sqrt(Math.Max(w.Runoff[t], 0f)) * sf;
                }

                float meanRecharge = acc / filled.Count;
                if (meanRecharge <= 0.5f) continue;

                short aqId = (short)aqCount++;
                float waterTableElev = seedElev - (1f + 150f * rng.NextFloat());
                for (int k = 0; k < filled.Count; k++)
                {
                    int t = filled[k];
                    geo.AquiferId[t] = aqId;
                    float rawDiff = w.Elevation[t] - waterTableElev;
                    if (rawDiff <= 5f) geo.Flags[t] |= GlebaGeoFlags.ArtesianAquifer;
                    float depth = FMath.Clamp(rawDiff, 1f, 5000f) + rng.NextFloat();
                    float thickness = Math.Max(1f, 2.0f * (fbmNoise[t] + meanRecharge) * (1f + 0.25f * rng.NextFloat()));
                    geo.AquiferDepthMeters[t] = (ushort)FMath.Clamp((int)Math.Round(depth), 1, 5001);
                    geo.AquiferThicknessTenths[t] = (ushort)FMath.Clamp((int)Math.Round(thickness * 10f), 10, 65000);
                }
            }
            geo.AquiferCount = aqCount;
        }

        static void BuildDomainsRealmsAndHierarchy(
            WorldData w, FlatTerrainFacts f, GlebaGeographyFacts geo,
            float[] ux, float[] uy, float[] uz,
            float tileAreaKm2, uint seed, CancellationToken cancel)
        {
            int n = w.Count;
            var tec = w.Tectonics;
            float riverThresh = FlatTerrainAnalysis.RiverThreshold(w) * 3.5f;

            // 1. 8-Class Physiographic Domain & Biogeographic Realms
            for (int i = 0; i < n; i++)
            {
                if (FlatTerrainAnalysis.AreaWater(w.Water[i]))
                {
                    geo.Domains[i] = GlebaTerrainDomain.Water;
                }
                else if (f.GroundedIceMeters[i] > 0f)
                {
                    geo.Domains[i] = GlebaTerrainDomain.Glacier;
                }
                else if (w.Water[i] == WaterKind.River && w.Runoff[i] >= riverThresh)
                {
                    geo.Domains[i] = GlebaTerrainDomain.MajorRiverValley;
                }
                else if (f.Relief[i] >= 1100f || (tec != null && tec.Setting[i] == TectonicSetting.Collision && w.Elevation[i] > 1500))
                {
                    geo.Domains[i] = GlebaTerrainDomain.HighOrogen;
                }
                else if (w.Elevation[i] > 4000)
                {
                    geo.Domains[i] = GlebaTerrainDomain.HighPlateau;
                }
                else if ((tec != null && (tec.Setting[i] == TectonicSetting.Rift || tec.Setting[i] == TectonicSetting.Subduction)) ||
                         (w.Elevation[i] > 750 && f.Relief[i] >= 350f))
                {
                    geo.Domains[i] = GlebaTerrainDomain.TectonicUpland;
                }
                else
                {
                    var rp = geo.Bedrock[i];
                    bool shield = rp == GlebaRockKind.Granite || rp == GlebaRockKind.Gneiss || rp == GlebaRockKind.Schist;
                    geo.Domains[i] = shield ? GlebaTerrainDomain.ContinentalShield : GlebaTerrainDomain.LowlandBasin;
                }

                // Biogeographic realm: plate-hashed on tectonic worlds, macro-continental Wallace realms on Earth/imported
                if (tec != null)
                {
                    uint h = FMath.Hash(seed ^ ((uint)tec.Plate[i] * 0x45d9f3bu));
                    geo.BiogeographicRealms[i] = (byte)(h & 7u);
                }
                else
                {
                    float lon = ((i % w.Width + 0.5f) / w.Width) * 360f - 180f;
                    float lat = w.Latitude(i);
                    geo.BiogeographicRealms[i] = ClassifyMacroRealm(lon, lat);
                }
            }

            // 2. Tier-1 Geographic Areas (Stages 55..58 scaled to physical area)
            var rng = new WyRand(seed ^ 0x55a8ea50UL);
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.NextRange(0, i + 1);
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }

            int riverMax = Math.Max(2, (int)Math.Round(2500f / tileAreaKm2));
            int landMax = Math.Max(4, (int)Math.Round(12000f / tileAreaKm2));
            int waterMax = Math.Max(12, (int)Math.Round(48000f / tileAreaKm2));
            float cellSlackKm = geo.CellKm * 1.6f;

            var heap = new MaxHeap();
            var visited = new int[n];
            int stamp = 0;
            var areaSize = new List<int>(1024);
            var areaKind = new List<GlebaTerrainDomain>(1024);
            var areaCx = new List<float>(1024);
            var areaCy = new List<float>(1024);
            var areaCz = new List<float>(1024);

            for (int idx = 0; idx < n; idx++)
            {
                if ((idx & 4095) == 0) cancel.ThrowIfCancellationRequested();
                int seedTile = order[idx];
                if (geo.AreaId[seedTile] >= 0) continue;

                var kind = geo.Domains[seedTile];
                int maxTiles = kind == GlebaTerrainDomain.MajorRiverValley ? riverMax
                    : kind == GlebaTerrainDomain.Water ? waterMax : landMax;
                float maxDistKm = (kind == GlebaTerrainDomain.MajorRiverValley ? 90f
                    : kind == GlebaTerrainDomain.Water ? 450f : 180f) + cellSlackKm;

                int aid = areaSize.Count;
                stamp++;
                heap.Clear();
                heap.Push(seedTile, 0f);

                int count = 0;
                float sx = 0f, sy = 0f, sz = 0f;
                int seedPlate = tec != null ? tec.Plate[seedTile] : 0;
                GlebaBiomeKind seedBiome = f.GlebaBiomes[seedTile];
                float seedElev = w.Elevation[seedTile];

                while (heap.Count > 0 && count < maxTiles)
                {
                    int curr = heap.Pop();
                    if (visited[curr] == stamp || geo.AreaId[curr] >= 0) continue;
                    visited[curr] = stamp;
                    geo.AreaId[curr] = aid;
                    count++;
                    sx += ux[curr]; sy += uy[curr]; sz += uz[curr];
                    if (count >= maxTiles) break;

                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(curr, d);
                        if (visited[nb] == stamp || geo.AreaId[nb] >= 0) continue;
                        if (geo.Domains[nb] != kind) continue;
                        float distKm = GreatCircleKm(ux[seedTile], uy[seedTile], uz[seedTile], ux[nb], uy[nb], uz[nb]);
                        if (distKm > maxDistKm) continue;

                        float riverPenalty = w.Water[nb] == WaterKind.River
                            ? 100f * FMath.Clamp(w.Runoff[nb] / Math.Max(1f, riverThresh), 0f, 1f) : 0f;
                        float prio = (tec != null && tec.Plate[nb] == seedPlate ? 25f : 0f)
                                   + (f.GlebaBiomes[nb] == seedBiome ? 50f : 0f)
                                   - Math.Abs(seedElev - w.Elevation[nb])
                                   - riverPenalty
                                   - 5f * distKm;
                        heap.Push(nb, prio);
                    }
                }

                float norm = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
                if (norm > 1e-6f) { sx /= norm; sy /= norm; sz /= norm; }
                areaSize.Add(count);
                areaKind.Add(kind);
                areaCx.Add(sx); areaCy.Add(sy); areaCz.Add(sz);
            }

            // Phase 2: Dissolve <= 2-tile runts into adjacent non-runt Areas of matching Land vs Water domain
            int rawAreas = areaSize.Count;
            if (rawAreas > 1)
            {
                var mergeTarget = new int[rawAreas];
                for (int a = 0; a < rawAreas; a++) mergeTarget[a] = -1;
                for (int i = 0; i < n; i++)
                {
                    int a = geo.AreaId[i];
                    if (areaSize[a] > 2) continue;
                    bool isWater = areaKind[a] == GlebaTerrainDomain.Water;
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = w.SphereNeighbor(i, d);
                        int b = geo.AreaId[nb];
                        if (b == a || areaSize[b] <= 2) continue;
                        if ((areaKind[b] == GlebaTerrainDomain.Water) != isWater) continue;
                        if (mergeTarget[a] < 0 || areaSize[b] < areaSize[mergeTarget[a]])
                            mergeTarget[a] = b;
                    }
                }
                var remap = new int[rawAreas];
                for (int a = 0; a < rawAreas; a++) remap[a] = -1;
                int compactAreas = 0;
                var nextKind = new List<GlebaTerrainDomain>(rawAreas);
                var nextCx = new List<float>(rawAreas);
                var nextCy = new List<float>(rawAreas);
                var nextCz = new List<float>(rawAreas);

                for (int i = 0; i < n; i++)
                {
                    int a = geo.AreaId[i];
                    if (mergeTarget[a] >= 0) a = mergeTarget[a];
                    if (remap[a] < 0)
                    {
                        remap[a] = compactAreas++;
                        nextKind.Add(areaKind[a]);
                        nextCx.Add(areaCx[a]);
                        nextCy.Add(areaCy[a]);
                        nextCz.Add(areaCz[a]);
                    }
                    geo.AreaId[i] = remap[a];
                }
                areaKind = nextKind;
                areaCx = nextCx; areaCy = nextCy; areaCz = nextCz;
            }

            geo.AreaCount = areaKind.Count;

            // Build Area adjacency graph once, then cluster Regions -> SuperRegions -> Subcontinents
            var areaAdj = BuildGroupAdjacency(w, geo.AreaId, geo.AreaCount);
            geo.AreaToRegion = ClusterUpperTier(
                geo.AreaCount, areaAdj, areaKind, areaCx, areaCy, areaCz,
                maxLand: 7, maxWater: 24, maxDistLandKm: 650f + cellSlackKm * 2f, maxDistWaterKm: 2200f + cellSlackKm * 2f,
                ref rng, out var regKind, out var regCx, out var regCy, out var regCz, out var regAdj);
            geo.RegionCount = regKind.Count;

            geo.RegionToSuperRegion = ClusterUpperTier(
                geo.RegionCount, regAdj, regKind, regCx, regCy, regCz,
                maxLand: 6, maxWater: 20, maxDistLandKm: 1800f + cellSlackKm * 3f, maxDistWaterKm: 5500f + cellSlackKm * 3f,
                ref rng, out var supKind, out var supCx, out var supCy, out var supCz, out var supAdj);
            geo.SuperRegionCount = supKind.Count;

            geo.SuperRegionToSubcontinent = ClusterUpperTier(
                geo.SuperRegionCount, supAdj, supKind, supCx, supCy, supCz,
                maxLand: 8, maxWater: 28, maxDistLandKm: 4500f + cellSlackKm * 4f, maxDistWaterKm: 12000f + cellSlackKm * 4f,
                ref rng, out var subKind, out _, out _, out _, out _);
            geo.SubcontinentCount = subKind.Count;
        }

        static byte ClassifyMacroRealm(float lonDeg, float latDeg)
        {
            if (latDeg < -60f) return 7; // Antarctic
            if (lonDeg < -26f) return latDeg >= 12f ? (byte)1 : (byte)2; // Nearctic (1) vs Neotropical (2)
            if (lonDeg >= 115f && latDeg < 12f)
                return lonDeg >= 120f && latDeg < -8f ? (byte)6 : (byte)5; // Australasian / Wallacea (6) vs Indomalayan (5)
            if (latDeg < 22f && lonDeg >= -26f && lonDeg < 54f) return 3; // Afrotropical (3)
            if (latDeg < 30f && lonDeg >= 66f && lonDeg < 125f) return 5; // Indomalayan (5)
            if (lonDeg >= 155f && latDeg > -50f && latDeg < 25f) return 4; // Oceanian (4)
            return 0; // Palearctic (0)
        }

        static List<int>[] BuildGroupAdjacency(WorldData w, int[] tileGroup, int groupCount)
        {
            var adj = new List<int>[groupCount];
            for (int g = 0; g < groupCount; g++) adj[g] = new List<int>(6);
            for (int i = 0; i < w.Count; i++)
            {
                int ga = tileGroup[i];
                for (int d = 1; d <= 3; d += 2) // East and North edges cover all undirected pairs
                {
                    int nb = w.SphereNeighbor(i, d);
                    int gb = tileGroup[nb];
                    if (ga == gb) continue;
                    if (!adj[ga].Contains(gb)) adj[ga].Add(gb);
                    if (!adj[gb].Contains(ga)) adj[gb].Add(ga);
                }
            }
            return adj;
        }

        static int[] ClusterUpperTier(
            int childCount, List<int>[] childAdj, List<GlebaTerrainDomain> childKind,
            List<float> cx, List<float> cy, List<float> cz,
            int maxLand, int maxWater, float maxDistLandKm, float maxDistWaterKm,
            ref WyRand rng,
            out List<GlebaTerrainDomain> parentKind,
            out List<float> px, out List<float> py, out List<float> pz,
            out List<int>[] parentAdj)
        {
            var parentOf = new int[childCount];
            var order = new int[childCount];
            for (int i = 0; i < childCount; i++) { parentOf[i] = -1; order[i] = i; }
            for (int i = childCount - 1; i > 0; i--)
            {
                int j = rng.NextRange(0, i + 1);
                int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }

            parentKind = new List<GlebaTerrainDomain>(Math.Max(4, childCount / 4));
            px = new List<float>(parentKind.Capacity);
            py = new List<float>(parentKind.Capacity);
            pz = new List<float>(parentKind.Capacity);

            var heap = new MaxHeap();
            var visited = new int[childCount];
            int stamp = 0;

            for (int idx = 0; idx < childCount; idx++)
            {
                int seed = order[idx];
                if (parentOf[seed] >= 0) continue;
                var kind = childKind[seed];
                bool isWater = kind == GlebaTerrainDomain.Water;
                int maxChildren = isWater ? maxWater : maxLand;
                float maxDist = isWater ? maxDistWaterKm : maxDistLandKm;

                int pid = parentKind.Count;
                stamp++;
                heap.Clear();
                heap.Push(seed, 0f);
                int count = 0;
                float sx = 0f, sy = 0f, sz = 0f;

                while (heap.Count > 0 && count < maxChildren)
                {
                    int u = heap.Pop();
                    if (visited[u] == stamp || parentOf[u] >= 0) continue;
                    visited[u] = stamp;
                    parentOf[u] = pid;
                    count++;
                    sx += cx[u]; sy += cy[u]; sz += cz[u];
                    if (count >= maxChildren) break;

                    var nbs = childAdj[u];
                    for (int k = 0; k < nbs.Count; k++)
                    {
                        int v = nbs[k];
                        if (visited[v] == stamp || parentOf[v] >= 0) continue;
                        if ((childKind[v] == GlebaTerrainDomain.Water) != isWater) continue;
                        float distKm = GreatCircleKm(cx[seed], cy[seed], cz[seed], cx[v], cy[v], cz[v]);
                        if (distKm > maxDist) continue;
                        float bonus = childKind[v] == kind ? 200f : 0f;
                        heap.Push(v, bonus - 5f * distKm);
                    }
                }

                float norm = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
                if (norm > 1e-6f) { sx /= norm; sy /= norm; sz /= norm; }
                parentKind.Add(kind);
                px.Add(sx); py.Add(sy); pz.Add(sz);
            }

            parentAdj = new List<int>[parentKind.Count];
            for (int p = 0; p < parentKind.Count; p++) parentAdj[p] = new List<int>(6);
            for (int u = 0; u < childCount; u++)
            {
                int pu = parentOf[u];
                var nbs = childAdj[u];
                for (int k = 0; k < nbs.Count; k++)
                {
                    int pv = parentOf[nbs[k]];
                    if (pu != pv && !parentAdj[pu].Contains(pv)) parentAdj[pu].Add(pv);
                }
            }
            return parentOf;
        }

        // --- 12-Month Seasonal Ocean Surface Currents & Gleba Mapmode Color / Vector Flow Evaluators ---

        /// <summary>
        /// Samples 12-month seasonal surface ocean current velocity (m/s), sea-surface dynamic height,
        /// and SST anomaly (°C) at normalized coordinates (u, v) in [0, 1].
        /// Combines annual Sverdrup/Stommel/Ekman circulation with monthly monsoon & ITCZ wind-drift anomalies.
        /// </summary>
        public static void SampleSeasonalOceanCurrent(
            WorldData world, float u, float v, int month,
            out float eastMs, out float northMs, out float dynamicHeight, out float sstAnomalyC)
        {
            eastMs = 0f;
            northMs = 0f;
            dynamicHeight = 1000f;
            sstAnomalyC = 0f;
            if (world?.Moisture == null) return;
            month = FMath.Wrap(month, 12);

            var occ = world.Moisture.OceanCurrents;
            if (occ != null)
            {
                occ.SampleCurrent(u, v, out eastMs, out northMs);
                sstAnomalyC = occ.Sample(occ.SeaAnomaly, u, v);
            }

            var gleba = world.Moisture.Gleba;
            if (gleba != null)
            {
                dynamicHeight = gleba.SampleMonthly(gleba.OceanDynamicHeight, u, v, month);
                float oceanFrac = gleba.SampleStatic(gleba.OceanFraction, u, v);
                if (oceanFrac > 0.05f)
                {
                    float mEast = gleba.SampleMonthly(gleba.CurrentEast, u, v, month);
                    float mNorth = gleba.SampleMonthly(gleba.CurrentNorth, u, v, month);
                    float annEast = 0f, annNorth = 0f;
                    for (int m = 0; m < 12; m++)
                    {
                        annEast += gleba.SampleMonthly(gleba.CurrentEast, u, v, m);
                        annNorth += gleba.SampleMonthly(gleba.CurrentNorth, u, v, m);
                    }
                    annEast /= 12f;
                    annNorth /= 12f;

                    // Gleba CurrentEast/North has *0.005 internal scale; convert seasonal anomaly to m/s (~22x)
                    // Zero annual-mean perturbation preserves exact annual drifter velocity while capturing monsoon reversals!
                    const float SeasonalScaleMs = 22.0f;
                    eastMs += (mEast - annEast) * SeasonalScaleMs * oceanFrac;
                    northMs += (mNorth - annNorth) * SeasonalScaleMs * oceanFrac;
                }
            }
        }

        /// <summary>
        /// Samples Gleba's 11-stop Cividis colormap (.rdata@0x1822f89b0) for t in [0, 1].
        /// </summary>
        public static void SampleCividis(float t, out byte r, out byte g, out byte b)
        {
            t = FMath.Clamp(t, 0f, 1f);
            if (t <= 0f) { r = CividisR[0]; g = CividisG[0]; b = CividisB[0]; return; }
            if (t >= 1f) { r = CividisR[10]; g = CividisG[10]; b = CividisB[10]; return; }
            float scaled = t * 10f;
            int idx = FMath.Clamp((int)scaled, 0, 9);
            float u = scaled - idx;
            r = (byte)FMath.Clamp(CividisR[idx] + (CividisR[idx + 1] - CividisR[idx]) * u, 0f, 255f);
            g = (byte)FMath.Clamp(CividisG[idx] + (CividisG[idx + 1] - CividisG[idx]) * u, 0f, 255f);
            b = (byte)FMath.Clamp(CividisB[idx] + (CividisB[idx + 1] - CividisB[idx]) * u, 0f, 255f);
        }

        /// <summary>
        /// Evaluates Gleba mapmode RGB color for a world tile and calendar month (matching mapmodes::basic::climate_*).
        /// </summary>
        public static void EvaluateMapmodeColor(
            WorldData world, int tile, int month, GlebaMapmodeKind mode,
            out byte r, out byte g, out byte b)
        {
            r = g = b = 80;
            if (world == null || (uint)tile >= (uint)world.Count) return;
            month = FMath.Wrap(month, 12);
            bool isWater = FlatTerrainAnalysis.AreaWater(world.Water[tile]);
            float baseVal = isWater ? 30f : 80f;
            float u = (tile % world.Width + 0.5f) / world.Width;
            float v = (tile / world.Width + 0.5f) / world.Height;
            float latDeg = world.Latitude(tile);
            var gleba = world.Moisture?.Gleba;
            var geo = world.Facts?.GlebaGeography;

            switch (mode)
            {
                case GlebaMapmodeKind.ClimateSurfaceWinds:
                {
                    float p = gleba != null ? gleba.SampleMonthly(gleba.PressureHpa, u, v, month) : 1013.25f;
                    float t = 1f - FMath.Clamp((p - 900f) / 200f, 0f, 1f);
                    SampleCividis(t, out byte cr, out byte cg, out byte cb);
                    r = (byte)FMath.Clamp(baseVal + 0.955f * (cr - baseVal), 0f, 255f);
                    g = (byte)FMath.Clamp(baseVal + 0.955f * (cg - baseVal), 0f, 255f);
                    b = (byte)FMath.Clamp(baseVal + 0.955f * (cb - baseVal), 0f, 255f);
                    if (gleba != null)
                    {
                        int gx = FMath.Wrap((int)(u * gleba.Width), gleba.Width);
                        int gy = FMath.Clamp((int)(v * gleba.Height), 0, gleba.Height - 1);
                        var shore = gleba.ShoreTypes[(gy * gleba.Width + gx) * 12 + month];
                        if (shore == GlebaShoreType.OnShore)
                        {
                            r = (byte)(r + 0.5f * (50f - r));
                            g = (byte)(g + 0.5f * (50f - g));
                            b = (byte)(b + 0.5f * (200f - b));
                        }
                        else if (shore == GlebaShoreType.OffShore)
                        {
                            r = (byte)(r + 0.5f * (150f - r));
                            g = (byte)(g + 0.5f * (50f - g));
                            b = (byte)(b + 0.5f * (50f - b));
                        }
                    }
                    return;
                }
                case GlebaMapmodeKind.ClimateOceanCurrents:
                {
                    if (!isWater) { r = g = b = 80; return; }
                    SampleSeasonalOceanCurrent(world, u, v, month, out _, out _, out float dynH, out _);
                    SampleCividis(FMath.Clamp((dynH - 900f) / 200f, 0f, 1f), out byte cr, out byte cg, out byte cb);
                    r = (byte)FMath.Clamp(30f + 0.25f * (cr - 30f), 0f, 255f);
                    g = (byte)FMath.Clamp(30f + 0.25f * (cg - 30f), 0f, 255f);
                    b = (byte)FMath.Clamp(30f + 0.25f * (cb - 30f), 0f, 255f);
                    return;
                }
                case GlebaMapmodeKind.ClimateItcz:
                {
                    if (gleba == null) { r = g = b = (byte)baseVal; return; }
                    float active = gleba.SampleBandDegrees(gleba.ItczLatRad, u, month);
                    float summer = gleba.SampleBandDegrees(gleba.ItczLatRad, u, 6);
                    float winter = gleba.SampleBandDegrees(gleba.ItczLatRad, u, 0);
                    BlendBandRgb(baseVal, Math.Abs(latDeg - summer) < 2f, Math.Abs(latDeg - active) < 2f, Math.Abs(latDeg - winter) < 2f, out r, out g, out b);
                    return;
                }
                case GlebaMapmodeKind.ClimateSthz:
                {
                    if (gleba == null) { r = g = b = (byte)baseVal; return; }
                    bool sum = Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.SthzNorthLatRad, u, 6)) < 2f ||
                               Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.SthzSouthLatRad, u, 6)) < 2f;
                    bool act = Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.SthzNorthLatRad, u, month)) < 2f ||
                               Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.SthzSouthLatRad, u, month)) < 2f;
                    bool win = Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.SthzNorthLatRad, u, 0)) < 2f ||
                               Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.SthzSouthLatRad, u, 0)) < 2f;
                    BlendBandRgb(baseVal, sum, act, win, out r, out g, out b);
                    return;
                }
                case GlebaMapmodeKind.ClimatePolarFront:
                {
                    if (gleba == null) { r = g = b = (byte)baseVal; return; }
                    bool sum = Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.PolarNorthLatRad, u, 6)) < 2f ||
                               Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.PolarSouthLatRad, u, 6)) < 2f;
                    bool act = Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.PolarNorthLatRad, u, month)) < 2f ||
                               Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.PolarSouthLatRad, u, month)) < 2f;
                    bool win = Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.PolarNorthLatRad, u, 0)) < 2f ||
                               Math.Abs(latDeg - gleba.SampleBandDegrees(gleba.PolarSouthLatRad, u, 0)) < 2f;
                    BlendBandRgb(baseVal, sum, act, win, out r, out g, out b);
                    return;
                }
                case GlebaMapmodeKind.ClimateDistance:
                {
                    if (gleba == null) { r = g = b = (byte)baseVal; return; }
                    float dDeep = gleba.SampleStatic(gleba.DistanceToDeepSeaKm, u, v);
                    float dLand = gleba.SampleStatic(gleba.DistanceToLandKm, u, v);
                    float dSea = gleba.SampleStatic(gleba.DistanceToSeaKm, u, v);
                    float rr = dDeep > 2000f ? 255f : FMath.Clamp(dDeep / 2500f * 255f, 0f, 255f);
                    float gg = dLand > 2500f ? 255f : FMath.Clamp(dLand / 2500f * 255f, 0f, 255f);
                    float bb = dSea > 2500f ? 255f : FMath.Clamp(dSea / 2500f * 255f, 0f, 255f);
                    r = (byte)FMath.Clamp(baseVal + 0.25f * (rr - baseVal), 0f, 255f);
                    g = (byte)FMath.Clamp(baseVal + 0.25f * (gg - baseVal), 0f, 255f);
                    b = (byte)FMath.Clamp(baseVal + 0.25f * (bb - baseVal), 0f, 255f);
                    return;
                }
                case GlebaMapmodeKind.ClimateLandVsSea:
                {
                    byte gray = gleba != null
                        ? (byte)FMath.Clamp(gleba.SampleStatic(gleba.LandFraction, u, v) * 255f, 0f, 255f)
                        : (byte)(isWater ? 0 : 255);
                    r = g = b = gray;
                    return;
                }
                case GlebaMapmodeKind.RainShadow:
                {
                    float sh = gleba != null ? FMath.Clamp(gleba.SampleMonthly(gleba.RainShadowiness, u, v, month), 0f, 1f) : 0f;
                    SampleCividis(sh, out r, out g, out b);
                    return;
                }
                case GlebaMapmodeKind.Continentality:
                {
                    float c = gleba != null ? FMath.Clamp(gleba.SampleMonthly(gleba.WindContinentality, u, v, month) / 2.5f, 0f, 1f) : 0f;
                    SampleCividis(c, out r, out g, out b);
                    return;
                }
                case GlebaMapmodeKind.Bedrock:
                {
                    if (geo == null) { r = g = b = (byte)baseVal; return; }
                    var rp = RockTable[(int)geo.Bedrock[tile]];
                    r = rp.MapR; g = rp.MapG; b = rp.MapB;
                    return;
                }
                case GlebaMapmodeKind.Aquifers:
                {
                    if (geo == null || geo.AquiferId[tile] < 0) { r = g = b = (byte)baseVal; return; }
                    uint h = FMath.Hash((uint)geo.AquiferId[tile] * 0x9e3779b9u);
                    r = (byte)(150 + (h & 0xFF) * 105 / 255);
                    g = (byte)(150 + ((h >> 8) & 0xFF) * 105 / 255);
                    b = (byte)(150 + ((h >> 16) & 0xFF) * 105 / 255);
                    return;
                }
                case GlebaMapmodeKind.CoralReefsAndFjords:
                {
                    if (geo != null)
                    {
                        if (geo.HasFlag(tile, GlebaGeoFlags.CoralCay)) { r = 255; g = 230; b = 120; return; }
                        if (geo.HasFlag(tile, GlebaGeoFlags.CoralReef)) { r = 255; g = 110; b = 165; return; }
                        if (geo.HasFlag(tile, GlebaGeoFlags.FjordCoast)) { r = 70; g = 210; b = 255; return; }
                    }
                    r = g = b = (byte)baseVal;
                    return;
                }
                case GlebaMapmodeKind.Inaccessibility:
                {
                    float km = geo != null ? geo.InaccessibilityKm(tile) : 0f;
                    SampleCividis(FMath.Clamp(km / 2500f, 0f, 1f), out r, out g, out b);
                    return;
                }
                case GlebaMapmodeKind.GeographicRegions:
                {
                    int reg = geo != null ? geo.GetRegionId(tile) : -1;
                    if (reg < 0) { r = g = b = (byte)baseVal; return; }
                    uint h = FMath.Hash((uint)reg * 0x85ebca6bu);
                    r = (byte)(60 + (h & 0xFF) * 180 / 255);
                    g = (byte)(60 + ((h >> 8) & 0xFF) * 180 / 255);
                    b = (byte)(60 + ((h >> 16) & 0xFF) * 180 / 255);
                    return;
                }
                default:
                    r = g = b = (byte)baseVal;
                    return;
            }
        }

        static void BlendBandRgb(float baseVal, bool redBand, bool greenBand, bool blueBand, out byte r, out byte g, out byte b)
        {
            r = (byte)FMath.Clamp(baseVal + 0.25f * ((redBand ? 255f : 0f) - baseVal), 0f, 255f);
            g = (byte)FMath.Clamp(baseVal + 0.25f * ((greenBand ? 255f : 0f) - baseVal), 0f, 255f);
            b = (byte)FMath.Clamp(baseVal + 0.25f * ((blueBand ? 255f : 0f) - baseVal), 0f, 255f);
        }

        /// <summary>
        /// Evaluates Gleba Mapmode::get_tile_flow UV vector (FlowU = +East, FlowV = -North for equirectangular UV textures).
        /// Returns true when the mapmode carries an active vector flow overlay.
        /// </summary>
        public static bool EvaluateMapmodeFlow(
            WorldData world, float u, float v, int month, GlebaMapmodeKind mode,
            out float flowU, out float flowV)
        {
            flowU = 0f;
            flowV = 0f;
            if (world == null) return false;
            month = FMath.Wrap(month, 12);

            switch (mode)
            {
                case GlebaMapmodeKind.RossbyJetStream:
                {
                    double latRad = (v - 0.5) * Math.PI;
                    double lonRad = (u - 0.5) * 2.0 * Math.PI;
                    var rossby = new GlebaRossbyWaveField(FMath.Seed(world.Seed))
                    {
                        RossbyTimeRadians = (month / 12.0) * 2.0 * Math.PI
                    };
                    rossby.GetJetStreamMovementAtLatLon(latRad, lonRad, out float vx, out float vy);
                    flowU = vx;
                    flowV = -vy;
                    return true;
                }
                case GlebaMapmodeKind.ClimateSurfaceWinds:
                {
                    if (world.Moisture != null)
                    {
                        float wx = world.Moisture.Sample(world.Moisture.East, u, v, month);
                        float wy = world.Moisture.Sample(world.Moisture.North, u, v, month);
                        flowU = 0.01f * wx;
                        flowV = -0.01f * wy;
                        return true;
                    }
                    return false;
                }
                case GlebaMapmodeKind.ClimateOceanCurrents:
                {
                    int x = FMath.Wrap((int)(u * world.Width), world.Width);
                    int y = FMath.Clamp((int)(v * world.Height), 0, world.Height - 1);
                    if (!FlatTerrainAnalysis.Ocean(world.Water[y * world.Width + x]))
                    {
                        flowU = 0f;
                        flowV = 0f;
                        return true;
                    }
                    SampleSeasonalOceanCurrent(world, u, v, month, out float cx, out float cy, out _, out _);
                    flowU = cx;
                    flowV = -cy;
                    return true;
                }
                default:
                    return false;
            }
        }

        // --- Explicit Procedural Bathymetric & Geomorphic Carving Operators (Stages 32, 37, 40, 41, 45, 51) ---

        /// <summary>
        /// Stage 32 (`fjords::generate_fjords`): Carves drowned coastal U-shaped glacial valleys (-30..-10 m)
        /// inland uphill from LGM-glaciated mountainous coastlines.
        /// </summary>
        public static int CarveGlacialFjords(WorldData world, uint seedOffset = 0, float acceptanceProbability = 0.75f)
        {
            if (world == null) return 0;
            int n = world.Count, width = world.Width, height = world.Height;
            float cellKm = 40030f / width;
            var rng = new WyRand((FMath.Seed(world.Seed) ^ 0xf108d500u) + seedOffset);

            var isSeed = new bool[n];
            var seeds = new List<int>();
            for (int t = 0; t < n; t++)
            {
                if (FlatTerrainAnalysis.AreaWater(world.Water[t])) continue;
                bool touchesOcean = false;
                float maxDiff = 0f;
                for (int d = 0; d < 4; d++)
                {
                    int nb = world.SphereNeighbor(t, d);
                    if (FlatTerrainAnalysis.Ocean(world.Water[nb])) touchesOcean = true;
                    else maxDiff = Math.Max(maxDiff, Math.Abs(world.Elevation[t] - world.Elevation[nb]));
                }
                if (!touchesOcean) continue;

                // Evaluate LGM glacier thickness
                float meanT = 0f, warmest = -1000f, rain = 0f;
                int b = t * 12;
                for (int m = 0; m < 12; m++)
                {
                    float tm = world.Temperature[b + m];
                    meanT += tm / 12f;
                    if (tm > warmest) warmest = tm;
                    rain += world.Precipitation[b + m];
                }
                float lgm = ComputeLgmIceMeters(true, world.Elevation[t], maxDiff, Math.Abs(world.Latitude(t)), meanT, warmest, rain);
                if (lgm <= 10.0f) continue;

                bool orogenic = maxDiff >= 300f || world.Elevation[t] >= 450;
                if (!orogenic && world.Tectonics != null)
                {
                    var s = world.Tectonics.Setting[t];
                    orogenic = s == TectonicSetting.Collision || s == TectonicSetting.Subduction || s == TectonicSetting.IslandArc;
                }
                if (!orogenic) continue;
                if (rng.NextFloat() >= acceptanceProbability) continue;

                bool adjSeed = false;
                for (int d = 0; d < 4; d++)
                    if (isSeed[world.SphereNeighbor(t, d)]) { adjSeed = true; break; }
                if (adjSeed) continue;

                isSeed[t] = true;
                seeds.Add(t);
            }

            var path = new List<int>(8);
            int carvedCount = 0;
            for (int s = 0; s < seeds.Count; s++)
            {
                int seed = seeds[s];
                if (FlatTerrainAnalysis.AreaWater(world.Water[seed])) continue;
                float lenKm = 20f + 50f * rng.NextFloat();
                int maxSteps = Math.Max(2, (int)Math.Ceiling(lenKm / Math.Max(25.231325f, cellKm)) + 1);
                float extraDepth = 10f + 10f * rng.NextFloat();

                path.Clear();
                path.Add(seed);
                int curr = seed;
                while (path.Count < maxSteps)
                {
                    int bestNb = -1;
                    short bestElev = short.MinValue;
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = world.SphereNeighbor(curr, d);
                        if (!FlatTerrainAnalysis.AreaWater(world.Water[nb]) && !path.Contains(nb) && world.Elevation[nb] > bestElev)
                        {
                            bestElev = world.Elevation[nb];
                            bestNb = nb;
                        }
                    }
                    if (bestNb < 0) break;
                    path.Add(bestNb);
                    curr = bestNb;
                }

                for (int k = 0; k < path.Count; k++)
                {
                    int tile = path[k];
                    float frac = 1.0f - ((k + 0.5f) / maxSteps);
                    short depth = (short)Math.Round(-10.0f - frac * extraDepth);
                    world.Elevation[tile] = depth;
                    world.WaterLevel[tile] = 0;
                    world.Water[tile] = HeightBands.Ocean(depth);
                    carvedCount++;
                }
            }
            return carvedCount;
        }

        /// <summary>
        /// Stage 51 (`coral_reefs::generate_coral_reefs`): Shoals reef tiles 97.5% toward [-5, -1) m limestone flats
        /// and raises rare isolated coral atoll cays ([1, 3] m).
        /// </summary>
        public static int ShoalCoralReefs(WorldData world, GlebaGeographyFacts geo, uint seedOffset = 0)
        {
            if (world == null || geo == null) return 0;
            var rng = new WyRand((FMath.Seed(world.Seed) ^ 0xc08a1ef0u) + seedOffset);
            int modified = 0;
            for (int t = 0; t < world.Count; t++)
            {
                if (!geo.HasFlag(t, GlebaGeoFlags.CoralReef)) continue;
                if (geo.HasFlag(t, GlebaGeoFlags.CoralCay))
                {
                    short cay = (short)rng.NextRange(1, 4);
                    world.Elevation[t] = cay;
                    world.WaterLevel[t] = cay;
                    world.Water[t] = WaterKind.Land;
                }
                else
                {
                    float targetDepth = 4f * rng.NextFloat() - 5f;
                    float elevNew = targetDepth + 0.025f * (world.Elevation[t] - targetDepth);
                    short bed = (short)FMath.Clamp((int)Math.Round(elevNew), -750, -1);
                    world.Elevation[t] = bed;
                    world.WaterLevel[t] = 0;
                    world.Water[t] = HeightBands.Ocean(bed);
                }
                modified++;
            }
            return modified;
        }

        /// <summary>
        /// Stage 37 / 40a (`erosion::thermal_erosion`): 10 synchronous Jacobi talus-relaxation passes
        /// smoothing land slopes steeper than 3.0 degrees half-way toward the neighbor mean.
        /// </summary>
        public static int ApplyThermalErosion(WorldData world, int iterations = 10)
        {
            if (world == null || iterations <= 0) return 0;
            int n = world.Count;
            double spacingMeters = (40030.0 / world.Width) * 1000.0;
            const double RadToDeg = 57.29577951308232;
            var meanNb = new float[n];
            int relaxed = 0;

            for (int iter = 0; iter < iterations; iter++)
            {
                for (int t = 0; t < n; t++)
                {
                    if (FlatTerrainAnalysis.AreaWater(world.Water[t])) continue;
                    float sum = 0f;
                    for (int d = 0; d < 4; d++) sum += world.Elevation[world.SphereNeighbor(t, d)];
                    meanNb[t] = sum * 0.25f;
                }
                for (int t = 0; t < n; t++)
                {
                    if (FlatTerrainAnalysis.AreaWater(world.Water[t]) || world.Elevation[t] < 1) continue;
                    double maxSlopeRad = 0.0;
                    double eT = Math.Max((int)world.Elevation[t], 0);
                    for (int d = 0; d < 4; d++)
                    {
                        double eNb = Math.Max((int)world.Elevation[world.SphereNeighbor(t, d)], 0);
                        double s = Math.Atan2(Math.Abs(eT - eNb), spacingMeters);
                        if (s > maxSlopeRad) maxSlopeRad = s;
                    }
                    if (maxSlopeRad * RadToDeg > 3.0)
                    {
                        float next = Math.Max(1f, world.Elevation[t] + 0.5f * (meanNb[t] - world.Elevation[t]));
                        short q = (short)FMath.Clamp((int)Math.Round(next), 1, HeightBands.MaximumBed);
                        if (q != world.Elevation[t])
                        {
                            world.Elevation[t] = q;
                            if (world.Water[t] == WaterKind.Land) world.WaterLevel[t] = q;
                            relaxed++;
                        }
                    }
                }
            }
            return relaxed;
        }

        /// <summary>
        /// Stage 40b (`erosion::sorted_erosion`): Two-pass elevation-sorted stream-power hydraulic incision
        /// (`slopeRad * sqrt(q)`) with aridity loss (`sqrt(raw)/humidity`) and safe half-drop pit prevention.
        /// </summary>
        public static float[] ApplySortedHydraulicErosion(WorldData world)
        {
            if (world == null) return Array.Empty<float>();
            int n = world.Count;
            double spacingMeters = (40030.0 / world.Width) * 1000.0;
            var etWaterflow = new float[n];
            var accFlow = new float[n];
            var rainfall = new float[n];
            var humidity = new float[n];
            var landTiles = new List<int>(n / 2);

            for (int t = 0; t < n; t++)
            {
                float rainSum = 0f;
                for (int m = 0; m < 12; m++) rainSum += world.Precipitation[t * 12 + m];
                rainfall[t] = rainSum / 12f;

                if (world.Moisture != null)
                {
                    float u = (t % world.Width + 0.5f) / world.Width;
                    float v = (t / world.Width + 0.5f) / world.Height;
                    float hSum = 0f;
                    for (int m = 0; m < 12; m++) hSum += world.Moisture.Sample(world.Moisture.Humidity, u, v, m);
                    humidity[t] = FMath.Clamp(hSum / 12f, 1e-4f, 1f);
                }
                else
                {
                    humidity[t] = FMath.Clamp(rainSum / 1200f, 0.05f, 1f);
                }

                if (!FlatTerrainAnalysis.AreaWater(world.Water[t])) landTiles.Add(t);
            }

            for (int pass = 0; pass < 2; pass++)
            {
                Array.Clear(etWaterflow, 0, n);
                Array.Clear(accFlow, 0, n);
                landTiles.Sort((a, b) =>
                {
                    int cmp = world.Elevation[b].CompareTo(world.Elevation[a]);
                    return cmp != 0 ? cmp : a.CompareTo(b);
                });

                for (int k = 0; k < landTiles.Count; k++)
                {
                    int u = landTiles[k];
                    float qIn = accFlow[u];
                    float raw = qIn + rainfall[u] - 10f;
                    float net = Math.Max(raw - (float)Math.Sqrt(Math.Max(raw, 0f)) / humidity[u], 0f);
                    float q = Math.Max(qIn, net);
                    etWaterflow[u] = q;

                    int v = world.Downstream[u];
                    if (v < 0) continue;
                    accFlow[v] += q;
                    etWaterflow[v] += q;

                    float eU = world.Elevation[u];
                    float maxDrop = 0.5f * (!FlatTerrainAnalysis.AreaWater(world.Water[v]) ? Math.Max(0f, eU - world.Elevation[v]) : Math.Max(0f, eU));
                    double slopeRad = 0.0;
                    double cU = Math.Max((double)eU, 0.0);
                    for (int d = 0; d < 4; d++)
                    {
                        double cNb = Math.Max((double)world.Elevation[world.SphereNeighbor(u, d)], 0.0);
                        double s = Math.Atan2(Math.Abs(cU - cNb), spacingMeters);
                        if (s > slopeRad) slopeRad = s;
                    }
                    float dh = Math.Min(maxDrop, (float)slopeRad * (float)Math.Sqrt(q));
                    if (FMath.Finite(dh) && dh >= 0.5f)
                    {
                        short next = (short)FMath.Clamp((int)Math.Round(eU - dh), 1, HeightBands.MaximumBed);
                        world.Elevation[u] = next;
                        if (world.Water[u] == WaterKind.Land) world.WaterLevel[u] = next;
                    }
                }
            }

            for (int t = 0; t < n; t++) etWaterflow[t] *= 0.5f;
            return etWaterflow;
        }

        /// <summary>
        /// Stage 41 (`tectonics::oceanography::lower_coastal_land_tiles`): Bevels coastal cliffs directly bordering
        /// oceanic trenches deeper than -4000 m by [600, 1000) m.
        /// </summary>
        public static int LowerCoastalTrenchCliffs(WorldData world, uint seedOffset = 0)
        {
            if (world == null) return 0;
            var rng = new WyRand((FMath.Seed(world.Seed) ^ 0x41c0a57u) + seedOffset);
            int lowered = 0;
            for (int t = 0; t < world.Count; t++)
            {
                if (FlatTerrainAnalysis.AreaWater(world.Water[t])) continue;
                short minNb = short.MaxValue;
                bool hasOceanNb = false;
                for (int d = 0; d < 4; d++)
                {
                    int nb = world.SphereNeighbor(t, d);
                    if (FlatTerrainAnalysis.Ocean(world.Water[nb]))
                    {
                        hasOceanNb = true;
                        if (world.Elevation[nb] < minNb) minNb = world.Elevation[nb];
                    }
                }
                if (hasOceanNb && minNb < -4000)
                {
                    int drop = (int)Math.Round(600f + 400f * rng.NextFloat());
                    short next = (short)FMath.Clamp(world.Elevation[t] - drop, HeightBands.MinimumBed, HeightBands.MaximumBed);
                    world.Elevation[t] = next;
                    if (next <= 0)
                    {
                        world.WaterLevel[t] = 0;
                        world.Water[t] = HeightBands.Ocean(next);
                    }
                    else
                    {
                        world.WaterLevel[t] = next;
                    }
                    lowered++;
                }
            }
            return lowered;
        }
    }
}
