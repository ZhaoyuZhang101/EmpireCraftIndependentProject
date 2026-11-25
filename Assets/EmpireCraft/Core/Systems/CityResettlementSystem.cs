// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CityResettlementSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 空城优先接收附近城市的真实平民。每个出资政权一次一座，选源和海路搜索分帧执行。
// 陆路够不够得着：通达层算好后(用户 2026-11-06 同意"先信息类、再贸易迁徙"，见 ConnectivitySystem)按人员走过去的成本
// (resettle_max_cost 以内、不走海路，查两城的近邻表)，翻山、绕无人区远的不算近；没算好时照旧直线 192 格、原版寻路走得到。
internal static class CityResettlementSystem
{
    private sealed class Work
    {
        public long[] sources;
        public int cursor;
        public City landSource;
        public float landPeople;
        public long landDistance = long.MaxValue;
        public readonly List<(City city, long distance)> seaSources = new();
        public int seaCursor;
        public SettlementSeaSearch sea;
        public string unavailable = "resettlement_people";
    }

    private static readonly Dictionary<long, Work> Works = new();
    private static readonly Queue<long> Pending = new();
    private static bool Enabled => CityPopulationSystem.AbstractPopulationEnabled && !ModClass.IS_CLEAR;
#if EC_PENDING
    private static bool Valid(City city) => city?.Record != null && !city.IsGone() &&
        city.Realm != null && !city.Realm.IsGone() && !city.Realm.IsWild &&
        !AncientWarfareCompatibility.OwnsObject(city) && !AncientWarfareCompatibility.Owns(city.Realm) &&
        city.SeatCell()?.region?.island != null;

    private static bool Managed(Realm payer, City city)
    {
        if (!Valid(city)) return false;
        if (StateSettlementSystem.FundingRealm(city.Realm) == payer) return true;
        var empire = payer.GetEmpire();
        return empire != null && empire.CoreKingdom == payer && city.Realm.GetEmpire() == empire;
    }
#endif

    private static bool Due(City city) => CityPopulationSystem.Get(city).last_resettlement < 0d ||
        GameClock.MonthsSince(CityPopulationSystem.Get(city).last_resettlement) >= 1;

#if EC_PENDING
    private static bool Empty(City city) => Valid(city) && CityPopulationSystem.GetTotal(city) < 1f &&
        city.People?.Any(actor => actor != null && actor.City == city && !actor.IsGone() && actor.Alive) != true &&
        !StateSettlementSystem.AwaitingPopulation(city) && Due(city) &&
        !(city.GetOrCreate().OccupiedStatus?.Count > 0) &&
        city.Zones.Any(zone => zone.GroundTiles > 0);

    public static bool Request(Realm payer, StateSettlementData state)
    {
        if (!Enabled) return false;
        if (state.resettlement == null)
        {
            City target = StateSettlementSystem.ExpansionCities(payer).FirstOrDefault(Empty);
            if (target == null) return false;
            state.resettlement = new CityResettlementProject { target = target.Key, target_owner = target.Realm.Key };
        }
        if (!Works.ContainsKey(payer.Key))
        {
            Works[payer.Key] = new Work { sources = StateSettlementSystem.ExpansionCities(payer).Select(city => city.Key).ToArray() };
            Pending.Enqueue(payer.Key);
        }
        return true;
    }

    private static float Migrants(City source, City target)
    {
        if (!Valid(source) || !Due(source) || source == target ||
            source.People?.Any(actor => actor != null && !actor.IsGone() && actor.Alive && actor.HasTrait("plague")) == true)
            return 0f;
        float civilians = CityPopulationSystem.CivilianBackground(source);
        float people = Math.Max(0f, Math.Min(civilians * 0.05f, civilians - CityPopulationSystem.PeoplePerSlot(source)));
        float seed = CityPopulationSystem.PeoplePerSlot(target) * 2f;
        float capacity = Math.Max(0f, target.getPopulationMaximum());
        if (capacity > 0f) seed = Math.Min(seed, Math.Max(1f, capacity - CityPopulationSystem.GetTotal(target)));
        return (float)Math.Floor(Math.Min(people, seed));
    }
#endif

    // 陆路成本(见类头)：查不到(通达层没算好、城不在图上)返回 -1；走不到、太远、要走海路返回 +∞
    private static float LandCost(City source, City target)
    {
        if (!ConnectivitySystem.Ready) return -1f;
        float cost = ConnectivitySystem.NearCost(target, source, out bool sea);
        if (cost < 0f) return -1f;
        if (float.IsPositiveInfinity(cost) || sea)
        {
            float back = ConnectivitySystem.NearCost(source, target, out bool backSea);
            if (back >= 0f && !float.IsPositiveInfinity(back) && !backSea) cost = back;
            else return float.PositiveInfinity;
        }
        return cost <= ConnectivitySystem.Config.resettle_max_cost ? cost : float.PositiveInfinity;
    }

    // 陆路够得着：同一块陆地、通达层的陆路成本够近(没算好时直线 192 格、原版寻路走得到)
#if EC_PENDING
    private static bool LandReach(City source, City target)
    {
        if (!source.SeatCell().IsSameIsland(target.SeatCell())) return false;
        float cost = LandCost(source, target);
        if (cost >= 0f) return !float.IsPositiveInfinity(cost);
        return Distance(source, target) <= 192L * 192L && target.SeatCell().reachableFrom(source.SeatCell());
    }
#endif

    private static long Distance(City a, City b)
    {
        long dx = (long)a.SeatCell().X - b.SeatCell().X, dy = (long)a.SeatCell().Y - b.SeatCell().Y;
        return dx * dx + dy * dy;
    }

#if EC_PENDING
    public static void Tick()
    {
        if (!Enabled || Pending.Count == 0 || !SimulationFrameBudget.HasTime) return;
        long id = Pending.Dequeue();
        try { Process(id); }
        catch (Exception error)
        {
            var state = EmpireWorld.Current.RealmByKey(id)?.GetOrCreate().settlement;
            if (state?.resettlement != null)
            {
                state.status = "resettlement_recovery";
                Pending.Enqueue(id);
            }
            LogService.LogWarning($"[EmpireCraft] 空城安置待恢复({id}): {error.Message}");
        }
    }

    private static void Process(long id)
    {
        Realm payer = EmpireWorld.Current.RealmByKey(id);
        StateSettlementData state = payer?.GetOrCreate().settlement;
        CityResettlementProject plan = state?.resettlement;
        if (payer == null || payer.IsGone() || plan == null) { Works.Remove(id); return; }
        City target = EmpireWorld.Current.CityByKey(plan.target);
        // 人口已经到达后，归属变化也不能丢弃已扣除的货物；只恢复实际在途部分。
        if (plan.arrived && Valid(target)) { Finish(payer, state, target); Requeue(id, state); return; }
        if (!Managed(payer, target) || target.Realm.Key != plan.target_owner)
        { Cancel(payer, state, "changed"); return; }
        if (!Empty(target)) { Cancel(payer, state, "changed"); return; }
        if (!Works.TryGetValue(id, out Work work)) return;
        if (plan.source >= 0L)
        {
            Arrive(payer, state, target);
            // 等待航行的项目由下个月 Request 重新排队，不每帧轮询世界时间。
            if (plan.arrived) Requeue(id, state);
            else if (state.resettlement != null) Works.Remove(id);
            return;
        }
        for (int steps = 0; steps < 8 && work.cursor < work.sources.Length && SimulationFrameBudget.HasTime; steps++)
        {
            City source = EmpireWorld.Current.CityByKey(work.sources[work.cursor++]);
            if (!Managed(payer, source)) continue;
            float people = Migrants(source, target);
            if (people < 1f) continue;
            work.unavailable = "resettlement_unreachable";
            long distance = Distance(source, target);
            if (source.SeatCell().IsSameIsland(target.SeatCell()))
            {
                if (!LandReach(source, target)) continue;
                // 通达层算好后同等规模按陆路成本比远近(成本放大成整数，同直线距离一样比较)
                float cost = LandCost(source, target);
                if (cost >= 0f) distance = (long)(cost * 1000f);
                // 能安置更多人优先，同等规模选择更近的城市。
                if (people > work.landPeople || people == work.landPeople && distance < work.landDistance)
                { work.landSource = source; work.landPeople = people; work.landDistance = distance; }
            }
            else
            {
                work.seaSources.Add((source, distance));
                work.seaSources.Sort((a, b) => a.distance.CompareTo(b.distance));
                if (work.seaSources.Count > 4) work.seaSources.RemoveAt(4);
            }
        }
        if (work.cursor < work.sources.Length || !SimulationFrameBudget.HasTime)
        { Pending.Enqueue(id); return; }
        if (work.landSource != null)
        {
            Select(plan, work.landSource);
            Pending.Enqueue(id); // 安置另占一帧，避免选源与建设初始化挤在一起。
            return;
        }
        if (work.sea == null)
        {
            if (work.seaCursor >= work.seaSources.Count) { Cancel(payer, state, work.unavailable); return; }
            if (Works.Values.Count(item => item.sea != null) >= 4) { Pending.Enqueue(id); return; }
            City source = work.seaSources[work.seaCursor++].city;
            Select(plan, source);
            plan.source = -1L;
            work.sea = new SettlementSeaSearch(source, StateSettlementSystem.ExpansionCities(payer), target);
        }
        City sailor = EmpireWorld.Current.CityByKey(plan.journey.source);
        if (!Managed(payer, sailor)) { Cancel(payer, state, "changed"); return; }
        work.sea.Advance(sailor);
        for (int steps = 0; steps < 8 && SimulationFrameBudget.HasTime; steps++)
        {
            var landing = work.sea.NextLanding();
            if (landing == null) break;
            if (!target.SeatCell().IsSameIsland(landing.landfall) || !target.SeatCell().reachableFrom(landing.landfall) ||
                landing.zone.City != target) continue;
            work.sea.Accept(landing).Apply(plan.journey);
            plan.source = sailor.Key;
            plan.journey.started = GameClock.Now;
            state.status = "resettlement_sailing";
            Works.Remove(id);
            return;
        }
        if (work.sea.Finished)
        {
            if (work.sea.unavailable == "port" || work.sea.unavailable == "navigation" ||
                work.sea.unavailable == "overseas_tech")
                work.unavailable = "resettlement_" + work.sea.unavailable;
            work.sea = null;
        }
        Pending.Enqueue(id);
    }
#endif

    private static void Select(CityResettlementProject plan, City source)
    {
        plan.source = source.Key;
        plan.source_owner = source.Realm.Key;
        plan.journey = new StateSettlementProject { source = source.Key, owner = source.Realm.Key, spontaneous = true };
    }

#if EC_PENDING
    private static void Arrive(Realm payer, StateSettlementData state, City target)
    {
        var plan = state.resettlement;
        City source = EmpireWorld.Current.CityByKey(plan.source);
        if (!Managed(payer, source) || source.Realm.Key != plan.source_owner)
        { Cancel(payer, state, "changed"); return; }
        if (plan.journey.overseas)
        {
            if (!SettlementSeaSearch.Validate(plan.journey, source, out Cell shore, out string reason, target))
            {
                Cancel(payer, state, reason == "overseas_tech" || reason == "navigation" || reason == "port"
                    ? "resettlement_" + reason : "resettlement_unreachable");
                return;
            }
            if (!target.SeatCell().IsSameIsland(shore) || !target.SeatCell().reachableFrom(shore))
            { Cancel(payer, state, "resettlement_unreachable"); return; }
            if (GameClock.MonthsSince(plan.journey.started) < SettlementSeaSearch.TravelMonths(plan.journey.sea_route.Count, source))
            { state.status = "resettlement_sailing"; return; }
        }
        else if (!LandReach(source, target))
        { Cancel(payer, state, "resettlement_unreachable"); return; }
        float people = Migrants(source, target);
        if (people < 1f) { Cancel(payer, state, "resettlement_people"); return; }
        PlanCargo(plan, source, target, people);
        float before = CityPopulationSystem.CivilianBackground(target);
        try { CityPopulationSystem.TransferCivilianBackground(source, target, people); }
        finally
        {
            plan.people = Math.Max(0f, CityPopulationSystem.CivilianBackground(target) - before);
            if (plan.people >= 1f)
            {
                plan.arrived = true;
                CityPopulationSystem.Get(source).last_resettlement = CityPopulationSystem.Get(target).last_resettlement =
                    GameClock.Now;
                CityPopulationSystem.Get(target).spontaneous_settlement = true;
            }
        }
        if (!plan.arrived) { Cancel(payer, state, "resettlement_people"); return; }
        Finish(payer, state, target);
    }

    private static void PlanCargo(CityResettlementProject plan, City source, City target, float people)
    {
        plan.cargo.Clear();
        int need = (int)Math.Ceiling(people / CityPopulationSystem.PeoplePerSlot(target));
        var foods = AssetManager.resources.list.Where(resource => resource.type == ResType.Food).ToArray();
        int surplus = Math.Max(0, foods.Sum(resource => source.getResourcesAmount(resource.id)) -
            (int)Math.Ceiling((CityPopulationSystem.GetTotal(source) - people) / CityPopulationSystem.PeoplePerSlot(source)));
        foreach (var food in foods)
        {
            int amount = Math.Min(need, Math.Min(surplus, source.getResourcesAmount(food.id)));
            if (amount > 0) plan.cargo.Add(new SettlementCargo { source = source.Key, resource = food.id, amount = amount });
            need -= amount; surplus -= amount;
        }
        foreach (var item in new[] { ("wood", 6), ("stone", 4) })
        {
            int amount = Math.Min(item.Item2, Math.Max(0, source.getResourcesAmount(item.Item1)));
            if (amount > 0) plan.cargo.Add(new SettlementCargo { source = source.Key, resource = item.Item1, amount = amount });
        }
    }

    private static void Finish(Realm payer, StateSettlementData state, City target)
    {
        var plan = state.resettlement;
        City source = EmpireWorld.Current.CityByKey(plan.source);
        var population = CityPopulationSystem.Get(target);
        population.settlement_supplies ??= new Dictionary<string, int>();
        try
        {
            foreach (var cargo in plan.cargo)
            {
                if (cargo.delivered) continue;
                if (!cargo.taken)
                {
                    int before = Valid(source) && source.Realm.Key == plan.source_owner &&
                        target.Realm.Key == plan.target_owner && Managed(payer, target) ? source.getResourcesAmount(cargo.resource) : 0;
                    try
                    {
                        if (before > 0)
                            using (PopulationEconomySystem.PrivateUse()) source.takeResource(cargo.resource, Math.Min(before, cargo.amount));
                    }
                    finally
                    {
                        int taken = Math.Max(0, before - (before > 0 ? source.getResourcesAmount(cargo.resource) : 0));
                        cargo.in_transit = taken;
                        cargo.public_in_transit = taken > 0 ? PopulationEconomySystem.TakePublicStock(source, cargo.resource, taken) : 0;
                        cargo.taken = true;
                    }
                }
                population.settlement_supplies.TryGetValue(cargo.resource, out int have);
                population.settlement_supplies[cargo.resource] = have + cargo.in_transit;
                PopulationEconomySystem.AddPublicStock(target, cargo.resource, cargo.public_in_transit);
                cargo.delivered = true;
            }
            if (string.IsNullOrEmpty(CultureService.GetMainCulture(target, initialize: false)) && Valid(source))
                CultureService.SetCityMainCulture(target, CultureService.GetMainCulture(source));
            CityPopulationSystem.Census(target, population, keepBackground: true, preserveSmallGroups: true);
            if (Enabled)
            {
                CityPopulationSystem.EnsureLeader(target);
                CityPopulationSystem.Census(target, population, keepBackground: true, preserveSmallGroups: true);
                CityConstructionSystem.StartSettlement(target);
            }
            CityPopulationSystem.InvalidateHouseholdCaches();
            target.updateCityStatus();
            state.resettlement = null;
            state.status = "resettled";
            Works.Remove(payer.Key);
            string message = string.Format(LM.Get("state_settlement_resettlement_history"), source?.GetCityName() ?? "?",
                (int)Math.Round(plan.people), target.GetCityName());
            LogService.LogInfo(message);
            payer.GetEmpire()?.RecordHistory(directContent: message, kingdomId: payer.Key);
        }
        catch (Exception error)
        {
            state.status = state.resettlement == null ? "resettled" : "resettlement_recovery";
            LogService.LogWarning($"[EmpireCraft] 空城安置待恢复({target.Key}): {error.Message}");
        }
    }
#endif

    private static void Requeue(long id, StateSettlementData state)
    { if (state.resettlement != null) Pending.Enqueue(id); }
    private static void Cancel(Realm payer, StateSettlementData state, string status)
    { state.resettlement = null; state.status = status; Works.Remove(payer.Key); }
    public static void ResetWorldState() { Works.Clear(); Pending.Clear(); }
#if EC_PENDING
    public static void ModeChanged(bool enabled)
    {
        if (enabled) return;
        foreach (Realm payer in EmpireWorld.Current.LiveRealms)
        {
            var state = payer.GetOrCreate().settlement;
            if (state?.resettlement == null) continue;
            City target = EmpireWorld.Current.CityByKey(state.resettlement.target);
            if (state.resettlement.arrived && Valid(target)) Finish(payer, state, target);
            else Cancel(payer, state, "idle");
        }
    }
#endif
}
}
