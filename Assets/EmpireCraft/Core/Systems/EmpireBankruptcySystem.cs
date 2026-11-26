// Generated from the EmpireCraft mod: Scripts/GeneralSystems/EmpireBankruptcySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 国库亏空(帝国核心国库 <0)：朝廷发不出军饷。
//   · 军饷断绝：除京师(帝国核心王国的首都)外，各城兵额为 0，地方驻军就地解散(见 CityPatch 的兵额补丁)；
//   · 地方离心：每年亏空越久，成员国越容易离开——
//       军府(节度使)与自主性强的(可自募军队、自办外交、不供养中央军)拥兵自立，打独立战争；
//       自主性弱的就近投靠正在与朝廷交战的叛军，举地归附；附近没有叛军的才偶尔自立；
//   · 前现代正统按当前亏空状况有界扣分；现代/过渡政体保留原有年度事件法统扣分。
// 国库转正后亏空结束。每年由 ConstitutionalEconomySystem 的年度结算调用。
public static class EmpireBankruptcySystem
{
    private const float BaseChance = 0.05f;
    private const float ChancePerYear = 0.04f;
    private const float MaxChance = 0.75f;
    private const float JiedushiFactor = 2f;
    // 自主性强的成员(多是封建封地)离去 1.5 → 0.75 倍(用户 2026-10-21 选"破产离散降频")
    private const float AutonomousFactor = 0.75f;
    private const float StrandedFactor = 0.25f;
    private const float MaxLossShare = 0.5f;
    private const float NearbyRebelDistance = 60f;

    // 国库亏空；新财政下国库不记成负数，京畿四分之一以上的城欠着治理、驻军、军费也算朝廷发不出饷
    public static bool IsBankrupt(Empire empire) =>
        empire?.CoreKingdom != null && !empire.CoreKingdom.IsGone() &&
        (empire.CurrentMoney < 0 || TreasurySystem.Enabled(empire.CoreKingdom) && InArrears(empire.CoreKingdom));

    private static bool InArrears(Realm core)
    {
        if (core.Cities == null || core.Cities.Count == 0) return false;
        int owing = 0;
        foreach (City city in core.Cities)
        {
            CityStabilityData state = city?.Record == null ? null : city.GetOrCreate().stability;
            if (state != null && state.owner_id == core.Key && state.arrears > 0) owing++;
        }
        return owing * 4 >= core.Cities.Count;
    }

    // 亏空期间只有京师养得起兵
    public static bool IsUnpaidGarrison(City city)
    {
        try
        {
            Realm kingdom = city?.Realm;
            if (kingdom == null || kingdom.IsGone()) return false;
            if (TreasurySystem.Enabled(city)) return city != kingdom.GetEmpire()?.CoreKingdom?.Capital &&
                city.GetOrCreate().stability?.withdrawn == true;
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.IsGone() || !IsBankrupt(empire)) return false;
            return city != empire.CoreKingdom.Capital;
        }
        catch
        {
            return false;
        }
    }

#if EC_PENDING
    public static void Update(Empire empire, ConstitutionalEconomyState state)
    {
        if (state == null || empire?.CoreKingdom == null || EmpireWorld.Current == null) return;
        MonarchyLegitimacy.Invalidate(empire);
        if (!IsBankrupt(empire))
        {
            if (state.bankrupt_since >= 0d)
                EventRecorder.Record(empire, string.Format(LM.Get("bankruptcy_end_history"), empire.GetEmpireFullName()));
            state.bankrupt_since = -1d;
            return;
        }
        if (state.bankrupt_since < 0d)
        {
            state.bankrupt_since = GameClock.Now;
            EventRecorder.Record(empire, string.Format(LM.Get("bankruptcy_start_history"), empire.GetEmpireFullName()));
            return;
        }
        int years = GameClock.YearsSince(state.bankrupt_since);
        // 前现代亏空是有界的现状贡献，财政恢复即解除，不再永久累扣原始法统。
        if (!MonarchyLegitimacy.Applies(empire)) empire.AddMandate(-Mathf.Min(5, 1 + years / 5));
        try
        {
            Disintegrate(empire, years);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 国库亏空离心失败: {exception.Message}");
        }
    }

    private static void Disintegrate(Empire empire, int years)
    {
        Realm core = empire.CoreKingdom;
        // 新财政逐城评估欠款、稳定恢复和撤军，普通州郡不再随机拆分；
        // 但拥兵的节度使、藩镇照样趁朝廷发不出饷拥兵自立(以前新财政下整段跳过，节度使从来不反)
        bool cityLevel = TreasurySystem.Enabled(core);
        // 开国气象(统一战争期间、统一后三十年)财政再难也不解体(见 UnificationSystem)
        if (UnificationSystem.InGrace(core)) return;
        // 不许自立(见 SecessionGate)：亏空不离心(先离开后开战，Empire.leave 那道门拦不到)
        if (SecessionGate.Forbids("亏空自立", empire.GetEmpireName())) return;
        float chance = Mathf.Min(MaxChance, BaseChance + ChancePerYear * years);
        List<Realm> rebels = RebellionSnowballSystem.FindRebels(empire, core).ToList();
        // 新入帝国十年内、神力拉入未满期的不因亏空离去(见 MembershipCooldown)
        List<Realm> members = empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.IsGone() &&
                kingdom != core && !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling() &&
                !kingdom.GetWars().Any() && MembershipCooldown.Settled(kingdom))
            .OrderBy(_ => EcRandom.value).ToList();
        int budget = Math.Max(1, Mathf.CeilToInt(members.Count * MaxLossShare));
        int lost = 0;
        foreach (Realm member in members)
        {
            if (lost >= budget || empire.IsGone() || core.IsGone()) break;
            bool jiedushi = member.GetKingdomType() == KingdomType.LvLing_jiedushi ||
                            member.GetKingdomType() == KingdomType.LvLing_duhufu;
            bool autonomous = member.HasRuler() && (jiedushi || IsAutonomous(member));
            if (cityLevel && (!autonomous || !RebellionSystem.CanAttempt(member))) continue;
            Realm rebel = autonomous ? null : FindNearbyRebel(member, rebels);
            float factor = jiedushi ? JiedushiFactor
                : autonomous ? AutonomousFactor
                : rebel != null ? 1f
                : member.HasRuler() ? StrandedFactor : 0f;
            if (EcRandom.value >= chance * factor) continue;
            if (rebel != null ? Defect(empire, member, rebel) : DeclareIndependence(empire, core, member)) lost++;
        }
    }
#endif

    // 自主性强：可自募军队、自办外交、不供养中央军
    private static bool IsAutonomous(Realm kingdom)
    {
        Regime regime = kingdom.GetRegime();
        return regime != null && regime.IsAllowArmy() && regime.IsAllowDiplomacy() && !regime.IsAllowSupportCenterArmy();
    }

    // 与本国接壤的叛军优先，其次是首都相距不远的
#if EC_PENDING
    private static Realm FindNearbyRebel(Realm member, List<Realm> rebels)
    {
        if (rebels.Count == 0 || member.Cities == null) return null;
        Realm bordering = rebels.FirstOrDefault(rebel => !rebel.IsGone() && member.Cities.Any(city =>
            city?.NeighbourCities != null && city.NeighbourCities.Any(other => other?.Realm == rebel)));
        if (bordering != null) return bordering;
        Cell home = member.Capital?.SeatCell(false);
        if (home == null) return null;
        return rebels.Where(rebel => !rebel.IsGone() && rebel.Capital?.SeatCell(false) != null)
            .Select(rebel => (rebel, distance: Land.Dist(home, rebel.capital.getTile(false))))
            .Where(pair => pair.distance <= NearbyRebelDistance)
            .OrderBy(pair => pair.distance).Select(pair => pair.rebel).FirstOrDefault();
    }

    private static bool Defect(Empire empire, Realm member, Realm rebel)
    {
        string name = member.GetKingdomFullName();
        Person defector = member.Ruler;
        City capital = member.Capital;
        int moved = 0;
        foreach (City city in member.Cities.ToList().OrderBy(city => city == capital ? 1 : 0))
        {
            if (city == null || city.IsGone()) continue;
            city.JoinRealm(rebel, pCaptured: true, rebellion: true);
            if (city.Realm == rebel) moved++;
        }
        if (moved == 0) return false;
        // 举城投贼记在原来那一国之主身上(量刑天平的叛逆砝码)
        if (defector?.Record != null && !defector.IsGone()) EmpireCraft.Core.EmpireLawSystem.NoteDefiance(defector, true);
        EventRecorder.Record(empire, actor: rebel.Ruler, logKingdom: rebel, text: string.Format(
            LM.Get("bankruptcy_defect_history"), empire.GetEmpireFullName(), name, rebel.GetKingdomFullName()));
        return true;
    }
#endif

    private static bool DeclareIndependence(Empire empire, Realm core, Realm member)
    {
        if (!member.HasRuler()) return false;
        empire.leave(member);
        War war = EmpireWorld.Current.StartWar(member, core, WarKinds.Normal);
        war?.SetEmpireWarType(EmpireWarType.地方独立);
        EventRecorder.Record(empire, actor: member.Ruler, logKingdom: member, text: string.Format(
            LM.Get("bankruptcy_independence_history"), empire.GetEmpireFullName(), member.GetKingdomFullName()));
        return true;
    }
}
}
