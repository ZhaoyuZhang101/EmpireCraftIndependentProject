// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorCheckWarriorMove.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using System;
using System.Collections.Generic;
using System.Linq;
#if EC_PENDING
using life.taxi;
#endif

namespace EmpireCraft.Core
{

public class EmpireCraftActorCheckWarriorMove : GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    private const int MinWarriorsPerFrontZone = 1;
    private const int MaxWarriorsPerFrontZone = 3;

    private const float WarriorCrowdingPenalty = 240f;
    private const float UnderDefendedZoneBonus = 360f;

    // ------------------------------------------------------------
    // 分帧性能参数
    // ------------------------------------------------------------

    // 每帧最多处理多少名“开战立即出征”的 Warrior。
    // 48 名在视觉上仍然近似同时动员，但不会把几百/几千人塞进同一帧。
    private const int MobilizeActorsPerFrame = 48;

    // 每帧最多实际向 TaxiManager 提交多少个跨岛请求。
    private const int TaxiRequestsPerFrame = 12;

    // 前线 Zone 缓存 120 帧，避免每个 Person 都重新调用 GetAllEnemyFrontZones。
    private const int FrontZoneCacheLifetimeFrames = 120;

    private const int ProgressCheckIntervalFrames = 45;
    private const int SoftStuckFrames = 180;
    private const int HardRecoveryAfter = 2;
    private const int TaxiRetryIntervalFrames = 180;
    private const int MovementWatchdogActorsPerFrame = 24;
    private const int MovementWatchdogSnapshotFrames = 60;

    // ------------------------------------------------------------
    // 开战动员队列
    // ------------------------------------------------------------

    private sealed class MobilizeEntry
    {
        public long actorId;
        public Person actor;
        public Realm kingdom;
    }

    private static readonly Queue<MobilizeEntry> _mobilizeQueue = new();
    private static readonly HashSet<long> _queuedMobilizeActorIds = new();

    // ------------------------------------------------------------
    // Taxi 队列
    // ------------------------------------------------------------

    private sealed class TaxiEntry
    {
        public long actorId;
        public Person actor;
        public Cell target;
    }

    private static readonly Queue<TaxiEntry> _taxiQueue = new();
    private static readonly HashSet<long> _queuedTaxiActorIds = new();

    // 确保即使 War.update 对多场战争各调用一次，
    // 本帧也只真正处理一次队列。
    private static int _lastQueueProcessFrame = -1;

    // ------------------------------------------------------------
    // Front Zone 缓存
    // ------------------------------------------------------------

    private sealed class FrontZoneCacheEntry
    {
        public int frame;
        public List<Cell> zones;
    }

    private static readonly Dictionary<Realm, FrontZoneCacheEntry> _frontZoneCache = new();
    private static readonly List<Realm> _staleKingdomCacheKeys = new();
    private const int RuntimeCachePruneFrames = 1800;
    private static int _nextRuntimeCachePruneFrame = -1;

    // 同一次开战分配中记录已计划去各 Zone 的人数，
    // 防止同一批士兵全部扎到第一个目标。
    private static readonly Dictionary<Realm, Dictionary<Cell, int>> _plannedFrontCounts = new();

    // ------------------------------------------------------------
    // Zone Warrior 缓存
    // ------------------------------------------------------------

    private static int _zoneWarriorCacheFrame = -1;
    private static readonly Dictionary<Cell, List<Person>> _zoneWarriors = new();
    private static readonly Dictionary<Cell, Dictionary<Realm, int>> _zoneWarriorCounts = new();

    // ------------------------------------------------------------
    // Person 移动状态
    // ------------------------------------------------------------

    private static readonly Dictionary<long, ActorMoveState> _moveStates = new();
    private static List<KeyValuePair<long, ActorMoveState>> _movementWatchdogSnapshot = new();
    private static int _movementWatchdogIndex;
    private static int _movementWatchdogSnapshotFrame = -1;

    private sealed class ActorMoveState
    {
        public Person actor;
        public Cell targetTile;
        public Cell lastObservedTile;
        public int lastProgressFrame;
        public int lastCheckFrame;
        public int lastTaxiFrame = -TaxiRetryIntervalFrames;
        public int recoveryCount;
    }

    public static void ClearRuntimeState()
    {
        _mobilizeQueue.Clear();
        _queuedMobilizeActorIds.Clear();
        _taxiQueue.Clear();
        _queuedTaxiActorIds.Clear();
        _lastQueueProcessFrame = -1;
        _frontZoneCache.Clear();
        _staleKingdomCacheKeys.Clear();
        _nextRuntimeCachePruneFrame = -1;
        _plannedFrontCounts.Clear();
        _zoneWarriorCacheFrame = -1;
        _zoneWarriors.Clear();
        _zoneWarriorCounts.Clear();
        _moveStates.Clear();
        _movementWatchdogSnapshot = new List<KeyValuePair<long, ActorMoveState>>();
        _movementWatchdogIndex = 0;
        _movementWatchdogSnapshotFrame = -1;
    }

#if EC_PENDING
    public override BehResult Execute(Person pActor)
    {
        if (pActor == null)
        {
            return BehResult.Continue;
        }

        // 本类 V3 只负责战争移动。
        // Warrior 生育已经彻底移到 EmpireCraftActorCheckWarrior
        // 并由 ArmyManager.update 独立驱动，不再依赖这个军事 Behaviour
        // 在和平时期是否会被 AI 调度。
        if (!WorldLaws.SwitchOccupyMode.IsEnabled() ||
            EmpireCraft.Core.CorpsSystem.Enabled)
        {
            ClearActorMoveState(pActor);
            return BehResult.Continue;
        }

        if (pActor.Realm == null ||
            pActor.Cell == null ||
            pActor.Cell.Zone == null)
        {
            ClearActorMoveState(pActor);
            return BehResult.Continue;
        }

        Realm kingdom = pActor.Realm;

        if (kingdom.IsGone() ||
            kingdom.IsNeutral() ||
            !pActor.IsWarrior())
        {
            ClearActorMoveState(pActor);
            return BehResult.Continue;
        }

        // 和平时期只停止战争移动，不停止 Warrior 生育。
        if (!kingdom.HasEnemies())
        {
            ClearActorMoveState(pActor);
            return BehResult.Continue;
        }

        ActorMoveState state = GetState(pActor);

        // 已经在真实战斗中，绝不抢任务。
        if (pActor.has_attack_target || pActor.isFighting())
        {
            MarkProgress(pActor, state);
            return BehResult.Continue;
        }

        Person localEnemy = FindEnemyWarriorInZone(
            pActor,
            pActor.Cell.Zone);

        if (localEnemy != null)
        {
            pActor.ClearFrontLineMoveTarget();
            state.targetTile = null;
            pActor.setAttackTarget(localEnemy);
            return BehResult.Continue;
        }

        if (!IsValidFrontTarget(pActor, state.targetTile))
        {
            state.targetTile = null;
            pActor.ClearFrontLineMoveTarget();
        }

        if (state.targetTile == null)
        {
            Cell target = FindTargetZone(pActor);

            if (target == null)
            {
                pActor.ClearFrontLineMoveTarget();
                return BehResult.Continue;
            }

            SetStateTarget(pActor, state, target);

            // 平时只补目标，不无条件 cancelAllBeh。
            IssueFrontLineMove(pActor, target, false);
        }

        Cell targetTile = state.targetTile;

        if (targetTile == null)
            return BehResult.Continue;

        if (pActor.Cell.Zone == targetTile.Zone)
        {
            pActor.ClearFrontLineMoveTarget();
            state.targetTile = null;
            state.recoveryCount = 0;
            return BehResult.Continue;
        }

        if (!pActor.Cell.IsSameIsland(targetTile))
        {
            HandleCrossIsland(pActor, targetTile, state);
            return BehResult.Continue;
        }

        MaintainMovement(pActor, targetTile, state);
        return BehResult.Continue;
    }
#endif

    // ============================================================
    // 战争开始：只排队，不同步处理全部士兵
    // ============================================================

#if EC_PENDING
    public static void QueueKingdomMobilizationOnWarStart(Realm kingdom)
    {
        if (kingdom == null ||
            kingdom.IsGone() ||
            kingdom.IsNeutral())
        {
            return;
        }

        if (!WorldLaws.SwitchOccupyMode.IsEnabled())
            return;

        // 开战时刷新一次该国前线缓存，之后所有士兵共享。
        InvalidateFrontZoneCache(kingdom);
        GetCachedFrontZones(kingdom);

        if (!_plannedFrontCounts.ContainsKey(kingdom))
        {
            _plannedFrontCounts[kingdom] = new Dictionary<Cell, int>();
        }
        else
        {
            _plannedFrontCounts[kingdom].Clear();
        }

        foreach (Person actor in kingdom.getUnits().ToList())
        {
            QueueActorForWarMobilization(actor, kingdom);
        }
    }
#endif

    // 新征召成功的 Warrior 可直接加入同一个分帧队列。
    public static void QueueActorForWarMobilization(Person actor, Realm kingdom)
    {
        if (actor == null ||
            actor.Record == null ||
            kingdom == null ||
            !actor.Alive ||
            !actor.IsWarrior() ||
            actor.Realm != kingdom)
        {
            return;
        }

        long id = actor.Key;

        if (!_queuedMobilizeActorIds.Add(id))
            return;

        _mobilizeQueue.Enqueue(new MobilizeEntry
        {
            actorId = id,
            actor = actor,
            kingdom = kingdom
        });
    }

    // 由 WarPatch.update 每帧调用；内部有 frame guard。
#if EC_PENDING
    public static void ProcessQueuedMovementJobs()
    {
        int frame = Time.frameCount;

        if (_lastQueueProcessFrame == frame)
            return;

        _lastQueueProcessFrame = frame;
        PruneRuntimeCachesIfNeeded(frame);

        ProcessMobilizeQueue();
        ProcessActiveMovementWatchdog();
        ProcessTaxiQueue();
    }
#endif

    private static void PruneRuntimeCachesIfNeeded(int frame)
    {
        if (_nextRuntimeCachePruneFrame >= 0 && frame < _nextRuntimeCachePruneFrame)
        {
            return;
        }

        _nextRuntimeCachePruneFrame = frame + RuntimeCachePruneFrames;
        _staleKingdomCacheKeys.Clear();
        foreach (Realm kingdom in _frontZoneCache.Keys)
        {
            if (IsStaleKingdomCacheKey(kingdom))
            {
                _staleKingdomCacheKeys.Add(kingdom);
            }
        }

        for (int i = 0; i < _staleKingdomCacheKeys.Count; i++)
        {
            _frontZoneCache.Remove(_staleKingdomCacheKeys[i]);
        }

        _staleKingdomCacheKeys.Clear();
        foreach (Realm kingdom in _plannedFrontCounts.Keys)
        {
            if (IsStaleKingdomCacheKey(kingdom))
            {
                _staleKingdomCacheKeys.Add(kingdom);
            }
        }

        for (int i = 0; i < _staleKingdomCacheKeys.Count; i++)
        {
            _plannedFrontCounts.Remove(_staleKingdomCacheKeys[i]);
        }
        _staleKingdomCacheKeys.Clear();
    }

    private static bool IsStaleKingdomCacheKey(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone())
        {
            return true;
        }

        try
        {
            return !kingdom.HasEnemies();
        }
        catch
        {
            return true;
        }
    }

#if EC_PENDING
    private static void ProcessActiveMovementWatchdog()
    {
        int frame = Time.frameCount;
        if (_movementWatchdogSnapshotFrame < 0 ||
            frame - _movementWatchdogSnapshotFrame >= MovementWatchdogSnapshotFrames)
        {
            // Snapshot both the ActorMoveState and its already-known Person ID.
            // Never rediscover the ID from an Person that may have been destroyed
            // between frames.
            _movementWatchdogSnapshot = _moveStates.ToList();
            _movementWatchdogIndex = 0;
            _movementWatchdogSnapshotFrame = frame;
        }

        int budget = MovementWatchdogActorsPerFrame;
        while (budget-- > 0 && _movementWatchdogIndex < _movementWatchdogSnapshot.Count)
        {
            KeyValuePair<long, ActorMoveState> snapshotEntry =
                _movementWatchdogSnapshot[_movementWatchdogIndex++];

            long actorId = snapshotEntry.Key;

            // The state may have been removed/replaced after this snapshot was built.
            // In that case the snapshot entry is stale and must not touch the new state.
            if (!_moveStates.TryGetValue(actorId, out ActorMoveState state) ||
                state == null ||
                !ReferenceEquals(state, snapshotEntry.Value))
            {
                continue;
            }

            Person actor = state.actor;

            // WorldBox can leave a non-null Person wrapper whose BaseSimObject data
            // has already been released. Calling getID() on that object throws from
            // BaseSimObject.Key, so remove it by the dictionary key instead.
            if (actor == null || actor.Record == null)
            {
                _moveStates.Remove(actorId);
                continue;
            }

            if (!actor.Alive || !actor.IsWarrior() ||
                actor.Realm == null || !actor.Realm.HasEnemies() ||
                actor.Cell?.Zone == null)
            {
                ClearActorMoveState(actorId, actor);
                continue;
            }

            Cell target = state.targetTile;
            if (!IsValidFrontTarget(actor, target))
            {
                ClearActorMoveState(actorId, actor);
                continue;
            }

            if (actor.has_attack_target || actor.isFighting())
            {
                MarkProgress(actor, state);
                continue;
            }

            if (actor.Cell.Zone == target.Zone)
            {
                ClearActorMoveState(actorId, actor);
                continue;
            }

            if (!actor.Cell.IsSameIsland(target))
            {
                HandleCrossIsland(actor, target, state);
                continue;
            }

            MaintainMovement(actor, target, state);
        }
    }

    private static void ProcessMobilizeQueue()
    {
        int budget = MobilizeActorsPerFrame;

        while (budget > 0 && _mobilizeQueue.Count > 0)
        {
            MobilizeEntry entry = _mobilizeQueue.Dequeue();
            budget--;

            Person actor = entry.actor;
            Realm kingdom = entry.kingdom;

            _queuedMobilizeActorIds.Remove(entry.actorId);

            if (actor == null ||
                actor.Record == null ||
                kingdom == null ||
                !actor.Alive ||
                actor.Realm != kingdom ||
                !actor.IsWarrior() ||
                !kingdom.HasEnemies())
            {
                continue;
            }

            if (actor.has_attack_target || actor.isFighting())
                continue;

            Dictionary<Cell, int> planned =
                GetPlannedFrontCountMap(kingdom);

            Cell target = FindTargetZone(actor, planned);

            if (target == null)
                continue;

            if (target.Zone != null)
            {
                planned.TryGetValue(target.Zone, out int plannedCount);
                planned[target.Zone] = plannedCount + 1;
            }

            ActorMoveState state = GetState(actor);
            SetStateTarget(actor, state, target);

            // 只在这一名士兵真正轮到开战动员时 cancel 一次。
            IssueFrontLineMove(actor, target, true);

            if (actor.Cell != null &&
                !actor.Cell.IsSameIsland(target))
            {
                QueueTaxi(actor, target);
            }
        }
    }
#endif

    private static Dictionary<Cell, int> GetPlannedFrontCountMap(Realm kingdom)
    {
        if (!_plannedFrontCounts.TryGetValue(
                kingdom,
                out Dictionary<Cell, int> map))
        {
            map = new Dictionary<Cell, int>();
            _plannedFrontCounts[kingdom] = map;
        }

        return map;
    }

    // ============================================================
    // Taxi 分帧
    // ============================================================

    private static void QueueTaxi(Person actor, Cell target)
    {
        if (actor == null ||
            actor.Record == null ||
            target == null ||
            !actor.Alive)
        {
            return;
        }

        long id = actor.Key;

        if (!_queuedTaxiActorIds.Add(id))
            return;

        _taxiQueue.Enqueue(new TaxiEntry
        {
            actorId = id,
            actor = actor,
            target = target
        });
    }

#if EC_PENDING
    private static void ProcessTaxiQueue()
    {
        int budget = TaxiRequestsPerFrame;

        while (budget > 0 && _taxiQueue.Count > 0)
        {
            TaxiEntry entry = _taxiQueue.Dequeue();
            budget--;

            Person actor = entry.actor;
            Cell target = entry.target;

            _queuedTaxiActorIds.Remove(entry.actorId);

            if (actor == null ||
                actor.Record == null ||
                target == null ||
                !actor.Alive ||
                actor.Realm == null ||
                !actor.Realm.HasEnemies() ||
                actor.Cell == null)
            {
                continue;
            }

            if (actor.Cell.IsSameIsland(target))
                continue;

            try
            {
                TaxiManager.newRequest(actor, target);
            }
            catch
            {
                // 下一次 Person AI 卡死恢复时仍可再次请求。
            }
        }
    }
#endif

    // ============================================================
    // 常规移动 / 卡死恢复
    // ============================================================

#if EC_PENDING
    private static void IssueFrontLineMove(
        Person actor,
        Cell target,
        bool forceWake)
    {
        if (actor == null || target == null)
            return;

        if (forceWake)
        {
            actor.cancelAllBeh();
        }

        actor.ClearFrontLineMoveTarget();
        actor.SetFrontLineMoveTarget(target);

        // Setting the bookkeeping fields alone is not enough when another behaviour
        // currently owns movement. Explicitly wake the pathfinder for this target.
        actor.GoTo(target);

        if (actor.IsArmyCaptain && actor.Army != null)
        {
            actor.Army._prev_captain_position = target;
        }
    }

    private static void MaintainMovement(
        Person actor,
        Cell target,
        ActorMoveState state)
    {
        int frame = Time.frameCount;

        // Other behaviours can replace the engine's live target without clearing our
        // persistent front-line target. Restore it immediately instead of waiting for
        // the stuck timer, which keeps armies advancing between AI ticks.
        if (actor.tile_target != target || actor.beh_tile_target != target)
        {
            actor.SetFrontLineMoveTarget(target);
            actor.GoTo(target);
        }

        if (!actor.HasFrontLineMoveTarget())
        {
            actor.SetFrontLineMoveTarget(target);
            actor.GoTo(target);
        }

        if (frame - state.lastCheckFrame < ProgressCheckIntervalFrames)
            return;

        state.lastCheckFrame = frame;

        if (actor.Cell != state.lastObservedTile)
        {
            state.lastObservedTile = actor.Cell;
            state.lastProgressFrame = frame;
            state.recoveryCount = 0;
            return;
        }

        if (frame - state.lastProgressFrame < SoftStuckFrames)
            return;

        state.recoveryCount++;

        // 真正卡住才 reset 一次。
        IssueFrontLineMove(actor, target, true);
        state.lastObservedTile = actor.Cell;
        state.lastProgressFrame = frame;

        if (state.recoveryCount >= HardRecoveryAfter &&
            actor.Army != null &&
            !actor.IsArmyCaptain)
        {
            try
            {
                actor.setTask(
                    "warrior_army_follow_leader",
                    true,
                    false,
                    true);
            }
            catch
            {
            }

            state.recoveryCount = 0;
        }
    }
#endif

    private static void HandleCrossIsland(
        Person actor,
        Cell target,
        ActorMoveState state)
    {
        int frame = Time.frameCount;

        if (frame - state.lastTaxiFrame < TaxiRetryIntervalFrames)
            return;

        state.lastTaxiFrame = frame;
        QueueTaxi(actor, target);
    }

    // ============================================================
    // 前线 Zone 缓存
    // ============================================================

    private static List<Cell> GetCachedFrontZones(Realm kingdom)
    {
        if (kingdom == null)
            return new List<Cell>();

        int frame = Time.frameCount;

        if (_frontZoneCache.TryGetValue(
                kingdom,
                out FrontZoneCacheEntry cached) &&
            cached != null &&
            cached.zones != null &&
            frame - cached.frame <= FrontZoneCacheLifetimeFrames)
        {
            return cached.zones;
        }

        List<Cell> zones;

        try
        {
            zones = KingdomFrontLineHelper
                .GetAllEnemyFrontZones(kingdom) ?? new List<Cell>();
        }
        catch
        {
            zones = new List<Cell>();
        }

        // 在缓存写入时就先去掉空对象，减少每名士兵后续循环成本。
        zones = zones
            .Where(z => z != null && z.Center != null)
            .Distinct()
            .ToList();

        _frontZoneCache[kingdom] = new FrontZoneCacheEntry
        {
            frame = frame,
            zones = zones
        };

        return zones;
    }

    private static void InvalidateFrontZoneCache(Realm kingdom)
    {
        if (kingdom != null)
            _frontZoneCache.Remove(kingdom);
    }

    public static Cell FindTargetZone(Person pActor)
    {
        return FindTargetZone(pActor, null);
    }

    private static Cell FindTargetZone(
        Person pActor,
        Dictionary<Cell, int> plannedCounts)
    {
        if (pActor == null ||
            pActor.Realm == null ||
            pActor.Cell == null)
        {
            return null;
        }

        Realm kingdom = pActor.Realm;
        List<Cell> zones = GetCachedFrontZones(kingdom);

        if (zones.Count == 0)
            return null;

        EnsureZoneWarriorCache();

        Cell bestZone = null;
        float bestScore = float.MaxValue;

        // 第一轮：优先尚未达到每前线 3 人的区域。
        foreach (Cell zone in zones)
        {
            if (!IsValidEnemyFrontZone(kingdom, zone))
                continue;

            int current = GetWarriorCount(zone, kingdom);
            int planned = 0;

            if (plannedCounts != null)
                plannedCounts.TryGetValue(zone, out planned);

            int effective = current + planned;

            if (effective >= MaxWarriorsPerFrontZone)
                continue;

            float score = CalculateZoneScore(
                pActor,
                zone,
                effective);

            if (score < bestScore)
            {
                bestScore = score;
                bestZone = zone;
            }
        }

        // 所有前线都达到 3 人时，继续推进，但仍按距离 + 拥挤度选择。
        if (bestZone == null)
        {
            foreach (Cell zone in zones)
            {
                if (!IsValidEnemyFrontZone(kingdom, zone))
                    continue;

                int current = GetWarriorCount(zone, kingdom);
                int planned = 0;

                if (plannedCounts != null)
                    plannedCounts.TryGetValue(zone, out planned);

                int effective = current + planned;

                float score = CalculateZoneScore(
                    pActor,
                    zone,
                    effective);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestZone = zone;
                }
            }
        }

        return bestZone?.Center;
    }

    private static float CalculateZoneScore(
        Person actor,
        Cell zone,
        int friendlyCount)
    {
        float score = Land.SquaredDist(
            actor.Cell,
            zone.Center);

        score += friendlyCount * WarriorCrowdingPenalty;

        if (friendlyCount < MinWarriorsPerFrontZone)
        {
            score -= UnderDefendedZoneBonus;
        }

        return score;
    }

    private static bool IsValidEnemyFrontZone(
        Realm kingdom,
        Cell zone)
    {
        if (kingdom == null ||
            zone == null ||
            zone.City == null ||
            zone.City.Realm == null)
        {
            return false;
        }

        return AreWarSidesHostile(
            kingdom,
            zone.City.Realm);
    }

    private static bool IsValidFrontTarget(
        Person actor,
        Cell target)
    {
        return actor != null &&
               target != null &&
               target.Zone != null &&
               IsValidEnemyFrontZone(
                   actor.Realm,
                   target.Zone);
    }

    // 帝国战争可能登记在 CoreKingdom 上。
    private static bool AreWarSidesHostile(Realm a, Realm b)
    {
        if (a == null || b == null || a == b)
            return false;

        if (AreDirectlyAtWar(a, b))
            return true;

        Empire aEmpire = a.GetEmpire();
        Empire bEmpire = b.GetEmpire();

        Realm aCore = aEmpire?.CoreKingdom;
        Realm bCore = bEmpire?.CoreKingdom;

        if (aCore == null && a.IsEmpire())
            aCore = a;

        if (bCore == null && b.IsEmpire())
            bCore = b;

        if (aCore != null && AreDirectlyAtWar(aCore, b))
            return true;

        if (bCore != null && AreDirectlyAtWar(a, bCore))
            return true;

        return aCore != null &&
               bCore != null &&
               AreDirectlyAtWar(aCore, bCore);
    }

    private static bool AreDirectlyAtWar(Realm a, Realm b)
    {
        if (a == null || b == null || a == b)
            return false;

        try
        {
            return a.IsAtWarWith(b) || b.IsAtWarWith(a);
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // 当前 Zone Warrior 缓存
    // ============================================================

    private static void EnsureZoneWarriorCache()
    {
        int frame = HighPopulationPerformance.GetFrontLineCacheKey();

        if (_zoneWarriorCacheFrame == frame)
            return;

        _zoneWarriorCacheFrame = frame;
        _zoneWarriors.Clear();
        _zoneWarriorCounts.Clear();

        foreach (Person actor in EmpireWorld.Current.Living)
        {
            if (!IsOccupyArmyGroupMember(actor))
                continue;

            Cell zone = actor.Cell?.Zone;

            if (zone == null)
                continue;

            if (!_zoneWarriors.TryGetValue(
                    zone,
                    out List<Person> actorsInZone))
            {
                actorsInZone = new List<Person>();
                _zoneWarriors[zone] = actorsInZone;
            }

            actorsInZone.Add(actor);

            if (!_zoneWarriorCounts.TryGetValue(
                    zone,
                    out Dictionary<Realm, int> kingdomCounts))
            {
                kingdomCounts = new Dictionary<Realm, int>();
                _zoneWarriorCounts[zone] = kingdomCounts;
            }

            if (actor.Realm != null)
            {
                kingdomCounts.TryGetValue(
                    actor.Realm,
                    out int count);

                kingdomCounts[actor.Realm] = count + 1;
            }
        }
    }

    private static int GetWarriorCount(
        Cell zone,
        Realm kingdom)
    {
        if (zone == null || kingdom == null)
            return 0;

        if (!_zoneWarriorCounts.TryGetValue(
                zone,
                out Dictionary<Realm, int> kingdomCounts))
        {
            return 0;
        }

        return kingdomCounts.TryGetValue(
            kingdom,
            out int count)
            ? count
            : 0;
    }

    public static Person FindEnemyWarriorInZone(
        Person actor,
        Cell zone)
    {
        if (actor == null ||
            actor.Realm == null ||
            zone == null)
        {
            return null;
        }

        EnsureZoneWarriorCache();

        if (!_zoneWarriors.TryGetValue(
                zone,
                out List<Person> actorsInZone))
        {
            return null;
        }

        Person nearest = null;
        float bestDistance = float.MaxValue;

        foreach (Person other in actorsInZone)
        {
            if (other == null ||
                other == actor ||
                !other.Alive ||
                other.Realm == null ||
                !IsOccupyArmyGroupMember(other))
            {
                continue;
            }

            if (!AreWarSidesHostile(
                    actor.Realm,
                    other.Realm))
            {
                continue;
            }

            float distance = Land.SquaredDist(
                actor.Cell,
                other.Cell);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = other;
            }
        }

        return nearest;
    }

    private static bool IsOccupyArmyGroupMember(Person actor)
    {
        return actor != null &&
               actor.Alive &&
               actor.IsWarrior();
    }

    // ============================================================
    // State
    // ============================================================

    private static ActorMoveState GetState(Person actor)
    {
        long id = actor.Key;

        if (!_moveStates.TryGetValue(
                id,
                out ActorMoveState state))
        {
            state = new ActorMoveState
            {
                actor = actor,
                lastObservedTile = actor.Cell,
                lastProgressFrame = Time.frameCount,
                lastCheckFrame = Time.frameCount
            };

            _moveStates[id] = state;
        }

        state.actor = actor;

        return state;
    }

    private static void SetStateTarget(
        Person actor,
        ActorMoveState state,
        Cell target)
    {
        state.targetTile = target;
        state.lastObservedTile = actor.Cell;
        state.lastProgressFrame = Time.frameCount;
        state.lastCheckFrame = Time.frameCount;
        state.recoveryCount = 0;
    }

    private static void MarkProgress(
        Person actor,
        ActorMoveState state)
    {
        state.lastObservedTile = actor.Cell;
        state.lastProgressFrame = Time.frameCount;
        state.lastCheckFrame = Time.frameCount;
        state.recoveryCount = 0;
    }

    private static void ClearActorMoveState(Person actor)
    {
        if (actor == null || actor.Record == null)
            return;

        long actorId;
        try
        {
            actorId = actor.Key;
        }
        catch
        {
            // The BaseSimObject may already be partially disposed.
            return;
        }

        ClearActorMoveState(actorId, actor);
    }

    private static void ClearActorMoveState(long actorId, Person actor)
    {
        // actorId comes from _moveStates / queue bookkeeping and remains usable
        // even after the Person's BaseSimObject data has been released.
        if (actor != null && actor.Record != null)
        {
            try
            {
                actor.ClearFrontLineMoveTarget();
            }
            catch
            {
                // Cleanup must never break the simulation update loop.
            }
        }

        _moveStates.Remove(actorId);
    }
}
}
