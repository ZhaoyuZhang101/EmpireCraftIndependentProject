using System;
using System.IO;
using System.Linq;
using Orbis.Flat;

public static class RiverMapRegression
{
    const string Data = "Assets/ORBIS/Resources/ORBIS/FlatWorld/";
    // Independent centreline vertices from Natural Earth 1:10m v5.1.2, longitude first.
    static readonly (string name, double lon, double lat)[] Sites = {
        ("Amazonas", -64.596099, -3.387709), ("Nile", 31.157319, 27.218573),
        ("Yangtze", 107.519786, 29.887315), ("Lena", 106.699718, 57.045111),
        ("Yukon", -159.773834, 63.051540)
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
        Check(saved.SequenceEqual(FlatSave.Encode(loaded)) && Sample(loaded,Sites[0].lon,Sites[0].lat).Distance<0,
            "Earth reload restores the survey with identical serialized terrain, water and climate");

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
}
