using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Orbis.Editor
{
 public static class WaterCloudRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate HP clouds and SW3 water")]
  public static void Run()
  {
   var report=new List<string>();var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/WaterCloudProbe.shader");
   var m=new Material(shader);var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   var rt=RenderTexture.GetTemporary(128,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(128,64,TextureFormat.RGBAFloat,false,true);
   try
   {
    ShaderUtil.CompilePass(m,0,true);Orbis.Planet.OrbisAssetVisuals.BindWater(m);
    m.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));m.SetFloat("_WaterClock",3);m.SetFloat("_Bathymetry",.5f);
    Func<Color[]> sample=()=>{Graphics.Blit(Texture2D.whiteTexture,rt,m,0);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,128,64),0,0);image.Apply();return image.GetPixels();};
    m.SetFloat("_ProbeKind",0);var cloud=sample();int bad=0,nonempty=0;
    foreach(var c in cloud){if(!Finite(c)||c.r<0||c.g<0||Mathf.Abs(c.b-1)>.0001f)bad++;if(c.r>.01f)nonempty++;}
    report.Add((bad==0&&nonempty>0?"PASS":"FAIL")+" HP density, phi_fwd and full light segment coverage; invalid="+bad+" cloud samples="+nonempty);
    m.SetFloat("_ProbeKind",1);var samples=new List<Color[]>();
    foreach(int kind in new[]{3,4,5})
    {
     m.SetFloat("_WaterKind",kind);var pixels=sample();samples.Add(pixels);bad=0;
     foreach(var c in pixels)if(!Finite(c)||c.r<0||c.g<0||c.b<0)bad++;
     report.Add((bad==0?"PASS":"FAIL")+" SW3 water kind="+kind+" finite at depths 0.1..4000m; invalid="+bad);
    }
    double oceanLake=Difference(samples[0],samples[1]),riverLake=Difference(samples[2],samples[1]);
    report.Add((oceanLake>1e-4&&riverLake>1e-4?"PASS":"FAIL")+" distinct ocean/lake/river results; differences="+oceanLake+", "+riverLake);
    var lakeWaves=Resources.Load<Texture2D>("ORBISVendor/SW3/SmoothWaves");
    var sharpWaves=Resources.Load<Texture2D>("ORBISVendor/SW3/SharpWaves");
    report.Add((m.GetFloat("_VendorWaterReady")>.5&&lakeWaves&&sharpWaves?"PASS":"FAIL")+" SW3 lake and river ripple textures available");
    string[] waterSlots={"_VendorStreamNormal","_VendorLakeNormal","_VendorLakeSmoothNormal","_VendorOceanNormal","_VendorOceanFoam","_VendorRiverFoam"};
    foreach(string resource in new[]{"ORBIS/Surface","ORBIS/FlatWorld/FlatMap","ORBIS/FlatWorld/LocalSurface","ORBIS/FlatWorld/ScrollSurface"})
    {
     var surface=new Material(Resources.Load<Shader>(resource));Material copy=null;
     try
     {
      Orbis.Planet.OrbisAssetVisuals.BindWater(surface);copy=new Material(surface);bool valid=true;
      foreach(string slot in waterSlots)valid&=surface.HasProperty(slot)&&copy.HasProperty(slot)&&surface.GetTexture(slot)!=null&&copy.GetTexture(slot)==surface.GetTexture(slot);
      valid=valid&&surface.GetTexture("_VendorLakeSmoothNormal")==lakeWaves&&surface.GetTexture("_VendorLakeNormal")==sharpWaves;
      if(resource=="ORBIS/Surface")valid&=surface.HasProperty("_Natural");
      report.Add((valid?"PASS":"FAIL")+" declared SW3 texture properties survive material cloning on "+resource);
     }
     finally{if(copy)UnityEngine.Object.DestroyImmediate(copy);UnityEngine.Object.DestroyImmediate(surface);}
    }
    m.SetFloat("_ProbeKind",3);m.SetFloat("_WaterKind",3);m.SetFloat("_ProbeFlip",0);var forward=sample();
    m.SetFloat("_ProbeFlip",1);var mirrored=sample();double rotationError=0;
    for(int y=0;y<64;y++)for(int x=0;x<128;x++)
    {
     var a=forward[y*128+x];var b=mirrored[y*128+127-x];
     rotationError=Math.Max(rotationError,Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b))));
    }
    report.Add((rotationError<.001?"PASS":"FAIL")+" geographic water normals remain stable under reversed screen direction; max error="+rotationError);
    m.SetFloat("_ProbeFlip",0);
    foreach(int kind in new[]{3,4,5})
    {
     float period=kind==4?1/.065f:5;
     m.SetFloat("_WaterKind",kind);m.SetFloat("_WaterClock",period-.0001f);var before=sample();
     m.SetFloat("_WaterClock",period+.0001f);var after=sample();
     double boundary=Difference(before,after);
     report.Add((boundary<.003?"PASS":"FAIL")+" continuous two-phase wave advection kind="+kind+" mean RGB difference="+boundary);
    }
    m.SetFloat("_WaterClock",3);
    var substrate=new Texture2D(16,8,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
    try
    {
     var materials=new Color[128];for(int i=0;i<materials.Length;i++)materials[i]=new Color(0,1,0,0);
     substrate.SetPixels(materials);substrate.Apply();m.SetTexture("_GeoTex",substrate);
     m.SetFloat("_FieldReady",1);m.SetFloat("_Bathymetry",0);m.SetFloat("_ProbeKind",4);m.SetFloat("_WaterKind",4);
     m.SetFloat("_ProbeDepth",.7f);m.SetFloat("_ProbeSediment",0);m.SetFloat("_ProbeWaves",1);
     m.SetVector("_ProbeView",new Vector4(0,0,1,0));m.SetVector("_ProbeCell",new Vector4(200,130,0,0));
     m.SetVector("_ProbeSun",new Vector4(-.8f,.5f,.3f,0));
     var clearBed=sample();double shallowContrast=Contrast(clearBed);
     report.Add((shallowContrast>.02?"PASS":"FAIL")+" shallow clear water resolves bed artwork; contrast="+shallowContrast);
     m.SetFloat("_ProbeDepth",3);var refractedBed=sample();
     m.SetFloat("_WaterClock",10);var movingBed=sample();double movement=Difference(refractedBed,movingBed);
     report.Add((movement>.00005?"PASS":"FAIL")+" calm SW3 waves animate the refracted bed over seven seconds; mean change="+movement);
     m.SetFloat("_ProbeDepth",40);double deepContrast=Contrast(sample());
     report.Add((deepContrast<shallowContrast*.2?"PASS":"FAIL")+" optical depth hides the bed; contrast="+deepContrast);
     foreach(int kind in new[]{3,4,5})
     foreach(float bathymetry in new[]{0f,1f})
     foreach(float depth in new[]{.7f,12f})
     {
      m.SetFloat("_WaterKind",kind);
      m.SetFloat("_Bathymetry",bathymetry);
      m.SetFloat("_ProbeDepth",depth);
      m.SetFloat("_ProbeSediment",0);
      var clearWater=sample();
      double tintError=0;
      foreach(float sediment in new[]{.8f,1f})
      {
       m.SetFloat("_ProbeSediment",sediment);
       tintError=Math.Max(tintError,MaximumDifference(clearWater,sample()));
      }
      report.Add((tintError<.00001?"PASS":"FAIL")+" clear water colour is independent of sediment metadata; kind="+kind+" bathymetry="+bathymetry+" depth="+depth+" max RGB change="+tintError);
     }
     m.SetFloat("_WaterKind",4);m.SetFloat("_Bathymetry",1);m.SetFloat("_ProbeSediment",0);
     m.SetFloat("_ProbeDepth",80);var lakeShelf=sample();
     m.SetFloat("_ProbeDepth",320);var lakeBasin=sample();
     double basinContrast=Difference(lakeShelf,lakeBasin);
     report.Add((basinContrast>.035&&lakeBasin[32*128+64].grayscale<lakeShelf[32*128+64].grayscale-.006f?"PASS":"FAIL")+" actual lake depths remain distinguishable in natural view; RGB contrast="+basinContrast);
     m.SetFloat("_Bathymetry",0);m.SetFloat("_ProbeDepth",.7f);
     m.SetFloat("_ProbeSediment",0);m.SetFloat("_ProbeWaves",0);m.SetFloat("_WaterClock",3);var still=sample();
     m.SetFloat("_WaterClock",4);double stillChange=Difference(still,sample());
     report.Add((stillChange<.00001?"PASS":"FAIL")+" stationary water keeps the bed stable; mean change="+stillChange);
     m.SetFloat("_ProbeDepth",3);m.SetFloat("_ProbeWaves",1);m.SetFloat("_WaterClock",3);
     m.SetVector("_ProbeCell",new Vector4(511.3f,130,0,0));var seamBed=sample();
     m.SetFloat("_ProbeFlip",1);double bedRotationError=MaximumDifference(seamBed,Mirror(sample(),128,64));
     report.Add((bedRotationError<.001?"PASS":"FAIL")+" refracted bed and geographic caustics stay stable under reversed screen direction; max error="+bedRotationError);
     m.SetFloat("_ProbeFlip",0);m.SetFloat("_ProbeWrap",1);double wrappedError=MaximumDifference(seamBed,sample());m.SetFloat("_ProbeWrap",0);
     report.Add((wrappedError<.001?"PASS":"FAIL")+" actual wrapped water coordinates preserve bed light bands at longitude zero; max error="+wrappedError);
     m.SetVector("_ProbeSun",new Vector4(-.8f,.5f,-.3f,0));var nightBed=sample();
     m.SetFloat("_ProbeFlip",1);double nightRotation=MaximumDifference(nightBed,Mirror(sample(),128,64));
     report.Add((nightRotation<.001?"PASS":"FAIL")+" bed refraction stays stable with the sun below the horizon; max error="+nightRotation);
     m.SetVector("_ProbeSun",new Vector4(-.8f,.5f,.3f,0));
     m.SetFloat("_ProbeFlip",0);m.SetVector("_ProbeCell",new Vector4(-.7f,130,0,0));double bedSeamError=Difference(seamBed,sample());
     report.Add((bedSeamError<.001?"PASS":"FAIL")+" water-bottom artwork wraps at the date line; mean error="+bedSeamError);
     float period=1/.065f;m.SetFloat("_WaterClock",period-.0001f);var phaseBed=sample();
     m.SetFloat("_WaterClock",period+.0001f);double bedPhaseError=Difference(phaseBed,sample());
     report.Add((bedPhaseError<.0005?"PASS":"FAIL")+" refracted bed and light focusing remain continuous at wave phase wrap; mean error="+bedPhaseError);
     m.SetFloat("_ProbeKind",5);m.SetFloat("_ProbeDepth",40);m.SetVector("_ProbeView",new Vector4(.8f,0,.6f,0));
     int invalidOffsets=0;
     foreach(float latitudeCell in new[]{1f,130f,254f})
     {
      m.SetVector("_ProbeCell",new Vector4(511.3f,latitudeCell,0,0));
      foreach(var pixel in sample())if(!Finite(pixel)||Mathf.Sqrt(pixel.r*pixel.r+pixel.g*pixel.g)>pixel.b+.00001f)invalidOffsets++;
     }
     report.Add((invalidOffsets==0?"PASS":"FAIL")+" grazing refraction stays inside wet shore limits at poles and date line; invalid="+invalidOffsets);
     m.SetFloat("_ProbeKind",6);m.SetVector("_ProbeView",new Vector4(0,-.6f,.8f,0));
     m.SetVector("_ScrollRelief",new Vector4(1,0,.72f,0));
     m.SetFloat("_ProbeDepth",2);var shallowOffset=sample();
     m.SetFloat("_ProbeDepth",20);var deepOffset=sample();
     double parallaxChange=Difference(shallowOffset,deepOffset);
     report.Add((parallaxChange>.003?"PASS":"FAIL")+" flat water refracts the bed with depth-dependent view parallax; mean change="+parallaxChange);
     m.SetFloat("_ProbeKind",7);m.SetFloat("_ScrollHeightScale",.000006f);
     m.SetVector("_ProbeCell",new Vector4(200,130,0,0));
     m.SetFloat("_ProbeDepth",80);var shelfOffset=sample();
     m.SetFloat("_ProbeDepth",320);var basinOffset=sample();
     double deepParallax=Difference(shelfOffset,basinOffset);
     report.Add((deepParallax>.008?"PASS":"FAIL")+" bathymetric parallax distinguishes deep basin elevations; mean change="+deepParallax);
     m.SetVector("_ScrollRelief",Vector4.zero);
    }
    finally{UnityEngine.Object.DestroyImmediate(substrate);}
    m.SetFloat("_ProbeKind",2);m.SetFloat("_WaterClock",3);
    var bed=new Texture2D(16,8,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
    try
    {
     var heights=new Color[128];for(int y=0;y<8;y++)for(int x=0;x<16;x++){float depth=10*Mathf.Pow(2,Mathf.Min(x,15-x));heights[y*16+x]=new Color(-depth,3,depth,0);}
     bed.SetPixels(heights);bed.Apply();m.SetTexture("_VisualTex",bed);m.SetVector("_MapSize",new Vector4(16,8,1f/16,1f/8));m.SetFloat("_FieldReady",1);m.SetFloat("_Relief",1);m.SetFloat("_ProbeKind",2);
     var relief=sample();float left=relief[32*128+32].r,right=relief[32*128+96].r;
     report.Add((Mathf.Abs(left-right)>.12f?"PASS":"FAIL")+" actual seabed opposing slopes remain visible; contrast="+Mathf.Abs(left-right));
     report.Add((right>left+.12f?"PASS":"FAIL")+" submerged basin slopes face the geographic light consistently with negative bed height; west="+left+" east="+right);
    }
    finally{UnityEngine.Object.DestroyImmediate(bed);}
    Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");File.WriteAllLines("Documentation/LocalPlanetUpgrade/water-cloud-regression.txt",report);
    if(report.Exists(line=>line.StartsWith("FAIL")))throw new InvalidOperationException(string.Join("\n",report));
   }
   finally{ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(image);}
  }
  static bool Finite(Color c)=>!float.IsNaN(c.r+c.g+c.b)&&!float.IsInfinity(c.r+c.g+c.b);
  static double Contrast(Color[] pixels)
  {
   double sum=0,squares=0;
   foreach(var pixel in pixels){double value=pixel.r*.2126+pixel.g*.7152+pixel.b*.0722;sum+=value;squares+=value*value;}
   return Math.Sqrt(Math.Max(0,squares/pixels.Length-sum*sum/(pixels.Length*pixels.Length)));
  }
  static Color[] Mirror(Color[] pixels,int width,int height)
  {
   var mirrored=new Color[pixels.Length];
   for(int y=0;y<height;y++)for(int x=0;x<width;x++)mirrored[y*width+x]=pixels[y*width+width-1-x];
   return mirrored;
  }
  static double MaximumDifference(Color[] a,Color[] b)
  {
   double error=0;
   for(int i=0;i<a.Length;i++)error=Math.Max(error,Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b))));
   return error;
  }
  static double Difference(Color[] a,Color[] b){double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return sum/a.Length;}
 }
}
