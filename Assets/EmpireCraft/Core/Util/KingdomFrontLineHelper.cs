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
    private static readonly Dictionary<Realm, HashSet<Cell>> _kingdomZonesCache = new();
    private static readonly Dictionary<Realm, HashSet<Cell>> _effectiveZonesCache = new();
    private static readonly Dictionary<Realm, List<Realm>> _friendlyWarKingdomsCache = new();
    private static readonly Dictionary<Realm, HashSet<Cell>> _occupiedZonesByKingdomCache = new();
    private static bool _occupiedZonesIndexed;

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

    /// <summary>
    /// 获取 actorKingdom 面对所有敌国的有效敌方前线 zone。
    /// </summary>
    public static List<Cell> GetAllEnemyFrontZones(Realm actorKingdom)
    {
        List<Cell> result = new List<Cell>();
        HashSet<Cell> uniqueZones = new HashSet<Cell>();

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

            List<Cell> zones = GetEnemyFrontZones(actorKingdom, enemyKingdom);

            foreach (Cell zone in zones)
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

    /// <summary>
    /// 获取 enemyKingdom 面向 actorKingdom 阵营的有效前线。
    /// 如果有直接接壤前线，则返回直接接壤前线。
    /// 如果没有直接接壤，则返回敌国靠近 actorKingdom 阵营的那一侧边界。
    /// </summary>
    public static List<Cell> GetEnemyFrontZones(Realm actorKingdom, Realm enemyKingdom)
    {
        List<Cell> directZones = GetDirectEnemyFrontZones(actorKingdom, enemyKingdom);

        if (directZones.Count > 0)
        {
            return directZones;
        }

        return GetNearSideEnemyBorderZones(actorKingdom, enemyKingdom, NearSideRange);
    }

    /// <summary>
    /// 获取直接接壤的敌方前线。
    /// enemyZone 属于 enemyKingdom，
    /// 并且它相邻的 zone 中存在 actorKingdom 同阵营控制区。
    /// </summary>
    public static List<Cell> GetDirectEnemyFrontZones(Realm actorKingdom, Realm enemyKingdom)
    {
        List<Cell> result = new List<Cell>();

        if (actorKingdom == null || enemyKingdom == null)
        {
            return result;
        }

        HashSet<Cell> enemyZones = GetEffectiveControlledZones(enemyKingdom);

        if (enemyZones.Count == 0)
        {
            return result;
        }

        foreach (Cell enemyZone in enemyZones)
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

            if (enemyZone.NeighboursAll == null)
            {
                continue;
            }

            foreach (Cell neighbour in enemyZone.NeighboursAll)
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

    /// <summary>
    /// 不直接接壤时，获取敌国靠近 actorKingdom 同阵营的那一侧边界。
    /// 注意：这里不会使用所有友军国土，而只使用“面向该敌国的友军源头 zone”。
    /// </summary>
    public static List<Cell> GetNearSideEnemyBorderZones(Realm actorKingdom, Realm enemyKingdom, float range)
    {
        List<Cell> result = new List<Cell>();

        if (actorKingdom == null || enemyKingdom == null)
        {
            return result;
        }

        HashSet<Cell> sourceZones = GetFriendlyFrontSourceZonesFacingEnemy(actorKingdom, enemyKingdom);
        HashSet<Cell> enemyZones = GetEffectiveControlledZones(enemyKingdom);

        if (sourceZones.Count == 0 || enemyZones.Count == 0)
        {
            return result;
        }

        List<Cell> enemyBorderZones = new List<Cell>();

        foreach (Cell enemyZone in enemyZones)
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

            if (enemyZone.NeighboursAll == null)
            {
                continue;
            }

            foreach (Cell neighbour in enemyZone.NeighboursAll)
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
        Dictionary<Cell, float> bestDistances = new Dictionary<Cell, float>(enemyBorderZones.Count);

        foreach (Cell enemyBorderZone in enemyBorderZones)
        {
            float bestDistanceToSource = float.MaxValue;

            foreach (Cell sourceZone in sourceZones)
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

        foreach (Cell enemyBorderZone in enemyBorderZones)
        {
            if (bestDistances.TryGetValue(enemyBorderZone, out float bestDistanceToSource) &&
                bestDistanceToSource <= nearestDistance + range)
            {
                result.Add(enemyBorderZone);
            }
        }

        return result;
    }

    /// <summary>
    /// 获取“面向 enemyKingdom 的友军前线源头”。
    /// 这个方法不会返回所有友军国土，只返回：
    /// 1. actorKingdom 自己的国土；
    /// 2. 与 enemyKingdom 接壤的同阵营友军 zone；
    /// 3. 已经被同阵营友军占领的 enemyKingdom zone。
    /// </summary>
    public static HashSet<Cell> GetFriendlyFrontSourceZonesFacingEnemy(Realm actorKingdom, Realm enemyKingdom)
    {
        HashSet<Cell> result = new HashSet<Cell>();

        if (actorKingdom == null || enemyKingdom == null)
        {
            return result;
        }

        HashSet<Cell> enemyZones = GetEffectiveControlledZones(enemyKingdom);

        if (enemyZones.Count == 0)
        {
            return result;
        }

        // actorKingdom 自己的国土永远作为基础方向来源
        foreach (Cell zone in GetEffectiveControlledZones(actorKingdom))
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

            foreach (Cell zone in GetEffectiveControlledZones(friendlyKingdom))
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
        foreach (Cell enemyZone in enemyZones)
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

    /// <summary>
    /// 获取所有同一战争阵营的友军国家，包括 actorKingdom 自己。
    /// </summary>
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

    /// <summary>
    /// 获取所有同阵营控制区。
    /// 这个方法可以用于士兵填充、连续战线判断。
    /// 不要用它来判断“不接壤时的敌国靠近哪一侧”，否则会把远处友军国土也算进去。
    /// </summary>
    public static HashSet<Cell> GetAllFriendlyControlledZones(Realm actorKingdom)
    {
        HashSet<Cell> result = new HashSet<Cell>();

        if (actorKingdom == null)
        {
            return result;
        }

        foreach (Realm friendlyKingdom in GetFriendlyWarKingdoms(actorKingdom))
        {
            foreach (Cell zone in GetEffectiveControlledZones(friendlyKingdom))
            {
                if (IsValidZone(zone))
                {
                    result.Add(zone);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 获取某个王国全部城市 zone。
    /// </summary>
    public static HashSet<Cell> GetKingdomZones(Realm kingdom)
    {
        EnsureFrameCache();
        HashSet<Cell> result = new HashSet<Cell>();

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
            if (city == null || city.Zones == null)
            {
                continue;
            }

            foreach (Cell zone in city.Zones)
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

    public static HashSet<Cell> GetEffectiveControlledZones(Realm kingdom)
    {
        EnsureFrameCache();
        HashSet<Cell> result = new HashSet<Cell>();

        if (kingdom == null)
        {
            return result;
        }

        if (_effectiveZonesCache.TryGetValue(kingdom, out var cachedResult))
        {
            return cachedResult;
        }

        foreach (Cell zone in GetKingdomZones(kingdom))
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
            foreach (Cell zone in occupiedZones)
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
            if (city == null || city.IsGone() || city.Zones == null)
            {
                continue;
            }

            foreach (Cell zone in city.Zones)
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
                    zones = new HashSet<Cell>();
                    _occupiedZonesByKingdomCache[occupier] = zones;
                }

                zones.Add(zone);
            }
        }
    }

    /// <summary>
    /// 判断一个 zone 是否与指定王国接壤。
    /// </summary>
    public static bool ZoneTouchesKingdom(Cell zone, Realm kingdom)
    {
        if (!IsValidZone(zone) || kingdom == null)
        {
            return false;
        }

        if (zone.NeighboursAll == null)
        {
            return false;
        }

        HashSet<Cell> controlledZones = GetEffectiveControlledZones(kingdom);

        foreach (Cell neighbour in zone.NeighboursAll)
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

    /// <summary>
    /// 获取 zone 原本所属王国。
    /// </summary>
    public static Realm GetZoneKingdom(Cell zone)
    {
        if (zone == null || zone.City == null)
        {
            return null;
        }

        return zone.City.Realm;
    }

    /// <summary>
    /// 获取 zone 当前占领国。
    /// </summary>
    public static Realm GetZoneOccupier(Cell zone)
    {
        if (zone == null || zone.City == null)
        {
            return null;
        }

        return zone.City.GetTileZoneOccupier(zone);
    }

    public static Realm GetEffectiveZoneController(Cell zone)
    {
        if (zone == null || zone.City == null)
        {
            return null;
        }

        return GetZoneOccupier(zone) ?? GetZoneKingdom(zone);
    }

    public static bool IsFriendlyControlledZone(Realm actorKingdom, Cell zone)
    {
        if (actorKingdom == null || !IsValidZone(zone))
        {
            return false;
        }

        Realm controller = GetEffectiveZoneController(zone);
        return controller != null && IsFriendlyKingdom(actorKingdom, controller);
    }

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

    public static bool IsValidZone(Cell zone)
    {
        if (zone == null)
        {
            return false;
        }

        if (zone.OnEdge)
        {
            return false;
        }

        return true;
    }

    public static float GetZoneDistanceSqr(Cell a, Cell b)
    {
        if (a == null || b == null)
        {
            return float.MaxValue;
        }

        int dx = a.X - b.X;
        int dy = a.Y - b.Y;

        return dx * dx + dy * dy;
    }

    /// <summary>
    /// 统计 zone 内同阵营士兵数量。
    /// 用于判断某个前线 zone 是否已经被友军填满。
    /// </summary>
#if EC_PENDING
    public static int CountFriendlyWarriorsInZone(Cell zone, Realm actorKingdom)
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

            if (!actor.IsWarrior())
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

            List<Cell> directZones = GetDirectEnemyFrontZones(actorKingdom, enemyKingdom);
            List<Cell> finalZones = GetEnemyFrontZones(actorKingdom, enemyKingdom);

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

            foreach (Cell zone in finalZones)
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
                    zone.X +
                    "," +
                    zone.Y +
                    ") city=" +
                    (zone.City == null ? "null" : zone.City.Name)
                );
            }
        }
    }

    public static void MarkDebugFrontZone(Cell zone)
    {
        if (zone == null)
        {
            return;
        }

        Cell tile = zone.Center;

        if (tile == null)
        {
            return;
        }

        if (tile.Zone == null)
        {
            return;
        }

        if (tile.Zone.OnEdge)
        {
            return;
        }

        HighlightZones(zone, Color.red);
    }

    public static void HighlightZones(Cell zone, Color pColor, float pAlpha = 0.8f)
    {
        if (zone == null || zone.Center == null)
        {
            return;
        }

        // 纯净地图下不画一闪一闪的区块高亮
        if (EmpireCraft.Core.PureMapMode.Enabled) return;
        ((ZoneFlash)EffectsLibrary.spawn("fx_zone_highlight", zone.Center, null, null, pAlpha)).start(pColor, pAlpha);
    }
#endif
}
}
