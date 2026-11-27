namespace EmpireCraft.Core
{
    // Hand-written half of StateSettlementSystem: ModeChanged. When the world law switched between people on the map
    // and population as numbers, the mod finished or dropped the settlement projects under way. In this game the
    // population is numbers from the start and never switches, so there is nothing to wind up.
    public static partial class StateSettlementSystem
    {
        public static void ModeChanged(bool enabled) { }

        // Whether realms are still waiting for their site search to go on. The mod's scheduler took one a frame;
        // a month step here goes on until none is waiting (see PortedSystems).
        public static bool SearchesPending => PendingSearches.Count > 0;
    }
}
