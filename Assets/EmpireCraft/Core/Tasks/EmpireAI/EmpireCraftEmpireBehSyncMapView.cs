// Generated from the EmpireCraft mod: Scripts/AI/EmpireAI/EmpireCraftEmpireBehSyncMapView.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Linq;

namespace EmpireCraft.Core
{

public class EmpireCraftEmpireBehSyncMapView:GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult Execute(Realm pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!pKingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = pKingdom.GetEmpire();
        if (empire == null) return BehResult.Continue;
        empire.cities_list = EmpireMembershipService.EnumerateCities(empire).ToList();
        return BehResult.Continue;
    }
#endif
}
}
