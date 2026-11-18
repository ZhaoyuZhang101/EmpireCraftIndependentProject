using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
 public static class TerrainQualityRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate terrain surface detail")]
  public static void Run()
  {
   var report=new List<string>();
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/TerrainQualityProbe.shader");
   var material=new Material(shader);
   var field=new Texture2D(16,8,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Repeat};
   var target=RenderTexture.GetTemporary(128,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(128,64,TextureFormat.RGBAFloat,false,true);
   var previous=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;
   try
   {
    ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(material,0,true);
    material.SetTexture("_VisualTex",field);
    material.SetVector("_MapSize",new Vector4(16,8,1f/16,1f/8));
    material.SetVector("_ProbeOrigin",new Vector4(5,3,0,0));
    material.SetFloat("_ProbePixels",48);material.SetFloat("_FieldReady",1);material.SetFloat("_Relief",1);
    Func<Color[]> sample=()=>
    {
     Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
     image.ReadPixels(new Rect(0,0,128,64),0,0);image.Apply();return image.GetPixels();
    };
    var pixels=new Color[128];
    for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(400,-3,0,.35f);
    field.SetPixels(pixels);field.Apply();var soil=sample();
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)pixels[y*16+x]=new Color(400+(x-5)*280,-3,0,.35f);
    field.SetPixels(pixels);field.Apply();var rock=sample();
    double rockResponse=Difference(soil,rock);
    Check(report,rockResponse>.04,"actual steep terrain exposes distinct rock and ridge detail; mean RGB change="+rockResponse);
    double nearContrast=Contrast(rock);
    material.SetFloat("_ProbePixels",2);double farContrast=Contrast(sample());
    Check(report,nearContrast>.012&&nearContrast>farContrast*1.5,"close terrain gains detail while distant detail filters out; contrast="+nearContrast+","+farContrast);
    material.SetFloat("_ProbePixels",48);material.SetFloat("_ProbeFlip",1);var mirrored=sample();
    double maximumError=0;string errorDetail="";
    for(int y=0;y<64;y++)for(int x=0;x<128;x++)
    {
     Color a=rock[y*128+x],b=mirrored[y*128+127-x];
     double difference=Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
     if(difference>maximumError){maximumError=difference;errorDetail=" at "+x+","+y+" original="+a+" reversed="+b;}
    }
    Check(report,maximumError<.005,"geographic terrain detail remains stable under reversed view; maximum RGB error="+maximumError+errorDetail);
    material.SetFloat("_ProbeFlip",0);
    int invalid=0;
    foreach(var origin in new[]{new Vector4(15.3f,.1f,0,0),new Vector4(-.7f,7,0,0)})
    {
     material.SetVector("_ProbeOrigin",origin);
     foreach(var color in sample())if(float.IsNaN(color.r+color.g+color.b)||float.IsInfinity(color.r+color.g+color.b)||color.r<0||color.g<0||color.b<0)invalid++;
    }
    Check(report,invalid==0,"terrain detail remains finite at polar and wrapped coordinates; invalid="+invalid);
    material.SetFloat("_ProbeMode",1);
    material.SetVector("_ProbeOrigin",new Vector4(7.49f,3.49f,0,0));
    material.SetVector("_ProbeSpan",new Vector4(.02f,.02f,0,0));
    for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(1800,-3,0,0);
    field.SetPixels(pixels);field.Apply();double plateau=Mean(sample());
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)
    {
     float distance=(x-7)*(x-7)+(y-3)*(y-3);
     pixels[y*16+x]=new Color(1800+70*distance,-3,0,0);
    }
    field.SetPixels(pixels);field.Apply();double valley=Mean(sample());
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)
    {
     float distance=(x-7)*(x-7)+(y-3)*(y-3);
     pixels[y*16+x]=new Color(1800-70*distance,-3,0,0);
    }
    field.SetPixels(pixels);field.Apply();double ridge=Mean(sample());
    Check(report,valley<plateau-.06&&ridge>plateau+.025,"relief distinguishes sheltered valleys, plateaus and convex ridges at equal elevation; shades="+valley+","+plateau+","+ridge);
    material.SetVector("_ProbeOrigin",new Vector4(23.49f,3.49f,0,0));
    Check(report,Math.Abs(Mean(sample())-ridge)<.001,"landform shading remains continuous across wrapped longitude");
    material.SetVector("_ProbeOrigin",new Vector4(7.49f,3.49f,0,0));
    var slopeShades=new List<double>();
    foreach(float slope in new[]{100f,-100f})
    {
     for(int y=0;y<8;y++)for(int x=0;x<16;x++)
      pixels[y*16+x]=new Color(1800+(x-7)*slope,-3,0,0);
     field.SetPixels(pixels);field.Apply();slopeShades.Add(Mean(sample()));
    }
    Check(report,slopeShades[0]-slopeShades[1]>.20,"moderate real-height slopes retain clear light and shade; shades="+slopeShades[0]+","+slopeShades[1]);
    material.SetFloat("_Relief",0);
    Check(report,Math.Abs(Mean(sample())-1)<.000001,"relief control removes all terrain-form shading");
    material.SetFloat("_Relief",1);material.SetVector("_ProbeSpan",Vector4.zero);
    invalid=0;
    foreach(var origin in new[]{new Vector4(15.3f,.1f,0,0),new Vector4(-.7f,7,0,0)})
    {
     material.SetVector("_ProbeOrigin",origin);
     foreach(var color in sample())if(float.IsNaN(color.r)||float.IsInfinity(color.r)||color.r<.58f||color.r>1.24f)invalid++;
    }
    Check(report,invalid==0,"multiscale relief stays bounded through poles and longitude seam; invalid="+invalid);
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)
     pixels[y*16+x]=new Color(2000+1000*Mathf.Cos(((x+.5f)/16-.5f)*Mathf.PI*2),-3,0,0);
    field.SetPixels(pixels);field.Apply();
    material.SetVector("_ProbeSpan",new Vector4(.00001f,.00001f,0,0));
    foreach(float boundary in new[]{2f,6f})
    {
     material.SetVector("_ProbeOrigin",new Vector4(8,boundary-.0002f,0,0));double beforePole=Mean(sample());
     material.SetVector("_ProbeOrigin",new Vector4(8,boundary+.0002f,0,0));double afterPole=Mean(sample());
     Check(report,Math.Abs(beforePole-afterPole)<.002,"geodesic relief crosses the polar stencil boundary without a ring; row="+boundary+" difference="+Math.Abs(beforePole-afterPole));
    }
    material.SetVector("_ProbeSpan",Vector4.zero);
    material.SetFloat("_ProbeMode",2);material.SetFloat("_ProbeCoverage",1);
    material.SetVector("_ProbeOrigin",new Vector4(7,3,0,0));
    double levelSnow=Mean(sample());material.SetVector("_ProbeGradient",new Vector4(1000,0,0,0));
    double steepSnow=Mean(sample());
    Check(report,levelSnow>.999&&steepSnow>.1&&steepSnow<.35,"snow accumulates on level ground while steep rock faces remain exposed; coverage="+levelSnow+","+steepSnow);
    material.SetVector("_ProbeGradient",new Vector4(300,0,0,0));
    double slopeSnow=Mean(sample());
    Check(report,slopeSnow>.35&&slopeSnow<.90,"moderate snowy slopes expose terrain structure; coverage="+slopeSnow);
    material.SetFloat("_ProbeCoverage",0);
    Check(report,Mean(sample())==0,"slope detail creates no snow outside the climate-derived snow cover");
    material.SetFloat("_ProbeCoverage",.5f);material.SetVector("_ProbeGradient",Vector4.zero);
    var snowEdge=sample();
    Check(report,Contrast(snowEdge)>.01,"snowline has continuous geographic breakup");
    material.SetFloat("_ProbePixels",1);
    Check(report,Contrast(sample())<.000001,"subpixel snowline detail filters out in distant views");
    material.SetFloat("_ProbePixels",48);
    material.SetVector("_ProbeOrigin",new Vector4(23,3,0,0));
    Check(report,Difference(snowEdge,sample())<.001,"snowline detail agrees across wrapped longitude");
    material.SetFloat("_ProbeMode",3);
    for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(1600,-3,0,0);
    field.SetPixels(pixels);field.Apply();var altitudeColor=sample();
    for(int i=0;i<pixels.Length;i++)pixels[i].r=1601;
    field.SetPixels(pixels);field.Apply();
    Check(report,Difference(altitudeColor,sample())<.002,"elevation palette blends continuously between adjacent metres");
    for(int i=0;i<pixels.Length;i++)pixels[i].r=-200;
    field.SetPixels(pixels);field.Apply();double shelf=Mean(sample());
    for(int i=0;i<pixels.Length;i++)pixels[i].r=-3200;
    field.SetPixels(pixels);field.Apply();double deep=Mean(sample());
    Check(report,shelf>deep+.08,"height view distinguishes continental shelf from deep seafloor");
    material.SetFloat("_ProbeMode",4);material.SetFloat("_ProbePixels",48);
    material.SetVector("_ProbeOrigin",new Vector4(5,3,0,0));
    material.SetVector("_ProbeSpan",new Vector4(4,1,0,0));
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)pixels[y*16+x]=new Color(153+(x-5)*8,-3,0,0);
    field.SetPixels(pixels);field.Apply();var gentleContours=sample();
    double closeContours=Mean(gentleContours);
    Check(report,closeContours>.005,"detail contours reveal gentle terrain with less than 100 metres of relief; coverage="+closeContours);
    Check(report,closeContours<.011,"gentle terrain uses sparse auxiliary contours; coverage="+closeContours);
    material.SetFloat("_ProbeMode",5);
    Check(report,Mean(sample())>closeContours*.9,"auxiliary contour visibility retains gentle slopes");
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)pixels[y*16+x]=new Color(153+(x-5)*400,-3,0,0);
    field.SetPixels(pixels);field.Apply();
    Check(report,Mean(sample())<.000001,"auxiliary contours disappear from clearly defined steep terrain");
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)pixels[y*16+x]=new Color(153+(x-5)*8,-3,0,0);
    field.SetPixels(pixels);field.Apply();material.SetFloat("_ProbeMode",4);
    material.SetFloat("_ProbePixels",2);
    Check(report,Mean(sample())<closeContours*.1,"minor contours fade from distant views without shifting elevation levels");
    material.SetFloat("_ProbePixels",48);material.SetFloat("_ProbeFlip",1);
    var reversedContours=sample();maximumError=0;
    for(int y=0;y<64;y++)for(int x=0;x<128;x++)
     maximumError=Math.Max(maximumError,Math.Abs(gentleContours[y*128+x].r-reversedContours[y*128+127-x].r));
    Check(report,maximumError<.005,"contours remain attached to geographic heights when the view reverses; maximum error="+maximumError);
    material.SetFloat("_ProbeFlip",0);material.SetVector("_ProbeOrigin",new Vector4(21,3,0,0));
    Check(report,Difference(gentleContours,sample())<.002,"contours agree across wrapped longitude");
    material.SetVector("_ProbeOrigin",new Vector4(5,3,0,0));
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)pixels[y*16+x]=new Color(-153-(x-5)*8,3,153+(x-5)*8,0);
    field.SetPixels(pixels);field.Apply();
    Check(report,Math.Abs(Mean(sample())-closeContours)<.002,"underwater depth isolines use the same spacing as land contours");
    for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(150,-3,0,0);
    field.SetPixels(pixels);field.Apply();
    Check(report,Mean(sample())<.000001,"level terrain remains clear even at an exact contour elevation");
    var densityBoundary=new List<Color[]>();
    foreach(float slope in new[]{66.665f,66.668f})
    {
     for(int y=0;y<8;y++)for(int x=0;x<16;x++)pixels[y*16+x]=new Color(153+(x-5)*slope,-3,0,0);
     field.SetPixels(pixels);field.Apply();densityBoundary.Add(sample());
    }
    Check(report,Difference(densityBoundary[0],densityBoundary[1])<.01,"contour density blends continuously across screen-space spacing thresholds");
    CheckNaturalPalette(report);
    CheckLandLighting(report,material,field,pixels,sample);
    if(report.Exists(line=>line.StartsWith("FAIL")))throw new Exception(string.Join("\n",report));
   }
   finally
   {
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");
    File.WriteAllLines("Documentation/LocalPlanetUpgrade/terrain-quality-regression.txt",report);
    RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(field);UnityEngine.Object.DestroyImmediate(image);
   }
  }

  static void CheckNaturalPalette(List<string> report)
  {
   var world=new WorldData(64,32,"natural land palette",TerrainPreset.Continents);
   world.Facts=new FlatTerrainFacts(world.Count);
   Color32 Land(Biome biome,VegetationCover cover,GroundMaterial material,float wetness)
   {
    world.Biomes[0]=biome;world.Facts.Vegetation[0]=cover;world.Facts.Materials[0]=material;world.Facts.Wetness[0]=wetness;
    return FlatPalette.Terrain(world,0);
   }
   var sand=Land(Biome.Desert,VegetationCover.Bare,GroundMaterial.Sand,.12f);
   var steppe=Land(Biome.Steppe,VegetationCover.DrySteppe,GroundMaterial.Soil,.21f);
   var grass=Land(Biome.Grassland,VegetationCover.Grassland,GroundMaterial.Soil,.64f);
   Check(report,sand.r-sand.g>=17&&steppe.g>=steppe.r&&grass.g-grass.r>=14,
         "sand, dry steppe and moist grassland have distinct natural hues: "+sand+", "+steppe+", "+grass);
   var soil=Land(Biome.Grassland,VegetationCover.Bare,GroundMaterial.Soil,.12f);
   var damp=Land(Biome.Grassland,VegetationCover.Bare,GroundMaterial.Soil,.8f);
   var silt=Land(Biome.Grassland,VegetationCover.Bare,GroundMaterial.Silt,.85f);
   var rock=Land(Biome.Grassland,VegetationCover.Bare,GroundMaterial.Rock,.25f);
   var marsh=Land(Biome.Wetland,VegetationCover.Marsh,GroundMaterial.Silt,.86f);
   Check(report,soil.r>=damp.r+10&&soil.g>=damp.g+5,"soil darkens with its measured wetness");
   Check(report,Math.Abs(rock.r-rock.b)<15&&soil.r>rock.r+8&&silt.r-silt.b>12&&marsh.g>marsh.r+12&&marsh.b>silt.b+8,
         "bare stone, silt, soil and marsh carry different reflectance at the same elevation");
   var snow=Land(Biome.Snow,VegetationCover.Ice,GroundMaterial.Snow,.3f);
   Check(report,snow.r==221&&snow.g==228&&snow.b==220,"permanent snow retains its source palette");
  }

  static void CheckLandLighting(List<string> report,Material material,Texture2D field,Color[] pixels,Func<Color[]> sample)
  {
   material.SetFloat("_ProbeMode",7);material.SetFloat("_ProbePixels",48);
   material.SetVector("_ProbeOrigin",new Vector4(7.49f,3.49f,0,0));
   material.SetVector("_ProbeSpan",new Vector4(.02f,.02f,0,0));
   material.SetVector("_ProbeView",new Vector4(0,0,1,0));
   material.SetVector("_ProbeGround",new Vector4(0,.20f,.1f,1));
   var east=new Vector4(.8660254f,0,.5f,0);
   var west=new Vector4(-east.x,0,east.z,0);
   void Plane(float slope)
   {
    for(int y=0;y<8;y++)for(int x=0;x<16;x++)pixels[y*16+x]=new Color(1200+(x-7)*slope,-3,0,.1f);
    field.SetPixels(pixels);field.Apply();
   }
   double FaceDifference(Vector4 sun,Vector4 moon)
   {
    material.SetVector("_ProbeSun",sun);material.SetVector("_ProbeMoon",moon);
    Plane(120);double risingEast=Mean(sample());
    Plane(-120);return Mean(sample())-risingEast;
   }
   double eastSun=FaceDifference(east,Vector4.zero),westSun=FaceDifference(west,Vector4.zero);
   Check(report,eastSun>.15&&westSun<-.15,"opposite slopes exchange sunlight as solar bearing reverses; "+eastSun+", "+westSun);
   var night=new Vector4(0,0,-1,0);var moon=east;moon.w=.12f;
   double full=FaceDifference(night,moon);moon.w=0;double dark=FaceDifference(night,moon);
   Check(report,full>.035&&Math.Abs(dark)<.012,"moon phase controls directional terrain light; "+full+", "+dark);
   material.SetVector("_ProbeGround",new Vector4(0,.20f,.1f,0));
   Check(report,Math.Abs(FaceDifference(east,Vector4.zero)-FaceDifference(west,Vector4.zero))<.002,
         "cartographic relief remains stable with the day-night cycle switched off");
   Plane(0);
   material.SetVector("_ProbeGround",new Vector4(2,.92f,.05f,1));
   material.SetVector("_ProbeSun",new Vector4(.5f,0,.8660254f,0));
   material.SetVector("_ProbeView",new Vector4(-.5f,0,.8660254f,0));
   float wetSheen=sample()[0].a;
   material.SetVector("_ProbeView",east);float offAxis=sample()[0].a;
   material.SetVector("_ProbeGround",new Vector4(2,.12f,.05f,1));
   Check(report,wetSheen>.015f&&wetSheen<=.0381f&&offAxis<wetSheen*.2f&&sample()[0].a==0,
         "wet silt has a bounded reflection that fades off axis and on dry ground; "+wetSheen+", "+offAxis);
   material.SetFloat("_ProbeMode",9);material.SetVector("_ProbeSpan",new Vector4(2,1,0,0));
   material.SetVector("_ProbeGround",new Vector4(1,.12f,.10f,1));var sand=sample();
   material.SetVector("_ProbeGround",new Vector4(0,.12f,.10f,1));var earth=sample();
   Check(report,Difference(sand,earth)>.002,"fine sand carries different microstructure from bare soil at close range");
   material.SetFloat("_ProbePixels",2);earth=sample();
   material.SetVector("_ProbeGround",new Vector4(1,.12f,.10f,1));
   Check(report,Difference(earth,sample())<.00001,"subpixel sand ripples filter out at map distance");
   // Adjacent sand and rock have very different IDs. Neither side should
   // paint an entire square or create the intervening gravel category.
   for(int y=0;y<8;y++)for(int x=0;x<16;x++)
    pixels[y*16+x]=new Color(400,x<8?1:4,x<8?.2f:.8f,0);
   field.SetPixels(pixels);field.Apply();
   material.SetTexture("_ProbeGeo",field);material.SetFloat("_ProbeMode",10);material.SetFloat("_ProbePixels",48);
   material.SetVector("_ProbeSpan",new Vector4(.00001f,.00001f,0,0));
   material.SetVector("_ProbeOrigin",new Vector4(7.9998f,3.49f,0,0));var westEdge=sample();
   material.SetVector("_ProbeOrigin",new Vector4(8.0002f,3.49f,0,0));var eastEdge=sample();
   Check(report,Difference(westEdge,eastEdge)<.002&&Mathf.Abs(westEdge[0].r-.5f)<.001f&&westEdge[0].g==0
         &&Mathf.Abs(eastEdge[0].a-.5f)<.001f,
         "sand grain, wetness and retention meet continuously across a sand-rock cell boundary without false gravel");
  }

  static void Check(List<string> report,bool pass,string message){report.Add((pass?"PASS ":"FAIL ")+message);}
  static double Difference(Color[] first,Color[] second)
  {
   double sum=0;
   for(int i=0;i<first.Length;i++)sum+=Math.Abs(first[i].r-second[i].r)+Math.Abs(first[i].g-second[i].g)+Math.Abs(first[i].b-second[i].b);
   return sum/first.Length;
  }
  static double Contrast(Color[] pixels)
  {
   double sum=0,squares=0;
   foreach(var pixel in pixels){double value=pixel.r*.2126+pixel.g*.7152+pixel.b*.0722;sum+=value;squares+=value*value;}
   return Math.Sqrt(Math.Max(0,squares/pixels.Length-sum*sum/(pixels.Length*pixels.Length)));
  }
  static double Mean(Color[] pixels)
  {
   double sum=0;
   foreach(var pixel in pixels)sum+=pixel.r;
   return sum/pixels.Length;
  }
 }
}
