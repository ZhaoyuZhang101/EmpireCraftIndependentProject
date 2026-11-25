// Ported from the EmpireCraft mod: Scripts/GeneralSystems/DynasticCycleSystem.cs#DynasticCycleSystem.Transition @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

public static partial class DynasticCycleSystem
{
    // 改朝换代的三档(用户 2026-10-26)
    public enum Transition
    {
        // 清空：外姓打进京城、外藩入继这类真正的改朝换代——王朝积弊留四分之一(至多 20)、六弊各留两成、派系失衡留三成，王朝年岁从头算
        Clear,
        // 减半：军阀兵变禅让上位(赵匡胤式)、宗室另立或复国(东晋、南宋式)——王朝积弊、六弊减半，王朝年岁从头算，不另给开国气象
        Halve,
        // 保留：权臣篡位、外戚代立(司马炎式)——王朝积弊、六弊原样，王朝年岁接着算(以前归零，按新朝慢速滋生，几十年就消化掉)
        Keep
    }
}
}
