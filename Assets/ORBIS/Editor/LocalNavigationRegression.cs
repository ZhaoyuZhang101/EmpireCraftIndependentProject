using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Orbis.Flat;
using Orbis.Planet;
namespace Orbis.Editor
{
 public static class LocalNavigationRegression
 {
  [MenuItem("Tools/ORBIS/Local Planet/Validate zoom drag and water")]
  public static void Run()
  {
   var result=new List<string>();var root=new GameObject("Navigation regression temporary");
   try
   {
    var cameraObject=new GameObject("Navigation regression camera");cameraObject.transform.SetParent(root.transform,false);
    var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.pixelRect=new Rect(0,0,1280,720);
    var data=FlatGenerator.Generate(64,32,"NAVIGATION-REGRESSION",TerrainPreset.Continents,default);
    var map=root.AddComponent<FlatMapRenderer>();map.Initialize(data);var nav=root.AddComponent<FlatScrollNavigator>();nav.Initialize(data,camera,map);
    result.Add((map.Material.GetFloat("_VendorWaterReady")>.5f&&nav.SurfaceMaterial.GetFloat("_VendorWaterReady")>.5f?"PASS":"FAIL")+" SW3 resource readiness survives map-to-local material copy");
    result.Add((map.Material.GetFloat("_FieldReady")>.5f&&nav.SurfaceMaterial.GetFloat("_FieldReady")>.5f&&nav.SurfaceMaterial.GetTexture("_VisualTex")==map.VisualTexture?"PASS":"FAIL")+" water reflection terrain fields survive map-to-local material copy");
    DisplayedGroundShape(result,nav,camera,data);
    TransitionRoutes(result,nav,camera,data);
    nav.Toggle(0);nav.Tick(.17f,0);float before=nav.PixelsPerTile;var center=nav.CenterTile;
    nav.Drag(new Vector2(55,12));nav.Tick(.2f,0);
    result.Add((Mathf.Abs(nav.PixelsPerTile-before)<.01f&&Vector2.Distance(center,nav.CenterTile)>.01f?"PASS":"FAIL")+" drag cancels pending zoom and moves geography without rescaling; pixels="+before+" -> "+nav.PixelsPerTile+"; moved="+Vector2.Distance(center,nav.CenterTile));
    nav.Toggle(0);for(int i=0;i<200;i++)nav.Tick(1/60f,0);
    if(!nav.IsFlat){nav.Toggle(0);for(int i=0;i<200;i++)nav.Tick(1/60f,0);}
    nav.Wheel(1,camera.pixelRect.center,0);for(int i=0;i<200;i++)nav.Tick(1/60f,0);
    result.Add((nav.IsTransitioning&&nav.Morph>.8f?"PASS":"FAIL")+" one outward wheel step stays close to surface, morph="+nav.Morph+" distance="+nav.Distance);
    nav.Wheel(-1,camera.pixelRect.center,0);for(int i=0;i<200;i++)nav.Tick(1/60f,0);
    result.Add((nav.IsFlat?"PASS":"FAIL")+" reversing wheel re-enters local view");
    nav.PanBy(new Vector2(9,6));nav.GoGlobe(0);for(int i=0;i<200;i++)nav.Tick(1/60f,0);
    var returned=camera.transform.rotation;nav.Drag(Vector2.zero);
    float jump=Quaternion.Angle(returned,camera.transform.rotation);
    result.Add((jump<.01f?"PASS":"FAIL")+" zero drag after unfolded return preserves orientation; degrees="+jump);
    returned=camera.transform.rotation;nav.Drag(new Vector2(.5f,.5f));
    jump=Quaternion.Angle(returned,camera.transform.rotation);
    result.Add((jump<.5f?"PASS":"FAIL")+" small return drag stays incremental; degrees="+jump);
    nav.Home(0);for(int i=0;i<200;i++)nav.Tick(1/60f,0);nav.Drag(new Vector2(170,-95));
    nav.TryGetPlanetLocation(camera.pixelRect.center,out var expectedLocation);
    var expected=Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(expectedLocation);
    nav.Wheel(-24,camera.pixelRect.center+new Vector2(100,45),0);
    var actual=nav.LocalFrame.Up;double centerError=Orbis.Planet.PlanetLocalProjection.Distance(expected,actual,1)*180/Math.PI;
    result.Add((centerError<.05?"PASS":"FAIL")+" unfolding uses viewport centre regardless of wheel cursor; degrees="+centerError);
    nav.Wheel(-24,camera.pixelRect.center,0);for(int i=0;i<200;i++)nav.Tick(1/60f,0);
    var geographic=Orbis.Planet.PlanetCoordinateSystem.LocalFrame(Orbis.Planet.PlanetCoordinateSystem.FromWorld(nav.LocalFrame.Up,new Orbis.Planet.PlanetVector(),1),new Orbis.Planet.PlanetVector(),1);
    double northAlignment=Orbis.Planet.PlanetVector.Dot(nav.LocalFrame.North,geographic.North);
    result.Add((nav.IsFlat&&northAlignment>.99999?"PASS":"FAIL")+" unfolded chart puts geographic north at the top, alignment="+northAlignment);
    nav.TryGetPlanetLocation(camera.pixelRect.center,out var flatCenter);
    centerError=Orbis.Planet.PlanetLocalProjection.Distance(expected,Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(flatCenter),1)*180/Math.PI;
    result.Add((centerError<.05?"PASS":"FAIL")+" final flat viewport remains centred on captured geography; degrees="+centerError);
    PolarDrag(result,nav,data);
    foreach(bool wheelExit in new[]{false,true})
    {
     nav.Focus(30*data.Width+14,0);nav.Tick(3,0);
     for(int j=0;j<40;j++)nav.PanBy(new Vector2(1.3f,.7f));
     if(wheelExit){for(int j=0;j<8;j++){nav.Wheel(24,camera.pixelRect.center,0);nav.Tick(3,0);}}
     else {nav.GoGlobe(0);nav.Tick(3,0);}
     var pose=camera.transform.rotation;var radial=camera.transform.position.normalized;
     var right=camera.transform.right;var up=camera.transform.up;
     nav.Drag(new Vector2(1,0));var horizontal=camera.transform.position.normalized-radial;
     float crossAxis=Mathf.Abs(Vector3.Dot(horizontal.normalized,up));
     float movement=Vector3.Angle(radial,camera.transform.position.normalized);
     nav.Drag(new Vector2(-1,0));float undo=Quaternion.Angle(pose,camera.transform.rotation);
     radial=camera.transform.position.normalized;nav.Drag(new Vector2(0,1));var vertical=camera.transform.position.normalized-radial;
     float axes=Mathf.Abs(Vector3.Dot(horizontal.normalized,vertical.normalized));
     result.Add((nav.IsGlobe&&movement>.20f&&crossAxis<.01f&&axes<.01f&&undo<.03f?"PASS":"FAIL")+" long polar pan return has independent viewport drag axes; wheel="+wheelExit+" movement="+movement+" crossAxis="+crossAxis+" axesDot="+axes+" undo="+undo);
    }
    foreach(bool wheelExit in new[]{false,true})
    {
     nav.Focus(16*data.Width+24,1.95);nav.Tick(3,1.95);nav.PanBy(new Vector2(6,3));
     var ground=nav.LocalFrame.Up;nav.Tick(0,2.35);
     if(wheelExit){for(int j=0;j<8;j++){nav.Wheel(24,camera.pixelRect.center,2.35);nav.Tick(3,2.35);}}
     else {nav.GoGlobe(2.35);nav.Tick(3,2.35);}
     nav.TryGetPlanetLocation(camera.pixelRect.center,out var groundAfter);
     double drift=Orbis.Planet.PlanetLocalProjection.Distance(ground,Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(groundAfter),1)*180/Math.PI;
     result.Add((drift<.05?"PASS":"FAIL")+" waiting unfolded across day wrap preserves returned geography; wheel="+wheelExit+" degrees="+drift);
     var freePose=camera.transform.rotation;nav.Tick(.1f,2.36);
     result.Add((Quaternion.Angle(freePose,camera.transform.rotation)<.01f?"PASS":"FAIL")+" globe camera stops following rotation after return");
    }
    nav.Home(3);nav.Tick(3,3);
    for(int j=0;j<35;j++)nav.Wheel(-3,camera.pixelRect.center,3);
    nav.Tick(3,3);nav.Tick(3,3);
    result.Add((nav.IsDetailed&&nav.PixelsPerTile>nav.ReturnPixelsPerTile*1.5f?"PASS":"FAIL")+" fast wheel burst carries zoom across unfolding; pixels="+nav.PixelsPerTile+" entry="+nav.ReturnPixelsPerTile);
    nav.SetViewport(new Rect(0,0,3840,720));
    foreach(int row in new[]{2,16,29})
    {
     nav.Focus(row*data.Width+17,3);nav.Tick(3,3);
     var frame=nav.LocalFrame;
     var chart=new Orbis.Planet.PlanetLocalProjection(Orbis.Planet.PlanetCoordinateSystem.FromWorld(frame.Up,new Orbis.Planet.PlanetVector(),1));
     var screen=camera.pixelRect.min+Vector2.Scale(new Vector2(.85f,.75f),camera.pixelRect.size);
     bool hit=nav.TryReliefPoint(screen,out var point)&&nav.Pick(screen)>=0;
     hit&=nav.TryGetPlanetLocation(screen,out var picked);
     var expectedDirection=chart.StereographicExp(point.x*Math.PI*2/data.Width,point.y*Math.PI*2/data.Width);
     double error=Orbis.Planet.PlanetLocalProjection.Distance(expectedDirection,Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(picked),1);
     result.Add((hit&&error<.000001?"PASS":"FAIL")+" detailed picking follows camera-centred stereographic chart; row="+row+" radians="+error);
     var move=new Vector2(2.3f,1.2f);
     expectedDirection=chart.StereographicExp(move.x*Math.PI*2/data.Width,move.y*Math.PI*2/data.Width);
     nav.PanBy(move);
     error=Orbis.Planet.PlanetLocalProjection.Distance(expectedDirection,nav.LocalFrame.Up,1);
     result.Add((error<.000001?"PASS":"FAIL")+" dragging dynamically recentres the displayed chart; row="+row+" radians="+error);
     nav.Focus(row*data.Width+17,3);nav.Tick(3,3);
     nav.TryGetPlanetLocation(screen,out var anchorBefore);
     nav.Wheel(-1,screen,3);nav.Tick(3,3);nav.TryGetPlanetLocation(screen,out var anchorAfter);
     double drift=Orbis.Planet.PlanetLocalProjection.Distance(Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(anchorBefore),Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(anchorAfter),1);
     drift=drift/(Math.PI*2/data.Width)*nav.PixelsPerTile;
     result.Add((drift<.05?"PASS":"FAIL")+" off-centre wheel zoom preserves its geographic anchor; row="+row+" pixels="+drift);
     nav.TryGetPlanetLocation(screen,out anchorBefore);
     nav.Wheel(1,screen,3);nav.Tick(3,3);nav.TryGetPlanetLocation(screen,out anchorAfter);
     drift=Orbis.Planet.PlanetLocalProjection.Distance(Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(anchorBefore),Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(anchorAfter),1);
     drift=drift/(Math.PI*2/data.Width)*nav.PixelsPerTile;
     result.Add((drift<.05?"PASS":"FAIL")+" off-centre outward wheel zoom preserves its geographic anchor; row="+row+" pixels="+drift);
    }
    nav.SetViewport(new Rect(0,0,1280,720));nav.Tick(0,3);
    PolarWheelGestures(result,nav,camera,data);
    var centreBounds=FlatGlobeGeometry.ReliefBounds(new Vector2(4e-8f,0),FlatGlobeGeometry.ReliefParameters(96,1,.000702f));
    result.Add((centreBounds.Contains(Vector2.zero)?"PASS":"FAIL")+" bounded relief picking retains the centre fan through world-space float rounding");
    var geometry=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PolarDetailProbe.shader"));
    var geometryTarget=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
    var geometryRead=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
    var geometryActive=RenderTexture.active;bool geometryAsync=ShaderUtil.allowAsyncCompilation;
    try
    {
     ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(geometry,0,true);
     var position=typeof(FlatScrollNavigator).GetMethod("Position",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
     int peak=0;
     for(int tile=1;tile<data.Count;tile++)if(data.Elevation[tile]>data.Elevation[peak])peak=tile;
     nav.Focus(peak,3);nav.Tick(3,3);
     geometry.CopyPropertiesFromMaterial(nav.SurfaceMaterial);geometry.SetFloat("_ProjectionProbe",3);
     geometry.SetVector("_ProbeOrigin",Vector4.zero);
     var seaLevelView=nav.SurfaceMaterial.GetVector("_ScrollRelief");
     seaLevelView.y=0;
     geometry.SetVector("_ScrollRelief",seaLevelView);
     CoastalMountainRelief(result,seaLevelView);
     Graphics.Blit(Texture2D.whiteTexture,geometryTarget,geometry);RenderTexture.active=geometryTarget;
     geometryRead.ReadPixels(new Rect(0,0,1,1),0,0);geometryRead.Apply();
     Color raised=geometryRead.GetPixel(0,0);
     var raisedPoint=new Vector3(raised.r,raised.g,raised.b);
     Vector3 surfaceNormal=nav.SurfaceMaterial.GetVector("_ScrollNormal"),surfaceUp=nav.SurfaceMaterial.GetVector("_ScrollUp");
     float lift=Vector3.Dot(raisedPoint-surfaceNormal,surfaceNormal),shift=Vector3.Dot(raisedPoint-surfaceNormal,surfaceUp);
     float expectedShift=lift*.72f*.18f/(.18f-lift);
     float metresShiftPerMetre=shift*6371008.8f/data.Elevation[peak];
     result.Add((lift>0&&Mathf.Abs(shift-expectedShift)<.000005f&&metresShiftPerMetre>.5f&&metresShiftPerMetre<3?"PASS":"FAIL")
      +" detailed GPU terrain retains visible parallax within three ground metres per metre of peak elevation; shift ratio="+metresShiftPerMetre);
     nav.Focus(16*data.Width+17,3);nav.Tick(3,3);
     foreach(float wheel in new[]{0f,4f,6f,12f})
     {
      nav.Wheel(wheel,camera.pixelRect.center,3);nav.Tick(3,3);
      geometry.CopyPropertiesFromMaterial(nav.SurfaceMaterial);geometry.SetFloat("_ProjectionProbe",3);
      var chartPoint=new Vector2(.03f,-.02f);geometry.SetVector("_ProbeOrigin",chartPoint);
      Graphics.Blit(Texture2D.whiteTexture,geometryTarget,geometry);RenderTexture.active=geometryTarget;
      geometryRead.ReadPixels(new Rect(0,0,1,1),0,0);geometryRead.Apply();
      Color gpu=geometryRead.GetPixel(0,0);var cpu=(Vector3)position.Invoke(nav,new object[]{chartPoint});
      float error=Vector3.Distance(cpu,new Vector3(gpu.r,gpu.g,gpu.b));
      result.Add((error<.00005f?"PASS":"FAIL")+" CPU selection and GPU geometry share unfolding projection and water height; morph="+nav.Morph+" error="+error);
     }
    }
    finally
    {
     RenderTexture.active=geometryActive;ShaderUtil.allowAsyncCompilation=geometryAsync;RenderTexture.ReleaseTemporary(geometryTarget);
     UnityEngine.Object.DestroyImmediate(geometry);UnityEngine.Object.DestroyImmediate(geometryRead);
    }
    RenderedPicking(result,nav,camera,data,new[]{2*data.Width+17,16*data.Width+17,29*data.Width+17});
    MinimapNavigation(result,nav,camera);
    map.SetLayer(MapLayer.Temperature);map.Configure(false,32);
    result.Add((map.VisibleLayer==MapLayer.Natural&&map.Material.GetFloat("_Natural")>.5f?"PASS":"FAIL")+" pending layer keeps old texture semantics");
    var timeout=System.Diagnostics.Stopwatch.StartNew();while(map.VisibleLayer!=MapLayer.Temperature&&timeout.Elapsed.TotalSeconds<10)map.Flush();
    result.Add((map.VisibleLayer==MapLayer.Temperature&&map.Material.GetFloat("_Natural")<.5f?"PASS":"FAIL")+" completed layer publishes texture and shader semantics together");
   }
   finally{UnityEngine.Object.DestroyImmediate(root);}
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/WaterFiniteProbe.shader");var material=new Material(shader);
   var rt=RenderTexture.GetTemporary(256,4,0,RenderTextureFormat.ARGBFloat);var previous=RenderTexture.active;var image=new Texture2D(256,4,TextureFormat.RGBAFloat,false,true);
   try
   {
    Graphics.Blit(Texture2D.whiteTexture,rt,material);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,256,4),0,0);image.Apply();int invalid=0;
    foreach(var p in image.GetPixels())if(float.IsNaN(p.r)||float.IsInfinity(p.r)||float.IsNaN(p.g)||float.IsInfinity(p.g))invalid++;
    result.Add((invalid==0?"PASS":"FAIL")+" GPU water slope finite from 0.1m to 4000m; invalid="+invalid);
   }
   finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(image);}
   Directory.CreateDirectory("Documentation/LocalPlanetUpgrade");File.WriteAllLines("Documentation/LocalPlanetUpgrade/navigation-regression.txt",result);
   if(result.Exists(line=>line.StartsWith("FAIL",StringComparison.Ordinal)))throw new Exception(string.Join("\n",result));
  }
  static void CoastalMountainRelief(List<string> report,Vector4 relief)
  {
   // The steep northern flank of Hawai'i folded over the coast in the 2048 Earth preset.
   const int width=2048,height=1024;
   var dem=Resources.Load<TextAsset>("ORBIS/FlatWorld/Earth2048").GetData<short>();
   var chart=new PlanetLocalProjection(new PlanetLocation(-155.57*Math.PI/180,19.76*Math.PI/180));
   Vector2 Sample(int x,int y,out Vector2 displaced)
   {
    var location=PlanetCoordinateSystem.FromMapUV((x+.5)/width,(y+.5)/height);
    var chartPoint=chart.StereographicLog(PlanetCoordinateSystem.SurfaceNormal(location));
    var point=new Vector2((float)chartPoint.X,(float)chartPoint.Y);
    int metres=Math.Max(0,(int)dem[(height-1-y)*width+x]);
    displaced=FlatGlobeGeometry.ReliefPosition(point,metres*FlatGlobeGeometry.HeightScale,relief);
    return point;
   }
   float Area(Vector2 a,Vector2 b,Vector2 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
   float smallest=float.PositiveInfinity,largest=0;
   for(int y=619;y<628;y++)for(int x=135;x<143;x++)
   {
    var a=Sample(x,y,out var pa);
    var b=Sample(x+1,y,out var pb);
    var c=Sample(x,y+1,out var pc);
    var d=Sample(x+1,y+1,out var pd);
    float lower=Area(pa,pb,pc)/Area(a,b,c);
    float upper=Area(pc,pb,pd)/Area(c,b,d);
    smallest=Mathf.Min(smallest,Mathf.Min(lower,upper));largest=Mathf.Max(largest,Mathf.Max(lower,upper));
   }
   report.Add((smallest>.5f&&largest<2?"PASS":"FAIL")+" Hawaii coast and mountain faces retain winding and area; min="+smallest+" max="+largest);
   var basinPoint=new Vector2(.004f,-.003f);
   Vector2 basinScreen=FlatGlobeGeometry.ReliefPosition(basinPoint,-430*FlatGlobeGeometry.HeightScale,relief);
   report.Add((FlatGlobeGeometry.ReliefBounds(basinScreen,relief).Contains(basinPoint)?"PASS":"FAIL")
    +" picking bounds include exposed terrain below sea level");
  }
  static void MinimapNavigation(List<string> report,FlatScrollNavigator nav,Camera camera)
  {
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   var getProjection=typeof(FlatScrollNavigator).GetMethod("MinimapProjection",flags);
   var sample=getProjection.ReturnType.GetMethod("Sample",flags);
   var highLatitude=PlanetCoordinateSystem.FromMapUV(.998,.96);
   nav.Home(3);nav.Tick(3,3);
   for(int mode=0;mode<4;mode++)
   {
    if(mode==1||mode==2){nav.Wheel(-24,camera.pixelRect.center,3);nav.Tick(3,3);}
    if(mode==3){nav.OpenAtlas(3);nav.Wheel(-2,camera.pixelRect.center,3);nav.Tick(3,3);}
    float zoom=nav.PixelsPerTile,distance=nav.Distance,morph=nav.Morph;
    nav.Recenter(highLatitude,3);
    var footprint=getProjection.Invoke(nav,null);
    var marker=(Vector2)sample.Invoke(footprint,new object[]{Vector2.zero});
    var indicated=PlanetCoordinateSystem.FromMapUV(marker.x,marker.y);
    double error=PlanetLocalProjection.Distance(PlanetCoordinateSystem.SurfaceNormal(indicated),PlanetCoordinateSystem.SurfaceNormal(highLatitude),1);
    bool scale=Mathf.Abs(nav.PixelsPerTile-zoom)<.001f&&Mathf.Abs(nav.Distance-distance)<.00001f&&Mathf.Abs(nav.Morph-morph)<.00001f;
    bool expected=mode==0?nav.IsGlobe:mode==1?nav.IsTransitioning:mode==2?nav.IsFlat&&!nav.IsAtlas:nav.IsAtlas;
    report.Add((expected&&scale&&error<.002?"PASS":"FAIL")+" minimap jump retains scale and camera centre; mode="+mode+" radians="+error);
    double worst=0;
    foreach(var ndc in new[]{Vector2.zero,new Vector2(.15f,.25f),new Vector2(.40f,-.55f),new Vector2(-.5f,.40f)})
    {
     var screen=camera.pixelRect.center+Vector2.Scale(camera.pixelRect.size,ndc)*.5f;
     var uv=(Vector2)sample.Invoke(footprint,new object[]{ndc});
     var predicted=PlanetCoordinateSystem.SurfaceNormal(PlanetCoordinateSystem.FromMapUV(uv.x,uv.y));
     if(nav.TryGetPlanetLocation(screen,out var picked))
      worst=Math.Max(worst,PlanetLocalProjection.Distance(predicted,PlanetCoordinateSystem.SurfaceNormal(picked),1));
     else if(nav.IsGlobe)
     {
      double arc=PlanetLocalProjection.Distance(predicted,PlanetCoordinateSystem.SurfaceNormal(indicated),1);
      worst=Math.Max(worst,Math.Abs(arc-Math.Acos(1/camera.transform.position.magnitude)));
     }
     else {worst=double.PositiveInfinity;break;}
    }
    report.Add((worst<.014?"PASS":"FAIL")+" minimap footprint agrees with visible terrain within one map pixel; mode="+mode+" radians="+worst);
   }
  }
  static void TransitionRoutes(List<string> report,FlatScrollNavigator nav,Camera camera,WorldData data)
  {
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   var localPoint=typeof(FlatScrollNavigator).GetMethod("LocalPoint",flags);
   var direction=typeof(FlatScrollNavigator).GetMethod("LocalDirection",flags);
   foreach(double latitude in new[]{-89.0,-80,80,89})foreach(float morph in new[]{.75f,.9f,.95f,.98f,.995f})
   {
    nav.Focus(16*data.Width+17,3);nav.Tick(3,3);
    nav.Recenter(new PlanetLocation(Math.PI-.01,latitude*Math.PI/180),3);
    float altitude=.22f+(.6f-.22f)*(1-morph);
    nav.Wheel(Mathf.Log(altitude/.22f)/.075f,camera.pixelRect.center,3);nav.Tick(3,3);
    double maximum=0;
    foreach(var chart in new[]{new Vector2(.13617012f,.07036180f),new Vector2(-.13617012f,-.07036180f),new Vector2(.08f,.03f),new Vector2(-.08f,-.03f)})
    {
     var expected=(PlanetVector)direction.Invoke(nav,new object[]{chart});
     var point=(Vector2)localPoint.Invoke(nav,new object[]{expected});
     var actual=(PlanetVector)direction.Invoke(nav,new object[]{point});
     maximum=Math.Max(maximum,PlanetLocalProjection.Distance(expected,actual,1));
    }
    report.Add((maximum<.000001?"PASS":"FAIL")+" route inverse follows blended polar projection; latitude="+latitude+" morph="+morph+" radians="+maximum);
   }
   int centre=16*data.Width+17;
   nav.Focus(centre,3);nav.Tick(3,3);
   nav.Wheel(Mathf.Log(.239f/.22f)/.075f,camera.pixelRect.center,3);nav.Tick(3,3);
   nav.SetRoute(new[]{centre-1,centre,centre+data.Width/2,centre,centre+1});
   Mesh routeMesh=null;
   foreach(var filter in nav.GetComponentsInChildren<MeshFilter>())if(filter.sharedMesh.name=="Clipped local route")routeMesh=filter.sharedMesh;
   report.Add((routeMesh&&routeMesh.vertexCount==8&&routeMesh.triangles.Length==12?"PASS":"FAIL")+" off-patch route endpoint cuts adjacent segments while retaining both visible sections");
   nav.SetRoute(new int[0]);
   nav.Home(0);nav.Tick(3,0);
  }
  static void DisplayedGroundShape(List<string> report,FlatScrollNavigator nav,Camera camera,WorldData data)
  {
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   var localPoint=typeof(FlatScrollNavigator).GetMethod("LocalPoint",flags);
   var localDirection=typeof(FlatScrollNavigator).GetMethod("LocalDirection",flags);
   nav.Focus(16*data.Width+17,3);nav.Tick(3,3);
   foreach(double latitude in new[]{-90d,-89.99,-85,-80,-60,0,60,80,85,89.99,90})
   {
    nav.Recenter(new PlanetLocation(Math.PI-.0001,latitude*Math.PI/180),3);
    double pixelsPerRadian=camera.pixelHeight/(2*camera.orthographicSize);
    double radius=40/pixelsPerRadian,worstAxes=1,smallestRadius=double.PositiveInfinity;
    foreach(float u in new[]{-.7f,0,.7f})foreach(float v in new[]{-.7f,0,.7f})
    {
     // Flatten relief for this optical calibration; keep the actual chart and camera scale.
     var offset=new Vector2(u*camera.aspect,v)*camera.orthographicSize;
     var direction=(PlanetVector)localDirection.Invoke(nav,new object[]{offset});
     var site=new PlanetLocalProjection(PlanetCoordinateSystem.FromWorld(direction,new PlanetVector(),1));
     double sx=0,sy=0,xx=0,yy=0,xy=0;
     const int samples=64;
     for(int i=0;i<samples;i++)
     {
      double angle=2*Math.PI*i/samples;
      var ground=site.Exp(radius*Math.Cos(angle),radius*Math.Sin(angle));
      var point=(Vector2)localPoint.Invoke(nav,new object[]{ground});
      double x=(point.x-offset.x)*pixelsPerRadian,y=(point.y-offset.y)*pixelsPerRadian;
      sx+=x;sy+=y;xx+=x*x;yy+=y*y;xy+=x*y;
     }
     xx-=sx*sx/samples;yy-=sy*sy/samples;xy-=sx*sy/samples;
     double trace=xx+yy,spread=Math.Sqrt((xx-yy)*(xx-yy)+4*xy*xy);
     worstAxes=Math.Max(worstAxes,Math.Sqrt((trace+spread)/(trace-spread)));
     smallestRadius=Math.Min(smallestRadius,Math.Sqrt((trace-spread)/samples));
    }
    report.Add((worstAxes<=1.03&&smallestRadius>=32?"PASS":"FAIL")+" geodesic circles across central 70% of flat camera; latitude="+latitude+" dateLine=true worstAxes="+worstAxes+" minimumRadiusPx="+smallestRadius);
   }
   nav.Home(0);nav.Tick(3,0);
  }
  static void PolarDrag(List<string> report,FlatScrollNavigator nav,WorldData data)
  {
   double tile=2*Math.PI/data.Width;
   foreach(double sign in new[]{-1d,1})
   {
    nav.Recenter(new PlanetLocation(Math.PI-.001,sign*89*Math.PI/180),0);
    double maximumTurn=0;
    for(int step=0;step<12;step++)
    {
     var before=nav.LocalFrame.East;
     nav.PanBy(new Vector2(0,(float)(sign*.003/tile)));
     maximumTurn=Math.Max(maximumTurn,PlanetLocalProjection.Distance(before,nav.LocalFrame.East,1)*180/Math.PI);
    }
    var crossed=PlanetCoordinateSystem.FromWorld(nav.LocalFrame.Up,new PlanetVector(),1);
    double longitudeChange=Math.Abs(PlanetCoordinateSystem.NearestOffset(crossed.Longitude-(Math.PI-.001),2*Math.PI));
    nav.PanBy(new Vector2(0,(float)(sign*.35/tile)));
    for(int i=0;i<200;i++)nav.Tick(1/60f,0);
    var north=PlanetCoordinateSystem.LocalFrame(PlanetCoordinateSystem.FromWorld(nav.LocalFrame.Up,new PlanetVector(),1),new PlanetVector(),1).North;
    double alignment=PlanetVector.Dot(north,nav.LocalFrame.North);
    report.Add((longitudeChange>3&&maximumTurn<1&&alignment>.99999?"PASS":"FAIL")+" drag crosses pole with continuous heading then restores geographic north; hemisphere="+sign+" largestTurnDegrees="+maximumTurn+" northAlignment="+alignment);
   }
  }
  static void PolarWheelGestures(List<string> report,FlatScrollNavigator nav,Camera camera,WorldData data)
  {
   foreach(double latitude in new[]{-89.99,-80,80,89.99})
   {
    var center=camera.pixelRect.center;
    var screen=center+Vector2.Scale(camera.pixelRect.size,new Vector2(.31f,.23f));
    nav.Focus(16*data.Width+17,3);nav.Tick(3,3);
    nav.Wheel(-6,center,3);nav.Tick(3,3);
    nav.Recenter(new PlanetLocation(Math.PI-.0001,latitude*Math.PI/180),3);
    bool hit=nav.TryGetPlanetLocation(screen,out var ground);
    var anchor=PlanetCoordinateSystem.SurfaceNormal(ground);
    double maximum=0;
    nav.Wheel(-2,screen,3);
    for(int step=0;step<75;step++)
    {
     if(step==10)nav.Wheel(2,screen,3);
     nav.Tick(1/60f,3);
     hit&=nav.IsFlat&&nav.TryGetPlanetLocation(screen,out ground);
     var direction=PlanetCoordinateSystem.SurfaceNormal(ground);
     maximum=Math.Max(maximum,PlanetLocalProjection.Distance(anchor,direction,1)*nav.PixelsPerTile*data.Width/(2*Math.PI));
    }
    report.Add((hit&&maximum<.05?"PASS":"FAIL")+" continuous inward and reversed wheel keep the same displaced terrain point; latitude="+latitude+" worstPixels="+maximum);
   }
  }
  static void RenderedPicking(List<string> report,FlatScrollNavigator nav,Camera camera,WorldData data,int[] sites)
  {
   const int width=128,height=72;
   var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
   var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
   var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PolarDetailProbe.shader"));
   var commands=new CommandBuffer();var previous=RenderTexture.active;
   bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   try
   {
    nav.SetViewport(new Rect(0,0,1280,720));
    foreach(string origin in new[]{"local","atlas focus","Markley overview"})
    {
     foreach(int site in sites)
     {
      var expected=PlanetCoordinateSystem.FromMapUV((site%data.Width+.5)/data.Width,(site/data.Width+.5)/data.Height);
      if(origin!="local")nav.OpenAtlas(3);
      if(origin=="Markley overview")nav.Recenter(expected,3);
      else nav.Focus(site,3);
      nav.Tick(3,3);nav.Tick(3,3);nav.SyncMaterials();
      bool centered=nav.TryGetPlanetLocation(camera.pixelRect.center,out var actual);
      double centerError=PlanetLocalProjection.Distance(PlanetCoordinateSystem.SurfaceNormal(expected),PlanetCoordinateSystem.SurfaceNormal(actual),1);
      centerError*=nav.PixelsPerTile*data.Width/(Math.PI*2);
      report.Add((centered&&centerError<.20&&nav.IsAtlas==(origin=="Markley overview")?"PASS":"FAIL")+" view keeps the requested geography centred; path="+origin+" site="+site+" pixels="+centerError);
      material.CopyPropertiesFromMaterial(nav.SurfaceMaterial);
      int pass=nav.IsAtlas?2:1;ShaderUtil.CompilePass(material,pass,true);
      commands.Clear();commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,width,height));
      commands.ClearRenderTarget(true,true,Color.clear);
      commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,false));
      foreach(var renderer in nav.GetComponentsInChildren<MeshRenderer>())
      {
       if(!renderer.enabled||renderer.sharedMaterial!=nav.SurfaceMaterial)continue;
       var filter=renderer.GetComponent<MeshFilter>();var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
       commands.DrawMesh(filter.sharedMesh,filter.transform.localToWorldMatrix,material,0,pass,block);
      }
      Graphics.ExecuteCommandBuffer(commands);RenderTexture.active=target;
      image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
      double maximumError=0;int missed=0;
      foreach(int y in new[]{10,36,61})foreach(int x in new[]{16,64,111})
      {
       Color pixel=image.GetPixel(x,y);
       var screen=camera.pixelRect.min+Vector2.Scale(new Vector2((x+.5f)/width,(y+.5f)/height),camera.pixelRect.size);
       if(pixel.a<.5f||!nav.TryGetPlanetLocation(screen,out var picked)){missed++;continue;}
       var gpu=new PlanetVector(pixel.r,pixel.g,pixel.b);
       double error=PlanetLocalProjection.Distance(gpu,PlanetCoordinateSystem.SurfaceNormal(picked),1);
       error*=nav.PixelsPerTile*nav.ProjectionSize.x/(Math.PI*2);
       maximumError=Math.Max(maximumError,error);
      }
      report.Add((missed==0&&maximumError<.20?"PASS":"FAIL")+" picking matches the rendered displaced triangles; path="+origin+" site="+site+" missed="+missed+" pixels="+maximumError);
     }
     nav.TryGetPlanetLocation(camera.pixelRect.center,out var beforeReturn);
     nav.GoGlobe(3);nav.Tick(3,3);nav.TryGetPlanetLocation(camera.pixelRect.center,out var returned);
     double returnError=PlanetLocalProjection.Distance(PlanetCoordinateSystem.SurfaceNormal(beforeReturn),PlanetCoordinateSystem.SurfaceNormal(returned),1)*180/Math.PI;
     report.Add((nav.IsGlobe&&returnError<.01?"PASS":"FAIL")+" globe return preserves the visible relief centre; path="+origin+" degrees="+returnError);
    }
   }
   finally
   {
    RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;commands.Release();
    RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(material);
   }
  }
 }
}
