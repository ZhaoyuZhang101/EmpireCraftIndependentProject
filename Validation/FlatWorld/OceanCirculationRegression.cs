using System;
using Orbis.Flat;

internal static class OceanCirculationRegression
{
    static void Check(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
    }

    static float[] Basin(int width, int offset = 0)
    {
        int height = width / 2;
        var mask = new float[width * height];
        for (int y = 0; y < height; y++)
            for (int x = width / 4; x < 3 * width / 4; x++) mask[y * width + (x + offset) % width] = 1;
        return mask;
    }

    public static void Run()
    {
        foreach (int width in new[] {64, 128, 256}) CheckBasin(width);
        CheckIsolation();
        CheckSphericalStress();
        CheckGeography();
        Console.WriteLine("PASS current direction, mixed-layer heat, coastal air, wind-off, seam, basin isolation, lake mask and workers.");
    }

    static void CheckBasin(int width)
    {
        int height = width / 2, west = width / 4, east = 3 * width / 4 - 1;
        var ocean = Basin(width);
        var field = FlatOceanCirculation.Build(ocean, width, height, FlatWorkerPolicy.Options(default, 1));
        var shifted = FlatOceanCirculation.Build(Basin(width, width / 2), width, height, FlatWorkerPolicy.Options(default, 4));
        int row = (int)((32 + 90) * height / 180.0) * width;
        Check(field.North[row + west] > .1f && field.North[row + east] < -.003f, "Expected poleward western return and equatorward interior flow.");
        Check(field.SeaAnomaly[row + west] > 1 && field.SeaAnomaly[row + east] < -.15f, "Currents must affect the water temperature on both coasts.");
        Check(field.AirAnomaly[row + west - 1] > .15f && field.AirAnomaly[row + east + 1] < -.05f, "Marine air must carry heat onto adjacent land.");

        double area = 0, heat = 0, maxReflection = 0, maxShift = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x, mirror = (height - 1 - y) * width + x;
            int shift = y * width + (x + width / 2) % width;
            double latitude = ((y + .5) / height - .5) * Math.PI;
            double equilibrium = Math.Max(-1.8, 28 - 31 * Math.Pow(Math.Sin(latitude), 2));
            double sst = equilibrium + field.SeaAnomaly[i];
            Check(sst >= -1.8001 && sst <= 28.0001, "Mixed-layer temperature escaped its finite source bounds.");
            maxReflection = Math.Max(maxReflection, Math.Abs(field.SeaAnomaly[i] - field.SeaAnomaly[mirror]));
            maxReflection = Math.Max(maxReflection, Math.Abs(field.North[i] + field.North[mirror]));
            maxShift = Math.Max(maxShift, Math.Abs(field.SeaAnomaly[i] - shifted.SeaAnomaly[shift]));
            maxShift = Math.Max(maxShift, Math.Abs(field.AirAnomaly[i] - shifted.AirAnomaly[shift]));
            if (ocean[i] == 0)
                Check(field.SeaAnomaly[i] == 0 && field.East[i] == 0 && field.North[i] == 0, "Land carries a through-current.");
            else
            {
                double weight = Math.Cos(latitude);
                heat += field.SeaAnomaly[i] * weight;
                area += weight;
            }
        }
        Check(maxReflection < .00003 && maxShift < .00001,
            $"Hemisphere / longitude and worker errors {maxReflection:G3} / {maxShift:G3}.");
        Check(Math.Abs(heat / area) < .003, "Steady mixed-layer heat balance exceeds the discrete solve tolerance.");
        Console.WriteLine($"Synthetic {width} mixed layer, 32N west/east SST {field.SeaAnomaly[row + west]:F2} / {field.SeaAnomaly[row + east]:F2} C, basin heat residual {heat / area:G3} C.");
    }

    static void CheckIsolation()
    {
        const int width = 128, height = width / 2;
        var options = FlatWorkerPolicy.Options(default);
        var ocean = Basin(width);
        var first = FlatOceanCirculation.Build(ocean, width, height, options);
        for (int y = 5; y < height - 8; y++)
            for (int x = 2; x < 22; x++) ocean[y * width + x] = 1;
        var two = FlatOceanCirculation.Build(ocean, width, height, options);
        var still = FlatOceanCirculation.Build(ocean, width, height, options, new float[height]);
        for (int i = 0; i < ocean.Length; i++)
        {
            Check(still.SeaAnomaly[i] == 0 && still.AirAnomaly[i] == 0 && still.North[i] == 0 && still.East[i] == 0,
                "Turning off the wind must remove currents and their thermal effects.");
            int x = i % width;
            if (x >= width / 4 && x < 3 * width / 4)
                Check(Math.Abs(first.SeaAnomaly[i] - two.SeaAnomaly[i]) < .0002, "Heat crossed a dry divide between basins.");
        }
        Array.Fill(ocean, 1f);
        var channel = FlatOceanCirculation.Build(ocean, width, height, options);
        int middleLatitude = (int)((48 + 90) * height / 180.0) * width;
        Check(channel.East[middleLatitude] > .07 && channel.North[middleLatitude] == 0 && Math.Abs(channel.SeaAnomaly[middleLatitude]) < .00001,
            "An unbounded ocean latitude should carry the westerly current with no fictitious zonal temperature gradient.");
    }

    // Eastward stress around a latitude circle has curl tau*tan(lat)/R even
    // when the stress magnitude is constant. The basin interior flows poleward.
    static void CheckSphericalStress()
    {
        const int width = 128, height = width / 2;
        var winds = new float[height];
        Array.Fill(winds, .7f);
        var field = FlatOceanCirculation.Build(Basin(width), width, height, FlatWorkerPolicy.Options(default, 1), winds);
        foreach (int sign in new[] { -1, 1 })
        {
            int y = (int)((sign * 45 + 90) * height / 180.0);
            double lat = ((y + .5) / height - .5) * Math.PI;
            double expected = .09 * .7 * Math.Tan(lat) / (1025 * 180 * 2 * 7.292115e-5 * Math.Cos(lat));
            double interior = field.North[y * width + width / 2];
            Check(Math.Abs(interior - expected) < .12 * Math.Abs(expected),
                $"Constant zonal stress at {lat * 180 / Math.PI:F1} degrees: interior {interior:G4}, expected {expected:G4} m/s.");
            Check(field.North[y * width + width / 4] * sign < 0, "The western return must close the spherical stress transport.");
        }
    }

    static void CheckGeography()
    {
        // A 192-column world does not divide the 128-column climate grid.
        var world = new WorldData(192, 96, "OCEAN-GEOMETRY", TerrainPreset.Continents);
        var options = FlatWorkerPolicy.Options(default);
        for (int i = 0; i < world.Count; i++)
        {
            int x = i % world.Width;
            world.Water[i] = x >= 48 && x < 144 ? WaterKind.Lake : WaterKind.Land;
        }
        var lakes = FlatOceanCirculation.Build(world, options);
        Check(Array.TrueForAll(lakes.SeaAnomaly, d => d == 0) && Array.TrueForAll(lakes.AirAnomaly, d => d == 0),
            "Lakes must be excluded from the ocean mask.");
        for (int i = 0; i < world.Count; i++) if (world.Water[i] == WaterKind.Lake) world.Water[i] = WaterKind.DeepOcean;
        var aggregated = FlatOceanCirculation.Build(world, options);
        var reference = FlatOceanCirculation.Build(Basin(128), 128, 64, options);
        Check(aggregated.Width == 128 && aggregated.Height == 64, "Expected the reduced physical grid.");
        for (int i = 0; i < reference.SeaAnomaly.Length; i++)
            Check(aggregated.SeaAnomaly[i] == reference.SeaAnomaly[i] && aggregated.AirAnomaly[i] == reference.AirAnomaly[i],
                "Area-overlap integration misplaced the model coastline.");
        Check(aggregated.SampleAir(-.015625f, .62f) == aggregated.SampleAir(.984375f, .62f), "Marine air lookup broke at longitude wrap.");
    }
}
