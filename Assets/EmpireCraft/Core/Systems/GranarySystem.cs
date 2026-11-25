// Generated from the EmpireCraft mod: Scripts/GeneralSystems/GranarySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 国家粮仓(无小人模式)：按粮食征收政策(宪法条款，见 ConstitutionGrain；没有宪法的国家按"征收粮赋")，
// 各城收割的余粮一部分交国家粮仓——留归地方 0、征收粮赋三成、统购统销七成。先留足本城的口粮，只从余粮里征
// (用户 2026-10-28：以前按收成整份抽，农田村越多被抽得越多，种粮的城自己不够吃、再靠打了折的赈灾调回来，见 SurplusShare)。
// 粮仓按帝国(没有帝国就按王国)存放，每年损耗 SpoilagePerYear；饥荒时开仓赈灾，向缺粮的城市调粮，每年记一次赈灾。
// 赈灾随积弊变差(见 ReliefEfficiency)：调粮按缺口 × 赈灾效率，粮仓存粮仍是上限。
// 粮食单位与城市仓库相同(户数尺度)。
public static class GranarySystem
{
    private const float SpoilagePerYear = 0.05f;
    private static readonly Dictionary<Realm, (int cities, float amount)> ReliefThisYear = new();
    private static readonly Dictionary<Realm, (double at, float value)> EfficiencyCache = new();

    public static Realm ModRealm(Realm kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    // 推行郡县制(各文明的中央集权官僚制度，制度特性 state_granary)之后才有国家粮仓；现代国家都有。
    // 之前的分封时代收成全留地方，也没有国家赈灾
    public const string FeatureStateGranary = "state_granary";

#if EC_PENDING
    public static bool Enabled(Realm kingdom)
    {
        Realm realm = ModRealm(kingdom);
        if (realm == null || realm.IsGone() || realm.IsWild) return false;
        if (ModernStability.IsModern(realm)) return true;
        // 制度特性照制度文化(旧制犹存的照旧制，见 LegacyInstitutionSystem)
        string culture = CultureService.GetInstitutionCulture(realm);
        return CultureService.IsValidCulture(culture) && InstitutionSystem.HasFeature(culture, FeatureStateGranary);
    }
#endif

    public static ConstitutionGrain Policy(Realm kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire == null ? ConstitutionGrain.Tribute
            : ConstitutionSystem.GetClauses(empire)?.grain ?? ConstitutionGrain.Tribute;
    }

    public static float LevyShare(ConstitutionGrain policy) => policy switch
    {
        ConstitutionGrain.Local => 0f,
        ConstitutionGrain.Central => 0.7f,
        _ => 0.3f
    };

    public static float Stock(Realm kingdom)
    {
        Realm realm = ModRealm(kingdom);
        return realm == null || realm.IsGone() ? 0f : realm.GetOrCreate().granary;
    }

    // 收成：按政策交国家粮仓的部分存进粮仓，返回留给本城的部分。只从余粮里征(见 SurplusShare)
#if EC_PENDING
    public static float Collect(City city, float harvest, CityPopulationData data = null)
    {
        Realm realm = ModRealm(city?.Realm);
        if (harvest <= 0f || !Enabled(city?.Realm)) return harvest;
        float levied = harvest * LevyShare(Policy(city.Realm)) * SurplusShare(city, data);
        if (levied <= 0f) return harvest;
        Spoil(realm);
        realm.GetOrCreate().granary += levied;
        return harvest - levied;
    }
#endif

    // 收成里余粮的比例：本城一年要吃的粮(每户 1，其中半份由菜园渔猎自给，余下的靠收成)比上年的收成(交粮仓之前)，
    // 余下的才是余粮；上年收成还没结算过的不征
    private const float HarvestNeedPerHousehold = 0.5f;

#if EC_PENDING
    public static float SurplusShare(City city, CityPopulationData data = null)
    {
        data ??= CityPopulationSystem.Get(city);
        float gross = data?.last_harvest_gross ?? -1f;
        if (gross <= 0f) return 0f;
        float need = CityPopulationSystem.BackgroundHouseholds(city) * HarvestNeedPerHousehold;
        return Mathf.Clamp01(1f - need / gross);
    }
#endif

    // 捐粮入国家粮仓(世家灾年赈济，见 GentrySystem.Events)；没有国家粮仓的不收
#if EC_PENDING
    public static void Donate(Realm kingdom, float amount)
    {
        Realm realm = ModRealm(kingdom);
        if (amount <= 0f || !Enabled(kingdom)) return;
        realm.GetOrCreate().granary += amount;
    }
#endif

    // 赈灾：缺粮的城市从国家粮仓调粮，返回调到的数量
#if EC_PENDING
    public static float Relieve(City city, float shortage)
    {
        Realm realm = ModRealm(city?.Realm);
        if (shortage <= 0f || !Enabled(city?.Realm)) return 0f;
        Spoil(realm);
        KingdomExtension.KingdomExtraData data = realm.GetOrCreate();
        float relief = Mathf.Min(shortage * ReliefEfficiency(realm), data.granary);
        if (relief < 1f) return 0f;
        data.granary -= relief;
        ReliefThisYear.TryGetValue(realm, out var total);
        ReliefThisYear[realm] = (total.cities + 1, total.amount + relief);
        return relief;
    }
#endif

    // 赈灾效率(用户 2026-10-26："朝廷赈灾安抚力度由当前积弊决定(积弊≥80 任何灾都赈不了；50~80 只小灾能赈；
    // 10~50 中小灾能赈；<10 全能赈)")：本模组的赈灾是按缺口调粮，不分灾的大小，改成调粮打折——
    //   王朝积弊(DynasticCycleSystem.GetPressure)<10 全额、10~50 八成、50~80 五成、≥80 两成；
    //   再乘 (1 − 腐败率×0.5)(层层克扣)、(1 − 吏治败坏×0.25)(MaladySystem 的吏治一条，已有一部分折进腐败)；
    //   没有帝国、不走王朝周期(共和、现代)的只看腐败；最少一成。按政权每月重算一次(腐败率要扫各城)。
    // 国家粮仓赈灾、朝议"赈济灾荒"发粮都乘它
#if EC_PENDING
    public static float ReliefEfficiency(Realm kingdom)
    {
        Realm realm = ModRealm(kingdom);
        if (realm == null || realm.IsGone()) return 1f;
        double now = EmpireWorld.Current?.Now() ?? 0d;
        if (EfficiencyCache.TryGetValue(realm, out var cached) && now >= cached.at && GameClock.MonthsSince(cached.at) < 1)
            return cached.value;
        Empire empire = realm.GetEmpire();
        float pressure = DynasticCycleSystem.GetPressure(empire);
        float tier = pressure >= 80f ? 0.2f : pressure >= 50f ? 0.5f : pressure >= 10f ? 0.8f : 1f;
        double corruption = empire?.data != null && empire.CoreKingdom == realm
            ? CorruptionSystem.GetRate(empire) : CorruptionSystem.GetRate(realm);
        float graft = MaladySystem.Severity(empire, MaladySystem.Graft);
        float value = Mathf.Clamp(tier * (1f - (float)corruption * 0.5f) * (1f - graft * 0.25f), 0.1f, 1f);
        EfficiencyCache[realm] = (now, value);
        return value;
    }
#endif

    // 年度损耗；顺便把上一年的赈灾写进史书
#if EC_PENDING
    private static void Spoil(Realm realm)
    {
        KingdomExtension.KingdomExtraData data = realm.GetOrCreate();
        double now = EmpireWorld.Current?.Now() ?? 0d;
        if (data.granary_last_spoil < 0d || now < data.granary_last_spoil)
        {
            data.granary_last_spoil = now;
            return;
        }
        int years = GameClock.YearsSince(data.granary_last_spoil);
        if (years < 1) return;
        data.granary_last_spoil = now;
        data.granary *= Mathf.Pow(1f - SpoilagePerYear, Mathf.Min(years, 10));
        if (!ReliefThisYear.TryGetValue(realm, out var relief) || relief.cities == 0) return;
        ReliefThisYear.Remove(realm);
        string text = string.Format(LM.Get("granary_relief_history"), realm.GetKingdomName(), relief.cities,
            Mathf.RoundToInt(relief.amount));
        // 开仓赈灾是例行的事：只记入帝国史书，不报世界提示
        Empire empire = realm.GetEmpire();
        if (empire?.data != null && !empire.IsGone()) EmpireCraft.Core.HistoryRecordSystem.RecordHistory(empire, directContent: text);
    }
#endif

    public static void ResetWorldState()
    {
        ReliefThisYear.Clear();
        EfficiencyCache.Clear();
    }
}
}
