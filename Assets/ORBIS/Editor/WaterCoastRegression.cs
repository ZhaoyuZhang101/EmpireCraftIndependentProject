using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Orbis.Editor
{
 public static class WaterCoastRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate smooth coast water style")]
  public static void Run()
  {
   var m=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/WaterCoastProbe.shader"));
   var info=new Texture2D(512,256,TextureFormat.RGBA32,false,true);var pixels=new Color32[512*256];
   for(int y=0;y<256;y++)for(int x=0;x<512;x++)pixels[y*512+x]=new Color32(0,(byte)(x<183?4:0),0,0);
   info.SetPixels32(pixels);info.Apply();info.filterMode=FilterMode.Point;info.wrapMode=TextureWrapMode.Repeat;
   var rt=RenderTexture.GetTemporary(128,4,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(128,4,TextureFormat.RGBAFloat,false,true);var old=RenderTexture.active;
   var geo=new Texture2D(512,256,TextureFormat.RGBAFloat,false,true);
   bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   try
   {
    m.SetTexture("_InfoTex",info);m.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));
    Orbis.Planet.OrbisAssetVisuals.BindWater(m);m.SetTexture("_VendorOceanFoam",Texture2D.whiteTexture);m.SetTexture("_VendorRiverFoam",Texture2D.whiteTexture);
    m.SetFloat("_WaterClock",3);ShaderUtil.CompilePass(m,0,true);Graphics.Blit(Texture2D.whiteTexture,rt,m);
    RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,4),0,0);image.Apply();
    var a=image.GetPixel(31,2);var b=image.GetPixel(95,2);float gap=Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));
    string report=(gap<.003f?"PASS":"FAIL")+" smoothed lake shoreline across land semantic cell, colour jump="+gap;
    m.SetFloat("_ProbeMode",2);m.SetFloat("_WaterClock",0);
    Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,4),0,0);image.Apply();
    int firstPeak=0;float firstValue=0,farFoam=image.GetPixel(0,2).r+image.GetPixel(127,2).r;
    for(int x=0;x<58;x++)if(image.GetPixel(x,2).r>firstValue){firstValue=image.GetPixel(x,2).r;firstPeak=x;}
    m.SetFloat("_WaterClock",1);
    Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,4),0,0);image.Apply();
    int laterPeak=0;float laterValue=0;
    for(int x=0;x<58;x++)if(image.GetPixel(x,2).r>laterValue){laterValue=image.GetPixel(x,2).r;laterPeak=x;}
    bool crestValid=farFoam<.0001f&&firstValue>.5f&&laterValue>.5f&&laterPeak<firstPeak-1;
    report+="\n"+(crestValid?"PASS":"FAIL")+" crests advance toward shore and exclude inland/deep water; peak="+firstPeak+" -> "+laterPeak;
    m.SetFloat("_ProbeMode",3);
    Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,4),0,0);image.Apply();
    float shoreError=0;
    foreach(var point in image.GetPixels())
    {
     float expected=Orbis.Flat.FlatSurfaceSampling.ShoreDetail(point.g,point.b,512,256);
     shoreError=Mathf.Max(shoreError,Mathf.Abs(expected-point.r));
    }
    report+="\n"+(shoreError<.0002f?"PASS":"FAIL")+" shared CPU/GPU continuous shoreline across longitude seam; max error="+shoreError;
    var substrate=new Color[pixels.Length];
    for(int i=0;i<substrate.Length;i++)substrate[i]=new Color(0,i%512<183?0:3,0,0);
    geo.SetPixels(substrate);geo.Apply();m.SetTexture("_GeoTex",geo);m.SetFloat("_ProbeMode",4);
    Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,4),0,0);image.Apply();
    a=image.GetPixel(31,2);b=image.GetPixel(95,2);float bedJump=Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));
    report+="\n"+(bedJump<.003f?"PASS":"FAIL")+" submerged substrate colour is continuous across material cells; jump="+bedJump;
    m.SetFloat("_ProbeMode",5);m.SetFloat("_Bathymetry",1);
    Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,4),0,0);image.Apply();
    float mouthJump=0;
    for(int y=0;y<4;y++)
    {
     a=image.GetPixel(31,y);b=image.GetPixel(95,y);
     mouthJump=Mathf.Max(mouthJump,Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));
    }
    report+="\n"+(mouthJump<.0001f?"PASS":"FAIL")+" ocean/lake depth stays continuous across a channel semantic cell; jump="+mouthJump;
    var snow=new Color[pixels.Length];
    for(int y=0;y<256;y++)for(int x=0;x<512;x++){pixels[y*512+x]=new Color32(0,(byte)(x<183?0:3),0,0);snow[y*512+x]=new Color(0,x<183?5:0,0,0);}
    info.SetPixels32(pixels);info.Apply();geo.SetPixels(snow);geo.Apply();geo.filterMode=FilterMode.Point;
    m.SetTexture("_GeoTex",geo);m.SetFloat("_ProbeMode",1);Graphics.Blit(Texture2D.whiteTexture,rt,m);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,4),0,0);image.Apply();
    a=image.GetPixel(31,2);b=image.GetPixel(95,2);
    report+="\n"+(a.r>.99f&&b.r>.99f?"PASS":"FAIL")+" snow artwork extends into reconstructed dry part of coastal water cell; coverage="+a.r+","+b.r;
    report+="\n"+SedimentChecks(m,geo);
    File.WriteAllText("Documentation/LocalPlanetUpgrade/water-coast-regression.txt",report);
    if(report.Contains("FAIL"))throw new InvalidOperationException(report);
   }
   finally {RenderTexture.active=old;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(info);UnityEngine.Object.DestroyImmediate(geo);UnityEngine.Object.DestroyImmediate(image);}
  }

  const int PatchSize=96;

  static string SedimentChecks(Material material,Texture2D geo)
  {
   var report=new System.Collections.Generic.List<string>();
   var visual=new Texture2D(512,256,TextureFormat.RGBAFloat,false,true)
   {filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
   var target=RenderTexture.GetTemporary(PatchSize,PatchSize,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(PatchSize,PatchSize,TextureFormat.RGBAFloat,false,true);
   var previous=RenderTexture.active;
   try
   {
    var substrate=new Color[512*256];var samples=new Color[substrate.Length];
    for(int y=0;y<256;y++)for(int x=0;x<512;x++)
    {
     int i=y*512+x;
     substrate[i]=new Color(0,1,.88f,0);
     samples[i]=new Color(0,y+.5f-128,Mathf.Max(5,120+(x+.5f-211)*58),0);
    }
    visual.SetPixels(samples);visual.Apply();geo.SetPixels(substrate);geo.Apply();
    material.SetTexture("_VisualTex",visual);material.SetTexture("_GeoTex",geo);
    material.SetFloat("_FieldReady",1);material.SetFloat("_Relief",1);material.SetFloat("_Bathymetry",1);
    material.SetFloat("_VendorWaterReady",0);material.SetFloat("_WaterClock",9);
    var openSea=new Vector4(12,0,0,3);
    var plume=new Vector4(12,.15f,.69f,3);
    var gentle=new Vector4(60,0,0,1);
    var shore=new Vector4(210.1f,128,0,0);
    material.SetVector("_CoastCondition",gentle);material.SetVector("_CoastOrigin",shore);

    Color[] Draw(float mode,Vector4 basin)
    {
     material.SetFloat("_ProbeMode",mode);material.SetVector("_CoastBasin",basin);
     Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
     image.ReadPixels(new Rect(0,0,PatchSize,PatchSize),0,0);return image.GetPixels();
    }
    void Check(bool valid,string label,float value)
    {report.Add((valid?"PASS ":"FAIL ")+label+" = "+value);}

    var sandy=Draw(6,openSea);var silty=Draw(6,plume);
    float contrast=0;int compared=0;
    for(int i=0;i<sandy.Length;i++)if(Mathf.Min(sandy[i].a,silty[i].a)>.65f)
    {contrast+=sandy[i].grayscale-silty[i].grayscale;compared++;}
    contrast/=Mathf.Max(1,compared);
    Check(contrast>.09f,"sand brighter than estuarine silt on the same ground",contrast);
    float anisotropy=TextureDirection(sandy)/Mathf.Max(.001f,TextureDirection(silty));
    Check(anisotropy>1.35f,"alongshore sand crests and seaward mud runnels differ in orientation",anisotropy);

    material.SetFloat("_WaterClock",55);var later=Draw(6,plume);
    float colourChange=0,coverageChange=0;
    for(int i=0;i<later.Length;i++)
    {colourChange=Mathf.Max(colourChange,Mathf.Abs(later[i].grayscale-silty[i].grayscale));coverageChange=Mathf.Max(coverageChange,Mathf.Abs(later[i].a-silty[i].a));}
    Check(colourChange>.012f&&coverageChange<.0001f,"tidal wetness changes with fixed coverage",colourChange);
    material.SetFloat("_WaterClock",9);
    material.SetVector("_CoastOrigin",shore+new Vector4(512,0,0,0));var wrapped=Draw(6,plume);
    float seam=0;for(int i=0;i<wrapped.Length;i++)seam=Mathf.Max(seam,Mathf.Abs(wrapped[i].grayscale-silty[i].grayscale),Mathf.Abs(wrapped[i].a-silty[i].a));
    Check(seam<.004f,"date-line repeat of both texture and coverage",seam);
    material.SetVector("_CoastOrigin",shore);

    material.SetVector("_CoastCondition",new Vector4(15,0,0,1));var lowShore=Draw(7,openSea);
    material.SetVector("_CoastCondition",new Vector4(120,0,0,1));var risingShore=Draw(7,openSea);
    float extraWidth=0;int drySamples=0;
    for(int i=0;i<lowShore.Length;i++)
    {
     if(lowShore[i].a>=0||lowShore[i].a<=-.34f)continue;
     extraWidth+=lowShore[i].r-risingShore[i].r;drySamples++;
    }
    Check(drySamples>0&&extraWidth/drySamples>.09f,"gentle beach extends farther inland than a rising shore",extraWidth/Mathf.Max(1,drySamples));
    material.SetVector("_CoastCondition",gentle);
    float excluded=0;
    foreach(var kind in new[]{4,5})
    {var basin=plume;basin.w=kind;foreach(var c in Draw(7,basin))excluded=Mathf.Max(excluded,c.r);}
    // Frozen land/sea, a rock cliff, the interior of a river mouth and missing wet metadata.
    foreach(var condition in new[]{new Vector4(60,1,0,1),new Vector4(900,0,0,1),new Vector4(60,0,-.22f,1),new Vector4(60,0,0,0)})
    {material.SetVector("_CoastCondition",condition);foreach(var c in Draw(7,plume))excluded=Mathf.Max(excluded,c.r);}
    Check(excluded<.0001f,"marine coast excluded from lakes, rivers, ice, cliffs and missing basin cells",excluded);
    material.SetVector("_CoastCondition",gentle);
    var sandWater=Draw(9,openSea);var mudWater=Draw(9,plume);
    float opticalDifference=0;
    for(int i=0;i<sandWater.Length;i++)
     opticalDifference=Mathf.Max(opticalDifference,Mathf.Abs(sandWater[i].grayscale-mudWater[i].grayscale));
    Check(opticalDifference>.035f,"submerged coastal materials survive water-column optics",opticalDifference);
    material.SetVector("_CoastOrigin",shore+new Vector4(0,4,0,0));
    var deepSand=Draw(9,new Vector4(1200,0,0,3));var deepMud=Draw(9,new Vector4(1200,.3f,.9f,3));
    float offshore=0;
    for(int i=0;i<deepSand.Length;i++)
     offshore=Mathf.Max(offshore,deepSand[i].a,Mathf.Abs(deepSand[i].grayscale-deepMud[i].grayscale));
    Check(offshore<.0001f&&deepSand[0].b>deepSand[0].r,"deep ocean excludes littoral pigments",offshore);
    material.SetVector("_CoastOrigin",shore);
    material.SetVector("_BedSun",new Vector4(.72f,0,.65f,0));var lit=Draw(8,openSea);
    material.SetVector("_BedSun",new Vector4(-.72f,0,.65f,0));var shaded=Draw(8,openSea);
    float direction=0;for(int i=0;i<lit.Length;i++)direction+=lit[i].r-shaded[i].r;
    direction/=lit.Length;
    Check(direction>.18f&&lit[0].g>40,"measured seabed slope responds to reversed sun",direction);

    // Narrow the water ahead of both flanks without changing sediment or salinity.
    var openStyle=Draw(7,openSea);
    for(int y=0;y<256;y++)for(int x=0;x<512;x++)
    {int i=y*512+x;samples[i].g=Mathf.Min(samples[i].g,.65f-Mathf.Abs(x+.5f-210.8f));}
    visual.SetPixels(samples);visual.Apply();var bay=Draw(7,openSea);
    float shelter=0;for(int i=0;i<bay.Length;i++)shelter+=bay[i].g-openStyle[i].g;
    shelter/=bay.Length;
    Check(shelter>.13f,"sheltered wet inlet accumulates more silt than exposed coast",shelter);
   }
   finally
   {
    RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(visual);UnityEngine.Object.DestroyImmediate(image);
   }
   return string.Join("\n",report);
  }

  static float TextureDirection(Color[] patch)
  {
   float across=0,along=0;
   for(int y=1;y<PatchSize;y++)for(int x=1;x<PatchSize;x++)
   {
    int i=y*PatchSize+x;
    if(Mathf.Min(patch[i].a,patch[i-1].a,patch[i-PatchSize].a)<.65f)continue;
    across+=Mathf.Abs(patch[i].grayscale-patch[i-PatchSize].grayscale);
    along+=Mathf.Abs(patch[i].grayscale-patch[i-1].grayscale);
   }
   return across/Mathf.Max(.001f,along);
  }
 }
}
