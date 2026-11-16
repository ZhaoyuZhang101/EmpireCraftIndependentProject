using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Orbis.Editor
{
 public static class WaterCloudRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate HP clouds and SW3 water")]
  public static void Run()
  {
   var report=new List<string>();var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/WaterCloudProbe.shader");
   var m=new Material(shader);var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   var rt=RenderTexture.GetTemporary(128,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(128,64,TextureFormat.RGBAFloat,false,true);
   try
   {
    ShaderUtil.CompilePass(m,0,true);Orbis.Planet.OrbisAssetVisuals.BindWater(m);
    m.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));m.SetFloat("_WaterClock",3);m.SetFloat("_ShowPlumes",1);m.SetFloat("_Bathymetry",.5f);
    Func<Color[]> sample=()=>{Graphics.Blit(Texture2D.whiteTexture,rt,m,0);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,64),0,0);image.Apply();return image.GetPixels();};
    m.SetFloat("_ProbeKind",0);var cloud=sample();int bad=0,nonempty=0;
    foreach(var c in cloud){if(!Finite(c)||c.r<0||c.g<0||Mathf.Abs(c.b-1)>.0001f)bad++;if(c.r>.01f)nonempty++;}
    report.Add((bad==0&&nonempty>0?"PASS":"FAIL")+" HP density, phi_fwd and full light segment coverage; invalid="+bad+" cloud samples="+nonempty);
    m.SetFloat("_ProbeKind",1);var samples=new List<Color[]>();
    foreach(int kind in new[]{3,4,5})
    {
     m.SetFloat("_WaterKind",kind);var pixels=sample();samples.Add(pixels);bad=0;
     foreach(var c in pixels)if(!Finite(c)||c.r<0||c.g<0||c.b<0)bad++;
     report.Add((bad==0?"PASS":"FAIL")+" SW3 water kind="+kind+" finite at depths 0.1..4000m; invalid="+bad);
    }
    double oceanLake=Difference(samples[0],samples[1]),riverLake=Difference(samples[2],samples[1]);
    report.Add((oceanLake>1e-4&&riverLake>1e-4?"PASS":"FAIL")+" distinct ocean/lake/river results; differences="+oceanLake+", "+riverLake);
    report.Add((m.GetFloat("_VendorWaterReady")>.5?"PASS":"FAIL")+" all five SW3 artwork resources bound");
    m.SetFloat("_ProbeKind",3);m.SetFloat("_WaterKind",3);m.SetFloat("_ProbeFlip",0);var forward=sample();
    m.SetFloat("_ProbeFlip",1);var mirrored=sample();double rotationError=0;
    for(int y=0;y<64;y++)for(int x=0;x<128;x++)
    {
     var a=forward[y*128+x];var b=mirrored[y*128+127-x];
     rotationError=Math.Max(rotationError,Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b))));
    }
    report.Add((rotationError<.001?"PASS":"FAIL")+" geographic water normals remain stable under reversed screen direction; max error="+rotationError);
    m.SetFloat("_ProbeFlip",0);
    foreach(int kind in new[]{3,4,5})
    {
     float period=kind==4?1/.065f:5;
     m.SetFloat("_WaterKind",kind);m.SetFloat("_WaterClock",period-.0001f);var before=sample();
     m.SetFloat("_WaterClock",period+.0001f);var after=sample();
     double boundary=Difference(before,after);
     report.Add((boundary<.003?"PASS":"FAIL")+" continuous two-phase wave advection kind="+kind+" mean RGB difference="+boundary);
    }
    m.SetFloat("_WaterClock",3);
    var bed=new Texture2D(16,8,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
    try
    {
     var heights=new Color[128];for(int y=0;y<8;y++)for(int x=0;x<16;x++){float depth=10*Mathf.Pow(2,Mathf.Min(x,15-x));heights[y*16+x]=new Color(-depth,3,depth,0);}
     bed.SetPixels(heights);bed.Apply();m.SetTexture("_VisualTex",bed);m.SetVector("_MapSize",new Vector4(16,8,1f/16,1f/8));m.SetFloat("_FieldReady",1);m.SetFloat("_Relief",1);m.SetFloat("_ProbeKind",2);
     var relief=sample();float left=relief[32*128+32].r,right=relief[32*128+96].r;
     report.Add((Mathf.Abs(left-right)>.12f?"PASS":"FAIL")+" actual seabed opposing slopes remain visible; contrast="+Mathf.Abs(left-right));
    }
    finally{UnityEngine.Object.DestroyImmediate(bed);}
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");File.WriteAllLines("Documentation/LocalPlanetUpgrade/water-cloud-regression.txt",report);
    if(report.Exists(line=>line.StartsWith("FAIL")))throw new InvalidOperationException(string.Join("\n",report));
   }
   finally{ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(image);}
  }
  static bool Finite(Color c)=>!float.IsNaN(c.r+c.g+c.b)&&!float.IsInfinity(c.r+c.g+c.b);
  static double Difference(Color[] a,Color[] b){double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return sum/a.Length;}
 }
}
