// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ReignRecordSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 记录每位皇帝的在位生平(写进 EmpireCraftHistory)，供谥法定庙号谥号(PosthumousNameGenerator)：
//   疆域：即位、极盛、身后的城市数；正统：即位与身后；战绩：对外胜负、平叛与叛乱得手；
//   结局：身后是否在世(失位)、享年、是否中兴之主、是否末代之君(改朝换代时回填)。
public static class ReignRecordSystem
{
#if EC_PENDING
    public static void OnReignStart(Empire empire)
    {
        EmpireCraftHistory reign = empire?.data?.currentHistory;
        if (reign == null) return;
        int cities = empire.countCities();
        reign.start_cities = cities;
        reign.max_cities = cities;
        reign.start_mandate = empire.Mandate;
    }
#endif

    public static void OnYear(Empire empire)
    {
        EmpireCraftHistory reign = empire?.data?.currentHistory;
        if (reign == null || reign.start_cities < 0) return;
        reign.max_cities = Math.Max(reign.max_cities, empire.countCities());
        if (empire.data.is_been_controlled) reign.was_controlled = true;
    }

#if EC_PENDING
    public static void OnReignEnd(Empire empire, Person emperor)
    {
        EmpireCraftHistory reign = empire?.data?.currentHistory;
        if (reign == null || emperor?.Record == null) return;
        if (reign.start_cities >= 0) reign.max_cities = Math.Max(reign.max_cities, empire.countCities());
        reign.end_cities = empire.countCities();
        reign.end_mandate = empire.Mandate;
        reign.ended_alive = emperor.Alive;
        reign.age_at_end = emperor.Age();
        reign.restorer = emperor.HasTrait(RulerTraitSystem.Restorer) || emperor.HasTrait(RulerTraitSystem.GreatRestorer);
        reign.ruler_grade = RulerQualitySystem.Of(emperor).ToString();
        if (empire.data.is_been_controlled) reign.was_controlled = true;
    }
#endif

    // 旧档的在位年数校正(读档后头一回帝国月度结算，见 Empire.RepairUnregisteredEmperor；每个存档只做一次)。
    // 用户 2026-10-21 截图：游牧汗国一位先汗"在位 136 年"——继承人本是别国(兀鲁思、封国)君主时，帝国中央直接改成他的国，
    // 不走即位登记，即位时间还停在上一位(或更早)，他身后记下的在位年数把前几位都算了进去。
    //   · 已故各位(共和国元首除外，他们任满没有末条记事)：上一位身后才轮到他，在位年数不会超过"上一位最后一条记事
    //     到他最后一条记事"的年数(他的最后一条是驾崩、退位那条)，超出两年以上的按这个改；
    //   · 当今：即位时间比上一位最后一条记事还早一年以上的，改到上一位最后一条记事那天(年号纪年随之从那天算起)
    private const double SecondsPerYear = 60d;

    // 旧核心直接清空死亡君主的王位，史册可能丢页。用同一帝国的登基经历补页，不按姓氏推测继承。
    internal static void RecoverMissingAccessions()
    {
        const string accessionKey = "personal_history_became_emperor";
        bool Accession(PersonalHistoryRecord record)
        {
            if (record == null || record.empire_id <= 0 || record.timestamp < 0d) return false;
            if (record.event_key == accessionKey) return true;
            return string.IsNullOrEmpty(record.event_key) && !string.IsNullOrEmpty(record.kingdom_name) &&
                   (record.content == $"登基为{record.kingdom_name}皇帝。" ||
                    record.content == $"登基為{record.kingdom_name}皇帝。" ||
                    record.content == $"Ascended as emperor of {record.kingdom_name}.");
        }

        var byEmpire = SpecificClanManager._globalPersonLookup.Values
            .Where(person => person?.actor_id > 0 && person.personal_history != null)
            .SelectMany(person => person.personal_history.Where(Accession).Select(record => (person, record)))
            .ToLookup(entry => entry.record.empire_id);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire?.data == null || empire.IsArchived()) continue;
            List<EmpireCraftHistory> history = empire.data.history ??= new List<EmpireCraftHistory>();
            var known = new HashSet<long>(history.Where(reign => reign != null).Select(reign => reign.id));
            if (empire.data.currentHistory is { } current) known.Add(current.id);
            if (empire.Emperor?.Record != null) known.Add(empire.Emperor.Key);
            var records = byEmpire[empire.Key].OrderBy(entry => entry.record.timestamp).ToArray();
            if (records.All(entry => known.Contains(entry.person.actor_id))) continue;
            var timelines = records.ToLookup(entry => entry.person.actor_id, entry => entry.record.timestamp);
            var recovered = new List<EmpireCraftHistory>();
            for (int i = 0; i < records.Length; i++)
            {
                (PersonalClanIdentity person, PersonalHistoryRecord accession) = records[i];
                if (known.Contains(person.actor_id)) continue;
                double next = i + 1 < records.Length ? records[i + 1].record.timestamp : empire.data.newEmperor_timestamp;
                double end = next > accession.timestamp ? next : double.PositiveInfinity;
                PersonalHistoryRecord death = person.personal_history.FirstOrDefault(record =>
                    record?.event_key == "death" && record.timestamp >= accession.timestamp && record.timestamp <= end);
                if (death != null) end = death.timestamp;
                if (double.IsPositiveInfinity(end)) continue;

                var reign = new EmpireCraftHistory
                {
                    id = person.actor_id, emperor = person.name, empire_name = accession.kingdom_name,
                    dynasty_name = accession.kingdom_name, royal_surname = person.surname,
                    total_time = (int)((end - accession.timestamp) / SecondsPerYear)
                };
                reign.descriptions.Add(new HistoryDescription
                {
                    actor_id = person.actor_id, timestamp = accession.timestamp, time = GameClock.DateText(accession.timestamp),
                    description = accession.content
                });
                if (death != null)
                    reign.descriptions.Add(new HistoryDescription
                    {
                        actor_id = person.actor_id, timestamp = death.timestamp, time = GameClock.DateText(death.timestamp),
                        description = death.content
                    });
                recovered.Add(reign);
                known.Add(person.actor_id);
            }
            if (recovered.Count == 0) continue;
            var ordered = history.Concat(recovered).OrderBy(reign =>
                reign == null ? -1d : timelines[reign.id].DefaultIfEmpty(
                    reign.descriptions?.FirstOrDefault(entry => entry?.timestamp >= 0d)?.timestamp ?? -1d).First()).ToList();
            history.Clear();
            history.AddRange(ordered);
        }
    }

#if EC_PENDING
    public static void RepairLegacyTenure(Empire empire)
    {
        List<EmpireCraftHistory> history = empire?.data?.history;
        if (history == null || history.Count == 0 || EmpireWorld.Current == null) return;
        try
        {
            int repaired = 0;
            var samples = new List<string>();
            double previousEnd = -1d;
            foreach (EmpireCraftHistory reign in history)
            {
                if (reign == null) continue;
                double end = LastRecord(reign);
                if (!reign.is_republic && previousEnd > 0d && end > previousEnd)
                {
                    int bound = (int)Math.Ceiling((end - previousEnd) / SecondsPerYear);
                    if (reign.total_time > bound + 2)
                    {
                        if (samples.Count < 4) samples.Add($"{reign.emperor} {reign.total_time}→{Math.Max(1, bound)}");
                        reign.total_time = Math.Max(1, bound);
                        repaired++;
                    }
                }
                if (end > 0d) previousEnd = end;
            }
            double now = GameClock.Now;
            string current = "";
            if (previousEnd > 0d && previousEnd <= now && empire.data.newEmperor_timestamp >= 0d &&
                empire.data.newEmperor_timestamp + SecondsPerYear < previousEnd)
            {
                current = $"；当今即位时间从 {GameClock.YearsSince(empire.data.newEmperor_timestamp)} 年前改到 {GameClock.YearsSince(previousEnd)} 年前";
                empire.data.newEmperor_timestamp = previousEnd;
            }
            if (repaired > 0 || current.Length > 0)
                LogService.LogInfo($"[EmpireCraft][寿数] 旧档在位年数校正 {empire.GetEmpireName()}：" +
                                                        $"先君 {repaired} 位({string.Join("、", samples)}){current}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][寿数] 旧档在位年数校正失败: {exception.Message}");
        }
    }
#endif

    // 在位起算：登记过的按即位时间；没登记的(中央改成继承人的国，还没等到月度补登记就走了)按上一位最后一条记事
    public static double ReignStart(Empire empire, Person emperor)
    {
        double since = empire.data.newEmperor_timestamp;
        if (empire.data.reign_emperor_id < 0 || emperor?.Record == null || empire.data.reign_emperor_id == emperor.Key)
            return since;
        EmpireCraftHistory previous = empire.data.history?.LastOrDefault(reign => reign != null);
        double end = previous == null ? -1d : LastRecord(previous);
        return end > since && end <= GameClock.Now ? end : since;
    }

    private static double LastRecord(EmpireCraftHistory reign)
    {
        double last = -1d;
        if (reign.descriptions == null) return last;
        foreach (HistoryDescription description in reign.descriptions)
            if (description != null && description.timestamp > last) last = description.timestamp;
        return last;
    }

    // inheritCourt：朝廷里的易代(权臣篡位、外戚代立等)，积弊原样留下、王朝年岁接着算(三档之"保留"，见 DynasticCycleSystem.OnDynastyChanged)
#if EC_PENDING
    public static void OnDynastyChanged(Empire empire, Person newEmperor, bool inheritCourt = false)
    {
        DynasticCycleSystem.OnDynastyChanged(empire, inheritCourt);
        EmpireCraftHistory last = empire?.data?.history?.LastOrDefault(reign => reign != null && !reign.is_republic);
        if (last != null && last.id != newEmperor?.Key) last.ended_dynasty = true;
    }
#endif

    public static void OnWarEnded(War war, WarOutcome winner)
    {
        if (war == null) return;
        bool rebellion = war.GetKind() == WarKinds.Rebellion;
        var seen = new HashSet<long>();
        foreach (Realm kingdom in war.Attackers.ToList()) Tally(kingdom, true, winner, rebellion, seen);
        foreach (Realm kingdom in war.Defenders.ToList()) Tally(kingdom, false, winner, rebellion, seen);
    }

    private static void Tally(Realm kingdom, bool attacker, WarOutcome winner, bool rebellion, HashSet<long> seen)
    {
        if (kingdom == null || kingdom.IsGone() || !kingdom.IsEmpire()) return;
        Empire empire = kingdom.GetEmpire();
        EmpireCraftHistory reign = empire?.data?.currentHistory;
        if (reign == null || reign.is_republic || !seen.Add(empire.Key)) return;
        bool won = winner == (attacker ? WarOutcome.Attackers : WarOutcome.Defenders);
        bool lost = winner == (attacker ? WarOutcome.Defenders : WarOutcome.Attackers);
        if (rebellion && !attacker)
        {
            reign.rebellions++;
            if (lost) reign.rebellions_lost++;
            return;
        }
        if (won) reign.wars_won++;
        else if (lost) reign.wars_lost++;
    }
}
}
