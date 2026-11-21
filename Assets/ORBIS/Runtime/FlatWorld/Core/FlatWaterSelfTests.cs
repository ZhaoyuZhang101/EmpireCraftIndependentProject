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
        static void TestFlatDrainageQuality()
        {
            var world=new WorldData(64,32,"FLAT-TIE-REGRESSION",TerrainPreset.Continents);
            for(int i=0;i<world.Count;i++)
            {
                int x=i%world.Width,y=i/world.Width;
                bool ocean=x<4&&y>=14&&y<=17;
                world.Elevation[i]=(short)(ocean?-100:100);
                world.Water[i]=ocean?WaterKind.ShallowOcean:WaterKind.Land;
                for(int month=0;month<12;month++)world.Precipitation[i*12+month]=100;
            }
            FlatHydrology.Build(world,CancellationToken.None);
            double GroundDistance(int from,int to)
            {
                double interval=Math.PI/world.Height;
                double south=(from/world.Width+.5)*interval-Math.PI*.5;
                double north=(to/world.Width+.5)*interval-Math.PI*.5;
                int dx=FlatTerrainAnalysis.WrappedDX(to%world.Width,from%world.Width,world.Width);
                double lat=Math.Sin((north-south)*.5),lon=Math.Sin(dx*interval*.5);
                return 2*Math.Asin(Math.Sqrt(lat*lat+Math.Cos(south)*Math.Cos(north)*lon*lon))/interval;
            }
            int diagonal=0;
            for(int y=18;y<=24;y++)for(int x=6;x<=24;x++)
            {
                int at=world.Index(x,y),next=world.Downstream[at],steps=0;
                if(at/world.Width!=next/world.Width&&at%world.Width!=next%world.Width)diagonal++;
                double actual=0,available=0;
                while(!FlatTerrainAnalysis.Ocean(world.Water[next])&&steps++<world.Count)
                {actual+=GroundDistance(at,next);at=next;next=world.Downstream[at];}
                // Construct a feasible route to a known spill face. A shortest
                // spherical path may approach a closer face at another latitude.
                int xx=x,yy=y;
                while(xx>4||yy>17)
                {
                    at=world.Index(xx,yy);
                    if(xx>4)xx--;
                    if(yy>17)yy--;
                    available+=GroundDistance(at,world.Index(xx,yy));
                }
                Check(steps<world.Count&&actual<=available+.0001,"Level routing reaches an outlet within the available ground-distance budget");
            }
            Check(diagonal>0,"An oblique flat valley can drain across local diagonals");
            var repeat=world.Clone();FlatHydrology.Build(repeat,CancellationToken.None);
            for(int i=0;i<world.Count;i++)Check(world.Downstream[i]==repeat.Downstream[i],"Seeded flat routing is repeatable");
            var symmetric=new WorldData(64,32,"EQUAL-SPILLS",TerrainPreset.Continents);
            for(int i=0;i<symmetric.Count;i++)
            {
                int x=i%symmetric.Width;bool coast=x==8||x==24;
                symmetric.Elevation[i]=(short)(coast?-100:100);
                symmetric.Water[i]=coast?WaterKind.ShallowOcean:WaterKind.Land;
            }
            int target=symmetric.Index(16,16),lower=symmetric.Index(15,16);
            symmetric.Elevation[lower]=90;FlatHydrology.Build(symmetric,CancellationToken.None);
            Check(symmetric.Downstream[target]==lower,"Lower actual terrain wins equally long routes to symmetric coasts");
        }
        static void TestChannelMeanders()
        {
            const int width=256,height=128,x=175,first=78,last=102;
            var world=new WorldData(width,height,"CHANNEL-PHASE-REGRESSION",TerrainPreset.Continents);
            for(int i=0;i<world.Count;i++)
            {
                world.Elevation[i]=100;world.WaterLevel[i]=100;
                world.DrainOrder[i]=world.Count-1-i;
            }
            for(int y=first;y<=last;y++)
            {
                int cell=world.Index(x,y);
                world.Water[cell]=WaterKind.River;world.Runoff[cell]=24;
                world.Downstream[cell]=world.Index(x,y+1);
            }
            int mouth=world.Index(x,last+1),tributary=world.Index(x-1,90);
            world.Water[mouth]=WaterKind.Lake;world.Elevation[mouth]=99;
            world.Water[tributary]=WaterKind.River;world.Runoff[tributary]=24;
            world.Downstream[tributary]=world.Index(x,90);
            FlatTerrainAnalysis.Build(world,CancellationToken.None,false,1);
            int changes=0;float openBends=0;
            for(int y=first;y<=last;y++)
            {
                int cell=world.Index(x,y);openBends+=Math.Abs(world.Facts.Meander[cell]);
                if(y>first&&world.Facts.Meander[cell]*world.Facts.Meander[world.Index(x,y-1)]<0)changes++;
            }
            Check(changes>=3,"Lowland meander wavelength follows channel distance across a global-field stationary point");
            Check(Math.Abs(world.Facts.Meander[tributary]-world.Facts.Meander[world.Index(x,89)])<.000001f,"Equal tributary approaches inherit the same downstream bend phase");
            var confined=world.Clone();
            for(int y=first;y<=last;y++)
            {confined.Elevation[confined.Index(x-2,y)]=1200;confined.Elevation[confined.Index(x+2,y)]=1200;}
            FlatTerrainAnalysis.Build(confined,CancellationToken.None,false,1);
            float confinedBends=0;
            for(int y=first;y<=last;y++)confinedBends+=Math.Abs(confined.Facts.Meander[confined.Index(x,y)]);
            Check(confinedBends<openBends*.4f,"Nearby valley walls confine bends even with level immediate banks");
            var parallel=world.Clone();FlatTerrainAnalysis.Build(parallel,CancellationToken.None,false,8);
            for(int i=0;i<world.Count;i++)Check(world.Facts.Meander[i]==parallel.Facts.Meander[i],"Channel meanders agree across worker counts");
        }
        public static int Run()
        {
            Assertions=0;
            TestFlatDrainageQuality();TestChannelMeanders();
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
            foreach(TerrainPreset preset in FlatGenerator.ProceduralPresets)
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
