using System;
using System.Collections.Generic;

namespace Orbis.Flat
{
    public enum WaterKind : byte { Land, DeepOcean, MiddleOcean, ShallowOcean, Lake, River }
    public enum Biome : byte { Water, Beach, Grassland, Woodland, Rainforest, Desert, Steppe, Taiga, Tundra, Alpine, Snow, Wetland }
    // Values are stored as one byte in existing world saves.
    public enum TerrainPreset : byte { Continents, Archipelago, GreatContinent, InlandSea, Earth }
    public enum Koppen : byte { Unknown, Af, Am, Aw, BWh, BWk, BSh, BSk, Csa, Csb, Csc, Cwa, Cwb, Cwc, Cfa, Cfb, Cfc, Dsa, Dsb, Dsc, Dsd, Dwa, Dwb, Dwc, Dwd, Dfa, Dfb, Dfc, Dfd, ET, EF }
    public enum Structure : byte { None, Settlement, Farm, Road, Bridge }
    public enum MapLayer : byte { Natural, Nations, Height, Climate, Temperature, Rain, Water, Fertility, Landforms, Materials, Wetness, Sediment, Vegetation, Rivers, SurfaceWinds, OceanCurrents, RossbyJets, RainShadow, RelativeHumidity, GlebaBiomes, GlebaFlora, GlebaSoilTexture, GlebaSoilFertility, GlebaBedrock, GlebaTectonics, GlebaHydroGeo, GlebaCrops, ExoConvectiveRain, ExoRadiationOlr, ExoDiurnalRange, ExoStormPotential, ExoPastaClimate }

    // The core deliberately has no Unity references. Arrays use SOUTH-first rows.
    public static class FMath
    {
        public static float Clamp(float v,float lo,float hi) { return Math.Max(lo,Math.Min(hi,v)); }
        public static int Clamp(int v,int lo,int hi) { return Math.Max(lo,Math.Min(hi,v)); }
        public static float Lerp(float a,float b,float t) { return a+(b-a)*t; }
        public static bool Finite(float f) { return !float.IsNaN(f)&&!float.IsInfinity(f); }
        public static uint Hash(uint v) { unchecked { v^=v>>16;v*=0x7feb352du;v^=v>>15;v*=0x846ca68bu;return v^(v>>16); } }
        public static uint Seed(string text) { unchecked { uint s=2166136261;foreach(char c in text??""){s^=c;s*=16777619;}return s; } }
        public static float Unit(uint v) { return (Hash(v)&0xffffff)/16777215f; }
        public static int Wrap(int x,int w) { return (x%w+w)%w; }
    }
    public static class HeightBands
    {
        // Physical DEM bounds in metres. Authoring brushes and the legacy PNG have their own range.
        public const short MinimumBed = -12000, MaximumBed = 9000;
        // (previous upper bound, current upper bound]. Sea level is 0, not land band 1.
        public static readonly int[] Upper={100,200,300,400,500,600,700,800,900,1000,1120,1280,1480,1720,2000,2320,2680,3080,3520,4000};
        public static int Level(float meters)
        {
            if(!FMath.Finite(meters))throw new ArgumentOutOfRangeException(nameof(meters));
            if(meters<=0)return 0;
            for(int i=0;i<Upper.Length;i++)if(meters<=Upper[i])return i+1;
            return 20;
        }
        public static short Preset(int level)
        { if(level<1||level>20)throw new ArgumentOutOfRangeException(nameof(level));return (short)Upper[level-1]; }
        public static short Quantize(float meters)
        { return meters<=0?(short)FMath.Clamp((int)Math.Round(meters),-4000,0):Preset(Level(meters)); }
        public static WaterKind Ocean(short bed)
        { return bed < -1000 ? WaterKind.DeepOcean : bed < -200 ? WaterKind.MiddleOcean : WaterKind.ShallowOcean; }
    }
    [Serializable]
    public sealed class Nation
    {
        public string Name;
        public int Capital;
        public uint Color; // 0xRRGGBB, stable across both views and saves
        public double Food=250, Refuges, ExpansionClock;
        public Nation Clone() { return (Nation)MemberwiseClone(); }
    }
    public sealed class WorldData
    {
        public const int ZoneSide=4, TilesPerZone=16, MaxWidth=4096, MaxNations=12;
        public readonly int Width,Height,Count,ZoneWidth,ZoneHeight;
        public string Seed;
        public TerrainPreset Preset;
        // Deterministic derived cache. Not a second authoritative simulation or save payload.
        public FlatTerrainFacts Facts;
        public FlatMoistureState Moisture;
        // Plate/volcano record of a generated bed (null for imported heightmaps).
        public FlatTectonicFacts Tectonics;
        public FlatWaterReference WaterReference;
        // The original coarse DEM and the lake-aware Earth preset have distinct edit baselines.
        public bool UsesSurveyedLakeBeds { get; internal set; }
        public bool UsesSurveyedStraits { get; internal set; }
        public FlatEarthRiverReference RiverReference;
        public FlatCycloneWeather Cyclones;
        public ExoPlaSimPlanetConfig PlanetClimate;
        public short[] Elevation,WaterLevel;
        public WaterKind[] Water;
        public Biome[] Biomes;
        public Koppen[] Climates;
        public Structure[] Buildings;
        public int[] Owner,ZoneOwner,Downstream,DrainOrder;
        public float[] Fertility,Runoff,Population,Temperature,Precipitation;
        public ushort[] CoastDistance;
        public List<Nation> Nations=new List<Nation>();
        public readonly List<WildlifeActor> Wildlife=new List<WildlifeActor>();
        public bool WildlifeInitialized;
        public double Day, ExpansionRemainder;
        public int Revision, TerrainRevision;
        public int TileRevision { get; private set; }
        public readonly HashSet<int> DirtyChunks=new HashSet<int>();
        public const int ChunkSide=32;
        public int ChunkWidth { get { return (Width+ChunkSide-1)/ChunkSide; } }
        public int Month { get { return ((int)(Day/30)%12+12)%12; } }
        public int ZoneCount { get { return ZoneWidth*ZoneHeight; } }
        public static bool SupportsDimensions(int width,int height)
        { return width>=64 && width<=MaxWidth && height==width/2 && width%ZoneSide==0 && height%ZoneSide==0; }
        public WorldData(int width,int height,string seed,TerrainPreset preset)
        {
            if(!SupportsDimensions(width,height))
                throw new ArgumentException($"Map must be 2:1, 64..{MaxWidth} tiles wide, dimensions divisible by {ZoneSide}.");
            if(string.IsNullOrWhiteSpace(seed)||seed.Length>128)throw new ArgumentException("Seed must contain 1..128 characters.");
            Width=width;Height=height;Count=width*height;ZoneWidth=width/4;ZoneHeight=height/4;Seed=seed;Preset=preset;
            Elevation=new short[Count];WaterLevel=new short[Count];Water=new WaterKind[Count];Biomes=new Biome[Count];Climates=new Koppen[Count];Buildings=new Structure[Count];
            Owner=new int[Count];ZoneOwner=new int[ZoneCount];Downstream=new int[Count];DrainOrder=new int[Count];
            Fertility=new float[Count];Runoff=new float[Count];Population=new float[Count];CoastDistance=new ushort[Count];
            Temperature=new float[Count*12];Precipitation=new float[Count*12];
            for(int i=0;i<Count;i++){Owner[i]=-1;Downstream[i]=-1;}
            for(int i=0;i<ZoneCount;i++)ZoneOwner[i]=-1;
            MarkAllDirty();
        }
        public int Index(int x,int y) { return y<0||y>=Height?-1:y*Width+FMath.Wrap(x,Width); }
        public int Neighbor(int id,int dir)
        {
            int x=id%Width,y=id/Width;
            switch(dir){case 0:return Index(x-1,y);case 1:return Index(x+1,y);case 2:return Index(x,y-1);default:return Index(x,y+1);}
        }
        public int SphereNeighbor(int id,int dir)
        {
            int next=Neighbor(id,dir);if(next>=0)return next;
            // A geodesic crossing a pole enters the opposite meridian of the SAME pole.
            return (id/Width)*Width+FMath.Wrap(id%Width+Width/2,Width);
        }
        public int ZoneOf(int id) { return (id/Width/4)*ZoneWidth+(id%Width/4); }
        public int ZoneTile(int zone,int local)
        { return ((zone/ZoneWidth)*4+local/4)*Width+(zone%ZoneWidth)*4+local%4; }
        public float Latitude(int id) { return ((id/Width+.5f)/Height-.5f)*180; }
        public bool Habitable(int id)
        { return id>=0&&id<Count&&(Water[id]==WaterKind.Land||Water[id]==WaterKind.River)&&Elevation[id]<3520&&Climates[id]!=Koppen.EF; }
        public bool Walkable(int id)
        { return id>=0&&id<Count&&(Water[id]==WaterKind.Land||Water[id]==WaterKind.River||Buildings[id]==Structure.Bridge); }
        public void Touch(int id)
        { DirtyChunks.Add((id/Width/ChunkSide)*ChunkWidth+(id%Width/ChunkSide));TileRevision++;Revision++; }
        public void MarkAllDirty()
        { for(int y=0;y<(Height+31)/32;y++)for(int x=0;x<ChunkWidth;x++)DirtyChunks.Add(y*ChunkWidth+x);Revision++; }
        public WorldData Clone() => Clone(true);
        // Rebuild publishes new terrain facts and moisture before the worker exposes this copy.
        public WorldData CloneForTerrainRebuild() => Clone(false);
        WorldData Clone(bool copyDerivedCaches)
        {
            var w=new WorldData(Width,Height,Seed,Preset){Day=Day,ExpansionRemainder=ExpansionRemainder,Revision=Revision,TerrainRevision=TerrainRevision,TileRevision=TileRevision,WildlifeInitialized=WildlifeInitialized};
            Array.Copy(Elevation,w.Elevation,Count);Array.Copy(WaterLevel,w.WaterLevel,Count);Array.Copy(Water,w.Water,Count);Array.Copy(Biomes,w.Biomes,Count);Array.Copy(Climates,w.Climates,Count);Array.Copy(Buildings,w.Buildings,Count);
            Array.Copy(Owner,w.Owner,Count);Array.Copy(ZoneOwner,w.ZoneOwner,ZoneCount);Array.Copy(Downstream,w.Downstream,Count);Array.Copy(DrainOrder,w.DrainOrder,Count);
            Array.Copy(Fertility,w.Fertility,Count);Array.Copy(Runoff,w.Runoff,Count);Array.Copy(Population,w.Population,Count);Array.Copy(CoastDistance,w.CoastDistance,Count);
            Array.Copy(Temperature,w.Temperature,Count*12);Array.Copy(Precipitation,w.Precipitation,Count*12);
            if(copyDerivedCaches)
            {
                w.Facts=Facts?.Clone();
                w.Moisture=Moisture?.Clone();
            }
            w.Tectonics=Tectonics;
            w.WaterReference=WaterReference;
            w.UsesSurveyedLakeBeds=UsesSurveyedLakeBeds;
            w.UsesSurveyedStraits=UsesSurveyedStraits;
            w.RiverReference=RiverReference;
            w.Cyclones=Cyclones==null?null:Cyclones.Clone();
            w.PlanetClimate=PlanetClimate?.Clone();
            foreach(var actor in Wildlife)w.Wildlife.Add(actor.Clone());
            foreach(var n in Nations)w.Nations.Add(n.Clone());return w;
        }
    }
}
