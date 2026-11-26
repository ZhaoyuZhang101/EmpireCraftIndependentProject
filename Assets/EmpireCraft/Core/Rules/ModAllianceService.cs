// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ModAllianceService.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 模组同盟的标记：共主联盟、城邦同盟、反帝同盟等由模组机制建立的同盟记在 ModClass.MOD_ALLIANCE_IDS 里(随存档保存)。
// 世界规则"禁止原版结盟"打开时，解散所有未标记的同盟；玩家用神力强制结成的同盟也保留。
public static class ModAllianceService
{
    public static void Mark(Alliance alliance)
    {
        if (alliance == null || !alliance.Alive) return;
        ModClass.MOD_ALLIANCE_IDS.Add(alliance.Key);
    }

    public static bool IsModAlliance(Alliance alliance) =>
        alliance != null && ModClass.MOD_ALLIANCE_IDS.Contains(alliance.Key);

    public static bool IsVanillaAllianceBanned() =>
        WorldLaws.BanVanillaAlliance?.IsEnabled() == true;

    // 旧存档里模组同盟还没有标记：成员中有两国同属一位君主的(共主联盟、城邦同盟)视为模组同盟并补上标记
    private static bool LooksLikeUnionAlliance(Alliance alliance)
    {
        List<Person> kings = alliance.MemberSet
            .Where(kingdom => kingdom != null && kingdom.Alive && kingdom.HasRuler())
            .Select(kingdom => kingdom.Ruler).ToList();
        return kings.Count != kings.Distinct().Count();
    }

    // 帝国与同盟不兼容：帝国本身及其成员国一律退出同盟(同盟剩不足两国时原版会自行解散)
    public static void LeaveAllianceForEmpire(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone() || !kingdom.HasAlliance()) return;
        kingdom.GetAlliance().Leave(kingdom);
    }

    // 定期调用：清掉已在帝国里(或本身是帝国)的同盟成员
    public static void RemoveEmpireMembersFromAlliances()
    {
        if (EmpireWorld.Current?.LiveAlliances == null) return;
        foreach (Alliance alliance in EmpireWorld.Current.LiveAlliances.ToList())
        {
            if (alliance == null || !alliance.Alive) continue;
            // 同盟里有国家成了帝国：整个同盟解散(与称帝时的处理一致)
            if (alliance.MemberSet.Any(member => member != null && !member.IsGone() && member.IsEmpire()))
            {
                EmpireWorld.Current.DissolveAlliance(alliance);
                continue;
            }
            foreach (Realm member in alliance.MemberSet.ToList())
                if (member != null && !member.IsGone() && member.IsInEmpire())
                    alliance.Leave(member);
        }
    }

    // 世界规则打开时调用：解散原版同盟
#if EC_PENDING
    public static void DissolveVanillaAlliances()
    {
        if (EmpireWorld.Current?.LiveAlliances == null) return;
        foreach (Alliance alliance in EmpireWorld.Current.LiveAlliances.ToList())
        {
            if (alliance == null || !alliance.Alive || alliance.isForcedType()) continue;
            if (IsModAlliance(alliance)) continue;
            if (LooksLikeUnionAlliance(alliance))
            {
                Mark(alliance);
                continue;
            }
            EmpireWorld.Current.DissolveAlliance(alliance);
        }
        ModClass.MOD_ALLIANCE_IDS.RemoveWhere(id => EmpireWorld.Current.AllianceByKey(id) == null);
    }
#endif
}
}
