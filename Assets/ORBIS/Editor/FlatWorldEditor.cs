using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Orbis.Flat;
namespace Orbis.Editor
{
    public static class FlatWorldEditor
    {
        [Serializable] sealed class CoreReport
        {public string utc,unityVersion,status,scope="Executed core C# tests only; native shader, visual and FPS acceptance are separate.",lastOperation,message;public int assertions;public bool passed;}
        [MenuItem("Tools/ORBIS/2D World/1 Open 2D main scene")]
        public static void OpenMain()
        {if(EditorApplication.isPlaying){Debug.LogWarning("Leave Play Mode before changing scenes.");return;}if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");}
        [MenuItem("Tools/ORBIS/2D World/2 Run core data validation")]
        public static void ValidateCore()
        {
            var report=new CoreReport{utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,status="STARTED"};
            try{report.message=FlatSelfTests.Run();report.passed=true;report.status="PASS";Debug.Log(report.message);}
            catch(Exception e){report.message=e.ToString();report.status="FAIL";Debug.LogException(e);}
            report.assertions=FlatSelfTests.Assertions;report.lastOperation=FlatSelfTests.LastOperation;
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../Validation/FlatWorld/unity-core-report.json"));Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(!report.passed)throw new InvalidOperationException("2D core validation failed. "+path);
        }
        public static void ValidateCoreBatch(){ValidateCore();}
        [MenuItem("Tools/ORBIS/2D World/3 Compile current-API map and globe shaders")]
        public static void ValidateShaders()
        {
            OrbisV42ShaderValidation.Validate();
            string[] paths={"Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatMap.shader","Assets/ORBIS/Resources/ORBIS/FlatWorld/Route.shader"};
            bool old=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                foreach(string path in paths)
                {
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if(!shader)throw new InvalidOperationException("Missing shader "+path);var material=new Material(shader);
                    try{for(int p=0;p<material.passCount;p++){ShaderUtil.CompilePass(material,p,true);if(!ShaderUtil.IsPassCompiled(material,p))throw new InvalidOperationException("Pass not compiled: "+path+" / "+p);}if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shader has errors: "+path);}
                    finally{UnityEngine.Object.DestroyImmediate(material);}
                }
                Debug.Log("New map/route default passes compiled on the CURRENT graphics API. Inspect the separate baseline globe shader report as well. No visual or cross-platform acceptance implied.");
            }
            finally{ShaderUtil.allowAsyncCompilation=old;}
        }
    }
}
