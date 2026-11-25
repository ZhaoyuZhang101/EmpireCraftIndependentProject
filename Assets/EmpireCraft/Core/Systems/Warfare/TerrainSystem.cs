// Generated from the EmpireCraft mod: Scripts/GeneralSystems/Warfare/TerrainSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 战线地形(借鉴钢铁雄心)：每块地块按格子构成定一种地形(格子改了当场重判，否则 4~6 年重判一次，见 Lookup)。
// 交战时看攻方要推进的那块地：攻方战力打折、双方额外掉组织度(补给难)、每月最多推进几块；
// 山地、沼泽、丛林行军慢一半。城中心那块地算城池。
// 不可通行的地块(军团绕行，战线不从这里推进)：原版最高的地形(山地、山巅是阻挡格，地面单位走不了)占到四分之三，
// 或者山以外的地(平地、丘陵、水面)在地块里被山隔成了几片、或通不到两条边(用户 2026-10-26 "山地军队不应该通行"，
// 问答选"按实际走不走得通"：山脊拦腰切断的过不去，有山口、谷地连成一片的照旧能过，同原版小人翻不过山)；
// 不到 4 格的山间小块不算一片。本地中国图上不可通行的地块由 166 块增到 310 块。城心那一块总能进。
// 路线经过的地块、跨过的交界算走得通(用户 2026-11-04 转群友"能走正常小人的，能保证从 A 城的主城走到 B 城的便可以走军团"，见 RoadNetwork.IsTrail、Spans)：
// 路线是按格找出来的，山脊上一格宽的缺口小人也走得过去；城市扩张、剧本铺辖地(seatOpen 为假)不认这条。
// 同一份缓存还记着地块里的陆地格数与旱地格数(水土用，见 ShuiTuSystem、DryCounts)：旱地按 LandKinds.Dry(荒漠、稀树草原、荒原)，
// 陆地不含水、岩浆——同毡帐村镇的口径(RuralSettlementSystem.YurtGround)；这里的"沙漠"地形连海滩沙地也算，水土不用它。
// 高原(用户 2026-11-10，模组的地面格，见 PlateauTerrain)：高寒、黄土高原格(连同冻上、修了路开了田的，看底下的地面)占四成算高原地形
// (山地照旧先判)：攻方打八折、攻方每月多掉 3 组织度(高原反应)、每月至多推进两块；草原高原面是开阔草原，不算。
// 高寒高原格算旱地(牧地，见 LandKinds)、宜居三成、不能耕；黄土高原宜居七成、能耕(收成打八折)；草原高原算旱地、宜居五成、
// 能少量耕(收成五折，见 PlateauArable 与 FarmlandSystem)；高原格不算纯沙漠纯冻土。
// 高原哪一边(PlateauSide)：哪种高原格占陆地过半就是那一种，一成五以下是平地，其间是外缘(两边都算)，外缘作界线用(见 PlateauSides)
public enum FrontTerrain { Plains, Forest, Jungle, Hills, Mountain, River, Swamp, Desert, Snow, Urban, Plateau }

public readonly struct TerrainEffect
{
    public readonly float Attack, AttackerOrg, DefenderOrg;
    public readonly int MaxAdvance;
    public readonly bool SlowMarch;

    public TerrainEffect(float attack, float attackerOrg, float defenderOrg, int maxAdvance, bool slowMarch)
    {
        Attack = attack;
        AttackerOrg = attackerOrg;
        DefenderOrg = defenderOrg;
        MaxAdvance = maxAdvance;
        SlowMarch = slowMarch;
    }
}

public static class TerrainSystem
{
    // 攻方战力倍率、攻方/守方每月额外掉的组织度、每月最多推进几块、行军是否减半
    private static readonly Dictionary<FrontTerrain, TerrainEffect> Effects = new()
    {
        [FrontTerrain.Plains] = new TerrainEffect(1f, 0f, 0f, 4, false),
        [FrontTerrain.Forest] = new TerrainEffect(0.85f, 0f, 0f, 3, false),
        [FrontTerrain.Jungle] = new TerrainEffect(0.75f, 3f, 2f, 2, true),
        [FrontTerrain.Hills] = new TerrainEffect(0.8f, 0f, 0f, 3, false),
        [FrontTerrain.Mountain] = new TerrainEffect(0.6f, 4f, 0f, 2, true),
        [FrontTerrain.River] = new TerrainEffect(0.75f, 0f, 0f, 3, false),
        [FrontTerrain.Swamp] = new TerrainEffect(0.7f, 4f, 4f, 2, true),
        [FrontTerrain.Desert] = new TerrainEffect(0.95f, 5f, 5f, 4, false),
        [FrontTerrain.Snow] = new TerrainEffect(0.85f, 5f, 5f, 3, false),
        [FrontTerrain.Urban] = new TerrainEffect(0.7f, 0f, 0f, 2, false),
        [FrontTerrain.Plateau] = new TerrainEffect(0.8f, 3f, 0f, 2, false)
    };

    // 地块地形缓存(按地块编号)：以前一律一年过期，又和行政控制力的年度结算同步，到期那一刻一国的地块一起重判。
    // 现在格子改了(设地面格、顶层格，见 MapWallPatch)当场作废；没改的只为树木长、砍重判，4~6 年一次，按地块编号错开到期
    private struct Entry
    {
        public double At;
        public FrontTerrain Terrain;
        public bool Impassable, Known;
        // 陆地格数、旱地格数(一块地 64 格)；宜居格(按地面加权 × LivableScale)、可耕格；纯沙漠 1、纯冻土 2(见 BarrenKind)；丛林格数；阻挡格数
        public byte Land, Dry, Livable, Arable, Barren, Jungle, Rock;
        // 高寒高原格数、黄土高原与草原高原上的可耕格数；高原哪一边(0 平地，1 高寒，2 黄土，3 草原，255 外缘)
        public byte Alpine, LoessArable, SteppeArable, Side;
    }

    private const int RefreshMonths = 48, RefreshSpreadMonths = 24;
    private static Entry[] _cache = new Entry[0];

    public static void ResetWorldState()
    {
        Edges.Clear();
        MountainZones.Clear();
        RiverZones.Clear();
        _cache = new Entry[0];
        CityLivable.Clear();
        _marks = new int[0];
        _markTypes = new TerrainKind[0];
    }

    // 地块里有格子改了：下次用到时重判
    public static void Invalidate(Cell zone)
    {
        if (zone != null && zone.Index >= 0 && zone.Index < _cache.Length) _cache[zone.Index].Known = false;
        // 交界的路口跟着重判(地形难得改，整表清掉)
        Edges.Clear();
        // 山地、河上只看本块的格子：只清这一块(草木蔓生时时改格子，整表清掉等于没缓存，城市扩张每看一块地都要重数四邻的格子)
        if (zone == null)
        {
            MountainZones.Clear();
            RiverZones.Clear();
            return;
        }
        MountainZones.Remove(zone.Index);
        RiverZones.Remove(zone.Index);
    }

    // 地面格水陆变了(挖河、填海)：水体大小跟着变，别处的地块算不算河上也可能变，整表清掉(见 MapWallPatch.AfterSetTileType)
#if EC_PENDING
    public static void GroundChanged(TerrainKind before, TerrainKind after)
    {
        bool wasWater = before != null && (Marks(before) & MarkWater) != 0;
        bool isWater = after != null && (Marks(after) & MarkWater) != 0;
        if (wasWater != isWater) RiverZones.Clear();
    }
#endif

    // 河上的地块(城市扩张不越过大河，见 CityExpansionPatch.Bordering)：水面占一成二以上，且水都是小水体。
    // 世界地图上的河每 14 格留渡口(见 EarthTerrain.RiverTiles)，一段段只有几十格；海、大湖连成一大片，
    // 沿海、湖边的地块不算(FrontTerrain.River 把这些也算进去了，那里照常沿岸领地)。原版随机地图的长河连成一片，也不算
    private const float RiverShare = 0.12f;
    private const int BigWater = 160;
    private static readonly Dictionary<int, bool> RiverZones = new();

#if EC_PENDING
    public static bool RiverZone(Cell zone)
    {
        if (zone?.Tiles == null) return false;
        if (RiverZones.TryGetValue(zone.Index, out bool cached)) return cached;
        int total = 0, water = 0;
        bool big = false;
        foreach (Cell tile in zone.Tiles)
        {
            TerrainKind type = tile?.Type;
            if (type == null) continue;
            total++;
            if ((Marks(type) & MarkWater) == 0) continue;
            water++;
            if (!big && (tile.region?.island?.getTileCount() ?? 0) >= BigWater) big = true;
        }
        bool result = !big && total > 0 && water >= total * RiverShare;
        if (RiverZones.Count > 100_000) RiverZones.Clear();
        RiverZones[zone.Index] = result;
        return result;
    }
#endif

    // 山地区块：原版山地、山巅格占两成以上(同 WorldMapZones.Classify 的 mountainous)。城市扩张进这种地块要更严(见 CityExpansionPatch.Bordering)
    private const float MountainShare = 0.2f;
    private static readonly Dictionary<int, bool> MountainZones = new();

#if EC_PENDING
    public static bool Mountainous(Cell zone)
    {
        if (zone?.Tiles == null) return false;
        if (MountainZones.TryGetValue(zone.Index, out bool cached)) return cached;
        int total = 0, mountain = 0;
        foreach (Cell tile in zone.Tiles)
        {
            TerrainKind type = tile?.Type;
            if (type == null) continue;
            total++;
            int marks = Marks(type) | (tile.MainType != null ? Marks(tile.MainType) : 0);
            if ((marks & MarkMountain) != 0) mountain++;
        }
        bool result = total > 0 && mountain >= total * MountainShare;
        if (MountainZones.Count > 100_000) MountainZones.Clear();
        MountainZones[zone.Index] = result;
        return result;
    }
#endif

    // 两块相邻的地之间走不走得过去(用户 10-29 "军队不应该跨越"山、"不仅国土跨越了军队也跨越了")：
    // 以前只看一块地本身(山占四分之三、或里面被山隔断)，两块都能走、交界却是一道山脊时，军团照样翻过去、战线照样推过去。
    // 现在看交界：两边贴着交界的格子至少有 MinPass 对都不是阻挡格(原版山地、山巅，同 Impassable)才算有路口；
    // 斜着走要经过旁边一块能走、两段交界都有路口的地，不从两道山的夹角钻过去。城心那一块总能进(同 Impassable)；
    // seatOpen 为假时城心也照交界算(剧本开局铺辖地用，见 WorldMapZones：紧挨城心的一圈也不隔脊领地)
    private const int MinPass = 2;
    private static readonly Dictionary<long, bool> Edges = new();
    // 上限按地图大小：每块地 8 条交界(四条直、四条斜，两边共用一半)，城心另记一份。以前固定 20 万，第 2 档大图(4.7 万块)
    // 交界就有近 19 万条，稍多一点就整表清掉重算，战线段地形层一轮要多花几倍(2026-11-14 实测)
    public static int EdgeFlushes;
    private static int EdgeCap() => Math.Max(200_000, (EmpireWorld.Current?.Zones?.All?.Count ?? 0) * 10);

#if EC_PENDING
    public static bool Crossable(Cell a, Cell b, bool seatOpen = true)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        int dx = b.X - a.X, dy = b.Y - a.Y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return true;
        if (seatOpen && (Seat(a) || Seat(b) || RoadNetwork.Spans(a, b))) return true;
        // 不看城心时，城心那块的交界另记一份(与总能进的那份分开)
        bool seat = !seatOpen && (Seat(a) || Seat(b));
        return EdgeOpen(a, b, dx, dy, seatOpen, seat);
    }
#endif

    // 只看地形的 Crossable(seatOpen 为真)：城心随占领挪动、路线随路网改，多核快照另按当时的补上(见 FrontSystem.Parallel)。
    // Crossable(a, b) = 有一头是城心 || 路线跨过这条交界(RoadNetwork.Spans) || CrossableTerrain(a, b)
#if EC_PENDING
    internal static bool CrossableTerrain(Cell a, Cell b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        int dx = b.X - a.X, dy = b.Y - a.Y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return true;
        return EdgeOpen(a, b, dx, dy, true, false);
    }
#endif

    // 只看地形的 Impassable(同上)：Impassable(zone) = ImpassableTerrain(zone) && 不是城心 && 不是路线(RoadNetwork.IsTrail)
#if EC_PENDING
    internal static bool ImpassableTerrain(Cell zone) => zone != null && Lookup(zone).Impassable;
#endif

    // 交界查表(没有就按格子算出来记下)
#if EC_PENDING
    private static bool EdgeOpen(Cell a, Cell b, int dx, int dy, bool seatOpen, bool seat)
    {
        long key = (a.Index < b.Index ? (long)a.Index * 1_000_000L + b.Index : (long)b.Index * 1_000_000L + a.Index) * (seat ? -1L : 1L);
        if (Edges.TryGetValue(key, out bool cached)) return cached;
        bool open;
        if (dx != 0 && dy != 0)
        {
            ZoneGrid zones = EmpireWorld.Current.Zones;
            bool Via(Cell side) => side != null && !Impassable(side) && Crossable(a, side, seatOpen) && Crossable(side, b, seatOpen);
            open = Via(zones.Get(a.X + dx, a.Y)) || Via(zones.Get(a.X, a.Y + dy));
        }
        else open = OpenPairs(a, dx, dy) >= MinPass;
        if (Edges.Count > EdgeCap()) { EdgeFlushes++; Edges.Clear(); }
        Edges[key] = open;
        return open;
    }
#endif

    // a 朝 (dx, dy) 那一边的交界上，两边都不是阻挡格的格子对数
#if EC_PENDING
    private static int OpenPairs(Cell a, int dx, int dy)
    {
        int size = Land.ZoneSize, ox = a.X * size, oy = a.Y * size, count = 0;
        for (int i = 0; i < size; i++)
        {
            int x = dx > 0 ? ox + size - 1 : dx < 0 ? ox : ox + i;
            int y = dy > 0 ? oy + size - 1 : dy < 0 ? oy : oy + i;
            if (!Blocks(EmpireWorld.Current.CellAt(x, y)) && !Blocks(EmpireWorld.Current.CellAt(x + dx, y + dy))) count++;
        }
        return count;
    }
#endif

    // 军牌在两块地之间按格走一步的代价(见 CorpsSystem.MarchLine；只管画在哪，行军快慢照旧按地块算)：
    // 修通的路最省(桥也是路，过得了河)；平地 1；林地、丛林 1.4；丘陵、沼泽 2；山地、山巅、水面、岩浆走不了，返回负数
#if EC_PENDING
    internal static float StepCost(Cell tile)
    {
        TerrainKind type = tile?.Type;
        if (type == null) return -1f;
        if (RoadNetwork.IsRoadTile(tile)) return 0.5f;
        if (type.Lava) return -1f;
        int marks = Marks(type);
        if ((marks & MarkWater) != 0) return -1f;
        int under = tile.MainType != null ? Marks(tile.MainType) : 0;
        if (((marks | under) & MarkBlock) != 0) return -1f;
        if ((marks & (MarkHills | MarkSnowHills | MarkSwamp)) != 0 || (under & MarkHills) != 0) return 2f;
        if ((marks & (MarkJungle | MarkWoods)) != 0) return 1.4f;
        return 1f;
    }
#endif

    // 一格挡不挡路：原版的阻挡格(山地、山巅)，看地面层与顶层(同 ClassifyTiles)
#if EC_PENDING
    private static bool Blocks(Cell tile)
    {
        TerrainKind type = tile?.Type;
        if (type == null) return true;
        int marks = Marks(type);
        if ((marks & MarkWater) != 0) return false;
        int under = tile.MainType != null ? Marks(tile.MainType) : 0;
        return ((marks | under) & MarkBlock) != 0;
    }
#endif

    public static TerrainEffect Effect(FrontTerrain terrain) =>
        Effects.TryGetValue(terrain, out TerrainEffect effect) ? effect : Effects[FrontTerrain.Plains];

#if EC_PENDING
    public static FrontTerrain Of(Cell zone) => Lookup(zone).Terrain;
#endif

    // 地形与能否通行一次取出(地形阻力两样都要，免得查两遍缓存)
#if EC_PENDING
    public static FrontTerrain Of(Cell zone, out bool impassable)
    {
        Entry entry = Lookup(zone);
        impassable = zone != null && entry.Impassable && !Seat(zone);
        return entry.Terrain;
    }
#endif

    // 不可通行(见类头)：军团绕行、战线不从这里推进；城心那一块总能进，路线经过的地块也能进(见 RoadNetwork.IsTrail)
#if EC_PENDING
    public static bool Impassable(Cell zone) => zone != null && Lookup(zone).Impassable && !Seat(zone) && !RoadNetwork.IsTrail(zone);
#endif

    // 只看地形的不可通行(路线找路用：不认路线自己，免得走过一回的绝路下回就算通了)
#if EC_PENDING
    public static bool ImpassableGround(Cell zone) => zone != null && Lookup(zone).Impassable && !Seat(zone);
#endif

    // 山脉无人区里其实是丘陵的地块(用户 2026-11-04 转群友"还有丘陵能走军团，哪怕他是无人区里的"，问答定"山格不到四分之一")：
    // 阻挡格不到陆地的四分之一，又没被山脊隔断(见 WildernessPassage.Blocking)
    private const float HillyRockShare = 0.25f;

#if EC_PENDING
    public static bool Hilly(Cell zone)
    {
        if (zone == null) return false;
        Entry entry = Lookup(zone);
        return !entry.Impassable && entry.Rock < entry.Land * HillyRockShare;
    }
#endif

    private static bool Seat(Cell zone) => zone.City != null && zone.City.SeatCell()?.Zone == zone;

    // 地块里的陆地格数与旱地格数(水土，见 ShuiTuSystem)
#if EC_PENDING
    public static (int land, int dry) DryCounts(Cell zone)
    {
        if (zone == null) return (0, 0);
        Entry entry = Lookup(zone);
        return (entry.Land, entry.Dry);
    }
#endif

    // 旱地地块：陆地里一半以上是旱地
#if EC_PENDING
    public static bool IsDry(Cell zone)
    {
        (int land, int dry) = DryCounts(zone);
        return land > 0 && dry * 2 > land;
    }
#endif

    // 宜居度(0~1，用户 2026-10-22 见荒漠帐比中原城人多："设置的地块大是因为他是沙漠和荒原，实际居住面积应该很小")：
    // 一块地 64 格按地面加权(见 LivableWeight)。民居上限按它折算(见 NoCommonersPatch.AfterRecalculateMaxHouses)
    // 纯沙漠、纯冻土(城心那块除外)住不了人，为零(见 BarrenLandSystem)
    // 雨林全开发(见 WildernessSystem.JungleDeveloped)：丛林格按草地住人，这里现算不进缓存，开关一改就生效，剧本不受影响
#if EC_PENDING
    public static float Livability(Cell zone)
    {
        if (zone == null || BarrenLandSystem.Barren(zone)) return 0f;
        Entry entry = Lookup(zone);
        float livable = entry.Livable + (WildernessSystem.JungleDeveloped ? entry.Jungle * JungleLivableGain : 0f);
        return Math.Min(1f, livable / (64f * LivableScale));
    }
#endif

    // 地块里的陆地格数与其中的高寒高原格数
#if EC_PENDING
    public static (int land, int alpine) AlpineCounts(Cell zone)
    {
        if (zone == null) return (0, 0);
        Entry entry = Lookup(zone);
        return (entry.Land, entry.Alpine);
    }
#endif

    // 可耕格里有几格在黄土高原、草原高原上(收成各打八折、五折，见 FarmlandSystem)
#if EC_PENDING
    public static (int loess, int steppe) PlateauArable(Cell zone)
    {
        if (zone == null) return (0, 0);
        Entry entry = Lookup(zone);
        return (entry.LoessArable, entry.SteppeArable);
    }
#endif

    public const byte LowlandSide = 0, EdgeSide = 255;

    // 这块地在哪种高原上(见类头)：0 平地、1 高寒、2 黄土、3 草原、255 外缘
#if EC_PENDING
    public static byte PlateauSide(Cell zone) => zone == null ? LowlandSide : Lookup(zone).Side;
#endif

    // 陆地全是沙地、荒漠为 1(纯沙漠)，全是冻土(连冻住的沙地)为 2(纯冻土)，混着别的地、没有陆地为 0。
    // 只看地形；城心那块不算的口径见 BarrenLandSystem.KindOf
#if EC_PENDING
    public static int BarrenKind(Cell zone) => zone == null ? 0 : Lookup(zone).Barren;
#endif

    // 陆地格数与其中的可耕格数(城郊收成按可耕比例折算，见 FarmlandSystem)
#if EC_PENDING
    public static (int land, int arable) ArableCounts(Cell zone)
    {
        if (zone == null) return (0, 0);
        Entry entry = Lookup(zone);
        // 雨林全开发：丛林格也能种地
        return (entry.Land, WildernessSystem.JungleDeveloped ? Math.Min(entry.Land, entry.Arable + entry.Jungle) : entry.Arable);
    }
#endif

    private const float LivableScale = 3f;
    // 丛林格按草地算比原来多多少(丛林 0.6，草地 1，见 LivableWeight)
    private const float JungleLivableGain = (1f - 0.6f) * LivableScale;
    // 荒地格：一格宜居度低于这个(沙漠、荒原、山石、沙地、地狱)
    public const float BarrenWeight = 0.3f;

    // 一座城的地块宜居度合计(几块"整块草地")：盖房上限、民居选址都要用，按城缓存几秒(真实时间)
    private static readonly Dictionary<City, (float value, float at)> CityLivable = new();

#if EC_PENDING
    public static float CityLivability(City city)
    {
        if (city?.Zones == null) return 0f;
        float now = Time.realtimeSinceStartup;
        if (CityLivable.TryGetValue(city, out var hit) && now - hit.at < 5f && now >= hit.at) return hit.value;
        float sum = 0f;
        foreach (Cell zone in city.Zones) sum += Livability(zone);
        if (CityLivable.Count > 4096) CityLivable.Clear();
        CityLivable[city] = (sum, now);
        return sum;
    }
#endif

    // 一格地能住多少人(相对草地)：草地、可耕地 1，林地 0.8，黄土高原 0.7，丛林 0.6，稀树草原 0.5，沼泽 0.4，冻土、高寒高原 0.3，
    // 丘陵山石、沙地 0.2，荒原 0.2，沙漠 0.15，地狱 0.05，水、岩浆 0
#if EC_PENDING
    public static float LivableWeight(TerrainKind type)
    {
        if (type == null) return 0f;
        if (PlateauTerrain.IsPlateau(type))
            return ReferenceEquals(type, PlateauTerrain.Alpine) ? 0.3f : ReferenceEquals(type, PlateauTerrain.Steppe) ? 0.5f : 0.7f;
        int kinds = LandKinds.Of(type);
        if ((kinds & (LandKinds.Water | LandKinds.Lava)) != 0) return 0f;
        if ((kinds & LandKinds.Infernal) != 0) return 0.05f;
        if ((kinds & LandKinds.Rough) != 0) return 0.2f;
        switch (type.BiomeId)
        {
            case "biome_desert": return 0.15f;
            case "biome_wasteland": return 0.2f;
            case "biome_savanna": return 0.5f;
            case "biome_permafrost": return 0.3f;
        }
        if ((kinds & LandKinds.Sand) != 0) return 0.2f;
        if ((kinds & LandKinds.Forest) != 0) return (kinds & LandKinds.Wet) != 0 ? 0.6f : 0.8f;
        if ((kinds & LandKinds.Wet) != 0) return 0.4f;
        if ((kinds & LandKinds.Woodland) != 0) return 0.8f;
        if ((kinds & (LandKinds.Grass | LandKinds.Arable)) != 0) return 1f;
        return 0.5f;
    }

    private static Entry Lookup(Cell zone)
    {
        if (zone?.Tiles == null || EmpireWorld.Current == null) return new Entry { Terrain = FrontTerrain.Plains };
        int id = zone.Index;
        if (id < 0) return Classify(zone);
        if (id >= _cache.Length)
            Array.Resize(ref _cache, Math.Max(id + 1, EmpireWorld.Current.Zones?.All?.Count ?? 0));
        double now = GameClock.Now;
        ref Entry hit = ref _cache[id];
        if (hit.Known && now >= hit.At && GameClock.MonthsSince(hit.At) < RefreshMonths + id % RefreshSpreadMonths)
            return hit;
        Entry fresh = Classify(zone);
        fresh.At = now;
        fresh.Known = true;
        hit = fresh;
        return fresh;
    }
#endif

    // 交战地形：城中心那块地是城池
#if EC_PENDING
    public static FrontTerrain OfBattle(City city, Cell zone) =>
        zone != null && city?.SeatCell()?.Zone == zone ? FrontTerrain.Urban : Of(zone);

    public static bool SlowsMarch(Cell zone) => zone != null && Effect(Of(zone)).SlowMarch;
#endif

    public static string Name(FrontTerrain terrain) => LM.Get("terrain_" + terrain.ToString().ToLowerInvariant());

    // 地形效果的一句话说明
    public static string Describe(FrontTerrain terrain)
    {
        TerrainEffect effect = Effect(terrain);
        var parts = new List<string>();
        if (effect.Attack < 0.999f) parts.Add(string.Format(LM.Get("terrain_effect_attack"), effect.Attack.ToString("0.##")));
        if (effect.AttackerOrg > 0f && effect.AttackerOrg == effect.DefenderOrg)
            parts.Add(string.Format(LM.Get("terrain_effect_org_both"), effect.AttackerOrg));
        else
        {
            if (effect.AttackerOrg > 0f) parts.Add(string.Format(LM.Get("terrain_effect_org_attacker"), effect.AttackerOrg));
            if (effect.DefenderOrg > 0f) parts.Add(string.Format(LM.Get("terrain_effect_org_defender"), effect.DefenderOrg));
        }
        if (effect.MaxAdvance < 4) parts.Add(string.Format(LM.Get("terrain_effect_advance"), effect.MaxAdvance));
        if (effect.SlowMarch) parts.Add(LM.Get("terrain_effect_march"));
        return parts.Count == 0 ? LM.Get("terrain_effect_none") : string.Join(LM.Get("terrain_effect_separator"), parts);
    }

    // 格子种类的地形标记(按 TerrainKind.index_id：原版地面格与顶层格共用一套编号)。判地形每块地 64 格，
    // 以前逐格比 id、生物群系的字符串；现在每种格子第一次见到时算一次标记，逐格只取标记
    private const int MarkWater = 1, MarkBlock = 2, MarkMountain = 4, MarkHills = 8, MarkSnowHills = 16, MarkSwamp = 32,
        MarkJungle = 64, MarkWoods = 128, MarkDesert = 256, MarkSnow = 512;
    private static int[] _marks = new int[0];
    private static TerrainKind[] _markTypes = new TerrainKind[0];

#if EC_PENDING
    private static int Marks(TerrainKind type)
    {
        int index = type.index_id;
        if (index < 0) return ComputeMarks(type);
        if (index >= _marks.Length)
        {
            int size = Math.Max(index + 1, 256);
            Array.Resize(ref _marks, size);
            Array.Resize(ref _markTypes, size);
        }
        // 编号撞上别的格子(模板格不分编号)就照算，不串用
        if (ReferenceEquals(_markTypes[index], type)) return _marks[index];
        int marks = ComputeMarks(type);
        _marks[index] = marks;
        _markTypes[index] = type;
        return marks;
    }
#endif

    private static int ComputeMarks(TerrainKind type)
    {
        int marks = 0;
        if (type.Liquid && !type.Lava) marks |= MarkWater;
        if (type.Block) marks |= MarkBlock;
        if (type.Mountains || type.Summit) marks |= MarkMountain;
        string id = type.Id ?? "";
        if (id == "hills") marks |= MarkHills;
        else if (id == "snow_hills") marks |= MarkSnowHills;
        switch (type.BiomeId)
        {
            case "biome_swamp": marks |= MarkSwamp; break;
            case "biome_jungle": marks |= MarkJungle; break;
            case "biome_birch":
            case "biome_maple": marks |= MarkWoods; break;
            // 沙地是沙滩，不算沙漠(用户 2026-11-02 "沙滩不是沙漠"，同 WildernessSystem.ComputeMarks)
            case "biome_desert": marks |= MarkDesert; break;
        }
        if (type.BiomeId == "biome_permafrost" || id.StartsWith("snow", StringComparison.Ordinal) ||
            id.StartsWith("frozen", StringComparison.Ordinal)) marks |= MarkSnow;
        return marks;
    }

    // 山间小块：不到这么多格的一片不算(碰着边也不算把地块隔开)
    private const int PocketCells = 4;

    // 山以外的地(四邻相连)碰得到地块边的只有一片(不到 PocketCells 格的不算)，且这一片碰得到两条边：能从一边走到另一边。
    // 被山脊隔成两片(各自碰着边)的、只通一条边的死胡同都过不去
    private static bool Crossable(bool[] open, int size)
    {
        var seen = new bool[open.Length];
        var stack = new Stack<int>();
        int pieces = 0;
        bool through = false;
        for (int start = 0; start < open.Length; start++)
        {
            if (!open[start] || seen[start]) continue;
            int edges = 0, cells = 0;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int cell = stack.Pop(), x = cell % size, y = cell / size;
                cells++;
                if (x == 0) edges |= 1;
                if (x == size - 1) edges |= 2;
                if (y == 0) edges |= 4;
                if (y == size - 1) edges |= 8;
                if (x > 0) Visit(cell - 1);
                if (x < size - 1) Visit(cell + 1);
                if (y > 0) Visit(cell - size);
                if (y < size - 1) Visit(cell + size);
            }
            if (edges == 0 || cells < PocketCells) continue;
            pieces++;
            through |= (edges & (edges - 1)) != 0;
        }
        return pieces == 1 && through;

        void Visit(int cell)
        {
            if (!open[cell] || seen[cell]) return;
            seen[cell] = true;
            stack.Push(cell);
        }
    }

    // 按地块里 64 格的构成判定：山多为山地，山与丘陵多为丘陵，其次沼泽、水网(河湖)、丛林、森林、沙漠、雪原，都不够为平原。
    // 门槛按本地大地图实测：城市地块里平原约四成，丛林、雪原、水网、山地、丘陵各占一到两成(视地图而定)
#if EC_PENDING
    private static Entry Classify(Cell zone)
    {
        (FrontTerrain terrain, bool impassable, int land, int dry, int livable, int arable, int barren, int jungle, int rock) =
            ClassifyTiles(zone, out int alpine, out int loessArable, out int steppeArable, out byte side);
        return new Entry
        {
            Terrain = terrain, Impassable = impassable,
            Land = (byte)Math.Min(byte.MaxValue, land), Dry = (byte)Math.Min(byte.MaxValue, dry),
            Livable = (byte)Math.Min(byte.MaxValue, livable), Arable = (byte)Math.Min(byte.MaxValue, arable),
            Barren = (byte)barren, Jungle = (byte)Math.Min(byte.MaxValue, jungle), Rock = (byte)Math.Min(byte.MaxValue, rock),
            Alpine = (byte)Math.Min(byte.MaxValue, alpine), LoessArable = (byte)Math.Min(byte.MaxValue, loessArable),
            SteppeArable = (byte)Math.Min(byte.MaxValue, steppeArable), Side = side
        };
    }

    private static (FrontTerrain terrain, bool impassable, int land, int dry, int livable, int arable, int barren, int jungle, int rock) ClassifyTiles(
        Cell zone, out int alpine, out int loessArable, out int steppeArable, out byte side)
    {
        int total = 0, water = 0, mountain = 0, hills = 0, trees = 0, woods = 0, swamp = 0, jungle = 0, desert = 0, snow = 0,
            blocked = 0, land = 0, dry = 0, arable = 0, sandy = 0, frozen = 0, snowHills = 0, plateau = 0, loess = 0, steppe = 0;
        alpine = loessArable = steppeArable = 0;
        side = LowlandSide;
        float livableSum = 0f;
        // 地块里哪些格走得了(不是山地、山巅)，看山以外的地连不连得过去(见 Crossable)
        int size = Land.ZoneSize, ox = zone.X * size, oy = zone.Y * size;
        var open = new bool[size * size];
        void Open(Cell at, bool walkable)
        {
            int lx = at.X - ox, ly = at.Y - oy;
            if (lx >= 0 && ly >= 0 && lx < size && ly < size) open[ly * size + lx] = walkable;
        }
        foreach (Cell tile in zone.Tiles)
        {
            TerrainKind type = tile?.Type;
            if (type == null) continue;
            total++;
            // 高原格(地面格是高原)：冻上、修了路的照样按高原算(看底下的地面)；开了田的按田算可耕
            TerrainKind high = PlateauTerrain.IsPlateau(tile.MainType) ? tile.MainType : null;
            // 水土：陆地(不含水、岩浆)与其中的旱地
            int kinds = LandKinds.Of(high != null && !type.FarmField ? high : type);
            if ((kinds & (LandKinds.Water | LandKinds.Lava)) == 0)
            {
                land++;
                if ((kinds & LandKinds.Dry) != 0) dry++;
                // 草原高原算旱地，但能少量耕(收成五折)：可耕格照算
                bool steppeTile = ReferenceEquals(high, PlateauTerrain.Steppe);
                if ((kinds & LandKinds.Arable) != 0 || steppeTile)
                {
                    arable++;
                    if (steppeTile) steppeArable++;
                    else if (high != null) loessArable++;
                }
                livableSum += LivableWeight(high ?? type);
                if (high != null)
                {
                    if (steppeTile) steppe++;
                    else
                    {
                        plateau++;
                        if (ReferenceEquals(high, PlateauTerrain.Alpine)) alpine++;
                        else loess++;
                    }
                }
                // 纯沙漠、纯冻土(见 BarrenLandSystem)：荒漠(沙地是沙滩，不算——海边一圈沙滩的地块以前算成纯荒地)；冻土(冻住的沙地也是冻土)。
                // 路、田、墙铺在上面的看底下的地面(沙漠里修了路、开过田也还是沙漠)；高原格不算(冻上了也是高原)
                TerrainKind ground = (type.Road || type.FarmField || type.Wall) && tile.MainType != null ? tile.MainType : type;
                if (high == null)
                {
                    if (ground.BiomeId == "biome_permafrost") frozen++;
                    else if (ground.BiomeId == "biome_desert") sandy++;
                }
            }
            int marks = Marks(type);
            if ((marks & MarkWater) != 0)
            {
                water++;
                Open(tile, true);
                continue;
            }
            // 底下的地面格只看阻挡、山地，丘陵只认 hills
            TerrainKind main = tile.MainType;
            int under = main != null ? Marks(main) : 0;
            bool block = ((marks | under) & MarkBlock) != 0;
            if (block) blocked++;
            Open(tile, !block);
            if (((marks | under) & MarkMountain) != 0) mountain++;
            else if ((marks & (MarkHills | MarkSnowHills)) != 0 || (under & MarkHills) != 0) hills++;
            if ((marks & MarkSnowHills) != 0) snowHills++;
            if ((marks & MarkSwamp) != 0) swamp++;
            else if ((marks & MarkJungle) != 0) jungle++;
            else if ((marks & MarkWoods) != 0) woods++;
            else if ((marks & MarkDesert) != 0) desert++;
            if ((marks & MarkSnow) != 0) snow++;
            BuildingAsset flora = tile.building?.asset;
            if (flora != null && flora.flora && flora.flora_type == FloraType.Tree) trees++;
        }
        int livable = (int)Math.Round(livableSum * LivableScale);
        int highLand = plateau + steppe;
        if (land > 0 && highLand * 2 > land)
            side = alpine >= loess && alpine >= steppe ? (byte)1 : loess >= steppe ? (byte)2 : (byte)3;
        else if (land > 0 && highLand > land * 0.15f) side = EdgeSide;
        int barren = land == 0 || sandy + frozen < land ? 0 : sandy >= frozen ? 1 : 2;
        if (total == 0) return (FrontTerrain.Plains, false, land, dry, livable, arable, barren, jungle, blocked);
        bool impassable = blocked >= total * 0.75f || blocked > 0 && !Crossable(open, size);
        float Share(int count) => count / (float)total;
        if (Share(mountain) >= 0.25f) return (FrontTerrain.Mountain, impassable, land, dry, livable, arable, barren, jungle, blocked);
        if (Share(plateau) >= 0.4f) return (FrontTerrain.Plateau, impassable, land, dry, livable, arable, barren, jungle, blocked);
        // 雪丘连片(青藏、帕米尔高原面)是雪原天堑，不是丘陵(用户 2026-10-29："控制力……完全没有体现地形的影响")
        if (Share(snowHills) >= 0.4f) return (FrontTerrain.Snow, impassable, land, dry, livable, arable, barren, jungle, blocked);
        if (Share(mountain + hills) >= 0.3f) return (FrontTerrain.Hills, impassable, land, dry, livable, arable, barren, jungle, blocked);
        if (Share(swamp) >= 0.3f) return (FrontTerrain.Swamp, impassable, land, dry, livable, arable, barren, jungle, blocked);
        if (Share(water) >= 0.15f) return (FrontTerrain.River, impassable, land, dry, livable, arable, barren, jungle, blocked);
        if (Share(jungle) >= 0.3f) return (FrontTerrain.Jungle, impassable, land, dry, livable, arable, barren, jungle, blocked);
        // 城市周边的树大多被砍过，一块地里有四棵树(约 6%)就算林地；桦树林、枫树林等林地生物群系按面积算
        if (Share(trees) >= 0.06f || Share(woods) >= 0.3f) return (FrontTerrain.Forest, impassable, land, dry, livable, arable, barren, jungle, blocked);
        if (Share(desert) >= 0.4f) return (FrontTerrain.Desert, impassable, land, dry, livable, arable, barren, jungle, blocked);
        if (Share(snow) >= 0.4f) return (FrontTerrain.Snow, impassable, land, dry, livable, arable, barren, jungle, blocked);
        return (FrontTerrain.Plains, impassable, land, dry, livable, arable, barren, jungle, blocked);
    }
#endif
}
}
