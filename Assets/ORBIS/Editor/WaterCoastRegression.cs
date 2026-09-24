using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Orbis.Editor
{
 public static class WaterCoastRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate smooth coast water style")]
  public static void Run()
  {
   var m=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/WaterCoastProbe.shader"));
   var info=new Texture2D(512,256,TextureFormat.RGBA32,false,true);var pixels=new Color32[512*256];
   for(int y=0;y<256;y++)for(int x=0;x<512;x++)pixels[y*512+x]=new Color32(0,(byte)(x<183?4:0),0,0);
   info.SetPixels32(pixels);info.Apply();info.filterMode=FilterMode.Point;info.wrapMode=TextureWrapMode.Repeat;
   var rt=RenderTexture.GetTemporary(128,4,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(128,4,TextureFormat.RGBAFloat,false,true);var old=RenderTexture.active;
   var geo=new Texture2D(512,256,TextureFormat.RGBAFloat,false,true);
   bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   try
   {
    m.SetTexture("_InfoTex",info);m.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));
    Orbis.Planet.OrbisAssetVisuals.BindWater(m);m.SetTexture("_VendorOceanFoam",Texture2D.whiteTexture);m.SetTexture("_VendorRiverFoam",Texture2D.whiteTexture);
    m.SetFloat("_WaterClock",3);ShaderUtil.CompilePass(m,0,true);Graphics.Blit(Texture2D.whiteTexture,rt,m);
    RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,4),0,0);image.Apply();
    var a=image.GetPixel(31,2);var b=image.GetPixel(95,2);float gap=Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));
    string report=(gap<.003f?"PASS":"FAIL")+" smoothed lake shoreline across land semantic cell, colour jump="+gap;
    var snow=new Color[pixels.Length];
    for(int y=0;y<256;y++)for(int x=0;x<512;x++){pixels[y*512+x]=new Color32(0,(byte)(x<183?0:3),0,0);snow[y*512+x]=new Color(0,x<183?5:0,0,0);}
    info.SetPixels32(pixels);info.Apply();geo.SetPixels(snow);geo.Apply();geo.filterMode=FilterMode.Point;
    m.SetTexture("_GeoTex",geo);m.SetFloat("_ProbeMode",1);Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,4),0,0);image.Apply();
    a=image.GetPixel(31,2);b=image.GetPixel(95,2);
    report+="\n"+(a.r>.99f&&b.r>.99f?"PASS":"FAIL")+" snow artwork extends into reconstructed dry part of coastal water cell; coverage="+a.r+","+b.r;
    File.WriteAllText("Documentation/LocalPlanetUpgrade/water-coast-regression.txt",report);
   }
   finally {RenderTexture.active=old;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(info);UnityEngine.Object.DestroyImmediate(geo);UnityEngine.Object.DestroyImmediate(image);}
  }
 }
}
