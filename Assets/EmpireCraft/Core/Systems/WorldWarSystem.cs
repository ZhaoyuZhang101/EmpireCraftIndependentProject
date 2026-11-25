// Ported from the EmpireCraft mod: Scripts/GeneralSystems/WorldWarSystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// Promotes an existing war into a world war when industrial or modern powers
// form genuine multi-state coalitions on both sides. It does not manufacture a
// war; alliances and diplomatic escalation remain responsible for participation.
public static class WorldWarSystem
{
    private sealed class PoliticalBloc
    {
        public string Key;
        public Realm Representative;
        public Empire Empire;
        public double Power;
        public bool Modern;
    }

#if EC_PENDING
    public static void TryEscalate(War war, bool force = false)
    {
        if (war == null || war.data == null || war.hasEnded() || EmpireWorld.Current == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        if (data.world_war) return;
        if (!force && data.world_war_last_check >= 0d &&
            GameClock.MonthsSince(data.world_war_last_check) < 1) return;
        data.world_war_last_check = GameClock.Now;

        List<PoliticalBloc> attackers = BuildBlocs(war._list_attackers);
        List<PoliticalBloc> defenders = BuildBlocs(war._list_defenders);
        if (attackers.Count < 2 || defenders.Count < 2) return;
        List<PoliticalBloc> participants = attackers.Concat(defenders)
            .GroupBy(bloc => bloc.Key).Select(group => group.First()).ToList();
        if (participants.Count < 4 || participants.Count(bloc => bloc.Modern) < 3) return;

        List<PoliticalBloc> world = BuildBlocs(EmpireWorld.Current.LiveRealms);
        double worldPower = world.Sum(bloc => Math.Max(1d, bloc.Power));
        double participantPower = participants.Sum(bloc => Math.Max(1d, bloc.Power));
        float powerShare = worldPower <= 0d ? 0f : (float)(participantPower / worldPower);
        if (powerShare < 0.45f) return;

        data.world_war = true;
        data.world_war_bloc_count = participants.Count;
        data.world_war_power_share = powerShare;
        string attackerName = GetPolityName(war.getMainAttacker());
        string defenderName = GetPolityName(war.getMainDefender());
        war.data.name = string.Format(LM.Get("world_war_name"), attackerName, defenderName);
        string text = string.Format(LM.Get("world_war_started_history"), war.data.name,
            participants.Count, Mathf.RoundToInt(powerShare * 100f));
        RecordForParticipants(participants, text);
        TranslateHelper.LogEventMessage(text, war.getMainAttacker());
    }

    public static void Resolve(War war, WarWinner winner)
    {
        if (war == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        if (!data.world_war || data.world_war_end_recorded) return;
        data.world_war_end_recorded = true;
        List<PoliticalBloc> participants = BuildBlocs(war._list_attackers.Concat(war._list_defenders));
        Realm winnerKingdom = winner == WarWinner.Attackers ? war.getMainAttacker() :
            winner == WarWinner.Defenders ? war.getMainDefender() : null;
        string text = winnerKingdom == null
            ? string.Format(LM.Get("world_war_peace_history"), war.data.name)
            : string.Format(LM.Get("world_war_victory_history"), GetPolityName(winnerKingdom), war.data.name);
        RecordForParticipants(participants, text);
        TranslateHelper.LogEventMessage(text, winnerKingdom ?? war.getMainAttacker());
        // 战后秩序：战败国正统大跌、革命形势陡增，战胜国发起万国公会(见 WorldOrderSystem)
        WorldOrderSystem.OnWorldWarEnded(war, winner);
    }

    private static List<PoliticalBloc> BuildBlocs(IEnumerable<Realm> kingdoms)
    {
        if (kingdoms == null) return new List<PoliticalBloc>();
        var blocs = new Dictionary<string, PoliticalBloc>(StringComparer.Ordinal);
        foreach (Realm kingdom in kingdoms)
        {
            if (kingdom == null || kingdom.IsGone()) continue;
            Empire empire = kingdom.GetEmpire();
            string key = empire == null ? $"kingdom:{kingdom.Key}" : $"empire:{empire.Key}";
            if (blocs.ContainsKey(key)) continue;
            Realm representative = empire?.CoreKingdom ?? kingdom;
            string culture = empire == null
                ? CultureService.GetRealmCulture(kingdom)
                : InstitutionSystem.GetCustomsCulture(empire);
            blocs[key] = new PoliticalBloc
            {
                Key = key,
                Representative = representative,
                Empire = empire,
                Power = empire?.GetNationalPower() ?? kingdom.GetNationalPower(),
                Modern = representative?.GetRegime()?.type == RegimeType.Modern ||
                         !string.IsNullOrWhiteSpace(culture) && TechnologySystem.HasTech(culture, "industrialization")
            };
        }
        return blocs.Values.ToList();
    }

    private static string GetPolityName(Realm kingdom)
    {
        if (kingdom == null) return LM.Get("label_none");
        string name = kingdom.GetEmpire()?.GetEmpireFullName();
        if (!string.IsNullOrWhiteSpace(name)) return name;
        name = kingdom.GetKingdomFullName();
        return string.IsNullOrWhiteSpace(name) ? kingdom.Name : name;
    }

    private static void RecordForParticipants(IEnumerable<PoliticalBloc> participants, string text)
    {
        var recorded = new HashSet<long>();
        foreach (PoliticalBloc bloc in participants ?? Enumerable.Empty<PoliticalBloc>())
        {
            Empire empire = bloc.Empire;
            if (empire == null || empire.IsArchived() || empire.IsGone() || !recorded.Add(empire.Key)) continue;
            empire.RecordHistory(directContent: text, kingdomId: bloc.Representative?.Key ?? -1L);
        }
    }
#endif
}
}
