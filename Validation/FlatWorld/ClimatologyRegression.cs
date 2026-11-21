using System;
using Orbis.Flat;

internal static class ClimatologyRegression
{
    // Move the entire coastline through the date line and change the preset
    // label. Its climate should follow the bed, even on a fictional continent.
    public static void RotatedContinent()
    {
        const int width=128, height=64, shift=39;
        var original=FlatGenerator.Generate(width,height,"MERIDIAN-CLIMATE",TerrainPreset.GreatContinent,default);
        var moved=new WorldData(width,height,"ANOTHER-SEED",TerrainPreset.Continents);
        for(int i=0;i<original.Count;i++)
        {
            int j=moved.Index(i%width+shift,i/width);
            moved.Water[j]=original.Water[i];
            moved.WaterLevel[j]=original.WaterLevel[i];
            moved.Elevation[j]=original.Elevation[i];
            moved.CoastDistance[j]=original.CoastDistance[i];
        }
        FlatClimate.Build(original,default,1);
        FlatClimate.Build(moved,default,4);
        double maxTemperature=0,maxRain=0;
        for(int i=0;i<original.Count;i++)
        {
            int j=moved.Index(i%width+shift,i/width);
            for(int m=0;m<12;m++)
            {
                maxTemperature=Math.Max(maxTemperature,Math.Abs(original.Temperature[i*12+m]-moved.Temperature[j*12+m]));
                maxRain=Math.Max(maxRain,Math.Abs(original.Precipitation[i*12+m]-moved.Precipitation[j*12+m]));
            }
        }
        if(!(maxTemperature<=.001 && maxRain<=.05))
            throw new Exception($"Longitude-dependent normals: differences {maxTemperature} °C, {maxRain} mm/month.");
        Console.WriteLine($"Rotated continent, 1 vs 4 workers: max difference {maxTemperature:G3} °C, {maxRain:G3} mm/month.");
    }

    public static void LandStatistics(WorldData world, string label)
    {
        double area=0, rain=0, desert=0, steppe=0, surfaceDesert=0, wetness=0, wetBanks=0;
        for (int y=0; y<world.Height; y++)
        {
            double weight=Math.Cos(((y+.5)/world.Height-.5)*Math.PI);
            for (int x=0; x<world.Width; x++)
            {
                int i=world.Index(x,y);
                if (FlatTerrainAnalysis.AreaWater(world.Water[i])) continue;
                area+=weight;
                rain+=weight*world.Facts.AnnualRain[i];
                wetness+=weight*world.Facts.Wetness[i];
                if (world.Climates[i]==Koppen.BWh || world.Climates[i]==Koppen.BWk) desert+=weight;
                if (world.Climates[i]==Koppen.BSh || world.Climates[i]==Koppen.BSk) steppe+=weight;
                if (world.Biomes[i]==Biome.Desert) surfaceDesert+=weight;
                if (world.Facts.FreshDistance[i]<=1 && world.Facts.SaturatedMonths[i]>=8) wetBanks+=weight;
            }
        }
        Console.WriteLine($"{label} area-weighted land: {rain/area:F0} mm/yr, BW {100*desert/area:F1}%, BS {100*steppe/area:F1}%, desert biome {100*surfaceDesert/area:F1}%, mean soil bucket {100*wetness/area:F1}%, near freshwater and saturated 8+ months {100*wetBanks/area:F1}%");
    }

    public static void TerrainPresets(WorldData atlas)
    {
        LandStatistics(atlas,"Default atlas 512");
        foreach (var sample in new[] {
            (TerrainPreset.Archipelago,"SHELF-ISLANDS"),(TerrainPreset.GreatContinent,"CLIMATE-CONTINENT"),
            (TerrainPreset.InlandSea,"INLAND-BASIN"),(TerrainPreset.GreatContinent,"MONSOON-PLAINS") })
            LandStatistics(FlatGenerator.Generate(256,128,sample.Item2,sample.Item1,default),sample.Item1+" "+sample.Item2+" 256");
    }

    public static void RainShadow()
    {
        var level = new WorldData(128,64,"BARRIER-COAST",TerrainPreset.Continents);
        for (int y=0;y<level.Height;y++)
        for (int x=0;x<level.Width;x++)
            level.Elevation[level.Index(x,y)] = (short)(x>=38 && x<84 ? 60 : -800);
        var ridge = level.Clone();
        for (int y=0;y<ridge.Height;y++) ridge.Elevation[ridge.Index(65,y)] = 1700;
        FlatGenerator.Rebuild(level,default);
        FlatGenerator.Rebuild(ridge,default);
        double plain=0,sheltered=0,crest=0;int samples=0;
        for (int y=0;y<ridge.Height;y++)
        {
            if (Math.Abs(ridge.Latitude(ridge.Index(0,y)))>12) continue;
            // Tropical easterlies reach the ridge from the east. Compare the
            // same low, western lee cells with and without an intervening ridge.
            for (int x=58;x<=62;x++)
            {
                plain+=level.Facts.AnnualRain[level.Index(x,y)];
                sheltered+=ridge.Facts.AnnualRain[ridge.Index(x,y)];
                crest+=ridge.Facts.AnnualRain[ridge.Index(65,y)];
                samples++;
            }
        }
        Console.WriteLine($"Trade-wind section, mm/year: level control {plain/samples:F0}, sheltered lowland {sheltered/samples:F0}, windward crest {crest/samples:F0}");
        if (sheltered>plain*.85 || crest<plain*1.3)
            throw new Exception("A mountain barrier must gain windward precipitation and remove moisture from downwind lowlands.");
    }

    public static void WeatherYears()
    {
        var world = new WorldData(64, 32, "YEARLY-WEATHER", TerrainPreset.Continents);
        world.Moisture = new FlatMoistureState(world.Width, world.Height);
        for (int i = 0; i < world.Temperature.Length; i++)
        {
            world.Temperature[i] = 18;
            world.Precipitation[i] = 320;
        }
        var first = new FlatWeatherSimulation(world, 1);
        world.Day = 720;
        var later = new FlatWeatherSimulation(world, 1).Model;
        var a = FlatClimateYear.At(FMath.Seed(world.Seed), 0);
        var b = FlatClimateYear.At(FMath.Seed(world.Seed), world.Day);
        for (int i = 0; i < first.Model.Count; i++)
        {
            var p = first.Model.Position[i];
            a.Offsets((float)p.X, (float)p.Y, (float)p.Z, out float t0, out float rain0);
            b.Offsets((float)p.X, (float)p.Y, (float)p.Z, out float t1, out float rain1);
            // Dry initial air avoids latent heating in the first model layer.
            if (Math.Abs(later.Temperature[i] - first.Model.Temperature[i] - (t1 - t0)) > .001)
                throw new Exception("The evolving atmosphere ignores the regional temperature for this climate year.");
            // Offset the saved normals so the two years have identical realized
            // temperature gradients and rainfall. Their frontal ascent must match.
            for (int m = 0; m < 12; m++)
            {
                world.Temperature[i * 12 + m] = 18 + t0 - t1;
                world.Precipitation[i * 12 + m] = 320 * (1 + rain0) / (1 + rain1);
            }
        }
        var equivalent = new FlatWeatherSimulation(world, 1);
        equivalent.Model.Restore(first.Model.Save());
        first.Step(.1, 0);
        equivalent.Step(.1, world.Day);
        for (int i = 0; i < first.Model.Count; i++)
            if (Math.Abs(first.Model.VerticalVelocity[i] - equivalent.Model.VerticalVelocity[i]) > .0001)
                throw new Exception("Weather-system ascent must use the realized rainfall and thermal gradients of the same year.");
    }

    static double EquatorialOcean(int width)
    {
        var world = new WorldData(width, width / 2, "FLAT-OCEAN", TerrainPreset.Continents);
        for (int i = 0; i < world.Count; i++)
        {
            world.Water[i] = WaterKind.DeepOcean;
            world.Elevation[i] = -1800;
        }
        FlatClimate.Build(world, default);
        var air = world.Moisture;
        double rain = 0;
        for (int x = 0; x < 8; x++)
        for (int m = 0; m < 12; m++)
            rain += air.Sample(air.Rain, (x + .5f) / 8, .5f, m);
        return rain / 8;
    }

    public static void GridScale()
    {
        double small = EquatorialOcean(64), large = EquatorialOcean(128);
        Console.WriteLine($"Equatorial ocean on 64/128 columns, mm/year: {small:F0} / {large:F0}");
        if (Math.Abs(small - large) > large * .25)
            throw new Exception("Changing tile resolution changed the effective atmospheric transport distance.");
    }

    public static void EquatorialWinds()
    {
        var world=new WorldData(128,64,"EQUATORIAL-PRESSURE",TerrainPreset.GreatContinent);
        for (int i=0;i<world.Count;i++)
        {
            world.CoastDistance[i]=100;
            for (int m=0;m<12;m++) world.Temperature[i*12+m]=25;
        }
        var air=FlatMoisture.Build(world,default);
        int south=air.Height/2-1,north=south+1;
        foreach (int month in new[] {0,6})
        {
            float southJump=Math.Abs(air.North[(south*air.Width)*12+month]-air.North[((south-1)*air.Width)*12+month]);
            float northJump=Math.Abs(air.North[(north*air.Width)*12+month]-air.North[((north+1)*air.Width)*12+month]);
            if (Math.Max(southJump,northJump)>.18f)
                throw new Exception($"Uniform continental solstice heating creates an equator-only wind jump ({southJump:F2}, {northJump:F2}).");
        }
    }

    public static void FreshwaterBasin()
    {
        var world = new WorldData(64, 32, "UPLAND-LAKE", TerrainPreset.InlandSea);
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
        {
            // A small equatorial lake receives a converging upland catchment
            // on its eastern bank. A surveyed 180 m sill opens to the western sea.
            int h = x < 16 ? -500 : 180 + 12 * (Math.Max(0, x - 24) + Math.Max(0, Math.Abs(y - 16) - 1));
            if (x >= 20 && x <= 24 && y >= 15 && y <= 17) h = 40;
            if (x >= 16 && x < 20 && y == 16) h = 120 + 20 * (x - 16);
            world.Elevation[world.Index(x, y)] = (short)h;
        }
        FlatGenerator.Rebuild(world, default);
        int basin = world.Index(22, 16);
        if (world.Water[basin] != WaterKind.Lake || world.WaterLevel[basin] != 180)
            throw new Exception("Expected a permanent freshwater basin above sea level.");
        int outlet = basin;
        while (world.Downstream[outlet] >= 0) outlet = world.Downstream[outlet];
        if (!FlatTerrainAnalysis.Ocean(world.Water[outlet]) || world.Runoff[outlet] <= 0)
            throw new Exception("The supplied equatorial lake must overflow through its sill to the ocean.");
        var dry = world.Clone();
        Array.Clear(dry.Precipitation, 0, dry.Precipitation.Length);
        FlatHydrology.Build(dry, default);
        if (dry.Water[basin] != WaterKind.Land || dry.WaterLevel[basin] != 40 || dry.Runoff[basin] != 0)
            throw new Exception("The same empty depression flooded or carried flow without precipitation.");
        world.Day = 97.25;
        var loaded = FlatSave.Decode(FlatSave.Encode(world));
        if (loaded.Day != world.Day || loaded.Month != world.Month)
            throw new Exception("Loading the lake changed the climate calendar.");
        for (int j = 0; j < world.Moisture.Rain.Length; j++)
            if (world.Moisture.Rain[j] != loaded.Moisture.Rain[j] ||
                world.Moisture.Humidity[j] != loaded.Moisture.Humidity[j])
                throw new Exception("The upland lake first contributes evaporation after reloading the same generated terrain.");
    }

    public static void FullGridCoverage()
    {
        // Accepted imported/save dimensions need not divide the 128-column
        // atmospheric grid. This water source occupies the far northeast only.
        var world = new WorldData(192, 96, "EASTERN-WATER", TerrainPreset.Continents);
        for (int i = 0; i < world.Count; i++)
        {
            world.Water[i] = WaterKind.Land;
            world.Elevation[i] = 45;
            world.CoastDistance[i] = 12;
            for (int m = 0; m < 12; m++) world.Temperature[i * 12 + m] = 22;
        }
        var dry = FlatMoisture.Build(world, default);
        for (int y = 64; y < world.Height; y++)
        for (int x = 128; x < world.Width; x++)
        {
            int i = world.Index(x, y);
            world.Water[i] = WaterKind.MiddleOcean;
            world.Elevation[i] = -450;
            world.CoastDistance[i] = 0;
        }
        var wet = FlatMoisture.Build(world, default);
        double change = 0;
        for (int j = 0; j < wet.Rain.Length; j++) change += Math.Abs(wet.Rain[j] - dry.Rain[j]);
        if (change < wet.Rain.Length)
            throw new Exception("Atmospheric downsampling dropped the northeast part of the imported terrain.");
    }

    public static void MaritimeRainfall()
    {
        // A flat island spanning six climate columns has a warm ocean both
        // upwind and downwind. There is no high ground to cast a rain shadow.
        var world = new WorldData(128, 64, "TROPICAL-MARITIME", TerrainPreset.Archipelago);
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
            world.Elevation[world.Index(x, y)] = (short)(x >= 61 && x < 67 && y >= 26 && y < 38 ? 40 : -500);
        FlatGenerator.Rebuild(world, default);

        double island = 0, subtropicalSea = 0, equatorialSea = 0;
        int islandCount = 0, subtropicalCount = 0, equatorialCount = 0;
        for (int i = 0; i < world.Count; i++)
        {
            double lat = Math.Abs(world.Latitude(i));
            bool ocean = FlatTerrainAnalysis.Ocean(world.Water[i]);
            double rain = world.Facts.AnnualRain[i];
            if (!ocean && lat < 10) { island += rain; islandCount++; }
            if (ocean && lat >= 24 && lat <= 34) { subtropicalSea += rain; subtropicalCount++; }
            if (ocean && lat < 10) { equatorialSea += rain; equatorialCount++; }
        }
        island /= islandCount;
        subtropicalSea /= subtropicalCount;
        equatorialSea /= equatorialCount;
        Console.WriteLine($"Flat-island climate, mm/year: equatorial land {island:F0}, equatorial ocean {equatorialSea:F0}, subtropical ocean {subtropicalSea:F0}");
        if (island < 1000 || island < equatorialSea * .45)
            throw new Exception("The warm, low equatorial island loses its maritime rainfall over level ground.");
        if (subtropicalSea < 250 || subtropicalSea >= equatorialSea * .75)
            throw new Exception("Subtropical waters should receive episodic showers and remain drier than the equatorial belt.");
    }
}
