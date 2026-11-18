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
    GeographicGrid(result,nav,data);
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
    nav.Focus(30*data.Width+14,0);nav.Tick(3,0);
    for(int j=0;j<40;j++)nav.PanBy(new Vector2(1.3f,.7f));
    geographic=Orbis.Planet.PlanetCoordinateSystem.LocalFrame(Orbis.Planet.PlanetCoordinateSystem.FromWorld(nav.LocalFrame.Up,new Orbis.Planet.PlanetVector(),1),new Orbis.Planet.PlanetVector(),1);
    northAlignment=Orbis.Planet.PlanetVector.Dot(nav.LocalFrame.North,geographic.North);
    result.Add((northAlignment>.99999?"PASS":"FAIL")+" polar long pan keeps geographic north at the top, alignment="+northAlignment);
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
     nav.TryReliefPoint(screen,out var point);nav.TryGetPlanetLocation(screen,out var picked);
     var expectedDirection=chart.GeographicExp(point.x*Math.PI*2/data.Width,point.y*Math.PI*2/data.Width);
     double error=Orbis.Planet.PlanetLocalProjection.Distance(expectedDirection,Orbis.Planet.PlanetCoordinateSystem.SurfaceNormal(picked),1);
     result.Add((error<.000001?"PASS":"FAIL")+" detailed picking follows camera-centred geographic chart; row="+row+" radians="+error);
     var move=new Vector2(2.3f,1.2f);
     expectedDirection=chart.GeographicExp(move.x*Math.PI*2/data.Width,move.y*Math.PI*2/data.Width);
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
    var centreBounds=FlatGlobeGeometry.ReliefBounds(new Vector2(4e-8f,0),new Vector4(1,.000702f,.72f,0));
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
     geometry.SetVector("_ScrollRelief",new Vector4(1,0,.72f,0));
     Graphics.Blit(Texture2D.whiteTexture,geometryTarget,geometry);RenderTexture.active=geometryTarget;
     geometryRead.ReadPixels(new Rect(0,0,1,1),0,0);geometryRead.Apply();
     Color raised=geometryRead.GetPixel(0,0);
     var raisedPoint=new Vector3(raised.r,raised.g,raised.b);
     Vector3 surfaceNormal=nav.SurfaceMaterial.GetVector("_ScrollNormal"),surfaceUp=nav.SurfaceMaterial.GetVector("_ScrollUp");
     float lift=Vector3.Dot(raisedPoint-surfaceNormal,surfaceNormal),shift=Vector3.Dot(raisedPoint-surfaceNormal,surfaceUp);
     float expectedShift=lift*.72f*.18f/(.18f-lift);
     result.Add((lift>.001f&&Mathf.Abs(shift-expectedShift)<.00005f?"PASS":"FAIL")+" detailed terrain has actual height and oblique parallax; lift="+lift+" shift="+shift);
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
    if(mode==3){nav.OpenAtlas(3);nav.Wheel(-24,camera.pixelRect.center,3);nav.Tick(3,3);}
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
   var projection=typeof(FlatScrollNavigator).GetField("projection",flags);
   var localPoint=typeof(FlatScrollNavigator).GetMethod("LocalPoint",flags);
   var direction=typeof(FlatScrollNavigator).GetMethod("LocalDirection",flags);
   foreach(double latitude in new[]{-89.0,-80,80,89})foreach(float morph in new[]{.75f,.9f,.95f,.98f,.995f})
   {
    nav.Focus(16*data.Width+17,3);nav.Tick(3,3);
    projection.SetValue(nav,new PlanetLocalProjection(new PlanetLocation(.7,latitude*Math.PI/180)));
    nav.Tick(0,3);
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
  static void GeographicGrid(List<string> report,FlatScrollNavigator nav,WorldData data)
  {
   var localPoint=typeof(FlatScrollNavigator).GetMethod("LocalPoint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   foreach(int row in new[]{0,2,5,16,26,29,31})foreach(int column in new[]{0,17,63})
   {
    nav.Focus(row*data.Width+column,3);nav.Tick(3,3);
    var centre=PlanetCoordinateSystem.FromWorld(nav.LocalFrame.Up,new PlanetVector(),1);
    foreach(int size in new[]{1,4})
    {
     // Real 512x256 addresses reproduce the user's Tile and four-cell Zone.
     double span=Math.PI*2/512*size;
     double south=Math.Max(-Math.PI*.5+.000001,centre.Latitude-span*.5);
     double north=Math.Min(Math.PI*.5-.000001,centre.Latitude+span*.5);
     var corners=new Vector2[4];
     for(int i=0;i<4;i++)
     {
      var location=new PlanetLocation(centre.Longitude+((i&1)==0?-.5:.5)*span,i<2?south:north);
      corners[i]=(Vector2)localPoint.Invoke(nav,new object[]{PlanetCoordinateSystem.SurfaceNormal(location)});
     }
     float width=((corners[1]-corners[0]).magnitude+(corners[3]-corners[2]).magnitude)*.5f;
     float height=((corners[2]-corners[0]).magnitude+(corners[3]-corners[1]).magnitude)*.5f;
     double ratio=width/height;
     double expected=span/(north-south);
     float shear=Mathf.Max(Mathf.Abs(corners[0].y-corners[1].y),Mathf.Abs(corners[0].x-corners[2].x));
     report.Add((Math.Abs(ratio/expected-1)<.0001&&shear<span*.0001?"PASS":"FAIL")+" geographic Tile/Zone stays rectangular after dynamic recenter; row="+row+" column="+column+" size="+size+" ratio="+ratio+" shear="+shear);
    }
   }
   nav.Home(0);nav.Tick(3,0);
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
    foreach(bool atlas in new[]{false,true})
    {
     if(atlas)nav.OpenAtlas(3);
     foreach(int site in sites)
     {
      nav.Focus(site,3);nav.Tick(3,3);nav.Tick(3,3);nav.SyncMaterials();
      var expected=PlanetCoordinateSystem.FromMapUV((site%data.Width+.5)/data.Width,(site/data.Width+.5)/data.Height);
      bool centered=nav.TryGetPlanetLocation(camera.pixelRect.center,out var actual);
      double centerError=PlanetLocalProjection.Distance(PlanetCoordinateSystem.SurfaceNormal(expected),PlanetCoordinateSystem.SurfaceNormal(actual),1);
      centerError*=nav.PixelsPerTile*data.Width/(Math.PI*2);
      report.Add((centered&&centerError<.20?"PASS":"FAIL")+" relief focus keeps the requested geography centred; atlas="+atlas+" site="+site+" pixels="+centerError);
      material.CopyPropertiesFromMaterial(nav.SurfaceMaterial);
      int pass=atlas?2:1;ShaderUtil.CompilePass(material,pass,true);
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
      report.Add((missed==0&&maximumError<.20?"PASS":"FAIL")+" picking matches the rendered displaced triangles; atlas="+atlas+" site="+site+" missed="+missed+" pixels="+maximumError);
     }
     nav.TryGetPlanetLocation(camera.pixelRect.center,out var beforeReturn);
     nav.GoGlobe(3);nav.Tick(3,3);nav.TryGetPlanetLocation(camera.pixelRect.center,out var returned);
     double returnError=PlanetLocalProjection.Distance(PlanetCoordinateSystem.SurfaceNormal(beforeReturn),PlanetCoordinateSystem.SurfaceNormal(returned),1)*180/Math.PI;
     report.Add((nav.IsGlobe&&returnError<.01?"PASS":"FAIL")+" globe return preserves the visible relief centre; atlas="+atlas+" degrees="+returnError);
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
