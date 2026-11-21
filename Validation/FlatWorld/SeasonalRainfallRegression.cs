using System;
using Orbis.Flat;

internal static class SeasonalRainfallRegression
{
    public static void Run()
    {
        CheckWaterSources();
        // Rotate seasons through the same terrain in the two hemispheres. Flat
        // topography isolates the influence of the adjacent ocean and storms.
        var world = new WorldData(128, 64, "SEASONAL-MARGINS", TerrainPreset.Continents);
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
            world.Elevation[world.Index(x, y)] = (short)(x >= 28 && x < 81 ? 60 : -800);
        FlatGenerator.Rebuild(world, default);

        foreach (int hemisphere in new[] { -1, 1 })
        {
            int row = (int)((hemisphere * 36.0 / 180 + .5) * world.Height);
            int west = world.Index(29, row), east = world.Index(78, row);
            int summer = hemisphere > 0 ? 6 : 0, winter = (summer + 6) % 12;
            float[] rain = world.Precipitation;
            float ws = rain[west * 12 + summer], ww = rain[west * 12 + winter];
            float es = rain[east * 12 + summer], ew = rain[east * 12 + winter];
            Console.WriteLine($"{hemisphere * 36} degrees, west and east coast summer/winter mm: {ws:F1}/{ww:F1}, {es:F1}/{ew:F1}");
            if (!(ws < 40 && ws < ww / 3 && es > 2 * ew))
                throw new Exception("The two continental margins should have opposite rainy seasons.");
        }
    }

    private static void CheckWaterSources()
    {
        var world = new WorldData(64, 32, "RAIN-SOURCES", TerrainPreset.Continents);
        Array.Fill(world.Temperature, 22f);
        Array.Fill(world.CoastDistance, ushort.MaxValue);
        var empty = FlatMoisture.Build(world, default, 1);
        if (Array.Exists(empty.Rain, mm => mm != 0))
            throw new Exception("A dry world with no water sources generates precipitation.");

        // Compare two oceans with the same annual mean and identical January
        // forcing. Move a warm anomaly from June into the preceding December.
        for (int i = 0; i < world.Count; i++)
            if (i % world.Width < 28)
            {
                world.Water[i] = WaterKind.DeepOcean;
                world.CoastDistance[i] = 0;
            }
        var first = FlatMoisture.Build(world, default, 1);
        for (int i = 0; i < world.Count; i++)
            if (world.Water[i] == WaterKind.DeepOcean)
            {
                world.Temperature[i * 12 + 11] += 8;
                world.Temperature[i * 12 + 5] -= 8;
            }
        var second = FlatMoisture.Build(world, default, 4);
        double change = 0;
        for (int i = 0; i < world.Count; i++)
            change += Math.Abs(first.Rain[i * 12] - second.Rain[i * 12]);
        Console.WriteLine($"January response to the preceding ocean season: {change / world.Count:F2} mm/month mean absolute difference.");
        if (change / world.Count < .1)
            throw new Exception("January rain lost the moisture carried across the December boundary.");
    }
}
