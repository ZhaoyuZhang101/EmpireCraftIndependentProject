// Generated from the EmpireCraft mod: Scripts/GeneralSystems/GentrySystem.Events.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Random = EmpireCraft.Core.EcRandom;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 世家事件与名人(用户 2026-10-27 问答选"世家事件与名人")：
//   · 家学渊源：世家子弟立功成名快一半(见 FamousTalentSystem.Credit)；
//   · 世家女入宫：华夏帝国的皇帝(或储君)成年、没有在世的正室，每年按 ConsortChance 从本朝门第最高的几家里挑一位
//     待嫁的女儿(15~30 岁)入宫为后(储君为妃)，那家门第 +3、朝廷与世家关系 +10，史书记、上屏幕；
//   · 灾年赈济：郡望之城闹饥荒，世家每年按 ReliefChance 开仓赈济乡里——门第 +2、国家粮仓 +门第×2、
//     这座城两年里自然稳定度 +ReliefStability(见 StabilityBonus)，三年至多一回；
//   · 世家相争：本朝两家甲乙等世家族中都有人在朝，每年按 FeudChance 争权分党——两家门第各 -3、法统 -1、
//     朝廷与世家关系 -3，每朝 FeudYears 年至多一回。
public static partial class GentrySystem
{
    private const float FameBonus = 1.5f, ConsortChance = 0.5f, HeirConsortChance = 0.3f, ReliefChance = 0.4f,
        ReliefPedigree = 2f, ReliefGranaryPerPedigree = 2f, ConsortPedigree = 3f, FeudChance = 0.06f, FeudPedigree = 3f;
    private const int ConsortClans = 5, BrideMinAge = 15, BrideMaxAge = 30, ReliefYears = 2, ReliefCooldownYears = 3, FeudYears = 15;
    public const float ReliefStability = 6f;

    public static float FameFactor(Person actor) => IsGentry(actor) ? FameBonus : 1f;

    // 世家女入宫(每年，见 YearlyCourt)：皇帝没有正室为后，储君没有正室为妃
#if EC_PENDING
    private static void TryConsort(Empire empire, List<SpecificClan> clans)
    {
        Realm core = empire.CoreKingdom;
        if (core?.Capital == null || !Huaxia(core.Capital)) return;
        Person emperor = empire.Emperor;
        Person heir = core.HasHeir() ? core.GetHeir() : null;
        if (Unwed(emperor) && Random.value < ConsortChance) Wed(empire, clans, emperor, "gentry_empress_history");
        else if (heir != emperor && Unwed(heir) && Random.value < HeirConsortChance) Wed(empire, clans, heir, "gentry_heir_consort_history");
    }
#endif

    private static bool Unwed(Person actor)
    {
        if (actor?.Record == null || actor.IsGone() || !actor.Alive || !actor.IsAdult() || !actor.IsMale()) return false;
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        return identity != null && identity.IsFreeToMarry(-2L);
    }

#if EC_PENDING
    private static void Wed(Empire empire, List<SpecificClan> clans, Person groom, string key)
    {
        foreach (SpecificClan clan in clans.Take(ConsortClans).OrderByDescending(clan => clan.gentry_pedigree * Random.Range(0.6f, 1f)))
        {
            PersonalClanIdentity candidate = clan.LivingPeople.FirstOrDefault(person => person.sex == Sex.Female &&
                person.age >= BrideMinAge && person.age <= BrideMaxAge && !person.is_concubine && person.IsFreeToMarry(-2L));
            if (candidate == null) continue;
            Person bride = candidate.is_virtual ? candidate.Realize() : candidate._actor;
            if (bride?.Record == null || bride.IsGone() || !bride.Alive || KinshipRules.AreCloseKin(bride, groom)) continue;
            PersonalClanIdentity head = groom.GetPersonalIdentity();
            if (head == null) return;
            // 鳏居的先清掉已故正室的记录(同 RoyalMarriageSystem)
            if (head.hasLover() && head.IsFreeToMarry(-2L)) head.lover = (-1L, -1L);
            head.setLover(bride);
            if (!head.hasLover()) continue;
            groom.setLover(bride);
            bride.setLover(groom);
            Realm core = empire.CoreKingdom;
            if (bride.IsCityLeader() && bride.City != null) bride.City.RemoveLeader();
            if (bride.Realm != core) bride.SetRealm(core);
            if (core.Capital != null && bride.City != core.Capital) bride.JoinCity(core.Capital);
            clan.gentry_pedigree = Mathf.Min(100f, clan.gentry_pedigree + ConsortPedigree);
            ShiftFavor(empire, FavorEmpress);
            Record(Seat(clan), string.Format(LM.Get(key), JunWang(clan), bride.FullName(), groom.FullName()), force: true);
            NoteClan(clan, "clan_history_gentry_consort", string.Format(LM.Get(key), JunWang(clan), bride.FullName(), groom.FullName()));
            return;
        }
    }
#endif

    // 灾年赈济(每年，Assess 里)
#if EC_PENDING
    private static void TryRelief(SpecificClan clan, City seat)
    {
        if (seat?.Realm == null || !FamineUnrestSystem.InFamine(seat) || Random.value >= ReliefChance) return;
        double now = GameClock.Now;
        if (clan.gentry_relief_at >= 0d && now >= clan.gentry_relief_at && GameClock.YearsSince(clan.gentry_relief_at) < ReliefCooldownYears)
            return;
        clan.gentry_relief_at = now;
        clan.gentry_pedigree = Mathf.Min(100f, clan.gentry_pedigree + ReliefPedigree);
        GranarySystem.Donate(seat.Realm, clan.gentry_pedigree * ReliefGranaryPerPedigree);
        string text = string.Format(LM.Get("gentry_relief_history"), JunWang(clan), seat.GetCityName());
        Record(seat, text, force: true);
        NoteClan(clan, "clan_history_gentry_relief", text);
    }
#endif

    // 赈济过的城两年里自然稳定度加成
    private static bool Relieved(City city)
    {
        if (city?.Record == null) return false;
        foreach (long id in GentryIds)
        {
            SpecificClan clan = SpecificClanManager.Get(id);
            if (clan == null || clan.gentry_seat_city_id != city.Key || clan.gentry_relief_at < 0d) continue;
            double now = GameClock.Now;
            if (now >= clan.gentry_relief_at && GameClock.YearsSince(clan.gentry_relief_at) < ReliefYears) return true;
        }
        return false;
    }

    // 世家相争(每年，见 YearlyCourt)
    private static void TryFeud(Empire empire, List<SpecificClan> clans)
    {
        double now = GameClock.Now;
        if (empire.data.gentry_feud_at >= 0d && now >= empire.data.gentry_feud_at && GameClock.YearsSince(empire.data.gentry_feud_at) < FeudYears)
            return;
        List<SpecificClan> strong = clans.Where(clan => Grade(clan.gentry_pedigree) <= 2 && InOffice(clan) > 0).Take(4).ToList();
        if (strong.Count < 2 || Random.value >= FeudChance) return;
        SpecificClan a = strong[0], b = strong[1 + Random.Range(0, strong.Count - 1)];
        empire.data.gentry_feud_at = now;
        a.gentry_pedigree = Mathf.Max(0f, a.gentry_pedigree - FeudPedigree);
        b.gentry_pedigree = Mathf.Max(0f, b.gentry_pedigree - FeudPedigree);
        empire.AddMandate(-1);
        ShiftFavor(empire, FavorFeud);
        string text = string.Format(LM.Get("gentry_feud_history"), JunWang(a), JunWang(b));
        Record(Seat(a), text, force: true);
        NoteClan(a, "clan_history_gentry_feud", text);
        NoteClan(b, "clan_history_gentry_feud", text);
    }
}
}
