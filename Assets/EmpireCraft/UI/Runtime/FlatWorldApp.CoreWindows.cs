using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Core;
using EmpireCraft.UI;
using UnityEngine.UIElements;
using Action = System.Action;

namespace Orbis.Flat
{
    // 卷册 archive windows on the EmpireCraft core, laid out after the mod's wide windows
    // (empire list, empire home and finance, war, house, chronicle): a fixed left column
    // with the identity card, people, key figures and the page menu, and a scrolling page.
    // The selection (realm, war, house, person) and the filters live here, so a window
    // rebuilt every second keeps its place.
    public sealed partial class FlatWorldApp
    {
        War activeWar; Clan activeClan; Person activePerson;
        string nationsCulture; int nationsSort; bool nationsFallen;
        bool financeYear = true;
        int annalsKind; bool annalsRealmOnly; bool warListPast;

        // ---------- shared ----------
        static string Pct(float v) => (v * 100).ToString("0") + "%";
        static string Tone(float v, float good, float bad) => v >= good ? "g" : v <= bad ? "r" : "o";

        static VisualElement NoCore(EmpireWorld w, VisualElement into, string text = null)
        {
            into.Add(Arc.Empty(text ?? "列国尚在立国，稍候即可。"));
            return into;
        }

        string Talents(Person p) => "治" + p.Stewardship + " 武" + p.Warfare + " 智" + p.Intelligence + " 交" + p.Diplomacy;

        string Role(EmpireWorld w, Person p)
        {
            if (p.IsRuler()) return p.Throne.Name + "君主";
            if (p.IsCityLeader()) return p.City.Name + "城主";
            return p.Realm != null ? p.Realm.Name + "人" : "布衣";
        }

        // Month the ruler came to the throne: the last accession the chronicle has for the realm.
        static int ReignStart(EmpireWorld w, Realm r)
        {
            var e = w.Annals.Entries;
            for (int i = e.Count - 1; i >= 0; i--)
                if ((e[i].Kind == AnnalKind.Accession || e[i].Kind == AnnalKind.NewDynasty) && e[i].A == r.Id) return e[i].Month;
            return r.FoundedMonth;
        }

        static string RulerNote(EmpireWorld w, Realm r)
        {
            var k = r.Ruler;
            if (k == null || !k.Alive) return "君位空悬";
            return w.AgeOf(k) + " 岁 · 在位 " + Math.Max(0, (w.Month - ReignStart(w, r)) / 12) + " 年 · 名望 " + k.Renown;
        }

        VisualElement RulerRow(EmpireWorld w, Realm r, bool talents)
        {
            var k = r.Ruler;
            if (k == null || !k.Alive) return Arc.Person("civ_crown", "君主", "君位空悬", null);
            return Arc.Person("civ_crown", "君主", w.FullName(k), talents ? w.AgeOf(k) + " 岁 · " + Talents(k) : RulerNote(w, r), () => OpenCoreClan(k.Clan, k));
        }

        static float AverageStability(EmpireWorld w, Realm r)
        {
            float sum = 0; int n = 0;
            foreach (var c in r.Cities) if (c.Alive) { sum += w.Ledger.City(c.Id).Stability; n++; }
            return n == 0 ? 0 : sum / n;
        }

        // Realms an entry of the chronicle is about (for the war report and the per-realm filter).
        static IEnumerable<int> RealmsOf(AnnalEntry e)
        {
            switch (e.Kind)
            {
                case AnnalKind.RealmFounded: case AnnalKind.RealmEnded: case AnnalKind.Accession: case AnnalKind.RulerDied:
                case AnnalKind.CapitalMoved: case AnnalKind.NewDynasty: yield return e.A; break;
                case AnnalKind.CityFounded: yield return e.B; break;
                case AnnalKind.CityTransferred: case AnnalKind.WarStarted: case AnnalKind.AllianceFormed: yield return e.B; yield return e.C; break;
                case AnnalKind.Secession: yield return e.A; yield return e.C; break;
            }
        }

        // 0 all, 1 thrones and houses, 2 founding and fall, 3 cities, 4 war and alliance.
        static int AnnalGroup(AnnalKind k)
        {
            switch (k)
            {
                case AnnalKind.Accession: case AnnalKind.RulerDied: case AnnalKind.NewDynasty: return 1;
                case AnnalKind.RealmFounded: case AnnalKind.RealmEnded: case AnnalKind.Secession: return 2;
                case AnnalKind.CityFounded: case AnnalKind.CityTransferred: case AnnalKind.CapitalMoved: return 3;
                default: return 4;
            }
        }
        static readonly string[] AnnalTones = { null, "r", "y", "g", "o" };
        static readonly string[] AnnalGroupNames = { "全部", "易姓 · 君主", "立国 · 亡国", "筑城 · 城池", "战争 · 盟约" };

        // The chronicle line without its date (the window prints the month itself).
        static string AnnalText(EmpireWorld w, AnnalEntry e)
        {
            string s = Safe(() => Annals.Format(w, e));
            int cut = s.IndexOf('，');
            return cut >= 0 && cut < 12 ? s.Substring(cut + 1) : s;
        }

        // Chronicle lines grouped by year, newest first.
        void AddAnnalLines(EmpireWorld w, VisualElement card, IEnumerable<AnnalEntry> entries, int limit)
        {
            int year = int.MinValue, shown = 0;
            foreach (var e in entries)
            {
                if (shown++ >= limit) break;
                int y = YearOf(w, e.Month);
                if (y != year) { year = y; card.Add(Arc.Year(y + " 年")); }
                int target = RealmsOf(e).FirstOrDefault(id => id >= 0 && id < w.Realms.Count && w.Realms[id].Alive);
                var realm = target >= 0 && target < w.Realms.Count && w.Realms[target].Alive ? w.Realms[target] : null;
                card.Add(Arc.Event(MonthName(e.Month), AnnalText(w, e), AnnalTones[AnnalGroup(e.Kind)], realm != null ? () => FocusCoreRealm(realm) : (Action)null));
            }
        }

        static string WarName(War war) =>
            !string.IsNullOrEmpty(war.Name) ? war.Name : (war.MainAttacker != null ? war.MainAttacker.Name : "?") + "与" + (war.MainDefender != null ? war.MainDefender.Name : "?") + "之战";
        static string WarKindName(War war) =>
            war.Kind == WarKinds.Rebellion ? "叛乱" : war.Kind == WarKinds.Inspire ? "起事" : war.Kind == WarKinds.WhisperOfWar ? "挑唆" : "征伐";
        static string Outcome(War war)
        {
            switch (war.Outcome)
            {
                case WarOutcome.Attackers: return "攻方胜";
                case WarOutcome.Defenders: return "守方胜";
                case WarOutcome.Peace: return "议和";
                case WarOutcome.Merged: return "并入他战";
                default: return "不了了之";
            }
        }

        // =====================================================================
        // 列国榜 (the mod's empire list): peoples and filters on the left, headline
        // figures and realm cards grouped by people on the right.
        void OpenCoreNations()
        {
            var win = new ArchiveWindow("列国榜", "empire_list", CloseHudWindow, "list");
            win.Rail = (x, rail) =>
            {
                var w = CoreHere(); if (w == null) { NoCore(w, rail); return; }
                int live = 0, dead = 0; foreach (var r in w.Realms) if (r.Alive) live++; else dead++;
                rail.Add(Arc.IdCard("天下列国", "第 " + w.Year + " 年 · 现存 " + live + " · 已亡 " + dead));
                var show = Arc.Card();
                show.Add(Arc.MenuItem("civ_nations", "现存列国", live.ToString(), !nationsFallen, () => { nationsFallen = false; x.Refresh(); }));
                show.Add(Arc.MenuItem("iconHistory", "已亡之国", dead.ToString(), nationsFallen, () => { nationsFallen = true; x.Refresh(); }));
                rail.Add(show);
                var peoples = new Dictionary<string, int>();
                foreach (var r in w.LiveRealms) if (r.Alive) { string id = CultureId(r) ?? ""; peoples.TryGetValue(id, out int n); peoples[id] = n + 1; }
                var by = Arc.Card(); by.Add(Arc.MenuHead("按民族"));
                by.Add(Arc.MenuItem(null, "全部民族", live.ToString(), nationsCulture == null, () => { nationsCulture = null; x.Refresh(); }));
                foreach (var kv in peoples.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
                {
                    string id = kv.Key;
                    by.Add(Arc.MenuItem(null, CultureName(id), kv.Value.ToString(), nationsCulture == id, () => { nationsCulture = id; x.Refresh(); }));
                }
                rail.Add(by);
                var sort = Arc.Card(); sort.Add(Arc.MenuHead("排序"));
                string[] sorts = { "人口", "城池", "立国" };
                for (int i = 0; i < sorts.Length; i++) { int k = i; sort.Add(Arc.MenuItem(null, sorts[i], null, nationsSort == k, () => { nationsSort = k; x.Refresh(); })); }
                rail.Add(sort);
            };
            win.Main = (x, page) =>
            {
                var w = CoreHere(); if (w == null) { NoCore(w, page); return; }
                if (nationsFallen) { BuildFallenRealms(w, page); return; }
                var realms = w.LiveRealms.Where(r => r.Alive && (nationsCulture == null || (CultureId(r) ?? "") == nationsCulture)).ToList();
                var pop = realms.ToDictionary(r => r, r => SafeValue(() => r.PopulationCount(), 0));
                Comparison<Realm> order = nationsSort == 1 ? (a, b) => b.Cities.Count != a.Cities.Count ? b.Cities.Count.CompareTo(a.Cities.Count) : pop[b].CompareTo(pop[a])
                    : nationsSort == 2 ? (a, b) => a.FoundedMonth != b.FoundedMonth ? a.FoundedMonth.CompareTo(b.FoundedMonth) : a.Id.CompareTo(b.Id)
                    : (Comparison<Realm>)((a, b) => pop[b] != pop[a] ? pop[b].CompareTo(pop[a]) : a.Id.CompareTo(b.Id));
                realms.Sort(order);
                int cities = realms.Sum(r => r.Cities.Count(c => c.Alive));
                int oldest = realms.Count == 0 ? 0 : realms.Max(r => r.GetAge());
                int wars = realms.Count(r => r.Wars.Count > 0);
                page.Add(Arc.Head(nationsCulture == null ? "现存列国" : CultureName(nationsCulture) + "诸国", realms.Count + " 国"));
                page.Add(Arc.Kpis(("辖城总数", cities.ToString("N0"), "g"), ("人口总数", Arc.Count(pop.Values.Sum(v => (double)v)), "c"),
                    ("最久立国", oldest + " 年", "y"), ("交战之国", wars.ToString(), wars > 0 ? "r" : null)));
                if (realms.Count == 0) { page.Add(Arc.Empty("没有符合的国家。")); return; }
                // Grouped by people in the order of their first realm, two cards a row.
                foreach (var group in realms.GroupBy(r => CultureId(r) ?? ""))
                {
                    var list = group.ToList();
                    if (nationsCulture == null) page.Add(Arc.Group(CultureName(group.Key), "×" + list.Count));
                    for (int i = 0; i < list.Count; i += 2)
                    {
                        var row = Arc.Cols();
                        for (int j = i; j < i + 2; j++)
                        {
                            if (j >= list.Count) { row.Add(new VisualElement()); continue; }
                            var r = list[j];
                            var books = w.Ledger.Realm(r.Id);
                            string note = RegimeOf(r) + (r.Wars.Count > 0 ? " · 交战中" : "") + (SafeValue(() => r.IsInEmpire(), false) ? " · 帝国" : "");
                            row.Add(Arc.RealmCard(r.Color, r.Name, note, RulerRow(w, r, false),
                                new[] { ("城", r.Cities.Count.ToString(), "g"), ("人口", Arc.Count(pop[r]), "c"),
                                        ("正统", books.Legitimacy.ToString(), books.Legitimacy < 50 ? "r" : "y"), ("立国", r.GetAge() + "年", (string)null) },
                                () => { FocusCoreRealm(r); OpenCoreState(r); }, r == activeRealm));
                        }
                        page.Add(row);
                    }
                }
                page.Add(Arc.Note("点国家卡：地图对准都城，打开国务。"));
            };
            OpenHudWindow("nations", win);
        }

        void BuildFallenRealms(EmpireWorld w, VisualElement page)
        {
            var fallen = w.Realms.Where(r => !r.Alive).OrderByDescending(r => r.EndedMonth).ToList();
            page.Add(Arc.Head("已亡之国", fallen.Count + " 国"));
            if (fallen.Count == 0) { page.Add(Arc.Empty("尚无亡国。")); return; }
            var card = Arc.Card("亡国录", "iconHistory", "立国 — 亡国");
            for (int i = 0; i < fallen.Count && i < 80; i++)
            {
                var r = fallen[i];
                card.Add(Arc.Line(i, null, Arc.Swatch(r.Color), Arc.Text(r.Name, "ec-label"), Arc.Fill(),
                    Arc.Text(CultureOf(r), "ec-label", "m"), Arc.Text(YearOf(w, r.FoundedMonth) + "—" + YearOf(w, r.EndedMonth) + " 年", "ec-label")));
            }
            page.Add(card);
        }

        // =====================================================================
        // 国务 (the mod's empire home): identity, ruler and house, key figures and the page
        // menu on the left; 总览 / 财政 / 城池 / 世系 / 外交战事 / 文明 pages.

        void OpenCoreState(Realm realm, string page = "overview")
        {
            if (realm != null) activeRealm = realm;
            var win = new ArchiveWindow("国务", "TabEmpire", CloseHudWindow, "overview", "finance", "cities", "lineage", "foreign", "culture");
            win.Rail = (x, rail) =>
            {
                var w = CoreHere(); var r = StateRealm(w);
                if (r == null) { NoCore(w, rail, "尚未选中国家。点地图上某国的地块，或在列国榜中点选。"); return; }
                var books = w.Ledger.Realm(r.Id);
                rail.Add(Arc.IdCard(r.Name, RegimeOf(r) + " · " + CultureOf(r), "立国 " + r.GetAge() + " 年 · 都城 " + (r.Capital != null ? r.Capital.Name : "—"),
                    r.Color, r.Name.Substring(0, 1)));
                var people = Arc.Card();
                people.Add(RulerRow(w, r, false));
                if (r.RulingClan != null)
                {
                    int living = w.Living.Count(p => p.Clan == r.RulingClan);
                    var clan = r.RulingClan;
                    people.Add(Arc.Person("specificClanIcon", "王室", clan.Surname + "氏", "在世 " + living + " 人", () => OpenCoreClan(clan, null)));
                }
                rail.Add(people);
                var key = Arc.Card();
                int k = 0;
                key.Add(Arc.Kv("人口", Arc.Count(SafeValue(() => r.PopulationCount(), 0)), k++));
                key.Add(Arc.Kv("城池", r.Cities.Count.ToString(), k++));
                key.Add(Arc.Kv("驻军", SafeValue(() => r.Warriors(), 0).ToString("N0"), k++));
                key.Add(Arc.Kv("国库", books.Treasury.ToString("N0"), k++, "y"));
                key.Add(Arc.Kv("正统", books.Legitimacy.ToString(), k++, books.Legitimacy < 50 ? "r" : "g"));
                key.Add(Arc.Kv("腐败", Pct(books.Corruption), k++, books.Corruption > .25f ? "r" : "o"));
                rail.Add(key);
                var menu = Arc.Card();
                menu.Add(Arc.MenuItem("TabEmpire", "总览", null, x.Page == "overview", () => x.Go("overview")));
                menu.Add(Arc.MenuItem("wealth_layer", "财政", null, x.Page == "finance", () => x.Go("finance")));
                menu.Add(Arc.MenuItem("place_city", "城池", r.Cities.Count.ToString(), x.Page == "cities", () => x.Go("cities")));
                menu.Add(Arc.MenuItem("TabDynasty", "世系", null, x.Page == "lineage", () => x.Go("lineage")));
                menu.Add(Arc.MenuItem("diplomacy", "外交战事", r.Wars.Count > 0 ? r.Wars.Count.ToString() : null, x.Page == "foreign", () => x.Go("foreign")));
                menu.Add(Arc.MenuItem("TabInstitutions", "文明", null, x.Page == "culture", () => x.Go("culture")));
                rail.Add(menu);
            };
            win.Main = (x, into) =>
            {
                var w = CoreHere(); var r = StateRealm(w);
                if (r == null) { NoCore(w, into, "尚未选中国家。"); return; }
                switch (x.Page)
                {
                    case "finance": BuildStateFinance(w, r, into, x); break;
                    case "cities": BuildStateCities(w, r, into); break;
                    case "lineage": BuildStateLineage(w, r, into); break;
                    case "foreign": BuildStateForeign(w, r, into); break;
                    case "culture": BuildStateCulture(w, r, into); break;
                    default: BuildStateOverview(w, r, into); break;
                }
            };
            OpenHudWindow("state", win, Math.Max(0, win.Pages.IndexOf(page)));
        }

        Realm StateRealm(EmpireWorld w)
        {
            if (w == null) return null;
            if (activeRealm == null || !activeRealm.Alive || activeRealm.World != w)
            {
                activeRealm = null;
                foreach (var x in w.LiveRealms) if (x.Alive && (activeRealm == null || x.Cities.Count > activeRealm.Cities.Count)) activeRealm = x;
            }
            return activeRealm;
        }

        void BuildStateOverview(EmpireWorld w, Realm r, VisualElement page)
        {
            var books = w.Ledger.Realm(r.Id);
            string id = CultureId(r);
            page.Add(Arc.Head("国势", null, Arc.Chip(Safe(() => TechnologySystem.GetEraName(id)), false), Arc.Chip(r.Wars.Count > 0 ? "交战中" : "无战事", r.Wars.Count > 0)));
            float stability = AverageStability(w, r);
            page.Add(Arc.Kpis(("各城平均稳定", stability.ToString("0"), Tone(stability, 70, 40)), ("正统", books.Legitimacy.ToString(), books.Legitimacy < 50 ? "r" : "y"),
                ("王朝积弊", books.Pressure.ToString("0"), books.Pressure > 60 ? "r" : "c"), ("欠饷", books.Unpaid.ToString("N0"), books.Unpaid > 0 ? "r" : null)));
            var cols = Arc.Cols();
            var ruler = Arc.Card("君主", "civ_crown", r.Ruler != null && r.Ruler.Alive ? "在位 " + Math.Max(0, (w.Month - ReignStart(w, r)) / 12) + " 年" : null);
            var k = r.Ruler;
            if (k != null && k.Alive)
            {
                ruler.Add(Arc.Person("civ_crown", null, w.FullName(k), w.AgeOf(k) + " 岁 · " + (k.Clan != null ? k.Clan.Surname + "氏 · " : "") + "名望 " + k.Renown, () => OpenCoreClan(k.Clan, k)));
                ruler.Add(Arc.Bar("治理", k.Stewardship / 19f, k.Stewardship.ToString(), "g"));
                ruler.Add(Arc.Bar("军事", k.Warfare / 19f, k.Warfare.ToString(), "r"));
                ruler.Add(Arc.Bar("智略", k.Intelligence / 19f, k.Intelligence.ToString(), "c"));
                ruler.Add(Arc.Bar("外交", k.Diplomacy / 19f, k.Diplomacy.ToString(), "o"));
            }
            else ruler.Add(Arc.Note("君位空悬。"));
            cols.Add(ruler);
            cols.Add(CultureCard(w, r, id));
            page.Add(cols);
            var list = r.Cities.Where(c => c.Alive).OrderByDescending(c => c.Population).ToList();
            var card = Arc.Card("城池", "place_city", "人口 · 稳定 · 驻军（点城去地图）");
            for (int i = 0; i < list.Count && i < 5; i++) card.Add(CityLine(w, list[i], i));
            if (list.Count > 5) card.Add(Arc.Note("另 " + (list.Count - 5) + " 城 ·「城池」页列全。"));
            page.Add(card);
        }

        VisualElement CultureCard(EmpireWorld w, Realm r, string id)
        {
            var same = w.LiveRealms.Count(x => x.Alive && CultureId(x) == id);
            var card = Arc.Card("文明", "TabInstitutions", CultureOf(r) + " · " + same + " 国");
            int k = 0;
            card.Add(Arc.Kv("时代", Safe(() => TechnologySystem.GetEraName(id)), k++, "c"));
            var tech = SafeValue(() => TechnologySystem.GetState(id), null);
            card.Add(Arc.Kv("已研科技", tech == null ? "—" : tech.researched_techs.Count + " 项", k++));
            card.Add(Arc.Kv("年研究点", tech == null ? "—" : tech.last_yearly_points.ToString("0"), k++, "g"));
            if (tech != null && !string.IsNullOrEmpty(tech.current_tech))
            {
                string name = Safe(() => TechnologySystem.GetTechName(tech.current_tech, id), tech.current_tech);
                card.Add(Arc.Kv("正在研究", name, k++, "c"));
                if (tech.progress <= 1.001f) card.Add(Arc.Bar("进度", tech.progress, Pct(tech.progress), "c"));
            }
            var inst = SafeValue(() => InstitutionSystem.GetOrCreateCultureState(id), null);
            card.Add(Arc.Kv("已行制度", inst?.enacted_node_ids == null ? "—" : inst.enacted_node_ids.Count + " 项", k++));
            if (inst != null && !string.IsNullOrEmpty(inst.drift_node_id))
            {
                string name = Safe(() => InstitutionSystem.GetNodeName(InstitutionDefinitionRegistry.Get(inst.drift_node_id), id), inst.drift_node_id);
                card.Add(Arc.Kv("正在推行", name, k++, "o"));
                if (inst.drift_progress <= 1.001f) card.Add(Arc.Bar("进度", inst.drift_progress, Pct(inst.drift_progress), "o"));
            }
            return card;
        }

        VisualElement CityLine(EmpireWorld w, City c, int index)
        {
            var acc = w.Ledger.City(c.Id);
            var cells = new List<VisualElement>();
            if (c.IsCapital()) cells.Add(Arc.Tag("都"));
            cells.Add(Arc.Text(c.Name, "ec-label"));
            cells.Add(Arc.Fill());
            cells.Add(Arc.Text(c.PopulationCount().ToString("N0") + " 人", "ec-label", "c"));
            cells.Add(Arc.Text("稳定 " + acc.Stability.ToString("0"), "ec-label", Tone(acc.Stability, 70, 40)));
            cells.Add(Arc.Text("驻 " + acc.Garrison.ToString("N0"), "ec-label"));
            return Arc.Line(index, () => FocusCoreTile(c.Tile), cells.ToArray());
        }

        void BuildStateFinance(EmpireWorld w, Realm r, VisualElement page, ArchiveWindow x)
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
            long totalIn = income.Values.Sum(), totalOut = expense.Values.Sum();
            page.Add(Arc.Head("财政", null, Arc.Chip("近一月", !financeYear, () => { financeYear = false; x.Refresh(); }), Arc.Chip("近一年", financeYear, () => { financeYear = true; x.Refresh(); })));
            page.Add(Arc.Kpis(("国库", books.Treasury.ToString("N0"), "y"), ("本月结余", (monthNet >= 0 ? "+" : "") + monthNet.ToString("N0"), monthNet >= 0 ? "g" : "r"),
                ((financeYear ? "近一年" : "近一月") + "结余", (totalIn - totalOut >= 0 ? "+" : "") + (totalIn - totalOut).ToString("N0"), totalIn >= totalOut ? "g" : "r"),
                ("欠饷", books.Unpaid.ToString("N0"), books.Unpaid > 0 ? "r" : null)));
            var cols = Arc.Cols();
            cols.Add(Breakdown("收入构成", "g", income, totalIn));
            cols.Add(Breakdown("支出构成", "o", expense, totalOut));
            page.Add(cols);
            var list = r.Cities.Where(c => c.Alive).OrderByDescending(c => c.Population).ToList();
            var card = Arc.Card("各城吏治", "governance_layer", "稳定 · 腐败 · 驻军");
            for (int i = 0; i < list.Count && i < 12; i++)
            {
                var c = list[i]; var acc = w.Ledger.City(c.Id);
                card.Add(Arc.Line(i, () => FocusCoreTile(c.Tile), Arc.Text(c.Name, "ec-label"), Arc.Fill(),
                    Arc.Text("稳定 " + acc.Stability.ToString("0"), "ec-label", Tone(acc.Stability, 70, 40)),
                    Arc.Text("腐败 " + Pct(acc.Corruption), "ec-label", acc.Corruption > .25f ? "r" : "o"),
                    Arc.Text("驻 " + acc.Garrison.ToString("N0"), "ec-label")));
            }
            page.Add(card);
            if (periods == null || periods.Count == 0) page.Add(Arc.Note("国库尚无收支记录。"));
        }

        static VisualElement Breakdown(string title, string tone, Dictionary<string, long> parts, long total)
        {
            var card = Arc.Card(title, tone == "g" ? "wealth_layer" : "market_layer", total.ToString("N0"));
            if (parts.Count == 0) { card.Add(Arc.Note("无记录。")); return card; }
            long max = Math.Max(1, parts.Values.Max());
            foreach (var kv in parts.OrderByDescending(p => p.Value).Take(8))
            {
                string name = Safe(() => { string s = LM.Get("fiscal_source_" + kv.Key); return s == "fiscal_source_" + kv.Key ? kv.Key : s; }, kv.Key);
                card.Add(Arc.Bar(name, kv.Value / (float)max, kv.Value.ToString("N0"), tone == "o" && kv.Key == "Corruption" ? "r" : tone));
            }
            return card;
        }

        void BuildStateCities(EmpireWorld w, Realm r, VisualElement page)
        {
            var list = r.Cities.Where(c => c.Alive).OrderByDescending(c => c.Population).ToList();
            double pop = list.Sum(c => c.Population);
            page.Add(Arc.Head("城池", list.Count + " 城"));
            page.Add(Arc.Kpis(("城池", list.Count.ToString(), "g"), ("人口", Arc.Count(pop), "c"), ("辖地", list.Sum(c => c.Tiles.Count).ToString("N0") + " 格", null),
                ("驻军", list.Sum(c => w.Ledger.City(c.Id).Garrison).ToString("N0"), null)));
            var card = Arc.Card("全部城池", "place_city", "人口 · 稳定 · 驻军（点城去地图）");
            for (int i = 0; i < list.Count; i++) card.Add(CityLine(w, list[i], i));
            page.Add(card);
        }

        void BuildStateLineage(EmpireWorld w, Realm r, VisualElement page)
        {
            // Rulers in order, from the chronicle's accessions and changes of house.
            var reigns = new List<(int month, int person, bool newHouse)>();
            foreach (var e in w.Annals.Entries)
                if (e.A == r.Id && (e.Kind == AnnalKind.Accession || e.Kind == AnnalKind.NewDynasty))
                {
                    if (reigns.Count > 0 && reigns[reigns.Count - 1].month == e.Month && reigns[reigns.Count - 1].person == e.B) continue;
                    reigns.Add((e.Month, e.B, e.Kind == AnnalKind.NewDynasty));
                }
            int houses = 1 + reigns.Count(x => x.newHouse);
            page.Add(Arc.Head("世系", "历代君主"));
            page.Add(Arc.Kpis(("历代", (reigns.Count + 1) + " 君", "y"), ("易姓", (houses - 1) + " 次", houses > 1 ? "r" : null),
                ("当朝", r.RulingClan != null ? r.RulingClan.Surname + "氏" : "—", "c"), ("当朝历年", Math.Max(0, (w.Month - w.Ledger.Realm(r.Id).DynastyStart) / 12) + " 年", null)));
            var cur = Arc.Card("今上", "civ_crown");
            cur.Add(RulerRow(w, r, true));
            page.Add(cur);
            var card = Arc.Card("历代", "TabDynasty", "新的在上");
            if (reigns.Count == 0) card.Add(Arc.Note("开国之君在位至今。"));
            for (int i = reigns.Count - 1, n = 0; i >= 0; i--, n++)
            {
                var (month, pid, newHouse) = reigns[i];
                var p = pid >= 0 && pid < w.People.Count ? w.People[pid] : null;
                int end = i + 1 < reigns.Count ? reigns[i + 1].month : w.Month;
                string span = YearOf(w, month) + (i + 1 < reigns.Count ? "—" + YearOf(w, end) + " 年" : " 年至今");
                var cells = new List<VisualElement> { Arc.Text(p != null ? w.FullName(p) : "?", "ec-label", i + 1 == reigns.Count ? "y" : null) };
                if (newHouse) cells.Add(Arc.Tag("易姓", "r"));
                cells.Add(Arc.Fill());
                cells.Add(Arc.Text(span + " · " + Math.Max(0, (end - month) / 12) + " 年", "ec-label", "m"));
                card.Add(Arc.Line(n, p != null && p.Clan != null ? () => OpenCoreClan(p.Clan, p) : (Action)null, cells.ToArray()));
            }
            page.Add(card);
        }

        void BuildStateForeign(EmpireWorld w, Realm r, VisualElement page)
        {
            var neighbours = new HashSet<Realm>();
            foreach (var c in r.Cities) if (c.Alive) foreach (var n in SafeValue(() => c.NeighbourRealms, new HashSet<Realm>())) if (n != null && n != r && n.Alive) neighbours.Add(n);
            page.Add(Arc.Head("外交战事"));
            page.Add(Arc.Kpis(("战事", r.Wars.Count.ToString(), r.Wars.Count > 0 ? "r" : null), ("盟约", r.HasAlliance() ? "1" : "0", "c"), ("邻国", neighbours.Count.ToString(), null)));
            var wars = Arc.Card("战事", "TabWarfare", "点战争打开战争窗口");
            if (r.Wars.Count == 0) wars.Add(Arc.Note("无战事。"));
            for (int i = 0; i < r.Wars.Count; i++)
            {
                var war = r.Wars[i];
                bool attacking = war.IsAttacker(r);
                var enemy = attacking ? war.MainDefender : war.MainAttacker;
                wars.Add(Arc.Line(i, () => OpenCoreWar(war), Arc.Tag(attacking ? "攻" : "守", attacking ? "r" : "c"), Arc.Text(WarName(war), "ec-label"), Arc.Fill(),
                    Arc.Text("对 " + (enemy != null ? enemy.Name : "—") + " · 已 " + war.AgeYears() + " 年", "ec-label", "m")));
            }
            var cols = Arc.Cols();
            cols.Add(wars);
            var ally = Arc.Card("盟约", "plot_kingdom_start_join_taken_alliance");
            var alliance = r.GetAlliance();
            if (alliance == null) ally.Add(Arc.Note("未结盟。"));
            else
            {
                ally.Add(Arc.Kv("盟名", Safe(() => alliance.Name), 0, "y"));
                int k = 1;
                foreach (var m in SafeValue(() => alliance.Members, new List<Realm>())) if (m != r && m.Alive) ally.Add(Arc.Line(k++, () => OpenCoreState(m), Arc.Swatch(m.Color), Arc.Text(m.Name, "ec-label")));
            }
            cols.Add(ally);
            page.Add(cols);
            var near = Arc.Card("邻国", "civ_nations", "好感 · 城池（点国打开国务）");
            int j = 0;
            foreach (var n in neighbours.OrderBy(n => n.Id))
            {
                var opinion = SafeValue(() => w.Diplomacy.GetOpinion(r, n), null);
                int total = opinion == null ? 0 : opinion.Total;
                bool atWar = r.Wars.Any(war => war.IsInWarWith(r, n));
                near.Add(Arc.Line(j++, () => OpenCoreState(n), Arc.Swatch(n.Color), Arc.Text(n.Name, "ec-label"), atWar ? Arc.Tag("交战", "r") : Arc.Text("", "ec-label"), Arc.Fill(),
                    Arc.Text(CultureOf(n), "ec-label", "m"), Arc.Text(n.Cities.Count + " 城", "ec-label"),
                    Arc.Text("好感 " + (total >= 0 ? "+" : "") + total, "ec-label", total >= 0 ? "g" : "r")));
            }
            if (j == 0) near.Add(Arc.Note("四境无邻。"));
            page.Add(near);
        }

        void BuildStateCulture(EmpireWorld w, Realm r, VisualElement page)
        {
            string id = CultureId(r);
            var tech = SafeValue(() => TechnologySystem.GetState(id), null);
            var inst = SafeValue(() => InstitutionSystem.GetOrCreateCultureState(id), null);
            page.Add(Arc.Head("文明", CultureOf(r)));
            page.Add(Arc.Kpis(("时代", Safe(() => TechnologySystem.GetEraName(id)), "c"), ("已研科技", tech == null ? "—" : tech.researched_techs.Count.ToString(), "g"),
                ("年研究点", tech == null ? "—" : tech.last_yearly_points.ToString("0"), "y"), ("已行制度", inst?.enacted_node_ids == null ? "—" : inst.enacted_node_ids.Count.ToString(), "o")));
            var cols = Arc.Cols();
            var techs = Arc.Card("科技", "TabInstitutions", "依研成先后");
            if (tech == null || tech.researched_techs.Count == 0) techs.Add(Arc.Note("尚无科技。"));
            else for (int i = 0; i < tech.researched_techs.Count; i++)
                {
                    string t = tech.researched_techs[i];
                    string when = tech.timestamps != null && tech.timestamps.TryGetValue(t, out double s) ? YearOf(w, (int)s) + " 年" : "";
                    techs.Add(Arc.Line(i, null, Arc.Text(Safe(() => TechnologySystem.GetTechName(t, id), t), "ec-label"), Arc.Fill(), Arc.Text(when, "ec-label", "m")));
                }
            cols.Add(techs);
            var reforms = Arc.Card("制度", "TabConstitution", "已推行");
            if (inst?.enacted_node_ids == null || inst.enacted_node_ids.Count == 0) reforms.Add(Arc.Note("尚无制度。"));
            else for (int i = 0; i < inst.enacted_node_ids.Count; i++)
                {
                    string n = inst.enacted_node_ids[i];
                    string when = inst.enacted_timestamps != null && inst.enacted_timestamps.TryGetValue(n, out double s) ? YearOf(w, (int)s) + " 年" : "";
                    reforms.Add(Arc.Line(i, null, Arc.Text(Safe(() => InstitutionSystem.GetNodeName(InstitutionDefinitionRegistry.Get(n), id), n), "ec-label"), Arc.Fill(), Arc.Text(when, "ec-label", "m")));
                }
            cols.Add(reforms);
            page.Add(cols);
            var kin = Arc.Card("同族之国", "civ_nations");
            int j = 0;
            foreach (var x in w.LiveRealms.Where(x => x.Alive && CultureId(x) == id)) kin.Add(Arc.Line(j++, () => OpenCoreState(x), Arc.Swatch(x.Color), Arc.Text(x.Name, "ec-label", x == r ? "y" : null), Arc.Fill(),
                Arc.Text(RegimeOf(x), "ec-label", "m"), Arc.Text(x.Cities.Count + " 城", "ec-label")));
            page.Add(kin);
        }

        // =====================================================================
        // 战争 (the mod's war window): the war's identity and both sides on the left;
        // 概况 / 双方 / 战报, and 天下战事 (running and past wars).
        void OpenCoreWar(War war = null)
        {
            var w0 = CoreHere();
            if (war != null) activeWar = war;
            if (w0 != null && (activeWar == null || !w0.Wars.Contains(activeWar))) activeWar = w0.LiveWars.Count > 0 ? w0.LiveWars[0] : null;
            var win = new ArchiveWindow("战争", "TabWarfare", CloseHudWindow, "overview", "sides", "reports", "list");
            win.Rail = (x, rail) =>
            {
                var w = CoreHere(); if (w == null) { NoCore(w, rail); return; }
                var v = activeWar;
                if (v != null)
                {
                    x.SetTitle(WarName(v));
                    rail.Add(Arc.IdCard(WarName(v), WarKindName(v) + " · " + YearOf(w, v.StartedMonth) + " 年宣战",
                        v.HasEnded() ? YearOf(w, v.EndedMonth) + " 年罢兵 · " + Outcome(v) : "第 " + (v.AgeYears() + 1) + " 年", null, null, v.HasEnded() ? Arc.Tag("已结束", "m") : Arc.Tag("进行中", "r")));
                    var sides = Arc.Card();
                    var a = v.MainAttacker; var d = v.MainDefender;
                    sides.Add(Arc.Person("TabWarfare", "攻方 · " + (v.Attackers.Count + v.PastAttackers.Count) + " 国", a != null ? a.Name : "—", a?.Ruler != null && a.Ruler.Alive ? "君主 " + w.FullName(a.Ruler) : null, a != null && a.Alive ? () => OpenCoreState(a) : (Action)null, "r"));
                    sides.Add(Arc.Note("对阵"));
                    sides.Add(Arc.Person("chess_rook", "守方 · " + (v.Defenders.Count + v.PastDefenders.Count) + " 国", d != null ? d.Name : "—", d?.Ruler != null && d.Ruler.Alive ? "君主 " + w.FullName(d.Ruler) : null, d != null && d.Alive ? () => OpenCoreState(d) : (Action)null, "c"));
                    rail.Add(sides);
                    var menu = Arc.Card(); menu.Add(Arc.MenuHead("战争"));
                    menu.Add(Arc.MenuItem("TabWarfare", "概况", null, x.Page == "overview", () => x.Go("overview")));
                    menu.Add(Arc.MenuItem("chess_knight", "双方", (v.Attackers.Count + v.Defenders.Count).ToString(), x.Page == "sides", () => x.Go("sides")));
                    menu.Add(Arc.MenuItem("iconHistory", "战报", WarReport(w, v).Count().ToString(), x.Page == "reports", () => x.Go("reports")));
                    rail.Add(menu);
                }
                else { x.SetTitle("战争"); rail.Add(Arc.IdCard("天下战事", "第 " + w.Year + " 年")); }
                var all = Arc.Card(); all.Add(Arc.MenuHead("天下战事"));
                all.Add(Arc.MenuItem("TabWarfare", "进行中", w.LiveWars.Count.ToString(), x.Page == "list" && !warListPast, () => { warListPast = false; x.Go("list"); }));
                all.Add(Arc.MenuItem("iconHistory", "往事", w.Wars.Count(z => z != null && z.HasEnded()).ToString(), x.Page == "list" && warListPast, () => { warListPast = true; x.Go("list"); }));
                rail.Add(all);
            };
            win.Main = (x, page) =>
            {
                var w = CoreHere(); if (w == null) { NoCore(w, page); return; }
                if (activeWar == null || x.Page == "list") { BuildWarList(w, page, x); return; }
                switch (x.Page)
                {
                    case "sides": BuildWarSides(w, activeWar, page); break;
                    case "reports": BuildWarReports(w, activeWar, page); break;
                    default: BuildWarOverview(w, activeWar, page); break;
                }
            };
            OpenHudWindow("war", win, activeWar == null ? 3 : 0);
        }

        static List<Realm> Side(War v, bool attackers)
        {
            var list = new List<Realm>(attackers ? v.Attackers : v.Defenders);
            foreach (var r in attackers ? v.PastAttackers : v.PastDefenders) if (!list.Contains(r)) list.Add(r);
            return list;
        }

        IEnumerable<AnnalEntry> WarReport(EmpireWorld w, War v)
        {
            var ids = new HashSet<int>(Side(v, true).Concat(Side(v, false)).Select(r => r.Id));
            int end = v.HasEnded() ? v.EndedMonth : int.MaxValue;
            var e = w.Annals.Entries;
            for (int i = e.Count - 1; i >= 0; i--)
            {
                if (e[i].Month < v.StartedMonth) break;
                if (e[i].Month > end) continue;
                if (e[i].Kind == AnnalKind.WarEnded ? e[i].A == v.Id : RealmsOf(e[i]).Any(ids.Contains)) yield return e[i];
            }
        }

        int CitiesTaken(EmpireWorld w, War v)
        {
            var a = new HashSet<int>(Side(v, true).Select(r => r.Id)); var d = new HashSet<int>(Side(v, false).Select(r => r.Id));
            return WarReport(w, v).Count(e => e.Kind == AnnalKind.CityTransferred && (a.Contains(e.B) && d.Contains(e.C) || d.Contains(e.B) && a.Contains(e.C)));
        }

        void BuildWarOverview(EmpireWorld w, War v, VisualElement page)
        {
            var att = Side(v, true); var def = Side(v, false);
            int aMen = att.Where(r => r.Alive).Sum(r => SafeValue(() => r.Warriors(), 0)), dMen = def.Where(r => r.Alive).Sum(r => SafeValue(() => r.Warriors(), 0));
            int aCities = att.Where(r => r.Alive).Sum(r => r.Cities.Count), dCities = def.Where(r => r.Alive).Sum(r => r.Cities.Count);
            page.Add(Arc.Head("战争概况", "第 " + w.Year + " 年"));
            page.Add(Arc.Kpis(("兵力 攻 / 守", aMen.ToString("N0") + " / " + dMen.ToString("N0"), aMen >= dMen ? "r" : "c"), ("城池 攻 / 守", aCities + " / " + dCities, null),
                ("易手之城", CitiesTaken(w, v).ToString(), "y"), (v.HasEnded() ? "历时" : "已打", Math.Max(0, v.DurationMonths() / 12) + " 年", null)));
            var cols = Arc.Cols();
            cols.Add(SideCard(w, v, true, false));
            cols.Add(SideCard(w, v, false, false));
            page.Add(cols);
            var report = Arc.Card("战报", "iconHistory", "新的在上");
            var lines = WarReport(w, v).Take(8).ToList();
            if (lines.Count == 0) report.Add(Arc.Note("尚无战报。"));
            else AddAnnalLines(w, report, lines, 8);
            page.Add(report);
        }

        VisualElement SideCard(EmpireWorld w, War v, bool attackers, bool full)
        {
            var side = Side(v, attackers);
            int men = side.Where(r => r.Alive).Sum(r => SafeValue(() => r.Warriors(), 0));
            var card = Arc.Card(attackers ? "攻方" : "守方", attackers ? "TabWarfare" : "chess_rook", side.Where(r => r.Alive).Sum(r => r.Cities.Count) + " 城 · " + men.ToString("N0") + " 兵");
            int i = 0;
            foreach (var r in side)
            {
                bool main = r == (attackers ? v.MainAttacker : v.MainDefender);
                bool left = !(attackers ? v.Attackers : v.Defenders).Contains(r);
                card.Add(Arc.Line(i++, r.Alive ? () => OpenCoreState(r) : (Action)null, Arc.Swatch(r.Color), Arc.Text(r.Name, "ec-label"),
                    main ? Arc.Tag(attackers ? "主攻" : "主守", attackers ? "r" : "c") : left ? Arc.Tag(r.Alive ? "已退出" : "已亡", "m") : Arc.Text("", "ec-label"), Arc.Fill(),
                    Arc.Text(r.Alive ? r.Cities.Count + " 城 · " + Arc.Count(SafeValue(() => r.PopulationCount(), 0)) : "—", "ec-label", "m")));
                if ((full || main) && r.Alive && r.Ruler != null && r.Ruler.Alive)
                    card.Add(Arc.Person("civ_crown", "君主", w.FullName(r.Ruler), "武 " + r.Ruler.Warfare + " · 智 " + r.Ruler.Intelligence + (full ? " · 驻军 " + SafeValue(() => r.Warriors(), 0).ToString("N0") + " · 稳定 " + AverageStability(w, r).ToString("0") : ""), () => OpenCoreClan(r.Ruler.Clan, r.Ruler)));
            }
            return card;
        }

        void BuildWarSides(EmpireWorld w, War v, VisualElement page)
        {
            page.Add(Arc.Head("双方", "攻 " + Side(v, true).Count + " 国 · 守 " + Side(v, false).Count + " 国"));
            var cols = Arc.Cols();
            cols.Add(SideCard(w, v, true, true));
            cols.Add(SideCard(w, v, false, true));
            page.Add(cols);
        }

        void BuildWarReports(EmpireWorld w, War v, VisualElement page)
        {
            var lines = WarReport(w, v).ToList();
            page.Add(Arc.Head("战报", lines.Count + " 条"));
            var card = Arc.Card("编年", "iconHistory", "宣战、易手、君位、罢兵；新的在上");
            if (lines.Count == 0) card.Add(Arc.Note("尚无战报。"));
            else AddAnnalLines(w, card, lines, 200);
            page.Add(card);
        }

        void BuildWarList(EmpireWorld w, VisualElement page, ArchiveWindow x)
        {
            if (!warListPast)
            {
                page.Add(Arc.Head("进行中的战争", w.LiveWars.Count + " 场"));
                if (w.LiveWars.Count == 0) { page.Add(Arc.Empty("天下暂无战事。")); return; }
                foreach (var v in w.LiveWars.OrderBy(z => z.StartedMonth))
                {
                    var war = v;
                    var card = Arc.Card(WarName(v), "TabWarfare", WarKindName(v) + " · " + YearOf(w, v.StartedMonth) + " 年起 · 已 " + v.AgeYears() + " 年");
                    card.AddToClassList("ec-click");
                    card.AddManipulator(new Clickable(() => { activeWar = war; x.Go("overview"); }));
                    card.Add(Arc.Kv("攻方", string.Join("、", v.Attackers.Select(r => r.Name)), 0, "r"));
                    card.Add(Arc.Kv("守方", string.Join("、", v.Defenders.Select(r => r.Name)), 1, "c"));
                    page.Add(card);
                }
                return;
            }
            var ended = w.Wars.Where(z => z != null && z.HasEnded()).OrderByDescending(z => z.EndedMonth).ToList();
            page.Add(Arc.Head("往事", ended.Count + " 场已结束"));
            if (ended.Count == 0) { page.Add(Arc.Empty("尚无已结束的战争。")); return; }
            var list = Arc.Card("近年战争", "iconHistory", "起止 · 结局（点开看详情）");
            for (int i = 0; i < ended.Count && i < 60; i++)
            {
                var war = ended[i];
                list.Add(Arc.Line(i, () => { activeWar = war; x.Go("overview"); }, Arc.Text(WarName(war), "ec-label"), Arc.Fill(),
                    Arc.Text(YearOf(w, war.StartedMonth) + "—" + YearOf(w, war.EndedMonth) + " 年", "ec-label", "m"),
                    Arc.Text(Outcome(war), "ec-label", war.Outcome == WarOutcome.Attackers ? "r" : war.Outcome == WarOutcome.Defenders ? "c" : "o")));
            }
            page.Add(list);
        }

        // =====================================================================
        // 宗族 (the mod's house window): the house, its head and the page menu on the left;
        // 宗族总览 / 族人名录 / 亲属 (around one person) / 天下宗族.
        void OpenCoreClan(Clan clan = null, Person person = null)
        {
            if (clan != null) { activeClan = clan; activePerson = person; }
            var win = new ArchiveWindow("宗族", "specificClanIcon", CloseHudWindow, "overview", "members", "kin", "houses");
            win.Rail = (x, rail) =>
            {
                var w = CoreHere(); if (w == null) { NoCore(w, rail); return; }
                var c = ActiveClan(w);
                if (c == null) { rail.Add(Arc.IdCard("天下宗族", "尚无在世的宗族")); }
                else
                {
                    var throne = w.LiveRealms.FirstOrDefault(r => r.Alive && r.RulingClan == c);
                    var chief = SafeValue(() => c.GetChief(), null);
                    rail.Add(Arc.IdCard(c.Surname + " 氏", throne != null ? throne.Name + "王室" : "庶姓",
                        "开族 " + YearText(w, c.FoundedMonth) + " 年 · " + Generations(chief, c) + " 世", throne?.Color ?? 0x6a5a44u, c.Surname, throne != null ? Arc.Tag("王室") : null));
                    if (chief != null) { var p = Arc.Card(); p.Add(Arc.Person(throne != null && chief.IsRuler() ? "civ_crown" : "famous_talents", "族长" + (chief.IsRuler() ? " · " + chief.Throne.Name + "君主" : ""), w.FullName(chief), w.AgeOf(chief) + " 岁 · 名望 " + chief.Renown, () => { activePerson = chief; x.Go("kin"); })); rail.Add(p); }
                    var menu = Arc.Card(); menu.Add(Arc.MenuHead("宗族"));
                    menu.Add(Arc.MenuItem("specificClanIcon", "宗族总览", null, x.Page == "overview", () => x.Go("overview")));
                    menu.Add(Arc.MenuItem("iconPopulation50", "族人名录", w.Living.Count(z => z.Clan == c).ToString(), x.Page == "members", () => x.Go("members")));
                    menu.Add(Arc.MenuHead("本人"));
                    menu.Add(Arc.MenuItem("TabDynasty", "亲属", null, x.Page == "kin", () => x.Go("kin")));
                    rail.Add(menu);
                }
                var all = Arc.Card(); all.Add(Arc.MenuHead("天下"));
                all.Add(Arc.MenuItem("empire_list", "天下宗族", w.Living.Where(z => z.Clan != null).Select(z => z.Clan).Distinct().Count().ToString(), x.Page == "houses", () => x.Go("houses")));
                all.Add(Arc.MenuItem("famous_talents", "名望榜", null, false, () => { x.Go("houses"); }));
                rail.Add(all);
            };
            win.Main = (x, page) =>
            {
                var w = CoreHere(); if (w == null) { NoCore(w, page); return; }
                var c = ActiveClan(w);
                if (c == null || x.Page == "houses") { BuildClanHouses(w, page, x); return; }
                switch (x.Page)
                {
                    case "members": BuildClanMembers(w, c, page, x); break;
                    case "kin": BuildClanKin(w, c, page, x); break;
                    default: BuildClanOverview(w, c, page, x); break;
                }
            };
            OpenHudWindow("clans", win, person != null ? 2 : 0);
        }

        void OpenHudClansCore(int first) { OpenCoreClan(null, null); if (first == 1) hudWindow?.Show(3); }

        Clan ActiveClan(EmpireWorld w)
        {
            if (activeClan != null && w.Clans.Contains(activeClan)) return activeClan;
            var r = StateRealm(w);
            activeClan = r?.RulingClan;
            if (activeClan == null) activeClan = w.Living.Where(p => p.Clan != null).GroupBy(p => p.Clan).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            return activeClan;
        }

        static int Generations(Person p, Clan c)
        {
            int n = 0;
            for (var x = p; x != null && x.Clan == c && n < 99; x = x.Father ?? x.Mother) n++;
            return Math.Max(1, n);
        }

        void BuildClanOverview(EmpireWorld w, Clan c, VisualElement page, ArchiveWindow x)
        {
            var living = w.Living.Where(p => p.Clan == c).ToList();
            int ever = w.People.Count(p => p.Clan == c);
            var chief = SafeValue(() => c.GetChief(), null);
            page.Add(Arc.Head("宗族总览", c.Founder != null ? "开族始祖 " + w.FullName(c.Founder) : null));
            page.Add(Arc.Kpis(("在世 · 历代 " + ever, living.Count.ToString(), "g"), ("名望合计", living.Sum(p => (long)p.Renown).ToString("N0"), "y"),
                ("居城", living.Where(p => p.City != null).Select(p => p.City).Distinct().Count() + " 城", "c"), ("君主 · 城主", living.Count(p => p.IsRuler() || p.IsCityLeader()) + " 人", null)));
            var cols = Arc.Cols();
            var famous = Arc.Card("显赫族人", "famous_talents", "按名望");
            foreach (var p in living.OrderByDescending(p => p.Renown).Take(4))
            {
                var person = p;
                famous.Add(Arc.Person(p.IsRuler() ? "civ_crown" : "famous_talents", Role(w, p), w.FullName(p), w.AgeOf(p) + " 岁 · " + Talents(p) + " · 名望 " + p.Renown, () => { activePerson = person; x.Go("kin"); }));
            }
            if (living.Count == 0) famous.Add(Arc.Note("族中已无在世之人。"));
            cols.Add(famous);
            var line = Arc.Card("世系", "TabDynasty", "族长上溯");
            var chain = new List<Person>();
            for (var p = chief; p != null && p.Clan == c && chain.Count < 12; p = p.Father ?? p.Mother) chain.Add(p);
            chain.Reverse();
            string[] nums = { "一", "二", "三", "四", "五", "六", "七", "八", "九", "十", "十一", "十二" };
            for (int i = 0; i < chain.Count; i++)
            {
                var p = chain[i];
                string span = YearText(w, p.BirthMonth) + "—" + (p.Alive ? "" : YearText(w, p.DeathMonth)) + " 年" + (p.Alive ? "（在世）" : "");
                line.Add(Arc.Line(i, () => { activePerson = p; x.Go("kin"); }, Arc.Text(nums[i] + "世", "ec-label", "y"), Arc.Text(w.FullName(p), "ec-label"), Arc.Fill(), Arc.Text(span, "ec-label", "m")));
            }
            if (chain.Count == 0) line.Add(Arc.Note("无族长。"));
            cols.Add(line);
            page.Add(cols);
            if (chief != null) page.Add(KinCard(w, chief, x, "亲属（以族长为中心）"));
        }

        VisualElement KinCard(EmpireWorld w, Person p, ArchiveWindow x, string title)
        {
            var card = Arc.Card(title, "TabDynasty", null);
            var tiles = Arc.Tiles();
            void Add(Person q, string rel) { if (q == null) return; var t = q; tiles.Add(Arc.Tile(q.IsRuler() ? "civ_crown" : "famous_talents", rel + " " + w.FullName(q), q.Alive ? w.AgeOf(q) + " 岁" + (q.IsRuler() ? " · 君主" : q.IsCityLeader() ? " · 城主" : "") : "已故", () => { activePerson = t; activeClan = t.Clan ?? activeClan; x.Go("kin"); })); }
            Add(p.Father, "父"); Add(p.Mother, "母"); Add(p.Spouse, p.Sex == Sex.Male ? "妻" : "夫");
            int n = 0;
            foreach (var child in p.Children.OrderBy(z => z.BirthMonth)) { if (n++ >= 6) break; Add(child, child.Sex == Sex.Male ? (n == 1 ? "长子" : "子") : "女"); }
            if (tiles.childCount == 0) card.Add(Arc.Note("无在册亲属。")); else card.Add(tiles);
            return card;
        }

        void BuildClanMembers(EmpireWorld w, Clan c, VisualElement page, ArchiveWindow x)
        {
            var living = w.Living.Where(p => p.Clan == c).OrderByDescending(p => p.Renown).ThenBy(p => p.BirthMonth).ToList();
            page.Add(Arc.Head("族人名录", "在世 " + living.Count + " · 历代 " + w.People.Count(p => p.Clan == c)));
            var card = Arc.Card("名录", "iconPopulation50", "按名望（点人看亲属）");
            for (int i = 0; i < living.Count && i < 120; i++)
            {
                var p = living[i];
                card.Add(Arc.Line(i, () => { activePerson = p; x.Go("kin"); }, Arc.Text(w.FullName(p), "ec-label", p.IsRuler() ? "y" : null), Arc.Text(w.AgeOf(p) + " 岁", "ec-label", "m"), Arc.Fill(),
                    Arc.Text(Role(w, p), "ec-label", "c"), Arc.Text(Talents(p), "ec-label", "m"), Arc.Text("名望 " + p.Renown, "ec-label", "y")));
            }
            if (living.Count == 0) card.Add(Arc.Note("族中已无在世之人。"));
            page.Add(card);
        }

        void BuildClanKin(EmpireWorld w, Clan c, VisualElement page, ArchiveWindow x)
        {
            var p = activePerson;
            if (p == null || (p.Clan != c && p.Clan != null)) p = activePerson = SafeValue(() => c.GetChief(), null);
            if (p == null) { page.Add(Arc.Empty("无可显示的人物。")); return; }
            page.Add(Arc.Head(w.FullName(p), (p.Alive ? w.AgeOf(p) + " 岁" : "已故") + " · " + Role(w, p)));
            var top = Arc.Card("本人", "famous_talents");
            top.Add(Arc.Person(p.IsRuler() ? "civ_crown" : "famous_talents", Role(w, p), w.FullName(p), YearText(w, p.BirthMonth) + " 年生" + (p.Alive ? "" : " · " + YearText(w, p.DeathMonth) + " 年卒") + " · 名望 " + p.Renown));
            top.Add(Arc.Bar("治理", p.Stewardship / 19f, p.Stewardship.ToString(), "g"));
            top.Add(Arc.Bar("军事", p.Warfare / 19f, p.Warfare.ToString(), "r"));
            top.Add(Arc.Bar("智略", p.Intelligence / 19f, p.Intelligence.ToString(), "c"));
            top.Add(Arc.Bar("外交", p.Diplomacy / 19f, p.Diplomacy.ToString(), "o"));
            page.Add(top);
            page.Add(KinCard(w, p, x, "亲属"));
            var siblings = (p.Father ?? p.Mother)?.Children.Where(z => z != p).ToList() ?? new List<Person>();
            if (siblings.Count > 0)
            {
                var card = Arc.Card("同辈", "iconPopulation50");
                var tiles = Arc.Tiles();
                foreach (var s in siblings.OrderBy(z => z.BirthMonth).Take(9))
                {
                    var t = s;
                    tiles.Add(Arc.Tile("famous_talents", (s.BirthMonth < p.BirthMonth ? (s.Sex == Sex.Male ? "兄 " : "姊 ") : (s.Sex == Sex.Male ? "弟 " : "妹 ")) + w.FullName(s), s.Alive ? w.AgeOf(s) + " 岁" : "已故", () => { activePerson = t; x.Go("kin"); }));
                }
                card.Add(tiles);
                page.Add(card);
            }
        }

        void BuildClanHouses(EmpireWorld w, VisualElement page, ArchiveWindow x)
        {
            var living = new Dictionary<Clan, int>();
            foreach (var p in w.Living) if (p.Clan != null) { living.TryGetValue(p.Clan, out int n); living[p.Clan] = n + 1; }
            var ruling = new Dictionary<Clan, Realm>();
            foreach (var r in w.LiveRealms) if (r.Alive && r.RulingClan != null) ruling[r.RulingClan] = r;
            var clans = living.Keys.OrderBy(c => ruling.ContainsKey(c) ? 0 : 1).ThenByDescending(c => living[c]).ThenBy(c => c.Id).ToList();
            var top = clans.Count == 0 ? null : clans.OrderByDescending(c => living[c]).First();
            page.Add(Arc.Head("天下宗族", clans.Count + " 族"));
            page.Add(Arc.Kpis(("宗族", clans.Count.ToString(), "g"), ("在册人物", w.Living.Count.ToString("N0"), "c"), ("在位王室", ruling.Count.ToString(), "y"),
                ("最大宗族", top == null ? "—" : top.Surname + "氏 " + living[top] + " 人", null)));
            var card = Arc.Card("门第", "specificClanIcon", "王室在前 · 在世人数（点族打开）");
            for (int i = 0; i < clans.Count && i < 80; i++)
            {
                var c = clans[i];
                ruling.TryGetValue(c, out var throne);
                var chief = SafeValue(() => c.GetChief(), null);
                card.Add(Arc.Line(i, () => { activeClan = c; activePerson = null; x.Go("overview"); }, throne != null ? Arc.Swatch(throne.Color) : Arc.Text("", "ec-label"),
                    Arc.Text(c.Surname + "氏", "ec-label", throne != null ? "y" : null), throne != null ? Arc.Tag(throne.Name + "王室") : Arc.Text("", "ec-label"), Arc.Fill(),
                    Arc.Text(chief != null ? "族长 " + w.FullName(chief) : "", "ec-label", "m"), Arc.Text(living[c] + " 人在世", "ec-label", "g")));
            }
            if (clans.Count == 0) card.Add(Arc.Note("尚无在世的宗族。"));
            page.Add(card);
            var people = w.Living.OrderByDescending(p => p.Renown).Take(20).ToList();
            var fame = Arc.Card("名望榜", "famous_talents", "天下前 20");
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                fame.Add(Arc.Line(i, p.Clan != null ? () => { activeClan = p.Clan; activePerson = p; x.Go("kin"); } : (Action)null,
                    Arc.Text((i + 1) + ". " + w.FullName(p), "ec-label", p.IsRuler() ? "y" : null), Arc.Fill(), Arc.Text(Role(w, p), "ec-label", "c"),
                    Arc.Text(Talents(p), "ec-label", "m"), Arc.Text("名望 " + p.Renown, "ec-label", "y")));
            }
            page.Add(fame);
        }

        // =====================================================================
        // 史书 (the mod's chronicle): volumes and filters on the left; the annals grouped by
        // year and coloured by kind, or the recent news.
        void OpenCoreAnnals()
        {
            var win = new ArchiveWindow("史书", "iconHistory", CloseHudWindow, "annals", "news");
            win.Rail = (x, rail) =>
            {
                var w = CoreHere(); if (w == null) { NoCore(w, rail); return; }
                rail.Add(Arc.IdCard("天下史书", "第 " + w.Year + " 年 · 共 " + w.Annals.Entries.Count.ToString("N0") + " 条"));
                var vol = Arc.Card(); vol.Add(Arc.MenuHead("卷目"));
                vol.Add(Arc.MenuItem("iconHistory", "编年", w.Annals.Entries.Count.ToString(), x.Page == "annals", () => x.Go("annals")));
                vol.Add(Arc.MenuItem("TabEmpire", "天下大事", w.News.Items.Count.ToString(), x.Page == "news", () => x.Go("news")));
                rail.Add(vol);
                var counts = new int[5];
                foreach (var e in w.Annals.Entries) counts[AnnalGroup(e.Kind)]++;
                var kinds = Arc.Card(); kinds.Add(Arc.MenuHead("按类"));
                for (int i = 0; i < AnnalGroupNames.Length; i++)
                {
                    int k = i;
                    kinds.Add(Arc.MenuItem(null, AnnalGroupNames[i], i == 0 ? w.Annals.Entries.Count.ToString() : counts[i].ToString(), annalsKind == k, () => { annalsKind = k; x.Go("annals"); }, AnnalTones[i]));
                }
                rail.Add(kinds);
                var realm = StateRealm(w);
                if (realm != null)
                {
                    var only = Arc.Card(); only.Add(Arc.MenuHead("按国"));
                    only.Add(Arc.MenuItem(null, "只看 " + realm.Name, null, annalsRealmOnly, () => { annalsRealmOnly = !annalsRealmOnly; x.Refresh(); }));
                    rail.Add(only);
                }
            };
            win.Main = (x, page) =>
            {
                var w = CoreHere(); if (w == null) { NoCore(w, page); return; }
                var realm = StateRealm(w);
                if (x.Page == "news")
                {
                    var items = w.News.Items;
                    page.Add(Arc.Head("天下大事", "最近 " + Math.Min(items.Count, 120) + " 条"));
                    var card = Arc.Card("近事", "TabEmpire", "新的在上 · 点条目跳到该国");
                    int year = int.MinValue, shown = 0;
                    for (int i = items.Count - 1; i >= 0 && shown < 120; i--)
                    {
                        var item = items[i];
                        if (annalsRealmOnly && realm != null && item.RealmId != realm.Id) continue;
                        shown++;
                        int y = YearOf(w, item.Month);
                        if (y != year) { year = y; card.Add(Arc.Year(y + " 年")); }
                        var target = item.RealmId >= 0 ? w.RealmByKey(item.RealmId) : null;
                        card.Add(Arc.Event(MonthName(item.Month), item.Text, item.OnScreen ? "y" : null, target != null ? () => FocusCoreRealm(target) : (Action)null));
                    }
                    if (shown == 0) card.Add(Arc.Note("尚无大事。"));
                    page.Add(card);
                    return;
                }
                IEnumerable<AnnalEntry> entries = Enumerable.Range(0, w.Annals.Entries.Count).Select(i => w.Annals.Entries[w.Annals.Entries.Count - 1 - i]);
                if (annalsKind != 0) entries = entries.Where(e => AnnalGroup(e.Kind) == annalsKind);
                if (annalsRealmOnly && realm != null) entries = entries.Where(e => RealmsOf(e).Contains(realm.Id));
                page.Add(Arc.Head("编年", AnnalGroupNames[annalsKind] + (annalsRealmOnly && realm != null ? " · " + realm.Name : ""),
                    Arc.Chip("全部", !annalsRealmOnly, () => { annalsRealmOnly = false; x.Refresh(); }), Arc.Chip("本国", annalsRealmOnly, () => { annalsRealmOnly = true; x.Refresh(); })));
                var list = entries.Take(200).ToList();
                var chron = Arc.Card("纪事", "iconHistory", "新的在上 · 点条目跳到该国（最近 200 条）");
                if (list.Count == 0) chron.Add(Arc.Note("没有符合的记载。"));
                else AddAnnalLines(w, chron, list, 200);
                page.Add(chron);
            };
            OpenHudWindow("annals", win);
        }
    }
}
