#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Orbis.Flat;
using Orbis.Planet;

namespace Orbis.EditorTools
{
    // Real Editor regression for event-after-Update commits and the return threshold.
    public static class FlatSeamValidation
    {
        [Serializable] sealed class Report
        {
            public string status,device,unityVersion;
            public bool visualFlickerVerified=false;
            public List<string> checks=new List<string>();
        }
        static Report report;
        static FlatWorldApp app;static FlatScrollNavigator nav;static Camera camera;
        static FieldInfo pauseField;static bool savedPause;
        static int phase,target,settled;static double deadline;static float threshold;
        [MenuItem("Tools/ORBIS/Perf3.3/5 Verify seam commit and return scale in Play Mode")]
        public static void Run()
        {
            if(report!=null&&report.status=="RUNNING"){Debug.LogWarning("Seam validation is already running.");return;}
            if(!EditorApplication.isPlaying){Debug.LogWarning("Enter Play Mode in Orbis2DWorld.unity first.");return;}
            app=UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if(!app||app.World==null||app.Busy||!app.Navigation){Debug.LogWarning("Wait for world generation.");return;}
            nav=app.Navigation;camera=app.GetComponentInChildren<Camera>();
            pauseField=typeof(FlatWorldApp).GetField("paused",BindingFlags.Instance|BindingFlags.NonPublic);
            savedPause=(bool)pauseField.GetValue(app);pauseField.SetValue(app,true);
            report=new Report{status="RUNNING",device=SystemInfo.graphicsDeviceName,unityVersion=Application.unityVersion};
            target=app.World.Height/2*app.World.Width+app.World.Width-1;
            phase=settled=0;deadline=EditorApplication.timeSinceStartup+90;
            nav.GoGlobe(app.CalendarDay);EditorApplication.update+=Step;
        }
        static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
        static void CheckCommit()
        {
            // Read immediately after PanBy, before another application Update/SyncMaterials.
            Vector4 anchor=nav.SurfaceMaterial.GetVector("_ScrollAnchor"),bound=nav.SurfaceMaterial.GetVector("_ScrollPan");
            double expected=anchor.x*app.World.Width+PlanetCoordinateSystem.NearestOffset(nav.CenterTile.x-anchor.x*app.World.Width,app.World.Width);
            Require(Math.Abs(bound.x-expected)<.002&&Math.Abs(bound.y-nav.CenterTile.y)<.002,"PanBy published stale shader coordinates");
            int visible=0;
            float half=camera.pixelWidth/Mathf.Max(.1f,nav.PixelsPerTile)*.5f;
            foreach(var renderer in nav.GetComponentsInChildren<MeshRenderer>())
            {
                if(!renderer.name.StartsWith("Shared terrain sheet ",StringComparison.Ordinal))continue;
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);float copy=block.GetFloat("_ScrollCopy");
                float left=(anchor.x-.5f+copy)*app.World.Width,right=left+app.World.Width;
                bool expectedVisible=right>=bound.x-half-1&&left<=bound.x+half+1;
                Require(renderer.enabled==expectedVisible,"Copy visibility disagrees with bound projection");
                if(renderer.enabled)visible++;
            }
            Require(visible==nav.ActiveSheets&&visible>0,"Missing visible terrain sheets");
        }
        static void Step()
        {
            try
            {
                Require(EditorApplication.isPlaying&&app&&nav,"Play Mode or world ended");
                Require(EditorApplication.timeSinceStartup<deadline,"Seam/return regression timed out");
                int w=app.World.Width;
                if(phase==0&&nav.IsGlobe){nav.Focus(target,app.CalendarDay);phase=1;}
                else if(phase==1&&nav.IsFlat&&Mathf.Abs(nav.PixelsPerTile-48)<.01f&&++settled>3)
                {
                    foreach(float move in new[]{.51f,-.02f,.02f,w*3f,-w*7f}){nav.PanBy(new Vector2(move,0));CheckCommit();}
                    float cut=(nav.SurfaceMaterial.GetVector("_ScrollAnchor").x+.5f)*w;
                    nav.PanBy(new Vector2((float)PlanetCoordinateSystem.NearestOffset(cut-nav.CenterTile.x,w)-.02f,0));CheckCommit();
                    nav.PanBy(new Vector2(.04f,0));CheckCommit();
                    report.checks.Add("Immediate material/copy agreement across longitude zero, moving sheet cut, and repeated laps");
                    target=nav.Pick(camera.pixelRect.center);threshold=nav.ReturnPixelsPerTile;
                    nav.Wheel(24,camera.pixelRect.center,app.CalendarDay);
                    // A second notch can be needed for unusually small landing scales.
                    nav.Wheel(24,camera.pixelRect.center,app.CalendarDay);phase=2;
                }
                else if(phase==2)
                {
                    if(nav.IsFlat)Require(nav.PixelsPerTile>=threshold*.999f,"Wheel zoomed below the landing threshold");
                    if(nav.IsGlobe)
                    {
                        Require(nav.Pick(camera.pixelRect.center)==target,"Wheel return changed focused Tile ID");
                        report.checks.Add("Wheel returned at landing scale and preserved current centre Tile "+target);
                        nav.Focus(target,app.CalendarDay);phase=3;settled=0;
                    }
                }
                else if(phase==3&&nav.IsFlat&&Mathf.Abs(nav.PixelsPerTile-48)<.01f&&++settled>3)
                {
                    float pixels=nav.PixelsPerTile,size=camera.orthographicSize;
                    nav.GoGlobe(app.CalendarDay);
                    Require(Mathf.Abs(nav.PixelsPerTile-pixels)<.001f&&Mathf.Abs(camera.orthographicSize-size)<.00001f,"Direct return changed the detailed-view scale");
                    CheckCommit();phase=4;
                }
                else if(phase==4&&nav.IsGlobe)
                {
                    Require(nav.Pick(camera.pixelRect.center)==target,"Direct return changed focused Tile ID");
                    report.checks.Add("Direct return folds from the detailed view without an atlas-fit step");
                    report.status="PASS";Finish();
                }
            }
            catch(Exception e){report.status="FAIL";report.checks.Add(e.ToString());Finish();}
        }
        static void Finish()
        {
            EditorApplication.update-=Step;
            if(app&&pauseField!=null)pauseField.SetValue(app,savedPause);
            string dir=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Validation","Perf3_3_3");
            Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"seam-return-unity-report.json"),JsonUtility.ToJson(report,true));
            if(report.status=="PASS")Debug.Log("ORBIS seam/return checks PASS. Inspect moving imagery separately; view remains at the test location.");
            else Debug.LogError("ORBIS seam/return checks FAIL: "+string.Join("\n",report.checks));
        }
    }
}
#endif
