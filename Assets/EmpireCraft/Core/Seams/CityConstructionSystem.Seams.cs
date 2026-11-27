namespace EmpireCraft.Core
{
    // Hand-written half of CityConstructionSystem: StartSettlement. In the mod a newly settled city began putting up
    // its first buildings (houses, a granary, a windmill) as objects on the map, paid from its stores. Nothing is built
    // as an object in this game: a city's works are counted in its numbers, so a new city starts with nothing to queue.
    public static partial class CityConstructionSystem
    {
        public static void StartSettlement(City city) { }
    }
}
