using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Audio;

namespace Orbis
{
    /// <summary>Camera-local rain/splashes and event-driven lightning. Pools never spawn
    /// independent storms. Geometry stays in planet space, including while the planet spins.</summary>
    public sealed class OrbisWeatherEffects:MonoBehaviour
    {
        public OrbisDemo demo;
        public AudioMixerGroup outputMixer;
        public float LocalRainIntensity {get;private set;}
        const int DropCount=900,SplashCount=160,BoltCount=4,ThunderCount=8;
        struct Drop {public bool alive;public Vector3 p,velocity;public float ground,age;}
        struct Splash {public bool alive;public Vector3 p;public float age;}
        sealed class Bolt {public LineRenderer main,branchA,branchB;public float age=10,power;public Vector3 position;}
        sealed class Thunder {public AudioSource source;public float delay=-1,volume;}
        Drop[] drops=new Drop[DropCount];Splash[] splashes=new Splash[SplashCount];
        readonly Bolt[] bolts=new Bolt[BoltCount];readonly Thunder[] thunder=new Thunder[ThunderCount];
        readonly Vector4[] flashData=new Vector4[BoltCount];
        readonly Vector3[] linePoints=new Vector3[24];
        Mesh rainMesh,splashMesh;MeshRenderer rainRenderer,splashRenderer;
        Vector3[] rainVertices,splashVertices;Color[] rainColors,splashColors;
        Material rainMaterial,splashMaterial,boltMaterial;AudioClip rainClip,thunderClip;AudioSource rainAudio;
        OrbisWorld lastWorld;System.Random rng=new System.Random(42019);
        float emission,flashCooldown,audioCooldown;int dropCursor,splashCursor,boltCursor,thunderCursor;bool initialized;
        Vector3 cameraLocal,cameraRight;
        float Random01(){return (float)rng.NextDouble();}
        Vector3 RandomTangent(Vector3 up)
        {Vector3 v=new Vector3(Random01()*2-1,Random01()*2-1,Random01()*2-1);return Vector3.ProjectOnPlane(v,up).normalized;}
        public void Initialize(OrbisDemo owner)
        {
            demo=owner;Shader shader=Resources.Load<Shader>("ORBIS/WeatherParticles");
            if(!shader){Debug.LogError("Missing ORBIS/WeatherParticles shader.");enabled=false;return;}
            rainMaterial=new Material(shader);rainMaterial.SetFloat("_Mode",0);
            splashMaterial=new Material(shader);splashMaterial.SetFloat("_Mode",1);
            boltMaterial=new Material(shader);boltMaterial.SetFloat("_Mode",2);
            rainMesh=CreateQuads("V4.2 rain droplets",DropCount,rainMaterial,out rainRenderer,out rainVertices,out rainColors);
            splashMesh=CreateQuads("V4.2 ground splashes",SplashCount,splashMaterial,out splashRenderer,out splashVertices,out splashColors);
            for(int i=0;i<BoltCount;i++)bolts[i]=new Bolt{main=Line("Lightning "+i),branchA=Line("Lightning branch A"),branchB=Line("Lightning branch B")};
            // Mono procedural clips: no downloaded audio assets, no import dependencies.
            rainClip=MakeAudio(false);thunderClip=MakeAudio(true);
            rainAudio=CreateAudio("Local rain",demo.PlanetCamera.transform,false);rainAudio.clip=rainClip;rainAudio.loop=true;
            for(int i=0;i<ThunderCount;i++){var source=CreateAudio("Delayed thunder "+i,demo.View.transform,true);source.clip=thunderClip;thunder[i]=new Thunder{source=source};}
            initialized=true;ResetEffects();
        }
        Mesh CreateQuads(string label,int count,Material material,out MeshRenderer renderer,out Vector3[] vertices,out Color[] colors)
        {
            var go=new GameObject(label,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(demo.View.transform,false);
            renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            var mesh=new Mesh{name=label};mesh.MarkDynamic();vertices=new Vector3[count*4];colors=new Color[count*4];
            var uv=new Vector2[count*4];var indices=new int[count*6];
            for(int i=0;i<count;i++)
            {
                int v=i*4,t=i*6;uv[v]=new Vector2(0,0);uv[v+1]=new Vector2(1,0);uv[v+2]=new Vector2(1,1);uv[v+3]=new Vector2(0,1);
                indices[t]=v;indices[t+1]=v+1;indices[t+2]=v+2;indices[t+3]=v;indices[t+4]=v+2;indices[t+5]=v+3;
            }
            mesh.vertices=vertices;mesh.colors=colors;mesh.uv=uv;mesh.triangles=indices;mesh.bounds=new Bounds(Vector3.zero,Vector3.one*2.5f);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;return mesh;
        }
        LineRenderer Line(string label)
        {
            var go=new GameObject(label,typeof(LineRenderer));go.transform.SetParent(demo.View.transform,false);
            var line=go.GetComponent<LineRenderer>();line.sharedMaterial=boltMaterial;line.useWorldSpace=false;
            line.positionCount=linePoints.Length;line.widthMultiplier=.00018f;line.numCapVertices=2;line.shadowCastingMode=ShadowCastingMode.Off;
            line.enabled=false;return line;
        }
        AudioSource CreateAudio(string label,Transform parent,bool spatial)
        {
            var go=new GameObject(label,typeof(AudioSource));go.transform.SetParent(parent,false);
            var a=go.GetComponent<AudioSource>();a.playOnAwake=false;a.spatialBlend=spatial?1:0;a.dopplerLevel=0;
            a.minDistance=.005f;a.maxDistance=.15f;a.rolloffMode=AudioRolloffMode.Linear;a.outputAudioMixerGroup=outputMixer;return a;
        }
        AudioClip MakeAudio(bool thunderSound)
        {
            const int frequency=24000;int count=frequency*(thunderSound?6:3);var pcm=new float[count];
            var random=new System.Random(thunderSound?642:421);double low=0;
            for(int i=0;i<count;i++)
            {
                double t=i/(double)frequency,n=random.NextDouble()*2-1;
                low+= (n-low)*(thunderSound?.023:.22);
                double value;
                if(thunderSound)
                {
                    double envelope=(1-Math.Exp(-t*75))*Math.Exp(-t*.72);
                    double rumbles=.55+.30*Math.Sin(t*13)+.15*Math.Sin(t*29);
                    value=(low*4.5+Math.Sin(t*2*Math.PI*54)*.13)*envelope*rumbles;
                }
                else value=(n-low)*.14+low*.3;
                // Fade both ends so the looping rain bed and thunder have no click.
                double fade=Math.Min(1,Math.Min(t/.03,(count-i)/(frequency*.04)));
                pcm[i]=(float)Math.Max(-.75,Math.Min(.75,value*fade));
            }
            var clip=AudioClip.Create(thunderSound?"ORBIS synthesized thunder":"ORBIS synthesized rainfall",count,1,frequency,false);
            clip.SetData(pcm,0);return clip;
        }
        void Update()
        {
            if(!initialized||!demo||demo.World==null)return;
            if(lastWorld!=demo.World){ResetEffects();lastWorld=demo.World;}
            var settings=demo.World.settings;
            float dt=demo.Paused||!settings.dynamicWeather?0:Mathf.Min(Time.unscaledDeltaTime,.05f);
            bool map=demo.MapMode,natural=demo.View.layer<=1;
            cameraLocal=demo.View.transform.InverseTransformPoint(demo.PlanetCamera.transform.position);
            cameraRight=demo.View.transform.InverseTransformDirection(demo.PlanetCamera.transform.right);
            var core=demo.World.Weather.Atmosphere;
            float realDt=Mathf.Min(Time.unscaledDeltaTime,.1f);
            flashCooldown=Mathf.Max(0,flashCooldown-realDt);audioCooldown=Mathf.Max(0,audioCooldown-realDt);
            bool chosen=false;AtmosphereLightning candidate=new AtmosphereLightning();float best=-1;
            while(core.Lightning.Count>0)
            {
                var strike=core.Lightning.Dequeue();
                // Consume obsolete/off-screen events, never replay them in a catch-up burst.
                if(!settings.lightningEffects||!natural||map||demo.Paused||flashCooldown>0)continue;
                Vector3 up=demo.World.grid.points[strike.Cell];
                float facing=Vector3.Dot(cameraLocal.normalized,up);
                float horizon=1f/Mathf.Max(1.001f,cameraLocal.magnitude)-.20f;
                if(facing<horizon)continue;
                float score=facing+(float)strike.Strength*.05f;
                if(score>best){best=score;candidate=strike;chosen=true;}
            }
            if(chosen){StartLightning(candidate,settings.weatherAudio);flashCooldown=.70f+Random01()*.65f;}
            UpdateBolts(realDt,map||!natural||!settings.lightningEffects||demo.Paused);
            if(map||!natural||!settings.rainEffects)
            {
                LocalRainIntensity=0;rainRenderer.enabled=false;splashRenderer.enabled=false;
                Array.Clear(drops,0,drops.Length);Array.Clear(splashes,0,splashes.Length);emission=0;
                if(map||!natural)ClearThunder();
            }
            else UpdateRain(dt);
            if(rainAudio)
            {
                rainAudio.volume=settings.weatherAudio&&!demo.Paused?Mathf.Clamp01(LocalRainIntensity)*.30f:0;
                if(rainAudio.volume>.002&&!rainAudio.isPlaying)rainAudio.Play();
                if(rainAudio.volume<=.002&&rainAudio.isPlaying)rainAudio.Stop();
            }
            for(int i=0;i<ThunderCount;i++)
            {
                var sound=thunder[i];
                if(!settings.weatherAudio){sound.source.Stop();sound.delay=-1;continue;}
                if(demo.Paused){if(sound.source.isPlaying)sound.source.Pause();continue;}
                sound.source.UnPause();
                if(sound.delay<0)continue;sound.delay-=dt;
                if(sound.delay<=0){sound.source.volume=sound.volume;sound.source.PlayScheduled(AudioSettings.dspTime+.02);sound.delay=-1;}
            }
            rainMaterial.SetVector("_Sun",demo.VisualSun);splashMaterial.SetVector("_Sun",demo.VisualSun);
        }
        void UpdateRain(float dt)
        {
            Vector3 up=cameraLocal.normalized;float radius=demo.View.RadiusAt(up,out var sample);
            float height=(cameraLocal.magnitude-radius)/OrbisWeatherField.HeightScale;
            int cell=sample.a;var core=demo.World.Weather.Atmosphere;
            float rainfall=(float)(core.RainRate[sample.a]*sample.weights.x+core.RainRate[sample.b]*sample.weights.y+core.RainRate[sample.c]*sample.weights.z);
            int layer=0;while(layer<11&&height>OrbisAtmosphereCore.Edges[layer+1])layer++;
            int offset=layer*core.Count;float airborne=(float)(core.Water[3][offset+sample.a]*sample.weights.x+core.Water[3][offset+sample.b]*sample.weights.y+core.Water[3][offset+sample.c]*sample.weights.z);
            // Near-ground rain follows the ground flux. Higher up it follows the rain-water layer.
            float intensity=height<500?1-Mathf.Exp(-rainfall/8):1-Mathf.Exp(-airborne*6);
            if(height<0||height>14500)intensity=0;
            LocalRainIntensity=Mathf.MoveTowards(LocalRainIntensity,intensity,dt*.65f);
            rainRenderer.enabled=LocalRainIntensity>.002;splashRenderer.enabled=rainRenderer.enabled&&height<2300;
            emission+=LocalRainIntensity*550*dt;
            int birth=Mathf.Min(80,(int)emission);emission-=birth;
            Vector3 look=demo.View.transform.InverseTransformDirection(demo.PlanetCamera.transform.forward);
            for(int k=0;k<birth;k++)
            {
                int index=dropCursor++%DropCount;Vector3 offsetDisk=RandomTangent(up)*Mathf.Sqrt(Random01())*.009f;
                Vector3 p=cameraLocal+look*.006f+up*(.004f+Random01()*.009f)+offsetDisk;
                Vector3 radial=p.normalized;float ground=demo.View.RadiusAt(radial,out var s);
                // Query the SAME field at the actual spawn location. No rain outside a rain cell.
                float local=(float)(core.RainRate[s.a]*s.weights.x+core.RainRate[s.b]*s.weights.y+core.RainRate[s.c]*s.weights.z);
                if(height<500&&local<.015f)continue;
                Vector3 wind=OrbisWeather.ToUnity(core.Wind[s.a]/.014);
                drops[index]=new Drop{alive=true,p=p,ground=ground,velocity=-radial*(.021f+Random01()*.012f)+wind*.0012f};
            }
            float sun=Mathf.Clamp01(Vector3.Dot(up,demo.VisualSun));Color color=new Color(.42f+.35f*sun,.55f+.30f*sun,.70f+.22f*sun,.36f*LocalRainIntensity);
            for(int i=0;i<DropCount;i++)
            {
                var d=drops[i];int v=i*4;
                if(d.alive)
                {
                    d.p+=d.velocity*dt;d.age+=dt;
                    if(d.p.magnitude<=d.ground)
                    {
                        Vector3 ground=d.p.normalized*d.ground;
                        if(Vector3.Distance(cameraLocal,ground)<.022f)
                            splashes[splashCursor++%SplashCount]=new Splash{alive=true,p=ground};
                        d.alive=false;
                    }
                    if(d.age>1.8f||Vector3.Distance(d.p,cameraLocal)>.055f)d.alive=false;
                }
                if(d.alive)
                {
                    Vector3 halfWidth=cameraRight*.000022f,trail=d.velocity.normalized*.00075f;
                    rainVertices[v]=d.p-halfWidth;rainVertices[v+1]=d.p+halfWidth;rainVertices[v+2]=d.p+halfWidth-trail;rainVertices[v+3]=d.p-halfWidth-trail;
                    for(int j=0;j<4;j++)rainColors[v+j]=color;
                }
                else for(int j=0;j<4;j++){rainVertices[v+j]=Vector3.zero;rainColors[v+j]=Color.clear;}
                drops[i]=d;
            }
            for(int i=0;i<SplashCount;i++)
            {
                var s=splashes[i];int v=i*4;s.age+=dt;if(s.age>.45f)s.alive=false;
                if(s.alive)
                {
                    Vector3 n=s.p.normalized,e=Vector3.Cross(n,Mathf.Abs(n.y)<.9f?Vector3.up:Vector3.right).normalized;
                    Vector3 north=Vector3.Cross(n,e);float size=.00008f+s.age*.0005f;Vector3 center=s.p+n*.000035f;
                    splashVertices[v]=center-e*size-north*size;splashVertices[v+1]=center+e*size-north*size;
                    splashVertices[v+2]=center+e*size+north*size;splashVertices[v+3]=center-e*size+north*size;
                    Color c=new Color(color.r,color.g,color.b,(1-s.age/.45f)*.42f);
                    for(int j=0;j<4;j++)splashColors[v+j]=c;
                }
                else for(int j=0;j<4;j++){splashVertices[v+j]=Vector3.zero;splashColors[v+j]=Color.clear;}
                splashes[i]=s;
            }
            rainMesh.vertices=rainVertices;rainMesh.colors=rainColors;splashMesh.vertices=splashVertices;splashMesh.colors=splashColors;
        }
        void StartLightning(AtmosphereLightning strike,bool allowAudio)
        {
            Vector3 up=demo.World.grid.points[strike.Cell];float ground=demo.View.RadiusAt(up,out var sample);
            Vector3 origin=up*(ground+(float)strike.Height*OrbisWeatherField.HeightScale);
            Vector3 end=strike.CloudToGround?up*(ground+.00004f):origin+RandomTangent(up)*.026f-up*.012f;
            Bolt bolt=bolts[boltCursor++%BoltCount];bolt.age=0;bolt.power=(float)strike.Strength;bolt.position=origin;
            ShapeBolt(bolt.main,origin,end,.004f);
            ShapeBolt(bolt.branchA,Vector3.Lerp(origin,end,.38f),Vector3.Lerp(origin,end,.76f)+RandomTangent(up)*.012f,.002f);
            ShapeBolt(bolt.branchB,Vector3.Lerp(origin,end,.58f),Vector3.Lerp(origin,end,.9f)+RandomTangent(up)*.008f,.0015f);
            if(!allowAudio||audioCooldown>0)return;
            float distance=Vector3.Distance(cameraLocal,origin)/OrbisWeatherField.HeightScale;
            float cameraAltitude=(cameraLocal.magnitude-demo.View.RadiusAt(cameraLocal.normalized,out var cs))/OrbisWeatherField.HeightScale;
            if(distance>25000||cameraAltitude>5000)return;
            audioCooldown=8+Random01()*6;
            Thunder sound=thunder[thunderCursor++%ThunderCount];sound.source.Stop();sound.source.transform.localPosition=origin;
            float speed=331.3f+.606f*demo.World.tiles[strike.Cell].Temperature;
            sound.delay=distance/Mathf.Clamp(speed,290,365);sound.volume=Mathf.Lerp(.65f,.16f,distance/25000);
        }
        void ShapeBolt(LineRenderer line,Vector3 a,Vector3 b,float spread)
        {
            Vector3 up=a.normalized;
            for(int i=0;i<linePoints.Length;i++)
            {float t=i/(float)(linePoints.Length-1);linePoints[i]=Vector3.Lerp(a,b,t)+RandomTangent(up)*(Random01()*spread*Mathf.Sin(t*Mathf.PI));}
            line.SetPositions(linePoints);
        }
        void UpdateBolts(float dt,bool hidden)
        {
            for(int i=0;i<BoltCount;i++)
            {
                Bolt bolt=bolts[i];bolt.age+=dt;float t=bolt.age;
                float pulse=bolt.power*(Mathf.Exp(-t*24)+.58f*Mathf.Exp(-Mathf.Pow((t-.085f)*50,2))+.20f*Mathf.Exp(-Mathf.Pow((t-.19f)*38,2)));
                if(hidden||!demo.World.settings.lightningEffects||demo.View.layer>1)pulse=0;
                flashData[i]=new Vector4(bolt.position.x,bolt.position.y,bolt.position.z,pulse);
                bool visible=!hidden&&t<.4f;bolt.main.enabled=bolt.branchA.enabled=bolt.branchB.enabled=visible;
                Color color=new Color(3.1f,3.8f,5.2f,Mathf.Clamp01(pulse));
                bolt.main.startColor=bolt.main.endColor=color;bolt.branchA.startColor=bolt.branchA.endColor=color;bolt.branchB.startColor=bolt.branchB.endColor=color;
            }
            Shader.SetGlobalVectorArray("_OrbisLightning",flashData);
        }
        void ClearThunder()
        {for(int i=0;i<ThunderCount;i++)if(thunder[i]!=null){thunder[i].delay=-1;thunder[i].source.Stop();}}
        void ResetEffects()
        {
            Array.Clear(drops,0,drops.Length);Array.Clear(splashes,0,splashes.Length);Array.Clear(flashData,0,flashData.Length);
            for(int i=0;i<BoltCount;i++)if(bolts[i]!=null){bolts[i].age=10;bolts[i].main.enabled=bolts[i].branchA.enabled=bolts[i].branchB.enabled=false;}
            if(rainRenderer)rainRenderer.enabled=false;if(splashRenderer)splashRenderer.enabled=false;
            Shader.SetGlobalVectorArray("_OrbisLightning",flashData);flashCooldown=2;audioCooldown=0;emission=0;LocalRainIntensity=0;ClearThunder();if(rainAudio)rainAudio.Stop();
        }
        void OnDisable(){if(initialized)ResetEffects();}
        void OnDestroy()
        {
            if(initialized)ResetEffects();
            if(rainMesh)Destroy(rainMesh);if(splashMesh)Destroy(splashMesh);if(rainMaterial)Destroy(rainMaterial);if(splashMaterial)Destroy(splashMaterial);if(boltMaterial)Destroy(boltMaterial);
            if(rainClip)Destroy(rainClip);if(thunderClip)Destroy(thunderClip);
            if(rainRenderer)Destroy(rainRenderer.gameObject);if(splashRenderer)Destroy(splashRenderer.gameObject);
            if(rainAudio)Destroy(rainAudio.gameObject);
            foreach(var b in bolts)if(b!=null){if(b.main)Destroy(b.main.gameObject);if(b.branchA)Destroy(b.branchA.gameObject);if(b.branchB)Destroy(b.branchB.gameObject);}
            foreach(var t in thunder)if(t!=null&&t.source)Destroy(t.source.gameObject);
        }
    }
}
