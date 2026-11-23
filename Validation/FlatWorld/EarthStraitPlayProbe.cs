// Copy to Assets/ORBIS/Editor in a disposable ORBIS-Straits-QA-* product. Use
// Unity -batchmode -force-d3d11 -projectPath <copy> -executeMethod EarthStraitPlayProbe.Run
// -straitWidth 2048 -straitOutput <directory> -logFile <log>. Do not pass -quit.
// Add -straitRoute (Turkey) or -straitSetoRoute (Kanmon) to test the maritime tool.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class EarthStraitPlayProbe
{
    const string Key = "ORBIS.QA.EarthStraits";
    const string Seed = "ORBIS-2D-2026";
    const int Width = 960, Height = 640;
    static int stage, lastFrame = -1, failures;
    static float startPixels;
    static double generateTime, routeRequested;
    static double lastSample, firstDay;
    static int samples;
    static readonly double[] intervals = new double[30];
    static string Output => SessionState.GetString(Key + ".out", "");
    static int WorldWidth => SessionState.GetInt(Key + ".width", 0);
    static bool SetoRoute => Array.IndexOf(Environment.GetCommandLineArgs(), "-straitSetoRoute") >= 0;
    static bool TestRoute => SetoRoute || Array.IndexOf(Environment.GetCommandLineArgs(), "-straitRoute") >= 0;
    static Vector2 Departure => SetoRoute ? Sites[2].seaA : Sites[1].seaA;
    static Vector2 Destination => SetoRoute ? Sites[2].seaB : Sites[0].seaB;
    static double Now => EditorApplication.timeSinceStartup;

    // Independent WGS84 control points on the Natural Earth 1:10m ocean polygon.
    // At 41.11N the Bosporus spans 29.06053E..29.07458E; at 40.16N the
    // Dardanelles span 26.36546E..26.40206E. Shore samples straddle each cut.
    sealed class Strait
    {
        public string name;
        public Vector2 center, mouthA, mouthB, throat, bankA, bankB, seaA, seaB;
    }
    static readonly Strait[] Sites =
    {
        new Strait { name="bosporus", center=new Vector2(29.061f,41.110f),
            mouthA=new Vector2(28.960f,40.940f), mouthB=new Vector2(29.155f,41.280f),
            throat=new Vector2(29.0675f,41.110f), bankA=new Vector2(29.031f,41.110f), bankB=new Vector2(29.106f,41.110f),
            seaA=new Vector2(28.40f,40.81f), seaB=new Vector2(29.48f,41.64f) },
        new Strait { name="dardanelles", center=new Vector2(26.43f,40.22f),
            mouthA=new Vector2(26.130f,39.990f), mouthB=new Vector2(26.760f,40.440f),
            throat=new Vector2(26.384f,40.160f), bankA=new Vector2(26.330f,40.160f), bankB=new Vector2(26.430f,40.160f),
            seaA=new Vector2(25.65f,40.00f), seaB=new Vector2(28.40f,40.81f) },
        new Strait { name="kanmon", center=new Vector2(130.96f,33.955f),
            mouthA=new Vector2(130.938f,33.937f), mouthB=new Vector2(131.046f,33.979f),
            throat=new Vector2(130.950f,33.949f), bankA=new Vector2(130.930f,33.949f), bankB=new Vector2(130.978f,33.949f),
            seaA=new Vector2(130.77f,33.96f), seaB=new Vector2(131.16f,33.92f) },
        new Strait { name="naruto", center=new Vector2(134.653f,34.239f),
            mouthA=new Vector2(134.634f,34.277f), mouthB=new Vector2(134.687f,34.204f),
            throat=new Vector2(134.651f,34.239f), bankA=new Vector2(134.629f,34.224f), bankB=new Vector2(134.679f,34.261f),
            seaA=new Vector2(134.60f,34.32f), seaB=new Vector2(134.70f,34.18f) },
        new Strait { name="akashi", center=new Vector2(135.02f,34.62f),
            mouthA=new Vector2(134.965f,34.629f), mouthB=new Vector2(135.072f,34.602f),
            throat=new Vector2(135.021f,34.620f), bankA=new Vector2(135.021f,34.583f), bankB=new Vector2(135.021f,34.655f),
            seaA=new Vector2(134.86f,34.59f), seaB=new Vector2(135.13f,34.63f) }
    };

    static EarthStraitPlayProbe()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
    }
    static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
    static void Record(string message)
    {
        File.AppendAllText(Path.Combine(Output, "play.txt"), message + Environment.NewLine);
    }
    static void Expect(bool ok, string message)
    {
        if (!ok) failures++;
        Record((ok ? "PASS " : "FAIL ") + message);
    }
    // Unity's Windows Editor Mono returns zero for Process.WorkingSet64.
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryCounters
    {
        public uint Size, Faults;
        public UIntPtr PeakWorking, Working, PeakPaged, Paged, PeakNonPaged, NonPaged, Pagefile, PeakPagefile, Private;
    }
    [DllImport("psapi.dll")]
    static extern bool GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, int size);
    static void ResourcesAt(string step)
    {
        Require(GetProcessMemoryInfo(new IntPtr(-1), out var memory, Marshal.SizeOf<MemoryCounters>()),
            "Windows process memory counters unavailable");
        Record($"{step} at {Now:F2}s, working {memory.Working.ToUInt64()/1048576} MiB, peak {memory.PeakWorking.ToUInt64()/1048576} MiB, private {memory.Private.ToUInt64()/1048576} MiB");
    }
    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        Require(index >= 0 && index + 1 < args.Length, "Missing " + name);
        return args[index + 1];
    }
    public static void Run()
    {
        Require(Application.isBatchMode && Application.productName.StartsWith("ORBIS-Straits-QA-"),
            "Use an isolated strait QA product.");
        SessionState.SetString(Key + ".out", Path.GetFullPath(Arg("-straitOutput")));
        SessionState.SetInt(Key + ".width", int.Parse(Arg("-straitWidth")));
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "play.txt"),
            $"{Application.unityVersion} / {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType} / {Application.productName}\n");
        SessionState.SetFloat(Key + ".start", (float)Now);
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }
    static void Press(VisualElement ui, string name)
    {
        var button = ui.Q<Button>(name);
        button.Focus();
        using (var submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = button;
            button.SendEvent(submit);
        }
    }
    static PlanetLocation Location(Vector2 xy) =>
        new PlanetLocation(xy.x * Math.PI / 180, xy.y * Math.PI / 180);
    static Vector2Int Tile(WorldData world, Vector2 xy) =>
        new Vector2Int((int)((xy.x + 180.0)/360 * world.Width), (int)((xy.y + 90.0)/180 * world.Height));

    static void Tick()
    {
        try
        {
            if (Now - SessionState.GetFloat(Key + ".start", 0) > 1800) throw new TimeoutException("Stage " + stage);
            if (!EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            var app = Object.FindFirstObjectByType<FlatWorldApp>();
            if (!app || app.Busy || app.World == null) return;
            var ui = app.GetComponentInChildren<UIDocument>().rootVisualElement;
            var camera = app.GetComponentInChildren<Camera>();
            var nav = app.Navigation;
            var map = app.GetComponentInChildren<FlatMapRenderer>();
            switch (stage)
            {
                case 0:
                    ShaderUtil.allowAsyncCompilation = false;
                    var size = ui.Q<DropdownField>("size");
                    int selected = size.choices.IndexOf($"{WorldWidth} × {WorldWidth/2}");
                    Require(selected >= 0, "Missing player menu size. Choices: " + string.Join(", ", size.choices));
                    size.index = selected;
                    ui.Q<DropdownField>("preset").index = (int)TerrainPreset.Earth;
                    ui.Q<TextField>("seed").value = Seed;
                    ui.Q<Toggle>("clouds").value = false;
                    ui.Q<Toggle>("grid").value = false;
                    if (ui.Q<Button>("pause").text != "继续") Press(ui, "pause");
                    Record("Player menu: " + string.Join(", ", size.choices));
                    ResourcesAt("Generate pressed");
                    generateTime = Now;
                    Press(ui, "generate");
                    Require(app.Busy, "Generate button did not enqueue the selected world");
                    break;
                case 1:
                    Require(app.World.Width == WorldWidth && app.World.Height == WorldWidth/2 &&
                        app.World.Preset == TerrainPreset.Earth && app.World.Seed == Seed, "World installation failed: " + ui.Q<Label>("notice").text);
                    Require(ui.Q<DropdownField>("size").value == $"{WorldWidth} × {WorldWidth/2}", "Menu selection changed");
                    Require(map.TerrainTexture.width == WorldWidth, "Renderer published the wrong texture width");
                    Record($"PASS player menu generated Earth {WorldWidth}x{app.World.Height}, seed {app.World.Seed}, installed in {Now-generateTime:F2}s");
                    ResourcesAt("Installed");
                    foreach (var site in Sites) CheckTiles(app.World, site);
                    startPixels = nav.PixelsPerTile;
                    var center = Tile(app.World, Sites[0].center);
                    nav.Focus(app.World.Index(center.x, center.y), app.CalendarDay);
                    nav.Tick(3, app.CalendarDay);
                    nav.Wheel(-4, camera.pixelRect.center, app.CalendarDay);
                    nav.Tick(3, app.CalendarDay);
                    Require(nav.IsDetailed && nav.PixelsPerTile > startPixels, "Focus / wheel did not enter the player's close view");
                    Record($"PASS native navigation {startPixels:F2} to {nav.PixelsPerTile:F2} px/tile via focus and wheel, {nav.SurfaceMaterial.shader.name}");
                    break;
                case 2:
                    if (map.PreparationPending || !map.RiverAtlasReady || map.VisibleLayer != MapLayer.Natural) return;
                    CheckCompactMarineDepth(map);
                    CheckGlobalShoreSamples(map);
                    if (WorldWidth == 4096)
                    {
                        // Keep the same probe source runnable against pre-4096 revisions.
                        var gpuTest = typeof(Orbis.Editor.EcologyPresentationRegression).GetMethod("CheckLargeSeasonTexture");
                        Require(gpuTest != null, "Missing 4096 seasonal GPU regression");
                        gpuTest.Invoke(null,new object[] {map});
                        Record("PASS 4096 seasonal texture geometry, month uploads and geographic GPU samples");
                    }
                    Capture(app, map, camera, "marmara-natural", new Vector2(27.85f,40.72f), 42, null, true);
                    Capture(app, map, camera, "seto-natural", new Vector2(132.70f,34.10f), 40, null, true);
                    Capture(app, map, camera, "seto-regional", new Vector2(131.90f,34.05f), 16.3f, null, true);
                    foreach (var site in Sites) Capture(app, map, camera, site.name + "-natural", site.center, 96, site, true);
                    ResourcesAt("Natural camera readbacks");
                    ui.Q<DropdownField>("layer").index = (int)MapLayer.Rivers;
                    break;
                case 3:
                    if (map.VisibleLayer != MapLayer.Rivers || map.PreparationPending) return;
                    foreach (var site in Sites) Capture(app, map, camera, site.name + "-river-map", site.center, 96, site, false);
                    ResourcesAt("Verified local views");
                    ui.Q<DropdownField>("layer").index = (int)MapLayer.Natural;
                    break;
                case 4:
                    if (map.VisibleLayer != MapLayer.Natural || map.PreparationPending) return;
                    if (ui.Q<Button>("pause").text == "继续") Press(ui,"pause");
                    firstDay = app.CalendarDay;
                    lastSample = Now;
                    break;
                case 5:
                    double now = Now;
                    intervals[samples++] = 1000*(now-lastSample);
                    lastSample = now;
                    if (samples < intervals.Length) return;
                    Array.Sort(intervals);
                    Expect(app.CalendarDay > firstDay,"unpaused local natural view advanced the calendar");
                    Record($"30 unpaused native Editor update intervals, clouds off: median {intervals[15]:F1} ms, p95 {intervals[28]:F1} ms; +{app.CalendarDay-firstDay:F6} days");
                    ResourcesAt("Unpaused local updates");
                    if (!TestRoute) { Result(); return; }
                    if (ui.Q<Button>("pause").text != "继续") Press(ui,"pause");
                    var tool = ui.Q<DropdownField>("tool");
                    int seaTool = tool.choices.FindIndex(s => s.Contains("海上航线"));
                    Require(seaTool >= 0, "No maritime route in the player tools");
                    tool.index = seaTool;
                    ClickSea(app,camera,ui,Departure);
                    routeRequested=Now;
                    Record("Maritime departure click: " + ui.Q<Label>("notice").text);
                    break;
                case 6:
                    if (!ui.Q<Label>("notice").text.Contains("已选出发点"))
                    {
                        Require(Now-routeRequested<30,"Player did not accept the marine departure. "+ui.Q<Label>("notice").text);
                        return;
                    }
                    ClickSea(app,camera,ui,Destination);
                    routeRequested=Now;
                    Record("Maritime destination click: " + ui.Q<Label>("notice").text);
                    break;
                case 7:
                    var points = RoutePoints(nav);
                    if (points == null || points.Count < 2)
                    {
                        Require(Now-routeRequested<120,"Timed out waiting for the maritime route. "+ui.Q<Label>("notice").text);
                        return;
                    }
                    Record("Maritime route status: " + ui.Q<Label>("notice").text);
                    Expect(ui.Q<Label>("notice").text.Contains("已显示海上航线"),"player confirmed the maritime overlay");
                    SaveRoute(points);
                    if (SetoRoute)
                        Capture(app,map,camera,"kanmon-sea-route",Sites[2].center,96,null,true,true);
                    else
                    {
                        Capture(app,map,camera,"marmara-sea-route",new Vector2(27.85f,40.72f),42,null,true,true,atlasRoundTrip:true);
                        foreach (var site in new[] { Sites[0], Sites[1] })
                            Capture(app,map,camera,site.name+"-sea-route",site.center,96,null,true,true);
                    }
                    ResourcesAt("Player sea route");
                    Result();
                    return;
            }
            stage++;
        }
        catch (Exception error)
        {
            Record("FATAL stage " + stage + " " + error);
            UnityEngine.Debug.LogException(error);
            Finish(1);
        }
    }
    static void Finish(int code)
    {
        SessionState.EraseBool(Key);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
    static void Result()
    {
        Record($"RESULT {failures} failed checks");
        Finish(failures == 0 ? 0 : 2);
    }
    static void ClickSea(FlatWorldApp app,Camera camera,VisualElement ui,Vector2 geographic)
    {
        var nav=app.Navigation;
        nav.Recenter(Location(geographic),app.CalendarDay);
        Vector2 screen=camera.pixelRect.center;
        int tile=nav.Pick(screen);
        Require(tile>=0 && FlatTerrainAnalysis.Ocean(app.World.Water[tile]),"Maritime click did not target offshore water at "+geographic);
        Vector2 local=new Vector2(screen.x/Screen.width*ui.resolvedStyle.width,(1-screen.y/Screen.height)*ui.resolvedStyle.height);
        var raw=new Event {type=EventType.MouseDown,button=0,mousePosition=ui.LocalToWorld(local)};
        var viewport=ui.Q("viewport");
        using(var down=PointerDownEvent.GetPooled(raw)){down.target=viewport;viewport.SendEvent(down);}
        raw.type=EventType.MouseUp;
        using(var up=PointerUpEvent.GetPooled(raw)){up.target=viewport;viewport.SendEvent(up);}
        Record($"Native viewport pointer release at {geographic.x:F3}E/{geographic.y:F3}N on ocean tile {tile}");
    }
    static void SaveRoute(IReadOnlyList<PlanetLocation> points)
    {
        var text=new StringBuilder("longitude,latitude\n");
        foreach(var p in points)
            text.AppendFormat(CultureInfo.InvariantCulture,"{0:R},{1:R}\n",p.Longitude*180/Math.PI,p.Latitude*180/Math.PI);
        File.WriteAllText(Path.Combine(Output,SetoRoute?"kanmon-route.csv":"maritime-route.csv"),text.ToString());
        double Distance(PlanetLocation p,Vector2 q) => Math.Abs(p.Longitude*180/Math.PI-q.x)+Math.Abs(p.Latitude*180/Math.PI-q.y);
        double tolerance=720.0/WorldWidth;
        Expect(Distance(points[0],Departure)<tolerance && Distance(points[points.Count-1],Destination)<tolerance,
            $"maritime polyline has {points.Count} points and joins the two clicked sea locations");
    }
    static IReadOnlyList<PlanetLocation> RoutePoints(FlatScrollNavigator nav)
    {
        // Reflection keeps this same probe buildable against the pre-route baseline.
        var member=typeof(FlatScrollNavigator).GetProperty("RoutePoints");
        Require(member!=null,"Player navigator has no route points");
        return (IReadOnlyList<PlanetLocation>)member.GetValue(nav);
    }
    static RectInt Bounds(Vector2Int a, Vector2Int b, int pad, int w, int h)
    {
        var low = Vector2Int.Max(Vector2Int.zero, Vector2Int.Min(a,b) - new Vector2Int(pad,pad));
        var high = Vector2Int.Min(new Vector2Int(w-1,h-1), Vector2Int.Max(a,b) + new Vector2Int(pad,pad));
        return new RectInt(low, high - low + Vector2Int.one);
    }
    // Distinguish a face-connected, local sea passage from a diagonal touch or
    // a way around Denmark/Africa. Use the same flood on screen and in tile space.
    static bool Connected(RectInt bounds, Vector2Int a, Vector2Int b, Func<int,int,bool> wet)
    {
        var queue = new Queue<Vector2Int>();
        var seen = new bool[bounds.width * bounds.height];
        void Visit(int x, int y)
        {
            if (x < bounds.xMin || y < bounds.yMin || x >= bounds.xMax || y >= bounds.yMax) return;
            int i = (y - bounds.yMin)*bounds.width + x - bounds.xMin;
            if (seen[i]) return;
            seen[i] = true;
            if (wet(x,y)) queue.Enqueue(new Vector2Int(x,y));
        }
        Visit(a.x,a.y);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (p == b) return true;
            Visit(p.x-1,p.y); Visit(p.x+1,p.y); Visit(p.x,p.y-1); Visit(p.x,p.y+1);
        }
        return false;
    }
    static void CheckTiles(WorldData w, Strait site)
    {
        var a = Tile(w,site.seaA);
        var b = Tile(w,site.seaB);
        var bounds = Bounds(a,b,Mathf.Max(1,Mathf.CeilToInt(.22f * w.Width/360)),w.Width,w.Height);
        bool wet(int x,int y) => FlatTerrainAnalysis.Ocean(w.Water[w.Index(x,y)]);
        Expect(Connected(bounds,a,b,wet), $"{site.name} generated world has a local four-face marine route, {a} to {b} in {bounds}");
    }
    static Color32[] Render(Camera camera, RenderTexture target, Texture2D readback)
    {
        camera.Render();
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0,0,Width,Height),0,0);
        readback.Apply();
        return readback.GetPixels32();
    }
    static void Capture(FlatWorldApp app, FlatMapRenderer map, Camera camera, string name,
        Vector2 location, float refPixels, Strait site, bool natural, bool route=false, bool atlasRoundTrip=false)
    {
        var nav = app.Navigation;
        var target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var oldTarget = camera.targetTexture;
        var oldRect = camera.pixelRect;
        var oldActive = RenderTexture.active;
        var pixels = new Texture2D(Width,Height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture = target;
            camera.rect = new Rect(0,0,1,1);
            nav.Tick(0,app.CalendarDay);
            var center = Tile(app.World,location);
            nav.Focus(app.World.Index(center.x,center.y), app.CalendarDay);
            nav.Tick(3,app.CalendarDay);
            float desired = refPixels * (2048f/app.World.Width);
            nav.Wheel(Mathf.Log(nav.PixelsPerTile/desired)/.075f,camera.pixelRect.center,app.CalendarDay);
            nav.Tick(3,app.CalendarDay);
            nav.Recenter(Location(location),app.CalendarDay);
            Require(nav.IsFlat && !nav.IsAtlas && Mathf.Abs(nav.PixelsPerTile-desired)<.2f, "Unexpected camera scale " + nav.PixelsPerTile);
            map.Configure(false,nav.PixelsPerTile);
            nav.SyncMaterials();
            var mat = nav.SurfaceMaterial;
            var sun = FlatWeatherSimulation.Direction((3.0+180)/360,(33.0+90)/180);
            mat.SetVector("_FlatSun", new Vector4((float)sun.X,(float)sun.Y,(float)sun.Z,0));
            mat.SetFloat("_FlatDayNight",0);
            mat.SetFloat("_FlatRain",0);
            mat.SetFloat("_FlatCloudShadows",0);
            mat.SetFloat("_FlatClouds",0);
            mat.SetFloat("_WaterClock",2);
            Record($"{name}: {Width}x{Height}, {nav.PixelsPerTile:F2} px/tile, center {location.x:F4}E {location.y:F4}N, day {app.CalendarDay:F6}, clear sky, fixed sun 3E/33N, water clock 2");
            var visible = Render(camera,target,pixels);
            File.WriteAllBytes(Path.Combine(Output,name + ".png"),pixels.EncodeToPNG());
            if (name=="seto-regional") CheckSetoViews(app,map,camera,target,pixels,location);
            if (route) CheckRouteOverlay(nav,camera,target,pixels,visible,name);
            if (atlasRoundTrip) CheckRouteViews(app,camera,target,pixels);
            if (site == null) return;
            if (natural)
            {
                if (TestRoute && site.name=="bosporus") CaptureIzmitTreeSource(app,map,camera,target,pixels,visible);
                // A second render disables optical shading. Only the waterMask
                // blend can change these natural-surface pixels (same camera/frame).
                float showWater = mat.GetFloat("_ShowWater");
                Require(showWater > .5f, "Player water optics were disabled");
                mat.SetFloat("_ShowWater",0);
                Color32[] plain;
                try { plain = Render(camera,target,pixels); }
                finally { mat.SetFloat("_ShowWater",showWater); }
                bool Wet(int x,int y)
                {
                    int i=y*Width+x;
                    var p=visible[i]; var q=plain[i];
                    return Math.Max(Math.Abs(p.r-q.r),Math.Max(Math.Abs(p.g-q.g),Math.Abs(p.b-q.b))) >= 5;
                }
                CheckPixels(nav,camera,name,site,visible,Wet);
            }
            else
            {
                // The map's sea fill is blue-gray; its dry relief is ochre.
                // Exclude dark river ink so a river cannot complete a sea route.
                bool Sea(int x,int y)
                {
                    var c = visible[y*Width+x];
                    return c.b-c.r > 12 && c.r > 103;
                }
                CheckPixels(nav,camera,name,site,visible,Sea);
            }
        }
        finally
        {
            camera.targetTexture=oldTarget;
            camera.pixelRect=oldRect;
            RenderTexture.active=oldActive;
            Object.DestroyImmediate(pixels);
            RenderTexture.ReleaseTemporary(target);
        }
    }
    static void CheckSetoViews(FlatWorldApp app, FlatMapRenderer map, Camera camera,
        RenderTexture target, Texture2D pixels, Vector2 location)
    {
        var nav=app.Navigation;
        var ui=app.GetComponentInChildren<UIDocument>().rootVisualElement;
        var local=nav.SurfaceMaterial;
        var shore=local.GetTexture("_EarthStraitShore");
        var detail=local.GetTexture("_EarthStraitDetail");
        Require(shore && detail && local.GetFloat("_EarthStraitReady")>.5f,"Missing local marine survey");
        void Readback(string name)
        {
            map.Configure(false,nav.PixelsPerTile);
            nav.SyncMaterials();
            var material=nav.SurfaceMaterial;
            Expect(material.shader.isSupported && material.GetFloat("_EarthStraitReady")>.5f &&
                material.GetTexture("_EarthStraitShore")==shore && material.GetTexture("_EarthStraitDetail")==detail,
                $"{name}: {material.shader.name} uses the same native {shore.width}x{shore.height} marine map and {detail.width}x{detail.height} surveyed banks");
            Render(camera,target,pixels);
            File.WriteAllBytes(Path.Combine(Output,name+".png"),pixels.EncodeToPNG());
        }
        double day=app.CalendarDay;
        Press(ui,"atlas");nav.Tick(3,day);
        Require(nav.IsAtlas,"Failed to reach the strategic atlas");
        Readback("seto-atlas");
        Press(ui,"mode");nav.Tick(3,day);
        Require(nav.IsFlat && !nav.IsAtlas,"Failed to return to the local chart");
        nav.GoGlobe(day);nav.Tick(3,day);
        nav.Recenter(Location(location),day);
        Require(nav.IsGlobe,"Failed to reach the globe camera");
        Readback("seto-globe");
    }
    static void CheckCompactMarineDepth(FlatMapRenderer map)
    {
        var native=map.Material.GetTexture("_EarthMarineDepth");
        if (!native || native.height<=4096) return;
        var loader=typeof(FlatMapRenderer).Assembly.GetType("Orbis.Flat.FlatEarthPolarRelief").GetMethod("LoadMarineDepth",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var compact=(Texture2D)loader.Invoke(null,new object[]{4096});
        Require(compact && compact.width==4096 && compact.height<=4096,"Marine depths exceed the requested 4096 texture limit");
        var probe=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/RiverOpticsProbe.shader"));
        var target=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var read=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
        var previous=RenderTexture.active;
        try
        {
            probe.SetVector("_MapSize",map.Material.GetVector("_MapSize"));
            probe.SetFloat("_TestShore",1);probe.SetFloat("_TestCoastKm",2);
            Color Sample(Texture texture)
            {
                probe.SetTexture("_EarthMarineDepth",texture);
                Graphics.Blit(null,target,probe);RenderTexture.active=target;
                read.ReadPixels(new Rect(0,0,1,1),0,0,false);return read.GetPixel(0,0);
            }
            ShaderUtil.CompilePass(probe,0,true);
            foreach(var p in new[]{Sites[2].throat,Sites[3].throat,Sites[4].throat,
                new Vector2(-73.60f,-41.80f),new Vector2(174.60f,-41.60f),new Vector2(36.62f,45.35f)})
            {
                probe.SetVector("_TestSite",new Vector4(p.x/360+.5f,p.y/180+.5f,0,0));
                probe.SetFloat("_TestOutput",10);
                var original=Sample(native);var packed=Sample(compact);
                Expect(original.g>.5f && Math.Abs(original.r-packed.r)<.04f && Math.Abs(original.g-packed.g)<.01f,
                    $"{p}: full and 4096-limit marine map GPU depths {original.r:F2} m / {packed.r:F2} m, optical weights {original.g:F2} / {packed.g:F2}");
                probe.SetFloat("_TestOutput",11);
                var raw=Sample(native);var compactRaw=Sample(compact);
                Expect(raw.r>0 && raw.g>0 && Math.Abs(raw.r-compactRaw.r)<.001f && Math.Abs(raw.g-compactRaw.g)<.00001f,
                    $"{p}: ETOPO source depth {raw.r:F4} / {compactRaw.r:F4} m, wet-data support {raw.g:F5} / {compactRaw.g:F5}");
            }
        }
        finally
        {
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(read);Object.DestroyImmediate(probe);Object.DestroyImmediate(compact);
        }
    }
    static void CheckGlobalShoreSamples(FlatMapRenderer map)
    {
        var source=map.Material;
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/PolarDetailProbe.shader");
        var probe=new Material(shader);
        var target=RenderTexture.GetTemporary(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var pixels=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
        var previous=RenderTexture.active;
        try
        {
            foreach(var key in new[]{"_EarthStraitShore","_EarthStraitDetail","_EarthSurveyMask","_VisualTex"})
                probe.SetTexture(key,source.GetTexture(key));
            probe.SetVector("_MapSize",source.GetVector("_MapSize"));
            probe.SetFloat("_EarthStraitReady",source.GetFloat("_EarthStraitReady"));
            probe.SetFloat("_EarthPolarReady",0);probe.SetFloat("_ProjectionProbe",7);
            ShaderUtil.CompilePass(probe,0,true);
            Color Sample(float lon,float lat)
            {
                probe.SetVector("_ProbeOrigin",new Vector4(lon/360+.5f,lat/180+.5f,0,0));
                Graphics.Blit(null,target,probe,0);RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1,1),0,0,false);return pixels.GetPixel(0,0);
            }
            // Each pair was classified independently by the pinned vector ocean.
            foreach(var s in new[]{
                ("Little Belt",9.70f,55.43f,9.78f,55.49f), ("Nevelskoy",141.61f,52.25f,141.69f,52.23f),
                ("Belle Isle",-56.53f,51.64f,-56.60f,51.68f), ("Magellan",-70.95f,-53.69f,-71.19f,-53.65f),
                ("Pemba",39.50f,-5.15f,39.72f,-5.11f), ("San Bernardino",124.24f,12.60f,124.31f,12.54f),
                ("Torres",142.25f,-10.50f,142.28f,-10.61f), ("Foveaux",168.26f,-46.67f,168.41f,-46.52f),
                ("Chacao",-73.73f,-41.80f,-73.70f,-41.85f)})
            {
                var wet=Sample(s.Item2,s.Item3);var dry=Sample(s.Item4,s.Item5);
                Expect(wet.b>.99f && dry.b>.99f && wet.r>0 && dry.r<0,
                    $"{s.Item1} native marine/dry bank pair, signed distances {wet.r:F4} / {dry.r:F4} tiles, weights {wet.b:F3} / {dry.b:F3}");
            }
        }
        finally
        {
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(pixels);Object.DestroyImmediate(probe);
        }
    }
    static void CaptureIzmitTreeSource(FlatWorldApp app, FlatMapRenderer map, Camera camera, RenderTexture target,
        Texture2D pixels, Color32[] original)
    {
        var world=app.World;
        var cornerA=Tile(world,new Vector2(29.35f,40.68f));
        var cornerB=Tile(world,new Vector2(29.87f,40.82f));
        var bounds=Bounds(cornerA,cornerB,2,world.Width,world.Height);
        var method=typeof(Orbis.Editor.VegetationArtRegression).GetMethod("ReadGeographicTreeRoots");
        Require(method!=null,"Missing native geographic tree-root probe");
        var roots=(Color[])method.Invoke(null,new object[]{app.Navigation.SurfaceMaterial,bounds});
        var coastTree=Tile(world,new Vector2(29.48868f,40.72220f));
        bool tested=false;
        var text=new StringBuilder("tile_x,tile_y,longitude,latitude,gpu_marine_km,eligible,toggled\n");
        for(int i=0;i<roots.Length;i++)
        {
            int x=bounds.x+i%bounds.width,y=bounds.y+i/bounds.width;
            Color r=roots[i];
            // Ensure the shader readback row and its source tile are aligned.
            Require(Tile(world,new Vector2(r.r,r.g))==new Vector2Int(x,y),"GPU tree root is outside its source tile");
            bool toggle=r.a>.5f && x==coastTree.x && y==coastTree.y;
            tested|=toggle;
            text.AppendFormat(CultureInfo.InvariantCulture,"{0},{1},{2:R},{3:R},{4:R},{5},{6}\n",
                x,y,r.r,r.g,r.b,r.a>.5f?1:0,toggle?1:0);
        }
        File.WriteAllText(Path.Combine(Output,"izmit-tree-roots.csv"),text.ToString());
        if(world.Width==4096)Expect(tested,"the original southern-bank Izmit root remains eligible for visible artwork");
        if(!tested)return;
        // At 4096 the legitimate tree on the southern bank projects a crown into the
        // gulf. Remove only its source ecology cover on the presentation texture.
        // The independent verifier determines which visible leaves this tree supplied.
        var texture=map.EcologyTexture;
        var saved=texture.GetPixel(coastTree.x,coastTree.y);
        try
        {
            var bare=saved;bare.r=0;
            texture.SetPixel(coastTree.x,coastTree.y,bare);texture.Apply(false,false);
            Render(camera,target,pixels);
            string file=$"izmit-without-tree-{coastTree.x}-{coastTree.y}.png";
            File.WriteAllBytes(Path.Combine(Output,file),pixels.EncodeToPNG());
        }
        finally {texture.SetPixel(coastTree.x,coastTree.y,saved);texture.Apply(false,false);}
        var restored=Render(camera,target,pixels);
        bool identical=true;
        for(int i=0;identical && i<original.Length;i++)identical=restored[i].Equals(original[i]);
        Expect(identical,$"restoring the isolated Izmit source tile {coastTree} restores the original camera pixels exactly");
    }
    static void CheckRouteViews(FlatWorldApp app,Camera camera,RenderTexture target,Texture2D pixels)
    {
        var nav=app.Navigation;
        var ui=app.GetComponentInChildren<UIDocument>().rootVisualElement;
        double day=app.CalendarDay;
        var saved=new List<PlanetLocation>(RoutePoints(nav));
        void CheckPoints(string stageName)
        {
            var current=RoutePoints(nav);
            bool same=current!=null && current.Count==saved.Count;
            for(int i=0;same && i<saved.Count;i++)
                same=current[i].Longitude==saved[i].Longitude && current[i].Latitude==saved[i].Latitude;
            Expect(same,$"{stageName} preserves all {saved.Count} original geographic route points exactly");
        }
        void CaptureRoute(string name)
        {
            var frame=Render(camera,target,pixels);
            File.WriteAllBytes(Path.Combine(Output,name+".png"),pixels.EncodeToPNG());
            CheckRouteOverlay(nav,camera,target,pixels,frame,name);
        }
        float initial=nav.PixelsPerTile;
        nav.Wheel(1,camera.pixelRect.center,day);
        nav.Tick(3,day);
        Expect(nav.IsFlat && !nav.IsAtlas && nav.PixelsPerTile<initial,"outward wheel changes the local map scale");
        CheckPoints("Local wheel zoom");
        Press(ui,"atlas");
        nav.Tick(3,day);
        Require(nav.IsAtlas,"The player atlas button did not open the strategic map");
        CheckPoints("Strategic atlas");
        CaptureRoute("marmara-strategic-sea-route");
        Press(ui,"mode");
        nav.Tick(3,day);
        Require(nav.IsFlat && !nav.IsAtlas,"The player view control did not return to the bookmarked local map");
        CheckPoints("Return from strategic atlas");
        CaptureRoute("marmara-returned-sea-route");
    }
    static void CheckRouteOverlay(FlatScrollNavigator nav,Camera camera,RenderTexture target,Texture2D pixels,
        Color32[] visible,string name)
    {
        string expectedMesh=nav.IsAtlas?"Wrapped path preview":"Clipped local route";
        MeshRenderer line=null;
        foreach(var filter in nav.GetComponentsInChildren<MeshFilter>())
            if(filter.sharedMesh && filter.sharedMesh.name==expectedMesh)
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                if(renderer && renderer.enabled && filter.sharedMesh.vertexCount>0) {line=renderer;break;}
            }
        Require(line!=null,"Player's visible route mesh is missing: "+expectedMesh);
        Color32[] hidden;
        line.enabled=false;
        try { hidden=Render(camera,target,pixels); }
        finally { line.enabled=true; }
        int marked=0;
        for(int i=0;i<visible.Length;i++)
        {
            var p=visible[i];var q=hidden[i];
            bool ink=Math.Max(Math.Abs(p.r-q.r),Math.Max(Math.Abs(p.g-q.g),Math.Abs(p.b-q.b)))>=12;
            hidden[i]=ink?new Color32(255,255,255,255):new Color32(0,0,0,255);
            if(ink)marked++;
        }
        pixels.SetPixels32(hidden);pixels.Apply();
        File.WriteAllBytes(Path.Combine(Output,name+"-ink.png"),pixels.EncodeToPNG());
        // A 395-km journey occupies only a few pixels on the fitted global chart.
        Expect(marked>(nav.IsAtlas?0:10),$"{name} native route mesh contributes {marked} visible camera pixels");
    }
    static void CheckPixels(FlatScrollNavigator nav, Camera camera, string name, Strait site,
        Color32[] colors, Func<int,int,bool> wet)
    {
        Vector2Int Pixel(Vector2 location)
        {
            Require(nav.TryActorSurface(Location(location),out var position,out _,out _,out _), "Point outside rendered surface: " + location);
            var screen = camera.WorldToScreenPoint(position);
            var p = new Vector2Int(Mathf.FloorToInt(screen.x),Mathf.FloorToInt(screen.y));
            Require(p.x>=0 && p.x<Width && p.y>=0 && p.y<Height, "Point outside camera: " + location + " -> " + p);
            return p;
        }
        string Sample(Vector2Int p) => $"{p} RGB({colors[p.y*Width+p.x].r},{colors[p.y*Width+p.x].g},{colors[p.y*Width+p.x].b}) wet={wet(p.x,p.y)}";
        var a = Pixel(site.mouthA); var b = Pixel(site.mouthB);
        var channel = Pixel(site.throat); var west = Pixel(site.bankA); var east = Pixel(site.bankB);
        var bounds = Bounds(a,b,32,Width,Height);
        Record($"{name} sampled pixels bottom-up: mouth A {Sample(a)}, mouth B {Sample(b)}, throat {Sample(channel)}, bank A {Sample(west)}, bank B {Sample(east)}");
        var across=((Vector2)(east-west)).normalized;
        int WetSteps(int sign)
        {
            int step=0;
            while (step<Width)
            {
                var p=Vector2Int.RoundToInt((Vector2)channel+sign*(step+1)*across);
                if (p.x<0 || p.y<0 || p.x>=Width || p.y>=Height || !wet(p.x,p.y)) break;
                step++;
            }
            return step;
        }
        int span=wet(channel.x,channel.y)?1+WetSteps(-1)+WetSteps(1):0;
        Record($"{name} contiguous wet transect along the two bank controls at {site.throat.x:F3}E/{site.throat.y:F3}N: {span} px");
        Expect(Connected(bounds,a,b,wet), $"{name} local four-neighbor screen-water path between mouths");
        Expect(wet(channel.x,channel.y), $"{name} surveyed throat is visibly wet");
        Expect(!wet(west.x,west.y) && !wet(east.x,east.y), $"{name} both sampled banks visibly dry");
        if (site.name == "dardanelles")
        {
            var neck = Pixel(new Vector2(26.76f,40.52f));
            var saros = Pixel(new Vector2(26.655f,40.52f));
            var marmara = Pixel(new Vector2(26.90f,40.52f));
            Expect(!wet(neck.x,neck.y), $"{name} Gallipoli isthmus stays dry");
            Expect(wet(saros.x,saros.y) && wet(marmara.x,marmara.y), $"{name} sampled seas either side of the isthmus are visibly wet");
        }
    }
}
