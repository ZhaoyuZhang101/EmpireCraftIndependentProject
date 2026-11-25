// Ported from the EmpireCraft mod: Scripts/HelperFunc/MoneyDisplay.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Globalization;

namespace EmpireCraft.Core
{

public static class MoneyDisplay
{
#if EC_PENDING
    public static string Format(double amount) => Format(amount,
        PlayerConfig.dict.TryGetValue("language", out var language) && language.stringVal == "en");
#endif

    public static string Format(double amount, bool english)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount)) return "0";
        double absolute = Math.Abs(amount), scale = 1d;
        string suffix = "";
        if (english)
        {
            if (absolute >= 1e9) { scale = 1e9; suffix = "B"; }
            else if (absolute >= 1e6) { scale = 1e6; suffix = "M"; }
            else if (absolute >= 1e3) { scale = 1e3; suffix = "K"; }
        }
        else
        {
            if (absolute >= 1e8) { scale = 1e8; suffix = LM.Get("money_unit_hundred_million"); }
            else if (absolute >= 1e4) { scale = 1e4; suffix = LM.Get("money_unit_ten_thousand"); }
        }
        return (amount / scale).ToString("0.##", CultureInfo.InvariantCulture) + suffix;
    }
}
}
