// Generated from the EmpireCraft mod: Scripts/AI/CityAI/EmpireCraftCityBehCheckJoinRebellion.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Linq;

namespace EmpireCraft.Core
{

public class EmpireCraftCityBehCheckJoinRebellion: GameAICityBase
{
    public override Type OriginalBeh => GetType();

    // 这一步排在模组城市大任务"do_mod_city_beh"里(见 GameAIMain.CityAIs)，返回 Stop 会结束整个任务、后面各步都不跑：
    // 只有已毁、无主的城整座不跑模组城市 AI 才用 Stop。帝国都城只是不投叛军，后面的律令、城主、官职、收税、法理首都、
    // 合并民居照常(以前这里也返回 Stop，帝国都城这些步一直轮不到)
#if EC_PENDING
    public override BehResult Execute(City pCity)
    {
        if (pCity.IsGone()) return BehResult.Stop;
        if (pCity.isNeutral()) return BehResult.Stop;
        if (pCity.IsCapital()&&pCity.Realm.IsEmpire()) return BehResult.Continue;
        if (!RebellionSystem.CanAttempt(pCity.Realm) || !CityStabilitySystem.CanRise(pCity)) return BehResult.Continue;
        int loyalty = pCity.getLoyalty();
        if (loyalty > 38) return BehResult.Continue;
        CityValueSnapshot cityValue = pCity.GetCityStrategicValue();
        if (loyalty > CityValueRules.GetIsolationRebellionLoyaltyThreshold(cityValue)) return BehResult.Continue;
        var initKingdom = pCity.Realm;
        var initCore = initKingdom.GetEmpire()?.CoreKingdom;
        var target = pCity.NeighbourCities.ToList().Find(c =>
        {
            if (c.IsGone()) return false;
            if (c.isNeutral()) return false;
            if (!c.HasRealm()) return false;
            var targetKingdom = c.Realm;
            if (initKingdom == targetKingdom) return false;
            var targetWar = targetKingdom.GetWars().ToList().Find(w =>
                (w.GetEmpireWarType() == EmpireWarType.地方叛乱 || w.GetEmpireWarType() == EmpireWarType.地方独立) &&
                w.MainAttacker == targetKingdom && (w.MainDefender == initKingdom || initCore != null && w.MainDefender == initCore));
            if (targetWar == null) return false;
            return true;
        });
        if (target == null) return BehResult.Continue;
        var targetKingdom = target.Realm;
        if (targetKingdom.CountCities()>=targetKingdom.getMaxCities()*2) return BehResult.Continue;
        pCity.JoinRealm(target.Realm);
        return  BehResult.Continue;
    }
#endif
}
}
