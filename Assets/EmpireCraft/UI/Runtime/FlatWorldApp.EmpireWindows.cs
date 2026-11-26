using System;
using System.Collections.Generic;
using System.IO;
using EmpireCraft.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.Flat
{
    // 卷册 wide windows of the civilization HUD. Every window is draggable by its title bar
    // and collapsible into a draggable thumbnail pill. All ORBIS & EmpireCraft settings are
    // consolidated cleanly inside OpenHudWorld (世界设置).
    public sealed partial class FlatWorldApp
    {
        string hudWindowKey;
        double[] hudNationPeople = new double[WorldData.MaxNations];
        int[] hudNationZones = new int[WorldData.MaxNations];

        void OpenHudWindow(string key, ScrollWindow window, int page = 0)
        {
            hudWindows.Clear();
            hudWindow = window; hudWindowKey = key; hudWindowAge = 0;
            hudWindows.RemoveFromClassList("ec-hidden");
            hudWindows.RemoveFromClassList("ec-window-collapsed");
            hudWindows.pickingMode = PickingMode.Position;
            hudWindows.Add(window.Root);
            window.AttachFloating(hudWindows, OnHudWindowCollapsedChanged);
            hudWindows.BringToFront();
            window.Show(page);
            TickEmpireHud();
        }

        void OnHudWindowCollapsedChanged(bool collapsed)
        {
            if (hudWindows == null) return;
            hudWindows.EnableInClassList("ec-window-collapsed", collapsed);
            hudWindows.pickingMode = collapsed ? PickingMode.Ignore : PickingMode.Position;
            if (!collapsed) hudWindows.BringToFront();
        }

        void CloseHudWindow()
        {
            hudWindows.Clear();
            hudWindows.AddToClassList("ec-hidden");
            hudWindows.RemoveFromClassList("ec-window-collapsed");
            hudWindows.pickingMode = PickingMode.Position;
            hudWindow = null; hudWindowKey = null;
            TickEmpireHud();
        }

        void CountHudNations()
        {
            Array.Clear(hudNationPeople, 0, hudNationPeople.Length);
            Array.Clear(hudNationZones, 0, hudNationZones.Length);
            if (World == null) return;
            var owner = World.Owner; var people = World.Population;
            for (int i = 0; i < World.Count; i++) { int o = owner[i]; if (o >= 0 && o < hudNationPeople.Length) hudNationPeople[o] += people[i]; }
            for (int z = 0; z < World.ZoneCount; z++) { int o = World.ZoneOwner[z]; if (o >= 0 && o < hudNationZones.Length) hudNationZones[o]++; }
        }

        static VisualElement HudEmpty(string text)
        {
            var card = Hud.Card(null);
            card.Add(Hud.Text(text, "ec-empty"));
            return card;
        }

        // ---------- 列国榜 ----------
        void OpenHudNations()
        {
            var w = new ScrollWindow("列国榜", "empire_list", CloseHudWindow);
            w.Add("列国", "civ_nations", BuildHudNations, true, () => World == null ? null : World.Nations.Count.ToString());
            OpenHudWindow("nations", w);
        }

        void BuildHudNations(VisualElement page)
        {
            if (World == null) { page.Add(HudEmpty("世界尚在生成。")); return; }
            CountHudNations();
            int taken = 0; for (int c = 0; c < World.Nations.Count; c++) taken += hudNationZones[c];
            page.Add(Hud.Header("列国", "点一行选中该文明并对准其首都"));
            page.Add(Hud.Kpis(("文明", World.Nations.Count.ToString()), ("总人口", Simulation.TotalPopulation().ToString("N0")),
                ("已辟区域", taken.ToString("N0") + " / " + World.ZoneCount.ToString("N0")), ("世界", World.Width + " × " + World.Height)));
            if (World.Nations.Count == 0) { page.Add(HudEmpty("世上尚无文明。可在「文明 › 列国」中建立或播种。")); return; }
            var order = new List<int>();
            for (int c = 0; c < World.Nations.Count; c++) order.Add(c);
            order.Sort((a, b) => hudNationZones[b].CompareTo(hudNationZones[a]));
            var card = Hud.Card("疆域排序", "civ_nations", "区域 · 人口 · 存粮");
            for (int k = 0; k < order.Count; k++)
            {
                int c = order[k];
                var n = World.Nations[c];
                var row = new VisualElement();
                row.AddToClassList("ec-opt"); row.AddToClassList("ec-click");
                if (k % 2 == 0) row.AddToClassList("ec-alt");
                if (c == activeNation) row.AddToClassList("ec-active");
                var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("ec-swatch"); swatch.style.backgroundColor = Hud.Rgb(n.Color);
                row.Add(swatch);
                row.Add(Hud.Text((k + 1) + ". " + n.Name, "ec-label"));
                row.Add(Hud.Text(hudNationZones[c] + " 区 · " + hudNationPeople[c].ToString("N0") + " 人 · 粮 " + n.Food.ToString("N0"), "ec-opt-n"));
                row.AddManipulator(new Clickable(() => FocusHudNation(c)));
                card.Add(row);
            }
            page.Add(card);
        }

        void FocusHudNation(int nation)
        {
            if (World == null || nation < 0 || nation >= World.Nations.Count) return;
            activeNation = nation;
            int capital = World.Nations[nation].Capital;
            if (capital >= 0 && capital < World.Count && navigation && !Busy)
            {
                selected = capital; map.Select(capital);
                navigation.Focus(capital, CalendarDay);
            }
            UpdateHUD();
            hudWindow?.Refresh();
        }

        // ---------- 国务 ----------
        void OpenHudState()
        {
            var w = new ScrollWindow("国务", "TabEmpire", CloseHudWindow);
            w.Add("总览", "civ_empire", BuildHudState, true);
            OpenHudWindow("state", w);
        }

        void BuildHudState(VisualElement page)
        {
            if (World == null || activeNation < 0 || activeNation >= World.Nations.Count)
            { page.Add(HudEmpty("尚未选中文明。点地图上某国的地块，或在列国榜中点选。")); return; }
            CountHudNations();
            var n = World.Nations[activeNation];
            int rank = 1; for (int c = 0; c < World.Nations.Count; c++) if (hudNationZones[c] > hudNationZones[activeNation]) rank++;
            page.Add(Hud.Header(n.Name, "疆域第 " + rank + " 位"));
            page.Add(Hud.Kpis(("区域", hudNationZones[activeNation].ToString("N0")), ("人口", hudNationPeople[activeNation].ToString("N0")),
                ("存粮", n.Food.ToString("N0")), ("首都", n.Capital % World.Width + ", " + n.Capital / World.Width)));
            var two = new VisualElement(); two.AddToClassList("ec-two");
            var land = Hud.Card("国土", "civ_nations", null, true);
            land.Add(Hud.Row("首都地表", BiomeName(World.Biomes[n.Capital]), false));
            land.Add(Hud.Row("首都海拔", World.Elevation[n.Capital] + " m", true));
            land.Add(Hud.Row("首都水体", WaterName(World.Water[n.Capital]), false));
            land.Add(Hud.Row("人均存粮", hudNationPeople[activeNation] > 0 ? (n.Food / hudNationPeople[activeNation]).ToString("F2") : "—", true));
            two.Add(land);
            var later = Hud.Card("朝堂与制度", "civ_court");
            later.Add(Hud.Text("君主、宗族、官署、法理与制度随移植计划后续阶段接入：人口城市 → 宗族人物 → 政体制度。", "ec-text"));
            two.Add(later);
            page.Add(two);
            var act = Hud.Card("国事", "civ_empire");
            var buttons = new VisualElement(); buttons.AddToClassList("ec-btns");
            buttons.Add(Hud.Button("ec-btn", "civ_surface", "对准首都", () => FocusHudNation(activeNation)));
            buttons.Add(Hud.Button("ec-btn", "wild_develop", "扩张", () => { toolField.index = 2; CloseHudWindow(); SelectHudModeKeepTool(2); }));
            buttons.Add(Hud.Button("ec-btn", "road_connect", "首都路径", () => { toolField.index = 7; CloseHudWindow(); SelectHudModeKeepTool(2); }));
            act.Add(buttons);
            page.Add(act);
        }

        void SelectHudModeKeepTool(int mode)
        {
            int tool = toolField.index;
            SelectHudMode(mode);
            toolField.index = tool;
            SetStatus(tool == 2 ? "扩张：点相邻的无主区域。" : "路径：点目标地块查看从首都出发的陆路。");
        }

        // ---------- 宗族 / 战争 (systems not ported yet) ----------
        void OpenHudClans()
        {
            var w = new ScrollWindow("宗族", "specificClanIcon", CloseHudWindow);
            w.Add("门第", "specificClanIcon", page =>
            {
                page.Add(Hud.Header("宗族", "移植第二阶段"));
                page.Add(HudEmpty("宗族与人物尚未接入。移植到「宗族人物」阶段后，这里列出各家门第、族谱、族人名录与大事记。"));
            });
            OpenHudWindow("clans", w);
        }

        void OpenHudWar()
        {
            var w = new ScrollWindow("战争", "TabWarfare", CloseHudWindow);
            w.Add("战事", "TabWarfare", page =>
            {
                page.Add(Hud.Header("战争", World == null ? null : "第 " + ((int)(CalendarDay / 360) + 1) + " 年"));
                page.Add(HudEmpty("暂无战事。"));
            }, true);
            OpenHudWindow("war", w);
        }

        // ---------- 统一设置中心（世界设置） ----------
        void OpenHudWorld(int page)
        {
            if (hudWindowKey == "world" && hudWindow != null)
            {
                if (hudWindow.IsCollapsed) { hudWindow.SetCollapsed(false); return; }
                CloseHudWindow();
                return;
            }
            var w = new ScrollWindow("世界设置", "TabColor", CloseHudWindow);
            w.Add("界面与布局", "TabColor", BuildHudThemePage);
            w.Add("画面与显示", "nat_cloud", BuildHudViewPage);
            w.Add("开辟世界", "civ_globe", BuildHudGeneratePage);
            w.Add("行星气候", "nat_climate", BuildHudPlanetPage);
            w.Add("灾害演示", "nat_cyclone", BuildHudHazardsPage);
            w.Add("存档与高度图", "iconHistory", BuildHudFilesPage);
            OpenHudWindow("world", w, page);
        }

        static readonly Color32[][] HudThemeSwatches =
        {
            new[] { new Color32(18, 28, 38, 255), new Color32(222, 184, 112, 255), new Color32(132, 214, 232, 255) },
            new[] { new Color32(60, 48, 34, 255), new Color32(238, 218, 170, 255), new Color32(196, 52, 40, 255) },
            new[] { new Color32(32, 41, 50, 255), new Color32(218, 226, 232, 255), new Color32(104, 190, 150, 255) },
        };
        static readonly string[] HudThemeNotes = { "青铜星盘、刻度与星芒", "羊皮舆图、绳纹与罗盘", "石青底板、白玉与铆钉" };

        void BuildHudThemePage(VisualElement page)
        {
            page.Add(Hud.Header("界面与布局", "主题、面板折叠与拖动位置均自动记住"));
            var row = new VisualElement(); row.AddToClassList("ec-two");
            for (int i = 0; i < HudThemes.Length; i++)
            {
                int index = i;
                var card = Hud.Button("ec-theme-card", null, null, () => { ApplyHudTheme(index); hudWindow?.Refresh(); });
                if (i == hudTheme) card.AddToClassList("ec-on");
                card.Add(Hud.Text(HudThemeNames[i], "ec-ch-title"));
                var sw = new VisualElement { pickingMode = PickingMode.Ignore }; sw.AddToClassList("ec-theme-swatches");
                foreach (var c in HudThemeSwatches[i]) sw.Add(new VisualElement { pickingMode = PickingMode.Ignore, style = { backgroundColor = (Color)c } });
                card.Add(sw);
                card.Add(Hud.Text(HudThemeNotes[i], "ec-text"));
                row.Add(card);
            }
            page.Add(row);

            var layout = Hud.Card("悬浮面板布局", "civ_atlas", "所有选单与略缩按钮均可折叠和拖动");
            layout.Add(Hud.Text("按住任意选单标题栏或握把可自由拖动，点击右上角「−」折叠为可拖动的略缩按钮；单击略缩按钮即在当前位置展开。", "ec-text"));
            var lb = new VisualElement(); lb.AddToClassList("ec-btns");
            lb.Add(Hud.Button("ec-btn", null, "全部折叠", () => { SetAllHudPanelsCollapsed(true); }));
            lb.Add(Hud.Button("ec-btn", null, "全部展开", () => { SetAllHudPanelsCollapsed(false); }));
            lb.Add(Hud.Button("ec-btn", null, "重置全部位置与展开状态", () => { ResetHudLayout(true); SetStatus("已重置所有悬浮面板的位置与折叠状态。"); }));
            layout.Add(lb);
            page.Add(layout);

            var classic = Hud.Card("经典调试侧栏", "nat_grid");
            classic.Add(Hud.Text("ORBIS 原有侧栏保留为可选调试抽屉；日常设置均已统一到本设置窗口各页。", "ec-text"));
            var b = new VisualElement(); b.AddToClassList("ec-btns");
            b.Add(Hud.Button("ec-btn", null, root.ClassListContains("ec-classic") ? "收起经典面板" : "打开经典面板",
                () => { root.ToggleInClassList("ec-classic"); CloseHudWindow(); }));
            classic.Add(b);
            page.Add(classic);
        }

        static DropdownField HudMirror(string label, DropdownField source, Action<int> onChanged = null)
        {
            var d = new DropdownField(label, new List<string>(source.choices), source.index);
            d.AddToClassList("ec-field");
            d.RegisterValueChangedCallback(_ => { source.index = d.index; onChanged?.Invoke(d.index); });
            return d;
        }

        static Toggle HudMirror(string label, Toggle source)
        {
            var t = new Toggle(label) { value = source.value };
            t.AddToClassList("ec-field");
            t.RegisterValueChangedCallback(e => source.value = e.newValue);
            return t;
        }

        void BuildHudViewPage(VisualElement page)
        {
            page.Add(Hud.Header("画面与显示", "所有图面与气象显示开关统一在此管理"));
            var two = new VisualElement(); two.AddToClassList("ec-two");

            var card = Hud.Card("云层与图面显示", "nat_cloud", null, true);
            card.Add(HudMirror("画面与云层质量", cloudQuality));
            card.Add(HudMirror("云层与云影", cloudToggle));
            if (volumetricToggle != null) card.Add(HudMirror("体积云", volumetricToggle));
            card.Add(HudMirror("等高线 (快捷键 C)", contourToggle));
            card.Add(HudMirror("地块网格 (快捷键 G)", gridToggle));
            if (hazardLiveWeatherToggle != null) card.Add(HudMirror("接通实时台风与暴雨事件", hazardLiveWeatherToggle));
            two.Add(card);

            var sky = Hud.Card("实时状态与工具", "nat_daynight");
            sky.Add(Hud.Text(string.IsNullOrEmpty(skyStatus.text) ? "天空系统待命。" : skyStatus.text, "ec-text"));
            sky.Add(Hud.Text(string.IsNullOrEmpty(waterStatus.text) ? "局部水面待命。" : waterStatus.text, "ec-text"));
            var actions = new VisualElement(); actions.AddToClassList("ec-btns");
            actions.Add(Hud.Button("ec-btn", null, "无UI截图", () => { if (World != null && !Busy) { CloseHudWindow(); StartCoroutine(Capture()); } }));
            actions.Add(Hud.Button("ec-btn", "iconHistory", "打开气候统计", () => { CloseHudWindow(); ShowClimateStatistics(); }));
            sky.Add(actions);
            two.Add(sky);

            page.Add(two);
        }

        void BuildHudGeneratePage(VisualElement page)
        {
            page.Add(Hud.Header("开辟世界", World == null ? null : "当前 " + World.Width + " × " + World.Height + " · " + World.Seed));
            var card = Hud.Card("新世界参数", "civ_globe", null, true);
            var seed = new TextField("种子") { value = seedField.value, maxLength = 128 };
            seed.AddToClassList("ec-field");
            seed.RegisterValueChangedCallback(e => seedField.value = e.newValue);
            card.Add(seed);
            card.Add(HudMirror("地图大小", sizeField));
            card.Add(HudMirror("地形预设", presetField));
            var b = new VisualElement(); b.AddToClassList("ec-btns");
            b.Add(Hud.Button("ec-btn", "civ_globe", "生成新世界", () => { Generate(); CloseHudWindow(); }));
            card.Add(b);
            page.Add(card);
            var note = Hud.Card("说明", null);
            note.Add(Hud.Text("生成在后台进行，完成前当前世界照常运行。「地球」使用内置真实高程、海湖与河道资料。地形重算请使用「塑造 › 地形 › 重算」，播种文明请使用「文明 › 列国 › 播种文明」。", "ec-text"));
            page.Add(note);
        }

        void BuildHudPlanetPage(VisualElement page)
        {
            page.Add(Hud.Header("行星气候", "ExoPlaSim 轨道与温室气体配置"));
            var card = Hud.Card("行星与温室气候 (ExoPlaSim)", "nat_climate", null, true);
            Label summary = Hud.Text(planetSummaryLabel != null ? planetSummaryLabel.text : "", "ec-text");
            Label co2Val = null, obliqVal = null;
            SliderInt co2 = null;
            Slider obliq = null;

            if (planetPresetField != null)
            {
                card.Add(HudMirror("行星预设", planetPresetField, _ =>
                {
                    if (co2 != null && planetCo2Slider != null)
                    {
                        co2.SetValueWithoutNotify(planetCo2Slider.value);
                        if (co2Val != null) co2Val.text = planetCo2Slider.value + " ppm";
                    }
                    if (obliq != null && planetObliqSlider != null)
                    {
                        obliq.SetValueWithoutNotify(planetObliqSlider.value);
                        if (obliqVal != null) obliqVal.text = planetObliqSlider.value.ToString("F1") + "°";
                    }
                    if (planetSummaryLabel != null) summary.text = planetSummaryLabel.text;
                }));
            }
            if (planetCo2Slider != null)
            {
                var row = new VisualElement(); row.AddToClassList("ec-slider-row");
                co2 = new SliderInt("CO2 (ppm)", planetCo2Slider.lowValue, planetCo2Slider.highValue) { value = planetCo2Slider.value };
                co2.AddToClassList("ec-field");
                co2Val = Hud.Text(co2.value + " ppm", "ec-slider-val");
                co2.RegisterValueChangedCallback(e =>
                {
                    planetCo2Slider.value = e.newValue;
                    co2Val.text = e.newValue + " ppm";
                    if (planetSummaryLabel != null) summary.text = planetSummaryLabel.text;
                });
                row.Add(co2); row.Add(co2Val);
                card.Add(row);
            }
            if (planetObliqSlider != null)
            {
                var row = new VisualElement(); row.AddToClassList("ec-slider-row");
                obliq = new Slider("地轴倾角 (°)", planetObliqSlider.lowValue, planetObliqSlider.highValue) { value = planetObliqSlider.value };
                obliq.AddToClassList("ec-field");
                obliqVal = Hud.Text(obliq.value.ToString("F1") + "°", "ec-slider-val");
                obliq.RegisterValueChangedCallback(e =>
                {
                    planetObliqSlider.value = e.newValue;
                    obliqVal.text = e.newValue.ToString("F1") + "°";
                    if (planetSummaryLabel != null) summary.text = planetSummaryLabel.text;
                });
                row.Add(obliq); row.Add(obliqVal);
                card.Add(row);
            }
            card.Add(summary);
            var btns = new VisualElement(); btns.AddToClassList("ec-btns");
            btns.Add(Hud.Button("ec-btn", "nat_climate", "应用行星气候并重算", () => { ApplyPlanetClimate(); CloseHudWindow(); }));
            card.Add(btns);
            page.Add(card);
        }

        void BuildHudHazardsPage(VisualElement page)
        {
            page.Add(Hud.Header("灾害演示", IsDemoWorldActive ? "当前处于隔离演示：" + activeDemoLabel : "64×64 SWE 局部水灾与隔离地球气象演示"));
            var local = Hud.Card("局部水灾情景 (64×64 SWE)", "nat_rain", null, true);
            var row1 = new VisualElement(); row1.AddToClassList("ec-btns");
            row1.Add(Hud.Button("ec-btn", "nat_rainfall", "降雨漫堤洪水", () => { TriggerHazard(FlatHazardKind.RainFlood); CloseHudWindow(); }));
            row1.Add(Hud.Button("ec-btn", "nat_cyclone", "台风风暴潮", () => { TriggerHazard(FlatHazardKind.TyphoonSurge); CloseHudWindow(); }));
            row1.Add(Hud.Button("ec-btn", "nat_current", "海床位移海啸", () => { TriggerHazard(FlatHazardKind.Tsunami); CloseHudWindow(); }));
            local.Add(row1);
            var row2 = new VisualElement(); row2.AddToClassList("ec-btns");
            row2.Add(Hud.Button("ec-btn", "civ_surface", "定位灾区", () => { FocusActiveHazard(); CloseHudWindow(); }));
            row2.Add(Hud.Button("ec-btn", null, "清理退水", () =>
            {
                if (map && map.LocalWater)
                {
                    map.LocalWater.ClearHazard();
                    navigation?.SyncMaterials();
                    UpdateHUD();
                    SetStatus("已清理局部水灾并恢复基准河湖海材质。");
                }
                hudWindow?.Refresh();
            }));
            local.Add(row2);
            page.Add(local);

            var demo = Hud.Card("隔离地球气象演示", "nat_cyclone", "原局日历与存档完全受保护");
            var row3 = new VisualElement(); row3.AddToClassList("ec-btns");
            row3.Add(Hud.Button("ec-btn", "nat_cyclone", "东海台风登陆", () => { StartEarthStormScenario(FlatStormScenario.EastChinaSeaLandfall); CloseHudWindow(); }));
            row3.Add(Hud.Button("ec-btn", "nat_cyclone", "北大西洋飓风登陆", () => { StartEarthStormScenario(FlatStormScenario.NorthAtlanticLandfall); CloseHudWindow(); }));
            if (IsDemoWorldActive)
                row3.Add(Hud.Button("ec-btn", "iconHistory", "返回原局", () => { ExitIsolatedDemoWorld(); CloseHudWindow(); }));
            demo.Add(row3);
            demo.Add(Hud.Text(string.IsNullOrEmpty(waterStatus.text) ? "提示：实时台风与暴雨总开关在「画面与显示」页中统一管理。" : waterStatus.text, "ec-text"));
            page.Add(demo);
        }

        void BuildHudFilesPage(VisualElement page)
        {
            page.Add(Hud.Header("存档与高度图", Application.persistentDataPath));
            var two = new VisualElement(); two.AddToClassList("ec-two");

            var save = Hud.Card("世界存档", "iconHistory", null, true);
            save.Add(Hud.Row("存档文件", Path.GetFileName(SavePath), false));
            var b = new VisualElement(); b.AddToClassList("ec-btns");
            b.Add(Hud.Button("ec-btn", null, "保存", Save));
            b.Add(Hud.Button("ec-btn", null, "读取", () => { Load(); CloseHudWindow(); }));
            b.Add(Hud.Button("ec-btn", null, "打开目录", () => Application.OpenURL(new Uri(Application.persistentDataPath + Path.DirectorySeparatorChar).AbsoluteUri)));
            save.Add(b);
            two.Add(save);

            var raster = Hud.Card("高度图导入 / 导出", "nat_relief");
            var path = new TextField("文件路径") { value = pathField.value };
            path.AddToClassList("ec-field");
            path.RegisterValueChangedCallback(e => pathField.value = e.newValue);
            raster.Add(path);
            raster.Add(HudMirror("按米保留原图高程", root.Q<Toggle>("preserveElevation")));
            var r = new VisualElement(); r.AddToClassList("ec-btns");
            r.Add(Hud.Button("ec-btn", null, "导出", () =>
            {
                if (World == null || Busy) return;
                FlatRasterIO.Export(World, HeightPath);
                SetStatus("已导出高度、水体预览和河网图：" + Path.ChangeExtension(HeightPath, "rivers.png"));
            }));
            r.Add(Hud.Button("ec-btn", null, "导入为新世界", () => { Import(); CloseHudWindow(); }));
            raster.Add(r);
            raster.Add(Hud.Text("灰度高度图：黑 −4000 米，白 4000 米。", "ec-text"));
            two.Add(raster);

            page.Add(two);
        }
    }
}
