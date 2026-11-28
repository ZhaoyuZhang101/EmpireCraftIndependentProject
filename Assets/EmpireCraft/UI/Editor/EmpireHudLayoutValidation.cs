using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using EmpireCraft.UI;
using Orbis.Flat;
using Orbis.Planet;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace EmpireCraft.EditorTools
{
    /// <summary>
    /// Unity 6000 D3D11 Play-mode validation for the consolidated EmpireCraft HUD:
    ///   1) Zero duplicated tools across modes/categories and settings;
    ///   2) All ORBIS settings unified into 世界设置 (6 pages, live-mirrored);
    ///   3) Default visible panels (especially ecLegend vs ecFeed) strictly non-overlapping;
    ///   4) Every HUD panel & ScrollWindow is collapsible into a draggable thumbnail pill,
    ///      draggable when expanded, draggable while collapsed without auto-expanding, and
    ///      expands cleanly at the dragged pill position on click;
    ///   5) Captures composite UI+World PNGs for visual inspection.
    ///
    /// Command line:
    ///   Unity.exe -batchmode -force-d3d11 -projectPath &lt;p&gt;
    ///     -executeMethod EmpireCraft.EditorTools.EmpireHudLayoutValidation.Run
    ///     -ecLayoutOut ValidationOutput/HudLayout
    /// </summary>
    [InitializeOnLoad]
    public static class EmpireHudLayoutValidation
    {
        const string Flag = "EmpireHudLayoutVal.Out";
        const int Width = 1440, Height = 900;
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        static EmpireHudLayoutValidation()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(Flag, "")))
                EditorApplication.update += Step;
        }

        public static void Run()
        {
            string outDir = Arg("-ecLayoutOut") ?? Path.GetFullPath("ValidationOutput/HudLayout");
            Directory.CreateDirectory(outDir);
            SessionState.SetString(Flag, outDir);
            SessionState.SetFloat("EmpireHudLayoutVal.Start", (float)EditorApplication.timeSinceStartup);
            EditorSceneManager.OpenScene("Assets/ORBIS/Scenes/Orbis2DWorld.unity");
            EditorApplication.update += Step;
            EditorApplication.EnterPlaymode();
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static int stage, waitFrames, failures, unityErrors;
        static readonly List<string> lines = new List<string>();
        static RenderTexture uiTarget;
        static int demoTile = -1;
        static bool hookInstalled;

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                unityErrors++;
                lines.Add("UNITY_ERR: " + condition);
            }
        }

        static void Check(bool ok, string message)
        {
            if (!ok) failures++;
            string line = (ok ? "PASS " : "FAIL ") + message;
            lines.Add(line);
            Debug.Log("[HudLayoutVal] " + line);
        }

        static void Step()
        {
            string outDir = SessionState.GetString(Flag, "");
            if (string.IsNullOrEmpty(outDir)) { EditorApplication.update -= Step; return; }
            if (!hookInstalled) { Application.logMessageReceived += OnLog; hookInstalled = true; }

            double elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat("EmpireHudLayoutVal.Start", 0);
            if (elapsed > 600) { Finish(outDir, 3); return; }
            if (!EditorApplication.isPlaying) return;

            var app = UnityEngine.Object.FindFirstObjectByType<FlatWorldApp>();
            var map = app ? Get<FlatMapRenderer>(app, "map") : null;
            var nav = app ? Get<FlatScrollNavigator>(app, "navigation") : null;
            var cam = app ? Get<Camera>(app, "viewCamera") : null;
            if (!app || app.World == null || app.Busy || !map || !nav || !cam || !map.InitialSurfaceReady) return;

            try
            {
                switch (stage)
                {
                    case 0:
                        SetupScene(app, nav, map);
                        stage = 1; waitFrames = 8;
                        break;
                    case 1:
                        if (--waitFrames > 0) return;
                        VerifyDeduplication(app);
                        VerifyDefaultNoOverlap(app);
                        CaptureComposite(app, cam, nav, Path.Combine(outDir, "01_default_clean.png"));
                        // Open unified settings window on page 0 (界面与布局)
                        Call(app, "OpenHudWorld", 0);
                        stage = 2; waitFrames = 6;
                        break;
                    case 2:
                        if (--waitFrames > 0) return;
                        CaptureComposite(app, cam, nav, Path.Combine(outDir, "02_settings_theme_layout.png"));
                        Get<ScrollWindow>(app, "hudWindow")?.Show(1);
                        stage = 3; waitFrames = 5;
                        break;
                    case 3:
                        if (--waitFrames > 0) return;
                        CaptureComposite(app, cam, nav, Path.Combine(outDir, "03_settings_display.png"));
                        VerifyUnifiedSettingsPages(app);
                        VerifyDragAndCollapseAll(app);
                        stage = 4; waitFrames = 6;
                        break;
                    case 4:
                        if (--waitFrames > 0) return;
                        CaptureComposite(app, cam, nav, Path.Combine(outDir, "04_dragged_and_collapsed.png"));
                        // Collapse every panel into compact draggable pills
                        Call(app, "SetAllHudPanelsCollapsed", true, false);
                        Call(app, "TickEmpireHud");
                        stage = 5; waitFrames = 6;
                        break;
                    case 5:
                        if (--waitFrames > 0) return;
                        VerifyAllCollapsedState(app);
                        CaptureComposite(app, cam, nav, Path.Combine(outDir, "05_all_collapsed_pills.png"));
                        // Reset cleanly and verify 1-click DirectRail + unselected auto-hide tile card
                        Call(app, "CloseHudWindow");
                        Call(app, "ResetHudLayout", false);
                        Call(app, "SelectHudMode", 2); // 文明 1-click direct rail
                        Call(app, "ClearHudSelection"); // unselected -> ecInfo auto-hidden
                        var toolField = Get<DropdownField>(app, "toolField");
                        if (toolField != null) toolField.index = 1; // 建立文明 active -> bottom-center banner
                        Call(app, "TickEmpireHud");
                        stage = 6; waitFrames = 5;
                        break;
                    case 6:
                        if (--waitFrames > 0) return;
                        var byId = Get<Dictionary<string, HudFloatingPanel>>(app, "hudPanelsById");
                        Check(!byId["info"].IsLogicalVisible && byId["info"].Panel.ClassListContains("ec-hidden"),
                            "Contextual ecInfo auto-hides cleanly when no tile is selected");
                        Check(byId["brush"].IsLogicalVisible && !byId["brush"].Panel.ClassListContains("ec-hidden"),
                            "Bottom-center contextual tool bar appears automatically when a map tool is active");
                        var hudEl = Get<VisualElement>(app, "hud");
                        Check(!hudEl.Q("ecRail").worldBound.Overlaps(hudEl.Q("ecFeed").worldBound),
                            $"Civilization direct rail disjoint from bottom-left feed (railYMax={hudEl.Q("ecRail").worldBound.yMax:F0}, feedYMin={hudEl.Q("ecFeed").worldBound.yMin:F0})");
                        CaptureComposite(app, cam, nav, Path.Combine(outDir, "06_civ_direct_rail_clean.png"));
                        Call(app, "CancelActiveHudTool");
                        Check(!byId["brush"].IsLogicalVisible,
                            "Bottom-center contextual tool bar hides cleanly when active tool is cancelled");
                        // Zoom close onto the river reach to capture crystal-clear riverbed, caustics & reflections
                        var navT = typeof(FlatScrollNavigator);
                        float minZoom = (float)navT.GetProperty("MinZoom", Private).GetValue(nav);
                        nav.Focus(demoTile, app.CalendarDay);
                        navT.GetField("targetZoom", Private).SetValue(nav, minZoom);
                        navT.GetField("zoom", Private).SetValue(nav, minZoom);
                        navT.GetField("zoomVelocity", Private)?.SetValue(nav, 0f);
                        nav.Tick(0, app.CalendarDay);
                        stage = 7; waitFrames = 10;
                        break;
                    case 7:
                        if (--waitFrames > 0 || nav.UsingShaderPreview) return;
                        CaptureComposite(app, cam, nav, Path.Combine(outDir, "07_river_closeup_clear.png"));
                        Check(unityErrors == 0, "Zero new Unity console errors (" + unityErrors + ")");
                        Finish(outDir, failures == 0 ? 0 : 1);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                lines.Add("EXCEPTION: " + e);
                failures++;
                Finish(outDir, 2);
            }
        }

        static void SetupScene(FlatWorldApp app, FlatScrollNavigator nav, FlatMapRenderer map)
        {
            lines.Clear();
            failures = 0;
            unityErrors = 0;
            lines.Add($"ENV Unity={Application.unityVersion} GPU={SystemInfo.graphicsDeviceName} API={SystemInfo.graphicsDeviceType}");

            Set(app, "paused", true);
            Call(app, "ResetHudLayout", false);
            Call(app, "ApplyHudTheme", 0);

            var w = app.World;
            demoTile = PickLandCoastTile(w);
            double lon = ((demoTile % w.Width + .5) / w.Width - .5) * Math.PI * 2;
            double fraction = (-lon + 35 * Math.PI / 180) / (Math.PI * 2) - .5; fraction -= Math.Floor(fraction);
            w.Day = Math.Floor(w.Day) + fraction;

            nav.Focus(demoTile, app.CalendarDay);
            var navType = typeof(FlatScrollNavigator);
            float tz = (float)navType.GetField("targetZoom", Private).GetValue(nav);
            navType.GetField("zoom", Private).SetValue(nav, tz);
            navType.GetField("zoomVelocity", Private)?.SetValue(nav, 0f);
            nav.Tick(0, app.CalendarDay);
            Set(app, "selected", demoTile);
            map.Select(demoTile);

            // Switch to Nations layer so legend has text just like user screenshot,
            // and push 5 multi-line feed entries so ecFeed reaches its full height!
            Call(app, "ChooseLayer", MapLayer.Nations);
            Call(app, "SelectHudMode", 0);
            for (int i = 0; i < 5; i++)
            {
                Call(app, "SetStatus", $"测试纪事 #{i + 1}：世界就绪默认星球，滚轮进入局部地表，拖动可连续穿过日期线和极区。");
                Call(app, "TickEmpireHud");
            }
            Call(app, "UpdateHUD");
            Call(app, "TickEmpireHud");

            var panel = Get<PanelSettings>(app, "panel");
            uiTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "HudLayoutVal UI" };
            panel.targetTexture = uiTarget;
            panel.clearColor = true;
            panel.colorClearValue = new Color(0, 0, 0, 0);
        }

        static void VerifyDeduplication(FlatWorldApp app)
        {
            var modes = Get<List<HudMode>>(app, "hudModeList");
            var seenTools = new Dictionary<string, string>();
            var dupes = new List<string>();
            var deadItems = new List<string>();
            bool hasScatteredDisplayToggle = false;

            foreach (var m in modes)
            {
                if (m.Categories.Count == 0) deadItems.Add($"Empty mode {m.Name}");
                foreach (var c in m.Categories)
                {
                    if (!c.AnyPorted) deadItems.Add($"Dead category {m.Name}/{c.Name}");
                    if (m.Name == "观察" && (c.Name == "舆图" || c.Name == "信仰" || c.Name == "军情"))
                        dupes.Add($"Redundant category {m.Name}/{c.Name}");
                    foreach (var t in c.Tools)
                    {
                        if (t.Run == null) deadItems.Add($"Unported dead button {m.Name}/{c.Name}/{t.Name}");
                        if (m.Name == "观察" && (t.Name == "云层" || t.Name == "网格" || t.Name == "等高线" || t.Name == "聚焦地块"))
                            hasScatteredDisplayToggle = true;
                        string loc = $"{m.Name}/{c.Name}/{t.Name}";
                        if (seenTools.TryGetValue(t.Name, out string prev))
                            dupes.Add($"Duplicate tool '{t.Name}' in [{prev}] and [{loc}]");
                        else
                            seenTools[t.Name] = loc;
                    }
                }
            }
            Check(dupes.Count == 0, "Zero duplicate tools/categories across all 4 modes" + (dupes.Count > 0 ? " -> " + string.Join("; ", dupes) : ""));
            Check(deadItems.Count == 0, "Zero dead/unported placeholder buttons across all 4 modes" + (deadItems.Count > 0 ? " -> " + string.Join("; ", deadItems) : ""));
            Check(!hasScatteredDisplayToggle, "Display settings (云层/网格/等高线/聚焦地块) removed from 观察 mode and unified in 设置");

            // Verify 1-click DirectRail on 塑造 / 文明 / 营建 (zero secondary popout required)
            Check(!modes[0].DirectRail && modes[1].DirectRail && modes[2].DirectRail && modes[3].DirectRail,
                "塑造 / 文明 / 营建 use 1-click DirectRail while 观察 uses 4-category layer popout");

            // Tile info card should no longer duplicate 气候统计 button
            var infoButtons = Get<VisualElement>(app, "hudInfoButtons");
            bool hasClimateBtn = false;
            foreach (var lbl in infoButtons.Query<Label>(className: "ec-label").ToList())
                if (lbl.text == "气候") hasClimateBtn = true;
            Check(!hasClimateBtn, "Tile card ecInfo no longer duplicates 气候统计 button");
        }

        static void VerifyDefaultNoOverlap(FlatWorldApp app)
        {
            var hud = Get<VisualElement>(app, "hud");
            var legend = hud.Q("ecLegend");
            var feed = hud.Q("ecFeed");
            var era = hud.Q("ecEra");
            var modes = hud.Q("ecModes");
            var views = hud.Q("ecViews");
            var rail = hud.Q("ecRail");
            var info = hud.Q("ecInfo");

            Rect rLegend = legend.worldBound;
            Rect rFeed = feed.worldBound;
            bool legendFeedDisjoint = rLegend.height > 10 && rFeed.height > 10 && !rLegend.Overlaps(rFeed);
            float gap = rFeed.yMin - rLegend.yMax;
            Check(legendFeedDisjoint && gap >= 4f,
                $"ecLegend and ecFeed strictly non-overlapping (legendY={rLegend.yMin:F1}..{rLegend.yMax:F1}, feedY={rFeed.yMin:F1}..{rFeed.yMax:F1}, gap={gap:F1}px)");

            Check(!era.worldBound.Overlaps(modes.worldBound) && !modes.worldBound.Overlaps(views.worldBound),
                $"Top bars non-overlapping (eraX={era.worldBound.xMax:F0}, modesX={modes.worldBound.xMin:F0}..{modes.worldBound.xMax:F0}, viewsX={views.worldBound.xMin:F0})");
            Check(!rail.worldBound.Overlaps(legend.worldBound) && !rail.worldBound.Overlaps(feed.worldBound),
                $"Left rail disjoint from legend & feed (railYMax={rail.worldBound.yMax:F0}, legendYMin={legend.worldBound.yMin:F0}, feedYMin={feed.worldBound.yMin:F0})");
            Check(!info.worldBound.Overlaps(views.worldBound),
                $"Tile card disjoint from top-right views bar (viewsYMax={views.worldBound.yMax:F0}, infoYMin={info.worldBound.yMin:F0})");

            // Ensure ecFeed vertical scroller & internal slider input stay strictly inside the gold frame border
            var feedScroll = hud.Q<ScrollView>("ecFeedRows");
            var vScroller = feedScroll?.verticalScroller;
            var sliderInput = vScroller?.Q(className: "unity-base-field__input");
            float maxChildX = vScroller != null ? vScroller.worldBound.xMax : feed.worldBound.xMin;
            if (sliderInput != null && sliderInput.worldBound.xMax > maxChildX) maxChildX = sliderInput.worldBound.xMax;
            float rightInset = feed.worldBound.xMax - maxChildX;
            Check(rightInset >= 6f,
                $"ecFeed vertical scrollbar strictly contained inside gold frame (feedXMax={feed.worldBound.xMax:F1}, scrollerMaxX={maxChildX:F1}, inset={rightInset:F1}px)");
        }

        static void VerifyUnifiedSettingsPages(FlatWorldApp app)
        {
            var win = Get<ScrollWindow>(app, "hudWindow");
            Check(win != null && win.PageCount == 6, $"Unified 世界设置 window has all 6 pages (actual={win?.PageCount})");

            // Step through all 6 settings pages and verify zero exceptions + key controls exist.
            string[] expectedKeywords = { "重置全部位置", "等高线", "生成新世界", "应用行星气候", "北大西洋飓风登陆", "导入为新世界" };
            for (int p = 0; p < win.PageCount; p++)
            {
                win.Show(p);
                bool found = false;
                foreach (var lbl in win.Root.Query<Label>().ToList())
                    if (!string.IsNullOrEmpty(lbl.text) && lbl.text.Contains(expectedKeywords[p])) { found = true; break; }
                Check(found, $"Settings page {p} contains unified control '{expectedKeywords[p]}'");
            }

            // Test live mirror on page 1 (画面与显示 -> gridToggle)
            win.Show(1);
            var gridToggle = Get<Toggle>(app, "gridToggle");
            bool before = gridToggle.value;
            Toggle mirroredGrid = null;
            foreach (var t in win.Root.Query<Toggle>().ToList())
                if (t.label != null && t.label.Contains("地块网格")) { mirroredGrid = t; break; }
            Check(mirroredGrid != null, "Settings page 1 exposes mirrored 地块网格 toggle");
            if (mirroredGrid != null)
            {
                mirroredGrid.value = !before;
                Check(gridToggle.value == !before, "Mirrored 地块网格 toggle drives backing ORBIS gridToggle live");
                mirroredGrid.value = before;
            }
        }

        static void VerifyDragAndCollapseAll(FlatWorldApp app)
        {
            var panels = Get<List<HudFloatingPanel>>(app, "hudFloatingPanels");
            var byId = Get<Dictionary<string, HudFloatingPanel>>(app, "hudPanelsById");
            Check(panels.Count >= 10, $"All HUD floating panels registered (count={panels.Count})");

            // Ensure brush & pop are also logically visible during testing
            Call(app, "SelectHudMode", 0);
            Call(app, "ShowHudCategory", 0);
            Call(app, "TickEmpireHud");

            // 1. Test expanded drag on ecFeed and ecLegend (explicit user screenshot panels!)
            var feed = byId["feed"];
            var legend = byId["legend"];
            var info = byId["info"];
            var pop = byId["pop"];
            var minimap = byId["minimap"];

            Vector2 feedTarget = new Vector2(460f, 520f);
            Vector2 legendTarget = new Vector2(460f, 340f);
            feed.MoveTo(feedTarget, save: false);
            legend.MoveTo(legendTarget, save: false);
            Check(feed.HasCustomPosition && Vector2.Distance(feed.CustomPosition, feedTarget) < 2f,
                $"Expanded ecFeed moved cleanly to {feed.CustomPosition}");
            Check(legend.HasCustomPosition && Vector2.Distance(legend.CustomPosition, legendTarget) < 2f,
                $"Expanded ecLegend moved cleanly to {legend.CustomPosition}");

            // 2. Collapse ecInfo, ecPop, minimap, and ScrollWindow -> verify pills appear & can be dragged while collapsed!
            info.SetCollapsed(true, save: false);
            Check(info.IsCollapsed && info.Panel.ClassListContains("ec-hidden") && !info.Pill.ClassListContains("ec-hidden"),
                "ecInfo collapsed cleanly into thumbnail pill");

            Vector2 infoPillDragged = new Vector2(1180f, 220f);
            info.MoveTo(infoPillDragged, save: false);
            Check(info.IsCollapsed && Vector2.Distance(info.CustomPosition, infoPillDragged) < 2f,
                $"Collapsed ecInfo pill dragged to {info.CustomPosition} while staying collapsed");

            // Expand info pill and verify it opens near dragged pill position (clamped inside viewport)
            info.SetCollapsed(false, save: false);
            Check(!info.IsCollapsed && info.HasCustomPosition && info.CustomPosition.y >= 200f,
                $"Expanding dragged ecInfo pill preserved dragged position {info.CustomPosition}");
            // Collapse again for visual screenshot
            info.SetCollapsed(true, save: false);

            pop.SetCollapsed(true, save: false);
            pop.MoveTo(new Vector2(210f, 120f), save: false);
            Check(pop.IsCollapsed && !pop.Pill.ClassListContains("ec-hidden"),
                $"Collapsed ecPop pill visible and dragged to {pop.CustomPosition}");

            minimap.SetCollapsed(true, save: false);
            minimap.MoveTo(new Vector2(1240f, 820f), save: false);
            var mmObj = typeof(FlatWorldApp).GetField("minimap", Private)?.GetValue(app);
            bool mmCollapsed = mmObj != null && (bool)mmObj.GetType().GetProperty("Collapsed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(mmObj);
            Check(minimap.IsCollapsed && mmCollapsed && !minimap.Pill.ClassListContains("ec-hidden"),
                $"Minimap collapsed cleanly into draggable pill at {minimap.CustomPosition}");

            // 3. Test ScrollWindow collapse into draggable pill & transparent non-blocking backdrop!
            var win = Get<ScrollWindow>(app, "hudWindow");
            var winLayer = Get<VisualElement>(app, "hudWindows");
            win.SetCollapsed(true);
            win.Floating.MoveTo(new Vector2(620f, 92f), save: false);
            Check(win.IsCollapsed && winLayer.ClassListContains("ec-window-collapsed") && winLayer.pickingMode == PickingMode.Ignore,
                "Collapsed ScrollWindow releases modal backdrop so map stays interactive");
            Check(!win.CollapsePill.ClassListContains("ec-hidden") && Vector2.Distance(win.Floating.CustomPosition, new Vector2(620f, 92f)) < 2f,
                $"Collapsed ScrollWindow pill dragged to {win.Floating.CustomPosition}");

            // Verify clicking 设置 button while world window is collapsed re-expands it cleanly, then re-collapse for screenshot
            Call(app, "OpenHudWorld", 0);
            Check(!win.IsCollapsed && winLayer.pickingMode == PickingMode.Position,
                "Clicking 设置 button while collapsed re-expands 世界设置 window");
            win.SetCollapsed(true);
            win.Floating.MoveTo(new Vector2(620f, 92f), save: false);

            // Also verify EVERY remaining panel (era, modes, views, rail, brush) supports collapse & pill drag cleanly
            Call(app, "SelectHudMode", 1); // show brush
            Call(app, "TickEmpireHud");
            foreach (string id in new[] { "era", "modes", "views", "rail", "brush" })
            {
                var p = byId[id];
                p.SetCollapsed(true, save: false);
                bool collapsedOk = p.IsCollapsed && p.Panel.ClassListContains("ec-hidden") && !p.Pill.ClassListContains("ec-hidden");
                p.MoveTo(new Vector2(300f, 180f), save: false);
                bool movedOk = p.IsCollapsed && p.HasCustomPosition;
                p.SetCollapsed(false, save: false);
                p.ResetLayout(save: false);
                Check(collapsedOk && movedOk && !p.IsCollapsed, $"Panel '{id}' collapse + pill drag + expand + reset passed");
            }
            Call(app, "SelectHudMode", 0);
            Call(app, "ShowHudCategory", 0);
            pop.SetCollapsed(true, save: false);
            pop.MoveTo(new Vector2(180f, 110f), save: false);
            Call(app, "TickEmpireHud");
        }

        static void VerifyAllCollapsedState(FlatWorldApp app)
        {
            var panels = Get<List<HudFloatingPanel>>(app, "hudFloatingPanels");
            int collapsedVisible = 0;
            foreach (var p in panels)
            {
                if (!p.IsLogicalVisible) continue;
                bool ok = p.IsCollapsed && p.Panel.ClassListContains("ec-hidden") && !p.Pill.ClassListContains("ec-hidden");
                if (ok) collapsedVisible++;
            }
            Check(collapsedVisible >= 8, $"All active HUD panels collapsed simultaneously into thumbnail pills (count={collapsedVisible})");
        }

        static void CaptureComposite(FlatWorldApp app, Camera camera, FlatScrollNavigator nav, string path)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                nav.SetViewport(new Rect(0, 0, Width, Height));
                nav.Tick(0, app.CalendarDay);
                nav.SyncMaterials();
                camera.Render();
                var world = ReadRT(rt);
                var ui = ReadRT(uiTarget);
                var a = world.GetPixels32();
                var b = ui.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                {
                    int k = 255 - b[i].a;
                    a[i] = new Color32(
                        (byte)Math.Min(255, b[i].r + a[i].r * k / 255),
                        (byte)Math.Min(255, b[i].g + a[i].g * k / 255),
                        (byte)Math.Min(255, b[i].b + a[i].b * k / 255), 255);
                }
                world.SetPixels32(a);
                world.Apply();
                File.WriteAllBytes(path, world.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(world);
                UnityEngine.Object.DestroyImmediate(ui);
                Debug.Log("[HudLayoutVal] Wrote " + path);
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static Texture2D ReadRT(RenderTexture src)
        {
            RenderTexture.active = src;
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            return tex;
        }

        static int PickLandCoastTile(WorldData w)
        {
            int best = -1; float flow = 0;
            for (int i = 0; i < w.Count; i++)
            {
                if (w.Water[i] != WaterKind.River || w.CoastDistance[i] > 2) continue;
                float lat = Math.Abs(w.Latitude(i));
                if (lat < 15 || lat > 50) continue;
                if (w.Runoff[i] > flow) { flow = w.Runoff[i]; best = i; }
            }
            return best < 0 ? w.Count / 2 : best;
        }

        static T Get<T>(object o, string name) where T : class => o.GetType().GetField(name, Private)?.GetValue(o) as T;
        static void Set(object o, string name, object value) => o.GetType().GetField(name, Private)?.SetValue(o, value);
        static void Call(object o, string name, params object[] args)
        {
            var m = o.GetType().GetMethod(name, Private);
            if (m == null) throw new MissingMethodException(o.GetType().Name, name);
            m.Invoke(o, args);
        }

        static void Finish(string outDir, int code)
        {
            EditorApplication.update -= Step;
            if (hookInstalled) { Application.logMessageReceived -= OnLog; hookInstalled = false; }
            SessionState.EraseString(Flag);
            lines.Add($"SUMMARY failures={failures} unityErrors={unityErrors}");
            lines.Add(code == 0 ? "RESULT: PASS" : $"RESULT: FAIL (code={code}, failures={failures})");
            File.WriteAllLines(Path.Combine(outDir, "report.txt"), lines, Encoding.UTF8);
            Debug.Log("[HudLayoutVal] " + lines[lines.Count - 1]);
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }
}
