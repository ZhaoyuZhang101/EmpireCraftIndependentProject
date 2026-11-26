// Ported from the EmpireCraft mod: Scripts/Layer/ImperialLegitimacyRules.cs @ e770dbbe. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public static class ImperialLegitimacyRules
{
    public static bool CanChallenge(double kingdomPower, double empirePower,
        int sameCultureEmpireCount, bool isIndependent, bool sharesLandBorder)
    {
        return isIndependent && sameCultureEmpireCount >= 1 && sharesLandBorder && kingdomPower > empirePower;
    }

    public static int RequiredOccupiedZones(int totalZones)
    {
        return totalZones <= 0 ? int.MaxValue : (int)Math.Ceiling(totalZones / 3d);
    }

    public static bool HasOccupiedThird(int occupiedZones, int totalZones)
    {
        int required = RequiredOccupiedZones(totalZones);
        return required != int.MaxValue && occupiedZones >= required;
    }
}
}
