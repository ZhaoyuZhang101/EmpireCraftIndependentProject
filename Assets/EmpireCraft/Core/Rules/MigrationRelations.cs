// Generated from the EmpireCraft mod: Scripts/GeneralSystems/MigrationRelations.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 移民的外交面(用户 2026-10-19 选"影响好感并有难民"；以前开放移民不经邻国同意就抽人、清除移民把人倒到国外，都没有后果)：
//   · 开放移民从邻国抽人：被抽走人口的邻国(帝国按核心)十年内对本国好感 −25；
//   · 清除移民：被赶出去的人送回同文化的邻国，没有就涌入接壤的外国城做难民——接收国十年内对驱逐国好感 −40，
//     接收难民的城五年内稳定度少 6(难民涌入，见 CityStabilitySystem.Natural)。
public static class MigrationRelations
{
    private const int GrievanceYears = 10, RefugeeYears = 5;
    private const int DrainedOpinion = -25, ExpelledOpinion = -40;
    public const float RefugeeStabilityLoss = 6f;

    private static Realm ModRealm(Realm kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    public static void Drained(Realm victim, Realm culprit) => Note(victim, culprit, expelled: false);

    public static void Expelled(Realm receiver, Realm culprit) => Note(receiver, culprit, expelled: true);

    private static void Note(Realm victim, Realm culprit, bool expelled)
    {
        Realm a = ModRealm(victim), b = ModRealm(culprit);
        if (a?.Record == null || b?.Record == null || a == b || EmpireWorld.Current == null) return;
        var data = a.GetOrCreate();
        Dictionary<long, double> book = expelled
            ? data.migration_expelled ??= new Dictionary<long, double>()
            : data.migration_drained ??= new Dictionary<long, double>();
        book[b.Key] = GameClock.Now;
    }

    // 好感(见 EmpireCraftOpinionAddition)：main 对 target
    public static int Opinion(Realm main, Realm target)
    {
        Realm a = ModRealm(main), b = ModRealm(target);
        if (a?.Record == null || b?.Record == null || a == b) return 0;
        var data = a.GetOrCreate();
        if (Recent(data.migration_expelled, b.Key, GrievanceYears)) return ExpelledOpinion;
        return Recent(data.migration_drained, b.Key, GrievanceYears) ? DrainedOpinion : 0;
    }

    private static bool Recent(Dictionary<long, double> book, long id, int years) =>
        book != null && book.TryGetValue(id, out double at) && at >= 0d && GameClock.YearsSince(at) < years;

    public static void TakeRefugees(City city)
    {
        if (city?.Record != null && EmpireWorld.Current != null) city.GetOrCreate().refugees_at = GameClock.Now;
    }

    public static float RefugeeLoss(City city)
    {
        if (city?.Record == null) return 0f;
        double at = city.GetOrCreate().refugees_at;
        return at >= 0d && GameClock.YearsSince(at) < RefugeeYears ? RefugeeStabilityLoss : 0f;
    }

    public static string Describe() => LM.Get("stability_refugee_detail");
}
}
