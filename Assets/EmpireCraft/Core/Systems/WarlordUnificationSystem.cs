// Generated from the EmpireCraft mod: Scripts/GeneralSystems/WarlordUnificationSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Random = EmpireCraft.Core.EcRandom;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 军阀时期的统一进程(参照北伐、解放战争)，以天下为单位，每年随 UpdateRealm 结算一次：
//   1. 内战打到底：同一法统内部的战争(IsCivilWar)不受"战争年限强制停战"和原版议和限制(见 WarPatch、attacker_stop_war)；
//   2. 中央主动统一：中央每年可对接壤的法统内对手发动统一战争，不受开战间隔限制，最多同时 MaxCivilWars 场；
//      与中央交战的一方地盘被中央占得越多越可能举国归附(每月，见 FounderCapitulation)；
//      地方武装之间(都不是中央)的混战打满四年各自罢兵(SettleWarlordFeuds)；
//   3. 民心所向：城市主流理念占比过半、统治者却是别的理念时，倒向代表该理念的政府——
//      接壤就起义归附，不接壤就揭竿而起成为该理念的地方武装(之后由同理念政府吸引归附)；
//   4. 民心即正统：政府的正统每年向"本理念在法统内的支持率"靠拢；
//   5. 大势所趋：中央控制法统过半城市后，各理念的地方武装每年都可能易帜归附；
//   6. 兵败如山倒：与中央交战、只剩不到 CollapseShare 核心城市的政府，接壤中央的城市起义投向中央。
// 民族统一战线期间(NationalSentimentSystem)不打内战，2、3、6 暂停。
public static partial class WarlordEraSystem
{
    private const int MaxCivilWars = 3;
    private const float UnifyWarChance = 0.8f;
    private const float ShiftMinShare = 0.5f;
    private const float ShiftBaseChance = 0.06f;
    private const float ShiftMaxExtraChance = 0.24f;
    private const float UprisingFactor = 0.5f;
    private const int MaxShiftsPerYear = 4;
    private const int MandateBase = 20;
    private const int MandateStep = 5;
    private const float BandwagonShare = 0.4f;
    private const float CollapseShare = 0.3f;
    private const float CollapseChance = 0.25f;
    private const int MaxCollapsePerYear = 10;

    public static bool IsCivilWar(War war)
    {
        if (war == null || war.HasEnded()) return false;
        EmpireCore attacker = LawfulCoreOf(war.GetMainAttacker());
        return attacker != null && attacker == LawfulCoreOf(war.GetMainDefender()) &&
               attacker.fragmented_since >= 0d && !attacker.split_recognized;
    }

    // 法统 = 都城所在的天下(军阀时期以天下为单位，见 WarlordEraSystem 文件头)
    private static EmpireCore LawfulCoreOf(Realm kingdom) => RealmOf(kingdom);

#if EC_PENDING
    private static void UpdateUnification(EmpireCore core, Dictionary<Realm, int> held, int total, Empire central,
        HashSet<Empire> representatives)
    {
        if (central == null || central.IsGone() || central.IsArchived() || total <= 0) return;
        List<City> cities = TianxiaSystem.Cities(core).Where(city => city != null && !city.IsGone() && city.Realm != null)
            .ToList();
        if (cities.Count == 0) return;

        AlignMandates(cities, representatives);
        Bandwagon(core, held, total, central);
        if (NationalSentimentSystem.InUnitedFront(core)) return;
        LaunchUnificationWar(core, held, central, representatives);
        PopularShift(core, cities, representatives);
        Collapse(held, total, central, representatives);
    }
#endif

    // 4. 民心即正统
#if EC_PENDING
    private static void AlignMandates(List<City> cities, HashSet<Empire> representatives)
    {
        var dominant = cities.Select(IdeologyPopulationSystem.GetDominant).ToList();
        foreach (Empire government in representatives)
        {
            if (government == null || government.IsGone() || government.IsArchived()) continue;
            PartyIdeology ideology = GovernmentIdeology(government);
            float support = dominant.Count(value => value == ideology) / (float)dominant.Count;
            int target = Mathf.RoundToInt(MandateBase + (100 - MandateBase) * support);
            int delta = Mathf.Clamp(target - government.Mandate, -MandateStep, MandateStep);
            if (delta != 0) government.AddMandate(delta);
        }
    }
#endif

    // 2. 中央主动统一
#if EC_PENDING
    private static void LaunchUnificationWar(EmpireCore core, Dictionary<Realm, int> held, Empire central,
        HashSet<Empire> representatives)
    {
        Realm centralKingdom = central.CoreKingdom;
        if (centralKingdom == null || !centralKingdom.HasRuler() || CorpsSystem.CannotFundWar(centralKingdom) ||
            Random.value >= UnifyWarChance) return;
        if (centralKingdom.GetWars().Count(IsCivilWar) >= MaxCivilWars) return;
        Realm target = held.Keys
            .Where(holder => holder != centralKingdom && IsLocalHolder(core, holder) && !InRealm(holder, central) &&
                             !holder.IsEnemy(centralKingdom) && !InTruce(centralKingdom, holder, central) &&
                             central.IsNeighbourWith(holder) &&
                             FeudalVassalService.CanDeclareExternalWar(centralKingdom, holder))
            .OrderByDescending(holder => Government(holder) != null && representatives.Contains(Government(holder)))
            .ThenBy(holder => held[holder]).FirstOrDefault();
        if (target == null) return;
        War war = EmpireWorld.Current.StartWar(centralKingdom, target, WarKinds.Normal);
        if (war == null) return;
        EventRecorder.Record(centralKingdom, string.Format(LM.Get("warlord_unify_war_history"),
            central.GetBaseEmpireFullName(), HolderName(target)));
    }
#endif

    // 3. 民心所向
#if EC_PENDING
    private static void PopularShift(EmpireCore core, List<City> cities, HashSet<Empire> representatives)
    {
        var byIdeology = new Dictionary<PartyIdeology, Empire>();
        foreach (Empire government in representatives)
            if (government != null && !government.IsGone() && !government.IsArchived())
                byIdeology[GovernmentIdeology(government)] = government;
        int shifts = 0;
        foreach (City city in cities.OrderBy(_ => Random.value))
        {
            if (shifts >= MaxShiftsPerYear) return;
            Realm ruler = city.Realm;
            if (ruler == null || ruler.IsGone() || city == ruler.Capital) continue;
            PartyIdeology ideology = IdeologyPopulationSystem.GetDominant(city);
            if (!byIdeology.TryGetValue(ideology, out Empire target) || InRealm(ruler, target)) continue;
            Empire rulerGovernment = RulingGovernment(ruler);
            PartyIdeology rulerIdeology = rulerGovernment != null ? GovernmentIdeology(rulerGovernment) : HolderIdeology(ruler);
            if (rulerIdeology == ideology) continue;
            float share = IdeologyPopulationSystem.GetCityShare(city, ideology);
            if (share < ShiftMinShare) continue;
            float chance = (ShiftBaseChance + ShiftMaxExtraChance * (share - ShiftMinShare) / (1f - ShiftMinShare)) *
                           Suppression(rulerGovernment);
            Realm neighbour = AdjacentRealmKingdom(city, target);
            if (neighbour == null) chance *= UprisingFactor;
            if (Random.value >= chance) continue;
            string cityName = city.GetCityName();
            if (neighbour != null)
            {
                city.JoinRealm(neighbour, pCaptured: true, pRebellion: true);
                EventRecorder.Record(neighbour, string.Format(LM.Get("warlord_defect_city_history"), cityName,
                    PartySystem.GetIdeologyName(ideology), target.GetBaseEmpireFullName()));
                shifts++;
            }
            else if (TryUprising(city, ruler, ideology))
            {
                EventRecorder.Record(ruler, string.Format(LM.Get("warlord_uprising_history"), cityName,
                    PartySystem.GetIdeologyName(ideology)));
                shifts++;
            }
        }
    }

    private static bool TryUprising(City city, Realm ruler, PartyIdeology ideology)
    {
        Person leader = city.People?.Where(actor => actor != null && !actor.IsGone() && actor.Alive &&
                                                  actor.IsAdult() && actor.CanFoundCivKingdom())
            .OrderByDescending(actor => IdeologyPopulationSystem.Get(actor) == ideology)
            .ThenByDescending(actor => actor.Renown).FirstOrDefault();
        if (leader == null) return false;
        Realm rebel = city.makeOwnKingdom(leader, pRebellion: true);
        if (rebel == null) return false;
        if (!rebel.StartLocalRebelling(EmpireWarType.地方叛乱))
        {
            RebellionStartupService.RollbackCitySplit(city, ruler, rebel);
            return false;
        }
        War war = EmpireWorld.Current.StartWar(rebel, ruler, WarKinds.Rebellion);
        if (war == null)
        {
            RebellionStartupService.RollbackCitySplit(city, ruler, rebel);
            return false;
        }
        war.SetEmpireWarType(EmpireWarType.地方叛乱);
        return true;
    }
#endif

    // 统治者宪法的意识形态强度越高，越压得住异见民心
    private static float Suppression(Empire government) => government == null ? 1f :
        ConstitutionSystem.GetIdeologyIntensity(government) switch
        {
            ConstitutionIdeologyIntensity.High => 0.5f,
            ConstitutionIdeologyIntensity.Low => 1.3f,
            _ => 1f
        };

    // 5. 大势所趋。leftoversOnly：统一之后清剩下的地方武装(见 AbsorbLeftovers)，正在造反的、与中央交战的不动
#if EC_PENDING
    private static void Bandwagon(EmpireCore core, Dictionary<Realm, int> held, int total, Empire central,
        bool leftoversOnly = false)
    {
        Realm centralKingdom = central.CoreKingdom;
        if (centralKingdom == null || !held.TryGetValue(centralKingdom, out int centralHeld)) return;
        float share = centralHeld / (float)total;
        if (share < BandwagonShare) return;
        float chance = Mathf.Clamp(0.15f + (share - BandwagonShare) * 1.5f, 0f, 0.6f);
        foreach (Realm holder in held.Keys.ToList())
        {
            if (holder == centralKingdom || !IsLocalHolder(core, holder) || Government(holder) != null ||
                holder.IsInEmpire() || FeudalVassalService.GetOverlord(holder) != null ||
                holder.GetRegime()?.type != RegimeType.Modern || !holder.HasRuler()) continue;
            if (leftoversOnly && (holder.IsFactionRebelling() || holder.IsLocalRebelling() ||
                                  holder.IsEnemy(centralKingdom))) continue;
            if (Random.value >= chance) continue;
            War war = FindActiveWar(holder, centralKingdom);
            if (war != null)
                EmpireWorld.Current.EndWar(war, war.IsAttacker(centralKingdom) ? WarOutcome.Attackers : WarOutcome.Defenders);
            if (holder.IsEnemy(centralKingdom) || !AbsorbDefector(central, holder)) continue;
            EmpireCoreControl.Invalidate(core);
            EventRecorder.Record(centralKingdom, string.Format(LM.Get("warlord_bandwagon_history"),
                HolderName(holder), central.GetBaseEmpireFullName()));
        }
    }
#endif

    // 开国雄主的望风归附、兴兵讨伐本是"每年一次"，这个"每年"只靠 WarlordEraSystem.UpdateWorld 的年度闸把着。
    // 神力加冕(EmpireFormButton.Refresh)调的是 UpdateWorld(force: true)，会绕开那道闸、把全世界的
    // 帝国法理立刻重跑一遍：玩家连点几次手动称帝，全图的开国雄主就一次次对接壤的割据政权宣战(目标吞并)、
    // 周边小国一次次望风归附，看着就是"一称帝就到处叛乱、到处被吞"(玩家 2026-11-08 反馈"现在不能手动称帝，
    // 会导致其他地方叛乱的")。这里按法理各记一次时间，强制扫描也不会把这一年的份额重复用掉。
    private static readonly Dictionary<long, double> FounderPassAt = new();

    private static bool FounderPassDue(EmpireCore core, double now)
    {
        if (core == null) return false;
        if (FounderPassAt.TryGetValue(core.id, out double last) && last >= 0d && now >= last &&
            GameClock.YearsSince(last) < 1) return false;
        FounderPassAt[core.id] = now;
        return true;
    }

    // 开国雄主的望风归附(古代王朝，不限军阀时期)：开国雄主在位的帝国控制本核心四成以上城市时，
    // 接壤、尚未入帝国、没有宗主的本核心割据政权每年按控制比例有一定概率归附(与"大势所趋"同一套概率与归附方式)。
    // 只剩一两座城的小国才不战而降(用户 2026-10-19 选"一并改成战线崩溃"：以前多大的割据政权都整国倒过来)
    private const int BandwagonMaxCities = 2;
#if EC_PENDING
    private static void FounderBandwagon(EmpireCore core, Dictionary<Realm, int> held, int total)
    {
        if (total <= 0 || NationalSentimentSystem.InUnitedFront(TianxiaSystem.Lead(core))) return;
        Realm founderKingdom = held.Keys
            .Where(kingdom => kingdom != null && !kingdom.IsGone() && kingdom.IsEmpire() &&
                              kingdom.GetEmpire()?.CoreKingdom == kingdom && RulerTraitSystem.FounderReigns(kingdom) &&
                              IsCoreHolder(core, kingdom))
            .OrderByDescending(kingdom => held[kingdom]).FirstOrDefault();
        if (founderKingdom == null) return;
        Empire founder = founderKingdom.GetEmpire();
        float share = held[founderKingdom] / (float)total;
        if (share < BandwagonShare) return;
        float chance = Mathf.Clamp(0.10f + (share - BandwagonShare) * 1.25f, 0f, 0.5f);
        foreach (Realm holder in held.Keys.ToList())
        {
            if (holder == null || holder == founderKingdom || holder.IsGone() || !holder.HasRuler() ||
                !IsCoreHolder(core, holder) || holder.IsInEmpire() || FeudalVassalService.GetOverlord(holder) != null ||
                holder.GetRegime()?.type == RegimeType.Modern || (holder.Cities?.Count ?? 0) > BandwagonMaxCities ||
                !Borders(holder, founder) || Random.value >= chance) continue;
            War war = FindActiveWar(holder, founderKingdom);
            if (war != null)
                EmpireWorld.Current.EndWar(war, war.IsAttacker(founderKingdom) ? WarOutcome.Attackers : WarOutcome.Defenders);
            if (holder.IsEnemy(founderKingdom) || !AbsorbDefector(founder, holder)) continue;
            EmpireCoreControl.Invalidate(core);
            EventRecorder.Record(founderKingdom, string.Format(LM.Get("founder_bandwagon_history"),
                holder.GetKingdomFullName(), founder.GetBaseEmpireFullName(), founder.Emperor?.FullName() ?? "",
                RulerTraitSystem.FounderTitle(founder.Emperor)));
        }
    }
#endif

    // 开国雄主志在一统(古代王朝，不限军阀时期)：开国雄主在位的帝国每年几乎必定对接壤的本核心割据政权(不在帝国里、
    // 不是自己的附庸，含同核心的对立王朝)兴兵讨伐，同时至多 MaxFounderWars 场；不看兵力对比(雄主不等时机)，
    // 只是不打同盟。这类战争不受战争年限强制停战(见 RulerTraitSystem.IsFounderUnificationWar)
    private const int MaxFounderWars = 2;
    private const float FounderWarChance = 0.9f;

#if EC_PENDING
    private static void FounderDrive(EmpireCore core, Dictionary<Realm, int> held)
    {
        if (NationalSentimentSystem.InUnitedFront(TianxiaSystem.Lead(core))) return;
        Realm founderKingdom = held.Keys
            .Where(kingdom => kingdom != null && !kingdom.IsGone() && kingdom.IsEmpire() && kingdom.HasRuler() &&
                              kingdom.GetEmpire()?.CoreKingdom == kingdom && RulerTraitSystem.FounderReigns(kingdom) &&
                              IsCoreHolder(core, kingdom))
            .OrderByDescending(kingdom => held[kingdom]).FirstOrDefault();
        if (founderKingdom == null || CorpsSystem.CannotFundWar(founderKingdom)) return;
        Empire founder = founderKingdom.GetEmpire();
        int wars = founderKingdom.GetWars().Count(RulerTraitSystem.IsFounderUnificationWar);
        foreach (Realm target in held.Keys
                     .Where(holder => holder != null && holder != founderKingdom && !holder.IsGone() && holder.HasRuler() &&
                                      IsCoreHolder(core, holder) && holder.GetEmpire() != founder &&
                                      FeudalVassalService.GetOverlord(holder) != founderKingdom &&
                                      !holder.IsEnemy(founderKingdom) && !InTruce(founderKingdom, holder, founder) &&
                                      Borders(holder, founder) &&
                                      (founderKingdom.getAlliance() == null || holder.getAlliance() != founderKingdom.getAlliance()))
                     .OrderBy(holder => held[holder]).ToList())
        {
            if (wars >= MaxFounderWars || Random.value >= FounderWarChance) break;
            War war = EmpireWorld.Current.StartWar(founderKingdom, target, WarKinds.Normal);
            if (war == null) continue;
            // 雄主之战志在一统：目标吞并(以前交给 WarGoalSystem.Ensure 按兵力、接壤定，多半成了赔款、割地，一赔就停战三年)
            WarGoalSystem.Preset(war, WarGoal.吞并);
            wars++;
            EventRecorder.Record(founderKingdom, string.Format(LM.Get("founder_unify_war_history"),
                founder.GetBaseEmpireFullName(), RulerTraitSystem.FounderTitle(founder.Emperor),
                founder.Emperor?.FullName() ?? "", target.GetKingdomFullName()));
        }
    }
#endif

    // 每月：开国雄主的统一战争里，被讨伐的割据政权正规军打垮、又丢了都城或三分之一的城，兵败如山倒——战线崩溃、
    // 推进加快，城仍一座座打(见 FrontCollapse；用户 2026-10-19 选"一并改成战线崩溃"，以前丢一块地就每月有几率举国归附)。
    // 军阀时期的内战照旧：与中央交战的一方地盘被中央占得越多越可能举国归附
#if EC_PENDING
    public static void FounderCapitulation()
    {
        if (EmpireWorld.Current?.LiveWars == null) return;
        foreach (War war in EmpireWorld.Current.LiveWars.ToList())
        {
            if (war == null || war.HasEnded()) continue;
            Realm attacker = war.GetMainAttacker(), defender = war.GetMainDefender();
            bool founderWar = RulerTraitSystem.IsFounderUnificationWar(war);
            bool attackerFounder;
            if (founderWar)
                attackerFounder = attacker?.GetEmpire()?.CoreKingdom == attacker && RulerTraitSystem.FounderReigns(attacker);
            else if (IsCivilWar(war))
            {
                EmpireCore lawful = LawfulCoreOf(attacker);
                Empire centralGovernment = lawful == null ? null : ModClass.EMPIRE_MANAGER?.Get(lawful.central_empire_id);
                Realm centralKingdom = centralGovernment?.CoreKingdom;
                if (centralKingdom == null || attacker != centralKingdom && defender != centralKingdom) continue;
                attackerFounder = attacker == centralKingdom;
            }
            else continue;
            Realm founderKingdom = attackerFounder ? attacker : defender, rival = attackerFounder ? defender : attacker;
            Empire founder = founderKingdom?.GetEmpire();
            if (founder == null || rival == null || rival.IsGone() || rival.Cities == null || rival.Cities.Count == 0) continue;
            if (founderWar)
            {
                FrontCollapse.Check(founderKingdom, rival, war);
                continue;
            }
            int zones = 0, lost = 0;
            foreach (City city in rival.Cities)
            {
                if (city?.Zones == null) continue;
                foreach (Cell zone in city.Zones)
                {
                    if (zone == null) continue;
                    zones++;
                    Realm occupier = city.GetTileZoneOccupier(zone);
                    if (occupier != null && (occupier == founderKingdom || occupier.GetEmpire() == founder)) lost++;
                }
            }
            float share = zones == 0 ? 0f : lost / (float)zones;
            Cell seat = rival.Capital?.SeatCell()?.Zone;
            Realm seatHolder = seat == null ? null : rival.Capital.GetTileZoneOccupier(seat);
            bool capitalFallen = rival.Capital == null || seatHolder != null && (seatHolder == founderKingdom || seatHolder.GetEmpire() == founder);
            float chance = 0.04f + 0.6f * share + (capitalFallen ? 0.3f : 0f);
            if (lost == 0 && !capitalFallen || Random.value >= chance) continue;
            EmpireWorld.Current.EndWar(war, attackerFounder ? WarOutcome.Attackers : WarOutcome.Defenders);
            if (!AbsorbDefector(founder, rival)) continue;
            EmpireCore core = rival.Capital?.GetEmpireCore();
            if (core != null) EmpireCoreControl.Invalidate(core);
            if (founderWar)
                EventRecorder.Record(founderKingdom, string.Format(LM.Get("founder_capitulation_history"),
                    rival.GetKingdomFullName(), founder.GetBaseEmpireFullName(), RulerTraitSystem.FounderTitle(founder.Emperor),
                    founder.Emperor?.FullName() ?? ""));
            else
                EventRecorder.Record(founderKingdom, string.Format(LM.Get("warlord_capitulation_history"),
                    HolderName(rival), founder.GetBaseEmpireFullName()));
        }
    }
#endif

    // 刚议和的(各守旧土、岁币求和)停战期内不再兴兵，岁币照付的对象也不打——以前中央、开国雄主每年挑最弱的割据政权开战，
    // 刚和完第二年又打同一家，看上去反复投降反复进攻。停战年限同原版挑战争目标(minimum_years_between_wars)
#if EC_PENDING
    private static bool InTruce(Realm kingdom, Realm target, Empire government) =>
        government?.given_Kingdoms != null && government.given_Kingdoms.Contains(target) ||
        GameClock.YearsSince(EmpireWorld.Current.diplomacy.getRelation(kingdom, target).data.timestamp_last_war_ended) <
        SimGlobals.m.minimum_years_between_wars;
#endif

    // 与这个帝国接壤：本国有城市与帝国的城市相邻
    private static bool Borders(Realm kingdom, Empire empire) =>
        kingdom?.Cities != null && kingdom.Cities.Any(city =>
            city?.NeighbourCities != null && city.NeighbourCities.Any(neighbour =>
                neighbour?.Realm != null && neighbour.Realm.GetEmpire() == empire));

    // 6. 兵败如山倒
#if EC_PENDING
    private static void Collapse(Dictionary<Realm, int> held, int total, Empire central,
        HashSet<Empire> representatives)
    {
        Realm centralKingdom = central.CoreKingdom;
        int defected = 0;
        foreach (Empire loser in representatives)
        {
            if (loser == null || loser == central || loser.IsGone() || loser.IsArchived()) continue;
            Realm loserKingdom = loser.CoreKingdom;
            if (loserKingdom == null || FindActiveWar(loserKingdom, centralKingdom) == null) continue;
            int count = held.TryGetValue(loserKingdom, out int value) ? value : 0;
            if (count >= total * CollapseShare) continue;
            foreach (City city in loser.AllCities().ToList())
            {
                if (defected >= MaxCollapsePerYear) return;
                if (city == null || city.IsGone() || city == city.Realm?.Capital || Random.value >= CollapseChance)
                    continue;
                Realm neighbour = AdjacentRealmKingdom(city, central);
                if (neighbour == null) continue;
                string cityName = city.GetCityName();
                city.JoinRealm(neighbour, pCaptured: true, pRebellion: true);
                defected++;
                EventRecorder.Record(neighbour, string.Format(LM.Get("warlord_collapse_history"),
                    loser.GetBaseEmpireFullName(), cityName, central.GetBaseEmpireFullName()));
            }
        }
    }
#endif

    // 易帜归附：先按宗属绑定并尝试并入政府；并入不了(法统判定对不上等)就直接加入政府成为正式成员，
    // 只有加入也失败才保留附庸身份。返回是否已归附(成员或附庸)。
#if EC_PENDING
    internal static bool AbsorbDefector(Empire central, Realm holder)
    {
        Realm centralKingdom = central?.CoreKingdom;
        if (centralKingdom == null || holder == null || holder.IsGone()) return false;
        if (!FeudalVassalService.Bind(centralKingdom, holder)) return false;
        FeudalVassalService.MarkDefected(holder);
        ModernStateFormationSystem.TryMergeIntoOverlordGovernment(holder);
        if (holder.GetEmpire() == central) return true;
        if (holder.IsEmpire()) return true;                    // 自有政府的保留附庸身份
        FeudalVassalService.Break(holder);
        if (holder.IsInEmpire()) holder.GetEmpire()?.leave(holder);
        central.join(holder, pForce: true, pLegitimacyTransfer: true);
        if (holder.GetEmpire() == central)
        {
            holder.GetOrCreate().ideology_country_suffix = "";
            holder.updateColor(centralKingdom.getColor());
            EmpireCraft.Core.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(holder);
            return true;
        }
        FeudalVassalService.Bind(centralKingdom, holder);
        FeudalVassalService.MarkDefected(holder);
        return true;
    }
#endif

    private static Empire RulingGovernment(Realm kingdom) =>
        kingdom.GetEmpire() ?? Government(FeudalVassalService.GetOverlord(kingdom));

    private static bool InRealm(Realm kingdom, Empire government) =>
        kingdom != null && government != null &&
        (kingdom == government.CoreKingdom || kingdom.GetEmpire() == government ||
         FeudalVassalService.GetOverlord(kingdom) == government.CoreKingdom);

    private static Realm AdjacentRealmKingdom(City city, Empire government) =>
        city.NeighbourCities?.FirstOrDefault(neighbour => neighbour != null && !neighbour.IsGone() &&
                                                             InRealm(neighbour.Realm, government))?.Realm;
}
}
