// Ported from the EmpireCraft mod: Scripts/GeneralSystems/RebellionStartupService.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public static class RebellionStartupService
{
    private const float MinMilitiaShare = 0.12f;
    private const float MaxMilitiaShare = 0.35f;
    private const int MinMilitia = 3;

    // 起事城市原先多是郡县，政体不许养兵、兵早被裁撤，起义时城里一个兵都没有；
    // 原版规则下没有守军的城市，任何一个敌兵进城十来秒就被收复，起义等于一冒头就被扑灭。
    // 起事时按民怨烈度(0~1)就地武装一部分成年居民为义军，并立刻组建军队。
    // 规模是守住本城、跟小股官军周旋的量级，真正的正规军压境仍然打不过。
    // cities：只在这些城征(叛军新得的城)；不给就是叛军的全部城
#if EC_PENDING
    public static int RaiseUprisingMilitia(Realm rebel, float fervor, IEnumerable<City> cities = null)
    {
        if (rebel == null || rebel.IsGone() || rebel.Cities == null) return 0;
        float share = MinMilitiaShare + (MaxMilitiaShare - MinMilitiaShare) * Math.Max(0f, Math.Min(1f, fervor));
        // 分封制下的小乱：义军减半(见 PettyRebellion)
        if (PettyRebellion.IsPetty(rebel)) share *= 0.5f;
        int raised = 0;
        foreach (City city in (cities ?? rebel.Cities).ToList())
        {
            if (city == null || city.IsGone() || city.units == null || city.Realm != rebel) continue;
            // 无小人模式：城里几乎没有平民实体，从背景人口里武装义军(每个兵代表一支义军)
            if (CityPopulationSystem.AbstractPopulationEnabled)
            {
                int militia = Math.Max(MinMilitia, Math.Min(15,
                    (int)Math.Ceiling(CityPopulationSystem.Households(city) * share * 0.3f)));
                raised += CityPopulationSystem.RaiseMilitia(city, militia, share);
            }
            var candidates = city.units.Where(actor => actor != null && actor.Alive && actor.isAdult() &&
                                                       actor.kingdom == rebel && !actor.isWarrior() &&
                                                       !actor.isKing() && !actor.isCityLeader() &&
                                                       actor.equipment != null && actor.CanFoundCivKingdom())
                .OrderByDescending(actor => actor.stats["health"]).ToList();
            int adults = city.units.Count(actor => actor != null && actor.Alive && actor.isAdult());
            // 只补足到目标人数：已有守军的封国起兵时不会再额外叠加
            int garrison = city.units.Count(actor => actor != null && actor.Alive && actor.isWarrior());
            int target = Math.Min(candidates.Count,
                Math.Max(0, Math.Max(MinMilitia, (int)Math.Ceiling(adults * share)) - garrison));
            for (int i = 0; i < target; i++)
            {
                city.makeWarrior(candidates[i]);
                raised++;
            }
            city.checkArmyExistence();
            if (!city.hasArmy()) EmpireCraftCityBehCheckArmy.CreateNewArmy(city);
            // 立刻重新评估军队(不等下一个月)，并清掉起事前官军留下的占领进度
            CityExtension.CityExtraData cityData = city.GetOrCreate();
            if (cityData != null) cityData.last_army_check_ts = -1d;
            city.clearCapture();
        }
        return raised;
    }

    public static void RollbackCitySplit(City seat, Realm origin, Realm rebel)
    {
        if (seat == null || origin == null || rebel == null || origin.IsGone() || seat.Realm != rebel) return;
        rebel.EndLocalRebelling();
        seat.JoinRealm(origin);
        if (seat.Realm != origin) return;
        EmpireWorld.Current.game_stats.data.citiesRebelled = Math.Max(0, EmpireWorld.Current.game_stats.data.citiesRebelled - 1);
        EmpireWorld.Current.map_stats.citiesRebelled = Math.Max(0, EmpireWorld.Current.map_stats.citiesRebelled - 1);
    }
#endif
}
}
