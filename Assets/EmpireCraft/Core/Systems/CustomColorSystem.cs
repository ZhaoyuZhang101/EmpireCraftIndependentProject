// Ported from the EmpireCraft mod: Scripts/GeneralSystems/CustomColorSystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using ColorUtility = EmpireCraft.Core.EcColorUtility;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 自定义颜色：国家、帝国可以自己定一种地图色(色值如 #3A6EA5，存在 KingdomExtraData / EmpireData.custom_color)。
// 原版的颜色按调色板序号存，塞进调色板外的颜色读档会越界，所以不改原版的颜色数据——
// 只在画地图色块时把底色、边框换成自定义的(见 PureMapPatch.BeforeApplyColors)，国家图层、帝国图层都生效；
// 铭牌、旗帜仍是原版的颜色。改了以后整张图重画一遍。
public static class CustomColorSystem
{
#if EC_PENDING
    private static readonly Dictionary<string, ColorAsset> Assets = new();
#endif
    private static int _count = -1;
    private static object _world;

    // 有没有任何国家、帝国定了自定义颜色(没有就不必每块地都查)
#if EC_PENDING
    public static bool Any
    {
        get
        {
            if (!ReferenceEquals(_world, EmpireWorld.Current))
            {
                _world = EmpireWorld.Current;
                _count = -1;
            }
            if (_count >= 0) return _count > 0;
            _count = 0;
            foreach (Realm kingdom in EmpireWorld.Current?.kingdoms?.list ?? new List<Realm>())
                if (!string.IsNullOrEmpty(kingdom?.Record == null ? null : kingdom.GetOrCreate().custom_color)) _count++;
            if (ModClass.EMPIRE_MANAGER != null)
                foreach (Empire empire in ModClass.EMPIRE_MANAGER)
                    if (!string.IsNullOrEmpty(empire?.data?.custom_color)) _count++;
            return _count > 0;
        }
    }
#endif

    public static string Hex(Realm kingdom) => kingdom?.Record == null ? "" : kingdom.GetOrCreate().custom_color ?? "";
    public static string Hex(Empire empire) => empire?.data?.custom_color ?? "";

    // 地图上这个国家、帝国用的自定义颜色(没定返回 null)
#if EC_PENDING
    public static ColorAsset Of(object meta)
    {
        // 帝国的颜色就是核心王国的颜色：核心王国自己没定时，国家图层上也用帝国定的
        string hex = meta switch
        {
            Realm kingdom => !string.IsNullOrEmpty(Hex(kingdom)) ? Hex(kingdom)
                : kingdom.IsEmpire() ? Hex(kingdom.GetEmpire()) : "",
            Empire empire => Hex(empire),
            _ => ""
        };
        if (string.IsNullOrEmpty(hex)) return null;
        if (Assets.TryGetValue(hex, out ColorAsset asset)) return asset;
        asset = ColorAsset.tryMakeNewColorAsset(hex);
        asset.initColor();
        Assets[hex] = asset;
        return asset;
    }
#endif

    // 读输入：#RRGGBB 或 RRGGBB；留空为恢复原版颜色
    public static bool TryParse(string text, out string hex)
    {
        hex = "";
        string value = (text ?? "").Trim().TrimStart('#');
        if (value.Length == 0) return true;
        if (value.Length != 6 || !ColorUtility.TryParseHtmlString("#" + value, out _)) return false;
        hex = "#" + value.ToUpperInvariant();
        return true;
    }

#if EC_PENDING
    public static bool Set(Realm kingdom, string text)
    {
        if (kingdom?.Record == null || !TryParse(text, out string hex)) return false;
        kingdom.GetOrCreate().custom_color = hex;
        Changed();
        return true;
    }

    public static bool Set(Empire empire, string text)
    {
        if (empire?.data == null || !TryParse(text, out string hex)) return false;
        empire.data.custom_color = hex;
        Changed();
        return true;
    }

    private static void Changed()
    {
        _count = -1;
        EmpireWorld.Current?.zone_calculator?.dirtyAndClear();
    }
#endif
}
}
