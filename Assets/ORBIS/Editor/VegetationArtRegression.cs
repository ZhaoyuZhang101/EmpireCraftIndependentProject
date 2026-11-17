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

  static void Check(List<string> lines,bool pass,string message)
  {lines.Add((pass?"PASS ":"FAIL ")+message);}
 }
}
