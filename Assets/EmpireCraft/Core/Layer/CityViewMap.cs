// Generated from the EmpireCraft mod: Scripts/Layer/CityViewMap.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Color = EmpireCraft.Core.EcColor;

namespace EmpireCraft.Core
{

public sealed class CityViewColor : RecordObject<IdeologyMapColorData>
{
    private Tint _color;

#if EC_PENDING
    public override MetaType meta_type => MetaTypeExtension.CityView;
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;
#endif
    public override Tint GetColor() => _color;

    public static CityViewColor Create()
    {
        Tint asset = Tint.FromHex("#8a8a80");
        asset.InitColor();
        var color = new CityViewColor { _color = asset };
        color.LoadData(new IdeologyMapColorData { name = "city_view" });
        return color;
    }
}

// 城市图层(MetaTypeExtension.CityView)：只显示模拟的城市——不涂国家颜色，露出原本的地形；
// 城(城墙、街巷、建筑、新城区)、出城的路、关厢、镇、村、田块、林子照各自的颜色画在地形上(城市平面图层，见 CityPlanOverlay；色块全透明)，
// 国界、城界都不画，也不画铭牌。悬停高亮整座城
public static class CityViewMap
{
    private static CityViewColor _color;
    private static CityViewColor Color => _color ??= CityViewColor.Create();

#if EC_PENDING
    public static bool Active => EmpireWorld.Current?.getCachedMapMetaAsset()?.map_mode == MetaTypeExtension.CityView;
#endif

    private static bool Shown(City city) =>
        city != null && !city.IsGone() && city.Realm != null && !AncientWarfareCompatibility.OwnsObject(city);

#if EC_PENDING
    public static void Configure(MetaTypeAsset asset)
    {
        asset.draw_zones = (MetaZoneDrawAction)(meta =>
        {
            foreach (City city in EmpireWorld.Current.Cities)
            {
                if (!Shown(city)) continue;
                foreach (Cell zone in city.Zones)
                {
                    EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
                    EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(Color, zone, false, false, false, false, Color.data, meta);
                    EmpireCraftMetaTypeLibrary.zone_manager.drawEnd(zone);
                }
            }
        });
        asset.check_cursor_highlight = (MetaZoneHighlightAction)((meta, tile, highlight) =>
        {
            City hovered = tile?.zone?.city;
            if (!Shown(hovered)) return;
            QuantumSpriteLibrary.colorZones(highlight, hovered.Zones, highlight.color);
        });
        asset.check_tile_has_meta = (MetaZoneTooltipAction)((zone, meta, option) => Shown(zone?.city));
        asset.check_cursor_tooltip = (zone, meta, option) => false;
        asset.click_action_zone = new MetaZoneClickAction((tile, power) => false);
    }
#endif
}
}
