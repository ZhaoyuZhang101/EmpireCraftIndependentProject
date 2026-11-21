// Copy into Assets/ORBIS/Editor of a disposable project and run LargeWorldMenuProbe.Run
// in a D3D11 batch Editor without -quit. The product name must start with ORBIS-Scale-QA-.
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Orbis.Flat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

[InitializeOnLoad]
public static class LargeWorldMenuProbe
{
    const string Key="ORBIS.Scale.QA";
    static int stage,frames,paths,lastGameFrame;
    static double started,lastFrame,detailDay;
    static readonly double[] frameIntervals=new double[30];
    static ulong bedHash;
    static string globeScale,detailScale;
    static string Report=>SessionState.GetString(Key+".report","");
    static double Now=>EditorApplication.timeSinceStartup;
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static LargeWorldMenuProbe()
    {
        if(SessionState.GetBool(Key+".done",false))EditorApplication.update+=Close;
        else if(SessionState.GetBool(Key,false))EditorApplication.update+=Tick;
    }
    public static void Run()
    {
        Require(Application.isBatchMode&&Application.productName.StartsWith("ORBIS-Scale-QA-"),"Use an isolated QA product.");
        var args=Environment.GetCommandLineArgs();
        SessionState.SetString(Key+".report",args[Array.IndexOf(args,"-scaleReport")+1]);
        File.WriteAllText(Report,Application.unityVersion+" / "+SystemInfo.graphicsDeviceName+" / "+SystemInfo.graphicsDeviceType+Environment.NewLine);
        SessionState.SetBool(Key,true);
        SessionState.SetFloat(Key+".start",(float)Now);
        EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        EditorApplication.EnterPlaymode();
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryCounters
    {
        public uint Size,Faults;
        public UIntPtr PeakWorking,Working,PeakPaged,Paged,PeakNonPaged,NonPaged,Pagefile,PeakPagefile;
    }
    [DllImport("psapi.dll")]
    static extern bool GetProcessMemoryInfo(IntPtr process,out MemoryCounters counters,int size);
    static void Record(string message)
    {
        Require(GetProcessMemoryInfo(new IntPtr(-1),out var memory,Marshal.SizeOf<MemoryCounters>()),"Process memory counters unavailable.");
        File.AppendAllText(Report,$"{DateTime.UtcNow:HH:mm:ss} UTC {message}; working {memory.Working.ToUInt64()/1048576} MiB, peak {memory.PeakWorking.ToUInt64()/1048576} MiB"+Environment.NewLine);
    }
    static void Press(VisualElement ui,string id)
    {
        var button=ui.Q<Button>(id);
        button.Focus();
        using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=button;button.SendEvent(submit);}
    }
    static void SetPaused(VisualElement ui,bool paused)
    {
        var button=ui.Q<Button>("pause");
        if((button.text=="继续")!=paused)Press(ui,"pause");
        Require((button.text=="继续")==paused,"Unexpected pause state "+button.text);
    }
    static void Generate(FlatWorldApp app,VisualElement ui,TerrainPreset preset,int width)
    {
        var size=ui.Q<DropdownField>("size");
        size.index=size.choices.IndexOf(width+" × "+width/2);
        Require(size.index>=0,"Missing menu size "+width);
        ui.Q<DropdownField>("preset").index=(int)preset;
        ui.Q<TextField>("seed").value="SCALE-QA-"+width;
        started=Now;
        Press(ui,"generate");
        Require(app.Busy,"Generate ignored. "+ui.Q<Label>("notice").text);
    }
    static ulong VerifyEarth(WorldData world)
    {
        int width=world.Width,height=world.Height;
        var source=Resources.Load<TextAsset>("ORBIS/FlatWorld/Earth"+width).bytes;
        ulong hash=14695981039346656037ul;short min=0,max=0;
        for(int i=0;i<world.Count;i++)
        {
            int k=2*((height-1-i/width)*width+i%width);
            short expected=unchecked((short)(source[k]|source[k+1]<<8));
            if(world.Elevation[i]!=expected)throw new Exception("Relief/orientation mismatch at "+i);
            min=Math.Min(min,expected);max=Math.Max(max,expected);
            unchecked{hash=(hash^(ushort)expected)*1099511628211ul;hash=(hash^(byte)world.Water[i])*1099511628211ul;hash=(hash^(byte)world.Climates[i])*1099511628211ul;}
        }
        Require(min < -6000&&max>5000,"Missing high summits or deep ocean.");
        int Tile(double lon,double lat)=>world.Index((int)((lon+180)/360*width),(int)((lat+90)/180*height));
        int tarim=Tile(82.5,39),black=Tile(34,43),caspian=Tile(51,38);
        Require((world.Water[tarim]==WaterKind.Land||world.Water[tarim]==WaterKind.River)&&world.Elevation[tarim]==world.WaterLevel[tarim],"Tarim desert basin became a lake.");
        Require(world.Water[black]>=WaterKind.DeepOcean&&world.Water[black]<=WaterKind.ShallowOcean&&world.WaterLevel[black]==0,"Black Sea lost ocean connection.");
        Require(world.Water[caspian]==WaterKind.Lake&&world.WaterLevel[caspian]>=-32&&world.WaterLevel[caspian]<=-24,"Caspian water level invalid.");
        Record($"Tarim {world.Water[tarim]} bed/surface {world.Elevation[tarim]} m, Black Sea {world.Water[black]} surface {world.WaterLevel[black]} m, Caspian {world.Water[caspian]} surface {world.WaterLevel[caspian]} m");
        Record($"{world.Count:N0} Earth elevations match DEM, range {min} to {max} m, relief/water/climate FNV {hash:x16}");
        return hash;
    }
    static void CheckWorld(FlatWorldApp app,VisualElement ui,int width,TerrainPreset preset)
    {
        var w=app.World;
        Require(w.Width==width&&w.Height==width/2&&w.Preset==preset&&w.Seed=="SCALE-QA-"+width,"Incorrect world: "+ui.Q<Label>("notice").text);
        Require(w.Nations.Count>0&&w.WildlifeInitialized&&w.Wildlife.Count>0&&w.Facts!=null,"Missing playable world state.");
        Require(ui.Q<DropdownField>("size").value==width+" × "+width/2&&ui.Q<DropdownField>("preset").index==(int)preset,"Selectors not restored.");
        var map=app.GetComponentInChildren<FlatMapRenderer>();
        var pixels=map.TerrainTexture.GetPixelData<Color32>(0);
        var colors=new System.Collections.Generic.HashSet<Color32>();
        for(int i=0;i<pixels.Length;i+=Math.Max(1,pixels.Length/256))colors.Add(pixels[i]);
        Require(map.World==w&&map.TerrainTexture.width==width&&map.TerrainTexture.height==w.Height&&colors.Count>4,"Map texture publish");
        Record($"{preset} {width}x{w.Height} installed in {Now-started:F2}s, {w.Nations.Count} nations, {w.Wildlife.Count} animals");
        var sea=new double[-HeightBands.MinimumBed+1];var land=new double[HeightBands.MaximumBed+1];
        double all=0;
        for(int y=0;y<w.Height;y++)
        {
            double area=Math.Cos(((y+.5)/w.Height-.5)*Math.PI);
            all+=width*area;
            for(int x=0;x<width;x++)
            {
                int i=y*width+x;
                if(w.Water[i]>=WaterKind.DeepOcean&&w.Water[i]<=WaterKind.ShallowOcean)sea[-w.Elevation[i]]+=area;
                else if(w.Elevation[i]>0&&w.Water[i]!=WaterKind.Lake)land[w.Elevation[i]]+=area;
            }
        }
        int Quantile(double[] bins,double probability)
        {
            double threshold=bins.Sum()*probability,total=0;
            for(int i=0;i<bins.Length;i++)if((total+=bins[i])>=threshold)return i;
            return bins.Length-1;
        }
        string Quantiles(double[] bins)=>string.Join("/",new[]{.1,.5,.9,.99}.Select(q=>Quantile(bins,q).ToString()));
        Record($"Area-weighted ocean {100*sea.Sum()/all:F3}%, P10/50/90/99 depth {Quantiles(sea)} m, positive emergent terrain {Quantiles(land)} m");
    }
    static void Tick()
    {
        try
        {
            if(Now-SessionState.GetFloat(Key+".start",0)>1800)throw new TimeoutException("Stage "+stage);
            if(!EditorApplication.isPlaying)return;
            var app=UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if(!app||app.Busy||app.World==null)return;
            var ui=app.GetComponentInChildren<UIDocument>().rootVisualElement;
            var scale=ui.Q<Label>("mapScaleDistance");
            switch(stage)
            {
                case 0:
                    Require(new DirectoryInfo(Application.persistentDataPath).Name.StartsWith("ORBIS-Scale-QA-")&&!File.Exists(app.SavePath),"Save isolation.");
                    SetPaused(ui,true);
                    ui.Q<Button>("generate").GetFirstAncestorOfType<Foldout>().value=true;
                    Record("Default world ready. Save directory "+Application.persistentDataPath);
                    Generate(app,ui,TerrainPreset.GreatContinent,2048);
                    System.Threading.Thread.Sleep(40);
                    Generate(app,ui,TerrainPreset.Archipelago,1536);stage++;break;
                case 1:
                    CheckWorld(app,ui,1536,TerrainPreset.Archipelago);
                    Record("Immediate second Generate superseded the 2048 procedural request.");
                    Require(app.World.Tectonics!=null,"Missing procedural plate history.");
                    Generate(app,ui,TerrainPreset.Earth,2048);stage++;break;
                case 2:
                    CheckWorld(app,ui,2048,TerrainPreset.Earth);
                    bedHash=VerifyEarth(app.World);
                    started=Now;frames=0;stage++;break;
                case 3:
                    if(++frames<8)return;
                    globeScale=scale?.text;
                    Record("Globe scale "+globeScale);
                    var world=app.World;
                    var routes=app.GetComponentInChildren<FlatWildlifePathfinder>();
                    Action<int,int,bool> request=(start,end,flying)=>routes.RequestPath(start,end,flying,path=>{
                        if(path.Length<2||path[0]!=start||path[path.Length-1]!=end)return;
                        for(int i=1;i<path.Length;i++)if(!FlatWildlifeSimulation.CanStep(world,path[i-1],path[i],flying))return;
                        paths++;
                    });
                    int seam=world.Index(0,world.Height/2),pole=world.Index(13,world.Height-1);
                    request(seam,world.SphereNeighbor(seam,0),true);request(pole,world.SphereNeighbor(pole,3),true);
                    int ground=Array.FindIndex(world.Buildings,b=>b==Structure.Settlement);
                    bool asked=false;
                    for(int i=Math.Max(0,ground);i<world.Count&&!asked;i++)
                    {
                        int next=world.SphereNeighbor(i,1);
                        if(FlatWildlifeSimulation.CanStep(world,i,next,false)){request(i,next,false);asked=true;}
                    }
                    Require(asked,"No traversable ground sample.");
                    app.Navigation.Focus(app.World.Nations[0].Capital,app.CalendarDay);started=Now;stage++;break;
                case 4:
                    if(Now-started>90)throw new TimeoutException("Local focus or three wildlife A* routes: paths="+paths+" px="+app.Navigation.PixelsPerTile);
                    if(paths!=3||!app.Navigation.IsDetailed)return;
                    detailScale=scale?.text;
                    Record($"Local focus {app.Navigation.PixelsPerTile:F2} px/tile, scale {detailScale}, three verified A* routes (ground, date line, pole)");
                    var large=app.World;
                    var civil=app.Navigation.gameObject.AddComponent<FlatPlanetPathfinder>();
                    int capital=large.Nations[0].Capital,destination=-1;
                    for(int side=0;side<4;side++)
                    {
                        int adjacent=large.SphereNeighbor(capital,side);
                        if(large.Walkable(adjacent)){destination=adjacent;break;}
                    }
                    Require(destination>=0,"Capital route sample.");
                    double routeStart=Now;
                    var route=civil.Find(large,capital,destination);
                    Require(route.Length>=2&&route[0]==capital&&route[route.Length-1]==destination,"Civilization A* route invalid.");
                    Record($"Explicit civilization route ready in {Now-routeStart:F2}s");
                    frames=0;lastGameFrame=Time.frameCount;lastFrame=Now;detailDay=app.CalendarDay;
                    SetPaused(ui,false);started=Now;stage++;break;
                case 5:
                    if(Now-started>90)throw new TimeoutException("Game frame "+Time.frameCount+", samples "+frames+", pause "+ui.Q<Button>("pause").text);
                    if(Time.frameCount==lastGameFrame)return;
                    lastGameFrame=Time.frameCount;
                    frameIntervals[frames++]=(Now-lastFrame)*1000;lastFrame=Now;
                    if(frames<frameIntervals.Length)return;
                    Array.Sort(frameIntervals);
                    Require(app.CalendarDay>detailDay,"Simulation clock did not advance.");
                    Record($"Unpaused native editor, 30 local update intervals, median {frameIntervals[15]:F1} ms, p95 {frameIntervals[28]:F1} ms; +{app.CalendarDay-detailDay:F4} days");
                    SetPaused(ui,true);
                    var camera=app.GetComponentInChildren<Camera>();app.Navigation.Wheel(4,camera.pixelRect.center,app.CalendarDay);
                    started=Now;stage++;break;
                case 6:
                    if(Now-started<1.5)return;
                    Record($"After wheel {app.Navigation.PixelsPerTile:F2} px/tile, scale {scale?.text}");
                    Press(ui,"tabFiles");started=Now;Press(ui,"save");
                    long length=new FileInfo(app.SavePath).Length;
                    Require(length>250L*1024*1024&&length<FlatSave.MaxFileBytes,"Wrong save length "+length);
                    Record($"V3 file {length} bytes written in {Now-started:F2}s");
                    Press(ui,"tabWorld");Generate(app,ui,TerrainPreset.Continents,256);stage++;break;
                case 7:
                    CheckWorld(app,ui,256,TerrainPreset.Continents);
                    Press(ui,"tabFiles");started=Now;Press(ui,"load");Require(app.Busy,"Load ignored.");stage++;break;
                case 8:
                    CheckWorld(app,ui,2048,TerrainPreset.Earth);
                    Require(VerifyEarth(app.World)==bedHash,"Largest save changed during reload.");
                    Require(!string.IsNullOrWhiteSpace(globeScale)&&!string.IsNullOrWhiteSpace(detailScale)&&globeScale!=detailScale,"Scale did not follow zoom.");
                    started=Now;Press(ui,"load");Require(app.Busy,"Second load ignored.");stage++;break;
                case 9:
                    CheckWorld(app,ui,2048,TerrainPreset.Earth);
                    Require(VerifyEarth(app.World)==bedHash,"Same-size reload changed the world.");
                    Record("PASS real menu, generation, textures, A* workers, local focus/wheel, native save/load, large-to-large replacement.");Finish(0);break;
            }
        }
        catch(Exception e){Record("FAIL stage "+stage+" "+e);UnityEngine.Debug.LogException(e);Finish(1);}
    }
    static void Finish(int code)
    {
        SessionState.EraseBool(Key);SessionState.SetBool(Key+".done",true);SessionState.SetInt(Key+".code",code);
        EditorApplication.update-=Tick;EditorApplication.update+=Close;EditorApplication.ExitPlaymode();
    }
    static void Close()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        EditorApplication.update-=Close;SessionState.EraseBool(Key+".done");EditorApplication.Exit(SessionState.GetInt(Key+".code",1));
    }
}
