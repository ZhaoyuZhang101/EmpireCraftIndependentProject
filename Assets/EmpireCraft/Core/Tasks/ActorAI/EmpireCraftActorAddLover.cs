// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorAddLover.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftActorAddLover:GameAIActorBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult execute(Person pActor)
    {
        if (!pActor.IsFitToRule()) return BehResult.Continue;
        // 原版几岁就算成年：到了生养的年纪才纳妾、娶妻(见 RealisticAgeSystem.MinParentAge)
        if (!pActor.IsAdult() || !RealisticAgeSystem.OldEnoughToParent(pActor)) return BehResult.Continue;
        if (!pActor.HasRealm()) return BehResult.Continue;
        if (pActor.IsEmperor()) return BehResult.Continue;
        var culture = pActor.culture;
        if (culture==null) return BehResult.Continue;
        if (!culture.hasTrait("patriarchy")&&!culture.hasTrait("matriarchy")) return BehResult.Continue;
        var loverSex = culture.hasTrait("patriarchy") ? Sex.Female : Sex.Male;
        var identity = pActor.GetPersonalIdentity();
        if ( identity == null) return BehResult.Continue;
        if (pActor.age>70) return BehResult.Continue;
        var cus = identity.concubines;
        if (cus.Count > 0)
        {
            cus.ForEach(c =>
            {
                var id = c.identity;
                var cIdentity = SpecificClanManager.getPerson(id);
                var cActor = cIdentity._actor;
                if (cActor != null)
                {
                    if (RealisticAgeSystem.OldEnoughToParent(cActor) &&
                        OverallHelperFunc.HasChangeToGiveBirth(cActor, pActor)&&cActor.IsNeedToGiveBirth()&&
                        EmpireCraft.Core.RoyalHouseSystem.CanBirth(pActor))
                    {
                        BabyMaker.makeBaby(pActor, cActor); 
                        cActor.RecordGiveBirthTime();
                    }
                }
            });
        }
        if (pActor.Money > 25&&pActor.IsNeedToAddLover())
        {
            Random random = new Random();
            var possibility = random.NextDouble();
            if (possibility < 0.3f)
            {
                pActor.AddMoney(-25);
                // 原版的恋人与模组家族里的配偶/妾都要空着(只看原版会把别人的妻妾挑走)
                var actor = pActor.Realm.units.Find(a => a.data.sex == loverSex
                                                              && a.age <= 35 && a.isAdult()
                                                              && RealisticAgeSystem.OldEnoughToParent(a)
                                                              && !a.hasLover()
                                                              && a.GetPersonalIdentity()?.IsFreeToMarry(identity.id) != false
                                                              && !KinshipRules.AreCloseKin(pActor, a));
                if (actor == null) return BehResult.Continue;
                identity.setLover(actor, true);
            }
        }
        return BehResult.Continue;
    }
#endif
}
}
