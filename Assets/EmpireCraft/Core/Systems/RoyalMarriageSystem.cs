// Generated from the EmpireCraft mod: Scripts/GeneralSystems/RoyalMarriageSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 王室联姻与继承(西方封建制、阿拉伯政体的君主国；用户 2026-10-19 选"西方与阿拉伯君主国"，华夏、游牧走和亲，
// 见 FrontierRelations。原始封建制 2026-11-06 改为君主制后也议婚：印度从阿拉伯政体改走它，孔雀、莫卧儿与班图诸王国照旧联姻)：
//   ① 联姻：每年各国可能为储君、国王(没有正室时)、成年未婚的王子公主向他国王室议婚——对方的国王、储君或王子公主，
//      异性、成年、未婚、不是近亲；看对方国力、是否接壤、信仰与文化是否相同、两国好感，配国王、储君的更体面。
//      只与够得着的国家议婚(用户 2026-11-04 "超远距离外交的bug"；以前全世界的王室都挑，隔半个地图也结亲)：
//      通达层算好后(用户 2026-11-06 同意，见 ConnectivitySystem)按本国派使节过去的人员往来强度(本国的航海档、路，不要战舰)
//      不低于 diplomacy_min_strength(0.25)；近便加分 marriage_bonus(1.5) × 强度(以前接壤或隔海相望一律 +1.5)。
//      没算好时照旧同宣战的远近(WarReach.Reachable：接壤、同一块陆地上相距不远且不隔着过不去的无人区，或隔海在水师辐射内)。
//      一方是在位君主或储君的，另一方嫁(入赘)过去；两位在位君主联姻则各守其国，子女日后兼承两国；都不是的，
//      女方嫁到男方的国家。两国结为姻亲：二十年内互不开战，一方遭外敌攻打另一方可能出兵相助。
//   ② 宣称：在位君主与他国君主是隔四代以内的血亲(含外嫁女子的后代)，就对那国王位有继承宣称，可以为此开战
//      (见 FeudalVassalService.CanDeclareExternalWar)。
//   ③ 继承(见 EmpireCraftKingdomBehCheckHeir)：封建王国(阿拉伯按父系，不走这一条)本宗绝嗣，按血缘远近找外嫁宗亲的后代入继；
//      入继者本是别国君主的，两国共主(见 PersonalUnionService)。
public static class RoyalMarriageSystem
{
    private const int PactYears = 20, ClaimDistance = 4, MinAge = 16, MaxAge = 40, KingMaxAge = 60;
    private const float ProposalChance = 0.35f, AidChance = 0.5f;

#if EC_PENDING
    public static bool Applies(Realm kingdom) =>
        kingdom?.Record != null && !kingdom.IsGone() && kingdom.HasRuler() &&
        kingdom.GetRegime()?.type is RegimeType.Feudalism or RegimeType.Arabic or RegimeType.OriginFeudalism;
#endif

    #region 议婚(RoyalHouseSystem 每月调用；各国按编号分到十二个月里，每国每年一次机会，不在同一帧里全算)

    private static int _month;

#if EC_PENDING
    public static void Monthly()
    {
        if (EmpireWorld.Current?.LiveRealms == null) return;
        // 世界法则「禁止AI和亲」(见 DiplomacyLaws)
        if (DiplomacyLaws.MarriageBlocked) return;
        int slot = _month++ % 12;
        using var timing = FrameProfiler.Measure("联姻·议婚");
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms.ToList())
        {
            if (kingdom?.Record == null || (int)(kingdom.Key % 12) != slot) continue;
            if (!Applies(kingdom) || EcRandom.value >= ProposalChance) continue;
            try
            {
                TryArrange(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][联姻] {kingdom.Name} 议婚失败: {exception.Message}");
            }
        }
    }
#endif

    // 本国的王室：储君、国王、国王的子女(有实体的)
#if EC_PENDING
    private static IEnumerable<Person> Royals(Realm kingdom)
    {
        Person king = kingdom.Ruler;
        if (kingdom.HasHeir()) yield return kingdom.GetHeir();
        yield return king;
        foreach (Person child in king?.GetChildren(false) ?? Enumerable.Empty<Person>()) yield return child;
    }
#endif

    private static bool Marriageable(Person actor, Realm kingdom)
    {
        if (actor?.Record == null || actor.IsGone() || !actor.Alive || !actor.IsAdult() || actor.IsEmperor()) return false;
        int age = actor.Age();
        if (age < MinAge || age > (actor == kingdom.Ruler ? KingMaxAge : MaxAge)) return false;
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        return identity != null && !identity.is_concubine && identity.IsFreeToMarry(-2L);
    }

    private static bool Ruling(Person actor, Realm kingdom) =>
        actor == kingdom.Ruler || kingdom.HasHeir() && kingdom.GetHeir() == actor;

    // 国王在族谱里的虚拟子女(纯净地图下王子公主多不在地图上；以前只配得到国王、储君，王子公主从不外嫁)：
    // 议婚时按族谱比，定下来才落成
#if EC_PENDING
    private static IEnumerable<PersonalClanIdentity> VirtualRoyals(Realm kingdom) =>
        kingdom?.Ruler?.Record == null ? Enumerable.Empty<PersonalClanIdentity>()
            : KinRoster.Children(kingdom.Ruler).Where(KinRoster.Virtual);
#endif

    private static bool Marriageable(PersonalClanIdentity person) =>
        KinRoster.Virtual(person) && person.age >= MinAge && person.age <= MaxAge && !person.is_concubine &&
        person.IsFreeToMarry(-2L);

#if EC_PENDING
    private static void TryArrange(Realm kingdom)
    {
        Person royal = Royals(kingdom).Distinct().FirstOrDefault(actor => Marriageable(actor, kingdom));
        PersonalClanIdentity royalPerson = royal != null ? royal.GetPersonalIdentity() : VirtualRoyals(kingdom).FirstOrDefault(Marriageable);
        if (royalPerson == null) return;
        Sex royalSex = royal != null ? royal.Sex : royalPerson.sex;
        (Person partner, PersonalClanIdentity person, Realm other) best = (null, null, null);
        float bestScore = float.MinValue;
        foreach (Realm other in EmpireWorld.Current.LiveRealms)
        {
            if (other == kingdom || other?.Record == null || other.IsGone() || !other.HasRuler() ||
                !RegimeManager.IsMonarchy(other.GetRegime()?.type) || kingdom.IsAtWarWith(other)) continue;
            float opinion = EmpireWorld.Current.diplomacy?.getOpinion(kingdom, other)?.total ?? 0;
            if (opinion < -30f) continue;
            float realm = (float)Math.Log10(1d + other.GetNationalPower());
            bool? reach = null;
            float contact = -1f;
            float Score(float rank)
            {
                float score = realm + opinion / 50f + EcRandom.Range(0f, 1.5f) + rank;
                if (contact >= 0f) score += ConnectivitySystem.Config.marriage_bonus * contact;
                else if (StraitReach.Near(kingdom, other)) score += 1.5f;
                if (kingdom.Religion != null && kingdom.Religion == other.Religion) score += 1f;
                if (CultureService.GetRealmCulture(kingdom) == CultureService.GetRealmCulture(other)) score += 0.5f;
                return score;
            }
            foreach (Person candidate in Royals(other).Distinct())
            {
                if (!Marriageable(candidate, other) || candidate.Sex == royalSex ||
                    (royal != null ? KinshipRules.AreCloseKin(royal, candidate)
                        : KinshipRules.AreCloseKin(royalPerson, candidate.GetPersonalIdentity()))) continue;
                // 够不着的国家不议婚(见类头)；量远近不便宜，到这里才量、一国只量一次
                if (reach == null) reach = Reach(kingdom, other, out contact);
                if (reach == false) break;
                float score = Score(candidate == other.Ruler ? 3f : other.HasHeir() && other.GetHeir() == candidate ? 2f : 0f);
                if (score <= bestScore) continue;
                bestScore = score;
                best = (candidate, null, other);
            }
            if (reach == false) continue;
            foreach (PersonalClanIdentity candidate in VirtualRoyals(other))
            {
                if (!Marriageable(candidate) || candidate.sex == royalSex || KinshipRules.AreCloseKin(royalPerson, candidate)) continue;
                if (reach == null) reach = Reach(kingdom, other, out contact);
                if (reach == false) break;
                float score = Score(0f);
                if (score <= bestScore) continue;
                bestScore = score;
                best = (null, candidate, other);
            }
        }
        if (best.other == null) return;
        // 定下来了才落成：虚拟的一方落成后照样要成年、可婚，不合格的并回人口
        royal ??= KinRoster.RealizeFirst(new[] { royalPerson }, actor => Marriageable(actor, kingdom), 1);
        Person partner = best.partner ?? KinRoster.RealizeFirst(new[] { best.person }, actor => Marriageable(actor, best.other), 1);
        if (royal == null || partner == null) return;
        Marry(kingdom, royal, best.other, partner);
    }
#endif

    // 议婚够得着(见类头)：contact 为本国派使节过去的强度(还没算为 -1)
#if EC_PENDING
    private static bool Reach(Realm kingdom, Realm other, out float contact)
    {
        contact = ConnectivitySystem.ContactStrength(kingdom, other);
        return contact >= 0f ? contact >= ConnectivitySystem.Config.diplomacy_min_strength : WarReach.Reachable(kingdom, other);
    }
#endif

    // 和亲(见 FrontierRelations)：本国君主的女儿或宗室女嫁给对方君主或储君；成了返回出嫁的人，没有合适的人返回 null
#if EC_PENDING
    public static Person ArrangeHeqin(Realm home, Realm other)
    {
        if (home?.Record == null || other?.Record == null || !home.HasRuler() || !other.HasRuler()) return null;
        Person groom = new[] { other.Ruler, other.HasHeir() ? other.GetHeir() : null }
            .FirstOrDefault(actor => actor != null && actor.IsMale() && Marriageable(actor, other));
        if (groom == null) return null;
        IEnumerable<Person> clan = home.GetEmpire()?.EmpireSpecificClan?.AllAliveMembers ?? Enumerable.Empty<Person>();
        bool Fit(Person actor) => actor != null && actor.IsFemale() && !Ruling(actor, home) &&
                                 Marriageable(actor, home) && !KinshipRules.AreCloseKin(actor, groom);
        Person bride = (home.Ruler?.GetChildren(false) ?? Enumerable.Empty<Person>()).Concat(clan).Distinct().FirstOrDefault(Fit);
        // 纯净地图下公主、宗室女多是虚拟族人：先君主的女儿，再宗室女，落成一位(以前地图上没有就和不成亲)
        if (bride == null)
        {
            PersonalClanIdentity groomPerson = groom.GetPersonalIdentity();
            SpecificClan royalClan = home.GetEmpire()?.EmpireSpecificClan ?? home.Ruler?.GetSpecificClan();
            IEnumerable<PersonalClanIdentity> daughters = home.Ruler?.Record == null ? Enumerable.Empty<PersonalClanIdentity>()
                : KinRoster.Children(home.Ruler);
            bride = KinRoster.RealizeFirst(daughters.Concat(KinRoster.Lineage(royalClan))
                    .Where(person => person.sex == Sex.Female && Marriageable(person) &&
                                     !KinshipRules.AreCloseKin(person, groomPerson))
                    .GroupBy(person => person.id).Select(group => group.First()), Fit, 2);
        }
        if (bride == null) return null;
        Marry(home, bride, other, groom, log: false);
        return bride.HasSpouse() ? bride : null;
    }

    private static void Marry(Realm home, Person royal, Realm other, Person partner, bool log = true)
    {
        PersonalClanIdentity a = royal.GetPersonalIdentity(), b = partner.GetPersonalIdentity();
        if (a == null || b == null) return;
        // 正室：由本宗承嗣的一方(男系宗族的男方、女系宗族的女方)娶(招)；鳏寡的先清掉已故配偶的记录
        bool aHeads = a.IsHeirPriority();
        PersonalClanIdentity head = aHeads ? a : b;
        Person spouse = aHeads ? partner : royal;
        if (head.hasLover() && head.IsFreeToMarry(-2L)) head.lover = (-1L, -1L);
        head.setLover(spouse);
        if (!head.hasLover()) return;
        royal.setLover(partner);
        partner.setLover(royal);
        // 谁去谁的国家
        bool royalRules = Ruling(royal, home), partnerRules = Ruling(partner, other);
        Person mover = null;
        Realm destination = null;
        if (royalRules && !partnerRules) (mover, destination) = (partner, home);
        else if (partnerRules && !royalRules) (mover, destination) = (royal, other);
        else if (!royalRules && !partnerRules)
            (mover, destination) = royal.IsFemale() ? (royal, other) : (partner, home);
        if (mover != null && destination?.Capital != null) Relocate(mover, destination);
        double now = GameClock.Now;
        home.GetOrCreate().marriage_pacts[other.Key] = now;
        other.GetOrCreate().marriage_pacts[home.Key] = now;
        if (!log) return;
        string text = string.Format(LM.Get("royal_marriage_history"), home.GetKingdomName(), royal.FullName(),
            other.GetKingdomName(), partner.FullName());
        TranslateHelper.LogEventMessage(text, home);
        Empire otherEmpire = other.GetEmpire();
        if (otherEmpire != null && otherEmpire != home.GetEmpire()) otherEmpire.RecordHistory(directContent: text);
    }

    private static void Relocate(Person mover, Realm destination)
    {
        if (mover.IsOnOffice())
        {
            mover.GetOffice()?.RemoveActor();
            mover.EndOffice();
        }
        if (mover.IsCityLeader() && mover.City != null) mover.City.removeLeader();
        mover.removeFromArmy();
        mover.joinKingdom(destination);
        mover.JoinCity(destination.Capital);
    }
#endif

    #endregion

    #region 姻亲之国

    // 二十年内的姻亲：互不开战(EmpireCraftPlotsAddition、WarTargetPatch、FeudalVassalService)
    public static bool Allied(Realm a, Realm b)
    {
        if (a?.Record == null || b?.Record == null || a == b) return false;
        return Pact(a, b) || Pact(Core(a), Core(b));
    }

    private static Realm Core(Realm kingdom) => kingdom.GetEmpire()?.CoreKingdom ?? kingdom;

    private static bool Pact(Realm a, Realm b)
    {
        if (a?.Record == null || b?.Record == null || a == b) return false;
        var pacts = a.GetOrCreate().marriage_pacts;
        if (pacts == null || !pacts.TryGetValue(b.Key, out double since)) return false;
        if (GameClock.YearsSince(since) < PactYears && !b.IsGone()) return true;
        pacts.Remove(b.Key);
        return false;
    }

    // 姻亲之国遭外敌攻打：可能出兵相助(WarPatch.new_war)
#if EC_PENDING
    public static void OnWarStarted(War war)
    {
        Realm defender = war?.MainDefender, attacker = war?.MainAttacker;
        var pacts = defender?.Record == null ? null : defender.GetOrCreate().marriage_pacts;
        if (pacts == null || pacts.Count == 0 || attacker == null) return;
        foreach (long id in pacts.Keys.ToList())
        {
            Realm ally = EmpireWorld.Current.RealmByKey(id);
            if (ally?.Record == null || ally.IsGone() || !Pact(defender, ally) || ally == attacker || war.HasRealm(ally) ||
                Allied(ally, attacker) || ally.IsAtWarWith(attacker) || EcRandom.value >= AidChance) continue;
            war.JoinDefenders(ally);
            TranslateHelper.LogEventMessage(string.Format(LM.Get("royal_marriage_aid_history"), ally.GetKingdomName(),
                defender.GetKingdomName()), ally);
        }
    }
#endif

    #endregion

    #region 宣称

    // 两国君主是隔 ClaimDistance 代以内的血亲(沿父母、子女走，含外嫁女子的后代)：claimant 对 target 的王位有宣称。
    // 挑开战目标时各国每隔几秒就要对每个邻国查一次：同一对君主一分钟内直接用上次的结果
    private static readonly Dictionary<(long, long), (bool claim, float at)> ClaimCache = new();
    private const float ClaimCacheSeconds = 60f;

    public static bool HasClaim(Realm claimant, Realm target)
    {
        Person a = claimant?.Ruler, b = target?.Ruler;
        if (a?.Record == null || b?.Record == null || a == b) return false;
        var key = (a.Key, b.Key);
        float now = Time.unscaledTime;
        if (ClaimCache.TryGetValue(key, out var cached) && now - cached.at < ClaimCacheSeconds && now >= cached.at)
            return cached.claim;
        if (ClaimCache.Count > 4000) ClaimCache.Clear();
        using var timing = FrameProfiler.Measure("联姻·继承宣称");
        int distance = BloodDistance(a.GetPersonalIdentity(), b.GetPersonalIdentity(), ClaimDistance);
        bool claim = distance > 0 && distance <= ClaimDistance;
        ClaimCache[key] = (claim, now);
        return claim;
    }

    // 血缘远近(沿父母、子女走的步数，找不到为 -1)
    public static int BloodDistance(PersonalClanIdentity from, PersonalClanIdentity to, int limit)
    {
        if (from == null || to == null) return -1;
        if (from.id == to.id) return 0;
        var seen = new HashSet<long> { from.id };
        var frontier = new List<PersonalClanIdentity> { from };
        for (int depth = 1; depth <= limit && frontier.Count > 0; depth++)
        {
            var next = new List<PersonalClanIdentity>();
            foreach (PersonalClanIdentity person in frontier)
                foreach (long id in Relatives(person))
                {
                    if (!seen.Add(id)) continue;
                    if (id == to.id) return depth;
                    PersonalClanIdentity relative = SpecificClanManager.getPerson(id);
                    if (relative != null) next.Add(relative);
                }
            if (seen.Count > 2000) break;
            frontier = next;
        }
        return -1;
    }

    // 外家入继：从先君出发沿父母、子女找血亲，近的在前(同样近的成年人在前、年长的在前)
    public static IEnumerable<(PersonalClanIdentity person, int distance)> Cognates(PersonalClanIdentity origin, int limit)
    {
        if (origin == null) yield break;
        var seen = new HashSet<long> { origin.id };
        var frontier = new List<PersonalClanIdentity> { origin };
        for (int depth = 1; depth <= limit && frontier.Count > 0; depth++)
        {
            var next = new List<PersonalClanIdentity>();
            foreach (PersonalClanIdentity person in frontier)
                foreach (long id in Relatives(person))
                {
                    if (!seen.Add(id)) continue;
                    PersonalClanIdentity relative = SpecificClanManager.getPerson(id);
                    if (relative != null) next.Add(relative);
                }
            foreach (PersonalClanIdentity relative in next.Where(person => person.is_alive && !person.is_concubine)
                         .OrderByDescending(person => person.age >= 18).ThenByDescending(person => person.age))
                yield return (relative, depth);
            if (seen.Count > 2000) yield break;
            frontier = next;
        }
    }

    private static IEnumerable<long> Relatives(PersonalClanIdentity person)
    {
        if (person.father > 0) yield return person.father;
        if (person.mother > 0) yield return person.mother;
        foreach (long child in person.children ?? new List<long>()) yield return child;
    }

    #endregion
}
}
