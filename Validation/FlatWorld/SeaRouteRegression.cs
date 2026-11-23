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
        byte[] Asset(string name)=>File.ReadAllBytes(root+name+".bytes");
        var surface = FlatGeographicSurface.Read(Asset("EarthGlobalStraitShore"),Asset("EarthOceanShore"),
            Asset("EarthMajorLakes"),Asset("EarthLakeDetail"),Asset("EarthPolarCoast"));
        var widths = args.Length > 1 ? new[] { int.Parse(args[1]) } :
            Array.ConvertAll(File.ReadAllLines(root + "PlayerWidths.txt"), int.Parse);
        if (Array.IndexOf(widths,2048) >= 0 || Array.IndexOf(widths,256) >= 0)
        {
            var oldSurface=FlatGeographicSurface.Read(Asset("EarthStraitShore"),null,Asset("EarthMajorLakes"),Asset("EarthLakeDetail"),Asset("EarthPolarCoast"));
            if(Array.IndexOf(widths,2048)>=0)EarthRoutes(root,oldSurface.Regional,2048,null,null,false,oldSurface);
            if(Array.IndexOf(widths,256)>=0)LegacyModelShore(root,oldSurface);
        }
        if(Array.IndexOf(widths,512)>=0)GeneratedSurfaces(root,surface);
        foreach (var width in widths) EarthRoutes(root, surface.Regional, width,
            args.Length == 4 ? args[2] : null, args.Length == 4 ? args[3] : null, surface:surface);
    }

    static void LegacyModelShore(string root,FlatGeographicSurface surface)
    {
        var w=new WorldData(256,128,"ORBIS-2D-2026",TerrainPreset.Earth) { UsesSurveyedLakeBeds=true,UsesSurveyedStraits=true };
        byte[] Asset(string name)=>File.ReadAllBytes(root+name+".bytes");
        w.WaterReference=FlatWaterReference.Read(256,Asset("Earth256"),Asset("EarthWater256"),true,true,false);
        for(int i=0;i<w.Count;i++)w.Elevation[i]=w.WaterReference.OriginalBed(i);
        FlatGenerator.Rebuild(w,default);surface.Attach(w);
        var planner=new FlatSeaNavigation(w);
        FlatSeaPoint Point(double x,double y)=>new FlatSeaPoint(x*360/w.Width-180,y*180/w.Height-90);
        // Off Brazil, these two points straddle a modeled headland within V5 sea tile 11101.
        var a=Point(93+1.5/7,43+.5/7);var b=Point(93.5,43+5.5/7);
        var headland=Point(93+2.5/7,43+3.0/7);
        Require(surface.Regional.RegionAt(headland)==0 && FlatGeographicSurface.IsGroundAt(w,headland) && !planner.IsWater(headland),
            "V5 regional fallback accepted a visible headland as a sea pick.");
        var path=planner.Find(a,b);CheckEndpoints(path,a,b);
        Require(!planner.SegmentIsWater(a,b) && path.Length>2,"V5 regional fallback cut across the modeled headland.");
        CheckModelLegs(w,path);
        var inlet=Point(77+.5/7,43+.5/7);
        Require(w.Water[w.Index(77,43)]==WaterKind.Land && planner.IsWater(inlet),"V5 regional fallback rejected a water pick in a dry Chilean tile.");
        Console.WriteLine("PASS V5 generated Earth: model shoreline picks, headland detour and continuous sailing legs outside the historical strait charts");
    }

    static void GeneratedSurfaces(string root,FlatGeographicSurface surface)
    {
        var random=FlatGenerator.Generate(256,128,"shore-archipelago-2026",TerrainPreset.Archipelago,default);
        FlatSeaPoint CellPoint(double x,double y)=>new FlatSeaPoint(x*360/random.Width-180,y*180/random.Height-90);
        var nav=new FlatSeaNavigation(random);
        // All three points lie in procedural shallow-sea tile 10753.
        double column=1+6.5/7;
        var a=CellPoint(column,42+.5/7);var b=CellPoint(column,42+6.5/7);var inland=CellPoint(column,42.5);
        Require(random.Water[10753]==WaterKind.ShallowOcean && nav.IsWater(a) && nav.IsWater(b) &&
            !nav.IsWater(inland) && FlatGeographicSurface.IsGroundAt(random,inland),"Procedural island clicks disagree with the continuous shore.");
        var detour=nav.Find(a,b);CheckEndpoints(detour,a,b);
        Require(detour.Length>2 && !nav.SegmentIsWater(a,b),"Procedural route crossed a dry finger between two wet endpoints.");
        CheckModelLegs(random,detour);
        byte[] Asset(string name)=>File.ReadAllBytes(root+name+".bytes");
        var earth=FlatGenerator.Generate(512,256,"ORBIS-2D-2026",TerrainPreset.Earth,default,Asset("Earth512"),Asset("EarthWater512"),Asset("EarthRivers"));
        const int westBalaton=99608,eastBalaton=99609;
        Require(FlatWildlifeSimulation.CanStep(earth,westBalaton,eastBalaton,false),"Expected two coarse walkable cells beside Lake Balaton.");
        surface.Attach(earth);
        Require(!FlatWildlifeSimulation.CanStep(earth,westBalaton,eastBalaton,false) && FlatWildlifeSimulation.CanStep(earth,westBalaton,eastBalaton,true),
            "Ground animal crossed a surveyed lake or a flying animal lost its path.");
        const int west=97923,east=97924;
        Require(!FlatGeographicSurface.GroundEdge(earth,west,east),"Unbridged Great Lakes crossing accepted.");
        earth.Buildings[east]=Structure.Bridge;
        Require(FlatPathfinder.Find(earth,west,east).Length==2,"Built lake bridge no longer joins the two adjacent tiles.");
        earth.Buildings[east]=Structure.None;
        Require(!FlatGeographicSurface.GroundEdge(earth,west,east),"Removed bridge left a passable cached edge.");
        var gyaros=new FlatSeaPoint(24.71146,37.61164);
        Require(FlatGeographicSurface.IsGroundAt(earth,gyaros),"Missing Gyaros before edit.");
        earth.Elevation[92963]=-2200;
        FlatGenerator.Rebuild(earth,default);
        var edited=new FlatSeaNavigation(earth);
        Require(!FlatGeographicSurface.IsGroundAt(earth,gyaros) && edited.IsWater(gyaros),"Rebuilt edit retained the original island.");
        Require(edited.SegmentIsWater(new FlatSeaPoint(24.63468,37.61164),new FlatSeaPoint(24.76622,37.61164)),"Submerged island still blocked a route.");
        Console.WriteLine("PASS generated archipelago sub-cell selection and detour, Lake Balaton wildlife and flight, lake bridge construction/removal, Earth elevation edit and route invalidation");
    }

    static void CheckModelLegs(WorldData w,FlatSeaPoint[] path)
    {
        for(int i=1;i<path.Length;i++)
        {
            var p=path[i-1];var q=path[i];
            for(int s=0;s<=512;s++)
            {
                double t=s/512.0,lon=p.Longitude+(q.Longitude-p.Longitude)*t,lat=p.Latitude+(q.Latitude-p.Latitude)*t;
                Require(FlatSurfaceSampling.SampleShore(w,(float)((lon+180)*w.Width/360),(float)((lat+90)*w.Height/180))>0,
                    "A sailing leg left the displayed model shoreline.");
            }
        }
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

    static void EarthRoutes(string root, FlatStraitNavigation chart, int width, string mouths, string export, bool global = true, FlatGeographicSurface surface=null)
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
        surface?.Attach(w);
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
            if(surface!=null)
            {
                foreach(var trial in new[] {
                    (24.63468,24.71146,24.76622,37.61164), // Gyaros
                    (25.75553,25.80878,25.86705,37.11845), // Makares archipelago
                    (-160.27304,-160.14218,-160.02644,21.90219) }) // Niihau
                {
                    var a=new FlatSeaPoint(trial.Item1,trial.Item4);
                    var island=new FlatSeaPoint(trial.Item2,trial.Item4);var b=new FlatSeaPoint(trial.Item3,trial.Item4);
                    int Cell(FlatSeaPoint p)=>w.Index((int)((p.Longitude+180)*width/360),(int)((p.Latitude+90)*width/360));
                    Require(Cell(a)==Cell(b) && Cell(a)==Cell(island) && FlatSeaNavigation.IsMarineCell(w,Cell(a)),"Expected three receivers in one ocean tile.");
                    Require(FlatGeographicSurface.IsGroundAt(w,island) && !planner.IsWater(island) && planner.IsWater(a) && planner.IsWater(b),"Subcell selection disagrees with the geographical island.");
                    Require(!chart.SegmentIsWater(w,a,b),"Sea segment crossed a sub-cell island.");
                    var around=planner.Find(a,b);CheckEndpoints(around,a,b);
                    Require(around.Length>2 && planner.Find(a,island).Length==0,"Water route accepted an island interior.");
                    for(int j=1;j<around.Length;j++)Require(chart.SegmentIsWater(w,around[j-1],around[j]),"Unverified sailing leg around an island.");
                }
                Console.WriteLine("PASS three sub-cell islands, exact endpoints and every sailing leg; inshore water is selectable in a coarse dry tile.");
            }
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
