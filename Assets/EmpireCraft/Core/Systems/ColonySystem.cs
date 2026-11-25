// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ColonySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 殖民体系(用户 2026-10-28："把殖民系统也给写了，以后还要加公司政体(东印度公司)、总督什么的""公司政体应该是研究了金融学以后才有的"；
// 问答定：殖民地是附属政权、海外城自动编入加主动殖民、公司政体一起做、研究远洋航海的都能殖民)：
//   · 殖民地自成一国(总督府或特许公司)，记着宗主国(KingdomExtraData.colony)；不是封建附庸(附庸隔海就放走)，也不入帝国；
//     宗主国与殖民地、同一宗主的殖民地之间不开战(见 Bound，接在 WarGate、CanDeclareExternalWar、WarReach、参战闸)，
//     殖民地随宗主出征、宗主保护殖民地(见 FeudalVassalService.ExpandSide)；地图上是自己与宗主的合成色；不再认别的宗主、不称帝、不入帝国；
//   · 编入：研究出远洋航海的国家，离都城够远的海外城(不在都城那座岛、相距 ColonyDistance 地块以上、不是半壁江山)每年按岛编成殖民地，
//     那座岛上已有本国殖民地的并进去；
//   · 主动殖民：每年有机会派殖民船去海路够得着的无主海岸(别的岛)，航行几个月后建城、迁去 Settlers 口人，编进殖民地；
//     研究出金融以后，派出的殖民船多半由特许公司经营；
//   · 总督府：总督由宗主国任免，任期 GovernorTermYears 年，出缺当场补派(穿越政体的宗主国派元老去当总督)；每年把经常收入的
//     RemitShare 解送宗主国；
//   · 特许公司：董事会选总督(同投票)，自己养兵、开战；国库超出储备的余钱每年一半分红给宗主国；做到 NationalizeCities 座城，
//     或亏空成债，就被宗主国收归国有、改设总督府；
//   · 离心：路远、立得久、宗主不如自己强、宗主在打仗就涨，满了起兵独立(复用"附庸独立"这类战争)，打赢就脱离，打输压回去。
// 属地(殖民地的延伸，用户 2026-11-10 "还有那种美属, 英属 这种属国前缀和属国机制要做""应该算殖民地的延申"
// "x属的x只取国名的第一个字""战争要求里面也可以把这几种属国类型加进去")：
//   · 国号"某属某地"：某是宗主国国名的头一个字(英属印度、法属印度支那、日属朝鲜)；地名剧本给的(place)或首府所在的法理名；
//     自治领叫"某地自治领"，特许公司照旧"宗主某地公司"；宗主改了国号，下一年随之改；
//   · 直辖殖民地(总督府)：宗主派总督、改殖民政体与宗主文化、解送三成；本族移民为主又成了规模的，离心满了不起兵，和平改为自治领；
//   · 保护国：本地君主与政体、文化照旧，外交归宗主(不自行宣战)，解送一成半，离心慢；
//   · 自治领：自有政体、自有元首，不解送，随宗主出征；满 DominionYears 年、离心满了和平取得完全独立；
//   · 委任统治地：宗主派总督托管(文化不改)，解送一成；满 MandateYears 年、离心过半又不在打仗，托管期满和平独立；
//   · 租借地：宗主派总督，解送三成；租期满了把城交还出租国(出租国亡了就留着)；
//   · 战争要求：战争目标"保护国""租借"，议和台"立为保护国""租借某城""移交殖民地(委任统治)"(见 WarGoalSystem、PeaceTable)。
public static class ColonySystem
{
    public const string OceanTech = "ocean_navigation", CompanyTech = "banking";
    private const int ColonyDistance = 36;            // 地块
    private const int GovernorTermYears = 6;
    private const float RemitShare = 0.3f, DividendShare = 0.5f;
    private const int NationalizeCities = 6;
    private const int VoyageCooldownYears = 8, VoyageCost = 600, Settlers = 300, VoyageMinCapital = 1500;
    private const float VoyageChance = 0.35f, CompanyChance = 0.6f;
    private const int RiseMinYears = 30, RiseCooldownYears = 10;
    private const int StartingGrant = 200;
    // 属地：解送几成；自治领几年后可和平独立；委任统治几年期满；租借默认几年；本族移民殖民地至少几城才改自治领
    private const float ProtectorateRemit = 0.15f, MandateRemit = 0.1f;
    private const int DominionYears = 30, MandateYears = 25, DominionMinCities = 3;
    public const int LeaseYears = 99;

    // 由宗主派总督的：总督府、委任统治地、租借地
    public static bool HasGovernor(int kind) => kind is ColonyState.Governorate or ColonyState.Mandate or ColonyState.Lease;

    // 改用宗主文化的：总督府、特许公司(殖民与移民)；保护国、自治领、委任统治、租借照本地文化
    private static bool TakesMetropoleCulture(int kind) => kind is ColonyState.Governorate or ColonyState.Company;

    // 近代殖民列强：能当宗主国(远洋航海)，又是近代国家(见 StateModernization.IsModernPower)；立保护国、租借港口的战争要求只有它们提
#if EC_PENDING
    public static bool ColonialPower(Realm kingdom) => CanColonize(kingdom) && StateModernization.IsModernPower(kingdom);
#endif

    // 可租的港口城：有船坞或临海、不是都城，挑户数最多的
#if EC_PENDING
    public static City LeaseTarget(Realm kingdom) =>
        kingdom?.Cities?.Where(city => city?.Record != null && !city.IsGone() && city != kingdom.Capital && Port(city))
            .OrderByDescending(PeaceTermRules.CityValue).FirstOrDefault();

    public static bool Port(City city)
    {
        if (city?.Record == null || city.IsGone()) return false;
        if (city.getBuildingOfType("type_docks", pCountOnlyFinished: true, pRandom: false) != null) return true;
        // 临不临海要逐格看，内陆大城上万格：同一座城、地块数没变的一年内照旧(议和台每场仗每年给每座城问一遍)
        int count = city.Zones?.Count ?? 0, year = GameClock.Year;
        if (PortCache.TryGetValue(city.Key, out (City city, int zones, int year, bool port) hit) &&
            ReferenceEquals(hit.city, city) && hit.zones == count && hit.year == year)
            return hit.port;
        bool port = false;
        if (city.Zones != null)
            foreach (Cell zone in city.Zones)
            {
                foreach (Cell tile in zone?.Tiles ?? Array.Empty<Cell>())
                    if (tile?.Type?.Ocean == true)
                    {
                        port = true;
                        break;
                    }
                if (port) break;
            }
        if (PortCache.Count > 4096) PortCache.Clear();
        PortCache[city.Key] = (city, count, year, port);
        return port;
    }
#endif

    private static readonly Dictionary<long, (City city, int zones, int year, bool port)> PortCache = new();

    // 战争收场时的属地安排(立保护国、租借、移交殖民地)：过几帧再做，不在原版结束战争的同一帧里改归属、关系
    private static readonly Queue<Action> Deferred = new();

    public static void Defer(Action action)
    {
        if (action != null) Deferred.Enqueue(action);
    }

    // 外交归宗主、不自行宣战的：保护国、委任统治地、租借地(见 WarGate)
    public static bool ForeignAffairsByMetropole(Realm kingdom) =>
        Of(kingdom)?.kind is ColonyState.Protectorate or ColonyState.Mandate or ColonyState.Lease && MetropoleOf(kingdom) != null;

    // 建殖民城时挡住建城后的钩子(见 CityPatch.CityBuilt)；殖民地起兵独立时放行开战闸(见 WarReach.MayDeclare)
    public static bool Founding { get; private set; }
    [ThreadStatic] private static bool _rising;
    public static bool Rising => _rising;

    #region 关系

    public static ColonyState Of(Realm kingdom) => kingdom?.Record == null || kingdom.IsGone() ? null : kingdom.GetOrCreate().colony;

    public static Realm MetropoleOf(Realm kingdom)
    {
        ColonyState state = Of(kingdom);
        if (state == null || state.metropole_id < 0 || EmpireWorld.Current?.LiveRealms == null) return null;
        Realm metropole = EmpireWorld.Current.RealmByKey(state.metropole_id);
        return metropole != null && metropole != kingdom && !metropole.IsGone() ? metropole : null;
    }

    public static bool IsColony(Realm kingdom) => MetropoleOf(kingdom) != null;

    public static IEnumerable<Realm> ColoniesOf(Realm metropole) =>
        metropole == null || EmpireWorld.Current?.LiveRealms == null ? Enumerable.Empty<Realm>()
            : EmpireWorld.Current.LiveRealms.Where(kingdom => kingdom != null && MetropoleOf(kingdom) == metropole);

    // 宗主与殖民地、同一宗主的两块殖民地：互不开战
    public static bool Bound(Realm a, Realm b)
    {
        if (a == null || b == null || a == b) return false;
        Realm ma = MetropoleOf(a), mb = MetropoleOf(b);
        return ma == b || mb == a || ma != null && ma == mb;
    }

    // 这场仗是不是殖民地起兵独立(攻方是殖民地、守方是它的宗主)
    public static bool IsIndependenceWar(War war) =>
        war?.Record != null && MetropoleOf(war.GetMainAttacker()) is Realm metropole && metropole == war.GetMainDefender();

    // 参战闸：不站到自己宗主、自己殖民地的对面(殖民地自己的独立战争除外)
#if EC_PENDING
    public static bool JoinsAgainstKin(War war, Realm kingdom, bool attackers)
    {
        if (war?.Record == null || kingdom == null || IsIndependenceWar(war)) return false;
        IEnumerable<long> opposite = attackers ? war.Record.list_defenders : war.Record.list_attackers;
        if (opposite == null) return false;
        foreach (long id in opposite)
        {
            Realm other = EmpireWorld.Current.RealmByKey(id);
            if (other != null && Bound(kingdom, other)) return true;
        }
        return false;
    }
#endif

    #endregion

    #region 年度

    private static int _year = int.MinValue;
    private static object _world;
    private static readonly Queue<Realm> Pending = new();
    private const int PerFrame = 6;

#if EC_PENDING
    public static void Tick()
    {
        if (EmpireWorld.Current == null) return;
        int year = GameClock.Year;
        if (!ReferenceEquals(_world, EmpireWorld.Current))
        {
            _world = EmpireWorld.Current;
            _year = year;
            Pending.Clear();
            Deferred.Clear();
            return;
        }
        if (Deferred.Count > 0)
            try
            {
                Deferred.Dequeue()();
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][殖民] 属地安排失败: {exception.Message}");
            }
        if (year != _year)
        {
            _year = year;
            foreach (Realm kingdom in EmpireWorld.Current.LiveRealms) Pending.Enqueue(kingdom);
        }
        for (int i = 0; i < PerFrame && Pending.Count > 0; i++)
        {
            Realm kingdom = Pending.Dequeue();
            if (kingdom?.Record == null || kingdom.IsGone()) continue;
            try
            {
                // 旧档里互为上下家的每年核一遍(见 SubjectChain)
                SubjectChain.Untangle(kingdom);
                if (Of(kingdom) != null) Colony(kingdom);
                else
                {
                    Incorporate(kingdom);
                    Voyage(kingdom);
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][殖民] {kingdom.GetKingdomName()} 年度结算失败: {exception.Message}");
            }
        }
    }
#endif

    // 能当宗主国：独立国或帝国核心、不是别人的殖民地、本国采纳了远洋航海
#if EC_PENDING
    private static bool CanColonize(Realm kingdom) =>
        kingdom?.Capital != null && !kingdom.IsGone() && kingdom.IsCivilized() && !IsColony(kingdom) &&
        (!kingdom.IsInEmpire() || kingdom.IsEmpire()) && TechnologySystem.RealmHasTech(kingdom, OceanTech);

    private static object IslandOf(City city) => city?.SeatCell()?.region?.island;
#endif

    // 离都城够远的海外：通达层算好后(用户 2026-11-06 同意"先信息类、再贸易迁徙"，见 ConnectivitySystem)按人员从都城走海路过去的成本，
    // 不低于 colony_min_cost(20，约远洋帆船三百格)或走不到才算；没算好时照旧按地块距离 ColonyDistance
#if EC_PENDING
    private static bool FarOverseas(City city, City capital)
    {
        if (ConnectivitySystem.Ready)
        {
            float cost = ConnectivitySystem.Cost(capital, city, ConnectivityMode.People);
            if (cost >= 0f) return cost >= ConnectivitySystem.Config.colony_min_cost;
        }
        return Distance(city, capital) >= ColonyDistance;
    }
#endif

    private static int Distance(City a, City b)
    {
        Cell za = a?.SeatCell()?.Zone, zb = b?.SeatCell()?.Zone;
        return za == null || zb == null ? 0 : Mathf.Abs(za.X - zb.X) + Mathf.Abs(za.Y - zb.Y);
    }

    #endregion

    #region 编入与设立

#if EC_PENDING
    private static void Incorporate(Realm realm)
    {
        if (!CanColonize(realm)) return;
        object home = IslandOf(realm.Capital);
        if (home == null) return;
        int homeCount = realm.Cities.Count(city => IslandOf(city) == home);
        // 海外看行政控制力算出的"必须走海路才连得上"(见 AdministrativeControlSystem)：原版的岛会被山脉、高峰切开，
        // 同一块大陆上隔着山的城不能当成海外
        var groups = realm.Cities
            .Where(city => city != null && !city.IsGone() && city != realm.Capital && IslandOf(city) is object island &&
                           island != home && AdministrativeControlSystem.Known(city) && city.GetOrCreate().admin_overseas &&
                           FarOverseas(city, realm.Capital))
            .GroupBy(IslandOf).ToList();
        foreach (var group in groups)
        {
            // 那边才是大半壁江山的不算殖民地
            List<City> cities = group.OrderByDescending(city => city.PopulationCount()).ToList();
            if (cities.Count > 1 && cities.Count * 2 > homeCount) continue;
            Realm existing = ColoniesOf(realm).FirstOrDefault(colony => IslandOf(colony.Capital) == group.Key);
            if (existing != null)
            {
                foreach (City city in cities) city.JoinRealm(existing);
                continue;
            }
            Found(realm, cities, ColonyState.Governorate);
        }
    }
#endif

    // 剧本开局直接设的殖民地(世界地图：荷兰东印度公司、菲律宾总督府……)；给了名字的作地名，国号照"某属某地"、公司
#if EC_PENDING
    public static Realm Establish(Realm metropole, List<City> cities, bool company, string name)
    {
        Realm colony = Found(metropole, cities, company ? ColonyState.Company : ColonyState.Governorate);
        if (colony != null && !string.IsNullOrWhiteSpace(name) && Of(colony) is ColonyState state)
        {
            state.place = name;
            Name(colony, metropole, state.kind);
        }
        return colony;
    }
#endif

    // 外交界面(见 DiplomacyActions)：把现成的一国设为殖民地；立即改派总督；特许公司立即收归国有
#if EC_PENDING
    public static void Annex(Realm metropole, Realm kingdom, bool company)
    {
        // 宗主正从属于它的：先翻身(见 SubjectChain.Overturn)，解不开就不设
        if (!SubjectChain.Overturn(metropole, kingdom)) return;
        Setup(kingdom, metropole, company ? ColonyState.Company : ColonyState.Governorate);
    }
#endif

    // 把现成的一国设为某种属地(剧本 colony_of、战争要求)；place 地名("简体|英文|繁体"，空的取首府法理名)
#if EC_PENDING
    public static string Annex(Realm metropole, Realm kingdom, int kind, string place, Realm lessor = null, int leaseYears = 0)
    {
        if (kingdom?.Record == null || metropole?.Record == null || kingdom == metropole) return null;
        if (!SubjectChain.Overturn(metropole, kingdom)) return null;
        if (Of(kingdom) != null) kingdom.GetOrCreate().colony = null;
        string result = Setup(kingdom, metropole, kind, place);
        if (kind == ColonyState.Lease && Of(kingdom) is ColonyState state)
        {
            state.lessor_id = lessor?.Key ?? -1L;
            state.lease_until = GameClock.Now + Math.Max(1, leaseYears > 0 ? leaseYears : LeaseYears) * 60d;
        }
        return result;
    }
#endif

    // 战败一方立为保护国(战争目标"保护国"、议和"立为保护国")：本地君主照旧，外交归宗主
#if EC_PENDING
    public static bool MakeProtectorate(Realm metropole, Realm kingdom)
    {
        if (kingdom?.Record == null || metropole?.Record == null || kingdom.IsGone() || metropole.IsGone() || kingdom == metropole) return false;
        if (kingdom.IsEmpire() && kingdom.GetEmpire()?.kingdoms_list?.Count > 1) return false;
        if (WarlordEraSystem.Dependent(kingdom)) return false;
        // 打赢了自己的上家：先翻身(见 SubjectChain.Overturn)，解不开就不立
        if (!SubjectChain.Overturn(metropole, kingdom)) return false;
        // 立为保护国(战果、议和)：不许自立的闸放行(见 SecessionGate)
        if (kingdom.IsInEmpire() && !kingdom.IsEmpire()) SecessionGate.Sanctioned(() => kingdom.GetEmpire()?.leave(kingdom, true, true));
        DiplomacyActions.EndWars(metropole, kingdom);
        Annex(metropole, kingdom, ColonyState.Protectorate, "");
        TranslateHelper.LogEventMessage(string.Format(LM.Get("colony_protectorate_imposed"), metropole.GetKingdomFullName(),
            kingdom.GetKingdomFullName()), metropole);
        return true;
    }
#endif

    // 租借(战争目标"租借"、议和"租借某城")：出租国的这些城另设租借地，归 lessee，租期满交还
#if EC_PENDING
    public static Realm Lease(Realm lessee, Realm lessor, List<City> cities, int years = LeaseYears)
    {
        cities = cities?.Where(city => city?.Record != null && !city.IsGone() && city.Realm == lessor && city != lessor.Capital).ToList();
        if (lessee?.Record == null || lessor?.Record == null || cities == null || cities.Count == 0) return null;
        Realm lease = AdministrativeControlSystem.ReleaseToLocal(lessor, cities,
            colony => Setup(colony, lessee, ColonyState.Lease, ""), out _);
        if (lease != null && Of(lease) is ColonyState state)
        {
            state.lessor_id = lessor.Key;
            state.lease_until = GameClock.Now + Math.Max(1, years) * 60d;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("colony_lease_signed"), lessor.GetKingdomFullName(),
                string.Join(LM.Get("list_separator"), cities.Select(city => city.GetCityName())), lessee.GetKingdomFullName(), years), lessee);
        }
        return lease;
    }
#endif

    // 战败一方的殖民地移交战胜国、改为委任统治地(议和"移交殖民地")
#if EC_PENDING
    public static bool TransferAsMandate(Realm colony, Realm receiver)
    {
        if (colony?.Record == null || receiver?.Record == null || colony.IsGone() || receiver.IsGone() || Of(colony) is not ColonyState old) return false;
        string place = old.place;
        Realm former = MetropoleOf(colony);
        colony.GetOrCreate().colony = null;
        if (former != null) DiplomacyActions.EndWars(former, colony);
        Setup(colony, receiver, ColonyState.Mandate, place);
        return true;
    }

    public static bool RotateGovernor(Realm colony)
    {
        ColonyState state = Of(colony);
        Realm metropole = MetropoleOf(colony);
        if (state == null || metropole == null || state.kind != ColonyState.Governorate) return false;
        Person next = AppointGovernor(colony, metropole);
        if (next == null) return false;
        string former = colony.Ruler?.FullName() ?? "";
        TransmigrantSystem.InstallRuler(colony, next);
        state.governor_id = next.Key;
        state.governor_since = GameClock.Now;
        TranslateHelper.LogEventOnly(string.Format(LM.Get("colony_governor_rotated"), colony.GetKingdomFullName(),
            next.FullName(), former), metropole);
        return true;
    }

    public static bool NationalizeNow(Realm colony)
    {
        ColonyState state = Of(colony);
        Realm metropole = MetropoleOf(colony);
        if (state == null || metropole == null || state.kind != ColonyState.Company) return false;
        Nationalize(colony, metropole, state);
        return true;
    }
#endif

    // 一组城拆出去立为殖民地(总督府或特许公司)
#if EC_PENDING
    private static Realm Found(Realm metropole, List<City> cities, int kind) =>
        AdministrativeControlSystem.ReleaseToLocal(metropole, cities, colony => Setup(colony, metropole, kind), out _);

    private static string Setup(Realm colony, Realm metropole, int kind, string place = "")
    {
        double now = GameClock.Now;
        var state = new ColonyState { metropole_id = metropole.Key, kind = kind, founded_at = now, remitted_at = now, place = place ?? "" };
        colony.GetOrCreate().colony = state;
        string culture = CultureService.GetRealmCulture(metropole);
        if (TakesMetropoleCulture(kind) && CultureService.IsValidCulture(culture))
            CultureService.SetRealmCulture(colony, culture, updatePoliticalSystem: false, allowLegacyInstitutions: false);
        // 保护国、自治领留着本地的政体与君主
        if (HasGovernor(kind) || kind == ColonyState.Company) ApplyRegime(colony, kind);
        Name(colony, metropole, kind);
        VassalColorService.Apply(colony, metropole);
        if (metropole.GetMoney() > StartingGrant * 2)
        {
            metropole.SubMoney(StartingGrant, TreasuryCategory.Construction);
            colony.AddMoney(StartingGrant, TreasuryCategory.InternalTransfer);
        }
        string ruler;
        if (HasGovernor(kind))
        {
            Person governor = AppointGovernor(colony, metropole);
            TransmigrantSystem.InstallRuler(colony, governor);
            state.governor_id = governor?.Key ?? -1L;
            state.governor_since = now;
            ruler = governor?.FullName() ?? "";
        }
        else ruler = colony.Ruler?.FullName() ?? "";
        TranslateHelper.LogEventMessage(string.Format(LM.Get(kind == ColonyState.Company ? "colony_company_founded" : "colony_founded"),
            metropole.GetKingdomFullName(), colony.GetKingdomFullName(), ruler), metropole);
        return "colony";
    }

    private static void ApplyRegime(Realm colony, int kind)
    {
        if (!HasGovernor(kind) && kind != ColonyState.Company) return;
        colony.SetRegimeType(kind == ColonyState.Company ? RegimeType.Company : RegimeType.Colonial);
        colony.LoadRegime();
        colony.GetOrCreate().regime_manually_selected = true;
    }
#endif

    // 国号：特许公司叫"<宗主国><首府>公司"；自治领"<地名>自治领"；别的"<宗主国名头一个字>属<地名>"，
    // 委任统治地、租借地另加后缀(英属巴勒斯坦托管地、日属关东州租借地)
#if EC_PENDING
    private static void Name(Realm colony, Realm metropole, int kind)
    {
        if (kind == ColonyState.Company)
        {
            string seat = colony.Capital?.GetCityName() ?? colony.GetKingdomName();
            string core = colony.Capital?.GetOrCreate().core_name;
            if (!string.IsNullOrEmpty(core)) seat = core;
            colony.SetCustomCountryName(SovereignName(metropole) + seat);
            colony.SetCustomCountrySuffix(LM.Get("colony_company_suffix"));
            return;
        }
        string place = Place(colony, Of(colony));
        if (kind == ColonyState.Dominion)
        {
            colony.SetCustomCountryName(place);
            colony.SetCustomCountrySuffix(LM.Get("colony_dominion_suffix"));
            return;
        }
        colony.SetCustomCountryName(string.Format(LM.Get("colony_prefix_name"), Abbrev(metropole), place, SovereignName(metropole)));
        colony.SetCustomCountrySuffix(kind switch
        {
            ColonyState.Mandate => LM.Get("colony_mandate_suffix"),
            ColonyState.Lease => LM.Get("colony_lease_suffix"),
            _ => ""
        });
    }
#endif

    // 宗主国名的头一个字(英国 → 英、荷兰 → 荷)；美号"大"(大日本、大清)不算。
    // 宗主自己也是属地的按它的地名取(用户 2026-11-13 转群友截图"彭属彭其实是彭属莱属彭"：彭的属地"彭属莱"又收了彭作属地，
    // 拿"彭属莱"取头一个字，成了"彭属彭"；应是"莱属彭")
#if EC_PENDING
    public static string Abbrev(Realm metropole)
    {
        // 剧本给了简称的照简称(大不列颠帝国→英、大日本帝国→日：国号头一个字不一定是简称)
        string custom = metropole?.Record == null ? null : metropole.GetOrCreate().country_abbrev;
        if (!string.IsNullOrWhiteSpace(custom)) return WorldMapData.PickParts(custom);
        string name = SovereignName(metropole);
        string grand = RealmNamingSystem.GrandPrefix;
        if (!string.IsNullOrEmpty(grand) && name.Length > grand.Length && name.StartsWith(grand, StringComparison.Ordinal) &&
            metropole.GetEmpire()?.data?.directPre == grand)
            name = name.Substring(grand.Length);
        global::System.Globalization.TextElementEnumerator elements = global::System.Globalization.StringInfo.GetTextElementEnumerator(name);
        return elements.MoveNext() ? elements.GetTextElement() : "";
    }
#endif

    // 给法理、帝国起名时借用的国名：属地不带"某属"名号、用地名(用户 2026-11-13 转群友"称帝后宗主灭亡了也不把国号改回正常"：
    // 属地期间新立的法理借了当时的国名，就叫"某属某地"，帝号又照法理名定，脱离宗主以后清掉的只是属地名号，这两处照旧)；别的照国名
#if EC_PENDING
    public static string SovereignName(Realm kingdom) =>
        kingdom?.Record == null ? "" : Of(kingdom) is ColonyState state ? Place(kingdom, state) : kingdom.GetKingdomName();
#endif

    // 脱离宗主时把写死成属地名号的名字改回地名：本国主法理、首府所在的法理、帝号(核心名)。
    // 只改名字正是那个名号的，或"某属"打头的；别的名字不动
#if EC_PENDING
    private static void Unbrand(Realm kingdom, string branded, string place)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || string.IsNullOrWhiteSpace(place)) return;
        string Plain(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (!string.IsNullOrWhiteSpace(branded) && string.Equals(name, branded, StringComparison.Ordinal))
                return string.Equals(place, name, StringComparison.Ordinal) ? null : place;
            string stripped = StripBrand(name);
            return stripped != name ? stripped : null;
        }
        try
        {
            var renamed = new List<string>();
            foreach (var title in new[] { kingdom.GetMainTitle(), kingdom.Capital?.GetTitle() })
            {
                if (title?.data == null || title.IsGone() || Plain(title.data.name) is not string plain) continue;
                renamed.Add(title.data.name + "→" + plain);
                title.data.name = plain;
            }
            var empire = kingdom.IsEmpire() ? kingdom.GetEmpire() : null;
            if (empire?.data != null && !empire.IsGone() && empire.CoreKingdom == kingdom && Plain(empire.data.core_name) is string imperial)
            {
                renamed.Add(empire.data.core_name + "→" + imperial);
                empire.SetEmpireName(imperial, preserveEmpireType: true);
            }
            else if (empire == null && Plain(kingdom.GetOrCreate().core_name) is string core)
            {
                renamed.Add(kingdom.GetOrCreate().core_name + "→" + core);
                kingdom.SetKingdomCoreName(core);
            }
            if (renamed.Count > 0) LogService.LogInfo("[EmpireCraft][殖民] 脱离宗主，名号改回：" + string.Join("、", renamed));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][殖民] 名号改回失败: {exception.Message}");
        }
    }
#endif

    // 地名：剧本给的、或首府所在的王国法理名，都没有用首府城名
#if EC_PENDING
    private static string Place(Realm colony, ColonyState state)
    {
        // 剧本给的地名本身带"某属"的(葡属印度)：换了宗主照样去掉，叫"法属印度"，不叫"法属葡属印度"
        if (!string.IsNullOrWhiteSpace(state?.place)) return StripBrand(WorldMapData.PickParts(state.place));
        // 法理按"英属马来亚"这样的国名立的：去掉"某属"(以前暹罗收它作保护国叫"暹属英属马来亚")
        string title = StripBrand(colony.Capital?.GetTitle()?.data?.name);
        if (!string.IsNullOrWhiteSpace(title)) return title;
        string seat = colony.Capital?.GetCityName();
        if (!string.IsNullOrWhiteSpace(seat)) return seat;
        // 没有都城(也就没有法理名、城名)：只剩本国的国名可用。国名这时多半已经是"某属某地"，每年重新起名(见 Yearly)都拿它再套
        // 一层，就成了"吕属吕属吕属……"越叠越长(独立核心里没都城的国多，2026-11-17 长跑时见到)。头一回把"某属"去干净记下，往后照用
        if (state != null && !string.IsNullOrWhiteSpace(state.own_name)) return state.own_name;
        string own = StripBrand(colony.GetKingdomName());
        if (state != null && !string.IsNullOrWhiteSpace(own)) state.own_name = own;
        return own ?? "";
    }
#endif

    // 去掉名字前头的"某属"，叠了几层去几层("吕属吕属莱" → "莱")；去完是空的照原样还回去。
    // "某"按简称算，至多两个字(见 Abbrev)；英文的国号是"地名 (宗主)"，没有这个字，不动
    internal static string StripBrand(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        string rest = name;
        while (true)
        {
            int at = rest.IndexOfAny(BrandMarks);
            if (at < 1 || at > 2 || at + 1 >= rest.Length) break;
            rest = rest.Substring(at + 1);
        }
        return rest;
    }

    private static readonly char[] BrandMarks = { '属', '屬' };

    #endregion

    #region 总督

    // 总督府出缺(见 OfficeSelector.Select)：宗主国派人；公司由董事会照投票选，这里不管
#if EC_PENDING
    public static Person PickGovernor(Realm colony)
    {
        ColonyState state = Of(colony);
        Realm metropole = MetropoleOf(colony);
        if (state == null || metropole == null || !HasGovernor(state.kind)) return null;
        Person governor = AppointGovernor(colony, metropole);
        if (governor == null) return null;
        state.governor_id = governor.Key;
        state.governor_since = GameClock.Now;
        return governor;
    }

    private static Person AppointGovernor(Realm colony, Realm metropole)
    {
        City seat = colony.Capital ?? colony.Cities.FirstOrDefault();
        if (seat == null) return null;
        Person governor = TransmigrantSystem.IsTransmigrant(metropole) ? TransmigrantSystem.RealizeSenator(metropole, seat) : null;
        governor ??= CityPopulationSystem.SpawnPerson(seat, metropole.getSpecies(), CultureService.GetRealmCulture(metropole));
        governor?.RecordPersonalHistory(string.Format(LM.Get("colony_governor_history"), metropole.GetKingdomFullName(),
            colony.GetKingdomFullName()));
        return governor;
    }
#endif

    #endregion

    #region 殖民地的年度：任期、解送、分红、收归国有、离心独立

#if EC_PENDING
    private static void Colony(Realm colony)
    {
        ColonyState state = Of(colony);
        Realm metropole = MetropoleOf(colony);
        if (metropole == null)
        {
            // 宗主亡了：殖民地就此独立
            Release(colony, string.Format(LM.Get("colony_orphaned"), colony.GetKingdomFullName()));
            return;
        }
        double now = GameClock.Now;
        VassalColorService.Apply(colony, metropole);
        // 宗主改了国号："某属"跟着改(公司照旧)
        if (state.kind != ColonyState.Company) Name(colony, metropole, state.kind);
        // 租借地：租期满交还出租国
        if (state.kind == ColonyState.Lease && state.lease_until >= 0d && now >= state.lease_until)
        {
            ReturnLease(colony, metropole, state);
            return;
        }
        if (HasGovernor(state.kind))
        {
            if (state.governor_since >= 0d && now - state.governor_since >= GovernorTermYears * 60d && !colony.HasEnemies())
            {
                Person next = AppointGovernor(colony, metropole);
                if (next != null)
                {
                    string former = colony.Ruler?.FullName() ?? "";
                    TransmigrantSystem.InstallRuler(colony, next);
                    state.governor_id = next.Key;
                    state.governor_since = now;
                    TranslateHelper.LogEventOnly(string.Format(LM.Get("colony_governor_rotated"), colony.GetKingdomFullName(),
                        next.FullName(), former), metropole);
                }
            }
            Remit(colony, metropole, state.kind == ColonyState.Mandate ? MandateRemit : RemitShare);
        }
        else if (state.kind == ColonyState.Protectorate) Remit(colony, metropole, ProtectorateRemit);
        else if (state.kind == ColonyState.Dominion) { }
        else
        {
            long surplus = TreasuryHoardSystem.Surplus(colony);
            int dividend = (int)Math.Min(int.MaxValue, (long)(surplus * DividendShare));
            if (dividend > 0)
            {
                colony.SubMoney(dividend, TreasuryCategory.Tribute);
                metropole.AddMoney(dividend, TreasuryCategory.Tribute);
                state.dividends += dividend;
            }
            if (colony.Cities.Count >= NationalizeCities || colony.GetTreasuryBalance() < 0 && now - state.founded_at >= 10 * 60d)
            {
                Nationalize(colony, metropole, state);
                return;
            }
        }
        state.remitted_at = now;
        Drift(colony, metropole, state, now);
    }
#endif

    // 把经常收入的几成解送宗主国
    private static void Remit(Realm colony, Realm metropole, float share)
    {
        int remit = Mathf.Min(Mathf.Max(0, colony.GetMoney()), (int)(StateDebtSystem.AnnualStableIncome(colony) * share));
        if (remit <= 0) return;
        colony.SubMoney(remit, TreasuryCategory.Tribute);
        metropole.AddMoney(remit, TreasuryCategory.Tribute);
    }

    // 租期届满：城交还出租国(出租国亡了，租借地就此留给宗主，改为直辖殖民地)
#if EC_PENDING
    private static void ReturnLease(Realm colony, Realm metropole, ColonyState state)
    {
        Realm lessor = state.lessor_id < 0 ? null : EmpireWorld.Current.RealmByKey(state.lessor_id);
        if (lessor?.Record == null || lessor.IsGone())
        {
            state.kind = ColonyState.Governorate;
            state.lease_until = -1d;
            Name(colony, metropole, state.kind);
            return;
        }
        string name = colony.GetKingdomFullName();
        foreach (City city in colony.Cities.ToList()) city.JoinRealm(lessor);
        colony.GetOrCreate().colony = null;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("colony_lease_returned"), name, lessor.GetKingdomFullName()), lessor);
    }
#endif

    // 特许公司收归国有，改设总督府
#if EC_PENDING
    private static void Nationalize(Realm colony, Realm metropole, ColonyState state)
    {
        string before = colony.GetKingdomFullName();
        state.kind = ColonyState.Governorate;
        ApplyRegime(colony, state.kind);
        Name(colony, metropole, state.kind);
        Person governor = AppointGovernor(colony, metropole);
        TransmigrantSystem.InstallRuler(colony, governor);
        state.governor_id = governor?.Key ?? -1L;
        state.governor_since = GameClock.Now;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("colony_nationalized"), metropole.GetKingdomFullName(), before,
            colony.GetKingdomFullName()), metropole);
    }

    private static void Drift(Realm colony, Realm metropole, ColonyState state, double now)
    {
        float gain = 2f + Mathf.Min(4f, Distance(colony.Capital, metropole.Capital) / 40f);
        double own = colony.GetNationalPower(), lord = metropole.GetNationalPower();
        if (lord < own * 1.5d) gain += 4f;
        if (metropole.HasEnemies()) gain += 3f;
        // 去殖民浪潮里各地离心大涨(见 DecolonizationSystem)
        gain += DecolonizationSystem.DriftBonus;
        gain *= state.kind switch
        {
            ColonyState.Company => 0.7f,
            ColonyState.Protectorate => 0.6f,
            ColonyState.Lease => 0.5f,
            ColonyState.Dominion => 1.2f,
            _ => 1f
        };
        state.autonomy = Mathf.Clamp(state.autonomy + gain, 0f, 100f);
        float years = (float)((now - state.founded_at) / 60d);
        // 自治领：立满 DominionYears 年、离心满了，和平取得完全独立
        if (state.kind == ColonyState.Dominion)
        {
            if (state.autonomy >= 100f && years >= DominionYears)
                Release(colony, string.Format(LM.Get("colony_dominion_independent"), colony.GetKingdomFullName(), metropole.GetKingdomFullName()),
                    friendly: metropole);
            return;
        }
        // 委任统治：托管满 MandateYears 年、离心过半又不在打仗，期满独立
        if (state.kind == ColonyState.Mandate && years >= MandateYears && state.autonomy >= 50f && !colony.HasEnemies() && !metropole.HasEnemies())
        {
            Release(colony, string.Format(LM.Get("colony_mandate_ended"), colony.GetKingdomFullName(), metropole.GetKingdomFullName()),
                friendly: metropole);
            return;
        }
        // 本族移民为主、成了规模的直辖殖民地：离心满了不起兵，和平改为自治领(近代以后)
        if (state.kind == ColonyState.Governorate && state.autonomy >= 100f && colony.Cities.Count >= DominionMinCities &&
            StateModernization.IsModernPower(metropole) && colony.Capital != null &&
            CultureService.SameKin(CultureService.GetMainCulture(colony.Capital, initialize: false), CultureService.GetRealmCulture(metropole)))
        {
            BecomeDominion(colony, metropole, state);
            return;
        }
        if (state.autonomy < 100f || now - state.founded_at < RiseMinYears * 60d || colony.HasEnemies() || !colony.HasRuler() ||
            state.rising_at >= 0d && now - state.rising_at < RiseCooldownYears * 60d) return;
        // 宗主强过自己三倍(去殖民浪潮里六倍)又不在打仗的，不敢起兵
        if (lord > own * DecolonizationSystem.DareRatio && !metropole.HasEnemies()) return;
        War war;
        _rising = true;
        try
        {
            war = EmpireWorld.Current.StartWar(colony, metropole, WarKinds.Normal);
        }
        finally
        {
            _rising = false;
        }
        if (war == null) return;
        war.SetEmpireWarType(EmpireWarType.附庸独立);
        state.rising_at = now;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("colony_rising"), colony.GetKingdomFullName(), metropole.GetKingdomFullName()),
            metropole);
    }
#endif

    // 直辖殖民地和平改为自治领：总督卸任，本地推出自己的元首，政体照本地文化定
#if EC_PENDING
    private static void BecomeDominion(Realm colony, Realm metropole, ColonyState state)
    {
        state.kind = ColonyState.Dominion;
        state.autonomy = 40f;
        state.founded_at = GameClock.Now;
        state.governor_id = -1L;
        colony.GetOrCreate().regime_manually_selected = false;
        string culture = CultureService.GetRealmCulture(colony);
        if (CultureService.IsValidCulture(culture))
            CultureService.SetRealmCulture(colony, culture, updatePoliticalSystem: true, allowLegacyInstitutions: false);
        Name(colony, metropole, state.kind);
        TranslateHelper.LogEventMessage(string.Format(LM.Get("colony_became_dominion"), colony.GetKingdomFullName(), metropole.GetKingdomFullName()),
            metropole);
    }
#endif

    // 独立战争结束(见 FeudalVassalService.ResolveIndependenceWar)：殖民地胜则脱离，败则离心压回去
#if EC_PENDING
    public static void ResolveIndependenceWar(War war, WarOutcome winner)
    {
        if (!IsIndependenceWar(war)) return;
        Realm colony = war.GetMainAttacker();
        if (winner == WarOutcome.Attackers) Release(colony, string.Format(LM.Get("colony_independent"), colony.GetKingdomFullName(),
            war.GetMainDefender().GetKingdomFullName()));
        else if (Of(colony) is ColonyState state) state.autonomy = 30f;
    }
#endif

    // 殖民地脱离宗主：按本文化重定政体、去掉殖民地名号与合成色；friendly 和平独立的(自治领、委任统治期满)与旧宗主交好
#if EC_PENDING
    public static void Release(Realm colony, string reason, Realm friendly = null)
    {
        if (colony?.Record == null || Of(colony) == null) return;
        int kind = Of(colony).kind;
        // 写死成属地名号的法理名、帝号一会儿改回地名(见 Unbrand)：名号与地名先记下
        string branded = colony.GetCustomCountryShortName(), place = Place(colony, Of(colony));
        colony.GetOrCreate().colony = null;
        colony.GetOrCreate().regime_manually_selected = false;
        string culture = CultureService.GetRealmCulture(colony);
        if (CultureService.IsValidCulture(culture))
            CultureService.SetRealmCulture(colony, culture, updatePoliticalSystem: true, allowLegacyInstitutions: false);
        colony.SetCustomCountryName("");
        colony.SetCustomCountrySuffix("");
        Unbrand(colony, branded, place);
        VassalColorService.Clear(colony);
        if (friendly?.Record != null && !friendly.IsGone()) DiplomacyActions.ApplyScenario("friend", colony, friendly, value: 40);
        if (!string.IsNullOrEmpty(reason)) TranslateHelper.LogEventMessage(reason, colony);
    }
#endif

    #endregion

    #region 主动殖民

#if EC_PENDING
    private static void Voyage(Realm realm)
    {
        if (!CanColonize(realm)) return;
        var data = realm.GetOrCreate();
        double now = GameClock.Now;
        if (data.colonial_voyage_zone >= 0)
        {
            if (now >= data.colonial_voyage_arrive) Arrive(realm, data);
            return;
        }
        if (data.colonial_voyage_at >= 0d && now < data.colonial_voyage_at) return;
        if (realm.HasEnemies() || realm.GetMoney() < VoyageCost * 2 ||
            CityPopulationSystem.GetBackgroundTotal(realm.Capital) < VoyageMinCapital || EcRandom.value >= VoyageChance) return;
        Cell target = FindSite(realm, out int distance);
        data.colonial_voyage_at = now + VoyageCooldownYears * 60d;
        if (target == null) return;
        realm.SubMoney(VoyageCost, TreasuryCategory.Construction);
        data.colonial_voyage_zone = target.Index;
        data.colonial_voyage_arrive = now + (6 + distance / 40) * 5d;
        TranslateHelper.LogEventOnly(string.Format(LM.Get("colony_voyage_departed"), realm.GetKingdomFullName(),
            realm.Capital.GetCityName()), realm);
    }
#endif

    // 海路够得着(本国港口出发、不穿敌方制海、在航程以内)的别的岛上的无主海岸，按地力挑
#if EC_PENDING
    private static Cell FindSite(Realm realm, out int distance)
    {
        distance = 0;
        Dictionary<int, int> reach = SeaLanding.Reachable(realm);
        if (reach == null || reach.Count == 0) return null;
        int range = NavySystem.MaxRange(realm);
        object home = IslandOf(realm.Capital);
        Cell capital = realm.Capital.SeatCell()?.Zone;
        var options = new List<(Cell zone, float score, int distance)>();
        foreach (Cell zone in EmpireWorld.Current.Zones.All)
        {
            if (zone == null || zone.City != null || zone.GroundTiles < 48 || !zone.canStartCityHere()) continue;
            if (zone.Center?.region?.island == home) continue;
            int far = capital == null ? 0 : Mathf.Abs(zone.X - capital.X) + Mathf.Abs(zone.Y - capital.Y);
            if (far < ColonyDistance / 2) continue;
            bool reachable = false;
            foreach (SeaRegionSystem.Region water in SeaLanding.Waters(zone))
                if (reach.TryGetValue(water.Id, out int hops) && hops <= range)
                {
                    reachable = true;
                    break;
                }
            if (!reachable || zone.NeighboursAll.Any(next => next?.City != null)) continue;
            float score = zone.GroundTiles + MarketDemand(realm, zone) * 40f + EcRandom.value * 20f;
            options.Add((zone, score, far));
        }
        if (options.Count == 0) return null;
        var best = options.OrderByDescending(option => option.score).First();
        distance = best.distance;
        return best.zone;
    }
#endif

    // 本国市场缺、这块地出的物资(0~1，见 CommodityMarketSystem.ColonyDemand)：殖民就有了去处
    private static float MarketDemand(Realm realm, Cell zone) => CommodityMarketSystem.ColonyDemand(realm, zone);

#if EC_PENDING
    private static void Arrive(Realm realm, KingdomExtension.KingdomExtraData data)
    {
        Cell zone = EmpireWorld.Current.Zones.ById(data.colonial_voyage_zone);
        data.colonial_voyage_zone = -1;
        if (zone == null || zone.City != null || !zone.canStartCityHere() || realm.Capital == null) return;
        Person founder = realm.Capital.Leader ?? realm.Ruler;
        if (founder == null) return;
        City city;
        Founding = true;
        try
        {
            city = EmpireWorld.Current.Cities.buildNewCity(founder, zone);
        }
        finally
        {
            Founding = false;
        }
        if (city == null) return;
        string culture = CultureService.GetRealmCulture(realm);
        CityPopulationSystem.TransferCivilianBackground(realm.Capital, city, Settlers);
        if (CultureService.IsValidCulture(culture)) CultureService.SetCityMainCulture(city, culture);
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (population != null) CityPopulationSystem.Census(city, population, keepBackground: true, preserveSmallGroups: true);
        CityPopulationSystem.EnsureLeader(city);
        if (population != null) CityPopulationSystem.Census(city, population, keepBackground: true, preserveSmallGroups: true);
        CityConstructionSystem.StartSettlement(city);
        TranslateHelper.LogEventOnly(string.Format(LM.Get("colony_voyage_arrived"), realm.GetKingdomFullName(), city.GetCityName()), realm);
        Realm existing = ColoniesOf(realm).FirstOrDefault(colony => IslandOf(colony.Capital) == IslandOf(city));
        if (existing != null)
        {
            city.JoinRealm(existing);
            return;
        }
        int kind = TechnologySystem.RealmHasTech(realm, CompanyTech) && EcRandom.value < CompanyChance
            ? ColonyState.Company : ColonyState.Governorate;
        Found(realm, new List<City> { city }, kind);
    }
#endif

    #endregion
}
}
