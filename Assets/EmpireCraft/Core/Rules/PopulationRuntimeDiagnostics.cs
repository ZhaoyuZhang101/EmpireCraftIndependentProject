// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PopulationRuntimeDiagnostics.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using System.Linq;

namespace EmpireCraft.Core
{

public static class PopulationRuntimeDiagnostics
{
    private static object _world;
    private static float _at;
    private static bool _active;
    public static void Reset() { _world = null; _active = false; _at = 0f; }
#if EC_PENDING
    public static void Tick()
    {
        // 性能诊断日志关着时不记(见 FrameProfiler.Enabled)
        if (!CityPopulationSystem.AbstractPopulationEnabled || !FrameProfiler.Enabled) { _active = false; return; }
        if (EmpireWorld.Current?.Cities == null || !GameClock.Running || GameClock.Paused || GameClock.Loading) return;
        bool started = !_active || !ReferenceEquals(_world, EmpireWorld.Current) || Time.unscaledTime < _at;
        if (started)
        {
            _active = true;
            _world = EmpireWorld.Current;
            FrameProfiler.Reset();
            string header = "[EmpireCraft][性能采样启动] 版本 2026-10-09-profile-4；虚拟人口已开启；原版计时补丁 " +
                EmpireCraft.Core.NativeWorldOptimizationPatch.Initialized +
                "；u4_deadCheck 前缀=" + NativeBenchmarkSnapshot.ActorPrefixOwners() +
                "；记录均值、单次峰值与帧耗时 P95/P99，嵌套标签不要相加；时间戳为 UTC";
            PerformanceTraceFile.Record(header);
            LogService.LogInfo(header);
        }
        else if (Time.unscaledTime - _at < 30f) return;
        _at = Time.unscaledTime;
        int groups = 0;
        double background = 0d;
        // 只看各城已经保存的数值，不遍历单位和族谱，不重新计算经济、阶层或住房。
        foreach (City city in EmpireWorld.Current.Cities)
        {
            if (city?.Record == null || city.IsGone()) continue;
            var population = CityPopulationSystem.Get(city);
            groups += population.groups.Count;
            foreach (var group in population.groups) if (group != null) background += group.Background;
        }
        // 实体单位构成：找出地图上的人都是谁
        int soldiers = 0, generals = 0, vehicles = 0, rulers = 0, children = 0, adults = 0, wild = 0;
        var reasons = new global::System.Collections.Generic.Dictionary<string, int>();
        foreach (Person actor in EmpireWorld.Current.Living)
        {
            if (actor?.Record == null || actor.IsGone() || !actor.Alive) continue;
            if (CityPopulationSystem.IsVehicle(actor)) vehicles++;
            else if (actor.asset == null || !actor.asset.civ) wild++;
            else if (CorpsSystem.IsGeneral(actor)) generals++;
            else if (CityPopulationSystem.IsSoldier(actor)) soldiers++;
            else if (actor.IsRuler() || actor.IsCityLeader()) rulers++;
            else
            {
                if (!actor.IsAdult()) children++;
                else adults++;
                // 其余实体为什么留在地图上(孩童与成人一并统计)
                string reason = CityPopulationSystem.NotableReason(actor);
                reasons.TryGetValue(reason, out int count);
                reasons[reason] = count + 1;
            }
        }
        int corps = 0;
        foreach (var _ in CorpsSystem.All()) corps++;
        string why = string.Join("、", reasons.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value}"));
        string composition = $"[EmpireCraft][实体构成] 士兵 {soldiers}；军团统领 {generals}；载具 {vehicles}；" +
                             $"君主城主 {rulers}；孩童 {children}；其余成人 {adults}；野生 {wild}；军团 {corps} 支" +
                             $"(军团模式{(CorpsSystem.Enabled ? "开" : "关")})；孩童与其余成人按留存原因：{why}；" +
                             $"近 30 秒新生成实体 {EntitySpawnStats.Flush()}";
        LogService.LogInfo(composition);
        string report = $"[EmpireCraft][虚拟人口负载] 实体单位 {EmpireWorld.Current.Living.Count}；" +
            $"城市 {EmpireWorld.Current.Cities.Count}；人口组 {groups}；城内背景人口 {background:0}；" +
            $"建筑 {EmpireWorld.Current.buildings.Count}；后台待办 {PopulationMathWorkers.PendingCount}/64；" +
            $"可见植物建筑 {EmpireWorld.Current.buildings.countVisibleBuildings()}；地块 {EmpireWorld.Current.Zones.Length}；" +
            $"满果植物休眠 {NativeVegetationScheduler.SleepingCount}；地块检查/原版扫描 " +
            $"{EmpireCraft.Core.NativeWorldOptimizationPatch.ZoneChecks}/" +
            $"{EmpireCraft.Core.NativeWorldOptimizationPatch.OriginalZoneChecks}；" +
            $"合并地图刷新 {EmpireCraft.Core.NativeWorldOptimizationPatch.DeferredMiniMaps}；" +
            $"省略空边缘上传 {EmpireCraft.Core.NativeWorldOptimizationPatch.SkippedEdgeUploads}；" +
            $"边缘逐格重画 {EmpireCraft.Core.NativeWorldOptimizationPatch.PartialEdgeRedraws}；" +
            $"原版建筑并行帧 {EmpireCraft.Core.NativeWorldOptimizationPatch.BuildingParallelFrames}；" +
            $"最近建筑批次实际后台线程 {EmpireCraft.Core.NativeWorldOptimizationPatch.LastBuildingBackgroundThreads}；" +
            $"线程 {PopulationMathWorkers.WorkerCount}；举人排名后台命中/即时计算 " +
            $"{PopulationJurenRanking.WorkerResults}/{PopulationJurenRanking.FallbackResults}；理念后台命中/即时计算 " +
            $"{IdeologyPopulationSystem.ContactWorkerResults}/{IdeologyPopulationSystem.ContactFallbackResults}；经济后台命中/即时计算 " +
            $"{PopulationParallelSystem.EconomyWorkerResults}/{PopulationParallelSystem.EconomyFallbackResults}；" +
            $"财政预算后台命中/重算 {CityStabilitySystem.WorkerResults}/{CityStabilitySystem.FallbackResults}；" +
            $"财政待办城市 {CityStabilitySystem.PendingCities}/16；" +
            $"撤军待办城市 {CityStabilitySystem.PendingRetirements}；" +
            $"占领视图命中/重建 {OccupationReadIndex.Hits}/{OccupationReadIndex.Builds}；" +
            $"战争统计命中/重算 {OccupationReadIndex.ControlHits}/{OccupationReadIndex.ControlBuilds}；" +
            $"官员名单命中/重建 {OfficeCandidateRoster.Hits}/{OfficeCandidateRoster.Builds}；" +
            $"军团统计命中/重建 {LegionStatisticsReadCache.Hits}/{LegionStatisticsReadCache.Builds}；" +
            $"风车农田扫描/地块读取 {VirtualFarmScan.Scans}/{VirtualFarmScan.TileChecks}。结合[帧率]与[每帧耗时]判断瓶颈";
        LogService.LogInfo(report);
        PerformanceTraceFile.Record(report);
        NativeBenchmarkSnapshot.Record();
    }
#endif
}
}
