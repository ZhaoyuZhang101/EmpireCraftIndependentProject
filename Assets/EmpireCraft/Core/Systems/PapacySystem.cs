// Generated from the EmpireCraft mod: Scripts/GeneralSystems/PapacySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 西方(Roma 制度线)的教廷与世俗帝国：
//   · 教廷：控制本宗教圣地(Religion.CityID)的国家；它的君主就是教宗。圣地所在国不是教皇国、而是世俗强国(帝国成员或核心)时，
//     教廷落在世俗强国手里(教廷之囚)；
//   · 教权(每个宗教一个值 0~100)：每年向目标回归——基准 55，教廷是独立的教皇国 +10，各西方帝国施行教会叙任 +2、
//     君主叙任 −2、脱离罗马 −4(合计最多 ±20)，教廷之囚 −15；加冕、悔罪、圣战胜负另有加减；
//   · 加冕：西方帝国的新君每年有四成可能赴圣地请教宗加冕(献礼为年度经常收入的一成)，加冕后正统 +8；
//     登基三年仍未加冕正统 −5；脱离罗马或世俗化之后自行加冕 +3；帝国自己握有圣地的直接加冕
//     (封建制帝国圣地不在皇帝自家领内的，要先远征，见 HolyRomanSystem.Romzug)；
//   · 叙任权：教会叙任 / 政教协约 / 君主叙任，按施行的制度定(叙任权之争打赢也会改为君主叙任)；
//   · 什一税：每年把年度经常收入的 6% 按叙任权档位(全额 / 一半 / 不缴)上缴教廷；
//   · 西方大分裂期间各帝国按服从(见 PapacySystem.Antipope)：奉对立教宗的在对立教廷受冕(正统 +4)、什一税交对立教廷。
public static partial class PapacySystem
{
    public const string Line = "Roma";
    public const string NodeClerical = "western_clerical_supremacy";
    public const string NodeSecular = "western_secular_estates";
    public const string NodeConcordat = "western_concordat";
    public const string NodeReformation = "western_reformation";
    public const string NodeSecularization = "western_secularization";

    public const int InvestitureChurch = 0, InvestitureConcordat = 1, InvestitureCrown = 2;

    private const float BaseAuthority = 55f, PapalStateBonus = 10f, CaptivityPenalty = 15f, AuthorityDrift = 0.15f;
    private const float CoronationChance = 0.4f;
    private const float CoronationGift = 0.1f;
    private const int UncrownedGraceYears = 3;
    private const float TitheRate = 0.06f;
    public const int CrownedLegitimacy = 8, SelfCrownedLegitimacy = 3, UncrownedLegitimacy = -5;

    #region 查询

    public static PapalRelationState State(Empire empire)
    {
        if (empire?.data == null) return null;
        return empire.data.papal ??= new PapalRelationState();
    }

#if EC_PENDING
    public static Religion ReligionOf(Empire empire)
    {
        Realm core = empire?.CoreKingdom;
        return core != null && core.hasReligion() ? core.religion : null;
    }
#endif

    // 奉不奉教廷是宗教亲和，照风俗文化(旧制犹存不改口径，见 InstitutionSystem.GetCustomsCulture)
#if EC_PENDING
    public static bool IsWestern(Empire empire) =>
        empire?.data != null && InstitutionSystem.GetCultureLine(InstitutionSystem.GetCustomsCulture(empire)) == Line;
#endif

    // 有教廷可言：西方文化、君主制、奉一个有圣地的普世教会(同天下的地方教会没有教廷，见 ReligionSphere；
    // 别的类型的宗教没有教廷，见 ReligionFaith——犹太文化走罗马制度线也没有)。奉教派的也算：教派没有教廷，只能自行加冕
#if EC_PENDING
    public static bool Applies(Empire empire) =>
        empire?.data != null && !empire.IsGone() && !empire.IsArchived() && empire.CoreKingdom != null &&
        !empire.CoreKingdom.IsGone() && IsWestern(empire) && !RepublicSystem.IsRepublic(empire) &&
        RegimeManager.IsMonarchy(empire.CoreKingdom.GetRegime()?.type) && HolyCity(ReligionOf(empire)) != null &&
        ReligionFaith.IsPapal(ReligionOf(empire)) && !ReligionSphere.IsLocalChurch(ReligionOf(empire));

    public static City HolyCity(Religion religion)
    {
        City city = religion?.GetCity();
        return city?.Record != null && !city.IsGone() && city.Realm != null && !city.Realm.IsWild ? city : null;
    }
#endif

    // 有教廷(教宗、哈里发座)：普世教会、伊斯兰的本宗(见 ReligionFaith)。同天下的地方教会、分出去的教派都没有——
    // 一个天下一处圣地、一位教宗，号召力在本宗(见 ReligionSphere"教派与圣地")。
    // 伊斯兰分出的分支(什叶派、伊斯玛仪派)例外：自有伊玛目、哈里发座(史上法蒂玛王朝就是什叶派哈里发)，对立哈里发、
    // 伊玛目断绝与复立都靠它(见 IslamicSchismSystem)；圣地仍随本宗，不另立
#if EC_PENDING
    public static bool HasSee(Religion religion) =>
        ReligionFaith.HasHierarchy(religion) && !ReligionSphere.IsLocalChurch(religion) &&
        (!ReligionSphere.IsBranch(religion) || ReligionFaith.IsCaliphal(religion));
#endif

    // 能号召圣战：普世教会、伊斯兰，地方教会和教派不号召
#if EC_PENDING
    public static bool CallsHolyWar(Religion religion) =>
        ReligionFaith.WagesHolyWar(religion) && !ReligionSphere.IsLocalChurch(religion) && !ReligionSphere.IsBranch(religion);
#endif

    // 教廷：控制圣地的国家(须奉本宗教)
#if EC_PENDING
    public static Realm Papacy(Religion religion)
    {
        if (!HasSee(religion)) return null;
        Realm holder = HolyCity(religion)?.Realm;
        return holder?.Record != null && !holder.IsGone() && holder.religion == religion ? holder : null;
    }

    public static Person Pope(Religion religion)
    {
        Person king = Papacy(religion)?.Ruler;
        return king?.Record != null && !king.IsGone() && king.Alive ? king : null;
    }
#endif

    // 教廷落在世俗强国手里：圣地所在国不是教皇国，而且是帝国的一部分；皇帝兼管的教会(正教会)不算。
    // 帝都就在圣地的是皇帝即教宗(见 PopeIsEmperor)，不算——教宗不能是自己的囚徒；圣地在皇帝自家领内、帝都在别处的照旧算
#if EC_PENDING
    public static bool IsCaptive(Religion religion)
    {
        Realm holder = Papacy(religion);
        Empire empire = holder?.GetEmpire();
        return holder != null && holder.GetKingdomType() != KingdomType.Feudalism_papal_state && empire != null &&
               !(empire.CoreKingdom == holder && holder.Capital == HolyCity(religion)) && !religion.GetOrCreate().Caesaropapist;
    }
#endif

    // 皇帝就是教宗：圣座帝国，或教宗与皇帝是同一人、帝都就在教宗国(帝国核心国握着圣地，圣地就是它的都城)——教宗国称帝后
    // 核心的类别成了帝国、宗教等级也常被改掉，以前只当"握着教廷的世俗帝国"，国号一直是"教宗国"。这样的帝国由教宗自授天下之名
    // (见 RealmNamingSystem.CoronationVia)，不称神圣(神圣是皇帝对教廷的自我标榜，见 HolyBlocker)。
    // 圣地在皇帝自家领内但帝都在别处的是皇帝挟制教廷，教廷在成员国、附庸手里的是护教，都不算
#if EC_PENDING
    public static bool PopeIsEmperor(Empire empire)
    {
        Realm core = empire?.CoreKingdom;
        if (core == null) return false;
        if (IsHolySee(empire)) return true;
        Religion religion = ReligionOf(empire);
        City holy = HolyCity(religion);
        return holy != null && Papacy(religion) == core && core.Capital == holy;
    }

    public static float Authority(Religion religion) => religion?.data == null ? 0f : religion.GetOrCreate().PapalAuthority;

    public static void AddAuthority(Religion religion, float delta)
    {
        if (religion?.data == null) return;
        var data = religion.GetOrCreate();
        data.PapalAuthority = Mathf.Clamp(data.PapalAuthority + delta, 0f, 100f);
    }
#endif

    // 脱离罗马(宗教改革)或世俗化：施行了世俗化；另立过国教会或君主率国改奉了新教会(national_church_id)；所奉就是宗教改革分出的新教会；
    // 本文化施行了宗教改革而所奉已不是有教廷的本土普世教会(剧本里的路德宗、正教会等)。
    // 以前本文化一施行宗教改革就算脱离——君主还奉着罗马也不交什一税、自行加冕；现在君主没改宗前照旧(见 PapacySystem.Reformation)。
    // 选帝制帝国的皇帝不随宗教改革脱离罗马(由诸侯改宗，见 HolyRomanSystem.KeepsOldFaith)
#if EC_PENDING
    public static bool BrokeWithRome(Empire empire) =>
        InstitutionSystem.IsEnacted(empire, NodeSecularization) ||
        (State(empire)?.national_church_id ?? -1L) >= 0 || IsReformedChurch(ReligionOf(empire)) ||
        InstitutionSystem.IsEnacted(empire, NodeReformation) && !HolyRomanSystem.KeepsOldFaith(empire) && !FollowsRomanSee(empire);
#endif

    // 仍奉有教廷的本土普世教会
#if EC_PENDING
    private static bool FollowsRomanSee(Empire empire)
    {
        Religion religion = ReligionOf(empire);
        return religion != null && ReligionFaith.IsPapal(religion) && ReligionSphere.IsNative(religion) && Papacy(religion) != null;
    }

    public static int Investiture(Empire empire)
    {
        PapalRelationState state = State(empire);
        if (state == null) return InvestitureChurch;
        if (BrokeWithRome(empire) || state.investiture_won) return InvestitureCrown;
        if (InstitutionSystem.IsEnacted(empire, NodeConcordat)) return InvestitureConcordat;
        return InvestitureChurch;
    }
#endif

    public static bool IsCrowned(Empire empire)
    {
        PapalRelationState state = State(empire);
        Person emperor = empire?.Emperor;
        return state != null && emperor != null && state.crowned_emperor_id == emperor.Key;
    }

    public static bool IsExcommunicated(Empire empire) => State(empire)?.excommunicated_at >= 0d;

    // 正统里的教廷一项(MonarchyLegitimacy.Breakdown 调用)
#if EC_PENDING
    public static IEnumerable<(string key, int value)> LegitimacyItems(Empire empire)
    {
        bool caliphal = !Applies(empire) && AppliesCaliphal(empire);
        if (!Applies(empire) && !caliphal) yield break;
        PapalRelationState state = State(empire);
        // 阿拉伯一线：受册封、未受册封(见 PapacySystem.Caliphate)
        if (caliphal)
        {
            if (IsCrowned(empire)) yield return ("mandate_caliphal_investiture", CrownedLegitimacy);
            else if (empire.data.newEmperor_timestamp >= 0d && EmpireWorld.Current != null &&
                     GameClock.Now >= empire.data.newEmperor_timestamp &&
                     GameClock.YearsSince(empire.data.newEmperor_timestamp) >= UncrownedGraceYears)
                yield return ("mandate_uncrowned_caliphal", UncrownedLegitimacy);
            yield break;
        }
        // 圣座帝国：教宗即皇帝，正统按教权计
        if (IsHolySee(empire))
        {
            int holy = Mathf.RoundToInt((Authority(ReligionOf(empire)) - 50f) / 2f);
            if (holy != 0) yield return ("mandate_holy_see", holy);
            yield break;
        }
        if (IsExcommunicated(empire))
        {
            yield return ("mandate_excommunicated", ExcommunicatedLegitimacy);
            yield break;
        }
        if (IsCrowned(empire))
        {
            yield return state.self_crowned
                ? ("mandate_self_coronation", SelfCrownedLegitimacy)
                : state.crowned_by_rival
                    ? ("mandate_antipope_coronation", AntipopeCrownedLegitimacy)
                    : ("mandate_papal_coronation", CrownedLegitimacy);
            yield break;
        }
        double since = empire.data.newEmperor_timestamp;
        if (since >= 0d && EmpireWorld.Current != null && GameClock.Now >= since &&
            GameClock.YearsSince(since) >= UncrownedGraceYears)
            yield return ("mandate_uncrowned", UncrownedLegitimacy);
    }
#endif

    public const int ExcommunicatedLegitimacy = -20;

    #endregion

    #region 教会与世俗两条路

    private static readonly HashSet<string> ClericalNodes = new() { NodeClerical, "western_theocratic_state" };
    private static readonly HashSet<string> SecularNodes = new()
        { NodeSecular, "western_centralized_monarchy", NodeConcordat, NodeReformation, NodeSecularization };

    // AI 选改革的加分：教会类节点 (教权-50)×0.6，世俗类节点反过来
#if EC_PENDING
    public static float ReformBias(Empire empire, InstitutionNodeConfig node)
    {
        if (node == null || !IsWestern(empire)) return 0f;
        Religion religion = ReligionOf(empire);
        if (religion == null) return 0f;
        float lean = (Authority(religion) - 50f) * 0.6f;
        if (ClericalNodes.Contains(node.id)) return lean;
        if (SecularNodes.Contains(node.id)) return -lean;
        return 0f;
    }
#endif

    // 文化演进挑节点的次序：教权 55 以上先教会后世俗，以下反过来；文化没有教廷时按文化名随机定一边
#if EC_PENDING
    public static int DriftRank(string culture, InstitutionNodeConfig node)
    {
        if (node == null || (!ClericalNodes.Contains(node.id) && !SecularNodes.Contains(node.id))) return 0;
        Religion religion = CultureReligion(culture);
        bool church = religion != null
            ? Authority(religion) >= 55f
            : ((culture?.GetHashCode() ?? 0) & 1) == 0;
        return ClericalNodes.Contains(node.id) == church ? 0 : 1;
    }

    private static Religion CultureReligion(string culture)
    {
        if (string.IsNullOrEmpty(culture) || EmpireWorld.Current?.LiveRealms == null) return null;
        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms)
            if (kingdom?.Record != null && !kingdom.IsGone() && kingdom.hasReligion() &&
                string.Equals(CultureService.GetRealmCulture(kingdom), culture, StringComparison.Ordinal) &&
                HolyCity(kingdom.religion) != null)
                return kingdom.religion;
        return null;
    }
#endif

    #endregion

    #region 年度结算

    // 每年(ConstitutionalEconomySystem 年度结算)逐个帝国调用
#if EC_PENDING
    public static void Yearly(Empire empire)
    {
        try
        {
            Religion religion = ReligionOf(empire);
            if (religion != null) UpdateReligion(religion);
            TryBreakWithRome(empire);
            religion = ReligionOf(empire);
            if (!Applies(empire))
            {
                // 阿拉伯一线：册封与天课(见 PapacySystem.Caliphate)
                if (AppliesCaliphal(empire)) CaliphalYear(empire, religion);
                return;
            }
            PapalRelationState state = State(empire);
            NameHolySee(empire, state);
            state.investiture = Investiture(empire);
            UpdateParties(empire);
            Conflict(empire, state, religion);
            Coronation(empire, state, religion);
            Tithe(empire, state, religion);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][教廷] 年度结算失败: {exception.Message}");
        }
    }
#endif

    // 教权每年向目标回归一次(同一宗教一年只算一次)
#if EC_PENDING
    private static void UpdateReligion(Religion religion)
    {
        var data = religion.GetOrCreate();
        double now = GameClock.Now;
        if (data.PapalUpdatedAt >= 0d && now >= data.PapalUpdatedAt && GameClock.YearsSince(data.PapalUpdatedAt) < 1) return;
        data.PapalUpdatedAt = now;
        UpdateCaptivity(religion);
        Realm papacy = Papacy(religion);
        if (papacy == null)
        {
            Realm strongest = Strongest(religion);
            // 圣战：普世教会、伊斯兰的本宗(见 ReligionFaith)，地方教会、教派不号召
            if (strongest != null && CallsHolyWar(religion)) TryCrusade(religion);
            UpdateOrders(religion);
            return;
        }
        float target = BaseAuthority;
        if (papacy.GetKingdomType() == KingdomType.Feudalism_papal_state) target += PapalStateBonus;
        float stance = 0f;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (empire?.data == null || empire.IsArchived() || ReligionOf(empire) != religion || !IsWestern(empire)) continue;
            stance += BrokeWithRome(empire) ? -4f : Investiture(empire) switch
            {
                InvestitureChurch => 2f,
                InvestitureCrown => -2f,
                _ => 0f
            };
        }
        target += Mathf.Clamp(stance, -20f, 20f);
        // 教宗本人的正统(见 PopeLegitimacy)
        if (PopeLegitimacy.Applies(religion)) target += (PopeLegitimacy.Get(religion) - 50) / 5f;
        if (IsCaptive(religion)) target -= CaptivityPenalty;
        if (data.Schism) target -= SchismPenalty;
        data.PapalAuthority = Mathf.Clamp(data.PapalAuthority + (target - data.PapalAuthority) * AuthorityDrift, 0f, 100f);
        // 圣战：普世教会的十字军、伊斯兰的吉哈德(见 ReligionFaith)
        if (CallsHolyWar(religion)) TryCrusade(religion);
        UpdateOrders(religion);
    }
#endif

    // 加冕：教宗加冕(献礼)、帝国自己握有圣地直接加冕、脱离罗马后自行加冕；大分裂中奉对立教宗的由对立教宗加冕(见 PapacySystem.Antipope)
#if EC_PENDING
    private static void Coronation(Empire empire, PapalRelationState state, Religion religion)
    {
        Person emperor = empire.Emperor;
        if (emperor == null || !emperor.Alive || IsCrowned(empire) || IsExcommunicated(empire)) return;
        Realm core = empire.CoreKingdom;
        Realm papacy = Papacy(religion);
        bool antipope = FollowsAntipope(empire);
        if (BrokeWithRome(empire) || papacy == null && !antipope)
        {
            Crown(empire, state, emperor, self: true, null);
            return;
        }
        if (antipope)
        {
            AntipopeCoronation(empire, state, religion, emperor);
            return;
        }
        // 封建制帝国：教廷不是皇帝自家领国就要远征受冕，圣地在本帝国诸侯手里(教宗受挟)也一样——原型罗马远征，
        // 圣城在帝国之内皇帝照样率军前往。以前教廷在本帝国就直接加冕，玩家日志里三次加冕都没有远征
        if (papacy == core || papacy.GetEmpire() == empire && !HolyRomanSystem.HasDiet(empire))
        {
            Crown(empire, state, emperor, self: false, papacy);
            // 普世之名(见 RealmNamingSystem.Universal)：控制教宗国、好感、施压
            RealmNamingSystem.OnPapalCoronation(empire, RealmNamingSystem.CoronationVia(empire, papacy, false));
            return;
        }
        // 教宗正统太低(见 PopeLegitimacy)：各帝国不赴其加冕
        if (PopeLegitimacy.Applies(religion) && PopeLegitimacy.Get(religion) < PopeLegitimacy.CoronationBelow) return;
        // 皇帝要远征教廷，一年后到达才加冕(见 HolyRomanSystem.Romzug)；到了却与教廷开战就作罢
        int romzug = HolyRomanSystem.Romzug(empire);
        if (romzug == HolyRomanSystem.RomzugTravelling) return;
        if (romzug != HolyRomanSystem.RomzugArrived)
        {
            if (EcRandom.value >= CoronationChance) return;
            if (papacy.IsAtWarWith(core)) return;
            if (HolyRomanSystem.StartRomzug(empire, papacy)) return;
        }
        else if (papacy.IsAtWarWith(core)) return;
        int gift = (int)Math.Min(Math.Max(0L, core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * CoronationGift));
        if (gift > 0)
        {
            core.SubMoney(gift, TreasuryCategory.Tithe);
            papacy.AddMoney(gift, TreasuryCategory.Tithe);
        }
        Crown(empire, state, emperor, self: false, papacy);
        RealmNamingSystem.OnPapalCoronation(empire,
            RealmNamingSystem.CoronationVia(empire, papacy, romzug == HolyRomanSystem.RomzugArrived));
    }
#endif

    // antipope：由对立教宗在对立教廷加冕(见 PapacySystem.Antipope)
#if EC_PENDING
    private static void Crown(Empire empire, PapalRelationState state, Person emperor, bool self, Realm papacy, bool antipope = false)
    {
        state.crowned_emperor_id = emperor.Key;
        state.self_crowned = self;
        state.crowned_by_rival = antipope && !self;
        state.crowned_at = GameClock.Now;
        Religion religion = ReligionOf(empire);
        if (!self) AddAuthority(religion, 2f);
        City holy = state.crowned_by_rival ? AntipopeSeeCity(religion) : HolyCity(religion);
        Person pope = state.crowned_by_rival ? Antipope(religion) : Pope(religion);
        string text = self
            ? string.Format(LM.Get("papal_self_coronation_history"), emperor.FullName(), empire.GetEmpireFullName())
            : string.Format(LM.Get(IsCaliphal(empire) ? "caliphal_investiture_history"
                    : state.crowned_by_rival ? "papal_coronation_rival_history" : "papal_coronation_history"),
                emperor.FullName(), holy?.GetCityName() ?? "", pope?.FullName() ?? LM.Get("label_none"), empire.GetEmpireFullName());
        empire.RecordHistory(directContent: text, actorId: emperor.Key, kingdomId: empire.CoreKingdom.Key);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
        emperor.RecordPersonalHistory(text);
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
        // 封建制帝国的帝国权威：教宗加冕、自行加冕都是 +8(每位皇帝一次)
        HolyRomanSystem.OnCrowned(empire, emperor, self);
        MonarchyLegitimacy.Invalidate(empire);
    }
#endif

    // 什一税：年度经常收入的 6%，按叙任权档位上缴教廷(教廷就是本帝国的不缴；大分裂中交给自己服从的那边，见 PapacySystem.Antipope)
#if EC_PENDING
    private static void Tithe(Empire empire, PapalRelationState state, Religion religion)
    {
        double now = GameClock.Now;
        if (state.last_tithe_at >= 0d && now >= state.last_tithe_at && GameClock.YearsSince(state.last_tithe_at) < 1) return;
        state.last_tithe_at = now;
        state.tithe_paid = 0;
        Realm core = empire.CoreKingdom, papacy = ObeyedSee(empire, religion);
        if (papacy == null || papacy == core || papacy.GetEmpire() == empire || papacy.IsAtWarWith(core)) return;
        float share = Investiture(empire) switch
        {
            InvestitureChurch => 1f,
            InvestitureConcordat => 0.5f,
            _ => 0f
        };
        if (share <= 0f || IsExcommunicated(empire)) return;
        long amount = (long)(StateDebtSystem.AnnualStableIncome(core) * TitheRate * share);
        amount = Math.Min(amount, Math.Max(0L, (long)core.GetMoney()));
        if (amount <= 0) return;
        core.SubMoney((int)amount, TreasuryCategory.Tithe);
        papacy.AddMoney((int)amount, TreasuryCategory.Tithe);
        state.tithe_paid = amount;
    }
#endif

    #endregion
}
}
