using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Orbis.Flat;
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
    var camera=root.AddComponent<Camera>();camera.enabled=false;camera.pixelRect=new Rect(0,0,1280,720);
    var data=FlatGenerator.Generate(64,32,"NAVIGATION-REGRESSION",TerrainPreset.Continents,default);
    var map=root.AddComponent<FlatMapRenderer>();map.Initialize(data);var nav=root.AddComponent<FlatScrollNavigator>();nav.Initialize(data,camera,map);
    result.Add((map.Material.GetFloat("_VendorWaterReady")>.5f&&nav.SurfaceMaterial.GetFloat("_VendorWaterReady")>.5f?"PASS":"FAIL")+" SW3 resource readiness survives map-to-local material copy");
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
  }
 }
}
