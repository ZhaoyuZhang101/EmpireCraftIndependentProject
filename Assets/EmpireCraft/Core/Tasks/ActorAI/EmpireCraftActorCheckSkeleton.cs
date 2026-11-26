// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorCheckSkeleton.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftActorCheckSkeleton: GameAIActorBase
{
    public override Type OriginalBeh => GetType();
    public override BehResult Execute(Person pActor)
    {
        if(pActor.IsGone()) return BehResult.Continue;
        if (pActor.Subspecies == null) return BehResult.Continue;
        if (pActor.Subspecies.SpeciesId != "skeleton") return BehResult.Continue;
        if (!pActor.HasTrait("death_mark"))
        {
            pActor.AddTrait("death_mark");
        }
        return BehResult.Continue;
    }
}
}
