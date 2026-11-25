// Generated from the EmpireCraft mod: Scripts/GeneralSystems/SuccessionCouncilSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Color = EmpireCraft.Core.EcColor;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 议立新君(朝堂奏议)：皇帝驾崩，身后没有子孙、生前未立储君时，皇位不再按亲疏直接落到某位宗亲头上，
// 而是由朝廷议立——
//   ① 开列人选：议程官(首辅、丞相、中书令)召集群臣，从宗室里开列立亲(血缘最近)、立长(年长之君)、
//      立贤(最有声望)、立幼(年幼的近支)几位；
//   ② 廷议：各派按自己的盘算拥立——尊王、血脉看亲疏，文臣要长君贤君，权臣所在的一派想立幼主好把持朝政；
//      本派的人优先；
//   ③ 定策：太后降懿旨，没有太后由权臣、再没有由议程官拍板，多半从廷议多数，也会偏向对自己有利的人；
//   ④ 迎立：定下的人入继大统(继承人就位，下一次王位检查登基)。
// 皇族已无可立之人：天下无主、各地散伙，京城由权臣据而称帝(见 InterregnumSystem)；
// 周制天下、原始部落联盟等不裂土的群臣劝进——权臣、重臣，或由尊王、血脉派拥立一位宗室远支，劝进外姓即改朝换代，定策后直接即位。
// 每步隔一个月，空位约两个月。有议会、共和、选帝制、宪制保护皇位的帝国不走这条路，照继承法递补。
public static class SuccessionCouncilSystem
{
    public const string Elder = "elder", Kin = "kin", Worthy = "worthy", Young = "young",
        Cadet = "cadet", Minister = "minister", Acclaim = "acclaim", Commoner = "commoner", Union = "union";
    private const int StageMonths = 1, StaleMonths = 6, MaxAttempts = 3, AdultAge = 18, EnthroneMonths = 2;

#if EC_PENDING
    public static bool Applies(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || !kingdom.IsEmpire()) return false;
        Empire empire = kingdom.GetEmpire();
        Regime regime = kingdom.GetRegime();
        return empire?.data != null && regime != null && RegimeManager.IsMonarchy(regime.type) &&
               CourtSystem.Applies(empire) && !RepublicSystem.IsRepublic(empire) &&
               !HolyRomanSystem.IsElective(empire) && !ConstitutionalSuccessionSystem.ThroneBound(empire);
    }
#endif

    public static CourtSuccession Current(Empire empire) => empire?.data?.court?.succession;

    private static bool Vacant(Empire empire)
    {
        Person emperor = empire?.Emperor;
        return emperor?.Record == null || emperor.IsGone() || !emperor.Alive;
    }

    // 王位空缺时由继承检查调用(EmpireCraftKingdomBehCheckHeir.ResolveHeir)：议立未完返回空(皇位暂缺)，
    // 定策后返回迎立的人；handled 为假(屡次迎立不成)时照原来的继承顺序递补
#if EC_PENDING
    public static (Person actor, string relation, bool handled) Resolve(Realm core, PersonalClanIdentity predecessor)
    {
        Empire empire = core.GetEmpire();
        CourtState state = CourtSystem.State(empire);
        if (state == null || predecessor == null || EmpireWorld.Current == null) return (null, "", false);
        CourtSuccession council = state.succession;
        if (council == null || council.predecessor != predecessor.id)
        {
            state.succession = new CourtSuccession
            {
                predecessor = predecessor.id, stage = -1, stage_time = GameClock.Now
            };
            return (null, "", true);
        }
        if (council.gave_up) return (null, "", false);
        if (council.stage < 2) return (null, "", true);
        Person chosen = EmpireWorld.Current.PersonByKey(council.chosen_actor);
        if (chosen?.Record != null && !chosen.IsGone() && chosen.Alive && !EmpireCraftKingdomBehCheckHeir.BarredBySex(core, chosen))
            return (chosen, LM.Get("court_succession_relation").ColorString(pColor: new Color(0.95f, 0.76f, 0.29f)), true);
        // 迎立的人没等到登基就死了，或是不立女主的国定了女子(旧档)：重新议立
        council.attempts++;
        council.gave_up = council.attempts >= MaxAttempts;
        council.stage = -1;
        council.chosen = -1;
        council.chosen_actor = -1L;
        return (null, "", !council.gave_up);
    }
#endif

    // 议立期间不从虚拟族谱里直接落成继承人(见 EmpireCraftKingdomBehCheckKing)
#if EC_PENDING
    public static bool InSession(Realm kingdom)
    {
        if (!Applies(kingdom)) return false;
        Empire empire = kingdom.GetEmpire();
        CourtSuccession council = Current(empire);
        return council != null && !council.gave_up && Vacant(empire);
    }
#endif

    #region 每月推进(RoyalHouseSystem.Tick)

#if EC_PENDING
    public static void Monthly()
    {
        if (ModClass.EMPIRE_MANAGER == null || EmpireWorld.Current == null) return;
        double now = GameClock.Now;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
        {
            CourtState state = empire?.data?.court;
            CourtSuccession council = state?.succession;
            if (council == null) continue;
            if (!Vacant(empire) || !Applies(empire.CoreKingdom) || council.gave_up)
            {
                if (!Vacant(empire) || !Applies(empire.CoreKingdom)) state.succession = null;
                continue;
            }
            if (council.stage >= 2)
            {
                // 定了人两个月还没登基(玩家截图：迎立外姓重臣入继大统，却一直没有皇帝上位)：直接扶上去
                Person chosen = EmpireWorld.Current.PersonByKey(council.chosen_actor);
                if (GameClock.MonthsSince(council.stage_time) >= EnthroneMonths && chosen?.Record != null && chosen.Alive &&
                    !chosen.IsGone() && !EmpireCraftKingdomBehCheckHeir.BarredBySex(empire.CoreKingdom, chosen) &&
                    empire.InstallHeadOfState(chosen))
                {
                    LogService.LogInfo($"[EmpireCraft][议立] {empire.GetEmpireFullName()} 定策后迟迟未登基，直接扶 {chosen.FullName()} 即位");
                    state.succession = null;
                    continue;
                }
                // 定了人却迟迟没能登基：作罢，照原来的继承顺序递补
                if (GameClock.MonthsSince(council.stage_time) >= StaleMonths) council.gave_up = true;
                continue;
            }
            if (council.stage >= 0 && GameClock.MonthsSince(council.stage_time) < StageMonths) continue;
            switch (council.stage)
            {
                case -1:
                    Open(empire, state, council, now);
                    break;
                case 0:
                    Debate(empire, state, council, now);
                    break;
                case 1:
                    Decide(empire, state, council, now, -1);
                    break;
            }
        }
    }
#endif

    #endregion

    #region ① 开列人选

#if EC_PENDING
    private static void Open(Empire empire, CourtState state, CourtSuccession council, double now)
    {
        Realm core = empire.CoreKingdom;
        PersonalClanIdentity predecessor = SpecificClanManager.getPerson(council.predecessor);
        council.candidates = new List<CourtSuccessionCandidate>();
        council.backing = new Dictionary<string, int>();
        council.weights = new Dictionary<string, float>();
        council.chosen = -1;
        council.chosen_actor = -1L;
        council.outsider = false;
        if (predecessor != null) Nominate(core, predecessor, council.candidates);
        if (council.candidates.Count == 0)
        {
            council.outsider = true;
            NominateOutsiders(empire, core, council.candidates);
            // 皇族绝嗣、天下无主：京城由权臣重臣(没有就京城官员)据而称帝，各地散伙(见 InterregnumSystem)
            CourtSuccessionCandidate first = council.candidates.FirstOrDefault(candidate => candidate.basis != Commoner) ??
                                             council.candidates.FirstOrDefault();
            Person strongman = first == null ? null : first.basis == Commoner ? Enthrone(empire, core, first) : CandidateActor(first);
            if (InterregnumSystem.TryFracture(empire, predecessor, strongman))
            {
                state.succession = null;
                return;
            }
        }
        // 分治的另一朝皇帝同出一宗：可迎立他兼承大统，两朝复归一统(见 EmpirePartitionSystem)
        if (EmpirePartitionSystem.CanUnite(empire, out Empire twin))
            council.candidates.Insert(0, new CourtSuccessionCandidate
            {
                actor = twin.Emperor.Key, name = twin.Emperor.FullName(), basis = Union, age = twin.Emperor.Age(),
                kinship = 4
            });
        if (council.candidates.Count == 0)
        {
            council.gave_up = true;
            return;
        }
        council.stage = 0;
        council.stage_time = now;
        Person setter = CourtSystem.AgendaSetter(empire) ?? empire.GetPowerfulMinister();
        string convener = setter?.FullName() ?? LM.Get("court_succession_convener");
        string names = string.Join("、", council.candidates.Select(Describe));
        string text = string.Format(LM.Get(council.outsider ? "court_succession_open_acclaim" : "court_succession_open"),
            convener, names);
        Publish(empire, state, text);
    }
#endif

    // 宗室人选：立亲、立长、立贤、立幼各一位(同一人不重复)
#if EC_PENDING
    private static void Nominate(Realm core, PersonalClanIdentity predecessor, List<CourtSuccessionCandidate> result)
    {
        List<(PersonalClanIdentity person, int kinship)> pool = EmpireCraftKingdomBehCheckHeir.CollateralPool(core, predecessor);
        if (pool.Count == 0) return;
        int nearest = pool.Min(item => item.kinship);
        var used = new HashSet<long>();
        void Add((PersonalClanIdentity person, int kinship) item, string basis)
        {
            if (item.person == null || !used.Add(item.person.id)) return;
            result.Add(new CourtSuccessionCandidate
            {
                identity = item.person.id, name = item.person._actor?.FullName() ?? item.person.name ?? "",
                basis = basis, age = item.person.age, kinship = item.kinship
            });
        }
        // 立亲：血缘最近的，近支里先挑成年、年长的
        Add(pool.Where(item => item.kinship == nearest).OrderByDescending(item => item.person.age >= AdultAge)
            .ThenByDescending(item => item.person.age).ThenBy(item => item.person.id).First(), Kin);
        // 立长：近支(最近的再往外两代以内)里年纪最大的成年人(六十以下)
        var elder = pool.Where(item => item.kinship <= nearest + 2 && item.person.age >= AdultAge && item.person.age <= 60)
            .OrderByDescending(item => item.person.age).ThenBy(item => item.kinship).FirstOrDefault();
        if (elder.person != null) Add(elder, Elder);
        // 立贤：近支里有声望的成年人(虚拟族人按并回人口时记下的声望折算，见 VirtualGenealogySystem.VirtualRenown)
        var worthy = pool.Where(item => item.kinship <= nearest + 3 && item.person.age >= AdultAge && Merit(item.person) > 0)
            .OrderByDescending(item => Merit(item.person)).FirstOrDefault();
        if (worthy.person != null) Add(worthy, Worthy);
        // 立幼：近支里五到十五岁的
        var young = pool.Where(item => item.kinship <= nearest + 2 && item.person.age >= 5 && item.person.age < 16)
            .OrderBy(item => item.kinship).ThenBy(item => item.person.age).FirstOrDefault();
        if (young.person != null) Add(young, Young);
    }
#endif

    private static int Merit(PersonalClanIdentity person)
    {
        if (KinRoster.Virtual(person)) return VirtualGenealogySystem.VirtualRenown(person);
        Person actor = person?.is_virtual == false ? person._actor : null;
        return actor?.Record == null || actor.IsGone() ? 0 : actor.Renown;
    }

    // 皇族无人可继：权臣、两位声望最高的重臣，尊王一派可拥立一位宗室远支(从都城人口里认一位，改用皇族的姓)
#if EC_PENDING
    private static void NominateOutsiders(Empire empire, Realm core, List<CourtSuccessionCandidate> result)
    {
        var used = new HashSet<long>();
        void Add(Person actor, string basis)
        {
            if (actor?.Record == null || actor.IsGone() || !actor.Alive || !actor.IsAdult() ||
                EmpireCraftKingdomBehCheckHeir.BarredBySex(core, actor) || !used.Add(actor.Key)) return;
            result.Add(new CourtSuccessionCandidate
            {
                actor = actor.Key, name = actor.FullName(), basis = basis, age = actor.Age(), kinship = 99
            });
        }
        Add(empire.GetPowerfulMinister(), Minister);
        foreach (Person actor in CourtSystem.Council(empire).Concat(empire.GetCabinetMembers())
                     .Where(actor => actor?.Record != null && !actor.IsGone() && actor.Alive && actor.IsAdult())
                     .OrderByDescending(actor => actor.Renown).Take(4))
        {
            if (result.Count(candidate => candidate.basis == Acclaim) >= 2) break;
            Add(actor, Acclaim);
        }
        // 不再从都城百姓里认一位"宗室远支"：宗室人选只从世系里真实的支脉开列(见 Nominate)，那里都没人就是真的绝嗣
        if (result.Count == 0 && core.Capital != null)
            result.Add(new CourtSuccessionCandidate
            {
                name = LM.Get("court_succession_commoner_name"), basis = Commoner, age = 0, kinship = 99
            });
    }
#endif

    #endregion

    #region ② 廷议

#if EC_PENDING
    private static void Debate(Empire empire, CourtState state, CourtSuccession council, double now)
    {
        List<FixedFaction> factions = CourtSystem.Factions(empire);
        Dictionary<FixedFaction, float> weights = CourtSystem.Weights(empire, factions);
        Person minister = empire.GetPowerfulMinister();
        council.backing.Clear();
        council.weights.Clear();
        var items = new List<string>();
        foreach (FixedFaction faction in factions)
        {
            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < council.candidates.Count; i++)
            {
                float score = Backing(faction, council.candidates[i], minister, council.outsider);
                if (score <= bestScore) continue;
                bestScore = score;
                best = i;
            }
            if (best < 0) continue;
            council.backing[faction.GetID()] = best;
            council.weights[faction.GetID()] = weights.TryGetValue(faction, out float weight) ? weight : 0f;
            items.Add(string.Format(LM.Get("court_succession_debate_item"), faction.Name, council.candidates[best].name));
        }
        council.stage = 1;
        council.stage_time = now;
        if (items.Count > 0)
            Publish(empire, state, string.Format(LM.Get("court_succession_debate"), string.Join("，", items)));
    }

    private static float Backing(FixedFaction faction, CourtSuccessionCandidate candidate, Person minister, bool council_outsider)
    {
        Person actor = CandidateActor(candidate);
        bool legitimist = faction.Type is FactionType.尊王 or FactionType.血脉;
        float score = Random.Range(0f, 8f);
        if (actor?.Record != null && actor.GetFaction() == faction) score += 25f;
        switch (candidate.basis)
        {
            case Union:
                // 另一朝皇帝同出一宗，按从兄弟一支算亲疏；本朝还有近支宗亲时多立近支，只剩远支或无人可立时才兼承大统
                return score - 4f * (legitimist ? 12f : 6f) + 20f + (council_outsider ? 40f : 0f);
            case Cadet:
                return score + (legitimist || faction.Type == FactionType.神权 ? 45f : 5f);
            case Commoner:
                return score;
            case Minister:
            case Acclaim:
                if (legitimist) score -= 20f;
                if (actor != null && CorpsSystem.IsGeneral(actor) && faction.Type is FactionType.攘夷 or FactionType.僭主) score += 20f;
                if (candidate.basis == Minister && minister?.GetFaction() == faction) score += 30f;
                return score + (actor?.Renown ?? 0) * 0.05f;
        }
        score -= Mathf.Min(candidate.kinship, 12) * (legitimist ? 12f : 6f);
        bool adult = candidate.age >= AdultAge;
        bool regencyFaction = minister?.Record != null && minister.GetFaction() == faction;
        if (adult)
            score += faction.Type switch
            {
                FactionType.绥靖 => 18f,
                FactionType.攘夷 or FactionType.僭主 => 15f,
                FactionType.诸侯 or FactionType.自治 => 6f,
                _ => 12f
            };
        else score += regencyFaction ? 40f : -8f;
        if (candidate.basis == Worthy) score += faction.Type == FactionType.绥靖 ? 15f : 6f;
        if (candidate.basis == Kin && legitimist) score += 6f;
        // 诸侯、自治派不想要强君：年长有为的扣一点
        if (adult && candidate.basis is Elder or Worthy && faction.Type is FactionType.诸侯 or FactionType.自治) score -= 6f;
        return score;
    }
#endif

    #endregion

    #region ③ 定策、④ 迎立

#if EC_PENDING
    public static (Person decider, string role) Decider(Empire empire)
    {
        Person dowager = empire?.GetEmpressDowagerForCouncil();
        if (dowager != null) return (dowager, "dowager");
        Person minister = empire?.GetPowerfulMinister();
        if (minister?.Record != null && !minister.IsGone() && minister.Alive) return (minister, "minister");
        Person setter = CourtSystem.AgendaSetter(empire);
        return setter?.Record != null && !setter.IsGone() && setter.Alive ? (setter, "regent") : (null, "court");
    }
#endif

    // 各人选的廷议支持率(0~100)
    public static float Support(CourtSuccession council, int index)
    {
        float total = council.weights.Values.Sum();
        if (total <= 0f) return 0f;
        return council.backing.Where(pair => pair.Value == index)
            .Sum(pair => council.weights.TryGetValue(pair.Key, out float weight) ? weight : 0f) / total * 100f;
    }

    // 定策；forced ≥ 0 时直接立这一位(神力钦定)
#if EC_PENDING
    private static void Decide(Empire empire, CourtState state, CourtSuccession council, double now, int forced)
    {
        Person decider = null;
        string role = "god";
        if (forced < 0) (decider, role) = Decider(empire);
        Person minister = empire.GetPowerfulMinister();
        bool ambitious = minister != null && (empire.data.powerful_minister_progress >= 20 || Random.value < 0.5f);
        bool benevolent = role == "dowager" && Random.value < 0.3f;
        string deciderFaction = decider?.GetFaction()?.GetID();
        var order = Enumerable.Range(0, council.candidates.Count).OrderByDescending(index =>
        {
            if (forced >= 0) return index == forced ? 1f : 0f;
            CourtSuccessionCandidate candidate = council.candidates[index];
            float score = Support(council, index) + Random.Range(0f, 10f);
            bool young = candidate.basis is Kin or Elder or Worthy or Young && candidate.age < AdultAge;
            if (candidate.basis == Union && role is "dowager" or "minister") score -= 15f;
            switch (role)
            {
                case "dowager":
                    // 立幼主便于太后临朝称制；贤后从众
                    if (!benevolent && young) score += 25f;
                    break;
                case "minister":
                    if (ambitious && young) score += 35f;
                    if (candidate.actor == decider?.Key) score += 60f;
                    break;
                case "regent":
                    if (deciderFaction != null && council.backing.TryGetValue(deciderFaction, out int backed) && backed == index)
                        score += 20f;
                    if (candidate.actor == decider?.Key) score += 30f;
                    break;
            }
            return score;
        }).ToList();
        Realm core = empire.CoreKingdom;
        foreach (int index in order)
        {
            CourtSuccessionCandidate candidate = council.candidates[index];
            if (candidate.basis == Union)
            {
                if (!EmpirePartitionSystem.CanUnite(empire, out Empire heirEmpire)) continue;
                string unionText = string.Format(LM.Get("court_succession_decided_" + role), decider?.FullName() ?? "",
                    heirEmpire.Emperor.FullName());
                Publish(empire, state, unionText);
                state.succession = null;
                EmpirePartitionSystem.Reunite(empire, heirEmpire);
                return;
            }
            Person actor = Enthrone(empire, core, candidate);
            // 不立女主的国不迎立女子(人选开列时已筛过，这里防旧档里开列好的名单和神力钦定)
            if (actor?.Record == null || actor.IsGone() || !actor.Alive ||
                EmpireCraftKingdomBehCheckHeir.BarredBySex(core, actor)) continue;
            council.chosen = index;
            council.chosen_actor = actor.Key;
            council.decider = decider?.FullName() ?? "";
            council.decider_role = role;
            council.stage = 2;
            council.stage_time = now;
            candidate.name = actor.FullName();
            PrinceStruggleSystem.Note(core, actor, council.outsider && candidate.basis != Cadet
                ? PrinceStruggleSystem.NoteAcclaimed : PrinceStruggleSystem.NoteCourtChoice);
            string text = string.Format(LM.Get("court_succession_decided_" + role), council.decider, actor.FullName());
            Publish(empire, state, text);
            // 劝进外姓：直接即位(以前立为继承人等王位检查，外姓重臣常常登不了基，皇位一直空着)
            if (council.outsider && candidate.basis != Cadet && empire.InstallHeadOfState(actor))
            {
                state.succession = null;
                return;
            }
            // 继承人就位，下一次王位检查即登基
            core.SetHeir(actor);
            return;
        }
        council.attempts++;
        council.gave_up = council.attempts >= MaxAttempts;
        council.stage = -1;
    }
#endif

    // 落成人选：宗室族人从族谱落成实体，远支从都城人口里认一位，外姓用现成的人
#if EC_PENDING
    private static Person Enthrone(Empire empire, Realm core, CourtSuccessionCandidate candidate)
    {
        switch (candidate.basis)
        {
            case Cadet:
                return VirtualGenealogySystem.SpawnCadet(core, empire.EmpireSpecificClan);
            case Commoner:
            {
                Person official = core.Capital == null ? null : CityPopulationSystem.SpawnOfficial(core.Capital, core);
                if (EmpireCraftKingdomBehCheckHeir.BarredBySex(core, official)) official.Sex = Sex.Male;
                return official;
            }
        }
        return CandidateActor(candidate, realize: true);
    }

    private static Person CandidateActor(CourtSuccessionCandidate candidate, bool realize = false)
    {
        if (candidate == null) return null;
        if (candidate.actor > 0) return EmpireWorld.Current.PersonByKey(candidate.actor);
        if (candidate.identity <= 0) return null;
        PersonalClanIdentity person = SpecificClanManager.getPerson(candidate.identity);
        if (person == null || !person.is_alive) return null;
        if (realize) return person.Realize();
        return person.is_virtual ? null : person._actor;
    }
#endif

    // 神力钦定：开列人选后、定策前直接立其中一位
#if EC_PENDING
    public static bool GodChoose(Empire empire, int index)
    {
        CourtState state = CourtSystem.State(empire);
        CourtSuccession council = state?.succession;
        if (council == null || council.stage < 0 || council.stage >= 2 || index < 0 || index >= council.candidates.Count)
            return false;
        Decide(empire, state, council, GameClock.Now, index);
        return council.stage == 2;
    }
#endif

    #endregion

    // 人选的称呼：名字(立亲/立长……，亲疏，年龄)
    public static string Describe(CourtSuccessionCandidate candidate)
    {
        string basis = LM.Get("court_succession_basis_" + candidate.basis);
        if (candidate.basis is Cadet or Commoner) return $"{candidate.name}({basis})";
        string relation = candidate.basis switch
        {
            Minister or Acclaim => LM.Get("court_succession_kin_outsider"),
            Union => LM.Get("court_succession_kin_union"),
            _ => KinLabel(candidate.kinship)
        };
        return string.Format(LM.Get("court_succession_candidate"), candidate.name, basis, relation, candidate.age);
    }

    public static string KinLabel(int kinship) => LM.Get(kinship switch
    {
        <= 2 => "court_succession_kin_2",
        3 => "court_succession_kin_3",
        4 => "court_succession_kin_4",
        <= 6 => "court_succession_kin_near",
        _ => "court_succession_kin_far"
    });

    // 朝局纪事 + 世界日志 + 史书(空位期间写进先帝那一朝的末尾，见 HistoryRecordSystem.RecordHistory)
#if EC_PENDING
    private static void Publish(Empire empire, CourtState state, string text)
    {
        state.log.Insert(0, new CourtLogEntry { time = EmpireWorld.Current?.Now() ?? 0d, text = text });
        if (state.log.Count > 8) state.log.RemoveRange(8, state.log.Count - 8);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }
#endif
}
}
