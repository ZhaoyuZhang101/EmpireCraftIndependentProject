using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace Orbis.Editor
{
 public static class FlatNightRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate flat moonlight")]
  public static void Run()
  {
   var result=new List<string>();
   var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/FlatNightProbe.shader"));
   var display=new Material(Resources.Load<Shader>("ORBIS/Display"));display.SetFloat("_Exposure",1);
   var scene=RenderTexture.GetTemporary(8,8,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var output=RenderTexture.GetTemporary(8,8,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(8,8,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
   try
   {
    var settings=new PlanetSettings();settings.nightLighting=new OrbisNightLightingSettings();
    probe.SetVector("_FlatSun",Vector3.back);probe.SetFloat("_FlatDayNight",1);probe.SetFloat("_FlatNightAdaptation",1);probe.SetTexture("_SkyTex",Texture2D.blackTexture);
    Func<float> sample=()=>{Graphics.Blit(Texture2D.whiteTexture,scene,probe);Graphics.Blit(scene,output,display);RenderTexture.active=output;image.ReadPixels(new Rect(0,0,8,8),0,0);image.Apply();var p=image.GetPixel(4,4);return p.r*.2126f+p.g*.7152f+p.b*.0722f;};
    settings.nightLighting.phaseAtDay100=0;OrbisNightLighting.Bind(probe,settings,Vector3.back,100);float newMoon=sample();
    settings.nightLighting.phaseAtDay100=.5f;OrbisNightLighting.Bind(probe,settings,Vector3.back,100);float fullMoon=sample();
    OrbisNightLighting.Bind(probe,settings,Vector3.forward,100);float belowHorizon=sample();
    probe.SetTexture("_SkyTex",Texture2D.whiteTexture);probe.SetFloat("_SkyReady",1);probe.SetFloat("_FlatCloudShadows",1);
    OrbisNightLighting.Bind(probe,settings,Vector3.back,100);float overcast=sample();
    result.Add((newMoon>.10f?"PASS":"FAIL")+" new moon terrain readability="+newMoon);
    result.Add((fullMoon>newMoon*1.2f?"PASS":"FAIL")+" full moon brighter="+fullMoon);
    result.Add((Mathf.Abs(belowHorizon-newMoon)<.003f?"PASS":"FAIL")+" Moon below horizon contributes zero="+belowHorizon);
    result.Add((overcast<fullMoon&&overcast>.08f?"PASS":"FAIL")+" cloudy night stays readable="+overcast);
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");File.WriteAllLines("Documentation/LocalPlanetUpgrade/night-regression.txt",result);
   }
   finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(scene);RenderTexture.ReleaseTemporary(output);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(probe);UnityEngine.Object.DestroyImmediate(display);}
  }
 }
}
