using System;
using System.Threading;
namespace Orbis.Flat
{
    // Executed only when the user runs the core validation menu or the .NET project.
    public static class FlatWaterSelfTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition,string name)
        {Assertions++;if(!condition)throw new InvalidOperationException("Water/climate regression: "+name);}
        public static int Run()
        {
            Assertions=0;
            Check(FlatTerrainAnalysis.ClassifyForm(1500,200,100,800,false,false,false)==Landform.Plateau,"High level ground is plateau");
            Check(FlatTerrainAnalysis.ClassifyForm(400,450,150,400,false,false,false)==Landform.Hills,"Local relief is hills");
            Check(FlatTerrainAnalysis.ClassifyForm(1800,1200,600,700,false,false,false)==Landform.Mountains,"Steep mountains");
            Check(FlatTerrainAnalysis.ClassifyBiome(Koppen.Af,100,26,1800,12,50,50,false,false)==Biome.Rainforest,"Humid tropical forest away from freshwater");
            Check(FlatTerrainAnalysis.ClassifyBiome(Koppen.Af,100,26,1800,12,50,50,true,false)==Biome.Wetland,"Riparian saturation creates wetland");
            Check(FlatTerrainAnalysis.ClassifyBiome(Koppen.Af,500,26,1800,12,400,50,true,false)==Biome.Rainforest,"Steep riparian land is not swamp");
            Check(FlatTerrainAnalysis.ClassifyBiome(Koppen.BWh,100,26,100,0,50,500,false,false)==Biome.Desert,"BW desert");
            Check(FlatTerrainAnalysis.ClassifyBiome(Koppen.BSk,400,10,400,0,50,500,false,false)==Biome.Steppe,"BS steppe");
            Check(FlatTerrainAnalysis.ClassifyBiome(Koppen.EF,100,-10,200,0,0,0,false,false)==Biome.Snow,"Polar ice");
            const int size=32;var depth=new float[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)depth[y*size+x]=x==15?0: .4f+(x+y)%9*.3f;
            var lake=new FlatShallowWater(size,size,2,depth);
            for(int n=0;n<60;n++)lake.Advance(.1);
            for(int i=0;i<depth.Length;i++)Check(Math.Abs(lake.Depth[i]-depth[i])<1e-10,"Lake at rest above varying bottom");
            lake.Ripple(8,16,.08);for(int n=0;n<600;n++)lake.Advance(.1);
            Check(lake.RelativeMassError()<1e-10,"Balanced ripple conserves water");
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                int i=y*size+x;Check(!double.IsNaN(lake.Depth[i])&&!double.IsInfinity(lake.Depth[i])&&lake.Depth[i]>=0,"Finite nonnegative water");
                if(x==15)Check(lake.Depth[i]==0,"Dry wall stays dry");
                if(x>15)Check(Math.Abs(lake.Depth[i]-depth[i])<1e-10,"No flux across dry wall");
            }
            lake.Advance(100,3);Check(lake.LastSubsteps<=3,"Bounded work budget");
            foreach(TerrainPreset preset in Enum.GetValues(typeof(TerrainPreset)))
            {
                var w=FlatGenerator.Generate(64,32,"WATER-CLIMATE-REGRESSION",preset,CancellationToken.None);
                var f=w.Facts;Check(f!=null,"Derived facts exist");var rank=new int[w.Count];
                for(int j=0;j<w.Count;j++)rank[w.DrainOrder[j]]=j;
                for(int i=0;i<w.Count;i++)
                {
                    int parent=w.Downstream[i];
                    if(parent>=0){Check(rank[parent]<rank[i],"Acyclic drainage");Check(w.WaterLevel[parent]<=w.WaterLevel[i],"Downhill water head");}
                    Check(FMath.Finite(f.Depth[i])&&f.Depth[i]>=0,"Valid rendered water depth");
                    Check(f.Freshness[i]>=0&&f.Freshness[i]<=1&&f.Sediment[i]>=0&&f.Sediment[i]<=1,"Mixing proxies bounded");
                    if(w.Water[i]==WaterKind.Land)Check(f.Freshness[i]==0&&f.HalfWidth[i]==0,"No water plume painted across land");
                    if(w.Water[i]==WaterKind.River&&parent>=0)Check(f.FlowX[i]==FlatTerrainAnalysis.WrappedDX(parent%w.Width,i%w.Width,w.Width)&&f.FlowY[i]==parent/w.Width-i/w.Width,"Flow follows drainage receiver");
                }
                var clone=w.Clone();Check(!ReferenceEquals(clone.Facts.Depth,w.Facts.Depth),"Derived arrays cloned independently");
            }
            return Assertions;
        }
    }
}
