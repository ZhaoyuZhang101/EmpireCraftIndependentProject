using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Core;
using EmpireCraft.UI;
using UnityEngine;
using UnityEngine.UIElements;
using Action = System.Action;

namespace Orbis.Flat
{
    // The sub-pages of the archive windows in the v5 house layout (docs/design/ui-style.md):
    // 国务 财政/城池/世系/外交战事/文明, 战争 概况/双方/战报/往事, 宗族 族人/亲属/天下宗族, 史书 天下大事.
    public sealed partial class FlatWorldApp
    {
        MapPicker cityMap, foreignMap, warMap; EmpireWorld cityMapWorld, foreignMapWorld, warMapWorld; object cityMapAim, foreignMapAim, warMapAim;

        static readonly Color32 Steward = new Color32(214, 172, 78, 255), Martial = new Color32(226, 103, 90, 255),
            Wit = new Color32(127, 216, 234, 255), Grace = new Color32(101, 214, 110, 255), Dim = new Color32(66, 58, 50, 255);

        static void PersonMeters(VisualElement card, Person p)
        {
            card.Add(Arc.Meter("治", p.Stewardship / 20f, p.Stewardship.ToString(), Steward));
            card.Add(Arc.Meter("武", p.Warfare / 20f, p.Warfare.ToString(), Martial));
            card.Add(Arc.Meter("智", p.Intelligence / 20f, p.Intelligence.ToString(), Wit));
            card.Add(Arc.Meter("交", p.Diplomacy / 20f, p.Diplomacy.ToString(), Grace));
        }

        static string Strong(Person p)
        {
            int best = Math.Max(Math.Max(p.Stewardship, p.Warfare), Math.Max(p.Intelligence, p.Diplomacy));
            return best == p.Stewardship ? "善治" : best == p.Warfare ? "善战" : best == p.Intelligence ? "多谋" : "善交";
        }

        // A small map aimed at some tiles, keyed and coloured by the caller.
        MapPicker AimedMap(ref MapPicker picker, ref EmpireWorld world, ref object aimKey, EmpireWorld w, object aim, IEnumerable<int> tiles, float margin,
                           Func<int, int> keyAt, Func<int, MapPicker.Look> look, Action<int> pick)
        {
            if (picker == null || !ReferenceEquals(world, w)) { world = w; picker = new MapPicker(w.Width, w.Height, 336, 128, 4); aimKey = null; }
            picker.KeyAt = keyAt; picker.IsWater = t => !w.Claimable(t); picker.LookOf = look; picker.Pick = pick;
            if (!Equals(aimKey, aim)) { aimKey = aim; picker.FocusOn(tiles, margin); }
            picker.Redraw();
            return picker;
        }

        static string FiscalName(string key) => Safe(() => { string s = LM.Get("fiscal_source_" + key); return s == "fiscal_source_" + key ? key : s; }, key);

        // ---------- 国务 · 财政 ----------
        void BuildStateFinanceV5(EmpireWorld w, Realm r, VisualElement page, ArchiveWindow x)
        {
            var books = w.Ledger.Realm(r.Id);
            var income = new Dictionary<string, long>(); var expense = new Dictionary<string, long>();
            long monthNet = 0; double latest = double.MinValue;
            var periods = books.Books?.periods;
            if (periods != null)
            {
                foreach (var p in periods) if (p != null && p.timestamp > latest) latest = p.timestamp;
                foreach (var p in periods)
                {
                    if (p == null) continue;
                    bool inRange = financeYear ? p.timestamp > w.Month - 12 : p.timestamp >= latest;
                    long inSum = 0, outSum = 0;
                    if (p.income != null) foreach (var kv in p.income) if (kv.Key != "InternalTransfer") { inSum += kv.Value; if (inRange) { income.TryGetValue(kv.Key, out long a); income[kv.Key] = a + kv.Value; } }
                    if (p.expense != null) foreach (var kv in p.expense) { outSum += kv.Value; if (inRange) { expense.TryGetValue(kv.Key, out long a); expense[kv.Key] = a + kv.Value; } }
                    if (p.timestamp >= latest) monthNet = inSum - outSum;
                }
            }
            long totalIn = income.Values.Sum(), totalOut = expense.Values.Sum(), net = totalIn - totalOut;
            string span = financeYear ? "近一年" : "近一月";
            page.Add(Arc.PageHead("财政", r.Name + "：钱从哪来，花到哪去"));
            var modes = Arc.Row(Arc.Chip("近一月", !financeYear, () => { financeYear = false; x.Refresh(); }), Arc.Chip("近一年", financeYear, () => { financeYear = true; x.Refresh(); }));
            modes.AddToClassList("ec-mapmodes");
            page.Add(modes);
            page.Add(Arc.KpiRow(("国库", books.Treasury.ToString("N0"), books.Treasury < 0 ? "r" : "y", true), ("本月结余", (monthNet >= 0 ? "+" : "") + monthNet.ToString("N0"), monthNet >= 0 ? "g" : "r", false),
                (span + "结余", (net >= 0 ? "+" : "") + net.ToString("N0"), net >= 0 ? "g" : "r", false), ("欠饷", books.Unpaid.ToString("N0"), books.Unpaid > 0 ? "r" : null, false)));
            var grid = Arc.Grid(2);
            VisualElement Breakdown(string title, string icon, Dictionary<string, long> parts, long total, bool spend)
            {
                var card = Arc.InfoCard(title, icon, Arc.Badge(total.ToString("N0"), spend ? "o" : "g"));
                var sorted = parts.OrderByDescending(p => p.Value).ToList();
                if (sorted.Count == 0) { card.Add(Arc.Verdict("无记录", "m")); return card; }
                card.Add(ShareBar(sorted.Select(p => (FiscalName(p.Key), (double)p.Value)).ToList()));
                var list = Arc.ListBlock();
                foreach (var kv in sorted.Take(4)) Arc.ListRow(list, FiscalName(kv.Key), spend && kv.Key == "Corruption" ? Arc.Badge("贪墨", "r") : null, kv.Value.ToString("N0"));
                card.Add(list);
                var top = sorted[0];
                long corrupt = spend && parts.TryGetValue("Corruption", out long c) ? c : 0;
                card.Add(Arc.Verdict(spend && corrupt > total * .2 ? "贪墨占支出 " + Mathf.RoundToInt(100f * corrupt / Math.Max(1, total)) + "%，宜整饬吏治"
                    : (spend ? "最大开销：" : "最大进项：") + FiscalName(top.Key) + "，占 " + Mathf.RoundToInt(100f * top.Value / Math.Max(1, total)) + "%", spend && corrupt > total * .2 ? "r" : "o"));
                return card;
            }
            Arc.GridAdd(grid, Breakdown(span + "收入", "wealth_layer", income, totalIn, false));
            Arc.GridAdd(grid, Breakdown(span + "支出", "market_layer", expense, totalOut, true));
            var cities = r.Cities.Where(c => c.Alive).OrderBy(c => w.Ledger.City(c.Id).Stability).ToList();
            var gov = Arc.InfoCard("吏治", "governance_layer", Arc.Badge("稳定低的在上", "m"));
            var gl = Arc.ListBlock();
            Arc.ListHead(gl, "城", "稳定", "腐败");
            foreach (var c in cities.Take(5))
            {
                var city = c; var acc = w.Ledger.City(c.Id);
                var row = Arc.ListRow(gl, c.Name, acc.Stability < 50 ? Arc.Badge("不稳", "o") : null, null, null, null, null, () => FocusCoreTile(city.Tile));
                row.Add(Arc.Col(acc.Stability.ToString("0"))); row.Add(Arc.Col(Pct(acc.Corruption)));
            }
            gov.Add(gl);
            gov.Add(Arc.Verdict(cities.Count == 0 ? "—" : w.Ledger.City(cities[0].Id).Stability < 50 ? cities[0].Name + "最不稳，宜减税或驻军" : "诸城安定"));
            Arc.GridAdd(grid, gov);
            var debt = Arc.InfoCard("积弊", "TabConstitution", books.Pressure > 60 ? Arc.Badge("积重", "r") : Arc.Badge("尚可", "g"));
            var dl = Arc.ListBlock();
            Arc.ListRow(dl, "正统", null, books.Legitimacy.ToString(), null, null, books.Legitimacy < 50 ? "r" : null);
            Arc.ListRow(dl, "王朝积弊", null, books.Pressure.ToString("0"), null, null, books.Pressure > 60 ? "r" : null);
            Arc.ListRow(dl, "全国腐败", null, Pct(books.Corruption), null, null, books.Corruption > .25f ? "r" : null);
            Arc.ListRow(dl, "欠饷", null, books.Unpaid.ToString("N0"), null, null, books.Unpaid > 0 ? "r" : null);
            debt.Add(dl);
            debt.Add(Arc.Verdict(books.Unpaid > 0 ? "欠饷未清，军心不稳" : books.Pressure > 60 ? "积弊深重，恐生变乱" : "府库与人心尚稳"));
            Arc.GridAdd(grid, debt);
            page.Add(grid);
            if (periods == null || periods.Count == 0) page.Add(Arc.Note("· 国库尚无收支记录"));
            else page.Add(Arc.Note("· 上方切换近一月 / 近一年；点城去地图"));
        }

        // ---------- 国务 · 城池 ----------
        void BuildStateCitiesV5(EmpireWorld w, Realm r, VisualElement page)
        {
            var list = r.Cities.Where(c => c.Alive).OrderByDescending(c => c.Population).ToList();
            double pop = list.Sum(c => c.Population);
            int garrison = list.Sum(c => (int)w.Ledger.City(c.Id).Garrison);
            page.Add(Arc.PageHead("城池", r.Name + "：哪些城撑起这个国"));
            page.Add(Arc.KpiRow(("城池", list.Count + " 城", null, false), ("人口", Arc.Count(pop), null, true), ("辖地", list.Sum(c => c.Tiles.Count).ToString("N0") + " 格", null, false), ("驻军", garrison.ToString("N0"), null, false)));
            var grid = Arc.Grid(2);
            var ids = new HashSet<int>(list.Select(c => c.Id));
            var map = Arc.InfoCard("辖境", "iconTitleLayer", Arc.Badge("按城上色", "m"));
            map.Add(AimedMap(ref cityMap, ref cityMapWorld, ref cityMapAim, w, r, list.SelectMany(c => c.Tiles), .6f,
                t => w.TileCity[t] >= 0 && ids.Contains(w.TileCity[t]) ? w.TileCity[t] : -1,
                id => { var c = w.Cities[id]; var col = Color.Lerp(Hud.Rgb(r.Color), Color.white, (id * 37 % 5) * .08f); return new MapPicker.Look { Fill = col, Label = c.Name, Clickable = true, Edge = c.IsCapital() ? MapPicker.Edge.Gold : MapPicker.Edge.Plain }; },
                id => FocusCoreTile(w.Cities[id].Tile)).Root);
            var cap = list.FirstOrDefault(c => c.IsCapital());
            map.Add(Arc.Verdict(cap == null ? "无都城" : "都城" + cap.Name + "，辖 " + list.Count + " 城"));
            Arc.GridAdd(grid, map);
            var top = Arc.InfoCard("人口榜", "iconPopulation50", Arc.Badge("前八", "m"));
            var tl = Arc.ListBlock();
            foreach (var c in list.Take(8)) { var city = c; Arc.ListRow(tl, c.Name, c.IsCapital() ? Arc.Badge("都城", "y") : null, Arc.Count(c.Population), null, null, null, () => FocusCoreTile(city.Tile)); }
            top.Add(tl);
            top.Add(Arc.Verdict(cap == null || pop <= 0 ? "—" : "都城占全国人口 " + Mathf.RoundToInt((float)(100 * cap.Population / pop)) + "%"));
            Arc.GridAdd(grid, top);
            var shaky = list.OrderBy(c => w.Ledger.City(c.Id).Stability).ToList();
            var st = Arc.InfoCard("稳定", "governance_layer", Arc.Badge(shaky.Count(c => w.Ledger.City(c.Id).Stability < 50) + " 城不稳", "o"));
            var sl = Arc.ListBlock();
            foreach (var c in shaky.Take(5)) { var city = c; float s = w.Ledger.City(c.Id).Stability; Arc.ListRow(sl, c.Name, s < 50 ? Arc.Badge("不稳", "o") : null, s.ToString("0"), null, null, null, () => FocusCoreTile(city.Tile)); }
            st.Add(sl);
            st.Add(Arc.Verdict(shaky.Count == 0 ? "—" : w.Ledger.City(shaky[0].Id).Stability < 50 ? shaky[0].Name + "最不稳" : "诸城安定"));
            Arc.GridAdd(grid, st);
            var gar = Arc.InfoCard("驻军", "TabMilitary", Arc.Badge(garrison.ToString("N0") + " 人", "m"));
            var byGarrison = list.OrderByDescending(c => w.Ledger.City(c.Id).Garrison).ToList();
            gar.Add(ShareBar(byGarrison.Select(c => (c.Name, (double)w.Ledger.City(c.Id).Garrison)).ToList()));
            var gl = Arc.ListBlock();
            foreach (var c in byGarrison.Take(3)) Arc.ListRow(gl, c.Name, null, w.Ledger.City(c.Id).Garrison.ToString("N0"));
            gar.Add(gl);
            gar.Add(Arc.Verdict(byGarrison.Count == 0 || garrison <= 0 ? "无驻军" : byGarrison[0].Name + "驻兵最多"));
            Arc.GridAdd(grid, gar);
            page.Add(grid);
            if (list.Count > 8)
            {
                var all = Arc.InfoCard("全部城池", "place_city", Arc.Badge(list.Count + " 城", "m"));
                var al = Arc.ListBlock();
                Arc.ListHead(al, "城", "稳定", "驻军");
                foreach (var c in list) { var city = c; var acc = w.Ledger.City(c.Id); var row = Arc.ListRow(al, c.Name + "  " + Arc.Count(c.Population), c.IsCapital() ? Arc.Badge("都城", "y") : null, null, null, null, null, () => FocusCoreTile(city.Tile)); row.Add(Arc.Col(acc.Stability.ToString("0"))); row.Add(Arc.Col(Arc.Count(acc.Garrison))); }
                all.Add(al);
                page.Add(all);
            }
            page.Add(Arc.Note("· 点图上或名单里的城，地图对准它"));
        }

        // ---------- 国务 · 世系 ----------
        void BuildStateLineageV5(EmpireWorld w, Realm r, VisualElement page)
        {
            var reigns = new List<(int month, int person, bool newHouse)>();
            foreach (var e in w.Annals.Entries)
                if (e.A == r.Id && (e.Kind == AnnalKind.Accession || e.Kind == AnnalKind.NewDynasty))
                {
                    if (reigns.Count > 0 && reigns[reigns.Count - 1].month == e.Month && reigns[reigns.Count - 1].person == e.B) continue;
                    reigns.Add((e.Month, e.B, e.Kind == AnnalKind.NewDynasty));
                }
            int houses = 1 + reigns.Count(z => z.newHouse);
            page.Add(Arc.PageHead("世系", r.Name + "：历代谁坐过这个位子"));
            page.Add(Arc.KpiRow(("历代", (reigns.Count + 1) + " 君", null, false), ("易姓", (houses - 1) + " 次", houses > 1 ? "o" : null, false),
                ("当朝", r.RulingClan != null ? r.RulingClan.Surname + "氏" : "—", "y", true), ("当朝历年", Math.Max(0, (w.Month - w.Ledger.Realm(r.Id).DynastyStart) / 12) + " 年", null, false)));
            var grid = Arc.Grid(2);
            var k = r.Ruler;
            var now = Arc.InfoCard("今上", "ChineseCrown", k == null || !k.Alive ? Arc.Badge("空悬", "r") : Arc.Badge("在位 " + Math.Max(0, (w.Month - ReignStart(w, r)) / 12) + " 年", "y"));
            if (k != null && k.Alive)
            {
                var kk = k;
                now.Add(Arc.Row(Arc.Text(w.FullName(k), "ec-li-name", "y"), Arc.Note(w.AgeOf(k) + " 岁 · 名望 " + k.Renown)));
                PersonMeters(now, k);
                now.Add(Arc.Verdict(Strong(k) + "之君"));
                now.AddToClassList("ec-click"); now.AddManipulator(new Clickable(() => OpenCoreClan(kk.Clan, kk)));
            }
            else now.Add(Arc.Verdict("君位空悬", "r"));
            Arc.GridAdd(grid, now);
            var past = Arc.InfoCard("历代", "TabDynasty", Arc.Badge("新的在上", "m"));
            var pl = Arc.ListBlock();
            for (int i = reigns.Count - 1, n = 0; i >= 0 && n < 7; i--, n++)
            {
                var (month, pid, newHouse) = reigns[i];
                var p = pid >= 0 && pid < w.People.Count ? w.People[pid] : null;
                int end = i + 1 < reigns.Count ? reigns[i + 1].month : w.Month;
                Arc.ListRow(pl, p != null ? w.FullName(p) : "?", newHouse ? Arc.Badge("易姓", "o") : null, Math.Max(0, (end - month) / 12) + " 年", null, null, i + 1 == reigns.Count ? "y" : null,
                    p != null && p.Clan != null ? () => OpenCoreClan(p.Clan, p) : (Action)null);
            }
            if (reigns.Count == 0) Arc.ListRow(pl, "开国之君在位至今", null, null);
            past.Add(pl);
            past.Add(Arc.Verdict(reigns.Count == 0 ? "国祚未传" : "共 " + (reigns.Count + 1) + " 君，易姓 " + (houses - 1) + " 次"));
            Arc.GridAdd(grid, past);
            var byHouse = reigns.Where(z => z.person >= 0 && z.person < w.People.Count && w.People[z.person].Clan != null)
                .GroupBy(z => w.People[z.person].Clan).Select(g => (clan: g.Key, n: g.Count())).OrderByDescending(g => g.n).ToList();
            var dyn = Arc.InfoCard("王朝", "specificClanIcon", Arc.Badge(houses + " 姓", "m"));
            dyn.Add(ShareBar(byHouse.Select(g => (g.clan.Surname + "氏", (double)g.n)).ToList()));
            var dl = Arc.ListBlock();
            foreach (var (clan, n) in byHouse.Take(4)) Arc.ListRow(dl, clan.Surname + "氏", clan == r.RulingClan ? Arc.Badge("当朝", "y") : null, n + " 君");
            dyn.Add(dl);
            dyn.Add(Arc.Verdict(byHouse.Count == 0 ? "—" : byHouse[0].clan.Surname + "氏出君最多"));
            Arc.GridAdd(grid, dyn);
            var lengths = new List<(Person p, int years)>();
            for (int i = 0; i < reigns.Count; i++)
            {
                var p = reigns[i].person >= 0 && reigns[i].person < w.People.Count ? w.People[reigns[i].person] : null;
                int end = i + 1 < reigns.Count ? reigns[i + 1].month : w.Month;
                if (p != null) lengths.Add((p, Math.Max(0, (end - reigns[i].month) / 12)));
            }
            var len = Arc.InfoCard("在位长短", "iconHistory", Arc.Badge("最久的在上", "m"));
            var ll = Arc.ListBlock();
            foreach (var (p, years) in lengths.OrderByDescending(z => z.years).Take(4)) Arc.ListRow(ll, w.FullName(p), null, years + " 年");
            if (lengths.Count == 0) Arc.ListRow(ll, "—", null, null);
            len.Add(ll);
            double avg = lengths.Count == 0 ? 0 : lengths.Average(z => z.years);
            len.Add(Arc.Verdict(lengths.Count == 0 ? "—" : "平均在位 " + avg.ToString("0") + " 年" + (avg < 5 ? "，君位更迭频繁" : "")));
            Arc.GridAdd(grid, len);
            page.Add(grid);
            page.Add(Arc.Note("· 点君主看他的宗族"));
        }

        // ---------- 国务 · 外交战事 ----------
        void BuildStateForeignV5(EmpireWorld w, Realm r, VisualElement page)
        {
            var neighbours = new HashSet<Realm>();
            foreach (var c in r.Cities) if (c.Alive) foreach (var n in SafeValue(() => c.NeighbourRealms, new HashSet<Realm>())) if (n != null && n != r && n.Alive) neighbours.Add(n);
            var opinions = neighbours.ToDictionary(n => n, n => SafeValue(() => w.Diplomacy.GetOpinion(r, n)?.Total ?? 0, 0));
            var alliance = r.GetAlliance();
            var allies = alliance == null ? new List<Realm>() : SafeValue(() => alliance.Members.Where(m => m != r && m.Alive).ToList(), new List<Realm>());
            int hostile = opinions.Count(kv => kv.Value < 0);
            page.Add(Arc.PageHead("外交战事", r.Name + "：与谁为敌，与谁为友"));
            page.Add(Arc.KpiRow(("战事", r.Wars.Count + " 场", r.Wars.Count > 0 ? "r" : null, false), ("盟友", allies.Count + " 国", allies.Count > 0 ? "g" : null, false),
                ("邻国", neighbours.Count + " 国", null, true), ("交恶邻国", hostile + " 国", hostile > 0 ? "o" : null, false)));
            var grid = Arc.Grid(2);
            var enemies = new HashSet<Realm>(SafeValue(() => r.GetEnemies(), new List<Realm>()));
            var map = Arc.InfoCard("四邻", "iconTitleLayer", Arc.Badge("点国看外交", "m"));
            var tiles = r.Cities.SelectMany(c => c.Tiles).Concat(neighbours.SelectMany(n => n.Cities.SelectMany(c => c.Tiles)));
            map.Add(AimedMap(ref foreignMap, ref foreignMapWorld, ref foreignMapAim, w, r, tiles, .3f,
                t => { int c = w.TileCity[t]; if (c < 0) return -1; var z = w.Cities[c].Realm; return z != null && z.Alive ? z.Id : -1; },
                id =>
                {
                    var z = w.Realms[id];
                    Color32 fill = z == r ? new Color32(201, 162, 74, 255) : enemies.Contains(z) ? new Color32(200, 60, 44, 255) : allies.Contains(z) ? new Color32(90, 138, 94, 255)
                        : neighbours.Contains(z) ? (Color32)Color.Lerp(Hud.Rgb(z.Color), new Color(.24f, .2f, .17f), .35f) : Dim;
                    return new MapPicker.Look { Fill = fill, Label = neighbours.Contains(z) || z == r ? z.Name : null, Clickable = z != r, Edge = z == r ? MapPicker.Edge.Gold : MapPicker.Edge.Plain };
                },
                id => { var z = w.Realms[id]; if (z != r && z.Alive) OpenCoreDiplomacy(r, z); }).Root);
            map.Add(Arc.Legend((new Color32(201, 162, 74, 255), "本国"), (new Color32(200, 60, 44, 255), "交战"), (new Color32(90, 138, 94, 255), "盟友"), (Dim, "远国")));
            map.Add(Arc.Verdict(enemies.Count > 0 ? "正与 " + enemies.Count + " 国交战" : "四境无战事"));
            Arc.GridAdd(grid, map);
            var wars = Arc.InfoCard("战事", "TabWarfare", Arc.Badge(r.Wars.Count + " 场", r.Wars.Count > 0 ? "r" : "m"));
            var wl = Arc.ListBlock();
            foreach (var war in r.Wars.Take(5))
            {
                var v = war; bool attacking = war.IsAttacker(r); var enemy = attacking ? war.MainDefender : war.MainAttacker;
                Arc.ListRow(wl, "对 " + (enemy != null ? enemy.Name : "—"), Arc.Badge(attacking ? "攻" : "守", attacking ? "r" : "c"), "已 " + war.AgeYears() + " 年", null, null, null, () => OpenCoreWar(v));
            }
            if (r.Wars.Count == 0) Arc.ListRow(wl, "无战事", null, null);
            wars.Add(wl);
            wars.Add(Arc.Verdict(r.Wars.Count == 0 ? "可以休养" : r.Wars.Count >= 2 ? "多线作战，宜先停一处" : "专心一战"));
            Arc.GridAdd(grid, wars);
            var ally = Arc.InfoCard("盟约", "plot_kingdom_start_join_taken_alliance", alliance == null ? Arc.Badge("未结盟", "m") : Arc.Badge(Safe(() => alliance.Name), "g"));
            var al = Arc.ListBlock();
            foreach (var m in allies.Take(5)) { var z = m; Arc.ListRow(al, m.Name, null, m.Cities.Count + " 城", null, Hud.Rgb(m.Color), null, () => OpenCoreState(z)); }
            if (allies.Count == 0) Arc.ListRow(al, "无盟友", null, null);
            ally.Add(al);
            ally.Add(Arc.Verdict(allies.Count == 0 ? "孤立无援" : "有 " + allies.Count + " 国可为援"));
            Arc.GridAdd(grid, ally);
            var near = Arc.InfoCard("邻国好感", "civ_nations", Arc.Badge(neighbours.Count + " 国", "m"));
            var nl = Arc.ListBlock();
            foreach (var n in neighbours.OrderBy(n => opinions[n]).Take(6))
            {
                var z = n; int total = opinions[n];
                Arc.ListRow(nl, n.Name, enemies.Contains(n) ? Arc.Badge("交战", "r") : total < 0 ? Arc.Badge("交恶", "o") : total > 0 ? Arc.Badge("友好", "g") : null, (total >= 0 ? "+" : "") + total, null, Hud.Rgb(n.Color), null, () => OpenCoreDiplomacy(r, z));
            }
            if (neighbours.Count == 0) Arc.ListRow(nl, "四境无邻", null, null);
            near.Add(nl);
            var worst = neighbours.OrderBy(n => opinions[n]).FirstOrDefault();
            near.Add(Arc.Verdict(worst == null ? "—" : opinions[worst] < 0 ? "与" + worst.Name + "最恶，当心边衅" : "四邻尚睦"));
            Arc.GridAdd(grid, near);
            page.Add(grid);
            page.Add(Arc.Note("· 点图上或名单里的邻国，进两国的外交"));
        }

        // ---------- 国务 · 文明 ----------
        void BuildStateCultureV5(EmpireWorld w, Realm r, VisualElement page)
        {
            string id = CultureId(r);
            var tech = SafeValue(() => TechnologySystem.GetState(id), null);
            var inst = SafeValue(() => InstitutionSystem.GetOrCreateCultureState(id), null);
            page.Add(Arc.PageHead("文明", CultureOf(r) + "：走到了哪个时代"));
            page.Add(Arc.KpiRow(("时代", Safe(() => TechnologySystem.GetEraName(id)), "c", true), ("已研科技", tech == null ? "—" : tech.researched_techs.Count + " 项", null, false),
                ("年研究点", tech == null ? "—" : tech.last_yearly_points.ToString("0"), "g", false), ("已行制度", inst?.enacted_node_ids == null ? "—" : inst.enacted_node_ids.Count + " 项", null, false)));
            var grid = Arc.Grid(2);
            var tc = Arc.InfoCard("科技", "TabInstitutions", tech != null && !string.IsNullOrEmpty(tech.current_tech) ? Arc.Badge("研究中", "c") : Arc.Badge("停顿", "m"));
            if (tech != null && !string.IsNullOrEmpty(tech.current_tech))
            {
                tc.Add(Arc.Row(Arc.Text(Safe(() => TechnologySystem.GetTechName(tech.current_tech, id), tech.current_tech), "ec-li-name", "c")));
                if (tech.progress <= 1.001f) tc.Add(Arc.Meter("进", tech.progress, Pct(tech.progress), Wit));
            }
            var tl = Arc.ListBlock();
            if (tech != null) foreach (var t in Enumerable.Reverse(tech.researched_techs).Take(4))
                {
                    string when = tech.timestamps != null && tech.timestamps.TryGetValue(t, out double s) ? "第 " + YearText(w, (int)s) + " 年" : "";
                    Arc.ListRow(tl, Safe(() => TechnologySystem.GetTechName(t, id), t), null, when);
                }
            if (tl.childCount == 0) Arc.ListRow(tl, "尚无科技", null, null);
            tc.Add(tl);
            tc.Add(Arc.Verdict(tech == null ? "—" : "已研 " + tech.researched_techs.Count + " 项，最近：" + (tech.researched_techs.Count > 0 ? Safe(() => TechnologySystem.GetTechName(tech.researched_techs[tech.researched_techs.Count - 1], id)) : "无")));
            Arc.GridAdd(grid, tc);
            var ic = Arc.InfoCard("制度", "TabConstitution", inst != null && !string.IsNullOrEmpty(inst.drift_node_id) ? Arc.Badge("推行中", "o") : Arc.Badge("无推行", "m"));
            if (inst != null && !string.IsNullOrEmpty(inst.drift_node_id))
            {
                ic.Add(Arc.Row(Arc.Text(Safe(() => InstitutionSystem.GetNodeName(InstitutionDefinitionRegistry.Get(inst.drift_node_id), id), inst.drift_node_id), "ec-li-name", "o")));
                if (inst.drift_progress <= 1.001f) ic.Add(Arc.Meter("进", inst.drift_progress, Pct(inst.drift_progress), Steward));
            }
            var il = Arc.ListBlock();
            if (inst?.enacted_node_ids != null) foreach (var n in Enumerable.Reverse(inst.enacted_node_ids).Take(4))
                {
                    string when = inst.enacted_timestamps != null && inst.enacted_timestamps.TryGetValue(n, out double s) ? "第 " + YearText(w, (int)s) + " 年" : "";
                    Arc.ListRow(il, Safe(() => InstitutionSystem.GetNodeName(InstitutionDefinitionRegistry.Get(n), id), n), null, when);
                }
            if (il.childCount == 0) Arc.ListRow(il, "尚无制度", null, null);
            ic.Add(il);
            ic.Add(Arc.Verdict(inst?.enacted_node_ids == null ? "—" : "已行 " + inst.enacted_node_ids.Count + " 项制度"));
            Arc.GridAdd(grid, ic);
            var kin = w.LiveRealms.Where(z => z.Alive && CultureId(z) == id).OrderByDescending(z => z.Cities.Count).ToList();
            var kc = Arc.InfoCard("同族之国", "civ_nations", Arc.Badge(kin.Count + " 国", "m"));
            var kl = Arc.ListBlock();
            foreach (var z in kin.Take(6)) { var zz = z; Arc.ListRow(kl, z.Name, z == r ? Arc.Badge("本国", "y") : null, z.Cities.Count + " 城", null, Hud.Rgb(z.Color), null, () => OpenCoreState(zz)); }
            kc.Add(kl);
            kc.Add(Arc.Verdict(kin.Count <= 1 ? "同族只此一国" : "同族 " + kin.Count + " 国共享科技与制度"));
            Arc.GridAdd(grid, kc);
            var all = w.LiveRealms.Where(z => z.Alive).Select(z => CultureId(z)).Distinct().Select(c => SafeValue(() => (double)TechnologySystem.GetState(c).last_yearly_points, 0d)).ToList();
            double mine = tech == null ? 0 : tech.last_yearly_points, avg = all.Count == 0 ? 0 : all.Average();
            var speed = Arc.InfoCard("研究速度", "iconHistory", mine >= avg ? Arc.Badge("快于天下", "g") : Arc.Badge("慢于天下", "o"));
            speed.Add(Arc.Legend((Hud.Rgb(r.Color), CultureOf(r)), (new Color32(120, 110, 96, 255), "各族平均")));
            speed.Add(Arc.Tug("TabInstitutions", mine, avg, mine.ToString("0"), avg.ToString("0"), Hud.Rgb(r.Color), new Color32(120, 110, 96, 255)));
            speed.Add(Arc.Verdict(avg <= 0 ? "—" : "年研究点是各族平均的 " + (mine / avg).ToString("0.0") + " 倍"));
            Arc.GridAdd(grid, speed);
            page.Add(grid);
            page.Add(Arc.Note("· 科技与制度按民族共享；点同族之国看它的国务"));
        }

        // ---------- 战争 · 概况（一场） ----------
        void BuildWarOverviewV5(EmpireWorld w, War v, VisualElement page)
        {
            var att = Side(v, true).Where(r => r.Alive).ToList(); var def = Side(v, false).Where(r => r.Alive).ToList();
            double aMen = att.Sum(r => (double)SafeValue(() => r.Warriors(), 0)), dMen = def.Sum(r => (double)SafeValue(() => r.Warriors(), 0));
            int aCities = att.Sum(r => r.Cities.Count), dCities = def.Sum(r => r.Cities.Count), taken = CitiesTaken(w, v);
            Color ac = v.MainAttacker != null ? Hud.Rgb(v.MainAttacker.Color) : Color.red, dc = v.MainDefender != null ? Hud.Rgb(v.MainDefender.Color) : Color.cyan;
            page.Add(Arc.PageHead("概况", WarName(v) + "：谁占上风"));
            page.Add(Arc.KpiRow(("兵力比", dMen > 0 ? (aMen / dMen).ToString("0.0") + " : 1" : "—", aMen >= dMen ? "r" : "c", true), ("城池比", aCities + " : " + dCities, null, false),
                ("易手", taken + " 城", taken > 0 ? "o" : null, false), (v.HasEnded() ? "历时" : "已打", Math.Max(0, v.DurationMonths() / 12) + " 年", null, false)));
            var grid = Arc.Grid(2);
            var aSet = new HashSet<Realm>(att); var dSet = new HashSet<Realm>(def);
            var map = Arc.InfoCard("战场", "iconTitleLayer", Arc.Badge(v.HasEnded() ? Outcome(v) : "进行中", v.HasEnded() ? "m" : "r"));
            map.Add(AimedMap(ref warMap, ref warMapWorld, ref warMapAim, w, v, att.Concat(def).SelectMany(r => r.Cities.SelectMany(c => c.Tiles)), .3f,
                t => { int c = w.TileCity[t]; if (c < 0) return -1; var z = w.Cities[c].Realm; return z != null && z.Alive ? z.Id : -1; },
                id => { var z = w.Realms[id]; return new MapPicker.Look { Fill = aSet.Contains(z) ? (Color32)ac : dSet.Contains(z) ? (Color32)dc : Dim, Label = aSet.Contains(z) || dSet.Contains(z) ? z.Name : null, Clickable = true }; },
                id => FocusCoreRealm(w.Realms[id])).Root);
            map.Add(Arc.Legend((ac, "攻方"), (dc, "守方"), (Dim, "旁观")));
            map.Add(Arc.Verdict(att.Count + " 国攻 " + def.Count + " 国"));
            Arc.GridAdd(grid, map);
            var tug = Arc.InfoCard("双方", "TabMilitary", aMen >= dMen * 1.4 ? Arc.Badge("攻方占优", "r") : dMen >= aMen * 1.4 ? Arc.Badge("守方占优", "c") : Arc.Badge("相持", "m"));
            tug.Add(Arc.Legend((ac, "攻 " + (v.MainAttacker?.Name ?? "?")), (dc, "守 " + (v.MainDefender?.Name ?? "?"))));
            tug.Add(Arc.Tug("TabMilitary", aMen, dMen, Arc.Count(aMen), Arc.Count(dMen), ac, dc));
            tug.Add(Arc.Tug("iconCity", aCities, dCities, aCities.ToString(), dCities.ToString(), ac, dc));
            double aPop = att.Sum(r => (double)SafeValue(() => r.PopulationCount(), 0)), dPop = def.Sum(r => (double)SafeValue(() => r.PopulationCount(), 0));
            tug.Add(Arc.Tug("iconPopulation50", aPop, dPop, Arc.Count(aPop), Arc.Count(dPop), ac, dc));
            tug.Add(Arc.Verdict(aMen >= dMen * 1.4 ? "攻方兵多，守方难支" : dMen >= aMen * 1.4 ? "守方兵多，攻方难下" : "兵力相近，胜负难料"));
            Arc.GridAdd(grid, tug);
            var goal = SafeValue(() => WarGoalSystem.Get(v), WarGoal.None);
            var gc = Arc.InfoCard("目标", "chess_rook", Arc.Badge(goal == WarGoal.None ? WarKindName(v) : Safe(() => WarGoalSystem.Name(goal)), "r"));
            var gl = Arc.ListBlock();
            Arc.ListRow(gl, GoalNote(goal), null, null);
            Arc.ListRow(gl, "开战", null, "第 " + YearText(w, v.StartedMonth) + " 年");
            if (v.HasEnded()) Arc.ListRow(gl, "结局", Arc.Badge(Outcome(v), "m"), "第 " + YearText(w, v.EndedMonth) + " 年");
            gc.Add(gl);
            gc.Add(Arc.Verdict(v.HasEnded() ? "已罢兵：" + Outcome(v) : taken > 0 ? "已易手 " + taken + " 城" : "尚无城池易手"));
            Arc.GridAdd(grid, gc);
            var rep = Arc.InfoCard("战报", "iconHistory", Arc.Badge("新的在上", "m"));
            var rl = Arc.ListBlock();
            var lines = WarReport(w, v).Take(5).ToList();
            foreach (var e in lines) Arc.ListRow(rl, AnnalText(w, e), null, "第 " + YearText(w, e.Month) + " 年");
            if (lines.Count == 0) Arc.ListRow(rl, "尚无战报", null, null);
            rep.Add(rl);
            rep.Add(Arc.Verdict(lines.Count == 0 ? "—" : "最近：" + AnnalText(w, lines[0])));
            Arc.GridAdd(grid, rep);
            page.Add(grid);
            page.Add(Arc.Note("· 点图上的国，地图对准它；左栏看双方与全部战报"));
        }

        VisualElement SideCardV5(EmpireWorld w, War v, bool attackers)
        {
            var side = Side(v, attackers);
            double men = side.Where(r => r.Alive).Sum(r => (double)SafeValue(() => r.Warriors(), 0));
            var card = Arc.InfoCard(attackers ? "攻方" : "守方", attackers ? "TabWarfare" : "chess_rook", Arc.Badge(side.Count + " 国", attackers ? "r" : "c"));
            var list = Arc.ListBlock();
            Arc.ListHead(list, "国", "城", "兵");
            foreach (var r in side)
            {
                var z = r;
                bool main = r == (attackers ? v.MainAttacker : v.MainDefender);
                bool left = !(attackers ? v.Attackers : v.Defenders).Contains(r);
                var row = Arc.ListRow(list, r.Name, main ? Arc.Badge(attackers ? "主攻" : "主守", attackers ? "r" : "c") : left ? Arc.Badge(r.Alive ? "已退出" : "已亡", "m") : null, null, null, Hud.Rgb(r.Color), null,
                    r.Alive ? () => OpenCoreState(z) : (Action)null);
                row.Add(Arc.Col(r.Alive ? r.Cities.Count.ToString() : "—")); row.Add(Arc.Col(r.Alive ? Arc.Count(SafeValue(() => r.Warriors(), 0)) : "—"));
            }
            card.Add(list);
            var lead = attackers ? v.MainAttacker : v.MainDefender;
            card.Add(Arc.Verdict(lead?.Ruler != null && lead.Ruler.Alive ? "主帅之君 " + w.FullName(lead.Ruler) + "，武 " + lead.Ruler.Warfare : "共 " + Arc.Count(men) + " 兵"));
            return card;
        }

        void BuildWarSidesV5(EmpireWorld w, War v, VisualElement page)
        {
            var att = Side(v, true); var def = Side(v, false);
            page.Add(Arc.PageHead("双方", WarName(v) + "：谁站在哪边"));
            page.Add(Arc.KpiRow(("攻方", att.Count + " 国", "r", false), ("守方", def.Count + " 国", "c", false),
                ("攻方兵", Arc.Count(att.Where(r => r.Alive).Sum(r => (double)SafeValue(() => r.Warriors(), 0))), null, true), ("守方兵", Arc.Count(def.Where(r => r.Alive).Sum(r => (double)SafeValue(() => r.Warriors(), 0))), null, false)));
            var grid = Arc.Grid(2);
            Arc.GridAdd(grid, SideCardV5(w, v, true));
            Arc.GridAdd(grid, SideCardV5(w, v, false));
            page.Add(grid);
            page.Add(Arc.Note("· 点一国看它的国务"));
        }

        void BuildWarReportsV5(EmpireWorld w, War v, VisualElement page)
        {
            var lines = WarReport(w, v).ToList();
            int moved = lines.Count(e => e.Kind == AnnalKind.CityTransferred), thrones = lines.Count(e => AnnalGroup(e.Kind) == 1);
            page.Add(Arc.PageHead("战报", WarName(v) + "：打成了什么样"));
            page.Add(Arc.KpiRow(("战报", lines.Count + " 条", null, false), ("易手", moved + " 城", moved > 0 ? "o" : null, true), ("君位变动", thrones + " 次", null, false), ("开战", "第 " + YearText(w, v.StartedMonth) + " 年", null, false)));
            var card = Arc.InfoCard("编年", "iconHistory", Arc.Badge("新的在上", "m"));
            var list = Arc.ListBlock();
            string[] tags = { "", "君位", "兴亡", "城池", "战事" };
            foreach (var e in lines.Take(120)) { int g = AnnalGroup(e.Kind); Arc.ListRow(list, MonthName(e.Month) + "　" + AnnalText(w, e), Arc.Badge(tags[g], AnnalTones[g] ?? "m"), "第 " + YearText(w, e.Month) + " 年"); }
            if (lines.Count == 0) Arc.ListRow(list, "尚无战报", null, null);
            card.Add(list);
            card.Add(Arc.Verdict(moved > 0 ? "共易手 " + moved + " 城" : "尚无城池易手"));
            page.Add(card);
        }

        void BuildWarPastV5(EmpireWorld w, VisualElement page, ArchiveWindow x)
        {
            var ended = w.Wars.Where(z => z != null && z.HasEnded()).OrderByDescending(z => z.EndedMonth).ToList();
            int att = ended.Count(z => z.Outcome == WarOutcome.Attackers), def = ended.Count(z => z.Outcome == WarOutcome.Defenders), peace = ended.Count(z => z.Outcome == WarOutcome.Peace);
            page.Add(Arc.PageHead("往事", "打过哪些仗，结局如何"));
            page.Add(Arc.KpiRow(("已结束", ended.Count + " 场", null, false), ("攻方胜", att + " 场", "r", false), ("守方胜", def + " 场", "c", false), ("议和", peace + " 场", "g", true)));
            var card = Arc.InfoCard("近年战争", "iconHistory", Arc.Badge("最近 60 场", "m"));
            var list = Arc.ListBlock();
            foreach (var war in ended.Take(60))
            {
                var v = war;
                Arc.ListRow(list, WarName(war), Arc.Badge(Outcome(war), war.Outcome == WarOutcome.Attackers ? "r" : war.Outcome == WarOutcome.Defenders ? "c" : "m"),
                    YearText(w, war.StartedMonth) + "—" + YearText(w, war.EndedMonth) + " 年", null, null, null, () => { activeWar = v; x.Go("overview"); });
            }
            if (ended.Count == 0) Arc.ListRow(list, "尚无已结束的战争", null, null);
            card.Add(list);
            card.Add(Arc.Verdict(ended.Count == 0 ? "—" : att >= def ? "攻方多胜，天下尚武" : "守方多胜，攻城不易"));
            page.Add(card);
            page.Add(Arc.Note("· 点一场看它的概况"));
        }

        // ---------- 宗族 ----------
        void BuildClanMembersV5(EmpireWorld w, Clan c, VisualElement page, ArchiveWindow x)
        {
            var living = w.Living.Where(p => p.Clan == c).OrderByDescending(p => p.Renown).ThenBy(p => p.BirthMonth).ToList();
            page.Add(Arc.PageHead("族人", c.Surname + "氏有哪些人"));
            page.Add(Arc.KpiRow(("在世", living.Count + " 人", null, true), ("男", living.Count(p => p.Sex == Sex.Male) + " 人", null, false),
                ("女", living.Count(p => p.Sex != Sex.Male) + " 人", null, false), ("有职", living.Count(p => p.IsRuler() || p.IsCityLeader()) + " 人", "y", false)));
            var card = Arc.InfoCard("名录", "iconPopulation50", Arc.Badge("按名望", "m"));
            var list = Arc.ListBlock();
            Arc.ListHead(list, "名字", "治", "武", "智", "名望");
            foreach (var p in living.Take(120))
            {
                var person = p;
                var row = Arc.ListRow(list, w.FullName(p) + "  " + w.AgeOf(p) + "岁", p.IsRuler() ? Arc.Badge("君主", "y") : p.IsCityLeader() ? Arc.Badge("城主", "c") : null, null, null, null, p.IsRuler() ? "y" : null,
                    () => { activePerson = person; x.Go("kin"); });
                row.Add(Arc.Col(p.Stewardship.ToString())); row.Add(Arc.Col(p.Warfare.ToString())); row.Add(Arc.Col(p.Intelligence.ToString())); row.Add(Arc.Col(p.Renown.ToString()));
            }
            if (living.Count == 0) Arc.ListRow(list, "族中已无在世之人", null, null);
            card.Add(list);
            var able = living.OrderByDescending(p => p.Stewardship + p.Warfare + p.Intelligence).FirstOrDefault();
            card.Add(Arc.Verdict(able == null ? "—" : "族中才具最高：" + w.FullName(able) + "（" + Strong(able) + "）"));
            page.Add(card);
            page.Add(Arc.Note("· 点人看他的亲属"));
        }

        void BuildClanKinV5(EmpireWorld w, Clan c, VisualElement page, ArchiveWindow x)
        {
            var p = activePerson;
            if (p == null || (p.Clan != c && p.Clan != null)) p = activePerson = SafeValue(() => c.GetChief(), null);
            if (p == null) { page.Add(Arc.Empty("无可显示的人物。")); return; }
            page.Add(Arc.PageHead(w.FullName(p), "他的亲属与才具"));
            page.Add(Arc.KpiRow(("年岁", p.Alive ? w.AgeOf(p) + " 岁" : "已故", null, false), ("名望", p.Renown.ToString(), "y", true),
                ("子女", p.Children.Count + " 人", null, false), ("身份", Role(w, p), null, false)));
            var grid = Arc.Grid(2);
            var me = Arc.InfoCard("本人", "famous_talents", p.IsRuler() ? Arc.Badge("君主", "y") : p.IsCityLeader() ? Arc.Badge("城主", "c") : Arc.Badge(p.Alive ? "在世" : "已故", "m"));
            me.Add(Arc.Note(YearText(w, p.BirthMonth) + " 年生" + (p.Alive ? "" : " · " + YearText(w, p.DeathMonth) + " 年卒")));
            PersonMeters(me, p);
            me.Add(Arc.Verdict(Strong(p)));
            Arc.GridAdd(grid, me);
            void Kin(VisualElement list, Person q, string rel)
            {
                if (q == null) return; var t = q;
                Arc.ListRow(list, w.FullName(q), Arc.Badge(rel, q.IsRuler() ? "y" : "m"), q.Alive ? w.AgeOf(q) + " 岁" : "已故", null, null, null, () => { activePerson = t; activeClan = t.Clan ?? activeClan; x.Go("kin"); });
            }
            var family = Arc.InfoCard("父母与配偶", "TabDynasty");
            var fl = Arc.ListBlock();
            Kin(fl, p.Father, "父"); Kin(fl, p.Mother, "母"); Kin(fl, p.Spouse, p.Sex == Sex.Male ? "妻" : "夫");
            if (fl.childCount == 0) Arc.ListRow(fl, "无在册亲属", null, null);
            family.Add(fl);
            family.Add(Arc.Verdict(p.Spouse == null ? "尚无配偶" : "配偶" + w.FullName(p.Spouse)));
            Arc.GridAdd(grid, family);
            var kids = Arc.InfoCard("子女", "iconPopulation50", Arc.Badge(p.Children.Count + " 人", "m"));
            var kl = Arc.ListBlock();
            int n = 0;
            foreach (var child in p.Children.OrderBy(z => z.BirthMonth).Take(6)) { n++; Kin(kl, child, child.Sex == Sex.Male ? (n == 1 ? "长子" : "子") : "女"); }
            if (kl.childCount == 0) Arc.ListRow(kl, "无子女", null, null);
            kids.Add(kl);
            kids.Add(Arc.Verdict(p.Children.Count == 0 ? "后继无人" : "有子女 " + p.Children.Count + " 人"));
            Arc.GridAdd(grid, kids);
            var sibs = (p.Father ?? p.Mother)?.Children.Where(z => z != p).OrderBy(z => z.BirthMonth).ToList() ?? new List<Person>();
            var sc = Arc.InfoCard("同辈", "specificClanIcon", Arc.Badge(sibs.Count + " 人", "m"));
            var sl = Arc.ListBlock();
            foreach (var s in sibs.Take(6)) Kin(sl, s, s.BirthMonth < p.BirthMonth ? (s.Sex == Sex.Male ? "兄" : "姊") : (s.Sex == Sex.Male ? "弟" : "妹"));
            if (sl.childCount == 0) Arc.ListRow(sl, "独子", null, null);
            sc.Add(sl);
            sc.Add(Arc.Verdict(sibs.Count == 0 ? "无兄弟姊妹" : "兄弟姊妹 " + sibs.Count + " 人"));
            Arc.GridAdd(grid, sc);
            page.Add(grid);
            page.Add(Arc.Note("· 点亲属换作他"));
        }

        void BuildClanHousesV5(EmpireWorld w, VisualElement page, ArchiveWindow x)
        {
            var living = new Dictionary<Clan, int>();
            foreach (var p in w.Living) if (p.Clan != null) { living.TryGetValue(p.Clan, out int n); living[p.Clan] = n + 1; }
            var ruling = new Dictionary<Clan, Realm>();
            foreach (var r in w.LiveRealms) if (r.Alive && r.RulingClan != null) ruling[r.RulingClan] = r;
            var clans = living.Keys.OrderBy(c => ruling.ContainsKey(c) ? 0 : 1).ThenByDescending(c => living[c]).ThenBy(c => c.Id).ToList();
            var top = clans.Count == 0 ? null : clans.OrderByDescending(c => living[c]).First();
            page.Add(Arc.PageHead("天下宗族", "哪些家族最显赫"));
            page.Add(Arc.KpiRow(("宗族", clans.Count + " 族", null, false), ("在册人物", w.Living.Count.ToString("N0"), null, false), ("在位王室", ruling.Count + " 家", "y", true),
                ("最大宗族", top == null ? "—" : top.Surname + "氏", null, false)));
            var grid = Arc.Grid(2);
            var gate = Arc.InfoCard("门第", "specificClanIcon", Arc.Badge("王室在前", "m"));
            var gl = Arc.ListBlock();
            foreach (var c in clans.Take(10))
            {
                var cc = c; ruling.TryGetValue(c, out var throne);
                Arc.ListRow(gl, c.Surname + "氏", throne != null ? Arc.Badge(throne.Name + "王室", "y") : null, living[c] + " 人", null, throne != null ? Hud.Rgb(throne.Color) : (Color?)null, null,
                    () => { activeClan = cc; activePerson = null; x.Go("overview"); });
            }
            if (clans.Count == 0) Arc.ListRow(gl, "尚无在世的宗族", null, null);
            gate.Add(gl);
            gate.Add(Arc.Verdict(top == null ? "—" : top.Surname + "氏人丁最旺（" + living[top] + " 人）"));
            Arc.GridAdd(grid, gate);
            var people = w.Living.OrderByDescending(p => p.Renown).Take(10).ToList();
            var fame = Arc.InfoCard("名望榜", "famous_talents", Arc.Badge("天下前十", "m"));
            var fl = Arc.ListBlock();
            foreach (var p in people)
            {
                var pp = p;
                Arc.ListRow(fl, w.FullName(p), p.IsRuler() ? Arc.Badge("君主", "y") : p.IsCityLeader() ? Arc.Badge("城主", "c") : null, p.Renown.ToString(), null, null, null,
                    p.Clan != null ? () => { activeClan = pp.Clan; activePerson = pp; x.Go("kin"); } : (Action)null);
            }
            fame.Add(fl);
            fame.Add(Arc.Verdict(people.Count == 0 ? "—" : "名望第一：" + w.FullName(people[0])));
            Arc.GridAdd(grid, fame);
            page.Add(grid);
            page.Add(Arc.Note("· 点族看它的总览，点人看他的亲属"));
        }

        // ---------- 史书 · 天下大事 ----------
        void BuildNewsV5(EmpireWorld w, VisualElement page, Realm realm)
        {
            var items = w.News.Items;
            var shown = new List<NewsItem>();
            for (int i = items.Count - 1; i >= 0 && shown.Count < 120; i--)
            {
                var item = items[i];
                if (annalsRealmOnly && realm != null && item.RealmId != realm.Id) continue;
                shown.Add(item);
            }
            int year = YearOf(w, w.Month);
            page.Add(Arc.PageHead("天下大事", "最近天下在传什么"));
            page.Add(Arc.KpiRow(("今年", shown.Count(z => YearOf(w, z.Month) == year) + " 条", null, false), ("上屏", shown.Count(z => z.OnScreen) + " 条", "y", true),
                ("近百条", shown.Count + " 条", null, false), ("本国", realm == null ? "—" : items.Count(z => z.RealmId == realm.Id) + " 条", null, false)));
            var card = Arc.InfoCard("近事", "TabEmpire", Arc.Badge("新的在上", "m"));
            var list = Arc.ListBlock();
            foreach (var item in shown)
            {
                var target = item.RealmId >= 0 ? w.RealmByKey(item.RealmId) : null;
                Arc.ListRow(list, MonthName(item.Month) + "　" + item.Text, item.OnScreen ? Arc.Badge("大事", "y") : null, "第 " + YearText(w, item.Month) + " 年", null, null, null,
                    target != null ? () => FocusCoreRealm(target) : (Action)null);
            }
            if (shown.Count == 0) Arc.ListRow(list, "尚无大事", null, null);
            card.Add(list);
            card.Add(Arc.Verdict(shown.Count == 0 ? "—" : "最新：" + shown[0].Text));
            page.Add(card);
            page.Add(Arc.Note("· 点一条跳到相关的国；左栏可只看本国"));
        }
    }
}
