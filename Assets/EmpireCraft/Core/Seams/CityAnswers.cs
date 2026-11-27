using System;
using Orbis.Flat;

namespace EmpireCraft.Core
{
    // What the ported systems ask of a city that this game answers from its own books.
    //
    // - Loyalty: the city's attachment to its realm, the same number the realm's books use for its
    //   stability (RealmAccounts.Loyalty: full at the capital, falling with distance from it).
    // - Food in store: this game does not keep grain as goods; a city has the food its land yields.
    //   Counted, as the ported systems count it, in households fed for a year: the households of the
    //   city times how far its land's carrying capacity covers its people (at most three years' worth).
    //   A city within what its land can feed is fed; one far past it runs short.
    // - Its level as a town (CityLevel): the mod counted the levels of a city's own hall, which
    //   comes with a library and a temple at each level; here there are no buildings, so the level
    //   goes by the people: one at 5 000, then 10 000, 20 000, 50 000 and 100 000 (at most five).
    // - What its land yields (LandYields): goods are not kept in store here, so a material a
    //   technology needs is found where the city's land has it: wood in forest, stone in hills and
    //   mountains, hide and bone where game lives, salt on coasts and in desert; ores in hills and
    //   mountains, and each city has a fixed draw for each ore (copper six cities in ten, coal more than
    //   half, silver and gold three in ten, oil and uranium fewer), so ores are found in some places
    //   and not in others. Made things (gunpowder, fertiliser) and the stuff of legend are not dug up.
    public static class CityAnswers
    {
        static readonly double[] LevelAt = { 5000, 10000, 20000, 50000, 100000 };

        public static int CityLevel(City city)
        {
            if (city == null || !city.Alive) return 0;
            int level = 0;
            foreach (double people in LevelAt) if (city.Population >= people) level++;
            return level;
        }

        public static bool LandYields(City city, string resource)
        {
            var world = city?.World;
            if (world == null || !city.Alive || resource == null) return false;
            bool forest = false, hills = false, mountains = false, game = false, coast = false, dry = false, wet = false;
            foreach (int tile in city.Tiles)
            {
                Biome biome = world.Map.Biomes[tile];
                TerrainKind kind = Land.KindOf(world.Map, tile);
                forest |= biome == Biome.Woodland || biome == Biome.Rainforest || biome == Biome.Taiga || biome == Biome.Wetland;
                game |= biome == Biome.Grassland || biome == Biome.Steppe || biome == Biome.Woodland || biome == Biome.Taiga || biome == Biome.Tundra || biome == Biome.Rainforest;
                hills |= kind.Rocks || kind.Mountains;
                mountains |= kind.Mountains || kind.Summit;
                dry |= biome == Biome.Desert || biome == Biome.Steppe;
                wet |= biome == Biome.Wetland;
                if (!coast) foreach (var next in world.CellAt(tile).Neighbours) if (next.Type.Ocean) { coast = true; break; }
            }
            switch (resource)
            {
                case "wood": return forest;
                case "stone": return hills;
                case "leather": case "bones": return game;
                case "crystal_salt": return coast || dry;
                case "copper": case "common_metals": return hills && Draw(city, resource) < 0.6;
                case "coal": return (hills || forest) && Draw(city, resource) < 0.55;
                case "silver": case "gold": case "gems": return hills && Draw(city, resource) < 0.3;
                case "saltpeter": return (dry || hills) && Draw(city, resource) < 0.35;
                case "oil": return (dry || wet || coast) && Draw(city, resource) < 0.3;
                case "uranium": return mountains && Draw(city, resource) < 0.15;
                default: return false;
            }
        }

        // A fixed number in [0, 1) for this city and this ore, the same in every run.
        static double Draw(City city, string resource) =>
            FMath.Unit(FMath.Seed(resource) ^ (uint)city.Id * 0x9E3779B9u ^ (uint)city.Tile * 0x85EBCA6Bu);

        const double MaxYearsInStore = 3d;

        public static int GetLoyalty(this City city)
        {
            var world = city?.World;
            return world == null || city.Realm == null ? 0 : RealmAccounts.Loyalty(world, city.Realm, city);
        }

        public static float GetTotalFood(this City city)
        {
            var world = city?.World;
            if (world == null || !city.Alive) return 0f;
            double people = Math.Max(1d, city.Population);
            double covered = Math.Min(MaxYearsInStore, SkeletonRules.Capacity(world, city) / people);
            return (float)(CityPopulationSystem.Households(city) * covered);
        }
    }
}
