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
    ecotone.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));ecotone.SetFloat("_ProbeShore",1);var water=render(ecotone);
    ecotone.SetFloat("_ProbeShore",-4);var land=render(ecotone);float warp=0,seam=0,waterMotion=0;
    for(int i=0;i<land.Length;i++){warp=Mathf.Max(warp,land[i].r);seam=Mathf.Max(seam,land[i].g);waterMotion=Mathf.Max(waterMotion,water[i].r);}
    if(warp<.001f||waterMotion>1e-5f||seam>1e-5f)throw new Exception("Ecotone warp/seam/water "+warp+" "+seam+" "+waterMotion);
    report.Add("PASS spherical ecotone noise, continuous date-line, unchanged water coordinates; displacement="+warp+" seam="+seam);
    var shape=OrbisCloudNoise.Create();var detail=OrbisCloudNoise.CreateDetail();owned.Add(shape);owned.Add(detail);
    if(shape.width!=128||detail.width!=64)throw new Exception("Wrong cloud volume resolution");
    report.Add("PASS independent 128^3 shape / 64^3 detail resources loaded");
    var column=new Material(Resources.Load<Shader>("ORBIS/FlatWorld/HPCloudColumn"));owned.Add(column);
    ShaderUtil.CompilePass(column,0,true);
    column.SetTexture("_CloudNoise",shape);column.SetTexture("_CloudDetailNoise",detail);
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
