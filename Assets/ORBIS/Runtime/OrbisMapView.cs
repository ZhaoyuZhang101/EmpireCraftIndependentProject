using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
namespace Orbis
{
    // Atlas presentation shares authoritative tile IDs, heights and rivers with the globe.
    public sealed class OrbisMapView:MonoBehaviour
    {
        const int Width=256,Height=128;
        static readonly ProfilerMarker AppearanceMarker=new ProfilerMarker("ORBIS.Map.AppearanceSlice");
        static readonly ProfilerMarker GeometryMarker=new ProfilerMarker("ORBIS.Map.Geometry");
        Mesh mesh,riverMesh;Material material,riverMaterial;MeshRenderer riverDisplay;
        OrbisWorld world;OrbisPlanetView planetView;int revision=-1,riverRevision=-1,geometryRiverRevision=-1;
        Vector3[] directions,sphere,normals;OrbisSphericalSampler.Sample[] samples;Color[] colors,palette;
        Vector4[] tileClimate,tileWater;float[] wet,depth,height;List<Vector4> climate,water;List<Vector3> sphereUV;
        int cursor,startedAppearance=-1,publishedAppearance=-1,startedLayer=-1,publishedLayer=-1;bool pending;
        public int RemainingAppearanceVertices=>pending?samples.Length-cursor:0;
        public int AppearancePublications{get;private set;}
        public static Vector3 Project(Vector3 p,float elevation=0)
        {p.Normalize();return new Vector3(-Mathf.Atan2(p.x,p.z),Mathf.Asin(Mathf.Clamp(p.y,-1,1)),elevation*.12f);}
        public static Vector3 Unproject(Vector3 p)
        {float lat=Mathf.Clamp(p.y,-Mathf.PI/2,Mathf.PI/2);return new Vector3(Mathf.Sin(-p.x)*Mathf.Cos(lat),Mathf.Sin(lat),Mathf.Cos(p.x)*Mathf.Cos(lat));}
        public void Initialize(OrbisWorld state,OrbisPlanetView view)
        {
            world=state;planetView=view;material=new Material(view.SurfaceMaterial);material.SetFloat("_Map",1);material.SetFloat("_PatchActive",0);view.Weather.Bind(material);
            mesh=new Mesh{name="TerrainPerf1 cached atlas",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
            var rivers=new GameObject("Same river network in atlas",typeof(MeshFilter),typeof(MeshRenderer));rivers.transform.SetParent(transform,false);
            riverMesh=new Mesh{name="V4 atlas river water",indexFormat=IndexFormat.UInt32};riverMesh.MarkDynamic();rivers.GetComponent<MeshFilter>().sharedMesh=riverMesh;
            riverMaterial=new Material(Resources.Load<Shader>("ORBIS/RiverWater"));riverDisplay=rivers.GetComponent<MeshRenderer>();riverDisplay.sharedMaterial=riverMaterial;riverDisplay.shadowCastingMode=ShadowCastingMode.Off;
            BuildTopology();RebuildGeometry();Refresh(true);
        }
        public void SetWorld(OrbisWorld state){world=state;revision=riverRevision=geometryRiverRevision=-1;pending=false;publishedAppearance=-1;}
        void BuildTopology()
        {
            int row=Width+1,n=row*(Height+1);var vertices=new Vector3[n];var indices=new int[Width*Height*6];
            directions=new Vector3[n];sphere=new Vector3[n];normals=new Vector3[n];samples=new OrbisSphericalSampler.Sample[n];colors=new Color[n];wet=new float[n];depth=new float[n];height=new float[n];
            climate=new List<Vector4>(new Vector4[n]);water=new List<Vector4>(new Vector4[n]);sphereUV=new List<Vector3>(new Vector3[n]);
            palette=new Color[world.tiles.Length];tileClimate=new Vector4[world.tiles.Length];tileWater=new Vector4[world.tiles.Length];int hint=0;
            for(int y=0;y<=Height;y++)for(int x=0;x<=Width;x++)
            {
                int i=y*row+x;Vector3 v=new Vector3((x/(float)Width*2-1)*Mathf.PI,(y/(float)Height-.5f)*Mathf.PI,0);
                vertices[i]=v;directions[i]=Unproject(v);samples[i]=planetView.SurfaceSampler.Locate(directions[i],hint);hint=samples[i].a;
            }
            int k=0;for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {int a=y*row+x;indices[k++]=a;indices[k++]=a+1;indices[k++]=a+row;indices[k++]=a+1;indices[k++]=a+row+1;indices[k++]=a+row;}
            mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateBounds();
        }
        void RebuildGeometry()
        {
            using(GeometryMarker.Auto())
            {
                int row=Width+1;
                for(int i=0;i<samples.Length;i++)
                {
                    float r=planetView.UncarvedRadius(directions[i],samples[i],out wet[i],out depth[i],out height[i]);
                    // Use the same carved river bed as the globe for normals and material sampling.
                    if(wet[i]<.5f)r=planetView.Rivers.Carve(directions[i],r);
                    sphere[i]=directions[i]*r;sphereUV[i]=sphere[i];
                }
                for(int y=0;y<=Height;y++)for(int x=0;x<=Width;x++)
                {
                    int i=y*row+x,left=x==0?Width-1:x-1,right=x==Width?1:x+1;
                    Vector3 dx=sphere[y*row+right]-sphere[y*row+left],dy=sphere[Mathf.Min(Height,y+1)*row+x]-sphere[Mathf.Max(0,y-1)*row+x];
                    Vector3 normal=Vector3.Cross(dx,dy).normalized;if(normal.sqrMagnitude<.1f)normal=directions[i];if(Vector3.Dot(normal,directions[i])<0)normal=-normal;normals[i]=normal;
                }
                mesh.normals=normals;mesh.SetUVs(0,sphereUV);revision=world.Revision;geometryRiverRevision=planetView.Rivers.Revision;pending=false;publishedAppearance=-1;
            }
        }
        public void Refresh(bool immediate=false)
        {
            if(world==null)return;
            if(revision!=world.Revision||geometryRiverRevision!=planetView.Rivers.Revision)RebuildGeometry();
            if(riverRevision!=planetView.Rivers.Revision){planetView.Rivers.FillMesh(riverMesh,true);riverRevision=planetView.Rivers.Revision;}
            if(startedLayer!=planetView.layer)pending=false;
            if(!pending&&(publishedAppearance!=planetView.AppearanceRevision||publishedLayer!=planetView.layer))BeginAppearance();
            if(immediate&&pending)FlushAppearance(int.MaxValue);
            material.SetFloat("_Natural",planetView.layer==0?1:0);riverDisplay.enabled=planetView.showRivers;
        }
        void BeginAppearance()
        {
            planetView.CopyAppearancePalette(palette,tileClimate,tileWater);
            startedAppearance=planetView.AppearanceRevision;startedLayer=planetView.layer;cursor=0;pending=true;
        }
        void FlushAppearance(int budget)
        {
            using(AppearanceMarker.Auto())
            {
                int end=budget>=samples.Length?samples.Length:Mathf.Min(samples.Length,cursor+budget);
                for(;cursor<end;cursor++)
                {
                    int i=cursor;var s=samples[i];Vector3 w=s.weights;
                    colors[i]=palette[s.a]*w.x+palette[s.b]*w.y+palette[s.c]*w.z;
                    climate[i]=tileClimate[s.a]*w.x+tileClimate[s.b]*w.y+tileClimate[s.c]*w.z;
                    water[i]=tileWater[s.a]*w.x+tileWater[s.b]*w.y+tileWater[s.c]*w.z;
                    planetView.RefineAppearance(ref colors[i],ref water,i,wet[i],depth[i],height[i],climate[i]);
                }
                if(cursor==samples.Length)
                {
                    mesh.colors=colors;mesh.SetUVs(1,climate);mesh.SetUVs(2,water);pending=false;
                    publishedAppearance=startedAppearance;publishedLayer=startedLayer;AppearancePublications++;
                }
            }
        }
        public void Sync(double clock,Vector3 sun,float interpolation)
        {
            Refresh();if(pending)FlushAppearance(4096);
            planetView.BindSurfaceSettings(material,clock,sun,true);material.SetFloat("_PatchActive",0);
            planetView.BindSurfaceSettings(riverMaterial,clock,sun,true);riverMaterial.SetFloat("_PatchActive",0);riverDisplay.enabled=planetView.showRivers;
        }
        void OnDestroy(){if(mesh)Destroy(mesh);if(riverMesh)Destroy(riverMesh);if(material)Destroy(material);if(riverMaterial)Destroy(riverMaterial);}
    }
}
