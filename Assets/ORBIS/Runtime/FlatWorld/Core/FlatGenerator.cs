using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Orbis.Flat
{
    // Plate-tectonic bed (FlatTectonics), fluvial erosion (FlatErosion), then water, climate and facts.
    public static class FlatGenerator
    {
        public static IReadOnlyList<TerrainPreset> ProceduralPresets { get; } = Array.AsReadOnly(new[] {
            TerrainPreset.Continents, TerrainPreset.Archipelago, TerrainPreset.GreatContinent, TerrainPreset.InlandSea
        });
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
            => Generate(width,height,seed,preset,cancel,null);

        public static WorldData Generate(int width,int height,string seed,TerrainPreset preset,CancellationToken cancel,byte[] earthHeights)
            => Generate(width,height,seed,preset,cancel,earthHeights,null);

        public static WorldData Generate(int width,int height,string seed,TerrainPreset preset,CancellationToken cancel,byte[] earthHeights,byte[] geographicWater,byte[] geographicRivers=null)
        {
            cancel.ThrowIfCancellationRequested();
            if(preset == TerrainPreset.Earth && (earthHeights == null || earthHeights.LongLength != (long)width*height*sizeof(short)))
                throw new ArgumentException("地球高程数据缺失或与地图大小不符。", nameof(earthHeights));
            var w=new WorldData(width,height,seed,preset);
            if(preset == TerrainPreset.Earth)
            {
                if(geographicWater!=null)
                {
                    w.WaterReference=FlatWaterReference.Read(width,earthHeights,geographicWater);
                    w.UsesSurveyedLakeBeds=w.WaterReference.UsesSurveyedLakeBeds;
                    w.UsesSurveyedStraits=w.WaterReference.UsesSurveyedStraits;
                    w.UsesGlobalStraits=w.WaterReference.UsesGlobalStraits;
                }
                if(geographicRivers!=null)w.RiverReference=FlatEarthRiverReference.Read(geographicRivers);
                for(int y=0;y<height;y++)
                {
                    cancel.ThrowIfCancellationRequested();
                    int source=(height-1-y)*width*2;
                    for(int x=0;x<width;x++,source+=2)
                    {
                        short metres=unchecked((short)(earthHeights[source] | earthHeights[source+1]<<8));
                        if(metres < HeightBands.MinimumBed || metres > HeightBands.MaximumBed)
                            throw new ArgumentException("地球高程超过游戏支持的范围。", nameof(earthHeights));
                        int tile=y*width+x;
                        w.Elevation[tile]=w.UsesSurveyedLakeBeds?w.WaterReference.OriginalBed(tile):metres;
                    }
                }
            }
            else
            {
                w.Tectonics=FlatTectonics.Build(w,cancel);
                FlatErosion.Apply(w,cancel);
                GlebaPlanetGeomorphology.Apply(w,cancel);
            }
            Rebuild(w,cancel);return w;
        }
        public static void Rebuild(WorldData w,CancellationToken cancel)
        {
            DetectWater(w,cancel);
            FlatClimate.Build(w,cancel);
            // Estimate lake supply from the climate without provisional flooding.
            // Recompute the fluxes if a modelled lake changes the evaporating area.
            if(FlatHydrology.BuildDrainage(w,cancel))FlatClimate.Build(w,cancel);
            var discharge=FlatHydrology.RouteRainfall(w,cancel);
            FlatTerrainAnalysis.Build(w,cancel,true,riverDischarge:discharge);
            ReconcileSettlements(w);
            w.TerrainRevision++;w.MarkAllDirty();
        }
        static void DetectWater(WorldData w,CancellationToken cancel)
        {
            var ocean=FlatOcean.Find(w,cancel);var q=new Queue<int>();
            for(int i=0;i<w.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                w.Water[i]=WaterKind.Land;w.WaterLevel[i]=w.Elevation[i];w.CoastDistance[i]=ushort.MaxValue;
                if(ocean[i]){w.Water[i]=HeightBands.Ocean(w.Elevation[i]);w.WaterLevel[i]=0;w.CoastDistance[i]=0;q.Enqueue(i);}
                else if(w.WaterReference!=null)
                {
                    short stage=w.WaterReference.At(i);
                    if(stage>=w.Elevation[i]){w.Water[i]=WaterKind.Lake;w.WaterLevel[i]=stage;}
                }
            }
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
