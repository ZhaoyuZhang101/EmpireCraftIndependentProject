// Run in an isolated Unity project with -batchmode -force-d3d11 -executeMethod
// RiverMouthPlayProbe.Run -mouthOutput <directory> -mouthWidth 2048 (or 4096).
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
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class RiverMouthPlayProbe
{
    const string Key = "ORBIS.QA.RiverMouths";
    static int stage, frame = -1;
    static string Output => SessionState.GetString(Key + ".output", "");
    static int Width => SessionState.GetInt(Key + ".width", 2048);
    // Scale is pixels per 2048-wide world cell, for equivalent geographic cameras.
    static readonly (string name, double lon, double lat, float scale)[] Sites =
    {
        ("jingjiang", 112.6318, 29.7510, 188),
        ("sichuan-yangtze", 105.7324, 28.9160, 180),
        ("yangtze-estuary", 121.52, 31.53, 68),
        ("yellow-mouth", 118.97, 37.77, 106),
        ("pearl-delta", 113.49, 22.29, 79),
        ("nile-delta", 31.00, 30.94, 62),
        ("rosetta", 30.359, 31.413, 120),
        ("damietta", 31.816, 31.479, 120),
        ("ganges-delta", 90.6, 22.25, 58)
    };

    static RiverMouthPlayProbe()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
    }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.productName.Contains("QA-"))
            throw new InvalidOperationException("Use a separate QA project and save directory.");
        var args = Environment.GetCommandLineArgs();
        string Arg(string flag) => args[Array.IndexOf(args, flag) + 1];
        SessionState.SetInt(Key + ".width", int.Parse(Arg("-mouthWidth")));
        SessionState.SetString(Key + ".output", Path.GetFullPath(Arg("-mouthOutput")));
        SessionState.SetFloat(Key + ".start", (float)EditorApplication.timeSinceStartup);
        SessionState.SetBool(Key, true);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "play.txt"), Application.unityVersion + " / " +
            SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType + Environment.NewLine);
        EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Record(string s) => File.AppendAllText(Path.Combine(Output, "play.txt"), s + Environment.NewLine);

    public static void RunVegetation()
    {
        if(!Application.isBatchMode||!Application.productName.Contains("QA-"))
            throw new InvalidOperationException("Use the isolated QA project.");
        Orbis.Editor.VegetationParallaxRegression.Run();
        Orbis.Editor.PropShadowRegression.Run();
    }

    static void Press(VisualElement ui, string id)
    {
        var b = ui.Q<Button>(id);
        b.Focus();
        using var e = NavigationSubmitEvent.GetPooled();
        e.target = b;
        b.SendEvent(e);
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".start", 0) > 1200)
                throw new TimeoutException("River camera stage " + stage);
            if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            var app = Object.FindFirstObjectByType<FlatWorldApp>();
            if (!app || app.Busy || app.World == null) return;
            var ui = app.GetComponentInChildren<UIDocument>().rootVisualElement;
            var map = app.GetComponentInChildren<FlatMapRenderer>();
            if (stage == 0)
            {
                ShaderUtil.allowAsyncCompilation = false;
                var size = ui.Q<DropdownField>("size");
                int choice = size.choices.IndexOf($"{Width} × {Width / 2}");
                if (choice < 0) throw new InvalidOperationException("Requested player size unavailable");
                size.index = choice;
                ui.Q<DropdownField>("preset").index = (int)TerrainPreset.Earth;
                ui.Q<TextField>("seed").value = "ORBIS-2D-2026";
                ui.Q<Toggle>("clouds").value = false;
                ui.Q<Toggle>("grid").value = false;
                if (ui.Q<Button>("pause").text != "继续") Press(ui, "pause");
                Press(ui, "generate");
                stage++;
                return;
            }
            if (app.World.Preset != TerrainPreset.Earth || app.World.Width != Width)
                throw new InvalidOperationException("Earth generation failed");
            if (map.PreparationPending) return;
            var requested = stage == 1 ? MapLayer.Natural : MapLayer.Rivers;
            if (map.VisibleLayer != requested) return;
            Record($"Earth {Width}x{Width/2}, {requested}, survey={app.World.Facts.RiverCurves.HasSurvey}, {app.World.Facts.RiverCurves.SegmentCount} segments");
            foreach (var site in Sites)
                Capture(app, map, site.name, site.lon, site.lat, site.scale, requested);
            if (stage == 1)
            {
                ui.Q<DropdownField>("layer").index = (int)MapLayer.Rivers;
                stage++;
                return;
            }
            // The same harness can capture the pre-fix revision before this editor regression exists.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-verifyRiverFootprint") >= 0)
            {
                var check = typeof(Orbis.Editor.RiverAtlasRegression).GetMethod("CheckSurveyFootprint",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (check == null) throw new MissingMethodException("CheckSurveyFootprint");
                check.Invoke(null, new object[] {map});
                Record("PASS surveyed river footprint on the installed player material");
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-verifyVegetation") >= 0)
            {
                Orbis.Editor.VegetationParallaxRegression.Run();
                Orbis.Editor.PropShadowRegression.Run();
                Record("PASS vegetation roots on both projections, seasonal banks, shadows and reflections");
            }
            Record("PASS both map layers rendered through the player camera");
            Finish(0);
        }
        catch (Exception e) { Record(e.ToString()); Debug.LogException(e); Finish(1); }
    }

    static void Capture(FlatWorldApp app, FlatMapRenderer map, string name, double lon, double lat,
        float referenceScale, MapLayer layer)
    {
        var nav = app.Navigation;
        var cam = app.GetComponentInChildren<Camera>();
        var target = RenderTexture.GetTemporary(960, 640, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var original = cam.targetTexture;
        var rect = cam.pixelRect;
        var active = RenderTexture.active;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            cam.targetTexture = target;
            cam.rect = new Rect(0, 0, 1, 1);
            double day = app.CalendarDay;
            nav.Tick(0, day);
            nav.Focus(app.World.Index((int)((lon + 180) / 360 * Width),
                (int)((lat + 90) / 180 * app.World.Height)), day);
            nav.Tick(3, day);
            float desired = Mathf.Min(96, referenceScale * 2048 / Width);
            nav.Wheel(Mathf.Log(nav.PixelsPerTile / desired) / .075f, cam.pixelRect.center, day);
            nav.Tick(3, day);
            nav.Recenter(new PlanetLocation(lon * Math.PI / 180, lat * Math.PI / 180), day);
            if (!nav.IsFlat || nav.IsAtlas || Mathf.Abs(nav.PixelsPerTile - desired) > .25f)
                throw new InvalidOperationException("Failed to enter local map at the chosen scale");
            map.Configure(false, nav.PixelsPerTile);
            nav.SyncMaterials();
            var material = nav.SurfaceMaterial;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-requireSurveyVectors")>=0 &&
               material.GetVector("_RiverAtlasLayout").z>=0)
                throw new InvalidOperationException("The surveyed map fell back to a filtered river raster");
            if(material.shader.keywordSpace.FindKeyword("ORBIS_RIVER_VECTORS").isValid &&
                material.GetVector("_RiverAtlasLayout").z<0 &&
                !material.IsKeywordEnabled("ORBIS_RIVER_VECTORS"))
                throw new InvalidOperationException("Local player material lost its surveyed river variant");
            var sun = FlatWeatherSimulation.Direction((lon - 25 + 180) / 360, (lat + 10 + 90) / 180);
            var sunlight=new Vector4((float)sun.X,(float)sun.Y,(float)sun.Z,0);
            SetLighting(material,sunlight,2);
            cam.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            string prefix = name + "-" + layer;
            File.WriteAllBytes(Path.Combine(Output, prefix + ".png"), pixels.EncodeToPNG());
            var colors = pixels.GetPixels32();
            if(name=="nile-delta" && layer==MapLayer.Natural && Width==4096 &&
                Array.IndexOf(Environment.GetCommandLineArgs(),"-verifyRiverFootprint")>=0)
                CheckDeltaSurface(colors,target.width,target.height);
            int magenta=0, variegated=0;
            var first=colors[0];
            for(int i=0;i<colors.Length;i+=7)
            {
                var c=colors[i];
                if(c.r>235 && c.g<20 && c.b>235) magenta++;
                if(Math.Abs(c.r-first.r)+Math.Abs(c.g-first.g)+Math.Abs(c.b-first.b)>12) variegated++;
            }
            if(magenta>colors.Length/700 || variegated<20 || ShaderUtil.ShaderHasError(material.shader))
                throw new InvalidOperationException(prefix+" rendered an invalid frame or material: "+material.shader.name);
            Record($"{prefix}: {lon:F5}E {lat:F5}N, {nav.PixelsPerTile:F2} px/tile, {material.shader.name}, atlas {material.GetFloat("_RiverAtlasReady")}, preview {material.GetFloat("_RiverPreviewReady")}, layout {material.GetVector("_RiverAtlasLayout")}");
            if(name=="jingjiang" && layer==MapLayer.Natural &&
                Array.IndexOf(Environment.GetCommandLineArgs(),"-riverTiming")>=0)
            {
                var times=new double[9];
                for(int i=0;i<times.Length;i++)
                {
                    var clock=System.Diagnostics.Stopwatch.StartNew();
                    cam.Render();
                    RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,1,1),0,0,false);
                    times[i]=clock.Elapsed.TotalMilliseconds;
                }
                Array.Sort(times);
                Record($"Jingjiang 960x640, 9 warmed synchronous camera renders including one-pixel readback, median {times[4]:F2} ms");
            }
            if(name=="jingjiang" && layer==MapLayer.Natural &&
                Array.IndexOf(Environment.GetCommandLineArgs(),"-riverMotion")>=0)
                CheckMotion(app,map,cam,lon,lat,sunlight,target,pixels,colors);
            if(name=="jingjiang" && layer==MapLayer.Natural &&
                Array.IndexOf(Environment.GetCommandLineArgs(),"-riverClip")>=0)
                CaptureRiverClip(app,map,cam,lon,lat,desired,sunlight,target,pixels);
        }
        finally
        {
            cam.targetTexture = original;
            cam.pixelRect = rect;
            RenderTexture.active = active;
            Object.DestroyImmediate(pixels);
            RenderTexture.ReleaseTemporary(target);
        }
    }

    static void SetLighting(Material material,Vector4 sun,float waterTime)
    {
        material.SetVector("_FlatSun",sun);
        material.SetFloat("_FlatDayNight",0);
        material.SetFloat("_FlatRain",0);
        material.SetFloat("_FlatCloudShadows",0);
        material.SetFloat("_WaterClock",waterTime);
    }

    static bool ChannelColor(Color32 c) => c.b>c.r+18 && c.g>c.r+12 && c.b>c.g-22;

    static void CheckDeltaSurface(Color32[] pixels,int width,int height)
    {
        // Fixed geographic camera. Three ESA WorldCover cropland points and
        // independent wet controls in the Mediterranean, Burullus and Manzala.
        foreach(var (x,y,wet) in new[]{(434,242,false),(445,230,false),(525,217,false),
                                        (485,34,true),(483,111,true),(793,186,true)})
        {
            var c=pixels[(height-1-y)*width+x];
            bool expected=wet?ChannelColor(c):c.g>c.r+12 && c.r>c.b+8;
            if(!expected)throw new InvalidOperationException($"Delta surface at {x},{y}: expected wet={wet}, observed {c}");
        }
        Record("PASS Nile farmland, coastal lagoons and offshore sea on the natural material");
    }

    static void CaptureRiverClip(FlatWorldApp app,FlatMapRenderer map,Camera cam,double lon,double lat,
        float zoom,Vector4 sun,RenderTexture target,Texture2D pixels)
    {
        string folder=Path.Combine(Output,"clip");
        Directory.CreateDirectory(folder);
        var nav=app.Navigation;
        double day=app.CalendarDay;
        nav.Wheel(Mathf.Log(nav.PixelsPerTile/zoom)/.075f,cam.pixelRect.center,day);
        nav.Tick(3,day);
        for(int i=0;i<72;i++)
        {
            double t=i/71.0,deg=360.0/Width/zoom;
            // First hold the view, then pan along the bend. The final section
            // follows the player's wheel path with a smooth 12% zoom-out.
            float pan=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.20f,.79f,(float)t));
            float scale=zoom*(1-.12f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.58f,1,(float)t)));
            if(Mathf.Abs(nav.PixelsPerTile-scale)>.001f)
            {
                nav.Wheel(Mathf.Log(nav.PixelsPerTile/scale)/.075f,cam.pixelRect.center,day);
                nav.Tick(3,day);
            }
            nav.Recenter(new PlanetLocation((lon+70*pan*deg/Math.Cos(lat*Math.PI/180))*Math.PI/180,
                (lat+20*pan*deg)*Math.PI/180),day);
            map.Configure(false,nav.PixelsPerTile);
            nav.SyncMaterials();
            SetLighting(nav.SurfaceMaterial,sun,2+i/24f);
            cam.Render();
            RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,pixels.width,pixels.height),0,0,false);
            File.WriteAllBytes(Path.Combine(folder,$"jingjiang-{i:000}.png"),pixels.EncodeToPNG());
        }
    }

    static void CheckMotion(FlatWorldApp app,FlatMapRenderer map,Camera cam,double lon,double lat,
        Vector4 sun,RenderTexture target,Texture2D pixels,Color32[] first)
    {
        var nav=app.Navigation;
        var curves=app.World.Facts.RiverCurves;
        int w=pixels.width,h=pixels.height;
        double day=app.CalendarDay;
        float initialZoom=nav.PixelsPerTile;
        var sites=new List<(PlanetLocation geo,bool wet,Color32 first)>();
        int wet=0,dry=0,expectedWater=0,missingWater=0;
        for(int y=12;y<h-12;y+=6)for(int x=12;x<w-12;x+=6)
        {
            var cursor=new Vector2(x+.5f,y+.5f);
            if(!nav.TryGetPlanetLocation(cursor,out var loc))continue;
            var sample=curves.Sample((float)PlanetCoordinateSystem.MapU(loc)*Width,
                (float)PlanetCoordinateSystem.MapV(loc)*app.World.Height);
            float clearance=Mathf.Max(.17f*sample.HalfWidth,1.4f/initialZoom);
            if(sample.HalfWidth<=0||Mathf.Abs(sample.Distance)<clearance||
                sample.Distance>sample.HalfWidth+4/initialZoom)continue;
            bool submerged=sample.Distance<0;
            var original=first[y*w+x];
            if(!nav.TryActorSurface(loc,out var pos,out _,out _,out _))continue;
            Vector2 projected=cam.WorldToScreenPoint(pos);
            if((projected-cursor).sqrMagnitude>1)continue;
            bool drawnWater=ChannelColor(original);
            if(submerged)
            {
                expectedWater++;
                if(!drawnWater)
                {
                    if(missingWater<3)Record($"Channel color control at pixel ({x},{y}) from lower left: {original}, bank clearance {-sample.Distance*initialZoom:F2} px");
                    missingWater++;
                }
            }
            if(drawnWater!=submerged)continue;
            sites.Add((loc,submerged,original));
            if(submerged)wet++;else dry++;
        }
        if(wet<30||dry<30)throw new InvalidOperationException($"Insufficient tracked river controls: {wet} wet, {dry} dry");
        Record($"Natural-material water at {expectedWater-missingWater}/{expectedWater} independently projected channel interiors");
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-requireNaturalBanks")>=0 &&
            missingWater>.03f*expectedWater)
            throw new InvalidOperationException("Player natural layer interrupted the resolved channel");

        float maxFlip=0;
        int[] lumaDelta=new int[256];
        for(int frame=0;frame<12;frame++)
        {
            float scale=frame<7 ? initialZoom : initialZoom*(1-.015f*(frame-6));
            float pan=frame<4 ? 0 : (frame-3)*.55f;
            double degreesPerPixel=360.0/Width/initialZoom;
            nav.Wheel(Mathf.Log(nav.PixelsPerTile/scale)/.075f,cam.pixelRect.center,day);
            nav.Tick(3,day);
            nav.Recenter(new PlanetLocation((lon+pan*degreesPerPixel/Math.Cos(lat*Math.PI/180))*Math.PI/180,
                (lat+pan*.37*degreesPerPixel)*Math.PI/180),day);
            map.Configure(false,nav.PixelsPerTile);
            nav.SyncMaterials();
            SetLighting(nav.SurfaceMaterial,sun,2+frame/30f);
            cam.Render();
            RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,w,h),0,0,false);
            var actual=pixels.GetPixels32();
            int flips=0,tracked=0;
            foreach(var site in sites)
            {
                if(!nav.TryActorSurface(site.geo,out var pos,out _,out _,out _))continue;
                var pt=cam.WorldToScreenPoint(pos);
                int x=Mathf.FloorToInt(pt.x), y=Mathf.FloorToInt(pt.y);
                if(x<0||x>=w||y<0||y>=h)continue;
                var color=actual[y*w+x];
                tracked++;
                if(ChannelColor(color)!=site.wet)flips++;
                if(frame<4 && site.wet)
                {
                    int delta=Math.Abs((color.r+2*color.g+color.b)-(site.first.r+2*site.first.g+site.first.b))/4;
                    lumaDelta[delta]++;
                }
            }
            if(tracked<.9f*sites.Count)throw new InvalidOperationException("Lost geographic controls during camera motion");
            maxFlip=Mathf.Max(maxFlip,(float)flips/tracked);
            if(frame==0 || frame==5 || frame==11)
                File.WriteAllBytes(Path.Combine(Output,$"jingjiang-motion-{frame:00}.png"),pixels.EncodeToPNG());
        }
        float staticFlash=Percentile(lumaDelta,.99f);
        nav.Wheel(Mathf.Log(nav.PixelsPerTile/initialZoom)/.075f,cam.pixelRect.center,day);
        nav.Tick(3,day);
        nav.Recenter(new PlanetLocation(lon*Math.PI/180,lat*Math.PI/180),day);
        map.Configure(false,nav.PixelsPerTile);
        nav.SyncMaterials();
        SetLighting(nav.SurfaceMaterial,sun,3.5f);
        cam.Render();
        RenderTexture.active=target;
        pixels.ReadPixels(new Rect(0,0,w,h),0,0,false);
        var animated=pixels.GetPixels32();
        var animationDelta=new int[256];
        foreach(var site in sites)
        {
            if(!site.wet || !nav.TryActorSurface(site.geo,out var pos,out _,out _,out _))continue;
            var pt=cam.WorldToScreenPoint(pos);
            int x=Mathf.FloorToInt(pt.x),y=Mathf.FloorToInt(pt.y);
            if(x<0||x>=w||y<0||y>=h)continue;
            var c=animated[y*w+x];
            animationDelta[Math.Max(Math.Abs(c.r-site.first.r),Math.Max(Math.Abs(c.g-site.first.g),Math.Abs(c.b-site.first.b)))]++;
        }
        Record($"Jingjiang 12 tracked frames, {wet} deep-water and {dry} dry-bank controls, worst wet/dry flips {maxFlip:P3}, fixed-camera p99 luminance change {staticFlash:F4}, 1.5s water RGB change p90 {Percentile(animationDelta,.90f):F4}");
        if(maxFlip>.025f||staticFlash>.16f)throw new InvalidOperationException("Unstable river banks or full-channel flashing");
    }

    static float Percentile(int[] histogram,float fraction)
    {
        int total=0,cumulative=0;
        foreach(int n in histogram)total+=n;
        if(total==0)throw new InvalidOperationException("No projected river measurements.");
        for(int i=0;i<histogram.Length;i++)if((cumulative+=histogram[i])>=total*fraction)return i/255f;
        return 1;
    }

    static void Finish(int code)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
}
