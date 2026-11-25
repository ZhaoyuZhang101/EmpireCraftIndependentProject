// Generated from the EmpireCraft mod: Scripts/GeneralSystems/SouthernGarrisonSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 南明诸镇(用户 2026-11-10 "而且你这南明太团结了""至少要做成战国那种""只是明面上的天子"，问答选"藩镇留在帝国里""藩镇降清")：
// 南明结成以后(见 SouthernRetreatSystem)，朝廷只是名义上的天子——
//   · 诸镇：核心以外的成员(行省、来投的义军)一律改为世袭藩镇(律令制军府，见 EmpireCraftKingdomBehCheckKingdomType.IsHereditaryGarrison)：
//     自收自支、自主外交、不奉诏(见 EmpirePeacePatch.Obeys)，中央不替它付饷；直隶(京畿)仍归朝廷。期内每年再看一遍，
//     新划出的行省、后来投的义军也改。各镇以朝廷的颜色为基准取同一色系(见 KingdomColorService.ApplyFamily)：
//     国家图层看得出诸镇林立，帝国图层仍是一整个南明；
//   · 互相兼并：每年每镇有 FeudChance 的机会向接壤、国力不到自己 1/FeudPowerRatio(或交恶)的藩镇开战，目标是对方都城；
//     一个南明一年至多 FeudsPerYear 场，同一对打完 minimum_years_between_wars 年内不再打。只打藩镇，不打朝廷；
//     朝廷与别的成员不来援、不拉姻亲同盟(见 IsInternalWar、HolyRomanSystem.ContainedWar)；
//   · 举镇降敌：入侵者兵临(与朝廷交战中)、与入侵者接壤、国力不到入侵者 SurrenderPowerShare 的藩镇，
//     每年按入侵者的压力(占同族之城的比例，见 ScenarioEventSystem.ConquestPressure)× SurrenderChance 举镇投降，
//     连城带兵并入入侵者的帝国，在那边仍是世袭藩镇(三藩那样)；一年至多一镇。
//     归附来的流寇余部(顺、西，见 ScenarioEventSystem.Submit)与入侵者有血仇，不降。
// 期限：南明结成后偏安的那些年(SouthernRetreatRules.Sheltered)，以及京师(帝国法理首都)没收回之前。期满不再兼并、不再降敌，
// 藩镇照旧是世袭藩镇(要削藩走撤销军府)。
public static class SouthernGarrisonSystem
{
    private const float FeudChance = 0.15f, FeudPowerRatio = 1.3f;
    private const int FeudsPerYear = 2;
    private const float SurrenderChance = 0.3f, SurrenderPowerShare = 0.5f;

    // 正由本系统兴起诸镇火并(startWar 前后)：开战钩子据此不拉姻亲、同盟，不升级(见 HolyRomanSystem.ContainedWar)
    public static bool Declaring;

    private static readonly Dictionary<long, double> Yearly = new();
    private static object _world;

    private static bool Alive(Realm kingdom) => kingdom?.Record != null && !kingdom.IsGone();

    // 结成过南明的帝国
    public static bool Formed(Empire empire) => empire?.data != null && !empire.IsGone() && empire.data.southern_retreat_at >= 0d;

    // 诸镇割据的期限：偏安的那些年，或京师还没收回
    public static bool Active(Empire empire)
    {
        if (!Formed(empire) || EmpireWorld.Current == null) return false;
        if (SouthernRetreatSystem.Sheltered(empire)) return true;
        City capital = EmpireCoreManager.Get(empire)?.GetCoreCapital();
        return capital?.Realm == null || capital.IsGone() || capital.Realm.GetEmpire() != empire;
    }

    // 南明的藩镇：结成过南明的帝国里世袭的军府
    public static bool IsGarrison(Realm kingdom)
    {
        if (!Alive(kingdom)) return false;
        Empire empire = kingdom.GetEmpire();
        return Formed(empire) && kingdom != empire.CoreKingdom &&
               EmpireCraftKingdomBehCheckKingdomType.IsHereditaryGarrison(kingdom, kingdom.GetRegime());
    }

    // 诸镇火并：交战两方都是同一个南明的藩镇
    public static bool IsInternalWar(War war)
    {
        Realm attacker = war?.MainAttacker, defender = war?.MainDefender;
        return IsGarrison(attacker) && IsGarrison(defender) && attacker.GetEmpire() == defender.GetEmpire();
    }

    // 每月(SouthernRetreatSystem.Check)：期内一年结算一次
#if EC_PENDING
    public static void Update(Empire empire)
    {
        if (!Active(empire)) return;
        if (!ReferenceEquals(_world, EmpireWorld.Current))
        {
            _world = EmpireWorld.Current;
            Yearly.Clear();
        }
        double now = GameClock.Now;
        if (Yearly.TryGetValue(empire.Key, out double last) && now >= last && GameClock.YearsSince(last) < 1) return;
        Yearly[empire.Key] = now;
        Enfeoff(empire);
        Feuds(empire);
        Surrender(empire);
    }
#endif

    // 核心以外的成员改为世袭藩镇；直隶(京畿)、羁縻、正在叛乱的、不是律令制的不动
#if EC_PENDING
    public static void Enfeoff(Empire empire)
    {
        if (!Formed(empire)) return;
        var converted = new List<string>();
        foreach (Realm member in empire.kingdoms_list.ToList())
        {
            if (!Alive(member) || member == empire.CoreKingdom || member.IsFactionRebelling() || member.IsLocalRebelling()) continue;
            if (member.GetOrCreate().jimi_mode != 0 || member.GetKingdomType() == KingdomType.LvLing_zhili) continue;
            Regime regime = member.GetRegime();
            if (regime == null || regime.type != RegimeType.LvLing ||
                EmpireCraftKingdomBehCheckKingdomType.IsHereditaryGarrison(member, regime)) continue;
            // 先照"转军府"改成军府(自主外交、自收自支)，再改世袭：世袭以后照旧是军府，不改封国
            regime.SetAllowDiplomacy(true);
            regime.SetAllowArmy(true);
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            TreasurySystem.EnsureCollectsTax(regime);
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
            if (member.GetKingdomType() is not (KingdomType.LvLing_jiedushi or KingdomType.LvLing_duhufu))
            {
                LogService.LogWarning($"[EmpireCraft][南明诸镇] {member.GetKingdomName()} 没改成军府({member.GetKingdomType()})");
                continue;
            }
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
            converted.Add(member.GetKingdomName());
        }
        List<Realm> garrisons = empire.kingdoms_list.Where(IsGarrison).ToList();
        if (garrisons.Count > 0) KingdomColorService.ApplyGarrisonFamily(empire, garrisons);
        if (converted.Count == 0) return;
        string text = string.Format(LM.Get("southern_garrison_history"), empire.GetEmpireName(), string.Join("、", converted));
        EventRecorder.Record(empire, text, empire.Emperor, empire.CoreKingdom);
        LogService.LogInfo($"[EmpireCraft][南明诸镇] {empire.GetEmpireFullName()} 改藩镇 {converted.Count} 家：{string.Join("、", converted)}");
    }
#endif

    // 诸镇火并
#if EC_PENDING
    private static void Feuds(Empire empire)
    {
        List<Realm> garrisons = empire.kingdoms_list.Where(IsGarrison).ToList();
        int wars = 0;
        foreach (Realm attacker in garrisons.OrderByDescending(kingdom => kingdom.GetNationalPower()).ToList())
        {
            if (wars >= FeudsPerYear) break;
            if (!Alive(attacker) || !attacker.HasRuler() || attacker.Capital == null || Feuding(attacker) ||
                EcRandom.value >= FeudChance) continue;
            Realm target = garrisons.Where(other => FeudTarget(attacker, other))
                .OrderBy(other => other.GetNationalPower()).FirstOrDefault();
            if (target == null) continue;
            War war;
            Declaring = true;
            try
            {
                war = EmpireWorld.Current.StartWar(attacker, target, WarKinds.Normal);
            }
            finally
            {
                Declaring = false;
            }
            if (war?.Record == null) continue;
            WarGoalSystem.Preset(war, WarGoal.夺城);
            WarObjective.Aim(war, target.Capital);
            wars++;
            string text = string.Format(LM.Get("southern_garrison_feud_history"), attacker.GetKingdomName(), target.GetKingdomName(),
                target.Capital.GetCityName());
            EventRecorder.Record(empire, text, attacker.Ruler, attacker);
            LogService.LogInfo($"[EmpireCraft][南明诸镇] {attacker.GetKingdomName()}(国力{attacker.GetNationalPower():0}) 攻 " +
                               $"{target.GetKingdomName()}(国力{target.GetNationalPower():0})，争{target.Capital.GetCityName()}");
        }
    }
#endif

    // 已在和别的藩镇打
    private static bool Feuding(Realm kingdom) =>
        kingdom.GetWars().Any(war => war?.Record != null && !war.HasEnded() && IsInternalWar(war));

#if EC_PENDING
    private static bool FeudTarget(Realm attacker, Realm target)
    {
        if (!Alive(target) || target == attacker || target.Capital == null || target.Capital.IsGone() ||
            !attacker.IsNeighbourWith(target) || attacker.IsAtWarWith(target) || Feuding(target)) return false;
        if (attacker.Ruler != null && attacker.Ruler == target.Ruler) return false;
        if (attacker.hasAlliance() && attacker.getAlliance() == target.getAlliance() || RoyalMarriageSystem.Allied(attacker, target))
            return false;
        double ended = EmpireWorld.Current.diplomacy.getRelation(attacker, target)?.data?.timestamp_last_war_ended ?? -1d;
        if (ended >= 0d && GameClock.YearsSince(ended) < SimGlobals.m.minimum_years_between_wars) return false;
        bool hostile = (EmpireWorld.Current.diplomacy.getOpinion(attacker, target)?.total ?? 0) < 0;
        return hostile || attacker.GetNationalPower() >= target.GetNationalPower() * FeudPowerRatio;
    }
#endif

    // 举镇降敌
#if EC_PENDING
    private static void Surrender(Empire empire)
    {
        Realm former = EmpireWorld.Current.RealmByKey(empire.data.southern_ming_invader);
        if (!Alive(former)) return;
        Realm invader = FrontSystem.ModRealm(former) ?? former;
        Empire conqueror = invader.GetEmpire();
        if (conqueror == null || conqueror == empire || conqueror.IsGone() || conqueror.IsArchived() || conqueror.CoreKingdom != invader)
            return;
        float pressure = ScenarioEventSystem.ConquestPressure(invader, CultureService.GetRealmCulture(empire.CoreKingdom));
        double power = WarSituation.Power(invader);
        foreach (Realm garrison in empire.kingdoms_list.Where(IsGarrison).OrderBy(_ => EcRandom.value).ToList())
        {
            if (garrison.GetOrCreate().submitted_empire_id == empire.Key || !FrontSystem.Hostile(garrison, invader) ||
                !Borders(garrison, invader) || garrison.GetNationalPower() >= power * SurrenderPowerShare) continue;
            if (EcRandom.value >= SurrenderChance * pressure) continue;
            Defect(empire, garrison, invader, conqueror, pressure);
            return;
        }
    }
#endif

    // 与入侵者(连同其帝国成员)的城相邻
    private static bool Borders(Realm kingdom, Realm invader) =>
        kingdom.Cities?.Any(city => city?.NeighbourCities != null && city.NeighbourCities.Any(other =>
            other?.Realm != null && !other.IsGone() && FrontSystem.ModRealm(other.Realm) == invader)) == true;

#if EC_PENDING
    private static void Defect(Empire empire, Realm garrison, Realm invader, Empire conqueror, float pressure)
    {
        // 退出与入侵者一方的仗(只退自己，南明与入侵者的大仗照打；DiplomacyActions.EndWars 会把整场仗收掉)
        foreach (War war in garrison.GetWars().ToList())
        {
            if (war?.Record == null || war.HasEnded()) continue;
            if (conqueror.kingdoms_list.Any(member => Alive(member) && war.isInWarWith(garrison, member))) war.Leave(garrison);
        }
        conqueror.join(garrison, true, pForce: true);
        if (garrison.GetEmpire() != conqueror) return;
        // 降敌记在镇将身上(量刑天平的叛逆砝码)
        EmpireCraft.Core.EmpireLawSystem.NoteSecession(garrison);
        // 归了入侵者，随那边的颜色
        garrison._cached_color = null;
        garrison.updateColor(conqueror.GetColor());
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(garrison);
        EmpireWorld.Current.Zones?.MarkDirty();
        string text = string.Format(LM.Get("southern_garrison_surrender_history"), garrison.GetKingdomName(), invader.GetKingdomName(),
            empire.GetEmpireName());
        EventRecorder.Record(empire, text, garrison.Ruler, garrison);
        TranslateHelper.LogEventMessage(text, garrison);
        LogService.LogInfo($"[EmpireCraft][南明诸镇] 压力 {pressure:P0}，{garrison.GetKingdomName()} 举镇降 {conqueror.GetEmpireName()}");
    }
#endif
}
}
