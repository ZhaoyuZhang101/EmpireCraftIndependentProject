// Generated from the EmpireCraft mod: Scripts/GeneralSystems/GentrySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 世家(用户 2026-10-16："我可能还需要一个世家系统"；选了门第郡望、仕途垄断、庄园部曲、兴衰站队四样，只给华夏)。
// 第一步——门第与郡望：
//   · 每年给每个在世的华夏宗族(在位皇帝的宗室除外)算一回"门第"目标(0~100)：
//       族人做官(在任官员每人 6，城主 4，诸侯王 12；虚拟族人记着官职的 3)、爵位与领有的头衔(每个 5，荣誉爵位 6，藩王 6)，
//       这两项合计至多 60；先世官爵(过世族人做过官、有爵位头衔、封过王的，每远一代打七折，至多 20，见 Ancestry)；
//       郡望之城的田产(5×√份数，至多 15)；名望最高的三人(每 30 名望 1，至多 10)；
//       族大(2×√谱上在世族人至多 10，与 5×lg 族众取大，至多 15——百人 10、千人 15，见 GentrySystem.Kin)；
//       年深(立族每 15 年 1，至多 10)；与世家、宗室联姻(每对 2，至多 8)；
//     门第向目标靠拢——升得快(每年一成五)、跌得慢(每年半成)：家道中落要好些年；
//   · 门第到三十列为世家，跌到二十二以下不再是世家；七十以上甲等、五十五乙等、四十丙等，其余丁等；
//   · 郡望：田产最多的城，没有田产按祖籍，再没有按族人最多的城；称"某城某氏"；
//   · 头一次算(旧存档、新开局)直接到位、不记史；此后列为世家、门第衰微记入郡望所在帝国的史册(每国每年至多三条)。
// 第二步——仕途垄断：
//   · 官缺由世家子弟补上的机会：施行九品中正 七成五，施行察举 四成，二者都没有(世卿世禄) 两成五，施行科举以后不再优先；
//     再乘在册世家的声势(势力合计，0.6~1.3 倍，至多九成五，见 GentrySystem.Kin)；
//     从本帝国郡望在册的世家里按势力挑一家，取那家在本国的成年族人(实体优先，没有就把虚拟族人落成实体)；
//   · 九品中正期间门第固化：门第跌得慢一半。
// 第三步——庄园部曲：
//   · 荫户：世家在城里的田产荫庇佃户，那座城的税少收(每家：占地份额 × 2 × 门第/100；郡望之城再与族众每人三口佃客取大)，
//     一城至多少收两成五，施行均田以后减半；荫户也不服兵役(见 CorpsSystem 的兵源)；
//   · 部曲：郡望之城有敌军占地或交战时，世家聚部曲成一支义军守城(族众每人一个半，抽本城可出丁口的百分之二到三十)，
//     三年至多一次，仗打完就散；
//   · 据城自守：甲、乙等世家的郡望不是国都，所在国正在打仗、朝廷衰弱(法统二十以下或皇位空缺)，每年百分之四的机会
//     由族中名望最高的人据城自立；天下裂土时，郡望在京畿的甲等世家各有一半机会据城自守(见 InterregnumSystem)。
// 第四步——兴衰与站队：
//   · 择主：两朝并立、天下裂土时，世家依附郡望所在的那一朝；附的世家门第合计每 40 得法统 1(至多 10)，
//     史册记"某氏、某氏等若干家附某朝"(见 ChooseSides)；
//   · 篡位：篡位者出自世家，诸世家多附之(法统 +5)；起于寒门，甲乙等世家耻之(每家 -2，至多 -8)；
//   · 皇权压门阀：帝党、文官可请"修氏族志"(以当朝官爵重定门第：族中有人在朝的升、没人出仕的降，法统 +3，三十年一次)；
//     法家、帝党可请"抑豪强"(抄没门第最高三家一半田产、家财入国库，这几家门第 -10，贵族地主怨、农民喜，二十年一次)。
// 宗族成千上万，按帧分批算(见 Tick)。
public static partial class GentrySystem
{
    public const float EnterAt = 30f, LeaveAt = 22f;
    public const string NineRankNode = "huaxia_nine_rank", RecommendationNode = "huaxia_recommendation",
        ExamNode = "huaxia_examination_officialdom";
    private const float NineRankMonopoly = 0.75f, RecommendationMonopoly = 0.4f, HereditaryMonopoly = 0.25f;
    private const int CandidateClans = 12, CandidateTries = 3;
    private const float ShelterPerShare = 2f, ShelterCap = 0.25f, RetainerMin = 0.02f, RetainerMax = 0.3f,
        AutonomyChance = 0.04f, FractureAutonomyChance = 0.5f;
    private const int RetainerCooldownYears = 3, AutonomyMandate = 20, FractureAutonomyMax = 2;
    public const string EqualFieldNode = "huaxia_land_equalization";
    private const float RiseRate = 0.15f, FallRate = 0.05f, OfficeCap = 60f;
    private const int MaxRecordsPerEmpire = 3;

#if EC_PENDING
    private static readonly FrameBudgetQueue<long> Queue = new(1.5d, Assess, "世家门第");
#endif
    private static double _lastPass = -1d;
    private static readonly Dictionary<long, (float share, long city)> Land = new();
    private static readonly HashSet<SpecificClan> Ruling = new();
    private static readonly Dictionary<long, int> Recorded = new();
    // 各城被世家荫庇的份额(上一轮结算时算)；在册世家(上一轮)
    private static readonly Dictionary<long, float> Sheltered = new();
    private static readonly List<long> GentryIds = new();
    private static double _lastMonth = -1d;

#if EC_PENDING
    public static void ResetWorldState()
    {
        Queue.Cancel();
        _lastPass = -1d;
        _lastMonth = -1d;
        Sheltered.Clear();
        GentryIds.Clear();
        Land.Clear();
        Ruling.Clear();
        Recorded.Clear();
        Registry.Clear();
    }
#endif

    #region 查询

    public static bool IsGentry(SpecificClan clan) => clan != null && clan.gentry;

    public static bool IsGentry(Person actor) => IsGentry(actor?.GetSpecificClan());

    public static float Pedigree(SpecificClan clan) => clan?.gentry_pedigree ?? 0f;

    // 门第等第：1 甲、2 乙、3 丙、4 丁
    public static int Grade(float pedigree) => pedigree >= 70f ? 1 : pedigree >= 55f ? 2 : pedigree >= 40f ? 3 : 4;

    public static string GradeName(SpecificClan clan) => LM.Get("gentry_grade_" + Grade(Pedigree(clan)));

    public static City Seat(SpecificClan clan)
    {
        long id = clan?.gentry_seat_city_id ?? -1L;
        City city = id > 0 ? EmpireWorld.Current.CityByKey(id) : null;
        return city?.Record == null || city.IsGone() ? null : city;
    }

    // 郡望："某城某氏"
#if EC_PENDING
    public static string JunWang(SpecificClan clan)
    {
        if (clan == null) return "";
        string surname = !string.IsNullOrEmpty(clan.shi) ? clan.shi.TrimEnd('氏') : clan.name ?? "";
        City seat = Seat(clan);
        return string.Format(LM.Get("gentry_junwang"), seat?.GetCityName() ?? "", surname);
    }
#endif

    // 郡望在本帝国的世家，门第从高到低
#if EC_PENDING
    public static List<SpecificClan> Of(Empire empire)
    {
        if (empire?.data == null) return new List<SpecificClan>();
        return SpecificClanManager._specificClans
            .Where(clan => clan != null && clan.gentry && clan.Count > 0 && RealmOf(Seat(clan)) == empire)
            .OrderByDescending(clan => clan.gentry_pedigree).ToList();
    }
#endif

    private static Empire RealmOf(City city)
    {
        Realm kingdom = city?.Realm;
        if (kingdom == null || kingdom.IsGone()) return null;
        return kingdom.IsEmpire() || kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
    }

#if EC_PENDING
    private static bool Huaxia(City city)
    {
        if (city == null) return false;
        try
        {
            return InstitutionSystem.GetCultureLine(CultureService.GetMainCulture(city, initialize: false)) == "Huaxia";
        }
        catch
        {
            return false;
        }
    }
#endif

    #endregion

    #region 仕途

    // 官缺由世家子弟补上的机会
#if EC_PENDING
    public static float Monopoly(Realm kingdom)
    {
        // 选官之制照制度文化(旧制犹存的照旧制，见 LegacyInstitutionSystem)
        string culture = CultureService.GetInstitutionCulture(kingdom);
        if (!CultureService.IsValidCulture(culture)) return 0f;
        try
        {
            if (InstitutionSystem.GetCultureLine(culture) != "Huaxia") return 0f;
        }
        catch
        {
            return 0f;
        }
        if (InstitutionSystem.IsEnacted(culture, ExamNode)) return 0f;
        if (InstitutionSystem.IsEnacted(culture, NineRankNode)) return NineRankMonopoly;
        return InstitutionSystem.IsEnacted(culture, RecommendationNode) ? RecommendationMonopoly : HereditaryMonopoly;
    }
#endif

    private static readonly Dictionary<long, (double at, List<long> clans)> Registry = new();

    // 本帝国郡望在册的世家(门第从高到低，一年一更)
#if EC_PENDING
    private static List<SpecificClan> Registered(Empire empire)
    {
        double now = GameClock.Now;
        if (!Registry.TryGetValue(empire.data.id, out var cached) || now < cached.at || GameClock.YearsSince(cached.at) >= 1)
        {
            cached = (now, Of(empire).Take(CandidateClans).Select(clan => clan.id).ToList());
            Registry[empire.data.id] = cached;
        }
        return cached.clans.Select(SpecificClanManager.Get).Where(clan => clan != null && clan.gentry).ToList();
    }
#endif

    // 官缺补人(OfficeSelector.Select)：按机会挑一位世家子弟，没挑上返回空
#if EC_PENDING
    public static Person Candidate(Realm kingdom, Func<Person, bool> fit)
    {
        Empire empire = kingdom?.GetEmpire();
        float monopoly = Monopoly(kingdom);
        if (empire?.data == null || monopoly <= 0f) return null;
        List<SpecificClan> clans = Registered(empire);
        if (clans.Count == 0 || EcRandom.value >= Mathf.Min(MonopolyMax, monopoly * Sway(clans))) return null;
        for (int attempt = 0; attempt < CandidateTries; attempt++)
        {
            SpecificClan clan = Weighted(clans);
            Person member = Member(clan, kingdom, fit);
            if (member == null) continue;
            ShiftFavor(empire, FavorAppoint);
            return member;
        }
        return null;
    }
#endif

    private const float MonopolyMax = 0.95f, SwayBase = 0.6f, SwayPer = 1000f, SwayMax = 1.3f;

    // 在册世家的声势：势力合计每 1000 加一倍(0.6~1.3 倍)，世家又少又小的补官机会少
#if EC_PENDING
    private static float Sway(List<SpecificClan> clans) => Mathf.Clamp(SwayBase + Influence(clans) / SwayPer, SwayBase, SwayMax);
#endif

    // 按势力的平方挑一家(门第高、族众多的大族多中)
#if EC_PENDING
    private static SpecificClan Weighted(List<SpecificClan> clans)
    {
        float total = clans.Sum(clan => Influence(clan) * Influence(clan));
        float roll = EcRandom.value * total;
        foreach (SpecificClan clan in clans)
            if ((roll -= Influence(clan) * Influence(clan)) <= 0f) return clan;
        return clans[clans.Count - 1];
    }
#endif

    // 那家在本国的成年族人：实体优先，没有就把虚拟族人落成实体
#if EC_PENDING
    private static Person Member(SpecificClan clan, Realm kingdom, Func<Person, bool> fit)
    {
        PersonalClanIdentity candidate = null;
        foreach (PersonalClanIdentity person in clan.LivingPeople.OrderBy(_ => EcRandom.value))
        {
            if (!person.is_virtual)
            {
                Person actor = person._actor;
                if (actor?.Record != null && !actor.IsGone() && actor.Alive && actor.IsAdult() && actor.Realm == kingdom &&
                    !actor.IsOnOffice() && !actor.IsRuler() && !actor.IsCityLeader() && fit(actor)) return actor;
                continue;
            }
            if (candidate == null && person.age >= 18 && EmpireWorld.Current.CityByKey(person.virtual_city_id)?.Realm == kingdom)
                candidate = person;
        }
        Person realized = candidate?.Realize();
        return realized?.Record != null && fit(realized) ? realized : null;
    }
#endif

    #endregion

    #region 庄园部曲

#if EC_PENDING
    private static float ShelterShare(City city)
    {
        if (city?.Record == null || !Sheltered.TryGetValue(city.Key, out float share) || share <= 0f) return 0f;
        share = Mathf.Min(ShelterCap, share);
        string culture = CultureService.GetMainCulture(city, initialize: false);
        return CultureService.IsValidCulture(culture) && InstitutionSystem.IsEnacted(culture, EqualFieldNode) ? share * 0.5f : share;
    }
#endif

    // 荫户少收的税(乘在税率上，见 PopulationEconomySystem.PayTaxes)
#if EC_PENDING
    public static float TaxFactor(City city) => 1f - ShelterShare(city);
#endif

    // 荫户不服兵役(见 CorpsSystem)
#if EC_PENDING
    public static float ShelteredPeople(City city) => CityPopulationSystem.GetBackgroundTotal(city) * ShelterShare(city);
#endif

    // 每月：郡望之城告急，世家聚部曲守城
#if EC_PENDING
    private static void Monthly()
    {
        double now = GameClock.Now;
        if (_lastMonth >= 0d && now >= _lastMonth && GameClock.MonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        if (!CorpsSystem.Enabled) return;
        foreach (long id in GentryIds)
        {
            SpecificClan clan = SpecificClanManager.Get(id);
            City seat = Seat(clan);
            if (clan == null || !clan.gentry || seat?.Realm == null || !Threatened(seat)) continue;
            if (clan.gentry_retainers_at >= 0d && now >= clan.gentry_retainers_at &&
                GameClock.YearsSince(clan.gentry_retainers_at) < RetainerCooldownYears) continue;
            float before = CityPopulationSystem.GetBackgroundTotal(seat);
            float share = RetainerShare(clan, before);
            if (CorpsSystem.RaiseMilitia(seat, 1, share) <= 0) continue;
            clan.gentry_retainers_at = now;
            int raised = Mathf.Max(0, Mathf.RoundToInt(before - CityPopulationSystem.GetBackgroundTotal(seat)));
            Record(seat, string.Format(LM.Get("gentry_retainers_history"), JunWang(clan), raised, seat.GetCityName()), force: true);
        }
    }
#endif

    private static bool Threatened(City city)
    {
        if (FrontSystem.Battles.Values.Any(battle => battle.city_id == city.Key)) return true;
        Dictionary<int, long> occupied = ScorchedEarthSystem.OccupiedZones(city);
        return occupied != null && occupied.Values.Any(owner => EmpireWorld.Current.RealmByKey(owner) is Realm enemy &&
                                                                 enemy.IsEnemy(city.Realm));
    }

    // 据城自守：族中名望最高的人(实体优先，没有把郡望之城的虚拟族人落成实体)据郡望之城自立
#if EC_PENDING
    private static bool Autonomy(SpecificClan clan, City seat, string key)
    {
        // 不许自立(见 SecessionGate)：立国前先动了城主，在这里先问
        if (SecessionGate.Forbids()) return false;
        if (seat?.Realm == null || seat.Realm.Capital == seat) return false;
        Realm former = seat.Realm;
        List<PersonalClanIdentity> living = clan.LivingPeople;
        Person head = living.Where(person => !person.is_virtual).Select(person => person._actor)
            .Where(actor => actor?.Record != null && !actor.IsGone() && actor.Alive && actor.IsAdult() && actor.City == seat &&
                            !actor.IsRuler())
            .OrderByDescending(actor => actor.renown).FirstOrDefault()
            ?? living.FirstOrDefault(person => person.is_virtual && person.age >= 18 && person.virtual_city_id == seat.Key)?.Realize();
        if (head?.Record == null || head.IsRuler()) return false;
        if (head.IsCityLeader() && head.City != null && head.City != seat) head.City.removeLeader();
        Realm own = seat.makeOwnKingdom(head);
        if (own == null) return false;
        Record(seat, string.Format(LM.Get(key), JunWang(clan), seat.GetCityName(), former.GetKingdomName()), force: true);
        return true;
    }
#endif

    // 乱世里的强宗：每年(Assess)。筑了坞堡的丙等也算强宗、机会加倍；朝廷与世家交恶(关系 FavorLow 以下)机会再加倍
#if EC_PENDING
    private static void TryAutonomy(SpecificClan clan, City seat)
    {
        bool fortress = HasFortress(seat);
        if (Grade(clan.gentry_pedigree) > (fortress ? 3 : 2) || seat?.Realm == null || seat.Realm.Capital == seat ||
            !seat.Realm.HasEnemies())
            return;
        Empire realm = RealmOf(seat);
        bool weak = realm == null ? seat.Realm.Ruler == null
            : realm.Mandate <= AutonomyMandate || realm.Emperor == null || realm.Emperor.IsGone();
        float chance = AutonomyChance * (fortress ? 2f : 1f) * (realm != null && Favor(realm) <= FavorLow ? 2f : 1f);
        if (!weak || EcRandom.value >= chance) return;
        Autonomy(clan, seat, "gentry_autonomy_history");
    }
#endif

    // 天下裂土(InterregnumSystem.TryFracture)：郡望在京畿的甲等世家各有一半机会据城自守
#if EC_PENDING
    public static void OnFracture(Empire empire)
    {
        Realm core = empire?.CoreKingdom;
        if (core == null) return;
        int count = 0;
        foreach (SpecificClan clan in Of(empire).Where(clan => Grade(clan.gentry_pedigree) == 1).ToList())
        {
            if (count >= FractureAutonomyMax) break;
            City seat = Seat(clan);
            if (seat?.Realm != core || EcRandom.value >= FractureAutonomyChance) continue;
            if (Autonomy(clan, seat, "gentry_fracture_autonomy_history")) count++;
        }
    }
#endif

    #endregion

    #region 兴衰与站队

    private const int RegisterYears = 30, CurbYears = 20, CurbClans = 3, CurbMoneyPerClan = 150, SidesMandatePer = 60,
        SidesMandateMax = 15, UsurperGentryMandate = 5, UsurperDisdainPer = 2, UsurperDisdainMax = 8;

    // 两朝并立、天下裂土：世家依附郡望所在的那一朝(势力合计每 60 得法统 1，至多 15)
#if EC_PENDING
    public static void ChooseSides(IEnumerable<Empire> realms)
    {
        foreach (Empire realm in realms.Where(realm => realm?.data != null && !realm.IsGone()).Distinct().ToList())
        {
            List<SpecificClan> clans = Of(realm);
            if (clans.Count == 0) continue;
            foreach (SpecificClan clan in clans) clan.gentry_patron_empire_id = realm.data.id;
            int bonus = Mathf.Min(SidesMandateMax, Mathf.RoundToInt(Influence(clans) / SidesMandatePer));
            if (bonus > 0) realm.AddMandate(bonus);
            string names = string.Join("、", clans.Take(2).Select(JunWang));
            string content = string.Format(LM.Get("gentry_sides_history"), names, clans.Count, realm.GetEmpireFullName());
            LogService.LogInfo("[EmpireCraft][世家] " + content);
            realm.RecordHistory(directContent: content);
        }
    }
#endif

    // 篡位(Empire.NewEmperor)：出自世家的诸世家多附之，起于寒门的诸世家耻之
#if EC_PENDING
    public static void OnUsurpation(Empire empire, Person usurper)
    {
        if (empire?.data == null || usurper?.Record == null) return;
        SpecificClan clan = usurper.GetSpecificClan();
        string content;
        if (clan != null && clan.gentry)
        {
            empire.AddMandate(UsurperGentryMandate);
            ShiftFavor(empire, FavorGentryUsurper);
            content = string.Format(LM.Get("gentry_usurper_history"), JunWang(clan), usurper.FullName());
        }
        else
        {
            int proud = Of(empire).Count(other => Grade(other.gentry_pedigree) <= 2);
            if (proud == 0) return;
            empire.AddMandate(-Mathf.Min(UsurperDisdainMax, proud * UsurperDisdainPer));
            ShiftFavor(empire, FavorUpstart);
            content = string.Format(LM.Get("gentry_upstart_history"), usurper.FullName());
        }
        LogService.LogInfo("[EmpireCraft][世家] " + content);
        empire.RecordHistory(directContent: content, actorId: usurper.Key);
    }
#endif

    private static bool Due(double at, int years) =>
        at < 0d || GameClock.Now < at || GameClock.YearsSince(at) >= years;

#if EC_PENDING
    public static bool CanReviseRegister(Empire empire) =>
        empire?.data != null && Due(empire.data.gentry_register_at, RegisterYears) && Of(empire).Count >= 3;
#endif

    // 修氏族志：族中有人在朝的世家门第上升(每人 5，至多 15)，没人出仕的降 15；返回升、降的家数
#if EC_PENDING
    public static (int raised, int lowered) ReviseRegister(Empire empire)
    {
        if (empire?.data == null) return (0, 0);
        empire.data.gentry_register_at = GameClock.Now;
        int raised = 0, lowered = 0;
        foreach (SpecificClan clan in Of(empire))
        {
            int office = clan.LivingPeople.Count(person => !person.is_virtual && person._actor?.Record != null &&
                                                            !person._actor.IsGone() && (person._actor.IsOnOffice() || person._actor.IsCityLeader()));
            if (office > 0)
            {
                clan.gentry_pedigree = Mathf.Min(100f, clan.gentry_pedigree + 5f * Mathf.Min(3, office));
                raised++;
            }
            else
            {
                clan.gentry_pedigree = Mathf.Max(0f, clan.gentry_pedigree - 15f);
                lowered++;
            }
        }
        empire.AddMandate(3);
        ShiftFavor(empire, raised * 2f - lowered * 3f);
        empire.RecordHistory(directContent: string.Format(LM.Get("gentry_register_history"), empire.GetEmpireFullName(), raised, lowered));
        return (raised, lowered);
    }

    public static bool CanCurbMagnates(Empire empire) =>
        empire?.data != null && Due(empire.data.gentry_curbed_at, CurbYears) &&
        Of(empire).Any(clan => Grade(clan.gentry_pedigree) <= 2);
#endif

    // 抑豪强：门第最高三家的田产减半(空出的田归民田)、家财入国库(每家 150，族众每人再 0.5)、门第 -10、族众折三成；
    // 返回家数、入库钱数
#if EC_PENDING
    public static (int clans, int money) CurbMagnates(Empire empire)
    {
        if (empire?.data == null || empire.CoreKingdom == null) return (0, 0);
        empire.data.gentry_curbed_at = GameClock.Now;
        List<SpecificClan> targets = Of(empire).Take(CurbClans).ToList();
        int money = targets.Sum(clan => CurbMoneyPerClan + Mathf.RoundToInt(clan.gentry_kin * KinCurbMoney));
        foreach (SpecificClan clan in targets)
        {
            string key = $"s:{clan.id}";
            foreach (City city in EmpireWorld.Current.Cities)
            {
                if (city?.Record == null || city.IsGone()) continue;
                Dictionary<string, float> shares = city.GetOrCreate().household_land_shares;
                if (shares != null && shares.TryGetValue(key, out float share) && share > 0f) shares[key] = share * 0.5f;
            }
            clan.gentry_pedigree = Mathf.Max(0f, clan.gentry_pedigree - 10f);
            clan.gentry_kin *= 1f - KinCurbLoss;
        }
        if (money > 0) empire.CoreKingdom.AddMoney(money, TreasuryCategory.RecoveredFunds);
        ShiftFavor(empire, FavorCurb);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Noble, 6f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Landlord, 6f);
        ClaimPolicySystem.AddGrievance(empire, SocialClass.Peasant, -4f);
        if (targets.Count > 0)
            empire.RecordHistory(directContent: string.Format(LM.Get("gentry_curb_history"), empire.GetEmpireFullName(),
                string.Join("、", targets.Select(JunWang))));
        return (targets.Count, money);
    }
#endif

    #endregion

    #region 每年分批结算

    // 每帧(CityPopulationSystem 里跟着虚拟族谱一起推进)：一年一轮
#if EC_PENDING
    public static void Tick()
    {
        if (EmpireWorld.Current == null) return;
        Monthly();
        if (Queue.Active)
        {
            Queue.Tick();
            return;
        }
        double now = GameClock.Now;
        if (_lastPass >= 0d && now >= _lastPass && GameClock.YearsSince(_lastPass) < 1) return;
        if (SpecificClanManager._specificClans.Count == 0) return;
        _lastPass = now;
        GentryIds.Clear();
        GentryIds.AddRange(SpecificClanManager._specificClans.Where(clan => clan != null && clan.gentry).Select(clan => clan.id));
        BuildLand();
        BuildRuling();
        SeedScenario();
        Recorded.Clear();
        BuildFortresses();
        YearlyCourt();
        // 已绝嗣的世家也要评：本宗凋尽由支脉承嗣或除名(以前只排没绝的宗族，绝了的世家一直挂在名录上，
        // 玩家 10-30 反馈"经常 0 族人挂空在榜上")
        Queue.Start(SpecificClanManager._specificClans.Where(clan => clan != null && (clan.extinct_since < 0d || clan.gentry))
            .Select(clan => clan.id).ToList());
        Queue.Tick();
    }
#endif

    // 各宗族的私田：合计占地(按城的分数加总)、占地最多的城
    private static void BuildLand()
    {
        Land.Clear();
        Sheltered.Clear();
        foreach (City city in EmpireWorld.Current.Cities)
        {
            if (city?.Record == null || city.IsGone()) continue;
            Dictionary<string, float> shares = city.GetOrCreate().household_land_shares;
            if (shares == null) continue;
            foreach (KeyValuePair<string, float> pair in shares)
            {
                if (pair.Value <= 0f || !pair.Key.StartsWith("s:", StringComparison.Ordinal) ||
                    !long.TryParse(pair.Key.Substring(2), out long clanId)) continue;
                // 荫户：世家在这座城的田产(份额是百分数)
                SpecificClan owner = SpecificClanManager.Get(clanId);
                if (owner != null && owner.gentry)
                {
                    Sheltered.TryGetValue(city.Key, out float sheltered);
                    Sheltered[city.Key] = sheltered + pair.Value / 100f * ShelterPerShare * owner.gentry_pedigree / 100f;
                }
                Land.TryGetValue(clanId, out var held);
                float best = held.city > 0 && EmpireWorld.Current.CityByKey(held.city) is City bestCity &&
                             bestCity.GetOrCreate().household_land_shares.TryGetValue(pair.Key, out float bestShare)
                    ? bestShare : 0f;
                Land[clanId] = (held.share + pair.Value, pair.Value > best ? city.Key : held.city);
            }
        }
        // 郡望之城再按族众荫庇佃客：比田产算出来的多就补上差额(见 GentrySystem.Kin 的 ShelterOf)
        foreach (long id in GentryIds)
        {
            SpecificClan clan = SpecificClanManager.Get(id);
            City seat = clan?.gentry == true && clan.gentry_kin > 0f ? Seat(clan) : null;
            if (seat == null) continue;
            float people = CityPopulationSystem.GetBackgroundTotal(seat);
            float extra = ShelterOf(clan, seat, people) - SeatLand(clan, seat) / 100f * ShelterPerShare * clan.gentry_pedigree / 100f;
            if (extra <= 0f) continue;
            Sheltered.TryGetValue(seat.Key, out float sheltered);
            Sheltered[seat.Key] = sheltered + extra;
        }
    }

    // 在位皇帝的宗室(连同分出去的各支)：是皇族，不算世家
    private static void BuildRuling()
    {
        Ruling.Clear();
        if (ModClass.EMPIRE_MANAGER == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire?.data == null || empire.IsGone() || empire.IsArchived() || empire.EmpireSpecificClan == null) continue;
            foreach (SpecificClan clan in SpecificClanManager.LineageClans(empire.EmpireSpecificClan)) Ruling.Add(clan);
        }
    }

#if EC_PENDING
    private static void Assess(long id)
    {
        SpecificClan clan = SpecificClanManager.Get(id);
        if (clan == null) return;
        List<PersonalClanIdentity> living = clan.LivingPeople;
        City seat = living.Count == 0 ? null : FindSeat(clan, living);
        if (living.Count == 0 || Ruling.Contains(clan) || !Huaxia(seat))
        {
            if (clan.gentry && living.Count == 0)
            {
                // 族众还在：族中远支记一人入谱为宗子(见 GentrySystem.Kin)
                if (Revive(clan)) return;
                // 旧档里早已绝嗣、一直没除名的：悄悄除名，不再补记史书
                if (clan.extinct_since >= 0d && GameClock.Now >= clan.extinct_since &&
                    GameClock.YearsSince(clan.extinct_since) >= 2) clan.gentry = false;
                // 本宗凋尽：最大的一支承嗣，门第、郡望归之(见 GentrySystem.Lineage)
                else if (!BranchSucceeds(clan)) Decline(clan, extinct: true);
            }
            else if (clan.gentry) clan.gentry = false;
            UpdateKin(clan, null, living.Count);
            return;
        }
        // 世家无男丁可承：从支脉或族中过继一人(见 GentrySystem.Lineage)
        if (clan.gentry) TryAdopt(clan, living);
        clan.gentry_seat_city_id = seat.Key;
        float target = Target(clan, living);
        if (!clan.gentry_assessed)
        {
            // 头一次直接到位、不记史
            clan.gentry_assessed = true;
            clan.gentry_pedigree = target;
            clan.gentry = target >= EnterAt;
            if (clan.gentry) clan.gentry_since = GameClock.Now;
            NoteTrend(clan);
            UpdateKin(clan, seat, living.Count);
            return;
        }
        float pedigree = clan.gentry_pedigree;
        // 九品中正：门第固化，跌得慢一半
        float fall = FallRate * (InstitutionSystem.IsEnacted(CultureService.GetMainCulture(seat, initialize: false), NineRankNode) ? 0.5f : 1f);
        pedigree += (target - pedigree) * (target > pedigree ? RiseRate : fall);
        clan.gentry_pedigree = Mathf.Clamp(pedigree, 0f, 100f);
        NoteTrend(clan);
        if (!clan.gentry && clan.gentry_pedigree >= EnterAt)
        {
            clan.gentry = true;
            clan.gentry_since = GameClock.Now;
            string rise = string.Format(LM.Get("gentry_rise_history"), JunWang(clan), GradeName(clan));
            Record(seat, rise);
            NoteClan(clan, "clan_history_gentry_rise", rise);
            clan.gentry_noted_grade = Grade(clan.gentry_pedigree);
        }
        else if (clan.gentry && clan.gentry_pedigree < LeaveAt) Decline(clan, extinct: false);
        else if (clan.gentry) NoteGrade(clan);
        // 族众长落、兼并郡望的田(见 GentrySystem.Kin)
        UpdateKin(clan, seat, living.Count);
        if (clan.gentry)
        {
            Annex(clan, seat);
            TryRelief(clan, seat);
            TryAutonomy(clan, seat);
        }
    }

    private static void Decline(SpecificClan clan, bool extinct)
    {
        string name = JunWang(clan);
        City seat = Seat(clan);
        clan.gentry = false;
        string text = string.Format(LM.Get(extinct ? "gentry_extinct_history" : "gentry_decline_history"), name);
        Record(seat, text);
        NoteClan(clan, "clan_history_gentry_decline", text);
    }
#endif

    private const int TrendYears = 80, ClanHistoryCap = 40;
    // 门第走势只给世家与够得着世家的(门第 20 以上)记，跌到 20 以下再记一笔就停，免得每个寒门都存八十年
    public const float TrendFloor = 20f;
    private const float GradeMargin = 2f;

    // 门第换了等(离分界 2 分以上才算，回落、被括户抑豪强压下去的也在这里记)
#if EC_PENDING
    private static void NoteGrade(SpecificClan clan)
    {
        float pedigree = clan.gentry_pedigree;
        int grade = Grade(pedigree);
        if (Grade(pedigree + GradeMargin) != grade || Grade(pedigree - GradeMargin) != grade) return;
        int noted = clan.gentry_noted_grade;
        clan.gentry_noted_grade = grade;
        if (noted <= 0 || noted == grade) return;
        NoteClan(clan, "clan_history_gentry_grade", string.Format(LM.Get(grade < noted ? "clan_history_grade_up" : "clan_history_grade_down"),
            JunWang(clan), GradeName(clan)));
    }
#endif

    // 门第走势记一笔：同一年只留一笔(读档重算、补算时覆盖)
    private static void NoteTrend(SpecificClan clan)
    {
        int year = GameClock.Year;
        int value = Mathf.RoundToInt(clan.gentry_pedigree);
        int last = (clan.pedigree_years?.Count ?? 0) - 1;
        bool recording = last >= 0 && clan.pedigree_years[last] >= year - 1;
        if (!clan.gentry && clan.gentry_pedigree < TrendFloor && !recording) return;
        clan.pedigree_years ??= new List<int>();
        clan.pedigree_values ??= new List<int>();
        if (clan.pedigree_values.Count != clan.pedigree_years.Count)
        {
            clan.pedigree_years.Clear();
            clan.pedigree_values.Clear();
            last = -1;
        }
        if (last >= 0 && clan.pedigree_years[last] == year)
        {
            clan.pedigree_values[last] = value;
            return;
        }
        clan.pedigree_years.Add(year);
        clan.pedigree_values.Add(value);
        while (clan.pedigree_years.Count > TrendYears)
        {
            clan.pedigree_years.RemoveAt(0);
            clan.pedigree_values.RemoveAt(0);
        }
    }

    // 宗族大事记一条(宗族窗口大事记用)
#if EC_PENDING
    private static void NoteClan(SpecificClan clan, string key, string content)
    {
        clan.clan_history ??= new List<PersonalHistoryRecord>();
        clan.clan_history.Add(new PersonalHistoryRecord
        {
            timestamp = GameClock.Now, date = Date.getDate(GameClock.Now),
            content = content, event_key = key, major = true
        });
        if (clan.clan_history.Count > ClanHistoryCap) clan.clan_history.RemoveAt(0);
    }
#endif

    // 郡望：田产最多的城；没有田产按祖籍；再没有按族人最多的城
#if EC_PENDING
    private static City FindSeat(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        if (Land.TryGetValue(clan.id, out var held) && EmpireWorld.Current.CityByKey(held.city) is City landed && landed.Record != null &&
            !landed.IsGone()) return landed;
        City ancestral = clan.ancestral_city_id > 0 ? EmpireWorld.Current.CityByKey(clan.ancestral_city_id) : null;
        if (ancestral?.Record != null && !ancestral.IsGone()) return ancestral;
        long cityId = living.Select(person => person.is_virtual ? person.virtual_city_id : person._actor?.city?.data?.id ?? -1L)
            .Where(cityIdOf => cityIdOf > 0).GroupBy(cityIdOf => cityIdOf).OrderByDescending(group => group.Count())
            .Select(group => group.Key).FirstOrDefault();
        City common = cityId > 0 ? EmpireWorld.Current.CityByKey(cityId) : null;
        return common?.Record != null && !common.IsGone() ? common : null;
    }

    private static float Target(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        float office = 0f, marriage = 0f;
        var renown = new List<int>();
        foreach (PersonalClanIdentity person in living)
        {
            Person actor = person.is_virtual ? null : person._actor;
            if (actor?.Record != null && !actor.IsGone() && actor.Alive)
            {
                if (actor.IsRuler()) office += 12f;
                else if (actor.IsOnOffice()) office += 6f;
                else if (actor.IsCityLeader()) office += 4f;
                if (actor.HasHonoraryPeerage()) office += 6f;
                office += 5f * (actor.GetOwnedTitle()?.Count ?? 0);
                renown.Add(actor.renown);
            }
            else
            {
                if (!string.IsNullOrEmpty(person.officeName)) office += 3f;
                office += 5f * (person.ownedTitleNames?.Count ?? 0);
            }
            if (!string.IsNullOrEmpty(person.prince_title)) office += 6f;
            SpecificClan spouse = person.lover.identity > 0 ? SpecificClanManager.getPerson(person.lover.identity)?._specificClan : null;
            if (spouse != null && spouse != clan && (spouse.gentry || Ruling.Contains(spouse))) marriage += 2f;
        }
        float score = Mathf.Min(OfficeCap, office) + Ancestry(clan, living);
        // 田产、族大开方算：一族多是几个人、一两份地(玩家 10-30 存档：在世宗族九成五不到五人)，照人头、份数算到不了顶
        if (Land.TryGetValue(clan.id, out var held)) score += Mathf.Min(15f, 5f * Mathf.Sqrt(held.share));
        score += Mathf.Min(10f, renown.OrderByDescending(value => value).Take(3).Sum() / 30f);
        // 族大：谱上在世的(小族)与族众(大族，百人 10、千人 15)取大
        score += Mathf.Min(SizeCap, Mathf.Max(Mathf.Min(10f, 2f * Mathf.Sqrt(living.Count)),
            5f * Mathf.Log10(Mathf.Max(1f, clan.gentry_kin))));
        // 年深：开局就有的宗族(剧本里的官员、开局的部落首领)立族时间是 0，以前不算，存档里最老的几家世家吃了亏
        if (clan.established_timestamp >= 0d && GameClock.Now >= clan.established_timestamp)
            score += Mathf.Min(10f, GameClock.YearsSince(clan.established_timestamp) / 15f);
        score += Mathf.Min(8f, marriage);
        return Mathf.Clamp(score, 0f, 100f);
    }
#endif

    private const float AncestorOffice = 5f, AncestorTitle = 3f, AncestorFade = 0.7f, AncestryCap = 20f, SizeCap = 15f;

    // 先世官爵(累世公卿)：过世的族人做过官、有爵位(每人 5)、头衔(每个 3)、封过王(5)，离在世族人最晚一代每远一代打七折，
    // 至多 AncestryCap。只是本人做官的寒门没有这一项；几代人接连做官，才够得上世家(玩家 10-30 反馈"世家根本起不来")
    private static float Ancestry(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        int latest = living.Max(person => person.generation);
        float score = 0f;
        foreach (PersonalClanIdentity person in clan.SnapshotPeople())
        {
            if (person == null || person.is_alive) continue;
            float points = (string.IsNullOrEmpty(person.officeName) && string.IsNullOrEmpty(person.PeeragesLevel) ? 0f : AncestorOffice) +
                           AncestorTitle * (person.ownedTitleNames?.Count ?? 0) +
                           (string.IsNullOrEmpty(person.prince_title) ? 0f : AncestorOffice);
            if (points <= 0f) continue;
            score += points * Mathf.Pow(AncestorFade, Mathf.Max(0, latest - person.generation - 1));
            if (score >= AncestryCap) return AncestryCap;
        }
        return score;
    }

#if EC_PENDING
    private static void Record(City seat, string content, bool force = false)
    {
        LogService.LogInfo("[EmpireCraft][世家] " + content);
        Empire empire = RealmOf(seat);
        if (empire == null) return;
        Recorded.TryGetValue(empire.data.id, out int count);
        if (!force && count >= MaxRecordsPerEmpire) return;
        Recorded[empire.data.id] = count + 1;
        empire.RecordHistory(directContent: content);
        if (force) TranslateHelper.LogEventMessage(content, seat?.Realm);
    }
#endif

    #endregion
}
}
