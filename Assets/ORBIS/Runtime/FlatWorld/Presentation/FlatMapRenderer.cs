using System;
using System.Collections.Generic;
using Orbis.Planet;
using Unity.Collections;
using UnityEngine;
namespace Orbis.Flat
{
    public sealed class FlatMapRenderer : MonoBehaviour
    {
        public WorldData World { get; private set; }
        public Texture2D TerrainTexture { get; private set; }
        internal int PublishedTerrainRevision => dataRevision;
        public Texture2D DisplayTexture { get; private set; }
        public Texture2D OwnerTexture { get; private set; }
        public Texture2D InfoTexture { get; private set; }
        public Texture2D HydroTexture { get; private set; }
        public Texture2D MixingTexture { get; private set; }
        public Texture2D GeometryTexture { get; private set; }
        public Texture2D VisualTexture { get; private set; }
        internal FlatFoldHeights FoldHeights { get; private set; }
        Vector4 poleBedDepth;
        public Texture2D EcologyTexture { get; private set; }
        public Texture2D VegetationTexture { get; private set; }
        Texture2D lakeIceTexture;
        const int GlebaFlowWidth=256,GlebaFlowHeight=128;
        Texture2D glebaWindFlowTex,glebaCurrentFlowTex,glebaBandsTex;
        ushort[] glebaFlowBuffer,glebaBandsBuffer;
        int uploadedGlebaFlowMonth=-1,uploadedGlebaFlowRevision=-1,uploadedGlebaFlowMode=-1;
        double uploadedGlebaBandsDay=double.NaN;
        float circulationClock;
        public Texture2D RiverSeasonTexture { get; private set; }
        FlatRiverStageField riverSeasons;
        public FlatSkyRenderer Sky { get; private set; }
        public FlatLocalWater LocalWater { get; private set; }
        public Material Material { get; private set; }
        public MapLayer Layer { get; private set; }
        public MapLayer VisibleLayer { get; private set; }
        public int ClimateYearOffset { get; private set; }
        internal int LocalFocusTile { get; private set; } = -1;
        internal PlanetLocation? SelectedLocation { get; private set; }
        internal RectInt LocalEditFootprint { get; private set; }
        Mesh mesh;MeshRenderer meshRenderer;int dataRevision=-1;
        FlatTerrainFacts vegetationSource;
        bool vegetationMonthPair,riverPanelPair;
        int uploadedVegetationMonth=-1,uploadedRiverPanel=-1;
        FlatMapRenderJobs renderJobs;
        FlatRiverAtlas riverAtlas;
        FlatEarthPolarRelief earthRelief;
        public bool RiverAtlasReady {get{return riverAtlas!=null&&riverAtlas.Ready;}}
        public Texture2D RiverAtlasTexture {get{return riverAtlas==null?null:riverAtlas.Texture;}}
        public bool PreparationPending {get{return renderJobs!=null&&renderJobs.Pending||riverAtlas!=null&&riverAtlas.Pending;}}
        public int RejectedPreparationBatches {get{return renderJobs==null?0:renderJobs.RejectedBatches;}}
        public int LastPreparedTiles {get{return renderJobs==null?0:renderJobs.ScheduledTiles;}}
        public double LastSnapshotMilliseconds {get{return renderJobs==null?0:renderJobs.LastSnapshotMilliseconds;}}
        public double LastPreparationLatencyMilliseconds {get{return renderJobs==null?0:renderJobs.LastLatencyMilliseconds;}}
        public double LastUploadMilliseconds {get;private set;}
        sealed class ChunkBuffers
        {
            public readonly Color32[] Terrain,Display,Owners,Info,Ecology;
            public readonly Color[] Hydro,Mixing,Geometry,Visual;
            public ChunkBuffers(int count)
            {
                Terrain=new Color32[count];Display=new Color32[count];Owners=new Color32[count];Info=new Color32[count];
                Ecology=new Color32[count];
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
        void BindWorld(WorldData world,int maximumTextureSize)
        {
            World=world;
            // Keep large worlds' two interpolated months at full tile resolution.
            vegetationMonthPair=world.Width>2048||Math.Max((world.Width+2)*3,(world.Height+2)*4)>maximumTextureSize;
            riverPanelPair=world.Height*3>maximumTextureSize;
        }
        static void BindKoppenPalette()
        {
            // The simulation classifies all cells, including marine cells with
            // surveyed islands. Reuse the exact colours of the CPU climate map.
            var colours=new Vector4[FlatPalette.ClimateColors.Length+1];
            for(int i=0;i<FlatPalette.ClimateColors.Length;i++)
                colours[i]=(Color)FlatPalette.RGB(FlatPalette.ClimateColors[i]);
            colours[colours.Length-1]=(Color)FlatPalette.RGB(FlatPalette.ClimateOceanColor);
            Shader.SetGlobalVectorArray("_OrbisKoppenColours",colours);
        }
        public void Initialize(WorldData world)
        {
            if(world.Facts==null)FlatTerrainAnalysis.Build(world,System.Threading.CancellationToken.None,false);
            BindWorld(world,SystemInfo.maxTextureSize);
            if(world.Preset==TerrainPreset.Earth)BindKoppenPalette();
            TerrainTexture=Make(world.Width,world.Height,"Flat world terrain / water");DisplayTexture=Make(world.Width,world.Height,"Flat world active overlay");OwnerTexture=Make(world.Width,world.Height,"Authoritative nation colors");InfoTexture=Make(world.Width,world.Height,"Flat world semantic data",true);
            TerrainTexture.filterMode=DisplayTexture.filterMode=FilterMode.Bilinear;
            Shader shader=Resources.Load<Shader>("ORBIS/FlatWorld/FlatMap");if(!shader)throw new InvalidOperationException("Missing ORBIS/FlatWorld/FlatMap shader.");
            if(!shader.isSupported)throw new InvalidOperationException("FlatMap shader is unsupported or failed to compile. See the Unity shader Console report.");
            // The globe, local surface and river map share these display settings.
            Shader.SetGlobalFloat("_RiverWidthGain",FlatRiverCurves.DisplayWidthGain);
            Shader.SetGlobalFloat("_RiverMinPixels",2.4f);Shader.SetGlobalFloat("_RiverTint",.38f);
            // Light and shade look in FlatSkyComposite (0 = reference): blue sky-lit shadows, warm low sun, sun-side cloud shading.
            Shader.SetGlobalFloat("_FlatLightLook",1);
            // Cloud translucency (ScrollCloudSource globals, 0 = reference): thin cover and cirrus let the
            // surface show through; only thick cores stay opaque white.
            Shader.SetGlobalFloat("_CloudHighCut",.7f);Shader.SetGlobalFloat("_CloudThinFade",1);Shader.SetGlobalFloat("_CloudCoverCut",.15f);
            Material=new Material(shader);Material.SetTexture("_MainTex",DisplayTexture);Material.SetTexture("_OwnerTex",OwnerTexture);Material.SetTexture("_InfoTex",InfoTexture);Material.SetVector("_MapSize",new Vector4(world.Width,world.Height,1f/world.Width,1f/world.Height));
            HydroTexture=MakeData(world.Width,world.Height,"Depth / downhill flow / river width",true);
            MixingTexture=MakeData(world.Width,world.Height,"River meander / sediment / freshwater / links",true);
            GeometryTexture=MakeData(world.Width,world.Height,"Metres / sediment material / soil wetness",true);
            VisualTexture=MakeData(world.Width,world.Height,"Continuous elevation / Euclidean shore / depth / canopy",true);
            earthRelief=FlatEarthPolarRelief.Create(world);
            EcologyTexture=Make(world.Width,world.Height,"Vegetation cover / biome and tree / wetness / seeded variation",true);
            Material.SetTexture("_EcologyTex",EcologyTexture);Material.SetFloat("_EcologyReady",1);
            Material.SetTexture("_VisualTex",VisualTexture);Material.SetFloat("_FieldReady",1);
            glebaWindFlowTex=MakeData(GlebaFlowWidth,GlebaFlowHeight,"Gleba two-month surface wind flow",true);
            glebaCurrentFlowTex=MakeData(GlebaFlowWidth,GlebaFlowHeight,"Gleba two-month ocean current flow",true);
            glebaBandsTex=MakeData(GlebaFlowWidth,4,"Gleba circulation front latitudes",true);
            glebaFlowBuffer=new ushort[GlebaFlowWidth*GlebaFlowHeight*4];
            glebaBandsBuffer=new ushort[GlebaFlowWidth*4*4];
            Material.SetTexture("_GlebaWindFlowTex",glebaWindFlowTex);
            Material.SetTexture("_GlebaCurrentFlowTex",glebaCurrentFlowTex);
            Material.SetTexture("_GlebaBandsTex",glebaBandsTex);
            RefreshVegetationSeasons();SetVegetationDay(world.Day);
            Material.SetFloat("_SmoothTerrain",1);Material.SetFloat("_Vegetation",1);Material.SetFloat("_Bathymetry",1);Material.SetFloat("_Relief",1);
            Material.SetTexture("_HydroA",HydroTexture);Material.SetTexture("_HydroB",MixingTexture);Material.SetTexture("_GeoTex",GeometryTexture);
            riverAtlas=new FlatRiverAtlas(Material);
            Material.SetTexture("_SWEState",Texture2D.grayTexture);Material.SetFloat("_SWEReady",0);
            Material.SetFloat("_ShowWater",1);Material.SetFloat("_ShowBanks",1);Material.SetFloat("_WaveAmount",1);
            Material.SetFloat("_ShowContours",1);Material.SetFloat("_ContourInterval",100);Material.SetFloat("_WaterView",1);Material.SetFloat("_Natural",1);
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
        public void SetClimateYearOffset(int offset)
        {
            if(offset==ClimateYearOffset)return;
            ClimateYearOffset=offset;
            if(Layer==MapLayer.Temperature||Layer==MapLayer.Rain)Flush(true);
        }
        public void Select(int i) => Select(i, null);
        public void Select(int i, PlanetLocation? location)
        {
            SelectedLocation = i >= 0 ? location : null;
            var p = SelectedLocation.GetValueOrDefault();
            Material.SetVector("_SelectedPoint", SelectedLocation.HasValue ?
                new Vector4((float)PlanetCoordinateSystem.MapU(p), (float)PlanetCoordinateSystem.MapV(p), 1, 0) : Vector4.zero);
            Material.SetVector("_Selected",i<0?new Vector4(-100,-100,-100,-100):new Vector4(i%World.Width,i/World.Width,(i%World.Width)/4*4,(i/World.Width)/4*4));
            SetLocalTarget(i,default);
        }
        public void SetLocalTarget(int tile,RectInt footprint)
        {
            LocalFocusTile=tile;
            LocalEditFootprint=footprint;
        }
        public void Configure(bool grid,float pixelsPerTile,float flowClock=0f)
        {
            circulationClock=flowClock;
            Material.SetFloat("_Grid",grid?1:0);Material.SetFloat("_PixelsPerTile",pixelsPerTile);
            SetVegetationDay(World.Day);
            ConfigureLayer();
        }
        static void UploadHalfData(Texture2D tex,ushort[] halfData)
        {
            if(tex.format==TextureFormat.RGBAHalf)tex.SetPixelData(halfData,0);
            else
            {
                var floats=new float[halfData.Length];
                for(int i=0;i<halfData.Length;i++)floats[i]=Mathf.HalfToFloat(halfData[i]);
                tex.SetPixelData(floats,0);
            }
            tex.Apply(false,false);
        }
        void UpdateClimateLayers(double day,int month,float monthBlend,Material target)
        {
            target.SetFloat("_KoppenMap",VisibleLayer==MapLayer.Climate && World.UsesGlobalStraits ? 1 : 0);
            bool chart=VisibleLayer==MapLayer.Climate||VisibleLayer==MapLayer.SurfaceWinds||VisibleLayer==MapLayer.OceanCurrents||
                       VisibleLayer==MapLayer.RossbyJets||VisibleLayer==MapLayer.ExoStormPotential;
            // A standalone legacy planet retains its native surface until a
            // flat world publishes valid classification and geographic fields.
            if(chart&&target.HasProperty("_FlatWorldReady"))chart=target.GetFloat("_FlatWorldReady")>.5f;
            riverAtlas?.SetDiagnosticMode(target,chart);
            UpdateGlebaCirculation(day,month,monthBlend,target);
        }
        void UpdateGlebaCirculation(double day,int month,float monthBlend,Material target)
        {
            int mode=(VisibleLayer==MapLayer.SurfaceWinds||VisibleLayer==MapLayer.ExoStormPotential)?1:VisibleLayer==MapLayer.OceanCurrents?2:VisibleLayer==MapLayer.RossbyJets?3:0;
            if(mode>0&&glebaWindFlowTex&&glebaCurrentFlowTex&&glebaBandsTex)
            {
                if(uploadedGlebaFlowMonth!=month||uploadedGlebaFlowRevision!=World.TerrainRevision||uploadedGlebaFlowMode!=mode)
                {
                    if(mode==3)
                        GlebaGeographySupplement.PackTwoMonthRossbyFlowHalf(World,month,GlebaFlowWidth,GlebaFlowHeight,glebaFlowBuffer);
                    else
                        GlebaGeographySupplement.PackTwoMonthWindFlowHalf(World,month,GlebaFlowWidth,GlebaFlowHeight,glebaFlowBuffer);
                    UploadHalfData(glebaWindFlowTex,glebaFlowBuffer);
                    if(mode==2||uploadedGlebaFlowRevision!=World.TerrainRevision||uploadedGlebaFlowMonth!=month)
                    {
                        GlebaGeographySupplement.PackTwoMonthCurrentFlowHalf(World,month,GlebaFlowWidth,GlebaFlowHeight,glebaFlowBuffer);
                        UploadHalfData(glebaCurrentFlowTex,glebaFlowBuffer);
                    }
                    uploadedGlebaFlowMonth=month;
                    uploadedGlebaFlowRevision=World.TerrainRevision;
                    uploadedGlebaFlowMode=mode;
                    uploadedGlebaBandsDay=double.NaN;
                }
                if((mode==1||mode==3)&&(double.IsNaN(uploadedGlebaBandsDay)||Math.Abs(day-uploadedGlebaBandsDay)>.05))
                {
                    GlebaGeographySupplement.PackInterpolatedBandsHalf(World,day,GlebaFlowWidth,glebaBandsBuffer);
                    UploadHalfData(glebaBandsTex,glebaBandsBuffer);
                    uploadedGlebaBandsDay=day;
                }
                target.SetTexture("_GlebaWindFlowTex",glebaWindFlowTex);
                target.SetTexture("_GlebaCurrentFlowTex",glebaCurrentFlowTex);
                target.SetTexture("_GlebaBandsTex",glebaBandsTex);
                // The storm diagnostic shares the surface wind data and fronts.
                // The extra cartographic coastline belongs to the wind/current maps.
                int displayMode=VisibleLayer==MapLayer.ExoStormPotential?4:mode;
                target.SetVector("_GlebaCirculation",new Vector4(displayMode,circulationClock,monthBlend,1f));
            }
            else
            {
                target.SetVector("_GlebaCirculation",Vector4.zero);
            }
        }
        void RefreshVegetationSeasons()
        {
            var facts=World.Facts;
            if(ReferenceEquals(vegetationSource,facts))return;
            if(!facts.VegetationSeasonsReady){vegetationSource=null;return;}
            ReleaseTexture(lakeIceTexture);
            var ice=facts.LakeIce;
            if(ice!=null)
            {
                lakeIceTexture=new Texture2D(ice.Width,ice.Height*3,TextureFormat.RGBA32,false,true)
                {name="Monthly lake surface cooling",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
                lakeIceTexture.LoadRawTextureData(ice.Packed);
                lakeIceTexture.Apply(false,true);
            }
            int atlasWidth=(World.Width+2)*3,atlasHeight=(World.Height+2)*4;
            if(!VegetationTexture)
                VegetationTexture=new Texture2D(vegetationMonthPair?World.Width:atlasWidth,vegetationMonthPair?World.Height*2:atlasHeight,TextureFormat.RGBA32,false,true)
                {name="Monthly growth, foliage, snow and soil moisture",filterMode=FilterMode.Bilinear,
                 wrapModeU=vegetationMonthPair?TextureWrapMode.Repeat:TextureWrapMode.Clamp,wrapModeV=TextureWrapMode.Clamp,anisoLevel=0};
            if(!vegetationMonthPair){VegetationTexture.LoadRawTextureData(facts.VegetationSeasons);VegetationTexture.Apply(false,false);}
            uploadedVegetationMonth=-1;
            vegetationSource=facts;
        }
        void UploadVegetationMonths(int month)
        {
            if(vegetationSource==null||!vegetationMonthPair||uploadedVegetationMonth==month)return;
            var pixels=VegetationTexture.GetPixelData<byte>(0);
            int rowBytes=World.Width*4;
            for(int offset=0;offset<2;offset++)
            {
                int panelMonth=(month+offset)%12;
                if(uploadedVegetationMonth>=0&&(panelMonth==uploadedVegetationMonth||panelMonth==(uploadedVegetationMonth+1)%12))continue;
                int destination=(panelMonth%2)*World.Height*rowBytes;
                for(int row=0;row<World.Height;row++)
                {
                    int source=FlatEcology.SeasonOffset(World,row*World.Width,panelMonth);
                    NativeArray<byte>.Copy(vegetationSource.VegetationSeasons,source,pixels,destination+row*rowBytes,rowBytes);
                }
            }
            VegetationTexture.Apply(false,false);uploadedVegetationMonth=month;
        }
        public void SetVegetationDay(double day,Material target=null)
        {
            if(!target)target=Material;
            double period=day/30,whole=Math.Floor(period);int month=(int)((whole%12+12)%12);
            var stages=World.Facts.RiverCurves?.Seasons;
            if(!ReferenceEquals(stages,riverSeasons))
            {
                if(stages!=null)
                {
                    if(!RiverSeasonTexture)
                        RiverSeasonTexture=new Texture2D(World.Width,World.Height*(riverPanelPair?2:3),TextureFormat.RGBA32,false,true)
                        {name="Monthly river discharge ratios",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
                    if(!riverPanelPair){RiverSeasonTexture.LoadRawTextureData(stages.Packed);RiverSeasonTexture.Apply(false,false);}
                    uploadedRiverPanel=-1;
                }
                riverSeasons=stages;
            }
            target.SetTexture("_RiverSeasonTex",RiverSeasonTexture?RiverSeasonTexture:Texture2D.blackTexture);
            target.SetFloat("_RiverSeasonReady",riverSeasons!=null?1:0);
            var months=new Vector4(month,(month+1)%12,(float)(period-whole),0);
            bool iceReady=vegetationSource?.LakeIce!=null&&lakeIceTexture;
            target.SetTexture("_LakeIceTex",iceReady?lakeIceTexture:Texture2D.whiteTexture);
            target.SetVector("_LakeIceSeason",new Vector4(month,months.y,FlatLakeIceField.MonthBlend(months.z),iceReady?1:0));
            if(riverPanelPair&&stages!=null&&uploadedRiverPanel!=month/4)
            {
                int panel=month/4,bytes=World.Count*4;
                var pixels=RiverSeasonTexture.GetPixelData<byte>(0);
                NativeArray<byte>.Copy(stages.Packed,panel*bytes,pixels,0,bytes);
                NativeArray<byte>.Copy(stages.Packed,((panel+1)%3)*bytes,pixels,bytes,bytes);
                RiverSeasonTexture.Apply(false,false);uploadedRiverPanel=panel;
            }
            target.SetVector("_RiverSeason",riverPanelPair?new Vector4(month%4,month%4+1,months.z,1):months);
            if(target!=Material&&riverAtlas!=null)riverAtlas.Bind(target);
            target.SetTexture("_VegetationSeasonTex",VegetationTexture?VegetationTexture:Texture2D.whiteTexture);
            target.SetFloat("_VegetationSeasonReady",vegetationSource!=null?1:0);
            UploadVegetationMonths(month);months.w=vegetationMonthPair?1:0;
            target.SetVector("_VegetationSeason",months);
            var year=FlatClimateYear.At(FMath.Seed(World.Seed),day);
            target.SetVector("_ClimateYearHeat",new Vector4(year.HeatX,year.HeatY,year.HeatZ,year.MeanHeat));
            target.SetVector("_ClimateYearRain",new Vector4(year.RainX,year.RainY,year.RainZ,0));
            UpdateClimateLayers(day,month,(float)(period-whole),target);
        }
        void ConfigureLayer()
        {
            Material.SetFloat("_Natural",VisibleLayer==MapLayer.Natural?1:0);Material.SetFloat("_Political",VisibleLayer==MapLayer.Nations?1:0);
            Material.SetFloat("_DrawHeight",VisibleLayer==MapLayer.Height?1:0);
            Material.SetFloat("_RiverMap",VisibleLayer==MapLayer.Rivers?1:0);
            Material.SetFloat("_WaterView",VisibleLayer==MapLayer.Natural||VisibleLayer==MapLayer.Nations||VisibleLayer==MapLayer.Water?1:0);
            double period=World.Day/30,whole=Math.Floor(period);int month=(int)((whole%12+12)%12);
            UpdateClimateLayers(World.Day,month,(float)(period-whole),Material);
        }
        public void Flush(bool force=false)
        {
            if(World==null||renderJobs==null)return;
            LastUploadedChunks=0;
            if(force)World.MarkAllDirty();
            if(renderJobs.Pending)
            {
                bool accepted;
                if(!renderJobs.TryComplete(World,Layer,out accepted,ClimateYearOffset))
                {riverAtlas.Tick(World.Facts.RiverCurves,World.TerrainRevision,dataRevision);return;}
                if(accepted)PublishCompleted();
            }
            riverAtlas.Tick(World.Facts.RiverCurves,World.TerrainRevision,dataRevision);
            bool refreshData=dataRevision!=World.TerrainRevision;
            if(refreshData)World.MarkAllDirty();
            else earthRelief?.UpdateTreeBanks(Material,false);
            renderJobs.Schedule(Layer,refreshData,ClimateYearOffset);
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
                    if(refreshData){b.Hydro[j]=pixel.Hydro;b.Mixing[j]=pixel.Mixing;b.Geometry[j]=pixel.Geometry;b.Visual[j]=pixel.Visual;b.Ecology[j]=pixel.Ecology;}
                }
                TerrainTexture.SetPixels32(c.X,c.Y,c.Width,c.Height,b.Terrain);DisplayTexture.SetPixels32(c.X,c.Y,c.Width,c.Height,b.Display);
                OwnerTexture.SetPixels32(c.X,c.Y,c.Width,c.Height,b.Owners);InfoTexture.SetPixels32(c.X,c.Y,c.Width,c.Height,b.Info);
                if(refreshData){HydroTexture.SetPixels(c.X,c.Y,c.Width,c.Height,b.Hydro);MixingTexture.SetPixels(c.X,c.Y,c.Width,c.Height,b.Mixing);GeometryTexture.SetPixels(c.X,c.Y,c.Width,c.Height,b.Geometry);VisualTexture.SetPixels(c.X,c.Y,c.Width,c.Height,b.Visual);EcologyTexture.SetPixels32(c.X,c.Y,c.Width,c.Height,b.Ecology);}
                LastUploadedChunks++;
            }
            if(LastUploadedChunks>0)
            {
                TerrainTexture.Apply(false,false);DisplayTexture.Apply(false,false);OwnerTexture.Apply(false,false);InfoTexture.Apply(false,false);
                if(refreshData)
                {
                    double southBed=0,southDepth=0,northBed=0,northDepth=0;
                    for(int x=0;x<World.Width;x++)
                    {
                        int n=World.Count-World.Width+x;
                        southBed+=World.Elevation[x];northBed+=World.Elevation[n];
                        southDepth+=World.Facts.Depth[x];northDepth+=World.Facts.Depth[n];
                    }
                    poleBedDepth=new Vector4((float)(southBed/World.Width),(float)(southDepth/World.Width),
                        (float)(northBed/World.Width),(float)(northDepth/World.Width));
                    earthRelief?.UpdateCoverage(World);
                    if(World.Width>FlatFoldHeights.Columns)
                    {
                        if(FoldHeights==null)FoldHeights=new FlatFoldHeights();
                        FoldHeights.Rebuild(World);
                    }
                    BindPolarTerrain(Material);
                    HydroTexture.Apply(false,false);MixingTexture.Apply(false,false);GeometryTexture.Apply(false,false);VisualTexture.Apply(false,false);EcologyTexture.Apply(false,false);
                    earthRelief?.UpdateTreeBanks(Material,true);
                    dataRevision=renderJobs.CapturedTerrainRevision;RefreshVegetationSeasons();SetVegetationDay(World.Day);
                }
            }
            // New dirties belong to the next snapshot; publication never clears them.
            VisibleLayer=renderJobs.CapturedLayer;ConfigureLayer();
            LastUploadMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        }
        internal void BindPolarTerrain(Material target)
        {
            target.SetVector("_PoleBedDepth",poleBedDepth);
            if(earthRelief!=null)earthRelief.Bind(target);
            else {target.SetFloat("_EarthPolarReady",0);target.SetFloat("_EarthLakeReady",0);target.SetFloat("_EarthStraitReady",0);}
        }
        internal static void ReleaseTexture(Texture2D texture)
        {
            if(!texture)return;
            if(Application.isPlaying)Destroy(texture);
            else DestroyImmediate(texture);
        }
        // Edit-mode fixtures can initialize the renderer without receiving OnDestroy.
        public void ReleaseRenderJobs()
        {
            renderJobs?.Dispose();
            renderJobs=null;
        }
        void OnDestroy()
        {
            FoldHeights?.Dispose();
            earthRelief?.Dispose();
            riverAtlas?.Dispose();
            ReleaseRenderJobs();
            if(mesh)Destroy(mesh);
            if(Material)Destroy(Material);
            foreach(var texture in new[]{TerrainTexture,DisplayTexture,OwnerTexture,InfoTexture,HydroTexture,MixingTexture,GeometryTexture,VisualTexture,EcologyTexture,VegetationTexture,RiverSeasonTexture,lakeIceTexture,glebaWindFlowTex,glebaCurrentFlowTex,glebaBandsTex})
                ReleaseTexture(texture);
        }
    }
}


