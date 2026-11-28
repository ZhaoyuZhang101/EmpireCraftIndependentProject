using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace EmpireCraft.UI
{
    // One entry in a popout tool grid. Run == null means the system behind it has not been
    // ported yet: the tile stays visible (greyed) so the final layout is already in place.
    public sealed class HudTool
    {
        public string Name, Icon, Hint;
        public Action Run;
        public Func<bool> IsOn;
        public bool Opener, StayOpen;
        public int ToolIndex;
    }

    public sealed class HudCategory
    {
        public string Name, Icon, Note;
        public readonly List<HudTool> Tools = new List<HudTool>();
        public bool AnyPorted { get { foreach (var t in Tools) if (t.Run != null) return true; return false; } }
    }

    public sealed class HudMode
    {
        public string Name, Icon, RailTitle;
        public bool Brush, Legend, DirectRail;
        public readonly List<HudCategory> Categories = new List<HudCategory>();
    }

    // Element factory for the pixel-frame HUD. All clickable things are plain VisualElements
    // with a Clickable, so the ORBIS Button theme never leaks into the frames.
    public static class Hud
    {
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        public static Texture2D IconTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!icons.TryGetValue(name, out var texture))
            {
                texture = Resources.Load<Texture2D>("EmpireCraft/Icons/" + name);
                if (!texture) Debug.LogWarning("EmpireCraft HUD icon missing: " + name);
                icons[name] = texture;
            }
            return texture;
        }

        public static VisualElement Icon(string name, string size = null)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("ec-icon");
            if (size != null) e.AddToClassList(size);
            SetIcon(e, name);
            return e;
        }

        public static void SetIcon(VisualElement e, string name)
        {
            var texture = IconTexture(name);
            e.style.backgroundImage = texture ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);
        }

        public static Label Text(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            if (cls != null) l.AddToClassList(cls);
            return l;
        }

        public static VisualElement Button(string cls, string icon, string text, Action click, string iconSize = null)
        {
            var b = new VisualElement();
            b.AddToClassList("ec-frame");
            b.AddToClassList("ec-click");
            if (cls != null) b.AddToClassList(cls);
            if (icon != null) b.Add(Icon(icon, iconSize));
            if (text != null) b.Add(Text(text, "ec-label"));
            if (click != null) b.AddManipulator(new Clickable(click));
            return b;
        }

        public static VisualElement MiniButton(string text, string tooltip, Action click, string extraClass = null)
        {
            var b = new VisualElement { tooltip = tooltip ?? "" };
            b.AddToClassList("ec-click");
            b.AddToClassList("ec-min-btn");
            if (!string.IsNullOrEmpty(extraClass)) b.AddToClassList(extraClass);
            b.Add(Text(text, "ec-min-btn-text"));
            // Stop pointer-down from initiating parent header drags.
            b.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) e.StopPropagation(); });
            if (click != null) b.AddManipulator(new Clickable(click));
            return b;
        }

        public static VisualElement DragGrip(string tooltip = "按住拖动面板")
        {
            var g = new VisualElement { tooltip = tooltip ?? "" };
            g.AddToClassList("ec-grip");
            g.Add(Text("⋮⋮", "ec-grip-dots"));
            return g;
        }

        public static void SetText(VisualElement button, string text)
        {
            var l = button.Q<Label>(className: "ec-label");
            if (l != null && l.text != text) l.text = text;
        }

        // on = dominant frame (current mode/category), sel = select frame (active tool or view),
        // off = not ported yet.
        public static void State(VisualElement e, bool on, bool sel = false, bool off = false)
        {
            e.EnableInClassList("ec-on", on);
            e.EnableInClassList("ec-sel", sel && !on);
            e.EnableInClassList("ec-off", off);
        }

        public static VisualElement Row(string key, string value, bool alt)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("ec-row");
            if (alt) row.AddToClassList("ec-alt");
            row.Add(Text(key, "ec-row-key"));
            row.Add(Text(value, "ec-row-val"));
            return row;
        }

        public static VisualElement Card(string title, string icon = null, string note = null, bool dominant = false)
        {
            var card = new VisualElement();
            card.AddToClassList("ec-frame");
            card.AddToClassList("ec-card");
            if (dominant) card.AddToClassList("ec-on");
            if (title != null)
            {
                var head = new VisualElement { pickingMode = PickingMode.Ignore };
                head.AddToClassList("ec-ch");
                if (icon != null) head.Add(Icon(icon, "ec-sm"));
                head.Add(Text(title, "ec-ch-title"));
                if (note != null) head.Add(Text(note, "ec-ch-note"));
                card.Add(head);
            }
            return card;
        }

        public static VisualElement Kpis(params (string label, string value)[] items)
        {
            var row = new VisualElement();
            row.AddToClassList("ec-kpis");
            foreach (var (label, value) in items)
            {
                var k = new VisualElement();
                k.AddToClassList("ec-frame");
                k.AddToClassList("ec-kpi");
                k.Add(Text(label, "ec-kpi-l"));
                k.Add(Text(value, "ec-kpi-n"));
                row.Add(k);
            }
            return row;
        }

        public static VisualElement Header(string title, string note = null)
        {
            var hd = new VisualElement();
            hd.AddToClassList("ec-hd");
            hd.Add(Text(title, "ec-hd-title"));
            if (note != null) hd.Add(Text(note, "ec-hd-note"));
            return hd;
        }

        public static Color Rgb(uint rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        }

        internal static bool IsInteractiveElement(VisualElement target, VisualElement stopAt)
        {
            for (var cur = target; cur != null && cur != stopAt; cur = cur.parent)
            {
                if (cur.ClassListContains("ec-click") || cur.ClassListContains("ec-min-btn")) return true;
                if (cur is Button || cur is Toggle || cur is TextField || cur is DropdownField ||
                    cur is Slider || cur is SliderInt || cur is Scroller || cur is RepeatButton)
                    return true;
            }
            return false;
        }

        internal static Vector2 VisualLocalPosition(VisualElement el, VisualElement container)
        {
            if (el == null || container == null) return Vector2.zero;
            var cw = container.worldBound;
            var ew = el.worldBound;
            if (!float.IsNaN(ew.x) && !float.IsNaN(cw.x) && ew.width > 0.5f && ew.height > 0.5f)
                return new Vector2(ew.x - cw.x, ew.y - cw.y);
            var l = el.layout;
            if (!float.IsNaN(l.x) && !float.IsNaN(l.y)) return new Vector2(l.x, l.y);
            return Vector2.zero;
        }

        internal static Vector2 ClampToContainer(Vector2 pos, Vector2 size, Vector2 containerSize, float margin = 4f)
        {
            if (containerSize.x <= 1f || containerSize.y <= 1f) return pos;
            float maxX = Mathf.Max(margin, containerSize.x - Mathf.Max(24f, size.x) - margin);
            float maxY = Mathf.Max(margin, containerSize.y - Mathf.Max(24f, size.y) - margin);
            return new Vector2(Mathf.Clamp(pos.x, margin, maxX), Mathf.Clamp(pos.y, margin, maxY));
        }

        internal static void ApplyAbsolutePosition(VisualElement el, Vector2 pos)
        {
            el.style.right = StyleKeyword.Auto;
            el.style.bottom = StyleKeyword.Auto;
            el.style.translate = new StyleTranslate(new Translate(0, 0, 0));
            el.style.left = pos.x;
            el.style.top = pos.y;
        }

        internal static void ClearAbsolutePosition(VisualElement el)
        {
            el.style.left = StyleKeyword.Null;
            el.style.top = StyleKeyword.Null;
            el.style.right = StyleKeyword.Null;
            el.style.bottom = StyleKeyword.Null;
            el.style.translate = StyleKeyword.Null;
        }
    }

    // Reusable controller turning any HUD panel or toolbar into a draggable, collapsible window
    // whose collapsed thumbnail pill is also independently draggable and restores on click.
    public sealed class HudFloatingPanel
    {
        const string PrefPrefix = "EmpireCraft.HudPanel.";
        const float DragThresholdSq = 25f; // 5 px

        public readonly string Id;
        public readonly VisualElement Panel;
        public readonly VisualElement Container;
        public readonly VisualElement Pill;
        readonly VisualElement pillIconEl;
        readonly Label pillLabelEl;
        readonly bool rightAligned, bottomAligned, persist;
        readonly Action<bool> onCollapsedChanged;

        bool logicalVisible = true;
        bool collapsed;
        bool hasCustomPos;
        Vector2 customPos;
        Vector2 lastPanelRectSize = new Vector2(240, 120);
        Vector2 lastPillRectSize = new Vector2(108, 34);

        // Expanded drag state
        int panelDragPointer = -1;
        bool panelDragged;
        Vector2 panelDownPointer, panelStartPos;

        // Pill drag/click state
        int pillPointer = -1;
        bool pillDragged;
        Vector2 pillDownPointer, pillStartPos;

        public bool IsCollapsed => collapsed;
        public bool IsLogicalVisible => logicalVisible;
        public bool HasCustomPosition => hasCustomPos;
        public Vector2 CustomPosition => customPos;

        public HudFloatingPanel(
            string id,
            VisualElement panel,
            VisualElement container,
            VisualElement dragHandle,
            string pillIcon,
            string pillText,
            bool rightAligned = false,
            bool bottomAligned = false,
            bool dragOnPanelSurface = false,
            bool persist = true,
            Action<bool> onCollapsedChanged = null)
        {
            Id = id;
            Panel = panel;
            Container = container;
            this.rightAligned = rightAligned;
            this.bottomAligned = bottomAligned;
            this.persist = persist;
            this.onCollapsedChanged = onCollapsedChanged;

            Panel.pickingMode = PickingMode.Position;
            Panel.RegisterCallback<PointerDownEvent>(OnPanelRaise, TrickleDown.TrickleDown);
            Panel.RegisterCallback<WheelEvent>(e => e.StopPropagation());
            Panel.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.width > 1f && e.newRect.height > 1f)
                {
                    lastPanelRectSize = e.newRect.size;
                    if (hasCustomPos && !collapsed && logicalVisible) ClampActiveToContainer();
                }
            });

            if (dragHandle != null)
            {
                dragHandle.pickingMode = PickingMode.Position;
                dragHandle.AddToClassList("ec-drag-handle");
                BindExpandedDrag(dragHandle);
            }
            if (dragOnPanelSurface && dragHandle != Panel)
            {
                BindExpandedDrag(Panel);
            }

            // Build the compact draggable collapsed thumbnail pill.
            Pill = new VisualElement { name = "ecPill_" + id, tooltip = "点击展开「" + pillText + "」，按住可拖动位置" };
            Pill.AddToClassList("ec-frame");
            Pill.AddToClassList("ec-pill");
            Pill.AddToClassList("ec-hidden");
            pillIconEl = Hud.Icon(pillIcon, "ec-sm");
            pillIconEl.EnableInClassList("ec-hidden", string.IsNullOrEmpty(pillIcon));
            pillLabelEl = Hud.Text(pillText, "ec-pill-label");
            var plus = Hud.Text("+", "ec-pill-plus");
            Pill.Add(pillIconEl);
            Pill.Add(pillLabelEl);
            Pill.Add(plus);
            Pill.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.width > 1f && e.newRect.height > 1f)
                {
                    lastPillRectSize = e.newRect.size;
                    if (collapsed && logicalVisible) ClampActiveToContainer();
                }
            });
            Pill.RegisterCallback<WheelEvent>(e => e.StopPropagation());
            BindPillDragAndClick();
            Container.Add(Pill);

            Container.RegisterCallback<GeometryChangedEvent>(_ => ClampActiveToContainer());

            if (persist) LoadState();
            SyncDisplay();
        }

        public VisualElement CreateCollapseButton(string tooltip = "折叠为可拖动略缩按钮")
        {
            return Hud.MiniButton("−", tooltip, () => SetCollapsed(true, save: true));
        }

        public void SetPillContent(string icon, string text)
        {
            if (icon != null)
            {
                Hud.SetIcon(pillIconEl, icon);
                pillIconEl.EnableInClassList("ec-hidden", string.IsNullOrEmpty(icon));
            }
            if (!string.IsNullOrEmpty(text) && pillLabelEl.text != text)
            {
                pillLabelEl.text = text;
                Pill.tooltip = "点击展开「" + text + "」，按住可拖动位置";
            }
        }

        public void SetLogicalVisible(bool visible)
        {
            if (logicalVisible == visible) return;
            logicalVisible = visible;
            SyncDisplay();
            if (logicalVisible) ClampActiveToContainer();
        }

        public void SetCollapsed(bool value, bool save = true)
        {
            if (collapsed == value)
            {
                SyncDisplay();
                return;
            }
            if (value)
            {
                // Capture current visual location before hiding expanded panel.
                Vector2 panelPos = hasCustomPos ? customPos : Hud.VisualLocalPosition(Panel, Container);
                Vector2 pSize = Panel.worldBound.size;
                if (pSize.x > 1f && pSize.y > 1f) lastPanelRectSize = pSize;
                Vector2 pillPos = ComputeCollapsedPosition(panelPos, lastPanelRectSize, lastPillRectSize);
                if (hasCustomPos) pillPos = customPos;
                customPos = Hud.ClampToContainer(pillPos, lastPillRectSize, ContainerSize());
                Hud.ApplyAbsolutePosition(Pill, customPos);
            }
            collapsed = value;
            SyncDisplay();
            if (!collapsed)
            {
                if (hasCustomPos)
                {
                    customPos = Hud.ClampToContainer(customPos, lastPanelRectSize, ContainerSize());
                    Hud.ApplyAbsolutePosition(Panel, customPos);
                }
                else
                {
                    Hud.ClearAbsolutePosition(Panel);
                }
                if (logicalVisible) Panel.BringToFront();
            }
            else if (logicalVisible)
            {
                Pill.BringToFront();
            }
            onCollapsedChanged?.Invoke(collapsed);
            if (save && persist) SaveState();
        }

        public void MoveTo(Vector2 pos, bool save = true)
        {
            Vector2 activeSize = collapsed ? lastPillRectSize : lastPanelRectSize;
            customPos = Hud.ClampToContainer(pos, activeSize, ContainerSize());
            hasCustomPos = true;
            Hud.ApplyAbsolutePosition(collapsed ? Pill : Panel, customPos);
            if (save && persist) SaveState();
        }

        public void ResetLayout(bool save = true)
        {
            hasCustomPos = false;
            customPos = Vector2.zero;
            Hud.ClearAbsolutePosition(Panel);
            Hud.ClearAbsolutePosition(Pill);
            if (collapsed)
            {
                collapsed = false;
                onCollapsedChanged?.Invoke(false);
            }
            SyncDisplay();
            if (save && persist) EraseState();
        }

        public void ClampActiveToContainer()
        {
            Vector2 cSize = ContainerSize();
            if (cSize.x <= 1f || cSize.y <= 1f) return;
            if (collapsed)
            {
                Vector2 pSize = Pill.worldBound.size;
                if (pSize.x > 1f && pSize.y > 1f) lastPillRectSize = pSize;
                Vector2 current = hasCustomPos ? customPos : Hud.VisualLocalPosition(Pill, Container);
                Vector2 clamped = Hud.ClampToContainer(current, lastPillRectSize, cSize);
                if (hasCustomPos || (clamped - current).sqrMagnitude > 0.25f)
                {
                    customPos = clamped;
                    Hud.ApplyAbsolutePosition(Pill, clamped);
                }
            }
            else if (hasCustomPos)
            {
                Vector2 pSize = Panel.worldBound.size;
                if (pSize.x > 1f && pSize.y > 1f) lastPanelRectSize = pSize;
                Vector2 clamped = Hud.ClampToContainer(customPos, lastPanelRectSize, cSize);
                customPos = clamped;
                Hud.ApplyAbsolutePosition(Panel, clamped);
            }
        }

        Vector2 ComputeCollapsedPosition(Vector2 panelPos, Vector2 panelSize, Vector2 pillSize)
        {
            float x = rightAligned ? panelPos.x + Mathf.Max(0f, panelSize.x - pillSize.x) : panelPos.x;
            float y = bottomAligned ? panelPos.y + Mathf.Max(0f, panelSize.y - pillSize.y) : panelPos.y;
            return new Vector2(x, y);
        }

        Vector2 ContainerSize()
        {
            var r = Container.worldBound.size;
            if (r.x > 1f && r.y > 1f) return r;
            return Container.layout.size;
        }

        void SyncDisplay()
        {
            Panel.EnableInClassList("ec-hidden", !logicalVisible || collapsed);
            Pill.EnableInClassList("ec-hidden", !logicalVisible || !collapsed);
            if (logicalVisible && !collapsed && hasCustomPos)
                Hud.ApplyAbsolutePosition(Panel, Hud.ClampToContainer(customPos, lastPanelRectSize, ContainerSize()));
            if (logicalVisible && collapsed)
                Hud.ApplyAbsolutePosition(Pill, Hud.ClampToContainer(customPos, lastPillRectSize, ContainerSize()));
        }

        void OnPanelRaise(PointerDownEvent e)
        {
            if (logicalVisible && !collapsed) Panel.BringToFront();
        }

        void BindExpandedDrag(VisualElement target)
        {
            target.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || panelDragPointer >= 0) return;
                if (Hud.IsInteractiveElement(e.target as VisualElement, target)) return;
                if (e.clickCount >= 2 && hasCustomPos)
                {
                    ResetLayout(save: persist);
                    e.StopPropagation();
                    return;
                }
                panelDragPointer = e.pointerId;
                panelDragged = false;
                panelDownPointer = e.position;
                panelStartPos = Hud.VisualLocalPosition(Panel, Container);
                var size = Panel.worldBound.size;
                if (size.x > 1f && size.y > 1f) lastPanelRectSize = size;
                Panel.BringToFront();
                target.CapturePointer(panelDragPointer);
                e.StopPropagation();
            });
            target.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != panelDragPointer) return;
                Vector2 delta = (Vector2)e.position - panelDownPointer;
                if (!panelDragged && delta.sqrMagnitude > DragThresholdSq)
                {
                    panelDragged = true;
                    Panel.AddToClassList("ec-dragging");
                }
                if (panelDragged)
                {
                    Vector2 next = Hud.ClampToContainer(panelStartPos + delta, lastPanelRectSize, ContainerSize());
                    customPos = next;
                    hasCustomPos = true;
                    Hud.ApplyAbsolutePosition(Panel, next);
                }
                e.StopPropagation();
            });
            target.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != panelDragPointer) return;
                target.ReleasePointer(panelDragPointer);
                panelDragPointer = -1;
                Panel.RemoveFromClassList("ec-dragging");
                if (panelDragged && persist) SaveState();
                panelDragged = false;
                e.StopPropagation();
            });
            target.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                panelDragPointer = -1;
                panelDragged = false;
                Panel.RemoveFromClassList("ec-dragging");
            });
        }

        void BindPillDragAndClick()
        {
            Pill.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || pillPointer >= 0) return;
                pillPointer = e.pointerId;
                pillDragged = false;
                pillDownPointer = e.position;
                pillStartPos = Hud.VisualLocalPosition(Pill, Container);
                var size = Pill.worldBound.size;
                if (size.x > 1f && size.y > 1f) lastPillRectSize = size;
                Pill.BringToFront();
                Pill.CapturePointer(pillPointer);
                e.StopPropagation();
            });
            Pill.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != pillPointer) return;
                Vector2 delta = (Vector2)e.position - pillDownPointer;
                if (!pillDragged && delta.sqrMagnitude > DragThresholdSq)
                {
                    pillDragged = true;
                    Pill.AddToClassList("ec-dragging");
                }
                if (pillDragged)
                {
                    Vector2 next = Hud.ClampToContainer(pillStartPos + delta, lastPillRectSize, ContainerSize());
                    customPos = next;
                    hasCustomPos = true;
                    Hud.ApplyAbsolutePosition(Pill, next);
                }
                e.StopPropagation();
            });
            Pill.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != pillPointer) return;
                Pill.ReleasePointer(pillPointer);
                pillPointer = -1;
                Pill.RemoveFromClassList("ec-dragging");
                bool wasDragged = pillDragged;
                pillDragged = false;
                if (wasDragged)
                {
                    if (persist) SaveState();
                }
                else
                {
                    SetCollapsed(false, save: true);
                }
                e.StopPropagation();
            });
            Pill.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                pillPointer = -1;
                pillDragged = false;
                Pill.RemoveFromClassList("ec-dragging");
            });
        }

        void SaveState()
        {
            string key = PrefPrefix + Id;
            string payload = (collapsed ? "1" : "0") + "|" + (hasCustomPos ? "1" : "0") + "|" +
                customPos.x.ToString("F1", CultureInfo.InvariantCulture) + "|" +
                customPos.y.ToString("F1", CultureInfo.InvariantCulture);
            PlayerPrefs.SetString(key, payload);
        }

        void LoadState()
        {
            string raw = PlayerPrefs.GetString(PrefPrefix + Id, null);
            if (string.IsNullOrEmpty(raw)) return;
            var parts = raw.Split('|');
            if (parts.Length < 4) return;
            collapsed = parts[0] == "1";
            hasCustomPos = parts[1] == "1";
            if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                customPos = new Vector2(x, y);
            }
            else
            {
                hasCustomPos = false;
            }
        }

        void EraseState()
        {
            PlayerPrefs.DeleteKey(PrefPrefix + Id);
        }
    }

    // 卷册 wide window: draggable title bar with 返回 / − (collapse to draggable pill) / ×,
    // a page rail on the left and a scrolling main column.
    public sealed class ScrollWindow : IHudWindow
    {
        VisualElement IHudWindow.Root => Root;
        public sealed class Page
        {
            public string Name, Icon;
            public bool Live;
            public Func<string> Count;
            public Action<VisualElement> Build;
        }

        public readonly VisualElement Root;
        readonly VisualElement titleBar;
        readonly VisualElement rail;
        readonly ScrollView main;
        readonly List<Page> pages = new List<Page>();
        readonly Stack<int> history = new Stack<int>();
        readonly Action close;
        readonly string windowTitle, windowIcon;
        HudFloatingPanel floating;
        int current = -1;

        public bool IsCollapsed => floating != null && floating.IsCollapsed;
        public VisualElement CollapsePill => floating?.Pill;
        public HudFloatingPanel Floating => floating;

        public ScrollWindow(string title, string icon, Action close)
        {
            windowTitle = title;
            windowIcon = icon;
            this.close = close;
            Root = new VisualElement();
            Root.AddToClassList("ec-frame");
            Root.AddToClassList("ec-win");
            titleBar = new VisualElement { tooltip = "按住标题栏可拖动窗口" };
            titleBar.AddToClassList("ec-win-tb");
            titleBar.Add(Hud.Icon(icon));
            titleBar.Add(Hud.Text(title, "ec-win-title"));
            titleBar.Add(Hud.Button("ec-win-back", null, "‹ 返回", Back));
            titleBar.Add(Hud.Button("ec-win-min", null, "−", () => SetCollapsed(true)));
            titleBar.Add(Hud.Button("ec-win-close", null, "×", close));
            Root.Add(titleBar);
            var body = new VisualElement();
            body.AddToClassList("ec-win-body");
            rail = new VisualElement();
            rail.AddToClassList("ec-win-rail");
            main = new ScrollView(ScrollViewMode.Vertical);
            main.AddToClassList("ec-win-main");
            body.Add(rail);
            body.Add(main);
            Root.Add(body);
        }

        public void AttachFloating(VisualElement container, Action<bool> onCollapsedChanged = null)
        {
            if (floating != null) return;
            floating = new HudFloatingPanel(
                "window_" + windowTitle,
                Root,
                container,
                titleBar,
                windowIcon,
                windowTitle,
                rightAligned: false,
                bottomAligned: false,
                dragOnPanelSurface: false,
                persist: false,
                onCollapsedChanged: onCollapsedChanged);
        }

        public void SetCollapsed(bool collapsed)
        {
            floating?.SetCollapsed(collapsed, save: false);
        }

        public ScrollWindow Add(string name, string icon, Action<VisualElement> build, bool live = false, Func<string> count = null)
        {
            pages.Add(new Page { Name = name, Icon = icon, Build = build, Live = live, Count = count });
            return this;
        }

        public bool CurrentIsLive { get { return current >= 0 && pages[current].Live; } }
        public int PageCount => pages.Count;

        public void Show(int page)
        {
            if (page < 0 || page >= pages.Count) page = 0;
            if (current >= 0 && current != page) history.Push(current);
            current = page;
            Rebuild();
        }

        public void Back()
        {
            if (history.Count == 0) { close(); return; }
            current = history.Pop();
            Rebuild();
        }

        public void Refresh()
        {
            if (current < 0) return;
            float scroll = main.scrollOffset.y;
            Rebuild();
            main.scrollOffset = new Vector2(0, scroll);
        }

        void Rebuild()
        {
            rail.Clear();
            rail.EnableInClassList("ec-hidden", pages.Count < 2);
            for (int i = 0; i < pages.Count; i++)
            {
                int index = i;
                var p = pages[i];
                var opt = new VisualElement();
                opt.AddToClassList("ec-opt");
                opt.AddToClassList("ec-click");
                if (i % 2 == 0) opt.AddToClassList("ec-alt");
                if (i == current) opt.AddToClassList("ec-active");
                if (p.Icon != null) opt.Add(Hud.Icon(p.Icon));
                opt.Add(Hud.Text(p.Name, "ec-label"));
                string count = p.Count?.Invoke();
                if (!string.IsNullOrEmpty(count)) opt.Add(Hud.Text(count, "ec-opt-n"));
                opt.AddManipulator(new Clickable(() => { if (index != current) Show(index); }));
                rail.Add(opt);
            }
            main.Clear();
            try { pages[current].Build(main.contentContainer); }
            catch (Exception e)
            {
                main.Add(Hud.Text("此页暂时无法显示：" + e.Message, "ec-empty"));
                Debug.LogException(e);
            }
        }
    }
}
