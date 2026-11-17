using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
namespace Orbis.Flat
{
    [DefaultExecutionOrder(-100)]
    public sealed class FlatWorldApp : MonoBehaviour
    {
        public string initialSeed="ORBIS-2D-2026";
        public int initialWidth=512;
        public WorldData World { get; private set; }
        public FlatSimulation Simulation { get; private set; }
        FlatScrollNavigator navigation;
        readonly FlatSolarClock solarClock = new FlatSolarClock();
        Action<float> simulationStep;
        bool screenshotMode; double visualClock;
        public double CalendarDay { get { return World==null?0:solarClock.RenderDay(World.Day); } }
        Camera viewCamera;FlatMapRenderer map;FlatGlobePresenter globe;UIDocument document;PanelSettings panel;Font font;
        VisualElement root,viewport;Label info,stats,notice,nations;Button modeButton,pauseButton,speedButton;
        TextField seedField,pathField;DropdownField sizeField,presetField,layerField,toolField,heightField,brushField;
        Toggle gridToggle,cloudToggle,contourToggle;
        DropdownField cloudQuality;Label legend,waterStatus,skyStatus;
        readonly string[] tabNames={"World","Terrain","Civs","Files"};
        Task<WorldData> pending;CancellationTokenSource cancellation;bool seedOnInstall,paused,globeMode,dragging,moved;
        int selected=-1,activeNation=-1,pointer=-1,lastMonth=-1;float pixelsPerTile=2,speed=1,hudAge;
        Vector2 pan,lastPointer,downPointer;string status="准备世界数据…";int[] route=new int[0];
        public bool Busy { get { return pending!=null; } }
        public FlatScrollNavigator Navigation { get { return navigation; } }
        public bool GlobeMode { get { return globeMode; } }
        public string SavePath { get { return Path.Combine(Application.persistentDataPath,"orbis-flat-world-v1.orbis"); } }
        string HeightPath { get { return string.IsNullOrWhiteSpace(pathField.value)?Path.Combine(Application.persistentDataPath,"orbis-height16.png"):pathField.value.Trim(); } }
        void Awake()
        {
            // Pipeline detection first. This branch preserves the baseline's Built-in shaders.
            if(GraphicsSettings.currentRenderPipeline!=null)
            {Debug.LogError("ORBIS 2D-first full package uses the Built-in Render Pipeline. Open its own project; do not merge into URP/HDRP without shader migration.");enabled=false;return;}
            Application.targetFrameRate=60;
            var cameraObject=new GameObject("RTS / globe presentation camera",typeof(Camera),typeof(AudioListener));cameraObject.transform.SetParent(transform,false);
            viewCamera=cameraObject.GetComponent<Camera>();viewCamera.tag="MainCamera";viewCamera.clearFlags=CameraClearFlags.SolidColor;viewCamera.backgroundColor=new Color(.045f,.075f,.09f);
            viewCamera.allowMSAA=false;viewCamera.allowDynamicResolution=false;viewCamera.depthTextureMode=DepthTextureMode.Depth;
            QualitySettings.antiAliasing=0;
            var ui=new GameObject("2D World HUD",typeof(UIDocument));ui.transform.SetParent(transform,false);document=ui.GetComponent<UIDocument>();
            panel=Instantiate(Resources.Load<PanelSettings>("ORBIS/OrbisPanel"));panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1440,900);panel.match=.5f;
            panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("ORBIS/OrbisTheme");document.panelSettings=panel;
            var tree=Resources.Load<VisualTreeAsset>("ORBIS/FlatWorld/FlatWorld");if(!tree)throw new InvalidOperationException("FlatWorld.uxml is missing.");
            root=document.rootVisualElement;tree.CloneTree(root);
            font=OrbisSystemFont.Create();if(font)root.style.unityFontDefinition=FontDefinition.FromFont(font);
            BindUI();Generate();
        }
        void Bind(string id,Action action)
        {
            var b=root.Q<Button>(id);if(b==null)throw new InvalidOperationException("Missing UI button: "+id);
            b.clicked+=()=>{try{action();}catch(Exception e){SetStatus(e.Message);Debug.LogException(e);}};
        }
        void BindUI()
        {
            viewport=root.Q("viewport");info=root.Q<Label>("tileInfo");stats=root.Q<Label>("stats");notice=root.Q<Label>("notice");nations=root.Q<Label>("nations");
            seedField=root.Q<TextField>("seed");seedField.value=initialSeed;seedField.maxLength=128;
            sizeField=root.Q<DropdownField>("size");sizeField.choices=new List<string>{"256 × 128","512 × 256","1024 × 512"};sizeField.index=initialWidth>=1024?2:initialWidth>=512?1:0;
            presetField=root.Q<DropdownField>("preset");presetField.choices=new List<string>{"大陆与群山","群岛","超级大陆","内海世界"};presetField.index=0;
            layerField=root.Q<DropdownField>("layer");layerField.choices=new List<string>{"自然地表","文明版图","20 档高度","Köppen 柯本气候","本月温度","本月降水","海水与淡水","土地肥力","地貌 / 丘陵高原","地表材料 / 沙泥砾岩","土壤桶湿度","河流 / 河口悬沙","生态植被 / 14 类"};layerField.index=0;
            layerField.RegisterValueChangedCallback(e=>{if(map){map.SetLayer((MapLayer)layerField.index);UpdateHUD();}});
            toolField=root.Q<DropdownField>("tool");toolField.choices=new List<string>{"选择 Tile / Zone","建立文明","相邻 Zone 扩张","预设高度笔刷","修建农田","修建道路","修建桥梁","路径查看（首都出发）"};toolField.index=0;
            heightField=root.Q<DropdownField>("height");heightField.choices=new List<string>{"深海床 −2200 m","中海床 −600 m","浅海床 −80 m"};
            for(int i=1;i<=20;i++)heightField.choices.Add("L"+i.ToString("00")+" · "+HeightBands.Preset(i)+" m");heightField.index=3;
            brushField=root.Q<DropdownField>("brush");brushField.choices=new List<string>{"1 Tile","1 Zone · 4×4","3×3 Tiles","9×9 Tiles"};brushField.index=0;
            pathField=root.Q<TextField>("path");pathField.value=Path.Combine(Application.persistentDataPath,"orbis-height16.png");
            gridToggle=root.Q<Toggle>("grid");gridToggle.value=false;cloudToggle=root.Q<Toggle>("clouds");cloudToggle.value=true;
            modeButton=root.Q<Button>("mode");pauseButton=root.Q<Button>("pause");speedButton=root.Q<Button>("speed");
            Bind("generate",Generate);Bind("seedCivs",()=>{if(Ready()){int n=Simulation.SeedNations(4);map.Flush();SetStatus("新增 "+n+" 个文明。领地按 4×4 Zone 扩展。 ");}});
            Bind("pause",()=>{paused=!paused;UpdateHUD();});Bind("speed",()=>{speed=speed==1?4:speed==4?12:speed==12?20:1;UpdateHUD();});
            Bind("atlas",OpenAtlas);
            Bind("mode",ToggleMode);Bind("home",Home);Bind("focus",Focus);Bind("save",Save);Bind("load",Load);
            Bind("export",()=>{if(Ready()){FlatRasterIO.Export(World,HeightPath);SetStatus("已导出 16-bit 高度图、水体预览与元数据："+HeightPath);}});
            Bind("import",Import);Bind("rebuild",()=>{if(Ready()){solarClock.Flush(simulationStep);var copy=World.Clone();Queue(c=>{FlatGenerator.Rebuild(copy,c);return copy;},"重新计算排水、月均气候与生物群系…");}});
            Bind("shot",()=>{if(World!=null&&!Busy)StartCoroutine(Capture());});
            Bind("folder",()=>Application.OpenURL(new Uri(Application.persistentDataPath+Path.DirectorySeparatorChar).AbsoluteUri));
            BindGroupedUI();
            viewport.focusable=true;
            viewport.RegisterCallback<PointerDownEvent>(e=>{dragging=true;moved=false;pointer=e.pointerId;lastPointer=downPointer=e.position;viewport.CapturePointer(pointer);viewport.Focus();e.StopPropagation();});
            viewport.RegisterCallback<PointerMoveEvent>(e=>
            {
                if(!dragging)return;Vector2 now=e.position;if(Vector2.Distance(now,downPointer)>4)moved=true;
                if(moved){Vector2 delta=now-lastPointer;if(navigation)navigation.Drag(new Vector2(delta.x,-delta.y)*(Screen.height/Mathf.Max(1,root.resolvedStyle.height)));}
                lastPointer=now;
            });
            viewport.RegisterCallback<PointerUpEvent>(e=>
            {
                if(!dragging||e.pointerId!=pointer)return;dragging=false;viewport.ReleasePointer(pointer);pointer=-1;
                if(!moved&&e.button==0)Pick(e.position);else if(!moved&&e.button==1)ShowRoute();
            });
            viewport.RegisterCallback<PointerCaptureOutEvent>(e=>{dragging=false;pointer=-1;});
            viewport.RegisterCallback<WheelEvent>(e=>{Zoom(e.delta.y,e.mousePosition);e.StopPropagation();});
            viewport.RegisterCallback<KeyDownEvent>(e=>
            {
                if(e.keyCode==KeyCode.Space){paused=!paused;UpdateHUD();}
                if(e.keyCode==KeyCode.M)ToggleMode();if(e.keyCode==KeyCode.F)Focus();if(e.keyCode==KeyCode.Home)Home();
                float move=48/pixelsPerTile;
                if(navigation&&(navigation.IsFlat||navigation.IsTransitioning))
                {
                    Vector2 delta=Vector2.zero;
                    if(e.keyCode==KeyCode.A||e.keyCode==KeyCode.LeftArrow)delta.x-=move;
                    if(e.keyCode==KeyCode.D||e.keyCode==KeyCode.RightArrow)delta.x+=move;
                    if(e.keyCode==KeyCode.W||e.keyCode==KeyCode.UpArrow)delta.y+=move;
                    if(e.keyCode==KeyCode.S||e.keyCode==KeyCode.DownArrow)delta.y-=move;
                    navigation.PanBy(delta);
                }
                if(e.keyCode==KeyCode.C)contourToggle.value=!contourToggle.value;
                if(e.keyCode==KeyCode.G)gridToggle.value=!gridToggle.value;
            });
        }
        void BindGroupedUI()
        {
            legend=root.Q<Label>("legend");waterStatus=root.Q<Label>("waterStatus");skyStatus=root.Q<Label>("skyStatus");
            contourToggle=root.Q<Toggle>("contours");contourToggle.value=true;
            cloudQuality=root.Q<DropdownField>("cloudQuality");cloudQuality.choices=new List<string>{"性能","均衡","精细"};cloudQuality.index=1;
            foreach(string name in tabNames){string tab=name;Bind("tab"+name,()=>ShowTab(tab));}
            ShowTab("World");
        }
        void ShowTab(string selectedTab)
        {
            foreach(string name in tabNames)
            {
                root.Q("panel"+name).EnableInClassList("is-hidden",name!=selectedTab);
                root.Q<Button>("tab"+name).EnableInClassList("active-tab",name==selectedTab);
            }
        }
        void ChooseLayer(MapLayer layer) { layerField.index=(int)layer; }
        void ApplyWaterSettings(Material target)
        {
            if(!target)return;
            Orbis.Planet.OrbisAssetVisuals.BindWater(target);
            target.SetFloat("_ShowWater",true?1:0);target.SetFloat("_ShowBanks",true?1:0);
            target.SetFloat("_WaveAmount",1f);
            target.SetFloat("_SmoothTerrain",true?1:0);target.SetFloat("_Vegetation",true?1:0);
            target.SetFloat("_Bathymetry",true?1:0);target.SetFloat("_Relief",true?1:0);
            {
                target.SetFloat("_ShowContours",contourToggle.value?1:0);
                int interval=100;
                target.SetFloat("_ContourInterval",interval);
            }
        }
        void BindSky(Material target,Vector3 sun)
        {
            // At the local-map endpoint the HP radial cache represents overhead
            // clouds while the globe endpoint keeps the full 3D HP ray marcher.
            // The map is below the cloud shell: show its projected translucent
            // shadow, not a gray overhead cloud sprite pasted onto the ground.
            map.Sky.Bind(target,sun,cloudToggle.value,cloudToggle.value,true,true,true,true,true,0);
            if(globe&&globe.Snapshot!=null)OrbisNightLighting.Bind(target,globe.Snapshot.settings,sun,CalendarDay);
            target.SetFloat("_FlatNightAdaptation",navigation?Mathf.SmoothStep(0,1,navigation.Morph):1);
            target.SetFloat("_FlatCloudShadows",cloudToggle.value?1:0);
        }
        void NextMouth()
        {
            if(!Ready())return;
            for(int offset=1;offset<=World.Count;offset++)
            {
                int i=(Math.Max(-1,selected)+offset)%World.Count,parent=World.Downstream[i];
                if(World.Water[i]!=WaterKind.River||parent<0||!FlatTerrainAnalysis.Ocean(World.Water[parent]))continue;
                selected=i;toolField.index=0;map.Select(i);ChooseLayer(MapLayer.Natural);Focus();UpdateHUD();
                SetStatus("河口 Tile "+i+" → 海水 Tile "+parent+"；全图 "+World.Facts.MouthCount+" 个入海口。悬沙区由上游汇流和海水连通性生成。");return;
            }
            SetStatus("当前世界没有满足汇流阈值的入海河口；湖泊溢出口不计作海口。");
        }
        bool Ready()
        {if(World==null||Busy){SetStatus("世界数据正在生成或重建；完成后才能修改。 ");return false;}if(!navigation||!navigation.IsFlat){SetStatus("请展开到二维地图后操作文明和地形。 ");return false;}return true;}
        void SetStatus(string text) { status=text;if(notice!=null)notice.text=text; }
        void CancelPending()
        {
            var task=pending;var old=cancellation;pending=null;cancellation=null;
            if(old==null)return;old.Cancel();
            if(task==null){old.Dispose();return;}
            task.ContinueWith(t=>{if(t.IsFaulted){var observed=t.Exception;}old.Dispose();},TaskScheduler.Default);
        }
        void Queue(Func<CancellationToken,WorldData> operation,string text,bool seed=false)
        {
            CancelPending();cancellation=new CancellationTokenSource();var token=cancellation.Token;seedOnInstall=seed;
            pending=Task.Run(()=>{token.ThrowIfCancellationRequested();WorldData w=operation(token);token.ThrowIfCancellationRequested();FlatSave.Validate(w);if(w.Facts==null)FlatTerrainAnalysis.Build(w,token,false);return w;},token);SetStatus(text);
        }
        void Generate()
        {
            int width=256<<Mathf.Clamp(sizeField.index,0,2);string seed=seedField.value.Trim();var preset=(TerrainPreset)Mathf.Clamp(presetField.index,0,3);
            var atlasData=Array.Find(Resources.LoadAll<TextAsset>("ORBIS/WorldAtlas"),
                asset=>asset.name=="Default"&&FlatAtlasTerrain.HasSignature(asset.bytes));
            byte[] baked=atlasData?atlasData.bytes:null;
            string project=Path.GetDirectoryName(Application.dataPath);
            Queue(c=>preset==TerrainPreset.Continents?FlatAtlasGenerator.Generate(project,width,seed,baked,c):FlatGenerator.Generate(width,width/2,seed,preset,c),"生成板块地形、海岸、溢流湖泊、河网和气候；首次生成新种子需要几分钟…",true);
        }
        void Install(WorldData world)
        {
            var simulation=new FlatSimulation(world);if(seedOnInstall)simulation.SeedNations(4);
            var nextObject=new GameObject("Authoritative 2D map / render-data provider");nextObject.SetActive(false);nextObject.transform.SetParent(transform,false);
            var next=nextObject.AddComponent<FlatMapRenderer>();
            var navObject=new GameObject("Shared globe-to-scroll presentation");navObject.SetActive(false);navObject.transform.SetParent(transform,false);
            var nextNav=navObject.AddComponent<FlatScrollNavigator>();
            var globeObject=new GameObject("Native atmosphere from the same 2D world");globeObject.SetActive(false);globeObject.transform.SetParent(transform,false);
            var nextGlobe=globeObject.AddComponent<FlatGlobePresenter>();
            bool wasFlat=navigation&&navigation.IsFlat;Vector2 previousCenter=navigation?navigation.CenterTile:Vector2.zero;
            int previousWidth=World==null?world.Width:World.Width,previousHeight=World==null?world.Height:World.Height;
            try
            {
                next.Initialize(world);next.SetLayer((MapLayer)Mathf.Clamp(layerField.index,0,(int)MapLayer.Vegetation));
                nextNav.Initialize(world,viewCamera,next);
                nextGlobe.Initialize(world,viewCamera,next,true);nextGlobe.PrepareScroll();
                var lens=viewCamera.GetComponent<OrbisCelestialLens>();if(!lens)lens=viewCamera.gameObject.AddComponent<OrbisCelestialLens>();lens.view=nextGlobe.View;lens.navigation=nextNav;
            }
            catch
            {
                foreach(var volume in viewCamera.GetComponents<OrbisAtmosphereRenderer>())if(volume.view==nextGlobe.View){volume.enabled=false;Destroy(volume);}
                Destroy(nextObject);Destroy(navObject);Destroy(globeObject);
                if(navigation)navigation.Tick(0,CalendarDay);
                throw;
            }
            DestroyGlobe();if(navigation){navigation.gameObject.SetActive(false);Destroy(navigation.gameObject);}
            if(map){map.gameObject.SetActive(false);Destroy(map.gameObject);}route=new int[0];
            World=world;Simulation=simulation;simulationStep=simulation.Step;map=next;navigation=nextNav;globe=nextGlobe;solarClock.Reset();
            // The old quad stays inactive. Only the new shared lattice is rendered, in ALL modes.
            navObject.SetActive(true);globeObject.SetActive(true);
            selected=-1;activeNation=world.Nations.Count>0?0:-1;globeMode=true;lastMonth=world.Month;
            seedField.SetValueWithoutNotify(world.Seed);presetField.SetValueWithoutNotify(presetField.choices[(int)world.Preset]);
            if(wasFlat)
            {
                int x=Mathf.Clamp((int)(previousCenter.x/previousWidth*world.Width),0,world.Width-1);
                int y=Mathf.Clamp((int)(previousCenter.y/previousHeight*world.Height),0,world.Height-1);
                navigation.Focus(y*world.Width+x,CalendarDay);
            }
            navigation.Tick(0,CalendarDay);UpdateHUD();SetStatus("世界就绪：默认星球。滚轮进入局部地表，拖动可连续穿过日期线和极区；全球总览独立打开。");
        }
        void Update()
        {
            if(pending!=null&&pending.IsCompleted)
            {
                var completed=pending;pending=null;var source=cancellation;cancellation=null;
                try{Install(completed.GetAwaiter().GetResult());}catch(OperationCanceledException){SetStatus("已取消，保留原世界。");}catch(Exception e){SetStatus("操作失败，保留原世界："+e.Message);Debug.LogException(e);}finally{source?.Dispose();}
            }
            if(World==null||!navigation)return;
            float realDt=Mathf.Max(0,Time.unscaledDeltaTime),dt=Mathf.Min(realDt,.10f);bool animate=!paused&&!Busy;
            solarClock.Advance(realDt,speed,120f,!animate,simulationStep);
            if(World.Month!=lastMonth){lastMonth=World.Month;if(map.Layer==MapLayer.Temperature||map.Layer==MapLayer.Rain)World.MarkAllDirty();}
            map.Flush();if(animate)visualClock+=dt;
            UpdateViewportRect();navigation.Tick(dt,CalendarDay);
            globeMode=!navigation.IsFlat;pixelsPerTile=navigation.PixelsPerTile;pan=navigation.CenterTile;
            map.Configure(gridToggle.value,pixelsPerTile);
            bool natural=map.VisibleLayer==MapLayer.Natural||map.VisibleLayer==MapLayer.Nations;
            bool skyNeeded=natural&&cloudToggle.value;
            map.Sky.Tick(dt,skyNeeded,animate,false,cloudQuality.index==0?512:1024,CalendarDay,pan,navigation.Morph>.5f);
            Vector3 sun=map.Sky.Sun(180f,CalendarDay,true);
            bool waterView=natural||map.VisibleLayer==MapLayer.Water;
            // SW3 owns the visible normal and foam animation; skip the old CPU/GPU
            // shallow-water patch when its output cannot contribute to the shader.
            bool sw3=map.Material.GetFloat("_VendorWaterReady")>.5f;
            map.LocalWater.Tick(dt,pan.x,pan.y,!sw3&&navigation.IsFlat&&waterView&&pixelsPerTile>=8,animate);
            BindSky(map.Material,sun);ApplyWaterSettings(map.Material);navigation.SyncMaterials();
            if(globe)globe.Snapshot.settings.cloudQuality=Mathf.Clamp(cloudQuality.index,0,2);
            if(globe)globe.TickScroll(animate?dt:0,visualClock,CalendarDay,navigation.DisplaySpin,navigation.Morph,
                cloudToggle.value,true,true,cloudToggle.value,sun,natural);
            hudAge+=dt;if(hudAge>.25f){hudAge=0;UpdateHUD();}
        }

        void UpdateMapCamera()
        {
            if(World==null||!map||!navigation)return;
            UpdateViewportRect();navigation.Tick(0,CalendarDay);pixelsPerTile=navigation.PixelsPerTile;pan=navigation.CenterTile;
            map.Configure(gridToggle.value,pixelsPerTile);
        }
        void UpdateViewportRect()
        {
            if(screenshotMode){navigation.SetViewport(new Rect(0,0,Screen.width,Screen.height));return;}
            Rect r=viewport.worldBound;Vector3 lower=ScreenPoint(new Vector2(r.xMin,r.yMax)),upper=ScreenPoint(new Vector2(r.xMax,r.yMin));
            navigation.SetViewport(Rect.MinMaxRect(lower.x,lower.y,upper.x,upper.y));
        }
        void OpenAtlas()
        {
            if(World==null||Busy||!navigation)return;
            navigation.OpenAtlas(CalendarDay);SetStatus("全球战略总览：拖动、WASD、滚轮缩放；M 返回星球。主地图使用球面局部展开。");
        }
        Vector3 ScreenPoint(Vector2 panelPoint)
        {Vector2 p=root.WorldToLocal(panelPoint);return new Vector3(p.x/Mathf.Max(1,root.resolvedStyle.width)*Screen.width,(1-p.y/Mathf.Max(1,root.resolvedStyle.height))*Screen.height,10);}
        void Zoom(float wheel,Vector2 position)
        {if(World==null||!navigation)return;navigation.Wheel(wheel,ScreenPoint(position),CalendarDay);}
        void Pick(Vector2 position)
        {
            if(World==null||Busy||!navigation)return;
            int hit=navigation.Pick(ScreenPoint(position));if(hit<0)return;
            selected=hit;map.Select(selected);if(World.Owner[selected]>=0)activeNation=World.Owner[selected];
            if(!navigation.IsFlat){UpdateHUD();return;}
            try
            {
                switch(toolField.index)
                {
                    case 1:int id=Simulation.Found(selected);if(id>=0){activeNation=id;SetStatus("已建立文明。500 人只放入首都 Tile，不会在 16 格中重复计数。");}else SetStatus("此 Zone 已有归属、不可居住，或文明数已达上限。");break;
                    case 2:SetStatus(Simulation.Annex(selected,activeNation)?"已扩张一个相邻 Zone，并迁移部分人口。":"扩张需要本国接壤陆路、无主 Zone 和 60 粮食。先点击本国地块选择文明。");break;
                    case 3:Paint();break;
                    case 4:SetStatus(Simulation.Build(selected,Structure.Farm)?"农田已建立，将影响月度粮食产出。":"农田需要本国可居住地块和 15 粮食，不能覆盖聚落。");break;
                    case 5:SetStatus(Simulation.Build(selected,Structure.Road)?"道路已修建，将降低路径成本。":"道路需要本国地块和 15 粮食，不能覆盖聚落。");break;
                    case 6:SetStatus(Simulation.Build(selected,Structure.Bridge)?"桥梁已修建。":"桥梁仅允许本国河流/浅湖（深度≤100m），费用 40 粮食。");break;
                    case 7:ShowRoute();break;
                }
            }
            catch(Exception e){SetStatus(e.Message);Debug.LogException(e);}
            map.Flush();UpdateHUD();
        }
        void Paint()
        {
            if(!Ready()||selected<0)return;solarClock.Flush(simulationStep);var copy=World.Clone();int x=selected%World.Width,y=selected/World.Width,selection=heightField.index;
            short meters=selection==0?(short)-2200:selection==1?(short)-600:selection==2?(short)-80:HeightBands.Preset(selection-2);
            if(brushField.index==1){int zone=copy.ZoneOf(selected);for(int p=0;p<16;p++)copy.Elevation[copy.ZoneTile(zone,p)]=meters;}
            else{int radius=brushField.index==2?1:brushField.index==3?4:0;for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++){int i=copy.Index(x+dx,y+dy);if(i>=0)copy.Elevation[i]=meters;}}
            Queue(c=>{FlatGenerator.Rebuild(copy,c);return copy;},"应用预设高度并重算排水/气候；未完成的数据不会进入正在显示的世界…");
        }
        void ShowRoute()
        {
            if(!Ready()||selected<0||activeNation<0||!navigation)return;
            int capital=World.Nations[activeNation].Capital;var pathfinder=GetComponent<FlatPlanetPathfinder>();if(!pathfinder)pathfinder=gameObject.AddComponent<FlatPlanetPathfinder>();route=pathfinder.Find(World,capital,selected);
            navigation.SetRoute(route);
            SetStatus(route.Length==0?"未找到陆路；水域、地形或搜索预算阻断了路径。":"陆路路径："+route.Length+" 个 Tile。跨经线段按最短环绕方向显示；这仍是寻路预览。");
        }
        void ToggleMode()
        {
            if(World==null||Busy||!navigation)return;
            navigation.Toggle(CalendarDay);UpdateHUD();SetStatus("同一地表网格展开 / 收拢。过渡期间可反向滚轮；进入二维后才允许编辑。Space 独立暂停模拟。");
        }
        void DestroyGlobe()
        {
            if(!globe)return;
            var oldView=globe.View;
            if(viewCamera)foreach(var volume in viewCamera.GetComponents<OrbisAtmosphereRenderer>())if(volume.view==oldView){volume.enabled=false;Destroy(volume);}
            globe.gameObject.SetActive(false);Destroy(globe.gameObject);globe=null;
        }
        void Home()
        {if(World!=null&&navigation)navigation.Home(CalendarDay);}
        void Focus()
        {if(World!=null&&navigation&&selected>=0)navigation.Focus(selected,CalendarDay);}
        void Save()
        {
            if(World==null||Busy)return;solarClock.Flush(simulationStep);FlatSave.WriteAtomic(SavePath,World);
            SetStatus("已保存世界与连续日历相位；前一份存档保留 .bak："+SavePath);
        }
        void Load() {string path=SavePath;Queue(c=>{c.ThrowIfCancellationRequested();return FlatSave.Read(path);},"校验并载入 2D 世界存档…");}
        void Import()
        {if(Busy)return;string path=HeightPath,seed=seedField.value.Trim();Queue(c=>FlatRasterIO.Import(path,seed,c),"从灰度高度图导入新世界（黑 −4000m / 白 4000m）…",true);}
        IEnumerator Capture()
        {
            string path=Path.Combine(Application.persistentDataPath,"ORBIS_"+(globeMode?"globe":"2D")+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".png");
            screenshotMode=true;root.style.visibility=Visibility.Hidden;yield return null;yield return new WaitForEndOfFrame();
            string failure=null;try{ScreenCapture.CaptureScreenshot(path,1);}catch(Exception e){failure=e.Message;}
            yield return null;screenshotMode=false;root.style.visibility=Visibility.Visible;UpdateMapCamera();SetStatus(failure==null?"截图请求已提交："+path:"截图失败："+failure);
        }
        readonly int[] zoneCounts=new int[WorldData.MaxNations];
        readonly StringBuilder nationText=new StringBuilder(512);
        void UpdateHUD()
        {
            if(World==null)return;
            stats.text="第 "+((int)(CalendarDay/360)+1)+" 年 / "+(World.Month+1)+" 月 · "+((int)(FlatSolarClock.Phase(CalendarDay)*24)).ToString("00")+":"+((int)(FlatSolarClock.Phase(CalendarDay)*1440)%60).ToString("00")+"    "+World.Width+" × "+World.Height+" Tiles    "+World.ZoneCount.ToString("N0")+" Zones    人口 "+Simulation.TotalPopulation().ToString("N0")+("    "+navigation.ModeName+(paused?" · 已暂停":" · 同一世界时钟"));
            modeButton.text=globeMode?"M  展开总览":"M  返回星球";pauseButton.text=paused?"继续":"暂停";speedButton.text=speed+"×";
            Array.Clear(zoneCounts,0,zoneCounts.Length);for(int z=0;z<World.ZoneCount;z++){int ownerId=World.ZoneOwner[z];if(ownerId>=0)zoneCounts[ownerId]++;}
            var text=nationText;text.Clear();for(int c=0;c<World.Nations.Count;c++){var n=World.Nations[c];int zones=zoneCounts[c];text.Append(c==activeNation?"● ":"○ ").Append(n.Name).Append(" · ").Append(zones).Append("区 · 粮 ").Append(n.Food.ToString("N0")).Append('\n');}nations.text=text.ToString();
            skyStatus.text=map.Sky.Status+(globeMode&&globe.View.UsingSurfaceFallback?"\n警告：球体当前为简化 fallback，需检查 Shader。":"");
            legend.text=FlatPalette.Legend(map.Layer);waterStatus.text=globeMode?"摄影模式不运行局部 SWE。":map.LocalWater.Status;
            if(selected<0){info.text="点击地图选择一个 Tile。\n\n每个 Zone = 4×4 = 16 个真实 Tile。\n海拔、生物群系、水体、气候与归属分别存储。";return;}
            int i=selected,zone=World.ZoneOf(i),level=HeightBands.Level(World.Elevation[i]);string owner=World.Owner[i]<0?"无主":World.Nations[World.Owner[i]].Name;
            info.text="TILE "+i+"   ["+(i%World.Width)+", "+(i/World.Width)+"]\nZONE "+zone+" · 内部格 "+(i%World.Width%4)+","+(i/World.Width%4)+"\n\n"+World.Elevation[i]+" m · "+(level>0?"L"+level.ToString("00"):"海平面以下")+"\n水体："+WaterName(World.Water[i])+"\n地表："+BiomeName(World.Biomes[i])+"\n柯本："+World.Climates[i]+"\n本月温度："+World.Temperature[i*12+World.Month].ToString("F1")+" °C\n本月降水："+World.Precipitation[i*12+World.Month].ToString("F0")+" mm\n肥力："+(World.Fertility[i]*100).ToString("F0")+"%\n归属："+owner+"\n人口："+World.Population[i].ToString("F0")+"\n设施："+StructureName(World.Buildings[i])+"\n下游 Tile："+World.Downstream[i];
            var facts=World.Facts;
            info.text+="\n植被："+FlatPalette.VegetationNames[(int)facts.Vegetation[i]]+"\n河宽（显示估计）："+(facts.HalfWidth[i]*2).ToString("F2")+" Tile"+"\n\n地貌："+FlatPalette.LandformNames[(int)facts.Forms[i]]+"\n材料："+FlatPalette.MaterialNames[(int)facts.Materials[i]]+
                "\n5×5 起伏："+facts.Relief[i].ToString("F0")+" m\n相邻最大高差："+facts.Gradient[i].ToString("F0")+" m/Tile"+
                "\n排水高差："+facts.Hand[i].ToString("F0")+" m\n湿润月份："+facts.SaturatedMonths[i]+" / 12"+
                "\n年降水："+facts.AnnualRain[i].ToString("F0")+" mm\n年均温："+facts.MeanTemperature[i].ToString("F1")+" °C"+
                "\n汇流量（相对）："+World.Runoff[i].ToString("F1")+"\n水面高程："+World.WaterLevel[i]+" m"+
                "\n水深"+(World.Water[i]==WaterKind.River?"（河槽估计）":"")+"："+facts.Depth[i].ToString("F1")+" m"+
                "\n悬沙 / 淡水比例："+facts.Sediment[i].ToString("F2")+" / "+facts.Freshness[i].ToString("F2");
        }
        static string BiomeName(Biome b)
        {return FlatPalette.BiomeNames[(int)b];}
        static string StructureName(Structure s)
        {return new[]{"无","聚落","农田","道路","桥梁"}[(int)s];}
        static string WaterName(WaterKind w)
        {switch(w){case WaterKind.DeepOcean:return "深海";case WaterKind.MiddleOcean:return "中海";case WaterKind.ShallowOcean:return "浅海";case WaterKind.Lake:return "淡水湖";case WaterKind.River:return "淡水河流";default:return "陆地";}}
        void OnDestroy()
        {
            CancelPending();if(font)Destroy(font);if(panel)Destroy(panel);
        }
    }
}
