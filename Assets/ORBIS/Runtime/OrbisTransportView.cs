using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis
{
    /// <summary>Advected trajectories of the simulation's tangent velocity field, not a
    /// Shadertoy source port. Integrates on the sphere; the atlas only projects the history.</summary>
    public sealed class OrbisTransportView:MonoBehaviour
    {
        public OrbisDemo demo;
        const int Count=640,Tail=32;
        const float Step=.18f;
        sealed class Tracer
        {
            public Vector3 p;public readonly Vector3[] history=new Vector3[Tail];
            public float age,life;public int hint,kind;public bool valid;
        }
        readonly Tracer[] tracers=new Tracer[Count];
        readonly List<Vector3> vertices=new List<Vector3>(Count*Tail*4);
        readonly List<Color> colors=new List<Color>(Count*Tail*4);
        readonly List<int> indices=new List<int>(Count*Tail*6);
        Mesh mesh;Material material;MeshRenderer display;RandomStream random;
        OrbisWorld world;OrbisSphericalSampler sampler;int mask=-1;
        double lastClock;float accumulator;
        void Start()
        {
            Shader shader=Resources.Load<Shader>("ORBIS/Transport");
            if(!shader||!shader.isSupported){Debug.LogWarning("ORBIS transport shader unavailable.");enabled=false;return;}
            var go=new GameObject("V4.1 wind and ocean trajectories",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
            mesh=new Mesh{name="V4.1 spherical RK2 tracers",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh=mesh;display=go.GetComponent<MeshRenderer>();
            material=new Material(shader);display.sharedMaterial=material;display.shadowCastingMode=ShadowCastingMode.Off;
            for(int i=0;i<Count;i++)tracers[i]=new Tracer();
        }
        int RequestedMask()
        {
            var s=demo.World.settings;
            if(s.transportView>0)return 1<<(s.transportView-1);
            int result=(s.showWind||demo.View.layer==11)?1:0;
            if(s.showCurrents||demo.View.layer==12)result|=4;
            return result;
        }
        void ResetField(int requested)
        {
            world=demo.World;mask=requested;sampler=world.Weather.sampler;
            random=new RandomStream(RandomStream.Hash(world.seed+"|v4.1-transport"));
            var kinds=new List<int>();for(int i=1;i<=3;i++)if((mask&(1<<(i-1)))!=0)kinds.Add(i);
            for(int i=0;i<Count;i++)
            {
                tracers[i].kind=kinds.Count==0?1:kinds[i%kinds.Count];
                if(kinds.Count>0){Respawn(tracers[i]);tracers[i].age=.5f;}
            }
            lastClock=world.VisualTime;accumulator=0;
        }
        Vector3 Velocity(Vector3 p,ref int hint,int kind,out float humidity,out bool ocean)
        {
            var s=sampler.Locate(p,hint);Vector3 w=s.weights;
            hint=w.x>=w.y&&w.x>=w.z?s.a:w.y>=w.z?s.b:s.c;
            Tile a=world.tiles[s.a],b=world.tiles[s.b],c=world.tiles[s.c];
            humidity=a.vapor*w.x+b.vapor*w.y+c.vapor*w.z;
            ocean=world.tiles[hint].h<=0&&a.h*w.x+b.h*w.y+c.h*w.z<=0;
            Vector3 v=kind==3?a.current*w.x+b.current*w.y+c.current*w.z:a.cloudWind*w.x+b.cloudWind*w.y+c.cloudWind*w.z;
            return Vector3.ProjectOnPlane(v,p);
        }
        static Vector3 AdvanceSphere(Vector3 p,Vector3 v,float dt)
        {
            v=Vector3.ProjectOnPlane(v,p);float speed=v.magnitude;
            if(speed<1e-8f)return p;
            float angle=Mathf.Clamp(speed*dt,-.025f,.025f);
            return (p*Mathf.Cos(angle)+v/speed*Mathf.Sin(angle)).normalized;
        }
        Vector3 Integrate(Vector3 p,ref int hint,int kind,float dt,out bool ocean)
        {
            Vector3 v0=Velocity(p,ref hint,kind,out _,out _)*OrbisWeather.AngularSpeed;
            Vector3 mid=AdvanceSphere(p,v0,dt*.5f);
            Vector3 vm=Velocity(mid,ref hint,kind,out _,out _)*OrbisWeather.AngularSpeed;
            Vector3 candidate=AdvanceSphere(p,vm,dt);
            // Validate the NEW location, not the old hint. Conservative at a mixed coast.
            Velocity(candidate,ref hint,kind,out _,out ocean);
            return candidate;
        }
        void Respawn(Tracer t)
        {
            t.valid=false;
            if(t.kind==3&&world.LandCount>=world.tiles.Length)return;
            for(int attempt=0;attempt<64;attempt++)
            {
                t.p=random.Unit();t.hint=0;
                Velocity(t.p,ref t.hint,t.kind,out _,out bool ocean);
                if(t.kind!=3||ocean){t.valid=true;break;}
            }
            if(!t.valid)return;
            t.age=0;t.life=random.Range(12,32);t.history[0]=t.p;
            Vector3 p=t.p;int hint=t.hint;
            for(int j=1;j<Tail;j++)
            {
                Vector3 candidate=Integrate(p,ref hint,t.kind,-Step,out bool ocean);
                if(t.kind!=3||ocean)p=candidate;
                t.history[j]=p;
            }
        }
        void LateUpdate()
        {
            if(!mesh||demo==null||demo.World?.Weather==null)return;
            int requested=RequestedMask();
            if(world!=demo.World||mask!=requested)ResetField(requested);
            display.enabled=mask!=0;
            if(!display.enabled)return;
            double now=world.VisualTime;
            float dt=(float)Math.Max(0,Math.Min(.5,now-lastClock));lastClock=now;
            if(!demo.Paused)accumulator+=dt;
            while(accumulator>=Step)
            {
                accumulator-=Step;
                foreach(var t in tracers)
                {
                    if(!t.valid){Respawn(t);continue;}
                    t.age+=Step;if(t.age>t.life){Respawn(t);continue;}
                    Vector3 candidate=Integrate(t.p,ref t.hint,t.kind,Step,out bool ocean);
                    if(t.kind==3&&!ocean){Respawn(t);continue;}
                    t.p=candidate;
                    for(int j=Tail-1;j>0;j--)t.history[j]=t.history[j-1];t.history[0]=t.p;
                }
            }
            vertices.Clear();colors.Clear();indices.Clear();
            float width=demo.MapMode?.0028f:Mathf.Clamp(demo.Altitude*.00085f,.00008f,.0022f);
            foreach(var t in tracers)
            {
                if(!t.valid)continue;
                Vector3 wind=Velocity(t.p,ref t.hint,t.kind,out float moisture,out _);
                float flux=moisture*wind.magnitude;
                Color tint=t.kind==3?new Color(.12f,.78f,1):t.kind==2?
                    Color.Lerp(new Color(1,.63f,.15f),new Color(.12f,1,.78f),Mathf.Clamp01(flux/1.5f)):new Color(.77f,.94f,1);
                float lifeFade=Mathf.Clamp01(t.age/.45f)*Mathf.Clamp01((t.life-t.age)/1.2f);
                for(int j=0;j<Tail-1;j++)
                {
                    Vector3 a=Place(t.history[j],t.kind),b=Place(t.history[j+1],t.kind);
                    if((a-b).sqrMagnitude<1e-12f||(demo.MapMode&&Mathf.Abs(a.x-b.x)>Mathf.PI))continue;
                    Vector3 face=demo.MapMode?Vector3.forward:(a+b).normalized;
                    Vector3 side=Vector3.Cross(face,b-a).normalized*width;
                    int n=vertices.Count;vertices.Add(a-side);vertices.Add(a+side);vertices.Add(b-side);vertices.Add(b+side);
                    tint.a=Mathf.Pow(1f-(float)j/Tail,1.4f)*.95f*lifeFade*(t.kind==2?Mathf.Lerp(.15f,1,Mathf.Clamp01(flux)):1);
                    for(int k=0;k<4;k++)colors.Add(tint);
                    indices.Add(n);indices.Add(n+2);indices.Add(n+1);indices.Add(n+1);indices.Add(n+2);indices.Add(n+3);
                }
            }
            display.transform.localRotation=Quaternion.identity; // parent owns planetary spin
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
        }
        Vector3 Place(Vector3 p,int kind)=>demo.MapMode?OrbisMapView.Project(p)+Vector3.forward*.025f:p*(kind==3?1.002f:1.135f);
        void OnDestroy(){if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}
