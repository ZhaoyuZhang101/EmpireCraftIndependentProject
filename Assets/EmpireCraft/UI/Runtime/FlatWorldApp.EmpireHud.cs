using System;
using System.Collections.Generic;
using EmpireCraft.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.Flat
{
    // EmpireCraft civilization HUD over the ORBIS flat-world app: four modes
    // (观察 / 塑造 / 文明 / 营建), a category rail per mode with a popout tool grid,
    // the tile card, the world chronicle and 卷册 wide windows.
    // Every tool drives the existing ORBIS controls (layer / tool / brush dropdowns and
    // toggles), so behaviour stays in FlatWorldApp; tools whose systems are not ported
    // yet are shown greyed. The classic ORBIS sidebar stays reachable as a drawer.
    public sealed partial class FlatWorldApp
    {
        // The lacquer theme is the house style (docs/design/ui-style.md) and comes first; the
        // key changed with it so an older saved index does not pick another theme.
        const string HudThemePref = "EmpireCraft.UiTheme2";
        static readonly string[] HudThemes = { "lacquer", "star", "atlas", "slate" };
        static readonly string[] HudThemeNames = { "漆器朱", "星图青铜", "舆图羊皮", "石青白玉" };
        const int HudFeedRows = 5, HudCompactRows = 12;

        VisualElement hud, hudModes, hudViews, hudRail, hudPop, hudPopGrid, hudPopIcon, hudInfoButtons, hudFeedRows,
            hudBrush, hudWindows, hudLegend, hudClock, hudInfoIcon;
        ScrollView hudInfoRows;
        Label hudDate, hudDateSub, hudPopTitle, hudPopNote, hudPopHint, hudInfoTitle, hudInfoNote, hudLegendTitle, hudLegendText;
        VisualElement hudPause, hudSpeed, hudExpand;
        readonly List<VisualElement> hudModeButtons = new List<VisualElement>(), hudRailButtons = new List<VisualElement>();
        readonly List<(VisualElement el, HudTool tool)> hudToolButtons = new List<(VisualElement, HudTool)>();
        readonly List<(VisualElement el, Func<bool> on)> hudViewButtons = new List<(VisualElement, Func<bool>)>();
        readonly List<(VisualElement el, int brush)> hudBrushChips = new List<(VisualElement, int)>();
        readonly List<(VisualElement row, Label key, Label val)> hudInfoPool = new List<(VisualElement, Label, Label)>();
        readonly List<(VisualElement row, Label key, Label val)> hudFeedPool = new List<(VisualElement, Label, Label)>();
        readonly List<(string when, string text)> hudFeed = new List<(string, string)>();
        Label hudHeightLabel;
        List<HudMode> hudModeList;
        int hudMode, hudCategory = -1, hudTheme;
        bool hudInfoExpanded;
        string hudLastStatus, hudInfoSource;
        IHudWindow hudWindow;
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

            hudModes = hud.Q("ecModes"); hudViews = hud.Q("ecViews"); hudRail = hud.Q("ecRail");
            hudPop = hud.Q("ecPop"); hudPopGrid = hud.Q("ecPopGrid"); hudPopIcon = hud.Q("ecPopIcon");
            hudPopTitle = hud.Q<Label>("ecPopTitle"); hudPopNote = hud.Q<Label>("ecPopNote"); hudPopHint = hud.Q<Label>("ecPopHint");
            hudInfoRows = hud.Q<ScrollView>("ecInfoRows"); hudInfoButtons = hud.Q("ecInfoButtons"); hudInfoIcon = hud.Q("ecInfoIcon");
            hudInfoTitle = hud.Q<Label>("ecInfoTitle"); hudInfoNote = hud.Q<Label>("ecInfoNote");
            hudFeedRows = hud.Q("ecFeedRows"); hudBrush = hud.Q("ecBrush"); hudWindows = hud.Q("ecWindows");
            hudLegend = hud.Q("ecLegend"); hudLegendTitle = hud.Q<Label>("ecLegendTitle"); hudLegendText = hud.Q<Label>("ecLegendText");
            hudDate = hud.Q<Label>("ecDate"); hudDateSub = hud.Q<Label>("ecDateSub"); hudClock = hud.Q("ecClock");
            Hud.SetIcon(hud.Q("ecFeedIcon"), "iconHistory");
            Hud.SetIcon(hudInfoIcon, "civ_surface");

            hudPause = Hud.Button("ec-ib", null, "暂停", () => { paused = !paused; UpdateHUD(); TickEmpireHud(); });
            hudSpeed = Hud.Button("ec-ib", null, "1×", () => { speed = speed == 1 ? 4 : speed == 4 ? 12 : speed == 12 ? 20 : 1; UpdateHUD(); TickEmpireHud(); });
            hudClock.Add(hudPause); hudClock.Add(hudSpeed);

            BuildHudViews();
            BuildHudInfoButtons();
            BuildHudBrush();
            hudModeList = BuildHudModes();
            for (int i = 0; i < hudModeList.Count; i++)
            {
                int index = i;
                var b = Hud.Button("ec-mode", hudModeList[i].Icon, hudModeList[i].Name, () => SelectHudMode(index));
                hudModeButtons.Add(b); hudModes.Add(b);
            }
            hudWindows.RegisterCallback<PointerDownEvent>(e => { if (e.target == hudWindows) CloseHudWindow(); });
            root.RegisterCallback<KeyDownEvent>(HudKeys, TrickleDown.TrickleDown);
            SelectHudMode(0);
            hud.schedule.Execute(TickEmpireHud).Every(250);
        }

        void ApplyHudTheme(int theme)
        {
            hudTheme = theme;
            for (int i = 0; i < HudThemes.Length; i++) root.EnableInClassList("theme-" + HudThemes[i], i == theme);
            // The lacquer theme sets its text in the pixel font (inherited by the whole panel).
            root.style.unityFontDefinition = HudThemes[theme] == "lacquer" && PixelFont.Definition.HasValue
                ? new StyleFontDefinition(PixelFont.Definition.Value) : new StyleFontDefinition(StyleKeyword.Null);
            PlayerPrefs.SetInt(HudThemePref, theme);
        }

        void HudKeys(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Escape) return;
            if (hudWindow != null) CloseHudWindow();
            else if (corePower != CorePower.None) ChooseCorePower(CorePower.None);
            else if (hudCategory >= 0) ShowHudCategory(-1);
            else if (toolField.index != 0) toolField.index = 0;
            else return;
            e.StopPropagation();
        }

        // ---------- catalog ----------
        HudTool HudLayer(string name, string icon, MapLayer layer)
        {
            return new HudTool { Name = name, Icon = icon, Hint = "切换为「" + name + "」图层。",
                Run = () => ChooseLayer(layer), IsOn = () => layerField.index == (int)layer };
        }

        HudTool HudEdit(string name, string icon, int tool, string hint)
        {
            return new HudTool { Name = name, Icon = icon, Hint = hint,
                Run = () => toolField.index = toolField.index == tool ? 0 : tool, IsOn = () => toolField.index == tool };
        }

        static HudTool HudToggle(string name, string icon, Toggle toggle, string hint)
        {
            return new HudTool { Name = name, Icon = icon, Hint = hint, Run = () => toggle.value = !toggle.value, IsOn = () => toggle.value };
        }

        static HudTool HudAction(string name, string icon, Action run, string hint)
        {
            return new HudTool { Name = name, Icon = icon, Hint = hint, Run = run };
        }

        static HudTool HudWindow(string name, string icon, Action open, string hint)
        {
            return new HudTool { Name = name, Icon = icon, Hint = hint, Run = open, Opener = true };
        }

        static HudTool HudLater(string name, string icon)
        {
            return new HudTool { Name = name, Icon = icon };
        }

        static HudCategory HudCat(string name, string icon, string note, params HudTool[] tools)
        {
            var c = new HudCategory { Name = name, Icon = icon, Note = note };
            c.Tools.AddRange(tools);
            return c;
        }

        List<HudMode> BuildHudModes()
        {
            var see = new HudMode { Name = "观察", Icon = "civ_observe", Legend = true };
            see.Categories.Add(HudCat("自然", "nat_terrain", "地表图层",
                HudLayer("自然地表", "nat_terrain", MapLayer.Natural), HudLayer("20 档高度", "nat_relief", MapLayer.Height),
                HudLayer("地貌", "iconTileLoessPlateau", MapLayer.Landforms), HudLayer("地表材料", "iconTileSteppePlateau", MapLayer.Materials),
                HudLayer("生态植被", "nat_plants", MapLayer.Vegetation), HudLayer("土地肥力", "civ_farm", MapLayer.Fertility),
                HudLayer("生态群系", "nat_forest", MapLayer.GlebaBiomes), HudLayer("土壤", "deposit_clear", MapLayer.GlebaSoilFertility),
                HudLayer("基岩", "iconTileAlpinePlateau", MapLayer.GlebaBedrock), HudLayer("板块", "nat_quake", MapLayer.GlebaTectonics)));
            see.Categories.Add(HudCat("气候", "nat_climate", "气候图层",
                HudLayer("柯本气候", "nat_climate", MapLayer.Climate), HudLayer("本月温度", "nat_coldwave", MapLayer.Temperature),
                HudLayer("本月降水", "nat_rainfall", MapLayer.Rain), HudLayer("土壤湿度", "nat_rain", MapLayer.Wetness),
                HudToggle("云层", "nat_cloud", cloudToggle, "显示或隐藏云层与云影。"),
                new HudTool { Name = "同月对比", Icon = "nat_daynight", Hint = "温度、降水图层在今年、明年、后年同月之间切换。",
                    Run = () => { climateYearField.index = (climateYearField.index + 1) % 3; SetStatus("同月对比：" + climateYearField.value + "。"); },
                    IsOn = () => climateYearField.index > 0 },
                HudWindow("气候统计", "iconHistory", () => ShowClimateStatistics(), "打开气候统计图集。"),
                HudLayer("风带气压", "nat_cloud", MapLayer.SurfaceWinds), HudLayer("洋流海温", "nat_current", MapLayer.OceanCurrents),
                HudLayer("高空急流", "nat_daynight", MapLayer.RossbyJets), HudLayer("雨影", "nat_rainfall", MapLayer.RainShadow),
                HudLayer("相对湿度", "nat_rain", MapLayer.RelativeHumidity), HudLayer("风暴潜势", "nat_cyclone", MapLayer.ExoStormPotential)));
            see.Categories.Add(HudCat("水文", "nat_river", "江河湖海",
                HudLayer("海水与淡水", "nat_lake", MapLayer.Water), HudLayer("河网", "nat_river", MapLayer.Rivers),
                HudLayer("河口悬沙", "deposit_clear", MapLayer.Sediment), HudLayer("含水层", "nat_lake", MapLayer.GlebaHydroGeo),
                HudAction("寻汇流", "nat_strait", () => FocusRiver(true), "依次定位两条以上河道相汇之处。"),
                HudAction("寻河口", "nat_find_mouth", () => FocusRiver(false), "依次定位河流入海之处。")));
            see.Categories.Add(HudCat("舆图", "nat_grid", "图面辅助",
                HudToggle("网格", "nat_grid", gridToggle, "显示地块网格（G）。"),
                HudToggle("等高线", "nat_relief", contourToggle, "显示等高线（C）。"),
                HudToggle("云层", "nat_cloud", cloudToggle, "显示或隐藏云层与云影。"),
                HudAction("聚焦地块", "civ_surface", Focus, "镜头对准选中的地块（F）。")));
            see.Categories.Add(HudCat("政区", "iconNationLayer", "疆域与法理",
                HudLayer("文明版图", "iconNationLayer", MapLayer.Nations),
                HudLater("法理", "iconTitleLayer"), HudLater("天下", "tianxia_layer"), HudLater("行政区", "iconToolProvinceCreate"), HudLater("帝国", "empire_layer")));
            see.Categories.Add(HudCat("治理", "governance_layer", "政令与教化",
                HudLater("政令", "governance_layer"), HudLater("教化", "edification_layer"), HudLater("城市", "city_view")));
            see.Categories.Add(HudCat("民生", "wealth_layer", "财富与市场",
                HudLayer("作物承载", "civ_farm", MapLayer.GlebaCrops),
                HudLater("财富", "wealth_layer"), HudLater("市场", "market_layer"), HudLater("人口", "iconPopulation50")));
            see.Categories.Add(HudCat("信仰", "TabPapacy", "宗教分布",
                HudLater("宗教", "TabPapacy"), HudLater("圣地", "iconToolHolyCity")));
            see.Categories.Add(HudCat("军情", "TabWarfare", "战事与军团",
                HudWindow("战争", "TabWarfare", OpenHudWar, "列出正在进行的战争。"), HudLater("军团", "corps_command")));

            var shape = new HudMode { Name = "塑造", Icon = "nat_raise", Brush = true };
            shape.Categories.Add(HudCat("地形", "nat_terrain", "高度与地貌",
                HudEdit("定高笔刷", "nat_level", 3, "按笔刷大小把地块设为预设高度，随后重算排水与气候。"),
                HudLater("抬升", "nat_raise"), HudLater("压低", "nat_lower"), HudLater("平滑", "nat_smooth"), HudLater("高原", "iconTileAlpinePlateau"),
                HudAction("重算", "nat_rebuild", RebuildFromHud, "重新计算排水、月均气候与生物群系。")));
            shape.Categories.Add(HudCat("水文", "nat_river", "开河造湖",
                HudLater("开河", "nat_river"), HudLater("造湖", "nat_lake"), HudLater("海峡", "nat_strait")));
            shape.Categories.Add(HudCat("气候", "nat_climate", "天时",
                HudLater("降雨", "nat_rain"), HudLater("寒潮", "nat_coldwave"), HudLater("干旱", "nat_drought"), HudLater("气旋", "nat_cyclone")));
            shape.Categories.Add(HudCat("生命", "nat_plants", "草木鸟兽",
                HudLater("植树", "nat_forest"), HudLater("草木", "nat_plants"), HudLater("走兽", "nat_beast"), HudLater("鱼群", "nat_fish")));
            shape.Categories.Add(HudCat("天象", "nat_meteor", "天灾地变",
                HudLater("陨石", "nat_meteor"), HudLater("地震", "nat_quake"), HudLater("火山", "nat_volcano")));

            var civ = new HudMode { Name = "文明", Icon = "civ_mode" };
            civ.Categories.Add(HudCat("列国", "civ_nations", "立国与扩张",
                CoreEnabled ? HudLater("建立文明", "actor_create_kingdom") : HudEdit("建立文明", "actor_create_kingdom", 1, "点击可居住的无主区域建立文明。"),
                CoreEnabled ? HudLater("扩张", "wild_develop") : HudEdit("扩张", "wild_develop", 2, "先点本国地块，再点相邻的无主区域；耗粮 60。"),
                CoreEnabled ? HudLater("播种文明", "population_add") : HudAction("播种文明", "population_add", SeedNationsFromHud, "在世界上随机新增 4 个文明。"),
                HudWindow("列国榜", "empire_list", OpenHudNations, CoreEnabled ? "列国的民族、政体、城池与人口，各民族的时代与制度。" : "各文明的疆域、人口与存粮。"),
                CoreEnabled ? HudLater("路径", "road_connect") : HudEdit("路径", "road_connect", 7, "显示从首都到所点地块的陆路。"),
                CoreEnabled ? HudCorePower("立君", "ChineseCrown", CorePower.Crown, "点一国，在名单里挑人立为君主。") : HudLater("立君", "civ_crown")));
            civ.Categories.Add(HudCat("帝国", "civ_empire", "国务与从属",
                HudWindow("国务", "TabEmpire", OpenHudState, "选中之国的君主、国势、文明与城池。"),
                HudWindow("史书", "iconHistory", OpenCoreAnnals, "天下大事与编年。"),
                CoreEnabled ? HudCorePower("称帝", "create_empire", CorePower.Empire, "点一国，它即称帝；已在别国帝国里的先脱离。") : HudLater("称帝", "create_empire"),
                HudLater("从属", "TabDependencies")));
            civ.Categories.Add(HudCat("法理", "civ_claims", "法理与宣称",
                HudLater("法理", "iconToolTitleCreate"), HudLater("核心", "iconToolCoreCreate"), HudLater("宣称", "TabClaimScale")));
            civ.Categories.Add(HudCat("朝堂", "civ_court", "官署与制度",
                HudLater("官署", "TabBureau"), HudLater("议会", "TabDiet"), HudLater("量刑", "TabSentencing"),
                HudLater("宪制", "TabConstitution"), HudLater("制度", "TabInstitutions")));
            civ.Categories.Add(HudCat("人物", "famous_talents", "宗族与名人",
                HudWindow("宗族", "specificClanIcon", OpenHudClans, "各家门第与族人。"),
                CoreEnabled ? HudWindow("名人", "famous_talents", () => OpenHudClans(1), "按名望排列的人物。") : HudLater("名人", "famous_talents")));
            civ.Categories.Add(HudCat("宗教", "TabPapacy", "教会与教派", HudLater("宗教", "TabPapacy"), HudLater("圣地", "iconToolHolyCity")));
            civ.Categories.Add(HudCat("外交", "diplomacy", "盟约与往来",
                CoreEnabled ? HudCorePower("外交", "diplomacy", CorePower.Diplomacy, "先点本国，再点另一国：宣战、议和、停战。") : HudLater("外交", "diplomacy"),
                HudLater("结盟", "plot_kingdom_start_join_taken_alliance")));
            civ.Categories.Add(HudCat("征伐", "TabWarfare", "战争与军团",
                HudWindow("战争", "TabWarfare", OpenHudWar, "列出正在进行的战争。"),
                HudLater("军团", "corps_command"),
                CoreEnabled ? HudCorePower("停战", "plot_force_stop_war", CorePower.Truce, "先点交战的一方，再点它的敌国；同一国点两下，它所有的仗都停。") : HudLater("停战", "plot_force_stop_war")));

            var build = new HudMode { Name = "营建", Icon = "civ_build" };
            build.Categories.Add(HudCat("农桑", "civ_farm", "田亩",
                HudEdit("农田", "civ_farm", 4, "本国可居住地块，耗粮 15，提高月产粮。")));
            build.Categories.Add(HudCat("道路", "road_paint", "道路桥梁",
                HudEdit("道路", "road_paint", 5, "本国地块，耗粮 15，降低通行成本。"),
                HudEdit("桥梁", "civ_bridge", 6, "本国河流或浅湖（水深不超过 100 米），耗粮 40。"),
                HudEdit("海上航线", "ship_1", 8, "先点出发海面，再点目的海面，查看航线；右键重选。"),
                HudLater("官道", "road_connect")));
            build.Categories.Add(HudCat("城池", "place_city", "城与关隘",
                HudLater("城", "place_city"), HudLater("关城", "place_fort"), HudLater("宫殿", "build_palace"), HudLater("港口", "port")));
            build.Categories.Add(HudCat("村镇", "place_village", "乡野聚落", HudLater("村镇", "place_village")));
            build.Categories.Add(HudCat("厂区", "factory_medium", "工坊工厂",
                HudLater("小厂", "factory_small"), HudLater("中厂", "factory_medium"), HudLater("大厂", "factory_large")));
            build.Categories.Add(HudCat("无人区", "wild_title", "荒野开发",
                HudLater("归属", "wild_title"), HudLater("开发", "wild_develop"), HudLater("名册", "wild_window")));
            return new List<HudMode> { see, shape, civ, build };
        }

        void RebuildFromHud()
        {
            if (!Ready()) return;
            solarClock.Flush(simulationStep);
            var copy = World.CloneForTerrainRebuild();
            Queue(c => { FlatGenerator.Rebuild(copy, c); return copy; }, "重新计算排水、月均气候与生物群系…");
        }

        void SeedNationsFromHud()
        {
            if (!Ready()) return;
            int n = Simulation.SeedNations(4);
            map.Flush();
            SetStatus("新增 " + n + " 个文明。领地按 4×4 区域扩展。");
        }

        // ---------- modes, rail, popout ----------
        void SelectHudMode(int mode)
        {
            if (mode != hudMode && toolField.index != 0) toolField.index = 0;
            hudMode = mode;
            for (int i = 0; i < hudModeButtons.Count; i++) Hud.State(hudModeButtons[i], i == mode);
            hudRail.Clear(); hudRailButtons.Clear();
            var cats = hudModeList[mode].Categories;
            for (int i = 0; i < cats.Count; i++)
            {
                int index = i;
                var b = Hud.Button("ec-cat", cats[i].Icon, cats[i].Name, () => ShowHudCategory(hudCategory == index ? -1 : index));
                hudRailButtons.Add(b); hudRail.Add(b);
            }
            hudBrush.EnableInClassList("ec-hidden", !hudModeList[mode].Brush);
            ShowHudCategory(-1);
            TickEmpireHud();
        }

        void ShowHudCategory(int category)
        {
            hudCategory = category;
            var cats = hudModeList[hudMode].Categories;
            for (int i = 0; i < hudRailButtons.Count; i++) Hud.State(hudRailButtons[i], i == category, false, !cats[i].AnyPorted);
            hudPop.EnableInClassList("ec-hidden", category < 0);
            hudToolButtons.Clear(); hudPopGrid.Clear();
            if (category < 0) return;
            var cat = cats[category];
            Hud.SetIcon(hudPopIcon, cat.Icon);
            hudPopTitle.text = cat.Name; hudPopNote.text = cat.Note ?? "";
            hudPopHint.text = cat.AnyPorted ? "悬停查看说明；Esc 收起。" : "这一类尚未移植，图标先占位。";
            foreach (var tool in cat.Tools)
            {
                var t = tool;
                var b = Hud.Button("ec-tool", t.Icon, t.Name, () => RunHudTool(t), "ec-x2");
                if (t.Opener) b.AddToClassList("ec-opener");
                b.RegisterCallback<PointerEnterEvent>(_ => hudPopHint.text = t.Name + "：" + (t.Run == null ? "尚未移植。" : t.Hint ?? ""));
                hudToolButtons.Add((b, t)); hudPopGrid.Add(b);
            }
            RefreshHudToolStates();
        }

        void RunHudTool(HudTool tool)
        {
            if (tool.Run == null) { SetStatus(tool.Name + "：尚未移植，对应系统接入后启用。"); return; }
            try { tool.Run(); }
            catch (Exception e) { SetStatus(e.Message); Debug.LogException(e); }
            if (tool.Opener) ShowHudCategory(-1);
            RefreshHudToolStates();
        }

        void RefreshHudToolStates()
        {
            foreach (var (el, tool) in hudToolButtons)
            {
                bool on = false;
                if (tool.IsOn != null) { try { on = tool.IsOn(); } catch { on = false; } }
                Hud.State(el, false, on, tool.Run == null);
            }
        }

        // ---------- top-right view buttons ----------
        void BuildHudViews()
        {
            AddHudView("civ_globe", "星球", Home, () => navigation && !navigation.IsFlat && !navigation.IsAtlas);
            AddHudView("civ_atlas", "总览", ToggleMode, () => navigation && navigation.IsAtlas);
            AddHudView("civ_surface", "地表", Focus, () => navigation && navigation.IsFlat && !navigation.IsAtlas);
            AddHudView(null, "截图", () => { if (World != null && !Busy) StartCoroutine(Capture()); }, null);
            AddHudView("TabColor", "设置", () => OpenHudWorld(0), () => hudWindow != null && hudWindowKey == "world");
        }

        void AddHudView(string icon, string text, Action click, Func<bool> on)
        {
            var b = Hud.Button("ec-vbtn", icon, text, click);
            hudViews.Add(b);
            if (on != null) hudViewButtons.Add((b, on));
        }

        // ---------- tile card ----------
        void BuildHudInfoButtons()
        {
            hudInfoButtons.Add(Hud.Button("ec-btn", null, "聚焦", Focus));
            hudExpand = Hud.Button("ec-btn", null, "展开", () => { hudInfoExpanded = !hudInfoExpanded; hudInfoSource = null; TickEmpireHud(); });
            hudInfoButtons.Add(hudExpand);
            hudInfoButtons.Add(Hud.Button("ec-btn", null, "气候", () => ShowClimateStatistics()));
            hudInfoButtons.Add(Hud.Button("ec-btn", null, "清除", () => { selected = -1; if (map) map.Select(-1); toolField.index = 0; UpdateHUD(); TickEmpireHud(); }));
        }

        void UpdateHudInfo()
        {
            string source = info.text;
            if (source == hudInfoSource) return;
            hudInfoSource = source;
            Hud.SetText(hudExpand, hudInfoExpanded ? "收起" : "展开");
            int count = 0;
            if (selected < 0 || World == null || selected >= World.Count)
            {
                hudInfoTitle.text = "地块"; hudInfoNote.text = "";
                SetHudInfoRow(count++, "提示", "点击地图查看地块。");
                SetHudInfoRow(count++, "区域", "每区 4×4 = 16 个地块。");
            }
            else
            {
                hudInfoTitle.text = "地块 " + selected % World.Width + ", " + selected / World.Width;
                int owner = World.Owner[selected];
                string coreNote = CoreTileNote(selected);
                hudInfoNote.text = coreNote ?? (owner >= 0 && owner < World.Nations.Count ? World.Nations[owner].Name : "无主");
                var coreCity = CoreHere()?.CityAt(selected);
                var lines = source.Split('\n');
                foreach (var raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("TILE") || line.StartsWith("ZONE")) continue;
                    if (!hudInfoExpanded && count >= HudCompactRows) break;
                    int colon = line.IndexOf('：');
                    string key = colon > 0 ? line.Substring(0, colon) : null;
                    // With the core on, ownership and people are the core's: the city's realm and its count.
                    if (coreNote != null && key == "归属") { SetHudInfoRow(count++, "归属", coreNote); continue; }
                    if (coreNote != null && key == "人口") { SetHudInfoRow(count++, "城中人口", coreCity != null && coreCity.Alive ? coreCity.PopulationCount().ToString("N0") : "—"); continue; }
                    if (colon > 0) SetHudInfoRow(count++, key, line.Substring(colon + 1));
                    else if (line.EndsWith("以下") || line.Contains(" m · ")) SetHudInfoRow(count++, "海拔", line);
                    else SetHudInfoRow(count++, "", line);
                }
            }
            for (int i = 0; i < hudInfoPool.Count; i++) hudInfoPool[i].row.EnableInClassList("ec-hidden", i >= count);
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

        // ---------- chronicle ----------
        void UpdateHudFeed()
        {
            bool changed = FeedCoreNews();
            if (status != hudLastStatus)
            {
                hudLastStatus = status;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    string when = World == null ? "开辟" : "第" + ((int)(CalendarDay / 360) + 1) + "年" + (World.Month + 1) + "月";
                    hudFeed.Insert(0, (when, status.Trim()));
                    if (hudFeed.Count > HudFeedRows) hudFeed.RemoveAt(hudFeed.Count - 1);
                    changed = true;
                }
            }
            if (!changed) return;
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

        // ---------- brush bar (塑造) ----------
        void BuildHudBrush()
        {
            hudBrush.Add(Hud.Text("笔刷", "ec-brush-lab"));
            string[] names = { "1 格", "1 区", "3×3", "9×9" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var chip = Hud.Button("ec-chip", null, names[i], () => brushField.index = index);
                hudBrushChips.Add((chip, i)); hudBrush.Add(chip);
            }
            hudBrush.Add(Hud.Text("高度", "ec-brush-lab"));
            hudBrush.Add(Hud.Button("ec-chip", null, "−", () => heightField.index = Math.Max(0, heightField.index - 1)));
            hudHeightLabel = Hud.Text("", "ec-label");
            hudHeightLabel.style.minWidth = 118;
            hudBrush.Add(hudHeightLabel);
            hudBrush.Add(Hud.Button("ec-chip", null, "+", () => heightField.index = Math.Min(heightField.choices.Count - 1, heightField.index + 1)));
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
                    " · 人口 " + (CoreHere() != null ? CoreTotalPopulation(CoreHere()) : (long)Simulation.TotalPopulation()).ToString("N0") + (navigation ? " · " + navigation.ModeName : "");
            }
            else { hudDate.text = "开辟之初"; hudDateSub.text = status; }
            Hud.SetText(hudPause, paused ? "继续" : "暂停");
            Hud.State(hudPause, paused);
            Hud.SetText(hudSpeed, speed + "×");
            foreach (var (el, on) in hudViewButtons) Hud.State(el, false, on());
            RefreshHudToolStates();
            foreach (var (el, brush) in hudBrushChips) Hud.State(el, false, brushField.index == brush);
            if (hudHeightLabel != null) hudHeightLabel.text = heightField.value;
            bool legendOn = hudModeList[hudMode].Legend && !string.IsNullOrEmpty(legend.text);
            hudLegend.EnableInClassList("ec-hidden", !legendOn);
            if (legendOn) { hudLegendTitle.text = layerField.value; if (hudLegendText.text != legend.text) hudLegendText.text = legend.text; }
            SyncCoreTerritory();
            FollowCoreSelection();
            UpdateHudInfo();
            UpdateHudFeed();
            hudWindowAge += .25f;
            if (hudWindow != null && hudWindowAge >= 1 && hudWindow.CurrentIsLive) { hudWindowAge = 0; hudWindow.Refresh(); }
        }
    }
}
