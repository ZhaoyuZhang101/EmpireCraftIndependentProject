namespace EmpireCraft.Core
{
    // Hand-written half of SettlementSeaSearch: CanTransport. The mod asked whether a harbour's docks could launch a
    // transport boat of the city's people with the technology it had. Boats are not built here yet, so no harbour can
    // carry settlers overseas; settlement stays on the landmass.
    internal sealed partial class SettlementSeaSearch
    {
        private static bool CanTransport(City city, Building port) => false;
    }
}
