// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CorruptionSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 复用城市、封国原有 corruption_rate；年度汇总只是治理快照，不是第三套可增长的腐败值。
public static class CorruptionSystem
{
    public static double GetRate(Empire empire)
    {
        Realm core = empire?.CoreKingdom;
        if (core == null || core.IsGone()) return 0d;
        double local = empire.data?.constitutional_economy?.local_corruption_snapshot ?? -1d;
        return local < 0d ? CorruptionRules.Bound(core.GetCorruptionRate())
            : CorruptionRules.Bound(core.GetCorruptionRate()) * 0.4d + CorruptionRules.Bound(local) * 0.6d;
    }

#if EC_PENDING
    public static void Update(Empire empire)
    {
        var state = empire?.data?.constitutional_economy;
        Realm core = empire?.CoreKingdom;
        if (state == null || core == null || core.IsGone() || empire.IsArchived() || EmpireWorld.Current == null) return;
        double now = GameClock.Now;
        if (state.last_corruption_snapshot >= 0d && now >= state.last_corruption_snapshot &&
            GameClock.YearsSince(state.last_corruption_snapshot) < 1) return;
        // 核心国不向自己上缴税，不能依赖成员国征税 AI 更新中央吏治。
        // 君主资质：明君整肃、昏君荒怠(见 RulerQualitySystem)
        core.AddCorruptionRate(AnnualOfficeChange(core.Ruler, core) + RulerQualitySystem.CorruptionDelta(core.Ruler));
        RefreshLocalSnapshot(empire);
        state.last_corruption_snapshot = now;
    }

    private static void RefreshLocalSnapshot(Empire empire)
    {
        var rates = LocalRates(empire.CoreKingdom, empire.kingdoms_list, empire);
        empire.data.constitutional_economy.local_corruption_snapshot = (rates.provinces + rates.cities) * 0.5d;
    }

    public static double GetRate(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone()) return 0d;
        var rates = LocalRates(kingdom, new[] { kingdom }, null);
        return CorruptionRules.Realm(kingdom.GetCorruptionRate(), rates.provinces, rates.cities);
    }

    private static (double provinces, double cities) LocalRates(Realm core, IEnumerable<Realm> realms,
        Empire empire)
    {
        double provincialWeight = 0d, provincialCorruption = 0d, cityWeight = 0d, cityCorruption = 0d;
        var visitedKingdoms = new HashSet<Realm>();
        var visitedCities = new HashSet<City>();
        // 帝国列表漏掉核心国时仍计入直辖城市；失效或已脱离的成员不进入本国治理数据。
        foreach (Realm kingdom in new[] { core }.Concat(realms ?? Enumerable.Empty<Realm>()))
        {
            if (kingdom == null || kingdom.IsGone() || !visitedKingdoms.Add(kingdom) ||
                (empire != null && kingdom != core && kingdom.GetEmpire() != empire)) continue;
            double households = 0d;
            foreach (City city in kingdom.Cities)
            {
                if (city == null || city.IsGone() || city.Realm != kingdom || !visitedCities.Add(city)) continue;
                int weight = Math.Max(0, CityPopulationSystem.Households(city));
                households += weight;
                cityWeight += weight;
                cityCorruption += CorruptionRules.Bound(city.GetCorruptionRate()) * weight;
            }
            if (kingdom == core || households <= 0d) continue;
            provincialWeight += households;
            provincialCorruption += CorruptionRules.Bound(kingdom.GetCorruptionRate()) * households;
        }
        double central = CorruptionRules.Bound(core.GetCorruptionRate());
        return (provincialWeight > 0d ? provincialCorruption / provincialWeight : central,
            cityWeight > 0d ? cityCorruption / cityWeight : central);
    }

    public static double AnnualOfficeChange(Person actor, Realm kingdom, double? corruptibility = null) =>
        actor == null || actor.IsGone() || !actor.Alive ? 0d
            : CorruptionRules.AnnualOfficeChange(corruptibility ?? actor.CalcCorruptionValue(), kingdom.IsInEmpire(),
                Integrity(actor));
#endif

    // 清廉程度：品行(正面特质多于负面)越好越高，0~1
#if EC_PENDING
    private static double Integrity(Person actor) =>
        Math.Max(0d, EmpireCraft.Core.PerformanceEvents.GetPersonalPerformance(actor) - 0.5d) / 0.5d;
#endif

    private static IEnumerable<Realm> CampaignRealms(Empire empire, Realm target) =>
        target == empire.CoreKingdom
            ? new[] { target }.Concat(empire.kingdoms_list ?? Enumerable.Empty<Realm>()).Distinct()
                .Where(k => k != null && !k.IsGone() && (k == target || k.GetEmpire() == empire))
            : new[] { target };

    public static bool CanCampaign(Empire empire, Realm target)
    {
        var state = empire?.data?.constitutional_economy;
        if (state == null || empire.IsArchived() || empire.IsGone() || EmpireWorld.Current == null ||
            target == null || target.IsGone() || target.GetEmpire() != empire) return false;
        double now = GameClock.Now;
        if (state.last_anticorruption_campaign >= 0d && now >= state.last_anticorruption_campaign &&
            GameClock.YearsSince(state.last_anticorruption_campaign) < 5) return false;
        return CampaignRealms(empire, target).Any(k => k.GetCorruptionRate() >= 0.25d ||
            k.Cities.Any(c => c != null && !c.IsGone() && c.Realm == k && c.GetCorruptionRate() >= 0.25d));
    }

#if EC_PENDING
    public static bool TryCampaign(Empire empire, Realm target)
    {
        if (!CanCampaign(empire, target)) return false;
        var state = empire.data.constitutional_economy;
        state.last_anticorruption_campaign = GameClock.Now;
        List<Realm> realms = CampaignRealms(empire, target).ToList();
        // 每次只整顿三处最严重的行政机构和三城，保留长期治理而不是全国瞬间清零。
        foreach (Realm realm in realms.Where(k => k.GetCorruptionRate() > 0d)
                     .OrderByDescending(k => k.GetCorruptionRate()).Take(3)) realm.AddCorruptionRate(-0.05d);
        foreach (City city in realms.SelectMany(k => k.Cities.Where(c => c != null && !c.IsGone() && c.Realm == k))
                     .Distinct().Where(c => c.GetCorruptionRate() > 0d)
                     .OrderByDescending(c => c.GetCorruptionRate()).Take(3)) city.AddCorruptionRate(-0.1d);
        RefreshLocalSnapshot(empire);
        MonarchyLegitimacy.Invalidate(empire);
        // 整顿吏治告成，吏治败坏的积弊减轻
        MaladySystem.Relieve(empire, MaladySystem.Graft, 15f);
        return true;
    }
#endif

    // 原有国家意志 AI 的年度入口。只有中央发起全国整顿，附属国不会各自重复全国行动。
#if EC_PENDING
    public static bool TryYearlyCampaign(Realm kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        var state = empire?.data?.constitutional_economy;
        if (state == null || kingdom != empire.CoreKingdom || empire.IsArchived() || empire.IsGone() ||
            EmpireWorld.Current == null) return false;
        double now = GameClock.Now;
        if (state.last_anticorruption_check >= 0d && now >= state.last_anticorruption_check &&
            GameClock.YearsSince(state.last_anticorruption_check) < 1) return false;
        state.last_anticorruption_check = now;
        Person ruler = kingdom.Ruler;
        if (ruler == null || ruler.IsGone() || !ruler.Alive) return false;
        bool restorer = RulerTraitSystem.ReignedBy(kingdom, RulerTraitSystem.Restorer);
        // 昏聩、荒淫之君不整顿吏治；明君更常整顿
        if (RulerQualitySystem.Indulgent(ruler)) return false;
        if (ruler.CalcCorruptionValue() > 0d && !restorer) return false;
        float chance = RulerQualitySystem.Of(ruler) == RulerQualitySystem.Grade.Wise ? 0.4f : 0.2f;
        if (!restorer && EcRandom.value >= chance)
            return false;
        if (!TryCampaign(empire, kingdom)) return false;
        EventRecorder.Record(empire, string.Format(LM.Get("anticorruption_campaign_history"), empire.GetEmpireFullName()));
        return true;
    }
#endif

    // 由法律系统在处罚后调用；辖区在处罚前捕获，罢官/流放后仍能整顿原任职地。
#if EC_PENDING
    public static void OnLawEnforced(Realm kingdom, City city, bool administration, LawType law,
        IList<PunishmentLevel> punishments, int moneyBefore, int moneyAfter)
    {
        if (kingdom == null || kingdom.IsGone() || punishments == null || punishments.Count == 0 ||
            (law != LawType.贪污 && law != LawType.受贿 && law != LawType.买官 && law != LawType.卖官)) return;
        if (punishments.Contains(PunishmentLevel.罚金) || punishments.Contains(PunishmentLevel.没收财产))
        {
            int recovered = CorruptionRules.RecoveredMoney(moneyBefore, moneyAfter, kingdom.GetMoney());
            if (recovered > 0) kingdom.AddMoney(recovered, TreasuryCategory.RecoveredFunds);
        }
        bool removed = punishments.Any(p => p == PunishmentLevel.剥夺官职 || p == PunishmentLevel.流放 ||
            p == PunishmentLevel.死刑 || p == PunishmentLevel.夷三族);
        double cut = removed ? 0.1d : 0.025d;
        if (city != null && !city.IsGone() && city.Realm == kingdom) city.AddCorruptionRate(-cut);
        else if (administration) kingdom.AddCorruptionRate(-cut);
        MonarchyLegitimacy.Invalidate(kingdom.GetEmpire());
    }
#endif
}
}
