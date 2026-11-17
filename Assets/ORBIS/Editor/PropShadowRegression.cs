using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Orbis.Editor
{
 public static class PropShadowRegression
 {
  public static void Run()
  {
   var lines=new List<string>();var old=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PropShadowProbe.shader");var m=new Material(shader);
   var field=new Texture2D(8,4,TextureFormat.RGBAFloat,false,true);var info=new Texture2D(8,4,TextureFormat.RGBAFloat,false,true);
   field.filterMode=info.filterMode=FilterMode.Point;field.wrapMode=info.wrapMode=TextureWrapMode.Repeat;
   var rt=RenderTexture.GetTemporary(128,128,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var read=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
   try
   {
    ShaderUtil.CompilePass(m,0,true);
    foreach(var message in ShaderUtil.GetShaderMessages(shader))if(message.severity.ToString()=="Error")throw new Exception(message.message);
    var pixels=new Color[32];for(int k=0;k<32;k++)pixels[k]=new Color(10,-3,0,1);field.SetPixels(pixels);field.Apply();
    m.SetTexture("_VisualTex",field);m.SetTexture("_InfoTex",info);m.SetTexture("_HydroA",Texture2D.blackTexture);m.SetTexture("_HydroB",Texture2D.blackTexture);
    m.SetVector("_MapSize",new Vector4(8,4,.125f,.25f));
    for(int kind=0;kind<2;kind++)for(int axis=0;axis<2;axis++)
    {
     float continuation=0,difference=0;
     // Placement is jittered. Exercise several seeds, requiring a crossing where
     // the volume reaches a boundary and continuity for every sampled caster.
     for(int rootX=1;rootX<=5;rootX++)
     {
      var semantic=new Color[32];for(int i=0;i<32;i++)semantic[i]=new Color(0,0,2f/255,0);
      semantic[rootX+8]=new Color(0,0,kind/255f,0);info.SetPixels(semantic);info.Apply();
      m.SetVector("_ProbeRoot",new Vector4(rootX,1,0,0));
      float lon=((rootX+.5f)/8-.5f)*Mathf.PI*2,lat=(1.5f/4-.5f)*Mathf.PI;
      var radial=new Vector3(Mathf.Sin(lon)*Mathf.Cos(lat),Mathf.Sin(lat),Mathf.Cos(lon)*Mathf.Cos(lat));
      var east=new Vector3(Mathf.Cos(lon),0,-Mathf.Sin(lon));
      var north=new Vector3(-Mathf.Sin(lat)*Mathf.Sin(lon),Mathf.Cos(lat),-Mathf.Sin(lat)*Mathf.Cos(lon));
      m.SetVector("_FlatSun",(radial*.20f-(axis==0?east:north)*.98f).normalized);
      Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,128,128),0,0);read.Apply();
      for(int t=0;t<128;t++)
      {
       float a=read.GetPixel(axis==0?63:t,axis==0?t:63).r,b=read.GetPixel(axis==0?64:t,axis==0?t:64).r;
       continuation=Mathf.Max(continuation,Mathf.Min(a,b));difference=Mathf.Max(difference,Mathf.Abs(a-b));
      }
     }
     bool pass=continuation>.03f&&continuation<=.301f&&difference<.065f;
     lines.Add((pass?"PASS":"FAIL")+" "+(kind==0?"tree":"building")+" crosses "+(axis==0?"east":"north")+" cell boundary; coverage="+continuation+" delta="+difference);
     if(!pass)throw new Exception(lines[lines.Count-1]);
    }
    BuildingSilhouette(lines,m,info,rt,read);
    WaterReflections(lines,m,field,info,rt,read);
    TreeReflectionEdges(lines,m,rt,read);
   }
   finally
   {
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");File.WriteAllLines("Documentation/LocalPlanetUpgrade/prop-shadow-regression.txt",lines);
    ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);
    UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(field);UnityEngine.Object.DestroyImmediate(info);UnityEngine.Object.DestroyImmediate(m);
   }
  }
  static void BuildingSilhouette(List<string> report,Material material,Texture2D info,RenderTexture target,Texture2D image)
  {
   var semantic=new Color[32];
   for(int i=0;i<semantic.Length;i++)semantic[i]=new Color(0,0,2f/255,0);
   semantic[2+8]=new Color(0,0,1f/255,0);
   info.SetPixels(semantic);info.Apply();
   material.SetFloat("_ProbeMode",4);
   material.SetVector("_ProbeCaster",new Vector4(2,1,0,0));
   float longitude=(2.5f/8-.5f)*Mathf.PI*2;
   float latitude=(1.5f/4-.5f)*Mathf.PI;
   material.SetVector("_FlatSun",new Vector3(Mathf.Sin(longitude)*Mathf.Cos(latitude),Mathf.Sin(latitude),Mathf.Cos(longitude)*Mathf.Cos(latitude)));
   Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
   image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
   double mismatch=0,coverage=0;
   foreach(var pixel in image.GetPixels()){mismatch+=Math.Abs(pixel.r-pixel.g);coverage+=pixel.g;}
   bool pass=coverage>100&&mismatch/coverage<.06;
   report.Add((pass?"PASS":"FAIL")+" building shader shadow follows its actual wall and roof silhouette; mismatch="+mismatch/Math.Max(1,coverage));
   if(!pass)throw new Exception(report[report.Count-1]);
  }
  static void WaterReflections(List<string> report,Material material,Texture2D field,Texture2D info,RenderTexture target,Texture2D image)
  {
   var semantic=new Color[32];for(int i=0;i<semantic.Length;i++)semantic[i]=new Color(0,0,2f/255,0);
   material.SetFloat("_ProbeMode",1);material.SetVector("_ProbeRoot",new Vector4(1.5f,-.25f,0,0));
   material.SetVector("_ProbeReflectionRay",new Vector4(0,.65f,.76f,0));
   Func<Color[]> sample=()=>
   {
    Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
    image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();return image.GetPixels();
   };
   foreach(int kind in new[]{0,1})
   {
    semantic[2+8]=new Color(0,0,kind/255f,0);info.SetPixels(semantic);info.Apply();
    var reflected=sample();double coverage=0;int invalid=0;
    foreach(var pixel in reflected){coverage+=pixel.a;if(float.IsNaN(pixel.r+pixel.g+pixel.b+pixel.a)||float.IsInfinity(pixel.r+pixel.g+pixel.b+pixel.a))invalid++;}
    bool pass=coverage>20&&invalid==0;
    report.Add((pass?"PASS":"FAIL")+" "+(kind==0?"tree":"building")+" artwork appears in near-shore reflection; coverage="+coverage+" invalid="+invalid);
    if(!pass)throw new Exception(report[report.Count-1]);
    material.SetVector("_ProbeReflectionRay",new Vector4(.2f,.65f,.76f,0));var warped=sample();double difference=0;
    for(int i=0;i<reflected.Length;i++)difference+=Math.Abs(reflected[i].a-warped[i].a);
    pass=difference>20;report.Add((pass?"PASS":"FAIL")+" reflected artwork follows the wave reflection direction; delta="+difference);
    if(!pass)throw new Exception(report[report.Count-1]);
    material.SetVector("_ProbeReflectionRay",new Vector4(0,.65f,.76f,0));
   }
   semantic[2+8]=new Color(0,0,2f/255,0);info.SetPixels(semantic);info.Apply();double empty=0;
   foreach(var pixel in sample())empty+=pixel.a;
   report.Add((empty==0?"PASS":"FAIL")+" absent casters leave no reflected silhouettes");
   if(empty!=0)throw new Exception(report[report.Count-1]);
   material.SetFloat("_ProbeMode",2);material.SetTexture("_SkyTex",Texture2D.blackTexture);var clear=sample();
   material.SetTexture("_SkyTex",Texture2D.whiteTexture);var cloudy=sample();double skyChange=0;
   for(int i=0;i<clear.Length;i++)skyChange+=Math.Abs(clear[i].r-cloudy[i].r)+Math.Abs(clear[i].g-cloudy[i].g)+Math.Abs(clear[i].b-cloudy[i].b);
   skyChange/=clear.Length;report.Add((skyChange>.1?"PASS":"FAIL")+" sky reflections use the shared HP cloud coverage; RGB change="+skyChange);
   if(skyChange<=.1)throw new Exception(report[report.Count-1]);
   var terrain=new Color[32];for(int i=0;i<terrain.Length;i++)terrain[i]=new Color(0,-3,0,1);field.SetPixels(terrain);field.Apply();
   foreach(int row in new[]{0,3})
   {
    semantic[2+row*8]=new Color(0,0,1f/255,0);info.SetPixels(semantic);info.Apply();
    material.SetVector("_ProbeCaster",new Vector4(2,row,0,0));
    material.SetVector("_ProbeRoot",new Vector4(1.5f,row-1.25f,0,0));
    material.SetFloat("_ProbeMode",1);var actual=sample();
    material.SetFloat("_ProbeMode",3);var expected=sample();double mismatch=0,coverage=0;
    for(int i=0;i<actual.Length;i++){mismatch+=Math.Abs(actual[i].a-expected[i].a);coverage+=expected[i].a;}
    bool pass=coverage>20&&mismatch/coverage<.02;
    report.Add((pass?"PASS":"FAIL")+" building reflection shares latitude-corrected artwork anchor at row="+row+"; mismatch="+mismatch/Math.Max(1,coverage));
    if(!pass)throw new Exception(report[report.Count-1]);
    semantic[2+row*8]=new Color(0,0,2f/255,0);
   }
  }
  static void TreeReflectionEdges(List<string> report,Material material,RenderTexture target,Texture2D image)
  {
   var field=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
   var info=new Texture2D(512,256,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point};
   var ecology=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
   var wind=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
   try
   {
    var semantic=new Color32[512*256];
    for(int i=0;i<semantic.Length;i++)semantic[i]=new Color32(0,0,2,0);
    semantic[260+127*512]=new Color32(0,0,0,0);info.SetPixels32(semantic);info.Apply();
    ecology.SetPixel(0,0,new Color32(6,18,190,110));ecology.Apply();
    wind.SetPixel(0,0,new Color(1,.35f,1,.35f));wind.Apply();
    material.SetTexture("_VisualTex",field);material.SetTexture("_InfoTex",info);
    material.SetTexture("_EcologyTex",ecology);material.SetFloat("_EcologyReady",1);
    material.SetTexture("_VegetationWindTex",wind);
    material.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));
    material.SetVector("_ProbeCaster",new Vector4(260,127,0,0));
    material.SetVector("_ProbeReflectionRay",new Vector4(0,.65f,.76f,0));
    material.SetFloat("_ProbeMode",5);
    foreach(int elevation in new[]{200,600})foreach(int windEnabled in new[]{0,1})
    {
     field.SetPixel(0,0,new Color(elevation,-3,0,1));field.Apply();
     material.SetFloat("_VegetationWindReady",windEnabled);material.SetFloat("_SkyClock",1.7f);
     Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
     image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
     double mismatch=0,coverage=0;
     foreach(var pixel in image.GetPixels()){mismatch+=Math.Abs(pixel.r-pixel.g);coverage+=pixel.g;}
     bool pass=coverage>5&&mismatch/coverage<.01;
     report.Add((pass?"PASS":"FAIL")+" reflected lower canopy remains complete on a raised bank; elevation="+elevation+" wind="+windEnabled+" coverage="+coverage+" mismatch="+mismatch/Math.Max(1,coverage));
     if(!pass)throw new InvalidOperationException(report[report.Count-1]);
    }
   }
   finally
   {
    foreach(var resource in new UnityEngine.Object[]{field,info,ecology,wind})UnityEngine.Object.DestroyImmediate(resource);
   }
  }
 }
}
