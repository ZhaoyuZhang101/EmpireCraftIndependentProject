// Generated from the EmpireCraft mod: Scripts/GeneralSystems/GreatPowerDiplomacy.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Vector2 = EmpireCraft.Core.EcVector2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 跨文明大国外交(用户 2026-10-19 选"跨文明大国外交"；以前同物种的两个帝国一律 -999 好感、帝国之间不能结好)：
//   · 两个皇帝：同一文明(同天下，见 CultureService.SameKin)的两个帝国是宿敌(-200)；不同文明的帝国按远近相忌——
//     都城相距远的几乎无碍(-5)，中等(-20)，近的(-50)；通达层算好后(用户 2026-11-06 同意，见 ConnectivitySystem)不量都城直线距离，
//     按两国人员往来的隔绝度(隔着高原大漠的算远)：≥0.8 −5、≥0.5 −20，否则 −50；
//   · 通好：不同文明的两个帝国不在打仗、好感不太坏时，每年有机会遣使通好(有共同的敌人时更容易)——互认对方的称号
//     (皇帝、大可汗、哈里发、元首……)，三十年内互不侵犯(见 WarGate、WarTargetPatch)、好感 +40；
//     一方遭第三国攻打，另一方没在打仗、过得了开战闸门时，每月有机会依约出兵相助；
//   · 只与够得着的帝国通好、只援够得着的仗(用户 2026-11-04 "超远距离外交的bug"；以前任意两个不同文明的帝国都能通好、隔半个地图出兵)：
//     同宣战的远近(WarReach.Reachable：两个帝国连同成员接壤、同一块陆地上相距不远且不隔着过不去的无人区，或隔海在水师辐射内)；
//     通达层算好后通好改看使节往来：两边谁派使节过去的人员强度不低于 diplomacy_min_strength(0.25)就行(航海档，不要战舰)；
//     援助是出兵，另要够得着来犯的那一方(照旧同宣战)。
public static class GreatPowerDiplomacy
{
    private const int PactYears = 30, PactOpinion = 40;
    private const float PactChance = 0.08f, CommonEnemyChance = 0.4f, AidChance = 0.15f, MinOpinion = -60f;
    // 同一天下的两个皇帝：-999 → -200(用户 2026-10-21 选"同天下帝国不再死斗")。仍是宿敌、好感照样压到谈不拢，
    // 但不再是什么都抵不过的死仇；罗马天下有六个文化、最多并立六个帝国，以前彼此 -999，西欧内耗最重。华夏、乌玛一样
    private const int SameCivilizationRivalry = -200;
    private static double _lastScan = -1d;

    public static void ResetWorldState() => _lastScan = -1d;

    private static Dictionary<long, double> Pacts(Empire empire) => empire.data.diplomatic_pacts ??= new Dictionary<long, double>();

    private static bool Active(Empire a, Empire b) =>
        a?.data != null && b?.data != null && Pacts(a).TryGetValue(b.Key, out double since) && since >= 0d &&
        GameClock.YearsSince(since) < PactYears;

    // 两国(或所在的帝国)通好之中：互不侵犯
    public static bool Bound(Realm a, Realm b)
    {
        Empire ea = a?.GetEmpire(), eb = b?.GetEmpire();
        return ea != null && eb != null && ea != eb && Active(ea, eb);
    }

    private static bool SameCivilization(Realm a, Realm b) =>
        CultureService.SameKin(CultureService.GetRealmCulture(a), CultureService.GetRealmCulture(b));

    // 两个皇帝之间的好感(见 EmpireCraftOpinionAddition)
    public static int EmperorRivalry(Realm a, Realm b)
    {
        if (Bound(a, b)) return 0;
        if (SameCivilization(a, b)) return SameCivilizationRivalry;
        if (a.Capital == null || b.Capital == null) return -20;
        float isolation = ConnectivitySystem.Isolation(a, b, ConnectivityMode.People);
        if (isolation >= 0f)
        {
            ConnectivityConfig config = ConnectivitySystem.Config;
            return isolation >= config.rivalry_far_isolation ? config.rivalry_far
                : isolation >= config.rivalry_mid_isolation ? config.rivalry_mid
                : config.rivalry_near;
        }
        float distance = Vector2.Distance(a.Capital.Center, b.Capital.Center);
        return distance > 300f ? -5 : distance > 150f ? -20 : -50;
    }

    // 通好之国的好感
    public static int PactOpinionOf(Realm main, Realm target) =>
        main != null && target != null && main.IsEmpire() && target.IsEmpire() && Bound(main, target) ? PactOpinion : 0;

    public static void TryYearlyScan()
    {
        if (EmpireWorld.Current == null || ModClass.EMPIRE_MANAGER == null || ModClass.IS_CLEAR) return;
        // 世界法则「禁止AI通好」(见 DiplomacyLaws)
        if (DiplomacyLaws.PactBlocked) return;
        double now = GameClock.Now;
        if (_lastScan >= 0d && now >= _lastScan && GameClock.YearsSince(_lastScan) < 1) return;
        _lastScan = now;
        List<Empire> empires = ModClass.EMPIRE_MANAGER.Where(empire => empire?.data != null && !empire.IsGone() &&
            !empire.IsArchived() && empire.CoreKingdom?.Record != null && !empire.CoreKingdom.IsGone()).ToList();
        for (int i = 0; i < empires.Count; i++)
        for (int j = i + 1; j < empires.Count; j++)
        {
            try
            {
                Consider(empires[i], empires[j]);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][通好] {empires[i].GetEmpireName()}、{empires[j].GetEmpireName()} 结算失败: {exception.Message}");
            }
        }
    }

    private static void Consider(Empire a, Empire b)
    {
        Realm ca = a.CoreKingdom, cb = b.CoreKingdom;
        if (Active(a, b) || SameCivilization(ca, cb) || ca.IsAtWarWith(cb) || BarredByConquest(ca, cb)) return;
        float opinion = EmpireWorld.Current.Diplomacy?.GetOpinion(ca, cb)?.Total ?? 0f;
        if (opinion < MinOpinion) return;
        bool common = ca.GetEnemies().Intersect(cb.GetEnemies()).Any();
        if (EcRandom.value >= (common ? CommonEnemyChance : PactChance)) return;
        // 够不着的不通好(见类头)：先掷机会再量远近，省得每年两两都量
        if (!(ConnectivitySystem.CanContactEither(ca, cb) ?? WarReach.Reachable(ca, cb))) return;
        double now = GameClock.Now;
        Pacts(a)[b.Key] = now;
        Pacts(b)[a.Key] = now;
        string text = string.Format(LM.Get("great_power_pact_history"), a.GetEmpireName(), b.GetEmpireName(), Title(ca), Title(cb));
        a.RecordHistory(directContent: text, kingdomId: ca.Key);
        b.RecordHistory(directContent: text, kingdomId: cb.Key);
        TranslateHelper.LogEventMessage(text, ca);
    }

    // 编旗的政权(入关前的后金也算)要入主中原：不与华夏各朝通好(一纸三十年互不侵犯，入关就打不下去了)
    private static bool BarredByConquest(Realm a, Realm b) =>
        BannerSystem.Organized(a) && IsHuaxia(b) || BannerSystem.Organized(b) && IsHuaxia(a);

    private static bool IsHuaxia(Realm kingdom) =>
        InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) == "Huaxia";

    private static string Title(Realm core)
    {
        RegimeType? regime = core.GetRegime()?.type;
        // 共和国元首照元首称号(以前陕甘宁边区"互认总统、皇帝之号")；剧本定下的职位优先
        Empire empire = core.GetEmpire();
        if (empire != null && empire.CoreKingdom == core && RepublicSystem.IsRepublic(empire)) return RepublicSystem.GetHeadOfStateTitle(empire);
        if (empire != null && empire.CoreKingdom == core && RepublicSystem.CustomHeadTitle(empire) is string custom) return custom;
        return regime == null ? LM.Get("actor_emperor_B") : LM.Get(EmpireCraft.Core.RegimeVariants.RankKey(regime.Value, 0, core));
    }

    // 依约相助(王国 AI 每月一查，帝国核心自己查)
    public static void Aid(Realm core)
    {
        Empire empire = core?.GetEmpire();
        if (empire?.data?.diplomatic_pacts == null || empire.CoreKingdom != core || core.HasEnemies() ||
            EcRandom.value >= AidChance) return;
        foreach (long partnerId in empire.data.diplomatic_pacts.Keys.ToList())
        {
            Empire partner = ModClass.EMPIRE_MANAGER.Get(partnerId);
            Realm ally = partner?.CoreKingdom;
            if (ally?.Record == null || ally.IsGone() || !Active(empire, partner)) continue;
            War war = ally.GetWars().FirstOrDefault(candidate =>
                candidate?.Record != null && candidate.Alive && !candidate.HasEnded() && candidate.IsDefender(ally) &&
                !candidate.HasRealm(core) && candidate.MainAttacker?.GetEmpire() != empire);
            Realm attacker = war?.MainAttacker;
            if (attacker?.Record == null || attacker.IsGone() || EmpirePeacePatch.HasLeft(core, war, joiningAttackers: false) ||
                WarSituation.TooBusyForWar(core) || CorpsSystem.CannotFundWar(core) || !WarGate.CanDeclare(core, attacker) ||
                !WarReach.Reachable(core, attacker)) continue;
            war.JoinDefenders(core);
            if (!war.Defenders.Contains(core)) continue;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("great_power_aid"), empire.GetEmpireName(),
                partner.GetEmpireName(), attacker.GetKingdomName()), core);
            return;
        }
    }
}
}
