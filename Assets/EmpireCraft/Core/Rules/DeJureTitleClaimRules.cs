// Ported from the EmpireCraft mod: Scripts/Layer/DeJureTitleClaimRules.cs @ e770dbbe. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public static class DeJureTitleClaimRules
{
    public const double DeclarationControlShare = 2d / 3d;

    public static bool ControlsRequiredShare(int controlledZones, int totalZones)
    {
        if (controlledZones < 0 || totalZones <= 0) return false;
        return controlledZones >= (int)Math.Ceiling(totalZones * DeclarationControlShare);
    }

    public static bool IsStrongEnough(double claimantPower, double holderPower)
    {
        return claimantPower > 0d && claimantPower > holderPower;
    }

    public static bool CanDeclare(int controlledZones, int totalZones,
        double claimantPower, double holderPower)
    {
        return ControlsRequiredShare(controlledZones, totalZones) &&
               IsStrongEnough(claimantPower, holderPower);
    }

    public static bool CanAcquireWithoutWar(bool controlsTitleCapital,
        int controlledZones, int totalZones)
    {
        return controlsTitleCapital || ControlsRequiredShare(controlledZones, totalZones);
    }

    public static bool ControlsAll(int controlledZones, int totalZones)
    {
        return totalZones > 0 && controlledZones >= totalZones;
    }
}
}
