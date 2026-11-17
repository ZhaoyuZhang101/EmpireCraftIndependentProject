using System;
using System.Threading;

namespace Orbis.Flat
{
    public static class FlatRiverCurveSelfTests
    {
        public static int Assertions { get; private set; }

        static void Check(bool condition,string message)
        {
            Assertions++;
            if(!condition)throw new InvalidOperationException("Continuous river regression: "+message);
        }

        static WorldData Reach(bool confined=false,bool varyingFlow=false)
        {
            var world=new WorldData(64,32,"CONTINUOUS-RIVER-FIELD",TerrainPreset.Continents);
            for(int i=0;i<world.Count;i++)
            {
                world.Elevation[i]=100;world.WaterLevel[i]=100;
                world.DrainOrder[i]=world.Count-1-i;
            }
            for(int x=5;x<=45;x++)
            {
                int tile=world.Index(x,16);
                world.Water[tile]=WaterKind.River;
                world.Runoff[tile]=varyingFlow?6+(x-5)*(x-5):64;
                world.Downstream[tile]=world.Index(x+1,16);
                if(confined)
                {
                    world.Elevation[world.Index(x,15)]=1600;
                    world.Elevation[world.Index(x,17)]=1600;
                }
            }
            int mouth=world.Index(46,16);
            world.Water[mouth]=WaterKind.Lake;world.Elevation[mouth]=90;
            FlatTerrainAnalysis.Build(world,CancellationToken.None,false,1);
            return world;
        }

        static float Centre(WorldData world,float x,out float radius)
        {
            float best=8,centre=16.5f;
            int ix=(int)Math.Floor(x);
            for(int k=0;k<=600;k++)
            {
                float y=13.5f+k*.01f;int iy=(int)Math.Floor(y);
                float distance=FlatWaterGeometry.RiverDistance(world,world.Index(ix,iy),x-ix,y-iy);
                if(distance<best){best=distance;centre=y;}
            }
            radius=-best;return centre;
        }

        static void TestValleyFollowing()
        {
            var valley=Reach();
            for(int x=5;x<=46;x++)
            {
                valley.Elevation[valley.Index(x,15)]=140;
                valley.Elevation[valley.Index(x,17)]=60;
            }
            FlatTerrainAnalysis.Build(valley,CancellationToken.None,false,1);
            float average=0;
            for(int k=0;k<=120;k++)average+=Centre(valley,9+k*.25f,out float radius)-16.5f;
            average/=121;
            Check(average>.20f,"Reconstructed channel seeks the lower terrain corridor, mean displacement="+average);
            for(int x=17;x<=27;x++)valley.Elevation[valley.Index(x,17)]=450;
            FlatTerrainAnalysis.Build(valley,CancellationToken.None,false,1);
            for(int k=0;k<=100;k++)
            {
                float x=17.5f+k*.1f,y=Centre(valley,x,out float radius);
                Check(y<16.64f,"Sampled curve avoids the raised valley wall between drainage nodes");
                Check(y+radius<16.65f,"The full ribbon retains the bank clearance permitted by local water head and relief");
                if(k>2&&k<98)
                {
                    var curves=valley.Facts.RiverCurves;float bankY=y+radius+.03f;
                    var bank=curves.Sample(x,bankY);
                    float gx=(curves.Sample(x+.001f,bankY).Distance-curves.Sample(x-.001f,bankY).Distance)/.002f;
                    float gy=(curves.Sample(x,bankY+.001f).Distance-curves.Sample(x,bankY-.001f).Distance)/.002f;
                    Check(Math.Abs(gx*bank.FlowX+gy*bank.FlowY)<.15f,"Flow follows the terrain-constrained ribbon tangent");
                }
            }
        }

        static void SameSample(FlatRiverSample a,FlatRiverSample b,float tolerance,string message)
        {
            Check(Math.Abs(a.Distance-b.Distance)<=tolerance&&Math.Abs(a.HalfWidth-b.HalfWidth)<=tolerance&&
                Math.Abs(a.Depth-b.Depth)<=tolerance&&Math.Abs(a.FlowX-b.FlowX)<=tolerance&&
                Math.Abs(a.FlowY-b.FlowY)<=tolerance&&Math.Abs(a.Sediment-b.Sediment)<=tolerance,message);
        }

        static void TestCacheAndAttributes(WorldData world)
        {
            var facts=world.Facts;var curves=facts.RiverCurves;
            Check(curves!=null&&curves.SegmentCount>=41*16,"Complete graph reaches are cached as sampled global curves");
            Check(curves.Width==world.Width&&curves.Height==world.Height,"Curve cache records its immutable domain");
            var active=new bool[world.Count];int previousTile=-1;
            for(int k=0;k<curves.ActiveTileCount;k++)
            {
                int tile=curves.GetActiveTile(k);
                Check(tile>previousTile&&tile<world.Count,"Active raster receivers are sorted and unique");
                active[tile]=true;previousTile=tile;
            }
            float minRadius=1,maxRadius=0,previousRadius=0;
            bool displaced=false;
            for(int k=0;k<=300;k++)
            {
                float x=8+k*.1f,y=Centre(world,x,out float radius);
                var sample=curves.Sample(x,y);
                Check(sample.Distance<-.025f&&sample.Depth>.1f,"Cached centre samples retain water and depth");
                Check(sample.HalfWidth>=.006f&&sample.HalfWidth<=.22001f,"Continuous hydraulic width stays bounded");
                Check(Math.Abs(sample.FlowX*sample.FlowX+sample.FlowY*sample.FlowY-1)<.00001f,"River flow follows a unit curve tangent");
                if(k>0)Check(Math.Abs(sample.HalfWidth-previousRadius)<.018f,"Width has no per-tile steps");
                previousRadius=sample.HalfWidth;
                minRadius=Math.Min(minRadius,sample.HalfWidth);maxRadius=Math.Max(maxRadius,sample.HalfWidth);
                for(int side=-1;side<=1;side+=2)
                {
                    float bankY=y+side*(radius+.28f);
                    int bank=world.Index((int)Math.Floor(x),(int)Math.Floor(bankY));
                    Check(active[bank],"Curve candidate index includes the bank and .30-tile filtering margin");
                    Check(curves.Sample(x,bankY).Distance<.31f,"Margin samples read the same finite ribbon");
                }
                int tile=world.Index((int)Math.Floor(x),(int)Math.Floor(y));
                if(facts.HalfWidth[tile]==0)
                {
                    displaced=true;
                    Check(FlatWaterGeometry.Coverage(world,x,y,out float depth)>.99f&&Math.Abs(depth-sample.Depth)<.00001f,
                        "CPU coverage and root exclusion follow displaced water into adjacent land receivers");
                }
            }
            Check(displaced,"Lowland ribbon leaves the old per-tile river envelope");
            Check(maxRadius>minRadius*1.25f,"Pool and riffle widths vary continuously at constant discharge");
            for(int x=6;x<=45;x++)
            {
                int tile=world.Index(x,16);facts.Sediment[tile]=.08f+x*.006f;facts.Depth[tile]=1+x*.04f;
            }
            facts.RiverCurves=FlatRiverCurves.Build(world,facts,CancellationToken.None);curves=facts.RiverCurves;
            for(int x=8;x<43;x++)
            {
                float node=x+.5f,y=Centre(world,node,out float radius);
                var a=curves.Sample(node-.0001f,y);var b=curves.Sample(node+.0001f,y);
                SameSample(a,b,.001f,"Main-stem width, depth, sediment and tangent remain continuous at shared graph nodes");
            }
            for(int k=0;k<=40;k++)
            {
                float x=44.5f+k*.05f,y=Centre(world,x,out float radius);
                Check(FlatWaterGeometry.Coverage(world,x,y,out float depth)>.95f,"Global mouth ribbon reaches the receiving lake without a face gap");
            }
            var clone=world.Clone();
            Check(ReferenceEquals(clone.Facts.RiverCurves,curves),"World clones share only the immutable curve cache");
            var before=curves.Sample(23.7f,Centre(world,23.7f,out float ignored));
            Array.Clear(world.Elevation,0,world.Elevation.Length);Array.Clear(facts.HalfWidth,0,facts.HalfWidth.Length);
            Array.Clear(facts.Sediment,0,facts.Sediment.Length);Array.Clear(world.Downstream,0,world.Downstream.Length);
            SameSample(before,curves.Sample(23.7f,Centre(clone,23.7f,out ignored)),0,"Published curves retain no mutable world or fact references");
            var first=FlatRiverCurves.Build(clone,clone.Facts,CancellationToken.None);
            var second=FlatRiverCurves.Build(clone,clone.Facts,CancellationToken.None);
            Check(first.SegmentCount==second.SegmentCount&&first.ActiveTileCount==second.ActiveTileCount,"Repeated builds produce the same sparse topology");
            for(int k=0;k<first.ActiveTileCount;k++)Check(first.GetActiveTile(k)==second.GetActiveTile(k),"Repeated builds preserve receiver order");
            for(int k=0;k<200;k++)SameSample(first.Sample(k*.31f,14+k*.023f),second.Sample(k*.31f,14+k*.023f),0,"Cache construction is bit-repeatable");
            FlatTerrainAnalysis.Build(clone,CancellationToken.None,false,1);var serial=clone.Facts.RiverCurves;
            FlatTerrainAnalysis.Build(clone,CancellationToken.None,false,8);var parallel=clone.Facts.RiverCurves;
            for(int k=0;k<200;k++)SameSample(serial.Sample(k*.31f,14+k*.023f),parallel.Sample(k*.31f,14+k*.023f),0,"Curve reconstruction is independent of fact worker count");
            var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;
            try{FlatRiverCurves.Build(clone,clone.Facts,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"Curve rebuilding observes cancellation before publishing geometry");cancel.Dispose();
        }

        static void TestWrappedAndPolarReaches()
        {
            var world=new WorldData(64,32,"RIVER-DATELINE",TerrainPreset.Continents);
            for(int i=0;i<world.Count;i++){world.Elevation[i]=100;world.WaterLevel[i]=100;world.DrainOrder[i]=world.Count-1-i;}
            for(int k=0;k<8;k++)
            {
                int tile=world.Index(60+k,16);world.Water[tile]=WaterKind.River;
                world.Runoff[tile]=60+k*2;world.Downstream[tile]=world.Index(61+k,16);
            }
            int lake=world.Index(68,16);world.Water[lake]=WaterKind.Lake;world.Elevation[lake]=90;
            FlatTerrainAnalysis.Build(world,CancellationToken.None,false);
            var curves=world.Facts.RiverCurves;
            for(int k=0;k<=160;k++)
            {
                float y=14.5f+k*.025f;
                SameSample(curves.Sample(-.00001f,y),curves.Sample(63.99999f,y),.00002f,"Date-line geometry and attributes are periodic");
                SameSample(curves.Sample(0,y),curves.Sample(64,y),0,"Exact date-line samples are identical");
                Check(Math.Abs(FlatWaterGeometry.RiverDistance(world,world.Index(63,16),1,y-16)-
                    FlatWaterGeometry.RiverDistance(world,world.Index(0,16),0,y-16))<.000001f,"Shared date-line face uses one global field");
            }
            Check(curves.Sample(32,16.5f).Distance>1,"Date-line reach avoids a line across the map");
            for(int pole=0;pole<2;pole++)
            {
                var polar=new WorldData(64,32,"RIVER-POLE",TerrainPreset.Continents);int row=pole==0?0:31;
                for(int i=0;i<polar.Count;i++){polar.Elevation[i]=100;polar.WaterLevel[i]=100;polar.DrainOrder[i]=i;}
                int a=polar.Index(2,row),b=polar.Index(34,row);
                polar.Water[a]=polar.Water[b]=WaterKind.River;polar.Runoff[a]=40;polar.Runoff[b]=60;polar.Downstream[a]=b;
                FlatTerrainAnalysis.Build(polar,CancellationToken.None,false);
                var polarCurves=polar.Facts.RiverCurves;
                Check(polarCurves.SegmentCount==2,"Polar adjacency becomes two short boundary ribbons");
                Check(polarCurves.Sample(18.5f,pole==0?0:32).Distance>1,"Polar river never spans the opposite meridians across the map");
                float best=8;
                for(int k=0;k<=60;k++)best=Math.Min(best,polarCurves.Sample(1+k*.05f,pole==0?0:32).Distance);
                Check(best<0,"Polar channel reaches the bounded latitude edge");
                for(int k=0;k<100;k++)
                {
                    var sample=polarCurves.Sample(k*1.37f-32,k*.47f-8);
                    Check(FMath.Finite(sample.Distance)&&FMath.Finite(sample.HalfWidth)&&FMath.Finite(sample.Depth)&&
                        FMath.Finite(sample.FlowX)&&FMath.Finite(sample.FlowY)&&FMath.Finite(sample.Sediment),"Polar and off-domain queries stay finite");
                }
            }
            var springWorld=new WorldData(64,32,"ISOLATED-SPRING",TerrainPreset.Continents);
            int spring=springWorld.Index(10,10);springWorld.Water[spring]=WaterKind.River;springWorld.Runoff[spring]=20;
            FlatTerrainAnalysis.Build(springWorld,CancellationToken.None,false);
            Check(springWorld.Facts.RiverCurves.SegmentCount==1&&springWorld.Facts.RiverCurves.Sample(10.5f,10.5f).Distance<0,"Isolated legacy headwaters retain a finite spring");
        }

        static void TestTributary()
        {
            var world=Reach();
            for(int y=11;y<16;y++)
            {
                int tile=world.Index(24,y);world.Water[tile]=WaterKind.River;
                world.Runoff[tile]=4+(y-11);world.Downstream[tile]=world.Index(24,y+1);
            }
            FlatTerrainAnalysis.Build(world,CancellationToken.None,false);
            var curves=world.Facts.RiverCurves;
            float mainY=Centre(world,20.5f,out float ignored);
            float mainWidth=curves.Sample(20.5f,mainY).HalfWidth;
            float best=8,branchWidth=0;
            for(int k=0;k<=300;k++)
            {
                var sample=curves.Sample(23+k*.01f,14.5f);
                if(sample.Distance<best){best=sample.Distance;branchWidth=sample.HalfWidth;}
            }
            Check(best<0&&branchWidth<mainWidth*.75f,"A small tributary retains its own hydraulic width while approaching the main stem");
            float joinY=Centre(world,24.5f,out ignored);
            var join=curves.Sample(24.5f,joinY);
            Check(join.Distance<0&&join.HalfWidth>branchWidth*1.2f,"Main-stem width carries through a smaller tributary junction");
            var downstream=(int[])world.Downstream.Clone();var runoff=(float[])world.Runoff.Clone();
            FlatRiverCurves.Build(world,world.Facts,CancellationToken.None);
            for(int i=0;i<world.Count;i++)Check(downstream[i]==world.Downstream[i]&&runoff[i]==world.Runoff[i],"Presentation reconstruction preserves every authoritative drainage edge and discharge");
        }

        static FlatRiverSample CrossSection(FlatRiverCurves curves,float x,float y,float nx,float ny)
        {
            FlatRiverSample best=curves.Sample(x,y);
            for(int k=-200;k<=200;k++)
            {
                float offset=k*.005f;
                var sample=curves.Sample(x+nx*offset,y+ny*offset);
                if(sample.Distance<best.Distance)best=sample;
            }
            return best;
        }

        static void TestLakeOutlet()
        {
            var world=Reach();int lake=world.Index(4,16),first=world.Index(5,16);
            world.Water[lake]=WaterKind.Lake;world.Elevation[lake]=90;world.Runoff[lake]=64;
            world.Downstream[lake]=first;
            for(int i=0;i<world.Count;i++)world.Climates[i]=Koppen.Cfb;
            FlatTerrainAnalysis.Build(world,CancellationToken.None,false);
            var curves=world.Facts.RiverCurves;
            Check(curves.Sample(4.5f,16.5f).Distance<0,"A lake-fed reach begins inside the source lake");
            for(int k=0;k<=120;k++)
            {
                float x=4.5f+k*.025f;
                var sample=CrossSection(curves,x,16.5f,0,1);
                Check(sample.Distance<-.02f,"Lake outlet joins its downstream river without a missing reach");
            }
            var source=CrossSection(curves,5.5f,16.5f,0,1);
            Check(source.HalfWidth>world.Facts.HalfWidth[first]*.65f,"Lake discharge retains channel width instead of a spring taper");
            Check(Math.Abs(curves.Sample(4.5f,16.5f).Depth-world.Facts.Depth[lake])<.0001f,"Source lake depth is preserved at the outlet anchor");
            float y=Centre(world,5.5f,out float radius);
            SameSample(curves.Sample(5.4999f,y),curves.Sample(5.5001f,y),.001f,"Lake-fed width, depth and sediment join the first river node continuously");
            byte[] before=FlatSave.Encode(world);
            FlatRiverCurves.Build(world,world.Facts,CancellationToken.None);
            byte[] after=FlatSave.Encode(world);
            Check(before.Length==after.Length,"Outlet geometry preserves save payload length");
            for(int k=0;k<before.Length;k++)Check(before[k]==after[k],"Outlet reconstruction preserves serialized world authority byte-for-byte");

            var wrapped=new WorldData(64,32,"LAKE-OUTLET-DATELINE",TerrainPreset.Continents);
            for(int i=0;i<wrapped.Count;i++){wrapped.Elevation[i]=100;wrapped.WaterLevel[i]=100;wrapped.DrainOrder[i]=wrapped.Count-1-i;}
            int origin=wrapped.Index(63,16),stream=wrapped.Index(0,16),receiver=wrapped.Index(1,16);
            wrapped.Water[origin]=wrapped.Water[receiver]=WaterKind.Lake;wrapped.Elevation[origin]=wrapped.Elevation[receiver]=90;
            wrapped.Water[stream]=WaterKind.River;wrapped.Runoff[origin]=wrapped.Runoff[stream]=64;
            wrapped.Downstream[origin]=stream;wrapped.Downstream[stream]=receiver;
            FlatTerrainAnalysis.Build(wrapped,CancellationToken.None,false);
            var seam=wrapped.Facts.RiverCurves;
            Check(seam.Sample(63.5f,16.5f).Distance<0,"Wrapped lake outlet begins inside its source lake");
            for(int k=0;k<=80;k++)
            {
                float x=63.5f+k*.025f;
                Check(CrossSection(seam,x,16.5f,0,1).Distance<-.02f,"Lake outlet crosses the date line as one connected reach");
            }
            for(int k=0;k<100;k++)SameSample(seam.Sample(-.001f,15.5f+k*.02f),seam.Sample(63.999f,15.5f+k*.02f),.00001f,"Wrapped outlet attributes agree across longitude");
            Check(seam.Sample(32,16.5f).Distance>1,"Wrapped lake outlet stays local to the date line");
            for(int pole=0;pole<2;pole++)
            {
                var polar=new WorldData(64,32,"LAKE-OUTLET-POLE",TerrainPreset.Continents);int row=pole==0?0:31;
                for(int i=0;i<polar.Count;i++){polar.Elevation[i]=100;polar.WaterLevel[i]=100;polar.DrainOrder[i]=i;}
                int a=polar.Index(2,row),b=polar.Index(34,row),c=polar.Index(35,row);
                polar.Water[a]=polar.Water[c]=WaterKind.Lake;polar.Elevation[a]=polar.Elevation[c]=90;
                polar.Water[b]=WaterKind.River;polar.Runoff[a]=polar.Runoff[b]=64;polar.Downstream[a]=b;polar.Downstream[b]=c;
                FlatTerrainAnalysis.Build(polar,CancellationToken.None,false);
                var polarCurves=polar.Facts.RiverCurves;
                Check(polarCurves.Sample(2.5f,row+.5f).Distance<0,"Polar outlet starts at the source lake centre");
                Check(polarCurves.Sample(2.5f,pole==0?0:32).Distance<0,"Polar source reaches its latitude boundary");
                Check(CrossSection(polarCurves,34.5f,pole==0?0:32,1,0).Distance<0,"Polar outlet resumes at the opposite-meridian boundary");
                Check(polarCurves.Sample(18.5f,pole==0?0:32).Distance>1,"Polar source avoids a giant line between meridians");
            }
        }

        public static int Run()
        {
            Assertions=0;
            var open=Reach();float min=100,max=-100,previous=0;
            for(int k=0;k<=300;k++)
            {
                float x=8+k*.1f,centre=Centre(open,x,out float radius);
                Check(radius>.025f,"Long reach has uninterrupted water coverage");
                min=Math.Min(min,centre);max=Math.Max(max,centre);
                if(k>0)Check(Math.Abs(centre-previous)<.16f,"Whole-reach bend stays continuous through tile faces");
                previous=centre;
            }
            Check(max-min>.85f,"Lowland bends migrate visibly across several channel widths, span="+(max-min));
            var confined=Reach(true);float confinedMin=100,confinedMax=-100;
            for(int k=0;k<=100;k++)
            {
                float centre=Centre(confined,8+k*.3f,out float radius);
                confinedMin=Math.Min(confinedMin,centre);confinedMax=Math.Max(confinedMax,centre);
                Check(Math.Abs(centre-16.5f)+radius<.4f,"Confined reach retains terrain banks");
                Check(Math.Abs(centre-16.5f)+radius<.085f,"Gorge width fits the 120-metre water-head corridor below adjacent 1500-metre walls");
            }
            Check(confinedMax-confinedMin<(max-min)*.35f,"Valley walls suppress lowland-scale migration");
            var variable=Reach(false,true);float smallest=1,largest=0;
            for(int k=0;k<=300;k++)
            {
                Centre(variable,8+k*.1f,out float radius);
                smallest=Math.Min(smallest,radius);largest=Math.Max(largest,radius);
            }
            Check(largest>smallest*1.6f,"Discharge widens the continuous ribbon meaningfully");
            TestValleyFollowing();
            TestCacheAndAttributes(open);TestWrappedAndPolarReaches();TestTributary();TestLakeOutlet();
            return Assertions;
        }
    }
}
