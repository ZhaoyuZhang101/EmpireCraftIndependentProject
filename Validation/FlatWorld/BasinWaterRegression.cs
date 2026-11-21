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

    static void MajorLakes(WorldData w)
    {
        // Interior points of the Natural Earth 1:10m polygons. The narrow lakes
        // in the second group first have majority-water cells on the 512 grid.
        void Lake(string name, double lon, double lat, int level)
        {
            int i = Tile(w, lon, lat);
            Check(w.Water[i] == WaterKind.Lake && w.WaterLevel[i] == level &&
                w.Elevation[i] <= w.WaterLevel[i],
                $"Earth {w.Width} {name} ({lon}, {lat}) has {w.Water[i]} at {w.WaterLevel[i]} m on a {w.Elevation[i]} m bed.");
        }
        Lake("Superior", -86.5, 47.2, 193);
        Lake("Michigan", -86.9, 43, 180);
        Lake("Huron", -82.6, 44.65, 179);
        Lake("Baikal", 108.63, 53.79, 464);
        Lake("Great Bear", w.Width == 256 ? -121.64 : -121.29, w.Width == 256 ? 65.39 : 66.445, 156);
        Lake("Great Slave", w.Width == 256 ? -114.60 : -114.26, w.Width == 256 ? 61.17 : 61.50, 156);
        Lake("Victoria", 33, -1, 1135);
        foreach (var site in new[] { ("Erie", -81.9, 41.85, 175), ("Ontario", -77.7, 43.9, 86),
            ("Tanganyika", 29.88, -6.68, 771), ("Malawi", 34.8, -11.6, 475),
            ("Titicaca", -69.26, -15.82, 3809), ("Qinghai", 100.19, 36.91, 3197) })
        {
            if (w.Width >= 512) Lake(site.Item1, site.Item2, site.Item3, site.Item4);
            else
            {
                int i = Tile(w, site.Item2, site.Item3);
                Check(!FlatTerrainAnalysis.AreaWater(w.Water[i]) && w.WaterLevel[i] == w.Elevation[i],
                    $"Earth {w.Width} promoted the minority lake area at {site.Item1} to a full water tile.");
            }
        }
        foreach (var site in new[] { ("Minnesota", -93.5, 45.9), ("Ontario uplands", -79.8, 45.2),
            ("East African uplands", 36.6, -2.3), ("west of Baikal", 105.4, 54.3),
            ("between Great Bear and Slave", -115.45, 63.8), ("south of Qinghai", 100.9, 35.8),
            ("Altiplano", -68.1, -17.5), ("exposed Aral bed", 60.1, 45.5) })
        {
            int i = Tile(w, site.Item2, site.Item3);
            Check(!FlatTerrainAnalysis.AreaWater(w.Water[i]) && w.WaterLevel[i] == w.Elevation[i],
                $"Earth {w.Width} flooded {site.Item1}.");
        }
    }

    static void SaveBaselines(WorldData current, byte[] bed, byte[] water, byte[] rivers)
    {
        // OWR2 retains the original OWR1 classification spans before its sparse bed list.
        int spansEnd = 12 + 6 * BitConverter.ToInt32(water, 8);
        byte[] oldSurvey = water.AsSpan(0, spansEnd).ToArray();
        oldSurvey[3] = (byte)'1';
        void Reject(byte[] data)
        {
            try { FlatWaterReference.Read(current.Width, bed, data); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Accepted corrupt surveyed lake-bed indices.");
        }
        Reject(water.AsSpan(0, water.Length - 1).ToArray());
        var duplicate = (byte[])water.Clone();
        Array.Copy(duplicate, spansEnd + 4, duplicate, spansEnd + 8, 4);
        Reject(duplicate);
        var outsideGrid = (byte[])water.Clone();
        BitConverter.GetBytes(current.Count).CopyTo(outsideGrid, spansEnd + 4);
        Reject(outsideGrid);
        var legacy = FlatGenerator.Generate(current.Width, current.Height, current.Seed, TerrainPreset.Earth,
            default, bed, oldSurvey);
        int bear = Tile(current, -121.64, 65.39);
        Check(legacy.Elevation[bear] == 212 && !FlatTerrainAnalysis.AreaWater(legacy.Water[bear]) &&
            current.Elevation[bear] == 156 && current.Water[bear] == WaterKind.Lake,
            "Legacy and lake-aware Earth maps must retain distinct terrain at Great Bear Lake.");
        const int versionOffset = 13 + sizeof(int) + 32;
        var legacyFile = FlatSave.Encode(legacy);
        var loadedOld = FlatSave.Decode(legacyFile);
        FlatEarthRiverReference.Restore(loadedOld, bed, water, rivers, default);
        Check(BitConverter.ToInt32(legacyFile, versionOffset) == 3 && !loadedOld.UsesSurveyedLakeBeds &&
            loadedOld.WaterReference.MatchesOriginalBed(bear, 212) && !loadedOld.WaterReference.MatchesOriginalBed(bear, 156) &&
            legacyFile.SequenceEqual(FlatSave.Encode(loadedOld)), "A pre-update Earth save lost its original bed baseline.");
        FlatGenerator.Rebuild(loadedOld, default);
        Check(loadedOld.Elevation.SequenceEqual(legacy.Elevation) && loadedOld.Water.SequenceEqual(legacy.Water) &&
            loadedOld.WaterLevel.SequenceEqual(legacy.WaterLevel) && loadedOld.Downstream.SequenceEqual(legacy.Downstream),
            "Rebuilding an unedited old Earth save changed its water or drainage.");

        // A player can raise the new bed to exactly the height of the previous DEM.
        var edited = current.Clone();
        edited.Elevation[bear] = 212;
        FlatGenerator.Rebuild(edited, default);
        var editedFile = FlatSave.Encode(edited);
        var loadedNew = FlatSave.Decode(editedFile);
        FlatEarthRiverReference.Restore(loadedNew, bed, water, rivers, default);
        Check(BitConverter.ToInt32(editedFile, versionOffset) == 4 && loadedNew.UsesSurveyedLakeBeds &&
            loadedNew.WaterReference.MatchesOriginalBed(bear, 156) && !loadedNew.WaterReference.MatchesOriginalBed(bear, 212) &&
            editedFile.SequenceEqual(FlatSave.Encode(loadedNew)), "The alternate unedited baseline disguised a real terrain edit.");
        FlatGenerator.Rebuild(loadedNew, default);
        Check(loadedNew.Elevation[bear] == 212 && !FlatTerrainAnalysis.AreaWater(loadedNew.Water[bear]) &&
            edited.Water.SequenceEqual(loadedNew.Water) && edited.WaterLevel.SequenceEqual(loadedNew.WaterLevel),
            "Restoring the surveyed source reflooded the player's raised bed.");
    }

    static void EarthSurfaces(int width)
    {
        const string folder = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        byte[] bed = File.ReadAllBytes(folder + "Earth" + width + ".bytes");
        byte[] water = File.ReadAllBytes(folder + "EarthWater" + width + ".bytes");
        var w = FlatGenerator.Generate(width, width / 2, "EARTH-REGRESSION", TerrainPreset.Earth, default, bed, water);
        MajorLakes(w);
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
        Console.WriteLine($"Earth {width}: {(width == 256 ? 8 : 14)} major lakes including Caspian {w.WaterLevel[caspian]} m, " +
            $"10 dry controls, Mediterranean, Black and Baltic marine; annual runoff residual {Math.Abs(inputs-outputs):G4}/{inputs:F2}.");
        if (width == 256)
        {
            byte[] original = FlatSave.Encode(w);
            var loaded = FlatSave.Decode(original);
            Check(loaded.Water.SequenceEqual(w.Water) && loaded.WaterLevel.SequenceEqual(w.WaterLevel) &&
                loaded.Downstream.SequenceEqual(w.Downstream), "Saving changed the authoritative basin state.");
            byte[] rivers = File.ReadAllBytes(folder + "EarthRivers.bytes");
            SaveBaselines(w, bed, water, rivers);
            FlatEarthRiverReference.Restore(loaded, bed, water, rivers, default);
            Check(original.SequenceEqual(FlatSave.Encode(loaded)), "Restoring an unedited lake-aware Earth save changed its bytes.");
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

    static void ChineseLakeCells()
    {
        const string folder = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        foreach (int width in new[] { 1024, 1536, 2048 })
        {
            byte[] bed = File.ReadAllBytes(folder + "Earth" + width + ".bytes");
            byte[] water = File.ReadAllBytes(folder + "EarthWater" + width + ".bytes");
            var current = FlatWaterReference.Read(width, bed, water);
            var legacy = FlatWaterReference.Read(width, bed, water, false);
            void Site(string name, double lon, double lat, int stage, bool majority)
            {
                int i = (int)((lat + 90) * width / 360) * width + (int)((lon + 180) * width / 360);
                Check(legacy.At(i) == FlatWaterReference.Dry, "Changed pre-survey lake coverage at " + name);
                Check(current.At(i) == (majority ? stage : FlatWaterReference.Dry), $"Earth {width} {name} surface");
                int sample = ((width / 2 - 1 - i / width) * width + i % width) * 2;
                short original = BitConverter.ToInt16(bed, sample);
                Check(legacy.OriginalBed(i) == original &&
                    current.OriginalBed(i) == (majority ? Math.Min((int)original, stage) : original),
                    $"Earth {width} {name} bed or legacy baseline");
            }
            Site("Sayram", 81.123, 44.5605, 2072, width >= 1536);
            Site("Dianchi", 102.744, 24.873, 1886, width == 2048);
            Site("Erhai", 100.182, 25.783, 1962, false);
            Site("Fuxian", 102.887, 24.494, 1721, false);
            Site("ridge between Dianchi and Fuxian", 102.823, 24.662, 0, false);
            if (width == 2048)
            {
                Site("east Sayram", 81.2988, 44.5605, 2072, true);
                void RejectExtra(byte[] damaged)
                {
                    try { FlatWaterReference.Read(width, bed, damaged); }
                    catch (InvalidDataException) { return; }
                    throw new InvalidOperationException("Accepted a corrupt supplemental lake record.");
                }
                RejectExtra(water.AsSpan(0, water.Length - 1).ToArray());
                var duplicate = (byte[])water.Clone();
                Array.Copy(duplicate, duplicate.Length - 12, duplicate, duplicate.Length - 6, sizeof(int));
                RejectExtra(duplicate);
                var badLevel = (byte[])water.Clone();
                BitConverter.GetBytes(FlatWaterReference.Dry).CopyTo(badLevel, badLevel.Length - 2);
                RejectExtra(badLevel);
            }
        }
        Console.WriteLine("PASS Chinese lake coverage by cell area, source reference stages and legacy beds");
    }

    public static void Run()
    {
        DryAndWet();
        SurveyedPassage();
        EarthSurfaces(256);
        EarthSurfaces(512);
        EarthSurfaces(1024);
        ChineseLakeCells();
        Console.WriteLine("PASS dry internal sinks, wet spill lakes, ocean-only planet, one-cell strait and dam across the longitude seam");
    }
}
