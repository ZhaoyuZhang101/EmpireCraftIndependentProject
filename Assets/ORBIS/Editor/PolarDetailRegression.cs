using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Orbis.Editor
{
 public static class PolarDetailRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate polar detail scale")]
  public static void Run()
  {
   var m=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PolarDetailProbe.shader"));
   var rt=RenderTexture.GetTemporary(128,128,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);var old=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;var lines=new List<string>();
   try
   {
    m.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));ShaderUtil.CompilePass(m,0,true);
    foreach(float lat in new[]{0f,60f,-60f,80f,-80f,89f,-89f})
    {
     m.SetFloat("_Latitude",lat);Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
     double x=0,y=0;foreach(var p in image.GetPixels()){x+=p.r;y+=p.g;}double ratio=x/y;
     lines.Add((ratio>.95&&ratio<1.05?"PASS":"FAIL")+" equal surface distance has equal artwork scale at latitude="+lat+" ratio="+ratio);
    }
    File.WriteAllLines("Documentation/LocalPlanetUpgrade/polar-detail-regression.txt",lines);
   }
   finally {RenderTexture.active=old;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(image);}
  }
 }
}
