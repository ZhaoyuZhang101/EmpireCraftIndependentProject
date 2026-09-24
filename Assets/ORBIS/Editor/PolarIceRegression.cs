using System;
using System.IO;
using UnityEngine;
using UnityEditor;
namespace Orbis.Editor
{
 public static class PolarIceRegression
 {
  public static void Run()
  {
   var m=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PolarIceProbe.shader"));
   var rt=RenderTexture.GetTemporary(256,128,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var image=new Texture2D(256,128,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;int bad=0;
   try
   {
    ShaderUtil.CompilePass(m,0,true);
    for(int water=0;water<=1;water++)
    {
     m.SetFloat("_ProbeWater",water);Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,256,128),0,0);image.Apply();var p=image.GetPixels();
     for(int y=0;y<128;y++)for(int x=0;x<256;x++)
     {
      var c=p[y*256+x];float latitude=Mathf.Abs(((y+.5f)/128-.5f)*180);
      if(float.IsNaN(c.r+c.g+c.b+c.a)||float.IsInfinity(c.r+c.g+c.b+c.a)||c.a<0||c.a>1)bad++;
      if(latitude>84&&c.a<.999f)bad++;if(latitude<56&&c.a>0)bad++;
     }
    }
    string result=(bad==0?"PASS":"FAIL")+" GPU north/south ice caps, sea ice/land snow, temperate exclusion and finite color; samples=65536 invalid="+bad;
    File.WriteAllText("Documentation/LocalPlanetUpgrade/polar-ice-regression.txt",result);if(bad>0)throw new Exception(result);
   }
   finally{ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(image);}
  }
 }
}
