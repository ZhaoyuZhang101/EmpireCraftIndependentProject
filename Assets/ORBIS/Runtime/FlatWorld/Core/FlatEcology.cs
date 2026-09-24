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
    }
}
