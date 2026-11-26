using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // One population book: the mod's. A city's people are the mod's population groups (by class, people, kind and
    // ideology; CityPopulationSystem), which grow, move, fall to plague and are levied month by month. This game's
    // skeleton keeps a head count per city (City.Population) that its own rules read and change (new cities take
    // settlers from the capital, a breakaway takes its cities, the realm's books tax heads); its own growth gives way
    // while the ported systems run (SkeletonRules.PopulationElsewhere).
    //
    // Each month, before the ported systems: what the skeleton changed in a city's head count since the last month is
    // carried into the city's groups (people added as the mod adds them when a city falls short, see
    // CityPopulationSystem.EnsureFloor: peasants of the city's main people, kind and ideology; people taken away from
    // all groups alike). A city the mod has no people for yet is seeded that way with its whole head count.
    // After them: each city's head count is the mod's total again.
    public static class PopulationBridge
    {
        static readonly Dictionary<int, double> Synced = new Dictionary<int, double>();

        public static void ResetWorldState() => Synced.Clear();

        public static void Before(EmpireWorld w)
        {
            if (!CityPopulationSystem.AbstractPopulationEnabled) return;
            foreach (var city in w.Cities)
            {
                if (!city.Alive || city.Realm == null) continue;
                double change = Synced.TryGetValue(city.Id, out double last) ? city.Population - last
                    : city.Population - CityPopulationSystem.GetTotal(city);
                if (change >= 1d) Add(city, (float)change);
                else if (change <= -1d) Take(city, (float)-change);
            }
        }

        public static void After(EmpireWorld w)
        {
            if (!CityPopulationSystem.AbstractPopulationEnabled) return;
            foreach (var city in w.Cities)
            {
                if (!city.Alive) { Synced.Remove(city.Id); continue; }
                city.Population = Math.Max(0d, CityPopulationSystem.GetTotal(city));
                Synced[city.Id] = city.Population;
            }
        }

        static void Add(City city, float people)
        {
            string species = city.GetSpecies();
            if (string.IsNullOrEmpty(species)) return;
            CityPopulationSystem.AddBackground(city, SocialClass.Peasant, CultureService.GetMainCulture(city, initialize: false) ?? "",
                species, IdeologyPopulationSystem.GetDominant(city), people);
        }

        static void Take(City city, float people)
        {
            CityPopulationData data = CityPopulationSystem.Get(city);
            if (data?.groups == null) return;
            float total = 0f;
            foreach (PopGroup group in data.groups) total += Math.Max(0f, group.Background);
            if (total <= 0f) return;
            float share = Math.Min(1f, people / total);
            foreach (PopGroup group in data.groups.ToArray())
                if (group.Background > 0f) CityPopulationSystem.RemoveBackground(group, group.Background * share);
        }
    }
}
