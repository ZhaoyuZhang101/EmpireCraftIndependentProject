using System;
using System.Threading.Tasks;
using UnityEngine;
namespace Orbis.Flat
{
    /// <summary>A shared, presentation-only cloud cache driven by authoritative monthly
    /// climate. Does not advance weather, drainage, populations or a second WorldData.</summary>
    public sealed class FlatSkyRenderer : MonoBehaviour
    {
        public RenderTexture Texture { get; private set; }
        public RenderTexture ColumnTexture { get; private set; }
        RenderTexture weatherBack,columnBack,columnStrip;int pendingStrip=-1;
        const int ColumnStrips=4;
        public double Clock { get; private set; }
        public bool Ready { get { return Texture && ColumnTexture && bake && hpColumn && rendered && Texture.IsCreated() && ColumnTexture.IsCreated(); } }
        public string Status { get; private set; } = "云层缓存等待启用。";
        public int CacheWidth { get; private set; }
        WorldData world;Texture2D climate,hpHigh,moisture,wind,liveCloud,liveWind,liveEffects;Texture3D hpNoise,hpDetail;Material bake,hpColumn;Color32[] monthly;
        int month=-1,revision=-1;float debt;bool rendered,failed;double rotationSeconds;
        FlatWeatherSimulation weather;Task<FlatWeatherFrame> weatherJob;double lastCalendarDay=double.NaN,weatherDebt;
        readonly Vector4[] flashes=new Vector4[4];readonly float[] flashAge=new float[4];
        Color[] cloudPixels,windPixels,effectPixels;
        public Texture2D DynamicWind {get{return liveWind;}}
        public bool DynamicReady {get{return liveCloud;}}
        public Vector4[] LightningFlashes {get{return flashes;}}
        public void Initialize(WorldData data)
        {
            world=data;
            Shader shader=Resources.Load<Shader>("ORBIS/FlatWorld/FlatSkyBake");
            if(!shader||!shader.isSupported){failed=true;Status="云层缓存 Shader 不支持：保留地表与晨昏，云层已降级关闭。";Debug.LogWarning(Status);return;}
            bake=new Material(shader){name="Perf3.2 shared climate cloud cache"};
            hpColumn=new Material(Resources.Load<Shader>("ORBIS/FlatWorld/HPCloudColumn")){name="HP radial cloud cache"};
            hpNoise=OrbisCloudNoise.Create();hpDetail=OrbisCloudNoise.CreateDetail();hpHigh=OrbisCloudNoise.CreateHighDetail();
            hpColumn.SetTexture("_CloudNoise",hpNoise);hpColumn.SetTexture("_CloudDetailNoise",hpDetail);hpColumn.SetTexture("_HPHighDetail",hpHigh);
            hpColumn.SetTexture("_ScrollHeightTex",Texture2D.blackTexture);hpColumn.SetFloat("_ScrollHeightScale",0);
            hpColumn.SetVector("_ScrollSourceSize",new Vector4(512,256,0,0));hpColumn.SetFloat("_CloudHeightScale",OrbisWeatherField.HeightScale);
            hpColumn.SetFloat("_Density",1);hpColumn.SetFloat("_Cover",1);
            weather=new FlatWeatherSimulation(world,Math.Min(4,FlatWorkerPolicy.Resolve()));
            liveCloud=WeatherTexture("Prognostic HP cloud columns");liveWind=WeatherTexture("Prognostic cloud wind");liveEffects=WeatherTexture("Prognostic rain and snow");
            bake.SetTexture("_LiveCloudTex",liveCloud);bake.SetTexture("_LiveWindTex",liveWind);bake.SetTexture("_LiveEffectTex",liveEffects);bake.SetFloat("_DynamicReady",0);
            hpColumn.SetTexture("_ScrollCloudWindTex",liveWind);
            hpColumn.SetTexture("_ScrollEffectsTex",liveEffects);
            try{Resize(512);}catch(Exception ex){failed=true;Status="云缓存不可用，地表继续运行："+ex.Message;Debug.LogException(ex);}
        }
        static Texture2D WeatherTexture(string label)
        {return new Texture2D(FlatWeatherSimulation.Width,FlatWeatherSimulation.Height,TextureFormat.RGBAHalf,false,true)
            {name=label,filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};}
        void PublishWeather(FlatWeatherFrame frame)
        {
            int count=frame.Width*frame.Height;
            if(cloudPixels==null||cloudPixels.Length!=count){cloudPixels=new Color[count];windPixels=new Color[count];effectPixels=new Color[count];}
            var c=cloudPixels;var v=windPixels;var e=effectPixels;
            for(int i=0;i<count;i++){int j=i*4;c[i]=new Color(frame.Clouds[j],frame.Clouds[j+1],frame.Clouds[j+2],frame.Clouds[j+3]);
                v[i]=new Color(frame.Wind[j],frame.Wind[j+1],frame.Wind[j+2],0);e[i]=new Color(frame.Effects[j],frame.Effects[j+1],frame.Effects[j+2],0);}
            SealPole(c,frame.Width,frame.Height);SealPole(v,frame.Width,frame.Height);SealPole(e,frame.Width,frame.Height);
            liveCloud.SetPixels(c);liveCloud.Apply(false,false);liveWind.SetPixels(v);liveWind.Apply(false,false);liveEffects.SetPixels(e);liveEffects.Apply(false,false);
            bake.SetFloat("_DynamicReady",1);
            foreach(var strike in frame.Lightning)
            {
                int slot=0;for(int j=1;j<4;j++)if(flashAge[j]<flashAge[slot])slot=j;
                var p=weather.Model.Position[strike.Cell];flashes[slot]=new Vector4((float)p.X,(float)p.Y,(float)p.Z,Mathf.Clamp((float)strike.Strength,0,2));flashAge[slot]=.7f;
            }
        }
        static void SealPole(Color[] values,int w,int h)
        {for(int side=0;side<2;side++){int start=side==0?0:(h-1)*w;Color mean=Color.clear;for(int x=0;x<w;x++)mean+=values[start+x];mean/=w;for(int x=0;x<w;x++)values[start+x]=mean;}}
        void Resize(int width)
        {
            width=width>=1024?1024:512;
            if(Texture&&CacheWidth==width)return;
            if(Texture){Texture.Release();Destroy(Texture);}if(climate)Destroy(climate);
            if(ColumnTexture){ColumnTexture.Release();Destroy(ColumnTexture);}
            ReleaseCache(weatherBack);ReleaseCache(columnBack);ReleaseCache(columnStrip);pendingStrip=-1;
            CacheWidth=width;int height=width/2;
            climate=new Texture2D(width,height,TextureFormat.RGBA32,false,true){name="Monthly climate visual cache",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            monthly=new Color32[width*height];
            Texture=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){name="Perf3.2 low and high clouds",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,useMipMap=false,autoGenerateMips=false};
            if(!Texture.Create())throw new InvalidOperationException("Cannot allocate 2D cloud render target.");
            ColumnTexture=new RenderTexture(Texture.descriptor){name="HP integrated low/high map clouds",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            if(!ColumnTexture.Create())throw new InvalidOperationException("Cannot allocate HP cloud column cache.");
            weatherBack=NewCache(Texture.descriptor,"Weather staging");
            columnBack=NewCache(ColumnTexture.descriptor,"Cloud column staging");
            var strip=ColumnTexture.descriptor;strip.height=height/ColumnStrips;columnStrip=NewCache(strip,"Cloud column update strip");
            bake.SetTexture("_ClimateTex",climate);month=revision=-1;rendered=false;debt=1;
        }
        static RenderTexture NewCache(RenderTextureDescriptor descriptor,string label)
        {
            var result=new RenderTexture(descriptor){name=label,filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            if(!result.Create())throw new InvalidOperationException("Cannot allocate "+label);return result;
        }
        void ReleaseCache(RenderTexture target){if(target){target.Release();Destroy(target);}}
        void RenderColumnStrip()
        {
            hpColumn.SetVector("_ColumnUVTransform",new Vector4(0,pendingStrip/(float)ColumnStrips,1,1f/ColumnStrips));
            Graphics.Blit(Texture2D.blackTexture,columnStrip,hpColumn,0);
            Graphics.CopyTexture(columnStrip,0,0,0,0,columnStrip.width,columnStrip.height,columnBack,0,0,0,pendingStrip*columnStrip.height);
            if(++pendingStrip<ColumnStrips)return;
            // Publish only a complete weather/optical-column pair. No visible stripes.
            var old=Texture;Texture=weatherBack;weatherBack=old;
            old=ColumnTexture;ColumnTexture=columnBack;columnBack=old;
            pendingStrip=-1;rendered=true;
        }
        void UploadClimate()
        {
            int w=CacheWidth,h=w/2,m=world.Month,next=(m+1)%12;
            UploadMoisture(m);
            BindCyclone("_CycloneNorth",FlatCycloneSelection.Find(world,m,true));
            BindCyclone("_CycloneSouth",FlatCycloneSelection.Find(world,m,false));
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                // Neighbour-averaged downsample avoids missing an entire wet coastal tile.
                int sx=(int)((x+.5f)*world.Width/w),sy=(int)((y+.5f)*world.Height/h);
                float rain=0,rainNext=0,temp=0,tempNext=0;
                for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
                {
                    int i=world.Index(sx+dx,Mathf.Clamp(sy+dy,0,world.Height-1));
                    rain+=world.Precipitation[i*12+m];rainNext+=world.Precipitation[i*12+next];
                    temp+=world.Temperature[i*12+m];tempNext+=world.Temperature[i*12+next];
                }
                monthly[y*w+x]=new Color(Mathf.Clamp01(rain/880),Mathf.Clamp01(rainNext/880),Mathf.Clamp01((temp*.25f+25)/65),Mathf.Clamp01((tempNext*.25f+25)/65));
            }
            Color south=Color.clear,north=Color.clear;
            for(int x=0;x<w;x++){south+=(Color)monthly[x];north+=(Color)monthly[(h-1)*w+x];}
            bake.SetVector("_ClimatePoleSouth",(Vector4)(south/w));bake.SetVector("_ClimatePoleNorth",(Vector4)(north/w));
            bake.SetFloat("_ClimateRows",h);
            climate.SetPixels32(monthly);climate.Apply(false,false);month=m;revision=world.TerrainRevision;
        }
        void UploadMoisture(int month)
        {
            var state=world.Moisture;bake.SetFloat("_MoistureReady",state==null?0:1);
            if(state==null)return;
            if(!moisture)
            {
                moisture=new Texture2D(state.Width,state.Height,TextureFormat.RGBAHalf,false,true){name="Column humidity and condensation",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
                wind=new Texture2D(state.Width,state.Height,TextureFormat.RGBAHalf,false,true){name="Seasonal moisture transport wind",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
                bake.SetTexture("_MoistureTex",moisture);bake.SetTexture("_ClimateWindTex",wind);
            }
            int next=(month+1)%12;var water=new Color[state.Width*state.Height];var velocity=new Color[water.Length];
            for(int i=0;i<water.Length;i++)
            {
                int a=i*12+month,b=i*12+next;
                water[i]=new Color(state.Humidity[a],state.Humidity[b],state.Condensation[a]/6,state.Condensation[b]/6);
                velocity[i]=new Color(state.East[a],state.North[a],state.East[b],state.North[b]);
            }
            moisture.SetPixels(water);moisture.Apply(false,false);wind.SetPixels(velocity);wind.Apply(false,false);
        }
        void BindCyclone(string property,FlatCyclone cyclone)
        {
            if(!cyclone.Active){bake.SetVector(property,Vector4.zero);return;}
            float longitude=((cyclone.X+.5f)/world.Width-.5f)*Mathf.PI*2;
            float latitude=((cyclone.Y+.5f)/world.Height-.5f)*Mathf.PI;
            float cosine=Mathf.Cos(latitude);
            bake.SetVector(property,new Vector4(Mathf.Sin(longitude)*cosine,Mathf.Sin(latitude),
                                                Mathf.Cos(longitude)*cosine,cyclone.Strength));
        }
        public void Tick(float delta,bool needed,bool animate,bool autoSun,int width,double calendarDay)
        {
            if(animate){Clock+=Math.Max(0,delta);if(autoSun)rotationSeconds+=Math.Max(0,delta);}
            for(int j=0;j<4;j++){flashAge[j]=Mathf.Max(0,flashAge[j]-(animate?delta:0));flashes[j].w*=flashAge[j]>0?Mathf.Exp(-(animate?delta:0)*6):0;}
            // Hold wind and precipitation constant while the four GPU slices
            // are assembled, otherwise their boundaries could become visible.
            if(weatherJob!=null&&weatherJob.IsCompleted&&pendingStrip<0)
            {try{PublishWeather(weatherJob.GetAwaiter().GetResult());}catch(Exception ex){Debug.LogException(ex);bake.SetFloat("_DynamicReady",0);weather=null;}weatherJob=null;}
            if(double.IsNaN(lastCalendarDay))lastCalendarDay=calendarDay;
            double elapsed=animate?Math.Max(0,calendarDay-lastCalendarDay):0;lastCalendarDay=calendarDay;
            weatherDebt=Math.Min(.8,weatherDebt+elapsed*120);
            if(weather!=null&&weatherJob==null&&weatherDebt>=.1)
            {
                int steps=Math.Min(8,(int)(weatherDebt/.1));weatherDebt-=steps*.1;
                double day=calendarDay-weatherDebt/120;
                weatherJob=Task.Run(()=>{for(int j=0;j<steps;j++)weather.Step(.1,day-(steps-j-1)*.1/120);return weather.Capture();});
            }
            if(!needed){Status="云缓存休眠；晨昏/大气开关独立。";return;}
            if(failed)return;
            try
            {
                Resize(width);
                bool climateChanged=month!=world.Month||revision!=world.TerrainRevision;
                if(climateChanged)UploadClimate();
                if(pendingStrip>=0){RenderColumnStrip();return;}
                if(!animate&&rendered&&!climateChanged){Status="二维云缓存暂停；保留已渲染图像。";return;}
                debt+=Mathf.Max(0,delta);
                if(debt<.20f&&rendered)return;debt=0;
                bake.SetFloat("_VisualTime",(float)Clock);
                bake.SetFloat("_MonthBlend",(float)(world.Day%30)/30f);
                Graphics.Blit(Texture2D.blackTexture,weatherBack,bake,0);
                hpColumn.SetTexture("_ScrollWeatherTex",weatherBack);
                hpColumn.SetFloat("_ScrollWeatherTime",(float)Clock);pendingStrip=0;RenderColumnStrip();
                Status="HP 云层缓存 "+CacheWidth+"×"+(CacheWidth/2)+" · 分层水汽、风场、降水 · 最高 5 Hz";
            }
            catch(Exception ex){failed=true;rendered=false;Status="云缓存已安全关闭："+ex.Message;Debug.LogException(ex);}
        }
        public Vector3 Sun(float manualAngle,double calendarDay=double.NaN,bool automatic=false)
        {
            double day=double.IsNaN(calendarDay)?world.Day:calendarDay;
            float season=(float)((day%360-80)*Math.PI*2/360);
            float declination=Mathf.Asin(Mathf.Sin(23.44f*Mathf.Deg2Rad)*Mathf.Sin(season));
            // Gameplay advances days in large increments; never use that directly as
            // solar rotation. Otherwise six days/second would cause a flashing map.
            float angle=automatic?(float)FlatSolarClock.SolarLongitude(day,manualAngle):manualAngle*Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(angle)*Mathf.Cos(declination),Mathf.Sin(declination),Mathf.Cos(angle)*Mathf.Cos(declination));
        }
        public void Bind(Material target,Vector3 sun,bool clouds,bool shadows,bool atmosphere,bool dayNight,bool rain,bool lightning,bool nightLights,float opacity)
        {
            if(!target)return;
            target.SetTexture("_SkyTex",Ready?(UnityEngine.Texture)ColumnTexture:Texture2D.blackTexture);
            target.SetFloat("_SkyReady",Ready?1:0);target.SetVector("_FlatSun",sun);
            target.SetFloat("_SkyClock",(float)Clock);target.SetFloat("_FlatClouds",clouds?1:0);
            target.SetFloat("_FlatCloudShadows",shadows?1:0);target.SetFloat("_FlatAtmosphere",atmosphere?1:0);
            target.SetFloat("_FlatDayNight",dayNight?1:0);target.SetFloat("_FlatRain",rain?1:0);target.SetFloat("_FlatLightning",lightning?1:0);
            target.SetFloat("_FlatNightLights",nightLights?1:0);target.SetFloat("_CloudOpacity",Mathf.Clamp01(opacity));
            target.SetVectorArray("_FlatLightningEvents",flashes);
        }
        void OnDestroy()
        {ReleaseCache(weatherBack);ReleaseCache(columnBack);ReleaseCache(columnStrip);if(Texture){Texture.Release();Destroy(Texture);}if(ColumnTexture){ColumnTexture.Release();Destroy(ColumnTexture);}if(climate)Destroy(climate);if(moisture)Destroy(moisture);if(wind)Destroy(wind);if(liveCloud)Destroy(liveCloud);if(liveWind)Destroy(liveWind);if(liveEffects)Destroy(liveEffects);if(bake)Destroy(bake);if(hpColumn)Destroy(hpColumn);if(hpNoise)Destroy(hpNoise);if(hpDetail)Destroy(hpDetail);if(hpHigh)Destroy(hpHigh);}
    }
}
