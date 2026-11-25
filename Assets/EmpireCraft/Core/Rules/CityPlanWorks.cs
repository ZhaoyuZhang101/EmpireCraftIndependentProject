// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CityPlanWorks.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Time = EmpireCraft.Core.FrameClock;
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 城池全图里的真营建(用户 2026-10-28 要编辑器里的改动都是真的：只能在本城领地里画，领土不变；界面见 CityPlanScreen)：
//   · 城墙(问答拍板"城墙变真的：开城墙＝沿城区外沿真筑一圈图层城墙(同神力画的城墙，挡兵、四门是关门)；关掉就拆。护城河、中轴仍只是画法")：
//     玩家在城池全图里开了城墙(plan_walls = 1)、平面图也画着城墙(近代拆成环路的不算)，就沿平面图画城墙的那一圈格
//     (画成本城城区、四邻有一格不是城区的格，同 PureMapPatch.UrbanEdge)真筑图层城墙(MapWallSystem.Add，关掉纯净地图读档后还原成原版城墙)；
//     水、岩浆、山、高峰、厂区(平面图在那里画厂房、不画城墙)上不筑，已有城墙(神力画的)的格不动也不记；
//     城门中线(PlanFacts.GateX/GateY；没有就取这一圈外框的两条中线)与这一圈相交的格是城门：同样筑上墙、记作关门(见 WallGateSystem)——
//     本方与友邦的路、兵从城门过，外人过不去(同关城)。这一圈连同城门楼由平面图画，MapWallOverlay 不再画成长城；
//     筑了哪几格、城门是哪几格记进城的存档(CityExtraData.plan_wall_tiles、plan_gate_tiles)，拆只拆自己筑的；
//     这一圈的指纹也记下(plan_wall_key)：城区、城门中线、城墙开关变了才对账，只拆、筑变了的格，没变不动；
//     地图上隔几秒对一次账(MapWallSystem.Tick 调 Tick；只用已算好的平面图)，城池全图里改完调 SyncWalls 当场对；城没了，它筑的城墙一并拆掉。
//   · 村镇：立村、设镇、筑关、小中大厂区、毁弃，与营建神力完全相同(SettlementPowers.Build、RazeAt)，只多一条：
//     只认本城城郊的地(本城的地、不画成城区的地块；筑关吸到城墙上的，落点也要在本城城郊)，毁弃只认本城的。
//   · 道路(用户 2026-10-28 "道路可以自动链接也可以手动"、"画完村镇和城市后可以一键自动连接")：画路、擦路同道路神力(RoadPaintSystem)，
//     只画本城领地里的格；一键连路(ConnectAll)：从城门往外按格寻路——只走本城的地、不穿城区，山、海、城墙不走，
//     河湖浅水要能架桥的等级(同画路的规矩)；已有的路便宜，平面图画着的出城大路、岔路、小路也便宜(沿着画好的那条走，不另画一条并排的)，
//     路过别处村镇厂区的贵些；本城完好的村、镇、关、厂区各取到城门最省的一条(都从城门出发，几条路在半道并成一条)，
//     画成神力画的路格(并进路网、能走、要养护)；再沿路网规划把本城与相邻各城的干道当场修通(RoadBuildingSystem.ConnectByPower)。
//   · 城池全图的预览(DrawOverPreview)：照路网图层的颜色画修通的干道与神力画的路，照长城的颜色画别的图层城墙。
// 真改了东西都知会城池全图(CityPlanEditing.Touch)。
public static class CityPlanWorks
{
    // 地图上隔几秒对一次城墙的账；城墙登记(各城筑的城墙、城门)至多隔几秒按存档重收一遍
    private const float TickSeconds = 2f, RegistrySeconds = 5f;
    // 一键连路：已有的路、平面图画着的路与小路每格的代价倍数；丘陵、过水(桥)每格的代价；路过别处村镇厂区加价
    private const float RoadCost = 0.35f, TrailCost = 0.5f, HillCost = 2f, BridgeCost = 6f, CrowdCost = 3f;
    // 从城门中线交点往外找城门，最多走几格
    private const int MaxGateWalk = 256;
    private static readonly int[] AroundX = { 1, -1, 0, 0, 1, 1, -1, -1 }, AroundY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    // 各城筑的城墙、城门每变一次加一(WallGateSystem 据此重收城门)
    public static int WallRevision { get; private set; }

    // 各城筑的城墙格、城门格(按各城存档收，见 Registry)
    private static readonly HashSet<int> PlannedWalls = new(), PlannedGateTiles = new();
    private static object _registryWorld;
    private static bool _registryStale = true;
    private static float _registryAt = -1000f;
    private static int _registryHash;
    // 各城上回对账时平面图的指纹(不存档：读档后头一回照常对一遍，城墙没变就不动)
    private static readonly Dictionary<long, (City city, int key)> Seen = new();
    // 筑了城墙的城与它筑的格(城没了，存档跟着没了，凭这份拆它的城墙)
    private static readonly Dictionary<long, (City city, List<int> walls)> Tracked = new();

    // 各城自己筑的那圈城墙(导入剧本时不当长城，见 ScenarioWorldExport.Walls)
    public static HashSet<int> CityWallTiles()
    {
        var tiles = new HashSet<int>();
        foreach ((City _, List<int> walls) in Tracked.Values)
            if (walls != null) tiles.UnionWith(walls);
        return tiles;
    }
    private static float _tickAt = -1000f;
    private static bool _syncing;
    private static string _wallType;
    // 画路一笔刷过许多格：每帧至多知会一次
    private static City _touched;
    private static int _touchedFrame = -1;

    public static void ResetWorldState()
    {
        Seen.Clear();
        Tracked.Clear();
        _registryStale = true;
        _tickAt = -1000f;
        WallRevision++;
    }

    #region 城墙

    // 这一格是城池全图筑的城墙(由城市平面图画，MapWallOverlay 不画成长城)
    internal static bool Planned(int tileId) => Registry() && PlannedWalls.Contains(tileId);

    // 各城城墙上的城门格(WallGateSystem 收作关门)
    internal static IReadOnlyCollection<int> PlannedGates
    {
        get
        {
            Registry();
            return PlannedGateTiles;
        }
    }

    // 按各城的存档重收(自己改了、读档、隔 RegistrySeconds 秒；城没了的不收)；收到的变了 WallRevision 加一。有城墙格返回 true
    private static bool Registry()
    {
        float now = Time.unscaledTime;
        if (!_registryStale && ReferenceEquals(_registryWorld, EmpireWorld.Current) && now >= _registryAt && now - _registryAt < RegistrySeconds)
            return PlannedWalls.Count > 0;
        _registryStale = false;
        _registryWorld = EmpireWorld.Current;
        _registryAt = now;
        PlannedWalls.Clear();
        PlannedGateTiles.Clear();
        if (EmpireWorld.Current?.Cities != null)
            foreach (City city in EmpireWorld.Current.Cities)
            {
                if (city?.Record == null || city.IsGone()) continue;
                CityExtension.CityExtraData extra = city.GetOrCreate();
                if (extra.plan_wall_tiles != null) PlannedWalls.UnionWith(extra.plan_wall_tiles);
                if (extra.plan_gate_tiles != null) PlannedGateTiles.UnionWith(extra.plan_gate_tiles);
            }
        unchecked
        {
            int hash = PlannedWalls.Count * 7919 + PlannedGateTiles.Count;
            foreach (int id in PlannedWalls) hash += Mix(id * 2);
            foreach (int id in PlannedGateTiles) hash += Mix(id * 2 + 1);
            if (hash != _registryHash)
            {
                _registryHash = hash;
                WallRevision++;
            }
        }
        return PlannedWalls.Count > 0;
    }

    // 每帧(MapWallSystem.Tick，读档同步以后)：纯净地图下隔 TickSeconds 秒给开了城墙、筑过城墙的城对一次账；城没了拆它筑的城墙
#if EC_PENDING
    internal static void Tick()
    {
        if (!PureMapMode.Enabled || !MapWallSystem.Ready || EmpireWorld.Current?.Cities == null) return;
        float now = Time.unscaledTime;
        if (now >= _tickAt && now - _tickAt < TickSeconds) return;
        _tickAt = now;
        foreach (City city in EmpireWorld.Current.Cities)
        {
            if (city?.Record == null || city.IsGone()) continue;
            CityExtension.CityExtraData extra = city.GetOrCreate();
            bool records = HasRecords(extra);
            if (extra.plan_walls != 1 && !records) continue;
            if (records && !Tracked.ContainsKey(city.Key)) Track(city, extra);
            Sync(city, false);
        }
        List<long> gone = null;
        foreach (var pair in Tracked)
        {
            City city = pair.Value.city;
            if (city?.Record != null && !city.IsGone() && ReferenceEquals(EmpireWorld.Current.CityByKey(pair.Key), city)) continue;
            (gone ??= new List<long>()).Add(pair.Key);
        }
        if (gone == null) return;
        foreach (long id in gone)
        {
            foreach (int tile in Tracked[id].walls) RemoveWall(tile);
            Tracked.Remove(id);
            Seen.Remove(id);
        }
        _registryStale = true;
        WallRevision++;
    }
#endif

    // 城墙对账(城池全图里改了城区、城墙开关、中轴以后调)：按眼下的平面图现算一圈，与筑下的对一遍，变了的格拆、筑；被毁了的补上
#if EC_PENDING
    public static void SyncWalls(City city) => Sync(city, true);
#endif

    // force：城池全图里点的，平面图没算就现算、指纹没变也对一遍；地图上轮转的只用已算好的平面图，指纹没变不动
#if EC_PENDING
    private static void Sync(City city, bool force)
    {
        if (_syncing || city?.Record == null || city.IsGone() || !PureMapMode.Enabled || !MapWallSystem.Ready) return;
        CityExtension.CityExtraData extra = city.GetOrCreate();
        long cityId = city.Key;
        bool want = extra.plan_walls == 1, records = HasRecords(extra);
        if (!want && !records)
        {
            Seen.Remove(cityId);
            return;
        }
        var ring = new HashSet<int>();
        var gates = new HashSet<int>();
        if (want)
        {
            // 重算一座城的平面图不便宜(还会顺带添村镇)：轮转的等地图上画到这座城时算好再对
            if (!force && PureMapPatch.BuiltZones(city) == null) return;
            PureMapPatch.PlanFacts facts = PureMapPatch.FactsOf(city);
            if (facts == null) return;
            int seen = unchecked(PureMapPatch.StampOf(city) * 31 + (facts.Walled ? 1 : 0) + (facts.WallsDown ? 2 : 0));
            if (!force && Seen.TryGetValue(cityId, out var last) && ReferenceEquals(last.city, city) && last.key == seen) return;
            Seen[cityId] = (city, seen);
            if (facts.Walled && !facts.WallsDown) Ring(city, facts, ring, gates);
        }
        else Seen.Remove(cityId);
        int key = Fingerprint(ring, gates);
        if (!force && ring.Count > 0 && key == extra.plan_wall_key) return;
        _syncing = true;
        try
        {
            var mine = new HashSet<int>(extra.plan_wall_tiles ?? new List<int>());
            bool changed = false;
            // 拆：不在这一圈里的(已经被毁了的只从记录里去掉)
            foreach (int id in new List<int>(mine))
            {
                if (ring.Contains(id)) continue;
                RemoveWall(id);
                mine.Remove(id);
                changed = true;
            }
            // 筑：这一圈里还没有城墙的格(自己筑过、后来被毁的也补上)；神力画的城墙不动、不记
            foreach (int id in ring)
            {
                Cell tile = TileAt(id);
                if (tile == null || MapWallSystem.Contains(tile) || tile.Type?.Wall == true) continue;
                if (MapWallSystem.Add(tile, WallType()))
                {
                    mine.Add(id);
                    changed = true;
                }
                else if (mine.Remove(id)) changed = true;
            }
            bool gatesChanged = !Same(extra.plan_gate_tiles, gates);
            extra.plan_wall_tiles = mine.Count > 0 ? Sorted(mine) : null;
            extra.plan_gate_tiles = gates.Count > 0 ? Sorted(gates) : null;
            extra.plan_wall_key = ring.Count > 0 ? key : 0;
            if (!changed && !gatesChanged) return;
            _registryStale = true;
            WallRevision++;
            Track(city, extra);
            CityPlanEditing.Touch(city);
        }
        finally
        {
            _syncing = false;
        }
    }
#endif

    // 城墙那一圈：画成本城城区、四邻(上下左右)有一格不是城区的格——同平面图画城墙的那一圈(PureMapPatch.UrbanEdge)，真墙与画的重合；
    // 水、岩浆、山、高峰、厂区上不筑。城门：城门中线与这一圈相交的格(平面图在这里画城门楼)；没有中线取这一圈外框的两条中线
#if EC_PENDING
    private static void Ring(City city, PureMapPatch.PlanFacts facts, HashSet<int> ring, HashSet<int> gates)
    {
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        if (built == null || built.Count == 0 || city.Zones == null) return;
        var town = new HashSet<int>();
        foreach (Cell zone in city.Zones)
        {
            if (zone?.Tiles == null || !built.Contains(zone.Index)) continue;
            foreach (Cell tile in zone.Tiles)
                if (tile?.data != null && PureMapPatch.TileInTown(city, tile)) town.Add(tile.Index);
        }
        if (town.Count == 0) return;
        bool In(Cell tile) => tile?.data != null && town.Contains(tile.Index);
        Cell[] tiles = EmpireWorld.Current.Zones;
        var edge = new List<Cell>();
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (int id in town)
        {
            Cell tile = tiles[id];
            if (In(tile.Up) && In(tile.Down) && In(tile.Left) && In(tile.Right)) continue;
            edge.Add(tile);
            minX = Mathf.Min(minX, tile.X);
            minY = Mathf.Min(minY, tile.Y);
            maxX = Mathf.Max(maxX, tile.X);
            maxY = Mathf.Max(maxY, tile.Y);
        }
        int gateX = facts.GateX, gateY = facts.GateY;
        if (gateX == int.MinValue || gateY == int.MinValue)
        {
            gateX = (minX + maxX) / 2;
            gateY = (minY + maxY) / 2;
        }
        foreach (Cell tile in edge)
        {
            TerrainKind type = tile.Type;
            if (type == null || type.Liquid || type.Lava || type.Mountains || type.Block && !type.Wall || FactorySystem.At(tile) != null)
                continue;
            ring.Add(tile.Index);
            if (tile.X == gateX || tile.Y == gateY) gates.Add(tile.Index);
        }
    }
#endif

    // 筑墙用哪种原版城墙(关掉纯净地图读档时还原成它)：有 wall_order 用它，没有取头一种
#if EC_PENDING
    private static string WallType()
    {
        if (_wallType != null) return _wallType;
        foreach (TerrainKind type in AssetManager.top_tiles?.list ?? new List<TerrainKind>())
        {
            if (type?.Wall != true) continue;
            _wallType ??= type.Id;
            if (type.Id != "wall_order") continue;
            _wallType = type.Id;
            break;
        }
        return _wallType ?? "wall_order";
    }

    private static bool RemoveWall(int id)
    {
        Cell tile = TileAt(id);
        return tile != null && MapWallSystem.Contains(tile) && MapWallSystem.Remove(tile);
    }
#endif

    private static void Track(City city, CityExtension.CityExtraData extra)
    {
        if (extra.plan_wall_tiles == null || extra.plan_wall_tiles.Count == 0) Tracked.Remove(city.Key);
        else Tracked[city.Key] = (city, new List<int>(extra.plan_wall_tiles));
    }

    // 这座城在城池全图里筑了城墙(路网找路时把整座城的地算进走廊，见 RoadNetwork.BuildRoad)
    internal static bool Walled(City city) => city?.Record != null && !city.IsGone() && city.GetOrCreate().plan_wall_tiles?.Count > 0;

    private static bool HasRecords(CityExtension.CityExtraData extra) =>
        extra.plan_wall_tiles?.Count > 0 || extra.plan_gate_tiles?.Count > 0;

    // 这一圈的指纹(与格的先后无关；没有城墙为 0)
    private static int Fingerprint(HashSet<int> ring, HashSet<int> gates)
    {
        if (ring.Count == 0) return 0;
        unchecked
        {
            int hash = ring.Count * 7919 + gates.Count;
            foreach (int id in ring) hash += Mix(id * 2 + (gates.Contains(id) ? 1 : 0));
            return hash == 0 ? 1 : hash;
        }
    }

    private static int Mix(int value)
    {
        unchecked
        {
            uint x = (uint)value * 0x9E3779B1u;
            x ^= x >> 15;
            x *= 0x85EBCA6Bu;
            x ^= x >> 13;
            return (int)x;
        }
    }

    private static bool Same(List<int> list, HashSet<int> set)
    {
        if (list == null || list.Count == 0) return set.Count == 0;
        if (list.Count != set.Count) return false;
        foreach (int id in list)
            if (!set.Contains(id)) return false;
        return true;
    }

    private static List<int> Sorted(HashSet<int> set)
    {
        var list = new List<int>(set);
        list.Sort();
        return list;
    }

#if EC_PENDING
    private static Cell TileAt(int id)
    {
        Cell[] tiles = EmpireWorld.Current?.Zones;
        return tiles != null && id >= 0 && id < tiles.Length ? tiles[id] : null;
    }
#endif

    #endregion

    #region 村镇

    // 立村、设镇、筑关、放小中大厂区：kind 为 village、town、fort、factory_small、factory_medium、factory_large；
    // 只认本城城郊的地(本城的地、不在城区)，其余同营建神力；返回本地化提示(认不得的 kind 返回 null)
#if EC_PENDING
    public static string PlaceSettlement(City city, Cell tile, string kind)
    {
        if (!CityPlanEditing.Editable(city, out string reason)) return reason;
        // 筑关点在城墙边会吸到墙上(见 WallGateSystem.SnapToWall)：吸到的落点也要在本城城郊
        Cell at = kind == "fort" ? WallGateSystem.SnapToWall(tile) ?? tile : tile;
        if (!Suburb(city, at)) return LM.Get("city_plan_works_not_suburb");
        int settlements = RuralSettlementSystem.Revision, factories = FactorySystem.Revision;
        string text = SettlementPowers.Build(tile, kind);
        if (RuralSettlementSystem.Revision != settlements || FactorySystem.Revision != factories) Touched(city);
        return text;
    }
#endif

    // 毁弃(同神力：完好的村镇毁为废墟，废墟再点抹去；点的不是村镇就拆厂区)：只认本城的
#if EC_PENDING
    public static string Raze(City city, Cell tile)
    {
        if (!CityPlanEditing.Editable(city, out string reason)) return reason;
        if (tile?.Zone == null) return LM.Get("settlement_none");
        int settlements = RuralSettlementSystem.Revision, factories = FactorySystem.Revision;
        string text = SettlementPowers.RazeAt(tile, city);
        if (RuralSettlementSystem.Revision != settlements || FactorySystem.Revision != factories) Touched(city);
        return text;
    }
#endif

    // 本城城郊：本城的地、不画成城区的地块
#if EC_PENDING
    private static bool Suburb(City city, Cell tile) =>
        tile?.Zone != null && tile.Zone.City == city && !CityPlanEditing.InTown(city, tile.Zone);
#endif

    // 知会城池全图；画路一笔刷过许多格，路不在平面图里，只让预览重画(CityPlanEditing.Bump，不重算平面图)，每帧至多一次
#if EC_PENDING
    private static void Touched(City city, bool oncePerFrame = false)
    {
        if (oncePerFrame && ReferenceEquals(_touched, city) && _touchedFrame == Time.frameCount) return;
        _touched = city;
        _touchedFrame = Time.frameCount;
        if (oncePerFrame) CityPlanEditing.Bump();
        else CityPlanEditing.Touch(city);
    }
#endif

    #endregion

    #region 道路

    // 画路、擦路(同道路神力)：只认本城领地里的格；画上(擦掉)了返回 true
#if EC_PENDING
    public static bool PaintRoad(City city, Cell tile)
    {
        if (!Own(city, tile) || !RoadPaintSystem.Paint(tile)) return false;
        Touched(city, true);
        return true;
    }

    public static bool EraseRoad(City city, Cell tile)
    {
        if (!Own(city, tile) || !RoadPaintSystem.Erase(tile)) return false;
        Touched(city, true);
        return true;
    }
#endif

    private static bool Own(City city, Cell tile) =>
        city?.Record != null && !city.IsGone() && tile?.Zone != null && tile.Zone.City == city;

    // 一键连路：本城完好的村、镇、关、厂区连到城门，再把本城与相邻各城的干道修通；返回本地化提示
#if EC_PENDING
    public static string ConnectAll(City city)
    {
        if (!CityPlanEditing.Editable(city, out string reason)) return reason;
        var parts = new List<string>();
        List<(int x, int y, int reach)> ends = Ends(city);
        int painted = 0;
        if (ends.Count == 0) parts.Add(LM.Get("city_plan_connect_none"));
        else
        {
            (int linked, int failed, int laid) = LinkToGates(city, ends);
            painted = laid;
            parts.Add(string.Format(LM.Get("city_plan_connect_done"), linked, laid));
            if (failed > 0) parts.Add(string.Format(LM.Get("city_plan_connect_failed"), failed));
        }
        // 邻城：路网规划里与本城相连的城，沿规划好的干道当场修通(同连路神力)
        int tried = 0, reached = 0, built = 0;
        foreach (City other in Neighbours(city))
        {
            tried++;
            if (RoadBuildingSystem.ConnectByPower(city, other, out int made) < 0) continue;
            reached++;
            built += made;
        }
        if (reached > 0) parts.Add(string.Format(LM.Get("city_plan_connect_neighbours"), reached, built));
        if (!RoadNetwork.PlansComplete && (tried == 0 || reached < tried)) parts.Add(LM.Get("city_plan_connect_planning"));
        else if (reached == 0) parts.Add(LM.Get("city_plan_connect_no_neighbours"));
        if (painted > 0 || built > 0)
        {
            // 新画的路格当场并进路网、重画路网图层(不等画路的半秒节流)
            RoadNetwork.RefreshNow();
            RoadPaintSystem.RequestRedraw();
            Touched(city);
        }
        return string.Join(LM.Get("city_plan_connect_separator"), parts);
    }
#endif

    // 要连的：本城地上完好的村、镇、关(废墟、被城区盖住没画出来的不连)与厂区；reach：占地的半边长(路接到它的边上)
#if EC_PENDING
    private static List<(int x, int y, int reach)> Ends(City city)
    {
        var ends = new List<(int x, int y, int reach)>();
        PureMapPatch.FactsOf(city);
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        Cell center = city.SeatCell()?.Zone;
        foreach (RuralSettlement settlement in RuralSettlementSystem.InCity(city))
        {
            if (settlement == null || settlement.cleared || settlement.ruined_at >= 0d) continue;
            Cell zone = EmpireWorld.Current.CellAt(settlement.x, settlement.y)?.Zone;
            if (zone?.City != city || zone == center || built?.Contains(zone.Index) == true) continue;
            int reach = settlement.fort ? RuralSettlementSystem.FortReach + 1 : settlement.town ? Mathf.Max(2, settlement.size) + 1 : 2;
            ends.Add((settlement.x, settlement.y, reach));
        }
        // 厂区：中心落在城区里的不连(城里有街)
        foreach (FactorySite site in FactorySystem.InCity(city))
        {
            Cell zone = EmpireWorld.Current.CellAt(site.x, site.y)?.Zone;
            if (zone == null || zone == center || CityPlanEditing.InTown(city, zone)) continue;
            (int x0, int y0, int side) = FactorySystem.Footprint(site);
            ends.Add((x0 + side / 2, y0 + side / 2, side / 2 + 1));
        }
        return ends;
    }
#endif

    // 相邻的城：路网规划里与本城相连的(修通了的、在修的也算)
    private static List<City> Neighbours(City city)
    {
        long id = city.Key;
        var ids = new HashSet<long>();
        void Collect((long, long) key)
        {
            if (key.Item1 == id) ids.Add(key.Item2);
            else if (key.Item2 == id) ids.Add(key.Item1);
        }
        foreach ((long, long) key in RoadNetwork.Plans.Keys) Collect(key);
        foreach ((long, long) key in RoadBuildingSystem.All.Keys) Collect(key);
        var result = new List<City>();
        foreach (long other in ids)
        {
            City neighbour = EmpireWorld.Current.CityByKey(other);
            if (neighbour?.Record != null && !neighbour.IsGone() && neighbour != city) result.Add(neighbour);
        }
        return result;
    }

    // 从各城门一齐往外按格寻路(八方向，斜着走乘 1.41，斜接的城墙缝钻不过去)，每处村镇厂区取占地里到城门最省的一格，
    // 顺着来路画到城门。返回连上了几处、几处走不通、新画了几格
#if EC_PENDING
    private static (int linked, int failed, int painted) LinkToGates(City city, List<(int x, int y, int reach)> ends)
    {
        PureMapPatch.PlanFacts facts = PureMapPatch.FactsOf(city);
        HashSet<int> built = PureMapPatch.BuiltZones(city);
        int size = Land.ZoneSize;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Cell zone in city.Zones)
        {
            if (zone == null) continue;
            minX = Mathf.Min(minX, zone.X * size);
            minY = Mathf.Min(minY, zone.Y * size);
            maxX = Mathf.Max(maxX, zone.X * size + size - 1);
            maxY = Mathf.Max(maxY, zone.Y * size + size - 1);
        }
        if (minX == int.MaxValue) return (0, ends.Count, 0);
        int w = maxX - minX + 1, h = maxY - minY + 1;
        int Index(int x, int y) => x < minX || y < minY || x > maxX || y > maxY ? -1 : (y - minY) * w + (x - minX);
        bool Town(Cell tile) =>
            built != null && tile?.Zone != null && built.Contains(tile.Zone.Index) && PureMapPatch.TileInTown(city, tile);
        List<Cell> sources = Gates(city, facts, Town);
        if (sources.Count == 0) return (0, ends.Count, 0);
        HashSet<int> trails = DrawnPaths(city);
        IReadOnlyDictionary<int, int> network = RoadNetwork.RoadTileGrades;
        IReadOnlyDictionary<int, PaintedRoad> brushed = RoadPaintSystem.Tiles;
        bool Road(int id) => network.ContainsKey(id) || brushed.ContainsKey(id);
        // 村镇厂区占的地：路从旁边绕，不从别处的村子当中穿过
        var crowded = new bool[w * h];
        foreach ((int x, int y, int reach) in ends)
            for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int i = Index(x + dx, y + dy);
                if (i >= 0) crowded[i] = true;
            }
        // 走进一格的代价(-1 走不了)：本城的地、不在城区(城门是起点)；已有的路便宜，没有路的要画得上(同画路的规矩)
        var steps = new float[w * h];
        for (int i = 0; i < steps.Length; i++) steps[i] = float.NaN;
        float Step(int i)
        {
            if (!float.IsNaN(steps[i])) return steps[i];
            Cell tile = EmpireWorld.Current.CellAt(minX + i % w, minY + i / w);
            float cost;
            if (tile?.data == null || tile.Type == null || tile.Zone?.City != city || Town(tile)) cost = -1f;
            else
            {
                int id = tile.Index;
                bool road = Road(id);
                if (road ? MapWallSystem.Blocks(tile) || tile.Type.Mountains || tile.Type.Lava : RoadPaintSystem.GradeAt(tile) <= 0) cost = -1f;
                else
                {
                    cost = tile.Type.Liquid ? BridgeCost : tile.Type.Rocks ? HillCost : 1f;
                    if (road) cost *= RoadCost;
                    else if (trails.Contains(id)) cost *= TrailCost;
                    if (crowded[i]) cost *= CrowdCost;
                }
            }
            return steps[i] = cost;
        }
        var total = new float[w * h];
        var from = new int[w * h];
        for (int i = 0; i < total.Length; i++)
        {
            total[i] = float.MaxValue;
            from[i] = -1;
        }
        var open = new List<(float cost, int index)>();
        foreach (Cell source in sources)
        {
            int i = Index(source.X, source.Y);
            if (i < 0 || total[i] <= 0f) continue;
            total[i] = 0f;
            Push(open, (0f, i));
        }
        while (open.Count > 0)
        {
            (float here, int index) = Pop(open);
            if (here > total[index]) continue;
            int cx = minX + index % w, cy = minY + index / w;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + AroundX[k], ny = cy + AroundY[k], next = Index(nx, ny);
                if (next < 0) continue;
                float step = Step(next);
                if (step < 0f || k >= 4 && MapWallSystem.CutsCorner(cx, cy, nx, ny)) continue;
                float cost = here + step * (k >= 4 ? 1.41f : 1f);
                if (cost >= total[next]) continue;
                total[next] = cost;
                from[next] = index;
                Push(open, (cost, next));
            }
        }
        int linked = 0, failed = 0, painted = 0;
        foreach ((int x, int y, int reach) in ends)
        {
            int best = -1;
            float bestCost = float.MaxValue;
            for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int i = Index(x + dx, y + dy);
                if (i < 0 || total[i] >= bestCost) continue;
                bestCost = total[i];
                best = i;
            }
            if (best < 0)
            {
                failed++;
                continue;
            }
            linked++;
            for (int i = best; i >= 0; i = from[i])
            {
                Cell tile = EmpireWorld.Current.CellAt(minX + i % w, minY + i / w);
                if (tile?.data == null || Road(tile.Index)) continue;
                if (RoadPaintSystem.Paint(tile)) painted++;
            }
        }
        return (linked, failed, painted);
    }
#endif

    // 城门：从城门中线交点(没有就取城心)往东北西南四个方向走，城区的最后一格(筑了城墙就是城门)，连同本城记下的城门格；
    // 交点不在城区里(原始聚落等)就从交点出发
#if EC_PENDING
    private static List<Cell> Gates(City city, PureMapPatch.PlanFacts facts, Func<Cell, bool> town)
    {
        var result = new List<Cell>();
        int gx = facts?.GateX ?? int.MinValue, gy = facts?.GateY ?? int.MinValue;
        if (gx == int.MinValue || gy == int.MinValue)
        {
            Cell center = city.SeatCell();
            if (center == null) return result;
            gx = center.X;
            gy = center.Y;
        }
        Cell start = EmpireWorld.Current.CellAt(gx, gy);
        if (start == null) return result;
        if (!town(start))
        {
            result.Add(start);
            return result;
        }
        for (int k = 0; k < 4; k++)
        {
            Cell last = start;
            for (int step = 1; step <= MaxGateWalk; step++)
            {
                Cell next = EmpireWorld.Current.CellAt(gx + AroundX[k] * step, gy + AroundY[k] * step);
                if (next == null || !town(next)) break;
                last = next;
            }
            if (!result.Contains(last)) result.Add(last);
        }
        foreach (int id in city.GetOrCreate().plan_gate_tiles ?? new List<int>())
            if (TileAt(id) is Cell gate && gate.Zone?.City == city && !result.Contains(gate)) result.Add(gate);
        return result;
    }
#endif

    // 平面图画着的城外的路(出城大路、岔路、官道)与小路经过的格：一键连路沿着它们走(只用已算好的平面图，见 PureMapPatch.FabricOf)
#if EC_PENDING
    private static HashSet<int> DrawnPaths(City city)
    {
        var tiles = new HashSet<int>();
        UrbanFabric.Geometry g = PureMapPatch.FabricOf(city);
        if (g == null) return tiles;
        void Mark(int x, int y)
        {
            if (EmpireWorld.Current.CellAt(x, y)?.data is WorldTileData data) tiles.Add(data.tile_id);
        }
        foreach (UrbanFabric.Road road in g.Roads)
            for (int d = 0; d <= road.Length; d++)
            {
                (int x, int y) = UrbanFabric.RoadPoint(road, d);
                Mark(x, y);
            }
        foreach (Vector2[] path in g.Paths)
            for (int i = 0; i + 1 < path.Length; i++)
            {
                Vector2 a = path[i], b = path[i + 1];
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 2f));
                for (int k = 0; k <= count; k++)
                {
                    Vector2 p = Vector2.Lerp(a, b, k / (float)count);
                    Mark(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
                }
            }
        return tiles;
    }
#endif

    private static void Push(List<(float cost, int index)> heap, (float cost, int index) item)
    {
        heap.Add(item);
        for (int c = heap.Count - 1; c > 0;)
        {
            int p = (c - 1) / 2;
            if (heap[p].cost <= heap[c].cost) break;
            (heap[p], heap[c]) = (heap[c], heap[p]);
            c = p;
        }
    }

    private static (float cost, int index) Pop(List<(float cost, int index)> heap)
    {
        (float cost, int index) top = heap[0];
        heap[0] = heap[heap.Count - 1];
        heap.RemoveAt(heap.Count - 1);
        for (int c = 0;;)
        {
            int l = c * 2 + 1, r = l + 1, m = c;
            if (l < heap.Count && heap[l].cost < heap[m].cost) m = l;
            if (r < heap.Count && heap[r].cost < heap[m].cost) m = r;
            if (m == c) break;
            (heap[m], heap[c]) = (heap[c], heap[m]);
            c = m;
        }
        return top;
    }

    #endregion

    #region 预览

    // 城池全图的预览上画真路与图层城墙：pixels 是 w×h 格、每格 scale×scale 像素，第 0 行是 y0。
    //   · 路：修通的干道与神力画的路(还没并进路网的也算)，按等级照路网图层的颜色(驿道以上描一道深色的边)；画成城区的格不画(同 RoadOverlay)；
    //   · 城墙：图层城墙照长城的墙身颜色；城池全图筑的那一圈平面图已经画了、关城压着的让给关城(同 MapWallOverlay)，不画。
    // 只画落在范围里的格
#if EC_PENDING
    public static void DrawOverPreview(Color32[] pixels, int x0, int y0, int w, int h, int scale)
    {
        Cell[] tiles = EmpireWorld.Current?.Zones;
        if (pixels == null || tiles == null || w <= 0 || h <= 0 || scale <= 0 || pixels.Length < w * h * scale * scale) return;
        int pw = w * scale;
        bool InRange(Cell tile) => tile != null && tile.X >= x0 && tile.Y >= y0 && tile.X < x0 + w && tile.Y < y0 + h;
        var grades = new Dictionary<int, int>();
        foreach (var pair in RoadNetwork.RoadTileGrades)
            if (InRange(TileAt(pair.Key))) grades[pair.Key] = pair.Value;
        foreach (var pair in RoadPaintSystem.Tiles)
        {
            if (pair.Value == null || pair.Value.grade <= 0 || !InRange(TileAt(pair.Key))) continue;
            grades[pair.Key] = grades.TryGetValue(pair.Key, out int had) ? Mathf.Max(had, pair.Value.grade) : pair.Value.grade;
        }
        var roads = new List<(Cell tile, int grade)>();
        foreach (var pair in grades)
        {
            Cell tile = tiles[pair.Key];
            if (tile.Zone == null || PureMapPatch.UrbanTile(tile)) continue;
            roads.Add((tile, pair.Value));
        }
        // 描边：驿道以上路两侧不是路的格
        var cased = new HashSet<int>();
        foreach ((Cell tile, int grade) in roads)
        {
            if (grade < 2) continue;
            foreach (Cell next in tile.Neighbours ?? Array.Empty<Cell>())
            {
                if (next?.data == null || next.Zone == null || !InRange(next) || grades.ContainsKey(next.Index) ||
                    PureMapPatch.UrbanTile(next) || !cased.Add(next.Index)) continue;
                Blend(pixels, pw, next.X - x0, next.Y - y0, scale, RoadOverlay.CasingColor);
            }
        }
        foreach ((Cell tile, int grade) in roads) Blend(pixels, pw, tile.X - x0, tile.Y - y0, scale, RoadOverlay.GradeColor(grade));
        foreach (int id in MapWallSystem.Walls.Keys)
        {
            Cell tile = TileAt(id);
            if (!InRange(tile) || Planned(id) || WallGateSystem.Covered(id)) continue;
            Blend(pixels, pw, tile.X - x0, tile.Y - y0, scale, MapWallOverlay.StoneAt(tile.X, tile.Y));
        }
    }
#endif

    // 一格(scale×scale 像素)按颜色的不透明度盖上去
#if EC_PENDING
    private static void Blend(Color32[] pixels, int pw, int tx, int ty, int scale, Color32 color)
    {
        float t = color.a / 255f;
        for (int sy = 0; sy < scale; sy++)
        {
            int row = (ty * scale + sy) * pw + tx * scale;
            for (int sx = 0; sx < scale; sx++)
            {
                int i = row + sx;
                if (i < 0 || i >= pixels.Length) continue;
                Color32 under = pixels[i];
                pixels[i] = new Color32((byte)(under.r + (color.r - under.r) * t), (byte)(under.g + (color.g - under.g) * t),
                    (byte)(under.b + (color.b - under.b) * t), 255);
            }
        }
    }
#endif

    #endregion
}
}
