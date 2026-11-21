using System;
using System.IO;
using System.Linq;
using Orbis.Flat;

internal static class BasinWaterRegression
{
    static void Check(bool ok, string reason)
    {
        if (!ok) throw new InvalidOperationException(reason);
    }

    static WorldData Basin(bool coast, float rain, short floor)
    {
        var w = new WorldData(64, 32, "CLOSED-DRAINAGE", TerrainPreset.Continents);
        for (int i = 0; i < w.Count; i++)
        {
            int x = i % w.Width, y = i / w.Width;
            bool sea = coast && (x <= 3 || x >= 60);
            bool basin = x >= 29 && x <= 34 && y >= 14 && y <= 17;
            w.Elevation[i] = sea ? (short)-300 : basin ? floor : (short)100;
            w.Water[i] = sea ? WaterKind.MiddleOcean : WaterKind.Land;
            for (int m = 0; m < 12; m++)
            {
                w.Temperature[i * 12 + m] = 19;
                w.Precipitation[i * 12 + m] = rain;
            }
        }
        return w;
    }

    static void DryAndWet()
    {
        var dry = Basin(true, 0, -12);
        var wet = Basin(true, 240, -12);
        FlatHydrology.Build(dry, default);
        FlatHydrology.Build(wet, default);
        int center = dry.Index(32, 15);
        Check(dry.Water[center] == WaterKind.Land && dry.WaterLevel[center] == dry.Elevation[center],
            "The dry below-datum basin rendered a standing spill surface without any water supply.");
        int end = center;
        while (dry.Downstream[end] >= 0) end = dry.Downstream[end];
        Check(!FlatTerrainAnalysis.Ocean(dry.Water[end]) && dry.Elevation[end] == -12,
            "Water in the dry sink climbed a 112 m sill to the sea.");
        Check(wet.Water[center] == WaterKind.Lake && wet.WaterLevel[center] == 100,
            "A supplied basin failed to fill and discharge at its actual sill.");
        var partial = Basin(true, 0, 20);
        partial.Elevation[center] = -35;
        for (int m = 0; m < 12; m++) partial.Precipitation[center * 12 + m] = 300;
        FlatHydrology.Build(partial, default);
        Check(partial.Water[center] == WaterKind.Lake && partial.WaterLevel[center] == 20 &&
            !FlatTerrainAnalysis.AreaWater(partial.Water[center - 1]),
            "A limited supply must sustain the deep pocket while leaving its broad evaporating shelf exposed.");
        var noOcean = Basin(false, 0, -35);
        FlatGenerator.Rebuild(noOcean, default);
        Check(!noOcean.Water.Any(FlatTerrainAnalysis.Ocean),
            "An isolated low patch in an otherwise entirely terrestrial DEM was promoted to a world ocean.");
        var sea = Basin(false, 0, -35);
        Array.Fill(sea.Elevation, (short)-100);
        FlatGenerator.Rebuild(sea, default);
        Check(sea.Water.All(k => k == WaterKind.ShallowOcean), "An ocean-only planet lost its ocean.");
        var channel = Basin(true, 0, -12);
        for (int x = 3; x < 30; x++) channel.Elevation[channel.Index(x, 15)] = -1;
        FlatGenerator.Rebuild(channel, default);
        Check(FlatTerrainAnalysis.Ocean(channel.Water[center]) &&
            FlatTerrainAnalysis.Ocean(channel.Water[channel.Index(63, 15)]),
            "Sea-level water failed to reach the inner sea through a one-face strait and the longitude seam.");
        channel.Elevation[channel.Index(17, 15)] = 100;
        FlatGenerator.Rebuild(channel, default);
        Check(!FlatTerrainAnalysis.Ocean(channel.Water[center]),
            "Closing the only strait failed to separate inland water from the ocean.");
    }

    static void SurveyedPassage()
    {
        var w = Basin(true, 0, -12);
        var hint = new short[w.Count];
        Array.Fill(hint, FlatWaterReference.Dry);
        for (int i = 0; i < w.Count; i++) if (w.Elevation[i] < 0) hint[i] = FlatWaterReference.Marine;
        int shelf = w.Index(1, 15), dryPlain = w.Index(4, 10), dam = w.Index(17, 15);
        w.Elevation[shelf] = w.Elevation[dryPlain] = 0;
        for (int x = 4; x < 29; x++)
        {
            int i = w.Index(x, 15);
            hint[i] = FlatWaterReference.Passage;
            w.Elevation[i] = 40;
        }
        int protectedBasin = w.Index(8, 10);
        w.Elevation[protectedBasin] = -12;
        var rawBed = new byte[w.Count * 2];
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x3152574f); writer.Write(w.Width); writer.Write(w.Count);
            for (int j = 0; j < w.Count; j++)
            {
                int i = w.Index(j % w.Width, w.Height - 1 - j / w.Width);
                rawBed[j * 2] = (byte)w.Elevation[i];
                rawBed[j * 2 + 1] = (byte)(w.Elevation[i] >> 8);
                writer.Write(hint[i]); writer.Write(1);
            }
            w.WaterReference = FlatWaterReference.Read(w.Width, rawBed, stream.ToArray());
        }
        FlatGenerator.Rebuild(w, default);
        Check(FlatTerrainAnalysis.Ocean(w.Water[w.Index(32, 15)]) && !FlatTerrainAnalysis.AreaWater(w.Water[dam]),
            "The surveyed strait must convey marine connectivity while its positive average cell remains exposed.");
        Check(FlatTerrainAnalysis.Ocean(w.Water[shelf]) && !FlatTerrainAnalysis.AreaWater(w.Water[dryPlain]),
            "A marine shelf and neighbouring dry land at the same zero datum must retain their water provenance.");
        Check(!FlatTerrainAnalysis.AreaWater(w.Water[protectedBasin]), "The surveyed inland dry floor was flooded.");
        var edited = w.Clone();
        edited.Elevation[dam] = 110;
        for (int x = 4; x < 8; x++) edited.Elevation[w.Index(x, 10)] = -2;
        FlatGenerator.Rebuild(edited, default);
        Check(!FlatTerrainAnalysis.Ocean(edited.Water[w.Index(32, 15)]),
            "The original marine polygon overrode a dam across its only positive-average strait cell.");
        Check(FlatTerrainAnalysis.Ocean(edited.Water[protectedBasin]),
            "Sea-level water failed to enter an original dry lowland through a newly excavated canal.");
    }

    static int Tile(WorldData w, double lon, double lat)
    {
        return w.Index((int)((lon + 180) / 360 * w.Width), (int)((lat + 90) / 180 * w.Height));
    }

    static void EarthSurfaces(int width)
    {
        const string folder = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        byte[] bed = File.ReadAllBytes(folder + "Earth" + width + ".bytes");
        byte[] water = File.ReadAllBytes(folder + "EarthWater" + width + ".bytes");
        var w = FlatGenerator.Generate(width, width / 2, "EARTH-REGRESSION", TerrainPreset.Earth, default, bed, water);
        foreach (var site in new[] { ("Tarim", 82.5, 39.0), ("Qaidam", 94.0, 36.7) })
        {
            int i = Tile(w, site.Item2, site.Item3);
            Check(!FlatTerrainAnalysis.AreaWater(w.Water[i]) && w.Facts.ShoreDistance[i] < 0 &&
                w.WaterLevel[i] == w.Elevation[i], site.Item1 + " basin floor covered by standing water.");
        }
        foreach (var site in new[] { ("Mediterranean", 19.0, 35.0), ("Black Sea", 34.0, 43.0), ("Baltic", 19.0, 57.0) })
        {
            int i = Tile(w, site.Item2, site.Item3);
            Check(FlatTerrainAnalysis.Ocean(w.Water[i]) && w.WaterLevel[i] == 0 && w.CoastDistance[i] == 0 &&
                w.Facts.ShoreDistance[i] > 0, site.Item1 + " lost its connection to the world ocean.");
        }
        int caspian = Tile(w, 51, 38), terminal = caspian;
        while (w.Downstream[terminal] >= 0) terminal = w.Downstream[terminal];
        Check(w.Water[caspian] == WaterKind.Lake && w.WaterLevel[caspian] >= -32 && w.WaterLevel[caspian] <= -24 &&
            !FlatTerrainAnalysis.Ocean(w.Water[terminal]), "The Caspian must be a closed inland waterbody below the marine datum.");
        var local = new float[w.Count];
        FlatRiverDischarge.BuildSources(w, default, local);
        double inputs = local.Sum(v => (double)v), outputs = 0;
        for (int i = 0; i < w.Count; i++) if (w.Downstream[i] < 0) outputs += w.Runoff[i];
        Check(Math.Abs(inputs - outputs) < 1e-5 * Math.Max(1, inputs), "Runoff lost at a closed or marine outlet.");
        Console.WriteLine($"Earth {width}: Tarim/Qaidam dry; Caspian {w.WaterLevel[caspian]} m; " +
            $"Mediterranean, Black and Baltic marine; annual runoff residual {Math.Abs(inputs-outputs):G4}/{inputs:F2}.");
        if (width == 256)
        {
            var loaded = FlatSave.Decode(FlatSave.Encode(w));
            Check(loaded.Water.SequenceEqual(w.Water) && loaded.WaterLevel.SequenceEqual(w.WaterLevel) &&
                loaded.Downstream.SequenceEqual(w.Downstream), "Saving changed the authoritative basin state.");
            loaded.WaterReference = FlatWaterReference.Read(width, bed, water);
            FlatGenerator.Rebuild(loaded, default);
            Check(loaded.Water.SequenceEqual(w.Water) && loaded.Downstream.SequenceEqual(w.Downstream),
                "Restoring the source and rebuilding changed the unedited water map.");
            int endorheic = Tile(loaded, 82.5, 39), nearby = endorheic + 1;
            loaded.Elevation[endorheic] -= 10;
            loaded.Elevation[caspian] -= 10;
            FlatGenerator.Rebuild(loaded, default);
            Check(!FlatTerrainAnalysis.AreaWater(loaded.Water[nearby]) && loaded.WaterLevel[caspian] == w.WaterLevel[caspian],
                "A local excavation refilled the surrounding Tarim floor or changed the stage of the entire Caspian.");
        }
    }

    public static void Run()
    {
        DryAndWet();
        SurveyedPassage();
        EarthSurfaces(256);
        EarthSurfaces(512);
        Console.WriteLine("PASS dry internal sinks, wet spill lakes, ocean-only planet, one-cell strait and dam across the longitude seam");
    }
}
