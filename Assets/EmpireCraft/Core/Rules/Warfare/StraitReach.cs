// Generated from the EmpireCraft mod: Scripts/GeneralSystems/Warfare/StraitReach.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using Vector2 = EmpireCraft.Core.EcVector2;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 隔海相望的近邻：宣战、扩张、统一、迫使朝贡原本只挑接壤的国家，近海的小岛永远打不到。
// 隔海的目标要在本国(或所在帝国)水师的辐射范围里(用户 2026-10-15："能攻打隔海相望的目标需要海军的力量能辐射到")：
// 以各水师港口为圆心、NavySystem.ReachRadius 为半径(舰种定最远，战舰战力定几成；没有战舰就过不了海)，
// 对方有临海的城落在圈里、而且与那个港口同一片水域(隔着大陆的另一片海不算)，才算可以宣战的近邻；
// 真打起来照跨海登陆的规矩，也只能打圈里的城，要有不经敌方制海海区的航线与足够的运输船(见 FrontSystem、NavySystem)。
public static class StraitReach
{
    // 各国(帝国按核心)的水师港口与辐射半径，缓存一秒(真实时间)：宣战、诉求挑目标时要对每个国家问一遍
    private const float CacheSeconds = 1f;
    // 港口：圆心取码头所在的格(没有就取城心)，及港外海区所在的水域
    private static readonly Dictionary<long, (float at, List<(Vector2 port, HashSet<int> basins)> ports, float radius)> PortCache = new();

#if EC_PENDING
    public static bool Across(Realm initiator, Realm target)
    {
        if (initiator?.Record == null || target?.Record == null || initiator == target || initiator.IsGone() || target.IsGone())
            return false;
        if (target.Cities == null) return false;
        foreach (City shore in target.Cities)
            if (Reaches(initiator, shore)) return true;
        return false;
    }
#endif

    // 本国(或所在帝国)的水师辐射半径(格)
#if EC_PENDING
    public static float Radius(Realm side) => side?.Record == null || side.IsGone() ? 0f : Navy(side).radius;

    private static (List<(Vector2 port, HashSet<int> basins)> ports, float radius) Navy(Realm initiator)
    {
        Empire empire = initiator.GetEmpire();
        bool imperial = empire != null && !empire.IsGone() && !empire.IsArchived();
        long key = imperial ? -empire.Key : initiator.Key;
        float now = Time.unscaledTime;
        if (PortCache.TryGetValue(key, out var cached) && now >= cached.at && now - cached.at < CacheSeconds)
            return (cached.ports, cached.radius);
        List<Realm> realm = imperial ? empire.kingdoms_list.ToList() : new List<Realm> { initiator };
        if (!realm.Contains(initiator)) realm.Add(initiator);
        float radius = NavySystem.ReachRadius(realm);
        var ports = radius <= 0f ? new List<(Vector2, HashSet<int>)>()
            : realm.Where(kingdom => kingdom?.Record != null && !kingdom.IsGone() && NavySystem.NavyEra(kingdom))
                .SelectMany(NavySystem.Ports).Where(city => city?.Record != null && !city.IsGone())
                .Select(city => (PortAt(city), new HashSet<int>(SeaRegionSystem.RegionsAround(city).Select(region => region.Basin))))
                .Where(entry => entry.Item2.Count > 0).ToList();
        if (PortCache.Count > 512) PortCache.Clear();
        PortCache[key] = (now, ports, radius);
        return (ports, radius);
    }

    private static Vector2 PortAt(City city) =>
        NavySystem.Port(city)?.current_tile is Cell tile ? new Vector2(tile.X, tile.Y) : city.Center;
#endif

    // 这座临海城在本国(或所在帝国)水师的辐射范围里：本城某处海岸离某个水师港口(码头)不过辐射半径、同一片水域
    // (见 FrontSystem.InReach)。以前量城心到城心，城心在内陆的城隔着窄海也够不着
#if EC_PENDING
    public static bool Reaches(Realm side, City target)
    {
        if (side?.Record == null || side.IsGone() || target?.Record == null || target.IsGone()) return false;
        var (ports, radius) = Navy(side);
        if (ports.Count == 0 || radius <= 0f) return false;
        SeaRegionSystem.Shore shore = SeaRegionSystem.ShoreOf(target);
        if (shore.Points.Count == 0) return false;
        float limit = radius * radius, rough = (radius + shore.Offset) * (radius + shore.Offset);
        foreach (var (port, basins) in ports)
        {
            if ((port - target.Center).sqrMagnitude > rough) continue;
            foreach (var (at, basin) in shore.Points)
                if (basins.Contains(basin) && (port - at).sqrMagnitude <= limit) return true;
        }
        return false;
    }
#endif

    // 两家接壤或隔海相望，两边都按全体成员算(帝国连同各成员国)：远距离不开战
    // (用户 2026-10-15："能不能阻止远距离交战，但是很近的海岛却可以")
#if EC_PENDING
    public static bool Within(Realm a, Realm b)
    {
        if (a?.Record == null || b?.Record == null || a.IsGone() || b.IsGone()) return false;
        List<Realm> ours = Members(a), theirs = Members(b);
        foreach (Realm y in theirs)
        {
            if (ours.Contains(y) || Across(a, y)) return true;
            foreach (Realm x in ours)
                if (x.IsNeighbourWith(y)) return true;
        }
        return false;
    }
#endif

    private static List<Realm> Members(Realm kingdom)
    {
        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.IsGone() || empire.IsArchived()) return new List<Realm> { kingdom };
        List<Realm> members = empire.kingdoms_list.Where(member => member?.Record != null && !member.IsGone()).ToList();
        if (!members.Contains(kingdom)) members.Add(kingdom);
        return members;
    }

    // 接壤或隔海相望(帝国按全体成员算)
#if EC_PENDING
    public static bool Near(Realm initiator, Realm target) =>
        initiator != null && target != null &&
        (initiator.IsNeighbourWith(target) || initiator.GetEmpire()?.IsNeighbourWith(target) == true || Across(initiator, target));
#endif
}
}
