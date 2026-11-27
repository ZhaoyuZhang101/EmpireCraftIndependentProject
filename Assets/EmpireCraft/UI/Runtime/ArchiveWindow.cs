using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace EmpireCraft.UI
{
    // What the HUD needs of an open wide window: its root, a live refresh, and a page by index.
    public interface IHudWindow
    {
        VisualElement Root { get; }
        bool CurrentIsLive { get; }
        void Refresh();
        void Show(int page);
    }

    // 卷册 archive window, after the mod's wide windows: a fixed left column (identity card,
    // people, key figures, then the page menu and filters) beside a scrolling page. Both
    // columns are rebuilt from the window's state on every Go / Refresh, so the owner keeps
    // its selection (realm, war, house, filters) in its own fields and just calls Refresh().
    public sealed class ArchiveWindow : IHudWindow
    {
        public VisualElement Root { get; }
        public bool CurrentIsLive => true;
        public string Page { get; private set; }
        // Page keys in menu order, so Show(int) can address them (review captures use it).
        public readonly List<string> Pages = new List<string>();
        public Action<ArchiveWindow, VisualElement> Rail, Main;

        readonly ScrollView rail, main;
        readonly Label title;
        readonly Stack<string> history = new Stack<string>();
        readonly Action close;

        public ArchiveWindow(string title, string icon, Action close, params string[] pages)
        {
            this.close = close;
            Pages.AddRange(pages);
            Page = pages.Length > 0 ? pages[0] : null;
            Root = new VisualElement();
            Root.AddToClassList("ec-frame");
            Root.AddToClassList("ec-win");
            Root.AddToClassList("ec-arc");
            var bar = new VisualElement();
            bar.AddToClassList("ec-win-tb");
            bar.Add(Hud.Icon(icon));
            this.title = Hud.Text(title, "ec-win-title");
            bar.Add(this.title);
            bar.Add(Hud.Button("ec-win-back", null, "‹ 返回", Back));
            bar.Add(Hud.Button("ec-win-close", null, "×", close));
            Root.Add(bar);
            var body = new VisualElement();
            body.AddToClassList("ec-win-body");
            rail = new ScrollView(ScrollViewMode.Vertical);
            rail.AddToClassList("ec-arc-rail");
            main = new ScrollView(ScrollViewMode.Vertical);
            main.AddToClassList("ec-win-main");
            rail.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            main.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(rail);
            body.Add(main);
            Root.Add(body);
        }

        public void SetTitle(string text) { if (title.text != text) title.text = text; }

        public void Go(string page)
        {
            if (page != Page) { if (Page != null) history.Push(Page); Page = page; main.scrollOffset = Vector2.zero; }
            Rebuild();
        }

        public void Show(int page) { if (page >= 0 && page < Pages.Count) Go(Pages[page]); else Rebuild(); }

        public void Back()
        {
            if (history.Count == 0) { close(); return; }
            Page = history.Pop();
            Rebuild();
        }

        public void Refresh()
        {
            Vector2 a = main.scrollOffset, b = rail.scrollOffset;
            Rebuild();
            main.scrollOffset = a; rail.scrollOffset = b;
        }

        void Rebuild()
        {
            rail.Clear(); main.Clear();
            Fill(Rail, rail.contentContainer);
            Fill(Main, main.contentContainer);
        }

        void Fill(Action<ArchiveWindow, VisualElement> build, VisualElement into)
        {
            if (build == null) return;
            try { build(this, into); }
            catch (Exception e)
            {
                into.Add(Hud.Text("此处暂时无法显示：" + e.Message, "ec-empty"));
                Debug.LogException(e);
            }
        }
    }

    // Parts of the archive windows (the mod's WideKit, in UI Toolkit). Colour classes for
    // figures: g green (good), r red (bad), c cyan (information), y gold (key), o orange
    // (cost/warning), m grey (muted).
    public static class Arc
    {
        public static Label Text(string text, string cls, string tone = null)
        {
            var l = Hud.Text(text, cls);
            if (tone != null) l.AddToClassList("ec-t-" + tone);
            return l;
        }

        public static Label Tag(string text, string tone = null)
        {
            var l = Text(text, "ec-arc-tag", tone);
            return l;
        }

        // ---------- left column ----------
        // The identity card at the top of the archive: flag, name, two lines.
        public static VisualElement IdCard(string name, string line1, string line2 = null, uint? flag = null, string flagText = null, Label tag = null)
        {
            var card = Hud.Card(null, null, null, true);
            card.AddToClassList("ec-arc-id");
            var top = new VisualElement(); top.AddToClassList("ec-arc-id-top");
            if (flag.HasValue)
            {
                var f = new VisualElement(); f.AddToClassList("ec-arc-flag");
                f.style.backgroundColor = Hud.Rgb(flag.Value);
                if (!string.IsNullOrEmpty(flagText)) f.Add(Hud.Text(flagText, "ec-arc-flag-t"));
                top.Add(f);
            }
            top.Add(Hud.Text(name, "ec-arc-id-name"));
            if (tag != null) top.Add(tag);
            card.Add(top);
            if (!string.IsNullOrEmpty(line1)) card.Add(Hud.Text(line1, "ec-arc-id-l1"));
            if (!string.IsNullOrEmpty(line2)) card.Add(Hud.Text(line2, "ec-arc-id-l2"));
            return card;
        }

        // A person: framed avatar icon, then role, name and a note.
        public static VisualElement Person(string icon, string role, string name, string note, Action click = null, string nameTone = null)
        {
            var row = new VisualElement(); row.AddToClassList("ec-arc-person");
            if (click != null) { row.AddToClassList("ec-click"); row.AddManipulator(new Clickable(click)); }
            var ava = new VisualElement(); ava.AddToClassList("ec-arc-ava");
            ava.Add(Hud.Icon(icon, "ec-sm"));
            row.Add(ava);
            var col = new VisualElement(); col.AddToClassList("ec-arc-pcol");
            if (!string.IsNullOrEmpty(role)) col.Add(Hud.Text(role, "ec-arc-role"));
            col.Add(Text(name, "ec-arc-pname", nameTone));
            if (!string.IsNullOrEmpty(note)) col.Add(Hud.Text(note, "ec-arc-pnote"));
            row.Add(col);
            return row;
        }

        public static VisualElement Card(string title = null, string icon = null, string note = null)
        {
            var card = Hud.Card(title, icon, note);
            card.AddToClassList("ec-arc-card");
            return card;
        }

        // Key figures: key on the left, value (optionally coloured) on the right.
        public static VisualElement Kv(string key, string value, int index, string tone = null)
        {
            var row = Hud.Row(key, value, index % 2 == 1);
            if (tone != null) row[1].AddToClassList("ec-t-" + tone);
            return row;
        }

        public static VisualElement MenuHead(string text) => Hud.Text(text, "ec-arc-mhead");

        public static VisualElement MenuItem(string icon, string text, string count, bool active, Action click, string tone = null)
        {
            var row = new VisualElement(); row.AddToClassList("ec-arc-mi");
            if (active) row.AddToClassList("ec-active");
            if (click != null) { row.AddToClassList("ec-click"); row.AddManipulator(new Clickable(click)); }
            if (icon != null) row.Add(Hud.Icon(icon, "ec-sm"));
            row.Add(Text(text, "ec-label", tone));
            if (!string.IsNullOrEmpty(count)) row.Add(Hud.Text(count, "ec-opt-n"));
            return row;
        }

        // ---------- page ----------
        public static VisualElement Head(string title, string note = null, params VisualElement[] chips)
        {
            var hd = new VisualElement(); hd.AddToClassList("ec-hd");
            hd.Add(Hud.Text(title, "ec-hd-title"));
            var right = new VisualElement(); right.AddToClassList("ec-arc-chips");
            if (!string.IsNullOrEmpty(note)) right.Add(Hud.Text(note, "ec-arc-hnote"));
            foreach (var chip in chips) right.Add(chip);
            hd.Add(right);
            return hd;
        }

        public static VisualElement Chip(string text, bool on, Action click = null)
        {
            var chip = new VisualElement(); chip.AddToClassList("ec-arc-chip");
            if (on) chip.AddToClassList("ec-on");
            if (click != null) { chip.AddToClassList("ec-click"); chip.AddManipulator(new Clickable(click)); }
            chip.Add(Hud.Text(text, "ec-label"));
            return chip;
        }

        // Headline figures, each with its own colour.
        public static VisualElement Kpis(params (string label, string value, string tone)[] items)
        {
            var row = new VisualElement(); row.AddToClassList("ec-kpis");
            foreach (var (label, value, tone) in items)
            {
                var k = new VisualElement(); k.AddToClassList("ec-frame"); k.AddToClassList("ec-kpi");
                k.Add(Hud.Text(label, "ec-kpi-l"));
                k.Add(Text(value, "ec-kpi-n", tone));
                row.Add(k);
            }
            return row;
        }

        // A row of equal columns; cards added to it share the width.
        public static VisualElement Cols()
        {
            var row = new VisualElement(); row.AddToClassList("ec-arc-cols");
            return row;
        }

        // A labelled bar: label, track filled to the fraction, value.
        public static VisualElement Bar(string label, float fraction, string value, string tone)
        {
            var row = new VisualElement(); row.AddToClassList("ec-arc-bar");
            row.Add(Hud.Text(label, "ec-arc-bar-l"));
            var track = new VisualElement(); track.AddToClassList("ec-arc-track");
            var fill = new VisualElement(); fill.AddToClassList("ec-arc-fill"); fill.AddToClassList("ec-f-" + tone);
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            track.Add(fill);
            row.Add(track);
            row.Add(Text(value, "ec-arc-bar-v", tone));
            return row;
        }

        // A band naming a group of cards (a people, a side).
        public static VisualElement Group(string text, string count, string tone = "c")
        {
            var band = new VisualElement(); band.AddToClassList("ec-arc-group");
            band.Add(Text("◈ " + text, "ec-arc-group-t", tone));
            if (count != null) band.Add(Hud.Text(count, "ec-arc-group-n"));
            return band;
        }

        // A plain line in a card: left text, filler, right-hand cells.
        public static VisualElement Line(int index, Action click, params VisualElement[] cells)
        {
            var row = new VisualElement(); row.AddToClassList("ec-arc-line");
            if (index % 2 == 1) row.AddToClassList("ec-alt");
            if (click != null) { row.AddToClassList("ec-click"); row.AddManipulator(new Clickable(click)); }
            foreach (var c in cells) row.Add(c);
            return row;
        }

        public static VisualElement Fill() { var f = new VisualElement(); f.AddToClassList("ec-arc-grow"); return f; }

        public static VisualElement Swatch(uint rgb)
        {
            var s = new VisualElement { pickingMode = PickingMode.Ignore };
            s.AddToClassList("ec-swatch"); s.style.backgroundColor = Hud.Rgb(rgb);
            return s;
        }

        // A realm card: colour strip, name and regime, a person, four figures under it.
        public static VisualElement RealmCard(uint colour, string name, string note, VisualElement person,
                                              (string label, string value, string tone)[] stats, Action click, bool active = false)
        {
            var card = Card();
            card.AddToClassList("ec-arc-realm");
            if (active) card.AddToClassList("ec-sel");
            if (click != null) { card.AddToClassList("ec-click"); card.AddManipulator(new Clickable(click)); }
            var strip = new VisualElement(); strip.AddToClassList("ec-arc-strip"); strip.style.backgroundColor = Hud.Rgb(colour);
            card.Add(strip);
            var hd = new VisualElement(); hd.AddToClassList("ec-arc-rhead");
            var nm = Hud.Text(name, "ec-arc-rname"); nm.style.color = Color.Lerp(Hud.Rgb(colour), Color.white, .45f);
            hd.Add(nm);
            hd.Add(Fill());
            if (note != null) hd.Add(Hud.Text(note, "ec-arc-rnote"));
            card.Add(hd);
            if (person != null) card.Add(person);
            card.Add(Stats(stats));
            return card;
        }

        public static VisualElement Stats((string label, string value, string tone)[] stats)
        {
            var grid = new VisualElement(); grid.AddToClassList("ec-arc-stats");
            foreach (var (label, value, tone) in stats)
            {
                var cell = new VisualElement(); cell.AddToClassList("ec-arc-stat");
                cell.Add(Hud.Text(label, "ec-arc-stat-l"));
                cell.Add(Text(value, "ec-arc-stat-v", tone));
                grid.Add(cell);
            }
            return grid;
        }

        // Chronicle: a year heading, then month + text lines.
        public static VisualElement Year(string text) => Hud.Text(text, "ec-arc-year");

        public static VisualElement Event(string month, string text, string tone, Action click = null)
        {
            var row = new VisualElement(); row.AddToClassList("ec-arc-ev");
            if (click != null) { row.AddToClassList("ec-click"); row.AddManipulator(new Clickable(click)); }
            row.Add(Hud.Text(month, "ec-arc-ev-m"));
            row.Add(Text(text, "ec-arc-ev-t", tone));
            return row;
        }

        // Small person tile for kin grids.
        public static VisualElement Tile(string icon, string name, string note, Action click = null, bool active = false)
        {
            var tile = new VisualElement(); tile.AddToClassList("ec-arc-tile");
            if (active) tile.AddToClassList("ec-active");
            if (click != null) { tile.AddToClassList("ec-click"); tile.AddManipulator(new Clickable(click)); }
            var ava = new VisualElement(); ava.AddToClassList("ec-arc-ava"); ava.AddToClassList("ec-arc-ava-sm");
            ava.Add(Hud.Icon(icon, "ec-sm"));
            tile.Add(ava);
            var col = new VisualElement(); col.AddToClassList("ec-arc-pcol");
            col.Add(Hud.Text(name, "ec-arc-tname"));
            if (!string.IsNullOrEmpty(note)) col.Add(Hud.Text(note, "ec-arc-pnote"));
            tile.Add(col);
            return tile;
        }

        public static VisualElement Tiles()
        {
            var grid = new VisualElement(); grid.AddToClassList("ec-arc-tiles");
            return grid;
        }

        public static VisualElement Note(string text) => Hud.Text(text, "ec-hint");

        public static VisualElement Empty(string text)
        {
            var card = Card();
            card.Add(Hud.Text(text, "ec-empty"));
            return card;
        }

        // 12,345 → 1.2万 above ten thousand.
        public static string Count(double n) => n >= 100000000 ? (n / 100000000).ToString("0.##") + "亿" : n >= 10000 ? (n / 10000).ToString("0.#") + "万" : n.ToString("N0");
    }
}
