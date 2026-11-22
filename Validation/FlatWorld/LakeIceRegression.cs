using System;
using System.IO;
using Orbis.Flat;

internal static class LakeIceRegression
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void CheckSeasons()
    {
        var w = new WorldData(64, 32, "LAKE-ICE", TerrainPreset.Continents);
        for (int i = 0; i < w.Count; i++)
        {
            w.DrainOrder[i] = i;
            w.Climates[i] = Koppen.Dfb;
            w.Biomes[i] = Biome.Grassland;
            for (int m = 0; m < 12; m++)
            {
                int month = i / w.Width < w.Height / 2 ? (m + 6) % 12 : m;
                w.Temperature[i * 12 + m] = month >= 1 && month <= 2 ? -12 : 14;
            }
        }
        int north = w.Index(20, 24), south = w.Index(20, 7), warm = w.Index(18, 24);
        w.Water[north] = w.Water[south] = w.Water[warm] = WaterKind.Lake;
        w.Biomes[north] = w.Biomes[south] = w.Biomes[warm] = Biome.Water;
        w.WaterLevel[north] = w.WaterLevel[south] = w.WaterLevel[warm] = 10;
        for (int m = 0; m < 12; m++) w.Temperature[warm * 12 + m] = 9;
        FlatTerrainAnalysis.Build(w, default, false);
        var field = w.Facts.LakeIce;
        float At(int tile, double day) => field.FilteredAirTemperature(
            (tile % w.Width + .5f) / w.Width, (tile / w.Width + .5f) / w.Height, day);
        Check(At(north, 31) > 9 && At(north, 60) < -3 && At(north, 89) < -7,
            "An isolated cold day should leave a previously warm lake open; sustained cold must freeze it.");
        Check(At(north, 90.1) < -7 && At(north, 114) > 2,
            "The spring thaw should take time after a sub-zero month.");
        Check(At(south, 75) > 9 && At(south, 255) < -6 && At(north, 255) > 9,
            "Reversed monthly normals did not reverse the winter phase.");
        for (int d = 0; d < 720; d++)
            Check(At(warm, d) > 5, "A warm lake froze at the same latitude as a cold lake.");
        FlatEcology.SampleSurfaceSeason(w, north, 75, out _, out _, out float snow, out _);
        Check(snow == 0, "Frozen lake water was put into the ground snow channel.");
        for (int day = -360; day <= 720; day += 30)
            Check(Math.Abs(At(north, day - .0001) - At(north, day + .0001)) < .002,
                $"Cooling jumped at a month, panel or year boundary on day {day}.");
        Check(Math.Abs(field.FilteredAirTemperature(0, .4f, 78) - field.FilteredAirTemperature(1, .4f, 78)) < .001,
            "The date line introduced a lake ice seam.");
        var recovered = FlatSave.Decode(FlatSave.Encode(w));
        Check(At(north, 71) == recovered.Facts.LakeIce.FilteredAirTemperature(20.5f / 64, 24.5f / 32, 71),
            "Save and reload failed to reproduce the same cooling history.");
        Console.WriteLine("PASS thermal lag, spring thaw, both hemispheres, warm control, ground snow, month/year wrap and save/load.");
    }

    static void CheckSampling()
    {
        var w = new WorldData(1032, 516, "LAKE-ICE-SPATIAL", TerrainPreset.Continents);
        // The first downsampled tile covers these four model tiles.
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                for (int m = 0; m < 12; m++)
                    w.Temperature[w.Index(x, y) * 12 + m] = x * 4 + y * 8;
        var field = new FlatLakeIceField(w, default, 1);
        Check(field.Width == 516 && field.Height == 258 && field.Packed.Length == 12 * 516 * 258,
            "The large-world climate texture lost its 2:1 footprint or packed-month memory bound.");
        for (int panel = 0; panel < 3; panel++)
            for (int component = 0; component < 4; component++)
                Check(field.Packed[panel * field.Width * field.Height * 4 + component] == 152,
                    "The four source climates did not average to 6 C in every monthly RGBA channel.");
        Console.WriteLine("PASS large-world packed atlas, sub-tile averaging and all twelve channels.");
    }

    static void Earth(int width)
    {
        const string directory = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        var w = FlatGenerator.Generate(width, width / 2, "ORBIS-2D-2025", TerrainPreset.Earth, default,
            File.ReadAllBytes(directory + $"Earth{width}.bytes"), File.ReadAllBytes(directory + $"EarthWater{width}.bytes"),
            File.ReadAllBytes(directory + "EarthRivers.bytes"));
        float At(float lon, float lat, double day) =>
            w.Facts.LakeIce.FilteredAirTemperature((lon + 180) / 360, (lat + 90) / 180, day);
        void Site(string name, float lon, float lat, bool warm)
        {
            int i = w.Index((int)((lon + 180) * width / 360), (int)((lat + 90) * w.Height / 180));
            float winter = At(lon, lat, 45), summer = At(lon, lat, 225);
            Console.WriteLine($"{name} {lon:F2}E {lat:F2}N ({w.Water[i]}). Filtered air " +
                $"days 45/120/127/135/225/330/345: {winter:F1}, {At(lon, lat, 120):F1}, " +
                $"{At(lon, lat, 127):F1}, {At(lon, lat, 135):F1}, {summer:F1}, " +
                $"{At(lon, lat, 330):F1}, {At(lon, lat, 345):F1} C.");
            Check(summer > 5 && (warm ? winter > 3 : winter < -5.5f), name + " has incorrect winter/summer cooling.");
        }
        Site("Baikal", 108.2f, 53.1f, false);
        Site("Khanka", 132.4f, 45f, false);
        Site("Ladoga", 31.25f, 60.86f, false);
        Site("Taihu near Shanghai", 120.127f, 31.195f, true);
        Site("Shanghai Dianshan", 120.97f, 31.10f, true);
        Console.WriteLine($"PASS current Earth{width} lake climate.");
    }

    static void ProceduralSouthernLake()
    {
        var w = FlatGenerator.Generate(256, 128, "ORBIS-2D-2025", TerrainPreset.Continents, default);
        int tile = w.Index(40, 21);
        const float u = 40.5f / 256, v = 21.5f / 128;
        float summer = w.Facts.LakeIce.FilteredAirTemperature(u, v, 45);
        float winter = w.Facts.LakeIce.FilteredAirTemperature(u, v, 225);
        Check(w.Water[tile] == WaterKind.Lake && summer > 5 && winter < -5.5f,
            "Generated southern lake at 123.05 W, 59.77 S did not have a summer thaw and winter freeze.");
        Console.WriteLine($"PASS procedural southern lake, -123.05 E -59.77 N: February {summer:F1}, August {winter:F1} C.");
    }

    internal static void Run(string[] args)
    {
        CheckSeasons();
        CheckSampling();
        ProceduralSouthernLake();
        if (args.Length > 1) Earth(int.Parse(args[1]));
    }
}
