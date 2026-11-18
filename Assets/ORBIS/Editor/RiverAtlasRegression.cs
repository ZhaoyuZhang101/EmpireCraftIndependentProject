using System;
using System.Collections.Generic;
using System.IO;
using Orbis.Flat;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class RiverAtlasRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate uploaded continuous rivers")]
        public static void Run()
        {
            var map=UnityEngine.Object.FindFirstObjectByType<FlatMapRenderer>(FindObjectsInactive.Include);
            if(map==null||!map.RiverAtlasReady)
                throw new InvalidOperationException("Run a world and wait for its river field to finish uploading.");
            var curves=map.World.Facts.RiverCurves;
            var positions=new List<Vector2>();
            int shiftedLand=0;
            for(int k=0;k<curves.ActiveTileCount;k++)
            {
                int tile=curves.GetActiveTile(k),x=tile%curves.Width,y=tile/curves.Width;
                foreach(float dx in new[]{0f,.125f,.5f,.875f,1f})
                foreach(float dy in new[]{0f,.5f,1f})
                    positions.Add(new Vector2(x+dx,Mathf.Min(curves.Height-.001f,y+dy)));
                if(map.World.Water[tile]==WaterKind.Land)shiftedLand++;
            }
            // Identical wrapped and clamped coordinates exercise both addressing edges.
            foreach(float x in new[]{-.125f,0f,curves.Width-.125f,(float)curves.Width})
            foreach(float y in new[]{-.001f,.001f,curves.Height-.001f,curves.Height+.001f})
                positions.Add(new Vector2(x,y));
            const int width=256;
            int height=Mathf.CeilToInt(positions.Count/(float)width);
            var points=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true)
            {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/RiverAtlasProbe.shader"));
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var previous=RenderTexture.active;
            bool async=ShaderUtil.allowAsyncCompilation;
            var report=new List<string>();
            try
            {
                var pixels=new Color[width*height];
                for(int k=0;k<positions.Count;k++)pixels[k]=new Color(positions[k].x,positions[k].y,0,0);
                points.SetPixels(pixels);points.Apply();
                material.SetTexture("_ProbeCells",points);material.SetFloat("_ProbePoints",1);
                material.SetTexture("_VisualTex",map.VisualTexture);
                material.SetTexture("_RiverAtlas",map.RiverAtlasTexture);
                material.SetVector("_RiverAtlasLayout",map.Material.GetVector("_RiverAtlasLayout"));
                material.SetFloat("_RiverAtlasReady",1);
                material.SetVector("_MapSize",new Vector4(curves.Width,curves.Height,1f/curves.Width,1f/curves.Height));
                ShaderUtil.allowAsyncCompilation=false;ShaderUtil.CompilePass(material,0,true);
                Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();pixels=image.GetPixels();
                float distanceError=0,depthError=0,relativeDepthError=0,flowWidthError=0;
                string depthDetail="";
                int invalid=0,wetSamples=0;
                for(int k=0;k<positions.Count;k++)
                {
                    Vector2 point=positions[k];
                    var expected=curves.Sample(point.x,Mathf.Clamp(point.y,0,curves.Height-.0001f));
                    Color actual=pixels[k];
                    if(float.IsNaN(actual.r+actual.g+actual.b+actual.a)||float.IsInfinity(actual.r+actual.g+actual.b+actual.a))invalid++;
                    distanceError=Mathf.Max(distanceError,Mathf.Abs(actual.r-expected.Distance));
                    // At polar clamp points the address can fall between raster nodes.
                    if(point.y<0||point.y>=curves.Height-.01f)continue;
                    float depthDifference=Mathf.Abs(actual.g-expected.Depth);
                    if(depthDifference>depthError)
                    {
                        depthError=depthDifference;
                        depthDetail=", deepest error at "+point+" CPU="+expected.Depth+" GPU="+actual.g;
                    }
                    relativeDepthError=Mathf.Max(relativeDepthError,depthDifference/Mathf.Max(1,expected.Depth));
                    flowWidthError=Mathf.Max(flowWidthError,Mathf.Abs(actual.b-expected.FlowX*expected.HalfWidth),Mathf.Abs(actual.a-expected.FlowY*expected.HalfWidth));
                    if(expected.Distance<0)wetSamples++;
                }
                report.Add((distanceError<.006f&&relativeDepthError<.0007f&&flowWidthError<.001f&&invalid==0&&wetSamples>0?"PASS ":"FAIL ")+
                    "GPU uploaded field matches global CPU geometry, samples="+positions.Count+", wet="+wetSamples+", active land receivers="+shiftedLand+
                    ", distanceError="+distanceError+", depthError="+depthError+", relativeDepthError="+relativeDepthError+", tangentWidthError="+flowWidthError+", invalid="+invalid+depthDetail);
                if(curves.Seasons!=null)
                {
                    material.SetTexture("_RiverSeasonTex",map.RiverSeasonTexture);
                    material.SetFloat("_RiverSeasonReady",1);
                    int seasonalSamples=0,changed=0;float bankError=0,depthFraction=0,widthError=0;
                    // Include interpolation between December and January.
                    foreach(double day in new[]{30.0,136.5,345.0})
                    {
                        double phase=day/30;int month=(int)phase;
                        material.SetVector("_RiverSeason",new Vector4(month,(month+1)%12,(float)(phase-month),0));
                        Graphics.Blit(Texture2D.whiteTexture,target,material);RenderTexture.active=target;
                        image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();var seasonalPixels=image.GetPixels();
                        for(int k=0;k<positions.Count;k++)
                        {
                            var point=positions[k];
                            if(point.y<.01f||point.y>curves.Height-.01f||pixels[k].r>.18f||pixels[k].g<=0)continue;
                            var cpu=curves.SampleAtDay(point.x,point.y,day);var gpu=seasonalPixels[k];
                            bankError=Mathf.Max(bankError,Mathf.Abs(gpu.r-cpu.Distance));
                            depthFraction=Mathf.Max(depthFraction,Mathf.Abs(gpu.g-cpu.Depth)/Mathf.Max(1,cpu.Depth));
                            widthError=Mathf.Max(widthError,Mathf.Abs(new Vector2(gpu.b,gpu.a).magnitude-cpu.HalfWidth));
                            if(Mathf.Abs(gpu.r-pixels[k].r)>.003f)changed++;
                            seasonalSamples++;
                        }
                    }
                    report.Add((seasonalSamples>0&&changed>0&&bankError<.006f&&depthFraction<.002f&&widthError<.001f?"PASS ":"FAIL ")+
                        "seasonal GPU water matches CPU banks, widths and depths, samples="+seasonalSamples+", changed="+changed+
                        ", bankError="+bankError+", widthError="+widthError+", relativeDepthError="+depthFraction);
                }
                int clones=0;
                foreach(var candidate in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if(candidate==map.Material||candidate.shader==null)continue;
                    string shader=candidate.shader.name;
                    if(shader!="ORBIS/FlatWorld/LocalSurface"&&shader!="ORBIS/FlatWorld/ScrollSurface")continue;
                    if(candidate.GetTexture("_VisualTex")!=map.VisualTexture)continue;
                    clones++;
                    report.Add((candidate.GetTexture("_RiverAtlas")==map.RiverAtlasTexture&&candidate.GetFloat("_RiverAtlasReady")>.5f?"PASS ":"FAIL ")+candidate.name+" shares the published river field");
                }
                report.Add((clones>0?"PASS ":"FAIL ")+"navigation surface bindings checked, materials="+clones);
                report.Add("INFO atlas="+map.RiverAtlasTexture.width+"x"+map.RiverAtlasTexture.height+", format="+map.RiverAtlasTexture.format+", readable="+map.RiverAtlasTexture.isReadable);
                if(report.Exists(line=>line.StartsWith("FAIL")))throw new InvalidOperationException(string.Join("\n",report));
            }
            finally
            {
                Directory.CreateDirectory("ValidationOutput/RiverAtlas");
                File.WriteAllLines("ValidationOutput/RiverAtlas/river-atlas-regression.txt",report);
                RenderTexture.active=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.ReleaseTemporary(target);
                foreach(var resource in new UnityEngine.Object[]{points,image,material})UnityEngine.Object.DestroyImmediate(resource);
            }
        }
    }
}
