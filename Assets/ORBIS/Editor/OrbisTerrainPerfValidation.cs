using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Orbis.Editor
{
    // Explicitly invoked; never changes the user's active world, scene, or render pipeline.
    public static class OrbisTerrainPerfValidation
    {
        [Serializable] sealed class Report
        {
            public string version="V4.2 GreenRingFix TerrainPerf1",utc,unityVersion,state="STARTED",operation,error;
            public string scope="Production C# data checks in this Editor. Does not measure FPS, validate native shaders, or perform visual acceptance.";
            public bool passed,shaderCompilePerformed=false,visualAcceptancePerformed=false;public int assertions;public double elapsedMilliseconds;
        }
        static Report report;static string path;
        static void Check(bool condition,string message){report.assertions++;if(!condition)throw new InvalidOperationException(message);}
        static void Checkpoint(string operation){report.operation=operation;Write();}
        static void Write(){File.WriteAllText(path,JsonUtility.ToJson(report,true));}
        [MenuItem("Tools/ORBIS/TerrainPerf1/1 Run isolated terrain data checks")]
        public static void Validate(){Run();}
        public static void ValidateBatch(){if(!Run())throw new InvalidOperationException("TerrainPerf1 production data validation failed; inspect "+path);}
        static bool Run()
        {
            string folder=Path.Combine(Application.persistentDataPath,"TerrainPerf1");Directory.CreateDirectory(folder);
            path=Path.Combine(folder,"production-data-validation.json");
            report=new Report{utc=DateTime.UtcNow.ToString("o"),unityVersion=Application.unityVersion};Write();
            double start=EditorApplication.timeSinceStartup;
            try
            {
                Checkpoint("Create isolated deterministic world");var world=new OrbisWorld("TerrainPerf1-VALIDATION");
                Check(world.tiles.Length==3456,"Stable authoritative tile IDs");
                Checkpoint("Topology and map coordinates");
                for(int i=0;i<world.tiles.Length;i++)
                {
                    Check(world.grid.neighbors[i].Length==4,"Each logical tile has four neighbors");
                    foreach(int j in world.grid.neighbors[i])Check(Array.IndexOf(world.grid.neighbors[j],i)>=0,"Reciprocal neighbors at cube seams");
                    Vector3 p=world.grid.points[i];Check(Vector3.Distance(p,OrbisMapView.Unproject(OrbisMapView.Project(p)))<3e-5f,"Globe/atlas position roundtrip");
                }
                Checkpoint("Cached geology repeatability");
                var original=new float[world.tiles.Length];for(int i=0;i<original.Length;i++)original[i]=world.tiles[i].h;
                world.Terrain();for(int i=0;i<original.Length;i++)Check(Mathf.Abs(original[i]-world.tiles[i].h)<1e-6f,"Unchanged geology cache must preserve height");
                Checkpoint("Brush locality, raise/lower, and invalid-input protection");
                int center=world.grid.Nearest(Vector3.up);const float radius=.12f,strength=.08f;
                int changed=world.ApplyTerrainBrush(center,3,radius,strength,0);Check(changed>0,"Raise brush changed terrain");
                for(int i=0;i<original.Length;i++)
                {
                    if(Vector3.Distance(world.grid.points[i],world.grid.points[center])>=radius)Check(world.tiles[i].edit==0,"Brush cannot cross its spherical footprint");
                    else Check(world.tiles[i].edit>=0,"Raise cannot lower terrain");
                }
                world.ApplyTerrainBrush(center,4,radius,strength,0);
                for(int i=0;i<original.Length;i++)Check(Mathf.Abs(world.tiles[i].h-original[i])<2e-6f,"Opposite unclamped brush restores terrain");
                int revision=world.Revision;bool rejected=false;
                try{world.ApplyTerrainBrush(center,6,float.NaN,strength,0);}catch(ArgumentException){rejected=true;}
                Check(rejected&&world.Revision==revision,"Reject NaN without modifying world");
                Check(world.ApplyTerrainBrush(-1,3,radius,strength,0)==0,"Invalid tile safely ignored");
                Checkpoint("Smooth and flatten, drainage and regions");
                world.ApplyTerrainBrush(center,6,radius,strength,0);world.ApplyTerrainBrush(center,7,radius,strength,.15f);
                world.geography.Validate(world);
                for(int i=0;i<world.tiles.Length;i++)
                {
                    Tile tile=world.tiles[i];Check(!float.IsNaN(tile.h)&&!float.IsInfinity(tile.h),"Finite edited heights");
                    if(tile.downstream>=0)Check(tile.flood>world.tiles[tile.downstream].flood,"Drainage is strictly descending, hence acyclic");
                }
                Checkpoint("Full-save roundtrip and older-save defaults");
                world.settings.terrainQuality=2;world.settings.terrainBrushRadius=.2f;world.settings.terrainBrushStrength=.09f;world.settings.terrainFlattenHeight=.15f;
                var save=world.Save();save.view=new DemoViewState{speed=1,altitude=1,distance=2,mapZoom=2,selected=-1,tool=7};
                string json=JsonUtility.ToJson(save);var loaded=OrbisWorld.Restore(OrbisSaveMigration.Read(json));
                Check(loaded.settings.terrainQuality==2&&Mathf.Abs(loaded.settings.terrainBrushRadius-.2f)<1e-6f,"Persist independent terrain settings");
                for(int i=0;i<world.tiles.Length;i++)Check(Mathf.Abs(world.tiles[i].edit-loaded.tiles[i].edit)<1e-6f,"Persist authoritative edits");
                string oldJson=Regex.Replace(json,"\\\"terrain(?:Quality|BrushRadius|BrushStrength|FlattenHeight)\\\"\\s*:\\s*[-+0-9.eE]+\\s*,","");
                var old=OrbisSaveMigration.Read(oldJson);Check(old.settings.terrainQuality==1&&Mathf.Abs(old.settings.terrainBrushRadius-.12f)<1e-6f,"Old JSON gets explicit defaults");
                Checkpoint("Imported-map river continuity");
                var map=OrbisMapIO.Example();var imported=new OrbisWorld("TerrainPerf1-MAP");imported.ImportWorldMap(map);
                var rivers=new OrbisRiverNetwork();rivers.Build(imported,map.Sample);
                Check(rivers.reaches.Count>0&&rivers.MaxUpliftWarnings==0,"Example map river constraints remain valid");
                foreach(var reach in rivers.reaches)Check(reach.ha+2e-6f>=reach.hb,"Authored river cannot climb downstream");
                report.state="COMPLETED";report.passed=true;report.operation="All isolated data checks completed";
            }
            catch(Exception e){report.state="FAILED";report.error=e.ToString();Debug.LogException(e);}
            finally{report.elapsedMilliseconds=(EditorApplication.timeSinceStartup-start)*1000;Write();}
            Debug.Log("[ORBIS TerrainPerf1] Data validation "+(report.passed?"PASS":"FAIL")+": "+report.assertions+" assertions. "+path);return report.passed;
        }
        [MenuItem("Tools/ORBIS/TerrainPerf1/2 Compile current-API shaders and save diagnostics")]
        public static void CompileShaders(){OrbisV42RepairIPCValidation.Validate();}
        [MenuItem("Tools/ORBIS/TerrainPerf1/3 Inspect running terrain (read only)")]
        public static void InspectRunningTerrain()
        {
            if(!Application.isPlaying){Debug.LogWarning("Enter Play Mode in OrbisDemo before inspecting the running terrain.");return;}
            var demo=UnityEngine.Object.FindFirstObjectByType<OrbisDemo>();
            if(!demo||!demo.View){Debug.LogWarning("No initialized OrbisDemo in the active loaded scenes.");return;}
            if(demo.View.SurfaceVertexCount!=221058||demo.View.RenderPatchCount!=96)
                Debug.LogError("TerrainPerf1 topology counters differ from the shipped 3456-tile/detail-8 configuration. Check source consistency.");
            Debug.Log("[ORBIS TerrainPerf1] Read-only runtime counters:\n"+demo.PerformanceStatus()+"\nVisual/shader acceptance is NOT implied.");
        }
    }
}
