// Generated from the EmpireCraft mod: Scripts/HelperFunc/KingdomFrontLineHelper.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Color = EmpireCraft.Core.EcColor;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public static class KingdomFrontLineHelper
{
    private const float NearSideRange = 400f;
    private static int _cacheFrame = -1;
#if EC_PENDING
    private static readonly Dictionary<Realm, HashSet<TileZone>> _kingdomZonesCache = new();
    private static readonly Dictionary<Realm, HashSet<TileZone>> _effectiveZonesCache = new();
#endif
    private static readonly Dictionary<Realm, List<Realm>> _friendlyWarKingdomsCache = new();
#if EC_PENDING
    private static readonly Dictionary<Realm, HashSet<TileZone>> _occupiedZonesByKingdomCache = new();
#endif
    private static bool _occupiedZonesIndexed;

#if EC_PENDING
    public static void ClearCache()
    {
        _cacheFrame = -1;
        _kingdomZonesCache.Clear();
        _effectiveZonesCache.Clear();
        _friendlyWarKingdomsCache.Clear();
        _occupiedZonesByKingdomCache.Clear();
        _occupiedZonesIndexed = false;
    }

    private static void EnsureFrameCache()
    {
        int frame = HighPopulationPerformance.GetFrontLineCacheKey();
        if (_cacheFrame == frame)
        {
            return;
        }

        ClearCache();
        _cacheFrame = frame;
    }
#endif

    /// <summary>
    /// 获取 actorKingdom 面对所有敌国的有效敌方前线 zone。
    /// </summary>
#if EC_PENDING
    public static List<TileZone> GetAllEnemyFrontZones(Realm actorKingdom)
    {
        List<TileZone> result = new List<TileZone>();
        HashSet<TileZone> uniqueZones = new HashSet<TileZone>();

        if (actorKingdom == null)
        {
            return result;
        }

        var enemies = actorKingdom.GetEnemies();

        if (enemies == null)
        {
            return result;
        }

        foreach (Realm enemyKingdom in enemies)
        {
            if (enemyKingdom == null || enemyKingdom.IsGone() || enemyKingdom.IsNeutral())
            {
                continue;
            }

            if (!actorKingdom.IsAtWarWith(enemyKingdom))
            {
                continue;
            }

            List<TileZone> zones = GetEnemyFrontZones(actorKingdom, enemyKingdom);

            foreach (TileZone zone in zones)
            {
                if (zone == null)
                {
                    continue;
                }

                if (uniqueZones.Add(zone))
                {
                    result.Add(zone);
                }
            }
        }

        return result;
    }
#endif

    /// <summary>
    /// 获取 enemyKingdom 面向 actorKingdom 阵营的有效前线。
    /// 如果有直接接壤前线，则返回直接接壤前线。
    /// 如果没有直接接壤，则返回敌国靠近 actorKingdom 阵营的那一侧边界。
    /// </summary>
#if EC_PENDING
    public static List<TileZone> GetEnemyFrontZones(Realm actorKingdom, Realm enemyKingdom)
    {
        List<TileZone> directZones = GetDirectEnemyFrontZones(actorKingdom, enemyKingdom);

        if (directZones.Count > 0)
        {
            return directZones;
        }

        return GetNearSideEnemyBorderZones(actorKingdom, enemyKingdom, NearSideRange);
    }
#endif

    /// <summary>
    /// 获取直接接壤的敌方前线。
    /// enemyZone 属于 enemyKingdom，
    /// 并且它相邻的 zone 中存在 actorKingdom 同阵营控制区。
    /// </summary>
#if EC_PENDING
    public static List<TileZone> GetDirectEnemyFrontZones(Realm actorKingdom, Realm enemyKingdom)
    {
        List<TileZone> result = new List<TileZone>();

        if (actorKingdom == null || enemyKingdom == null)
        {
            return result;
        }

        HashSet<TileZone> enemyZones = GetEffectiveControlledZones(enemyKingdom);

        if (enemyZones.Count == 0)
        {
            return result;
        }

        foreach (TileZone enemyZone in enemyZones)
        {
            if (!IsValidZone(enemyZone))
            {
                continue;
            }

            Realm occupier = GetZoneOccupier(enemyZone);

            // 已经被己方或友军占领的敌方 zone，不再算敌方前线目标
            if (occupier != null && IsFriendlyKingdom(actorKingdom, occupier))
            {
                continue;
            }

            bool touchesFriendlyControlledZone = false;
            bool touchesNonWarKingdom = false;

            if (enemyZone.neighbours_all == null)
            {
                continue;
            }

            foreach (TileZone neighbour in enemyZone.neighbours_all)
            {
                if (!IsValidZone(neighbour))
                {
                    continue;
                }

                Realm neighbourController = GetEffectiveZoneController(neighbour);

                // 友军占领区算作我方控制区
                if (IsFriendlyControlledZone(actorKingdom, neighbour))
                {
                    touchesFriendlyControlledZone = true;
                    continue;
                }

                // 自己或同阵营友军原生领土，也算作我方控制区

                // 敌国自己的 zone，不算边界
                if (neighbourController == enemyKingdom)
                {
                    continue;
                }

                // 接壤非交战第三方国家的边界，不算当前战争前线
                if (neighbourController != null &&
                    !IsFriendlyKingdom(actorKingdom, neighbourController) &&
                    !actorKingdom.IsAtWarWith(neighbourController))
                {
                    touchesNonWarKingdom = true;
                    break;
                }
            }

            if (!touchesFriendlyControlledZone)
            {
                continue;
            }

            if (touchesNonWarKingdom)
            {
                continue;
            }

            result.Add(enemyZone);
        }

        return result;
    }
#endif

    /// <summary>
    /// 不直接接壤时，获取敌国靠近 actorKingdom 同阵营的那一侧边界。
    /// 注意：这里不会使用所有友军国土，而只使用“面向该敌国的友军源头 zone”。
    /// </summary>
#if EC_PENDING
    public static List<TileZone> GetNearSideEnemyBorderZones(Realm actorKingdom, Realm enemyKingdom, float range)
    {
        List<TileZone> result = new List<TileZone>();

        if (actorKingdom == null || enemyKingdom == null)
        {
            return result;
        }

        HashSet<TileZone> sourceZones = GetFriendlyFrontSourceZonesFacingEnemy(actorKingdom, enemyKingdom);
        HashSet<TileZone> enemyZones = GetEffectiveControlledZones(enemyKingdom);

        if (sourceZones.Count == 0 || enemyZones.Count == 0)
        {
            return result;
        }

        List<TileZone> enemyBorderZones = new List<TileZone>();

        foreach (TileZone enemyZone in enemyZones)
        {
            if (!IsValidZone(enemyZone))
            {
                continue;
            }

            Realm occupier = GetZoneOccupier(enemyZone);

            // 已经被己方或友军占领的敌方 zone，不再作为敌方边界目标
            if (occupier != null && IsFriendlyKingdom(actorKingdom, occupier))
            {
                continue;
            }

            bool isBorder = false;
            bool touchesNonWarKingdom = false;

            if (enemyZone.neighbours_all == null)
            {
                continue;
            }

            foreach (TileZone neighbour in enemyZone.neighbours_all)
            {
                if (!IsValidZone(neighbour))
                {
                    continue;
                }

                Realm neighbourController = GetEffectiveZoneController(neighbour);

                if (neighbourController != enemyKingdom)
                {
                    isBorder = true;

                    // 如果这段边界接壤的是中立或非当前战争国家，则排除
                    if (neighbourController != null &&
                        !IsFriendlyKingdom(actorKingdom, neighbourController) &&
                        !actorKingdom.IsAtWarWith(neighbourController))
                    {
                        touchesNonWarKingdom = true;
                        break;
                    }
                }
            }

            if (!isBorder)
            {
                continue;
            }

            if (touchesNonWarKingdom)
            {
                continue;
            }

            enemyBorderZones.Add(enemyZone);
        }

        if (enemyBorderZones.Count == 0)
        {
            return result;
        }

        float nearestDistance = float.MaxValue;
        Dictionary<TileZone, float> bestDistances = new Dictionary<TileZone, float>(enemyBorderZones.Count);

        foreach (TileZone enemyBorderZone in enemyBorderZones)
        {
            float bestDistanceToSource = float.MaxValue;

            foreach (TileZone sourceZone in sourceZones)
            {
                float distance = GetZoneDistanceSqr(enemyBorderZone, sourceZone);

                if (distance < bestDistanceToSource)
                {
                    bestDistanceToSource = distance;
                }
            }

            if (bestDistanceToSource < float.MaxValue)
            {
                bestDistances[enemyBorderZone] = bestDistanceToSource;
            }

            if (bestDistanceToSource < nearestDistance)
            {
                nearestDistance = bestDistanceToSource;
            }
        }

        if (nearestDistance == float.MaxValue)
        {
            return result;
        }

        foreach (TileZone enemyBorderZone in enemyBorderZones)
        {
            if (bestDistances.TryGetValue(enemyBorderZone, out float bestDistanceToSource) &&
                bestDistanceToSource <= nearestDistance + range)
            {
                result.Add(enemyBorderZone);
            }
        }

        return result;
    }
#endif

    /// <summary>
    /// 获取“面向 enemyKingdom 的友军前线源头”。
    /// 这个方法不会返回所有友军国土，只返回：
    /// 1. actorKingdom 自己的国土；
    /// 2. 与 enemyKingdom 接壤的同阵营友军 zone；
    /// 3. 已经被同阵营友军占领的 enemyKingdom zone。
    /// </summary>
#if EC_PENDING
    public static HashSet<TileZone> GetFriendlyFrontSourceZonesFacingEnemy(Realm actorKingdom, Realm enemyKingdom)
    {
        HashSet<TileZone> result = new HashSet<TileZone>();

        if (actorKingdom == null || enemyKingdom == null)
        {
            return result;
        }

        HashSet<TileZone> enemyZones = GetEffectiveControlledZones(enemyKingdom);

        if (enemyZones.Count == 0)
        {
            return result;
        }

        // actorKingdom 自己的国土永远作为基础方向来源
        foreach (TileZone zone in GetEffectiveControlledZones(actorKingdom))
        {
            if (IsValidZone(zone) && IsFriendlyControlledZone(actorKingdom, zone))
            {
                result.Add(zone);
            }
        }

        // 友军只有在真正接壤当前 enemyKingdom 时，才作为方向来源
        foreach (Realm friendlyKingdom in GetFriendlyWarKingdoms(actorKingdom))
        {
            if (friendlyKingdom == actorKingdom)
            {
                continue;
            }

            foreach (TileZone zone in GetEffectiveControlledZones(friendlyKingdom))
            {
                if (!IsValidZone(zone))
                {
                    continue;
                }

                if (!IsFriendlyControlledZone(actorKingdom, zone))
                {
                    continue;
                }

                if (ZoneTouchesKingdom(zone, enemyKingdom))
                {
                    result.Add(zone);
                }
            }
        }

        // 已经被任意同阵营友军占领的敌方 zone，也作为下一轮推进源头
        foreach (TileZone enemyZone in enemyZones)
        {
            if (!IsValidZone(enemyZone))
            {
                continue;
            }

            Realm occupier = GetZoneOccupier(enemyZone);

            if (occupier != null && IsFriendlyKingdom(actorKingdom, occupier))
            {
                result.Add(enemyZone);
            }
        }

        return result;
    }
#endif

    /// <summary>
    /// 获取所有同一战争阵营的友军国家，包括 actorKingdom 自己。
    /// </summary>
#if EC_PENDING
    public static List<Realm> GetFriendlyWarKingdoms(Realm actorKingdom)
    {
        EnsureFrameCache();

        if (actorKingdom == null)
        {
            return new List<Realm>();
        }

        if (_friendlyWarKingdomsCache.TryGetValue(actorKingdom, out var cachedResult))
        {
            return cachedResult;
        }

        List<Realm> result = new List<Realm>();
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms)
        {
            if (kingdom == null || kingdom.IsGone() || kingdom.IsNeutral())
            {
                continue;
            }

            if (!IsFriendlyKingdom(actorKingdom, kingdom))
            {
                continue;
            }

            if (!result.Contains(kingdom))
            {
                result.Add(kingdom);
            }
        }

        _friendlyWarKingdomsCache[actorKingdom] = result;
        return result;
    }
#endif

    /// <summary>
    /// 获取所有同阵营控制区。
    /// 这个方法可以用于士兵填充、连续战线判断。
    /// 不要用它来判断“不接壤时的敌国靠近哪一侧”，否则会把远处友军国土也算进去。
    /// </summary>
#if EC_PENDING
    public static HashSet<TileZone> GetAllFriendlyControlledZones(Realm actorKingdom)
    {
        HashSet<TileZone> result = new HashSet<TileZone>();

        if (actorKingdom == null)
        {
            return result;
        }

        foreach (Realm friendlyKingdom in GetFriendlyWarKingdoms(actorKingdom))
        {
            foreach (TileZone zone in GetEffectiveControlledZones(friendlyKingdom))
            {
                if (IsValidZone(zone))
                {
                    result.Add(zone);
                }
            }
        }

        return result;
    }
#endif

    /// <summary>
    /// 获取某个王国全部城市 zone。
    /// </summary>
#if EC_PENDING
    public static HashSet<TileZone> GetKingdomZones(Realm kingdom)
    {
        EnsureFrameCache();
        HashSet<TileZone> result = new HashSet<TileZone>();

        if (kingdom == null || kingdom.Cities == null)
        {
            return result;
        }

        if (_kingdomZonesCache.TryGetValue(kingdom, out var cachedResult))
        {
            return cachedResult;
        }

        foreach (City city in kingdom.Cities)
        {
            if (city == null || city.zones == null)
            {
                continue;
            }

            foreach (TileZone zone in city.zones)
            {
                if (IsValidZone(zone))
                {
                    result.Add(zone);
                }
            }
        }

        _kingdomZonesCache[kingdom] = result;
        return result;
    }

    public static HashSet<TileZone> GetEffectiveControlledZones(Realm kingdom)
    {
        EnsureFrameCache();
        HashSet<TileZone> result = new HashSet<TileZone>();

        if (kingdom == null)
        {
            return result;
        }

        if (_effectiveZonesCache.TryGetValue(kingdom, out var cachedResult))
        {
            return cachedResult;
        }

        foreach (TileZone zone in GetKingdomZones(kingdom))
        {
            if (!IsValidZone(zone))
            {
                continue;
            }

            Realm occupier = GetZoneOccupier(zone);
            if (occupier == null || occupier == kingdom)
            {
                result.Add(zone);
            }
        }

        EnsureOccupiedZoneIndex();
        if (_occupiedZonesByKingdomCache.TryGetValue(kingdom, out var occupiedZones))
        {
            foreach (TileZone zone in occupiedZones)
            {
                if (IsValidZone(zone))
                {
                    result.Add(zone);
                }
            }
        }

        _effectiveZonesCache[kingdom] = result;
        return result;
    }

    private static void EnsureOccupiedZoneIndex()
    {
        EnsureFrameCache();
        if (_occupiedZonesIndexed)
        {
            return;
        }

        _occupiedZonesIndexed = true;
        if (EmpireWorld.Current?.Cities == null)
        {
            return;
        }

        foreach (City city in EmpireWorld.Current.Cities)
        {
            if (city == null || city.IsGone() || city.zones == null)
            {
                continue;
            }

            foreach (TileZone zone in city.zones)
            {
                if (!IsValidZone(zone))
                {
                    continue;
                }

                Realm occupier = GetZoneOccupier(zone);
                if (occupier == null)
                {
                    continue;
                }

                if (!_occupiedZonesByKingdomCache.TryGetValue(occupier, out var zones))
                {
                    zones = new HashSet<TileZone>();
                    _occupiedZonesByKingdomCache[occupier] = zones;
                }

                zones.Add(zone);
            }
        }
    }
#endif

    /// <summary>
    /// 判断一个 zone 是否与指定王国接壤。
    /// </summary>
#if EC_PENDING
    public static bool ZoneTouchesKingdom(TileZone zone, Realm kingdom)
    {
        if (!IsValidZone(zone) || kingdom == null)
        {
            return false;
        }

        if (zone.neighbours_all == null)
        {
            return false;
        }

        HashSet<TileZone> controlledZones = GetEffectiveControlledZones(kingdom);

        foreach (TileZone neighbour in zone.neighbours_all)
        {
            if (!IsValidZone(neighbour))
            {
                continue;
            }

            if (controlledZones.Contains(neighbour))
            {
                return true;
            }
        }

        return false;
    }
#endif

    /// <summary>
    /// 获取 zone 原本所属王国。
    /// </summary>
#if EC_PENDING
    public static Realm GetZoneKingdom(TileZone zone)
    {
        if (zone == null || zone.city == null)
        {
            return null;
        }

        return zone.city.kingdom;
    }
#endif

    /// <summary>
    /// 获取 zone 当前占领国。
    /// </summary>
#if EC_PENDING
    public static Realm GetZoneOccupier(TileZone zone)
    {
        if (zone == null || zone.city == null)
        {
            return null;
        }

        return zone.city.GetTileZoneOccupier(zone);
    }

    public static Realm GetEffectiveZoneController(TileZone zone)
    {
        if (zone == null || zone.city == null)
        {
            return null;
        }

        return GetZoneOccupier(zone) ?? GetZoneKingdom(zone);
    }

    public static bool IsFriendlyControlledZone(Realm actorKingdom, TileZone zone)
    {
        if (actorKingdom == null || !IsValidZone(zone))
        {
            return false;
        }

        Realm controller = GetEffectiveZoneController(zone);
        return controller != null && IsFriendlyKingdom(actorKingdom, controller);
    }
#endif

    /// <summary>
    /// 判断两个王国是否属于同一战争阵营。
    /// </summary>
    public static bool IsFriendlyKingdom(Realm actorKingdom, Realm otherKingdom)
    {
        if (actorKingdom == null || otherKingdom == null)
        {
            return false;
        }

        if (actorKingdom == otherKingdom)
        {
            return true;
        }

        return actorKingdom.IsOnSameSide(otherKingdom);
    }

#if EC_PENDING
    public static bool IsValidZone(TileZone zone)
    {
        if (zone == null)
        {
            return false;
        }

        if (zone.world_edge)
        {
            return false;
        }

        return true;
    }

    public static float GetZoneDistanceSqr(TileZone a, TileZone b)
    {
        if (a == null || b == null)
        {
            return float.MaxValue;
        }

        int dx = a.x - b.x;
        int dy = a.y - b.y;

        return dx * dx + dy * dy;
    }
#endif

    /// <summary>
    /// 统计 zone 内同阵营士兵数量。
    /// 用于判断某个前线 zone 是否已经被友军填满。
    /// </summary>
#if EC_PENDING
    public static int CountFriendlyWarriorsInZone(TileZone zone, Realm actorKingdom)
    {
        if (zone == null || actorKingdom == null)
        {
            return 0;
        }

        int count = 0;

        foreach (Person actor in EmpireWorld.Current.Living)
        {
            if (actor == null || actor.Realm == null)
            {
                continue;
            }

            if (!actor.isWarrior())
            {
                continue;
            }

            if (actor.current_tile == null || actor.current_tile.zone != zone)
            {
                continue;
            }

            if (IsFriendlyKingdom(actorKingdom, actor.Realm))
            {
                count++;
            }
        }

        return count;
    }
#endif

    /// <summary>
    /// 调试用：打印并标记该王国面对所有敌人的前线。
    /// </summary>
#if EC_PENDING
    public static void DebugFrontZones(Realm actorKingdom)
    {
        if (actorKingdom == null)
        {
            LogService.LogInfo("FrontLine Debug: actorKingdom is null");
            return;
        }

        var enemies = actorKingdom.GetEnemies();

        if (enemies == null)
        {
            LogService.LogInfo("FrontLine Debug: no enemies");
            return;
        }

        foreach (Realm enemyKingdom in enemies)
        {
            if (enemyKingdom == null || enemyKingdom.IsGone() || enemyKingdom.IsNeutral())
            {
                continue;
            }

            if (!actorKingdom.IsAtWarWith(enemyKingdom))
            {
                continue;
            }

            List<TileZone> directZones = GetDirectEnemyFrontZones(actorKingdom, enemyKingdom);
            List<TileZone> finalZones = GetEnemyFrontZones(actorKingdom, enemyKingdom);

            LogService.LogInfo(
                "FrontLine Debug | " +
                actorKingdom.Name +
                " vs " +
                enemyKingdom.Name +
                " | direct front zones: " +
                directZones.Count +
                " | final front zones: " +
                finalZones.Count
            );

            foreach (TileZone zone in finalZones)
            {
                if (zone == null)
                {
                    continue;
                }

                MarkDebugFrontZone(zone);

                LogService.LogInfo(
                    "FrontZone: enemy=" +
                    enemyKingdom.Name +
                    " zone=(" +
                    zone.x +
                    "," +
                    zone.y +
                    ") city=" +
                    (zone.city == null ? "null" : zone.city.data.name)
                );
            }
        }
    }

    public static void MarkDebugFrontZone(TileZone zone)
    {
        if (zone == null)
        {
            return;
        }

        WorldTile tile = zone.centerTile;

        if (tile == null)
        {
            return;
        }

        if (tile.zone == null)
        {
            return;
        }

        if (tile.zone.world_edge)
        {
            return;
        }

        HighlightZones(zone, Color.red);
    }

    public static void HighlightZones(TileZone zone, Color pColor, float pAlpha = 0.8f)
    {
        if (zone == null || zone.centerTile == null)
        {
            return;
        }

        // 纯净地图下不画一闪一闪的区块高亮
        if (EmpireCraft.Core.PureMapMode.Enabled) return;
        ((ZoneFlash)EffectsLibrary.spawn("fx_zone_highlight", zone.centerTile, null, null, pAlpha)).start(pColor, pAlpha);
    }
#endif
}
}
