using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    public static class LightningRegression
    {
        [MenuItem("Tools/ORBIS/Local Planet/Validate localized lightning")]
        public static void Run()
        {
            var material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/FlatNightProbe.shader"));
            var target=RenderTexture.GetTemporary(128,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(128,64,TextureFormat.RGBAFloat,false,true);
            var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
            var report=new List<string>();ShaderUtil.allowAsyncCompilation=false;
            try
            {
                ShaderUtil.CompilePass(material,0,true);
                foreach(var error in ShaderUtil.GetShaderMessages(material.shader))
                    if(error.severity.ToString()=="Error")throw new InvalidOperationException(error.message);
                material.SetFloat("_ProbeLightning",1);material.SetFloat("_SkyReady",1);
                material.SetFloat("_FlatClouds",1);material.SetFloat("_FlatLightning",1);
                material.SetVector("_FlatSun",Vector3.forward);material.SetFloat("_SkyClock",4);
                var flashes=new Vector4[4];flashes[0]=new Vector4(0,0,1,.8f);
                Action bind=()=>material.SetVectorArray("_OrbisLightning",flashes);
                Func<Color[]> sample=()=>
                {
                    bind();Graphics.Blit(Texture2D.blackTexture,target,material);
                    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,128,64),0,0);pixels.Apply();return pixels.GetPixels();
                };
                material.SetTexture("_SkyTex",Texture2D.blackTexture);float clear=Maximum(sample());
                report.Add((clear<.00001f?"PASS":"FAIL")+" clear columns exclude lightning; maximum="+clear);
                material.SetTexture("_SkyTex",Texture2D.grayTexture);float thin=Maximum(sample());
                report.Add((thin<.00001f?"PASS":"FAIL")+" thin cloud columns exclude lightning; maximum="+thin);
                material.SetTexture("_SkyTex",Texture2D.whiteTexture);var storm=sample();float peak=Maximum(storm);int lit=0;
                foreach(var pixel in storm)if(pixel.r+pixel.g+pixel.b>.01f)lit++;
                report.Add((peak>.005f&&lit>0&&lit<storm.Length/20?"PASS":"FAIL")+" storm flash stays local; maximum="+peak+" lit pixels="+lit);
                material.SetFloat("_FlatClouds",0);float disabled=Maximum(sample());
                report.Add((disabled<.00001f?"PASS":"FAIL")+" disabling clouds clears visible lightning; maximum="+disabled);
                material.SetFloat("_FlatClouds",1);Array.Clear(flashes,0,flashes.Length);float idle=Maximum(sample());
                report.Add((idle<.00001f?"PASS":"FAIL")+" no events produces no spontaneous grid flashes; maximum="+idle);
                ValidateVolumeLighting(report);
                File.WriteAllLines("Documentation/LocalPlanetUpgrade/lightning-regression.txt",report);
                if(report.Exists(line=>line.StartsWith("FAIL")))throw new InvalidOperationException(string.Join("\n",report));
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(material);
            }
        }

        static void ValidateVolumeLighting(List<string> report)
        {
            var volume = new Material(Resources.Load<Shader>("ORBIS/Clouds"));
            var probe = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/CloudPassageProbe.shader"));
            var weather = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var image = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var target = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                if (volume.passCount != 4) throw new InvalidOperationException("Volume cloud shader requires four rendering passes");
                for (int pass = 0; pass < volume.passCount; pass++) ShaderUtil.CompilePass(volume, pass, true);
                foreach (var message in ShaderUtil.GetShaderMessages(volume.shader))
                    if (message.severity.ToString() == "Error" || message.message.IndexOf("uninitialized", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new InvalidOperationException("Volume cloud pass " + message.severity + " " + message.message);
                report.Add("PASS all four volume cloud passes compile without errors or uninitialized values");
                ShaderUtil.CompilePass(probe, 0, true);
                probe.SetFloat("_ProbeLightning", 1);
                probe.SetFloat("_Clouds", 1);
                probe.SetFloat("_ScrollCloudSource", 1);
                probe.SetTexture("_ScrollWeatherTex", weather);
                var flashes = new Vector4[4];
                Action<Color> setWeather = color => { weather.SetPixel(0, 0, color); weather.Apply(); };
                Func<Vector3, Color> sample = position =>
                {
                    probe.SetVector("_ProbePosition", position);
                    probe.SetVectorArray("_OrbisLightning", flashes);
                    Graphics.Blit(Texture2D.blackTexture, target, probe);
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); image.Apply();
                    return image.GetPixel(0, 0);
                };
                setWeather(Color.white);
                CheckVolumeLight(report, sample(Vector3.forward), 0, "idle volume cloud emits no lightning");
                flashes[0] = new Vector4(0, 0, 1, .8f);
                setWeather(Color.clear);
                CheckVolumeLight(report, sample(Vector3.forward), 0, "clear volume cloud excludes lightning");
                setWeather(new Color(.5f, 0, 0, 1));
                CheckVolumeLight(report, sample(Vector3.forward), 0, "thin volume cloud excludes lightning");
                setWeather(new Color(1, 0, 0, 0));
                CheckVolumeLight(report, sample(Vector3.forward), 0, "nonstorm volume cloud excludes lightning");
                setWeather(Color.white);
                CheckVolumeLight(report, sample(Vector3.forward), .8f, "storm volume cloud retains its flash radiance");
                CheckVolumeLight(report, sample(Vector3.right), 0, "volume lightning remains localized to the strike");
                setWeather(new Color(.8f, 0, 0, .6f));
                float strength = .8f * .6f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, .92f, .8f));
                CheckVolumeLight(report, sample(Vector3.forward), strength, "volume flash follows continuous storm coverage");
                probe.SetFloat("_ScrollCloudSource", 0); setWeather(Color.clear);
                CheckVolumeLight(report, sample(Vector3.forward), .8f, "layered volume weather retains its flash radiance");
                probe.SetFloat("_ScrollCloudSource", 1); setWeather(Color.white);
                for (int i = 0; i < flashes.Length; i++) flashes[i] = new Vector4(0, 0, 1, 2);
                CheckVolumeLight(report, sample(Vector3.forward), 8, "simultaneous volume strikes remain finite in HDR");
                probe.SetFloat("_Clouds", 0);
                CheckVolumeLight(report, sample(Vector3.forward), 0, "disabled volume clouds emit no lightning");
                probe.SetFloat("_Clouds", 1); Array.Clear(flashes, 0, flashes.Length);
                flashes[0] = new Vector4(0, 0, 1, -.8f);
                CheckVolumeLight(report, sample(Vector3.forward), 0, "inactive volume strike slots produce no radiance");
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                foreach (var resource in new UnityEngine.Object[] { volume, probe, weather, image })
                    UnityEngine.Object.DestroyImmediate(resource);
            }
        }

        static void CheckVolumeLight(List<string> report, Color actual, float strength, string description)
        {
            float error = Mathf.Max(Mathf.Abs(actual.r - strength * .60f), Mathf.Abs(actual.g - strength * .72f), Mathf.Abs(actual.b - strength));
            bool passed = !float.IsNaN(error) && !float.IsInfinity(error) && error < .00005f;
            report.Add((passed ? "PASS " : "FAIL ") + description + ", maximum error=" + error);
            if (!passed) throw new InvalidOperationException(description + ", maximum error=" + error);
        }

        static float Maximum(Color[] pixels)
        {
            float value=0;
            foreach(var pixel in pixels)value=Mathf.Max(value,pixel.r,pixel.g,pixel.b);
            return value;
        }
    }
}
