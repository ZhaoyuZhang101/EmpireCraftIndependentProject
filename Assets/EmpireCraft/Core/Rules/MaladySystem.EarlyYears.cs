// Ported from the EmpireCraft mod: Scripts/GeneralSystems/MaladySystem.cs#MaladySystem.EarlyYears,MaladySystem.EarlyPace,MaladySystem.PaceAt @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

public static partial class MaladySystem
{
    // 积弊滋生的快慢(历史周期律)：开国气象里不滋生；立国头 80 年二成，80~160 年四成五，之后全速
    // (用户 2026-10-21 选"放慢衰落"：以前 50/100 年、0.25/0.6，百年时几项同时加速，平庸之君的王朝 110 年正统就跌破 40)
    private const int EarlyYears = 80, MiddleYears = 160;
}

public static partial class MaladySystem
{
    private const float EarlyPace = 0.2f, MiddlePace = 0.45f;
}

public static partial class MaladySystem
{
    // 王朝年岁的快慢(不含开国气象与天命动荡)；王朝积弊的同岁数补底也照它算(见 DynasticCycleRules.AgeFloor)
    public static float PaceAt(int age) => age < EarlyYears ? EarlyPace : age < MiddleYears ? MiddlePace : 1f;
}
}
