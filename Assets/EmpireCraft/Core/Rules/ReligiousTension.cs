// Ported from the EmpireCraft mod: Scripts/GeneralSystems/ReligiousTension.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Linq;

namespace EmpireCraft.Core
{

// 宗教影响稳定(用户 2026-10-19 选"宗教影响稳定"；以前宗教与稳定、情绪无关，宗教叛乱从没发生过)：
//   · 城里通行的宗教与国教(国家的宗教)不同的是异教之城，按国家的宗教政策：
//       宽容(国教不排他——多神、萨满、三教、法教、神道，见 ReligionFaith；中央宗教选项在自由信仰及以下；
//         特性 religious_tolerance：游牧宗教宽容、罗马世俗化、华夏三教并立、日本神佛习合)——稳定度少 3，民族情绪不受影响；
//         (华夏、日本以前整棵制度树没有宽容，异教之城永远少 10、会起事；用户 2026-10-21 选文明平衡 D3 各加一个中期节点)
//       收人头税(特性 poll_tax：阿拉伯人丁税制)——稳定度少 6，情绪每年 +1，每座异教之城每年多交一笔人头税；
//         人头税按国家税率折算(中等税率每十户一钱，税级为无不收)，和别的赋税一样走 TreasurySystem.CollectResidentTax：
//         控制力、水土、税基流失、贪腐、央地分成都照算(以前直接进国库，样样不扣)；
//         交了人头税就豁免兵役：异教之城的人口只征 DhimmiLevy(同野人，见 CorpsSystem.Mobilize)，打下异教之地添税不添兵；
//       不宽容(其余)——稳定度少 10，情绪每年 +2；
//   · 宗教叛乱：不宽容的国家，异教之城稳定度低于起事线时每年有半成机会举教起事(新国奉本城之教，十年内同城不再起事)；
//     收人头税的国家机会减半(以前收人头税就永不起事)；
//   · 圣战(见 EmpireCraftKingdomBehCheckPlots.CheckJoinReligionWar)：同教的国家要对发起者有好感、不与被攻者同属一个帝国、
//     都城离被攻者都城不太远，一场圣战攻方最多六国。
public static class ReligiousTension
{
    public const string ToleranceFeature = "religious_tolerance", PollTaxFeature = "poll_tax";
    public const int MaxHolyWarAttackers = 6;
    // 收人头税的国家，异教之城的人口征兵只征几成
    public const float DhimmiLevy = 0.25f;
    public const float HolyWarReach = 160f;
    // 每年两成 → 半成(玩家 2026-10-21 反馈"除了阿拉伯和游牧，其他国家很容易分裂"：西欧、华夏长期不宽容，异教城一乱就起事)
    private const float RevoltChance = 0.05f;
    // 收人头税的国家起事机会打几折
    private const float PollTaxRevoltShare = 0.5f;
    // 人头税：中等税率(ReferenceTaxRate)时每 HouseholdsPerCoin 户交一钱，随国家税率增减
    private const int HouseholdsPerCoin = 10;
    private const double ReferenceTaxRate = 0.3d;
    private const int RevoltCooldownYears = 10;
    private static double _lastScan = -1d;

    public enum Policy { Intolerant, PollTax, Tolerant }

    // 中央(帝国按核心)的宗教选项是"有国教-自由信仰"或"无国教"
#if EC_PENDING
    private static bool FreeBelief(Realm kingdom)
    {
        Realm realm = kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;
        Regime regime = realm?.Record == null ? null : realm.GetRegime();
        return regime?.options != null && regime.options.ContainsKey("option_religion_type") &&
               regime.GetReligionLevel() <= ReligionLevel.Low;
    }
#endif

    public static void ResetWorldState()
    {
        _lastScan = -1d;
        // 教义合计、改宗渠道计数(见 ReligionTenets、ReligionDiagnostics)
        ReligionTenets.ResetWorldState();
        ReligionDiagnostics.Reset();
    }

#if EC_PENDING
    public static bool IsHeterodox(City city)
    {
        if (city?.Record == null || city.IsGone() || city.Realm?.Record == null || city.Realm.IsWild) return false;
        Religion state = city.Realm.religion, local = city.religion;
        return state != null && local != null && state != local;
    }

    public static Policy PolicyOf(Realm kingdom)
    {
        // 国教不排他(多神、萨满、三教、法教、神道，见 ReligionFaith)向来宽容；宗教选项在自由信仰及以下的也宽容(见 TempFac_自由信仰)
        if (kingdom?.Record != null && kingdom.hasReligion() && !ReligionFaith.IsExclusive(kingdom.religion)) return Policy.Tolerant;
        if (FreeBelief(kingdom)) return Policy.Tolerant;
        string culture = CultureService.GetRealmCulture(kingdom);
        // 宽容、人头税是制度特性，照制度文化(旧制犹存的照旧制，见 LegacyInstitutionSystem)；法教传统是文化本身的
        string institutions = CultureService.GetInstitutionCulture(kingdom);
        // 法教传统(印度)向来宽容(见 PapacySystem.DharmicTrait)
        if (InstitutionSystem.HasFeature(institutions, ToleranceFeature) ||
            EmpireCraft.Core.OnomasticsRule.HasPoliticalTrait(culture, PapacySystem.DharmicTrait)) return Policy.Tolerant;
        return InstitutionSystem.HasFeature(institutions, PollTaxFeature) ? Policy.PollTax : Policy.Intolerant;
    }
#endif

    // 异教之城征兵打几折(见 CorpsSystem.Mobilize)：收人头税的国家豁免异教之民的兵役，只征 DhimmiLevy
#if EC_PENDING
    public static float LevyFactor(City city) =>
        IsHeterodox(city) && PolicyOf(city.Realm) == Policy.PollTax ? DhimmiLevy : 1f;
#endif

    // 异教之城的稳定度损失(见 CityStabilitySystem.Natural)
#if EC_PENDING
    public static float StabilityLoss(City city) =>
        !IsHeterodox(city) ? 0f : PolicyOf(city.Realm) switch
        {
            Policy.Tolerant => 3f,
            Policy.PollTax => 6f,
            _ => 10f
        };
#endif

    // 异教之城的民族情绪每年增长(见 NationalSentimentSystem)
#if EC_PENDING
    public static float SentimentGain(City city) =>
        !IsHeterodox(city) ? 0f : PolicyOf(city.Realm) switch
        {
            Policy.Tolerant => 0f,
            Policy.PollTax => 1f,
            _ => 2f
        };

    public static string Describe(City city) =>
        string.Format(LM.Get("stability_religion_detail"), city.religion?.name ?? "", LM.Get("religion_policy_" + PolicyOf(city.Realm)));
#endif

    // 每年一次：人头税、宗教叛乱
#if EC_PENDING
    public static void TryYearlyScan()
    {
        if (EmpireWorld.Current?.Cities == null || ModClass.IS_CLEAR) return;
        double now = GameClock.Now;
        if (_lastScan >= 0d && now >= _lastScan && GameClock.YearsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (City city in EmpireWorld.Current.Cities.ToList())
        {
            try
            {
                if (!IsHeterodox(city)) continue;
                Policy policy = PolicyOf(city.Realm);
                if (policy == Policy.PollTax)
                {
                    CollectPollTax(city);
                    TryRevolt(city, RevoltChance * PollTaxRevoltShare);
                }
                else if (policy == Policy.Intolerant) TryRevolt(city, RevoltChance);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][宗教] {city?.data?.name} 结算失败: {exception.Message}");
            }
        }
        // 无小人模式：按背景人口创教、传教(见 BackgroundReligionSystem)
        try
        {
            using (FrameProfiler.Measure("宗教·背景人口传教")) BackgroundReligionSystem.YearlyPass();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宗教] 背景人口传教结算失败: {exception.Message}");
        }
        // 各文明按时代立本土大教、外来民族宗教回流(见 NativeFaithSystem)
        TryStep("本土立教", NativeFaithSystem.YearlyPass);
        // 同天下的地方教会并入正教(见 ReligionSphere)
        try
        {
            ReligionSphere.YearlyPass();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宗教] 地方教会并入正教失败: {exception.Message}");
        }
        // 教义融合(见 SyncretismSystem，用户 2026-11-06)：在统治者皈依之前，本年融到的教义当年就护着本教
        TryStep("教义融合", SyncretismSystem.YearlyPass);
        // 哈里发国只奉伊斯兰的补查(见 CaliphateFaith)；统治者皈依、迫害与驱逐、大分裂、民间教门(见各系统)
        TryStep("哈里发国定教", CaliphateFaith.YearlyPass);
        TryStep("统治者皈依", ReligionConversionSystem.YearlyPass);
        TryStep("迫害与驱逐", ReligiousPersecution.YearlyPass);
        TryStep("宗教分裂", ReligionSchismSystem.YearlyPass);
        TryStep("民间教门", SectUprisingSystem.YearlyPass);
        TryStep("历代教首", ReligionHeads.YearlyPass);
        // [宗教诊断] 本年各渠道改宗计数(见 ReligionDiagnostics)
        TryStep("宗教诊断", ReligionDiagnostics.Flush);
    }
#endif

    private static void TryStep(string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宗教] {name}结算失败: {exception.Message}");
        }
    }

#if EC_PENDING
    private static void CollectPollTax(City city)
    {
        if (!TreasurySystem.Enabled(city)) return;
        double rate = city.Realm.GetTaxRate();
        if (rate <= 0d) return;
        int tax = (int)(CityPopulationSystem.Households(city) / (double)HouseholdsPerCoin * rate / ReferenceTaxRate);
        if (tax > 0) TreasurySystem.CollectResidentTax(city, tax, TreasuryCategory.ResidentTax);
    }
#endif

    // 遭迫害的异教之城当年起事(见 ReligiousPersecution)：机会更大，其余条件同宗教叛乱
#if EC_PENDING
    public static void TryPersecutionRevolt(City city, float chance)
    {
        if (city?.Record != null && !city.IsGone() && city.Realm?.Record != null && !city.Realm.IsWild) TryRevolt(city, chance);
    }

    private static void TryRevolt(City city, float chance)
    {
        if (!CanRevolt(city) || EcRandom.value >= chance) return;
        string cityName = city.GetCityName();
        StartRevolt(city, "rebellion_reason_religion", Describe(city),
            string.Format(LM.Get("religious_revolt_kingdom_name"), cityName),
            string.Format(LM.Get("religious_revolt_war_name"), cityName),
            string.Format(LM.Get("religious_revolt_history"), cityName, city.religion?.name ?? ""));
    }
#endif

    private const int StartGraceYears = 10;

    // 能不能举事：不是都城、本国能起事、本城稳定低于起事线、十年内没起过事。打仗时照样会起事(以前打仗就不起，正与史实相反)
#if EC_PENDING
    public static bool CanRevolt(City city)
    {
        // 开局头几年不举事：城的稳定度还没养起来，剧本第一年就教门四起、都城旁边开打(一年 60 秒)
        if (EmpireWorld.Current == null || GameClock.Now < StartGraceYears * 60d) return false;
        Realm origin = city?.Realm;
        if (origin?.Record == null || city == origin.Capital || !RebellionSystem.CanAttempt(origin) || !CityStabilitySystem.CanRise(city) ||
            CityStabilitySystem.Effective(city) >= CityStabilityRules.RebellionThreshold || !ModernStability.PassRebellionGate(origin))
            return false;
        double last = city.GetOrCreate().last_harsh_uprising;
        return last < 0d || GameClock.YearsSince(last) >= RevoltCooldownYears;
    }
#endif

    // 举教起事(宗教叛乱、民间教门共用，见 SectUprisingSystem)：本城分出来立国，新国奉本城之教
#if EC_PENDING
    public static bool StartRevolt(City city, string reasonKey, string detail, string kingdomName, string warName, string history)
    {
        Realm origin = city.Realm;
        Religion faith = city.religion;
        Person leader = CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.SpawnRebelLeader(city) : null;
        leader ??= city.units?.Where(actor => actor != null && !actor.IsGone() && actor.Alive && actor.isAdult() &&
                                              !actor.isKing() && actor.CanFoundCivKingdom())
            .OrderByDescending(actor => actor.religion == faith).ThenByDescending(actor => actor.data?.renown ?? 0)
            .FirstOrDefault();
        if (leader == null) return false;
        RebellionCauseData cause = RebellionSystem.Capture(origin, city, reasonKey, detail);
        Realm rebel = city.makeOwnKingdom(leader, pRebellion: true);
        if (rebel == null) return false;
        if (!rebel.StartLocalRebelling(EmpireWarType.宗教叛乱))
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return false;
        }
        if (faith != null) rebel.setReligion(faith);
        rebel.Name = kingdomName;
        War war = EmpireWorld.Current.diplomacy.startWar(rebel, origin, WarTypeLibrary.rebellion);
        if (war == null)
        {
            RebellionStartupService.RollbackCitySplit(city, origin, rebel);
            return false;
        }
        war.SetEmpireWarType(EmpireWarType.宗教叛乱);
        RebellionSystem.Record(rebel, war, cause);
        war.data.name = warName;
        RebellionStartupService.RaiseUprisingMilitia(rebel, 0.5f);
        city.GetOrCreate().last_harsh_uprising = GameClock.Now;
        Empire empire = origin.GetEmpire();
        if (empire != null) EventRecorder.Record(empire, history, leader, origin);
        else EventRecorder.Record(origin, history, leader);
        return true;
    }
#endif

    // 圣战参战：没满员、不与被攻者同属一个帝国、都城离被攻者都城不太远
#if EC_PENDING
    public static bool CanJoinHolyWar(Realm kingdom, War war)
    {
        Realm defender = war?.main_defender;
        if (kingdom?.Capital == null || defender?.Capital == null || defender.IsGone()) return false;
        if (war._list_attackers.Count >= MaxHolyWarAttackers || kingdom.IsInSameEmpire(defender)) return false;
        return Vector2.Distance(kingdom.Capital.city_center, defender.Capital.city_center) <= HolyWarReach;
    }
#endif
}
}
