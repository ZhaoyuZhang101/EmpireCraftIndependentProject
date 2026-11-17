using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
 public static class TerrainQualityRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate terrain surface detail")]
  public static void Run()
  {
   var report=new List<string>();
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/TerrainQualityProbe.shader");
   var material=new Material(shader);
   var field=new Texture2D(16,8,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Repeat};
   var target=RenderTexture.GetTemporary(128,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(128,64,TextureFormat.RGBAFloat,false,true);
   var previous=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;
   try
   {
    ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(material,0,true);
    material.SetTexture("_VisualTex",field);
    material.SetVector("_MapSize",new Vector4(16,8,1f/16,1f/8));
    material.SetVector("_ProbeOrigin",new Vector4(5,3,0,0));
    material.SetFloat("_ProbePixels",48);material.SetFloat("_FieldReady",1);material.SetFloat("_Relief",1);
    Func<Color[]> sample=()=>
    {
     Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
     image.ReadPixels(new Rect(0,0,128,64),0,0);image.Apply();return image.GetPixels();
    };
    var pixels=new Color[128];
    for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(400,-3,0,.35f);
    field.SetPixels(pixels);field.Apply();var soil=sample();
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)pixels[y*16+x]=new Color(400+(x-5)*280,-3,0,.35f);
    field.SetPixels(pixels);field.Apply();var rock=sample();
    double rockResponse=Difference(soil,rock);
    Check(report,rockResponse>.04,"actual steep terrain exposes distinct rock and ridge detail; mean RGB change="+rockResponse);
    double nearContrast=Contrast(rock);
    material.SetFloat("_ProbePixels",2);double farContrast=Contrast(sample());
    Check(report,nearContrast>.012&&nearContrast>farContrast*1.5,"close terrain gains detail while distant detail filters out; contrast="+nearContrast+","+farContrast);
    material.SetFloat("_ProbePixels",48);material.SetFloat("_ProbeFlip",1);var mirrored=sample();
    double maximumError=0;string errorDetail="";
    for(int y=0;y<64;y++)for(int x=0;x<128;x++)
    {
     Color a=rock[y*128+x],b=mirrored[y*128+127-x];
     double difference=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
     if(difference>maximumError){maximumError=difference;errorDetail=" at "+x+","+y+" original="+a+" reversed="+b;}
    }
    Check(report,maximumError<.005,"geographic terrain detail remains stable under reversed view; maximum RGB error="+maximumError+errorDetail);
    material.SetFloat("_ProbeFlip",0);
    int invalid=0;
    foreach(var origin in new[]{new Vector4(15.3f,.1f,0,0),new Vector4(-.7f,7,0,0)})
    {
     material.SetVector("_ProbeOrigin",origin);
     foreach(var color in sample())if(float.IsNaN(color.r+color.g+color.b)||float.IsInfinity(color.r+color.g+color.b)||color.r<0||color.g<0||color.b<0)invalid++;
    }
    Check(report,invalid==0,"terrain detail remains finite at polar and wrapped coordinates; invalid="+invalid);
    if(report.Exists(line=>line.StartsWith("FAIL")))throw new Exception(string.Join("\n",report));
   }
   finally
   {
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");
    File.WriteAllLines("Documentation/LocalPlanetUpgrade/terrain-quality-regression.txt",report);
    RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(field);UnityEngine.Object.DestroyImmediate(image);
   }
  }

  static void Check(List<string> report,bool pass,string message){report.Add((pass?"PASS ":"FAIL ")+message);}
  static double Difference(Color[] first,Color[] second)
  {
   double sum=0;
   for(int i=0;i<first.Length;i++)sum+=Math.Abs(first[i].r-second[i].r)+Math.Abs(first[i].g-second[i].g)+Math.Abs(first[i].b-second[i].b);
   return sum/first.Length;
  }
  static double Contrast(Color[] pixels)
  {
   double sum=0,squares=0;
   foreach(var pixel in pixels){double value=pixel.r*.2126+pixel.g*.7152+pixel.b*.0722;sum+=value;squares+=value*value;}
   return Math.Sqrt(Math.Max(0,squares/pixels.Length-sum*sum/(pixels.Length*pixels.Length)));
  }
 }
}
