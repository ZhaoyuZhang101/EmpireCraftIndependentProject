// Generated from the EmpireCraft mod: Scripts/GeneralSystems/BannerSystem.Frontier.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 盟旗、理藩院、封禁(用户 2026-11-06 选"盟旗、理藩院、封禁")：编旗的王朝(入关前后都算)治下的外藩——别的游牧文化的成员国：
//   · 盟旗分治：外藩不许统合成大国；有姻亲、或八旗战力尚在五十以上时不自立(见 EmpireCraftPlotsAddition)；
//     外藩的军编成部落兵(BannerKhanate 的 district)，不编绿营；
//   · 满蒙联姻：每年有机会与一个还没结亲(或姻亲已过二十年)的外藩互嫁宗女，结为姻亲；
//   · 封禁：入关以后本族旧地(旗下旧地)封禁，属地之民不许迁入，保住龙兴之地；入关百年以后天命衰(低于五十)、或满两百年弛禁，
//     此后每年属地之城里最挤的几座往旧地闯关东。
public static partial class BannerSystem
{
    private const float LeagueMarriageChance = 0.2f, LeagueVigorHold = 50f;
    private const int LeagueMarriageYears = 20;

    // 外藩：编旗的王朝治下、别的游牧文化的成员国
    public static bool LeagueBound(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || !kingdom.IsInEmpire()) return false;
        Empire empire = kingdom.GetEmpire();
        Realm core = empire?.CoreKingdom;
        if (core == null || core == kingdom || !Organized(core)) return false;
        string culture = CultureService.GetRealmCulture(kingdom);
        return culture.IsNomadCulture() && !string.Equals(culture, RulingCultureOf(core), global::System.StringComparison.Ordinal);
    }

    // 外藩不自立：与中央有姻亲，或八旗战力尚在
    public static bool LeagueRestrains(Realm kingdom)
    {
        if (!LeagueBound(kingdom)) return false;
        Realm core = kingdom.GetEmpire().CoreKingdom;
        return RoyalMarriageSystem.Allied(kingdom, core) || Vigor(core) >= LeagueVigorHold;
    }

    private static IEnumerable<Realm> LeaguesOf(Empire empire) =>
        (empire?.kingdoms_list ?? new List<Realm>()).Where(LeagueBound);

    // 每年：满蒙联姻(编旗以后就有，入关前后都算)
    private static void LeagueYearly(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        if (!EcRandom.Chance(LeagueMarriageChance)) return;
        double now = GameClock.Now;
        Realm league = LeaguesOf(empire).ToList()
            .Where(member => !core.GetOrCreate().marriage_pacts.TryGetValue(member.Key, out double at) ||
                             GameClock.YearsSince(at) >= LeagueMarriageYears)
            .OrderBy(member => member.Key).FirstOrDefault();
        if (league == null) return;
        // 两头都嫁：宗女下嫁外藩，外藩之女入宫
        Person bride = EcRandom.Chance(0.5f) ? RoyalMarriageSystem.ArrangeHeqin(core, league) : RoyalMarriageSystem.ArrangeHeqin(league, core);
        bride ??= RoyalMarriageSystem.ArrangeHeqin(core, league) ?? RoyalMarriageSystem.ArrangeHeqin(league, core);
        if (bride == null) return;
        string text = string.Format(LM.Get("banner_league_marriage_history"), empire.GetEmpireName(), league.GetKingdomName(), bride.FullName());
        empire.RecordHistory(directContent: text, kingdomId: core.Key);
        TranslateHelper.LogEventOnly(text, core);
    }

    #region 封禁

    private const int SealLiftYears = 100, SealLiftLongYears = 200, SealLiftMandate = 50, MigrantSources = 3;
    private const float MigrantShare = 0.01f;

    // 封禁之城：入关以后、没弛禁的旗下旧地
    public static bool Sealed(City city)
    {
        if (city?.Record == null || !city.GetOrCreate().banner_home) return false;
        Empire empire = city.Realm?.GetEmpire();
        if (empire == null || !Practices(empire)) return false;
        var extra = empire.CoreKingdom.GetOrCreate();
        return extra.banner_entered_at >= 0d && !extra.banner_seal_lifted;
    }

    // 迁徙(见 CityPopulationSystem.Migrate)：属地之民不许迁进封禁之城，旧地之间照常
    public static bool MigrationBarred(City from, City to) => Sealed(to) && !from.GetOrCreate().banner_home;

    private static void SealYearly(Empire empire, Realm core, KingdomExtension.KingdomExtraData extra)
    {
        if (extra.banner_entered_at < 0d) return;
        float years = YearsSince(extra.banner_entered_at);
        if (!extra.banner_seal_lifted)
        {
            if (years >= SealLiftLongYears || years >= SealLiftYears && empire.Mandate < SealLiftMandate)
            {
                extra.banner_seal_lifted = true;
                Announce(empire, core, string.Format(LM.Get("banner_seal_lifted_history"), empire.GetEmpireName()));
            }
            return;
        }
        Migrate(empire);
    }

    // 闯关东：属地之城里最挤的几座各迁一成之一到旧地里最空的城
    private static void Migrate(Empire empire)
    {
        string institutional = CompositeEmpireService.GetInstitutionalCulture(empire);
        List<City> cities = CitiesOf(empire).ToList();
        City home = cities.Where(city => city.GetOrCreate().banner_home)
            .OrderByDescending(Room).FirstOrDefault();
        if (home == null || Room(home) <= 0f) return;
        foreach (City source in cities
                     .Where(city => !city.GetOrCreate().banner_home &&
                                    CultureService.SameKin(CultureService.GetMainCulture(city, initialize: false), institutional))
                     .OrderByDescending(Crowding).Take(MigrantSources).ToList())
        {
            if (Crowding(source) <= 1f) break;
            CityPopulationSystem.TransferBackground(source, home, MigrantShare);
        }
    }

    // 住满了几成(人口 / 住房上限)
    private static float Crowding(City city)
    {
        CityPopulationSystem.GrowthFactors factors = CityPopulationSystem.GetGrowthFactors(city);
        return factors.Capacity > 0f ? factors.Total / factors.Capacity : 0f;
    }

    // 还能住几成
    private static float Room(City city)
    {
        CityPopulationSystem.GrowthFactors factors = CityPopulationSystem.GetGrowthFactors(city);
        return factors.Capacity > 0f ? Mathf.Clamp01((factors.Capacity - factors.Total) / factors.Capacity) : 0f;
    }

    #endregion
}
}
