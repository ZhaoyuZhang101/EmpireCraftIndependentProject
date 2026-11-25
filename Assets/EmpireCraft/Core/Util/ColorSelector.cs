// Ported from the EmpireCraft mod: Scripts/HelperFunc/ColorSelector.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Color = EmpireCraft.Core.EcColor;
using ColorUtility = EmpireCraft.Core.EcColorUtility;
using System.Collections.Generic;
using System.Linq;
#if EC_PENDING
using Random = UnityEngine.Random;
#endif

namespace EmpireCraft.Core
{

public static class ColorSelector
{
#if EC_PENDING
    public static string NextColor()
    {
        Color randomColor = new Color(Random.value, Random.value, Random.value);
        Color pastel = Color.Lerp(randomColor, Color.white, 0.5f);
        return pastel.ToHexString();
    }
#endif
    /// <summary>
    /// 将 Color 转为 "#RRGGBBAA" 格式的十六进制字符串
    /// </summary>
#if EC_PENDING
    public static string ToHexString(this Color color, bool includeAlpha = true)
    {
        // ColorUtility 会返回不带 “#” 的纯十六进制码
        string hex = includeAlpha
            ? ColorUtility.ToHtmlStringRGBA(color)  // 包含透明度
            : ColorUtility.ToHtmlStringRGB(color);  // 不包含透明度

        return "#" + hex;
    }
#endif
}
}
