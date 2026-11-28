using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Core;
using EmpireCraft.Presentation;
using EmpireCraft.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Action = System.Action;

namespace Orbis.Flat
{
    // Save slots (docs/design/ui-style.md, save window mockup): each slot is a folder under
    // persistentDataPath/saves with the ORBIS world (world.orbis), the core (core.ecs, via
    // EmpireCoreDriver.EncodeCore), a political thumbnail and a small description. Loading a
    // slot hands the core bytes to the driver, then loads the world the ORBIS way; the core
    // is restored when that world opens. Autosave every ten core years keeps the last three.
    public sealed partial class FlatWorldApp
    {
        [Serializable]
        sealed class SlotMeta
        {
            public string name; public bool auto; public long savedAt;
            public int year; public int month; public int realms; public string biggest;
            public string seed; public int width, height; public bool core;
        }

        sealed class Slot { public string dir; public SlotMeta meta; public Texture2D thumb; }

        const int AutoEveryYears = 10, AutoKeep = 3;
        static string SavesRoot => Path.Combine(Application.persistentDataPath, "saves");
        string saveToast; bool saveToastBad; string saveName; string deleteArmed;
        int lastAutoYear = int.MinValue; EmpireWorld lastAutoWorld;
        readonly Dictionary<string, (long stamp, Texture2D tex)> thumbCache = new Dictionary<string, (long, Texture2D)>();

        // ---------- files ----------
        List<Slot> ReadSlots()
        {
            var list = new List<Slot>();
            if (!Directory.Exists(SavesRoot)) return list;
            foreach (var dir in Directory.GetDirectories(SavesRoot))
            {
                string metaPath = Path.Combine(dir, "meta.json");
                if (!File.Exists(metaPath) || !File.Exists(Path.Combine(dir, "world.orbis"))) continue;
                SlotMeta meta = null;
                try { meta = JsonUtility.FromJson<SlotMeta>(File.ReadAllText(metaPath)); } catch (Exception) { }
                if (meta == null) continue;
                list.Add(new Slot { dir = dir, meta = meta, thumb = Thumb(Path.Combine(dir, "thumb.png")) });
            }
            return list.OrderByDescending(s => s.meta.savedAt).ToList();
        }

        Texture2D Thumb(string path)
        {
            if (!File.Exists(path)) return null;
            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            if (thumbCache.TryGetValue(path, out var c) && c.stamp == stamp) return c.tex;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            try { tex.LoadImage(File.ReadAllBytes(path)); } catch (Exception) { return null; }
            thumbCache[path] = (stamp, tex);
            return tex;
        }

        // Writes one slot; returns null or the reason it failed.
        string WriteSlot(string name, bool auto)
        {
            var w = CoreHere();
            if (World == null || Busy) return "世界还没就绪";
            try
            {
                Directory.CreateDirectory(SavesRoot);
                string id = (auto ? "auto_" : "save_") + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string dir = Path.Combine(SavesRoot, id), tmp = dir + ".tmp";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                solarClock.Flush(simulationStep);
                FlatSave.WriteAtomic(Path.Combine(tmp, "world.orbis"), World);
                byte[] core = null;
                if (w != null) { try { core = EmpireCoreDriver.EncodeCore(w); } catch (Exception e) { Debug.LogException(e); } }
                if (core != null) File.WriteAllBytes(Path.Combine(tmp, "core.ecs"), core);
                var meta = new SlotMeta
                {
                    name = string.IsNullOrWhiteSpace(name) ? DefaultSaveName() : name.Trim(), auto = auto, savedAt = DateTime.Now.Ticks,
                    year = w != null ? YearOf(w, w.Month) : (int)(CalendarDay / 360) + 1, month = w != null ? w.Month % 12 : World.Month,
                    realms = w != null ? w.LiveRealms.Count : 0, biggest = w != null ? Biggest(w) : "", seed = World.Seed, width = World.Width, height = World.Height,
                    core = core != null
                };
                File.WriteAllText(Path.Combine(tmp, "meta.json"), JsonUtility.ToJson(meta));
                var thumb = w != null ? SaveThumb(w) : null;
                if (thumb != null) File.WriteAllBytes(Path.Combine(tmp, "thumb.png"), thumb);
                Directory.Move(tmp, dir);
                if (auto) PruneAutos();
                return null;
            }
            catch (Exception e) { Debug.LogException(e); return e.Message; }
        }

        void PruneAutos()
        {
            foreach (var old in ReadSlots().Where(s => s.meta.auto).Skip(AutoKeep))
                try { Directory.Delete(old.dir, true); } catch (Exception) { }
        }

        string Biggest(EmpireWorld w)
        {
            Realm best = null;
            foreach (var r in w.LiveRealms) if (best == null || r.Cities.Count > best.Cities.Count) best = r;
            return best == null ? "" : best.Name + " " + best.Cities.Count + " 城";
        }

        string DefaultSaveName()
        {
            var w = CoreHere();
            if (w == null) return "第 " + ((int)(CalendarDay / 360) + 1) + " 年";
            Realm best = null;
            foreach (var r in w.LiveRealms) if (best == null || r.Cities.Count > best.Cities.Count) best = r;
            return "第 " + YearText(w, w.Month) + " 年" + (best != null ? " · " + best.Name + "最强" : "");
        }

        // A small political map of the whole world for the slot card.
        byte[] SaveThumb(EmpireWorld w)
        {
            try
            {
                var picker = new MapPicker(w.Width, w.Height, 148, 56, 4);
                picker.KeyAt = t => { int c = w.TileCity[t]; if (c < 0) return -1; var r = w.Cities[c].Realm; return r != null && r.Alive ? r.Id : -1; };
                picker.IsWater = t => !w.Claimable(t);
                picker.LookOf = id => new MapPicker.Look { Fill = id >= 0 && id < w.Realms.Count ? (Color32)Hud.Rgb(w.Realms[id].Color) : new Color32(74, 66, 54, 255) };
                picker.ShowAll(); picker.Redraw();
                return picker.Texture.EncodeToPNG();
            }
            catch (Exception e) { Debug.LogException(e); return null; }
        }

        void LoadSlot(Slot slot)
        {
            if (Busy) return;
            string core = Path.Combine(slot.dir, "core.ecs");
            EmpireCoreDriver.PendingCore = File.Exists(core) ? File.ReadAllBytes(core) : null;
            string world = Path.Combine(slot.dir, "world.orbis");
            lastAutoYear = int.MinValue;
            CloseHudWindow();
            Queue(c => { c.ThrowIfCancellationRequested(); return FlatSave.Read(world); }, "读取存档「" + slot.meta.name + "」…");
            SetStatus("读取存档「" + slot.meta.name + "」：第 " + slot.meta.year + " 年。");
        }

        // Autosave once every AutoEveryYears core years (called from the HUD tick).
        void TickAutosave()
        {
            var w = CoreHere();
            if (w == null || Busy) return;
            if (!ReferenceEquals(w, lastAutoWorld)) { lastAutoWorld = w; lastAutoYear = w.Year; return; }
            if (w.Year - lastAutoYear < AutoEveryYears) return;
            lastAutoYear = w.Year;
            string failed = WriteSlot("自动 · 第 " + YearText(w, w.Month) + " 年", true);
            if (failed == null) SetStatus("已自动存档：第 " + YearText(w, w.Month) + " 年。");
        }

        // ---------- window ----------
        void OpenCoreSaves()
        {
            if (hudWindowKey == "saves") { CloseHudWindow(); return; }
            saveToast = null; deleteArmed = null; saveName = DefaultSaveName();
            var win = new ArchiveWindow("存档", "iconHistory", CloseHudWindow, "saves");
            win.Rail = (x, rail) => BuildSavesRail(x, rail);
            win.Main = (x, page) => BuildSavesPage(x, page);
            OpenHudWindow("saves", win);
        }

        void BuildSavesRail(ArchiveWindow x, VisualElement rail)
        {
            var w = CoreHere();
            var now = Arc.InfoCard("当前世界", null);
            now.Add(Arc.Note(w != null ? "第 " + YearText(w, w.Month) + " 年" + MonthName(w.Month) + " · " + w.LiveRealms.Count + " 国" : "世界尚未就绪"));
            if (World != null) now.Add(Arc.Note("种子 " + World.Seed + " · " + World.Width + "×" + World.Height));
            rail.Add(now);
            var menu = Arc.Card();
            menu.Add(Arc.MenuItem("iconHistory", "存档", ReadSlots().Count.ToString(), true, null));
            menu.Add(Arc.MenuItem("TabColor", "世界设置", null, false, () => OpenHudWorld(0)));
            rail.Add(menu);
            rail.Add(Arc.Steps("怎么用", "iconQuestionMark", "每 " + AutoEveryYears + " 年自动存一份，留最近 " + AutoKeep + " 份",
                (Arc.StepState.Now, "写个名字，按「存档」"), (Arc.StepState.Todo, "想回到从前：在卡上按「读取」")));
        }

        void BuildSavesPage(ArchiveWindow x, VisualElement page)
        {
            var w = CoreHere();
            var slots = ReadSlots();
            page.Add(Arc.PageHead("存档", "把这个世界存下来，或回到从前"));
            if (saveToast != null) page.Add(Arc.Toast(null, saveToast, saveToastBad));
            var last = slots.FirstOrDefault(s => !s.meta.auto);
            page.Add(Arc.KpiRow(("当前", w != null ? "第 " + YearText(w, w.Month) + " 年" : "—", null, false),
                ("存档", slots.Count + " 份", null, false),
                ("上次保存", last == null ? "从未" : Ago(last.meta.savedAt), last != null && DateTime.Now.Ticks - last.meta.savedAt < TimeSpan.TicksPerMinute * 10 ? "g" : null, true),
                ("自动存档", "每 " + AutoEveryYears + " 年", null, false)));

            var create = Arc.InfoCard("新存档", "iconHistory", Arc.Badge("名字可改", "m"));
            var name = new TextField { value = saveName ?? "" };
            name.AddToClassList("ec-savename");
            name.RegisterValueChangedCallback(e => saveName = e.newValue);
            create.Add(Arc.Row(Arc.Grow(name, 1), Arc.Button("存档", null, Arc.ButtonKind.Gold, () =>
            {
                string n = string.IsNullOrWhiteSpace(saveName) ? DefaultSaveName() : saveName.Trim();
                string failed = WriteSlot(n, false);
                saveToastBad = failed != null;
                saveToast = failed == null ? "已存档「" + n + "」：第 " + (w != null ? YearText(w, w.Month) : "?") + " 年" + (w != null ? MonthName(w.Month) : "") : "存档没成：" + failed;
                if (failed == null) { SetStatus("已存档「" + n + "」。"); saveName = DefaultSaveName(); }
                x.Refresh();
            })));
            page.Add(create);

            var grid = Arc.Grid(2);
            foreach (var slot in slots) Arc.GridAdd(grid, SlotCard(x, w, slot));
            if (slots.Count % 2 == 1) { var pad = new VisualElement(); pad.AddToClassList("ec-pgrid-cell"); grid[grid.childCount - 1].Add(pad); }
            if (slots.Count == 0) page.Add(Arc.Empty("还没有存档。写个名字，按「存档」。"));
            page.Add(grid);
            page.Add(Arc.Note("· 存档在 " + SavesRoot + "；世界与列国一起存；读取前不会自动保存当前这一局"));
        }

        VisualElement SlotCard(ArchiveWindow x, EmpireWorld w, Slot slot)
        {
            var m = slot.meta;
            bool sameMap = World != null && m.seed == World.Seed && m.width == World.Width && m.height == World.Height;
            var card = Arc.InfoCard(m.name, "iconHistory", m.auto ? Arc.Badge("自动", "c") : Arc.Badge("手动", "m"),
                !sameMap ? Arc.Badge("别的世界", "o") : !m.core ? Arc.Badge("只有地图", "o") : null);
            var body = new VisualElement(); body.AddToClassList("ec-prow"); body.style.alignItems = Align.FlexStart;
            var pic = new VisualElement(); pic.AddToClassList("ec-savethumb");
            if (slot.thumb != null) pic.style.backgroundImage = new StyleBackground(slot.thumb);
            body.Add(pic);
            var facts = Arc.Grow(Arc.ListBlock(), 1);
            Arc.ListRow(facts, "年份", null, "第 " + m.year + " 年");
            Arc.ListRow(facts, "列国", null, m.realms + " 国");
            Arc.ListRow(facts, "最大", null, string.IsNullOrEmpty(m.biggest) ? "—" : m.biggest);
            Arc.ListRow(facts, "存于", null, Ago(m.savedAt));
            body.Add(facts);
            card.Add(body);
            int now = w != null ? YearOf(w, w.Month) : 0;
            card.Add(Arc.Verdict(!sameMap ? "读取会换成另一张地图" : !m.core ? "只存了地图，读取后列国重新开局"
                : w == null ? "第 " + m.year + " 年的天下" : m.year == now ? "与当前同年" : m.year < now ? "比当前早 " + (now - m.year) + " 年，那时 " + m.realms + " 国并立" : "比当前晚 " + (m.year - now) + " 年"));
            var buttons = new List<VisualElement> { Arc.Grow(Arc.Button("读取", null, Arc.ButtonKind.Gold, () => LoadSlot(slot)), 2) };
            if (!m.auto) buttons.Add(Arc.Grow(Arc.Button("覆盖", null, Arc.ButtonKind.Plain, () =>
            {
                string failed = WriteSlot(m.name, false);
                if (failed == null) try { Directory.Delete(slot.dir, true); } catch (Exception) { }
                saveToastBad = failed != null; saveToast = failed == null ? "已覆盖「" + m.name + "」" : "覆盖没成：" + failed;
                x.Refresh();
            }), 1));
            bool armed = deleteArmed == slot.dir;
            buttons.Add(Arc.Grow(Arc.Button(armed ? "确认删除" : "删除", null, armed ? Arc.ButtonKind.Red : Arc.ButtonKind.Plain, () =>
            {
                if (!armed) { deleteArmed = slot.dir; x.Refresh(); return; }
                try { Directory.Delete(slot.dir, true); saveToastBad = false; saveToast = "已删除「" + m.name + "」"; }
                catch (Exception e) { saveToastBad = true; saveToast = "删除没成：" + e.Message; }
                deleteArmed = null; x.Refresh();
            }), 1));
            var row = Arc.Row(buttons.ToArray()); row.AddToClassList("ec-savebtns");
            card.Add(Arc.Fill());
            card.Add(row);
            return card;
        }

        static string Ago(long ticks)
        {
            var span = DateTime.Now - new DateTime(ticks);
            if (span.TotalMinutes < 1) return "刚刚";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " 分钟前";
            var at = new DateTime(ticks);
            return at.Date == DateTime.Today ? at.ToString("HH:mm") : at.Date == DateTime.Today.AddDays(-1) ? "昨天 " + at.ToString("HH:mm") : at.ToString("MM-dd");
        }
    }
}
