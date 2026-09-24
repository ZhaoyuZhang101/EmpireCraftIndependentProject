using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Orbis.Flat;
namespace Orbis.Editor
{
 public static class RiverGeometryRegression
 {
  public static void Run()
  {
   var world=new WorldData(64,32,"river-shape",TerrainPreset.Continents);world.Facts=new FlatTerrainFacts(world.Count);
   var a=new Color[world.Count];var b=new Color[world.Count];
   for(int i=0;i<world.Count;i++)
   {world.Facts.HalfWidth[i]=.025f+(i%7)*.022f;world.Facts.Connections[i]=3;world.Facts.FlowX[i]=1;a[i]=new Color(1,1,0,world.Facts.HalfWidth[i]);b[i]=new Color(0,0,0,3);}
   var ta=new Texture2D(64,32,TextureFormat.RGBAFloat,false,true);var tb=new Texture2D(64,32,TextureFormat.RGBAFloat,false,true);
   ta.filterMode=tb.filterMode=FilterMode.Point;ta.wrapMode=tb.wrapMode=TextureWrapMode.Repeat;ta.SetPixels(a);tb.SetPixels(b);ta.Apply();tb.Apply();
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/RiverGeometryProbe.shader");var m=new Material(shader);
   var rt=RenderTexture.GetTemporary(128,128,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;var image=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
   bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   try
   {
    ShaderUtil.CompilePass(m,0,true);m.SetTexture("_HydroA",ta);m.SetTexture("_HydroB",tb);m.SetVector("_MapSize",new Vector4(64,32,1f/64,1f/32));
    // Include longitude wrap; matching tests prevent decorative splines diverging from gameplay coverage.
    m.SetVector("_ProbeOrigin",new Vector4(63,12,0,0));Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
    var pixels=image.GetPixels();float maximum=0;int invalid=0;string detail="";
    for(int y=0;y<128;y++)for(int x=0;x<128;x++)
    {
     var p=pixels[y*128+x];float cx=63+p.g*2,cy=12+p.b*2;int ix=(int)cx,iy=(int)cy;
     float expected=FlatWaterGeometry.RiverDistance(world,world.Index(ix,iy),cx-ix,cy-iy);
     if(float.IsNaN(p.r+p.g+p.b)||float.IsInfinity(p.r+p.g+p.b))invalid++;
     if(Mathf.Abs(p.r-expected)>maximum){maximum=Mathf.Abs(p.r-expected);detail=" cell="+cx+","+cy+" GPU="+p.r+" CPU="+expected+" width="+p.a+" expectedWidth="+world.Facts.HalfWidth[world.Index(ix,iy)];}
    }
    string result=(maximum<.0001f&&invalid==0?"PASS":"FAIL")+" CPU/GPU meandering variable-width channel field, longitude wrap; samples=16384 maximumError="+maximum+" invalid="+invalid+detail;
    File.WriteAllText("Documentation/LocalPlanetUpgrade/river-geometry-regression.txt",result);
    if(maximum>=.0001f||invalid>0)throw new Exception(result);
   }
   finally{ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(ta);UnityEngine.Object.DestroyImmediate(tb);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(image);}
  }
 }
}
