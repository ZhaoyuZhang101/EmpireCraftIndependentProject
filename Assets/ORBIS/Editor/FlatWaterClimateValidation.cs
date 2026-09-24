using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Orbis.Flat;
namespace Orbis.Editor
{
    public static class FlatWaterClimateValidation
    {
        [Serializable] sealed class Report
        {
            public string utc,unityVersion,graphicsAPI,device,status="STARTED",message;
            public string scope="Actual managed core tests, native UXML import/clone, current graphics API default shader passes. Not Play Mode/visual/FPS acceptance.";
            public int assertions,compiledPasses;public bool csharpExecuted,nativeShadersExecuted,uxmlImported;
        }
        [MenuItem("Tools/ORBIS/Perf3.1/Run water climate and current-API validation")]
        public static void Validate()
        {
            var report=new Report{utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,graphicsAPI=SystemInfo.graphicsDeviceType.ToString(),device=SystemInfo.graphicsDeviceName};
            bool old=ShaderUtil.allowAsyncCompilation;
            try
            {
                report.csharpExecuted=true;report.message=FlatSelfTests.Run();report.assertions=FlatSelfTests.Assertions;
                const string ui="Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatWorld.uxml";
                AssetDatabase.ImportAsset(ui,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                var tree=AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ui);if(!tree)throw new InvalidOperationException("UXML failed to import.");
                var root=tree.CloneTree();foreach(string name in new[]{"World","Terrain","Water","Climate","Civs","Files"})
                {if(root.Q<Button>("tab"+name)==null||root.Q("panel"+name)==null)throw new InvalidOperationException("Missing tab/panel "+name);}
                report.uxmlImported=true;
                if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)throw new InvalidOperationException("No graphics API. Re-run WITHOUT -nographics; on Windows select D3D11.");
                ShaderUtil.allowAsyncCompilation=false;
                foreach(string path in new[]{"Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatMap.shader","Assets/ORBIS/Resources/ORBIS/Surface.shader","Assets/ORBIS/Resources/ORBIS/FlatWorld/Route.shader"})
                {
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                    Shader shader=AssetDatabase.LoadAssetAtPath<Shader>(path);if(!shader)throw new InvalidOperationException("Missing shader "+path);
                    var material=new Material(shader);
                    try
                    {
                        for(int pass=0;pass<material.passCount;pass++)
                        {ShaderUtil.CompilePass(material,pass,true);if(!ShaderUtil.IsPassCompiled(material,pass))throw new InvalidOperationException("Uncompiled pass "+path+" / "+pass);report.compiledPasses++;}
                        if(ShaderUtil.ShaderHasError(shader)||!shader.isSupported)throw new InvalidOperationException("Shader compilation failed: "+path+". See Editor.log and Console.");
                    }
                    finally{UnityEngine.Object.DestroyImmediate(material);}
                }
                report.nativeShadersExecuted=true;report.status="PASS";
                Debug.Log("Perf3.1 validation PASS on "+report.graphicsAPI+". Default passes only; visually inspect Play Mode separately.");
            }
            catch(Exception e){report.status="FAIL";report.message=e.ToString();Debug.LogException(e);}
            finally
            {
                ShaderUtil.allowAsyncCompilation=old;
                string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Validation/WaterClimate"));Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"unity-native-report.json"),JsonUtility.ToJson(report,true));
            }
            if(report.status!="PASS")throw new InvalidOperationException("Perf3.1 native validation failed. Read Validation/WaterClimate/unity-native-report.json");
        }
        public static void RunD3D11Batch()
        {
            try{Validate();if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11)throw new InvalidOperationException("This batch entry requires Direct3D11; pass -force-d3d11 on Windows.");EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
