#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Orbis.Flat;
namespace Orbis.EditorTools
{
    /// <summary>Run on the real Unity camera to validate focus after a spherical landing.</summary>
    public static class FlatVisualRepairValidation
    {
        [Serializable] sealed class Report
        {
            public string status,unityVersion,device,scope;
            public List<string> checks=new List<string>();
            public bool visualQualityVerified=false;
        }
        static FlatWorldApp app; static FlatScrollNavigator nav; static Camera camera;
        static FieldInfo pauseField; static bool savedPause;
        static int phase,index,settled; static int[] targets;
        static double deadline; static Report report;
        [MenuItem("Tools/ORBIS/Perf3.3/4 Verify repaired globe focus in Play Mode")]
        public static void Run()
        {
            if(!EditorApplication.isPlaying){Debug.LogWarning("Enter Play Mode in Orbis2DWorld.unity first.");return;}
            app=UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if(!app||app.World==null||app.Busy||!app.Navigation){Debug.LogWarning("Wait for world generation to finish.");return;}
            if(GraphicsSettings.currentRenderPipeline!=null){Debug.LogError("This project requires Built-in Render Pipeline.");return;}
            EditorApplication.update-=Step;
            nav=app.Navigation;camera=app.GetComponentInChildren<Camera>();
            pauseField=typeof(FlatWorldApp).GetField("paused",BindingFlags.Instance|BindingFlags.NonPublic);
            savedPause=(bool)pauseField.GetValue(app);pauseField.SetValue(app,true);
            int w=app.World.Width,h=app.World.Height;
            targets=new[]{h/5*w+w/3,(h*4/5)*w+w-1,(h-2)*w+1,2*w+w/2};
            report=new Report{status="RUNNING",unityVersion=Application.unityVersion,device=SystemInfo.graphicsDeviceName,
                scope="Real Unity Play Mode camera / tile identity checks. Screenshots and FPS require separate inspection."};
            index=phase=settled=0;deadline=EditorApplication.timeSinceStartup+90;
            nav.GoGlobe(app.CalendarDay);EditorApplication.update+=Step;
        }
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static void Step()
        {
            try
            {
                Require(EditorApplication.isPlaying&&app&&nav,"World or Play Mode ended");
                Require(EditorApplication.timeSinceStartup<deadline,"Focus test timed out");
                int tile=targets[index],w=app.World.Width;
                if(phase==0&&nav.IsGlobe){nav.Focus(tile,app.CalendarDay);phase=1;settled=0;}
                else if(phase==1)
                {
                    Require(Mathf.Abs(nav.CenterTile.y-(tile/w+.5f))<.01f,"Latitude reset during landing");
                    if(nav.IsFlat&&Mathf.Abs(nav.PixelsPerTile-48)<.01f&&++settled>4)
                    {
                        Require(camera.orthographic,"Detailed view must be orthographic");
                        Require(nav.Pick(camera.pixelRect.center)==tile,"Focused tile missed the screen centre");
                        nav.PanBy(new Vector2(w*3,0));
                        Require(nav.Pick(camera.pixelRect.center)==tile,"Wrapped copy changed Tile ID");
                        report.checks.Add("Focus + three wraps preserved Tile "+tile+" at latitude row "+tile/w);
                        index++;
                        if(index>=targets.Length){report.status="PASS";Finish();return;}
                        nav.GoGlobe(app.CalendarDay);phase=0;
                    }
                }
            }
            catch(Exception e){report.status="FAIL";report.checks.Add(e.ToString());Finish();}
        }
        static void Finish()
        {
            EditorApplication.update-=Step;
            if(app&&pauseField!=null)pauseField.SetValue(app,savedPause);
            string dir=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Validation","Perf3_3_2");
            Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"focus-unity-report.json"),JsonUtility.ToJson(report,true));
            if(report.status=="PASS")Debug.Log("ORBIS repaired focus PASS; view remains at the last test location.");
            else Debug.LogError("ORBIS repaired focus FAIL: "+string.Join("\n",report.checks));
        }
    }
}
#endif
