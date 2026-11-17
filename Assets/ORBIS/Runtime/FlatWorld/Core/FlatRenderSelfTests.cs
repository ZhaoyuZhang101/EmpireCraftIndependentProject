using System;
using System.Threading;
namespace Orbis.Flat
{
    public static class FlatRenderSelfTests
    {
        public static int Assertions {get;private set;}
        static void Check(bool value,string message)
        {Assertions++;if(!value)throw new InvalidOperationException("Perf3.2 surface regression: "+message);}
        static void Near(float a,float b,float tolerance,string message)
        {Check(Math.Abs(a-b)<=tolerance,message+" / "+a+" vs "+b);}
        static void TestLightning()
        {
            Near(FlatLightningVisuals.Envelope(-1),0,0,"Lightning cannot precede its event");
            Near(FlatLightningVisuals.Envelope(.035f),1,.00001f,"Lightning has a brief primary pulse");
            Near(FlatLightningVisuals.Envelope(.085f),0,.00001f,"Lightning has a dark interval between pulses");
            Near(FlatLightningVisuals.Envelope(.14f),.55f,.00001f,"Lightning has a weaker secondary pulse");
            Near(FlatLightningVisuals.Envelope(.3f),0,0,"Lightning expires instead of holding the map bright");
            var world=new WorldData(64,32,"lightning-regression",TerrainPreset.Continents);
            for(int i=0;i<world.Count*12;i++){world.Precipitation[i]=240;world.Temperature[i]=24;}
            var first=new FlatLightningVisuals(world);var second=new FlatLightningVisuals(world);bool observed=false;
            double day=world.Day;
            for(int frame=0;frame<900;frame++)
            {
                first.Tick(.016f,true,63.8f,31.9f,true);second.Tick(.016f,true,63.8f,31.9f,true);
                for(int i=0;i<4;i++)
                {
                    var a=first.Flashes[i];var b=second.Flashes[i];
                    Check(a.X==b.X&&a.Y==b.Y&&a.Z==b.Z&&a.Strength==b.Strength,"Visual lightning is repeatable for the same seed/ticks");
                    Check(!float.IsNaN(a.Strength)&&a.Strength>=0&&a.Strength<=1,"Lightning strength remains finite and bounded");
                    if(a.Strength>0){observed=true;Near(a.X*a.X+a.Y*a.Y+a.Z*a.Z,1,.00001f,"Polar lightning has a valid spherical direction");}
                }
            }
            Check(observed&&first.Flashes.Length==4,"Wet warm climate produces bounded visual events");
            first.Tick(.016f,false,0,0,false);
            foreach(var flash in first.Flashes)Near(flash.Strength,0,0,"Pause/cloud disable clears active flashes");
            Array.Clear(world.Precipitation,0,world.Precipitation.Length);var dry=new FlatLightningVisuals(world);
            for(int frame=0;frame<500;frame++)
            {
                dry.Tick(.05f,true,0,0,false);
                foreach(var flash in dry.Flashes)Near(flash.Strength,0,0,"Dry climate produces no visual lightning");
            }
            Check(world.Day==day,"Visual lightning never advances the world calendar");
            for(int i=0;i<world.Precipitation.Length;i++)world.Precipitation[i]=240;
            var slow=new FlatLightningVisuals(world);float slowPeak=0;
            for(int frame=0;frame<300;frame++)
            {
                slow.Tick(.1f,true,32,16,true);
                foreach(var flash in slow.Flashes)slowPeak=Math.Max(slowPeak,flash.Strength);
            }
            Check(slowPeak>.8f,"Coarse render steps retain the brief primary flash");
        }
        public static int Run()
        {
            Assertions=0;TestLightning();TestVegetationSeasons();Assertions+=FlatRiverCurveSelfTests.Run();var random=new Random(320912);
            for(int k=0;k<2000;k++)
            {
                float a=random.Next(-4000,4001),b=random.Next(-4000,4001),c=random.Next(-4000,4001),d=random.Next(-4000,4001);
                Near(FlatSurfaceSampling.Cubic(a,b,c,d,0),b,.001f,"Cubic left centre exact");
                Near(FlatSurfaceSampling.Cubic(a,b,c,d,1),c,.001f,"Cubic right centre exact");
                for(int j=0;j<=32;j++)
                {float v=FlatSurfaceSampling.Cubic(a,b,c,d,j/32f);Check(v>=Math.Min(b,c)-.01f&&v<=Math.Max(b,c)+.01f,"No height/depth overshoot");}
            }
            var field=new float[64*32];for(int i=0;i<field.Length;i++)field[i]=(float)random.NextDouble()*3000;
            for(int y=0;y<32;y++)
            {
                Near(FlatSurfaceSampling.Sample(field,64,32,0,y+.5f),FlatSurfaceSampling.Sample(field,64,32,64,y+.5f),.002f,"Longitude field seam");
                Near(FlatSurfaceSampling.Sample(field,64,32,7.5f,y+.5f),field[y*64+7],.002f,"Field centre exact");
            }
            Near(FlatSurfaceSampling.Sample(field,64,32,4.5f,-2),field[4],.002f,"Latitude clamps, never repeats");
            for(int variant=0;variant<6;variant++)
            {
                const int width=19,height=11;var seeds=new bool[width*height];
                for(int i=0;i<seeds.Length;i++)seeds[i]=variant==1||(variant>1&&random.NextDouble()<.1);
                if(variant==2){Array.Clear(seeds,0,seeds.Length);seeds[0]=true;}
                var distance=FlatSurfaceSampling.DistanceSquared(seeds,width,height,CancellationToken.None);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    float exact=1e12f;
                    for(int j=0;j<seeds.Length;j++)if(seeds[j])
                    {int dx=Math.Abs(x-j%width);dx=Math.Min(dx,width-dx);int dy=y-j/width;exact=Math.Min(exact,dx*dx+dy*dy);}
                    Near(distance[y*width+x],exact,.01f,"Exact periodic Euclidean distance");
                }
            }
            // All-land and all-water worlds remain finite and retain every tile centre.
            var w=new WorldData(64,32,"PERF32-FIXTURE",TerrainPreset.Continents);w.Facts=new FlatTerrainFacts(w.Count);
            for(int variant=0;variant<2;variant++)
            {
                for(int i=0;i<w.Count;i++)w.Water[i]=variant==0?WaterKind.Land:WaterKind.ShallowOcean;
                FlatSurfaceSampling.BuildShore(w,w.Facts,CancellationToken.None);
                for(int i=0;i<w.Count;i++)Near(w.Facts.ShoreDistance[i],variant==0?-8:8,.001f,"Full domain shore sentinel");
            }
            var one=new bool[w.Count];one[w.Index(10,10)]=true;
            var raw=new float[w.Count];for(int i=0;i<w.Count;i++)raw[i]=one[i]?.5f:-8;
            var constrained=FlatSurfaceSampling.SmoothShore(raw,one,w.Width,w.Height);
            for(int i=0;i<w.Count;i++)Check((constrained[i]>0)==one[i],"Shore smoothing preserves every tile-centre classification");
            Check(raw[w.Index(10,10)]==.5f,"Smoothing never mutates original field");
            float corner=FlatWaterGeometry.BezierDistance(.375f,.625f,0,.5f,.5f,1,.5f,.5f,.07f,.07f,.07f);
            Check(corner<-.06f,"90 degree bend is a quadratic arc");
            Check(FlatWaterGeometry.BezierDistance(.5f,.5f,0,.5f,.5f,1,.5f,.5f,.07f,.07f,.07f)>0,"No old square centre bulge on a narrow bend");
            for(int a=0;a<4;a++)for(int b=0;b<4;b++)if(a!=b)
            {
                float ax=a==0?0:a==1?1:.5f,ay=a==2?0:a==3?1:.5f;
                float bx=b==0?0:b==1?1:.5f,by=b==2?0:b==3?1:.5f;
                Check(FlatWaterGeometry.BezierDistance(ax,ay,ax,ay,bx,by,.5f,.5f,.09f,.09f,.09f)<0,"Connected inlet endpoint");
                Check(FlatWaterGeometry.BezierDistance(bx,by,ax,ay,bx,by,.5f,.5f,.09f,.09f,.09f)<0,"Connected outlet endpoint");
            }
            int left=w.Index(20,12),right=w.Index(21,12);
            // Discharge, not the stream-extraction threshold, determines channel width.
            float[] flows={.5f,2,8,32,128,1000000};
            for(int j=0;j<flows.Length;j++){w.Water[j]=WaterKind.River;w.Runoff[j]=flows[j];}
            FlatTerrainAnalysis.Build(w,CancellationToken.None,false);
            for(int j=1;j<flows.Length;j++)Check(w.Facts.HalfWidth[j]>w.Facts.HalfWidth[j-1],"Higher discharge widens the river even below the old extraction threshold");
            for(int j=1;j<flows.Length;j++)Check(FlatTerrainAnalysis.RiverDepth(flows[j],20)>FlatTerrainAnalysis.RiverDepth(flows[j-1],20),"Larger discharge deepens the channel");
            Check(Math.Abs(FlatTerrainAnalysis.RiverMeander(.1f,5,1))>Math.Abs(FlatTerrainAnalysis.RiverMeander(.1f,500,1))*3,"Confined steep rivers meander less than lowland rivers");
            for(int j=0;j<100;j++)
            {
                float x=j*.17f,y=10+j*.05f;
                Near(FlatSurfaceSampling.ShoreDetail(x,y,64,32),FlatSurfaceSampling.ShoreDetail(x+64,y,64,32),.0001f,"Shore detail repeats continuously around longitude");
                Near(FlatSurfaceSampling.ShoreDetail(j%64+.5f,12.5f,64,32),0,.0001f,"Shore detail preserves authoritative tile centres");
            }
            Check(w.Facts.HalfWidth[flows.Length-1]<=.24f,"Mouth full width stays below half a tile");
            w.Facts.RiverCurves=null;
            int spring=w.Index(30,16);w.Facts.HalfWidth[spring]=.12f;w.Facts.HalfWidth[w.Neighbor(spring,1)]=.12f;
            w.Facts.Connections[spring]=2;w.Facts.FlowX[spring]=1;int sourceSpan=0,downstreamSpan=0;
            for(int y=0;y<100;y++)
            {
                if(FlatWaterGeometry.RiverDistance(w,spring,.5f,y/100f)<0)sourceSpan++;
                if(FlatWaterGeometry.RiverDistance(w,spring,.90f,y/100f)<0)downstreamSpan++;
            }
            Check(sourceSpan>0&&downstreamSpan>sourceSpan*2,"Headwater emerges narrow and widens into its first reach");
            var shoreWorld=new WorldData(64,32,"physical-shore",TerrainPreset.Continents);
            for(int i=0;i<shoreWorld.Count;i++){bool water=i%64<32;shoreWorld.Water[i]=water?WaterKind.Lake:WaterKind.Land;shoreWorld.Elevation[i]=(short)(water?-20:180);shoreWorld.WaterLevel[i]=(short)(water?0:180);}
            shoreWorld.Facts=new FlatTerrainFacts(shoreWorld.Count);FlatSurfaceSampling.BuildShore(shoreWorld,shoreWorld.Facts,CancellationToken.None);
            Check(shoreWorld.Facts.ShoreDistance[shoreWorld.Index(31,16)]<.2f,"Shallow water beside a steep bank intersects near the wet centre");
            Check(FlatSurfaceSampling.SampleShore(shoreWorld,32,16.5f)<0,"Terrain-constrained shore is not forced to the tile midpoint");
            var sequential=w.Clone();var parallel=w.Clone();
            FlatTerrainAnalysis.Build(sequential,CancellationToken.None,true,1);
            FlatTerrainAnalysis.Build(parallel,CancellationToken.None,true,8);
            for(int j=0;j<w.Count;j++)
            {
                Check(sequential.Facts.Relief[j]==parallel.Facts.Relief[j]&&sequential.Facts.Wetness[j]==parallel.Facts.Wetness[j]&&sequential.Facts.HalfWidth[j]==parallel.Facts.HalfWidth[j]&&sequential.Facts.Meander[j]==parallel.Facts.Meander[j]&&sequential.Facts.Sediment[j]==parallel.Facts.Sediment[j],"Serial and parallel terrain diagnostics agree");
                Check(sequential.Biomes[j]==parallel.Biomes[j]&&sequential.Fertility[j]==parallel.Fertility[j],"Serial and parallel biome updates agree");
            }
            w.Facts.Connections[left]=3;w.Facts.Connections[right]=9;w.Facts.FlowX[left]=1;w.Facts.FlowY[right]=1;
            w.Facts.HalfWidth[left]=.025f;
            Check(FlatWaterGeometry.RiverDistance(w,left,1,.5f)<0,"Zero channel bend keeps the crossing centred without a preset world-space wave");
            w.Facts.Meander[left]=w.Facts.Meander[right]=w.Facts.Meander[w.Neighbor(left,0)]=.018f;
            Check(FlatWaterGeometry.RiverDistance(w,left,.25f,.62f)<0,"Straight drainage tile follows a gradual multi-tile meander");
            Check(FlatWaterGeometry.RiverDistance(w,left,.25f,.5f)>0,"Narrow channel is no longer ruler-straight");
            float positiveCentre=0,negativeCentre=0,positiveDistance=float.MaxValue,negativeDistance=float.MaxValue;
            w.Facts.Meander[left]=w.Facts.Meander[right]=.07f;
            for(int j=0;j<=200;j++)
            {
                float y=j/200f,distance=FlatWaterGeometry.RiverDistance(w,left,1,y);
                if(distance<positiveDistance){positiveDistance=distance;positiveCentre=y;}
            }
            w.Facts.Meander[left]=w.Facts.Meander[right]=-.07f;
            for(int j=0;j<=200;j++)
            {
                float y=j/200f,distance=FlatWaterGeometry.RiverDistance(w,left,1,y);
                if(distance<negativeDistance){negativeDistance=distance;negativeCentre=y;}
            }
            Check(positiveCentre-negativeCentre>.30f,"Coherent channel meander moves shared crossings instead of only bending inside each tile");
            int wide=w.Index(16,5);
            for(int axis=0;axis<2;axis++)for(int sign=-1;sign<=1;sign+=2)
            {
                w.Facts.Connections[wide]=(byte)(axis==0?12:3);
                w.Facts.FlowX[wide]=axis;w.Facts.FlowY[wide]=1-axis;
                w.Facts.HalfWidth[wide]=FlatTerrainAnalysis.RiverHalfWidth(1000000);
                w.Facts.Meander[wide]=sign*.08f;
                for(int d=0;d<4;d++)
                {int n=w.Neighbor(wide,d);w.Facts.HalfWidth[n]=w.Facts.HalfWidth[wide];w.Facts.Meander[n]=w.Facts.Meander[wide];}
                for(int edge=0;edge<2;edge++)for(int j=0;j<=100;j++)
                {
                    float distance=FlatWaterGeometry.RiverDistance(w,wide,axis==0?edge:j/100f,axis==0?j/100f:edge);
                    Check(distance>=.05f,"Wide meandering river retains bank clearance at unconnected faces");
                }
            }
            for(int k=0;k<40;k++)
            {
                w.Facts.HalfWidth[left]=.07f+(float)random.NextDouble()*.32f;w.Facts.HalfWidth[right]=.07f+(float)random.NextDouble()*.32f;
                for(int j=8;j<=92;j++)Near(FlatWaterGeometry.RiverDistance(w,left,1,j/100f),FlatWaterGeometry.RiverDistance(w,right,0,j/100f),.00001f,"Shared variable-width river face");
            }
            Check(FlatEcology.Classify(Biome.Grassland,Koppen.Aw,25,950,80)==VegetationCover.Savanna,"Savanna");
            Check(FlatEcology.Classify(Biome.Woodland,Koppen.Cfb,12,1600,80)==VegetationCover.TemperateRainforest,"Temperate rainforest");
            Check(FlatEcology.Classify(Biome.Woodland,Koppen.Cfa,20,900,80)==VegetationCover.DeciduousForest,"Deciduous forest");
            Check(FlatEcology.Classify(Biome.Woodland,Koppen.Csa,16,650,80)==VegetationCover.Shrubland,"Mediterranean shrub");
            Check(FlatEcology.Classify(Biome.Alpine,Koppen.Cfc,5,650,100)==VegetationCover.AlpineMeadow,"Alpine meadow");
            Check(Enum.GetValues(typeof(VegetationCover)).Length==14,"Fourteen derived covers");
            Check(Enum.GetValues(typeof(Biome)).Length==12,"Legacy save biome enum unchanged");
            w.Facts.Canopy[0]=.72f;w.Facts.Vegetation[0]=VegetationCover.BorealForest;var copy=w.Facts.Clone();
            Near(copy.Canopy[0],.72f,.0001f,"Canopy cloned");Check(copy.Vegetation[0]==VegetationCover.BorealForest,"Cover cloned");
            copy.Canopy[0]=0;Near(w.Facts.Canopy[0],.72f,.0001f,"No shared mutable canopy array");
            return Assertions;
        }
        static void TestVegetationSeasons()
        {
            Check(FlatEcology.GrowthTarget(VegetationCover.Grassland,22,1)>.95f,"Warm wet ground supports active growth");
            Near(FlatEcology.GrowthTarget(VegetationCover.DeciduousForest,-12,1),0,0,"Frozen ground stops vegetation growth");
            Check(FlatEcology.GrowthTarget(VegetationCover.Grassland,25,.01f)<.05f,"Heat without soil water cannot support green growth");
            Check(FlatEcology.GrowthTarget(VegetationCover.Tundra,5,.8f)>.2f,"Cold-adapted vegetation uses the short thaw season");
            Near(FlatEcology.GrowthTarget(VegetationCover.Ice,24,1),0,0,"Ice has no vegetation growth");
            Near(FlatEcology.GrowthTarget(VegetationCover.Bare,24,1),0,0,"Bare habitat does not create vegetation");
            var world=new WorldData(64,32,"vegetation-seasons",TerrainPreset.Continents);
            for(int i=0;i<world.Count;i++)
            {
                world.Elevation[i]=world.WaterLevel[i]=400;world.Biomes[i]=Biome.Woodland;
                world.Climates[i]=Koppen.Cfb;world.DrainOrder[i]=i;
                for(int m=0;m<12;m++)
                {
                    world.Temperature[i*12+m]=8-14*(float)Math.Cos(m*Math.PI*2/12);
                    world.Precipitation[i*12+m]=100;
                }
            }
            int north=world.Index(12,16),south=world.Index(13,16),evergreen=world.Index(20,16);
            world.Biomes[evergreen]=Biome.Taiga;world.Climates[evergreen]=Koppen.Dfc;
            for(int m=0;m<12;m++)world.Temperature[south*12+m]=world.Temperature[north*12+(m+6)%12];
            int dry=world.Index(20,10),bank=world.Index(23,10),lake=world.Index(24,10);
            foreach(int i in new[]{dry,bank,lake})for(int m=0;m<12;m++)
            {world.Temperature[i*12+m]=25;world.Precipitation[i*12+m]=0;}
            world.Biomes[dry]=world.Biomes[bank]=Biome.Grassland;
            world.Water[lake]=WaterKind.Lake;world.WaterLevel[lake]=410;world.Biomes[lake]=Biome.Water;
            FlatTerrainAnalysis.Build(world,default,false,1);
            Check(world.Facts.VegetationSeasonsReady,"Terrain analysis publishes the complete seasonal vegetation cache");
            FlatEcology.SampleSeason(world,north,0,out float northWinter,out float winterLeaf);
            FlatEcology.SampleSeason(world,north,180,out float northSummer,out float summerLeaf);
            FlatEcology.SampleSeason(world,south,0,out float southSummer,out _);
            FlatEcology.SampleSeason(world,south,180,out float southWinter,out _);
            Check(northSummer>northWinter+.6f&&summerLeaf>winterLeaf+.6f,"Temperate vegetation grows leaves during the warm season");
            Check(southSummer>southWinter+.6f,"Season timing follows local climate across hemispheres");
            FlatEcology.SampleSeason(world,evergreen,0,out float winterGrowth,out float evergreenLeaf);
            Check(winterGrowth<.1f&&evergreenLeaf>.7f,"Evergreen canopy survives winter while growth is dormant");
            FlatEcology.SampleSeason(world,dry,180,out float dryGrowth,out _);
            FlatEcology.SampleSeason(world,bank,180,out float bankGrowth,out _);
            Check(bankGrowth>dryGrowth+.5f,"Low freshwater banks retain growth during a regional dry season");
            FlatEcology.SampleSeason(world,lake,180,out float waterGrowth,out float waterLeaf);
            Check(waterGrowth==0&&waterLeaf==0,"Open water has no terrestrial seasonal vegetation");
            for(int m=0;m<12;m++)
            {
                FlatEcology.SampleSeason(world,north,m*30-.0001,out float before,out float leafBefore);
                FlatEcology.SampleSeason(world,north,m*30+.0001,out float after,out float leafAfter);
                Check(Math.Abs(before-after)<.00002f&&Math.Abs(leafBefore-leafAfter)<.00002f,"Vegetation blends across month and year boundaries");
            }
            var first=world.Facts;
            var clone=first.Clone();clone.VegetationSeasons[0]^=255;
            Check(clone.VegetationSeasonsReady&&clone.VegetationSeasons[0]!=first.VegetationSeasons[0],"Vegetation cache clones retain state with independent storage");
            FlatTerrainAnalysis.Build(world,default,false,4);
            for(int i=0;i<first.VegetationSeasons.Length;i++)
                Check(first.VegetationSeasons[i]==world.Facts.VegetationSeasons[i],"Seasonal vegetation is independent of worker ordering");
            var loaded=FlatSave.Decode(FlatSave.Encode(world));
            Check(loaded.Facts.VegetationSeasonsReady,"Existing saves rebuild climate-driven vegetation on load");
            for(int i=0;i<first.VegetationSeasons.Length;i++)
                Check(first.VegetationSeasons[i]==loaded.Facts.VegetationSeasons[i],"Saving and loading reproduces seasonal vegetation");
        }
    }
}
