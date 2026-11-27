// Generated from the EmpireCraft mod: Scripts/GeneralSystems/RoyalHouseSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 宗室的生老病死(让王朝有起有落，不再代代父死子继、一帆风顺)：
//   · 寿数(真实年龄法则开着时，不看原版老死法则)：所有人按本人文化的科技时代定天年——石器平均四十六、中世纪五十五、
//     工业六十二、现代七十二、未来八十(标准差十，见 RealisticAgeSystem.MeanDeathAge)；
//     君主再按资质：明君 +3，庸弱 -3，昏聩 -5，暴虐 -4，荒淫 -10；按人定死，不会每年重掷；
//     成为君主、储君时已到或将到天年的(年长的大臣、宿将被立)按那时的年纪
//     另定余寿，即位后至少三年不寿终(见 RealisticAgeSystem.RoyalLifespan)；
//   · 老死每年过生日时检查(原版只在人物空闲时查，纯净地图、无小人模式下几乎查不到，实体活到一两百岁)；
//     真实年龄关着时这两种模式下按原版寿命曲线每年掷一次(见 RealisticAgeSystem.NativeAgeDeathDue)；
//   · 皇嗣夭折：君主、储君的子女与幼主，一岁内每年一成二，一到四岁四分，五到十四岁一分二，工业化、电气、计算机逐级减少；
//     储君、幼帝夭折记入史书；
//   · 生育：君主与储君一年半至多添一个孩子，在世子女至多十四个；约一成二的君主不育(按人定)——所以会有绝嗣，
//     要从兄弟、孙辈、旁支里找人继位(见 EmpireCraftKingdomBehCheckHeir)；
//   · 意外：皇帝每年有千分之六的机会意外身亡(坠马、落水、暴病、遇刺、雷击)，暴君多遇刺、荒淫之君多误服丹药或纵欲暴亡，
//     御驾亲征中流矢、出巡在外另加；身后朝野震动：天命正统下降、各城稳定下降、权臣趁势坐大，
//     新君年幼、生前没有册立储君、无人可继时更重；没有册立储君的还可能诸王争位(见 PrinceStruggleSystem)；
//   · 诸王就藩：君主成年、不是储君、不在朝任职的儿子不住都城，去自己的封地(没有封地就分到本国别的大城)——
//     都城出事不会一次把宗室死光；
//   · 补姓：在宗族里却没有姓的人(偶有起名漏了姓)，每年过生日时补上宗族的姓；
//   · 婚配与添丁(无小人、纯净地图模式)：原版人物的日常(找配偶、怀孕)在这两种模式下几乎不跑——实测一个世界四百多年、
//     三十多位皇帝只记到两次宗室生育，代代无嗣、群臣议立、换姓，一个朝代撑不过一百五十年(虚拟族谱的生育只管已转为
//     虚拟的族人，在位的君主、储君是实体，轮不到)。每年替原版办一次：君主、储君成年(西方封建制留给议婚，到
//     RoyalMarryLateAge 才在国内找)还没配偶的，从本国现有人物里挑一位异性(同种族、成年未婚、非近亲，名望高的优先；
//     不另落成新人)；成年、生母不过 RoyalBirthAgeMax 岁的，每年按 RoyalBirthChance 添一胎，按古代幼儿夭折(ChildhoodDeath)
//     掷过活下来的记入族谱为虚拟子嗣——平均成年子女四五个，
//     不生出实体，继位、分封要用时再落成(见 VirtualGenealogySystem.AddRoyalChild、RealizeHeir)；没有配偶的生母不入谱。
//     不育、一年半一胎、子女上限照旧(见 CanBirth，虚拟子女也算)；
//   · 后宫添丁(用户 2026-10-22"那么多妃子的情况下子嗣应该不可能少")：男君主、储君的妃嫔(族谱里的妾室，后宫官职选进来的，
//     见 OfficeSelector.TryGetHarem)各按自己的年龄、一年半一胎每年按 HaremBirthChance 生育，不受正妻那份间隔限制；
//     有后宫的君主子女上限放宽到 HaremMaxChildren。
public static class RoyalHouseSystem
{
    private const float BirthIntervalYears = 1.5f;
    // 不育按人定：以前一成二，君主不育则后宫也无出，每八位君主就有一位注定绝嗣(群友 2026-11-22 "目前有君主绝嗣的问题")
    private const int MaxChildren = 14, InfertilePercent = 4;
    private const float MishapBase = 0.006f, MishapCampaign = 0.03f, MishapTour = 0.01f, MishapVice = 0.015f;
    private const int DeathsPerFrame = 30;

    private enum Fate { Age, Infant }

    private static readonly List<(Person actor, Fate fate)> Pending = new();
    private static double _lastMonth = -1d;
    private static int _months;
    private static object _world;

    public static bool IsRoyal(Person actor) =>
        actor?.Record != null && !actor.IsGone() && (actor.IsRuler() || actor.Realm != null && actor.Realm.GetHeir() == actor);

    private static bool Has(Person actor, string tech)
    {
        string culture = actor?.Realm == null ? null : TechnologySystem.GetCultureOf(actor.Realm);
        return !string.IsNullOrEmpty(culture) && TechnologySystem.IsActiveFor(culture) && TechnologySystem.HasTech(culture, tech);
    }

    // 君主、储君的天年：与常人同按科技时代与按人散列(见 RealisticAgeSystem.NaturalDeathAge)，君主再按资质加减
    // (成为君主、储君时已近天年的另定，见 RealisticAgeSystem.RoyalLifespan)
    public static int RoyalDeathAge(Person actor) =>
        RealisticAgeSystem.NaturalDeathAge(RealisticAgeSystem.Seed(actor), RoyalMeanDeathAge(actor));

    public static float RoyalMeanDeathAge(Person actor)
    {
        float mean = RealisticAgeSystem.MeanDeathAge(actor);
        if (actor.IsRuler())
            mean += RulerQualitySystem.Of(actor) switch
            {
                RulerQualitySystem.Grade.Wise => 3f,
                RulerQualitySystem.Grade.Weak => -3f,
                RulerQualitySystem.Grade.Fatuous => -5f,
                RulerQualitySystem.Grade.Tyrant => -4f,
                RulerQualitySystem.Grade.Dissolute => -10f,
                _ => 0f
            };
        return mean;
    }

    #region 每年过生日(ActorPatch.UpdateAge)

    public static void OnBirthday(Person actor)
    {
        if (actor?.Record == null || actor.IsGone() || !actor.Alive || !actor.IsSapient()) return;
        RepairSurname(actor);
        // 真实年龄开着时按科技时代的天年(不看原版老死法则，见 RealisticAgeSystem.AgeDeathDue)；
        // 关着时按原版寿命曲线每年掷一次(只在纯净地图、无小人模式，见 RealisticAgeSystem.NativeAgeDeathDue)
        if (RealisticAgeSystem.Enabled ? RealisticAgeSystem.AgeDeathDue(actor) : RealisticAgeSystem.NativeAgeDeathDue(actor))
        {
            Pending.Add((actor, Fate.Age));
            return;
        }
        float infant = InfantMortality(actor);
        if (infant > 0f && EcRandom.value < infant) Pending.Add((actor, Fate.Infant));
    }

    private static float InfantMortality(Person actor)
    {
        int age = actor.Age();
        if (age >= 15) return 0f;
        // 已经即位、立了储的不再掷(同一份反馈"君主超低龄化")：虚拟皇嗣出生时已按幼儿夭折掷过一次(见 ChildDeathChance)，
        // 落成即位后每年再掷，幼主接连早夭、一朝连换几个娃娃皇帝，换到宗室无人
        if (IsRoyal(actor)) return 0f;
        // 父母按族谱认(落成的皇嗣与转成虚拟族人的父母，原版亲子关系接不上)
        PersonalClanIdentity self = actor.GetPersonalIdentity();
        bool royal = IsRoyal(actor) || (actor.GetParents()?.Any(IsRoyal) ?? false) ||
                     self != null && new[] { self.father, self.mother }.Any(id => id > 0 &&
                         SpecificClanManager.getPerson(id) is PersonalClanIdentity parent && KinRoster.OnMap(parent) && IsRoyal(parent._actor));
        if (!royal) return 0f;
        float chance = age < 1 ? 0.12f : age < 5 ? 0.04f : 0.012f;
        if (Has(actor, "industrialization")) chance *= 0.6f;
        if (Has(actor, "electricity")) chance *= 0.6f;
        if (Has(actor, "computing")) chance *= 0.5f;
        return chance;
    }

    private static void RepairSurname(Person actor)
    {
        // 华夏宗族的本宗族人姓本族的姓(见 SurnameInheritanceSystem.EnforceClanSurname)
        if (SurnameInheritanceSystem.EnforceClanSurname(actor.GetPersonalIdentity())) return;
        string clan = actor.GetPersonalIdentity()?._specificClan?.name;
        if (string.IsNullOrWhiteSpace(clan)) return;
        Name name = actor.GetModName();
        if (name == null || name.hasFamilyName(actor) || !name.hasFirstName(actor)) return;
        actor.SetFamilyName(clan);
        name.SetName(actor);
    }

    #endregion

    #region 生育

    #region 婚配与添丁(无小人、纯净地图模式)

    private const int RoyalMarryAge = 16, RoyalMarryLateAge = 22, RoyalMarryMaxAge = 55, ConsortMaxAge = 40, RoyalBirthAgeMax = 45,
        RoyalFatherAgeMax = 60;
    // 每年添丁的机会；虚拟子嗣只有老死一种死法，出生时就按古代幼儿夭折掷一次(约三分之一活不到成年，
    // 工业化、电气、计算机逐级降低，同 InfantMortality)：一位君主平均成年子女四五个
    private const float RoyalBirthChance = 0.25f, ChildhoodDeath = 0.34f;
    // 后宫每位妃嫔每年添丁的机会；有后宫的君主在世子女上限
    private const float HaremBirthChance = 0.2f;
    private const int HaremMaxChildren = 40, HeirlessAge = 30;

#if EC_PENDING
    private static void RoyalFamily(Realm kingdom)
    {
        if (kingdom?.Record == null || kingdom.IsGone() || !kingdom.HasRuler() ||
            !EmpireCraft.Core.RegimeManager.IsMonarchy(kingdom.GetRegime()?.type)) return;
        Tend(kingdom, kingdom.Ruler);
        Person heir = kingdom.HasHeir() ? kingdom.GetHeir() : null;
        if (heir != null && heir != kingdom.Ruler) Tend(kingdom, heir);
    }

    private static void Tend(Realm kingdom, Person royal)
    {
        if (royal?.Record == null || royal.IsGone() || !royal.Alive || !royal.IsSapient()) return;
        int age = royal.Age();
        Person lover = royal.Spouse;
        if (lover?.Record == null || lover.IsGone() || !lover.Alive) lover = null;
        // 婚配：成年还没配偶的，从本国现有人物里挑一位(不另落成新人)
        int from = RoyalMarriageSystem.Applies(kingdom) ? RoyalMarryLateAge : RoyalMarryAge;
        if (lover == null && age >= from && age <= RoyalMarryMaxAge)
        {
            lover = FindConsort(kingdom, royal);
            if (lover != null) royal.becomeLoversWith(lover);
        }
        // 添丁：记入族谱为虚拟子嗣。生母的年龄：君主是女子看她自己，否则看配偶；没有配偶的按宫人所出，
        // 父亲不过 RoyalFatherAgeMax 岁
        // 后宫各妃嫔自己算，不受正妻年纪、正妻那份间隔的限制
        if (royal.Sex != Sex.Female && age >= RoyalMarryAge) TendHarem(royal, age, lover);
        if (age < RoyalMarryAge || lover != null && lover.Age() < RoyalMarryAge) return;
        bool queen = royal.Sex == Sex.Female;
        if (queen ? age > RoyalBirthAgeMax : lover != null ? lover.Age() > RoyalBirthAgeMax : age > RoyalFatherAgeMax) return;
        // 无子求嗣：三十岁以上还没有在世的子女，添丁的机会加倍
        float chance = age >= HeirlessAge && LivingChildren(royal) == 0 ? RoyalBirthChance * 2f : RoyalBirthChance;
        if (!CanBirth(royal) || lover != null && !CanBirth(lover) || EcRandom.value >= chance) return;
        // 这一胎算数(一年半内不再添丁)；夭折的不入谱
        NoteBirth(royal, lover);
        if (EcRandom.value < ChildDeathChance(royal)) return;
        VirtualGenealogySystem.AddRoyalChild(royal, lover);
    }
#endif

    // 古代幼儿夭折(出生时掷一次)，工业化、电气、计算机逐级降低
    private static float ChildDeathChance(Person royal)
    {
        float death = ChildhoodDeath;
        if (Has(royal, "industrialization")) death *= 0.6f;
        if (Has(royal, "electricity")) death *= 0.6f;
        if (Has(royal, "computing")) death *= 0.5f;
        return death;
    }

    // 后宫添丁：每位妃嫔(活着、女子、16~45 岁、能生育)按自己的一年半一胎、每年 HaremBirthChance；正妻另算(见上)
    private static void TendHarem(Person royal, int age, Person wife)
    {
        if (age > RoyalFatherAgeMax || Infertile(royal)) return;
        PersonalClanIdentity person = royal.GetPersonalIdentity();
        if (person?.concubines == null || person.concubines.Count == 0) return;
        int living = LivingChildren(royal);
        double now = GameClock.Now;
        foreach ((long _, long id) in person.concubines.ToList())
        {
            if (living >= HaremMaxChildren) return;
            // 妃嫔转成了虚拟族人(以前就此不再添丁)：在本国的落成一次，之后作君主、储君的妃嫔留在地图上(见 PersonRef.Keeps)
            PersonalClanIdentity consortPerson = SpecificClanManager.getPerson(id);
            Person consort = KinRoster.OnMap(consortPerson) ? consortPerson._actor
                : KinRoster.KingdomOf(consortPerson) == royal.Realm ? PersonRef.Present(consortPerson) : null;
            if (consort?.Record == null || consort.IsGone() || !consort.Alive || consort == wife ||
                consort.Sex != Sex.Female || Infertile(consort)) continue;
            int consortAge = consort.Age();
            if (consortAge < RoyalMarryAge || consortAge > RoyalBirthAgeMax) continue;
            double last = consort.GetOrCreate().last_royal_birth;
            if (last >= 0d && now >= last && GameClock.YearsSince(last) < BirthIntervalYears) continue;
            if (EcRandom.value >= HaremBirthChance) continue;
            consort.GetOrCreate().last_royal_birth = now;
            if (EcRandom.value < ChildDeathChance(royal)) continue;
            if (VirtualGenealogySystem.AddRoyalChild(royal, consort)) living++;
        }
    }

    // 约一成二的人不育(按人定，不会每年重掷)
    private static bool Infertile(Person actor) =>
        ((ulong)actor.Key * 0xD6E8FEB86659FD93UL >> 40) % 100UL < InfertilePercent;

    // 在世子女(实体与虚拟子嗣取多的那个)
    private static int LivingChildren(Person parent)
    {
        int living = parent.GetChildren(false)?.Count(child => child?.Record != null && !child.IsGone() && child.Alive) ?? 0;
        PersonalClanIdentity person = parent.GetPersonalIdentity();
        if (person?.children != null)
            living = Math.Max(living, person.children.Count(id => SpecificClanManager.getPerson(id)?.is_alive == true));
        return living;
    }

    // 本国在世的人物里挑配偶：异性、同种族、成年未婚、非近亲，名望高的优先
    private static Person FindConsort(Realm kingdom, Person royal)
    {
        Person best = null;
        if (kingdom.People == null) return null;
        foreach (Person candidate in kingdom.People)
        {
            if (candidate?.Record == null || candidate == royal || candidate.IsGone() || !candidate.Alive ||
                candidate.IsRuler() || candidate.Spouse != null && !candidate.Spouse.IsGone() && candidate.Spouse.Alive ||
                candidate.Sex == royal.Sex || candidate.Asset != royal.Asset ||
                candidate.Age() < RoyalMarryAge || candidate.Age() > ConsortMaxAge || Kin(royal, candidate)) continue;
            if (best == null || candidate.Renown > best.Renown) best = candidate;
        }
        if (best != null) return best;
        // 纯净地图下本国地图上没几个人：京城大族里成年未婚、不是近亲的虚拟族人，声望(并回人口时记下的)高的落成一位
        PersonalClanIdentity self = royal.GetPersonalIdentity();
        IEnumerable<PersonalClanIdentity> pool = VirtualGenealogySystem.MagnatesOf(kingdom.Capital)
            .SelectMany(clan => clan.LivingPeople)
            .Where(person => KinRoster.Virtual(person) && person.sex != royal.Sex && !person.is_concubine &&
                             person.age >= RoyalMarryAge && person.age <= ConsortMaxAge && person.IsFreeToMarry(-2L) &&
                             person.species == royal.Asset?.Id && !KinshipRules.AreCloseKin(self, person))
            .OrderByDescending(VirtualGenealogySystem.VirtualRenown).ThenBy(person => person.id);
        return KinRoster.RealizeFirst(pool, candidate => candidate.Alive && candidate.Spouse == null, 1);
    }

    private static bool Kin(Person a, Person b)
    {
        // 族谱上的近亲(父母转成了虚拟族人时原版 getParents 找不到)
        if (KinshipRules.AreCloseKin(a, b)) return true;
        List<Person> parentsA = a.GetParents()?.Where(parent => parent != null).ToList() ?? new List<Person>();
        List<Person> parentsB = b.GetParents()?.Where(parent => parent != null).ToList() ?? new List<Person>();
        return parentsA.Contains(b) || parentsB.Contains(a) || parentsA.Any(parentsB.Contains);
    }

    #endregion

    // 君主、储君还能不能添孩子：不育的不能，一年半内添过的不能，在世子女满了不能
    public static bool CanBirth(Person parent)
    {
        if (!IsRoyal(parent)) return true;
        if (Infertile(parent)) return false;
        double last = parent.GetOrCreate().last_royal_birth;
        if (last >= 0d && GameClock.Now >= last && GameClock.YearsSince(last) < BirthIntervalYears) return false;
        // 虚拟子嗣也算(见 VirtualGenealogySystem.AddRoyalChild)；有后宫的上限放宽
        bool harem = parent.GetPersonalIdentity()?.concubines?.Count > 0;
        return LivingChildren(parent) < (harem ? HaremMaxChildren : MaxChildren);
    }

    // 添了孩子(BabyHelperPatch.MakeBaby)：记下君主、储君添孩子的时间
    public static void NoteBirth(Person parent1, Person parent2)
    {
        double now = GameClock.Now;
        if (IsRoyal(parent1)) parent1.GetOrCreate().last_royal_birth = now;
        if (IsRoyal(parent2)) parent2.GetOrCreate().last_royal_birth = now;
    }

    #endregion

    #region 每帧调度

#if EC_PENDING
    public static void Tick()
    {
        if (EmpireWorld.Current == null) return;
        if (!ReferenceEquals(_world, EmpireWorld.Current))
        {
            _world = EmpireWorld.Current;
            Pending.Clear();
            _lastMonth = -1d;
            _months = 0;
        }
        Flush();
        double now = GameClock.Now;
        if (_lastMonth >= 0d && now >= _lastMonth && GameClock.MonthsSince(_lastMonth) < 1) return;
        bool first = _lastMonth < 0d;
        _lastMonth = now;
        if (first) return;
        try
        {
            PrinceStruggleSystem.Monthly();
            SuccessionCouncilSystem.Monthly();
            EmpirePartitionSystem.Monthly();
            CityStateService.EnforceOneCity();
            TribalFissionService.EnforceOneCity();
            TribalAssemblySystem.Monthly();
            RoyalMarriageSystem.Monthly();
            WarlordEraSystem.FounderCapitulation();
            if (++_months % 12 != 0) return;
            if (ModClass.EMPIRE_MANAGER != null)
                foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
                {
                    if (empire?.data == null || RepublicSystem.IsRepublic(empire)) continue;
                    Mishap(empire);
                    PrinceStruggleSystem.Yearly(empire);
                    // 宠臣专权(见 FavouriteSystem)
                    FavouriteSystem.Yearly(empire);
                    EmpirePartitionSystem.Yearly(empire);
                    PrinceLineage.GrantPrinces(empire);
                }
            foreach (Realm kingdom in EmpireWorld.Current.LiveRealms.ToList()) Enfeoff(kingdom);
            // 质子：即位、身故、宗属关系没了(见 HostageSystem)
            HostageSystem.Yearly();
            // 个人恩怨：构陷、复仇、投敌(见 PersonalTieSystem)；志向(见 AmbitionSystem)
            PersonalTieSystem.Yearly();
            AmbitionSystem.Yearly();
            if (CityPopulationSystem.AbstractPopulationEnabled || PureMapMode.Enabled)
                foreach (Realm kingdom in EmpireWorld.Current.LiveRealms.ToList()) RoyalFamily(kingdom);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宗室] 结算失败: {exception.Message}");
        }
    }
#endif

    // 过生日时排下的生死，分帧执行(不在原版逐人更新年龄的循环里杀人)
#if EC_PENDING
    private static void Flush()
    {
        int done = 0;
        while (Pending.Count > 0 && done < DeathsPerFrame)
        {
            (Person actor, Fate fate) = Pending[Pending.Count - 1];
            Pending.RemoveAt(Pending.Count - 1);
            if (actor?.Record == null || actor.IsGone() || !actor.Alive) continue;
            done++;
            Empire empire = actor.Realm?.GetEmpire();
            bool emperor = empire?.data != null && empire.Emperor == actor;
            Realm core = emperor ? empire.CoreKingdom : null;
            Person heir = core != null && core.HasHeir() ? core.GetHeir() : null;
            bool designated = core != null && core.GetOrCreate().designated_heir_id >= 0;
            if (fate == Fate.Age)
            {
                if (IsRoyal(actor)) RealisticAgeSystem.LogRoyalDeath(actor, "年度检查");
                actor.getHitFullHealth(DeathCause.Age);
                // 寿终而没有册立储君：诸子也可能相争
                if (emperor && !designated && !actor.Alive) PrinceStruggleSystem.QueueContest(empire, 0.6f);
                continue;
            }
            // 夭折：储君、幼帝记入史书；幼帝夭折朝局动荡
            Empire realm = actor.Realm?.GetEmpire();
            bool crownPrince = actor.Realm != null && actor.Realm.GetHeir() == actor;
            if ((emperor || crownPrince) && realm?.data != null)
            {
                string text = string.Format(LM.Get(emperor ? "royal_child_emperor_died" : "royal_heir_died"), actor.FullName());
                realm.RecordHistory(directContent: text, actorId: actor.Key);
                TranslateHelper.LogEventMessage(text, realm.CoreKingdom);
            }
            BiographySystem.Kill(actor, LM.Get("death_cause_child"));
            if (emperor) Turmoil(empire, heir, designated);
        }
    }
#endif

    #endregion

    #region 意外与动荡

    private static void Mishap(Empire empire)
    {
        Person emperor = empire.Emperor;
        if (emperor?.Record == null || emperor.IsGone() || !emperor.Alive) return;
        RulerQualitySystem.Grade grade = RulerQualitySystem.Of(emperor);
        string tour = empire.data.tour_kind;
        float chance = MishapBase;
        if (grade is RulerQualitySystem.Grade.Tyrant or RulerQualitySystem.Grade.Dissolute) chance += MishapVice;
        if (tour == EmperorTourSystem.Campaign) chance += MishapCampaign;
        else if (!string.IsNullOrEmpty(tour)) chance += MishapTour;
        if (EcRandom.value >= chance) return;
        var causes = new List<(string key, float weight)> { ("fall", 1f), ("drown", 0.6f), ("illness", 1.2f), ("assassin", 0.5f), ("lightning", 0.15f) };
        if (grade == RulerQualitySystem.Grade.Tyrant) causes.Add(("assassin", 3f));
        if (grade == RulerQualitySystem.Grade.Dissolute) causes.AddRange(new[] { ("elixir", 2f), ("excess", 2f) });
        if (tour == EmperorTourSystem.Campaign) causes.Add(("arrow", 4f));
        float roll = EcRandom.value * causes.Sum(cause => cause.weight);
        string key = causes[causes.Count - 1].key;
        foreach ((string cause, float weight) in causes)
        {
            if (roll < weight) { key = cause; break; }
            roll -= weight;
        }
        Realm core = empire.CoreKingdom;
        Person heir = core.HasHeir() ? core.GetHeir() : null;
        bool designated = core.GetOrCreate().designated_heir_id >= 0;
        string text = string.Format(LM.Get("emperor_mishap_" + key), emperor.FullName());
        empire.RecordHistory(directContent: text, actorId: emperor.Key);
        TranslateHelper.LogEventMessage(text, core);
        BiographySystem.Kill(emperor, LM.Get("death_cause_mishap_" + key));
        Turmoil(empire, heir, designated);
    }

    // 骤然驾崩后的动荡：新君年幼、没有册立储君、无人可继时更重
    public static void Turmoil(Empire empire, Person heir, bool designated)
    {
        if (empire?.data == null) return;
        float severity = 1f;
        var notes = new List<string>();
        if (heir == null)
        {
            severity *= 1.3f;
            notes.Add("emperor_turmoil_no_heir");
        }
        else if (!heir.IsAdult())
        {
            severity *= 1.5f;
            notes.Add("emperor_turmoil_child");
        }
        if (!designated)
        {
            severity *= 1.4f;
            notes.Add("emperor_turmoil_undesignated");
        }
        empire.AddMandate(-Mathf.RoundToInt(4f * severity));
        Realm core = empire.CoreKingdom;
        foreach (City city in core?.Cities?.ToList() ?? new List<City>())
        {
            CityStabilityData state = city?.Record == null ? null : CityStabilitySystem.State(city);
            if (state != null) state.stability = Mathf.Max(0f, state.stability - 8f * severity);
        }
        empire.data.powerful_minister_progress += Mathf.RoundToInt(3f * severity);
        string text = LM.Get("emperor_turmoil_history") + string.Concat(notes.Select(LM.Get));
        empire.RecordHistory(directContent: text);
        if (!designated) PrinceStruggleSystem.QueueContest(empire, severity);
    }

    #endregion

    #region 诸王就藩

    private static void Enfeoff(Realm kingdom)
    {
        Person king = kingdom?.Ruler;
        City capital = kingdom?.Capital;
        if (king?.Record == null || capital?.Record == null || kingdom.Cities == null || kingdom.Cities.Count < 2) return;
        Person heir = kingdom.HasHeir() ? kingdom.GetHeir() : null;
        var princes = (king.GetChildren(false) ?? Enumerable.Empty<Person>())
            .Where(son => son?.Record != null && !son.IsGone() && son.Alive && son.IsAdult() && son != heir &&
                          !son.IsRuler() && !son.IsOnOffice() && son.Realm == kingdom && son.City == capital).ToList();
        foreach (Person prince in princes)
        {
            City fief = Fief(prince, kingdom) ?? kingdom.Cities
                .Where(city => city?.Record != null && !city.IsGone() && city != capital && city.SeatCell() != null)
                .OrderBy(city => city.People?.Count(unit => unit != null && unit.Alive && unit.GetParents().Contains(king)) ?? 0)
                .ThenByDescending(CityPopulationSystem.GetBackgroundTotal).FirstOrDefault();
            if (fief == null) continue;
            prince.JoinCity(fief);
            if (PureMapMode.Enabled) prince.SetCurrentTilePosition(fief.SeatCell());
            else prince.GoTo(fief.SeatCell());
        }
        // 虚拟族人里的成年皇子(纯净地图下多是这样，以前一直记在京城)：族谱上改记到封地，不落成。
        // 封地同上：有爵位封地的(族谱上看不出，留给落成后再走上面那步)以外，挑皇子最少、人口最多的城
        long heirIdentity = heir?.GetPersonalIdentity()?.id ?? -1L;
        List<PersonalClanIdentity> virtualPrinces = KinRoster.Children(king)
            .Where(son => KinRoster.Virtual(son) && son.age >= KinRoster.AdultAge && son.id != heirIdentity &&
                          KinRoster.CityOf(son) == capital).ToList();
        if (virtualPrinces.Count == 0) return;
        var placed = new Dictionary<long, int>();
        foreach (PersonalClanIdentity son in KinRoster.Children(king))
        {
            City at = KinRoster.CityOf(son);
            if (at?.Record != null) placed[at.Key] = placed.TryGetValue(at.Key, out int n) ? n + 1 : 1;
        }
        foreach (PersonalClanIdentity son in virtualPrinces)
        {
            City fief = kingdom.Cities.Where(city => city?.Record != null && !city.IsGone() && city != capital)
                .OrderBy(city => placed.TryGetValue(city.Key, out int n) ? n : 0)
                .ThenByDescending(CityPopulationSystem.GetBackgroundTotal).FirstOrDefault();
            if (fief == null) return;
            son.virtual_city_id = fief.Key;
            placed[fief.Key] = placed.TryGetValue(fief.Key, out int count) ? count + 1 : 1;
        }
    }

    // 封地：有实封或虚封的爵位，取那块封地的治所(还在本国的)
    private static City Fief(Person prince, Realm kingdom)
    {
        var data = prince.GetOrCreate();
        IEnumerable<long> titles = (prince.GetOwnedTitle() ?? new List<long>()).ToList();
        if (data.virtual_enfeoff) titles = titles.Append(data.virtual_enfeoff_title_id);
        foreach (long id in titles)
        {
            City seat = ModClass.KINGDOM_TITLE_MANAGER?.Get(id)?.title_capital;
            if (seat?.Record != null && !seat.IsGone() && seat.Realm == kingdom && seat != kingdom.Capital) return seat;
        }
        return null;
    }

    #endregion
}
}
