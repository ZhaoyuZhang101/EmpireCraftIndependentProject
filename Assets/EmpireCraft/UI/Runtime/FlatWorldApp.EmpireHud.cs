using System;
using System.Collections.Generic;
using EmpireCraft.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.Flat
{
    // EmpireCraft civilization HUD over the ORBIS flat-world app:
    //   - Clean 12px perimeter layout keeping the center viewport unobstructed;
    //   - 观察 mode uses 4 live layer categories with auto-closing popout;
    //   - 塑造 / 文明 / 营建 expose 1-click direct rail actions without secondary popups or dead buttons;
    //   - Contextual tile card (auto-hidden until a tile is selected) & bottom-center active-tool bar;
    //   - Every panel and ScrollWindow remains collapsible into a draggable pill and freely draggable.
    public sealed partial class FlatWorldApp
    {
        const string HudThemePref = "EmpireCraft.UiTheme";
        static readonly string[] HudThemes = { "star", "atlas", "slate" };
        static readonly string[] HudThemeNames = { "星图青铜", "舆图羊皮", "石青白玉" };
        const int HudFeedRows = 8, HudCompactRows = 6;

        VisualElement hud, hudEra, hudModes, hudModesBody, hudViews, hudViewsBody, hudRail, hudRailList,
            hudPop, hudPopGrid, hudPopIcon, hudInfo, hudInfoButtons, hudBrush, hudBrushBody,
            hudTerrainBrushGroup, hudActiveToolGroup, hudWindows, hudLegend, hudClock, hudInfoIcon,
            hudDemoReturnBtn, hudLegendResetBtn, hudInfoNationBtn, hudTerrainBrushDoneBtn;
        ScrollView hudInfoRows, hudFeedRows;
        Label hudDate, hudDateSub, hudRailTitle, hudPopTitle, hudPopNote, hudPopHint,
            hudInfoTitle, hudInfoNote, hudLegendTitle, hudLegendText, hudActiveToolName, hudActiveToolHint;
        VisualElement hudPause, hudSpeed, hudExpand;
        readonly List<VisualElement> hudModeButtons = new List<VisualElement>(), hudRailButtons = new List<VisualElement>();
        readonly List<(VisualElement el, HudTool tool)> hudToolButtons = new List<(VisualElement, HudTool)>();
        readonly List<(VisualElement el, HudTool tool)> hudDirectRailTools = new List<(VisualElement, HudTool)>();
        readonly List<(VisualElement el, Func<bool> on)> hudViewButtons = new List<(VisualElement, Func<bool>)>();
        readonly List<(VisualElement el, int brush)> hudBrushChips = new List<(VisualElement, int)>();
        readonly List<(VisualElement row, Label key, Label val)> hudInfoPool = new List<(VisualElement, Label, Label)>();
        readonly List<(VisualElement row, Label key, Label val)> hudFeedPool = new List<(VisualElement, Label, Label)>();
        readonly List<(string when, string text)> hudFeed = new List<(string, string)>();
        readonly List<HudFloatingPanel> hudFloatingPanels = new List<HudFloatingPanel>();
        readonly Dictionary<string, HudFloatingPanel> hudPanelsById = new Dictionary<string, HudFloatingPanel>();
        readonly Dictionary<int, (string name, string hint)> hudToolPrompts = new Dictionary<int, (string, string)>();
        HudFloatingPanel hudEraPanel, hudModesPanel, hudViewsPanel, hudRailPanel, hudPopPanel,
            hudInfoPanel, hudLegendPanel, hudFeedPanel, hudBrushPanel, hudMinimapPanel;
        Label hudHeightLabel;
        List<HudMode> hudModeList;
        int hudMode, hudCategory = -1, hudTheme;
        bool hudInfoExpanded;
        string hudLastStatus, hudInfoSource;
        int hudInfoLastSelected = int.MinValue;
        ScrollWindow hudWindow;
        float hudWindowAge;

        void InstallEmpireHud()
        {
            var sheet = Resources.Load<StyleSheet>("EmpireCraft/EmpireHudStyle");
            var tree = Resources.Load<VisualTreeAsset>("EmpireCraft/EmpireHud");
            if (!sheet || !tree) { Debug.LogWarning("EmpireCraft HUD assets missing; keeping the ORBIS sidebar."); return; }
            root.styleSheets.Add(sheet);
            hud = tree.Instantiate().Q("ecHud");
            var app = viewport.parent;
            app.Insert(app.IndexOf(viewport) + 1, hud);
            root.AddToClassList("ec-hud-on");
            hudTheme = Mathf.Clamp(PlayerPrefs.GetInt(HudThemePref, 0), 0, HudThemes.Length - 1);
            ApplyHudTheme(hudTheme);

            hudEra = hud.Q("ecEra");
            hudModes = hud.Q("ecModes"); hudModesBody = hud.Q("ecModesBody");
            hudViews = hud.Q("ecViews"); hudViewsBody = hud.Q("ecViewsBody");
            hudRail = hud.Q("ecRail"); hudRailList = hud.Q("ecRailList"); hudRailTitle = hud.Q<Label>("ecRailTitle");
            hudPop = hud.Q("ecPop"); hudPopGrid = hud.Q("ecPopGrid"); hudPopIcon = hud.Q("ecPopIcon");
            hudPopTitle = hud.Q<Label>("ecPopTitle"); hudPopNote = hud.Q<Label>("ecPopNote"); hudPopHint = hud.Q<Label>("ecPopHint");
            hudInfo = hud.Q("ecInfo"); hudInfoRows = hud.Q<ScrollView>("ecInfoRows"); hudInfoButtons = hud.Q("ecInfoButtons"); hudInfoIcon = hud.Q("ecInfoIcon");
            hudInfoTitle = hud.Q<Label>("ecInfoTitle"); hudInfoNote = hud.Q<Label>("ecInfoNote");
            hudFeedRows = hud.Q<ScrollView>("ecFeedRows");
            hudBrush = hud.Q("ecBrush"); hudBrushBody = hud.Q("ecBrushBody");
            hudWindows = hud.Q("ecWindows");
            hudLegend = hud.Q("ecLegend"); hudLegendTitle = hud.Q<Label>("ecLegendTitle"); hudLegendText = hud.Q<Label>("ecLegendText");
            hudDate = hud.Q<Label>("ecDate"); hudDateSub = hud.Q<Label>("ecDateSub"); hudClock = hud.Q("ecClock");

            Hud.SetIcon(hud.Q("ecFeedIcon"), "iconHistory");
            Hud.SetIcon(hudInfoIcon, "civ_surface");
            Hud.SetIcon(hud.Q("ecLegendIcon"), "nat_terrain");

            var eraText = hud.Q("ecEraText");
            if (eraText != null)
            {
                eraText.tooltip = "点击查看列国榜";
                eraText.AddManipulator(new Clickable(OpenHudNations));
            }

            hudPause = Hud.Button("ec-ib", null, "暂停", () => { paused = !paused; UpdateHUD(); TickEmpireHud(); });
            hudPause.tooltip = "暂停 / 继续 (Space)";
            hudSpeed = Hud.Button("ec-ib", null, "1×", () => { speed = speed == 1 ? 4 : speed == 4 ? 12 : speed == 12 ? 20 : 1; UpdateHUD(); TickEmpireHud(); });
            hudSpeed.tooltip = "左键加速切换，右键减速";
            hudSpeed.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                speed = speed == 20 ? 12 : speed == 12 ? 4 : speed == 4 ? 1 : 20;
                UpdateHUD(); TickEmpireHud();
                e.StopPropagation();
            });
            hudClock.Add(hudPause); hudClock.Add(hudSpeed);

            BuildHudViews();
            BuildHudInfoButtons();
            BuildHudBrush();
            InstallFloatingPanels();

            hudModeList = BuildHudModes();
            for (int i = 0; i < hudModeList.Count; i++)
            {
                int index = i;
                var b = Hud.Button("ec-mode", hudModeList[i].Icon, hudModeList[i].Name, () => SelectHudMode(index));
                hudModeButtons.Add(b); hudModesBody.Add(b);
            }
            hudWindows.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == hudWindows && (hudWindow == null || !hudWindow.IsCollapsed)) CloseHudWindow();
            });
            viewport.RegisterCallback<PointerDownEvent>(OnHudViewportPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(HudKeys, TrickleDown.TrickleDown);
            SelectHudMode(0);
            hud.schedule.Execute(TickEmpireHud).Every(250);
        }

        void OnHudViewportPointerDown(PointerDownEvent e)
        {
            if (minimap != null && minimap.Frame != null && minimap.Frame.Contains(e.target as VisualElement)) return;
            // Touching the map automatically closes any open layer popout so it never blocks sightlines.
            if (hudCategory >= 0) ShowHudCategory(-1);
            // Right-clicking on the map cancels any active build/edit tool (except SeaRoute, which resets its origin).
            if (e.button == 1 && toolField != null && toolField.index != 0 && toolField.value != SeaRouteToolName)
            {
                CancelActiveHudTool();
                e.StopPropagation();
            }
        }

        void CancelActiveHudTool()
        {
            if (toolField == null || toolField.index == 0) return;
            if (toolField.value == SeaRouteToolName) ResetSeaRoute();
            toolField.index = 0;
            navigation?.SetRoute(Array.Empty<int>());
            SetStatus("已返回地图浏览模式。");
            UpdateHUD();
            TickEmpireHud();
        }

        void ClearHudSelection()
        {
            selected = -1;
            map?.Select(-1);
            if (toolField != null && toolField.index != 0) toolField.index = 0;
            UpdateHUD();
            TickEmpireHud();
        }

        void InstallFloatingPanels()
        {
            hudFloatingPanels.Clear();
            hudPanelsById.Clear();

            // Perimeter bars: surface drag + double-click reset + unified right-side collapse button.
            hudEraPanel = RegisterBarPanel("era", hudEra, hud.Q("ecEraGrip"), "nat_daynight", "纪年", rightAligned: false, bottomAligned: false);
            hudModesPanel = RegisterBarPanel("modes", hudModes, hud.Q("ecModesGrip"), "civ_observe", "模式", rightAligned: false, bottomAligned: false);
            hudViewsPanel = RegisterBarPanel("views", hudViews, hud.Q("ecViewsGrip"), "civ_globe", "视图", rightAligned: true, bottomAligned: false);
            hudBrushPanel = RegisterBarPanel("brush", hudBrush, hud.Q("ecBrushGrip"), "nat_level", "笔刷", rightAligned: false, bottomAligned: true);

            // Left action/category rail: compact title + right-side collapse button.
            var railHead = hud.Q("ecRailHead");
            railHead.tooltip = "按住拖动侧栏；双击复位";
            hudRailPanel = RegisterPanel(new HudFloatingPanel(
                "rail", hudRail, hud, railHead, "civ_observe", "侧栏",
                rightAligned: false, bottomAligned: false, dragOnPanelSurface: true));
            railHead.Add(hudRailPanel.CreateCollapseButton("折叠侧栏"));

            // Layer popout grid (used only in 观察 mode): header drag + collapse + close.
            var popHead = hud.Q("ecPopHead");
            popHead.tooltip = "按住拖动选单；双击复位";
            var popActions = hud.Q("ecPopActions");
            hudPopPanel = RegisterPanel(new HudFloatingPanel(
                "pop", hudPop, hud, popHead, "nat_terrain", "图层",
                rightAligned: false, bottomAligned: false, dragOnPanelSurface: false));
            popActions.Add(hudPopPanel.CreateCollapseButton("折叠图层选单"));
            popActions.Add(Hud.MiniButton("×", "关闭选单 (Esc)", () => ShowHudCategory(-1)));
            hudPopPanel.SetLogicalVisible(false);

            // Contextual tile info card: hidden until a tile is selected; header has collapse + 1-click close.
            var infoHead = hud.Q("ecInfoHead");
            infoHead.tooltip = "按住拖动地块卡；双击复位";
            var infoActions = hud.Q("ecInfoActions");
            hudInfoPanel = RegisterPanel(new HudFloatingPanel(
                "info", hudInfo, hud, infoHead, "civ_surface", "地块",
                rightAligned: true, bottomAligned: false, dragOnPanelSurface: false));
            infoActions.Add(hudInfoPanel.CreateCollapseButton("折叠地块信息"));
            infoActions.Add(Hud.MiniButton("×", "取消选中并收起 (Esc)", ClearHudSelection));
            hudInfoPanel.SetLogicalVisible(false);

            // Map legend card: includes 1-click reset to Natural terrain + collapse button.
            var legendHead = hud.Q("ecLegendHead");
            legendHead.tooltip = "按住拖动图例；双击复位";
            var legendActions = hud.Q("ecLegendActions");
            hudLegendPanel = RegisterPanel(new HudFloatingPanel(
                "legend", hudLegend, hud, legendHead, "nat_terrain", "图例",
                rightAligned: false, bottomAligned: true, dragOnPanelSurface: true));
            hudLegendResetBtn = Hud.MiniButton("自然", "一键切回自然地表", () => { ChooseLayer(MapLayer.Natural); TickEmpireHud(); });
            legendActions.Add(hudLegendResetBtn);
            legendActions.Add(hudLegendPanel.CreateCollapseButton("折叠图例"));

            // Compact bottom-left chronicle feed.
            var feed = hud.Q("ecFeed");
            var feedHead = hud.Q("ecFeedHead");
            feedHead.tooltip = "按住拖动天下大事；双击复位";
            var feedActions = hud.Q("ecFeedActions");
            hudFeedPanel = RegisterPanel(new HudFloatingPanel(
                "feed", feed, hud, feedHead, "iconHistory", "天下大事",
                rightAligned: false, bottomAligned: true, dragOnPanelSurface: true));
            feedActions.Add(hudFeedPanel.CreateCollapseButton("折叠天下大事"));

            // Minimap inside viewport: compact draggable/collapsible header strip.
            if (minimap != null && minimap.Frame != null)
            {
                var miniFrame = minimap.Frame;
                var miniHead = new VisualElement { name = "ecMinimapHead", tooltip = "按住拖动小地图；双击复位" };
                miniHead.AddToClassList("ec-mini-hd");
                miniHead.Add(Hud.Icon("civ_atlas", "ec-sm"));
                miniHead.Add(Hud.Text("小地图", "ec-ch-title"));
                var miniActions = new VisualElement();
                miniActions.AddToClassList("ec-ch-actions");
                miniHead.Add(miniActions);
                miniFrame.Insert(0, miniHead);

                hudMinimapPanel = RegisterPanel(new HudFloatingPanel(
                    "minimap", miniFrame, viewport, miniHead, "civ_atlas", "小地图",
                    rightAligned: true, bottomAligned: true, dragOnPanelSurface: false,
                    onCollapsedChanged: collapsed => minimap?.SetCollapsed(collapsed)));
                miniActions.Add(hudMinimapPanel.CreateCollapseButton("折叠小地图"));
                minimap.SetCollapsed(hudMinimapPanel.IsCollapsed);
                hudMinimapPanel.SetLogicalVisible(World != null);
            }

            hudWindows.BringToFront();
        }

        HudFloatingPanel RegisterBarPanel(string id, VisualElement panel, VisualElement endHost, string icon, string title, bool rightAligned, bool bottomAligned)
        {
            panel.tooltip = "按住空白处拖动「" + title + "」；双击复位";
            var ctrl = RegisterPanel(new HudFloatingPanel(
                id, panel, hud, panel, icon, title,
                rightAligned: rightAligned, bottomAligned: bottomAligned, dragOnPanelSurface: true));
            endHost?.Add(ctrl.CreateCollapseButton("折叠「" + title + "」"));
            return ctrl;
        }

        HudFloatingPanel RegisterPanel(HudFloatingPanel panel)
        {
            hudFloatingPanels.Add(panel);
            hudPanelsById[panel.Id] = panel;
            return panel;
        }

        void SetAllHudPanelsCollapsed(bool collapsed, bool save = true)
        {
            foreach (var p in hudFloatingPanels) p.SetCollapsed(collapsed, save);
            hudWindow?.SetCollapsed(collapsed);
        }

        void ResetHudLayout(bool save = true)
        {
            foreach (var p in hudFloatingPanels) p.ResetLayout(save);
            hudWindow?.Floating?.ResetLayout(save: false);
            hudWindows?.BringToFront();
        }

        void ApplyHudTheme(int theme)
        {
            hudTheme = theme;
            for (int i = 0; i < HudThemes.Length; i++) root.EnableInClassList("theme-" + HudThemes[i], i == theme);
            PlayerPrefs.SetInt(HudThemePref, theme);
        }

        void HudKeys(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Escape) return;
            if (hudWindow != null && !hudWindow.IsCollapsed) CloseHudWindow();
            else if (hudCategory >= 0) ShowHudCategory(-1);
            else if (toolField.index != 0) CancelActiveHudTool();
            else if (selected >= 0) ClearHudSelection();
            else return;
            e.StopPropagation();
        }

        void EnsureFlatSurfaceForTool()
        {
            if (World == null || Busy || !navigation || screenshotMode) return;
            if (!navigation.IsFlat) Focus();
        }

        // ---------- catalog (zero dead placeholders; 1-click direct rails for 塑造/文明/营建) ----------
        HudTool HudLayer(string name, string icon, MapLayer layer)
        {
            return new HudTool
            {
                Name = name,
                Icon = icon,
                Hint = "切换为「" + name + "」图层。",
                Run = () => ChooseLayer(layer),
                IsOn = () => layerField.index == (int)layer
            };
        }

        HudTool HudEdit(string name, string icon, int tool, string hint, bool autoFocusSurface = true)
        {
            hudToolPrompts[tool] = (name, hint);
            return new HudTool
            {
                Name = name,
                Icon = icon,
                Hint = hint,
                ToolIndex = tool,
                Run = () =>
                {
                    bool enabling = toolField.index != tool;
                    toolField.index = enabling ? tool : 0;
                    if (enabling)
                    {
                        if (autoFocusSurface) EnsureFlatSurfaceForTool();
                        SetStatus(name + "：" + hint);
                    }
                    else
                    {
                        navigation?.SetRoute(Array.Empty<int>());
                        SetStatus("已返回地图浏览模式。");
                    }
                    TickEmpireHud();
                },
                IsOn = () => toolField.index == tool
            };
        }

        static HudTool HudAction(string name, string icon, Action run, string hint, bool stayOpen = false)
        {
            return new HudTool { Name = name, Icon = icon, Hint = hint, Run = run, StayOpen = stayOpen };
        }

        static HudTool HudWindow(string name, string icon, Action open, string hint)
        {
            return new HudTool { Name = name, Icon = icon, Hint = hint, Run = open, Opener = true };
        }

        static HudCategory HudCat(string name, string icon, string note, params HudTool[] tools)
        {
            var c = new HudCategory { Name = name, Icon = icon, Note = note };
            c.Tools.AddRange(tools);
            return c;
        }

        List<HudMode> BuildHudModes()
        {
            hudToolPrompts.Clear();

            // 观察: 4 live layer categories with auto-closing popout.
            var see = new HudMode { Name = "观察", Icon = "civ_observe", RailTitle = "图层", Legend = true, DirectRail = false };
            see.Categories.Add(HudCat("自然", "nat_terrain", "地貌与生态图层",
                HudLayer("自然地表", "nat_terrain", MapLayer.Natural), HudLayer("20 档高度", "nat_relief", MapLayer.Height),
                HudLayer("地貌", "iconTileLoessPlateau", MapLayer.Landforms), HudLayer("地表材料", "iconTileSteppePlateau", MapLayer.Materials),
                HudLayer("生态植被", "nat_plants", MapLayer.Vegetation), HudLayer("土地肥力", "civ_farm", MapLayer.Fertility),
                HudLayer("生态群系", "nat_forest", MapLayer.GlebaBiomes), HudLayer("土壤", "deposit_clear", MapLayer.GlebaSoilFertility),
                HudLayer("基岩", "iconTileAlpinePlateau", MapLayer.GlebaBedrock), HudLayer("板块", "nat_quake", MapLayer.GlebaTectonics)));
            see.Categories.Add(HudCat("气候", "nat_climate", "大气与环流图层",
                HudLayer("柯本气候", "nat_climate", MapLayer.Climate), HudLayer("本月温度", "nat_coldwave", MapLayer.Temperature),
                HudLayer("本月降水", "nat_rainfall", MapLayer.Rain), HudLayer("土壤湿度", "nat_rain", MapLayer.Wetness),
                HudLayer("相对湿度", "nat_rain", MapLayer.RelativeHumidity), HudLayer("风带气压", "nat_cloud", MapLayer.SurfaceWinds),
                HudLayer("洋流海温", "nat_current", MapLayer.OceanCurrents), HudLayer("高空急流", "nat_daynight", MapLayer.RossbyJets),
                HudLayer("雨影", "nat_rainfall", MapLayer.RainShadow), HudLayer("风暴潜势", "nat_cyclone", MapLayer.ExoStormPotential),
                new HudTool
                {
                    Name = "同月对比", Icon = "nat_daynight", StayOpen = true,
                    Hint = "温度、降水图层在今年、明年、后年同月之间切换。",
                    Run = () => { climateYearField.index = (climateYearField.index + 1) % 3; SetStatus("同月对比：" + climateYearField.value + "。"); },
                    IsOn = () => climateYearField.index > 0
                },
                HudWindow("气候统计", "iconHistory", () => ShowClimateStatistics(), "打开气候统计图集。")));
            see.Categories.Add(HudCat("水文", "nat_river", "江河与含水层",
                HudLayer("海水与淡水", "nat_lake", MapLayer.Water), HudLayer("河网", "nat_river", MapLayer.Rivers),
                HudLayer("河口悬沙", "deposit_clear", MapLayer.Sediment), HudLayer("含水层", "nat_lake", MapLayer.GlebaHydroGeo),
                HudAction("寻汇流", "nat_strait", () => FocusRiver(true), "依次定位两条以上河道相汇之处。", stayOpen: true),
                HudAction("寻河口", "nat_find_mouth", () => FocusRiver(false), "依次定位河流入海之处。", stayOpen: true)));
            see.Categories.Add(HudCat("人文", "iconNationLayer", "政区与农耕承载",
                HudLayer("文明版图", "iconNationLayer", MapLayer.Nations),
                HudLayer("作物承载", "civ_farm", MapLayer.GlebaCrops)));

            // 塑造: 1-click direct rail + bottom-center terrain brush bar.
            var shape = new HudMode { Name = "塑造", Icon = "nat_raise", RailTitle = "地形", Brush = true, DirectRail = true };
            shape.Categories.Add(HudCat("地形", "nat_terrain", "高度编辑与重算",
                HudEdit("定高笔刷", "nat_level", 3, "点击地块刷入目标高度（右键或 Esc 结束）"),
                HudAction("重算", "nat_rebuild", RebuildFromHud, "重新计算全球排水、月均气候与生物群系。")));

            // 文明: 1-click direct rail for founding, expansion, routes, and nation books.
            var civ = new HudMode { Name = "文明", Icon = "civ_mode", RailTitle = "列国", DirectRail = true };
            civ.Categories.Add(HudCat("列国", "civ_nations", "立国、扩张与总览",
                HudEdit("建立文明", "actor_create_kingdom", 1, "点击无主可居住陆地建立新文明（右键或 Esc 结束）"),
                HudEdit("扩张", "wild_develop", 2, "先选本国地块，再点相邻无主区域，耗粮 60（右键结束）"),
                HudAction("播种文明", "population_add", SeedNationsFromHud, "立即在世界上随机新增 4 个文明。"),
                HudEdit("路径", "road_connect", 7, "点击目标地块查看从当前国首都出发的陆路"),
                HudWindow("列国榜", "empire_list", OpenHudNations, "查看全部文明疆域、人口与存粮排行。"),
                HudWindow("国务", "TabEmpire", OpenHudState, "查看当前选中文明的国土与国事。")));

            // 营建: 1-click direct rail for farms, roads, bridges, and sea routes.
            var build = new HudMode { Name = "营建", Icon = "civ_build", RailTitle = "建设", DirectRail = true };
            build.Categories.Add(HudCat("建设", "civ_build", "农田与水陆交通",
                HudEdit("农田", "civ_farm", 4, "点击本国可居住地块开垦农田，耗粮 15（右键结束）"),
                HudEdit("道路", "road_paint", 5, "点击本国地块修筑道路降低通行成本，耗粮 15"),
                HudEdit("桥梁", "civ_bridge", 6, "点击本国河流或浅湖（≤100m）架桥，耗粮 40"),
                HudEdit("海上航线", "ship_1", 8, "依次点击出发海面与目的海面测算航线；右键重选起点", autoFocusSurface: false)));

            return new List<HudMode> { see, shape, civ, build };
        }

        void RebuildFromHud()
        {
            if (World == null || Busy) { SetStatus("世界数据正在生成或重建；请稍候。"); return; }
            solarClock.Flush(simulationStep);
            var copy = World.CloneForTerrainRebuild();
            Queue(c => { FlatGenerator.Rebuild(copy, c); return copy; }, "重新计算排水、月均气候与生物群系…");
        }

        void SeedNationsFromHud()
        {
            if (World == null || Busy || Simulation == null) { SetStatus("世界就绪后可播种文明。"); return; }
            int n = Simulation.SeedNations(4);
            if (activeNation < 0 && World.Nations.Count > 0) activeNation = 0;
            map.Flush();
            UpdateHUD();
            TickEmpireHud();
            SetStatus("新增 " + n + " 个文明（当前共 " + World.Nations.Count + " 国）。");
        }

        // ---------- modes, direct rail & layer popout ----------
        void SelectHudMode(int mode)
        {
            if (mode != hudMode && toolField.index != 0) toolField.index = 0;
            hudMode = mode;
            for (int i = 0; i < hudModeButtons.Count; i++) Hud.State(hudModeButtons[i], i == mode);

            var currentMode = hudModeList[mode];
            if (hudRailTitle != null) hudRailTitle.text = currentMode.RailTitle ?? currentMode.Name;

            hudRailList.Clear();
            hudRailButtons.Clear();
            hudDirectRailTools.Clear();

            if (currentMode.DirectRail)
            {
                foreach (var cat in currentMode.Categories)
                {
                    foreach (var tool in cat.Tools)
                    {
                        var t = tool;
                        var b = Hud.Button("ec-cat", t.Icon, t.Name, () => RunHudTool(t));
                        b.tooltip = string.IsNullOrEmpty(t.Hint) ? t.Name : t.Name + "：" + t.Hint;
                        if (t.Opener) b.AddToClassList("ec-opener");
                        hudDirectRailTools.Add((b, t));
                        hudRailList.Add(b);
                    }
                }
            }
            else
            {
                var cats = currentMode.Categories;
                for (int i = 0; i < cats.Count; i++)
                {
                    int index = i;
                    var b = Hud.Button("ec-cat", cats[i].Icon, cats[i].Name, () => ShowHudCategory(hudCategory == index ? -1 : index));
                    b.tooltip = cats[i].Name + (string.IsNullOrEmpty(cats[i].Note) ? "" : " · " + cats[i].Note);
                    hudRailButtons.Add(b);
                    hudRailList.Add(b);
                }
            }

            hudModesPanel?.SetPillContent(currentMode.Icon, currentMode.Name);
            hudRailPanel?.SetPillContent(currentMode.Icon, currentMode.Name + "栏");
            ShowHudCategory(-1);
            TickEmpireHud();
        }

        void ShowHudCategory(int category)
        {
            var currentMode = hudModeList[hudMode];
            if (currentMode.DirectRail) category = -1;

            hudCategory = category;
            var cats = currentMode.Categories;
            for (int i = 0; i < hudRailButtons.Count; i++) Hud.State(hudRailButtons[i], i == category, false, !cats[i].AnyPorted);
            hudToolButtons.Clear(); hudPopGrid.Clear();
            if (category < 0 || category >= cats.Count)
            {
                hudCategory = -1;
                hudPopPanel?.SetLogicalVisible(false);
                return;
            }
            var cat = cats[category];
            Hud.SetIcon(hudPopIcon, cat.Icon);
            hudPopTitle.text = cat.Name; hudPopNote.text = cat.Note ?? "";
            hudPopHint.text = "点击即生效；点地图空白处或按 Esc 自动收起。";
            foreach (var tool in cat.Tools)
            {
                var t = tool;
                var b = Hud.Button("ec-tool", t.Icon, t.Name, () => RunHudTool(t), "ec-x2");
                if (t.Opener) b.AddToClassList("ec-opener");
                b.RegisterCallback<PointerEnterEvent>(_ => hudPopHint.text = t.Name + "：" + (t.Hint ?? ""));
                hudToolButtons.Add((b, t)); hudPopGrid.Add(b);
            }
            hudPopPanel?.SetPillContent(cat.Icon, cat.Name);
            hudPopPanel?.SetLogicalVisible(true);
            if (hudPopPanel != null && hudPopPanel.IsCollapsed) hudPopPanel.SetCollapsed(false, save: false);
            hudPop.BringToFront();
            hudWindows?.BringToFront();
            RefreshHudToolStates();
        }

        void RunHudTool(HudTool tool)
        {
            if (tool.Run == null) return;
            try { tool.Run(); }
            catch (Exception e) { SetStatus(e.Message); Debug.LogException(e); }
            if (hudCategory >= 0 && !tool.StayOpen) ShowHudCategory(-1);
            RefreshHudToolStates();
            TickEmpireHud();
        }

        void RefreshHudToolStates()
        {
            foreach (var (el, tool) in hudToolButtons)
            {
                bool on = false;
                if (tool.IsOn != null) { try { on = tool.IsOn(); } catch { on = false; } }
                Hud.State(el, false, on, tool.Run == null);
            }
            foreach (var (el, tool) in hudDirectRailTools)
            {
                bool on = false;
                if (tool.IsOn != null) { try { on = tool.IsOn(); } catch { on = false; } }
                Hud.State(el, false, on, tool.Run == null);
            }
        }

        // ---------- top-right view navigation + settings ----------
        void BuildHudViews()
        {
            AddHudView("civ_globe", "星球", Home, () => navigation && !navigation.IsFlat && !navigation.IsAtlas, "返回三维星球视图 (Home)");
            AddHudView("civ_atlas", "总览", ToggleMode, () => navigation && navigation.IsAtlas, "展开或退出全球战略总览 (M)");
            AddHudView("civ_surface", "地表", Focus, () => navigation && navigation.IsFlat && !navigation.IsAtlas, "进入局部地表细看 (F)");
            hudDemoReturnBtn = Hud.Button("ec-vbtn", "iconHistory", "返回原局", () => { try { ExitIsolatedDemoWorld(); } catch (Exception e) { SetStatus(e.Message); Debug.LogException(e); } });
            hudDemoReturnBtn.AddToClassList("ec-demo-return");
            hudDemoReturnBtn.AddToClassList("ec-on");
            hudDemoReturnBtn.AddToClassList("ec-hidden");
            hudViewsBody.Add(hudDemoReturnBtn);
            AddHudView("TabColor", "设置", () => OpenHudWorld(0), () => hudWindow != null && hudWindowKey == "world", "打开世界设置中心");
        }

        void AddHudView(string icon, string text, Action click, Func<bool> on, string tooltip = null)
        {
            var b = Hud.Button("ec-vbtn", icon, text, click);
            if (!string.IsNullOrEmpty(tooltip)) b.tooltip = tooltip;
            hudViewsBody.Add(b);
            if (on != null) hudViewButtons.Add((b, on));
        }

        // ---------- contextual tile card ----------
        void BuildHudInfoButtons()
        {
            var focusBtn = Hud.Button("ec-btn", null, "聚焦", Focus);
            focusBtn.tooltip = "将镜头对准此地块 (F)";
            hudInfoButtons.Add(focusBtn);

            hudExpand = Hud.Button("ec-btn", null, "详情", () => { hudInfoExpanded = !hudInfoExpanded; hudInfoSource = null; TickEmpireHud(); });
            hudInfoButtons.Add(hudExpand);

            hudInfoNationBtn = Hud.Button("ec-btn", null, "国务", () =>
            {
                if (World != null && selected >= 0 && selected < World.Count && World.Owner[selected] >= 0)
                {
                    activeNation = World.Owner[selected];
                    OpenHudState();
                }
            });
            hudInfoNationBtn.AddToClassList("ec-opener");
            hudInfoNationBtn.AddToClassList("ec-hidden");
            hudInfoNationBtn.tooltip = "打开此地块所属文明的国务总览";
            hudInfoButtons.Add(hudInfoNationBtn);
        }

        void UpdateHudInfo()
        {
            bool hasSelection = World != null && selected >= 0 && selected < World.Count;
            hudInfoPanel?.SetLogicalVisible(hasSelection);
            if (!hasSelection)
            {
                hudInfoLastSelected = -1;
                hudInfoSource = null;
                return;
            }

            string source = info.text;
            if (source == hudInfoSource && selected == hudInfoLastSelected) return;
            hudInfoSource = source;
            hudInfoLastSelected = selected;

            int owner = World.Owner[selected];
            bool owned = owner >= 0 && owner < World.Nations.Count;
            hudInfoTitle.text = "地块 " + selected % World.Width + ", " + selected / World.Width;
            hudInfoNote.text = owned ? World.Nations[owner].Name : "无主";
            hudInfoPanel?.SetPillContent("civ_surface", owned ? World.Nations[owner].Name : "地块");
            hudInfoNationBtn?.EnableInClassList("ec-hidden", !owned);

            int totalRows = 0;
            int shown = 0;
            var lines = source.Split('\n');
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("TILE") || line.StartsWith("ZONE")) continue;
                totalRows++;
                if (!hudInfoExpanded && shown >= HudCompactRows) continue;
                int colon = line.IndexOf('：');
                if (colon > 0) SetHudInfoRow(shown++, line.Substring(0, colon), line.Substring(colon + 1));
                else if (line.EndsWith("以下") || line.Contains(" m · ")) SetHudInfoRow(shown++, "海拔", line);
                else SetHudInfoRow(shown++, "", line);
            }

            hudExpand.EnableInClassList("ec-hidden", totalRows <= HudCompactRows);
            Hud.SetText(hudExpand, hudInfoExpanded ? "收起" : "详情 (" + totalRows + ")");
            for (int i = 0; i < hudInfoPool.Count; i++) hudInfoPool[i].row.EnableInClassList("ec-hidden", i >= shown);
        }

        void SetHudInfoRow(int index, string key, string value)
        {
            while (hudInfoPool.Count <= index)
            {
                var row = Hud.Row("", "", hudInfoPool.Count % 2 == 1);
                hudInfoPool.Add((row, (Label)row[0], (Label)row[1]));
                hudInfoRows.Add(row);
            }
            var r = hudInfoPool[index];
            if (r.key.text != key) r.key.text = key;
            if (r.val.text != value) r.val.text = value;
        }

        // ---------- compact chronicle ----------
        void UpdateHudFeed()
        {
            if (status == hudLastStatus) return;
            hudLastStatus = status;
            if (string.IsNullOrWhiteSpace(status)) return;
            string when = World == null ? "开辟" : "第" + ((int)(CalendarDay / 360) + 1) + "年" + (World.Month + 1) + "月";
            hudFeed.Insert(0, (when, status.Trim()));
            if (hudFeed.Count > HudFeedRows) hudFeed.RemoveAt(hudFeed.Count - 1);
            while (hudFeedPool.Count < HudFeedRows)
            {
                var row = Hud.Row("", "", hudFeedPool.Count % 2 == 1);
                hudFeedPool.Add((row, (Label)row[0], (Label)row[1]));
                hudFeedRows.Add(row);
            }
            for (int i = 0; i < hudFeedPool.Count; i++)
            {
                bool has = i < hudFeed.Count;
                hudFeedPool[i].row.EnableInClassList("ec-hidden", !has);
                if (!has) continue;
                hudFeedPool[i].key.text = hudFeed[i].when;
                hudFeedPool[i].val.text = hudFeed[i].text;
            }
        }

        // ---------- bottom-center contextual brush & active-tool bar ----------
        void BuildHudBrush()
        {
            hudTerrainBrushGroup = new VisualElement();
            hudTerrainBrushGroup.AddToClassList("ec-bar-body");
            hudTerrainBrushGroup.Add(Hud.Text("范围", "ec-brush-lab"));
            string[] names = { "1 格", "1 区", "3×3", "9×9" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var chip = Hud.Button("ec-chip", null, names[i], () => brushField.index = index);
                hudBrushChips.Add((chip, i));
                hudTerrainBrushGroup.Add(chip);
            }
            hudTerrainBrushGroup.Add(Hud.Text("高度", "ec-brush-lab"));
            hudTerrainBrushGroup.Add(Hud.Button("ec-chip", null, "−", () => heightField.index = Math.Max(0, heightField.index - 1)));
            hudHeightLabel = Hud.Text("", "ec-label");
            hudHeightLabel.style.minWidth = 108;
            hudTerrainBrushGroup.Add(hudHeightLabel);
            hudTerrainBrushGroup.Add(Hud.Button("ec-chip", null, "+", () => heightField.index = Math.Min(heightField.choices.Count - 1, heightField.index + 1)));
            hudTerrainBrushDoneBtn = Hud.Button("ec-chip", null, "完成", CancelActiveHudTool);
            hudTerrainBrushDoneBtn.tooltip = "退出定高笔刷 (右键 / Esc)";
            hudTerrainBrushGroup.Add(hudTerrainBrushDoneBtn);
            hudBrushBody.Add(hudTerrainBrushGroup);

            hudActiveToolGroup = new VisualElement();
            hudActiveToolGroup.AddToClassList("ec-bar-body");
            hudActiveToolGroup.AddToClassList("ec-hidden");
            hudActiveToolName = Hud.Text("", "ec-ch-title");
            hudActiveToolHint = Hud.Text("", "ec-brush-hint");
            var done = Hud.Button("ec-chip", null, "完成", CancelActiveHudTool);
            done.tooltip = "结束当前地图操作 (右键 / Esc)";
            hudActiveToolGroup.Add(hudActiveToolName);
            hudActiveToolGroup.Add(hudActiveToolHint);
            hudActiveToolGroup.Add(done);
            hudBrushBody.Add(hudActiveToolGroup);
        }

        void UpdateHudBrushBar()
        {
            int activeTool = toolField != null ? toolField.index : 0;
            bool showTerrainBrush = hudModeList[hudMode].Brush || activeTool == 3;
            bool showToolBanner = !showTerrainBrush && activeTool != 0;
            hudBrushPanel?.SetLogicalVisible(showTerrainBrush || showToolBanner);
            if (!showTerrainBrush && !showToolBanner) return;

            hudTerrainBrushGroup.EnableInClassList("ec-hidden", !showTerrainBrush);
            hudActiveToolGroup.EnableInClassList("ec-hidden", !showToolBanner);
            if (showTerrainBrush)
            {
                foreach (var (el, brush) in hudBrushChips) Hud.State(el, false, brushField.index == brush);
                if (hudHeightLabel != null) hudHeightLabel.text = heightField.value;
                hudTerrainBrushDoneBtn?.EnableInClassList("ec-hidden", activeTool != 3);
                hudBrushPanel?.SetPillContent("nat_level", "笔刷");
            }
            else if (hudToolPrompts.TryGetValue(activeTool, out var info))
            {
                hudActiveToolName.text = info.name;
                hudActiveToolHint.text = info.hint;
                hudBrushPanel?.SetPillContent("civ_build", info.name);
            }
        }

        // ---------- periodic refresh (4 Hz, UI scheduler) ----------
        void TickEmpireHud()
        {
            if (hud == null) return;
            if (World != null)
            {
                double day = CalendarDay;
                hudDate.text = "第 " + ((int)(day / 360) + 1) + " 年 · " + (World.Month + 1) + " 月";
                float phase = (float)FlatSolarClock.Phase(day);
                hudDateSub.text = ((int)(phase * 24)).ToString("00") + ":" + ((int)(phase * 1440) % 60).ToString("00") +
                    " · 人口 " + Simulation.TotalPopulation().ToString("N0") + (navigation ? " · " + navigation.ModeName : "");
                hudEraPanel?.SetPillContent("nat_daynight", hudDate.text);
            }
            else
            {
                hudDate.text = "开辟之初";
                hudDateSub.text = status;
                hudEraPanel?.SetPillContent("nat_daynight", "开辟之初");
            }
            Hud.SetText(hudPause, paused ? "继续" : "暂停");
            Hud.State(hudPause, paused);
            Hud.SetText(hudSpeed, speed + "×");
            if (hudDemoReturnBtn != null) hudDemoReturnBtn.EnableInClassList("ec-hidden", !IsDemoWorldActive);
            foreach (var (el, on) in hudViewButtons) Hud.State(el, false, on());
            RefreshHudToolStates();
            UpdateHudBrushBar();

            bool legendOn = hudModeList[hudMode].Legend && !string.IsNullOrEmpty(legend.text);
            hudLegendPanel?.SetLogicalVisible(legendOn);
            if (legendOn)
            {
                string layerName = layerField.value;
                hudLegendTitle.text = layerName;
                if (hudLegendText.text != legend.text) hudLegendText.text = legend.text;
                hudLegendResetBtn?.EnableInClassList("ec-hidden", layerField.index == (int)MapLayer.Natural);
                hudLegendPanel?.SetPillContent("nat_terrain", string.IsNullOrEmpty(layerName) ? "图例" : layerName);
            }
            hudMinimapPanel?.SetLogicalVisible(World != null && minimap != null && minimap.Bound);

            UpdateHudInfo();
            UpdateHudFeed();
            hudWindowAge += .25f;
            if (hudWindow != null && !hudWindow.IsCollapsed && hudWindowAge >= 1 && hudWindow.CurrentIsLive)
            {
                hudWindowAge = 0;
                hudWindow.Refresh();
            }
        }
    }
}
