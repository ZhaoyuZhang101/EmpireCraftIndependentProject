using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace Orbis
{
    /// <summary>Double-buffered tangent terrain. A complete old mesh stays visible until the
    /// new mesh is ready. Work is sliced on Unity's main thread, not a worker-thread Unity API call.</summary>
    public sealed class OrbisSurfaceLOD:MonoBehaviour
    {
        sealed class Patch
        {
            public Mesh mesh;public MeshRenderer display;
            public Vector3 center,right,north;public float radius;public int resolution,cursor,hint;
            public Vector3[] vertices,geometry;public int[] indices;
            public OrbisSphericalSampler.Sample[] samples;public Color[] colors;public List<Vector4> climates,waters;
            public OrbisWorld world;public int revision,quality,appearanceRevision=-1;
        }
        static readonly int[] Resolutions={48,64,96,128};
        static readonly ProfilerMarker BuildMarker=new ProfilerMarker("ORBIS.Terrain.LocalBuildSlice");
        static readonly ProfilerMarker PublishMarker=new ProfilerMarker("ORBIS.Terrain.LocalPublish");
        OrbisPlanetView view;Material material;Patch active,spare,pending;
        bool invalid=true;bool hasActive;bool requestedVisible;
        public int VertexCount=>hasActive?active.mesh.vertexCount:0;
        public bool IsBuilding=>pending!=null;
        public int PendingVertices=>pending==null?0:pending.vertices.Length-pending.cursor;
        public int PublishedBuilds{get;private set;}
        public void Initialize(OrbisPlanetView source)
        {
            view=source;material=new Material(view.SurfaceMaterial){name="TerrainPerf1 local surface material"};
            active=Create("A");spare=Create("B");
        }
        Patch Create(string suffix)
        {
            var o=new GameObject("LocalTerrain_"+suffix,typeof(MeshFilter),typeof(MeshRenderer));o.transform.SetParent(transform,false);
            var p=new Patch{mesh=new Mesh{name="Local tangent patch "+suffix,indexFormat=IndexFormat.UInt32},display=o.GetComponent<MeshRenderer>()};
            p.mesh.MarkDynamic();o.GetComponent<MeshFilter>().sharedMesh=p.mesh;p.display.sharedMaterial=material;
            p.display.shadowCastingMode=ShadowCastingMode.On;p.display.enabled=false;return p;
        }
        public void Invalidate()
        {
            invalid=true;pending=null;hasActive=false;
            // A dormant spare must not retain an entire replaced world and its atmosphere arrays.
            if(active!=null)active.world=null;if(spare!=null)spare.world=null;Hide();
        }
        public void Tick(double clock,Vector3 sun)
        {
            Vector3 camera=view.transform.InverseTransformPoint(view.viewCamera.transform.position),focus=camera.normalized;
            float altitude=camera.magnitude-view.RadiusAt(focus);
            requestedVisible=view.world.settings.surfaceLOD&&altitude<.19f&&(view.layer==0||view.layer==1)&&!view.viewCamera.orthographic;
            if(!requestedVisible){Hide();pending=null;return;}
            int quality=Mathf.Clamp(view.world.settings.terrainQuality,0,3);
            float desired=Mathf.Clamp(altitude*3.5f,.015f,.24f);
            float size=Mathf.Min(.24f,.015f*Mathf.Pow(2,Mathf.Ceil(Mathf.Log(desired/.015f,2))));
            // Hysteresis around power-of-two bands; do not thrash at a zoom threshold.
            if(hasActive&&desired<=active.radius*1.08f&&desired>=active.radius*.42f)size=active.radius;
            bool stale=hasActive&&(active.world!=view.world||active.revision!=view.world.Revision);
            if(stale){hasActive=false;active.display.enabled=false;invalid=true;}
            if(pending!=null&&(pending.world!=view.world||pending.revision!=view.world.Revision||pending.quality!=quality||
                Vector3.Angle(pending.center,focus)*Mathf.Deg2Rad>pending.radius*.9f))pending=null;
            bool moved=!hasActive||Vector3.Angle(active.center,focus)*Mathf.Deg2Rad>size*.25f;
            if(pending==null&&(invalid||!hasActive||active.quality!=quality||Mathf.Abs(active.radius-size)>.0001f||moved))
            {Begin(focus,size,quality);invalid=false;}
            if(pending!=null)BuildSlice();
            if(!hasActive){Hide();return;}
            if(active.appearanceRevision!=view.AppearanceRevision)FillColors(active);
            view.BindSurfaceSettings(material,clock,sun);
            ApplyClip(view.SurfaceMaterial,0);ApplyClip(material,1);active.display.enabled=true;spare.display.enabled=false;
        }
        void ApplyClip(Material m,int patchLayer)
        {
            m.SetFloat("_PatchActive",1);m.SetVector("_PatchCenter",active.center);m.SetVector("_PatchRight",active.right);m.SetVector("_PatchUp",active.north);
            m.SetFloat("_PatchRadius",active.radius);m.SetFloat("_LocalGrid",0); // Fixed 4x4 Zone geometry is rendered by SelectionView.
            m.SetFloat("_GridSpacing",.0025f);m.SetFloat("_PatchLayer",patchLayer);
        }
        void Hide()
        {
            if(active!=null)active.display.enabled=false;if(spare!=null)spare.display.enabled=false;
            if(view&&view.SurfaceMaterial)view.SurfaceMaterial.SetFloat("_PatchActive",0);
        }
        void Begin(Vector3 center,float radius,int quality)
        {
            Patch p=spare;p.center=center;p.radius=radius;p.quality=quality;p.world=view.world;p.revision=view.world.Revision;p.cursor=0;
            Vector3 right=hasActive?Vector3.ProjectOnPlane(active.right,center):Vector3.zero;
            if(right.sqrMagnitude<.01f)right=Vector3.Cross(Mathf.Abs(center.y)>.93f?Vector3.forward:Vector3.up,center);
            p.right=right.normalized;p.north=Vector3.Cross(center,p.right).normalized;p.hint=view.world.grid.Nearest(center);
            int res=Resolutions[quality];
            if(p.vertices==null||p.resolution!=res)
            {
                p.resolution=res;int row=res+1,count=row*row;
                p.vertices=new Vector3[count];p.geometry=new Vector3[count];p.samples=new OrbisSphericalSampler.Sample[count];p.colors=new Color[count];
                p.climates=new List<Vector4>(new Vector4[count]);p.waters=new List<Vector4>(new Vector4[count]);p.indices=new int[res*res*6];
                int k=0;for(int y=0;y<res;y++)for(int x=0;x<res;x++)
                {int a=y*row+x;p.indices[k++]=a;p.indices[k++]=a+1;p.indices[k++]=a+row;p.indices[k++]=a+1;p.indices[k++]=a+row+1;p.indices[k++]=a+row;}
                p.mesh.Clear();p.mesh.vertices=p.vertices;p.mesh.triangles=p.indices;
            }
            pending=p;
        }
        void BuildSlice()
        {
            using(BuildMarker.Auto())
            {
                Patch p=pending;double deadline=Time.realtimeSinceStartupAsDouble+.0015;int end=Mathf.Min(p.cursor+768,p.vertices.Length),row=p.resolution+1;
                while(p.cursor<end)
                {
                    int i=p.cursor++,x=i%row,y=i/row;
                    float u=(2f*x/p.resolution-1)*p.radius,v=(2f*y/p.resolution-1)*p.radius;
                    Vector3 direction=(p.center+p.right*u+p.north*v).normalized;
                    var s=view.SurfaceSampler.Locate(direction,p.hint);p.samples[i]=s;p.hint=s.a;
                    float r=view.UncarvedRadius(direction,s,out float wet,out float depth,out float h);
                    p.geometry[i]=new Vector3(wet,depth,h);p.vertices[i]=direction*(wet>.5f?r:view.Rivers.Carve(direction,r));
                    if((p.cursor&31)==0&&Time.realtimeSinceStartupAsDouble>=deadline)break;
                }
                if(p.cursor==p.vertices.Length)Publish();
            }
        }
        void Publish()
        {
            using(PublishMarker.Auto())
            {
                Patch ready=pending;pending=null;
                ready.mesh.vertices=ready.vertices;ready.mesh.RecalculateNormals();ready.mesh.RecalculateBounds();
                FillColors(ready);active.display.enabled=false;spare=active;active=ready;hasActive=true;PublishedBuilds++;
            }
        }
        void FillColors(Patch p)
        {
            for(int i=0;i<p.samples.Length;i++)
            {view.SampleAppearanceCached(p.samples[i],p.geometry[i],out p.colors[i],out var climate,out var water);p.climates[i]=climate;p.waters[i]=water;}
            p.mesh.colors=p.colors;p.mesh.SetUVs(1,p.climates);p.mesh.SetUVs(2,p.waters);p.appearanceRevision=view.AppearanceRevision;
        }
        public void RefreshColors(){if(hasActive&&requestedVisible&&active.world==view.world&&active.revision==view.world.Revision)FillColors(active);}
        void OnDisable(){requestedVisible=false;pending=null;Hide();}
        void OnDestroy(){if(active!=null&&active.mesh)Destroy(active.mesh);if(spare!=null&&spare.mesh)Destroy(spare.mesh);if(material)Destroy(material);}
    }
}
