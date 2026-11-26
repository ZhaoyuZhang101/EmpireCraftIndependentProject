// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorCheckTraits.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftActorCheckTraits: GameAIActorBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult Execute(Person pActor)
    {
        var office = pActor.GetOffice();
        if (office != null)
        {
            if (office.GetActor() != pActor)
            {
                pActor.EndOffice();
            }
        }
        if (!pActor.HasRealm()) return BehResult.Continue;
        if (!pActor.IsAdult()) return BehResult.Continue;
        if (pActor.Realm.GetRegime() == null) return BehResult.Continue;
        if (pActor.Realm.GetRegime().type == RegimeType.LvLing|| pActor.Realm.GetRegime().type == RegimeType.ZhouFeudalism) return BehResult.Continue;
        if (!pActor.HasChooseToBecomeCleric())
        {
            Random random = new Random();
            random.NextDouble();
            // 宗教等级越高，入神职的人越多(原来一律四成)：政教合一 25%、国教限制 15%、国教自由 8%、无国教 4%
            double clergy = pActor.Realm.GetRegime().GetReligionLevel() switch
            {
                ReligionLevel.High => 0.25,
                ReligionLevel.Medium => 0.15,
                ReligionLevel.Low => 0.08,
                _ => 0.04
            };
            if (random.NextDouble() <= clergy)
            {
                pActor.AddTrait("cleric");
            }
            pActor.FinishChooseToBecomeCleric();
        }
        return BehResult.Continue;
    }
}
}
