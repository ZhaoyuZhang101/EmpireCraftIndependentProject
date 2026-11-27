// Generated from the EmpireCraft mod: Scripts/GeneralSystems/StateSettlementSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 无小人模式的国家移民：月度决策、局部搜索分帧续跑、提交只在主线程执行。
// 选址搜索：通达层开着时(用户 2026-11-06 同意"先信息类、再贸易迁徙"，见 ConnectivitySystem)不再按地块层数(8 层)，
//   按走过去的地形阻力(TerrainResistance.Step：平原 1、山 4、过河 +2、有路减半，政令过不去的无人区不走)做最短路，
//   预算 settle_budget(16)以内；离原城至少 settle_min_cost(官办 5、自发 2)；评分里的远近按成本扣。没开时照旧按层数。
public static partial class StateSettlementSystem
{
    private sealed class Search
    {
        public readonly Queue<(Cell zone, int depth)> queue = new();
        public readonly HashSet<int> seen = new();
        // 按成本的最短路(见类头)：待走的地块与已知最低成本；weighted 为假时照旧按层数
        public readonly MinHeap<Cell> open = new();
        public readonly Dictionary<int, float> best = new();
        public bool weighted;
        public readonly List<(Cell zone, int score)> candidates = new();
        public readonly Dictionary<int, int> terrainScores = new();
        public SettlementSeaSearch sea;
        public readonly Dictionary<int, SettlementSeaSearch.Candidate> seaCandidates = new();
        public readonly Random random;

        public Search(int seed) { random = new Random(seed); }
    }

    private static readonly Dictionary<long, Search> Searches = new();
    private static readonly Queue<long> PendingSearches = new();
    public static bool CreatingCity { get; private set; }

    public static Realm FundingRealm(Realm owner)
    {
        if (owner == null || owner.IsGone()) return null;
        Realm core = owner.GetEmpire()?.CoreKingdom;
        return core != null && !core.IsGone() && !AncientWarfareCompatibility.Owns(core) &&
               owner.IsAdministrativeKingdomType() && !owner.HasMainTitle() ? core : owner;
    }

    private static StateSettlementData Data(Realm payer) =>
        payer.GetOrCreate().settlement ??= new StateSettlementData();

    private static IEnumerable<City> Cities(Realm payer)
    {
        foreach (City city in payer.Cities)
            if (ValidCity(city) && city.Realm == payer) yield return city;
        var members = payer.GetEmpire()?.kingdoms_list;
        if (members == null) yield break;
        foreach (Realm member in members)
        {
            if (member == null || member == payer || member.IsGone() || FundingRealm(member) != payer ||
                AncientWarfareCompatibility.Owns(member)) continue;
            foreach (City city in member.Cities)
                if (ValidCity(city) && city.Realm == member) yield return city;
        }
    }

    private static bool ValidCity(City city) => city?.Record != null && !city.IsGone() &&
        !AncientWarfareCompatibility.OwnsObject(city) && city.SeatCell() != null;

    internal static IEnumerable<City> ExpansionCities(Realm payer)
    {
        Empire empire = payer.GetEmpire();
        if (empire == null || empire.CoreKingdom != payer)
        {
            foreach (City city in Cities(payer)) yield return city;
            yield break;
        }
        // 帝国主导时也等待已有封国的城市长满，不能只看核心就连续新建封国。
        foreach (Realm member in empire.kingdoms_list.Concat(new[] { payer }).Distinct())
        {
            if (member == null || member.IsGone() || member.GetEmpire() != empire || AncientWarfareCompatibility.Owns(member)) continue;
            foreach (City city in member.Cities)
                if (ValidCity(city) && city.Realm == member) yield return city;
        }
    }

    private static bool AllCitiesAtLimit(Realm payer) => ModClass.CITY_MAX_ZONES <= 0 ||
        ExpansionCities(payer).All(city => StateSettlementRules.AtZoneLimit(city.Zones.Count, ModClass.CITY_MAX_ZONES));

    private static bool PopulationMode => CityPopulationSystem.AbstractPopulationEnabled && !ModClass.IS_CLEAR;
    private static bool Enabled => PopulationMode &&
        WorldLaws.KingdomExpansion.IsEnabled();

    private static Person Founder(Realm owner, City source)
    {
        Person actor = source?.Leader;
        if (actor == null || actor.IsGone() || !actor.Alive || actor.Realm != owner) actor = owner?.Ruler;
        return actor?.Record != null && !actor.IsGone() && actor.Alive && actor.Realm == owner ? actor : null;
    }

    private static bool SafeCity(City city)
        => SafetyReason(city) == null;

    private static string SafetyReason(City city, bool spontaneous = false)
    {
        if (!ValidCity(city) || city.Realm == null) return "unsafe";
        // 自发迁民正是为了离开饥荒、拥挤或战乱，不要求原居地先恢复安全与富足。
        if (spontaneous) return null;
        if (city.Realm.HasEnemies() ||
            city.GetOrCreate().OccupiedStatus?.Count > 0) return "unsafe";
        if (city.People?.Any(actor => actor != null && !actor.IsGone() && actor.Alive &&
                actor.HasTrait("plague")) == true) return "unsafe";
        return CityPopulationSystem.GetGrowthFactors(city).Famine ? "famine" : null;
    }

    public static void Check(Realm owner)
    {
        if (!PopulationMode || owner == null || owner.IsGone() || owner.IsWild || AncientWarfareCompatibility.Owns(owner)) return;
        Realm payer = FundingRealm(owner);
        if (payer == null || payer.IsWild) return;
        StateSettlementData state = Data(payer);
        double now = GameClock.Now;
        if (state.last_checked >= 0d && now >= state.last_checked && GameClock.MonthsSince(state.last_checked) < 1) return;
        state.last_checked = now; // 行政区共用核心的项目和月度限额。
        StateSettlementProject project = state.project;
        if ((project == null || project.city < 0L && !project.committing) && CityResettlementSystem.Request(payer, state))
        {
            if (project != null) Cancel(payer, state, "resettling");
            ReserveForProject(state, 0);
            state.status = state.resettlement?.journey?.overseas == true ? "resettlement_sailing" : "resettling";
            return;
        }
        if (!Enabled)
        {
            if (project?.city >= 0L || project?.committing == true) Complete(payer, state);
            return;
        }
        // 未支付的官办计划让位给实际迁民；但已经选好地点、开始筹备的，只让位给逃荒、避战的难民
        // (住房拥挤、缺岗的迁民同样要筹备，顶掉官办计划只会让筹备从头再来)
        if (project == null || !project.spontaneous && project.city < 0L && !project.committing)
        {
            bool planning = project != null && project.zone >= 0;
            City source = Cities(payer).FirstOrDefault(city => SpontaneousCause(city) is string cause &&
                (!planning || cause == "famine" || cause == "war") &&
                CityPopulationSystem.CivilianBackground(city) >= 1f && Founder(city.Realm, city) != null);
            if (source != null)
            {
                // 未支付的官办计划让位给实际迁民；已创建的城市继续原有恢复流程。
                if (project != null) Cancel(payer, state, "changed");
                project = new StateSettlementProject { owner = source.Realm.Key, source = source.Key,
                    spontaneous = true, cause = SpontaneousCause(source) };
                state.project = project;
                ReserveForProject(state, 0);
            }
        }
        if (project != null)
        {
            ReserveForProject(state, project.paid ? 0 : project.gold);
            Realm landOwner = EmpireWorld.Current.RealmByKey(project.owner);
            City source = EmpireWorld.Current.CityByKey(project.source);
            // 已经创建的项目必须完成记账，不能因为关闭法则或爆发战争留下免费城市。
            if (project.city >= 0L || project.committing) { Complete(payer, state); return; }
            if (!project.spontaneous && payer.HasEnemies()) { state.status = "unsafe"; return; }
            if (landOwner == null || landOwner.IsGone() || FundingRealm(landOwner) != payer ||
                !ValidCity(source) || source.Realm != landOwner)
            { Cancel(payer, state, "changed"); return; }
            if (!project.spontaneous && !AllCitiesAtLimit(payer))
            { Cancel(payer, state, "expanding"); return; }
            string safety = SafetyReason(source, project.spontaneous);
            if (safety != null) { state.status = safety; return; }
            if (project.spontaneous && !Prepare(payer, project, source, null, out string migrationReason))
            { Cancel(payer, state, migrationReason); return; }
            if (project.zone < 0) { QueueSearch(payer, source); return; }
            Cell target = EmpireWorld.Current.Zones.ById(project.zone);
            if (!ValidSite(project, target, source, Founder(landOwner, source), out string siteReason))
            { Cancel(payer, state, siteReason); return; }
            if (!Ready(project))
            { state.status = project.overseas ? "sailing" : "planning"; return; }
            if (!Prepare(payer, project, source, target, out string reason))
            {
                if (reason == "funds") ReserveForProject(state, project.gold);
                state.status = reason; return;
            }
            ReserveForProject(state, project.gold);
            Complete(payer, state);
            return;
        }
        if (payer.HasEnemies()) { state.status = "unsafe"; return; }
        City lastCity = EmpireWorld.Current.CityByKey(state.last_city);
        // 只等待本系统上次建立的城安置，不因别的旧城人少而锁死全国。
        if (ValidCity(lastCity) && (FundingRealm(lastCity.Realm) == payer ||
                payer.GetEmpire() != null && lastCity.Realm?.GetEmpire() == payer.GetEmpire()) &&
            (lastCity.GetTotalFood() < CityPopulationSystem.Households(lastCity) ||
             lastCity.GetPopulationMaximum() < CityPopulationSystem.GetTotal(lastCity)))
        { ReserveForProject(state, 0); state.status = "stabilizing"; return; }
        if (!AllCitiesAtLimit(payer))
        { ReserveForProject(state, 0); state.status = "expanding"; return; }
        if (state.last_completed >= 0d && (now < state.last_completed ||
            GameClock.MonthsSince(state.last_completed) < ModClass.SETTLEMENT_COOLDOWN_MONTHS))
        { ReserveForProject(state, 0); state.status = "cooldown"; return; }
        List<City> cities = Cities(payer).ToList();
        List<City> sources = cities.Where(city => NeedsSettlement(city, payer, state))
            .OrderByDescending(city => city.Zones.Count).ToList();
        state.status = "idle";
        if (sources.Count == 0) { ReserveForProject(state, 0); return; }
        int baseCost = StateSettlementRules.AdministrativeCost(cities.Count,
            ModClass.SETTLEMENT_BASE_GOLD, ModClass.SETTLEMENT_PER_CITY_GOLD);
        int previousReserve = state.reserved_project_gold;
        ReserveForProject(state, baseCost);
        state.reserved_project_gold = Math.Max(previousReserve, state.reserved_project_gold);
        if (!StateSettlementRules.CanPay(payer.GetMoney(), baseCost, ModClass.SETTLEMENT_RESERVE_GOLD))
        { state.status = "funds"; return; }
        foreach (City source in sources)
        {
            string safety = SafetyReason(source);
            if (safety != null) { state.status = safety; continue; }
            if (Founder(source.Realm, source) == null) { state.status = "ruler"; continue; }
            project = new StateSettlementProject { owner = source.Realm.Key, source = source.Key };
            if (!Prepare(payer, project, source, null, out string reason))
            {
                if (reason == "funds") ReserveForProject(state, project.gold);
                state.status = reason; continue;
            }
            ReserveForProject(state, project.gold);
            state.project = project;
            state.status = "searching";
            QueueSearch(payer, source);
            return;
        }
    }

    private static void ReserveForProject(StateSettlementData state, int gold) => state.reserved_project_gold =
        Math.Max(0, gold);

    // 自动科研和升级只用余款；建城仍按完整报价复核和扣款。
    public static int DiscretionaryFunds(Realm kingdom)
        => (int)Math.Min(int.MaxValue, DiscretionaryBalance(kingdom));

    public static long DiscretionaryBalance(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone()) return 0;
        long money = Math.Max(0L, kingdom.GetTreasuryBalance());
        if (!TreasurySystem.Enabled(kingdom)) return money;
        StateSettlementData state = kingdom.GetOrCreate().settlement;
        long reserve = TreasurySystem.SafetyReserve(kingdom);
        long projectReserve = Enabled && state?.project?.paid != true
            ? Math.Max(0L, state?.reserved_project_gold ?? 0) : 0;
        if (Enabled && (state == null || state.last_checked < 0d) && FundingRealm(kingdom) == kingdom && AllCitiesAtLimit(kingdom) && kingdom.Cities.Any(city =>
                ValidCity(city) && StateSettlementRules.AtZoneLimit(city.Zones.Count, ModClass.CITY_MAX_ZONES)))
            projectReserve = Math.Max(projectReserve, StateSettlementRules.AdministrativeCost(
                kingdom.Cities.Count, ModClass.SETTLEMENT_BASE_GOLD, ModClass.SETTLEMENT_PER_CITY_GOLD));
        return TreasuryRules.AvailableBalance(money, reserve, projectReserve);
    }

    private static bool NeedsSettlement(City city, Realm payer, StateSettlementData state)
    {
        if (StateSettlementRules.AtZoneLimit(city.Zones.Count, ModClass.CITY_MAX_ZONES)) return true;
        CityPopulationData population = CityPopulationSystem.Get(city);
        var growth = CityPopulationSystem.GetGrowthFactors(city, population);
        bool pressure = growth.Capacity <= 0f || growth.Total >= growth.Capacity * 0.9f ||
            population.last_workforce > 0f && population.last_jobs < population.last_workforce * 0.8f;
        if (pressure && (!city.CanGrowZones() || !HasClaimableLand(city))) return true;
        // 区域上限关闭时仍允许富裕国家低频移民，至少间隔十年；有上限时先通过全城门槛。
        double since = state.last_completed >= 0d ? state.last_completed : city.FoundedStamp;
        return pressure && payer.GetMoney() >= Math.Max(2000L, (long)ModClass.SETTLEMENT_BASE_GOLD * 5L) &&
               GameClock.MonthsSince(since) >= 120;
    }

    private static string SpontaneousCause(City city)
    {
        if (!ValidCity(city) || city.Realm == null) return null;
        CityPopulationData population = CityPopulationSystem.Get(city);
        // 新聚落尚未开始经济结算时，零住房和零产出只是初始化状态。
        if (population.settlement_supplies != null && population.last_economy < 0d) return null;
        var growth = CityPopulationSystem.GetGrowthFactors(city, population);
        if (growth.Famine) return "famine";
        if (city.GetOrCreate().OccupiedStatus?.Count > 0) return "war";
        if (growth.Total > 0f && growth.Total >= growth.Capacity) return "housing";
        if (population.last_workforce > 0f && population.last_jobs < population.last_workforce * 0.8f)
            return "jobs";
        return null;
    }

    private static bool HasClaimableLand(City city)
    {
        Person actor = Founder(city.Realm, city);
        if (actor == null) return false;
        foreach (Cell border in city.BorderZones)
            foreach (Cell zone in border.Neighbours)
                if (zone?.City == null && zone?.Center != null && zone.GroundCount > 0 &&
                    zone.CanBeClaimedByCity(city) && zone.CheckCanSettleInThisBiomes(actor.Subspecies) &&
                    zone.Center.IsSameIsland(city.SeatCell()) && zone.Center.ReachableFrom(city.SeatCell())) return true;
        return false;
    }

    private static void QueueSearch(Realm payer, City source)
    {
        if (Searches.ContainsKey(payer.Key)) return;
        var search = new Search(unchecked((int)(payer.Key * 397L ^ source.Key ^
            BitConverter.DoubleToInt64Bits(GameClock.Now))));
        search.weighted = ConnectivitySystem.Config.enabled;
        foreach (Cell zone in source.BorderZones)
        {
            if (zone == null) continue;
            if (search.weighted)
            {
                if (search.best.ContainsKey(zone.Index)) continue;
                search.best[zone.Index] = 0f;
                search.open.Push(0f, zone);
            }
            else if (search.seen.Add(zone.Index)) search.queue.Enqueue((zone, 0));
        }
        Searches[payer.Key] = search;
        StateSettlementProject project = Data(payer).project;
        Data(payer).status = project?.spontaneous == true ? "spontaneous_searching" : "searching";
        PendingSearches.Enqueue(payer.Key);
    }

    // 调度器每帧推进一个国家，最多 8 个区域和 128 格海面；不调用原版整图 CityPlaceFinder.recalc。
    public static void Tick()
    {
        CityResettlementSystem.Tick();
        if (!Enabled || PendingSearches.Count == 0) return;
        long id = PendingSearches.Dequeue();
        Realm payer = EmpireWorld.Current.RealmByKey(id);
        if (payer == null || payer.IsGone()) { Searches.Remove(id); return; }
        StateSettlementData state = Data(payer);
        StateSettlementProject project = state.project;
        if (project == null) return;
        if (project.zone >= 0)
        {
            if (project.spontaneous)
            {
                if (!SimulationFrameBudget.HasTime) { PendingSearches.Enqueue(id); return; }
                // 还在筹备(或渡海)：等月度检查到期后提交
                if (!Ready(project)) { state.status = project.overseas ? "sailing" : "planning"; return; }
                Complete(payer, state);
            }
            return;
        }
        if (!Searches.TryGetValue(id, out Search search)) return;
        City source = EmpireWorld.Current.CityByKey(project.source);
        Realm owner = EmpireWorld.Current.RealmByKey(project.owner);
        Person actor = Founder(owner, source);
        if (!ValidCity(source) || source.Realm != owner || FundingRealm(owner) != payer || actor == null)
        { Cancel(payer, state, "changed"); return; }
        if (!project.spontaneous && (payer.HasEnemies() || owner.HasEnemies()))
        { state.status = "unsafe"; PendingSearches.Enqueue(id); return; }
        // 人物只提供原版物种/文化初始化上下文，不派人寻路，也不检查人物所在位置。
        state.status = search.sea != null ? "sea_searching" : project.spontaneous ? "spontaneous_searching" : "searching";
        if (search.weighted) WeightedSteps(search, source, actor, project, payer);
        else
            for (int steps = 0; steps < 8 && search.queue.Count > 0 && SimulationFrameBudget.HasTime; steps++)
            {
                (Cell zone, int depth) = search.queue.Dequeue();
                if (depth >= (project.spontaneous ? 2 : 5) && GoodSite(zone, source, actor, project.spontaneous))
                    search.candidates.Add((zone, SiteScore(zone, depth, source, actor, search)));
                if (depth >= 8) continue;
                foreach (Cell neighbour in zone.Neighbours)
                    if (neighbour?.Center != null && (neighbour.City == null || neighbour.City == source) &&
                        neighbour.Center.IsSameIsland(source.SeatCell()) && search.seen.Add(neighbour.Index))
                        search.queue.Enqueue((neighbour, depth + 1));
            }
        if (search.queue.Count > 0 || search.open.Count > 0 || !SimulationFrameBudget.HasTime)
        { PendingSearches.Enqueue(id); return; }
        // 搜完附近八层区域再比较；在最高分 95% 以上的地点中等概率随机选择。
        // 每帧只复核一个最终候选，搜索期间被占用的地点不会拖出整帧的大扫描。
        if (search.candidates.Count == 0 && search.sea == null)
        {
            // 同时只保留四个海面搜索，避免多国都遇到岛屿瓶颈时堆积大量格子队列。
            if (Searches.Values.Count(active => active.sea != null) >= 4)
            { state.status = "sea_searching"; PendingSearches.Enqueue(id); return; }
            search.sea = new SettlementSeaSearch(source, Cities(payer).OrderBy(city => city == source ? 0 : 1));
            search.terrainScores.Clear();
            state.status = "sea_searching";
            PendingSearches.Enqueue(id);
            return;
        }
        if (search.sea != null && !search.sea.Finished)
        {
            state.status = "sea_searching";
            search.sea.Advance(source);
            for (int steps = 0; steps < 8 && SimulationFrameBudget.HasTime; steps++)
            {
                SettlementSeaSearch.Landing landing = search.sea.NextLanding();
                if (landing == null) break;
                if (!GoodSite(landing.zone, source, actor, project.spontaneous, landing.landfall, true)) continue;
                SettlementSeaSearch.Candidate candidate = search.sea.Accept(landing);
                search.seaCandidates[landing.zone.Index] = candidate;
                search.candidates.Add((landing.zone, SiteScore(landing.zone, candidate.distance / 8 + landing.depth,
                    source, actor, search, landing.landfall)));
            }
            if (!search.sea.Finished || !SimulationFrameBudget.HasTime)
            { PendingSearches.Enqueue(id); return; }
        }
        if (search.candidates.Count == 0) { Cancel(payer, state, search.sea?.unavailable ?? "land"); return; }
        int best = search.candidates.Max(candidate => candidate.score), chosen = -1, matches = 0;
        for (int i = 0; i < search.candidates.Count; i++)
            if (StateSettlementRules.NearBestSite(search.candidates[i].score, best) && search.random.Next(++matches) == 0)
                chosen = i;
        Cell target = search.candidates[chosen].zone;
        if (search.seaCandidates.TryGetValue(target.Index, out SettlementSeaSearch.Candidate seaCandidate))
            seaCandidate.Apply(project);
        if (!ValidSite(project, target, source, actor, out _))
        { search.candidates.RemoveAt(chosen); PendingSearches.Enqueue(id); return; }
        project.zone = target.Index;
        project.started = GameClock.Now;
        state.status = project.overseas ? "sailing" : project.spontaneous ? "spontaneous_settling" : "planning";
        if (project.overseas)
        {
            if (!Prepare(payer, project, source, target, out string reason)) state.status = reason;
            ReserveForProject(state, project.gold);
        }
        Searches.Remove(id);
        // 自发迁民留到下一帧提交(逃荒、避战的不筹备，当即成行)，仍服从统一帧预算。
        if (project.spontaneous) PendingSearches.Enqueue(id);
    }

    // 按成本的选址搜索(见类头)：每帧至多 8 块
    private static void WeightedSteps(Search search, City source, Person actor, StateSettlementProject project, Realm payer)
    {
        ConnectivityConfig config = ConnectivitySystem.Config;
        float near = project.spontaneous ? config.settle_min_cost_spontaneous : config.settle_min_cost;
        for (int steps = 0; steps < 8 && search.open.Count > 0 && SimulationFrameBudget.HasTime; steps++)
        {
            (float cost, Cell zone) = search.open.Pop();
            if (!search.seen.Add(zone.Index)) continue;
            if (search.best.TryGetValue(zone.Index, out float known) && cost > known) continue;
            if (cost >= near && GoodSite(zone, source, actor, project.spontaneous))
                search.candidates.Add((zone, SiteScore(zone, (int)Math.Round(cost), source, actor, search)));
            if (cost >= config.settle_budget) continue;
            foreach (Cell neighbour in zone.Neighbours ?? Array.Empty<Cell>())
            {
                if (neighbour?.Center == null || neighbour.City != null && neighbour.City != source ||
                    search.seen.Contains(neighbour.Index) || !neighbour.Center.IsSameIsland(source.SeatCell())) continue;
                float step = TerrainResistance.Step(neighbour, payer);
                if (step < 0f) continue;
                float total = cost + step;
                if (search.best.TryGetValue(neighbour.Index, out float old) && old <= total) continue;
                search.best[neighbour.Index] = total;
                search.open.Push(total, neighbour);
            }
        }
    }

    private static int SiteScore(Cell zone, int depth, City source, Person actor, Search search, Cell landOrigin = null)
    {
        int score = TerrainScore(zone, source, actor, search, landOrigin);
        foreach (Cell neighbour in zone.NeighboursAll ?? zone.Neighbours)
            score += TerrainScore(neighbour, source, actor, search, landOrigin);
        return Math.Max(1, score - depth * 16);
    }

    private static int TerrainScore(Cell zone, City source, Person actor, Search search, Cell landOrigin)
    {
        if (zone == null) return 0;
        Cell origin = landOrigin ?? source.SeatCell();
        if (zone.City != null || zone.Center == null || zone.GroundCount <= 0 ||
            !zone.Center.IsSameIsland(origin) || !zone.Center.ReachableFrom(origin) ||
            !zone.CheckCanSettleInThisBiomes(actor.Subspecies)) return 0;
        if (search.terrainScores.TryGetValue(zone.Index, out int score)) return score;
        int fields = 0;
        if (zone.Tiles != null)
            foreach (Cell tile in zone.Tiles)
                if (tile?.Type?.CanBeFarm == true) fields++;
        // 周围能建设/耕作的土地优先，木石加分设上限，避免一片矿石压过安置条件。
        score = zone.GroundCount + fields * 2 + (zone.CanStartCityHere() ? 32 : 0) +
            Math.Min(12, zone.GetHashset(BuildingList.Trees)?.Count ?? 0) * 3 +
            Math.Min(4, zone.GetHashset(BuildingList.Minerals)?.Count ?? 0) * 8;
        search.terrainScores[zone.Index] = score;
        return score;
    }

    private static bool GoodSite(Cell zone, City source, Person actor, bool spontaneous = false,
        Cell landOrigin = null, bool overseas = false)
    {
        Cell origin = landOrigin ?? source.SeatCell();
        if (zone?.Center == null || actor == null || zone.City != null || !zone.CanStartCityHere() ||
            !zone.CheckCanSettleInThisBiomes(actor.Subspecies) ||
            zone.Center.Region?.island == null || !spontaneous && !overseas && zone.Center.Region.island.getTileCount() < 300 ||
            !zone.Center.IsSameIsland(origin) || !zone.Center.ReachableFrom(origin)) return false;
        // 自发聚落可以靠近原居地，不沿用官办城市的四层留白。
        var queue = new Queue<(Cell zone, int depth)>();
        var seen = new HashSet<int> { zone.Index };
        queue.Enqueue((zone, 0));
        while (queue.Count > 0)
        {
            (Cell near, int depth) = queue.Dequeue();
            if (near.City != null) return false;
            if (depth >= (spontaneous ? 1 : 4)) continue;
            foreach (Cell neighbour in near.Neighbours)
                if (neighbour != null && seen.Add(neighbour.Index)) queue.Enqueue((neighbour, depth + 1));
        }
        return true;
    }

    private static bool ValidSite(StateSettlementProject project, Cell target, City source, Person actor, out string reason)
    {
        Cell landfall = null;
        reason = "land";
        if (project.overseas && !SettlementSeaSearch.Validate(project, source, out landfall, out reason)) return false;
        reason = "land";
        return GoodSite(target, source, actor, project.spontaneous, landfall, project.overseas);
    }

    // 建城筹备月数(模组设置)：官办建城与百姓因住房拥挤、缺少岗位自发迁出的聚落都要筹备；逃荒、避战的难民当即动身。
    // 以前自发聚落一律不筹备，而满城、缺岗时常有自发迁民，看起来就像筹备月数不生效
    private static int PreparationMonths(StateSettlementProject project) =>
        project.spontaneous && (project.cause == "famine" || project.cause == "war")
            ? 0
            : Math.Max(0, ModClass.SETTLEMENT_PREPARATION_MONTHS);

    private static bool Ready(StateSettlementProject project) => project.started >= 0d &&
        GameClock.Now >= project.started && GameClock.MonthsSince(project.started) >=
        PreparationMonths(project) +
        (project.overseas ? SettlementSeaSearch.TravelMonths(project.sea_route?.Count ?? 0,
            EmpireWorld.Current?.CityByKey(project.embarkation_city)) : 0);

    private static bool Prepare(Realm payer, StateSettlementProject project, City source, Cell target, out string reason)
    {
        if (project.spontaneous) return PrepareSpontaneous(project, source, out reason);
        reason = "people";
        List<City> all = Cities(payer).ToList();
        List<City> donors = all.Where(city => city.Realm == source.Realm && SafeCity(city) &&
                city.SeatCell().IsSameIsland(source.SeatCell()) && city.SeatCell().ReachableFrom(source.SeatCell()))
            .OrderBy(city => city == source ? 0 : 1).ThenBy(city => city.Key).ToList();
        project.migrants.Clear(); project.cargo.Clear(); project.granary_food = 0;
        int targetScale = CityPopulationSystem.PeoplePerSlot(source);
        float need = Math.Max(ModClass.SETTLEMENT_MIN_HOUSEHOLDS, ModClass.SETTLEMENT_TARGET_HOUSEHOLDS) * (float)targetScale;
        foreach (City donor in donors)
        {
            float people = Math.Min(need, StateSettlementRules.MigrantPeople(
                CityPopulationSystem.CivilianBackground(donor), CityPopulationSystem.PeoplePerSlot(donor),
                ModClass.SETTLEMENT_RETAIN_HOUSEHOLDS, ModClass.SETTLEMENT_MAX_MIGRATION_PERCENT));
            if (people <= 0f) continue;
            project.migrants.Add(new SettlementMigration { source = donor.Key, people = people });
            need -= people;
            if (need <= 0f) break;
        }
        project.people = project.migrants.Sum(move => move.people);
        if (project.people + 0.01f < ModClass.SETTLEMENT_MIN_HOUSEHOLDS * (float)targetScale) return false;
        reason = "materials";
        if (!PlanCargo(project, donors, "wood", ModClass.SETTLEMENT_WOOD) ||
            !PlanCargo(project, donors, "stone", ModClass.SETTLEMENT_STONE)) return false;
        reason = "food";
        int food = (int)Math.Ceiling(project.people / targetScale * ModClass.SETTLEMENT_FOOD_YEARS);
        ResourceKind[] foods = ResourceKinds.Table.list.Where(resource => resource.type == ResType.Food).ToArray();
        // 自主藩国不会使用帝国核心的粮仓。没有粮仓制度时直接用城市余粮。
        if (GranarySystem.ModRealm(payer) == payer && GranarySystem.Enabled(payer) && foods.Length > 0)
        {
            project.granary_food = Math.Min(food, (int)Math.Max(0f, payer.GetOrCreate().granary));
            project.granary_resource = foods[0].id;
            food -= project.granary_food;
        }
        foreach (City donor in donors)
        {
            // 原版食物缓存可能尚未反映本帧吃粮，保护线必须读取实际库存。
            int surplus = Math.Max(0, foods.Sum(resource => donor.GetResourcesAmount(resource.id)) -
                CityPopulationSystem.Households(donor));
            foreach (ResourceKind resource in foods)
            {
                int amount = Math.Min(food, Math.Min(surplus, donor.GetResourcesAmount(resource.id)));
                if (amount <= 0) continue;
                AddCargo(project, donor, resource.id, amount);
                food -= amount; surplus -= amount;
            }
            if (food <= 0) break;
        }
        if (food > 0) return false;
        long cost = StateSettlementRules.AdministrativeCost(all.Count, ModClass.SETTLEMENT_BASE_GOLD,
            ModClass.SETTLEMENT_PER_CITY_GOLD) + (long)Math.Ceiling(project.cargo.Sum(cargo => (double)cargo.price)) +
            SettlementSeaSearch.TransportGold(project);
        project.gold = (int)Math.Min(int.MaxValue, cost);
        project.startup_gold = Math.Min(50, project.gold);
        var empire = payer.GetEmpire();
        var regime = payer.GetRegime();
        project.create_fief = empire != null && empire.CoreKingdom == payer &&
            regime?.type is RegimeType.ZhouFeudalism or RegimeType.Feudalism;
        project.empire = project.create_fief ? empire.Key : -1L;
        project.fief_regime = project.create_fief ? (int)regime.type : -1;
        reason = "funds";
        return cost <= int.MaxValue && StateSettlementRules.CanPay(payer.GetMoney(), project.gold,
            (int)Math.Min(int.MaxValue, TreasurySystem.SafetyReserve(payer)));
    }

    private static bool PrepareSpontaneous(StateSettlementProject project, City source, out string reason)
    {
        reason = "people";
        float civilians = CityPopulationSystem.CivilianBackground(source);
        float people = (float)Math.Floor(Math.Min(civilians, Math.Max(1d, civilians * 0.1d)));
        if (people < 1f) return false;
        project.people = people;
        project.migrants.Clear();
        project.migrants.Add(new SettlementMigration { source = source.Key, people = people });
        project.cargo.Clear();
        project.granary_food = 0;
        project.gold = project.startup_gold = 0;
        // 百姓只带现有行粮，不征用国库、国家粮仓或建材，也不生成启动物资。
        int food = (int)Math.Ceiling(people / CityPopulationSystem.PeoplePerSlot(source));
        float share = people / Math.Max(1f, CityPopulationSystem.GetTotal(source));
        foreach (ResourceKind resource in ResourceKinds.Table.list)
        {
            if (resource.type != ResType.Food || food <= 0) continue;
            int amount = Math.Min(food, (int)Math.Floor(Math.Max(0, source.GetResourcesAmount(resource.id)) * share));
            if (amount <= 0) continue;
            project.cargo.Add(new SettlementCargo { source = source.Key, resource = resource.id, amount = amount });
            food -= amount;
        }
        // 自发聚落按原居地国家归属；后续沿用原有城邦与帝国分封规则。
        project.create_fief = false;
        project.empire = -1L;
        project.fief_regime = -1;
        reason = null;
        return true;
    }

    private static bool PlanCargo(StateSettlementProject project, List<City> donors, string resource, int need)
    {
        foreach (City donor in donors)
        {
            int amount = Math.Min(need, Math.Max(0, donor.GetResourcesAmount(resource)));
            if (amount <= 0) continue;
            AddCargo(project, donor, resource, amount);
            need -= amount;
            if (need <= 0) return true;
        }
        return need <= 0;
    }

    private static void AddCargo(StateSettlementProject project, City donor, string resource, int amount)
    {
        var stock = CityPopulationSystem.Get(donor).public_stock;
        int publicAmount = stock != null && stock.TryGetValue(resource, out int have)
            ? Math.Min(amount, Math.Max(0, Math.Min(have, donor.GetResourcesAmount(resource)))) : 0;
        project.cargo.Add(new SettlementCargo { source = donor.Key, resource = resource, amount = amount,
            price = (amount - publicAmount) * Math.Max(0f, PopulationEconomySystem.UnitValue(donor, resource)) });
    }

    private static void Complete(Realm payer, StateSettlementData state)
    {
        StateSettlementProject project = state.project;
        Cell target = EmpireWorld.Current.Zones.ById(project.zone);
        City source = EmpireWorld.Current.CityByKey(project.source);
        Realm owner = EmpireWorld.Current.RealmByKey(project.owner);
        City city = EmpireWorld.Current.CityByKey(project.city);
        try
        {
            if (city == null)
            {
                Person actor = Founder(owner, source);
                if (!Ready(project)) { state.status = project.overseas ? "sailing" : "planning"; return; }
                if (!project.spontaneous && !AllCitiesAtLimit(payer))
                { Cancel(payer, state, "expanding"); return; }
                // 异常恢复时重新报价；空地或来源易主则取消尚未支付的项目。
                if (!ValidCity(source) || source.Realm != owner || FundingRealm(owner) != payer ||
                    SafetyReason(source, project.spontaneous) != null ||
                    !ValidSite(project, target, source, actor, out _) ||
                    !Prepare(payer, project, source, target, out _))
                { Cancel(payer, state, "changed"); return; }
                project.committing = true;
                CreatingCity = true;
                try
                {
                    city = WorldActions.BuildNewCity(actor, target);
                    project.native_initialized = city != null;
                    if (city != null) FiscalDiagnostics.Count(project.overseas ? "跨海建城" : "官办建城");
                }
                finally
                {
                    CreatingCity = false;
                    // 原版建城在注册城市后可能抛错，保留该城 ID，下次只补完安置。
                    if (city == null && target.City?.Realm == owner) city = target.City;
                    if (city != null) project.city = city.Key;
                }
                if (city == null) { Cancel(payer, state, "land"); return; }
            }
            if (city.IsGone()) { Cancel(payer, state, "changed"); return; }
            CityPopulationData population = CityPopulationSystem.Get(city);
            population.settlement_supplies ??= new Dictionary<string, int>();
            if (project.spontaneous) population.spontaneous_settlement = true;
            if (!project.initialized)
            {
                Person actor = Founder(owner, source);
                if (!project.native_initialized && actor != null)
                { city.SetUnitMetas(actor); city.NewCityEvent(actor); project.native_initialized = true; }
                CultureService.SetCityMainCulture(city, CultureService.GetMainCulture(source));
                project.initialized = true;
            }
            if (!project.paid)
            {
                int startup = Math.Min(project.gold, project.startup_gold);
                payer.SubMoney(project.gold - startup, TreasuryCategory.Construction);
                payer.SubMoney(startup, TreasuryCategory.InternalTransfer);
                project.paid = true;
                // 官办建城征发民力筑城：记一笔工役，算大兴土木(自发移民不算，见 CorveeSystem)
                if (!project.spontaneous) CorveeSystem.Add(payer, CorveeSystem.NewCity, true);
            }
            ReserveForProject(state, 0);
            if (!project.startup_paid) { city.AddMoney(project.startup_gold, TreasuryCategory.InternalTransfer); project.startup_paid = true; }
            foreach (SettlementMigration move in project.migrants)
            {
                if (move.done) continue;
                City donor = EmpireWorld.Current.CityByKey(move.source);
                if (!ValidCity(donor) || donor.Realm != owner || FundingRealm(owner) != payer ||
                    CityPopulationSystem.CivilianBackground(donor) + 0.01f < move.people)
                { state.status = "recovery"; return; }
                float moved = CityPopulationSystem.TransferCivilianBackground(donor, city, move.people);
                move.people -= moved;
                move.done = move.people < 0.01f;
                if (!move.done) { state.status = "recovery"; return; }
            }
            if (project.granary_food > 0 && !project.granary_taken)
            {
                if (payer.GetOrCreate().granary < project.granary_food) { state.status = "recovery"; return; }
                payer.GetOrCreate().granary -= project.granary_food;
                AddSupplies(population, project.granary_resource, project.granary_food);
                project.granary_taken = true;
            }
            foreach (SettlementCargo cargo in project.cargo)
            {
                if (cargo.delivered) continue;
                City donor = EmpireWorld.Current.CityByKey(cargo.source);
                if (!cargo.taken)
                {
                    int remaining = cargo.amount - cargo.in_transit;
                    if (project.spontaneous && ValidCity(donor) && donor.Realm == owner)
                    {
                        // 恢复期间行粮被消耗也不能卡死迁民；已有启运数量仍按原日志交付。
                        remaining = Math.Min(remaining, Math.Max(0, donor.GetResourcesAmount(cargo.resource)));
                        cargo.amount = cargo.in_transit + remaining;
                        if (cargo.amount == 0) { cargo.taken = cargo.delivered = true; continue; }
                    }
                    if (!ValidCity(donor) || donor.Realm != owner || donor.GetResourcesAmount(cargo.resource) < remaining)
                    { state.status = "recovery"; return; }
                    int before = donor.GetResourcesAmount(cargo.resource);
                    try
                    {
                        using (PopulationEconomySystem.PrivateUse()) donor.TakeResource(cargo.resource, remaining);
                    }
                    finally
                    {
                        // 原版/其它钩子只扣了部分货物或扣货后抛错时，也保存已经启运的实际数量。
                        int taken = Math.Max(0, Math.Min(remaining, before - donor.GetResourcesAmount(cargo.resource)));
                        cargo.in_transit += taken;
                        cargo.taken = cargo.in_transit >= cargo.amount;
                        PopulationEconomySystem.TakePublicStock(donor, cargo.resource, taken);
                        CityPopulationSystem.Get(donor).public_paid += cargo.price * taken / cargo.amount;
                    }
                    if (!cargo.taken) { state.status = "recovery"; return; }
                }
                AddSupplies(population, cargo.resource, cargo.amount);
                PopulationEconomySystem.AddPublicStock(city, cargo.resource, cargo.amount);
                cargo.delivered = true;
            }
            if (!project.politics_done)
            {
                CityPopulationSystem.Census(city, population, keepBackground: true, preserveSmallGroups: true);
                CityPopulationSystem.EnsureLeader(city);
                CityPopulationSystem.Census(city, population, keepBackground: true, preserveSmallGroups: true);
                if ((CityStateService.IsCityState(city.Realm) || TribalFissionService.IsTribal(city.Realm)) &&
                    city.Realm.Cities.Count > 1 &&
                    (city.Leader == null || !city.Leader.IsAdult() || !city.Leader.IsFitToRule()))
                { state.status = "recovery"; return; }
                if (project.create_fief)
                {
                    if (!FinishFief(payer, project, city)) { state.status = "recovery"; return; }
                }
                else
                {
                    CityStateService.OnCityBuilt(city, city.Leader);
                    TribalFissionService.OnCityBuilt(city, city.Leader);
                }
                project.politics_done = true;
            }
            // 启动现有正常建设：资源由公共库存支付，仍遵守住房/风车规划和科技。
            CityPopulationSystem.InvalidateHouseholdCaches();
            city.UpdateCityStatus();
            CityConstructionSystem.StartSettlement(city);
            if (!project.spontaneous)
            {
                state.last_completed = GameClock.Now;
                state.last_city = city.Key;
            }
            state.project = null;
            state.status = project.spontaneous ? "spontaneous_done" : "cooldown";
            string historyKey = "state_settlement_" + (project.spontaneous ? "spontaneous_" : project.create_fief ? "fief_" : "") +
                (project.overseas ? "sea_history" : "history");
            string content = string.Format(LM.Get(historyKey), payer.GetKingdomName(),
                source?.GetCityName() ?? "?", (int)Math.Round(project.people), city.GetCityName(), project.gold,
                project.spontaneous ? LM.Get("state_settlement_cause_" + project.cause) : "");
            LogService.LogInfo(content);
            payer.GetEmpire()?.RecordHistory(directContent: content, kingdomId: payer.Key);
        }
        catch (Exception error)
        {
            state.status = state.project == null ? project.spontaneous ? "spontaneous_done" : "cooldown" : "recovery";
            LogService.LogWarning($"[EmpireCraft] 国家建城项目待恢复({payer.Key}): {error.Message}");
        }
    }

    private static bool FinishFief(Realm payer, StateSettlementProject project, City city)
    {
        Empire empire = payer.GetEmpire();
        if (empire == null || empire.Key != project.empire || empire.IsGone()) return false;
        Realm fief = EmpireWorld.Current.RealmByKey(project.fief);
        if (fief == null)
        {
            if (EnfeoffmentHelper.IsRealmCapital(city)) return false;
            Person leader = city.Leader;
            if (leader == null || !leader.IsAdult() || !leader.IsFitToRule()) return false;
            // 复用城市立国入口，只拆新建城市，不把旧法理下的其它城市一起划走。
            Realm previous = city.Realm;
            // 新城立封国：受命立国，不许自立的闸放行(见 SecessionGate)
            try { fief = SecessionGate.Sanctioned(() => city.MakeOwnRealm(leader)); }
            finally
            {
                if (fief == null && city.Realm != previous) fief = city.Realm;
                if (fief != null) project.fief = fief.Key;
            }
            if (fief == null) return false;
        }
        fief.SetRegimeType((RegimeType)project.fief_regime);
        fief.LoadRegime();
        var regime = fief.GetRegime();
        regime?.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
        regime?.SetAllowSupportCenterArmy(false);
        regime?.SetTaxLevel(TaxLevel.Low);
        regime?.SetAllowDiplomacy(true);
        regime?.SetAllowArmy(true);
        fief.SetFiedTimestamp(GameClock.Now);
        empire.join(fief, pForce: true);
        fief.ReconcileMainTitle();
        return true;
    }

    private static void AddSupplies(CityPopulationData population, string resource, int amount)
    {
        population.settlement_supplies.TryGetValue(resource, out int have);
        population.settlement_supplies[resource] = have + amount;
    }

    private static void Cancel(Realm payer, StateSettlementData state, string reason)
    {
        Searches.Remove(payer.Key);
        ReserveForProject(state, 0);
        state.project = null;
        state.status = reason;
    }

    public static bool AwaitingPopulation(City city) => CreatingCity || city?.Realm != null &&
        FundingRealm(city.Realm)?.GetOrCreate().settlement?.project is StateSettlementProject project &&
        project.city == city.Key && project.migrants.Any(move => !move.done);

    public static string Status(Realm owner)
    {
        Realm payer = FundingRealm(owner);
        return LM.Get("state_settlement_" + (payer == null ? "idle" : Data(payer).status));
    }

    public static bool WantsPort(City city) => PopulationMode && ValidCity(city) &&
        (FundingRealm(city.Realm)?.GetOrCreate().settlement?.status == "resettlement_port" ||
         Enabled && FundingRealm(city.Realm)?.GetOrCreate().settlement?.status == "port") &&
        city.GetBuildingOfType("type_docks", pCountOnlyFinished: false) == null;

#if EC_REPLACED // written by hand for this game: see Assets/EmpireCraft/Core/Seams
    public static void ModeChanged(bool enabled)
    {
        CityResettlementSystem.ModeChanged(enabled);
        ResetWorldState();
        foreach (Realm kingdom in EmpireWorld.Current.kingdoms.ToList())
        {
            StateSettlementData state = kingdom.GetOrCreate().settlement;
            if (state == null) continue;
            state.last_checked = GameClock.Now;
            if (!enabled && state.project != null)
            {
                if (state.project.city < 0L) { state.project = null; state.status = "idle"; }
                else Complete(kingdom, state);
            }
        }
    }
#endif

    public static void ResetWorldState()
    {
        CityResettlementSystem.ResetWorldState();
        Searches.Clear(); PendingSearches.Clear(); CreatingCity = false;
    }
}
}
