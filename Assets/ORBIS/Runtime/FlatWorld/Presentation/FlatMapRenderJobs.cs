using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Orbis.Flat
{
    /// <summary>One bounded batch, owned and polled by the main thread. Workers see copied values only.</summary>
    public sealed class FlatMapRenderJobs : IDisposable
    {
        struct Tile
        {
            public short Elevation;
            public WaterKind Water;
            public Biome Biome;
            public Koppen Climate;
            public Structure Building;
            public GroundMaterial Material;
            public VegetationCover Vegetation;
            public TreeSpecies Tree;
            public Landform Form;
            public Color32 Owner;
            public float Temperature,Rain,Fertility,Depth,FlowX,FlowY,HalfWidth,Shore,Sediment,Freshness,Wetness,Hand,Canopy,Meander;
            public byte Connections;
        }
        public struct Pixel
        {
            public Color32 Terrain,Display,Owner,Info,Ecology;
            public Color Hydro,Mixing,Geometry,Visual;
        }
        struct PackedColors
        {
            public Color32 Terrain,Display,Info,Ecology;
        }
        public struct Chunk
        {
            public int Id,X,Y,Width,Height,Offset;
            public int Count {get{return Width*Height;}}
        }
        readonly WorldData source;
        readonly uint worldSeed;
        NativeArray<Tile> tiles;
        NativeArray<int> indices,bands;
        NativeArray<uint> palette;
        NativeArray<PackedColors> output;
        readonly List<Chunk> chunks=new List<Chunk>();
        JobHandle handle;
        int terrainRevision,month,climatePeriod;
        MapLayer layer;
        long started;
        bool disposed;
        public bool Pending {get;private set;}
        public bool RefreshData {get;private set;}
        public int CapturedTerrainRevision {get{return terrainRevision;}}
        public MapLayer CapturedLayer {get{return layer;}}
        public int ScheduledTiles {get;private set;}
        public int CapturedTiles {get;private set;}
        public int RejectedBatches {get;private set;}
        public long Version {get;private set;}
        public double LastSnapshotMilliseconds {get;private set;}
        public double LastLatencyMilliseconds {get;private set;}
        public IReadOnlyList<Chunk> Chunks {get{return chunks;}}
        public Pixel GetPixel(int index)
        {
            if(disposed)throw new ObjectDisposedException(nameof(FlatMapRenderJobs));
            if(Pending)throw new InvalidOperationException("Complete the batch before reading its output.");
            var color=output[index];
            var t=tiles[indices[index]];
            // The frozen tile already owns these float channels. Keeping a second native
            // copy until upload would cost another 512 MiB on a 4096 x 2048 map.
            return new Pixel{Terrain=color.Terrain,Display=color.Display,Owner=t.Owner,Info=color.Info,Ecology=color.Ecology,
                Hydro=new Color(t.Depth,t.FlowX,t.FlowY,t.HalfWidth),Mixing=new Color(t.Meander,t.Sediment,t.Freshness,t.Connections),
                Geometry=new Color(t.Elevation,(float)t.Material,t.Wetness,t.Hand),Visual=new Color(t.Elevation,t.Shore,t.Depth,t.Canopy)};
        }
        public FlatMapRenderJobs(WorldData world)
        {
            source=world??throw new ArgumentNullException(nameof(world));
            worldSeed=FMath.Seed(world.Seed);
            tiles=new NativeArray<Tile>(world.Count,Allocator.Persistent);
            indices=new NativeArray<int>(world.Count,Allocator.Persistent);
            output=new NativeArray<PackedColors>(world.Count,Allocator.Persistent);
            bands=new NativeArray<int>(HeightBands.Upper,Allocator.Persistent);
            // Read mutable managed palette tables once, never from a job.
            var colors=new List<uint>();colors.AddRange(FlatPalette.BiomeColors);colors.AddRange(FlatPalette.VegetationColors);
            colors.AddRange(FlatPalette.ClimateColors);colors.AddRange(FlatPalette.LandformColors);colors.AddRange(FlatPalette.MaterialColors);
            palette=new NativeArray<uint>(colors.ToArray(),Allocator.Persistent);
        }
        Tile Capture(int i)
        {
            var f=source.Facts;int owner=source.Owner[i];
            float canopy=f.Canopy[i];
            if(source.Water[i]==WaterKind.Lake && f.ShoreDistance[i]<=2 && source.WaterReference!=null)
            {
                // A surveyed bank may expose land inside a coarse lake cell. Extend
                // the canopy like the land albedo under that bank; water masks it.
                float total=0,weight=0;
                for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
                {
                    int d=dx*dx+dy*dy;
                    if(d==0||d>4)continue;
                    int n=source.Index(i%source.Width+dx,i/source.Width+dy);
                    if(n<0||FlatTerrainAnalysis.AreaWater(source.Water[n]))continue;
                    total+=f.Canopy[n]/d;weight+=1f/d;
                }
                if(weight>0)canopy=total/weight;
            }
            return new Tile{Elevation=source.Elevation[i],Water=source.Water[i],Biome=source.Biomes[i],Climate=source.Climates[i],Building=source.Buildings[i],
                Material=f.Materials[i],Vegetation=f.Vegetation[i],Tree=f.Trees[i],Form=f.Forms[i],Owner=owner<0?new Color32(0,0,0,0):FlatPalette.RGB(source.Nations[owner].Color,(byte)(owner+1)),
                Temperature=source.Temperature[i*12+month],Rain=source.Precipitation[i*12+month],Fertility=source.Fertility[i],Depth=f.Depth[i],FlowX=f.FlowX[i],FlowY=f.FlowY[i],
                // Earth streams its mapped overview first. Suppress the legacy tile
                // channels while waiting for the matching detailed distance field.
                HalfWidth=f.RiverCurves!=null&&f.RiverCurves.HasSurvey?0:f.HalfWidth[i],
                Shore=f.ShoreDistance[i],Sediment=f.Sediment[i],Freshness=f.Freshness[i],Wetness=f.Wetness[i],Hand=f.Hand[i],Canopy=canopy,Meander=f.Meander[i],Connections=f.Connections[i]};
        }
        public bool Schedule(MapLayer requestedLayer,bool refreshData,int climateYearOffset=0)
        {
            if(disposed)throw new ObjectDisposedException(nameof(FlatMapRenderJobs));
            if(Pending)throw new InvalidOperationException("Only one render batch may be in flight.");
            if(source.DirtyChunks.Count==0)return false;
            long start=Stopwatch.GetTimestamp();
            chunks.Clear();layer=requestedLayer;month=source.Month;terrainRevision=source.TerrainRevision;RefreshData=refreshData;
            climatePeriod=ClimatePeriod(source.Day,requestedLayer,climateYearOffset);
            int offset=0;CapturedTiles=0;
            foreach(int id in source.DirtyChunks)
            {
                int x=id%source.ChunkWidth*32,y=id/source.ChunkWidth*32;
                var c=new Chunk{Id=id,X=x,Y=y,Width=Math.Min(32,source.Width-x),Height=Math.Min(32,source.Height-y),Offset=offset};
                chunks.Add(c);
                for(int cy=0;cy<c.Height;cy++)for(int cx=0;cx<c.Width;cx++)indices[offset++]=(y+cy)*source.Width+x+cx;
            }
            bool full=offset==source.Count;
            if(full)
            {
                for(int i=0;i<source.Count;i++)tiles[i]=Capture(i);
                CapturedTiles=source.Count;
            }
            else foreach(var c in chunks)
            {
                // Halo supplies the coastal albedo search, including periodic longitude.
                for(int y=Math.Max(0,c.Y-2);y<Math.Min(source.Height,c.Y+c.Height+2);y++)
                for(int x=c.X-2;x<c.X+c.Width+2;x++)
                {int i=source.Index(x,y);tiles[i]=Capture(i);CapturedTiles++;}
            }
            ScheduledTiles=offset;Version++;
            handle=new PackJob{Tiles=tiles,Indices=indices,Palette=palette,Bands=bands,Output=output,Width=source.Width,Height=source.Height,Seed=worldSeed,Layer=layer,
                ClimateYear=FlatClimateYear.At(worldSeed,climatePeriod*30.0)}.Schedule(offset,128);
            Pending=true;started=Stopwatch.GetTimestamp();
            // Ownership transfers only after successful scheduling. Subsequent main-thread dirties remain in this set.
            source.DirtyChunks.Clear();
            LastSnapshotMilliseconds=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            JobHandle.ScheduleBatchedJobs();
            return true;
        }
        static int ClimatePeriod(double day,MapLayer layer,int yearOffset)
        {
            return (int)Math.Floor(day/30)+(layer==MapLayer.Temperature||layer==MapLayer.Rain?12*yearOffset:0);
        }
        public bool TryComplete(WorldData current,MapLayer currentLayer,out bool accepted,int climateYearOffset=0)
        {
            accepted=false;
            if(!Pending||!handle.IsCompleted)return false;
            handle.Complete();Pending=false;
            LastLatencyMilliseconds=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
            // Ordinary simulation edits remain dirty for the next batch. Rejecting every
            // revision would starve publication when a simulation tick occurs every frame.
            accepted=ReferenceEquals(source,current)&&terrainRevision==current.TerrainRevision&&climatePeriod==ClimatePeriod(current.Day,currentLayer,climateYearOffset)&&layer==currentLayer;
            if(!accepted){RejectedBatches++;RestoreDirty();}
            return true;
        }
        // The renderer uses this only for its first visible frame. Recurring Flush never waits.
        public void CompleteInitialWarmup() {if(Pending)handle.Complete();}
        void RestoreDirty() {foreach(var c in chunks)source.DirtyChunks.Add(c.Id);}
        public void Dispose()
        {
            if(disposed)return;
            if(Pending){handle.Complete();RestoreDirty();Pending=false;}
            if(tiles.IsCreated)tiles.Dispose();if(indices.IsCreated)indices.Dispose();if(output.IsCreated)output.Dispose();
            if(bands.IsCreated)bands.Dispose();if(palette.IsCreated)palette.Dispose();disposed=true;
        }

        [BurstCompile(FloatMode=FloatMode.Strict,FloatPrecision=FloatPrecision.Standard)]
        struct PackJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Tile> Tiles;
            [ReadOnly] public NativeArray<int> Indices,Bands;
            [ReadOnly] public NativeArray<uint> Palette;
            [WriteOnly] public NativeArray<PackedColors> Output;
            public int Width,Height;
            public uint Seed;
            public MapLayer Layer;
            public FlatClimateYear ClimateYear;
            static Color32 RGB(uint value) {return new Color32((byte)(value>>16),(byte)(value>>8),(byte)value,255);}
            static bool Area(WaterKind w) {return w>=WaterKind.DeepOcean&&w<=WaterKind.Lake;}
            static Color32 Water(WaterKind w)
            {
                switch(w){case WaterKind.DeepOcean:return RGB(0x183950);case WaterKind.MiddleOcean:return RGB(0x245c78);case WaterKind.ShallowOcean:return RGB(0x449295);case WaterKind.Lake:return RGB(0x599ba7);case WaterKind.River:return RGB(0x74b0bd);default:return RGB(0x867c64);}
            }
            Color32 Land(Tile t)
            {return FlatPalette.NaturalLand(t.Vegetation,t.Material,t.Wetness,Palette[12+(int)t.Vegetation],Palette[65+(int)t.Material]);}
            public void Execute(int j)
            {
                int i=Indices[j];Tile t=Tiles[i];bool area=Area(t.Water);
                Color32 terrain=area?Water(t.Water):Land(t);
                if(area&&t.Shore<=2)
                {
                    int best=-1,distance=100,tx=i%Width,ty=i/Width;
                    for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
                    {
                        int y=ty+dy,d=dx*dx+dy*dy;
                        if(y<0||y>=Height||d>=distance)continue;
                        int n=y*Width+(tx+dx+Width)%Width;
                        if(!Area(Tiles[n].Water)){best=n;distance=d;}
                    }
                    if(best>=0)terrain=Land(Tiles[best]);
                }
                terrain.a=t.Water==WaterKind.Land?(byte)0:(byte)255;
                Color display=terrain;display.a=1;
                if(Layer==MapLayer.Temperature||Layer==MapLayer.Rain)
                {
                    ClimateYear.AtTile(i,Width,Height,out float degrees,out float rainFraction);
                    t.Temperature+=degrees;t.Rain*=1+rainFraction;
                }
                switch(Layer)
                {
                    case MapLayer.Height:display=area?Water(t.Water):Color.Lerp(RGB(0x597b62),RGB(0xe4e1d2),t.Elevation/4000f);break;
                    case MapLayer.Climate:display=t.Water>=WaterKind.DeepOcean&&t.Water<=WaterKind.ShallowOcean?Water(t.Water):RGB(Palette[26+(int)t.Climate]);break;
                    case MapLayer.Temperature:display=Color.Lerp(RGB(0x4169ab),RGB(0xe9aa6a),Mathf.InverseLerp(-25,35,t.Temperature));break;
                    case MapLayer.Rain:display=Color.Lerp(RGB(0xd1bb93),RGB(0x2c869b),Mathf.Clamp01(t.Rain/240));break;
                    case MapLayer.Water:display=area?Water(t.Water):RGB(0x767b71);break;
                    case MapLayer.Rivers:display=FlatPalette.RiverMapBase(t.Water);break;
                    case MapLayer.Landforms:display=RGB(Palette[57+(int)t.Form]);break;
                    case MapLayer.Materials:display=area?Water(t.Water):RGB(Palette[65+(int)t.Material]);break;
                    case MapLayer.Wetness:display=Color.Lerp(RGB(0xad8862),RGB(0x326c56),t.Wetness);break;
                    case MapLayer.Sediment:display=t.Water==WaterKind.Land?RGB(0x727773):Color.Lerp(RGB(0x326785),RGB(0xb79b69),t.Sediment);break;
                    case MapLayer.Vegetation:display=area?Water(t.Water):RGB(Palette[12+(int)t.Vegetation]);break;
                    case MapLayer.Fertility:display=Color.Lerp(RGB(0xb39d7a),RGB(0x478c68),t.Fertility);break;
                }
                display.a=1;
                int level=0;if(t.Elevation>0){level=20;for(int b=0;b<Bands.Length;b++)if(t.Elevation<=Bands[b]){level=b+1;break;}}
                Output[j]=new PackedColors{Terrain=terrain,Display=Layer==MapLayer.Natural?terrain:(Color32)display,
                    Ecology=new Color32((byte)t.Vegetation,(byte)(((int)t.Tree+1)*16+(int)t.Biome),(byte)Mathf.RoundToInt(Mathf.Clamp01(t.Wetness)*255),(byte)(FMath.Hash((uint)i^Seed)&255)),
                    Info=new Color32((byte)level,(byte)t.Water,(byte)t.Building,(byte)t.Climate)};
            }
        }
    }
}
