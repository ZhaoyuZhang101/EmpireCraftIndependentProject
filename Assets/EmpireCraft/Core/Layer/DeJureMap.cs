// Generated from the EmpireCraft mod: Scripts/Layer/DeJureMap.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 帝国法理在法理图层上的着色对象：一个帝国法理一个，颜色取自 DeJureMap 的纹章色
public sealed class DeJureCoreColor : RecordObject<IdeologyMapColorData>
{
    private Tint _color;
    public EmpireCore core { get; private set; }

    public override EntityKind KindOfEntity => MetaTypeExtension.KingdomTitle;
#if EC_PENDING
    public override ColorLibrary getColorLibrary() => AssetManager.kingdom_colors_library;
#endif
    public override Tint GetColor() => _color;

    // 编号与真国家、真法理错开
    private const long UniqueIdBase = 7_000_000_000_000L;

#if EC_PENDING
    public static DeJureCoreColor Create(EmpireCore core, Tint color)
    {
        var meta = new DeJureCoreColor { core = core, _color = color };
        // 原版按哈希码判同一对象(Entity.Equals)：不给自己的哈希码，各帝国法理都是 0，平滑国界与描边会把它们当成同一片
        int hash = BaseSystemManager._latest_hash++;
        meta.LoadData(new IdeologyMapColorData { name = "dejure_core_" + core.id, id = UniqueIdBase + core.id });
        meta.setHash(hash);
        return meta;
    }
#endif

    internal void SetColor(Tint color) => _color = color;
}

// 法理三层各用一套色(天下的设色见 TianxiaMap)：
//   王国法理：明快的浅色；
//   帝国法理：饱和的纹章色(以前藏青墨绿一类深色，半透明叠在真实地形上几乎看不出来，用户 10-29 选"换亮色配色")。
// 两层都是接壤的不同色。以前王国法理与国家同一个色库、帝国法理取帝国(就是核心王国)的颜色或代表它的王国法理的颜色，
// 三层看上去一个样，也分不清哪片是哪个帝国法理
public static class DeJureMap
{
    // 缃、青碧、海棠、葱、丁香、天青、鹅黄、杏、湖、藕荷、粉、柳、驼、翠、雪青、月白
    private static readonly string[] TitlePalette =
    {
        "#e0a458", "#6fb7a8", "#d9777f", "#93c26a", "#b48fd1", "#6aa6dc", "#e3cf5e", "#e08d5a", "#7cc6c6", "#a9b0e6",
        "#e78fb3", "#b9c96a", "#d6a77a", "#5fbf8a", "#c99ae0", "#8fb8e8"
    };

    // 朱红、宝蓝、品红、紫、橙、青、胭脂、金、靛、青绿、粉、天蓝(不用黄绿一带，免得和草地混在一起)
    private static readonly string[] CorePalette =
    {
        "#e3342f", "#2e6fe8", "#d63cb0", "#8f45e8", "#f27a1a", "#14a8d0", "#c8264f", "#f2c41a", "#5246e8", "#12c4a0",
        "#ff70b0", "#5ab8ff"
    };

    private static Tint[] _titleColors, _coreColors;
    private static readonly Dictionary<long, int> TitleIndex = new(), CoreIndex = new();
    private static readonly Dictionary<long, DeJureCoreColor> CoreMetas = new();
    private static object _world;
    private static float _nextRecolor;
    // 归属几秒才变一次，按时重排即可(着色只看接壤，同样的局面排出来的颜色不变，不会无故闪色)
    private const float RecolorSeconds = 5f;

    private static Tint[] Make(string[] palette) => palette.Select(hex =>
    {
        // 原版 tryMakeNewColorAsset 新建的颜色不 initColor() 就是透明色
        Tint asset = Tint.FromHex(hex);
        asset.InitColor();
        return asset;
    }).ToArray();

    public static Tint TitleColor(KingdomTitle title)
    {
        if (title?.data == null) return null;
        Refresh();
        _titleColors ??= Make(TitlePalette);
        return _titleColors[TitleIndex.TryGetValue(title.Key, out int index) ? index : Preferred(title.Key, TitlePalette.Length)];
    }

    // 帝国法理在法理图层上的归属对象(颜色、内部不描边)；点击、悬停仍按城找帝国法理(见 EmpireCraftMetaTypeLibrary)
#if EC_PENDING
    public static DeJureCoreColor CoreMeta(EmpireCore core)
    {
        if (core == null) return null;
        Refresh();
        Tint color = CoreColor(core);
        if (CoreMetas.TryGetValue(core.id, out DeJureCoreColor meta))
        {
            if (!ReferenceEquals(meta.GetColor(), color)) meta.SetColor(color);
            return meta;
        }
        return CoreMetas[core.id] = DeJureCoreColor.Create(core, color);
    }
#endif

    private static Tint CoreColor(EmpireCore core)
    {
        _coreColors ??= Make(CorePalette);
        return _coreColors[CoreIndex.TryGetValue(core.id, out int index) ? index : Preferred(core.id, CorePalette.Length)];
    }

    // 城在帝国法理视图上属于哪个帝国法理：城的王国法理在帝国法理里才算
    public static EmpireCore CoreOf(City city)
    {
        KingdomTitle title = city?.GetTitle();
        EmpireCore core = city?.GetEmpireCore();
        return core != null && title != null && EmpireCoreManager.ContainsTitle(core, title) ? core : null;
    }

    private static int Preferred(long id, int count) => (int)((id % count + count) % count);

    private static void Refresh()
    {
        if (ReferenceEquals(_world, EmpireWorld.Current) && Time.realtimeSinceStartup < _nextRecolor) return;
        if (!ReferenceEquals(_world, EmpireWorld.Current)) CoreMetas.Clear();
        _world = EmpireWorld.Current;
        _nextRecolor = Time.realtimeSinceStartup + RecolorSeconds;
        if (EmpireWorld.Current?.Cities == null) return;
        Recolor(TitleIndex, TitlePalette.Length, city => city.hasTitle() ? city.GetTitle() : null, title => title.Key);
        Recolor(CoreIndex, CorePalette.Length, CoreOf, core => core.id);
    }

    // 贪心着色：按编号排，先取按编号定的那一色，与已排的接壤者撞色就往后换
    private static void Recolor<T>(Dictionary<long, int> result, int count, global::System.Func<City, T> of, global::System.Func<T, long> idOf)
        where T : class
    {
        result.Clear();
        var adjacent = new Dictionary<long, HashSet<long>>();
        foreach (City city in EmpireWorld.Current.Cities)
        {
            if (city?.Record == null || city.IsGone()) continue;
            T own = of(city);
            if (own == null) continue;
            long id = idOf(own);
            if (!adjacent.TryGetValue(id, out HashSet<long> near)) adjacent[id] = near = new HashSet<long>();
            if (city.NeighbourCities == null) continue;
            foreach (City next in city.NeighbourCities)
            {
                if (next?.Record == null || next.IsGone()) continue;
                T other = of(next);
                if (other != null && idOf(other) != id) near.Add(idOf(other));
            }
        }
        foreach (long id in adjacent.Keys.OrderBy(id => id))
        {
            var used = new HashSet<int>();
            foreach (long other in adjacent[id])
                if (result.TryGetValue(other, out int taken)) used.Add(taken);
            int preferred = Preferred(id, count), pick = preferred;
            for (int step = 0; step < count && used.Contains(pick); step++) pick = (preferred + step + 1) % count;
            result[id] = pick;
        }
    }
}
}
