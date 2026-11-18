using System;
namespace Orbis.Flat
{
    // Derived vegetation subdivisions, not new persistent enum values in a v1 save.
    public enum VegetationCover : byte
    { Bare, Grassland, DrySteppe, Savanna, Shrubland, SeasonalForest, DeciduousForest,
      TemperateRainforest, BorealForest, TropicalRainforest, Marsh, AlpineMeadow, Tundra, Ice }

    // Matches the existing tree artwork IDs. Derived on terrain rebuild and load.
    public enum TreeSpecies : byte { Broadleaf, Pine, Birch, Palm, Acacia, Willow, Shrub, Tropical }

    public static class FlatEcology
    {
        public static float ContinuousCanopy(Biome biome,float temperature,float annualRain,float soil,float gradient,float warmest=float.NaN)
        {
            if(biome==Biome.Water||biome==Biome.Snow||biome==Biome.Beach)return 0;
            float demand=Math.Max(240,(temperature+5)*36);
            float available=FMath.Clamp(soil*.55f+annualRain/demand*.45f,0,1);
            // Growing-season warmth allows Siberian-type forests to retain a
            // canopy through long winters. Legacy callers retain the annual proxy.
            float warmth=FMath.Finite(warmest)?Ramp(warmest,8,14):FMath.Clamp((temperature+8)/18,0,1);
            float slope=1-FMath.Clamp((gradient-200)/1100,0,.85f);
            float t=FMath.Clamp((available-.12f)/.78f,0,1);t=t*t*(3-2*t);
            float canopy=(.04f+.90f*t)*warmth*slope;
            if(biome==Biome.Tundra||biome==Biome.Alpine&&(!FMath.Finite(warmest)||warmest<=10))canopy=Math.Min(.16f,canopy);
            if(biome==Biome.Wetland)canopy=Math.Min(.42f,canopy);
            return canopy;
        }
        public static VegetationCover Classify(Biome biome,Koppen climate,float temperature,float rain,float gradient,float warmest=float.NaN)
        {
            if(biome==Biome.Water||biome==Biome.Beach||biome==Biome.Desert)return VegetationCover.Bare;
            if(climate==Koppen.EF||biome==Biome.Snow)return VegetationCover.Ice;
            if(climate==Koppen.ET||biome==Biome.Tundra)return VegetationCover.Tundra;
            if(biome==Biome.Alpine)
            {
                // Monthly warmth supports forests on humid tropical highlands.
                if(FMath.Finite(warmest)&&warmest>10&&rain>=650)biome=Biome.Woodland;
                else return temperature>1&&rain>=450&&gradient<=200?VegetationCover.AlpineMeadow:VegetationCover.Bare;
            }
            if(biome==Biome.Wetland)return VegetationCover.Marsh;
            if(biome==Biome.Steppe)return VegetationCover.DrySteppe;
            if(biome==Biome.Taiga)return VegetationCover.BorealForest;
            if(Subarctic(climate)&&biome==Biome.Woodland)return VegetationCover.BorealForest;
            if(climate==Koppen.Aw)return VegetationCover.Savanna;
            if(climate==Koppen.Csa||climate==Koppen.Csb||climate==Koppen.Csc)return VegetationCover.Shrubland;
            if(biome==Biome.Rainforest)return climate==Koppen.Am?VegetationCover.SeasonalForest:VegetationCover.TropicalRainforest;
            if(biome==Biome.Woodland)
            {
                if(climate==Koppen.Cwa||climate==Koppen.Cwb||climate==Koppen.Cwc)return VegetationCover.SeasonalForest;
                if(climate>=Koppen.Dsa&&climate<=Koppen.Dfd)return VegetationCover.DeciduousForest;
                return temperature<18&&rain>=1400?VegetationCover.TemperateRainforest:VegetationCover.DeciduousForest;
            }
            return VegetationCover.Grassland;
        }
        static bool Subarctic(Koppen climate)
        {
            if(climate==Koppen.Csc||climate==Koppen.Cwc||climate==Koppen.Cfc)return true;
            return climate>=Koppen.Dsa&&climate<=Koppen.Dfd&&((int)climate-(int)Koppen.Dsa)%4>=2;
        }
        static bool Mediterranean(Koppen climate)
        {
            return climate>=Koppen.Csa&&climate<=Koppen.Csc;
        }
        // Beck et al. 2018 supplies the climate classes. Monthly temperature and
        // local water supply constrain the representative tree mix within them.
        // Palm winter limit uses the common 5 C cold-month proxy discussed by
        // Reichgelt et al. 2018, https://doi.org/10.1038/s41598-018-23147-2.
        public static TreeSpecies SelectTree(VegetationCover cover,Koppen climate,float coldest,float warmest,
                                             float soilWater,bool freshBank,bool coast,float seed)
        {
            if(cover==VegetationCover.Bare||cover>=VegetationCover.AlpineMeadow||climate>=Koppen.ET||warmest<=10)
                return TreeSpecies.Shrub;
            bool palm=coldest>=5&&warmest>=20&&soilWater>=.25f;
            bool tropical=climate>=Koppen.Af&&climate<=Koppen.Aw&&coldest>=15;
            if(climate>=Koppen.BWh&&climate<=Koppen.BSk)
            {
                if(freshBank&&soilWater>=.55f)
                {
                    if(palm&&seed<.45f)return TreeSpecies.Palm;
                    if(coldest<5)return seed<.70f?TreeSpecies.Willow:TreeSpecies.Birch;
                    return TreeSpecies.Acacia;
                }
                if(coldest>=10&&soilWater>.18f&&climate!=Koppen.BSk&&seed<.72f)return TreeSpecies.Acacia;
                return TreeSpecies.Shrub;
            }
            if(cover==VegetationCover.Marsh)
            {
                if(palm&&(freshBank||coast)&&seed<.25f)return TreeSpecies.Palm;
                if(tropical)return TreeSpecies.Tropical;
                if(Subarctic(climate))
                {
                    if(seed<.25f)return TreeSpecies.Pine;
                    return seed<.5f?TreeSpecies.Birch:TreeSpecies.Shrub;
                }
                return seed<.80f?TreeSpecies.Willow:TreeSpecies.Shrub;
            }
            if(cover==VegetationCover.DrySteppe)return TreeSpecies.Shrub;
            if(cover==VegetationCover.Savanna)
                return coldest>=10&&seed<.85f?TreeSpecies.Acacia:TreeSpecies.Shrub;
            if(Subarctic(climate))return seed<.86f?TreeSpecies.Pine:TreeSpecies.Birch;
            if(climate>=Koppen.Dsa&&climate<=Koppen.Dfd)
            {
                if(freshBank&&soilWater>.65f&&seed<.12f)return TreeSpecies.Willow;
                float pine=((int)climate-(int)Koppen.Dsa)%4==0?.18f:.50f;
                if(seed<pine)return TreeSpecies.Pine;
                return seed<pine+.22f?TreeSpecies.Birch:TreeSpecies.Broadleaf;
            }
            if(Mediterranean(climate))
            {
                if(seed<.35f&&soilWater>.25f)return TreeSpecies.Pine;
                return seed<.60f?TreeSpecies.Broadleaf:TreeSpecies.Shrub;
            }
            if(tropical)
                return palm&&seed<(coast?.45f:.15f)?TreeSpecies.Palm:TreeSpecies.Tropical;
            if(cover==VegetationCover.Shrubland)return TreeSpecies.Shrub;
            if(palm&&(climate==Koppen.Cfa||climate==Koppen.Cwa)&&(coast||freshBank)&&seed<.18f)
                return TreeSpecies.Palm;
            if(freshBank&&soilWater>.65f&&seed<.30f)return TreeSpecies.Willow;
            if(cover==VegetationCover.TemperateRainforest)
                return seed<.60f?TreeSpecies.Pine:TreeSpecies.Broadleaf;
            if(climate==Koppen.Cwb&&warmest<22&&seed<.45f)return TreeSpecies.Pine;
            return seed<.25f?TreeSpecies.Birch:TreeSpecies.Broadleaf;
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
        static float StandCell(int x,int y,int z,uint seed)
        {
            unchecked{return FMath.Unit((uint)x*73428767u^(uint)y*912931u^(uint)z*19349663u^seed);}
        }
        static float StandNoise(float x,float y,float z,uint seed)
        {
            int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y),iz=(int)Math.Floor(z);
            float u=Ramp(x-ix,0,1),v=Ramp(y-iy,0,1),w=Ramp(z-iz,0,1);
            float southLow=FMath.Lerp(StandCell(ix,iy,iz,seed),StandCell(ix+1,iy,iz,seed),u);
            float northLow=FMath.Lerp(StandCell(ix,iy+1,iz,seed),StandCell(ix+1,iy+1,iz,seed),u);
            float southHigh=FMath.Lerp(StandCell(ix,iy,iz+1,seed),StandCell(ix+1,iy,iz+1,seed),u);
            float northHigh=FMath.Lerp(StandCell(ix,iy+1,iz+1,seed),StandCell(ix+1,iy+1,iz+1,seed),u);
            return FMath.Lerp(FMath.Lerp(southLow,northLow,v),FMath.Lerp(southHigh,northHigh,v),w);
        }
        internal static float StandVariation(WorldData world,int x,int y,uint seed,out float clearing)
        {
            // Stands span several authoritative tiles. Spherical coordinates
            // avoid longitude seams and polar stretching in either map view.
            double longitude=(FMath.Wrap(x,world.Width)+.5)/world.Width*Math.PI*2;
            double latitude=((y+.5)/world.Height-.5)*Math.PI;
            float frequency=world.Width/48f,cosine=(float)Math.Cos(latitude);
            float nx=(float)Math.Sin(longitude)*cosine*frequency;
            float ny=(float)Math.Sin(latitude)*frequency;
            float nz=(float)Math.Cos(longitude)*cosine*frequency;
            clearing=StandNoise(nx*.65f+7.1f,ny*.65f-3.7f,nz*.65f+11.3f,seed^0x2c9277b5u);
            return FMath.Clamp(.5f+(StandNoise(nx,ny,nz,seed)-.5f)*2,0,1);
        }
        internal static float StandCanopy(VegetationCover cover,float canopy,float growingSoil,float warmest,float riparian,float clearing)
        {
            if(cover>=VegetationCover.SeasonalForest&&cover<=VegetationCover.TropicalRainforest)
            {
                float moisture=Ramp(growingSoil,.03f,.55f);
                float woodland=FMath.Clamp(canopy*1.18f,0,1)*moisture;
                return woodland*FMath.Lerp(.10f,1,Ramp(clearing,.20f,.42f));
            }
            if(cover==VegetationCover.Marsh)
            {
                float bank=Ramp(riparian,.18f,.55f)*Ramp(growingSoil,.30f,.85f)*Ramp(warmest,10,16);
                return Math.Max(canopy,.92f*bank)*FMath.Lerp(.65f,1,Ramp(clearing,.20f,.42f));
            }
            return canopy;
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
        static float FoliageTarget(VegetationCover cover,TreeSpecies tree,bool evergreen,float temperature,float soilWater,float growth)
        {
            if(cover==VegetationCover.Bare||cover==VegetationCover.Ice)return 0;
            if(evergreen)return .8f+.2f*Ramp(soilWater,.02f,.55f);
            if(tree==TreeSpecies.Birch||tree==TreeSpecies.Willow||tree==TreeSpecies.Broadleaf)
                return Ramp(temperature,0,12)*Ramp(soilWater,.03f,.55f);
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
        internal static void BuildSeasons(WorldData world,FlatTerrainFacts facts,int tile,float coldest)
        {
            float growth=0,foliage=.5f;var cover=facts.Vegetation[tile];
            var tree=facts.Trees[tile];var climate=world.Climates[tile];
            bool evergreenBroadleaf=tree==TreeSpecies.Broadleaf&&
                (cover==VegetationCover.TemperateRainforest||Mediterranean(climate)||
                    coldest>=5&&(climate==Koppen.Cfa||climate==Koppen.Cwa));
            bool evergreen=tree==TreeSpecies.Pine||tree==TreeSpecies.Palm||evergreenBroadleaf||
                tree==TreeSpecies.Tropical&&climate==Koppen.Af||
                tree==TreeSpecies.Shrub&&Mediterranean(climate);
            // The soil bucket has written twelve water samples into R. Spin up
            // perennial responses before replacing those samples with growth.
            for(int step=0;step<36;step++)
            {
                int month=step%12,offset=SeasonOffset(world,tile,month);
                float soil=facts.VegetationSeasons[offset]/255f,temperature=world.Temperature[tile*12+month];
                float target=GrowthTarget(cover,temperature,soil);
                growth+=(target-growth)*.65f;
                float leaf=FoliageTarget(cover,tree,evergreen,temperature,soil,target);
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
