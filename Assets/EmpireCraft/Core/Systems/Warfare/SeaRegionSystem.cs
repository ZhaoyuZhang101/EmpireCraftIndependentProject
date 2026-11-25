// Generated from the EmpireCraft mod: Scripts/GeneralSystems/Warfare/SeaRegionSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 海区：海军(NavySystem)的地图单位。
//   · 一个地块区块(8×8 格)几乎全是水(陆地 ≤ 16 格)就算海；
//   · 连通的海面从一块未分的海开始向外广度扩张，满 RegionSize 块切成一个海区，大洋因此分成许多海区，制海权可以局部争夺；
//   · 记下海区之间的相邻关系(找航线用)、海区的中心块(画兵牌)、沿岸港口城(港口城的地块与海区相邻)。
// 读档或换地图后第一次用到时建一次；海陆变了(造岛、填海)最多每年重建一次。
// 以前不论变没变每年整图重划一次(排序全图地块、广度扩张)；现在设地面格(MapWallPatch)时看这块地算不算海变没变，
// 变了才记下要重划，到一年照旧重划，没变就不划；十年兜底重划一次
public static class SeaRegionSystem
{
    private const int RegionSize = 60;
    private const int MaxGround = 16;
    private const int FallbackYears = 10;

    public sealed class Region
    {
        public int Id;
        public readonly List<Cell> Zones = new();
        public readonly HashSet<int> Neighbours = new();
        public Cell Center;
        // 所在水域(连通的海区同一个编号)：隔着大陆的两片海到不了
        public int Basin = -1;
        // 制海权：最近一次海战胜出(或独自在场)的一方，及其时间
        public long ControllerId = -1L;
        public double ControlledAt = -1d;
    }

    private static readonly List<Region> Regions = new();
    private static readonly Dictionary<int, int> RegionByZone = new();
    private static object _world;
    private static double _builtAt = -1d;
    // 上次划分时各地块算不算海(按地块编号)；有地块变了就要重划
    private static bool[] _seaAtBuild = new bool[0];
    private static bool _dirty;

    public static IReadOnlyList<Region> All
    {
        get
        {
            EnsureBuilt();
            return Regions;
        }
    }

    public static void ResetWorldState()
    {
        Regions.Clear();
        RegionByZone.Clear();
        ShoreCache.Clear();
        _world = null;
        _builtAt = -1d;
        _seaAtBuild = new bool[0];
        _dirty = false;
    }

    // 设地面格之后(MapWallPatch)：这块地算不算海和上次划分时不一样了，记下要重划
    public static void OnTileChanged(Cell zone)
    {
        if (_dirty || zone == null || zone.Index < 0 || zone.Index >= _seaAtBuild.Length) return;
        if (_seaAtBuild[zone.Index] != IsSea(zone)) _dirty = true;
    }

    // 城的海岸：本城临海的陆上地块(中心格坐标)及外面海区所在的水域；Offset 是离城心最远的一处(按距离粗筛用)。
    // 水师辐射量到对方最近的海岸，不量城心(玩家反馈"两个隔海的国家一千五百年没打下来"：城心在内陆几十格的城，
    // 隔着窄海也"够不着")。按城缓存，一个月或城的地块数变了重算
    public sealed class Shore
    {
        public readonly List<(Vector2 at, int basin)> Points = new();
        public float Offset;
    }

    private static readonly Dictionary<long, (double at, int zones, Shore shore)> ShoreCache = new();

    public static Shore ShoreOf(City city)
    {
        var empty = new Shore();
        if (city?.Zones == null || city.Record == null || EmpireWorld.Current == null) return empty;
        EnsureBuilt();
        double now = GameClock.Now;
        if (ShoreCache.TryGetValue(city.Key, out var cached) && cached.zones == city.Zones.Count && now >= cached.at &&
            GameClock.MonthsSince(cached.at) < 1)
            return cached.shore;
        var shore = new Shore();
        foreach (Cell zone in city.Zones)
        {
            if (zone?.Center == null || IsSea(zone)) continue;
            var at = new Vector2(zone.Center.X, zone.Center.Y);
            var basins = new HashSet<int>();
            foreach (Cell next in zone.NeighboursAll ?? Array.Empty<Cell>())
                if (next != null && RegionByZone.TryGetValue(next.Index, out int id) && basins.Add(Regions[id].Basin))
                    shore.Points.Add((at, Regions[id].Basin));
            if (basins.Count > 0) shore.Offset = Mathf.Max(shore.Offset, (at - city.Center).magnitude);
        }
        if (ShoreCache.Count > 4096) ShoreCache.Clear();
        ShoreCache[city.Key] = (now, city.Zones.Count, shore);
        return shore;
    }

    public static bool IsSea(Cell zone) =>
        zone != null && !zone.OnEdge && zone.GroundCount <= MaxGround && zone.LiquidCount > 0;

    public static Region Get(int id)
    {
        EnsureBuilt();
        return id >= 0 && id < Regions.Count ? Regions[id] : null;
    }

    public static Region Of(Cell zone)
    {
        EnsureBuilt();
        return zone != null && RegionByZone.TryGetValue(zone.Index, out int id) ? Regions[id] : null;
    }

    private static void EnsureBuilt()
    {
        if (EmpireWorld.Current?.Zones == null) return;
        double now = GameClock.Now;
        bool stale = !ReferenceEquals(_world, EmpireWorld.Current) || Regions.Count == 0 && _builtAt < 0d ||
                     _builtAt >= 0d && (now < _builtAt ||
                                        GameClock.YearsSince(_builtAt) >= (_dirty ? 1 : FallbackYears));
        if (!stale) return;
        _world = EmpireWorld.Current;
        _builtAt = now;
        using var timing = FrameProfiler.Measure("海区·划分");
        try
        {
            Build();
            _dirty = false;
        }
        catch (Exception exception)
        {
            // 划分失败照旧一年后再试
            _dirty = true;
            LogService.LogWarning($"[EmpireCraft][海区] 划分失败: {exception.Message}");
        }
    }

    private static void Build()
    {
        // 记下这次划分时各地块算不算海
        List<Cell> all = EmpireWorld.Current.Zones.All;
        int size = all.Count;
        foreach (Cell zone in all)
            if (zone != null && zone.Index >= size) size = zone.Index + 1;
        if (_seaAtBuild.Length != size) _seaAtBuild = new bool[size];
        foreach (Cell zone in all)
            if (zone != null && zone.Index >= 0) _seaAtBuild[zone.Index] = IsSea(zone);
        // 保留旧的制海权(按中心块对上)
        var oldControl = new Dictionary<int, (long, double)>();
        foreach (Region region in Regions)
            if (region.Center != null && region.ControllerId >= 0)
                oldControl[region.Center.Index] = (region.ControllerId, region.ControlledAt);
        Regions.Clear();
        RegionByZone.Clear();
        ShoreCache.Clear();
        List<Cell> zones = EmpireWorld.Current.Zones.All;
        foreach (Cell seed in zones.OrderBy(zone => zone.Y).ThenBy(zone => zone.X))
        {
            if (!IsSea(seed) || RegionByZone.ContainsKey(seed.Index)) continue;
            var region = new Region { Id = Regions.Count };
            var queue = new Queue<Cell>();
            queue.Enqueue(seed);
            RegionByZone[seed.Index] = region.Id;
            while (queue.Count > 0 && region.Zones.Count < RegionSize)
            {
                Cell zone = queue.Dequeue();
                region.Zones.Add(zone);
                foreach (Cell next in zone.Neighbours ?? Array.Empty<Cell>())
                {
                    if (!IsSea(next) || RegionByZone.ContainsKey(next.Index)) continue;
                    if (region.Zones.Count + queue.Count >= RegionSize) break;
                    RegionByZone[next.Index] = region.Id;
                    queue.Enqueue(next);
                }
            }
            // 排队但没轮到的块还给后面的海区
            foreach (Cell left in queue) RegionByZone.Remove(left.Index);
            Regions.Add(region);
        }
        // 中心块：离各块平均位置最近的一块(画兵牌、旗舰驶向的位置)
        foreach (Region region in Regions)
        {
            float cx = (float)region.Zones.Average(zone => zone.X), cy = (float)region.Zones.Average(zone => zone.Y);
            region.Center = region.Zones.OrderBy(zone => (zone.X - cx) * (zone.X - cx) + (zone.Y - cy) * (zone.Y - cy))
                .First();
        }
        foreach (Region region in Regions)
            foreach (Cell zone in region.Zones)
                foreach (Cell next in zone.Neighbours ?? Array.Empty<Cell>())
                    if (next != null && RegionByZone.TryGetValue(next.Index, out int other) && other != region.Id)
                        region.Neighbours.Add(other);
        int basins = 0;
        foreach (Region seed in Regions)
        {
            if (seed.Basin >= 0) continue;
            seed.Basin = basins;
            var open = new Stack<Region>();
            open.Push(seed);
            while (open.Count > 0)
                foreach (int next in open.Pop().Neighbours)
                    if (Regions[next].Basin < 0)
                    {
                        Regions[next].Basin = basins;
                        open.Push(Regions[next]);
                    }
            basins++;
        }
        foreach (Region region in Regions)
            if (region.Center != null && oldControl.TryGetValue(region.Center.Index, out var control))
                (region.ControllerId, region.ControlledAt) = control;
    }

    // 与城市地块相邻的海区(港口城出海、登陆点)
    public static List<Region> RegionsAround(City city)
    {
        var result = new List<Region>();
        if (city?.Zones == null) return result;
        EnsureBuilt();
        var seen = new HashSet<int>();
        foreach (Cell zone in city.Zones)
            foreach (Cell next in zone?.NeighboursAll ?? Array.Empty<Cell>())
                if (next != null && RegionByZone.TryGetValue(next.Index, out int id) && seen.Add(id))
                    result.Add(Regions[id]);
        return result;
    }

    // 两组海区之间的最短航线(按海区数，含首尾)；不通返回 null。blocked：不能经过的海区(终点除外)
    public static List<Region> Route(IEnumerable<Region> from, ICollection<Region> to, Func<Region, bool> blocked = null)
    {
        EnsureBuilt();
        var targets = new HashSet<int>(to.Where(region => region != null).Select(region => region.Id));
        if (targets.Count == 0) return null;
        var previous = new Dictionary<int, int>();
        var queue = new Queue<int>();
        foreach (Region start in from)
        {
            if (start == null || previous.ContainsKey(start.Id)) continue;
            if (blocked != null && blocked(start) && !targets.Contains(start.Id)) continue;
            previous[start.Id] = -1;
            queue.Enqueue(start.Id);
        }
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (targets.Contains(current))
            {
                var path = new List<Region>();
                for (int id = current; id >= 0; id = previous[id]) path.Add(Regions[id]);
                path.Reverse();
                return path;
            }
            foreach (int next in Regions[current].Neighbours)
            {
                if (previous.ContainsKey(next)) continue;
                if (blocked != null && !targets.Contains(next) && blocked(Regions[next])) continue;
                previous[next] = current;
                queue.Enqueue(next);
            }
        }
        return null;
    }

    public static float Distance(Region a, Region b) =>
        a?.Center == null || b?.Center == null ? float.MaxValue
            : Mathf.Max(Mathf.Abs(a.Center.X - b.Center.X), Mathf.Abs(a.Center.Y - b.Center.Y));
}
}
