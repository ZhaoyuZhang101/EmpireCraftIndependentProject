namespace EmpireCraft.Core
{
    // Hand-written half of CorpsSystem: one member added, nothing switched off. The mod settled each month of corps and
    // fronts a few realms a frame; this game steps a month at a time and calls the corps until the month is settled
    // (see PortedSystems). The realms are settled in the same order however the work is sliced.
    public static partial class CorpsSystem
    {
        internal static bool MonthPending => _monthPhase != MonthPhase.Idle;
    }
}
