// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PersonalTieSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 个人恩怨(用户 2026-10-19 选"个人恩怨")：要人之间的恩主/门生、宿敌，记在族谱身份上(每人至多三条，见 PersonalClanIdentity.ties)。
//   结缘/结仇：被弹劾去职(与弹劾一派的首领)；要人被杀(死者的子与兄弟与凶手，杀亲之仇)；被俘放归、赎回(与胜方统领)；
//   殿试及第(以当朝首辅为座主)。
//   每年结算(只看在世的实体要人，一人一年至多一件事)：
//     · 构陷：同朝为官的宿敌，名望不低于对方的一边有一成机会使对方去职；
//     · 复仇：有杀亲之仇的，有五分机会遣刺客，三成五得手(刺君主减半)；
//       构陷得逞、遣刺客得手都暗中记罪(陷害、杀人，刺本国君主是谋反)，日后由查案发现(见 LawEvents)；
//     · 投敌：与本国君主有仇的统领、官员，本国正与人交战时有五分机会投奔敌国；
//   朝堂：恩主被弹劾，在朝为官的门生每人使弹劾难成一成(至多两成)；弹劾宿敌易成一成五(见 CourtSystem.TryImpeach)。
//   恩怨写进双方生平(大事)，列传里列出(见 BiographySystem.Compose)。
public static class PersonalTieSystem
{
    public const int Patron = 1, Protege = 2, Rival = 3;
    public const string CauseImpeached = "impeached", CauseKinKilled = "kin_killed", CauseCaptured = "captured",
        CauseExam = "exam", CauseDismissed = "dismissed", CauseRank = "rank", CauseFavourite = "favourite";
    private const int MaxTies = 3, MaxAvengers = 3;
    private const float FrameChance = 0.1f, RevengeChance = 0.05f, RevengeSuccess = 0.35f, DefectChance = 0.05f;

    #region 结缘结仇

    private static void Add(PersonalClanIdentity owner, PersonalClanIdentity other, int kind, string cause)
    {
        if (owner == null || other == null || owner.id == other.id || EmpireWorld.Current == null) return;
        owner.ties ??= new List<PersonalTie>();
        owner.ties.RemoveAll(tie => tie == null || tie.other == other.id);
        if (owner.ties.Count >= MaxTies)
        {
            // 满了先挤掉最早的恩主/门生，都是宿敌就挤掉最早的宿敌
            PersonalTie drop = owner.ties.Where(tie => tie.kind != Rival).OrderBy(tie => tie.since).FirstOrDefault() ??
                               owner.ties.OrderBy(tie => tie.since).First();
            owner.ties.Remove(drop);
        }
        owner.ties.Add(new PersonalTie { other = other.id, kind = kind, cause = cause, since = GameClock.Now });
        if (kind == Rival && cause == CauseKinKilled) Avengers?.Add(owner.id);
    }

#if EC_PENDING
    public static void MakeRivals(Person a, Person b, string cause)
    {
        PersonalClanIdentity ia = a?.GetPersonalIdentity(), ib = b?.GetPersonalIdentity();
        if (ia == null || ib == null || ia.id == ib.id || TieOf(ia, ib.id)?.kind == Rival) return;
        Add(ia, ib, Rival, cause);
        Add(ib, ia, Rival, cause);
        string text = LM.Get("tie_cause_" + cause);
        BiographySystem.Note(a, string.Format(LM.Get("bio_tie_rival"), b.FullName(), text), b);
        BiographySystem.Note(b, string.Format(LM.Get("bio_tie_rival"), a.FullName(), text), a);
    }

    public static void MakePatron(Person protege, Person patron, string cause)
    {
        PersonalClanIdentity ip = protege?.GetPersonalIdentity(), ia = patron?.GetPersonalIdentity();
        if (ip == null || ia == null || ip.id == ia.id || TieOf(ip, ia.id) != null) return;
        Add(ip, ia, Patron, cause);
        Add(ia, ip, Protege, cause);
        BiographySystem.Note(protege, string.Format(LM.Get("bio_tie_patron"), patron.FullName()), patron);
    }
#endif

    // 要人被杀：死者在世的子与兄弟(至多三人)与凶手结下杀亲之仇
    public static void OnKinKilled(Person victim, Person killer)
    {
        PersonalClanIdentity dead = victim?.GetPersonalIdentity();
        PersonalClanIdentity murderer = killer?.GetPersonalIdentity();
        if (dead == null || murderer == null || EmpireWorld.Current == null) return;
        IEnumerable<PersonalClanIdentity> kin = SpecificClanManager.getChildren(dead).Select(pair => pair.Item2)
            .Concat(SpecificClanManager.GetSiblingsWithRelation(dead).Select(pair => pair.Item2));
        int count = 0;
        foreach (PersonalClanIdentity relative in kin)
        {
            if (count >= MaxAvengers) break;
            if (relative == null || !relative.is_alive || relative.id == murderer.id || relative.age < 12) continue;
            Add(relative, murderer, Rival, CauseKinKilled);
            Add(murderer, relative, Rival, CauseKinKilled);
            relative.RecordPersonalHistory(string.Format(LM.Get("bio_tie_kin_killed"), victim.FullName(), killer.FullName()),
                eventKey: "tie", relatedActorId: killer.Key, major: true);
            count++;
        }
    }

    public static PersonalTie TieOf(PersonalClanIdentity owner, long otherId) =>
        owner?.ties?.FirstOrDefault(tie => tie != null && tie.other == otherId);

    public static bool AreRivals(Person a, Person b) =>
        TieOf(a?.GetPersonalIdentity(), b?.GetPersonalIdentity()?.id ?? -1L)?.kind == Rival;

    // 在朝为官、在世的门生人数
    public static int ProtegesInOffice(Person patron)
    {
        PersonalClanIdentity identity = patron?.GetPersonalIdentity();
        if (identity?.ties == null) return 0;
        int count = 0;
        foreach (PersonalTie tie in identity.ties)
        {
            if (tie?.kind != Protege) continue;
            Person actor = SpecificClanManager.getPerson(tie.other)?._actor;
            if (actor?.Record != null && actor.Alive && actor.IsOnOffice() && actor.Realm?.GetEmpire() == patron.Realm?.GetEmpire())
                count++;
        }
        return count;
    }

    #endregion

    #region 年度结算

#if EC_PENDING
    public static void Yearly()
    {
        if (EmpireWorld.Current?.Living == null) return;
        int acted = 0;
        foreach (Person actor in EmpireWorld.Current.Living.ToList())
        {
            if (acted >= 12) break;
            PersonalClanIdentity identity = actor?.Record == null || actor.IsGone() || !actor.Alive ? null : actor.GetPersonalIdentity();
            if (identity?.ties == null || identity.ties.Count == 0) continue;
            try
            {
                if (Act(actor, identity)) acted++;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][恩怨] {actor.FullName()} 结算失败: {exception.Message}");
            }
        }
        if (acted < 12) VirtualRevenge(12 - acted);
    }
#endif

    // 记着杀亲之仇的人(族谱身份)：读档后头一回年结扫一遍族谱，之后结仇时补记
    private static HashSet<long> Avengers;

    public static void ResetAvengers() => Avengers = null;

    // 报杀亲之仇的多是死者的子与兄弟，纯净地图下多是虚拟族人(以前年结只看地图上的人，仇结了永远报不了)：
    // 族谱里在世、仇人也在世的照样按 RevengeChance 掷，掷中了才把两人落成、照常报仇
#if EC_PENDING
    private static void VirtualRevenge(int budget)
    {
        if (Avengers == null)
        {
            Avengers = new HashSet<long>();
            foreach (PersonalClanIdentity person in SpecificClanManager._globalPersonLookup.Values)
                if (person?.ties != null && person.ties.Exists(tie => tie?.kind == Rival && tie.cause == CauseKinKilled))
                    Avengers.Add(person.id);
        }
        foreach (long id in Avengers.ToList())
        {
            if (budget <= 0) return;
            PersonalClanIdentity avenger = SpecificClanManager.getPerson(id);
            PersonalTie tie = avenger?.ties?.FirstOrDefault(item => item?.kind == Rival && item.cause == CauseKinKilled);
            PersonalClanIdentity target = tie == null ? null : SpecificClanManager.getPerson(tie.other);
            if (avenger == null || !avenger.is_alive || tie == null || target == null || !target.is_alive)
            {
                Avengers.Remove(id);
                continue;
            }
            // 在地图上的照上面的年结走
            if (!KinRoster.Virtual(avenger) || avenger.age < 16 || EcRandom.value >= RevengeChance) continue;
            Person actor = PersonRef.Present(avenger), rival = actor == null ? null : PersonRef.Present(target);
            if (actor == null || rival == null) continue;
            budget--;
            try
            {
                Revenge(actor, rival);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][恩怨] {actor.FullName()} 报仇失败: {exception.Message}");
            }
        }
    }

    private static bool Act(Person actor, PersonalClanIdentity identity)
    {
        foreach (PersonalTie tie in identity.ties.Where(tie => tie?.kind == Rival).ToList())
        {
            Person rival = SpecificClanManager.getPerson(tie.other)?._actor;
            if (rival?.Record == null || rival.IsGone() || !rival.Alive) continue;
            if (tie.cause == CauseKinKilled && EcRandom.value < RevengeChance) return Revenge(actor, rival);
            if (rival.IsRuler() && rival.Realm == actor.Realm && !actor.IsRuler() &&
                (CorpsSystem.IsGeneral(actor) || actor.IsOnOffice()) &&
                EcRandom.value < DefectChance * (AmbitionSystem.Wants(actor, AmbitionSystem.Throne) ? 2f : 1f) &&
                Defect(actor, rival)) return true;
            if (actor.IsOnOffice() && rival.IsOnOffice() && actor.Realm?.GetEmpire() is Empire empire &&
                rival.Realm?.GetEmpire() == empire && actor.Renown >= rival.Renown && EcRandom.value < FrameChance)
                return Frame(actor, rival);
        }
        return false;
    }
#endif

    // 构陷：使宿敌去职
#if EC_PENDING
    private static bool Frame(Person actor, Person rival)
    {
        OfficeObject office = rival.GetOffice();
        if (office == null) return false;
        office.RemoveActor();
        BiographySystem.Note(actor, string.Format(LM.Get("bio_tie_framed_other"), rival.FullName()), rival);
        BiographySystem.Note(rival, string.Format(LM.Get("bio_tie_framed_by"), actor.FullName()), actor);
        Record(actor.Realm, string.Format(LM.Get("tie_frame_history"), actor.FullName(), rival.FullName()), actor);
        // 构陷得逞：暗中记一笔陷害(日后查办，见 LawEvents)
        LawEvents.Framed(actor);
        return true;
    }
#endif

    // 复仇：遣刺客
#if EC_PENDING
    private static bool Revenge(Person actor, Person rival)
    {
        float chance = RevengeSuccess * (rival.IsRuler() ? 0.5f : 1f);
        if (EcRandom.value < chance)
        {
            // 遣刺客杀人：同国(同一帝国)的记杀人，刺杀本国君主记谋反(暗中，日后查办，见 LawEvents)；死前判定
            LawEvents.Assassinated(actor, rival.Realm, rival.IsRuler() && rival.Realm == actor.Realm);
            string text = string.Format(LM.Get("tie_revenge_history"), actor.FullName(), rival.FullName());
            BiographySystem.Note(actor, string.Format(LM.Get("bio_tie_revenge"), rival.FullName()), rival);
            Record(rival.Realm, text, rival);
            BiographySystem.Kill(rival, string.Format(LM.Get("death_cause_revenge"), actor.FullName()), actor);
        }
        else
        {
            BiographySystem.Note(actor, string.Format(LM.Get("bio_tie_revenge_failed"), rival.FullName()), rival);
            BiographySystem.Note(rival, string.Format(LM.Get("bio_tie_survived_assassin"), actor.FullName()), actor);
        }
        return true;
    }
#endif

    // 投敌：与本国君主有仇，投奔正与本国交战的敌国
#if EC_PENDING
    private static bool Defect(Person actor, Person ruler)
    {
        Realm home = actor.Realm;
        Realm enemy = home?.GetWars().Where(war => war?.Record != null && !war.HasEnded())
            .SelectMany(war => war.IsAttacker(home) ? war.Defenders : war.Attackers)
            .Where(kingdom => kingdom?.Record != null && !kingdom.IsGone() && kingdom.Capital?.Record != null && !kingdom.Capital.IsGone())
            .OrderByDescending(kingdom => kingdom.CountCities()).FirstOrDefault();
        if (enemy == null) return false;
        CorpsSystem.LoseGeneral(actor);
        actor.GetOffice()?.RemoveActor();
        string text = string.Format(LM.Get("tie_defect_history"), actor.FullName(), ruler.FullName(), enemy.GetKingdomName());
        BiographySystem.Note(actor, string.Format(LM.Get("bio_tie_defected"), ruler.FullName(), enemy.GetKingdomName()), ruler);
        Record(home, text, actor);
        actor.JoinCity(enemy.Capital);
        actor.SetRealm(enemy);
        return true;
    }

    private static void Record(Realm kingdom, string text, Person actor)
    {
        if (kingdom == null) return;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && !empire.IsGone()) EventRecorder.Record(empire, text, actor, kingdom);
        else EventRecorder.Record(kingdom, text, actor);
    }
#endif

    #endregion

    // 列传里的一句：座主、门生、宿敌
    public static string Describe(PersonalClanIdentity person)
    {
        if (person?.ties == null || person.ties.Count == 0) return "";
        var parts = new List<string>();
        foreach (PersonalTie tie in person.ties)
        {
            string name = SpecificClanManager.getPerson(tie?.other ?? -1L)?.name;
            if (tie == null || string.IsNullOrWhiteSpace(name)) continue;
            parts.Add(tie.kind switch
            {
                Patron => string.Format(LM.Get("biography_tie_patron"), name),
                Protege => string.Format(LM.Get("biography_tie_protege"), name),
                _ => string.Format(LM.Get("biography_tie_rival"), name, LM.Get("tie_cause_" + tie.cause))
            });
        }
        return parts.Count == 0 ? "" : string.Join(LM.Get("biography_tie_separator"), parts) + LM.Get("biography_period");
    }
}
}
