// Generated from the EmpireCraft mod: Scripts/GeneralSystems/Warfare/FrontLineCleanup.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 孤块归位(用户 2026-11-09 "为什么不能像钢铁雄心三一样保持战线或者维持战线"，选"全线推进，孤块自动归位")：
// 每月交战结算之后整理一遍有地块被占的城，国界保持一条线——
//   · 占领者的孤块：某国在一座城里占着的一片地，往外不穿对方控制的地，走不到己方或友方的城池(PocketZones 块以内走不出去)，
//     这片地和它四周也没有本方的军，就还给城主(断了后路守不住)；
//   · 城主的孤块：城主在一座部分被占的城里剩下的一片地(城池那片不算)，同样走不到己方的城池、四周全是同一个占领者，
//     上面没有城主一方的军，就归那个占领者。
//   有军在里面的是被围的军，归 EncirclementSystem 管，这里不动。
public static class FrontLineCleanup
{
    private const int PocketZones = 24;
    private static readonly HashSet<int> Seen = new();
    private static readonly Queue<Cell> Frontier = new();

#if EC_PENDING
    public static void Monthly()
    {
        if (EmpireWorld.Current?.Cities == null || !CorpsSystem.Enabled) return;
        List<City> cities = EmpireWorld.Current.Cities.Where(city => city?.Record != null && !city.IsGone() && city.HasOccupiedZones()).ToList();
        if (cities.Count == 0) return;
        // 各地块上有哪些国的军
        var troops = new Dictionary<int, List<Realm>>();
        foreach ((Realm kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (corps == null || corps.strength < 1f || corps.embarked) continue;
            if (!troops.TryGetValue(corps.zone_id, out List<Realm> here)) troops[corps.zone_id] = here = new List<Realm>();
            here.Add(kingdom);
        }
        foreach (City city in cities)
        {
            try
            {
                Tidy(city, troops);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][孤块归位] {city.GetCityName()} 失败: {exception.Message}");
            }
        }
    }

    private static void Tidy(City city, Dictionary<int, List<Realm>> troops)
    {
        Realm owner = city.Realm;
        if (owner?.Record == null || owner.IsGone() || city.Zones == null) return;
        Cell seat = city.SeatCell()?.Zone;
        // 各块地的控制者
        var byController = new Dictionary<Realm, List<Cell>>();
        foreach (Cell zone in city.Zones)
        {
            if (zone == null) continue;
            Realm controller = FrontSystem.Controller(zone);
            if (controller?.Record == null) continue;
            if (!byController.TryGetValue(controller, out List<Cell> list)) byController[controller] = list = new List<Cell>();
            list.Add(zone);
        }
        int returned = 0, taken = 0;
        // 占领者的孤块还给城主
        foreach (KeyValuePair<Realm, List<Cell>> pair in byController.Where(pair => pair.Key != owner).ToList())
        {
            Realm occupier = pair.Key;
            foreach (List<Cell> piece in Pieces(pair.Value))
            {
                if (!Cut(occupier, piece, troops)) continue;
                foreach (Cell zone in piece)
                    if (city.RemoveOccupiedTileZone(occupier, zone)) returned++;
            }
        }
        // 城主的孤块(城池那片不算)归围住它的占领者
        if (byController.TryGetValue(owner, out List<Cell> held))
        {
            foreach (List<Cell> piece in Pieces(held))
            {
                if (seat != null && piece.Contains(seat)) continue;
                if (!Cut(owner, piece, troops)) continue;
                Realm besieger = Surrounder(piece, owner);
                if (besieger == null) continue;
                foreach (Cell zone in piece)
                    if (city.AddOccupiedTileZone(besieger, zone)) taken++;
            }
        }
        if (returned + taken > 0)
            LogService.LogInfo($"[EmpireCraft][孤块归位] {city.GetCityName()}：还给城主 {returned} 块，归占领者 {taken} 块");
    }
#endif

    // 同一座城里同一控制者的地块，按相邻连成几片
    private static List<List<Cell>> Pieces(List<Cell> zones)
    {
        var left = new HashSet<Cell>(zones);
        var pieces = new List<List<Cell>>();
        while (left.Count > 0)
        {
            Cell start = left.First();
            left.Remove(start);
            var piece = new List<Cell> { start };
            var queue = new Queue<Cell>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Cell zone = queue.Dequeue();
                foreach (Cell next in zone.NeighboursAll ?? Array.Empty<Cell>())
                {
                    if (next == null || !left.Remove(next)) continue;
                    piece.Add(next);
                    queue.Enqueue(next);
                }
            }
            pieces.Add(piece);
        }
        return pieces;
    }

    // 这片地断了后路：往外不穿敌控的地，走不到 side 一方的城池，PocketZones 块以内就走完了；走到的地方也没有 side 一方的军
    private static bool Cut(Realm side, List<Cell> piece, Dictionary<int, List<Realm>> troops)
    {
        Seen.Clear();
        Frontier.Clear();
        foreach (Cell zone in piece)
        {
            Seen.Add(zone.Index);
            Frontier.Enqueue(zone);
        }
        bool enemy = false;
        while (Frontier.Count > 0)
        {
            Cell zone = Frontier.Dequeue();
            if (troops.TryGetValue(zone.Index, out List<Realm> here) && here.Any(kingdom => FrontSystem.Friendly(side, kingdom))) return false;
            City city = zone.City;
            if (city?.Record != null && !city.IsGone() && city.SeatCell()?.Zone == zone && FrontSystem.Friendly(side, FrontSystem.Controller(zone)))
                return false;
            foreach (Cell next in zone.NeighboursAll ?? Array.Empty<Cell>())
            {
                if (next == null || Seen.Contains(next.Index) || SeaRegionSystem.IsSea(next) || TerrainSystem.Impassable(next) ||
                    !TerrainSystem.Crossable(zone, next)) continue;
                Realm controller = FrontSystem.Controller(next);
                if (controller != null && FrontSystem.Hostile(side, controller))
                {
                    enemy = true;
                    continue;
                }
                Seen.Add(next.Index);
                if (Seen.Count > PocketZones) return false;
                Frontier.Enqueue(next);
            }
        }
        return enemy;
    }

    // 围住这片地的敌方：四周挨着的敌控地块最多的那一国
    private static Realm Surrounder(List<Cell> piece, Realm owner)
    {
        var count = new Dictionary<Realm, int>();
        foreach (Cell zone in piece)
        foreach (Cell next in zone.NeighboursAll ?? Array.Empty<Cell>())
        {
            Realm controller = FrontSystem.Controller(next);
            if (controller?.Record == null || !FrontSystem.Hostile(owner, controller)) continue;
            count[controller] = count.TryGetValue(controller, out int n) ? n + 1 : 1;
        }
        return count.Count == 0 ? null : count.OrderByDescending(pair => pair.Value).First().Key;
    }
}
}
