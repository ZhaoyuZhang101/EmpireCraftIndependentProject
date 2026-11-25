// Ported from the EmpireCraft mod: Scripts/GeneralSystems/HolyRomanSystem.Diet.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 帝国议会(封建制帝国，原型神圣罗马帝国议会)：皇帝要钱、要兵、要惩治不听话的诸侯，都得先过议会。
//   · 三院：选帝侯院(选帝侯团)、诸侯院(其余成员国君主)、城市院(帝国自由城市)；
//     选帝侯院与诸侯院都过半才算通过，城市院只有建议权；
//   · 议程(每次一项，按急缓)：
//     帝国战争——中央与外敌交战时付议，通过则各成员国(含有自主外交的世袭诸侯)按中央一方参战、出征(见 CallToArms)；
//     帝国禁令——有成员国拒缴摊派或拒不应征时付议，通过则由与它接壤的最强诸侯(施行帝国改革后由所在大区)
//       奉命讨伐，打下的城归执行者；被禁者不得列席议会、不得为选帝侯；
//     帝国改革——文化制度节点已施行、本帝国未颁行、帝国权威 60 以上时付议(四年一次)，通过则颁行、扣权威 30
//       (小诸侯、自由城市、教会诸侯乐见永久和平，五城以上的大诸侯不愿失了私战之权)；
//     陪臣化——大诸侯请并一城小邦、不走法院时付议(见 Immediacy，不经 PickAgenda)；
//     帝国摊派——中央国库吃紧或在打仗时付议，通过则各成员国按年度经常收入的 15% 上缴中央；
//   · 投票：与皇帝的关系、是否同一家族、教皇党或皇帝党、皇帝家族领地的厚薄、帝国权威(每票 +(权威−50)/200)，
//     加上议题本身的利害；提请禁令花权威 10(AI 权威不到 20 不提)；
//   · 照常每四年一议，帝国战争与禁令一年即可再议；皇帝远征加冕在外时不开议会。
public static partial class HolyRomanSystem
{
    public const string AgendaLevy = "levy", AgendaWar = "war", AgendaBan = "ban", AgendaReform = "reform";
    private const int ReformAgendaGapYears = 4;
    public const int CollegeElectors = 0, CollegePrinces = 1, CollegeCities = 2;
    private const int DietIntervalYears = 4, UrgentDietGapYears = 1;
    private const float LevyRate = 0.15f;
    private const int DefianceMemoryYears = 10, BanMaxYears = 15;
    private const int MaxDietRecords = 12;
    public const int BanLegitimacyWin = 5, BanLegitimacyLoss = -10, ImperialWarWin = 8, ImperialWarLoss = -8;

    public sealed class DietVoter
    {
        public Person person;
        public Realm realm;
        public int college;
    }

    // 封建制帝国(圣座帝国除外)，至少两个成员国
#if EC_PENDING
    public static bool HasDiet(Empire empire) =>
        empire?.data != null && !empire.IsGone() && !empire.IsArchived() && empire.CoreKingdom?.Record != null &&
        !empire.CoreKingdom.IsGone() && empire.CoreKingdom.GetRegime()?.type == RegimeType.Feudalism &&
        !PapacySystem.IsHolySee(empire) && Members(empire).Count >= 2;

    public static List<Realm> Members(Empire empire) =>
        empire?.kingdoms_list?.Where(kingdom => kingdom?.Record != null && !kingdom.IsGone() && kingdom != empire.CoreKingdom &&
                                                kingdom.GetEmpire() == empire).ToList() ?? new List<Realm>();

    public static bool IsBanned(Realm kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        HolyRomanState state = State(empire);
        return state != null && kingdom.Key == state.banned_kingdom_id;
    }
#endif

    // 三院席位：选帝侯(本人)，其余成员国按自由城市与诸侯分院；被禁者不得列席
#if EC_PENDING
    public static List<DietVoter> Voters(Empire empire)
    {
        var result = new List<DietVoter>();
        var electorRealms = new HashSet<Realm>();
        HolyRomanState state = State(empire);
        foreach (Person actor in empire.GetCabinetMembers())
        {
            if (actor?.Record == null || actor.IsGone() || !actor.Alive || actor == empire.Emperor) continue;
            Realm realm = actor.Realm;
            if (realm == null || realm.IsGone() || realm.GetEmpire() != empire || realm.Key == state.banned_kingdom_id) continue;
            result.Add(new DietVoter { person = actor, realm = realm, college = CollegeElectors });
            if (realm != empire.CoreKingdom) electorRealms.Add(realm);
        }
        foreach (Realm kingdom in Members(empire))
        {
            if (electorRealms.Contains(kingdom) || kingdom.Key == state.banned_kingdom_id) continue;
            result.Add(new DietVoter { person = kingdom.Ruler, realm = kingdom, college = IsFreeCity(kingdom) ? CollegeCities : CollegePrinces });
        }
        return result;
    }
#endif

    private static bool SameHouse(Person a, Person b) =>
        a?.Record != null && b?.Record != null && SpecificClanManager.SameLineage(a.GetSpecificClan(), b.GetSpecificClan());

#if EC_PENDING
    private static int Opinion(Realm from, Realm to) => EmpireWorld.Current?.diplomacy?.getOpinion(from, to)?.total ?? 0;

    private static bool Allied(Realm a, Realm b) => a.hasAlliance() && b.hasAlliance() && a.getAlliance() == b.getAlliance();
#endif

    // 一票：皇帝的人缘、家族、党派、教会诸侯的叙任，加上议题本身的利害
#if EC_PENDING
    private static float Stance(Empire empire, DietVoter voter, string agenda, Realm subject)
    {
        Realm core = empire.CoreKingdom, realm = voter.realm;
        HolyRomanState state = State(empire);
        float score = 0f;
        if (realm == core) score += 0.6f;
        else
        {
            score += Mathf.Clamp(Opinion(realm, core) / 100f, -0.4f, 0.4f);
            if (SameHouse(voter.person, empire.Emperor)) score += 0.5f;
            int party = PapacySystem.Party(realm);
            if (party == PapacySystem.PartyEmperor) score += 0.15f;
            else if (party == PapacySystem.PartyPope) score -= PapacySystem.IsExcommunicated(empire) ? 0.4f : 0.15f;
            if (IsEcclesiastical(realm))
                score += PapacySystem.Investiture(empire) == PapacySystem.InvestitureCrown ? 0.25f
                    : PapacySystem.IsExcommunicated(empire) ? -0.4f : 0f;
        }
        score += HouseShare(empire) - 0.2f;
        score += ImperialAuthorityRules.StanceBonus(Authority(empire));
        if (state.sovereign_princes) score -= 0.2f;
        if (ReformEnacted(empire)) score += 0.1f;
        switch (agenda)
        {
            case AgendaLevy:
                score -= 0.35f;
                Realm foe = OutsideFoes(empire).FirstOrDefault();
                if (foe != null)
                {
                    score += 0.35f;
                    if (foe.hasReligion() && core.hasReligion() && foe.religion != core.religion) score += 0.15f;
                }
                if (voter.college == CollegeCities) score += 0.1f;
                break;
            case AgendaWar when subject != null:
                score += 0.05f;
                if (subject.hasReligion() && core.hasReligion() && subject.religion != core.religion) score += 0.3f;
                if (CoreWarWith(empire, subject)?.isDefender(core) == true) score += 0.3f;
                if (realm != core && realm.IsNeighbourWith(subject)) score += 0.15f;
                if (realm.isInWarWith(subject)) score += 0.4f;
                if (Allied(realm, subject)) score -= 0.6f;
                if (SameHouse(realm.Ruler, subject.Ruler)) score -= 0.5f;
                break;
            case AgendaBan when subject != null:
                if (realm == subject) return -1f;
                score += 0.1f;
                if (realm.IsNeighbourWith(subject)) score += 0.25f;
                if (SameHouse(realm.Ruler, subject.Ruler)) score -= 0.6f;
                int partyRealm = PapacySystem.Party(realm);
                if (partyRealm != PapacySystem.PartyNone && partyRealm == PapacySystem.Party(subject)) score -= 0.3f;
                if (subject.Cities.Count > realm.Cities.Count * 2) score -= 0.15f;
                score -= Mathf.Clamp(Opinion(realm, subject) / 100f, -0.3f, 0.3f);
                break;
            case AgendaMediatize when subject != null:
            {
                float stance = MediatizeStance(empire, state, voter, subject);
                if (Mathf.Abs(stance) >= 1f) return stance;
                score += stance;
                break;
            }
            case AgendaReform:
                // 永久和平护着小邦，大诸侯失了私战之权
                if (voter.college == CollegeElectors) score += 0.1f;
                if (realm == core) break;
                if (IsFreeCity(realm)) score += 0.3f;
                else if (IsEcclesiastical(realm)) score += 0.1f;
                else if (realm.Cities.Count <= 2) score += 0.2f;
                else if (realm.Cities.Count >= 5) score -= 0.2f;
                break;
        }
        return score + EcRandom.Range(-0.2f, 0.2f);
    }
#endif

    // 中央与外敌(不在本帝国的)交战、中央领头的仗
#if EC_PENDING
    private static IEnumerable<Realm> OutsideFoes(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        foreach (War war in core.getWars())
        {
            if (war?.data == null || !war.Alive || war.hasEnded()) continue;
            Realm foe = war.main_attacker == core ? war.main_defender : war.main_defender == core ? war.main_attacker : null;
            if (foe?.Record != null && !foe.IsGone() && foe.GetEmpire() != empire) yield return foe;
        }
    }

    private static War CoreWarWith(Empire empire, Realm foe)
    {
        Realm core = empire.CoreKingdom;
        return core.getWars().FirstOrDefault(war => war?.data != null && war.Alive && !war.hasEnded() &&
                                                     (war.main_attacker == core && war.main_defender == foe ||
                                                      war.main_defender == core && war.main_attacker == foe));
    }

    public static War ImperialWar(Empire empire) => LiveWar(State(empire)?.imperial_war_id ?? -1L);
    public static War BanWar(Empire empire) => LiveWar(State(empire)?.ban_war_id ?? -1L);

    private static War LiveWar(long id)
    {
        War war = id >= 0 ? EmpireWorld.Current?.wars?.get(id) : null;
        return war?.data != null && war.Alive && !war.hasEnded() ? war : null;
    }
#endif

    // 应征参加帝国战争的成员国(中央议和时随中央退出，见 EmpirePeacePatch)
#if EC_PENDING
    public static bool InImperialWar(Empire empire, Realm member)
    {
        HolyRomanState state = State(empire);
        return state != null && member != null && ImperialWar(empire) != null && state.imperial_war_members.Contains(member.Key);
    }
#endif

    // 记抗命(以备禁令)，帝国权威 −2(每年至多 −8)
#if EC_PENDING
    private static void MarkDefiance(Empire empire, HolyRomanState state, Realm kingdom)
    {
        state.defiance ??= new Dictionary<long, double>();
        state.defiance[kingdom.Key] = GameClock.Now;
        AuthorityDefiance(empire, state, kingdom);
    }
#endif

    #region 年度

#if EC_PENDING
    private static void DietYearly(Empire empire, HolyRomanState state)
    {
        CleanupBan(empire, state);
        if (ImperialWar(empire) == null)
        {
            state.imperial_war_id = -1L;
            state.imperial_war_members?.Clear();
        }
        if (state.defiance != null)
            foreach (long id in state.defiance.Keys.ToList())
                if (GameClock.YearsSince(state.defiance[id]) > DefianceMemoryYears || EmpireWorld.Current.RealmByKey(id)?.GetEmpire() != empire)
                    state.defiance.Remove(id);
        if (RomzugUnderway(empire)) return;
        (string agenda, Realm subject) = PickAgenda(empire, state);
        if (agenda == null) return;
        double years = state.last_diet_at < 0d ? double.MaxValue : GameClock.YearsSince(state.last_diet_at);
        if (years < (agenda is AgendaLevy or AgendaReform ? DietIntervalYears : UrgentDietGapYears)) return;
        Convene(empire, agenda, subject);
    }

    public static (string agenda, Realm subject) PickAgenda(Empire empire, HolyRomanState state)
    {
        Realm core = empire.CoreKingdom;
        foreach (Realm foe in OutsideFoes(empire))
        {
            War war = CoreWarWith(empire, foe);
            if (war != null && war.data.id != state.imperial_war_id && war.data.id != state.last_war_agenda_id)
                return (AgendaWar, foe);
        }
        // 禁令要花权威 10，AI 留 10 的余量(权威不到 20 不提)
        if (state.banned_kingdom_id < 0 && state.defiance != null && Authority(empire) >= BanAiThreshold)
        {
            Realm defiant = state.defiance.OrderByDescending(pair => pair.Value)
                .Select(pair => EmpireWorld.Current.RealmByKey(pair.Key))
                .FirstOrDefault(kingdom => kingdom?.Record != null && !kingdom.IsGone() && kingdom.GetEmpire() == empire &&
                                           kingdom != core);
            if (defiant != null) return (AgendaBan, defiant);
        }
        // 帝位虚悬：帝国代理主持的议会只议帝国战争与禁令
        if (state.interregnum_since >= 0d) return (null, null);
        if (ReformDue(empire, state)) return (AgendaReform, null);
        long income = StateDebtSystem.AnnualStableIncome(core);
        if (core.GetMoney() < income / 2 || OutsideFoes(empire).Any()) return (AgendaLevy, null);
        return (null, null);
    }
#endif

    // 帝国改革可以付议：文化节点已施行、本帝国未颁行、权威够门槛、距上次付议满四年
#if EC_PENDING
    private static bool ReformDue(Empire empire, HolyRomanState state) =>
        ReformNodeEnacted(empire) && !ReformEnacted(empire) && Authority(empire) >= ReformThreshold &&
        (state.last_reform_agenda_at < 0d || GameClock.YearsSince(state.last_reform_agenda_at) >= ReformAgendaGapYears);
#endif

    #endregion

    #region 开议

#if EC_PENDING
    public static DietRecord Convene(Empire empire, string agenda, Realm subject)
    {
        try
        {
            HolyRomanState state = State(empire);
            var record = new DietRecord
            {
                at = GameClock.Now,
                agenda = agenda,
                subject = subject?.GetKingdomName() ?? ""
            };
            // 陪臣化：议题写"小邦并入请愿者"
            if (agenda == AgendaMediatize && subject != null)
                record.subject = string.Format(LM.Get("hre_mediatize_subject"), subject.GetKingdomName(),
                    LiveKingdom(state.petition_lord_id)?.GetKingdomName() ?? "");
            var ballots = new Dictionary<Realm, bool>();
            foreach (DietVoter voter in Voters(empire))
            {
                bool yes = Stance(empire, voter, agenda, subject) > 0f;
                if (!ballots.ContainsKey(voter.realm) || voter.college == CollegeElectors) ballots[voter.realm] = yes;
                switch (voter.college)
                {
                    case CollegeElectors: if (yes) record.electors_yes++; else record.electors_no++; break;
                    case CollegePrinces: if (yes) record.princes_yes++; else record.princes_no++; break;
                    default: if (yes) record.cities_yes++; else record.cities_no++; break;
                }
            }
            record.passed = Majority(record.electors_yes, record.electors_no) && Majority(record.princes_yes, record.princes_no);
            state.last_diet_at = record.at;
            if (agenda == AgendaWar && subject != null) state.last_war_agenda_id = CoreWarWith(empire, subject)?.data?.id ?? -1L;
            if (agenda == AgendaReform) state.last_reform_agenda_at = record.at;
            // 提请禁令即花权威(不论通过与否)
            if (agenda == AgendaBan && subject != null) Spend(empire, BanCost, "hre_auth_ban", subject.GetKingdomName());
            record.outcome = record.passed ? Enact(empire, state, agenda, subject, ballots) : LM.Get("hre_diet_rejected");
            state.diets ??= new List<DietRecord>();
            state.diets.Add(record);
            while (state.diets.Count > MaxDietRecords) state.diets.RemoveAt(0);
            Record(empire, string.Format(LM.Get("hre_diet_history"), AgendaText(agenda, record.subject),
                record.electors_yes, record.electors_no, record.princes_yes, record.princes_no,
                record.cities_yes, record.cities_no, record.outcome));
            return record;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 帝国议会失败: {exception.Message}");
            return null;
        }
    }
#endif

    // 没有席位的院不拦议案
    private static bool Majority(int yes, int no) => yes + no == 0 || yes * 2 > yes + no;

    public static string AgendaText(string agenda, string subject) => agenda switch
    {
        AgendaWar => string.Format(LM.Get("hre_agenda_war"), subject),
        AgendaBan => string.Format(LM.Get("hre_agenda_ban"), subject),
        AgendaReform => LM.Get("hre_agenda_reform"),
        AgendaMediatize => string.Format(LM.Get("hre_agenda_mediatize"), subject),
        _ => LM.Get("hre_agenda_levy")
    };

#if EC_PENDING
    private static string Enact(Empire empire, HolyRomanState state, string agenda, Realm subject, Dictionary<Realm, bool> ballots)
    {
        Realm core = empire.CoreKingdom;
        var refused = new List<string>();
        switch (agenda)
        {
            case AgendaWar:
            {
                War war = subject == null ? null : CoreWarWith(empire, subject);
                if (war == null) return LM.Get("hre_diet_war_over");
                return CallToArms(empire, state, war, subject, ballots);
            }
            case AgendaBan:
            {
                if (subject?.Record == null || subject.IsGone() || subject.GetEmpire() != empire) return LM.Get("label_none");
                state.banned_kingdom_id = subject.Key;
                state.banned_at = GameClock.Now;
                state.banned_name = subject.GetKingdomName();
                state.defiance?.Remove(subject.Key);
                subject.Ruler?.RecordPersonalHistory(string.Format(LM.Get("personal_history_imperial_ban"), empire.GetEmpireFullName()));
                Realm executor = Executor(empire, subject);
                if (executor == null) return LM.Get("hre_diet_ban_no_executor");
                War war = EmpireWorld.Current.diplomacy.startWar(executor, subject, WarTypeLibrary.normal);
                if (war?.data == null) return LM.Get("hre_diet_ban_no_executor");
                war.SetEmpireWarType(EmpireWarType.伐不臣);
                WarGoalSystem.Preset(war, WarGoal.割地);
                war.data.name = string.Format(LM.Get("hre_ban_war_name"), subject.GetKingdomName());
                state.ban_war_id = war.data.id;
                foreach (Realm member in Members(empire).Where(member => member != executor && member != subject &&
                             ballots.TryGetValue(member, out bool yes) && yes && member.IsNeighbourWith(subject) &&
                             !war.hasKingdom(member) && !Allied(member, subject)).Take(2))
                    war.joinAttackers(member);
                return string.Format(LM.Get("hre_diet_ban_outcome"), executor.GetKingdomName());
            }
            case AgendaMediatize:
            {
                Realm lord = LiveKingdom(state.petition_lord_id);
                if (lord == null || subject?.Record == null || subject.IsGone() || subject.GetEmpire() != empire)
                    return LM.Get("label_none");
                return Mediatize(empire, state, lord, subject, RouteDiet);
            }
            case AgendaReform:
            {
                if (ReformEnacted(empire)) return LM.Get("hre_diet_reform_already");
                state.reform_at = GameClock.Now;
                Spend(empire, ReformCost, "hre_auth_reform");
                MonarchyLegitimacy.Invalidate(empire);
                return LM.Get("hre_diet_reform_outcome");
            }
            default:
            {
                long total = 0;
                foreach (Realm member in Members(empire))
                {
                    if (member.isInWarWith(core) || IsBanned(member)) continue;
                    if (Refuses(empire, member, ballots))
                    {
                        refused.Add(member.GetKingdomName());
                        MarkDefiance(empire, state, member);
                        continue;
                    }
                    long amount = Math.Min(Math.Max(0L, member.GetMoney()),
                        (long)(StateDebtSystem.AnnualStableIncome(member) * LevyRate));
                    if (amount <= 0) continue;
                    member.SubMoney((int)amount, TreasuryCategory.ImperialLevy);
                    core.AddMoney((int)amount, TreasuryCategory.ImperialLevy);
                    total += amount;
                }
                return string.Format(LM.Get("hre_diet_levy_outcome"), MoneyDisplay.Format(total), Names(refused));
            }
        }
    }
#endif

    // 帝国战争应征：各成员国按中央一方出兵。听命于中央的诸侯在中央开战时就被带进了战争名单(见 FeudalVassalService.ExpandSide)，
    // 以前只数新加入的，回回"0国应征"，也没人下令出征——远处的军团照就近动员留守驻地。现在已在中央一方的与新加入的一律出征
    // (WarScopeSystem.Dispatch：全军可开往任何战线，兵多的诸侯出得多)；抗命的不出兵(已被带进战争的也只守本境)，记下以备禁令
#if EC_PENDING
    private static string CallToArms(Empire empire, HolyRomanState state, War war, Realm subject, Dictionary<Realm, bool> ballots)
    {
        Realm core = empire.CoreKingdom;
        bool attacking = war.isAttacker(core);
        var refused = new List<string>();
        int answered = 0, already = 0, opposed = 0, allied = 0, banned = 0, failed = 0;
        float troops = 0f;
        state.imperial_war_members ??= new List<long>();
        foreach (Realm member in Members(empire))
        {
            if (IsBanned(member))
            {
                banned++;
                continue;
            }
            bool inWar = war.hasKingdom(member);
            if (member.isInWarWith(core) || inWar && (attacking ? !war.isAttacker(member) : !war.isDefender(member)))
            {
                opposed++;
                continue;
            }
            if (!inWar && Allied(member, subject))
            {
                allied++;
                continue;
            }
            if (Refuses(empire, member, ballots))
            {
                refused.Add(member.GetKingdomName());
                MarkDefiance(empire, state, member);
                continue;
            }
            if (inWar) already++;
            else
            {
                if (attacking) war.joinAttackers(member);
                else war.joinDefenders(member);
                if (!war.hasKingdom(member))
                {
                    failed++;
                    continue;
                }
            }
            WarScopeSystem.Dispatch(member, war);
            FrontSystem.Log(member, string.Format(LM.Get("hre_corps_log_call_to_arms"), war.data.name));
            answered++;
            troops += Troops(member);
            if (!state.imperial_war_members.Contains(member.Key)) state.imperial_war_members.Add(member.Key);
        }
        state.imperial_war_id = war.data.id;
        LogService.LogInfo($"[EmpireCraft][神罗诊断] {empire.GetEmpireFullName()} 帝国战争「{war.data.name}」：应征出兵 {answered} 国" +
                           $"(原已随中央参战 {already}、新加入 {answered - already})，兵力约 {troops:0}；抗命 {refused.Count}；" +
                           $"未征——与中央交战或在敌方 {opposed}、与敌国同盟 {allied}、被禁 {banned}、加入失败 {failed}");
        return string.Format(LM.Get("hre_diet_war_troops_outcome"), answered, Names(refused), CorpsSystem.ShortStrength(troops));
    }
#endif

    // 成员国现有兵力：军团模式按各军团兵力，否则按原版计兵
#if EC_PENDING
    private static float Troops(Realm kingdom) =>
        CorpsSystem.Enabled ? CorpsSystem.Of(kingdom).Sum(corps => corps.strength) : kingdom.countTotalWarriors();
#endif

    // 不听调遣：家族领地薄弱时比中央大的诸侯一概不理；投了反对票的有三成抗命，投赞成的偶尔也赖掉；
    // 帝国权威高则少抗命(×1.5 − 权威/100，夹 0.5~1.5)
#if EC_PENDING
    private static bool Refuses(Empire empire, Realm member, Dictionary<Realm, bool> ballots)
    {
        // 分裂进程里割据的诸侯不奉诏(见 SchismSystem，用户 2026-10-21 选"先割据后独立")，同不听调遣的大诸侯
        if (Defies(empire, member) || SchismSystem.IsBreakaway(member)) return true;
        bool yes = ballots.TryGetValue(member, out bool vote) && vote;
        float chance = yes ? 0.05f : 0.3f;
        if (State(empire).sovereign_princes) chance += 0.2f;
        if (InCircle(empire, member)) chance *= 0.5f;
        chance *= ImperialAuthorityRules.RefusalFactor(Authority(empire));
        return EcRandom.value < chance;
    }
#endif

    private static string Names(List<string> names) => names.Count == 0 ? LM.Get("label_none") : string.Join("、", names);

    // 执行禁令：施行帝国改革后由被禁者所在大区最强的诸侯；否则与它接壤的最强诸侯；都没有时皇帝自己出兵
#if EC_PENDING
    private static Realm Executor(Empire empire, Realm target)
    {
        int strength = target.countTotalWarriors();
        bool Eligible(Realm kingdom) => kingdom != target && !kingdom.isInWarWith(target) && !Allied(kingdom, target) &&
                                          !SameHouse(kingdom.Ruler, target.Ruler) && kingdom.HasRuler() &&
                                          kingdom.countTotalWarriors() >= strength * 0.8f;
        HolyRomanState state = State(empire);
        if (ReformEnacted(empire) && state.circles != null && state.circles.TryGetValue(target.Key, out int circle))
        {
            Realm director = Members(empire).Where(kingdom => state.circles.TryGetValue(kingdom.Key, out int other) && other == circle)
                .Where(Eligible).OrderByDescending(kingdom => kingdom.countTotalWarriors()).FirstOrDefault();
            if (director != null) return director;
        }
        Realm neighbour = Members(empire).Where(kingdom => kingdom.IsNeighbourWith(target)).Where(Eligible)
            .OrderByDescending(kingdom => kingdom.countTotalWarriors()).FirstOrDefault();
        if (neighbour != null) return neighbour;
        Realm core = empire.CoreKingdom;
        return core.countTotalWarriors() >= strength && !core.isInWarWith(target) ? core : null;
    }
#endif

    // 禁令期满(或被禁者已不在帝国)解除；执行禁令的战争打完后解除
#if EC_PENDING
    private static void CleanupBan(Empire empire, HolyRomanState state)
    {
        if (state.banned_kingdom_id < 0) return;
        Realm banned = EmpireWorld.Current.RealmByKey(state.banned_kingdom_id);
        bool gone = banned?.Record == null || banned.IsGone() || banned.GetEmpire() != empire;
        bool expired = state.banned_at >= 0d && GameClock.YearsSince(state.banned_at) >= BanMaxYears && BanWar(empire) == null;
        if (gone || expired) LiftBan(empire, state, gone ? null : LM.Get("hre_ban_expired_history"));
    }

    private static void LiftBan(Empire empire, HolyRomanState state, string text)
    {
        if (!string.IsNullOrEmpty(text)) Record(empire, string.Format(text, state.banned_name));
        state.banned_kingdom_id = -1L;
        state.banned_at = -1d;
        state.ban_war_id = -1L;
        state.banned_name = "";
    }
#endif

    #endregion

    #region 战争结束

    // 战争结束(WarPatch 在开战目标结算之后调用)：帝国战争、执行禁令的战争、宗教战争、护佑之战、诸侯私战
#if EC_PENDING
    public static void OnWarEnded(War war, WarWinner winner)
    {
        try
        {
            if (war?.data == null || ModClass.EMPIRE_MANAGER == null) return;
            long id = war.data.id;
            foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
            {
                HolyRomanState state = empire?.data?.holy_roman;
                if (state == null || empire.IsGone() || empire.IsArchived()) continue;
                if (FeudRecord(empire, id) != null)
                {
                    SettleFeud(empire, state, war, winner);
                    continue;
                }
                if (id == state.anti_king_war_id)
                {
                    SettleContest(empire, state, winner);
                    continue;
                }
                if (id != state.imperial_war_id && id != state.ban_war_id && id != state.league_war_id &&
                    state.protect_wars?.ContainsKey(id) != true) continue;
                (int attackerGain, int defenderGain) = WarGoalSystem.Gains(war);
                bool attackersWon = winner == WarWinner.Attackers || winner == WarWinner.Peace && attackerGain >= defenderGain + 1;
                bool defendersWon = winner == WarWinner.Defenders || winner == WarWinner.Peace && defenderGain >= attackerGain + 1;
                if (id == state.imperial_war_id)
                {
                    bool coreAttacking = war._list_attackers.Contains(empire.CoreKingdom) || war.main_attacker == empire.CoreKingdom;
                    bool won = coreAttacking ? attackersWon : defendersWon;
                    bool lost = coreAttacking ? defendersWon : attackersWon;
                    if (won) empire.AddMandate(ImperialWarWin);
                    if (lost) empire.AddMandate(ImperialWarLoss);
                    if (won) ChangeAuthority(empire, AuthImperialWarWin, "hre_auth_imperial_war_won", war.data.name);
                    if (lost) ChangeAuthority(empire, AuthImperialWarLoss, "hre_auth_imperial_war_lost", war.data.name);
                    Record(empire, string.Format(LM.Get(won ? "hre_imperial_war_won_history" : lost ? "hre_imperial_war_lost_history"
                        : "hre_imperial_war_peace_history"), war.data.name));
                    state.imperial_war_id = -1L;
                    state.imperial_war_members?.Clear();
                }
                if (id == state.ban_war_id)
                {
                    state.ban_war_id = -1L;
                    state.year_turmoil = true;
                    if (attackersWon)
                    {
                        empire.AddMandate(BanLegitimacyWin);
                        ChangeAuthority(empire, AuthBanExecuted, "hre_auth_ban_executed", state.banned_name);
                        // 禁令执行成功：褫夺被禁者的选帝侯位(权威不到 10 只停权)
                        StripElector(empire, state, state.banned_kingdom_id, auto: true);
                        LiftBan(empire, state, LM.Get("hre_ban_executed_history"));
                    }
                    else if (defendersWon)
                    {
                        empire.AddMandate(BanLegitimacyLoss);
                        ChangeAuthority(empire, AuthBanFailed, "hre_auth_ban_failed", state.banned_name);
                        LiftBan(empire, state, LM.Get("hre_ban_failed_history"));
                    }
                }
                if (id == state.league_war_id) OnLeagueWarEnded(empire, state, war, attackersWon, defendersWon);
                SettleProtection(empire, state, war, defendersWon, attackerGain, winner);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 战争结算失败: {exception.Message}");
        }
    }
#endif

    #endregion

    #region 上帝视角

    // 帝国议会页的按钮：立即召开(按当前最急的议程，没有就议摊派)、对最不听话的成员国提请禁令、解除禁令、颁行帝国改革；
    // 神力按钮照扣帝国权威，扣到 0 为止，不因权威不够拦下
#if EC_PENDING
    public static DietRecord ActConvene(Empire empire)
    {
        if (!HasDiet(empire)) return null;
        (string agenda, Realm subject) = PickAgenda(empire, State(empire));
        return Convene(empire, agenda ?? AgendaLevy, subject);
    }

    public static DietRecord ActProposeBan(Empire empire)
    {
        if (!HasDiet(empire)) return null;
        HolyRomanState state = State(empire);
        if (state.banned_kingdom_id >= 0) return null;
        Realm core = empire.CoreKingdom;
        Realm target = Members(empire).Where(member => !IsFreeCity(member))
            .OrderBy(member => state.defiance != null && state.defiance.ContainsKey(member.Key) ? 0 : 1)
            .ThenBy(member => Opinion(member, core)).ThenByDescending(member => member.Cities.Count).FirstOrDefault();
        return target == null ? null : Convene(empire, AgendaBan, target);
    }
#endif

    // 颁行帝国改革：文化节点已施行、本帝国未颁行时径付议会(权威不够照扣到 0)
#if EC_PENDING
    public static DietRecord ActEnactReform(Empire empire)
    {
        if (!HasDiet(empire) || ReformEnacted(empire) || !ReformNodeEnacted(empire)) return null;
        return Convene(empire, AgendaReform, null);
    }

    public static bool ActLiftBan(Empire empire)
    {
        HolyRomanState state = State(empire);
        if (state == null || state.banned_kingdom_id < 0) return false;
        LiftBan(empire, state, LM.Get("hre_ban_lifted_history"));
        return true;
    }
#endif

    #endregion
}
}
