using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.Editor
{
    /// <summary>Explicitly compiles each default-material pass for the CURRENT Editor graphics API.
    /// This is not a cross-platform build or visual/performance test.</summary>
    public static class OrbisV42ShaderValidation
    {
        [Serializable] sealed class PassResult
        {
            public int index;
            public string name;
            public bool requested, compiled;
            public string exception;
        }
        [Serializable] sealed class ShaderResult
        {
            public string name, path;
            public bool loaded, supported, hasErrors, passed;
            public int expectedPasses, actualPasses;
            public string exception;
            public string[] messages;
            public PassResult[] passes;
        }
        [Serializable] sealed class Report
        {
            public string version="4.2", timeUtc, unityVersion, graphicsAPI, graphicsDevice, colorSpace;
            public bool builtIn, graphicsAvailable, allRequestedPassesPassed;
            public bool playModeTestPerformed=false, visualAcceptancePerformed=false, crossPlatformBuildPerformed=false;
            public string scope="Synchronous compilation of default-material passes on the CURRENT Editor graphics API and color space only. Runtime uniform values are NOT separate shader variants. No scene or project settings are modified.";
            public ShaderResult[] shaders;
        }

        [MenuItem("Tools/ORBIS/V4.2/強制編譯雲與大氣並輸出報告")]
        public static void Validate() { Run(); }

        // Example: Unity -batchmode -projectPath <project> -executeMethod
        // Orbis.Editor.OrbisV42ShaderValidation.ValidateBatch -logFile <log>
        // Do NOT pass -nographics: a real graphics backend is required by this check.
        public static void ValidateBatch()
        {
            bool passed=Run();
            if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
        }

        static bool Run()
        {
            var report=new Report
            {
                timeUtc=DateTime.UtcNow.ToString("O"), unityVersion=Application.unityVersion,
                graphicsAPI=SystemInfo.graphicsDeviceType.ToString(), graphicsDevice=SystemInfo.graphicsDeviceName,
                colorSpace=QualitySettings.activeColorSpace.ToString(),
                builtIn=GraphicsSettings.currentRenderPipeline==null,
                graphicsAvailable=SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null
            };
            string[] names={"Clouds","CloudShadowCache","AtmosphereComposite","Display","Surface","RiverWater","WeatherParticles"};
            int[] counts={3,1,1,1,2,1,1};
            var results=new List<ShaderResult>();
            bool passed=report.builtIn&&report.graphicsAvailable;
            bool oldAsync=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                for(int n=0;n<names.Length;n++)
                {
                    var item=new ShaderResult{name="ORBIS/"+names[n],
                        path="Assets/ORBIS/Resources/ORBIS/"+names[n]+".shader",expectedPasses=counts[n]};
                    var passes=new List<PassResult>();var messages=new List<string>();Material material=null;
                    try
                    {
                        AssetDatabase.ImportAsset(item.path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                        Shader shader=AssetDatabase.LoadAssetAtPath<Shader>(item.path);
                        item.loaded=shader!=null;
                        if(shader!=null)
                        {
                            item.actualPasses=shader.passCount;
                            if(report.graphicsAvailable)
                            {
                                material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
                                for(int i=0;i<material.passCount;i++)
                                {
                                    var pass=new PassResult{index=i,name=material.GetPassName(i)};
                                    try
                                    {
                                        pass.requested=true;
                                        ShaderUtil.CompilePass(material,i,true);
                                        pass.compiled=ShaderUtil.IsPassCompiled(material,i);
                                    }
                                    catch(Exception e){pass.exception=e.ToString();}
                                    passes.Add(pass);
                                }
                            }
                            else messages.Add("SKIPPED: no graphics backend. This is NOT a successful shader compilation check.");
                            item.hasErrors=ShaderUtil.ShaderHasError(shader);
                            item.supported=report.graphicsAvailable&&shader.isSupported;
                            foreach(var m in ShaderUtil.GetShaderMessages(shader))
                                messages.Add(m.severity+": "+m.file+":"+m.line+" "+m.message);
                        }
                        else messages.Add("FAILED: shader asset could not be loaded.");
                        bool everyPass=passes.Count>=item.expectedPasses;
                        foreach(var p in passes)everyPass&=p.requested&&p.compiled&&string.IsNullOrEmpty(p.exception);
                        item.passed=item.loaded&&item.supported&&!item.hasErrors&&everyPass;
                    }
                    catch(Exception e){item.exception=e.ToString();item.passed=false;}
                    finally
                    {
                        if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                        item.messages=messages.ToArray();item.passes=passes.ToArray();results.Add(item);
                        passed&=item.passed;
                    }
                }
            }
            finally { ShaderUtil.allowAsyncCompilation=oldAsync; }
            report.shaders=results.ToArray();report.allRequestedPassesPassed=passed;
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Documentation"));
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"Unity-V4_2-shader-report.json");
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(passed)Debug.Log("ORBIS V4.2：当前图形 API 的指定 Shader Pass 编译通过；不是跨平台或画面验收。报告："+path);
            else Debug.LogError("ORBIS V4.2：Shader 编译检查未通过或没有可用图形后端。报告："+path);
            return passed;
        }
    }
}
