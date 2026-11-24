using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.EditorTools
{
    // Cloud light-and-shadow review shots of the real 2D-world presentation:
    //   Unity.exe -batchmode -projectPath <p> -executeMethod Orbis.EditorTools.OrbisLightingCapture.Run -orbisCaptureOut <dir> [-orbisHours 0,55,80,92]
    // Freezes the calendar, places the sun at fixed hour angles over one coastal
    // river tile (0 = noon, 90 = on the terminator) and photographs the globe, the
    // half-unfolded transition and the local surface for each sun position.
    [InitializeOnLoad]
    public static class OrbisLightingCapture
    {
        const string Flag="OrbisLightingCapture.Out";
        const int Width=1600,Height=900;
        const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;

        static OrbisLightingCapture(){if(!string.IsNullOrEmpty(SessionState.GetString(Flag,"")))EditorApplication.update+=Step;}

        public static void Run()
        {
            string output=Arg("-orbisCaptureOut")??Path.GetFullPath("ValidationOutput/Lighting");Directory.CreateDirectory(output);
            SessionState.SetString(Flag,output);SessionState.SetString("OrbisLightingCapture.Hours",Arg("-orbisHours")??"0,55,80,92");
            SessionState.SetString("OrbisLightingCapture.Views",Arg("-orbisViews")??"0,1,2");
            SessionState.SetString("OrbisLightingCapture.Variants",Arg("-orbisVariants")??"base:");
            SessionState.SetFloat("OrbisLightingCapture.Start",(float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
            EditorApplication.update+=Step;EditorApplication.EnterPlaymode();
        }
        static string Arg(string name){var a=Environment.GetCommandLineArgs();for(int i=0;i<a.Length-1;i++)if(a[i]==name)return a[i+1];return null;}

        static readonly string[] Views={"globe","transition","surface"};
        static double lastStatus,appSeen=-1;static List<float> hours;static List<int> viewList;static List<string> variantNames,variantSettings;static int variant;static int hourIndex=-1,view,waited,tile=-1;static float globeZoom,surfaceZoom;static double baseDay;

        static void Step()
        {
            string output=SessionState.GetString(Flag,"");if(string.IsNullOrEmpty(output)){EditorApplication.update-=Step;return;}
            double elapsed=EditorApplication.timeSinceStartup-SessionState.GetFloat("OrbisLightingCapture.Start",0);
            if(elapsed>10800){Finish(output,"timeout",3);return;}
            if(!EditorApplication.isPlaying)return;
            var app=UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            var map=app?Get<FlatMapRenderer>(app,"map"):null;var nav=app?Get<FlatScrollNavigator>(app,"navigation"):null;var camera=app?Get<Camera>(app,"viewCamera"):null;
            if(elapsed-lastStatus>120)
            {
                lastStatus=elapsed;
                Debug.Log($"OrbisLightingCapture waiting {elapsed:F0}s app={(bool)app} world={(app&&app.World!=null)} map={(bool)map} atlas={(map&&map.RiverAtlasReady)} pending={(map&&map.PreparationPending)} frame={Time.frameCount} compiling={ShaderUtil.anythingCompiling}");
            }
            if(app&&appSeen<0)appSeen=elapsed;
            if(app&&app.World==null&&elapsed-appSeen>900)
            {
                // World generation never finished: report the app's own status and stop.
                var status=typeof(FlatWorldApp).GetField("status",Private)?.GetValue(app) as string;
                Debug.Log("OrbisLightingCapture world not generated after 600s; app status: "+status);
                Finish(output,"no world: "+status,5);return;
            }
            if(!app||app.World==null||!map||!nav||!camera||!map.RiverAtlasReady||map.PreparationPending)return;
            if(hourIndex<0)
            {
                hours=new List<float>();foreach(var h in SessionState.GetString("OrbisLightingCapture.Hours","0").Split(','))hours.Add(float.Parse(h,System.Globalization.CultureInfo.InvariantCulture));
                viewList=new List<int>();foreach(var v in SessionState.GetString("OrbisLightingCapture.Views","0,1,2").Split(','))viewList.Add(int.Parse(v));
                variantNames=new List<string>();variantSettings=new List<string>();
                foreach(var v in SessionState.GetString("OrbisLightingCapture.Variants","base:").Split(';')){int c=v.IndexOf(':');variantNames.Add(c<0?v:v.Substring(0,c));variantSettings.Add(c<0?"":v.Substring(c+1));}
                variant=0;ApplyVariant(app);
                typeof(FlatWorldApp).GetField("screenshotMode",Private)?.SetValue(app,true);
                typeof(FlatWorldApp).GetField("paused",Private)?.SetValue(app,true);
                var toggle=Get<Toggle>(app,"cloudToggle");if(toggle!=null)toggle.value=true;
                var quality=Get<DropdownField>(app,"cloudQuality");if(quality!=null)quality.index=1;
                tile=PickTile(app.World);baseDay=Math.Floor(app.World.Day);
                nav.Home(app.CalendarDay);globeZoom=(float)Field(nav,"zoom");
                nav.Focus(tile,app.CalendarDay);surfaceZoom=(float)Field(nav,"targetZoom");
                hourIndex=0;view=0;Setup(app,nav);waited=0;return;
            }
            if(waited++<60)return;
            Render(camera,nav,app,Path.Combine(output,$"{variantNames[variant]}_h{hours[hourIndex]:00}_{viewList[view]}_{Views[viewList[view]]}.png"));
            waited=0;view++;
            if(view>=viewList.Count)
            {
                view=0;hourIndex++;
                if(hourIndex>=hours.Count){hourIndex=0;variant++;if(variant>=variantNames.Count){Finish(output,"done",0);return;}ApplyVariant(app);}
            }
            Setup(app,nav);
        }

        static T Get<T>(object o,string name) where T:class=>o.GetType().GetField(name,Private)?.GetValue(o) as T;
        static object Field(object o,string name)=>o.GetType().GetField(name,Private).GetValue(o);
        static void SetZoom(FlatScrollNavigator nav,float z,double day)
        {
            var t=typeof(FlatScrollNavigator);t.GetField("zoom",Private).SetValue(nav,z);t.GetField("targetZoom",Private).SetValue(nav,z);
            t.GetField("zoomVelocity",Private)?.SetValue(nav,0f);nav.Tick(0,day);
        }

        // Sun over the tile at the given hour angle: Sky.Sun uses longitude a=frac(day+.5)*2pi,
        // overhead where a = -tile longitude (FlatRadial convention).
        static void Setup(FlatWorldApp app,FlatScrollNavigator nav)
        {
            var w=app.World;double lon=((tile%w.Width+.5)/w.Width-.5)*Math.PI*2;
            double a=-lon+hours[hourIndex]*Math.PI/180;
            double fraction=a/(Math.PI*2)-.5;fraction-=Math.Floor(fraction);
            w.Day=baseDay+fraction;double day=app.CalendarDay;
            var location=PlanetCoordinateSystem.FromMapUV((tile%w.Width+.5)/w.Width,(tile/w.Width+.5)/w.Height);
            switch(Views[viewList[view]])
            {
                case "globe": nav.GoGlobe(day);nav.Recenter(location,day);SetZoom(nav,globeZoom,day);break;
                case "transition":
                    nav.GoGlobe(day);nav.Focus(tile,day);
                    float lo=0,hi=1;for(int k=0;k<20;k++){float f=(lo+hi)*.5f;SetZoom(nav,Mathf.Lerp(globeZoom,surfaceZoom,f),day);if(nav.Morph<.5f)lo=f;else hi=f;}
                    break;
                case "surface": SetZoom(nav,surfaceZoom,day);break;
            }
        }

        // Settings overrides for look development, e.g. "haze=0.4,air=0.8,exposure=0.9".
        // The globe presenter re-applies its captured base air/haze/cloud density every
        // tick, so those are written to its base fields; the rest go to the snapshot.
        static Dictionary<string,float> defaults;
        static void ApplyVariant(FlatWorldApp app)
        {
            var globe=Get<FlatGlobePresenter>(app,"globe");if(!globe)return;var s=globe.Snapshot.settings;var t=typeof(FlatGlobePresenter);
            if(defaults==null)defaults=new Dictionary<string,float>{{"air",(float)t.GetField("baseAir",Private).GetValue(globe)},{"haze",(float)t.GetField("baseHaze",Private).GetValue(globe)},
                {"cloudDensity",(float)t.GetField("baseCloudDensity",Private).GetValue(globe)},{"exposure",s.exposure},{"scatter",s.scatteringStrength},{"shadow",s.shadowStrength}};
            var values=new Dictionary<string,float>(defaults);
            foreach(var pair in variantSettings[variant].Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))
            {
                var kv=pair.Split('=');float value=float.Parse(kv[1],System.Globalization.CultureInfo.InvariantCulture);
                if(kv[0].Trim().StartsWith("_"))Shader.SetGlobalFloat(kv[0].Trim(),value);else values[kv[0].Trim()]=value;
            }
            t.GetField("baseAir",Private).SetValue(globe,values["air"]);t.GetField("baseHaze",Private).SetValue(globe,values["haze"]);
            t.GetField("baseCloudDensity",Private).SetValue(globe,values["cloudDensity"]);
            s.exposure=values["exposure"];s.scatteringStrength=values["scatter"];s.shadowStrength=values["shadow"];
            Debug.Log("OrbisLightingCapture variant "+variantNames[variant]+" "+variantSettings[variant]);
        }

        static int PickTile(WorldData w)
        {
            if(Arg("-orbisCloudy")!=null)
            {
                // Wettest coastal land this month: the likeliest overcast ground.
                int wet=-1;float rain=-1;int month=w.Month;
                for(int i=0;i<w.Count;i++)
                {
                    if(w.Water[i]!=WaterKind.Land||w.CoastDistance[i]>3)continue;
                    float lat=Math.Abs(w.Latitude(i));if(lat<10||lat>55)continue;
                    float r=w.Precipitation[i*12+month];if(r>rain){rain=r;wet=i;}
                }
                if(wet>=0)return wet;
            }
            // A river mouth: coast, fresh water and likely weather in one frame.
            int best=-1;float flow=0;
            for(int i=0;i<w.Count;i++)
            {
                if(w.Water[i]!=WaterKind.River||w.CoastDistance[i]>2)continue;
                float lat=Math.Abs(w.Latitude(i));if(lat<15||lat>55)continue;
                if(w.Runoff[i]>flow){flow=w.Runoff[i];best=i;}
            }
            return best<0?w.Count/2:best;
        }

        static void Render(Camera camera,FlatScrollNavigator nav,FlatWorldApp app,string path)
        {
            var rt=RenderTexture.GetTemporary(Width,Height,24,RenderTextureFormat.ARGB32);
            var previous=camera.targetTexture;var active=RenderTexture.active;
            try
            {
                camera.targetTexture=rt;nav.SetViewport(new Rect(0,0,Width,Height));
                if(Views[viewList[view]]!="globe")SetZoom(nav,(float)Field(nav,"zoom"),app.CalendarDay);
                nav.Tick(0,app.CalendarDay);nav.SyncMaterials();camera.Render();
                RenderTexture.active=rt;var image=new Texture2D(Width,Height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                Debug.Log($"OrbisLightingCapture wrote {path} morph={nav.Morph:F2} px/tile={nav.PixelsPerTile:F1} day={app.CalendarDay:F3}");
            }
            finally{camera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);}
        }

        static void Finish(string output,string result,int code)
        {
            EditorApplication.update-=Step;SessionState.EraseString(Flag);File.WriteAllText(Path.Combine(output,"result.txt"),result);
            Debug.Log("OrbisLightingCapture "+result);
            if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.ExitPlaymode();
        }
    }
}
