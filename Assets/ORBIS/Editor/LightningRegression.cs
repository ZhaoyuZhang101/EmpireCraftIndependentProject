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
                File.WriteAllLines("Documentation/LocalPlanetUpgrade/lightning-regression.txt",report);
                if(report.Exists(line=>line.StartsWith("FAIL")))throw new InvalidOperationException(string.Join("\n",report));
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(material);
            }
        }
        static float Maximum(Color[] pixels)
        {
            float value=0;
            foreach(var pixel in pixels)value=Mathf.Max(value,pixel.r,pixel.g,pixel.b);
            return value;
        }
    }
}
