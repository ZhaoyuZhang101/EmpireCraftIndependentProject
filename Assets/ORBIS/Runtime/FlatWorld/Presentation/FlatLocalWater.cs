using System;
using System.Threading.Tasks;
using UnityEngine;
namespace Orbis.Flat
{
    // One bounded 64x64 patch, 10Hz worker tasks, main-thread texture upload.
    // No GameObjects per river/tile, no GPU readback, no second world simulation.
    public sealed class FlatLocalWater : MonoBehaviour
    {
        const int Resolution=64,Span=16;
        WorldData world;Material material;Texture2D texture,previousTexture;
        float presentationBlend=1;bool published,hasPrevious;
        FlatShallowWater solver;Task<Frame> pending;
        int startX=-1000,startY=-1000;float age;bool stopped,ripple;
        byte[] pixelBuffer=new byte[Resolution*Resolution*4];
        double clock;public string Status { get; private set; }="局部 SWE：缩放至每 Tile ≥8 像素时启用";
        sealed class Frame { public FlatShallowWater Solver;public byte[] RGBA;public int X,Y;public double MassError; }
        public void Initialize(WorldData source,Material target)
        {
            world=source;material=target;
            texture=new Texture2D(Resolution,Resolution,TextureFormat.RGBA32,false,true){name="SWE local normals and foam",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=0};
            previousTexture=new Texture2D(Resolution,Resolution,TextureFormat.RGBA32,false,true){name="SWE previous world patch",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=0};
            material.SetTexture("_SWEState",texture);material.SetTexture("_SWEPreviousState",previousTexture);
            material.SetFloat("_SWEReady",0);material.SetFloat("_SWEPreviousReady",0);material.SetFloat("_SWEBlend",1);
        }
        public void RequestRipple(){ripple=true;}
        public void Tick(float dt,float focusX,float focusY,bool visible,bool animate)
        {
            if(!material||world==null)return;
            if(animate)clock+=Math.Min(.1,dt);
            // Double presentation clock; no global simulation time or topology writes.
            material.SetFloat("_WaterClock",(float)clock);
            bool active=visible&&!stopped;
            presentationBlend=Mathf.Min(1,presentationBlend+Mathf.Max(0,dt)/.22f);
            int x=FMath.Wrap(Mathf.FloorToInt((focusX-Span*.5f)/4)*4,world.Width);
            int y=Mathf.Clamp(Mathf.FloorToInt((focusY-Span*.5f)/4)*4,0,world.Height-Span);
            if(pending!=null&&pending.IsCompleted)
            {
                var task=pending;pending=null;
                try
                {
                    Frame f=task.GetAwaiter().GetResult();
                    bool moved=!published||f.X!=startX||f.Y!=startY;
                    if(moved)
                    {
                        // Keep the old world-aligned patch visible while its replacement arrives.
                        var swap=previousTexture;previousTexture=texture;texture=swap;
                        hasPrevious=published;presentationBlend=0;
                        material.SetTexture("_SWEState",texture);material.SetTexture("_SWEPreviousState",previousTexture);
                        material.SetVector("_SWEPreviousRect",new Vector4(startX,startY,Span,Span));
                    }
                    solver=f.Solver;startX=f.X;startY=f.Y;published=true;
                    texture.LoadRawTextureData(f.RGBA);texture.Apply(false,false);
                    material.SetVector("_SWERect",new Vector4(startX,startY,Span,Span));
                    Status="局部 SWE 64×64 / 10Hz；相对水量误差 "+f.MassError.ToString("E2")+"（可视波动，不改全球水量）";
                }
                catch(Exception e){stopped=true;active=false;Status="局部 SWE 已降级为流向波纹："+e.Message;Debug.LogWarning(Status);}
            }
            // A camera move does not invalidate data already published at these world coordinates.
            material.SetFloat("_SWEReady",active&&published?1:0);
            material.SetFloat("_SWEPreviousReady",active&&hasPrevious?1:0);
            material.SetFloat("_SWEBlend",presentationBlend);
            if(!active){if(!stopped)Status=visible?"局部 SWE 暂停":"局部 SWE：关闭或视图太远；使用低成本流向波纹";return;}
            if(pending!=null||!animate||presentationBlend<1)return;
            age+=dt;bool changed=x!=startX||y!=startY||solver==null;
            if(age<.1f&&!changed&&!ripple)return;
            float step=Mathf.Min(.12f,Mathf.Max(.025f,age));age=0;
            bool disturbance=ripple;ripple=false;
            var current=changed?null:solver;WorldData source=world;
            pending=Task.Run(()=>Solve(source,current,x,y,step,disturbance,pixelBuffer));
        }
        static Frame Solve(WorldData world,FlatShallowWater current,int x0,int y0,float dt,bool ripple,byte[] bytes)
        {
            if(current==null)
            {
                float[] depths=new float[Resolution*Resolution];
                for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++)
                {
                    float depth;float mask=FlatWaterGeometry.Coverage(world,x0+(x+.5f)*Span/Resolution,y0+(y+.5f)*Span/Resolution,out depth);
                    depths[y*Resolution+x]=mask>.52f?FMath.Clamp(depth,.05f,12):0;
                }
                // Local display scale only: 100m per displayed tile, 25m per subcell.
                // The synthetic global climate map does not claim this geodetic cell size.
                current=new FlatShallowWater(Resolution,Resolution,25,depths);
            }
            if(ripple)
            {
                int nearest=-1,best=int.MaxValue;
                for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++)
                {int i=y*Resolution+x,d=(x-32)*(x-32)+(y-32)*(y-32);if(current.IsWet(i)&&d<best){nearest=i;best=d;}}
                if(nearest>=0)current.Ripple(nearest%Resolution,nearest/Resolution,.055);
            }
            current.Advance(dt);

            for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++)
            {
                int i=y*Resolution+x,a=y*Resolution+Math.Max(0,x-1),b=y*Resolution+Math.Min(Resolution-1,x+1);
                int c=Math.Max(0,y-1)*Resolution+x,d=Math.Min(Resolution-1,y+1)*Resolution+x;
                double center=current.Depth[i]+current.Bed[i];
                double ha=current.IsWet(a)?current.Depth[a]+current.Bed[a]:center,hb=current.IsWet(b)?current.Depth[b]+current.Bed[b]:center;
                double hc=current.IsWet(c)?current.Depth[c]+current.Bed[c]:center,hd=current.IsWet(d)?current.Depth[d]+current.Bed[d]:center;
                // Display magnification for shallow-wave normals; not a terrain-height write.
                double gx=(hb-ha)/(2*current.Dx)*120,gy=(hd-hc)/(2*current.Dx)*120;
                bytes[i*4]=(byte)Math.Round(Math.Max(0,Math.Min(255,128+gx*127)));
                bytes[i*4+1]=(byte)Math.Round(Math.Max(0,Math.Min(255,128+gy*127)));
                bytes[i*4+2]=(byte)Math.Round(Math.Max(0,Math.Min(255,current.Foam[i]*255)));
                bytes[i*4+3]=(byte)(current.IsWet(i)?255:0);
            }
            return new Frame{Solver=current,RGBA=bytes,X=x0,Y=y0,MassError=current.RelativeMassError()};
        }
        void OnDisable(){if(material){material.SetFloat("_SWEReady",0);material.SetFloat("_SWEPreviousReady",0);}}
        void OnDestroy()
        {
            // Workers hold managed snapshots only. Observe late exceptions; never touch destroyed Unity objects.
            if(pending!=null)pending.ContinueWith(t=>{if(t.IsFaulted){var observed=t.Exception;}},TaskScheduler.Default);
            if(texture)Destroy(texture);if(previousTexture)Destroy(previousTexture);
        }
    }
}
