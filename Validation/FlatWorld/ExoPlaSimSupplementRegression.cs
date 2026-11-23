using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;

internal static class ExoPlaSimSupplementRegression
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ExoPlaSimSupplementRegression failed: " + message);
    }

    public static void Run()
    {
        var earth256 = LoadEarth(256);
        var earth512 = LoadEarth(512);
        CheckZeroSoutheastAsiaDesert(earth256);
        CheckZeroSoutheastAsiaDesert(earth512);
        CheckRainModalSplitAndWaterBudget(earth256);
        CheckRadiationAlbedoAndDiurnalRange(earth256);
        CheckStormClimatologyMpiAndGpi(earth256);
        CheckPastaBioclimateAndMapmodes(earth256);
        CheckPlanetaryPresetsAndEyeball();
        Console.WriteLine("PASS ExoPlaSim supplement package: 0.0% SE Asia desert gate (256 & 512), split convective/stratiform rain, 4-component radiation/OLR/DTR, storm MPI/CAPE/GPI, 19-class Pasta bioclimate, and planetary presets.");
    }

    static WorldData LoadEarth(int width)
    {
        const string assets = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        return FlatGenerator.Generate(
            width, width / 2, "ORBIS-2D-2026", TerrainPreset.Earth, default,
            File.ReadAllBytes(assets + $"Earth{width}.bytes"),
            File.ReadAllBytes(assets + $"EarthWater{width}.bytes"),
            File.ReadAllBytes(assets + "EarthRivers.bytes"));
    }

    static void EvaluateBox(
        WorldData w, double west, double east, double south, double north,
        out double areaWeight, out double meanRainMm, out double desertKoppenShare,
        out double desertBiomeShare, out double aridShare, out double meanSeaBreeze)
    {
        areaWeight = 0;
        double rainSum = 0, bwSum = 0, desertBiomeSum = 0, aridSum = 0, breezeSum = 0;
        var exo = w.Moisture?.ExoPlaSim;
        for (int y = 0; y < w.Height; y++)
        {
            double lat = ((y + 0.5) / w.Height - 0.5) * 180.0;
            if (lat < south || lat >= north) continue;
            double wt = Math.Cos(lat * Math.PI / 180.0);
            float v = (y + 0.5f) / w.Height;
            for (int x = 0; x < w.Width; x++)
            {
                double lon = ((x + 0.5) / w.Width - 0.5) * 360.0;
                if (lon < west || lon >= east) continue;
                int i = y * w.Width + x;
                if (FlatTerrainAnalysis.AreaWater(w.Water[i])) continue;
                areaWeight += wt;
                rainSum += wt * w.Facts.AnnualRain[i];
                var k = w.Climates[i];
                if (k == Koppen.BWh || k == Koppen.BWk) bwSum += wt;
                if (k >= Koppen.BWh && k <= Koppen.BSk) aridSum += wt;
                if (w.Biomes[i] == Biome.Desert) desertBiomeSum += wt;
                if (exo != null) breezeSum += wt * exo.SampleStatic(exo.MaritimeSeaBreezeGuard, (x + 0.5f) / w.Width, v);
            }
        }
        meanRainMm = areaWeight > 0 ? rainSum / areaWeight : 0;
        desertKoppenShare = areaWeight > 0 ? bwSum / areaWeight : 0;
        desertBiomeShare = areaWeight > 0 ? desertBiomeSum / areaWeight : 0;
        aridShare = areaWeight > 0 ? aridSum / areaWeight : 0;
        meanSeaBreeze = areaWeight > 0 ? breezeSum / areaWeight : 0;
    }

    static void CheckZeroSoutheastAsiaDesert(WorldData earth)
    {
        // 1. Mainland Indochina (Myanmar, Thailand, Laos, Cambodia, Vietnam: 97..109E, 8..22N)
        EvaluateBox(earth, 97, 109, 8, 22, out double indoArea, out double indoRain, out double indoBw, out double indoDes, out double indoArid, out double indoBreeze);
        Console.WriteLine($"  [Earth {earth.Width}] Mainland Indochina: rain={indoRain:F0} mm/yr, BW={indoBw:P2}, DesertBiome={indoDes:P2}, Arid={indoArid:P2}, SeaBreeze={indoBreeze:F2}");
        Check(indoArea > 0, "Mainland Indochina must have exposed land tiles");
        Check(indoBw == 0.0 && indoDes == 0.0, $"Southeast Asia / Mainland Indochina MUST have 0.0% desert, got BW={indoBw:P2}, Desert={indoDes:P2}");
        Check(indoRain >= 1380.0, $"Mainland Indochina annual rainfall too low: {indoRain:F1} mm");
        Check(indoBreeze >= 0.12, $"Mainland Indochina sea-breeze convergence guard failed to activate: {indoBreeze:F3}");

        // 2. Maritime Continent (Malay Peninsula, Sumatra, Java, Borneo, Sulawesi: 95..130E, -10..10N)
        EvaluateBox(earth, 95, 130, -10, 10, out double mcArea, out double mcRain, out double mcBw, out double mcDes, out double mcArid, out double mcBreeze);
        Console.WriteLine($"  [Earth {earth.Width}] Maritime Continent: rain={mcRain:F0} mm/yr, BW={mcBw:P2}, DesertBiome={mcDes:P2}, Arid={mcArid:P2}, SeaBreeze={mcBreeze:F2}");
        Check(mcArea > 0, "Maritime Continent must have exposed land tiles");
        Check(mcBw == 0.0 && mcDes == 0.0 && mcArid == 0.0, $"Maritime Continent MUST have 0.0% arid/desert tiles, got Arid={mcArid:P2}");
        Check(mcRain >= 1950.0, $"Maritime Continent annual rainfall too low: {mcRain:F1} mm");
        Check(mcBreeze >= 0.35, $"Maritime Continent sea-breeze guard too weak: {mcBreeze:F3}");

        // 3. Philippines (119..127E, 5..19N)
        EvaluateBox(earth, 119, 127, 5, 19, out double phArea, out double phRain, out double phBw, out double phDes, out double phArid, out _);
        Console.WriteLine($"  [Earth {earth.Width}] Philippines: rain={phRain:F0} mm/yr, BW={phBw:P2}, Arid={phArid:P2}");
        Check(phArea > 0 && phBw == 0.0 && phDes == 0.0 && phArid == 0.0, "Philippines must have 0.0% desert/arid tiles");
        Check(phRain >= 1950.0, $"Philippines annual rainfall too low: {phRain:F1} mm");

        // 4. Control check: Arabian Peninsula (44..54E, 19..28N) & Sahara (-10..28E, 20..30N) must remain arid/desert with near-zero sea-breeze guard!
        EvaluateBox(earth, 44, 54, 19, 28, out _, out double arabRain, out double arabBw, out _, out double arabArid, out double arabBreeze);
        EvaluateBox(earth, -10, 28, 20, 30, out _, out double sahRain, out double sahBw, out _, out _, out double sahBreeze);
        Check(arabBreeze < 0.03 && sahBreeze < 0.02, $"Subtropical deserts must not trigger sea-breeze guard: Arabia={arabBreeze:F3}, Sahara={sahBreeze:F3}");
        Check(arabArid >= 0.85 && arabBw >= 0.30 && sahBw >= 0.85 && sahRain < 85.0, $"Arabia/Sahara must remain hyper-arid/arid: Arabia Arid={arabArid:P1} (BW={arabBw:P1}), Sahara BW={sahBw:P1} ({sahRain:F1} mm)");
    }

    static void CheckRainModalSplitAndWaterBudget(WorldData earth)
    {
        var m = earth.Moisture;
        var exo = m?.ExoPlaSim;
        Check(exo != null, "ExoPlaSimSupplement must be populated on WorldData.Moisture");

        for (int month = 0; month < 12; month++)
            Check(m.BudgetError[month] <= 2e-5, $"Water mass budget drift in month {month}: {m.BudgetError[month]}");

        float maxModalMismatch = 0f;
        for (int i = 0; i < exo.ConvectiveRain.Length; i++)
        {
            Check(FMath.Finite(exo.ConvectiveRain[i]) && exo.ConvectiveRain[i] >= 0f, "Invalid convective rain");
            Check(FMath.Finite(exo.StratiformRain[i]) && exo.StratiformRain[i] >= 0f, "Invalid stratiform rain");
            maxModalMismatch = Math.Max(maxModalMismatch, Math.Abs((exo.ConvectiveRain[i] + exo.StratiformRain[i]) - m.Rain[i]));
        }
        Check(maxModalMismatch < 0.01f, $"Convective + Stratiform must equal total Rain, max diff={maxModalMismatch}");

        // Equatorial Borneo (114E, 0N, July) convective share vs Northern Europe / Siberia (60E, 62N, Jan) stratiform snow share
        int borneo = earth.Index((int)((114.0 + 180.0) / 360.0 * earth.Width), (int)((0.0 + 90.0) / 180.0 * earth.Height));
        int boreal = earth.Index((int)((60.0 + 180.0) / 360.0 * earth.Width), (int)((62.0 + 90.0) / 180.0 * earth.Height));
        var dBorneo = ExoPlaSimSupplement.SampleTileDiagnostic(earth, borneo, 6);
        var dBoreal = ExoPlaSimSupplement.SampleTileDiagnostic(earth, boreal, 0);
        Check(dBorneo.ConvectiveRainMm > dBorneo.StratiformRainMm, $"Equatorial rainforest should be convective-dominated: conv={dBorneo.ConvectiveRainMm:F1}, strat={dBorneo.StratiformRainMm:F1}");
        Check(dBoreal.StratiformRainMm > 4f * dBoreal.ConvectiveRainMm && dBoreal.SnowfallMm > 0.7f * dBoreal.StratiformRainMm,
            $"Boreal winter should be stratiform snow-dominated: conv={dBoreal.ConvectiveRainMm:F1}, strat={dBoreal.StratiformRainMm:F1}, snow={dBoreal.SnowfallMm:F1}");
    }

    static void CheckRadiationAlbedoAndDiurnalRange(WorldData earth)
    {
        var exo = earth.Moisture.ExoPlaSim;
        Console.WriteLine($"  Global energy diagnostics: NetSW={exo.GlobalMeanToaNetSolarWm2:F1} W/m2, OLR={exo.GlobalMeanOlrWm2:F1} W/m2, SurfAlbedo={exo.GlobalMeanAlbedo:F3}");
        Check(exo.GlobalMeanToaNetSolarWm2 > 195f && exo.GlobalMeanToaNetSolarWm2 < 275f, "Unrealistic global net shortwave");
        Check(exo.GlobalMeanOlrWm2 > 195f && exo.GlobalMeanOlrWm2 < 275f, "Unrealistic global OLR");

        int sahara = earth.Index((int)((12.0 + 180.0) / 360.0 * earth.Width), (int)((24.0 + 90.0) / 180.0 * earth.Height));
        int amazon = earth.Index((int)((-64.0 + 180.0) / 360.0 * earth.Width), (int)((-3.0 + 90.0) / 180.0 * earth.Height));
        int pacific = earth.Index((int)((-150.0 + 180.0) / 360.0 * earth.Width), (int)((10.0 + 90.0) / 180.0 * earth.Height));
        int antarctica = earth.Index((int)((90.0 + 180.0) / 360.0 * earth.Width), (int)((-78.0 + 90.0) / 180.0 * earth.Height));

        var dSah = ExoPlaSimSupplement.SampleTileDiagnostic(earth, sahara, 6);
        var dAmz = ExoPlaSimSupplement.SampleTileDiagnostic(earth, amazon, 6);
        var dPac = ExoPlaSimSupplement.SampleTileDiagnostic(earth, pacific, 6);
        var dAnt = ExoPlaSimSupplement.SampleTileDiagnostic(earth, antarctica, 6);

        Console.WriteLine($"  DTR (July): Sahara={dSah.DiurnalTempRangeC:F1} C, Amazon={dAmz.DiurnalTempRangeC:F1} C, Pacific={dPac.DiurnalTempRangeC:F1} C");
        Check(dSah.DiurnalTempRangeC > 13.0f && dSah.DiurnalTempRangeC > dAmz.DiurnalTempRangeC + 3.0f && dAmz.DiurnalTempRangeC > dPac.DiurnalTempRangeC + 2.5f,
            "Diurnal temperature range must satisfy Desert > Rainforest > Open Ocean");
        Check(dSah.SensibleHeatWm2 > dSah.LatentHeatWm2 && dAmz.LatentHeatWm2 > dAmz.SensibleHeatWm2,
            $"Bowen ratio mismatch: Sah SH/LH={dSah.SensibleHeatWm2:F1}/{dSah.LatentHeatWm2:F1}, Amz SH/LH={dAmz.SensibleHeatWm2:F1}/{dAmz.LatentHeatWm2:F1} (deepW={dAmz.DeepRootMoisture:F2})");
        Check(dAnt.SurfaceAlbedo > 0.60f && dSah.SurfaceAlbedo > dAmz.SurfaceAlbedo + 0.08f && dAmz.SurfaceAlbedo > dPac.SurfaceAlbedo,
            $"Albedo hierarchy violated: Ant={dAnt.SurfaceAlbedo:F2}, Sah={dSah.SurfaceAlbedo:F2}, Amz={dAmz.SurfaceAlbedo:F2}, Pac={dPac.SurfaceAlbedo:F2}");
    }

    static void CheckStormClimatologyMpiAndGpi(WorldData earth)
    {
        // Philippine Sea warm pool (134E, 16N, August m=7) vs Peru Humboldt cold upwelling (-78E, -15S, August)
        int philSea = earth.Index((int)((134.0 + 180.0) / 360.0 * earth.Width), (int)((16.0 + 90.0) / 180.0 * earth.Height));
        int peruSea = earth.Index((int)((-78.0 + 180.0) / 360.0 * earth.Width), (int)((-15.0 + 90.0) / 180.0 * earth.Height));
        int northAtlHigh = earth.Index((int)((-30.0 + 180.0) / 360.0 * earth.Width), (int)((56.0 + 90.0) / 180.0 * earth.Height));

        var dPhil = ExoPlaSimSupplement.SampleTileDiagnostic(earth, philSea, 7);
        var dPeru = ExoPlaSimSupplement.SampleTileDiagnostic(earth, peruSea, 7);
        var dSubpolar = ExoPlaSimSupplement.SampleTileDiagnostic(earth, northAtlHigh, 7);

        Console.WriteLine($"  Storm MPI/CAPE/GPI (Aug): PhilippineSea MPI={dPhil.MaxPotentialIntensityMs:F1} m/s CAPE={dPhil.CapeJkg:F0} GPI={dPhil.GenesisPotentialIndex:F2} | PeruSea MPI={dPeru.MaxPotentialIntensityMs:F1} GPI={dPeru.GenesisPotentialIndex:F3}");
        Check(dPhil.MaxPotentialIntensityMs > 55f && dPhil.CapeJkg > 1100f && dPhil.GenesisPotentialIndex > 0.15f,
            "Philippine Sea warm pool must support strong typhoon MPI, CAPE, and Genesis GPI");
        Check(dPeru.GenesisPotentialIndex < 0.02f && dSubpolar.GenesisPotentialIndex == 0f,
            "Cold upwelling and subpolar oceans must suppress tropical cyclogenesis");
    }

    static void CheckPastaBioclimateAndMapmodes(WorldData earth)
    {
        var seenPasta = new HashSet<PastaClimateKind>();
        for (int i = 0; i < earth.Count; i++)
        {
            var d = ExoPlaSimSupplement.SampleTileDiagnostic(earth, i, 6);
            seenPasta.Add(d.PastaClimate);
        }
        Console.WriteLine($"  Pasta bioclimate diversity on Earth 256: {seenPasta.Count}/19 classes");
        Check(seenPasta.Count >= 14, $"Expected >= 14 Pasta bioclimate classes on Earth, got {seenPasta.Count}");

        MapLayer[] layers =
        {
            MapLayer.ExoConvectiveRain,
            MapLayer.ExoRadiationOlr,
            MapLayer.ExoDiurnalRange,
            MapLayer.ExoStormPotential,
            MapLayer.ExoPastaClimate
        };
        foreach (var layer in layers)
        {
            int nonUniform = 0;
            byte r0 = 0, g0 = 0, b0 = 0;
            for (int i = 0; i < earth.Count; i += 17)
            {
                ExoPlaSimSupplement.EvaluateMapmodeColor(earth, i, 6, ExoPlaSimSupplement.MapLayerToExoMode(layer), out byte r, out byte g, out byte b);
                if (i == 0) { r0 = r; g0 = g; b0 = b; }
                else if (r != r0 || g != g0 || b != b0) nonUniform++;
            }
            Check(nonUniform > 50, $"ExoPlaSim mapmode {layer} produced degenerate uniform colors");
        }
    }

    static void CheckPlanetaryPresetsAndEyeball()
    {
        var baseWorld = FlatGenerator.Generate(128, 64, "EXO-PLANET-TEST", TerrainPreset.Continents, default);
        double MeanTemp(WorldData w)
        {
            double s = 0, wtSum = 0;
            for (int y = 0; y < w.Height; y++)
            {
                double wt = Math.Cos(((y + 0.5) / w.Height - 0.5) * Math.PI);
                for (int x = 0; x < w.Width; x++)
                {
                    int i = y * w.Width + x;
                    wtSum += wt * 12.0;
                    for (int m = 0; m < 12; m++) s += wt * w.Temperature[i * 12 + m];
                }
            }
            return s / wtSum;
        }

        var eocene = baseWorld.CloneForTerrainRebuild();
        eocene.PlanetClimate = ExoPlaSimPlanetConfig.CreatePreset(ExoPlaSimPreset.WarmGreenhouseEocene);
        FlatGenerator.Rebuild(eocene, default);

        var glacial = baseWorld.CloneForTerrainRebuild();
        glacial.PlanetClimate = ExoPlaSimPlanetConfig.CreatePreset(ExoPlaSimPreset.IcehouseGlacial);
        FlatGenerator.Rebuild(glacial, default);

        var eyeball = baseWorld.CloneForTerrainRebuild();
        eyeball.PlanetClimate = ExoPlaSimPlanetConfig.CreatePreset(ExoPlaSimPreset.TidallyLockedEyeball);
        FlatGenerator.Rebuild(eyeball, default);

        double tModern = MeanTemp(baseWorld), tEocene = MeanTemp(eocene), tGlacial = MeanTemp(glacial);
        Console.WriteLine($"  Planetary global mean T: Icehouse={tGlacial:F2} C, Modern={tModern:F2} C, Eocene(1400ppm)={tEocene:F2} C");
        Check(tEocene > tModern + 3.0 && tGlacial < tModern - 1.2, "Greenhouse CO2 presets failed to warm/cool planet monotonically");

        // Tidally-locked eyeball check: substellar point (15E, 0N) much warmer than antistellar nightside (-165E, 0N)
        int eqRow = eyeball.Height / 2;
        int dayTile = eyeball.Index((int)((15.0 + 180.0) / 360.0 * eyeball.Width), eqRow);
        int nightTile = eyeball.Index((int)((-165.0 + 180.0) / 360.0 * eyeball.Width), eqRow);
        float tDay = eyeball.Temperature[dayTile * 12], tNight = eyeball.Temperature[nightTile * 12];
        var dDay = ExoPlaSimSupplement.SampleTileDiagnostic(eyeball, dayTile, 0);
        Console.WriteLine($"  Tidally-locked Eyeball: Substellar={tDay:F1} C, Antistellar={tNight:F1} C, Land/Sea DTR={dDay.DiurnalTempRangeC:F2} C");
        Check(tDay > tNight + 25.0f && dDay.DiurnalTempRangeC < 2.6f, "Tidally-locked eyeball planet failed substellar/nightside contrast or DTR suppression");
    }
}
