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
     try{for(int p=0;p<material.passCount;p++){ShaderUtil.CompilePass(material,p,true);if(!ShaderUtil.IsPassCompiled(material,p))throw new Exception("Not compiled "+name+" pass "+p);}
      foreach(var message in ShaderUtil.GetShaderMessages(shader))if(message.severity.ToString()=="Error")throw new Exception(name+": "+message.message);
      lines.Add("PASS shader "+name+" / "+material.passCount+" passes");}finally{UnityEngine.Object.DestroyImmediate(material);}
    }
    // A compiled lens shader can still render an imperceptible sun. Check the
    // output radiance with the same material path used by OnRenderImage.
    var lens=new Material(Resources.Load<Shader>("ORBIS/CelestialLens"));
    var source=RenderTexture.GetTemporary(64,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
    var target=RenderTexture.GetTemporary(64,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
    var readback=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
    var active=RenderTexture.active;
    try
    {
     Graphics.Blit(Texture2D.blackTexture,source);lens.SetVector("_LensSun",new Vector4(.5f,.5f,1,1));
     Graphics.Blit(source,target,lens);RenderTexture.active=target;
     readback.ReadPixels(new Rect(0,0,64,64),0,0);readback.Apply();
     float lit=readback.GetPixel(31,31).r;
     lens.SetVector("_LensSun",new Vector4(.5f,.5f,0,1));Graphics.Blit(source,target,lens);
     readback.ReadPixels(new Rect(0,0,64,64),0,0);readback.Apply();
     float hidden=readback.GetPixel(31,31).r;
     if(lit<.15f||hidden>.01f)throw new Exception("Solar lens radiance invalid: visible="+lit+", hidden="+hidden);
     lines.Add("PASS solar lens visible="+lit+" hidden="+hidden);
    }
    finally{RenderTexture.active=active;RenderTexture.ReleaseTemporary(source);RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(readback);UnityEngine.Object.DestroyImmediate(lens);}
    var data=Resources.Load<TextAsset>("ORBIS/WorldAtlas/Default");var w=FlatAtlasTerrain.Read(data.bytes,"ORBIS-2D-2026",default);FlatSave.Validate(w);
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
