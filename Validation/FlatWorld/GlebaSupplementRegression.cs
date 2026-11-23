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
        CheckRossbyJetsAndMapmodes();
        CheckRealEarthDiagnostics();
        CheckProceduralGeographyCarvingAndErosion();
        CheckProceduralPlanetGeneration();
        Console.WriteLine("PASS Gleba supplement package: circulation, Rossby jets, ocean currents/upwelling, Holdridge ecology, saltwater/freshwater lakes, coral reefs, fjords, aquifers, hierarchy, procedural planet generation, and Earth 256 diagnostics.");
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

        var open = new GlebaPlantCover { Trees = .23f, Grass = .61f, Shrubs = .16f, TotalPlants = .87f };
        GlebaBiomeKind OpenGround(Koppen climate, float height = 80, bool marsh = false) =>
            GlebaEcologySupplement.ClassifyGlebaBiome(false, false, false, 0, height, 21, 25, 65, 70, climate, in open, marsh);
        const GlebaBiomeKind tropicalSavanna = GlebaBiomeKind.TropicalGrasslandsSavannasAndShrublands;
        Check(OpenGround(Koppen.BSh) != tropicalSavanna, "A hot semi-arid steppe must not be reported as tropical savanna");
        Check(OpenGround(Koppen.Af) != tropicalSavanna && OpenGround(Koppen.Am) != tropicalSavanna,
            "Open wet tropical sites need a different biome from seasonally dry savanna");
        Check(OpenGround(Koppen.Aw) == tropicalSavanna, "Open tropical wet-dry sites must retain savanna");
        Check(OpenGround(Koppen.Csa) == GlebaBiomeKind.MediterraneanForestWoodlandAndScrub &&
              OpenGround(Koppen.Csb) == GlebaBiomeKind.MediterraneanForestWoodlandAndScrub &&
              OpenGround(Koppen.Csc) == GlebaBiomeKind.MediterraneanForestWoodlandAndScrub,
            "Use the twelve-month dry-summer classification even when January and July are both warm");
        Check(OpenGround(Koppen.Aw, 3000) == GlebaBiomeKind.MontaneGrasslandsAndShrublands &&
              OpenGround(Koppen.Aw, marsh: true) == GlebaBiomeKind.Marsh,
            "Montane open ground and seasonally saturated marsh take precedence over savanna");

        var taiga = GlebaEcologySupplement.EvaluateFlora(true, 0f, 200f, 61f, 5.5f, 550f, 20f, -22f);
        var taigaBiome = GlebaEcologySupplement.ClassifyGlebaBiome(false, false, false, 0f, 200f, -22f, 16f, 22f, 75f, Koppen.Dfc, in taiga);
        Check(taiga.ConiferFraction > 0.65f && taigaBiome == GlebaBiomeKind.Taiga, "Boreal Dfc forest must be conifer-dominated Taiga");

        float seaIce = GlebaEcologySupplement.ComputeSeaIceMeters(true, -12f);
        float glacier = GlebaEcologySupplement.ComputeGroundedIceMeters(true, 1800f, -8f, 45f);
        Check(seaIce > 10f && glacier > 200f, "Polar sea ice and subfreezing highland glacier must have positive thickness");

        // Holdridge latitude/warmth-dependent alpine treeline canopy taper
        float tempLowCanopy = FlatEcology.ContinuousCanopy(Biome.Woodland, 13f, 1200f, 0.75f, 80f, 20f, biotemperature: 13f, elevation: 400f);
        float tempHighCanopy = FlatEcology.ContinuousCanopy(Biome.Woodland, 13f, 1200f, 0.75f, 80f, 20f, biotemperature: 13f, elevation: 2900f);
        float tropHighMult = GlebaEcologySupplement.ComputeTreelineMultiplier(24f, 2900f);
        Check(tempHighCanopy < 0.45f * tempLowCanopy && Math.Abs(tropHighMult - 1f) < 1e-5f,
            $"Holdridge treeline must suppress temperate 2900m canopy ({tempHighCanopy:F2} vs {tempLowCanopy:F2}) while sparing 2900m equatorial cloud forest ({tropHighMult:F2})");

        // 6-stage soil pipeline (glacial loess silt/clay enrichment vs desert aeolian sand deflation)
        var loessSoil = GlebaEcologySupplement.EvaluateLocalSoil(true, false, 180f, 40f, 650f, 6f, 0.85f);
        var ergSoil = GlebaEcologySupplement.EvaluateLocalSoil(true, false, 350f, 30f, 35f, 34f, 0.05f);
        Check(loessSoil.Clay + loessSoil.Silt > 0.80f && loessSoil.Depth > 4f,
            $"LGM loess lowland must be deep silt/clay rich: clay+silt={loessSoil.Clay + loessSoil.Silt:F2}, depth={loessSoil.Depth:F2}");
        Check(ergSoil.Sand > 0.75f, $"Hyper-arid lowland desert must deflate fines into sandy erg: sand={ergSoil.Sand:F2}");
    }

    static int TileAtLonLat(WorldData world, float lonDeg, float latDeg)
    {
        int x = FMath.Clamp((int)((lonDeg + 180f) / 360f * world.Width), 0, world.Width - 1);
        int y = FMath.Clamp((int)((latDeg + 90f) / 180f * world.Height), 0, world.Height - 1);
        return y * world.Width + x;
    }

    static void CheckRealEarthDiagnostics()
    {
        const string assets = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        var earth = FlatGenerator.Generate(256, 128, "ORBIS-2D-2026", TerrainPreset.Earth, default,
            File.ReadAllBytes(assets + "Earth256.bytes"),
            File.ReadAllBytes(assets + "EarthWater256.bytes"),
            File.ReadAllBytes(assets + "EarthRivers.bytes"));
        Check(earth.Moisture?.Gleba != null && earth.Facts?.Biotemperature != null &&
              earth.Facts.GroundedIceMeters != null && earth.Facts.GlebaBiomes != null,
            "Generated Earth must publish integrated Moisture.Gleba and Facts.{Biotemperature, GroundedIceMeters, GlebaBiomes}");

        var sup = earth.Moisture.Gleba;
        // Sahara (25N, 10E) vs South China (24N, 113E) vs Tarim Leeward (39N, 84E) in July (month 6)
        float saharaImpact = sup.SampleMonthly(sup.RainfallImpact, (10f + 180f) / 360f, (25f + 90f) / 180f, 6);
        float southChinaImpact = sup.SampleMonthly(sup.RainfallImpact, (113f + 180f) / 360f, (24f + 90f) / 180f, 6);
        float tibetShadow = sup.SampleMonthly(sup.RainShadowiness, (84f + 180f) / 360f, (39f + 90f) / 180f, 6);

        var antarctica = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, 90f, -80f), 0);
        var amazon = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, -62f, -3f), 6);
        var sahara = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, 12f, 24f), 6);
        var siberia = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, 100f, 63f), 6);

        Console.WriteLine($"Earth 256 Gleba diagnostics (July): Sahara impact={saharaImpact:F2} ({sahara.GlebaBiome}, sand={sahara.Soil.Sand:F2}), " +
                          $"South China impact={southChinaImpact:F2}, Tarim rainShadow={tibetShadow:F2}, " +
                          $"Amazon={amazon.GlebaBiome} (B={amazon.BiotemperatureC:F1}C), Siberia={siberia.GlebaBiome}, Antarctica ice={antarctica.IceMeters:F0}m");

        Check(saharaImpact < southChinaImpact - 0.3f && tibetShadow > 0.05f, "Earth Sahara vs South China SLP impact or Tarim rain shadow missing");
        Check(antarctica.Available && antarctica.IceMeters > 500f && antarctica.GlebaBiome == GlebaBiomeKind.Glacier,
            $"Antarctica must carry grounded ice sheet: {antarctica.IceMeters:F1}m, {antarctica.GlebaBiome}");
        Check(amazon.BiotemperatureC > 22f && amazon.GlebaBiome == GlebaBiomeKind.WetJungle && amazon.Flora.Trees > 0.60f,
            $"Amazon must classify as WetJungle: B={amazon.BiotemperatureC:F1}, biome={amazon.GlebaBiome}, trees={amazon.Flora.Trees:F2}");
        Check(sahara.GlebaBiome == GlebaBiomeKind.DesertsAndXericShrublands && sahara.Soil.Sand > 0.65f,
            $"Sahara must classify as desert with sandy deflated soil: biome={sahara.GlebaBiome}, sand={sahara.Soil.Sand:F2}");
        Check(siberia.GlebaBiome == GlebaBiomeKind.Taiga && siberia.Flora.ConiferFraction > 0.65f,
            $"Central Siberia must classify as conifer Taiga: biome={siberia.GlebaBiome}, conifer={siberia.Flora.ConiferFraction:F2}");

        // Geography supplement checks on Earth-256: lakes, reefs, fjords, aquifers, inaccessibility, seasonal currents
        var geo = earth.Facts.GlebaGeography;
        Check(geo != null, "Generated Earth must publish Facts.GlebaGeography");

        // 1. Endorheic SaltwaterLake (Caspian Sea ~42N, 50E) vs Outflowing SweetwaterLake (Lake Superior ~47.5N, 87.5W & Lake Victoria ~1S, 33E)
        int caspianTile = FindNearestMatchingTile(earth, 50f, 42f, 5, t => earth.Water[t] == WaterKind.Lake);
        int superiorTile = FindNearestMatchingTile(earth, -87.5f, 47.5f, 5, t => earth.Water[t] == WaterKind.Lake);
        Check(caspianTile >= 0 && superiorTile >= 0, $"Expected Caspian ({caspianTile}) and Superior ({superiorTile}) lake cells on Earth 256");
        var caspianDiag = GlebaClimateSupplement.SampleTileDiagnostic(earth, caspianTile, 6);
        var superiorDiag = GlebaClimateSupplement.SampleTileDiagnostic(earth, superiorTile, 6);
        Check(caspianDiag.WaterbodyType == GlebaWaterbodyType.SaltwaterLake &&
              caspianDiag.LakeOrigin == GlebaLakeOrigin.Endorheic &&
              (caspianDiag.GlebaBiome == GlebaBiomeKind.SaltwaterLake || caspianDiag.GlebaBiome == GlebaBiomeKind.GlaciatedSaltwaterLake),
            $"Caspian Sea must classify as endorheic SaltwaterLake: wb={caspianDiag.WaterbodyType}, origin={caspianDiag.LakeOrigin}, biome={caspianDiag.GlebaBiome}");
        Check(superiorDiag.WaterbodyType == GlebaWaterbodyType.SweetwaterLake &&
              superiorDiag.LakeOrigin == GlebaLakeOrigin.Glacial &&
              superiorDiag.GlebaBiome == GlebaBiomeKind.SweetwaterLake,
            $"Lake Superior must classify as glacial SweetwaterLake: wb={superiorDiag.WaterbodyType}, origin={superiorDiag.LakeOrigin}, biome={superiorDiag.GlebaBiome}");

        // 2. Coral reefs restricted strictly to warm tropical/subtropical shallow seas (zero in high latitudes)
        int polarReefs = 0;
        for (int t = 0; t < earth.Count; t++)
            if (geo.HasFlag(t, GlebaGeoFlags.CoralReef) && Math.Abs(earth.Latitude(t)) > 40f) polarReefs++;
        Check(geo.CoralReefTileCount > 20 && polarReefs == 0,
            $"Expected warm-water coral reefs (>20) and zero polar reefs: total={geo.CoralReefTileCount}, polar={polarReefs}");

        // 3. Glacial fjord coasts in high-latitude mountain margins (Norway/Chile/Alaska/Greenland) and zero in tropics
        int tropicFjords = 0, norwayPatagoniaFjords = 0;
        for (int t = 0; t < earth.Count; t++)
        {
            if (!geo.HasFlag(t, GlebaGeoFlags.FjordCoast)) continue;
            float absLat = Math.Abs(earth.Latitude(t));
            if (absLat < 32f) tropicFjords++;
            if (absLat >= 42f) norwayPatagoniaFjords++;
        }
        Check(geo.FjordCoastTileCount > 25 && norwayPatagoniaFjords > 25 && tropicFjords == 0,
            $"Expected high-latitude fjord coasts and zero tropical fjords: total={geo.FjordCoastTileCount}, highLat={norwayPatagoniaFjords}, tropic={tropicFjords}");

        // 4. Deep groundwater aquifers on porous unglaciated land, zero under Antarctic ice sheet
        Check(geo.AquiferCount >= 10 && antarctica.AquiferId < 0,
            $"Expected >=10 regional aquifers and no subglacial Antarctic aquifer: count={geo.AquiferCount}, antAq={antarctica.AquiferId}");

        // 5. Global Ocean Inaccessibility & 4-Tier Hierarchy & Biogeographic Realms
        var dzungaria = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, 86f, 45f), 6);
        var pacific = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, -150f, 0f), 6);
        Check(pacific.InaccessibilityKm == 0f && dzungaria.InaccessibilityKm > 1800f && caspianDiag.InaccessibilityKm > 400f,
            $"Expected Pacific inaccessibility=0, Dzungaria >1800 km, Caspian >400 km: pac={pacific.InaccessibilityKm:F0}, dz={dzungaria.InaccessibilityKm:F0}, casp={caspianDiag.InaccessibilityKm:F0}");
        Check(geo.AreaCount > geo.RegionCount && geo.RegionCount > geo.SuperRegionCount &&
              geo.SuperRegionCount >= geo.SubcontinentCount && geo.SubcontinentCount >= 2,
            $"Expected nested 4-tier hierarchy counts: Areas={geo.AreaCount}, Regions={geo.RegionCount}, Super={geo.SuperRegionCount}, Subcont={geo.SubcontinentCount}");
        Check(amazon.BiogeographicRealm != siberia.BiogeographicRealm && sahara.BiogeographicRealm != antarctica.BiogeographicRealm,
            "Distinct continents must carry distinct Wallace biogeographic realms");

        // 6. Seasonal Somali / Monsoon Ocean Current shift & Eastern-Boundary Upwelling SST contrast
        GlebaGeographySupplement.SampleSeasonalOceanCurrent(earth, (54f + 180f) / 360f, (9f + 90f) / 180f, 0,
            out float somaliJanE, out float somaliJanN, out _, out _);
        GlebaGeographySupplement.SampleSeasonalOceanCurrent(earth, (54f + 180f) / 360f, (9f + 90f) / 180f, 6,
            out float somaliJulE, out float somaliJulN, out _, out _);
        GlebaGeographySupplement.SampleSeasonalOceanCurrent(earth, (-72f + 180f) / 360f, (34f + 90f) / 180f, 6,
            out _, out _, out _, out float gulfStreamSstAnom);
        GlebaGeographySupplement.SampleSeasonalOceanCurrent(earth, (-16f + 180f) / 360f, (24f + 90f) / 180f, 6,
            out _, out _, out _, out float canarySstAnom);
        Check(somaliJulN > somaliJanN + 0.01f || somaliJulE > somaliJanE + 0.01f,
            $"Expected Somali / Arabian Sea monsoon surface current shift Jan->Jul: Jan=({somaliJanE:F3},{somaliJanN:F3}), Jul=({somaliJulE:F3},{somaliJulN:F3})");
        Check(gulfStreamSstAnom > canarySstAnom + 1.5f,
            $"Expected Gulf Stream warm anomaly > Canary upwelling cold anomaly: GS={gulfStreamSstAnom:F2}C vs Can={canarySstAnom:F2}C");

        Console.WriteLine($"Earth 256 Gleba geography: reefs={geo.CoralReefTileCount}, fjords={geo.FjordCoastTileCount}, aquifers={geo.AquiferCount}, " +
                          $"volcanoes={geo.VolcanoCount}(active={geo.ActiveVolcanoCount}), Caspian={caspianDiag.WaterbodyType}, Superior={superiorDiag.WaterbodyType}, " +
                          $"DzungariaDist={dzungaria.InaccessibilityKm:F0}km, Hierarchy={geo.AreaCount}/{geo.RegionCount}/{geo.SuperRegionCount}/{geo.SubcontinentCount}, GS-Canary SST={gulfStreamSstAnom - canarySstAnom:F2}C");

        // 7. 6-pass drainage-routed soils, LGM ice sheets & inferred Earth tectonics/volcanism
        int riverMouth = -1; float bestRunoff = -1f;
        for (int dy = -5; dy <= 5; dy++)
        for (int dx = -5; dx <= 5; dx++)
        {
            int idx = earth.Index((int)((89.5f + 180f) / 360f * earth.Width) + dx, (int)((23.5f + 90f) / 180f * earth.Height) + dy);
            if (idx >= 0 && !FlatTerrainAnalysis.AreaWater(earth.Water[idx]) && earth.Runoff[idx] > bestRunoff)
            {
                bestRunoff = earth.Runoff[idx];
                riverMouth = idx;
            }
        }
        Check(riverMouth >= 0, "Expected alluvial river lowland tile near Ganges-Brahmaputra delta");
        var deltaSoil = geo.GetSoilState(riverMouth);
        Check(sahara.Soil.Sand > 0.70f && (deltaSoil.Silt + deltaSoil.Clay) > 0.55f && deltaSoil.Depth > sahara.Soil.Depth,
            $"Expected aeolian sand in Sahara ({sahara.Soil.Sand:F2}) and deeper silt-clay alluvium in delta (silt+clay={deltaSoil.Silt + deltaSoil.Clay:F2}, depth={deltaSoil.Depth:F2} vs {sahara.Soil.Depth:F2})");
        var scandinavia = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, 15f, 65f), 6);
        Check(scandinavia.LgmIceMeters > 100f && geo.VolcanoCount >= 10 && geo.ActiveVolcanoCount >= 5,
            $"Expected Fennoscandian LGM ice >100m ({scandinavia.LgmIceMeters:F0}m) and Ring-of-Fire volcanoes ({geo.VolcanoCount}/{geo.ActiveVolcanoCount})");

        // 8. Synoptic Cloud Forcing Physics on Earth (July vs January)
        const int cw = 256, ch = 128;
        var forcingJul = new byte[cw * ch * 4];
        var auxJul = new byte[cw * ch * 4];
        var upperJul = new ushort[cw * ch * 4];
        var forcingJan = new byte[cw * ch * 4];
        var auxJan = new byte[cw * ch * 4];
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 195.0, cw, ch, forcingJul, auxJul, upperJul);
        GlebaGeographySupplement.BuildCloudSynopticFields(earth, 15.0, cw, ch, forcingJan, auxJan, null);

        double MeanChannel(byte[] buf, int chIdx, float lon0, float lon1, float lat0, float lat1)
        {
            double sum = 0; int cnt = 0;
            for (int y = 0; y < ch; y++)
            {
                float lat = ((y + 0.5f) / ch - 0.5f) * 180f;
                if (lat < lat0 || lat > lat1) continue;
                for (int x = 0; x < cw; x++)
                {
                    float lon = ((x + 0.5f) / cw - 0.5f) * 360f;
                    if (lon < lon0 || lon > lon1) continue;
                    sum += buf[(y * cw + x) * 4 + chIdx] / 255.0;
                    cnt++;
                }
            }
            return cnt > 0 ? sum / cnt : 0.0;
        }

        double monsoonLift = MeanChannel(forcingJul, 0, 82f, 94f, 14f, 24f);
        double monsoonItcz = MeanChannel(forcingJul, 1, 82f, 94f, 14f, 24f);
        double saharaLift = MeanChannel(forcingJul, 0, 8f, 32f, 20f, 28f);
        double saharaMoist = MeanChannel(forcingJul, 3, 8f, 32f, 20f, 28f);
        Check(monsoonLift > saharaLift + 0.25 && monsoonItcz > 0.35 && saharaMoist < 0.25,
            $"July cloud forcing must favor South Asian Monsoon over Sahara subsidence: monsoonLift={monsoonLift:F2} (ITCZ={monsoonItcz:F2}) vs saharaLift={saharaLift:F2} (moist={saharaMoist:F2})");

        double himalayaWindward = MeanChannel(auxJul, 1, 84f, 92f, 26f, 28.5f);
        double tibetLeeShadow = MeanChannel(auxJul, 2, 82f, 92f, 30.5f, 35f);
        Check(himalayaWindward > 0.08 && tibetLeeShadow > 0.18,
            $"Expected Himalayan windward crest lift ({himalayaWindward:F2}) and Tibetan leeward rain-shadow clearing ({tibetLeeShadow:F2})");

        double peruSc = MeanChannel(auxJul, 0, -84f, -75f, -24f, -10f);
        double benguelaSc = MeanChannel(auxJul, 0, 6f, 13f, -26f, -14f);
        double kuroshioSc = MeanChannel(auxJul, 0, 130f, 142f, 24f, 32f);
        Check(peruSc > 0.30 && benguelaSc > 0.30 && peruSc > kuroshioSc + 0.08,
            $"Expected eastern-boundary upwelling Marine Stratocumulus > western boundary: Peru={peruSc:F2}, Benguela={benguelaSc:F2} vs Kuroshio={kuroshioSc:F2}");

        double northAtlanticTrack = MeanChannel(forcingJul, 2, -45f, -15f, 46f, 60f);
        double roaringFiftiesTrack = MeanChannel(forcingJul, 2, -180f, 180f, -60f, -46f);
        Check(northAtlanticTrack > 0.42 && roaringFiftiesTrack > 0.45,
            $"Expected strong mid-latitude baroclinic storm tracks: N.Atl={northAtlanticTrack:F2}, S.Ocean={roaringFiftiesTrack:F2}");

        // Hadley-Walker Orthogonal Double Circulation + Equatorial Ekman Cold-Tongue & Split Double-ITCZ:
        // Audit ALL FOUR SEASONS (Jan, Apr Equinox, Jul, Oct Equinox) to guarantee NO 360-degree continuous equatorial cloud ring!
        double warmPoolMcConv = MeanChannel(forcingJul, 1, 100f, 140f, -3f, 4f);
        double amazonEqConv = MeanChannel(forcingJan, 1, -70f, -54f, -5f, 2f);
        double eastPacColdSlotConv = MeanChannel(forcingJul, 1, -135f, -92f, -2.2f, 2.2f);
        double eastPacOffEqItcz = MeanChannel(forcingJul, 1, -135f, -95f, 6.0f, 10.0f);
        double eastPacColdSlotSc = MeanChannel(auxJul, 0, -135f, -92f, -2.2f, 2.2f);
        Console.WriteLine($"  Hadley-Walker & Double-ITCZ: MaritimeContinent Cb={warmPoolMcConv:F2}, Amazon Cb={amazonEqConv:F2}, EastPac 0deg ColdSlot Cb={eastPacColdSlotConv:F2} (Sc={eastPacColdSlotSc:F2}), EastPac 6-10N Split-ITCZ={eastPacOffEqItcz:F2}");
        Check(warmPoolMcConv > eastPacColdSlotConv + 0.30 && amazonEqConv > eastPacColdSlotConv + 0.25,
            $"Walker ascending limbs must exceed equatorial Pacific cold-tongue Cb: MC={warmPoolMcConv:F2}, Amz={amazonEqConv:F2} vs EastPac0={eastPacColdSlotConv:F2}");
        Check(eastPacColdSlotConv < 0.16 && eastPacOffEqItcz > eastPacColdSlotConv + 0.10 && eastPacColdSlotSc > 0.22,
            $"Equatorial Ekman cold tongue must suppress 0 deg thunderclouds, split ITCZ off-equator, and form low Sc: slotCb={eastPacColdSlotConv:F2}, offEq={eastPacOffEqItcz:F2}, slotSc={eastPacColdSlotSc:F2}");

        var fSeason = new byte[cw * ch * 4];
        var aSeason = new byte[cw * ch * 4];
        foreach ((string name, double day) in new[] { ("Jan", 15.0), ("Apr-Equinox", 105.0), ("Jul", 195.0), ("Oct-Equinox", 285.0) })
        {
            GlebaGeographySupplement.BuildCloudSynopticFields(earth, day, cw, ch, fSeason, aSeason, null);
            double eastPacCb = MeanChannel(fSeason, 1, -145f, -90f, -2.5f, 2.5f);
            double eastPacClear = MeanChannel(aSeason, 2, -145f, -90f, -2.5f, 2.5f);
            int suppressedCols = 0, maxContiguousActive = 0, curActive = 0;
            int eqY0 = (int)((-3.0 / 180.0 + 0.5) * ch), eqY1 = (int)((3.0 / 180.0 + 0.5) * ch);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int x = 0; x < cw; x++)
                {
                    double colCb = 0, colClear = 0;
                    for (int y = eqY0; y <= eqY1; y++)
                    {
                        colCb += fSeason[(y * cw + x) * 4 + 1] / 255.0;
                        colClear += aSeason[(y * cw + x) * 4 + 2] / 255.0;
                    }
                    int rows = eqY1 - eqY0 + 1;
                    colCb /= rows; colClear /= rows;
                    if (pass == 0 && colCb < 0.20 && colClear > 0.22) suppressedCols++;
                    if (colCb >= 0.32) { curActive++; if (curActive > maxContiguousActive) maxContiguousActive = curActive; }
                    else curActive = 0;
                }
            }
            maxContiguousActive = Math.Min(cw, maxContiguousActive);
            double clearLongitudeShare = suppressedCols / (double)cw;
            double maxActiveSpanDeg = maxContiguousActive * 360.0 / cw;
            Console.WriteLine($"  4-Season Equator Audit [{name,-11}]: EastPac0 Cb={eastPacCb:F2} (clear={eastPacClear:F2}), suppressedLngShare={clearLongitudeShare:P1}, maxContiguousConvSpan={maxActiveSpanDeg:F1} deg");
            Check(eastPacCb < 0.16 && eastPacClear > 0.35 && clearLongitudeShare >= 0.42 && maxActiveSpanDeg < 115.0,
                $"Equatorial cloud ring failed to break up in {name}: EastPacCb={eastPacCb:F2}, clear={eastPacClear:F2}, suppressedShare={clearLongitudeShare:P1}, maxSpan={maxActiveSpanDeg:F1} deg");
        }

        // Global 12-month Earth cloud climatology consistency vs NOAA PSL 1991-2020 30-year zonal & regional normals (>= 85% gate)
        double[] noaaZonal18 = { 24.3, 46.6, 62.9, 63.0, 59.3, 51.4, 48.1, 53.9, 57.9, 55.8, 44.6, 44.6, 48.1, 52.7, 53.8, 47.6, 43.6, 43.8 };
        var simZonal18 = new double[18];
        var simZonalCnt = new int[18];
        var fMon = new byte[cw * ch * 4];
        var aMon = new byte[cw * ch * 4];
        for (int m = 0; m < 12; m++)
        {
            GlebaGeographySupplement.BuildCloudSynopticFields(earth, m * 30.0 + 15.0, cw, ch, fMon, aMon, null);
            for (int y = 0; y < ch; y++)
            {
                float lat = ((y + 0.5f) / ch - 0.5f) * 180f;
                int bin = FMath.Clamp((int)Math.Floor((lat + 85.0) / 10.0), 0, 17);
                for (int x = 0; x < cw; x++)
                {
                    simZonal18[bin] += (fMon[(y * cw + x) * 4] / 255.0) * 100.0;
                    simZonalCnt[bin]++;
                }
            }
        }
        double wZSum = 0, maeZSum = 0, meanObsZ = 0, meanSimZ = 0;
        var wZArr = new double[18];
        for (int b = 0; b < 18; b++)
        {
            simZonal18[b] /= Math.Max(1, simZonalCnt[b]);
            double midLat = Math.Clamp(-80.0 + b * 10.0, -88.0, 88.0);
            wZArr[b] = Math.Cos(midLat * Math.PI / 180.0);
            wZSum += wZArr[b];
            maeZSum += wZArr[b] * Math.Abs(simZonal18[b] - noaaZonal18[b]);
            meanObsZ += wZArr[b] * noaaZonal18[b];
            meanSimZ += wZArr[b] * simZonal18[b];
        }
        meanObsZ /= wZSum; meanSimZ /= wZSum;
        double covZ = 0, varObsZ = 0, varSimZ = 0;
        for (int b = 0; b < 18; b++)
        {
            double dO = noaaZonal18[b] - meanObsZ, dS = simZonal18[b] - meanSimZ;
            covZ += wZArr[b] * dO * dS; varObsZ += wZArr[b] * dO * dO; varSimZ += wZArr[b] * dS * dS;
        }
        double zonalConsistency = 100.0 - maeZSum / wZSum;
        double zonalCorr = covZ / Math.Sqrt(Math.Max(1e-9, varObsZ * varSimZ));
        Console.WriteLine($"  Global Cloud vs NOAA 1991-2020: Mean Sim={meanSimZ:F1}% vs Obs={meanObsZ:F1}%, Consistency={zonalConsistency:F2}% (1-MAE), Zonal r={zonalCorr:F4}");
        Check(zonalConsistency >= 85.0 && zonalCorr >= 0.85,
            $"Global Earth cloud simulation fell below 85% NOAA consistency gate: consistency={zonalConsistency:F2}%, r={zonalCorr:F4}");

        // Seasonal migration of Asian-Indian sector ITCZ convective cloud core (75E..95E)
        double WeightedItczLat(byte[] buf)
        {
            double num = 0, den = 1e-9;
            for (int y = 0; y < ch; y++)
            {
                float lat = ((y + 0.5f) / ch - 0.5f) * 180f;
                if (Math.Abs(lat) > 35f) continue;
                for (int x = 0; x < cw; x++)
                {
                    float lon = ((x + 0.5f) / cw - 0.5f) * 360f;
                    if (lon < 75f || lon > 95f) continue;
                    double w = buf[(y * cw + x) * 4 + 1] / 255.0;
                    num += lat * w; den += w;
                }
            }
            return num / den;
        }
        double itczJanLat = WeightedItczLat(forcingJan);
        double itczJulLat = WeightedItczLat(forcingJul);
        Check(itczJulLat - itczJanLat > 12.0,
            $"Expected Indian/Asian ITCZ deep convective cloud belt to migrate >12 deg northward Jan->Jul: Jan={itczJanLat:F1}deg, Jul={itczJulLat:F1}deg");
        Console.WriteLine($"Earth 256 Gleba cloud physics: MonsoonLift={monsoonLift:F2} vs SaharaLift={saharaLift:F2}, PeruSc={peruSc:F2}, Roaring50s={roaringFiftiesTrack:F2}, ITCZ Jan={itczJanLat:F1}->Jul={itczJulLat:F1}deg");

        // 9. Round 11: Complete 37-rock registry, Deccan/Siberian LIPs, Paleo-orogens & Wild Founder Crops / Carrying Capacity
        Check(GlebaGeographySupplement.RockTypeCount == 37 &&
              GlebaGeographySupplement.GetRockProperties(GlebaRockKind.Limestone).CoralReefBedrock &&
              Math.Abs(GlebaGeographySupplement.GetRockProperties(GlebaRockKind.Basalt).MineralRichness - 1.0f) < 1e-5f,
            "Gleba rock registry must contain all 37 rock_types.lua entries with exact Limestone reef & Basalt mineral richness");
        GlebaGeographySupplement.GetMicroplateGeochemistry(17, 0x12345u, out _, out float mpQ, out float mpC, out _, out _);
        Check(Math.Abs((mpQ + mpC) - 1.0f) < 1e-4f, "Microplate quartz + calcite must normalize to 1.0");

        var seenRocks = new bool[37];
        for (int t = 0; t < earth.Count; t++) seenRocks[(int)geo.Bedrock[t]] = true;
        int distinctEarthRocks = 0;
        for (int r = 0; r < 37; r++) if (seenRocks[r]) distinctEarthRocks++;

        var deccan = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, 76f, 19f), 6);
        var yangtze = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, 116f, 29f), 6);
        var europeLoess = GlebaClimateSupplement.SampleTileDiagnostic(earth, TileAtLonLat(earth, 18f, 50f), 6);
        Check(geo.LipTileCount >= 20 && (deccan.GeoFlags & GlebaGeoFlags.LargeIgneousProvince) != 0 &&
              (deccan.RockKind == GlebaRockKind.Basalt || deccan.RockKind == GlebaRockKind.Picrobasalt),
            $"Deccan Traps (76E,19N) must carry LargeIgneousProvince flood basalt: flags={deccan.GeoFlags}, rock={deccan.RockKind}, lipTiles={geo.LipTileCount}");
        Check(geo.PaleoOrogenTileCount >= 10 && distinctEarthRocks >= 22,
            $"Expected paleo-orogen belts ({geo.PaleoOrogenTileCount}) and >=22 distinct rock types on Earth 256 ({distinctEarthRocks})");
        Check((yangtze.DominantCrop == GlebaFounderCrop.WildRice || yangtze.DominantCrop == GlebaFounderCrop.AzollaCompanion) &&
              yangtze.CarryingCapacity > 0.45f,
            $"Yangtze monsoon basin must favor WildRice/Azolla with high carrying capacity: crop={yangtze.DominantCrop}, cap={yangtze.CarryingCapacity:F2}");
        Check((europeLoess.DominantCrop == GlebaFounderCrop.WildRye || europeLoess.DominantCrop == GlebaFounderCrop.WildPea || europeLoess.DominantCrop == GlebaFounderCrop.WildBitterVetch) &&
              europeLoess.CarryingCapacity > sahara.CarryingCapacity + 0.25f &&
              antarctica.CarryingCapacity == 0f && geo.CradleTileCount >= 30,
            $"Temperate loess vs Sahara/Antarctica carrying capacity failed: eurCrop={europeLoess.DominantCrop}({europeLoess.CarryingCapacity:F2}), sahara={sahara.CarryingCapacity:F2}, cradles={geo.CradleTileCount}");
        Console.WriteLine($"Earth 256 Gleba Round 11: rocks={distinctEarthRocks}/37, LIPs={geo.LipTileCount} (Deccan={deccan.RockKind}), PaleoOrogens={geo.PaleoOrogenTileCount}, Yangtze={yangtze.DominantCrop}(cap={yangtze.CarryingCapacity:F2}), Cradles={geo.CradleTileCount}");
    }

    static int FindNearestMatchingTile(WorldData w, float lonDeg, float latDeg, int radius, Func<int, bool> pred)
    {
        int cx = FMath.Clamp((int)((lonDeg + 180f) / 360f * w.Width), 0, w.Width - 1);
        int cy = FMath.Clamp((int)((latDeg + 90f) / 180f * w.Height), 0, w.Height - 1);
        int best = -1, bestDist2 = int.MaxValue;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int idx = w.Index(cx + dx, cy + dy);
            if (idx < 0 || !pred(idx)) continue;
            int d2 = dx * dx + dy * dy;
            if (d2 < bestDist2) { bestDist2 = d2; best = idx; }
        }
        return best;
    }

    static void CheckRossbyJetsAndMapmodes()
    {
        // Rossby jet streams peak eastward near +-30 deg and +-60 deg with longitudinal wave meanders
        var rossby = new GlebaRossbyWaveField(0x12345678u);
        double minVy = double.MaxValue, maxVy = double.MinValue, meanVx = 0.0;
        for (int i = 0; i < 36; i++)
        {
            double lon = -Math.PI + (i + 0.5) * (2.0 * Math.PI / 36.0);
            rossby.GetJetStreamMovementAtLatLon(58.0 * Math.PI / 180.0, lon, out float vx, out float vy);
            meanVx += vx / 36.0;
            if (vy < minVy) minVy = vy;
            if (vy > maxVy) maxVy = vy;
        }
        Check(meanVx > 0.20 && maxVy > 0.15 && minVy < -0.15,
            $"Rossby polar jet must flow eastward with meridional wave meanders: meanVx={meanVx:F3}, vy=[{minVy:F3}, {maxVy:F3}]");

        GlebaGeographySupplement.SampleCividis(0f, out byte r0, out byte g0, out byte b0);
        GlebaGeographySupplement.SampleCividis(1f, out byte r1, out byte g1, out byte b1);
        Check(r0 == 0 && g0 == 34 && b0 == 78 && r1 == 254 && g1 == 232 && b1 == 55,
            "Cividis endpoint stops must match Gleba .rdata lookup table (#00224e -> #fee837)");

        Check(Enum.TryParse<MapLayer>("SurfaceWinds", out _) && Enum.TryParse<MapLayer>("OceanCurrents", out _),
            "MapLayer must expose SurfaceWinds and OceanCurrents views");
        Check(GlebaGeographySupplement.FloatToHalfBits(0f) == 0x0000 &&
              GlebaGeographySupplement.FloatToHalfBits(1f) == 0x3C00 &&
              GlebaGeographySupplement.FloatToHalfBits(-1f) == 0xBC00 &&
              GlebaGeographySupplement.FloatToHalfBits(0.5f) == 0x3800,
            "FloatToHalfBits must encode IEEE-754 binary16 values accurately");
    }

    static void CheckWindShorePacking(WorldData world, int month)
    {
        var bands = world.Moisture.Gleba;
        int onshore = 0, offshore = 0;
        for (int tile = 0; tile < world.Count; tile++)
        {
            float u = (tile % world.Width + .5f) / world.Width, v = (tile / world.Width + .5f) / world.Height;
            int index = (int)(v * bands.Height) * bands.Width + (int)(u * bands.Width);
            var type = bands.ShoreTypes[index * 12 + month];
            GlebaGeographySupplement.PackMapmodeDisplay(world, tile, month, GlebaMapmodeKind.ClimateSurfaceWinds,
                out byte r, out byte g, out byte b, out byte a);
            Check(a == (type == GlebaShoreType.OnShore ? 255 : type == GlebaShoreType.OffShore ? 251 : 253),
                "Wind display must carry the simulated shore class separately from pressure");
            if (world.CoastDistance[tile] <= 2 && type != GlebaShoreType.NotApplicable)
            {
                bool wet = type == GlebaShoreType.OnShore;
                if (wet) onshore++; else offshore++;
                r = (byte)(.5f * (r + (wet ? 50 : 150)));
                g = (byte)(.5f * (g + 50));
                b = (byte)(.5f * (b + (wet ? 200 : 50)));
            }
            GlebaGeographySupplement.EvaluateMapmodeColor(world, tile, month, GlebaMapmodeKind.ClimateSurfaceWinds,
                out byte originalR, out byte originalG, out byte originalB);
            Check(r == originalR && g == originalG && b == originalB,
                "Wind pressure and the stored class must reproduce the original Gleba diagnostic");
        }
        Check(onshore > 0 && offshore > 0, "Wind display fixture must contain both marine shore classes");
    }

    static void CheckProceduralGeographyCarvingAndErosion()
    {
        var world = FlatGenerator.Generate(128, 64, "GLEBA-CARVE-2026", TerrainPreset.Continents, default);
        Check(world.Facts?.GlebaGeography != null, "Procedural Continents world must build GlebaGeography");
        CheckWindShorePacking(world, 6);

        GlebaGeographySupplement.EvaluateMapmodeColor(world, 1000, 6, GlebaMapmodeKind.ClimateSurfaceWinds, out byte wr, out byte wg, out byte wb);
        GlebaGeographySupplement.EvaluateMapmodeColor(world, 1000, 6, GlebaMapmodeKind.ClimateOceanCurrents, out byte cr, out byte cg, out byte cb);
        bool hasWindFlow = GlebaGeographySupplement.EvaluateMapmodeFlow(world, 0.5f, 0.35f, 6, GlebaMapmodeKind.ClimateSurfaceWinds, out float fu, out float fv);
        bool hasRossbyFlow = GlebaGeographySupplement.EvaluateMapmodeFlow(world, 0.25f, 0.82f, 6, GlebaMapmodeKind.RossbyJetStream, out float ru, out _);
        Check(hasWindFlow && FMath.Finite(fu) && FMath.Finite(fv) && hasRossbyFlow && ru > 0f && (wr + wg + wb) > 0 && (cr + cg + cb) > 0,
            "Mapmode color and UV flow evaluators must produce valid colors and vectors");

        for (MapLayer layer = MapLayer.SurfaceWinds; layer <= MapLayer.GlebaCrops; layer++)
        {
            var gMode = GlebaGeographySupplement.MapLayerToGlebaMode(layer);
            GlebaGeographySupplement.EvaluateMapmodeColor(world, 1000, 6, gMode, out byte mr, out byte mg, out byte mb);
            Check((mr + mg + mb) >= 0, $"Invalid mapmode color for {layer}");
        }

        var windHalf = new ushort[64 * 32 * 4];
        var currHalf = new ushort[64 * 32 * 4];
        var rossbyHalf = new ushort[64 * 32 * 4];
        var bandHalf = new ushort[64 * 4 * 4];
        GlebaGeographySupplement.PackTwoMonthWindFlowHalf(world, 6, 64, 32, windHalf);
        GlebaGeographySupplement.PackTwoMonthCurrentFlowHalf(world, 6, 64, 32, currHalf);
        GlebaGeographySupplement.PackTwoMonthRossbyFlowHalf(world, 6, 64, 32, rossbyHalf);
        GlebaGeographySupplement.PackInterpolatedBandsHalf(world, 195.0, 64, bandHalf);
        int nonZeroWind = 0, nonZeroCurr = 0, nonZeroRossby = 0;
        for (int i = 0; i < windHalf.Length; i++)
        {
            if ((windHalf[i] & 0x7FFFu) != 0) nonZeroWind++;
            if ((currHalf[i] & 0x7FFFu) != 0) nonZeroCurr++;
            if ((rossbyHalf[i] & 0x7FFFu) != 0) nonZeroRossby++;
        }
        Check(nonZeroWind > 1000 && nonZeroCurr > 500 && nonZeroRossby > 1000 && bandHalf[0] == bandHalf[64 * 4] && bandHalf[2 * 64 * 4] == bandHalf[3 * 64 * 4],
            $"Two-month RGBAHalf flow and band packers failed: wind={nonZeroWind}, curr={nonZeroCurr}, rossby={nonZeroRossby}");

        var clone = world.Clone();
        int shoaled = GlebaGeographySupplement.ShoalCoralReefs(clone, clone.Facts.GlebaGeography);
        int fjords = GlebaGeographySupplement.CarveGlacialFjords(clone, acceptanceProbability: 1.0f);
        int relaxed = GlebaGeographySupplement.ApplyThermalErosion(clone, iterations: 4);
        float[] etFlow = GlebaGeographySupplement.ApplySortedHydraulicErosion(clone);
        Check(shoaled == clone.Facts.GlebaGeography.CoralReefTileCount && fjords >= 0 && relaxed >= 0 && etFlow.Length == clone.Count,
            $"Procedural carving & erosion operators failed: shoaled={shoaled}, fjords={fjords}, relaxed={relaxed}");
    }

    static void CheckProceduralPlanetGeneration()
    {
        int totalPassiveShelf = 0, totalTrench = 0, totalForeland = 0, totalPaleo = 0, totalLip = 0, totalSwell = 0, totalFjords = 0, totalReefs = 0;
        double passiveDepthSum = 0, trenchDepthSum = 0;

        foreach (TerrainPreset preset in FlatGenerator.ProceduralPresets)
        {
            var w = FlatGenerator.Generate(256, 128, "GLEBA-PLANET-GEN-2026", preset, default);
            var tec = w.Tectonics;
            var geo = w.Facts?.GlebaGeography;
            Check(tec != null && geo != null, $"{preset}: missing Tectonics or GlebaGeography");
            Check(tec.Microplate != null && tec.Microplate.Length == w.Count && tec.MicroplateCount >= 40,
                $"{preset}: invalid microplate hierarchy count={tec.MicroplateCount}");
            Check(tec.Province != null && tec.Province.Length == w.Count && tec.RockResistance != null && tec.RockResistance.Length == w.Count,
                $"{preset}: missing Gleba province or lithology resistance arrays");

            float minRes = float.MaxValue, maxRes = float.MinValue;
            int passive = 0, trench = 0, foreland = 0, paleo = 0, lip = 0, swell = 0, shield = 0, hawaii = 0;

            for (int i = 0; i < w.Count; i++)
            {
                // Sub-plate microplates are strictly nested inside macro-plates (4 microplates per plate)
                Check(tec.Microplate[i] / 4 == tec.Plate[i] && geo.MicroplateId[i] == tec.Microplate[i],
                    $"{preset}: microplate {tec.Microplate[i]} out of sync with macro-plate {tec.Plate[i]} or GlebaGeography");

                float r = tec.RockResistance[i];
                if (r < minRes) minRes = r;
                if (r > maxRes) maxRes = r;

                var p = tec.Province[i];
                if ((p & GlebaTectonicProvince.PassiveShelf) != 0 && FlatTerrainAnalysis.Ocean(w.Water[i]))
                {
                    passive++;
                    passiveDepthSum += -w.Elevation[i];
                }
                if ((p & GlebaTectonicProvince.ActiveTrench) != 0 && FlatTerrainAnalysis.Ocean(w.Water[i]))
                {
                    trench++;
                    trenchDepthSum += -w.Elevation[i];
                }
                if ((p & GlebaTectonicProvince.ForelandBasin) != 0) foreland++;
                if ((p & GlebaTectonicProvince.PaleoOrogen) != 0)
                {
                    paleo++;
                    Check(geo.HasFlag(i, GlebaGeoFlags.PaleoOrogen), $"{preset}: PaleoOrogen flag not synced to GlebaGeography");
                }
                if ((p & GlebaTectonicProvince.LargeIgneousProvince) != 0)
                {
                    lip++;
                    Check(geo.HasFlag(i, GlebaGeoFlags.LargeIgneousProvince), $"{preset}: LIP flag not synced to GlebaGeography");
                    if (!FlatTerrainAnalysis.Ocean(w.Water[i]))
                    {
                        var rock = geo.Bedrock[i];
                        Check(rock == GlebaRockKind.Basalt || rock == GlebaRockKind.Picrobasalt,
                            $"{preset}: continental LIP tile must map to flood-basalt bedrock, got {rock}");
                    }
                }
                if ((p & GlebaTectonicProvince.MantleSuperswell) != 0) swell++;
                if ((p & GlebaTectonicProvince.CratonShield) != 0) shield++;
                if ((p & GlebaTectonicProvince.HawaiianChain) != 0) hawaii++;
            }

            Check(maxRes - minRes > 0.45f, $"{preset}: insufficient lithology resistance contrast ({minRes:F2}..{maxRes:F2})");
            Check(passive > 50 && hawaii > 0 && shield > 0, $"{preset}: missing passive shelves ({passive}), shields ({shield}), or hotspot chains ({hawaii})");

            // Verify all 4 seasons on procedural worlds never form an unbroken 360-degree equatorial cloud ring
            const int pw = 128, ph = 64;
            var pf = new byte[pw * ph * 4];
            var pa = new byte[pw * ph * 4];
            int py0 = (int)((-3.0 / 180.0 + 0.5) * ph), py1 = (int)((3.0 / 180.0 + 0.5) * ph);
            foreach (double pDay in new[] { 15.0, 105.0, 195.0, 285.0 })
            {
                GlebaGeographySupplement.BuildCloudSynopticFields(w, pDay, pw, ph, pf, pa, null);
                int pSuppressed = 0, pMaxSpan = 0, pCur = 0;
                for (int pass = 0; pass < 2; pass++)
                for (int x = 0; x < pw; x++)
                {
                    double cCb = 0;
                    for (int y = py0; y <= py1; y++) cCb += pf[(y * pw + x) * 4 + 1] / 255.0;
                    cCb /= (py1 - py0 + 1);
                    if (pass == 0 && cCb < 0.22) pSuppressed++;
                    if (cCb >= 0.35) { pCur++; if (pCur > pMaxSpan) pMaxSpan = pCur; } else pCur = 0;
                }
                pMaxSpan = Math.Min(pw, pMaxSpan);
                Check(pSuppressed >= pw * 0.35 && pMaxSpan * 360.0 / pw < 150.0,
                    $"{preset} day={pDay}: unbroken equatorial convective belt (suppressed={pSuppressed}/{pw}, maxSpan={pMaxSpan * 360.0 / pw:F1} deg)");
            }

            totalPassiveShelf += passive;
            totalTrench += trench;
            totalForeland += foreland;
            totalPaleo += paleo;
            totalLip += lip;
            totalSwell += swell;
            totalFjords += geo.FjordCoastTileCount;
            totalReefs += geo.CoralReefTileCount;

            Console.WriteLine($"  GlebaPlanetGen {preset,-14}: microplates={tec.MicroplateCount}, passiveShelf={passive}, trench={trench}, foreland={foreland}, paleoOrogen={paleo}, LIP={lip}, superswell={swell}, fjords={geo.FjordCoastTileCount}, reefs={geo.CoralReefTileCount}");
        }

        double meanPassiveDepth = passiveDepthSum / Math.Max(1, totalPassiveShelf);
        double meanTrenchDepth = trenchDepthSum / Math.Max(1, totalTrench);
        Console.WriteLine($"  GlebaPlanetGen shelf/trench contrast: passive shelf mean depth={meanPassiveDepth:F0} m vs active trench mean depth={meanTrenchDepth:F0} m");

        Check(totalTrench > 50 && meanPassiveDepth < 320.0 && meanTrenchDepth > 5200.0,
            $"Passive shelf ({meanPassiveDepth:F0} m) vs active trench ({meanTrenchDepth:F0} m) bathymetric contrast failed");
        Check(totalForeland > 100 && totalPaleo > 50 && totalLip > 100 && totalSwell > 100 && totalFjords > 10 && totalReefs > 20,
            $"Missing Gleba provinces across presets: foreland={totalForeland}, paleo={totalPaleo}, lip={totalLip}, swell={totalSwell}, fjords={totalFjords}, reefs={totalReefs}");
    }
}
