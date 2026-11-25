// Generated from the EmpireCraft mod: Scripts/GeneralSystems/MassacreSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;

namespace EmpireCraft.Core
{

// 战争中的平民与屠戮(平民保护见 GamePatches/CivilianProtectionPatch)：
//   · 交战时士兵不攻击敌国平民，弱小民族不会因为打了败仗就被杀光；
//   · 仇外的军队仍会屠戮异族平民。被屠戮的城市记下仇恨：屠城者(或其所在帝国)统治这座城时，
//     城市忠诚按死难人数下降(每人 -LoyaltyPerVictim，最多 -MaxLoyaltyPenalty)，三十年内逐渐淡去，
//     忠诚跌破零就会叛乱——屠戮换来的只是更多叛乱。
public static class MassacreSystem
{
    public const int MemoryYears = 30;
    private const int LoyaltyPerVictim = 2;
    private const int MaxLoyaltyPenalty = 40;
    private const int NoticeVictims = 5;

#if EC_PENDING
    private static bool IsCivilian(Person actor) => actor?.profession_asset?.is_civilian == true;
#endif

    // 交战双方之间、至少一方是平民的交锋
#if EC_PENDING
    public static bool IsWartimeCivilianEncounter(Person attacker, Person target)
    {
        if (attacker == null || target == null) return false;
        Realm attackerKingdom = attacker.Realm;
        Realm targetKingdom = target.Realm;
        if (attackerKingdom == null || targetKingdom == null || attackerKingdom == targetKingdom) return false;
        if (!attacker.isKingdomCiv() || !target.isKingdomCiv()) return false;
        if (!IsCivilian(attacker) && !IsCivilian(target)) return false;
        return attackerKingdom.IsEnemy(targetKingdom);
    }
#endif

    // 仇外的士兵对异族(物种或文化不同)平民下手
#if EC_PENDING
    public static bool WillMassacre(Person attacker, Person target) =>
        !IsCivilian(attacker) && IsCivilian(target) && attacker.hasXenophobic() &&
        !(attacker.culture == target.culture && attacker.asset == target.asset);

    public static void OnCivilianKilled(Person victim, Person killer)
    {
        if (EmpireWorld.Current == null || !IsCivilian(victim) || killer == null || IsCivilian(killer)) return;
        Realm killerKingdom = killer.Realm;
        if (killerKingdom == null || victim.Realm == null || !killer.isKingdomCiv() || !victim.isKingdomCiv() ||
            !killerKingdom.IsEnemy(victim.Realm)) return;
        City city = victim.City ?? victim.current_tile?.zone_city;
        if (city == null || city.IsGone()) return;
        CityExtension.CityExtraData data = city.GetOrCreate();
        double now = GameClock.Now;
        if (data.massacre_by_kingdom_id != killerKingdom.Key ||
            data.massacre_last < 0d || GameClock.YearsSince(data.massacre_last) >= MemoryYears)
        {
            data.massacre_by_kingdom_id = killerKingdom.Key;
            data.massacre_victims = 0;
            data.massacre_noticed = false;
        }
        data.massacre_victims++;
        data.massacre_last = now;
        if (data.massacre_noticed || data.massacre_victims < NoticeVictims) return;
        data.massacre_noticed = true;
        Announce(city, data, killerKingdom, now);
    }
#endif

    // 无小人模式：纵兵烧杀(见 ScorchedEarthSystem)杀害的背景人口也记入屠城之恨
#if EC_PENDING
    public static void RecordBackgroundMassacre(City city, Realm killerKingdom, int victims)
    {
        if (EmpireWorld.Current == null || city?.Record == null || killerKingdom == null || victims <= 0) return;
        CityExtension.CityExtraData data = city.GetOrCreate();
        double now = GameClock.Now;
        if (data.massacre_by_kingdom_id != killerKingdom.Key ||
            data.massacre_last < 0d || GameClock.YearsSince(data.massacre_last) >= MemoryYears)
        {
            data.massacre_by_kingdom_id = killerKingdom.Key;
            data.massacre_victims = 0;
            data.massacre_noticed = false;
        }
        data.massacre_victims += victims;
        data.massacre_last = now;
        if (data.massacre_noticed) return;
        data.massacre_noticed = true;
        Announce(city, data, killerKingdom, now);
    }
#endif

    // 同一国在同一城 MemoryYears 年内只播报一次：几国轮番烧杀同一座城时，记账的屠城者来回换，
    // 以前每换一次就重播一次，消息刷屏
#if EC_PENDING
    private static void Announce(City city, CityExtension.CityExtraData data, Realm killerKingdom, double now)
    {
        data.massacre_announced ??= new global::System.Collections.Generic.Dictionary<long, double>();
        if (data.massacre_announced.TryGetValue(killerKingdom.Key, out double at) && at >= 0d && now >= at &&
            GameClock.YearsSince(at) < MemoryYears) return;
        data.massacre_announced[killerKingdom.Key] = now;
        TranslateHelper.LogEventMessage(string.Format(LM.Get("massacre_event"), killerKingdom.GetKingdomName(),
            city.GetCityName()), killerKingdom);
    }
#endif

    // 屠城之恨：城市现在归屠城者(或与之同一帝国)统治时的忠诚惩罚
    public static int LoyaltyPenalty(City city)
    {
        if (city?.Realm == null || EmpireWorld.Current == null) return 0;
        CityExtension.CityExtraData data = city.GetOrCreate();
        if (data.massacre_victims <= 0 || data.massacre_last < 0d) return 0;
        int years = GameClock.YearsSince(data.massacre_last);
        if (years >= MemoryYears) return 0;
        Realm perpetrator = EmpireWorld.Current.RealmByKey(data.massacre_by_kingdom_id);
        bool ruledByPerpetrator = city.Realm.Key == data.massacre_by_kingdom_id ||
                                  perpetrator != null && perpetrator.GetEmpire() != null &&
                                  perpetrator.GetEmpire() == city.Realm.GetEmpire();
        if (!ruledByPerpetrator) return 0;
        float fade = 1f - years / (float)MemoryYears;
        return -Mathf.RoundToInt(Math.Min(MaxLoyaltyPenalty, data.massacre_victims * LoyaltyPerVictim) * fade);
    }
}
}
