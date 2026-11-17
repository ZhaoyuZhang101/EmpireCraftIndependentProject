using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Orbis.Planet;
namespace Orbis.Editor
{
 public static class PolarDetailRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate polar detail scale")]
  public static void Run()
  {
   var m=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PolarDetailProbe.shader"));
   var rt=RenderTexture.GetTemporary(128,128,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);var old=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;var lines=new List<string>();
   try
   {
    m.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));ShaderUtil.CompilePass(m,0,true);
    foreach(float lat in new[]{0f,60f,-60f,80f,-80f,89f,-89f})
    {
     m.SetFloat("_Latitude",lat);Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
     double x=0,y=0;foreach(var p in image.GetPixels()){x+=p.r;y+=p.g;}double ratio=x/y;
     lines.Add((ratio>.95&&ratio<1.05?"PASS":"FAIL")+" equal surface distance has equal artwork scale at latitude="+lat+" ratio="+ratio);
    }
    m.SetFloat("_ProjectionProbe",1);m.SetFloat("_ScrollMorph",1);
    foreach(float lat in new[]{0f,60f,-60f,85f,-85f,89.99f,-89.99f,90f,-90f})
    {
     var frame=PlanetCoordinateSystem.LocalFrame(new PlanetLocation(.7,lat*Math.PI/180),new PlanetVector(),1);
     m.SetVector("_LocalCenter",Vector(frame.Up));m.SetVector("_LocalEast",Vector(frame.East));m.SetVector("_LocalNorth",Vector(frame.North));
     foreach(bool corner in new[]{false,true})
     {
      float span=corner?.02f:.001f;
      m.SetVector("_ProbeSpan",new Vector4(span,span,0,0));
      m.SetVector("_ProbeOrigin",corner?new Vector4(.4f,.2f,0,0):Vector4.zero);
      Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();
      double stretch=0,shear=0,maximumError=0;bool finite=true;
      foreach(var pixel in image.GetPixels())
      {
       stretch+=Math.Abs(pixel.r-1);shear+=pixel.g;maximumError=Math.Max(maximumError,pixel.b);
       finite&=!float.IsNaN(pixel.r+pixel.g+pixel.b)&&!float.IsInfinity(pixel.r+pixel.g+pixel.b);
      }
      stretch/=128*128;shear/=128*128;
      bool pass=finite&&stretch<.001&&shear<.001&&maximumError<.2;
      lines.Add((pass?"PASS":"FAIL")+" camera-local projection preserves shape and geographic precision; latitude="+lat+" corner="+corner+" stretch="+stretch+" shear="+shear+" pixelError="+maximumError);
     }
    }
    File.WriteAllLines("Documentation/LocalPlanetUpgrade/polar-detail-regression.txt",lines);
    if(lines.Exists(line=>line.StartsWith("FAIL",StringComparison.Ordinal)))throw new Exception(string.Join("\n",lines));
   }
   finally {RenderTexture.active=old;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(image);}
  }
  static Vector3 Vector(PlanetVector value){return new Vector3((float)value.X,(float)value.Y,(float)value.Z);}
 }
}
