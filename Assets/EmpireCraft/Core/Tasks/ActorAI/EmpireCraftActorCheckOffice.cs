// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorCheckOffice.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftActorCheckOffice:GameAIActorBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult Execute(Person pActor)
    {
        if (pActor.IsGone()) return BehResult.Continue;
        if (!pActor.IsOnOffice()) return BehResult.Continue;
        var office = pActor.GetOffice();
        if (office == null)
        {
            pActor.EndOffice();
            return BehResult.Continue;
        }

        // 妃嫔、在位君主的配偶不担任后宫以外的官职(已在任的卸任，另选他人)
        bool rulerOffice = office.meta_object is Realm ruled && ruled.GetOffice() == office;
        if (!pActor.CanServeOffice(pActor.Realm) || !rulerOffice && !pActor.IsAdult() ||
            office.leader_select_method != EmpireCraft.Core.LeaderSelectMethod.Harem && !rulerOffice &&
            pActor.IsHaremOrConsort())
        {
            office.RemoveActor();
            pActor.EndOffice();
            return BehResult.Continue;
        }

        if (pActor.IsRuler())
        {
            if (pActor.Realm.HasMainTitle())
            {
                var mainTitle = pActor.Realm.GetMainTitle();
                if (mainTitle.owner.IsGone())
                {
                    pActor.AddOwnedTitle(mainTitle);
                    (pActor.Realm.GetEmpire() ?? pActor.Realm.GetTakenAllianceEmpire())?
                        .SynchronizeLandedLegalTitles(pActor.Realm);
                }
            }
        }
        if (office.actor_id == pActor.Key) return BehResult.Continue;
        pActor.EndOffice();
        return BehResult.Continue;
    }
#endif
}
}
