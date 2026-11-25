// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ImperialLegitimacyChallengeService.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public static class ImperialLegitimacyChallengeService
{
    private const string AnlePeerageKey = "tang_honorary_anle_gong";

#if EC_PENDING
    public static bool TryFindTarget(Realm kingdom, out Empire target)
    {
        target = null;
        if (kingdom == null || kingdom.IsGone() || !kingdom.HasRuler() || kingdom.Ruler == null ||
            kingdom.Ruler.IsGone() || kingdom.IsInEmpire() || kingdom.IsEmpire() || kingdom.HasEnemies() ||
            kingdom.GetMoney() < 0 || AncientWarfareCompatibility.BlocksEmpireFormation(kingdom) ||
            EmpireCraftWorldLawLibrary.empirecraft_law_ban_empire.isEnabled()) return false;
        // 现代政体没有僭越称帝：政权只能组建政府(见 ModernStateFormationSystem)
        if (kingdom.GetRegime()?.type == RegimeType.Modern) return false;
        // 别国的从属(附庸、朝贡国、属地、傀儡)不僭越称帝(见 EmpireFormationService.IsSubject)
        if (EmpireFormationService.IsSubject(kingdom)) return false;
        // 战国的周诸侯仍奉王室，只称王不称帝；王畿只剩几城，按国力比较人人都能压倒它(王室亡了才各自独立)
        if (ZhouEraSystem.IsDetachedVassal(kingdom)) return false;
        // 原始政体的部落不僭越称帝(用户 2026-10-26 选"部落不再是帝国")：只能打赢收部结成部落联盟(见 TribalFissionService.Incorporate)。
        // 以前每个部落本来就是帝国(上面 IsEmpire 挡下)，如今是国，同文化出了部落联盟就会来争正统
        if (TribalFissionService.IsTribal(kingdom)) return false;
        // 周制下一个文化只有一个正统核心：同文化之国不能另立帝国与周室争正统，只能受封为诸侯
        if (ZhouEraSystem.BlocksRivalEmpire(kingdom)) return false;
        // 万世一系：已有天皇在位，别家不另立天皇争正统
        if (ConstitutionalSuccessionSystem.BlocksRivalThrone(kingdom)) return false;
        if (!kingdom.HasMainTitle() && !kingdom.GetControlledTitle().Any()) return false;

        string culture = CultureService.GetRealmCulture(kingdom);
        if (!CultureService.IsValidCulture(culture) || ModClass.EMPIRE_MANAGER == null) return false;
        List<Empire> sameCultureEmpires = ModClass.EMPIRE_MANAGER
            .Where(empire => IsActiveEmpire(empire) &&
                             string.Equals(CultureService.GetEmpireDefaultCulture(empire), culture,
                                 StringComparison.Ordinal))
            .ToList();
        // 本文化已有的帝国到了上限(模组设置，默认两个)就不能再僭越称帝
        if (sameCultureEmpires.Count < 1 || sameCultureEmpires.Count >= Math.Max(1, ModClass.SAME_CULTURE_EMPIRE_LIMIT)) return false;

        // 几个帝国并立时，挑接壤、国力不如自己、没在别的正统之争里的那个里最强的(帝国正在打仗也可以趁机发起)
        double power = kingdom.GetNationalPower();
        foreach (Empire incumbent in sameCultureEmpires.OrderByDescending(empire => empire.GetNationalPower()))
        {
            if (incumbent.data.legitimacy_rivalry_recognized) continue;
            // 够不着的不挑(见 WarReach)：先立了帝国再开不成战，又得把它解散
            if (!WarReach.Reachable(kingdom, incumbent.CoreKingdom)) continue;
            if (!ImperialLegitimacyRules.CanChallenge(power, incumbent.GetNationalPower(), sameCultureEmpires.Count,
                    !kingdom.IsInEmpire(), SharesLandBorder(kingdom, incumbent))) continue;
            target = incumbent;
            return true;
        }
        return false;
    }
#endif

    private static bool SharesLandBorder(Realm challenger, Empire incumbent)
    {
        if (challenger == null || challenger.IsGone() || challenger.Cities == null || incumbent == null) return false;
        foreach (City city in challenger.Cities)
        {
            if (city != null && city.Realm == challenger && SharesLandBorder(city, incumbent)) return true;
        }
        return false;
    }

    private static bool SharesLandBorder(Empire challenger, Empire incumbent)
    {
        if (challenger == null || incumbent == null || challenger == incumbent) return false;
        foreach (City city in EmpireMembershipService.EnumerateCities(challenger))
            if (SharesLandBorder(city, incumbent)) return true;
        return false;
    }

    private static bool SharesLandBorder(City city, Empire incumbent)
    {
        if (city == null || city.IsGone() || city.Zones == null) return false;
        // 读取当前区域归属，避免城市易主后旧邻国缓存仍判定接壤；四向相邻才有共同边界。
        foreach (Cell zone in city.Zones)
        {
            if (zone == null || zone.City != city || zone.Neighbours == null) continue;
            foreach (Cell neighbour in zone.Neighbours)
            {
                City neighbouringCity = neighbour?.City;
                if (neighbouringCity != null && neighbouringCity != city && !neighbouringCity.IsGone() &&
                    EmpireMembershipService.BelongsTo(incumbent, neighbouringCity.Realm))
                    return true;
            }
        }
        return false;
    }

#if EC_PENDING
    public static bool CanChallenge(Person actor)
    {
        return actor != null && !actor.IsGone() && actor.IsRuler() && actor.Realm?.Ruler == actor &&
               actor.plot?.isActive() != true && TryFindTarget(actor.Realm, out _);
    }

    public static bool CanContinueChallenge(Person actor)
    {
        return actor != null && !actor.IsGone() && actor.IsRuler() && actor.Realm?.Ruler == actor &&
               TryFindTarget(actor.Realm, out _);
    }

    public static bool StartChallenge(Person actor)
    {
        Realm kingdom = actor?.Realm;
        if (kingdom == null || kingdom.Ruler != actor || !TryFindTarget(kingdom, out Empire incumbent))
            return false;

        bool contestsIncumbentCore = EmpireFormationService.GetSeatCoreEmpire(kingdom) == incumbent;
        // 挑战称帝以自家王国法理名为号，不拿那个帝国法理名(见 RealmNamingSystem.Founding)
        Empire challenger;
        RealmNamingSystem.BeginChallengeFounding();
        try
        {
            // 冻结法理：不是和对手争同一个帝国法理的，按常规绑(空着的接手，占着才另立)，不把别的帝国法理里的法理另记一份
            challenger = ModClass.EMPIRE_MANAGER.NewEmpire(kingdom, allowCultureRival: true,
                forceNewCore: contestsIncumbentCore || !ModClass.KINGDOM_TITLE_FREEZE);
        }
        finally
        {
            RealmNamingSystem.EndChallengeFounding();
        }
        if (challenger == null) return false;
        MarkRivalry(challenger, incumbent, challengerIsClaimant: true);
        if (contestsIncumbentCore)
        {
            EmpireCore falseCore = EmpireCoreManager.Get(challenger);
            if (falseCore != null) falseCore.false_core_against_empire_id = incumbent.Key;
        }

        War war = DiplomacyHelpers.wars.newWar(challenger.CoreKingdom, incumbent.CoreKingdom,
            WarKinds.Normal);
        if (war == null)
        {
            ClearRivalry(challenger);
            ClearRivalry(incumbent);
            ModClass.EMPIRE_MANAGER.dissolveEmpire(challenger);
            return false;
        }

        war.SetEmpireWarType(EmpireWarType.去帝号, nanoObject: incumbent);
        war.InitializeLegitimacyChallenge(challenger, incumbent);
        foreach (Realm member in challenger.kingdoms_list.ToList())
            if (member != null && member != challenger.CoreKingdom && !member.IsGone()) war.JoinAttackers(member);
        foreach (Realm member in incumbent.kingdoms_list.ToList())
            if (member != null && member != incumbent.CoreKingdom && !member.IsGone()) war.JoinDefenders(member);

        RecordDeclaration(challenger, incumbent, actor, incumbent.Emperor);
        return true;
    }
#endif

    // 僭越称帝：与主法理所在核心的现存帝国并立，互为正统对手(先不开战)；
    // 天命在称帝路线的初始值(CompleteFormation 刚定下)上打折扣，见 EmpireFormationService.GetUsurpationMandate
#if EC_PENDING
    public static void DeclareUsurpation(Empire usurper, Empire incumbent)
    {
        if (!IsActiveEmpire(usurper) || !IsActiveEmpire(incumbent) || usurper == incumbent) return;
        MarkRivalry(usurper, incumbent, challengerIsClaimant: true);
        EmpireCore falseCore = EmpireCoreManager.Get(usurper);
        if (falseCore != null) falseCore.false_core_against_empire_id = incumbent.Key;
        usurper.data.Mandate = EmpireFormationService.GetUsurpationMandate(usurper.data.Mandate);
        string content = string.Format(LM.Get("history_usurpation_declared"),
            usurper.Emperor?.FullName() ?? usurper.GetEmpireFullName(), usurper.GetEmpireFullName(),
            incumbent.GetEmpireFullName());
        usurper.RecordHistory(directContent: content, actorId: usurper.Emperor?.Key ?? -1L,
            kingdomId: usurper.CoreKingdom?.Key ?? -1L);
        incumbent.RecordHistory(directContent: content, actorId: usurper.Emperor?.Key ?? -1L,
            kingdomId: usurper.CoreKingdom?.Key ?? -1L);
        EmpireCraft.Core.TranslateHelper.LogEventMessage(content, usurper.CoreKingdom);
    }
#endif

    // 互为正统对手且领土接壤的两个帝国：较强的一方(双方之间没有战争时)可以发起正统之争。
#if EC_PENDING
    public static bool TryStartRivalryWar(Empire empire)
    {
        if (!IsActiveEmpire(empire) || !empire.data.legitimacy_rivalry_recognized) return false;
        Empire rival = ModClass.EMPIRE_MANAGER?.Get(empire.data.legitimacy_rival_empire_id);
        if (!AreRecognizedRivals(empire, rival)) return false;
        Realm core = empire.CoreKingdom;
        Realm rivalCore = rival.CoreKingdom;
        if (core == null || rivalCore == null || core.IsGone() || rivalCore.IsGone()) return false;
        if (core.IsAtWarWith(rivalCore) || core.GetMoney() < 0) return false;
        if (empire.GetNationalPower() <= rival.GetNationalPower()) return false;
        if (!SharesLandBorder(empire, rival)) return false;
        if (EcRandom.value > RivalryWarChance) return false;

        War war = DiplomacyHelpers.wars.newWar(core, rivalCore, WarKinds.Normal);
        if (war == null) return false;
        war.SetEmpireWarType(EmpireWarType.去帝号, nanoObject: rival);
        war.InitializeLegitimacyChallenge(empire, rival);
        foreach (Realm member in empire.kingdoms_list.ToList())
            if (member != null && member != core && !member.IsGone()) war.JoinAttackers(member);
        foreach (Realm member in rival.kingdoms_list.ToList())
            if (member != null && member != rivalCore && !member.IsGone()) war.JoinDefenders(member);
        RecordDeclaration(empire, rival, empire.Emperor, rival.Emperor);
        return true;
    }
#endif

    private const float RivalryWarChance = 0.1f;

    // 分立的两朝互为正统对手(见 EmpirePartitionSystem)：claimant 是分出来的一朝；war 为真时当即开战
#if EC_PENDING
    public static void DeclareDynasticRivals(Empire claimant, Empire incumbent, bool war)
    {
        if (!IsActiveEmpire(claimant) || !IsActiveEmpire(incumbent) || claimant == incumbent) return;
        MarkRivalry(claimant, incumbent, challengerIsClaimant: true);
        Realm core = claimant.CoreKingdom;
        Realm rivalCore = incumbent.CoreKingdom;
        if (!war || core.IsAtWarWith(rivalCore)) return;
        War started = DiplomacyHelpers.wars.newWar(core, rivalCore, WarKinds.Normal);
        if (started == null) return;
        started.SetEmpireWarType(EmpireWarType.去帝号, nanoObject: incumbent);
        started.InitializeLegitimacyChallenge(claimant, incumbent);
        foreach (Realm member in claimant.kingdoms_list.ToList())
            if (member != null && member != core && !member.IsGone()) started.JoinAttackers(member);
        foreach (Realm member in incumbent.kingdoms_list.ToList())
            if (member != null && member != rivalCore && !member.IsGone()) started.JoinDefenders(member);
    }
#endif

    public static bool AreRecognizedRivals(Empire first, Empire second)
    {
        if (!IsActiveEmpire(first) || !IsActiveEmpire(second) || first == second) return false;
        return first.data.legitimacy_rivalry_recognized && second.data.legitimacy_rivalry_recognized &&
               first.data.legitimacy_rival_empire_id == second.Key &&
               second.data.legitimacy_rival_empire_id == first.Key;
    }

    // 得天下者得正统：伪核心针对的正统帝国已经不在(被别的方式灭掉、解散)，正规核心也没有别的帝国据有，
    // 而僭越者占据了正规核心过半城池时，伪核心销毁、由它接管正规核心(同打赢正统之战)。
    // 以前只能靠打赢"去帝号"之战转正，正统帝国另外亡了就永远是伪法理，占满全图也一样。
    // 每年(ConstitutionalEconomySystem 年度结算)查一次
#if EC_PENDING
    public static bool TryLegitimizeFalseCore(Empire empire)
    {
        if (!IsActiveEmpire(empire)) return false;
        EmpireCore falseCore = EmpireCoreManager.Get(empire);
        if (falseCore == null || falseCore.false_core_against_empire_id <= 0 ||
            EmpireCoreManager.GetLegitimateEmpire(falseCore) != null) return false;
        EmpireCore lawful = FindLawfulCore(empire, falseCore);
        if (lawful == null || EmpireCoreManager.GetEmpires(lawful)
                .Any(other => other != null && other != empire && !other.IsArchived() && !other.IsGone())) return false;
        List<City> cities = EmpireCoreManager.GetCities(lawful);
        if (cities.Count == 0 || cities.Count(city => Holds(empire, city)) * 2 <= cities.Count)
            return false;
        EmpireCoreManager.DestroyEmpireCore(falseCore);
        EmpireCoreManager.RebindEmpire(empire, lawful);
        ClearRivalry(empire);
        empire.recalculate();
        string text = string.Format(LM.Get("history_false_core_legitimized"), empire.GetEmpireFullName(),
            EmpireCoreManager.GetDisplayName(lawful));
        empire.RecordHistory(directContent: text, actorId: empire.Emperor?.Key ?? -1L, kingdomId: empire.CoreKingdom?.Key ?? -1L);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
        return true;
    }
#endif

    // 这座城归本帝国：周制战国时诸侯离开帝国、只记着所属周室，也算(见 ZhouEraSystem.SphereOf)
#if EC_PENDING
    private static bool Holds(Empire empire, City city)
    {
        Realm kingdom = city?.Realm;
        return kingdom != null && (ZhouEraSystem.SphereOf(kingdom) ?? kingdom.GetEmpire()) == empire;
    }
#endif

    // 伪核心对应的正规核心：原正统帝国据有过的那个；其次含本国主法理的；都找不到就取本帝国据有城池最多的正规核心
#if EC_PENDING
    private static EmpireCore FindLawfulCore(Empire empire, EmpireCore falseCore)
    {
        long rivalId = falseCore.false_core_against_empire_id;
        KingdomTitle mainTitle = empire.CoreKingdom?.GetMainTitle();
        List<EmpireCore> lawful = EmpireCoreManager.EmpireCores.Values
            .Where(core => core != null && core != falseCore && core.false_core_against_empire_id <= 0 &&
                           core.warlord_parent_core_id <= 0 && core.titlesRecord is { Count: > 0 }).ToList();
        EmpireCore byRival = lawful.FirstOrDefault(core => core.empire_id == rivalId ||
                                                           core.empire_history_ids?.Contains(rivalId) == true);
        if (byRival != null) return byRival;
        EmpireCore byTitle = mainTitle == null ? null : lawful.FirstOrDefault(core => EmpireCoreManager.ContainsTitle(core, mainTitle));
        return byTitle ?? lawful.OrderByDescending(core => EmpireCoreManager.GetCities(core)
            .Count(city => Holds(empire, city))).FirstOrDefault();
    }
#endif

    public static void ClearRivalry(Empire empire)
    {
        if (empire?.data == null) return;
        // 正统对手关系可以结束，但伪核心的法理性质不因此自动转正：要么打赢正统之战，
        // 要么正统帝国已亡而自己据有正规核心过半(见 TryLegitimizeFalseCore)，或随政权败亡而消失。
        empire.data.legitimacy_rival_empire_id = -1L;
        empire.data.legitimacy_rivalry_recognized = false;
        empire.data.legitimacy_challenger = false;
    }

#if EC_PENDING
    public static void ResolveWar(War war, WarOutcome winner)
    {
        if (war == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        Empire challenger = ModClass.EMPIRE_MANAGER?.Get(data.legitimacy_challenger_empire_id);
        Empire incumbent = ModClass.EMPIRE_MANAGER?.Get(data.legitimacy_defender_empire_id);
        if (winner == WarOutcome.Attackers && challenger != null && !challenger.IsArchived() &&
            (war.IsMainDefenderEliminated() || war.HasLegitimacyOccupationThreshold()))
        {
            ResolveVictory(war, challenger, incumbent);
            return;
        }
        RecordFailure(war, challenger, incumbent);
    }
#endif

    private static void MarkRivalry(Empire challenger, Empire incumbent, bool challengerIsClaimant)
    {
        challenger.data.legitimacy_rival_empire_id = incumbent.Key;
        challenger.data.legitimacy_rivalry_recognized = true;
        challenger.data.legitimacy_challenger = challengerIsClaimant;
        incumbent.data.legitimacy_rival_empire_id = challenger.Key;
        incumbent.data.legitimacy_rivalry_recognized = true;
        incumbent.data.legitimacy_challenger = !challengerIsClaimant;
    }

#if EC_PENDING
    private static void ResolveVictory(War war, Empire challenger, Empire incumbent)
    {
        if (challenger == null || challenger.IsArchived()) return;
        bool challengerWasClaimant = challenger.data.legitimacy_challenger;
        WarExtension.WarExtraData warData = war.GetOrCreate();
        Person challengerEmperor = challenger.Emperor;
        Person formerEmperor = incumbent?.Emperor ?? EmpireWorld.Current?.PersonByKey(warData.legitimacy_defender_emperor_id);
        Realm formerCore = incumbent?.CoreKingdom ??
                             EmpireWorld.Current?.RealmByKey(warData.legitimacy_defender_core_kingdom_id);
        Realm newCore = challenger.CoreKingdom;
        string oldEmpireName = incumbent?.GetEmpireFullName() ?? warData.legitimacy_defender_empire_name ?? "";
        string newEmpireName = challenger.GetEmpireFullName();

        string success = string.Format(LM.Get("history_legitimacy_challenge_victory"),
            challengerEmperor?.FullName() ?? newEmpireName, oldEmpireName, newEmpireName);
        challenger.RecordHistory(directContent: success, actorId: challengerEmperor?.Key ?? -1L,
            kingdomId: newCore?.Key ?? -1L);
        if (incumbent != null)
            incumbent.RecordHistory(directContent: success, actorId: challengerEmperor?.Key ?? -1L,
                kingdomId: newCore?.Key ?? -1L);
        else
            RecordArchivedEmpireHistory(warData.legitimacy_defender_empire_id, success,
                challengerEmperor?.Key ?? -1L, newCore?.Key ?? -1L);
        challengerEmperor?.RecordPersonalHistory(success, "legitimacy_challenge_victory",
            relatedActorId: formerEmperor?.Key ?? -1L);
        formerEmperor?.RecordPersonalHistory(success, "legitimacy_challenge_defeat",
            relatedActorId: challengerEmperor?.Key ?? -1L);

        IEnumerable<Realm> formerMembers = incumbent != null && !incumbent.IsArchived()
            ? incumbent.kingdoms_list
            : (warData.legitimacy_defender_kingdom_ids ?? new List<long>())
                .Select(id => EmpireWorld.Current?.RealmByKey(id));
        foreach (Realm administration in formerMembers
                     .Where(member => member != null && member != formerCore && !member.IsGone()).ToList())
        {
            challenger.join(administration, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
        }

        City retirementCity = null;
        Realm retirementKingdom = formerCore;
        if (formerCore != null && !formerCore.IsGone() && formerCore.Cities?.Count > 0)
        {
            retirementCity = formerCore.Capital != null && !formerCore.Capital.IsGone()
                ? formerCore.Capital
                : formerCore.Cities.FirstOrDefault(city => city != null && !city.IsGone());
            foreach (City city in formerCore.Cities.ToList())
                if (city != null && city != retirementCity && !city.IsGone() && newCore != null)
                    city.JoinRealm(newCore);
            if (retirementCity != null) formerCore.SetCapital(retirementCity);
        }
        else if (formerEmperor != null && !formerEmperor.IsGone())
        {
            retirementCity = (warData.legitimacy_defender_city_ids ?? new List<long>())
                .Select(id => EmpireWorld.Current?.CityByKey(id))
                .FirstOrDefault(city => city != null && !city.IsGone());
            if (retirementCity != null)
            {
                // 失位之君退守一城(帝号之争收场)：不许自立的闸放行(见 SecessionGate)
                retirementKingdom = SecessionGate.Sanctioned(() => retirementCity.makeOwnKingdom(formerEmperor));
                retirementKingdom?.SetCapital(retirementCity);
            }
        }

        EmpireCore inheritedCore = EmpireCoreManager.Get(incumbent) ??
                                   EmpireCoreManager.Get(warData.legitimacy_defender_core_id);
        EmpireCore provisionalCore = EmpireCoreManager.Get(challenger);
        if (challengerWasClaimant)
        {
            // 僭越者胜出时伪核心本身不会转正：销毁伪核心，再让胜利政权接管
            // 已经存在的正规核心。冻结法理不阻止这种所有者更替。
            if (provisionalCore != null && provisionalCore != inheritedCore)
                EmpireCoreManager.DestroyEmpireCore(provisionalCore);
            if (inheritedCore != null) EmpireCoreManager.RebindEmpire(challenger, inheritedCore);
        }
        else if (inheritedCore != null && inheritedCore != provisionalCore)
        {
            // 原帝国平定僭越者：保留自己的核心，僭越者的临时核心随之销毁
            EmpireCoreManager.DestroyEmpireCore(inheritedCore);
        }

        if (incumbent != null && !incumbent.IsArchived()) ModClass.EMPIRE_MANAGER.dissolveEmpire(incumbent);
        if (retirementKingdom != null && !retirementKingdom.IsGone())
            challenger.join(retirementKingdom, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
        challenger.recalculate();
        ClearRivalry(challenger);

        if (formerEmperor != null && !formerEmperor.IsGone())
        {
            challenger.RememberDefeatedEmpireHouse(new DefeatedEmpireHouse
            {
                empire_id = warData.legitimacy_defender_empire_id,
                emperor_id = formerEmperor.Key,
                royal_clan_id = formerEmperor.GetSpecificClan()?.id ?? -1L
            });
            if (ForceGrantAnlePeerage(formerEmperor, challenger))
            {
                string enfeoffed = string.Format(LM.Get("history_deposed_emperor_enfeoffed_anle"),
                    formerEmperor.FullName(), retirementCity?.GetCityName() ?? "");
                challenger.RecordHistory(directContent: enfeoffed, actorId: formerEmperor.Key,
                    kingdomId: retirementKingdom?.Key ?? -1L);
                formerEmperor.RecordPersonalHistory(enfeoffed, "deposed_emperor_enfeoffed_anle",
                    relatedActorId: challengerEmperor?.Key ?? -1L);
            }
        }
        EmpireCraft.Core.TranslateHelper.LogEventMessage(success, challenger.CoreKingdom);
    }

    private static bool ForceGrantAnlePeerage(Person actor, Empire empire)
    {
        if (actor == null || actor.IsGone() || empire == null ||
            empire.CoreKingdom?.GetRegime()?.type == RegimeType.Modern)
            return false;
        actor.CheckSpecificClan(false);
        var actorData = actor.GetOrCreate();
        actorData.honorary_peerage_key = AnlePeerageKey;
        actorData.honorary_peerage_empire_id = empire.Key;
        actor.RemoveEmpire();
        empire.RememberHonoraryPeerageHolder(AnlePeerageKey, actor);
        TranslateHelper.LogHonoraryPeerageGranted(actor, empire, AnlePeerageKey);
        return true;
    }

    private static void RecordDeclaration(Empire challenger, Empire incumbent, Person challengerEmperor,
        Person incumbentEmperor)
    {
        string content = string.Format(LM.Get("history_legitimacy_challenge_declared"),
            challengerEmperor?.FullName() ?? challenger.GetEmpireFullName(), incumbent.GetEmpireFullName());
        challenger.RecordHistory(directContent: content, actorId: challengerEmperor?.Key ?? -1L,
            kingdomId: challenger.CoreKingdom?.Key ?? -1L);
        incumbent.RecordHistory(directContent: content, actorId: challengerEmperor?.Key ?? -1L,
            kingdomId: challenger.CoreKingdom?.Key ?? -1L);
        challengerEmperor?.RecordPersonalHistory(content, "legitimacy_challenge_declared",
            relatedActorId: incumbentEmperor?.Key ?? -1L);
        incumbentEmperor?.RecordPersonalHistory(content, "legitimacy_challenge_defended",
            relatedActorId: challengerEmperor?.Key ?? -1L);
        EmpireCraft.Core.TranslateHelper.LogEventMessage(content, challenger.CoreKingdom);
    }

    private static void RecordFailure(War war, Empire challenger, Empire incumbent)
    {
        if (war == null) return;
        WarExtension.WarExtraData data = war.GetOrCreate();
        Person challengerEmperor = challenger?.Emperor ??
                                   EmpireWorld.Current?.PersonByKey(data.legitimacy_challenger_emperor_id);
        Person incumbentEmperor = incumbent?.Emperor ??
                                  EmpireWorld.Current?.PersonByKey(data.legitimacy_defender_emperor_id);
        string content = string.Format(LM.Get("history_legitimacy_challenge_failed"),
            challenger?.GetEmpireFullName() ?? data.legitimacy_challenger_empire_name ?? "",
            incumbent?.GetEmpireFullName() ?? data.legitimacy_defender_empire_name ?? "");
        if (challenger != null)
            challenger.RecordHistory(directContent: content, actorId: incumbentEmperor?.Key ?? -1L,
                kingdomId: incumbent?.CoreKingdom?.Key ?? data.legitimacy_defender_core_kingdom_id);
        else
            RecordArchivedEmpireHistory(data.legitimacy_challenger_empire_id, content,
                incumbentEmperor?.Key ?? -1L, data.legitimacy_defender_core_kingdom_id);
        if (incumbent != null)
            incumbent.RecordHistory(directContent: content, actorId: challengerEmperor?.Key ?? -1L,
                kingdomId: challenger?.CoreKingdom?.Key ?? data.legitimacy_challenger_core_kingdom_id);
        else
            RecordArchivedEmpireHistory(data.legitimacy_defender_empire_id, content,
                challengerEmperor?.Key ?? -1L, data.legitimacy_challenger_core_kingdom_id);
        challengerEmperor?.RecordPersonalHistory(content, "legitimacy_challenge_failed",
            relatedActorId: incumbentEmperor?.Key ?? -1L);
        incumbentEmperor?.RecordPersonalHistory(content, "legitimacy_challenge_survived",
            relatedActorId: challengerEmperor?.Key ?? -1L);
        EmpireCraft.Core.TranslateHelper.LogEventMessage(content, challenger?.CoreKingdom);
    }

    private static void RecordArchivedEmpireHistory(long empireId, string content, long actorId, long kingdomId)
    {
        if (empireId <= 0 || string.IsNullOrWhiteSpace(content) ||
            !ModClass.ALL_HISTORY_DATA.TryGetValue(empireId, out List<EmpireCraftHistory> histories) ||
            histories == null || histories.Count == 0) return;
        EmpireCraftHistory history = histories.LastOrDefault(item => item != null);
        if (history == null) return;
        history.descriptions ??= new List<HistoryDescription>();
        if (history.descriptions.Any(item => item?.description == content)) return;
        history.descriptions.Add(new HistoryDescription
        {
            time = GameClock.DateText(GameClock.Now),
            timestamp = GameClock.Now,
            description = content,
            actor_id = actorId,
            kingdom_id = kingdomId
        });
    }
#endif

    private static bool IsActiveEmpire(Empire empire)
    {
        return empire != null && !empire.IsArchived() && !empire.IsGone() &&
               empire.CoreKingdom != null && !empire.CoreKingdom.IsGone() &&
               !AncientWarfareCompatibility.Owns(empire.CoreKingdom);
    }
}
}
