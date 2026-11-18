using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
 public static class WaterOpticsRegression
 {
  const int MapWidth=32,MapHeight=16,ImageWidth=128,ImageHeight=8;

  [MenuItem("Tools/ORBIS/Local Planet/Validate continuous wet-water optics")]
  public static void Run()
  {
   var report=new List<string>();
   var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/WaterOpticsProbe.shader"));
   var target=RenderTexture.GetTemporary(ImageWidth,ImageHeight,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(ImageWidth,ImageHeight,TextureFormat.RGBAFloat,false,true);
   var hydro=DataTexture(MapWidth,MapHeight);
   var geo=DataTexture(MapWidth,MapHeight);
   var mixing=DataTexture(MapWidth,MapHeight);
   var info=new Texture2D(MapWidth,MapHeight,TextureFormat.RGBA32,false,true)
   {filterMode=FilterMode.Point,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
   var ocean=SolidTexture(new Color(.2f,.5f,1,1));
   var lake=SolidTexture(new Color(.8f,.5f,1,1));
   var atlas=DataTexture(64,48);
   var previous=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;
   try
   {
    ShaderUtil.allowAsyncCompilation=false;
    material.SetVector("_MapSize",new Vector4(MapWidth,MapHeight,1f/MapWidth,1f/MapHeight));
    material.SetTexture("_HydroA",hydro);material.SetTexture("_GeoTex",geo);
    material.SetTexture("_HydroB",mixing);material.SetTexture("_InfoTex",info);
    material.SetTexture("_VendorOceanNormal",ocean);material.SetTexture("_VendorLakeNormal",lake);
    material.SetTexture("_VendorStreamNormal",ocean);
    material.SetTexture("_VendorOceanFoam",Texture2D.blackTexture);material.SetTexture("_VendorRiverFoam",Texture2D.blackTexture);
    material.SetFloat("_VendorWaterReady",1);material.SetFloat("_FieldReady",1);material.SetFloat("_WaterClock",3);
    material.SetFloat("_Bathymetry",0);material.SetFloat("_Relief",0);
    var water=new Color[MapWidth*MapHeight];
    var waterMixing=new Color[water.Length];
    var semantics=new Color32[water.Length];
    for(int y=0;y<MapHeight;y++)for(int x=0;x<MapWidth;x++)
    {
     int index=y*MapWidth+x;
     water[index]=new Color(x<16?.7f:0,0,0,0);
     waterMixing[index]=x<16?new Color(0,.2f,1,0):new Color(0,1,.33f,0);
     semantics[index]=new Color32(0,(byte)(x<16?4:0),0,0);
    }
    hydro.SetPixels(water);hydro.Apply();
    mixing.SetPixels(waterMixing);mixing.Apply();info.SetPixels32(semantics);info.Apply();
    SetSubstrate(geo,1,1);
    ShaderUtil.CompilePass(material,0,true);

    material.SetFloat("_ProbeMode",0);
    var transition=Sample(material,target,image);
    float kindJump=MaximumChannelDifference(transition[ImageWidth*4+31],transition[ImageWidth*4+95]);
    Check(report,kindJump<.003f,"lake/ocean SW3 normal blend remains continuous at kind 3.5, RGB jump="+kindJump);

    material.SetFloat("_ProbeMode",1);
    SetSubstrate(geo,1,3);var rockBank=Sample(material,target,image);
    SetSubstrate(geo,1,5);var snowBank=Sample(material,target,image);
    float dryBankError=MaximumDifference(rockBank,snowBank);
    Check(report,dryBankError<.0001f,"dry-bank material does not tint reconstructed area-water bed, RGB change="+dryBankError);
    Check(report,Finite(transition)&&Finite(rockBank)&&Finite(snowBank),"continuous optics produce finite nonnegative radiance");

    material.SetFloat("_ProbeMode",2);
    foreach(var position in new[]{new Vector2(16,8),new Vector2(0,.001f),new Vector2(MapWidth,MapHeight-.001f)})
    {
     material.SetVector("_ProbeCell",new Vector4(position.x,position.y,0,0));
     var metadata=Sample(material,target,image);
     float error=0;
     foreach(var value in metadata)
      error=Mathf.Max(error,Mathf.Abs(value.r-.7f),Mathf.Abs(value.g-.2f),Mathf.Abs(value.b-1),Mathf.Abs(value.a-4));
     Check(report,error<.0006f,"wet-only depth, sediment, freshness and lake type survive a dry semantic boundary at "+position+", maximum error="+error);
    }

    var channel=new Color[64*48];channel[4*64+16]=new Color(1,0,0,0);
    for(int y=8;y<43;y++)for(int x=0;x<35;x++)channel[y*64+x]=new Color(-.1f,3,.2f,0);
    atlas.SetPixels(channel);atlas.Apply();
    material.SetTexture("_RiverAtlas",atlas);material.SetFloat("_RiverAtlasReady",1);
    material.SetVector("_RiverAtlasLayout",new Vector4(64,48,35,8));
    material.SetFloat("_ProbeMode",3);material.SetFloat("_FieldReady",0);
    var mouth=Sample(material,target,image);
    float mouthJump=MaximumChannelDifference(mouth[ImageWidth*4+31],mouth[ImageWidth*4+95]);
    Check(report,mouthJump<.002f,"lake inlet and outlet retain channel depth across the shoreline, RGB jump="+mouthJump);
    CheckMouthReadability(report,target,image,atlas);
    if(report.Exists(line=>line.StartsWith("FAIL")))throw new InvalidOperationException(string.Join("\n",report));
   }
   finally
   {
    Directory.CreateDirectory("ValidationOutput/WaterOptics");
    File.WriteAllLines("ValidationOutput/WaterOptics/water-optics-regression.txt",report);
    RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(target);
    foreach(var resource in new UnityEngine.Object[]{material,image,hydro,geo,mixing,info,ocean,lake,atlas})UnityEngine.Object.DestroyImmediate(resource);
   }
  }

  // Exercise the final map pass too: the cartographic river colour is applied after water optics.
  static void CheckMouthReadability(List<string> report,RenderTexture target,Texture2D image,Texture2D atlas)
  {
   var map=new Material(Resources.Load<Shader>("ORBIS/FlatWorld/FlatMap"));
   var visual=SolidTexture(new Color(0,.12f,12,0));
   try
   {
    var field=new Color[atlas.width*atlas.height];field[0]=new Color(1,0,0,0);
    for(int y=8;y<43;y++)for(int x=0;x<35;x++)
        field[y*atlas.width+x]=new Color(Mathf.Abs((x-17)/32f)-.06f,2,0,.06f);
    atlas.SetPixels(field);atlas.Apply();
    map.SetTexture("_VisualTex",visual);map.SetTexture("_RiverAtlas",atlas);map.SetFloat("_RiverAtlasReady",1);
    map.SetVector("_MapSize",Vector4.one);map.SetVector("_RiverAtlasLayout",new Vector4(64,48,35,8));
    map.SetVector("_Selected",new Vector4(-20,-20,-20,-20));map.SetVector("_FlatSun",new Vector4(-.4f,.4f,1,0));
    map.SetFloat("_WaterView",1);map.SetFloat("_ShowWater",1);map.SetFloat("_WaveAmount",0);
    ShaderUtil.CompilePass(map,0,true);
    float TintDifference(float pixels)
    {
     map.SetFloat("_PixelsPerTile",pixels);map.SetFloat("_RiverTint",0);var plain=Sample(map,target,image);
     map.SetFloat("_RiverTint",.38f);return MaximumDifference(plain,Sample(map,target,image));
    }
    float inLake=TintDifference(4);
    var inland=new Color(0,-1,0,0);
    visual.SetPixels(new[]{inland,inland,inland,inland});visual.Apply();
    float near=TintDifference(96),far=TintDifference(4);
    Check(report,inLake<.002f&&near<.002f&&far>.009f,
      "map tint dissolves in the receiving basin and on resolved channels; basin="+inLake+", close="+near+", distant="+far);
   }
   finally{UnityEngine.Object.DestroyImmediate(map);UnityEngine.Object.DestroyImmediate(visual);}
  }

  static Texture2D DataTexture(int width,int height)
  {
   return new Texture2D(width,height,TextureFormat.RGBAHalf,false,true)
   {filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
  }

  static Texture2D SolidTexture(Color colour)
  {
   var texture=DataTexture(2,2);texture.SetPixels(new[]{colour,colour,colour,colour});texture.Apply();return texture;
  }

  static void SetSubstrate(Texture2D texture,float wetMaterial,float dryMaterial)
  {
   var pixels=new Color[MapWidth*MapHeight];
   for(int y=0;y<MapHeight;y++)for(int x=0;x<MapWidth;x++)pixels[y*MapWidth+x]=new Color(0,x<16?wetMaterial:dryMaterial,0,0);
   texture.SetPixels(pixels);texture.Apply();
  }

  static Color[] Sample(Material material,RenderTexture target,Texture2D image)
  {
   Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
   image.ReadPixels(new Rect(0,0,ImageWidth,ImageHeight),0,0);image.Apply();return image.GetPixels();
  }

  static float MaximumChannelDifference(Color a,Color b)
  {return Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));}

  static float MaximumDifference(Color[] a,Color[] b)
  {
   float result=0;
   for(int i=0;i<a.Length;i++)result=Mathf.Max(result,MaximumChannelDifference(a[i],b[i]));
   return result;
  }

  static bool Finite(Color[] pixels)
  {
   foreach(var pixel in pixels)
    if(float.IsNaN(pixel.r+pixel.g+pixel.b)||float.IsInfinity(pixel.r+pixel.g+pixel.b)||pixel.r<0||pixel.g<0||pixel.b<0)return false;
   return true;
  }

  static void Check(List<string> report,bool passed,string description)
  {report.Add((passed?"PASS ":"FAIL ")+description);}
 }
}
