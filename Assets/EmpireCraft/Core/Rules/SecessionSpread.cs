// Ported from the EmpireCraft mod: Scripts/GeneralSystems/SecessionSpread.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 自立按法理成片(用户 2026-10-27 嫌版图太碎，问答四条全选)：一座城起事、或欠饷撤守自立时，同一个王国法理里挨着它的、
// 原主手里的城，人心也已经散了的(开着财政看实际稳定度不到起事门槛，不开看忠诚为负)，一并跟着走——至多 MaxCities 座，
// 不带原主的都城。分封诸侯国里的小乱(PettyRebellion)本来就只一城一邑，不跟。
// 叛乱隔断(用户 2026-10-27："叛乱的时候如果隔绝了其他行政区使他们变成了飞地，也应当将他们同时转为叛乱")：起事(连同跟着走的城)之后，
// 原主手里与都城同在一块陆地、却已沿本国(在帝国里的算整个帝国)相邻的城走不到都城的，这一片挨着起事一方的，一并转入起事一方；
// 本来就是飞地、不挨着起事一方的不在此列(归飞地审计，见 ExclaveMaintenanceSystem)。
public static class SecessionSpread
{
    private const int MaxCities = 4;

    // rebellion：起事(跟着走的城按原版起事那样易主)；欠饷撤守自立为 false
#if EC_PENDING
    public static int Spread(Realm rebel, City seed, Realm former, bool rebellion = true)
    {
        if (rebel?.Record == null || rebel.IsGone() || seed?.Record == null || former?.Record == null || former.IsGone() ||
            rebel == former || PettyRebellion.IsPetty(rebel)) return 0;
        int joined = Contiguous(rebel, seed, former, rebellion);
        return joined + CutOff(rebel, former, rebellion);
    }

    private static int Contiguous(Realm rebel, City seed, Realm former, bool rebellion)
    {
        KingdomTitle title = seed.GetTitle();
        if (title == null || title.IsGone()) return 0;
        var frontier = new Queue<City>();
        var seen = new HashSet<City> { seed };
        frontier.Enqueue(seed);
        int joined = 0;
        while (frontier.Count > 0 && joined < MaxCities)
        {
            City city = frontier.Dequeue();
            foreach (City next in (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>())
            {
                if (joined >= MaxCities) break;
                if (next?.Record == null || next.IsGone() || !seen.Add(next) || next.Realm != former ||
                    next == former.Capital || next.GetTitle() != title || !CityStabilitySystem.Disaffected(next)) continue;
                next.JoinRealm(rebel, pCaptured: false, pRebellion: rebellion);
                if (next.Realm != rebel) continue;
                joined++;
                frontier.Enqueue(next);
            }
        }
        if (joined > 0)
            LogService.LogInfo($"[EmpireCraft][自立成片] {rebel.GetKingdomName()} 起于 {seed.Name}，同一法理里 {joined} 座城跟着脱离 {former.GetKingdomName()}");
        return joined;
    }

    private static Realm ModRealm(Realm kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    private static IEnumerable<City> Neighbours(City city) => (IEnumerable<City>)city.neighbours_cities ?? Array.Empty<City>();
#endif

    // 被起事隔断的城：从原主都城沿本国(帝国按整个帝国)相邻的城走不到、与都城同在一块陆地、所在那一片挨着起事一方的，转入起事一方
#if EC_PENDING
    private static int CutOff(Realm rebel, Realm former, bool rebellion)
    {
        City capital = former.Capital;
        WorldTile seat = capital?.getTile();
        if (seat == null || capital.Realm != former || rebel.IsGone()) return 0;
        Realm realm = ModRealm(former);
        var reached = new HashSet<City> { capital };
        var queue = new Queue<City>();
        queue.Enqueue(capital);
        while (queue.Count > 0)
            foreach (City next in Neighbours(queue.Dequeue()))
                if (next?.Record != null && !next.IsGone() && next.Realm?.Record != null && ModRealm(next.Realm) == realm && reached.Add(next))
                    queue.Enqueue(next);
        var stranded = new HashSet<City>(former.Cities.Where(city => city?.Record != null && !city.IsGone() && !reached.Contains(city) &&
                                                                     city.getTile() is WorldTile at && at.isSameIsland(seat)));
        if (stranded.Count == 0) return 0;
        var done = new HashSet<City>();
        var names = new List<string>();
        foreach (City start in stranded.ToList())
        {
            if (!done.Add(start)) continue;
            var part = new List<City> { start };
            queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (City next in Neighbours(queue.Dequeue()))
                    if (stranded.Contains(next) && done.Add(next))
                    {
                        part.Add(next);
                        queue.Enqueue(next);
                    }
            if (!part.Any(city => Neighbours(city).Any(next => next?.Realm == rebel))) continue;
            foreach (City city in part)
            {
                if (city.Realm != former) continue;
                city.JoinRealm(rebel, pCaptured: false, pRebellion: rebellion);
                if (city.Realm == rebel) names.Add(city.Name);
            }
        }
        if (names.Count > 0)
            LogService.LogInfo($"[EmpireCraft][叛乱隔断] {rebel.GetKingdomName()} 起事隔断了 {former.GetKingdomName()} 的 {string.Join("、", names)}，一并转入");
        return names.Count;
    }
#endif
}
}
