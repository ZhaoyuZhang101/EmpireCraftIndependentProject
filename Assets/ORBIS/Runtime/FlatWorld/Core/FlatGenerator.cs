using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Orbis.Flat
{
    // Plate-tectonic bed (FlatTectonics), fluvial erosion (FlatErosion), then water, climate and facts.
    public static class FlatGenerator
    {
        static float Smooth(float x){x=FMath.Clamp(x,0,1);return x*x*(3-2*x);}
        // Depth is a genuine sea-bed elevation source. Continental shelves slope
        // into basins; independent coherent ridge/trench fields add local relief.
        public static float OceanDepth(float continent,float ridge,float trench)
        {
            float offshore=Math.Max(0,-continent);
            float shelf=18+240*Smooth(offshore/.055f);
            float basin=2700*Smooth((offshore-.04f)/.22f);
            float deep=Smooth((offshore-.04f)/.13f);
            return FMath.Clamp(shelf+basin-650*deep*FMath.Clamp(ridge,0,1)+620*deep*FMath.Clamp(trench,0,1),15,3900);
        }
        public static WorldData Generate(int width,int height,string seed,TerrainPreset preset,CancellationToken cancel)
        {
            var w=new WorldData(width,height,seed,preset);
            w.Tectonics=FlatTectonics.Build(w,cancel);
            FlatErosion.Apply(w,cancel);
            Rebuild(w,cancel);return w;
        }
        public static void Rebuild(WorldData w,CancellationToken cancel)
        {
            DetectWater(w,cancel);
            // Spill lakes supply evaporation during initial climate generation
            // and on reload. River discharge then follows the resulting rainfall.
            FlatHydrology.BuildDrainage(w,cancel);
            FlatClimate.Build(w,cancel);
            var discharge=FlatHydrology.RouteRainfall(w,cancel);
            FlatTerrainAnalysis.Build(w,cancel,true,riverDischarge:discharge);
            ReconcileSettlements(w);
            w.TerrainRevision++;w.MarkAllDirty();
        }
        static void DetectWater(WorldData w,CancellationToken cancel)
        {
            var seen=new bool[w.Count];var q=new Queue<int>();var best=new List<int>();
            for(int i=0;i<w.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                w.Water[i]=WaterKind.Land;w.WaterLevel[i]=w.Elevation[i];w.CoastDistance[i]=ushort.MaxValue;
                if(w.Elevation[i]>0||seen[i])continue;
                var part=new List<int>();seen[i]=true;q.Enqueue(i);
                while(q.Count>0){int t=q.Dequeue();part.Add(t);for(int d=0;d<4;d++){int n=w.SphereNeighbor(t,d);if(n>=0&&!seen[n]&&w.Elevation[n]<=0){seen[n]=true;q.Enqueue(n);}}}
                if(part.Count>best.Count)best=part;
            }
            foreach(int i in best){w.Water[i]=HeightBands.Ocean(w.Elevation[i]);w.WaterLevel[i]=0;w.CoastDistance[i]=0;q.Enqueue(i);}
            // Non-ocean depressions are freshwater, not inland salt oceans.
            for(int i=0;i<w.Count;i++)if(w.Elevation[i]<=0&&w.Water[i]==WaterKind.Land){w.Water[i]=WaterKind.Lake;w.WaterLevel[i]=0;}
            while(q.Count>0)
            {
                int i=q.Dequeue();int dist=Math.Min(65534,w.CoastDistance[i]+1);
                for(int d=0;d<4;d++){int n=w.SphereNeighbor(i,d);if(n>=0&&w.CoastDistance[n]>dist){w.CoastDistance[n]=(ushort)dist;q.Enqueue(n);}}
            }
        }
        static void ReconcileSettlements(WorldData w)
        {
            // A terrain edit must not delete people silently or allow cities under the sea.
            for(int i=0;i<w.Count;i++)
            {
                if(w.Buildings[i]==Structure.Bridge&&((w.Water[i]!=WaterKind.River&&w.Water[i]!=WaterKind.Lake)||w.WaterLevel[i]-w.Elevation[i]>100))
                    w.Buildings[i]=Structure.None;
            }
            for(int i=0;i<w.Count;i++)if(!w.Habitable(i))
            {
                if(w.Owner[i]>=0&&w.Owner[i]<w.Nations.Count)w.Nations[w.Owner[i]].Refuges+=w.Population[i];
                w.Population[i]=0;
                if(w.Buildings[i]!=Structure.Bridge)w.Buildings[i]=Structure.None;
            }
            for(int n=0;n<w.Nations.Count;n++)
            {
                var civ=w.Nations[n];int best=-1;
                for(int i=0;i<w.Count;i++)if(w.Owner[i]==n&&w.Habitable(i)&&(best<0||w.Population[i]>w.Population[best]))best=i;
                civ.Capital=best;
                if(best>=0){w.Population[best]+=(float)civ.Refuges;civ.Refuges=0;w.Buildings[best]=Structure.Settlement;}
            }
        }
    }
}
