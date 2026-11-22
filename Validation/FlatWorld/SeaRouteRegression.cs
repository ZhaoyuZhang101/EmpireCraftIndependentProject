using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Orbis.Flat;

public static class SeaRouteRegression
{
    static void Require(bool condition, string description)
    { if (!condition) throw new Exception(description); }

    public static void Run(string[] args)
    {
        if (args.Length > 2 && args.Length != 4)
            throw new ArgumentException("Use --sea-routes <width> <mouths.json> <paths.json> to export exact sailing paths.");
        GridRoutes();
        if (args.Length > 1 && args[1] == "grid") return;
        const string root = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
        var chart = FlatStraitNavigation.Read(File.ReadAllBytes(root + "EarthGlobalStraitShore.bytes"));
        var widths = args.Length > 1 ? new[] { int.Parse(args[1]) } :
            Array.ConvertAll(File.ReadAllLines(root + "PlayerWidths.txt"), int.Parse);
        if (Array.IndexOf(widths,2048) >= 0)
        {
            var oldChart = FlatStraitNavigation.Read(File.ReadAllBytes(root + "EarthStraitShore.bytes"));
            EarthRoutes(root,oldChart,2048,null,null,false);
        }
        foreach (var width in widths) EarthRoutes(root, chart, width,
            args.Length == 4 ? args[2] : null, args.Length == 4 ? args[3] : null);
    }

    static void GridRoutes()
    {
        var world = new WorldData(64, 32, "MARINE-ROUTE", TerrainPreset.Continents);
        var planner = new FlatSeaNavigation(world);
        int a = world.Index(17, 12), b = world.Index(18, 12), c = world.Index(19, 12), diagonal = world.Index(20, 13);
        foreach (int tile in new[] { a, b, c, diagonal, world.Index(63, 1), world.Index(0, 1), 4, 36 })
        {
            world.Water[tile] = WaterKind.ShallowOcean;
            world.Elevation[tile] = -1;
        }
        FlatSeaPoint[] Between(int i, int j) => planner.Find(planner.Center(i), planner.Center(j));
        Require(Between(a, c).Length == 3 && Between(c, a).Length == 3, "Missing connected shallow-water route.");
        Require(Between(c, diagonal).Length == 0, "A vessel crossed a dry corner.");
        Require(Between(world.Index(63, 1), world.Index(0, 1)).Length == 2, "Longitude seam took the long way around.");
        Require(Between(4, 36).Any(p => p.Latitude == -90), "Polar face cut across unrelated meridians.");
        world.Buildings[b] = Structure.Bridge; world.Water[b] = WaterKind.Lake;
        Require(Between(a, c).Length == 0, "An inland lake or land bridge joined two sea passages.");
        world.Water[b] = WaterKind.ShallowOcean; world.Elevation[b] = 18;
        Require(Between(a, c).Length == 0, "A raised dam with a stale marine label remained open.");
        Require(!planner.IsWater(new FlatSeaPoint(double.NaN, 0)) && !planner.IsWater(new FlatSeaPoint(0, 91)), "Invalid geographic pick.");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        try { planner.Find(planner.Center(a), planner.Center(c), stop.Token); }
        catch (OperationCanceledException) { Console.WriteLine("PASS navigable sea cells, dry corners, lake, dam, dateline, pole and cancellation"); return; }
        throw new Exception("Cancelled ship route still ran.");
    }

    static void EarthRoutes(string root, FlatStraitNavigation chart, int width, string mouths, string export, bool global = true)
    {
        var w = new WorldData(width, width/2, "ORBIS-2D-2026", TerrainPreset.Earth)
            { UsesSurveyedLakeBeds = true, UsesSurveyedStraits = true, UsesGlobalStraits = global };
        w.WaterReference = FlatWaterReference.Read(width, File.ReadAllBytes(root + $"Earth{width}.bytes"),
            File.ReadAllBytes(root + $"EarthWater{width}.bytes"),true,true,global);
        for (int i = 0; i < w.Count; i++) w.Elevation[i] = w.WaterReference.OriginalBed(i);
        var ocean = FlatOcean.Find(w, default);
        for (int i = 0; i < w.Count; i++)
        {
            if (ocean[i]) w.Water[i] = HeightBands.Ocean(w.Elevation[i]);
            else if (w.WaterReference.At(i) >= w.Elevation[i]) w.Water[i] = WaterKind.Lake;
        }
        var planner = new FlatSeaNavigation(w, chart);
        var aegean = new FlatSeaPoint(25.65, 40.00);
        var blackSea = new FlatSeaPoint(29.48, 41.64);
        var path = planner.Find(aegean, blackSea);
        CheckEndpoints(path, aegean, blackSea);
        for (int i = 1; i < path.Length; i++)
            Require(chart.SegmentIsWater(w, path[i-1], path[i]), $"Earth {width}: dry land inside a sailing leg at {i}.");
        Require(!planner.IsWater(new FlatSeaPoint(26.755, 40.525)), "Gallipoli isthmus accepted as a ship destination.");
        Require(planner.IsWater(new FlatSeaPoint(29.0675, 41.11)), "Water pick inside the Bosporus rejected.");
        if (mouths != null)
        {
            var paths = new Dictionary<string, double[][]>();
            var gates = JsonSerializer.Deserialize<Dictionary<string, double[][]>>(File.ReadAllText(mouths));
            foreach (var (name, ends) in gates)
            {
                var a = new FlatSeaPoint(ends[0][0], ends[0][1]);
                var b = new FlatSeaPoint(ends[1][0], ends[1][1]);
                var track = planner.Find(a, b);
                Require(track.Length >= 2, $"Earth {width}: missing route for {name}.");
                CheckEndpoints(track, a, b);
                paths.Add(name, track.Select(p => new[] { p.Longitude, p.Latitude }).ToArray());
            }
            File.WriteAllText(export, JsonSerializer.Serialize(paths));
        }
        if (width == 512)
        {
            var pick = new FlatSeaPoint(28.11, 40.79);
            int coarse = w.Index((int)((pick.Longitude+180)*width/360), (int)((pick.Latitude+90)*width/360));
            Require(!FlatSeaNavigation.IsMarineCell(w, coarse), "Expected the measured Marmara water pick to lie in a coarse dry cell.");
            CheckEndpoints(planner.Find(pick, blackSea), pick, blackSea);
            var atlantic = new FlatSeaPoint(-12, 36);
            var through = planner.Find(atlantic, blackSea);
            CheckEndpoints(through, atlantic, blackSea);
            for (int i = 1; i < through.Length; i++)
                if (chart.RegionAt(through[i-1]) != 0 || chart.RegionAt(through[i]) != 0)
                    Require(chart.SegmentIsWater(w, through[i-1], through[i]), "Dry leg entering or leaving a surveyed region.");
            int throat = w.Index((int)((29.058+180)*width/360), (int)((41.083+90)*width/360));
            w.Elevation[throat + 1] = 1700; w.Water[throat + 1] = WaterKind.Land;
            CheckEndpoints(planner.Find(aegean, blackSea), aegean, blackSea);
            w.Elevation[throat] = 1700; w.Water[throat] = WaterKind.Land;
            Require(planner.Find(aegean, blackSea).Length == 0, "Unedited banks became a shortcut around a Bosporus dam.");
        }
        if (width == 2048)
        {
            int dam = w.Index((int)((29.09+180)*width/360), (int)((41.22+90)*width/360));
            w.Elevation[dam] = 29; w.Water[dam] = WaterKind.Land;
            Require(planner.Find(aegean, blackSea).Length == 0, "Ship crossed the raised Bosporus dam.");
        }
        Console.WriteLine($"PASS V{(global?6:5)} Earth {width}: Aegean to Black Sea via {path.Length} navigable geographic waypoints.");
    }

    static void CheckEndpoints(FlatSeaPoint[] path, FlatSeaPoint a, FlatSeaPoint b)
    {
        Require(path.Length >= 2 && path[0].Longitude == a.Longitude && path[0].Latitude == a.Latitude &&
            path[^1].Longitude == b.Longitude && path[^1].Latitude == b.Latitude, "Exact selected sea points did not connect.");
    }
}
