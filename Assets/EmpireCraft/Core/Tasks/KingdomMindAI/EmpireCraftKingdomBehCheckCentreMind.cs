// Generated from the EmpireCraft mod: Scripts/AI/KingdomMindAI/EmpireCraftKingdomBehCheckCentreMind.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftKingdomBehCheckCentreMind: GameAIKingdomMindBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult Execute(Realm pKingdom)
    {
        if (!pKingdom.HasRuler()) return BehResult.Continue;
        var regime = pKingdom.GetRegime();
        var centreMind = regime?.CentreMind;
        if (centreMind == null) return BehResult.Continue;
        CorruptionSystem.TryYearlyCampaign(pKingdom);
        return BehResult.Continue;
    }

}
}
