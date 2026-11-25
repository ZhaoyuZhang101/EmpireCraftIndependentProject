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
#if EC_PENDING
    public static void Mark(Alliance alliance)
    {
        if (alliance == null || !alliance.Alive) return;
        ModClass.MOD_ALLIANCE_IDS.Add(alliance.id);
    }

    public static bool IsModAlliance(Alliance alliance) =>
        alliance != null && ModClass.MOD_ALLIANCE_IDS.Contains(alliance.id);
#endif

    public static bool IsVanillaAllianceBanned() =>
        WorldLaws.BanVanillaAlliance?.IsEnabled() == true;

    // 旧存档里模组同盟还没有标记：成员中有两国同属一位君主的(共主联盟、城邦同盟)视为模组同盟并补上标记
#if EC_PENDING
    private static bool LooksLikeUnionAlliance(Alliance alliance)
    {
        List<Person> kings = alliance.kingdoms_hashset
            .Where(kingdom => kingdom != null && kingdom.Alive && kingdom.HasRuler())
            .Select(kingdom => kingdom.king).ToList();
        return kings.Count != kings.Distinct().Count();
    }
#endif

    // 帝国与同盟不兼容：帝国本身及其成员国一律退出同盟(同盟剩不足两国时原版会自行解散)
#if EC_PENDING
    public static void LeaveAllianceForEmpire(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone() || !kingdom.hasAlliance()) return;
        kingdom.getAlliance().leave(kingdom);
    }
#endif

    // 定期调用：清掉已在帝国里(或本身是帝国)的同盟成员
#if EC_PENDING
    public static void RemoveEmpireMembersFromAlliances()
    {
        if (EmpireWorld.Current?.alliances == null) return;
        foreach (Alliance alliance in EmpireWorld.Current.alliances.list.ToList())
        {
            if (alliance == null || !alliance.Alive) continue;
            // 同盟里有国家成了帝国：整个同盟解散(与称帝时的处理一致)
            if (alliance.kingdoms_hashset.Any(member => member != null && !member.IsGone() && member.IsEmpire()))
            {
                EmpireWorld.Current.alliances.dissolveAlliance(alliance);
                continue;
            }
            foreach (Realm member in alliance.kingdoms_hashset.ToList())
                if (member != null && !member.IsGone() && member.IsInEmpire())
                    alliance.leave(member);
        }
    }
#endif

    // 世界规则打开时调用：解散原版同盟
#if EC_PENDING
    public static void DissolveVanillaAlliances()
    {
        if (EmpireWorld.Current?.alliances == null) return;
        foreach (Alliance alliance in EmpireWorld.Current.alliances.list.ToList())
        {
            if (alliance == null || !alliance.Alive || alliance.isForcedType()) continue;
            if (IsModAlliance(alliance)) continue;
            if (LooksLikeUnionAlliance(alliance))
            {
                Mark(alliance);
                continue;
            }
            EmpireWorld.Current.alliances.dissolveAlliance(alliance);
        }
        ModClass.MOD_ALLIANCE_IDS.RemoveWhere(id => EmpireWorld.Current.alliances.get(id) == null);
    }
#endif
}
}
