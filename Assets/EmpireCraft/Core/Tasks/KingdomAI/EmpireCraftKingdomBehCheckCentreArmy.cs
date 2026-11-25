// Generated from the EmpireCraft mod: Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckCentreArmy.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftKingdomBehCheckCentreArmy:GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult Execute(Realm pKingdom)
    {
        if (pKingdom.GetRegime() == null) return BehResult.Continue;
        if (pKingdom.GetRegime().IsAllowDiplomacy() && !pKingdom.GetRegime().IsAllowSupportCenterArmy())
        {
            pKingdom?.GetCenterArmy()?.disband();
        }
        return BehResult.Continue;
    }
#endif
    
}
}
