// Copy into the Editor directory of an isolated QA project. Run with -batchmode
// -force-d3d11 -executeMethod RiverMapPlayProbe.Run -riverOutput <directory>.
using System;
using System.IO;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

[InitializeOnLoad]
public static class RiverMapPlayProbe
{
    const string Key="ORBIS.QA.RiverMap";
    static int stage,frame=-1;
    static string Output=>SessionState.GetString(Key+".output","");
    static string Report=>Path.Combine(Output,"play.txt");
    static RiverMapPlayProbe(){if(SessionState.GetBool(Key,false))EditorApplication.update+=Tick;}
    static void Check(bool ok,string message)
    {
        if(!ok)throw new InvalidOperationException(message);
        File.AppendAllText(Report,"PASS "+message+Environment.NewLine);
    }
    public static void Run()
    {
        if(!Application.isBatchMode||!Application.productName.StartsWith("ORBIS-River-QA-"))
            throw new InvalidOperationException("Use an isolated river QA product and save directory.");
        var args=Environment.GetCommandLineArgs();
        SessionState.SetString(Key+".output",args[Array.IndexOf(args,"-riverOutput")+1]);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Report,Application.unityVersion+" / "+SystemInfo.graphicsDeviceName+" / "+SystemInfo.graphicsDeviceType+Environment.NewLine);
        SessionState.SetBool(Key,true);SessionState.SetFloat(Key+".start",(float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
    }
    static void Press(VisualElement ui,string id)
    {
        var button=ui.Q<Button>(id);
        using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}
    }
    static void Capture(Camera camera,string name,FlatMapRenderer chart=null)
    {
        int h=chart?1024:720,w=chart?2048:Mathf.RoundToInt(camera.aspect*h);
        var rt=RenderTexture.GetTemporary(w,h,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var old=RenderTexture.active;var previous=camera.targetTexture;var rect=camera.pixelRect;
        var pixels=new Texture2D(w,h,TextureFormat.RGB24,false);
        try
        {
            if(chart)Graphics.Blit(chart.DisplayTexture,rt,chart.Material);
            else {camera.targetTexture=rt;camera.rect=new Rect(0,0,1,1);camera.Render();}
            RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,w,h),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output,name+".png"),pixels.EncodeToPNG());
            if(chart && chart.World.Preset==TerrainPreset.Earth)
            {
                foreach(var site in new[]{new Vector2(-64.59610f,-3.38771f),new Vector2(31.15732f,27.21857f),
                    new Vector2(107.51979f,29.88732f),new Vector2(106.69972f,57.04511f),new Vector2(-159.77383f,63.05154f)})
                {
                    int x=(int)((site.x/360+.5f)*w),y=(int)((site.y/180+.5f)*h);
                    Color c=pixels.GetPixel(x,y);
                    Check(c.b-c.r>.10f,$"river ink on GPU at {site} (R={c.r:F2}, B={c.b:F2})");
                }
                Color desert=pixels.GetPixel((int)((15f/360+.5f)*w),(int)((23f/180+.5f)*h));
                Check(desert.r>desert.b,"Sahara control renders land");
            }
        }
        finally
        {
            camera.targetTexture=previous;camera.pixelRect=rect;RenderTexture.active=old;
            UnityEngine.Object.DestroyImmediate(pixels);RenderTexture.ReleaseTemporary(rt);
        }
    }
    static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".start",0)>420)throw new TimeoutException("Stage "+stage);
            if(!EditorApplication.isPlaying||frame==Time.frameCount)return;
            var app=UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            if(!app||app.Busy||app.World==null)return;
            frame=Time.frameCount;
            var ui=app.GetComponentInChildren<UIDocument>().rootVisualElement;
            var nav=app.Navigation;var camera=app.GetComponentInChildren<Camera>();
            var map=app.GetComponentInChildren<FlatMapRenderer>();
            var layer=ui.Q<DropdownField>("layer");var material=nav.SurfaceMaterial;
            switch(stage)
            {
                case 0:
                    ShaderUtil.allowAsyncCompilation=false;
                    layer.index=(int)MapLayer.Rivers;
                    ui.Q<DropdownField>("size").index=1;
                    ui.Q<DropdownField>("preset").index=(int)TerrainPreset.Earth;
                    Press(ui,"generate");break;
                case 1:
                    if(map.VisibleLayer!=MapLayer.Rivers||material.GetFloat("_RiverOverviewReady")<.5f)return;
                    Check(app.World.Preset==TerrainPreset.Earth&&app.World.Width==512&&app.World.Facts.RiverCurves.HasSurvey,"Earth512 generated with real courses and retained selected layer");
                    if(ui.Q<Button>("pause").text!="继续")Press(ui,"pause");
                    nav.Recenter(new PlanetLocation(-62*Math.PI/180,-8*Math.PI/180),app.CalendarDay);
                    break;
                case 2:
                    Check(nav.IsGlobe&&material.GetFloat("_RiverMap")>.5f,"river layer on globe");
                    Check(material.GetFloat("_EarthLakeReady")>.5f,"globe bound the surveyed lake shores");
                    Capture(camera,"earth-globe");nav.ToggleStrategic(app.CalendarDay);break;
                case 3:
                    Check(nav.IsAtlas&&material.GetFloat("_RiverMap")>.5f,"river layer on strategic atlas");
                    Check(material.GetFloat("_EarthLakeReady")>.5f,"strategic atlas bound the surveyed lake shores");
                    Capture(camera,"earth-atlas");nav.ToggleStrategic(app.CalendarDay);
                    nav.Wheel(-24,camera.pixelRect.center,app.CalendarDay);nav.Tick(3,app.CalendarDay);
                    nav.Wheel(-24,camera.pixelRect.center,app.CalendarDay);nav.Tick(3,app.CalendarDay);
                    nav.Recenter(new PlanetLocation(-64.596099*Math.PI/180,-3.387709*Math.PI/180),app.CalendarDay);
                    nav.Wheel(8,camera.pixelRect.center,app.CalendarDay);nav.Tick(3,app.CalendarDay);break;
                case 4:
                    if(!map.RiverAtlasReady)return;
                    Check(nav.IsFlat&&material.GetFloat("_RiverMap")>.5f,"river layer on local map");
                    CheckAmazonChannel(map);
                    Capture(camera,"earth-regional");Capture(camera,"earth-river-map",map);
                    CheckLakeRegion(map,"huron",-82.5f,45f,7f,new Vector2(-82.55f,44.90f),new Vector2(-82.33f,45.80f));
                    CheckLakeRegion(map,"taymyr",101.9f,74.5f,16f,new Vector2(101.6f,74.55f),new Vector2(102.25f,74.50f));
                    CheckLakeRegion(map,"balaton",17.8f,46.85f,1.7f,new Vector2(18.083f,47.0f),new Vector2(17.79f,47.04f));
                    CheckLakeRegion(map,"jianghuai",118.75f,32.15f,6.2f,new Vector2(117.608f,31.526f),new Vector2(118.20f,32.55f),
                        new Vector2(120.127f,31.195f),new Vector2(118.720f,33.300f),new Vector2(119.340f,32.841f));
                    CheckLakeRegion(map,"poyang",116.22f,29.22f,2.6f,new Vector2(116.221f,29.137f),new Vector2(116.64f,29.30f));
                    CheckLakeRegion(map,"dongting",112.58f,29.15f,2.7f,new Vector2(112.217f,28.920f),new Vector2(111.98f,29.59f),
                        new Vector2(112.930f,29.361f));
                    CheckLakeRegion(map,"yunnan",101.54f,25.18f,4.1f,new Vector2(102.721f,24.849f),new Vector2(102.823f,24.662f),
                        new Vector2(100.182f,25.783f),new Vector2(102.887f,24.494f));
                    CheckLakeRegion(map,"yellow-headwaters",97.49f,34.93f,1.5f,new Vector2(97.286f,34.928f),new Vector2(97.45f,34.91f),
                        new Vector2(97.631f,34.878f));
                    CheckLakeRegion(map,"sayram",81.18f,44.61f,1.22f,new Vector2(81.180f,44.609f),new Vector2(81.57f,44.62f));
                    CheckLakeRegion(map,"xingkai",132.43f,45.0f,2.4f,new Vector2(132.409f,45.032f),new Vector2(133.07f,45.02f));
                    layer.index=(int)MapLayer.Natural;ui.Q<Toggle>("clouds").value=false;break;
                case 5:
                    if(map.VisibleLayer!=MapLayer.Natural||material.GetFloat("_RiverMap")>.5f)return;
                    Check(material.GetFloat("_RiverSurvey")>.5f&&material.GetFloat("_WaterView")>.5f,"natural surface receives surveyed water");
                    Capture(camera,"earth-natural");
                    float dayNight=material.GetFloat("_FlatDayNight");
                    material.SetFloat("_FlatDayNight",0);
                    try {Capture(camera,"earth-natural-daylight");} finally {material.SetFloat("_FlatDayNight",dayNight);}
                    ui.Q<TextField>("path").value=Path.Combine(Output,"export-height.png");
                    Press(ui,"export");
                    var export=Orbis.OrbisRasterCodec.Read(File.ReadAllBytes(Path.Combine(Output,"export-height.rivers.png")));
                    int ex=(int)((-64.596099/360+.5)*export.width),ey=(int)((.5+3.387709/180)*export.height);
                    Check(export.width==2048&&export.height==1024&&export.rgba[(ey*export.width+ex)*4+3]>70&&export.rgba[3]==0,
                        "north-first transparent river PNG from the player export button");
                    Check(!File.Exists(app.SavePath),"empty isolated save slot");
                    Press(ui,"save");Check(File.Exists(app.SavePath),"saved Earth through game menu");
                    layer.index=(int)MapLayer.Rivers;Press(ui,"load");break;
                case 6:
                    if(map.VisibleLayer!=MapLayer.Rivers||material.GetFloat("_RiverOverviewReady")<.5f)return;
                    Check(app.World.RiverReference!=null&&app.World.Facts.RiverCurves.HasSurvey,"save reload restored geographic river network and layer");
                    Check(material.GetFloat("_EarthLakeReady")>.5f,"Earth reload rebound the surveyed lake shores");
                    ui.Q<DropdownField>("size").index=0;ui.Q<DropdownField>("preset").index=(int)TerrainPreset.Continents;
                    ui.Q<TextField>("seed").value="ORBIS-RIVERS-QA";Press(ui,"generate");break;
                case 7:
                    if(!map.RiverAtlasReady||map.VisibleLayer!=MapLayer.Rivers||material.GetFloat("_RiverOverviewReady")<.5f)return;
                    Check(app.World.RiverReference==null&&material.GetFloat("_RiverSurvey")<.5f&&app.World.Facts.RiverCurves.SegmentCount>0,
                        "random world uses its own simulated channels and retains river layer");
                    Check(material.GetFloat("_EarthLakeReady")<.5f,"procedural water has no Earth lake override");
                    Capture(camera,"random-river-map",map);
                    Orbis.Editor.RiverAtlasRegression.Run();Orbis.Editor.RiverOpticsRegression.Run();
                    Orbis.Editor.RiverGeometryRegression.Run();
                    Check(true,"existing river atlas, geometry and optics GPU suites");
                    foreach(string path in new[]{"FlatWorld/FlatMap","FlatWorld/LocalSurface","FlatWorld/ScrollSurface","Surface"})
                    {
                        var shader=Resources.Load<Shader>("ORBIS/"+path);var probe=new Material(shader);
                        try
                        {
                            for(int p=0;p<probe.passCount;p++)ShaderUtil.CompilePass(probe,p,true);
                            Check(shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"compiled "+path);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(probe); }
                    }
                    Finish(0);return;
            }
            stage++;
        }
        catch(Exception e){File.AppendAllText(Report,"FAIL "+stage+" "+e+Environment.NewLine);Debug.LogException(e);Finish(1);}
    }
    static void Finish(int code)
    {SessionState.EraseBool(Key);EditorApplication.update-=Tick;EditorApplication.Exit(code);}

    // Draw the player's actual river-map pass at regional scale. The fixed water
    // and dry probes include an island, a high-latitude shore and a sub-cell lake.
    static void CheckLakeRegion(FlatMapRenderer map,string name,float lon,float lat,float lonSpan,Vector2 wet,Vector2 dry,params Vector2[] otherWater)
    {
        const int width=800,height=480;
        float latSpan=lonSpan*Mathf.Cos(lat*Mathf.Deg2Rad)*height/width;
        var source=new Rect(lon-lonSpan*.5f,lat-latSpan*.5f,lonSpan,latSpan);
        var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var previous=RenderTexture.active;
        var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
        var material=new Material(map.Material);
        try
        {
            RenderTexture.active=target;
            GL.Clear(true,true,Color.magenta);
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                material.SetFloat("_PixelsPerTile",height*180f/(map.World.Height*latSpan));
                material.SetFloat("_Grid",0);
                if(!material.SetPass(0))throw new InvalidOperationException("Lake chart pass unavailable");
                void Corner(float x,float y)
                {
                    GL.TexCoord2((source.x+x*source.width)/360+.5f,(source.y+y*source.height)/180+.5f);
                    GL.Vertex3(x,y,0);
                }
                GL.Begin(GL.QUADS);
                Corner(0,0);Corner(1,0);Corner(1,1);Corner(0,1);
                GL.End();
            }
            finally {GL.PopMatrix();}
            pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output,"lake-"+name+".png"),pixels.EncodeToPNG());
            Color At(Vector2 geo)=>pixels.GetPixel((int)((geo.x-source.x)/source.width*width),(int)((geo.y-source.y)/source.height*height));
            Color water=At(wet),land=At(dry);
            Check(water.b-water.r>.09f&&land.r>land.b,
                $"{name} shows open lake and dry shore/island on GPU (B-R {water.b-water.r:F2}, {land.b-land.r:F2})");
            foreach(var geo in otherWater)
            {
                Color c=At(geo);
                Check(source.Contains(geo)&&c.b-c.r>.09f,$"{name} open water on GPU at {geo} (B-R {c.b-c.r:F2})");
            }
            if(name=="jianghuai")
            {
                foreach(var geo in new[]{new Vector2(120.206f,31.924f),new Vector2(121.500f,31.483f)})
                {
                    Color c=At(geo);
                    Check(c.r<.23f&&c.b-c.r>.20f,$"Yangtze estuary channel on GPU at {geo}");
                }
                Color island=At(new Vector2(121.580f,31.622f));
                Check(island.r>.30f,"Yangtze river ink stays off the interior of Chongming Island");
            }
            if(name=="balaton")
            {
                // Two source-polygon cross-sections previously faded into land between the basins.
                foreach(var geo in new[]{new Vector2(17.602f,46.80095f),new Vector2(17.900f,46.93417f)})
                {
                    Color neck=At(geo);
                    Check(neck.b-neck.r>.09f,$"Balaton narrows stays blue at {geo} (B-R {neck.b-neck.r:F2})");
                }
            }
        }
        finally
        {
            RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(pixels);
            RenderTexture.ReleaseTemporary(target);
        }
    }

    static void CheckAmazonChannel(FlatMapRenderer map)
    {
        // Arbitrary sub-texel receivers reveal shifted pages which the lattice
        // node samples in the general atlas regression do not exercise.
        const int side=256;
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ORBIS/Editor/RiverAtlasProbe.shader");
        var probe=new Material(shader);probe.CopyPropertiesFromMaterial(map.Material);
        probe.SetTexture("_RiverAtlas",map.RiverAtlasTexture);
        var layout=map.Material.GetVector("_RiverAtlasLayout");
        probe.SetVector("_RiverAtlasLayout",layout);probe.SetFloat("_RiverAtlasReady",1);
        var size=map.Material.GetVector("_MapSize");probe.SetVector("_MapSize",size);
        var origin=new Vector2((float)((-64.596099/360+.5)*size.x)-4,(float)((-3.387709/180+.5)*size.y)-4);
        probe.SetVector("_ProbeOrigin",origin);probe.SetVector("_ProbeSpan",new Vector4(8,8));
        var rt=RenderTexture.GetTemporary(side,side,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var old=RenderTexture.active;var pixels=new Texture2D(side,side,TextureFormat.RGBAFloat,false,true);
        try
        {
            Graphics.Blit(Texture2D.whiteTexture,rt,probe);
            RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,side,side),0,0);pixels.Apply();
            var actual=pixels.GetPixels();int wet=0,missed=0;string example="";
            for(int y=0;y<side;y++)for(int x=0;x<side;x++)
            {
                float cx=origin.x+(x+.5f)*8/side,cy=origin.y+(y+.5f)*8/side;
                var sample=map.World.Facts.RiverCurves.Sample(cx,cy);
                if(sample.HalfWidth<.045f||sample.Distance>-.020f)continue;
                wet++;
                float distance=actual[y*side+x].r;
                if(distance>.006f)
                {
                    if(missed++<3)example+=$" ({cx:F3},{cy:F3}: cpu={sample.Distance:F3}, gpu={distance:F3})";
                }
            }
            Check(wet>100&&missed==0,$"Amazon cross-tile continuity on GPU, {wet} interior receivers, missed {missed}; layout {layout};{example}");
        }
        finally
        {
            RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(probe);
        }
    }
}
