using System;
namespace Orbis.Flat
{
    // Derived vegetation subdivisions, not new persistent enum values in a v1 save.
    public enum VegetationCover : byte
    { Bare, Grassland, DrySteppe, Savanna, Shrubland, SeasonalForest, DeciduousForest,
      TemperateRainforest, BorealForest, TropicalRainforest, Marsh, AlpineMeadow, Tundra, Ice }

    public static class FlatEcology
    {
        public static float ContinuousCanopy(Biome biome,float temperature,float annualRain,float soil,float gradient)
        {
            if(biome==Biome.Water||biome==Biome.Snow||biome==Biome.Beach)return 0;
            float demand=Math.Max(240,(temperature+5)*36);
            float available=FMath.Clamp(soil*.55f+annualRain/demand*.45f,0,1);
            float warmth=FMath.Clamp((temperature+8)/18,0,1),slope=1-FMath.Clamp((gradient-200)/1100,0,.85f);
            float t=FMath.Clamp((available-.12f)/.78f,0,1);t=t*t*(3-2*t);
            float canopy=(.04f+.90f*t)*warmth*slope;
            if(biome==Biome.Tundra||biome==Biome.Alpine)canopy=Math.Min(.16f,canopy);
            if(biome==Biome.Wetland)canopy=Math.Min(.42f,canopy);
            return canopy;
        }
        public static VegetationCover Classify(Biome biome,Koppen climate,float temperature,float rain,float gradient)
        {
            if(biome==Biome.Water||biome==Biome.Beach||biome==Biome.Desert)return VegetationCover.Bare;
            if(biome==Biome.Snow)return VegetationCover.Ice;
            if(biome==Biome.Tundra)return VegetationCover.Tundra;
            if(biome==Biome.Alpine)return temperature>1&&rain>=450&&gradient<=200?VegetationCover.AlpineMeadow:VegetationCover.Bare;
            if(biome==Biome.Wetland)return VegetationCover.Marsh;
            if(biome==Biome.Steppe)return VegetationCover.DrySteppe;
            if(biome==Biome.Taiga)return VegetationCover.BorealForest;
            if(climate==Koppen.Aw)return VegetationCover.Savanna;
            if(climate==Koppen.Csa||climate==Koppen.Csb||climate==Koppen.Csc)return VegetationCover.Shrubland;
            if(biome==Biome.Rainforest)return climate==Koppen.Am?VegetationCover.SeasonalForest:VegetationCover.TropicalRainforest;
            if(biome==Biome.Woodland)
            {
                if(climate==Koppen.Cwa||climate==Koppen.Cwb||climate==Koppen.Cwc)return VegetationCover.SeasonalForest;
                return temperature<18&&rain>=1400?VegetationCover.TemperateRainforest:VegetationCover.DeciduousForest;
            }
            return VegetationCover.Grassland;
        }
        public static float Density(VegetationCover cover)
        {
            switch(cover)
            {
                case VegetationCover.Grassland:return .20f;case VegetationCover.DrySteppe:return .10f;
                case VegetationCover.Savanna:return .36f;case VegetationCover.Shrubland:return .43f;
                case VegetationCover.SeasonalForest:return .66f;case VegetationCover.DeciduousForest:return .74f;
                case VegetationCover.TemperateRainforest:return .88f;case VegetationCover.BorealForest:return .72f;
                case VegetationCover.TropicalRainforest:return .94f;case VegetationCover.Marsh:return .29f;
                case VegetationCover.AlpineMeadow:return .18f;case VegetationCover.Tundra:return .08f;default:return 0;
            }
        }
        static float Ramp(float value,float minimum,float maximum)
        {
            float t=FMath.Clamp((value-minimum)/(maximum-minimum),0,1);
            return t*t*(3-2*t);
        }
        public static float GrowthTarget(VegetationCover cover,float temperature,float soilWater)
        {
            if(cover==VegetationCover.Bare||cover==VegetationCover.Ice)return 0;
            bool coldAdapted=cover==VegetationCover.BorealForest||cover==VegetationCover.AlpineMeadow||cover==VegetationCover.Tundra;
            float minimum=coldAdapted?-2:cover==VegetationCover.TropicalRainforest?10:2;
            float warmth=Ramp(temperature,minimum,minimum+10)*(1-Ramp(temperature,32,48));
            bool droughtAdapted=cover==VegetationCover.DrySteppe||cover==VegetationCover.Savanna||cover==VegetationCover.Shrubland;
            float moisture=droughtAdapted?Ramp(soilWater,.02f,.45f):Ramp(soilWater,.08f,.65f);
            return warmth*moisture;
        }
        static float FoliageTarget(VegetationCover cover,float temperature,float soilWater,float growth)
        {
            if(cover==VegetationCover.Bare||cover==VegetationCover.Ice)return 0;
            if(cover==VegetationCover.BorealForest||cover==VegetationCover.TemperateRainforest)
                return .8f+.2f*Ramp(soilWater,.02f,.55f);
            if(cover==VegetationCover.TropicalRainforest)
                return Ramp(temperature,0,14)*(.65f+.35f*Ramp(soilWater,.02f,.55f));
            if(cover==VegetationCover.DeciduousForest||cover==VegetationCover.SeasonalForest)
                return Ramp(temperature,0,12)*Ramp(soilWater,.03f,.55f);
            if(cover==VegetationCover.Savanna||cover==VegetationCover.Shrubland)return .18f+.82f*growth;
            return growth;
        }
        // Twelve padded monthly RG tiles form a 3x4 atlas. Padding is periodic
        // in longitude and clamped at the poles, matching the geographic fields.
        public static int SeasonOffset(WorldData world,int tile,int month)
        {
            int x=month%3*(world.Width+2)+1+tile%world.Width;
            int y=month/3*(world.Height+2)+1+tile/world.Width;
            return (y*(world.Width+2)*3+x)*2;
        }
        internal static void BuildSeasons(WorldData world,FlatTerrainFacts facts,int tile)
        {
            float growth=0,foliage=.5f;var cover=facts.Vegetation[tile];
            // The soil bucket has written twelve water samples into R. Spin up
            // perennial responses before replacing those samples with growth.
            for(int step=0;step<36;step++)
            {
                int month=step%12,offset=SeasonOffset(world,tile,month);
                float soil=facts.VegetationSeasons[offset]/255f,temperature=world.Temperature[tile*12+month];
                float target=GrowthTarget(cover,temperature,soil);
                growth+=(target-growth)*.65f;
                float leaf=FoliageTarget(cover,temperature,soil,target);
                foliage+=(leaf-foliage)*(leaf<foliage?.75f:.60f);
                if(step<24)continue;
                facts.VegetationSeasons[offset]=(byte)(FMath.Clamp(growth,0,1)*255+.5f);
                facts.VegetationSeasons[offset+1]=(byte)(FMath.Clamp(foliage,0,1)*255+.5f);
            }
        }
        internal static void CompleteSeasonAtlas(WorldData world,FlatTerrainFacts facts)
        {
            int width=world.Width+2,height=world.Height+2,stride=width*3*2;
            var data=facts.VegetationSeasons;
            for(int month=0;month<12;month++)
            {
                int start=(month/3*height*width*3+month%3*width)*2;
                for(int row=1;row<=world.Height;row++)
                {
                    int left=start+row*stride,right=left+(width-1)*2;
                    data[left]=data[right-2];data[left+1]=data[right-1];
                    data[right]=data[left+2];data[right+1]=data[left+3];
                }
                Buffer.BlockCopy(data,start+stride,data,start,width*2);
                Buffer.BlockCopy(data,start+world.Height*stride,data,start+(height-1)*stride,width*2);
            }
            facts.VegetationSeasonsReady=true;
        }
        public static void SampleSeason(WorldData world,int tile,double day,out float growth,out float foliage)
        {
            growth=foliage=1;
            if(world.Facts==null||!world.Facts.VegetationSeasonsReady)return;
            double period=day/30,whole=Math.Floor(period);int month=(int)((whole%12+12)%12);
            float blend=(float)(period-whole);
            int first=SeasonOffset(world,tile,month),next=SeasonOffset(world,tile,(month+1)%12);
            var data=world.Facts.VegetationSeasons;
            growth=FMath.Lerp(data[first],data[next],blend)/255f;
            foliage=FMath.Lerp(data[first+1],data[next+1],blend)/255f;
        }
    }
}
