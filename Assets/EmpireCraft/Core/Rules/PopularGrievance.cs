// Ported from the EmpireCraft mod: Scripts/GeneralSystems/PopularGrievance.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 民怨指数(0~100，只读)。用户 2026-10-26："民怨……增长来源：好大喜功类君主、大兴土木、重赋税、重徭役；
// 积弊满额之后，民怨只升不降。降低途径：改革、反腐。作用：直接影响叛乱规模。"
// 本模组的民怨不是一个数，由三样分担：
//   · 苛政(HarshRuleSystem，每年重算的快照：重税、贪腐、兼并、兵役、徭役、大兴土木、暴君、好大喜功、积弊……)；
//   · 各阶层怨气(InstitutionSystem.UpdateSocialUnrest，只有帝国逐年结算)；
//   · 积弊(DynasticCycleSystem、MaladySystem，已折进苛政)。
// 这里只把它们合成一个读数给叛乱规模、灾荒等系统用，不存档、不反写：
//   民怨 = max(所在政权的苛政, 平民阶层怨气按人口加权的平均)
// 平民 = 农民、国人、野人、工人、奴隶(与 SocialCrisisSystem 的"庶民"同口径)。
// 帝国成员按帝国、独立王国按本国；古战模组的国家、空值返回 0。
// 阶层人口构成要扫全体人口，按政权每年只算一次；怨气本身随时读，年内事件加减的怨气即时反映。只在主线程调用(缓存不加锁)。
public static class PopularGrievance
{
    // 档位(界面、叛乱规模可用；别的系统不必用)
    public const int Calm = 0;       // 安堵：< 30
    public const int Murmuring = 1;  // 怨声：≥ 30
    public const int Seething = 2;   // 沸腾：≥ 60(与民变门槛 HarshRuleSystem.UprisingBurden 同)
    public const int Boiling = 3;    // 鼎沸：≥ 80

    private static readonly Dictionary<long, (int year, Dictionary<SocialClass, float> shares)> EmpireShares = new();
    private static readonly Dictionary<long, (int year, Dictionary<SocialClass, float> shares)> KingdomShares = new();
    private static object _cacheWorld;

#if EC_PENDING
    public static float Index(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || EmpireWorld.Current == null) return 0f;
        if (AncientWarfareCompatibility.Owns(kingdom)) return 0f;
        try
        {
            Empire empire = kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;
            float burden = HarshRuleSystem.GetRealmBurden(kingdom);
            float commoners = empire != null ? CommonerGrievance(empire) : CommonerGrievance(kingdom);
            return Mathf.Clamp(Mathf.Max(burden, commoners), 0f, 100f);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 民怨指数计算失败({kingdom.Name}): {exception.Message}");
            return 0f;
        }
    }
#endif

    // 0~1 的系数(民怨 / 100)
#if EC_PENDING
    public static float Factor(Realm kingdom) => Index(kingdom) / 100f;

    public static int Tier(Realm kingdom) => Tier(Index(kingdom));
#endif

    public static int Tier(float index) =>
        index >= 80f ? Boiling : index >= 60f ? Seething : index >= 30f ? Murmuring : Calm;

    // 平民阶层怨气：按人口加权的平均(阶层怨气只有帝国逐年结算；独立王国一般为 0)
#if EC_PENDING
    public static float CommonerGrievance(Empire empire)
    {
        if (empire?.data == null || empire.IsGone()) return 0f;
        return Weighted(empire.data.institution_state?.class_grievances,
            () => SharesOf(EmpireShares, empire.data.id, empire.kingdoms_hashset));
    }

    private static float CommonerGrievance(Realm kingdom) =>
        Weighted(kingdom.GetOrCreate()?.institution_state?.class_grievances,
            () => SharesOf(KingdomShares, kingdom.Key, new[] { kingdom }));
#endif

    public static bool IsCommoner(SocialClass socialClass) =>
        socialClass.IsPeasantry() || socialClass is SocialClass.Labour or SocialClass.Slave;

    private static float Weighted(Dictionary<SocialClass, float> grievances,
        Func<Dictionary<SocialClass, float>> shares)
    {
        // 平民都没有怨气就不必扫人口
        if (grievances == null || !grievances.Any(pair => IsCommoner(pair.Key) && pair.Value > 0.5f)) return 0f;
        Dictionary<SocialClass, float> population = shares();
        float weight = 0f;
        float sum = 0f;
        foreach (KeyValuePair<SocialClass, float> pair in grievances)
        {
            if (!IsCommoner(pair.Key) || population == null ||
                !population.TryGetValue(pair.Key, out float share) || share <= 0f) continue;
            weight += share;
            sum += Mathf.Clamp(pair.Value, 0f, 100f) * share;
        }
        return weight > 0f ? sum / weight : 0f;
    }

#if EC_PENDING
    private static Dictionary<SocialClass, float> SharesOf(
        Dictionary<long, (int year, Dictionary<SocialClass, float> shares)> cache, long id, IEnumerable<Realm> scope)
    {
        if (!ReferenceEquals(_cacheWorld, EmpireWorld.Current))
        {
            _cacheWorld = EmpireWorld.Current;
            EmpireShares.Clear();
            KingdomShares.Clear();
        }
        int year = GameClock.Year;
        if (cache.TryGetValue(id, out var cached) && cached.year == year) return cached.shares;
        // 亡国、往年的旧条目攒多了就清掉
        if (cache.Count > 256)
            foreach (long stale in cache.Where(pair => pair.Value.year != year).Select(pair => pair.Key).ToList())
                cache.Remove(stale);
        Dictionary<SocialClass, float> shares = InstitutionSystem.BuildClassShares(scope);
        cache[id] = (year, shares);
        return shares;
    }
#endif
}
}
