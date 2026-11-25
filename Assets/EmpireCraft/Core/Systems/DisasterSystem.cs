// Generated from the EmpireCraft mod: Scripts/GeneralSystems/DisasterSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 灾年(用户 2026-10-26："每五年，在粮食产量最低的若干村庄里随机选取目标判定灾年，灾分小、中、大三级：小灾 5 年一轮基础 1 处；
// 中灾 50 年必触发一轮基础 2 处；大灾 200 年必触发一轮基础 3 处")。以前的饥荒全是内生的(缺田、缺人、兵火)，没有外来的天灾。
//   · 按政权判定(帝国按全体成员一起抽，记在核心王国；独立王国按本国——与国家粮仓同一口径，见 GranarySystem.Realm)：
//     每五年一轮，必有一场灾；这一轮是中灾的机会 8%、大灾 2%，其余是小灾；
//     五十年没来过中灾以上，这一轮至少中灾；两百年没来过大灾，这一轮就是大灾；
//   · 受灾城：本国人均粮食产出最少的那批城(后三成，至少后十座)里随机抽，基础 小 1、中 2、大 3 处，
//     本国每 15 座城加一倍(至多四倍)；一次至多波及本国四成的城(至少一座)，免得小国一场灾就完；
//   · 效果是减产：受灾城田里的收成(见 FarmlandSystem)小灾一年 ×0.6、中灾两年 ×0.45、大灾三年 ×0.3；现代国家减产减半。
//     缺粮照现有的流程走：吃饱率下降 → 国家粮仓赈灾(随积弊打折，见 GranarySystem.ReliefEfficiency) → 饥荒 → 饥荒动荡、
//     饥民起义(受中灾、大灾的城连荒一年就可能起事，见 FamineUnrestSystem)；
//   · 小人模式没有本模组的收成结算(粮食是小人自己收割入库的)：灾年改为每月损毁受灾城存粮的 (1−系数)×15%；
//   · 灾名随机(旱、水、蝗、疫、雹)；中灾、大灾记入史书。只对有城的文明国家；古战模组的国家跳过。
// 旧档、新国的计时从第一次看到时起算，第一轮在一到五年后，不补灾。每月至多判定四个政权(到期的按先后)。
public static class DisasterSystem
{
    public const int Minor = 1, Moderate = 2, Major = 3;

    private const int RollYears = 5, ModerateGuaranteeYears = 50, MajorGuaranteeYears = 200;
    private const float ModerateChance = 0.08f, MajorChance = 0.02f;
    // 按等级(下标 1 小、2 中、3 大)：基础受灾城数、减产年数、收成系数
    private static readonly int[] BaseCities = { 0, 1, 2, 3 };
    private static readonly int[] DurationYears = { 0, 1, 2, 3 };
    private static readonly float[] HarvestFactors = { 1f, 0.6f, 0.45f, 0.3f };
    private const int CitiesPerScale = 15, MaxScale = 4;
    private const float MaxStrickenShare = 0.4f, PoolShare = 0.3f;
    private const int PoolMin = 10;
    private const int MaxRollsPerMonth = 4;
    // 政权两年没被看到(并入帝国、被灭后复国)：计时作废，从头起算
    private const int StaleYears = 2;
    // 灾后一年里仍算"受过灾"(饥民起义的门槛，见 RecentLevel)
    private const int AftermathYears = 1;
    private const float MonthlyStockLoss = 0.15f, ModernSoftening = 0.5f;

    private static readonly string[] Kinds = { "drought", "flood", "locust", "pestilence", "hail" };
    private static readonly HashSet<City> Afflicted = new();
    private static object _world;

    // 收成系数(FarmlandSystem 三处收成统一乘)：没灾、灾已过去为 1
#if EC_PENDING
    public static float HarvestFactor(City city)
    {
        if (city?.Record == null || EmpireWorld.Current == null) return 1f;
        CityExtension.CityExtraData data = city.GetOrCreate();
        int level = data.disaster_level;
        if (level <= 0 || level >= HarvestFactors.Length || GameClock.Now >= data.disaster_until) return 1f;
        float factor = HarvestFactors[level];
        return ModernStability.IsModern(city.Realm) ? 1f - (1f - factor) * ModernSoftening : factor;
    }

    public static bool Active(City city) => HarvestFactor(city) < 1f;
#endif

    // 正在受灾或灾后一年内的等级(0 = 没有)
    public static int RecentLevel(City city)
    {
        if (city?.Record == null || EmpireWorld.Current == null) return 0;
        CityExtension.CityExtraData data = city.GetOrCreate();
        if (data.disaster_level <= 0) return 0;
        return GameClock.Now < data.disaster_until + AftermathYears * YearlyStagger.YearSeconds ? data.disaster_level : 0;
    }

    public static string KindName(City city)
    {
        string kind = city?.Record == null ? "" : city.GetOrCreate().disaster_kind;
        return LM.Get("disaster_kind_" + (string.IsNullOrEmpty(kind) ? Kinds[0] : kind));
    }

    // 每月(由 FamineUnrestSystem.Tick 调用)：轮到期的政权判定灾年；小人模式损毁受灾城的存粮；清理过了灾后期的城
#if EC_PENDING
    public static void Monthly()
    {
        if (EmpireWorld.Current?.kingdoms?.list == null) return;
        EnsureWorld();
        double now = GameClock.Now;
        var due = new List<Realm>();
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms)
        {
            if (!IsRealmHead(kingdom)) continue;
            KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
            if (data.disaster_seen < 0d || now < data.disaster_seen || GameClock.YearsSince(data.disaster_seen) >= StaleYears ||
                data.disaster_last_roll < 0d || now < data.disaster_last_roll) Begin(data, now);
            data.disaster_seen = now;
            if (GameClock.YearsSince(data.disaster_last_roll) >= RollYears) due.Add(kingdom);
        }
        foreach (Realm head in due.OrderBy(kingdom => kingdom.GetOrCreate().disaster_last_roll).Take(MaxRollsPerMonth).ToList())
            Roll(head, now);
        Aftermath(now);
    }
#endif

    private static void EnsureWorld()
    {
        if (ReferenceEquals(_world, EmpireWorld.Current)) return;
        _world = EmpireWorld.Current;
        Afflicted.Clear();
        // 读档后一次：把存档里还在受灾的城找回来
        if (EmpireWorld.Current.Cities == null) return;
        foreach (City city in EmpireWorld.Current.Cities)
            if (city?.Record != null && !city.IsGone() && city.GetOrCreate().disaster_level > 0) Afflicted.Add(city);
    }

    // 政权的头：帝国的核心王国、不在帝国里的王国
#if EC_PENDING
    private static bool IsRealmHead(Realm kingdom) =>
        kingdom?.Record != null && !kingdom.IsGone() && !kingdom.IsWild && kingdom.IsCivilized() && kingdom.Cities != null &&
        kingdom.Cities.Count > 0 && !AncientWarfareCompatibility.Owns(kingdom) && GranarySystem.Realm(kingdom) == kingdom;
#endif

    // 计时从现在起算：第一轮在一到五年后，五十年、两百年的保底也从现在算
    private static void Begin(KingdomExtension.KingdomExtraData data, double now)
    {
        data.disaster_last_roll = now - EcRandom.value * (RollYears - 1) * YearlyStagger.YearSeconds;
        data.disaster_last_mid = now;
        data.disaster_last_major = now;
    }

#if EC_PENDING
    private static void Roll(Realm head, double now)
    {
        KingdomExtension.KingdomExtraData data = head.GetOrCreate();
        data.disaster_last_roll = now;
        List<City> cities = RealmCities(head);
        if (cities.Count == 0) return;
        float roll = EcRandom.value;
        int level = Minor;
        if (roll < MajorChance || GameClock.YearsSince(data.disaster_last_major) >= MajorGuaranteeYears) level = Major;
        else if (roll < MajorChance + ModerateChance || GameClock.YearsSince(data.disaster_last_mid) >= ModerateGuaranteeYears)
            level = Moderate;
        if (level >= Moderate) data.disaster_last_mid = now;
        if (level >= Major) data.disaster_last_major = now;
        string kind = Kinds[EcRandom.Range(0, Kinds.Length)];
        List<City> stricken = Pick(cities, level);
        foreach (City city in stricken) Strike(city, level, kind, now);
        if (level >= Moderate && stricken.Count > 0) Record(head, stricken, level, kind);
    }
#endif

    // 帝国：核心与全体成员的城；古战模组的成员不算
    private static List<City> RealmCities(Realm head)
    {
        var result = new List<City>();
        Empire empire = head.GetEmpire();
        IEnumerable<Realm> members = empire?.CoreKingdom == head && empire.kingdoms_list != null
            ? new[] { head }.Concat(empire.kingdoms_list.ToList()) : new[] { head };
        var seen = new HashSet<Realm>();
        foreach (Realm kingdom in members)
        {
            if (kingdom?.Cities == null || kingdom.IsGone() || !seen.Add(kingdom) || AncientWarfareCompatibility.Owns(kingdom)) continue;
            foreach (City city in kingdom.Cities)
                if (city?.Record != null && !city.IsGone()) result.Add(city);
        }
        return result;
    }

    // 从人均粮食最少的那批城里随机抽；正在受灾的城尽量不重复抽
#if EC_PENDING
    private static List<City> Pick(List<City> cities, int level)
    {
        int total = cities.Count;
        int scale = Mathf.Min(MaxScale, 1 + total / CitiesPerScale);
        int count = Mathf.Min(BaseCities[level] * scale, Mathf.Max(1, Mathf.FloorToInt(total * MaxStrickenShare)));
        List<City> candidates = cities.Where(city => !Active(city)).ToList();
        if (candidates.Count == 0) candidates = cities;
        int poolSize = Mathf.Min(candidates.Count, Mathf.Max(PoolMin, Mathf.CeilToInt(candidates.Count * PoolShare)));
        List<City> pool = candidates.Select(city => (city, score: FoodScore(city))).OrderBy(pair => pair.score)
            .Take(poolSize).Select(pair => pair.city).ToList();
        count = Mathf.Min(count, pool.Count);
        for (int i = 0; i < count; i++)
        {
            int j = EcRandom.Range(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.Take(count).ToList();
    }
#endif

    // 人均粮食：无小人模式按一年的粮食产出(见 PopulationEconomySystem)，还没结算过的、小人模式按存粮
#if EC_PENDING
    private static float FoodScore(City city)
    {
        float households = Mathf.Max(1, CityPopulationSystem.Households(city));
        if (CityPopulationSystem.AbstractPopulationEnabled)
        {
            CityPopulationData population = CityPopulationSystem.Get(city);
            if (population != null && population.last_food_output > 0f) return population.last_food_output / households;
        }
        float food = 0f;
        try { food = city.getTotalFood(); }
        catch
        {
            // 读不到存粮按中等处理
            food = households;
        }
        return food / households;
    }
#endif

    // 已在受灾的城：取重的那级，减产期取长的
    private static void Strike(City city, int level, string kind, double now)
    {
        CityExtension.CityExtraData data = city.GetOrCreate();
        double until = now + DurationYears[level] * YearlyStagger.YearSeconds;
        bool active = data.disaster_level > 0 && now < data.disaster_until;
        if (!active || level >= data.disaster_level)
        {
            data.disaster_level = level;
            data.disaster_kind = kind;
        }
        data.disaster_until = active ? Math.Max(data.disaster_until, until) : until;
        Afflicted.Add(city);
    }

#if EC_PENDING
    private static void Aftermath(double now)
    {
        if (Afflicted.Count == 0) return;
        bool vanilla = !CityPopulationSystem.AbstractPopulationEnabled;
        foreach (City city in Afflicted.ToList())
        {
            if (city?.Record == null || city.IsGone())
            {
                Afflicted.Remove(city);
                continue;
            }
            CityExtension.CityExtraData data = city.GetOrCreate();
            if (data.disaster_level <= 0)
            {
                Afflicted.Remove(city);
                continue;
            }
            if (now >= data.disaster_until)
            {
                if (now < data.disaster_until + AftermathYears * YearlyStagger.YearSeconds) continue;
                data.disaster_level = 0;
                data.disaster_kind = "";
                data.disaster_until = -1d;
                Afflicted.Remove(city);
                continue;
            }
            if (vanilla && !AncientWarfareCompatibility.OwnsObject(city)) SpoilStock(city);
        }
    }
#endif

    // 小人模式：灾年每月损毁一部分存粮
#if EC_PENDING
    private static void SpoilStock(City city)
    {
        float share = (1f - HarvestFactor(city)) * MonthlyStockLoss;
        if (share <= 0f) return;
        int stock;
        try { stock = city.getTotalFood(); }
        catch { return; }
        int loss = Mathf.FloorToInt(stock * share);
        if (loss > 0) PopulationEconomySystem.TakeFood(city, loss);
    }
#endif

    // 中灾、大灾记入史书：帝国中灾按例行政事记(屏幕日志只给举足轻重的国家)，大灾记史书并报世界提示；
    // 没有帝国的王国没有史书，大灾只报提示
#if EC_PENDING
    private static void Record(Realm head, List<City> stricken, int level, string kind)
    {
        string separator = LM.Get("disaster_city_separator");
        string names = string.Join(separator, stricken.Take(3).Select(city => city.GetCityName()));
        if (stricken.Count > 3) names = string.Format(LM.Get("disaster_cities_more"), names, stricken.Count);
        Empire empire = head.GetEmpire();
        bool imperial = empire?.data != null && !empire.IsGone() && empire.CoreKingdom == head;
        string realm = imperial ? empire.GetEmpireFullName() : head.GetKingdomName();
        string text = string.Format(LM.Get(level >= Major ? "disaster_major_history" : "disaster_mid_history"), realm, names,
            LM.Get("disaster_kind_" + kind));
        if (imperial)
        {
            if (level >= Major) EventRecorder.Record(empire, text, null, head);
            else EventRecorder.RecordRoutine(empire, text);
        }
        else if (level >= Major) EventRecorder.Record(head, text);
    }
#endif
}
}
