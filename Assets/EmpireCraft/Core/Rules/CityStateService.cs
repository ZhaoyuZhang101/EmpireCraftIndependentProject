// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CityStateService.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 西方古典共和(城邦)阶段：一城一国(名下多出来的城每月分出一座，见 EnforceOneCity)。
//   · 城邦不占第二座城：攻下的城由本地贵族立为新城邦、加入攻城方的同盟；对方只剩这一座城时整国入盟；
//   · 城邦的国民新建城市即独立为殖民城邦，与母邦结盟；
//   · 联盟只用于没有称帝的城邦：攻城方/母邦已属于帝国时，新城邦或被征服的城邦直接并入该帝国
//     (本模组里帝国和原版联盟是互不兼容的两套东西)；
//   · 城邦国名默认用城市名，不能建立法理；
//   · 称帝后是古典共和帝国：成员完全自治；首领在施行「元老院」之前由最强者担任，之后由元老院推举；
//   · 施行「元首制」后：元首在家族里相传；核心与它的行省不再一城一国——核心打下的城归核心，再设行省派总督
//     (见 Empire.IsPrincipate)，原有的自治城邦照旧。
public static class CityStateService
{
#if EC_PENDING
    public static bool IsCityState(Realm kingdom) =>
        kingdom != null && !kingdom.IsGone() && kingdom.GetRegime()?.type == RegimeType.ClassicalRepublic;
#endif

    // 元首制的核心与行省(没有外交权的成员)：不再一城一国
#if EC_PENDING
    public static bool IsPrincipateRealm(Realm kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || !IsCityState(kingdom) || !empire.IsPrincipate()) return false;
        return kingdom == empire.CoreKingdom || kingdom.GetRegime()?.IsAllowDiplomacy() == false;
    }
#endif

    // 元老院推举由制度特性 senate_election 提供
#if EC_PENDING
    public static bool HasSenate(Empire empire) =>
        empire != null && InstitutionSystem.HasFeature(empire, InstitutionFeatures.SenateElection);

    public static void ApplyCityName(Realm kingdom)
    {
        if (!IsCityState(kingdom) || kingdom.Capital == null || kingdom.Capital.IsGone()) return;
        string name = kingdom.Capital.GetCityName();
        if (string.IsNullOrWhiteSpace(name)) return;
        kingdom.Name = name;
        kingdom.RememberInitialRandomKingdomName(name, overwrite: true);
        kingdom.SetKingdomCoreName(name, name);
    }
#endif

    // 城邦加入古典共和帝国(诸侯加入封建制帝国)后完全自治：保留外交权和军队，不向中央输兵。
    // "不交税"是不向中央上缴(古典共和的自治城邦不交，封建诸侯照封建成员的分成交，见 TreasurySystem.TaxShares)，
    // 不是本国不收税：以前把税级设成"无"，成员一分税收不上；现在税级为"无"的改回"低"，原有的税级不动。
    // 帝国自由城市、主教领(见 HolyRomanSystem.Members)本就没有外交开战权，不给
#if EC_PENDING
    public static void GrantAutonomy(Realm kingdom)
    {
        Regime regime = kingdom?.GetRegime();
        if (regime == null) return;
        if (HolyRomanSystem.Estate(kingdom) == HolyRomanSystem.EstateNone)
        {
            regime.SetAllowDiplomacy(true);
            regime.SetAllowArmy(true);
            regime.SetAllowSupportCenterArmy(false);
        }
        TreasurySystem.EnsureCollectsTax(regime);
    }
#endif

    // 旧档修补(每年随帝国年度结算，见 ConstitutionalEconomySystem)：帝国自由城市、主教领入帝国时被 GrantAutonomy
    // 误给了外交开战权，收回；诸侯订立和约、各家取得完全主权之后不收
#if EC_PENDING
    public static void RepairEstateAutonomy(Empire empire)
    {
        if (empire?.data == null || empire.kingdoms_list == null || empire.data.holy_roman?.sovereign_princes == true) return;
        foreach (Realm member in empire.kingdoms_list)
        {
            if (member?.Record == null || member.IsGone() || member == empire.CoreKingdom ||
                HolyRomanSystem.Estate(member) == HolyRomanSystem.EstateNone) continue;
            Regime regime = member.GetRegime();
            if (regime == null || !regime.IsAllowDiplomacy()) continue;
            regime.SetAllowDiplomacy(false);
            regime.SetAllowSupportCenterArmy(false);
        }
    }
#endif

    // CityManager.buildNewCity 之后调用：开国的第一座城用来定国名，之后新建的城独立为殖民城邦
#if EC_PENDING
    public static void OnCityBuilt(City city, Person founder)
    {
        Realm mother = city?.Realm;
        if (!IsCityState(mother)) return;
        if (mother.Cities.Count <= 1 || city == mother.Capital)
        {
            ApplyCityName(mother);
            return;
        }
        if (IsPrincipateRealm(mother)) return;
        Person leader = founder != null && !founder.IsGone() && founder.Alive && !founder.IsRuler() &&
                       founder.City == city
            ? founder
            : FindLocalLeader(city, null);
        if (leader == null) return;
        Realm colony = SpinOff(city, leader, mother);
        if (colony == null) return;
        bool inEmpire = Attach(mother, colony);
        Record(mother, inEmpire ? "city_state_colony_empire_history" : "city_state_colony_history",
            mother.GetKingdomName(), colony.GetKingdomName());
    }
#endif

    // 一城一国：城邦名下多出来的城(建城时还没施行城邦共和、后来才改制；和约割让、归附等别的来路)每月分出一座，
    // 由本城的主事者立为殖民城邦，与母邦结盟、共用一位君主(母邦在帝国里的并入帝国)。RoyalHouseSystem 每月调用
#if EC_PENDING
    public static void EnforceOneCity()
    {
        if (EmpireWorld.Current?.LiveRealms == null) return;
        foreach (Realm mother in EmpireWorld.Current.LiveRealms.ToList())
        {
            if (!IsCityState(mother) || mother.Capital == null || mother.Cities == null || mother.Cities.Count <= 1 ||
                EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(mother) ||
                IsPrincipateRealm(mother)) continue;
            City city = mother.Cities.Where(candidate => candidate?.Record != null && !candidate.IsGone() &&
                                                         candidate != mother.Capital && !candidate.GetOrCreate().placed_by_hand)
                .OrderBy(candidate => candidate.Key).FirstOrDefault();
            if (city == null) continue;
            if (!city.HasLeader()) CityPopulationSystem.EnsureLeader(city);
            Person current = city.Leader;
            Person leader = current != null && !current.IsGone() && current.Alive && current.IsAdult() &&
                           !current.IsRuler() && current.IsFitToRule()
                ? current
                : FindLocalLeader(city, mother.Ruler);
            if (leader == null) continue;
            try
            {
                Realm colony = SpinOff(city, leader, mother);
                if (colony == null) continue;
                bool inEmpire = Attach(mother, colony);
                Record(mother, inEmpire ? "city_state_colony_empire_history" : "city_state_colony_history",
                    mother.GetKingdomName(), colony.GetKingdomName());
            }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][城邦] {mother.GetKingdomName()} 分出殖民城邦失败: {exception.Message}");
            }
        }
    }
#endif

    // CityPatch.FinishedCapture 调用。返回 true 表示已按城邦规则处理，城市不归攻城方。
#if EC_PENDING
    public static bool TryResolveCapture(City city, Realm captor, Realm defender)
    {
        if (city == null || !IsCityState(captor) || defender == null || defender == captor || defender.IsGone() ||
            IsPrincipateRealm(captor))
            return false;
        string captorName = captor.GetKingdomName();

        // 对方只剩这一座城：整国加入攻城方的同盟
        if (defender.Cities.Count <= 1 && !defender.IsEmpire())
        {
            EndWarsBetween(captor, defender);
            bool joinedEmpire = Attach(captor, defender);
            Record(captor, joinedEmpire ? "city_state_submit_empire_history" : "city_state_submit_alliance_history",
                captorName, defender.GetKingdomName());
            return true;
        }

        // 否则由本地贵族把这座城立为新城邦，加入攻城方的同盟；找不到人就守不住，城仍归原主
        Person leader = FindLocalLeader(city, defender.Ruler);
        if (leader == null) return true;
        if (defender.Ruler?.City == city) defender.kingFledCity();
        Realm newState = SpinOff(city, leader, captor);
        if (newState == null) return true;
        bool spunIntoEmpire = Attach(captor, newState);
        Record(captor, spunIntoEmpire ? "city_state_capture_spin_empire_history" : "city_state_capture_spin_history",
            captorName, newState.GetKingdomName(), leader.FullName());
        return true;
    }

    private static Realm SpinOff(City city, Person leader, Realm parent)
    {
        // 攻下的城立为城邦(战果)：不许自立的闸放行(见 SecessionGate)
        Realm state = SecessionGate.Sanctioned(() => city.makeOwnKingdom(leader));
        if (state == null) return null;
        // 同文化的殖民城邦先继承母邦现行制度，之后再由各国逐步完成封建化。
        if (parent != null && IsCityState(parent) &&
            CultureService.GetRealmCulture(state) == CultureService.GetRealmCulture(parent) &&
            state.GetRegime()?.type != RegimeType.ClassicalRepublic)
        {
            state.SetRegimeType(RegimeType.ClassicalRepublic);
            state.LoadRegime();
            state.SystemChange();
        }
        if (state.HasMainTitle()) state.RemoveMainTitle();
        ApplyCityName(state);
        return state;
    }
#endif

    // 本地领袖：优先贵族阶层，其次城中其他成年人，按威望取最高者
#if EC_PENDING
    internal static Person FindLocalLeader(City city, Person exclude)
    {
        List<Person> locals = (city.units ?? new List<Person>()).ToList()
            .Where(actor => actor != null && actor != exclude && !actor.IsGone() && actor.Alive &&
                            actor.isAdult() && !actor.isKing() && actor.isUnitFitToRule())
            .ToList();
        return locals.Where(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)
                   .OrderByDescending(actor => actor.Record?.Renown ?? 0).FirstOrDefault()
               ?? locals.OrderByDescending(actor => actor.Record?.Renown ?? 0).FirstOrDefault();
    }

    public static bool TryCrownLocalRuler(Realm realm)
    {
        City city = realm?.Capital;
        if (city == null || city.IsGone()) return false;
        Person leader = city.Leader;
        if (leader == null || leader.IsGone() || !leader.Alive || !leader.IsAdult() ||
            leader.IsRuler())
            leader = FindLocalLeader(city, realm.Ruler);
        if (leader == null) return false;
        GraceEdictService.Crown(realm, leader);
        return realm.Ruler == leader;
    }

    private static void EndWarsBetween(Realm victor, Realm loser)
    {
        foreach (War war in loser.GetWars().ToList())
        {
            bool opposing = (war.Attackers.Contains(victor) && war.Defenders.Contains(loser)) ||
                            (war.Defenders.Contains(victor) && war.Attackers.Contains(loser));
            if (opposing && war.Alive && !war.HasEnded()) war.lostWar(loser);
        }
    }
#endif

    // 攻城方/母邦在帝国里：并入该帝国(先退出原版联盟，两者不兼容)，返回 true；否则入盟，返回 false
#if EC_PENDING
    private static bool Attach(Realm leader, Realm member)
    {
        Empire empire = leader?.GetEmpire();
        if (empire != null && !empire.IsGone() && !empire.IsArchived())
        {
            if (member.hasAlliance()) member.getAlliance().leave(member);
            empire.join(member, pForce: true);
            return true;
        }
        // 别的帝国的成员被城邦打下、入城邦同盟：先退出原帝国(帝国与同盟互不兼容)。以前留在帝国里，帝国的仗又把它拉回去，
        // 对面的城邦一打再打，同一座城一会儿入这家同盟、一会儿入那家，日志里成串地刷"即将被…捕获"
        Empire former = member.GetEmpire();
        if (former != null && !former.IsGone() && !former.IsArchived() && former.CoreKingdom != member) former.leave(member);
        PersonalUnionService.JoinAlliance(leader, member);
        ShareSovereign(leader, member);
        return false;
    }
#endif

    // 入盟的城邦同步盟主(战胜国/母邦)的颜色，并与盟主共用同一位君主；联盟以盟主城邦的名字命名。
    // 这里直接 setKing 而不走官职任命：任命会把君主迁到对方都城，他就离开了自己的城邦。
#if EC_PENDING
    private static void ShareSovereign(Realm leader, Realm member)
    {
        if (leader == null || member == null || member.IsGone()) return;
        member.updateColor(leader.getColor());
        Person sovereign = leader.Ruler;
        if (sovereign == null || sovereign.IsGone() || !sovereign.Alive) return;
        PersonalUnionService.CrownInUnion(member, sovereign);
        PersonalUnionService.NameAllianceAfterLeader(leader);
    }

    private static void Record(Realm kingdom, string key, params object[] args)
    {
        string content = string.Format(LM.Get(key), args);
        LogService.LogInfo(content);
        Empire empire = kingdom?.GetEmpire();
        if (empire != null && !empire.IsGone()) empire.RecordHistory(directContent: content, kingdomId: kingdom.Key);
    }
#endif
}
}
