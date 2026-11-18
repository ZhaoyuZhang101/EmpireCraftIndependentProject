using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Orbis.Flat
{
    // Hand-authored shape rules + periodic coherent noise. No third-party generator code.
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
        static float Noise(float x,float y,int period,uint seed)
        {
            int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);float u=x-ix,v=y-iy;
            u=u*u*(3-2*u);v=v*v*(3-2*v);
            return FMath.Lerp(FMath.Lerp(Cell(ix,iy,period,seed),Cell(ix+1,iy,period,seed),u),FMath.Lerp(Cell(ix,iy+1,period,seed),Cell(ix+1,iy+1,period,seed),u),v);
        }
        static float Cell(int x,int y,int period,uint s)
        { unchecked{return FMath.Unit((uint)FMath.Wrap(x,period)*73428767u^(uint)y*912931u^s)*2-1;} }
        static float FBM(float u,float v,int frequency,uint seed,int octaves=5)
        {
            float result=0,weight=1,sum=0;
            for(int o=0;o<octaves;o++){result+=Noise(u*frequency,v*frequency,frequency,seed+(uint)o*971u)*weight;sum+=weight;weight*=.5f;frequency*=2;}
            return result/sum;
        }
        public static WorldData Generate(int width,int height,string seed,TerrainPreset preset,CancellationToken cancel)
        {
            var w=new WorldData(width,height,seed,preset);uint s=FMath.Seed(seed);
            for(int y=0;y<height;y++)
            {
                cancel.ThrowIfCancellationRequested();float v=(y+.5f)/height;
                for(int x=0;x<width;x++)
                {
                    float u=(x+.5f)/width;
                    float broad=FBM(u,v*.5f,4,s,5),detail=FBM(u,v*.5f,18,s^19287u,4);
                    float continents=broad*.80f+detail*.12f+.015f;
                    if(preset==TerrainPreset.Archipelago)continents=FBM(u,v*.5f,12,s,4)*.65f-.045f;
                    if(preset==TerrainPreset.GreatContinent)continents=broad*.30f+(float)Math.Cos((u-.5f)*Math.PI*2)*.25f+.02f;
                    if(preset==TerrainPreset.InlandSea)
                    {float dx=(u-.5f)*2,dy=(v-.5f)*2;continents=broad*.24f+(float)Math.Sqrt(dx*dx+dy*dy)*.44f-.25f;}
                    // Polar land remains seed-dependent; do not connect north and south rows.
                    float ridge=1-Math.Abs(FBM(u,v*.5f,10,s^91231u,4));
                    float mountain=(float)Math.Pow(FMath.Clamp((ridge-.55f)/.45f,0,1),3)*2700;
                    float polar=Smooth(Math.Min(v,1-v)/.06f);
                    float floorRidge=polar*Smooth((FBM(u,v*.5f,9,s^0x09f25a3u,4)*.5f+.5f-.52f)/.23f);
                    float floorTrench=polar*Smooth((FBM(u,v*.5f,23,s^0x16ab992u,3)*.5f+.5f-.62f)/.27f);
                    float meters=continents>0?continents*3500+mountain*FMath.Clamp(continents*7,0,1):-OceanDepth(continents,floorRidge,floorTrench);
                    // Drainage needs continuous slopes. Preset height bands are
                    // reserved for the editing brush and map labels.
                    w.Elevation[y*width+x]=(short)Math.Round(FMath.Clamp(meters,-4000,4000));
                }
            }
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
