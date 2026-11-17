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
   }
   finally
   {
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");File.WriteAllLines("Documentation/LocalPlanetUpgrade/prop-shadow-regression.txt",lines);
    ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);
    UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(field);UnityEngine.Object.DestroyImmediate(info);UnityEngine.Object.DestroyImmediate(m);
   }
  }
 }
}
