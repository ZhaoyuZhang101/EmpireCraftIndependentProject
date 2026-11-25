// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PrinceStruggleSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 夺嫡(皇帝的成年儿子之间争储、争位)：
//   · 实力：名望，加上统兵(是军团统领 +60)、封爵(每块封地 +30)、年纪(每岁 +0.5)；
//   · 废太子(每年)：太子之外最强的皇子比太子强三成以上时，皇帝有机会废太子、改立他(三分，昏聩、荒淫、暴虐之君加倍，
//     明君减半，皇帝年过五十再加五成)；被废的太子四分之一被赐死；
//   · 兵变夺位(每年)：皇帝年过四十五，统兵的皇子(或比太子强五成以上的)有一分五的机会发动兵变——诛杀太子、
//     逼皇帝退位(留作太上皇)，自立为帝；昏聩、暴虐之君加倍；
//   · 诸王争位(骤然驾崩或寿终而没有册立储君，见 RoyalHouseSystem)：一个月后新君已立，兄弟里最强的一位若不弱于新君八成，
//     有机会起兵——按实力比定胜负，胜则弑新君自立，败则被诛；新君年幼时他更敢动手；
//   · 怎么继的位记在纪年上(EmpireCraftHistory.succession_note)，世系图里标出来(见 DynastyLineageWindow)。
// 宫门之变(用户 2026-10-27 举"玄武门之变"，游戏里不写史名，按都城起名"某城宫门之变")：兵变夺位不再一掷成事——
//   皇子率本部(是军团统领的按所统军团的兵力，否则按实力折算的亲兵)伏于都城宫门，与京城禁军(空饷的不算，不奉诏的不出力)对垒，
//   伏击者占先手(兵力按一倍半算)：胜则诛太子、逼皇帝退位；败则皇子伏诛。胜负、兵力写进史书。
//   宫门之变、诸王争位兵败的皇子先按律以篡位论处(只处死本人，不连坐妻小，见 LawEvents)，再伏诛。
// 诸王之乱(用户举"八王之乱"，游戏里叫"诸王之乱")：诸王争位时，宗室里就藩为王(做着帝国成员国君主)的，实力不弱于新君六成的
//   各自起兵(至多四家，各打一场"某王争位之战"，标"诸王之乱")——谁攻下京城谁即位(见 CityPatch 藩王索取皇位)，余下的接着打新君；
//   没有就藩之王才照旧由最强的兄弟一掷定胜负。
public static class PrinceStruggleSystem
{
    private const float DeposeChance = 0.03f, CoupChance = 0.015f, ContestChance = 0.35f;
    private const int PendingMonths = 6;
    // 宫门之变：伏击者先手的兵力倍数、胜率上下限；没统兵的皇子每点实力折多少亲兵
    private const float AmbushFactor = 1.5f, MinCoupOdds = 0.15f, MaxCoupOdds = 0.9f, RetainersPerStrength = 40f;
    // 诸王之乱：起兵的门槛(实力是新君的几成)与至多几家
    private const float PrinceRiseRatio = 0.6f;
    private const int MaxPrinces = 4;

    public const string NoteDeposed = "deposed", NoteCoup = "coup", NoteStrife = "strife", NotePalaceCoup = "palace_coup",
        NoteUsurp = "usurp", NoteMilitaryCoup = "military_coup", NoteCourtChoice = "court_choice", NoteAcclaimed = "acclaimed",
        NotePartition = "partition", NoteEnthroned = "enthroned", NoteFled = "fled", NoteRaised = "raised",
        NoteRestoration = "restoration";

    private static readonly Dictionary<long, (double at, float severity)> Contests = new();

    public static void QueueContest(Empire empire, float severity)
    {
        if (empire?.data == null) return;
        Contests[empire.data.id] = (GameClock.Now, severity);
    }

    // 记下下一位君主是怎么继的位(新君即位时写进纪年，见 HistoryRecordSystem.RecordNewEmperorHistory)
    public static void Note(Realm kingdom, Person successor, string note)
    {
        if (kingdom?.Record == null || successor?.Record == null) return;
        var data = kingdom.GetOrCreate();
        data.succession_note = note;
        data.succession_note_actor = successor.Key;
    }

    private static float Strength(Person actor) =>
        actor?.Record == null ? 0f
            : actor.Renown + (CorpsSystem.IsGeneral(actor) ? 60f : 0f) + (actor.GetOwnedTitle()?.Count ?? 0) * 30f +
              Mathf.Min(actor.Age(), 60) * 0.5f;

    private static bool IsPrince(Person son, Empire empire) =>
        son?.Record != null && !son.IsGone() && son.Alive && son.IsMale() && son.IsAdult() &&
        !son.IsRuler() && son.Realm?.GetEmpire() == empire;

    // 成年皇子连族谱里的虚拟族人一起数(纯净地图下多不在地图上；以前只数地图上的，"两位以上成年皇子"几乎永远不成立，
    // 废太子、宫门之变从不发生)
#if EC_PENDING
    private static List<PersonalClanIdentity> PrincePeople(Empire empire, Person emperor) =>
        KinRoster.Children(emperor).Where(person => KinRoster.Counts(person,
            son => son.sex == Sex.Male && son.age >= KinRoster.AdultAge && KinRoster.KingdomOf(son)?.GetEmpire() == empire,
            son => IsPrince(son, empire))).ToList();
#endif

    // 用到皇子时才落成
#if EC_PENDING
    private static List<Person> Princes(Empire empire, Person emperor) =>
        KinRoster.Realize(PrincePeople(empire, emperor), son => IsPrince(son, empire));

    private static float Mood(Person emperor) => RulerQualitySystem.Of(emperor) switch
    {
        RulerQualitySystem.Grade.Wise => 0.5f,
        RulerQualitySystem.Grade.Fatuous or RulerQualitySystem.Grade.Dissolute or RulerQualitySystem.Grade.Tyrant => 2f,
        _ => 1f
    };

    public static void Yearly(Empire empire)
    {
        Person emperor = empire?.Emperor;
        Realm core = empire?.CoreKingdom;
        if (emperor?.Record == null || !emperor.Alive || core?.Record == null ||
            !EmpireCraft.Core.EmpireCraftKingdomBehCheckKing.NeedSuccession(core)) return;
        // 秘密立储：谁是储君无人知晓，废太子、宫门之变都没了由头(见 LateImperialSystem)
        if (LateImperialSystem.SecretSuccession(core)) return;
        List<PersonalClanIdentity> people = PrincePeople(empire, emperor);
        if (people.Count < 2) return;
        Person heir = core.HasHeir() ? core.GetHeir() : null;
        long heirIdentity = heir?.GetPersonalIdentity()?.id ?? -1L;
        if (heir == null || people.All(person => person.id != heirIdentity)) return;
        float mood = Mood(emperor);
        // 先掷骰：两样都掷不中就不必把虚拟皇子落成实体(掷法、概率同以前)
        float coupRoll = EcRandom.value, deposeRoll = EcRandom.value;
        bool coupRolled = emperor.Age() >= 45 && coupRoll < CoupChance * (mood > 1f ? 2f : 1f);
        bool deposeRolled = deposeRoll < DeposeChance * mood * (emperor.Age() >= 50 ? 1.5f : 1f);
        if (!coupRolled && !deposeRolled) return;
        List<Person> princes = Princes(empire, emperor);
        if (princes.Count < 2 || !princes.Contains(heir)) return;
        Person rival = princes.Where(prince => prince != heir).OrderByDescending(Strength).First();
        float ratio = Strength(rival) / Mathf.Max(1f, Strength(heir));
        // 兵变夺位(宫门之变)
        if (coupRolled && (CorpsSystem.IsGeneral(rival) || ratio >= 1.5f))
        {
            GateCoup(empire, core, emperor, heir, rival);
            return;
        }
        // 废太子
        if (ratio >= 1.3f && deposeRolled)
        {
            core.GetOrCreate().designated_heir_id = rival.Key;
            core.SetHeir(rival);
            Note(core, rival, NoteDeposed);
            Record(empire, string.Format(LM.Get("prince_deposed_history"), emperor.FullName(), heir.FullName(), rival.FullName()), emperor);
            BiographySystem.Note(heir, BiographySystem.Format("bio_heir_deposed", emperor.FullName(), rival.FullName()), emperor);
            BiographySystem.Note(emperor, BiographySystem.Format("bio_emperor_deposed_heir", heir.FullName(), rival.FullName()), heir);
            BiographySystem.Note(rival, BiographySystem.Format("bio_became_heir", heir.FullName()), heir);
            if (EcRandom.value < 0.25f)
            {
                Record(empire, string.Format(LM.Get("prince_deposed_killed_history"), heir.FullName()), heir);
                BiographySystem.Kill(heir, BiographySystem.Format("death_cause_deposed_heir", emperor.FullName()), emperor, executed: true);
            }
        }
    }
#endif

    // 宫门之变：皇子本部对京城禁军
#if EC_PENDING
    private static void GateCoup(Empire empire, Realm core, Person emperor, Person heir, Person rival)
    {
        float rebels = PrinceForce(rival) * AmbushFactor, guards = GuardForce(core);
        float odds = Mathf.Clamp(rebels / Mathf.Max(1f, rebels + guards), MinCoupOdds, MaxCoupOdds);
        string gate = string.Format(LM.Get("palace_gate_coup_name"), core.Capital?.GetCityName() ?? core.GetKingdomName());
        if (EcRandom.value < odds)
        {
            Record(empire, string.Format(LM.Get("palace_gate_coup_history"), gate, rival.FullName(), heir.FullName(), emperor.FullName(),
                CorpsSystem.ShortStrength(rebels / AmbushFactor), CorpsSystem.ShortStrength(guards)), rival);
            BiographySystem.Note(rival, BiographySystem.Format("bio_prince_coup_winner", heir.FullName()), heir);
            BiographySystem.Kill(heir, BiographySystem.Format("death_cause_coup", rival.FullName()), rival);
            Install(empire, rival, NoteCoup);
        }
        else
        {
            Record(empire, string.Format(LM.Get("palace_gate_coup_failed_history"), gate, rival.FullName(),
                CorpsSystem.ShortStrength(rebels / AmbushFactor), CorpsSystem.ShortStrength(guards)), rival);
            // 事败按律以篡位论处(只处死本人，不连坐妻小)；律法关闭、本政体无此律、律里没有死刑的，照旧伏诛(见 LawEvents)
            LawEvents.FailedUsurper(rival, core, "law_reason_failed_coup");
            BiographySystem.Kill(rival, BiographySystem.Format("death_cause_strife_lost", emperor.FullName()), emperor, executed: true);
        }
        foreach (City city in core.Cities?.ToList() ?? new List<City>())
        {
            CityStabilityData state = city?.Record == null ? null : CityStabilitySystem.State(city);
            if (state != null) state.stability = Mathf.Max(0f, state.stability - 4f);
        }
    }
#endif

    // 皇子的本部：统兵的按所统军团，没统兵的按实力折算亲兵
    private static float PrinceForce(Person prince)
    {
        CorpsData corps = CorpsSystem.CorpsOf(prince);
        return corps != null ? Mathf.Max(corps.strength, 1f) : Mathf.Max(1f, Strength(prince) * RetainersPerStrength);
    }

    // 京城禁军：空饷的不算，不奉诏的不出力
    private static float GuardForce(Realm core)
    {
        float sum = 0f;
        foreach (CorpsData corps in PalaceArmySystem.Palace(core))
            if (!PalaceArmySystem.Refuses(corps)) sum += corps.strength * PalaceArmySystem.RealShare(corps);
        return sum;
    }

    // 诸王之乱：宗室里就藩为王的各自起兵争位；返回起兵了几家
#if EC_PENDING
    private static int RiseOfPrinces(Empire empire, Person emperor, float severity)
    {
        Realm core = empire.CoreKingdom;
        SpecificClan clan = empire.EmpireSpecificClan ?? emperor.GetSpecificClan();
        if (core?.Record == null || clan?.AllAliveMembers == null) return 0;
        float emperorStrength = Mathf.Max(1f, Strength(emperor)) / (emperor.IsAdult() ? 1f : 1.5f);
        List<Person> princes = clan.AllAliveMembers
            .Where(actor => actor?.Record != null && !actor.IsGone() && actor.Alive && actor.IsAdult() && actor != emperor &&
                            actor.IsRuler() && actor.Realm != null && actor.Realm != core && actor.Realm.GetEmpire() == empire &&
                            Strength(actor) >= emperorStrength * PrinceRiseRatio)
            .OrderByDescending(Strength).Take(MaxPrinces).ToList();
        var risen = new List<Person>();
        // 已被卷进这一轮别的王的争位之战(结盟、附庸跟着参战)的不再另起：另起会让那一场作罢(见 StartLocalRebelling)
        var engaged = new HashSet<Realm>();
        foreach (Person prince in princes)
        {
            if (EcRandom.value >= ContestChance * severity) continue;
            Realm fief = prince.Realm;
            if (engaged.Contains(fief)) continue;
            if (!fief.StartLocalRebelling(EmpireCraft.Core.EmpireWarType.藩王索取皇位)) continue;
            War war = EmpireWorld.Current.StartWar(fief, core, WarKinds.Normal);
            if (war?.Record == null) continue;
            war.SetEmpireWarType(EmpireCraft.Core.EmpireWarType.藩王索取皇位);
            war.GetOrCreate().war_label = "princes";
            foreach (Realm kingdom in war.Attackers.Concat(war.Defenders)) engaged.Add(kingdom);
            WarCatalog.ApplyName(war);
            WarChronicle.Record(war, WarChronicle.Declare, string.Format(LM.Get("war_log_prince_rose"), prince.FullName(),
                fief.GetKingdomName(), emperor.FullName()));
            risen.Add(prince);
        }
        if (risen.Count >= 2)
            Record(empire, string.Format(LM.Get("princes_strife_history"), empire.GetEmpireName(),
                string.Join(LM.Get("list_separator"), risen.Select(prince => prince.FullName()))), risen[0]);
        else if (risen.Count == 1)
            Record(empire, string.Format(LM.Get("prince_rose_history"), risen[0].FullName(), emperor.FullName()), risen[0]);
        return risen.Count;
    }
#endif

    // 诸王争位：新君即位一个月后结算；半年内一直没有新君就作罢
#if EC_PENDING
    public static void Monthly()
    {
        foreach (var pair in Contests.ToList())
        {
            Empire empire = ModClass.EMPIRE_MANAGER?.Get(pair.Key);
            if (empire?.data == null || GameClock.MonthsSince(pair.Value.at) >= PendingMonths)
            {
                Contests.Remove(pair.Key);
                continue;
            }
            Person emperor = empire.Emperor;
            if (GameClock.MonthsSince(pair.Value.at) < 1 || emperor?.Record == null || !emperor.Alive) continue;
            Contests.Remove(pair.Key);
            Contest(empire, emperor, pair.Value.severity);
        }
    }

    private static void Contest(Empire empire, Person emperor, float severity)
    {
        // 有就藩之王的，起兵打仗(诸王之乱)
        if (RiseOfPrinces(empire, emperor, severity) > 0) return;
        PersonalClanIdentity identity = emperor.GetPersonalIdentity();
        if (identity == null) return;
        Person rival = SpecificClanManager.GetSiblingsWithRelation(identity).Select(pair => pair.Item2)
            .Where(person => person != null && person.is_alive && person.sex == Sex.Male)
            .Select(person => person.Realize())
            .Where(actor => actor?.Record != null && actor.Alive && actor.IsAdult() && actor != emperor && !actor.IsRuler() &&
                            actor.Realm?.GetEmpire() == empire)
            .OrderByDescending(Strength).FirstOrDefault();
        if (rival == null) return;
        float ratio = Strength(rival) / Mathf.Max(1f, Strength(emperor)) * (emperor.IsAdult() ? 1f : 1.5f);
        if (ratio < 0.8f || EcRandom.value >= ContestChance * severity) return;
        if (EcRandom.value < ratio / (ratio + 1f))
        {
            Record(empire, string.Format(LM.Get("prince_strife_win_history"), rival.FullName(), emperor.FullName()), rival);
            BiographySystem.Note(rival, BiographySystem.Format("bio_strife_winner", emperor.FullName()), emperor);
            Install(empire, rival, NoteStrife);
            if (emperor.Alive) BiographySystem.Kill(emperor, BiographySystem.Format("death_cause_strife", rival.FullName()), rival);
        }
        else
        {
            Record(empire, string.Format(LM.Get("prince_strife_lose_history"), rival.FullName()), rival);
            BiographySystem.Note(emperor, BiographySystem.Format("bio_strife_suppressed", rival.FullName()), rival);
            // 兵败按律以篡位论处，之后照旧伏诛(已被律法处死的不再杀，见 LawEvents)
            LawEvents.FailedUsurper(rival, empire.CoreKingdom, "law_reason_failed_strife");
            BiographySystem.Kill(rival, BiographySystem.Format("death_cause_strife_lost", emperor.FullName()), emperor, executed: true);
        }
        foreach (City city in empire.CoreKingdom?.cities?.ToList() ?? new List<City>())
        {
            CityStabilityData state = city?.Record == null ? null : CityStabilitySystem.State(city);
            if (state != null) state.stability = Mathf.Max(0f, state.stability - 6f);
        }
    }
#endif

    // 换上新君(照宫廷政变的做法：旧君离位，新君坐上君主官职)
#if EC_PENDING
    private static void Install(Empire empire, Person successor, string note)
    {
        Realm core = empire.CoreKingdom;
        Note(core, successor, note);
        core.RemoveRuler();
        OfficeObject office = core.GetOffice();
        if (office != null)
        {
            office.meta_object = core;
            office.SetActor(successor);
        }
        else core.SetRuler(successor);
    }

    private static void Record(Empire empire, string text, Person actor)
    {
        empire.RecordHistory(directContent: text, actorId: actor?.Key ?? -1L);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }
#endif
}
}
