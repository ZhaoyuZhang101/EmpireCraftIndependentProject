// Generated from the EmpireCraft mod: Scripts/GameClassExtensions/MetaTypeExtension.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
namespace EmpireCraft.Core
{

public static class MetaTypeExtension
{
    public const EntityKind Empire = (EntityKind)100;
    public const EntityKind KingdomTitle = (EntityKind)101;
    // 理念图层(独立于原版宗教)
    public const EntityKind Ideology = (EntityKind)102;
    // 民族情绪图层(见 Layer/NationMap.cs)
    public const EntityKind Nation = (EntityKind)103;
    // 矿产图层(见 Layer/MineralMap.cs)
    public const EntityKind Mineral = (EntityKind)104;
    // 财富图层(见 Layer/WealthMap.cs)
    public const EntityKind Wealth = (EntityKind)105;
    // 城市图层：只显示模拟的城市(见 Layer/CityViewMap.cs)
    public const EntityKind CityView = (EntityKind)106;
    // 教化图层、政令图层(见 Layer/RadianceMap.cs)
    public const EntityKind Edification = (EntityKind)107;
    public const EntityKind Governance = (EntityKind)108;
    // 天下图层(见 Layer/TianxiaMap.cs)
    public const EntityKind Tianxia = (EntityKind)109;
    // 商品市场图层(见 Layer/MarketMap.cs)
    public const EntityKind Market = (EntityKind)110;
    
    public static string ToMetaString(this EntityKind type)
    {
        switch (type)
        {
            case (EntityKind)100:
                return "Empire";
            case (EntityKind)101:
                return "KingdomTitle";
            case (EntityKind)102:
                return "Ideology";
            case (EntityKind)103:
                return "Nation";
            case (EntityKind)104:
                return "Mineral";
            case (EntityKind)105:
                return "Wealth";
            case (EntityKind)106:
                return "CityView";
            case (EntityKind)107:
                return "Edification";
            case (EntityKind)108:
                return "Governance";
            case (EntityKind)109:
                return "Tianxia";
            case (EntityKind)110:
                return "Market";
            default:
                return type.ToString();
        }
    }
}
}
