// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorCheckWarriorMoveAdvanced.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using Color = EmpireCraft.Core.EcColor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public class EmpireCraftActorCheckWarriorMoveAdvanced : GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    private const bool AggressiveMode = false;

    private const int MinWarriorsPerFrontZone = 1;
    private const int MaxWarriorsPerFrontZone = 3;

    // 保守模式：让占领区尽量连成一片
    private const int ConservativeConnectedBonus = 300;
    private const int ConservativeFriendlyControlledBonus = 120;
    private const int ConservativeIsolatedPenalty = 500;
    private const float RetreatHealthThreshold = 10f;
    
    private static int _lastGlobalMobilizeFrame = -1;
    private const int GlobalMobilizeIntervalFrames = 15;
    // 已经在前线的士兵尽量不要乱跑
    private const int HoldFrontMinEnemyNeighbour = 1;
    private static int _zoneWarriorCacheFrame = -1;
    private static readonly Dictionary<Cell, List<Person>> _zoneWarriors = new();
    private static readonly Dictionary<Cell, Dictionary<Realm, int>> _zoneWarriorCounts = new();
    private static int _frontZoneCacheFrame = -1;
    private static readonly Dictionary<Realm, List<Cell>> _frontZoneCache = new();
    private static int _occupiedOwnZoneCacheFrame = -1;
    private static readonly Dictionary<Realm, List<Cell>> _occupiedOwnZoneCache = new();
    private static int _armyMemberCountCacheFrame = -1;
    private static readonly Dictionary<Realm, int> _armyMemberCountCache = new();
    private static readonly List<Cell> _nearbyRetakeCandidates = new();
    private static readonly List<Cell> _availableFrontZoneCandidates = new();
    private static int _targetZoneCacheFrame = -1;
    private static readonly Dictionary<(Realm kingdom, Cell currentZone), Cell> _targetZoneCache = new();

    public static void ClearRuntimeState()
    {
        _lastGlobalMobilizeFrame = -1;
        _zoneWarriorCacheFrame = -1;
        _zoneWarriors.Clear();
        _zoneWarriorCounts.Clear();
        _frontZoneCacheFrame = -1;
        _frontZoneCache.Clear();
        _occupiedOwnZoneCacheFrame = -1;
        _occupiedOwnZoneCache.Clear();
        _armyMemberCountCacheFrame = -1;
        _armyMemberCountCache.Clear();
        _nearbyRetakeCandidates.Clear();
        _availableFrontZoneCandidates.Clear();
        _targetZoneCacheFrame = -1;
        _targetZoneCache.Clear();
    }

#if EC_PENDING
    public override BehResult execute(Person pActor)
    {
        return BehResult.Continue;
        if (!EmpireCraftWorldLawLibrary.empirecraft_law_switch_occupy_mode.isEnabled())
        {
            pActor?.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }
        if (pActor == null)
        {
            return BehResult.Continue;
        }

        if (pActor.Realm == null)
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        if (pActor.Cell == null || pActor.Cell.Zone == null)
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        Realm actorKingdom = pActor.Realm;

        if (actorKingdom.IsGone() || actorKingdom.IsNeutral())
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        if (!actorKingdom.HasEnemies())
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        if (!pActor.hasArmy())
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        if (TryEngageNearbyEnemy(pActor))
        {
            return BehResult.Continue;
        }

        if (pActor.HasFrontLineMoveTarget())
        {
            pActor.goTo(pActor.GetFrontLineMoveTarget());
            return BehResult.Continue;
        }
        // 2. 如果附近有己方城市被敌军占领的区块，优先清除占领状态
        // 3. 保守模式下，如果已经站在有效前线，就不要乱跑
        if (!AggressiveMode && ShouldHoldCurrentFrontZone(pActor))
        {
            pActor.SetFrontLineMoveTarget(pActor.Cell);
            return BehResult.Continue;
        }

        // 4. 重新计算目标：
        // 先处理本国被占领区；
        // 没有被占领区时，才推进敌方前线。
        Cell targetZone = FindTargetZoneForActor(pActor);

        if (targetZone == null)
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        Cell targetTile = GetZoneTile(targetZone);

        if (targetTile == null)
        {
            pActor.ClearFrontLineMoveTarget();
            return BehResult.Continue;
        }

        pActor.SetFrontLineMoveTarget(targetTile);
        if (pActor.is_army_captain)
        {
            pActor.army._prev_captain_position = targetTile;
        }
        pActor.goTo(targetTile);
        if (!EmpireCraft.Core.PureMapMode.Enabled)
            ((ZoneFlash)EffectsLibrary.spawn("fx_zone_highlight", targetTile, null, null, 0.8f)).start(Color.blue, 0.8f);
        return BehResult.Continue;
    }
    private static void EnsureZoneWarriorCache()
    {
        int frame = HighPopulationPerformance.GetFrontLineCacheKey();
        if (_zoneWarriorCacheFrame == frame)
        {
            return;
        }

        _zoneWarriorCacheFrame = frame;
        _zoneWarriors.Clear();
        _zoneWarriorCounts.Clear();
        _armyMemberCountCacheFrame = frame;
        _armyMemberCountCache.Clear();

        foreach (Person actor in EmpireWorld.Current.Living)
        {
            if (!IsOccupyArmyGroupMember(actor))
            {
                continue;
            }

            Cell zone = actor.Cell?.Zone;
            if (zone == null)
            {
                continue;
            }

            if (!_zoneWarriors.TryGetValue(zone, out var actorsInZone))
            {
                actorsInZone = new List<Person>();
                _zoneWarriors[zone] = actorsInZone;
            }
            actorsInZone.Add(actor);

            if (!_zoneWarriorCounts.TryGetValue(zone, out var kingdomCounts))
            {
                kingdomCounts = new Dictionary<Realm, int>();
                _zoneWarriorCounts[zone] = kingdomCounts;
            }

            kingdomCounts.TryGetValue(actor.Realm, out int count);
            kingdomCounts[actor.Realm] = count + 1;

            _armyMemberCountCache.TryGetValue(actor.Realm, out int armyCount);
            _armyMemberCountCache[actor.Realm] = armyCount + 1;
        }
    }

    private static Cell FindTargetZoneForActor(Person actor)
    {
        if (actor == null || actor.Realm == null || actor.Cell == null || actor.Cell.Zone == null)
        {
            return null;
        }

        // 1. 如果本国有土地被敌军占领，优先夺回
        int frame = Time.frameCount;
        if (_targetZoneCacheFrame != frame)
        {
            _targetZoneCacheFrame = frame;
            _targetZoneCache.Clear();
        }

        var cacheKey = (actor.Realm, actor.Cell.Zone);
        if (_targetZoneCache.TryGetValue(cacheKey, out Cell cachedTarget))
        {
            return cachedTarget;
        }

        // 2. 没有被占领土地时，才推进敌国前线
        Cell frontZone = FindTargetFrontZoneForActor(actor);
        _targetZoneCache[cacheKey] = frontZone;
        return frontZone;
    }
#endif

    private static List<Cell> GetCachedEnemyFrontZones(Realm kingdom)
    {
        int frame = Time.frameCount;
        if (_frontZoneCacheFrame != frame)
        {
            _frontZoneCacheFrame = frame;
            _frontZoneCache.Clear();
        }

        if (kingdom == null)
        {
            return null;
        }

        if (!_frontZoneCache.TryGetValue(kingdom, out var zones))
        {
            zones = KingdomFrontLineHelper.GetAllEnemyFrontZones(kingdom);
            _frontZoneCache[kingdom] = zones;
        }

        return zones;
    }

#if EC_PENDING
    private static List<Cell> GetCachedOccupiedOwnZones(Realm kingdom)
    {
        int frame = Time.frameCount;
        if (_occupiedOwnZoneCacheFrame != frame)
        {
            _occupiedOwnZoneCacheFrame = frame;
            _occupiedOwnZoneCache.Clear();
        }

        if (kingdom == null)
        {
            return null;
        }

        if (!_occupiedOwnZoneCache.TryGetValue(kingdom, out var zones))
        {
            zones = GetOccupiedOwnZones(kingdom);
            _occupiedOwnZoneCache[kingdom] = zones;
        }

        return zones;
    }

    private static Cell FindNearbyRetakeZoneForActor(Person actor)
    {
        if (actor == null || actor.Realm == null || actor.Cell == null || actor.Cell.Zone == null)
        {
            return null;
        }

        Realm actorKingdom = actor.Realm;
        Cell currentZone = actor.Cell.Zone;

        _nearbyRetakeCandidates.Clear();
        List<Cell> candidates = _nearbyRetakeCandidates;
        candidates.Add(currentZone);

        if (currentZone.NeighboursAll != null)
        {
            foreach (Cell neighbour in currentZone.NeighboursAll)
            {
                if (neighbour == null)
                {
                    continue;
                }

                candidates.Add(neighbour);
            }
        }

        Cell bestZone = null;
        int bestScore = int.MinValue;
        float bestDistance = float.MaxValue;
        int desiredWarriorsPerFrontZone = GetDesiredWarriorsPerFrontZone(actorKingdom, Math.Max(candidates.Count, 1));

        foreach (Cell zone in candidates)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            City city = zone.City;

            if (city == null || city.Realm == null)
            {
                continue;
            }

            // 只清理自己的城市
            if (city.Realm != actorKingdom)
            {
                continue;
            }

            Realm occupier = city.GetTileZoneOccupier(zone);

            if (occupier == null)
            {
                continue;
            }

            if (occupier == actorKingdom)
            {
                continue;
            }

            if (actorKingdom.IsOnSameSide(occupier))
            {
                continue;
            }

            if (!actorKingdom.IsAtWarWith(occupier))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actorKingdom);

            if (friendlyCount >= desiredWarriorsPerFrontZone)
            {
                continue;
            }

            int enemyCount = CountEnemyWarriorsInZone(zone, actorKingdom);
            float distance = KingdomFrontLineHelper.GetZoneDistanceSqr(currentZone, zone);

            int score = 0;

            // 当前脚下被占，最高优先级
            if (zone == currentZone)
            {
                score += 10000;
            }

            // 有敌军时，优先过去处理
            score += enemyCount * 300;

            // 没有友军正在处理的被占区优先
            if (friendlyCount == 0)
            {
                score += 200;
            }
            else
            {
                score += 50;
            }

            score -= (int)(distance * 0.1f);

            if (score > bestScore)
            {
                bestScore = score;
                bestDistance = distance;
                bestZone = zone;
            }
            else if (score == bestScore && distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        _nearbyRetakeCandidates.Clear();
        return bestZone;
    }

    private static Cell FindBestRetakeZoneForActor(Person actor)
    {
        if (actor == null || actor.Realm == null || actor.Cell == null || actor.Cell.Zone == null)
        {
            return null;
        }
        Realm actorKingdom = actor.Realm;

        List<Cell> occupiedOwnZones = GetCachedOccupiedOwnZones(actorKingdom);

        if (occupiedOwnZones == null || occupiedOwnZones.Count == 0)
        {
            return null;
        }

        Cell bestZone = null;
        int bestScore = int.MinValue;
        float bestDistance = float.MaxValue;
        int desiredWarriorsPerFrontZone = GetDesiredWarriorsPerFrontZone(actorKingdom, Math.Max(occupiedOwnZones.Count, 1));

        foreach (Cell zone in occupiedOwnZones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actorKingdom);

            // 已经有足够友军在清理，就不再增派
            if (friendlyCount >= desiredWarriorsPerFrontZone)
            {
                continue;
            }

            int enemyCount = CountEnemyWarriorsInZone(zone, actorKingdom);
            float distance = KingdomFrontLineHelper.GetZoneDistanceSqr(actor.Cell.Zone, zone);

            int score = 0;

            // 有敌军的被占领区优先处理
            score += enemyCount * 200;

            // 还没人去夺回的 zone 优先
            if (friendlyCount == 0)
            {
                score += 100;
            }
            else
            {
                score += 40;
            }

            // 距离越近越好
            score -= (int)(distance * 0.1f);

            if (score > bestScore)
            {
                bestScore = score;
                bestDistance = distance;
                bestZone = zone;
            }
            else if (score == bestScore && distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        return bestZone;
    }

    private static List<Cell> GetOccupiedOwnZones(Realm kingdom)
    {
        List<Cell> result = new List<Cell>();

        if (kingdom == null || kingdom.Cities == null)
        {
            return result;
        }

        foreach (City city in kingdom.Cities)
        {
            if (city == null || city.Zones == null)
            {
                continue;
            }

            foreach (Cell zone in city.Zones)
            {
                if (!KingdomFrontLineHelper.IsValidZone(zone))
                {
                    continue;
                }

                Realm occupier = city.GetTileZoneOccupier(zone);

                if (occupier == null)
                {
                    continue;
                }

                // 自己占自己的地，不算
                if (occupier == kingdom)
                {
                    continue;
                }

                // 友军占领，不需要夺回
                if (kingdom.IsOnSameSide(occupier))
                {
                    continue;
                }

                // 敌对国家占领，才需要夺回
                if (kingdom.IsAtWarWith(occupier))
                {
                    if (CanRetakeZoneFromFriendlyEdge(kingdom, zone))
                    {
                        result.Add(zone);
                    }
                }
            }
        }

        return result;
    }

    private static bool CanRetakeZoneFromFriendlyEdge(Realm kingdom, Cell zone)
    {
        if (kingdom == null || zone == null || zone.City == null)
        {
            return false;
        }

        if (zone.NeighboursAll == null || zone.NeighboursAll.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < zone.NeighboursAll.Length; i++)
        {
            Cell neighbour = zone.NeighboursAll[i];
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            City neighbourCity = neighbour.City;
            if (neighbourCity == null || neighbourCity.IsGone())
            {
                continue;
            }

            Realm neighbourOwner = neighbourCity.Realm;
            Realm neighbourOccupier = neighbourCity.GetTileZoneOccupier(neighbour);

            bool isFriendlyNativeEdge = neighbourOwner == kingdom && neighbourOccupier == null;
            bool isFriendlyRecoveredEdge = neighbourOccupier == kingdom;

            if (isFriendlyNativeEdge || isFriendlyRecoveredEdge)
            {
                return true;
            }
        }

        return false;
    }

    private static Cell FindTargetFrontZoneForActor(Person actor)
    {
        if (actor == null || actor.Realm == null || actor.Cell == null || actor.Cell.Zone == null)
        {
            return null;
        }

        List<Cell> allFrontZones = GetCachedEnemyFrontZones(actor.Realm);

        if (allFrontZones == null || allFrontZones.Count == 0)
        {
            return null;
        }

        _availableFrontZoneCandidates.Clear();
        List<Cell> availableZones = _availableFrontZoneCandidates;

        foreach (Cell zone in allFrontZones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            if (!TouchesOwnAdvanceEdge(actor.Realm, zone))
            {
                continue;
            }
            availableZones.Add(zone);
        }

        if (availableZones.Count == 0)
        {
            foreach (Cell zone in allFrontZones)
            {
                if (!KingdomFrontLineHelper.IsValidZone(zone))
                {
                    continue;
                }

                availableZones.Add(zone);
            }
        }

        if (availableZones.Count == 0)
        {
            _availableFrontZoneCandidates.Clear();
            return null;
        }

        Cell targetZone = AggressiveMode
            ? FindAggressiveTargetZone(actor, availableZones)
            : FindConservativeTargetZone(actor, availableZones);
        _availableFrontZoneCandidates.Clear();
        return targetZone;
    }

    private static Cell FindAggressiveTargetZone(Person actor, List<Cell> zones)
    {
        if (actor == null || actor.Realm == null || actor.Cell == null || actor.Cell.Zone == null)
        {
            return null;
        }

        Cell bestZone = null;
        int bestFriendlyCount = int.MaxValue;
        float bestDistance = float.MaxValue;

        foreach (Cell zone in zones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actor.Realm);
            float distance = KingdomFrontLineHelper.GetZoneDistanceSqr(actor.Cell.Zone, zone);

            // 激进模式：
            // 先填空 zone，避免所有士兵挤一个点；
            // 不强调连续战线，只看人数和距离。
            if (friendlyCount < bestFriendlyCount)
            {
                bestFriendlyCount = friendlyCount;
                bestDistance = distance;
                bestZone = zone;
            }
            else if (friendlyCount == bestFriendlyCount && distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        return bestZone;
    }

    private static Cell FindConservativeTargetZone(Person actor, List<Cell> zones)
    {
        if (actor == null || actor.Realm == null || actor.Cell == null || actor.Cell.Zone == null)
        {
            return null;
        }

        int desiredWarriorsPerFrontZone = GetDesiredWarriorsPerFrontZone(actor.Realm, zones.Count);

        Cell bestZone = null;
        int bestScore = int.MinValue;
        float bestDistance = float.MaxValue;

        foreach (Cell zone in zones)
        {
            if (!KingdomFrontLineHelper.IsValidZone(zone))
            {
                continue;
            }

            int friendlyCount = CountFriendlyWarriorsInZone(zone, actor.Realm);

            int friendlyControlledNeighbours = CountFriendlyControlledNeighbours(actor.Realm, zone);
            int friendlyOccupiedNeighbours = CountFriendlyOccupiedNeighbours(actor.Realm, zone);
            int enemyNeighbours = CountEnemyNeighbours(actor.Realm, zone);

            float distance = KingdomFrontLineHelper.GetZoneDistanceSqr(actor.Cell.Zone, zone);

            int score = 0;

            // 核心：保守模式下，优先选择贴着友军控制区/友军占领区的 zone
            score += friendlyControlledNeighbours * ConservativeFriendlyControlledBonus;
            score += friendlyOccupiedNeighbours * ConservativeConnectedBonus;

            // 如果这个 zone 周围没有任何友军控制/占领区，说明是孤立推进，强烈惩罚
            if (friendlyControlledNeighbours <= 0 && friendlyOccupiedNeighbours <= 0)
            {
                score -= ConservativeIsolatedPenalty;
            }

            // 无人前线可以补，但不能压过“连成一片”的权重
            if (friendlyCount == 0)
            {
                score += 80;
            }
            else
            {
                score += 30;
            }

            score -= Math.Max(0, friendlyCount - desiredWarriorsPerFrontZone) * 40;

            // 敌方邻居太多，说明太深入敌区，保守模式降低优先级
            score -= enemyNeighbours * 25;
            
            score -= (int)(distance * 0.01f);

            if (score > bestScore)
            {
                bestScore = score;
                bestDistance = distance;
                bestZone = zone;
            }
            else if (score == bestScore && distance < bestDistance)
            {
                bestDistance = distance;
                bestZone = zone;
            }
        }

        return bestZone;
    }
    public static Cell TryGetFrontLineMoveTileForActor(Person actor)
    {
        if (actor == null || actor.Realm == null || actor.Cell == null || actor.Cell.Zone == null)
        {
            return null;
        }

        if (!actor.hasArmy())
        {
            return null;
        }

        if (!actor.Realm.HasEnemies())
        {
            return null;
        }

        Cell targetZone = FindTargetZoneForActor(actor);

        if (targetZone == null)
        {
            return null;
        }

        return GetZoneTile(targetZone);
    }
    private static bool ShouldHoldCurrentFrontZone(Person actor)
    {
        if (actor == null || actor.Realm == null || actor.Cell == null || actor.Cell.Zone == null)
        {
            return false;
        }

        Cell currentZone = actor.Cell.Zone;

        if (!KingdomFrontLineHelper.IsValidZone(currentZone))
        {
            return false;
        }

        Realm actorKingdom = actor.Realm;
        Realm zoneKingdom = KingdomFrontLineHelper.GetZoneKingdom(currentZone);
        Realm occupier = KingdomFrontLineHelper.GetZoneOccupier(currentZone);

        bool isFriendlyControlled = zoneKingdom != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, zoneKingdom);

        if (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier))
        {
            isFriendlyControlled = true;
        }

        if (!isFriendlyControlled)
        {
            return false;
        }

        List<Cell> actualFrontZones = GetCachedEnemyFrontZones(actorKingdom);
        if (actualFrontZones == null || !actualFrontZones.Contains(currentZone))
        {
            return false;
        }

        int enemyNeighbours = CountEnemyNeighbours(actorKingdom, currentZone);

        if (enemyNeighbours < HoldFrontMinEnemyNeighbour)
        {
            return false;
        }

        int friendlyCount = CountFriendlyWarriorsInZone(currentZone, actorKingdom);
        int desiredWarriorsPerFrontZone = GetDesiredWarriorsPerFrontZone(actorKingdom, actualFrontZones.Count);

        // 当前 zone 是前线，而且人数没有明显超上限，就留在这里
        if (friendlyCount <= desiredWarriorsPerFrontZone)
        {
            return true;
        }

        return false;
    }

    private static bool TryEngageNearbyEnemy(Person actor)
    {
        if (actor == null || actor.Realm == null || actor.Cell?.Zone == null)
        {
            return false;
        }

        Person enemy = FindNearbyEnemyWarrior(actor);
        if (enemy == null)
        {
            return false;
        }

        if (enemy.Cell != null)
        {
            actor.SetFrontLineMoveTarget(enemy.Cell);
            actor.goTo(enemy.Cell);
        }
        actor.setAttackTarget(enemy);
        return true;
    }

    private static Person FindNearbyEnemyWarrior(Person actor)
    {
        Cell currentZone = actor?.Cell?.Zone;
        if (currentZone == null)
        {
            return null;
        }

        Person enemy = FindEnemyWarriorInZone(actor, currentZone);
        if (enemy != null)
        {
            return enemy;
        }

        if (currentZone.NeighboursAll == null)
        {
            return null;
        }

        foreach (Cell neighbour in currentZone.NeighboursAll)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            enemy = FindEnemyWarriorInZone(actor, neighbour);
            if (enemy != null)
            {
                return enemy;
            }
        }

        return null;
    }

    private static bool ShouldRetreat(Person actor)
    {
        return actor?.Record != null && actor.Record.health <= RetreatHealthThreshold;
    }

    private static Cell FindRetreatTileForActor(Person actor)
    {
        if (actor == null || actor.Realm == null)
        {
            return null;
        }

        Cell bestZone = FindBestFriendlyRetreatZone(actor);
        if (bestZone != null)
        {
            return GetZoneTile(bestZone);
        }

        return actor.Realm.Capital._city_tile;
    }

    private static Cell FindBestFriendlyRetreatZone(Person actor)
    {
        Cell currentZone = actor?.Cell?.Zone;
        Realm actorKingdom = actor?.Realm;
        if (currentZone == null || actorKingdom == null || currentZone.NeighboursAll == null)
        {
            return null;
        }

        Cell bestZone = null;
        int bestScore = int.MinValue;
        float bestDistance = float.MaxValue;
        Vector3 capitalCenter = actorKingdom.Capital?.Center ?? actor.Cell.posV3;

        foreach (Cell neighbour in currentZone.NeighboursAll)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Realm occupier = KingdomFrontLineHelper.GetZoneOccupier(neighbour);
            Realm owner = KingdomFrontLineHelper.GetZoneKingdom(neighbour);
            bool isFriendly = (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier)) ||
                              (occupier == null && owner != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, owner));
            if (!isFriendly)
            {
                continue;
            }

            int enemyNeighbours = CountEnemyNeighbours(actorKingdom, neighbour);
            int friendlyNeighbours = CountFriendlyControlledNeighbours(actorKingdom, neighbour) + CountFriendlyOccupiedNeighbours(actorKingdom, neighbour);
            float distanceToCapital = Toolbox.SquaredDist(neighbour.Center.posV3.x, neighbour.Center.posV3.y, capitalCenter.x, capitalCenter.y);
            int score = friendlyNeighbours * 100 - enemyNeighbours * 250;

            if (score > bestScore || (score == bestScore && distanceToCapital < bestDistance))
            {
                bestScore = score;
                bestDistance = distanceToCapital;
                bestZone = neighbour;
            }
        }

        return bestZone;
    }

    private static int GetDesiredWarriorsPerFrontZone(Realm actorKingdom, int frontZoneCount)
    {
        if (actorKingdom == null || frontZoneCount <= 0)
        {
            return MaxWarriorsPerFrontZone;
        }

        int totalWarriors = CountOccupyArmyGroupMembers(actorKingdom);
        if (totalWarriors <= 0)
        {
            return MaxWarriorsPerFrontZone;
        }

        int desired = (int)Math.Ceiling(totalWarriors / (double)frontZoneCount);
        return Math.Max(MaxWarriorsPerFrontZone, desired);
    }

    private static bool IsOccupyArmyGroupMember(Person actor)
    {
        return actor.isArmyGroupLeader()||actor.isArmyGroupWarrior();
    }

    private static int CountOccupyArmyGroupMembers(Realm kingdom)
    {
        if (kingdom == null)
        {
            return 0;
        }

        EnsureZoneWarriorCache();
        return _armyMemberCountCache.TryGetValue(kingdom, out int cachedCount) ? cachedCount : 0;
    }

    public static void HandleCurrentZoneWarState(Person actor)
    {
        if (actor == null || actor.Realm == null || actor.Cell == null || actor.Cell.Zone == null)
        {
            return;
        }

        Cell zone = actor.Cell.Zone;
        City city = zone.City;

        if (city == null || city.Realm == null)
        {
            return;
        }

        Realm actorKingdom = actor.Realm;
        Realm cityKingdom = city.Realm;

        // 1. 如果当前 zone 有敌军，先攻击
        Person enemy = FindEnemyWarriorInZone(actor, zone);

        if (enemy != null)
        {
            AttackEnemyInZone(actor, enemy);
            return;
        }

        // 虚拟部队(军团)模式：地块只随战线推进易手(见 FrontSystem)，统领等实体走进去不当场占领、夺回——
        // 以前统领先走到前线目标地块就把它占了，占领跑到推进线前面，和战线对不上
        if (EmpireCraft.Core.CorpsSystem.Enabled) return;

        Realm occupier = city.GetTileZoneOccupier(zone);

        // 2. 如果这是自己的城市，并且 zone 被敌军占领，但现在没有敌军，则夺回
        if (cityKingdom == actorKingdom)
        {
            if (occupier != null && occupier != actorKingdom)
            {
                if (!actorKingdom.IsOnSameSide(occupier) && actorKingdom.IsAtWarWith(occupier))
                {
                    city.RemoveOccupiedTileZone(occupier, zone);
                }
            }

            return;
        }

        // 3. 如果这是敌方城市，并且没有敌军，则尝试占领
        if (actorKingdom.IsAtWarWith(cityKingdom))
        {
            // 已经是友军占领，不重复覆盖
            if (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier))
            {
                return;
            }

            city.AddOccupiedTileZone(actorKingdom, zone, actor);
        }
    }

    public static Person FindEnemyWarriorInZone(Person actor, Cell zone)
    {
        if (actor == null || actor.Realm == null || zone == null)
        {
            return null;
        }

        EnsureZoneWarriorCache();
        if (!_zoneWarriors.TryGetValue(zone, out var actorsInZone))
        {
            return null;
        }

        foreach (Person other in actorsInZone)
        {
            if (other == null)
            {
                continue;
            }

            if (other == actor)
            {
                continue;
            }

            if (other.Realm == null)
            {
                continue;
            }

            if (!IsOccupyArmyGroupMember(other))
            {
                continue;
            }
            if (actor.Realm.IsAtWarWith(other.Realm))
            {
                return other;
            }
        }

        return null;
    }

    private static void AttackEnemyInZone(Person actor, Person enemy)
    {
        if (actor == null || enemy == null)
        {
            return;
        }

        // 如果你的项目里攻击方法名不同，只需要改这里。
        actor.setAttackTarget(enemy);
    }

    private static int CountFriendlyWarriorsInZone(Cell zone, Realm actorKingdom)
    {
        if (zone == null || actorKingdom == null)
        {
            return 0;
        }

        EnsureZoneWarriorCache();
        if (!_zoneWarriorCounts.TryGetValue(zone, out var kingdomCounts))
        {
            return 0;
        }

        int count = 0;
        foreach (var pair in kingdomCounts)
        {
            if (pair.Key != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, pair.Key))
            {
                count += pair.Value;
            }
        }

        return count;
    }

    private static int CountEnemyWarriorsInZone(Cell zone, Realm actorKingdom)
    {
        if (zone == null || actorKingdom == null)
        {
            return 0;
        }

        EnsureZoneWarriorCache();
        if (!_zoneWarriorCounts.TryGetValue(zone, out var kingdomCounts))
        {
            return 0;
        }

        int count = 0;
        foreach (var pair in kingdomCounts)
        {
            if (pair.Key != null && actorKingdom.IsAtWarWith(pair.Key))
            {
                count += pair.Value;
            }
        }

        return count;
    }
#endif

    private static int CountFriendlyControlledNeighbours(Realm actorKingdom, Cell zone)
    {
        if (actorKingdom == null || zone == null || zone.NeighboursAll == null)
        {
            return 0;
        }

        int count = 0;

        foreach (Cell neighbour in zone.NeighboursAll)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Realm zoneKingdom = KingdomFrontLineHelper.GetZoneKingdom(neighbour);
            Realm occupier = KingdomFrontLineHelper.GetZoneOccupier(neighbour);

            if (zoneKingdom != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, zoneKingdom))
            {
                count++;
                continue;
            }

            if (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountFriendlyOccupiedNeighbours(Realm actorKingdom, Cell zone)
    {
        if (actorKingdom == null || zone == null || zone.NeighboursAll == null)
        {
            return 0;
        }

        int count = 0;

        foreach (Cell neighbour in zone.NeighboursAll)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Realm occupier = KingdomFrontLineHelper.GetZoneOccupier(neighbour);

            if (occupier != null && KingdomFrontLineHelper.IsFriendlyKingdom(actorKingdom, occupier))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountEnemyNeighbours(Realm actorKingdom, Cell zone)
    {
        if (actorKingdom == null || zone == null || zone.NeighboursAll == null)
        {
            return 0;
        }

        int count = 0;

        foreach (Cell neighbour in zone.NeighboursAll)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Realm zoneKingdom = KingdomFrontLineHelper.GetZoneKingdom(neighbour);

            if (zoneKingdom != null && actorKingdom.IsAtWarWith(zoneKingdom))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TouchesOwnAdvanceEdge(Realm actorKingdom, Cell zone)
    {
        if (actorKingdom == null || zone == null || zone.NeighboursAll == null)
        {
            return false;
        }

        foreach (Cell neighbour in zone.NeighboursAll)
        {
            if (!KingdomFrontLineHelper.IsValidZone(neighbour))
            {
                continue;
            }

            Realm occupier = KingdomFrontLineHelper.GetZoneOccupier(neighbour);
            Realm owner = KingdomFrontLineHelper.GetZoneKingdom(neighbour);

            if (occupier == actorKingdom)
            {
                return true;
            }

            if (occupier == null && owner == actorKingdom)
            {
                return true;
            }
        }

        return false;
    }

    private static Cell GetZoneTile(Cell zone)
    {
        if (!KingdomFrontLineHelper.IsValidZone(zone))
        {
            return null;
        }

        Cell tile = zone.Center;

        if (tile == null)
        {
            return null;
        }

        if (tile.Zone == null)
        {
            return null;
        }

        if (tile.Zone.OnEdge)
        {
            return null;
        }

        return tile;
    }
}
}
