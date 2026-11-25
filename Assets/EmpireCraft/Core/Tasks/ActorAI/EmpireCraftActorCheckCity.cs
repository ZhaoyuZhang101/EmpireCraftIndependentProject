// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorCheckCity.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftActorCheckCity:GameAIActorBase
{
    public override Type OriginalBeh => GetType();
#if EC_PENDING
    public override BehResult Execute(Person pActor)
    {
        if (!pActor.IsFitToRule()) return BehResult.Continue;
        if (pActor.Realm.IsNeutral()) return BehResult.Continue;
        if (!WorldLaws.PreventCityDestroy.IsEnabled()) return BehResult.Continue;
        if (pActor.current_zone.hasCity())
        {
            City city = pActor.current_zone.city;
            if (pActor.HasRealm() && city != null && city.PopulationCount()<=0)
            {
                Realm kingdom = pActor.Realm;
                // 空城换主只在无主、或两国正在交战时(群友 2026-11-22 "城市永不毁灭好像出了点问题")：以前不看城是谁的，
                // 任何路过的人都把别国(盟友、同一帝国的也算)的空城划进本国，太平年月城就这样易了主。本国的空城照旧迁进去住
                Realm owner = city.Realm;
                if (owner != kingdom)
                {
                    if (owner != null && !owner.IsNeutral() && !owner.IsWild && !kingdom.IsAtWarWith(owner)) return BehResult.Continue;
                    city.JoinRealm(kingdom);
                }
                if (pActor.HasFamily())
                {
                    foreach (var fActor in pActor.Family.units)
                    {
                        fActor.joinCity(city);
                        fActor.goTo(city.SeatCell());
                    }
                }
                else
                {
                    pActor.JoinCity(city);
                    pActor.GoTo(city.SeatCell());
                }
            }
        }
        return BehResult.Continue;
    }
#endif
}
}
