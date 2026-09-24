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
        public static int Run()
        {
            Assertions=0;var random=new Random(320912);
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
            Check(w.Facts.HalfWidth[flows.Length-1]<=.24f,"Mouth full width stays below half a tile");
            var sequential=w.Clone();var parallel=w.Clone();
            FlatTerrainAnalysis.Build(sequential,CancellationToken.None,true,1);
            FlatTerrainAnalysis.Build(parallel,CancellationToken.None,true,8);
            for(int j=0;j<w.Count;j++)
            {
                Check(sequential.Facts.Relief[j]==parallel.Facts.Relief[j]&&sequential.Facts.Wetness[j]==parallel.Facts.Wetness[j]&&sequential.Facts.HalfWidth[j]==parallel.Facts.HalfWidth[j]&&sequential.Facts.Sediment[j]==parallel.Facts.Sediment[j],"Serial and parallel terrain diagnostics agree");
                Check(sequential.Biomes[j]==parallel.Biomes[j]&&sequential.Fertility[j]==parallel.Fertility[j],"Serial and parallel biome updates agree");
            }
            w.Facts.Connections[left]=3;w.Facts.Connections[right]=9;w.Facts.FlowX[left]=1;w.Facts.FlowY[right]=1;
            w.Facts.HalfWidth[left]=.025f;
            Check(FlatWaterGeometry.RiverDistance(w,left,.25f,.62f)<0,"Straight drainage tile follows a gradual multi-tile meander");
            Check(FlatWaterGeometry.RiverDistance(w,left,.25f,.5f)>0,"Narrow channel is no longer ruler-straight");
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
    }
}
