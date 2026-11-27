using System;
using System.Collections.Generic;
using System.IO;
using EmpireCraft.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.Flat
{
    // 卷册 wide windows of the civilization HUD. Live pages are rebuilt once a second while
    // open; pages with input fields are static so typing is never interrupted.
    public sealed partial class FlatWorldApp
    {
        string hudWindowKey;
        double[] hudNationPeople = new double[WorldData.MaxNations];
        int[] hudNationZones = new int[WorldData.MaxNations];

        void OpenHudWindow(string key, IHudWindow window, int page = 0)
        {
            hudWindows.Clear();
            hudWindow = window; hudWindowKey = key; hudWindowAge = 0;
            hudWindows.Add(window.Root);
            hudWindows.RemoveFromClassList("ec-hidden");
            window.Show(page);
        }

        void CloseHudWindow()
        {
            hudWindows.Clear();
            hudWindows.AddToClassList("ec-hidden");
            hudWindow = null; hudWindowKey = null;
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
            if (CoreEnabled) { OpenCoreNations(); return; }
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
            if (CoreEnabled) { OpenCoreState(activeRealm); return; }
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
        void OpenHudClans() => OpenHudClans(0);

        void OpenHudClans(int first)
        {
            if (CoreEnabled) { OpenHudClansCore(first); return; }
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
            if (CoreEnabled) { OpenCoreWar(); return; }
            var w = new ScrollWindow("战争", "TabWarfare", CloseHudWindow);
            w.Add("战事", "TabWarfare", page =>
            {
                page.Add(Hud.Header("战争", World == null ? null : "第 " + ((int)(CalendarDay / 360) + 1) + " 年"));
                page.Add(HudEmpty("暂无战事。"));
            }, true);
            OpenHudWindow("war", w);
        }

        // ---------- 世界设置 ----------
        void OpenHudWorld(int page)
        {
            if (hudWindowKey == "world") { CloseHudWindow(); return; }
            var w = new ScrollWindow("世界设置", "civ_globe", CloseHudWindow);
            w.Add("界面主题", "TabColor", BuildHudThemePage);
            w.Add("开辟世界", "civ_globe", BuildHudGeneratePage);
            w.Add("存档", "iconHistory", BuildHudFilesPage);
            w.Add("画面", "nat_cloud", BuildHudViewPage);
            OpenHudWindow("world", w, page);
        }

        static readonly Color32[][] HudThemeSwatches =
        {
            new[] { new Color32(26, 19, 17, 255), new Color32(196, 90, 72, 255), new Color32(246, 212, 138, 255) },
            new[] { new Color32(18, 28, 38, 255), new Color32(222, 184, 112, 255), new Color32(132, 214, 232, 255) },
            new[] { new Color32(60, 48, 34, 255), new Color32(238, 218, 170, 255), new Color32(196, 52, 40, 255) },
            new[] { new Color32(32, 41, 50, 255), new Color32(218, 226, 232, 255), new Color32(104, 190, 150, 255) },
        };
        static readonly string[] HudThemeNotes = { "黑漆朱边、金角像素框与像素字", "青铜星盘、刻度与星芒", "羊皮舆图、绳纹与罗盘", "石青底板、白玉与铆钉" };

        void BuildHudThemePage(VisualElement page)
        {
            page.Add(Hud.Header("界面主题", "即时生效，自动记住"));
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
            var classic = Hud.Card("经典面板", "nat_grid");
            classic.Add(Hud.Text("ORBIS 原有的侧栏（全部图层、操作与调试信息）可作为抽屉打开，与新界面并用。", "ec-text"));
            var b = new VisualElement(); b.AddToClassList("ec-btns");
            b.Add(Hud.Button("ec-btn", null, root.ClassListContains("ec-classic") ? "收起经典面板" : "打开经典面板",
                () => { root.ToggleInClassList("ec-classic"); CloseHudWindow(); }));
            classic.Add(b);
            page.Add(classic);
        }

        static DropdownField HudMirror(string label, DropdownField source)
        {
            var d = new DropdownField(label, new List<string>(source.choices), source.index);
            d.AddToClassList("ec-field");
            d.RegisterValueChangedCallback(_ => source.index = d.index);
            return d;
        }

        static Toggle HudMirror(string label, Toggle source)
        {
            var t = new Toggle(label) { value = source.value };
            t.AddToClassList("ec-field");
            t.RegisterValueChangedCallback(e => source.value = e.newValue);
            return t;
        }

        void BuildHudGeneratePage(VisualElement page)
        {
            page.Add(Hud.Header("开辟世界", World == null ? null : "当前 " + World.Width + " × " + World.Height + " · " + World.Seed));
            var card = Hud.Card("新世界参数", "civ_globe", null, true);
            var seed = new TextField("种子") { value = seedField.value, maxLength = 128 };
            seed.AddToClassList("ec-field");
            seed.RegisterValueChangedCallback(e => seedField.value = e.newValue);
            card.Add(seed);
            card.Add(HudMirror("大小", sizeField));
            card.Add(HudMirror("地形", presetField));
            var b = new VisualElement(); b.AddToClassList("ec-btns");
            b.Add(Hud.Button("ec-btn", "civ_globe", "生成新世界", () => { Generate(); CloseHudWindow(); }));
            b.Add(Hud.Button("ec-btn", "nat_rebuild", "重算当前世界", () => { RebuildFromHud(); CloseHudWindow(); }));
            if (!CoreEnabled) b.Add(Hud.Button("ec-btn", "population_add", "播种文明", SeedNationsFromHud));
            card.Add(b);
            page.Add(card);
            var note = Hud.Card("说明", null);
            note.Add(Hud.Text("生成在后台进行，完成前当前世界照常运行。「地球」使用内置真实高程、海湖与河道资料。", "ec-text"));
            page.Add(note);
        }

        void BuildHudFilesPage(VisualElement page)
        {
            page.Add(Hud.Header("存档", Application.persistentDataPath));
            if (CoreEnabled)
            {
                // With the core on, saves are slots holding the world and the realms together.
                var slots = Hud.Card("存档与读档", "iconHistory", null, true);
                slots.Add(Hud.Text("世界与列国一起存，可存多份，每十年自动存一份。", "ec-text"));
                var sb = new VisualElement(); sb.AddToClassList("ec-btns");
                sb.Add(Hud.Button("ec-btn", null, "打开存档窗口", OpenCoreSaves));
                slots.Add(sb);
                page.Add(slots);
            }
            var save = Hud.Card("世界存档", "iconHistory", null, true);
            save.Add(Hud.Row("存档位置", SavePath, false));
            var b = new VisualElement(); b.AddToClassList("ec-btns");
            b.Add(Hud.Button("ec-btn", null, "保存", Save));
            b.Add(Hud.Button("ec-btn", null, "读取", () => { Load(); CloseHudWindow(); }));
            b.Add(Hud.Button("ec-btn", null, "打开文件夹", () => Application.OpenURL(new Uri(Application.persistentDataPath + Path.DirectorySeparatorChar).AbsoluteUri)));
            save.Add(b);
            page.Add(save);
            var raster = Hud.Card("高度图", "nat_relief");
            var path = new TextField("文件") { value = pathField.value };
            path.AddToClassList("ec-field");
            path.RegisterValueChangedCallback(e => pathField.value = e.newValue);
            raster.Add(path);
            raster.Add(HudMirror("保留米制高程", root.Q<Toggle>("preserveElevation")));
            var r = new VisualElement(); r.AddToClassList("ec-btns");
            r.Add(Hud.Button("ec-btn", null, "导出", () =>
            {
                if (!Ready()) return;
                FlatRasterIO.Export(World, HeightPath);
                SetStatus("已导出高度、水体预览和河网图：" + Path.ChangeExtension(HeightPath, "rivers.png"));
            }));
            r.Add(Hud.Button("ec-btn", null, "导入为新世界", () => { Import(); CloseHudWindow(); }));
            raster.Add(r);
            raster.Add(Hud.Text("灰度高度图：黑 −4000 米，白 4000 米。", "ec-text"));
            page.Add(raster);
        }

        void BuildHudViewPage(VisualElement page)
        {
            page.Add(Hud.Header("画面"));
            var card = Hud.Card("云与图面", "nat_cloud", null, true);
            card.Add(HudMirror("云层质量", cloudQuality));
            card.Add(HudMirror("云层", cloudToggle));
            if (volumetricToggle != null) card.Add(HudMirror("体积云", volumetricToggle));
            card.Add(HudMirror("等高线", contourToggle));
            card.Add(HudMirror("网格", gridToggle));
            page.Add(card);
            var sky = Hud.Card("天空与水面", "nat_daynight");
            sky.Add(Hud.Text(skyStatus.text, "ec-text"));
            sky.Add(Hud.Text(waterStatus.text, "ec-text"));
            page.Add(sky);
        }
    }
}
