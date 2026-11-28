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
                BuildNationsV5(w, page, x, realms);
                return;
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
                    case "finance": BuildStateFinanceV5(w, r, into, x); break;
                    case "cities": BuildStateCitiesV5(w, r, into); break;
                    case "lineage": BuildStateLineageV5(w, r, into); break;
                    case "foreign": BuildStateForeignV5(w, r, into); break;
                    case "culture": BuildStateCultureV5(w, r, into); break;
                    default: BuildStateOverviewV5(w, r, into); break;
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
                if (activeWar == null || x.Page == "list") { if (warListPast) BuildWarPastV5(w, page, x); else BuildWarsV5(w, page, x); return; }
                switch (x.Page)
                {
                    case "sides": BuildWarSidesV5(w, activeWar, page); break;
                    case "reports": BuildWarReportsV5(w, activeWar, page); break;
                    default: BuildWarOverviewV5(w, activeWar, page); break;
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
                if (c == null || x.Page == "houses") { BuildClanHousesV5(w, page, x); return; }
                switch (x.Page)
                {
                    case "members": BuildClanMembersV5(w, c, page, x); break;
                    case "kin": BuildClanKinV5(w, c, page, x); break;
                    default: BuildClanOverviewV5(w, c, page, x); break;
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
                if (x.Page == "news") { BuildNewsV5(w, page, realm); return; }
                BuildAnnalsV5(w, page, x, realm);
                return;
            };
            OpenHudWindow("annals", win);
        }
    }
}
