// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CourtSystem.Year.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Random = EmpireCraft.Core.EcRandom;
using System;
using System.Collections.Generic;
using System.Linq;
#if EC_PENDING
using Random = UnityEngine.Random;
#endif

namespace EmpireCraft.Core
{

// 朝堂博弈的年度结算：政治资本入账、圣眷与怨望回落、皇帝定方略、各派各出一招、怨望过高者兵谏，
// 以及内阁的任免与吏治。
public static partial class CourtSystem
{
    private const float ImpeachCost = 20f;
    private const float ObstructCost = 12f;
    private const float FavorCost = 10f;
    private const float AllyCost = 8f;
    private const float RecruitCost = 15f;
    // 罢朝每年失圣眷(用户 2026-10-17 "既然怨气大为什么不造反"，选"罢朝逐年失圣眷，终至兵谏造反")：
    // 以前罢朝的派系不出招，圣眷停在 50、永远够不着兵谏的 25；现在约六年降到 25 以下
    private const float BoycottFavorLoss = 6f;

#if EC_PENDING
    private static void YearlyIfDue(Empire empire, CourtState state, double now)
    {
        if (state.last_year >= 0d && now >= state.last_year && GameClock.YearsSince(state.last_year) < 1) return;
        bool first = state.last_year < 0d;
        state.last_year = now;
        List<FixedFaction> factions = Factions(empire);
        var ids = new HashSet<string>(factions.Select(faction => faction.GetID()));
        foreach (string stale in state.factions.Keys.Where(id => !ids.Contains(id)).ToList()) state.factions.Remove(stale);
        foreach (FixedFaction faction in factions) FactionState(empire, faction);
        if (first) return;

        List<Person> council = Council(empire);
        foreach (FixedFaction faction in factions) Income(empire, faction, council);
        // 帝位虚悬：没有可罢的朝、可邀的宠，各派不出招(虚悬久了无人可立就降为王国，见 EmpireCraftKingdomBehCheckKing)
        if (empire.Emperor == null) return;
        UpdateStrategy(empire, state, factions);
        if (CouncilKind(empire) == CourtCouncilKind.Cabinet) ReshuffleCabinet(empire, state);
        foreach (FixedFaction faction in factions.OrderBy(_ => Random.value))
            TakeAction(empire, state, faction, factions, Council(empire));
        AdvanceCrisis(empire, state, factions);
        Escalate(empire, state, factions);
        if (CouncilKind(empire) == CourtCouncilKind.Cabinet) CabinetAdministration(empire);
    }
#endif

    #region 资本、圣眷、怨望

    private static void Income(Empire empire, FixedFaction faction, List<Person> council)
    {
        CourtFactionState data = FactionState(empire, faction);
        int seats = council.Count(actor => actor.GetFaction() == faction);
        int officials = faction.AllMembers.Count(actor => actor != null && !actor.IsGone() && actor.IsOnOffice());
        float income = 3f + seats * 3f + Mathf.Min(8f, officials * 0.5f) + faction.CentralRatio / 10f +
                       Mathf.Clamp(faction.ClassPower / 20f, 0f, 3f);
        data.capital = Mathf.Min(100f, data.capital + income);
        data.favor += (50f - data.favor) * 0.1f;
        data.grievance = Mathf.Clamp(data.grievance + CourtCrisisRules.GrievanceChange(empire.Legitimacy,
            IsBoycotting(data), data.favor), 0f, 100f);
        data.obstruction = 0f;
        data.last_action = "";
        data.last_action_detail = "";
        if (string.IsNullOrEmpty(data.ally)) return;
        FixedFaction partner = FindFaction(empire, data.ally);
        if (partner == null || partner.CentralRatio > 55 || faction.CentralRatio > 55)
        {
            if (partner != null) FactionState(empire, partner).ally = "";
            data.ally = "";
        }
    }

    // 皇帝方略：一派独大则制衡；威权不足又有宠信的派系则偏宠；有派系怨望过高而皇帝威权足则打压；否则亲政
    private static (string strategy, string target) ChooseStrategy(Empire empire, List<FixedFaction> factions)
    {
        (Person _, string role) = Decider(empire);
        // 批红不出于皇帝(幼主、被挟制、昏聩怠政)才是摄政；皇帝亲政而朝中没有派系的，照旧算亲政(以前也记成摄政)
        if (role != "emperor") return ("regency", "");
        if (factions.Count == 0 || empire.Emperor == null) return ("personal", "");
        int legitimacy = empire.Legitimacy;
        FixedFaction top = factions.OrderByDescending(faction => faction.CentralRatio).First();
        FixedFaction darling = factions.Where(faction => FactionState(empire, faction).favor >= 75f)
            .OrderByDescending(faction => FactionState(empire, faction).favor).FirstOrDefault();
        FixedFaction angry = factions.Where(faction => FactionState(empire, faction).grievance >= 70f)
            .OrderByDescending(faction => FactionState(empire, faction).grievance).FirstOrDefault();
        if (top.CentralRatio >= 55) return ("balance", top.GetID());
        if (darling != null && legitimacy < 60) return ("favorite", darling.GetID());
        if (angry != null && (legitimacy >= 70 || empire.Emperor.HasTrait("paranoid"))) return ("suppress", angry.GetID());
        return ("personal", "");
    }

    private static void SetStrategy(Empire empire, CourtState state, string strategy, string target)
    {
        if (strategy != state.strategy || target != state.strategy_target)
            Log(state, "court_log_strategy_" + strategy, FindFaction(empire, target)?.Name ?? "");
        state.strategy = strategy;
        state.strategy_target = target;
    }

    // 批红的人变了(幼主成年亲政、新君即位、权臣倒台，或反过来皇帝失势)：方略当月改定，不等年度结算。
    // 以前方略一年才定一次，成年的皇帝亲政后还挂着"摄政"，最长一年(年度结算那时帝位虚悬就更久)。
    // 只改方略本身，圣眷的增减仍按年度结算
    private static void RefreshStrategyOnRuleChange(Empire empire, CourtState state)
    {
        if (string.IsNullOrEmpty(state.strategy) || empire.Emperor == null) return;
        bool emperorRules = Decider(empire).role == "emperor";
        if (emperorRules == (state.strategy != "regency")) return;
        (string strategy, string target) = ChooseStrategy(empire, Factions(empire));
        SetStrategy(empire, state, strategy, target);
    }

    private static void UpdateStrategy(Empire empire, CourtState state, List<FixedFaction> factions)
    {
        (string strategy, string target) = ChooseStrategy(empire, factions);
        SetStrategy(empire, state, strategy, target);
        foreach (FixedFaction faction in factions)
        {
            CourtFactionState data = FactionState(empire, faction);
            bool isTarget = faction.GetID() == target;
            switch (strategy)
            {
                case "balance":
                    data.favor += isTarget ? -4f : 2f;
                    break;
                case "favorite":
                    data.favor += isTarget ? 3f : -1f;
                    break;
                case "suppress":
                    data.favor += isTarget ? -6f : 1f;
                    if (isTarget) data.grievance += 6f;
                    break;
            }
            data.favor = Mathf.Clamp(data.favor, 0f, 100f);
            data.grievance = Mathf.Clamp(data.grievance, 0f, 100f);
        }
    }

    #endregion

    #region 派系动作

    // 每派每年一招：罢朝 / 掣肘 / 弹劾 / 邀宠 / 结盟 / 拉拢，都没条件就蓄势。
    // 邀宠、结盟、拉拢要给上奏留出本钱
#if EC_PENDING
    private static void TakeAction(Empire empire, CourtState state, FixedFaction faction, List<FixedFaction> factions,
        List<Person> council)
    {
        CourtFactionState data = FactionState(empire, faction);
        if (IsBoycotting(data))
        {
            data.favor = Mathf.Max(0f, data.favor - BoycottFavorLoss);
            Act(data, "boycott", "");
            Log(state, "court_log_boycott", faction.Name);
            return;
        }
        CourtMemorial memorial = state.memorial;
        if (memorial?.stage == 4 && memorial.stances.TryGetValue(faction.GetID(), out int stance) && stance < 0 &&
            data.capital >= ObstructCost)
        {
            data.capital -= ObstructCost;
            data.obstruction = Mathf.Min(0.6f, data.obstruction + 0.3f);
            Act(data, "obstruct", IssueName(memorial.claim, empire));
            Log(state, "court_log_obstruct", faction.Name, IssueName(memorial.claim, empire));
            return;
        }
        if (data.capital >= ImpeachCost && (data.grievance >= 25f || Random.value < 0.25f) &&
            TryImpeach(empire, state, faction, data, council))
            return;
        if (data.favor < 45f && data.capital >= FavorCost + ProposalCost)
        {
            data.capital -= FavorCost;
            data.favor = Mathf.Min(100f, data.favor + 8f);
            Act(data, "favor", "");
            Log(state, "court_log_favor", faction.Name);
            return;
        }
        if (string.IsNullOrEmpty(data.ally) && data.capital >= AllyCost + ProposalCost &&
            TryAlly(empire, state, faction, data, factions))
            return;
        if (data.capital >= RecruitCost + ProposalCost && Random.value < 0.6f)
        {
            data.capital -= RecruitCost;
            empire.CoreKingdom.TryIncreaseFactionRatio(faction, 3);
            Act(data, "recruit", "");
            Log(state, "court_log_recruit", faction.Name);
            return;
        }
        Act(data, "save", "");
    }
#endif

    private static void Act(CourtFactionState data, string action, string detail)
    {
        data.last_action = action;
        data.last_action_detail = detail ?? "";
    }

    // 弹劾对手：内阁制弹劾别派阁臣，其他朝廷弹劾别派的中央官员。罪证(贪腐、近年财政案)越多越容易成，失败反坐
    private static bool TryImpeach(Empire empire, CourtState state, FixedFaction faction, CourtFactionState data,
        List<Person> council)
    {
        bool cabinet = CouncilKind(empire) == CourtCouncilKind.Cabinet;
        var targets = new List<(Person actor, OfficeObject office)>();
        if (cabinet)
        {
            targets.AddRange(council.Select(actor => (actor, (OfficeObject)null)));
        }
        else if (empire.data.centerOffice != null)
        {
            foreach (long id in (empire.data.centerOffice.CoreOffices ?? new List<long>())
                     .Concat(empire.data.centerOffice.Divisions ?? new List<long>()))
            {
                if (!OfficeManager.Offices.TryGetValue(id, out OfficeObject office)) continue;
                Person holder = office.GetActor();
                if (holder != null) targets.Add((holder, office));
            }
        }
        (Person actor, OfficeObject office) target = targets
            .Where(item => item.actor?.Record != null && !item.actor.IsGone() && item.actor != empire.Emperor)
            .Where(item =>
            {
                FixedFaction rival = item.actor.GetFaction();
                return rival != null && rival != faction && rival.GetID() != data.ally;
            })
            .OrderByDescending(item => (cabinet && council.FirstOrDefault() == item.actor ? 2f : 1f) *
                                       (0.5f + (float)item.actor.CalcCorruptionValue()) *
                                       Mathf.Max(5, item.actor.GetFaction().CentralRatio))
            .FirstOrDefault();
        if (target.actor == null) return false;
        FixedFaction rivalFaction = target.actor.GetFaction();
        CourtFactionState rivalData = FactionState(empire, rivalFaction);
        double crime = target.actor.GetOrCreate().last_fiscal_corruption_crime_timestamp;
        bool evidence = crime >= 0d && GameClock.YearsSince(crime) < 5;
        float chance = 0.3f + (float)target.actor.CalcCorruptionValue() * 0.4f + (evidence ? 0.2f : 0f) +
                       (data.favor - rivalData.favor) / 200f;
        // 秦汉：御史大夫监察百官，他所属的派系弹劾更容易成
        if (CouncilKind(empire) == CourtCouncilKind.Han && DepartmentHead(empire, CensorOffice)?.GetFaction() == faction)
            chance += 0.2f;
        // 个人恩怨(见 PersonalTieSystem)：门生在朝替恩主说话，每人难成一成(至多两成)；弹劾的是宿敌易成一成五
        Person impeacher = faction.GetLeader();
        chance -= 0.1f * Mathf.Min(2, PersonalTieSystem.ProtegesInOffice(target.actor));
        if (impeacher != null && PersonalTieSystem.AreRivals(impeacher, target.actor)) chance += 0.15f;
        data.capital -= ImpeachCost;
        string name = target.actor.FullName();
        if (Random.value < Mathf.Clamp(chance, 0.05f, 0.9f))
        {
            if (cabinet) empire.RemoveCabinetMember(target.actor);
            else target.office?.RemoveActor();
            rivalData.grievance = Mathf.Min(100f, rivalData.grievance + 8f);
            rivalData.favor = Mathf.Max(0f, rivalData.favor - 4f);
            target.actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_impeached"), faction.Name));
            if (impeacher != null) PersonalTieSystem.MakeRivals(target.actor, impeacher, PersonalTieSystem.CauseImpeached);
            Act(data, "impeach", name);
            Log(state, "court_log_impeach_success", faction.Name, rivalFaction.Name, name);
        }
        else
        {
            data.favor = Mathf.Max(0f, data.favor - 8f);
            data.grievance = Mathf.Min(100f, data.grievance + 4f);
            Act(data, "impeach_failed", name);
            Log(state, "court_log_impeach_failed", faction.Name, rivalFaction.Name, name);
        }
        return true;
    }

    // 结盟：找立场相近(各阶层亲和度方向一致；朋党还看利义议题，见 CourtDoctrine)的派系，都不能一家独大
    private static bool TryAlly(Empire empire, CourtState state, FixedFaction faction, CourtFactionState data,
        List<FixedFaction> factions)
    {
        if (faction.CentralRatio > 55) return false;
        FixedFaction partner = factions
            .Where(other => other != faction && other.CentralRatio <= 55 &&
                            string.IsNullOrEmpty(FactionState(empire, other).ally))
            .Select(other => (other, similarity: Similarity(faction, other)))
            .Where(item => item.similarity >= 0.35f)
            .OrderByDescending(item => item.similarity).Select(item => item.other).FirstOrDefault();
        if (partner == null) return false;
        data.capital -= AllyCost;
        data.ally = partner.GetID();
        FactionState(empire, partner).ally = faction.GetID();
        Act(data, "ally", partner.Name);
        Log(state, "court_log_ally", faction.Name, partner.Name);
        return true;
    }

    public static float Similarity(FixedFaction a, FixedFaction b)
    {
        FactionClassSystem.EnsureProfile(a);
        FactionClassSystem.EnsureProfile(b);
        float dot = 0f, na = 0f, nb = 0f;
        foreach (SocialClass socialClass in Enum.GetValues(typeof(SocialClass)))
        {
            a.ClassAffinities.TryGetValue(socialClass, out float x);
            b.ClassAffinities.TryGetValue(socialClass, out float y);
            dot += x * y;
            na += x * x;
            nb += y * y;
        }
        float classes = na <= 0f || nb <= 0f ? 0f : dot / Mathf.Sqrt(na * nb);
        // 朋党：一半看阶层亲疏，一半看利义议题上是否同进退(变法派与天子派同主削藩、清流与阉党势同水火)
        float? agreement = CourtDoctrine.Agreement(a, b);
        return agreement == null ? classes : classes * 0.5f + agreement.Value * 0.5f;
    }

    // 兵谏：怨望极高、圣眷尽失、又有成员国君主做本钱的派系起兵(派系叛乱)；每年最多一起。
    // 没有成员国可起兵(只剩京畿)的：在都城逼宫，废今上、另立宗室(见 Empire.CompleteFactionDeposition)
#if EC_PENDING
    private static void Escalate(Empire empire, CourtState state, List<FixedFaction> factions)
    {
        Realm core = empire.CoreKingdom;
        if (empire.Emperor == null || core == null || !RebellionSystem.CanAttempt(core)) return;
        double now = GameClock.Now;
        if (state.last_escalation >= 0d && now >= state.last_escalation && GameClock.YearsSince(state.last_escalation) < 5)
            return;
        if (core.GetWars().Any(war => war != null && !war.HasEnded() &&
                war.GetEmpireWarType() is EmpireWarType.派系叛乱 or EmpireWarType.清君侧)) return;
        if (TryCoalition(empire, state, factions)) return;
        foreach (FixedFaction faction in factions.OrderByDescending(faction => FactionState(empire, faction).grievance))
        {
            CourtFactionState data = FactionState(empire, faction);
            if (data.grievance < 90f || data.favor > 25f || faction.CentralRatio < 20) continue;
            Realm rebel = empire.kingdoms_list
                .Where(kingdom => kingdom != null && !kingdom.IsGone() && kingdom != core &&
                                  kingdom.Ruler?.GetFaction() == faction && RebellionSystem.CanAttempt(kingdom) &&
                                  kingdom.Capital != null && CityStabilitySystem.CanRise(kingdom.Capital) &&
                                  !kingdom.IsFactionRebelling() && !kingdom.GetWars().Any())
                .OrderByDescending(kingdom => kingdom.Warriors()).FirstOrDefault();
            if (rebel == null)
            {
                // 只剩京畿(没有成员国)才在都城逼宫；有成员国只是眼下起不了兵的，等以后
                if (empire.kingdoms_list.Any(kingdom => kingdom != null && !kingdom.IsGone() && kingdom != core)) continue;
                ConstitutionalEconomyState economy = empire.data.constitutional_economy;
                if (economy?.last_coup_at >= 0d && now >= economy.last_coup_at && GameClock.YearsSince(economy.last_coup_at) < 20)
                    continue;
                Person installed = empire.CompleteFactionDeposition(faction);
                if (installed == null) continue;
                if (economy != null) economy.last_coup_at = now;
                state.last_escalation = now;
                data.grievance = 50f;
                data.favor = 60f;
                Act(data, "palace_coup", installed.FullName());
                Log(state, "court_log_palace_coup", faction.Name);
                return;
            }
            RebellionCauseData cause = RebellionSystem.Capture(rebel, rebel.Capital, "rebellion_reason_court", faction.Name);
            rebel.StartFactionRebelling(faction);
            War war = EmpireWorld.Current.StartWar(rebel, core, WarKinds.Normal);
            if (war == null)
            {
                rebel.EndFactionRebelling();
                continue;
            }
            war.SetEmpireWarType(EmpireWarType.派系叛乱);
            // 战争按首领起名(见 FactionRebelNaming)
            string warName = FactionRebelNaming.WarName(new[] { rebel });
            if (!string.IsNullOrWhiteSpace(warName)) war.Name = warName;
            RebellionSystem.Record(rebel, war, cause);
            // 郡县多不许养兵，起兵时就地武装义军(见 RebellionStartupService)，按怨望定规模
            RebellionStartupService.RaiseUprisingMilitia(rebel, data.grievance / 100f);
            data.grievance = 50f;
            state.last_escalation = now;
            Act(data, "mutiny", rebel.GetKingdomName());
            Log(state, "court_log_mutiny", faction.Name, rebel.GetKingdomName());
            empire.RecordHistory(directContent: string.Format(ModernWording.Get(empire, "court_history_mutiny"), faction.Name,
                rebel.GetKingdomName()), kingdomId: core.Key);
            return;
        }
    }
#endif

    #endregion

    #region 内阁

    // 内阁由皇帝任命(幼主、傀儡时由权臣/议程官按同样规则补缺)：人数随皇帝治理能力，
    // 缺额按"才干 + 所属派系圣眷 + 方略"取人；首辅出缺由得分最高的阁臣接任。
    public static void MaintainCabinet(Empire empire)
    {
        if (empire?.CoreKingdom?.GetRegime() == null) return;
        EnsureBureau(empire);
        // 周、汉、唐的朝廷不设内阁(内阁制施行前)：旧档里已有的阁臣出阁
        if (CouncilKind(empire) != CourtCouncilKind.Cabinet)
        {
            foreach (Person actor in empire.GetCabinetMembers().ToList()) empire.RemoveCabinetMember(actor);
            return;
        }
        int size = CabinetSize(empire);
        List<Person> members = empire.GetCabinetMembers();
        foreach (Person actor in members.Where(actor => !actor.HasOfficeIdentity()).ToList())
        {
            empire.RemoveCabinetMember(actor);
            members.Remove(actor);
        }
        while (members.Count > size)
        {
            Person drop = members.Skip(1).OrderBy(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            if (drop == null) break;
            empire.RemoveCabinetMember(drop);
            members.Remove(drop);
        }
        while (members.Count < size)
        {
            Person pick = CabinetCandidates(empire).Where(actor => !members.Contains(actor))
                .OrderByDescending(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            if (pick == null || !empire.AddCabinetMember(pick)) break;
            members.Add(pick);
            Log(State(empire), "court_log_appointed", pick.FullName(), pick.GetFaction()?.Name ?? LM.Get("label_none"));
        }
        Person leader = empire.GetCabinetLeader();
        if (leader == null || !members.Contains(leader))
        {
            Person best = members.OrderByDescending(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            if (best != null && empire.SetCabinetLeader(best))
                Log(State(empire), "court_log_new_leader", best.FullName(), best.GetFaction()?.Name ?? LM.Get("label_none"));
        }
    }

    // 一年一次按方略换一人：制衡时拆掉占过半阁席的派系的一席，偏宠时给宠信派系一席
    private static void ReshuffleCabinet(Empire empire, CourtState state)
    {
        List<Person> members = empire.GetCabinetMembers();
        if (members.Count < 2) return;
        int size = members.Count;
        Person drop = null;
        Func<Person, bool> wanted = null;
        if (state.strategy == "balance")
        {
            IGrouping<FixedFaction, Person> dominant = members.Where(actor => actor.GetFaction() != null)
                .GroupBy(actor => actor.GetFaction()).OrderByDescending(group => group.Count()).FirstOrDefault();
            if (dominant == null || dominant.Count() * 2 <= size) return;
            drop = dominant.Where(actor => actor != members[0])
                .OrderBy(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            wanted = actor => actor.GetFaction() != dominant.Key;
        }
        else if (state.strategy == "favorite")
        {
            if (members.Any(actor => actor.GetFaction()?.GetID() == state.strategy_target)) return;
            drop = members.Skip(1).OrderBy(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
            wanted = actor => actor.GetFaction()?.GetID() == state.strategy_target;
        }
        if (drop == null || wanted == null) return;
        Person pick = CabinetCandidates(empire).Where(actor => !members.Contains(actor) && wanted(actor))
            .OrderByDescending(actor => AppointScore(empire, actor, members, size)).FirstOrDefault();
        if (pick == null) return;
        empire.RemoveCabinetMember(drop);
        if (!empire.AddCabinetMember(pick)) return;
        Log(state, "court_log_reshuffle", drop.FullName(), pick.FullName(), pick.GetFaction()?.Name ?? LM.Get("label_none"));
    }

    // 阁席数随皇帝治理能力(3~15 线性对应 1~满员)
    public static int CabinetSize(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        if (regime == null) return 0;
        int stewardship = empire.Emperor?.Stewardship ?? 0;
        if (stewardship < 3) stewardship = 0;
        if (stewardship > 15) stewardship = 15;
        return Mathf.Max(1, 1 + (stewardship * regime.cabinet_number - 1) / 15);
    }

    public static IEnumerable<Person> CabinetCandidates(Empire empire)
    {
        Realm core = empire.CoreKingdom;
        IEnumerable<Person> pool = (core.GetRegime()?.GetAllFactionMembers() ?? new List<Person>())
            .Concat(core.People?.ToList() ?? new List<Person>());
        return pool.Where(actor => actor?.Record != null && !actor.IsGone() && actor.Alive && actor.IsAdult() &&
                                   actor != empire.Emperor && !actor.IsWarMachine() && actor.HasOfficeIdentity() &&
                                   actor.CanServeOffice(core))
            .Distinct();
    }

    public static float Competence(Person actor) => actor == null ? 0f : actor.Stewardship + actor.Intelligence;

    public static float AppointScore(Empire empire, Person actor, List<Person> members, int size)
    {
        CourtState state = State(empire);
        FixedFaction faction = actor.GetFaction();
        float favor = faction == null ? 50f : FactionState(empire, faction).favor;
        float score = Competence(actor) + (favor - 50f) / 5f;
        // 外戚得势时(皇帝庸弱昏聩、或年轻而有太后撑腰)外家子弟优先入阁(见 ConsortKinSystem)
        if (ConsortKinSystem.Backed(empire) && ConsortKinSystem.IsConsortKin(empire, actor)) score += 12f;
        string id = faction?.GetID() ?? "";
        switch (state.strategy)
        {
            case "balance":
                if (faction != null && !members.Contains(actor) &&
                    members.Count(member => member.GetFaction() == faction) * 2 >= size)
                    score -= 8f;
                if (id == state.strategy_target) score -= 6f;
                break;
            case "favorite":
                if (id == state.strategy_target) score += 10f;
                break;
            case "suppress":
                if (id == state.strategy_target) score -= 15f;
                break;
        }
        return score;
    }

    // 内阁吏治：阁臣才干均值高于 20 时逐年压低中央腐败，低于时推高(每年最多 ±2%)
    public static float CabinetEfficiency(Empire empire)
    {
        List<Person> members = Council(empire);
        if (members.Count == 0) return 0f;
        return Mathf.Clamp((members.Average(Competence) - 20f) / 20f, -1f, 1f);
    }

    private static void CabinetAdministration(Empire empire)
    {
        float efficiency = CabinetEfficiency(empire);
        if (Mathf.Abs(efficiency) > 0.01f) empire.CoreKingdom.AddCorruptionRate(-0.02d * efficiency);
    }

    #endregion
}
}
