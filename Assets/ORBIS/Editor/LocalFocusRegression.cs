using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Editor
{
    public static class LocalFocusRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate selection corners and footprint")]
        public static void Run()
        {
            const int width=240,height=160;
            var root=new GameObject("Local focus regression");
            var cameraObject=new GameObject("Local focus camera");
            cameraObject.transform.SetParent(root.transform,false);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            var read=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            var commands=new CommandBuffer();
            var report=new List<string>();
            bool async=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                camera.targetTexture=target;camera.pixelRect=new Rect(0,0,width,height);
                var world=FlatGenerator.Generate(64,32,"LOCAL-FOCUS",TerrainPreset.Continents,default);
                var map=root.AddComponent<FlatMapRenderer>();map.Initialize(world);
                var nav=root.AddComponent<FlatScrollNavigator>();nav.Initialize(world,camera,map);
                // A dark map isolates only the selection ink in the actual local surface pass.
                map.Material.SetTexture("_MainTex",Texture2D.blackTexture);
                foreach(string property in new[]{"_Natural","_Political","_DrawHeight","_WaterView","_ShowContours","_Grid"})
                    map.Material.SetFloat(property,0);
                var material=nav.SurfaceMaterial;
                int pass=material.FindPass("SCROLL_SURFACE");
                ShaderUtil.CompilePass(material,pass,true);
                var terrain=Array.Find(nav.GetComponentsInChildren<MeshRenderer>(),r=>r.sharedMaterial==material);
                var mesh=terrain.GetComponent<MeshFilter>().sharedMesh;
                Color[] Render()
                {
                    commands.Clear();commands.SetRenderTarget(target);commands.SetViewport(new Rect(0,0,width,height));
                    commands.ClearRenderTarget(true,true,Color.black);
                    commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,false));
                    commands.DrawMesh(mesh,terrain.localToWorldMatrix,material,0,pass);Graphics.ExecuteCommandBuffer(commands);
                    RenderTexture.active=target;read.ReadPixels(new Rect(0,0,width,height),0,0);read.Apply();
                    return read.GetPixels();
                }
                foreach(int row in new[]{1,16,30})foreach(int column in new[]{0,world.Width-1})
                {
                    int tile=world.Index(column,row);
                    nav.Focus(tile,0);nav.Tick(3,0);nav.PanBy(new Vector2(.39f,-.34f));nav.Tick(0,0);
                    map.Select(tile);nav.SyncMaterials();
                    var pixels=Render();
                    int left=width,right=-1,bottom=height,top=-1,count=0;
                    for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                    {
                        if(pixels[y*width+x].r<.25f)continue;
                        left=Math.Min(left,x);right=Math.Max(right,x);bottom=Math.Min(bottom,y);top=Math.Max(top,y);count++;
                    }
                    var center=new Vector2((left+right+1)*.5f,(bottom+top+1)*.5f);
                    var focus=material.GetVector("_LocalFocus");
                    Vector2 exact=camera.pixelRect.center+new Vector2(focus.x,focus.y)*focus.z;
                    bool picked=nav.TryGetPlanetLocation(exact,out var hit);
                    var expected=PlanetCoordinateSystem.FromMapUV((column+.5)/world.Width,(row+.5)/world.Height);
                    double error=PlanetLocalProjection.Distance(PlanetCoordinateSystem.SurfaceNormal(hit),PlanetCoordinateSystem.SurfaceNormal(expected),1)
                        *world.Width*nav.PixelsPerTile/(2*Math.PI);
                    int w=right-left+1,h=top-bottom+1;
                    bool corners=count>12&&w>=8&&h>=8&&Math.Abs(w-h)<=1&&pixels[((top+bottom)/2)*width+right].r<.25f
                        &&pixels[top*width+(left+right)/2].r<.25f;
                    report.Add((corners&&picked&&nav.Pick(center)==tile&&error<.20&&Vector2.Distance(exact,center)<.85f?"PASS ":"FAIL ")+
                        $"screen corners over the actual selected tile ({column},{row}), size={w}x{h}, geographic anchor error={error:F3}px");
                    for(int brush=0;brush<=3;brush++)
                    {
                        var area=FlatWorldApp.EditFootprint(world,tile,3,brush);
                        map.SetLocalTarget(tile,area);nav.SyncMaterials();
                        // Isolate the ground-aligned footprint from the view-aligned overlay.
                        material.SetVector("_LocalFocus",Vector4.zero);
                        pixels=Render();
                        int matched=0,total=0,inside=0;
                        for(int y=5;y<height;y+=13)for(int x=5;x<width;x+=11)
                        {
                            if(!nav.TryFlatPoint(new Vector2(x+.5f,y+.5f),out var cell))continue;
                            // Edge hits within floating-point noise have no unique side.
                            if(Math.Abs(cell.x-Mathf.Round(cell.x))<.0002f||Math.Abs(cell.y-Mathf.Round(cell.y))<.0002f)continue;
                            int hitTile=world.Index(Mathf.FloorToInt(cell.x),Mathf.FloorToInt(cell.y));
                            int dx=FMath.Wrap(hitTile%world.Width-column,world.Width);
                            dx=Math.Min(dx,world.Width-dx);
                            int dy=Math.Abs(hitTile/world.Width-row);
                            int radius=brush==2?1:brush==3?4:0;
                            bool intended=brush==1?world.ZoneOf(hitTile)==world.ZoneOf(tile):Math.Max(dx,dy)<=radius;
                            // Includes linear and gamma rendering of the subtle fill on black.
                            bool painted=pixels[y*width+x].r>.006f;
                            if(intended)inside++;
                            if(intended==painted)matched++;
                            total++;
                        }
                        report.Add((total>0&&inside>0&&matched==total?"PASS ":"FAIL ")+
                            $"geographic brush {brush} at ({column},{row}) including wrapped and clipped cells, matches={matched}/{total}, inside={inside}");
                    }
                }
                CheckSettlementArtwork(report);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;camera.targetTexture=null;
                commands.Release();RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(root);
            }
            Directory.CreateDirectory("ValidationOutput/local-focus");
            File.WriteAllLines("ValidationOutput/local-focus/selection-regression.txt",report);
            if(report.Exists(item=>item.StartsWith("FAIL",StringComparison.Ordinal)))throw new Exception(string.Join("\n",report));
        }
        static void CheckSettlementArtwork(List<string> report)
        {
            const int size=120;
            var target=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;var image=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true);
            var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PropShadowProbe.shader"));
            try
            {
                probe.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));probe.SetFloat("_ProbeMode",6);
                probe.SetFloat("_LocalPhysicalArtwork",1);probe.SetTexture("_InfoTex",Texture2D.whiteTexture);
                ShaderUtil.CompilePass(probe,0,true);
                foreach(int row in new[]{0,7,14,127,241,248,255})foreach(int column in new[]{0,511})
                {
                    probe.SetVector("_ProbeCaster",new Vector4(column,row,0,0));
                    Graphics.Blit(Texture2D.whiteTexture,target,probe);RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,size,size),0,0);image.Apply();
                    int union=0,intersection=0;
                    foreach(var color in image.GetPixels())
                    {
                        bool actual=color.r>.5f,expected=color.g>.5f;
                        if(actual||expected)union++;
                        if(actual&&expected)intersection++;
                    }
                    float overlap=(float)intersection/Math.Max(1,union);
                    report.Add((union>300&&overlap>.97f?"PASS ":"FAIL ")+
                        $"physical settlement footprint matches its original silhouette at ({column},{row}), overlap={overlap:P2}");
                }
            }
            finally
            {
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(probe);UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
