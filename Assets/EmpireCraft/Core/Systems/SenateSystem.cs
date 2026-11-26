// Generated from the EmpireCraft mod: Scripts/GeneralSystems/SenateSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 古典共和的元老院(施行「元老院」制度后)。元老名单记在帝国的内阁名单里，和封建选帝侯团同一套：
// 执政官出缺时由 OfficeSelector.RankElectiveCandidates 把元老当选举人，按派系计票。
//   · 席位：每个成员城邦按人口分 1~4 席(不到 1 万 1 席，1 万起 2 席，10 万起 3 席，100 万以上 4 席)；
//   · 人选：城邦之主固定居首席，其余席位先留任上届仍属本城邦的元老，再按贵族优先、威望高低补足；
//   · 没有元老院的古典共和不设元老，名单清空(首领按兵力最强者接任)。
// 由帝国的内阁检查行为定期调用。
public static class SenateSystem
{
    public static int SeatsFor(Realm state)
    {
        long population = Math.Max(1L, state?.PopulationCount() ?? 1);
        return Mathf.Clamp((int)Math.Floor(Math.Log10(population)) - 2, 1, 4);
    }

    public static void Maintain(Empire empire)
    {
        Realm core = empire?.CoreKingdom;
        if (core == null || empire.data == null) return;
        empire.data.CabinetMembers ??= new List<long>();
        if (!CityStateService.HasSenate(empire))
        {
            if (empire.data.CabinetMembers.Count > 0) empire.data.CabinetMembers.Clear();
            return;
        }
        List<long> previous = empire.data.CabinetMembers.ToList();
        var used = new HashSet<long>();
        var senators = new List<long>();
        foreach (Realm state in empire.kingdoms_list
                     .Where(kingdom => kingdom != null && !kingdom.IsGone() && kingdom != core)
                     .OrderByDescending(kingdom => kingdom.Warriors()).ToList())
        {
            int seats = SeatsFor(state);
            var chosen = new List<Person>();
            void Take(Person actor)
            {
                if (chosen.Count < seats && Eligible(actor, core) && actor.Realm == state && used.Add(actor.Key))
                    chosen.Add(actor);
            }
            if (state.HasRuler()) Take(state.Ruler);
            foreach (long id in previous) Take(EmpireWorld.Current.PersonByKey(id));
            if (chosen.Count < seats && state.People != null)
                foreach (Person actor in state.People.ToList()
                             .Where(actor => Eligible(actor, core))
                             .OrderByDescending(actor => actor.GetOrCreate().socialClass == SocialClass.Noble)
                             .ThenByDescending(actor => actor.Renown))
                    Take(actor);
            senators.AddRange(chosen.Select(actor => actor.Key));
        }
        empire.data.CabinetMembers = senators;
        var before = new HashSet<long>(previous);
        foreach (long id in senators.Where(id => !before.Contains(id)))
            EmpireWorld.Current.PersonByKey(id)?.RecordPersonalHistory(LM.Get("personal_history_became_senator"));
    }

    private static bool Eligible(Person actor, Realm core) =>
        actor?.Record != null && !actor.IsGone() && actor.Alive && actor.IsAdult() && !actor.IsWarMachine() &&
        !actor.IsSkeleton() && actor.CanServeOffice(core);
}
}
