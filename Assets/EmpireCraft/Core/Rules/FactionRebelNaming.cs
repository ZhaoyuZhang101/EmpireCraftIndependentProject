// Generated from the EmpireCraft mod: Scripts/GeneralSystems/FactionRebelNaming.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 派系起兵的称呼(用户 2026-10-22 见几个响应的行省同叫"世族"分不清，问"有没有更符合历史的称呼"，
// 选"统兵者官爵""按首领与家数")：
//   · 叛军：统兵者官爵——本国官职的称呼(见 OfficeObject.GetName)：宗室封王称王号(燕王)、节度使称镇(淮西节度使)、
//     行省称官衔(山东巡抚)、州郡称官(某州刺史)；起兵那一刻取，停战改回原名；没有官职的称"地名+军"；
//   · 战争：一家起兵，宗室王称"某王之乱"，余称首领姓名(王敦之乱、侯景之乱式)；两家合称，王取封号首字(燕赵)、余取两人之姓(安史)；
//     三家以上按数称(七国、八王、三藩式)，字按多数人的官称：王、镇、省、州，余称国。
public static class FactionRebelNaming
{
    private enum Kind { Prince, Garrison, Province, Prefecture, ModRealm }

    private static readonly string[] Digits = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

    // 叛军的称呼(起兵前调：要按本国原来的名号与官职算)。官称直接拼在地名后面，只合中文；别的语言称"某地军"
    public static string RebelName(Realm kingdom)
    {
        string title = Chinese() ? Title(kingdom) : null;
        if (!string.IsNullOrWhiteSpace(title)) return title;
        string front = kingdom.GetKingdomName();
        if (string.IsNullOrWhiteSpace(front)) front = kingdom.Capital?.GetCityName() ?? kingdom.Name ?? "";
        return string.Format(LM.Get("faction_rebel_army_name"), front);
    }

    // 这场派系起兵的战争名
#if EC_PENDING
    public static string WarName(IReadOnlyList<Realm> rebels)
    {
        List<Realm> list = rebels?.Where(kingdom => kingdom?.Record != null).Distinct().ToList() ?? new List<Realm>();
        if (list.Count == 0) return null;
        if (list.Count == 1)
        {
            Realm only = list[0];
            string title = Title(only);
            string who = KindOf(only, title) == Kind.Prince ? title : only.Ruler?.FullName();
            if (string.IsNullOrWhiteSpace(who)) who = !string.IsNullOrWhiteSpace(title) ? title : only.GetKingdomName();
            return string.Format(LM.Get("faction_rebellion_war_name"), who);
        }
        if (list.Count == 2)
        {
            string a = Short(list[0]), b = Short(list[1]);
            if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && a != b)
                return string.Format(LM.Get("faction_war_pair"), a, b);
        }
        Kind kind = list.Select(kingdom => KindOf(kingdom, Title(kingdom))).GroupBy(k => k).OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key).First().Key;
        return string.Format(LM.Get("faction_war_many"), Number(list.Count), LM.Get("faction_war_unit_" + kind.ToString().ToLowerInvariant()));
    }
#endif

    // 本国官职的称呼(地名 + 官称)
    private static string Title(Realm kingdom)
    {
        try
        {
            return kingdom?.Record == null ? null : kingdom.GetOffice()?.GetName(kingdom);
        }
        catch
        {
            return null;
        }
    }

    // 两家合称时的一字：王取封号首字，余取首领之姓
#if EC_PENDING
    private static string Short(Realm kingdom)
    {
        string title = Title(kingdom);
        if (KindOf(kingdom, title) == Kind.Prince && title.Length > 1) return title.Substring(0, 1);
        Person king = kingdom.Ruler;
        string surname = king == null ? null : SurnameInheritanceSystem.SurnameOf(king.GetPersonalIdentity());
        string name = king?.FullName();
        if (string.IsNullOrWhiteSpace(surname) && !string.IsNullOrEmpty(name)) surname = name.Substring(0, 1);
        return surname;
    }
#endif

    // 按政区类别分(以前按官称字样猜，繁体、别朝官名都对不上，多半落成"国")；宗室封王看官称是不是"某王"
#if EC_PENDING
    private static Kind KindOf(Realm kingdom, string title)
    {
        if (!string.IsNullOrWhiteSpace(title) && title.EndsWith("王")) return Kind.Prince;
        switch (kingdom?.GetOrCreate().kingdomType)
        {
            case KingdomType.LvLing_jiedushi:
            case KingdomType.LvLing_duhufu:
                return Kind.Garrison;
            case KingdomType.Arabic_province:
            case KingdomType.Modern_province:
            case KingdomType.Modern_state:
                return Kind.Province;
            case KingdomType.LvLing_province:
                // 行省制下的州郡称省，县道制下称州(官称里带省、抚、督的是行省)
                return title != null && (title.Contains("省") || title.Contains("巡") || title.Contains("督") || title.Contains("布政"))
                    ? Kind.Province : Kind.Prefecture;
            case KingdomType.LvLing_jimizhou:
            case KingdomType.Modern_autonomous_prefecture:
                return Kind.Prefecture;
            default:
                return Kind.Realm;
        }
    }
#endif

    private static bool Chinese()
    {
        string language = GameOptions.All != null && GameOptions.All.TryGetValue("language", out GameOption option)
            ? option.StringVal : "";
        return language is "cz" or "ch";
    }

    // 家数：中文界面用汉字(七国、八王；两家称"两"不称"二")，其余用阿拉伯数字
    private static string Number(int count)
    {
        if (!Chinese()) return count.ToString();
        if (count == 2) return "两";
        if (count < 10) return Digits[count];
        if (count < 20) return "十" + Digits[count % 10];
        if (count < 100) return Digits[count / 10] + "十" + Digits[count % 10];
        return count.ToString();
    }
}
}
