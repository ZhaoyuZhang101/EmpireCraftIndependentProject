// Generated from the EmpireCraft mod: Scripts/GeneralSystems/LandEconomySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Time = EmpireCraft.Core.FrameClock;
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public sealed class CityLandHolderView
{
    public string Name;
    public float Share;
    public bool Institutional;
}

public sealed class CityLandReport
{
    public bool MarketOpen;
    public bool LandlordClassEnabled;
    public bool MigrationBlocked;
    public float LandlessPopulationRatio;
    public List<CityLandHolderView> Holders = new List<CityLandHolderView>();
}

public sealed class ClanLandHoldingView
{
    public City City;
    public string CityName;
    public string KingdomName;
    public float Share;
    public int Rank;
}

public sealed class FamilyLandHoldingView
{
    public string CityName;
    public float OwnershipShare;
    public float TenureShare;
    public bool MarketOpen;
}

/// <summary>
/// 城市土地与家庭经济身份。土地数据归城市所有，因此没有建立帝国的王国也能运行。
/// 私有土地按宗族(SpecificClan)持有：原版家庭很不固定，往往传不了一代就散，份额跟着家庭走会不停地
/// 丢失、重分。没有宗族的人退回原版氏族，再没有才按个人持有。
/// </summary>
public static class LandEconomySystem
{
    public const float HouseholdBaseShare = 0.5f;
    public const float LandlordThreshold = 1f;
    public const float RebellionLandlessThreshold = 0.20f;
    public const float ContagionLandlessThreshold = 0.30f;
    private const int MerchantMinimumAnnualIncome = 40;
    private const int MerchantEntryYears = 2;
    private const int MerchantExitYears = 3;
    private const int LandPurchasePrice = 25;
    private const int RebellionCooldownYears = 10;
    // 受中灾、大灾的城起饥民起义，与土地起义共用的冷却减为五年(见 StartFamineRebellion)
    private const int DisasterRebellionCooldownYears = 5;

    public static void RecordIncome(Person actor, int amount)
    {
        if (actor == null || actor.IsGone() || amount <= 0) return;
        ActorExtension.ActorExtraData data = actor.GetOrCreate();
        data.economic_income_current_year = IncomeStatisticsMath.Add(data.economic_income_current_year, amount);
    }

    public static bool IsLandlord(Person actor)
    {
        if (actor?.City == null || !IsLandMarketOpen(actor.City.Realm)) return false;
        if (!IsLandlordClassEnabled(actor.City.Realm)) return false;
        return GetHouseholdShare(actor.City, GetHouseholdKey(actor)) > LandlordThreshold;
    }

    public static bool IsLandlessResident(Person actor) => actor?.City != null &&
        IsLandMarketOpen(actor.City.Realm) && IsAgrarianCommoner(actor) &&
        GetHouseholdShare(actor.City, GetHouseholdKey(actor)) <= 0f;

    // 土地买卖开放：本国开过(制度完成时逐国打开)，或本文化已完成开放土地买卖的制度——
    // 以前只打开制度完成那一刻在帝国里的国家，后来加入的成员国、同文化的独立王国永远停在贵族八成
    public static bool IsLandMarketOpen(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone()) return false;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (!IsLandlordClassEnabled(kingdom)) return false;
        if (!data.private_land_market_open && CultureOpensLandMarket(kingdom)) data.private_land_market_open = true;
        return data.private_land_market_open;
    }

    private static readonly Dictionary<string, (float at, bool open)> CultureLandMarket = new();

    private static bool CultureOpensLandMarket(Realm kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        string culture = empire != null ? InstitutionSystem.GetPrimaryCulture(empire) : TechnologySystem.GetCultureOf(kingdom);
        if (string.IsNullOrEmpty(culture)) return false;
        float now = Time.unscaledTime;
        if (CultureLandMarket.TryGetValue(culture, out var cached) && now >= cached.at && now - cached.at < 10f) return cached.open;
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
        bool open = state?.enacted_node_ids?.Any(id => InstitutionDefinitionRegistry.Get(id)?.effects_on_complete?
            .Any(effect => effect?.type == "open_private_land_market") == true) == true;
        CultureLandMarket[culture] = (now, open);
        return open;
    }

    public static bool IsLandlordClassEnabled(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone()) return false;
        if (kingdom.GetOrCreate().is_peasant_revolutionary_government) return true;
        Regime regime = kingdom.GetRegime();
        return regime != null && RegimeManager.AllowsLandlordClass(regime.type);
    }

#if EC_PENDING
    public static void OpenPrivateLandMarket(Realm kingdom, bool redistribute, bool revolutionary)
    {
        if (kingdom == null || kingdom.IsGone()) return;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        data.private_land_market_open = true;
        if (revolutionary) data.is_peasant_revolutionary_government = true;
        foreach (City city in kingdom.Cities?.Where(city => city != null && !city.IsGone()).ToList() ??
                                     new List<City>())
        {
            if (redistribute) RedistributeLand(city);
            else EnsureInitialHouseholdShares(city);
        }
        if (revolutionary) IdeologyPopulationSystem.TriggerLandRevolution(kingdom);
    }

    public static void RedistributeLand(City city)
    {
        if (city == null || city.IsGone()) return;
        CityExtension.CityExtraData data = EnsureData(city);
        data.household_land_shares.Clear();
        data.household_land_representatives.Clear();
        foreach (KeyValuePair<string, List<Person>> household in BuildHouseholds(city))
        {
            Person representative = SelectRepresentative(household.Value);
            if (representative == null || IsPrivilegedHousehold(household.Value)) continue;
            data.household_land_shares[household.Key] = GetInitialGrant(household.Value);
            data.household_land_representatives[household.Key] = representative.Key;
        }
        TrimPrivateSharesToCapacity(city, data);
        data.land_redistribution_pending = false;
        RefreshClasses(city);
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (population != null) population.background_landless = 0f;
    }

    public static void UpdateCity(City city)
    {
        if (city == null || city.IsGone() || city.Realm == null || city.Realm.IsGone()) return;
        CityExtension.CityExtraData data = EnsureData(city);
        double now = EmpireWorld.Current?.Now() ?? -1d;
        // 各城错峰(见 YearlyStagger)，免得全部城市在同一刻做年度结算
        if (data.last_land_economy_timestamp < 0d)
        {
            data.last_land_economy_timestamp = EmpireCraft.Core.YearlyStagger.Initial(now);
            return;
        }
        if (GameClock.YearsSince(data.last_land_economy_timestamp) < 1) return;
        data.last_land_economy_timestamp = EmpireCraft.Core.YearlyStagger.Next(now);

        // 各步分别累计计时(见 FrameProfiler)
        Dictionary<string, List<Person>> households;
        using (FrameProfiler.Measure("土地经济·分户")) households = BuildHouseholds(city);
        using (FrameProfiler.Measure("土地经济·商户")) UpdateMerchantHouseholds(households);
        if (IsLandMarketOpen(city.Realm))
            using (FrameProfiler.Measure("土地经济·土地买卖"))
            {
                if (data.land_redistribution_pending) RedistributeLand(city);
                ReleaseExtinctLand(city, data);
                EnsureInitialHouseholdShares(city, households);
                ProcessLandPurchases(city, households);
                TryMoveLandlessHousehold(city, households);
            }
        using (FrameProfiler.Measure("土地经济·阶层刷新")) RefreshClasses(city);
        using (FrameProfiler.Measure("土地经济·土改")) TryFoundingLandReform(city);
        using (FrameProfiler.Measure("土地经济·背景人口土地")) UpdateBackgroundLand(city);

        using (FrameProfiler.Measure("土地经济·农民起义"))
        {
            float landlessRatio = CalculateLandlessPopulationRatio(city);
            if (!TryJoinAdjacentPeasantRebellion(city, landlessRatio))
                TryStartPeasantLandRebellion(city, landlessRatio);
        }
    }
#endif

    public static CityLandReport GetReport(City city)
    {
        var report = new CityLandReport();
        if (city == null || city.IsGone() || city.Realm == null) return report;
        report.MarketOpen = IsLandMarketOpen(city.Realm);
        report.LandlordClassEnabled = IsLandlordClassEnabled(city.Realm);
        report.MigrationBlocked = city.Realm.GetOrCreate().fugitive_household_law_enacted ||
                                  city.Realm.HasLaw(LawType.逃人法);
        report.LandlessPopulationRatio = CalculateLandlessPopulationRatio(city);
        float crown = GetCrownShare(city);
        float nobles = GetNobleShare(city);
        if (crown > 0f)
            report.Holders.Add(new CityLandHolderView
                { Name = GetCrownName(city), Share = crown, Institutional = true });
        if (nobles > 0f)
            report.Holders.Add(new CityLandHolderView
                { Name = LM.Get("city_land_local_nobles"), Share = nobles, Institutional = true });
        float bannerLand = BannerSystem.LandShare(city);
        if (bannerLand > 0f)
            report.Holders.Add(new CityLandHolderView
                { Name = LM.Get("city_land_banner"), Share = bannerLand, Institutional = true });
        if (report.MarketOpen)
        {
            CityExtension.CityExtraData data = EnsureData(city);
            foreach (KeyValuePair<string, float> pair in data.household_land_shares
                         .Where(pair => pair.Value > 0f))
            {
                report.Holders.Add(new CityLandHolderView
                {
                    Name = ResolveHouseholdName(city, pair.Key),
                    Share = pair.Value,
                    Institutional = false
                });
            }
        }
        report.Holders = report.Holders.OrderByDescending(holder => holder.Share).Take(10).ToList();
        return report;
    }

#if EC_PENDING
    public static List<FamilyLandHoldingView> GetFamilyHoldings(Family family)
    {
        var result = new List<FamilyLandHoldingView>();
        if (family == null || EmpireWorld.Current?.Cities == null) return result;
        // 土地记在宗族名下：家庭窗口显示的是这个家庭所属宗族(没有宗族则氏族/个人)的持有
        List<Person> members = (family.units ?? new List<Person>()).Where(IsLivingResident).ToList();
        var householdKeys = new HashSet<string>(members.Select(GetHouseholdKey));
        var residenceCityIds = new HashSet<long>(members.Select(actor => actor.City?.Key ?? -1L).Where(id => id > 0));
        foreach (City city in EmpireWorld.Current.Cities.Where(city => city != null && !city.IsGone()))
        {
            CityExtension.CityExtraData data = EnsureData(city);
            bool resides = residenceCityIds.Contains(city.Key);
            float ownership = householdKeys.Sum(key =>
                data.household_land_shares.TryGetValue(key, out float share) ? Mathf.Max(0f, share) : 0f);
            bool marketOpen = IsLandMarketOpen(city.Realm);
            if (!resides && ownership <= 0f) continue;
            result.Add(new FamilyLandHoldingView
            {
                CityName = city.GetCityName(),
                OwnershipShare = marketOpen ? ownership : 0f,
                TenureShare = !marketOpen && resides ? HouseholdBaseShare : 0f,
                MarketOpen = marketOpen
            });
        }
        return result.OrderByDescending(view => view.OwnershipShare)
            .ThenByDescending(view => view.TenureShare).ThenBy(view => view.CityName).ToList();
    }
#endif

    // 宗族在各城的土地占有(只列有地的城市)，Rank 是在该城私人持地者里的名次
    public static List<ClanLandHoldingView> GetSpecificClanHoldings(SpecificClan clan)
    {
        var result = new List<ClanLandHoldingView>();
        if (clan == null || EmpireWorld.Current?.Cities == null) return result;
        string key = $"s:{clan.id}";
        foreach (City city in EmpireWorld.Current.Cities.Where(city => city != null && !city.IsGone()))
        {
            if (!IsLandMarketOpen(city.Realm)) continue;
            CityExtension.CityExtraData data = EnsureData(city);
            if (!data.household_land_shares.TryGetValue(key, out float share) || share <= 0f) continue;
            result.Add(new ClanLandHoldingView
            {
                City = city,
                CityName = city.GetCityName(),
                KingdomName = city.Realm?.GetKingdomName() ?? "",
                Share = share,
                Rank = 1 + data.household_land_shares.Count(pair => pair.Value > share)
            });
        }
        return result.OrderByDescending(view => view.Share).ThenBy(view => view.CityName).ToList();
    }

    // 分家时按迁出人数的比例把原宗族在该城的土地分给分支
    public static void TransferClanLand(City city, SpecificClan from, SpecificClan to, float ratio)
    {
        if (city == null || city.IsGone() || from == null || to == null || ratio <= 0f) return;
        CityExtension.CityExtraData data = EnsureData(city);
        string fromKey = $"s:{from.id}";
        string toKey = $"s:{to.id}";
        if (!data.household_land_shares.TryGetValue(fromKey, out float share) || share <= 0f) return;
        float moved = share * Mathf.Clamp01(ratio);
        data.household_land_shares[fromKey] = Mathf.Max(0f, share - moved);
        data.household_land_shares[toKey] = GetHouseholdShare(city, toKey) + moved;
        Person representative = city.People?.Where(IsLivingResident)
            .Where(actor => actor.GetSpecificClan() == to)
            .OrderByDescending(actor => actor.Money).FirstOrDefault();
        if (representative != null) data.household_land_representatives[toKey] = representative.Key;
    }

#if EC_PENDING
    public static SocialClass GetFamilyEconomicClass(Family family)
    {
        List<Person> members = family?.units?.Where(IsLivingResident).ToList() ?? new List<Person>();
        if (members.Any(IsLandlord)) return SocialClass.Landlord;
        if (members.Any(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)) return SocialClass.Noble;
        if (members.Any(actor => actor.GetOrCreate().is_economic_merchant)) return SocialClass.Merchant;
        if (members.Any(actor => actor.GetOrCreate().socialClass == SocialClass.Officer)) return SocialClass.Officer;
        if (members.Any(actor => actor.GetOrCreate().socialClass == SocialClass.Army)) return SocialClass.Army;
        if (members.Any(actor => actor.GetOrCreate().socialClass == SocialClass.Labour)) return SocialClass.Labour;
        return SocialClass.Peasant;
    }

    public static void MarkPeasantSocialRebellion(War war, Realm rebel, Realm origin, City originCity,
        float landlessRatio, string cause)
    {
        if (war == null || rebel == null || origin == null) return;
        WarExtension.WarExtraData warData = war.GetOrCreate();
        warData.peasant_land_rebellion = true;
        warData.peasant_land_rebellion_origin_kingdom_id = origin.Key;
        warData.peasant_land_rebellion_kingdom_id = rebel.Key;
        warData.peasant_land_rebellion_origin_city_id = originCity?.Key ?? rebel.Capital?.Key ?? -1L;
        warData.peasant_land_rebellion_cause = cause ?? "";
        warData.peasant_landless_ratio = Mathf.Clamp01(landlessRatio);
        DetachPeasantRebelFromOriginEmpire(rebel, origin);
        KingdomExtension.KingdomExtraData rebelData = rebel.GetOrCreate();
        rebelData.is_peasant_land_rebellion = true;
        rebelData.peasant_land_rebellion_war_id = war.Key;
        rebelData.peasant_land_rebellion_origin_kingdom_id = origin.Key;
    }

    private static void DetachPeasantRebelFromOriginEmpire(Realm rebel, Realm origin)
    {
        Empire originEmpire = origin?.GetEmpire();
        if (rebel == null || originEmpire == null || originEmpire.IsArchived() || originEmpire.IsGone()) return;
        if (rebel.GetEmpire() == originEmpire)
            originEmpire.leave(rebel, pRecalc: true, isLeave: true);
    }

    public static void ResolvePeasantLandRebellion(War war, WarOutcome winner)
    {
        if (war == null) return;
        WarExtension.WarExtraData warData = war.GetOrCreate();
        if (!warData.peasant_land_rebellion) return;
        Realm rebel = war.GetMainAttacker() ?? EmpireWorld.Current?.RealmByKey(warData.peasant_land_rebellion_kingdom_id);
        bool won = winner == WarOutcome.Attackers && rebel != null && !rebel.IsGone();
        if (won)
        {
            OpenPrivateLandMarket(rebel, redistribute: true, revolutionary: true);
            string victory = string.Format(LM.Get("land_rebellion_victory_history"), rebel.GetKingdomName());
            RecordRebellionHistory(rebel, victory);
            Realm origin = EmpireWorld.Current?.RealmByKey(warData.peasant_land_rebellion_origin_kingdom_id);
            Empire originEmpire = origin?.GetEmpire();
            if (originEmpire != null && originEmpire != rebel.GetEmpire())
                originEmpire.RecordHistory(directContent: victory, actorId: rebel.Ruler?.Key ?? -1L,
                    kingdomId: rebel.Key);
            EmpireCraft.Core.TranslateHelper.LogEventMessage(victory, rebel);
        }
        if (rebel != null)
        {
            KingdomExtension.KingdomExtraData rebelData = rebel.GetOrCreate();
            rebelData.is_peasant_land_rebellion = false;
            rebelData.is_famine_rebellion = false;
            rebelData.peasant_land_rebellion_war_id = -1L;
            rebelData.peasant_land_rebellion_origin_kingdom_id = -1L;
        }
    }
#endif

    private static CityExtension.CityExtraData EnsureData(City city)
    {
        CityExtension.CityExtraData data = city.GetOrCreate();
        data.household_land_shares ??= new Dictionary<string, float>();
        data.household_land_representatives ??= new Dictionary<string, long>();
        if (data.household_land_shares.Keys.Any(key => key.StartsWith(LegacyFamilyKeyPrefix, StringComparison.Ordinal)))
            MigrateLegacyFamilyKeys(data);
        return data;
    }

    // 旧存档按家庭(f:)记的份额：按当年记下的代表人并入他现在所属的宗族；代表人已不在的份额作废，
    // 由下一次年度结算按宗族重新分配。
    private const string LegacyFamilyKeyPrefix = "f:";

    private static void MigrateLegacyFamilyKeys(CityExtension.CityExtraData data)
    {
        foreach (string key in data.household_land_shares.Keys
                     .Where(key => key.StartsWith(LegacyFamilyKeyPrefix, StringComparison.Ordinal)).ToList())
        {
            float share = data.household_land_shares[key];
            Person representative = ResolveRepresentative(data, key);
            data.household_land_shares.Remove(key);
            data.household_land_representatives.Remove(key);
            if (!IsLivingResident(representative)) continue;
            string newKey = GetHouseholdKey(representative);
            data.household_land_shares[newKey] =
                (data.household_land_shares.TryGetValue(newKey, out float existing) ? existing : 0f) + share;
            if (!data.household_land_representatives.ContainsKey(newKey))
                data.household_land_representatives[newKey] = representative.Key;
        }
    }

    private static Dictionary<string, List<Person>> BuildHouseholds(City city)
    {
        var result = new Dictionary<string, List<Person>>();
        foreach (Person actor in city.People?.Where(IsLivingResident).ToList() ?? new List<Person>())
        {
            string key = GetHouseholdKey(actor);
            if (!result.TryGetValue(key, out List<Person> members))
            {
                members = new List<Person>();
                result[key] = members;
            }
            members.Add(actor);
        }
        return result;
    }

    private static bool IsLivingResident(Person actor)
    {
        return actor != null && !actor.IsGone() && actor.Alive;
    }

    // 持地单位：宗族 → 原版氏族 → 个人
    private static string GetHouseholdKey(Person actor)
    {
        SpecificClan specificClan = actor?.GetSpecificClan();
        if (specificClan != null) return $"s:{specificClan.id}";
        if (actor?.HasClan() == true) return $"c:{actor.Clan.Key}";
        return $"a:{actor?.Key ?? -1L}";
    }

    // 初始授田按宗族在本城的"户数"(家庭数 + 不在家庭里的个人)算，每户一份——
    // 换成按宗族持有之后全城授出去的土地总量跟原来按家庭授田时一样，只是归到了宗族名下。
#if EC_PENDING
    private static float GetInitialGrant(IEnumerable<Person> members)
    {
        int households = members.Where(IsLivingResident)
            .Select(actor => actor.hasFamily() ? $"f:{actor.family.id}" : $"a:{actor.Key}")
            .Distinct().Count();
        return HouseholdBaseShare * Math.Max(1, households);
    }
#endif

    private static Person SelectRepresentative(IEnumerable<Person> members)
    {
        return members?.Where(IsLivingResident).OrderByDescending(actor => actor.Money)
            .ThenByDescending(actor => actor.Renown).FirstOrDefault();
    }

    // 宗族人多，原来"有一个人是官员/贵族就整户不授田"会让一个大宗族因为出了一个官就全族无地，
    // 改成过半成员是特权阶层才整族不授田(这部分由贵族份额覆盖)。
    private static bool IsPrivilegedHousehold(IEnumerable<Person> members)
    {
        List<Person> list = members.ToList();
        if (list.Count == 0) return false;
        int privileged = list.Count(actor => actor.IsOnOffice() || actor == actor.Realm?.Ruler ||
                                             actor.GetOrCreate().socialClass == SocialClass.Noble);
        return privileged * 2 > list.Count;
    }

    private static void UpdateMerchantHouseholds(Dictionary<string, List<Person>> households)
    {
        // Read each actor's income once; reuse totals for the median and qualification.
        var totals = new List<(List<Person> Members, long Income)>(households.Count);
        foreach (List<Person> members in households.Values)
        {
            if (members == null || members.Count == 0) continue;
            long income = 0;
            foreach (Person actor in members)
                income = IncomeStatisticsMath.Add(income, actor.GetOrCreate().economic_income_current_year);
            totals.Add((members, income));
        }
        List<long> incomes = totals.Select(entry => entry.Income).OrderBy(value => value).ToList();
        long median = incomes.Count == 0 ? 0 : incomes[incomes.Count / 2];
        long threshold = IncomeStatisticsMath.MerchantThreshold(median, MerchantMinimumAnnualIncome);
        foreach (var entry in totals)
        {
            List<Person> members = entry.Members;
            long income = entry.Income;
            Person representative = SelectRepresentative(members);
            bool merchant = members.Any(actor => actor.GetOrCreate().is_economic_merchant);
            int highYears = members.Max(actor => actor.GetOrCreate().merchant_high_income_years);
            int lowYears = members.Max(actor => actor.GetOrCreate().merchant_low_income_years);
            if (income >= threshold)
            {
                highYears = Math.Min(MerchantEntryYears - 1, Math.Max(0, highYears)) + 1;
                lowYears = 0;
                if (highYears >= MerchantEntryYears) merchant = true;
            }
            else
            {
                lowYears = Math.Min(MerchantExitYears - 1, Math.Max(0, lowYears)) + 1;
                highYears = 0;
                if (lowYears >= MerchantExitYears) merchant = false;
            }
            foreach (Person actor in members)
            {
                ActorExtension.ActorExtraData data = actor.GetOrCreate();
                data.economic_income_previous_year = Math.Max(0L, data.economic_income_current_year);
                data.economic_income_current_year = 0;
                data.merchant_high_income_years = highYears;
                data.merchant_low_income_years = lowYears;
                data.is_economic_merchant = merchant;
            }
            if (representative != null && merchant) representative.GetOrCreate().is_economic_merchant = true;
        }
    }

    // 绝户田：宗族在世族人都没了，它在本城的私田归同一世系在本城有田的一支(田多的先承)，没有就归上头还有人的本宗，
    // 都没有就退给背景农民(私田里实体家户没占的归背景人口，见 BackgroundLandPool)。以前绝户的份额一直挂着
    // (玩家 10-30 存档：有私田的 710 族里在世的只有 37 族)，活着的宗族、世家没地可置
    private static void ReleaseExtinctLand(City city, CityExtension.CityExtraData data)
    {
        List<(string key, SpecificClan clan)> extinct = null;
        var landed = new List<(SpecificClan clan, float share)>();
        foreach (KeyValuePair<string, float> pair in data.household_land_shares)
        {
            if (!pair.Key.StartsWith("s:", StringComparison.Ordinal) || !long.TryParse(pair.Key.Substring(2), out long clanId)) continue;
            SpecificClan clan = SpecificClanManager.Get(clanId);
            // 世家谱上的人死尽而族众还在的，下一轮门第结算由远支入谱为宗子(见 GentrySystem.Revive)，田不放
            if (clan != null && (clan.Count > 0 || GentrySystem.KeepsLand(clan)))
            {
                if (pair.Value > 0f) landed.Add((clan, pair.Value));
                continue;
            }
            (extinct ??= new List<(string, SpecificClan)>()).Add((pair.Key, clan));
        }
        if (extinct == null) return;
        foreach ((string key, SpecificClan clan) in extinct)
        {
            float share = data.household_land_shares[key];
            data.household_land_shares.Remove(key);
            data.household_land_representatives.Remove(key);
            if (share <= 0f || clan == null) continue;
            SpecificClan heir = landed.Where(holder => SpecificClanManager.SameLineage(holder.clan, clan))
                .OrderByDescending(holder => holder.share).Select(holder => holder.clan).FirstOrDefault();
            var visited = new HashSet<long> { clan.id };
            for (SpecificClan parent = clan.parent_clan_id >= 0 ? SpecificClanManager.Get(clan.parent_clan_id) : null;
                 heir == null && parent != null && visited.Add(parent.id);
                 parent = parent.parent_clan_id >= 0 ? SpecificClanManager.Get(parent.parent_clan_id) : null)
                if (parent.Count > 0) heir = parent;
            if (heir == null) continue;
            string heirKey = $"s:{heir.id}";
            data.household_land_shares[heirKey] = GetHouseholdShare(city, heirKey) + share;
        }
    }

#if EC_PENDING
    private static void EnsureInitialHouseholdShares(City city,
        Dictionary<string, List<Person>> households = null)
    {
        if (!IsLandMarketOpen(city?.Realm)) return;
        CityExtension.CityExtraData data = EnsureData(city);
        households ??= BuildHouseholds(city);
        float capacity = GetPrivateLandCapacity(city);
        float used = data.household_land_shares.Values.Where(value => value > 0f).Sum();
        foreach (KeyValuePair<string, List<Person>> household in households)
        {
            Person representative = SelectRepresentative(household.Value);
            if (representative == null || IsPrivilegedHousehold(household.Value)) continue;
            data.household_land_representatives[household.Key] = representative.Key;
            if (data.household_land_shares.ContainsKey(household.Key)) continue;
            // 余量不够整份时给剩下的零头，不够一点点就记 0(无地)
            float grant = Mathf.Min(GetInitialGrant(household.Value), capacity - used);
            if (grant <= 0.001f)
            {
                data.household_land_shares[household.Key] = 0f;
                continue;
            }
            data.household_land_shares[household.Key] = grant;
            used += grant;
        }
        TrimPrivateSharesToCapacity(city, data);
    }

    private static void ProcessLandPurchases(City city, Dictionary<string, List<Person>> households)
    {
        CityExtension.CityExtraData data = EnsureData(city);
        // 有收入不代表不缺钱：原先要求全族每个人上一年收入都为零，正常耕作的氏族几乎永远不会卖地。
        // 按背景人口相同的基础兼并速度，让非商户持地氏族每年有小概率出售一份地。
        float saleChance = Mathf.Clamp01(BaseConcentration * MaladySystem.AnnexationFactor(city.Realm));
        List<string> sellers = households.Where(pair => GetHouseholdShare(city, pair.Key) > 0f &&
                                                        !IsPrivilegedHousehold(pair.Value) &&
                                                        !pair.Value.Any(actor => actor.GetOrCreate().is_economic_merchant) &&
                                                        EcRandom.value < saleChance)
            .OrderBy(pair => SelectRepresentative(pair.Value)?.Money ?? int.MaxValue)
            .Select(pair => pair.Key).ToList();
        List<int> purses = households.Values.Select(SelectRepresentative).Where(actor => actor != null)
            .Select(actor => actor.Money).OrderBy(money => money).ToList();
        int medianPurse = purses.Count > 0 ? purses[(purses.Count - 1) / 2] : 0;
        foreach (KeyValuePair<string, List<Person>> buyerHousehold in households
                     .Where(pair => pair.Value.Any(actor => actor.GetOrCreate().is_economic_merchant) ||
                                    (SelectRepresentative(pair.Value)?.Money ?? 0) >= LandPurchasePrice * 2 &&
                                    (long)(SelectRepresentative(pair.Value)?.Money ?? 0) >=
                                    (long)medianPurse + LandPurchasePrice)
                     .OrderByDescending(pair => SelectRepresentative(pair.Value)?.Money ?? 0))
        {
            Person buyer = SelectRepresentative(buyerHousehold.Value);
            if (buyer == null || buyer.Money <= 0) continue;
            int sellerIndex = sellers.FindIndex(key => key != buyerHousehold.Key &&
                ResolveRepresentative(data, key) is Person seller && IsLivingResident(seller) &&
                buyer.Money > seller.Money);
            if (sellerIndex < 0)
            {
                // 无小人模式：实体卖家卖完了，向背景人口里的有地农民买(同一片地，两本账合一)
                // 富户参与实体土地交易；背景购地仍只由商户出资，避免大量富户每年直接买空背景私田。
                if (buyerHousehold.Value.Any(actor => actor.GetOrCreate().is_economic_merchant))
                    BuyFromBackground(city, data, buyer, buyerHousehold.Key);
                continue;
            }
            string sellerKey = sellers[sellerIndex];
            sellers.RemoveAt(sellerIndex);
            Person seller = ResolveRepresentative(data, sellerKey);
            float amount = Math.Min(HouseholdBaseShare, GetHouseholdShare(city, sellerKey));
            if (amount <= 0f || !IsLivingResident(seller) || sellerKey == buyerHousehold.Key) continue;
            int price = Mathf.CeilToInt(LandPurchasePrice * (amount / HouseholdBaseShare));
            if (!ActorMoneyTransfers.TryTransferNonIncome(buyer, seller, price)) continue;
            data.household_land_shares[sellerKey] = Mathf.Max(0f,
                GetHouseholdShare(city, sellerKey) - amount);
            data.household_land_shares[buyerHousehold.Key] =
                GetHouseholdShare(city, buyerHousehold.Key) + amount;
            data.household_land_representatives[buyerHousehold.Key] = buyer.Key;
        }
    }
#endif

    // 实体商户向背景农民买一份地：按背景地价付钱，背景农民的有地部分减少、无地比例上升
#if EC_PENDING
    private static bool BuyFromBackground(City city, CityExtension.CityExtraData data, Person buyer, string key)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return false;
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (population == null) return false;
        float pool = BackgroundLandPool(city, data);
        float owned = pool * (1f - Mathf.Clamp01(population.background_landless));
        float amount = HouseholdBaseShare;
        if (owned < amount || pool - amount < 0.01f) return false;
        int price = Mathf.CeilToInt(amount * BackgroundLandPrice);
        if (buyer.Money < price) return false;
        buyer.AddMoney(-price);
        population.background_landless = Mathf.Clamp01(1f - (owned - amount) / (pool - amount));
        data.household_land_shares[key] = GetHouseholdShare(city, key) + amount;
        data.household_land_representatives[key] = buyer.Key;
        ReceiveLandSale(city, population, price);
        return true;
    }
#endif

    // 世家兼并(见 GentrySystem.Annex)：从背景农民有地的那部分划 amount 份归宗族，背景无地比例随之上升。
    // 投献、强占，不付地价；返回实际划过来的份数
#if EC_PENDING
    public static float AnnexForClan(City city, SpecificClan clan, float amount)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city == null || city.IsGone() || clan == null || amount <= 0f ||
            !IsLandMarketOpen(city.Realm)) return 0f;
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (population == null) return 0f;
        CityExtension.CityExtraData data = EnsureData(city);
        float pool = BackgroundLandPool(city, data);
        float owned = pool * (1f - Mathf.Clamp01(population.background_landless));
        amount = Mathf.Min(amount, owned, pool - 0.01f);
        if (amount < 0.01f) return 0f;
        population.background_landless = Mathf.Clamp01(1f - (owned - amount) / (pool - amount));
        string key = $"s:{clan.id}";
        data.household_land_shares[key] = GetHouseholdShare(city, key) + amount;
        return amount;
    }
#endif

    // 背景人口的私田：私有土地里实体家户没占的部分(实体家户占的多了，背景农民的地就少了)
    private static float BackgroundLandPool(City city, CityExtension.CityExtraData data) =>
        Mathf.Max(0f, GetPrivateLandCapacity(city) - data.household_land_shares.Values.Where(value => value > 0f).Sum());

    // 背景人口的进账按税率交进城市国库(卖地的钱、商人利润)，返回应交的税
#if EC_PENDING
    private static float PayBackgroundTax(City city, CityPopulationData population, float paid, TreasuryCategory category)
    {
        if (city.Realm == null || paid <= 0f) return 0f;
        float due = paid * (float)SectorTaxRules.Rate(city.Realm.GetTaxRate(), category);
        population.sector_tax_carry ??= new();
        string key = "transaction_" + category;
        population.sector_tax_carry.TryGetValue(key, out double carry);
        int whole = SectorTaxRules.Assess(paid, city.Realm.GetTaxRate(), category, ref carry);
        population.sector_tax_carry[key] = carry;
        if (whole > 0) TreasurySystem.CollectResidentTax(city, whole, category);
        if (whole > 0 && CityPopulationSystem.AbstractPopulationEnabled) population.other_background_tax += whole;
        return due;
    }
#endif

    // 卖地的农民拿到钱，交过税存进民间存款
#if EC_PENDING
    private static void ReceiveLandSale(City city, CityPopulationData population, float paid)
    {
        float tax = PayBackgroundTax(city, population, paid, TreasuryCategory.LandTax);
        PopulationEconomySystem.AddSavings(city, population, paid - tax);
    }

    private static void TryMoveLandlessHousehold(City city, Dictionary<string, List<Person>> households)
    {
        if (!IsLandMarketOpen(city?.Realm)) return;
        if (city.Realm.GetOrCreate().fugitive_household_law_enacted || city.Realm.HasLaw(LawType.逃人法)) return;
        CityExtension.CityExtraData data = EnsureData(city);
        if (data.household_land_shares.Values.Sum() + HouseholdBaseShare <= GetPrivateLandCapacity(city)) return;
        KeyValuePair<string, List<Person>> landless = households.FirstOrDefault(pair =>
            GetHouseholdShare(city, pair.Key) <= 0f && !IsPrivilegedHousehold(pair.Value));
        if (string.IsNullOrEmpty(landless.Key)) return;
        City destination = city.Realm.Cities?.Where(candidate => candidate != null && candidate != city &&
                !candidate.IsGone() && IsLandMarketOpen(candidate.Realm) &&
                EnsureData(candidate).household_land_shares.Values.Sum() + HouseholdBaseShare <=
                GetPrivateLandCapacity(candidate))
            .OrderBy(candidate => Vector2.SqrMagnitude(candidate.Center - city.Center)).FirstOrDefault();
        if (destination == null) return;
        foreach (Person actor in landless.Value.ToList()) actor.JoinCity(destination);
    }

    private static void RefreshClasses(City city)
    {
        foreach (Person actor in city.People?.Where(IsLivingResident).ToList() ?? new List<Person>())
            actor.SetSocialClass(ActorAI.EmpireCaftActorJudgeClass.JudgeClass(actor));
    }
#endif

    // 城市无地农民占人口的比例(土地买卖未开放时为 0)，供理念传播等使用
    public static float GetLandlessRatio(City city) => CalculateLandlessPopulationRatio(city);

    // 无地比例按城缓存两秒：城市忠诚、稳定、起义判定每帧都要问，逐人算地权不便宜
    private static readonly Dictionary<long, (float at, float ratio)> LandlessCache = new();

    private static float CalculateLandlessPopulationRatio(City city)
    {
        if (city == null || !IsLandMarketOpen(city.Realm)) return 0f;
        float now = Time.unscaledTime;
        if (LandlessCache.TryGetValue(city.Key, out var cached) && now >= cached.at && now - cached.at < 2f) return cached.ratio;
        if (LandlessCache.Count > 4000) LandlessCache.Clear();
        float ratio = ComputeLandlessRatio(city);
        LandlessCache[city.Key] = (now, ratio);
        return ratio;
    }

    private static float ComputeLandlessRatio(City city)
    {
        List<Person> population = city.People?.Where(IsLivingResident).ToList() ?? new List<Person>();
        float landless = population.Count(actor => IsAgrarianCommoner(actor) &&
                                                   GetHouseholdShare(city, GetHouseholdKey(actor)) <= 0f);
        float total = population.Count;
        // 无小人模式：背景人口里的无地农民
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            CityPopulationData data = CityPopulationSystem.Get(city);
            total += CityPopulationSystem.GetBackgroundTotal(city);
            if (data != null) landless += BackgroundPeasants(data) * data.background_landless;
        }
        return total <= 0f ? 0f : landless / total;
    }

    // ---- 背景人口的土地(无小人模式) ----
    // 背景人口没有一户一户的地权，按城汇总一个"农民无地比例"，每年演变：
    //   兼并：每年 BaseConcentration 比例的有地农民想卖地；商人、地主越多越快；饥荒(卖地求活)、苛政再加速；
    //     买地要花钱，钱全部来自真实的市场买卖(见 AddBackgroundIncome)，钱不够就只买得起一部分；
    //     卖地求活(饥荒、苛政)是贱卖，半价；卖地的钱农民在城里花掉，照常交税；
    //     背景农民的地 = 私有土地里实体家户没占的部分，实体商户也能向背景农民买地(见 BuyFromBackground)；
    //   人口减少：死了人，空出的地回到幸存者手里，无地比例按人口减少的幅度回落；
    //   均田(开国均田、土改、农民革命政权)：归零(见 RedistributeLand)。
    private const float BaseConcentration = 0.02f;
    private const float FamineConcentration = 0.05f;
    // 背景土地的价钱：每 1% 城市土地
    private const float BackgroundLandPrice = 10f;
    // 税后收入拿来买地的比例；无地农民种别人的地，收成交租的比例
    private const float BuyerSavingShare = 0.8f;
    private const float RentRate = 0.5f;

    private static float BackgroundBuyers(CityPopulationData data)
    {
        float buyers = 0f;
        foreach (PopGroup group in data.groups)
            if (group.social_class is SocialClass.Merchant or SocialClass.Landlord) buyers += group.Background;
        return buyers;
    }

    // 卖粮收入里归地主的比例：无地农民的收成一半交租，地主把租粮拿到市场上卖。
    // 城里没有背景商人、地主(地都在实体家户手里)就不算
    public static float BackgroundRentShare(City city)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || !IsLandMarketOpen(city?.Realm)) return 0f;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null || BackgroundBuyers(data) < 1f) return 0f;
        return Mathf.Clamp01(data.background_landless) * RentRate;
    }

    // 背景商人、地主的真实收入：市场买卖里商人赚的一成利润(城里没有背景商人就是损耗)，加上卖租粮的钱。
    // 利润按税率交税；租粮在收割时已经算进产出交过税(见 PopulationEconomySystem.PayTaxes)，不再交。
    // 大部分攒成买地钱，最多攒够买下全城私田
#if EC_PENDING
    public static void AddBackgroundIncome(City city, float margin, float rent)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city == null) return;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data?.groups == null) return;
        float merchants = 0f;
        foreach (PopGroup group in data.groups)
            if (group.social_class == SocialClass.Merchant) merchants += group.Background;
        // 货是按九成价卖的，民间家底少了这一成；城里有背景商人，这一成是他们赚的，还在城里
        if (merchants < 1f)
        {
            PopulationEconomySystem.AddSavings(city, data, -margin);
            margin = 0f;
        }
        if (margin + rent <= 0f) return;
        float tax = PayBackgroundTax(city, data, margin, TreasuryCategory.CommercialTax);
        float before = Mathf.Max(0f, data.background_land_fund);
        data.background_land_fund = Mathf.Min(BackgroundLandPrice * 100f,
            before + (margin - tax + rent) * BuyerSavingShare);
        // 交的税和攒去买地的钱从民间存款里出
        PopulationEconomySystem.AddSavings(city, data, -(tax + data.background_land_fund - before));
    }
#endif

    private static float BackgroundPeasants(CityPopulationData data)
    {
        float peasants = 0f;
        foreach (PopGroup group in data.groups)
            if (group.social_class.IsPeasantry()) peasants += group.Background;
        return peasants;
    }

#if EC_PENDING
    private static void UpdateBackgroundLand(City city)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled) return;
        CityPopulationData data = CityPopulationSystem.Get(city);
        if (data == null) return;
        float peasants = BackgroundPeasants(data);
        if (!IsLandMarketOpen(city.Realm) || city.Realm.GetOrCreate().is_peasant_revolutionary_government)
        {
            data.background_landless = 0f;
            data.background_peasants_last = peasants;
            return;
        }
        float landless = Mathf.Clamp01(data.background_landless);
        // 人口减少：空出的地回到幸存者手里
        if (data.background_peasants_last > 0f && peasants < data.background_peasants_last)
            landless *= peasants / data.background_peasants_last;
        float total = Mathf.Max(1f, CityPopulationSystem.GetBackgroundTotal(city));
        float buyers = BackgroundBuyers(data);
        CityExtension.CityExtraData landData = EnsureData(city);
        float pool = BackgroundLandPool(city, landData);
        float owned = pool * (1f - landless);
        if (pool >= 0.01f && owned > 0f)
        {
            // 想卖的地：商人、地主占比越高越多；饥荒、苛政逼人贱卖
            // 土地兼并的积弊越重，卖地越快(见 MaladySystem)
            float voluntary = owned * BaseConcentration * (1f + 4f * buyers / total) * MaladySystem.AnnexationFactor(city.Realm);
            float distress = owned * Mathf.Min(1f, (data.last_food_shortage > 0f ? FamineConcentration : 0f) +
                Mathf.Min(0.03f, HarshRuleSystem.GetRealmBurden(city.Realm) / 2000f));
            // 先买便宜的贱卖地
            float cheap = BackgroundLandPrice * 0.5f;
            float boughtDistress = Mathf.Min(distress, data.background_land_fund / cheap);
            float paid = boughtDistress * cheap;
            float boughtVoluntary = Mathf.Min(voluntary, (data.background_land_fund - paid) / BackgroundLandPrice);
            paid += boughtVoluntary * BackgroundLandPrice;
            data.background_land_fund = Mathf.Max(0f, data.background_land_fund - paid);
            landless = 1f - Mathf.Max(0f, owned - boughtDistress - boughtVoluntary) / pool;
            ReceiveLandSale(city, data, paid);
        }
        data.background_landless = Mathf.Clamp01(landless);
        data.background_peasants_last = peasants;
    }
#endif

    private static bool IsAgrarianCommoner(Person actor)
    {
        if (actor == null || actor.IsOnOffice() || actor.IsWarrior() ||
            actor.GetOrCreate().is_economic_merchant || IsLandlord(actor) ||
            UrbanEmploymentSystem.IsEmployed(actor)) return false;
        if (actor.GetOrCreate().socialClass == SocialClass.Noble) return false;
        string job = actor.Job?.Id ?? "";
        return job != "woodcutter" && job != "miner" && job != "miner_deposit" &&
               job != "road_builder" && job != "cleaner" && job != "manure_cleaner";
    }

    // 开国均田：新帝国立国(开国气象期)或改朝换代后开国君主在位期间，辖下每座城(含后来打下、归附的城)
    // 平均一次地权，此后 RebellionCooldownYears 年内不再爆发农民起义。
    // 每朝每城只均一次(land_reform_key)；土地买卖未开放的城没有土地兼并，只记下标记与冷却。
#if EC_PENDING
    private static void TryFoundingLandReform(City city)
    {
        Empire empire = city.Realm?.GetEmpire();
        if (empire == null || empire.IsGone() || empire.IsArchived()) return;
        long founderId = empire.data.dynasty_founder_actor_id;
        bool founderReigning = founderId > 0 && empire.Emperor?.Key == founderId;
        if (!empire.InFoundingGrace && !founderReigning) return;
        string key = empire.Key + ":" + founderId;
        CityExtension.CityExtraData data = EnsureData(city);
        if (data.land_reform_key == key) return;
        data.land_reform_key = key;
        if (IsLandMarketOpen(city.Realm)) RedistributeLand(city);
        data.last_land_rebellion_timestamp = GameClock.Now;
    }
#endif

    // 一个政权(在帝国里就按整个帝国)各城无地农民比例的平均，按年缓存
    private static readonly Dictionary<long, (double time, float ratio)> RealmLandlessCache = new();

    public static float GetRealmLandlessRatio(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone()) return 0f;
        Empire empire = kingdom.GetEmpire();
        long key = empire != null && !empire.IsGone() ? -empire.Key : kingdom.Key;
        double now = EmpireWorld.Current?.Now() ?? 0d;
        if (RealmLandlessCache.TryGetValue(key, out var cached) && GameClock.YearsSince(cached.time) < 1)
            return cached.ratio;
        List<City> cities = empire != null && !empire.IsGone() ? empire.AllCities() : kingdom.Cities;
        float sum = 0f;
        int count = 0;
        foreach (City city in cities)
        {
            if (city == null || city.IsGone()) continue;
            sum += CalculateLandlessPopulationRatio(city);
            count++;
        }
        float ratio = count > 0 ? sum / count : 0f;
        RealmLandlessCache[key] = (now, ratio);
        return ratio;
    }

    // 民心归附：本城土地兼并严重(无地农民 >= RebellionLandlessThreshold)，而来攻的政权兼并轻得多
    // (平均无地率至少低 SurrenderLandlessGap)时，百姓望风归降，占领进度大幅加快(见 CaptureSpeedPatch)
    public const float SurrenderLandlessGap = 0.10f;

    public static bool WouldSurrenderTo(City city, Realm attacker, out float cityRatio)
    {
        cityRatio = 0f;
        if (city?.Realm == null || attacker == null || attacker.IsGone() || attacker == city.Realm) return false;
        cityRatio = CalculateLandlessPopulationRatio(city);
        if (cityRatio < RebellionLandlessThreshold) return false;
        return GetRealmLandlessRatio(attacker) <= cityRatio - SurrenderLandlessGap;
    }

    // 起义门槛随苛政降低(见 HarshRuleSystem)：苛政 60 时无地一成二即可起义，最低一成二
    public static float EffectiveRebellionThreshold(City city) =>
        RebellionLandlessThreshold - Mathf.Min(0.08f, HarshRuleSystem.GetRealmBurden(city?.Realm) / 750f);

    // 苛政民变的城市土地兼并严重时，改为农民土地起义(见 HarshRuleSystem)
#if EC_PENDING
    public static bool TryStartPeasantLandRebellion(City city) =>
        city != null && !city.IsGone() && TryStartPeasantLandRebellion(city, CalculateLandlessPopulationRatio(city));

    internal static bool CanStartPeasantLandUprising(City city, float ratio) => city != null && !city.IsGone() &&
        city.Realm != null && !city.Realm.IsGone() && city != city.Realm.Capital &&
        RebellionSystem.CanAttempt(city.Realm) &&
        CityStabilitySystem.CanRise(city) &&
        ratio >= EffectiveRebellionThreshold(city) &&
        (!city.Realm.GetWars().Any() || DynasticCycleSystem.InCrisis(city.Realm.GetEmpire()));

    private static bool TryStartPeasantLandRebellion(City city, float ratio)
    {
        if (!CanStartPeasantLandUprising(city, ratio)) return false;
        if (!ModernStability.PassRebellionGate(city.Realm)) return false;
        CityExtension.CityExtraData data = EnsureData(city);
        if (data.last_land_rebellion_timestamp >= 0d &&
            GameClock.YearsSince(data.last_land_rebellion_timestamp) < RebellionCooldownYears) return false;
        Person leader = city.People?.Where(actor => IsLivingResident(actor) && IsAgrarianCommoner(actor) &&
                                                  actor.CanFoundCivKingdom())
            .OrderByDescending(actor => actor.Renown).FirstOrDefault();
        // 无小人模式：城里没有农民实体，从背景人口里生成带头的人
        leader ??= CityPopulationSystem.SpawnRebelLeader(city);
        if (leader == null) return false;
        Realm origin = city.Realm;
        RebellionCauseData rebellionCause = RebellionSystem.Capture(origin, city, "rebellion_reason_land",
            string.Format(LM.Get("land_rebellion_cause"), city.GetCityName(), ratio * 100f));
        Realm rebel = city.MakeOwnRealm(leader, rebellion: true);
        if (rebel == null) return false;
        if (!rebel.StartLocalRebelling(EmpireWarType.地方叛乱))
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return false;
        }
        rebel.Name = string.Format(LM.Get("land_rebel_kingdom_name"), city.GetCityName());
        War war = EmpireWorld.Current.StartWar(rebel, origin, WarKinds.Rebellion);
        if (war == null)
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return false;
        }
        war.SetEmpireWarType(EmpireWarType.地方叛乱);
        string cause = string.Format(LM.Get("land_rebellion_cause"), city.GetCityName(), ratio * 100f);
        war.Name = LM.Get("land_rebellion_war_name");
        MarkPeasantSocialRebellion(war, rebel, origin, city, ratio, cause);
        RebellionSystem.Record(rebel, war, rebellionCause);
        RebellionStartupService.RaiseUprisingMilitia(rebel, ratio);
        data.last_land_rebellion_timestamp = GameClock.Now;
        string history = string.Format(LM.Get("land_rebellion_started_history"), city.GetCityName(),
            ratio * 100f, cause);
        RecordRebellionHistory(origin, history, leader);
        EmpireCraft.Core.TranslateHelper.LogEventMessage(history, origin);
        return true;
    }
#endif

    // 饥民起义(连年饥荒，见 FamineUnrestSystem)：照农民起义的流程，但不看无地比例，打仗时照样会起
#if EC_PENDING
    public static bool TryStartFamineRebellion(City city, int years) => StartFamineRebellion(city, years, false) != null;
#endif

    // disaster：受中灾、大灾(见 DisasterSystem)、赈灾后仍在饥荒的城——冷却减为五年，起因写灾名，烈度中灾 +0.1、大灾 +0.2。
    // 饥民军烈度再乘民怨系数 0.8 + 民怨/100(用户 2026-10-26 "饥民军规模 = 基础 × 民怨系数"，见 FamineUnrestSystem.FervorFactor)，
    // 仍受每城义军上限(见 RebellionStartupService)。返回饥民军，没起成返回空
#if EC_PENDING
    public static Realm StartFamineRebellion(City city, int years, bool disaster)
    {
        if (city == null || city.IsGone() || city.Realm == null || city.Realm.IsGone() || city == city.Realm.Capital ||
            !RebellionSystem.CanAttempt(city.Realm) || !CityStabilitySystem.CanRise(city)) return null;
        if (!ModernStability.PassRebellionGate(city.Realm)) return null;
        CityExtension.CityExtraData data = EnsureData(city);
        int cooldown = disaster ? DisasterRebellionCooldownYears : RebellionCooldownYears;
        if (data.last_land_rebellion_timestamp >= 0d &&
            GameClock.YearsSince(data.last_land_rebellion_timestamp) < cooldown) return null;
        Person leader = city.People?.Where(actor => IsLivingResident(actor) && IsAgrarianCommoner(actor) &&
                                                  actor.CanFoundCivKingdom())
            .OrderByDescending(actor => actor.Renown).FirstOrDefault();
        leader ??= CityPopulationSystem.SpawnRebelLeader(city);
        if (leader == null) return null;
        Realm origin = city.Realm;
        // 民怨、灾级在分城之前读(分出去以后本城就不归原国了)
        float grievance = FamineUnrestSystem.FervorFactor(origin);
        int level = disaster ? DisasterSystem.RecentLevel(city) : 0;
        string cause = level >= DisasterSystem.Moderate
            ? string.Format(LM.Get("disaster_rebellion_cause"), city.GetCityName(), DisasterSystem.KindName(city), years)
            : string.Format(LM.Get("famine_rebellion_cause"), city.GetCityName(), years);
        RebellionCauseData rebellionCause = RebellionSystem.Capture(origin, city, "rebellion_reason_famine", cause);
        Realm rebel = city.MakeOwnRealm(leader, rebellion: true);
        if (rebel == null) return null;
        if (!rebel.StartLocalRebelling(EmpireWarType.地方叛乱))
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return null;
        }
        rebel.Name = string.Format(LM.Get("famine_rebel_kingdom_name"), city.GetCityName());
        War war = EmpireWorld.Current.StartWar(rebel, origin, WarKinds.Rebellion);
        if (war == null)
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return null;
        }
        war.SetEmpireWarType(EmpireWarType.地方叛乱);
        war.Name = LM.Get("famine_rebellion_war_name");
        float ratio = CalculateLandlessPopulationRatio(city);
        MarkPeasantSocialRebellion(war, rebel, origin, city, ratio, cause);
        rebel.GetOrCreate().is_famine_rebellion = true;
        RebellionSystem.Record(rebel, war, rebellionCause);
        float fervor = Mathf.Max(ratio, 0.2f + 0.05f * years + 0.1f * Mathf.Max(0, level - 1));
        RebellionStartupService.RaiseUprisingMilitia(rebel, fervor * grievance);
        data.last_land_rebellion_timestamp = GameClock.Now;
        RecordRebellionHistory(origin, cause, leader);
        EmpireCraft.Core.TranslateHelper.LogEventMessage(cause, origin);
        return rebel;
    }
#endif

    // 饥民合流(用户 2026-10-26 "灾荒叛乱接壤的联合叛乱，不接壤各自起事")：接壤的连荒城开城投奔饥民军，就地武装义军。
    // 门槛同起事(不是都城、能起事)，不看冷却；入伙后本城记起事冷却。announce：逐城报一笔(年度入伙)，
    // 起事当年成片合流由 FamineUnrestSystem 合报一笔
#if EC_PENDING
    public static bool JoinFamineRebellion(City city, Realm rebel, bool announce)
    {
        if (city?.Record == null || city.IsGone() || rebel == null || rebel.IsGone()) return false;
        Realm origin = city.Realm;
        if (origin == null || origin.IsGone() || origin == rebel || city == origin.Capital ||
            !RebellionSystem.CanAttempt(origin) || !CityStabilitySystem.CanRise(city)) return false;
        KingdomExtension.KingdomExtraData rebelData = rebel.GetOrCreate();
        if (!rebelData.is_famine_rebellion || rebelData.peasant_land_rebellion_war_id <= 0) return false;
        War war = EmpireWorld.Current?.WarByKey(rebelData.peasant_land_rebellion_war_id);
        if (war == null || war.HasEnded() || !war.IsDefender(origin)) return false;
        float grievance = FamineUnrestSystem.FervorFactor(origin);
        int years = city.GetOrCreate().famine_years;
        city.JoinRealm(rebel, pCaptured: true, rebellion: true);
        if (city.Realm != rebel) return false;
        EnsureData(city).last_land_rebellion_timestamp = GameClock.Now;
        RebellionStartupService.RaiseUprisingMilitia(rebel, (0.2f + 0.05f * years) * grievance, new[] { city });
        if (!announce) return true;
        string history = string.Format(LM.Get("famine_rebellion_city_joined_history"), city.GetCityName(), years,
            rebel.GetKingdomName());
        RecordRebellionHistory(origin, history);
        EmpireCraft.Core.TranslateHelper.LogEventMessage(history, origin);
        return true;
    }

    private static bool TryJoinAdjacentPeasantRebellion(City city, float ratio)
    {
        if (city == null || city.NeighbourCities == null ||
            !RebellionSystem.CanAttempt(city.Realm) || !CityStabilitySystem.CanRise(city)) return false;
        bool landless = ratio >= ContagionLandlessThreshold;
        // 饥民军也拉连荒的邻城入伙(见 FamineUnrestSystem 的合流)：本城连荒、不是都城、本国今年卷入饥民起义的城还没到上限
        bool hungry = !landless && city.GetOrCreate().famine_years >= 1 && city != city.Realm.Capital &&
                      FamineUnrestSystem.RoomLeft(city.Realm) > 0;
        if (!landless && !hungry) return false;
        bool? starving = null;
        foreach (City neighbour in city.NeighbourCities)
        {
            Realm rebel = neighbour?.Realm;
            KingdomExtension.KingdomExtraData rebelData = rebel?.GetOrCreate();
            if (rebelData?.is_peasant_land_rebellion != true || rebelData.peasant_land_rebellion_war_id <= 0) continue;
            if (!landless)
            {
                if (!rebelData.is_famine_rebellion) continue;
                // 眼下还在饥荒(见 FamineUnrestSystem.InFamine，要算吃饱率)：接壤饥民军时才查，只查一次
                starving ??= FamineUnrestSystem.InFamine(city);
                if (starving != true) return false;
                Realm origin = city.Realm;
                if (!JoinFamineRebellion(city, rebel, announce: true)) continue;
                FamineUnrestSystem.CountRebelCities(origin, 1);
                return true;
            }
            War war = EmpireWorld.Current?.WarByKey(rebelData.peasant_land_rebellion_war_id);
            if (war == null || war.HasEnded() || !war.IsDefender(city.Realm)) continue;
            city.JoinRealm(rebel, pCaptured: true, rebellion: true);
            string history = string.Format(LM.Get("land_rebellion_city_joined_history"), city.GetCityName(),
                ratio * 100f, rebel.GetKingdomName());
            RecordRebellionHistory(rebel, history);
            EmpireCraft.Core.TranslateHelper.LogEventMessage(history, rebel);
            return true;
        }
        return false;
    }
#endif

    private static void RecordRebellionHistory(Realm kingdom, string content, Person actor = null)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire != null)
            empire.RecordHistory(directContent: content, actorId: actor?.Key ?? kingdom.Ruler?.Key ?? -1L,
                kingdomId: kingdom.Key);
        (actor ?? kingdom?.Ruler)?.RecordPersonalHistory(content, "peasant_land_rebellion");
    }

    // 某宗族在这座城占的私田(点数，同土地买卖的口径)；内帑的皇庄用(见 PrivyPurse)
    public static float ClanShare(City city, SpecificClan clan) => clan == null ? 0f : GetHouseholdShare(city, $"s:{clan.id}");

    // 买 points 点背景私田的价钱
    public static float LandPrice(float points) => points * BackgroundLandPrice;

    // 买地的钱给卖地的百姓：交过地税存进民间存款
#if EC_PENDING
    public static void PaySellers(City city, float paid)
    {
        CityPopulationData population = CityPopulationSystem.Get(city);
        if (population != null && paid > 0f) ReceiveLandSale(city, population, paid);
    }
#endif

    private static float GetHouseholdShare(City city, string key)
    {
        if (city == null || string.IsNullOrEmpty(key)) return 0f;
        return EnsureData(city).household_land_shares.TryGetValue(key, out float share) ? share : 0f;
    }

    private static Person ResolveRepresentative(CityExtension.CityExtraData data, string key)
    {
        return data.household_land_representatives.TryGetValue(key, out long id)
            ? EmpireWorld.Current?.PersonByKey(id)
            : null;
    }

    private static string ResolveHouseholdName(City city, string key)
    {
        CityExtension.CityExtraData data = EnsureData(city);
        Person representative = ResolveRepresentative(data, key);
        // 城市界面的排名按宗族排：只写"X宗族"，不再带代表人；没有宗族的退回氏族名/个人名
        if (key.StartsWith("s:", StringComparison.Ordinal) && long.TryParse(key.Substring(2), out long clanId))
        {
            SpecificClan clan = SpecificClanManager.Get(clanId);
            if (!string.IsNullOrWhiteSpace(clan?.name)) return clan.GetDisplayName();
        }
        if (key.StartsWith("c:", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(representative?.Clan?.Name))
            return representative.Clan.Name;
        return representative?.FullName() ?? LM.Get("city_land_unknown_household");
    }

    private static string GetCrownName(City city)
    {
        Person ruler = city?.Realm?.GetEmpire()?.Emperor ?? city?.Realm?.Ruler;
        return ruler == null
            ? LM.Get("city_land_crown")
            : string.Format(LM.Get("city_land_crown_holder"), ruler.FullName());
    }

    private static float GetCrownShare(City city)
    {
        if (city?.Realm == null || city.Realm.GetOrCreate().is_peasant_revolutionary_government) return 0f;
        Empire empire = city.Realm.GetEmpire();
        return empire?.CoreKingdom == city.Realm ? 50f : 20f;
    }

    private static float GetNobleShare(City city)
    {
        if (city?.Realm == null || city.Realm.GetOrCreate().is_peasant_revolutionary_government) return 0f;
        return IsLandMarketOpen(city.Realm) ? 20f : 100f - GetCrownShare(city);
    }

    private static float GetPrivateLandCapacity(City city)
    {
        return Mathf.Max(0f, 100f - GetCrownShare(city) - GetNobleShare(city) - BannerSystem.LandShare(city));
    }

    private static void TrimPrivateSharesToCapacity(City city, CityExtension.CityExtraData data)
    {
        float capacity = GetPrivateLandCapacity(city);
        float used = data.household_land_shares.Values.Where(value => value > 0f).Sum();
        if (used <= capacity || used <= 0f) return;
        float scale = capacity / used;
        foreach (string key in data.household_land_shares.Keys.ToList())
            data.household_land_shares[key] = Mathf.Max(0f, data.household_land_shares[key] * scale);
    }
}
}
