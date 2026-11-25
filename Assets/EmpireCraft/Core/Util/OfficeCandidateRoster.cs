// Generated from the EmpireCraft mod: Scripts/HelperFunc/OfficeCandidateRoster.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EmpireCraft.Core
{

// 同帧的多个职位共享有序名单；资格、官职、随机评分仍在每次选择时重新检查。
internal static class OfficeCandidateRoster
{
    private sealed class Entry
    {
        internal object World, Source;
        internal int Frame = -1, Count;
        internal long Revision;
        internal Person[] Actors;
    }
    private static readonly ConditionalWeakTable<Realm, Entry> Entries = new();
    private static long _revision;
    public static long Builds { get; private set; }
    public static long Hits { get; private set; }
#if EC_PENDING
    public static void Invalidate() { if (CityPopulationSystem.AbstractPopulationEnabled) _revision++; }

    public static IEnumerable<Person> Empire(IEnumerable<Realm> kingdoms)
    {
        if (kingdoms == null) yield break;
        var seen = new HashSet<Realm>();
        foreach (Realm kingdom in kingdoms.ToArray())
        {
            if (kingdom?.Record == null || kingdom.IsGone() || !seen.Add(kingdom) || AncientWarfareCompatibility.Owns(kingdom)) continue;
            var units = kingdom.units;
            if (units == null) continue;
            Entry entry = Entries.GetOrCreateValue(kingdom);
            if (entry.Actors == null || entry.Frame != Time.frameCount || entry.Revision != _revision ||
                !ReferenceEquals(entry.World, EmpireWorld.Current) || !ReferenceEquals(entry.Source, units) || entry.Count != units.Count)
            {
                entry.Actors = units.ToArray(); entry.Frame = Time.frameCount; entry.Revision = _revision;
                entry.World = EmpireWorld.Current; entry.Source = units; entry.Count = units.Count; Builds++;
            }
            else Hits++;
            foreach (Person actor in entry.Actors)
            {
                if (kingdom.Record == null || kingdom.IsGone()) break;
                if (actor?.Record == null || actor.asset == null || !actor.Alive || actor.asset.is_boat || actor.Realm != kingdom ||
                    AncientWarfareCompatibility.Owns(actor)) continue;
                yield return actor;
            }
        }
    }
#endif
}
}
