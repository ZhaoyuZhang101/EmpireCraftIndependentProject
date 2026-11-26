namespace EmpireCraft.Core
{
    // Hand-written half of StateSettlementSystem: ModeChanged. When the world law switched between people on the map
    // and population as numbers, the mod finished or dropped the settlement projects under way. In this game the
    // population is numbers from the start and never switches, so there is nothing to wind up.
    public static partial class StateSettlementSystem
    {
        public static void ModeChanged(bool enabled) { }
    }
}
