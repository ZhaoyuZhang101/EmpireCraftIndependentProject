// Generated from the EmpireCraft mod: Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckEmpireLaw.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftKingdomBehCheckEmpireLaw : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult Execute(Realm pKingdom)
    {
        if (pKingdom == null || pKingdom.IsGone()) return BehResult.Continue;
        EmpireLawSystem.CheckMercenaryOvermightyLaw(pKingdom);
        return BehResult.Continue;
    }
}
}
