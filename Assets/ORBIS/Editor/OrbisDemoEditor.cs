using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Orbis.Editor
{
    [InitializeOnLoad]
    public static class OrbisDemoEditor
    {
        const string ScenePath="Assets/ORBIS/Scenes/OrbisDemo.unity";
        const string MainScenePath="Assets/ORBIS/Scenes/Orbis2DWorld.unity";
        static OrbisDemoEditor(){EditorApplication.delayCall+=FirstOpen;}
        static void FirstOpen()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=FirstOpen;return;}
            if(EditorPrefs.GetBool("ORBIS.Initialized.TerrainPerf3."+Application.dataPath))return;
            // In this dedicated demo project only, open the prepared scene after first import.
            string marker=Path.Combine(Application.dataPath,"../ORBIS_DEMO_PROJECT.txt");if(!File.Exists(marker))return;
            var active=SceneManager.GetActiveScene();if(active.isDirty)return;
            EditorPrefs.SetBool("ORBIS.Initialized.TerrainPerf3."+Application.dataPath,true);
            if(string.IsNullOrEmpty(active.path))EditorSceneManager.OpenScene(MainScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(MainScenePath,true),new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.companyName="ORBIS";PlayerSettings.productName="ORBIS 2D World";
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.runInBackground=true;PlayerSettings.colorSpace=ColorSpace.Linear;
            Debug.Log("ORBIS 專案已就緒。当前主场景是 2D 世界。切到 Game 并按 Play；Tools → ORBIS → 2D World 提供验证与构建菜单。");
        }
        [MenuItem("Tools/ORBIS/旧版球体回归/打开 TerrainPerf2 场景")]
        public static void OpenDemo(){if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);}
        [MenuItem("Tools/ORBIS/2D World/4 Build Windows player")]
        public static void BuildWindows()
        {
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,BuildTarget.StandaloneWindows64)){EditorUtility.DisplayDialog("需要 Windows 建置模組","請在 Unity Hub 為目前的 Editor 安裝 Windows Build Support 後重試。","知道了");return;}
            string folder=EditorUtility.OpenFolderPanel("建置 ORBIS Windows 示範版","","");if(string.IsNullOrEmpty(folder))return;
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{MainScenePath,ScenePath},locationPathName=Path.Combine(folder,"ORBIS.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            if(report.summary.result==BuildResult.Succeeded)EditorUtility.RevealInFinder(Path.Combine(folder,"ORBIS.exe"));else Debug.LogError("ORBIS 建置失敗："+report.summary.result);
        }
        [MenuItem("Tools/ORBIS/旧版球体回归/验证旧世界模拟")]
        public static void Validate()
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();int checks=0;CheckRoutes();checks++;
            foreach(string seed in new[]{"ORBIS-VI","BOYANG","RIVER-TEST"}){
                var w=new OrbisWorld(seed);Require(w.tiles.Length==OrbisWorld.TileCount,"Quad tile count");checks++;
                for(int t=0;t<w.tiles.Length;t++){Require(w.grid.neighbors[t].Length==4,"Four neighbors per tile");Require(w.grid.tileCorners[t].Length==4,"Four corners per tile");}checks++;
                Require(w.LandCount>w.tiles.Length*.32f&&w.LandCount<w.tiles.Length*.40f,"Land fraction");checks++;
                CheckAtmosphere(w);checks++;
                CheckDrainage(w);checks++;
                w.geography.Build(w);w.geography.Validate(w);checks++;
                var same=new OrbisWorld(seed);for(int i=0;i<w.tiles.Length;i++)Require(w.tiles[i].h==same.tiles[i].h,"Seed determinism");checks++;
                w.SeedFour();for(int i=0;i<1800;i++)w.Step(.2f,2);
                foreach(var c in w.tiles){Require(!float.IsNaN(c.Temperature)&&!float.IsInfinity(c.Temperature)&&c.Temperature>-160&&c.Temperature<140,"Finite bounded climate");Require(c.soil>=0&&c.soil<=1,"Soil bounds");Require(c.owner>=-1&&c.owner<w.civs.Count,"Owner bounds");}checks++;
                CheckDrainage(w);checks++;
                w.geography.Build(w);w.geography.Validate(w);checks++;
                string json=JsonUtility.ToJson(w.Save());var restored=OrbisWorld.Restore(JsonUtility.FromJson<WorldSave>(json));Require(Mathf.Abs(restored.Population-w.Population)<.01f&&restored.day==w.day&&restored.rng.state==w.rng.state,"Save roundtrip");checks++;
                w.Step(.2f,2);restored.Step(.2f,2);for(int i=0;i<w.tiles.Length;i++)Require(Mathf.Abs(w.tiles[i].Temperature-restored.tiles[i].Temperature)<.001f&&w.tiles[i].owner==restored.tiles[i].owner,"Save continuation");checks++;
                foreach(var c in w.tiles)c.h=.2f;w.BuildHydrology();CheckDrainage(w);checks++;
                w.geography.Build(w);w.geography.Validate(w);checks++;
                foreach(var c in w.tiles)c.h=-.2f;w.BuildHydrology();CheckDrainage(w);checks++;
                w.geography.Build(w);w.geography.Validate(w);checks++;
            }
            Debug.Log("ORBIS: "+checks+" validation groups passed in "+watch.Elapsed.TotalSeconds.ToString("F1")+"s. Graphics and input still require Play Mode verification.");
        }
        static void CheckRoutes()
        {
            var w=new OrbisWorld("ROUTE-CHECK");int home=0,goal=-1;
            for(int i=0;i<w.tiles.Length;i++){w.tiles[i].h=-.2f;w.tiles[i].lake=0;if(Vector3.Dot(w.grid.points[home],w.grid.points[i])<-.98f)goal=i;}
            Require(goal>=0,"Two separated islands exist");
            foreach(int id in new[]{home,goal}){w.tiles[id].h=.2f;w.tiles[id].soil=.8f;w.tiles[id].temp=20;}
            w.BuildHydrology();w.geography.Build(w);w.RefreshNames();int civ=w.Spawn(home);
            Require(w.FindRoute(home,goal,civ,false)==null,"Pre-sailing civilization cannot cross the ocean");
            w.civs[civ].tech=2;int[] route=w.FindRoute(home,goal,civ,false);
            Require(route!=null&&route.Length>2,"Sailing unlocks a continuous sea route");
            for(int i=1;i<route.Length;i++)Require(Array.IndexOf(w.grid.neighbors[route[i-1]],route[i])>=0,"Route uses adjacent tiles");
            float before=w.Population;Require(w.Dispatch(home,true,goal),"Settler dispatch succeeds");
            Require(Mathf.Abs(w.Population-before)<.01f,"Population is conserved when dispatching");
            var restored=OrbisWorld.Restore(JsonUtility.FromJson<WorldSave>(JsonUtility.ToJson(w.Save())));
            Require(restored.expeditions.Count==1&&restored.expeditions[0].path.Length==route.Length,"In-flight route survives save/load");
            var legacy=new OrbisWorld("LEGACY-CHECK").Save();legacy.version=1;legacy.settings.cloudCover=0;
            var migrated=OrbisWorld.Restore(JsonUtility.FromJson<WorldSave>(JsonUtility.ToJson(legacy)));
            Require(migrated.settings.cloudCover==1&&migrated.settings.atmosphere,"Version one settings migrate");
        }
        static void CheckAtmosphere(OrbisWorld w)
        {
            foreach(float angle in new[]{0f,.1f,Mathf.PI,Mathf.PI*2-.01f})
            {
                Vector3 sun=Quaternion.Euler(0,angle*Mathf.Rad2Deg,0)*w.SunAt(w.day,angle);
                Require(Mathf.Abs(sun.x)<.0001f&&Mathf.Abs(sun.magnitude-1)<.0001f,"Sun and planet share a continuous frame");
            }
            var drift=new Vector3[w.tiles.Length];for(int i=0;i<drift.Length;i++)drift[i]=w.tiles[i].cloudDrift;
            float oldWind=w.settings.windPower;w.settings.windPower=0;w.Step(.2f,.4f);
            for(int i=0;i<drift.Length;i++)Require(w.tiles[i].wind.sqrMagnitude<1e-8f&&(w.tiles[i].cloudDrift-drift[i]).sqrMagnitude<1e-8f,"Zero wind stops cloud advection");
            w.settings.windPower=oldWind;w.Step(.2f,.4f);
            for(int i=0;i<w.tiles.Length;i++)Require(Mathf.Abs(Vector3.Dot(w.grid.points[i],w.tiles[i].wind))<.0001f&&w.tiles[i].wind.magnitude<=3.001f,"Wind remains tangent and bounded");
        }
        static void Require(bool ok,string message){if(!ok)throw new Exception("ORBIS validation failed: "+message);}
        static void CheckDrainage(OrbisWorld w)
        {
            foreach(int start in System.Linq.Enumerable.Range(0,w.tiles.Length)){int id=start;for(int n=0;n<=w.tiles.Length;n++){int next=w.tiles[id].downstream;if(next<0)break;Require(next<w.tiles.Length&&w.tiles[next].flood<w.tiles[id].flood,"Strictly downhill drainage");id=next;Require(n<w.tiles.Length,"No drainage cycles");}}
        }
    }
}
