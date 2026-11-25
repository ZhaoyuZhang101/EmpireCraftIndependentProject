// Generated from the EmpireCraft mod: Scripts/GeneralSystems/EconomicCircleSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Time = EmpireCraft.Core.FrameClock;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 经济圈：富庶的城市成为一方经济中心(主城)，周边同一国家(或同一帝国)的城市成为它的卫星城。
//   · 谁是主城：按富庶度(人均产出、人口为主，乘地利，见 CityConstructionSystem.Wealth)从高到低排，首都另加三成；
//     全图排名前四成(殷实、富庶两档)、半径 Radius 格内还没有更富的主城的，成为主城。
//     通达层算好后(用户 2026-11-06 同意"先信息类、再贸易迁徙"，见 ConnectivitySystem)不量直线，量人员走过去的成本：
//     circle_max_cost(10) × 世界地图比例尺(见 WorldMapScale.ReachFactor)以内——沿路、沿河谷的城圈得进来，翻山的县不入圈；
//     古代多半是首都或临河临海的城，现代是工商业大城；
//   · 卫星城不会长出自己的主城格局，除非富庶度超过了它的主城(现任主城有一成的在位优势，免得来回换)；
//   · 虹吸：卫星城每月有一部分人口迁往主城(主城还有空房时，颁布逃人法的国家不流动)；
//     主城建设加快(每个卫星城 +6%，至多 +30%)，卫星城建设慢一成半。
// 城市格局(纯净地图)按这里的档位画：主城是都会(主城、门外关厢、门路上的镇、散村)，其余是县城与村落，
// 赤贫的只有几处村子(见 UrbanFabric)。
public static class EconomicCircleSystem
{
    public const float Radius = 60f;
    private const float RefreshSeconds = 10f, Incumbency = 1.1f, CapitalWeight = 1.3f;
    private const float SiphonPerMonth = 0.003f, BoostPerMember = 0.06f, MaxBoost = 0.3f, MemberSlowdown = 0.85f;

    private static readonly Dictionary<long, long> CoreOfCity = new();
    private static readonly Dictionary<long, int> MemberCount = new();
    private static readonly HashSet<long> Cores = new();
    private static readonly List<ConnectivityNeighbour> CircleNear = new();
    private static float _at = -100f;
    private static object _world;

    public static void ResetWorldState()
    {
        CoreOfCity.Clear();
        MemberCount.Clear();
        Cores.Clear();
        _at = -100f;
        _world = null;
    }

#if EC_PENDING
    public static bool IsCore(City city)
    {
        Refresh();
        return city?.Record != null && Cores.Contains(city.Key);
    }
#endif

    // 所在经济圈的主城(主城自己返回自己，不在任何经济圈返回 null)
#if EC_PENDING
    public static City CoreOf(City city)
    {
        Refresh();
        if (city?.Record == null || !CoreOfCity.TryGetValue(city.Key, out long core)) return null;
        City found = EmpireWorld.Current.CityByKey(core);
        return found == null || found.IsGone() ? null : found;
    }

    public static int Members(City core)
    {
        Refresh();
        return core?.Record != null && MemberCount.TryGetValue(core.Key, out int count) ? count : 0;
    }
#endif

    // 城市格局的档位：0 赤贫(几处村子) 1 清贫 2 小康(县城，卫星城至多到这一档) 3 殷实主城 4 富庶主城
#if EC_PENDING
    public static int RegionTier(City city)
    {
        if (city?.Record == null) return 0;
        int bucket = WealthMap.Bucket(WealthMap.Score(city));
        return IsCore(city) ? (bucket >= 4 ? 4 : 3) : Mathf.Min(bucket, 2);
    }
#endif

    // 建设快慢(见 CityConstructionSystem.Settle)
#if EC_PENDING
    public static float ConstructionFactor(City city)
    {
        if (city?.Record == null) return 1f;
        if (IsCore(city)) return 1f + Mathf.Min(MaxBoost, Members(city) * BoostPerMember);
        return CoreOf(city) != null ? MemberSlowdown : 1f;
    }
#endif

    // 每月：卫星城的一部分人口迁往同一国家的主城(见 CityPopulationSystem.Migrate)
#if EC_PENDING
    public static void Siphon(City city, float corePull)
    {
        City core = CoreOf(city);
        if (core == null || core == city || core.Realm != city.Realm || corePull <= 0f) return;
        CityPopulationSystem.TransferBackground(city, core, SiphonPerMonth);
    }
#endif

    private static bool SameRealm(City a, City b)
    {
        if (a.Realm == null || b.Realm == null) return false;
        if (a.Realm == b.Realm) return true;
        Empire empire = a.Realm.GetEmpire();
        return empire != null && empire == b.Realm.GetEmpire();
    }

#if EC_PENDING
    private static float Weight(City city) =>
        WealthMap.Score(city) * (city.IsCapital() ? CapitalWeight : 1f) * (Cores.Contains(city.Key) ? Incumbency : 1f);

    private static void Refresh()
    {
        float now = Time.unscaledTime;
        if (_world != EmpireWorld.Current)
        {
            ResetWorldState();
            _world = EmpireWorld.Current;
        }
        if (now - _at < RefreshSeconds && now >= _at) return;
        _at = now;
        if (EmpireWorld.Current?.Cities == null) return;
        var cities = EmpireWorld.Current.Cities
            .Where(city => city?.Record != null && !city.IsGone() && city.Realm != null && !city.Realm.IsWild &&
                           city.getTile() != null)
            .Select(city => (city, weight: Weight(city)))
            .OrderByDescending(pair => pair.weight)
            .ToList();
        var cores = new List<City>();
        var coreSet = new HashSet<City>();
        CoreOfCity.Clear();
        MemberCount.Clear();
        bool reach = ConnectivitySystem.Ready;
        float limit = ConnectivitySystem.Config.circle_max_cost * WorldMapScale.ReachFactor;
        foreach ((City city, _) in cities)
        {
            WorldTile tile = city.getTile();
            City nearest = null;
            if (reach && ConnectivitySystem.NearCities(city, CircleNear, limit) >= 0)
            {
                // 近邻表按成本由近到远：第一座同属一国的主城
                foreach (ConnectivityNeighbour near in CircleNear)
                {
                    if (!coreSet.Contains(near.City) || !SameRealm(city, near.City)) continue;
                    nearest = near.City;
                    break;
                }
            }
            else
            {
                float best = Radius * Radius;
                foreach (City core in cores)
                {
                    if (!SameRealm(city, core)) continue;
                    WorldTile there = core.getTile();
                    float dx = there.x - tile.x, dy = there.y - tile.y, d = dx * dx + dy * dy;
                    if (d >= best) continue;
                    best = d;
                    nearest = core;
                }
            }
            if (nearest != null)
            {
                CoreOfCity[city.Key] = nearest.Key;
                MemberCount[nearest.Key] = (MemberCount.TryGetValue(nearest.Key, out int count) ? count : 0) + 1;
                continue;
            }
            if (WealthMap.Bucket(WealthMap.Score(city)) < 3) continue;
            cores.Add(city);
            coreSet.Add(city);
            CoreOfCity[city.Key] = city.Key;
        }
        Cores.Clear();
        foreach (City core in cores) Cores.Add(core.Key);
    }
#endif
}
}
