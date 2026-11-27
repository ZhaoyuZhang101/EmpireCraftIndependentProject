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
    // The first god powers on the core (docs/design/ui-style.md, reference v5): 外交 (pick a
    // realm, then another: the diplomacy window's 战与和 page), 停战 (pick two realms at war),
    // 称帝 (pick a realm: it proclaims an empire, as the mod's god power) and 立君 (pick a realm:
    // the window to put someone on its throne). The rules are the ported core's own
    // (DiplomacyActions, EmpireManager, WorldOps.SetRuler); the windows only show and call them.
    public sealed partial class FlatWorldApp
    {
        enum CorePower { None, Diplomacy, Truce, Empire, Crown }

        CorePower corePower;
        Realm powerFirst;

        Realm diploFocus, diploOther;
        int diploGoal;                     // index into DiplomacyActions.Goals
        string diploToast; bool diploToastBad;
        MapPicker diploMap; EmpireWorld diploMapWorld; Realm diploMapAim;
        int diploMapMode = 1;              // 0 relation, 1 may we go to war
        string diploHover;

        Realm crownRealm; string crownToast; bool crownToastBad;

        // ---------- the power buttons ----------
        HudTool HudCorePower(string name, string icon, CorePower power, string hint)
        {
            return new HudTool
            {
                Name = name, Icon = icon, Hint = hint,
                Run = () => ChooseCorePower(corePower == power ? CorePower.None : power),
                IsOn = () => corePower == power
            };
        }

        void ChooseCorePower(CorePower power)
        {
            corePower = power; powerFirst = null;
            if (power != CorePower.None && toolField != null) toolField.index = 0;
            switch (power)
            {
                case CorePower.Diplomacy: SetStatus("外交：先点本国，再点另一国。"); break;
                case CorePower.Truce: SetStatus("停战：先点交战的一方，再点它的敌国；同一国点两下，它所有的仗都停。"); break;
                case CorePower.Empire: SetStatus("称帝：点一国，它即称帝。"); break;
                case CorePower.Crown: SetStatus("立君：点一国，挑人立为君主。"); break;
            }
        }

        // Called from Pick when the map is clicked. True when a power took the click.
        bool CorePowerPick(int tile)
        {
            if (corePower == CorePower.None) return false;
            var w = CoreHere();
            if (w == null || tile < 0 || tile >= w.TileCity.Length) return true;
            var realm = SafeValue(() => w.RealmAt(tile), null);
            if (realm == null || !realm.Alive) { SetStatus("这里无主。"); return true; }
            switch (corePower)
            {
                case CorePower.Diplomacy:
                    if (powerFirst == null || !powerFirst.Alive) { powerFirst = realm; SetStatus("外交：已选" + realm.Name + "，再点另一国。"); return true; }
                    if (realm == powerFirst) { OpenCoreDiplomacy(realm, null); }
                    else OpenCoreDiplomacy(powerFirst, realm);
                    powerFirst = null; corePower = CorePower.None;
                    return true;
                case CorePower.Truce:
                    if (powerFirst == null || !powerFirst.Alive) { powerFirst = realm; SetStatus("停战：已选" + realm.Name + "，再点它的敌国。"); return true; }
                    CoreTruce(powerFirst, realm == powerFirst ? null : realm);
                    powerFirst = null;
                    return true;
                case CorePower.Empire:
                    CoreProclaimEmpire(realm);
                    return true;
                case CorePower.Crown:
                    OpenCoreCrown(realm);
                    corePower = CorePower.None;
                    return true;
            }
            return true;
        }

        // ---------- 停战 / 称帝 ----------
        static DiplomacyActions.Action CoreAction(string key) =>
            DiplomacyActions.Groups.SelectMany(g => g.actions).FirstOrDefault(a => a.key == key);

        void CoreTruce(Realm a, Realm b)
        {
            var truce = CoreAction("diplomacy_act_truce");
            if (truce == null) { SetStatus("停战暂时用不了。"); return; }
            var enemies = b != null ? new List<Realm> { b }
                : SafeValue(() => a.GetEnemies().Where(e => e != null && e.Alive).Distinct().ToList(), new List<Realm>());
            if (enemies.Count == 0) { SetStatus(a.Name + "眼下没有在打仗。"); return; }
            int done = 0; string failure = null;
            foreach (var enemy in enemies)
            {
                if (b != null && !a.IsAtWarWith(enemy)) { failure = "两国没在打仗"; continue; }
                string r = SafeValue(() => DiplomacyActions.Do(truce, a, enemy, WarGoal.None), "diplomacy_reason_failed");
                if (r == null) done++; else failure = LM.Get(r);
            }
            SetStatus(done > 0 ? a.Name + "与" + (b != null ? b.Name : done + " 国") + "罢兵，" + WorldRules.m.minimum_years_between_wars + " 年内不得再战。"
                               : "停战没办成：" + (failure ?? "两国没在打仗") + "。");
            hudWindow?.Refresh();
        }

        // The mod's 称帝 god power (EmpireFormButton): an empire for the realm; one already in
        // another empire leaves it first (the old empire may not take it back for fifteen
        // years); a modern state forms a government instead.
        void CoreProclaimEmpire(Realm k)
        {
            try
            {
                bool modern = k.GetRegime()?.type == RegimeType.Modern;
                if (k.IsEmpire())
                {
                    var existing = k.GetEmpire();
                    if (!modern || existing?.data?.constitutional_economy == null) { SetStatus(k.Name + "已是帝国。"); return; }
                    existing.data.constitutional_economy.revolutionary_government = true;
                    SetStatus(k.Name + "改组为正式政府。");
                    hudWindow?.Refresh();
                    return;
                }
                if (k.IsInEmpire())
                {
                    var former = k.GetEmpire();
                    MembershipCooldown.ByGod(() => former.leave(k));
                }
                Empire empire = modern ? ModernStateFormationSystem.FormGovernment(k)
                                       : ModClass.EMPIRE_MANAGER?.NewEmpire(k, allowCultureRival: true);
                if (empire == null) { SetStatus(k.Name + "称帝未成。"); return; }
                empire.data.formed_by_god = true;
                if (modern && empire.data.constitutional_economy != null) empire.data.constitutional_economy.revolutionary_government = true;
                string name = Safe(() => empire.GetEmpireName(), k.Name);
                SetStatus(modern ? k.Name + "成立政府：" + name + "。" : k.Name + "称帝，建「" + name + "」。");
                hudWindow?.Refresh();
            }
            catch (Exception e) { SetStatus(k.Name + "称帝未成：" + e.Message); Debug.LogException(e); }
        }

        // ---------- 外交 window ----------
        void OpenCoreDiplomacy(Realm focus, Realm other)
        {
            var w = CoreHere();
            if (w == null || focus == null) return;
            diploFocus = focus; diploOther = other; diploToast = null; diploMapAim = null;
            var win = new ArchiveWindow("外交", "diplomacy", CloseHudWindow, "overview", "pair", "war");
            win.Rail = (x, rail) => BuildDiploRail(x, rail);
            win.Main = (x, page) => BuildDiploPage(x, page);
            OpenHudWindow("diplomacy", win, other == null ? 0 : 2);
        }

        void BuildDiploRail(ArchiveWindow x, VisualElement rail)
        {
            var w = CoreHere();
            if (w == null || diploFocus == null) return;
            var pair = Arc.Card();
            pair.Add(Arc.Row(Arc.Flag(diploFocus.Color, First(diploFocus.Name), true), Arc.Text(diploFocus.Name, "ec-li-name", "y"), Arc.Fill(), Arc.Badge("本国", "m")));
            if (diploOther != null)
                pair.Add(Arc.Row(Arc.Flag(diploOther.Color, First(diploOther.Name), true), Arc.Text(diploOther.Name, "ec-li-name"), Arc.Fill(), Arc.Badge("对方", "m")));
            rail.Add(pair);
            var menu = Arc.Card();
            menu.Add(Arc.MenuItem("iconKingdom", "总览", SafeValue(() => w.LiveRealms.Count - 1, 0).ToString(), x.Page == "overview", () => x.Go("overview")));
            menu.Add(Arc.MenuItem("TabDependencies", "两国", null, x.Page == "pair", diploOther == null ? null : (Action)(() => x.Go("pair"))));
            menu.Add(Arc.MenuItem("TabWarfare", "战与和", null, x.Page == "war", diploOther == null ? null : (Action)(() => x.Go("war"))));
            rail.Add(menu);
            rail.Add(Arc.Steps("怎么用", "iconQuestionMark", "在图上点别的国即换作对方",
                (Arc.StepState.Done, "点本国"),
                (diploOther != null ? Arc.StepState.Done : Arc.StepState.Now, "点对方（图上也能点）"),
                (diploOther != null ? Arc.StepState.Now : Arc.StepState.Todo, "挑目标，按「宣战」")));
        }

        void BuildDiploPage(ArchiveWindow x, VisualElement page)
        {
            var w = CoreHere();
            if (w == null || diploFocus == null || !diploFocus.Alive) { page.Add(Arc.Empty("这一国已亡。")); return; }
            if (diploOther != null && !diploOther.Alive) diploOther = null;
            string pageKey = diploOther == null ? "overview" : x.Page;
            switch (pageKey)
            {
                case "overview": BuildDiploOverview(w, x, page); break;
                case "pair": BuildDiploPair(w, x, page); break;
                default: BuildDiploWar(w, x, page); break;
            }
        }

        static string First(string name) => string.IsNullOrEmpty(name) ? "" : name.Substring(0, 1);

        // How a realm stands toward the focus for the war map: may we declare, with a worry, not at all.
        enum WarStanding { Self, Other, Open, Worry, AtWar, Friend, Far }

        WarStanding StandingOf(Realm r)
        {
            if (r == diploFocus) return WarStanding.Self;
            if (r == diploOther) return WarStanding.Other;
            if (diploFocus.IsAtWarWith(r)) return WarStanding.AtWar;
            var declare = CoreAction("diplomacy_act_declare");
            string check = declare == null ? "x" : SafeValue(() => DiplomacyActions.Check(declare, diploFocus, r), "x");
            if (check != null) return WarStanding.Friend;            // same empire and the like
            string warn = SafeValue(() => DiplomacyActions.Warn(declare, diploFocus, r), null);
            if (warn == null) return WarStanding.Open;
            if (warn == "diplomacy_reason_unreachable") return WarStanding.Far;
            if (warn == "diplomacy_reason_truce") return WarStanding.Worry;
            return WarStanding.Friend;                               // allies, pacts, kin…
        }

        static readonly Color32 MapSelf = new Color32(201, 162, 74, 255), MapOpen = new Color32(168, 80, 64, 255),
            MapWorry = new Color32(176, 122, 56, 255), MapFriend = new Color32(90, 138, 94, 255), MapFar = new Color32(58, 53, 50, 255),
            MapWar = new Color32(200, 60, 44, 255), MapNeutral = new Color32(96, 88, 76, 255);

        VisualElement DiploMap(EmpireWorld w)
        {
            if (diploMap == null || !ReferenceEquals(diploMapWorld, w))
            {
                diploMapWorld = w;
                diploMap = new MapPicker(w.Width, w.Height, 336, 128, 4);
                diploMapAim = null;
            }
            diploMap.KeyAt = t => { int c = w.TileCity[t]; if (c < 0) return -1; var r = w.Cities[c].Realm; return r != null && r.Alive ? r.Id : -1; };
            diploMap.IsWater = t => !w.Claimable(t);
            var standing = new Dictionary<int, WarStanding>();
            diploMap.LookOf = id =>
            {
                var r = id >= 0 && id < w.Realms.Count ? w.Realms[id] : null;
                if (r == null) return new MapPicker.Look { Fill = MapNeutral };
                if (!standing.TryGetValue(id, out var s)) standing[id] = s = StandingOf(r);
                var look = new MapPicker.Look { Label = r.Name, Clickable = r != diploFocus };
                if (diploMapMode == 0)
                {
                    look.Fill = s == WarStanding.Self ? MapSelf : s == WarStanding.AtWar ? MapWar : s == WarStanding.Friend ? MapFriend : (Color32)Color.Lerp(Hud.Rgb(r.Color), new Color(.24f, .2f, .17f), .55f);
                }
                else look.Fill = s switch
                {
                    WarStanding.Self => MapSelf, WarStanding.Other => (Color32)Hud.Rgb(r.Color), WarStanding.Open => MapOpen,
                    WarStanding.Worry => MapWorry, WarStanding.AtWar => MapWar, WarStanding.Friend => MapFriend, _ => MapFar
                };
                if (s == WarStanding.Other) { look.Fill = Hud.Rgb(r.Color); look.Edge = MapPicker.Edge.White; }
                if (s == WarStanding.Self) look.Edge = MapPicker.Edge.Gold;
                return look;
            };
            diploMap.Pick = id =>
            {
                var r = id >= 0 && id < w.Realms.Count ? w.Realms[id] : null;
                if (r == null || !r.Alive || r == diploFocus) return;
                diploOther = r; diploToast = null;
                if (hudWindow is ArchiveWindow aw) { if (aw.Page == "overview") aw.Go("war"); else aw.Refresh(); }
            };
            diploMap.Hover = id =>
            {
                var r = id >= 0 && id < w.Realms.Count ? w.Realms[id] : null;
                diploHover = r == null ? null : r.Name;
            };
            var aim = diploOther ?? diploFocus;
            if (!ReferenceEquals(aim, diploMapAim))
            {
                diploMapAim = aim;
                var tiles = new List<int>();
                foreach (var c in diploFocus.Cities) tiles.AddRange(c.Tiles);
                if (diploOther != null) foreach (var c in diploOther.Cities) tiles.AddRange(c.Tiles);
                diploMap.FocusOn(tiles, diploOther != null ? .5f : 1.2f);
            }
            diploMap.Redraw();
            return diploMap.Root;
        }

        bool Borders(EmpireWorld w, Realm a, Realm b)
        {
            int W = w.Width, H = w.Height;
            foreach (var c in a.Cities)
                foreach (int t in c.Tiles)
                {
                    int x = t % W, y = t / W;
                    int[] n = { y * W + (x + 1) % W, y * W + (x - 1 + W) % W, y + 1 < H ? t + W : -1, y > 0 ? t - W : -1 };
                    foreach (int m in n) { if (m < 0) continue; int ci = w.TileCity[m]; if (ci >= 0 && w.Cities[ci].Realm == b) return true; }
                }
            return false;
        }

        static double People(Realm r) { double s = 0; foreach (var c in r.Cities) if (c.Alive) s += c.Population; return s; }

        // 战与和: one question — do these two go to war, and how does it stop.
        void BuildDiploWar(EmpireWorld w, ArchiveWindow x, VisualElement page)
        {
            Realm a = diploFocus, b = diploOther;
            page.Add(Arc.PageHead("战与和", a.Name + "与" + b.Name + "：打不打，怎么停"));
            if (diploToast != null) page.Add(Arc.Toast(null, diploToast, diploToastBad));
            bool atWar = a.IsAtWarWith(b);
            var opinion = SafeValue(() => w.Diplomacy.GetOpinion(a, b), null);
            int op = opinion?.Total ?? 0;
            double pa = SafeValue(() => WarSituation.Power(a), 0d), pb = SafeValue(() => WarSituation.Power(b), 0d);
            double ratio = pb > 0 ? pa / pb : 0;
            double ended = SafeValue(() => w.Diplomacy.GetRelation(a, b)?.Record?.LastWarEnded ?? -1d, -1d);
            float since = ended > 0 ? SafeValue(() => GameClock.YearsSince(ended), -1f) : -1f;
            int truceYears = WorldRules.m.minimum_years_between_wars;
            page.Add(Arc.KpiRow(
                ("两国", atWar ? "交战" : a.HasAlliance() && a.Alliance == b.Alliance ? "同盟" : "和平", atWar ? "r" : null, false),
                ("好感", (op > 0 ? "+" : "") + op, op < 0 ? "r" : op > 0 ? "g" : null, false),
                ("国力比", pb > 0 ? ratio.ToString("0.0") + " : 1" : "—", ratio >= 1.2 ? "g" : ratio > 0 && ratio <= .83 ? "r" : null, true),
                ("上次停战", since < 0 ? "从未交战" : Mathf.FloorToInt(since) + " 年前", since >= 0 && since < truceYears ? "o" : null, false)));

            var grid = Arc.Grid(2);
            // map
            int open = 0;
            foreach (var r in w.LiveRealms) if (r != a && SafeValue(() => StandingOf(r), WarStanding.Far) == WarStanding.Open) open++;
            var mapCard = Arc.InfoCard("天下简图", "iconTitleLayer", Arc.Badge("可宣战 " + open + " 国", open > 0 ? "g" : "m"));
            var modes = Arc.Row(Arc.Chip("关系", diploMapMode == 0, () => { diploMapMode = 0; x.Refresh(); }), Arc.Chip("可否开战", diploMapMode == 1, () => { diploMapMode = 1; x.Refresh(); }));
            modes.AddToClassList("ec-mapmodes");
            mapCard.Add(modes);
            mapCard.Add(DiploMap(w));
            mapCard.Add(diploMapMode == 1
                ? Arc.Legend((MapSelf, "本国"), (Hud.Rgb(b.Color), "对方"), (MapOpen, "可宣战"), (MapWorry, "停战期"), (MapFriend, "盟友·有约"), (MapFar, "够不着"))
                : Arc.Legend((MapSelf, "本国"), (MapWar, "交战"), (MapFriend, "盟友·有约"), (MapNeutral, "其余")));
            bool border = Borders(w, a, b);
            bool reach = SafeValue(() => WarReach.MayDeclare(a, b, WarKinds.Normal), true);
            mapCard.Add(Arc.Verdict(border ? b.Name + "与本国接壤，可直接出兵" : reach ? b.Name + "不接壤，但兵力够得着" : b.Name + "远隔，够不着：出兵要远征"));
            Arc.GridAdd(grid, mapCard);

            // comparison
            Color ca = Hud.Rgb(a.Color), cb = Hud.Rgb(b.Color);
            double wa = SafeValue(() => (double)WarSituation.SideWarriors(a), 0d), wb = SafeValue(() => (double)WarSituation.SideWarriors(b), 0d);
            double ta = SafeValue(() => (double)w.Ledger.Realm(a.Id).Treasury, 0d), tb = SafeValue(() => (double)w.Ledger.Realm(b.Id).Treasury, 0d);
            var cmp = Arc.InfoCard("两国对比", "TabMilitary", ratio >= 1.2 ? Arc.Badge("本国占优", "g") : ratio > 0 && ratio <= .83 ? Arc.Badge("对方占优", "r") : Arc.Badge("旗鼓相当", "m"));
            cmp.Add(Arc.Legend((ca, a.Name), (cb, b.Name)));
            cmp.Add(Arc.Tug("iconCity", a.Cities.Count, b.Cities.Count, a.Cities.Count.ToString(), b.Cities.Count.ToString(), ca, cb));
            cmp.Add(Arc.Tug("iconPopulation50", People(a), People(b), Arc.Count(People(a)), Arc.Count(People(b)), ca, cb));
            cmp.Add(Arc.Tug("TabMilitary", wa, wb, Arc.Count(wa), Arc.Count(wb), ca, cb));
            cmp.Add(Arc.Tug("wealth_layer", Math.Max(0, ta), Math.Max(0, tb), Arc.Count(ta), Arc.Count(tb), ca, cb));
            var facts = Arc.ListBlock();
            int allies = b.HasAlliance() ? SafeValue(() => b.Alliance.Members.Count(m => m != b && m.Alive), 0) : 0;
            Arc.ListRow(facts, b.Name + "的盟友", Arc.Badge(allies > 0 ? allies + " 国" : "无", allies > 0 ? "o" : "m"), null);
            Arc.ListRow(facts, "两国战事", Arc.Badge(atWar ? "交战中" : since < 0 ? "从未" : "停战", atWar ? "r" : "m"), since < 0 ? "—" : Mathf.FloorToInt(since) + " 年前");
            cmp.Add(facts);
            cmp.Add(Arc.Verdict(wb > 0 && wa >= wb * 1.4 ? "兵多四成以上，宜速战" : wa > 0 && wb >= wa * 1.4 ? "对方兵多，宜慎战" : "兵力相近，胜负看后劲"));
            Arc.GridAdd(grid, cmp);

            // declare war
            var declare = CoreAction("diplomacy_act_declare");
            string check = declare == null ? "diplomacy_reason_failed" : SafeValue(() => DiplomacyActions.Check(declare, a, b), "diplomacy_reason_failed");
            string warn = check == null ? SafeValue(() => DiplomacyActions.Warn(declare, a, b), null) : null;
            var goals = DiplomacyActions.Goals;
            diploGoal = Mathf.Clamp(diploGoal, 0, goals.Length - 1);
            string goalName = Safe(() => DiplomacyActions.GoalName(goals[diploGoal]), goals[diploGoal].ToString());
            var warCard = Arc.InfoCard("宣战", "plot_feudal_independence_war",
                check != null ? Arc.Badge(LM.Get(check), "m") : warn != null ? Arc.Badge(LM.Get(warn), "o") : Arc.Badge("可以宣战", "g"));
            warCard.Add(Arc.ChipGrid(4, goals.Select(g => Safe(() => DiplomacyActions.GoalName(g), g.ToString())).ToList(), diploGoal, i => { diploGoal = i; x.Refresh(); }));
            var goalLine = Arc.ListBlock();
            Arc.ListRow(goalLine, goalName + "：" + GoalNote(goals[diploGoal]), null, null);
            warCard.Add(goalLine);
            warCard.Add(Arc.Verdict(check != null ? "眼下不能宣战：" + LM.Get(check)
                : warn == "diplomacy_reason_truce" ? "停战才 " + Mathf.Max(0, Mathf.FloorToInt(since)) + " 年，此时宣战会失信于诸国"
                : warn == "diplomacy_reason_unreachable" ? "够不着对方，出兵要远征"
                : warn != null ? "有约在先（" + LM.Get(warn) + "），宣战会背约"
                : "此时宣战，师出有名", check != null ? "m" : warn != null ? "o" : "g"));
            warCard.Add(Arc.Fill());
            warCard.Add(Arc.Button("以「" + goalName + "」向" + b.Name + "宣战", null, check == null ? Arc.ButtonKind.Red : Arc.ButtonKind.Off, () =>
            {
                string r = SafeValue(() => DiplomacyActions.Do(declare, a, b, goals[diploGoal]), "diplomacy_reason_failed");
                diploToastBad = r != null;
                diploToast = r == null ? "已宣战：以「" + goalName + "」向" + b.Name + "开战" : "宣战没办成：" + LM.Get(r);
                if (r == null) SetStatus(a.Name + "以「" + goalName + "」向" + b.Name + "宣战。");
                x.Refresh();
            }, true));
            Arc.GridAdd(grid, warCard);

            // peace and truce
            var peace = CoreAction("diplomacy_act_peace"); var truce = CoreAction("diplomacy_act_truce");
            string peaceCheck = peace == null ? "diplomacy_reason_failed" : SafeValue(() => DiplomacyActions.Check(peace, a, b), "diplomacy_reason_failed");
            string truceCheck = truce == null ? "diplomacy_reason_failed" : SafeValue(() => DiplomacyActions.Check(truce, a, b), "diplomacy_reason_failed");
            var stop = Arc.InfoCard("议和与停战", "plot_force_stop_war", Arc.Badge(atWar ? "交战中" : "未交战", atWar ? "r" : "m"));
            var rows = Arc.ListBlock();
            Arc.ListRow(rows, "议和", Arc.Badge(peaceCheck == null ? "按议和收场" : LM.Get(peaceCheck), peaceCheck == null ? "c" : "m"), null,
                Arc.Button("议和", null, peaceCheck == null ? Arc.ButtonKind.Gold : Arc.ButtonKind.Off, () => DiploDo(x, peace, "议和", "两国议和")));
            Arc.ListRow(rows, "强制停战", Arc.Badge(truceYears + " 年不战", "c"), null,
                Arc.Button("停战", null, truceCheck == null ? Arc.ButtonKind.Gold : Arc.ButtonKind.Off, () => DiploDo(x, truce, "停战", "已强制停战：两国 " + truceYears + " 年内不得开战")));
            stop.Add(rows);
            stop.Add(Arc.Verdict(atWar ? "议和照战况收场；强制停战另加 " + truceYears + " 年不得再战" : "强制停战后，" + truceYears + " 年内两国都不能再开战"));
            Arc.GridAdd(grid, stop);
            page.Add(grid);

            var foot = Arc.LockRow("更多外交", "后续开放", ("plot_kingdom_start_join_taken_alliance", "结盟"), ("force_vassal", "附庸"), ("jimi_policy", "朝贡"), ("specificClanIcon", "联姻"));
            foot.Insert(0, Arc.Note("· 点图上的国换作对方；滚轮缩放，按住拖动" + (diploHover != null ? "　指着：" + diploHover : "")));
            page.Add(foot);
        }

        void DiploDo(ArchiveWindow x, DiplomacyActions.Action action, string name, string success)
        {
            string r = action == null ? "diplomacy_reason_failed" : SafeValue(() => DiplomacyActions.Do(action, diploFocus, diploOther, WarGoal.None), "diplomacy_reason_failed");
            diploToastBad = r != null;
            diploToast = r == null ? success : name + "没办成：" + LM.Get(r);
            if (r == null) SetStatus(diploFocus.Name + "与" + diploOther.Name + "：" + success + "。");
            x.Refresh();
        }

        // A line on what each war goal asks for (interface wording; the goal names come from the core).
        static string GoalNote(WarGoal goal) => goal switch
        {
            WarGoal.夺城 => "攻下对方一座城即胜，城归本国",
            WarGoal.侵略 => "攻占对方边地，占多少得多少",
            WarGoal.吞并 => "攻下都城，整国并入本国",
            WarGoal.迫使附庸 => "战胜后对方奉本国为宗主",
            WarGoal.保护国 => "战胜后对方成为本国的保护国",
            WarGoal.租借 => "战胜后租借对方一处港口",
            WarGoal.赔款 => "战胜后对方赔付钱粮",
            WarGoal.劫掠 => "只抢不占，抢够就退",
            WarGoal.扶立 => "战胜后另立对方的君主",
            WarGoal.争法理 => "争夺本国宣称的法理之地",
            WarGoal.通商 => "战胜后对方开港通商",
            WarGoal.改宗 => "战胜后对方改奉本国的宗教",
            _ => "按战况议和"
        };

        // 总览: every realm as the focus sees it.
        void BuildDiploOverview(EmpireWorld w, ArchiveWindow x, VisualElement page)
        {
            Realm a = diploFocus;
            page.Add(Arc.PageHead("总览", a.Name + "与天下各国：谁友谁敌"));
            var others = w.LiveRealms.Where(r => r != a).Select(r => (r, op: SafeValue(() => w.Diplomacy.GetOpinion(a, r)?.Total ?? 0, 0))).OrderByDescending(t => t.op).ToList();
            int wars = others.Count(t => a.IsAtWarWith(t.r)), friends = others.Count(t => t.op > 0);
            page.Add(Arc.KpiRow(("国数", others.Count.ToString(), null, false), ("交战", wars.ToString(), wars > 0 ? "r" : null, false),
                ("友好", friends.ToString(), friends > 0 ? "g" : null, false), ("盟约", a.HasAlliance() ? "在盟" : "无", a.HasAlliance() ? "c" : null, false)));
            var grid = Arc.Grid(2);
            int mode = diploMapMode; diploMapMode = 0;
            var mapCard = Arc.InfoCard("天下简图", "iconTitleLayer", Arc.Badge("点一国看战与和", "m"));
            mapCard.Add(DiploMap(w));
            diploMapMode = mode;
            mapCard.Add(Arc.Legend((MapSelf, "本国"), (MapWar, "交战"), (MapFriend, "盟友·有约"), (MapNeutral, "其余")));
            mapCard.Add(Arc.Verdict(wars > 0 ? "正与 " + wars + " 国交战" : "眼下四境无战事", wars > 0 ? "r" : "o"));
            Arc.GridAdd(grid, mapCard);
            var list = Arc.InfoCard("各国好感", "diplomacy", Arc.Badge("点一行看战与和", "m"));
            var block = Arc.ListBlock();
            foreach (var (r, op) in others.Take(10))
                Arc.ListRow(block, r.Name, a.IsAtWarWith(r) ? Arc.Badge("交战", "r") : op > 0 ? Arc.Badge("友好", "g") : op < 0 ? Arc.Badge("交恶", "o") : Arc.Badge("平常", "m"),
                    (op > 0 ? "+" : "") + op, null, Hud.Rgb(r.Color), null, () => { diploOther = r; diploToast = null; x.Go("war"); });
            list.Add(block);
            if (others.Count > 10) list.Add(Arc.Note("另有 " + (others.Count - 10) + " 国"));
            var best = others.FirstOrDefault(); var worst = others.LastOrDefault();
            list.Add(Arc.Verdict(others.Count == 0 ? "天下只此一国" : "与" + best.r.Name + "最好，与" + worst.r.Name + "最坏"));
            Arc.GridAdd(grid, list);
            page.Add(grid);
            page.Add(Arc.Note("· 点图上或名单里的国，进两国的「战与和」"));
        }

        // 两国: what has passed between them.
        void BuildDiploPair(EmpireWorld w, ArchiveWindow x, VisualElement page)
        {
            Realm a = diploFocus, b = diploOther;
            page.Add(Arc.PageHead("两国", a.Name + "与" + b.Name + "：往来如何"));
            var shared = w.Annals.Entries.Where(e => (e.A == a.Id || e.B == a.Id || e.C == a.Id) && (e.A == b.Id || e.B == b.Id || e.C == b.Id) &&
                (e.Kind == AnnalKind.WarStarted || e.Kind == AnnalKind.WarEnded || e.Kind == AnnalKind.AllianceFormed || e.Kind == AnnalKind.AllianceEnded)).Reverse().Take(12).ToList();
            var op = SafeValue(() => w.Diplomacy.GetOpinion(a, b), null); var back = SafeValue(() => w.Diplomacy.GetOpinion(b, a), null);
            page.Add(Arc.KpiRow(("本国对对方", ((op?.Total ?? 0) > 0 ? "+" : "") + (op?.Total ?? 0), (op?.Total ?? 0) < 0 ? "r" : "g", false),
                ("对方对本国", ((back?.Total ?? 0) > 0 ? "+" : "") + (back?.Total ?? 0), (back?.Total ?? 0) < 0 ? "r" : "g", false),
                ("往来大事", shared.Count.ToString(), null, false), ("接壤", Borders(w, a, b) ? "是" : "否", null, false)));
            var grid = Arc.Grid(2);
            var parts = Arc.InfoCard("好感由来", "diplomacy");
            var block = Arc.ListBlock();
            if (op != null) foreach (var kv in op.Results) Arc.ListRow(block, kv.Key.ToString(), null, (kv.Value > 0 ? "+" : "") + kv.Value);
            if (block.childCount == 0) Arc.ListRow(block, "无特别缘由", null, "0");
            parts.Add(block);
            parts.Add(Arc.Verdict((op?.Total ?? 0) < 0 ? "积怨在先，容易起衅" : "关系尚可"));
            Arc.GridAdd(grid, parts);
            var annals = Arc.InfoCard("往来大事", "iconHistory");
            var ab = Arc.ListBlock();
            foreach (var e in shared) Arc.ListRow(ab, Safe(() => AnnalText(w, e)), null, YearText(w, e.Month) + " 年");
            if (shared.Count == 0) Arc.ListRow(ab, "两国素无往来", null, null);
            annals.Add(ab);
            annals.Add(Arc.Verdict(shared.Count == 0 ? "素无往来" : "最近一件：" + Safe(() => AnnalText(w, shared[0]))));
            Arc.GridAdd(grid, annals);
            page.Add(grid);
        }

        // ---------- 立君 window ----------
        void OpenCoreCrown(Realm realm)
        {
            if (CoreHere() == null || realm == null) return;
            crownRealm = realm; crownToast = null;
            var win = new ArchiveWindow("立君", "ChineseCrown", CloseHudWindow, "crown");
            win.Rail = (x, rail) => BuildCrownRail(x, rail);
            win.Main = (x, page) => BuildCrownPage(x, page);
            OpenHudWindow("crown", win);
        }

        void BuildCrownRail(ArchiveWindow x, VisualElement rail)
        {
            var r = crownRealm;
            if (r == null) return;
            var id = Arc.Card();
            id.Add(Arc.Row(Arc.Flag(r.Color, First(r.Name), true), Arc.Text(r.Name, "ec-li-name", "y")));
            id.Add(Arc.Note(RegimeOf(r) + " · " + CultureOf(r) + " · 立国 " + SafeValue(() => r.GetAge(), 0) + " 年"));
            rail.Add(id);
            rail.Add(Arc.Steps("怎么用", "iconQuestionMark", null, (Arc.StepState.Done, "点一国"), (Arc.StepState.Now, "在名单里按「立为君主」")));
        }

        // How a person stands to the ruler, in the house's words.
        static string KinOf(Person ruler, Person p)
        {
            if (ruler == null) return "族人";
            if (p.Father == ruler || p.Mother == ruler)
            {
                if (p.Sex != Sex.Male) return "女";
                var sons = ruler.Children.Where(c => c.Sex == Sex.Male && c.Alive).OrderBy(c => c.BirthMonth).ToList();
                return sons.Count > 0 && sons[0] == p ? "长子" : "子";
            }
            if ((p.Father?.Father ?? p.Mother?.Father) == ruler || (p.Father?.Mother ?? p.Mother?.Mother) == ruler) return p.Sex == Sex.Male ? "孙" : "孙女";
            if (p.Father != null && p.Father == ruler.Father) return p.BirthMonth < ruler.BirthMonth ? (p.Sex == Sex.Male ? "兄" : "姊") : (p.Sex == Sex.Male ? "弟" : "妹");
            if (p.Father?.Father != null && p.Father.Father == ruler.Father) return "侄";
            return "族人";
        }

        static int KinRank(string kin) => kin switch { "长子" => 0, "子" => 1, "孙" => 2, "弟" => 3, "兄" => 3, "侄" => 4, "女" => 5, "孙女" => 6, _ => 7 };

        void BuildCrownPage(ArchiveWindow x, VisualElement page)
        {
            var w = CoreHere(); var r = crownRealm;
            if (w == null || r == null || !r.Alive) { page.Add(Arc.Empty("这一国已亡。")); return; }
            var ruler = r.Ruler;
            var house = r.RulingClan;
            var kin = w.Living.Where(p => p != ruler && p.Clan != null && p.Clan == house && p.Throne == null && w.AgeOf(p) >= Person.AdultAge)
                .Select(p => (p, kin: KinOf(ruler, p))).OrderBy(t => KinRank(t.kin)).ThenBy(t => t.p.BirthMonth).Take(6).ToList();
            var others = w.Living.Where(p => p.Throne == null && p.Realm == r && p.Clan != house && w.AgeOf(p) >= Person.AdultAge)
                .OrderByDescending(p => p.Renown).Take(4).ToList();
            page.Add(Arc.PageHead("立君", r.Name + "：换谁来坐这个位子"));
            if (crownToast != null) page.Add(Arc.Toast(null, crownToast, crownToastBad));
            int reign = ruler == null ? 0 : Math.Max(0, (w.Month - ReignStart(w, r)) / 12);
            page.Add(Arc.KpiRow(("当今", ruler == null ? "空悬" : w.FullName(ruler), ruler == null ? "r" : null, false),
                ("在位", ruler == null ? "—" : reign + " 年", null, false),
                ("继承法", Safe(() => LM.Get(r.GetHeirLaw().ToString())), null, false),
                ("候选", (kin.Count + others.Count) + " 人", null, true)));
            var grid = Arc.Grid(2);

            int age = ruler == null ? 0 : w.AgeOf(ruler);
            var now = Arc.InfoCard("当今君主", "ChineseCrown", ruler == null ? Arc.Badge("空悬", "r") : age >= 60 ? Arc.Badge("年迈", "o") : age < 20 ? Arc.Badge("年少", "c") : Arc.Badge("壮年", "g"));
            if (ruler != null)
            {
                now.Add(Arc.Row(Arc.Text(w.FullName(ruler), "ec-li-name", "y"), Arc.Note(age + " 岁 · " + (ruler.Clan != null ? ruler.Clan.Surname + "氏" : "") + " · 名望 " + ruler.Renown)));
                now.Add(Arc.Meter("治", ruler.Stewardship / 20f, ruler.Stewardship.ToString(), new Color32(214, 172, 78, 255)));
                now.Add(Arc.Meter("武", ruler.Warfare / 20f, ruler.Warfare.ToString(), new Color32(226, 103, 90, 255)));
                now.Add(Arc.Meter("智", ruler.Intelligence / 20f, ruler.Intelligence.ToString(), new Color32(127, 216, 234, 255)));
                now.Add(Arc.Meter("交", ruler.Diplomacy / 20f, ruler.Diplomacy.ToString(), new Color32(101, 214, 110, 255)));
            }
            now.Add(Arc.Verdict(ruler == null ? "君位空悬，宜早立新君" : age >= 60 ? "年过六旬，换人不致生乱" : "正当其年，换人恐生怨望"));
            Arc.GridAdd(grid, now);

            var kinCard = Arc.InfoCard("储君与宗室", "TabDynasty", Arc.Badge("继承次序", "m"));
            var kb = Arc.ListBlock();
            Arc.ListHead(kb, "名字", "治", "武", "智", " ");
            foreach (var (p, rel) in kin)
                CrownRow(kb, w, x, r, p, rel, kin.Count > 0 && p == kin[0].p ? "y" : "m", false);
            if (kin.Count == 0) Arc.ListRow(kb, "宗室无成年之人", null, null);
            kinCard.Add(kb);
            var able = kin.OrderByDescending(t => t.p.Stewardship).FirstOrDefault();
            kinCard.Add(Arc.Verdict(kin.Count == 0 ? "宗室乏人，只能立外姓" : w.FullName(able.p) + "治理最好" + (able.p == kin[0].p ? "，立他最顺" : "")));
            Arc.GridAdd(grid, kinCard);

            var outCard = Arc.InfoCard("外姓名人", "famous_talents", Arc.Badge("立之改朝", "r"));
            var ob = Arc.ListBlock();
            Arc.ListHead(ob, "名字", "治", "武", "智", " ");
            foreach (var p in others) CrownRow(ob, w, x, r, p, p.Warfare >= p.Stewardship ? "名将" : "名臣", "c", true);
            if (others.Count == 0) Arc.ListRow(ob, "国中没有成年外姓之人", null, null);
            outCard.Add(ob);
            outCard.Add(Arc.Verdict("立外姓即改朝换代，" + (house != null ? house.Surname + "氏" : "旧王室") + "会离心", "r"));
            Arc.GridAdd(grid, outCard);

            var after = Arc.InfoCard("立君之后", "iconHistory", Arc.Badge("记入史书", "m"));
            var ab = Arc.ListBlock();
            Arc.ListRow(ab, "原君", Arc.Badge("退位", "m"), "仍在世");
            Arc.ListRow(ab, "立宗室", Arc.Badge("王朝不变", "g"), "—");
            Arc.ListRow(ab, "立外姓", Arc.Badge("改朝换代", "r"), "国号照旧");
            after.Add(ab);
            after.Add(Arc.Verdict("新君当月即位"));
            Arc.GridAdd(grid, after);
            page.Add(grid);
            page.Add(Arc.Note("· 名单按继承次序排；点名字看人物"));
        }

        void CrownRow(VisualElement list, EmpireWorld w, ArchiveWindow x, Realm r, Person p, string rel, string tone, bool outsider)
        {
            var row = Arc.ListRow(list, w.FullName(p) + "  " + w.AgeOf(p) + "岁", Arc.Badge(rel, tone), null, null, null, null, () => OpenCoreClan(p.Clan, p));
            row.Add(Arc.Col(p.Stewardship.ToString())); row.Add(Arc.Col(p.Warfare.ToString())); row.Add(Arc.Col(p.Intelligence.ToString()));
            row.Add(Arc.Button(outsider ? "改朝立之" : "立为君主", null, outsider ? Arc.ButtonKind.Red : Arc.ButtonKind.Gold, () =>
            {
                try
                {
                    var old = r.Ruler;
                    WorldOps.SetRuler(w, r, p);
                    crownToastBad = false;
                    crownToast = (outsider ? "改朝换代：" : "") + w.FullName(p) + "即位" + (old != null ? "，" + w.FullName(old) + "退位" : "");
                    SetStatus(r.Name + "：" + crownToast + "。");
                }
                catch (Exception e) { crownToastBad = true; crownToast = "立君没办成：" + e.Message; }
                x.Refresh();
            }));
        }
    }
}
