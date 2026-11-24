using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.EditorTools
{
    // Cloud placement review on the real Earth preset, through the game itself:
    //   Unity.exe -batchmode -projectPath <p> -executeMethod Orbis.EditorTools.OrbisCloudAtlasCapture.Run
    //     -orbisCaptureOut <dir> [-orbisEarthSize 512] [-orbisDays 14,195] [-orbisCenters "-100,20;0,10;100,15;-150,-20"]
    //     [-orbisVariants "name:_Global=1;..."]
    // For each variant and calendar day it writes the baked cloud cover as an
    // equirectangular map (clouds over a plain land/sea base, plus a raw channel map),
    // noon globe shots centred on each longitude/latitude, and a stats file with
    // cloud cover by latitude band and by named climate region.
    [InitializeOnLoad]
    public static class OrbisCloudAtlasCapture
    {
        const string Flag="OrbisCloudAtlasCapture.Out";
        const int Width=1600,Height=900;
        const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;

        static OrbisCloudAtlasCapture(){if(!string.IsNullOrEmpty(SessionState.GetString(Flag,"")))EditorApplication.update+=Step;}

        public static void Run()
        {
            string output=Arg("-orbisCaptureOut")??Path.GetFullPath("ValidationOutput/CloudAtlas");Directory.CreateDirectory(output);
            SessionState.SetString(Flag,output);
            SessionState.SetString(Flag+".Size",Arg("-orbisEarthSize")??"512");
            SessionState.SetString(Flag+".Days",Arg("-orbisDays")??"14,195");
            SessionState.SetString(Flag+".Centers",Arg("-orbisCenters")??"-100,20;0,10;100,15;-150,-20");
            SessionState.SetString(Flag+".Variants",Arg("-orbisVariants")??"base:");
            SessionState.SetFloat(Flag+".Start",(float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
            EditorApplication.update+=Step;EditorApplication.EnterPlaymode();
        }
        static string Arg(string name){var a=Environment.GetCommandLineArgs();for(int i=0;i<a.Length-1;i++)if(a[i]==name)return a[i+1];return null;}
        static float F(string s)=>float.Parse(s.Trim(),CultureInfo.InvariantCulture);

        static int stage,waited,variant,dayIndex,shot;static double lastStatus;
        static List<string> variantNames,variantSettings;static List<float> days;static List<Vector2> centers;static float globeZoom;
        static readonly StringBuilder stats=new StringBuilder();

        static void Step()
        {
            string output=SessionState.GetString(Flag,"");if(string.IsNullOrEmpty(output)){EditorApplication.update-=Step;return;}
            double elapsed=EditorApplication.timeSinceStartup-SessionState.GetFloat(Flag+".Start",0);
            if(elapsed>10800){Finish(output,"timeout",3);return;}
            if(!EditorApplication.isPlaying)return;
            var app=UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            var map=app?Get<FlatMapRenderer>(app,"map"):null;var nav=app?Get<FlatScrollNavigator>(app,"navigation"):null;var camera=app?Get<Camera>(app,"viewCamera"):null;
            if(elapsed-lastStatus>120)
            {
                lastStatus=elapsed;
                Debug.Log($"OrbisCloudAtlasCapture stage {stage} {elapsed:F0}s world={(app&&app.World!=null)} preset={(app&&app.World!=null?app.World.Preset.ToString():"-")} busy={(app&&app.Busy)} compiling={ShaderUtil.anythingCompiling}");
            }
            if(!app||!map||!nav||!camera)return;
            if(stage==0)
            {
                // Let the start-up world finish, then regenerate as Earth through the game UI.
                if(app.Busy||app.World==null||!map.RiverAtlasReady||map.PreparationPending)return;
                var doc=app.GetComponentInChildren<UIDocument>();if(!doc)return;var ui=doc.rootVisualElement;
                string size=SessionState.GetString(Flag+".Size","512")+" ";
                var sizeField=ui.Q<DropdownField>("size");int index=sizeField.choices.FindIndex(c=>c.StartsWith(size,StringComparison.Ordinal));
                if(index>=0)sizeField.index=index;
                ui.Q<DropdownField>("preset").index=(int)TerrainPreset.Earth;
                typeof(FlatWorldApp).GetField("paused",Private)?.SetValue(app,true);
                app.SendMessage("Generate",SendMessageOptions.RequireReceiver);
                stage=1;return;
            }
            if(stage==1)
            {
                if(app.Busy||app.World==null||app.World.Preset!=TerrainPreset.Earth||!map.RiverAtlasReady||map.PreparationPending)return;
                Debug.Log($"OrbisCloudAtlasCapture Earth {app.World.Width}x{app.World.Height} ready");
                days=new List<float>();foreach(var d in SessionState.GetString(Flag+".Days","14").Split(','))days.Add(F(d));
                centers=new List<Vector2>();foreach(var c in SessionState.GetString(Flag+".Centers","0,0").Split(';')){var p=c.Split(',');centers.Add(new Vector2(F(p[0]),F(p[1])));}
                variantNames=new List<string>();variantSettings=new List<string>();
                foreach(var v in SessionState.GetString(Flag+".Variants","base:").Split(';')){int k=v.IndexOf(':');variantNames.Add(k<0?v:v.Substring(0,k));variantSettings.Add(k<0?"":v.Substring(k+1));}
                typeof(FlatWorldApp).GetField("screenshotMode",Private)?.SetValue(app,true);
                var toggle=Get<Toggle>(app,"cloudToggle");if(toggle!=null)toggle.value=true;
                var quality=Get<DropdownField>(app,"cloudQuality");if(quality!=null)quality.index=1;
                nav.Home(app.CalendarDay);globeZoom=(float)Field(nav,"zoom");
                variant=0;dayIndex=0;shot=0;ApplyVariant(app,camera);Setup(app,nav);waited=0;stage=2;return;
            }
            bool cloudsOn=Get<Toggle>(app,"cloudToggle")?.value??true;
            if(cloudsOn&&(!map.Sky.Ready||map.Sky.RegionalClimateStep!=(long)Math.Floor(app.World.Day*4)))return;
            if(waited++<150)return;
            string prefix=$"{variantNames[variant]}_d{days[dayIndex]:000}";
            if(shot==0&&!cloudsOn){}
            else if(shot==0)
            {
                DumpMap(app.World,map.Sky.Texture,output,prefix);
                // Forcing grids: synoptic (lift, ITCZ, storm track, moisture) and aux (Sc, orographic, lee, polar).
                var bake=typeof(FlatSkyRenderer).GetField("bake",Private)?.GetValue(map.Sky) as Material;
                if(bake)foreach(var name in new[]{"_CloudSynopticTex","_CloudSynopticAuxTex","_ClimateTex"})
                    if(bake.GetTexture(name) is Texture2D t&&t.isReadable){var pixels=t.GetPixels32();for(int k=0;k<pixels.Length;k++)pixels[k].a=255;
                        var copy=t.GetPixels32();var alpha=new Color32[copy.Length];for(int k=0;k<copy.Length;k++){byte a=copy[k].a;alpha[k]=new Color32(a,a,a,255);}
                        Write(pixels,t.width,t.height,Path.Combine(output,$"{prefix}{name}_rgb.png"));Write(alpha,t.width,t.height,Path.Combine(output,$"{prefix}{name}_a.png"));}
            }
            else Render(camera,nav,app,Path.Combine(output,$"{prefix}_c{shot}_globe.png"));
            waited=0;shot++;
            if(shot>centers.Count)
            {
                shot=0;dayIndex++;
                if(dayIndex>=days.Count)
                {
                    dayIndex=0;variant++;
                    if(variant>=variantNames.Count){File.WriteAllText(Path.Combine(output,"stats.txt"),stats.ToString());Finish(output,"done",0);return;}
                    ApplyVariant(app,camera);
                }
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

        // Noon over the shot's centre (or over 0° for the map dump, which is not lit).
        static void Setup(FlatWorldApp app,FlatScrollNavigator nav)
        {
            var w=app.World;var centre=shot==0?Vector2.zero:centers[shot-1];
            double lon=centre.x*Math.PI/180,a=-lon;
            double fraction=a/(Math.PI*2)-.5;fraction-=Math.Floor(fraction);
            w.Day=Math.Floor(days[dayIndex])+fraction;double day=app.CalendarDay;
            if(shot==0)return;
            var location=PlanetCoordinateSystem.FromMapUV((centre.x+180)/360,(centre.y+90)/180);
            nav.GoGlobe(day);nav.Recenter(location,day);SetZoom(nav,globeZoom,day);
        }

        // Variant keys: "_Global=value" sets a shader global; "clouds=0" turns the cloud toggle
        // off; "atmo=0" disables the atmosphere/cloud image effect; "air", "haze" and "exposure"
        // set the globe atmosphere settings. Settings persist into later variants unless reset.
        static void ApplyVariant(FlatWorldApp app,Camera camera)
        {
            var toggle=Get<Toggle>(app,"cloudToggle");if(toggle!=null)toggle.value=true;
            foreach(var r in camera.GetComponents<OrbisAtmosphereRenderer>())r.enabled=true;
            foreach(var pair in variantSettings[variant].Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))
            {
                var kv=pair.Split('=');string key=kv[0].Trim();float value=F(kv[1]);
                if(key=="clouds"){if(toggle!=null)toggle.value=value>.5f;}
                else if(key=="air"||key=="haze"||key=="exposure")
                {
                    // The globe presenter re-applies its captured base air/haze every tick.
                    var globe=Get<FlatGlobePresenter>(app,"globe");if(!globe)continue;var t=typeof(FlatGlobePresenter);
                    if(key=="air")t.GetField("baseAir",Private).SetValue(globe,value);
                    else if(key=="haze")t.GetField("baseHaze",Private).SetValue(globe,value);
                    else t.GetField("baseExposure",Private).SetValue(globe,value);
                }
                else if(key=="atmo"){foreach(var r in camera.GetComponents<OrbisAtmosphereRenderer>())r.enabled=value>.5f;}
                else Shader.SetGlobalFloat(key,value);
            }
            // Shader globals also feed the baked cloud column: force a rebake for this variant.
            var sky=Get<FlatMapRenderer>(app,"map")?.Sky;var revision=typeof(FlatSkyRenderer).GetField("revision",Private);
            if(sky&&revision!=null)revision.SetValue(sky,-1);
            Debug.Log("OrbisCloudAtlasCapture variant "+variantNames[variant]+" "+variantSettings[variant]);
        }

        // Named climate regions: lon0, lon1, lat0, lat1 (degrees) and the observed
        // total cloud fraction for January / July (MODIS/ISCCP climatology, rounded).
        static readonly (string name,float lon0,float lon1,float lat0,float lat1,float jan,float jul)[] Regions=
        {
            ("Sahara",-10,30,18,30,.15f,.12f),("Arabia",40,55,17,28,.15f,.12f),("Australia interior",120,140,-30,-20,.25f,.20f),
            ("California Sc",-130,-120,22,34,.45f,.70f),("Peru Sc",-90,-78,-22,-8,.65f,.80f),("Namibia Sc",0,12,-25,-10,.55f,.75f),
            ("NE Pacific trades",-160,-135,15,28,.50f,.50f),("SE Pacific high",-130,-100,-35,-22,.55f,.55f),
            ("ITCZ Pacific",-170,-100,3,12,.70f,.75f),("ITCZ Atlantic",-40,-15,0,10,.65f,.75f),("Maritime Continent",95,150,-10,8,.80f,.80f),
            ("Amazon",-70,-50,-12,0,.80f,.60f),("Congo",12,30,-5,5,.75f,.75f),
            ("N Atlantic storm track",-50,-10,45,62,.80f,.75f),("N Pacific storm track",150,210,40,58,.85f,.85f),
            ("Southern Ocean",-180,180,-62,-45,.88f,.85f),("Europe",0,30,45,58,.75f,.60f),("India",72,88,15,28,.20f,.80f),
        };

        static void DumpMap(WorldData w,RenderTexture texture,string output,string prefix)
        {
            int tw=texture.width,th=texture.height;
            var active=RenderTexture.active;var read=new Texture2D(tw,th,TextureFormat.RGBA32,false,true);
            try{RenderTexture.active=texture;read.ReadPixels(new Rect(0,0,tw,th),0,0);read.Apply(false);}finally{RenderTexture.active=active;}
            var px=read.GetPixels32();UnityEngine.Object.DestroyImmediate(read);
            var view=new Color32[px.Length];var raw=new Color32[px.Length];var cover=new float[px.Length];
            for(int y=0;y<th;y++)for(int x=0;x<tw;x++)
            {
                int i=y*tw+x;var c=px[i];float low=c.r/255f,high=c.g/255f;
                float alpha=1-(1-low*.82f)*(1-high*.35f);cover[i]=alpha;
                int tile=w.Index(Mathf.Min(w.Width-1,x*w.Width/tw),Mathf.Min(w.Height-1,y*w.Height/th));
                bool land=w.Water[tile]==WaterKind.Land||w.Water[tile]==WaterKind.River;
                Color baseColor=land?new Color(.30f,.33f,.24f):new Color(.04f,.10f,.22f);
                Color cloud=Color.Lerp(new Color(.80f,.82f,.85f),Color.white,low);
                view[i]=Color.Lerp(baseColor,cloud,alpha);raw[i]=new Color32(c.r,c.g,c.a,255);
            }
            Write(view,tw,th,Path.Combine(output,prefix+"_map.png"));Write(raw,tw,th,Path.Combine(output,prefix+"_raw.png"));

            float Mean(float lon0,float lon1,float lat0,float lat1,out float edges)
            {
                double sum=0;int n=0,transitions=0,steps=0;
                for(int y=0;y<th;y++)
                {
                    float lat=((y+.5f)/th-.5f)*180;if(lat<lat0||lat>lat1)continue;
                    bool previous=false;bool first=true;
                    for(int x=0;x<tw;x++)
                    {
                        float lon=((x+.5f)/tw-.5f)*360;if(lon<lon0||lon>lon1)continue;
                        float a=cover[y*tw+x];sum+=a;n++;bool cloudy=a>.45f;
                        if(!first){steps++;if(cloudy!=previous)transitions++;}first=false;previous=cloudy;
                    }
                }
                edges=steps>0?transitions*100f/steps:0;return n>0?(float)(sum/n):0;
            }
            bool july=w.Month>=4&&w.Month<=9;
            stats.AppendLine($"== {prefix} month {w.Month+1}");
            stats.AppendLine("region                    model  real  edges/100px");
            float error=0;
            foreach(var r in Regions)
            {
                float lon0=r.lon0,lon1=r.lon1;
                float m=Mean(lon0>180?lon0-360:lon0,lon1>180?180:lon1,r.lat0,r.lat1,out float e);
                if(r.lon1>180){float m2=Mean(-180,r.lon1-360,r.lat0,r.lat1,out float e2);m=(m+m2)*.5f;e=(e+e2)*.5f;}
                float real=july?r.jul:r.jan;error+=Math.Abs(m-real);
                stats.AppendLine($"{r.name,-24} {m,5:F2} {real,5:F2} {e,8:F1}");
            }
            stats.AppendLine($"mean abs error {error/Regions.Length:F3}");
            stats.Append("zonal mean (S->N, 10° bands):");
            for(int b=-90;b<90;b+=10)stats.Append($" {Mean(-180,180,b,b+10,out _):F2}");
            stats.AppendLine();
            Mean(-180,180,-60,60,out float global);stats.AppendLine($"cloud edges per 100 px, 60S-60N: {global:F1}");
            File.WriteAllText(Path.Combine(output,"stats.txt"),stats.ToString());
            Debug.Log("OrbisCloudAtlasCapture map "+prefix);
        }
        static void Write(Color32[] pixels,int w,int h,string path)
        {var t=new Texture2D(w,h,TextureFormat.RGBA32,false);t.SetPixels32(pixels);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}

        static void Render(Camera camera,FlatScrollNavigator nav,FlatWorldApp app,string path)
        {
            var rt=RenderTexture.GetTemporary(Width,Height,24,RenderTextureFormat.ARGB32);
            var previous=camera.targetTexture;var active=RenderTexture.active;
            try
            {
                camera.targetTexture=rt;nav.SetViewport(new Rect(0,0,Width,Height));
                nav.Tick(0,app.CalendarDay);nav.SyncMaterials();camera.Render();
                RenderTexture.active=rt;var image=new Texture2D(Width,Height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                Debug.Log("OrbisCloudAtlasCapture wrote "+path);
            }
            finally{camera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);}
        }

        static void Finish(string output,string result,int code)
        {
            EditorApplication.update-=Step;SessionState.EraseString(Flag);File.WriteAllText(Path.Combine(output,"result.txt"),result);
            Debug.Log("OrbisCloudAtlasCapture "+result);
            if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.ExitPlaymode();
        }
    }
}
