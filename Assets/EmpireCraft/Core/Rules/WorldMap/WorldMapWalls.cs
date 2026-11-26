// Generated from the EmpireCraft mod: Scripts/GeneralSystems/WorldMap/WorldMapWalls.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Vector2Int = EmpireCraft.Core.EcVector2Int;
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 剧本的长城与关隘(用户 10-29 "我的所有剧本里面好像都没有长城和关口")：eras/*.json 的 walls(经纬度折线，见
// Tools/WorldMap/walls_source.py)与 passes，进世界后先铺墙、后筑关：
//   · 墙：沿线加密、侧向起伏的各点之间在走廊带里找最省的路——山、丘陵便宜，河边稍便宜，水面、岩浆不走，城心那块绕开——所以墙沿山脊、河谷走、八邻连通；
//     纯净地图下铺图层城墙(MapWallSystem.Add，挡兵、路网绕开)，普通地图下直接铺原版城墙顶层格(山上不铺)；
//   · 关：纯净地图下筑关城(RuralSettlementSystem.Place，压着城墙的那格成关门，路从关门过墙)，名字照剧本；
//     点在山上筑不成的，往外找最近筑得成的一格；普通地图没有关城。
// 走向要自然(用户 10-30 "但是长城也不要这么直，要自然一点"，截图秦长城在陕北两点之间拉成一条直线)：剧本的点隔一度上下、几乎排成直线，
// 黄土丘陵在图上又是一片平地，地形引不动路。现在先把整道墙按 Step 格一个点加密，沿线给一个平滑起伏的侧向偏移(Amplitude 度，主起伏
// Wave 度一个、叠三分之一的小起伏)，两头与关隘处收成零(关城仍压在墙上)；偏移不越过城(见 Corridor)。再在这些点之间一小段一小段按地形找路：
// 山、丘陵便宜(沿山脊)，河边稍便宜(顺河谷)，代价另叠两层平滑起伏，水面不走，城心那块绕开。
// 墙不绕到城背后去：走廊里的城心周围不过墙，离折线两格以外的城，城那一侧比城离折线还远的那片不走(城在墙哪边照剧本)。
// 剧本开局铺辖地前先定下走向(Plan)：铺辖地、分行政区都按这条实际走向分两边(见 WorldMapZones.SetWalls、FrontierWalls)，进世界后照它铺墙
internal static class WorldMapWalls
{
    // 走廊带：离剧本两点连线至多 CorridorShare 倍段长(至少 CorridorMin 格)
    private const float CorridorShare = 0.4f;
    private const int CorridorMin = 8, PassSearch = 4, Step = 6;
    // 侧向起伏(度)：幅度、主起伏的波长；两头与关隘这么近(度)以内收成零
    private const float Amplitude = 0.45f, Wave = 1.2f, Taper = 0.5f;
    // 从当前世界导入的剧本：墙就是原来那道，不再加起伏
    private static bool _keepLine;
    private const float MountainCost = 0.6f, HillCost = 0.75f, CityCost = 4f, RiverSide = 0.85f;
    private const float WiggleCell = 16f, WiggleFine = 6f;
    // 城心周围几格不过墙；城前后几格的"城背后"不走
    private const float CityClear = 3f, CityShadow = 7f;
    // 实际走向化简成折线(分两边用)：偏差不超过这么多格
    private const float SimplifyTiles = 1.5f;

    // 剧本开局定下的走向(见 Plan)：各道墙的几段格子(格编号)；进世界后照它铺
    private static List<(string name, List<List<int>> paths)> _planned;
    private static string _plannedEra;
    // 定下的墙压着的区块(补空小国不立在墙上，见 WorldMapScenario.PlanFillers)
    private static readonly HashSet<int> PlannedZones = new();
    private static int _seed;

    // 剧本开局(各城已立、铺辖地以前)：定下各道墙的走向，返回化简后的格坐标折线(铺辖地、分行政区按它分两边)；
    // 两点之间隔水铺不出路的照剧本折线连
#if EC_PENDING
    public static List<List<Vector2>> Plan(WorldMapEra era, WorldMapFrame frame, Vector2Int size)
    {
        _planned = null;
        _plannedEra = era?.id;
        _keepLine = era?.exact == true;
        PlannedZones.Clear();
        _seed = Environment.TickCount;
        var lines = new List<List<Vector2>>();
        if (era == null || frame == null || EmpireWorld.Current == null) return lines;
        _planned = Routes(era, frame, size, Seats());
        foreach (var (_, paths) in _planned)
            foreach (List<int> path in paths)
            {
                foreach (int id in path)
                    if (Tile(id)?.Zone is Cell zone) PlannedZones.Add(zone.Index);
                List<Vector2> line = Simplify(path.Select(id => new Vector2(id % EmpireWorld.Current.Width, id / EmpireWorld.Current.Width)).ToList(), SimplifyTiles);
                if (line.Count >= 2) lines.Add(line);
            }
        return lines;
    }
#endif

    // 这块地压着开局定下的墙
    public static bool OnPlanned(Cell zone) => zone != null && PlannedZones.Contains(zone.Index);

#if EC_PENDING
    public static void Build(WorldMapEra era, WorldMapFrame frame, Vector2Int size)
    {
        _keepLine = era?.exact == true;
        if (era == null || frame == null || EmpireWorld.Current == null) return;
        TerrainKind vanilla = WallType();
        if (vanilla == null) return;
        // 开局定下的走向(分两边用的就是它)；没定过的现算
        List<(string name, List<List<int>> paths)> routes = _planned != null && _plannedEra == era.id ? _planned : Routes(era, frame, size, Seats());
        _planned = null;
        PlannedZones.Clear();
        foreach ((string name, List<List<int>> paths) in routes)
        {
            int laid = 0;
            foreach (List<int> path in paths)
                foreach (int id in path)
                    if (Lay(Tile(id), vanilla)) laid++;
            LogService.LogInfo($"[EmpireCraft][世界地图] 长城 {name}：{laid} 格");
        }
        if (!PureMapMode.Enabled) return;
        foreach (WorldMapPass pass in era.passes ?? new List<WorldMapPass>())
        {
            if (pass.at == null || pass.at.Length < 2) continue;
            Vector2Int at = EarthTerrain.TileOf(frame, size, pass.at[0], pass.at[1]);
            RuralSettlement fort = Fort(at);
            if (fort == null)
            {
                LogService.LogInfo($"[EmpireCraft][世界地图] 关隘 {pass.Name} 没筑成");
                continue;
            }
            RuralSettlementSystem.Rename(fort, pass.Name);
        }
    }
#endif

    // 各道墙的路线：加密、侧向起伏后一小段一小段找路接起来；有点在图外就从那里断开
#if EC_PENDING
    private static List<(string name, List<List<int>> paths)> Routes(WorldMapEra era, WorldMapFrame frame, Vector2Int size, List<Vector2> seats)
    {
        var result = new List<(string name, List<List<int>> paths)>();
        frame.Bounds(size, out _, out float degrees);
        float perDegree = degrees > 0f ? 1f / degrees : 20f;
        List<Vector2> passes = (era.passes ?? new List<WorldMapPass>()).Where(pass => pass.at != null && pass.at.Length >= 2)
            .Select(pass => (Vector2)EarthTerrain.TileOf(frame, size, pass.at[0], pass.at[1])).ToList();
        int index = 0;
        foreach (WorldMapWall wall in era.walls ?? new List<WorldMapWall>())
        {
            List<Vector2Int> points = (wall.pts ?? new List<float[]>()).Where(point => point != null && point.Length >= 2)
                .Select(point => EarthTerrain.TileOf(frame, size, point[0], point[1])).ToList();
            var paths = new List<List<int>>();
            var path = new List<int>();
            List<(Vector2Int at, Corridor corridor)> waypoints = Waypoints(points, seats, passes, perDegree, index++);
            for (int i = 0; i + 1 < waypoints.Count; i++)
            {
                (Vector2Int from, Corridor corridor) = waypoints[i];
                Vector2Int to = waypoints[i + 1].at;
                if (!Inside(from) || !Inside(to))
                {
                    if (path.Count > 0) paths.Add(path);
                    path = new List<int>();
                    continue;
                }
                foreach (Cell tile in Leg(from, to, corridor))
                {
                    int id = tile.Y * EmpireWorld.Current.Width + tile.X;
                    if (path.Count == 0 || path[path.Count - 1] != id) path.Add(id);
                }
            }
            if (path.Count > 0) paths.Add(path);
            result.Add((wall.Name, paths));
        }
        return result;
    }
#endif

    // 剧本一段(两点连线)的走廊：带宽、带里的城；墙的点与路都要它放行
    private sealed class Corridor
    {
        private readonly Vector2 _origin, _dir;
        public readonly Vector2 normal;
        private readonly float _margin;
        private readonly List<(Vector2 seat, float u, float v)> _cities = new();

#if EC_PENDING
        public Corridor(Vector2Int a, Vector2Int b, List<Vector2> seats)
        {
            _origin = a;
            Vector2 axis = b - a;
            float length = axis.magnitude;
            _dir = length > 0f ? axis / length : Vector2.right;
            normal = new Vector2(-_dir.y, _dir.x);
            _margin = Mathf.Max(CorridorMin, Mathf.Ceil(length * CorridorShare));
            float x0 = Mathf.Min(a.x, b.x) - _margin, x1 = Mathf.Max(a.x, b.x) + _margin;
            float y0 = Mathf.Min(a.y, b.y) - _margin, y1 = Mathf.Max(a.y, b.y) + _margin;
            foreach (Vector2 seat in seats)
                if (seat.x >= x0 && seat.x <= x1 && seat.y >= y0 && seat.y <= y1)
                    _cities.Add((seat, Vector2.Dot(seat - _origin, _dir), Vector2.Dot(seat - _origin, normal)));
        }

        public bool Allows(int x, int y)
        {
            var at = new Vector2(x, y);
            float u = Vector2.Dot(at - _origin, _dir), v = Vector2.Dot(at - _origin, normal);
            if (Mathf.Abs(v) > _margin) return false;
            foreach ((Vector2 seat, float cu, float cv) in _cities)
            {
                if ((at - seat).sqrMagnitude < CityClear * CityClear) return false;
                // 城在墙哪边照剧本：城那一侧、比城离连线还远的那片(城前后 CityShadow 格)不走；压在连线上的城不管
                if (Mathf.Abs(cv) >= 2f && v * cv > 0f && Mathf.Abs(v) >= Mathf.Abs(cv) - 2f && Mathf.Abs(u - cu) <= CityShadow) return false;
            }
            return true;
        }
#endif
    }

    // 加密成 Step 格一个点，各点按沿线的平滑起伏往两侧偏(两头、关隘附近收成零；偏过去落水、越过城的减半再试，不行就不偏)
#if EC_PENDING
    private static List<(Vector2Int at, Corridor corridor)> Waypoints(List<Vector2Int> points, List<Vector2> seats, List<Vector2> passes,
        float perDegree, int wall)
    {
        var result = new List<(Vector2Int at, Corridor corridor)>();
        if (points.Count < 2) return result;
        float total = 0f;
        for (int i = 1; i < points.Count; i++) total += Vector2Int.Distance(points[i - 1], points[i]);
        float amplitude = (_keepLine ? 0f : Amplitude) * perDegree, taper = Taper * perDegree, wave = Wave * perDegree;
        float arc = 0f;
        Corridor last = null;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2Int a = points[i], b = points[i + 1];
            var corridor = last = new Corridor(a, b, seats);
            float length = Vector2Int.Distance(a, b);
            int count = Mathf.Max(1, Mathf.RoundToInt(length / Step));
            for (int k = 0; k < count; k++)
            {
                float t = k / (float)count;
                Vector2 at = Vector2.Lerp(a, b, t);
                float along = arc + length * t;
                float fade = Mathf.Clamp01(Mathf.Min(along, total - along) / taper);
                foreach (Vector2 pass in passes) fade = Mathf.Min(fade, Mathf.Clamp01(Vector2.Distance(at, pass) / taper));
                int s = Mathf.RoundToInt(along);
                float swing = 0.65f * ValueNoise(s, wall * 977, _seed, wave) + 0.35f * ValueNoise(s, wall * 977, _seed + 31, wave / 3f);
                float offset = Mathf.Clamp((swing * 2f - 1f) * 1.8f, -1f, 1f) * amplitude * fade;
                Vector2Int chosen = Vector2Int.RoundToInt(at);
                foreach (float share in new[] { 1f, 0.5f })
                {
                    Vector2Int moved = Vector2Int.RoundToInt(at + corridor.normal * (offset * share));
                    if (!Inside(moved) || !Passable(EmpireWorld.Current.CellAt(moved.x, moved.y)) || !corridor.Allows(moved.x, moved.y)) continue;
                    chosen = moved;
                    break;
                }
                if (result.Count == 0 || result[result.Count - 1].at != chosen) result.Add((chosen, corridor));
            }
            arc += length;
        }
        Vector2Int end = points[points.Count - 1];
        if (result[result.Count - 1].at != end) result.Add((end, last));
        return result;
    }
#endif

    // 相邻两点之间的最短路(八邻，限在走廊里)；走不通(隔着水)时照直线连，水面上的格子铺不上自然断开
#if EC_PENDING
    private static List<Cell> Leg(Vector2Int a, Vector2Int b, Corridor corridor)
    {
        var path = new List<Cell>();
        int margin = Mathf.Max(4, Mathf.CeilToInt(Vector2Int.Distance(a, b) * 0.5f));
        int x0 = Mathf.Max(0, Mathf.Min(a.x, b.x) - margin), x1 = Mathf.Min(EmpireWorld.Current.Width - 1, Mathf.Max(a.x, b.x) + margin);
        int y0 = Mathf.Max(0, Mathf.Min(a.y, b.y) - margin), y1 = Mathf.Min(EmpireWorld.Current.Height - 1, Mathf.Max(a.y, b.y) + margin);
        int width = x1 - x0 + 1, count = width * (y1 - y0 + 1);
        int Index(int x, int y) => (y - y0) * width + (x - x0);
        var best = new float[count];
        var from = new int[count];
        var cost = new float[count];
        for (int i = 0; i < count; i++)
        {
            best[i] = float.MaxValue;
            from[i] = -1;
            cost[i] = -1f;
        }
        int start = Index(a.x, a.y), goal = Index(b.x, b.y);
        best[start] = 0f;
        var heap = new WorldMapZones.MinHeap();
        heap.Push(0f, start);
        while (heap.Count > 0)
        {
            heap.Pop(out float key, out int index);
            if (key > best[index]) continue;
            if (index == goal) break;
            int x = x0 + index % width, y = y0 + index / width;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < x0 || ny < y0 || nx > x1 || ny > y1) continue;
                int neighbour = Index(nx, ny);
                if (cost[neighbour] < 0f)
                {
                    Cell tile = EmpireWorld.Current.CellAt(nx, ny);
                    cost[neighbour] = Passable(tile) && (neighbour == goal || corridor.Allows(nx, ny)) ? Cost(tile) : float.PositiveInfinity;
                }
                if (float.IsPositiveInfinity(cost[neighbour])) continue;
                float next = key + (dx != 0 && dy != 0 ? 1.414f : 1f) * cost[neighbour];
                if (next >= best[neighbour]) continue;
                best[neighbour] = next;
                from[neighbour] = index;
                heap.Push(next, neighbour);
            }
        }
        if (from[goal] < 0 && goal != start) return Straight(a, b);
        for (int index = goal; index >= 0; index = from[index])
        {
            path.Add(EmpireWorld.Current.CellAt(x0 + index % width, y0 + index / width));
            if (index == start) break;
        }
        path.Reverse();
        return path;
    }
#endif

    private static List<Vector2> Seats()
    {
        var seats = new List<Vector2>();
        foreach (City city in EmpireWorld.Current?.Cities ?? new List<City>())
        {
            Cell tile = city?.Record == null || city.IsGone() ? null : city.SeatCell();
            if (tile != null) seats.Add(new Vector2(tile.X, tile.Y));
        }
        return seats;
    }

    private static Cell Tile(int id)
    {
        Cell[] tiles = EmpireWorld.Current?.AllCells;
        return tiles != null && id >= 0 && id < tiles.Length ? tiles[id] : null;
    }

#if EC_PENDING
    private static TerrainKind WallType()
    {
        TerrainKind fallback = null;
        foreach (TerrainKind type in AssetManager.top_tiles?.list ?? new List<TerrainKind>())
        {
            if (type?.Wall != true) continue;
            if (type.Id == "wall_order") return type;
            fallback ??= type;
        }
        return fallback;
    }
#endif

    private static bool Inside(Vector2Int point) =>
        point.x >= 2 && point.y >= 2 && point.x < EmpireWorld.Current.Width - 2 && point.y < EmpireWorld.Current.Height - 2;

    private static bool Passable(Cell tile) => tile?.Type != null && !tile.Type.Liquid && !tile.Type.Lava;

    private static bool CityCore(Cell tile) =>
        tile.Zone?.City is City city && city.SeatCell()?.Zone == tile.Zone;

    private static float Cost(Cell tile)
    {
        TerrainKind type = tile.Type;
        float cost = type.Mountains || type.Summit ? MountainCost
            : type.Rocks || type.Id == "hills" || type.Id == "snow_hills" || tile.MainType?.Id == "hills" ? HillCost : 1f;
        if (NearWater(tile)) cost *= RiverSide;
        cost *= 0.5f + Wiggle(tile.X, tile.Y);
        return CityCore(tile) ? cost + CityCost : cost;
    }

    // 两格内有水(河谷、湖边)
    private static bool NearWater(Cell tile)
    {
        for (int dx = -2; dx <= 2; dx++)
        for (int dy = -2; dy <= 2; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            Cell near = EmpireWorld.Current.CellAt(tile.X + dx, tile.Y + dy);
            if (near?.Type != null && near.Type.Liquid) return true;
        }
        return false;
    }

    // 两层值噪声(0~1)：大弯折叠小起伏
    private static float Wiggle(int x, int y) => 0.65f * ValueNoise(x, y, _seed, WiggleCell) + 0.35f * ValueNoise(x, y, _seed + 7919, WiggleFine);

    private static float ValueNoise(int x, int y, int seed, float cell)
    {
        float fx = x / cell, fy = y / cell;
        int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
        float sx = fx - ix, sy = fy - iy;
        sx = sx * sx * (3f - 2f * sx);
        sy = sy * sy * (3f - 2f * sy);
        float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    private static List<Cell> Straight(Vector2Int a, Vector2Int b)
    {
        var path = new List<Cell>();
        int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : i / (float)steps;
            path.Add(EmpireWorld.Current.CellAt(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t))));
        }
        return path;
    }

    // 折线化简(道格拉斯-普克)：偏离连线不到 tolerance 格的点去掉
    private static List<Vector2> Simplify(List<Vector2> points, float tolerance)
    {
        if (points.Count < 3) return points;
        var keep = new bool[points.Count];
        keep[0] = keep[points.Count - 1] = true;
        var stack = new Stack<(int first, int last)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            (int first, int last) = stack.Pop();
            Vector2 p = points[first], q = points[last], pq = q - p;
            float length = pq.magnitude;
            int farthest = -1;
            float distance = tolerance;
            for (int i = first + 1; i < last; i++)
            {
                Vector2 r = points[i] - p;
                float d = length > 0f ? Mathf.Abs(pq.x * r.y - pq.y * r.x) / length : r.magnitude;
                if (d <= distance) continue;
                distance = d;
                farthest = i;
            }
            if (farthest < 0) continue;
            keep[farthest] = true;
            stack.Push((first, farthest));
            stack.Push((farthest, last));
        }
        return points.Where((_, i) => keep[i]).ToList();
    }

#if EC_PENDING
    private static bool Lay(Cell tile, TerrainKind vanilla)
    {
        if (!Passable(tile) || tile.Type.Wall || CityCore(tile)) return false;
        if (PureMapMode.Enabled) return MapWallSystem.Add(tile, vanilla.Id);
        if (tile.TopType?.Wall == true || tile.Type.Mountains || tile.Type.Summit || tile.hasBuilding()) return false;
        tile.setTileTypes(tile.MainType, vanilla);
        return true;
    }
#endif

    // 筑关：先吸到最近的城墙格(关门)；筑不成(山上、城心)的往外 PassSearch 格内找筑得成的
#if EC_PENDING
    private static RuralSettlement Fort(Vector2Int at)
    {
        if (!Inside(at)) return null;
        Cell origin = EmpireWorld.Current.CellAt(at.x, at.y);
        if (origin == null) return null;
        for (int radius = 0; radius <= PassSearch; radius++)
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue;
                Cell tile = EmpireWorld.Current.CellAt(at.x + dx, at.y + dy);
                if (tile == null) continue;
                Cell target = WallGateSystem.SnapToWall(tile) ?? tile;
                if (RuralSettlementSystem.At(target.X, target.Y) is RuralSettlement existing) return existing.fort ? existing : null;
                RuralSettlementSystem.Place(target, town: false, fort: true);
                if (RuralSettlementSystem.At(target.X, target.Y) is RuralSettlement made) return made;
            }
        return null;
    }
#endif
}
}
