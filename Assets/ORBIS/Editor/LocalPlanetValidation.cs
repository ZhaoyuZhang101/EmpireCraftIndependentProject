using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Orbis.Flat;
using Orbis.Planet;
namespace Orbis.Editor
{
 public static class LocalPlanetValidation
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate shaders and geography")]
  public static void Validate()
  {
   var lines=new List<string>();bool old=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   try
   {
    foreach(string name in new[]{"FlatWorld/LocalSurface","FlatWorld/ScrollSurface","FlatWorld/FlatMap","Surface","CelestialLens","Clouds","AtmosphereComposite","FlatWorld/HPCloudColumn","FlatWorld/FlatSkyBake"})
    {
     var shader=Resources.Load<Shader>("ORBIS/"+name);if(!shader)throw new Exception("Missing "+name);var material=new Material(shader);
     try{for(int p=0;p<material.passCount;p++){
       if(material.GetPassName(p)=="SURFACE_DEPTH_PERF32")material.EnableKeyword("SHADOWS_DEPTH");
       ShaderUtil.CompilePass(material,p,true);if(!ShaderUtil.IsPassCompiled(material,p))throw new Exception("Not compiled "+name+" pass "+p);}
      foreach(var message in ShaderUtil.GetShaderMessages(shader))if(message.severity.ToString()=="Error")throw new Exception(name+": "+message.message);
      lines.Add("PASS shader "+name+" / "+material.passCount+" passes");}finally{UnityEngine.Object.DestroyImmediate(material);}
    }
    ValidateSolarLens(lines);
    var data=Array.Find(Resources.LoadAll<TextAsset>("ORBIS/WorldAtlas"),asset=>asset.name=="Default"&&FlatAtlasTerrain.HasSignature(asset.bytes));
    if(!data)throw new Exception("Missing binary World Atlas terrain");
    var w=FlatAtlasTerrain.Read(data.bytes,"ORBIS-2D-2026",default);FlatSave.Validate(w);
    int rivers=0,lakes=0;var rank=new int[w.Count];for(int k=0;k<w.Count;k++)rank[w.DrainOrder[k]]=k;
    for(int i=0;i<w.Count;i++)
    {
     if(w.Water[i]==WaterKind.River)rivers++;if(w.Water[i]==WaterKind.Lake)lakes++;
     int n=w.Downstream[i];if(n>=0&&(rank[n]>=rank[i]||w.WaterLevel[n]>w.WaterLevel[i]))throw new Exception("Cyclic/uphill drainage "+i);
    }
    if(rivers==0||lakes==0)throw new Exception("Missing rivers/lakes");
    lines.Add("PASS World Atlas physical bed + drainage: "+rivers+" river cells, "+lakes+" lake cells; acyclic downhill spill graph");
    lines.Add("PASS save validation; "+w.Facts.MouthCount+" river mouths");
   }
   catch(Exception e){lines.Add("FAIL "+e);throw;}
   finally{ShaderUtil.allowAsyncCompilation=old;Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");File.WriteAllLines("Documentation/LocalPlanetUpgrade/validation.txt",lines);}
  }

  // Distances use image height. Compare the displayed sRGB image to the reference
  // in both project colour spaces, without depending on a scene or a bloom volume.
  public static void ValidateSolarLens(List<string> lines)
  {
   const int width=400,height=224,angles=128;
   const float aspect=(float)width/height;
   var lens=new Material(Resources.Load<Shader>("ORBIS/CelestialLens"));
   var source=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var readback=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
   var partial=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
   var previous=RenderTexture.active;
   bool srgbWrite=GL.sRGBWrite,linear=QualitySettings.activeColorSpace==ColorSpace.Linear;
   try
   {
    GL.sRGBWrite=false;
    Graphics.Blit(Texture2D.blackTexture,source);
    partial.SetPixel(0,0,new Color(.25f,.25f,.25f,1));partial.Apply();
    lens.SetFloat("_Exposure",1);lens.SetVector("_Sun",Vector3.back);lens.SetVector("_CameraLocal",Vector3.forward*3);
    foreach(string ray in new[]{"_RayBL","_RayBR","_RayTL","_RayTR"})lens.SetVector(ray,Vector3.forward);

    Vector2 UV(Vector2 p)=>new Vector2(.5f+p.x/aspect,.5f+p.y);
    Color Sample(Vector2 p)
    {
     var uv=UV(p);var c=readback.GetPixelBilinear(uv.x,uv.y);
     return linear?c.gamma:c;
    }
    float Luma(Color c)=>.2126f*c.r+.7152f*c.g+.0722f*c.b;
    void Render(Vector2 p,Texture transmission)
    {
     var uv=UV(p);
     lens.SetVector("_LensSun",new Vector4(uv.x,uv.y,1.15f,aspect));
     lens.SetTexture("_LensTransmission",transmission);
     Graphics.Blit(source,target,lens);RenderTexture.active=target;
     readback.ReadPixels(new Rect(0,0,width,height),0,0);readback.Apply();
    }
    float[] Ring(float radius)
    {
     var ring=new float[angles];
     for(int i=0;i<angles;i++)
     {
      float angle=i*(2*Mathf.PI/angles);
      ring[i]=Luma(Sample(radius*new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))));
     }
     return ring;
    }
    float MedianRing(float radius)
    {
     var values=Ring(radius);Array.Sort(values);return values[angles/2];
    }
    void Check(bool pass,string message)
    {
     if(!pass)throw new InvalidOperationException("Solar lens: "+message);
     lines.Add("PASS solar lens "+message);
    }

    Render(Vector2.zero,Texture2D.whiteTexture);
    int whitePixels=0;
    float threshold=linear?Mathf.GammaToLinearSpace(.97f):.97f;
    foreach(var c in readback.GetPixels())if(Mathf.Min(c.r,Mathf.Min(c.g,c.b))>=threshold)whitePixels++;
    float whiteRadius=Mathf.Sqrt(whitePixels/Mathf.PI)/height;
    Check(whiteRadius>.065f&&whiteRadius<.12f,"white core radius / image height = "+whiteRadius.ToString("F3"));
    float inner=MedianRing(.15f),middle=MedianRing(.23f),outer=MedianRing(.34f);
    Check(inner>.62f&&inner<.96f&&middle>.40f&&outer>.20f&&inner-middle>.075f&&middle-outer>.060f,
      "halo luminance at .15/.23/.34 image heights = "+inner.ToString("F3")+"/"+middle.ToString("F3")+"/"+outer.ToString("F3"));
    var rays=Ring(.20f);
    int peaks=0;
    for(int i=0;i<angles;i++)
    {
     float value=rays[i];
     float wings=.5f*(rays[(i+angles-3)%angles]+rays[(i+3)%angles]);
     if(value>rays[(i+angles-1)%angles]&&value>=rays[(i+1)%angles]&&value-wings>.010f)peaks++;
    }
    Array.Sort(rays);
    float contrast=rays[angles*9/10]-rays[angles/10];
    Check(peaks>=7&&contrast>.035f&&contrast<.27f,"angular peaks="+peaks+" 90th–10th percentile contrast="+contrast.ToString("F3"));
    Render(Vector2.zero,partial);
    float attenuated=MedianRing(.23f);
    Check(attenuated>.02f&&attenuated<middle-.035f,"quarter-transmission halo="+attenuated.ToString("F3"));
    Render(new Vector2(.48f,.09f),Texture2D.blackTexture);
    float hidden=0;
    foreach(var c in readback.GetPixels())hidden=Mathf.Max(hidden,Mathf.Max(c.r,Mathf.Max(c.g,c.b)));
    Check(hidden<.005f,"opaque aperture peak="+hidden.ToString("F4"));

    foreach(var axis in new[]{new Vector2(.50f,.075f),new Vector2(-.46f,.19f)})
    {
     Render(axis,Texture2D.whiteTexture);
     var normal=new Vector2(-axis.y,axis.x).normalized;
     void CheckGhost(float position,float halfWidth)
     {
      var p=axis*position;
      float Across(float d)=>.5f*(Luma(Sample(p+d*normal))+Luma(Sample(p-d*normal)));
      var core=Sample(p);
      float centre=Luma(core),shoulder=Across(halfWidth*.50f),outside=Across(halfWidth);
      Check(core.r>core.b+.025f&&centre>outside+.018f&&centre>=shoulder-.012f&&shoulder>outside+.004f,
       "warm soft reflection at "+position+" on "+axis+": "+centre.ToString("F3")+"/"+shoulder.ToString("F3")+"/"+outside.ToString("F3"));
     }
     CheckGhost(.22f,.10f);CheckGhost(-.30f,.14f);CheckGhost(-1.30f,.22f);
    }
   }
   finally
   {
    RenderTexture.active=previous;GL.sRGBWrite=srgbWrite;
    RenderTexture.ReleaseTemporary(source);RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(partial);UnityEngine.Object.DestroyImmediate(readback);UnityEngine.Object.DestroyImmediate(lens);
   }
  }
 }
}
