// Ported from the EmpireCraft mod: Scripts/HelperFunc/EnfeoffmentHelper.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 把兄弟分封为附庸国的原语,原本只写在 TempFac_分封(周期性诉求,一次只分封一人)里；
// 提炼出来供分割继承法(一次继承事件里批量分封新君的所有合法兄弟)复用，行为完全不变。
public static class EnfeoffmentHelper
{
    // 首都身份以国家当前指针为准，不能只依赖可能尚未同步的法理编号。
    internal static bool IsRealmCapital(City city) => city != null &&
        (city.Realm?.Capital == city || city.IsCapital());

    // 按法理分封：可分的是核心王国手里的法理头衔(整块封出去)，但王畿不分——都城所在的头衔、
    // 以及跟帝国同名的头衔留给天子。之前是挑一座城，并且跳过帝国核心法理内的所有城市；而帝国
    // 建立时会把当时控制的全部法理都登记为核心，结果一般帝国一座可封的城都找不到，分封从不发生。
    // 城多的头衔排在前面，年长的兄弟先挑。
    // 原始政体没有封爵分土的制度，一律不分封(玩家反馈：原始体制能分封)
#if EC_PENDING
    public static bool RegimeForbids(Empire empire) => RegimeManager.IsPrimitive(empire?.CoreKingdom?.GetRegime()?.type);
#endif

    // 游牧几乎年年在打仗，等停战再分就永远分不了：游牧打仗时也分，只是前线的地不分(见 AtFront)
#if EC_PENDING
    public static bool Nomadic(Empire empire) => empire?.CoreKingdom?.GetRegime()?.type == RegimeType.YouMu;
#endif

    // 兄弟分封(新君即位分封、分封诉求)仍只有游牧战时照分，别的政体等停战：打仗时找不到不在前线的法理，
    // 即位分封会改发正统补偿、不再挂起，别的政体也放开就成了战时兄弟分不到地，所以不跟领土划分(见 DeferCity)一起改
#if EC_PENDING
    public static bool WarBlocks(Empire empire) => empire != null && empire.HasTerritorialWar() && !Nomadic(empire);
#endif

    // 前线：城里有敌军占着的地块，或挨着与帝国核心(或本城所属成员国)交战之国的城
#if EC_PENDING
    public static bool IsFrontCity(City city, Realm core)
    {
        if (city?.Record == null || city.IsGone() || city.neighbours_cities == null) return false;
        if (city.HasOccupiedZones()) return true;
        Realm owner = city.Realm;
        foreach (City other in city.neighbours_cities)
        {
            Realm enemy = other?.Realm;
            if (enemy == null || other.IsGone() || enemy == core || enemy == owner) continue;
            if (FrontSystem.Hostile(core, enemy) || owner != null && owner != core && FrontSystem.Hostile(owner, enemy))
                return true;
        }
        return false;
    }
#endif

    // 划行政区、设行省、分封打下的地时先不动的城(用户 2026-10-19 选"所有政体都改")：
    // 以前帝国里任何一国有敌人，这些就整个挂起等停战；阿拉伯、封建、周制、幕府年年在打，打下的地从来设不成行省、封不出去。
    // 现在战时照样划分，只跳过前线的城、战时代管等战后分配的城(见 WartimeCustodySystem)、正与中央交战的成员的城；停战后照旧
#if EC_PENDING
    public static bool DeferCity(City city, Realm core, bool atWar) =>
        atWar && (WartimeCustodySystem.InCustody(city) || Contested(city?.Realm, core) || IsFrontCity(city, core));
#endif

    // 正在叛乱、或与中央交战的成员：战时不从它那里划城，也不往它那里划
#if EC_PENDING
    public static bool Contested(Realm kingdom, Realm core) =>
        kingdom?.Record != null && core != null && kingdom != core &&
        (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling() || kingdom.isInWarWith(core));
#endif

    // 法理里归本帝国的城有一座在前线就算前线(别国手里的城不算)
#if EC_PENDING
    public static bool AtFront(KingdomTitle title, Realm core)
    {
        foreach (City city in title?.getCities() ?? Enumerable.Empty<City>())
            if (InRealm(city, core) && IsFrontCity(city, core)) return true;
        return false;
    }

    public static bool TitleDeferred(KingdomTitle title, Realm core, bool atWar)
    {
        foreach (City city in title?.getCities() ?? Enumerable.Empty<City>())
            if (InRealm(city, core) && DeferCity(city, core, atWar)) return true;
        return false;
    }
#endif

    // 成员国有一座城在前线就算前线(周制战时改封皇子用，见 FeudalConquestService)
#if EC_PENDING
    public static bool KingdomAtFront(Realm kingdom, Realm core)
    {
        if (kingdom?.Cities == null || kingdom.IsGone()) return false;
        foreach (City city in kingdom.Cities)
            if (IsFrontCity(city, core)) return true;
        return false;
    }

    private static bool InRealm(City city, Realm core)
    {
        Realm owner = city?.Realm;
        return owner != null && !city.IsGone() && (owner == core || owner.GetEmpire()?.CoreKingdom == core);
    }

    public static List<KingdomTitle> FindEnfeoffableTitles(Empire empire)
    {
        Realm coreKingdom = empire?.CoreKingdom;
        if (coreKingdom?.Capital == null || ModClass.KINGDOM_TITLE_MANAGER == null || RegimeForbids(empire))
            return new List<KingdomTitle>();
        KingdomTitle capitalTitle = coreKingdom.Capital.GetTitle();
        string empireName = empire.GetEmpireName();
        bool atWar = empire.HasTerritorialWar();
        return ModClass.KINGDOM_TITLE_MANAGER
            .Where(title => title != null && !title.IsGone() && title != capitalTitle &&
                            !string.Equals(title.data?.name, empireName, StringComparison.Ordinal))
            .Where(title => title.title_capital != null && !title.title_capital.IsGone() &&
                            title.title_capital.kingdom == coreKingdom && title.title_capital != coreKingdom.Capital &&
                            !IsRealmCapital(title.title_capital) &&
                            // 战时代管的城等战后分配完再说
                            !EmpireCraft.Core.WartimeCustodySystem.InCustody(title.title_capital))
            .Where(title => !title.getCities().Any(city => city == coreKingdom.Capital ||
                city?.id == coreKingdom.Capital.Key))
            .Where(title => !atWar || !AtFront(title, coreKingdom))
            .OrderByDescending(title => title.getCities().Count(city => city != null && !city.IsGone() &&
                                                                       city.kingdom == coreKingdom))
            .ThenBy(title => title.id)
            .ToList();
    }

    public static KingdomTitle FindEnfeoffableTitle(Empire empire) => FindEnfeoffableTitles(empire).FirstOrDefault();
#endif

    // 兼容"分封"诉求等旧调用：返回下一块可封法理的法理首府
#if EC_PENDING
    public static City FindEnfeoffableCity(Empire empire, EmpireCore empireCore) =>
        FindEnfeoffableTitle(empire)?.title_capital;

    public static Person FindEnfeoffmentCandidate(Empire empire)
    {
        PersonalClanIdentity emperorIdentity = empire?.Emperor?.GetPersonalIdentity();
        Person actor = SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
            .Select(item => item.Item2)
            .Where(identity => identity != null && IsEligibleSibling(identity._actor, empire))
            .OrderBy(identity => identity.rank)
            .Select(identity => identity._actor)
            .FirstOrDefault();
        if (actor != null || !CityPopulationSystem.AbstractPopulationEnabled) return actor;
        // 无小人模式：皇帝的兄弟多半已转为虚拟族人(族谱里在世、但地图上没有单位)。
        // 按长幼挑一位够资格的虚拟兄弟落成实体，再按常规条件校验
        foreach (PersonalClanIdentity sibling in SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
                     .Select(item => item.Item2)
                     .Where(identity => identity != null && identity.is_alive && identity.is_virtual &&
                                        identity.CanHeir(emperorIdentity) &&
                                        SpecificClanManager.SameLineage(identity._specificClan, empire.EmpireSpecificClan))
                     .OrderBy(identity => identity.rank)
                     .Take(3))
        {
            Person realized = sibling.Realize();
            if (IsEligibleSibling(realized, empire)) return realized;
        }
        return null;
    }

    public static bool IsEligibleSibling(Person actor, Empire empire)
    {
        if (actor == null || actor.IsGone() || actor.IsRuler() || empire?.Emperor == null)
            return false;
        PersonalClanIdentity emperorIdentity = empire.Emperor.GetPersonalIdentity();
        PersonalClanIdentity actorIdentity = actor.GetPersonalIdentity();
        if (emperorIdentity == null || actorIdentity == null || !actorIdentity.CanHeir(emperorIdentity)) return false;
        if (!SpecificClanManager.SameLineage(actorIdentity._specificClan, empire.EmpireSpecificClan)) return false;
        if (actor.Key == (empire.CoreKingdom?.GetHeir()?.Key ?? -1L)) return false;
        if (actor.HasVirtualEnfeoff(empire)) return false;
        if (actor.Realm?.GetEmpire() != empire) return false;
        return SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
            .Any(item => item.Item2?.id == actorIdentity.id);
    }
#endif

    // 把 actor 分封为附庸国国王；调用方需要自行保证 actor 已经通过 IsEligibleSibling 校验，
    // 这里只重新做一次城池/政体防御性检查(跟 TempFac_分封.Execute 完全一致)。
#if EC_PENDING
    public static bool TryEnfeoffActor(Empire empire, Person actor, int mandateReward = 10)
    {
        if (empire == null || WarBlocks(empire) || RegimeForbids(empire) || !IsEligibleSibling(actor, empire)) return false;
        return GrantTitle(empire, actor, FindEnfeoffableTitle(empire), mandateReward);
    }
#endif

    // 把一块法理整块封给 actor 立为成员国(兄弟分封、宗王分兀鲁思共用，见 PartialEnfeoffment)；人选由调用方校验
#if EC_PENDING
    internal static bool GrantTitle(Empire empire, Person actor, KingdomTitle fief, int mandateReward)
    {
        if (empire == null || actor == null || actor.IsGone() || RegimeForbids(empire)) return false;
        Realm coreKingdom = empire.CoreKingdom;
        Regime empireRegime = coreKingdom?.GetRegime();
        City city = fief?.title_capital;
        if (empireRegime == null || empireRegime.enfeoff_virtual_only || city == null || IsRealmCapital(city) ||
            city.Realm != coreKingdom)
        {
            return false;
        }

        City retainedCapital = coreKingdom.Capital;
        List<City> grantedCities = fief.getCities().ToList();
        if (grantedCities.Any(other => other == retainedCapital || other?.Key == retainedCapital?.Key)) return false;
        // 分封：受命立国，不许自立的闸放行(见 SecessionGate)
        Realm kingdom = SecessionGate.Sanctioned(() => city.makeOwnKingdom(actor));
        if (kingdom == null) return false;
        // 整块法理一起封出去：法理首府立国之后，同一法理下仍归核心王国的城市随之划入封国
        foreach (City other in grantedCities)
        {
            if (other == null || other.IsGone() || other == city || other.Realm != coreKingdom ||
                other == retainedCapital || IsRealmCapital(other) ||
                EmpireCraft.Core.WartimeCustodySystem.InCustody(other)) continue;
            other.JoinRealm(kingdom);
        }

        kingdom.SetRegimeType(empireRegime.type);
        kingdom.LoadRegime();
        Regime kingdomRegime = kingdom.GetRegime();
        if (kingdomRegime != null)
        {
            kingdomRegime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            kingdomRegime.SetAllowSupportCenterArmy(false);
            kingdomRegime.SetTaxLevel(TaxLevel.Low);
        }
        KingdomTitle title = city.GetTitle();
        if (title?.title_capital == city)
        {
            kingdom.SetMainTitle(title);
            kingdom.Ruler?.AddOwnedTitle(title);
        }
        empire.join(kingdom, pForce: true);
        kingdom.ReconcileMainTitle(title == null ? null : new[] { title });
        empire.SynchronizeLandedLegalTitles(kingdom);
        empire.AddMandate(mandateReward);
        actor.CheckSpecificClan(false);
        TranslateHelper.LogPeerageGranted(actor, empire,
            (title?.data?.name ?? kingdom.Name) + LM.Get("default_peerages_2"));
        return true;
    }
#endif

    // 分割继承法在没有城池可分/政体不支持真实分封(如 enfeoff_virtual_only)时的退路:
    // 数一数有几个兄弟本来"够资格"分到一份(除了缺地之外没有别的理由被排除)，
    // 好让调用方用头衔/俸禄+正统性做补偿，而不是直接假装分割继承法没发生。
#if EC_PENDING
    public static int CountCompensableSiblings(Empire empire)
    {
        PersonalClanIdentity emperorIdentity = empire?.Emperor?.GetPersonalIdentity();
        if (emperorIdentity == null) return 0;
        // 连虚拟族人一起数(纯净地图下兄弟多不在地图上，以前补偿几乎总是 0)；只是计数，不落成
        return KinRoster.Siblings(emperorIdentity).Count(identity => KinRoster.Counts(identity,
            person => person.CanHeir(emperorIdentity) && person.age >= KinRoster.AdultAge &&
                      SpecificClanManager.SameLineage(person._specificClan, empire.EmpireSpecificClan) &&
                      KinRoster.KingdomOf(person)?.GetEmpire() == empire,
            actor => IsEligibleSibling(actor, empire)));
    }
#endif

    // 兀鲁思(用户 2026-10-17 转群友"游牧太强了几乎不会分封"，选"汗死诸子分兀鲁思")：游牧政体的大汗死了、
    // 汗国的核心有 UlusMinCities 座城以上，新汗的兄弟(先汗诸子)各分一块法理做兀鲁思(成员国)，同分割继承法；
    // 打仗时等停了再分(见 Empire.update)。大汗弱时兀鲁思可以自立(见 kingdom_allow_independent)。
    // 门槛 8 → 5 城(用户 2026-10-21 选"游牧雄主死后真分裂"：小些的汗国汗死也分兀鲁思，诸子各据一方)
    // → 8 城(同日玩家反馈"帝国容易突然碎一地"，用户选回调：五城的小汗国也分，雄主一死新分的兀鲁思又成批自立)
    public const int UlusMinCities = 8;

    // 要游牧政体、且是真游牧(本国文化带 nomad 特性或水土整国游牧化，见 KingdomExtension.IsTrueNomad；用户 2026-10-21 选)：
    // 以前只看政体，阿兹特克、奥吉布瓦这类挂在游牧线、并不游牧的文化也分兀鲁思；征服王朝从俗改了政体的照旧不算
#if EC_PENDING
    public static bool UlusPartition(Empire empire) =>
        empire?.CoreKingdom?.GetRegime()?.type == RegimeType.YouMu &&
        EmpireCraft.Core.KingdomExtension.IsTrueNomad(empire.CoreKingdom) &&
        empire.CoreKingdom.countCities() >= UlusMinCities;
#endif

    // 新皇帝即位(不论是立储继承、选举还是拥立)时由 KingdomPatch 的 setKing 补丁调用：
    //   · 分割继承法、游牧兀鲁思：诸子分封；
    //   · 本文化已施行「分封建国」且尚未「削藩」：先君一死，新君的兄弟按法理裂土受封。
    // 政体只能虚封(如律令制)或已无法理可分时，按兄弟人数给正统性补偿。
#if EC_PENDING
    public static void OnEmperorSucceeded(Empire empire)
    {
        Realm coreKingdom = empire?.CoreKingdom;
        if (coreKingdom == null || coreKingdom.IsGone() || empire.Emperor == null) return;
        empire.data.succession_enfeoff_pending = false;
        bool divisionLaw = coreKingdom.GetSuccessionLaw() == SuccessionLawType.分割继承法 || UlusPartition(empire);
        bool enfeoffmentEnacted =
            InstitutionSystem.HasFeature(empire, InstitutionFeatures.SiblingEnfeoffment) &&
            !InstitutionSystem.HasFeature(empire, InstitutionFeatures.SiblingEnfeoffmentAbolished);
        if (!divisionLaw && !enfeoffmentEnacted) return;
        if (WarBlocks(empire))
        {
            empire.data.succession_enfeoff_pending = true;
            return;
        }

        int enfeoffed = EnfeoffAllEligibleSiblings(empire);
        if (enfeoffed > 0) return;
        int compensable = CountCompensableSiblings(empire);
        if (compensable > 0) empire.AddMandate(Math.Min(compensable, 3) * 3);
    }

    public static void ResumeSuccessionEnfeoff(Empire empire)
    {
        if (empire?.data?.succession_enfeoff_pending == true && !WarBlocks(empire))
            OnEmperorSucceeded(empire);
    }
#endif

    // 分割继承法专用:一次继承事件里把新君的所有合法兄弟依次分封为附庸国，而不是像
    // TempFac_分封 那样按周期一次只分封一个。每分封成功一人，该兄弟自身就不再满足
    // IsEligibleSibling(变成 isKing())，循环靠这个天然终止；没有可分封的城池/候选人时同样终止。
#if EC_PENDING
    public static int EnfeoffAllEligibleSiblings(Empire empire, int mandateRewardPerHeir = 5)
    {
        int enfeoffedCount = 0;
        Realm coreKingdom = empire?.CoreKingdom;
        Regime empireRegime = coreKingdom?.GetRegime();
        if (empireRegime == null || empireRegime.enfeoff_virtual_only) return 0;

        Person candidate;
        while ((candidate = FindEnfeoffmentCandidate(empire)) != null)
        {
            if (!TryEnfeoffActor(empire, candidate, mandateRewardPerHeir)) break;
            enfeoffedCount++;
        }
        return enfeoffedCount;
    }
#endif
}
}
