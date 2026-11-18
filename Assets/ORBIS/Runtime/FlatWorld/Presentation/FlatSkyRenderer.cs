using System;
using UnityEngine;
namespace Orbis.Flat
{
    /// <summary>Low-frequency visual weather shared by globe and local map.</summary>
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
        const int ClimateWidth=128,ClimateHeight=64;
        WorldData world;Texture2D climate,hpHigh,wind,climateWind;Texture3D hpNoise,hpDetail;Material bake,hpColumn;
        readonly Color32[] monthly=new Color32[ClimateWidth*ClimateHeight];
        readonly Color[] windPixels=new Color[ClimateWidth*ClimateHeight],seasonalWindPixels=new Color[ClimateWidth*ClimateHeight];
        int climatePeriod=-1,revision=-1,windMonth=-1,windRevision=-1;float debt;bool rendered,failed;
        double stagedClock;
        Vector4 pendingNorth,pendingSouth,visibleNorth,visibleSouth;
        FlatRegionalCloudField regionalClimate;
        long? stagingClimateStep;
        int stagingRevision;
        public long? RegionalClimateStep { get; private set; }
        bool weatherWasHidden;
        readonly Vector4[] flashes=new Vector4[4];
        FlatLightningVisuals lightning;
        public Texture2D DynamicWind {get{return wind;}}
        public bool DynamicReady {get{return Ready;}}
        public double RenderClock {get{return stagedClock;}}
        public Vector4[] LightningFlashes {get{return flashes;}}
        public void Initialize(WorldData data)
        {
            world=data;
            lightning=new FlatLightningVisuals(data);
            wind=WeatherTexture("Visual cloud drift");climateWind=WeatherTexture("Seasonal prevailing winds");
            UploadWind(world.Month);
            Shader shader=Resources.Load<Shader>("ORBIS/FlatWorld/FlatSkyBake");
            Shader columnShader=Resources.Load<Shader>("ORBIS/FlatWorld/HPCloudColumn");
            if(!shader||!shader.isSupported||!columnShader||!columnShader.isSupported)
            {
                failed=true;
                Status="云层缓存 Shader 不支持：云层已降级关闭。";
                Debug.LogWarning(Status);
                return;
            }
            bake=new Material(shader){name="Perf3.2 shared climate cloud cache"};
            hpColumn=new Material(columnShader){name="HP radial cloud cache"};
            hpNoise=OrbisCloudNoise.Create();hpDetail=OrbisCloudNoise.CreateDetail();hpHigh=OrbisCloudNoise.CreateHighDetail();
            hpColumn.SetTexture("_CloudNoise",hpNoise);hpColumn.SetTexture("_CloudDetailNoise",hpDetail);hpColumn.SetTexture("_HPHighDetail",hpHigh);
            hpColumn.SetTexture("_ScrollHeightTex",Texture2D.blackTexture);hpColumn.SetFloat("_ScrollHeightScale",0);
            hpColumn.SetVector("_ScrollSourceSize",new Vector4(512,256,0,0));hpColumn.SetFloat("_CloudHeightScale",OrbisWeatherField.HeightScale);
            hpColumn.SetFloat("_Density",1);hpColumn.SetFloat("_Cover",1);
            climate=new Texture2D(ClimateWidth,ClimateHeight,TextureFormat.RGBA32,false,true)
                {name="Monthly visual weather",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            bake.SetTexture("_ClimateTex",climate);bake.SetTexture("_ClimateWindTex",climateWind);
            hpColumn.SetTexture("_ScrollCloudWindTex",wind);hpColumn.SetTexture("_ClimateTex",climate);
            try{Resize(512);}catch(Exception ex){failed=true;Status="云缓存不可用，地表继续运行："+ex.Message;Debug.LogException(ex);}
        }
        static Texture2D WeatherTexture(string label)
        {return new Texture2D(ClimateWidth,ClimateHeight,TextureFormat.RGBAHalf,false,true)
            {name=label,filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};}
        static void SealPole(Color[] values,int w,int h)
        {for(int side=0;side<2;side++){int start=side==0?0:(h-1)*w;Color mean=Color.clear;for(int x=0;x<w;x++)mean+=values[start+x];mean/=w;for(int x=0;x<w;x++)values[start+x]=mean;}}
        void Resize(int width)
        {
            width=width>=1024?1024:512;
            if(Texture&&CacheWidth==width)return;
            ReleaseCache(Texture);ReleaseCache(ColumnTexture);
            ReleaseCache(weatherBack);ReleaseCache(columnBack);ReleaseCache(columnStrip);pendingStrip=-1;
            CacheWidth=width;int height=width/2;
            // The cheap weather pass resolves thin feeder bands at twice the
            // linear resolution of the ray-integrated shadow/precipitation map.
            Texture=new RenderTexture(width*2,height*2,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){name="Low and high cloud cover",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,useMipMap=false,autoGenerateMips=false};
            if(!Texture.Create())throw new InvalidOperationException("Cannot allocate 2D cloud render target.");
            var columnDescriptor=Texture.descriptor;columnDescriptor.width=width;columnDescriptor.height=height;
            ColumnTexture=new RenderTexture(columnDescriptor){name="HP integrated low/high map clouds",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            if(!ColumnTexture.Create())throw new InvalidOperationException("Cannot allocate HP cloud column cache.");
            weatherBack=NewCache(Texture.descriptor,"Weather staging");
            columnBack=NewCache(ColumnTexture.descriptor,"Cloud column staging");
            var strip=ColumnTexture.descriptor;strip.height=height/ColumnStrips;columnStrip=NewCache(strip,"Cloud column update strip");
            bake.SetTexture("_ClimateTex",climate);climatePeriod=revision=-1;rendered=false;debt=1;
            RegionalClimateStep=null;
        }
        static RenderTexture NewCache(RenderTextureDescriptor descriptor,string label)
        {
            var result=new RenderTexture(descriptor){name=label,filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            if(!result.Create())
            {
                Destroy(result);
                throw new InvalidOperationException("Cannot allocate "+label);
            }
            return result;
        }
        void ReleaseCache(RenderTexture target)
        {
            if(!target)return;
            if(RenderTexture.active==target)RenderTexture.active=null;
            target.Release();Destroy(target);
        }
        static void RenderCache(RenderTexture target,Material material)
        {
            RenderTexture previous=RenderTexture.active;bool previousSRGB=GL.sRGBWrite;
            try
            {
                GL.sRGBWrite=false;
                Graphics.Blit(Texture2D.blackTexture,target,material,0);
            }
            finally{RenderTexture.active=previous;GL.sRGBWrite=previousSRGB;}
        }
        void RenderColumnStrip()
        {
            hpColumn.SetVector("_ColumnUVTransform",new Vector4(0,pendingStrip/(float)ColumnStrips,1,1f/ColumnStrips));
            RenderCache(columnStrip,hpColumn);
            Graphics.CopyTexture(columnStrip,0,0,0,0,columnStrip.width,columnStrip.height,columnBack,0,0,0,pendingStrip*columnStrip.height);
            if(++pendingStrip<ColumnStrips)return;
            // Publish only a complete weather/optical-column pair. No visible stripes.
            var old=Texture;Texture=weatherBack;weatherBack=old;
            old=ColumnTexture;ColumnTexture=columnBack;columnBack=old;
            stagedClock=hpColumn.GetFloat("_ScrollWeatherTime");
            visibleNorth=pendingNorth;visibleSouth=pendingSouth;
            RegionalClimateStep=stagingClimateStep;
            pendingStrip=-1;rendered=true;
            Status="云层 "+Texture.width+"×"+Texture.height+" · 云影和雨雪 "+CacheWidth+"×"+(CacheWidth/2)+
                (RegionalClimateStep.HasValue?" · 区域气候统计":" · 月气候");
        }
        void UploadClimate()
        {
            int w=ClimateWidth,h=ClimateHeight,m=world.Month,next=(m+1)%12;
            int period=(int)Math.Floor(world.Day/30);
            uint seed=FMath.Seed(world.Seed);
            var currentYear=FlatClimateYear.At(seed,period*30.0);
            var followingYear=FlatClimateYear.At(seed,(period+1)*30.0);
            // Hold cloud drift constant until all strips of its climate snapshot finish.
            wind.SetPixels(windPixels);wind.Apply(false,false);
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
                var p=FlatWeatherSimulation.Direction((x+.5)/w,(y+.5)/h);
                currentYear.Offsets((float)p.X,(float)p.Y,(float)p.Z,out float degrees,out float rainDelta);
                followingYear.Offsets((float)p.X,(float)p.Y,(float)p.Z,out float nextDegrees,out float nextRainDelta);
                monthly[y*w+x]=new Color(Mathf.Clamp01(rain*(1+rainDelta)/880),Mathf.Clamp01(rainNext*(1+nextRainDelta)/880),
                    Mathf.Clamp01((temp*.25f+degrees+25)/65),Mathf.Clamp01((tempNext*.25f+nextDegrees+25)/65));
            }
            Color south=Color.clear,north=Color.clear;
            for(int x=0;x<w;x++){south+=(Color)monthly[x];north+=(Color)monthly[(h-1)*w+x];}
            bake.SetVector("_ClimatePoleSouth",(Vector4)(south/w));bake.SetVector("_ClimatePoleNorth",(Vector4)(north/w));
            bake.SetFloat("_ClimateRows",h);
            climate.SetPixels32(monthly);climate.Apply(false,false);climatePeriod=period;revision=world.TerrainRevision;
        }
        void UploadWind(int month)
        {
            var state=world.Moisture;int next=(month+1)%12;
            for(int y=0;y<ClimateHeight;y++)for(int x=0;x<ClimateWidth;x++)
            {
                float u=(x+.5f)/ClimateWidth,v=(y+.5f)/ClimateHeight;
                float lon=(u-.5f)*Mathf.PI*2,lat=(v-.5f)*Mathf.PI;
                float east=state==null?.4f:state.Sample(state.East,u,v,month);
                float north=state==null?0:state.Sample(state.North,u,v,month);
                float nextEast=state==null?east:state.Sample(state.East,u,v,next);
                float nextNorth=state==null?north:state.Sample(state.North,u,v,next);
                var eastDirection=new Vector3(Mathf.Cos(lon),0,-Mathf.Sin(lon));
                var northDirection=new Vector3(-Mathf.Sin(lat)*Mathf.Sin(lon),Mathf.Cos(lat),-Mathf.Sin(lat)*Mathf.Cos(lon));
                Vector3 velocity=Vector3.ClampMagnitude((eastDirection*east+northDirection*north)*.014f,.025f);
                int i=y*ClimateWidth+x;
                windPixels[i]=new Color(velocity.x,velocity.y,velocity.z,0);
                seasonalWindPixels[i]=new Color(east,north,nextEast,nextNorth);
            }
            SealPole(windPixels,ClimateWidth,ClimateHeight);
            climateWind.SetPixels(seasonalWindPixels);climateWind.Apply(false,false);
            windMonth=month;windRevision=world.TerrainRevision;
        }
        static Vector4 StormVector(FlatCycloneTrack track)
        {
            if(!track.Tracked||track.Strength<=0)return Vector4.zero;
            var p=FlatWeatherSimulation.Direction(track.U,track.V);
            return new Vector4((float)p.X,(float)p.Y,(float)p.Z,(float)track.Strength);
        }
        void StageCyclones(double calendarDay)
        {
            var state=world.Cyclones;
            var north=state==null?default:state.Sample(true,calendarDay);
            var south=state==null?default:state.Sample(false,calendarDay);
            pendingNorth=StormVector(north);pendingSouth=StormVector(south);
            bake.SetVector("_CycloneNorth",pendingNorth);bake.SetVector("_CycloneSouth",pendingSouth);
            bake.SetVector("_CyclonePhase",new Vector4((float)(north.Phase%(2*Math.PI)),(float)(south.Phase%(2*Math.PI)),0,0));
        }
        public void Tick(float delta,bool needed,bool animate,bool autoSun,int width,double calendarDay,Vector2 focus=default,bool local=false)
        {
            lightning.Tick(delta,needed&&animate&&!failed,focus.x,focus.y,local);
            for(int i=0;i<flashes.Length;i++)
            {var flash=lightning.Flashes[i];flashes[i]=new Vector4(flash.X,flash.Y,flash.Z,flash.Strength);}
            if(animate)Clock+=Math.Max(0,delta);
            // Vegetation shares prevailing winds even while cloud rendering sleeps.
            if(windMonth!=world.Month||windRevision!=world.TerrainRevision)UploadWind(world.Month);
            if(!needed){weatherWasHidden=true;Status="云缓存休眠；晨昏/大气开关独立。";return;}
            if(failed)return;
            try
            {
                Resize(width);
                // A sleeping cache may contain half of an old storm. Resume at today's track.
                bool resumed=weatherWasHidden;
                if(resumed){pendingStrip=-1;weatherWasHidden=false;}
                if(pendingStrip>=0 && (stagingRevision!=world.TerrainRevision ||
                    (stagingClimateStep.HasValue && stagingClimateStep.Value!=(long)Math.Floor(world.Day*4))))
                    pendingStrip=-1;
                if(pendingStrip>=0){RenderColumnStrip();return;}
                bool climateChanged=climatePeriod!=(int)Math.Floor(world.Day/30)||revision!=world.TerrainRevision;
                if(climateChanged)UploadClimate();
                if(regionalClimate==null)regionalClimate=new FlatRegionalCloudField(world);
                bool fieldReady=regionalClimate.Prepare(world.Day,out bool changed);
                // Keep the last complete pair on screen while the next six-hour field is computed.
                // A newly ready field also wakes a paused sky after the first neutral bake.
                if(!fieldReady&&rendered)return;
                bool regionalChanged=changed||RegionalClimateStep!=regionalClimate.Step;
                if(!animate&&rendered&&!climateChanged&&!resumed&&!regionalChanged)
                {Status=RegionalClimateStep.HasValue?"云缓存暂停 · 区域气候统计":"云缓存暂停 · 月气候";return;}
                debt+=Mathf.Max(0,delta);
                if(debt<.5f&&rendered&&!climateChanged&&!resumed&&!regionalChanged)return;debt=0;
                regionalClimate.Bind(bake);
                stagingClimateStep=regionalClimate.Step;stagingRevision=world.TerrainRevision;
                StageCyclones(calendarDay);
                bake.SetFloat("_VisualTime",(float)Clock);
                bake.SetFloat("_MonthBlend",(float)(world.Day%30)/30f);
                hpColumn.SetFloat("_MonthBlend",(float)(world.Day%30)/30f);
                RenderCache(weatherBack,bake);
                hpColumn.SetTexture("_ScrollWeatherTex",weatherBack);
                hpColumn.SetFloat("_ScrollWeatherTime",(float)Clock);pendingStrip=0;RenderColumnStrip();
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
            target.SetTexture("_VegetationWindTex",climateWind?climateWind:Texture2D.blackTexture);
            target.SetFloat("_VegetationWindReady",climateWind?1:0);
            target.SetFloat("_VegetationWindBlend",world==null?0:(float)(world.Day/30-Math.Floor(world.Day/30)));
            target.SetVector("_CycloneNorth",visibleNorth);target.SetVector("_CycloneSouth",visibleSouth);
            target.SetFloat("_FlatCloudShadows",shadows?1:0);target.SetFloat("_FlatAtmosphere",atmosphere?1:0);
            target.SetFloat("_FlatDayNight",dayNight?1:0);target.SetFloat("_FlatRain",rain?1:0);target.SetFloat("_FlatLightning",lightning?1:0);
            target.SetFloat("_FlatNightLights",nightLights?1:0);target.SetFloat("_CloudOpacity",Mathf.Clamp01(opacity));
            target.SetVectorArray("_OrbisLightning",flashes);
        }
        void OnDestroy()
        {regionalClimate?.Dispose();ReleaseCache(weatherBack);ReleaseCache(columnBack);ReleaseCache(columnStrip);ReleaseCache(Texture);ReleaseCache(ColumnTexture);if(climate)Destroy(climate);if(wind)Destroy(wind);if(climateWind)Destroy(climateWind);if(bake)Destroy(bake);if(hpColumn)Destroy(hpColumn);if(hpNoise)Destroy(hpNoise);if(hpDetail)Destroy(hpDetail);if(hpHigh)Destroy(hpHigh);}
    }
}
