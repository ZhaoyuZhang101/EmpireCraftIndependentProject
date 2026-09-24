using System;
using System.Collections.Generic;
using UnityEngine;
namespace Orbis.Flat
{
    public sealed class FlatMapRenderer : MonoBehaviour
    {
        public WorldData World { get; private set; }
        public Texture2D TerrainTexture { get; private set; }
        public Texture2D DisplayTexture { get; private set; }
        public Texture2D OwnerTexture { get; private set; }
        public Texture2D InfoTexture { get; private set; }
        public Texture2D HydroTexture { get; private set; }
        public Texture2D MixingTexture { get; private set; }
        public Texture2D GeometryTexture { get; private set; }
        public Texture2D VisualTexture { get; private set; }
        public FlatSkyRenderer Sky { get; private set; }
        public FlatLocalWater LocalWater { get; private set; }
        public Material Material { get; private set; }
        public MapLayer Layer { get; private set; }
        public MapLayer VisibleLayer { get; private set; }
        Mesh mesh;MeshRenderer meshRenderer;int dataRevision=-1;
        FlatMapRenderJobs renderJobs;
        public bool PreparationPending {get{return renderJobs!=null&&renderJobs.Pending;}}
        public int RejectedPreparationBatches {get{return renderJobs==null?0:renderJobs.RejectedBatches;}}
        public int LastPreparedTiles {get{return renderJobs==null?0:renderJobs.ScheduledTiles;}}
        public double LastSnapshotMilliseconds {get{return renderJobs==null?0:renderJobs.LastSnapshotMilliseconds;}}
        public double LastPreparationLatencyMilliseconds {get{return renderJobs==null?0:renderJobs.LastLatencyMilliseconds;}}
        public double LastUploadMilliseconds {get;private set;}
        sealed class ChunkBuffers
        {
            public readonly Color32[] Terrain,Display,Owners,Info;
            public readonly Color[] Hydro,Mixing,Geometry,Visual;
            public ChunkBuffers(int count)
            {
                Terrain=new Color32[count];Display=new Color32[count];Owners=new Color32[count];Info=new Color32[count];
                Hydro=new Color[count];Mixing=new Color[count];Geometry=new Color[count];Visual=new Color[count];
            }
        }
        readonly Dictionary<int,ChunkBuffers> buffers=new Dictionary<int,ChunkBuffers>();
        ChunkBuffers GetBuffers(int count)
        {ChunkBuffers b;if(!buffers.TryGetValue(count,out b)){b=new ChunkBuffers(count);buffers.Add(count,b);}return b;}
        public int LastUploadedChunks { get; private set; }
        static Texture2D Make(int w,int h,string label,bool linear=false)
        {return new Texture2D(w,h,TextureFormat.RGBA32,false,linear){name=label,filterMode=FilterMode.Point,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,anisoLevel=0};}
        static Texture2D MakeData(int width,int height,string label,bool smooth)
        {
            TextureFormat format=SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf)?TextureFormat.RGBAHalf:TextureFormat.RGBAFloat;
            if(!SystemInfo.SupportsTextureFormat(format))throw new NotSupportedException("Water data requires RGBAHalf or RGBAFloat textures.");
            return new Texture2D(width,height,format,false,true){name=label,filterMode=smooth?FilterMode.Bilinear:FilterMode.Point,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,anisoLevel=0};
        }
        public void Initialize(WorldData world)
        {
            if(world.Facts==null)FlatTerrainAnalysis.Build(world,System.Threading.CancellationToken.None,false);
            World=world;TerrainTexture=Make(world.Width,world.Height,"Flat world terrain / water");DisplayTexture=Make(world.Width,world.Height,"Flat world active overlay");OwnerTexture=Make(world.Width,world.Height,"Authoritative nation colors");InfoTexture=Make(world.Width,world.Height,"Flat world semantic data",true);
            TerrainTexture.filterMode=DisplayTexture.filterMode=FilterMode.Bilinear;
            Shader shader=Resources.Load<Shader>("ORBIS/FlatWorld/FlatMap");if(!shader)throw new InvalidOperationException("Missing ORBIS/FlatWorld/FlatMap shader.");
            if(!shader.isSupported)throw new InvalidOperationException("FlatMap shader is unsupported or failed to compile. See the Unity shader Console report.");
            Material=new Material(shader);Material.SetTexture("_MainTex",DisplayTexture);Material.SetTexture("_OwnerTex",OwnerTexture);Material.SetTexture("_InfoTex",InfoTexture);Material.SetVector("_MapSize",new Vector4(world.Width,world.Height,1f/world.Width,1f/world.Height));
            HydroTexture=MakeData(world.Width,world.Height,"Depth / downhill flow / river width",true);
            MixingTexture=MakeData(world.Width,world.Height,"Connected estuary / shore / river links",true);
            GeometryTexture=MakeData(world.Width,world.Height,"Metres / sediment material / soil wetness",true);
            VisualTexture=MakeData(world.Width,world.Height,"Continuous elevation / Euclidean shore / depth / canopy",true);
            Material.SetTexture("_VisualTex",VisualTexture);Material.SetFloat("_FieldReady",1);
            Material.SetFloat("_SmoothTerrain",1);Material.SetFloat("_Vegetation",1);Material.SetFloat("_Bathymetry",1);Material.SetFloat("_Relief",1);
            Material.SetTexture("_HydroA",HydroTexture);Material.SetTexture("_HydroB",MixingTexture);Material.SetTexture("_GeoTex",GeometryTexture);
            Material.SetTexture("_SWEState",Texture2D.grayTexture);Material.SetFloat("_SWEReady",0);
            Material.SetFloat("_ShowWater",1);Material.SetFloat("_ShowBanks",1);Material.SetFloat("_ShowPlumes",1);Material.SetFloat("_WaveAmount",1);
            Material.SetFloat("_ShowContours",0);Material.SetFloat("_ContourInterval",100);Material.SetFloat("_WaterView",1);Material.SetFloat("_Natural",1);
            Orbis.Planet.OrbisAssetVisuals.BindWater(Material);
            Sky=gameObject.AddComponent<FlatSkyRenderer>();Sky.Initialize(world);
            LocalWater=gameObject.AddComponent<FlatLocalWater>();LocalWater.Initialize(world,Material);
            mesh=new Mesh{name="2D authoritative rectangular map"};
            mesh.vertices=new[]{new Vector3(0,0,0),new Vector3(world.Width,0,0),new Vector3(0,world.Height,0),new Vector3(world.Width,world.Height,0)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,1,2,3};mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;meshRenderer=gameObject.AddComponent<MeshRenderer>();meshRenderer.sharedMaterial=Material;meshRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;meshRenderer.receiveShadows=false;
            renderJobs=new FlatMapRenderJobs(world);Flush(true);renderJobs.CompleteInitialWarmup();Flush();Select(-1);
        }
        public void SetLayer(MapLayer layer) { if(Layer==layer)return;Layer=layer;Flush(true); }
        public void Select(int i)
        {Material.SetVector("_Selected",i<0?new Vector4(-100,-100,-100,-100):new Vector4(i%World.Width,i/World.Width,(i%World.Width)/4*4,(i/World.Width)/4*4));}
        public void Configure(bool grid,float pixelsPerTile)
        {
            Material.SetFloat("_Grid",grid?1:0);Material.SetFloat("_PixelsPerTile",pixelsPerTile);
            ConfigureLayer();
        }
        void ConfigureLayer()
        {
            Material.SetFloat("_Natural",VisibleLayer==MapLayer.Natural?1:0);Material.SetFloat("_Political",VisibleLayer==MapLayer.Nations?1:0);
            Material.SetFloat("_DrawHeight",VisibleLayer==MapLayer.Height?1:0);
            Material.SetFloat("_WaterView",VisibleLayer==MapLayer.Natural||VisibleLayer==MapLayer.Nations||VisibleLayer==MapLayer.Water?1:0);
        }
        public void Flush(bool force=false)
        {
            if(World==null||renderJobs==null)return;
            LastUploadedChunks=0;
            if(force)World.MarkAllDirty();
            if(renderJobs.Pending)
            {
                bool accepted;
                if(!renderJobs.TryComplete(World,Layer,out accepted))return;
                if(accepted)PublishCompleted();
            }
            bool refreshData=dataRevision!=World.TerrainRevision;
            if(refreshData)World.MarkAllDirty();
            renderJobs.Schedule(Layer,refreshData);
        }
        void PublishCompleted()
        {
            long start=System.Diagnostics.Stopwatch.GetTimestamp();bool refreshData=renderJobs.RefreshData;
            foreach(var c in renderJobs.Chunks)
            {
                var b=GetBuffers(c.Count);
                for(int j=0;j<c.Count;j++)
                {
                    var pixel=renderJobs.GetPixel(c.Offset+j);
                    b.Terrain[j]=pixel.Terrain;b.Display[j]=pixel.Display;b.Owners[j]=pixel.Owner;b.Info[j]=pixel.Info;
                    if(refreshData){b.Hydro[j]=pixel.Hydro;b.Mixing[j]=pixel.Mixing;b.Geometry[j]=pixel.Geometry;b.Visual[j]=pixel.Visual;}
                }
                TerrainTexture.SetPixels32(c.X,c.Y,c.Width,c.Height,b.Terrain);DisplayTexture.SetPixels32(c.X,c.Y,c.Width,c.Height,b.Display);
                OwnerTexture.SetPixels32(c.X,c.Y,c.Width,c.Height,b.Owners);InfoTexture.SetPixels32(c.X,c.Y,c.Width,c.Height,b.Info);
                if(refreshData){HydroTexture.SetPixels(c.X,c.Y,c.Width,c.Height,b.Hydro);MixingTexture.SetPixels(c.X,c.Y,c.Width,c.Height,b.Mixing);GeometryTexture.SetPixels(c.X,c.Y,c.Width,c.Height,b.Geometry);VisualTexture.SetPixels(c.X,c.Y,c.Width,c.Height,b.Visual);}
                LastUploadedChunks++;
            }
            if(LastUploadedChunks>0)
            {
                TerrainTexture.Apply(false,false);DisplayTexture.Apply(false,false);OwnerTexture.Apply(false,false);InfoTexture.Apply(false,false);
                if(refreshData){HydroTexture.Apply(false,false);MixingTexture.Apply(false,false);GeometryTexture.Apply(false,false);VisualTexture.Apply(false,false);dataRevision=renderJobs.CapturedTerrainRevision;}
            }
            // New dirties belong to the next snapshot; publication never clears them.
            VisibleLayer=renderJobs.CapturedLayer;ConfigureLayer();
            LastUploadMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        }
        void OnDestroy()
        {if(renderJobs!=null){renderJobs.Dispose();renderJobs=null;}if(mesh)Destroy(mesh);if(Material)Destroy(Material);foreach(var t in new[]{TerrainTexture,DisplayTexture,OwnerTexture,InfoTexture,HydroTexture,MixingTexture,GeometryTexture,VisualTexture})if(t)Destroy(t);}
    }
}


