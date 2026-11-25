// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PersonalUnionService.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 共主联盟(西方古典共和城邦 + 西方封建制)：一个人可以同时是好几个王国的君主。
//   · 同一君主名下、都不在帝国里的王国自动结成原版联盟，以盟主国的名字命名(帝国和联盟不兼容，
//     帝国里的王国只共用君主、不结盟)；
//   · 城邦：分割继承法解锁前，只有影响力达到200的子嗣能续任共主，否则各城邦另立本地君主；
//     解锁后，其余入盟城邦跟随盟主城邦选出的新君主；
//   · 封建：共主去世后按长幼每人一国(不分男女)，长子女得最主要的王国，王国比子女多时多出的归长子女；
//   · 封建王国的继承人本就是别国君主时，不再把两国合并，而是由他兼领(共主)；
//   · 这两种政体下都不能用"统合治下王国"决议把几个王国并成一个。
public static class PersonalUnionService
{
#if EC_PENDING
    public static bool IsUnionRegime(Realm kingdom) =>
        kingdom != null && !kingdom.IsGone() &&
        kingdom.GetRegime()?.type is RegimeType.Feudalism or RegimeType.ClassicalRepublic;

    private static bool IsFeudal(Realm kingdom) => kingdom?.GetRegime()?.type == RegimeType.Feudalism;

    public static bool AllowsFemaleSuccession(Realm kingdom) => IsFeudal(kingdom);
#endif

    public static List<Realm> GetRealms(Person ruler) =>
        ruler == null
            ? new List<Realm>()
            : EmpireWorld.Current.LiveRealms.Where(kingdom => kingdom != null && !kingdom.IsGone() && kingdom.Ruler == ruler)
                .ToList();

#if EC_PENDING
    public static bool IsAtFeudalRealmLimit(Person ruler) => ruler != null &&
        IsFeudal(ruler.Realm) &&
        InstitutionSystem.IsEnacted(CultureService.GetInstitutionCulture(ruler.Realm), "western_feudalization") &&
        GetRealms(ruler).Count(IsFeudal) >= ModClass.FEUDAL_UNION_REALM_LIMIT;

    public static bool CrownLocalVassal(Realm realm, Realm overlord)
    {
        Person local = (realm?.units ?? new List<Person>()).ToList()
            .Where(actor => actor != null && !actor.IsGone() && actor.Alive && actor.isAdult() &&
                            !actor.isKing() && actor.isUnitFitToRule())
            .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
        // 无小人模式：藩国里没有合适的实体，从其都城人口中生成一人
        if (local == null && realm?.Capital != null) local = CityPopulationSystem.SpawnOfficial(realm.Capital, realm);
        if (local == null || !FeudalVassalService.CanBind(overlord, realm)) return false;
        GraceEdictService.Crown(realm, local);
        return realm.Ruler == local && FeudalVassalService.Bind(overlord, realm);
    }
#endif

    // 共主兼领：直接 setKing，不走官职任命(任命会把他迁到对方都城，离开自己的国家)
    public static void CrownInUnion(Realm kingdom, Person ruler)
    {
        if (kingdom == null || kingdom.IsGone() || ruler == null || ruler.IsGone()) return;
        if (kingdom.Ruler == ruler) return;
        if (kingdom.HasRuler()) kingdom.RemoveRuler();
        kingdom.SetRuler(ruler);
    }

    // 由 KingdomPatch 的 setKing 补丁调用：某人名下有两个以上不在帝国里的共主政体王国时，保证它们同盟
#if EC_PENDING
    public static void OnKingCrowned(Realm kingdom, Person king)
    {
        if (!IsUnionRegime(kingdom) || king == null || king.IsGone()) return;
        if (kingdom.hasAlliance())
        {
            Alliance existing = kingdom.getAlliance();
            if (kingdom.GetOrCreate().union_alliance_leader_kingdom_id < 0)
            {
                foreach (Realm member in existing.kingdoms_hashset.ToList())
                {
                    if (member == null || member == kingdom ||
                        member.GetOrCreate().union_alliance_leader_kingdom_id != kingdom.Key || member.Ruler == king ||
                        member.GetOrCreate().union_leader_kingdom_id == kingdom.Key)
                        continue;
                    member.GetOrCreate().union_alliance_leader_kingdom_id = -1L;
                    existing.leave(member);
                }
            }
            else
            {
                Realm leader = EmpireWorld.Current.RealmByKey(kingdom.GetOrCreate().union_alliance_leader_kingdom_id);
                if (leader?.Ruler != king)
                {
                    kingdom.GetOrCreate().union_alliance_leader_kingdom_id = -1L;
                    existing.leave(kingdom);
                }
            }
        }
        List<Realm> realms = GetRealms(king)
            .Where(realm => IsUnionRegime(realm) && !realm.IsEmpire() && !realm.IsInEmpire()).ToList();
        if (realms.Count < 2) return;
        Realm primary = realms.Contains(king.Realm) ? king.Realm : realms[0];
        foreach (Realm realm in realms.Where(realm => realm != primary)) JoinAlliance(primary, realm);
        NameAllianceAfterLeader(primary);
    }
#endif

    // 由 ActorPatch.Die 前缀在君主去世时调用(此时还能查到他名下的王国)
#if EC_PENDING
    public static void OnRulerDying(Person ruler)
    {
        List<Realm> realms = GetRealms(ruler);
        if (realms.Count < 2) return;
        Realm home = realms.Contains(ruler.Realm) ? ruler.Realm : null;

        List<Realm> cityRealms = realms.Where(CityStateService.IsCityState).ToList();
        Realm cityHome = CityStateService.IsCityState(home) ? home : cityRealms.OrderBy(realm => realm.Key).FirstOrDefault();
        if (cityHome != null && cityRealms.Count >= 2)
        {
            bool partitionUnlocked = SuccessionLawSystem.IsSuccessionLawUnlocked(cityHome,
                SuccessionLawType.分割继承法);
            Person qualifiedChild = null;
            // 续任共主的影响力门槛 500 → 200：以前子嗣多半够不着，城邦共主一代就散(用户 2026-10-21 选"减少碎片")
            if (!partitionUnlocked && ruler.GetPersonalIdentity() is PersonalClanIdentity cityIdentity)
                qualifiedChild = VirtualGenealogySystem.RealizeFirst(SpecificClanManager.getChildren(cityIdentity)
                        .Select(item => item.Item2)
                        .Where(person => person != null && person.is_alive)
                        .OrderBy(person => person.rank),
                    child => !child.IsGone() && child.Alive && child.IsFitToRule() &&
                             (child.Record?.Renown ?? 0) >= 200);

            if (!partitionUnlocked && qualifiedChild == null)
                DissolveCityStateUnion(cityRealms);
            else
            {
                if (qualifiedChild != null)
                    cityHome.GetOrCreate().union_city_state_heir_id = qualifiedChild.Key;
                foreach (Realm realm in cityRealms.Where(realm => realm != cityHome))
                    realm.GetOrCreate().union_leader_kingdom_id = cityHome.Key;
            }
        }

        // 封建：施行分割继承法的，按长幼每人一国，不分男女；主要王国(本国，其次城多的)排在前面。
        // 其余继承法由继承人一并继承，各国按各自的继承顺序选出同一人，共主联盟延续(见 TryHandleVacancy、CrownInUnion)
        List<Realm> feudalRealms = realms.Where(IsFeudal)
            .OrderByDescending(realm => realm == home)
            .ThenByDescending(realm => realm.Cities?.Count ?? 0).ThenBy(realm => realm.Key)
            .ToList();
        if (feudalRealms.Count < 2 || feudalRealms[0].GetSuccessionLaw() != SuccessionLawType.分割继承法) return;
        PersonalClanIdentity identity = ruler.GetPersonalIdentity();
        if (identity == null) return;
        List<Person> children = VirtualGenealogySystem.RealizeAll(SpecificClanManager.getChildren(identity)
                .Select(item => item.Item2)
                .Where(person => person != null && person.is_alive)
                .OrderBy(person => person.rank),
            actor => !actor.IsGone() && actor.Alive && actor.IsFitToRule());
        if (children.Count == 0) return;
        for (int i = 0; i < feudalRealms.Count; i++)
            feudalRealms[i].GetOrCreate().union_partition_heir_id = (i < children.Count ? children[i] : children[0]).Key;
    }
#endif

    // 由 EmpireCraftKingdomBehCheckKing 在王国没有在世国王时调用；返回 true 表示已处理(或需等待)。
    // 注意原版君主死亡时只清空他本国的国王，他兼领的其他王国里仍挂着这位已故君主，所以按"在世"判断。
#if EC_PENDING
    public static bool TryHandleVacancy(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone()) return false;
        if (kingdom.HasRuler() && kingdom.Ruler != null && !kingdom.Ruler.IsGone() && kingdom.Ruler.Alive) return false;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();

        if (data.union_city_state_local_succession)
        {
            if (CityStateService.TryCrownLocalRuler(kingdom))
                data.union_city_state_local_succession = false;
            return true;
        }

        if (data.union_city_state_heir_id >= 0)
        {
            Person heir = EmpireWorld.Current.PersonByKey(data.union_city_state_heir_id);
            data.union_city_state_heir_id = -1L;
            if (heir != null && !heir.IsGone() && heir.Alive && heir.IsFitToRule())
            {
                if (heir.IsRuler()) CrownInUnion(kingdom, heir);
                else GraceEdictService.Crown(kingdom, heir);
                if (kingdom.Ruler == heir) return true;
            }
            DissolveCityStateUnion(EmpireWorld.Current.LiveRealms.Where(realm => realm == kingdom ||
                realm != null && !realm.IsGone() && realm.GetOrCreate().union_leader_kingdom_id == kingdom.Key));
            if (CityStateService.TryCrownLocalRuler(kingdom))
                data.union_city_state_local_succession = false;
            return true;
        }

        if (data.union_partition_heir_id >= 0)
        {
            Person heir = EmpireWorld.Current.PersonByKey(data.union_partition_heir_id);
            data.union_partition_heir_id = -1L;
            if (heir != null && !heir.IsGone() && heir.Alive)
            {
                if (IsFeudal(kingdom) && heir.IsRuler() && IsAtFeudalRealmLimit(heir))
                {
                    if (CrownLocalVassal(kingdom, heir.Realm)) return true;
                    data.union_partition_heir_id = heir.Key;
                    return true;
                }
                if (heir.IsRuler()) CrownInUnion(kingdom, heir);
                else GraceEdictService.Crown(kingdom, heir);
                return kingdom.Ruler == heir;
            }
        }

        if (data.union_leader_kingdom_id >= 0)
        {
            Realm leader = EmpireWorld.Current.RealmByKey(data.union_leader_kingdom_id);
            if (leader == null || leader.IsGone() || !CityStateService.IsCityState(leader) ||
                !CityStateService.IsCityState(kingdom))
            {
                data.union_leader_kingdom_id = -1L;
                return false;
            }
            // 盟主城邦还没选出新君主：先等着，不自行选举
            if (!leader.HasRuler() || leader.Ruler == null || !leader.Ruler.Alive) return true;
            data.union_leader_kingdom_id = -1L;
            CrownInUnion(kingdom, leader.Ruler);
            kingdom.updateColor(leader.getColor());
            return true;
        }
        return false;
    }

    private static void DissolveCityStateUnion(IEnumerable<Realm> realms)
    {
        List<Realm> members = realms.Where(CityStateService.IsCityState).Distinct().ToList();
        foreach (Realm realm in members)
        {
            KingdomExtension.KingdomExtraData data = realm.GetOrCreate();
            data.union_leader_kingdom_id = -1L;
            data.union_city_state_heir_id = -1L;
            data.union_city_state_local_succession = true;
            data.union_alliance_leader_kingdom_id = -1L;
        }
        foreach (Realm realm in members)
        {
            if (realm.hasAlliance()) realm.getAlliance().leave(realm);
            realm.generateColor();
        }
    }
#endif

    // 西方封建制"一法理一国"：索取到的法理不并进获胜国，而是在法理首府另立一国(该法理原属战败国的城市
    // 一并划入)，由获胜国君主兼领。首府已在获胜国手里的法理无法另立，返回在 remaining 里交给原逻辑处理。
#if EC_PENDING
    public static List<KingdomTitle> InheritTitlesAsUnion(Realm successor, Realm defeated,
        IEnumerable<long> titleIds, out List<long> remaining)
    {
        var inherited = new List<KingdomTitle>();
        remaining = new List<long>();
        Person ruler = successor?.Ruler;
        // 施行中央集权(直辖治理)后能容纳多个法理与城市：索取的法理照常并入获胜国
        if (!IsFeudal(successor) || FeudalVassalService.HasDirectAdministration(successor) || ruler == null ||
            ruler.IsGone() || titleIds == null)
        {
            remaining.AddRange(titleIds ?? Enumerable.Empty<long>());
            return inherited;
        }
        foreach (long titleId in titleIds.Distinct().ToList())
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.Get(titleId);
            City seat = title?.title_capital;
            if (title == null || title.IsGone() || seat == null || seat.IsGone() || seat.Realm == null ||
                seat.Realm == successor)
            {
                remaining.Add(titleId);
                continue;
            }
            Realm holder = seat.Realm;
            Person founder = (seat.units ?? new List<Person>()).ToList()
                .Where(actor => actor != null && actor != holder.Ruler && !actor.IsGone() && actor.Alive &&
                                actor.isAdult() && !actor.isKing() && actor.isUnitFitToRule())
                .OrderByDescending(actor => actor.data?.renown ?? 0).FirstOrDefault();
            if (founder == null)
            {
                remaining.Add(titleId);
                continue;
            }
            if (holder.Ruler?.City == seat) holder.kingFledCity();
            // 索取的法理另立一国由胜者兼领(战果)：不许自立的闸放行(见 SecessionGate)
            Realm realm = SecessionGate.Sanctioned(() => seat.makeOwnKingdom(founder));
            if (realm == null)
            {
                remaining.Add(titleId);
                continue;
            }
            foreach (City city in title.getCities().ToList())
                if (city != null && !city.IsGone() && city != seat && city.Realm == holder)
                    city.JoinRealm(realm);
            realm.SetRegimeType(RegimeType.Feudalism);
            realm.LoadRegime();
            realm.InheritRealmTitles(holder, new[] { titleId });
            if (IsAtFeudalRealmLimit(ruler))
            {
                FeudalVassalService.Bind(successor, realm);
                inherited.Add(title);
                continue;
            }
            CrownInUnion(realm, ruler);
            inherited.Add(title);
        }
        return inherited;
    }

    public static void JoinAlliance(Realm leader, Realm member)
    {
        if (leader == null || member == null || leader == member ||
            leader.IsInEmpire() || member.IsInEmpire() ||
            FeudalVassalService.GetOverlord(member) == leader ||
            !FeudalVassalService.CanJoinAlliance(member, leader.hasAlliance() ? leader.getAlliance() : null)) return;
        Alliance leaderAlliance = leader.hasAlliance() ? leader.getAlliance() : null;
        if (member.hasAlliance())
        {
            if (member.getAlliance() == leaderAlliance)
            {
                if (leader.Ruler != null && leader.Ruler == member.Ruler)
                    member.GetOrCreate().union_alliance_leader_kingdom_id = leader.Key;
                return;
            }
            member.getAlliance().leave(member);
        }
        if (leaderAlliance != null) leaderAlliance.join(member, true, true);
        else leaderAlliance = EmpireWorld.Current.alliances.newAlliance(leader, member);
        ModAllianceService.Mark(leaderAlliance);
        if (leader.Ruler != null && leader.Ruler == member.Ruler)
            member.GetOrCreate().union_alliance_leader_kingdom_id = leader.Key;
    }
#endif

    // 同盟直接以盟主国的名字命名(城邦即主城名)，不带"城邦"等后缀
#if EC_PENDING
    public static void NameAllianceAfterLeader(Realm leader)
    {
        Alliance alliance = leader != null && leader.hasAlliance() ? leader.getAlliance() : null;
        if (alliance == null) return;
        string name = CityStateService.IsCityState(leader) && leader.Capital != null && !leader.Capital.IsGone()
            ? leader.Capital.GetCityName()
            : leader.Record?.Name;
        if (!string.IsNullOrWhiteSpace(name)) alliance.setName(name);
    }
#endif
}
}
