using System;
using System.Collections.Generic;
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
        public bool Opener;
    }

    public sealed class HudCategory
    {
        public string Name, Icon, Note;
        public readonly List<HudTool> Tools = new List<HudTool>();
        public bool AnyPorted { get { foreach (var t in Tools) if (t.Run != null) return true; return false; } }
    }

    public sealed class HudMode
    {
        public string Name, Icon;
        public bool Brush, Legend;
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
    }

    // 卷册 wide window: title bar with 返回 / ×, a page rail on the left and a scrolling
    // main column. Pages build their content on demand; live pages are rebuilt by Refresh().
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
        readonly VisualElement rail;
        readonly ScrollView main;
        readonly List<Page> pages = new List<Page>();
        readonly Stack<int> history = new Stack<int>();
        readonly Action close;
        int current = -1;

        public ScrollWindow(string title, string icon, Action close)
        {
            this.close = close;
            Root = new VisualElement();
            Root.AddToClassList("ec-frame");
            Root.AddToClassList("ec-win");
            var bar = new VisualElement();
            bar.AddToClassList("ec-win-tb");
            bar.Add(Hud.Icon(icon));
            bar.Add(Hud.Text(title, "ec-win-title"));
            bar.Add(Hud.Button("ec-win-back", null, "‹ 返回", Back));
            bar.Add(Hud.Button("ec-win-close", null, "×", close));
            Root.Add(bar);
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

        public ScrollWindow Add(string name, string icon, Action<VisualElement> build, bool live = false, Func<string> count = null)
        {
            pages.Add(new Page { Name = name, Icon = icon, Build = build, Live = live, Count = count });
            return this;
        }

        public bool CurrentIsLive { get { return current >= 0 && pages[current].Live; } }

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
