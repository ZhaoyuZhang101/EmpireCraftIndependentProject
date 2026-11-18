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
    // Test the shared optical response with controlled incident light.
    var lens=new Material(Resources.Load<Shader>("ORBIS/CelestialLens"));
    const int size=192;
    var source=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
    var target=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
    var readback=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
    var active=RenderTexture.active;
    try
    {
     Graphics.Blit(Texture2D.blackTexture,source);
     lens.SetFloat("_Exposure",1);lens.SetVector("_Sun",Vector3.back);lens.SetVector("_CameraLocal",Vector3.forward*3);
     foreach(string ray in new[]{"_RayBL","_RayBR","_RayTL","_RayTR"})lens.SetVector(ray,Vector3.forward);
     void RenderLens(Vector2 sun,Texture transmission)
     {
      lens.SetVector("_LensSun",new Vector4(sun.x,sun.y,1,1));lens.SetTexture("_LensTransmission",transmission);
      Graphics.Blit(source,target,lens);RenderTexture.active=target;
      readback.ReadPixels(new Rect(0,0,size,size),0,0);readback.Apply();
     }
     var centre=new Vector2(.5f,.5f);
     RenderLens(centre,Texture2D.whiteTexture);
     float lit=readback.GetPixel(size/2,size/2).r;
     RenderLens(centre,Texture2D.blackTexture);
     float hidden=readback.GetPixel(size/2,size/2).r;
     if(lit<.15f||hidden>.01f)throw new Exception("Solar lens radiance invalid: visible="+lit+", hidden="+hidden);
     lines.Add("PASS solar lens visible="+lit+" hidden="+hidden);
     foreach(var offset in new[]{new Vector2(.30f,.20f),new Vector2(-.24f,.28f)})
     {
      RenderLens(centre+offset,Texture2D.whiteTexture);
      Color Sample(Vector2 uv)=>readback.GetPixelBilinear(uv.x,uv.y);
      var near=Sample(centre-offset*.46f);
      var far=Sample(centre-offset*1.06f);
      var beside=Sample(centre+new Vector2(-offset.y,offset.x)*.46f);
      if(near.b-near.r<.012f||near.b-beside.b<.012f||far.r-far.b<.012f)
       throw new Exception("Lens reflections do not follow the solar axis: near="+near+" far="+far+" beside="+beside);
     }
     lines.Add("PASS cool and warm reflections track two off-axis sun positions");
    }
    finally{RenderTexture.active=active;RenderTexture.ReleaseTemporary(source);RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(readback);UnityEngine.Object.DestroyImmediate(lens);}
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
 }
}
