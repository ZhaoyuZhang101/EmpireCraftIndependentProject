using System;
using System.IO;
using Orbis.Flat;

internal static class GlebaSupplementRegression
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("GlebaSupplementRegression failed: " + message);
    }

    public static void Run()
    {
        CheckRotationAndWorkers();
        CheckHemisphericMirrorSymmetry();
        CheckWideContinentDesertsVsMonsoon();
        CheckOceanGyresAndBoundaryCurrents();
        CheckHoldridgeBiomesAndGlaciers();
        CheckRealEarthDiagnostics();
        Console.WriteLine("PASS Gleba supplement package: rotation equivariance, hemispheric symmetry, STHZ desert/monsoon ovals, boundary currents, Holdridge ecology, and Earth 256 diagnostics.");
    }

    static void CheckRotationAndWorkers()
    {
        const int width = 128, height = 64, shift = 43;
        var original = FlatGenerator.Generate(width, height, "GLEBA-ROT-A", TerrainPreset.GreatContinent, default);
        var rotated = new WorldData(width, height, "GLEBA-ROT-B", TerrainPreset.Continents);
        for (int i = 0; i < original.Count; i++)
        {
            int j = rotated.Index(i % width + shift, i / width);
            rotated.Water[j] = original.Water[i];
            rotated.WaterLevel[j] = original.WaterLevel[i];
            rotated.Elevation[j] = original.Elevation[i];
            rotated.CoastDistance[j] = original.CoastDistance[i];
        }

        var supA = GlebaClimateSupplement.Build(original, default, workers: 1, maxColumns: width);
        var supB = GlebaClimateSupplement.Build(rotated, default, workers: 4, maxColumns: width);

        float maxPressureDiff = 0f, maxImpactDiff = 0f, maxContDiff = 0f, maxShadowDiff = 0f, maxWindDiff = 0f;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            int j = y * width + ((x + shift) % width);
            for (int m = 0; m < 12; m++)
            {
                maxPressureDiff = Math.Max(maxPressureDiff, Math.Abs(supA.PressureHpa[i * 12 + m] - supB.PressureHpa[j * 12 + m]));
                maxImpactDiff = Math.Max(maxImpactDiff, Math.Abs(supA.RainfallImpact[i * 12 + m] - supB.RainfallImpact[j * 12 + m]));
                maxContDiff = Math.Max(maxContDiff, Math.Abs(supA.WindContinentality[i * 12 + m] - supB.WindContinentality[j * 12 + m]));
                maxShadowDiff = Math.Max(maxShadowDiff, Math.Abs(supA.RainShadowiness[i * 12 + m] - supB.RainShadowiness[j * 12 + m]));
                maxWindDiff = Math.Max(maxWindDiff, Math.Abs(supA.WindEast[i * 12 + m] - supB.WindEast[j * 12 + m]));
                maxWindDiff = Math.Max(maxWindDiff, Math.Abs(supA.WindNorth[i * 12 + m] - supB.WindNorth[j * 12 + m]));
            }
        }
        Check(maxPressureDiff < 1e-3f && maxImpactDiff < 1e-4f && maxContDiff < 1e-4f && maxShadowDiff < 1e-4f && maxWindDiff < 1e-4f,
            $"Longitude rotation / worker drift: dP={maxPressureDiff}, dImp={maxImpactDiff}, dCont={maxContDiff}, dShadow={maxShadowDiff}, dWind={maxWindDiff}");
    }

    static void CheckHemisphericMirrorSymmetry()
    {
        const int width = 128, height = 64;
        var world = new WorldData(width, height, "GLEBA-MIRROR", TerrainPreset.Continents);
        for (int y = 0; y < height / 2; y++)
        for (int x = 0; x < width; x++)
        {
            bool land = x >= 24 && x < 88 && y >= 8 && y < 29;
            short elev = (short)(land ? (x >= 40 && x <= 44 ? 2600 : 180) : -1200);
            WaterKind wk = land ? WaterKind.Land : WaterKind.DeepOcean;
            int south = y * width + x;
            int north = (height - 1 - y) * width + x;
            world.Elevation[south] = world.Elevation[north] = elev;
            world.WaterLevel[south] = world.WaterLevel[north] = (short)(land ? elev : 0);
            world.Water[south] = world.Water[north] = wk;
        }

        var sup = GlebaClimateSupplement.Build(world, default, workers: 2, maxColumns: width);
        float maxP = 0, maxImp = 0, maxWE = 0, maxWN = 0, maxCE = 0, maxCN = 0, maxCont = 0, maxShad = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            int mirror = (height - 1 - y) * width + x;
            for (int m = 0; m < 12; m++)
            {
                int mMirror = (m + 6) % 12;
                int a = i * 12 + m, b = mirror * 12 + mMirror;
                maxP = Math.Max(maxP, Math.Abs(sup.PressureHpa[a] - sup.PressureHpa[b]) * 0.01f);
                maxImp = Math.Max(maxImp, Math.Abs(sup.RainfallImpact[a] - sup.RainfallImpact[b]));
                maxWE = Math.Max(maxWE, Math.Abs(sup.WindEast[a] - sup.WindEast[b]) * 0.1f);
                maxWN = Math.Max(maxWN, Math.Abs(sup.WindNorth[a] + sup.WindNorth[b]) * 0.1f);
                maxCE = Math.Max(maxCE, Math.Abs(sup.CurrentEast[a] - sup.CurrentEast[b]));
                maxCN = Math.Max(maxCN, Math.Abs(sup.CurrentNorth[a] + sup.CurrentNorth[b]));
                maxCont = Math.Max(maxCont, Math.Abs(sup.WindContinentality[a] - sup.WindContinentality[b]));
                maxShad = Math.Max(maxShad, Math.Abs(sup.RainShadowiness[a] - sup.RainShadowiness[b]));
            }
        }
        float maxSymError = Math.Max(Math.Max(Math.Max(maxP, maxImp), Math.Max(maxWE, maxWN)),
                                     Math.Max(Math.Max(maxCE, maxCN), Math.Max(maxCont, maxShad)));
        Check(maxSymError < 1e-4f, $"Hemispheric reflection + 6-month shift error {maxSymError} (P={maxP}, Imp={maxImp}, WE={maxWE}, WN={maxWN}, CE={maxCE}, CN={maxCN}, Cont={maxCont}, Shad={maxShad})");
    }

    static void CheckWideContinentDesertsVsMonsoon()
    {
        const int width = 128, height = 64;
        var world = new WorldData(width, height, "GLEBA-WIDE-CONT", TerrainPreset.Continents);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // Wide Afro-Eurasia-scale continent spanning 52 columns (~16,000 km at equator) east of wide ocean
            bool land = x >= 36 && x < 88;
            int i = y * width + x;
            world.Water[i] = land ? WaterKind.Land : WaterKind.DeepOcean;
            world.Elevation[i] = (short)(land ? 220 : -1500);
            world.WaterLevel[i] = (short)(land ? 220 : 0);
        }

        var sup = GlebaClimateSupplement.Build(world, default, workers: 2, maxColumns: width);
        int row30N = (int)((30.0 / 180.0 + 0.5) * height);
        int westDesert = (row30N * width + 42) * 12; // ~12% into continent (Sahara/Arabia analog)
        int eastMonsoon = (row30N * width + 75) * 12; // ~75% into continent (East Asia analog)

        // In July (m=6), western interior stays high pressure & negative rainfall impact, while eastern 75% drops to thermal low & positive rainfall impact!
        Check(sup.PressureHpa[westDesert + 6] > sup.PressureHpa[eastMonsoon + 6] + 15f,
            $"Expected summer western subtropical high > eastern thermal low: {sup.PressureHpa[westDesert + 6]:F1} vs {sup.PressureHpa[eastMonsoon + 6]:F1}");
        Check(sup.RainfallImpact[westDesert + 6] < -0.35f && sup.RainfallImpact[eastMonsoon + 6] > 0.15f,
            $"Expected summer western aridity < -0.35 and eastern monsoon boost > +0.15: {sup.RainfallImpact[westDesert + 6]:F2} vs {sup.RainfallImpact[eastMonsoon + 6]:F2}");
        // In January (m=0), eastern continent switches to winter high & negative rainfall impact
        Check(sup.PressureHpa[eastMonsoon + 0] > sup.PressureHpa[eastMonsoon + 6] + 20f && sup.RainfallImpact[eastMonsoon + 0] < -0.35f,
            $"Expected winter continental high & dry impact on eastern margin: {sup.PressureHpa[eastMonsoon + 0]:F1} hPa, impact {sup.RainfallImpact[eastMonsoon + 0]:F2}");
    }

    static void CheckOceanGyresAndBoundaryCurrents()
    {
        const int width = 128, height = 64;
        var world = new WorldData(width, height, "GLEBA-GYRES", TerrainPreset.Continents);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            bool ocean = x >= 32 && x < 96;
            int i = y * width + x;
            world.Water[i] = ocean ? WaterKind.DeepOcean : WaterKind.Land;
            world.Elevation[i] = (short)(ocean ? -2000 : 200);
            world.WaterLevel[i] = (short)(ocean ? 0 : 200);
        }

        var sup = GlebaClimateSupplement.Build(world, default, workers: 2, maxColumns: width);
        int row30N = (int)((30.0 / 180.0 + 0.5) * height);
        int westMargin = (row30N * width + 34) * 12 + 6; // Western ocean margin (Gulf Stream / Kuroshio)
        int eastMargin = (row30N * width + 93) * 12 + 6; // Eastern ocean margin (California / Canary)
        Check(sup.CurrentNorth[westMargin] > 0f && sup.CurrentNorth[eastMargin] < 0f,
            $"Expected poleward western boundary current and equatorward eastern boundary current at 30N: {sup.CurrentNorth[westMargin]:F4} vs {sup.CurrentNorth[eastMargin]:F4}");
    }

    static void CheckHoldridgeBiomesAndGlaciers()
    {
        var desert = GlebaEcologySupplement.EvaluateFlora(true, 0f, 300f, 25f, 24f, 40f, 0f, 14f);
        var desertBiome = GlebaEcologySupplement.ClassifyGlebaBiome(false, false, false, 0f, 300f, 14f, 34f, 2f, 5f, Koppen.BWh, in desert);
        Check(desert.DeadLand > 0.90f && desertBiome == GlebaBiomeKind.DesertsAndXericShrublands, "Hot hyper-arid tile must classify as desert");

        var rainforest = GlebaEcologySupplement.EvaluateFlora(true, 0f, 120f, 3f, 26f, 2400f, 140f, 25f);
        var rfBiome = GlebaEcologySupplement.ClassifyGlebaBiome(false, false, false, 0f, 120f, 25.5f, 26.5f, 180f, 220f, Koppen.Af, in rainforest);
        Check(rainforest.Trees > 0.65f && rfBiome == GlebaBiomeKind.WetJungle, "Equatorial wet forest must classify as WetJungle");

        var savanna = GlebaEcologySupplement.EvaluateFlora(true, 0f, 300f, 14f, 25f, 950f, 2f, 21f);
        Check(savanna.Trees < rainforest.Trees - 0.25f && savanna.Grass + savanna.Shrubs > 0.50f, "Tropical dry-season drought transfer must shift trees into savanna grass/shrubs");

        var taiga = GlebaEcologySupplement.EvaluateFlora(true, 0f, 200f, 61f, 5.5f, 550f, 20f, -22f);
        var taigaBiome = GlebaEcologySupplement.ClassifyGlebaBiome(false, false, false, 0f, 200f, -22f, 16f, 22f, 75f, Koppen.Dfc, in taiga);
        Check(taiga.ConiferFraction > 0.65f && taigaBiome == GlebaBiomeKind.Taiga, "Boreal Dfc forest must be conifer-dominated Taiga");

        float seaIce = GlebaEcologySupplement.ComputeSeaIceMeters(true, -12f);
        float glacier = GlebaEcologySupplement.ComputeGroundedIceMeters(true, 1800f, -8f, 45f);
        Check(seaIce > 10f && glacier > 200f, "Polar sea ice and subfreezing highland glacier must have positive thickness");
    }

    static void CheckRealEarthDiagnostics()
    {
        const string assets = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        var earth = FlatGenerator.Generate(256, 128, "ORBIS-2D-2026", TerrainPreset.Earth, default,
            File.ReadAllBytes(assets + "Earth256.bytes"),
            File.ReadAllBytes(assets + "EarthWater256.bytes"),
            File.ReadAllBytes(assets + "EarthRivers.bytes"));
        var sup = GlebaClimateSupplement.Build(earth, default, workers: 4, maxColumns: 128);

        // Sahara (25N, 10E) vs South China (24N, 113E) vs Deccan Leeward (16N, 78E) in July (month 6)
        float saharaImpact = sup.SampleMonthly(sup.RainfallImpact, (10f + 180f) / 360f, (25f + 90f) / 180f, 6);
        float southChinaImpact = sup.SampleMonthly(sup.RainfallImpact, (113f + 180f) / 360f, (24f + 90f) / 180f, 6);
        float tibetShadow = sup.SampleMonthly(sup.RainShadowiness, (84f + 180f) / 360f, (39f + 90f) / 180f, 6);
        Console.WriteLine($"Earth 256 Gleba diagnostics (July): Sahara impact={saharaImpact:F2}, South China impact={southChinaImpact:F2}, Tarim rainShadow={tibetShadow:F2}");
        Check(saharaImpact < southChinaImpact - 0.3f && tibetShadow > 0.05f, "Earth Sahara vs South China SLP impact or Tarim rain shadow missing");
    }
}
