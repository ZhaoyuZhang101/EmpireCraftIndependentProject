// Generated from the EmpireCraft mod: Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckHeir.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Time = EmpireCraft.Core.FrameClock;
using Color = EmpireCraft.Core.EcColor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EmpireCraft.Core
{

public class EmpireCraftKingdomBehCheckHeir : GameAIKingdomBase
{
    private sealed class ChildRepairState { public float NextScan; }
    private static readonly ConditionalWeakTable<Person, ChildRepairState> ChildRepairs = new();
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult execute(Realm kingdom)
    {
        if (kingdom?.Record == null || EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(kingdom))
            return BehResult.Continue;
        // 穿越政权不立储：主席由元老院选(见 TransmigrantSystem.TryHandleVacancy)
        if (EmpireCraft.Core.TransmigrantSystem.IsTransmigrant(kingdom)) return BehResult.Continue;
        if (!EmpireCraftKingdomBehCheckKing.NeedSuccession(kingdom))
        {
            kingdom.RecoverToDefaultHeir();
            return BehResult.Continue;
        }
        bool vacancy = kingdom.Ruler == null || kingdom.Ruler.IsGone() || !kingdom.Ruler.Alive;
        if (vacancy && kingdom.IsEmpire() && ConstitutionalSuccessionSystem.ThroneBound(kingdom.GetEmpire()))
        {
            ConstitutionalSuccessionSystem.TryHandleVacancy(kingdom);
            return BehResult.Continue;
        }
        // 秘密立储：皇帝在诸皇子里按才具暗定储君(见 LateImperialSystem)
        if (!vacancy) LateImperialSystem.Designate(kingdom);
        // 在位时从本继承法起点复查，不保留以前级联到旁系的结果。
        InstallHeir(kingdom, ResolveHeir(kingdom, vacancy));
        return BehResult.Continue;
    }

    private static void InstallHeir(Realm kingdom, (Person actor, string relation) heir)
    {
        kingdom.RecoverToDefaultHeir();
        // 不立女主的国不立女储君(见 BarredBySex)：各条择立的路都已筛过，这里是最后一道
        if (!IsFitCandidate(heir.actor, null) || ForeignRuler(kingdom, heir.actor) || BarredBySex(kingdom, heir.actor))
        {
            kingdom.RemoveHeir();
            kingdom.StartToChooseHeir();
            return;
        }
        // 先君的亲生子女落在别的宗族(生时随了母亲或父亲那一族)：立为储君就认祖归宗，入先君的宗族、随先君的姓，
        // 帝系、王系不随储君的生母换姓(用户 2026-10-28 问"为什么皇帝会换姓")
        heir.actor.CheckSpecificClan(false);
        ReturnToLine(PredecessorOf(kingdom), heir.actor);
        kingdom.SetHeir(heir.actor);
        kingdom.ChooseHeirFinished();
        // 按族谱身份比较：背景人口里的子孙每次复查可能被重新落成新的单位，单位编号会变，人还是那个人
        long identity = heir.actor.GetPersonalIdentity()?.id ?? -heir.actor.Key - 2;
        var data = kingdom.GetOrCreate();
        if (data.logged_heir_identity == identity) return;
        data.logged_heir_identity = identity;
        // 秘密立储不昭告天下(见 LateImperialSystem)
        if (LateImperialSystem.SecretSuccession(kingdom)) return;
        TranslateHelper.LogKingChooseHeir(kingdom, heir.relation, heir.actor);
    }
#endif

    // 认祖归宗：储君是先君(本宗主位)的亲生子女、却在另一个世系里，挪进先君的宗族、随先君的姓(名字本来不带姓的文化不加姓)。
    // 外家入继的(先君外嫁女儿的后代)、兄弟旁支不是先君亲生的，不挪
#if EC_PENDING
    private static void ReturnToLine(PersonalClanIdentity parent, Person heir)
    {
        PersonalClanIdentity child = heir?.GetPersonalIdentity();
        SpecificClan line = parent?._specificClan;
        if (child == null || line == null || !parent.is_main || parent.is_concubine || child.id == parent.id ||
            child.father != parent.id && child.mother != parent.id && !parent.children.Contains(child.id) ||
            SpecificClanManager.SameLineage(child._specificClan, line)) return;
        SpecificClanManager.MoveToClan(child, line);
        string surname = !string.IsNullOrWhiteSpace(parent.surname) ? parent.surname : SurnameInheritanceSystem.SurnameOf(parent);
        if (line.Patrilineal() || line.Matrilineal() || !string.IsNullOrWhiteSpace(heir.GetModName()?.familyName))
            SurnameInheritanceSystem.ApplySurname(child, surname);
    }
#endif

    // 死亡、退位和真正的王位空缺才允许兄弟→孙辈→宗族→官员的原有兜底。
#if EC_PENDING
    public static void PrepareForSuccession(Realm kingdom, PersonalClanIdentity predecessor = null)
    {
        if (kingdom?.Record == null || EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(kingdom) ||
            !EmpireCraftKingdomBehCheckKing.NeedSuccession(kingdom)) return;
        InstallHeir(kingdom, ResolveHeir(kingdom, true, predecessor));
    }
#endif

    // 储君按规矩即不了位(不立女主的国里储君是女子：旧档留下的、别的途径立的)：储位作废、钦定的名分一并撤掉，
    // 当场照继承顺序另立一位，返回新储君；没有合规的人返回空(储位空着，君位照无人可立处理，见 EmpireCraftKingdomBehCheckKing)。
    // 以前只撤储位不撤钦定：下一轮王位检查又把同一个人立回来，君位一直空着，要把储君赐死才解得开
#if EC_PENDING
    public static Person ReplaceBarredHeir(Realm kingdom, Person barred)
    {
        if (kingdom?.Record == null) return null;
        var data = kingdom.GetOrCreate();
        if (barred != null && data.designated_heir_id == barred.Key) data.designated_heir_id = -1L;
        kingdom.RemoveHeir();
        PrepareForSuccession(kingdom);
        Person next = kingdom.GetHeir();
        if (next == null || next == barred || BarredBySex(kingdom, next) || ForeignRuler(kingdom, next))
        {
            kingdom.RemoveHeir();
            kingdom.StartToChooseHeir();
            return null;
        }
        return next;
    }

    public static PersonalClanIdentity PredecessorOf(Realm kingdom)
    {
        if (kingdom?.Ruler != null)
        {
            kingdom.Ruler.CheckSpecificClan(false);
            PersonalClanIdentity current = kingdom.Ruler.GetPersonalIdentity();
            if (current != null) return current;
        }
        return kingdom == null ? null : SpecificClanManager.getPerson(kingdom.GetOrCreate().last_ruler_identity_id);
    }
#endif

    // 成年、年幼的界线(继承排序用)
    private const int GrownAge = 16, TenderAge = 6;

    // 不立娃娃、兄终弟及的政体：游牧、阿拉伯、原始部落
#if EC_PENDING
    private static bool SeniorFirst(Realm kingdom) =>
        kingdom?.GetRegime()?.type is RegimeType.YouMu or RegimeType.NomadFeudalism or RegimeType.Arabic or RegimeType.Origin
            or RegimeType.OriginFeudalism;

    public static (Person actor, string relation) ResolveHeir(Realm kingdom, bool allowFallback,
        PersonalClanIdentity predecessor = null)
    {
        if (kingdom?.Record == null) return (null, "");
        // 剧本点名的继承名单优先：先帝后来添的皇子、强者继承都不顶替
        Person scripted = ScriptedHeir(kingdom);
        if (scripted != null)
            return (scripted, LM.Get("designated_heir_relation").ColorString(pColor: new Color(0.9f, 0.3f, 0.2f)));
        PersonalClanIdentity parent = predecessor ?? PredecessorOf(kingdom);
        if (parent == null) return (null, "");
        // 真正交接时不能受十秒修复间隔影响而漏掉异地子女、误选旁系。
        if (allowFallback && CityPopulationSystem.AbstractPopulationEnabled) ChildrenOf(parent, true);
        SuccessionLawType law = kingdom.GetSuccessionLaw();
        if (law == SuccessionLawType.强者继承法)
            return CheckStrongestHeir(kingdom, parent, !allowFallback);
        Person designated = DesignatedHeir(kingdom, parent);
        if (designated != null)
            return (designated, LM.Get("designated_heir_relation").ColorString(pColor: new Color(0.9f, 0.3f, 0.2f)));
        EmpireHeirLawType start = KingdomExtension.ResolveHeirLawStart(law);
        // 兄终弟及仍在交接时按兄弟顺序继承，在位时不预立旁系。
        if (!allowFallback && start == EmpireHeirLawType.siblings) return (null, "");
        var result = CheckHeir(kingdom, start, law, parent);
        // 兄终弟及(同"国赖长君"那份反馈)：游牧、阿拉伯、部落之国不立娃娃——真到交接时该立的子嗣未满 TenderAge 岁，
        // 而先君有成年的兄弟，由兄弟即位。华夏、西方照旧幼主即位加辅政
        if (allowFallback && SeniorFirst(kingdom) && start != EmpireHeirLawType.siblings &&
            result.actor?.Record != null && result.actor.Age() < TenderAge)
        {
            var sibling = CheckHeir(kingdom, EmpireHeirLawType.siblings, law, parent);
            if (sibling.actor?.Record != null && sibling.actor.Age() >= GrownAge)
            {
                LogService.LogInfo(
                    $"[EmpireCraft][继承] {kingdom.GetKingdomName()} 子嗣 {result.actor.FullName()} 年仅 {result.actor.Age()} 岁，由先君之弟 {sibling.actor.FullName()} 即位");
                return sibling;
            }
        }
        if (result.actor != null || !allowFallback) return result;
        // 帝国皇位：先君的孙辈仍是直系；子孙都没有、生前又未立储的，交群臣议立(见 SuccessionCouncilSystem)
        if (SuccessionCouncilSystem.Applies(kingdom))
        {
            if (start != EmpireHeirLawType.grand_child_generation)
            {
                result = CheckHeir(kingdom, EmpireHeirLawType.grand_child_generation, law, parent);
                if (result.actor != null) return result;
            }
            // 有藩王一系的：最近的藩王直接即位，不交群臣议立(见 PrinceLineage.DirectPrince)
            var prince = PrinceLineage.DirectPrince(kingdom, parent);
            if (prince.actor != null) return prince;
            var council = SuccessionCouncilSystem.Resolve(kingdom, parent);
            if (council.handled) return (council.actor, council.relation);
        }
        foreach (EmpireHeirLawType step in new[] { EmpireHeirLawType.siblings, EmpireHeirLawType.grand_child_generation,
                     EmpireHeirLawType.random, EmpireHeirLawType.officer })
        {
            // 西方封建制(女性可继承)：本宗旁支之前先按血缘远近找外嫁宗亲的后代入继(见 RoyalMarriageSystem)
            if (step == EmpireHeirLawType.random && PersonalUnionService.AllowsFemaleSuccession(kingdom))
            {
                var cognate = CognaticHeir(kingdom, parent);
                if (cognate.actor != null) return cognate;
            }
            if (step == start || step == EmpireHeirLawType.officer && kingdom.IsEmpire()) continue;
            result = CheckHeir(kingdom, step, law, parent);
            if (result.actor != null) return result;
        }
        return (null, "");
    }

    private static List<PersonalClanIdentity> ChildrenOf(PersonalClanIdentity parent, bool forceRepair = false)
    {
        var children = new Dictionary<long, PersonalClanIdentity>();
        foreach (var pair in SpecificClanManager.getChildren(parent))
            if (pair.Item2 != null) children[pair.Item2.id] = pair.Item2;
        // 旧档可能有原版亲子关系，却没有完整的模组索引；只补实际在世单位的缺失链接。
        Person actor = parent?._actor;
        if (actor != null)
        {
            bool virtualPopulation = CityPopulationSystem.AbstractPopulationEnabled;
            IEnumerable<Person> nativeChildren = virtualPopulation ? actor.getChildren() :
                actor.getChildren(pOnlyCurrentFamily: false);
            int knownLiving = children.Values.Count(person => person._actor != null && !person._actor.IsGone() && person._actor.Alive);
            if (virtualPopulation && (forceRepair || knownLiving < actor.current_children_count))
            {
                ChildRepairState repair = ChildRepairs.GetValue(actor, _ => new ChildRepairState());
                if (forceRepair || Time.unscaledTime >= repair.NextScan)
                {
                    repair.NextScan = Time.unscaledTime + 10f;
                    nativeChildren = nativeChildren.Concat(actor.getChildren(pOnlyCurrentFamily: false));
                }
            }
            foreach (Person child in nativeChildren)
            {
                if (child == null || child.IsGone() || !child.Alive) continue;
                child.CheckSpecificClan(false);
                PersonalClanIdentity identity = child.GetPersonalIdentity();
                if (identity == null) continue;
                if (!parent.children.Contains(identity.id)) identity.setParent(parent, recordHistory: false);
                foreach (Person otherParent in child.getParents())
                {
                    if (otherParent == null || otherParent == actor) continue;
                    otherParent.CheckSpecificClan(false);
                    PersonalClanIdentity other = otherParent.GetPersonalIdentity();
                    if (other != null && (other.sex == ActorSex.Male ? identity.father <= 0 : identity.mother <= 0))
                        identity.setParent(other, recordHistory: false);
                }
                children[identity.id] = identity;
            }
        }
        return children.Values.ToList();
    }
#endif

    // 在先君的世系里(本宗或分出去的诸王各支)：立储时先于生在别的宗族的子女——那样的子女即位，皇室就换了姓
    private static bool InLine(PersonalClanIdentity person, PersonalClanIdentity parent) =>
        SpecificClanManager.SameLineage(person?._specificClan, parent?._specificClan);

#if EC_PENDING
    private static bool IsEligibleRelative(PersonalClanIdentity person, PersonalClanIdentity parent, bool allowFemale,
        bool maleOnly) =>
        person != null && person.id != parent?.id && person.is_alive && !person.is_concubine &&
        person._specificClan != null && SexEligible(person, parent, maleOnly, allowFemale);
#endif

    // 按性别、主次能不能继位。不立女主的国(maleOnly，见 MaleOnlyThrone)只认男子，不看宗族是男子优先还是女子优先：
    // 宗族的偏好按始祖的文化定，国按本国文化定，两边对不上时(异族宗族入主、母系氏族阶段、旧档里女子优先的宗族)
    // 以前只挑得出女子，挑出来即位时又被退回，君位一直空着。女子优先的宗族里成了婚的男子记作入赘(is_main 为假)，
    // 生在本族的照样算，嫁入、入赘来的外姓人不算。其余的国照宗族的偏好，允许女子继承的再放开女子
#if EC_PENDING
    public static bool SexEligible(PersonalClanIdentity person, PersonalClanIdentity parent, bool maleOnly, bool allowFemale)
    {
        if (person == null) return false;
        if (!maleOnly) return person.CanHeir(parent) || allowFemale && person.sex == ActorSex.Female;
        return person.sex == ActorSex.Male && !person.disinherited && person.id != parent?.id &&
               (person.is_main || BornInLine(person));
    }
#endif

    // 生在本族：父亲或母亲和本人同一世系
    private static bool BornInLine(PersonalClanIdentity person)
    {
        PersonalClanIdentity father = person.father > 0 ? SpecificClanManager.getPerson(person.father) : null;
        if (father != null && SpecificClanManager.SameLineage(father._specificClan, person._specificClan)) return true;
        PersonalClanIdentity mother = person.mother > 0 ? SpecificClanManager.getPerson(person.mother) : null;
        return mother != null && SpecificClanManager.SameLineage(mother._specificClan, person._specificClan);
    }

    // 钦定的储君：在世、可继位、仍是先君子嗣(新君即位后自然失效)
    // 剧本继承名单里头一位还在本国(帝国连同成员)、还能继位的；死了、被俘、已经即位的划掉
#if EC_PENDING
    private static Person ScriptedHeir(Realm kingdom)
    {
        List<long> heirs = kingdom.GetOrCreate().scripted_heirs;
        if (heirs == null || heirs.Count == 0) return null;
        long id = EmpireCraft.Core.ScenarioPlotRules.NextHeir(heirs, candidate =>
        {
            Person actor = EmpireWorld.Current.PersonByKey(candidate);
            return actor != kingdom.Ruler && IsFitCandidate(actor, null) && !ForeignRuler(kingdom, actor) &&
                   !BarredBySex(kingdom, actor) &&
                   (actor.Realm == kingdom || kingdom.IsEmpire() && actor.Realm?.GetEmpire() == kingdom.GetEmpire());
        });
        return id < 0L ? null : EmpireWorld.Current.PersonByKey(id);
    }

    private static Person DesignatedHeir(Realm kingdom, PersonalClanIdentity parent)
    {
        var data = kingdom.GetOrCreate();
        if (data.designated_heir_id < 0) return null;
        Person actor = EmpireWorld.Current.PersonByKey(data.designated_heir_id);
        actor?.CheckSpecificClan(false);
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        // 不立女主的国钦定不了女储君(旧档里已经钦定的在这里作废，照继承顺序另立)
        bool barred = BarredBySex(kingdom, actor);
        bool valid = identity != null && actor != kingdom.Ruler && IsFitCandidate(actor, parent) && !ForeignRuler(kingdom, actor) &&
                     !barred && ChildrenOf(parent).Any(child => child.id == identity.id) &&
                     (!kingdom.IsEmpire() || !ConstitutionalSuccessionSystem.ThroneBound(kingdom.GetEmpire()) ||
                      ConstitutionalSuccessionSystem.CanInherit(kingdom.GetEmpire(), actor));
        if (!valid) data.designated_heir_id = -1L;
        if (barred)
            LogService.LogInfo(
                $"[EmpireCraft][继承] {kingdom.GetKingdomName()} 钦定的储君 {actor.FullName()} 是女子，本国不立女主：储位作废，照继承顺序另立");
        return valid ? actor : null;
    }
#endif

    // 册立储君的候选：先君(在位皇帝)在世、可继位的子嗣。不立女主的国只列皇子(见 MaleOnlyThrone)：
    // 以前公主也在列，册立、秘密立储(按才具挑)、手令都能把公主立为储君，君主身故后她即不了位、又一直占着储位
    // 无小人模式下皇子多是虚拟族人：只数地图上的，"两位以上皇子才议立储"几乎永远凑不齐。
    // realize：把虚拟皇子落成实体一起列上(要挑出一个人来用时才落成，如秘密立储、诉求选目标)；不落成时只列地图上的
#if EC_PENDING
    public static List<Person> HeirCandidates(Realm kingdom, bool realize = false)
    {
        PersonalClanIdentity parent = PredecessorOf(kingdom);
        if (parent == null) return new List<Person>();
        bool maleOnly = MaleOnlyThrone(kingdom);
        bool Fit(Person actor) => IsFitCandidate(actor, parent) && actor != kingdom.Ruler && !ForeignRuler(kingdom, actor) &&
                                 !(maleOnly && actor.Record.sex != ActorSex.Male);
        List<PersonalClanIdentity> children = ChildrenOf(parent);
        List<Person> result = children.Select(person => person._actor).Where(Fit).Distinct().ToList();
        if (!realize) return result;
        foreach (Person actor in KinRoster.Realize(children.Where(person => KinRoster.Virtual(person) &&
                     VirtualHeirFit(person, parent, maleOnly)), Fit))
            if (!result.Contains(actor)) result.Add(actor);
        return result;
    }
#endif

    // 册立储君的候选连虚拟皇子一共几人(不落成)；adultOnly 只数成年的
#if EC_PENDING
    public static int HeirCandidateCount(Realm kingdom, bool adultOnly = false)
    {
        PersonalClanIdentity parent = PredecessorOf(kingdom);
        if (parent == null) return 0;
        bool maleOnly = MaleOnlyThrone(kingdom);
        return ChildrenOf(parent).Count(person => KinRoster.Counts(person,
            virtualPerson => VirtualHeirFit(virtualPerson, parent, maleOnly) && (!adultOnly || KinRoster.Adult(virtualPerson)),
            actor => IsFitCandidate(actor, parent) && actor != kingdom.Ruler && !ForeignRuler(kingdom, actor) &&
                     !(maleOnly && actor.Record.sex != ActorSex.Male) && (!adultOnly || actor.IsAdult())));
    }
#endif

    // 虚拟皇子按族谱能不能列为储君候选(同 IsFitCandidate：没被废储；不立女主的国只列皇子)
#if EC_PENDING
    private static bool VirtualHeirFit(PersonalClanIdentity person, PersonalClanIdentity parent, bool maleOnly) =>
        person != null && person.is_alive && person.id != parent?.id && !person.disinherited &&
        !(maleOnly && person.sex != ActorSex.Male);
#endif

    // 宗人府废了储的(见 EmpireLawSystem.ApplyRoyalCourt)不再入继承人之列
#if EC_PENDING
    private static bool IsFitCandidate(Person actor, PersonalClanIdentity parent) =>
        actor != null && actor != parent?._actor && !actor.IsGone() && actor.Alive && actor.isUnitFitToRule() &&
        actor.GetPersonalIdentity()?.disinherited != true;
#endif

    // 已经是别国君主的人不按继承法入继(用户 2026-10-16："明明南北朝了，两边的皇帝有时候还会通过继承法同时继承给一个人，
    // 这是华夏又不是西方")：
    //   · 华夏：本帝国以外的君主一概不入继(本帝国的藩王入继大统照旧)；
    //   · 任何文化：别的帝国的皇帝不入继——两朝、两个帝国不因继承法合到一人身上。
    // 一朝绝嗣、群臣迎立另一朝皇帝兼承大统另走廷议(见 SuccessionCouncilSystem、EmpirePartitionSystem.Reunite)；
    // 西方封建、古典共和王国之间的共主联盟不受此限(见 PersonalUnionService)
#if EC_PENDING
    public static bool ForeignRuler(Realm kingdom, Person actor)
    {
        if (kingdom == null || actor?.Record == null || !actor.IsRuler() || actor.Realm == null || actor.Realm == kingdom) return false;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && actor.Realm.GetEmpire() == empire) return false;
        if (actor.Realm.IsEmpire() && kingdom.IsEmpire()) return true;
        try
        {
            return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) == "Huaxia";
        }
        catch
        {
            return false;
        }
    }
#endif

    // 华夏一系不立女主(用户 2026-10-14："不会存在女帝，尤其是华夏，至少继承法不会支持")：宗室继承本就只传男，
    // 群臣劝进的权臣重臣、从都城人口里落成的人、已定下的继承人也照此。以前皇族无人可继时劝进的重臣是女子，
    // 女帝与皇夫所生的子女读档时被挪进皇夫一族，帝系就改了姓(聂高日之后成了崔氏)
#if EC_PENDING
    public static bool MaleOnlyThrone(Realm kingdom)
    {
        if (kingdom?.Record == null) return false;
        try
        {
            return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(kingdom)) == "Huaxia";
        }
        catch
        {
            return false;
        }
    }

    public static bool BarredBySex(Realm kingdom, Person actor) =>
        actor?.Record != null && actor.Record.sex != ActorSex.Male && MaleOnlyThrone(kingdom);

    private static Person Pick(Realm kingdom, IEnumerable<PersonalClanIdentity> candidates, PersonalClanIdentity parent,
        out PersonalClanIdentity selected)
    {
        bool protectedDynasty = kingdom.IsEmpire() && ConstitutionalSuccessionSystem.ThroneBound(kingdom.GetEmpire());
        bool maleOnly = MaleOnlyThrone(kingdom);
        foreach (PersonalClanIdentity person in candidates)
        {
            Person actor = person.Realize();
            // 族谱记的性别和落成的人对不上时以人为准(即位时看的是人，见 BarredBySex)
            if (!IsFitCandidate(actor, parent) || ForeignRuler(kingdom, actor) || maleOnly && actor.Record.sex != ActorSex.Male ||
                protectedDynasty && !ConstitutionalSuccessionSystem.CanInherit(kingdom.GetEmpire(), actor)) continue;
            selected = person;
            return actor;
        }
        selected = null;
        return null;
    }

    private static (Person actor, string relation) CheckHeir(Realm kingdom, EmpireHeirLawType selection,
        SuccessionLawType law, PersonalClanIdentity parent)
    {
        // 不立女主的国只在男子里排(见 SexEligible)，政体许不许女子继承、宗族偏好哪一边都不看
        bool maleOnly = MaleOnlyThrone(kingdom);
        bool allowFemale = !maleOnly && PersonalUnionService.AllowsFemaleSuccession(kingdom);
        bool Preferred(PersonalClanIdentity person) =>
            maleOnly ? person.sex == ActorSex.Male : person._specificClan != null && person.IsHeirPriority();
        string relation = LM.Get(selection.ToString());
        IEnumerable<PersonalClanIdentity> candidates;
        switch (selection)
        {
            case EmpireHeirLawType.eldest_child:
            case EmpireHeirLawType.smallest_child:
                // 嫡长子继承：嫡长子早逝而身后有子，嫡长孙承重，先于诸叔
                if (selection == EmpireHeirLawType.eldest_child && law == SuccessionLawType.嫡长子继承法)
                {
                    PersonalClanIdentity first = ChildrenOf(parent).Where(person => Preferred(person) && !person.is_concubine)
                        .OrderByDescending(person => InLine(person, parent))
                        .ThenByDescending(person => IsBornOfPrimarySpouse(parent, person)).ThenBy(BirthKey).FirstOrDefault();
                    if (first != null && !first.is_alive)
                    {
                        var grandsons = ChildrenOf(first).Where(person => IsEligibleRelative(person, first, allowFemale, maleOnly))
                            .OrderByDescending(Preferred)
                            .ThenByDescending(person => IsBornOfPrimarySpouse(first, person)).ThenBy(BirthKey);
                        Person grandson = Pick(kingdom, grandsons, parent, out _);
                        if (grandson != null)
                            return (grandson, LM.Get("heir_relation_eldest_grandson").ColorString(pColor: new Color(0.9f, 0.3f, 0.2f)));
                    }
                }
                var children = ChildrenOf(parent).Where(person => IsEligibleRelative(person, parent, allowFemale, maleOnly)).ToList();
                // 国赖长君(群友 2026-11-22 "目前有君主绝嗣和君主超低龄化的问题")：先君有成年的儿子时，未满 TenderAge 岁的排到后面——
                // 以前嫡出排在年龄前面，三岁的嫡子压过二十五岁的庶长子；幼子继承更是永远挑最小的那个，襁褓里的也照立。
                // 诸子都没成年的照旧按嫡庶长幼(幼主即位，辅政、太后照常，见 RealisticAgeSystem.IsMinor)
                bool hasAdult = children.Any(person => person.age >= GrownAge);
                // 嫡子优先，嫡系没有合适儿子时仍在自己的子嗣中接续，不提前跳到叔伯。
                // 本宗世系里的子女先于生在别的宗族的(见 InLine)
                var ordered = children.OrderByDescending(person => InLine(person, parent)).ThenByDescending(Preferred)
                    .ThenByDescending(person => !hasAdult || person.age >= TenderAge)
                    .ThenByDescending(person => law == SuccessionLawType.嫡长子继承法 && IsBornOfPrimarySpouse(parent, person));
                // 幼子继承：成年的儿子里挑最小的，都没成年才挑最小的
                candidates = selection == EmpireHeirLawType.smallest_child
                    ? ordered.ThenByDescending(person => person.age >= GrownAge).ThenBy(person => person.age)
                        .ThenByDescending(person => person.rank).ThenBy(person => person.id)
                    : ordered.ThenByDescending(person => person.age).ThenBy(person => person.rank).ThenBy(person => person.id);
                Person child = Pick(kingdom, candidates, parent, out PersonalClanIdentity selected);
                if (child != null)
                    relation = string.Format(LM.Get($"rank_child_{(kingdom.IsEmpire() ? "empire" : "kingdom")}_{(selected.sex == ActorSex.Female ? "female" : "male")}"), selected.rank);
                return (child, relation.ColorString(pColor: new Color(0.9f, 0.3f, 0.2f)));
            case EmpireHeirLawType.siblings:
                candidates = SpecificClanManager.GetSiblingsWithRelation(parent).Select(pair => pair.Item2)
                    .Where(person => IsEligibleRelative(person, parent, allowFemale, maleOnly))
                    .OrderByDescending(Preferred).ThenByDescending(person => person.age).ThenBy(person => person.id);
                break;
            case EmpireHeirLawType.grand_child_generation:
                candidates = SpecificClanManager.GetGrandChildren(parent).Select(pair => pair.Item2)
                    .Where(person => IsEligibleRelative(person, parent, allowFemale, maleOnly))
                    .OrderByDescending(Preferred).ThenByDescending(person => person.age).ThenBy(person => person.id);
                break;
            case EmpireHeirLawType.random:
                if (kingdom.IsEmpire() && ConstitutionalSuccessionSystem.ThroneBound(kingdom.GetEmpire()))
                    return (ConstitutionalSuccessionSystem.SelectRoyalHeir(kingdom.GetEmpire()), ConstitutionalSuccessionSystem.SelectionRelation(kingdom.GetEmpire()));
                // 绝嗣：从宗室里按亲疏找人入继(隔几代找到共同的祖先)，同样亲疏的先立成年的、三十岁上下的
                // (帝国皇位另由群臣议立，见 SuccessionCouncilSystem)
                // 整个世系：本宗和分出去的各支(受封诸王一系)
                candidates = SpecificClanManager.LineageClans(parent._specificClan).SelectMany(clan => clan.SnapshotPeople());
                Dictionary<long, int> ancestors = Ancestors(parent);
                candidates = candidates.Where(person => IsEligibleRelative(person, parent, allowFemale, maleOnly))
                    .OrderBy(person => Kinship(person, ancestors)).ThenByDescending(person => person.age >= 18)
                    .ThenBy(person => Math.Abs(person.age - 30)).ThenBy(person => person.id);
                relation = LM.Get("heir_relation_collateral");
                break;
            case EmpireHeirLawType.officer:
                if (kingdom.IsEmpire()) return (null, relation);
                Person officer = kingdom.Cities.Select(city => city?.Leader)
                    .FirstOrDefault(actor => IsFitCandidate(actor, parent) && !(maleOnly && actor.Record.sex != ActorSex.Male));
                return (officer, relation);
            default: return (null, relation);
        }
        return (Pick(kingdom, candidates, parent, out _), relation.ColorString(pColor: new Color(0.3f, 0.3f, 0.9f)));
    }
#endif

    // 外家入继：沿父母、子女找先君六步以内的血亲(含外嫁女子的后代)，近的在前；入继者本是别国君主的两国共主
#if EC_PENDING
    private static (Person actor, string relation) CognaticHeir(Realm kingdom, PersonalClanIdentity parent)
    {
        bool protectedDynasty = kingdom.IsEmpire() && ConstitutionalSuccessionSystem.ThroneBound(kingdom.GetEmpire());
        int realized = 0;
        foreach ((PersonalClanIdentity person, int _) in RoyalMarriageSystem.Cognates(parent, 6))
        {
            if (person.is_virtual && realized >= 6) continue;
            if (person.is_virtual) realized++;
            Person actor = person.Realize();
            if (!IsFitCandidate(actor, parent) || actor.IsEmperor() || ForeignRuler(kingdom, actor) ||
                BarredBySex(kingdom, actor) || protectedDynasty && !ConstitutionalSuccessionSystem.CanInherit(kingdom.GetEmpire(), actor)) continue;
            return (actor, LM.Get("heir_relation_cognatic").ColorString(pColor: new Color(0.3f, 0.6f, 0.9f)));
        }
        return (null, "");
    }
#endif

    // 议立新君的宗室人选池：先君以外、可以继位的宗亲(兄弟、叔侄、族人)，附亲疏(隔几代)
#if EC_PENDING
    public static List<(PersonalClanIdentity person, int kinship)> CollateralPool(Realm kingdom, PersonalClanIdentity parent)
    {
        var result = new List<(PersonalClanIdentity, int)>();
        if (kingdom == null || parent == null) return result;
        bool maleOnly = MaleOnlyThrone(kingdom);
        bool allowFemale = !maleOnly && PersonalUnionService.AllowsFemaleSuccession(kingdom);
        Dictionary<long, int> ancestors = Ancestors(parent);
        var people = new Dictionary<long, PersonalClanIdentity>();
        foreach (var pair in SpecificClanManager.GetSiblingsWithRelation(parent))
            if (pair.Item2 != null) people[pair.Item2.id] = pair.Item2;
        // 本宗与分出去的各支(受封诸王一系仍是宗室，见 ClanBranchSystem)，皇族世系也算
        var clans = SpecificClanManager.LineageClans(parent._specificClan);
        SpecificClan royal = kingdom.IsEmpire() ? kingdom.GetEmpire()?.EmpireSpecificClan : null;
        if (royal != null) foreach (SpecificClan member in SpecificClanManager.LineageClans(royal)) if (!clans.Contains(member)) clans.Add(member);
        IEnumerable<PersonalClanIdentity> clan = clans.SelectMany(member => member.SnapshotPeople());
        foreach (PersonalClanIdentity person in clan)
            if (person != null) people[person.id] = person;
        foreach (PersonalClanIdentity person in people.Values)
            if (IsEligibleRelative(person, parent, allowFemale, maleOnly) && person._actor?.IsEmperor() != true &&
                !ForeignRuler(kingdom, person._actor))
                result.Add((person, Kinship(person, ancestors)));
        // 先君的子孙不算旁支，照常；旁支入继先从藩王一系里找(见 PrinceLineage)
        var direct = result.Where(item => item.Item1.father == parent.id || item.Item1.mother == parent.id).ToList();
        if (direct.Count > 0) return result;
        return PrinceLineage.PreferPrinces(result, item => item.Item1);
    }
#endif

    // 出生先后(生日"11,062年5月20日")；解析不了按编号
    private static long BirthKey(PersonalClanIdentity person)
    {
        var match = global::System.Text.RegularExpressions.Regex.Match(person?.birthday ?? "", @"([\d,]+)年(\d+)月(\d+)日");
        if (!match.Success || !long.TryParse(match.Groups[1].Value.Replace(",", ""), out long year)) return long.MaxValue / 2 + (person?.id ?? 0) % 1000000;
        return year * 10000 + int.Parse(match.Groups[2].Value) * 100 + int.Parse(match.Groups[3].Value);
    }

    // 往上八代的祖先 → 隔几代
    private static Dictionary<long, int> Ancestors(PersonalClanIdentity person)
    {
        var result = new Dictionary<long, int>();
        PersonalClanIdentity current = person;
        for (int generation = 0; current != null && generation <= 8; generation++)
        {
            result[current.id] = generation;
            current = current.father > 0 ? SpecificClanManager.getPerson(current.father) : null;
        }
        return result;
    }

    // 亲疏：两人各自往上几代到共同的祖先，合起来的代数(找不到算很远)
    private static int Kinship(PersonalClanIdentity person, Dictionary<long, int> ancestors)
    {
        PersonalClanIdentity current = person;
        for (int up = 0; current != null && up <= 8; up++)
        {
            if (ancestors.TryGetValue(current.id, out int down)) return up + down;
            current = current.father > 0 ? SpecificClanManager.getPerson(current.father) : null;
        }
        return 99;
    }

#if EC_PENDING
    private static bool IsBornOfPrimarySpouse(PersonalClanIdentity parent, PersonalClanIdentity child)
    {
        long otherId = parent.sex == ActorSex.Male ? child.mother : child.father;
        if (otherId <= 0) return false;
        if (parent.lover.identity == otherId) return true;
        // 再婚不能把前任正妻所生的孩子全部降为非嫡。
        PersonalClanIdentity other = SpecificClanManager.getPerson(otherId);
        return other != null && !other.is_concubine && !parent.concubines.Any(pair => pair.identity == otherId);
    }

    private static (Person actor, string relation) CheckStrongestHeir(Realm kingdom, PersonalClanIdentity parent, bool childrenOnly)
    {
        IEnumerable<PersonalClanIdentity> pool = ChildrenOf(parent);
        if (!childrenOnly) pool = pool.Concat(SpecificClanManager.GetSiblingsWithRelation(parent).Select(pair => pair.Item2));
        bool maleOnly = MaleOnlyThrone(kingdom);
        var candidates = pool.Where(person => IsEligibleRelative(person, parent, false, maleOnly))
            // 政绩(没做过官的算零)，再看声望：虚拟族人按并回人口时记下的声望折算。以前没实体的一律排在最后
            .OrderByDescending(person => KinRoster.OnMap(person) ? person._actor.GetIdentity()?.TotalPerformance ?? 0d : 0d)
            .ThenByDescending(person => KinRoster.OnMap(person) ? person._actor.renown : VirtualGenealogySystem.VirtualRenown(person))
            .ThenByDescending(person => person.age).ThenBy(person => person.id);
        return (Pick(kingdom, candidates, parent, out _),
            LM.Get(SuccessionLawType.强者继承法.ToString()).ColorString(pColor: new Color(0.6f, 0.05f, 0.05f)));
    }
#endif
}
}
