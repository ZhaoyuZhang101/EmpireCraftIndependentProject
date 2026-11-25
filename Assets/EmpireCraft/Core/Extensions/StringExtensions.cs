// Generated from the EmpireCraft mod: Scripts/GameClassExtensions/StringExtensions.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Color = EmpireCraft.Core.EcColor;
using System;

namespace EmpireCraft.Core
{

public static class StringExtensions
{
    /// <summary>
    /// 将颜色赋值于文本
    /// </summary>
    public static string ColorString(this string content, string color= "", Color pColor = default)
    {
        if (String.IsNullOrEmpty(color))
        {
            return $"<color={pColor.ToHexString()}>{content}</color>";
        }
        return $"<color={color}>{content}</color>";
    }
    /// <summary>
    /// 格式化本地化文件中的语句
    /// </summary>
    public static string LocalFormat(this string content, params object[] additions)
    {
        return string.Format(LM.Get(content), additions);
    }
}
}
