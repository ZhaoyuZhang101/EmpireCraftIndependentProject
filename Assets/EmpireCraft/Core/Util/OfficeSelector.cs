// Generated from the EmpireCraft mod: Scripts/HelperFunc/OfficeSelector.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public static class OfficeSelector
{
#if EC_PENDING
    public static bool IsPreferredOfficeCandidate(Person actor, Realm kingdom)
    {
        if (actor == null) return false;
        if (kingdom == null) return true;
        // 现代、共和政体任官不分性别
        if (kingdom.GetRegime()?.type is RegimeType.Modern or RegimeType.Republic) return true;
        string culture = CultureService.GetRealmCulture(kingdom);
        if (string.IsNullOrEmpty(culture)) return true;
        if (!OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) || setting?.traits == null)
            return true;
        if (setting.traits.Contains("patriarchy")) return actor.isSexMale();
        if (setting.traits.Contains("matriarchy")) return actor.isSexFemale();
        return true;
    }

    public static void Select(this OfficeObject office, Realm pKingdom, string debugType = "中央")
    {
        Person actor = null;
        Regime regime = pKingdom.GetRegime();
        LeaderSelectMethod method = office.leader_select_method;
        if (office.leader_select_method == LeaderSelectMethod.Default)
        {
            method =  regime.GetLeaderSelectMethod();
        }
        // 古典共和帝国首领：施行「元老院」之前谁强谁上，之后由元老院推举；施行「元首制」后元首在家族里相传
        if (regime?.type == RegimeType.ClassicalRepublic && pKingdom.IsEmpire() && office == pKingdom.GetOffice())
            method = pKingdom.GetEmpire()?.IsPrincipate() == true ? LeaderSelectMethod.Succession
                : CityStateService.HasSenate(pKingdom.GetEmpire()) ? LeaderSelectMethod.Vote : LeaderSelectMethod.Army;
        // 殖民地总督府：总督由宗主国派(见 ColonySystem.PickGovernor)；特许公司照投票由董事会选
        if (office == pKingdom.GetOffice() && ColonySystem.PickGovernor(pKingdom) is Person governor)
        {
            office.SetActor(governor);
            return;
        }
        switch (method)
        {
            case LeaderSelectMethod.Exam:
                actor = TryGetExamOfficer(office, pKingdom)??TryGetClanOfficer(pKingdom, office)??TryGetProfessionOfficer(pKingdom, office);
                break;
            case LeaderSelectMethod.Succession:
                // 世家子弟入仕(九品中正、察举、世卿世禄，见 GentrySystem)
                actor = GentrySystem.Candidate(pKingdom, unit => unit.isUnitFitToRule() && unit.CanServeOffice(pKingdom) &&
                                                                 IsPreferredOfficeCandidate(unit, pKingdom) && office.Accepts(unit))
                        ?? TryGetClanOfficer(pKingdom, office)??TryGetProfessionOfficer(pKingdom, office);
                break;
            case LeaderSelectMethod.Vote:
                // 穿越政体：执委会主席由元老院全体大会从元老里选(见 TransmigrantSystem.ElectChairman)，选不出也不找外人
                if (office == pKingdom.GetOffice() && TransmigrantSystem.IsTransmigrant(pKingdom))
                {
                    actor = TransmigrantSystem.ElectChairman(pKingdom);
                    if (actor == null) return;
                    break;
                }
                actor = TryGetFeudalElectiveRuler(office, pKingdom) ?? TryGetProfessionOfficer(pKingdom, office);
                break;
            case LeaderSelectMethod.Army:
                actor = TryGetStrongerLeader(office, pKingdom)??TryGetProfessionOfficer(pKingdom, office);
                break;
            case LeaderSelectMethod.Harem:
                if (!EmpireCraftWorldLawLibrary.empirecraft_law_allow_harem.isEnabled())
                {
                    break;
                }
                actor = TryGetHarem(office, pKingdom);
                break;
            default:
                actor = TryGetProfessionOfficer(pKingdom, office);
                break;
        }

        // 等着起事的流寇不任官(见 ScenarioEventSystem.IsReserved)，换人
        if (EmpireCraft.Core.ScenarioEventSystem.IsReserved(actor)) actor = null;
        // 华夏一系的君主国不立女主(见 EmpireCraftKingdomBehCheckHeir.BarredBySex)：择官偏好只是先后，没有合偏好的人时会选出女子——
        // 君主之位选出女子就换本国一位够格的男子，没有就先空着(无小人模式由王位检查从都城人口里落成一位)
        if (actor != null && office == pKingdom.GetOffice() && regime != null && RegimeManager.IsMonarchy(regime.type) &&
            EmpireCraft.Core.EmpireCraftKingdomBehCheckHeir.BarredBySex(pKingdom, actor))
            actor = MaleRuler(office, pKingdom);
        // 选出的人任不上(妃嫔、君主配偶……)就当没选出，下面从人口里补；以前 SetActor 拒掉后官位空着，下一轮又选中同一人
        // (君主之位照旧：选帝、立宪皇位有自己的规矩)
        if (actor != null && office != pKingdom.GetOffice() && !office.Accepts(actor)) actor = null;
        // 前现代父系(母系)文化：选出的人不合择官偏好时，无小人模式先从人口里补一名合偏好的人，补不出才用原人选
        // (选帝侯预先选立的储君不换人)
        if (actor != null && method != LeaderSelectMethod.Harem && regime?.type != RegimeType.Transmigrant &&
            !IsPreferredOfficeCandidate(actor, pKingdom) &&
            actor != HolyRomanSystem.KingOfRomans(pKingdom.GetEmpire()))
            actor = CityPopulationSystem.SpawnForOffice(office, pKingdom, preferredOnly: true) ?? actor;

        // 无小人模式：没有合适人选就从人口里生成一人补上(后宫除外)
        if (actor == null && method != LeaderSelectMethod.Harem)
            actor = CityPopulationSystem.SpawnForOffice(office, pKingdom);

        if (actor != null)
        {
            // 选帝制：选帝侯推举的皇帝即位(含带着家族领国入主中央)不按普通即位扣正统，见 Empire.NewEmperor
            bool election = method == LeaderSelectMethod.Vote && office == pKingdom.GetOffice() && pKingdom.IsEmpire() &&
                            HolyRomanSystem.IsElective(pKingdom.GetEmpire());
            if (election) HolyRomanSystem.Electing = true;
            try
            {
                office.SetActor(actor);
            }
            finally
            {
                if (election) HolyRomanSystem.Electing = false;
            }
        }
    }
#endif

    // 本国够格当君主的男子里声望最高的一位
#if EC_PENDING
    private static Person MaleRuler(OfficeObject office, Realm kingdom)
    {
        Person best = null;
        if (kingdom?.units == null) return null;
        foreach (Person unit in kingdom.units)
        {
            if (unit == null || unit.IsGone() || !unit.Alive || !unit.isSexMale() || !unit.IsAdult() || unit.IsRuler() ||
                !unit.isUnitFitToRule() || !unit.CanServeOffice(kingdom) || unit.IsSkeleton() || !office.Accepts(unit)) continue;
            if (best == null || unit.renown > best.renown) best = unit;
        }
        return best;
    }

    public static Person TryGetHarem(OfficeObject pOffice, Realm pKingdom)
    {
        if (pOffice.meta_object.IsGone()) return null;
        if (pKingdom.Ruler.IsGone()) return null;
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (empire != null)
            {
                var emperor = empire.Emperor;
                var concubines = emperor.GetPersonalIdentity().concubines;
                // 在世的妃嫔(族谱，含虚拟族人)：以前直接取 _actor，虚拟妃嫔是空引用；这份名单也只用来排除，
                // 后宫官职空出来总是另纳新人，已有的妃嫔从不补位
                List<PersonalClanIdentity> livingConsorts = concubines
                    .Select(pValueTuple => SpecificClanManager.getPerson(pValueTuple.identity))
                    .Where(a => a != null && a.is_alive).ToList();
                var lives = livingConsorts.Where(KinRoster.OnMap).Select(a => a._actor)
                    .Where(a => a.CanServeOffice(pKingdom) && !a.IsSkeleton()).ToList();
                if (pOffice.officeType != 13)
                {
                    // 先从已有、没在任的妃嫔里补；地图上没有就落成一位在本帝国的(之后作妃嫔留在地图上，见 PersonRef.Keeps)
                    Person consort = lives.FirstOrDefault(a => !a.IsOnOffice()) ??
                                    KinRoster.RealizeFirst(livingConsorts.Where(a => KinRoster.Virtual(a) &&
                                            KinRoster.KingdomOf(a)?.GetEmpire() == empire),
                                        a => a.CanServeOffice(pKingdom) && !a.IsSkeleton() && !a.IsOnOffice(), 1);
                    if (consort != null)
                    {
                        if (consort.City != emperor.City && emperor.City != null) consort.joinCity(emperor.City);
                        return consort;
                    }
                    var lover = empire.getUnits().ToList().Find(a =>
                        a.CanServeOffice(pKingdom) && a.isSexFemale() && a.IsAdult() && a.age <= 25 &&
                        (a.Clan == null || a.Clan != emperor.Clan) &&
                        !a.hasLover() && !lives.Contains(a) && !KinshipRules.AreCloseKin(emperor, a));
                    if (lover != null)
                    {
                        lover.lover = emperor;
                        emperor.GetPersonalIdentity().setLover(lover, isCus:true);
                        lover.joinCity(emperor.City);
                        return lover;
                    }
                }
                else
                {
                    if (emperor.hasLover() && emperor.lover.CanServeOffice(pKingdom))
                    {
                        return emperor.lover;
                    }

                    var lover = empire.getUnits().ToList().Find(a =>
                        a.CanServeOffice(pKingdom) && a.isSexFemale() && a.IsAdult() && a.age <= 25 &&
                        (a.Clan == null || a.Clan != emperor.Clan) &&
                        !a.hasLover() && !a.IsSkeleton() && !KinshipRules.AreCloseKin(emperor, a));
                    if (lover != null)
                    {
                        lover.lover = emperor;
                        emperor.setLover(lover);
                        emperor.GetPersonalIdentity().setLover(lover, isCus:false);
                        lover.joinCity(emperor.City);
                        return lover;
                    }
                }
            }
        }

        return null;
    }
    private static Person TryGetFeudalElectiveRuler(OfficeObject office, Realm pKingdom)
    {
        if (office == null || pKingdom == null || !pKingdom.IsEmpire() ||
            pKingdom.GetRegime()?.type is not (RegimeType.Feudalism or RegimeType.ClassicalRepublic) ||
            office != pKingdom.GetOffice())
        {
            return null;
        }
        // 选立的储君直接即位，不再另选
        Person designated = HolyRomanSystem.KingOfRomans(pKingdom.GetEmpire());
        if (designated != null) return designated;
        return RankElectiveCandidates(pKingdom).Select(item => item.candidate).FirstOrDefault();
    }
#endif

    // 选举制首领(封建选帝、古典共和元老院)的推举次序：同派系的选帝侯/元老票数 → 偏好人选 → 威望 → 兵力。
    // 候选人是帝国各成员国(不含中央)的君主。帝国窗口的元老院示意图也按这个次序画
#if EC_PENDING
    public static List<(Person candidate, int votes)> RankElectiveCandidates(Realm pKingdom)
    {
        Empire empire = pKingdom?.GetEmpire();
        if (empire == null) return new List<(Person, int)>();
        List<Person> electors = empire.GetCabinetMembers()
            .Where(elector => elector != null && !elector.IsGone())
            .ToList();

        // 帝国自由城市的市长、主教领的主教不是诸侯，不当选皇帝(以前自由城市的市长当选，整座城成了帝国中央，见 HolyRomanSystem.Members)
        return empire.kingdoms_list
            .Where(kingdom => kingdom != null && !kingdom.IsGone() && kingdom != pKingdom && kingdom.HasRuler() &&
                              HolyRomanSystem.Estate(kingdom) == HolyRomanSystem.EstateNone)
            .Select(kingdom => kingdom.Ruler)
            .Where(candidate => candidate != null && !candidate.IsGone() && candidate.IsAdult() &&
                                candidate.isUnitFitToRule() && candidate.CanServeOffice(pKingdom) &&
                                !candidate.IsSkeleton())
            .Select(candidate => (candidate, votes: electors.Count(elector => elector.GetFaction() == candidate.GetFaction())))
            .OrderByDescending(item => item.votes)
            .ThenByDescending(item => IsPreferredOfficeCandidate(item.candidate, pKingdom))
            .ThenByDescending(item => item.candidate.renown)
            .ThenByDescending(item => item.candidate.kingdom?.countTotalWarriors() ?? 0)
            .ToList();
    }
#endif

    // 没有元老院的古典共和：偏好人选优先，其次兵力最强的成员国君主(同 TryGetStrongerLeader)
#if EC_PENDING
    public static List<Person> RankStrongestCandidates(Realm pKingdom)
    {
        Empire empire = pKingdom?.GetEmpire();
        if (empire == null) return new List<Person>();
        return empire.kingdoms_list
            .Where(kingdom => kingdom != null && !kingdom.IsGone() && kingdom.HasRuler() &&
                              kingdom.Ruler.CanServeOffice(pKingdom))
            .OrderByDescending(kingdom => IsPreferredOfficeCandidate(kingdom.Ruler, pKingdom))
            .ThenByDescending(kingdom => kingdom.countTotalWarriors())
            .Select(kingdom => kingdom.king).ToList();
    }

    private static Person TryGetStrongerLeader(OfficeObject pOffice, Realm pKingdom)
    {
        if (pOffice.meta_object.IsGone()) return null;
        if (pKingdom.IsEmpire()&&pOffice.meta_object.meta_type == MetaType.Kingdom)
        {
            Realm kingdom =  null;
            int currentWarriors = 0;
            Empire empire = pKingdom.GetEmpire();
            foreach (var k in empire.kingdoms_list)
            {
                if (!k.HasRuler() || !k.Ruler.CanServeOffice(pKingdom)) continue;
                if (k.countTotalWarriors() >= currentWarriors)
                {
                    kingdom = k;
                    currentWarriors = k.countTotalWarriors();
                }
            }
            Person preferred = empire.kingdoms_list
                .Where(k => k != null && !k.IsGone() && k.HasRuler() && k.Ruler.CanServeOffice(pKingdom) &&
                            IsPreferredOfficeCandidate(k.Ruler, pKingdom))
                .OrderByDescending(k => k.countTotalWarriors())
                .Select(k => k.king).FirstOrDefault();
            return preferred ?? kingdom?.Ruler;
        }
        return null;
    }
#endif

    // 已经在别处主政的人(别城城主、别国君主)不调来补地方主官：调走了原处又空缺，下一轮又按同样的分数被调回，
    // 城主在两座城之间来回跳(无小人模式补位出来的城主不算在任官员，以前挡不住)。中央官职不挪人，不受此限
    private static bool LeadsElsewhere(Person unit, OfficeObject office)
    {
        if (unit == null || office == null || !office.is_local) return false;
        object target = office.meta_object;
        if (unit.IsRuler()) return !ReferenceEquals(target, unit.Realm);
        return unit.IsCityLeader() && unit.City != null && unit.City.Leader == unit && !ReferenceEquals(target, unit.City);
    }

#if EC_PENDING
    private static Person TryGetExamOfficer(OfficeObject pOffice, Realm  pKingdom)
    {
        IEnumerable<Person> targetPool;
        if (pOffice.meta_object.IsGone()) return null;
        using ListPool<Person> pool = new ListPool<Person>();
        if (pOffice.select_from_local)
        {
            targetPool = pOffice.meta_object.meta_type == MetaType.City
                ? ((City)pOffice.meta_object).units
                : ((Realm)pOffice.meta_object).units;
        }
        else
        {
            Realm kingdom = pOffice.meta_object.meta_type == MetaType.City
                ? ((City)pOffice.meta_object).Realm
                : (Realm)pOffice.meta_object;
            if (kingdom.IsInEmpire())
            {
                Empire empire = kingdom.GetEmpire();
                targetPool = CityPopulationSystem.AbstractPopulationEnabled ? OfficeCandidateRoster.Empire(empire.kingdoms_list) : empire.getUnits().ToList();
            }
            else
            {
                targetPool = pOffice.meta_object.meta_type == MetaType.City
                    ? ((City)pOffice.meta_object).units
                    : ((Realm)pOffice.meta_object).units;
            }
        }

        foreach (Person unit in targetPool)
        {
            if (LeadsElsewhere(unit, pOffice)) continue;
            if (unit.isUnitFitToRule() && unit.IsAdult() && unit.CanServeOffice(pKingdom) && !unit.IsEmperor() && !unit.IsOnOffice() && unit.HasClan()&& (!unit.IsRuler()||(unit.IsRuler()&&unit.Realm.GetRegime().GetLeaderSelectMethod()!=LeaderSelectMethod.Succession)))
            {
                if (unit.IsSkeleton()) continue;
                if (!pOffice.Accepts(unit)) continue;
                if (unit.HasOfficeIdentity()&&unit.isActor()&&unit.hasCulture())
                {
                    var flag1 = false;
                    var flag2 = false;
                    OfficeIdentity identity = unit.GetIdentity();
                    if (identity.honoraryOfficial <= pOffice.honorary)
                    {
                        flag1 = true;
                    }
                    if (CityPopulationSystem.AbstractPopulationEnabled ? pOffice.require_traits.Any(t => unit.hasTrait(t)) :
                        pOffice.require_traits.FindAll(t => unit.hasTrait(t)).Any())
                    {
                        flag2 = true;
                    }

                    if (flag1 || flag2)
                    {
                        pool.Add(unit);
                    }
                }
            }
        }
        return SelectBestCandidate(pool, pKingdom);
    }
    private static Person TryGetProfessionOfficer(Realm pKingdom, OfficeObject office = null)
    {
        if (pKingdom == null || pKingdom.IsGone() || pKingdom.units == null)
        {
            return null;
        }

        Person actor = null;
        int num = 0;
        Person fallback = null;
        int fallbackScore = 0;
        foreach (Person unit in pKingdom.units)
        {
            if (unit == null || unit.IsGone())
            {
                continue;
            }

            if (unit.IsRuler() || unit.IsCityLeader() || unit.IsOnOffice() || !unit.IsAdult() || !unit.CanServeOffice(pKingdom) || unit.IsSkeleton())
            {
                continue;
            }
            if (office != null && !office.Accepts(unit)) continue;
            int num2 = 1;
            if (unit.is_profession_citizen)
            {
                if (unit.isFavorite())
                {
                    num2 += 2;
                }
                int num3 = ActorTool.attributeDice(unit, num2);
                if (IsPreferredOfficeCandidate(unit, pKingdom))
                {
                    if (actor == null || num3 > num)
                    {
                        actor = unit;
                        num = num3;
                    }
                }
                else if (fallback == null || num3 > fallbackScore)
                {
                    fallback = unit;
                    fallbackScore = num3;
                }
            }
        }
        return actor ?? fallback;
    }
    private static Person TryGetClanOfficer(Realm pKingdom, OfficeObject office = null)
    {
        Clan clan = null;
        if (pKingdom.Record.royal_clan_id.hasValue())
        {
            clan = EmpireWorld.Current.ClanByKey(pKingdom.Record.royal_clan_id);
        }
        using ListPool<Person> listPool = new ListPool<Person>();
        using ListPool<Person> listPool2 = new ListPool<Person>();
        foreach (City city in pKingdom.Cities)
        {
            foreach (Person unit in city.units)
            {
                if (unit.isUnitFitToRule() && unit.IsAdult() && unit.CanServeOffice(pKingdom) && !unit.IsRuler() && !unit.IsCityLeader() && unit.HasClan()&&!unit.IsOnOffice() && !unit.IsSkeleton() &&
                    (office == null || office.Accepts(unit)))
                {
                    if (clan != null && unit.Clan == clan)
                    {
                        listPool.Add(unit);
                    }
                    else
                    {
                        listPool2.Add(unit);
                    }
                }
            }
        }
        using ListPool<Person> preferredRoyal = new ListPool<Person>();
        using ListPool<Person> preferredOther = new ListPool<Person>();
        foreach (Person unit in listPool)
            if (IsPreferredOfficeCandidate(unit, pKingdom)) preferredRoyal.Add(unit);
        foreach (Person unit in listPool2)
            if (IsPreferredOfficeCandidate(unit, pKingdom)) preferredOther.Add(unit);

        return SelectBestCandidate(preferredRoyal, pKingdom, alreadyPreferred: true) ??
               SelectBestCandidate(preferredOther, pKingdom, alreadyPreferred: true) ??
               SelectBestCandidate(listPool, pKingdom) ??
               SelectBestCandidate(listPool2, pKingdom);
    }

    private static Person SelectBestCandidate(ListPool<Person> pool, Realm kingdom, bool alreadyPreferred = false)
    {
        if (!pool.Any()) return null;
        using ListPool<Person> preferred = new ListPool<Person>();
        bool ready = alreadyPreferred && CityPopulationSystem.AbstractPopulationEnabled;
        if (!ready)
            foreach (Person unit in pool)
                if (IsPreferredOfficeCandidate(unit, kingdom)) preferred.Add(unit);
        ListPool<Person> candidates = ready ? pool : preferred.Any() ? preferred : pool;
        // 族群等第(见 EthnicRankSystem)：帝国核心的缺按配额先尽上等——四等人给国族色目，满汉复职的满缺给国族
        using ListPool<Person> upper = new ListPool<Person>();
        if (EthnicRankSystem.FavorsUpper(kingdom))
        {
            foreach (Person unit in candidates)
                if (EthnicRankSystem.IsUpper(kingdom, unit)) upper.Add(unit);
            if (upper.Any()) candidates = upper;
        }
        if (kingdom.hasCulture())
            return ListSorters.getUnitSortedByAgeAndTraits(candidates, kingdom.culture);
        candidates.Sort(ListSorters.sortUnitByAgeOldFirst);
        return candidates.ElementAt(0);
    }
#endif
}
}
