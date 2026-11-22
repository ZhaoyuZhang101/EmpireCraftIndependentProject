using System;
using Orbis.Flat;

internal static class SeasonalRainfallRegression
{
    public static void Run()
    {
        CheckThermalInflow();
        CheckWaterSources();
        CheckPeninsulaMonsoon();
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

            // On the poleward part of this unobstructed coast the sea can
            // sustain the wet season after the inland summer maximum passes.
            int midlatitudeRow = (int)((hemisphere * 46.0 / 180 + .5) * world.Height);
            int shore = world.Index(80, midlatitudeRow) * 12;
            int interior = world.Index(72, midlatitudeRow) * 12;
            int lateSummer = (summer + 1) % 12;
            Console.WriteLine($"{hemisphere * 46} degrees, midsummer/late summer: shore {rain[shore+summer]:F1}/{rain[shore+lateSummer]:F1}, interior {rain[interior+summer]:F1}/{rain[interior+lateSummer]:F1} mm.");
            if (!(rain[shore+lateSummer] > 2 * rain[shore+summer] / 3 &&
                  rain[interior+lateSummer] < rain[interior+summer] / 2))
                throw new Exception("Late-summer marine supply should persist at the coast and diminish inland.");
        }
    }

    private static void CheckThermalInflow()
    {
        const int width=128, height=64;
        var water=new float[width*height];
        var temperature=new float[width*height*12];
        var annual=new float[width*height];
        // A north-south continent crosses the equator. Both shorelines have
        // the same annual heat and opposite normals. Only its land has seasons.
        for(int y=0;y<height;y++)
        for(int x=0;x<width;x++)
        {
            int i=y*width+x;
            water[i]=x>=32 && x<96 ? 0 : 1;
            annual[i]=24;
            for(int m=0;m<12;m++)
                temperature[i*12+m]=24+(1-water[i])*7*(float)Math.Cos((m-6)*Math.PI/6);
        }
        var gradients=FlatMoisture.RegionalHeatLow(water,temperature,annual,width,height);
        int north=(int)((15.0/180+.5)*height),south=height-1-north;
        var west=gradients[north*width+32];var east=gradients[north*width+95];
        var reflected=gradients[south*width+32];
        if (!(west.East>.3f && east.East<-.3f && west.SummerEast>.25f &&
              Math.Abs(west.SummerEast+reflected.SummerEast)<1e-5f &&
              Math.Abs(west.SummerEast+east.SummerEast)<1e-5f))
            throw new Exception("A seasonal thermal low should draw air inland on either shore and reverse six months across the equator.");
        Array.Fill(temperature,24f);
        var constant=FlatMoisture.RegionalHeatLow(water,temperature,annual,width,height)[north*width+32];
        if (constant.SummerEast!=0 || constant.East!=west.East)
            throw new Exception("Removing seasonal heating should leave only the year-round onshore gradient.");
        float calm=FlatMoisture.OpenWaterExchange(0,0,0);
        float trade=FlatMoisture.OpenWaterExchange(-.24f,0,0);
        float storm=FlatMoisture.OpenWaterExchange(.24f,0,1);
        if (!(calm>0 && calm<trade && trade<storm && trade==FlatMoisture.OpenWaterExchange(.24f,0,0)))
            throw new Exception("Open-water exchange should increase with mean speed and with the variance of transient winds.");
    }

    private static void CheckPeninsulaMonsoon()
    {
        // Two mirrored peninsulas point towards the equator beneath a large
        // continent. A ridge runs along each western shoreline, with a high
        // range inland to the poleward side of each summer heat low.
        var world = new WorldData(128, 64, "MONSOON-PENINSULAS", TerrainPreset.Continents);
        for (int y = 0; y < world.Height; y++)
        {
            double lat = Math.Abs((y + .5) / world.Height * 180 - 90);
            int center = y >= world.Height / 2 ? 32 : 96;
            for (int x = 0; x < world.Width; x++)
            {
                int dx = FMath.Wrap(x - center + 64, 128) - 64;
                bool peninsula = lat >= 8 && dx >= -3 && dx <= 4;
                bool continent = lat >= 24 && dx >= -15 && dx <= 20;
                short elevation = -800;
                if (peninsula || continent) elevation = 150;
                if (peninsula && dx == -3 && lat < 24) elevation = 950;
                if (continent && lat >= 29 && lat < 39 && dx >= -8 && dx <= 14) elevation = 3400;
                world.Elevation[world.Index(x, y)] = elevation;
            }
        }
        FlatGenerator.Rebuild(world, default);
        var air = world.Moisture;
        foreach (int hemisphere in new[] {-1, 1})
        {
            int row = (int)((hemisphere * 15 + 90) / 180.0 * world.Height);
            int center = hemisphere > 0 ? 32 : 96;
            int summer = hemisphere > 0 ? 6 : 0, winter = (summer + 6) % 12;
            int sea = world.Index(center - 5, row) * 12;
            int crest = world.Index(center - 3, row) * 12, lee = world.Index(center, row) * 12;
            Console.WriteLine($"Peninsula {hemisphere * 15}: summer marine wind ({air.East[sea+summer]:F2}, {air.North[sea+summer]:F2}), winter east {air.East[sea+winter]:F2}, windward/leeward summer rain {air.Rain[crest+summer]:F0}/{air.Rain[lee+summer]:F0} mm.");
            if (!(air.East[sea+summer] > 0 && air.North[sea+summer]*hemisphere > 0 && air.East[sea+winter] < 0))
                throw new Exception("Trades did not reverse into a poleward, eastward summer marine inflow.");
            if (!(air.Rain[crest+summer] > air.Rain[lee+summer]))
                throw new Exception("A peninsula's windward coastal ridge should intercept more summer rain than the lee plain.");
            int foothillRow = (int)((hemisphere * 26 + 90) / 180.0 * world.Height);
            int easternBay = world.Index(center+6, foothillRow)*12;
            Console.WriteLine($"Eastern bay, {hemisphere * 26}: along-range summer wind {air.East[easternBay+summer]:F2}.");
            if (!(air.East[easternBay+summer] < 0))
                throw new Exception("A poleward-facing bay should feed the lowlands westward along their blocking range.");
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
