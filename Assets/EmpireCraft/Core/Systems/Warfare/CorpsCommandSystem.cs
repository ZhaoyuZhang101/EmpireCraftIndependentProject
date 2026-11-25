// Generated from the EmpireCraft mod: Scripts/GeneralSystems/Warfare/CorpsCommandSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 军令(用户转玩家 2026-10-17 "可以对国家发动军事命令"，用户选"军令神力")：神力先点一座城选中它的国家，再点目标城——
//   · 本国的城：集中固守。分兵守城时这座城排第一，至少拿全国能上阵兵力的六成(攻势更大就按攻势)，余下的照常分守别处、出征；
//   · 交战敌国的城：集中攻打。出征的军都投这一段(城在敌境深处、还不是战线的，投离它最近的一段)，守城的兵照常先留够；
//   · 再点同一座城撤销；军令管到这场仗打完(本国不再有交战之敌、或攻打的城已不归敌国)。军令不存档。
// 见 FrontSystem.Assign、CorpsCommandPower。
public static class CorpsCommandSystem
{
    public const float HoldShare = 0.6f;
    private static readonly Dictionary<long, long> Holds = new(), Attacks = new();

    public static void ResetWorldState()
    {
        Holds.Clear();
        Attacks.Clear();
    }

    public static City HoldCity(Realm kingdom)
    {
        if (kingdom == null || !Holds.TryGetValue(kingdom.Key, out long id)) return null;
        City city = EmpireWorld.Current.CityByKey(id);
        if (city?.Record != null && !city.IsGone() && city.Realm == kingdom && kingdom.HasEnemies()) return city;
        Holds.Remove(kingdom.Key);
        return null;
    }

    public static City AttackCity(Realm kingdom)
    {
        if (kingdom == null || !Attacks.TryGetValue(kingdom.Key, out long id)) return null;
        City city = EmpireWorld.Current.CityByKey(id);
        if (city?.Realm != null && !city.IsGone() && kingdom.IsAtWarWith(city.Realm)) return city;
        Attacks.Remove(kingdom.Key);
        return null;
    }

    // 下令或撤销，返回提示
#if EC_PENDING
    public static string Command(Realm kingdom, City city)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || city?.Realm == null) return LM.Get("force_vassal_no_kingdom");
        if (city.Realm == kingdom)
        {
            if (HoldCity(kingdom) == city)
            {
                Holds.Remove(kingdom.Key);
                return string.Format(LM.Get("corps_command_hold_cancelled"), kingdom.GetKingdomName(), city.GetCityName());
            }
            if (!kingdom.HasEnemies()) return string.Format(LM.Get("corps_command_no_war"), kingdom.GetKingdomName());
            Holds[kingdom.Key] = city.Key;
            return string.Format(LM.Get("corps_command_hold"), kingdom.GetKingdomName(), city.GetCityName());
        }
        if (!kingdom.IsAtWarWith(city.Realm))
            return string.Format(LM.Get("corps_command_not_enemy"), kingdom.GetKingdomName(), city.Realm.GetKingdomName());
        if (AttackCity(kingdom) == city)
        {
            Attacks.Remove(kingdom.Key);
            return string.Format(LM.Get("corps_command_attack_cancelled"), kingdom.GetKingdomName(), city.GetCityName());
        }
        Attacks[kingdom.Key] = city.Key;
        return string.Format(LM.Get("corps_command_attack"), kingdom.GetKingdomName(), city.GetCityName());
    }
#endif

    // 军事页：这支军是不是正在奉令(固守或攻打军令所指的城)
#if EC_PENDING
    public static bool Ordered(Realm kingdom, CorpsData corps) =>
        corps != null && corps.sector_city_id >= 0 &&
        (corps.task == "defend" && HoldCity(kingdom)?.data?.id == corps.sector_city_id ||
         corps.task == "attack" && (AttackCity(kingdom) is City target &&
                                    (target.Key == corps.sector_city_id || FrontSystem.FocusSector(kingdom) == corps.sector_city_id)));
#endif
}
}
