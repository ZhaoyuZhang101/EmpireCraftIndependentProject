using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
 public static class AmbientLifeRegression
 {
  const int Width=512,Height=256,MapWidth=96,MapHeight=48;

  [MenuItem("Tools/ORBIS/Local Planet/Validate scenic wildlife")]
  public static void Run()
  {
   var report=new List<string>();
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/AmbientLifeProbe.shader");
   if(shader==null)throw new Exception("The scenic wildlife probe shader is missing.");
   var material=new Material(shader);
   var field=MakeTexture(MapWidth,MapHeight);
   var info=MakeTexture(MapWidth,MapHeight);
   var ecology=MakeTexture(MapWidth,MapHeight);
   var atlas=MakeTexture(MapWidth,54);
   var image=MakeTexture(Width,Height);
   var target=RenderTexture.GetTemporary(Width,Height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var previousTarget=RenderTexture.active;
   bool previousAsync=ShaderUtil.allowAsyncCompilation;
   try
   {
    ShaderUtil.allowAsyncCompilation=false;
    ShaderUtil.CompilePass(material,0,true);
    foreach(var message in ShaderUtil.GetShaderMessages(shader))
     if(message.severity.ToString()=="Error")throw new Exception(message.message);

    Fill(field,new Color(40,-3,0,.55f));
    Fill(info,Color.clear);
    Fill(ecology,Ecology(2));
    var atlasPixels=new Color[MapWidth*54];
    for(int y=0;y<MapHeight;y++)for(int x=0;x<MapWidth;x++)
     atlasPixels[y*MapWidth+x]=new Color(1,0,0,1);
    for(int y=48;y<54;y++)for(int x=0;x<MapWidth;x++)
     atlasPixels[y*MapWidth+x]=new Color(-1,.4f,0,.1f);
    atlas.SetPixels(atlasPixels);atlas.Apply();

    material.SetTexture("_VisualTex",field);
    material.SetTexture("_InfoTex",info);
    material.SetTexture("_EcologyTex",ecology);
    material.SetTexture("_RiverAtlas",atlas);
    material.SetTexture("_HydroA",Texture2D.blackTexture);
    material.SetTexture("_HydroB",Texture2D.blackTexture);
    material.SetVector("_MapSize",new Vector4(MapWidth,MapHeight,1f/MapWidth,1f/MapHeight));
    material.SetVector("_RiverAtlasLayout",new Vector4(MapWidth,54,4,48));
    material.SetFloat("_EcologyReady",1);
    material.SetFloat("_RiverAtlasReady",0);
    material.SetFloat("_DetailWeight",1);
    material.SetFloat("_ProbeSeed",.1f);
    material.SetFloat("_ProbeVariant",.2f);
    material.SetFloat("_WaterClock",2.375f);
    var view=new Vector4(18,15,36,18);
    material.SetVector("_ProbeCellRect",view);
    Vector3 sun=Radial(new Vector2(36,24));
    material.SetVector("_FlatSun",sun);
    Func<Color[]> sample=()=>Sample(material,target,image);

    var baseline=sample();
    int coverage=CountMask(baseline);
    Check(report,coverage>4&&coverage<baseline.Length*.015f,
     "ground wildlife occupies a small fraction of a natural landscape; pixels="+coverage+" / "+baseline.Length);
    Check(report,Difference(baseline,sample())<.000001,
     "an unchanged pause-controlled clock produces identical wildlife pixels");
    material.SetFloat("_WaterClock",16.25f);
    Check(report,ChangedPixels(baseline,sample())>4,
     "the live clock visibly changes the bounded ground movement");
    material.SetFloat("_WaterClock",2.375f);
    material.SetVector("_ProbeCellRect",new Vector4(view.x+MapWidth,view.y,view.z,view.w));
    int wrappedChanges=ChangedPixels(baseline,sample());
    Check(report,wrappedChanges<=4,
     "the same geographic pixels retain their species and pose across longitude wrap; changed pixels="+wrappedChanges);
    material.SetVector("_ProbeCellRect",view);

    material.SetFloat("_ProbeMode",2);
    var homes=sample();
    material.SetVector("_ProbeCellRect",new Vector4(view.x-MapWidth,view.y,view.z,view.w));
    Check(report,Difference(homes,sample())<.0001,
     "wrapped coordinates select the same stable home patch and movement phase");
    material.SetVector("_ProbeCellRect",view);

    int reference=Math.Max(0,Array.FindIndex(baseline,pixel=>pixel.a>.5f));
    Color residentHome=homes[reference];
    int neighbourX=Mathf.FloorToInt(residentHome.r)+(Mathf.Repeat(residentHome.r,1)>.5f?1:-1);
    neighbourX=(neighbourX+MapWidth)%MapWidth;
    int neighbourY=Mathf.Clamp(Mathf.FloorToInt(residentHome.g),0,MapHeight-1);
    material.SetFloat("_ProbeMode",5);
    Check(report,sample()[reference].r>.5f,"an unobstructed home passes the full movement-footprint check");
    info.SetPixel(neighbourX,neighbourY,new Color(0,0,1f/255,0));info.Apply();
    Check(report,sample()[reference].r<.5f,"a building in an adjacent touched cell rejects the complete home path");
    info.SetPixel(neighbourX,neighbourY,new Color(0,5f/255,0,0));info.Apply();
    Check(report,sample()[reference].r<.5f,"a river in an adjacent touched cell rejects the complete ground path");
    info.SetPixel(neighbourX,neighbourY,Color.clear);info.Apply();

    material.SetFloat("_ProbeMode",3);
    var habitats=sample();
    Check(report,SpeciesAt(habitats,0)==0&&SpeciesAt(habitats,13)==0,
     "bare ground and permanent ice have no scenic wildlife");
    Check(report,SpeciesAt(habitats,2)==1&&SpeciesAt(habitats,8)==2&&
     SpeciesAt(habitats,9)==4&&SpeciesAt(habitats,10)==3,
     "steppe rabbits, boreal deer, tropical butterflies and marsh birds use distinct habitats");

    material.SetFloat("_ProbeMode",1);
    for(int species=1;species<=4;species++)
    {
     material.SetFloat("_ProbeSpecies",species);
     material.SetFloat("_WaterClock",.45f);
     var first=sample();
     material.SetFloat("_WaterClock",1.2f);
     var second=sample();
     int mask=CountMask(first);
     Check(report,mask>300&&mask<first.Length*.25f,
      "species="+species+" retains a small legible pixel silhouette; pixels="+mask);
     Check(report,ChangedPixels(first,second)>64,
      "species="+species+" has visible idle or wing animation");
     Check(report,InvalidPixels(first)+InvalidPixels(second)==0,
      "species="+species+" artwork remains finite");
    }

    material.SetFloat("_ProbeMode",0);
    material.SetFloat("_WaterClock",2.375f);
    Fill(ecology,Ecology(13));
    Check(report,CountMask(sample())==0,"permanent ice rejects wildlife in the production compositing function");
    Fill(ecology,Ecology(2));
    Fill(info,new Color(0,0,1f/255,0));
    Check(report,CountMask(sample())==0,"buildings reject the entire wildlife home footprint");
    Fill(info,Color.clear);
    Fill(field,new Color(40,1,0,.55f));
    Check(report,CountMask(sample())==0,"the actual reconstructed water field excludes ground wildlife");
    Fill(field,new Color(40,-3,0,.55f));
    material.SetFloat("_RiverAtlasReady",1);
    Check(report,CountMask(sample())==0,"the actual river atlas excludes ground wildlife from wet home paths");

    Fill(ecology,Ecology(9));
    int flying=CountMask(sample());
    Check(report,flying>4,"small flying creatures can cross river water from a suitable habitat; pixels="+flying);
    material.SetVector("_FlatSun",-sun);
    Check(report,CountMask(sample())==0,"day-active birds and butterflies disappear at night");
    Fill(ecology,Ecology(2));
    material.SetFloat("_RiverAtlasReady",0);
    Check(report,CountMask(sample())>4,"ground rabbits retain their crepuscular and night activity");
    material.SetVector("_FlatSun",sun);

    material.SetFloat("_EcologyReady",0);
    Fill(info,new Color(0,0,0,4f/255));
    Check(report,CountMask(sample())==0,"legacy desert climate data also excludes woodland wildlife");
    material.SetFloat("_EcologyReady",1);
    Fill(info,Color.clear);

    material.SetFloat("_ProbeMode",2);
    material.SetVector("_MapSize",new Vector4(97,49,1f/97,1f/49));
    material.SetVector("_ProbeCellRect",new Vector4(94,18,6,12));
    var oddSizedHomes=sample();
    material.SetVector("_ProbeCellRect",new Vector4(-3,18,6,12));
    Check(report,Difference(oddSizedHomes,sample())<.0001,
     "an odd-sized map closes home patches across the date line with the same pose");
    material.SetVector("_MapSize",new Vector4(MapWidth,MapHeight,1f/MapWidth,1f/MapHeight));
    material.SetFloat("_ProbeMode",0);

    int invalid=0;
    foreach(var polarView in new[]{new Vector4(-3,-3,6,6),new Vector4(93,45,6,6),new Vector4(-99,0,6,6)})
    {
     material.SetVector("_ProbeCellRect",polarView);
     invalid+=InvalidPixels(sample());
    }
    Check(report,invalid==0,"wildlife coordinates remain finite at both poles and across the date line; invalid="+invalid);
    if(report.Exists(line=>line.StartsWith("FAIL")))throw new Exception(string.Join("\n",report));
   }
   finally
   {
    Directory.CreateDirectory("ValidationOutput/Ecology");
    File.WriteAllLines("ValidationOutput/Ecology/ambient-life-regression.txt",report);
    RenderTexture.active=previousTarget;
    ShaderUtil.allowAsyncCompilation=previousAsync;
    RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(material);
    UnityEngine.Object.DestroyImmediate(field);
    UnityEngine.Object.DestroyImmediate(info);
    UnityEngine.Object.DestroyImmediate(ecology);
    UnityEngine.Object.DestroyImmediate(atlas);
    UnityEngine.Object.DestroyImmediate(image);
   }
  }

  static Texture2D MakeTexture(int width,int height)
  {
   return new Texture2D(width,height,TextureFormat.RGBAFloat,false,true)
    {filterMode=FilterMode.Point,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};
  }

  static void Fill(Texture2D texture,Color color)
  {
   var pixels=new Color[texture.width*texture.height];
   for(int i=0;i<pixels.Length;i++)pixels[i]=color;
   texture.SetPixels(pixels);texture.Apply();
  }

  static Color Ecology(int cover){return new Color(cover/255f,3f/255,.5f,.2f);}

  static Color[] Sample(Material material,RenderTexture target,Texture2D image)
  {
   Graphics.Blit(Texture2D.whiteTexture,target,material);
   RenderTexture.active=target;
   image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();
   return image.GetPixels();
  }

  static Vector3 Radial(Vector2 cell)
  {
   float longitude=(cell.x/MapWidth-.5f)*Mathf.PI*2;
   float latitude=(cell.y/MapHeight-.5f)*Mathf.PI;
   return new Vector3(Mathf.Sin(longitude)*Mathf.Cos(latitude),Mathf.Sin(latitude),Mathf.Cos(longitude)*Mathf.Cos(latitude));
  }

  static int SpeciesAt(Color[] pixels,int cover)
  {
   int x=Mathf.Clamp(Mathf.FloorToInt((cover+.5f)/14*Width),0,Width-1);
   return Mathf.RoundToInt(pixels[Height/2*Width+x].r);
  }

  static int CountMask(Color[] pixels)
  {
   int count=0;
   foreach(var pixel in pixels)if(pixel.a>.5f)count++;
   return count;
  }

  static int InvalidPixels(Color[] pixels)
  {
   int count=0;
   foreach(var pixel in pixels)
    if(float.IsNaN(pixel.r+pixel.g+pixel.b+pixel.a)||float.IsInfinity(pixel.r+pixel.g+pixel.b+pixel.a))count++;
   return count;
  }

  static int ChangedPixels(Color[] first,Color[] second)
  {
   int changed=0;
   for(int i=0;i<first.Length;i++)
    if(Mathf.Abs(first[i].r-second[i].r)+Mathf.Abs(first[i].g-second[i].g)+
     Mathf.Abs(first[i].b-second[i].b)+Mathf.Abs(first[i].a-second[i].a)>.001f)changed++;
   return changed;
  }

  static double Difference(Color[] first,Color[] second)
  {
   double maximum=0;
   for(int i=0;i<first.Length;i++)
    maximum=Math.Max(maximum,Math.Abs(first[i].r-second[i].r)+Math.Abs(first[i].g-second[i].g)+
     Math.Abs(first[i].b-second[i].b)+Math.Abs(first[i].a-second[i].a));
   return maximum;
  }

  static void Check(List<string> report,bool pass,string message)
  {report.Add((pass?"PASS ":"FAIL ")+message);}
 }
}
