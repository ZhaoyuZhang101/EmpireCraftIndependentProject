using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
 public static class WaterSunGlitterRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate SW3 sunlight glitter")]
  public static void Run()
  {
   var report=new List<string>();
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/WaterCloudProbe.shader");
   var material=new Material(shader);
   var target=RenderTexture.GetTemporary(256,128,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(256,128,TextureFormat.RGBAFloat,false,true);
   var previous=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;
   try
   {
    ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(material,0,true);
    Orbis.Planet.OrbisAssetVisuals.BindWater(material);
    material.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));
    material.SetFloat("_WaterClock",3);material.SetFloat("_ProbeKind",4);material.SetFloat("_ProbeDepth",15);
    material.SetFloat("_ProbeSediment",0);material.SetFloat("_ProbeWaves",1);
    material.SetVector("_ProbeView",new Vector4(0,0,1,0));material.SetVector("_ProbeCell",new Vector4(200,130,0,0));
    Func<Color[]> sample=()=>
    {
     Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
     image.ReadPixels(new Rect(0,0,256,128),0,0);image.Apply();return image.GetPixels();
    };
    foreach(int kind in new[]{3,4,5})
    {
     material.SetFloat("_WaterKind",kind);material.SetFloat("_ProbeWaves",1);
     material.SetVector("_ProbeSun",new Vector4(.12f,.08f,1,0));var day=sample();
     material.SetVector("_ProbeSun",new Vector4(.12f,.08f,-1,0));var night=sample();
     double mean=0;float peak=0;int bright=0,invalid=0;
     for(int i=0;i<day.Length;i++)
     {
      float difference=Mathf.Max(0,day[i].r-night[i].r);
      mean+=difference;peak=Mathf.Max(peak,difference);if(difference>.3f)bright++;
      if(float.IsNaN(day[i].r+day[i].g+day[i].b)||float.IsInfinity(day[i].r+day[i].g+day[i].b)||day[i].r<0||day[i].g<0||day[i].b<0)invalid++;
     }
     mean/=day.Length;
     Check(report,peak>.3f&&peak<5&&mean<.20&&bright>0&&bright<day.Length*.4,"kind="+kind+" localized sun glints; peak="+peak+" mean="+mean+" bright pixels="+bright);
     Check(report,peak/Math.Max(.000001,mean)>60,"kind="+kind+" clear wave-crest highlights retain contrast against unlit water; peak/mean="+peak/Math.Max(.000001,mean));
     Check(report,invalid==0,"kind="+kind+" glitter stays finite and nonnegative; invalid="+invalid);
     material.SetFloat("_ProbeWaves",0);var stillNight=sample();
     material.SetVector("_ProbeSun",new Vector4(.12f,.08f,1,0));var stillDay=sample();
     double stillError=0;
     for(int i=0;i<stillDay.Length;i++)stillError=Math.Max(stillError,Math.Abs(stillDay[i].r-stillNight[i].r));
     Check(report,stillError<.00001,"kind="+kind+" disabled or frozen waves suppress sun glitter; maximum change="+stillError);
    }
    if(report.Exists(line=>line.StartsWith("FAIL")))throw new Exception(string.Join("\n",report));
   }
   finally
   {
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");
    File.WriteAllLines("Documentation/LocalPlanetUpgrade/water-sun-glitter-regression.txt",report);
    RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(image);
   }
  }

  static void Check(List<string> report,bool pass,string message){report.Add((pass?"PASS ":"FAIL ")+message);}
 }
}
