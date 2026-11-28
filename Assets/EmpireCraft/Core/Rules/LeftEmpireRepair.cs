// Generated from the EmpireCraft mod: Scripts/GeneralSystems/LeftEmpireRepair.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Linq;

namespace EmpireCraft.Core
{

// 读档时补一次已经脱离帝国的行政区(用户 2026-11-16 "那为什么脱离了还是省"，要求补一次)：
//   · 以前普通的省离开帝国不改名(只改带政区名的都护府这类)，独立后又一直在打仗，定期改名被"交战不改名"挡着，
//     一直叫"某某省"；现在离开时当场改(见 KingdomExtension.EmpireLeave)，旧档里早离开的这里补改；
//   · 独立服务器以前没有国家颜色库，离开帝国时换回自己颜色那一步被跳过，一直顶着原帝国的颜色；补换一种(避开原帝国核心的颜色)。
// 只动不在帝国里、记着离开过哪个帝国(left_empire_id)的国；附庸、朝贡、傀儡这些从属之国套宗主色是有意的，不动颜色。
// 补过的不再满足条件，每次读档跑也只动没补过的
public static partial class LeftEmpireRepair
{
#if EC_REPLACED // written by hand for this game: see Assets/EmpireCraft/Core/Seams
    public static void AfterLoad()
    {
        if (EmpireWorld.Current?.kingdoms == null) return;
        int renamed = 0, recolored = 0;
        foreach (Realm kingdom in EmpireWorld.Current.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.IsGone() || kingdom.wild || AncientWarfareCompatibility.Owns(kingdom)) continue;
            if (kingdom.IsInEmpire() || kingdom.IsEmpire()) continue;
            var data = kingdom.GetOrCreate();
            if (data.left_empire_id < 0) continue;
            try
            {
                Empire former = ModClass.EMPIRE_MANAGER?.get(data.left_empire_id);
                Realm core = former?.CoreKingdom;
                if (core?.data != null && !core.IsGone() && core != kingdom && kingdom.data.color_id == core.data.color_id &&
                    !EmpireFormationService.IsSubject(kingdom))
                {
                    int before = kingdom.data.color_id;
                    KingdomColorService.Restore(kingdom, -1, core.getColor());
                    if (kingdom.data.color_id != before) recolored++;
                }
                if (kingdom.IsAdministrativeKingdomType() || !string.IsNullOrWhiteSpace(data.admin_name) || StaleAdministrativeName(kingdom))
                {
                    string before = kingdom.data.name;
                    EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom, afterIndependence: true);
                    if (kingdom.data.name != before) renamed++;
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][脱离帝国] 补改 {kingdom.data.name} 失败: {exception.Message}");
            }
        }
        if (renamed + recolored > 0)
            LogService.LogInfo($"[EmpireCraft][脱离帝国] 读档补改：{renamed} 国改了名，{recolored} 国换回了自己的颜色");
    }
#endif

    // 名字跟不上类别("陇右省"早已是王国)：独立后一直交战，定期改名被"交战不改名"挡着。
    // 后缀按年代、文化各有叫法(明清的省、唐的道)，不逐个对，只看名字是不是以眼下类别的后缀结尾；
    // 补改走的是平时定期改名的同一套(SyncKingdomStatus)，本来就该叫别的(军阀、现代国名)的它自己会定
#if EC_PENDING
    private static bool StaleAdministrativeName(Realm kingdom)
    {
        if (kingdom.Record.custom_name) return false;
        string name = Plain(kingdom.Name), current = Plain(kingdom.GetKingdomTypeSuffixText());
        return name.Length > 0 && current.Length > 0 && !name.EndsWith(current, StringComparison.Ordinal);
    }
#endif

    private static string Plain(string text) =>
        string.IsNullOrEmpty(text) ? "" : new string(text.Where(ch => !char.IsWhiteSpace(ch) && ch != ' ' && ch != '​').ToArray());
}
}
