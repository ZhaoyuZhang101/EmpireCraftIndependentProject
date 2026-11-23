using System;
using System.IO;
using UnityEngine;
using UnityEditor;
namespace Orbis.Editor
{
 public static class PolarIceRegression
 {
  public static string CheckLakeIce()
  {
   byte[] months={96,120,160,128,132,136,140,144,148,152,156,160};
   var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PolarIceProbe.shader"));
   var data=new Texture2D(1,3,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Bilinear};
   var image=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
   var target=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var previous=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;
   try
   {
    ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(material,0,true);
    data.LoadRawTextureData(months);data.Apply(false,true);
    material.SetTexture("_LakeIceTex",data);
    material.SetFloat("_ProbeLakeMode",1);
    material.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));
    material.SetVector("_ProbeLakeUV",new Vector4(.8f,.75f,0,0));
    Color Sample(int month,float kind,float support,float survey,float shore,float depth,float heat=0,float blend=0)
    {
     material.SetVector("_LakeIceSeason",new Vector4(month,(month+1)%12,blend,1));
     material.SetVector("_ProbeBasin",new Vector4(kind,support,survey,shore));
     material.SetFloat("_ProbeDepth",depth);
     material.SetVector("_ClimateYearHeat",new Vector4(0,0,0,heat));
     Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
     image.ReadPixels(new Rect(0,0,1,1),0,0);image.Apply();
     return image.GetPixel(0,0);
    }
    void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
    foreach(float kind in new[]{0f,1f,2f,3f,5f})
     Check(Sample(0,kind,1,0,1,10).g==0,"Lake ice leaked onto water/land kind "+kind);
    Check(Sample(0,4,1,0,-.1f,10).g==0,"Lake ice extended onto a dry bank or its river.");
    Check(Sample(0,0,0,1,.2f,2).g>.999f,"A surveyed lake over a land tile failed to freeze.");
    Check(Sample(1,4,1,0,.001f,1).g>.95f&&Sample(1,4,1,0,1,80).g<.001f,
     "Under the same cooling the shallow shore and deeper centre did not freeze separately.");
    float partial=Sample(1,4,1,0,1,80,-1.5f).g;
    Check(partial>.01f&&partial<.99f&&Sample(0,4,1,0,1,80).g>.999f,
     "Sustained cooling did not advance a partially frozen lake to a full sheet.");
    Check(Sample(2,4,1,0,1,80).g==0&&Sample(0,4,1,0,.001f,1,12).g==0,
     "Summer conditions or local warming left ice in the lake.");
    foreach(float v in new[]{0f,.5f,1f})
    {
     material.SetVector("_ProbeLakeUV",new Vector4(.19f,v,0,0));
     for(int m=0;m<12;m++)
     {
      float expected=Mathf.Lerp(months[m],months[(m+1)%12],.35f);
      Check(Mathf.Abs(Sample(m,4,1,0,1,80,0,.35f).b*255-expected)<.08f,
       "Wrong monthly panel, polar row or December/January interpolation at month "+m);
     }
    }
    return "PASS GPU seasonal lake ice: lake/survey masks, marine/river exclusion, shallow margins, deep centres, thaw, regional heat, twelve months and both poles.";
   }
   finally
   {
    ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(data);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(material);
   }
  }

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
    if(bad==0)result+="\n"+CheckLakeIce();
    File.WriteAllText("Documentation/LocalPlanetUpgrade/polar-ice-regression.txt",result);if(bad>0)throw new Exception(result);
   }
   finally{ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(image);}
  }
 }
}
