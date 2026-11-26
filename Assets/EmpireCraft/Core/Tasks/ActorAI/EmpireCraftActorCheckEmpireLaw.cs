// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorCheckEmpireLaw.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftActorCheckEmpireLaw : GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult Execute(Person pActor)
    {
        if (pActor == null || pActor.IsGone()) return BehResult.Continue;
        EmpireLawSystem.CheckAutomaticLawTriggers(pActor);
        return BehResult.Continue;
    }
}
}
