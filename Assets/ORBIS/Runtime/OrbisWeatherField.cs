using System;
using UnityEngine;
namespace Orbis
{
    /// <summary>One physical state feeds globe/map clouds, shadows, rain and wet ground.
    /// Layers are above the sampled terrain; 3D textures contain no noise-generated coverage.</summary>
    public sealed class OrbisWeatherField:IDisposable
    {
        const int Width=256,Height=128,LayerWidth=128,LayerHeight=64;
        public const float HeightScale=OrbisAtmosphereProfile.CloudMetresToRadius;
        public readonly Texture2D weather,wind,ground,effects,hpHighDetail;
        public readonly Texture3D noise,detailNoise,layers,layerWind;
        readonly bool ownsNoise;
        int groundRevision=-1;OrbisWorld groundWorld;
        public OrbisPlanetView view;
        readonly Color[] grounds=new Color[Width*Height],states=new Color[Width*Height],winds=new Color[Width*Height],effectPixels=new Color[Width*Height];
        readonly Color[] layerPixels=new Color[LayerWidth*LayerHeight*OrbisAtmosphereCore.Layers],layerWindPixels=new Color[LayerWidth*LayerHeight*OrbisAtmosphereCore.Layers];
        readonly OrbisSphericalSampler.Sample[] samples=new OrbisSphericalSampler.Sample[Width*Height],layerSamples=new OrbisSphericalSampler.Sample[LayerWidth*LayerHeight];
        public float SampleClock {get;private set;}
        public float CloudShellTop {get;private set;}=OrbisAtmosphereProfile.CloudTop;
        public double VisualClock {get;set;}
        static TextureFormat Format=>SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf)?TextureFormat.RGBAHalf:TextureFormat.RGBA32;
        static Texture2D Field(string label)=>new Texture2D(Width,Height,Format,false,true)
            {name=label,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        static Texture3D Volume(string label)=>new Texture3D(LayerWidth,LayerHeight,OrbisAtmosphereCore.Layers,Format,false)
            {name=label,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,wrapModeW=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        static Vector3 Direction(int index,int width,int height)
        {
            float lat=((index/width+.5f)/height-.5f)*Mathf.PI,lon=((index%width+.5f)/width-.5f)*Mathf.PI*2;
            return new Vector3(Mathf.Sin(lon)*Mathf.Cos(lat),Mathf.Sin(lat),Mathf.Cos(lon)*Mathf.Cos(lat));
        }
        public OrbisWeatherField(SphereGrid grid)
        {
            ground=Field("V4.2 terrain radius / snow / water mask / wetness");
            weather=Field("V4.2 column coverage / RH / convection / path");wind=Field("V4.2 lower-layer tangent wind");
            effects=Field("V4.2 rain intensity / wetness / storm stage / snowfall");
            layers=Volume("V4.2 cloud liquid / cloud ice / fraction / vertical velocity");
            layerWind=Volume("V4.2 height-dependent wind");
            var sampler=new OrbisSphericalSampler(grid);int hint=0;
            for(int i=0;i<samples.Length;i++){samples[i]=sampler.Locate(Direction(i,Width,Height),hint);hint=samples[i].a;}
            for(int i=0;i<layerSamples.Length;i++){layerSamples[i]=sampler.Locate(Direction(i,LayerWidth,LayerHeight),hint);hint=layerSamples[i].a;}
            noise=Resources.Load<Texture3D>("ORBIS/CloudNoiseExternal");ownsNoise=!noise;if(ownsNoise)noise=OrbisCloudNoise.Create();
            detailNoise=OrbisCloudNoise.CreateDetail();
            hpHighDetail=OrbisCloudNoise.CreateHighDetail();
        }
        static float Read(double[] data,int offset,OrbisSphericalSampler.Sample s)
        {return (float)(data[offset+s.a]*s.weights.x+data[offset+s.b]*s.weights.y+data[offset+s.c]*s.weights.z);}
        double lastAtmosphereClock=double.NaN;float lastCalendarDay=float.NaN;
        public void Update(OrbisWorld world,bool force=false)
        {
            if(!force&&groundWorld==world&&groundRevision==world.Revision&&lastAtmosphereClock==world.AtmosphereClock&&lastCalendarDay==world.day)return;
            SampleClock=(float)(world.AtmosphereClock%65536.0);
            bool rebuildGround=groundRevision!=world.Revision||groundWorld!=world;
            if(rebuildGround)CloudShellTop=OrbisAtmosphereProfile.CloudTop;
            for(int i=0;i<samples.Length;i++)
            {
                var s=samples[i];Vector3 w=s.weights;Tile a=world.tiles[s.a],b=world.tiles[s.b],c=world.tiles[s.c];
                if(rebuildGround)
                {
                    float radius=1,water=0,dep=0,h=0;
                    if(view)radius=view.UncarvedRadius(Direction(i,Width,Height),s,out water,out dep,out h);
                    grounds[i]=new Color(radius,0,water,0);
                    CloudShellTop=Mathf.Max(CloudShellTop,radius+13500*HeightScale+.001f);
                }
                // Snow and wetness must update even when geometry Revision does not change.
                float wet=a.surfaceWetness*w.x+b.surfaceWetness*w.y+c.surfaceWetness*w.z;
                grounds[i].g=OrbisPlanetView.VisibleSnow(a)*w.x+OrbisPlanetView.VisibleSnow(b)*w.y+OrbisPlanetView.VisibleSnow(c)*w.z;grounds[i].a=wet;
                float cloud=a.cloud*w.x+b.cloud*w.y+c.cloud*w.z;
                states[i]=new Color(cloud,a.moist*w.x+b.moist*w.y+c.moist*w.z,
                    a.convection*w.x+b.convection*w.y+c.convection*w.z,Mathf.Clamp01((a.cloudWater*w.x+b.cloudWater*w.y+c.cloudWater*w.z)/2));
                Vector3 v=a.cloudWind*w.x+b.cloudWind*w.y+c.cloudWind*w.z;
                winds[i]=new Color(v.x/9.4f+.5f,v.y/9.4f+.5f,v.z/9.4f+.5f,1);
                float rain=a.precipitationMmH*w.x+b.precipitationMmH*w.y+c.precipitationMmH*w.z;
                float snow=a.snowfallMmH*w.x+b.snowfallMmH*w.y+c.snowfallMmH*w.z;
                effectPixels[i]=new Color(1-Mathf.Exp(-rain/8),wet,(a.stormStage*w.x+b.stormStage*w.y+c.stormStage*w.z)/4,1-Mathf.Exp(-snow/5));
            }
            var model=world.Weather.Atmosphere;
            for(int k=0;k<OrbisAtmosphereCore.Layers;k++)for(int i=0;i<layerSamples.Length;i++)
            {
                var s=layerSamples[i];int offset=k*model.Count,j=k*layerSamples.Length+i;
                float thickness=(float)(OrbisAtmosphereCore.Edges[k+1]-OrbisAtmosphereCore.Edges[k]);
                float liquid=Read(model.Water[1],offset,s)*1000/thickness,ice=Read(model.Water[2],offset,s)*1000/thickness;
                float fraction=1-Mathf.Exp(-(liquid+ice)*4);
                layerPixels[j]=new Color(1-Mathf.Exp(-liquid*2.0f),1-Mathf.Exp(-ice*1.5f),fraction,
                    Mathf.Clamp01((Read(model.VerticalVelocity,offset,s)+6)/38));
                AtmosVector velocity=model.Wind[offset+s.a]*s.weights.x+model.Wind[offset+s.b]*s.weights.y+model.Wind[offset+s.c]*s.weights.z;
                layerWindPixels[j]=new Color((float)(velocity.X/.13+.5),(float)(velocity.Y/.13+.5),(float)(velocity.Z/.13+.5),1);
            }
            SealPoles(states,Width,Height,1);SealPoles(winds,Width,Height,1);SealPoles(effectPixels,Width,Height,1);
            SealPoles(layerPixels,LayerWidth,LayerHeight,OrbisAtmosphereCore.Layers);SealPoles(layerWindPixels,LayerWidth,LayerHeight,OrbisAtmosphereCore.Layers);
            ground.SetPixels(grounds);ground.Apply(false,false);groundRevision=world.Revision;groundWorld=world;
            weather.SetPixels(states);weather.Apply(false,false);wind.SetPixels(winds);wind.Apply(false,false);
            effects.SetPixels(effectPixels);effects.Apply(false,false);layers.SetPixels(layerPixels);layers.Apply(false,false);layerWind.SetPixels(layerWindPixels);layerWind.Apply(false,false);
            lastAtmosphereClock=world.AtmosphereClock;lastCalendarDay=world.day;
        }
        static void SealPoles(Color[] pixels,int width,int height,int depth)
        {
            for(int z=0;z<depth;z++)for(int side=0;side<2;side++)
            {
                int start=z*width*height+(side==0?0:height-1)*width;Color sum=Color.clear;
                for(int x=0;x<width;x++)sum+=pixels[start+x];sum/=width;
                for(int x=0;x<width;x++)pixels[start+x]=sum;
            }
        }
        public void Bind(Material m)
        {
            m.SetTexture("_GroundField",ground);m.SetTexture("_Weather",weather);m.SetTexture("_CloudWind",wind);m.SetTexture("_CloudNoise",noise);
            m.SetTexture("_CloudDetailNoise",detailNoise);
            m.SetTexture("_AtmosLayers",layers);m.SetTexture("_AtmosLayerWind",layerWind);m.SetTexture("_WeatherEffects",effects);
            m.SetFloat("_LayeredWeather",1);m.SetFloat("_CloudHeightScale",HeightScale);
            m.SetTexture("_HPHighDetail",hpHighDetail);
            m.SetFloat("_CloudShellTop",CloudShellTop);
        }
        public void Dispose()
        {
            UnityEngine.Object.Destroy(weather);UnityEngine.Object.Destroy(wind);UnityEngine.Object.Destroy(ground);UnityEngine.Object.Destroy(effects);
            UnityEngine.Object.Destroy(layers);UnityEngine.Object.Destroy(layerWind);if(ownsNoise)UnityEngine.Object.Destroy(noise);
            UnityEngine.Object.Destroy(detailNoise);
            UnityEngine.Object.Destroy(hpHighDetail);
        }
    }
}
