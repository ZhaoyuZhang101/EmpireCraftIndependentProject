// Generated from the EmpireCraft mod: Scripts/GeneralSystems/GraceEdictService.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 推恩令(华夏制度树「推恩令」节点)。本文化施行后，帝国内实行世袭的封国国王去世时：
//   · 有多个儿子、封国不止一城：嫡长子承袭封国(都城与原法理头衔不变)，其余城市按重要程度
//     逐一分给诸子，各自立为只用城市名、没有法理头衔的小封国；城多于子时多出的城归嫡长子；
//   · 绝嗣(没有可以继承的儿子)：封国全部城市收归帝国，设为行政区(郡县)，原法理由行政区接管；
//   · 其余情况(只有一个儿子、只有一座城)照常继承，不在这里处理。
// 法理的归属逻辑没有变：原法理头衔仍在嫡长子一支手里，分出去的只是城市。
public static class GraceEdictService
{
    // 推恩令 / 郡国并行(封国绝嗣则除国为郡，分封制下也能设郡) 分别由制度特性
    // grace_edict / commandery_kingdom 提供，任何线的节点都可以声明。

    public static bool IsCommanderyKingdomActive(Empire empire) =>
        empire != null && !empire.IsGone() && !empire.IsArchived() &&
        InstitutionSystem.HasFeature(empire, InstitutionFeatures.CommanderyKingdom);

    // 由 EmpireCraftKingdomBehCheckKing 在封国没有国王时调用；返回 true 表示已按推恩令处理完毕。
#if EC_PENDING
    public static bool TryHandleVacancy(Realm fief)
    {
        if (fief == null || fief.IsGone() || fief.HasRuler()) return false;
        KingdomExtension.KingdomExtraData data = fief.GetOrCreate();
        long lastKingIdentityId = data.last_king_identity_id;
        if (lastKingIdentityId < 0) return false;
        data.last_king_identity_id = -1L;

        Empire empire = fief.GetEmpire();
        if (fief.IsEmpire() || empire == null || empire.IsGone() || empire.IsArchived() ||
            empire.CoreKingdom == null || empire.CoreKingdom == fief || ZhouEraSystem.RoyalReclaimBlocked(empire))
            return false;
        bool graceEdict = InstitutionSystem.HasFeature(empire, InstitutionFeatures.GraceEdict);
        bool commanderyKingdom = IsCommanderyKingdomActive(empire);
        // 封建制帝国的分割继承(见 HolyRomanSystem.RollPartible)：诸子分城，绝嗣照常继承
        bool partible = !graceEdict && HolyRomanSystem.RollPartible(empire, fief);
        // 附庸王国、亲王封地、行省制汗国的兀鲁思：绝嗣并为行省/辖区(见 PartialEnfeoffment.RevertsOnExtinction)
        bool partialGrant = PartialEnfeoffment.RevertsOnExtinction(empire, fief);
        if (!graceEdict && !commanderyKingdom && !partible && !partialGrant) return false;
        Regime regime = fief.GetRegime();
        if (regime == null || regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession) return false;

        PersonalClanIdentity lastKing = SpecificClanManager.getPerson(lastKingIdentityId);
        if (lastKing == null) return false;
        List<Person> sons = VirtualGenealogySystem.RealizeAll(SpecificClanManager.getChildren(lastKing)
                .Select(item => item.Item2)
                .Where(identity => identity != null && identity.CanHeir())
                .OrderBy(identity => identity.rank),
            actor => !actor.IsGone() && actor.Alive && !actor.IsRuler() && actor.Realm?.GetEmpire() == empire);

        if (sons.Count == 0)
        {
            if (!graceEdict && !commanderyKingdom && !partialGrant) return false;
            string fiefName = fief.GetKingdomName();
            Realm division = Reclaim(empire, fief, lastKing.name, announce: graceEdict);
            if (!graceEdict && commanderyKingdom)
                Record(empire, null, "commandery_kingdom_reclaim_history", lastKing.name, fiefName);
            else if (!graceEdict)
                Record(empire, null, "partial_grant_reclaim_history", lastKing.name, fiefName,
                    division?.GetKingdomFullName() ?? empire.CoreKingdom.GetKingdomName());
            return true;
        }
        // 只有郡国并行、没有推恩令时，有子嗣就照常继承，不拆分
        if (!graceEdict && !partible) return false;

        List<City> otherCities = (fief.Cities ?? new List<City>())
            .Where(city => city != null && !city.IsGone() && city != fief.Capital)
            .OrderByDescending(city => city.GetCityStrategicValue().Total).ThenBy(city => city.Key)
            .ToList();
        if (sons.Count < 2 || otherCities.Count == 0) return false;

        Person eldest = sons[0];
        var granted = new List<string>();
        foreach ((Person son, City city) in sons.Skip(1).Zip(otherCities, (son, city) => (son, city)))
        {
            if (CreateCityFief(empire, regime, city, son))
                granted.Add($"{son.FullName()}({city.GetCityName()})");
        }
        Crown(fief, eldest);

        string content = string.Format(LM.Get(partible ? "hre_partible_history" : "grace_edict_partition_history"), lastKing.name, eldest.FullName(),
            fief.GetKingdomName(), string.Join("、", granted));
        empire.RecordHistory(directContent: content, actorId: eldest.Key, kingdomId: fief.Key);
        EmpireCraft.Core.TranslateHelper.LogEventMessage(content, fief);
        return true;
    }
#endif

    // 次子们各得一城：立为只用城市名的小封国，不带法理头衔，同样世袭、归属帝国
#if EC_PENDING
    private static bool CreateCityFief(Empire empire, Regime parentRegime, City city, Person son)
    {
        if (city == null || city.IsGone() || EnfeoffmentHelper.IsRealmCapital(city)) return false;
        // 推恩：受命立国，不许自立的闸放行(见 SecessionGate)
        Realm cityFief = SecessionGate.Sanctioned(() => city.MakeOwnRealm(son));
        if (cityFief == null) return false;
        cityFief.SetRegimeType(parentRegime.type);
        cityFief.LoadRegime();
        Regime regime = cityFief.GetRegime();
        if (regime != null)
        {
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            regime.SetAllowSupportCenterArmy(false);
            // 税级是本国向百姓收税的税率(见 KingdomExtension.GetTaxRate)：以前设成"无"，分出来的小封国一分税收不上
            regime.SetTaxLevel(TaxLevel.Low);
        }
        if (cityFief.HasMainTitle()) cityFief.RemoveMainTitle();
        string cityName = city.GetCityName();
        cityFief.Name = cityName;
        cityFief.SetKingdomCoreName(cityName, cityName);
        cityFief.SetFiedTimestamp(GameClock.Now);
        empire.join(cityFief, true, true);
        return true;
    }

    internal static void Crown(Realm fief, Person heir)
    {
        heir.RemoveFromArmy();
        if (heir.IsCityLeader()) heir.City.RemoveLeader();
        OfficeObject office = fief.GetOffice();
        if (office == null)
        {
            fief.InitialRegime();
            office = fief.GetOffice();
        }
        if (office != null)
        {
            office.meta_object = fief;
            office.SetActor(heir);
        }
        else
        {
            fief.SetRuler(heir);
        }
    }
#endif

    public static bool IsActive(Empire empire) =>
        empire != null && !empire.IsGone() && !empire.IsArchived() && InstitutionSystem.HasFeature(empire, InstitutionFeatures.GraceEdict);

    // 推恩令下一人不得兼领两国：某人刚成为 kingdom 的国王时，如果他还是同一帝国里别的封国的国王，
    // 那个封国从他的宗室里另立一君(子女→兄弟姐妹→同宗族其他成年人)；宗室无人可立则收归郡县。
    // 由 KingdomPatch 的 setKing 补丁调用。帝国核心王国不会被这样处理。
#if EC_PENDING
    public static void OnKingCrowned(Realm kingdom, Person king)
    {
        if (kingdom == null || kingdom.IsGone() || king == null || king.IsGone()) return;
        Empire empire = kingdom.GetEmpire();
        if (!IsActive(empire)) return;
        List<Realm> otherRealms = EmpireWorld.Current.LiveRealms.Where(other =>
                other != null && other != kingdom && !other.IsGone() && other.Ruler == king &&
                other.GetEmpire() == empire && other != empire.CoreKingdom)
            .ToList();
        foreach (Realm other in otherRealms)
        {
            Person successor = FindClanSuccessor(empire, king);
            if (successor != null)
            {
                Crown(other, successor);
                Record(empire, other, "grace_edict_double_rule_history", king.FullName(), kingdom.GetKingdomName(),
                    other.GetKingdomName(), successor.FullName());
            }
            else
            {
                string otherName = other.GetKingdomName();
                Reclaim(empire, other, king.FullName(), announce: false);
                Record(empire, null, "grace_edict_double_rule_reclaim_history", king.FullName(),
                    kingdom.GetKingdomName(), otherName);
            }
        }
    }

    private static Person FindClanSuccessor(Empire empire, Person king)
    {
        bool IsAvailable(Person actor) => actor != null && actor != king && !actor.IsGone() && actor.Alive &&
                                         actor.IsAdult() && !actor.IsRuler() && actor.Realm?.GetEmpire() == empire;
        PersonalClanIdentity identity = king.GetPersonalIdentity();
        Person Relative(IEnumerable<(ClanRelation, PersonalClanIdentity)> list) => VirtualGenealogySystem.RealizeFirst(
            list.Select(item => item.Item2).Where(person => person != null && person.CanHeir())
                .OrderBy(person => person.rank), IsAvailable);
        if (identity != null)
        {
            Person child = Relative(SpecificClanManager.getChildren(identity));
            if (child != null) return child;
            Person sibling = Relative(SpecificClanManager.GetSiblingsWithRelation(identity));
            if (sibling != null) return sibling;
        }
        // 同族远支：地图上没有就从族谱里落成一位(纯净地图下多是虚拟族人，以前找不到就把封国收归郡县)
        return king.GetSpecificClan()?.AllAliveMembers?.Where(IsAvailable)
                   .OrderByDescending(actor => actor.Age()).FirstOrDefault() ??
               KinRoster.RealizeAdultKin(king.GetSpecificClan(), realm => realm?.GetEmpire() == empire, IsAvailable);
    }
#endif

    private static void Record(Empire empire, Realm kingdom, string key, params object[] args)
    {
        string content = string.Format(LM.Get(key), args);
        empire.RecordHistory(directContent: content, kingdomId: kingdom?.Key ?? empire.CoreKingdom?.Key ?? -1L);
        EmpireCraft.Core.TranslateHelper.LogEventMessage(content, kingdom ?? empire.CoreKingdom);
    }

    // 绝嗣：城市收归核心王国，再一并设为行政区；有法理的由行政区接管原法理
#if EC_PENDING
    private static Realm Reclaim(Empire empire, Realm fief, string lastKingName, bool announce = true)
    {
        KingdomTitle title = fief.GetMainTitle();
        string fiefName = fief.GetKingdomName();
        List<City> cities = (fief.Cities ?? new List<City>()).Where(city => city != null && !city.IsGone()).ToList();
        foreach (City city in cities) city.JoinRealm(empire.CoreKingdom);
        List<City> reclaimed = cities.Where(city => city.Realm == empire.CoreKingdom).ToList();
        Realm division = reclaimed.Count > 0 ? empire.EstablishAdministrativeDivision(reclaimed, title) : null;
        if (!announce) return division;

        string content = string.Format(LM.Get("grace_edict_reclaim_history"), lastKingName, fiefName);
        empire.RecordHistory(directContent: content, kingdomId: empire.CoreKingdom.Key);
        EmpireCraft.Core.TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
        return division;
    }
#endif
}
}
