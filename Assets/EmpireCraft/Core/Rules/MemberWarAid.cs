// Generated from the EmpireCraft mod: Scripts/GeneralSystems/MemberWarAid.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Linq;

namespace EmpireCraft.Core
{

// 成员的仗：中央不直接插手行省、诸侯、军府与外敌的战争——
//   · 求援(member_request_aid)：挨打的成员由长官发起决议，决议通过后中央才出兵加入守方；
//     世袭诸侯、藩镇(有自主外交的)还要中央对它有好感才肯出兵；
//   · 驰援邻省(member_aid_neighbour)：与挨打成员接壤的成员也要发起决议才出兵相助；
//   · 中央自己的仗(中央或盟友领头)，听命于中央的成员照旧随征；
//     成员的仗即便中央已出兵，别的成员也不自动跟进(见 FeudalVassalService.ExpandSide、CheckJoinWar)。
public static class MemberWarAid
{
    public const string RequestPlot = "member_request_aid";
    public const string NeighbourPlot = "member_aid_neighbour";
    public const string CenterPlot = "member_aid_center";

    // 某一方领头的是本帝国的非核心成员：这是成员的仗，不是中央的
    public static bool IsMemberWar(War war, Empire empire, bool attackers)
    {
        if (war?.Record == null || empire?.CoreKingdom == null) return false;
        Realm lead = attackers ? war.GetMainAttacker() : war.GetMainDefender();
        return lead != null && lead != empire.CoreKingdom && lead.GetEmpire() == empire;
    }

    // 王国 AI 每月一查：长官没在办别的决议时，本国挨打就向中央求援，邻近成员挨打就议驰援
#if EC_PENDING
    public static void TryRaise(Realm member)
    {
        Person king = member?.Ruler;
        if (king == null || king.IsGone() || !king.Alive || king.plot?.isActive() == true ||
            EmpireWorld.Current?.plots == null || Context(member) == null) return;
        string id = FindRequestWar(member) != null ? RequestPlot :
            FindNeighbourWar(member, out _, out _) ? NeighbourPlot :
            FindCenterWar(member) != null ? CenterPlot : null;
        if (id == null) return;
        PlotAsset asset = AssetManager.plots_library.get(id);
        asset?.try_to_start_advanced?.Invoke(king, asset, false);
    }

    public static War FindRequestWar(Realm member) =>
        Context(member) == null ? null : member.GetWars().FirstOrDefault(war => RequestValid(member, war));

    public static bool FindNeighbourWar(Realm member, out War target, out Realm ally)
    {
        target = null;
        ally = null;
        Empire empire = Context(member);
        if (empire == null || member.GetWars().Any(war => war != null && !war.HasEnded())) return false;
        foreach (Realm other in empire.kingdoms_list.ToList())
        {
            War war = other?.GetWars().FirstOrDefault(candidate => NeighbourValid(member, candidate, other));
            if (war == null) continue;
            target = war;
            ally = other;
            return true;
        }
        return false;
    }
#endif

    // 求援：成员是守方，对方没有本帝国的人(叛乱的除外)，中央没下场、也没就这场仗与对方议和过
#if EC_PENDING
    public static bool RequestValid(Realm member, War war)
    {
        Empire empire = Context(member);
        if (empire == null || !Aidable(war, member, empire)) return false;
        Realm core = empire.CoreKingdom;
        if (war.HasRealm(core) || EmpirePeacePatch.HasLeft(core, war, joiningAttackers: false)) return false;
        if (core.GetWars().Any(other => other.GetEmpireWarType() == EmpireWarType.藩王索取皇位)) return false;
        return EmpirePeacePatch.Obeys(empire, member) || core.isOpinionTowardsKingdomGood(member);
    }
#endif

    // 驰援：本成员没在打仗，与挨打的同帝国成员接壤；世袭诸侯、藩镇要对它有好感
#if EC_PENDING
    public static bool NeighbourValid(Realm member, War war, Realm ally)
    {
        Empire empire = Context(member);
        if (empire == null || ally == null || ally == member || ally == empire.CoreKingdom || ally.IsGone() ||
            ally.GetEmpire() != empire || !Aidable(war, ally, empire) || war.HasRealm(member)) return false;
        if (member.GetWars().Any(other => other != null && !other.HasEnded())) return false;
        if (EmpirePeacePatch.HasLeft(member, war, joiningAttackers: false) || !member.IsNeighbourWith(ally)) return false;
        return EmpirePeacePatch.Obeys(empire, member) || member.isOpinionTowardsKingdomGood(ally);
    }

    public static bool StartRequest(Person actor, PlotAsset asset, bool forced)
    {
        Realm member = actor?.Realm;
        if (member?.Ruler != actor || actor.plot?.isActive() == true) return false;
        War war = FindRequestWar(member);
        if (war == null) return false;
        Plot plot = EmpireWorld.Current.plots.newPlot(actor, asset, forced);
        plot.target_war = war;
        plot.target_kingdom = war.GetMainAttacker();
        return true;
    }

    public static bool StartNeighbour(Person actor, PlotAsset asset, bool forced)
    {
        Realm member = actor?.Realm;
        if (member?.Ruler != actor || actor.plot?.isActive() == true) return false;
        if (!FindNeighbourWar(member, out War war, out Realm ally)) return false;
        Plot plot = EmpireWorld.Current.plots.newPlot(actor, asset, forced);
        plot.target_war = war;
        plot.target_kingdom = ally;
        return true;
    }

    public static bool RequestContinues(Person actor) =>
        actor?.Realm?.Ruler == actor && RequestValid(actor.Realm, actor.plot?.target_war);

    public static bool NeighbourContinues(Person actor) =>
        actor?.Realm?.Ruler == actor && NeighbourValid(actor.Realm, actor.plot?.target_war, actor.plot?.target_kingdom);
#endif

    // 求援决议通过：中央加入守方，并为这场仗出征(京师禁军与扩大范围的兵开往成员那里，见 WarScopeSystem)
#if EC_PENDING
    public static bool AnswerRequest(Person actor)
    {
        if (!RequestContinues(actor)) return false;
        Realm member = actor.Realm;
        Empire empire = member.GetEmpire();
        if (!Join(actor.plot.target_war, empire.CoreKingdom)) return false;
        WarScopeSystem.Dispatch(empire.CoreKingdom, actor.plot.target_war);
        empire.data.timestamp_invite_war_cool_down = GameClock.Now;
        TranslateHelper.LogEmpireJoinWar(empire, member);
        empire.RecordHistory(directContent: string.Format(LM.Get("empire_join_member_war_history"),
            empire.GetEmpireFullName(), member.GetKingdomName()), kingdomId: member.Key);
        // 封建制帝国：护佑诸侯，战后按胜负涨帝国权威(见 HolyRomanSystem.Authority)
        HolyRomanSystem.NoteProtection(empire, actor.plot.target_war, member);
        return true;
    }
#endif

    // 驰援决议通过：本成员加入邻近成员的守方
#if EC_PENDING
    public static bool AidNeighbour(Person actor)
    {
        if (!NeighbourContinues(actor)) return false;
        Realm member = actor.Realm;
        Realm ally = actor.plot.target_kingdom;
        if (!Join(actor.plot.target_war, member)) return false;
        WarScopeSystem.Dispatch(member, actor.plot.target_war);
        string text = string.Format(LM.Get("member_aid_neighbour_history"), member.GetKingdomName(), ally.GetKingdomName());
        TranslateHelper.LogEventMessage(text, member);
        member.GetEmpire().RecordHistory(directContent: text, kingdomId: member.Key);
        return true;
    }
#endif

    // 发兵勤王：中央自己的仗吃紧(就近动员升到一级以上，见 WarScopeSystem)，本成员还没为这场仗出征。
    // 已随中央参战的(听命于中央的)出征即可；没参战的(世袭诸侯、藩镇)一并加入中央一方，要对中央有好感。
    // 中央与本帝国(没在叛乱的)成员之间的仗不算。needy = false 时不看中央是否吃紧(谋划已开始、强制发起)
#if EC_PENDING
    public static War FindCenterWar(Realm member, bool needy = true) =>
        Context(member) is Empire empire
            ? empire.CoreKingdom.GetWars().FirstOrDefault(war => CenterValid(member, war, needy))
            : null;

    public static bool CenterValid(Realm member, War war, bool needy = true)
    {
        Empire empire = Context(member);
        Realm core = empire?.CoreKingdom;
        if (core == null || war?.Record == null || !war.Alive || war.HasEnded() || !war.HasRealm(core) ||
            war.GetEmpireWarType() == EmpireWarType.劫掠 || ZhouEraSystem.IsInternalWar(war) || SouthernGarrisonSystem.IsInternalWar(war) ||
            WarScopeSystem.DispatchedFor(member, war)) return false;
        bool attacking = war.IsAttacker(core);
        var opponents = attacking ? war.Defenders : war.Attackers;
        if (opponents.Any(kingdom => kingdom != null && !kingdom.IsGone() && kingdom.GetEmpire() == empire &&
                                     !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling())) return false;
        if (war.HasRealm(member))
        {
            if (attacking ? !war.IsAttacker(member) : !war.IsDefender(member)) return false;
        }
        else if (member.GetWars().Any(other => other != null && !other.HasEnded()) ||
                 EmpirePeacePatch.HasLeft(member, war, joiningAttackers: attacking)) return false;
        if (needy && WarScopeSystem.Level(core) < WarScopeSystem.Wider) return false;
        return EmpirePeacePatch.Obeys(empire, member) || member.isOpinionTowardsKingdomGood(core);
    }

    public static bool StartCenter(Person actor, PlotAsset asset, bool forced)
    {
        Realm member = actor?.Realm;
        if (member?.Ruler != actor || actor.plot?.isActive() == true) return false;
        War war = FindCenterWar(member, needy: !forced);
        if (war == null) return false;
        Plot plot = EmpireWorld.Current.plots.newPlot(actor, asset, forced);
        plot.target_war = war;
        plot.target_kingdom = member.GetEmpire().CoreKingdom;
        return true;
    }

    public static bool CenterContinues(Person actor) =>
        actor?.Realm?.Ruler == actor && CenterValid(actor.Realm, actor.plot?.target_war, needy: false);
#endif

    // 勤王决议通过：没参战的加入中央一方，为这场仗出征
#if EC_PENDING
    public static bool AidCenter(Person actor)
    {
        if (!CenterContinues(actor)) return false;
        Realm member = actor.Realm;
        War war = actor.plot.target_war;
        Realm core = member.GetEmpire().CoreKingdom;
        if (!war.HasRealm(member))
        {
            if (war.IsAttacker(core)) war.JoinAttackers(member);
            else war.JoinDefenders(member);
            if (!war.HasRealm(member)) return false;
        }
        WarScopeSystem.Dispatch(member, war);
        string text = string.Format(LM.Get("member_aid_center_history"), member.GetKingdomName());
        TranslateHelper.LogEventMessage(text, member);
        member.GetEmpire().RecordHistory(directContent: text, kingdomId: member.Key);
        return true;
    }
#endif

    // 能发起这几项决议的成员：帝国的非核心成员、没在叛乱；战国周室已不管诸侯的仗；
    // 分裂进程里割据的成员不奉诏，也不向中央求援、不驰援、不勤王(见 SchismSystem，用户 2026-10-21 选"先割据后独立")
#if EC_PENDING
    private static Empire Context(Realm member)
    {
        if (member?.Record == null || member.IsGone() || member.IsEmpire() ||
            member.IsFactionRebelling() || member.IsLocalRebelling() || SchismSystem.IsBreakaway(member)) return null;
        Empire empire = member.GetEmpire();
        if (empire == null || empire.IsGone() || empire.IsArchived() || empire.CoreKingdom == null ||
            empire.CoreKingdom.IsGone()) return null;
        return ZhouEraSystem.Era(empire) >= ZhouEraSystem.Warring ? null : empire;
    }
#endif

    // 成员守、对方是外敌(本帝国叛乱的成员也算外敌)；劫掠与周室诸侯间的征伐不算
#if EC_PENDING
    private static bool Aidable(War war, Realm defender, Empire empire)
    {
        if (war?.Record == null || !war.Alive || war.HasEnded() || !war.IsDefender(defender) ||
            war.GetEmpireWarType() == EmpireWarType.劫掠 || ZhouEraSystem.IsInternalWar(war) ||
            SouthernGarrisonSystem.IsInternalWar(war)) return false;
        return !war.Attackers.Any(kingdom => kingdom != null && !kingdom.IsGone() &&
                                                   kingdom.GetEmpire() == empire &&
                                                   !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling());
    }
#endif

    private static bool Join(War war, Realm kingdom)
    {
        if (war?.Record == null || kingdom == null || kingdom.IsGone() || war.HasEnded() || war.HasRealm(kingdom))
            return false;
        war.JoinDefenders(kingdom);
        return war.Defenders.Contains(kingdom);
    }
}
}
