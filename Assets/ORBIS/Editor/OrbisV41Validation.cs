using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Editor
{
    public static class OrbisV41Validation
    {
        [Serializable] sealed class Report
        {
            public string unityVersion,device,colorSpace,timeUtc;
            public bool builtIn,graphicsAvailable,shaderResourceChecksPassed,playMode;
            public bool visualAcceptancePerformed=false;
            public string note="Resource/import checks only; this command does not perform visual or GPU performance acceptance.";
            public string[] results;
        }
        [MenuItem("Tools/ORBIS/V4.1/驗證光照資源並輸出報告")]
        public static void Validate()
        {
            var messages=new List<string>();bool passed=true;
            foreach(string name in new[]{"Surface","Clouds","RiverWater","AtmosphereComposite","CloudShadowCache","Display","Transport"})
            {
                Shader shader=Resources.Load<Shader>("ORBIS/"+name);
                bool ok=shader&&!ShaderUtil.ShaderHasError(shader);
                if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null)ok=ok&&shader&&shader.isSupported;
                if(shader&&name=="Clouds")ok=ok&&shader.passCount>=3;
                messages.Add(name+": "+(ok?"resource/pass check OK":"FAILED"));passed&=ok;
                if(shader)foreach(var error in ShaderUtil.GetShaderMessages(shader))messages.Add(name+" "+error.severity+": "+error.message);
            }
            var external=Resources.Load<Texture3D>("ORBIS/CloudNoiseExternal");
            messages.Add(external?"Noise: external Texture3D "+external.width+" x "+external.height+" x "+external.depth:"Noise: internal periodic 32^3 fallback (expected when no external slices imported)");
            var demo=UnityEngine.Object.FindFirstObjectByType<OrbisDemo>();
            if(Application.isPlaying&&demo&&demo.View)
            {
                var p=demo.World.settings;int snow=0;
                foreach(var tile in demo.World.tiles)if(tile.h>0&&tile.snow>=30)snow++;
                messages.Add("Land at full snow palette: "+snow+" / "+demo.World.LandCount);
                messages.Add("Exposure="+p.exposure+", surfaceDebug="+p.surfaceDebug+", mapClouds="+p.mapClouds+", godRays="+p.godRays);
                messages.Add("Shadow atlas ready="+Shader.GetGlobalFloat("_OrbisShadowReady"));
                messages.Add("Camera HDR="+demo.PlanetCamera.allowHDR+", orthographic="+demo.PlanetCamera.orthographic);
                messages.Add("Scene appearance still requires human inspection.");
            }
            var report=new Report{unityVersion=Application.unityVersion,device=SystemInfo.graphicsDeviceType.ToString(),
                colorSpace=QualitySettings.activeColorSpace.ToString(),timeUtc=DateTime.UtcNow.ToString("O"),
                builtIn=GraphicsSettings.currentRenderPipeline==null,graphicsAvailable=SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,
                shaderResourceChecksPassed=passed,playMode=Application.isPlaying,results=messages.ToArray()};
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Documentation"));Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"Unity-V4_1-native-report.json");File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(!passed||!report.builtIn)Debug.LogError("ORBIS V4.1 資源檢查存在問題："+path);
            else Debug.Log("ORBIS V4.1 資源檢查完成（非畫面驗收）："+path);
        }
    }
}
