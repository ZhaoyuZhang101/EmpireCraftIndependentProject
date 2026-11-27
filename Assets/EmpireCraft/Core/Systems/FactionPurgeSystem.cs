// Generated from the EmpireCraft mod: Scripts/GeneralSystems/FactionPurgeSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 清君侧(派系平衡的一环，见 FactionBalanceSystem)：一派独大、把持朝政时，被排挤的派系以"清君侧"为名起兵，讨伐独大派系的首领。
//   起兵条件(每年)：失衡度 ≥ 60，独大的是朝中文臣、外戚、教士或其他把持朝政的派系(武人跋扈另走禁军政变，见 PalaceArmySystem)；
//     被排挤的派系怨望 ≥ 60，且有本派的人主政某省(省的长官属于该派)；帝国眼下没有别的清君侧之战。
//     机会 = 10% + (失衡度 - 60) / 200 + (怨望 - 60) / 200，明君在位减半。
//   起兵：本派主政的各省一同起兵(改称本派名号、就地武装义军，见 RebellionStartupService)，向中央开战，战争名"某派清君侧，讨某某"；
//     中央照叛乱那样要先集结(见 MusterSystem)。
//   胜负(每月看)：起兵一方拿下京城 → 清君侧功成；起兵各省的都城都丢了 → 兵败；打满五年未决，按双方兵力判。
//   功成：独大派系的首领伏诛，该派让出四成占比给起兵的派系；独大派系的人若是权臣，权臣罢黜、天子不再被挟制；
//     失衡度只留三成，起兵派系怨望平复，失势的一派怨望大涨。
//   兵败：起兵派系怨望压到三成，独大派系更加专横(失衡度 +10)。
public static class FactionPurgeSystem
{
    private const float PurgeImbalance = 60f, PurgeGrievance = 60f;
    private const int RatioTransfer = 40;
    private const int StalemateYears = 5;

    // 每年(FactionBalanceSystem.Yearly 末尾)
    public static void TryRaise(Empire empire, List<FixedFaction> factions, FixedFaction top)
    {
        if (FactionBalanceSystem.Imbalance(empire) < PurgeImbalance || factions.Count < 2) return;
        FactionBalanceSystem.Direction direction = FactionBalanceSystem.DirectionOf(top);
        if (direction is FactionBalanceSystem.Direction.Military or FactionBalanceSystem.Direction.None) return;
        Realm core = empire.CoreKingdom;
        if (core == null || core.Capital == null || Active(empire) != null) return;
        FixedFaction rebels = factions.Where(faction => faction != top)
            .OrderByDescending(faction => CourtSystem.FactionState(empire, faction).grievance).First();
        float grievance = CourtSystem.FactionState(empire, rebels).grievance;
        if (grievance < PurgeGrievance) return;
        List<Realm> provinces = empire.kingdoms_list.Where(kingdom => kingdom?.Record != null && !kingdom.IsGone() &&
                kingdom != core && kingdom.Capital != null && kingdom.Ruler?.GetFaction() == rebels &&
                !kingdom.IsFactionRebelling() && !kingdom.IsLocalRebelling() && !kingdom.GetWars().Any() &&
                RebellionSystem.CanAttempt(kingdom))
            .OrderByDescending(kingdom => kingdom.Warriors()).ToList();
        if (provinces.Count == 0) return;
        float chance = 0.1f + (FactionBalanceSystem.Imbalance(empire) - PurgeImbalance) / 200f + (grievance - PurgeGrievance) / 200f;
        if (RulerQualitySystem.Of(empire.Emperor) == RulerQualitySystem.Grade.Wise) chance *= 0.5f;
        if (EcRandom.value >= chance) return;
        Raise(empire, rebels, top, provinces, grievance);
    }

    private static void Raise(Empire empire, FixedFaction rebels, FixedFaction top, List<Realm> provinces, float grievance)
    {
        Realm core = empire.CoreKingdom;
        Realm leader = provinces[0];
        RebellionCauseData cause = RebellionSystem.Capture(leader, leader.Capital, "rebellion_reason_purge", top.Name);
        leader.StartFactionRebelling(rebels);
        War war = EmpireWorld.Current.StartWar(leader, core, WarKinds.Normal);
        if (war == null)
        {
            leader.EndFactionRebelling();
            return;
        }
        war.SetEmpireWarType(EmpireWarType.清君侧, belongingFaction: rebels);
        Person villain = top.GetLeader();
        war.Name = string.Format(LM.Get("purge_war_name"), rebels.Name, villain?.FullName() ?? top.Name);
        var data = war.GetOrCreate();
        data.purge_faction_id = rebels.GetID();
        data.purge_target_faction_id = top.GetID();
        data.purge_capital_city_id = core.Capital.Key;
        foreach (Realm province in provinces.Skip(1))
        {
            province.StartFactionRebelling(rebels);
            war.JoinAttackers(province);
        }
        // 郡县多不许养兵，起兵各省就地武装义军
        foreach (Realm province in provinces) RebellionStartupService.RaiseUprisingMilitia(province, grievance / 100f);
        RebellionSystem.Record(leader, war, cause);
        string text = string.Format(LM.Get("history_purge_raised"), rebels.Name, villain?.FullName() ?? top.Name,
            string.Join("、", provinces.Select(province => province.GetKingdomName())));
        empire.RecordHistory(directContent: text, actorId: leader.Ruler?.Key ?? -1L, kingdomId: core.Key);
        TranslateHelper.LogEventMessage(text, core);
    }

    // 帝国眼下的清君侧之战(本系统发起的)
    public static War Active(Empire empire) =>
        empire?.CoreKingdom?.GetWars().FirstOrDefault(war => war != null && !war.HasEnded() && IsPurge(war));

    public static bool IsPurge(War war) =>
        war?.Record != null && war.GetEmpireWarType() == EmpireWarType.清君侧 &&
        !string.IsNullOrEmpty(war.GetOrCreate().purge_target_faction_id);

    // 每月(FrontSystem.MonthTick)：判胜负
    public static void MonthTick()
    {
        foreach (War war in EmpireWorld.Current.LiveWars.ToList())
        {
            if (!IsPurge(war) || !war.Alive || war.HasEnded()) continue;
            try
            {
                WarOutcome? winner = Decide(war);
                if (winner != null) EmpireWorld.Current.EndWar(war, winner.Value);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][清君侧] 判定失败: {exception.Message}");
            }
        }
    }

    private static WarOutcome? Decide(War war)
    {
        var data = war.GetOrCreate();
        City capital = EmpireWorld.Current.CityByKey(data.purge_capital_city_id);
        List<Realm> attackers = war.Attackers.Where(kingdom => kingdom?.Record != null && !kingdom.IsGone()).ToList();
        if (capital?.Realm != null && attackers.Contains(capital.Realm)) return WarOutcome.Attackers;
        if (attackers.Count == 0 || attackers.All(kingdom => kingdom.Capital == null || !kingdom.HasCities())) return WarOutcome.Defenders;
        if (GameClock.YearsSince(war.StartedStamp) < StalemateYears) return null;
        float attack = attackers.Sum(Strength);
        float defend = war.Defenders.Where(kingdom => kingdom?.Record != null && !kingdom.IsGone()).Sum(Strength);
        return attack > defend ? WarOutcome.Attackers : WarOutcome.Defenders;
    }

    private static float Strength(Realm kingdom) =>
        CorpsSystem.Enabled ? CorpsSystem.Of(kingdom).Sum(corps => corps.strength) : kingdom.Warriors();

    // 战争结束(WarPatch)：结算清君侧
    public static void Resolve(War war, WarOutcome winner)
    {
        if (!IsPurge(war)) return;
        var data = war.GetOrCreate();
        Realm core = war.MainDefender;
        Empire empire = core?.GetEmpire();
        foreach (Realm kingdom in war.Attackers.ToList())
            if (kingdom?.Record != null && !kingdom.IsGone()) kingdom.EndFactionRebelling();
        if (empire == null || empire.IsGone()) return;
        FixedFaction rebels = CourtSystem.FindFaction(empire, data.purge_faction_id);
        FixedFaction target = CourtSystem.FindFaction(empire, data.purge_target_faction_id);
        if (rebels == null || target == null) return;
        string text;
        if (winner == WarOutcome.Attackers)
        {
            Person villain = target.GetLeader();
            string villainName = villain?.FullName() ?? target.Name;
            Person minister = empire.GetPowerfulMinister();
            if (minister?.Record != null && minister.GetFaction() == target)
            {
                empire.ResetPowerfulMinister();
                empire.data.is_been_controlled = false;
            }
            if (villain?.Record != null && villain.Alive && villain != empire.Emperor)
            {
                Person avenger = rebels.GetLeader();
                BiographySystem.Note(avenger, BiographySystem.Format("bio_purge_leader", villainName), villain);
                BiographySystem.Kill(villain, BiographySystem.Format("death_cause_purge", rebels.Name), avenger, executed: true);
            }
            // 宠臣一并被诛(见 FavouriteSystem)
            FavouriteSystem.OnPurge(empire, rebels.GetLeader());
            core.TryIncreaseFactionRatio(rebels, RatioTransfer);
            empire.data.faction_imbalance *= 0.3f;
            empire.data.faction_imbalance_warned = false;
            CourtSystem.FactionState(empire, rebels).grievance = 20f;
            CourtSystem.FactionState(empire, target).grievance =
                Mathf.Min(100f, CourtSystem.FactionState(empire, target).grievance + 40f);
            text = string.Format(LM.Get("history_purge_success"), rebels.Name, villainName, target.Name);
        }
        else
        {
            CourtSystem.FactionState(empire, rebels).grievance = 30f;
            empire.data.faction_imbalance = Mathf.Min(100f, empire.data.faction_imbalance + 10f);
            text = string.Format(LM.Get("history_purge_failed"), rebels.Name, target.Name);
        }
        empire.RecordHistory(directContent: text, kingdomId: core.Key);
        TranslateHelper.LogEventMessage(text, core);
    }
}
}
