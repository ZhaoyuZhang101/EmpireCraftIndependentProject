namespace EmpireCraft.Core
{
    // Hand-written half of RealisticAgeSystem: SpeciesLifespan. The mod read the natural lifespan of a kind of creature
    // from the base game's genome. There is one people here and no genome; the mod's own fallback lifespan is used.
    public static partial class RealisticAgeSystem
    {
        public static float SpeciesLifespan(string species) => FallbackLifespan;
    }
}
