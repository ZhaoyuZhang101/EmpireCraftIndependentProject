using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis
{
    [Serializable]
    public sealed class DemoViewState
    {
        public float altitude,simulationRemainder;
        public float lookTilt,lookHeading;
        public double renderClock;
        public float yaw, pitch, distance, mapX, mapY, mapZoom, speed;
        public int selected, tool, layer;
        public int selectedPlot=-1;
        public int activeNation=-1;
        public bool map, paused;
        public bool hasRenderRotation;
        public double renderRotationDegrees;
    }

    [DefaultExecutionOrder(-100)]
    public sealed partial class OrbisDemo : MonoBehaviour
    {
        public string initialSeed = "ORBIS-VI";
        [Header("V4 sprite art (optional)")]public Texture2D buildingSprite,personSprite;
        public OrbisWorld World { get; private set; }
        public OrbisPlanetView View { get; private set; }
        public OrbisMapView Atlas { get; private set; }
        public Camera PlanetCamera { get; private set; }
        public float Speed { get; private set; } = 1;
        public bool Paused { get; private set; }
        public bool MapMode { get; private set; }
        public bool AutoOrbit { get; private set; }
        public int Selected { get; private set; } = -1;
        public int SelectedPlot { get; private set; } = -1;
        public int Tool { get; private set; }
        public string Notice { get; private set; } = "按「播種四個文明」，開始觀察世界。";
        public string SavePath => Path.Combine(Application.persistentDataPath, "orbis-demo-v4.json");
        float yaw, pitch = 18, distance = 3.65f, mapZoom = 2.2f;
        Vector2 mapPan, smoothPan, panVelocity;
        Vector3 suspendedFocus;bool hasSuspendedFocus;int mapEntrySelection=-2;
        float smoothYaw, smoothPitch=18, smoothDistance=3.65f, smoothMapZoom=2.2f;
        float yawVelocity,pitchVelocity,distanceVelocity,zoomVelocity;
        public float VisualRotation { get; private set; }
        public float WeatherInterpolation => Paused ? 0 : simulationAccumulator;
        public Vector3 VisualSun => World.SunAt(World.day+(Paused?0:simulationAccumulator*Speed*2), VisualRotation*Mathf.Deg2Rad);
        float simulationAccumulator, visualAccumulator, markerAge;
        double visualClock,visualRotationDegrees;
        float paletteAge;
        float altitude=2.65f,smoothAltitude=2.65f,altitudeVelocity;
        public float Altitude=>smoothAltitude;
        public string ViewBand=>MapMode?"全景地圖":CameraRadius>=OrbisAtmosphereProfile.OuterRadius?"地外軌道":CameraRadius>OrbisAtmosphereProfile.CloudTop?"高層大氣":CameraRadius>=OrbisAtmosphereProfile.CloudBase?"雲層高度帶":"低層大氣／近地";
        int lastRevision;
        OrbisHud hud;
        PanelSettings panel;
        OrbisOverlays overlays;
        GameObject planet;

        void Awake()
        {
            Application.targetFrameRate = 60;
            World = new OrbisWorld(initialSeed);
            var cameraObject = new GameObject("ORBIS 相機", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            PlanetCamera = cameraObject.GetComponent<Camera>();
            PlanetCamera.tag = "MainCamera";
            PlanetCamera.clearFlags = CameraClearFlags.SolidColor;
            PlanetCamera.backgroundColor = Color.black; // Sky radiance now comes from the active GLSL atmosphere, not a blue clear color.
            OrbisCelestialBackdrop.Apply(PlanetCamera,true);
            PlanetCamera.nearClipPlane = .005f;
            PlanetCamera.farClipPlane = 100;
            PlanetCamera.fieldOfView = 42;
            PlanetCamera.depthTextureMode = DepthTextureMode.Depth;PlanetCamera.allowHDR=true;PlanetCamera.allowMSAA=false;
            planet = new GameObject("ORBIS 星球"); planet.transform.SetParent(transform, false);
            View = planet.AddComponent<OrbisPlanetView>(); View.Initialize(World, PlanetCamera,buildingSprite,personSprite);
            cameraObject.AddComponent<OrbisCelestialLens>().view = View;
            var mapObject = new GameObject("ORBIS 世界地圖"); mapObject.transform.SetParent(transform, false);
            Atlas = mapObject.AddComponent<OrbisMapView>(); Atlas.Initialize(World, View); PlanetCamera.GetComponent<OrbisAtmosphereRenderer>().atlas=Atlas; mapObject.SetActive(false);
            var overlayObject = new GameObject("ORBIS 地理與文明觀測"); overlayObject.transform.SetParent(transform, false);
            overlays = overlayObject.AddComponent<OrbisOverlays>(); overlays.Initialize(this);
            var transport=overlayObject.AddComponent<OrbisTransportView>();transport.demo=this;
            var weatherEffects=overlayObject.AddComponent<OrbisWeatherEffects>();weatherEffects.Initialize(this);
            var selectionObject=new GameObject("V4 selected region outlines");selectionObject.transform.SetParent(transform,false);
            SelectionView=selectionObject.AddComponent<OrbisSelectionView>();SelectionView.demo=this;
            var ui = new GameObject("ORBIS 中文介面"); ui.transform.SetParent(transform, false); ui.SetActive(false);
            var document = ui.AddComponent<UIDocument>();
            panel = Instantiate(Resources.Load<PanelSettings>("ORBIS/OrbisPanel"));
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1600, 900); panel.match = .5f;
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("ORBIS/OrbisTheme");
            document.panelSettings = panel;
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("ORBIS/Orbis");
            hud = ui.AddComponent<OrbisHud>(); hud.demo = this; ui.SetActive(true);
            FocusBestLand(); smoothYaw=yaw; smoothPitch=pitch; lastRevision = World.Revision; UpdateCamera(); overlays.Tick(0, true);
        }

        void Update()
        {
            float delta = Mathf.Min(Time.unscaledDeltaTime, .1f);
            TickPerformanceCapture();
            if (!Paused)
            {
                visualClock += delta; simulationAccumulator += delta;
                World.DeferWeatherUpdates=true;
                int steps = 0;
                while (simulationAccumulator >= .2f && steps++ < 2)
                { World.Step(.2f, .2f * Speed * 2); simulationAccumulator -= .2f; }
            }
            if(!Paused)
            {
                float rate=Mathf.Min(2.5f,Mathf.Sqrt(Mathf.Max(.05f,Speed)));
                // At most one moist-atmosphere tick per rendered frame; overload is counted.
                World.Weather.Advance(delta*rate,1);
            }
            TickAtmosphereFlight(delta);
            if (AutoOrbit && !MapMode) yaw += delta * 5;
            visualAccumulator += delta; markerAge += delta;
            if (visualAccumulator >= .3f)
            {
                visualAccumulator = 0;
                View.UpdateWeather();
                if (MapMode&&lastRevision==World.Revision) Atlas.Refresh();
                hud.Refresh();
            }
            paletteAge+=delta;
            // Stagger palette work away from the field upload frame.
            if(!Paused&&paletteAge>=.65f&&visualAccumulator>.04f){paletteAge=0;View.UpdateColors();}
            if (lastRevision != World.Revision)
            {
                lastRevision = World.Revision; View.RebuildGeometry();
                if (MapMode) Atlas.Refresh(); markerAge = 0;
            }
            else if (markerAge >= 2 && !Paused) { View.UpdateCities(); markerAge = 0; }
            float previousRotation=VisualRotation;
            if(!Paused&&World.settings.rotation)
                visualRotationDegrees=(visualRotationDegrees+delta*360.0/Mathf.Max(1f,World.settings.rotationPeriod))%360.0;
            VisualRotation=(float)visualRotationDegrees;
            if((World.settings.followSurface||smoothAltitude<.25f)&&!MapMode&&!AutoOrbit)
            {float spin=Mathf.DeltaAngle(previousRotation,VisualRotation);yaw+=spin;smoothYaw+=spin;}
            yaw=Mathf.Repeat(yaw+180,360)-180;smoothYaw=Mathf.Repeat(smoothYaw+180,360)-180;
            planet.transform.localRotation=Quaternion.Euler(0,VisualRotation,0);
            UpdateCamera();
            View.SyncVolumeSettings(VisualSun,World.Weather!=null?World.Weather.Clock+World.Weather.Accumulator:visualClock,
                World.day+(Paused?0:simulationAccumulator*Speed*2));
            if (!MapMode) View.Sync(visualClock,VisualSun,WeatherInterpolation);
            else Atlas.Sync(visualClock,VisualSun,WeatherInterpolation);
            overlays.Tick(delta); hud.UpdateNamePositions();
        }

        void UpdateCamera()
        {
            float dt=Mathf.Max(.0001f,Mathf.Min(Time.unscaledDeltaTime,.1f));
            smoothYaw=Mathf.SmoothDampAngle(smoothYaw,yaw,ref yawVelocity,.085f,Mathf.Infinity,dt);
            smoothPitch=Mathf.SmoothDampAngle(smoothPitch,pitch,ref pitchVelocity,.085f,Mathf.Infinity,dt);
            smoothAltitude=Mathf.SmoothDamp(smoothAltitude,altitude,ref altitudeVelocity,.12f,Mathf.Infinity,dt);
            smoothMapZoom=Mathf.SmoothDamp(smoothMapZoom,mapZoom,ref zoomVelocity,.12f,Mathf.Infinity,dt);
            smoothPan=Vector2.SmoothDamp(smoothPan,mapPan,ref panVelocity,.09f,Mathf.Infinity,dt);
            PlanetCamera.orthographic = MapMode;
            OrbisCelestialBackdrop.Apply(PlanetCamera,!MapMode);
            if (MapMode)
            {
                PlanetCamera.nearClipPlane=.005f;PlanetCamera.orthographicSize = smoothMapZoom;
                PlanetCamera.transform.position = new Vector3(smoothPan.x, smoothPan.y, 8);
                // Atlas X is negative longitude, so east remains screen-right when facing -Z.
                PlanetCamera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            }
            else
            {
                Quaternion orbit = Quaternion.Euler(smoothPitch, smoothYaw, 0);
                Vector3 radial=orbit*Vector3.forward;
                Vector3 local=planet.transform.InverseTransformDirection(radial);
                float ground=View.RadiusAt(local,out var sample);
                float safeAltitude=smoothAltitude; // Sprites do not impose the old cube-building camera barrier.
                distance=smoothDistance=ground+safeAltitude;
                PlanetCamera.nearClipPlane=Mathf.Clamp(smoothAltitude*.08f,.00008f,.01f);
                PlanetCamera.transform.position=planet.transform.position+radial*smoothDistance;
                ApplyAtmosphereLook(orbit,radial,dt);
            }
        }

        public void Orbit(Vector2 delta)
        {
            StopAtmosphereFlight();
            if (MapMode)
            {
                mapPan.x += delta.x * mapZoom / 360;
                mapPan.y += delta.y * mapZoom / 360;
                mapPan.x = Mathf.Clamp(mapPan.x, -Mathf.PI, Mathf.PI);
                mapPan.y = Mathf.Clamp(mapPan.y, -Mathf.PI / 2, Mathf.PI / 2);
            }
            else {float sensitivity=.22f*Mathf.Clamp(smoothAltitude/1.8f,.006f,1);
                yaw-=delta.x*sensitivity;pitch=Mathf.Clamp(pitch+delta.y*sensitivity,-89.8f,89.8f); }
        }
        public void Zoom(float wheel)
        {
            StopAtmosphereFlight();
            if (MapMode) mapZoom = Mathf.Clamp(mapZoom * Mathf.Exp(wheel * .025f), .09f, 3);
            else {altitude = Mathf.Clamp(altitude * Mathf.Exp(wheel * .04f), .003f, 5.5f);if(altitude<.25f)AutoOrbit=false;}
        }
        public void Pick(Vector2 normalized)
        {
            if(TryPick(normalized,out int tile,out Vector3 point))
            {SelectedPlot=OrbisZoneLayout.PickPlot(World,tile,point);SelectTile(tile,true);}
        }
        public bool TryPick(Vector2 normalized,out int tile)=>TryPick(normalized,out tile,out _);
        public bool TryPick(Vector2 normalized,out int tile,out Vector3 surfacePoint)
        {
            tile=-1;surfacePoint=Vector3.zero;
            Ray ray = PlanetCamera.ViewportPointToRay(new Vector3(normalized.x, 1 - normalized.y, 0));
            Vector3 p;
            if (MapMode)
            {
                var plane = new Plane(Vector3.forward, Vector3.zero);
                if (!plane.Raycast(ray, out float hit)) return false;
                Vector3 at = ray.GetPoint(hit);
                if (Mathf.Abs(at.x) > Mathf.PI || Mathf.Abs(at.y) > Mathf.PI / 2) return false;
                p = OrbisMapView.Unproject(at);
            }
            else
            {
                Vector3 origin = planet.transform.InverseTransformPoint(ray.origin);
                Vector3 direction = planet.transform.InverseTransformDirection(ray.direction);
                if(!View.RaycastSurface(new Ray(origin,direction),out p))return false;
            }
            tile=World.PickTile(p);surfacePoint=p.normalized;return true;
        }
        public void SelectTile(int tile, bool useTool = false)
        {
            if (tile < 0 || tile >= World.tiles.Length) return;
            Selected = tile;
            if(SelectedPlot<0||SelectedPlot/OrbisZoneLayout.PlotsPerZone!=tile)SelectedPlot=-1;
            if(useTool&&Tool==1){ActiveNation=World.FoundNation(Selected,World.settings.selectionMode);Notice=ActiveNation>=0?"已在所點 4×4 Zone 建國；自然地貌選區不會被整片圈占。":"只能在完全無主的陸地區域建立文明。";}
            else if(useTool&&(Tool==3||Tool==4||Tool==6||Tool==7)){EditTerrain(Selected,Tool);return;}
            else if(useTool){World.Brush(Selected,Tool);if(Tool==2)View.UpdateWeather(true);}
            World.UpdateStats(); View.Select(Selected); View.UpdateColors(); View.UpdateCities();
            if (MapMode) Atlas.Refresh();
            overlays.Tick(0, true); hud.Refresh();
        }
        void FocusBestLand()
        {
            int best = 0; float score = -1;
            for (int i = 0; i < World.tiles.Length; i++) if (World.tiles[i].Fertility > score) { score = World.tiles[i].Fertility; best = i; }
            Selected = best;SelectedPlot=-1; FocusSelected();
        }
        public void FocusSelected()
        {
            StopAtmosphereFlight();
            if (Selected < 0) return;
            if (MapMode) { Vector3 p = OrbisMapView.Project(World.grid.points[Selected]); mapPan = new Vector2(p.x, p.y); }
            else
            {
                Vector3 p = Quaternion.Euler(0, VisualRotation, 0) * World.grid.points[Selected];
                yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg; pitch = -Mathf.Asin(p.y) * Mathf.Rad2Deg;
            }
            View.Select(Selected); UpdateCamera();
        }
        public void ToggleMap()
        {
            StopAtmosphereFlight();
            bool entering=!MapMode;
            if(entering)
            {
                suspendedFocus=planet.transform.InverseTransformPoint(PlanetCamera.transform.position).normalized;
                hasSuspendedFocus=true;mapEntrySelection=Selected;
            }
            MapMode=entering;planet.SetActive(!MapMode);Atlas.gameObject.SetActive(MapMode);
            if(MapMode){mapPan=Vector2.zero;mapZoom=2.2f;Atlas.Refresh();}
            else
            {
                Vector3 focus=Selected>=0&&(!hasSuspendedFocus||Selected!=mapEntrySelection)?World.grid.points[Selected]:suspendedFocus;
                if(focus.sqrMagnitude>.5f)
                {
                    Vector3 p=Quaternion.Euler(0,VisualRotation,0)*focus;
                    yaw=Mathf.Atan2(p.x,p.z)*Mathf.Rad2Deg;pitch=-Mathf.Asin(Mathf.Clamp(p.y,-1,1))*Mathf.Rad2Deg;
                    smoothYaw=yaw;smoothPitch=pitch;yawVelocity=pitchVelocity=0;
                }
            }
            UpdateCamera();overlays.Tick(0,true);hud.Refresh();
        }
        public void LocalView()
        {
            StopAtmosphereFlight();lookTilt=0;lookHeading=0;
            if(MapMode)ToggleMap();FocusSelected();altitude=.025f;AutoOrbit=false;
            Notice="滾輪可連續穿越大氣與雲層；近景自動跟隨地表。局部格線是規劃輔助，不改變模擬地塊編號。";hud.Refresh();
        }
        public void HomeView() { StopAtmosphereFlight();lookTilt=0;lookHeading=0; if (MapMode) { mapPan = Vector2.zero; mapZoom = 2.2f; } else { altitude = 2.65f; FocusSelected(); } }
        public void FindRiver() { int tile = World.MainRiver();float largest=0;foreach(var reach in View.Rivers.reaches)if(reach.qa>largest&&reach.ha>.015f){largest=reach.qa;tile=World.grid.Nearest(reach.a);} if (tile >= 0) { SelectTile(tile); FocusSelected(); Notice = "已定位主要河流：" + World.RiverName(tile); } else Notice = "目前沒有可觀測的陸地河流。"; hud.Refresh(); }
        public void SetLayer(int value) { View.layer = value; View.UpdateColors(true); if (MapMode) Atlas.Refresh(true); overlays.Tick(0, true); hud.Refresh(); }
        public void SetTool(int value)
        {
            Tool = Mathf.Clamp(value,0,7);
            Notice = new[] { "點擊地塊，查看當地環境與歸屬。", "點擊無主陸地，建立新文明。", "點擊地表降雨，恢復土壤水分。", "點擊隆起地形；河流與自然分區會重新計算。", "點擊降低地形；受淹居民會遷往附近陸地。", "點擊移除當地聚落與人口。", "平滑所選位置附近的高程；半徑與強度可調。", "按設定目標整平高程；可先取用所選地塊的高度。" }[Tool];
            hud.Refresh();
        }
        public void TogglePause() { Paused = !Paused; if(!Paused)ClearTerrainHistory(); hud.Refresh(); }
        public void CycleSpeed() { Speed = Speed == 1 ? 5 : Speed == 5 ? 20 : 1; hud.Refresh(); }
        public void SeedCivs() { World.SeedFour(); World.UpdateStats(); View.UpdateCities(); View.UpdateColors(); hud.Refresh(); }
        public void SetWar(bool enabled) { World.war = enabled; World.settings.autoWar = false; if (!enabled) World.RecallExpeditions(); World.Log(enabled ? "所有文明進入戰爭狀態。" : "各文明停戰，遠征軍開始返鄉。"); hud.Refresh(); }
        public void SendExpedition(bool settlers)
        {
            bool ok = World.Dispatch(Selected, settlers);
            Notice = ok ? (settlers ? "拓荒隊已出發，路線會顯示在地圖上。" : "遠征軍已出發。") : "無法派遣：需要有居民的己方地塊、足夠人口及可達目標；跨海需進入航海時代，遠征軍需處於戰爭。";
            hud.Refresh();
        }
        public void ToggleClouds() { World.settings.clouds = !World.settings.clouds; hud.Refresh(); }
        public void ToggleNight() { World.settings.night = !World.settings.night; hud.Refresh(); }
        public void ToggleRotation() { World.settings.rotation = !World.settings.rotation; hud.Refresh(); }
        public void ToggleRivers() { View.showRivers = !View.showRivers; hud.Refresh(); }
        public void ToggleAutoOrbit() { StopAtmosphereFlight(); AutoOrbit = !AutoOrbit; hud.Refresh(); }
        public void JumpGeology()
        {
            if(World.MapSource!=null){Notice="匯入高度圖不進行程序板塊漂移；請使用地形工作台。";hud.Refresh();return;}
            // The ONLY live path advancing the geological clock. Simulation Step never calls this.
            Paused=true;AutoOrbit=false;StopAtmosphereFlight();ClearTerrainHistory();
            World.geo+=20;World.Terrain();World.Log("手動推進板塊兩千萬年；日常模擬不移動地形。");
            Notice="板塊已手動推進；世界暫停以便檢查海岸與領土，按繼續恢復天氣／文明。";
            SelectionView.Invalidate();hud.Refresh();
        }
        public void NewWorld(string seed)
        {
            World.Generate(seed);ClearTerrainHistory();ActiveNation=-1;SelectionView.Hovered=-1;SelectionView.Invalidate();hasSuspendedFocus=false;mapEntrySelection=-2;visualClock=0;visualRotationDegrees=0;VisualRotation=0; Selected = -1; simulationAccumulator = 0; FocusBestLand();
            Atlas.SetWorld(World); Notice = "新世界已生成，可以播下文明。"; hud.Refresh();
        }
        public void Save()
        {
            try
            {
                WorldSave state = World.Save();
                state.view = new DemoViewState { yaw = yaw, pitch = pitch, distance = distance, altitude=altitude, lookTilt=lookTilt, lookHeading=lookHeading, simulationRemainder=simulationAccumulator,renderClock=visualClock,hasRenderRotation=true,renderRotationDegrees=visualRotationDegrees, mapX = mapPan.x, mapY = mapPan.y, mapZoom = mapZoom, speed = Speed, selected = Selected, selectedPlot=SelectedPlot, tool = Tool, layer = View.layer, activeNation=ActiveNation, map = MapMode, paused = Paused };
                string json = JsonUtility.ToJson(state, true); Directory.CreateDirectory(Application.persistentDataPath);
                string temp = SavePath + ".tmp"; File.WriteAllText(temp, json);
                if (File.Exists(SavePath)) File.Replace(temp, SavePath, SavePath + ".bak"); else File.Move(temp, SavePath);
                Notice = "世界、軍團、分區與視角已保存；上一份存檔保留為備份。";
            }
            catch (Exception e) { Debug.LogException(e); Notice = "保存失敗，請確認存檔資料夾可寫入；既有備份不會刪除。"; }
            hud.Refresh();
        }
        public void Load()
        {
            try
            {
                string v3=Path.Combine(Application.persistentDataPath,"orbis-demo-v3.json");
                string v2=Path.Combine(Application.persistentDataPath,"orbis-demo-v2.json");
                string path=File.Exists(SavePath)?SavePath:File.Exists(v3)?v3:File.Exists(v2)?v2:Path.Combine(Application.persistentDataPath,"orbis-demo-v1.json");
                if (!File.Exists(path)) { Notice = "尚未找到存檔，請先保存世界。"; hud.Refresh(); return; }
                var info = new FileInfo(path); if (info.Length > 96 * 1024 * 1024) throw new InvalidDataException("存檔超過大小限制。");
                WorldSave state = OrbisSaveMigration.Read(File.ReadAllText(path));
                var next = OrbisWorld.Restore(state);
                World = next;ClearTerrainHistory();ActiveNation=-1;SelectionView.Hovered=-1;SelectionView.Invalidate();hasSuspendedFocus=false;mapEntrySelection=-2;visualClock=World.VisualTime;visualRotationDegrees=World.rotationAngle*Mathf.Rad2Deg;VisualRotation=(float)visualRotationDegrees; View.world = World; Atlas.SetWorld(World); simulationAccumulator = 0;
                View.RebuildGeometry(); View.UpdateWeather(); lastRevision = World.Revision;
                FocusBestLand();
                if (state.view != null) RestoreView(state.view);
                Notice = "存檔已載入。"; hud.SyncSettings(); overlays.Tick(0, true);
            }
            catch (Exception e) { Debug.LogException(e); Notice = "載入失敗，請查看主控台中的詳細原因；存檔檔案未被修改。"; }
            hud.Refresh();
        }
        void RestoreView(DemoViewState v)
        {
            simulationAccumulator=Mathf.Clamp(v.simulationRemainder,0,.19999f);visualClock=System.Math.Max(0,v.renderClock);
            visualRotationDegrees=v.hasRenderRotation&&OrbisAtmosphereCore.Finite(v.renderRotationDegrees)
                ?((v.renderRotationDegrees%360)+360)%360
                :(World.rotationAngle*Mathf.Rad2Deg+(World.settings.rotation?simulationAccumulator*360/Mathf.Max(1,World.settings.rotationPeriod):0))%360;
            VisualRotation=(float)visualRotationDegrees;
            StopAtmosphereFlight();lookTilt=smoothLookTilt=Mathf.Clamp(v.lookTilt,0,110);lookHeading=smoothLookHeading=v.lookHeading;lookTiltVelocity=lookHeadingVelocity=0;
            yaw=v.yaw;pitch=Mathf.Clamp(v.pitch,-89.8f,89.8f);altitude=Mathf.Clamp(v.altitude,.003f,5.5f);
            smoothYaw=yaw;smoothPitch=pitch;smoothAltitude=altitude;yawVelocity=pitchVelocity=altitudeVelocity=0;
            mapPan = new Vector2(Mathf.Clamp(v.mapX, -Mathf.PI, Mathf.PI), Mathf.Clamp(v.mapY, -Mathf.PI / 2, Mathf.PI / 2));
            mapZoom = Mathf.Clamp(v.mapZoom, .09f, 3); Speed = v.speed; Paused = v.paused;
            ActiveNation=v.activeNation>=0&&v.activeNation<World.civs.Count?v.activeNation:-1;
            Selected = v.selected;SelectedPlot=v.selectedPlot>=0&&v.selectedPlot/16==Selected?v.selectedPlot:-1; Tool = v.tool; View.layer = v.layer; MapMode = v.map;
            planet.SetActive(!MapMode); Atlas.gameObject.SetActive(MapMode); View.Select(Selected); View.UpdateColors();
            if (MapMode) Atlas.Refresh();smoothPan=mapPan;smoothMapZoom=mapZoom;UpdateCamera();
        }
        public void Screenshot()
        {
            try { Directory.CreateDirectory(Application.persistentDataPath); string path = Path.Combine(Application.persistentDataPath, "orbis-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"); ScreenCapture.CaptureScreenshot(path); Notice = "已提交畫面擷取，PNG 將寫入存檔資料夾。"; }
            catch (Exception e) { Debug.LogException(e); Notice = "畫面擷取失敗，請檢查存檔資料夾權限。"; }
            hud.Refresh();
        }
        public void OpenSaveFolder() { Directory.CreateDirectory(Application.persistentDataPath); Application.OpenURL(new Uri(Application.persistentDataPath + Path.DirectorySeparatorChar).AbsoluteUri); }
        public Vector3 LabelPosition(int tile)
        {
            Vector3 p = World.grid.points[tile];
            return MapMode ? OrbisMapView.Project(p) + Vector3.forward * .03f : planet.transform.TransformPoint(p * (View.VisibleRadiusAt(p)+.005f));
        }
        void OnDestroy() { if (panel) Destroy(panel); }
    }
}
