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
    // The main pages of the five archive windows in the v5 house layout (docs/design/ui-style.md):
    // a page head with the one question, a row of headline figures, an equal grid of info cards
    // each ending in a verdict, and a hint line at the foot.
    public sealed partial class FlatWorldApp
    {
        MapPicker nationsMap, warsMap; EmpireWorld nationsMapWorld, warsMapWorld;

        static readonly Color32[] ShareColours =
        {
            new Color32(214, 172, 78, 255), new Color32(127, 216, 234, 255), new Color32(226, 103, 90, 255),
            new Color32(101, 214, 110, 255), new Color32(176, 122, 56, 255), new Color32(138, 90, 216, 255)
        };
        static readonly Color32 ShareRest = new Color32(78, 64, 58, 255);

        // A stacked share bar with its legend: the biggest parts in colour, the rest grey.
        static VisualElement ShareBar(IList<(string name, double value)> parts, int coloured = 4)
        {
            var box = new VisualElement();
            double total = Math.Max(1e-9, parts.Sum(p => Math.Max(0, p.value)));
            var bar = new VisualElement(); bar.AddToClassList("ec-share");
            var legend = new List<(Color, string)>();
            double rest = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (i >= coloured) { rest += Math.Max(0, parts[i].value); continue; }
                var seg = new VisualElement(); seg.AddToClassList("ec-share-seg");
                seg.style.width = Length.Percent((float)(parts[i].value / total * 100));
                seg.style.backgroundColor = (Color)ShareColours[i % ShareColours.Length];
                bar.Add(seg);
                legend.Add((ShareColours[i % ShareColours.Length], parts[i].name));
            }
            if (rest > 0)
            {
                var seg = new VisualElement(); seg.AddToClassList("ec-share-seg");
                seg.style.width = Length.Percent((float)(rest / total * 100)); seg.style.backgroundColor = (Color)ShareRest;
                bar.Add(seg); legend.Add((ShareRest, "其余"));
            }
            box.Add(bar);
            box.Add(Arc.Legend(legend.ToArray()));
            return box;
        }

        MapPicker PoliticalMap(ref MapPicker picker, ref EmpireWorld world, EmpireWorld w, Func<Realm, Color32> colour, Action<Realm> pick)
        {
            if (picker == null || !ReferenceEquals(world, w)) { world = w; picker = new MapPicker(w.Width, w.Height, 336, 128, 4); picker.ShowAll(); }
            picker.KeyAt = t => { int c = w.TileCity[t]; if (c < 0) return -1; var r = w.Cities[c].Realm; return r != null && r.Alive ? r.Id : -1; };
            picker.IsWater = t => !w.Claimable(t);
            picker.LookOf = id =>
            {
                var r = id >= 0 && id < w.Realms.Count ? w.Realms[id] : null;
                return r == null ? new MapPicker.Look { Fill = new Color32(74, 66, 54, 255) }
                    : new MapPicker.Look { Fill = colour(r), Label = r.Name, Clickable = true, Edge = r == activeRealm ? MapPicker.Edge.Gold : MapPicker.Edge.Plain };
            };
            picker.Pick = id => { var r = id >= 0 && id < w.Realms.Count ? w.Realms[id] : null; if (r != null && r.Alive) pick(r); };
            picker.Redraw();
            return picker;
        }

        static string Pop(Realm r) => Arc.Count(SafeValue(() => r.PopulationCount(), 0));

        // ---------- 列国 ----------
        void BuildNationsV5(EmpireWorld w, VisualElement page, ArchiveWindow x, List<Realm> realms)
        {
            var pop = realms.ToDictionary(r => r, r => (double)SafeValue(() => r.PopulationCount(), 0));
            double total = pop.Values.Sum();
            var biggest = realms.OrderByDescending(r => r.Cities.Count).ThenByDescending(r => pop[r]).FirstOrDefault();
            int atWar = realms.Count(r => r.Wars.Count > 0);
            page.Add(Arc.PageHead(nationsCulture == null ? "列国" : CultureName(nationsCulture) + "诸国", "天下几国，谁强谁弱"));
            page.Add(Arc.KpiRow(("现存", realms.Count + " 国", null, false), ("人口", Arc.Count(total), null, false),
                ("最大", biggest == null ? "—" : biggest.Name, "g", true), ("交战", atWar + " 国", atWar > 0 ? "r" : null, false)));
            if (realms.Count == 0) { page.Add(Arc.Empty("没有符合的国家。")); return; }
            var grid = Arc.Grid(2);

            int held = 0, land = 0;
            for (int t = 0; t < w.TileCity.Length; t++) { if (w.Settleable(t)) land++; if (w.TileCity[t] >= 0) held++; }
            int bigTiles = biggest == null ? 0 : biggest.Cities.Sum(c => c.Tiles.Count);
            var map = Arc.InfoCard("天下简图", "iconTitleLayer", Arc.Badge("按国上色", "m"));
            map.Add(PoliticalMap(ref nationsMap, ref nationsMapWorld, w, r => Hud.Rgb(r.Color), r => { FocusCoreRealm(r); OpenCoreState(r); }).Root);
            map.Add(Arc.Verdict(biggest == null ? "天下无主" : biggest.Name + "地盘最大，占已辟之地" + (held == 0 ? "—" : Mathf.RoundToInt(100f * bigTiles / held) + "%") + "；天下尚有 " + (land == 0 ? 0 : 100 - Mathf.RoundToInt(100f * held / land)) + "% 无主"));
            Arc.GridAdd(grid, map);

            var top = realms.OrderByDescending(r => pop[r]).Take(8).ToList();
            var rank = Arc.InfoCard("实力榜", "TabMilitary", Arc.Badge("按人口", "m"));
            var rl = Arc.ListBlock();
            foreach (var r in top)
            {
                var rr = r;
                bool empire = SafeValue(() => r.IsEmpire(), false);
                Arc.ListRow(rl, r.Name, Arc.Badge(empire ? "帝国" : RegimeOf(r), empire ? "c" : "m"), r.Cities.Count + " 城 · " + Arc.Count(pop[r]), null, Hud.Rgb(r.Color), r == activeRealm ? "y" : null,
                    () => { FocusCoreRealm(rr); OpenCoreState(rr); });
            }
            rank.Add(rl);
            double top5 = top.Take(5).Sum(r => pop[r]);
            rank.Add(Arc.Verdict("前五国占天下人口" + (total <= 0 ? "—" : Mathf.RoundToInt((float)(100 * top5 / total)) + "%")));
            Arc.GridAdd(grid, rank);

            var peoples = realms.GroupBy(r => CultureId(r) ?? "").Select(g => (name: CultureName(g.Key), value: (double)g.Count(), id: g.Key, n: g.Count())).OrderByDescending(p => p.value).ToList();
            var culture = Arc.InfoCard("民族分布", "civ_nations", Arc.Badge(peoples.Count + " 族", "m"));
            culture.Add(ShareBar(peoples.Select(p => (p.name, p.value)).ToList()));
            var cl = Arc.ListBlock();
            foreach (var p in peoples.Take(3))
            {
                bool hasEmpire = realms.Any(r => (CultureId(r) ?? "") == p.id && SafeValue(() => r.IsEmpire(), false));
                Arc.ListRow(cl, p.name, hasEmpire ? Arc.Badge("有帝国", "c") : null, p.n + " 国");
            }
            culture.Add(cl);
            culture.Add(Arc.Verdict(peoples.Count == 0 ? "—" : peoples[0].name + "国最多，占" + Mathf.RoundToInt((float)(100 * peoples[0].value / realms.Count)) + "%"));
            Arc.GridAdd(grid, culture);

            int since = w.Month - 120;
            var rise = w.Annals.Entries.Where(e => e.Month >= since && (e.Kind == AnnalKind.RealmFounded || e.Kind == AnnalKind.RealmEnded || e.Kind == AnnalKind.Secession)).Reverse().ToList();
            int founded = rise.Count(e => e.Kind != AnnalKind.RealmEnded), ended = rise.Count(e => e.Kind == AnnalKind.RealmEnded);
            var fall = Arc.InfoCard("兴亡", "iconHistory", Arc.Badge("近十年", "m"));
            var fl = Arc.ListBlock();
            foreach (var e in rise.Take(5))
            {
                var r = e.A >= 0 && e.A < w.Realms.Count ? w.Realms[e.A] : null;
                Arc.ListRow(fl, r != null ? r.Name : "?", e.Kind == AnnalKind.RealmEnded ? Arc.Badge("灭亡", "r") : e.Kind == AnnalKind.Secession ? Arc.Badge("自立", "o") : Arc.Badge("立国", "g"), "第 " + YearText(w, e.Month) + " 年");
            }
            if (rise.Count == 0) Arc.ListRow(fl, "十年无兴亡", null, null);
            fall.Add(fl);
            fall.Add(Arc.Verdict("近十年立 " + founded + " 国、亡 " + ended + " 国，" + (founded > ended ? "天下仍在分裂" : founded < ended ? "天下渐归于一" : "列国相持")));
            Arc.GridAdd(grid, fall);
            page.Add(grid);
            page.Add(Arc.Note("· 点图上或榜上的国看它的国务；左栏可按民族筛"));
        }

        // ---------- 国务 · 总览 ----------
        void BuildStateOverviewV5(EmpireWorld w, Realm r, VisualElement page)
        {
            var books = w.Ledger.Realm(r.Id);
            var live = w.LiveRealms.Where(z => z.Alive).ToList();
            int rank = 1 + live.Count(z => z != r && SafeValue(() => z.PopulationCount(), 0) > SafeValue(() => r.PopulationCount(), 0));
            float stability = AverageStability(w, r);
            page.Add(Arc.PageHead("总览", r.Name + "：国势如何"));
            page.Add(Arc.KpiRow(("正统", books.Legitimacy.ToString(), books.Legitimacy < 50 ? "r" : null, false), ("稳定", stability.ToString("0"), Tone(stability, 70, 40), false),
                ("国库", Arc.Count(books.Treasury), books.Treasury < 0 ? "r" : "y", true), ("名次", "天下第 " + rank, rank <= 3 ? "g" : null, false)));
            var grid = Arc.Grid(2);

            var k = r.Ruler;
            int age = k != null && k.Alive ? w.AgeOf(k) : 0;
            var ruler = Arc.InfoCard("君主", "ChineseCrown", k == null || !k.Alive ? Arc.Badge("空悬", "r") : age >= 60 ? Arc.Badge("年迈", "o") : age < 20 ? Arc.Badge("年少", "c") : Arc.Badge("壮年", "g"));
            if (k != null && k.Alive)
            {
                ruler.Add(Arc.Row(Arc.Text(w.FullName(k), "ec-li-name", "y"), Arc.Note(age + " 岁 · 在位 " + Math.Max(0, (w.Month - ReignStart(w, r)) / 12) + " 年 · 名望 " + k.Renown)));
                ruler.Add(Arc.Meter("治", k.Stewardship / 20f, k.Stewardship.ToString(), new Color32(214, 172, 78, 255)));
                ruler.Add(Arc.Meter("武", k.Warfare / 20f, k.Warfare.ToString(), new Color32(226, 103, 90, 255)));
                ruler.Add(Arc.Meter("智", k.Intelligence / 20f, k.Intelligence.ToString(), new Color32(127, 216, 234, 255)));
                ruler.Add(Arc.Meter("交", k.Diplomacy / 20f, k.Diplomacy.ToString(), new Color32(101, 214, 110, 255)));
                int best = Math.Max(Math.Max(k.Stewardship, k.Warfare), Math.Max(k.Intelligence, k.Diplomacy));
                string strong = best == k.Stewardship ? "善治" : best == k.Warfare ? "善战" : best == k.Intelligence ? "多谋" : "善交";
                ruler.Add(Arc.Verdict(strong + (best == k.Warfare ? "，宜开疆" : best == k.Diplomacy ? "，宜结盟少争" : "，宜休养生息")));
            }
            else ruler.Add(Arc.Verdict("君位空悬，宜早立新君", "r"));
            Arc.GridAdd(grid, ruler);

            double avgPop = live.Count == 0 ? 0 : live.Average(z => (double)SafeValue(() => z.PopulationCount(), 0));
            double avgMen = live.Count == 0 ? 0 : live.Average(z => (double)SafeValue(() => z.Warriors(), 0));
            double avgGold = live.Count == 0 ? 0 : live.Average(z => (double)Math.Max(0, w.Ledger.Realm(z.Id).Treasury));
            double myPop = SafeValue(() => r.PopulationCount(), 0), myMen = SafeValue(() => r.Warriors(), 0), myGold = Math.Max(0, books.Treasury);
            Color mine = Hud.Rgb(r.Color), avg = new Color32(120, 110, 96, 255);
            var power = Arc.InfoCard("国力", "TabMilitary", Arc.Badge("天下第 " + rank, rank <= 3 ? "g" : "m"));
            power.Add(Arc.Legend((mine, r.Name), (avg, "天下平均")));
            power.Add(Arc.Tug("iconPopulation50", myPop, avgPop, Arc.Count(myPop), Arc.Count(avgPop), mine, avg));
            power.Add(Arc.Tug("TabMilitary", myMen, avgMen, Arc.Count(myMen), Arc.Count(avgMen), mine, avg));
            power.Add(Arc.Tug("wealth_layer", myGold, avgGold, Arc.Count(myGold), Arc.Count(avgGold), mine, avg));
            double times = avgPop <= 0 ? 0 : myPop / avgPop;
            power.Add(Arc.Verdict(times >= 2 ? "人口是天下平均的 " + times.ToString("0.#") + " 倍" : times >= 1 ? "略强于天下平均" : "弱于天下平均，宜守"));
            Arc.GridAdd(grid, power);

            var cities = r.Cities.Where(c => c.Alive).OrderByDescending(c => c.Population).ToList();
            var shaky = cities.Where(c => w.Ledger.City(c.Id).Stability < 50).ToList();
            var cc = Arc.InfoCard("城池", "iconCity", Arc.Badge(cities.Count + " 城", "m"), shaky.Count > 0 ? Arc.Badge(shaky.Count + " 城不稳", "o") : null);
            var cl = Arc.ListBlock();
            foreach (var c in cities.Take(5))
            {
                var city = c; var acc = w.Ledger.City(c.Id);
                Arc.ListRow(cl, c.Name, c.IsCapital() ? Arc.Badge("都城", "y") : acc.Stability < 50 ? Arc.Badge("不稳", "o") : null, Arc.Count(c.Population), null, null, null, () => FocusCoreTile(city.Tile));
            }
            cc.Add(cl);
            cc.Add(Arc.Verdict(shaky.Count == 0 ? "诸城安定" : shaky[0].Name + "稳定只有 " + w.Ledger.City(shaky[0].Id).Stability.ToString("0") + "，宜减税或驻军"));
            Arc.GridAdd(grid, cc);

            string id = CultureId(r);
            var tech = SafeValue(() => TechnologySystem.GetState(id), null);
            var inst = SafeValue(() => InstitutionSystem.GetOrCreateCultureState(id), null);
            var civ = Arc.InfoCard("文明", "civ_nations", Arc.Badge(Safe(() => TechnologySystem.GetEraName(id)), "c"));
            if (tech != null && !string.IsNullOrEmpty(tech.current_tech) && tech.progress <= 1.001f)
                civ.Add(Arc.Meter("科", tech.progress, Pct(tech.progress), new Color32(127, 216, 234, 255)));
            var il = Arc.ListBlock();
            Arc.ListRow(il, "已研科技", null, tech == null ? "—" : tech.researched_techs.Count + " 项");
            Arc.ListRow(il, "已行制度", null, inst?.enacted_node_ids == null ? "—" : inst.enacted_node_ids.Count + " 项");
            if (inst != null && !string.IsNullOrEmpty(inst.drift_node_id))
                Arc.ListRow(il, "正在推行", Arc.Badge(Safe(() => InstitutionSystem.GetNodeName(InstitutionDefinitionRegistry.Get(inst.drift_node_id), id), inst.drift_node_id), "c"), inst.drift_progress <= 1.001f ? Pct(inst.drift_progress) : "推行中");
            civ.Add(il);
            civ.Add(Arc.Verdict(tech != null && !string.IsNullOrEmpty(tech.current_tech) ? "正在研究" + Safe(() => TechnologySystem.GetTechName(tech.current_tech, id), tech.current_tech) : "眼下无研究"));
            Arc.GridAdd(grid, civ);
            page.Add(grid);
            page.Add(Arc.Note("· 左栏换页：财政、城池、世系、外交战事、文明"));
        }

        // ---------- 战争 · 天下战事 ----------
        void BuildWarsV5(EmpireWorld w, VisualElement page, ArchiveWindow x)
        {
            var live = w.LiveWars.ToList();
            var involved = new HashSet<Realm>(live.SelectMany(v => v.Attackers.Concat(v.Defenders)));
            int yearStart = w.Month - w.Month % 12;
            int moved = w.Annals.Entries.Count(e => e.Month >= yearStart && e.Kind == AnnalKind.CityTransferred);
            int endedThisYear = w.Wars.Count(v => v != null && v.HasEnded() && v.EndedMonth >= yearStart);
            page.Add(Arc.PageHead("天下战事", "哪里在打，谁占上风"));
            page.Add(Arc.KpiRow(("进行中", live.Count + " 场", live.Count > 0 ? "r" : null, false), ("卷入", involved.Count + " 国", null, false),
                ("今年易手", moved + " 城", moved > 0 ? "o" : null, true), ("今年罢兵", endedThisYear + " 场", endedThisYear > 0 ? "g" : null, false)));
            var grid = Arc.Grid(2);

            var map = Arc.InfoCard("战场简图", "iconTitleLayer", Arc.Badge("红为交战", "m"));
            map.Add(PoliticalMap(ref warsMap, ref warsMapWorld, w, r => involved.Contains(r) ? new Color32(200, 60, 44, 255) : new Color32(90, 120, 92, 255), r => { FocusCoreRealm(r); }).Root);
            map.Add(Arc.Legend((new Color32(200, 60, 44, 255), "交战中"), (new Color32(90, 120, 92, 255), "和平")));
            map.Add(Arc.Verdict(live.Count == 0 ? "天下太平" : involved.Count + " 国卷入战事"));
            Arc.GridAdd(grid, map);

            var ranked = live.OrderByDescending(v => v.Attackers.Concat(v.Defenders).Sum(r => SafeValue(() => r.Warriors(), 0))).ToList();
            var board = Arc.InfoCard("战事榜", "TabWarfare", Arc.Badge("按兵力", "m"));
            var bl = Arc.ListBlock();
            foreach (var v in ranked.Take(6))
            {
                var war = v; int taken = CitiesTaken(w, v);
                string goal = Safe(() => WarGoalSystem.Name(WarGoalSystem.Get(v)), WarKindName(v));
                Arc.ListRow(bl, (v.MainAttacker?.Name ?? "?") + " → " + (v.MainDefender?.Name ?? "?"), Arc.Badge(goal, "r"), taken > 0 ? "易手 " + taken + " 城" : "相持", null, null, null,
                    () => { activeWar = war; x.Go("overview"); });
            }
            if (live.Count == 0) Arc.ListRow(bl, "天下暂无战事", null, null);
            board.Add(bl);
            var lead = ranked.OrderByDescending(v => CitiesTaken(w, v)).FirstOrDefault();
            board.Add(Arc.Verdict(lead == null ? "—" : WarName(lead) + "易手最多，最可能先分胜负"));
            Arc.GridAdd(grid, board);

            var hot = ranked.FirstOrDefault();
            var fierce = Arc.InfoCard("最激烈一场", "TabMilitary", hot == null ? Arc.Badge("无", "m") : Arc.Badge(WarName(hot), "r"));
            if (hot != null)
            {
                var a = Side(hot, true).Where(r => r.Alive).ToList(); var d = Side(hot, false).Where(r => r.Alive).ToList();
                double am = a.Sum(r => (double)SafeValue(() => r.Warriors(), 0)), dm = d.Sum(r => (double)SafeValue(() => r.Warriors(), 0));
                Color ac = hot.MainAttacker != null ? Hud.Rgb(hot.MainAttacker.Color) : Color.red, dc = hot.MainDefender != null ? Hud.Rgb(hot.MainDefender.Color) : Color.cyan;
                fierce.Add(Arc.Legend((ac, "攻 " + (hot.MainAttacker?.Name ?? "?")), (dc, "守 " + (hot.MainDefender?.Name ?? "?"))));
                fierce.Add(Arc.Tug("TabMilitary", am, dm, Arc.Count(am), Arc.Count(dm), ac, dc));
                fierce.Add(Arc.Tug("iconCity", a.Sum(r => r.Cities.Count), d.Sum(r => r.Cities.Count), a.Sum(r => r.Cities.Count).ToString(), d.Sum(r => r.Cities.Count).ToString(), ac, dc));
                var fl = Arc.ListBlock();
                Arc.ListRow(fl, "开战", null, "第 " + YearText(w, hot.StartedMonth) + " 年");
                Arc.ListRow(fl, "目标", Arc.Badge(Safe(() => WarGoalSystem.Name(WarGoalSystem.Get(hot)), WarKindName(hot)), "r"), null);
                fierce.Add(fl);
                fierce.Add(Arc.Verdict(am >= dm * 1.4 ? "攻方兵多，守方难支" : dm >= am * 1.4 ? "守方兵多，攻方难下" : "兵力相近，胜负难料"));
                var war = hot; fierce.AddToClassList("ec-click"); fierce.AddManipulator(new Clickable(() => { activeWar = war; x.Go("overview"); }));
            }
            else fierce.Add(Arc.Verdict("天下太平"));
            Arc.GridAdd(grid, fierce);

            var recent = w.Wars.Where(v => v != null && v.HasEnded()).OrderByDescending(v => v.EndedMonth).Take(5).ToList();
            var ends = Arc.InfoCard("近来结局", "iconHistory", Arc.Badge("最近五场", "m"));
            var el = Arc.ListBlock();
            foreach (var v in recent)
            {
                var war = v;
                Arc.ListRow(el, WarName(v), Arc.Badge(Outcome(v), v.Outcome == WarOutcome.Attackers ? "r" : v.Outcome == WarOutcome.Defenders ? "c" : "m"), "第 " + YearText(w, v.EndedMonth) + " 年", null, null, null,
                    () => { activeWar = war; x.Go("overview"); });
            }
            if (recent.Count == 0) Arc.ListRow(el, "尚无结束的战争", null, null);
            ends.Add(el);
            int decided = recent.Count(v => v.Outcome == WarOutcome.Attackers || v.Outcome == WarOutcome.Defenders);
            ends.Add(Arc.Verdict(recent.Count == 0 ? "—" : "最近 " + recent.Count + " 场里 " + decided + " 场分出胜负"));
            Arc.GridAdd(grid, ends);
            page.Add(grid);
            page.Add(Arc.Note("· 点一场战事看概况、双方与战报；左栏看往事"));
        }

        // ---------- 宗族 · 总览 ----------
        void BuildClanOverviewV5(EmpireWorld w, Clan c, VisualElement page, ArchiveWindow x)
        {
            var living = w.Living.Where(p => p.Clan == c).ToList();
            int ever = w.People.Count(p => p.Clan == c);
            var chief = SafeValue(() => c.GetChief(), null);
            int rulers = w.People.Count(p => p.Clan == c && p.IsRuler()) + w.Annals.Entries.Count(e => (e.Kind == AnnalKind.Accession || e.Kind == AnnalKind.NewDynasty) && e.B >= 0 && e.B < w.People.Count && w.People[e.B].Clan == c && !w.People[e.B].IsRuler());
            page.Add(Arc.PageHead("总览", c.Surname + "氏：人丁、名望与权位"));
            page.Add(Arc.KpiRow(("在世", living.Count + " 人", null, false), ("历代", ever + " 人", null, false),
                ("名望", living.Sum(p => (long)p.Renown).ToString("N0"), "y", true), ("君主", rulers + " 位", rulers > 0 ? "g" : null, false)));
            var grid = Arc.Grid(2);

            var head = Arc.InfoCard("族长", "ChineseCrown", chief != null && chief.IsRuler() ? Arc.Badge(chief.Throne.Name + "君主", "y") : Arc.Badge("族长", "m"));
            if (chief != null)
            {
                var ch = chief;
                head.Add(Arc.Row(Arc.Text(w.FullName(chief), "ec-li-name", "y"), Arc.Note(w.AgeOf(chief) + " 岁 · 名望 " + chief.Renown)));
                head.Add(Arc.Meter("治", chief.Stewardship / 20f, chief.Stewardship.ToString(), new Color32(214, 172, 78, 255)));
                head.Add(Arc.Meter("武", chief.Warfare / 20f, chief.Warfare.ToString(), new Color32(226, 103, 90, 255)));
                head.Add(Arc.Meter("智", chief.Intelligence / 20f, chief.Intelligence.ToString(), new Color32(127, 216, 234, 255)));
                head.Add(Arc.Verdict(chief.IsRuler() ? "族长即国君，" + c.Surname + "氏正当权" : "族中无人在位"));
                head.AddToClassList("ec-click"); head.AddManipulator(new Clickable(() => { activePerson = ch; x.Go("kin"); }));
            }
            else head.Add(Arc.Verdict("族中已无在世之人", "r"));
            Arc.GridAdd(grid, head);

            var fame = Arc.InfoCard("名望榜", "famous_talents", Arc.Badge("在世前五", "m"));
            var fl = Arc.ListBlock();
            var ranked = living.OrderByDescending(p => p.Renown).Take(5).ToList();
            foreach (var p in ranked)
            {
                var person = p;
                Arc.ListRow(fl, w.FullName(p), p == chief ? Arc.Badge("族长", "y") : p.IsRuler() ? Arc.Badge("君主", "y") : p.IsCityLeader() ? Arc.Badge("城主", "c") : null, p.Renown.ToString(), null, null, null,
                    () => { activePerson = person; x.Go("kin"); });
            }
            if (ranked.Count == 0) Arc.ListRow(fl, "族中已无在世之人", null, null);
            fame.Add(fl);
            fame.Add(Arc.Verdict(ranked.Count < 2 ? "人丁单薄" : w.FullName(ranked[1]) + "名望第二"));
            Arc.GridAdd(grid, fame);

            var chain = new List<Person>();
            for (var p = chief; p != null && p.Clan == c && chain.Count < 6; p = p.Father ?? p.Mother) chain.Add(p);
            chain.Reverse();
            string[] nums = { "一", "二", "三", "四", "五", "六" };
            var line = Arc.InfoCard("世系", "TabDynasty", Arc.Badge(Generations(chief, c) + " 世", "m"));
            var ll = Arc.ListBlock();
            for (int i = 0; i < chain.Count; i++)
            {
                var p = chain[i];
                Arc.ListRow(ll, w.FullName(p), Arc.Badge(nums[i] + "世" + (p.IsRuler() ? " · 君主" : ""), p.IsRuler() ? "y" : "m"), YearText(w, p.BirthMonth) + " 年生", null, null, null, () => { activePerson = p; x.Go("kin"); });
            }
            if (chain.Count == 0) Arc.ListRow(ll, "无族长", null, null);
            line.Add(ll);
            line.Add(Arc.Verdict(chain.Count <= 1 ? "开族未久" : "自" + w.FullName(chain[0]) + "开族，传 " + chain.Count + " 世"));
            Arc.GridAdd(grid, line);

            var ids = new HashSet<int>(w.People.Where(p => p.Clan == c).Select(p => p.Id));
            var news = w.Annals.Entries.Where(e => (e.Kind == AnnalKind.Accession || e.Kind == AnnalKind.NewDynasty || e.Kind == AnnalKind.RulerDied) && ids.Contains(e.B)).Reverse().Take(4).ToList();
            var recent = Arc.InfoCard("近事", "iconHistory", Arc.Badge("君位", "m"));
            var rl = Arc.ListBlock();
            foreach (var e in news) Arc.ListRow(rl, AnnalText(w, e), null, "第 " + YearText(w, e.Month) + " 年");
            if (news.Count == 0) Arc.ListRow(rl, "族中无人登位", null, null);
            recent.Add(rl);
            recent.Add(Arc.Verdict(news.Count == 0 ? "尚无君位之事" : "最近：" + AnnalText(w, news[0])));
            Arc.GridAdd(grid, recent);
            page.Add(grid);
            page.Add(Arc.Note("· 点族人看他的亲属；左栏「天下宗族」比较各族"));
        }

        // ---------- 史书 · 编年 ----------
        void BuildAnnalsV5(EmpireWorld w, VisualElement page, ArchiveWindow x, Realm realm)
        {
            IEnumerable<AnnalEntry> all = Enumerable.Range(0, w.Annals.Entries.Count).Select(i => w.Annals.Entries[w.Annals.Entries.Count - 1 - i]);
            if (annalsKind != 0) all = all.Where(e => AnnalGroup(e.Kind) == annalsKind);
            if (annalsRealmOnly && realm != null) all = all.Where(e => RealmsOf(e).Contains(realm.Id));
            var list = all.Take(400).ToList();
            int year = YearOf(w, w.Month);
            var thisYear = list.Where(e => YearOf(w, e.Month) == year).ToList();
            page.Add(Arc.PageHead("编年", AnnalGroupNames[annalsKind] + (annalsRealmOnly && realm != null ? " · " + realm.Name : "") + "：天下这些年发生了什么"));
            page.Add(Arc.KpiRow(("今年", thisYear.Count + " 件", null, false), ("改朝", thisYear.Count(e => e.Kind == AnnalKind.NewDynasty) + " 次", "o", false),
                ("立国", thisYear.Count(e => e.Kind == AnnalKind.RealmFounded || e.Kind == AnnalKind.Secession) + " 国", "g", true), ("亡国", thisYear.Count(e => e.Kind == AnnalKind.RealmEnded) + " 国", "r", false)));
            var grid = Arc.Grid(2);
            string[] tags = { "", "君位", "兴亡", "城池", "战事" };
            var years = list.GroupBy(e => YearOf(w, e.Month)).Take(2).ToList();
            foreach (var g in years)
            {
                var card = Arc.InfoCard("第 " + (g.Key >= 1 ? g.Key.ToString() : "前" + (1 - g.Key)) + " 年", "iconHistory", Arc.Badge(g.Count() + " 件", "m"));
                var bl = Arc.ListBlock();
                foreach (var e in g.Take(6))
                {
                    int target = RealmsOf(e).FirstOrDefault(id => id >= 0 && id < w.Realms.Count && w.Realms[id].Alive);
                    var r = target >= 0 && target < w.Realms.Count && w.Realms[target].Alive ? w.Realms[target] : null;
                    int grp = AnnalGroup(e.Kind);
                    Arc.ListRow(bl, MonthName(e.Month) + "　" + AnnalText(w, e), Arc.Badge(tags[grp], AnnalTones[grp] ?? "m"), null, null, null, null, r != null ? () => FocusCoreRealm(r) : (Action)null);
                }
                card.Add(bl);
                var kinds = g.GroupBy(e => AnnalGroup(e.Kind)).OrderByDescending(k => k.Count()).First();
                card.Add(Arc.Verdict("这一年以" + tags[kinds.Key] + "之事最多（" + kinds.Count() + " 件）"));
                Arc.GridAdd(grid, card);
            }
            if (years.Count == 0) page.Add(Arc.Empty("没有符合的记载。"));

            var counts = new double[5];
            foreach (var e in w.Annals.Entries) counts[AnnalGroup(e.Kind)]++;
            var parts = new List<(string, double)> { ("城池", counts[3]), ("战事", counts[4]), ("兴亡", counts[2]), ("君位", counts[1]) };
            var dist = Arc.InfoCard("大事分布", "TabWarfare", Arc.Badge("自开辟以来", "m"));
            dist.Add(ShareBar(parts.OrderByDescending(p => p.Item2).ToList()));
            var dl = Arc.ListBlock();
            foreach (var p in parts.OrderByDescending(p => p.Item2)) Arc.ListRow(dl, p.Item1, null, ((int)p.Item2).ToString("N0") + " 件");
            dist.Add(dl);
            double sum = Math.Max(1, counts.Sum());
            dist.Add(Arc.Verdict("战事占 " + Mathf.RoundToInt((float)(100 * counts[4] / sum)) + "%，" + (counts[4] / sum > .3 ? "天下多事" : "天下尚算太平")));
            Arc.GridAdd(grid, dist);

            var own = Arc.InfoCard("本国大事", "ChineseCrown", Arc.Badge(realm != null ? realm.Name : "未选国", "y"));
            var ol = Arc.ListBlock();
            if (realm != null)
                foreach (var e in Enumerable.Range(0, w.Annals.Entries.Count).Select(i => w.Annals.Entries[w.Annals.Entries.Count - 1 - i])
                             .Where(e => AnnalGroup(e.Kind) != 3 && RealmsOf(e).Contains(realm.Id)).Take(5))
                {
                    int grp = AnnalGroup(e.Kind);
                    Arc.ListRow(ol, "第 " + YearText(w, e.Month) + " 年　" + AnnalText(w, e), Arc.Badge(tags[grp], AnnalTones[grp] ?? "m"), null);
                }
            if (ol.childCount == 0) Arc.ListRow(ol, "尚无大事", null, null);
            own.Add(ol);
            own.Add(Arc.Verdict(realm == null ? "点地图上的国，这里列它的大事" : realm.Name + "立国 " + realm.GetAge() + " 年"));
            Arc.GridAdd(grid, own);
            page.Add(grid);

            // The full record below the cards, for reading back further.
            var more = Arc.InfoCard("更早的记载", "iconHistory", Arc.Badge("最近 200 条", "m"));
            AddAnnalLines(w, more, list.Skip(years.Sum(g => Math.Min(6, g.Count()))), 200);
            page.Add(more);
            page.Add(Arc.Note("· 左栏筛大事种类、只看本国；点一条跳到相关的国"));
        }
    }
}
