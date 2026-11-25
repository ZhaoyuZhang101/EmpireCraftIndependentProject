// Generated from the EmpireCraft mod: Scripts/GeneralSystems/DeJureAgenda.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 先收法理再扩张(世界法则 empirecraft_law_dejure_first，默认开)：
// 玩家 2026-10-21 建议"版图按照法理统一后再考虑扩张，要不然真的很乱"，用户选"优先打法理"。
// 挑宣战目标时(独立王国见 WarTargetPatch，帝国与成员见 EmpireCraftPlotsAddition.GetWarTarget，派系"扩张地盘")，
// 占着本国(帝国按整个帝国算)法理城的邻国排在前面；开打后战争目标定为"收复"，议和归还这些城(见 WarGoalSystem.Claimed)。
// 只调先后：没有能打的法理目标时照常扩张，不会因此不打仗。
// 用户 2026-10-26 "感觉所有国家都没有按照法理扩张"，问答选"先打能补完自己法理的，再就近"：挑目标依次为
// 占着本国一方有宣称的城的(宣称口径见 WarGoalSystem.ClaimsTitle)→ 占着本国所在帝国法理里城的(见 HoldsCoreOf)→ 原版就近。
public static class DeJureAgenda
{
#if EC_PENDING
    public static bool Enabled => EmpireCraftWorldLawLibrary.empirecraft_law_dejure_first?.isEnabled() == true;
#endif

    // target 占着 initiator 一方法理的城
#if EC_PENDING
    public static bool HasClaimOn(Realm initiator, Realm target) =>
        Enabled && initiator?.Record != null && target?.Record != null && initiator != target &&
        WarGoalSystem.Claimed(initiator, target.Cities).Count > 0;
#endif

    // target 占着 initiator 所在帝国法理里的城(在帝国里的按本帝国的帝国法理，不在的按本国都城所在的帝国法理，兼属的也算)
#if EC_PENDING
    public static bool HoldsCoreOf(Realm initiator, Realm target)
    {
        if (!Enabled || initiator?.Record == null || target?.Cities == null || initiator == target) return false;
        var cores = new HashSet<EmpireCore>();
        Empire empire = initiator.GetEmpire();
        if (empire != null && !empire.IsGone() && EmpireCoreManager.Get(empire) is EmpireCore own) cores.Add(own);
        else if (initiator.Capital != null)
            foreach (EmpireCore core in DeJureShareSystem.CoresOf(initiator.Capital)) cores.Add(core);
        if (cores.Count == 0) return false;
        foreach (City city in target.Cities)
            foreach (EmpireCore core in DeJureShareSystem.CoresOf(city))
                if (cores.Contains(core)) return true;
        return false;
    }
#endif
}
}
