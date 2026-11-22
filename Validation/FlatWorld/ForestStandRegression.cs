using System;
using System.IO;
using Orbis.Flat;

public static class ForestStandRegression
{
    static int assertions;

    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    static WorldData TemperateForest(bool legacy)
    {
        var world = new WorldData(128, 64, "FOREST-STAND-REGRESSION", TerrainPreset.Continents);
        for (int tile = 0; tile < world.Count; tile++)
        {
            world.Elevation[tile] = world.WaterLevel[tile] = 400;
            world.DrainOrder[tile] = tile;
            world.Climates[tile] = Koppen.Cfb;
            world.Biomes[tile] = legacy ? Biome.Grassland : Biome.Woodland;
            for (int month = 0; month < 12; month++)
            {
                world.Temperature[tile * 12 + month] = 13 - 7 * (float)Math.Cos(month * Math.PI * 2 / 12);
                world.Precipitation[tile * 12 + month] = 140;
            }
        }
        FlatTerrainAnalysis.Build(world, default, false, 1);
        return world;
    }

    static bool Forest(VegetationCover cover)
    { return cover >= VegetationCover.SeasonalForest && cover <= VegetationCover.TropicalRainforest; }

    static void TropicalDrySeason()
    {
        var world = new WorldData(64, 32, "TROPICAL-DRY-SEASON", TerrainPreset.Continents);
        for (int tile = 0; tile < world.Count; tile++)
        {
            world.Elevation[tile] = world.WaterLevel[tile] = 100;
            world.DrainOrder[tile] = tile;
            int wetMonths = tile % world.Width < world.Width / 2 ? 7 : 4;
            float wetRain = (1600 - 5 * (12 - wetMonths)) / (float)wetMonths;
            for (int month = 0; month < 12; month++)
            {
                int index = tile * 12 + month;
                world.Temperature[index] = 26;
                world.Precipitation[index] = month < wetMonths ? wetRain : 5;
            }
            world.Climates[tile] = KoppenClassifier.Classify(world.Temperature, world.Precipitation, tile * 12);
        }

        FlatTerrainAnalysis.Build(world, default, true, 1);
        int woodland = world.Index(1, 10), savanna = world.Index(world.Width / 2 + 1, 10);
        Check(world.Climates[woodland] == Koppen.Aw && world.Climates[savanna] == Koppen.Aw,
              "Both 1600 mm tropical climates have a dry season.");
        Check(world.Biomes[woodland] == Biome.Woodland && world.Facts.Vegetation[woodland] == VegetationCover.SeasonalForest,
              "Seven months of root-zone recharge support tropical seasonal woodland.");
        Check(world.Biomes[savanna] == Biome.Grassland && world.Facts.Vegetation[savanna] == VegetationCover.Savanna,
              "Four wet months and a long dry season retain savanna.");
        var references = FlatBiomeStatistics.Calibrate(world).ExportReferences();
        Check(references.Length == 1 && references[0].Type == FlatBiomeClass.TropicalSavanna,
              "Dry tropical woodland must not supply a temperate or rainforest training label.");

        world.Biomes[woodland] = Biome.Grassland;
        FlatTerrainAnalysis.Build(world, default, false, 1);
        Check(world.Biomes[woodland] == Biome.Grassland && world.Facts.Vegetation[woodland] == VegetationCover.SeasonalForest,
              "The saved biome and the reconstructed seasonal cover retain their respective authorities.");
    }

    static void TropicalHillsides()
    {
        var world = new WorldData(256, 128, "TROPICAL-HILLSIDES", TerrainPreset.Continents);
        var air = world.Moisture = new FlatMoistureState(128, 64);
        for (int tile = 0; tile < air.Width * air.Height; tile++)
        {
            float humidity = tile % air.Width < 96 ? .65f : .05f;
            for (int month = 0; month < 12; month++) air.Humidity[tile * 12 + month] = humidity;
        }
        for (int tile = 0; tile < world.Count; tile++)
        {
            int x = tile % world.Width;
            // A mountain stands at x=41. At x=97 the nearest hill rises only 500 m above the coast.
            short height = x switch { 41 => 3500, 96 => -3500, 98 => 600, _ => 100 };
            world.Elevation[tile] = height;
            world.WaterLevel[tile] = height < 0 ? (short)0 : height;
            world.Water[tile] = height < 0 ? WaterKind.DeepOcean : WaterKind.Land;
            world.DrainOrder[tile] = tile;
            int wetMonths = x < world.Width / 2 ? 5 : 6;
            for (int month = 0; month < 12; month++)
            {
                int index = tile * 12 + month;
                world.Temperature[index] = 26;
                world.Precipitation[index] = month < wetMonths ? (1600f - 5 * (12 - wetMonths)) / wetMonths : 5;
            }
            world.Climates[tile] = KoppenClassifier.Classify(world.Temperature, world.Precipitation, tile * 12);
        }

        FlatTerrainAnalysis.Build(world, default, true, 1);
        var cover = world.Facts.Vegetation;
        int slope = world.Index(40, 64), plain = world.Index(85, 64), coast = world.Index(97, 64);
        int humid = world.Index(165, 64), dry = world.Index(225, 64);
        Check(world.Facts.SaturatedMonths[slope] == 5 && world.Facts.SaturatedMonths[plain] == 5 &&
              world.Facts.SaturatedMonths[humid] == 6 && world.Facts.SaturatedMonths[dry] == 6,
              "The 1600-mm tropical sites differ in recharge season while sharing annual rainfall.");
        Check(cover[slope] == VegetationCover.SeasonalForest && cover[plain] == VegetationCover.Savanna,
              "With five wet months and surplus rain, a sheltered hillside supports forest beside an open savanna.");
        Check(cover[coast] == VegetationCover.Savanna, "Deep offshore bathymetry must not supply onshore shelter.");
        Check(cover[humid] == VegetationCover.SeasonalForest && cover[dry] == VegetationCover.Savanna,
              "During six-month recharge cycles, dry roots coinciding with dry air can retain an open savanna.");
    }

    // CPU measurement of the production shader's one-root-per-tile admission.
    // Its sine hash has platform-dependent final bits, so only regional statistics are asserted.
    static float ShaderHash(float x, float y, int width)
    {
        x -= MathF.Floor(x / width) * width;
        float value = MathF.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
        return value - MathF.Floor(value);
    }

    static float Bilinear(WorldData world, float[] field, float x, float y)
    {
        x -= .5f;
        y -= .5f;
        int left = (int)MathF.Floor(x), bottom = (int)MathF.Floor(y);
        float u = x - left, v = y - bottom;
        int south = FMath.Clamp(bottom, 0, world.Height - 1);
        int north = FMath.Clamp(bottom + 1, 0, world.Height - 1);
        return FMath.Lerp(FMath.Lerp(field[world.Index(left, south)], field[world.Index(left + 1, south)], u),
            FMath.Lerp(field[world.Index(left, north)], field[world.Index(left + 1, north)], u), v);
    }

    static float CoverDensity(VegetationCover cover)
    {
        if (cover == VegetationCover.Bare || cover == VegetationCover.Ice) return 0;
        if (cover <= VegetationCover.DrySteppe) return cover == VegetationCover.Grassland ? .16f : .08f;
        if (cover == VegetationCover.Savanna) return .55f;
        if (cover == VegetationCover.Shrubland) return .72f;
        if (cover <= VegetationCover.TropicalRainforest) return 1;
        return cover == VegetationCover.Marsh ? 1 : .15f;
    }

    static bool Admitted(WorldData world, int tile)
    {
        int x = tile % world.Width, y = tile / world.Width;
        float rootSeed = ShaderHash(x, y, world.Width);
        float rootX = (MathF.Floor(16 + (rootSeed - .5f) * 16) + .5f) / 32;
        float rootY = (MathF.Floor(13 + (ShaderHash(x + 71, y + 71, world.Width) - .5f) * 14) + .5f) / 32;
        if (world.Buildings[tile] != Structure.None || FlatTerrainAnalysis.AreaWater(world.Water[tile])) return false;
        float canopy = Bilinear(world, world.Facts.Canopy, x + rootX, y + rootY);
        float density = FMath.Clamp((canopy - .20f) * 1.25f, 0, 1) * CoverDensity(world.Facts.Vegetation[tile]);
        float seed = (FMath.Hash((uint)tile ^ FMath.Seed(world.Seed)) & 255) / 255f;
        float occupancy = ShaderHash(x + 39.17f + seed * 17, y + 87.23f, world.Width);
        return density > 0 && occupancy <= density &&
            Bilinear(world, world.Facts.ShoreDistance, x + rootX, y + rootY) <= -.18f &&
            FlatWaterGeometry.RiverDistance(world, tile, rootX, rootY) >= .15f;
    }

    sealed class Metrics
    {
        public int Land, LandRoots, ForestTiles, ForestRoots, ForestEdges, SameSpeciesEdges, RootPairs;
        public int WetForestTiles, WetForestRoots, ForestBlocks, DenseBlocks, EmptyEdges, EmptyPairs;
        public double Canopy;
        public double SpeciesAgreement => ForestEdges == 0 ? 0 : SameSpeciesEdges / (double)ForestEdges;
    }

    static Metrics Measure(WorldData world, string name)
    {
        var metrics = new Metrics();
        var admitted = new bool[world.Count];
        for (int tile = 0; tile < world.Count; tile++) admitted[tile] = Admitted(world, tile);
        for (int tile = 0; tile < world.Count; tile++)
        {
            if (FlatTerrainAnalysis.AreaWater(world.Water[tile])) continue;
            metrics.Land++;
            if (admitted[tile]) metrics.LandRoots++;
            if (!Forest(world.Facts.Vegetation[tile])) continue;
            metrics.ForestTiles++;
            metrics.Canopy += world.Facts.Canopy[tile];
            if (admitted[tile]) metrics.ForestRoots++;
            if (world.Facts.Wetness[tile] > .65f && world.Facts.Gradient[tile] < 200)
            {
                metrics.WetForestTiles++;
                if (admitted[tile]) metrics.WetForestRoots++;
            }
            foreach (int direction in new[] { 1, 3 })
            {
                int next = world.Neighbor(tile, direction);
                if (next < 0 || !Forest(world.Facts.Vegetation[next]) || world.Climates[next] != world.Climates[tile]) continue;
                metrics.ForestEdges++;
                if (world.Facts.Trees[tile] == world.Facts.Trees[next]) metrics.SameSpeciesEdges++;
                if (admitted[tile] && admitted[next]) metrics.RootPairs++;
                if (!admitted[tile])
                {
                    metrics.EmptyEdges++;
                    if (!admitted[next]) metrics.EmptyPairs++;
                }
            }
        }
        for (int y = 0; y < world.Height - 3; y += 4)
        for (int x = 0; x < world.Width; x += 4)
        {
            bool forest = true, dense = true;
            for (int dy = 0; dy < 4; dy++)
            for (int dx = 0; dx < 4; dx++)
            {
                int tile = world.Index(x + dx, y + dy);
                forest &= Forest(world.Facts.Vegetation[tile]) && !FlatTerrainAnalysis.AreaWater(world.Water[tile]);
                dense &= admitted[tile];
            }
            if (!forest) continue;
            metrics.ForestBlocks++;
            if (dense) metrics.DenseBlocks++;
        }
        Console.WriteLine(name + " land=" + metrics.Land + " forest=" + metrics.ForestTiles +
            " landRoots=" + metrics.LandRoots +
            " meanCanopy=" + (metrics.Canopy / Math.Max(1, metrics.ForestTiles)).ToString("F4") +
            " rootAdmission=" + (metrics.ForestRoots / (double)Math.Max(1, metrics.ForestTiles)).ToString("P2") +
            " neighbouringSpeciesAgreement=" + metrics.SpeciesAgreement.ToString("P2") +
            " neighbouringOccupiedPair=" + (metrics.RootPairs / (double)Math.Max(1, metrics.ForestEdges)).ToString("P2") +
            " wetGentleRootAdmission=" + (metrics.WetForestRoots / (double)Math.Max(1, metrics.WetForestTiles)).ToString("P2") +
            " complete4x4Blocks=" + metrics.DenseBlocks + "/" + metrics.ForestBlocks +
            "(" + (metrics.DenseBlocks / (double)Math.Max(1, metrics.ForestBlocks)).ToString("P2") + ")" +
            " emptyNeighbourGivenClearing=" + (metrics.EmptyPairs / (double)Math.Max(1, metrics.EmptyEdges)).ToString("P2"));
        return metrics;
    }

    static void PersistenceAndWorkers(WorldData world)
    {
        byte[] before = FlatSave.Encode(world);
        var clone = world.Clone();
        FlatTerrainAnalysis.Build(clone, default, false, 4);
        var loaded = FlatSave.Decode(before);
        for (int tile = 0; tile < world.Count; tile++)
        {
            Check(clone.Facts.Trees[tile] == world.Facts.Trees[tile] &&
                clone.Facts.Canopy[tile] == world.Facts.Canopy[tile], "Forest stands must be independent of worker count.");
            Check(loaded.Biomes[tile] == Biome.Grassland && loaded.Facts.Vegetation[tile] == VegetationCover.TemperateRainforest,
                "Legacy grassland labels remain saved while forest habitat reconstructs from climate.");
            Check(loaded.Facts.Trees[tile] == world.Facts.Trees[tile] &&
                loaded.Facts.Canopy[tile] == world.Facts.Canopy[tile], "Saved climate must reconstruct identical forest stands.");
        }
        byte[] after = FlatSave.Encode(world);
        Check(Convert.ToBase64String(before) == Convert.ToBase64String(after), "Derived forest reconstruction preserves the authoritative save payload.");
        clone.Facts.Canopy[0] = -1;
        Check(world.Facts.Canopy[0] >= 0, "Cloned stand data has independent storage.");
    }

    static void ClimateAndTerrainLimits(WorldData wet)
    {
        foreach (Koppen climate in new[] { Koppen.BWh, Koppen.ET, Koppen.EF })
        {
            var world = wet.Clone();
            for (int tile = 0; tile < world.Count; tile++)
            {
                world.Climates[tile] = climate;
                for (int month = 0; month < 12; month++)
                {
                    world.Temperature[tile * 12 + month] = climate == Koppen.BWh ? 26 : climate == Koppen.ET ? 4 : -15;
                    world.Precipitation[tile * 12 + month] = climate == Koppen.BWh ? 5 : 80;
                }
            }
            FlatTerrainAnalysis.Build(world, default, false, 2);
            for (int tile = 0; tile < world.Count; tile++)
                Check(!Admitted(world, tile), climate + " excludes tall forest despite a legacy wooded label.");
        }
        var steep = wet.Clone();
        for (int tile = 0; tile < steep.Count; tile++)
            steep.Elevation[tile] = steep.WaterLevel[tile] = (short)(tile % 2 == 0 ? 400 : 1200);
        FlatTerrainAnalysis.Build(steep, default, false, 2);
        int steepRoots = 0, wetRoots = 0;
        for (int tile = 0; tile < wet.Count; tile++)
        {
            if (Admitted(steep, tile)) steepRoots++;
            if (Admitted(wet, tile)) wetRoots++;
        }
        Check(steepRoots < wetRoots * .5, "Steep relief reduces woodland density within the same wet climate.");
        Console.WriteLine("slope admission flat=" + wetRoots + " steep=" + steepRoots);
    }

    static void RiparianCorridor()
    {
        var world = TemperateForest(false);
        int riverX = world.Width / 2;
        for (int tile = 0; tile < world.Count; tile++)
        {
            world.Elevation[tile] = world.WaterLevel[tile] = 100;
            world.Biomes[tile] = Biome.Desert;
            world.Climates[tile] = Koppen.BWh;
            if (tile % world.Width == riverX)
            {
                world.Water[tile] = WaterKind.River;
                world.Runoff[tile] = 1000;
            }
            for (int month = 0; month < 12; month++)
            {
                world.Temperature[tile * 12 + month] = 25;
                world.Precipitation[tile * 12 + month] = 0;
            }
        }
        FlatTerrainAnalysis.Build(world, default, false, 2);
        int bankRoots = 0;
        for (int y = 0; y < world.Height; y++)
        {
            if (Admitted(world, world.Index(riverX - 1, y))) bankRoots++;
            if (Admitted(world, world.Index(riverX + 1, y))) bankRoots++;
            Check(!Admitted(world, world.Index(riverX - 5, y)), "Dry desert beyond the freshwater corridor remains treeless.");
        }
        Check(bankRoots > world.Height * 2 * .45, "Perennial groundwater supports a wooded river corridor within the desert.");
        FlatTerrainAnalysis.Build(world, default, true, 2);
        int rebuiltBankRoots = 0;
        for (int y = 0; y < world.Height; y++)
        {
            if (Admitted(world, world.Index(riverX - 1, y))) rebuiltBankRoots++;
            if (Admitted(world, world.Index(riverX + 1, y))) rebuiltBankRoots++;
        }
        Check(rebuiltBankRoots == bankRoots, "New terrain and retained legacy labels produce the same freshwater woodland.");
        Console.WriteLine("desert riverbank admission=" + (bankRoots / (world.Height * 2.0)).ToString("P2"));
    }

    static void ContinentalSeasonality()
    {
        var world = TemperateForest(false);
        for (int tile = 0; tile < world.Count; tile++)
        {
            world.Climates[tile] = Koppen.Dfa;
            for (int month = 0; month < 12; month++)
                world.Temperature[tile * 12 + month] = 10 - 17 * (float)Math.Cos(month * Math.PI * 2 / 12);
        }
        FlatTerrainAnalysis.Build(world, default, false, 2);
        int broadleaf = -1;
        for (int tile = 0; tile < world.Count; tile++)
            if (world.Facts.Trees[tile] == TreeSpecies.Broadleaf && world.Facts.Canopy[tile] > .5f) broadleaf = tile;
        Check(broadleaf >= 0, "Humid continental woodland retains a deciduous broadleaf stand.");
        FlatEcology.SampleSeason(world, broadleaf, 0, out _, out float winterLeaf);
        FlatEcology.SampleSeason(world, broadleaf, 180, out _, out float summerLeaf);
        Check(winterLeaf < .2f && summerLeaf > .8f,
            "High annual rain must not turn cold-winter continental broadleaf stands evergreen.");
    }

    static void WorldCoordinatesAndSeeds(WorldData world)
    {
        uint seed = FMath.Seed(world.Seed) ^ 0x5d2a9b71u;
        for (int y = 0; y < world.Height; y++)
        {
            float left = FlatEcology.StandVariation(world, -1, y, seed, out float leftClearing);
            float right = FlatEcology.StandVariation(world, world.Width - 1, y, seed, out float rightClearing);
            Check(left == right && leftClearing == rightClearing, "Species and clearing fields repeat exactly across the date line.");
        }
        for (int x = 0; x < world.Width; x++)
        {
            float crossing = FlatEcology.StandVariation(world, x, -1, seed, out float crossingClearing);
            float folded = FlatEcology.StandVariation(world, x + world.Width / 2, 0, seed, out float foldedClearing);
            Check(Math.Abs(crossing - folded) < .00001f && Math.Abs(crossingClearing - foldedClearing) < .00001f,
                "Forest stand fields share the spherical fold at the pole.");
        }
        var differentSeed = world.Clone();
        differentSeed.Seed = "FOREST-STAND-SECOND-SEED";
        FlatTerrainAnalysis.Build(differentSeed, default, false, 2);
        int changed = 0;
        for (int tile = 0; tile < world.Count; tile++)
        {
            var tree = differentSeed.Facts.Trees[tile];
            Check(tree == TreeSpecies.Pine || tree == TreeSpecies.Broadleaf, "World seed varies species only inside this humid temperate habitat.");
            if (tree != world.Facts.Trees[tile]) changed++;
        }
        Check(changed > world.Count * .15, "A different world seed produces different stands within the same climate.");
    }

    static void Representative(WorldData world, string name, VegetationCover firstCover, VegetationCover lastCover)
    {
        int best = -1, bestRoots = 0, bestSame = 0;
        double bestScore = double.NegativeInfinity;
        for (int tile = 0; tile < world.Count; tile++)
        {
            int x = tile % world.Width, y = tile / world.Width;
            var cover = world.Facts.Vegetation[tile];
            if (cover < firstCover || cover > lastCover || y < 3 || y >= world.Height - 3 ||
                world.Water[tile] != WaterKind.Land || world.Facts.Canopy[tile] < .65f) continue;
            int same = 0, roots = 0;
            double canopy = 0;
            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int next = world.Index(x + dx, y + dy);
                if (world.Facts.Trees[next] == world.Facts.Trees[tile] &&
                    world.Facts.Vegetation[next] >= firstCover && world.Facts.Vegetation[next] <= lastCover) same++;
                if (Admitted(world, next)) roots++;
                canopy += world.Facts.Canopy[next];
            }
            double score = same * 3 + roots * 2 + canopy;
            if (score <= bestScore) continue;
            bestScore = score; best = tile; bestRoots = roots; bestSame = same;
        }
        if (best < 0) return;
        int bx = best % world.Width, by = best / world.Width;
        Console.WriteLine("representative " + name + " tile=" + best + " x=" + bx + " y=" + by +
            " lon=" + (((bx + .5) / world.Width - .5) * 360).ToString("F3") +
            " lat=" + (((by + .5) / world.Height - .5) * 180).ToString("F3") +
            " climate=" + world.Climates[best] + " cover=" + world.Facts.Vegetation[best] +
            " tree=" + world.Facts.Trees[best] + " elevation=" + world.Elevation[best] +
            " 7x7Roots=" + bestRoots + "/49 sameSpecies=" + bestSame + "/49");
    }

    public static void Run()
    {
        assertions = 0;
        TropicalDrySeason();
        TropicalHillsides();
        var uniform = TemperateForest(false);
        var uniformMetrics = Measure(uniform, "uniform Cfb forest");
        var legacy = TemperateForest(true);
        var legacyMetrics = Measure(legacy, "legacy grassland in wet Cfb");
        var atlas = FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"),
            "ORBIS-2D-2026", default);
        Measure(atlas, "default atlas");
        Representative(atlas, "temperate", VegetationCover.DeciduousForest, VegetationCover.TemperateRainforest);
        Representative(atlas, "boreal", VegetationCover.BorealForest, VegetationCover.BorealForest);
        Representative(atlas, "tropical", VegetationCover.TropicalRainforest, VegetationCover.TropicalRainforest);
        Representative(atlas, "riverbank", VegetationCover.Marsh, VegetationCover.Marsh);
        Check(legacyMetrics.ForestTiles == legacy.Count, "Wet forest climate must reconstruct forest from a legacy grassland label.");
        Check(uniformMetrics.SpeciesAgreement > .88, "Suitable forest must form coherent stands instead of independently choosing every tile's species.");
        Check(uniformMetrics.DenseBlocks > uniformMetrics.ForestBlocks * .50,
            "Wet forest must contain complete multi-tile canopy stands.");
        Check(uniformMetrics.EmptyPairs > uniformMetrics.EmptyEdges * .30,
            "Forest gaps must form clearings instead of independent missing trees.");
        PersistenceAndWorkers(legacy);
        ClimateAndTerrainLimits(uniform);
        RiparianCorridor();
        ContinentalSeasonality();
        WorldCoordinatesAndSeeds(uniform);
        Console.WriteLine("PASS forest stand checks=" + assertions);
    }
}
