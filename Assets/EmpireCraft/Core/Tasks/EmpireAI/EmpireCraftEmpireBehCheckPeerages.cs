// Generated from the EmpireCraft mod: Scripts/AI/EmpireAI/EmpireCraftEmpireBehCheckPeerages.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

// Uses the same empire AI update path as the office system so vacant peerages
// are filled without a separate global scan.
public class EmpireCraftEmpireBehCheckPeerages : GameAIEmpireBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult execute(Realm kingdom)
    {
        kingdom.CheckEmpire();
        if (!kingdom.IsEmpire()) return BehResult.Continue;
        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.IsGone() || empire.IsArchived()) return BehResult.Continue;
        empire.update();
        return base.execute(kingdom);
    }
#endif
}
}
