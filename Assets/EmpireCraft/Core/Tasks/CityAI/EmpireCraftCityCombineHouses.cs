// Generated from the EmpireCraft mod: Scripts/AI/CityAI/EmpireCraftCityCombineHouses.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public class EmpireCraftCityCombineHouses: GameAICityBase
{
    public override Type OriginalBeh => GetType();

    // 城里找不到空地放城池的，一年内不再每次都扫全城
    private static readonly Dictionary<long, double> FailedAt = new();

#if EC_PENDING
    public override BehResult Execute(City pCity)
    {
        if (pCity?.Realm == null) return BehResult.Continue;
        if (!pCity.Realm.HasEnemies()) pCity.ClearOccupiedStatus();
        // 建筑台账(见 CityLedger)：民居只是账上的数，没有实体可并成城池
        if (EmpireCraft.Core.CityLedger.Uses(pCity)) return BehResult.Continue;
        // 无小人模式下城里实体单位很少，按户数判断(一户占一个住房位)
        int population = CityPopulationSystem.AbstractPopulationEnabled
            ? CityPopulationSystem.Households(pCity)
            : pCity.PopulationCount();
        if (!pCity.Buildings.ToList().Any(b => b.Asset.Id.Contains("city_")))
        {
            // 世界法则关掉合并时保留原版民居(modernmod 的 6~11 级民居也是民居，合并会把它们全拆掉)；
            // 已经住进 modernmod 高级民居的城市也不再合并
            if (WorldLaws.CombineHouses?.IsEnabled() == false) return BehResult.Continue;
            if (pCity.Buildings.Any(b => b.Asset.Type == "type_house" && b.Asset.Id.EndsWith("_modernmod")))
                return BehResult.Continue;
            if (population > 80) Combine(pCity, population);
        }
        else
        {
            var building = pCity.Buildings.Find(b=>b.Asset.Id.Contains("city_"));
            if (building != null)
            {
                if (population >= 100)
                {
                    if (building.Asset.UpgradeLevel == 1)
                    {
                        building.upgradeBuilding();
                    }
                }

                if (population >= 150 || (pCity.IsCapital() && pCity.Realm.IsEmpire()))
                {
                    if (building.Asset.UpgradeLevel <= 1)
                    {
                        building.upgradeBuilding();
                        building.upgradeBuilding();
                    }
                    else
                    {
                        building.upgradeBuilding();
                    }

                }
            }
        }
        return BehResult.Continue;
    }
#endif

    // 先在城里找空地放下城池，放成了才拆民居，而且只拆到剩下的住房仍够住为止。
    // 以前先拆光民居再在原处放城池，放不下时全城住房归零，之后新造的民居(包括手动造的)也会被再拆一遍
#if EC_PENDING
    private static void Combine(City city, int households)
    {
        if (FailedAt.TryGetValue(city.Key, out double failed) && GameClock.YearsSince(failed) < 1) return;
        var culture = city.Realm?.GetEmpireCraftCulture();
        culture = EmpireCraftBuildingLibrary.BuildingCulture(culture);
        BuildingKind asset = BuildingKinds.Table.Get($"city_{culture?.ToLower()}_0");
        if (asset == null) return;
        Building placed = Place(city, asset);
        if (placed == null)
        {
            FailedAt[city.Key] = GameClock.Now;
            return;
        }
        FailedAt.Remove(city.Key);
        if (placed.IsUnderConstruction()) return;
        int spare = city.status.housing_total + asset.HousingSlots - households;
        foreach (Building house in city.Buildings.ToList())
        {
            if (house == null || house == placed || house.IsOnRemove() || house.Asset.Type != "type_house" ||
                house.Asset.Id.Contains("city_")) continue;
            if (spare - house.Asset.HousingSlots < 0) continue;
            spare -= house.Asset.HousingSlots;
            house.startRemove();
        }
    }
#endif

    // 从城中心往外找：不放进规划农田(见 FarmlandSystem，风车范围里不盖民居)
#if EC_PENDING
    private static Building Place(City city, BuildingKind asset)
    {
        if (city.Zones == null) return null;
        Vector2 center = city.Center;
        foreach (Cell zone in city.Zones.ToList()
                     .Where(zone => zone?.Tiles != null && zone.City == city && !FarmlandSystem.IsPlanned(city, zone))
                     .OrderBy(zone => Vector2.SqrMagnitude(new Vector2(zone.Center.X, zone.Center.Y) - center)))
        {
            foreach (Cell tile in zone.Tiles)
                if (EmpireWorld.Current.buildings.canBuildFrom(tile, asset, city))
                    return EmpireWorld.Current.buildings.addBuilding(asset, tile);
        }
        return null;
    }
#endif
}
}
