// Generated from the EmpireCraft mod: Scripts/HelperFunc/EmpirePopulation.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{
    internal static class EmpirePopulation
    {
        public static IEnumerable<Person> Enumerate(IEnumerable<Realm> kingdoms)
        {
            if (kingdoms == null) yield break;
            var visited = new HashSet<Realm>();
            foreach (Realm kingdom in kingdoms.ToArray())
            {
                if (kingdom?.Record == null || kingdom.IsGone() || !visited.Add(kingdom) ||
                    AncientWarfareCompatibility.Owns(kingdom)) continue;
                List<Person> units = kingdom.People;
                if (units == null) continue;
                // Vanilla getUnits dereferences every slot before yielding. Sparse/stale
                // membership lists must be checked here, without mutating another mod's index.
                // A private snapshot also tolerates relocation/removal by the iterator's caller.
                foreach (Person actor in units.ToArray())
                {
                    if (kingdom.Record == null || kingdom.IsGone()) break;
                    if (actor?.Record == null || actor.Asset == null || !actor.Alive ||
                        actor.Asset.IsBoat || actor.Realm != kingdom ||
                        AncientWarfareCompatibility.Owns(actor)) continue;
                    yield return actor;
                }
            }
        }
    }
}
