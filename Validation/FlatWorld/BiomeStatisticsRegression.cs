using System;
using System.Linq;
using System.Threading;
using Orbis.Flat;

internal static class BiomeStatisticsRegression
{
    public static void Run()
    {
        WorldCalibration();
        AreaWeighting();
        EcotoneAndLikelihood();
        GeographicContinuity();
        HabitatAndSupport();
        ExchangeCalibration();
        GeneratedTerrain();
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Near(double value, double expected, double tolerance, string message)
    {
        Check(double.IsFinite(value) && Math.Abs(value - expected) <= tolerance,
            message + $" (expected {expected:G8}, found {value:G8})");
    }

    static WorldData Ocean()
    {
        var world = new WorldData(64, 32, "BIOME-REFERENCE", TerrainPreset.Continents);
        world.Facts = new FlatTerrainFacts(world.Count);
        for (int i = 0; i < world.Count; i++)
        {
            world.DrainOrder[i] = i;
            world.Elevation[i] = -1500;
            world.Water[i] = WaterKind.DeepOcean;
            world.Biomes[i] = Biome.Water;
            world.Climates[i] = Koppen.Cfb;
        }
        return world;
    }

    static void Cell(WorldData w, int tile, FlatBiomeClass type)
    {
        switch (type)
        {
            case FlatBiomeClass.TropicalRainforest:
                Climate(w, tile, 26, 2200, 4, .12, 150, Biome.Rainforest, Koppen.Af, VegetationCover.TropicalRainforest); break;
            case FlatBiomeClass.TropicalSavanna:
                Climate(w, tile, 25, 1100, 8, .90, 240, Biome.Grassland, Koppen.Aw, VegetationCover.Savanna); break;
            case FlatBiomeClass.TemperateForest:
                Climate(w, tile, 13, 1200, 15, .22, 400, Biome.Woodland, Koppen.Cfb, VegetationCover.DeciduousForest); break;
            case FlatBiomeClass.TemperateGrassland:
                Climate(w, tile, 13, 600, 15, .22, 400, Biome.Grassland, Koppen.Cfb, VegetationCover.Grassland); break;
            case FlatBiomeClass.Desert:
                Climate(w, tile, 23, 100, 18, .6, 500, Biome.Desert, Koppen.BWh, VegetationCover.Bare); break;
            case FlatBiomeClass.Tundra:
                Climate(w, tile, -6, 340, 22, .2, 220, Biome.Tundra, Koppen.ET, VegetationCover.Tundra); break;
            case FlatBiomeClass.BorealForest:
                Climate(w, tile, 1, 800, 30, .42, 640, Biome.Taiga, Koppen.Dfc, VegetationCover.BorealForest); break;
            case FlatBiomeClass.Alpine:
                Climate(w, tile, -2, 720, 20, .25, 3000, Biome.Tundra, Koppen.ET, VegetationCover.Tundra); break;
        }
    }

    static void Climate(WorldData w, int tile, double mean, double rain, double range,
                        double rainAmplitude, short height, Biome biome, Koppen climate, VegetationCover cover)
    {
        w.Water[tile] = WaterKind.Land;
        w.Elevation[tile] = w.WaterLevel[tile] = height;
        w.Biomes[tile] = biome;
        w.Climates[tile] = climate;
        w.Facts.Vegetation[tile] = cover;
        for (int month = 0; month < 12; month++)
        {
            double season = Math.Cos(2 * Math.PI * month / 12);
            w.Temperature[tile * 12 + month] = (float)(mean + range * season / 2);
            w.Precipitation[tile * 12 + month] = (float)(rain / 12 * (1 + rainAmplitude * season));
        }
    }

    static void WorldCalibration()
    {
        var world = Ocean();
        for (int x = 0; x < 64; x++)
        for (int y = 15; y <= 16; y++)
            Cell(world, world.Index(x, y), (FlatBiomeClass)(x / 8));
        byte[] saved = FlatSave.Encode(world);
        var facts = world.Facts;
        var covers = (VegetationCover[])facts.Vegetation.Clone();
        var model = FlatBiomeStatistics.Calibrate(world);
        var references = model.ExportReferences();
        Check(references.Length == 8, "All eight well-sampled terrain labels must produce models.");
        var work = new FlatBiomeScratch();
        for (int c = 0; c < 8; c++)
        {
            int tile = world.Index(c * 8 + 4, 16);
            var estimate = model.Estimate(world, tile, work);
            Check(estimate.Status == FlatBiomeStatus.Estimated && estimate.Best == (FlatBiomeClass)c,
                "The representative terrain sample should be supported for " + (FlatBiomeClass)c);
            Near(work.Probabilities.Sum(), 1, 1e-12, "Class probabilities must sum to one.");
            Check(references[c].Type == (FlatBiomeClass)c && references[c].SampleCount == 16 &&
                  references[c].EffectiveSampleSize > 15.9 && references[c].Source.Contains("BIOME-REFERENCE"),
                  "A world reference must identify its source and training support.");
            Check(references[c].Minimum[1] >= 0 && references[c].Minimum[3] >= 0 &&
                  references[c].Minimum[4] >= 0 && references[c].Maximum[4] <= 90,
                  "Reported applicability ranges must respect the physical domain of precipitation and latitude.");
        }
        Check(object.ReferenceEquals(facts, world.Facts) && covers.SequenceEqual(facts.Vegetation) &&
              saved.SequenceEqual(FlatSave.Encode(world)), "Analysis changed terrain facts or authoritative save bytes.");
        world.Day += 775;
        var later = FlatBiomeStatistics.Calibrate(world);
        var laterReferences = later.ExportReferences();
        for (int c = 0; c < 8; c++)
            Check(references[c].Covariance.SequenceEqual(laterReferences[c].Covariance) &&
                  references[c].Mean.SequenceEqual(laterReferences[c].Mean),
                "Biome calibration must consume long-term monthly normals independently of the weather day.");
        bool cancelled = false;
        try { FlatBiomeStatistics.Calibrate(world, new CancellationToken(true)); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "An obsolete world calibration must honor cancellation.");
    }

    static void AreaWeighting()
    {
        var world = Ocean();
        for (int x = 0; x < 24; x++)
        {
            Climate(world, world.Index(x, 0), 7, 650, 16, .2, 400, Biome.Grassland, Koppen.Cfb, VegetationCover.Grassland);
            Climate(world, world.Index(x, 15), 16, 650, 16, .2, 400, Biome.Grassland, Koppen.Cfb, VegetationCover.Grassland);
            Cell(world, world.Index(x, 31), FlatBiomeClass.Desert);
        }
        var references = FlatBiomeStatistics.Calibrate(world).ExportReferences();
        var grass = references.Single(r => r.Type == FlatBiomeClass.TemperateGrassland);
        var desert = references.Single(r => r.Type == FlatBiomeClass.Desert);
        double pole = Math.Cos(world.Latitude(world.Index(0, 0)) * Math.PI / 180);
        double equator = Math.Cos(world.Latitude(world.Index(0, 15)) * Math.PI / 180);
        Near(grass.Mean[0], (pole * 7 + equator * 16) / (pole + equator), 1e-5,
             "A high-latitude row must carry its spherical surface area in the class mean.");
        Near(desert.Prior, pole / (2 * pole + equator), 1e-10, "World class priors must be area fractions.");
        Check(grass.EffectiveSampleSize < grass.SampleCount, "Weighted support must retain the effective sample size.");
    }

    static FlatBiomeReference Reference(FlatBiomeClass type, double latitude, double rainMean = 900, double prior = 1)
    {
        double[] sd = { 3, 250, 4, .2, 24, 300 };
        var covariance = new double[36];
        for (int i = 0; i < sd.Length; i++) covariance[6 * i + i] = sd[i] * sd[i];
        return new FlatBiomeReference
        {
            Type = type, Source = "controlled seasonal normals", Prior = prior,
            Mean = new[] { 13.0, rainMean, 15.0, 0.0, latitude, 400.0 }, Covariance = covariance,
            Minimum = new[] { -100.0, 0, 0, 0, 0, -4000 },
            Maximum = new[] { 100.0, 120000, 200, 4, 90, 4000 }
        };
    }

    static FlatBiomeStatistics From(params FlatBiomeReference[] references)
    {
        Check(FlatBiomeStatistics.TryFromReferences(references, out var result), "Valid external calibration rejected.");
        return result;
    }

    static void EcotoneAndLikelihood()
    {
        var world = Ocean();
        int tile = world.Index(10, 16);
        double latitude = Math.Abs(world.Latitude(tile));
        var work = new FlatBiomeScratch();
        var forest = Reference(FlatBiomeClass.TemperateForest, latitude, 1200);
        var grass = Reference(FlatBiomeClass.TemperateGrassland, latitude, 600);
        var model = From(forest, grass);
        double previous = -1;
        for (int rain = 600; rain <= 1200; rain += 15)
        {
            Climate(world, tile, 13, rain, 15, 0, 400, Biome.Grassland, Koppen.Cfb, VegetationCover.Grassland);
            var estimate = model.Estimate(world, tile, work);
            double probability = work.Probabilities[(int)forest.Type];
            Check(probability > previous && (previous < 0 || probability - previous < .05),
                  "A smooth rainfall gradient should move continuously from grassland to forest.");
            if (rain == 900)
            {
                Near(estimate.Confidence, .5, 1e-8, "Equivalent density and prior should produce a mixed ecotone.");
                Check(estimate.Status == FlatBiomeStatus.Ambiguous && estimate.Best != estimate.RunnerUp,
                    "An ecotone needs two distinct candidates and an ambiguous status.");
            }
            if (rain == 600 || rain == 1200)
                Check(estimate.Status == FlatBiomeStatus.Estimated && estimate.Best == (rain == 600 ? grass.Type : forest.Type),
                      "Interior habitat should be more decisive than the middle of the gradient.");
            previous = probability;
        }
        forest = Reference(forest.Type, latitude, 900, 3);
        grass = Reference(grass.Type, latitude);
        Climate(world, tile, 13, 900, 15, 0, 400, Biome.Grassland, Koppen.Cfb, VegetationCover.Grassland);
        model = From(forest, grass);
        Near(model.Estimate(world, tile, work).Confidence, .75, 1e-8, "Equal Gaussian densities must preserve priors.");
        forest.Prior = 1;
        forest.Covariance[0] *= 4;
        model = From(forest, grass);
        Near(model.Estimate(world, tile, work).Confidence, 2.0 / 3, 1e-8, "Class density must include the log determinant.");
        Check(work.Probabilities[(int)grass.Type] > work.Probabilities[(int)forest.Type], "A wider distribution has lower peak density.");

        // Same annual temperature and rain, different amplitude of monthly rain.
        forest = Reference(forest.Type, latitude);
        grass = Reference(grass.Type, latitude);
        grass.Mean[3] = .8 / Math.Sqrt(2);
        model = From(forest, grass);
        Check(model.Estimate(world, tile, work).Best == forest.Type, "Even rainfall must match the even seasonal reference.");
        Climate(world, tile, 13, 900, 15, .8, 400, Biome.Grassland, Koppen.Cfb, VegetationCover.Grassland);
        Check(model.Estimate(world, tile, work).Best == grass.Type, "Rainfall seasonality must distinguish otherwise identical normals.");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int n = 0; n < 1024; n++) model.Estimate(world, tile, work);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Bulk biome queries must reuse caller scratch without allocations.");
    }

    static void GeographicContinuity()
    {
        foreach (var site in new[] { (x: 0, y: 16, direction: 0), (x: 14, y: 16, direction: 0), (x: 14, y: 31, direction: 3) })
        {
            var world = Ocean();
            int tile = world.Index(site.x, site.y), neighbor = world.SphereNeighbor(tile, site.direction);
            double latitude = Math.Abs(world.Latitude(tile));
            var model = From(Reference(FlatBiomeClass.TemperateForest, latitude), Reference(FlatBiomeClass.TemperateGrassland, latitude));
            Climate(world, tile, 13, 900, 15, 0, 400, Biome.Grassland, Koppen.Cfb, VegetationCover.Grassland);
            var work = new FlatBiomeScratch();
            model.Estimate(world, tile, work);
            Near(work.Probabilities[(int)FlatBiomeClass.TemperateForest], .5, 1e-8, "Open water cannot supply terrestrial votes.");
            Cell(world, neighbor, FlatBiomeClass.TemperateForest);
            model.Estimate(world, tile, work);
            double supported = work.Probabilities[(int)FlatBiomeClass.TemperateForest];
            Check(supported > .51 && supported < .65, "A real adjacent forest should weakly favor forest across either map seam.");
            world.Water[neighbor] = WaterKind.Lake;
            model.Estimate(world, tile, work);
            Near(work.Probabilities[(int)FlatBiomeClass.TemperateForest], .5, 1e-8, "A lake barrier should remove the spatial vote.");
        }
    }

    static void HabitatAndSupport()
    {
        var world = Ocean();
        int tile = world.Index(22, 16);
        Cell(world, tile, FlatBiomeClass.TemperateGrassland);
        var work = new FlatBiomeScratch();
        var sparse = FlatBiomeStatistics.Calibrate(world);
        Check(sparse.ExportReferences().Length == 0 && sparse.Estimate(world, tile, work).Status == FlatBiomeStatus.Unavailable,
              "Unsupported or absent classes need an explicit unavailable result.");
        var reference = Reference(FlatBiomeClass.TemperateForest, Math.Abs(world.Latitude(tile)));
        var model = From(reference);
        Check(model.Estimate(world, tile, work).Best == reference.Type, "The field test needs an initially supported terrestrial sample.");
        foreach (WaterKind water in new[] { WaterKind.ShallowOcean, WaterKind.MiddleOcean, WaterKind.DeepOcean, WaterKind.Lake, WaterKind.River })
        {
            world.Water[tile] = water;
            CheckProtected(model, world, tile, work);
        }
        world.Water[tile] = WaterKind.Land;
        foreach (Biome protectedBiome in new[] { Biome.Water, Biome.Beach, Biome.Wetland, Biome.Snow })
        {
            world.Biomes[tile] = protectedBiome;
            CheckProtected(model, world, tile, work);
        }
        world.Biomes[tile] = Biome.Grassland;
        world.Climates[tile] = Koppen.EF;
        CheckProtected(model, world, tile, work);
        world.Climates[tile] = Koppen.BWh; // Freshwater grass pocket in a regional dry climate.
        CheckProtected(model, world, tile, work);
        world.Climates[tile] = Koppen.Cfb;
        world.Facts.Vegetation[tile] = VegetationCover.Marsh;
        CheckProtected(model, world, tile, work);
        world.Facts.Vegetation[tile] = VegetationCover.Grassland;
        world.Temperature[tile * 12] = float.NaN;
        Check(model.Estimate(world, tile, work).Status == FlatBiomeStatus.Unavailable && work.Probabilities.Sum() == 0,
              "Invalid monthly data must clear previously computed support.");
        Cell(world, tile, FlatBiomeClass.TropicalRainforest);
        Check(model.Estimate(world, tile, work).Status == FlatBiomeStatus.OutsideReference && work.Probabilities.Sum() == 0,
              "A sole class must abstain beyond its multivariate support even when within broad per-axis bounds.");
        Cell(world, tile, FlatBiomeClass.Tundra);
        Check(model.Estimate(world, tile, work).Best == FlatBiomeClass.None, "Forest cannot be inferred for climatically frozen tundra.");
        Cell(world, tile, FlatBiomeClass.TemperateForest);
        reference.Type = FlatBiomeClass.Alpine;
        Check(From(reference).Estimate(world, tile, work).Best == FlatBiomeClass.None, "A lowland cannot be assigned an alpine class.");
        reference.Type = FlatBiomeClass.TemperateForest;
        reference.Maximum[1] = reference.Mean[1] + 1;
        Check(From(reference).Estimate(world, tile, work).Status == FlatBiomeStatus.OutsideReference,
              "An explicit external applicability bound must be enforced.");
        // A v1 saved regional desert label can coexist with a reconstructed, freshwater-fed grove.
        world.Biomes[tile] = Biome.Desert;
        world.Climates[tile] = Koppen.BWh;
        world.Facts.Vegetation[tile] = VegetationCover.DeciduousForest;
        CheckProtected(model, world, tile, work);
        Cell(world, tile, FlatBiomeClass.TemperateGrassland);
        world.Precipitation[tile * 12 + 7] = -1;
        Check(model.Estimate(world, tile, work).Status == FlatBiomeStatus.Unavailable, "A negative rain normal must be rejected.");
        Cell(world, tile, FlatBiomeClass.TemperateGrassland);
        world.Elevation[tile] = 7000;
        Check(model.Estimate(world, tile, work).Status == FlatBiomeStatus.OutsideReference,
              "Real high terrain is valid input but lies outside this lowland reference.");
        world.Elevation[tile] = HeightBands.MaximumBed + 1;
        Check(model.Estimate(world, tile, work).Status == FlatBiomeStatus.Unavailable, "Out-of-domain terrain input must be rejected.");
        world.Elevation[tile] = HeightBands.MinimumBed - 1;
        Check(model.Estimate(world, tile, work).Status == FlatBiomeStatus.Unavailable, "Invalid seabed depth must be rejected.");
    }

    static void CheckProtected(FlatBiomeStatistics model, WorldData world, int tile, FlatBiomeScratch work)
    {
        var e = model.Estimate(world, tile, work);
        Check(e.Status == FlatBiomeStatus.ProtectedHabitat && e.Best == FlatBiomeClass.None &&
              e.Confidence == 0 && work.Probabilities.Sum() == 0, "A protected habitat retained a terrestrial suggestion.");
    }

    static void ExchangeCalibration()
    {
        var world = Ocean();
        int tile = world.Index(12, 16);
        Cell(world, tile, FlatBiomeClass.TemperateForest);
        var reference = Reference(FlatBiomeClass.TemperateForest, Math.Abs(world.Latitude(tile)));
        var model = From(reference);
        var exported = model.ExportReferences();
        Check(exported[0].Schema == FlatBiomeStatistics.FeatureSchema && exported[0].Source == reference.Source,
              "Export must retain the feature order, units and provenance.");
        var roundtrip = From(exported);
        reference.Mean[0] = 95;
        exported[0].Maximum[0] = 0;
        var work = new FlatBiomeScratch();
        Check(model.Estimate(world, tile, work).Best == reference.Type && roundtrip.Estimate(world, tile, work).Best == reference.Type,
              "Import and export must not alias the caller's configuration.");
        foreach (Action<FlatBiomeReference> invalidate in new Action<FlatBiomeReference>[] {
            r => r.Prior = 0, r => r.Prior = double.NaN, r => r.Source = " ", r => r.Schema = "different units",
            r => r.Type = FlatBiomeClass.None, r => r.Mean[1] = double.PositiveInfinity, r => r.Covariance[7] = -1,
            r => r.Covariance[0] = 0, r => r.Covariance[1] = .4, r => r.Covariance[1] = double.NaN,
            r => r.Maximum[0] = r.Minimum[0] - 1, r => r.Minimum[0] = r.Mean[0] + 1,
            r => r.Maximum[1] = double.PositiveInfinity, r => r.Minimum = new double[1], r => r.EffectiveSampleSize = double.NaN })
        {
            var bad = model.ExportReferences()[0];
            invalidate(bad);
            Check(!FlatBiomeStatistics.TryFromReferences(new[] { bad }, out _), "Invalid external calibration accepted.");
        }
        var duplicate = model.ExportReferences()[0];
        Check(!FlatBiomeStatistics.TryFromReferences(new[] { duplicate, duplicate }, out _) &&
              !FlatBiomeStatistics.TryFromReferences(Array.Empty<FlatBiomeReference>(), out _), "Import requires distinct, nonempty class references.");
    }

    static void GeneratedTerrain()
    {
        var world = FlatGenerator.Generate(64, 32, "BIOME-CALIBRATION-FIELD", TerrainPreset.GreatContinent, default);
        byte[] saved = FlatSave.Encode(world);
        var model = FlatBiomeStatistics.Calibrate(world);
        var work = new FlatBiomeScratch();
        int supported = 0, protectedCount = 0;
        for (int i = 0; i < world.Count; i++)
        {
            var e = model.Estimate(world, i, work);
            if (e.Status == FlatBiomeStatus.Estimated || e.Status == FlatBiomeStatus.Ambiguous) supported++;
            if (e.Status == FlatBiomeStatus.ProtectedHabitat) protectedCount++;
        }
        Check(model.ExportReferences().Length >= 2 && supported > 25 && protectedCount > 25,
              "Generated terrain needs supported terrestrial samples as well as protected habitats.");
        Check(FlatSave.Encode(world).SequenceEqual(saved), "World calibration or bulk scoring changed a generated save.");
        Console.WriteLine($"Biome statistics on generated 64x32 terrain: {model.ExportReferences().Length} fitted classes, {supported} supported tiles, {protectedCount} protected tiles.");
    }
}
