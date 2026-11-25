// Ported from the EmpireCraft mod: Scripts/GeneralSystems/WildernessNaming.cs#WildernessSetting @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 文化配置里的无人区命名(CultureRule.jsonc 的 setting.Wilderness，见 WildernessNaming)：
// 类型(mountain 山脉、desert 沙漠、tundra 冻原、jungle 雨林、swamp 大泽、lava 熔岩) → 词库名，词库是本文件夹 <文化><词库名>.csv，
// 可写"别的文化:词库名"；没配的用 Locales/Cultures/Wilderness/ 的通用词库
public class WildernessSetting
{
    // 地名(太行、撒哈拉；末尾写"!"的是整名，不接后缀)；默认 Wilderness/Wild<类型>.csv(如 WildMountain)
    public Dictionary<string, string> words;
    // 类型后缀(山脉、沙漠，可写 {0} 代入地名)；默认 Wilderness/Wild<类型>Suffix.csv
    public Dictionary<string, string> suffix;
    // 大区域的后缀(大沙漠、大泽)；没配的照本文化的 suffix，两样都没配用 Wilderness/Wild<类型>Great.csv
    public Dictionary<string, string> great_suffix;
    // 方位字头(北{0}、南{0}……)，地名用完了才加；默认 Wilderness/WildDirections.csv
    public string directions = "";
}
}
