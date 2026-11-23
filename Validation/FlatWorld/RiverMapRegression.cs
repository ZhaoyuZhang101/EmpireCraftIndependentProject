using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Flat;

public static class RiverMapRegression
{
    const string Data = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
    // Natural Earth and GRWL controls on surveyed player rivers.
    static readonly (string name, double lon, double lat)[] Sites = {
        ("Amazonas", -64.596099, -3.387709), ("Nile", 31.157319, 27.218573),
        ("Yangtze", 107.512794, 29.881527), ("Lena", 106.699718, 57.045111),
        ("Yukon", -159.773834, 63.051540), ("Huangpu at the Bund", 121.488157, 31.239858)
    };

    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    static FlatRiverSample Sample(WorldData w, double lon, double lat)
    {
        return w.Facts.RiverCurves.Sample((float)((lon / 360 + .5) * w.Width),
            (float)((lat / 180 + .5) * w.Height));
    }

    public static void CheckWidthFormat()
    {
        // Full channel widths in metres. Two stations one degree apart on the equator.
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        {
            writer.Write(0x3256524f); writer.Write(1); writer.Write((byte)2); writer.Write((ushort)2);
            foreach (var station in new[] { (x: 10.0, width: (ushort)300), (x: 11.0, width: (ushort)1500) })
            {
                writer.Write((uint)Math.Round((station.x / 360 + .5) * 16777215));
                writer.Write((uint)Math.Round(.5 * 16777215));
                writer.Write(station.width);
            }
        }
        byte[] bytes = buffer.ToArray();
        var reference = FlatEarthRiverReference.Read(bytes);
        foreach (int width in new[] { 256, 512 })
        {
            var w = new WorldData(width, width/2, "WIDTH", TerrainPreset.Earth);
            w.WaterReference = FlatWaterReference.Read(width, File.ReadAllBytes(Data + $"Earth{width}.bytes"),
                File.ReadAllBytes(Data + $"EarthWater{width}.bytes"), true, true);
            for (int i=0; i<w.Count; i++) w.Elevation[i]=w.WaterReference.OriginalBed(i);
            w.RiverReference = reference;
            w.Facts = new FlatTerrainFacts(w.Count);
            w.Facts.RiverCurves = FlatRiverCurves.Build(w,w.Facts,default);
            foreach (double longitude in new[] { 10.0, 10.25, 10.5, 10.75, 11.0 })
            {
                double fullMetres = 300 + 1200 * (longitude-10);
                var sample = Sample(w, longitude, 0);
                double radius = .5 * fullMetres / 40030200 * width;
                Check(Math.Abs(radius - sample.HalfWidth) < .000006 && sample.Distance < 0,
                    $"ORV2 {width}: tapered full width {fullMetres} m at {longitude} E");
            }
            Check(Sample(w,10,.003).Distance>0 && Sample(w,11,.003).Distance<0,
                "bank position follows the interpolated observed width");
            // An isolated 900 m channel halfway along the transect. Its drawn
            // half-width is 900 m while its surveyed half-width is still 450 m.
            float x=(float)((10.5/360+.5)*width);
            float centreY=reference.Y(0,w.Height);
            float drawnRadius=900f/40030200f*width;
            float DrawnEdge(float y)
            {
                int tile=w.Index((int)x,(int)y);
                return FlatWaterGeometry.RiverDistance(w,tile,x-(int)x,y-(int)y);
            }
            foreach(int side in new[]{-1,1})
                Check(DrawnEdge(centreY+side*.95f*drawnRadius)<0 &&
                      DrawnEdge(centreY+side*1.05f*drawnRadius)>0,
                    $"Earth {width}: mapped banks enclose twice the surveyed channel width");
            var curves=w.Facts.RiverCurves;
            int column=(int)(x*curves.OverviewWidth/width);
            double StrokeProfile(byte[] ink)
            {
                double total=0;
                for(int row=0;row<curves.OverviewWidth/2;row++)
                    total+=ink[row*curves.OverviewWidth+column]/(255.0*(.43+.57*.8));
                return total;
            }
            double profile=StrokeProfile(curves.DrawOverview(default));
            double export=StrokeProfile(curves.DrawExportOverview(default));
            double oldStroke=2*(.46+1.10*.8)*(1+.16);
            Check(Math.Abs(profile-.5*oldStroke)<.03 && Math.Abs(export-2*oldStroke)<.22 &&
                  Math.Abs(profile/export-.25)<.01,
                $"Earth {width}: globe river {profile:F2} texels, full export {export:F2}, ratio {profile/export:P1}");
        }
        float Width(TerrainPreset preset,float unfolding) => 1-FlatRiverCurves.GlobeWidthReduction(preset,unfolding);
        Check(Width(TerrainPreset.Earth,0)==.25f && Width(TerrainPreset.Earth,.5f)==.625f && Width(TerrainPreset.Earth,1)==1,
            "Earth unfolds from 0.5x original river width through 1.25x to 2x local water and banks");
        foreach(TerrainPreset preset in Enum.GetValues(typeof(TerrainPreset)))
            if(preset!=TerrainPreset.Earth)
                Check(Width(preset,0)==1 && Width(preset,.5f)==1 && Width(preset,1)==1,
                    preset+" retains its own width throughout the globe-to-local transition");
        // A bad coordinate must be rejected before it reaches the spatial index.
        bytes[14]=0xff;
        bool invalid=false;
        try { FlatEarthRiverReference.Read(bytes); } catch(InvalidDataException) { invalid=true; }
        Check(invalid,"reject a surveyed coordinate outside the 24-bit range");
        CheckBendTangents(500);
        CheckBendTangents(6000);
        CheckEditedEstuaryStage();
    }

    static void CheckEditedEstuaryStage()
    {
        const int width=512;
        var w=new WorldData(width,width/2,"ESTUARY-STAGE",TerrainPreset.Earth);
        w.WaterReference=FlatWaterReference.Read(width,File.ReadAllBytes(Data+"Earth512.bytes"),
            File.ReadAllBytes(Data+"EarthWater512.bytes"));
        for(int i=0;i<w.Count;i++) w.Elevation[i]=w.WaterReference.OriginalBed(i);
        using var survey=new MemoryStream();
        using(var writer=new BinaryWriter(survey,System.Text.Encoding.UTF8,true))
        {
            writer.Write(0x3256524f);writer.Write(1);writer.Write((byte)1);writer.Write((ushort)2);
            foreach(double lon in new[]{120.0,122.0})
            {
                writer.Write((uint)Math.Round((lon/360+.5)*16777215));
                writer.Write((uint)Math.Round((31.5/180+.5)*16777215));
                writer.Write((ushort)56000);
            }
        }
        w.RiverReference=FlatEarthRiverReference.Read(survey.ToArray());
        // At 512 this synthetic mouth spans the same fraction of a cell as a
        // wide estuary on the larger maps. A remote edit activates the stage atlas.
        w.Elevation[0]++;
        FlatGenerator.Rebuild(w,default);
        var curves=w.Facts.RiverCurves;
        float x=(121f/360+.5f)*width, y=(31.5f/180+.5f)*w.Height;
        var basic=curves.Sample(x,y);
        Check(curves.Seasons!=null && basic.HalfWidth>.24f && Math.Abs(basic.HalfWidth-curves.SampleAtDay(x,y,60).HalfWidth)<.00001,
            "a distant terrain edit preserves an ungauged estuary at its reference stage");
    }

    static void CheckBendTangents(ushort fullWidth)
    {
        const int width=256;
        var w=new WorldData(width,width/2,"SURVEY-BEND",TerrainPreset.Earth);
        w.WaterReference=FlatWaterReference.Read(width,File.ReadAllBytes(Data+"Earth256.bytes"),
            File.ReadAllBytes(Data+"EarthWater256.bytes"));
        for(int i=0;i<w.Count;i++) w.Elevation[i]=w.WaterReference.OriginalBed(i);
        // East then north, crossing the date line at 60 degrees latitude.
        using var bytes=new MemoryStream();
        using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))
        {
            writer.Write(0x3256524f); writer.Write(1); writer.Write((byte)1); writer.Write((ushort)3);
            foreach(var p in new[]{(179.0,60.0),(-179.0,60.0),(-179.0,61.0)})
            {
                writer.Write((uint)Math.Round((p.Item1/360+.5)*16777215));
                writer.Write((uint)Math.Round((p.Item2/180+.5)*16777215)); writer.Write(fullWidth);
            }
        }
        w.RiverReference=FlatEarthRiverReference.Read(bytes.ToArray());
        w.Facts=new FlatTerrainFacts(w.Count);
        w.Facts.RiverCurves=FlatRiverCurves.Build(w,w.Facts,default);
        var incoming=Sample(w,-179.01,60);
        var outgoing=Sample(w,-179,60.005);
        if(fullWidth==500)
        {
            foreach(var s in new[]{incoming,outgoing})
                Check(s.FlowY>0 && Math.Abs(.5*s.FlowX/s.FlowY-1)<.04,
                    "both sides of a surveyed bend share the physical 45-degree current");
            return;
        }
        var curves=w.Facts.RiverCurves;
        // On the northbound reach the east/west offset must represent the
        // same ground distance at 60.6 degrees as it does at the equator.
        const double lat=60.6;
        double bankDegrees=fullWidth/(40030200.0/360*Math.Cos(lat*Math.PI/180));
        float row=(float)((lat/180+.5)*w.Height);
        foreach(int side in new[]{-1,1})
        {
            float Edge(double factor) => curves.SampleMap((float)(((-179+side*factor*bankDegrees)/360+.5)*width),row).Distance;
            Check(Edge(.95)<0&&Edge(1.05)>0,"mapped east and west banks retain their ground width at high latitude");
        }
        int tile=w.Index((int)(width/360.0),(int)(150.0*width/360));
        double largestTurn=0;
        var local=Enumerable.Range(0,curves.TileSegmentCount(tile)).Select(i=>curves.TileSegment(tile,i)).ToArray();
        foreach(var a in local) foreach(var b in local)
        {
            double joinX=a.B.X-b.A.X;
            joinX-=Math.Round(joinX/width)*width;
            if(Math.Abs(joinX)>.0001||Math.Abs(a.B.Y-b.A.Y)>.0001)continue;
            double ax=.5*(a.B.X-a.A.X), ay=a.B.Y-a.A.Y, bx=.5*(b.B.X-b.A.X), by=b.B.Y-b.A.Y;
            double norm=Math.Sqrt((ax*ax+ay*ay)*(bx*bx+by*by));
            if(norm<1e-9)continue;
            largestTurn=Math.Max(largestTurn,Math.Acos(Math.Clamp((ax*bx+ay*by)/norm,-1,1))*180/Math.PI);
        }
        Check(curves.SegmentCount>2 && largestTurn>0 && largestTurn<38 && Sample(w,-179,60).Distance<0,
            $"surveyed date-line bend remains wet and turns gradually (maximum {largestTurn:F1} degrees)");
    }

    public static void Run()
    {
        const int width = 512;
        var bed = File.ReadAllBytes(Data + "Earth512.bytes");
        var water = File.ReadAllBytes(Data + "EarthWater512.bytes");
        var rivers = File.ReadAllBytes(Data + "EarthRivers.bytes");
        var world = FlatGenerator.Generate(width, width / 2, "RIVER-MAP", TerrainPreset.Earth, default, bed, water, rivers);
        int hits = 0;
        foreach (var site in Sites)
        {
            var sample = Sample(world, site.lon, site.lat);
            if (sample.Distance < 0) hits++;
            Console.WriteLine($"{site.name}: edge {sample.Distance:F4} cells, radius {sample.HalfWidth:F4}");
        }
        Check(hits == Sites.Length, $"mapped river covers all {Sites.Length} surveyed positions (found {hits})");
        Check(Enum.TryParse<MapLayer>("Rivers", out _), "player river network map layer");
        Check(Sample(world,15,23).Distance>0 && Sample(world,-140,5).Distance>0,
            "desert and open-Pacific controls stay outside river banks");
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var overview=world.Facts.RiverCurves.DrawOverview(default);
        int pixels=world.Facts.RiverCurves.OverviewWidth;
        foreach(var site in Sites)
        {
            int x=(int)((site.lon/360+.5)*pixels),y=(int)((site.lat/180+.5)*pixels/2);
            Check(overview[y*pixels+x]>70,site.name+" visible in filtered global overview");
        }
        Console.WriteLine($"Earth overview {pixels}x{pixels/2}, {world.Facts.RiverCurves.SegmentCount} segments, {clock.Elapsed.TotalMilliseconds:F1}ms");
        var saved=FlatSave.Encode(world);
        var loaded=FlatSave.Decode(saved);
        FlatEarthRiverReference.Restore(loaded,bed,water,rivers,default);
        Check(saved.SequenceEqual(FlatSave.Encode(loaded)) && Sites.All(site=>Sample(loaded,site.lon,site.lat).Distance<0),
            "Earth reload restores the survey with identical serialized terrain, water and climate");
        CheckDeltaFloodplain(world,loaded,bed,water,rivers);

        var edited=loaded.Clone();
        int col=(int)((Sites[0].lon/360+.5)*width),row=(int)((Sites[0].lat/180+.5)*width/2);
        edited.Elevation[row*width+col]+=120;
        FlatGenerator.Rebuild(edited,default);
        var model=edited.Clone();model.RiverReference=null;
        model.Facts.RiverCurves=FlatRiverCurves.Build(model,model.Facts,default);
        Check(Sample(edited,Sites[0].lon,Sites[0].lat).Distance==Sample(model,Sites[0].lon,Sites[0].lat).Distance &&
            Sample(edited,Sites[1].lon,Sites[1].lat).Distance==Sample(world,Sites[1].lon,Sites[1].lat).Distance,
            "edited Amazon cell follows rerouted water while the Nile stays on its mapped course");
        var editInk=edited.Facts.RiverCurves.DrawOverview(default);
        int at=(int)((Sites[0].lat/180+.5)*pixels/2)*pixels+(int)((Sites[0].lon/360+.5)*pixels);
        Check(editInk[at]==model.Facts.RiverCurves.DrawOverview(default)[at],"edit is reflected in the overview");

        var a=FlatGenerator.Generate(128,64,"RIVERS-A",TerrainPreset.Continents,default,null,null,rivers);
        var b=FlatGenerator.Generate(128,64,"RIVERS-B",TerrainPreset.Continents,default);
        var inkA=a.Facts.RiverCurves.DrawOverview(default);
        var inkB=b.Facts.RiverCurves.DrawOverview(default);
        Check(inkA.SequenceEqual(a.Facts.RiverCurves.DrawExportOverview(default)),
            "procedural globe and export retain the identical network stroke");
        Check(!a.Facts.RiverCurves.HasSurvey && a.RiverReference==null && inkA.Any(x=>x>0) && inkB.Any(x=>x>0) &&
            !inkA.SequenceEqual(inkB),"two procedural seeds draw distinct simulated drainage networks");
        Check(inkA.SequenceEqual(FlatSave.Decode(FlatSave.Encode(a)).Facts.RiverCurves.DrawOverview(default)),
            "random save reproduces identical river ink");
        CheckSeam(world);
        bool rejected=false;
        try { FlatEarthRiverReference.Read(rivers.Take(rivers.Length-1).ToArray()); } catch(IOException) {rejected=true;}
        catch(InvalidDataException){rejected=true;}
        Check(rejected,"reject truncated geographic river data");
    }

    static void CheckSeam(WorldData world)
    {
        // This fixture crosses the date line at 65N. Natural Earth's current
        // release splits such features into separate parts before publication.
        var data=new MemoryStream();
        using(var writer=new BinaryWriter(data,System.Text.Encoding.UTF8,true))
        {
            writer.Write(0x3156524f);writer.Write(1);writer.Write((byte)2);writer.Write((ushort)2);
            foreach(double lon in new[]{179.0,-179.0})
            {
                writer.Write((ushort)Math.Round((lon/360+.5)*65535));
                writer.Write((ushort)Math.Round((65.0/180+.5)*65535));
            }
        }
        var sampleWorld=world.Clone();
        sampleWorld.RiverReference=FlatEarthRiverReference.Read(data.ToArray());
        sampleWorld.Facts.RiverCurves=FlatRiverCurves.Build(sampleWorld,sampleWorld.Facts,default);
        Check(Sample(sampleWorld,179.97,65).Distance<0 && Sample(sampleWorld,-179.97,65).Distance<0 &&
            Sample(sampleWorld,0,65).Distance>1,"high-latitude date-line reach takes the short arc");
        var ink=sampleWorld.Facts.RiverCurves.DrawOverview(default);
        int width=sampleWorld.Facts.RiverCurves.OverviewWidth,y=(int)((65.0/180+.5)*width/2);
        Check(ink[y*width]>100 && ink[y*width+width-1]>100 && ink[y*width+width/2]==0,
            "overview wraps both date-line edges without a stripe through Greenwich");
    }

    static void CheckDeltaFloodplain(WorldData world,WorldData loaded,byte[] bed,byte[] water,byte[] rivers)
    {
        int Cell(double lon,double lat) => world.Index((int)((lon+180)*world.Width/360),
            (int)((lat+90)*world.Height/180));
        int farm=Cell(30.99,30.78), westernDesert=Cell(29.1,29.7), easternDesert=Cell(31.95,29.9);
        var f=world.Facts;
        Console.WriteLine($"Tanta floodplain: {world.Elevation[farm]} m, {world.Climates[farm]}, " +
            $"{world.Biomes[farm]}, {f.Materials[farm]}, {f.Vegetation[farm]}, soil {f.Wetness[farm]:F2}");
        Check(world.Water[farm]!=WaterKind.Lake && f.Materials[farm]==GroundMaterial.Silt &&
            f.Forms[farm]==Landform.Floodplain && f.Vegetation[farm]==VegetationCover.Grassland && f.Wetness[farm]>.45f,
            "central cultivated Nile delta has moist alluvial herbaceous cover");
        foreach(int desert in new[]{westernDesert,easternDesert})
            Check(f.Materials[desert]!=GroundMaterial.Silt && f.Vegetation[desert]!=VegetationCover.Grassland,
                "adjacent Egyptian desert stays outside the agricultural floodplain");
        Check(f.Materials[farm]==loaded.Facts.Materials[farm] && f.Forms[farm]==loaded.Facts.Forms[farm] &&
            f.Vegetation[farm]==loaded.Facts.Vegetation[farm] && f.Wetness[farm]==loaded.Facts.Wetness[farm],
            "Earth reload reproduces delta ecology from the saved geography");
        for(int month=0;month<12;month++)
        {
            int offset=FlatEcology.SeasonOffset(world,farm,month);
            for(int channel=0;channel<4;channel++)
                if(f.VegetationSeasons[offset+channel]!=loaded.Facts.VegetationSeasons[offset+channel])
                    throw new Exception("Reload changed Nile delta seasonal growth/foliage/snow/soil");
        }
        var historic=world.Clone();
        historic.UsesSurveyedLakeBeds=historic.UsesSurveyedStraits=historic.UsesGlobalStraits=false;
        historic.Biomes[farm]=Biome.Beach;
        historic.Fertility[farm]=.137f;
        byte[] old=FlatSave.Encode(historic);
        var restored=FlatSave.Decode(old);
        FlatEarthRiverReference.Restore(restored,bed,water,rivers,default);
        Check(restored.Biomes[farm]==Biome.Beach && restored.Fertility[farm]==.137f &&
            restored.Facts.Vegetation[farm]==VegetationCover.Grassland && old.SequenceEqual(FlatSave.Encode(restored)),
            "V3 Earth recovers surveyed field cover and preserves the original save labels and bytes");
    }

    // Exercise the actual player shoreline decoder and indexed ribbon at all
    // preset scales without allocating the monthly climate analysis/renderer.
    public static void RunMouths(int width)
    {
        var w = new WorldData(width, width/2, "RIVER-MOUTHS", TerrainPreset.Earth);
        w.WaterReference = FlatWaterReference.Read(width, File.ReadAllBytes(Data+$"Earth{width}.bytes"),
            File.ReadAllBytes(Data+$"EarthWater{width}.bytes"));
        w.UsesSurveyedLakeBeds=w.WaterReference.UsesSurveyedLakeBeds;
        w.UsesSurveyedStraits=w.WaterReference.UsesSurveyedStraits;
        w.UsesGlobalStraits=w.WaterReference.UsesGlobalStraits;
        for (int i=0; i<w.Count; i++) w.Elevation[i] = w.WaterReference.OriginalBed(i);
        var marine = FlatOcean.Find(w, default);
        for (int i=0; i<w.Count; i++)
        {
            if (marine[i]) w.Water[i] = HeightBands.Ocean(w.Elevation[i]);
            else if (w.WaterReference.At(i) >= w.Elevation[i]) w.Water[i] = WaterKind.Lake;
        }
        w.RiverReference = FlatEarthRiverReference.Read(File.ReadAllBytes(Data+"EarthRivers.bytes"));
        w.Facts = new FlatTerrainFacts(w.Count);
        w.Facts.RiverCurves = FlatRiverCurves.Build(w,w.Facts,default);
        var chart = FlatStraitNavigation.Read(File.ReadAllBytes(Data+
            (w.UsesGlobalStraits?"EarthGlobalStraitShore.bytes":"EarthStraitShore.bytes")));
        var routes = new[] {
            ("Rosetta",30.36723,31.46772,30.38,31.55,.0012), ("Damietta",31.8442,31.5271,31.86,31.57,.0012),
            ("Yangtze",121.858226,31.414904,122.02,31.415,.004), ("Huang",119.23476,37.77714,119.29,37.79,.002),
            ("Huangpu / Wusongkou",121.517661,31.397444,121.58,31.45,.0015),
            ("Xi / Hengmen",113.59769,22.59360,113.70,22.53,.002), ("Xi / Modaomen",113.18139,22.54373,113.46,21.95,.003),
            ("Qiantang",120.20023,30.22262,121.44,30.41,.004), ("Padma / Meghna",90.25,23.462,90.62,22.0,.005),
            ("Amazon", -52.712,-1.584,-49.75,.60,.006), ("Mississippi",-89.41356,28.93517,-89.48,28.88,.002),
            ("Mekong / Bassac",105.83798,10.00499,106.30,9.47,.003),
        };
        var failures=new List<string>();
        foreach (var (name,lon,lat,seaLon,seaLat,step) in routes)
        {
            try { CheckMouth(w,chart,name,lon,lat,seaLon,seaLat,step); }
            catch(Exception e) { failures.Add(e.Message); }
        }
        foreach (var (name,lon,lat) in new[] {("land between Nile branches",30.94,30.80),
            ("Chongming Island",121.58,31.622),("upper Huangpu fork bank",121.084664,31.022586),
            ("Pearl delta bank",113.38,22.53),("Caspian basin",51.0,42.0)})
        {
            chart.TryWater(w,new FlatSeaPoint(lon,lat),out bool sea);
            Check(!sea && Sample(w,lon,lat).Distance>0, $"Earth {width} {name} remains outside the marine and river channels");
        }
        if(failures.Count!=0)throw new Exception(string.Join(Environment.NewLine,failures));
        // The Yangtze's lower tidal reach is traced inside the Natural Earth
        // marine polygon. Preserve its Landsat width in hydrology while the
        // polygon draws its islands and both banks. The adjoining inland
        // Huangpu must still supply the local river map at Wusongkou.
        FlatRiverSample Map(double lon,double lat) => w.Facts.RiverCurves.SampleMap(
            (float)((lon/360+.5)*width),(float)((lat/180+.5)*w.Height));
        var tidal=Sample(w,121.4437474,31.5187527);
        double measuredWidth=2*tidal.HalfWidth*40030200/width;
        Check(measuredWidth>20000 && measuredWidth<21500 && Map(121.4437474,31.5187527).Distance>0,
            $"Earth {width} keeps the {measuredWidth:F0} m tidal survey and draws the estuary from its shoreline");
        foreach(var (lon,lat) in new[]{(121.62168,31.60622),(121.31547,31.44582),(121.56134,31.60486)})
            Check(chart.TryWater(w,new FlatSeaPoint(lon,lat),out bool ocean) && !ocean && Map(lon,lat).Distance>0,
                $"Earth {width} surveyed island or mainland at {lon:F5}, {lat:F5} stays dry in both map fields");
        Check(Map(121.517661,31.397444).Distance<0 && Map(121.488157,31.239858).Distance<0,
            $"Earth {width} still draws the inland Huangpu at Wusongkou and the Bund");
        CheckCropFootprints(w);
    }

    static void CheckCropFootprints(WorldData w)
    {
        int Tile(double lon,double lat) => w.Index((int)((lon+180)*w.Width/360),(int)((lat+90)*w.Width/360));
        var f=w.Facts;
        if(w.Width==256)f.VegetationSeasons=new byte[(w.Width+2)*(w.Height+2)*12*4];
        FlatEcology.ApplyNileCropland(w,f,default);
        int tanta=Tile(30.99,30.78);
        if(w.Width==256)
        {
            // This 124 m mean-elevation tile contains both Tanta's fields and
            // adjoining desert. WorldCover puts 21.8% of its area in crops.
            Check(f.Vegetation[tanta]!=VegetationCover.Grassland && f.Wetness[tanta]>.14f && f.Wetness[tanta]<.18f,
                "mixed 256-wide delta and desert footprint retains its partial irrigated soil");
            for(int m=0;m<12;m++)
            {
                byte soil=f.VegetationSeasons[FlatEcology.SeasonOffset(w,tanta,m)+3];
                if(soil<36||soil>46)throw new Exception("256-wide mixed field lost its seasonal irrigation");
            }
        }
        else
        {
            var fields = w.Width==512 ? new[]{(30.99,30.78)} :
                new[]{(30.99,30.78),(30.8,30.9),(31.46,30.65)};
            foreach(var (lon,lat) in fields)
            {
                int i=Tile(lon,lat);
                if(f.Vegetation[i]!=VegetationCover.Grassland||f.Materials[i]!=GroundMaterial.Silt)
                    throw new Exception($"Earth {w.Width}: missing cultivated alluvium near {lon},{lat}");
            }
            Check(true,$"Earth {w.Width}: central and eastern farm soils in the measured delta footprint");
        }
        foreach(var (lon,lat) in new[]{(29.1,29.7),(33.2,29.45)})
            if(f.Wetness[Tile(lon,lat)]!=0)throw new Exception($"Earth {w.Width}: delta soil leaked into desert {lon},{lat}");
        f.Wetness[tanta]=0;
        int neighbour=tanta+1;
        w.Elevation[neighbour]++;
        FlatEcology.ApplyNileCropland(w,f,default);
        Check(f.Wetness[tanta]==0,$"Earth {w.Width}: terrain edits replace nearby surveyed crop ecology");
        w.Elevation[neighbour]--;
        w.Preset=TerrainPreset.Continents;
        FlatEcology.ApplyNileCropland(w,f,default);
        Check(f.Wetness[tanta]==0,$"Earth {w.Width}: procedural terrain keeps simulated ecology");
    }

    static void CheckMouth(WorldData world,FlatStraitNavigation chart,string name,double lon,double lat,
        double seaLon,double seaLat,double step)
    {
        var data=world.RiverReference;
        double Longitude(int i) => data.X(i,world.Width)*360.0/world.Width-180;
        double Latitude(int i) => data.Y(i,world.Height)*180.0/world.Height-90;
        double east=Math.Cos(lat*Math.PI/180);
        double Km(double dx,double dy) => 111.2*Math.Sqrt(dx*dx*east*east+dy*dy);
        var part=default(FlatEarthRiverReference.Part);
        double nearest=double.MaxValue;
        foreach (var candidate in data.Parts)
            foreach (int i in new[]{candidate.First,candidate.First+candidate.Count-1})
            {
                double distance=Km(Longitude(i)-lon,Latitude(i)-lat);
                if (distance>=nearest) continue;
                part=candidate;nearest=distance;
            }
        if(nearest>1.2) throw new Exception($"{name} surveyed outlet absent ({nearest:F2} km)");
        bool Sea(double x,double y) => chart.TryWater(world,new FlatSeaPoint(x,y),out bool wet) && wet;
        double west=Math.Min(lon,seaLon)-.25,south=Math.Min(lat,seaLat)-.25;
        int nx=(int)Math.Ceiling((Math.Abs(lon-seaLon)+.5)/step)+1;
        int ny=(int)Math.Ceiling((Math.Abs(lat-seaLat)+.5)/step)+1;
        int Index(double x,double y) => (int)Math.Round((y-south)/step)*nx+(int)Math.Round((x-west)/step);
        var contacts=new HashSet<int>();
        int sampled=0;
        for(int i=part.First+1;i<part.First+part.Count;i++)
        {
            double x0=Longitude(i-1),y0=Latitude(i-1),dx=Longitude(i)-x0,dy=Latitude(i)-y0;
            // Resolve a narrow marine handoff below the SDF texel spacing.
            double spacing=Km(x0-lon,y0-lat)<12||Km(x0+dx-lon,y0+dy-lat)<12 ? .06 : 1.5;
            int intervals=Math.Max(1,(int)Math.Ceiling(Km(dx,dy)/spacing));
            for(int j=0;j<=intervals;j++)
            {
                double t=(double)j/intervals,x=x0+t*dx,y=y0+t*dy;
                var hit=Sample(world,x,y);
                if(hit.Distance>=0)throw new Exception($"{name} indexed river is dry at {x:F5},{y:F5}");
                sampled++;
                if(Km(x-lon,y-lat)>5)continue;
                // Use a small physical strip even where the displayed river
                // is exaggerated. Both the river and the marine bank must cover it.
                double offsetKm=Math.Min(.2,.80*hit.HalfWidth*40075/world.Width);
                for(int ix=-1;ix<=1;ix++)for(int iy=-1;iy<=1;iy++)
                {
                    double px=x+ix*offsetKm/(111.2*east),py=y+iy*offsetKm/111.2;
                    if(Sea(px,py)&&Sample(world,px,py).Distance<0)contacts.Add(Index(px,py));
                }
            }
        }
        if(contacts.Count==0)throw new Exception($"Earth {world.Width} {name} ribbon misses its marine bank");
        // Flood only actual surveyed sea from the offshore point. A nearby
        // lake or a disconnected tidal channel cannot satisfy this condition.
        var seen=new bool[nx*ny];var pending=new Queue<int>();
        void Visit(int x,int y)
        {
            if(x<0||y<0||x>=nx||y>=ny)return;
            int cell=y*nx+x;
            if(seen[cell])return;
            seen[cell]=true;
            if(Sea(west+x*step,south+y*step))pending.Enqueue(cell);
        }
        int outlet=Index(seaLon,seaLat);
        Visit(outlet%nx,outlet/nx);
        while(pending.Count>0)
        {
            int i=pending.Dequeue();
            if(contacts.Contains(i))
            {
                Check(true,$"Earth {world.Width} {name}: {sampled} wet lower-course samples meet connected marine water");
                return;
            }
            int x=i%nx,y=i/nx;
            Visit(x-1,y);Visit(x+1,y);Visit(x,y-1);Visit(x,y+1);
        }
        throw new Exception($"Earth {world.Width} {name} lower course is isolated from its open sea");
    }
}
