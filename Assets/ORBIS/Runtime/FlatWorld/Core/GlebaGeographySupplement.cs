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
        Gneiss = 19,
        // Remaining 17 rock types from Gleba v0.3.6 mods/base/data/rock_types.lua (37 total)
        Breccia = 20,
        Granodiorite = 21,
        Peridotite = 22,
        Dacite = 23,
        Picrobasalt = 24,
        Marble = 25,
        Quartzite = 26,
        Skarn = 27,
        Hornfel = 28,
        Phyllite = 29,
        Migmatite = 30,
        Blueschist = 31,
        Eclogite = 32,
        Greenstone = 33,
        Amphibolite = 34,
        Greenschist = 35,
        Granulite = 36
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
        ArtesianAquifer = 1 << 4,
        LargeIgneousProvince = 1 << 5,
        PaleoOrogen = 1 << 6,
        MantleSuperswell = 1 << 7
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
        GeographicRegions = 14,
        RelativeHumidity = 15,
        GlebaBiomes = 16,
        GlebaFlora = 17,
        SoilTexture = 18,
        SoilFertility = 19,
        TectonicsAndVolcanism = 20,
        HydroGeoComposite = 21,
        CropsAndCarryingCapacity = 22
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
        // Stage 49 6-pass drainage-routed topsoil & erosion waterflow
        public readonly byte[] SoilSandByte, SoilSiltByte, SoilClayByte;
        public readonly ushort[] SoilDepthCentimeters;
        public readonly byte[] SoilRichnessByte, SoilPermeabilityByte, SoilOrganicsByte;
        public readonly float[] ErosionWaterflow;
        // Holdridge plant functional types & LGM ice thickness
        public readonly byte[] FloraTreesByte, FloraShrubsByte, FloraGrassByte, FloraTotalByte, FloraConiferByte;
        public readonly ushort[] LgmIceMeters;
        // Tectonics, microplates, orogeny & volcanism (procedural + Earth inferred)
        public readonly byte[] PlateId;
        public readonly ushort[] MicroplateId;
        public readonly TectonicSetting[] BoundarySetting;
        public readonly byte[] OrogenyStrengthByte;
        public readonly byte[] VolcanoFlags;
        public readonly byte[] VolcanoVeiTenths;
        // Stage 54 domesticable founder crops & Stages 61-62 civilization carrying capacity
        public readonly GlebaFounderCrop[] DominantCrop;
        public readonly byte[] CropSuitabilityByte;
        public readonly byte[] NitrogenFixingByte;
        public readonly byte[] CarryingCapacityByte;
        public int[] AreaToRegion;
        public int[] RegionToSuperRegion;
        public int[] SuperRegionToSubcontinent;
        public int AquiferCount;
        public int CoralReefTileCount;
        public int FjordCoastTileCount;
        public int VolcanoCount;
        public int ActiveVolcanoCount;
        public int LipTileCount;
        public int PaleoOrogenTileCount;
        public int CradleTileCount;
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
            SoilSandByte = new byte[count];
            SoilSiltByte = new byte[count];
            SoilClayByte = new byte[count];
            SoilDepthCentimeters = new ushort[count];
            SoilRichnessByte = new byte[count];
            SoilPermeabilityByte = new byte[count];
            SoilOrganicsByte = new byte[count];
            ErosionWaterflow = new float[count];
            FloraTreesByte = new byte[count];
            FloraShrubsByte = new byte[count];
            FloraGrassByte = new byte[count];
            FloraTotalByte = new byte[count];
            FloraConiferByte = new byte[count];
            LgmIceMeters = new ushort[count];
            PlateId = new byte[count];
            MicroplateId = new ushort[count];
            BoundarySetting = new TectonicSetting[count];
            OrogenyStrengthByte = new byte[count];
            VolcanoFlags = new byte[count];
            VolcanoVeiTenths = new byte[count];
            DominantCrop = new GlebaFounderCrop[count];
            CropSuitabilityByte = new byte[count];
            NitrogenFixingByte = new byte[count];
            CarryingCapacityByte = new byte[count];
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

        public GlebaSoilState GetSoilState(int tile)
        {
            if ((uint)tile >= (uint)Count || SoilDepthCentimeters[tile] == 0) return default;
            var s = new GlebaSoilState
            {
                Sand = SoilSandByte[tile] / 255f,
                Silt = SoilSiltByte[tile] / 255f,
                Clay = SoilClayByte[tile] / 255f,
                Depth = SoilDepthCentimeters[tile] * 0.01f,
                MineralRichness = SoilRichnessByte[tile] / 255f,
                Organics = SoilOrganicsByte[tile] / 255f
            };
            s.NormalizeTexture();
            return s;
        }

        public GlebaPlantCover GetFloraState(int tile)
        {
            if ((uint)tile >= (uint)Count) return default;
            return new GlebaPlantCover
            {
                Trees = FloraTreesByte[tile] / 255f,
                Shrubs = FloraShrubsByte[tile] / 255f,
                Grass = FloraGrassByte[tile] / 255f,
                TotalPlants = FloraTotalByte[tile] / 255f,
                ConifersVsBroadleaf = 1f - FloraConiferByte[tile] / 255f
            };
        }

        public bool TryGetVolcano(int tile, out VolcanoKind kind, out bool dormant, out float vei)
        {
            kind = VolcanoKind.Arc;
            dormant = false;
            vei = 0f;
            if ((uint)tile >= (uint)Count) return false;
            int code = VolcanoFlags[tile] & 0x07;
            if (code == 0) return false;
            kind = code == 1 ? VolcanoKind.Arc : code == 2 ? VolcanoKind.Hotspot : VolcanoKind.Rift;
            dormant = (VolcanoFlags[tile] & 0x10) != 0;
            vei = VolcanoVeiTenths[tile] * 0.1f;
            return true;
        }

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
                VolcanoCount = VolcanoCount,
                ActiveVolcanoCount = ActiveVolcanoCount,
                LipTileCount = LipTileCount,
                PaleoOrogenTileCount = PaleoOrogenTileCount,
                CradleTileCount = CradleTileCount,
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
            Array.Copy(SoilSandByte, c.SoilSandByte, Count);
            Array.Copy(SoilSiltByte, c.SoilSiltByte, Count);
            Array.Copy(SoilClayByte, c.SoilClayByte, Count);
            Array.Copy(SoilDepthCentimeters, c.SoilDepthCentimeters, Count);
            Array.Copy(SoilRichnessByte, c.SoilRichnessByte, Count);
            Array.Copy(SoilPermeabilityByte, c.SoilPermeabilityByte, Count);
            Array.Copy(SoilOrganicsByte, c.SoilOrganicsByte, Count);
            Array.Copy(ErosionWaterflow, c.ErosionWaterflow, Count);
            Array.Copy(FloraTreesByte, c.FloraTreesByte, Count);
            Array.Copy(FloraShrubsByte, c.FloraShrubsByte, Count);
            Array.Copy(FloraGrassByte, c.FloraGrassByte, Count);
            Array.Copy(FloraTotalByte, c.FloraTotalByte, Count);
            Array.Copy(FloraConiferByte, c.FloraConiferByte, Count);
            Array.Copy(LgmIceMeters, c.LgmIceMeters, Count);
            Array.Copy(PlateId, c.PlateId, Count);
            Array.Copy(MicroplateId, c.MicroplateId, Count);
            Array.Copy(BoundarySetting, c.BoundarySetting, Count);
            Array.Copy(OrogenyStrengthByte, c.OrogenyStrengthByte, Count);
            Array.Copy(VolcanoFlags, c.VolcanoFlags, Count);
            Array.Copy(VolcanoVeiTenths, c.VolcanoVeiTenths, Count);
            Array.Copy(DominantCrop, c.DominantCrop, Count);
            Array.Copy(CropSuitabilityByte, c.CropSuitabilityByte, Count);
            Array.Copy(NitrogenFixingByte, c.NitrogenFixingByte, Count);
            Array.Copy(CarryingCapacityByte, c.CarryingCapacityByte, Count);
            return c;
        }
    }

    /// <summary>
    /// Port of Gleba v0.3.6 remaining geographic, oceanographic, hydrogeological, erosion, and mapmode systems:
    /// - Complete 37-rock bedrock, felsicity/quartz/calcite & porosity registry (`core/data.lua` + `rock_types.lua`)
    /// - Large Igneous Provinces (LIPs), mantle superswells & eroded paleo-orogens (`lips`, `superswells`, `old_features`)
    /// - Deep confined/unconfined groundwater aquifers (`aquifer_gen::generate_aquifers`)
    /// - Tropical coral reefs & emergent coral cays (`coral_reefs::generate_coral_reefs`)
    /// - Glacial fjords & LGM snowline mass balance (`fjords::generate_fjords`)
    /// - Endorheic saltwater vs outflowing freshwater lakes (`lakes::generate_*_lakes`)
    /// - Global ocean inaccessibility, 8-domain 4-tier geographic hierarchy & biogeographic realms
    /// - Wild founder crops & civilization cradle carrying capacity (`plants.lua` + `find_civilization_spots`)
    /// - 12-month seasonal ocean surface currents & 11-stop Cividis / flowfield mapmode evaluators
    /// - Thermal angle-of-repose & sorted stream-power hydraulic erosion operators
    /// </summary>
    public static class GlebaGeographySupplement
    {
        const double EarthRadiusKm = 6371.0088;
        const double NominalPlanetAreaKm2 = 510064472.0;

        // Complete 37-entry rock registry matching Gleba v0.3.6 `core/data.lua` + `mods/base/data/rock_types.lua`
        static readonly GlebaRockProperties[] RockTable = new GlebaRockProperties[]
        {
            new GlebaRockProperties(GlebaRockKind.Sandstone,    0.75f, 0.15f, 0.10f, 0.7000f, 0.40f, 0.20f, false, 200, 190,  15),
            new GlebaRockProperties(GlebaRockKind.Conglomerate, 0.95f, 0.05f, 0.00f, 0.4000f, 0.20f, 0.15f, false, 190, 155, 165),
            new GlebaRockProperties(GlebaRockKind.Siltstone,    0.25f, 0.50f, 0.25f, 1.0000f, 0.20f, 0.30f, false, 166, 131,  17),
            new GlebaRockProperties(GlebaRockKind.Mudstone,     0.25f, 0.20f, 0.55f, 1.0000f, 0.40f, 0.20f, false, 163,  78,  30),
            new GlebaRockProperties(GlebaRockKind.Claystone,    0.20f, 0.10f, 0.70f, 1.0000f, 0.44f, 0.10f, false, 160,  40,  46),
            new GlebaRockProperties(GlebaRockKind.Shale,        0.20f, 0.40f, 0.40f, 1.0000f, 0.50f, 0.50f, false, 239, 228, 176),
            new GlebaRockProperties(GlebaRockKind.Chalk,        0.25f, 0.45f, 0.30f, 0.3000f, 0.20f, 0.25f, false, 195, 220, 195),
            new GlebaRockProperties(GlebaRockKind.Limestone,    0.35f, 0.40f, 0.25f, 0.2500f, 0.27f, 0.50f, true,   82, 242,  77),
            new GlebaRockProperties(GlebaRockKind.Dolostone,    0.30f, 0.35f, 0.35f, 0.2000f, 0.27f, 0.40f, false,  34, 177,  76),
            new GlebaRockProperties(GlebaRockKind.Chert,        0.35f, 0.40f, 0.25f, 0.5000f, 0.22f, 0.40f, false,  30, 147,  56),
            new GlebaRockProperties(GlebaRockKind.Halite,       0.40f, 0.30f, 0.30f, 0.2000f, 1.00f, 0.20f, false, 255, 235, 235),
            new GlebaRockProperties(GlebaRockKind.Granite,      0.70f, 0.10f, 0.20f, 0.5000f, 0.53f, 0.05f, false, 190, 100, 220),
            new GlebaRockProperties(GlebaRockKind.Diorite,      0.65f, 0.00f, 0.35f, 0.7500f, 0.80f, 0.03f, false, 150,  60, 180),
            new GlebaRockProperties(GlebaRockKind.Gabbro,       0.35f, 0.00f, 0.66f, 0.8000f, 1.00f, 0.02f, false, 130,  40, 160),
            new GlebaRockProperties(GlebaRockKind.Basalt,       0.10f, 0.45f, 0.45f, 1.0000f, 1.00f, 0.45f, false,  10,  18, 165),
            new GlebaRockProperties(GlebaRockKind.Andesite,     0.20f, 0.45f, 0.35f, 0.9000f, 0.73f, 0.40f, false,  11,  19, 190),
            new GlebaRockProperties(GlebaRockKind.Rhyolite,     0.40f, 0.30f, 0.30f, 0.8000f, 0.53f, 0.30f, false,  15,  21, 239),
            new GlebaRockProperties(GlebaRockKind.Slate,        0.40f, 0.05f, 0.55f, 0.7000f, 0.50f, 0.30f, false,  56,  56,  56),
            new GlebaRockProperties(GlebaRockKind.Schist,       0.30f, 0.00f, 0.70f, 0.3000f, 0.66f, 0.20f, false, 100, 100, 100),
            new GlebaRockProperties(GlebaRockKind.Gneiss,       0.20f, 0.00f, 0.80f, 0.2000f, 1.00f, 0.15f, false, 120, 120, 120),
            new GlebaRockProperties(GlebaRockKind.Breccia,      0.95f, 0.05f, 0.00f, 0.3000f, 0.10f, 0.15f, false, 180, 124, 135),
            new GlebaRockProperties(GlebaRockKind.Granodiorite, 0.65f, 0.05f, 0.30f, 0.6250f, 0.66f, 0.04f, false, 170,  80, 200),
            new GlebaRockProperties(GlebaRockKind.Peridotite,   0.25f, 0.00f, 0.75f, 1.0000f, 0.80f, 0.01f, false, 110,  20, 140),
            new GlebaRockProperties(GlebaRockKind.Dacite,       0.30f, 0.40f, 0.30f, 0.8500f, 0.60f, 0.35f, false,  13,  20, 211),
            new GlebaRockProperties(GlebaRockKind.Picrobasalt,  0.00f, 0.50f, 0.50f, 1.0000f, 0.87f, 0.50f, false,   6,  15, 140),
            new GlebaRockProperties(GlebaRockKind.Marble,       0.40f, 0.30f, 0.30f, 0.2000f, 0.33f, 0.15f, false, 200, 200, 250),
            new GlebaRockProperties(GlebaRockKind.Quartzite,    0.45f, 0.30f, 0.25f, 0.2000f, 0.26f, 0.15f, false, 250, 200, 200),
            new GlebaRockProperties(GlebaRockKind.Skarn,        0.15f, 0.25f, 0.55f, 0.1000f, 0.33f, 0.15f, false,  40, 145, 145),
            new GlebaRockProperties(GlebaRockKind.Hornfel,      0.20f, 0.20f, 0.60f, 0.3000f, 0.50f, 0.15f, false,  20, 223, 200),
            new GlebaRockProperties(GlebaRockKind.Phyllite,     0.40f, 0.00f, 0.60f, 0.4000f, 0.57f, 0.25f, false,  85,  85,  85),
            new GlebaRockProperties(GlebaRockKind.Migmatite,    0.10f, 0.00f, 0.90f, 0.0750f, 1.00f, 0.10f, false, 143, 143, 143),
            new GlebaRockProperties(GlebaRockKind.Blueschist,   0.00f, 0.40f, 0.60f, 0.3750f, 0.83f, 0.01f, false,   0,  50, 100),
            new GlebaRockProperties(GlebaRockKind.Eclogite,     0.00f, 0.30f, 0.70f, 0.2400f, 0.90f, 0.01f, false,   0,  50,  50),
            new GlebaRockProperties(GlebaRockKind.Greenstone,   0.30f, 0.30f, 0.40f, 0.1500f, 0.97f, 0.01f, false,   0, 100,   0),
            new GlebaRockProperties(GlebaRockKind.Amphibolite,  0.20f, 0.20f, 0.60f, 0.1000f, 1.00f, 0.01f, false,   0,  50,   0),
            new GlebaRockProperties(GlebaRockKind.Greenschist,  0.10f, 0.10f, 0.80f, 0.0750f, 0.93f, 0.01f, false,   0,  75,   0),
            new GlebaRockProperties(GlebaRockKind.Granulite,    0.00f, 0.00f, 1.00f, 0.0625f, 0.66f, 0.01f, false,   0,  25,   0)
        };

        // 11-stop Cividis colormap (.rdata@0x1822f89b0 in gleba_godot.dll)
        static readonly byte[] CividisR = { 0, 8, 53, 79, 102, 125, 149, 175, 201, 229, 254 };
        static readonly byte[] CividisG = { 34, 51, 69, 87, 105, 124, 143, 163, 184, 207, 232 };
        static readonly byte[] CividisB = { 78, 112, 108, 108, 112, 120, 119, 113, 102, 82, 55 };

        public static int RockTypeCount => RockTable.Length;
        public static GlebaRockProperties GetRockProperties(GlebaRockKind kind) => RockTable[(int)kind];

        /// <summary>
        /// Evaluates per-microplate normalized (quartz, calcite) matching `get_microplate_quartz_and_calcite` (0x1808f8220)
        /// plus terrane felsicity, dolomitization, and chalkification parameters.
        /// </summary>
        public static void GetMicroplateGeochemistry(
            ushort microplateId, uint worldSeed,
            out float felsicity, out float quartz, out float calcite, out float dolomitization, out float chalkification)
        {
            uint baseKey = unchecked(worldSeed ^ ((uint)microplateId * 0x9e3779b9u));
            var r1 = new WyRand(baseKey + 1u);
            var r2 = new WyRand(baseKey + 2u);
            float qRaw = r1.NextFloat();
            float cRaw = r2.NextFloat();
            float sum = Math.Max(qRaw + cRaw, 1e-5f);
            quartz = qRaw / sum;
            calcite = cRaw / sum;
            felsicity = 0.40f + 0.42f * FMath.Unit(baseKey ^ 0x13572468u);
            dolomitization = FMath.Unit(baseKey ^ 0x24681357u);
            chalkification = FMath.Unit(baseKey ^ 0x89abcdefu);
        }

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
                    float warmest = -1000f, coldest = 1000f, minRain = float.MaxValue;
                    int tBase = i * 12;
                    for (int m = 0; m < 12; m++)
                    {
                        float tm = w.Temperature[tBase + m];
                        float rm = w.Precipitation[tBase + m];
                        if (tm > warmest) warmest = tm;
                        if (tm < coldest) coldest = tm;
                        if (rm < minRain) minRain = rm;
                    }

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
                    geo.LgmIceMeters[i] = (ushort)FMath.Clamp((int)Math.Round(lgm), 0, 65535);
                    if (lgm > 0f) geo.Flags[i] |= GlebaGeoFlags.LgmGlaciated;

                    var flora = GlebaEcologySupplement.EvaluateFlora(
                        isLand, isLand ? f.GroundedIceMeters[i] : 0f, w.Elevation[i], absLatDeg,
                        f.Biotemperature[i], f.AnnualRain[i], minRain, coldest);
                    geo.FloraTreesByte[i] = (byte)FMath.Clamp((int)Math.Round(flora.Trees * 255f), 0, 255);
                    geo.FloraShrubsByte[i] = (byte)FMath.Clamp((int)Math.Round(flora.Shrubs * 255f), 0, 255);
                    geo.FloraGrassByte[i] = (byte)FMath.Clamp((int)Math.Round(flora.Grass * 255f), 0, 255);
                    geo.FloraTotalByte[i] = (byte)FMath.Clamp((int)Math.Round(flora.TotalPlants * 255f), 0, 255);
                    geo.FloraConiferByte[i] = (byte)FMath.Clamp((int)Math.Round(flora.ConiferFraction * 255f), 0, 255);
                }
            });

            // 1b. Tectonic plates, microplate terranes, orogeny strength & volcanism (procedural + Earth inferred)
            BuildTectonicsAndVolcanism(w, f, geo, ux, uy, uz, fbmNoise, baseSeed, options);

            Parallel.For(0, height, options, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    geo.Bedrock[i] = ClassifyTileBedrock(w, f, geo, i, ux[i], uy[i], uz[i], fbmNoise[i], baseSeed);
                }
            });

            // 2. Ocean waterbodies, PrimaryOcean vs InlandSea, Lake salinity/origin & Inaccessibility BFS
            ClassifyWaterbodiesAndInaccessibility(w, f, geo, lakeRoots, lgmIce, cancel);

            // 3. Glacial Fjord Coast evaluation (Stage 32 criteria: LGM ice > 10m + coastal orogeny/mountain relief)
            EvaluateFjordCoasts(w, f, geo, lgmIce);

            // 4. Tropical Coral Reefs (Stage 51: T_min >= 15.5 C, warm shallow shelf/seamounts, best-first growth)
            GenerateCoralReefs(w, geo, ux, uy, uz, fbmNoise, tileAreaKm2, baseSeed);

            // 4b. Stage 49 6-Pass Drainage-Routed Soils & Erosion Waterflow (bedrock + LGM loess + aeolian + fluvial)
            BuildRoutedSoils(w, f, geo, lgmIce, ux, uy, uz, baseSeed, cancel);

            // 5. Deep Groundwater Aquifers (Stage 50: porous >= 0.16 unglaciated land, +-750m structural band)
            GenerateAquifers(w, f, geo, ux, uy, uz, fbmNoise, tileAreaKm2, baseSeed, cancel);

            // 6. Physiographic Domains, 4-Tier Geographic Hierarchy & Biogeographic Realms
            BuildDomainsRealmsAndHierarchy(w, f, geo, ux, uy, uz, tileAreaKm2, baseSeed, cancel);

            // 7. Stage 54 Domesticable Wild Founder Crops & Stages 61-62 Settlement / Carrying Capacity
            BuildFounderCropsAndCarryingCapacity(w, f, geo, cancel);

            return geo;
        }

        static void BuildFounderCropsAndCarryingCapacity(
            WorldData w, FlatTerrainFacts f, GlebaGeographyFacts geo, CancellationToken cancel)
        {
            int n = w.Count, width = w.Width;
            var moisture = w.Moisture;
            int cradles = 0;

            for (int i = 0; i < n; i++)
            {
                if ((i & 8191) == 0) cancel.ThrowIfCancellationRequested();
                bool isLand = !FlatTerrainAnalysis.AreaWater(w.Water[i]);
                if (!isLand) continue;

                float rh = 0.5f;
                if (moisture != null)
                {
                    float u = (i % width + 0.5f) / width;
                    float v = (i / width + 0.5f) / w.Height;
                    float sum = 0f;
                    for (int m = 0; m < 12; m++) sum += moisture.Sample(moisture.Humidity, u, v, m);
                    rh = FMath.Clamp(sum / 12f, 0f, 1f);
                }
                else
                {
                    rh = FMath.Clamp(f.AnnualRain[i] / 1200f, 0.05f, 1f);
                }

                var soil = geo.GetSoilState(i);
                bool isCoast = w.CoastDistance[i] <= 1;
                var biome = f.GlebaBiomes != null ? f.GlebaBiomes[i] : GlebaBiomeKind.TemperateGrasslandsSavannasAndShrublands;
                var eval = GlebaEcologySupplement.EvaluateFounderCropsAndCarryingCapacity(
                    true, isCoast, w.Elevation[i], Math.Abs(w.Latitude(i)),
                    f.MeanTemperature[i], f.AnnualRain[i], rh,
                    geo.ErosionWaterflow[i], biome, in soil);

                geo.DominantCrop[i] = eval.DominantCrop;
                geo.CropSuitabilityByte[i] = (byte)FMath.Clamp((int)Math.Round(eval.MaxSuitability * 255f), 0, 255);
                geo.NitrogenFixingByte[i] = (byte)FMath.Clamp((int)Math.Round(eval.NitrogenFixing * 255f), 0, 255);
                geo.CarryingCapacityByte[i] = (byte)FMath.Clamp((int)Math.Round(eval.CarryingCapacity * 255f), 0, 255);
                if (eval.CarryingCapacity >= 0.58f) cradles++;
            }
            geo.CradleTileCount = cradles;
        }

        static void StampLipsPaleoOrogensAndSuperswells(
            WorldData w, FlatTerrainFacts f, GlebaGeographyFacts geo,
            float[] ux, float[] uy, float[] uz, float[] fbmNoise, uint seed)
        {
            int n = w.Count;
            if (w.Preset != TerrainPreset.Earth && w.Tectonics != null && w.Tectonics.Province != null && w.Tectonics.Province.Length == n)
            {
                var prov = w.Tectonics.Province;
                int lipAuth = 0, paleoAuth = 0;
                for (int i = 0; i < n; i++)
                {
                    var p = prov[i];
                    if ((p & GlebaTectonicProvince.LargeIgneousProvince) != 0)
                    {
                        geo.Flags[i] |= GlebaGeoFlags.LargeIgneousProvince;
                        lipAuth++;
                    }
                    if ((p & GlebaTectonicProvince.PaleoOrogen) != 0)
                    {
                        geo.Flags[i] |= GlebaGeoFlags.PaleoOrogen;
                        if (geo.OrogenyStrengthByte[i] < 92) geo.OrogenyStrengthByte[i] = 92;
                        paleoAuth++;
                    }
                    if ((p & GlebaTectonicProvince.MantleSuperswell) != 0)
                        geo.Flags[i] |= GlebaGeoFlags.MantleSuperswell;
                }
                geo.LipTileCount = lipAuth;
                geo.PaleoOrogenTileCount = paleoAuth;
                return;
            }

            const int LipProvinceCount = 5;
            var lx = new float[LipProvinceCount];
            var ly = new float[LipProvinceCount];
            var lz = new float[LipProvinceCount];
            var lRadiusCos = new float[LipProvinceCount];

            if (w.Preset == TerrainPreset.Earth)
            {
                // Anchor to Earth's 5 major Phanerozoic continental flood-basalt traps:
                // Deccan (76E,19N), Siberian (98E,64N), Ethiopian (38.5E,10.5N), Parana (-52W,-24S), Columbia (-118.5W,46N)
                float[] lons = { 76f, 98f, 38.5f, -52f, -118.5f };
                float[] lats = { 19f, 64f, 10.5f, -24f, 46f };
                float[] radKm = { 560f, 920f, 480f, 620f, 380f };
                for (int k = 0; k < LipProvinceCount; k++)
                {
                    double la = lats[k] * (Math.PI / 180.0), lo = lons[k] * (Math.PI / 180.0);
                    float cl = (float)Math.Cos(la);
                    lx[k] = cl * (float)Math.Cos(lo);
                    ly[k] = cl * (float)Math.Sin(lo);
                    lz[k] = (float)Math.Sin(la);
                    lRadiusCos[k] = (float)Math.Cos(radKm[k] / EarthRadiusKm);
                }
            }
            else
            {
                var rng = new WyRand(seed ^ 0x1197ba5aUL);
                for (int k = 0; k < LipProvinceCount; k++)
                {
                    float z = 1.6f * rng.NextFloat() - 0.8f;
                    float a = 6.2831853f * rng.NextFloat();
                    float r = (float)Math.Sqrt(Math.Max(0f, 1f - z * z));
                    lx[k] = r * (float)Math.Cos(a);
                    ly[k] = r * (float)Math.Sin(a);
                    lz[k] = z;
                    float radKm = 420f + 520f * rng.NextFloat();
                    lRadiusCos[k] = (float)Math.Cos(radKm / EarthRadiusKm);
                }
            }

            int lipCount = 0, paleoCount = 0;
            for (int i = 0; i < n; i++)
            {
                bool ocean = FlatTerrainAnalysis.Ocean(w.Water[i]);
                float elev = w.Elevation[i];
                var setting = geo.BoundarySetting[i];
                float warp = 0.032f * fbmNoise[i];

                // Stage 12 (`lips::generate_large_igneous_provinces`): flood-basalt traps
                for (int k = 0; k < LipProvinceCount; k++)
                {
                    float dot = ux[i] * lx[k] + uy[i] * ly[k] + uz[i] * lz[k] + warp;
                    if (dot >= lRadiusCos[k] && (!ocean ? elev >= 120f && elev <= 2800f : elev >= -3800f))
                    {
                        geo.Flags[i] |= GlebaGeoFlags.LargeIgneousProvince;
                        lipCount++;
                        break;
                    }
                }

                // Stages 2 & 11 (`old_features::generate_old_mountains_and_hills`): eroded intraplate paleo-orogens
                if (!ocean && setting == TectonicSetting.Interior && elev >= 340f && elev <= 1650f && f.Relief[i] >= 280f)
                {
                    float sutureNoise = SphericalFbm6(ux[i], uz[i], uy[i], 4.6f, seed ^ 0x01d0809eu);
                    if (sutureNoise > -0.08f)
                    {
                        geo.Flags[i] |= GlebaGeoFlags.PaleoOrogen;
                        if (geo.OrogenyStrengthByte[i] < 92) geo.OrogenyStrengthByte[i] = 92;
                        paleoCount++;
                    }
                }

                // Stage 17 (`superswells::generate_superswells`): broad dynamic-topography mantle superswell plateaus
                if (!ocean && setting == TectonicSetting.Interior && elev >= 720f && elev <= 2100f && f.Relief[i] < 420f)
                {
                    float swell = SphericalFbm6(uy[i], ux[i], uz[i], 1.65f, seed ^ 0x509e85e1u);
                    if (swell > 0.12f)
                        geo.Flags[i] |= GlebaGeoFlags.MantleSuperswell;
                }
            }
            geo.LipTileCount = lipCount;
            geo.PaleoOrogenTileCount = paleoCount;
        }

        static void BuildTectonicsAndVolcanism(
            WorldData w, FlatTerrainFacts f, GlebaGeographyFacts geo,
            float[] ux, float[] uy, float[] uz, float[] fbmNoise,
            uint seed, ParallelOptions options)
        {
            int n = w.Count, width = w.Width, height = w.Height;
            var tec = w.Tectonics;

            // Generate 64 warped spherical microplate terrane seeds (Gleba TectonicMicroplates)
            const int MicroCount = 64;
            var mx = new float[MicroCount];
            var my = new float[MicroCount];
            var mz = new float[MicroCount];
            var rng = new WyRand(seed ^ 0x7ec7031cUL);
            for (int k = 0; k < MicroCount; k++)
            {
                float z = 2f * rng.NextFloat() - 1f;
                float a = 6.2831853f * rng.NextFloat();
                float r = (float)Math.Sqrt(Math.Max(0f, 1f - z * z));
                mx[k] = r * (float)Math.Cos(a);
                my[k] = r * (float)Math.Sin(a);
                mz[k] = z;
            }

            if (tec != null)
            {
                Parallel.For(0, height, options, y =>
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int i = row + x;
                        geo.PlateId[i] = tec.Plate[i];
                        geo.BoundarySetting[i] = tec.Setting[i];
                        float orog = FMath.Clamp(tec.Seismicity[i] * 0.65f + Math.Max(0f, w.Elevation[i]) / 5200f * 0.35f, 0f, 1f);
                        geo.OrogenyStrengthByte[i] = (byte)Math.Round(orog * 255f);

                        if (tec.Microplate != null && tec.Microplate.Length == n)
                        {
                            geo.MicroplateId[i] = tec.Microplate[i];
                        }
                        else
                        {
                            float wx = ux[i] + 0.24f * fbmNoise[i];
                            float wy = uy[i] + 0.24f * SphericalFbm6(uy[i], uz[i], ux[i], 3.1f, seed ^ 0x55aa11u);
                            float wz = uz[i] + 0.18f * fbmNoise[i];
                            int bestM = 0; float bestDot = -9f;
                            for (int k = 0; k < MicroCount; k++)
                            {
                                float d = wx * mx[k] + wy * my[k] + wz * mz[k];
                                if (d > bestDot) { bestDot = d; bestM = k; }
                            }
                            geo.MicroplateId[i] = (ushort)((tec.Plate[i] * MicroCount) + bestM);
                        }
                    }
                });

                int vCount = 0, activeCount = 0;
                if (tec.Volcanoes != null)
                {
                    for (int k = 0; k < tec.Volcanoes.Length; k++)
                    {
                        var v = tec.Volcanoes[k];
                        if ((uint)v.Tile >= (uint)n) continue;
                        byte code = v.Kind == VolcanoKind.Arc ? (byte)1 : v.Kind == VolcanoKind.Hotspot ? (byte)2 : (byte)3;
                        bool dormant = v.Activity < 0.42f;
                        if (dormant) code |= 0x10;
                        if (v.Kind == VolcanoKind.Hotspot && v.Activity >= 0.85f) code |= 0x20;
                        float baseVei = v.Kind == VolcanoKind.Arc ? 3.8f : v.Kind == VolcanoKind.Rift ? 2.6f : 2.1f;
                        float vei = FMath.Clamp(baseVei + 2.8f * v.Activity * FMath.Unit(FMath.Hash((uint)v.Tile ^ seed)), 1.0f, 7.5f);
                        if (geo.VolcanoFlags[v.Tile] == 0) vCount++;
                        if (!dormant && (geo.VolcanoFlags[v.Tile] == 0 || (geo.VolcanoFlags[v.Tile] & 0x10) != 0)) activeCount++;
                        geo.VolcanoFlags[v.Tile] = code;
                        geo.VolcanoVeiTenths[v.Tile] = (byte)FMath.Clamp((int)Math.Round(vei * 10f), 10, 78);
                    }
                }
                geo.VolcanoCount = vCount;
                geo.ActiveVolcanoCount = activeCount;
                StampLipsPaleoOrogensAndSuperswells(w, f, geo, ux, uy, uz, fbmNoise, seed);
                return;
            }

            // Earth / imported DEM tectonic & volcanic inference from bathymetry, trenches, ridges & orogens
            const int MajorCount = 14;
            var px = new float[MajorCount];
            var py = new float[MajorCount];
            var pz = new float[MajorCount];
            for (int k = 0; k < MajorCount; k++)
            {
                float z = 1.65f * ((k + 0.5f) / MajorCount - 0.5f);
                float a = k * 2.3999632f + 0.45f;
                float r = (float)Math.Sqrt(Math.Max(0f, 1f - z * z));
                px[k] = r * (float)Math.Cos(a);
                py[k] = r * (float)Math.Sin(a);
                pz[k] = z;
            }

            int searchR = Math.Max(2, (int)Math.Round(320f / Math.Max(20f, geo.CellKm)));
            Parallel.For(0, height, options, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    float wx = ux[i] + 0.28f * fbmNoise[i];
                    float wy = uy[i] + 0.28f * SphericalFbm6(uy[i], uz[i], ux[i], 2.3f, seed ^ 0x9911u);
                    float wz = uz[i] + 0.20f * fbmNoise[i];

                    int bestP = 0; float bestPD = -9f;
                    for (int k = 0; k < MajorCount; k++)
                    {
                        float d = wx * px[k] + wy * py[k] + wz * pz[k];
                        if (d > bestPD) { bestPD = d; bestP = k; }
                    }
                    geo.PlateId[i] = (byte)bestP;

                    int bestM = 0; float bestMD = -9f;
                    for (int k = 0; k < MicroCount; k++)
                    {
                        float d = wx * mx[k] + wy * my[k] + wz * mz[k];
                        if (d > bestMD) { bestMD = d; bestM = k; }
                    }
                    geo.MicroplateId[i] = (ushort)(bestP * MicroCount + bestM);

                    float elev = w.Elevation[i];
                    bool ocean = FlatTerrainAnalysis.Ocean(w.Water[i]);
                    short minSea = 0, maxLand = 0, minSeaLocal = 0;
                    for (int dy = -searchR; dy <= searchR; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height) continue;
                        for (int dx = -searchR; dx <= searchR; dx++)
                        {
                            if (dx * dx + dy * dy > searchR * searchR) continue;
                            int nb = ny * width + FMath.Wrap(x + dx, width);
                            short eNb = w.Elevation[nb];
                            if (FlatTerrainAnalysis.Ocean(w.Water[nb]))
                            {
                                if (eNb < minSea) minSea = eNb;
                                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && eNb < minSeaLocal) minSeaLocal = eNb;
                            }
                            else if (eNb > maxLand) maxLand = eNb;
                        }
                    }

                    short trenchThresh = width <= 256 ? (short)-3500 : (short)-4200;
                    TectonicSetting setting = TectonicSetting.Interior;
                    float orog = 0f;
                    if (!ocean && minSea <= trenchThresh && maxLand >= 280 && w.CoastDistance[i] <= searchR + 2)
                    {
                        setting = w.CoastDistance[i] <= 2 && elev < 1600f ? TectonicSetting.IslandArc : TectonicSetting.Subduction;
                        orog = FMath.Clamp((-minSea - 3200f) / 3800f * 0.65f + f.Relief[i] / 2500f * 0.35f, 0.35f, 1f);
                    }
                    else if (ocean && elev <= trenchThresh - 300 && maxLand >= 200)
                    {
                        setting = TectonicSetting.Subduction;
                        orog = FMath.Clamp((-elev - 3600f) / 4000f, 0.30f, 1f);
                    }
                    else if (!ocean && (elev >= 2400f || (elev >= 1300f && f.Relief[i] >= 950f)))
                    {
                        setting = TectonicSetting.Collision;
                        orog = FMath.Clamp(elev / 5000f * 0.6f + f.Relief[i] / 2200f * 0.4f, 0.35f, 1f);
                    }
                    else if (!ocean && w.Water[i] == WaterKind.Lake && f.Depth[i] >= 140f && f.Relief[i] >= 400f)
                    {
                        setting = TectonicSetting.Rift;
                        orog = 0.45f;
                    }
                    else if (ocean && w.CoastDistance[i] > searchR && elev >= -3400f && elev <= -1400f && minSea <= elev - 750f)
                    {
                        setting = TectonicSetting.Ridge;
                        orog = 0.38f;
                    }
                    else
                    {
                        orog = FMath.Clamp(f.Relief[i] / 3200f, 0f, 0.35f);
                    }

                    geo.BoundarySetting[i] = setting;
                    geo.OrogenyStrengthByte[i] = (byte)Math.Round(orog * 255f);
                }
            });

            // Deterministic volcanic centers along inferred subduction arcs, rifts, and oceanic seamount hotspots
            int volTotal = 0, volActive = 0;
            int minSpacing = Math.Max(2, width / 96);
            short minArcElev = width <= 256 ? (short)260 : (short)480;
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    var s = geo.BoundarySetting[i];
                    bool ocean = FlatTerrainAnalysis.Ocean(w.Water[i]);
                    byte code = 0;
                    if (!ocean && (s == TectonicSetting.Subduction || s == TectonicSetting.IslandArc) && w.Elevation[i] >= minArcElev)
                        code = 1; // Arc stratovolcano
                    else if (!ocean && s == TectonicSetting.Rift && f.Relief[i] >= 380f)
                        code = 3; // Continental rift volcano
                    else if (!ocean && w.CoastDistance[i] <= 2 && w.Elevation[i] >= 850 && f.Relief[i] >= 850f && s != TectonicSetting.Collision)
                        code = 2; // High volcanic island hotspot (Hawaii, Tenerife, Reunion, Iceland)

                    if (code == 0) continue;

                    // Require local elevation peak within minSpacing
                    short eCenter = w.Elevation[i];
                    uint hCenter = FMath.Hash((uint)i ^ (seed + 0x701ca0u));
                    bool peak = width < 1024 || (hCenter & 1u) == 0;
                    for (int dy = -minSpacing; dy <= minSpacing && peak; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height) continue;
                        for (int dx = -minSpacing; dx <= minSpacing; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nb = ny * width + FMath.Wrap(x + dx, width);
                            if (w.Elevation[nb] > eCenter || (w.Elevation[nb] == eCenter && nb < i))
                            {
                                peak = false;
                                break;
                            }
                        }
                    }
                    if (!peak) continue;

                    bool dormant = ((hCenter >> 8) & 3u) == 0;
                    if (dormant) code |= 0x10;
                    if ((code & 0x07) == 2) code |= 0x20;
                    float vei = (code & 0x07) == 1
                        ? 3.4f + 3.2f * FMath.Unit(hCenter ^ 0x1234u)
                        : 2.0f + 2.4f * FMath.Unit(hCenter ^ 0x5678u);
                    geo.VolcanoFlags[i] = code;
                    geo.VolcanoVeiTenths[i] = (byte)FMath.Clamp((int)Math.Round(vei * 10f), 10, 75);
                    volTotal++;
                    if (!dormant) volActive++;
                }
            }
            geo.VolcanoCount = volTotal;
            geo.ActiveVolcanoCount = volActive;
            StampLipsPaleoOrogensAndSuperswells(w, f, geo, ux, uy, uz, fbmNoise, seed);
        }

        static void BuildRoutedSoils(
            WorldData w, FlatTerrainFacts f, GlebaGeographyFacts geo, float[] lgmIce,
            float[] ux, float[] uy, float[] uz, uint seed, CancellationToken cancel)
        {
            int n = w.Count;
            var soils = new GlebaSoilState[n];
            var humidity = new float[n];
            var moisture = w.Moisture;

            // Passes 1-3: Initialize from bedrock -> LGM glacier loess -> Aeolian desert deflation
            for (int i = 0; i < n; i++)
            {
                if ((i & 8191) == 0) cancel.ThrowIfCancellationRequested();
                bool isLand = !FlatTerrainAnalysis.AreaWater(w.Water[i]);
                if (moisture != null)
                {
                    float u = (i % w.Width + 0.5f) / w.Width;
                    float v = (i / w.Width + 0.5f) / w.Height;
                    float hSum = 0f;
                    for (int m = 0; m < 12; m++) hSum += moisture.Sample(moisture.Humidity, u, v, m);
                    humidity[i] = FMath.Clamp(hSum / 12f, 0.02f, 1f);
                }
                else
                {
                    humidity[i] = FMath.Clamp(f.AnnualRain[i] / 1200f, 0.05f, 1f);
                }

                if (!isLand) continue;
                var rp = RockTable[(int)geo.Bedrock[i]];
                bool coastalBeach = w.CoastDistance[i] <= 1 && w.Elevation[i] <= 90 && w.Water[i] != WaterKind.River && f.Forms[i] != Landform.Floodplain;
                float n0 = SphericalFbm6(ux[i], uy[i], uz[i], 5.1f, seed ^ 0x501101u);
                float n1 = SphericalFbm6(uy[i], uz[i], ux[i], 5.3f, seed ^ 0x501102u);
                float n2 = SphericalFbm6(uz[i], ux[i], uy[i], 5.5f, seed ^ 0x501103u);
                float n3 = SphericalFbm6(ux[i], uz[i], uy[i], 4.2f, seed ^ 0x501104u);

                var s = GlebaEcologySupplement.InitializeTileSoil(
                    rp.Sand, rp.Clay, rp.Silt, rp.WeatheringRate, rp.MineralRichness,
                    coastalBeach, f.AnnualRain[i], n0, n1, n2, n3, n0 * 0.7f, n1 * 0.7f);
                GlebaEcologySupplement.ApplyGlacierLoess(ref s, true, lgmIce[i], w.Elevation[i]);
                GlebaEcologySupplement.ApplyAeolianDeflation(ref s, true, w.Elevation[i], f.AnnualRain[i]);
                soils[i] = s;
            }

            // Pass 4 (`soil::blur_soils`, 0x1808ed640): Depth-weighted Jacobi diffusion across land neighbors
            GlebaEcologySupplement.BlurLandSoils(w, soils, iterations: 4, strength: 0.65f);

            // Pass 5 (`soil::thin_mountain_soils`): Strip soil on steep alpine gradients
            for (int i = 0; i < n; i++)
            {
                if (FlatTerrainAnalysis.AreaWater(w.Water[i])) continue;
                var s = soils[i];
                GlebaEcologySupplement.ThinMountainSoil(ref s, w.Elevation[i], f.Gradient[i]);
                soils[i] = s;
            }

            // Pass 6 (`soil::transport_sediments`): Multi-pass fluvial sediment & nutrient routing along DrainOrder
            var inflow = new float[n];
            var sedSand = new float[n];
            var sedSilt = new float[n];
            var sedClay = new float[n];
            var sedMinerals = new float[n];

            for (int iter = 0; iter < 3; iter++)
            {
                Array.Clear(inflow, 0, n);
                Array.Clear(sedSand, 0, n);
                Array.Clear(sedSilt, 0, n);
                Array.Clear(sedClay, 0, n);
                Array.Clear(sedMinerals, 0, n);

                for (int k = 0; k < n; k++)
                {
                    if ((k & 8191) == 0) cancel.ThrowIfCancellationRequested();
                    int u = w.DrainOrder[k];
                    if (FlatTerrainAnalysis.AreaWater(w.Water[u])) continue;
                    int v = w.Downstream[u];
                    int nextLand = (v >= 0 && !FlatTerrainAnalysis.AreaWater(w.Water[v])) ? v : -1;
                    bool floodplainOrDelta = nextLand < 0 || f.Forms[u] == Landform.Floodplain || (w.Water[u] == WaterKind.River && f.Gradient[u] <= 80f);
                    GlebaEcologySupplement.StepFluvialSediment(
                        u, nextLand, f.AnnualRain[u] / 12f, humidity[u],
                        inflow, geo.ErosionWaterflow, soils,
                        sedSand, sedSilt, sedClay, sedMinerals, floodplainOrDelta);
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (FlatTerrainAnalysis.AreaWater(w.Water[i])) continue;
                var s = soils[i];
                float sed = f.Sediment[i];
                if (sed > 0.02f)
                {
                    float dep = 1.65f * sed;
                    float oldD = Math.Max(s.Depth, 1e-4f), newD = oldD + dep;
                    s.Sand = (oldD * s.Sand + dep * 0.10f) / newD;
                    s.Silt = (oldD * s.Silt + dep * 0.62f) / newD;
                    s.Clay = (oldD * s.Clay + dep * 0.28f) / newD;
                    s.MineralRichness = FMath.Clamp((oldD * s.MineralRichness + dep * 0.85f) / newD, 0f, 1f);
                    s.Depth = newD;
                }
                float plants = geo.FloraTotalByte[i] / 255f;
                s.Organics = FMath.Clamp(0.10f + 0.75f * plants + 0.15f * FMath.Clamp(sed, 0f, 1f), 0f, 1f);
                s.NormalizeTexture();
                geo.SoilSandByte[i] = (byte)FMath.Clamp((int)Math.Round(s.Sand * 255f), 0, 255);
                geo.SoilSiltByte[i] = (byte)FMath.Clamp((int)Math.Round(s.Silt * 255f), 0, 255);
                geo.SoilClayByte[i] = (byte)FMath.Clamp((int)Math.Round(s.Clay * 255f), 0, 255);
                geo.SoilDepthCentimeters[i] = (ushort)FMath.Clamp((int)Math.Round(s.Depth * 100f), 1, 65000);
                geo.SoilRichnessByte[i] = (byte)FMath.Clamp((int)Math.Round(s.MineralRichness * 255f), 0, 255);
                geo.SoilPermeabilityByte[i] = (byte)FMath.Clamp((int)Math.Round(FMath.Clamp(s.GetPermeability(), 0f, 1f) * 255f), 0, 255);
                geo.SoilOrganicsByte[i] = (byte)FMath.Clamp((int)Math.Round(s.Organics * 255f), 0, 255);
            }
        }

        static GlebaRockKind ClassifyTileBedrock(
            WorldData w, FlatTerrainFacts f, GlebaGeographyFacts geo, int i,
            float vx, float vy, float vz, float fbmMain, uint seed)
        {
            float elev = w.Elevation[i];
            float lithNoise = SphericalFbm6(vx, vy, vz, 3.85f, seed ^ 0x2468ace0u);
            float chemNoise = SphericalFbm6(vx, vy, vz, 6.10f, seed ^ 0x97531fdbu);
            bool ocean = FlatTerrainAnalysis.Ocean(w.Water[i]);

            GetMicroplateGeochemistry(
                geo.MicroplateId[i], seed,
                out float baseFels, out float quartz, out float calcite, out float dolom, out float chalkif);
            float felsicity = FMath.Clamp(baseFels + 0.12f * lithNoise, 0.35f, 0.85f);

            // Stage 12 Large Igneous Provinces (flood-basalt traps: Basalt & rare ultramafic Picrobasalt)
            if (geo.HasFlag(i, GlebaGeoFlags.LargeIgneousProvince))
                return felsicity < 0.45f && lithNoise < -0.10f ? GlebaRockKind.Picrobasalt : GlebaRockKind.Basalt;

            if (ocean)
            {
                if (geo.BoundarySetting[i] == TectonicSetting.Ridge)
                    return lithNoise > 0.44f ? GlebaRockKind.Peridotite : (lithNoise > 0.16f ? GlebaRockKind.Gabbro : GlebaRockKind.Basalt);
                if (geo.BoundarySetting[i] == TectonicSetting.Subduction && elev < -3600f)
                    return lithNoise > 0.22f ? GlebaRockKind.Eclogite : (lithNoise > -0.15f ? GlebaRockKind.Blueschist : GlebaRockKind.Basalt);
                if (elev < -2200f)
                    return (quartz >= 0.54f && chemNoise > 0.18f) ? GlebaRockKind.Chert : (lithNoise > 0.22f ? GlebaRockKind.Gabbro : GlebaRockKind.Basalt);
                if (elev < -350f)
                    return lithNoise > 0.18f ? GlebaRockKind.Shale : (lithNoise > -0.22f ? GlebaRockKind.Mudstone : GlebaRockKind.Claystone);
                if (f.MeanTemperature[i] > 15f && calcite >= 0.50f && chemNoise > -0.20f)
                {
                    if (calcite >= 0.60f && chalkif >= 0.50f) return GlebaRockKind.Chalk;
                    return dolom >= 0.80f ? GlebaRockKind.Dolostone : GlebaRockKind.Limestone;
                }
                return lithNoise > 0f ? GlebaRockKind.Sandstone : GlebaRockKind.Shale;
            }

            var setting = geo.BoundarySetting[i];
            if (setting == TectonicSetting.Subduction || setting == TectonicSetting.IslandArc)
            {
                // High-P/Low-T subduction metamorphic belt (blueschist -> eclogite)
                if (f.Relief[i] > 920f && chemNoise > 0.30f)
                    return elev > 1850f ? GlebaRockKind.Eclogite : GlebaRockKind.Blueschist;
                // Contact metamorphic aureoles around volcanic centers (skarn / hornfels)
                if (geo.VolcanoFlags[i] != 0 && chemNoise > 0.10f)
                    return calcite >= 0.50f ? GlebaRockKind.Skarn : GlebaRockKind.Hornfel;
                // Deep eroded batholith vs extrusive volcanic arc by felsicity series
                if (f.Relief[i] > 880f && lithNoise < -0.12f)
                {
                    if (felsicity >= 0.69f) return GlebaRockKind.Granite;
                    if (felsicity >= 0.63f) return GlebaRockKind.Granodiorite;
                    if (felsicity >= 0.52f) return GlebaRockKind.Diorite;
                    return felsicity >= 0.45f ? GlebaRockKind.Gabbro : GlebaRockKind.Peridotite;
                }
                if (felsicity >= 0.69f) return GlebaRockKind.Rhyolite;
                if (felsicity >= 0.63f) return GlebaRockKind.Dacite;
                if (felsicity >= 0.52f) return GlebaRockKind.Andesite;
                return felsicity >= 0.45f ? GlebaRockKind.Basalt : GlebaRockKind.Picrobasalt;
            }

            if (setting == TectonicSetting.Rift || setting == TectonicSetting.Ridge)
            {
                if (chemNoise > 0.32f) return calcite >= 0.50f ? GlebaRockKind.Skarn : GlebaRockKind.Hornfel;
                if (lithNoise > 0.08f) return felsicity < 0.45f ? GlebaRockKind.Picrobasalt : GlebaRockKind.Basalt;
                return GlebaRockKind.Sandstone;
            }

            if (setting == TectonicSetting.Collision && elev > 1350f)
            {
                if (chemNoise > 0.30f)
                    return calcite >= 0.50f ? GlebaRockKind.Marble : GlebaRockKind.Quartzite;
                if (lithNoise > 0.24f)
                    return felsicity >= 0.66f ? GlebaRockKind.Granite : GlebaRockKind.Granodiorite;
                if (lithNoise < -0.26f)
                    return elev > 3400f ? GlebaRockKind.Granulite : (elev > 2200f ? GlebaRockKind.Amphibolite : GlebaRockKind.Greenschist);
                // Barrovian pelitic P-T series: slate -> phyllite -> schist -> gneiss -> migmatite
                if (elev > 4000f) return GlebaRockKind.Migmatite;
                if (elev > 2800f) return GlebaRockKind.Gneiss;
                if (elev > 2000f) return GlebaRockKind.Schist;
                return elev > 1550f ? GlebaRockKind.Phyllite : GlebaRockKind.Slate;
            }

            // Eroded intraplate paleo-orogens (Urals, Appalachians, Caledonides)
            if (geo.HasFlag(i, GlebaGeoFlags.PaleoOrogen))
            {
                if (chemNoise > 0.24f) return calcite >= 0.50f ? GlebaRockKind.Marble : GlebaRockKind.Quartzite;
                if (lithNoise > 0.22f) return GlebaRockKind.Granodiorite;
                if (lithNoise > -0.05f) return GlebaRockKind.Phyllite;
                return lithNoise > -0.28f ? GlebaRockKind.Slate : GlebaRockKind.Greenschist;
            }

            // Arid endorheic evaporite basins
            if ((w.Downstream[i] < 0 || f.Forms[i] == Landform.Depression) && f.AnnualRain[i] < 260f)
            {
                if (chemNoise > 0.28f) return GlebaRockKind.Halite;
                if (chemNoise > -0.05f) return GlebaRockKind.Dolostone;
                return lithNoise < -0.20f ? GlebaRockKind.Claystone : GlebaRockKind.Sandstone;
            }

            // High relief mountain belts & fanglomerate / talus breccias (minimum_elevation = 1000m in rock_types.lua)
            if (elev > 2200f || f.Relief[i] > 1100f)
            {
                if (lithNoise > 0.25f) return felsicity >= 0.66f ? GlebaRockKind.Granite : GlebaRockKind.Granodiorite;
                if (lithNoise > -0.12f) return elev > 3600f ? GlebaRockKind.Migmatite : GlebaRockKind.Gneiss;
                return chemNoise > 0.15f ? GlebaRockKind.Marble : GlebaRockKind.Schist;
            }

            if (f.Relief[i] > 650f && elev >= 800f)
            {
                if (lithNoise > 0.30f) return elev >= 1000f && chemNoise > 0.05f ? GlebaRockKind.Breccia : GlebaRockKind.Conglomerate;
                return lithNoise > -0.10f ? GlebaRockKind.Phyllite : GlebaRockKind.Slate;
            }

            // Archean cratonic shields & basement domes (aquitard porosity < 0.16)
            if (fbmMain > 0.42f)
                return chemNoise > 0.22f ? GlebaRockKind.Greenstone : (felsicity >= 0.66f ? GlebaRockKind.Granite : GlebaRockKind.Granodiorite);
            if (fbmMain > 0.30f)
                return chemNoise > 0.26f ? GlebaRockKind.Amphibolite : (chemNoise < -0.26f ? GlebaRockKind.Granulite : GlebaRockKind.Gneiss);

            // Continental sedimentary platforms, karst tables & river basins (porous >= 0.16 for regional aquifers)
            if (calcite >= 0.50f && chemNoise > 0.14f)
            {
                if (calcite >= 0.60f && chalkif >= 0.50f && elev < 550f) return GlebaRockKind.Chalk;
                return (dolom >= 0.80f || f.MeanTemperature[i] <= 7f) ? GlebaRockKind.Dolostone : GlebaRockKind.Limestone;
            }
            if (quartz >= 0.58f && chemNoise > 0.28f) return GlebaRockKind.Chert;
            if (lithNoise > 0.16f) return GlebaRockKind.Sandstone;
            if (lithNoise > -0.12f) return GlebaRockKind.Siltstone;
            if (lithNoise > -0.34f) return GlebaRockKind.Shale;
            return chemNoise > -0.18f ? GlebaRockKind.Mudstone : GlebaRockKind.Claystone;
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
                int seedPlate = geo.PlateId[seedTile];
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
                        float weight = (geo.PlateId[nb] == seedPlate ? 3000f : 0f)
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

                // Accumulate recharge from surface runoff + 6-pass routed soil texture permeability
                float acc = rng.NextFloat() * (250f * seedProps.Porosity) * rng.NextFloat() + filled.Count;
                for (int k = 0; k < filled.Count; k++)
                {
                    int t = filled[k];
                    float sf = Math.Max(0.2f, geo.SoilDepthCentimeters[t] > 0 ? geo.SoilPermeabilityByte[t] / 255f * 1.4f : 0.7f);
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
                    float p;
                    if (gleba != null)
                    {
                        p = gleba.SampleMonthly(gleba.PressureHpa, u, v, month);
                    }
                    else
                    {
                        float season = (float)Math.Cos((month - 6) * Math.PI / 6.0);
                        float shiftLat = latDeg - 8f * season;
                        p = 1000f - 45f * (float)Math.Cos(shiftLat * (Math.PI / 30.0));
                    }
                    float t = 1f - FMath.Clamp((p - 900f) / 200f, 0f, 1f);
                    SampleCividis(t, out byte cr, out byte cg, out byte cb);
                    r = (byte)FMath.Clamp(baseVal + 0.955f * (cr - baseVal), 0f, 255f);
                    g = (byte)FMath.Clamp(baseVal + 0.955f * (cg - baseVal), 0f, 255f);
                    b = (byte)FMath.Clamp(baseVal + 0.955f * (cb - baseVal), 0f, 255f);
                    if (gleba != null && world.CoastDistance[tile] <= 2)
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
                    if (!isWater)
                    {
                        float lift = FMath.Clamp(world.Elevation[tile] / 4000f, 0f, 1f) * 18f;
                        r = (byte)(68f + lift);
                        g = (byte)(72f + lift);
                        b = (byte)(78f + lift * 0.9f);
                        return;
                    }
                    if (world.Water[tile] == WaterKind.Lake)
                    {
                        bool salt = geo != null && geo.WaterbodyTypes[tile] == GlebaWaterbodyType.SaltwaterLake;
                        r = salt ? (byte)66 : (byte)56;
                        g = salt ? (byte)86 : (byte)106;
                        b = salt ? (byte)114 : (byte)124;
                        return;
                    }
                    SampleSeasonalOceanCurrent(world, u, v, month, out _, out _, out float dynH, out float sstAnom);
                    SampleCividis(FMath.Clamp((dynH - 900f) / 200f, 0f, 1f), out byte cr, out byte cg, out byte cb);
                    float rr = 24f + 0.36f * (cr - 24f);
                    float gg = 34f + 0.36f * (cg - 34f);
                    float bb = 52f + 0.36f * (cb - 52f);
                    // Subtle OISST warm western-boundary vs cold upwelling thermal tint
                    float warm = FMath.Clamp(sstAnom / 3.5f, 0f, 1f);
                    float cool = FMath.Clamp(-sstAnom / 2.8f, 0f, 1f);
                    rr = FMath.Lerp(rr, 212f, warm * 0.42f);
                    gg = FMath.Lerp(gg, 118f, warm * 0.32f);
                    bb = FMath.Lerp(bb, 68f, warm * 0.28f);
                    rr = FMath.Lerp(rr, 28f, cool * 0.32f);
                    gg = FMath.Lerp(gg, 146f, cool * 0.38f);
                    bb = FMath.Lerp(bb, 198f, cool * 0.42f);
                    r = (byte)FMath.Clamp(rr, 0f, 255f);
                    g = (byte)FMath.Clamp(gg, 0f, 255f);
                    b = (byte)FMath.Clamp(bb, 0f, 255f);
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
                    if (isWater)
                    {
                        r = 26; g = 42; b = 62;
                        return;
                    }
                    float sh = gleba != null
                        ? FMath.Clamp(gleba.SampleMonthly(gleba.RainShadowiness, u, v, month), 0f, 1f)
                        : FMath.Clamp((world.Facts != null ? world.Facts.Relief[tile] / 2400f : 0f) * (1f - world.Precipitation[tile * 12 + month] / 120f), 0f, 1f);
                    float cont = gleba != null
                        ? FMath.Clamp(gleba.SampleMonthly(gleba.MappedContinentality, u, v, month), 0f, 1f)
                        : FMath.Clamp(world.CoastDistance[tile] / 24f, 0f, 1f);
                    float combined = FMath.Clamp(0.72f * sh + 0.28f * cont, 0f, 1f);
                    SampleCividis(combined, out r, out g, out b);
                    return;
                }
                case GlebaMapmodeKind.Continentality:
                {
                    float c = gleba != null ? FMath.Clamp(gleba.SampleMonthly(gleba.WindContinentality, u, v, month) / 2.5f, 0f, 1f) : 0f;
                    SampleCividis(c, out r, out g, out b);
                    return;
                }
                case GlebaMapmodeKind.RossbyJetStream:
                {
                    double latRad = (v - 0.5) * Math.PI;
                    double lonRad = (u - 0.5) * 2.0 * Math.PI;
                    var rossby = new GlebaRossbyWaveField(FMath.Seed(world.Seed))
                    {
                        RossbyTimeRadians = (month / 12.0) * 2.0 * Math.PI
                    };
                    rossby.GetJetStreamMovementAtLatLon(latRad, lonRad, out float jx, out float jy);
                    float jetSpd = FMath.Clamp((float)Math.Sqrt(jx * jx + jy * jy) / 0.022f, 0f, 1f);
                    SampleCividis(jetSpd, out byte cr, out byte cg, out byte cb);
                    float bg = isWater ? 24f : 56f + FMath.Clamp(world.Elevation[tile] / 4500f, 0f, 1f) * 28f;
                    // Blend upper-tropospheric violet-indigo trough shading with golden-cyan jet isotachs
                    r = (byte)FMath.Clamp(bg + 0.86f * (cr - bg) + 34f * jetSpd * (1f - jetSpd), 0f, 255f);
                    g = (byte)FMath.Clamp(bg + 0.82f * (cg - bg), 0f, 255f);
                    b = (byte)FMath.Clamp(bg + 0.88f * (cb - bg) + 48f * (1f - jetSpd) * (isWater ? 0.5f : 0.35f), 0f, 255f);
                    return;
                }
                case GlebaMapmodeKind.RelativeHumidity:
                {
                    float rh = world.Moisture != null
                        ? FMath.Clamp(world.Moisture.Sample(world.Moisture.Humidity, u, v, month), 0f, 1f)
                        : FMath.Clamp((isWater ? 0.82f : 0.18f) + world.Precipitation[tile * 12 + month] / 180f, 0f, 1f);
                    // Gleba Humidity palette: arid sand-ochre (218,176,108) -> temperate emerald (72,168,142) -> saturated monsoon cobalt (26,88,168)
                    if (rh < 0.5f)
                    {
                        float t = rh * 2f;
                        r = (byte)FMath.Lerp(218f, 72f, t);
                        g = (byte)FMath.Lerp(176f, 168f, t);
                        b = (byte)FMath.Lerp(108f, 142f, t);
                    }
                    else
                    {
                        float t = (rh - 0.5f) * 2f;
                        r = (byte)FMath.Lerp(72f, 26f, t);
                        g = (byte)FMath.Lerp(168f, 88f, t);
                        b = (byte)FMath.Lerp(142f, 168f, t);
                    }
                    if (isWater)
                    {
                        r = (byte)(r * 0.72f + 10f);
                        g = (byte)(g * 0.76f + 16f);
                        b = (byte)(b * 0.84f + 24f);
                    }
                    return;
                }
                case GlebaMapmodeKind.GlebaBiomes:
                {
                    var bk = world.Facts?.GlebaBiomes != null ? world.Facts.GlebaBiomes[tile]
                        : (isWater ? GlebaBiomeKind.Sea : GlebaBiomeKind.TemperateGrasslandsSavannasAndShrublands);
                    GlebaEcologySupplement.GetGlebaBiomeMapColor(bk, out r, out g, out b);
                    return;
                }
                case GlebaMapmodeKind.GlebaFlora:
                {
                    if (isWater) { r = 20; g = 36; b = 54; return; }
                    if (world.Facts != null && world.Facts.GroundedIceMeters[tile] > 0.01f) { r = 228; g = 236; b = 244; return; }
                    float trees, shrubs, grass, total;
                    if (geo != null)
                    {
                        trees = geo.FloraTreesByte[tile] / 255f;
                        shrubs = geo.FloraShrubsByte[tile] / 255f;
                        grass = geo.FloraGrassByte[tile] / 255f;
                        total = geo.FloraTotalByte[tile] / 255f;
                    }
                    else
                    {
                        total = world.Facts != null ? FMath.Clamp(world.Facts.Wetness[tile] * 1.15f, 0f, 1f) : 0.5f;
                        trees = world.Facts != null ? world.Facts.Canopy[tile] : 0.3f;
                        grass = Math.Max(0f, 1f - trees) * 0.7f;
                        shrubs = Math.Max(0f, 1f - trees) * 0.3f;
                    }
                    // Gleba Vegetation mapmode: Red = shrubs, Green = grasses, Blue = trees, Dark = bare/empty land
                    float bareFloor = 24f * (1f - total);
                    r = (byte)FMath.Clamp(bareFloor + 235f * shrubs * total, 0f, 255f);
                    g = (byte)FMath.Clamp(bareFloor + 235f * grass * total, 0f, 255f);
                    b = (byte)FMath.Clamp(bareFloor + 245f * trees * total, 0f, 255f);
                    return;
                }
                case GlebaMapmodeKind.SoilTexture:
                {
                    if (isWater) { r = 24; g = 42; b = 62; return; }
                    if (world.Facts != null && world.Facts.GroundedIceMeters[tile] > 0.01f) { r = 222; g = 230; b = 238; return; }
                    if (geo != null && geo.SoilDepthCentimeters[tile] > 0)
                    {
                        float depthGain = 0.42f + 0.58f * FMath.Clamp(geo.SoilDepthCentimeters[tile] / 350f, 0f, 1f);
                        // Gleba SoilTexture: Red = sand, Green = silt, Blue = clay
                        r = (byte)FMath.Clamp(22f + geo.SoilSandByte[tile] * depthGain, 0f, 255f);
                        g = (byte)FMath.Clamp(22f + geo.SoilSiltByte[tile] * depthGain, 0f, 255f);
                        b = (byte)FMath.Clamp(22f + geo.SoilClayByte[tile] * depthGain, 0f, 255f);
                        return;
                    }
                    r = 154; g = 128; b = 92;
                    return;
                }
                case GlebaMapmodeKind.SoilFertility:
                {
                    if (isWater) { r = 24; g = 40; b = 58; return; }
                    if (world.Facts != null && world.Facts.GroundedIceMeters[tile] > 0.01f) { r = 216; g = 226; b = 235; return; }
                    float score;
                    if (geo != null && geo.SoilDepthCentimeters[tile] > 0)
                    {
                        float dNorm = FMath.Clamp(geo.SoilDepthCentimeters[tile] / 450f, 0f, 1f);
                        float rich = geo.SoilRichnessByte[tile] / 255f;
                        float org = geo.SoilOrganicsByte[tile] / 255f;
                        score = FMath.Clamp(0.42f * rich + 0.33f * dNorm + 0.25f * org, 0f, 1f);
                    }
                    else
                    {
                        score = FMath.Clamp(world.Fertility[tile], 0f, 1f);
                    }
                    // Rich chernozem/alluvial loam gradient: rocky ochre (142,108,76) -> golden loam (204,186,96) -> lush fertile green (46,158,78)
                    if (score < 0.5f)
                    {
                        float t = score * 2f;
                        r = (byte)FMath.Lerp(142f, 204f, t);
                        g = (byte)FMath.Lerp(108f, 186f, t);
                        b = (byte)FMath.Lerp(76f, 96f, t);
                    }
                    else
                    {
                        float t = (score - 0.5f) * 2f;
                        r = (byte)FMath.Lerp(204f, 46f, t);
                        g = (byte)FMath.Lerp(186f, 158f, t);
                        b = (byte)FMath.Lerp(96f, 78f, t);
                    }
                    return;
                }
                case GlebaMapmodeKind.Bedrock:
                {
                    if (geo == null) { r = g = b = (byte)baseVal; return; }
                    var rp = RockTable[(int)geo.Bedrock[tile]];
                    float shade = isWater ? 0.58f : (0.86f + 0.18f * FMath.Clamp(world.Elevation[tile] / 3500f, 0f, 1f));
                    r = (byte)FMath.Clamp(rp.MapR * shade, 0f, 255f);
                    g = (byte)FMath.Clamp(rp.MapG * shade, 0f, 255f);
                    b = (byte)FMath.Clamp(rp.MapB * shade, 0f, 255f);
                    return;
                }
                case GlebaMapmodeKind.TectonicsAndVolcanism:
                {
                    if (geo == null) { r = g = b = (byte)baseVal; return; }
                    if (geo.TryGetVolcano(tile, out var vKind, out bool dormant, out _))
                    {
                        if (!dormant)
                        {
                            r = 255;
                            g = vKind == VolcanoKind.Hotspot ? (byte)218 : (byte)86;
                            b = vKind == VolcanoKind.Rift ? (byte)180 : (byte)28;
                            return;
                        }
                        r = 196; g = 112; b = 88;
                        return;
                    }
                    uint ph = FMath.Hash((uint)geo.PlateId[tile] * 0x9e3779b9u + 17u);
                    uint mh = FMath.Hash((uint)geo.MicroplateId[tile] * 0x85ebca6bu);
                    float pr = 72f + (ph & 0x7Fu) * 0.85f + (mh & 0x1Fu) * 0.45f;
                    float pg = 76f + ((ph >> 8) & 0x7Fu) * 0.85f + ((mh >> 8) & 0x1Fu) * 0.45f;
                    float pb = 84f + ((ph >> 16) & 0x7Fu) * 0.85f + ((mh >> 16) & 0x1Fu) * 0.45f;
                    if (isWater) { pr *= 0.64f; pg *= 0.68f; pb *= 0.78f; }

                    // Highlight plate boundaries & active orogenic / subduction / spreading belts
                    bool plateEdge = false;
                    byte ownPlate = geo.PlateId[tile];
                    for (int d = 0; d < 4; d++)
                        if (geo.PlateId[world.SphereNeighbor(tile, d)] != ownPlate) { plateEdge = true; break; }

                    var bs = geo.BoundarySetting[tile];
                    float orog = geo.OrogenyStrengthByte[tile] / 255f;
                    if (bs == TectonicSetting.Subduction || bs == TectonicSetting.IslandArc)
                    {
                        pr = FMath.Lerp(pr, 232f, 0.55f * orog + 0.20f);
                        pg = FMath.Lerp(pg, 74f, 0.55f * orog + 0.20f);
                        pb = FMath.Lerp(pb, 96f, 0.55f * orog + 0.20f);
                    }
                    else if (bs == TectonicSetting.Collision)
                    {
                        pr = FMath.Lerp(pr, 238f, 0.62f * orog);
                        pg = FMath.Lerp(pg, 148f, 0.62f * orog);
                        pb = FMath.Lerp(pb, 58f, 0.62f * orog);
                    }
                    else if (bs == TectonicSetting.Ridge)
                    {
                        pr = FMath.Lerp(pr, 56f, 0.58f);
                        pg = FMath.Lerp(pg, 206f, 0.58f);
                        pb = FMath.Lerp(pb, 238f, 0.58f);
                    }
                    else if (bs == TectonicSetting.Rift)
                    {
                        pr = FMath.Lerp(pr, 198f, 0.58f);
                        pg = FMath.Lerp(pg, 88f, 0.58f);
                        pb = FMath.Lerp(pb, 236f, 0.58f);
                    }
                    if (geo.HasFlag(tile, GlebaGeoFlags.LargeIgneousProvince))
                    {
                        pr = FMath.Lerp(pr, 88f, 0.50f);
                        pg = FMath.Lerp(pg, 54f, 0.50f);
                        pb = FMath.Lerp(pb, 218f, 0.56f);
                    }
                    else if (geo.HasFlag(tile, GlebaGeoFlags.PaleoOrogen))
                    {
                        pr = FMath.Lerp(pr, 186f, 0.44f);
                        pg = FMath.Lerp(pg, 146f, 0.44f);
                        pb = FMath.Lerp(pb, 88f, 0.44f);
                    }
                    if (plateEdge)
                    {
                        pr = FMath.Lerp(pr, 250f, 0.42f);
                        pg = FMath.Lerp(pg, 236f, 0.42f);
                        pb = FMath.Lerp(pb, 196f, 0.42f);
                    }
                    r = (byte)FMath.Clamp(pr, 0f, 255f);
                    g = (byte)FMath.Clamp(pg, 0f, 255f);
                    b = (byte)FMath.Clamp(pb, 0f, 255f);
                    return;
                }
                case GlebaMapmodeKind.CropsAndCarryingCapacity:
                {
                    if (isWater) { r = 22; g = 38; b = 58; return; }
                    if (world.Facts != null && world.Facts.GroundedIceMeters[tile] > 0.01f) { r = 224; g = 232; b = 240; return; }
                    if (geo == null)
                    {
                        SampleCividis(FMath.Clamp(world.Fertility[tile], 0f, 1f), out r, out g, out b);
                        return;
                    }
                    float cap = geo.CarryingCapacityByte[tile] / 255f;
                    float suit = geo.CropSuitabilityByte[tile] / 255f;
                    var crop = geo.DominantCrop[tile];

                    // Base carrying-capacity tone: cold/arid slate (64,62,58) -> pastoral ochre (156,136,86) -> fertile cradle gold-green
                    float cr = FMath.Lerp(64f, 168f, FMath.Clamp(cap * 1.6f, 0f, 1f));
                    float cg = FMath.Lerp(62f, 162f, FMath.Clamp(cap * 1.6f, 0f, 1f));
                    float cb = FMath.Lerp(58f, 92f, FMath.Clamp(cap * 1.6f, 0f, 1f));

                    if (crop != GlebaFounderCrop.None && suit > 0.02f)
                    {
                        float wCrop = FMath.Clamp(0.28f + 0.65f * suit, 0f, 0.88f);
                        float tr = 180f, tg = 210f, tb = 110f;
                        switch (crop)
                        {
                            case GlebaFounderCrop.WildPea:         tr = 238f; tg = 196f; tb =  68f; break; // Warm pulse amber
                            case GlebaFounderCrop.WildBitterVetch: tr = 242f; tg = 128f; tb =  64f; break; // Fertile Crescent terracotta-amber
                            case GlebaFounderCrop.WildRye:         tr = 152f; tg = 224f; tb =  96f; break; // Temperate cereal lime-green
                            case GlebaFounderCrop.WildRice:        tr =  64f; tg = 208f; tb = 172f; break; // Monsoon paddy jade
                            case GlebaFounderCrop.AzollaCompanion: tr =  42f; tg = 236f; tb = 212f; break; // Azolla-rice nitrogen-fixing bright aqua
                        }
                        cr = FMath.Lerp(cr, tr, wCrop);
                        cg = FMath.Lerp(cg, tg, wCrop);
                        cb = FMath.Lerp(cb, tb, wCrop);
                    }
                    if (cap >= 0.58f)
                    {
                        // Highlight top alluvial/coastal civilization cradles
                        float glow = FMath.Clamp((cap - 0.58f) / 0.32f, 0f, 1f) * 0.35f;
                        cr = FMath.Lerp(cr, 255f, glow);
                        cg = FMath.Lerp(cg, 244f, glow);
                        cb = FMath.Lerp(cb, 156f, glow);
                    }
                    r = (byte)FMath.Clamp(cr, 0f, 255f);
                    g = (byte)FMath.Clamp(cg, 0f, 255f);
                    b = (byte)FMath.Clamp(cb, 0f, 255f);
                    return;
                }
                case GlebaMapmodeKind.HydroGeoComposite:
                {
                    if (geo != null)
                    {
                        if (geo.HasFlag(tile, GlebaGeoFlags.CoralCay)) { r = 255; g = 234; b = 112; return; }
                        if (geo.HasFlag(tile, GlebaGeoFlags.CoralReef)) { r = 255; g = 106; b = 162; return; }
                        if (geo.HasFlag(tile, GlebaGeoFlags.FjordCoast)) { r = 64; g = 216; b = 255; return; }
                        if (world.Water[tile] == WaterKind.Lake)
                        {
                            if (geo.WaterbodyTypes[tile] == GlebaWaterbodyType.SaltwaterLake) { r = 224; g = 122; b = 78; return; }
                            if (geo.LakeOrigins[tile] == GlebaLakeOrigin.Rifting) { r = 68; g = 128; b = 248; return; }
                            if (geo.LakeOrigins[tile] == GlebaLakeOrigin.Glacial) { r = 98; g = 204; b = 242; return; }
                            r = 78; g = 172; b = 214; return;
                        }
                        if (!isWater && geo.LgmIceMeters[tile] > 25)
                        {
                            float iceT = FMath.Clamp(geo.LgmIceMeters[tile] / 1800f, 0f, 1f);
                            r = (byte)FMath.Lerp(162f, 232f, iceT);
                            g = (byte)FMath.Lerp(198f, 242f, iceT);
                            b = (byte)FMath.Lerp(232f, 255f, iceT);
                            return;
                        }
                        if (!isWater && geo.AquiferId[tile] >= 0)
                        {
                            bool artesian = geo.HasFlag(tile, GlebaGeoFlags.ArtesianAquifer);
                            uint ah = FMath.Hash((uint)geo.AquiferId[tile] * 0x9e3779b9u);
                            r = (byte)(artesian ? 52 : 76 + (ah & 0x3Fu));
                            g = (byte)(artesian ? 196 : 138 + ((ah >> 8) & 0x4Fu));
                            b = (byte)(artesian ? 188 : 176 + ((ah >> 16) & 0x4Fu));
                            return;
                        }
                    }
                    r = (byte)(isWater ? 24 : 74);
                    g = (byte)(isWater ? 40 : 78);
                    b = (byte)(isWater ? 60 : 76);
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

        /// <summary>
        /// Converts a 32-bit IEEE-754 float to 16-bit binary16 (RGBAHalf channel) without .NET 5 System.Half dependency.
        /// </summary>
        public static ushort FloatToHalfBits(float value)
        {
            uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            uint sign = (bits >> 16) & 0x8000u;
            int exp = (int)((bits >> 23) & 0xFFu) - 127 + 15;
            uint mant = bits & 0x007FFFFFu;
            if (exp <= 0)
            {
                if (exp < -10) return (ushort)sign;
                mant = (mant | 0x00800000u) >> (1 - exp);
                return (ushort)(sign | ((mant + 0x00001000u) >> 13));
            }
            if (exp >= 31) return (ushort)(sign | 0x7C00u);
            return (ushort)(sign | ((uint)exp << 10) | ((mant + 0x00001000u) >> 13));
        }

        /// <summary>
        /// Packs two consecutive calendar months (m0 in RG, m1 in BA) of surface wind vectors (+East, +North)
        /// into an RGBAHalf buffer of size width * height * 4.
        /// </summary>
        public static void PackTwoMonthWindFlowHalf(WorldData world, int month0, int width, int height, ushort[] dstRgbaHalf)
        {
            if (world == null || dstRgbaHalf == null || dstRgbaHalf.Length < width * height * 4) return;
            int m0 = FMath.Wrap(month0, 12), m1 = (m0 + 1) % 12;
            var moisture = world.Moisture;
            float s0 = (float)Math.Cos((m0 - 6) * Math.PI / 6.0);
            float s1 = (float)Math.Cos((m1 - 6) * Math.PI / 6.0);

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float lat = (v - 0.5f) * 180f;
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float e0, n0, e1, n1;
                    if (moisture != null)
                    {
                        e0 = moisture.Sample(moisture.East, u, v, m0);
                        n0 = moisture.Sample(moisture.North, u, v, m0);
                        e1 = moisture.Sample(moisture.East, u, v, m1);
                        n1 = moisture.Sample(moisture.North, u, v, m1);
                    }
                    else
                    {
                        e0 = FlatWindBelts.Zonal(lat, s0);
                        n0 = -0.32f * (float)Math.Sin((lat - 8f * s0) * (Math.PI / 30.0));
                        e1 = FlatWindBelts.Zonal(lat, s1);
                        n1 = -0.32f * (float)Math.Sin((lat - 8f * s1) * (Math.PI / 30.0));
                    }
                    int p = (row + x) * 4;
                    dstRgbaHalf[p] = FloatToHalfBits(e0);
                    dstRgbaHalf[p + 1] = FloatToHalfBits(n0);
                    dstRgbaHalf[p + 2] = FloatToHalfBits(e1);
                    dstRgbaHalf[p + 3] = FloatToHalfBits(n1);
                }
            }
        }

        /// <summary>
        /// Packs two consecutive calendar months (m0 in RG, m1 in BA) of seasonal ocean surface current velocities (m/s)
        /// into an RGBAHalf buffer of size width * height * 4.
        /// </summary>
        public static void PackTwoMonthCurrentFlowHalf(WorldData world, int month0, int width, int height, ushort[] dstRgbaHalf)
        {
            if (world == null || dstRgbaHalf == null || dstRgbaHalf.Length < width * height * 4) return;
            int m0 = FMath.Wrap(month0, 12), m1 = (m0 + 1) % 12;

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    SampleSeasonalOceanCurrent(world, u, v, m0, out float e0, out float n0, out _, out _);
                    SampleSeasonalOceanCurrent(world, u, v, m1, out float e1, out float n1, out _, out _);
                    int p = (row + x) * 4;
                    dstRgbaHalf[p] = FloatToHalfBits(e0);
                    dstRgbaHalf[p + 1] = FloatToHalfBits(n0);
                    dstRgbaHalf[p + 2] = FloatToHalfBits(e1);
                    dstRgbaHalf[p + 3] = FloatToHalfBits(n1);
                }
            }
        }

        /// <summary>
        /// Packs Gleba's 5 dynamic circulation band latitudes (normalized V in [0, 1]) interpolated at calendarDay
        /// into a width * 4 RGBAHalf texture buffer (rows 0..1: ItczV, SthzNV, SthzSV, PolarNV; rows 2..3: PolarSV, 0, 0, 0).
        /// </summary>
        public static void PackInterpolatedBandsHalf(WorldData world, double calendarDay, int width, ushort[] dstRgbaHalf)
        {
            if (world == null || dstRgbaHalf == null || dstRgbaHalf.Length < width * 4 * 4) return;
            double period = calendarDay / 30.0, whole = Math.Floor(period);
            int m0 = FMath.Wrap((int)whole, 12), m1 = (m0 + 1) % 12;
            float blend = (float)(period - whole);
            var gleba = world.Moisture?.Gleba;
            float season = (float)Math.Cos(((m0 + blend) - 6.0) * Math.PI / 6.0);

            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float itczDeg, sthzNDeg, sthzSDeg, polNDeg, polSDeg;
                if (gleba != null)
                {
                    itczDeg = FMath.Lerp(gleba.SampleBandDegrees(gleba.ItczLatRad, u, m0), gleba.SampleBandDegrees(gleba.ItczLatRad, u, m1), blend);
                    sthzNDeg = FMath.Lerp(gleba.SampleBandDegrees(gleba.SthzNorthLatRad, u, m0), gleba.SampleBandDegrees(gleba.SthzNorthLatRad, u, m1), blend);
                    sthzSDeg = FMath.Lerp(gleba.SampleBandDegrees(gleba.SthzSouthLatRad, u, m0), gleba.SampleBandDegrees(gleba.SthzSouthLatRad, u, m1), blend);
                    polNDeg = FMath.Lerp(gleba.SampleBandDegrees(gleba.PolarNorthLatRad, u, m0), gleba.SampleBandDegrees(gleba.PolarNorthLatRad, u, m1), blend);
                    polSDeg = FMath.Lerp(gleba.SampleBandDegrees(gleba.PolarSouthLatRad, u, m0), gleba.SampleBandDegrees(gleba.PolarSouthLatRad, u, m1), blend);
                }
                else
                {
                    itczDeg = 6f * season;
                    sthzNDeg = 30f + 2f * season;
                    sthzSDeg = -30f + 2f * season;
                    polNDeg = 62f + 2f * season;
                    polSDeg = -62f + 2f * season;
                }

                ushort hItcz = FloatToHalfBits(FMath.Clamp((itczDeg + 90f) / 180f, 0.01f, 0.99f));
                ushort hSthzN = FloatToHalfBits(FMath.Clamp((sthzNDeg + 90f) / 180f, 0.01f, 0.99f));
                ushort hSthzS = FloatToHalfBits(FMath.Clamp((sthzSDeg + 90f) / 180f, 0.01f, 0.99f));
                ushort hPolN = FloatToHalfBits(FMath.Clamp((polNDeg + 90f) / 180f, 0.01f, 0.99f));
                ushort hPolS = FloatToHalfBits(FMath.Clamp((polSDeg + 90f) / 180f, 0.01f, 0.99f));
                ushort hZero = 0;

                for (int r = 0; r < 2; r++)
                {
                    int pA = (r * width + x) * 4;
                    dstRgbaHalf[pA] = hItcz; dstRgbaHalf[pA + 1] = hSthzN; dstRgbaHalf[pA + 2] = hSthzS; dstRgbaHalf[pA + 3] = hPolN;
                    int pB = ((r + 2) * width + x) * 4;
                    dstRgbaHalf[pB] = hPolS; dstRgbaHalf[pB + 1] = hZero; dstRgbaHalf[pB + 2] = hZero; dstRgbaHalf[pB + 3] = hZero;
                }
            }
        }

        public static GlebaMapmodeKind MapLayerToGlebaMode(MapLayer layer)
        {
            switch (layer)
            {
                case MapLayer.SurfaceWinds: return GlebaMapmodeKind.ClimateSurfaceWinds;
                case MapLayer.OceanCurrents: return GlebaMapmodeKind.ClimateOceanCurrents;
                case MapLayer.RossbyJets: return GlebaMapmodeKind.RossbyJetStream;
                case MapLayer.RainShadow: return GlebaMapmodeKind.RainShadow;
                case MapLayer.RelativeHumidity: return GlebaMapmodeKind.RelativeHumidity;
                case MapLayer.GlebaBiomes: return GlebaMapmodeKind.GlebaBiomes;
                case MapLayer.GlebaFlora: return GlebaMapmodeKind.GlebaFlora;
                case MapLayer.GlebaSoilTexture: return GlebaMapmodeKind.SoilTexture;
                case MapLayer.GlebaSoilFertility: return GlebaMapmodeKind.SoilFertility;
                case MapLayer.GlebaBedrock: return GlebaMapmodeKind.Bedrock;
                case MapLayer.GlebaTectonics: return GlebaMapmodeKind.TectonicsAndVolcanism;
                case MapLayer.GlebaHydroGeo: return GlebaMapmodeKind.HydroGeoComposite;
                case MapLayer.GlebaCrops: return GlebaMapmodeKind.CropsAndCarryingCapacity;
                default: return GlebaMapmodeKind.ClimateSurfaceWinds;
            }
        }

        /// <summary>
        /// Packs two consecutive calendar months (m0 in RG, m1 in BA) of upper-tropospheric Rossby wave jet stream
        /// velocities (+East, +North) into an RGBAHalf buffer of size width * height * 4.
        /// </summary>
        public static void PackTwoMonthRossbyFlowHalf(WorldData world, int month0, int width, int height, ushort[] dstRgbaHalf)
        {
            if (world == null || dstRgbaHalf == null || dstRgbaHalf.Length < width * height * 4) return;
            int m0 = FMath.Wrap(month0, 12), m1 = (m0 + 1) % 12;
            uint seed = FMath.Seed(world.Seed);
            var r0 = new GlebaRossbyWaveField(seed) { RossbyTimeRadians = (m0 / 12.0) * 2.0 * Math.PI };
            var r1 = new GlebaRossbyWaveField(seed) { RossbyTimeRadians = (m1 / 12.0) * 2.0 * Math.PI };

            for (int y = 0; y < height; y++)
            {
                double latRad = ((y + 0.5) / height - 0.5) * Math.PI;
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    double lonRad = ((x + 0.5) / width - 0.5) * 2.0 * Math.PI;
                    r0.GetJetStreamMovementAtLatLon(latRad, lonRad, out float jx0, out float jy0);
                    r1.GetJetStreamMovementAtLatLon(latRad, lonRad, out float jx1, out float jy1);
                    int p = (row + x) * 4;
                    dstRgbaHalf[p] = FloatToHalfBits(jx0);
                    dstRgbaHalf[p + 1] = FloatToHalfBits(jy0);
                    dstRgbaHalf[p + 2] = FloatToHalfBits(jx1);
                    dstRgbaHalf[p + 3] = FloatToHalfBits(jy1);
                }
            }
        }

        /// <summary>
        /// Returns planetary circulation parameters for cloud shader synthesis:
        /// (hadleyTerminusRad, polarFrontTerminusRad, dominantRossbyWaveNumber, circulationRegimeCode [0=ThreeCell, 1=DoubleCell, 2=SingleCell, 3=MultiBand]).
        /// </summary>
        public static void GetCloudCirculationParams(WorldData world, out float hadleyRad, out float polarRad, out float rossbyWaveK, out float regimeCode)
        {
            var cfg = world?.Moisture?.ExoPlaSim?.Config ?? world?.PlanetClimate;
            float hadleyDeg = cfg != null ? cfg.HadleyTerminusDeg : 30f;
            float polarDeg = cfg != null ? cfg.PolarFrontTerminusDeg : 62f;
            var regime = cfg != null ? cfg.CirculationRegime : ExoPlaSimCirculationRegime.ThreeCell;
            switch (regime)
            {
                case ExoPlaSimCirculationRegime.SingleCell:
                    rossbyWaveK = 1.0f;
                    regimeCode = 2f;
                    break;
                case ExoPlaSimCirculationRegime.DoubleCell:
                    rossbyWaveK = 1.6f;
                    regimeCode = 1f;
                    break;
                case ExoPlaSimCirculationRegime.MultiBandJets:
                    rossbyWaveK = 4.4f;
                    regimeCode = 3f;
                    break;
                default:
                    rossbyWaveK = 2.6f;
                    regimeCode = 0f;
                    break;
            }
            hadleyRad = hadleyDeg * ((float)Math.PI / 180f);
            polarRad = polarDeg * ((float)Math.PI / 180f);
        }

        /// <summary>
        /// Reusable single-owner scratch &amp; static-terrain cache for <see cref="BuildCloudSynopticFields"/>.
        /// Eliminates ~1.5 MiB of per-refresh heap allocations and caches terrain/ocean/fetch/SST/trig/bilinear
        /// tables across daily refreshes when the underlying world, TerrainRevision, Moisture, and SST are unchanged.
        /// Must only be used by one thread at a time (e.g., owned by a single background worker).
        /// </summary>
        public sealed class CloudSynopticWorkspace
        {
            internal int Width, Height;
            internal float[] ItczCol, SthzNCol, SthzSCol, PolNCol, PolSCol, MjoDryCol;
            internal double[] LonRadCol, SinLonCol, CosLonCol, SinLatRow, CosLatRow;
            internal float[] ZonalSlpMean, CosRow;
            internal float[] WindE, WindN, RainGrid, TempGrid, SlpTempGrid, SlpGrid, YearRainRow;
            // Static per-(world, terrainRevision, moisture) cached fields
            internal float[] ElevG, OceanRow, BreezeRow, FetchEastKm, DistWestKm, DhDxGrid, DhDyGrid, FineLandGrid, EastCoastalGrid;
            // Precomputed 1D bilinear index tables (small L1-resident arrays)
            internal int GWidth, GHeight, EWidth, EHeight, MWidth, MHeight, OWidth, OHeight;
            internal int[] GX0, GX1, GY0, GY1, EX0, EX1, EY0, EY1, MX0, MX1, MY0, MY1, OX0, OX1, OY0, OY1;
            internal float[] GTx, GTy, ETx, ETy, MTx, MTy, OTx, OTy;
            internal bool[] MExactX, MExactY;

            internal WorldData CachedWorld;
            internal int CachedRevision = -1;
            internal FlatMoistureState CachedMoisture;
            internal ulong CachedStaticFp;
            internal bool HasStaticCache;

            public void InvalidateStaticCache()
            {
                HasStaticCache = false;
                CachedWorld = null;
                CachedRevision = -1;
                CachedMoisture = null;
                CachedStaticFp = 0;
            }

            internal void EnsureSize(int width, int height)
            {
                if (Width == width && Height == height && WindE != null) return;
                Width = width;
                Height = height;
                int count = width * height;
                ItczCol = new float[width];
                SthzNCol = new float[width];
                SthzSCol = new float[width];
                PolNCol = new float[width];
                PolSCol = new float[width];
                MjoDryCol = new float[width];
                LonRadCol = new double[width];
                SinLonCol = new double[width];
                CosLonCol = new double[width];
                SinLatRow = new double[height];
                CosLatRow = new double[height];
                ZonalSlpMean = new float[height];
                CosRow = new float[height];

                WindE = new float[count];
                WindN = new float[count];
                RainGrid = new float[count];
                TempGrid = new float[count];
                SlpTempGrid = new float[count];
                SlpGrid = new float[count];
                YearRainRow = new float[count];

                ElevG = new float[count];
                OceanRow = new float[count];
                BreezeRow = new float[count];
                FetchEastKm = new float[count];
                DistWestKm = new float[count];
                DhDxGrid = new float[count];
                DhDyGrid = new float[count];
                FineLandGrid = new float[count];
                EastCoastalGrid = new float[count];

                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    double lon = ((double)u - 0.5) * Math.PI * 2.0;
                    LonRadCol[x] = lon;
                    SinLonCol[x] = Math.Sin(lon);
                    CosLonCol[x] = Math.Cos(lon);
                }
                for (int y = 0; y < height; y++)
                {
                    float v = (y + 0.5f) / height;
                    double latD = ((double)v - 0.5) * Math.PI;
                    SinLatRow[y] = Math.Sin(latD);
                    CosLatRow[y] = Math.Cos(latD);
                    float latRad = (v - 0.5f) * 3.14159265f;
                    CosRow[y] = Math.Max(0.16f, (float)Math.Cos(latRad));
                }

                GWidth = GHeight = EWidth = EHeight = MWidth = MHeight = OWidth = OHeight = -1;
                InvalidateStaticCache();
            }

            internal void EnsureBilinearTables(int width, int height, FlatMoistureState moisture, GlebaClimateSupplement gleba, ExoPlaSimSupplement exo, FlatOceanCirculation ocean)
            {
                if (gleba != null && (GWidth != gleba.Width || GHeight != gleba.Height))
                {
                    GWidth = gleba.Width; GHeight = gleba.Height;
                    BuildBilinear1D(width, height, GWidth, GHeight, out GX0, out GX1, out GTx, out GY0, out GY1, out GTy, out _, out _);
                }
                if (exo != null && (EWidth != exo.Width || EHeight != exo.Height))
                {
                    EWidth = exo.Width; EHeight = exo.Height;
                    BuildBilinear1D(width, height, EWidth, EHeight, out EX0, out EX1, out ETx, out EY0, out EY1, out ETy, out _, out _);
                }
                if (moisture != null && (MWidth != moisture.Width || MHeight != moisture.Height))
                {
                    MWidth = moisture.Width; MHeight = moisture.Height;
                    BuildBilinear1D(width, height, MWidth, MHeight, out MX0, out MX1, out MTx, out MY0, out MY1, out MTy, out MExactX, out MExactY);
                }
                if (ocean != null && (OWidth != ocean.Width || OHeight != ocean.Height))
                {
                    OWidth = ocean.Width; OHeight = ocean.Height;
                    BuildBilinear1D(width, height, OWidth, OHeight, out OX0, out OX1, out OTx, out OY0, out OY1, out OTy, out _, out _);
                }
            }

            static void BuildBilinear1D(
                int dstW, int dstH, int srcW, int srcH,
                out int[] x0Arr, out int[] x1Arr, out float[] txArr,
                out int[] y0Arr, out int[] y1Arr, out float[] tyArr,
                out bool[] exactXArr, out bool[] exactYArr)
            {
                x0Arr = new int[dstW]; x1Arr = new int[dstW]; txArr = new float[dstW]; exactXArr = new bool[dstW];
                y0Arr = new int[dstH]; y1Arr = new int[dstH]; tyArr = new float[dstH]; exactYArr = new bool[dstH];
                for (int x = 0; x < dstW; x++)
                {
                    float u = (x + 0.5f) / dstW;
                    float px = u * srcW - 0.5f;
                    int ix = (int)Math.Floor(px);
                    int col = (int)px;
                    exactXArr[x] = col == px && (uint)col < (uint)srcW;
                    txArr[x] = px - ix;
                    int x0 = FMath.Wrap(ix, srcW);
                    x0Arr[x] = x0;
                    x1Arr[x] = x0 + 1 == srcW ? 0 : x0 + 1;
                }
                for (int y = 0; y < dstH; y++)
                {
                    float v = (y + 0.5f) / dstH;
                    float pyRaw = v * srcH - 0.5f;
                    int rowRaw = (int)pyRaw;
                    exactYArr[y] = rowRaw == pyRaw && (uint)rowRaw < (uint)srcH;
                    float py = FMath.Clamp(pyRaw, 0f, srcH - 1f);
                    int iy = (int)py;
                    tyArr[y] = py - iy;
                    y0Arr[y] = iy;
                    y1Arr[y] = Math.Min(iy + 1, srcH - 1);
                }
            }

            internal static ulong ComputeStaticFingerprint(WorldData world)
            {
                unchecked
                {
                    ulong h = 14695981039346656037UL;
                    h = (h ^ (uint)world.TerrainRevision) * 1099511628211UL;
                    var elev = world.Elevation;
                    if (elev != null && elev.Length > 0)
                    {
                        int len = elev.Length;
                        int stride = Math.Max(1, len / 192);
                        if ((stride & 1) == 0) stride++;
                        if (stride % 3 == 0) stride += 2;
                        for (int i = 0; i < len; i += stride) h = (h ^ (ushort)elev[i]) * 1099511628211UL;
                        h = (h ^ (ushort)elev[len - 1]) * 1099511628211UL;
                    }
                    var water = world.Water;
                    if (water != null && water.Length > 0)
                    {
                        int len = water.Length;
                        int stride = Math.Max(1, len / 192);
                        if ((stride & 1) == 0) stride++;
                        if (stride % 3 == 0) stride += 2;
                        for (int i = 0; i < len; i += stride) h = (h ^ (byte)water[i]) * 1099511628211UL;
                        h = (h ^ (byte)water[len - 1]) * 1099511628211UL;
                    }
                    var sst = world.Moisture?.OceanCurrents?.SeaAnomaly;
                    if (sst != null && sst.Length > 0)
                    {
                        int len = sst.Length;
                        int stride = Math.Max(1, len / 192);
                        if ((stride & 1) == 0) stride++;
                        if (stride % 3 == 0) stride += 2;
                        for (int i = 0; i < len; i += stride)
                            h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(sst[i])) * 1099511628211UL;
                        h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(sst[len - 1])) * 1099511628211UL;
                    }
                    return h;
                }
            }
        }

        static float SampleBilinearBlend12(float[] field12, int c00, int c10, int c01, int c11, float tx, float ty, int m0, int m1, float blend)
        {
            float v0 = FMath.Lerp(FMath.Lerp(field12[c00 + m0], field12[c10 + m0], tx), FMath.Lerp(field12[c01 + m0], field12[c11 + m0], tx), ty);
            float v1 = FMath.Lerp(FMath.Lerp(field12[c00 + m1], field12[c10 + m1], tx), FMath.Lerp(field12[c01 + m1], field12[c11 + m1], tx), ty);
            return FMath.Lerp(v0, v1, blend);
        }

        /// <summary>
        /// Builds Gleba physical cloud synoptic forcing fields interpolated at <paramref name="calendarDay"/> on a width * height grid:
        /// - forcingRgba (RGBA32): R = SynopticLift, G = ItczDeepConvection, B = BaroclinicStormTrack, A = MoistureEnvelope
        /// - auxRgba (RGBA32): R = MarineStratocumulus, G = OrographicCrest, B = LeeRainShadow, A = PolarFrontNorm
        /// - upperWindHalf (RGBAHalf, optional): RG = start upper-level Rossby/outflow jet, BA = next half-month upper-level jet
        /// </summary>
        public static void BuildCloudSynopticFields(
            WorldData world, double calendarDay, int width, int height,
            byte[] forcingRgba, byte[] auxRgba, ushort[] upperWindHalf = null)
            => BuildCloudSynopticFields(world, calendarDay, width, height, forcingRgba, auxRgba, upperWindHalf, calendarDay, null, CancellationToken.None);

        /// <summary>
        /// Overload allowing the 15-day upper-level Rossby jet buffer (RG/BA blended by _MonthBlend in FlatSkyBake)
        /// to stay anchored to <paramref name="upperWindStartDay"/> while synoptic forcing and auxiliary fields
        /// refresh smoothly at <paramref name="calendarDay"/>.
        /// </summary>
        public static void BuildCloudSynopticFields(
            WorldData world, double calendarDay, int width, int height,
            byte[] forcingRgba, byte[] auxRgba, ushort[] upperWindHalf,
            double upperWindStartDay)
            => BuildCloudSynopticFields(world, calendarDay, width, height, forcingRgba, auxRgba, upperWindHalf, upperWindStartDay, null, CancellationToken.None);

        /// <summary>
        /// Zero-allocation, cancellable overload using a caller-owned <see cref="CloudSynopticWorkspace"/>
        /// for background thread execution without blocking the Unity main thread.
        /// </summary>
        public static void BuildCloudSynopticFields(
            WorldData world, double calendarDay, int width, int height,
            byte[] forcingRgba, byte[] auxRgba, ushort[] upperWindHalf,
            double upperWindStartDay, CloudSynopticWorkspace workspace, CancellationToken cancel)
        {
            if (world == null || forcingRgba == null || auxRgba == null) return;
            int count = width * height;
            if (forcingRgba.Length < count * 4 || auxRgba.Length < count * 4) return;
            cancel.ThrowIfCancellationRequested();

            var ws = workspace ?? new CloudSynopticWorkspace();
            ws.EnsureSize(width, height);

            // Align seasonal phase with FlatMonthlyForcing (each month m is centred at m*30 + 15)
            // so synoptic cloud forcing stays in phase with _ClimateTex, _ClimateWindTex, and FlatWeatherSimulation.
            var seasonForcing = new FlatMonthlyForcing(calendarDay);
            double centeredPeriod = calendarDay / 30.0 - 0.5;
            double whole = Math.Floor(centeredPeriod);
            int m0 = FMath.Wrap((int)whole, 12), m1 = (m0 + 1) % 12;
            float blend = (float)(centeredPeriod - whole);
            var moisture = world.Moisture;
            var gleba = moisture?.Gleba;
            var exo = moisture?.ExoPlaSim;
            var oceanCurrents = moisture?.OceanCurrents;
            var cfg = exo?.Config ?? world.PlanetClimate;
            bool customPlanet = cfg != null && !cfg.IsStandardModernEarth;
            float hadleyScale = customPlanet ? cfg.HadleyExpansionFactor : 1f;
            float targetHadleyDeg = customPlanet ? cfg.HadleyTerminusDeg : 30f;
            float targetPolarDeg = customPlanet ? cfg.PolarFrontTerminusDeg : 65f;
            float obliqScale = customPlanet ? FMath.Clamp(cfg.ObliquityDeg / 23.44f, 0f, 1.8f) : 1f;
            bool tidallyLocked = customPlanet && cfg.TidallyLocked;
            double subLonRad = tidallyLocked ? cfg.SubstellarLongitudeDeg * (Math.PI / 180.0) : 0.0;
            bool customCirculation = customPlanet && (Math.Abs(hadleyScale - 1f) > 0.03f || Math.Abs(targetPolarDeg - 65f) > 2f || tidallyLocked || Math.Abs(obliqScale - 1f) > 0.05f);
            float season = (float)Math.Cos(((m0 + blend) - 6.0) * Math.PI / 6.0);

            ws.EnsureBilinearTables(width, height, moisture, gleba, exo, oceanCurrents);

            var itczCol = ws.ItczCol;
            var sthzNCol = ws.SthzNCol;
            var sthzSCol = ws.SthzSCol;
            var polNCol = ws.PolNCol;
            var polSCol = ws.PolSCol;
            var mjoDryCol = ws.MjoDryCol;
            var lonRadCol = ws.LonRadCol;
            var sinLonCol = ws.SinLonCol;
            var cosLonCol = ws.CosLonCol;
            var sinLatRow = ws.SinLatRow;
            var cosLatRow = ws.CosLatRow;
            var cosRow = ws.CosRow;

            double mjoPhase = ((calendarDay % 360.0 + 360.0) % 360.0) / 45.0 * 2.0 * Math.PI;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                double lonRad = lonRadCol[x];
                float mjoWave = (float)(0.62 * Math.Cos(2.0 * lonRad - mjoPhase) + 0.38 * Math.Cos(3.0 * lonRad - 1.35 * mjoPhase + 1.15));
                mjoDryCol[x] = Smooth(-0.10f, 0.58f, -mjoWave);
                if (gleba != null)
                {
                    itczCol[x] = FMath.Lerp(gleba.SampleBandDegrees(gleba.ItczLatRad, u, m0), gleba.SampleBandDegrees(gleba.ItczLatRad, u, m1), blend);
                    sthzNCol[x] = FMath.Lerp(gleba.SampleBandDegrees(gleba.SthzNorthLatRad, u, m0), gleba.SampleBandDegrees(gleba.SthzNorthLatRad, u, m1), blend);
                    sthzSCol[x] = FMath.Lerp(gleba.SampleBandDegrees(gleba.SthzSouthLatRad, u, m0), gleba.SampleBandDegrees(gleba.SthzSouthLatRad, u, m1), blend);
                    polNCol[x] = FMath.Lerp(gleba.SampleBandDegrees(gleba.PolarNorthLatRad, u, m0), gleba.SampleBandDegrees(gleba.PolarNorthLatRad, u, m1), blend);
                    polSCol[x] = FMath.Lerp(gleba.SampleBandDegrees(gleba.PolarSouthLatRad, u, m0), gleba.SampleBandDegrees(gleba.PolarSouthLatRad, u, m1), blend);
                    if (customCirculation)
                    {
                        // Preserve Gleba's longitudinal land-sea wave perturbations while shifting
                        // the Hadley ridge & polar front to the planet's Held-Hou / ExoPlaSim circulation termini.
                        float waveScale = FMath.Lerp(1f, obliqScale, 0.5f);
                        itczCol[x] = tidallyLocked
                            ? 0f
                            : FMath.Clamp(itczCol[x] * obliqScale * FMath.Lerp(1f, hadleyScale, 0.35f), -targetHadleyDeg * 0.85f, targetHadleyDeg * 0.85f);
                        sthzNCol[x] = FMath.Clamp(targetHadleyDeg + (sthzNCol[x] - 30f) * waveScale, 14f, 82f);
                        sthzSCol[x] = FMath.Clamp(-targetHadleyDeg + (sthzSCol[x] + 30f) * waveScale, -82f, -14f);
                        polNCol[x] = FMath.Clamp(targetPolarDeg + (polNCol[x] - 62f) * 0.45f, sthzNCol[x] + 6f, 89f);
                        polSCol[x] = FMath.Clamp(-targetPolarDeg + (polSCol[x] + 62f) * 0.45f, -89f, sthzSCol[x] - 6f);
                    }
                }
                else
                {
                    float effSeason = tidallyLocked ? 0f : season * obliqScale;
                    itczCol[x] = FMath.Clamp(6f * effSeason, -targetHadleyDeg * 0.85f, targetHadleyDeg * 0.85f);
                    sthzNCol[x] = FMath.Clamp(targetHadleyDeg + 2f * effSeason, 14f, 82f);
                    sthzSCol[x] = FMath.Clamp(-targetHadleyDeg + 2f * effSeason, -82f, -14f);
                    polNCol[x] = FMath.Clamp(targetPolarDeg + 2f * effSeason, sthzNCol[x] + 6f, 89f);
                    polSCol[x] = FMath.Clamp(-targetPolarDeg + 2f * effSeason, -89f, sthzSCol[x] - 6f);
                }
            }

            uint seed = FMath.Seed(world.Seed);
            var climateYear = FlatClimateYear.At(seed, calendarDay);

            var windE = ws.WindE;
            var windN = ws.WindN;
            var elevG = ws.ElevG;
            var oceanRow = ws.OceanRow;
            var breezeRow = ws.BreezeRow;
            var rainGrid = ws.RainGrid;
            var tempGrid = ws.TempGrid;
            var slpTempGrid = ws.SlpTempGrid;
            var slpGrid = ws.SlpGrid;
            var zonalSlpMean = ws.ZonalSlpMean;
            var yearRainRow = ws.YearRainRow;
            var fetchEastKm = ws.FetchEastKm;
            var distWestKm = ws.DistWestKm;
            var dhDxGrid = ws.DhDxGrid;
            var dhDyGrid = ws.DhDyGrid;
            var fineLandGrid = ws.FineLandGrid;
            var eastCoastalGrid = ws.EastCoastalGrid;

            float baseLonKm = 40030f / width;
            float cellKm = 40030f / width;

            ulong staticFp = CloudSynopticWorkspace.ComputeStaticFingerprint(world);
            bool needStatic = !ws.HasStaticCache ||
                !ReferenceEquals(ws.CachedWorld, world) ||
                ws.CachedRevision != world.TerrainRevision ||
                !ReferenceEquals(ws.CachedMoisture, moisture) ||
                ws.CachedStaticFp != staticFp;

            if (needStatic)
            {
                for (int y = 0; y < height; y++)
                {
                    if ((y & 15) == 0) cancel.ThrowIfCancellationRequested();
                    float v = (y + 0.5f) / height;
                    int sy = FMath.Clamp((int)(v * world.Height), 0, world.Height - 1);
                    int row = y * width;
                    float lonStepKm = baseLonKm * cosRow[y];
                    for (int x = 0; x < width; x++)
                    {
                        float u = (x + 0.5f) / width;
                        int sx = FMath.Wrap((int)(u * world.Width), world.Width);
                        int tile = sy * world.Width + sx;
                        int idx = row + x;
                        elevG[idx] = Math.Max(0f, (float)world.Elevation[tile]);
                        fineLandGrid[idx] = FlatTerrainAnalysis.AreaWater(world.Water[tile]) ? 0f : 1f;
                        float oFrac = gleba != null
                            ? gleba.SampleStatic(gleba.OceanFraction, u, v)
                            : (FlatTerrainAnalysis.Ocean(world.Water[tile]) ? 1f : 0f);
                        oceanRow[idx] = oFrac;
                        breezeRow[idx] = exo != null ? exo.SampleStatic(exo.MaritimeSeaBreezeGuard, u, v) : 0f;
                    }

                    // Coordinate-free zonal ocean-basin fetch scan (periodic 2x wrap):
                    // Under tropical easterlies, eastern & central ocean margins shoal the thermocline (Walker descending
                    // limb + equatorial Ekman upwelling clear slot in ALL seasons), while warm water piles up in the far west.
                    float runWestward = 0f;
                    for (int step = 2 * width - 1; step >= 0; step--)
                    {
                        int x = step % width;
                        int idx = row + x;
                        if (oceanRow[idx] < 0.45f || breezeRow[idx] > 0.22f) runWestward = 0f;
                        else runWestward = Math.Min(18000f, runWestward + lonStepKm);
                        if (step < width)
                        {
                            fetchEastKm[idx] = runWestward;
                            eastCoastalGrid[idx] = 1f - Smooth(850f, 2300f, runWestward);
                        }
                    }
                    float runEastward = 0f;
                    for (int step = 0; step < 2 * width; step++)
                    {
                        int x = step % width;
                        int idx = row + x;
                        if (oceanRow[idx] < 0.45f || breezeRow[idx] > 0.22f) runEastward = 0f;
                        else runEastward = Math.Min(18000f, runEastward + lonStepKm);
                        if (step >= width) distWestKm[idx] = runEastward;
                    }
                }
                for (int y = 0; y < height; y++)
                {
                    int yS = Math.Max(0, y - 1), yN = Math.Min(height - 1, y + 1);
                    int row = y * width;
                    float inv2CosCell = 1f / (2f * cosRow[y] * cellKm);
                    float invDyCell = 1f / (Math.Max(1, yN - yS) * cellKm);
                    for (int x = 0; x < width; x++)
                    {
                        int xW = x == 0 ? width - 1 : x - 1;
                        int xE = x + 1 == width ? 0 : x + 1;
                        int idx = row + x;
                        dhDxGrid[idx] = (elevG[row + xE] - elevG[row + xW]) * inv2CosCell;
                        dhDyGrid[idx] = (elevG[yN * width + x] - elevG[yS * width + x]) * invDyCell;
                    }
                }
                ws.CachedWorld = world;
                ws.CachedRevision = world.TerrainRevision;
                ws.CachedMoisture = moisture;
                ws.CachedStaticFp = staticFp;
                ws.HasStaticCache = true;
            }

            var gX0 = ws.GX0; var gX1 = ws.GX1; var gTx = ws.GTx; var gY0 = ws.GY0; var gY1 = ws.GY1; var gTy = ws.GTy;
            var eX0 = ws.EX0; var eX1 = ws.EX1; var eTx = ws.ETx; var eY0 = ws.EY0; var eY1 = ws.EY1; var eTy = ws.ETy;
            var mX0 = ws.MX0; var mX1 = ws.MX1; var mTx = ws.MTx; var mY0 = ws.MY0; var mY1 = ws.MY1; var mTy = ws.MTy;
            var oX0 = ws.OX0; var oX1 = ws.OX1; var oTx = ws.OTx; var oY0 = ws.OY0; var oY1 = ws.OY1; var oTy = ws.OTy;
            var mExactX = ws.MExactX; var mExactY = ws.MExactY;
            int gW = gleba != null ? gleba.Width : 0;
            int eW = exo != null ? exo.Width : 0;
            int mW = moisture != null ? moisture.Width : 0;
            int oW = oceanCurrents != null ? oceanCurrents.Width : 0;
            var seaAnomArr = oceanCurrents?.SeaAnomaly;
            int worldW = world.Width, worldH = world.Height;
            var precipArr = world.Precipitation;
            var tempArr = world.Temperature;

            for (int y = 0; y < height; y++)
            {
                if ((y & 7) == 0) cancel.ThrowIfCancellationRequested();
                float v = (y + 0.5f) / height;
                float latDeg = (v - 0.5f) * 180f;
                int sy = FMath.Clamp((int)(v * worldH), 0, worldH - 1);
                int sy1 = Math.Min(sy + 1, worldH - 1);
                int row = y * width;
                double sinLat = sinLatRow[y], cosLat = cosLatRow[y];
                FlatWindBelts fallbackBelts = moisture == null ? new FlatWindBelts(latDeg, tidallyLocked ? 0f : season, cfg) : default;
                float fallbackE = 0f, fallbackN = 0f;
                if (moisture == null)
                {
                    fallbackBelts.Surface(6f * (tidallyLocked ? 0f : season * obliqScale), 0.7f, out fallbackE, out fallbackN);
                    fallbackE /= 0.32f;
                    fallbackN /= 0.32f;
                }
                int gRow0 = gleba != null ? gY0[y] * gW : 0;
                int gRow1 = gleba != null ? gY1[y] * gW : 0;
                float gTyVal = gleba != null ? gTy[y] : 0f;
                float slpFallback = gleba == null ? 1000f - 45f * (float)Math.Cos((latDeg - 8f * season * obliqScale) * (Math.PI / 30.0)) : 0f;
                int mRow0 = moisture != null ? mY0[y] * mW : 0;
                int mRow1 = moisture != null ? mY1[y] * mW : 0;
                float mTyVal = moisture != null ? mTy[y] : 0f;
                bool mExY = moisture != null && mExactY[y];

                double slpRowSum = 0.0;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    int sx = FMath.Wrap((int)(u * worldW), worldW);
                    int sx1 = sx + 1 == worldW ? 0 : sx + 1;
                    int idx = row + x;

                    float slpVal;
                    if (gleba != null)
                    {
                        int gx0 = gX0[x], gx1 = gX1[x];
                        slpVal = SampleBilinearBlend12(gleba.PressureHpa, (gRow0 + gx0) * 12, (gRow0 + gx1) * 12, (gRow1 + gx0) * 12, (gRow1 + gx1) * 12, gTx[x], gTyVal, m0, m1, blend);
                    }
                    else slpVal = slpFallback;
                    slpGrid[idx] = slpVal;
                    slpRowSum += slpVal;

                    if (moisture != null)
                    {
                        int mx0 = mX0[x], mx1 = mX1[x];
                        int mc00 = (mRow0 + mx0) * 12, mc10 = (mRow0 + mx1) * 12, mc01 = (mRow1 + mx0) * 12, mc11 = (mRow1 + mx1) * 12;
                        float mtx = mTx[x];
                        bool exact = mExY && mExactX[x];
                        windE[idx] = seasonForcing.SampleBilinear(moisture.East, mc00, mc10, mc01, mc11, mtx, mTyVal, exact);
                        windN[idx] = seasonForcing.SampleBilinear(moisture.North, mc00, mc10, mc01, mc11, mtx, mTyVal, exact);
                    }
                    else
                    {
                        windE[idx] = fallbackE;
                        windN[idx] = fallbackN;
                    }

                    // 2x2 neighborhood monthly rain & temperature with FlatMonthlyForcing + FlatClimateYear
                    int o00 = (sy * worldW + sx) * 12;
                    int o10 = (sy * worldW + sx1) * 12;
                    int o01 = (sy1 * worldW + sx) * 12;
                    int o11 = (sy1 * worldW + sx1) * 12;
                    float rainMm = 0.25f * seasonForcing.Sample(precipArr, o00, ClimateRange.Nonnegative)
                                 + 0.25f * seasonForcing.Sample(precipArr, o10, ClimateRange.Nonnegative)
                                 + 0.25f * seasonForcing.Sample(precipArr, o01, ClimateRange.Nonnegative)
                                 + 0.25f * seasonForcing.Sample(precipArr, o11, ClimateRange.Nonnegative);
                    float tempC = 0.25f * seasonForcing.Sample(tempArr, o00)
                                + 0.25f * seasonForcing.Sample(tempArr, o10)
                                + 0.25f * seasonForcing.Sample(tempArr, o01)
                                + 0.25f * seasonForcing.Sample(tempArr, o11);

                    float dxDir = (float)(sinLonCol[x] * cosLat);
                    float dyDir = (float)sinLat;
                    float dzDir = (float)(cosLonCol[x] * cosLat);
                    climateYear.Offsets(dxDir, dyDir, dzDir, out float yearTempDelta, out float yearRainDelta);
                    yearRainRow[idx] = yearRainDelta;
                    rainGrid[idx] = Math.Max(0f, rainMm * (1f + yearRainDelta));
                    tempGrid[idx] = tempC + yearTempDelta;
                    slpTempGrid[idx] = tempGrid[idx] + 0.0065f * elevG[idx];
                }
                zonalSlpMean[y] = (float)(slpRowSum / width);
            }

            double rossbyT0 = ((upperWindStartDay % 360.0 + 360.0) % 360.0) / 360.0 * 2.0 * Math.PI;
            double rossbyT1 = (((upperWindStartDay + FlatMonthlyForcing.HalfMonthDays) % 360.0 + 360.0) % 360.0) / 360.0 * 2.0 * Math.PI;
            var rossby0 = upperWindHalf != null ? new GlebaRossbyWaveField(seed) { RossbyTimeRadians = rossbyT0 } : null;
            var rossby1 = upperWindHalf != null ? new GlebaRossbyWaveField(seed) { RossbyTimeRadians = rossbyT1 } : null;
            if (upperWindHalf != null && customCirculation)
            {
                float subJetRad = FMath.Clamp(targetHadleyDeg, 15f, 72f) * ((float)Math.PI / 180f);
                float polJetRad = FMath.Clamp(targetPolarDeg, 35f, 82f) * ((float)Math.PI / 180f);
                rossby0.BaseNorthSubtropicalJetLatRad = rossby1.BaseNorthSubtropicalJetLatRad = subJetRad;
                rossby0.BaseSouthSubtropicalJetLatRad = rossby1.BaseSouthSubtropicalJetLatRad = -subJetRad;
                rossby0.BaseNorthPolarJetLatRad = rossby1.BaseNorthPolarJetLatRad = polJetRad;
                rossby0.BaseSouthPolarJetLatRad = rossby1.BaseSouthPolarJetLatRad = -polJetRad;
            }
            var upperEndForcing = upperWindHalf != null ? new FlatMonthlyForcing(upperWindStartDay + FlatMonthlyForcing.HalfMonthDays) : default;
            float itczWidth = 6.8f * FMath.Lerp(1f, hadleyScale, 0.65f);
            float invItczWidth2 = 1f / (itczWidth * itczWidth);
            float eqSlotWidth = 4.1f * FMath.Lerp(1f, hadleyScale, 0.5f);
            float offEqCenter = 7.4f * FMath.Lerp(1f, hadleyScale, 0.72f);
            float offEqWidth = 2.9f * FMath.Lerp(1f, hadleyScale, 0.55f);
            float polarWidth = 14.5f * FMath.Lerp(1f, hadleyScale, 0.35f);
            float invPolarWidth2 = 1f / (polarWidth * polarWidth);

            for (int y = 0; y < height; y++)
            {
                if ((y & 7) == 0) cancel.ThrowIfCancellationRequested();
                float v = (y + 0.5f) / height;
                float latDeg = (v - 0.5f) * 180f;
                float absLat = Math.Abs(latDeg);
                double latRad = (v - 0.5) * Math.PI;
                int yS = Math.Max(0, y - 1), yN = Math.Min(height - 1, y + 1);
                int row = y * width;
                float cosY = cosRow[y], cosYN = cosRow[yN], cosYS = cosRow[yS];
                float inv2CosY = 1f / (2f * cosY);
                float invDy = 1f / Math.Max(1, yN - yS);
                float inv2CosCell = 1f / (2f * cosY * cellKm);
                float invDyCell = 1f / (Math.Max(1, yN - yS) * cellKm);
                float hemiSgn = FMath.Clamp((float)sinLatRow[y] * 6f, -1f, 1f);
                float latSign = latDeg >= 0f ? 1f : -1f;
                float upwellLatGate = Smooth(6f * hadleyScale, 15f * hadleyScale, absLat) * (1f - Smooth(32f * hadleyScale, 44f * hadleyScale, absLat));
                float monsoonLatGate = 1f - Smooth(26f * hadleyScale, 35f * hadleyScale, absLat);
                float greenOceanLatGate = 1f - Smooth(15f * hadleyScale, 24f * hadleyScale, absLat);
                float eqCore = (float)Math.Exp(-(latDeg * latDeg) / (eqSlotWidth * eqSlotWidth));
                float tropOceanLatGate = 1f - Smooth(9f * hadleyScale, 23f * hadleyScale, absLat);
                float offEqDist = Math.Abs(absLat - offEqCenter);
                float hemiSummerBias = 0.64f + 0.36f * (float)Math.Tanh(latDeg * season * obliqScale * 0.25);
                float doubleItczLatFactor = hemiSummerBias * (float)Math.Exp(-(offEqDist * offEqDist) / (offEqWidth * offEqWidth));
                float spczBelt = Smooth(8f * hadleyScale, 15f * hadleyScale, absLat) * (1f - Smooth(27f * hadleyScale, 37f * hadleyScale, absLat));
                float scLat = Smooth(6f * hadleyScale, 13f * hadleyScale, absLat) * (1f - Smooth(33f * hadleyScale, 43f * hadleyScale, absLat));
                float defaultMidLat = customCirculation ? 0f : Smooth(24f, 35f, absLat) * (1f - Smooth(68f, 82f, absLat));
                float rowZonalSlp = zonalSlpMean[y];

                int gRow0 = gleba != null ? gY0[y] * gW : 0;
                int gRow1 = gleba != null ? gY1[y] * gW : 0;
                float gTyVal = gleba != null ? gTy[y] : 0f;
                int eRow0 = exo != null ? eY0[y] * eW : 0;
                int eRow1 = exo != null ? eY1[y] * eW : 0;
                float eTyVal = exo != null ? eTy[y] : 0f;
                int mRow0 = moisture != null ? mY0[y] * mW : 0;
                int mRow1 = moisture != null ? mY1[y] * mW : 0;
                float mTyVal = moisture != null ? mTy[y] : 0f;
                bool mExY = moisture != null && mExactY[y];
                int oRow0 = seaAnomArr != null ? oY0[y] * oW : 0;
                int oRow1 = seaAnomArr != null ? oY1[y] * oW : 0;
                float oTyVal = seaAnomArr != null ? oTy[y] : 0f;

                for (int x = 0; x < width; x++)
                {
                    int idx = row + x;
                    int xW = x == 0 ? width - 1 : x - 1;
                    int xE = x + 1 == width ? 0 : x + 1;

                    float rainMm = rainGrid[idx];
                    float tempC = tempGrid[idx];
                    float yearRainDelta = yearRainRow[idx];
                    float oceanFrac = oceanRow[idx];
                    float slp = slpGrid[idx];

                    int gc00 = 0, gc10 = 0, gc01 = 0, gc11 = 0;
                    float gtx = 0f;
                    float rainImpact, rainShadow;
                    if (gleba != null)
                    {
                        int gx0 = gX0[x], gx1 = gX1[x];
                        gc00 = (gRow0 + gx0) * 12; gc10 = (gRow0 + gx1) * 12;
                        gc01 = (gRow1 + gx0) * 12; gc11 = (gRow1 + gx1) * 12;
                        gtx = gTx[x];
                        rainImpact = SampleBilinearBlend12(gleba.RainfallImpact, gc00, gc10, gc01, gc11, gtx, gTyVal, m0, m1, blend);
                        rainShadow = SampleBilinearBlend12(gleba.RainShadowiness, gc00, gc10, gc01, gc11, gtx, gTyVal, m0, m1, blend);
                    }
                    else
                    {
                        rainImpact = 0f;
                        rainShadow = 0f;
                    }

                    int mc00 = 0, mc10 = 0, mc01 = 0, mc11 = 0;
                    float mtx = 0f;
                    bool mExact = false;
                    float rh, condNorm;
                    if (moisture != null)
                    {
                        int mx0 = mX0[x], mx1 = mX1[x];
                        mc00 = (mRow0 + mx0) * 12; mc10 = (mRow0 + mx1) * 12;
                        mc01 = (mRow1 + mx0) * 12; mc11 = (mRow1 + mx1) * 12;
                        mtx = mTx[x];
                        mExact = mExY && mExactX[x];
                        rh = seasonForcing.SampleBilinear(moisture.Humidity, mc00, mc10, mc01, mc11, mtx, mTyVal, mExact, ClimateRange.Fraction);
                        float condDay = Math.Max(0f, seasonForcing.SampleBilinear(moisture.Condensation, mc00, mc10, mc01, mc11, mtx, mTyVal, mExact, ClimateRange.Nonnegative) * (1f + yearRainDelta));
                        condNorm = 1f - (float)Math.Exp(-condDay * 30f / 65f);
                    }
                    else
                    {
                        rh = FMath.Clamp((oceanFrac > 0.5f ? 0.78f : 0.25f) + rainMm / 200f, 0f, 1f);
                        condNorm = 0f;
                    }
                    rh = FMath.Clamp(rh * (1f + 0.28f * yearRainDelta), 0f, 1f);

                    float sstAnom = 0f;
                    if (seaAnomArr != null && oceanFrac > 0.05f)
                    {
                        int ox0 = oX0[x], ox1 = oX1[x];
                        float otx = oTx[x];
                        sstAnom = FMath.Lerp(
                            FMath.Lerp(seaAnomArr[oRow0 + ox0], seaAnomArr[oRow0 + ox1], otx),
                            FMath.Lerp(seaAnomArr[oRow1 + ox0], seaAnomArr[oRow1 + ox1], otx),
                            oTyVal);
                    }
                    float eastCoastal = eastCoastalGrid[idx];
                    float seasonalUpwell = 0f;
                    if (gleba != null && oceanFrac > 0.05f && eastCoastal > 0.01f && upwellLatGate > 0f)
                    {
                        float curN = SampleBilinearBlend12(gleba.CurrentNorth, gc00, gc10, gc01, gc11, gtx, gTyVal, m0, m1, blend);
                        seasonalUpwell = eastCoastal * FMath.Clamp(-hemiSgn * curN * 8.5f, -0.40f, 0.80f) * upwellLatGate;
                    }
                    float effSstAnom = sstAnom - 0.85f * seasonalUpwell;

                    // Surface wind convergence (-div V), windward/leeward orographic slope forcing,
                    // and baroclinic sea-level thermal-gradient frontogenesis (matching FlatWeatherSimulation.SynopticLift).
                    float duDx = (windE[row + xE] - windE[row + xW]) * inv2CosY;
                    float dvDy = (windN[yN * width + x] * cosYN - windN[yS * width + x] * cosYS) * invDy;
                    float windConv = FMath.Clamp(-(duDx + dvDy) * 3.2f, -0.8f, 1.0f);

                    float dTdx = (slpTempGrid[row + xE] - slpTempGrid[row + xW]) * inv2CosCell;
                    float dTdy = (slpTempGrid[yN * width + x] - slpTempGrid[yS * width + x]) * invDyCell;
                    float thermalGrad300 = (float)Math.Sqrt(dTdx * dTdx + dTdy * dTdy) * 300f;
                    float baroclinicFront = Smooth(1.2f, 5.5f, thermalGrad300) * Smooth(0.26f, 0.72f, rh);

                    float dhDx = dhDxGrid[idx];
                    float dhDy = dhDyGrid[idx];
                    float slopeDot = windE[idx] * dhDx + windN[idx] * dhDy; // m/km * windUnit
                    float leeDownslope = FMath.Clamp(-slopeDot / 2.20f, 0f, 1f);
                    float eqFacingSlope = Math.Max(0f, latSign * dhDy);
                    float anabaticCrest = 0.34f * Smooth(2.0f, 7.2f, eqFacingSlope) * Smooth(0.26f, 0.56f, rh) * Smooth(-6f, 6f, tempC) * (1f - 0.85f * leeDownslope);
                    float windwardLift = Math.Max(FMath.Clamp(slopeDot / 1.65f, 0f, 1f), anabaticCrest);

                    float lowP = FMath.Clamp((1010f - slp) / 58f, 0f, 1f) + 0.50f * FMath.Clamp(rainImpact, 0f, 0.5f);
                    float highP = FMath.Clamp((slp - 1012f) / 55f, 0f, 1f);
                    // Eddy (zonal-anomaly) sea-level pressure separates synoptic troughs/ridges from the
                    // static meridional pressure profile without letting continental summer heat lows
                    // (e.g. July Asian monsoon) artificially suppress moderate-SLP open-ocean clouds.
                    float slpEddy = slp - rowZonalSlp;
                    float eddyLowP = FMath.Clamp(-slpEddy / 28f, 0f, 1f);
                    float absHighP = FMath.Clamp((slp - 1010f) / 18f, 0f, 1f);
                    float eddyHighP = FMath.Clamp(slpEddy / 28f, 0f, 1f) * (0.35f + 0.65f * absHighP);
                    float negImpact = FMath.Clamp(-rainImpact, 0f, 1f);
                    float dSthz = Math.Min(Math.Abs(latDeg - sthzNCol[x]), Math.Abs(latDeg - sthzSCol[x]));
                    float sthzRidge = (float)Math.Exp(-(dSthz * dSthz) / (9.5 * 9.5));
                    float aridSub = FMath.Clamp((0.44f - rh) / 0.30f, 0f, 1f) * (1f - 0.65f * oceanFrac);

                    float dItcz = Math.Abs(latDeg - itczCol[x]);
                    float itczBand = (float)Math.Exp(-(dItcz * dItcz) * invItczWidth2);
                    float monsoonCore = Smooth(105f, 240f, rainMm) * Smooth(18.5f, 24.5f, tempC) * monsoonLatGate;
                    float exoCapeBoost = 0f, exoCinDamp = 0f, capeVal = 0f, deepSoilVal = 0f, shallowSoilVal = 0f, chi = 0.35f;
                    float convFrac = 0f, stratRainVal = 0f, virgaDry = 0f, breezeVal = breezeRow[idx];
                    if (exo != null)
                    {
                        int ex0 = eX0[x], ex1 = eX1[x];
                        int ec00 = (eRow0 + ex0) * 12, ec10 = (eRow0 + ex1) * 12;
                        int ec01 = (eRow1 + ex0) * 12, ec11 = (eRow1 + ex1) * 12;
                        float etx = eTx[x];
                        capeVal = SampleBilinearBlend12(exo.CapeJkg, ec00, ec10, ec01, ec11, etx, eTyVal, m0, m1, blend);
                        deepSoilVal = SampleBilinearBlend12(exo.DeepSoil, ec00, ec10, ec01, ec11, etx, eTyVal, m0, m1, blend);
                        shallowSoilVal = SampleBilinearBlend12(exo.ShallowSoil, ec00, ec10, ec01, ec11, etx, eTyVal, m0, m1, blend);
                        chi = SampleBilinearBlend12(exo.EntropyDeficit, ec00, ec10, ec01, ec11, etx, eTyVal, m0, m1, blend);
                        float convRainVal = SampleBilinearBlend12(exo.ConvectiveRain, ec00, ec10, ec01, ec11, etx, eTyVal, m0, m1, blend);
                        stratRainVal = SampleBilinearBlend12(exo.StratiformRain, ec00, ec10, ec01, ec11, etx, eTyVal, m0, m1, blend);
                        float virgaVal = SampleBilinearBlend12(exo.VirgaReevap, ec00, ec10, ec01, ec11, etx, eTyVal, m0, m1, blend);
                        float totalModal = convRainVal + stratRainVal;
                        convFrac = totalModal > 1f ? FMath.Clamp(convRainVal / totalModal, 0f, 1f) : 0f;
                        virgaDry = totalModal + virgaVal > 1f ? FMath.Clamp(virgaVal / (totalModal + virgaVal + 12f), 0f, 1f) : 0f;
                        exoCapeBoost = 0.30f * Smooth(380f, 2000f, capeVal)
                                     + 0.12f * convFrac * Smooth(35f, 160f, rainMm)
                                     + 0.16f * breezeVal * Smooth(19f, 26f, tempC);
                        exoCinDamp = 0.28f * Smooth(0.66f, 0.94f, chi) * (1f - breezeVal) + 0.10f * virgaDry * (1f - oceanFrac);
                    }
                    // Continental "Green Ocean" afternoon canopy-transpiration convection over humid equatorial rainforests
                    // (Amazonia, Congo) persists across seasons via deep-root moisture recycling even when the heat-low migrates.
                    float greenOceanConv = (1f - oceanFrac) * Smooth(68f, 165f, rainMm + 48f * deepSoilVal) * Smooth(20.0f, 24.5f, tempC)
                                         * Smooth(0.20f, 0.48f, Math.Max(rh, 0.65f * deepSoilVal)) * greenOceanLatGate;
                    float landSoilMoist = (1f - oceanFrac) * (0.45f * shallowSoilVal + 0.55f * deepSoilVal);
                    float warmSoilConv = landSoilMoist * Smooth(16f, 26f, tempC) * Smooth(0.30f, 0.70f, rh);
                    float moistGate = Smooth(0.32f, 0.72f, rh + 0.38f * FMath.Clamp((rainMm + 35f * deepSoilVal) / 190f, 0f, 1f));

                    // Orthogonal Hadley-Walker Double Circulation + Equatorial Ekman Upwelling Cold-Tongue & Split Double-ITCZ:
                    float easterlyTrade = Smooth(-0.005f, 0.048f, -windE[idx]);
                    float basinSpanKm = fetchEastKm[idx] + distWestKm[idx];
                    float westwardBasinPos = basinSpanKm >= 34000f
                        ? 0.45f
                        : fetchEastKm[idx] / Math.Max(1400f, basinSpanKm);
                    float warmSst = Smooth(0.25f, 2.15f, effSstAnom);
                    float warmElNino = Smooth(0.75f, 2.00f, effSstAnom);
                    float eastCentralBasin = Smooth(1600f, 3800f, basinSpanKm) * (1f - Smooth(0.46f, 0.76f, westwardBasinPos)) * (1f - 0.85f * warmElNino);
                    float westernSomaliDiv = Smooth(1800f, 4000f, basinSpanKm) * (1f - Smooth(900f, 2400f, distWestKm[idx]))
                                           * Smooth(0f, 110f, 155f - rainMm);
                    float warmPool = Math.Max(Smooth(0.18f, 0.85f, effSstAnom) * Smooth(0.50f, 0.80f, westwardBasinPos), 0.78f * warmElNino);
                    float coolUpwelling = FMath.Clamp((0.18f - effSstAnom) / 1.75f, 0f, 1f);
                    float openEqOcean = Smooth(0.45f, 0.85f, oceanFrac) * (1f - Smooth(0.08f, 0.34f, breezeVal));
                    float mjoDryPhase = mjoDryCol[x];

                    float ekmanColdSlot = openEqOcean * eqCore
                                        * Math.Max(0.48f + 0.52f * easterlyTrade, eastCentralBasin)
                                        * (1f - 0.84f * warmPool)
                                        * Smooth(14f, 25f, tempC);
                    float tropOceanBelt = openEqOcean * tropOceanLatGate;
                    float walkerDescend = tropOceanBelt * Math.Max(eastCentralBasin * (1f - 0.75f * warmPool), westernSomaliDiv);
                    float walkerClear = FMath.Clamp(
                        0.88f * ekmanColdSlot + 0.56f * walkerDescend + 0.44f * tropOceanBelt * mjoDryPhase * (1f - 0.50f * warmPool),
                        0f, 0.94f);

                    float doubleItczBand = openEqOcean * (0.40f + 0.60f * easterlyTrade) * doubleItczLatFactor
                                         * (1f - 0.45f * mjoDryPhase);
                    // Diagonal Subtropical Convergence Zones (SPCZ / SACZ / Meiyu-Baiu): warm western-ocean moisture export toward midlatitudes
                    float spczBand = oceanFrac * spczBelt * Smooth(0.42f, 0.82f, westwardBasinPos + 0.25f * FMath.Clamp(effSstAnom, 0f, 1.5f))
                                   * Smooth(95f, 210f, rainMm) * Smooth(-0.08f, 0.35f, windConv);
                    float effectiveItczBand = Math.Max(Math.Max(itczBand * (1f - 0.90f * ekmanColdSlot), 0.76f * doubleItczBand), 0.62f * spczBand);

                    float landOrIsland = FMath.Clamp((1f - oceanFrac) + 0.92f * breezeVal, 0f, 1f);
                    float walkerMarineAscend = FMath.Clamp(
                        0.58f * warmPool + 0.46f * doubleItczBand + 0.38f * spczBand + 0.30f * Smooth(165f, 285f, rainMm) * (1f - 0.70f * eastCentralBasin)
                        + 0.26f * Smooth(0.18f, 0.68f, lowP + 0.5f * Math.Max(0f, windConv))
                        + 0.32f * Smooth(750f, 1950f, capeVal) * Smooth(18f, 25f, tempC + 0.5f * effSstAnom)
                        + 0.22f * warmElNino
                        - 0.45f * eastCentralBasin * (1f - 0.85f * doubleItczBand) - 0.35f * mjoDryPhase * (1f - 0.65f * warmElNino),
                        0.05f, 1f);
                    float walkerGate = FMath.Lerp(walkerMarineAscend, 1f, landOrIsland) * (1f - 0.90f * ekmanColdSlot);
                    if (tidallyLocked)
                    {
                        float cosSub = (float)(cosLatRow[y] * Math.Cos(lonRadCol[x] - subLonRad));
                        walkerGate *= Smooth(-0.15f, 0.55f, cosSub);
                    }

                    float landRainShadowDamp = FMath.Lerp(0.70f, 0.22f, greenOceanConv);
                    float convConvergence = 0.14f * Math.Max(0f, windConv) * Smooth(15f, 25f, tempC) * moistGate;
                    float sstConvMod = oceanFrac * Smooth(17.5f, 25.5f, tempC + 0.5f * effSstAnom) * (0.18f * warmSst - 0.14f * coolUpwelling);
                    float baseConv = Math.Max(effectiveItczBand * moistGate, Math.Max(monsoonCore * 0.88f, greenOceanConv * 0.92f));
                    float itczConv = FMath.Clamp(
                        (baseConv + (exoCapeBoost + 0.12f * warmSoilConv) * moistGate * walkerGate + convConvergence + sstConvMod * moistGate)
                        * walkerGate * (1f - 0.65f * negImpact * (1f - 0.80f * greenOceanConv))
                        * (1f - landRainShadowDamp * rainShadow)
                        * (1f - FMath.Clamp(exoCinDamp, 0f, 0.65f))
                        * (1f - 0.52f * coolUpwelling * oceanFrac * (1f - breezeVal)),
                        0f, 1f);

                    float dPolar = Math.Min(Math.Abs(latDeg - polNCol[x]), Math.Abs(latDeg - polSCol[x]));
                    float polarBand = (float)Math.Exp(-(dPolar * dPolar) * invPolarWidth2);
                    float midLat;
                    if (customCirculation)
                    {
                        float localSthzAbs = Math.Abs(latDeg >= 0f ? sthzNCol[x] : sthzSCol[x]);
                        float localPolAbs = Math.Abs(latDeg >= 0f ? polNCol[x] : polSCol[x]);
                        midLat = Smooth(localSthzAbs * 0.78f, localSthzAbs * 1.08f, absLat) * (1f - Smooth(Math.Min(85f, localPolAbs + 5f), Math.Min(89.5f, localPolAbs + 13f), absLat));
                    }
                    else midLat = defaultMidLat;
                    float warmFront = FMath.Clamp(effSstAnom / 2.4f, 0f, 1f);
                    float contBlock = (1f - oceanFrac) * FMath.Clamp(0.55f * highP + 0.45f * FMath.Clamp((0.38f - rh) * 2.5f, 0f, 1f), 0f, 0.85f);
                    float moistAir = Smooth(0.06f, 0.34f, rh + 0.32f * FMath.Clamp((rainMm + 25f * deepSoilVal) / 140f, 0f, 1f) + 0.22f * condNorm);
                    float frontDyn = 0.14f * baroclinicFront + 0.14f * Math.Max(0f, windConv) * Smooth(0.28f, 0.72f, rh) + 0.14f * eddyLowP * Smooth(0.28f, 0.72f, rh);
                    float stratSupport = exo != null ? 0.05f * Smooth(20f, 95f, stratRainVal) * (1f - convFrac) : 0f;
                    float stormTrack = FMath.Clamp(
                        midLat * (0.66f * polarBand + 0.20f + frontDyn + stratSupport)
                        * (0.42f + 0.50f * oceanFrac + 0.30f * warmFront - 0.16f * coolUpwelling * oceanFrac)
                        * (1f - contBlock) * (1f - 0.38f * eddyHighP) * moistAir,
                        0f, 1f);

                    float moistureEnv = FMath.Clamp(
                        0.54f * Smooth(0.22f, 0.82f, rh) + 0.30f * FMath.Clamp(rainMm / 185f, 0f, 1f) + 0.14f * condNorm
                        + 0.10f * landSoilMoist * Smooth(0.18f, 0.65f, rh + 0.35f * deepSoilVal)
                        - 0.36f * aridSub - 0.14f * virgaDry * (1f - oceanFrac) - 0.50f * walkerClear,
                        0f, 1f);

                    // Cold eastern-boundary upwelling + subtropical subsidence lid + low-level marine stability -> low Marine Stratocumulus.
                    // Neutral/warm open subtropical oceans stay < 0.30 (< scProtect=0.38) so gyres stay clear with trade Cu only;
                    // genuinely cold SST / eastern-boundary upwelling crosses scProtect (> 0.42) into low Sc decks.
                    float capeInstability = exo != null ? Smooth(550f, 1950f, capeVal) : Smooth(22f, 28f, tempC + effSstAnom);
                    float inversionLid = FMath.Clamp(0.50f * highP + 0.46f * sthzRidge + 0.30f * negImpact + 0.14f * Smooth(0.42f, 0.85f, chi), 0f, 1f);
                    float coldDeckGate = Math.Max(coolUpwelling * (0.40f + 0.60f * eastCoastal), Smooth(0.95f, 1.85f, -effSstAnom));
                    float lowLevelStability = FMath.Clamp((0.08f + 0.92f * coldDeckGate) * (0.30f + 0.70f * inversionLid) * (1f - 0.78f * warmSst) * (1f - 0.48f * capeInstability), 0f, 1f);
                    float subtropSc = oceanFrac * scLat * FMath.Clamp(lowLevelStability * 1.18f, 0f, 1f);
                    float eqColdSc = ekmanColdSlot * (0.28f + 0.24f * Math.Max(coolUpwelling, eastCentralBasin)) * (1f - 0.62f * warmSst);
                    float marineSc = FMath.Clamp(
                        Math.Max(subtropSc, eqColdSc) * (1f - 0.85f * itczConv) * (1f - 0.68f * leeDownslope) * moistAir,
                        0f, 1f);

                    float orogCrest = FMath.Clamp(windwardLift * Smooth(0.18f, 0.54f, rh + 0.30f * FMath.Clamp(rainMm / 140f, 0f, 1f)), 0f, 1f);
                    float dryAirClear = 0.96f * (1f - moistAir);
                    float ridgeClear = FMath.Clamp(0.26f * eddyHighP + 0.18f * Math.Max(0f, -windConv), 0f, 0.42f) * (1f - 0.55f * FMath.Clamp(rainMm / 150f, 0f, 1f));
                    // Active upslope flow overrides stale climatological rain-shadowiness from the opposite wind regime,
                    // while active downslope flow builds a dynamic foehn rain shadow on the leeward slope.
                    float effStaticShadow = rainShadow * (1f - 0.82f * windwardLift);
                    float leeShadow = FMath.Clamp(effStaticShadow + 0.56f * leeDownslope + 0.82f * walkerClear + ridgeClear + dryAirClear, 0f, 1f);
                    float localPol = latDeg >= 0f ? polNCol[x] : -polSCol[x];
                    float polNorm = FMath.Clamp((localPol - 30f) / 58f, 0f, 1f);

                    // Physically calibrated Sundqvist / PlaSim / Gleba climatological cloud potential (forcing.r in [0.04, 0.88])
                    // coupled with the restored moisture-gated synoptic vertical-motion anomaly (eddyLowP/eddyHighP, windConv,
                    // windwardLift/leeDownslope, baroclinicFront, CAPE/CIN, rainImpact) so weather-scale variations survive calibration
                    // without distorting the 30-year NOAA zonal climatology.
                    float fineLand = fineLandGrid[idx];
                    float fineSea = 1f - fineLand;
                    float effRainMm = rainMm * (1f - 0.78f * fineSea * leeShadow) + 34f * fineLand * deepSoilVal;
                    float fRain = 1f - (float)Math.Exp(-effRainMm / 72f);
                    float fRh = Smooth(0.26f, 0.84f, rh) * (1f - 0.55f * leeShadow);
                    float aridLandClear = fineLand * (1f - Smooth(0.12f, 0.62f, rh * Smooth(2f, 45f, rainMm + 25f * deepSoilVal))) * (1f - Smooth(1f, 32f, rainMm));
                    float subtropDry = (1f - 0.72f * fineSea) * (0.55f * highP + 0.45f * negImpact) * (1f - Smooth(4f, 42f, rainMm));
                    float winStratus = fineLand * (1f - Smooth(-18f, 2f, tempC)) * Smooth(0.34f, 0.75f, rh) * (1f - Smooth(1300f, 2600f, elevG[idx]));
                    float icePlateauClear = Smooth(1400f, 2900f, elevG[idx]) * (1f - Smooth(-32f, -10f, tempC));

                    float moistSupport = Smooth(0.18f, 0.68f, rh + 0.28f * FMath.Clamp((rainMm + 25f * deepSoilVal) / 150f, 0f, 1f) + 0.18f * condNorm);
                    float posImpact = FMath.Clamp(rainImpact, 0f, 1f);
                    float condExcess = Math.Max(0f, condNorm - fRain);
                    float dynAscent = 0.54f * eddyLowP
                                    + 0.42f * Math.Max(0f, windConv)
                                    + 0.44f * windwardLift
                                    + 0.22f * baroclinicFront
                                    + 0.22f * exoCapeBoost * walkerGate
                                    + 0.20f * posImpact
                                    + 0.14f * condExcess;
                    float dynDescent = 0.54f * eddyHighP
                                     + 0.36f * Math.Max(0f, -windConv)
                                     + 0.34f * leeDownslope
                                     + 0.24f * exoCinDamp
                                     + 0.20f * negImpact;
                    float synopticAnomaly = FMath.Clamp(dynAscent * moistSupport - dynDescent, -0.78f, 0.85f);

                    float climBase = 0.05f + 0.24f * fineSea * moistAir + 0.14f * fineLand * Smooth(0.24f, 0.64f, rh + 0.45f * shallowSoilVal) * Smooth(3f, 28f, rainMm + 30f * deepSoilVal);
                    float climDyn = 0.36f * fRain + 0.07f * fRh + 0.10f * marineSc + 0.16f * stormTrack + 0.22f * itczConv + 0.13f * winStratus + 0.05f * orogCrest;
                    float climSupp = 0.24f * leeShadow + 0.16f * aridLandClear + 0.10f * subtropDry + 0.18f * icePlateauClear;
                    float synopticLift = FMath.Clamp(climBase + climDyn - climSupp + 0.24f * synopticAnomaly, 0.04f, 0.88f);

                    int p4 = idx * 4;
                    forcingRgba[p4] = (byte)FMath.Clamp((int)Math.Round(synopticLift * 255f), 0, 255);
                    forcingRgba[p4 + 1] = (byte)FMath.Clamp((int)Math.Round(itczConv * 255f), 0, 255);
                    forcingRgba[p4 + 2] = (byte)FMath.Clamp((int)Math.Round(stormTrack * 255f), 0, 255);
                    forcingRgba[p4 + 3] = (byte)FMath.Clamp((int)Math.Round(moistureEnv * 255f), 0, 255);

                    auxRgba[p4] = (byte)FMath.Clamp((int)Math.Round(marineSc * 255f), 0, 255);
                    auxRgba[p4 + 1] = (byte)FMath.Clamp((int)Math.Round(orogCrest * 255f), 0, 255);
                    auxRgba[p4 + 2] = (byte)FMath.Clamp((int)Math.Round(leeShadow * 255f), 0, 255);
                    auxRgba[p4 + 3] = (byte)FMath.Clamp((int)Math.Round(polNorm * 255f), 0, 255);

                    if (upperWindHalf != null && upperWindHalf.Length >= count * 4)
                    {
                        double lonRad = lonRadCol[x];
                        rossby0.GetJetStreamMovementAtLatLon(latRad, lonRad, out float jx0, out float jy0);
                        rossby1.GetJetStreamMovementAtLatLon(latRad, lonRad, out float jx1, out float jy1);
                        float endWindN = moisture != null
                            ? upperEndForcing.SampleBilinear(moisture.North, mc00, mc10, mc01, mc11, mtx, mTyVal, mExact)
                            : windN[idx];
                        // Scale Rossby jet into surface-wind units + tropical upper-tropospheric easterly anvil outflow
                        float tropEasterly = -0.55f * itczConv;
                        float ux0 = jx0 * 52f + tropEasterly;
                        float uy0 = jy0 * 52f + 0.12f * windN[idx] * itczConv;
                        float ux1 = jx1 * 52f + tropEasterly;
                        float uy1 = jy1 * 52f + 0.12f * endWindN * itczConv;
                        upperWindHalf[p4] = FloatToHalfBits(ux0);
                        upperWindHalf[p4 + 1] = FloatToHalfBits(uy0);
                        upperWindHalf[p4 + 2] = FloatToHalfBits(ux1);
                        upperWindHalf[p4 + 3] = FloatToHalfBits(uy1);
                    }
                }
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
