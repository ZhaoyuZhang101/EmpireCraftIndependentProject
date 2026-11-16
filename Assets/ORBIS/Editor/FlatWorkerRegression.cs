using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Orbis.Flat;
namespace Orbis.Editor
{
    public static class FlatWorkerRegression
    {
        [Serializable] sealed class Report
        {
            public string status="STARTED",message,utc=DateTime.UtcNow.ToString("O");
            public int assertions,workers,tiles,dirtyCapturedTiles,maximumPaletteByteDifference;
            public double serialMilliseconds,fullSnapshotMilliseconds,fullObservedJobMilliseconds,dirtySnapshotMilliseconds;
            public string scope="Actual Job System output versus independent serial FlatPalette oracle; preparation only, not FPS or GPU upload benchmark. Burst compilation is asynchronous in Editor.";
        }
        static Report report;
        static void Check(bool pass,string message){report.assertions++;if(!pass)throw new InvalidOperationException("Render workers: "+message);}
        static void Same(Color32 a,Color32 b,string message)
        {Check(a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a,message+" / "+a+" vs "+b);}
        static void Same(Color a,Color b,string message)
        {Check(a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a,message);}
        static void SameDisplay(Color32 a,Color32 b,string message)
        {
            int error=Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)));
            report.maximumPaletteByteDifference=Math.Max(report.maximumPaletteByteDifference,error);
            Check(error<=1&&a.a==b.a,message+" exceeds one byte quantization unit");
        }
        static WorldData Fixture(int width)
        {
            var w=new WorldData(width,width/2,"workers",TerrainPreset.Continents);w.Facts=new FlatTerrainFacts(w.Count);
            w.Nations.Add(new Nation{Color=0xae73d1});var random=new System.Random(9202026);
            for(int i=0;i<w.Count;i++)
            {
                w.Elevation[i]=(short)random.Next(-4000,4500);w.Water[i]=(WaterKind)(i%6);w.Biomes[i]=(Biome)(i%12);
                w.Climates[i]=(Koppen)(i%31);w.Buildings[i]=(Structure)(i%5);w.Owner[i]=i%3==0?0:-1;
                w.Fertility[i]=(float)random.NextDouble();w.Facts.Materials[i]=(GroundMaterial)(i%6);w.Facts.Forms[i]=(Landform)(i%8);w.Facts.Vegetation[i]=(VegetationCover)(i%14);
                w.Facts.Depth[i]=(float)random.NextDouble()*2000;w.Facts.ShoreDistance[i]=(i%9)-4;w.Facts.HalfWidth[i]=(float)random.NextDouble()*.4f;
                w.Facts.Meander[i]=((i%17)-8)*.007f;
                w.Facts.FlowX[i]=(float)random.NextDouble()*2-1;w.Facts.FlowY[i]=(float)random.NextDouble()*2-1;
                w.Facts.Sediment[i]=(float)random.NextDouble();w.Facts.Freshness[i]=(float)random.NextDouble();w.Facts.Wetness[i]=(float)random.NextDouble();
                w.Facts.Hand[i]=(float)random.NextDouble()*1000;w.Facts.Canopy[i]=(float)random.NextDouble();w.Facts.Connections[i]=(byte)(i%256);
                for(int m=0;m<12;m++){w.Temperature[i*12+m]=(float)random.NextDouble()*100-50;w.Precipitation[i*12+m]=(float)random.NextDouble()*900;}
            }
            return w;
        }
        static bool Finish(FlatMapRenderJobs jobs,WorldData world,MapLayer layer)
        {
            var timeout=Stopwatch.StartNew();bool accepted;
            while(!jobs.TryComplete(world,layer,out accepted))
            {if(timeout.Elapsed.TotalSeconds>30)throw new TimeoutException("Render job did not complete.");Thread.Yield();}
            return accepted;
        }
        static FlatMapRenderJobs.Pixel Serial(WorldData w,int i,MapLayer layer)
        {
            var f=w.Facts;var terrain=FlatPalette.Terrain(w,i);
            return new FlatMapRenderJobs.Pixel{Terrain=terrain,Display=layer==MapLayer.Natural?terrain:FlatPalette.Display(w,i,layer),
                Owner=w.Owner[i]<0?new Color32(0,0,0,0):FlatPalette.RGB(w.Nations[w.Owner[i]].Color,(byte)(w.Owner[i]+1)),
                Info=new Color32((byte)HeightBands.Level(w.Elevation[i]),(byte)w.Water[i],(byte)w.Buildings[i],(byte)w.Climates[i]),
                Hydro=new Color(f.Depth[i],f.FlowX[i],f.FlowY[i],f.HalfWidth[i]),Mixing=new Color(f.Meander[i],f.Sediment[i],f.Freshness[i],f.Connections[i]),
                Geometry=new Color(w.Elevation[i],(float)f.Materials[i],f.Wetness[i],f.Hand[i]),Visual=new Color(w.Elevation[i],f.ShoreDistance[i],f.Depth[i],f.Canopy[i])};
        }
        static void Equivalence()
        {
            // 72x36 deliberately has partial chunks, a wrapped seam, and clipped polar halos.
            var w=Fixture(72);
            using(var jobs=new FlatMapRenderJobs(w))
            {
                foreach(MapLayer layer in Enum.GetValues(typeof(MapLayer)))
                {
                    w.MarkAllDirty();Check(jobs.Schedule(layer,true),"Schedule full "+layer);Check(Finish(jobs,w,layer),"Accept "+layer);
                    foreach(var c in jobs.Chunks)for(int y=0;y<c.Height;y++)for(int x=0;x<c.Width;x++)
                    {
                        int i=(c.Y+y)*w.Width+c.X+x;var actual=jobs.GetPixel(c.Offset+y*c.Width+x);var expected=Serial(w,i,layer);
                        Same(actual.Terrain,expected.Terrain,"Terrain "+layer+" "+i);SameDisplay(actual.Display,expected.Display,"Display "+layer+" "+i);
                        Same(actual.Owner,expected.Owner,"Owner");Same(actual.Info,expected.Info,"Info");Same(actual.Hydro,expected.Hydro,"Hydro");
                        Same(actual.Mixing,expected.Mixing,"Mixing");Same(actual.Geometry,expected.Geometry,"Geometry");Same(actual.Visual,expected.Visual,"Visual");
                    }
                }
                // Re-read the halo after a land-color change on the opposite side of the seam.
                w.Biomes[71]=Biome.Desert;w.Facts.Vegetation[71]=VegetationCover.Bare;w.Facts.Materials[71]=GroundMaterial.Soil;w.Water[71]=WaterKind.Land;
                w.Touch(0);jobs.Schedule(MapLayer.Natural,false);Check(Finish(jobs,w,MapLayer.Natural),"Dirty seam accepts");
                foreach(var c in jobs.Chunks)for(int y=0;y<c.Height;y++)for(int x=0;x<c.Width;x++)
                    Same(jobs.GetPixel(c.Offset+y*c.Width+x).Terrain,FlatPalette.Terrain(w,(c.Y+y)*w.Width+c.X+x),"Dirty halo equivalence");
                Check(jobs.CapturedTiles<w.Count,"Single chunk capture avoids whole-world copy");
            }
        }
        static void Invalidation()
        {
            var w=Fixture(72);
            using(var jobs=new FlatMapRenderJobs(w))
            {
                jobs.Schedule(MapLayer.Natural,true);w.Touch(33);Check(Finish(jobs,w,MapLayer.Natural),"Ongoing simulation cannot starve coherent publication");
                Check(w.DirtyChunks.Contains(1),"Later edited chunk stays queued");
                w.MarkAllDirty();
                jobs.Schedule(MapLayer.Natural,true);Check(!Finish(jobs,w,MapLayer.Rain),"Layer rejects old result");
                jobs.Schedule(MapLayer.Natural,true);w.TerrainRevision++;Check(!Finish(jobs,w,MapLayer.Natural),"Terrain revision rejects");
                jobs.Schedule(MapLayer.Natural,true);w.Day+=30;Check(!Finish(jobs,w,MapLayer.Natural),"Month rejects");
                jobs.Schedule(MapLayer.Natural,true);Check(!Finish(jobs,w.Clone(),MapLayer.Natural),"World identity rejects");
                jobs.Schedule(MapLayer.Natural,true);w.DirtyChunks.Add(1);Check(Finish(jobs,w,MapLayer.Natural),"Unversioned new dirty survives accepted batch");
                Check(w.DirtyChunks.Contains(1),"Completion never clears later dirty set");
                long previous=jobs.Version;jobs.Schedule(MapLayer.Natural,false);Check(jobs.Version==previous+1,"Batch version advances");
                short captured=w.Elevation[32];w.Elevation[32]=123;Check(Finish(jobs,w,MapLayer.Natural),"Snapshot contains no shared managed arrays");
                Check(jobs.GetPixel(0).Geometry.r==captured,"Source writes cannot alter copied snapshot");
            }
            w.MarkAllDirty();var pending=new FlatMapRenderJobs(w);pending.Schedule(MapLayer.Natural,true);pending.Dispose();pending.Dispose();
            Check(w.DirtyChunks.Count==6,"Dispose pending restores unpublished work");
            bool disposed=false;try{pending.Schedule(MapLayer.Natural,true);}catch(ObjectDisposedException){disposed=true;}Check(disposed,"Disposed buffers cannot be scheduled");
        }
        static void Benchmark()
        {
            var w=Fixture(512);report.tiles=w.Count;
            using(var jobs=new FlatMapRenderJobs(w))
            {
                jobs.Schedule(MapLayer.Temperature,true);Check(Finish(jobs,w,MapLayer.Temperature),"Warmup accepted");
                // A retained output array prevents dead-code elimination of the serial baseline.
                var baseline=new FlatMapRenderJobs.Pixel[w.Count];var timer=Stopwatch.StartNew();
                for(int i=0;i<w.Count;i++)baseline[i]=Serial(w,i,MapLayer.Temperature);
                report.serialMilliseconds=timer.Elapsed.TotalMilliseconds;
                w.MarkAllDirty();jobs.Schedule(MapLayer.Temperature,true);Check(Finish(jobs,w,MapLayer.Temperature),"Benchmark accepted");
                report.fullSnapshotMilliseconds=jobs.LastSnapshotMilliseconds;report.fullObservedJobMilliseconds=jobs.LastLatencyMilliseconds;
                w.Touch(w.Count/2);jobs.Schedule(MapLayer.Temperature,false);Check(Finish(jobs,w,MapLayer.Temperature),"Dirty benchmark accepted");
                report.dirtySnapshotMilliseconds=jobs.LastSnapshotMilliseconds;report.dirtyCapturedTiles=jobs.CapturedTiles;
                Check(jobs.ScheduledTiles==1024&&jobs.CapturedTiles<=1296,"Dirty work bounded to chunk and halo");
                GC.KeepAlive(baseline);
            }
        }
        [MenuItem("Tools/ORBIS/Validate render worker pipeline")]
        public static void Run()
        {
            report=new Report();
            try
            {
                report.workers=FlatWorkerPolicy.Resolve();Check(report.workers>=1&&report.workers<=8,"Bounded default workers");
                Check(FlatWorkerPolicy.Resolve(1000)==report.workers&&FlatWorkerPolicy.Resolve(1)==1,"Explicit worker counts also bounded");
                Equivalence();Invalidation();Benchmark();report.status="PASS";UnityEngine.Debug.Log("Render worker regression PASS: "+report.assertions+" assertions.");
            }
            catch(Exception e){report.status="FAIL";report.message=e.ToString();throw;}
            finally
            {
                string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Validation/Perf3_5"));Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"render-workers.json"),JsonUtility.ToJson(report,true));
            }
        }
    }
}
