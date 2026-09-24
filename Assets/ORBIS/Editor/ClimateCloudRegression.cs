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
    cloud.SetTexture("_ClimateTex",Texture2D.blackTexture);cloud.SetTexture("_ClimateWindTex",Texture2D.blackTexture);cloud.SetFloat("_MoistureReady",1);
    cloud.SetTexture("_MoistureTex",Texture2D.blackTexture);var dry=render(cloud);
    cloud.SetTexture("_MoistureTex",Texture2D.whiteTexture);var wet=render(cloud);
    double dryCloud=0,wetCloud=0;for(int i=0;i<wet.Length;i++){dryCloud+=dry[i].r+dry[i].g;wetCloud+=wet[i].r+wet[i].g;}
    if(dryCloud>1e-5||wetCloud<100)throw new Exception("Moisture cloud gating dry="+dryCloud+" wet="+wetCloud);
    report.Add("PASS dry column produces no cloud; wet column cloud sum="+wetCloud);
    cloud.SetTexture("_LiveCloudTex",Texture2D.whiteTexture);cloud.SetTexture("_LiveWindTex",Texture2D.blackTexture);
    cloud.SetTexture("_LiveEffectTex",Texture2D.blackTexture);cloud.SetFloat("_DynamicReady",1);
    var evolving=render(cloud);int occupied=0;foreach(var pixel in evolving)if(pixel.r>.25f)occupied++;
    float fraction=occupied/(float)evolving.Length;
    if(fraction<.12f||fraction>.85f)throw new Exception("Uniform cloud mass still renders as a blanket: "+fraction);
    report.Add("PASS noise-carved cloud mass, occupied fraction="+fraction);
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
   }
   catch(Exception e){report.Add("FAIL "+e.Message);throw;}
   finally
   {
    Directory.CreateDirectory("Documentation/ClimateCloudUpgrade");File.WriteAllLines("Documentation/ClimateCloudUpgrade/gpu-regression.txt",report);
    ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);foreach(var o in owned)UnityEngine.Object.DestroyImmediate(o);
   }
  }
 }
}
