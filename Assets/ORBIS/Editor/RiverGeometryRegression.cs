using System;
using System.IO;
using System.Collections.Generic;
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
   {world.Facts.HalfWidth[i]=.025f+(i%7)*.022f;world.Facts.Meander[i]=.06f*Mathf.Sin(i*.3f);world.Facts.Connections[i]=(byte)(i%3==0?2:3);world.Facts.FlowX[i]=1;a[i]=new Color(1,1,0,world.Facts.HalfWidth[i]);b[i]=new Color(world.Facts.Meander[i],0,0,world.Facts.Connections[i]);}
   var ta=new Texture2D(64,32,TextureFormat.RGBAFloat,false,true);var tb=new Texture2D(64,32,TextureFormat.RGBAFloat,false,true);
   ta.filterMode=tb.filterMode=FilterMode.Point;ta.wrapMode=tb.wrapMode=TextureWrapMode.Repeat;ta.SetPixels(a);tb.SetPixels(b);ta.Apply();tb.Apply();
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/RiverGeometryProbe.shader");var m=new Material(shader);
   var rt=RenderTexture.GetTemporary(128,128,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;var image=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
   bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   var report=new List<string>();
   try
   {
    ShaderUtil.CompilePass(m,0,true);m.SetTexture("_HydroA",ta);m.SetTexture("_HydroB",tb);m.SetVector("_MapSize",new Vector4(64,32,1f/64,1f/32));
    for(int scenario=0;scenario<2;scenario++)
    {
     if(scenario==1)
     {
      for(int i=0;i<world.Count;i++)
      {
       world.Facts.HalfWidth[i]=FlatTerrainAnalysis.RiverHalfWidth(1000000);world.Facts.Meander[i]=-.08f;
       world.Facts.Connections[i]=12;world.Facts.FlowX[i]=0;world.Facts.FlowY[i]=1;
       a[i]=new Color(1,0,1,world.Facts.HalfWidth[i]);b[i]=new Color(-.08f,0,0,12);
      }
      ta.SetPixels(a);tb.SetPixels(b);ta.Apply();tb.Apply();
     }
     // Include the date line and the wide-river closed-face safety bound.
     int originX=scenario==0?63:16,originY=scenario==0?12:5;
     m.SetVector("_ProbeOrigin",new Vector4(originX,originY,0,0));Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
     var pixels=image.GetPixels();float maximum=0;int invalid=0;string detail="";
     for(int y=0;y<128;y++)for(int x=0;x<128;x++)
     {
      var p=pixels[y*128+x];float cx=originX+p.g*2,cy=originY+p.b*2;int ix=(int)cx,iy=(int)cy;
      float expected=FlatWaterGeometry.RiverDistance(world,world.Index(ix,iy),cx-ix,cy-iy);
      if(float.IsNaN(p.r+p.g+p.b)||float.IsInfinity(p.r+p.g+p.b))invalid++;
      if(Mathf.Abs(p.r-expected)>maximum){maximum=Mathf.Abs(p.r-expected);detail=" cell="+cx+","+cy+" GPU="+p.r+" CPU="+expected+" width="+p.a+" expectedWidth="+world.Facts.HalfWidth[world.Index(ix,iy)];}
     }
     string result=(maximum<.0001f&&invalid==0?"PASS":"FAIL")+" CPU/GPU channel geometry "+(scenario==0?"longitude wrap":"wide constrained meander")+"; samples=16384 maximumError="+maximum+" invalid="+invalid+detail;
     report.Add(result);
     if(maximum>=.0001f||invalid>0)throw new Exception(result);
    }
    CheckDiagonalMouth(m,ta,tb,rt,image,report);
   }
   finally{File.WriteAllLines("Documentation/LocalPlanetUpgrade/river-geometry-regression.txt",report);ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(ta);UnityEngine.Object.DestroyImmediate(tb);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(image);}
  }

  static void CheckDiagonalMouth(Material material,Texture2D hydro,Texture2D mixing,RenderTexture target,Texture2D image,List<string> report)
  {
   var world=new WorldData(64,32,"DIAGONAL-BASIN",TerrainPreset.Continents);
   int river=world.Index(20,14),basin=world.Index(21,15);
   for(int i=0;i<world.Count;i++){world.Elevation[i]=100;world.WaterLevel[i]=100;world.DrainOrder[i]=i;}
   world.Water[river]=WaterKind.River;world.Runoff[river]=64;
   world.Water[basin]=WaterKind.Lake;world.Elevation[basin]=98;
   var visual=new Texture2D(64,32,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear};
   try
   {
    material.SetTexture("_VisualTex",visual);material.SetFloat("_ProbeMouth",1);
    material.SetVector("_ProbeOrigin",new Vector4(20,14,0,0));
    for(int scenario=0;scenario<3;scenario++)
    {
     bool inlet=scenario==0,outlet=scenario==1;
     world.Downstream[river]=inlet?basin:-1;world.Downstream[basin]=outlet?river:-1;
     FlatTerrainAnalysis.Build(world,default,false,1);
     var facts=world.Facts;
     // Exercise the same derived inputs with the optional curve cache absent.
     var preview=new FlatTerrainFacts(world.Count);
     Array.Copy(facts.Depth,preview.Depth,world.Count);Array.Copy(facts.HalfWidth,preview.HalfWidth,world.Count);
     Array.Copy(facts.Connections,preview.Connections,world.Count);Array.Copy(facts.ShoreDistance,preview.ShoreDistance,world.Count);
     Array.Copy(facts.FlowX,preview.FlowX,world.Count);Array.Copy(facts.FlowY,preview.FlowY,world.Count);
     Array.Copy(facts.Meander,preview.Meander,world.Count);world.Facts=preview;
     var a=new Color[world.Count];var b=new Color[world.Count];var field=new Color[world.Count];
     for(int i=0;i<world.Count;i++)
     {
      a[i]=new Color(facts.Depth[i],facts.FlowX[i],facts.FlowY[i],facts.HalfWidth[i]);
      b[i]=new Color(facts.Meander[i],0,0,facts.Connections[i]);
      field[i]=new Color(world.Elevation[i],facts.ShoreDistance[i],facts.Depth[i],0);
     }
     hydro.SetPixels(a);hydro.Apply();mixing.SetPixels(b);mixing.Apply();visual.SetPixels(field);visual.Apply();
     Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
     image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
     int centre=0,flanks=0,invalid=0;float maxDistanceError=0;
     foreach(var p in image.GetPixels())
     {
      float tx=p.g*2,ty=p.b*2;
      if(Mathf.Abs(tx-ty)>.065f||tx<.60f||tx>1.45f)continue;
      if(!inlet&&!outlet&&(tx<.85f||tx>1.15f))continue;
      int ix=Mathf.FloorToInt(tx),iy=Mathf.FloorToInt(ty),tile=world.Index(20+ix,14+iy);
      float cpu=Mathf.Min(FlatWaterGeometry.RiverDistance(world,tile,tx-ix,ty-iy),FlatWaterGeometry.MouthDistance(world,tile,tx-ix,ty-iy));
      maxDistanceError=Mathf.Max(maxDistanceError,Mathf.Abs(p.r-cpu));
      if(inlet||outlet){if(p.r>=0||cpu>=0||(outlet?p.a>=0:p.a<=0))invalid++;}
      else if(p.r<=0||cpu<=0)invalid++;
      if(ix==iy)centre++;else flanks++;
     }
     bool ok=invalid==0&&centre>0&&flanks>0&&maxDistanceError<.001f;
     string kind=inlet?"lake inlet":outlet?"lake outlet":"unconnected neighbours";
     string result=(ok?"PASS ":"FAIL ")+"diagonal "+kind+" before atlas, centre/side samples="+centre+"/"+flanks+", invalid="+invalid+", CPU/GPU distance error="+maxDistanceError;
     report.Add(result);if(!ok)throw new InvalidOperationException(result);
    }
   }finally{UnityEngine.Object.DestroyImmediate(visual);}
  }
 }
}
