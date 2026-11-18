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

        static float Foam(Color[] pixels)
        {
            float foam=0;
            for(int y=30;y<Side-30;y++)for(int x=0;x<Side;x++)foam=Mathf.Max(foam,1-pixels[y*Side+x].a);
            return foam;
        }
    }
}
