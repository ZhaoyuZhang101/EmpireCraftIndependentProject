using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Orbis.Flat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Orbis.EditorTools
{
    // Command-line visual capture of the real 2D world presentation:
    //   Unity.exe -batchmode -projectPath <p> -executeMethod Orbis.EditorTools.OrbisMapCapture.Run -orbisCaptureOut <dir>
    // Enters Play mode in Orbis2DWorld, waits for the default world and its river
    // atlas, renders fixed views (strategic atlas, atlas zoom, local surface, globe)
    // to PNG, then exits. Exit code 0 = all views written.
    [InitializeOnLoad]
    public static class OrbisMapCapture
    {
        const string Flag="OrbisMapCapture.Out";
        const string ScenePath="Assets/ORBIS/Scenes/Orbis2DWorld.unity";
        const int Width=1600,Height=900;
        const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;

        static OrbisMapCapture()
        {
            if(!string.IsNullOrEmpty(SessionState.GetString(Flag,"")))EditorApplication.update+=Step;
        }

        public static void Run()
        {
            string output=Argument("-orbisCaptureOut")??Path.GetFullPath("ValidationOutput/MapCapture");
            Directory.CreateDirectory(output);
            SessionState.SetString(Flag,output);
            SessionState.SetFloat("OrbisMapCapture.Start",(float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update+=Step;
            EditorApplication.EnterPlaymode();
        }

        static string Argument(string name)
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++)if(args[i]==name)return args[i+1];
            return null;
        }

        sealed class View { public string Name; public Action<FlatScrollNavigator,double> Setup; public int Frames; }
        static List<View> views;static int viewIndex=-1,waited;static int riverTile=-1,mountainTile=-1;

        static void Step()
        {
            string output=SessionState.GetString(Flag,"");
            if(string.IsNullOrEmpty(output)){EditorApplication.update-=Step;return;}
            double started=SessionState.GetFloat("OrbisMapCapture.Start",0);
            if(EditorApplication.timeSinceStartup-started>1500){Finish(output,"timeout",3);return;}
            if(!EditorApplication.isPlaying)return;
            var app=UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if(!app||app.World==null)return;
            var map=typeof(FlatWorldApp).GetField("map",Private).GetValue(app) as FlatMapRenderer;
            var nav=typeof(FlatWorldApp).GetField("navigation",Private).GetValue(app) as FlatScrollNavigator;
            var camera=typeof(FlatWorldApp).GetField("viewCamera",Private).GetValue(app) as Camera;
            if(!map||!nav||!camera)return;
            if(views==null)
            {
                if(!map.RiverAtlasReady||map.PreparationPending)return;
                PickTiles(app.World);
                typeof(FlatWorldApp).GetField("screenshotMode",Private)?.SetValue(app,true);
                views=new List<View>{
                    new View{Name="1_atlas_overview",Frames=90,Setup=(n,d)=>n.OpenAtlas(d)},
                    new View{Name="1b_atlas_regional",Frames=120,Setup=(n,d)=>n.Wheel(-14,new Vector2(Width*.5f,Height*.5f),d)},
                    new View{Name="2_atlas_river",Frames=90,Setup=(n,d)=>n.Focus(riverTile,d)},
                    new View{Name="3_local_river",Frames=150,Setup=(n,d)=>{n.GoGlobe(d);n.Focus(riverTile,d);}},
                    new View{Name="4_local_mountain",Frames=150,Setup=(n,d)=>n.Focus(mountainTile,d)},
                    new View{Name="5_globe",Frames=150,Setup=(n,d)=>n.Home(d)}};
                File.WriteAllText(Path.Combine(output,"world.txt"),$"seed={app.World.Seed} preset={app.World.Preset} size={app.World.Width}x{app.World.Height} riverTile={riverTile} mountainTile={mountainTile} plates={(app.World.Tectonics==null?0:app.World.Tectonics.Plates.Length)}");
            }
            if(viewIndex<0||waited>=views[viewIndex].Frames)
            {
                if(viewIndex>=0)Render(camera,nav,app,Path.Combine(output,views[viewIndex].Name+".png"));
                viewIndex++;waited=0;
                if(viewIndex>=views.Count){Finish(output,"done",0);return;}
                try{views[viewIndex].Setup(nav,app.CalendarDay);}
                catch(Exception e){Debug.LogException(e);Finish(output,"setup failed: "+e.Message,4);return;}
            }
            waited++;
        }

        static void PickTiles(WorldData w)
        {
            float best=0,high=-1;
            for(int i=0;i<w.Count;i++)
            {
                if(w.Water[i]==WaterKind.River&&w.Runoff[i]>best){best=w.Runoff[i];riverTile=i;}
                if(w.Water[i]==WaterKind.Land&&w.Elevation[i]>high){high=w.Elevation[i];mountainTile=i;}
            }
            // Step a few reaches upstream so the view shows the channel, not only the mouth.
            for(int k=0;k<6&&riverTile>=0;k++)
            {
                int up=-1;float flow=0;
                for(int d=0;d<4;d++){int n=w.Neighbor(riverTile,d);if(n>=0&&w.Downstream[n]==riverTile&&w.Water[n]==WaterKind.River&&w.Runoff[n]>flow){flow=w.Runoff[n];up=n;}}
                if(up<0)break;riverTile=up;
            }
        }

        static void Render(Camera camera,FlatScrollNavigator nav,FlatWorldApp app,string path)
        {
            var rt=RenderTexture.GetTemporary(Width,Height,24,RenderTextureFormat.ARGB32);
            var previous=camera.targetTexture;var active=RenderTexture.active;
            try
            {
                camera.targetTexture=rt;nav.SetViewport(new Rect(0,0,Width,Height));nav.Tick(0,app.CalendarDay);nav.SyncMaterials();
                camera.Render();
                RenderTexture.active=rt;var image=new Texture2D(Width,Height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                Debug.Log("OrbisMapCapture wrote "+path+" mode="+nav.ModeName+" px/tile="+nav.PixelsPerTile);
            }
            finally{camera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);}
        }

        static void Finish(string output,string result,int code)
        {
            EditorApplication.update-=Step;SessionState.EraseString(Flag);
            File.WriteAllText(Path.Combine(output,"result.txt"),result);
            Debug.Log("OrbisMapCapture "+result);
            if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.ExitPlaymode();
        }
    }
}
