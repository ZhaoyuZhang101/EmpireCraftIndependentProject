using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
 public static class VegetationArtRegression
 {
  const int Resolution=128;

  [MenuItem("Tools/ORBIS/Local Planet/Validate overhead pixel vegetation")]
  public static void Run()
  {
   var lines=new List<string>();
   var previousTarget=RenderTexture.active;
   bool previousAsync=ShaderUtil.allowAsyncCompilation;
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/VegetationArtProbe.shader");
   var material=new Material(shader);
   var field=new Texture2D(16,8,TextureFormat.RGBAFloat,false,true);
   var info=new Texture2D(16,8,TextureFormat.RGBAFloat,false,true);
   var image=new Texture2D(Resolution,Resolution,TextureFormat.RGBAFloat,false,true);
   var target=RenderTexture.GetTemporary(Resolution,Resolution,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   field.filterMode=info.filterMode=FilterMode.Point;
   field.wrapMode=info.wrapMode=TextureWrapMode.Repeat;
   try
   {
    ShaderUtil.allowAsyncCompilation=false;
    ShaderUtil.CompilePass(material,0,true);
    foreach(var message in ShaderUtil.GetShaderMessages(shader))
     if(message.severity.ToString()=="Error")throw new Exception(message.message);
    var fields=new Color[128];
    for(int i=0;i<fields.Length;i++)fields[i]=new Color(10,-3,0,1);
    field.SetPixels(fields);field.Apply();
    material.SetTexture("_VisualTex",field);
    material.SetTexture("_InfoTex",info);
    material.SetTexture("_HydroA",Texture2D.blackTexture);
    material.SetTexture("_HydroB",Texture2D.blackTexture);
    material.SetVector("_MapSize",new Vector4(16,8,1f/16,1f/8));
    foreach(var root in new[]{new Vector2(7,3),new Vector2(7,7),new Vector2(7,0)})
    {
     float lon=((root.x+.5f)/16-.5f)*Mathf.PI*2;
     float lat=((root.y+.5f)/8-.5f)*Mathf.PI;
     var radial=new Vector3(Mathf.Sin(lon)*Mathf.Cos(lat),Mathf.Sin(lat),Mathf.Cos(lon)*Mathf.Cos(lat));
     var east=new Vector3(Mathf.Cos(lon),0,-Mathf.Sin(lon));
     material.SetVector("_ProbeRoot",root);
     for(int kind=0;kind<2;kind++)
     {
      var semantics=new Color[128];
      for(int i=0;i<semantics.Length;i++)semantics[i]=new Color(0,0,2f/255,0);
      semantics[(int)root.x+(int)root.y*16]=new Color(0,0,0,kind==0?0:20f/255);
      info.SetPixels(semantics);info.Apply();
      material.SetVector("_FlatSun",(radial*.7f-east*.7f).normalized);
      Graphics.Blit(Texture2D.whiteTexture,target,material);
      RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Resolution,Resolution),0,0);image.Apply();
      var first=image.GetPixels();
      int minX=Resolution,maxX=0,minY=Resolution,maxY=0,foliage=0,trunk=0;
      foreach(var color in first)
      {
       if(color.a<.5f)continue;
       if(color.r>color.g*1.15f)trunk++;
       else foliage++;
      }
      for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++)
      {
       Color color=first[y*Resolution+x];
       if(color.a<.5f||color.g<color.r*1.03f)continue;
       minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
      }
      float aspect=(maxX-minX+1f)/(maxY-minY+1f);
      float trunkFraction=trunk/(float)Math.Max(1,foliage+trunk);
      int distinctRows=0;
      for(int y=1;y<Resolution;y++)
      {
       float change=0;
       for(int x=0;x<Resolution;x++)
       {
        Color current=first[y*Resolution+x],previous=first[(y-1)*Resolution+x];
        change+=Mathf.Abs(current.g-previous.g);
       }
       if(change>.001f)distinctRows++;
      }
      string label="row="+root.y+" kind="+kind;
      Check(lines,distinctRows>=10,label+" retains vertical pixel detail; distinct scanlines="+distinctRows);
      Check(lines,foliage>600,label+" crown remains legible with "+foliage+" pixels");
      Check(lines,aspect>.9f&&aspect<1.65f,label+" overhead crown aspect="+aspect);
      Check(lines,trunkFraction<.055f,label+" short exposed trunk fraction="+trunkFraction);
      material.SetVector("_FlatSun",(radial*.7f+east*.7f).normalized);
      Graphics.Blit(Texture2D.whiteTexture,target,material);
      RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Resolution,Resolution),0,0);image.Apply();
      var second=image.GetPixels();
      double response=0;
      for(int i=0;i<first.Length;i++)response+=Math.Abs(first[i].g-second[i].g);
      Check(lines,response/Math.Max(1,foliage)>.025,label+" crown follows sunlight; mean green response="+response/Math.Max(1,foliage));
      material.SetVector("_ProbeRoot",root+new Vector2(16,0));
      Graphics.Blit(Texture2D.whiteTexture,target,material);
      RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Resolution,Resolution),0,0);image.Apply();
      var wrapped=image.GetPixels();
      int changed=0;
      for(int i=0;i<second.Length;i++)if(Mathf.Abs(second[i].a-wrapped[i].a)>.5f)changed++;
      Check(lines,changed<=8,label+" longitude wrap preserves the tree silhouette; changed pixels="+changed);
      material.SetVector("_ProbeRoot",root);
     }
    }
    for(int i=0;i<fields.Length;i++)fields[i]=new Color(10,1,0,1);
    field.SetPixels(fields);field.Apply();
    for(int mode=0;mode<2;mode++)
    {
     material.SetFloat("_ProbeMode",mode);
     Graphics.Blit(Texture2D.whiteTexture,target,material);
     RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Resolution,Resolution),0,0);image.Apply();
     float maximum=0;
     foreach(var color in image.GetPixels())maximum=Mathf.Max(maximum,color.r,color.g,color.b);
     Check(lines,maximum<.00001f,"water clearance removes "+(mode==0?"tree artwork":"the matching tree shadow")+"; maximum="+maximum);
    }
    if(lines.Exists(line=>line.StartsWith("FAIL")))throw new Exception(string.Join("\n",lines));
   }
   finally
   {
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");
    File.WriteAllLines("Documentation/LocalPlanetUpgrade/vegetation-art-regression.txt",lines);
    RenderTexture.active=previousTarget;ShaderUtil.allowAsyncCompilation=previousAsync;
    RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(field);
    UnityEngine.Object.DestroyImmediate(info);UnityEngine.Object.DestroyImmediate(image);
   }
  }

  // Geographic longitude, latitude, signed marine distance in km, and live tree eligibility.
  public static Color[] ReadGeographicTreeRoots(Material surface,RectInt cells)
  {
   var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/VegetationArtProbe.shader"));
   var target=RenderTexture.GetTemporary(cells.width,cells.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var pixels=new Texture2D(cells.width,cells.height,TextureFormat.RGBAFloat,false,true);
   var previous=RenderTexture.active;
   try
   {
    probe.CopyPropertiesFromMaterial(surface);
    probe.SetFloat("_ProbeMode",11);probe.SetVector("_ProbeRoot",new Vector4(cells.x,cells.y,0,0));
    probe.SetVector("_ProbeGrid",new Vector4(cells.width,cells.height,0,0));
    ShaderUtil.CompilePass(probe,0,true);
    Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
    pixels.ReadPixels(new Rect(0,0,cells.width,cells.height),0,0);
    return pixels.GetPixels();
   }
   finally
   {
    RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(probe);
   }
  }

  internal static void CheckSurveyedTreeRoots(Material earth)
  {
   if(earth.GetFloat("_EarthStraitReady")<.5f)throw new InvalidOperationException("Expected the surveyed Earth strait atlas.");
   const int columns=20,rows=16;
   var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/VegetationArtProbe.shader"));
   var field=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
   var hydro=new Texture2D(1,1,TextureFormat.RGBA32,false,true);
   var target=RenderTexture.GetTemporary(columns,rows,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var pixels=new Texture2D(columns,rows,TextureFormat.RGBAFloat,false,true);
   var previous=RenderTexture.active;
   Color[] Sample()
   {
    Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
    pixels.ReadPixels(new Rect(0,0,columns,rows),0,0);
    return pixels.GetPixels();
   }
   try
   {
    probe.CopyPropertiesFromMaterial(earth);
    if (!(earth.GetTexture("_EarthTreeBanks") is RenderTexture treeBanks) || !treeBanks.IsCreated())
     throw new InvalidOperationException("The published Earth tree bank texture is unavailable.");
    var size=earth.GetVector("_MapSize");
    probe.SetFloat("_ProbeMode",13);probe.SetVector("_ProbeGrid",new Vector4(columns,rows,0,0));
    ShaderUtil.CompilePass(probe,0,true);
    int cachedWet=0,cachedDry=0,model=0;
    foreach(var site in new[]{new Vector2(22.5f,36.5f),new Vector2(121.36522f,36.60291f),
     new Vector2(29.1f,41.1f),new Vector2(120.1f,31.2f),new Vector2(-87,45.8f),
     new Vector2(172.8f,-43.7f),new Vector2(-180,-16.6f),new Vector2(180,-16.6f)})
    {
     int left=Mathf.FloorToInt((site.x/360+.5f)*size.x)-columns/2;
     int bottom=Mathf.FloorToInt((site.y/180+.5f)*size.y)-rows/2;
     probe.SetVector("_ProbeRoot",new Vector4(left,bottom,0,0));
     var banks=Sample();
     for(int i=0;i<banks.Length;i++)
     {
      var b=banks[i];
      if((b.r>=0)!=(b.b>=0)||Mathf.Abs(b.r-b.b)>.0011f*Mathf.Max(1,Mathf.Abs(b.r))||Mathf.Abs(b.g-b.a)>.00051f)
       throw new InvalidOperationException($"Tree bank differs at tile ({left+i%columns},{bottom+i/columns}): direct ({b.r:R},{b.g:R}), cached ({b.b:R},{b.a:R}).");
      if(b.g<=.001f)model++;else if(b.r>=0)cachedWet++;else cachedDry++;
     }
    }
    if(cachedWet==0||cachedDry==0||model==0)throw new InvalidOperationException("Tree bank controls lack surveyed wet, dry or model roots.");
    Debug.Log($"Tree bank cache PASS: {cachedWet} wet and {cachedDry} dry surveyed roots and {model} model roots match their live geographic samples.");
    // Every source tile would support a tree without the regional shoreline.
    // The real tree anchors may fall on the other side of the bank from their tile centres.
    field.SetPixel(0,0,new Color(10,-3,0,1));field.Apply();
    hydro.SetPixel(0,0,Color.clear);hydro.Apply();
    probe.SetTexture("_VisualTex",field);probe.SetTexture("_HydroA",hydro);probe.SetTexture("_HydroB",hydro);
    probe.SetFloat("_EarthLakeReady",0); // Isolate marine coverage for the Bosporus controls.
    probe.SetFloat("_RiverAtlasReady",0);probe.SetFloat("_RiverPreviewReady",0);probe.SetFloat("_RiverSeasonReady",0);
    probe.SetVector("_MapSize",new Vector4(4096,2048,1f/4096,1f/2048));
    probe.SetFloat("_ProbeMode",10);probe.SetVector("_ProbeRoot",new Vector4(2371,1481,0,0));
    probe.SetVector("_ProbeGrid",new Vector4(columns,rows,0,0));
    var surveyed=Sample();
    probe.SetTexture("_EarthSurveyMask",hydro);
    var edited=Sample();
    probe.SetTexture("_EarthSurveyMask",earth.GetTexture("_EarthSurveyMask"));probe.SetFloat("_EarthStraitReady",0);
    var legacy=Sample();
    int wet=0,dry=0,split=0,smaller=0,inland=0;
    for(int i=0;i<surveyed.Length;i++)
    {
     Color s=surveyed[i];
     bool expected=Mathf.Lerp(-3,s.g,s.b)<0;
     float originalSize=legacy[i].r;
     if((s.r>0)!=expected || originalSize<=0 || s.r>originalSize+.00001f ||
        edited[i].r!=originalSize || edited[i].b!=0 || legacy[i].b!=0)
      throw new InvalidOperationException($"Surveyed tree root {i} differs: survey={s}, edit={edited[i]}, old save={legacy[i]}");
     if(s.b<.99999f)continue;
     if(expected)
     {
      dry++;
      if(s.r<originalSize-.0001f)smaller++;else inland++;
     }
     else wet++;
     if((s.g<0)!=(s.a<0))split++;
    }
    if(wet==0||dry==0||split==0||smaller==0||inland==0)
     throw new InvalidOperationException($"Missing tree-root controls: sea={wet}, land={dry}, opposite centres={split}, reduced coast={smaller}, unchanged inland={inland}");
    Debug.Log($"Surveyed tree roots PASS: {surveyed.Length} Bosporus / Izmit candidates, {wet} sea roots removed, {dry} dry roots retained, {split} roots across shore from tile centres, {smaller} coastal symbols reduced, {inland} inland sizes unchanged; edits and old saves retain all original sizes.");
    // Natural Earth Taihu shore, both sides of the 31.2890625 N page boundary,
    // the east bank and open water. All control sites would grow a tree on the grid.
    probe.SetFloat("_EarthLakeReady",earth.GetFloat("_EarthLakeReady"));
    probe.SetFloat("_ProbeMode",12);
    foreach(var site in new[] { new Vector3(120.025f,31.285f,1), new Vector3(120.025f,31.294f,1),
     new Vector3(120.028f,31.204f,1),new Vector3(120.16f,31.18f,1),new Vector3(120.636f,31.22f,0),
     new Vector3(119.8f,31.16f,0) })
    {
     probe.SetVector("_ProbeRoot",new Vector4(site.x/360+.5f,site.y/180+.5f,0,0));
     var actual=Sample()[0];bool isWater=site.z>0;
     if((actual.r>0)!=isWater || (actual.g>.5f)!=isWater || (actual.b>0)==isWater)
      throw new InvalidOperationException($"Lake shore / tree root at {site.x:F4}, {site.y:F4}: shore={actual.r}, lake={actual.g}, tree size={actual.b}");
    }
    probe.SetTexture("_EarthSurveyMask",hydro);
    probe.SetVector("_ProbeRoot",new Vector4(120.028f/360+.5f,31.204f/180+.5f,0,0));
    var edit=Sample()[0];
    if(edit.r!=-3 || edit.g!=0 || edit.b<=0)throw new InvalidOperationException("Lake survey covered a player-edited dry tree root.");
   }
   finally
   {
    RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(field);
    UnityEngine.Object.DestroyImmediate(hydro);UnityEngine.Object.DestroyImmediate(probe);
   }
  }

  static void Check(List<string> lines,bool pass,string message)
  {lines.Add((pass?"PASS ":"FAIL ")+message);}
 }
}
