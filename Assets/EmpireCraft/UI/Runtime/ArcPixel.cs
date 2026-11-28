using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace EmpireCraft.UI
{
    // The house components of docs/design/ui-style.md §5 (漆器朱 pixel style): kinds of card,
    // bevelled buttons, receipts, numbered steps, icon grids, tug bars, meters, flags, banners
    // and locked chips. Every window builds from these; the look lives in the lacquer theme
    // block of EmpireHudStyle.uss, so other themes still get plain framed versions.
    public static partial class Arc
    {
        public enum CardKind { Plain, Gold, Dim }
        public enum ButtonKind { Plain, Gold, Red, Off }
        public enum StepState { Done, Now, Todo }

        // ---------- cards ----------
        public static VisualElement Card(string title, string icon, string note, CardKind kind)
        {
            var card = Card(title, icon, note);
            SetKind(card, kind);
            return card;
        }

        public static void SetKind(VisualElement card, CardKind kind)
        {
            card.EnableInClassList("ec-on", kind == CardKind.Gold);
            card.EnableInClassList("ec-k-dim", kind == CardKind.Dim);
        }

        // Something on the right of a card's header line (a badge, chips).
        public static void HeadAdd(VisualElement card, VisualElement item)
        {
            var head = card.Q(className: "ec-ch");
            if (head == null) { card.Insert(0, item); return; }
            var note = head.Q(className: "ec-ch-note");
            if (note == null) { var fill = Fill(); head.Add(fill); }
            head.Add(item);
        }

        // A horizontal group; children share the width by their grow values.
        public static VisualElement Row(params VisualElement[] items)
        {
            var row = new VisualElement(); row.AddToClassList("ec-prow");
            foreach (var i in items) if (i != null) row.Add(i);
            return row;
        }

        public static VisualElement Column(float grow, params VisualElement[] items)
        {
            var col = new VisualElement(); col.AddToClassList("ec-pcol");
            col.style.flexGrow = grow; col.style.flexBasis = 0;
            foreach (var i in items) if (i != null) col.Add(i);
            return col;
        }

        public static T Grow<T>(T e, float grow) where T : VisualElement
        {
            e.style.flexGrow = grow; e.style.flexBasis = 0; e.style.flexShrink = 1;
            return e;
        }

        // ---------- page anatomy (v5: one question per page, equal grid, a verdict per card) ----------
        // Page head: the page name, a dotted leader, the one question the page answers.
        public static VisualElement PageHead(string title, string question)
        {
            var hd = new VisualElement(); hd.AddToClassList("ec-phead");
            hd.Add(Hud.Text(title, "ec-phead-t"));
            var lead = new VisualElement { pickingMode = PickingMode.Ignore }; lead.AddToClassList("ec-lead"); hd.Add(lead);
            if (!string.IsNullOrEmpty(question)) hd.Add(Hud.Text(question, "ec-phead-q"));
            return hd;
        }

        // Headline figures: equal tiles in one row; the key one gets the gold frame.
        public static VisualElement KpiRow(params (string label, string value, string tone, bool key)[] items)
        {
            var row = new VisualElement(); row.AddToClassList("ec-kpis");
            foreach (var (label, value, tone, key) in items)
            {
                var k = new VisualElement(); k.AddToClassList("ec-frame"); k.AddToClassList("ec-kpi");
                if (key) k.AddToClassList("ec-on");
                k.Add(Hud.Text(label, "ec-kpi-l"));
                k.Add(Text(value, "ec-kpi-n", tone));
                row.Add(k);
            }
            return row;
        }

        // A grid of equal content cards (two columns by default).
        public static VisualElement Grid(int columns = 2)
        {
            var g = new VisualElement(); g.AddToClassList("ec-pgrid");
            g.userData = columns;
            return g;
        }

        // Adds a card to a grid row by row, so every card in a row has the same width.
        public static void GridAdd(VisualElement grid, VisualElement card)
        {
            int columns = grid.userData is int c ? c : 2;
            VisualElement row = grid.childCount > 0 ? grid[grid.childCount - 1] : null;
            if (row == null || row.childCount >= columns) { row = new VisualElement(); row.AddToClassList("ec-pgrid-row"); grid.Add(row); }
            card.AddToClassList("ec-pgrid-cell");
            row.Add(card);
        }

        // An info card: icon and title, a dotted leader, status tags on the right.
        public static VisualElement InfoCard(string title, string icon, params Label[] tags)
        {
            var card = new VisualElement(); card.AddToClassList("ec-frame"); card.AddToClassList("ec-card"); card.AddToClassList("ec-icard");
            var head = new VisualElement { pickingMode = PickingMode.Ignore }; head.AddToClassList("ec-ch");
            if (icon != null) head.Add(Hud.Icon(icon, "ec-sm"));
            head.Add(Hud.Text(title, "ec-ch-title"));
            var lead = new VisualElement { pickingMode = PickingMode.Ignore }; lead.AddToClassList("ec-lead"); head.Add(lead);
            foreach (var t in tags) if (t != null) head.Add(t);
            card.Add(head);
            return card;
        }

        // The sentence at the foot of a card that says what the figures mean.
        public static Label Verdict(string text, string tone = "o") => Text(text, "ec-verdict", tone);

        // A dark block of rows (one frame level only): name · tag · right-aligned value or button.
        public static VisualElement ListBlock() { var b = new VisualElement(); b.AddToClassList("ec-list"); return b; }

        public static VisualElement ListRow(VisualElement list, string name, Label tag, string value, VisualElement button = null,
                                            Color? dot = null, string nameTone = null, Action click = null)
        {
            var row = new VisualElement(); row.AddToClassList("ec-li");
            if (list.childCount % 2 == 1) row.AddToClassList("ec-alt");
            if (click != null) { row.AddToClassList("ec-click"); row.AddManipulator(new Clickable(click)); }
            if (dot.HasValue) { var d = new VisualElement { pickingMode = PickingMode.Ignore }; d.AddToClassList("ec-dot"); d.style.backgroundColor = dot.Value; row.Add(d); }
            row.Add(Text(name, "ec-li-name", nameTone));
            if (tag != null) row.Add(tag);
            if (value != null) row.Add(Hud.Text(value, "ec-li-v"));
            if (button != null) row.Add(button);
            list.Add(row);
            return row;
        }

        // Column heads for a list (muted, right-aligned figures).
        public static VisualElement ListHead(VisualElement list, string name, params string[] columns)
        {
            var row = new VisualElement(); row.AddToClassList("ec-li"); row.AddToClassList("ec-li-head");
            row.Add(Text(name, "ec-li-name", "m"));
            foreach (var c in columns) row.Add(Text(c, "ec-li-col", "m"));
            list.Add(row);
            return row;
        }

        public static Label Col(string text) => Hud.Text(text, "ec-li-col");

        // Legend dots under a bar or a map.
        public static VisualElement Legend(params (Color colour, string text)[] items)
        {
            var row = new VisualElement(); row.AddToClassList("ec-legend2");
            foreach (var (colour, text) in items)
            {
                var it = new VisualElement { pickingMode = PickingMode.Ignore }; it.AddToClassList("ec-legend2-i");
                var d = new VisualElement(); d.AddToClassList("ec-dot"); d.style.backgroundColor = colour;
                it.Add(d); it.Add(Hud.Text(text, "ec-label"));
                row.Add(it);
            }
            return row;
        }

        // Text choices in equal cells (war goals and the like); the chosen one is pressed in.
        public static VisualElement ChipGrid(int columns, IList<string> items, int selected, Action<int> pick)
        {
            var grid = new VisualElement(); grid.AddToClassList("ec-chipgrid");
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var cell = new VisualElement(); cell.AddToClassList("ec-chipcell");
                cell.style.width = Length.Percent(100f / columns);
                var chip = Chip(items[i], i == selected, pick == null ? null : (Action)(() => pick(index)));
                chip.style.flexGrow = 1;
                cell.Add(chip);
                grid.Add(cell);
            }
            return grid;
        }

        // ---------- small parts ----------
        public static Label Badge(string text, string tone = null)
        {
            var l = Hud.Text(text, "ec-badge");
            if (tone != null) { l.AddToClassList("ec-badge-" + tone); l.AddToClassList("ec-t-" + tone); }
            return l;
        }

        public static VisualElement Button(string text, string icon, ButtonKind kind, Action click, bool big = false)
        {
            var b = new VisualElement();
            b.AddToClassList("ec-frame");
            b.AddToClassList("ec-pbtn");
            b.AddToClassList("ec-pb-" + kind.ToString().ToLowerInvariant());
            if (big) b.AddToClassList("ec-pb-big");
            if (icon != null) b.Add(Hud.Icon(icon, "ec-sm"));
            b.Add(Hud.Text(text, "ec-label"));
            if (click != null && kind != ButtonKind.Off)
            {
                b.AddToClassList("ec-click"); b.AddManipulator(new Clickable(click));
                b.userData = click;   // the review capture presses buttons through this (EmpireCoreHudCapture)
            }
            return b;
        }

        // The green receipt at the top of a page after an action.
        public static VisualElement Toast(string icon, string text, bool failed = false)
        {
            var t = new VisualElement(); t.AddToClassList("ec-toast");
            if (failed) t.AddToClassList("ec-toast-bad");
            if (icon != null) t.Add(Hud.Icon(icon, "ec-sm"));
            t.Add(Hud.Text(text, "ec-toast-t"));
            return t;
        }

        // ---------- guidance ----------
        public static VisualElement StepMark(string text, StepState state = StepState.Now)
        {
            var m = new VisualElement(); m.AddToClassList("ec-step");
            m.AddToClassList("ec-step-" + state.ToString().ToLowerInvariant());
            m.Add(Hud.Text(text, "ec-step-t"));
            return m;
        }

        public static VisualElement StepRow(string mark, StepState state, string text, string tone = null)
        {
            var row = new VisualElement(); row.AddToClassList("ec-steprow");
            row.Add(StepMark(state == StepState.Done ? "✓" : mark, state));
            row.Add(Text(text, "ec-steprow-t", tone ?? (state == StepState.Todo ? "m" : null)));
            return row;
        }

        // A card of numbered steps: how to do what the page is for. Steps already done show a tick.
        public static VisualElement Steps(string title, string icon, string note, params (StepState state, string text)[] steps)
        {
            var card = Card(title, icon ?? "iconQuestionMark");
            int n = 1;
            foreach (var (state, text) in steps) card.Add(StepRow((n++).ToString(), state, text));
            if (!string.IsNullOrEmpty(note)) card.Add(Note(note));
            return card;
        }

        // ---------- choosing ----------
        // A grid of icon tiles (war goals and the like); the chosen one is pressed in.
        public static VisualElement IconGrid(int columns, IList<(string icon, string label)> items, int selected, Action<int> pick)
        {
            var grid = new VisualElement(); grid.AddToClassList("ec-igrid");
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var tile = new VisualElement(); tile.AddToClassList("ec-frame"); tile.AddToClassList("ec-itile");
                tile.style.width = Length.Percent(100f / columns);
                if (i == selected) tile.AddToClassList("ec-on");
                tile.Add(Hud.Icon(items[i].icon));
                tile.Add(Hud.Text(items[i].label, "ec-itile-t"));
                if (pick != null) { tile.AddToClassList("ec-click"); tile.AddManipulator(new Clickable(() => pick(index))); }
                grid.Add(tile);
            }
            return grid;
        }

        // ---------- figures ----------
        // Two sides pulling on one figure: left value, its share from the middle out, icon, the other side.
        public static VisualElement Tug(string icon, double a, double b, string aText, string bText, Color aColour, Color bColour)
        {
            double sum = Math.Max(1e-9, Math.Abs(a) + Math.Abs(b));
            var row = new VisualElement(); row.AddToClassList("ec-tug");
            row.Add(Hud.Text(aText, "ec-tug-a"));
            row.Add(Track((float)(Math.Abs(a) / sum), aColour, true));
            row.Add(Hud.Icon(icon, "ec-sm"));
            row.Add(Track((float)(Math.Abs(b) / sum), bColour, false));
            row.Add(Hud.Text(bText, "ec-tug-b"));
            return row;
        }

        static VisualElement Track(float fraction, Color colour, bool fromRight)
        {
            var track = new VisualElement(); track.AddToClassList("ec-ptrack");
            if (fromRight) track.AddToClassList("ec-ptrack-r");
            var fill = new VisualElement(); fill.AddToClassList("ec-pfill");
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            fill.style.backgroundColor = colour;
            track.Add(fill);
            return track;
        }

        public static VisualElement Meter(string label, float fraction, string value, Color colour)
        {
            var row = new VisualElement(); row.AddToClassList("ec-meter");
            row.Add(Hud.Text(label, "ec-meter-l"));
            row.Add(Track(fraction, colour, false));
            row.Add(Hud.Text(value, "ec-meter-v"));
            return row;
        }

        public static VisualElement StatTile(string icon, string label, string value, string tone = null)
        {
            var t = new VisualElement(); t.AddToClassList("ec-frame"); t.AddToClassList("ec-stile");
            t.Add(Hud.Icon(icon, "ec-sm"));
            t.Add(Hud.Text(label, "ec-stile-l"));
            t.Add(Text(value, "ec-stile-v", tone));
            return t;
        }

        // ---------- people and realms ----------
        public static VisualElement Flag(uint colour, string text, bool small = false)
        {
            var f = new VisualElement(); f.AddToClassList("ec-pflag");
            if (small) f.AddToClassList("ec-pflag-sm");
            f.style.backgroundColor = Hud.Rgb(colour);
            if (!string.IsNullOrEmpty(text)) f.Add(Hud.Text(text, "ec-pflag-t"));
            return f;
        }

        public static VisualElement Medal(string icon, bool small = false)
        {
            var m = new VisualElement(); m.AddToClassList("ec-medal");
            if (small) m.AddToClassList("ec-medal-sm");
            m.Add(Hud.Icon(icon, small ? null : "ec-md"));
            return m;
        }

        // A banner naming a group of cards; warn paints it red.
        public static VisualElement Banner(string icon, string text, string note = null, bool warn = false)
        {
            var b = new VisualElement(); b.AddToClassList("ec-banner");
            if (warn) b.AddToClassList("ec-banner-warn");
            if (icon != null) b.Add(Hud.Icon(icon, "ec-sm"));
            b.Add(Hud.Text(text, "ec-banner-t"));
            if (!string.IsNullOrEmpty(note)) b.Add(Text(note, "ec-banner-n", warn ? "r" : null));
            return b;
        }

        // Actions not open yet: a strip of greyed chips with a lock.
        public static VisualElement LockRow(string label, string badge, params (string icon, string text)[] items)
        {
            var row = new VisualElement(); row.AddToClassList("ec-lockrow");
            row.Add(Hud.Text(label, "ec-lockrow-l"));
            foreach (var (icon, text) in items)
            {
                var chip = new VisualElement(); chip.AddToClassList("ec-lockchip");
                chip.Add(Hud.Icon(icon, "ec-sm"));
                chip.Add(Hud.Text(text, "ec-label"));
                row.Add(chip);
            }
            row.Add(Fill());
            if (badge != null) row.Add(Badge(badge));
            return row;
        }

        // One action, one card: icon and title, a line on what it does (or why it cannot be done),
        // optional extra content, and the button at the bottom.
        public static VisualElement ActionCard(string icon, string title, string text, CardKind kind, VisualElement headBadge,
                                               VisualElement extra, VisualElement button, string textTone = null)
        {
            var card = Card(title, icon, null, kind);
            card.AddToClassList("ec-action");
            if (headBadge != null) HeadAdd(card, headBadge);
            if (!string.IsNullOrEmpty(text))
            {
                var line = new VisualElement(); line.AddToClassList("ec-action-line");
                if (kind == CardKind.Dim) line.Add(Hud.Icon("iconLock", "ec-xs"));
                line.Add(Text(text, "ec-action-t", textTone ?? (kind == CardKind.Dim ? "m" : null)));
                card.Add(line);
            }
            if (extra != null) card.Add(extra);
            if (button != null) { var fill = Fill(); card.Add(fill); card.Add(button); }
            return card;
        }
    }
}
