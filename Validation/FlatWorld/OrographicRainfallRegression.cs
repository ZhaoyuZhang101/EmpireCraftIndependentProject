using System;
using Orbis.Flat;

internal static class OrographicRainfallRegression
{
    public static void Run()
    {
        CheckOppositeWinds();
        CheckNorthwardFlow();
        foreach (int width in new[] {128, 136, 256, 384})
            CheckBudget(width);
    }

    private static WorldData Ridge(int width, int shift = 0)
    {
        var world = new WorldData(width, width / 2, "OROGRAPHIC-RAIN", TerrainPreset.Continents);
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < width; x++)
        {
            // A north-south ridge centred on the dateline. Its physical width is fixed.
            double lon = (FMath.Wrap(x - shift + width / 2, width) + .5) / width - .5;
            world.Elevation[y * width + x] = (short)(2200 * Math.Exp(-Math.Pow(lon * 40030 / 110, 2)));
        }
        return world;
    }

    private static FlatMoistureState Air()
    {
        var air = new FlatMoistureState(128, 64);
        for (int i = 0; i < air.Rain.Length; i++)
        {
            air.Rain[i] = 80;
            air.Humidity[i] = i % 12 < 6 ? .9f : .25f;
            air.East[i] = i % 2 == 0 ? .8f : -.8f;
        }
        return air;
    }

    private static void CheckOppositeWinds()
    {
        const int width = 512;
        var world = Ridge(width);
        var air = Air();
        var serial = FlatWorkerPolicy.Options(default, 1);
        FlatOrographicRainfall.Distribute(world, air, serial);
        float[] rain = world.Precipitation;
        int row = world.Height / 2, west = world.Index(-2, row) * 12, east = world.Index(1, row) * 12;
        Console.WriteLine($"Eastward and westward flow: west/east slopes {rain[west]:F2}/{rain[east]:F2}, {rain[west+1]:F2}/{rain[east+1]:F2} mm/month.");
        if (!(rain[west] > 82 && rain[east] < 78 && rain[east + 1] > 82 && rain[west + 1] < 78))
            throw new Exception("The wet slope should reverse when the wind reverses over a symmetric ridge.");
        if (rain[west] - rain[east] <= rain[west + 6] - rain[east + 6])
            throw new Exception("A moister incident column should intensify the slope contrast.");
        const int shift = 8;
        var rotated = Ridge(width, shift);
        FlatOrographicRainfall.Distribute(rotated, air, FlatWorkerPolicy.Options(default, 4));
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < width; x++)
        for (int m = 0; m < 12; m++)
            if (Math.Abs(rain[world.Index(x,y)*12+m] - rotated.Precipitation[world.Index(x+shift,y)*12+m]) > .0001)
                throw new Exception("Moving the ridge away from the periodic seam changed its rainfall.");

        var finer = Ridge(2 * width);
        FlatOrographicRainfall.Distribute(finer, air, FlatWorkerPolicy.Options(default, 4));
        double squareError = 0;
        for (int x = -5; x < 5; x++)
        {
            int fine = finer.Index(2*x, 2*row) * 12;
            float pair = .5f * (finer.Precipitation[fine] + finer.Precipitation[fine+12]);
            double delta = pair - rain[world.Index(x, row)*12];
            squareError += delta * delta;
        }
        double rmse = Math.Sqrt(squareError / 10);
        Console.WriteLine($"Matched equatorial ridge profile, {width} and {2*width}: {rmse:F2} mm/month RMS difference.");
        if (rmse > 2)
            throw new Exception("The same physical ridge and wind changed strength with the terrain resolution.");

        // A submerged ridge is invisible to the atmosphere.
        Array.Fill(world.Water, WaterKind.DeepOcean);
        FlatOrographicRainfall.Distribute(world, air, serial);
        foreach (float value in rain)
            if (Math.Abs(value - 80) > .001)
                throw new Exception("A constant rainy ocean developed a longitude or polar precipitation seam.");
    }

    private static void CheckNorthwardFlow()
    {
        var world = new WorldData(512, 256, "MERIDIONAL-RAIN", TerrainPreset.Continents);
        var air = Air();
        for (int i = 0; i < air.East.Length; i++)
        {
            air.North[i] = i % 2 == 0 ? .65f : -.65f;
            air.East[i] = 0;
        }
        for (int y = 0; y < world.Height; y++)
        {
            double km = ((y + .5)/world.Height - .5) * 20015;
            short height = (short)(2200 * Math.Exp(-Math.Pow(km/110, 2)));
            for (int x = 0; x < world.Width; x++) world.Elevation[y*world.Width+x] = height;
        }
        FlatOrographicRainfall.Distribute(world, air, FlatWorkerPolicy.Options(default, 4));
        int south = world.Index(10,world.Height/2-2)*12, north = world.Index(10,world.Height/2+1)*12;
        var rain = world.Precipitation;
        if (!(rain[south] > rain[north]+2 && rain[north+1] > rain[south+1]+2))
            throw new Exception("A meridional wind did not reverse the wet side of an east-west ridge.");
    }

    private static void CheckBudget(int width, double tolerance = 2e-7)
    {
        var world = Ridge(width);
        var air = Air();
        for (int y = 0; y < air.Height; y++)
        for (int x = 0; x < air.Width; x++)
        for (int m = 0; m < 12; m++)
        {
            // Isolate individual donors at both poles, at the periodic seam and in
            // the interior. The other months exercise a varied field and a dry sky.
            float mm = m < 4 ? (x == m && y == (m == 0 ? 0 : m == 1 ? air.Height-1 : air.Height/2) ? 93 : 0)
                : m == 11 ? 0 : 2 + FMath.Hash((uint)(x + 353*y + 1531*m)) % 160;
            int a = (y*air.Width+x)*12+m;
            air.Rain[a] = mm;
            air.North[a] = m % 2 == 0 ? .4f : -.4f;
        }
        FlatOrographicRainfall.Distribute(world, air, FlatWorkerPolicy.Options(default, 1));
        var serial = (float[])world.Precipitation.Clone();
        FlatOrographicRainfall.Distribute(world, air, FlatWorkerPolicy.Options(default, 4));
        for (int i = 0; i < serial.Length; i++)
        {
            float mm = world.Precipitation[i];
            if (!FMath.Finite(mm) || mm < 0 || serial[i] != mm || (i % 12 == 11 && mm != 0))
                throw new Exception("Nonfinite, negative, nondeterministic or artificially sourced rain.");
            if (width == air.Width && air.Rain[i] != mm)
                throw new Exception("An already resolved moisture tile must retain its monthly rainfall.");
        }
        var source = Integrals(air.Rain, air.Width, air.Height);
        var target = Integrals(serial, world.Width, world.Height);
        double worst = 0;
        for (int m = 0; m < 12; m++)
        {
            double relative = source[m] > 0 ? Math.Abs(target[m] / source[m] - 1) : target[m];
            worst = Math.Max(worst, relative);
            if (relative > tolerance)
                throw new Exception($"Monthly spherical precipitation budget changed by {relative:E3} at {width}.");
        }
        Console.WriteLine($"Rain redistribution {air.Width} -> {width}: maximum relative monthly volume error {worst:E3}.");
    }

    private static double[] Integrals(float[] field, int width, int height)
    {
        var sums = new double[12];
        for (int y = 0; y < height; y++)
        {
            double lower = Math.PI * ((double)y / height - .5), upper = Math.PI * ((double)(y+1) / height - .5);
            double area = (Math.Sin(upper) - Math.Sin(lower)) / width;
            for (int x = 0; x < width; x++)
            for (int m = 0; m < 12; m++)
                sums[m] += field[(y*width+x)*12+m] * area;
        }
        return sums;
    }
}
