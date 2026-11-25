// Ported from the EmpireCraft mod: Scripts/GeneralSystems/RebellionSnowballSystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 正统崩坏时革命滚雪球：帝国天命低于 SnowballMandate 时，与叛军接壤的帝国城市每年有机会倒戈投向叛军。
//   · 天命越低越猛(0 时满额)，忠诚越低的城越容易倒戈；
//   · 每年倒戈城数与叛军现有规模成正比——叛军越大涨得越快；
//   · 每座城倒戈再扣 1 点天命，形成正反馈；新得的城就地武装义军(RebellionStartupService)。
// 帝国核心王国的首都不会这样倒戈，只能打下来。军队也会倒戈(DefectSoldiers)，同样滚雪球。
// 王朝积弊满额、正统崩坏时倒戈不再看城的稳定度：城市稳定度几乎不随朝廷跌落，驻军再压 20，这道门把滚雪球挡死了(见 DynasticCycleRules.MayRise)。
// 每年由 ConstitutionalEconomySystem 的年度结算调用。
public static class RebellionSnowballSystem
{
    private const int SnowballMandate = 40;
    private const float DefectChance = 0.25f;
    private const float GrowthPerYear = 0.5f;

    private static readonly HashSet<EmpireWarType> RebellionTypes = new()
    {
        EmpireWarType.地方叛乱, EmpireWarType.派系叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱,
        EmpireWarType.地方独立
    };

#if EC_PENDING
    public static void Update(Empire empire)
    {
        Realm core = empire?.CoreKingdom;
        if (core == null || core.IsGone() || empire.Legitimacy >= SnowballMandate) return;
        float intensity = Mathf.Clamp01((SnowballMandate - empire.Legitimacy) / (float)SnowballMandate);
        foreach (Realm rebel in FindRebels(empire, core).ToList())
        {
            try
            {
                Snowball(empire, core, rebel, intensity);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 革命滚雪球失败: {exception.Message}");
            }
        }
    }
#endif

    // 正在与帝国打叛乱/革命战争的叛军(进攻方主帅)
#if EC_PENDING
    internal static IEnumerable<Realm> FindRebels(Empire empire, Realm core) => core.getWars()
        .Where(war => war != null && !war.hasEnded() && war.getMainDefender() != null &&
                      war.getMainDefender().GetEmpire() == empire &&
                      (RebellionTypes.Contains(war.GetEmpireWarType()) || war.getAsset() == WarTypeLibrary.rebellion))
        .Select(war => war.getMainAttacker())
        .Where(rebel => rebel != null && !rebel.IsGone() && rebel.GetEmpire() != empire)
        .Distinct();

    private static void Snowball(Empire empire, Realm core, Realm rebel, float intensity)
    {
        List<City> rebelCities = rebel.Cities?.Where(city => city != null && !city.IsGone()).ToList() ?? new List<City>();
        if (rebelCities.Count == 0) return;
        int budget = Math.Max(1, Mathf.CeilToInt(rebelCities.Count * GrowthPerYear * intensity));
        bool unbound = DynasticCycleSystem.UprisingsUnbound(empire);
        List<City> frontier = rebelCities
            .SelectMany(city => (IEnumerable<City>)city.neighbours_cities ?? Enumerable.Empty<City>())
            .Where(city => city != null && !city.IsGone() && city.kingdom != null && city.kingdom.GetEmpire() == empire &&
                           city != core.Capital && RebellionSystem.CanAttempt(city.kingdom) &&
                           (unbound || CityStabilitySystem.CanRise(city)))
            .Distinct().OrderBy(_ => EcRandom.value).ToList();
        var defected = new List<string>();
        var joined = new List<City>();
        foreach (City city in frontier)
        {
            if (defected.Count >= budget) break;
            float loyaltyFactor = 1f + Mathf.Max(0f, 50f - city.getLoyalty()) / 50f;
            if (EcRandom.value >= DefectChance * intensity * loyaltyFactor) continue;
            string name = city.GetCityName();
            city.JoinRealm(rebel, pCaptured: true, pRebellion: true);
            if (city.Realm != rebel) continue;
            defected.Add(name);
            joined.Add(city);
            empire.AddMandate(-1);
        }
        if (defected.Count > 0)
        {
            RebellionStartupService.RaiseUprisingMilitia(rebel, intensity, joined);
            EventRecorder.Record(empire, actor: rebel.Ruler, logKingdom: rebel, text: string.Format(LM.Get("rebellion_snowball_history"), string.Join("、", defected),
                rebel.GetKingdomFullName(), empire.GetEmpireFullName()));
        }
        DefectSoldiers(empire, rebel, intensity, new HashSet<City>(frontier));
    }
#endif

    // 军队倒戈也滚雪球：叛军在双方兵力里占比越大，倒戈的官兵越多。
    // 先是认同叛军理念的，其次是驻在与叛军接壤城市的，最后才是其他人。每 10 人倒戈再扣 1 点天命。
    private const float ArmyDefectBase = 0.03f;
    private const float ArmyDefectMomentum = 0.25f;
    private const float ArmyDefectMax = 0.2f;

#if EC_PENDING
    private static void DefectSoldiers(Empire empire, Realm rebel, float intensity, HashSet<City> frontier)
    {
        if (rebel.Capital == null || rebel.Capital.IsGone()) return;
        List<Person> loyal = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.IsGone())
            .SelectMany(kingdom => kingdom.units ?? new List<Person>())
            .Where(actor => actor != null && !actor.IsGone() && actor.Alive && actor.isWarrior() && !actor.isKing())
            .ToList();
        if (loyal.Count == 0) return;
        int rebelSoldiers = rebel.units?.Count(actor => actor != null && actor.Alive && actor.isWarrior()) ?? 0;
        float balance = rebelSoldiers / (float)(rebelSoldiers + loyal.Count);
        float fraction = Mathf.Min(ArmyDefectMax, intensity * (ArmyDefectBase + ArmyDefectMomentum * balance));
        int count = Mathf.RoundToInt(loyal.Count * fraction);
        if (count <= 0) return;
        PartyIdeology rebelIdeology = IdeologyFamilies.StateIdeology(rebel);
        List<Person> defectors = loyal
            .OrderBy(actor => IdeologyPopulationSystem.Get(actor) == rebelIdeology ? 0 : 1)
            .ThenBy(actor => actor.City != null && frontier.Contains(actor.City) ? 0 : 1)
            .ThenBy(_ => EcRandom.value)
            .Take(count).ToList();
        int moved = defectors.GroupBy(actor => actor.Realm)
            .Sum(group => InstitutionSystem.TransferDefectingSoldiers(group.Key, rebel, group));
        if (moved <= 0) return;
        empire.AddMandate(-Math.Max(1, moved / 10));
        EventRecorder.Record(empire, actor: rebel.Ruler, logKingdom: rebel, text: string.Format(LM.Get("rebellion_army_defection_history"), moved,
            empire.GetEmpireFullName(), rebel.GetKingdomFullName()));
    }
#endif

}
}
