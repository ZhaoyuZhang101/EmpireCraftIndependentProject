using System.Reflection;

namespace EmpireCraft.Core
{
    // Hand-written half of NuclearDoctrineSystem: TryStrike. The mod looked up, by reflection, the nuclear strike of
    // another mod (WarBox) it could work with. There is no other mod here, so there is no such strike to call.
    public static partial class NuclearDoctrineSystem
    {
        private static MethodInfo TryStrike => null;
    }
}
