using System;
using System.IO;
using Orbis.Flat;

internal static class SeasonalTemperatureRegression
{
    static WorldData Uniform(int width, bool ocean, short elevation)
    {
        var world = new WorldData(width, width/2, "SEASONAL-HEAT", TerrainPreset.Continents);
        for (int i = 0; i < world.Count; i++)
        {
            world.Water[i] = ocean ? WaterKind.DeepOcean : WaterKind.Land;
            world.Elevation[i] = elevation;
            world.WaterLevel[i] = ocean ? (short)0 : elevation;
            world.CoastDistance[i] = ocean ? (ushort)0 : ushort.MaxValue;
        }
        FlatClimate.BuildTemperature(world, FlatWorkerPolicy.Options(default));
        return world;
    }

    static double SemiannualAmplitude(WorldData world, int cell)
    {
        double cosine = 0, sine = 0;
        for (int m = 0; m < 12; m++)
        {
            double phase = m * Math.PI / 3;
            cosine += world.Temperature[cell*12 + m] * Math.Cos(phase) / 6;
            sine += world.Temperature[cell*12 + m] * Math.Sin(phase) / 6;
        }
        return Math.Sqrt(cosine*cosine + sine*sine);
    }

    public static void Run()
    {
        CheckPremonsoonHeat();
        foreach (int width in new[] {64, 128})
        {
            var land = Uniform(width, false, 200);
            var ocean = Uniform(width, true, -3000);
            var plateau = Uniform(width, false, 1200);
            int tropics = land.Index(0, land.Height/2);
            double twiceYearly = SemiannualAmplitude(land, tropics);
            if (twiceYearly <= .4 || SemiannualAmplitude(ocean, tropics) >= twiceYearly*.5)
                throw new Exception("Low-latitude land should resolve the two annual passages of the sun more strongly than the ocean.");
            int north = (int)((41 + 90)/180.0 * land.Height);
            foreach (int row in new[] {north, land.Height - 1 - north})
            {
                int i = land.Index(0, row) * 12;
                int summer = row == north ? 6 : 0, winter = (summer + 6) % 12;
                float landSwing = land.Temperature[i + summer] - land.Temperature[i + winter];
                float oceanSwing = ocean.Temperature[i + summer] - ocean.Temperature[i + winter];
                if (oceanSwing <= 0 || landSwing <= 1.5 * oceanSwing)
                    throw new Exception("A land surface must respond more strongly to the local summer than a deep ocean.");
                for (int m = 0; m < 12; m++)
                {
                    float lapse = land.Temperature[i+m] - plateau.Temperature[i+m];
                    if (lapse < 2.5 || lapse > 8)
                        throw new Exception("Lifting a level continent by 1 km must cool the monthly air.");
                }
            }
        }
        Console.WriteLine("PASS equatorial semiannual heating, hemispheric seasons, marine thermal storage and plateau lapse at 64/128 columns.");
    }

    private static void CheckPremonsoonHeat()
    {
        const string path = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        var world = FlatGenerator.Generate(256, 128, "ORBIS-2D-2026", TerrainPreset.Earth, default,
            File.ReadAllBytes(path + "Earth256.bytes"), File.ReadAllBytes(path + "EarthWater256.bytes"),
            File.ReadAllBytes(path + "EarthRivers.bytes"));
        // The development-area CRU normals put the hot season ahead of the
        // rains over the Deccan. Compare the premonsoon and established monsoon.
        double may = 0, july = 0, area = 0;
        for (int i = 0; i < world.Count; i++)
        {
            double lon = ((i % world.Width + .5) / world.Width - .5) * 360;
            double lat = ((i / world.Width + .5) / world.Height - .5) * 180;
            if (lon < 76 || lon >= 80 || lat < 13 || lat >= 18 || FlatTerrainAnalysis.AreaWater(world.Water[i])) continue;
            double weight = Math.Cos(lat * Math.PI / 180);
            may += weight * world.Temperature[i*12+4];
            july += weight * world.Temperature[i*12+6];
            area += weight;
        }
        Console.WriteLine($"Deccan May/July temperatures: {may/area:F1}/{july/area:F1} C.");
        if (!(area > 0 && may > july))
            throw new Exception("The dry premonsoon must be warmer than the established wet monsoon on the Deccan.");
    }
}
