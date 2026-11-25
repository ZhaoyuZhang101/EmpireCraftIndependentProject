// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CityGeographySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 地利：城的地理条件决定发展的快慢与贫富——以前各城发展只看人口与国库，山城、沙漠城与河口港城一样快，看不出哪里富庶哪里贫瘠。
//   · 靠海(与海区相邻)、近河湖(城里多水的地块)：水运便利，发展快；
//   · 山地、丘陵、丛林、沼泽占的地块多：发展慢；沙漠、雪原更慢；平原、林地不加不减；
//   · 修通的路多(见 RoadBuildingSystem)：交通要冲，商业尤其受益；
//   · 三个系数，夹在 0.3 ~ 1.8，按城缓存一年：
//       - 总的(Factor)：建造速度、民居升级、迁入的吸引力，并乘进富庶度(富庶度又加快建设，富者愈富，财富向河海平原集中)；
//       - 工业(Industry)：虚拟工厂的数目(工厂要水运与平地)；
//       - 商业(Commerce)：市场容量与商人在人口里的比例(港口、河口、路口最旺)。
public static class CityGeographySystem
{
    private sealed class Geo
    {
        public double at;
        public float factor = 1f, industry = 1f, commerce = 1f;
    }

    private static readonly Dictionary<long, Geo> Cache = new();
    private static object _world;

#if EC_PENDING
    public static float Factor(City city) => Get(city).factor;
    public static float Industry(City city) => Get(city).industry;
    public static float Commerce(City city) => Get(city).commerce;

    private static Geo Get(City city)
    {
        if (city?.Record == null || city.IsGone() || EmpireWorld.Current == null) return new Geo();
        if (!ReferenceEquals(_world, EmpireWorld.Current))
        {
            _world = EmpireWorld.Current;
            Cache.Clear();
        }
        double now = GameClock.Now;
        if (Cache.TryGetValue(city.Key, out Geo cached) && now >= cached.at && GameClock.YearsSince(cached.at) < 1) return cached;
        Geo geo = Compute(city);
        geo.at = now;
        Cache[city.Key] = geo;
        return geo;
    }

    private static Geo Compute(City city)
    {
        var geo = new Geo();
        List<Cell> zones = city.Zones?.Where(zone => zone != null).ToList() ?? new List<Cell>();
        if (zones.Count == 0) return geo;
        int rough = 0, arid = 0, wet = 0;
        foreach (Cell zone in zones)
        {
            switch (TerrainSystem.Of(zone))
            {
                case FrontTerrain.Mountain:
                case FrontTerrain.Hills:
                case FrontTerrain.Plateau:
                case FrontTerrain.Jungle:
                case FrontTerrain.Swamp:
                    rough++;
                    break;
                case FrontTerrain.Desert:
                case FrontTerrain.Snow:
                    arid++;
                    break;
                case FrontTerrain.River:
                    wet++;
                    break;
            }
            if (zone.LiquidTiles > 4 && !SeaRegionSystem.IsSea(zone)) wet++;
        }
        float n = zones.Count;
        float coast = SeaRegionSystem.RegionsAround(city).Count > 0 ? 1f : 0f;
        float river = Mathf.Clamp01(wet / n * 2f);
        float roughShare = rough / n, aridShare = arid / n;
        long id = city.Key;
        int roads = RoadBuildingSystem.All.Count(pair => pair.Value.grade > 0 && (pair.Key.Item1 == id || pair.Key.Item2 == id));
        geo.factor = Clamp(1f + 0.25f * coast + 0.25f * river - 0.45f * roughShare - 0.55f * aridShare + Mathf.Min(0.2f, 0.05f * roads));
        geo.industry = Clamp(1f + 0.3f * coast + 0.3f * river - 0.6f * roughShare - 0.4f * aridShare + Mathf.Min(0.2f, 0.05f * roads));
        geo.commerce = Clamp(1f + 0.4f * coast + 0.3f * river - 0.3f * roughShare - 0.4f * aridShare + Mathf.Min(0.4f, 0.08f * roads));
        return geo;
    }
#endif

    private static float Clamp(float value) => Mathf.Clamp(value, 0.3f, 1.8f);
}
}
