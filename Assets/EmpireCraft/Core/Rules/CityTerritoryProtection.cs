// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CityTerritoryProtection.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.

namespace EmpireCraft.Core
{

public static class CityTerritoryProtection
{
    // 正在撤城(见 CityDissolution)：不再保护
    public static readonly global::System.Collections.Generic.HashSet<long> Dissolving = new();

    // 城市边界固定(世界法则，以前是模组设置)：城的地块不再自动增减——不扩张、不缩界、不弃地、不被侵蚀、开荒不并地，无人区也不从城里划地；整城易主照常。
    // 还能动的：世界生成、剧本开局与读档；神力手动划界、营建、建城(ManualClaim、Placing)；新城领落脚那一小片(BeginFounding)；撤城
    private static int _founding;

    public static void BeginFounding() => _founding++;

    public static void EndFounding()
    {
        if (_founding > 0) _founding--;
    }

    public static bool BordersFixed => WorldLaws.FixedCityBorders?.IsEnabled() == true;

    public static bool BordersLocked => BordersFixed && !ModClass.IS_CLEAR && GameClock.Running && !GameClock.Loading &&
                                        !WorldMapGenerator.Generating && !WorldMapScenario.AfterLoadPending;

#if EC_PENDING
    private static bool ManualChange => CityExpansionPatch.ManualClaim || CityPlacementSystem.Placing;

    public static bool LocksAdd(City city) => BordersLocked && !ManualChange && _founding == 0 && city?.Zones?.Count > 0;

    public static bool LocksRemove(City city) => BordersLocked && !ManualChange && city?.Record != null && !city.IsGone() &&
                                                 !Dissolving.Contains(city.Key);
#endif

    // 战争、临时占领都不能触发自动缩界或弃地；只保护辖区，不阻挡整城换国。
    public static bool PreserveWarLayout(City city)
    {
        if (ModClass.IS_CLEAR || !GameClock.Running || GameClock.Loading || city?.Record == null || city.IsGone() ||
            AncientWarfareCompatibility.OwnsObject(city) || Dissolving.Contains(city.Key)) return false;
        return city.Realm?.HasEnemies() == true || city.GetOrCreate().OccupiedStatus?.Count > 0;
    }

    // World teardown marks cities dead before disposal. Only protect live cities
    // that still own territory, so loading another map can clear the old world.
    public static bool ShouldPreventDestruction(City city)
    {
        if (city?.Record == null || city.IsGone() || city.Zones.Count == 0 ||
            AncientWarfareCompatibility.OwnsObject(city) || Dissolving.Contains(city.Key)) return false;
        return WorldLaws.PreventCityDestroy?.IsEnabled() == true ||
               CityPopulationSystem.AbstractPopulationEnabled &&
               (CityPopulationSystem.GetBackgroundTotal(city) > 0f ||
                CityPopulationSystem.Get(city)?.levied > 0f);
    }

#if EC_PENDING
    public static bool ShouldKeepVirtualOwner(City city)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled || city?.Realm == null ||
            city.Realm.IsWild || city.Realm.IsGone()) return false;
        return ShouldPreventDestruction(city) || city.Record != null && !city.IsGone() &&
               city.Zones.Count > 0 && !AncientWarfareCompatibility.OwnsObject(city) &&
               StateSettlementSystem.AwaitingPopulation(city);
    }

    public static bool CanRemoveZone(City city, Cell zone)
    {
        if (city == null || zone == null) return false;
        if (city.Zones.Contains(zone) && LocksRemove(city)) return false;
        return !city.Zones.Contains(zone) || city.Zones.Count > 1 ||
               !ShouldPreventDestruction(city);
    }
#endif
}
}
