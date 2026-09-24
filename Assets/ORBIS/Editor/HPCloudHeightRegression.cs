using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace Orbis.Editor
{
 public static class HPCloudHeightRegression
 {
  public static void Run()
  {
   const int width=1024;var lines=new List<string>();var owned=new List<UnityEngine.Object>();
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/HPCloudHeightProbe.shader");var m=new Material(shader);owned.Add(m);
   var image=new Texture2D(width,1,TextureFormat.RGBAFloat,false,true);owned.Add(image);
   var rt=RenderTexture.GetTemporary(width,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;
   bool asynchronous=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   try
   {
    ShaderUtil.CompilePass(m,0,true);
    foreach(var error in ShaderUtil.GetShaderMessages(shader))if(error.severity.ToString()=="Error")throw new Exception(error.message);
    Func<Color,Texture2D> field=color=>{var t=new Texture2D(2,2,TextureFormat.RGBAFloat,false,true);t.SetPixels(new[]{color,color,color,color});t.Apply();owned.Add(t);return t;};
    var volume=new Texture3D(2,2,2,TextureFormat.RGBAFloat,false);owned.Add(volume);
    var colors=new Color[8];for(int i=0;i<8;i++)colors[i]=new Color(.8f,.8f,.8f,.8f);volume.SetPixels(colors);volume.Apply();
    m.SetTexture("_CloudNoise",volume);m.SetTexture("_CloudDetailNoise",volume);m.SetTexture("_AtmosLayers",volume);m.SetTexture("_AtmosLayerWind",volume);
    m.SetTexture("_GroundField",field(new Color(1,0,0,0)));m.SetTexture("_Weather",field(new Color(.8f,.5f,.7f,.4f)));
    m.SetTexture("_ScrollWeatherTex",field(new Color(.8f,.7f,.4f,.4f)));m.SetTexture("_HPHighDetail",field(new Color(.8f,.3f,.5f,.5f)));
    m.SetFloat("_Clouds",1);m.SetFloat("_LayeredWeather",1);m.SetFloat("_Density",1);m.SetFloat("_Cover",1);
    m.SetFloat("_CloudHeightScale",OrbisWeatherField.HeightScale);m.SetFloat("_ScrollHeightScale",Flat.FlatGlobeGeometry.HeightScale);
    m.SetVector("_ScrollSourceSize",new Vector4(512,256,0,0));
    for(int mode=0;mode<4;mode++)
    {
     float terrain=mode==2?4000:0,ground=mode==3?1.07875f:1+terrain*Flat.FlatGlobeGeometry.HeightScale;
     float shell=mode==3?ground+13500*OrbisWeatherField.HeightScale+.001f:OrbisAtmosphereProfile.CloudTop;
     m.SetFloat("_CloudShellTop",shell);m.SetTexture("_GroundField",field(new Color(ground,0,0,0)));
     m.SetFloat("_ScrollCloudSource",mode==0||mode==3?0:1);m.SetTexture("_ScrollHeightTex",field(new Color(terrain,-1,0,0)));
     Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,1),0,0);image.Apply();
     int low=0,high=0,invalid=0;float highest=1;
     var data=image.GetPixels();for(int i=0;i<data.Length;i++)
     {
      var p=data[i];float radius=1+(i+.5f)/width*.20f;
      if(float.IsNaN(p.r+p.g)||float.IsInfinity(p.r+p.g)||Mathf.Abs(p.b-shell)>1e-5f)invalid++;
      if(p.r>1e-5f)low++;if(p.g>1e-5f)high++;
      if(p.r+p.g>1e-5f){highest=Mathf.Max(highest,radius);if(radius<ground||radius>ground+13500*OrbisWeatherField.HeightScale+.0002f)invalid++;}
     }
     bool pass=invalid==0&&low>0&&high>0&&highest<OrbisAtmosphereProfile.OuterRadius-.10f;
     lines.Add((pass?"PASS":"FAIL")+" HP low/high cloud height source="+mode+" low="+low+" high="+high+" maxRadius="+highest+" invalid="+invalid);
     if(!pass)throw new Exception(lines[lines.Count-1]);
    }
    if(AssetDatabase.IsValidFolder("Assets/ThirdParty/Resources/ORBISVendor/UniStorm"))throw new Exception("Retired UniStorm still in Resources");
    lines.Add("PASS UniStorm resources excluded from Assets; HP shell top=1.060, atmosphere outer=1.200");
   }
   finally
   {
    ShaderUtil.allowAsyncCompilation=asynchronous;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
    foreach(var o in owned)UnityEngine.Object.DestroyImmediate(o);
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");File.WriteAllLines("Documentation/LocalPlanetUpgrade/hp-cloud-height-regression.txt",lines);
   }
  }
 }
}
