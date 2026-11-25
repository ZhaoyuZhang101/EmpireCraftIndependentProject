// Generated from the EmpireCraft mod: Scripts/Layer/TianxiaMap.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 天下图层(MetaTypeExtension.Tianxia，见 TianxiaSystem)：
//   0 天下：各天下一色，天下之间描边，铭牌写天下名与天命；
//   1 诸家：天下里按各家(宗主)着色——一眼看出谁据有多少、天下共主占了几成；天下以外不上色。
// 点地块打开天下窗口；悬停看天下名、天命、各家占比。
// 还没有帝国核心、按文化连片的天下(见 UnificationSystem.UncoredRealmOf)用素色画出、描边，铭牌写"某某(未立核心)"，
// 悬停看各家占比——以前这些地方在图层上一片空白。
public sealed class TianxiaMapColor : RecordObject<IdeologyMapColorData>
{
#if EC_PENDING
    private ColorAsset _color;

    public override MetaType meta_type => MetaTypeExtension.Tianxia;
    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;
    public override ColorAsset getColor() => _color;

    public static TianxiaMapColor Create(int index, string hex)
    {
        // 原版 tryMakeNewColorAsset 新建的颜色不 initColor() 就是透明色
        ColorAsset asset = ColorAsset.tryMakeNewColorAsset(hex);
        asset.initColor();
        var color = new TianxiaMapColor { _color = asset };
        color.LoadData(new IdeologyMapColorData { name = "tianxia_" + index, id = 7_100_000_000_000L + index });
        // 原版按哈希码判同一对象：不给的话各天下都是 0，平滑国界会把它们当成同一片
        color.setHash(BaseSystemManager._latest_hash++);
        return color;
    }
#endif
}

public static class TianxiaMap
{
    public const string TooltipType = "empirecraft_tianxia";

    // 鲜亮的设色：朱、石青、藤黄、品紫、碧、桃红、橘、群青、青绿、胭脂、天青、宝蓝。相邻的天下按主核心编号轮着取。
    // 色块半透明(同原版)，以前的绢本色偏暗偏素，叠在真实地形(世界地图的山地、草原、沙漠)上几乎看不出来(用户 10-29 选"换亮色配色")；
    // 不用黄绿一带的颜色，免得和草地混在一起
    private static readonly string[] Palette =
    {
        "#ee5a2f", "#2f86e6", "#f7c21c", "#cf47e0", "#14b8b8", "#ff63b2", "#ff8f24", "#7b5cf5", "#12c9a2", "#e0336a",
        "#4fd2f2", "#5a7cff"
    };

    private static TianxiaMapColor[] _colors;
    // 未立核心的天下：浅灰紫的素色(以前的灰褐色和地形分不开)
    private const string UncoredHex = "#b6b4cc";
    private static TianxiaMapColor _uncoredColor;

#if EC_PENDING
    private static TianxiaMapColor UncoredColor() => _uncoredColor ??= TianxiaMapColor.Create(Palette.Length, UncoredHex);

    private static TianxiaMapColor ColorOf(EmpireCore lead)
    {
        _colors ??= Palette.Select((hex, i) => TianxiaMapColor.Create(i, hex)).ToArray();
        return _colors[IndexOf(lead)];
    }
#endif

    public static string HexOf(EmpireCore lead) => Palette[IndexOf(lead)];

    // 着色：先照主核心编号取，与接壤的天下撞色就换一个(以前只按编号轮，相邻的天下常常同色)；每年随天下结算重排
    private static readonly Dictionary<long, int> ColorIndex = new();
    private static int _colorVersion = -1;
    private static object _colorWorld;

    private static int Preferred(EmpireCore lead) => (int)((lead.id % Palette.Length + Palette.Length) % Palette.Length);

    private static int IndexOf(EmpireCore lead)
    {
        if (lead == null) return 0;
        if (!ReferenceEquals(_colorWorld, EmpireWorld.Current) || _colorVersion != UnificationSystem.UncoredVersion) Recolor();
        if (ColorIndex.TryGetValue(lead.id, out int index)) return index;
        return ColorIndex[lead.id] = Preferred(lead);
    }

    private static void Recolor()
    {
        _colorWorld = EmpireWorld.Current;
        _colorVersion = UnificationSystem.UncoredVersion;
        ColorIndex.Clear();
        if (EmpireWorld.Current?.Cities == null) return;
        var adjacent = new Dictionary<long, HashSet<long>>();
        foreach (City city in EmpireWorld.Current.Cities)
        {
            EmpireCore lead = TianxiaSystem.LeadOf(city);
            if (lead == null || city.NeighbourCities == null) continue;
            foreach (City next in city.NeighbourCities)
            {
                EmpireCore other = TianxiaSystem.LeadOf(next);
                if (other == null || other == lead) continue;
                if (!adjacent.TryGetValue(lead.id, out HashSet<long> set)) adjacent[lead.id] = set = new HashSet<long>();
                set.Add(other.id);
            }
        }
        foreach (EmpireCore lead in TianxiaSystem.Leads().OrderBy(item => item.id))
        {
            var used = new HashSet<int>();
            if (adjacent.TryGetValue(lead.id, out HashSet<long> near))
                foreach (long other in near)
                    if (ColorIndex.TryGetValue(other, out int taken)) used.Add(taken);
            int preferred = Preferred(lead), pick = preferred;
            for (int step = 0; step < Palette.Length && used.Contains(pick); step++) pick = (preferred + step + 1) % Palette.Length;
            ColorIndex[lead.id] = pick;
        }
    }

    private static bool Shown(City city) =>
        city != null && !city.IsGone() && !AncientWarfareCompatibility.OwnsObject(city) &&
        (TianxiaSystem.LeadOf(city) != null || UnificationSystem.UncoredRealmOf(city) != null);

#if EC_PENDING
    public static void Configure(MetaTypeAsset asset)
    {
        asset.draw_zones = (MetaZoneDrawAction)(meta =>
        {
            int mode = meta.getZoneOptionState();
            foreach (City city in EmpireWorld.Current.Cities)
            {
                if (!Shown(city)) continue;
                EmpireCore lead = TianxiaSystem.LeadOf(city);
                Realm holder = EmpireCoreControl.Sovereign(city.Realm);
                if (lead == null)
                {
                    DrawUncored(city, mode, holder, meta);
                    continue;
                }
                foreach (Cell zone in city.Zones)
                {
                    EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
                    if (mode == 1 && holder != null)
                        EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(holder, zone,
                            HolderBorder(zone.Up, lead, holder), HolderBorder(zone.Down, lead, holder),
                            HolderBorder(zone.Left, lead, holder), HolderBorder(zone.Right, lead, holder),
                            holder.Record, meta);
                    else
                    {
                        TianxiaMapColor color = ColorOf(lead);
                        EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(color, zone,
                            TianxiaBorder(zone.Up, lead), TianxiaBorder(zone.Down, lead),
                            TianxiaBorder(zone.Left, lead), TianxiaBorder(zone.Right, lead), color.data, meta);
                    }
                    EmpireCraftMetaTypeLibrary.zone_manager.drawEnd(zone);
                }
            }
        });
        asset.check_cursor_highlight = (MetaZoneHighlightAction)((meta, tile, highlight) =>
        {
            EmpireCore lead = TianxiaSystem.LeadOf(tile?.zone?.city);
            List<City> cities = lead != null ? TianxiaSystem.Cities(lead) : UnificationSystem.UncoredRealmOf(tile?.zone?.city);
            if (cities == null) return;
            QuantumSpriteLibrary.colorZones(highlight, cities.Where(city => city != null && !city.IsGone())
                .SelectMany(city => city.Zones).ToList(), highlight.color);
        });
        // 按地块取归属：平滑国界(整图画布)要它才画得出来(见 SmoothBorders.Smoothable)——以前没给，平滑国界下天下图层只有铭牌、没有颜色
        asset.tile_get_metaobject = (zone, option) => MetaOf(zone?.city, option);
        asset.tile_get_metaobject_0 = zone => MetaOf(zone?.city, 0);
        asset.tile_get_metaobject_1 = zone => MetaOf(zone?.city, 1);
        // 原版每帧无条件调用这两个委托，不设会每帧抛空引用
        asset.check_tile_has_meta = (MetaZoneTooltipAction)((zone, meta, option) => Shown(zone?.city));
        asset.check_cursor_tooltip = (zone, meta, option) =>
        {
            City city = zone?.city;
            if (!Shown(city)) return false;
            Tooltip.hideTooltip(city, true, TooltipType);
            Tooltip.show(city, TooltipType, new TooltipData
            {
                city = city, kingdom = city.Realm, tooltip_scale = 0.7f, is_sim_tooltip = true
            });
            return true;
        };
        asset.click_action_zone = new MetaZoneClickAction((tile, power) =>
        {
            EmpireCore lead = TianxiaSystem.LeadOf(tile?.zone?.city);
            if (lead == null) return false;
            TianxiaWindow.Open(lead);
            return true;
        });
    }
#endif

    // 一块地在天下图层上归谁：0 天下(未立核心的用素绢色)、1 诸家(据有者)
#if EC_PENDING
    private static IMetaObject MetaOf(City city, int mode)
    {
        if (!Shown(city)) return null;
        if (mode == 1 && EmpireCoreControl.Sovereign(city.Realm) is Realm holder) return holder;
        EmpireCore lead = TianxiaSystem.LeadOf(city);
        return lead == null ? UncoredColor() : ColorOf(lead);
    }
#endif

    private static bool TianxiaBorder(Cell neighbour, EmpireCore lead) => TianxiaSystem.LeadOf(neighbour?.City) != lead;

#if EC_PENDING
    private static void DrawUncored(City city, int mode, Realm holder, MetaTypeAsset meta)
    {
        List<City> realm = UnificationSystem.UncoredRealmOf(city);
        bool Border(Cell neighbour) => TianxiaSystem.LeadOf(neighbour?.City) != null ||
                                          UnificationSystem.UncoredRealmOf(neighbour?.City) != realm ||
                                          mode == 1 && EmpireCoreControl.Sovereign(neighbour.City.Realm) != holder;
        foreach (Cell zone in city.Zones)
        {
            EmpireCraftMetaTypeLibrary.zone_manager.drawBegin();
            if (mode == 1 && holder != null)
                EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(holder, zone, Border(zone.Up), Border(zone.Down),
                    Border(zone.Left), Border(zone.Right), holder.Record, meta);
            else
            {
                TianxiaMapColor color = UncoredColor();
                EmpireCraftMetaTypeLibrary.zone_manager.drawZoneMeta(color, zone, Border(zone.Up), Border(zone.Down),
                    Border(zone.Left), Border(zone.Right), color.data, meta);
            }
            EmpireCraftMetaTypeLibrary.zone_manager.drawEnd(zone);
        }
    }
#endif

    // 未立核心的天下的称呼：按这片里最多的主流文化(铭牌每帧要用，每年重算后才重取)
    private static readonly Dictionary<List<City>, string> UncoredNames = new();
    private static int _namesVersion = -1;

#if EC_PENDING
    private static string UncoredName(List<City> cities)
    {
        if (_namesVersion != UnificationSystem.UncoredVersion)
        {
            UncoredNames.Clear();
            _namesVersion = UnificationSystem.UncoredVersion;
        }
        if (UncoredNames.TryGetValue(cities, out string cached)) return cached;
        return UncoredNames[cities] = ComputeUncoredName(cities);
    }

    private static string ComputeUncoredName(List<City> cities)
    {
        string culture = cities.Where(city => city?.Record != null && !city.IsGone())
            .Select(city => CultureService.GetMainCulture(city, false))
            .Where(CultureService.IsValidCulture).GroupBy(name => name).OrderByDescending(group => group.Count())
            .Select(group => group.Key).FirstOrDefault();
        return string.Format(LM.Get("tianxia_uncored_name"), culture?.GetCultureTranslate() ?? "");
    }
#endif

    private static bool HolderBorder(Cell neighbour, EmpireCore lead, Realm holder)
    {
        City other = neighbour?.City;
        return TianxiaSystem.LeadOf(other) != lead || EmpireCoreControl.Sovereign(other.Realm) != holder;
    }

    // 铭牌：每个天下一枚，沿用帝国的简易纯文字样式，只写天下名称。
#if EC_PENDING
    public static void SubmitLabels()
    {
        foreach (EmpireCore lead in TianxiaSystem.Leads())
        {
            List<City> cities = TianxiaSystem.Cities(lead);
            if (cities.Count == 0) continue;
            Components.TerritoryLabelRenderer.SubmitCities($"tianxia:{lead.id}", TianxiaSystem.Name(lead), cities,
                Components.TerritoryLabelRenderer.TianxiaStyle, followWilderness: true);
        }
        int index = 0;
        foreach (List<City> realm in UnificationSystem.UncoredRealms())
        {
            List<City> alive = realm.Where(city => city?.Record != null && !city.IsGone()).ToList();
            if (alive.Count > 0)
                Components.TerritoryLabelRenderer.SubmitCities($"tianxia:uncored:{index}", UncoredName(realm), alive,
                    Components.TerritoryLabelRenderer.UncoredTianxiaStyle, followWilderness: true);
            index++;
        }
    }
#endif

    // 悬停提示：天下名、天命、各家占比(前五)、本城归谁
#if EC_PENDING
    public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
    {
        City city = data?.city;
        EmpireCore lead = TianxiaSystem.LeadOf(city);
        if (lead == null)
        {
            ShowUncoredTooltip(tooltip, city);
            return;
        }
        tooltip.clear();
        tooltip.setTitle(TianxiaSystem.Name(lead), "tianxia_label", HexOf(lead));
        tooltip.addLineText(LM.Get("tianxia_mandate_short"), TianxiaSystem.MandateText(lead), "#FF8A7A", pPercent: false,
            pLocalize: false);
        (Dictionary<Realm, int> held, int total) = TianxiaSystem.Holdings(lead);
        foreach (KeyValuePair<Realm, int> pair in held.OrderByDescending(pair => pair.Value).Take(5))
            tooltip.addLineText(pair.Key.GetKingdomName(), $"{pair.Value / (float)total:P0}", pair.Key.getColor()?.color_text ?? "#FFFFFF",
                pPercent: false, pLocalize: false);
        Realm owner = EmpireCoreControl.Sovereign(city.Realm);
        tooltip.addBottomDescription(string.Format(LM.Get("tianxia_tooltip_city"), city.GetCityName(), owner?.GetKingdomName() ?? ""));
    }

    private static void ShowUncoredTooltip(Tooltip tooltip, City city)
    {
        List<City> whole = UnificationSystem.UncoredRealmOf(city);
        List<City> realm = whole?.Where(item => item?.Record != null && !item.IsGone()).ToList();
        if (realm == null || realm.Count == 0) return;
        tooltip.clear();
        tooltip.setTitle(UncoredName(whole), "tianxia_label", UncoredHex);
        tooltip.addLineText(LM.Get("tianxia_mandate_short"), LM.Get("tianxia_uncored_hint"), "#8FA0A8", pPercent: false,
            pLocalize: false);
        Dictionary<Realm, int> held = EmpireCoreControl.CountHolders(realm);
        foreach (KeyValuePair<Realm, int> pair in held.OrderByDescending(pair => pair.Value).Take(5))
            tooltip.addLineText(pair.Key.GetKingdomName(), $"{pair.Value / (float)realm.Count:P0}",
                pair.Key.getColor()?.color_text ?? "#FFFFFF", pPercent: false, pLocalize: false);
        Realm owner = EmpireCoreControl.Sovereign(city.Realm);
        tooltip.addBottomDescription(string.Format(LM.Get("tianxia_tooltip_city"), city.GetCityName(), owner?.GetKingdomName() ?? ""));
    }
#endif
}
}
