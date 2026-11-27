namespace EmpireCraft.Core
{
    // Hand-written half of CultureService: ResetWorldState. The mod kept the culture objects it had looked up by name for
    // ten seconds of real time, and a world took longer than that to load. Here the clock may stand still between
    // worlds (a run without frames), so the lookups are dropped when the world changes: another world's culture must
    // never be handed to this world's people.
    public static partial class CultureService
    {
        public static void ResetWorldState() => NativeCultureCache.Clear();
    }
}
