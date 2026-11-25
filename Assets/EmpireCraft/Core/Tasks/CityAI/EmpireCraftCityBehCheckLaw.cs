// Generated from the EmpireCraft mod: Scripts/AI/CityAI/EmpireCraftCityBehCheckLaw.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftCityBehCheckLaw : GameAICityBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult Execute(City pCity)
    {
        if (pCity == null || pCity.IsGone() || !pCity.HasRealm())
        {
            return BehResult.Continue;
        }

        if (!pCity.IsLawScanDue(1f))
        {
            return BehResult.Continue;
        }

        pCity.RecordLawScan();
        EmpireLawSystem.CheckAutomaticLawTriggersForCity(pCity);
        return BehResult.Continue;
    }
#endif
}
}
