using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
namespace Orbis.Editor
{
 public static class ClimateCloudRegression
 {
  public static void Run()
  {
   var report=new List<string>();var owned=new List<UnityEngine.Object>();var previous=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   var rt=RenderTexture.GetTemporary(128,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   try
   {
    var cloud=new Material(Resources.Load<Shader>("ORBIS/FlatWorld/FlatSkyBake"));owned.Add(cloud);
    var ecotone=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/EcotoneProbe.shader"));owned.Add(ecotone);
    foreach(var m in new[]{cloud,ecotone}){ShaderUtil.CompilePass(m,0,true);foreach(var e in ShaderUtil.GetShaderMessages(m.shader))if(e.severity.ToString()=="Error")throw new Exception(e.message);}
    var read=new Texture2D(128,64,TextureFormat.RGBAFloat,false,true);owned.Add(read);
    Func<Material,Color[]> render=m=>{Graphics.Blit(Texture2D.blackTexture,rt,m);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,128,64),0,0);read.Apply();return read.GetPixels();};
    cloud.SetTexture("_ClimateTex",Texture2D.blackTexture);cloud.SetTexture("_ClimateWindTex",Texture2D.blackTexture);var dry=render(cloud);
    cloud.SetTexture("_ClimateTex",Texture2D.whiteTexture);var wet=render(cloud);
    double dryCloud=0,wetCloud=0;for(int i=0;i<wet.Length;i++){dryCloud+=dry[i].r+dry[i].g;wetCloud+=wet[i].r+wet[i].g;}
    if(wetCloud<100||wetCloud<dryCloud*1.15)throw new Exception("Visual climate response dry="+dryCloud+" wet="+wetCloud);
    report.Add("PASS monthly rainfall modulates visual clouds; dry/wet sum="+dryCloud+"/"+wetCloud);
    var evolving=render(cloud);int occupied=0;foreach(var pixel in evolving)if(pixel.r>.25f)occupied++;
    float fraction=occupied/(float)evolving.Length;
    float smallest=1,largest=0;foreach(var pixel in evolving){smallest=Mathf.Min(smallest,pixel.r);largest=Mathf.Max(largest,pixel.r);}
    if(fraction<.12f||largest-smallest<.12f)throw new Exception("Cloud mass lost spatial shape: "+fraction+" range="+(largest-smallest));
    report.Add("PASS coherent weather mass with spatial structure; occupied fraction="+fraction+" range="+(largest-smallest));
    cloud.SetFloat("_VisualTime",40);var middle=render(cloud);double evolved=0;
    for(int i=0;i<evolving.Length;i++)evolved+=Math.Abs(evolving[i].r-middle[i].r);
    evolved/=evolving.Length;
    if(evolved<.025)throw new Exception("Cloud mask remains fixed through a weather era: "+evolved);
    cloud.SetFloat("_VisualTime",79.999f);var beforeWrap=render(cloud);
    cloud.SetFloat("_VisualTime",80.001f);var afterWrap=render(cloud);double seamTime=0;
    for(int i=0;i<beforeWrap.Length;i++)seamTime+=Math.Abs(beforeWrap[i].r-afterWrap[i].r);
    seamTime/=beforeWrap.Length;
    if(seamTime>.005)throw new Exception("Cloud mask pops at weather era boundary: "+seamTime);
    report.Add("PASS evolving cloud shape and continuous weather-era boundary; change="+evolved+" boundary="+seamTime);
    ValidateCycloneClouds(cloud,report);
    ecotone.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));ecotone.SetFloat("_ProbeShore",1);var water=render(ecotone);
    ecotone.SetFloat("_ProbeShore",-4);var land=render(ecotone);float warp=0,seam=0,waterMotion=0;
    for(int i=0;i<land.Length;i++){warp=Mathf.Max(warp,land[i].r);seam=Mathf.Max(seam,land[i].g);waterMotion=Mathf.Max(waterMotion,water[i].r);}
    if(warp<.001f||waterMotion>1e-5f||seam>1e-5f)throw new Exception("Ecotone warp/seam/water "+warp+" "+seam+" "+waterMotion);
    report.Add("PASS spherical ecotone noise, continuous date-line, unchanged water coordinates; displacement="+warp+" seam="+seam);
    var shape=OrbisCloudNoise.Create();var detail=OrbisCloudNoise.CreateDetail();var ice=OrbisCloudNoise.CreateHighDetail();
    owned.Add(shape);owned.Add(detail);owned.Add(ice);
    if(shape.width!=128||detail.width!=64)throw new Exception("Wrong cloud volume resolution");
    report.Add("PASS independent 128^3 shape / 64^3 detail resources loaded");
    var column=new Material(Resources.Load<Shader>("ORBIS/FlatWorld/HPCloudColumn"));owned.Add(column);
    ShaderUtil.CompilePass(column,0,true);
    column.SetTexture("_CloudNoise",shape);column.SetTexture("_CloudDetailNoise",detail);column.SetTexture("_HPHighDetail",ice);
    column.SetTexture("_ScrollHeightTex",Texture2D.blackTexture);column.SetTexture("_ScrollCloudWindTex",Texture2D.blackTexture);
    column.SetFloat("_CloudHeightScale",OrbisWeatherField.HeightScale);column.SetFloat("_ScrollHeightScale",0);
    column.SetVector("_ScrollSourceSize",new Vector4(512,256,0,0));column.SetFloat("_Density",1);column.SetFloat("_Cover",1);
    var weather=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);owned.Add(weather);
    var climate=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);owned.Add(climate);
    Action<float> setTemperature=celsius=>{float value=(celsius+25)/65;var c=new Color(1,1,value,value);climate.SetPixels(new[]{c,c,c,c});climate.Apply();column.SetTexture("_ClimateTex",climate);};
    setTemperature(15);
    Action<Color> setWeather=c=>{weather.SetPixels(new[]{c,c,c,c});weather.Apply();column.SetTexture("_ScrollWeatherTex",weather);};
    setWeather(new Color(0,0,1,1));var clearColumn=render(column);
    foreach(var p in clearColumn)if(p.r+p.g+p.a>1e-5f)throw new Exception("Rain or shadow exists outside cloud support");
    setWeather(new Color(.12f,.08f,.15f,1));var thinColumn=render(column);
    foreach(var p in thinColumn)if(p.a>1e-5f)throw new Exception("Thin cloud produces visible heavy rain");
    setWeather(new Color(0,.34f,.18f,0));var iceOnly=render(column);int translucent=0;
    foreach(var p in iceOnly)
    {
     if(p.r+p.b+p.a>.0001f)throw new Exception("Isolated cirrus adds low cloud or precipitation");
     if(p.g>.02f&&p.g<.90f)translucent++;
    }
    CheckRenderState(report,translucent>iceOnly.Length*.70f,"translucent high cloud above clear lower air; pixels="+translucent);
    setWeather(new Color(1,.5f,1,1));var stormColumn=render(column);
    int thick=0,raining=0;double shadowMean=0;
    foreach(var p in stormColumn){shadowMean+=p.r;if(p.r>.65f)thick++;if(p.a>.05f)raining++;if(p.a>.001f&&p.r<.55f)throw new Exception("Rain escapes optically thick cloud");}
    if(thick<stormColumn.Length*.15f||raining<stormColumn.Length*.05f)throw new Exception("Cumulonimbus missing visible thickness/rain: "+thick+"/"+raining);
    report.Add("PASS clear/thin/dense cloud rain gating and shadow column; thick="+thick+" rain="+raining+" mean="+shadowMean/stormColumn.Length);
    setTemperature(-12);var snowColumn=render(column);int snowing=0;
    foreach(var p in snowColumn){if(p.a>1e-5f)throw new Exception("Cold precipitation rendered as rain");if(p.b>.05f)snowing++;}
    if(snowing<snowColumn.Length*.05f)throw new Exception("Cold precipitation lost in cloud cache");
    setWeather(new Color(0,0,0,0));
    foreach(var p in render(column))if(p.b>1e-5f)throw new Exception("Snow escapes cloud support");
    report.Add("PASS temperature-dependent visual rain/snow and clear-sky exclusion; snow pixels="+snowing);
    ValidateSurfacePrecipitation(report,render);
    setWeather(new Color(1,.5f,1,1));
    var fullColumn=render(column);
    var strip=new RenderTexture(128,16,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);strip.Create();owned.Add(strip);
    for(int band=0;band<4;band++)
    {
     column.SetVector("_ColumnUVTransform",new Vector4(0,band*.25f,1,.25f));
     Graphics.Blit(Texture2D.blackTexture,strip,column);
     Graphics.CopyTexture(strip,0,0,0,0,128,16,rt,0,0,0,band*16);
    }
    RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,128,64),0,0);read.Apply();var sliced=read.GetPixels();float sliceError=0;
    for(int i=0;i<sliced.Length;i++)sliceError=Mathf.Max(sliceError,Mathf.Abs(sliced[i].r-fullColumn[i].r),Mathf.Abs(sliced[i].b-fullColumn[i].b));
    column.SetVector("_ColumnUVTransform",new Vector4(0,0,1,1));
    if(sliceError>.0001f)throw new Exception("Cloud update slices do not match full render: "+sliceError);
    report.Add("PASS amortized four-slice cache matches complete GPU render; max error="+sliceError);
    var passage=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/CloudPassageProbe.shader"));owned.Add(passage);
    ShaderUtil.CompilePass(passage,0,true);
    passage.CopyPropertiesFromMaterial(column);passage.SetTexture("_ScrollWeatherTex",weather);
    passage.SetFloat("_Clouds",1);passage.SetFloat("_ScrollCloudSource",1);passage.SetFloat("_ScrollVolumeWeight",1);
    passage.SetFloat("_LocalCloudMap",1);passage.SetFloat("_Atmosphere",0);passage.SetFloat("_AirDensity",0);
    passage.SetInt("_CloudSteps",64);passage.SetInt("_AirSteps",16);passage.SetInt("_LightSteps",4);
    passage.SetFloat("_CloudRefinement",2);passage.SetFloat("_CloudLightSimple",1);
    passage.SetVector("_CloudLocalUp",Vector3.forward);passage.SetVector("_CloudLocalEast",Vector3.right);passage.SetVector("_CloudLocalNorth",Vector3.up);
    passage.SetVector("_Sun",Vector3.forward);new OrbisAtmosphereProfile().Bind(passage,1);
    Func<float,double> transmission=height=>{passage.SetFloat("_CloudCameraHeight",height);var pixels=render(passage);double total=0;foreach(var pixel in pixels){if(float.IsNaN(pixel.r))throw new Exception("Invalid cloud passage ray");total+=pixel.r;}return total/pixels.Length;};
    double above=transmission(14500),inside=transmission(2500),below=transmission(150);
    if(above>=inside||inside>=below||below<.999)throw new Exception("Cloud passage must clear below cloud base: "+above+"/"+inside+"/"+below);
    report.Add("PASS same cloud clears as camera crosses below its base; transmission above/inside/below="+above+"/"+inside+"/"+below);
    ValidateRenderState(report);
   }
   catch(Exception e){report.Add("FAIL "+e.Message);throw;}
   finally
   {
    Directory.CreateDirectory("Documentation/ClimateCloudUpgrade");File.WriteAllLines("Documentation/ClimateCloudUpgrade/gpu-regression.txt",report);
    ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);foreach(var o in owned)UnityEngine.Object.DestroyImmediate(o);
   }
  }

  static void ValidateCycloneClouds(Material cloud,List<string> report)
  {
   const int width=1024,height=512,samples=96;
   var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
   var previous=RenderTexture.active;
   var read=new Texture2D(width,height,TextureFormat.RGBA32,false,true){wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
   try
   {
    void Bake()
    {
     Graphics.Blit(Texture2D.blackTexture,target,cloud);RenderTexture.active=target;
     read.ReadPixels(new Rect(0,0,width,height),0,0);read.Apply();
    }
    foreach(int spin in new[]{1,-1})
    {
     float latitude=spin*.34f;
     var axis=new Vector3(0,Mathf.Sin(latitude),Mathf.Cos(latitude));
     var north=Vector3.Cross(axis,Vector3.right);
     string property=spin==1?"_CycloneNorth":"_CycloneSouth";
     float Density(float radius,float bearing)
     {
      var local=Vector3.right*Mathf.Cos(bearing)+north*Mathf.Sin(bearing);
      var direction=axis*Mathf.Cos(radius)+local*Mathf.Sin(radius);
      return read.GetPixelBilinear(.5f+Mathf.Atan2(direction.x,direction.z)/(2*Mathf.PI),.5f+Mathf.Asin(direction.y)/Mathf.PI).r;
     }
     cloud.SetVector("_CyclonePhase",Vector4.zero);Bake();
     float calm=Density(0,0);
     cloud.SetVector(property,new Vector4(axis.x,axis.y,axis.z,.03f));Bake();
     CheckRenderState(report,Mathf.Abs(calm-Density(0,0))<.02f,"incipient disturbance has no painted eye, hemisphere="+spin);
     cloud.SetVector(property,new Vector4(axis.x,axis.y,axis.z,.94f));Bake();
     float eye=Density(0,0),wall=0,min=1,max=0;
     var band=new float[samples];double inward=0,outward=0;
     for(int i=0;i<samples;i++)
     {
      float a=i*2*Mathf.PI/samples;
      wall+=Density(.027f,a)/samples;band[i]=Density(.097f,a);
      min=Mathf.Min(min,band[i]);max=Mathf.Max(max,band[i]);
      for(int j=0;j<3;j++)
      {
       float radius=.067f+j*.024f,inner=Density(radius,a);
       inward+=Mathf.Abs(inner-Density(radius+.024f,a-spin*.44f));
       outward+=Mathf.Abs(inner-Density(radius+.024f,a+spin*.44f));
      }
     }
     CheckRenderState(report,wall-eye>.22f&&max-min>.35f,
      "mature eye, eyewall and broken outer bands, hemisphere="+spin+" eye/wall="+eye.ToString("F2")+"/"+wall.ToString("F2"));
     CheckRenderState(report,inward<outward*.91,"rainbands wrap inward in the hemispheric circulation, error="+inward.ToString("F1")+"/"+outward.ToString("F1"));
     float turn=spin*Mathf.PI/3;
     cloud.SetVector("_CyclonePhase",spin==1?new Vector4(turn,0,0,0):new Vector4(0,turn,0,0));Bake();
     double same=0,rotated=0;
     for(int i=0;i<samples;i++)
     {
      float a=i*2*Mathf.PI/samples;
      same+=Mathf.Abs(band[i]-Density(.097f,a));rotated+=Mathf.Abs(band[i]-Density(.097f,a+turn));
     }
     CheckRenderState(report,same>samples*.07&&rotated<same*.75,"cloud bands follow the signed simulated rotation, error="+rotated.ToString("F1")+"/"+same.ToString("F1"));
     cloud.SetVector(property,Vector4.zero);
    }
   }
   finally
   {
    cloud.SetVector("_CyclonePhase",Vector4.zero);
    RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(read);
   }
  }

  static void ValidateSurfacePrecipitation(List<string> report,Func<Material,Color[]> render)
  {
   var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/FlatNightProbe.shader"));
   var cloud=new Texture2D(2,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
   var wind=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
   try
   {
    ShaderUtil.CompilePass(probe,0,true);
    probe.SetTexture("_SkyTex",cloud);probe.SetTexture("_VegetationWindTex",wind);
    probe.SetFloat("_FlatClouds",1);probe.SetFloat("_SkyReady",1);probe.SetFloat("_FlatRain",1);
    probe.SetFloat("_VegetationWindReady",1);probe.SetFloat("_FlatGeographicChart",1);
    probe.SetFloat("_ProbePrecipitation",2);probe.SetFloat("_SkyClock",3.7f);
    probe.SetVector("_FlatSun",Vector3.forward);
    probe.SetVector("_MapSize",new Vector4(64,32,1f/64,1f/32));
    var area=new Vector4(30,14,4,2);probe.SetVector("_ProbeArea",area);
    var storm=new Color(1,.35f,0,1);var snow=new Color(1,.35f,1,0);
    Action<Color,Color> sky=(a,b)=>{cloud.SetPixels(new[]{a,b});cloud.Apply();};
    Action<float> breeze=x=>{wind.SetPixel(0,0,new Color(x,.25f,x,.25f));wind.Apply();};
    Func<Color[],int,float> sum=(pixels,c)=>{float value=0;foreach(var p in pixels)value+=Mathf.Abs(p[c]);return value;};
    Func<Color[],Color[],int,float> difference=(a,b,c)=>{float value=0;for(int i=0;i<a.Length;i++)value+=Mathf.Abs(a[i][c]-b[i][c]);return value;};
    breeze(1.3f);sky(storm,storm);var first=render(probe);var repeat=render(probe);
    CheckRenderState(report,sum(first,0)>1&&sum(first,1)>.1f&&sum(first,2)>1&&sum(first,3)==0,
     "warm storm produces falling drops, spray and water rings");
    CheckRenderState(report,difference(first,repeat,0)+difference(first,repeat,2)==0,"paused storm is stationary");
    probe.SetVector("_ProbeArea",area+new Vector4(1,0,0,0));var panned=render(probe);float panError=0;
    for(int y=0;y<64;y++)for(int x=0;x<96;x++){
     var d=first[y*128+x+32]-panned[y*128+x];panError+=Mathf.Abs(d.r)+Mathf.Abs(d.b);
    }
    CheckRenderState(report,panError/(96*64)<.0001f,"drops and impacts stay on the same terrain when panning");
    probe.SetVector("_ProbeArea",area+new Vector4(64,0,0,0));var wrapped=render(probe);
    CheckRenderState(report,(difference(first,wrapped,0)+difference(first,wrapped,2))/first.Length<.0001f,
     "rain particles and landing sites wrap at the date line");
    probe.SetVector("_ProbeArea",area);breeze(-1.3f);var reversed=render(probe);
    CheckRenderState(report,difference(first,reversed,0)>1&&difference(first,reversed,2)<.001f,
     "seasonal wind redirects incoming drops while their impact centres stay fixed");
    var centre=Flat.FlatWeatherSimulation.Direction(.480,.477);
    probe.SetVector("_CycloneSouth",new Vector4((float)centre.X,(float)centre.Y,(float)centre.Z,.9f));
    var rotatingRain=render(probe);
    CheckRenderState(report,difference(reversed,rotatingRain,0)>1&&difference(reversed,rotatingRain,2)<.001f,
     "simulated cyclone wind redirects local rain without moving ground impacts");
    probe.SetVector("_CycloneSouth",Vector4.zero);
    probe.SetFloat("_SkyClock",3.84f);var later=render(probe);
    CheckRenderState(report,difference(first,later,2)>1,"surface impacts expand and fade over time");
    sky(new Color(.12f,.1f,0,1),storm);var boundary=render(probe);int hits=0;
    for(int y=0;y<64;y++)for(int x=0;x<128;x++){
     var v=boundary[y*128+x];if(x<64&&v.maxColorComponent>0)throw new Exception("rain crosses the clear side of the cloud boundary");
     if(x>=64&&v.maxColorComponent>.05f)hits++;
    }
    CheckRenderState(report,hits>8,"rain is local to thick cloud on one side of a storm front");
    sky(snow,snow);var flakes=render(probe);
    CheckRenderState(report,sum(flakes,0)+sum(flakes,1)+sum(flakes,2)==0&&sum(flakes,3)>1,
     "cold storm draws drifting flakes without liquid streaks or surface ripples");
    sky(storm,storm);probe.SetFloat("_ProbePrecipitation",1);probe.SetFloat("_ProbeWater",1);
    var lake=render(probe);probe.SetFloat("_ProbeIce",1);var ice=render(probe);
    probe.SetFloat("_ProbeWater",0);var frozenSoil=render(probe);probe.SetFloat("_ProbeIce",0);var soil=render(probe);
    CheckRenderState(report,difference(lake,ice,0)>.2f&&difference(soil,frozenSoil,0)>.2f&&difference(ice,frozenSoil,0)==0,
     "liquid water ripples, soil wets and splashes, ice receives only airborne drops");
   }
   finally{UnityEngine.Object.DestroyImmediate(probe);UnityEngine.Object.DestroyImmediate(cloud);UnityEngine.Object.DestroyImmediate(wind);}
  }

  static void ValidateRenderState(List<string> report)
  {
   if(!Application.isPlaying)return;
   var map=UnityEngine.Object.FindFirstObjectByType<Flat.FlatMapRenderer>();
   if(!map||map.World==null)return;
   var sky=map.Sky;
   var previous=RenderTexture.active;bool previousSRGB=GL.sRGBWrite;int previousWidth=sky.CacheWidth;
   var receiver=RenderTexture.GetTemporary(16,16,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
   var releaseWarnings=new List<string>();
   Application.LogCallback capture=(message,stack,type)=>
   {
    if(message.IndexOf("Releasing render texture",StringComparison.OrdinalIgnoreCase)>=0)releaseWarnings.Add(message);
   };
   Application.logMessageReceived+=capture;
   try
   {
    foreach(bool srgb in new[]{false,true})
    {
     RenderTexture.active=receiver;GL.sRGBWrite=srgb;
     for(int frame=0;frame<8;frame++)
     {
      sky.Tick(.6f,true,true,false,sky.CacheWidth,map.World.Day);
      CheckRenderState(report,RenderTexture.active==receiver&&GL.sRGBWrite==srgb,
       "cloud bake preserves caller render target and sRGB state, frame="+frame+" sRGB="+srgb);
     }
    }
    for(int kind=0;kind<3;kind++)
    {
     var owned=kind==0?sky.Texture:kind==1?sky.ColumnTexture:
      (RenderTexture)typeof(Flat.FlatSkyRenderer).GetField("columnStrip",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(sky);
     RenderTexture.active=owned;
     int nextWidth=sky.CacheWidth==512?1024:512;
     sky.Tick(.6f,true,true,false,nextWidth,map.World.Day);
     CheckRenderState(report,RenderTexture.active==null&&!owned.IsCreated()&&sky.CacheWidth==nextWidth,
      "cloud quality change detaches an active owned texture before release, target="+kind);
     CheckRenderState(report,sky.Texture.width==2*nextWidth&&sky.ColumnTexture.width==nextWidth,
      "higher-resolution cloud cover shares the same spherical extent as the shadow columns, width="+nextWidth);
    }
    CheckRenderState(report,releaseWarnings.Count==0,"cloud quality changes release active caches without warnings");
   }
   finally
   {
    Application.logMessageReceived-=capture;
    RenderTexture.active=receiver;GL.sRGBWrite=false;
    sky.Tick(0,true,false,false,previousWidth,map.World.Day);
    for(int frame=0;frame<4;frame++)sky.Tick(0,true,false,false,previousWidth,map.World.Day);
    RenderTexture.active=previous;GL.sRGBWrite=previousSRGB;RenderTexture.ReleaseTemporary(receiver);
   }
  }

  static void CheckRenderState(List<string> report,bool passed,string message)
  {
   report.Add((passed?"PASS ":"FAIL ")+message);
   if(!passed)throw new InvalidOperationException(message);
  }
 }
}
