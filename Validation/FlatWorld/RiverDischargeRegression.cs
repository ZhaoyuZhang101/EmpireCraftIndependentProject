using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Orbis.Flat;

internal static class RiverDischargeRegression
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("River discharge: " + message);
    }

    static WorldData Slope(float temperature, float rain)
    {
        var world = new WorldData(64, 32, "SEASONAL-WATER-BUDGET", TerrainPreset.Continents);
        for (int i = 0; i < world.Count; i++)
        {
            int x = i % world.Width;
            world.Elevation[i] = (short)(x == 0 ? -100 : 100 + Math.Min(x, world.Width - x) * 10);
            world.Water[i] = x == 0 ? WaterKind.ShallowOcean : WaterKind.Land;
            for (int month = 0; month < 12; month++)
            {
                world.Temperature[i * 12 + month] = temperature;
                world.Precipitation[i * 12 + month] = rain;
            }
        }
        FlatHydrology.Build(world, default);
        return world;
    }

    static double OutletVolume(WorldData world)
    {
        double total = 0;
        for (int i = 0; i < world.Count; i++)
            if (world.Downstream[i] < 0) total += world.Runoff[i];
        return total;
    }

    static void Near(double a, double b, double tolerance, string message)
    {
        Check(Math.Abs(a - b) <= tolerance, message + $" ({a:R} / {b:R})");
    }

    static void StorageBalance()
    {
        foreach (bool lake in new[] { false, true })
        {
            var water = new FlatRiverDischarge.WaterColumn(lake, .45);
            double maxError = 0, snowfall = 0, spring = 0, lateSummer = 0;
            for (int step = 0; step < 72; step++)
            {
                int month = step % 12;
                double precipitation = month < 3 ? 110 : month == 5 ? 850 : 0;
                double temperature = month < 3 ? -9 : month == 3 ? 5 : 19;
                double initial = water.Snow + water.Soil + water.Aquifer;
                double discharge = water.Advance(new FlatRiverDischarge.ClimateMonth(precipitation, temperature, 3), out double evaporation);
                double final = water.Snow + water.Soil + water.Aquifer;
                maxError = Math.Max(maxError, Math.Abs(initial + precipitation - evaporation - discharge - final));
                Check(water.Snow >= 0 && water.Soil >= 0 && water.Aquifer >= 0 && discharge >= 0 && evaporation >= 0,
                    "Every transfer and store must remain nonnegative.");
                if (step == 62) snowfall = water.Snow;
                if (step == 63) spring = discharge;
                if (step == 69) lateSummer = discharge;
            }
            Check(maxError < 1e-10, "Monthly rain, ET, snow, soil, groundwater and streamflow must balance.");
            Check(snowfall >= 325 && spring > 30, "A cold winter stores snow for the thaw.");
            if (!lake)
            {
                Check(lateSummer > 1, "Finite groundwater sustains flow after the rainy season.");
                var dry = new FlatRiverDischarge.ClimateMonth(0, 25, 4);
                double discharge = 0;
                for (int m = 0; m < 120; m++) discharge = water.Advance(dry, out _);
                Check(discharge < 1e-7, "An unrecharged aquifer eventually dries up.");
            }
            Console.WriteLine($"Monthly storage closure, lake={lake}: residual={maxError:E2} mm, winter snow={snowfall:F1} mm, spring runoff={spring:F1} mm");
        }
    }

    static void AreaWeights()
    {
        var world = new WorldData(64, 32, "SPHERICAL-AREA", TerrainPreset.Continents);
        double areaSum = 0;
        for (int row = 0; row < world.Height; row++)
        {
            double area = FlatRiverDischarge.RelativeArea(row, world.Height);
            areaSum += area;
            Near(area, FlatRiverDischarge.RelativeArea(world.Height - 1 - row, world.Height), 1e-12,
                "Equal latitude bands in opposite hemispheres cover equal areas.");
        }
        Near(areaSum, world.Height, 1e-12, "Area weights must sum to the global surface area in mean-tile units.");
        int polar = world.Index(1, 0), equator = world.Index(1, 15);
        foreach (int tile in new[] { polar, equator })
            for (int m = 0; m < 12; m++)
            {
                world.Temperature[tile * 12 + m] = 3;
                world.Precipitation[tile * 12 + m] = 10000;
            }
        var annual = new float[world.Count];
        FlatRiverDischarge.BuildSources(world, default, annual);
        double expected = FlatRiverDischarge.RelativeArea(0, 32) / FlatRiverDischarge.RelativeArea(15, 32);
        Near(annual[polar] / annual[equator], expected, expected * .01,
            "Equal heavy rainfall yields much less water from a small polar cell.");
        for (int m = 0; m < 12; m++) world.Temperature[polar * 12 + m] = -18;
        FlatRiverDischarge.BuildSources(world, default, annual);
        Check(annual[polar] == 0, "A permanently frozen local snowfall balance stays in ice.");
        world.Water[polar] = WaterKind.River;
        world.Runoff[polar] = 5;
        // Authored terrain can carry a known annual flow before the climate
        // provides a seasonal signal, and before DrainOrder is built.
        var preserved = FlatRiverDischarge.Route(world, null, default);
        Near(preserved.StageRatio(polar, 0), 1, 0, "Without a seasonal signal an authored station retains its mean.");
        Near(preserved.Sample(polar, 120), 5, 0, "A stored perennial supply is not lost in an icy region.");
    }

    static WorldData Tributaries(out int snow, out int rain, out int junction, out int outlet, out int below)
    {
        var world = new WorldData(64, 32, "SNOW-RAIN-LAKE", TerrainPreset.Continents);
        for (int i = 0; i < world.Count; i++)
        {
            world.Elevation[i] = world.WaterLevel[i] = 100;
            world.Climates[i] = Koppen.Cfb;
            world.DrainOrder[i] = i;
        }
        snow = world.Index(32, 16);
        rain = world.Index(31, 17);
        junction = world.Index(31, 16);
        int lakeIn = world.Index(30, 16);
        outlet = world.Index(29, 16);
        below = world.Index(28, 16);
        foreach (int i in new[] { snow, rain, junction, below }) world.Water[i] = WaterKind.River;
        world.Water[lakeIn] = world.Water[outlet] = WaterKind.Lake;
        world.Elevation[lakeIn] = world.Elevation[outlet] = 93;
        world.Downstream[snow] = world.Downstream[rain] = junction;
        world.Downstream[junction] = lakeIn;
        world.Downstream[lakeIn] = outlet;
        world.Downstream[outlet] = below;
        for (int m = 0; m < 12; m++)
        {
            bool winter = m == 11 || m < 3;
            world.Temperature[snow * 12 + m] = winter ? -8 : 12;
            world.Precipitation[snow * 12 + m] = winter ? 95 : 0;
            world.Temperature[rain * 12 + m] = 15;
            world.Precipitation[rain * 12 + m] = m == 7 || m == 8 ? 210 : 0;
        }
        return world;
    }

    static void AccumulateAnnual(WorldData world)
    {
        for (int k = world.Count - 1; k >= 0; k--)
        {
            int i = world.DrainOrder[k], p = world.Downstream[i];
            if (p >= 0) world.Runoff[p] += world.Runoff[i];
        }
    }

    static void CheckHydrograph(FlatRiverDischarge flow, int a, int b, int junction, int outlet, int below)
    {
        foreach (int i in new[] { a, b, junction, outlet, below })
        {
            double sum = 0;
            for (int m = 0; m < 12; m++) sum += flow.Monthly(i, m);
            Near(sum / 12, flow.Annual(i), 2e-6, "The mean of the twelve rates must recover the saved annual flow.");
            Near(flow.Sample(i, 345), (flow.Monthly(i, 11) + flow.Monthly(i, 0)) / 2, 1e-6,
                "The December to January transition must interpolate continuously.");
            Near(flow.Sample(i, -15), flow.Sample(i, 345), 1e-6, "The 360-day hydrograph is periodic.");
            for (int m = 0; m < 12; m++) Near(flow.StageRatio(i, m * 30), flow.Monthly(i, m) / flow.Annual(i), 2e-6,
                "The stage multiplier samples the same discharge as the gauge.");
        }
        float inletPeak = 0, outletPeak = 0, inletLow = float.MaxValue, outletLow = float.MaxValue;
        for (int m = 0; m < 12; m++)
        {
            float incoming = flow.Monthly(junction, m), outgoing = flow.Monthly(outlet, m);
            Near(incoming, flow.Monthly(a, m) + flow.Monthly(b, m), 2e-6,
                "Rain-fed and snow-fed tributaries must sum at a junction in each month.");
            Near(outgoing, flow.Monthly(below, m), 2e-6, "The river below a lake must receive its outlet hydrograph.");
            inletPeak = Math.Max(inletPeak, incoming);
            outletPeak = Math.Max(outletPeak, outgoing);
            inletLow = Math.Min(inletLow, incoming);
            outletLow = Math.Min(outletLow, outgoing);
        }
        Near(flow.Annual(junction), flow.Annual(outlet), 1e-6, "Lake storage must conserve annual throughflow.");
        Check(outletPeak < inletPeak * .8 && outletLow > inletLow, "A lake must lower the crest and sustain dry-season outflow.");
        Check(flow.Monthly(a, 3) > 4 * flow.Monthly(a, 1), "The snowy tributary peaks after thaw.");
        Check(flow.Monthly(b, 8) > 3 * flow.Monthly(b, 3), "The rainy tributary peaks with its different wet season.");
        Console.WriteLine($"Junction/lake Q peak {inletPeak:F3}/{outletPeak:F3}, minimum {inletLow:F3}/{outletLow:F3}, annual {flow.Annual(outlet):F3}");
    }

    static byte[] WithoutWildlifeSection(byte[] version2)
    {
        const int magic = 13, digestBytes = 32, header = magic + 4 + digestBytes;
        // Empty v2 wildlife section is a bool and an Int32 count.
        var version1 = new byte[version2.Length - 5];
        Array.Copy(version2, version1, version1.Length);
        Array.Copy(BitConverter.GetBytes(version1.Length - header), 0, version1, magic, 4);
        Array.Copy(BitConverter.GetBytes(1), 0, version1, header, 4);
        using (var sha = SHA256.Create())
        {
            var digest = sha.ComputeHash(version1, header, version1.Length - header);
            Array.Copy(digest, 0, version1, magic + 4, digestBytes);
        }
        return version1;
    }

    static void RoutingAndSaves()
    {
        var world = Tributaries(out int snow, out int rain, out int junction, out int outlet, out int below);
        float[] sources = FlatRiverDischarge.BuildSources(world, default, world.Runoff, 1);
        float[] parallel = FlatRiverDischarge.BuildSources(world, default, null, 8);
        Check(sources.SequenceEqual(parallel), "Parallel cell water balances must be identical to serial ones.");
        AccumulateAnnual(world);
        FlatTerrainAnalysis.Build(world, default, false, riverDischarge: FlatRiverDischarge.Route(world, sources, default));
        var flow = world.Facts.RiverDischarge;
        CheckHydrograph(flow, snow, rain, junction, outlet, below);
        var current = FlatSave.Decode(FlatSave.Encode(world));
        foreach (int tile in new[] { snow, rain, junction, outlet, below })
            for (int m = 0; m < 12; m++)
                Check(flow.Monthly(tile, m) == current.Facts.RiverDischarge.Monthly(tile, m),
                    "New worlds must regenerate the exact seasonal flow cache from a save.");
        Check(world.Clone().Facts.RiverDischarge == flow, "World clones share the immutable historical climate normals.");

        // Rebuild the historical unweighted, gross-rain volume on the same graph.
        Array.Clear(world.Runoff, 0, world.Count);
        for (int i = 0; i < world.Count; i++)
            for (int m = 0; m < 12; m++) world.Runoff[i] += world.Precipitation[i * 12 + m] / 1200;
        AccumulateAnnual(world);
        byte[] v2 = FlatSave.Encode(world);
        foreach (byte[] file in new[] { WithoutWildlifeSection(v2), v2 })
        {
            var loaded = FlatSave.Decode(file);
            Check(FlatSave.Encode(loaded).SequenceEqual(v2), "Loading either historical save version preserves every authoritative field.");
            Check(loaded.Facts.RiverDischarge.Annual(outlet) == world.Runoff[outlet], "Legacy reach volume remains authoritative.");
            CheckHydrograph(loaded.Facts.RiverDischarge, snow, rain, junction, outlet, below);
        }
        Check(flow.StageRatio(-1, 0) == 1 && flow.Monthly(-1, 1) == 0, "Missing stations use neutral presentation.");
    }

    static void DefaultAtlas()
    {
        var world = FlatAtlasTerrain.Read(File.ReadAllBytes("Assets/ORBIS/Resources/ORBIS/WorldAtlas/Default.bytes"), "ORBIS-2D-2026", default);
        int rivers = 0, seasonal = 0;
        double maxError = 0;
        var flow = world.Facts.RiverDischarge;
        for (int i = 0; i < world.Count; i++)
        {
            if (world.Water[i] != WaterKind.River) continue;
            rivers++;
            double sum = 0;
            float low = float.MaxValue, high = 0;
            for (int m = 0; m < 12; m++)
            {
                float q = flow.Monthly(i, m);
                Check(FMath.Finite(q) && q >= 0, "All atlas monthly flows must be finite and nonnegative.");
                low = Math.Min(q, low);
                high = Math.Max(q, high);
                sum += q;
            }
            if (high > low * 2) seasonal++;
            maxError = Math.Max(maxError, Math.Abs(sum / 12 - world.Runoff[i]) / Math.Max(.001, world.Runoff[i]));
        }
        Console.WriteLine($"Atlas: {rivers} river cells, {seasonal} with peak > twice minimum, worst relative annual residual {maxError:E2}");
        Check(seasonal > 0 && maxError < 2e-6, "The default atlas exposes seasonal river regimes and conserves each annual flow.");
    }

    static void FirstFrameDiagonals()
    {
        foreach (int dx in new[] { -1, 1 }) foreach (int dy in new[] { -1, 1 })
        {
            var world = new WorldData(64, 32, "FIRST-FRAME-DIAGONALS", TerrainPreset.Continents);
            for (int i = 0; i < world.Count; i++)
            {
                world.Elevation[i] = world.WaterLevel[i] = 100;
                world.DrainOrder[i] = i;
            }
            int x = dx > 0 ? 63 : 0, y = 15;
            int a = world.Index(x, y), b = world.Index(x + dx, y + dy), c = world.Index(x + 2 * dx, y + dy);
            foreach (int i in new[] { a, b, c }) { world.Water[i] = WaterKind.River; world.Runoff[i] = 64; }
            world.Downstream[a] = b;
            world.Downstream[b] = c;
            FlatTerrainAnalysis.Build(world, default, false, 1);
            world.Facts.RiverCurves = null;
            int direction = 4 + (dx > 0 ? 1 : 0) + (dy > 0 ? 2 : 0);
            Check((world.Facts.Connections[a] & (1 << direction)) != 0 &&
                  (world.Facts.Connections[b] & (1 << (direction ^ 3))) != 0,
                "Both ends publish the diagonal link while the atlas is loading.");
            float cornerX = x + (dx > 0 ? 1 : 0), cornerY = y + (dy > 0 ? 1 : 0);
            for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2)
            {
                float coverage = FlatWaterGeometry.Coverage(world, cornerX + sx * .018f, cornerY + sy * .018f, out float depth);
                Check(coverage > .99f && depth > .5f, "All four quadrants at a diagonal bank corner have joined water and depth.");
            }
            for (int k = 0; k <= 40; k++)
            {
                float t = k / 40f;
                Check(FlatWaterGeometry.Coverage(world, x + .5f + dx * t, y + .5f + dy * t, out _) > .5f,
                    "The first-frame ribbon is continuous from one diagonal tile centre to the next.");
            }
            Check(FlatWaterGeometry.Coverage(world, cornerX + dx * .38f, cornerY - dy * .38f, out _) < .01f,
                "A dry cell beside the diagonal keeps its dry interior.");
        }
    }

    public static void Run()
    {
        var warm = Slope(30, 65);
        var cool = Slope(9, 65);
        double dryYield = OutletVolume(warm), humidYield = OutletVolume(cool);
        Console.WriteLine($"Equal rainfall and terrain, warm/cool outlet flows {dryYield:F3} / {humidYield:F3}");
        Check(dryYield < humidYield * .7, "Higher evaporative demand must reduce yield under identical precipitation and topography.");
        StorageBalance();
        AreaWeights();
        RoutingAndSaves();
        FirstFrameDiagonals();
        DefaultAtlas();
        Console.WriteLine("PASS seasonal water balance, spherical area, snowmelt, aquifer recession, tributaries, lake storage and v1/v2 saves");
    }
}
