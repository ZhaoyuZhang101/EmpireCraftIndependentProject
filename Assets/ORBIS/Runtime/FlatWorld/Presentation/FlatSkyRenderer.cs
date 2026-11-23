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
        const int ClimateWidth=256,ClimateHeight=128;
        WorldData world;Texture2D climate,hpHigh,wind,windBack,climateWind,glebaForcing,glebaAux,glebaUpperWind;Texture3D hpNoise,hpDetail;Material bake,hpColumn;
        readonly byte[] glebaForcingBytes=new byte[ClimateWidth*ClimateHeight*4],glebaAuxBytes=new byte[ClimateWidth*ClimateHeight*4];
        readonly ushort[] glebaUpperWindHalf=new ushort[ClimateWidth*ClimateHeight*4];
        readonly Color32[] monthly=new Color32[ClimateWidth*ClimateHeight];
        readonly Color[] windPixels=new Color[ClimateWidth*ClimateHeight],seasonalWindPixels=new Color[ClimateWidth*ClimateHeight];
        readonly Vector2[] longitudeBasis=new Vector2[ClimateWidth],latitudeBasis=new Vector2[ClimateHeight];
        double climateStartDay=double.NaN,windStartDay=double.NaN;
        double visibleWindDay=double.NaN,stagingWindDay;
        int visibleWindRevision=-1;bool stagingWind;
        int revision=-1,windRevision=-1;float debt,windBlend;bool rendered,failed;
        double stagedClock;
        Vector4 pendingNorth,pendingSouth,pendingPhase,visibleNorth,visibleSouth,visiblePhase;
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
            wind=WeatherTexture("Visual cloud drift");windBack=WeatherTexture("Cloud drift staging");climateWind=WeatherTexture("Seasonal prevailing winds");
            for(int x=0;x<ClimateWidth;x++)
            {
                float lon=((x+.5f)/ClimateWidth-.5f)*2*Mathf.PI;
                longitudeBasis[x]=new Vector2(Mathf.Sin(lon),Mathf.Cos(lon));
            }
            for(int y=0;y<ClimateHeight;y++)
            {
                float lat=((y+.5f)/ClimateHeight-.5f)*Mathf.PI;
                latitudeBasis[y]=new Vector2(Mathf.Sin(lat),Mathf.Cos(lat));
            }
            var season=new FlatMonthlyForcing(world.Day);windBlend=season.SegmentBlend;UploadWind(season);
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
            glebaForcing=new Texture2D(ClimateWidth,ClimateHeight,TextureFormat.RGBA32,false,true){name="Gleba synoptic cloud forcing",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            glebaAux=new Texture2D(ClimateWidth,ClimateHeight,TextureFormat.RGBA32,false,true){name="Gleba stratocumulus and orographic cloud aux",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            glebaUpperWind=WeatherTexture("Gleba upper-level Rossby and anvil wind");
            bake.SetTexture("_ClimateTex",climate);bake.SetTexture("_ClimateWindTex",climateWind);
            bake.SetTexture("_CloudSynopticTex",glebaForcing);bake.SetTexture("_CloudSynopticAuxTex",glebaAux);bake.SetTexture("_UpperWindTex",glebaUpperWind);bake.SetFloat("_GlebaCloudEnable",1f);
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
            // Keep nearby cloud coverage for local light shafts alongside the exact radial column.
            columnDescriptor.useMipMap=true;columnDescriptor.autoGenerateMips=false;columnDescriptor.mipCount=5;
            ColumnTexture=new RenderTexture(columnDescriptor){name="HP integrated low/high map clouds",filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
            if(!ColumnTexture.Create())throw new InvalidOperationException("Cannot allocate HP cloud column cache.");
            weatherBack=NewCache(Texture.descriptor,"Weather staging");
            columnBack=NewCache(ColumnTexture.descriptor,"Cloud column staging");
            var strip=ColumnTexture.descriptor;strip.height=height/ColumnStrips;strip.useMipMap=false;strip.mipCount=1;
            columnStrip=NewCache(strip,"Cloud column update strip");
            bake.SetTexture("_ClimateTex",climate);climateStartDay=double.NaN;revision=-1;rendered=false;debt=1;
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
            columnBack.GenerateMips();
            // Publish only a complete weather/optical-column pair. No visible stripes.
            var old=Texture;Texture=weatherBack;weatherBack=old;
            old=ColumnTexture;ColumnTexture=columnBack;columnBack=old;
            if(stagingWind)
            {
                var oldWind=wind;wind=windBack;windBack=oldWind;
                visibleWindDay=stagingWindDay;visibleWindRevision=stagingRevision;
            }
            stagedClock=hpColumn.GetFloat("_ScrollWeatherTime");
            visibleNorth=pendingNorth;visibleSouth=pendingSouth;visiblePhase=pendingPhase;
            Shader.SetGlobalVector("_OrbisCycloneNorth",visibleNorth);
            Shader.SetGlobalVector("_OrbisCycloneSouth",visibleSouth);
            Shader.SetGlobalVector("_OrbisCyclonePhase",visiblePhase);
            RegionalClimateStep=stagingClimateStep;
            pendingStrip=-1;rendered=true;
            Status="云层 "+Texture.width+"×"+Texture.height+" · 云影和雨雪 "+CacheWidth+"×"+(CacheWidth/2)+
                (RegionalClimateStep.HasValue?" · 区域气候统计":" · 月气候");
        }
        void UploadClimate(FlatMonthlyForcing season)
        {
            int w=ClimateWidth,h=ClimateHeight;
            var start=new FlatMonthlyForcing(season.SegmentStartDay);
            double endDay=season.SegmentStartDay+FlatMonthlyForcing.HalfMonthDays;
            var end=new FlatMonthlyForcing(endDay);
            uint seed=FMath.Seed(world.Seed);
            var currentYear=FlatClimateYear.At(seed,season.SegmentStartDay);
            var followingYear=FlatClimateYear.At(seed,endDay);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                // Neighbour-averaged downsample avoids missing an entire wet coastal tile.
                int sx=(int)((x+.5f)*world.Width/w),sy=(int)((y+.5f)*world.Height/h);
                float rain=0,rainNext=0,temp=0,tempNext=0;
                for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
                {
                    int offset=world.Index(sx+dx,Mathf.Clamp(sy+dy,0,world.Height-1))*12;
                    rain+=start.Sample(world.Precipitation,offset,ClimateRange.Nonnegative);
                    rainNext+=end.Sample(world.Precipitation,offset,ClimateRange.Nonnegative);
                    temp+=start.Sample(world.Temperature,offset);tempNext+=end.Sample(world.Temperature,offset);
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
            climate.SetPixels32(monthly);climate.Apply(false,false);
            GlebaGeographySupplement.BuildCloudSynopticFields(world,season.SegmentStartDay,w,h,glebaForcingBytes,glebaAuxBytes,glebaUpperWindHalf);
            glebaForcing.LoadRawTextureData(glebaForcingBytes);glebaForcing.Apply(false,false);
            glebaAux.LoadRawTextureData(glebaAuxBytes);glebaAux.Apply(false,false);
            if(glebaUpperWind.format==TextureFormat.RGBAHalf)glebaUpperWind.SetPixelData(glebaUpperWindHalf,0);
            else{var flt=new float[glebaUpperWindHalf.Length];for(int k=0;k<flt.Length;k++)flt[k]=Mathf.HalfToFloat(glebaUpperWindHalf[k]);glebaUpperWind.SetPixelData(flt,0);}
            glebaUpperWind.Apply(false,false);
            Vector4 fSouth=Vector4.zero,fNorth=Vector4.zero;int topRow=(h-1)*w*4;
            for(int x=0;x<w;x++){int b0=x*4,b1=topRow+x*4;fSouth+=new Vector4(glebaForcingBytes[b0],glebaForcingBytes[b0+1],glebaForcingBytes[b0+2],glebaForcingBytes[b0+3]);fNorth+=new Vector4(glebaForcingBytes[b1],glebaForcingBytes[b1+1],glebaForcingBytes[b1+2],glebaForcingBytes[b1+3]);}
            float poleInv=1f/(255f*w);bake.SetVector("_GlebaForcingPoleSouth",fSouth*poleInv);bake.SetVector("_GlebaForcingPoleNorth",fNorth*poleInv);
            GlebaGeographySupplement.GetCloudCirculationParams(world,out float hadleyRad,out float polarRad,out float rossbyWaveK,out float regimeCode);
            bake.SetVector("_GlebaCirculationParams",new Vector4(hadleyRad,polarRad,rossbyWaveK,regimeCode));
            bake.SetFloat("_GlebaCloudEnable",1f);
            climateStartDay=season.SegmentStartDay;revision=world.TerrainRevision;
        }
        void UploadWind(FlatMonthlyForcing season)
        {
            var state=world.Moisture;
            var start=new FlatMonthlyForcing(season.SegmentStartDay);
            var end=new FlatMonthlyForcing(season.SegmentStartDay+FlatMonthlyForcing.HalfMonthDays);
            for(int y=0;y<ClimateHeight;y++)for(int x=0;x<ClimateWidth;x++)
            {
                float u=(x+.5f)/ClimateWidth,v=(y+.5f)/ClimateHeight;
                float east=state==null?.4f:start.Sample(state,state.East,u,v);
                float north=state==null?0:start.Sample(state,state.North,u,v);
                float nextEast=state==null?east:end.Sample(state,state.East,u,v);
                float nextNorth=state==null?north:end.Sample(state,state.North,u,v);
                seasonalWindPixels[y*ClimateWidth+x]=new Color(east,north,nextEast,nextNorth);
            }
            climateWind.SetPixels(seasonalWindPixels);climateWind.Apply(false,false);
            windStartDay=season.SegmentStartDay;windRevision=world.TerrainRevision;
        }
        void StageCloudWind()
        {
            stagingWindDay=world.Day;
            stagingWind=stagingWindDay!=visibleWindDay||stagingRevision!=visibleWindRevision;
            if(stagingWind)
            {
                for(int y=0;y<ClimateHeight;y++)for(int x=0;x<ClimateWidth;x++)
                {
                    int i=y*ClimateWidth+x;Color ends=seasonalWindPixels[i];
                    float east=Mathf.Lerp(ends.r,ends.b,windBlend),north=Mathf.Lerp(ends.g,ends.a,windBlend);
                    var lon=longitudeBasis[x];var lat=latitudeBasis[y];
                    var velocity=Vector3.ClampMagnitude(new Vector3(east*lon.y-north*lat.x*lon.x,
                        north*lat.y,-east*lon.x-north*lat.x*lon.y)*.014f,.025f);
                    windPixels[i]=new Color(velocity.x,velocity.y,velocity.z,0);
                }
                SealPole(windPixels,ClimateWidth,ClimateHeight);
                windBack.SetPixels(windPixels);windBack.Apply(false,false);
            }
            hpColumn.SetTexture("_ScrollCloudWindTex",stagingWind?windBack:wind);
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
            pendingPhase=new Vector4((float)(north.Phase%(2*Math.PI)),(float)(south.Phase%(2*Math.PI)),0,0);
            bake.SetVector("_CycloneNorth",pendingNorth);bake.SetVector("_CycloneSouth",pendingSouth);
            bake.SetVector("_CyclonePhase",pendingPhase);
            hpColumn.SetVector("_OrbisCycloneNorth",pendingNorth);hpColumn.SetVector("_OrbisCycloneSouth",pendingSouth);
            hpColumn.SetVector("_OrbisCyclonePhase",pendingPhase);
        }
        public void Tick(float delta,bool needed,bool animate,bool autoSun,int width,double calendarDay,Vector2 focus=default,bool local=false)
        {
            lightning.Tick(delta,needed&&animate&&!failed,focus.x,focus.y,local);
            for(int i=0;i<flashes.Length;i++)
            {var flash=lightning.Flashes[i];flashes[i]=new Vector4(flash.X,flash.Y,flash.Z,flash.Strength);}
            if(animate)Clock+=Math.Max(0,delta);
            // Vegetation shares prevailing winds even while cloud rendering sleeps.
            var season=new FlatMonthlyForcing(world.Day);windBlend=season.SegmentBlend;
            if(windStartDay!=season.SegmentStartDay||windRevision!=world.TerrainRevision)UploadWind(season);
            if(!needed){weatherWasHidden=true;Status="云缓存休眠；晨昏/大气开关独立。";return;}
            if(failed)return;
            try
            {
                Resize(width);
                // A sleeping cache may contain half of an old storm. Resume at today's track.
                bool resumed=weatherWasHidden;
                if(resumed){pendingStrip=-1;weatherWasHidden=false;}
                if(pendingStrip>=0 && (stagingRevision!=world.TerrainRevision || climateStartDay!=season.SegmentStartDay ||
                    (stagingClimateStep.HasValue && stagingClimateStep.Value!=(long)Math.Floor(world.Day*4))))
                    pendingStrip=-1;
                if(pendingStrip>=0){RenderColumnStrip();return;}
                bool climateChanged=climateStartDay!=season.SegmentStartDay||revision!=world.TerrainRevision;
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
                if(climateChanged)UploadClimate(season);
                regionalClimate.Bind(bake);
                stagingClimateStep=regionalClimate.Step;stagingRevision=world.TerrainRevision;
                StageCloudWind();
                StageCyclones(calendarDay);
                bake.SetFloat("_VisualTime",(float)Clock);
                bake.SetFloat("_MonthBlend",season.SegmentBlend);
                hpColumn.SetFloat("_MonthBlend",season.SegmentBlend);
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
            target.SetFloat("_VegetationWindBlend",windBlend);
            target.SetVector("_CycloneNorth",visibleNorth);target.SetVector("_CycloneSouth",visibleSouth);
            target.SetFloat("_FlatCloudShadows",shadows?1:0);target.SetFloat("_FlatAtmosphere",atmosphere?1:0);
            target.SetFloat("_FlatDayNight",dayNight?1:0);target.SetFloat("_FlatRain",rain?1:0);target.SetFloat("_FlatLightning",lightning?1:0);
            target.SetFloat("_FlatNightLights",nightLights?1:0);target.SetFloat("_CloudOpacity",Mathf.Clamp01(opacity));
            target.SetVectorArray("_OrbisLightning",flashes);
        }
        void OnDestroy()
        {Shader.SetGlobalVector("_OrbisCycloneNorth",Vector4.zero);Shader.SetGlobalVector("_OrbisCycloneSouth",Vector4.zero);Shader.SetGlobalVector("_OrbisCyclonePhase",Vector4.zero);regionalClimate?.Dispose();ReleaseCache(weatherBack);ReleaseCache(columnBack);ReleaseCache(columnStrip);ReleaseCache(Texture);ReleaseCache(ColumnTexture);if(climate)Destroy(climate);if(glebaForcing)Destroy(glebaForcing);if(glebaAux)Destroy(glebaAux);if(glebaUpperWind)Destroy(glebaUpperWind);if(wind)Destroy(wind);if(windBack)Destroy(windBack);if(climateWind)Destroy(climateWind);if(bake)Destroy(bake);if(hpColumn)Destroy(hpColumn);if(hpNoise)Destroy(hpNoise);if(hpDetail)Destroy(hpDetail);if(hpHigh)Destroy(hpHigh);}
    }
}
