// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CityDissolution.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Vector2 = EmpireCraft.Core.EcVector2;
using System.Linq;

namespace EmpireCraft.Core
{

// 撤城与弃城(无小人模式下城里只要还记着人口就一直受保护，见 CityTerritoryProtection)：
//   · 撤城(神力 dissolve_city)：百姓与城里的人物迁到本国最近的城，城市当即废弃，建筑成为无主废墟；
//     本国只剩这一座城时，第一次点只提示会灭国，几秒内再点同一座城就彻底清除：百姓、本国所有人物(含船)一并清掉，
//     城市废弃，国家随之灭亡(原版在国家没有城、没有人、没有建筑时移除它)；无主城(野外、中立)直接废弃；
//   · 弃城：关闭「防止城市被摧毁」时，住不了人(住房上限为 0、没有在建的房子)、建城满三年的城，
//     百姓每月四成逃往本国最近的城，走光后人物也迁走，城市按原版规则废弃。
public static class CityDissolution
{
    public static bool IsLastCity(City city)
    {
        Realm kingdom = city?.Realm;
        return kingdom != null && !kingdom.IsWild && !kingdom.IsGone() &&
               (kingdom.Cities?.Count(other => other != null && !other.IsGone()) ?? 0) <= 1;
    }

    // 撤城：成功返回 null，否则返回提示文本键；wipeKingdom = 本国最后一座城也撤，连同国家彻底清除
#if EC_PENDING
    public static string Dissolve(City city, bool wipeKingdom = false)
    {
        if (city?.Record == null || city.IsGone()) return "dissolve_city_none";
        Realm kingdom = city.Realm;
        bool owned = kingdom != null && !kingdom.IsWild && !kingdom.IsGone();
        if (owned && IsLastCity(city))
        {
            if (!wipeKingdom) return "dissolve_city_last";
            Wipe(city, kingdom);
            return null;
        }
        City destination = owned ? Nearest(city) : null;
        CityTerritoryProtection.Dissolving.Add(city.Key);
        if (destination != null)
        {
            CityPopulationSystem.TransferBackground(city, destination, 1f);
            MoveUnits(city, destination);
            if (kingdom.Capital == city) kingdom.SetCapital(destination);
        }
        else CityPopulationSystem.ClearBackground(city);
        city.turnCityToNeutral();
        foreach (Cell zone in city.Zones.ToList()) city.RemoveZone(zone);
        CityPopulationSystem.InvalidateHouseholdCaches();
        return null;
    }
#endif

    // 彻底清除本国最后一座城：百姓、本国所有人物(含船)清掉，城市废弃
#if EC_PENDING
    private static void Wipe(City city, Realm kingdom)
    {
        CityTerritoryProtection.Dissolving.Add(city.Key);
        CityPopulationSystem.ClearBackground(city);
        foreach (Person unit in kingdom.People.ToList())
            if (unit?.Record != null && !unit.IsGone() && unit.Alive) unit.Die(true, DeathCause.Other, false, false);
        city.turnCityToNeutral();
        foreach (Cell zone in city.Zones.ToList()) city.RemoveZone(zone);
        CityPopulationSystem.InvalidateHouseholdCaches();
    }
#endif

    // 弃城(每月，见 CityPopulationSystem 的迁徙一步)：返回 true 表示本月已经在逃散
    public static bool TryAbandon(City city)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.Record == null || city.IsGone() ||
            city.Realm == null || city.Realm.IsWild) return false;
        if (WorldLaws.PreventCityDestroy?.IsEnabled() == true) return false;
        if (city.GetPopulationMaximum() > 0 || GameClock.YearsSince(city.FoundedStamp) < 3) return false;
        if (city.Buildings?.Any(building => building != null && building.IsUnderConstruction()) == true) return false;
        if (CityLedger.Pending(city) != null) return false;
        if (StateSettlementSystem.AwaitingPopulation(city)) return false;
        City destination = Nearest(city);
        if (destination == null) return false;
        float total = CityPopulationSystem.GetBackgroundTotal(city);
        if (total >= 1f) CityPopulationSystem.TransferBackground(city, destination, total < 20f ? 1f : 0.4f);
        if (CityPopulationSystem.GetBackgroundTotal(city) < 1f) MoveUnits(city, destination);
        return true;
    }

    private static City Nearest(City city)
    {
        Vector2 center = city.Center;
        return city.Realm?.Cities?.Where(other => other != null && other != city && !other.IsGone() &&
                                                    !CityTerritoryProtection.Dissolving.Contains(other.Key))
            .OrderBy(other => Vector2.SqrMagnitude(other.Center - center)).FirstOrDefault();
    }

    private static void MoveUnits(City city, City destination)
    {
        foreach (Person unit in city.People.ToList())
        {
            if (unit?.Record == null || unit.IsGone() || !unit.Alive) continue;
            if (unit.IsCityLeader() && unit.City == city) city.RemoveLeader();
            unit.JoinCity(destination);
        }
    }
}
}
