// Generated from the EmpireCraft mod: Scripts/GeneralSystems/HolyRomanSystem.Members.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 帝国的成员构成(封建制帝国)：
//   · 帝国自由城市：诸侯领内人口最多的非都城，得皇帝特许状脱离诸侯，自立为一城一国(市议会推举执政，不世袭)，
//     直属皇帝，每年把一成年度经常收入缴给中央，在帝国议会城市院有席位；数目不超过成员国的三分之一(至少可有两座)；
//     中央缺钱(国库不足一成年收)时，皇帝可把自由城市典押给最有钱的诸侯，城就并入那个诸侯国(原型"典押")，
//     记下押价，三十年内可赎回(见 Immediacy)；
//   · 教会诸侯(主教领)：皇帝把中央一座非都城的城封给主教(原型奥托王朝的帝国教会)，每年一成可能；
//     中央三城以上、帝国六城以上，每四家成员至多一家、最多三家(见 ChurchBlocker)；
//     主教不世袭，出缺时按叙任权另派——教会叙任由教廷派、政教协约由本地座堂教士推举、君主叙任由皇帝派自己人；
//     选帝侯团的三个教会席位先给主教领的主教(见 EmpireCraftEmpireBehCheckCabinet)。
public static partial class HolyRomanSystem
{
    public const int EstateNone = 0, EstateFreeCity = 1, EstateChurch = 2;
    // 自由城市每年特许几率一成二 → 6%：一城一国越封越多，小邦林立(用户 2026-10-21 选"减少碎片")
    private const float FreeCityChance = 0.06f, ChurchChance = 0.1f, PawnChance = 0.25f;
    private const int MaxChurchPrinces = 3, MinFreeCityCap = 2, ChurchMinCoreCities = 3, ChurchMinEmpireCities = 6;
    private const float FreeCityTax = 0.1f, PawnYears = 3f;

    public static int Estate(Realm kingdom) => kingdom?.Record == null ? EstateNone : kingdom.GetOrCreate().hre_estate;
    public static bool IsFreeCity(Realm kingdom) => Estate(kingdom) == EstateFreeCity;
    public static bool IsChurchEstate(Realm kingdom) => Estate(kingdom) == EstateChurch;

    // 投票时算教会诸侯的：主教领，以及教宗国、教区
#if EC_PENDING
    public static bool IsEcclesiastical(Realm kingdom) =>
        IsChurchEstate(kingdom) || kingdom?.Record != null &&
        kingdom.GetKingdomType() is KingdomType.Feudalism_papal_state or KingdomType.Feudalism_diocese;

    private static void MembersYearly(Empire empire, HolyRomanState state)
    {
        RepairEstates(empire);
        TryCharterFreeCity(empire);
        TryEndowChurch(empire);
        FreeCityTaxes(empire);
        TryPawnFreeCity(empire);
    }
#endif

    // 旧档修补：自由城市、主教领以前税级被设成"无"(一分税收不上)，改回"低"(直接写选项：议会掌握征税权时
    // SetTaxLevel 会被否决，这是修补不是改税)；自由城市的市长被推举为皇帝、整座城成了中央的，中央不再算自由城市
#if EC_PENDING
    private static void RepairEstates(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        if (Estate(core) != EstateNone) core.GetOrCreate().hre_estate = EstateNone;
        foreach (Realm member in Members(empire))
        {
            if (Estate(member) == EstateNone) continue;
            Regime regime = member.GetRegime();
            if (regime?.options != null && regime.options.TryGetValue("option_tax_level", out int[] tax) && tax != null &&
                tax.Length > 0 && tax[0] == (int)TaxLevel.None)
                tax[0] = (int)TaxLevel.Low;
        }
    }
#endif

    #region 自由城市

#if EC_PENDING
    private static void TryCharterFreeCity(Empire empire)
    {
        List<Realm> members = Members(empire);
        int cap = Math.Max(MinFreeCityCap, members.Count / 3);
        if (members.Count(IsFreeCity) >= cap || EcRandom.value >= FreeCityChance) return;
        City city = FreeCityCandidate(empire);
        if (city != null) CharterFreeCity(empire, city);
    }

    public static Realm ActCharterFreeCity(Empire empire)
    {
        City city = HasDiet(empire) ? FreeCityCandidate(empire) : null;
        return city == null ? null : CharterFreeCity(empire, city);
    }
#endif

    // 候选：三城以上的普通成员国(含中央)里，人口最多的非都城
    private static City FreeCityCandidate(Empire empire) =>
        empire.kingdoms_list
            .Where(kingdom => kingdom?.Record != null && !kingdom.IsGone() && kingdom.GetEmpire() == empire &&
                              kingdom.Cities.Count >= 3 && Estate(kingdom) == EstateNone && !IsBanned(kingdom) &&
                              !kingdom.IsAtWarWith(empire.CoreKingdom))
            .SelectMany(kingdom => kingdom.Cities.Where(item => item?.Record != null && !item.IsGone() && item != kingdom.Capital &&
                                                                !EnfeoffmentHelper.IsRealmCapital(item)))
            .OrderByDescending(item => item.PopulationCount()).FirstOrDefault();

#if EC_PENDING
    public static Realm CharterFreeCity(Empire empire, City city)
    {
        Realm former = city?.Realm;
        if (former == null || former.GetEmpire() != empire || city == former.Capital) return null;
        Person leader = Burgher(city, former.Ruler);
        if (leader == null) return null;
        string cityName = city.GetCityName();
        Realm free = SpinOff(empire, city, leader, LeaderSelectMethod.Vote, EstateFreeCity);
        if (free == null) return null;
        Record(empire, string.Format(LM.Get("hre_free_city_history"), cityName, former.GetKingdomName()));
        return free;
    }
#endif

    // 市民领袖：商人优先，其次城中其他成年人，按威望
#if EC_PENDING
    private static Person Burgher(City city, Person exclude)
    {
        List<Person> locals = (city.People ?? new List<Person>()).ToList()
            .Where(actor => actor?.Record != null && actor != exclude && !actor.IsGone() && actor.Alive && actor.IsAdult() &&
                            !actor.IsRuler() && actor.IsFitToRule()).ToList();
        return locals.Where(actor => actor.GetOrCreate().socialClass == SocialClass.Merchant)
                   .OrderByDescending(actor => actor.Renown).FirstOrDefault()
               ?? locals.OrderByDescending(actor => actor.Renown).FirstOrDefault()
               ?? CityPopulationSystem.SpawnOfficial(city, city.Realm);
    }
#endif

    // 一城立为帝国成员：不带法理头衔、用城名，不世袭，不给中央出兵
#if EC_PENDING
    private static Realm SpinOff(Empire empire, City city, Person ruler, LeaderSelectMethod method, int estate)
    {
        if (ruler.City != city) ruler.JoinCity(city);
        // 立为帝国成员：受命立国，不许自立的闸放行(见 SecessionGate)
        Realm realm = SecessionGate.Sanctioned(() => city.makeOwnKingdom(ruler));
        if (realm?.Record == null) return null;
        realm.SetRegimeType(RegimeType.Feudalism);
        realm.LoadRegime();
        Regime regime = realm.GetRegime();
        if (regime != null)
        {
            regime.SetLeaderSelectMethod(method);
            regime.SetAllowSupportCenterArmy(false);
            regime.SetAllowDiplomacy(false);
            // 税级是本国向百姓收税的税率(见 KingdomExtension.GetTaxRate)，不是向帝国的贡赋：以前设成"无"，
            // 自由城市一分税收不上，年收从几十掉到 0、年年废债重组(玩家日志)。向帝国缴的是税款分成与每年一成(见 FreeCityTaxes)
            regime.SetTaxLevel(TaxLevel.Low);
            if (estate == EstateChurch) regime.SetReligionLevel(ReligionLevel.High);
        }
        if (realm.HasMainTitle()) realm.RemoveMainTitle();
        string name = city.GetCityName();
        realm.Name = name;
        realm.SetKingdomCoreName(name, name);
        realm.GetOrCreate().hre_estate = estate;
        realm.SetFiedTimestamp(GameClock.Now);
        empire.join(realm, true, true);
        return realm;
    }
#endif

    // 自由城市只给帝国缴税：本城税款照封建成员的分成交中央(见 TreasurySystem.TaxShares)，另每年缴一成年收入。
    // 这一成只从可用资金里出(扣掉安全储备)，不为它动用储备、举债
#if EC_PENDING
    private static void FreeCityTaxes(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        foreach (Realm free in Members(empire).Where(IsFreeCity))
        {
            if (free.IsAtWarWith(core)) continue;
            int amount = (int)Math.Min(StateSettlementSystem.DiscretionaryFunds(free),
                (long)(StateDebtSystem.AnnualStableIncome(free) * FreeCityTax));
            if (amount <= 0) continue;
            free.SubMoney(amount, TreasuryCategory.Tribute);
            core.AddMoney(amount, TreasuryCategory.Tribute);
        }
    }
#endif

    // 典押：中央国库不足一成年收时，把一座自由城市押给出得起钱(三年城市收入)的最富诸侯
#if EC_PENDING
    private static void TryPawnFreeCity(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        if (core.GetMoney() >= StateDebtSystem.AnnualStableIncome(core) / 10 || EcRandom.value >= PawnChance) return;
        PawnFreeCity(empire);
    }

    public static bool PawnFreeCity(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        foreach (Realm free in Members(empire).Where(IsFreeCity).OrderBy(item => item.Key))
        {
            City city = free.Capital;
            if (city == null) continue;
            int price = (int)Math.Max(100L, (long)(StateDebtSystem.AnnualStableIncome(free) * PawnYears));
            Realm buyer = Members(empire)
                .Where(member => Estate(member) == EstateNone && !IsBanned(member) && member.GetMoney() >= price &&
                                 !member.IsAtWarWith(core))
                .OrderByDescending(member => member.IsNeighbourWith(free))
                .ThenByDescending(member => member.GetMoney()).FirstOrDefault();
            if (buyer == null) continue;
            string cityName = city.GetCityName();
            buyer.SubMoney(price, TreasuryCategory.Other);
            core.AddMoney(price, TreasuryCategory.Other);
            foreach (City item in free.Cities.ToList()) item.JoinRealm(buyer);
            if (city.Realm == buyer) NotePawn(State(empire), city, buyer, price);
            Record(empire, string.Format(LM.Get("hre_pawn_history"), cityName, buyer.GetKingdomName(), MoneyDisplay.Format(price)));
            return true;
        }
        return false;
    }
#endif

    #endregion

    #region 教会诸侯

#if EC_PENDING
    private static void TryEndowChurch(Empire empire)
    {
        if (ChurchBlocker(empire) != null || EcRandom.value >= ChurchChance) return;
        City city = ChurchCandidate(empire);
        if (city != null) EndowChurch(empire, city);
    }
#endif

    // 封不出主教领的缘由(年度诊断也用)，能封时为 null。以前要中央五城以上——选帝制下中央就是当选者的自家领国，
    // 多半一到三城，从来封不出(玩家日志 0 次)。现在中央三城以上(封后还剩两城)、帝国合计六城以上即可；
    // 每四家成员至多一家主教领(至少一家、至多三家)
#if EC_PENDING
    public static string ChurchBlocker(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        if (!core.HasReligion()) return "中央没有国教";
        int coreCities = core.Cities?.Count ?? 0, total = empire.countCities();
        if (coreCities < ChurchMinCoreCities) return $"中央{coreCities}城，要{ChurchMinCoreCities}城";
        if (total < ChurchMinEmpireCities) return $"帝国共{total}城，要{ChurchMinEmpireCities}城";
        List<Realm> members = Members(empire);
        int cap = Mathf.Clamp(members.Count / 4, 1, MaxChurchPrinces), have = members.Count(IsChurchEstate);
        if (have >= cap) return $"已有{have}家，上限{cap}家";
        return ChurchCandidate(empire) == null ? "中央没有可封的非都城" : null;
    }

    public static Realm ActEndowChurch(Empire empire)
    {
        City city = HasDiet(empire) && empire.CoreKingdom.Cities.Count >= 2 ? ChurchCandidate(empire) : null;
        return city == null ? null : EndowChurch(empire, city);
    }
#endif

    // 中央的非都城里挑一座不大不小的(最小的那座留给中央)
    private static City ChurchCandidate(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        List<City> cities = core.Cities.Where(item => item?.Record != null && !item.IsGone() && item != core.Capital &&
                                                      !EnfeoffmentHelper.IsRealmCapital(item))
            .OrderBy(item => item.Zones?.Count ?? 0).ToList();
        return cities.Count > 1 ? cities[1] : cities.FirstOrDefault();
    }

#if EC_PENDING
    public static Realm EndowChurch(Empire empire, City city)
    {
        if (city?.Realm == null || city.Realm.GetEmpire() != empire || city == city.Realm.Capital) return null;
        Person bishop = ChooseBishop(empire, city, out string appointer);
        if (bishop == null) return null;
        string cityName = city.GetCityName();
        Realm church = SpinOff(empire, city, bishop, LeaderSelectMethod.Exam, EstateChurch);
        if (church == null) return null;
        if (!bishop.HasTrait("cleric")) bishop.AddTrait("cleric");
        Record(empire, string.Format(LM.Get("hre_church_endowed_history"), empire.Emperor?.FullName() ?? empire.CoreKingdom.GetKingdomName(),
            cityName, bishop.FullName()));
        return church;
    }
#endif

    // 主教出缺(EmpireCraftKingdomBehCheckKing 在主教领没有君主时调用)：按叙任权另派
#if EC_PENDING
    public static bool TryAppointBishop(Realm church)
    {
        try
        {
            if (!IsChurchEstate(church) || church.HasRuler() || church.Capital == null) return false;
            Empire empire = church.GetEmpire();
            if (empire == null)
            {
                // 离开帝国(独立、被并)后就是普通的国家
                church.GetOrCreate().hre_estate = EstateNone;
                return false;
            }
            Person bishop = ChooseBishop(empire, church.Capital, out string appointer);
            if (bishop == null) return false;
            if (bishop.City != church.Capital) bishop.JoinCity(church.Capital);
            GraceEdictService.Crown(church, bishop);
            if (church.Ruler != bishop) return false;
            if (!bishop.HasTrait("cleric")) bishop.AddTrait("cleric");
            Record(empire, string.Format(LM.Get("hre_bishop_appointed_history"), appointer, bishop.FullName(), church.GetKingdomName()));
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 任命主教失败: {exception.Message}");
            return false;
        }
    }
#endif

    // 人选：教会叙任由教廷派(教廷都城里的人)，君主叙任由皇帝派(帝都里的人，同族优先)，政教协约由本地推举；
    // 有教士特质的优先，再按威望
#if EC_PENDING
    private static Person ChooseBishop(Empire empire, City seat, out string appointer)
    {
        int investiture = PapacySystem.Applies(empire) ? PapacySystem.Investiture(empire) : PapacySystem.InvestitureCrown;
        Religion religion = PapacySystem.ReligionOf(empire);
        Realm papacy = PapacySystem.Papacy(religion);
        Person emperor = empire.Emperor;
        City source = seat;
        appointer = LM.Get("hre_appointer_chapter");
        if (investiture == PapacySystem.InvestitureChurch && papacy?.Capital != null)
        {
            source = papacy.Capital;
            appointer = PapacySystem.Pope(religion)?.FullName() ?? papacy.GetKingdomName();
        }
        else if (investiture == PapacySystem.InvestitureCrown && empire.CoreKingdom.Capital != null)
        {
            source = empire.CoreKingdom.Capital;
            appointer = emperor?.FullName() ?? empire.CoreKingdom.GetKingdomName();
        }
        Person pick = Candidates(source).OrderByDescending(actor => actor.HasTrait("cleric"))
            .ThenByDescending(actor => investiture == PapacySystem.InvestitureCrown && SameHouse(actor, emperor))
            .ThenByDescending(actor => actor.Renown).FirstOrDefault();
        if (pick == null && source != seat)
        {
            pick = Candidates(seat).OrderByDescending(actor => actor.HasTrait("cleric"))
                .ThenByDescending(actor => actor.Renown).FirstOrDefault();
            appointer = LM.Get("hre_appointer_chapter");
        }
        return pick ?? CityPopulationSystem.SpawnOfficial(seat, seat.Realm);
    }
#endif

    private static IEnumerable<Person> Candidates(City city) =>
        (city?.People ?? new List<Person>()).ToList().Where(actor => actor?.Record != null && !actor.IsGone() && actor.Alive &&
                                                                    actor.IsAdult() && !actor.IsRuler() && !actor.IsCityLeader() &&
                                                                    actor.IsFitToRule());

    // 选帝侯团的教会席位：主教领的主教优先(最多三席)
    public static List<long> ChurchElectors(Empire empire) =>
        Members(empire).Where(IsChurchEstate).Where(kingdom => kingdom.Ruler?.Record != null && !IsBanned(kingdom))
            .OrderByDescending(kingdom => kingdom.Cities.Count).Select(kingdom => kingdom.Ruler.Key).Take(MaxChurchPrinces).ToList();

    #endregion
}
}
