using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis
{
    public sealed class OrbisHud : MonoBehaviour
    {
        public OrbisDemo demo;
        VisualElement root, viewport, nameLayer;
        Label summary, region, clock, notice, civilizations, chronicle, mission, seasons;
        TextField seedField;
        bool dragging, moved;
        Vector2 down, last;
        int pointerId = -1;Font runtimeFont;
        readonly List<Label> nameLabels = new List<Label>();
        readonly List<int> nameTiles = new List<int>();
        readonly List<Rect> occupied = new List<Rect>();

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            runtimeFont=OrbisSystemFont.Create();if(runtimeFont)root.style.unityFontDefinition=FontDefinition.FromFont(runtimeFont);
            viewport = root.Q("viewport"); nameLayer = root.Q("nameLayer");
            summary = root.Q<Label>("summary"); region = root.Q<Label>("region");
            clock = root.Q<Label>("clock"); notice = root.Q<Label>("notice");
            civilizations = root.Q<Label>("civilizations"); chronicle = root.Q<Label>("chronicle");
            mission = root.Q<Label>("mission"); seasons = root.Q<Label>("seasons");
            seedField = root.Q<TextField>("seed"); seedField.value = demo.World.seed;
            for (int i = 0; i < 13; i++) { int value = i; Bind("layer" + i, () => demo.SetLayer(value)); }
            for (int i = 0; i < 8; i++) { int value = i; Bind("tool" + i, () => demo.SetTool(value)); }
            Bind("pause", demo.TogglePause); Bind("speed", demo.CycleSpeed); Bind("seedCivs", demo.SeedCivs);
            Bind("war", () => demo.SetWar(true)); Bind("peace", () => demo.SetWar(false));
            Bind("settlers", () => demo.SendExpedition(true)); Bind("army", () => demo.SendExpedition(false));
            Bind("clouds", demo.ToggleClouds); Bind("night", demo.ToggleNight); Bind("rotation", demo.ToggleRotation);
            Bind("rivers", demo.ToggleRivers); Bind("geo", demo.JumpGeology);
            Bind("rezone", () => demo.World.RebuildRegions()); Bind("focus", demo.FocusSelected);
            Bind("save", demo.Save); Bind("load", demo.Load); Bind("shot", demo.Screenshot); Bind("folder", demo.OpenSaveFolder);
            Bind("map", demo.ToggleMap); Bind("localView", demo.LocalView); Bind("homeView", demo.HomeView); Bind("findRiver", demo.FindRiver);
            Bind("findRain42",()=>demo.FindWeather(false));Bind("findStorm42",()=>demo.FindWeather(true));
            Bind("rainSurface42",demo.RainSurfaceView);Bind("convectiveExperiment42",demo.ConvectiveExperiment);
            Bind("rainEffects42",()=>demo.World.settings.rainEffects=!demo.World.settings.rainEffects);
            Bind("lightningEffects42",()=>demo.World.settings.lightningEffects=!demo.World.settings.lightningEffects);
            Bind("weatherAudio42",()=>demo.World.settings.weatherAudio=!demo.World.settings.weatherAudio);
            Bind("wetSurfaces42",()=>demo.World.settings.wetSurfaces=!demo.World.settings.wetSurfaces);
            Bind("autoOrbit", demo.ToggleAutoOrbit);
            Bind("atmosphere",()=>demo.World.settings.atmosphere=!demo.World.settings.atmosphere);
            Bind("followSurface",()=>demo.World.settings.followSurface=!demo.World.settings.followSurface);
            Bind("showWind", () => demo.World.settings.showWind = !demo.World.settings.showWind);
            Bind("showCurrents", () => demo.World.settings.showCurrents = !demo.World.settings.showCurrents);
            Bind("showBorders", () => demo.World.settings.showBorders = !demo.World.settings.showBorders);
            Bind("showNames", () => demo.World.settings.showNames = !demo.World.settings.showNames);
            Bind("autoExpeditions", () => demo.World.settings.autoExpeditions = !demo.World.settings.autoExpeditions);
            Bind("autoWar", () => demo.World.settings.autoWar = !demo.World.settings.autoWar);
            Bind("cloudShadows", () => demo.World.settings.cloudShadows = !demo.World.settings.cloudShadows);
            Bind("newWorld", () => root.Q("confirmNew").RemoveFromClassList("hidden"));
            Bind("cancelNew", () => root.Q("confirmNew").AddToClassList("hidden"));
            Bind("confirmGenerate", () => { root.Q("confirmNew").AddToClassList("hidden"); demo.NewWorld(seedField.value); });
            Bind("zen", () => root.Q("layout").ToggleInClassList("zen"));
            Bind("help", () => root.Q("helpPanel").RemoveFromClassList("hidden"));
            Bind("closeHelp", () => root.Q("helpPanel").AddToClassList("hidden"));
            root.Q<Slider>("heat").RegisterValueChangedCallback(e => demo.World.settings.heat = e.newValue);
            root.Q<Slider>("greenhouse").RegisterValueChangedCallback(e => demo.World.settings.greenhouse = e.newValue);
            root.Q<Slider>("period").RegisterValueChangedCallback(e => demo.World.settings.rotationPeriod = e.newValue);
            root.Q<Slider>("wind").RegisterValueChangedCallback(e => demo.World.settings.windPower = e.newValue);
            root.Q<Slider>("tilt").RegisterValueChangedCallback(e => demo.World.settings.tilt = e.newValue);
            root.Q<Slider>("oceanPower").RegisterValueChangedCallback(e => demo.World.settings.oceanPower = e.newValue);
            root.Q<Slider>("monsoon").RegisterValueChangedCallback(e => demo.World.settings.monsoonPower = e.newValue);
            root.Q<Slider>("cloudCover").RegisterValueChangedCallback(e => demo.World.settings.cloudCover = e.newValue);
            root.Q<Slider>("cloudDensity").RegisterValueChangedCallback(e => demo.World.settings.cloudDensity = e.newValue);
            root.Q<Slider>("waterRoughness").RegisterValueChangedCallback(e => demo.World.settings.waterRoughness = e.newValue);
            root.Q<Slider>("surfaceDetail").RegisterValueChangedCallback(e => demo.World.settings.surfaceDetail = e.newValue);
            var quality = root.Q<DropdownField>("cloudQuality");
            quality.choices = new List<string> { "流暢 · 0.35×／雲32／氣16", "均衡 · 0.50×／雲48／氣24", "精細 · 0.65×／雲64／氣32", "參考 · 1.00×／雲64／氣48" };
            quality.RegisterValueChangedCallback(e => demo.World.settings.cloudQuality = quality.index);
            viewport.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 && e.button != 1) return;
                viewport.Focus(); dragging = true; moved = false; down = last = e.position; pointerId = e.pointerId;
                viewport.CapturePointer(pointerId); e.StopPropagation();
            });
            viewport.RegisterCallback<PointerMoveEvent>(e =>
            {
                if(!dragging){Vector2 hover=root.WorldToLocal(e.position);demo.Hover(new Vector2(hover.x/root.resolvedStyle.width,hover.y/root.resolvedStyle.height));return;}
                Vector2 position = e.position;
                if (Vector2.Distance(position, down) > 5) moved = true;
                if (moved) demo.Orbit(position - last);
                last = position;
            });
            viewport.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging || e.pointerId != pointerId) return;
                dragging = false; viewport.ReleasePointer(pointerId); pointerId = -1;
                if (!moved && e.button == 0)
                {
                    Vector2 p = root.WorldToLocal(e.position);
                    demo.Pick(new Vector2(p.x / root.resolvedStyle.width, p.y / root.resolvedStyle.height));
                }
            });
            viewport.RegisterCallback<PointerCaptureOutEvent>(e => { dragging = false; pointerId = -1; });
            viewport.RegisterCallback<WheelEvent>(e => { demo.Zoom(e.delta.y); e.StopPropagation(); });
            viewport.focusable = true;
            viewport.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Space) demo.TogglePause();
                if(e.keyCode==KeyCode.G)demo.World.settings.localGrid=!demo.World.settings.localGrid;
                if (e.keyCode == KeyCode.F) demo.FocusSelected();
                if (e.keyCode == KeyCode.M) demo.ToggleMap();
                if (e.keyCode == KeyCode.H) root.Q("layout").ToggleInClassList("zen");
            });
            Bind("surfaceLOD",()=>demo.World.settings.surfaceLOD=!demo.World.settings.surfaceLOD);
            Bind("localGrid",()=>demo.World.settings.localGrid=!demo.World.settings.localGrid);
            Bind("weatherLive",()=>demo.World.settings.dynamicWeather=!demo.World.settings.dynamicWeather);
            Bind("multipleScattering",()=>demo.World.settings.multipleScattering=!demo.World.settings.multipleScattering);
            Bind("enhancedWater",()=>demo.World.settings.enhancedWater=!demo.World.settings.enhancedWater);
            Bind("enterAtmosphere",demo.EnterAtmosphere);Bind("exitAtmosphere",demo.ExitAtmosphere);
            Bind("atmosphereFlight",demo.ToggleAtmosphereFlight);Bind("duskView",demo.ObserveAtmosphereDusk);
            root.Q<Slider>("lookTilt").RegisterValueChangedCallback(e=>demo.SetAtmosphereLook(e.newValue));
            Bind("volumeReference",()=>{var v=demo.PlanetCamera.GetComponent<OrbisAtmosphereRenderer>();v.fullResolutionReference=!v.fullResolutionReference;});
            root.Q<Slider>("airDensity").RegisterValueChangedCallback(e=>demo.World.settings.airDensity=e.newValue);
            root.Q<Slider>("haze").RegisterValueChangedCallback(e=>demo.World.settings.haze=e.newValue);
            root.Q<Slider>("scatteringStrength").RegisterValueChangedCallback(e=>demo.World.settings.scatteringStrength=e.newValue);
            root.Q<Slider>("waveStrength").RegisterValueChangedCallback(e=>demo.World.settings.waveStrength=e.newValue);
            var transport=root.Q<DropdownField>("transportMode");transport.choices=new List<string>{"跟隨風向／洋流開關","切向風場","水汽輸運","海洋流向"};
            transport.RegisterValueChangedCallback(e=>demo.World.settings.transportView=transport.index);
            var debug=root.Q<DropdownField>("cloudDebug");debug.choices=new List<string>{"最終合成","雲密度","直接光","多次散射近似","太陽透射","密度熱力"};debug.index=0;
            debug.RegisterValueChangedCallback(e=>demo.PlanetCamera.GetComponent<OrbisAtmosphereRenderer>().cloudDebug=debug.index);
            var surfaceDebug=root.Q<DropdownField>("surfaceDebug");
            surfaceDebug.choices=new List<string>{"最終渲染","原始底色","朝陽程度 N·L","太陽透射率","雲影透射率","積雪遮罩"};
            surfaceDebug.RegisterValueChangedCallback(e=>demo.World.settings.surfaceDebug=surfaceDebug.index);
            Bind("godRays",()=>demo.World.settings.godRays=!demo.World.settings.godRays);
            Bind("mapClouds",()=>demo.World.settings.mapClouds=!demo.World.settings.mapClouds);
            root.Q<Slider>("exposure").RegisterValueChangedCallback(e=>demo.World.settings.exposure=e.newValue);
            root.Q<Slider>("shadowStrength").RegisterValueChangedCallback(e=>demo.World.settings.shadowStrength=e.newValue);
            var selection=root.Q<DropdownField>("selectionMode");selection.choices=new List<string>{"4×4 Zone（領土）","自然地貌（觀察）","自然區塊（觀察）"};
            selection.RegisterValueChangedCallback(e=>demo.SetSelectionMode(selection.index));
            Bind("foundNation",demo.FoundSelectedNation);Bind("useNation",demo.SelectNationFromTile);Bind("claimRegion",demo.ClaimSelectedRegion);
            Bind("regionExpansion",()=>demo.World.settings.regionExpansion=!demo.World.settings.regionExpansion);
            Bind("spriteActors",()=>demo.World.settings.spriteActors=!demo.World.settings.spriteActors);
            root.Q<TextField>("mapFolder").value=OrbisMapIO.ImportFolder;
            Bind("mapFolderOpen",demo.OpenMapImportFolder);Bind("exportMaps",demo.ExportMaps);
            Bind("importMaps",()=>root.Q("confirmImport").RemoveFromClassList("hidden"));
            Bind("exampleMap",()=>root.Q("confirmExample").RemoveFromClassList("hidden"));
            Bind("cancelImport",()=>root.Q("confirmImport").AddToClassList("hidden"));
            Bind("cancelExample",()=>root.Q("confirmExample").AddToClassList("hidden"));
            Bind("confirmImportGo",()=>{root.Q("confirmImport").AddToClassList("hidden");demo.ImportMapFolder(root.Q<TextField>("mapFolder").value);});
            Bind("confirmExampleGo",()=>{root.Q("confirmExample").AddToClassList("hidden");demo.LoadExampleMap();});
            viewport.RegisterCallback<PointerLeaveEvent>(e=>demo.ClearHover());
            Bind("terrainUndo",demo.UndoTerrain);Bind("terrainRedo",demo.RedoTerrain);Bind("terrainSampleHeight",demo.SetFlattenFromSelection);
            Bind("performanceCapture",demo.BeginPerformanceCapture);Bind("performanceExport",demo.ExportPerformanceCapture);
            root.Q<Slider>("terrainBrushRadius").RegisterValueChangedCallback(e=>demo.World.settings.terrainBrushRadius=e.newValue);
            root.Q<Slider>("terrainBrushStrength").RegisterValueChangedCallback(e=>demo.World.settings.terrainBrushStrength=e.newValue);
            root.Q<Slider>("terrainFlattenHeight").RegisterValueChangedCallback(e=>demo.World.settings.terrainFlattenHeight=e.newValue);
            var terrainQuality=root.Q<DropdownField>("terrainQuality");terrainQuality.choices=new List<string>{"48 × 48","64 × 64","96 × 96","128 × 128"};
            terrainQuality.RegisterValueChangedCallback(e=>demo.World.settings.terrainQuality=terrainQuality.index);
            SyncSettings(); Refresh();
        }
        void Bind(string name, System.Action action) { root.Q<Button>(name).clicked += action; }
        void SliderValue(string name, float value) { root.Q<Slider>(name).SetValueWithoutNotify(value); }
        public void SyncSettings()
        {
            PlanetSettings s = demo.World.settings;
            root.Q<DropdownField>("selectionMode").SetValueWithoutNotify(new[]{"4×4 Zone（領土）","自然地貌（觀察）","自然區塊（觀察）"}[s.selectionMode]);
            SliderValue("heat", s.heat); SliderValue("greenhouse", s.greenhouse); SliderValue("period", s.rotationPeriod);
            SliderValue("wind", s.windPower); SliderValue("tilt", s.tilt); SliderValue("oceanPower", s.oceanPower);
            SliderValue("monsoon", s.monsoonPower); SliderValue("cloudCover", s.cloudCover); SliderValue("cloudDensity", s.cloudDensity);
            SliderValue("waterRoughness", s.waterRoughness); SliderValue("surfaceDetail", s.surfaceDetail);
            root.Q<DropdownField>("cloudQuality").index = s.cloudQuality;
            SliderValue("airDensity",s.airDensity);SliderValue("haze",s.haze);SliderValue("scatteringStrength",s.scatteringStrength);SliderValue("waveStrength",s.waveStrength);
            root.Q<DropdownField>("transportMode").index=s.transportView;
            root.Q<DropdownField>("surfaceDebug").index=s.surfaceDebug;
            SliderValue("exposure",s.exposure);SliderValue("shadowStrength",s.shadowStrength);
            SliderValue("terrainBrushRadius",Mathf.Clamp(s.terrainBrushRadius,.02f,.35f));
            SliderValue("terrainBrushStrength",Mathf.Clamp(s.terrainBrushStrength,.01f,.25f));
            SliderValue("terrainFlattenHeight",Mathf.Clamp(s.terrainFlattenHeight,-.5f,1));
            root.Q<DropdownField>("terrainQuality").index=Mathf.Clamp(s.terrainQuality,0,3);
            seedField.SetValueWithoutNotify(demo.World.seed);
        }
        void ToggleText(string name, string label, bool active) { root.Q<Button>(name).text = label + "：" + ChineseText.OnOff(active); }
        public void Refresh()
        {
            if (root == null || demo.World == null) return;
            OrbisWorld w = demo.World; PlanetSettings s = w.settings;
            root.Q<Label>("weatherStatus42").text=demo.WeatherStatus();
            ToggleText("rainEffects42","雨滴水花",s.rainEffects);ToggleText("lightningEffects42","閃電",s.lightningEffects);
            ToggleText("weatherAudio42","天氣聲音",s.weatherAudio);ToggleText("wetSurfaces42","濕潤材質",s.wetSurfaces);
            summary.text = w.WorldName + "　陸地 " + (100f * w.LandCount / w.tiles.Length).ToString("F0") + "%　自然區塊 " + w.geography.chunks.Length + "　人口 " + w.Population.ToString("N0");
            clock.text = "第 " + (1 + (int)(w.day / 360)).ToString("D3") + " 年　第 " + (1 + (int)(w.day % 360)).ToString("D3") + " 日　" + (demo.Paused ? "已暫停" : "世界演化中");
            root.Q<Button>("pause").text = demo.Paused ? "繼續" : "暫停";
            root.Q<Button>("speed").text = demo.Speed.ToString("F0") + " 倍速";
            root.Q<Button>("map").text = demo.MapMode ? "返回星球" : "展開地圖";
            ToggleText("clouds", "雲層", s.clouds); ToggleText("night", "晝夜", s.night);
            ToggleText("rotation", "自轉", s.rotation); ToggleText("rivers", "河流", demo.View.showRivers);
            ToggleText("atmosphere","大氣",s.atmosphere);ToggleText("followSurface","全程跟隨",s.followSurface);
            ToggleText("showWind", "風場流線", s.showWind); ToggleText("showCurrents", "洋流流線", s.showCurrents);
            ToggleText("showNames", "地名", s.showNames); ToggleText("showBorders", "國界", s.showBorders);
            ToggleText("autoExpeditions", "自動派遣", s.autoExpeditions); ToggleText("autoWar", "邊境戰爭", s.autoWar);
            ToggleText("autoOrbit", "環繞觀察", demo.AutoOrbit); ToggleText("cloudShadows", "雲影", s.cloudShadows);
            root.Q<Label>("planetValues").text = "日照 " + s.heat.ToString("F2") + "　溫室 " + s.greenhouse.ToString("F2") + "\n自轉 " + s.rotationPeriod.ToString("F0") + " 秒　傾角 " + s.tilt.ToString("F1") + "°";
            for (int i = 0; i < 13; i++) root.Q<Button>("layer" + i).EnableInClassList("active", demo.View.layer == i);
            for (int i = 0; i < 8; i++) root.Q<Button>("tool" + i).EnableInClassList("active", demo.Tool == i);
            notice.text = demo.Notice;
            root.Q<Button>("terrainUndo").SetEnabled(demo.TerrainUndoCount>0);root.Q<Button>("terrainRedo").SetEnabled(demo.TerrainRedoCount>0);
            root.Q<Label>("terrainPerformance").text=demo.PerformanceStatus();
            ToggleText("regionExpansion","逐 Zone 拓展",s.regionExpansion);ToggleText("spriteActors","2D 建築／居民",s.spriteActors);
            int count=w.SelectionTiles(demo.Selected,s.selectionMode).Length;
            root.Q<Label>("v4Status").text="觀察 "+count+" 個 Zone；圈地固定 1 Zone / 16 小地塊 · 國家 "+(demo.ActiveNation>=0&&demo.ActiveNation<w.civs.Count?w.civs[demo.ActiveNation].name:"未指定")+
                "\nZone #"+demo.Selected+(demo.SelectedPlot>=0?" · 小地塊 ("+(demo.SelectedPlot%4+1)+","+(demo.SelectedPlot%16/4+1)+")":"")+"\n河道曲線段 "+demo.View.Rivers.reaches.Count.ToString("N0")+" · "+(w.MapSource!=null?"雙圖地形":"程序地形");
            ToggleText("surfaceLOD","近地細分",s.surfaceLOD);ToggleText("localGrid","4×4 地塊格線",s.localGrid);
            ToggleText("weatherLive","動態天氣",s.dynamicWeather);ToggleText("multipleScattering","雲內散射",s.multipleScattering);
            ToggleText("enhancedWater","統一水體",s.enhancedWater);
            ToggleText("godRays","雲隙光",s.godRays);ToggleText("mapClouds","展開圖雲層",s.mapClouds);
            ToggleText("volumeReference","全解析度對照",demo.PlanetCamera.GetComponent<OrbisAtmosphereRenderer>().fullResolutionReference);
            root.Q<Slider>("lookTilt").SetValueWithoutNotify(demo.LookTilt);
            root.Q<Button>("atmosphereFlight").text=demo.AtmosphereFlightActive?"停止穿越演示":"穿越演示";
            root.Q<Label>("v3Status").text=demo.ViewBand+" · 目標離地 "+demo.Altitude.ToString("F4")+" R\n局部網格 "+demo.View.LocalSurface.VertexCount.ToString("N0")+" 頂點 · 邏輯地塊不變";
            float seasonal = Mathf.Repeat(w.day - 80, 360);
            string[] northSeasons = { "春季", "夏季", "秋季", "冬季" };
            int season = (int)(seasonal / 90);
            float declination = Mathf.Asin(Mathf.Sin(s.tilt * Mathf.Deg2Rad) * Mathf.Sin(seasonal * 2 * Mathf.PI / 360)) * Mathf.Rad2Deg;
            seasons.text = "北半球：" + northSeasons[season] + "　南半球：" + northSeasons[(season + 2) % 4] + "\n太陽直射緯度 " + declination.ToString("F1") + "°　地質時鐘 " + w.geo.ToString("F0") + " 百萬年";
            if (demo.Selected >= 0)
            {
                int id = demo.Selected; Tile t = w.tiles[id]; Vector3 p = w.grid.points[id];
                float lon = Mathf.Atan2(p.x, p.z), hour = Mathf.Repeat(12 + (lon + demo.VisualRotation*Mathf.Deg2Rad) / (2 * Mathf.PI) * 24, 24);
                string chunk = t.chunkId >= 0 ? w.geography.chunks[t.chunkId].name : "未分類地區";
                region.text = w.PlaceName(id) + "\n" + chunk + "\n地塊 " + id + " · 區域 " + t.zoneId + " · 區塊 " + t.chunkId +
                    "\n\n" + t.Temperature.ToString("F1") + "°C　降水指數 " + t.rain.ToString("F0") + "（遊戲量）" +
                    "\n海拔／水深　" + (t.h * 5500).ToString("F0") + " 公尺" +
                    "\n土壤水分　" + (t.soil * 100).ToString("F0") + "%　雲量 " + (t.cloud * 100).ToString("F0") + "%" +
                    "\n水汽／雲水　"+t.vapor.ToString("F2")+"／"+t.cloudWater.ToString("F2") +
                    "\n氣壓模型　"+t.pressure.ToString("F0")+" 百帕　濕度 "+(t.moist*100).ToString("F0")+"%"+
                    "\n"+w.CloudKind(id)+
                    "\n當地時間　" + ((int)hour).ToString("D2") + ":" + ((int)((hour % 1) * 60)).ToString("D2") +
                    "\n" + w.RiverName(id) + "　流量 " + t.flow.ToString("F1") +
                    "\n風場強度　" + t.wind.magnitude.ToString("F2") + "（遊戲量）" +
                    "\n人口　" + t.pop.ToString("N0") + "　歸屬 " + (t.owner >= 0 ? w.civs[t.owner].name : "無主地") +
                    "\n緯度 " + (Mathf.Asin(p.y) * Mathf.Rad2Deg).ToString("F1") + "°　經度 " + (lon * Mathf.Rad2Deg).ToString("F1") + "°";
            }
            civilizations.text = "";
            for (int i = 0; i < w.civs.Count; i++)
            {
                float population = 0; int tiles = 0, teams = 0;
                foreach (var t in w.tiles) if (t.owner == i) { population += t.pop; tiles++; }
                foreach (var e in w.expeditions) if (e.civ == i) { population += e.strength; teams++; }
                civilizations.text += w.civs[i].name + "　" + population.ToString("N0") + " 人／" + tiles + " 地塊\n" + ChineseText.Era(w.civs[i].tech) + "　遠征隊 " + teams + " 支\n\n";
            }
            if (w.civs.Count == 0) civilizations.text = "世界尚未出現文明。";
            chronicle.text = string.Join("\n\n", w.events.GetRange(0, Mathf.Min(6, w.events.Count)));
            mission.text = w.stability >= 360 ? "生態平衡達成\n世界已維持一整年的和平與宜居環境，可以繼續探索。" :
                "生機勃勃的世界\n至少四個文明、總人口五千人，平均溫度維持在 5–28°C，和平累積 360 日。\n\n" +
                Mathf.Min(4, w.AliveCivilizations) + "／4 個文明　" + Mathf.Min(5000, w.Population).ToString("N0") + "／5,000 人\n平均 " + w.MeanTemperature.ToString("F1") + "°C　穩定 " + w.stability.ToString("F0") + "／360 日";
            RefreshNames();
        }
        void RefreshNames()
        {
            nameTiles.Clear(); int count = 0;
            foreach (var c in demo.World.civs)
                if (c.capital >= 0 && demo.World.tiles[c.capital].city && demo.World.tiles[c.capital].owner == demo.World.civs.IndexOf(c)) SetName(count++, c.capital, c.name + "城");
            foreach (var place in demo.World.namedPlaces) { if (count >= 24) break; SetName(count++, place.tile, place.name); }
            for (int i = count; i < nameLabels.Count; i++) nameLabels[i].AddToClassList("hidden");
        }
        void SetName(int index, int tile, string text)
        {
            if (index >= nameLabels.Count)
            {
                var label = new Label { pickingMode = PickingMode.Ignore }; label.AddToClassList("map-name"); nameLayer.Add(label); nameLabels.Add(label);
            }
            nameTiles.Add(tile); nameLabels[index].text = text;
        }
        public void UpdateNamePositions()
        {
            if (root == null || demo.PlanetCamera == null) return;
            occupied.Clear(); float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            for (int i = 0; i < nameTiles.Count; i++)
            {
                int tile = nameTiles[i]; Vector3 position = demo.LabelPosition(tile);
                Vector3 screen = demo.PlanetCamera.WorldToViewportPoint(position);
                bool visible = demo.World.settings.showNames && screen.z > 0 && screen.x > .02f && screen.x < .98f && screen.y > .08f && screen.y < .89f;
                if (!demo.MapMode) visible &= Vector3.Dot(position.normalized, (demo.PlanetCamera.transform.position - position).normalized) > .15f;
                float x = screen.x * width, y = (1 - screen.y) * height;
                Vector2 viewportPoint = viewport.WorldToLocal(root.LocalToWorld(new Vector2(x, y)));
                visible &= viewport.contentRect.Contains(viewportPoint);
                var rectangle = new Rect(x - 45, y - 12, 105, 26);
                foreach (Rect other in occupied) if (other.Overlaps(rectangle)) { visible = false; break; }
                nameLabels[i].EnableInClassList("hidden", !visible);
                if (visible) { nameLabels[i].transform.position = new Vector3(x - 35, y - 10, 0); occupied.Add(rectangle); }
            }
        }
        void OnDestroy(){if(runtimeFont)Destroy(runtimeFont);}
    }
}
