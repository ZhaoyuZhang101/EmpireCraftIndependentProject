// Generated from the EmpireCraft mod: Scripts/GeneralSystems/HolyRomanSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

// 神圣罗马式的选帝制帝国(封建制、帝位由选帝侯选举——施行了金玺诏书)：
//   · 家族领地：当选的诸侯带着自家领国成为帝国中央，帝都随之搬到他的都城；原来的中央降为普通诸侯国、仍归原皇族。
//     选举换了家族不算篡位(不额外扣正统、旧皇族不出走、不改国号，见 Empire.NewEmperor)；
//   · 家族领地占帝国城数：不到一成正统 -8，二成五以上 +5，四成以上 +8；
//     不到一成时，城比中央多的诸侯不听调遣(见 EmpirePeacePatch.Obeys)；皇帝远征加冕在外时，城比中央一半多的也不听；
//   · 选举让步：帝位换了一家(或选立储君)时随机让出一项——帝国税降一档、某选帝侯所在国恢复外交与开战权、
//     自治派系 +5、中央一座非都城的城封给最强的选帝侯；十年内至多让一次，各项有底线(见 Concede)；
//   · 选立储君(原型"罗马人民的国王")：皇帝在世、有成年子嗣时，每年有两成可能让选帝侯先把长子选为储君
//     (让出一项、花一成年度经常收入；有帝国议会的帝国这笔钱改为替储君拉票，储君与票势最高的诸侯决选、过半才立，
//     见 Election)；皇帝死后他直接即位，不再另选。同一家族连续当选记届数。
// 其余见各分部：Authority 帝国权威、Diet 帝国议会与禁令、Members 自由城市与教会诸侯、Reform 帝国改革与分割继承、
// Feuds 诸侯私战与帝国法院、Electors 选帝侯席位、Election 会选与对立国王与大空位、Immediacy 陪臣化与帝国直属与典押、
// Leagues 诸侯同盟与和约、加冕远征、Decline 帝国解体。选帝制只管上面几条；其余凡封建制帝国(HasDiet)都适用。
public static partial class HolyRomanSystem
{
    private const float KingOfRomansChance = 0.2f;
    private const float KingOfRomansCost = 0.1f;
    private const float WeakHouse = 0.1f, StrongHouse = 0.25f, DominantHouse = 0.4f;
    // 选举让步：两次之间至少隔几年；帝国税最低让到几成、自治派系占到几成不再让、中央至少几城才封城给选帝侯
    private const int ConcessionGapYears = 10, ConcessionAutonomyCap = 40, ConcessionFiefMinCities = 4;
    private const float ConcessionTaxFloor = 0.1f;

    // 正在按选帝侯的推举立皇帝(OfficeSelector 选出、SetActor 即位的那一刻)：即位不扣正统，见 Empire.NewEmperor
    public static bool Electing;

    public static HolyRomanState State(Empire empire)
    {
        if (empire?.data == null) return null;
        return empire.data.holy_roman ??= new HolyRomanState();
    }

#if EC_PENDING
    public static bool IsElective(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        return regime?.type == RegimeType.Feudalism && regime.GetLeaderSelectMethod() == LeaderSelectMethod.Vote;
    }
#endif

    // 金玺诏书(1356)以后开局的剧本(临高 1628)：神罗开局即是选帝制(用户 2026-10-29 "为什么临高剧本里面每次神罗没过多久就会推到
    // 德意志帝国了，这不符合史实吧")。以前开局是世袭，日耳曼文化又已施行宗教改革，皇帝开局即算脱离罗马(见 PapacySystem.BrokeWithRome)：
    // 头一回年度结算就另立国教会、整个帝国改宗，普世之名随之收回，国号退回帝国法理名"德意志"。
    // 做法同诉求「颁布金玺诏书」：封建制、有君的诸侯至少两家时帝位改由选帝侯选举；世俗选帝侯席位按剧本在座的诸侯填好(见 EnsureSeats)。
    // 要在授神圣之名之前(WorldMapScenario.Hallow)：选帝制的皇帝不随宗教改革脱离罗马，才由教宗加冕
    public const int GoldenBullYear = 1356;

#if EC_PENDING
    public static bool GoldenBullAtStart(Empire empire, int year)
    {
        if (year < GoldenBullYear || empire?.data == null || empire.IsGone()) return false;
        Regime regime = empire.CoreKingdom?.GetRegime();
        if (regime?.type != RegimeType.Feudalism || Members(empire).Count(member => member.HasRuler()) < 2) return false;
        regime.SetLeaderSelectMethod(LeaderSelectMethod.Vote);
        if (HasDiet(empire)) SecularElectorKings(empire);
        LogService.LogInfo($"[EmpireCraft][神罗] {empire.GetEmpireFullName()} 开局({year} 年)已颁金玺诏书，帝位由选帝侯选举");
        return true;
    }
#endif

    // 家族领地：中央(皇帝自家领国)的城占帝国城数的比例
#if EC_PENDING
    public static float HouseShare(Empire empire)
    {
        int total = empire?.countCities() ?? 0;
        return total <= 0 ? 1f : (empire.CoreKingdom?.Cities?.Count ?? 0) / (float)total;
    }

    public static IEnumerable<(string key, int value)> LegitimacyItems(Empire empire)
    {
        if (IsElective(empire))
        {
            float share = HouseShare(empire);
            if (share < WeakHouse) yield return ("mandate_house_weak", -8);
            else if (share >= DominantHouse) yield return ("mandate_house_dominant", 8);
            else if (share >= StrongHouse) yield return ("mandate_house_strong", 5);
        }
        if (!HasDiet(empire)) yield break;
        HolyRomanState state = State(empire);
        // 帝国权威：隆盛 +5、名存实亡 −5(还没起算的不计)
        if (state.authority >= ImperialAuthorityRules.Glorious) yield return ("mandate_imperial_authority_high", AuthorityLegitimacyHigh);
        else if (state.authority >= 0 && state.authority < LowAuthority)
            yield return ("mandate_imperial_authority_low", AuthorityLegitimacyLow);
        if (ReformEnacted(empire)) yield return ("mandate_imperial_reform", ReformLegitimacy);
        if (state.sovereign_princes) yield return ("mandate_sovereign_princes", SovereignLegitimacy);
        if (RomzugUnderway(empire)) yield return ("mandate_romzug", RomzugLegitimacy);
    }
#endif

    // 家族领地薄弱时，比皇帝自家领国城多的诸侯不听调遣；皇帝远征在外时，比中央一半大的也不听
#if EC_PENDING
    public static bool Defies(Empire empire, Realm member)
    {
        if (member?.Cities == null || empire?.CoreKingdom == null) return false;
        int core = empire.CoreKingdom.Cities?.Count ?? 0;
        if (IsElective(empire) && HouseShare(empire) < WeakHouse && member.Cities.Count > core) return true;
        return member.Cities.Count * 2 > core && RomzugUnderway(empire);
    }
#endif

    public static Person KingOfRomans(Empire empire)
    {
        HolyRomanState state = State(empire);
        Person actor = state?.king_of_romans_id >= 0 ? EmpireWorld.Current.PersonByKey(state.king_of_romans_id) : null;
        return actor?.Record != null && !actor.IsGone() && actor.Alive && actor.Realm?.GetEmpire() == empire ? actor : null;
    }

    // 帝位选出的人(OfficeObject.SetActor 调用)：是别的成员国的君主时，带着自家领国成为中央，不调进原中央
#if EC_PENDING
    public static bool TryElectHouse(Realm seat, Person elected)
    {
        try
        {
            Empire empire = seat?.GetEmpire();
            if (empire == null || empire.CoreKingdom != seat || !IsElective(empire) || elected?.Record == null ||
                elected.IsGone() || !elected.IsRuler()) return false;
            Realm house = elected.Realm;
            if (house == null || house == seat || house.GetEmpire() != empire || house.Ruler != elected || !house.HasCapital())
                return false;
            // 已经坐在中央的皇帝不再"当选"一次：同一次选举里 SetActor 会被再调几回，以前帝国重心在两三家之间来回搬
            if (seat.Ruler == elected) return false;
            // 中央顶着帝国的国号，原家族领地按它的法理称呼；新家族的名字要在交出国号之前取
            string former = seat.GetMainTitle()?.Name;
            if (string.IsNullOrWhiteSpace(former)) former = seat.GetKingdomName();
            string houseName = house.GetKingdomName();
            // 选帝换家族不改国号(帝国本身照旧，见文件头)
            if (!empire.TransferCoreTo(house, keepName: true)) return false;
            HolyRomanState state = State(empire);
            // 选举让步只在帝位真的换了一家(不同世系)时给，同一世系的分支连选不算
            bool newHouse = NewHouse(state, elected);
            Elected(empire, state, elected, heir: false);
            if (newHouse) Concede(empire, state);
            Record(empire, string.Format(LM.Get("hre_election_history"), elected.FullName(), houseName,
                house.Capital?.GetCityName() ?? "", former));
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 选举换家族失败: {exception.Message}");
            return false;
        }
    }
#endif

    // 记届数；同族连任(第二届起)或储君即位，帝国权威 +3
#if EC_PENDING
    private static void Elected(Empire empire, HolyRomanState state, Person emperor, bool heir)
    {
        long clan = emperor.GetSpecificClan()?.id ?? -1L;
        state.house_terms = clan >= 0 && clan == state.house_clan_id ? state.house_terms + 1 : 1;
        state.house_clan_id = clan;
        state.last_election_at = GameClock.Now;
        if (heir) ChangeAuthority(empire, AuthContinuity, "hre_auth_heir_succeeded", emperor.FullName());
        else if (state.house_terms >= 2) ChangeAuthority(empire, AuthContinuity, "hre_auth_house_continued", emperor.FullName());
    }
#endif

    // 每年(ConstitutionalEconomySystem 年度结算)：选帝制的储君；封建制帝国的成员构成、帝国改革、诸侯同盟、帝国议会
#if EC_PENDING
    public static void Yearly(Empire empire)
    {
        try
        {
            if (!IsElective(empire) && !HasDiet(empire)) return;
            HolyRomanState state = State(empire);
            // 帝国权威最先结(起算、旧档帝国改革迁移都要赶在帝国改革、议会之前)
            if (HasDiet(empire)) AuthorityYearly(empire, state);
            if (IsElective(empire)) ElectiveYearly(empire, state);
            if (!HasDiet(empire)) return;
            ElectionYearly(empire, state);
            ImperialPeaceYearly(empire, state);
            MembersYearly(empire, state);
            ElectorsYearly(empire, state);
            ReformYearly(empire, state);
            FeudsYearly(empire, state);
            ImmediacyYearly(empire, state);
            LeaguesYearly(empire, state);
            // 皇帝退位、帝国已解散：不再往下结
            if (DeclineYearly(empire, state)) return;
            DietYearly(empire, state);
            Diagnose(empire, state);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 年度结算失败: {exception.Message}");
        }
    }
#endif

    // 储君：已即位的记一届；皇帝在世、有继承人时选立
#if EC_PENDING
    private static void ElectiveYearly(Empire empire, HolyRomanState state)
    {
        Person emperor = empire.Emperor;
        if (emperor == null) return;
        // 储君已经即位
        if (state.king_of_romans_id == emperor.Key)
        {
            state.king_of_romans_id = -1L;
            Elected(empire, state, emperor, heir: true);
        }
        if (KingOfRomans(empire) != null) return;
        state.king_of_romans_id = -1L;
        // 先掷骰再挑人：挑人可能落成虚拟族人
        if (EcRandom.value >= KingOfRomansChance) return;
        Person heir = KingOfRomansCandidate(empire);
        if (heir?.Record == null || heir.IsGone() || !heir.Alive) return;
        long cost = Math.Min(Math.Max(0L, (long)empire.CoreKingdom.GetMoney()),
            (long)(StateDebtSystem.AnnualStableIncome(empire.CoreKingdom) * KingOfRomansCost));
        if (HasDiet(empire))
        {
            // 过票：皇帝替储君拉票，选帝侯不答应就不立
            if (!KingOfRomansVote(empire, state, heir, cost)) return;
        }
        else if (cost > 0) empire.CoreKingdom.SubMoney((int)cost, TreasuryCategory.Gift);
        state.king_of_romans_id = heir.Key;
        Concede(empire, state);
        Record(empire, string.Format(LM.Get("hre_king_of_romans_history"), heir.FullName(), emperor.FullName()));
        heir.RecordPersonalHistory(LM.Get("personal_history_king_of_romans"));
    }
#endif

    // 储君人选：皇帝成年可继位、身在本帝国的子嗣，年长的先。选帝制下帝位不世袭，中央一直没有继承人
    // (EmpireCraftKingdomBehCheckHeir 只给世袭的国家立继承人)，以前按"有继承人"判，罗马人民的国王从来选不出来。
    // 无小人模式下皇子多是虚拟族人：实体里没有就按长幼落成一位(最多试两人，不合格的并回人口)
#if EC_PENDING
    public static Person KingOfRomansCandidate(Empire empire)
    {
        bool Fit(Person actor) => actor.IsAdult() && actor.IsFitToRule() && actor.Realm?.GetEmpire() == empire;
        Person pick = EmpireCraftKingdomBehCheckHeir.HeirCandidates(empire.CoreKingdom).Where(Fit)
            .OrderByDescending(actor => actor.Age()).FirstOrDefault();
        if (pick != null || !CityPopulationSystem.AbstractPopulationEnabled) return pick;
        PersonalClanIdentity parent = empire.Emperor?.GetPersonalIdentity();
        if (parent == null) return null;
        return VirtualGenealogySystem.RealizeFirst(SpecificClanManager.getChildren(parent).Select(item => item.Item2)
                .Where(identity => identity != null && identity.is_alive && identity.CanHeir())
                .OrderByDescending(identity => identity.age),
            actor => Fit(actor) && actor != empire.Emperor, 2);
    }
#endif

    private static bool NewHouse(HolyRomanState state, Person elected)
    {
        SpecificClan previous = state.house_clan_id >= 0 ? SpecificClanManager.Get(state.house_clan_id) : null;
        return previous == null || !SpecificClanManager.SameLineage(previous, elected.GetSpecificClan());
    }

    // 选举让步：随机让出一项(能让的才让)。以前每次当选都让，选帝侯推举的多是老迈的诸侯，一两年一选，
    // 二十来年攒下七八条(玩家日志)。现在：换了一家(或选立储君)才让，十年内至多一次；
    // 各项有底线——帝国税最多让到一成、自治派系占到四成就不再让、中央剩不到三城不再封城
#if EC_PENDING
    private static void Concede(Empire empire, HolyRomanState state)
    {
        if (state.last_concession_at >= 0d && GameClock.Now >= state.last_concession_at &&
            GameClock.YearsSince(state.last_concession_at) < ConcessionGapYears) return;
        var options = new List<Func<string>> { LowerTax, RestoreWarRight, Autonomy, GrantFief };
        while (options.Count > 0)
        {
            int index = EcRandom.Range(0, options.Count);
            Func<string> option = options[index];
            options.RemoveAt(index);
            string text = option();
            if (string.IsNullOrEmpty(text)) continue;
            state.concessions ??= new List<string>();
            state.concessions.Add(text);
            if (state.concessions.Count > 20) state.concessions.RemoveAt(0);
            state.last_concession_at = GameClock.Now;
            Record(empire, string.Format(LM.Get("hre_concession_history"), text));
            ChangeAuthority(empire, AuthConcession, "hre_auth_concession", text);
            return;
        }

        // 以前最低让到 0.02，比帝国税下限(Empire.MinimumTaxRate)还低，读档时又被重置回两成
        string LowerTax()
        {
            if (empire.data.TaxRate <= ConcessionTaxFloor + 0.001f) return null;
            empire.data.TaxRate = Mathf.Max(ConcessionTaxFloor, empire.data.TaxRate - 0.02f);
            return LM.Get("hre_concession_tax");
        }

        string RestoreWarRight()
        {
            // 主教领的选帝侯本就不开战，不在此列；治安令施行期间记为豁免
            Realm member = ElectorRealms(empire).FirstOrDefault(kingdom => SecularPrince(kingdom) &&
                                                                             kingdom.GetRegime()?.IsAllowDiplomacy() == false);
            if (member == null) return null;
            member.GetRegime().SetAllowDiplomacy(true);
            Exempt(empire, member);
            return string.Format(LM.Get("hre_concession_war_right"), member.GetKingdomName());
        }

        string Autonomy()
        {
            FixedFaction faction = empire.CoreKingdom.GetRegime()?.GetPlayerFactions()
                ?.FirstOrDefault(item => item.Type == FactionType.自治);
            if (faction == null || empire.CoreKingdom.GetFactionRatioValue(faction) >= ConcessionAutonomyCap ||
                !empire.CoreKingdom.TryIncreaseFactionRatio(faction, 5)) return null;
            return LM.Get("hre_concession_autonomy");
        }

        string GrantFief()
        {
            Realm core = empire.CoreKingdom;
            City city = core.Cities?.Where(item => item != null && !item.IsGone() && item != core.Capital)
                .OrderBy(item => item.Zones?.Count ?? 0).FirstOrDefault();
            Realm elector = ElectorRealms(empire).Where(SecularPrince).OrderByDescending(kingdom => kingdom.Cities.Count)
                .FirstOrDefault();
            if (city == null || elector == null || core.Cities.Count < ConcessionFiefMinCities) return null;
            city.JoinRealm(elector);
            return string.Format(LM.Get("hre_concession_fief"), city.GetCityName(), elector.GetKingdomName());
        }
    }
#endif

    #region 帝国治安令

    // 帝国治安令(诉求「颁布帝国治安令」)：施行期间世俗诸侯没有外交开战权、须向中央出兵；自由城市、主教领本就不开战、
    // 不给中央出兵，不在其内。以前诉求只看"还有没有诸侯能开战或不出兵"：新封的、分割出来的、换家族后降为诸侯的旧中央，
    // 连同本就不出兵的自由城市，一出现就又能提，同一帝国几年里颁布三次(玩家日志 2026-10-20)。现在：
    //   · 施行期间新入帝国、新立的世俗诸侯每年一查，照令办理；
    //   · 以诸侯特权(确认诸侯特权、允许诸侯自由开战)或选举让步取回开战权的记为豁免——从未施行、或有了豁免(被部分撤销)
    //     才能再提，颁布后十五年内不再提(见 TempFac_颁布帝国治安令)；
    //   · 和约之后诸侯取得完全主权，治安令作废，也不再能提。
    public static bool SecularPrince(Realm kingdom) => kingdom?.Record != null && Estate(kingdom) == EstateNone;

    public static bool ImperialPeaceInForce(Empire empire) => State(empire)?.imperial_peace_at >= 0d;

    // 可以再提：未施行，或已被特权、让步部分撤销；和约之后不再有
    public static bool ImperialPeaceOpen(Empire empire)
    {
        HolyRomanState state = State(empire);
        return state != null && !state.sovereign_princes && (state.imperial_peace_at < 0d || state.privileged_ids?.Count > 0);
    }

    // 颁布(诉求执行时调用)：诸侯一律受令，豁免清零
#if EC_PENDING
    public static void EnactImperialPeace(Empire empire, IEnumerable<Realm> princes)
    {
        HolyRomanState state = State(empire);
        if (state == null || EmpireWorld.Current == null) return;
        foreach (Realm prince in princes.ToList()) Bind(prince);
        state.imperial_peace_at = GameClock.Now;
        state.privileged_ids?.Clear();
    }
#endif

    // 某诸侯取回外交开战权(诸侯特权、选举让步)：施行期间记为豁免
    public static void Exempt(Empire empire, Realm prince)
    {
        HolyRomanState state = State(empire);
        if (state == null || prince?.Record == null || state.imperial_peace_at < 0d) return;
        state.privileged_ids ??= new List<long>();
        if (!state.privileged_ids.Contains(prince.Key)) state.privileged_ids.Add(prince.Key);
    }

#if EC_PENDING
    private static void Bind(Realm prince)
    {
        Regime regime = prince?.GetRegime();
        if (regime == null) return;
        regime.SetAllowDiplomacy(false);
        regime.SetAllowSupportCenterArmy(true);
    }
#endif

    // 每年：和约后作废；施行期间新来的世俗诸侯照令办理；豁免名单清掉已不在帝国的、又被收回开战权的(如夺取诸侯开战权)
#if EC_PENDING
    private static void ImperialPeaceYearly(Empire empire, HolyRomanState state)
    {
        if (state.imperial_peace_at < 0d) return;
        state.privileged_ids ??= new List<long>();
        if (state.sovereign_princes)
        {
            state.imperial_peace_at = -1d;
            state.privileged_ids.Clear();
            return;
        }
        state.privileged_ids.RemoveAll(id => EmpireWorld.Current.RealmByKey(id) is not Realm prince || prince.GetEmpire() != empire ||
                                             prince.GetRegime()?.IsAllowDiplomacy() != true);
        foreach (Realm member in Members(empire))
        {
            if (!SecularPrince(member) || !member.HasRuler() || state.privileged_ids.Contains(member.Key) ||
                member.IsFactionRebelling() || member.IsLocalRebelling()) continue;
            Regime regime = member.GetRegime();
            if (regime == null || !regime.IsAllowDiplomacy() && regime.IsAllowSupportCenterArmy()) continue;
            Bind(member);
        }
    }
#endif

    #endregion

    // 选帝侯所在的成员国(不含中央、被禁者)
#if EC_PENDING
    public static List<Realm> ElectorRealms(Empire empire) =>
        empire.GetCabinetMembers().Where(actor => actor?.Realm != null && !actor.IsGone())
            .Select(actor => actor.Realm)
            .Where(kingdom => kingdom != empire.CoreKingdom && kingdom.GetEmpire() == empire && !kingdom.IsGone() &&
                              !IsBanned(kingdom))
            .Distinct().ToList();
#endif

    #region 诊断

    // 每年一行(随年度结算，每个封建制帝国一行)：各项现状、卡在哪一条，进游戏看 Player.log 的 [EmpireCraft][神罗诊断]
#if EC_PENDING
    private static void Diagnose(Empire empire, HolyRomanState state)
    {
        try
        {
            Realm core = empire.CoreKingdom;
            List<Realm> members = Members(empire);
            var parts = new List<string>
            {
                "帝国权威：" + AuthorityStatus(empire, state),
                $"成员{members.Count}国、共{empire.countCities()}城，中央{core.Cities?.Count ?? 0}城(家族领地{HouseShare(empire):P0})，" +
                (IsElective(empire) ? "选帝制" : "世袭"),
                "治安令：" + (state.sovereign_princes ? "和约后作废" : state.imperial_peace_at >= 0d
                    ? $"施行中，豁免{state.privileged_ids?.Count ?? 0}家" : "未施行"),
                $"选举让步：累计{state.concessions?.Count ?? 0}条" +
                (state.last_concession_at >= 0d ? $"，距上次{GameClock.YearsSince(state.last_concession_at)}年" : "")
            };
            if (IsElective(empire)) parts.Add("储君：" + KingOfRomansStatus(empire));
            List<Realm> free = members.Where(IsFreeCity).ToList();
            parts.Add($"自由城市{free.Count}座" + (free.Count == 0 ? "" : "(" + string.Join("、", free.Take(4).Select(city =>
                $"{city.GetKingdomName()} 年收{StateDebtSystem.AnnualStableIncome(city)}/债{StateDebtSystem.Outstanding(city)}")) + ")"));
            parts.Add($"主教领{members.Count(IsChurchEstate)}家：" + (ChurchBlocker(empire) ?? "条件已齐，每年一成可能"));
            parts.Add("帝国改革：" + ReformStatus(empire, state));
            parts.Add("私战与法院：" + FeudStatus(empire, state));
            parts.Add("选帝侯：" + ElectorStatus(empire, state));
            if (IsElective(empire)) parts.Add("会选：" + ElectionStatus(empire, state));
            parts.Add("帝国直属：" + ImmediacyStatus(empire, state));
            parts.Add("帝国存续：" + DeclineStatus(empire, state));
            PartibleTally.TryGetValue(empire.data.id, out var partible);
            PartibleTally.Remove(empire.data.id);
            parts.Add("分割继承：" + (partible.vacant == 0 ? "本年无诸侯出缺"
                : $"本年诸侯出缺{partible.vacant}次、掷中{partible.rolled}次" +
                  (partible.blocked != null ? $"，最近没分：{partible.blocked}" : "")));
            parts.Add("诸侯同盟：" + LeagueStatus(empire, state));
            parts.Add("加冕远征：" + RomzugStatus(empire));
            LogService.LogInfo($"[EmpireCraft][神罗诊断] {empire.GetEmpireFullName()}：{string.Join("；", parts)}");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗诊断] 失败: {exception.Message}");
        }
    }

    private static string KingOfRomansStatus(Empire empire)
    {
        if (KingOfRomans(empire) is Person chosen) return $"已选立{chosen.FullName()}";
        Person emperor = empire.Emperor;
        if (emperor == null) return "帝位空缺";
        int living = EmpireCraftKingdomBehCheckHeir.HeirCandidates(empire.CoreKingdom).Count(actor => actor.IsAdult());
        PersonalClanIdentity parent = emperor.GetPersonalIdentity();
        int all = parent == null ? 0 : SpecificClanManager.getChildren(parent).Count(item =>
            item.Item2 != null && item.Item2.is_alive && item.Item2.CanHeir() && item.Item2.age >= 18);
        return Math.Max(living, all) == 0 ? "皇帝没有成年可继位的子嗣" : $"成年子嗣{Math.Max(living, all)}人，每年两成可能";
    }

    private static string ReformStatus(Empire empire, HolyRomanState state)
    {
        if (ReformEnacted(empire)) return $"已施行，{CircleCount(empire)}个大区，帝国法院判过{state.court_rulings}次";
        if (ReformNodeEnacted(empire))
            return Authority(empire) < ReformThreshold ? $"制度节点已施行，待议会颁行：权威{Authority(empire)}，要{ReformThreshold}"
                : state.last_reform_agenda_at >= 0d && GameClock.YearsSince(state.last_reform_agenda_at) < ReformAgendaGapYears
                    ? $"制度节点已施行，{GameClock.YearsSince(state.last_reform_agenda_at)}年前付议未过，满{ReformAgendaGapYears}年再议"
                    : "制度节点已施行，待下次议会付议";
        InstitutionNodeConfig node = InstitutionDefinitionRegistry.Get(NodeImperialReform);
        if (node == null) return "制度树里没有这个节点";
        if (InstitutionSystem.CanStartReform(empire, node, out string reason))
            return "条件已齐，待 AI 选中(别的改革在推时要排队)";
        string text = LM.Get(reason);
        if (reason == "institution_reform_missing_tech" &&
            !TechnologySystem.AreRealmInstitutionTechsMet(empire.CoreKingdom, node, out List<string> missing))
            text += "：" + string.Join("、", missing);
        return reason == "institution_reform_low_mandate" ? $"{text}(正统{empire.Legitimacy}，要{node.research.mandate_required})" : text;
    }

    private static string LeagueStatus(Empire empire, HolyRomanState state)
    {
        if (state.sovereign_princes) return $"已订{state.peace_name}，诸侯完全主权";
        if (LeagueWar(empire) != null) return $"{state.league_name}宗教战争进行中";
        if (state.league_leader_id >= 0) return $"{state.league_name}已结成({LeagueMembers(empire).Count}家)，每年一成二可能开战";
        Realm core = empire.CoreKingdom;
        if (!core.hasReligion()) return "中央没有国教";
        bool reformation = InstitutionSystem.IsEnacted(empire, PapacySystem.NodeReformation);
        if (PapacySystem.Reformations(core.religion).Count == 0)
            return !reformation ? "本文化未施行宗教改革，也没有从皇帝所奉宗教分出的新教会"
                : KeepsOldFaith(empire) ? "待改革者发端或诸侯另立新教会(每年一成五)" : "非选帝制，待改革者发端或皇帝率国改宗";
        List<Realm> reformed = Members(empire).Where(member => member.hasReligion() && Schismatic(member.religion, core.religion)).ToList();
        int total = Math.Max(1, empire.countCities());
        return $"改奉新教会{reformed.Count}家、占{reformed.Sum(member => member.Cities.Count) / (float)total:P0}城(要两家、两成)";
    }

    private static string RomzugStatus(Empire empire)
    {
        if (RomzugUnderway(empire)) return "远征途中";
        if (!PapacySystem.Applies(empire)) return "无教廷可言(非西方、非君主制或所奉宗教没有圣地)";
        if (empire.Emperor == null) return "帝位空缺";
        if (PapacySystem.IsCrowned(empire)) return "已加冕";
        Realm papacy = PapacySystem.Papacy(PapacySystem.ReligionOf(empire)), core = empire.CoreKingdom;
        if (papacy == null || PapacySystem.BrokeWithRome(empire)) return "教廷无主或已脱离罗马，自行加冕";
        if (papacy == core) return "教廷就在皇帝自家领内，直接加冕";
        if (PapacySystem.IsExcommunicated(empire)) return "绝罚中";
        if (papacy.IsAtWarWith(core)) return $"与教廷{papacy.GetKingdomName()}交战";
        return $"未加冕，每年四成可能远征{papacy.GetKingdomName()}";
    }
#endif

    #endregion

#if EC_PENDING
    private static void Record(Empire empire, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        empire.RecordHistory(directContent: text, actorId: empire.Emperor?.Key ?? -1L, kingdomId: empire.CoreKingdom?.Key ?? -1L);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }
#endif
}
}
