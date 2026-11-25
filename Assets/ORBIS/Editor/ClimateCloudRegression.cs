using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
namespace Orbis.Editor
{
 public static class ClimateCloudRegression
 {
  // Run once the default natural view has published a sky. Re-bake the same
  // shader at fixed time with/without its actual regional field; leave the live
  // cache, camera, calendar, materials and world untouched.
  [MenuItem("Tools/ORBIS/Climate/Validate published regional atmosphere")]
  public static void RegionalPublication()
  {
   var app=UnityEngine.Object.FindFirstObjectByType<Flat.FlatWorldApp>();
   var map=app?app.GetComponentInChildren<Flat.FlatMapRenderer>():null;
   var globe=app?app.GetComponentInChildren<Flat.FlatGlobePresenter>():null;
   if(!map||!globe||!map.Sky.Ready||map.Sky.RegionalClimateStep!=(long)Math.Floor(map.World.Day*4))
    throw new InvalidOperationException("Wait for the natural map to publish the current regional sky.");
   var sky=map.Sky;
   if(globe.View.CloudMaterial.GetTexture("_ScrollWeatherTex")!=sky.Texture)
    throw new InvalidOperationException("The planet's HP cloud material is not bound to the published regional weather.");
   if(app.Navigation.SurfaceMaterial.GetTexture("_SkyTex")!=sky.ColumnTexture)
    throw new InvalidOperationException("The local terrain is not bound to the published HP cloud columns.");
   var field=typeof(Flat.FlatSkyRenderer).GetField("bake",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
   var material=new Material((Material)field.GetValue(sky));
   var previous=RenderTexture.active;bool originalSRGB=GL.sRGBWrite;
   var rt=RenderTexture.GetTemporary(256,128,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
   var read=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false,true);
   try
   {
    var regional=material.GetTexture("_RegionalCloudResponse");
    if(!regional||regional.width!=64||regional.height!=32)throw new Exception("Missing published regional air grid");
    Color32[] Bake(){
     GL.sRGBWrite=false;Graphics.Blit(Texture2D.blackTexture,rt,material);
     RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);read.Apply();return read.GetPixels32();
    }
    var active=Bake();
    material.SetTexture("_RegionalCloudResponse",Texture2D.blackTexture);material.SetVector("_RegionalCloudPoles",Vector4.zero);
    material.SetFloat("_RegionalEvapReady",0f);
    var neutral=Bake();int changed=0,thicker=0,thinner=0;
    for(int i=0;i<active.Length;i++){
     int d=active[i].r-neutral[i].r;
     if(Math.Abs(d)>1)changed++;if(d>1)thicker++;if(d< -1)thinner++;
    }
    if(thicker==0||thinner==0)throw new Exception("The naturally generated regional field made no bidirectional difference to the rendered sky.");
    Directory.CreateDirectory("ValidationOutput/regional-atmosphere");
    File.WriteAllText("ValidationOutput/regional-atmosphere/publication.txt",
     $"PASS {Application.unityVersion} {SystemInfo.graphicsDeviceName}, weather step {sky.RegionalClimateStep}, region {regional.width}x{regional.height}, fixed-time low-cloud change >1/255: {changed}/{active.Length}, thicker={thicker}, thinner={thinner}. Both HP and local terrain bindings verified.");
   }
   finally
   {
    RenderTexture.active=previous;GL.sRGBWrite=originalSRGB;RenderTexture.ReleaseTemporary(rt);
    UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(material);
   }
  }

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
    ValidateRegionalAtmosphere(cloud,render,report);
    ValidateCycloneClouds(cloud,report);
    ecotone.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));ecotone.SetFloat("_ProbeShore",1);var water=render(ecotone);
    ecotone.SetFloat("_ProbeShore",-4);var land=render(ecotone);float warp=0,seam=0,waterMotion=0;
    for(int i=0;i<land.Length;i++){warp=Mathf.Max(warp,land[i].r);seam=Mathf.Max(seam,land[i].g);waterMotion=Mathf.Max(waterMotion,water[i].r);}
    if(warp<.001f||waterMotion>1e-5f||seam>1e-5f)throw new Exception("Ecotone warp/seam/water "+warp+" "+seam+" "+waterMotion);
    report.Add("PASS spherical ecotone noise, continuous date-line, unchanged water coordinates; displacement="+warp+" seam="+seam);
    CheckCoastalEcotone(ecotone,render,owned,report);
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
    ValidateRegionalCadence(report);
   }
   catch(Exception e){report.Add("FAIL "+e.Message);throw;}
   finally
   {
    Directory.CreateDirectory("Documentation/ClimateCloudUpgrade");File.WriteAllLines("Documentation/ClimateCloudUpgrade/gpu-regression.txt",report);
    ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);foreach(var o in owned)UnityEngine.Object.DestroyImmediate(o);
   }
  }

  static void CheckCoastalEcotone(Material probe,Func<Material,Color[]> render,List<UnityEngine.Object> owned,List<string> report)
  {
   // A north coast in a 4096-column world. At this dry delta site the
   // production spherical warp crosses several cells before sampling colour.
   const int rows=2048,coastRow=1381;
   var terrain=new Texture2D(1,rows,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
   var visual=new Texture2D(1,rows,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
   owned.Add(terrain);owned.Add(visual);
   var albedo=new Color[rows];var field=new Color[rows];
   for(int y=0;y<rows;y++)
   {
    albedo[y]=y>=coastRow+2?Color.blue:Color.green;
    field[y]=new Color(0,Mathf.Clamp(y-coastRow+.5f,-8,8),0,0);
   }
   terrain.SetPixels(albedo);terrain.Apply();visual.SetPixels(field);visual.Apply();
   probe.SetTexture("_ProbeTerrain",terrain);probe.SetTexture("_VisualTex",visual);
   probe.SetVector("_MapSize",new Vector4(4096,rows,1f/4096,1f/rows));
   probe.SetVector("_ProbeCell",new Vector4((30.85f/360+.5f)*4096,(31.16f/180+.5f)*rows,1,0));
   probe.SetFloat("_ProbeShore",-2);
   var dry=render(probe)[0];
   CheckRenderState(report,dry.g>.99f && dry.b<.01f,"delta land retains its terrestrial albedo when the ecotone reaches offshore");
   probe.SetVector("_ProbeCell",new Vector4(2400.5f,1385.5f,1,0));probe.SetFloat("_ProbeShore",4);
   var sea=render(probe)[0];
   CheckRenderState(report,sea.b>.99f && sea.g<.01f,"a wet receiver retains its water-albedo coordinate");
   // Land reclaimed at the destination must immediately participate in the
   // ecological blend. Keep a distinct terrestrial colour there to detect it.
   for(int y=0;y<rows;y++){field[y].g=-8;if(y>=coastRow+2)albedo[y]=Color.red;}
   visual.SetPixels(field);visual.Apply();terrain.SetPixels(albedo);terrain.Apply();
   probe.SetVector("_ProbeCell",new Vector4((30.85f/360+.5f)*4096,(31.16f/180+.5f)*rows,1,0));
   probe.SetFloat("_ProbeShore",-8);
   var inland=render(probe)[0];
   CheckRenderState(report,inland.r>.99f && inland.g<.01f,"edited inland terrain retains its warped land-to-land blend");
  }
  static void ValidateRegionalCadence(List<string> report)
  {
   if(!Application.isPlaying)return;
   var root=new GameObject("Regional cloud cache regression");
   try
   {
    var world=Flat.FlatGenerator.Generate(64,32,"CLOUD-CACHE-CADENCE",Flat.TerrainPreset.Continents,default);
    var sim=new Flat.FlatSimulation(world);
    var sky=root.AddComponent<Flat.FlatSkyRenderer>();sky.Initialize(world);
    void Publish(){
     long step=(long)Math.Floor(world.Day*4);
     var timer=System.Diagnostics.Stopwatch.StartNew();
     while(sky.RegionalClimateStep!=step && timer.Elapsed.TotalSeconds<10){
      var weather=sky.Texture;var column=sky.ColumnTexture;
      sky.Tick(0,true,false,false,512,world.Day);
      if((sky.Texture!=weather)!=(sky.ColumnTexture!=column))throw new Exception("Regional weather and HP column switched in different frames");
      System.Threading.Thread.Sleep(1);
     }
     if(sky.RegionalClimateStep!=step)throw new Exception("Paused sky did not publish the requested climate step "+step);
    }
    Publish();var previous=sky.Texture;var previousStep=sky.RegionalClimateStep;
    var savedDay=world.Day;var savedStorms=world.Cyclones?.Clone();
    sim.Step(.25f);
    for(int i=0;i<8;i++)sky.Tick(.1f,false,true,false,512,world.Day);
    CheckRenderState(report,previous==sky.Texture && previousStep==sky.RegionalClimateStep,"hidden clouds retain the last complete field");
    Publish();
    sim.Step(.25f);sky.Tick(.6f,true,true,false,512,world.Day);
    world.Day=savedDay;world.Cyclones=savedStorms;world.TerrainRevision++;
    CheckRenderState(report,!sky.RegionalPrecipitationReady && !sky.TrySampleRegionalPrecipitation(.5,.5,out _),
     "terrain revision immediately invalidates stale regional precipitation readout until rebuilt strips commit");
    Publish();
    CheckRenderState(report,sky.Ready,"paused resume and rewind discard stale regional work and publish complete weather/column pairs");
    ValidateRegionalPrecipitationStripSync(world,sky,report);
   }
   finally {UnityEngine.Object.DestroyImmediate(root);}
  }

  static void ValidateRegionalPrecipitationStripSync(Flat.WorldData world,Flat.FlatSkyRenderer sky,List<string> report)
  {
   const System.Reflection.BindingFlags priv=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   var pendingStripField=typeof(Flat.FlatSkyRenderer).GetField("pendingStrip",priv);
   var hpColumnField=typeof(Flat.FlatSkyRenderer).GetField("hpColumn",priv);
   var bakeField=typeof(Flat.FlatSkyRenderer).GetField("bake",priv);
   var hpMat=(Material)hpColumnField.GetValue(sky);
   var bakeMat=(Material)bakeField.GetValue(sky);
   // Use the column shader's declared diagnostic properties to probe the published binding.
   var publishedProbe=new Material(hpMat.shader);
   var gpuRt=RenderTexture.GetTemporary(64,32,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var gpuRead=new Texture2D(64,32,TextureFormat.RGBAFloat,false,true);
   var prevRt=RenderTexture.active;bool prevSrgb=GL.sRGBWrite;
   try
   {
    Color SampleGpuTexel(Texture tex,int x,int y)
    {
     if(!tex)throw new InvalidOperationException("Missing bound regional precipitation texture");
     GL.sRGBWrite=false;Graphics.Blit(tex,gpuRt);RenderTexture.active=gpuRt;
     gpuRead.ReadPixels(new Rect(0,0,64,32),0,0);gpuRead.Apply(false,false);
     return gpuRead.GetPixel(x,y);
    }
    void PublishAt(double day)
    {
     world.Day=day;
     long targetStep=(long)Math.Floor(day*4);
     var sw=System.Diagnostics.Stopwatch.StartNew();
     while((sky.RegionalClimateStep!=targetStep || (int)pendingStripField.GetValue(sky)>=0 || Math.Abs(sky.PublishedCalendarDay-day)>1e-6) && sw.Elapsed.TotalSeconds<10)
     {
      var w0=sky.Texture;var c0=sky.ColumnTexture;var p0=sky.RegionalPrecipitationTexture;
      sky.Tick(0,true,false,false,512,day);
      bool skySwapped=sky.Texture!=w0;
      if(skySwapped!=(sky.ColumnTexture!=c0))throw new Exception("Weather and HP column swapped on different ticks");
      if((sky.RegionalPrecipitationTexture!=p0 && p0!=null && sky.RegionalPrecipitationTexture!=null) && !skySwapped)
       throw new Exception("Published precipitation texture changed before final HP column strip swapped");
      System.Threading.Thread.Sleep(1);
     }
     if(sky.RegionalClimateStep!=targetStep)throw new Exception("Timed out publishing step "+targetStep);
    }

    // Deterministically pick a natural cell transitioning from active rain-out (wetDay) to dry G=0 (dryDay) within segment [0, 15)
    var budget=new Flat.FlatMoistureCloudBudget();
    double wetDay=2.0,dryDay=6.5;
    int pickCell=-1;
    for(int dOff=0;dOff<18 && pickCell<0;dOff++)
    {
     double candWet=1.0+dOff*0.5,candDry=candWet+4.0;
     budget.Solve(world,candWet,Flat.FlatCycloneWeather.SpinUpAt(world,candWet));
     var wetRain=(float[])budget.CloudRainOutKgM2Per6h.Clone();
     var wetSfc=(float[])budget.SurfacePrecipKgM2Per6h.Clone();
     budget.Solve(world,candDry,Flat.FlatCycloneWeather.SpinUpAt(world,candDry));
     for(int i=64;i<Flat.FlatMoistureCloudBudget.Count-64;i++)
     {
      if(wetRain[i]>0.5f && wetSfc[i]>0.35f && budget.CloudRainOutKgM2Per6h[i]==0f && budget.SurfacePrecipKgM2Per6h[i]==0f)
      {
       pickCell=i;wetDay=candWet;dryDay=candDry;break;
      }
     }
    }
    if(pickCell<0)throw new Exception("Could not locate natural wet->dry 6h transition cell on test world");
    int bx=pickCell%64,by=pickCell/64;
    double u=(bx+0.5)/64.0,v=(by+0.5)/32.0;
    long stepWet=(long)Math.Floor(wetDay*4),stepDry=(long)Math.Floor(dryDay*4);

    PublishAt(wetDay);
    var texWet=sky.Texture;var colWet=sky.ColumnTexture;var precipTexWet=sky.RegionalPrecipitationTexture;
    var cloudResponseWet=bakeMat.GetTexture("_RegionalCloudResponse");
    var rawWetBytes=(byte[])precipTexWet.GetRawTextureData().Clone();
    sky.Bind(publishedProbe,Vector3.forward,true,true,true,true,true,false,false,1f);
    CheckRenderState(report,hpMat.GetFloat("_RegionalPrecipReady")==1f && publishedProbe.GetFloat("_RegionalPrecipReady")==1f,
     "published regional precipitation binds _RegionalPrecipReady=1 on both the HP column and the published diagnostic probe");
    var gpuWet=SampleGpuTexel(publishedProbe.GetTexture("_RegionalPrecipDiagTex"),bx,by);
    bool hasWetCpu=sky.TrySampleRegionalPrecipitation(u,v,out var cpuWet);
    CheckRenderState(report,hasWetCpu && gpuWet.g>0.3f && Mathf.Abs(gpuWet.g-cpuWet.SurfacePrecipKgM2Per6h)<0.02f,
     $"wet snapshot GPU/CPU precipitation match at ({bx},{by}): gpuG={gpuWet.g:F3}, cpuG={cpuWet.SurfacePrecipKgM2Per6h:F3}");

    // Advance to dryDay and step until strip 0 begins (pendingStrip == 1 after Tick)
    world.Day=dryDay;
    var swStage=System.Diagnostics.Stopwatch.StartNew();
    while((int)pendingStripField.GetValue(sky)<0 && swStage.Elapsed.TotalSeconds<10)
    {
     sky.Tick(0,true,false,false,512,dryDay);
     if((int)pendingStripField.GetValue(sky)<0)
     {
      sky.Bind(publishedProbe,Vector3.forward,true,true,true,true,true,false,false,1f);
      var holdGpu=SampleGpuTexel(publishedProbe.GetTexture("_RegionalPrecipDiagTex"),bx,by);
      sky.TrySampleRegionalPrecipitation(u,v,out var holdCpu);
      if(Mathf.Abs(holdGpu.g-gpuWet.g)>1e-4f || Mathf.Abs(holdCpu.SurfacePrecipKgM2Per6h-cpuWet.SurfacePrecipKgM2Per6h)>1e-4f)
       throw new Exception("Published precipitation mutated while background worker was computing");
      System.Threading.Thread.Sleep(1);
     }
    }
    if((int)pendingStripField.GetValue(sky)!=1)throw new Exception("Expected pendingStrip==1 right after strip 0");

    // Strips 0, 1, 2 (pendingStrip == 1, 2, 3): hpColumn already stages dry texture, while published GPU & CPU stay immutable wet!
    for(int stripAfter=1;stripAfter<=3;stripAfter++)
    {
     if(stripAfter>1)sky.Tick(0,true,false,false,512,dryDay);
     if((int)pendingStripField.GetValue(sky)!=stripAfter)throw new Exception("Unexpected pendingStrip "+pendingStripField.GetValue(sky));
     var stagedHpGpu=SampleGpuTexel(hpMat.GetTexture("_RegionalPrecipDiagTex"),bx,by);
     sky.Bind(publishedProbe,Vector3.forward,true,true,true,true,true,false,false,1f);
     var publishedGpu=SampleGpuTexel(publishedProbe.GetTexture("_RegionalPrecipDiagTex"),bx,by);
     bool okMid=sky.TrySampleRegionalPrecipitation(u,v,out var midCpu);
     if(hpMat.GetFloat("_RegionalPrecipReady")!=1f || stagedHpGpu.g>1e-4f)
      throw new Exception($"HP column staging did not bind dry regional precipitation on strip {stripAfter-1}");
     if(!okMid || sky.RegionalClimateStep!=stepWet || sky.Texture!=texWet || sky.ColumnTexture!=colWet ||
        sky.RegionalPrecipitationTexture!=precipTexWet || Mathf.Abs(publishedGpu.g-gpuWet.g)>1e-4f ||
        Mathf.Abs(midCpu.SurfacePrecipKgM2Per6h-cpuWet.SurfacePrecipKgM2Per6h)>1e-4f)
      throw new Exception($"Published GPU/CPU regional snapshot leaked dry staging data before final strip (strip {stripAfter-1})");
    }
    report.Add("PASS strips 0..2 keep published GPU _RegionalPrecipDiagTex, CPU query and RegionalClimateStep immutable while hpColumn stages new 6h data");

    // Cancel / abort before strip 3 by rewinding calendar back to wetDay: verify zero leak
    world.Day=wetDay;
    sky.Tick(0,true,false,false,512,wetDay);
    sky.Bind(publishedProbe,Vector3.forward,true,true,true,true,true,false,false,1f);
    var abortGpu=SampleGpuTexel(publishedProbe.GetTexture("_RegionalPrecipDiagTex"),bx,by);
    sky.TrySampleRegionalPrecipitation(u,v,out var abortCpu);
    CheckRenderState(report,(int)pendingStripField.GetValue(sky)==-1 && sky.RegionalClimateStep==stepWet &&
     Mathf.Abs(abortGpu.g-gpuWet.g)<1e-4f && abortCpu.SurfacePrecipKgM2Per6h==cpuWet.SurfacePrecipKgM2Per6h &&
     hpMat.GetTexture("_RegionalPrecipDiagTex")==precipTexWet &&
     bakeMat.GetTexture("_RegionalCloudResponse")==cloudResponseWet,
     "aborting an incomplete 4-strip bake restores staging materials and leaks nothing into published GPU/CPU state");

    // Complete all 4 strips to dryDay and verify atomic switch on strip 3
    PublishAt(dryDay);
    sky.Bind(publishedProbe,Vector3.forward,true,true,true,true,true,false,false,1f);
    var gpuDry=SampleGpuTexel(publishedProbe.GetTexture("_RegionalPrecipDiagTex"),bx,by);
    bool hasDryCpu=sky.TrySampleRegionalPrecipitation(u,v,out var cpuDry);
    CheckRenderState(report,hasDryCpu && sky.RegionalClimateStep==stepDry && publishedProbe.GetFloat("_RegionalPrecipReady")==1f &&
     gpuDry.g==0f && cpuDry.SurfacePrecipKgM2Per6h==0f && sky.Texture!=texWet && sky.ColumnTexture!=colWet,
     "completing strip 3 atomically publishes dry G=0 regional precipitation alongside ColumnTexture and Weather");

    // Rewind back to wetDay: verify bit-for-bit identical raw RGBAHalf texture bytes and CPU diagnostic
    PublishAt(wetDay);
    var rewoundBytes=sky.RegionalPrecipitationTexture.GetRawTextureData();
    sky.TrySampleRegionalPrecipitation(u,v,out var cpuRewound);
    bool bytesIdentical=rewoundBytes.Length==rawWetBytes.Length;
    for(int b=0;bytesIdentical && b<rawWetBytes.Length;b++)bytesIdentical&=rewoundBytes[b]==rawWetBytes[b];
    CheckRenderState(report,bytesIdentical && cpuRewound.CloudRainOutKgM2Per6h==cpuWet.CloudRainOutKgM2Per6h &&
     cpuRewound.SurfacePrecipKgM2Per6h==cpuWet.SurfacePrecipKgM2Per6h,
     "rewinding back to same calendar day reproduces bit-exact RGBAHalf GPU texture and CPU regional precipitation");
   }
   finally
   {
    RenderTexture.active=prevRt;GL.sRGBWrite=prevSrgb;
    RenderTexture.ReleaseTemporary(gpuRt);UnityEngine.Object.DestroyImmediate(gpuRead);UnityEngine.Object.DestroyImmediate(publishedProbe);
   }
  }

  static void ValidateRegionalAtmosphere(Material cloud,Func<Material,Color[]> render,List<string> report)
  {
   if(!cloud.HasProperty("_RegionalCloudResponse"))throw new Exception("Cloud bake has no regional weather input");
   var forcing=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
   var regional=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
   var original=cloud.GetTexture("_ClimateTex");
   try
   {
    forcing.SetPixel(0,0,new Color(.5f,.5f,.6f,.6f));forcing.Apply();
    cloud.SetTexture("_ClimateTex",forcing);
    Func<Color,Color[]> withField=color=>{
     regional.SetPixel(0,0,color);regional.Apply();
     cloud.SetTexture("_RegionalCloudResponse",regional);
     cloud.SetVector("_RegionalCloudPoles",new Vector4(color.r-color.g,color.b,color.r-color.g,color.b));
     return render(cloud);
    };
    var neutral=withField(Color.black);
    var moist=withField(new Color(1,0,1,1));
    var dry=withField(Color.green);
    double more=0,less=0;
    for(int i=0;i<neutral.Length;i++){more+=moist[i].r-neutral[i].r;less+=neutral[i].r-dry[i].r;}
    CheckRenderState(report,more/neutral.Length>.04&&less/neutral.Length>.04,
     $"regional statistics modulate low-cloud mass at fixed winds, climate and time; mean moist/neutral/dry differences {more/neutral.Length:F4}/{less/neutral.Length:F4}");
    forcing.SetPixel(0,0,Color.black);forcing.Apply();
    foreach(var p in withField(new Color(1,0,1,1)))if(p.a!=0)throw new Exception("Regional forcing made a rain event in dry air");
    report.Add("PASS a statistical outlier cannot start rain without physical precipitation support");
   }
   finally
   {
    cloud.SetTexture("_ClimateTex",original);cloud.SetTexture("_RegionalCloudResponse",Texture2D.blackTexture);
    cloud.SetVector("_RegionalCloudPoles",Vector4.zero);
    UnityEngine.Object.DestroyImmediate(regional);UnityEngine.Object.DestroyImmediate(forcing);
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
    probe.SetFloat("_VegetationWindReady",1);probe.SetFloat("_LocalPhysicalArtwork",1);
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
