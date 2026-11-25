// Generated from the EmpireCraft mod: Scripts/GeneralSystems/RealmNamingSystem.Place.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Linq;

namespace EmpireCraft.Core
{

// 帝国号不用地名(用户 2026-11-08 截图"岭西朝"："为什么华夏会出现省份或者城市名为帝国名的情况""这种应该是绝对避免的"，
// "国号应当和法理绑死，帝国号也是，除了美号")：
//   · 帝国号只能是所绑帝国法理的名字、自家发家的王国法理名，或美号；城名、行政区(道、省、路)名一律不当帝国号；
//   · 王国法理照旧可以叫地名(汉代有济南国、河间国)，王国国号跟着法理走，只是称帝时不拿它当帝国号；
//   · 王国法理生成时名字取自城名、或当时所属行政区的名字，记下 place_name，那个行政区没了也认得出来。
public static partial class RealmNamingSystem
{
    // 这个名字是不是地名：现存的城名、现存行政区的名字、照地名起的王国法理名
    public static bool IsPlaceName(string name)
    {
        string plain = EmpireCoreManager.NormalizeCoreName(name);
        if (string.IsNullOrWhiteSpace(plain) || EmpireWorld.Current == null) return false;
        bool Same(string other) => string.Equals(EmpireCoreManager.NormalizeCoreName(other), plain, StringComparison.Ordinal);
        foreach (City city in EmpireWorld.Current.Cities ?? Enumerable.Empty<City>())
            if (city?.Record != null && !city.IsGone() && Same(city.GetCityName())) return true;
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms ?? Enumerable.Empty<Realm>())
            if (kingdom?.Record != null && !kingdom.IsGone() && kingdom.IsAdministrativeKingdomType() && Same(kingdom.EnsureKingdomCoreName()))
                return true;
        return ModClass.KINGDOM_TITLE_MANAGER != null &&
               ModClass.KINGDOM_TITLE_MANAGER.Any(title => title?.data != null && title.data.place_name && Same(title.data.name));
    }

    // 王国法理名能不能当帝国号
    public static bool PlaceTitle(KingdomTitle title) =>
        title?.data != null && (title.data.place_name || IsPlaceName(title.data.name));
}
}
