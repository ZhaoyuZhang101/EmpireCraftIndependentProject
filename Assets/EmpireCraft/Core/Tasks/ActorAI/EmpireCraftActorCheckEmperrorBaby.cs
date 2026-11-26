// Generated from the EmpireCraft mod: Scripts/AI/ActorAI/EmpireCraftActorCheckEmperrorBaby.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Linq;

namespace EmpireCraft.Core
{

public class EmpireCraftActorCheckEmperorBaby: GameAIActorBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult Execute(Person pActor)
    {
        if (!pActor.IsEmperor()) return BehResult.Continue;
        if (!pActor.HasSpouse()) return BehResult.Continue;
        if (!pActor.IsAdult()) return BehResult.Continue;
        if (!pActor.HasSpecificClan()) return BehResult.Continue;
        var sc = pActor.GetSpecificClan();
        var pc = pActor.GetPersonalIdentity();
        if (sc == null || pc==null) return BehResult.Continue;
        pc.is_main = true;
        if (sc.GetChildren(pc).Any(c => c.Item2.CanHeir(pc))) return BehResult.Continue;
        // 一年半至多一个、不育的生不了(见 RoyalHouseSystem)：不再保证代代有子
        if (!EmpireCraft.Core.RoyalHouseSystem.CanBirth(pActor)) return BehResult.Continue;
        var lover = pActor.Spouse;
        // 原版几岁就算成年：君主与配偶都到生养的年纪才强制生育(见 RealisticAgeSystem.MinParentAge)
        if (!EmpireCraft.Core.RealisticAgeSystem.OldEnoughToParent(pActor) ||
            !EmpireCraft.Core.RealisticAgeSystem.OldEnoughToParent(lover) ||
            !EmpireCraft.Core.RealisticAgeSystem.YoungEnoughToParent(pActor) ||
            !EmpireCraft.Core.RealisticAgeSystem.YoungEnoughToParent(lover)) return BehResult.Continue;
        var lsc = lover.GetPersonalIdentity();
        if (lsc == null) return BehResult.Continue;
        lsc.is_main = false;
        //无子嗣强制生育
        var baby = BabyMaker.makeBaby(pActor, lover,
            sc.clan_sex_priority == SpecificClanType.FemalePriority ? Sex.Female : Sex.Male);
        return BehResult.Continue;
    }
#endif
}
}
