// Generated from the EmpireCraft mod: Scripts/GeneralSystems/Decisions/DecisionSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EmpireCraft.Core
{

// 决议(用户叫"帝国决策")的运行：模组自己的一套，游戏和独立服务器都走它，日后移植新工程时照搬(用户 2026-11-14
// "记得用自己的方式实现，因为这个核心我之后要移植到 unity 新项目"，选"模组自建决议框架")。
// 决议的内容(谁能办、办成了做什么)仍写在各决议的定义里(EmpireCraftPlotsAddition 等，group_id = empirecraft_diplomacy)；
// 怎么发起、办多久、何时作罢由这里定，规则是模组自己的：
//   · 发起走天平(见 DecisionScale)：君主、城主每 ConsiderMonths 个月、只是族长的每 ChiefConsiderMonths 个月想一回；
//     月初主线程给本月该想的人取快照(Plan)，在别的核上逐人逐项过秤(推力÷阻力)，排出候选；到了各人的时候(Dispatch)，
//     主线程按候选从高到低跑决议自己的条件(读活的世界)、看付不付得起，第一项过的就发起；经过记进 LastCouncil 给天平页看；
//     门槛：身份(定义的 can_be_done_by_*)、资望(见 Standing，按名望折算，定义的 min_level 当资望档)、定义里的才干与名望下限、
//     世界法则、花费(先出个人钱袋，君主城主不够的走国库公费，见 Pay)、定义自己的条件；
//     别处直接发起的(国家 AI 点名、窗口按钮、终端)照旧经 newPlot，一并收进来(见 DecisionPatch)；
//   · 推进：每月一回，进度加 PaceBase + 才干/2(才干取内政、军事、智力、外交里最高的一项)；
//     办满就执行(开战类先等大军集结，见 WarReadinessSystem.Hold)；每月复核还该不该办(定义的 check_should_continue、目标还在)，
//     不该办了就作罢；办了 LapseFactor 倍预计工期(至少 LapseMinMonths 个月)还没办成也作罢；
//   · 原版的谋划对象只当个"牌子"(游戏里显示进度、别的系统查 actor.plot 用)：原版不再推进它(见 DecisionPatch)，进度由这里写上去；
//   · 存档：随模组存档(SaveData.decisions)；牌子没了的(独立服务器不存原版谋划)读档后按记下的目标重新立牌子。
// 帧率：过秤在别的核(ParallelWork)；主线程每月取一回快照，平时每帧只验到了时候的几个人、推几件到期的决议，合计限时 TickMilliseconds。
public sealed class DecisionCase
{
    public long plot = -1;
    public string def;
    public long initiator = -1;
    public double started;
    // 下一回推进的世界时刻：各件错开在一个月里，免得月初一帧推完全部
    public double next;
    public float effort, progress;
    // 发起时天平的读数与砝码(天平页看；别处点名发起的没有)
    public float ratio;
    public List<ScaleWeight> weights;
    // 亲自掌着的君主点名发起的(手机上，见 TerminalHost.DecisionScale)
    public bool personal;
    public long war = -1, kingdom = -1, city = -1, actor = -1, alliance = -1;
}

public static class DecisionSystem
{
    public const string Group = "empirecraft_diplomacy";
    // 君主、城主半年想一回，只是族长的一年想一回
    private const int ConsiderMonths = 6, ChiefConsiderMonths = 12, LapseMinMonths = 24, DispatchPerTick = 8, AdvancePerTick = 16;
    private const float PaceBase = 6f, LapseFactor = 4f, StandingPerTier = 85f, MinEffort = 10f;
    // 每帧推进与发起合用的时限(毫秒)；到了就留到下一帧
    private const double TickMilliseconds = 0.35d;
    private const int MustPerTick = 2;

    private static readonly Dictionary<string, PlotKind> Defs = new();
    private static readonly List<PlotKind> Initiable = new();
    private static readonly List<DecisionCase> Queue = new();
    private static int _queueCursor;
    private static readonly Dictionary<long, DecisionCase> Cases = new();
    private static readonly List<DecisionCase> Pending = new();
    private static readonly Dictionary<long, double> NextConsider = new();
    private static readonly List<Person> Pool = new();
    private static readonly SimWatch Clock = new();
    private static bool _adopted, _restoring;
    private static int _month = -1;

    public static int Started, Finished, Lapsed, Dropped;

    // 有人亲自掌着的(手机席位上的君主，见 TerminalHost)：天平不替他发起，只在他的"可议之事"里给群臣建议；
    // 别处点名发起的(打仗时的征粮、勤王、驰援、求援等)照旧
    public static Func<Person, bool> Steered;
    public static readonly Dictionary<string, int> FinishedBy = new();

    public static void ResetWorldState()
    {
        Cases.Clear();
        Pending.Clear();
        NextConsider.Clear();
        Pool.Clear();
        Queue.Clear();
        ReadyList.Clear();
        LastCouncil.Clear();
        Unfit.Clear();
        LastDone.Clear();
        _month = -1;
        _queueCursor = _readyCursor = 0;
    }

    public static bool Manages(PlotKind asset) => asset != null && Adopt() && Defs.ContainsKey(asset.Id);
    public static bool Manages(Plot plot) => plot?.data != null && Cases.ContainsKey(plot.data.id);
    public static PlotKind Def(string id) => id != null && Adopt() && Defs.TryGetValue(id, out PlotKind asset) ? asset : null;

    // 接手模组的决议：原版人物 AI(try_new_plot)从 basic_plots 里挑谋划，把模组的拿出来，改由这里发起；
    // 定义里 is_basic_plot 的由人物自行发起，其余只由别处点名发起
    private static bool Adopt()
    {
        if (_adopted) return true;
        PlotsLibrary library = PlotKinds.Library;
        if (library == null || library.list.Count == 0) return false;
        _adopted = true;
        foreach (PlotKind asset in library.list)
        {
            if (asset?.Id == null || asset.group_id != Group || Defs.ContainsKey(asset.Id)) continue;
            Defs[asset.Id] = asset;
            if (!asset.is_basic_plot) continue;
            Initiable.Add(asset);
        }
        library.basic_plots.RemoveAll(asset => asset != null && Defs.ContainsKey(asset.Id));
        LogService.LogInfo($"[EmpireCraft][决议] 接手 {Defs.Count} 项决议，其中 {Initiable.Count} 项由人物自行发起");
        return true;
    }

    // ───────── 立项(原版 newPlot 之后，见 DecisionPatch) ─────────
    public static void OnPlotCreated(Plot plot, Person author, PlotKind asset)
    {
        if (_restoring || plot?.data == null || author == null || !Manages(asset)) return;
        double now = GameClock.Now;
        Track(new DecisionCase
        {
            plot = plot.data.id, def = asset.Id, initiator = author.Key, started = now, next = now + GameClock.MonthLength,
            effort = Math.Max(MinEffort, asset.progress_needed)
        });
        Started++;
    }

    private static void Track(DecisionCase item)
    {
        Cases[item.plot] = item;
        Queue.Add(item);
    }

    // ───────── 每帧(战略调度经 FrameJobs 调来) ─────────
    public static void Tick()
    {
        if (EmpireWorld.Current?.Plots == null || !Adopt()) return;
        if (Pending.Count > 0) Restore();
        if (_month < 0) AdoptOrphans();
        int month = GameClock.Year * 12 + GameClock.Month;
        if (month != _month)
        {
            _month = month;
            using (FrameProfiler.Measure("决议·名单")) RebuildPool();
            Plan();
        }
        Clock.Restart();
        using (FrameProfiler.Measure("决议·推进")) Advance();
        using (FrameProfiler.Measure("决议·发起")) Dispatch();
    }

    // 推进到期的决议(每件每月一回，各自的时刻错开)：轮着看，每帧限量限时
    private static void Advance()
    {
        if (Queue.Count == 0) return;
        double now = GameClock.Now;
        for (int seen = 0, steps = 0, n = Queue.Count; seen < n && steps < AdvancePerTick && Queue.Count > 0; seen++)
        {
            if (Clock.Elapsed.TotalMilliseconds > TickMilliseconds) break;
            if (_queueCursor >= Queue.Count) _queueCursor = 0;
            DecisionCase item = Queue[_queueCursor];
            if (!Cases.TryGetValue(item.plot, out DecisionCase live) || !ReferenceEquals(live, item))
            {
                Queue.RemoveAt(_queueCursor);
                continue;
            }
            if (now < item.next)
            {
                _queueCursor++;
                continue;
            }
            steps++;
            item.next = now + GameClock.MonthLength;
            Plot plot = EmpireWorld.Current.Plots.Get(item.plot);
            bool closed;
            try
            {
                closed = Step(item, plot, now);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][决议] {item.def} 推进出错，作罢：{exception.GetType().Name}: {exception.Message}");
                closed = Close(item, plot, finished: false);
            }
            if (closed) Queue.RemoveAt(_queueCursor);
            else _queueCursor++;
        }
    }

    // 推进一件：返回 true = 已了结(办成或作罢)
    private static bool Step(DecisionCase item, Plot plot, double now)
    {
        PlotKind def = Def(item.def);
        Person author = plot?.GetAuthor();
        if (def == null || plot?.data == null || !plot.IsActive() || author == null || !author.Alive || author.Plot != plot)
        {
            Dropped++;
            Cases.Remove(item.plot);
            // 牌子还挂着(出错时被关掉的、发起人换了别的)：摘下来
            if (plot?.data != null && plot.GetState() == PlotState.Active) plot.Finish(PlotState.Cancelled, author);
            return true;
        }
        if (def.check_should_continue != null && !def.check_should_continue(author) || !plot.TargetsStillThere())
            return Close(item, plot, finished: false);
        item.progress = Math.Min(item.effort, item.progress + Pace(author));
        Show(item, plot, now);
        if (item.progress < item.effort)
        {
            if (GameClock.MonthsSince(item.started) > LapseMonths(item, author)) return Close(item, plot, finished: false);
            return false;
        }
        if (WarReadinessSystem.Hold(plot)) return false;
        if (def.action != null && def.action(author)) def.post_action?.Invoke(author);
        return Close(item, plot, finished: true);
    }

    private static float Talent(Person actor) =>
        Math.Max(0, Math.Min(30, Math.Max(Math.Max(actor.Intelligence, actor.Stewardship), Math.Max(actor.Diplomacy, actor.Warfare))));

    private static float Pace(Person actor) => PaceBase + Talent(actor) * 0.5f;

    private static int LapseMonths(DecisionCase item, Person author) =>
        Math.Max(LapseMinMonths, (int)Math.Ceiling(item.effort / Pace(author) * LapseFactor));

    // 牌子上的进度(游戏里谋划的进度条、别的系统查"进度满了没有")
    private static void Show(DecisionCase item, Plot plot, double now)
    {
        plot.data.progress_current = item.progress;
        plot.ProgressShown = item.progress;
        plot.LastProgressAt = now;
    }

    private static bool Close(DecisionCase item, Plot plot, bool finished)
    {
        Cases.Remove(item.plot);
        Person author = plot?.GetAuthor();
        if (finished)
        {
            NoteDone(item, author);
            Finished++;
            FinishedBy[item.def] = FinishedBy.TryGetValue(item.def, out int done) ? done + 1 : 1;
        }
        else Lapsed++;
        if (plot?.data != null && plot.IsActive()) plot.Finish(finished ? PlotState.Finished : PlotState.Cancelled, author);
        return true;
    }

    // ───────── 发起：天平(见 DecisionScale) ─────────
    // 资望：按名望折算的档次，定义里的 min_level 当资望档用(游戏存档里君主名望中位约 220、四分之三位约 340)
    public static int Standing(Person actor) => 1 + (int)(Math.Max(0, actor.Renown) / StandingPerTier);

    // 过完秤、等主线程办的：某人到了时候(due)，按候选从高到低验条件、发起
    private sealed class Ready
    {
        public long actor;
        public double due;
        public List<DecisionScale.Option> options;
    }

    // 一回议决的经过(决议天平页看)：谁、何时、各候选的读数与砝码、结果
    public sealed class Council
    {
        public long actor;
        public string name;
        public double at;
        public readonly List<(string def, float ratio, List<ScaleWeight> weights, string outcome)> options = new();
    }

    private static readonly List<DecisionScale.DefInfo> DefInfos = new(), ChiefDefInfos = new();
    private static readonly List<Ready> ReadyList = new();
    private static readonly Dictionary<long, Council> LastCouncil = new();
    // 各国各项决议上回办成的世界时刻(按 DefInfo.index)、各类上回办成的时刻(按 DecisionKind)；不存档，读档后从头记
    private static readonly Dictionary<long, (double[] byDef, double[] byKind)> LastDone = new();
    // 各人各项决议上回验条件不合的时刻(见 DecisionScale.UnfitMonths)；不存档
    private static readonly Dictionary<long, double[]> Unfit = new();
    private static int _readyCursor;
    public static int Planned, Weighed;

    public static Council LastCouncilOf(Realm realm) => realm?.Record != null && LastCouncil.TryGetValue(realm.Key, out Council council) ? council : null;

    // 想发起决议的人：各国君主、各城城主、各族族长(每月重排一回)
    private static void RebuildPool()
    {
        Pool.Clear();
        var seen = new HashSet<long>();
        void Add(Person actor)
        {
            if (actor?.Record != null && actor.Alive && seen.Add(actor.Key)) Pool.Add(actor);
        }
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms) Add(kingdom?.Ruler);
        foreach (City city in EmpireWorld.Current.Cities) Add(city?.Leader);
        foreach (Clan clan in EmpireWorld.Current.Clans) Add(clan?.GetChief());
        if (NextConsider.Count > Pool.Count * 4 + 500) NextConsider.Clear();
        if (Unfit.Count > Pool.Count * 2 + 500) Unfit.Clear();
    }

    private static void BuildDefInfos()
    {
        DefInfos.Clear();
        ChiefDefInfos.Clear();
        for (int i = 0; i < Initiable.Count; i++)
        {
            PlotKind def = Initiable[i];
            var info = new DecisionScale.DefInfo
            {
                index = i, id = def.Id, kind = DecisionScale.KindOf(def.Id), minStanding = def.min_level, minRenown = def.min_renown_actor,
                minRealmRenown = def.min_renown_kingdom, minIntelligence = def.min_intelligence, minDiplomacy = def.min_diplomacy,
                minWarfare = def.min_warfare, minStewardship = def.min_stewardship, cost = def.money_cost,
                byKing = def.can_be_done_by_king, byLeader = def.can_be_done_by_leader, byClan = def.can_be_done_by_clan_member,
                manual = RulerDecrees.IsManual(def.Id), minAuthority = RulerDecrees.MinAuthority(def.Id), gravity = RulerDecrees.Gravity(def.Id)
            };
            DefInfos.Add(info);
            if (def.can_be_done_by_clan_member || def.can_be_done_by_leader) ChiefDefInfos.Add(info);
        }
    }

    // 世界法则每月看一回(过秤时只读这一份)
    private static void RefreshLaws()
    {
        bool diplomacy = WorldLaws.Diplomacy?.IsEnabled() != false;
        bool rebellion = WorldLaws.Rebellions?.IsEnabled() != false;
        foreach (DecisionScale.DefInfo info in DefInfos)
        {
            PlotKind def = Initiable[info.index];
            info.lawsAllow = (!def.requires_diplomacy || diplomacy) && (!def.requires_rebellion || rebellion);
        }
    }

    // 月初：本月该想事的人取快照(主线程)，交给别的核过秤，过完的排进 ReadyList 等到时候再办
    private static void Plan()
    {
        if (Initiable.Count == 0 || Pool.Count == 0) return;
        if (DefInfos.Count != Initiable.Count) BuildDefInfos();
        RefreshLaws();
        double now = GameClock.Now;
        double month = GameClock.MonthLength;
        var snapshots = new List<DecisionScale.Snapshot>();
        var dues = new List<double>();
        var official = new List<bool>();
        var realms = new Dictionary<long, DecisionScale.Snapshot>();
        Dictionary<long, int> busy = null;
        using (FrameProfiler.Measure("决议·取快照"))
        {
            foreach (Person actor in Pool)
            {
                if (actor?.Record == null || !actor.Alive || actor.Plot != null) continue;
                Realm realm = actor.Realm;
                if (realm?.Record == null || realm.IsGone() || !actor.IsKingdomCiv()) continue;
                long id = actor.Key;
                bool isOfficial = actor.IsRuler() || actor.IsCityLeader();
                double interval = (isOfficial ? ConsiderMonths : ChiefConsiderMonths) * month;
                if (!NextConsider.TryGetValue(id, out double due))
                {
                    // 头一回见到：错开到往后一段里的某个时候，免得读档后大家同一个月一齐想
                    NextConsider[id] = due = now + EcRandom.Range(0f, (float)interval);
                }
                if (due >= now + month) continue;
                NextConsider[id] = Math.Max(due, now) + interval;
                if (Steered != null && Steered(actor)) continue;
                busy ??= BusyByRealm();
                snapshots.Add(Take(actor, realm, now, month, realms, busy));
                dues.Add(Math.Max(due, now));
                official.Add(isOfficial);
            }
        }
        if (snapshots.Count == 0) return;
        var results = new List<DecisionScale.Option>[snapshots.Count];
        using (FrameProfiler.Measure("决议·过秤"))
            ParallelWork.For(snapshots.Count, i => results[i] = DecisionScale.Weigh(snapshots[i], official[i] ? DefInfos : ChiefDefInfos));
        Planned += snapshots.Count;
        for (int i = 0; i < snapshots.Count; i++)
        {
            if (results[i] == null || results[i].Count == 0) continue;
            Weighed++;
            ReadyList.Add(new Ready { actor = snapshots[i].actor, due = dues[i], options = results[i] });
        }
        if (_readyCursor > 0) ReadyList.RemoveRange(0, Math.Min(_readyCursor, ReadyList.Count));
        _readyCursor = 0;
        ReadyList.Sort((a, b) => a.due.CompareTo(b.due));
    }

    private static Dictionary<long, int> BusyByRealm()
    {
        var busy = new Dictionary<long, int>();
        foreach (DecisionCase item in Cases.Values)
        {
            Realm realm = EmpireWorld.Current.PersonByKey(item.initiator)?.Realm;
            if (realm?.Record == null) continue;
            busy[realm.Key] = busy.TryGetValue(realm.Key, out int count) ? count + 1 : 1;
        }
        return busy;
    }

    private static DecisionScale.Snapshot Take(Person actor, Realm realm, double now, double month,
        Dictionary<long, DecisionScale.Snapshot> realms, Dictionary<long, int> busy)
    {
        long realmId = realm.Key;
        if (!realms.TryGetValue(realmId, out DecisionScale.Snapshot shared))
        {
            Empire empire = realm.GetEmpire();
            float authority = empire != null && empire.CoreKingdom == realm ? empire.Legitimacy / 100f : RealmAuthority.Get(realm) / 100f;
            LastDone.TryGetValue(realmId, out var done);
            realms[realmId] = shared = new DecisionScale.Snapshot
            {
                realm = realmId, realmRenown = realm.GetRenown(), treasury = StateSettlementSystem.DiscretionaryFunds(realm),
                wars = realm.GetWarsCached(false).Count, busy = busy.TryGetValue(realmId, out int count) ? count : 0,
                authority = Math.Max(0f, Math.Min(1f, authority)), lastByDef = done.byDef, lastByKind = done.byKind
            };
        }
        return new DecisionScale.Snapshot
        {
            actor = actor.Key, realm = realmId, king = actor.IsRuler(), leader = actor.IsCityLeader(), standing = Standing(actor),
            skipsWarRulerGate = actor.IsRuler() && EmpireCraftPlotsAddition.SkipsWarPlotRulerGate(actor),
            renown = actor.Renown, intelligence = actor.Intelligence, diplomacy = actor.Diplomacy, warfare = actor.Warfare,
            stewardship = actor.Stewardship, talent = (int)Talent(actor), money = actor.Money, realmRenown = shared.realmRenown,
            treasury = shared.treasury, wars = shared.wars, busy = shared.busy, authority = shared.authority, month = _month, now = now,
            lastByDef = shared.lastByDef, lastByKind = shared.lastByKind, monthSeconds = month,
            unfitByDef = Unfit.TryGetValue(actor.Key, out double[] unfit) ? unfit : null
        };
    }

    // 平时每帧：到了时候的，按候选从高到低跑决议自己的条件(读活的世界，只能在主线程)，过了且付得起就发起
    private static void Dispatch()
    {
        if (ReadyList.Count == 0) return;
        double now = GameClock.Now;
        double stale = now - 2d * GameClock.MonthLength;
        // 到了时候的有几个：每帧至少办掉四分之一、至多 MustPerTick 个(快进时一个月只有十来帧，光按时限会越积越多)，其余按时限。
        // 2026-11-24 群友"11/5 那版更流畅，之后越来越卡"：以前这四分之一不封顶，月初到期几十上百人时头几帧不看时限连办几十件，
        // 每件都在主线程跑决议自己的条件(开战类还要搜目标)；积着没轮到的过两个月自己作废(见下 stale)，不会越积越多
        int due = 0;
        for (int i = _readyCursor; i < ReadyList.Count && ReadyList[i].due <= now; i++) due++;
        // 欠得久了才放开：排在最前的已经逾期半个月以上，照旧办四分之一、不封顶，免得一直卡着时到期的总办不上、两个月后成批作废
        bool overdue = _readyCursor < ReadyList.Count && ReadyList[_readyCursor].due < now - 0.5d * GameClock.MonthLength;
        int must = overdue ? (due + 3) / 4 : Math.Min((due + 3) / 4, MustPerTick);
        for (int n = 0; _readyCursor < ReadyList.Count; n++)
        {
            if (n >= must && (n >= DispatchPerTick || Clock.Elapsed.TotalMilliseconds > TickMilliseconds)) break;
            Ready ready = ReadyList[_readyCursor];
            if (ready.due > now) break;
            _readyCursor++;
            // 积了两个月还没轮到的：不办了，下回到时候重新过秤
            if (ready.due < stale) continue;
            Person actor = EmpireWorld.Current.PersonByKey(ready.actor);
            if (actor?.Record == null || !actor.Alive || actor.Plot != null || actor.Realm?.Record == null || actor.Realm.IsGone()) continue;
            if (Steered != null && Steered(actor)) continue;
            try
            {
                Decide(actor, ready.options);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][决议] 发起出错：{exception.GetType().Name}: {exception.Message}");
            }
        }
        if (_readyCursor >= ReadyList.Count)
        {
            ReadyList.Clear();
            _readyCursor = 0;
        }
        else if (_readyCursor > 256)
        {
            ReadyList.RemoveRange(0, _readyCursor);
            _readyCursor = 0;
        }
    }

    private static void Decide(Person actor, List<DecisionScale.Option> options)
    {
        var council = new Council { actor = actor.Key, name = actor.FullName(), at = GameClock.Now };
        bool done = false;
        foreach (DecisionScale.Option option in options)
        {
            PlotKind def = Initiable[option.def];
            string outcome;
            if (done) outcome = "decision_outcome_skipped";
            else if (!def.canBeDoneByRole(actor) || def.check_is_possible != null && !def.check_is_possible(actor))
            {
                outcome = "decision_outcome_unfit";
                NoteUnfit(actor, option.def);
            }
            else if (!CanPay(actor, def.money_cost)) outcome = "decision_outcome_cost";
            else
            {
                bool started = def.try_to_start_advanced != null
                    ? def.try_to_start_advanced(actor, def, true)
                    : EmpireWorld.Current.Plots.newPlot(actor, def, true) != null;
                if (started && actor.Plot != null)
                {
                    Pay(actor, def.money_cost);
                    if (actor.Plot.data != null && Cases.TryGetValue(actor.Plot.data.id, out DecisionCase item))
                    {
                        item.ratio = option.ratio;
                        item.weights = option.weights;
                    }
                    outcome = "decision_outcome_started";
                    done = true;
                }
                else outcome = "decision_outcome_unfit";
            }
            council.options.Add((def.Id, option.ratio, option.weights, outcome));
        }
        LastCouncil[actor.Realm.Key] = council;
    }

    private static void NoteUnfit(Person actor, int def)
    {
        long id = actor.Key;
        if (!Unfit.TryGetValue(id, out double[] unfit))
        {
            Unfit[id] = unfit = new double[Math.Max(1, Initiable.Count)];
            for (int i = 0; i < unfit.Length; i++) unfit[i] = -1d;
        }
        if (def >= 0 && def < unfit.Length) unfit[def] = GameClock.Now;
    }

    // 办成了记下(新近办过的阻力)
    private static void NoteDone(DecisionCase item, Person author)
    {
        Realm realm = author?.Realm;
        if (realm?.Record == null) return;
        int index = Initiable.FindIndex(def => def.Id == item.def);
        if (!LastDone.TryGetValue(realm.Key, out var done))
        {
            done = (new double[Math.Max(1, Initiable.Count)], new double[Enum.GetValues(typeof(DecisionKind)).Length]);
            for (int i = 0; i < done.byDef.Length; i++) done.byDef[i] = -1d;
            for (int i = 0; i < done.byKind.Length; i++) done.byKind[i] = -1d;
            LastDone[realm.Key] = done;
        }
        double now = GameClock.Now;
        if (index >= 0 && index < done.byDef.Length) done.byDef[index] = now;
        done.byKind[(int)DecisionScale.KindOf(item.def)] = now;
    }

    // 花费：先出个人钱袋；君主、城主办的是公事，个人不够的由国库(可支配的部分)出
    private static bool Public(Person actor) => actor.IsRuler() || actor.IsCityLeader();

    private static bool CanPay(Person actor, int cost) =>
        cost <= 0 || actor.Money >= cost || Public(actor) && StateSettlementSystem.DiscretionaryFunds(actor.Realm) >= cost;

    private static void Pay(Person actor, int cost)
    {
        if (cost <= 0) return;
        if (actor.Money >= cost) actor.SpendMoney(cost);
        else if (Public(actor)) TreasurySystem.TrySpend(actor.Realm, cost, TreasuryCategory.Administration);
    }

    // 在办的决议(天平页看)
    public static IEnumerable<DecisionCase> CasesOf(Realm realm)
    {
        if (realm?.Record == null) yield break;
        foreach (DecisionCase item in Cases.Values)
            if (EmpireWorld.Current.PersonByKey(item.initiator)?.Realm == realm) yield return item;
    }

    public static float PaceOf(DecisionCase item)
    {
        Person author = EmpireWorld.Current.PersonByKey(item.initiator);
        return author == null ? PaceBase : Pace(author);
    }

    public static int LapseMonthsOf(DecisionCase item)
    {
        Person author = EmpireWorld.Current.PersonByKey(item.initiator);
        return author == null ? LapseMinMonths : LapseMonths(item, author);
    }

    // ───────── 亲自发起(手机席位) ─────────
    public sealed class Advice
    {
        public string id;
        public float ratio;
        public List<ScaleWeight> weights;
        public int cost;
        // null = 办得成；否则是缘由的文字键(decision_outcome_unfit、decision_outcome_cost)
        public string blocked;
        // 要点一座城的大政(迁都、设陪京……，见 RulerDecrees)：每座可选的城各列一条
        public long target = -1L;
        public string targetName;
    }

    // 亲自发起时点的那座城：决议立项时取(见 RulerDecrees)；天平自己过秤发起的没有
    public static City PendingTarget { get; private set; }

    // 这个人此刻能议的事：门槛过了的都过秤(读数从高到低)，前 maxChecks 项(通常就是全部)在主线程验条件与用度
    public static List<Advice> AdviseFor(Person actor, int maxChecks = 64)
    {
        var list = new List<Advice>();
        if (actor?.Record == null || !actor.Alive || actor.Realm?.Record == null || actor.Realm.IsGone() || !Adopt()) return list;
        if (Initiable.Count == 0) return list;
        if (DefInfos.Count != Initiable.Count) BuildDefInfos();
        RefreshLaws();
        double now = GameClock.Now;
        DecisionScale.Snapshot snapshot = Take(actor, actor.Realm, now, GameClock.MonthLength, new Dictionary<long, DecisionScale.Snapshot>(), BusyByRealm());
        foreach (DecisionScale.Option option in DecisionScale.Weigh(snapshot, DefInfos, all: true))
        {
            PlotKind def = Initiable[option.def];
            var advice = new Advice { id = def.Id, ratio = option.ratio, weights = option.weights, cost = def.money_cost };
            bool major = RulerDecrees.IsManual(def.Id);
            if (list.Count >= maxChecks) advice.blocked = "decision_outcome_skipped";
            else if (!def.canBeDoneByRole(actor) || def.check_is_possible != null && !def.check_is_possible(actor))
                advice.blocked = major ? RulerDecrees.WhyNot(def.Id, actor) : "decision_outcome_unfit";
            else if (!CanPay(actor, def.money_cost)) advice.blocked = "decision_outcome_cost";
            // 大政要天平压得过去才办得成(别的决议亲自发起不看读数)
            else if (major && option.ratio < DecisionScale.Threshold) advice.blocked = "decision_outcome_scale";
            if (major && advice.blocked == null && RulerDecrees.HasTargets(def.Id))
            {
                foreach (City city in RulerDecrees.Candidates(def.Id, actor))
                    list.Add(new Advice
                    {
                        id = def.Id, ratio = option.ratio, weights = option.weights, cost = def.money_cost,
                        target = city.Key, targetName = city.GetCityName()
                    });
                continue;
            }
            list.Add(advice);
        }
        return list;
    }

    // 亲自发起：到了办的时候再验一遍条件与用度，过了就立项付钱；成了返回 null，否则返回缘由的文字键
    // target：要点一座城的大政(见 RulerDecrees)点的那座城
    public static string Launch(Person actor, string id, long target = -1L)
    {
        PlotKind def = Def(id);
        if (def == null || !Initiable.Contains(def) || actor?.Record == null || !actor.Alive || actor.Realm?.Record == null) return "decision_outcome_unfit";
        if (actor.Plot != null && actor.Plot.IsActive()) return "decision_fail_busy";
        bool major = RulerDecrees.IsManual(id);
        if (!def.canBeDoneByRole(actor) || def.check_is_possible != null && !def.check_is_possible(actor))
            return major ? RulerDecrees.WhyNot(id, actor) : "decision_outcome_unfit";
        if (!CanPay(actor, def.money_cost)) return "decision_outcome_cost";
        List<Advice> scale = null;
        City city = null;
        if (major)
        {
            // 大政要天平压得过去；要点城的，点的城得在眼下可选的里头
            scale = AdviseFor(actor, 0);
            Advice weighed = scale.FirstOrDefault(advice => advice.id == id);
            if (weighed == null) return "decision_outcome_unfit";
            if (weighed.ratio < DecisionScale.Threshold) return "decision_outcome_scale";
            if (RulerDecrees.HasTargets(id))
            {
                city = RulerDecrees.Candidates(id, actor).FirstOrDefault(candidate => candidate.Key == target);
                if (city == null) return "decision_outcome_target";
            }
        }
        bool started;
        PendingTarget = city;
        try
        {
            started = def.try_to_start_advanced != null
                ? def.try_to_start_advanced(actor, def, true)
                : EmpireWorld.Current.Plots.newPlot(actor, def, true) != null;
        }
        finally
        {
            PendingTarget = null;
        }
        if (!started || actor.Plot == null) return "decision_outcome_unfit";
        Pay(actor, def.money_cost);
        if (actor.Plot.data != null && Cases.TryGetValue(actor.Plot.data.id, out DecisionCase item))
        {
            item.personal = true;
            scale ??= AdviseFor(actor, 0);
            Advice mine = scale.FirstOrDefault(advice => advice.id == id);
            if (mine != null)
            {
                item.ratio = mine.ratio;
                item.weights = mine.weights;
            }
        }
        return null;
    }

    // ───────── 存档 ─────────
    public static List<DecisionCase> Save()
    {
        var list = new List<DecisionCase>();
        foreach (DecisionCase item in Cases.Values)
        {
            Plot plot = EmpireWorld.Current?.Plots?.Get(item.plot);
            if (plot?.data != null)
            {
                item.war = plot.TargetWar?.Key ?? -1;
                item.kingdom = plot.TargetRealm?.Key ?? -1;
                item.city = plot.TargetCity?.Key ?? -1;
                item.actor = plot.TargetPerson?.Key ?? -1;
                item.alliance = plot.TargetAlliance?.Record?.Key ?? -1;
            }
            list.Add(item);
        }
        return list;
    }

    public static void Load(List<DecisionCase> saved)
    {
        ResetWorldState();
        if (saved != null) Pending.AddRange(saved.Where(item => item?.def != null));
    }

    // 旧档里还在办的模组决议(以前由原版推进，没有记录)：按牌子上的进度接过来
    private static void AdoptOrphans()
    {
        foreach (Plot plot in EmpireWorld.Current.Plots)
        {
            if (plot?.data == null || !plot.IsActive() || Cases.ContainsKey(plot.data.id) || !Manages(plot.GetKind())) continue;
            Person author = plot.GetAuthor();
            if (author == null || !author.Alive || author.Plot != plot) continue;
            PlotKind def = plot.GetKind();
            double now = GameClock.Now;
            Track(new DecisionCase
            {
                plot = plot.data.id, def = def.Id, initiator = author.Key, started = now,
                next = now + EcRandom.Range(0f, GameClock.MonthLength),
                effort = Math.Max(MinEffort, def.progress_needed), progress = Math.Max(0f, plot.data.progress_current)
            });
        }
    }

    // 读档后接上牌子：原版谋划还在的(游戏里随原版存档)直接接上，没了的照记下的目标重新立
    private static void Restore()
    {
        int kept = 0, rebuilt = 0, gone = 0, busy = 0;
        foreach (DecisionCase item in Pending)
        {
            PlotKind def = Def(item.def);
            Person author = EmpireWorld.Current.PersonByKey(item.initiator);
            if (def == null || author == null || !author.Alive)
            {
                gone++;
                continue;
            }
            Plot plot = EmpireWorld.Current.Plots.Get(item.plot);
            if (plot?.data == null || !plot.IsActive() || plot.GetKind() != def || author.Plot != plot)
            {
                if (author.Plot != null)
                {
                    // 读档后、接上之前别处已重新发起同一项(如征粮的月结)：并进那一件，进度取大的
                    if (author.Plot.GetKind() == def && author.Plot.data != null && Cases.TryGetValue(author.Plot.data.id, out DecisionCase live))
                    {
                        live.progress = Math.Max(live.progress, Math.Min(item.progress, live.effort));
                        live.started = Math.Min(live.started, item.started);
                        kept++;
                    }
                    else busy++;
                    continue;
                }
                _restoring = true;
                try
                {
                    plot = EmpireWorld.Current.Plots.newPlot(author, def, true);
                }
                finally
                {
                    _restoring = false;
                }
                if (plot?.data == null) continue;
                plot.TargetWar = item.war >= 0 ? EmpireWorld.Current.WarByKey(item.war) : null;
                plot.TargetRealm = item.kingdom >= 0 ? EmpireWorld.Current.RealmByKey(item.kingdom) : null;
                plot.TargetCity = item.city >= 0 ? EmpireWorld.Current.CityByKey(item.city) : null;
                plot.TargetPerson = item.actor >= 0 ? EmpireWorld.Current.PersonByKey(item.actor) : null;
                plot.TargetAlliance = item.alliance >= 0 ? EmpireWorld.Current.AllianceByKey(item.alliance) : null;
                item.plot = plot.data.id;
                rebuilt++;
            }
            kept++;
            double now = GameClock.Now;
            if (item.next < now || item.next > now + GameClock.MonthLength) item.next = now + EcRandom.Range(0f, GameClock.MonthLength);
            Track(item);
            Show(item, plot, now);
        }
        LogService.LogInfo($"[EmpireCraft][决议] 读档接上在办的决议 {kept}/{Pending.Count} 件(其中重立牌子 {rebuilt} 件；发起人不在 {gone}、另有决议在办 {busy})");
        Pending.Clear();
    }

    public static string Summary() =>
        $"决议：在办 {Cases.Count}，发起 {Started}、办成 {Finished}、作罢 {Lapsed}、失主 {Dropped}" +
        (FinishedBy.Count == 0 ? "" : "(办成的：" + string.Join("、", FinishedBy.OrderByDescending(pair => pair.Value).Take(12).Select(pair => pair.Key + "×" + pair.Value)) + ")");
}
}
