using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace Orbis
{
    public sealed partial class OrbisPlanetView : MonoBehaviour
    {
        public OrbisWorld world;public int layer;public bool showRivers=true;
        public Camera viewCamera;
        public OrbisAtmosphereProfile atmosphereProfile=new OrbisAtmosphereProfile();
        Mesh terrainMesh,riverMesh,cloudMesh,tileMesh;Material surfaceMaterial,cloudMaterial,riverMaterial;
        GameObject terrainObject,riverObject,cloudObject,tileObject,selection;
        readonly List<GameObject> cities=new List<GameObject>();
        sealed class ChunkBatch
        {
            public Mesh mesh;public MeshRenderer renderer;public int[] source;
            public Vector3[] vertices,normals;public Color[] colors;public List<Vector4> climate,water;
            public int id;public Vector3 center;public float horizonThreshold;
        }
        readonly List<ChunkBatch> chunkBatches=new List<ChunkBatch>();
        Color[] tilePalette;Vector4[] tileClimate,tileWater;int colorCursor;bool colorPending;
        public OrbisWeatherField Weather {get;private set;}
        OrbisCloudShadowCache shadowCache;
        int[] surfaceIndices;Vector3[] surfaceVertices;readonly List<Vector3> surfaceNormals=new List<Vector3>();
        Vector4[] geometryTileState;bool[] geometryTileDirty;float[] uncarvedRadii;
        OrbisWorld geometryWorld;OrbisWorldMap geometryMap;Noise3D geometryNoise;
        readonly Plane[] cullingPlanes=new Plane[6];
        static readonly ProfilerMarker GeometryMarker=new ProfilerMarker("ORBIS.Terrain.Geometry");
        static readonly ProfilerMarker PaletteMarker=new ProfilerMarker("ORBIS.Terrain.Palette");
        public int AppearanceRevision{get;private set;}
        public int GeometrySampleCount{get;private set;}
        public int SurfaceVertexCount=>directions==null?0:directions.Length;
        public int RenderPatchCount=>chunkBatches.Count;
        public int VisibleRenderPatches{get;private set;}
        readonly float[] wavePhases=new float[10];
        public bool UsingSurfaceFallback {get;private set;}
        public Material SurfaceMaterial=>surfaceMaterial;
        public Material CloudMaterial=>cloudMaterial;
        // The CPU lighting state is authoritative, even while shaders are reimporting.
        public Vector3 VisualSunLocal=>nightVisualSun;
        public bool VolumeVisible=>isActiveAndEnabled&&(layer==0||layer==1);
        public OrbisSphericalSampler SurfaceSampler {get;private set;}
        public OrbisSurfaceLOD LocalSurface {get;private set;}
        Vector3[] directions;int[] ca,cb,cc;Vector3[] weights;
        public OrbisRiverNetwork Rivers {get;private set;} = new OrbisRiverNetwork();
        float[] vertexWet,vertexDepth,vertexHeight;
        OrbisBillboardActors actors;
        public void Initialize(OrbisWorld w,Camera camera,Texture2D buildingSprite=null,Texture2D personSprite=null,bool volumeOnly=false)
        {
            world=w;viewCamera=camera;SurfaceSampler=new OrbisSphericalSampler(w.grid);
            SyncNightLighting(w.SunLocal,w.day);
            Shader surfaceShader=Resources.Load<Shader>("ORBIS/Surface");
            if(!surfaceShader||!surfaceShader.isSupported)
            {
                UsingSurfaceFallback=true;surfaceShader=Resources.Load<Shader>("ORBIS/FlatWorld/SurfaceFallback");
                Debug.LogWarning("ORBIS Perf3.2: main Surface unsupported. Using explicit unlit fallback; inspect the current-API shader validation report.");
                if(!surfaceShader||!surfaceShader.isSupported)throw new InvalidOperationException("Main surface and minimal fallback shaders both failed. See Editor.log.");
            }
            surfaceMaterial=new Material(surfaceShader);
            surfaceMaterial.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));
            surfaceMaterial.SetFloat("_ShowWater",1);surfaceMaterial.SetFloat("_WaveAmount",1);
            surfaceMaterial.SetFloat("_SmoothTerrain",1);surfaceMaterial.SetFloat("_Bathymetry",1);
            surfaceMaterial.SetFloat("_Vegetation",1);surfaceMaterial.SetFloat("_Relief",1);
            surfaceMaterial.SetFloat("_FlatNightLights",1);surfaceMaterial.SetFloat("_ContourInterval",100);
            var terrainAtlas=Resources.Load<Texture2D>("ORBIS/TerrainAtlas");
            surfaceMaterial.SetTexture("_TerrainAtlas",terrainAtlas);
            surfaceMaterial.SetFloat("_TerrainAtlasReady",terrainAtlas?1:0);
            if(!terrainAtlas)Debug.LogWarning("ORBIS V4-2: biome atlas missing; retaining vertex biome albedo, not white fallback.");
            cloudMaterial=new Material(Resources.Load<Shader>("ORBIS/Clouds"));
            terrainObject=CreateMeshObject("Surface source mesh",surfaceMaterial,out terrainMesh);
            terrainObject.GetComponent<MeshRenderer>().enabled=false;
            riverMaterial=new Material(Resources.Load<Shader>("ORBIS/RiverWater"));
            riverObject=CreateMeshObject("V4 flowing rivers",riverMaterial,out riverMesh);
            riverObject.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            tileObject=CreateMeshObject("Quad tile grid",surfaceMaterial,out tileMesh);
            cloudObject=CreateMeshObject("Atmosphere",cloudMaterial,out cloudMesh);cloudObject.SetActive(false);
            var sphere=new SphereGrid(4);Vector3[] cloudVertices=new Vector3[sphere.points.Length];for(int i=0;i<cloudVertices.Length;i++)cloudVertices[i]=sphere.points[i]*1.2f;
            cloudMesh.vertices=cloudVertices;cloudMesh.triangles=sphere.triangles;cloudMesh.RecalculateBounds();
            var exempt=new MaterialPropertyBlock();exempt.SetFloat("_PatchLayer",2);
            riverObject.GetComponent<Renderer>().SetPropertyBlock(exempt);tileObject.GetComponent<Renderer>().SetPropertyBlock(exempt);
            if(!volumeOnly)
            {
            actors=gameObject.AddComponent<OrbisBillboardActors>();actors.buildingTexture=buildingSprite;actors.personTexture=personSprite;actors.Initialize(this);
            LocalSurface=gameObject.AddComponent<OrbisSurfaceLOD>();LocalSurface.Initialize(this);
            }
            var volume=camera.gameObject.AddComponent<OrbisAtmosphereRenderer>();volume.view=this;
            if(!volumeOnly)BuildSurfaceTopology();Weather=new OrbisWeatherField(world.grid);shadowCache=new OrbisCloudShadowCache();Weather.Bind(cloudMaterial);Weather.Bind(surfaceMaterial);if(!volumeOnly)RebuildGeometry();UpdateWeather();
        }
        GameObject CreateMeshObject(string name,Material material,out Mesh mesh){var o=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));o.transform.SetParent(transform,false);mesh=new Mesh{name="ORBIS "+name,indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();o.GetComponent<MeshFilter>().sharedMesh=mesh;o.GetComponent<MeshRenderer>().sharedMaterial=material;o.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.On;return o;}
        void BuildSurfaceTopology()
        {
            // Canonical coarse-edge coordinates weld topology BEFORE any terrain/noise sampling.
            const int detail=8;
            var dirs=new List<Vector3>();var idsA=new List<int>();var idsB=new List<int>();var idsC=new List<int>();
            var ws=new List<Vector3>();var indices=new List<int>();var canonical=new Dictionary<Vector3Int,int>();
            int[] coarse=world.grid.triangles;var ids=new int[detail+1,detail+1];
            for(int k=0;k<coarse.Length;k+=3)
            {
                int a=coarse[k],b=coarse[k+1],c=coarse[k+2];
                for(int i=0;i<=detail;i++)for(int j=0;j<=detail-i;j++)
                {
                    int wa=detail-i-j,wb=i,wc=j;Vector3Int key;
                    if(wa==detail)key=new Vector3Int(a,a,0);
                    else if(wb==detail)key=new Vector3Int(b,b,0);
                    else if(wc==detail)key=new Vector3Int(c,c,0);
                    else if(wa==0)key=EdgeKey(b,c,wc,detail);
                    else if(wb==0)key=EdgeKey(a,c,wc,detail);
                    else if(wc==0)key=EdgeKey(a,b,wb,detail);
                    else key=new Vector3Int(-1,k,i*(detail+1)+j);
                    if(!canonical.TryGetValue(key,out int index))
                    {
                        index=dirs.Count;canonical.Add(key,index);Vector3 w=new Vector3(wa,wb,wc)/detail;
                        dirs.Add((world.grid.points[a]*w.x+world.grid.points[b]*w.y+world.grid.points[c]*w.z).normalized);
                        idsA.Add(a);idsB.Add(b);idsC.Add(c);ws.Add(w);
                    }
                    ids[i,j]=index;
                }
                for(int i=0;i<detail;i++)for(int j=0;j<detail-i;j++)
                {
                    indices.Add(ids[i,j]);indices.Add(ids[i+1,j]);indices.Add(ids[i,j+1]);
                    if(i+j<detail-1){indices.Add(ids[i+1,j]);indices.Add(ids[i+1,j+1]);indices.Add(ids[i,j+1]);}
                }
            }
            directions=dirs.ToArray();ca=idsA.ToArray();cb=idsB.ToArray();cc=idsC.ToArray();weights=ws.ToArray();
            surfaceIndices=indices.ToArray();surfaceVertices=new Vector3[dirs.Count];
            uncarvedRadii=new float[dirs.Count];vertexWet=new float[dirs.Count];vertexDepth=new float[dirs.Count];vertexHeight=new float[dirs.Count];
            geometryTileState=new Vector4[world.tiles.Length];geometryTileDirty=new bool[world.tiles.Length];
            surfaceNormals.Capacity=dirs.Count;
            terrainMesh.vertices=directions;terrainMesh.triangles=surfaceIndices;
        }
        static Vector3Int EdgeKey(int a,int b,int toB,int detail)=>a<b?new Vector3Int(a,b,toB):new Vector3Int(b,a,detail-toB);
        public static float SurfaceHeight(Tile c)=>c.h<=0?0:Mathf.Max(c.h,c.lake>.018f?c.flood:c.h);
        void BuildRivers()
        {
            int hint=0;Rivers.Build(world,p=>{var s=SurfaceSampler.Locate(p,hint);hint=s.a;return TerrainHeight(p,s);});
            Rivers.FillMesh(riverMesh,false);
        }
        public void RebuildGeometry()
        {
            using(GeometryMarker.Auto())
            {
                bool all=geometryWorld!=world||geometryMap!=world.MapSource||geometryNoise!=world.noise;
                for(int i=0;i<world.tiles.Length;i++)
                {
                    Tile t=world.tiles[i];Vector4 next=new Vector4(t.h,t.edit,t.lake,t.flood);
                    // Equals compares exact component values: no epsilon hides a small authoring edit.
                    geometryTileDirty[i]=all||!geometryTileState[i].Equals(next);geometryTileState[i]=next;
                }
                BuildRivers();if(layer==10)BuildTileOverlay();tileOverlayRevision=layer==10?world.Revision:-1;
                LocalSurface?.Invalidate();GeometrySampleCount=0;
                for(int i=0;i<directions.Length;i++)
                {
                    if(geometryTileDirty[ca[i]]||geometryTileDirty[cb[i]]||geometryTileDirty[cc[i]])
                    {
                        var s=new OrbisSphericalSampler.Sample{a=ca[i],b=cb[i],c=cc[i],weights=weights[i]};
                        uncarvedRadii[i]=UncarvedRadius(directions[i],s,out vertexWet[i],out vertexDepth[i],out vertexHeight[i]);GeometrySampleCount++;
                    }
                    // River topology can change downstream: never assume only the brush footprint is dirty.
                    float r=uncarvedRadii[i];surfaceVertices[i]=directions[i]*(vertexWet[i]>.5f?r:Rivers.Carve(directions[i],r));
                }
                terrainMesh.vertices=surfaceVertices;terrainMesh.RecalculateNormals();terrainMesh.GetNormals(surfaceNormals);terrainMesh.RecalculateBounds();
                geometryWorld=world;geometryMap=world.MapSource;geometryNoise=world.noise;
                UpdateChunkMeshes();UpdateColors(true);UpdateCities();
            }
        }
        int tileOverlayRevision=-1;
        public Color GetTileColor(int i)
        {
            Tile c=world.tiles[i];bool water=c.h<=0||c.lake>.018f;Color col;
            switch(layer){
                case 1:col=c.owner>=0?world.civs[c.owner].color:water?new Color(.035f,.1f,.15f):new Color(.27f,.31f,.29f);break;
                case 2:float t=Mathf.InverseLerp(-35,45,c.Temperature);col=t<.5f?Color.Lerp(new Color(.2f,.34f,.85f),new Color(.22f,.83f,.68f),t*2):Color.Lerp(new Color(.22f,.83f,.68f),new Color(1,.25f,.1f),(t-.5f)*2);break;
                case 3:col=Color.Lerp(new Color(.52f,.31f,.15f),new Color(.17f,.63f,.86f),Mathf.Clamp01(c.rain/220));break;
                case 4:col=Color.HSVToRGB(Mathf.Repeat(c.plate*.381966f,1),.45f,.8f);break;
                case 5:col=Color.Lerp(new Color(.41f,.25f,.12f),new Color(.24f,.72f,.31f),c.Fertility);break;
                case 6:col=water?new Color(.03f,.08f,.12f):Color.HSVToRGB(Mathf.Repeat(c.basin*.381966f,1),.45f,.8f);break;
                case 11:col=Color.Lerp(new Color(.15f,.28f,.65f),new Color(.95f,.65f,.2f),Mathf.Clamp01(c.wind.magnitude/2));break;
                case 12:col=c.h>0?new Color(.23f,.3f,.25f):Color.Lerp(new Color(.15f,.3f,.72f),new Color(.95f,.32f,.2f),Mathf.InverseLerp(-5,35,c.temp));break;
                case 10:col=c.h<=0?new Color(.045f,.16f,.22f):new Color(.27f,.42f,.31f);break;
                case 8:col=Color.HSVToRGB(Mathf.Repeat(c.zoneId*.381966f+.1f,1),.48f,.86f);break;
                case 9:col=Color.HSVToRGB(Mathf.Repeat(c.chunkId*.381966f+.04f,1),.52f,.88f);break;
                case 7:col=c.h<0?Color.Lerp(new Color(.02f,.07f,.15f),new Color(.13f,.44f,.5f),1+c.h):Color.Lerp(new Color(.22f,.48f,.3f),new Color(.9f,.89f,.82f),c.h);break;
                default:
                    if(water)col=c.h<0?Color.Lerp(new Color(.012f,.058f,.13f),new Color(.035f,.3f,.37f),Mathf.Pow(Mathf.Clamp01(1+c.h),4)):new Color(.05f,.25f,.29f);
                    else {col=c.Fertility<.28f?Color.Lerp(new Color(.67f,.49f,.26f),new Color(.35f,.43f,.19f),c.Fertility/.28f):Color.Lerp(new Color(.35f,.43f,.19f),new Color(.06f,.27f,.16f),(c.Fertility-.28f)/.72f);col=Color.Lerp(col,new Color(.4f,.39f,.36f),Mathf.InverseLerp(.45f,1,c.h));col=Color.Lerp(col,new Color(.88f,.93f,.95f),VisibleSnow(c));if(c.owner>=0)col=Color.Lerp(col,world.civs[c.owner].color,.13f);}
                    break;
            }
            col.a=water&&layer==0?1:0;return col;
        }
        public static float VisibleSnow(Tile t)
        {
            // Mean-temperature thaw mask avoids an old save's saturated snow proxy
            // whitening warm biomes; diurnal thermal fluctuations do not flicker snow.
            return Mathf.Clamp01(t.snow/30)*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,7,t.temp)));
        }
        public Vector4 TileClimate(int id){Tile t=world.tiles[id];return new Vector4(t.soil,VisibleSnow(t),Mathf.InverseLerp(.4f,.95f,t.h),1);}
        public Vector4 TileWater(int id){Tile t=world.tiles[id];return new Vector4(t.h<=0?Mathf.Max(.1f,-t.h*160):t.lake>.018f?Mathf.Max(.2f,t.lake*80):0,0,.5f,0);}
        public float RadiusAt(Vector3 p,out OrbisSphericalSampler.Sample sample,int hint=0)
        {
            p.Normalize();sample=SurfaceSampler.Locate(p,hint);
            float r=UncarvedRadius(p,sample,out float wet,out _,out _);return wet>.5f?r:Rivers.Carve(p,r);
        }
        public float RadiusAt(Vector3 p)=>RadiusAt(p,out _);
        public bool RaycastSurface(Ray ray,out Vector3 direction)
        {
            Vector3 o=ray.origin,d=ray.direction.normalized;direction=Vector3.forward;
            float b=Vector3.Dot(o,d),disc=b*b-o.sqrMagnitude+1.12f*1.12f;
            if(disc<0)return false;float end=-b+Mathf.Sqrt(disc),t=Mathf.Max(0,-b-Mathf.Sqrt(disc));
            if(end<=t)return false;int hint=0;
            Vector3 p=o+d*t;float f=p.magnitude-RadiusAt(p,out var sample,hint);hint=sample.a;
            for(int j=0;j<768&&t<end;j++)
            {
                float next=Mathf.Min(end,t+Mathf.Clamp(Mathf.Abs(f)*.3f,.00015f,.012f));
                Vector3 q=o+d*next;float value=q.magnitude-RadiusAt(q,out sample,hint);hint=sample.a;
                if(value<=0&&f>=0)
                {
                    float lo=t,hi=next;
                    for(int k=0;k<18;k++){float mid=(lo+hi)*.5f;Vector3 at=o+d*mid;if(at.magnitude>RadiusAt(at,out sample,hint))lo=mid;else hi=mid;}
                    direction=(o+d*((lo+hi)*.5f)).normalized;return true;
                }
                t=next;f=value;
            }
            return false;
        }
        public void UpdateColors(bool immediate=false)
        {
            using(PaletteMarker.Auto())
            {
            if(tilePalette==null){tilePalette=new Color[world.tiles.Length];tileClimate=new Vector4[world.tiles.Length];tileWater=new Vector4[world.tiles.Length];}
            for(int i=0;i<tilePalette.Length;i++){tilePalette[i]=GetTileColor(i);tileClimate[i]=TileClimate(i);tileWater[i]=TileWater(i);}
            AppearanceRevision++;colorPending=true;LocalSurface?.RefreshColors();
            if(immediate){colorCursor=0;FlushColors(chunkBatches.Count);}
            }
        }
        public void CopyAppearancePalette(Color[] palette,Vector4[] climates,Vector4[] waters)
        {Array.Copy(tilePalette,palette,tilePalette.Length);Array.Copy(tileClimate,climates,tileClimate.Length);Array.Copy(tileWater,waters,tileWater.Length);}
        void FlushColors(int budget)
        {
            if(!colorPending||chunkBatches.Count==0)return;
            for(int k=0;k<budget;k++)
            {
                if(colorCursor>=chunkBatches.Count){colorCursor=0;colorPending=false;break;}
                var batch=chunkBatches[colorCursor++];
                for(int j=0;j<batch.source.Length;j++)
                {
                    int i=batch.source[j];Vector3 w=weights[i];
                    batch.colors[j]=tilePalette[ca[i]]*w.x+tilePalette[cb[i]]*w.y+tilePalette[cc[i]]*w.z;
                    batch.climate[j]=tileClimate[ca[i]]*w.x+tileClimate[cb[i]]*w.y+tileClimate[cc[i]]*w.z;
                    batch.water[j]=tileWater[ca[i]]*w.x+tileWater[cb[i]]*w.y+tileWater[cc[i]]*w.z;
                    RefineAppearance(ref batch.colors[j],ref batch.water,j,vertexWet[i],vertexDepth[i],vertexHeight[i],batch.climate[j]);
                }
                batch.mesh.colors=batch.colors;batch.mesh.SetUVs(1,batch.climate);batch.mesh.SetUVs(2,batch.water);
                if(colorCursor>=chunkBatches.Count){colorCursor=0;colorPending=false;break;}
            }
        }
        void BuildTileOverlay()
        {
            var vertices=new List<Vector3>();var indices=new List<int>();var cs=new List<Color>();var used=new HashSet<long>();
            var heights=new float[world.grid.corners.Length];var counts=new int[heights.Length];
            for(int i=0;i<world.tiles.Length;i++)foreach(int c in world.grid.tileCorners[i]){heights[c]+=SurfaceHeight(world.tiles[i]);counts[c]++;}
            for(int i=0;i<heights.Length;i++)heights[i]/=counts[i];
            foreach(var corners in world.grid.tileCorners)for(int k=0;k<4;k++){
                int i=corners[k],j=corners[(k+1)%4];long key=((long)Math.Min(i,j)<<32)|(uint)Math.Max(i,j);if(!used.Add(key))continue;
                Vector3 a=world.grid.corners[i],b=world.grid.corners[j],side=Vector3.Cross(a,b).normalized*.00065f;
                float ra=1+heights[i]*.075f+.001f,rb=1+heights[j]*.075f+.001f;int n=vertices.Count;
                vertices.Add((a-side).normalized*ra);vertices.Add((a+side).normalized*ra);vertices.Add((b-side).normalized*rb);vertices.Add((b+side).normalized*rb);indices.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});for(int c=0;c<4;c++)cs.Add(new Color(.85f,.78f,.54f,0));
            }
            tileMesh.Clear();tileMesh.SetVertices(vertices);tileMesh.SetTriangles(indices,0);tileMesh.SetColors(cs);tileMesh.RecalculateNormals();tileMesh.RecalculateBounds();
        }
        static int SpatialPatch(Vector3 p)
        {
            Vector3 a=new Vector3(Mathf.Abs(p.x),Mathf.Abs(p.y),Mathf.Abs(p.z));int face;float u,v,d;
            if(a.x>=a.y&&a.x>=a.z){face=p.x>=0?0:1;d=a.x;u=p.z/d;v=p.y/d;}
            else if(a.y>=a.z){face=p.y>=0?2:3;d=a.y;u=p.x/d;v=p.z/d;}
            else{face=p.z>=0?4:5;d=a.z;u=p.x/d;v=p.y/d;}
            return face*16+Mathf.Clamp((int)((v+1)*2),0,3)*4+Mathf.Clamp((int)((u+1)*2),0,3);
        }
        void UpdateChunkMeshes()
        {
            if(chunkBatches.Count==0)
            {
                var lists=new List<int>[96];for(int i=0;i<lists.Length;i++)lists[i]=new List<int>();
                for(int i=0;i<surfaceIndices.Length;i+=3)
                {
                    int a=surfaceIndices[i],b=surfaceIndices[i+1],c=surfaceIndices[i+2];
                    var list=lists[SpatialPatch((directions[a]+directions[b]+directions[c]).normalized)];list.Add(a);list.Add(b);list.Add(c);
                }
                for(int id=0;id<lists.Length;id++)
                {
                    if(lists[id].Count==0)continue;
                    var source=new List<int>();var triangles=new List<int>();var remap=new Dictionary<int,int>();Vector3 center=Vector3.zero;
                    foreach(int global in lists[id])
                    {
                        if(!remap.TryGetValue(global,out int local)){local=source.Count;source.Add(global);remap.Add(global,local);center+=directions[global];}
                        triangles.Add(local);
                    }
                    center.Normalize();float angle=0;foreach(int global in source)angle=Mathf.Max(angle,Vector3.Angle(center,directions[global]));
                    var o=CreateMeshObject("TerrainRenderPatch_"+id.ToString("D3"),surfaceMaterial,out Mesh mesh);
                    var batch=new ChunkBatch{mesh=mesh,renderer=o.GetComponent<MeshRenderer>(),source=source.ToArray(),id=id,center=center,
                        horizonThreshold=-Mathf.Sin(Mathf.Min(90,angle+25)*Mathf.Deg2Rad),
                        vertices=new Vector3[source.Count],normals=new Vector3[source.Count],colors=new Color[source.Count],
                        climate=new List<Vector4>(new Vector4[source.Count]),water=new List<Vector4>(new Vector4[source.Count])};
                    mesh.vertices=batch.vertices;mesh.triangles=triangles.ToArray();chunkBatches.Add(batch);
                }
            }
            foreach(var batch in chunkBatches)
            {
                for(int i=0;i<batch.source.Length;i++){int src=batch.source[i];batch.vertices[i]=surfaceVertices[src];batch.normals[i]=surfaceNormals[src];}
                batch.mesh.vertices=batch.vertices;batch.mesh.normals=batch.normals;batch.mesh.RecalculateBounds();
            }
        }
        public void UpdateWeather(bool force=false){Weather.view=this;Weather.Update(world,force);}
        public void UpdateCities(){actors?.Refresh();}
        public void Select(int cell){} // The union-boundary selection renderer is owned by OrbisDemo.
        public void BindSurfaceSettings(Material material,double clock,Vector3 sun,bool map=false)
        {
            var p=world.settings;Weather.Bind(material);BindOpticalSettings(material);
            material.SetMatrix("_WorldToPlanet",transform.worldToLocalMatrix);
            material.SetVector("_Sun",sun);material.SetFloat("_Night",p.night?1:0);
            material.SetFloat("_WetSurfaces",p.wetSurfaces?1:0);
            material.SetFloat("_Natural",layer==0?1:0);material.SetFloat("_Map",map?1:0);
            material.SetFloat("_WaterRoughness",p.waterRoughness);material.SetFloat("_SurfaceDetail",p.surfaceDetail);
            material.SetFloat("_RiverPhase",(float)(clock%64.0)/64f);
            material.SetFloat("_Clock",(float)(clock%(Math.PI*4)));material.SetFloat("_SolarEnergy",p.heat);
            material.SetFloat("_FlowPhase",(float)((Weather.VisualClock/OrbisWeather.FlowPeriod)%1));
            material.SetFloat("_Clouds",p.clouds?1:0);material.SetFloat("_Cover",p.cloudCover);material.SetFloat("_Density",p.cloudDensity);
            material.SetFloat("_CloudShadow",p.clouds&&p.cloudShadows&&layer==0?1:0);
            material.SetFloat("_RainPhase",(float)((clock*.65)%1.0));
            material.SetFloat("_EnhancedWater",p.enhancedWater?1:0);material.SetFloat("_WaveStrength",p.waveStrength);
            material.SetInt("_WaterWaveCount",4+2*Mathf.Clamp(p.terrainQuality,0,3));
            for(int i=0;i<10;i++)wavePhases[i]=(float)((clock*2*Math.Pow(1.07,i))%(Math.PI*2));
            material.SetFloatArray("_WavePhases",wavePhases);
        }
        public void BindOpticalSettings(Material material)
        {
            var p=world.settings;
            if(atmosphereProfile==null)atmosphereProfile=new OrbisAtmosphereProfile();
            atmosphereProfile.Bind(material,p.cloudQuality);
            BindNightLighting(material);
            Orbis.Planet.OrbisAssetVisuals.BindCelestials(material);
            material.SetFloat("_Atmosphere",p.atmosphere?1:0);material.SetFloat("_AirDensity",p.airDensity);
            material.SetFloat("_Haze",p.haze);material.SetFloat("_SolarEnergy",p.heat);material.SetFloat("_Night",p.night?1:0);
            material.SetFloat("_Exposure",p.exposure);material.SetFloat("_ShadowStrength",p.shadowStrength);
            material.SetFloat("_GodRays",p.godRays&&p.clouds?1:0);material.SetInt("_SurfaceDebug",p.surfaceDebug);
        }
        // Also called while the planet GameObject is inactive in the unfolded atlas.
        public void SyncVolumeSettings(Vector3 visualSun,double visualTime=-1,double visualCalendarDay=-1)
        {
            SyncNightLighting(visualSun,visualCalendarDay);
            if(visualTime>=0)Weather.VisualClock=visualTime;
            shadowCache?.Update(world,Weather,visualSun);
            BindOpticalSettings(cloudMaterial);
            Weather.Bind(cloudMaterial);cloudMaterial.SetVector("_Sun",visualSun);
            cloudMaterial.SetFloat("_Night",world.settings.night?1:0);
            cloudMaterial.SetFloat("_FlowPhase",(float)((Weather.VisualClock/OrbisWeather.FlowPeriod)%1));
            cloudMaterial.SetFloat("_Cover",world.settings.cloudCover);cloudMaterial.SetFloat("_Density",world.settings.cloudDensity);
            cloudMaterial.SetFloat("_Clouds",world.settings.clouds?1:0);cloudMaterial.SetFloat("_SolarEnergy",world.settings.heat);
            var p=world.settings;
            cloudMaterial.SetFloat("_Atmosphere",p.atmosphere?1:0);cloudMaterial.SetFloat("_AirDensity",p.airDensity);cloudMaterial.SetFloat("_Haze",p.haze);
            cloudMaterial.SetFloat("_CloudMS",p.multipleScattering?1:0);cloudMaterial.SetFloat("_ScatteringStrength",p.scatteringStrength);
            cloudObject.SetActive(false);
        }
        public void Sync(double visualTime,Vector3 visualSun,float interpolation)
        {
            FlushColors(Mathf.Max(1,Mathf.CeilToInt(chunkBatches.Count/8f)));
            Vector3 cameraLocal=transform.InverseTransformPoint(viewCamera.transform.position).normalized;
            GeometryUtility.CalculateFrustumPlanes(viewCamera,cullingPlanes);VisibleRenderPatches=0;
            foreach(var batch in chunkBatches)
            {
                bool visible=Vector3.Dot(cameraLocal,batch.center)>=batch.horizonThreshold&&GeometryUtility.TestPlanesAABB(cullingPlanes,batch.renderer.bounds);
                batch.renderer.enabled=visible;if(visible)VisibleRenderPatches++;
            }
            if(layer==10&&tileOverlayRevision!=world.Revision){BuildTileOverlay();tileOverlayRevision=world.Revision;}
            BindSurfaceSettings(surfaceMaterial,visualTime,visualSun);BindSurfaceSettings(riverMaterial,visualTime,visualSun);
            // V3 atmosphere is a camera volume; never render the old translucent proxy sphere too.
            cloudObject.SetActive(false);tileObject.SetActive(layer==10);riverObject.SetActive(showRivers&&(layer==0||layer==6||layer==7));
            LocalSurface.Tick(visualTime,visualSun);
        }
        void OnDestroy(){foreach(var batch in chunkBatches)if(batch.mesh)Destroy(batch.mesh);foreach(var m in new[]{terrainMesh,riverMesh,cloudMesh,tileMesh})if(m)Destroy(m);if(surfaceMaterial)Destroy(surfaceMaterial);if(cloudMaterial)Destroy(cloudMaterial);if(riverMaterial)Destroy(riverMaterial);shadowCache?.Dispose();Weather?.Dispose();}
    }
}
