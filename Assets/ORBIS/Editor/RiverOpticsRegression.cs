using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class RiverOpticsRegression
    {
        const int Side = 128;

        [MenuItem("Tools/ORBIS/Local Planet/Validate river optics")]
        public static void Run()
        {
            var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/RiverOpticsProbe.shader"));
            var target = RenderTexture.GetTemporary(Side,Side,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var image = new Texture2D(Side,Side,TextureFormat.RGBAFloat,false,true);
            var previous = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            var report = new List<string>();
            Color[] Draw()
            {
                Graphics.Blit(Texture2D.whiteTexture,target,material);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,Side,Side),0,0);
                image.Apply();
                return image.GetPixels();
            }
            void Check(bool condition,string detail)
            {
                report.Add((condition?"PASS ":"FAIL ")+detail);
            }
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                ShaderUtil.CompilePass(material,0,true);
                material.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));
                material.SetFloat("_RiverAtlasReady",0);
                material.SetFloat("_RiverPreviewReady",0);
                material.SetFloat("_FieldReady",0);
                material.SetFloat("_VendorWaterReady",0);
                material.SetFloat("_WaterClock",0);
                material.SetFloat("_Bathymetry",0);
                material.SetFloat("_TestShore",-2);
                material.SetFloat("_TestBend",1.6f);
                var left = Draw();
                material.SetFloat("_TestBend",-1.6f);
                var right = Draw();
                material.SetFloat("_TestBend",0);
                var straight = Draw();
                int low=38*Side+Side/2,high=(Side-39)*Side+Side/2;
                bool northIsHigh=left[high].a>left[low].a;
                int south=northIsHigh?low:high,north=northIsHigh?high:low;
                float innerLeft = left[north].grayscale-left[south].grayscale;
                float innerRight = right[south].grayscale-right[north].grayscale;
                Check(innerLeft>.008f&&innerRight>.008f,"shallower inner banks transmit more light on opposite bends. Contrast "+innerLeft+", "+innerRight);
                Check(Mathf.Abs(straight[north].grayscale-straight[south].grayscale)<.003f,"straight reach has a symmetric cross-section");
                material.SetFloat("_TestOutput",1);
                material.SetFloat("_TestBend",1.6f);
                var profile = Draw();
                Check(profile[south].g>profile[north].g*1.6f&&profile[north].b>profile[south].b+.2f,
                    "outer pool is deeper and the deposit forms on the inside at the same radius");
                material.SetFloat("_TestOutput",3);
                material.SetFloat("_TestSediment",0);
                var gravel = Draw();
                material.SetFloat("_TestSediment",.8f);
                var silt = Draw();
                Check(gravel[north].grayscale-silt[north].grayscale>.015f&&Mathf.Abs(gravel[south].grayscale-silt[south].grayscale)<.001f,
                    "sediment changes the refracted point bar material and leaves the scoured outer bed clear");
                material.SetFloat("_TestOutput",5);
                var bank = Draw();
                int northBank=(northIsHigh?114:13)*Side+Side/2,southBank=(northIsHigh?13:114)*Side+Side/2;
                Check(bank[northBank].grayscale>bank[southBank].grayscale+.025f,"the exposed bar extends onto the dry bank");
                material.SetFloat("_TestOutput",2);
                material.SetFloat("_TestBend",0);
                var level = Draw();
                material.SetFloat("_TestGrade",160);
                var steep = Draw();
                float maxFlat=0,maxSteep=0;
                for(int y=30;y<Side-30;y++)for(int x=0;x<Side;x++)
                {
                    int i=y*Side+x;
                    maxFlat=Mathf.Max(maxFlat,level[i].g);
                    maxSteep=Mathf.Max(maxSteep,steep[i].g);
                }
                Check(maxFlat<.001f&&maxSteep>.12f,"riffles concentrate on falling reaches. Flat and steep "+maxFlat+", "+maxSteep);
                material.SetFloat("_TestOutput",4);
                material.SetFloat("_TestWaves",1);
                Orbis.Planet.OrbisAssetVisuals.BindWater(material);
                material.SetFloat("_TestGrade",0);
                float flatFoam=Foam(Draw());
                material.SetFloat("_TestGrade",160);
                float rapidFoam=Foam(Draw());
                Check(material.GetFloat("_VendorWaterReady")>.5f&&flatFoam<.001f&&rapidFoam>flatFoam+.005f,
                    "SW3 foam appears in broken water and clears from the level reach. Peak coverage "+flatFoam+", "+rapidFoam);
                material.SetFloat("_TestOutput",6);material.SetFloat("_TestHeading",0);
                material.SetFloat("_RiverSurvey",1);material.SetVector("_MapSize",new Vector4(4096,2048,1f/4096,1f/2048));
                material.SetFloat("_WaterClock",0);var still=Draw();
                material.SetFloat("_WaterClock",.1f);var next=Draw();
                material.SetFloat("_WaterClock",1.5f);var flowing=Draw();
                float shortChange=Difference(still,next,.99f),travel=Difference(still,flowing,.90f);
                Check(travel>.003f&&shortChange<.12f,$"resolved 1 km pool at Jingjiang scale, 1.5s P90 RGB change {travel:G4}, 0.1s P99 {shortChange:G4}");
                material.SetFloat("_TestHeading",-.0001f);var west=Draw();
                material.SetFloat("_TestHeading",.0001f);var east=Draw();
                float heading=Difference(west,east,.99f);
                material.SetFloat("_TestWorldOffset",1);float seam=Difference(east,Draw(),.99f);
                material.SetFloat("_TestWorldOffset",0);material.SetFloat("_WaterClock",4.9999f);var beforeReset=Draw();
                material.SetFloat("_WaterClock",5.0001f);float wrap=Difference(beforeReset,Draw(),.99f);
                Check(heading<.004f&&seam<.005f&&wrap<.004f,$"SW3 material continuity P99 at northward turn {heading:G4}, longitude wrap {seam:G4}, phase reset {wrap:G4}");
                material.SetFloat("_RiverSurvey",0);material.SetFloat("_TestOutput",4);
                material.SetFloat("_WaterClock",0);material.SetVector("_MapSize",new Vector4(512,256,1f/512,1f/256));
                material.SetFloat("_TestWaves",0);
                material.SetFloat("_TestGrade",0);
                material.SetFloat("_TestShore",-.0002f);
                var inlet=Draw();
                material.SetFloat("_TestShore",.0002f);
                var outlet=Draw();
                int centre=Side/2*Side+Side/2;
                material.SetFloat("_TestShore",.72f);
                var sea=Draw();
                Check(Mathf.Abs(inlet[centre].grayscale-outlet[centre].grayscale)<.002f&&
                    Mathf.Abs(outlet[centre].grayscale-sea[centre].grayscale)>.008f,
                    "freshwater and offshore optics meet continuously at the sea shore");
                material.SetFloat("_VendorWaterReady",0);
                material.SetFloat("_TestShore",.08f);material.SetFloat("_TestWaves",1);
                material.SetFloat("_WaterClock",1.5f);
                var surf=Draw();
                float inChannel=0,onBeach=0;
                for(int x=2;x<Side-2;x++)
                {
                    inChannel=Mathf.Max(inChannel,1-surf[(Side/2)*Side+x].a);
                    onBeach=Mathf.Max(onBeach,1-surf[4*Side+x].a);
                }
                Check(inChannel<.001f&&onBeach>.04f,"shore breakers stop at the open channel and continue on its banks. Foam "+inChannel+", "+onBeach);
                CheckEstuary(material,Draw,Check);
            }
            finally
            {
                Directory.CreateDirectory("ValidationOutput/RiverOptics");
                File.WriteAllLines("ValidationOutput/RiverOptics/river-optics-regression.txt",report);
                RenderTexture.active = previous;
                ShaderUtil.allowAsyncCompilation = async;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(image);
            }
            if(report.Exists(line=>line.StartsWith("FAIL")))throw new InvalidOperationException(string.Join("\n",report));
        }

        static void CheckEstuary(Material material, Func<Color[]> draw, Action<bool,string> check)
        {
            const int mid = Side / 2 * Side;
            int Sample(float shore) => mid + Mathf.FloorToInt((.5f + shore / 3) * Side);
            material.SetFloat("_TestOutput",7);
            foreach (float width in new[]{.06f, .2f})
            {
                material.SetFloat("_TestWidth",width);
                material.SetFloat("_TestHeading",0);
                material.SetFloat("_TestBed",0);
                var darkBed = draw();
                material.SetFloat("_TestBed",1);
                var whiteBed = draw();
                float inland = darkBed[Sample(-1.3f)].a, shore = darkBed[Sample(0)].a;
                float inner = darkBed[Sample(width)].a, outer = darkBed[Sample(2*width)].a;
                float sea = darkBed[Sample(1.35f)].a, beach = darkBed[2*Side+Side/2].a;
                bool monotone = true;
                float peakTransmission = 0;
                for (int i=mid+1; i<mid+Side; i++)
                {
                    if (darkBed[i].a > darkBed[i-1].a + .0001f) monotone = false;
                    // These renders differ only in bed albedo. Subtraction isolates transmitted light.
                    peakTransmission = Mathf.Max(peakTransmission,whiteBed[i].r-darkBed[i].r);
                }
                check(monotone && inland>.99f && shore>.70f && inner>outer && outer>.12f && sea<.001f && beach<.001f,
                    $"outflow with half-width {width:F2} cells, river weight upstream / shore / 1 and 2 half-widths offshore / sea / adjacent beach: {inland:F3}, {shore:F3}, {inner:F3}, {outer:F3}, {sea:F3}, {beach:F3}");
                check(peakTransmission<.56f,$"3 m channel meeting a 12 m basin: peak bed transmission through the mouth {peakTransmission:F3}");
                material.SetFloat("_TestHeading",Mathf.PI);
                check(draw()[Sample(2*width)].a<.01f,"reversing the flow removes the offshore extension");
            }

            material.SetFloat("_TestOutput",8);
            var tidalBed = draw();
            float stepMetres = 0;
            for (int y=0; y<Side; y++)
            {
                int centre = y*Side+Side/2;
                stepMetres = Mathf.Max(stepMetres,Mathf.Abs(tidalBed[centre].r-tidalBed[centre-1].r));
            }
            float shelfDepth = tidalBed[(Side-1)*Side+Side/2].r;
            check(stepMetres<.10f && Mathf.Abs(shelfDepth-24)<.001f,
                $"surveyed wet pixels crossing a 0.016 m DEM step, worst optical depth jump {stepMetres:F3} m, supported shelf {shelfDepth:F2} m");

            var grid = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try
            {
                material.SetFloat("_TestOutput",9);
                material.SetFloat("_FieldReady",1);
                material.SetFloat("_Relief",0);
                material.SetFloat("_Bathymetry",1);
                material.SetVector("_ScrollRelief",new Vector4(1,0,0,1));
                material.SetVector("_MapSize",new Vector4(4096,2048,1f/4096,1f/2048));
                material.SetTexture("_VisualTex",grid);
                float GridDepthContrast(float support)
                {
                    material.SetFloat("_TestGridSupport",support);
                    grid.SetPixel(0,0,Color.clear);
                    grid.Apply();
                    var empty = draw();
                    grid.SetPixel(0,0,new Color(0,0,24,0));
                    grid.Apply();
                    return Difference(empty,draw(),.5f);
                }
                float surveyed = GridDepthContrast(0), shelf = GridDepthContrast(1);
                check(surveyed<.001f && shelf>.01f,
                    $"bathymetric RGB response to coarse depth: survey-only estuary {surveyed:G4}, supported shelf {shelf:G4}");
            }
            finally { UnityEngine.Object.DestroyImmediate(grid); }
        }

        static float Foam(Color[] pixels)
        {
            float foam=0;
            for(int y=30;y<Side-30;y++)for(int x=0;x<Side;x++)foam=Mathf.Max(foam,1-pixels[y*Side+x].a);
            return foam;
        }

        static float Difference(Color[] a,Color[] b,float quantile)
        {
            var differences=new float[a.Length];
            for(int i=0;i<a.Length;i++)
                differences[i]=Mathf.Max(Mathf.Abs(a[i].r-b[i].r),Mathf.Max(Mathf.Abs(a[i].g-b[i].g),Mathf.Abs(a[i].b-b[i].b)));
            Array.Sort(differences);return differences[(int)((differences.Length-1)*quantile)];
        }

        [MenuItem("Tools/ORBIS/Local Planet/Validate all water and river optics")]
        public static void RunAll()
        {
            Run();
            WaterOpticsRegression.Run();
            WaterCoastRegression.Run();
            WaterCloudRegression.Run();
            WaterSunGlitterRegression.Run();
            string[] shaders =
            {
                "Assets/ORBIS/Resources/ORBIS/Surface.shader",
                "Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatMap.shader",
                "Assets/ORBIS/Resources/ORBIS/FlatWorld/LocalSurface.shader",
                "Assets/ORBIS/Resources/ORBIS/ScrollSurface.shader",
            };
            bool prevAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                foreach (string rawPath in shaders)
                {
                    string path = rawPath.Replace("ORBIS/ScrollSurface.shader", "ORBIS/FlatWorld/ScrollSurface.shader");
                    var s = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (!s || !s.isSupported || ShaderUtil.ShaderHasError(s))
                        throw new InvalidOperationException("Shader error or unsupported: " + path);
                    var m = new Material(s);
                    try
                    {
                        for (int p = 0; p < m.passCount; p++)
                        {
                            ShaderUtil.CompilePass(m, p, true);
                            if (!ShaderUtil.IsPassCompiled(m, p))
                                throw new InvalidOperationException($"Uncompiled pass {p} in {path}");
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(m); }
                    if (ShaderUtil.ShaderHasError(s))
                        throw new InvalidOperationException("Shader compilation error in " + path);
                }
            }
            finally { ShaderUtil.allowAsyncCompilation = prevAsync; }
            Debug.Log("[RiverOpticsRegression.RunAll] ALL 5 WATER/RIVER REGRESSION SUITES + PRODUCTION D3D11 SHADERS PASSED.");
        }
    }
}
