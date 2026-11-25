// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PettyRebellion.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.

namespace EmpireCraft.Core
{

// 分封制下的小乱(用户 2026-10-14："春秋战国时期的叛乱规模可以特别小，小到无法推翻国家，因为都是分封制")：
// 在周制分封的诸侯国(周室天下里的国家，或周制封建政体)里起事的叛军记下原主(KingdomExtraData.petty_rebel_origin)：
//   · 至多两座城——攻下、和约割让、倒戈投靠都算，满了就拿不到；
//   · 攻不下原主的都城；
//   · 别的诸侯不来投(地方叛乱的自动扩张不算它)；义军减半(见 RebellionStartupService)。
// 一城一邑之乱，推翻不了国家。和原主停战满一年、或原主已亡，就不再算叛军，当普通诸侯发展。
public static class PettyRebellion
{
    public const int MaxCities = 2;

    // 原主是分封制的诸侯国
#if EC_PENDING
    public static bool Feudal(Realm origin) =>
        origin?.Record != null && !origin.IsGone() &&
        (ZhouEraSystem.SphereOf(origin) != null || origin.GetRegime()?.type == RegimeType.ZhouFeudalism);
#endif

    // 起事时(CityPatch.makeOwnKingdom)记下原主
#if EC_PENDING
    public static void Mark(Realm rebel, Realm origin)
    {
        if (rebel?.Record == null || !Feudal(origin) || EmpireWorld.Current == null) return;
        var data = rebel.GetOrCreate();
        data.petty_rebel_origin = origin.Key;
        data.petty_rebel_since = GameClock.Now;
    }
#endif

    // 还是不是分封制下的小乱(原主亡了、停战满一年就不算了)
#if EC_PENDING
    public static bool IsPetty(Realm rebel)
    {
        if (rebel?.Record == null || rebel.IsGone()) return false;
        var data = rebel.GetOrCreate();
        if (data.petty_rebel_origin < 0) return false;
        Realm origin = EmpireWorld.Current?.kingdoms?.get(data.petty_rebel_origin);
        bool settled = origin?.Record == null || origin.IsGone() ||
                       data.petty_rebel_since >= 0d && GameClock.YearsSince(data.petty_rebel_since) >= 1 && !rebel.IsAtWarWith(origin);
        if (!settled) return true;
        data.petty_rebel_origin = -1;
        data.petty_rebel_since = -1d;
        return false;
    }
#endif

    // 这座城该不该归这支叛军：满两座城了、或是原主的都城，就不行
#if EC_PENDING
    public static bool Blocks(Realm rebel, City city)
    {
        if (city?.Record == null || city.Realm == rebel || !IsPetty(rebel)) return false;
        if ((rebel.Cities?.Count ?? 0) >= MaxCities) return true;
        Realm origin = EmpireWorld.Current.RealmByKey(rebel.GetOrCreate().petty_rebel_origin);
        return origin?.Record != null && origin.Capital == city;
    }
#endif
}
}
