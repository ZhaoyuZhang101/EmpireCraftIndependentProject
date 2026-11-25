// Ported from the EmpireCraft mod: Scripts/GeneralSystems/Warfare/WildernessNeighbours.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 隔着无人区相邻的城(用户 2026-11-04 转群友"虽然无人区法理界面上两座城市的领土是连着的，但是实际上在村庄视图中，
// 中间是隔了一大片雨林、沼泽、沙漠无人区的……邻国宣战机制把无人区法理算上")：
//   原版的邻城(City.neighbours_cities)只看地块真正相接；无人区不归城，隔着它的两座城不算邻城，路网不规划、AI 不挑。
//   这里把每块无人区算进它跟着的城(WildernessSystem.OriginOf)，两座城的这份地盘挨着就算一对：
//     · 一边是城的地、一边是另一座城名下的无人区；
//     · 两边都是无人区，跟着不同的城。
//   永久中立区不归任何城，不算；归了城的地块不是无人区(同 WildernessSystem)。
// 用的地方：路网规划小径(RoadNetwork)、开战够不够得着与 AI 挑目标(WarReach)、战线够不够得着(FrontSystem.InReach)。
// 不存档，用到时现算；划区或挂接变了隔几秒重算，平时半分钟核一回(城没了、法理易主)。
public static class WildernessNeighbours
{
    private const float ChangedSeconds = 5f, RefreshSeconds = 30f;
    private static readonly Dictionary<long, List<City>> Links = new();
    private static readonly HashSet<(long, long)> Seen = new();
    private static readonly List<City> None = new();
#if EC_PENDING
    private static readonly List<TileZone> Wild = new();
#endif
    private static City[] _origin = new City[0];
    private static object _world;
    private static int _version = int.MinValue;
    private static float _builtAt = float.NegativeInfinity;

#if EC_PENDING
    public static void ResetWorldState()
    {
        Links.Clear();
        Seen.Clear();
        Wild.Clear();
        _origin = new City[0];
        _world = null;
        _version = int.MinValue;
        _builtAt = float.NegativeInfinity;
    }

    public static IReadOnlyList<City> Of(City city)
    {
        if (city?.Record == null) return None;
        Ensure();
        return Links.TryGetValue(city.Key, out List<City> list) ? list : None;
    }

    private static void Ensure()
    {
        EmpireWorld world = EmpireWorld.Current;
        if (world?.zone_calculator?.zones == null) return;
        float age = Time.unscaledTime - _builtAt;
        bool fresh = ReferenceEquals(_world, world) && age >= 0f &&
                     age < (_version == WildernessSystem.Version ? RefreshSeconds : ChangedSeconds);
        if (fresh) return;
        _world = world;
        _version = WildernessSystem.Version;
        _builtAt = Time.unscaledTime;
        Rebuild(world.zone_calculator.zones);
    }

    private static void Rebuild(List<TileZone> zones)
    {
        Links.Clear();
        Seen.Clear();
        Wild.Clear();
        int size = zones.Count;
        foreach (TileZone zone in zones)
            if (zone != null && zone.id >= size) size = zone.id + 1;
        if (_origin.Length != size) _origin = new City[size];
        else global::System.Array.Clear(_origin, 0, size);
        foreach (TileZone zone in zones)
        {
            if (zone == null || zone.id < 0 || zone.city != null || !WildernessSystem.IsWild(zone) || WildernessSystem.IsNeutral(zone))
                continue;
            City origin = WildernessSystem.OriginOf(zone);
            if (origin?.Record == null || origin.IsGone()) continue;
            _origin[zone.id] = origin;
            Wild.Add(zone);
        }
        foreach (TileZone zone in Wild)
        {
            City own = _origin[zone.id];
            foreach (TileZone next in zone.neighbours_all ?? global::System.Array.Empty<TileZone>())
            {
                if (next == null || next.id < 0 || next.id >= size) continue;
                City other = next.city ?? _origin[next.id];
                if (other?.Record == null || other == own || other.IsGone()) continue;
                long a = own.Key, b = other.Key;
                if (!Seen.Add(a < b ? (a, b) : (b, a))) continue;
                Add(a, other);
                Add(b, own);
            }
        }
        Wild.Clear();
    }
#endif

    private static void Add(long id, City city)
    {
        if (!Links.TryGetValue(id, out List<City> list)) Links[id] = list = new List<City>();
        list.Add(city);
    }
}
}
