// Ported from the EmpireCraft mod: Scripts/GameClassExtensions/MetaTypeExtension.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
namespace EmpireCraft.Core
{

public static class MetaTypeExtension
{
#if EC_PENDING
    public const MetaType Empire = (MetaType)100;
    public const MetaType KingdomTitle = (MetaType)101;
#endif
    // 理念图层(独立于原版宗教)
#if EC_PENDING
    public const MetaType Ideology = (MetaType)102;
#endif
    // 民族情绪图层(见 Layer/NationMap.cs)
#if EC_PENDING
    public const MetaType Nation = (MetaType)103;
#endif
    // 矿产图层(见 Layer/MineralMap.cs)
#if EC_PENDING
    public const MetaType Mineral = (MetaType)104;
#endif
    // 财富图层(见 Layer/WealthMap.cs)
#if EC_PENDING
    public const MetaType Wealth = (MetaType)105;
#endif
    // 城市图层：只显示模拟的城市(见 Layer/CityViewMap.cs)
#if EC_PENDING
    public const MetaType CityView = (MetaType)106;
#endif
    // 教化图层、政令图层(见 Layer/RadianceMap.cs)
#if EC_PENDING
    public const MetaType Edification = (MetaType)107;
    public const MetaType Governance = (MetaType)108;
#endif
    // 天下图层(见 Layer/TianxiaMap.cs)
#if EC_PENDING
    public const MetaType Tianxia = (MetaType)109;
#endif
    // 商品市场图层(见 Layer/MarketMap.cs)
#if EC_PENDING
    public const MetaType Market = (MetaType)110;
    
    public static string ToMetaString(this MetaType type)
    {
        switch (type)
        {
            case (MetaType)100:
                return "Empire";
            case (MetaType)101:
                return "KingdomTitle";
            case (MetaType)102:
                return "Ideology";
            case (MetaType)103:
                return "Nation";
            case (MetaType)104:
                return "Mineral";
            case (MetaType)105:
                return "Wealth";
            case (MetaType)106:
                return "CityView";
            case (MetaType)107:
                return "Edification";
            case (MetaType)108:
                return "Governance";
            case (MetaType)109:
                return "Tianxia";
            case (MetaType)110:
                return "Market";
            default:
                return type.ToString();
        }
    }
#endif
}
}
