// Generated from the EmpireCraft mod: Scripts/HelperFunc/YearlyStagger.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
namespace EmpireCraft.Core
{

// 按城市(或按对象)计时的"每年一次"任务错峰。
// 以前计时起点都是读档后的第一刻，所有城市在同一刻到期，每年集中爆发一次，造成大顿挫。
//   · 首次：计时起点随机往前拨 0~1 年，各城从一开始就错开；
//   · 每次结算后：下一次的起点加 ±10 秒抖动(一年 = 60 秒世界时间)，平均周期仍是一年；
//     旧存档里已经同步的城市过几年也会自然错开。
public static class YearlyStagger
{
    public const double YearSeconds = 60d;
    private const double Jitter = 20d;

    public static double Initial(double now) => now - EcRandom.value * YearSeconds;

    public static double Next(double now) => now + (EcRandom.value - 0.5) * Jitter;
}
}
