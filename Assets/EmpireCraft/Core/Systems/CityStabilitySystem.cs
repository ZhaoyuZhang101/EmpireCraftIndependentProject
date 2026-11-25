// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CityStabilitySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Time = EmpireCraft.Core.FrameClock;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public static class CityStabilitySystem
{
    private static EmpireWorld _world;
    private static int _cursor;
    private static bool _virtual;
    private sealed class Request
    {
        public City City;
        public Realm Owner;
        public CityStabilityData State;
        public CityStabilityInput Input;
    }
    private sealed class Batch
    {
        public Request[] Requests;
        public PopulationMathWorkers.Job Job;
        public CityStabilityBudget[] Results;
        public int Cursor;
        public float SubmittedAt;
        public bool FromWorker;
    }
    private static readonly List<Request> Capturing = new();
    private static readonly Queue<Batch> Pending = new();
    private static readonly HashSet<City> Queued = new();
    private sealed class Retirement
    {
        public City City;
        public Realm Owner;
        public CityStabilityData State;
        public int Cursor;
    }
    private static readonly Queue<Retirement> Retirements = new();
    private static readonly HashSet<City> Retiring = new();
    public static int WorkerResults { get; private set; }
    public static int FallbackResults { get; private set; }
    public static int PendingCities => Queued.Count;
    public static int PendingRetirements => Retiring.Count;
    private readonly struct GuardCount
    {
        public readonly int Assigned, Present;
        public GuardCount(int assigned, int present) { Assigned = assigned; Present = present; }
    }
    private static readonly Dictionary<City, GuardCount> GuardCounts = new();
    private static readonly Dictionary<City, int> RecruitCursors = new();
    private static int _countFrame = -1;
    private static EmpireWorld _countWorld;
    private static bool _countVirtual;

#if EC_PENDING
    public static void ResetWorldState()
    {
        _world = EmpireWorld.Current; _virtual = CityPopulationSystem.AbstractPopulationEnabled; _cursor = 0;
        Capturing.Clear(); Pending.Clear(); Queued.Clear(); GuardCounts.Clear(); _countFrame = -1;
        RecruitCursors.Clear();
        Retirements.Clear(); Retiring.Clear();
        WorkerResults = FallbackResults = 0;
        FiscalBudgetPlanner.ResetWorldState();
    }

    public static void Tick()
    {
        if (ModClass.IS_CLEAR || EmpireWorld.Current == null) { ResetWorldState(); return; }
        if (!ReferenceEquals(_world, EmpireWorld.Current) || _virtual != CityPopulationSystem.AbstractPopulationEnabled) ResetWorldState();
        if (!SimulationFrameBudget.HasTime) return;
        using var measured = SimulationFrameBudget.Measure();
        using var profile = FrameProfiler.Measure("地方稳定与财政");
        DrainRetirements();
        if (!SimulationFrameBudget.HasTime) return;
        if (_virtual && Pending.Count > 0)
        {
            Batch batch = Pending.Peek();
            if (batch.Results == null)
            {
                if (batch.Job != null && batch.Job.TryGetResult(out PopulationNumericResult numeric))
                { batch.Results = numeric.Stability; batch.FromWorker = true; }
                else if (batch.Job == null || Time.realtimeSinceStartup - batch.SubmittedAt > 0.25f)
                    batch.Results = CityStabilityMath.Compute(batch.Requests.Select(request => request.Input).ToArray());
            }
            if (batch.Results != null)
            {
                Request request = batch.Requests[batch.Cursor];
                City city = request.City;
                Queued.Remove(city);
                if (TreasurySystem.Enabled(city) && city.Realm == request.Owner &&
                    ReferenceEquals(city.GetOrCreate().stability, request.State) && Due(request.State))
                {
                    CityStabilityInput current = CaptureInput(city);
                    bool matches = request.Input.Equals(current);
                    CityStabilityBudget budget = matches ? batch.Results[batch.Cursor] : CityStabilityMath.Compute(current);
                    if (matches && batch.FromWorker) WorkerResults++; else FallbackResults++;
                    UpdatePrepared(city, request.State, current, budget);
                }
                if (++batch.Cursor == batch.Requests.Length) Pending.Dequeue();
            }
        }
        if (!SimulationFrameBudget.HasTime || _virtual && Queued.Count >= 16) return;
        var cities = EmpireWorld.Current.Cities;
        if (cities == null || cities.Count == 0) return;
        if (_cursor >= cities.Count) _cursor = 0;
        City next = cities[_cursor++];
        if (TreasurySystem.Enabled(next))
        {
            if (!_virtual) Update(next);
            else if (!Queued.Contains(next))
            {
                CityStabilityData state = State(next);
                if (Due(state))
                {
                    Capturing.Add(new Request { City = next, Owner = next.Realm, State = state, Input = CaptureInput(next) });
                    Queued.Add(next);
                }
            }
        }
        if (_virtual && Capturing.Count > 0 && (Capturing.Count >= 8 || _cursor >= cities.Count))
        {
            Request[] requests = Capturing.ToArray(); Capturing.Clear();
            var input = new PopulationNumericInput(requests.Select(request => request.Input).ToArray());
            Pending.Enqueue(new Batch { Requests = requests, Job = PopulationMathWorkers.Submit(input),
                SubmittedAt = Time.realtimeSinceStartup });
        }
    }
#endif

    private const int CatchUpMonths = 24;

    private static bool Due(CityStabilityData state) => state != null &&
        (state.last_update < 0d || GameClock.MonthsSince(state.last_update) >= 1);

#if EC_PENDING
    private static bool CanRetire(Person actor, City city) => actor != null && actor.Alive && actor.IsWarrior() &&
        !actor.IsRuler() && !actor.IsCityLeader() && actor.City == city && actor.current_tile?.zone_city == city &&
        actor.Realm != null && (actor.Realm == city.Realm || actor.Realm.IsInSameEmpire(city.Realm));

    private static void DrainRetirements()
    {
        if (Retirements.Count == 0) return;
        Retirement work = Retirements.Dequeue();
        City city = work.City;
        if (!TreasurySystem.Enabled(city) || city.Realm != work.Owner ||
            !ReferenceEquals(city.GetOrCreate().stability, work.State) || !work.State.withdrawn)
        { Retiring.Remove(city); return; }
        int visited = 0, retired = 0;
        while (work.Cursor < city.units.Count && visited < 16 && retired < 4 && SimulationFrameBudget.HasTime)
        {
            Person soldier = city.units[work.Cursor++]; visited++;
            if (!CanRetire(soldier, city)) continue;
            CityPopulationSystem.DemobilizeSoldier(soldier, city); retired++;
        }
        GuardCounts.Remove(city);
        if (work.Cursor < city.units.Count) { Retirements.Enqueue(work); return; }
        Retiring.Remove(city);
        CityStabilityBudget budget = CityStabilityMath.Compute(CaptureInput(city));
        ReviewWithdrawal(city, work.State, budget.GuardMonthly + budget.GovernanceAnnual / 12d);
    }

    public static CityStabilityData State(City city)
    {
        if (!TreasurySystem.Enabled(city)) return null;
        var extra = city.GetOrCreate();
        var state = extra.stability;
        if (state != null && state.owner_id == city.Realm.Key) return state;
        // 旧政权的欠款不随城市转给新主人，也不凭空消失
        if (state != null) CityAssetSettlement.ReturnArrearsToDebtor(city, state);
        GuardCounts.Remove(city);
        RecruitCursors.Remove(city);
        float natural = Natural(city, out string detail);
        extra.stability = new CityStabilityData
        {
            owner_id = city.Realm.Key,
            stability = state?.stability ?? natural,
            natural_stability = natural,
            governance_policy = state?.governance_policy ?? -1,
            garrison_policy = state?.garrison_policy ?? -1,
            fear = state?.fear ?? 0f,
            last_massacre = state?.last_massacre ?? -1d,
            pressure_detail = detail
        };
        return extra.stability;
    }

    private static CityStabilityInput CaptureInput(City city)
    {
        Empire empire = city.Realm.GetEmpire();
        int loyalty = city.getLoyalty();
        int legitimacy = empire?.Legitimacy ?? -1;
        float corruption = (float)CorruptionSystem.GetRate(city.Realm);
        float sentiment = NationalSentimentSystem.GetCity(city);
        float landless = LandEconomySystem.GetLandlessRatio(city);
        // 治理费按财政户(固定 100 人一户)计，进入现代时不跳变
        int households = CityPopulationSystem.FiscalHouseholds(city);
        bool famine = CityPopulationSystem.AbstractPopulationEnabled
            ? CityPopulationSystem.GetGrowthFactors(city).Famine
            : households > 0 && city.getTotalFood() < households * 0.5f;
        float grievance = empire == null ? 0f : InstitutionSystem.GetClassGrievances(empire).Values.DefaultIfEmpty(0f).Max();
        var state = city.GetOrCreate().stability;
        return new CityStabilityInput(city.CountLivingPopulation(), households, loyalty, legitimacy, corruption,
            sentiment, landless, grievance, famine, WorldLawLibrary.world_law_civ_army.isEnabled(),
            state?.governance_policy ?? -1, state?.garrison_policy ?? -1,
            CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.PeoplePerSlot(city) : 1f);
    }
#endif

    private static string Pressure(CityStabilityInput input)
    {
        var causes = new List<string>();
        if (input.Loyalty < 0) causes.Add(LM.Get("rebellion_reason_city_loyalty") + " " + input.Loyalty);
        if (input.Corruption > 0.2f) causes.Add(string.Format(LM.Get("stability_corruption_detail"), input.Corruption * 100f));
        if (input.Sentiment > 20f) causes.Add(string.Format(LM.Get("stability_national_detail"), input.Sentiment));
        if (input.Landless >= 0.2f) causes.Add(string.Format(LM.Get("stability_land_detail"), input.Landless * 100f));
        if (input.Famine) causes.Add(LM.Get("city_pop_famine"));
        if (input.Grievance >= 40f) causes.Add(string.Format(LM.Get("stability_grievance_detail"), input.Grievance));
        return string.Join(" / ", causes);
    }

#if EC_PENDING
    private static float Natural(City city, out string detail)
    {
        CityStabilityInput input = CaptureInput(city);
        detail = Pressure(input);
        float natural = CityStabilityRules.Natural(input.Loyalty, input.Legitimacy, input.Corruption, input.Sentiment,
            input.Landless, input.Famine, input.Grievance);
        return LocalPressures(city, natural, ref detail);
    }
#endif

    // 公式之外按本城情形另扣的自然稳定度(城市稳定度初建、每月结算都要扣——以前只在初建时扣一次，
    // 之后每月按公式重算就没了，难民、异教、鞭长莫及从来没有真正压低过稳定度)
#if EC_PENDING
    private static float LocalPressures(City city, float natural, ref string detail)
    {
        void Note(ref string text, string cause) => text = string.IsNullOrEmpty(text) ? cause : text + " / " + cause;
        // 难民涌入(见 MigrationRelations)
        float refugees = MigrationRelations.RefugeeLoss(city);
        if (refugees > 0f)
        {
            natural = CityStabilityRules.Clamp(natural - refugees);
            Note(ref detail, MigrationRelations.Describe());
        }
        // 久战疲敝(见 FrontCollapse)：正被统一、被雄主讨伐，仗打久了
        float weary = FrontCollapse.WarWeariness(city);
        if (weary > 0f)
        {
            natural = CityStabilityRules.Clamp(natural - weary);
            Note(ref detail, FrontCollapse.DescribeWeariness(weary));
        }
        // 异教之城(见 ReligiousTension)
        float faith = ReligiousTension.StabilityLoss(city);
        if (faith > 0f)
        {
            natural = CityStabilityRules.Clamp(natural - faith);
            Note(ref detail, ReligiousTension.Describe(city));
        }
        // 族群等第(见 EthnicRankSystem)：四等人、旗民分治下等第低的城不服
        float rank = EthnicRankSystem.StabilityShift(city, out string rankDetail);
        if (rank != 0f)
        {
            natural = CityStabilityRules.Clamp(natural + rank);
            Note(ref detail, rankDetail);
        }
        // 剃发易服以后的属地之城(见 BannerSystem.Conquest)
        float queue = BannerSystem.QueueStabilityLoss(city);
        if (queue >= 0.5f)
        {
            natural = CityStabilityRules.Clamp(natural - queue);
            Note(ref detail, LM.Get("banner_queue_stability_detail"));
        }
        // 改土归流以后的土司旧地(见 LateImperialSystem)
        float tusi = LateImperialSystem.TusiStabilityLoss(city);
        if (tusi >= 0.5f)
        {
            natural = CityStabilityRules.Clamp(natural - tusi);
            Note(ref detail, LM.Get("tusi_reform_stability_detail"));
        }
        // 与国教同教的本天下宗教之城、节庆等教义(见 ReligionTenets.ConcordBonus，用户 2026-11-06)
        float concord = ReligionTenets.ConcordBonus(city);
        if (concord > 0f)
        {
            natural = CityStabilityRules.Clamp(natural + concord);
            Note(ref detail, ReligionTenets.DescribeConcord(city));
        }
        // 遭迫害的城(见 ReligiousPersecution)
        float persecuted = ReligiousPersecution.StabilityLoss(city);
        if (persecuted > 0f)
        {
            natural = CityStabilityRules.Clamp(natural - persecuted);
            Note(ref detail, LM.Get("stability_persecution_detail"));
        }
        // 水土不服(见 ShuiTuSystem)：离草游牧治下的农地城、只宜农地的政权治下的旱地城
        float soil = ShuiTuSystem.StabilityLoss(city);
        if (soil > 0f)
        {
            natural = CityStabilityRules.Clamp(natural - soil);
            Note(ref detail, ShuiTuSystem.DescribeStability(city));
        }
        // 世家坞堡、世家赈济(见 GentrySystem.Fortress)
        float gentry = GentrySystem.StabilityBonus(city, out string gentryDetail);
        if (gentry > 0f)
        {
            natural = CityStabilityRules.Clamp(natural + gentry);
            Note(ref detail, gentryDetail);
        }
        // 京师、陪京(见 MultiCapitalSystem)：自然稳定 +5
        float jing = MultiCapitalSystem.StabilityBonus(city);
        if (jing > 0f)
        {
            natural = CityStabilityRules.Clamp(natural + jing);
            Note(ref detail, MultiCapitalSystem.DescribeStability(city));
        }
        // 奢侈品(见 CommodityMarketSystem)：本国市场买得到丝绸、茶、香料、糖……稳定 +4，全缺 -4
        float luxury = CommodityMarketSystem.LuxuryStability(city);
        if (Mathf.Abs(luxury) >= 1f)
        {
            natural = CityStabilityRules.Clamp(natural + luxury);
            Note(ref detail, string.Format(LM.Get("stability_luxury_detail"), Mathf.RoundToInt(CommodityMarketSystem.LuxurySatisfaction(city) * 100f)));
        }
        // 民生用品(见 CommodityMarketSystem)：本国市场缺布匹、缺粮，稳定下降，全缺 -4
        float necessity = CommodityMarketSystem.NecessityStability(city);
        if (necessity <= -1f)
        {
            natural = CityStabilityRules.Clamp(natural + necessity);
            Note(ref detail, string.Format(LM.Get("stability_necessity_detail"), Mathf.RoundToInt(-necessity)));
        }
        // 鞭长莫及：行政控制力低的城自然稳定度下降
        float remote = AdministrativeControlSystem.StabilityLoss(city);
        if (remote < 1f) return natural;
        Note(ref detail, string.Format(LM.Get("stability_admin_detail"), AdministrativeControlSystem.Get(city) * 100f));
        return CityStabilityRules.Clamp(natural - remote);
    }

    public static float Effective(City city)
    {
        var state = State(city);
        if (state == null) return 0f;
        Counts(city, out _, out int present);
        float funding = state.withdrawn ? 0f : state.garrison_funding;
        float suppression = CityStabilityRules.Suppression(present, state.garrison_required, funding);
        return CityStabilityRules.Clamp(state.stability + suppression + state.fear);
    }
#endif

    // 本国有城拖欠治理/驻军/军费(国债加息用)
    public static bool HasArrears(Realm kingdom)
    {
        if (kingdom?.Cities == null) return false;
        foreach (City city in kingdom.Cities)
        {
            CityStabilityData state = city?.Record == null ? null : city.GetOrCreate().stability;
            if (state != null && state.owner_id == kingdom.Key && state.arrears > 0) return true;
        }
        return false;
    }

    // 人心已散：开着财政看实际稳定度不到起事门槛，不开看忠诚为负(自立成片用，见 SecessionSpread)
#if EC_PENDING
    public static bool Disaffected(City city) => city?.Record != null && !city.IsGone() &&
        (TreasurySystem.Enabled(city) ? Effective(city) < CityStabilityRules.RebellionThreshold : city.getLoyalty() < 0);

    public static bool CanRise(City city) => city != null && !city.IsGone() &&
        (!TreasurySystem.Enabled(city) || Effective(city) < CityStabilityRules.RebellionThreshold);
    public static bool ControlsTax(City city)
    {
        var state = State(city);
        if (state == null || state.withdrawn) return false;
        Counts(city, out _, out int present);
        float suppression = CityStabilityRules.Suppression(present, state.garrison_required, state.garrison_funding);
        return suppression > 0f && CityStabilityRules.Clamp(state.stability + suppression + state.fear) >=
            CityStabilityRules.RebellionThreshold;
    }
#endif

    public static int FundedSlots(City city) => TreasurySystem.Enabled(city) &&
        city.GetOrCreate().stability is CityStabilityData state && state.owner_id == city.Realm.Key && !state.withdrawn
        ? state.funded_slots : 0;

    private static float Funding(int due, int paid, bool fractionalBudget) => due > 0
        ? Mathf.Clamp01(paid / (float)due) : fractionalBudget ? 1f : 0f;

#if EC_PENDING
    public static void Counts(City city, out int assigned, out int present, bool refresh = false)
    {
        if (_countFrame != Time.frameCount || !ReferenceEquals(_countWorld, EmpireWorld.Current) ||
            _countVirtual != CityPopulationSystem.AbstractPopulationEnabled)
        { _countFrame = Time.frameCount; _countWorld = EmpireWorld.Current;
          _countVirtual = CityPopulationSystem.AbstractPopulationEnabled; GuardCounts.Clear(); }
        if (city != null && !refresh && GuardCounts.TryGetValue(city, out GuardCount cached))
        { assigned = cached.Assigned; present = cached.Present; return; }
        double all = 0d, local = 0d;
        if (city?.units == null) { assigned = present = 0; return; }
        foreach (Person actor in city.units)
        {
            if (actor == null || actor.IsGone() || !actor.Alive || !actor.IsWarrior() || actor.Realm == null || actor.City != city ||
                actor.Realm != city.Realm && !actor.Realm.IsInSameEmpire(city.Realm)) continue;
            double people = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.LegionAlive(actor) : 1d;
            all += people;
            if (actor.current_tile?.zone_city == city) local += people;
        }
        // 不留军人实体时，守兵只是城里的人数
        if (CorpsSystem.NoMilitaryUnits)
        {
            float virtualGarrison = CorpsSystem.VirtualGarrison(city);
            all += virtualGarrison;
            local += virtualGarrison;
        }
        assigned = (int)Math.Min(int.MaxValue, Math.Floor(all));
        present = (int)Math.Min(int.MaxValue, Math.Floor(local));
        GuardCounts[city] = new GuardCount(assigned, present);
    }
#endif

    private static IEnumerable<Realm> Payers(City city)
    {
        Realm local = city.Realm;
        if (TreasurySystem.Enabled(local)) yield return local;
        Realm central = local.GetEmpire()?.CoreKingdom;
        if (central != local && TreasurySystem.Enabled(central)) yield return central;
    }

    private static long Available(City city) => Math.Max(0, city.GetMoney()) +
        Payers(city).Sum(kingdom => (long)Math.Max(0, kingdom.GetMoney()));

#if EC_PENDING
    private static int Pay(City city, int bill, TreasuryCategory category, bool municipalFirst, bool discretionary = false)
    {
        int remaining = Math.Max(0, bill);
        void Municipality()
        {
            int paid = Math.Min(remaining, discretionary ? TreasurySystem.DiscretionaryFunds(city) : Math.Max(0, city.GetMoney()));
            if (paid > 0) city.SubMoney(paid, category);
            remaining -= paid;
        }
        if (municipalFirst) Municipality();
        foreach (Realm payer in Payers(city))
        {
            // 日常账单按国库预算统筹拨付(要地足额、一般地方按比例)，见 FiscalBudgetPlanner；还旧欠不在此列
            int allowed = discretionary ? remaining : FiscalBudgetPlanner.Allowance(city, payer, remaining);
            int paid = Math.Min(allowed, discretionary ? StateSettlementSystem.DiscretionaryFunds(payer) : Math.Max(0, payer.GetMoney()));
            if (paid > 0) payer.SubMoney(paid, category);
            remaining -= paid;
            if (remaining == 0) break;
        }
        if (!municipalFirst && remaining > 0) Municipality();
        // 俸禄、军饷付给本城的官员和士兵，回到民间(无小人模式，见 EnterpriseSystem)
        EnterpriseSystem.ReturnToLocal(city, Math.Max(0, bill) - remaining);
        return Math.Max(0, bill) - remaining;
    }

    private static void Recruit(City city, int needed)
    {
        if (needed <= 0 || !WorldLawLibrary.world_law_civ_army.isEnabled()) return;
        if (CorpsSystem.NoMilitaryUnits)
        {
            CorpsSystem.RecruitVirtualGarrison(city, needed);
            return;
        }
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            float pool = CityPopulationSystem.GetBackgroundTotal(city);
            if (pool < 1f) return;
            int people = Math.Min(needed, Math.Max(1, (int)(pool * 0.02f)));
            int units = Math.Min(2, Math.Max(1, Mathf.CeilToInt(people / CityPopulationSystem.PeoplePerLegion)));
            CityPopulationSystem.RaiseMilitia(city, units, people / pool, garrison: true);
            return;
        }
        if (city.units == null || city.units.Count == 0) return;
        int filled = 0, visited = 0;
        RecruitCursors.TryGetValue(city, out int cursor);
        // 不复制整城单位表；未轮到的居民留到下月继续，单城征募最多检查 64 人、补 4 人。
        while (visited < Math.Min(64, city.units.Count))
        {
            if (visited > 0 && !SimulationFrameBudget.HasTime) break;
            if (filled >= Math.Min(4, needed)) break;
            if (cursor >= city.units.Count) cursor = 0;
            Person actor = city.units[cursor++]; visited++;
            if (actor == null || actor.IsGone() || !actor.Alive || !actor.IsAdult() || actor.IsWarrior() ||
                actor.IsRuler() || actor.IsCityLeader() || actor.equipment == null || !actor.CanFoundCivKingdom()) continue;
            city.makeWarrior(actor);
            if (actor.IsWarrior()) filled++;
        }
        RecruitCursors[city] = cursor;
    }

    public static void Update(City city)
    {
        CityStabilityData state = State(city);
        if (!Due(state)) return;
        CityStabilityInput input = CaptureInput(city);
        UpdatePrepared(city, state, input, CityStabilityMath.Compute(input));
    }

    private static void UpdatePrepared(City city, CityStabilityData state, CityStabilityInput input, CityStabilityBudget budget)
    {
        if (state.military_fiscal_units_version < 1)
        {
            if (input.PeoplePerFiscalUnit > 1f)
            {
                state.garrison_arrears = (long)(state.garrison_arrears / (double)input.PeoplePerFiscalUnit);
                state.military_arrears = (long)(state.military_arrears / (double)input.PeoplePerFiscalUnit);
                state.garrison_carry /= input.PeoplePerFiscalUnit;
                state.military_carry /= input.PeoplePerFiscalUnit;
                state.arrears = state.governance_arrears + state.garrison_arrears + state.military_arrears;
            }
            state.military_fiscal_units_version = 1;
        }
        // 隔了几个月才轮到这座城(全图一帧至多一城，还要等帧预算；帧率低时一轮要好几年)：稳定度、治理质量、恐惧按经过的
        // 月数补，至多 CatchUpMonths(用户 2026-10-27 截图西大和朝正统 0、苛政 100，赵朝正统 29，都没人造反：存档里近半的城
        // 四年多没结算过，26 年里稳定度只动了 8 点，自然稳定度只有个位数的城实际还停在四五十，起事门槛 40 永远够不着)。
        // 军饷、治理费照旧按一个月开票(补收会一下子掏空国库)
        double previous = state.last_update;
        int months = previous < 0d || GameClock.Now < previous ? 1
            : Mathf.Clamp(GameClock.MonthsSince(previous), 1, CatchUpMonths);
        state.last_update = GameClock.Now;
        string pressure = Pressure(input);
        state.natural_stability = LocalPressures(city, budget.Natural, ref pressure);
        state.pressure_detail = pressure;
        float governance = budget.Governance;
        double governanceAnnual = budget.GovernanceAnnual;
        state.governance_due = CityStabilityRules.Invoice(governanceAnnual, ref state.governance_carry);
        bool municipalBudget = Available(city) > 0;
        state.governance_paid = Pay(city, state.governance_due, TreasuryCategory.Governance, true);
        float governanceFunding = governance > 0f ? Funding(state.governance_due, state.governance_paid, municipalBudget) : 0f;
        state.governance_quality = CityStabilityRules.Clamp(state.governance_quality +
            (governanceFunding >= 0.99f ? 2f * governance : -2f) * months);
        state.garrison_required = budget.Guards;
        CorpsSystem.SyncVirtualGarrison(city, state);
        Counts(city, out int assigned, out _, refresh: true);
        double plannedMonthly = budget.GuardMonthly;
        bool guardBudget = state.garrison_required > 0 &&
            Available(city) >= Math.Max(1d, Math.Ceiling(plannedMonthly + state.garrison_carry));
        if (guardBudget) { state.withdrawn = false; Recruit(city, Math.Max(0, state.garrison_required - assigned)); }
        Counts(city, out assigned, out int present, refresh: true);
        int maintained = Math.Min(present, state.garrison_required);
        // 驻军按人头计价(以前再除以每户人数，100 个兵只算一户的钱，全国驻军每月才几金)
        double garrisonAnnual = CityStabilityRules.GarrisonAnnual(maintained, state.natural_stability);
        state.garrison_due = CityStabilityRules.Invoice(garrisonAnnual, ref state.garrison_carry);
        state.garrison_paid = Pay(city, state.garrison_due, TreasuryCategory.Garrison, false);
        float guardFunding = Funding(state.garrison_due, state.garrison_paid, guardBudget);
        // 超出本城驻防需求的现有部队另记野战军费，替代旧的“国库增长×比例”账单。
        state.military_due = CityStabilityRules.Invoice(CityStabilityRules.MilitaryAnnual(
            Math.Max(0, assigned - maintained), city.Realm.HasEnemies()), ref state.military_carry);
        state.military_paid = Pay(city, state.military_due, TreasuryCategory.Military, false);
        long unpaid = (long)state.governance_due - state.governance_paid + state.garrison_due - state.garrison_paid +
                      state.military_due - state.military_paid;
        state.governance_arrears = Math.Min(long.MaxValue / 6, state.governance_arrears + state.governance_due - state.governance_paid);
        state.garrison_arrears = Math.Min(long.MaxValue / 6, state.garrison_arrears + state.garrison_due - state.garrison_paid);
        state.military_arrears = Math.Min(long.MaxValue / 6, state.military_arrears + state.military_due - state.military_paid);
        state.arrears = state.governance_arrears + state.garrison_arrears + state.military_arrears;
        if (unpaid == 0 && state.arrears > 0)
        {
            long allowance = Math.Max(1L, (long)state.governance_due + state.garrison_due + state.military_due);
            void Repay(ref long debt, TreasuryCategory category)
            {
                int quote = (int)Math.Min(int.MaxValue, Math.Min(debt, allowance));
                // Current services are paid first. Historical arrears use only funds
                // above the reserve and any live settlement project reservation.
                int paid = Pay(city, quote, TreasuryRules.ArrearsCategory(category),
                    category == TreasuryCategory.Governance, discretionary: true);
                debt -= paid; allowance -= paid;
            }
            Repay(ref state.governance_arrears, TreasuryCategory.Governance);
            Repay(ref state.garrison_arrears, TreasuryCategory.Garrison);
            Repay(ref state.military_arrears, TreasuryCategory.Military);
            state.arrears = state.governance_arrears + state.garrison_arrears + state.military_arrears;
        }
        state.garrison_assigned = assigned;
        state.garrison_actual = present;
        state.garrison_funding = guardFunding;
        state.funded_slots = state.garrison_required > 0 && guardFunding > 0f ? CityPopulationSystem.AbstractPopulationEnabled
            ? Math.Max(1, Mathf.CeilToInt(state.garrison_required * guardFunding / CityPopulationSystem.PeoplePerLegion))
            : Mathf.CeilToInt(state.garrison_required * guardFunding) : 0;
        state.suppression = CityStabilityRules.Suppression(present, state.garrison_required, guardFunding);
        bool unsupported = state.garrison_required > 0 && (!guardBudget || guardFunding < 0.99f) ||
            governance > 0f && governanceFunding < 0.99f || state.military_paid < state.military_due;
        state.unfunded_months = unsupported ? Math.Min(1200, state.unfunded_months + months) : 0;
        float before = state.stability;
        state.stability = CityStabilityRules.Advance(before, state.natural_stability, state.governance_quality, months);
        state.last_change = state.stability - before;
        var extra = city.GetOrCreate();
        if (MassacreSystem.LoyaltyPenalty(city) < 0 && extra.massacre_last > state.last_massacre)
        { state.last_massacre = extra.massacre_last; state.fear = 15f; }
        else state.fear = Mathf.Max(0f, state.fear - 0.5f * months);
        RemitSurplus(city, state);
        ReviewWithdrawal(city, state, plannedMonthly + governanceAnnual / 12d);
    }
#endif

    // 城库余钱上缴：超过两年开支(与安全储备)的部分每月上缴一成，按税款分成比例交本国与中央；拒缴的城不交。
    // 以前城市留成而开支极小，城库越攒越多(本地存档里各城合计与全部国库相当)，却从不上缴
#if EC_PENDING
    private static void RemitSurplus(City city, CityStabilityData state)
    {
        Realm local = city.Realm;
        if (local == null || !TreasurySystem.Enabled(local) || city.getLoyalty() <= 0 && !ControlsTax(city)) return;
        long surplus = city.GetTreasuryBalance() - SurplusReserve(city, state);
        if (surplus < 10L) return;
        int remit = (int)Math.Min(int.MaxValue, surplus / 10L);
        Realm central = local.GetEmpire()?.CoreKingdom;
        var shares = TreasurySystem.TaxShares(local);
        int toCentral = central != null && central != local && TreasurySystem.Enabled(central) &&
                        shares.local + shares.central > 0
            ? (int)((long)remit * shares.central / (shares.local + shares.central)) : 0;
        city.SubMoney(remit, TreasuryCategory.InternalTransfer);
        if (toCentral > 0) central.AddMoney(toCentral, TreasuryCategory.InternalTransfer);
        if (remit > toCentral) local.AddMoney(remit - toCentral, TreasuryCategory.InternalTransfer);
    }
#endif

    // 城库该留的钱：两年开支与安全储备取大的；多出来的是余钱(上缴见 RemitSurplus，积压损耗见 TreasuryHoardSystem)
    public static long SurplusReserve(City city)
    {
        // 只读现有的账(换了主人的城下回结算时再重建，见 State)
        CityStabilityData state = city.GetOrCreate().stability;
        return SurplusReserve(city, state != null && state.owner_id == city.Realm?.Key ? state : null);
    }

    private static long SurplusReserve(City city, CityStabilityData state)
    {
        long monthly = state == null ? 0L : (long)state.governance_due + state.garrison_due + state.military_due;
        return Math.Max(TreasurySystem.SafetyReserve(city), monthly * 24L);
    }

#if EC_PENDING
    private static void ReviewWithdrawal(City city, CityStabilityData state, double monthlyCost)
    {
        Realm origin = city.Realm;
        Empire empire = origin.GetEmpire();
        Realm authority = empire?.CoreKingdom ?? origin;
        // 神力拉入未满期的成员同新入帝国(见 MembershipCooldown)
        if (city == authority.Capital || origin.HasEnemies() || authority.HasEnemies() ||
            RebellionSystem.GraceYearsRemaining(origin) > 0 || UnificationSystem.InGrace(origin) ||
            MembershipCooldown.ManualJoinActive(origin)) return;
        if (state.unfunded_months < CityStabilityRules.WithdrawalMonths ||
            state.stability >= CityStabilityRules.RebellionThreshold || state.last_change > 0f) return;
        TreasuryReport fiscal = TreasurySystem.FundingReport(city);
        if (!CityStabilityRules.ShouldWithdraw(state.unfunded_months, state.stability, state.last_change,
            fiscal.months >= 12, fiscal.operating_balance - state.arrears,
            (int)Math.Min(int.MaxValue, Available(city)), monthlyCost)) return;
        state.withdrawn = true;
        state.funded_slots = 0;
        state.suppression = 0f;
        state.garrison_funding = 0f;
        if (Retiring.Contains(city)) return;
        if (city.units.Any(actor => CanRetire(actor, city)))
        {
            Retiring.Add(city);
            Retirements.Enqueue(new Retirement { City = city, Owner = origin, State = state });
            return;
        }
        GuardCounts.Remove(city);
        if (authority.GetOrCreate().last_stability_release >= 0d &&
            GameClock.YearsSince(authority.GetOrCreate().last_stability_release) < 1) return;
        // 不许自立(见 SecessionGate)：撤守之城不据城自立、成员国不离开(不记叛离，Empire.leave 那道门拦不到)
        if (SecessionGate.Forbids("撤守自立", city.Record?.Name)) return;
        Realm independent;
        if (city == origin.Capital)
        {
            if (empire == null || origin == authority || !origin.HasRuler() ||
                origin.Cities.Any(held => Effective(held) >= CityStabilityRules.RebellionThreshold)) return;
            empire.leave(origin);
            independent = origin;
        }
        else
        {
            Person founder = city.Leader;
            if (founder?.CanFoundCivKingdom() != true)
                founder = city.units?.FirstOrDefault(actor => actor.CanFoundCivKingdom() && actor.Alive && actor.isAdult());
            founder ??= CityPopulationSystem.SpawnRebelLeader(city);
            if (founder == null) return;
            independent = city.makeOwnKingdom(founder);
            if (independent == null) return;
            // 据城自立的取当地之俗(见 CultureService.ApplyLocalCulture)
            CultureService.ApplyLocalCulture(independent, city);
            // 同一法理里挨着的、人心也散了的城一并跟着走(见 SecessionSpread)
            SecessionSpread.Spread(independent, city, origin, rebellion: false);
        }
        independent.ClearAdministrativeTitle();
        independent.SetKingdomType(KingdomType.default_country_post);
        independent.GetRegime()?.SetAllowDiplomacy(true);
        independent.GetRegime()?.SetAllowArmy(true);
        authority.GetOrCreate().last_stability_release = GameClock.Now;
        EventRecorder.Record(empire, string.Format(LM.Get("stability_release_history"), authority.GetKingdomFullName(),
            city.GetCityName(), state.unfunded_months, state.stability), independent.Ruler, independent);
    }

    public static string GarrisonText(City city)
    {
        var state = State(city);
        if (state == null) return "";
        Counts(city, out _, out int present);
        return string.Format(LM.Get("city_garrison_format"), present, state.garrison_required);
    }
#endif

    public static string CostText(CityStabilityData state) => string.Format(LM.Get("city_stability_cost_format"),
        state.governance_paid, state.governance_due, state.garrison_paid, state.garrison_due);
    public static string PolicyText(int policy) => LM.Get(policy < 0 ? "stability_policy_auto" :
        policy == 0 ? "stability_policy_none" : policy == 1 ? "stability_policy_standard" : "stability_policy_extra");
}
}
