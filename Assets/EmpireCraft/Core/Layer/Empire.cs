// Generated from the EmpireCraft mod: Scripts/Layer/Empire.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
#if EC_PENDING
using NCMS;
#endif
using Random = System.Random;

namespace EmpireCraft.Core
{
// Token: 0x0200023B RID: 571
public class Empire : RecordObject<EmpireData>
{
    public const int PowerfulMinisterStageNone = 0;
    public const int PowerfulMinisterStageDominant = 1;
    public const int PowerfulMinisterStageDuke = 2;
    public const int PowerfulMinisterStageKing = 3;

#if EC_PENDING
    public BannerAsset BannerAsset;
    private Vector3 _lastEmpireCenter;
    private Vector3 _empireCenter;
#endif
    private bool _updatingHonoraryPeerages;
    private bool _completingMinisterUsurpation;
    public EmpireAddition Additions => data.additions;
#if EC_PENDING
    private readonly List<TileZone> _zoneScratch = new();
#endif
    private readonly int _avgCitiesPerKingdom = 3;
    public Clan EmpireClan;
#if EC_PENDING
    public int Mandate => MonarchyLegitimacy.Applies(this) ? MonarchyLegitimacy.Get(this) : data.Mandate;
#endif
    // 前现代正统按治理来源计算；现代国家与议会君主国按宪政合法性计算。
    // 叛乱、革命、附庸态度、城市忠诚都看它
#if EC_PENDING
    public int Legitimacy => EmpireCraft.Core.ModernLegitimacy.Applies(this)
        ? EmpireCraft.Core.ModernLegitimacy.Get(this)
        : Mandate;
#endif
    // 界面上统治合法性的名称：现代国家为"宪政合法性"，其余为"正统"
#if EC_PENDING
    public string LegitimacyLabelKey => EmpireCraft.Core.ModernLegitimacy.Applies(this)
        ? "label_constitutional_legitimacy"
        : "label_mandate";
    public Regime regime => CoreKingdom.GetRegime();
#endif
    public List<Realm> kingdoms_list = new List<Realm>();
    public HashSet<Realm> kingdoms_hashset = new HashSet<Realm>();
    public List<City> cities_list = new List<City>();
    
    //岁币国
    public List<Realm> given_Kingdoms = new List<Realm>();
    //朝贡国
    public List<Realm> taken_Kingdoms = new List<Realm>();
    
#if EC_PENDING
    public Religion Religion = null;
    private TemporaryFaction _runningTemporaryFaction;
#endif

    // 当前正在推进的派系诉求。
    //
    // 派系的诉求列表会被整体重建：制度解锁诉求(EnableClaim)、派系编辑界面、以及政体重载(LoadRegime 会
    // 从模板重新克隆派系)都会生成新的诉求对象。旧对象不再被调度器更新，却仍然是"已开始"状态——
    // 如果这里还指着它，诉求进度会永远冻结在当时的数值，而且因为它看起来一直在推进，别的诉求也
    // 都无法发起。所以每次读取都校验一次：引用已经不在任何派系的诉求列表里时，换成列表中同派系、
    // 同类型、仍在推进的新对象；找不到就换成列表里任意一个正在推进的诉求，都没有则清空。
#if EC_PENDING
    public TemporaryFaction RunningTemporaryFaction
    {
        get => _runningTemporaryFaction = ResolveRunningTemporaryFaction(_runningTemporaryFaction);
        set => _runningTemporaryFaction = value;
    }

    private TemporaryFaction ResolveRunningTemporaryFaction(TemporaryFaction current)
    {
        if (current == null) return null;
        List<FixedFaction> factions = CoreKingdom?.GetRegime()?.PlayerFactions;
        if (factions == null) return current;
        TemporaryFaction replacement = null;
        TemporaryFaction anyStarted = null;
        foreach (FixedFaction faction in factions)
        {
            if (faction?.TemporaryFactions == null) continue;
            foreach (TemporaryFaction claim in faction.TemporaryFactions)
            {
                if (claim == null) continue;
                if (ReferenceEquals(claim, current)) return current;
                if (!claim.IsStarted()) continue;
                anyStarted ??= claim;
                if (replacement == null && claim.type == current.type && claim.factionID == current.factionID)
                    replacement = claim;
            }
        }
        return replacement ?? anyStarted;
    }
#endif

    public Realm CoreKingdom;
    public Person Emperor => CoreKingdom?.Ruler;
    public int CurrentMoney => CoreKingdom?.GetMoney() ?? 0;
#if EC_PENDING
    private Vector3 _capitalCenter;
#endif
    public City OriginalCapital;
    public　SpecificClan EmpireSpecificClan => SpecificClanManager.Get(data.empire_specific_clan);
    
#if EC_PENDING
    public override MetaType meta_type => MetaTypeExtension.Empire;
#endif

    public bool HasEmperor()
    {
        return !Emperor.IsGone();
    }
    /// <summary>
    /// 增加或减少帝国正统值
    /// </summary>
    /// <param name="change">增加的数值</param>
    /// <returns></returns>
    // 开国气象：立国头 FoundingGraceYears 年人心思定，正统的损失减半(至少仍减 1)，
    // 免得新帝国刚称帝就被战争、罢工、民怨磨到叛乱门槛以下，永远统一不了；开国雄主在位期间一直有效
    public const int FoundingGraceYears = 10;

    // 统一战争期间、统一天下后三十年同样是开国气象(历史周期律，见 UnificationSystem)
#if EC_PENDING
    public bool InFoundingGrace => data != null &&
                                   // 剧本开局的老王朝(国祚按史实接着算，见 ConstitutionalEconomyState.dynastic_age_offset)不算新开国
                                   (data.created_time > 0d && GameClock.YearsSince(data.created_time) +
                                    (data.constitutional_economy?.dynastic_age_offset ?? 0) < FoundingGraceYears ||
                                    FounderGrace() ||
                                    EmpireCraft.Core.UnificationSystem.InGrace(CoreKingdom));
#endif

    // 开国雄主(或中兴雄主)在位的开国气象：只到登基后 FounderGraceYears 年；朝廷里篡位上来的(本帝国不是他开的)、
    // 绝嗣裂土时据地称帝承接旧朝廷的(inherited_decline)没有——积弊原样留着
    // (用户 2026-10-21 选"收窄开国气象"：以前雄主在位一直算，每个新开国之君都完整重置，积弊不长、乱世一转眼就过去)
    public const int FounderGraceYears = 25;

#if EC_PENDING
    private bool FounderGrace()
    {
        if (!RulerTraitSystem.FounderReigns(CoreKingdom)) return false;
        Person emperor = Emperor;
        if (emperor == null) return false;
        bool greatRestorer = emperor.HasTrait(RulerTraitSystem.GreatRestorer);
        if (!greatRestorer && (data.inherited_decline || emperor.Key != data.founder_actor_id)) return false;
        double since = data.newEmperor_timestamp;
        return since < 0d || EmpireWorld.Current == null || GameClock.Now < since ||
               GameClock.YearsSince(since) < FounderGraceYears;
    }

    public void AddMandate(int change)
    {
        if (change < 0 && InFoundingGrace) change = Math.Min(-1, change / 2);
        // 存档中的旧值保留为事件法统，不能拿综合正统来判断它是否越界。
        data.Mandate = (int)Math.Max(0L, Math.Min(100L, (long)data.Mandate + change));
        MonarchyLegitimacy.Invalidate(this);
        ModernLegitimacy.Invalidate(this);
    }
#endif

    public bool IsNeedToIncreaseMandate()
    {
        return data.last_increase_mandate_timestamp<0||GameClock.YearsSince(data.last_increase_mandate_timestamp)>=1;
    }
    public bool IsArchived()
    {
        return this.data?.archived ?? false;
    }
    public void Archive()
    {
        if (this.data != null) this.data.archived = true;
    }

#if EC_PENDING
    public void AddTaxRate(float addition = 0.1f)
    {
        if (!ConstitutionalEconomySystem.CanChangeTax(this)) return;
        float before = data.TaxRate;
        if (data.TaxRate < 1.0f)
        {
            data.TaxRate += addition;
            if (data.TaxRate >= 1.0f)
            {
                data.TaxRate = 1.0f;
            }
        }
        //增加税收减少正统性
        AddMandate(-(int)Math.Round((data.TaxRate - before) * 100f));
    }
#endif

    // 帝国基础税率的下限：地方实际税率 = 基础税率 + 政体税级加成，税级为"低"时没有加成，
    // 基础税率减到 0 就整个帝国一分税都收不上
    public const float MinimumTaxRate = 0.05f;

#if EC_PENDING
    public void SubTaxRate(float substraction = 0.1f)
    {
        if (!ConstitutionalEconomySystem.CanChangeTax(this)) return;
        float before = data.TaxRate;
        if (data.TaxRate > MinimumTaxRate)
        {
            data.TaxRate = Math.Max(MinimumTaxRate, data.TaxRate - substraction);
        }
        //减少税收增加正统性
        AddMandate((int)Math.Round((before - data.TaxRate) * 100f));
    }

    public List<Realm> GetKingdomNeighbours()
    {
        List<Realm> neighbours = new();
        foreach (var kingdom in EmpireWorld.Current.LiveRealms)
        {
            if (kingdom.IsInSameEmpire(CoreKingdom))continue;
            if (IsNeighbourWith(kingdom))
            {
                if (!neighbours.Contains(kingdom))
                {
                    neighbours.Add(kingdom);
                }
            }
        }

        return neighbours;
    }
    
    public List<Person> GetMembersWithTrait(string trait)
    {
        List<Person>  list = new List<Person>();
        foreach (Person actor in EmpirePopulation.Enumerate(kingdoms_hashset))
        {
            if (actor.HasTrait(trait))
            {
                list.Add(actor);
            }
        }
        return list;
    }

    public override IEnumerable<City> getCities()
    {
        return EmpireMembershipService.EnumerateCities(this);
    }
#endif

    public bool IsNeedToExam()
    {
        if (data.last_exam_timestamp <= 0) 
        {
            return true;
        }

        if (GameClock.YearsSince(data.last_exam_timestamp)>=4)
        {
            return true;
        }
        return false;
    }

#if EC_PENDING
    public new void AddRenown(int value)
    {
        this.CoreKingdom.Record.renown += value;
        if (this.CoreKingdom.Record.renown<=0)
        {
            this.CoreKingdom.Record.renown = 0;
        }
    }
#endif

    public bool IsNeedToOfficeExam()
    {
        if (data.last_office_exam_timestamp == -1L) return true;
        if (GameClock.YearsSince(data.last_office_exam_timestamp)>=1)
        {
            return true;
        }
        return false;
    }

#if EC_PENDING
    public new long getTotalDeaths()
    {
        long deaths = 0;
        foreach(Realm kingdom in kingdoms_hashset)
        {
            deaths += kingdom.getTotalDeaths();
        }
        return deaths;
    }
#endif

    public void StartEmpireExam()
    {
        this.data.last_exam_timestamp = GameClock.Now;
    }

    public int GetLastExamYear()
    {
        return GameClock.YearsSince(this.data.last_exam_timestamp);
    }

#if EC_PENDING
    public string GetCulture()
    {
        return CultureService.GetEmpireDefaultCulture(this);
    }

    private string GetNamingCulture()
    {
        string cultureName = GetCulture();
        return !string.IsNullOrWhiteSpace(cultureName) || CoreKingdom == null
            ? cultureName
            : CultureService.GetRealmCulture(CoreKingdom);
    }
#endif
    public bool IsAllowToMakeWar()
    {
        if (CurrentMoney<0)
        {
            return true;
        }
        return false;
    }

#if EC_PENDING
    public string EnsureEmpireCoreName()
    {
        if (data == null) return "";
        if (!string.IsNullOrWhiteSpace(data.core_name)) return data.core_name;
        string coreName = "";
        try
        {
            coreName = ExtractEmpireCoreName();
            if (string.IsNullOrWhiteSpace(coreName)) coreName = EmpireCraft.Core.ColonySystem.SovereignName(CoreKingdom);
        }
        catch
        {
            coreName = "";
        }
        if (string.IsNullOrWhiteSpace(coreName)) coreName = data.name ?? "";
        if (!string.IsNullOrWhiteSpace(coreName))
        {
            try { data.core_name = coreName; } catch { }
        }
        return coreName;
    }

    private string ExtractEmpireCoreName()
    {
        if (string.IsNullOrWhiteSpace(data.name)) return "";
        if (OverallHelperFunc.TryExtractEnglishPrefixedCountryName(data.name, out string prefixedName))
        {
            string localizedPrefix = OverallHelperFunc.LocalizeDirectPrefix(data);
            string[] coreParts = prefixedName.SplitNameParts();
            if (!string.IsNullOrWhiteSpace(localizedPrefix) && coreParts.Length > 1 &&
                string.Equals(coreParts[0], localizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return OverallHelperFunc.JoinNameParts(coreParts.Skip(1).ToArray());
            }
            return prefixedName;
        }

        string typeKey = string.IsNullOrWhiteSpace(data.empire_type_key)
            ? OverallHelperFunc.ResolveEmpireTypeKey(CoreKingdom?.GetRegime(), CoreKingdom)
            : data.empire_type_key;
        string typeName = string.IsNullOrWhiteSpace(typeKey) ? "" : LM.Get(typeKey);
        if (ShowsWesternOrdinal())
        {
            string ordinalName = OverallHelperFunc.StripLocalizedTypeSuffix(data.name,
                GetOrdinalText(data.western_ordinal) + typeName);
            if (!string.Equals(ordinalName, data.name.Trim(), StringComparison.Ordinal))
                return ordinalName.UseLocalizedNameSeparator();
        }
        string strippedName = OverallHelperFunc.StripLocalizedTypeSuffix(data.name, typeName);
        if (!string.Equals(strippedName, data.name.Trim(), StringComparison.Ordinal))
        {
            string localizedPrefix = OverallHelperFunc.LocalizeDirectPrefix(data);
            if (!string.IsNullOrWhiteSpace(localizedPrefix))
            {
                string[] separators = { ModClass.NARROW_SPACE + ModClass.NARROW_SPACE, ModClass.NARROW_SPACE, " " };
                foreach (string separator in separators)
                {
                    string prefix = localizedPrefix + separator;
                    if (!strippedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    strippedName = strippedName.Substring(prefix.Length).Trim();
                    break;
                }
            }
            return strippedName.UseLocalizedNameSeparator();
        }

        string[] nameParts = data.name.SplitNameParts();
        return nameParts.Length > 0 ? nameParts[0] : "";
    }

    public string GetEmpireName()
    {
        if (CoreKingdom?.HasCustomCountryNaming() == true)
        {
            string customName = CoreKingdom.GetCustomCountryShortName();
            if (!string.IsNullOrWhiteSpace(customName)) return customName;
        }
        if (data == null) return CoreKingdom?.GetKingdomName() ?? "";
        if (!string.IsNullOrWhiteSpace(data.core_name)) return data.core_name;
        if (string.IsNullOrWhiteSpace(data.name)) return CoreKingdom?.GetKingdomName() ?? "";
        return EnsureEmpireCoreName();
    }
#endif

    // 军阀时期(见 WarlordEraSystem)：帝国核心范围内的国家按中央/临时政府/军阀改称
#if EC_PENDING
    public string GetEmpireFullName()
    {
        // 复辟帝制期间(见 RestorationSystem)：国号改为"某某帝国"
        if (RestorationSystem.IsProclaimed(this)) return RestorationSystem.ImperialName(this);
        string baseName = GetBaseEmpireFullName();
        return WarlordEraSystem.DecorateName(this, baseName);
    }
#endif

    // 国号后缀词库的版本：词库内容有调整时加一，旧国号会按新词库核对一次
    private const int SuffixPoolVersion = 1;

#if EC_PENDING
    public string GetBaseEmpireFullName()
    {
        if (data == null) return "";
        try
        {
            if (CoreKingdom?.HasCustomCountryNaming() == true)
            {
                return CoreKingdom.GetCustomCountryFullName(GetEmpireName());
            }
            string coreName = EnsureEmpireCoreName();
            if (string.IsNullOrWhiteSpace(coreName)) return data.name ?? "";
            // 改制共和后国号后缀按执政理念取
            string ideologySuffix = CoreKingdom?.GetOrCreate().ideology_country_suffix;
            // 没经过改制就成了共和国的(直接组建政府、旧存档)补一个后缀，免得国号只剩核心名
            // 现代政体在年度结算把它标成共和国以前(SyncWithRegime 一年一次)也照此补，不落到政体帝国类别"现代国家"上
            if (string.IsNullOrWhiteSpace(ideologySuffix) && CoreKingdom != null &&
                (IsRepublicState || CoreKingdom.GetRegime()?.type == RegimeType.Modern))
                ideologySuffix = CoreKingdom.GetOrCreate().ideology_country_suffix =
                    PartySystem.PickCountrySuffix(this, IdeologyFamilies.StateIdeology(this));
            // 国号后缀词库更新后(如华夏补了"民国"等后缀)：旧国号的后缀不在本文化、本理念的现行词库里，重新挑一次
            if (!string.IsNullOrWhiteSpace(ideologySuffix) && CoreKingdom != null &&
                CoreKingdom.GetOrCreate().ideology_suffix_pool_version < SuffixPoolVersion)
            {
                var extra = CoreKingdom.GetOrCreate();
                extra.ideology_suffix_pool_version = SuffixPoolVersion;
                PartyIdeology stateIdeology = IdeologyFamilies.StateIdeology(this);
                List<string> pool = PartySystem.CountrySuffixPool(InstitutionSystem.GetCustomsCulture(this), stateIdeology);
                if (pool.Count > 0 && !pool.Contains(ideologySuffix))
                    ideologySuffix = extra.ideology_country_suffix = PartySystem.PickCountrySuffix(this, stateIdeology);
            }
            if (!string.IsNullOrWhiteSpace(ideologySuffix)) return OverallHelperFunc.JoinNameParts(coreName, ideologySuffix);
            // 从属之国不用帝号：全称只称"某国"，不带帝国、朝的后缀与序数、美号(见 EmpireFormationService.StyledAsSubject)
            if (EmpireFormationService.StyledAsSubject(CoreKingdom))
                return OverallHelperFunc.FormatEmpireFullName(coreName, LM.Get("subject_realm_suffix"), "", GetNamingCulture());

            if (string.IsNullOrWhiteSpace(data.empire_type_key))
                data.empire_type_key = OverallHelperFunc.ResolveEmpireTypeKey(CoreKingdom?.GetRegime(), CoreKingdom);

            string typeKey = data.empire_type_key;
            string typeName = string.IsNullOrWhiteSpace(typeKey) ? "" : LM.Get(typeKey);
            if (string.IsNullOrWhiteSpace(typeName) || string.Equals(typeName, typeKey, StringComparison.Ordinal))
                typeName = "";

            // 手选的后缀顶替政体给的那个(EmpireData.suffix_choice)
            if (!string.IsNullOrWhiteSpace(data.suffix_choice)) typeName = data.suffix_choice.Trim();

            string localizedPrefix = OverallHelperFunc.LocalizeDirectPrefix(data);
            if (ShowsWesternOrdinal())
            {
                typeName = GetOrdinalText(data.western_ordinal) + typeName;
                if (!OverallHelperFunc.IsHolyPrefix(data)) localizedPrefix = "";
            }
            return OverallHelperFunc.FormatEmpireFullName(coreName, typeName, localizedPrefix, GetNamingCulture()) +
                   OverallHelperFunc.SurnameSuffix(data);
        }
        catch
        {
            return data.name ?? "";
        }
    }
#endif

    // 剧本的国号(见 WorldMapScenario.BuildEmpires)，下一个立的帝国从头就用它、用完清空。以前先按法理定国号再改回剧本的，
    // 第一位皇帝的在位记录、即位记载都写成法理名，世系里李隆基是"雍"、李亨才是"唐"，同一家族像是改了国号
    // (用户 2026-11-04 转玩家"国号也给换了""哪怕是同一个家族的也会有问题")
    internal static string ScriptedFoundingName;

#if EC_PENDING
    private EmpireFoundingNameChoice SelectFoundingEmpireName(Realm kingdom, EmpireCore riseCore)
    {
        KingdomTitle mainTitle = kingdom?.GetMainTitle();
        SpecificClan clan = kingdom?.king?.GetSpecificClan();
        string ancestralEmpireName = "";
        Vector2 ancestralEmpirePosition = default;
        if (clan?.HasHistoryEmpire() == true)
        {
            var historyRecord = clan.GetHistoryEmpire();
            ancestralEmpireName = historyRecord.name;
            ancestralEmpirePosition = historyRecord.pos;
        }

        KingdomTitle ancestralTitle = kingdom?.GetAncestralMainTitleCandidate();
        KingdomTitle ownedTitle = kingdom?.king?.GetMainTitle();
        string coreName = EmpireCoreManager.GetFoundingEmpireName(riseCore);
        if (string.IsNullOrWhiteSpace(coreName)) coreName = riseCore?.name;

        // 按称帝路线定国号：宗室复国沿用前朝国号(加方位)；承续法统而非前朝血脉的，用家族发迹地的
        // 法理，其次都城所在的法理；其余路线(及非剧情称帝)仍按原规则，主法理优先。
        EmpireFormationRoute route = kingdom?.GetOrCreate().empire_formation_route ?? EmpireFormationRoute.None;
        // 天下之名(华夏据有法理、复国，西方用本帝国法理名)：有就照它，没有照旧规则(见 RealmNamingSystem)
        EmpireFoundingNameChoice realmChoice = RealmNamingSystem.SelectFoundingName(this, kingdom, riseCore, route);
        if (realmChoice != null) return realmChoice;
        EmpireFoundingNameChoice choice = null;
        if (route == EmpireFormationRoute.Restoration && !string.IsNullOrWhiteSpace(ancestralEmpireName))
        {
            choice = new EmpireFoundingNameChoice(ancestralEmpireName, EmpireFoundingNameSource.AncestralEmpire);
        }
        else if (route == EmpireFormationRoute.Legitimacy)
        {
            string originTitleName = clan?.GetAncestralCity()?.GetTitle()?.data?.name;
            string capitalTitleName = kingdom?.capital?.GetTitle()?.data?.name;
            if (!string.IsNullOrWhiteSpace(originTitleName))
                choice = new EmpireFoundingNameChoice(originTitleName, EmpireFoundingNameSource.ClanOriginTitle);
            else if (!string.IsNullOrWhiteSpace(capitalTitleName))
                choice = new EmpireFoundingNameChoice(capitalTitleName, EmpireFoundingNameSource.CapitalTitle);
        }
        choice ??= EmpireFoundingNameRules.Select(mainTitle?.data?.name,
            ancestralEmpireName, ancestralTitle?.data?.name, ownedTitle?.data?.name, coreName,
            EmpireCraft.Core.ColonySystem.SovereignName(kingdom));
        if (choice.Source == EmpireFoundingNameSource.AncestralEmpire)
        {
            data.directPre = GetDir(ancestralEmpirePosition);
        }
        return choice;
    }

    private void RecordFoundingNameHistory(EmpireFoundingNameChoice choice, Person founder)
    {
        if (choice == null || founder == null) return;
        string source = string.Format(LM.Get(choice.SourceLocalizationKey), choice.SourceName);
        string content = string.Format(LM.Get("history_empire_name_selected"), founder.FullName(), source,
            GetEmpireName());
        this.RecordHistory(directContent: content, actorId: founder.Key,
            kingdomId: CoreKingdom?.Key ?? -1L, prepend: true);
    }

    private void MoveToEmpireCapital(Person actor)
    {
        if (CoreKingdom?.capital?._city_tile==null) return;
        actor.JoinCity(this.CoreKingdom.Capital);
        actor.goTo(this.CoreKingdom.Capital._city_tile);
    }
#endif

    private void SyncCoreKingdomReference()
    {
        if (data == null) return;
        data.empire = CoreKingdom?.Key ?? -1L;
        data.last_core_kingdom_id = CoreKingdom?.Key ?? data.last_core_kingdom_id;
    }
    //提供岁币
#if EC_PENDING
    public void StartToGive()
    {
        if (TreasurySystem.Enabled(CoreKingdom))
        {
            if (!IsNeedToGive()) return;
            data.timestamp_given_time = GameClock.Now;
            var recipients = given_Kingdoms.Where(k => k != CoreKingdom && TreasurySystem.Enabled(k)).Distinct().ToArray();
            int available = StateSettlementSystem.DiscretionaryFunds(CoreKingdom);
            var report = TreasurySystem.Report(CoreKingdom);
            long incomeLimit = report.months > 0 ? report.stable_income / 10 : available / 10;
            int totalBudget = (int)Math.Min(available / 10, incomeLimit);
            data.given_annual_quotes ??= new Dictionary<long, int>();
            foreach (long id in data.given_annual_quotes.Keys.Where(id => !recipients.Any(k => k.Key == id)).ToArray())
                data.given_annual_quotes.Remove(id);
            foreach (Realm recipient in recipients)
                if (!data.given_annual_quotes.ContainsKey(recipient.Key))
                    data.given_annual_quotes[recipient.Key] = Math.Max(1, CityPopulationSystem.FiscalHouseholds(recipient) / 20);
            // 先覆盖能完整履约的协定，避免把预算分成所有协定都不够的碎款。
            foreach (Realm recipient in recipients.OrderBy(k => data.given_annual_quotes[k.Key]).ThenBy(k => k.Key))
            {
                int quote = Math.Max(1, data.given_annual_quotes[recipient.Key]);
                if (quote > totalBudget || !TreasurySystem.TrySpend(CoreKingdom, quote, TreasuryCategory.Gift))
                { recipient.RemoveGivenAlliance(); continue; }
                totalBudget -= quote;
                recipient.AddMoney(quote, TreasuryCategory.Gift);
                if (recipient.NeedToRemoveGivenAlliance()) recipient.RemoveGivenAlliance();
            }
            return;
        }
        data.timestamp_given_time = GameClock.Now;
        var tempGiven = given_Kingdoms.ToList();
        foreach (var kingdom in tempGiven)
        {
            if (AncientWarfareCompatibility.Owns(kingdom)) continue;
            if (CoreKingdom.GetMoney() <= 0)
            {
                kingdom.RemoveGivenAlliance();
                continue;
            }
            // 岁赐按人口(无小人模式按户，和单位个数同一量级)
            int gift = (CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.Households(this) : countUnits()) / 2;
            CoreKingdom.SubMoney(gift);
            kingdom.AddMoney(gift);
            if (kingdom.NeedToRemoveGivenAlliance())
            {
                kingdom.RemoveGivenAlliance();
            }
        }
    }
#endif
    public bool IsNeedToGive()
    {
        return GameClock.YearsSince(data.timestamp_given_time) >= 1;
    }
    
    //新皇登基
    private long _unregisteredEmperorRepairAttempt = -1L;

    // 自愈：皇帝在位，但当前这一朝的历史记录属于别人（空位期留下的记录，或即位登记中途失败），
    // 或即位时间记的是别人(reign_emperor_id)，说明这位皇帝从未完成即位登记——皇室、年号、在位历史都还停在上一任，
    // 年号、在位年数会从上一任起一直累加。后一种：继承人本是别国(兀鲁思、封国)君主时中央直接改成他的国
    // (EmpireCraftKingdomBehCheckKing.ChooseKingFromHeir)、中央灭亡改立别的成员国(TryRepairState)，都不经 setKing，
    // 当前这一朝的记录又是记事时按当今补开的，以前看不出来(用户 2026-10-21 截图：游牧汗国一位先汗"在位 136 年"，
    // 是几位汗的在位年数连在一起)。这里补做一次完整的即位处理。每位皇帝只尝试一次，避免即位处理本身出错时每帧重试。
#if EC_PENDING
    private void RepairUnregisteredEmperor()
    {
        Person emperor = Emperor;
        if (emperor?.Record == null || emperor.IsGone() || !emperor.Alive) return;
        // 旧档没记即位登记的是谁：校正一次旧的在位年数(见 ReignRecordSystem.RepairLegacyTenure)，当今算已登记
        if (data.reign_emperor_id < 0)
        {
            ReignRecordSystem.RepairLegacyTenure(this);
            data.reign_emperor_id = emperor.Key;
        }
        bool foreignPage = data.currentHistory != null && data.currentHistory.id != emperor.Key;
        if (!foreignPage && data.reign_emperor_id == emperor.Key) return;
        if (_unregisteredEmperorRepairAttempt == emperor.Key) return;
        _unregisteredEmperorRepairAttempt = emperor.Key;
        LogService.LogWarning($"帝国 {GetEmpireName()} 的皇帝 {emperor.FullName()} 未完成即位登记，补做即位处理。");
        // 登记过、本朝记录却是别人的(旧有的那种)：照旧只补登记
        if (data.reign_emperor_id == emperor.Key)
        {
            NewEmperor(emperor);
            return;
        }
        // 上一位登记过的君主的本朝记录还开着(没经 EmperorLeft 收进史书)：按他的即位时间收起，不随新开的一页丢掉
        if (foreignPage && data.currentHistory.id > 0 && data.currentHistory.id == data.reign_emperor_id)
        {
            data.currentHistory.total_time = GameClock.YearsSince(data.newEmperor_timestamp);
            data.history.Add(data.currentHistory);
            data.currentHistory = null;
        }
        CompleteSuccession(emperor);
    }
#endif

    // 即位后续：帝国核心换了君主之后的整套处理——即位登记(NewEmperor)、记入个人经历、重立储君、汗位更替之乱、
    // 哈里发更替之乱、水土(改从游牧的成员逢乱自立)、即位分封(兀鲁思、分割继承)。setKing(KingdomPatch.new_emperor)、
    // 继承人带着自己的国入继(EmpireCraftKingdomBehCheckKing.ChooseKingFromHeir，中央直接改成他的国)、中央灭亡改立别国后的
    // 月度补登记(RepairUnregisteredEmperor)都走这里。以前后两条路只换了人：汗国的新汗多是兀鲁思的汗(上一位分封出去的兄弟、
    // 儿子)，换汗走的正是中央改国这条路，不乱、不分封，和"大汗死后会分裂"的基调相反。
    // 同一次换君只走一遍：这位已经即位登记过(reign_emperor_id)就不再走(同一位皇帝挪到新中央也不算换君)；
    // 即位被拒(宪制不许、建不起宗族)、没登记上的没有后续
#if EC_PENDING
    public void CompleteSuccession(Person emperor)
    {
        if (emperor?.Record == null || data == null || data.reign_emperor_id == emperor.Key) return;
        NewEmperor(emperor);
        if (data.reign_emperor_id != emperor.Key) return;
        if (!RepublicSystem.IsRepublic(this))
            emperor.RecordPersonalHistory(string.Format(LM.Get("personal_history_became_emperor"), GetEmpireName()),
                "personal_history_became_emperor", major: true);
        CoreKingdom?.RemoveHeir();
        // 雄主之后第一次换汗：先按法理分兀鲁思给新汗的兄弟，再判汗位更替之乱——雄主一死，诸子各据一方
        // (用户 2026-10-21 选"游牧雄主死后真分裂"：以前先判后分，新分的兀鲁思不在其列，雄主死了只丢一成)
        bool heroSplit = UnificationSystem.HeroTurmoilPending(this);
        // 汗国崩解：正统太低的汗国大汗一死，同样先分兀鲁思，再让诸部(连同新分的)当场各自为汗，代替汗位更替之乱
        // (见 NomadCycleSystem.KhanateCollapse)。正统在分封之前看，分封论功给的天命不算
        int legitimacy = Legitimacy;
        bool collapse = NomadCycleSystem.CollapseDue(this);
        if (heroSplit || collapse)
        {
            EnfeoffmentHelper.OnEmperorSucceeded(this);
            // 原版各国的城市名单要到下一帧才重排：先排好，新分的兀鲁思才数得出城、算得进下面的自立与崩解
            EmpireWorld.Current?.kingdoms?.beginChecksCities();
        }
        // 汗国新汗即位：积弊深重或正统低落时各兀鲁思可能据地自立(雄主之后第一次换汗必乱，见 NomadCycleSystem)，
        // 平常先于即位分封——这一回新分出去的兄弟不在其列。
        // 汗位更替之乱、哈里发更替之乱各自查大分裂冷却(见 UpheavalGuard，用户 2026-10-21 选)：前一路开了分裂进程(预警，
        // 见 SchismSystem)，后一路随即被"已有分裂进程"挡住，同一次即位不再叠加；土崩瓦解收束期内两路都不判。
        // 水土(ShuiTuSystem)不算大分裂，不在此列
        if (collapse) NomadCycleSystem.KhanateCollapse(this, legitimacy);
        else NomadCycleSystem.OnKhanSucceeded(this);
        // 新哈里发即位：积弊深重或正统低落时世袭的埃米尔可能举兵自立(哈里发更替之乱，见 NomadCycleSystem)
        NomadCycleSystem.OnCaliphSucceeded(this);
        // 定居帝国换了君主：改从游牧的成员逢乱可能据地自立(见 ShuiTuSystem)
        ShuiTuSystem.OnSuzerainSucceeded(this);
        // 即位分封(按法理把土地封给新君的兄弟)；雄主之后的那一次已在上面分过
        if (!heroSplit && !collapse) EnfeoffmentHelper.OnEmperorSucceeded(this);
    }
#endif

    // 共和国元首更替、复辟迎回旧君：不是篡位，不触发分裂、内战、改国号那一套
#if EC_PENDING
    public bool InstallHeadOfState(Person actor)
    {
        Realm core = CoreKingdom;
        City capital = core?.Capital;
        if (actor == null || actor.IsGone() || !actor.Alive || core == null || capital == null) return false;
        if (ConstitutionalSuccessionSystem.ThroneBound(this) &&
            !ConstitutionalSuccessionSystem.CanInherit(this, actor)) return false;
        if (Emperor?.Key == actor.Key) return true;
        Person previousHead = Emperor;
        List<long> stateTitleIds = SnapshotSovereignTitles(previousHead, core);
        if (actor.IsRuler() && actor.Realm != core) actor.Realm.RemoveRuler();
        if (core.Ruler != null) core.RemoveRuler();
        actor.JoinCity(capital);
        actor.setKingdom(core);
        _completingMinisterUsurpation = true;
        try { core.SetRuler(actor); }
        finally { _completingMinisterUsurpation = false; }
        bool installed = Emperor?.Key == actor.Key;
        if (installed) TransferSovereignTitles(previousHead, actor, core, stateTitleIds);
        return installed;
    }
#endif

    private static List<long> SnapshotSovereignTitles(Person head, Realm core)
    {
        HashSet<long> titleIds = new HashSet<long>(head?.GetOwnedTitle() ?? new List<long>());
        if (core != null)
        {
            foreach (long titleId in core.GetRealmTitleIds()) titleIds.Add(titleId);
        }
        return titleIds.Where(titleId => titleId > 0 &&
            ModClass.KINGDOM_TITLE_MANAGER.Get(titleId) != null).ToList();
    }

#if EC_PENDING
    private static void TransferSovereignTitles(Person previousHead, Person newHead, Realm core,
        IEnumerable<long> titleIds)
    {
        if (newHead == null || core == null || titleIds == null) return;
        foreach (long titleId in titleIds.Distinct())
        {
            KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.Get(titleId);
            if (title == null || title.IsGone()) continue;
            if (previousHead != null && previousHead != newHead)
                previousHead.GetOwnedTitle()?.Remove(title.Key);
            if (title.owner != null && title.owner != newHead)
                title.owner.GetOwnedTitle()?.Remove(title.Key);
            newHead.AddOwnedTitle(title);
            title.owner = newHead;
            if (title.main_kingdom == core || core.IsTitleWithinRealm(title))
                core.RegisterRealmTitle(title);
        }
        core.TransferRealmTitlesToRuler(newHead);
    }
#endif

    private bool IsRepublicState => data?.constitutional_economy?.is_republic == true;
    // 没有王朝的国家：共和国，以及穿越政权(元老院选主席，见 TransmigrantSystem.Governs)——不定年号、不算篡位改朝、不改国号
    private bool NoDynasty => IsRepublicState || TransmigrantSystem.Governs(this);

#if EC_PENDING
    public void NewEmperor(Person actor, bool isNew = false)
    {
        if (actor == null) return;
        if (ConstitutionalSuccessionSystem.ThroneBound(this) &&
            !ConstitutionalSuccessionSystem.CanInherit(this, actor)) return;
        actor.SetEmpire(this);
        if (!NoDynasty) RegnalNameService.OnEmperorCrowned(this, actor);
        // 共和国元首、穿越政权的主席按选举更替，不扣正统
        if (NoDynasty) isNew = true;
        // 选帝侯推举的皇帝(选帝制)是正常换届，不吃普通即位的 -20；篡位、政变、权臣代立不走选举，照旧扣
        if (!isNew && !HolyRomanSystem.Electing)
        {
            AddMandate(-20);
        }
        string nameEmpire = "";
        SpecificClan previousRoyalClan = EmpireSpecificClan;
        Clan previousEmpireClan = EmpireClan;
        actor.CheckSpecificClan();
        //检查帝国分裂
        var currentSpecificClan = actor.GetSpecificClan();
        if (currentSpecificClan != previousRoyalClan && data.empire_specific_clan != -1L &&
            IsDynasticSuccessor(actor, previousRoyalClan, previousEmpireClan))
        {
            currentSpecificClan = RestoreDynasticSuccessorClan(actor, previousRoyalClan, previousEmpireClan);
            LogService.LogInfo($"修复皇位继承宗族：{actor.FullName()} 继承原皇族，不更改国号");
        }
        if (currentSpecificClan == null)
        {
            LogService.LogError($"新皇 {actor.FullName()} 无法建立宗族身份，取消帝国继承处理");
            return;
        }
        // 有些继位直接换了中央或君主，没有调用旧君退位；在开新页前保留上一位的记录。
        if (data.currentHistory is { id: > 0 } previous && previous.id != actor.Key)
            CloseCurrentReignRecord();
        // 篡位的连带后果（分裂、内战、改国号、清空帝系）牵涉大量外部状态，任何一步抛异常都不能
        // 打断下面的即位登记——否则皇室、年号、历史都停留在上一任，年号会一直累加下去。
        // 选帝制(神圣罗马式)换了家族是正常换届，不算篡位
        // 华夏原始政体推举共主(禅让，见 ShanrangSystem)：换了氏族也是正常换届
        bool usurpation = currentSpecificClan != previousRoyalClan && data.empire_specific_clan != -1L &&
                          !NoDynasty && !HolyRomanSystem.IsElective(this) && !ShanrangSystem.Elective(this);
        try
        {
            if (usurpation)
            {
                LogService.LogInfo("篡位逻辑");
                LogService.LogInfo($"上一任皇室: {EmpireSpecificClan?.name??"None"}");
                LogService.LogInfo($"皇室活人: {EmpireSpecificClan?.Count??0}");
                LogService.LogInfo($"合法继承人: {EmpireSpecificClan?.all_valid_members.Count??0}");
                // 被篡也算前朝亡了：同一个帝国换了姓，不走解散，亡国时间要在这里记，宗室才打得了旧号(见 RealmNamingSystem.OldDynastyHeir)
                if (previousRoyalClan != null) previousRoyalClan.empire_fall_timestamp = GameClock.Now;
                // 先朝宗室可能在离篡位者都城最远的那一半另立朝廷(看原来的法统，下个月起事，见 EmpirePartitionSystem)
                if (previousRoyalClan != null)
                    EmpirePartitionSystem.QueueRestoration(this, previousRoyalClan, data.core_name, Mandate,
                        _completingMinisterUsurpation);
                AddMandate(-30);
                // 世家：篡位者出自世家则多附之，起于寒门则耻之(见 GentrySystem)
                GentrySystem.OnUsurpation(this, actor);
                foreach (var k in kingdoms_list.ToList())
                {
                    if (_completingMinisterUsurpation) break;
                    if (k.IsEmpire()) continue;
                    if (!k.HasRuler()) continue;
                    var clan = k.Ruler.GetSpecificClan();
                    if (clan.id == data.empire_specific_clan)
                    {
                        leave(k);
                        DiplomacyHelpers.wars.newWar(k, CoreKingdom, WarKinds.Normal);
                    }
                }
                bool restoredHouse = currentSpecificClan.HasHistoryEmpire();
                if (restoredHouse)
                {
                    var historyRecord = currentSpecificClan.GetHistoryEmpire();
                    data.directPre = GetDir(historyRecord.pos);
                    SetEmpireName(historyRecord.name);
                }
                // 华夏：宗室复辟沿用旧号；其余改朝换代用篡位者的封国名，没有封国用美号(见 RealmNamingSystem.Founding)
                if (RealmNamingSystem.Enabled && RealmNamingSystem.IsHuaxia(this))
                {
                    if (restoredHouse || RealmNamingSystem.NameUsurper(this, actor))
                        currentSpecificClan.RecordHistoryEmpire(this, CoreKingdom.Capital);
                }
                else if (CoreKingdom.GetRegime().type == RegimeType.LvLing)
                {
                    data.directPre = "";
                    nameEmpire = actor.culture.getOnomasticData(MetaType.Kingdom).generateName();
                    SetEmpireName(nameEmpire);
                    currentSpecificClan.RecordHistoryEmpire(this, CoreKingdom.Capital);
                }
                isNew = true;
                data.history_emperrors.Clear();
                CoreKingdom.updateColor(getColorLibrary().getNextColor(actor.getActorAsset()));
                updateColor(CoreKingdom.getColor());
                foreach (var tk in taken_Kingdoms.ToList())
                {
                    if (AncientWarfareCompatibility.Owns(tk)) continue;
                    tk.RemoveTakenAlliance();
                }

                foreach (var k in kingdoms_list.ToList())
                {
                    if (_completingMinisterUsurpation) break;
                    // 新入帝国未满十年、神力拉入未满期的不随改朝离去(见 MembershipCooldown.Settled)
                    // 不许自立时不离去(见 SecessionGate；不记叛离，leave 那道门拦不到)
                    if (!k.isOpinionTowardsKingdomGood(CoreKingdom)&&Legitimacy<20&&MembershipCooldown.Settled(k)&&
                        !SecessionGate.Forbids("改朝离心", k.Record?.Name))
                    {
                        this.leave(k);
                        // 不服新朝、离心而去记在各国之主身上(量刑天平的叛逆砝码)
                        if (k.GetEmpire() != this) EmpireCraft.Core.EmpireLawSystem.NoteSecession(k);
                    }
                }
            } 
        
        }
        catch (Exception exception)
        {
            LogService.LogError($"新皇 {actor.FullName()} 即位时处理篡位后果失败，继续完成即位登记: {exception}");
            if (usurpation) isNew = true;
        }
        
        data.empire_specific_clan = currentSpecificClan.id;
        if (isNew)
        {
            data.dynasty_founder_actor_id = actor.Key;
            // 改朝换代：前朝最后一位皇帝即末代之君(谥法另论，见 PosthumousNameGenerator)。
            // 王朝积弊按三档承接(用户 2026-10-26，见 DynasticCycleSystem.OnDynastyChanged)：权臣篡位、外戚代立、太后另立、
            // 议立外姓、争位得胜是朝廷里的易代——保留(如司马代魏)；兵变禅让(MilitaryCoupSystem 声明)——减半；其余清空
            if (!NoDynasty) ReignRecordSystem.OnDynastyChanged(this, actor, _completingMinisterUsurpation);
        }
        EmpireClan = actor.Clan;
        CoreKingdom?.SetSpecificClan(currentSpecificClan);
        // 开国之君族里没有成年男丁的，补记已成年的子孙(见 VirtualGenealogySystem.EnsureFounderHouse)
        if (isNew && !NoDynasty) VirtualGenealogySystem.EnsureFounderHouse(actor);
        //设定天子身份并移居首都
        if (actor.isOfficer())
        {
            actor.RemoveIdentity();
        }
        actor.SetPeeragesLevel(PeeragesLevel.peerages_0);
        
        actor.Renown += 20;
        MoveToEmpireCapital(actor);
        if (NoDynasty)
        {
            data.year_name = "";
            data.newEmperor_timestamp = GameClock.Now;
        }
        // 以前一部自立的部落(帝国)换首领不刷继位提示；10-26 起单个部落不再是帝国(见 TribalFissionService)，
        // 部落换首领走原版王国的继位，这里只剩部落联盟，照常提示
        else if (data.has_year_name)
        {
            create_year_name();
            //公屏提示
            TranslateHelper.LogNewEmperor(actor, CoreKingdom.Capital, data.year_name, isNew);
        }
        else
        {
            create_year_name();
            TranslateHelper.LogNewEmperorWest(actor, CoreKingdom.Capital, isNew);
        }
        
        
        // 即位时间(newEmperor_timestamp，上面定年号时记下)属于这位(见 RepairUnregisteredEmperor)
        data.reign_emperor_id = actor.Key;
        //记录历史
        this.RecordNewEmperorHistory(isNew);
        RulerTraitSystem.OnCrowned(this, actor);
        // 新君即位大赦天下(只限君主制，见 EmpireLawSystem.OnRulerAccession)
        EmpireLawSystem.OnRulerAccession(CoreKingdom, actor);
        // 君主早逝、皇位更迭频繁推高王朝积弊(用户 2026-10-26，见 DynasticCycleSystem.OnSuccession)
        if (!NoDynasty) DynasticCycleSystem.OnSuccession(this, actor, isNew);
    }
#endif

    private bool IsDynasticSuccessor(Person actor, SpecificClan previousRoyalClan, Clan previousEmpireClan)
    {
        if (actor == null || previousRoyalClan == null) return false;
        if (actor.GetSpecificClan() == previousRoyalClan) return true;
        // 皇族分封出去的支系(某某支)仍是宗室：以前只有立宪保护时才认，先帝之孙因父亲受封分了支，
        // 即位被当成篡位，律令制下随机改了国号，前朝宗室还另立朝廷
        if (SpecificClanManager.SameLineage(actor.GetSpecificClan(), previousRoyalClan)) return true;
        if (previousEmpireClan != null && actor.Clan == previousEmpireClan) return true;

        PersonalClanIdentity successorIdentity = actor.GetPersonalIdentity();
        // 父系往上数代出自皇族的(先帝之孙、曾孙，或族谱分户后不在皇族宗族里的宗室)
        PersonalClanIdentity ancestor = successorIdentity;
        for (int generation = 0; generation < 8 && ancestor != null; generation++)
        {
            ancestor = ancestor.father > 0 ? SpecificClanManager.getPerson(ancestor.father) : null;
            if (ancestor == null) break;
            SpecificClan ancestorClan = SpecificClanManager.Get(ancestor.specific_clan_id);
            if (ancestorClan == previousRoyalClan || SpecificClanManager.SameLineage(ancestorClan, previousRoyalClan))
                return true;
        }
        EmpireCraftHistory previousReign = data.history?.LastOrDefault(history => history != null && history.id > 0);
        if (previousReign == null) return false;
        SpecificClanManager._actorToPersonLookup.TryGetValue(previousReign.id, out var previousIdentity);
        previousIdentity ??= EmpireWorld.Current.PersonByKey(previousReign.id)?.GetPersonalIdentity();
        if (previousIdentity != null && successorIdentity != null)
        {
            if (successorIdentity.father == previousIdentity.id || successorIdentity.mother == previousIdentity.id ||
                previousIdentity.children.Contains(successorIdentity.id)) return true;
        }

        Person previousEmperor = EmpireWorld.Current.PersonByKey(previousReign.id);
        return actor.GetParents().Any(parent => parent != null && parent.Key == previousReign.id) ||
               (previousEmperor?.GetChildren().Any(child => child != null && child.Key == actor.Key) ?? false);
    }

#if EC_PENDING
    private static SpecificClan RestoreDynasticSuccessorClan(Person actor, SpecificClan previousRoyalClan,
        Clan previousEmpireClan)
    {
        if (actor == null || previousRoyalClan == null) return actor?.GetSpecificClan();
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        if (identity == null)
        {
            previousRoyalClan.addActor(actor);
            identity = actor.GetPersonalIdentity();
        }
        else if (identity.specific_clan_id != previousRoyalClan.id)
        {
            identity._specificClan?.TakePerson(identity.id);
            identity.specific_clan_id = previousRoyalClan.id;
            previousRoyalClan.Upsert(identity);
            actor.SetPersonalIdentity(identity);
        }

        if (previousEmpireClan != null)
        {
            if (actor.Clan != previousEmpireClan) actor.setClan(previousEmpireClan);
            previousEmpireClan.SetSpecificClan(previousRoyalClan);
        }
        // 归回皇族也随皇族的姓(以前只改了宗族，名字还是母亲的姓)
        string royalSurname = previousRoyalClan.AllAliveMembers.Where(member => member != actor)
            .Select(member => member.GetPersonalIdentity()?.surname).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        royalSurname ??= SpecificClanManager.getPerson(identity?.father ?? -1L)?.surname;
        if (string.IsNullOrWhiteSpace(royalSurname)) royalSurname = previousRoyalClan.name;
        // 以前只有华夏一系(宗法父系)改姓：别的文化入了皇族、名字却还挂着母亲的姓，帝王表上一代一个姓
        // (用户 2026-10-28 问"为什么皇帝会换姓")。名字里本来有姓的都改随皇族；本来不带姓的文化不硬加
        if (previousRoyalClan.Patrilineal() || !string.IsNullOrWhiteSpace(actor.GetModName()?.familyName))
            SurnameInheritanceSystem.ApplySurname(identity, royalSurname);
        return previousRoyalClan;
    }
#endif
    public bool IsNeedToChooseLovers()
    {
        if (GameClock.YearsSince(data.last_select_lovers_timestamp) >= 3)
        {
            data.last_select_lovers_timestamp = GameClock.Now;
            return true;
        }
        else
        {
            return false;
        }
    }
#if EC_PENDING
    public void UpdateCapital(City capital)
    {
        this.OriginalCapital = capital;
        this._capitalCenter = capital.city_center;
    }

    public bool IsNeighbourWith(Realm kingdom)
    {
        foreach (Realm kingdom1 in this.kingdoms_list)
        {
            foreach(City city in kingdom1.Cities)
            {
                if (city.neighbours_kingdoms.Count > 0)
                {
                    foreach(Realm kingdom2 in city.neighbours_kingdoms)
                    {
                        if (kingdom2 == kingdom) 
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }
#endif

    // 帝国解散：末代君主的在位记录还挂在 currentHistory 上，收进史书再随帝国归档(见 Dispose、EmpireManager.dissolveEmpire)
    public void CloseCurrentReignRecord()
    {
        if (data?.currentHistory == null) return;
        data.history ??= new List<EmpireCraftHistory>();
        if (!data.history.Contains(data.currentHistory))
        {
            if (data.newEmperor_timestamp > 0d)
                data.currentHistory.total_time = GameClock.YearsSince(data.newEmperor_timestamp);
            data.history.Add(data.currentHistory);
        }
        data.currentHistory = null;
    }

#if EC_PENDING
    public void EmperorLeft()
    {
        Person emperor = Emperor;
        if (emperor?.Record == null) return;
        bool protectedDynasty = ConstitutionalSuccessionSystem.ThroneBound(this);
        SpecificClan previousClan = emperor.GetSpecificClan();
        data.currentHistory ??= new EmpireCraftHistory
        {
            id = emperor.Key,
            year_name = data.year_name,
            emperor = emperor.Name,
            empire_name = this.GetEmpireName(),
            empire_full_name = this.GetEmpireFullName(),
            dynasty_name = this.GetEmpireName(),
            royal_surname = previousClan?.name ?? "",
            miaohao_name = "",
            shihao_name = "",
            descriptions = new List<HistoryDescription>(),
            is_republic = NoDynasty,
            office_title = IsRepublicState ? RepublicSystem.GetHeadOfStateTitle(this)
                : NoDynasty ? LM.Get(DynastyNaming.RankKey(RegimeType.Transmigrant, 0, CoreKingdom)) : ""
        };
        RepairFoundingEmperorMarker();
        if (!NoDynasty)
            this.RecordHistory(
                emperor.Alive ? EmpireHistoryType.emperor_left_history : EmpireHistoryType.emperor_die_history,
                new Dictionary<string, string>()
                {
                    ["year_name"] = data.year_name,
                    ["actor"] = emperor.Name
                });
        data.history_emperrors.Add(emperor.Name);
        if (string.IsNullOrWhiteSpace(data.currentHistory.empire_full_name))
            data.currentHistory.empire_full_name = GetEmpireFullName();
        emperor.RemoveEmpire();
        if (!protectedDynasty) data.empire_specific_clan = previousClan?.id ?? -1L;
        LogService.LogInfo("上一任皇氏族记录:" + (previousClan?.name ?? "无"));
        // 没来得及补登记就走了的(见 RepairUnregisteredEmperor)：从上一位最后一条记事算起，不连上一位的年数
        data.currentHistory.total_time = GameClock.YearsSince(ReignRecordSystem.ReignStart(this, emperor));
        ReignRecordSystem.OnReignEnd(this, emperor);
        data.history.Add(data.currentHistory);
        data.currentHistory = null;
        // 空位：不再有登记在位的君主(0；-1 留给旧档)，同一人复位也照常即位(见 CompleteSuccession)
        data.reign_emperor_id = 0L;
    }
#endif
    // 追封先帝(谥号、庙号)是君主制的礼制：共和国与现代政体不追封，历史记录里的前朝皇帝也不例外
#if EC_PENDING
    public bool AllowsPosthumousNames() =>
        !RepublicSystem.IsRepublic(this) && CoreKingdom?.GetRegime()?.type != RegimeType.Modern &&
        EmpireCraft.Core.RealmNamingSystem.UsesCourtNames(this);
#endif

    // 不用年号、庙号、谥号的帝国(华夏、日本以外的文化线，见 RealmNamingSystem.UsesCourtNames)：关掉年号，
    // 撤掉以前按政体给先帝定的庙号谥号、记的年号(世界地图剧本写的史实称号 ScriptedTitle 留着)；每年定谥时顺带查一次
#if EC_PENDING
    public void DropCourtNames()
    {
        if (data == null || EmpireCraft.Core.RealmNamingSystem.UsesCourtNames(this)) return;
        data.has_year_name = false;
        data.year_name = "";
        foreach (EmpireCraftHistory history in data.history ?? new List<EmpireCraftHistory>())
        {
            if (history == null) continue;
            history.year_name = "";
            if (history.shihao_name == PosthumousNameGenerator.ScriptedTitle) continue;
            history.shihao_name = "";
            history.shihao_long = null;
            history.miaohao_name = "";
            history.miaohao_suffix = "";
        }
    }
#endif

    // 为本朝还没定谥的先帝定谥号、庙号(谥法见 PosthumousNameGenerator)，conferrer 为定谥的今上；返回定了几位。
    // 用户 2026-10-17 "藩王都是有谥号的""皇帝也要显示一样的"：以前只有今上发起"追封"决议才定，无小人时这条决议
    // 几乎没人发起，历代先帝都没有谥号；现在每年由今上自动为先帝定谥(见 EmpireCraftEmpireBehCheckMandate，决议照旧可用)。
    // 自动定谥一次补了好几位(旧存档)时只为最近的一位弹提示，史册照记每一位
#if EC_PENDING
    public int ConferPosthumousNames(Person conferrer, bool announceAll = false)
    {
        DropCourtNames();
        if (conferrer?.Record == null || !AllowsPosthumousNames()) return 0;
        var pending = new List<EmpireCraftHistory>();
        foreach (EmpireCraftHistory cHistory in data.history)
        {
            if (string.IsNullOrEmpty(cHistory.emperor) || !string.IsNullOrEmpty(cHistory.shihao_name)) continue;
            Person actor = EmpireWorld.Current.PersonByKey(cHistory.id);
            if (actor != null && actor.Alive) continue;
            pending.Add(cHistory);
        }
        foreach (EmpireCraftHistory cHistory in pending)
        {
            if (IsFoundingEmperorHistory(cHistory)) cHistory.is_first = true;
            var names = PosthumousNameGenerator.Decide(this, cHistory);
            cHistory.shihao_name = names.shi;
            cHistory.miaohao_name = names.miao;
            cHistory.miaohao_suffix = names.miao_suffix;
            // 本朝施行"增广尊谥"后(或帝国设置开了长谥号)，有庙号的先帝得明式长谥
            if ((data.long_posthumous || data.show_long_posthumous) && names.HasTemple)
                cHistory.shihao_long = PosthumousNameGenerator.LongName(names.shi, cHistory.is_first);
            string template = LM.Get(names.HasTemple
                ? "history_name_previous_emperor_reason"
                : "history_name_previous_emperor_no_temple");
            this.RecordHistory(directContent: template
                    .Replace("$actor$", conferrer.Name)
                    .Replace("$actor2$", cHistory.emperor)
                    .Replace("$reason$", LM.Get(names.reason))
                    .Replace("$shihao$", cHistory.shihao_long is { Count: > 0 }
                        ? PosthumousNameGenerator.ShiTitle(cHistory, this)
                        : LM.Get(cHistory.shihao_name))
                    .Replace("$miaohao$", names.HasTemple
                        ? LM.Get(cHistory.miaohao_name) + LM.Get(cHistory.miaohao_suffix)
                        : ""),
                actorId: conferrer.Key);
            if (announceAll || cHistory == pending[pending.Count - 1])
                TranslateHelper.LogEmpeorNamingPreviousEmperor(conferrer, cHistory.emperor);
        }
        return pending.Count;
    }

    public bool IsNeedToSetPosthumous()
    {
        if (!AllowsPosthumousNames()) return false;
        if (this.data.history.Count > 0)
        {
            foreach (EmpireCraftHistory cHistory in this.data.history)
            {
                Person actor = EmpireWorld.Current.PersonByKey(cHistory.id);
                if (!string.IsNullOrEmpty(cHistory.emperor))
                {
                    // 以谥号判断是否已追封：失位、早夭、末代之君按谥法不立庙号，只有谥号
                    if (string.IsNullOrEmpty(cHistory.shihao_name))
                    {
                        if (actor != null)
                        {
                            if (!actor.Alive)
                            {
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }
#endif

    public int GetEmperorYear()
    {
        return GameClock.YearsSince(this.data.newEmperor_timestamp) + 1;
    }

    public string GetYearNameWithTime()
    {
        if (IsRepublicState) return RepublicSystem.GetCalendarText(this);
        // 穿越政权用公元纪年
        if (NoDynasty) return string.Format(LM.Get("republic_common_era_format"), GameClock.YearsSince(0d) + 1);
        if (this.data.has_year_name)
        {
            if (this.Emperor!=null)
            {
                if (this.data.year_name != "" || this.data.year_name != null)
                {
                    return OverallHelperFunc.JoinNameParts(this.data.year_name, GetYearNameYear() + LM.Get("Year"));
                }
            }
        }
        else
        {
            if (this.Emperor!=null)
            {
                if (this.data.year_name != "" || this.data.year_name != null)
                {
                    return OverallHelperFunc.JoinNameParts(Emperor.GetModName()?.firstName ?? Emperor.FullName(),
                        GetEmperorYear() + LM.Get("Year"));
                }
            }
        }
        return "";
    }

    public bool CanJoinWar()
    {
        return GameClock.MonthsSince(data.timestamp_invite_war_cool_down)>=3;
    }

    // continuing：同一帝国换了中央接续(皇族王国接续、派系胜方接管，见 ReplaceEmpire)
#if EC_PENDING
    public void CreateNewEmpire(Realm kingdom, bool isSplit = false, Empire continuing = null)
    {
        if (kingdom == null) return;
        if (kingdom.Record == null) return;
        if (!kingdom.Alive) return;
        if (!kingdom.HasRuler()) return;
        kingdom.ai.setTask("do_mod_empire_beh");
        // 帝国的文化按立国之国本身的文化，不按君主个人的(用户 2026-10-27："为什么皇帝的个人文化会直接改变政体……
        // 应该根据国家文化反向影响皇帝个人")：以前称帝、接续都改从君主个人之俗，外族出身的新君一登基，整个国家连政体都换了。
        // 接续的：新中央不是原帝国这一文化圈的，改从原帝国的文化(帝国还是那个帝国)。君主个人改从本国之俗见 CultureService.AssimilateRuler
        string founderCulture = CultureService.GetRealmCulture(kingdom);
        string kept = continuing == null ? null
            : CompositeEmpireService.IsComposite(continuing) ? CompositeEmpireService.GetRulingCulture(continuing)
            : InstitutionSystem.GetCustomsCulture(continuing);
        if (CultureService.IsValidCulture(kept) && (!CultureService.IsValidCulture(founderCulture) ||
                                                    InstitutionSystem.GetCultureLine(kept) != InstitutionSystem.GetCultureLine(founderCulture)))
        {
            CultureService.SetRealmCulture(kingdom, kept, updatePoliticalSystem: true, allowLegacyInstitutions: false);
            founderCulture = kept;
        }
        else if (CultureService.IsValidCulture(founderCulture))
            CultureService.SetRealmCulture(kingdom, founderCulture, updatePoliticalSystem: true);
        else
        {
            CultureService.ApplyFounderCulture(kingdom, kingdom.Ruler);
            founderCulture = CultureService.GetFounderCulture(kingdom.Ruler);
        }
        CultureService.AssimilateRuler(kingdom, kingdom.Ruler);
        if (CultureService.IsValidCulture(founderCulture))
        {
            data.conquest_founder_culture = founderCulture;
            data.ruling_culture = founderCulture;
            data.external_identity_culture = founderCulture;
        }
        kingdom.GetOrCreate().isEmpire = true;
        data.history_emperrors = new List<string>();
        data.heir_type = EmpireHeirLawType.eldest_child;
        data.last_exam_timestamp = GameClock.Now;
        StartEmpireExam();
        Regime regime = kingdom.GetRegime();
        if (regime != null)
        {
            if (regime.type == RegimeType.YouMu)
            {
                data.directPre = "great";
            }
            data.has_year_name = regime.HasEraName() && EmpireCraft.Core.RealmNamingSystem.UsesCourtNames(this);
            regime.GetPlayerFactions().ForEach(f=>
            {
                f.EmpireId = this.Key;
                f.FixMissedTemporaryFactions();
                f.TemporaryFactions.ForEach(tf=>tf.Init(f));
            });
        }
        data.timestamp_invite_war_cool_down = GameClock.Now;
        CoreKingdom = kingdom;
        SyncCoreKingdomReference();
        data.centerOffice = new CenterOffice();
        data.centerOffice.Init(CoreKingdom);
        kingdom.SetLevel(0);
        if (CoreKingdom.getKingClan() != null) this.EmpireClan = this.CoreKingdom.getKingClan();
        else 
        {
            EmpireClan = null;
        }
        OriginalCapital = kingdom.Capital;
        data.banner_icon_id = kingdom.Record.banner_icon_id;
        data.banner_background_id = kingdom.Record.banner_background_id;
        data.timestamp_established_time = GameClock.Now;
        try
        {
            _capitalCenter = kingdom.Capital.city_center;
        } catch (Exception e)
        {
            LogService.LogError($"读取帝国首都失败: {e}");
        }
        generateNewMetaObject();
        EmpireCore riseCore = EmpireCoreManager.GetRiseCandidateCore(kingdom);
        EmpireFoundingNameChoice foundingName = SelectFoundingEmpireName(kingdom, riseCore);
        string scriptedName = ScriptedFoundingName;
        ScriptedFoundingName = null;
        bool scripted = !string.IsNullOrWhiteSpace(scriptedName);
        SetEmpireName(scripted ? scriptedName : foundingName.Name);
        AssignWesternOrdinal(data.core_name);
        if (data.western_ordinal >= 2) SetEmpireName(data.core_name);
        try
        {
            NewEmperor(kingdom.Ruler, !isSplit);
            if (!scripted) RecordFoundingNameHistory(foundingName, kingdom.Ruler);
            if (data.has_year_name)
            {
                this.RecordHistory(EmpireHistoryType.new_empire_history, new Dictionary<string, string>()
                {
                    ["actor"] = kingdom.Ruler?.FullName()??"",
                    ["place"] = kingdom.Capital.GetCityName(),
                    ["name"] = GetEmpireName(),
                }, actorId: kingdom.Ruler.Key, kingdomId: kingdom.Key, prepend: true);
            }
            else
            {
                this.RecordHistory(EmpireHistoryType.new_empire_history_west, new Dictionary<string, string>()
                {
                    ["actor"] = kingdom.Ruler?.FullName()??"",
                    ["place"] = kingdom.Capital.GetCityName(),
                    ["name"] = GetEmpireName(),
                }, actorId: kingdom.Ruler.Key, kingdomId: kingdom.Key, prepend: true);
            }
            kingdom.Ruler?.GetSpecificClan()?.RecordHistoryEmpire(this, CoreKingdom.Capital);

        } catch (Exception e)
        {
            LogService.LogError($"继承帝国信息失败: {e}");
        }

        kingdom.Name = this.data.name;
        EmpireWorld.Current.zone_calculator.dirtyAndClear();
    }
#endif
    public bool CanSetTitleToPreviousEmperor()
    {
        if (this.data.history.Count > 0)
        {
            foreach(EmpireCraftHistory cHistory in this.data.history)
            {
                if (cHistory is { emperor: not null } && cHistory.emperor != "" && !EmpireWorld.Current.PersonByKey(cHistory.id)!.Alive)
                {
                    return true;
                }
            }
        }
        return false;
    }

    public bool IsAllowToMakeYearName ()
    {
        return !NoDynasty && this.data.has_year_name;
    }
    public bool HasYearName()
    {
        return !NoDynasty && !string.IsNullOrEmpty(this.data.year_name);
    }

#if EC_PENDING
    public void create_year_name()
    {
        this.data.year_name = YearNameHelper.generateName();
        this.data.newEmperor_timestamp = GameClock.Now;
        this.data.year_name_timestamp = -1d;
    }
#endif

    // ───── 在位中途改元(用户 2026-11-12："改年号的决议为啥一直是灰色的"——那条决议原先要帝国没有年号才能发起，
    // 而新君一即位就定了年号，所以永远用不了；用户定："加个5年年限") ─────
    // 距上次定年号(即位，或上回改元)满这么多年才能再改
    public const int YearNameIntervalYears = 5;

    // 眼下这个年号从哪天起算：在位中途改过元的从改元那天，否则从即位那天
    public double YearNameSince =>
        data.year_name_timestamp >= 0d && data.year_name_timestamp >= data.newEmperor_timestamp ? data.year_name_timestamp : data.newEmperor_timestamp;

    // 年号纪年(某某几年)：从这个年号起算。在位几年另看 GetEmperorYear
    public int GetYearNameYear() => GameClock.YearsSince(YearNameSince) + 1;

    // 还要等几年才能再改元(0 = 眼下就能改)
    public int YearsUntilYearName()
    {
        if (!HasYearName()) return 0;
        double since = YearNameSince;
        if (since < 0d || EmpireWorld.Current == null || GameClock.Now < since) return 0;
        return Math.Max(0, YearNameIntervalYears - GameClock.YearsSince(since));
    }

    public bool CanChangeYearName() => IsAllowToMakeYearName() && YearsUntilYearName() == 0;

    // 改元：只换年号、年号纪年从头数。即位时间不动——在位几年、各处按"新君即位多久"算的事都不受影响
#if EC_PENDING
    public void ChangeYearName()
    {
        string before = data.year_name;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            data.year_name = YearNameHelper.generateName();
            if (data.year_name != before) break;
        }
        data.year_name_timestamp = GameClock.Now;
        if (data.currentHistory != null) data.currentHistory.year_name = data.year_name;
    }

    public bool AddCabinetMember(Person actor)
    {
        if (actor == null || !actor.CanServeOffice(CoreKingdom)) return false;
        if (!actor.HasOfficeIdentity()) return false;
        OfficeIdentity identity = actor.GetIdentity();
        if (identity.IsCabinet()) return false;
        if (data.CabinetMembers.Contains(actor.Key)) return false;
        identity.EnterCabinet();
        data.CabinetMembers.Add(actor.Key);
        actor.RecordPersonalHistory(LM.Get("personal_history_joined_cabinet"));
        return true;
    }

    public bool SetCabinetLeader(Person actor)
    {
        if (actor == null || !actor.CanServeOffice(CoreKingdom)) return false;
        if (!actor.HasOfficeIdentity()) return false;
        if (GetCabinetLeader()?.Key == actor.Key) return false;
        OfficeIdentity identity = actor.GetIdentity();
        bool wasCabinetMember = data.CabinetMembers.Contains(actor.Key);
        RemoveCabinetMember(actor);
        data.CabinetMembers.Insert(0, actor.Key);
        identity.EnterCabinet();
        if (!wasCabinetMember)
        {
            actor.RecordPersonalHistory(LM.Get("personal_history_joined_cabinet"));
        }
        actor.RecordPersonalHistory(LM.Get("personal_history_became_cabinet_leader"));
        return true;
    }

    public Person GetCabinetLeader()
    {
        return GetCabinetMembers().FirstOrDefault();
    }

    public List<Person> GetCabinetMembers()
    {
        List<long> validIds = new List<long>();
        List<Person> members = new List<Person>();
        foreach (long actorId in data.CabinetMembers ?? new List<long>())
        {
            Person actor = EmpireWorld.Current.PersonByKey(actorId);
            if (actor == null || !actor.CanServeOffice(CoreKingdom))
            {
                actor?.GetIdentity()?.ExitCabinet();
                continue;
            }
            if (validIds.Contains(actorId)) continue;
            validIds.Add(actorId);
            members.Add(actor);
        }
        if (data.CabinetMembers == null || !data.CabinetMembers.SequenceEqual(validIds))
            data.CabinetMembers = validIds;
        return members;
    }

    public bool RemoveCabinetMember(Person actor)
    {
        if (actor.HasOfficeIdentity())
        {
            OfficeIdentity identity = actor.GetIdentity();
            identity.ExitCabinet();
        }
        data.CabinetMembers.Remove(actor.Key);
        return true;
    }
#endif

    // 本朝都城相对 v(祖上帝国所在地)的方位，冠在国号前。
    // 以前 |dx| == |dy| 一律冠"后"，而两者都是 0 也算相等——祖上帝国的记录没记位置、或帝国刚 new 出来
    // _capitalCenter 还是默认值(0,0,0)时就是 0 对 0，于是剧本建国、宗室复辟成片冠"后"，
    // 冒出"后罗马帝国""后清朝"(玩家 2026-11-07"能不能把这个后字去掉，改成东西也行")。
    // 改为与 RealmNameRules.Direction4 同一套算法：算得出方位就用方位，两点重合(含位置未知)不冠前缀，不再退回"后"。
    // 真正该冠"后"的同号同都由 RealmNamingSystem 自己判(见 ReusePrefix、RestorationPrefix)
#if EC_PENDING
    private string GetDir(Vector2 v)
    {
        return RealmNameRules.Direction4(v.x, v.y, _capitalCenter.x, _capitalCenter.y) ?? "";
    }
#endif

    public bool IsRoyalBeenChanged()
    {
        return data.original_royal_been_changed;
    }

#if EC_PENDING
    public void SetEmpireName(string name, bool preserveEmpireType = false)
    {
        var core = CoreKingdom;
        if (core == null) return;
        name = name?.Trim() ?? "";
        if (!preserveEmpireType || string.IsNullOrWhiteSpace(data.empire_type_key))
            data.empire_type_key = OverallHelperFunc.ResolveEmpireTypeKey(core.GetRegime(), core);
        string typeKey = data.empire_type_key;
        string typeName = string.IsNullOrWhiteSpace(typeKey) ? "" : LM.Get(typeKey);
        if (string.IsNullOrWhiteSpace(typeName) || string.Equals(typeName, typeKey, StringComparison.Ordinal))
            typeName = "";
        if (OverallHelperFunc.TryExtractEnglishPrefixedCountryName(name, out string extractedName))
            name = extractedName;
        else
            name = OverallHelperFunc.StripLocalizedTypeSuffix(name, typeName);
        string localizedPrefix = OverallHelperFunc.LocalizeDirectPrefix(data);
        // 西方封建帝国的第二、第三代：不用方位前缀，改称"某某第二帝国"；教宗授的"神圣"不是方位，照旧保留
        if (ShowsWesternOrdinal())
        {
            name = OverallHelperFunc.StripLocalizedTypeSuffix(name, GetOrdinalText(data.western_ordinal) + typeName);
            typeName = GetOrdinalText(data.western_ordinal) + typeName;
            if (!OverallHelperFunc.IsHolyPrefix(data)) localizedPrefix = "";
        }
        if (!string.IsNullOrWhiteSpace(localizedPrefix))
        {
            string[] separators = { ModClass.NARROW_SPACE + ModClass.NARROW_SPACE, ModClass.NARROW_SPACE, " " };
            foreach (string separator in separators)
            {
                string prefix = localizedPrefix + separator;
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                name = name.Substring(prefix.Length).Trim();
                break;
            }
        }
        data.core_name = name;
        data.name = OverallHelperFunc.FormatEmpireFullName(name, typeName, localizedPrefix, GetNamingCulture()) +
                    OverallHelperFunc.SurnameSuffix(data);
        if (core.Record != null) core.SetKingdomCoreName(name, data.name);
        if (data.currentHistory != null && data.currentHistory.id == Emperor?.Key)
            data.currentHistory.empire_full_name = GetEmpireFullName();
    }
#endif

    // 帝国换了核心王国(选帝换家族、储君带着封国入继、中央丢光城池就地改立)：国号照旧。
    // 国号存在核心王国的核心名上，每月 SyncKingdomStatus 按核心王国的名字重设国号；不先把帝国的名字写到新核心上，
    // 下个月国号就成了新核心自己的法理名(西方选帝制每换一家就改一次国号)
#if EC_PENDING
    public void KeepNameOnNewCore()
    {
        if (CoreKingdom?.Record == null || CoreKingdom.IsGone() || string.IsNullOrWhiteSpace(data?.core_name)) return;
        CoreKingdom.GetOrCreate().isEmpire = true;
        SetEmpireName(data.core_name, preserveEmpireType: true);
    }
#endif

    // 西方封建帝国称"第几帝国"；以天下之名(普世之名)为号时不排代数(见 RealmNamingSystem)
#if EC_PENDING
    public bool ShowsWesternOrdinal() =>
        data != null && data.western_ordinal >= 2 && data.name_source != RealmNamingSystem.SourceTianxia &&
        CoreKingdom?.GetRegime()?.type == RegimeType.Feudalism;
#endif

    private static string GetOrdinalText(int ordinal)
    {
        string key = $"empire_ordinal_{ordinal}";
        string text = LM.Get(key);
        return string.IsNullOrWhiteSpace(text) || text == key
            ? string.Format(LM.Get("empire_ordinal_generic"), ordinal)
            : text;
    }

    // 建立西方封建帝国时定代数：之前已经灭亡的同名封建帝国有几个，这就是第几+1代。
    // 只算已经灭亡的，所以"核心王国灭亡后由皇族王国接续"这种延续不会被当成新的一代。
#if EC_PENDING
    private void AssignWesternOrdinal(string coreName)
    {
        if (CoreKingdom?.GetRegime()?.type != RegimeType.Feudalism || string.IsNullOrWhiteSpace(coreName)) return;
        if (!ModClass.FEUDAL_EMPIRE_LINEAGE.TryGetValue(coreName, out List<long> lineage))
        {
            lineage = new List<long>();
            ModClass.FEUDAL_EMPIRE_LINEAGE[coreName] = lineage;
        }
        int fallen = lineage.Count(id => id != data.id && ModClass.EMPIRE_MANAGER.Get(id) == null);
        if (!lineage.Contains(data.id)) lineage.Add(data.id);
        data.western_ordinal = fallen + 1;
        if (data.western_ordinal >= 2) data.directPre = "";
    }

    public void CheckDissolve(Realm mainKingdom)
    {
        // 核心王国没了(比如最后一座城被叛军攻下)：先把它移出成员，再从剩下的成员里找皇族当国王的
        // 王国来延续帝国，找不到才解散。之前这里先无条件解散，解散会清空成员列表，导致后面永远找
        // 不到皇族王国，只要核心王国一亡，帝国就整个没了。
        // 现代国家(共和国)没有皇族：省州长官多是考选的，按皇族找接续者总是落空，首都一失整个国家就解散，
        // 各省散成一地"某某自治军"。现代国家的中央王国真没了(城都丢光)时，改由状态修复从剩下的成员里另选中央
        // (同 checkActive 换核心)；神力移除帝国等核心王国还在的情形照旧解散
        bool modernState = !ModClass.IS_CLEAR && mainKingdom != null && (mainKingdom.Cities?.Count ?? 0) == 0 &&
                           (mainKingdom.GetRegime()?.type == RegimeType.Modern || RepublicSystem.IsRepublic(this));
        if (mainKingdom != null)
        {
            this.kingdoms_hashset.Remove(mainKingdom);
            mainKingdom.EmpireLeave(false);
            recalculate();
        }
        Realm heirEmpire = null;
        if (EmpireSpecificClan != null)
        {
            foreach (Realm kingdom in kingdoms_list)
            {
                // 接续的皇族王国必须还有城市（有都城），否则新帝国一建立就没有领土
                if (kingdom == null || kingdom.IsGone() || kingdom == mainKingdom || kingdom.Ruler == null ||
                    !kingdom.HasCapital() || kingdom.Cities.Count <= 0) continue;
                if (kingdom.Ruler.HasSpecificClan())
                    if (SpecificClanManager.SameLineage(kingdom.Ruler.GetSpecificClan(), EmpireSpecificClan))
                    {
                        if (heirEmpire == null || heirEmpire.IsGone() ||
                            kingdom.countTotalWarriors() > heirEmpire.countTotalWarriors())
                        {
                            heirEmpire = kingdom;
                        }
                    }
            }
        }
        if (heirEmpire == null && modernState && TryRepairState("modern core kingdom lost") &&
            CoreKingdom != null && CoreKingdom != mainKingdom && !CoreKingdom.IsGone())
        {
            LogService.LogInfo($"现代国家的中央王国灭亡，由{CoreKingdom.GetKingdomName()}接任中央");
            return;
        }
        if (heirEmpire == null)
        {
            ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
            LogService.LogInfo("核心王国灭亡且没有皇族王国可以延续，解散帝国");
            return;
        }
        LogService.LogInfo("核心王国灭亡，由皇族王国" + heirEmpire.GetKingdomName() + "延续帝国");
        if (!ReplaceEmpire(heirEmpire))
        {
            // 接续失败时不能让帝国停留在"核心已移出成员列表"的状态：否则状态修复会重新选出核心、
            // 随后又被判定核心缺失，陷入每帧 接续→修复 的死循环。
            LogService.LogWarning($"皇族王国 {heirEmpire.GetKingdomName()} 无法接续帝国，解散帝国");
            ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
        }
    }

    public Realm GetMostPowerfulKingdom()
    {
        Realm kingdom = null;
        foreach(Realm k in kingdoms_hashset)
        {
            if (kingdom == null)
            {
                kingdom = k;
            }
            if (k.countTotalWarriors() >= kingdom.countTotalWarriors())
            {
                kingdom = k;
            }
        }
        return kingdom;
    }
#endif

    // 返回是否成功由 newKingdom 接续为新帝国（成功时本帝国会被解散）
    // 选帝制(神圣罗马式)：当选的诸侯带着自家领国成为帝国中央，帝都随之搬迁；原中央降为普通诸侯国、仍归原皇族。
    // 帝国本身(国号、历史、帝国核心)不变，只换中央与皇族(同中央丢光城池时就地改立中央，见 checkActive)。
    // keepName：国号随帝国留下(选帝制)；不传的(华夏原始政体推举共主，见 ShanrangSystem)照旧随新的共主之部改称。
    // settledContest：分裂进程的争位之战得胜、争位者的国接任中央(见 SchismSystem，用户 2026-10-21 选"争位内战代替集体出走")——
    // 争位有它自己的结局(随攻方的割据成员复奉号令、帝国仍在)，换了家族也不再走篡位那套宗室成员出走开战、成员离去
    // (同逼宫废立、太后废立)；扣正统、改朝换代的记录照旧
#if EC_PENDING
    public bool TransferCoreTo(Realm newCore, bool keepName = false, bool settledContest = false)
    {
        Realm old = CoreKingdom;
        if (newCore == null || newCore.IsGone() || newCore == old || !kingdoms_hashset.Contains(newCore) ||
            !newCore.HasCapital() || newCore.Ruler == null) return false;
        LeaderSelectMethod method = old?.GetRegime()?.GetLeaderSelectMethod() ?? LeaderSelectMethod.Vote;
        if (old != null && !old.IsGone())
        {
            old.GetOrCreate().isEmpire = false;
            old.GetRegime()?.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            old.SetLevel(4);
        }
        newCore.GetOrCreate().isEmpire = true;
        newCore.GetRegime()?.SetLeaderSelectMethod(method);
        newCore.ai.setTask("do_mod_empire_beh");
        newCore.SetLevel(0);
        string imperial = InstitutionSystem.GetCustomsCulture(this);
        CoreKingdom = newCore;
        SyncCoreKingdomReference();
        AlignCoreCulture(imperial);
        data.centerOffice?.Init(CoreKingdom);
        recalculate();
        // 先定国号再登基：即位的史书、世界提示用的是照旧的国号
        if (keepName) KeepNameOnNewCore();
        bool completing = _completingMinisterUsurpation;
        if (settledContest) _completingMinisterUsurpation = true;
        try { NewEmperor(newCore.Ruler); }
        finally { _completingMinisterUsurpation = completing; }
        return true;
    }
#endif

    // 换了中央(原中央丢光城池、选帝、争位……)：新中央的国家文化不是本帝国这一文化圈的，改从帝国的文化(imperial：换中央之前取下的)——
    // 帝国还是那个帝国，不因接任中央的成员国(外族的都护府、改从游牧的成员)换文化、换政体(用户 2026-10-27："应该根据国家文化反向影响皇帝个人")；
    // 同一文化圈的(选帝制下各诸侯)不动；征服王朝另有统治文化之分，不动
#if EC_PENDING
    private void AlignCoreCulture(string imperial)
    {
        Realm core = CoreKingdom;
        if (core?.Record == null || core.IsGone() || !CultureService.IsValidCulture(imperial) ||
            CompositeEmpireService.IsComposite(this)) return;
        string own = CultureService.GetRealmCulture(core);
        if (CultureService.IsValidCulture(own) &&
            (string.Equals(own, imperial, StringComparison.Ordinal) ||
             InstitutionSystem.GetCultureLine(own) == InstitutionSystem.GetCultureLine(imperial))) return;
        CultureService.SetRealmCulture(core, imperial, updatePoliticalSystem: false, allowLegacyInstitutions: false);
        InstitutionSystem.ResyncCulture(this);
        CultureService.AssimilateRuler(core, core.Ruler);
        LogService.LogInfo($"[EmpireCraft][帝国文化] {GetEmpireName()} 新中央 {core.GetKingdomName()} {own} → {imperial}，改从帝国之俗");
    }

    public bool ReplaceEmpire(Realm newKingdom)
    {
        if (newKingdom == null || newKingdom.IsGone() || newKingdom.Ruler == null || !newKingdom.HasCapital())
            return false;
        Empire newEmpire = ModClass.EMPIRE_MANAGER.NewEmpire(newKingdom, replacingEmpire: this);
        if (newEmpire == null)
        {
            return false;
        }
        newEmpire.data.history.InsertRange(0, data.history);
        // 同一帝国的延续：王号序数的即位记录一并接过去
        newEmpire.data.regnal_records.InsertRange(0, data.regnal_records ?? new List<RegnalRecord>());
        RegnalNameService.OnEmperorCrowned(newEmpire, newKingdom.Ruler, recompute: true);
        newEmpire.SetEmpireName(EmpireCraft.Core.ColonySystem.SovereignName(newKingdom));
        newKingdom.GetOrCreate().isEmpire = true;
        newEmpire.data.Mandate = data.Mandate - 50;
        newEmpire.data.directPre = "";
        if (newKingdom.Capital.HasKingdomName()) 
        {
            newEmpire.SetEmpireName(newKingdom.Capital.SelectKingdomName());
        }

        if (newKingdom.Ruler.HasSpecificClan())
        {
            if (newKingdom.Ruler.GetSpecificClan().HasHistoryEmpire())
            {
                var historyRecord = newKingdom.Ruler.GetSpecificClan().GetHistoryEmpire();
                newEmpire.data.directPre = newEmpire.GetDir(historyRecord.pos);
                string empireName = historyRecord.name;
                newEmpire.SetEmpireName(empireName);
            }
        }
        KingdomTitle newMainTitle = newKingdom.GetMainTitle();
        if (!string.IsNullOrWhiteSpace(newMainTitle?.data?.name))
        {
            newEmpire.SetEmpireName(newMainTitle.data.name);
        }

        if (newKingdom.Ruler.HasSpecificClan())
        {
            if (SpecificClanManager.SameLineage(newKingdom.Ruler.GetSpecificClan(), EmpireSpecificClan))
            {
                // 华夏中兴沿用旧号，原朝随即解散、不并存，不加方位(见 RealmNamingSystem.Founding)
                newEmpire.data.directPre = RealmNamingSystem.Enabled && RealmNamingSystem.IsHuaxia(newKingdom)
                    ? "" : newEmpire.GetDir(this._empireCenter);
                newEmpire.SetEmpireName(GetEmpireName());
                // 皇族王国延续、中兴(南宋式)：王朝积弊、六弊减半，王朝年岁从头算(改朝换代三档之"减半"，用户 2026-10-26)。
                // 以前新帝国从零算，京城一丢积弊一笔勾销
                if (!IsRepublicState)
                    DynasticCycleSystem.Inherit(newEmpire, DynasticCycleSystem.Capture(this), DynasticCycleSystem.Transition.Halve);
            }
        }
        if (newKingdom.Ruler.HasSpecificClan())
        {
            newKingdom.Ruler.GetSpecificClan().RecordHistoryEmpire(this, newEmpire.CoreKingdom.Capital);
            newEmpire.EmpireClan = newKingdom.getKingClan();
            newEmpire.data.empire_specific_clan = newKingdom.Ruler.GetSpecificClan().id;
        }
        else
        {
            newKingdom.Ruler.CheckSpecificClan();
            newEmpire.EmpireClan = newKingdom.getKingClan();
            newKingdom.Ruler.GetSpecificClan().RecordHistoryEmpire(this, newEmpire.CoreKingdom.Capital);
            newEmpire.data.empire_specific_clan = newKingdom.Ruler.GetSpecificClan().id;
        }
        TranslateHelper.LogministerAqcuireEmpire(newKingdom.Ruler, newEmpire);
        foreach (Realm kingdom in kingdoms_hashset.Where(kingdom => EmpireMembershipService.BelongsTo(this, kingdom)).ToList())
        {
            newEmpire.kingdoms_hashset.Add(kingdom);
            kingdom.EmpireJoin(newEmpire);
            newEmpire.data.timestamp_member_joined = GameClock.Now;
            
        }
        newEmpire.create_year_name();
        newEmpire.recalculate();
        if (data.has_year_name)
        {
            TranslateHelper.LogNewEmperor(newKingdom.Ruler, newKingdom.Capital, newEmpire.data.year_name);
        }
        else
        {
            TranslateHelper.LogNewEmperorWest(newKingdom.Ruler, newKingdom.Capital);
        }
        
        newKingdom.SetKingdomCoreName(newEmpire.GetEmpireName(), newEmpire.data.name);
        ModClass.EMPIRE_MANAGER.dissolveEmpire(this, continued: true);
        return true;
    }
    public sealed override void setDefaultValues()
    {
        base.setDefaultValues();
    }
    public override int countTotalMoney()
    {
        // Base statistics dispatch through our safe getUnits; kingdom counters bypass it.
        return base.countTotalMoney();
    }
    public override int countHappyUnits()
    {
        return base.countHappyUnits();
    }
    public override int countSick()
    {
        return base.countSick();
    }
    public override int countHungry()
    {
        return base.countHungry();
    }
    public override int countStarving()
    {
        return base.countStarving();
    }
    public override int countChildren()
    {
        return base.countChildren();
    }
    public override int countAdults()
    {
        return base.countAdults();
    }
    public override int countHomeless()
    {
        return base.countHomeless();
    }
    public override IEnumerable<Family> getFamilies()
    {
        List<Realm> tKingdoms = this.kingdoms_list;
        int num;
        for (int i = 0; i < tKingdoms.Count; i = num + 1)
        {
            Realm tKingdom = tKingdoms[i];
            foreach (Family tFamily in tKingdom.getFamilies())
            {
                yield return tFamily;
            }
            IEnumerator<Family> enumerator = null;
            num = i;
        }
        yield break;
    }

    public override bool hasFamilies()
    {
        List<Realm> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            if (tKingdoms[i].hasFamilies())
            {
                return true;
            }
        }
        return false;
    }
#endif

    // Token: 0x0600111A RID: 4378 RVA: 0x000C753C File Offset: 0x000C573C
#if EC_PENDING
    public override int countMales()
    {
        return base.countMales();
    }
#endif

    // Token: 0x0600111B RID: 4379 RVA: 0x000C7578 File Offset: 0x000C5778
#if EC_PENDING
    public override int countFemales()
    {
        return base.countFemales();
    }
#endif

    // Token: 0x0600111C RID: 4380 RVA: 0x000C75B4 File Offset: 0x000C57B4
#if EC_PENDING
    public override int countHoused()
    {
        return base.countHoused();
    }
    public override ColorLibrary getColorLibrary()
    {
        return AssetManager.kingdom_colors_library;
    }
#endif

    // 颜色编号越界(原版 updateColor 记的是颜色在色库里的位置，给的颜色不在色库里就记成 -1)：原版取色下标越界，
    // 附庸独立、同步帝国颜色等处一路报错(玩家日志 2026-10-16)。国家那边见 VanillaGuardPatch，这里是帝国：
    // 越界时用核心王国的颜色，并把编号修回色库里
#if EC_PENDING
    public override ColorAsset getColor()
    {
        if (_cached_color != null) return _cached_color;
        var colors = getColorLibrary()?.list;
        if (data == null || colors == null || colors.Count == 0) return null;
        if (data.color_id >= 0 && data.color_id < colors.Count) return base.getColor();
        Realm core = CoreKingdom;
        ColorAsset coreColor = core?.Record != null && !core.IsGone() ? core.getColor() : null;
        int index = coreColor != null ? colors.IndexOf(coreColor) : -1;
        if (index < 0 && coreColor != null)
        {
            _cached_color = coreColor;
            return coreColor;
        }
        if (index < 0) index = global::System.Math.Abs(data.color_id) % colors.Count;
        LogService.LogWarning($"[EmpireCraft] 帝国 {data.name}({data.id}) 的颜色编号 {data.color_id} 超出颜色库(共 {colors.Count} 种)，改用第 {index} 种");
        data.setColorID(index);
        if (data.original_color_id < 0 || data.original_color_id >= colors.Count) data.original_color_id = index;
        return base.getColor();
    }

    public override void generateBanner()
    {
        Sprite[] tBgs = EmpireWorld.Current.alliances.getBackgroundsList();
        this.data.banner_background_id = Randy.randomInt(0, tBgs.Length);
        Sprite[] tIcons = EmpireWorld.Current.alliances.getIconsList();
        this.data.banner_icon_id = Randy.randomInt(0, tIcons.Length);
    }

    public void addFounder(Realm pKingdom)
    {
        data.founder_kingdom_name = pKingdom.Name;
        data.founder_kingdom_id = pKingdom.Key;
        EmpireData empireData = data;
        Person king = pKingdom.Ruler;
        empireData.founder_actor_name = king?.FullName();
        empireData.founder_actor_id = king?.Key ?? -1L;
        if (empireData.dynasty_founder_actor_id <= 0)
        {
            empireData.dynasty_founder_actor_id = empireData.founder_actor_id;
        }
        RepairFoundingEmperorMarker();
        join(pKingdom, true, true);
        RulerTraitSystem.OnCrowned(this, king);
    }

    public bool IsFoundingEmperorHistory(EmpireCraftHistory history)
    {
        if (history == null) return false;
        long dynastyFounderId = ResolveDynastyFounderActorId();
        return history.is_first ||
               data?.founder_actor_id > 0 && history.id == data.founder_actor_id ||
               dynastyFounderId > 0 && history.id == dynastyFounderId;
    }

    public void RepairFoundingEmperorMarker()
    {
        if (data == null) return;
        long dynastyFounderId = ResolveDynastyFounderActorId();
        if (data.currentHistory != null &&
            (data.currentHistory.id == data.founder_actor_id || data.currentHistory.id == dynastyFounderId))
        {
            data.currentHistory.is_first = true;
        }
        if (data.history == null) return;
        foreach (EmpireCraftHistory history in data.history)
        {
            if (history != null &&
                (history.id == data.founder_actor_id || history.id == dynastyFounderId))
            {
                history.is_first = true;
            }
        }
    }

    private long ResolveDynastyFounderActorId()
    {
        if (data == null) return -1L;
        if (data.dynasty_founder_actor_id > 0) return data.dynasty_founder_actor_id;

        string dynastyName = data.currentHistory?.dynasty_name ?? GetEmpireName();
        string royalSurname = data.currentHistory?.royal_surname ?? EmpireSpecificClan?.name ?? "";
        bool MatchesCurrentDynasty(EmpireCraftHistory history)
        {
            if (history == null) return false;
            bool dynastyMatches = string.IsNullOrWhiteSpace(dynastyName) || history.dynasty_name == dynastyName;
            bool surnameMatches = string.IsNullOrWhiteSpace(royalSurname) || history.royal_surname == royalSurname;
            return dynastyMatches && surnameMatches;
        }

        EmpireCraftHistory founder = data.history?.LastOrDefault(history =>
            history?.is_first == true && MatchesCurrentDynasty(history));
        founder ??= data.history?.FirstOrDefault(MatchesCurrentDynasty);
        if (founder == null && MatchesCurrentDynasty(data.currentHistory))
        {
            founder = data.currentHistory;
        }

        data.dynasty_founder_actor_id = founder?.id > 0
            ? founder.id
            : data.founder_actor_id > 0
                ? data.founder_actor_id
                : Emperor?.Key ?? -1L;
        return data.dynasty_founder_actor_id;
    }

    public void update()
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom)) return;
        if (data == null || EmpireWorld.Current == null || _updatingHonoraryPeerages) return;
        Realm coreKingdom = CoreKingdom;
        if (coreKingdom == null || coreKingdom.IsGone()) return;
        AdministrationReorganizationSystem.Resume(this);
        RepairUnregisteredEmperor();
        Regime regime = coreKingdom.GetRegime();
        CompositeEmpireService.Update(this);
        // 朝廷用了中原制度的外族王朝改汉名国号(复合帝国每年结算时也查；不是复合帝国的只在这里查，见 CompositeEmpireService)
        CompositeEmpireService.TryAdoptInstitutionalCultureName(this);
        // 京师失守、朝廷另立都城：南渡偏安(要在积弊、民变结算之前，见 SouthernRetreatSystem)
        SouthernRetreatSystem.Check(this);
        ConstitutionalEconomySystem.Update(this);
        // 土崩瓦解收束时可能就此灭亡，后面的更新不再跑
        if (IsArchived()) return;
        InstitutionSystem.Update(this);
        // 科技革新：与朝议、改革线并行的第三条线(用户 2026-11-11 "还有派系会支持的科技革新""也可以并行"，见 TechDriveSystem)
        TechDriveSystem.Update(this);
        // 上面的制度更新可能在本帧直接完成改制。后续行为必须读取新政体，
        // 否则刚进入现代的国家还会按旧郡县制配置再册封一次。
        regime = coreKingdom.GetRegime();
        if (regime?.type == RegimeType.LvLing)
        {
            // 议会存续期间由议会选出的总理大臣执政，不再产生权臣
            if (ParliamentSystem.HasParliament(this) || ConstitutionalSuccessionSystem.IsProtected(this))
            {
                if (data.powerful_minister_id > 0) ClearPowerfulMinister();
            }
            else UpdatePowerfulMinister(regime);
            ProcessTerritorialAcquisitionEnfeoff();
        }
        else
        {
            // 西方中央集权君主制、神权国家同郡县制一样，直辖的法理要划成辖区/教区
            // 阿拉伯：打下的法理设行省派埃米尔(见 EmirateSystem)
            // 元首制：核心打下的城设行省
            // 哪些政体划分打下的法理读政体配置(partition_conquests)：封建、周制、阿拉伯、幕府……
            // 穿越政体：共同纲领以后打下的法理划成大区(见 TransmigrantSystem.Partitions)
            if (IsWesternCentralized() || RegimeManager.GetTemplate(regime?.type)?.partition_conquests == true ||
                IsPrincipate() || TransmigrantSystem.Partitions(this))
                ProcessTerritorialAcquisitionEnfeoff();
            // 游牧施行行省制度后，打下的法理设行省(那一步里也补分兀鲁思)
            else if (IsNomadProvincial()) ProcessTerritorialAcquisitionEnfeoff();
            // 游牧兀鲁思：大汗死时正在打仗没分成的，停战后补分(见 EnfeoffmentHelper.UlusPartition)
            else if (regime?.type == RegimeType.YouMu) EnfeoffmentHelper.ResumeSuccessionEnfeoff(this);
            // 汗庭直辖的法理每年至多一块分给成年宗王做兀鲁思(见 PartialEnfeoffment；以前打下的地永远留在汗庭直辖)
            if (regime?.type == RegimeType.YouMu) PartialEnfeoffment.TickUlus(this);
            // 兀鲁思渐行渐远：大汗弱时自主到顶的兀鲁思和平独立(见 NomadCycleSystem.UlusDrift)
            if (regime?.type == RegimeType.YouMu) NomadCycleSystem.UlusDrift(this);
            // 哈里发弱时埃米尔世袭坐大
            if (regime?.type == RegimeType.Arabic) EmirateSystem.Tick(this);
            // 幕府将军、苏丹以权臣执政(见 MilitaryRegency)；别的非律令政体不设权臣
            if (regime?.type is RegimeType.Bakufu or RegimeType.Arabic) MilitaryRegency.Maintain(this);
            else if (regime != null && data.powerful_minister_id > 0) ClearPowerfulMinister();
        }
        // 现代国家不再运行传统封爵、恩赏爵位或爵位承袭流程。
        if (regime?.type == RegimeType.Modern || regime?.enfeoff_virtual_only != true) return;
        bool legalPeeragesDue = data.last_legal_peerage_timestamp < 0 ||
            GameClock.MonthsSince(data.last_legal_peerage_timestamp) >= 1;
        bool honoraryPeeragesDue = regime.enable_auto_honorary_peerages &&
            (data.last_honorary_peerage_timestamp < 0 || GameClock.YearsSince(data.last_honorary_peerage_timestamp) >= 1);
        if (!legalPeeragesDue && !honoraryPeeragesDue) return;

        _updatingHonoraryPeerages = true;
        try
        {
            if (legalPeeragesDue)
            {
                data.last_legal_peerage_timestamp = GameClock.Now;
                ProcessLegalPeerageSuccession();
                if (regime.enable_auto_honorary_peerages) ProcessHonoraryPeerageInheritance(regime);
            }
            if (!honoraryPeeragesDue) return;
            data.last_honorary_peerage_timestamp = GameClock.Now;

            List<Person> candidates = getUnits()
                .Where(a => a != null && !a.IsGone() && !a.IsRuler() && !a.HasHonoraryPeerage())
                .ToList();
            foreach (string peerageKey in regime.virtual_honorary_peerages ?? new List<string>())
            {
                if (IsHonoraryPeerageReserved(peerageKey)) continue;
                Person candidate = GetHonoraryPeerageCandidate(candidates, peerageKey);
                if (candidate != null && candidate.GrantHonoraryPeerage(this, peerageKey)) return;
            }
        }
        finally
        {
            _updatingHonoraryPeerages = false;
        }
    }

    private Person GetHonoraryPeerageCandidate(IEnumerable<Person> candidates, string peerageKey)
    {
        bool IsCivilMerit(Person a) => !a.IsWarrior() && (a.GetOrCreate().officeIdentity?.TotalPerformance ?? 0) >= 500 && a.Renown >= 100;
        bool IsMilitaryMerit(Person a) => a.IsWarrior() && a.Renown >= 120;
        return peerageKey switch
        {
            "tang_honorary_anle_gong" => candidates.Select(actor => (actor, priority: GetAnlePeeragePriority(actor)))
                .Where(item => item.priority > 0).OrderByDescending(item => item.priority)
                .ThenBy(item => item.actor.GetPersonalIdentity()?.rank ?? int.MaxValue)
                .ThenBy(item => item.actor.Key).Select(item => item.actor).FirstOrDefault(),
            "tang_honorary_wenan_gong" or "tang_honorary_longxi_gong" => candidates.Where(IsCivilMerit).OrderByDescending(a => a.Renown).FirstOrDefault(),
            "tang_honorary_wujun_hou" => candidates.Where(a => IsMilitaryMerit(a) && a.Renown >= 250).OrderByDescending(a => a.Renown).FirstOrDefault(),
            "tang_honorary_zhongyong_hou" => candidates.Where(a => IsMilitaryMerit(a) && (a.GetOrCreate().officeIdentity?.TotalPerformance ?? 0) >= 150).OrderByDescending(a => a.Renown).FirstOrDefault(),
            "tang_honorary_huaide_hou" => candidates.Where(a => !a.IsWarrior() && (a.GetOrCreate().officeIdentity?.TotalPerformance ?? 0) >= 350).OrderByDescending(a => a.GetOrCreate().officeIdentity.TotalPerformance).FirstOrDefault(),
            _ => null
        };
    }

    public bool IsHonoraryPeerageReserved(string peerageKey)
    {
        return PeerageSuccessionRules.IsReserved(data.honorary_peerage_holders, peerageKey) ||
            getUnits().Any(actor => actor != null && !actor.IsGone() && actor.HasHonoraryPeerage(this) &&
                actor.GetOrCreate().honorary_peerage_key == peerageKey);
    }

    public int GetAnlePeeragePriority(Person actor)
    {
        if (actor == null || actor.IsGone()) return 0;
        return AnlePeerageRules.CandidatePriority(data.defeated_empire_houses, id, actor.Key,
            actor.GetSpecificClan()?.id ?? -1L, actor.Realm?.GetEmpire() == this, actor.IsRuler());
    }

    public void RememberDefeatedEmpireHouse(DefeatedEmpireHouse house)
    {
        if (house == null || house.empire_id <= 0 || house.empire_id == id) return;
        data.defeated_empire_houses ??= new List<DefeatedEmpireHouse>();
        if (data.defeated_empire_houses.Any(old => old != null && old.empire_id == house.empire_id &&
            old.emperor_id == house.emperor_id && old.royal_clan_id == house.royal_clan_id)) return;
        data.defeated_empire_houses.Add(new DefeatedEmpireHouse
        {
            empire_id = house.empire_id,
            emperor_id = house.emperor_id,
            royal_clan_id = house.royal_clan_id
        });
    }

    public void RememberHonoraryPeerageHolder(string peerageKey, Person holder)
    {
        holder.CheckSpecificClan(false);
        data.honorary_peerage_holders ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_identities ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_names ??= new Dictionary<string, string>();
        data.honorary_peerage_holders[peerageKey] = holder.Key;
        data.honorary_peerage_holder_identities[peerageKey] = holder.GetPersonalIdentity()?.id ?? -1L;
        data.honorary_peerage_holder_names[peerageKey] = holder.FullName();
    }
#endif

    private static PersonalClanIdentity ResolvePeerageIdentity(long actorId, long identityId)
    {
        PersonalClanIdentity identity = SpecificClanManager.getPerson(identityId);
        if (identity != null) return identity;
        if (SpecificClanManager._actorToPersonLookup.TryGetValue(actorId, out identity)) return identity;
        return EmpireWorld.Current.PersonByKey(actorId)?.GetPersonalIdentity();
    }

#if EC_PENDING
    private Person FindPeerageDescendant(PersonalClanIdentity predecessor, Func<Person, bool> isAvailable)
    {
        if (predecessor?._specificClan == null) return null;
        // 嗣子连虚拟族人一起按长幼排(纯净地图下多不在地图上；以前只认地图上的，支系承袭几乎总落空，爵位改由朝廷另封)：
        // 挑中虚拟族人才落成，落成后不合格的并回人口、往下再找
        var rejected = new HashSet<long>();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            PersonalClanIdentity found = PeerageSuccessionRules.FindDescendant(predecessor,
                person => SpecificClanManager.getChildren(person).Select(item => item.Item2)
                    .OrderBy(child => child.rank).ThenBy(child => child.id),
                person => person.specific_clan_id == predecessor.specific_clan_id && person.is_main && person.IsHeirPriority(),
                person => !rejected.Contains(person.id) && person.CanHeir(predecessor) &&
                          (!person.is_virtual && isAvailable(person._actor) ||
                           KinRoster.Virtual(person) && KinRoster.KingdomOf(person)?.GetEmpire() == this),
                person => person.id);
            if (found == null) return null;
            if (!found.is_virtual) return found._actor;
            Person realized = KinRoster.RealizeFirst(new[] { found }, isAvailable, 1);
            if (realized != null) return realized;
            rejected.Add(found.id);
        }
        return null;
    }

    private void ProcessHonoraryPeerageInheritance(Regime regime)
    {
        data.honorary_peerage_holders ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_identities ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_names ??= new Dictionary<string, string>();
        // Backfill legacy saves without letting duplicate grants overwrite the recorded lineage.
        foreach (var group in getUnits().Where(a => a != null && !a.IsGone() && a.HasHonoraryPeerage(this))
            .GroupBy(a => a.GetOrCreate().honorary_peerage_key).ToList())
        {
            if (!data.honorary_peerage_holders.TryGetValue(group.Key, out long registeredId))
            {
                Person first = group.OrderBy(actor => actor.Key).First();
                RememberHonoraryPeerageHolder(group.Key, first);
                registeredId = first.Key;
            }
            foreach (Person actor in group)
            {
                if (actor.Key == registeredId) RememberHonoraryPeerageHolder(group.Key, actor);
                else
                {
                    actor.GetOrCreate().honorary_peerage_key = "";
                    actor.GetOrCreate().honorary_peerage_empire_id = -1L;
                }
            }
        }

        foreach (string peerageKey in (regime.virtual_honorary_peerages ?? new List<string>()).ToList())
        {
            if (!data.honorary_peerage_holders.TryGetValue(peerageKey, out long holderId)) continue;
            Person holder = EmpireWorld.Current.PersonByKey(holderId);
            if (holder != null && !holder.IsGone()) continue;
            data.honorary_peerage_holder_identities.TryGetValue(peerageKey, out long identityId);
            PersonalClanIdentity predecessor = ResolvePeerageIdentity(holderId, identityId);
            Person heir = FindPeerageDescendant(predecessor, a => a != null && !a.IsGone() && !a.IsRuler() &&
                a.Realm?.GetEmpire() == this && !a.HasHonoraryPeerage());
            if (heir == null) continue;

            if (holder != null)
            {
                holder.GetOrCreate().honorary_peerage_key = "";
                holder.GetOrCreate().honorary_peerage_empire_id = -1L;
            }
            heir.GetOrCreate().honorary_peerage_key = peerageKey;
            heir.GetOrCreate().honorary_peerage_empire_id = data.id;
            data.honorary_peerage_holder_names.TryGetValue(peerageKey, out string predecessorName);
            RememberHonoraryPeerageHolder(peerageKey, heir);
            TranslateHelper.LogPeerageInherited(heir, this, LM.Get(peerageKey), predecessor,
                holderId, predecessorName ?? holder?.FullName());
        }
    }

    public bool TryRepairState(string reason = "")
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom)) return true;
        if (IsArchived() || data == null || EmpireWorld.Current == null)
        {
            return false;
        }

        kingdoms_hashset ??= new HashSet<Realm>();
        kingdoms_list ??= new List<Realm>();
        cities_list ??= new List<City>();
        given_Kingdoms ??= new List<Realm>();
        taken_Kingdoms ??= new List<Realm>();
        data.kingdoms ??= new List<long>();
        data.cities ??= new List<long>();
        data.given_Kingdoms ??= new List<long>();
        data.taken_Kingdoms ??= new List<long>();
        data.history_emperrors ??= new List<string>();
        data.PreviousYearsMoney ??= new List<int>();
        data.honorary_peerage_holders ??= new Dictionary<string, long>();

        bool repaired = false;
        HashSet<Realm> rebuiltKingdoms = new HashSet<Realm>();

        foreach (Realm kingdom in kingdoms_hashset)
        {
            if (EmpireMembershipService.BelongsTo(this, kingdom))
            {
                rebuiltKingdoms.Add(kingdom);
            }
        }

        foreach (Realm kingdom in kingdoms_list)
        {
            if (EmpireMembershipService.BelongsTo(this, kingdom))
            {
                rebuiltKingdoms.Add(kingdom);
            }
        }

        foreach (long kingdomId in data.kingdoms)
        {
            Realm kingdom = EmpireWorld.Current.RealmByKey(kingdomId);
            if (EmpireMembershipService.BelongsTo(this, kingdom))
            {
                rebuiltKingdoms.Add(kingdom);
            }
        }

        foreach (Realm kingdom in EmpireWorld.Current.LiveRealms)
        {
            if (kingdom == null || kingdom.IsGone()) continue;
            if (kingdom.GetEmpireID() == this.Key)
            {
                rebuiltKingdoms.Add(kingdom);
            }
        }

        if (!kingdoms_hashset.SetEquals(rebuiltKingdoms))
        {
            kingdoms_hashset = rebuiltKingdoms;
            repaired = true;
        }

        List<City> previousCities = cities_list;
        recalculate();

        if (previousCities.Count != cities_list.Count || previousCities.Except(cities_list).Any())
        {
            repaired = true;
        }

        Realm currentCore = CoreKingdom;
        bool coreInvalid = currentCore == null || currentCore.IsGone() || !kingdoms_hashset.Contains(currentCore) || currentCore.Cities.Count <= 0;
        bool coreChanged = false;
        if (coreInvalid)
        {
            long preferredCoreId = data.last_core_kingdom_id > 0 ? data.last_core_kingdom_id : data.empire;
            Realm candidate = kingdoms_list
                .Where(k => k != null && !k.IsGone() && k.Cities.Count > 0)
                .OrderByDescending(k => k.Key == preferredCoreId)
                .ThenByDescending(k => k == currentCore)
                .ThenByDescending(k => k.IsEmpire())
                .ThenByDescending(k => k.countTotalWarriors())
                .ThenByDescending(k => k.cities.Count)
                .FirstOrDefault();

            if (candidate != null)
            {
                coreChanged = candidate != currentCore;
                string imperial = coreChanged ? InstitutionSystem.GetCustomsCulture(this) : null;
                CoreKingdom = candidate;
                SyncCoreKingdomReference();
                if (coreChanged) AlignCoreCulture(imperial);
                // 原中央灭亡或丢光城池、就地改立中央：同一个帝国，国号不随新中央改
                if (coreChanged && currentCore != null) KeepNameOnNewCore();
                repaired = true;
            }
        }

        if (CoreKingdom == null || CoreKingdom.IsGone() || !kingdoms_hashset.Contains(CoreKingdom))
        {
            if (!string.IsNullOrEmpty(reason))
            {
                LogService.LogInfo($"帝国 {id} 修复失败: {reason}");
            }
            return false;
        }

        // Only the actual core kingdom may retain the empire-center marker. Older
        // saves and capital transfers could leave the previous core marked as a
        // second direct domain, which also kept its stale local office/type.
        foreach (Realm kingdom in kingdoms_list.ToList())
        {
            if (kingdom == null || kingdom.IsGone()) continue;
            bool shouldBeCore = kingdom == CoreKingdom;
            KingdomExtension.KingdomExtraData extraData = kingdom.GetOrCreate();
            bool markerChanged = extraData.isEmpire != shouldBeCore;
            if (markerChanged)
            {
                extraData.isEmpire = shouldBeCore;
                repaired = true;
            }

            KingdomType currentType = kingdom.GetKingdomType();
            bool staleCentralType = !shouldBeCore && currentType is KingdomType.LvLing_centre or
                KingdomType.Modern_centre or KingdomType.Origin_centre or KingdomType.YouMu_centre;
            if (markerChanged || staleCentralType)
            {
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
            }
        }

        // 中央官署只在缺失、残缺(登记的官职不在官职表里)、政体对不上或换了核心国时重建，完好的只重新挂到核心国上。
        // 以前每次修复都整套重建，而读档、存档都会经 EmpireManager.update → checkActive 走到这里：
        // 三公卿士、三公九卿、后宫每次存读档都换成一套新的空缺官职，在任者留着指向已删官职的"在任"标记，
        // 存档里中央官职从来都是空的，读档后朝廷一片空缺，要等帝国 AI 重新选官
        bool rebuild = data.centerOffice == null || coreChanged;
        if (data.centerOffice == null)
        {
            data.centerOffice = new CenterOffice();
            repaired = true;
        }

        try
        {
            // 判断完好要取核心国的政体，读档后头一回取会现建政体(还原派系、迁移诉求清单)：放在 try 里，
            // 出错照旧按重建兜底。以前它在 try 外，一出错就从 checkActive 一路抛出读档流程，后面的帝国核心、法理、宗族都读不进来
            if (!rebuild) rebuild = !data.centerOffice.IsIntact(CoreKingdom);
            if (rebuild) data.centerOffice.Init(CoreKingdom);
            else
            {
                data.centerOffice.SyncMetaObject(CoreKingdom);
                // 不重建时也要把派系占比对回核心国的派系(以前由每次重建顺带做，见 CenterOffice.SyncFactionRatios)
                data.centerOffice.SyncFactionRatios(CoreKingdom);
            }
        }
        catch (Exception e)
        {
            LogService.LogError($"中央官署初始化失败，正在重置: {e}");
            data.centerOffice = new CenterOffice();
            data.centerOffice.Init(CoreKingdom);
            repaired = true;
        }

        if (OriginalCapital == null || OriginalCapital.IsGone() || OriginalCapital.Realm != CoreKingdom)
        {
            OriginalCapital = CoreKingdom.Capital;
            repaired = true;
        }

        if (OriginalCapital != null && !OriginalCapital.IsGone())
        {
            _capitalCenter = OriginalCapital.city_center;
            data.original_capital = OriginalCapital.Key;
        }
        SyncCoreKingdomReference();

        if (EmpireClan == null || EmpireClan.IsGone())
        {
            EmpireClan = CoreKingdom.getKingClan();
            data.empire_clan = EmpireClan?.data?.id ?? -1L;
            repaired = true;
        }

        if (Religion == null || Religion.IsGone())
        {
            Religion = EmpireWorld.Current.religions.get(data.Religion);
        }

        given_Kingdoms = given_Kingdoms.Where(k => k != null && !k.IsGone()).Distinct().ToList();
        taken_Kingdoms = taken_Kingdoms.Where(k => k != null && !k.IsGone()).Distinct().ToList();

        if (string.IsNullOrWhiteSpace(data.name))
        {
            string empireName = EmpireCraft.Core.ColonySystem.SovereignName(CoreKingdom);
            KingdomTitle mainTitle = CoreKingdom.GetMainTitle();
            if (!string.IsNullOrWhiteSpace(mainTitle?.data?.name))
            {
                empireName = mainTitle.data.name;
            }
            SetEmpireName(empireName);
            repaired = true;
        }

        data.kingdoms = kingdoms_list.Where(k => k != null && !k.IsGone()).Select(k => k.Key).Distinct().ToList();
        data.cities = cities_list.Where(c => c != null && !c.IsGone()).Select(c => c.Key).Distinct().ToList();
        SyncCoreKingdomReference();

        if (repaired && !string.IsNullOrEmpty(reason))
        {
            LogService.LogInfo($"帝国 {id} 已重置并修复: {reason}");
        }

        // 成员国全部失去城市（只剩流散的人口/军队）时帝国已经没有领土，不算修复成功：
        // 否则 checkActive 失败后的兜底修复永远返回 true，帝国会以无领土的"幽灵"状态一直存在，
        // 铭牌上没有国名、人口和兵力停留在旧值。
        return kingdoms_hashset.Any(kingdom => kingdom != null && !kingdom.IsGone() && kingdom.Cities?.Count > 0);
    }

    public bool checkActive()
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom)) return true;
        if (IsArchived()) return false;
        if (!TryRepairState("checkActive precheck"))
        {
            return false;
        }
        bool tChanged = false;
        recalculate();
        List<Realm> tKingdoms = this.kingdoms_list;
        if (tKingdoms.Count<=0)
        {
            return false;
        }
        if (CoreKingdom != null)
        {
            if (CoreKingdom.Cities.Count <= 0)
            {
                var originalEmperor = Emperor;
                var originalHasHeir = CoreKingdom.HasHeir();
                Person originalHeir = originalHasHeir ? CoreKingdom.GetHeir() : null;
                Realm candidate = null;
                foreach (var k in tKingdoms)
                {
                    if (k == CoreKingdom) continue;
                    if (k.IsGone()) continue;
                    if (k.Cities.Count <= 0) continue;
                    if (k.GetRegime().GetLeaderSelectMethod() != LeaderSelectMethod.Succession)
                    {
                        candidate = k;
                        break;
                    }
                }
                if (candidate == null)
                {
                    int maxWarriors = -1;
                    foreach (var k in tKingdoms)
                    {
                        if (k == CoreKingdom) continue;
                        if (k.IsGone()) continue;
                        if (k.Cities.Count <= 0) continue;
                        int warriors = k.countTotalWarriors();
                        if (warriors >= maxWarriors)
                        {
                            maxWarriors = warriors;
                            candidate = k;
                        }
                    }
                }
                if (candidate != null)
                {
                    string imperial = InstitutionSystem.GetCustomsCulture(this);
                    CoreKingdom = candidate;
                    SyncCoreKingdomReference();
                    AlignCoreCulture(imperial);
                    data.centerOffice.Init(CoreKingdom);
                    KeepNameOnNewCore();
                    if (originalEmperor != null && !originalEmperor.IsGone())
                    {
                        if (CoreKingdom.Ruler != originalEmperor)
                        {
                            CoreKingdom.SetRuler(originalEmperor);
                        }
                        MoveToEmpireCapital(originalEmperor);
                    }
                    else
                    {
                        if (originalHeir != null && !originalHeir.IsGone())
                        {
                            if (CoreKingdom.Ruler != originalHeir)
                            {
                                CoreKingdom.SetRuler(originalHeir);
                            }
                            MoveToEmpireCapital(originalHeir);
                        }
                    }
                }
                else
                {
                    return false;
                }
            }
        }
        List<Realm> remove_tKingdoms = new List<Realm>();
        foreach( Realm k in tKingdoms )
        {
            if (k.IsGone()||k.GetEmpire()!=this)
            {
                remove_tKingdoms.Add(k);
                tChanged = true;
            }
        }
        foreach (Realm k in remove_tKingdoms)
        {
            if (!k.IsGone())
            {
                leave(k);
            }
            kingdoms_hashset.Remove(k);
        }
        if (tChanged)
        {
            recalculate();
            return TryRepairState("checkActive cleanup");
        }
        return kingdoms_hashset.Count >= 1;
    }
#endif

    public int GetCapitalLostTime()
    {
        return GameClock.YearsSince(data.center_loss_timestamp);
    }

    public bool IsCapitalLost()
    {
        return data.is_center_lost;
    }
    // Token: 0x06001125 RID: 4389 RVA: 0x000C77B4 File Offset: 0x000C59B4
#if EC_PENDING
    public void dissolve()
    {
        foreach (Realm kingdom in this.kingdoms_hashset)
        {
            if (EmpireMembershipService.BelongsTo(this, kingdom)) kingdom.EmpireLeave();
        }
        this.kingdoms_hashset.Clear();
        this.kingdoms_list.Clear();
        this.cities_list.Clear();
    }
#endif

    // Token: 0x06001126 RID: 4390 RVA: 0x000C7810 File Offset: 0x000C5A10
    public void RefreshCompatibilityMembership()
    {
        if (!AncientWarfareCompatibility.Loaded || AncientWarfareCompatibility.Owns(CoreKingdom)) return;
        // Filter simulation lists only. Keep saved membership so disabling AW does not erase it.
        kingdoms_list.RemoveAll(k => AncientWarfareCompatibility.Owns(k));
        cities_list.RemoveAll(c => AncientWarfareCompatibility.OwnsObject(c));
    }

#if EC_PENDING
    public void recalculate()
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom)) return;
        kingdoms_hashset.RemoveWhere(kingdom => !EmpireMembershipService.BelongsTo(this, kingdom));
        this.kingdoms_list.Clear();
        this.kingdoms_list.AddRange(this.kingdoms_hashset.Where(k => !AncientWarfareCompatibility.Owns(k)));
        cities_list = EmpireMembershipService.EnumerateCities(this).ToList();
        this.mergeWars();
    }
#endif

    // Token: 0x06001127 RID: 4391 RVA: 0x000C7834 File Offset: 0x000C5A34
#if EC_PENDING
    public bool canJoin(Realm pKingdom)
    {
        if (pKingdom == null || pKingdom.IsGone()) return false;
        if (AncientWarfareCompatibility.Owns(CoreKingdom) || AncientWarfareCompatibility.Owns(pKingdom)) return false;
        if (!CanAcceptVoluntarySubmission() || pKingdom.HasRebelledAgainst(this)) return false;
        // 神力放出的保护期内、自己离开不到十年的，不自愿回来(危急除外，见 MembershipCooldown)
        if (MembershipCooldown.BlocksVoluntary(pKingdom, this)) return false;
        // 已有封建宗主的国家不能再自愿加入帝国：要么先独立，要么被宗主放还(避免双重从属)
        if (FeudalVassalService.GetOverlord(pKingdom) != null) return false;
        // 殖民地只认自己的宗主国(见 ColonySystem)
        if (ColonySystem.IsColony(pKingdom)) return false;
        if (!ContainsKingdomCapitalInCore(pKingdom) || !MeetsSubmissionPowerThreshold(pKingdom)) return false;
        if (!pKingdom.isOpinionTowardsKingdomGood(CoreKingdom))
        {
            return false;
        }
        return true;
    }

    public bool CanAcceptVoluntarySubmission()
    {
        return !isRekt() && !IsArchived() && CoreKingdom != null && !CoreKingdom.IsGone() &&
               EmpireSubmissionRules.CanAcceptVoluntarySubmission(Mandate, CoreKingdom.GetMoney());
    }
#endif

    public bool ContainsKingdomCapitalInCore(Realm kingdom)
    {
        if (kingdom?.Capital == null || kingdom.Capital.IsGone()) return false;
        EmpireCore core = EmpireCoreManager.Get(this);
        return core != null && kingdom.Capital.GetEmpireCore() == core;
    }

#if EC_PENDING
    public bool MeetsTributaryPowerThreshold(Realm kingdom)
    {
        return kingdom != null && NationalPowerRules.IsWithinShare(kingdom.GetNationalPower(),
            GetNationalPower(), NationalPowerRules.TributaryMaximumShare);
    }

    public bool MeetsSubmissionPowerThreshold(Realm kingdom)
    {
        return kingdom != null && NationalPowerRules.IsWithinShare(kingdom.GetNationalPower(),
            GetNationalPower(), NationalPowerRules.SubmissionMaximumShare);
    }
    public override void save()
    {
        if (this.data == null)
        {
            return;
        }
        if (this.CoreKingdom == null) return;
        if (this.CoreKingdom.Record == null) return;
        recalculate();
        this.data.kingdoms = new List<long>();
        foreach (Realm tKingdom in this.kingdoms_hashset)
        {
            if (tKingdom!=null)
            {
                this.data.kingdoms.Add(tKingdom.Key);
            }
        }
        this.data.cities = new List<long>();
        foreach (City tCity in this.cities_list)
        {
            if (!tCity.IsGone())
            {
                this.data.cities.Add(tCity.Key);
            }
        }

        this.data.Religion = Religion?.id ?? -1L;
        foreach (var k in this.given_Kingdoms)
        {
            this.data.given_Kingdoms.Add(k.Key);
        }

        foreach (var k in this.taken_Kingdoms)
        {
            this.data.taken_Kingdoms.Add(k.Key);
        }
        SyncCoreKingdomReference();
        this.data.original_capital = !this.OriginalCapital.IsGone() ? this.OriginalCapital.Key : -1L;
        try
        {
            this.data.empire_clan = this.EmpireClan == null ? -1L : this.EmpireClan.Key;
        }
        catch (Exception e)
        {
            this.data.empire_clan = -1L;
            LogService.LogError($"存储帝国氏族失败: {e}");
        }

    }
#endif

    // Token: 0x0600112B RID: 4395 RVA: 0x000C7CCC File Offset: 0x000C5ECC
#if EC_PENDING
    public override void LoadData(EmpireData pData)
    {
        base.LoadData(pData);
        if (pData.archived)
        {
            this.EmpireClan = EmpireWorld.Current.ClanByKey(pData.empire_clan);
            this.Religion = EmpireWorld.Current.religions.get(pData.Religion);
            this.OriginalCapital = EmpireWorld.Current.CityByKey(pData.original_capital);
            this.CoreKingdom = EmpireWorld.Current.RealmByKey(pData.empire);
            if (this.CoreKingdom == null || this.CoreKingdom.IsGone())
            {
                this.CoreKingdom = EmpireWorld.Current.RealmByKey(pData.last_core_kingdom_id);
            }
            SyncCoreKingdomReference();
            return;
        }
        foreach (long tKingdomID in this.data.kingdoms ?? new List<long>())
        {
            Realm tKingdom = EmpireWorld.Current.RealmByKey(tKingdomID);
            if (EmpireMembershipService.BelongsTo(this, tKingdom))
            {
                kingdoms_hashset.Add(tKingdom);
            }
        }
        if (this.data.cities != null)
        {
            foreach (long tCityID in this.data.cities)
            {
                City tCity = EmpireWorld.Current.CityByKey(tCityID);
                if (tCity != null && EmpireMembershipService.BelongsTo(this, tCity.Realm))
                {
                    cities_list.Add(tCity);
                }
            }       
        }
        else
        {
            cities_list = new List<City>();
            foreach (var kingdom in kingdoms_hashset)
            {
                cities_list = cities_list.Union(kingdom.Cities).ToList();
            }
        }
        
        foreach (var k in pData.given_Kingdoms)
        {
            given_Kingdoms.Add(EmpireWorld.Current.RealmByKey(k));
        }

        foreach (var k in pData.taken_Kingdoms)
        {
            taken_Kingdoms.Add(EmpireWorld.Current.RealmByKey(k));
        }

        this.Religion = EmpireWorld.Current.religions.get(pData.Religion);
        this.CoreKingdom = EmpireWorld.Current.RealmByKey(pData.empire);
        if (!EmpireMembershipService.BelongsTo(this, CoreKingdom))
        {
            this.CoreKingdom = EmpireWorld.Current.RealmByKey(pData.last_core_kingdom_id);
        }
        if (!EmpireMembershipService.BelongsTo(this, CoreKingdom)) CoreKingdom = null;
        if (this.CoreKingdom != null)
        {
            this.CoreKingdom.GetOrCreate().isEmpire = true;
            kingdoms_hashset.Add(CoreKingdom);
            SyncCoreKingdomReference();
        }
        this.EmpireClan = EmpireWorld.Current.ClanByKey(pData.empire_clan);
        this.OriginalCapital = EmpireWorld.Current.CityByKey(pData.original_capital);
        RepairFoundingEmperorMarker();
        this.recalculate();
    }
#endif

    // Token: 0x06001128 RID: 4392 RVA: 0x000C7890 File Offset: 0x000C5A90
#if EC_PENDING
    public void join(Realm pKingdom, bool pRecalc = true, bool pForce = false,
        bool pLegitimacyTransfer = false)
    {
        if (pKingdom == null || pKingdom.IsGone()) return;
        if (AncientWarfareCompatibility.Owns(CoreKingdom) || AncientWarfareCompatibility.Owns(pKingdom)) return;
        if (hasKingdom(pKingdom))
        {
            return;
        }
        // 神力放出的国保护期内不得收回，强制的也不行；打仗打下来的、自己危急来投的照旧(见 MembershipCooldown)
        if (MembershipCooldown.BlocksJoin(pKingdom, this)) return;
        // A rebel polity can never return to an empire it previously rebelled against,
        // including through forced or scripted membership changes.
        if (!pLegitimacyTransfer && pKingdom.HasRebelledAgainst(this)) return;
        if (pLegitimacyTransfer) pKingdom.ForgiveRebellionAgainst(this);
        if (!pForce && !this.canJoin(pKingdom))
        {
            return;
        }

        if (pKingdom.IsInEmpire())
        {
            var originalEmpire = pKingdom.GetEmpire();
            originalEmpire.leave(pKingdom);
        }
        kingdoms_hashset.Add(pKingdom);
        cities_list = cities_list.Union(pKingdom.Cities).ToList();
        pKingdom.EmpireJoin(this);
        // 帝国与同盟不兼容：入帝国即退出原来的同盟
        ModAllianceService.LeaveAllianceForEmpire(pKingdom);
        pKingdom.SetFiedTimestamp(GameClock.Now);
        if (pKingdom.HasTakenAlliance())
        {
            pKingdom.RemoveTakenAlliance();
        }
        if (pKingdom.HasGivenAlliance())
        {
            pKingdom.RemoveGivenAlliance();
        }
        pKingdom.RemoveFactionRatio();
        if (pRecalc)
        {
            recalculate();
        }
        data.timestamp_member_joined = GameClock.Now;
        // 古典共和帝国与西方封建帝国本质上是国家联盟：成员完全自治
        if (PersonalUnionService.IsUnionRegime(CoreKingdom) && pKingdom != CoreKingdom)
            CityStateService.GrantAutonomy(pKingdom);
        if (CompositeEmpireService.IsComposite(this))
            CompositeEmpireService.SynchronizeRegionalInstitution(this, pKingdom);
        // 神力拉入的记下稳定期；诊断日志(见 MembershipCooldown)
        if (pKingdom.GetEmpire() == this) MembershipCooldown.OnJoined(pKingdom, this, pForce, pLegitimacyTransfer);
    }

    public void leave(Realm pKingdom, bool pRecalc = true, bool isLeave = false)
    {
        if (pKingdom == null) return;
        // 旧帝国只清掉自己的残留记录，不能把已转投别国的国家清成独立，更不能解散新帝国。
        if (pKingdom.GetEmpireID() != id)
        {
            kingdoms_hashset.Remove(pKingdom);
            kingdoms_list.Remove(pKingdom);
            cities_list.RemoveAll(city => city?.Realm == pKingdom);
            data.kingdoms?.RemoveAll(kingdomId => kingdomId == pKingdom.Key);
            if (pRecalc) recalculate();
            return;
        }
        // 不许自立(见 SecessionGate)：开关开着时成员国不叛离；已经在叛乱、在跟中央打的照原规矩收尾。
        // 调用方离开后都看 GetEmpire / IsInEmpire 判成没成
        if (isLeave && pKingdom != CoreKingdom && !pKingdom.IsGone() && !pKingdom.IsLocalRebelling() &&
            !pKingdom.IsFactionRebelling() && (CoreKingdom == null || !pKingdom.IsAtWarWith(CoreKingdom)) &&
            SecessionGate.Forbids("叛离帝国", $"{pKingdom.Record?.Name}(离 {data?.name})"))
            return;
        if (isLeave)
        {
            pKingdom.RememberRebellionOrigin(this);
        }
        bool isCoreKingdom = pKingdom == CoreKingdom;
        this.kingdoms_hashset.Remove(pKingdom);
        pKingdom.EmpireLeave(isLeave);
        // 记下离开的帝国与时间(十年内不自愿回来)、神力放出的保护期；诊断日志(见 MembershipCooldown)
        if (!isCoreKingdom) MembershipCooldown.OnLeft(pKingdom, this, isLeave);
        cities_list = this.cities_list.Except(pKingdom?.Cities??new List<City>()).ToList();
        if (isCoreKingdom)
        {
            CheckDissolve(pKingdom);
        } else
        {
            if (ShouldDissolveEmpire())
            {
                if (!TryRepairState("leave after kingdom removed"))
                {
                    ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
                    LogService.LogInfo("帝国内部国家数量为0解散");
                }
            }
        }
        if (pRecalc)
        {
            this.recalculate();
        }
    }
#endif

    private bool ShouldDissolveEmpire()
    {
        // 如果没有王国剩余
        if (countKingdoms() <= 0)
        {
            return true;
        }

        return false;
    }

#if EC_PENDING
    public int countBuildings()
    {
        int tResult = 0;
        List<Realm> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Realm tKingdom = tKingdoms[i];
            tResult += tKingdom.countBuildings();
        }
        return tResult;
    }


    public int countZones()
    {
        int tResult = 0;
        List<Realm> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Realm tKingdom = tKingdoms[i];
            tResult += tKingdom.countZones();
        }
        return tResult;
    }

    public List<TileZone> allZones()
    {
        _zoneScratch.Clear();
        foreach (City city in EmpireMembershipService.EnumerateCities(this))
            foreach (TileZone zone in city.zones)
                if (zone?.city == city) _zoneScratch.Add(zone);
        return _zoneScratch;
    }

    public override int countUnits()
    {
        return this.CountPopulation();
    }
#endif


    public int CountPopulation()
    {
        return data.cached_population;
    }

#if EC_PENDING
    public void RefreshNationalPower(bool force = false)
    {
        if (data == null || isRekt() || IsArchived()) return;
        if (!force && data.last_national_power_timestamp > 0 &&
            GameClock.YearsSince(data.last_national_power_timestamp) < 1)
        {
            return;
        }

        int population = 0;
        int military = 0;
        int economy = 0;
        foreach (Realm kingdom in kingdoms_list)
        {
            if (kingdom == null || kingdom.IsGone()) continue;
            kingdom.RefreshNationalPower(force);
            KingdomExtension.KingdomExtraData kingdomData = kingdom.GetOrCreate();
            population += kingdomData.annual_power_population;
            military += kingdomData.annual_power_military;
            economy += kingdomData.annual_power_economy;
        }

        data.annual_power_population = population;
        data.annual_power_military = military;
        data.annual_power_economy = economy;
        data.annual_power_index = NationalPowerRules.Calculate(population, military, economy);
        data.last_national_power_timestamp = GameClock.Now;
    }

    public double GetNationalPower()
    {
        RefreshNationalPower();
        return data?.annual_power_index ?? 0d;
    }

    public string GetNationalPowerBreakdown()
    {
        RefreshNationalPower();
        return data == null
            ? "0 / 0 / 0"
            : $"{data.annual_power_population} / {data.annual_power_military} / {data.annual_power_economy}";
    }


    public int countMaxPopulation()
    {
        int tResult = 0;
        List<Realm> tKingdoms = this.kingdoms_list;
        foreach (var tKingdom in tKingdoms)
        {
            tResult += tKingdom.getPopulationTotalPossible();
        }
        return tResult;
    }
#endif

    public List<City> AllCities()
    {
        return EmpireMembershipService.EnumerateCities(this).ToList();
    }


#if EC_PENDING
    public int countCities()
    {
        int tResult = 0;
        List<Realm> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Realm tKingdom = tKingdoms[i];
            tResult += tKingdom.countCities();
        }
        return tResult;
    }
#endif

    // Token: 0x06001131 RID: 4401 RVA: 0x000C7E41 File Offset: 0x000C6041
    public int countKingdoms()
    {
        return this.kingdoms_hashset.Count;
    }

    // Token: 0x06001132 RID: 4402 RVA: 0x000C7E50 File Offset: 0x000C6050
#if EC_PENDING
    public string getMotto()
    {
        if (string.IsNullOrEmpty(this.data.motto))
        {
            this.data.motto = NameGenerator.getName("alliance_mottos", Sex.Male, false, null, null, false);
        }
        return this.data.motto;
    }
#endif

    // Token: 0x06001133 RID: 4403 RVA: 0x000C7E9C File Offset: 0x000C609C
    public int countWarriors()
    {
        return data.cached_warriors;
    }

    // Token: 0x06001133 RID: 4403 RVA: 0x000C7E9C File Offset: 0x000C609C
    public int countWarriorsMax()
    {
        return data.cached_warriors_max;
    }
    public void calculateDoesHaveTitle ()
    {
        foreach(KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            var title_cities = title.city_list;
        }
    }
    // Token: 0x06001134 RID: 4404 RVA: 0x000C7ED5 File Offset: 0x000C60D5
#if EC_PENDING
    public static bool isSame(Alliance pAlliance1, Alliance pAlliance2)
    {
        return pAlliance1 != null && pAlliance2 != null && pAlliance1 == pAlliance2;
    }
#endif

    // Token: 0x06001135 RID: 4405 RVA: 0x000C7EE4 File Offset: 0x000C60E4
    public bool hasWarsWith(Realm pKingdom)
    {
        List<Realm> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Realm tAllianceKingdom = tKingdoms[i];
            if (pKingdom.IsAtWarWith(tAllianceKingdom))
            {
                return true;
            }
        }
        return false;
    }

    // Token: 0x06001136 RID: 4406 RVA: 0x000C7F1D File Offset: 0x000C611D
#if EC_PENDING
    public bool hasSupremeKingdom()
    {
        return DiplomacyManager.kingdom_supreme != null && this.hasKingdom(DiplomacyManager.kingdom_supreme);
    }
#endif

    // Token: 0x06001137 RID: 4407 RVA: 0x000C7F33 File Offset: 0x000C6133
    public bool hasKingdom(Realm pKingdom)
    {
        return EmpireMembershipService.BelongsTo(this, pKingdom) && this.kingdoms_hashset.Contains(pKingdom);
    }

    // Token: 0x06001138 RID: 4408 RVA: 0x000C7F44 File Offset: 0x000C6144
#if EC_PENDING
    public bool hasSharedBordersWithKingdom(Realm pKingdom)
    {
        List<Realm> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Realm tKingdom = tKingdoms[i];
            if (DiplomacyHelpers.areKingdomsClose(pKingdom, tKingdom))
            {
                return true;
            }
        }
        return false;
    }
#endif

    // Token: 0x06001139 RID: 4409 RVA: 0x000C7F7D File Offset: 0x000C617D
#if EC_PENDING
    public bool hasWars()
    {
        return EmpireWorld.Current.HasWars(this.CoreKingdom);
    }
#endif

    // Token: 0x0600113A RID: 4410 RVA: 0x000C7F8F File Offset: 0x000C618F
#if EC_PENDING
    public IEnumerable<War> getWars(bool pRandom = false)
    {
        return EmpireWorld.Current.LiveWars.getWars(this.CoreKingdom, pRandom);
    }
#endif

    // Token: 0x0600113B RID: 4411 RVA: 0x000C7FA4 File Offset: 0x000C61A4
#if EC_PENDING
    public void mergeWars()
    {
        if (!this.hasWars())
        {
            return;
        }
        using (ListPool<War> tWars = new ListPool<War>(this.getWars(false)))
        {
            for (int i = 0; i < tWars.Count(); i++)
            {
                War tWar = tWars.ElementAt(i);
                if (!tWar.HasEnded())
                {
                    for (int j = i + 1; j < tWars.Count(); j++)
                    {
                        War tWar2 = tWars.ElementAt(j);
                        if (!tWar2.HasEnded() && tWar.isSameAs(tWar2))
                        {
                            if (tWar.StartedStamp < tWar2.StartedStamp)
                            {
                                EmpireWorld.Current.EndWar(tWar2, WarOutcome.Merged);
                            }
                            else
                            {
                                EmpireWorld.Current.EndWar(tWar, WarOutcome.Merged);
                            }
                            this.mergeWars();
                            return;
                        }
                    }
                }
            }
        }
    }

    public Vector3 GetEmpireCenter()
    {
        if (!this._units_dirty)
            return this._lastEmpireCenter;

        if (this.countZones()<=0)
        {
            this._empireCenter = Globals.POINT_IN_VOID_2;
            return this._empireCenter;
        }
        float num = 0f;
        float num2 = 0f;
        float num3 = float.MaxValue;
        TileZone tileZone = null;
        var zones = this.allZones();
        foreach (var tileZone2 in zones)
        {
            num += tileZone2.centerTile.posV3.x;
            num2 += tileZone2.centerTile.posV3.y;
        }
        this._empireCenter.x = num / (float)zones.Count;
        this._empireCenter.y = num2 / (float)zones.Count;
        for (int j = 0; j < zones.Count; j++)
        {
            TileZone tileZone3 = zones[j];
            float num4 = Toolbox.SquaredDist((float)tileZone3.centerTile.x, (float)tileZone3.centerTile.y, this._empireCenter.x, this._empireCenter.y);
            if (num4 < num3)
            {
                tileZone = tileZone3;
                num3 = num4;
            }
        }
        this._empireCenter.x = tileZone.centerTile.posV3.x;
        this._empireCenter.y = tileZone.centerTile.posV3.y + 2f;
        this._lastEmpireCenter = this._empireCenter;
        this._units_dirty = false;
        return this._lastEmpireCenter;
    }
    
    
    public override IEnumerable<Person> getUnits()
    {
        return EmpirePopulation.Enumerate(kingdoms_list);
    }


    private List<KingdomTitle> GetLegalPeerageTitles()
    {
        EmpireCore core = EmpireCoreManager.Get(this);
        if (core == null) return new List<KingdomTitle>();
        return EmpireCoreManager.GetTitles(core)
            .Where(title => title != null && !title.IsGone() &&
                !string.Equals(title.data.name, GetEmpireName(), StringComparison.Ordinal))
            .OrderBy(title => title.data.name)
            .ToList();
    }

    public bool CanReclaimTitlesFrom(Realm sourceKingdom)
    {
        // 春秋战国王室衰微，收不回诸侯的法理(见 ZhouEraSystem.RoyalReclaimBlocked)
        if (ZhouEraSystem.RoyalReclaimBlocked(this)) return false;
        return sourceKingdom != null && !sourceKingdom.IsGone() && sourceKingdom.GetEmpire() == this &&
               sourceKingdom != CoreKingdom && sourceKingdom.Ruler != null && sourceKingdom.Ruler != Emperor &&
               sourceKingdom.Ruler.Alive && sourceKingdom.Ruler.HasTitle() &&
               sourceKingdom.Ruler.GetViolateValue() >= 90 && sourceKingdom.HasMainCrime() &&
               CoreKingdom?.HasLaw(sourceKingdom.GetMainCrime()) == true &&
               Emperor != null && !Emperor.IsGone() && Emperor.Alive;
    }

    public int ReclaimTitles(Realm sourceKingdom, IEnumerable<KingdomTitle> titles)
    {
        if (sourceKingdom == null || sourceKingdom.IsGone() || sourceKingdom.GetEmpire() != this ||
            sourceKingdom == CoreKingdom || sourceKingdom.Ruler == Emperor ||
            Emperor == null || Emperor.IsGone()) return 0;
        if (!CanReclaimTitlesFrom(sourceKingdom)) return 0;
        List<KingdomTitle> reclaimedTitles = (titles ?? Enumerable.Empty<KingdomTitle>())
            .Where(title => title != null && !title.IsGone() && title.data != null &&
                title.owner == sourceKingdom.Ruler)
            .Distinct()
            .ToList();
        if (reclaimedTitles.Count == 0) return 0;

        Person formerHolder = sourceKingdom.Ruler;
        long storedMainTitleId = sourceKingdom.GetOrCreate().MainTitle;
        if (reclaimedTitles.Any(title => title.Key == storedMainTitleId))
        {
            sourceKingdom.RemoveMainTitle();
        }

        foreach (KingdomTitle title in reclaimedTitles)
        {
            title.EndJurisdiction(sourceKingdom, KingdomTitle.JurisdictionHolder);
            if (title.main_kingdom == sourceKingdom) title.main_kingdom = null;
            formerHolder?.GetOwnedTitle()?.Remove(title.Key);
            sourceKingdom.UnregisterRealmTitle(title);
            if (title.owner == formerHolder) title.owner = null;

            foreach (Person virtualHolder in getUnits().Where(actor => actor != null && !actor.IsGone() &&
                         actor.HasVirtualEnfeoff(this) &&
                         actor.GetOrCreate().virtual_enfeoff_title_id == title.Key).ToList())
            {
                var holderData = virtualHolder.GetOrCreate();
                holderData.virtual_enfeoff = false;
                holderData.virtual_enfeoff_empire_id = -1L;
                holderData.virtual_enfeoff_title_id = -1L;
                holderData.virtual_enfeoff_peerage_key = "";
            }

            data.legal_peerage_holders?.Remove(title.Key);
            data.legal_peerage_holder_identities?.Remove(title.Key);
            data.legal_peerage_types?.Remove(title.Key);
            data.legal_peerage_kingdoms?.Remove(title.Key);
            Emperor.AddOwnedTitle(title);
            title.owner = Emperor;
        }

        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(sourceKingdom);
        sourceKingdom.RemoveMainCrime();
        return reclaimedTitles.Count;
    }

    public KingdomTitle GetTributaryPetitionTitle(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone() || !kingdom.HasRuler() || !kingdom.HasCapital()) return null;
        bool isTributary = kingdom.GetTakenAllianceEmpire() == this;
        KingdomOpinion opinion = EmpireWorld.Current?.diplomacy?.getOpinion(CoreKingdom, kingdom);
        if (!TributaryTitlePetitionRules.CanPetition(isTributary, kingdom.HasMainTitle(),
                opinion?.total ?? int.MinValue, kingdom.Ruler.Renown)) return null;

        EmpireCore core = EmpireCoreManager.Get(this);
        if (core == null) return null;
        KingdomTitle capitalTitle = kingdom.Capital.GetTitle();
        KingdomTitle bestTitle = null;
        int bestControlledCount = -1;
        foreach (KingdomTitle title in EmpireCoreManager.GetTitles(core))
        {
            if (title == null || title.IsGone() || title.data == null || title.title_capital == null ||
                title.title_capital.IsGone() || title.title_capital.Realm != kingdom) continue;
            if (title.main_kingdom != null && title.main_kingdom != kingdom) continue;
            Realm landedHolder = GetLandedLegalTitleKingdom(title);
            if (landedHolder != null && landedHolder != kingdom) continue;
            if (title.HasOwner() && title.owner != Emperor && title.owner != kingdom.Ruler && title.owner.IsRuler())
                continue;

            List<City> titleCities = title.city_list
                .Where(city => city != null && !city.IsGone())
                .ToList();
            int controlledCount = titleCities.Count(city => city.Realm == kingdom);
            if (!TributaryTitlePetitionRules.ControlsRequiredTerritory(true, controlledCount,
                    titleCities.Count, title.data.title_controlled_rate)) continue;

            bool preferCurrentCapital = title == capitalTitle && bestTitle != capitalTitle;
            if (bestTitle == null || preferCurrentCapital ||
                bestTitle != capitalTitle && controlledCount > bestControlledCount)
            {
                bestTitle = title;
                bestControlledCount = controlledCount;
            }
        }
        return bestTitle;
    }

    public bool GrantTributaryPetitionTitle(Realm kingdom, KingdomTitle requestedTitle)
    {
        if (requestedTitle == null || GetTributaryPetitionTitle(kingdom) != requestedTitle) return false;
        Person ruler = kingdom.Ruler;
        foreach (Person holder in getUnits().Where(actor => actor != null && !actor.IsGone() &&
                     actor.HasVirtualEnfeoff(this) &&
                     actor.GetOrCreate().virtual_enfeoff_title_id == requestedTitle.Key).ToList())
        {
            var holderData = holder.GetOrCreate();
            holderData.virtual_enfeoff = false;
            holderData.virtual_enfeoff_empire_id = -1L;
            holderData.virtual_enfeoff_title_id = -1L;
            holderData.virtual_enfeoff_peerage_key = "";
            if (!holder.IsRuler() && (holder.GetOwnedTitle()?.Count ?? 0) == 0)
                holder.SetPeeragesLevel(PeeragesLevel.peerages_6);
        }

        data.legal_peerage_holders?.Remove(requestedTitle.Key);
        data.legal_peerage_holder_identities?.Remove(requestedTitle.Key);
        data.legal_peerage_types?.Remove(requestedTitle.Key);
        data.legal_peerage_kingdoms?.Remove(requestedTitle.Key);
        if (requestedTitle.owner != null && requestedTitle.owner != ruler)
            requestedTitle.owner.GetOwnedTitle()?.Remove(requestedTitle.Key);
        ruler.AddOwnedTitle(requestedTitle);
        requestedTitle.owner = ruler;
        if (!kingdom.ReconcileMainTitle(new[] { requestedTitle }) && kingdom.GetMainTitle() != requestedTitle)
            return false;
        SynchronizeLandedLegalTitle(requestedTitle, kingdom);
        if (GetLandedLegalTitleKingdom(requestedTitle) != kingdom ||
            GetLegalPeerageHolder(requestedTitle) != ruler) return false;

        ruler.Renown -= TributaryTitlePetitionRules.PetitionInfluenceCost;
        TranslateHelper.LogTributaryTitleGranted(kingdom, this, requestedTitle);
        return true;
    }

    public Realm GetLandedLegalTitleKingdom(KingdomTitle title)
    {
        if (title == null || title.IsGone()) return null;
        Realm landedKingdom = null;
        if (data.legal_peerage_kingdoms?.TryGetValue(title.Key, out long kingdomId) == true)
        {
            Realm savedKingdom = EmpireWorld.Current.RealmByKey(kingdomId);
            bool ownsTerritory = savedKingdom?.Cities != null &&
                (title.city_list_hash.Any(savedKingdom.Cities.Contains) ||
                 title.city_list.Any(savedKingdom.Cities.Contains));
            bool hasBinding = title.main_kingdom == savedKingdom || title.owner == savedKingdom?.Ruler ||
                              ((title.owner == null || title.owner.IsGone() || !title.owner.IsRuler()) &&
                               ownsTerritory);
            bool delegatedOnly = savedKingdom?.GetAdministrativeTitle() == title &&
                                 title.main_kingdom != savedKingdom && title.owner != savedKingdom?.Ruler;
            if (savedKingdom != null && !savedKingdom.IsGone() && savedKingdom != CoreKingdom &&
                IsLandedLegalTitleRealm(savedKingdom) && hasBinding && !delegatedOnly)
                landedKingdom = savedKingdom;
        }
        landedKingdom ??= title.main_kingdom;
        if (landedKingdom == null || landedKingdom.IsGone() || landedKingdom == CoreKingdom ||
            !IsLandedLegalTitleRealm(landedKingdom))
        {
            Person owner = title.owner;
            Realm ownerKingdom = owner != null && !owner.IsGone() && owner.IsRuler()
                ? owner.Realm
                : null;
            bool ownsTerritory = ownerKingdom?.Cities != null &&
                (title.city_list_hash.Any(ownerKingdom.Cities.Contains) ||
                 title.city_list.Any(ownerKingdom.Cities.Contains));
            landedKingdom = ownerKingdom != null && ownerKingdom != CoreKingdom &&
                            IsLandedLegalTitleRealm(ownerKingdom) && ownsTerritory
                ? ownerKingdom
                : null;
        }
        return landedKingdom;
    }

    private Person GetLivingVirtualLegalPeerageHolder(long titleId)
    {
        Person holder = getUnits().FirstOrDefault(actor => actor != null && !actor.IsGone() &&
            actor.HasVirtualEnfeoff(this) && actor.GetOrCreate().virtual_enfeoff_title_id == titleId);
        if (holder != null) return holder;
        if (data.legal_peerage_holders?.TryGetValue(titleId, out long holderId) != true) return null;
        holder = EmpireWorld.Current.PersonByKey(holderId);
        return holder != null && !holder.IsGone() && holder.HasVirtualEnfeoff(this) &&
            holder.GetOrCreate().virtual_enfeoff_title_id == titleId ? holder : null;
    }

    public Person GetLegalPeerageHolder(KingdomTitle title)
    {
        Realm landedKingdom = GetLandedLegalTitleKingdom(title);
        return landedKingdom != null
            ? landedKingdom.Ruler
            : GetLivingVirtualLegalPeerageHolder(title?.Key ?? -1L);
    }

    public bool SynchronizeLandedLegalTitles(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone() || kingdom == CoreKingdom || !kingdom.HasRuler()) return false;
        bool isMember = kingdom.GetEmpire() == this;
        bool isTributary = kingdom.GetTakenAllianceEmpire() == this;
        if (!isTributary && (!isMember || CoreKingdom?.GetRegime()?.type != RegimeType.LvLing)) return false;
        bool changed = false;
        foreach (KingdomTitle title in GetLegalPeerageTitles())
        {
            Realm landedKingdom = GetLandedLegalTitleKingdom(title);
            if (landedKingdom != kingdom) continue;
            changed |= SynchronizeLandedLegalTitle(title, kingdom);
        }
        return changed;
    }
#endif

    private bool IsLandedLegalTitleRealm(Realm kingdom)
    {
        return kingdom != null && (kingdom.GetEmpire() == this || kingdom.GetTakenAllianceEmpire() == this);
    }

#if EC_PENDING
    private bool SynchronizeLandedLegalTitle(KingdomTitle title, Realm kingdom)
    {
        Person ruler = kingdom?.Ruler;
        if (title == null || ruler == null || ruler.IsGone()) return false;
        bool changed = false;

        foreach (Person duplicate in getUnits().Where(actor => actor != null && actor.Key != ruler.Key &&
                     !actor.IsGone() && actor.HasVirtualEnfeoff(this) &&
                     actor.GetOrCreate().virtual_enfeoff_title_id == title.Key).ToList())
        {
            var duplicateData = duplicate.GetOrCreate();
            duplicateData.virtual_enfeoff = false;
            duplicateData.virtual_enfeoff_empire_id = -1L;
            duplicateData.virtual_enfeoff_title_id = -1L;
            duplicateData.virtual_enfeoff_peerage_key = "";
            if (!duplicate.IsRuler() && (duplicate.GetOwnedTitle()?.Count ?? 0) == 0)
                duplicate.SetPeeragesLevel(PeeragesLevel.peerages_6);
            changed = true;
        }

        Person previousOwner = title.owner;
        if (previousOwner != null && previousOwner.Key != ruler.Key)
        {
            previousOwner.GetOwnedTitle()?.Remove(title.Key);
            changed = true;
        }
        if (!(ruler.GetOwnedTitle()?.Contains(title.Key) ?? false))
        {
            ruler.AddOwnedTitle(title);
            changed = true;
        }
        title.owner = ruler;

        var rulerData = ruler.GetOrCreate();
        if (rulerData.virtual_enfeoff && rulerData.virtual_enfeoff_empire_id == data.id &&
            rulerData.virtual_enfeoff_title_id == title.Key)
        {
            rulerData.virtual_enfeoff = false;
            rulerData.virtual_enfeoff_empire_id = -1L;
            rulerData.virtual_enfeoff_title_id = -1L;
            rulerData.virtual_enfeoff_peerage_key = "";
            changed = true;
        }

        bool isImperialClan = ruler.GetSpecificClan() != null && SpecificClanManager.SameLineage(ruler.GetSpecificClan(), EmpireSpecificClan);
        bool isPetitionedTributaryTitle = kingdom.GetTakenAllianceEmpire() == this;
        bool receivesKingPeerage = isImperialClan || isPetitionedTributaryTitle;
        string peerageKey = DeJureTitleBindingRules.GetLandedPeerageKey(receivesKingPeerage);
        PeeragesLevel peerageLevel = receivesKingPeerage
            ? PeeragesLevel.peerages_2
            : PeeragesLevel.peerages_3;
        if (ruler.GetPeeragesLevel() != peerageLevel)
        {
            ruler.SetPeeragesLevel(peerageLevel);
            changed = true;
        }

        data.legal_peerage_holders ??= new Dictionary<long, long>();
        data.legal_peerage_holder_identities ??= new Dictionary<long, long>();
        data.legal_peerage_types ??= new Dictionary<long, string>();
        data.legal_peerage_kingdoms ??= new Dictionary<long, long>();
        long identityId = ruler.GetPersonalIdentity()?.id ?? -1L;
        if (!data.legal_peerage_holders.TryGetValue(title.Key, out long holderId) || holderId != ruler.Key ||
            !data.legal_peerage_holder_identities.TryGetValue(title.Key, out long savedIdentityId) ||
            savedIdentityId != identityId || !data.legal_peerage_types.TryGetValue(title.Key, out string savedType) ||
            savedType != peerageKey)
        {
            data.legal_peerage_holders[title.Key] = ruler.Key;
            data.legal_peerage_holder_identities[title.Key] = identityId;
            data.legal_peerage_types[title.Key] = peerageKey;
            changed = true;
        }
        if (!data.legal_peerage_kingdoms.TryGetValue(title.Key, out long savedKingdomId) ||
            savedKingdomId != kingdom.Key)
        {
            data.legal_peerage_kingdoms[title.Key] = kingdom.Key;
            changed = true;
        }

        if (data.powerful_minister_title_id == title.Key && ruler.Key != data.powerful_minister_id)
        {
            data.powerful_minister_title_id = -1L;
            data.powerful_minister_stage = PowerfulMinisterStageDominant;
            data.powerful_minister_stage_timestamp = GameClock.Now;
        }
        return changed;
    }

    private Person GetLivingLegalPeerageHolder(long titleId)
    {
        Person holder = getUnits().FirstOrDefault(actor => actor != null && !actor.IsGone() &&
            actor.HasVirtualEnfeoff(this) && actor.GetOrCreate().virtual_enfeoff_title_id == titleId);
        if (holder != null) return holder;
        if (data.legal_peerage_holders?.TryGetValue(titleId, out long holderId) != true) return null;
        holder = EmpireWorld.Current.PersonByKey(holderId);
        return holder != null && !holder.IsGone() && holder.HasVirtualEnfeoff(this) &&
            holder.GetOrCreate().virtual_enfeoff_title_id == titleId ? holder : null;
    }

    public Person GetPowerfulMinister()
    {
        Person actor = ResolvePowerfulMinister();
        return actor != null && actor.Realm?.GetEmpire() == this ? actor : null;
    }
#endif

    // 按族谱找回权臣(见 PersonRef)：以前只按单位编号找，权臣被并回人口、落成换了编号就当没了，专权进度无声中断
#if EC_PENDING
    private Person ResolvePowerfulMinister()
    {
        if (data == null || data.powerful_minister_id <= 0 || EmpireWorld.Current == null) return null;
        long actorId = data.powerful_minister_id, identityId = data.powerful_minister_identity_id;
        Person actor = PersonRef.Resolve(ref actorId, ref identityId);
        data.powerful_minister_id = actorId;
        data.powerful_minister_identity_id = identityId;
        return actor;
    }

    public bool IsCurrentPowerfulMinister(Person actor)
    {
        return actor != null && GetPowerfulMinister()?.Key == actor.Key;
    }

    public string GetPowerfulMinisterStatusText()
    {
        Person minister = GetPowerfulMinister();
        if (minister == null) return "";
        string statusKey = data.powerful_minister_is_empress_dowager
            ? "powerful_minister_status_dowager"
            : data.powerful_minister_is_regent ? "powerful_minister_status_regent" : "powerful_minister_status_progress";
        string progress = string.Format(LM.Get(statusKey),
            data.powerful_minister_progress);
        return data.powerful_minister_stage switch
        {
            PowerfulMinisterStageKing or PowerfulMinisterStageDuke => minister.GetPeerageDisplayName() + "\n" + progress,
            PowerfulMinisterStageDominant when data.is_been_controlled => LM.Get("powerful_minister_status_dominant") + "\n" + progress,
            _ => progress
        };
    }
#endif

    private bool IsCentralMinister(Person actor)
    {
        if (actor == null || actor.IsGone() || actor.IsRuler() || !actor.IsAdult() ||
            actor.Realm != CoreKingdom || SpecificClanManager.SameLineage(actor.GetSpecificClan(), EmpireSpecificClan)) return false;
        OfficeObject office = actor.GetOffice();
        return office != null && !office.is_local && office.actor_id == actor.Key && office.meta_object == CoreKingdom;
    }

#if EC_PENDING
    private bool HasCentralMinisterSupport(Person actor, Regime currentRegime)
    {
        FixedFaction dominant = currentRegime?.GetDominateFaction();
        return dominant != null && dominant.TotalPower > 0 && actor?.GetFaction() == dominant;
    }

    private Person GetEmperorMother()
    {
        Person emperor = Emperor;
        if (emperor == null || emperor.IsGone()) return null;
        Person mother = emperor.GetParents()?.Where(parent => parent != null && !parent.IsGone() &&
            parent.IsFemale()).OrderByDescending(parent => parent.Renown).FirstOrDefault();
        if (mother == null)
        {
            PersonalClanIdentity identity = emperor.GetPersonalIdentity();
            // 生母转成了虚拟族人的落成一次，之后作君主生母留在地图上(见 PersonRef.Keeps)
            mother = identity?.mother > 0 ? PresentHere(SpecificClanManager.getPerson(identity.mother)) : null;
        }
        return mother != null && !mother.IsGone() && mother.IsAdult() && mother.IsFemale() &&
            mother.Realm?.GetEmpire() == this ? mother : null;
    }
#endif

    // 族谱里在世的人：在本帝国的虚拟族人才落成(在别处的落成了也用不上，又会被并回人口)
#if EC_PENDING
    private Person PresentHere(PersonalClanIdentity person) =>
        KinRoster.OnMap(person) ? person._actor
            : KinRoster.KingdomOf(person)?.GetEmpire() == this ? PersonRef.Present(person) : null;

    private Person GetPreviousEmperorWife()
    {
        EmpireCraftHistory previousReign = data.history?.LastOrDefault(history => history != null && history.id > 0);
        if (previousReign == null) return null;
        Person previousEmperor = EmpireWorld.Current.PersonByKey(previousReign.id);
        SpecificClanManager._actorToPersonLookup.TryGetValue(previousReign.id, out var previousIdentity);
        previousIdentity ??= previousEmperor?.GetPersonalIdentity();
        Person wife = previousIdentity?.hasLover() == true
            ? PresentHere(SpecificClanManager.getPerson(previousIdentity.lover.identity))
            : null;
        wife ??= previousEmperor?.Spouse;
        return IsValidRegencyCandidate(wife) && wife.IsFemale() ? wife : null;
    }
#endif

    // 议立新君的定策者：皇位空缺时先帝的皇后即太后(见 SuccessionCouncilSystem)
#if EC_PENDING
    public Person GetEmpressDowagerForCouncil() => GetPreviousEmperorWife();

    private Person GetPreferredEmpressDowager(out bool isFormerEmpress)
    {
        Person formerEmpress = GetPreviousEmperorWife();
        if (formerEmpress != null)
        {
            isFormerEmpress = true;
            return formerEmpress;
        }
        isFormerEmpress = false;
        return GetEmperorMother();
    }
#endif

    private bool IsEmpressDowager(Person actor)
    {
        return actor != null && data.powerful_minister_is_empress_dowager &&
               data.powerful_minister_id == actor.Key;
    }

    private bool IsValidRegencyCandidate(Person actor)
    {
        return actor != null && !actor.IsGone() && actor.IsAdult() && actor != Emperor &&
               actor.Realm?.GetEmpire() == this;
    }

#if EC_PENDING
    private bool HasPowerfulMinisterSupport(Person actor, Regime currentRegime)
    {
        return IsEmpressDowager(actor) ||
               (data.powerful_minister_is_consort_kin && IsCurrentPowerfulMinister(actor) && ConsortKinSystem.Backed(this)) ||
               (data.powerful_minister_is_regent && RealisticAgeSystem.IsMinor(Emperor) &&
                IsCurrentPowerfulMinister(actor)) || HasCentralMinisterSupport(actor, currentRegime);
    }

    private bool IsValidPowerfulMinister(Person actor)
    {
        if (IsCentralMinister(actor)) return true;
        // 外戚专权不必身居中枢
        if (data.powerful_minister_is_consort_kin && IsCurrentPowerfulMinister(actor) && IsValidRegencyCandidate(actor) &&
            ConsortKinSystem.Backed(this)) return true;
        return data.powerful_minister_is_regent && RealisticAgeSystem.IsMinor(Emperor) &&
               IsCurrentPowerfulMinister(actor) && IsValidRegencyCandidate(actor);
    }

    private bool HasStrongEmperor()
    {
        Person emperor = Emperor;
        return emperor != null && !emperor.IsGone() && !RealisticAgeSystem.IsMinor(emperor) && Mandate >= 80 &&
            emperor.Stewardship >= 10 && !HasVulnerableNewEmperor();
    }
#endif

    private bool HasVulnerableNewEmperor()
    {
        Person emperor = Emperor;
        return emperor != null && !emperor.IsGone() && data.newEmperor_timestamp >= 0 &&
            PowerfulMinisterRules.IsVulnerableNewEmperor(emperor.Renown,
                GameClock.MonthsSince(data.newEmperor_timestamp));
    }

    private bool IsRegencyEnding()
    {
        return data.powerful_minister_regency_end_timestamp >= 0 &&
            GameClock.MonthsSince(data.powerful_minister_regency_end_timestamp) < PowerfulMinisterRules.RegencyRecoveryMonths;
    }

#if EC_PENDING
    private bool IsEligiblePowerBase(Person actor, Regime currentRegime, bool requireInitialInfluence,
        out bool isRegent)
    {
        isRegent = false;
        if (!IsCentralMinister(actor) || currentRegime?.type != RegimeType.LvLing) return false;
        Person cabinetLeader = GetCabinetLeader();
        isRegent = Emperor != null && !Emperor.IsGone() && RealisticAgeSystem.IsMinor(Emperor) &&
            cabinetLeader?.Key == actor.Key;
        if (isRegent) return true;
        bool isFactionLeader = actor.GetFaction()?.GetLeader()?.Key == actor.Key;
        bool isShangshu = actor.GetOffice()?.officeType == 4;
        if (cabinetLeader?.Key != actor.Key && !isFactionLeader && !isShangshu) return false;
        return !requireInitialInfluence || actor.Renown >= PowerfulMinisterRules.EntryInfluence;
    }

    private Person FindPowerfulMinisterCandidate(Regime currentRegime, out bool isRegent,
        out bool isEmpressDowager, out bool isFormerEmpress)
    {
        isRegent = false;
        isEmpressDowager = false;
        isFormerEmpress = false;
        Person current = GetPowerfulMinister();

        // Once powerful-minister progress is above 70%, the current holder is
        // protected from replacement. Resolve the stored actor directly so
        // losing an office, faction position, or other candidacy condition does
        // not replace them. Normal selection resumes only after that actor dies
        // or is otherwise removed from the world.
        if (data.powerful_minister_progress > 70 && data.powerful_minister_id > 0)
        {
            Person lockedCurrent = ResolvePowerfulMinister();
            if (lockedCurrent != null && !lockedCurrent.IsGone())
            {
                isRegent = Emperor != null && !Emperor.IsGone() && RealisticAgeSystem.IsMinor(Emperor);
                isEmpressDowager = data.powerful_minister_is_empress_dowager;
                isFormerEmpress = data.powerful_minister_is_former_empress ||
                    (isEmpressDowager && GetPreviousEmperorWife()?.Key == lockedCurrent.Key);
                return lockedCurrent;
            }
        }

        Person cabinetLeader = GetCabinetLeader();
        bool minorEmperor = Emperor != null && !Emperor.IsGone() && RealisticAgeSystem.IsMinor(Emperor);
        var centralCandidates = data.centerOffice == null ? new List<Person>() :
            data.centerOffice.CoreOffices.Concat(data.centerOffice.Divisions).Concat(data.centerOffice.Harems)
            .Distinct()
            .Select(id => OfficeManager.Offices.TryGetValue(id, out var office) ? office.GetActor() : null)
            .Where(IsCentralMinister).Distinct().ToList();
        Person normalCandidate;
        if (minorEmperor)
        {
            // A child emperor always receives an available adult regent. Office rank and the
            // ordinary 300-influence entry gate only determine preference, never eligibility.
            Person dowager = GetPreferredEmpressDowager(out bool dowagerIsFormerEmpress);
            normalCandidate = centralCandidates.Where(actor => actor?.Key != dowager?.Key)
                .OrderByDescending(actor => actor.Key == current?.Key)
                .ThenByDescending(actor => actor.Key == cabinetLeader?.Key)
                .ThenByDescending(actor => HasCentralMinisterSupport(actor, currentRegime))
                .ThenByDescending(actor => actor.Renown)
                .ThenByDescending(actor => actor.GetOrCreate().officeIdentity?.TotalPerformance ?? 0)
                .FirstOrDefault();
            normalCandidate ??= getUnits().Where(actor => IsValidRegencyCandidate(actor) &&
                    actor.Key != dowager?.Key && actor.Realm == CoreKingdom &&
                    !SpecificClanManager.SameLineage(actor.GetSpecificClan(), EmpireSpecificClan))
                .OrderByDescending(actor => actor.Key == current?.Key)
                .ThenByDescending(actor => actor.Renown).ThenBy(actor => actor.Key).FirstOrDefault();
            normalCandidate ??= getUnits().Where(actor => IsValidRegencyCandidate(actor) &&
                    actor.Key != dowager?.Key && actor.Realm == CoreKingdom)
                .OrderByDescending(actor => actor.Key == current?.Key)
                .ThenByDescending(actor => actor.Renown).ThenBy(actor => actor.Key).FirstOrDefault();
            // 外戚：外戚首领以大将军辅政，优先于朝臣与太后本人(见 ConsortKinSystem)
            Person consortRegent = ConsortKinSystem.RegentFor(this, normalCandidate);
            if (consortRegent != null)
            {
                isRegent = true;
                return consortRegent;
            }
            if (PowerfulMinisterRules.ShouldPreferEmpressDowager(true, dowager != null,
                    dowager?.Renown ?? 0, normalCandidate != null, normalCandidate?.Renown ?? 0))
            {
                isRegent = true;
                isEmpressDowager = true;
                isFormerEmpress = dowagerIsFormerEmpress;
                return dowager;
            }
            if (normalCandidate != null)
            {
                isRegent = true;
                return normalCandidate;
            }
            return null;
        }
        if (IsCentralMinister(current)) return current;
        // 外戚专权不必身居中枢：已在专权的照旧；皇帝庸弱昏聩、或年轻而有太后撑腰时外戚首领可以专权
        if (current != null && data.powerful_minister_is_consort_kin && IsValidRegencyCandidate(current) &&
            ConsortKinSystem.Backed(this)) return current;
        Person consort = ConsortKinSystem.DominantFor(this, PowerfulMinisterRules.EntryInfluence);
        if (consort != null) return consort;
        return centralCandidates.Where(actor => IsEligiblePowerBase(actor, currentRegime, true, out _))
            .OrderByDescending(actor => actor.Key == cabinetLeader?.Key)
            .ThenByDescending(actor => HasCentralMinisterSupport(actor, currentRegime))
            .ThenByDescending(actor => actor.Renown)
            .ThenByDescending(actor => actor.GetOrCreate().officeIdentity?.TotalPerformance ?? 0)
            .FirstOrDefault();
    }

    private void UpdatePowerfulMinister(Regime currentRegime)
    {
        Person emperor = Emperor;
        if (currentRegime?.type != RegimeType.LvLing) { ClearPowerfulMinister(); return; }
        if (emperor == null || emperor.IsGone())
        {
            data.last_powerful_minister_timestamp = GameClock.Now;
            return;
        }
        Person candidate = FindPowerfulMinisterCandidate(currentRegime, out bool isRegent,
            out bool isEmpressDowager, out bool isFormerEmpress);
        EmpireCraftDebugProbe.Observe("powerful_minister.candidate", id.ToString(),
            $"{emperor.Key}:{candidate?.Key ?? -1L}:{isRegent}:{isEmpressDowager}:{isFormerEmpress}", () =>
                $"empire={GetEmpireFullName()}({id}), emperor={emperor.FullName()}({emperor.Key}), " +
                $"emperorRenown={emperor.Renown}, emperorAdult={emperor.IsAdult()}, " +
                $"candidate={candidate?.FullName() ?? "none"}({candidate?.Key ?? -1L}), " +
                $"candidateRenown={candidate?.Renown ?? 0}, regent={isRegent}, " +
                $"empressDowager={isEmpressDowager}, formerEmpress={isFormerEmpress}, regime={currentRegime.type}");
        if (candidate?.Key != data.powerful_minister_id && data.is_been_controlled)
        {
            Person previous = ResolvePowerfulMinister();
            if (previous != null && !previous.IsGone()) RecordMinisterTransition(previous, "history_minister_lost_control");
        }
        if (candidate == null)
        {
            ClearPowerfulMinister();
            return;
        }
        if (data.powerful_minister_id != candidate.Key)
        {
            ClearPowerfulMinister();
            data.powerful_minister_id = candidate.Key;
            data.powerful_minister_identity_id = candidate.GetPersonalIdentity()?.id ?? -1L;
            data.powerful_minister_progress = 0;
            data.powerful_minister_is_regent = isRegent;
            data.powerful_minister_is_empress_dowager = isEmpressDowager;
            data.powerful_minister_is_former_empress = isFormerEmpress;
            data.powerful_minister_is_consort_kin = !isEmpressDowager && ConsortKinSystem.IsConsortKin(this, candidate);
            data.last_powerful_minister_timestamp = GameClock.Now;
            data.powerful_minister_emperor_id = emperor.Key;
            if (candidate.HasVirtualEnfeoff(this))
            {
                var titleData = candidate.GetOrCreate();
                data.powerful_minister_title_id = titleData.virtual_enfeoff_title_id;
                data.powerful_minister_stage = titleData.virtual_enfeoff_peerage_key == "default_peerages_2"
                    ? PowerfulMinisterStageKing : PowerfulMinisterStageDuke;
                data.powerful_minister_stage_timestamp = GameClock.Now;
            }
            if (data.powerful_minister_is_consort_kin)
                RecordMinisterTransition(candidate, isRegent ? "history_consort_regent" : "history_consort_dominant");
            else if (isRegent) RecordMinisterTransition(candidate,
                isEmpressDowager ? "history_empress_dowager_regent" : "history_minister_regent");
            return;
        }

        isRegent = RealisticAgeSystem.IsMinor(emperor);
        if (data.powerful_minister_is_regent && !isRegent)
        {
            data.powerful_minister_regency_end_timestamp = GameClock.Now;
            data.last_powerful_minister_timestamp = GameClock.Now;
        }
        if (isRegent)
        {
            data.powerful_minister_regency_end_timestamp = -1L;
            if (!data.powerful_minister_is_regent) RecordMinisterTransition(candidate,
                isEmpressDowager ? "history_empress_dowager_regent" : "history_minister_regent");
        }
        data.powerful_minister_is_regent = isRegent;
        data.powerful_minister_is_empress_dowager = isEmpressDowager;
        data.powerful_minister_is_former_empress = isFormerEmpress;
        if (data.powerful_minister_emperor_id != emperor.Key)
        {
            bool isNewEmperor = data.powerful_minister_emperor_id > 0;
            data.powerful_minister_emperor_id = emperor.Key;
            if (isNewEmperor)
            {
                data.is_been_controlled = false;
                data.last_powerful_minister_timestamp = GameClock.Now;
            }
        }
        // Older saves have no stage date: begin a full cooldown rather than allowing a stage skip.
        if (data.powerful_minister_stage >= PowerfulMinisterStageDominant && data.powerful_minister_stage_timestamp < 0)
            data.powerful_minister_stage_timestamp = GameClock.Now;
        if (data.last_powerful_minister_timestamp < 0)
        {
            data.last_powerful_minister_timestamp = GameClock.Now;
            return;
        }
        int elapsedMonths = GameClock.MonthsSince(data.last_powerful_minister_timestamp);
        if (elapsedMonths < 1) return;
        data.last_powerful_minister_timestamp = GameClock.Now;
        bool hasSupport = HasPowerfulMinisterSupport(candidate, currentRegime);
        bool chiefAndLeader = GetCabinetLeader()?.Key == candidate.Key &&
            hasSupport && candidate.GetFaction()?.GetLeader()?.Key == candidate.Key;
        int monthlyChange = PowerfulMinisterRules.MonthlyChange(isRegent, chiefAndLeader, hasSupport,
            HasStrongEmperor(), IsRegencyEnding(), HasVulnerableNewEmperor(),
            PowerfulMinisterRules.IsMandateCollapsed(Mandate));
        // 庸弱、昏聩、荒淫之君在位，权臣(外戚)坐大更快(见 RulerQualitySystem)
        if (monthlyChange > 0) monthlyChange += RulerQualitySystem.MinisterBonus(emperor);
        // 一派独大(文臣专权、外戚干政等)，这一派的人做权臣更快，强势皇帝也压不太住(见 FactionBalanceSystem)
        monthlyChange += FactionBalanceSystem.MinisterBonus(this, candidate);
        monthlyChange = PowerfulMinisterRules.ApplyMandate(monthlyChange, Mandate);
        monthlyChange = PowerfulMinisterRules.ApplyEmpressDowagerRate(monthlyChange, isEmpressDowager);
        // 军机处：成年皇帝亲裁，权臣坐大慢到三分之一(见 LateImperialSystem)
        monthlyChange = LateImperialSystem.MinisterGrowth(this, monthlyChange, isRegent || isEmpressDowager);
        int previousProgress = data.powerful_minister_progress;
        data.powerful_minister_progress = PowerfulMinisterRules.Advance(data.powerful_minister_progress,
            candidate.Renown, elapsedMonths, monthlyChange, out int cost);
        candidate.Renown -= cost;
        EmpireCraftDebugProbe.Hit("powerful_minister.progress", () =>
            $"empire={GetEmpireFullName()}({id}), emperor={emperor.FullName()}({emperor.Key}), " +
            $"minister={candidate.FullName()}({candidate.Key}), progress={previousProgress}->" +
            $"{data.powerful_minister_progress}, elapsedMonths={elapsedMonths}, monthlyChange={monthlyChange}, " +
            $"renownCost={cost}, remainingRenown={candidate.Renown}, mandate={Mandate}, " +
            $"support={hasSupport}, chiefAndLeader={chiefAndLeader}, regent={isRegent}, " +
            $"empressDowager={isEmpressDowager}");
        if (data.is_been_controlled && data.powerful_minister_progress < PowerfulMinisterRules.ReleaseControlBelow)
        {
            data.is_been_controlled = false;
            RecordMinisterTransition(candidate, "history_minister_lost_control");
        }
        if (data.powerful_minister_progress < 100) return;
        if (data.is_been_controlled) return;
        if (data.powerful_minister_stage < PowerfulMinisterStageDominant)
            data.powerful_minister_stage = PowerfulMinisterStageDominant;
        data.powerful_minister_stage_timestamp = GameClock.Now;
        data.is_been_controlled = true;
        TranslateHelper.LogPowerfulMinisterControlsCourt(candidate, this);
        this.RecordHistory(directContent: string.Format(LM.Get("history_powerful_minister_controls_court"),
            candidate.FullName(), emperor?.FullName() ?? ""), actorId: candidate.Key);
        candidate.RecordPersonalHistory(string.Format(LM.Get("personal_history_powerful_minister_controls_court"),
            GetEmpireName()), relatedActorId: emperor?.Key ?? -1L);
        emperor?.RecordPersonalHistory(string.Format(LM.Get("personal_history_became_puppet"),
            candidate.FullName()), relatedActorId: candidate.Key);
    }

    private void RecordMinisterTransition(Person minister, string key)
    {
        string content = string.Format(LM.Get(key), minister.FullName(), Emperor?.FullName() ?? "", GetEmpireFullName());
        this.RecordHistory(directContent: content, actorId: minister.Key);
        minister.RecordPersonalHistory(content, relatedActorId: Emperor?.Key ?? -1L);
        Emperor?.RecordPersonalHistory(content, relatedActorId: minister.Key);
    }
#endif

    // 外戚(见 ConsortKinSystem)用到的太后与重置权臣
#if EC_PENDING
    public Person GetEmpressDowager() => GetPreferredEmpressDowager(out _);
#endif
    public void ResetPowerfulMinister() => ClearPowerfulMinister();

    private void ClearPowerfulMinister()
    {
        data.powerful_minister_is_consort_kin = false;
        data.powerful_minister_id = -1L;
        data.powerful_minister_identity_id = -1L;
        data.powerful_minister_progress = 0;
        data.powerful_minister_stage = PowerfulMinisterStageNone;
        data.powerful_minister_is_regent = false;
        data.powerful_minister_is_empress_dowager = false;
        data.powerful_minister_is_former_empress = false;
        data.powerful_minister_title_id = -1L;
        data.last_powerful_minister_timestamp = -1L;
        data.powerful_minister_stage_timestamp = -1L;
        data.powerful_minister_regency_end_timestamp = -1L;
        data.powerful_minister_emperor_id = -1L;
        data.is_been_controlled = false;
    }

#if EC_PENDING
    private bool CanAdvanceMinisterPlot(Person actor)
    {
        if (ConstitutionalSuccessionSystem.ThroneBound(this)) return false;
        Regime currentRegime = CoreKingdom?.GetRegime();
        if (!IsCurrentPowerfulMinister(actor) || !IsValidPowerfulMinister(actor) ||
            currentRegime?.type != RegimeType.LvLing || Emperor == null || Emperor.IsGone() ||
            data.powerful_minister_emperor_id != Emperor.Key ||
            data.powerful_minister_stage_timestamp < 0) return false;
        bool regencyEnding = IsRegencyEnding() || (data.powerful_minister_is_regent && !RealisticAgeSystem.IsMinor(Emperor));
        return PowerfulMinisterRules.CanAdvance(data.is_been_controlled, data.powerful_minister_progress,
            HasPowerfulMinisterSupport(actor, currentRegime), HasStrongEmperor(), regencyEnding,
            HasUsurpingDisposition(actor), GameClock.MonthsSince(data.powerful_minister_stage_timestamp),
            data.powerful_minister_stage == PowerfulMinisterStageKing,
            PowerfulMinisterRules.IsMandateCollapsed(Mandate));
    }
#endif

    // realize：亲子都是虚拟族人时落成一位(真要废立时)；只判断有没有人时不落成，见 HasDepositionHeir
#if EC_PENDING
    private Person GetEmpressDowagerPreferredSon(Person dowager, bool realize = false)
    {
        if (dowager == null || dowager.IsGone()) return null;
        PersonalClanIdentity dowagerIdentity = dowager.GetPersonalIdentity();
        bool IsEligibleSon(Person son) => son != null && !son.IsGone() && son.IsMale() &&
            son.Key != (Emperor?.Key ?? -1L) && son.Realm?.GetEmpire() == this;
        Person son = SpecificClanManager.getChildren(dowagerIdentity)
            .Select(item => item.Item2)
            .Where(identity => identity != null && identity.isMale() &&
                identity.mother == dowagerIdentity?.id && IsEligibleSon(identity._actor))
            .OrderBy(identity => identity.rank).ThenBy(identity => identity.id)
            .Select(identity => identity._actor).FirstOrDefault();
        if (son != null) return son;
        son = dowager.GetChildren().Where(IsEligibleSon)
            .Where(child => child.GetPersonalIdentity()?.mother == dowagerIdentity?.id ||
                child.GetParents().Any(parent => parent?.Key == dowager.Key))
            .OrderByDescending(child => child.IsAdult()).ThenByDescending(child => child.Age())
            .ThenBy(child => child.Key).FirstOrDefault();
        // 纯净地图下亲子多是虚拟族人：真要废立时才落成
        return son ?? (realize ? KinRoster.RealizeFirst(VirtualDowagerSons(dowagerIdentity), IsEligibleSon, 2) : null);
    }
#endif

    // 太后在族谱里的亲子(虚拟族人)：男子、在本帝国、不是今上
#if EC_PENDING
    private IEnumerable<PersonalClanIdentity> VirtualDowagerSons(PersonalClanIdentity dowager) =>
        dowager == null ? Enumerable.Empty<PersonalClanIdentity>()
            : KinRoster.Children(dowager).Where(person => KinRoster.Virtual(person) && person.sex == Sex.Male &&
                                                          person.mother == dowager.id &&
                                                          KinRoster.KingdomOf(person)?.GetEmpire() == this)
                .OrderBy(person => person.rank).ThenBy(person => person.id);
#endif

    // 宗室里在本帝国的成年男子(虚拟族人)，年长的在前
#if EC_PENDING
    private IEnumerable<PersonalClanIdentity> VirtualDynastMen(long excludeActor) =>
        KinRoster.Lineage(EmpireSpecificClan)
            .Where(person => KinRoster.Virtual(person) && person.sex == Sex.Male && person.age >= 18 &&
                             person.actor_id != excludeActor && KinRoster.KingdomOf(person)?.GetEmpire() == this)
            .OrderByDescending(person => person.age).ThenBy(person => person.id);
#endif

    // 后宫干政(用户 2026-10-15："为什么会存在妃子篡位""她们无法篡位，唯一能做的就是废立皇帝")：
    // 掌权的是女子(皇后、妃嫔、太后)。华夏一系里她们不能封公、加九锡、篡位，权势到顶只能废立——
    // 废掉当今皇帝，另立皇族的人(先立亲子，没有就挑宗室里的成年男子)
    public bool IsHaremPower(Person actor) => actor?.Record != null && actor.IsFemale();

#if EC_PENDING
    private bool HaremBarred(Person actor)
    {
        if (!IsHaremPower(actor) || CoreKingdom == null) return false;
        try
        {
            return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(CoreKingdom)) == "Huaxia";
        }
        catch
        {
            return false;
        }
    }
#endif

    // 废立另立的人：先亲子，没有就挑宗室里年长的成年男子(不是当今皇帝)
#if EC_PENDING
    private Person GetDepositionHeir(Person actor)
    {
        Person son = GetEmpressDowagerPreferredSon(actor, realize: true);
        if (son != null || !HaremBarred(actor)) return son;
        bool Fit(Person unit) => unit != null && !unit.IsGone() && unit.Alive && unit.IsMale() && unit.IsAdult() &&
                                unit.Key != (Emperor?.Key ?? -1L) && unit.Realm?.GetEmpire() == this &&
                                SpecificClanManager.SameLineage(unit.GetSpecificClan(), EmpireSpecificClan);
        return getUnits().Where(Fit).OrderByDescending(unit => unit.Age()).ThenBy(unit => unit.Key).FirstOrDefault() ??
               KinRoster.RealizeFirst(VirtualDynastMen(Emperor?.Key ?? -1L), Fit, 2);
    }
#endif

    // 有没有可废立另立的人(连虚拟族人一起看，不落成)：权臣图谋每轮都要问，不能每问一次就落成一批
#if EC_PENDING
    private bool HasDepositionHeir(Person actor)
    {
        if (GetEmpressDowagerPreferredSon(actor) != null) return true;
        if (VirtualDowagerSons(actor?.GetPersonalIdentity()).Any()) return true;
        if (!HaremBarred(actor)) return false;
        return getUnits().Any(unit => unit != null && !unit.IsGone() && unit.Alive && unit.IsMale() && unit.IsAdult() &&
                                      unit.Key != (Emperor?.Key ?? -1L) && unit.Realm?.GetEmpire() == this &&
                                      SpecificClanManager.SameLineage(unit.GetSpecificClan(), EmpireSpecificClan)) ||
               VirtualDynastMen(Emperor?.Key ?? -1L).Any();
    }

    public bool CanEmpressDowagerInstallSon(Person actor)
    {
        return (data.powerful_minister_is_former_empress && IsEmpressDowager(actor) || HaremBarred(actor)) &&
            data.powerful_minister_stage == PowerfulMinisterStageDominant &&
            CanAdvanceMinisterPlot(actor) && HasDepositionHeir(actor);
    }

    public bool CompleteEmpressDowagerInstallSon(Person dowager)
    {
        if (!CanEmpressDowagerInstallSon(dowager)) return false;
        Person son = GetDepositionHeir(dowager);
        Person deposedEmperor = Emperor;
        Realm coreKingdom = CoreKingdom;
        City capital = coreKingdom?.Capital;
        if (son == null || deposedEmperor == null || coreKingdom == null || capital == null) return false;

        TranslateHelper.LogEmpressDowagerInstallsSon(dowager, deposedEmperor, son);
        this.RecordHistory(directContent: string.Format(LM.Get("history_empress_dowager_installs_son"),
            dowager.FullName(), deposedEmperor.FullName(), son.FullName()), actorId: dowager.Key);
        deposedEmperor.RecordPersonalHistory(string.Format(
            LM.Get("personal_history_deposed_by_empress_dowager"), dowager.FullName(), son.FullName()),
            relatedActorId: dowager.Key);
        dowager.RecordPersonalHistory(string.Format(
            LM.Get("personal_history_empress_dowager_deposed_emperor"), deposedEmperor.FullName(), son.FullName()),
            relatedActorId: deposedEmperor.Key);
        son.RecordPersonalHistory(string.Format(
            LM.Get("personal_history_installed_by_empress_dowager"), dowager.FullName(), deposedEmperor.FullName()),
            relatedActorId: dowager.Key);

        Realm formerKingdom = son.Realm;
        if (son.IsRuler() && formerKingdom != coreKingdom) formerKingdom.RemoveRuler();
        if (coreKingdom.Ruler?.Key == deposedEmperor.Key) coreKingdom.RemoveRuler();
        son.JoinCity(capital);
        son.setKingdom(coreKingdom);
        // Palace deposition has its own outcome and must not trigger the generic dynasty-usurpation revolt.
        _completingMinisterUsurpation = true;
        try { coreKingdom.SetRuler(son); }
        finally { _completingMinisterUsurpation = false; }
        if (Emperor?.Key != son.Key) return false;
        ClearPowerfulMinister();
        return true;
    }
#endif

    // 逼宫废立(朝堂兵谏没有成员国可起兵时，见 CourtSystem.Escalate)：派系在都城逼今上退位，另立宗室里年长的成年男子
    // (先挑本派的)；宗室里没人就作罢。宫廷内部易位，不走篡位叛乱那一套；返回新君
#if EC_PENDING
    public Person CompleteFactionDeposition(FixedFaction faction)
    {
        Person deposed = Emperor;
        Realm core = CoreKingdom;
        City capital = core?.Capital;
        if (deposed == null || core == null || capital == null || faction == null) return null;
        Person heir = getUnits().Where(unit => unit != null && !unit.IsGone() && unit.Alive && unit.IsMale() &&
                                              unit.IsAdult() && unit.Key != deposed.Key && unit.Realm?.GetEmpire() == this &&
                                              SpecificClanManager.SameLineage(unit.GetSpecificClan(), EmpireSpecificClan))
            .OrderByDescending(unit => unit.GetFaction() == faction)
            .ThenByDescending(unit => unit.Age()).ThenBy(unit => unit.Key).FirstOrDefault();
        // 无小人模式下宗室可能都在虚拟族谱中；只把实际要迎立的人落成，不能把“没有实体”当作皇族绝嗣。
        if (heir == null && EmpireSpecificClan != null)
            foreach (PersonalClanIdentity person in SpecificClanManager.LineageClans(EmpireSpecificClan)
                         .SelectMany(clan => clan.SnapshotPeople())
                         .Where(person => person != null && person.is_alive && person.is_virtual && person.sex == Sex.Male &&
                             person.age >= 18 && person.actor_id != deposed.Key && person.virtual_city_id > 0 &&
                             EmpireWorld.Current.CityByKey(person.virtual_city_id)?.kingdom?.GetEmpire() == this)
                         .OrderByDescending(person => person.age).ThenBy(person => person.id))
            {
                Person candidate = person.Realize();
                if (candidate?.Record == null || !candidate.Alive || candidate.IsGone() || !candidate.IsAdult() ||
                    candidate.Realm?.GetEmpire() != this) continue;
                heir = candidate;
                break;
            }
        if (heir == null) return null;
        // 宪政继承受保护的、新君过不了继承规矩的不废(否则废了立不上，皇位落空)
        if (ConstitutionalSuccessionSystem.ThroneBound(this) && !ConstitutionalSuccessionSystem.CanInherit(this, heir)) return null;
        string content = string.Format(LM.Get("court_history_palace_coup"), faction.Name, deposed.FullName(), heir.FullName());
        TranslateHelper.LogEventOnly(content, core);
        this.RecordHistory(directContent: content, actorId: heir.Key);
        deposed.RecordPersonalHistory(content, relatedActorId: heir.Key);
        heir.RecordPersonalHistory(content, relatedActorId: deposed.Key);
        PrinceStruggleSystem.Note(core, heir, PrinceStruggleSystem.NotePalaceCoup);
        Realm formerKingdom = heir.Realm;
        if (heir.IsRuler() && formerKingdom != core) formerKingdom.RemoveRuler();
        if (core.Ruler?.Key == deposed.Key) core.RemoveRuler();
        heir.JoinCity(capital);
        heir.setKingdom(core);
        _completingMinisterUsurpation = true;
        try { core.SetRuler(heir); }
        finally { _completingMinisterUsurpation = false; }
        return Emperor?.Key == heir.Key ? heir : null;
    }

    public bool CanPowerfulMinisterSeekDukedom(Person actor)
    {
        return !data.powerful_minister_is_former_empress && !HaremBarred(actor) && CanAdvanceMinisterPlot(actor) &&
            data.powerful_minister_stage == PowerfulMinisterStageDominant && !actor.HasVirtualEnfeoff(this) &&
            FindMinisterDukedomTarget() != null;
    }

    private KingdomTitle FindMinisterDukedomTarget()
    {
        // Occupied royal titles are taken before vacant titles; never dispossess another non-royal duke.
        // Scan residents once, not once per legal title during plot eligibility checks.
        var livingHolders = new Dictionary<long, Person>();
        foreach (Person holder in getUnits())
        {
            if (holder == null || holder.IsGone() || !holder.HasVirtualEnfeoff(this)) continue;
            long titleId = holder.GetOrCreate().virtual_enfeoff_title_id;
            if (!livingHolders.ContainsKey(titleId)) livingHolders[titleId] = holder;
        }
        Person FindHolder(long titleId)
        {
            if (livingHolders.TryGetValue(titleId, out Person holder)) return holder;
            if (data.legal_peerage_holders?.TryGetValue(titleId, out long actorId) != true) return null;
            holder = EmpireWorld.Current.PersonByKey(actorId);
            return holder != null && !holder.IsGone() && holder.HasVirtualEnfeoff(this) &&
                holder.GetOrCreate().virtual_enfeoff_title_id == titleId ? holder : null;
        }
        var titles = GetLegalPeerageTitles()
            .Where(title => GetLandedLegalTitleKingdom(title) == null)
            .Select(title => new
        {
            Title = title,
            Holder = FindHolder(title.Key)
        }).ToList();
        return titles.Where(item => item.Holder != null && item.Holder != Emperor &&
                EmpireSpecificClan != null && SpecificClanManager.SameLineage(item.Holder.GetSpecificClan(), EmpireSpecificClan))
            .OrderBy(item => item.Holder.Renown).ThenBy(item => item.Title.Key)
            .Select(item => item.Title).FirstOrDefault() ??
            titles.Where(item => item.Holder == null).OrderBy(item => item.Title.Key)
                .Select(item => item.Title).FirstOrDefault();
    }

    public bool TryGrantPowerfulMinisterDukedom(Person actor)
    {
        if (!CanPowerfulMinisterSeekDukedom(actor)) return false;
        KingdomTitle title = FindMinisterDukedomTarget();
        if (title == null) return false;
        Person displaced = GetLivingLegalPeerageHolder(title.Key);
        string displacedTitle = displaced?.GetPeerageDisplayName();
        var displacedData = displaced?.GetOrCreate();
        PeeragesLevel previousLevel = displaced?.GetPeeragesLevel() ?? PeeragesLevel.peerages_6;
        if (displacedData != null) displacedData.virtual_enfeoff = false;
        bool granted = false;
        try
        {
            granted = AssignLegalPeerage(actor, title, "tang_peerage_guogong",
                PeeragesLevel.peerages_3, -10, relocate: false);
        }
        finally
        {
            if (!granted && displacedData != null) displacedData.virtual_enfeoff = true;
        }
        if (!granted) return false;
        if (displacedData != null)
        {
            displacedData.virtual_enfeoff_empire_id = -1L;
            displacedData.virtual_enfeoff_title_id = -1L;
            displacedData.virtual_enfeoff_peerage_key = "";
            if (previousLevel == PeeragesLevel.peerages_2 && !displaced.IsRuler() &&
                displaced.GetOwnedTitle().Count == 0) displaced.SetPeeragesLevel(PeeragesLevel.peerages_6);
            string transfer = string.Format(LM.Get("history_minister_dispossessed_royal"),
                displaced.FullName(), displacedTitle, actor.FullName());
            this.RecordHistory(directContent: transfer, actorId: displaced.Key);
            displaced.RecordPersonalHistory(transfer, relatedActorId: actor.Key);
            actor.RecordPersonalHistory(transfer, relatedActorId: displaced.Key);
        }
        data.powerful_minister_title_id = title.Key;
        data.powerful_minister_stage = PowerfulMinisterStageDuke;
        data.powerful_minister_stage_timestamp = GameClock.Now;
        string peerageName = actor.GetPeerageDisplayName();
        TranslateHelper.LogPowerfulMinisterAcquireTitle(actor, this, peerageName);
        this.RecordHistory(directContent: string.Format(LM.Get("history_powerful_minister_duke"),
            actor.FullName(), peerageName), actorId: actor.Key);
        actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_powerful_minister_duke"), peerageName));
        return true;
    }

    public bool CanPowerfulMinisterReceiveNineBestowments(Person actor)
    {
        return !data.powerful_minister_is_former_empress && !HaremBarred(actor) && CanAdvanceMinisterPlot(actor) &&
            data.powerful_minister_stage == PowerfulMinisterStageDuke &&
            actor.HasVirtualEnfeoff(this) && actor.GetOrCreate().virtual_enfeoff_title_id == data.powerful_minister_title_id;
    }

    public bool GrantPowerfulMinisterNineBestowments(Person actor)
    {
        if (!CanPowerfulMinisterReceiveNineBestowments(actor)) return false;
        var actorData = actor.GetOrCreate();
        actorData.virtual_enfeoff_peerage_key = "default_peerages_2";
        actor.SetPeeragesLevel(PeeragesLevel.peerages_2);
        data.legal_peerage_types[data.powerful_minister_title_id] = "default_peerages_2";
        data.powerful_minister_stage = PowerfulMinisterStageKing;
        data.powerful_minister_stage_timestamp = GameClock.Now;
        AddMandate(-20);
        string peerageName = actor.GetPeerageDisplayName();
        TranslateHelper.LogPowerfulMinisterNineBestowments(actor, this, peerageName);
        this.RecordHistory(directContent: string.Format(LM.Get("history_powerful_minister_nine_bestowments"),
            actor.FullName(), peerageName), actorId: actor.Key);
        actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_powerful_minister_nine_bestowments"),
            peerageName));
        ResolveMinisterOpposition(actor);
        return true;
    }
#endif

    private static bool HasUsurpingDisposition(Person actor)
    {
        return actor != null && (actor.HasTrait("evil") || actor.HasTrait("ambitious"));
    }

#if EC_PENDING
    public bool CanPowerfulMinisterUsurp(Person actor)
    {
        // 开国气象(统一战争期间、统一后三十年)权臣不篡位(见 UnificationSystem)
        if (EmpireCraft.Core.UnificationSystem.InGrace(CoreKingdom)) return false;
        if (data.powerful_minister_is_former_empress || HaremBarred(actor) || !CanAdvanceMinisterPlot(actor) ||
            data.powerful_minister_stage != PowerfulMinisterStageKing ||
            data.powerful_minister_title_id <= 0) return false;
        return actor.HasVirtualEnfeoff(this) &&
            actor.GetOrCreate().virtual_enfeoff_title_id == data.powerful_minister_title_id;
    }

    public int GetMinisterOppositionPenalty()
    {
        Person minister = data.minister_usurper_id == Emperor?.Key ? Emperor :
            data.is_been_controlled && data.powerful_minister_stage == PowerfulMinisterStageKing
                ? GetPowerfulMinister() : null;
        return minister == null || minister.IsGone() ? 0 : PowerfulMinisterRules.OppositionPenalty(minister.Renown);
    }

    private City GetMinisterOpponentPowerBase(Person actor)
    {
        if (actor == null || actor.IsGone() || !actor.IsAdult() || actor == Emperor ||
            actor.Realm?.GetEmpire() != this) return null;
        bool IsLocal(City city) => city != null && !city.IsGone() && city != CoreKingdom?.Capital &&
            city.Realm?.GetEmpire() == this;
        if (actor.IsRuler() && actor.Realm != CoreKingdom && IsLocal(actor.Realm.Capital))
            return actor.Realm.Capital;
        OfficeObject office = actor.GetOffice();
        if (office != null && office.is_local && office.actor_id == actor.Key)
        {
            City seat = office.meta_object is City city ? city : (office.meta_object as Realm)?.Capital;
            if (IsLocal(seat)) return seat;
        }
        // An honorary/virtual title alone does not command any land or army.
        return actor.GetOwnedTitle().Select(id => ModClass.KINGDOM_TITLE_MANAGER.Get(id))
            .Where(title => title != null && !title.IsGone() && title.owner == actor)
            .SelectMany(title => title.getCities()).Where(IsLocal).OrderBy(city => city.Key).FirstOrDefault();
    }

    private void ResolveMinisterOpposition(Person minister, int? influence = null)
    {
        if (EmpireCraft.Core.UnificationSystem.InGrace(CoreKingdom)) return;
        // 不许自立(见 SecessionGate)：成员国君领头时先开战后起事，要在开战前问
        if (SecessionGate.Forbids("逼反权臣", data?.name)) return;
        int ministerInfluence = influence ?? minister.Renown;
        if (!PowerfulMinisterRules.ShouldRiseAgainstMinister(ministerInfluence, 501, true)) return;
        // Snapshot before creating kingdoms: each successful revolt changes empire membership.
        var opponents = getUnits().Where(actor => actor != null && actor != minister &&
                !actor.IsGone() && PowerfulMinisterRules.ShouldRiseAgainstMinister(ministerInfluence, actor.Renown, true))
            .Distinct().Select(actor => new { Person = actor, City = GetMinisterOpponentPowerBase(actor) })
            .Where(item => item.City != null).OrderByDescending(item => item.Actor.renown)
            .ThenBy(item => item.Actor.id).ToList();
        foreach (var opponent in opponents)
        {
            Person leader = opponent.Actor;
            City seat = opponent.City;
            if (leader.IsGone() || seat.IsGone() || seat.Realm?.GetEmpire() != this ||
                leader.Realm?.GetEmpire() != this || seat == CoreKingdom?.Capital) continue;
            Realm rebels = leader.IsRuler() && leader.Realm != CoreKingdom ? leader.Realm : null;
            if (rebels != null && (rebels.IsLocalRebelling() || rebels.IsFactionRebelling() ||
                rebels.IsAtWarWith(CoreKingdom))) continue;
            if (rebels == null)
            {
                leader.JoinCity(seat);
                rebels = seat.makeOwnKingdom(leader, pRebellion: true);
            }
            if (rebels == null || rebels == CoreKingdom) continue;
            rebels.GetRegime()?.SetAllowArmy(true);
            rebels.GetRegime()?.SetAllowSupportCenterArmy(false);
            War war = EmpireWorld.Current.StartWar(rebels, CoreKingdom, WarKinds.Rebellion);
            if (war == null) continue;
            war.SetEmpireWarType(EmpireWarType.地方叛乱, pre: CoreKingdom.Name,
                nanoObject: this, belongingFaction: leader.GetFaction());
            if (rebels.GetEmpire() == this) leave(rebels, true, true);
            rebels.StartLocalRebelling(EmpireWarType.地方叛乱);
            string content = string.Format(LM.Get("history_minister_opposition_rebellion"),
                leader.FullName(), minister.FullName());
            this.RecordHistory(directContent: content, actorId: leader.Key);
            leader.RecordPersonalHistory(content, relatedActorId: minister.Key);
            minister.RecordPersonalHistory(content, relatedActorId: leader.Key);
        }
    }

    public bool CompletePowerfulMinisterUsurpation(Person actor)
    {
        if (!CanPowerfulMinisterUsurp(actor)) return false;
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.Get(data.powerful_minister_title_id);
        Realm coreKingdom = CoreKingdom;
        City capital = coreKingdom?.Capital;
        if (title == null || coreKingdom == null || capital == null) return false;

        string newEmpireName = title.data.name;
        string oldEmpireName = GetEmpireName();
        Person previousEmperor = Emperor;
        int ministerInfluence = actor.Renown;
        TranslateHelper.LogPowerfulMinisterUsurpation(actor, this, newEmpireName);
        this.RecordHistory(directContent: string.Format(LM.Get("history_powerful_minister_usurpation"),
            actor.FullName(), newEmpireName), actorId: actor.Key);
        previousEmperor?.RecordPersonalHistory(string.Format(LM.Get("personal_history_deposed_by_powerful_minister"),
            actor.FullName()), relatedActorId: actor.Key);
        actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_powerful_minister_usurpation"),
            oldEmpireName, newEmpireName), relatedActorId: previousEmperor?.Key ?? -1L);

        long titleId = data.powerful_minister_title_id;
        var actorData = actor.GetOrCreate();
        actorData.virtual_enfeoff = false;
        actorData.virtual_enfeoff_empire_id = -1L;
        actorData.virtual_enfeoff_title_id = -1L;
        actorData.virtual_enfeoff_peerage_key = "";
        data.legal_peerage_holders?.Remove(titleId);
        data.legal_peerage_holder_identities?.Remove(titleId);
        data.legal_peerage_types?.Remove(titleId);

        if (previousEmperor != null && previousEmperor.Key != actor.Key)
        {
            coreKingdom.RemoveRuler();
        }
        actor.JoinCity(capital);
        actor.setKingdom(coreKingdom);
        // This path has its own influence-based opposition, not the generic dynasty-change rebellion.
        _completingMinisterUsurpation = true;
        EmpireCraft.Core.PrinceStruggleSystem.Note(coreKingdom, actor, EmpireCraft.Core.PrinceStruggleSystem.NoteUsurp);
        try { coreKingdom.SetRuler(actor); }
        finally { _completingMinisterUsurpation = false; }
        data.directPre = "";
        SetEmpireName(newEmpireName);
        if (data.currentHistory != null)
        {
            data.currentHistory.empire_name = newEmpireName;
            data.currentHistory.dynasty_name = newEmpireName;
        }
        if (Emperor?.Key != actor.Key) return false;
        data.minister_usurper_id = actor.Key;
        ResolveMinisterOpposition(actor, ministerInfluence);
        ClearPowerfulMinister();
        return Emperor?.Key == actor.Key;
    }

    private Person GetLegalPeerageCandidate(KingdomTitle title, out PersonalClanIdentity predecessor)
    {
        predecessor = null;
        bool IsAvailable(Person actor) => actor != null && !actor.IsGone() && !actor.IsRuler() &&
            !actor.HasVirtualEnfeoff(this) && actor.Realm?.GetEmpire() == this &&
            actor.Key != (CoreKingdom?.GetHeir()?.Key ?? -1L);
        bool IsDynasticHeir(PersonalClanIdentity identity, PersonalClanIdentity source) =>
            identity?._specificClan != null && SpecificClanManager.SameLineage(identity._specificClan, EmpireSpecificClan) &&
            identity.CanHeir(source) && IsAvailable(identity._actor);

        data.legal_peerage_types ??= new Dictionary<long, string>();
        data.legal_peerage_holder_identities ??= new Dictionary<long, long>();
        if (data.legal_peerage_types.TryGetValue(title.Key, out string previousType) &&
            previousType == "default_peerages_2")
        {
            data.legal_peerage_holder_identities.TryGetValue(title.Key, out long previousIdentityId);
            data.legal_peerage_holders.TryGetValue(title.Key, out long previousActorId);
            PersonalClanIdentity previousHolder = ResolvePeerageIdentity(previousActorId, previousIdentityId);
            Person branchHeir = FindPeerageDescendant(previousHolder,
                actor => IsAvailable(actor) && SpecificClanManager.SameLineage(actor.GetSpecificClan(), EmpireSpecificClan));
            if (branchHeir != null)
            {
                predecessor = previousHolder;
                return branchHeir;
            }
        }

        return GetLegalPeerageCrownCandidate();
    }
#endif

    // 朝廷另选王爵人选：皇帝的兄弟优先，其次太子以外的皇子。excludeId 用于诸侯请封被驳回时
    // 排除原嗣君。
#if EC_PENDING
    private Person GetLegalPeerageCrownCandidate(long excludeId = -1L)
    {
        bool IsAvailable(Person actor) => actor != null && !actor.IsGone() && !actor.IsRuler() &&
            !actor.HasVirtualEnfeoff(this) && actor.Realm?.GetEmpire() == this &&
            actor.Key != (CoreKingdom?.GetHeir()?.Key ?? -1L) && actor.Key != excludeId;
        bool IsDynasticHeir(PersonalClanIdentity identity, PersonalClanIdentity source) =>
            identity?._specificClan != null && SpecificClanManager.SameLineage(identity._specificClan, EmpireSpecificClan) &&
            identity.CanHeir(source) && IsAvailable(identity._actor);

        PersonalClanIdentity emperorIdentity = Emperor?.GetPersonalIdentity();
        Person sibling = SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
            .Select(item => item.Item2)
            .Where(identity => IsDynasticHeir(identity, emperorIdentity))
            .OrderBy(identity => identity.rank)
            .Select(identity => identity._actor)
            .FirstOrDefault();
        if (sibling != null) return sibling;

        long crownPrinceId = CoreKingdom?.GetHeir()?.Key ?? -1L;
        Person son = SpecificClanManager.getChildren(emperorIdentity)
            .Select(item => item.Item2)
            .Where(identity => IsDynasticHeir(identity, emperorIdentity) && identity.actor_id != crownPrinceId)
            .OrderBy(identity => identity.rank)
            .Select(identity => identity._actor)
            .FirstOrDefault();
        if (son != null || emperorIdentity == null) return son;
        // 纯净地图下皇帝的兄弟、皇子多是虚拟族人(以前只认地图上的，王爵常年空着没人封)：
        // 照同样的先后(兄弟→皇子，长幼)挑成年的落成，不合格的并回人口
        bool VirtualDynast(PersonalClanIdentity identity, PersonalClanIdentity source) =>
            KinRoster.Virtual(identity) && identity._specificClan != null &&
            SpecificClanManager.SameLineage(identity._specificClan, EmpireSpecificClan) && identity.CanHeir(source) &&
            identity.age >= KinRoster.AdultAge && KinRoster.KingdomOf(identity)?.GetEmpire() == this;
        IEnumerable<PersonalClanIdentity> virtualKin = KinRoster.Siblings(emperorIdentity)
            .Where(identity => VirtualDynast(identity, emperorIdentity)).OrderBy(identity => identity.rank)
            .Concat(KinRoster.Children(emperorIdentity).Where(identity => VirtualDynast(identity, emperorIdentity))
                .OrderBy(identity => identity.rank));
        return KinRoster.RealizeFirst(virtualKin, IsAvailable, 2);
    }

    private bool GrantLegalPeerage(Person actor, KingdomTitle title, PersonalClanIdentity predecessor = null)
    {
        if (actor == null || title == null || GetLandedLegalTitleKingdom(title) != null ||
            GetLivingLegalPeerageHolder(title.Key) != null) return false;
        bool isRoyal = actor.GetSpecificClan() != null && SpecificClanManager.SameLineage(actor.GetSpecificClan(), EmpireSpecificClan);
        if (!isRoyal) return false;
        if (!AssignLegalPeerage(actor, title, "default_peerages_2", PeeragesLevel.peerages_2,
                predecessor == null ? 10 : 0)) return false;
        if (predecessor == null)
            TranslateHelper.LogPeerageGranted(actor, this, actor.GetPeerageDisplayName());
        else
            TranslateHelper.LogPeerageInherited(actor, this, actor.GetPeerageDisplayName(), predecessor,
                predecessor.actor_id, predecessor.name);
        return true;
    }

    private bool AssignLegalPeerage(Person actor, KingdomTitle title, string peerageKey,
        PeeragesLevel peeragesLevel, int mandateChange, bool relocate = true)
    {
        if (actor == null || title == null || GetLandedLegalTitleKingdom(title) != null ||
            GetLivingLegalPeerageHolder(title.Key) != null) return false;
        actor.CheckSpecificClan(false);
        var actorData = actor.GetOrCreate();
        actorData.virtual_enfeoff = true;
        actorData.virtual_enfeoff_empire_id = data.id;
        actorData.virtual_enfeoff_title_id = title.Key;
        actorData.virtual_enfeoff_peerage_key = peerageKey;
        actor.SetPeeragesLevel(peeragesLevel);

        data.legal_peerage_holders ??= new Dictionary<long, long>();
        data.legal_peerage_holder_identities ??= new Dictionary<long, long>();
        data.legal_peerage_types ??= new Dictionary<long, string>();
        data.legal_peerage_holders[title.Key] = actor.Key;
        data.legal_peerage_holder_identities[title.Key] = actor.GetPersonalIdentity()?.id ?? -1L;
        data.legal_peerage_types[title.Key] = peerageKey;

        City destination = title.title_capital;
        if (relocate && destination != null && !destination.IsGone() && destination.Realm?.GetEmpire() == this)
        {
            actor.JoinCity(destination);
            actor.goTo(destination._city_tile);
        }
        AddMandate(mandateChange);
        return true;
    }

    private void ProcessLegalPeerageSuccession()
    {
        data.legal_peerage_holders ??= new Dictionary<long, long>();
        data.legal_peerage_holder_identities ??= new Dictionary<long, long>();
        data.legal_peerage_types ??= new Dictionary<long, string>();
        data.legal_peerage_kingdoms ??= new Dictionary<long, long>();
        List<KingdomTitle> legalTitles = GetLegalPeerageTitles();
        foreach (KingdomTitle title in legalTitles)
        {
            Realm landedKingdom = GetLandedLegalTitleKingdom(title);
            if (landedKingdom != null) SynchronizeLandedLegalTitle(title, landedKingdom);
        }
        HashSet<long> validTitleIds = legalTitles.Select(title => title.Key).ToHashSet();
        foreach (long obsoleteTitleId in data.legal_peerage_holders.Keys.Where(id => !validTitleIds.Contains(id)).ToList())
        {
            data.legal_peerage_holders.Remove(obsoleteTitleId);
            data.legal_peerage_holder_identities.Remove(obsoleteTitleId);
            data.legal_peerage_types.Remove(obsoleteTitleId);
            data.legal_peerage_kingdoms.Remove(obsoleteTitleId);
        }

        foreach (Person holder in getUnits().Where(actor => actor != null && !actor.IsGone() && actor.HasVirtualEnfeoff(this)))
        {
            var holderData = holder.GetOrCreate();
            if (!validTitleIds.Contains(holderData.virtual_enfeoff_title_id)) continue;
            if (string.IsNullOrWhiteSpace(holderData.virtual_enfeoff_peerage_key)) holder.GetPeerageDisplayName();
            data.legal_peerage_holders[holderData.virtual_enfeoff_title_id] = holder.Key;
            data.legal_peerage_holder_identities[holderData.virtual_enfeoff_title_id] = holder.GetPersonalIdentity()?.id ?? -1L;
            data.legal_peerage_types[holderData.virtual_enfeoff_title_id] = holderData.virtual_enfeoff_peerage_key;
        }

        foreach (KingdomTitle title in legalTitles)
        {
            if (Emperor != null && title.owner == Emperor &&
                Emperor.GetOwnedTitle()?.Contains(title.Key) == true) continue;
            if (GetLandedLegalTitleKingdom(title) != null) continue;
            if (GetLivingLegalPeerageHolder(title.Key) != null) continue;
            // New legal titles have no saved holder/type yet. They are royal peerages by default;
            // non-royal dukedoms keep their separate powerful-minister succession rules.
            if (data.legal_peerage_types.TryGetValue(title.Key, out string peerageType) &&
                peerageType != "default_peerages_2") continue;
            Person successor = GetLegalPeerageCandidate(title, out PersonalClanIdentity predecessor);
            // 支系承袭(有前任藩王)要过一遍册封判定：封国势大时是请封/自立，朝廷可能驳回改立
            if (successor != null && predecessor != null)
                successor = VassalInvestitureService.ResolveVirtualSuccession(this, title, successor,
                    GetLegalPeerageCrownCandidate(successor.Key), ref predecessor);
            if (successor != null) GrantLegalPeerage(successor, title, predecessor);
        }
    }

    public void AutoEnfeoff()
    {
        Realm coreKingdom = CoreKingdom;
        if (data == null || coreKingdom == null || coreKingdom.IsGone()) return;
        if (AdministrationReorganizationSystem.IsRunning(this)) return;
        // 战时照样划分，只跳过前线的城、战时代管的城(见 EnfeoffmentHelper.DeferCity)；跳过了就挂起，
        // 下一趟(每月一次，见 ProcessTerritorialAcquisitionEnfeoff)离了前线再划
        bool atWar = HasTerritorialWar();
        data.territorial_enfeoff_pending = false;
        int moves = _partitionMoves;
        Regime regime = coreKingdom.GetRegime();
        if (regime == null) return;
        // 穿越政体不分封：共同纲领以前执委会直辖，以后只划大区
        if (regime.type == RegimeType.Transmigrant && !TransmigrantSystem.Partitions(this)) return;

        // LvLing's unlanded peerages do not replace its territorial circuits.
        if (regime?.type == RegimeType.LvLing)
        {
            AutoEstablishLvLingAdministrativeDivisions(atWar);
            RedrawAfterPartition(atWar, moves);
            return;
        }
        if (regime != null && regime.enfeoff_virtual_only)
        {
            data.last_legal_peerage_timestamp = -1L;
            return;
        }
        // 西方中央集权君主制/神权国家：新得的土地不再分封，改设辖区/教区；游牧要施行了行省制度、古典共和要施行了元首制才设行省
        // 幕府：打下的地设天领，由奉行直辖(以前从不生成天领)
        bool provinces = RegimeSupportsProvince(regime) && (regime.type != RegimeType.YouMu || IsNomadProvincial()) &&
                         (regime.type != RegimeType.ClassicalRepublic || IsPrincipate()) || regime.type == RegimeType.Bakufu ||
                         regime.type == RegimeType.Transmigrant;
        AutoEstablishTerritorialDivisions(regime, provinces || IsWesternCentralized(), atWar);
        RedrawAfterPartition(atWar, moves);
    }
#endif

    // 划分时城易主的次数，用来判断这趟有没有动过版图
    private int _partitionMoves;

    // 每月都会再来一趟：这趟没有城易主就不重画地图(以前太平时不论动没动都整张重画，帝国一多，每月十几次)
#if EC_PENDING
    private void RedrawAfterPartition(bool atWar, int movesBefore)
    {
        if (_partitionMoves != movesBefore) EmpireWorld.Current.zone_calculator.dirtyAndClear();
    }
#endif

    // 这趟划分要跳过的城(见 EnfeoffmentHelper.DeferCity)；跳过了就挂起，等下一趟
#if EC_PENDING
    private bool DeferPartition(City city, bool atWar)
    {
        if (!EnfeoffmentHelper.DeferCity(city, CoreKingdom, atWar)) return false;
        data.territorial_enfeoff_pending = true;
        return true;
    }

    private void ProcessTerritorialAcquisitionEnfeoff()
    {
        if (data == null || CoreKingdom?.Cities == null || EmpireWorld.Current == null) return;
        double now = GameClock.Now;
        if (data.last_auto_enfeoff_check_timestamp >= 0 &&
            GameClock.MonthsSince(data.last_auto_enfeoff_check_timestamp) < 1) return;
        data.last_auto_enfeoff_check_timestamp = now;
        // 以前帝国里任何一国有敌人就整个挂起，常年打仗的帝国打下的地从来划不出去。现在战时照样处理，
        // 只是前线的城、战时代管的城先不动(见 AutoEnfeoff、EnfeoffmentHelper.DeferCity)
        bool atWar = HasTerritorialWar();

        FeudalConquestService.ProcessPeaceEnfeoff(this);
        EnfeoffmentHelper.ResumeSuccessionEnfeoff(this);
        List<City> coreCities = CoreKingdom.Cities
            .Where(city => city != null && !city.IsGone() && city.Realm == CoreKingdom).ToList();
        HashSet<long> currentCities = coreCities.Select(city => city.Key).ToHashSet();
        List<KingdomTitle> completeTitles = ModClass.KINGDOM_TITLE_MANAGER
            .Where(title => title != null && !title.IsGone())
            .Where(title =>
            {
                List<City> cities = title.getCities()
                    .Where(city => city != null && !city.IsGone()).Distinct().ToList();
                return cities.Count > 0 && cities.All(city => city.Realm?.GetEmpire() == this);
            }).ToList();
        HashSet<long> currentCompleteTitles = completeTitles.Select(title => title.Key).ToHashSet();

        data.auto_enfeoff_observed_city_ids ??= new List<long>();
        data.auto_enfeoff_observed_title_ids ??= new List<long>();
        if (!data.auto_enfeoff_tracking_initialized)
        {
            data.auto_enfeoff_observed_city_ids = currentCities.OrderBy(id => id).ToList();
            data.auto_enfeoff_observed_title_ids = currentCompleteTitles.OrderBy(id => id).ToList();
            data.auto_enfeoff_tracking_initialized = true;
            if (data.territorial_enfeoff_pending) AutoEnfeoff();
            return;
        }

        bool acquiredCity = currentCities.Except(data.auto_enfeoff_observed_city_ids).Any();
        bool completedTitle = currentCompleteTitles.Except(data.auto_enfeoff_observed_title_ids).Any();
        // 新得的城、新凑齐的法理还在前线(或战时代管)的不记作已见：离了前线，下一趟照样当新得的地处理
        HashSet<long> seenCities = data.auto_enfeoff_observed_city_ids.ToHashSet();
        HashSet<long> seenTitles = data.auto_enfeoff_observed_title_ids.ToHashSet();
        HashSet<long> deferredCities = coreCities
            .Where(city => !seenCities.Contains(city.Key) && EnfeoffmentHelper.DeferCity(city, CoreKingdom, atWar))
            .Select(city => city.Key).ToHashSet();
        HashSet<long> deferredTitles = completeTitles
            .Where(title => !seenTitles.Contains(title.Key) && EnfeoffmentHelper.TitleDeferred(title, CoreKingdom, atWar))
            .Select(title => title.Key).ToHashSet();
        if (deferredCities.Count > 0 || deferredTitles.Count > 0) data.territorial_enfeoff_pending = true;
        data.auto_enfeoff_observed_city_ids = currentCities.Where(id => !deferredCities.Contains(id))
            .OrderBy(id => id).ToList();
        data.auto_enfeoff_observed_title_ids = currentCompleteTitles.Where(id => !deferredTitles.Contains(id))
            .OrderBy(id => id).ToList();
        // 郡县制/中央集权/神权国家：核心王国除开国法理外还直辖别的法理时，一律划出行政区(同"普天之下")
        if (data.territorial_enfeoff_pending || acquiredCity || completedTitle || HoldsExtraDirectTitles()) AutoEnfeoff();
    }
#endif

    public bool HasTerritorialWar() => kingdoms_list.Any(kingdom => kingdom != null && !kingdom.IsGone() && kingdom.HasEnemies());

#if EC_PENDING
    private bool HoldsExtraDirectTitles()
    {
        HashSet<long> protectedCities = GetProtectedDirectCityIds();
        return CoreKingdom.Cities.Any(city => IsDirectPartitionCandidate(city, protectedCities) &&
                                              city.hasTitle() && city.GetTitle() is { } title && !title.IsGone());
    }

    private void AutoEstablishLvLingAdministrativeDivisions(bool atWar)
    {
        AutoEstablishTerritorialDivisions(CoreKingdom?.GetRegime(), createAdministration: true, atWar);
    }

    private void AutoEstablishTerritorialDivisions(Regime regime, bool createAdministration, bool atWar)
    {
        Realm coreKingdom = CoreKingdom;
        if (regime == null || coreKingdom?.Cities == null || coreKingdom.Cities.Count == 0) return;

        HashSet<long> protectedCities = GetProtectedDirectCityIds();
        List<KingdomTitle> titles = coreKingdom.Cities.ToList()
            .Where(city => IsDirectPartitionCandidate(city, protectedCities) && city.hasTitle())
            .Select(city => city.GetTitle())
            .Where(title => title != null && !title.IsGone())
            .Distinct().OrderBy(title => title.Key).ToList();

        // Existing grants are authoritative. Complete their de jure territory before creating anything new.
        foreach (KingdomTitle title in titles)
        {
            List<Realm> existingPartitions = FindExistingTitlePartitions(title);
            if (existingPartitions.Count > 0)
                CompleteTitlePartition(title, existingPartitions, protectedCities, atWar);
        }

        foreach (KingdomTitle title in titles)
        {
            if (!GetPartitionTitleCities(title)
                    .Any(city => IsDirectPartitionCandidate(city, protectedCities))) continue;

            List<Realm> existingPartitions = FindExistingTitlePartitions(title);
            // 没有现成的行政区、封国可归的城：新设一片。已有行政区的治所都隔着长城的(见 FrontierWalls)，行政区也在长城这边另设一片、共用这个法理
            List<City> region = GetPartitionTitleCities(title)
                .Where(city => IsDirectPartitionCandidate(city, protectedCities) &&
                               (existingPartitions.Count == 0 || existingPartitions.All(partition => partition.Capital != null &&
                                   WorldMap.FrontierWalls.Separates(city, partition.Capital)))).ToList();
            if (region.Count > 0)
            {
                Regime partitionRegime = regime;
                bool partitionAsAdministration = createAdministration;
                string titleCulture = CultureService.GetEffectiveTitleCulture(title);
                if (CompositeEmpireService.IsComposite(this) &&
                    CompositeEmpireService.TryGetCultureRegime(titleCulture, out _, out Regime localRegime))
                {
                    partitionRegime = localRegime;
                    partitionAsAdministration = RegimeSupportsProvince(localRegime);
                }
                // 行省为主也有封授：元首制的异族边地立附庸王国、集权君主制给亲王封地(见 PartialEnfeoffment)
                Person grantee = null;
                if (partitionAsAdministration && existingPartitions.Count == 0 &&
                    PartialEnfeoffment.TryPick(this, title, region, ref partitionRegime, out grantee))
                    partitionAsAdministration = false;
                // 行政区不跨长城：先设法理首府那边的一片，长城那边的下一趟另设(封国照旧整片法理一起封)
                if (partitionAsAdministration)
                    region = WorldMap.FrontierWalls.Split(region.OrderBy(city => city == title.title_capital ? 0 : 1)).First();
                // 新设的行政区、封国以整片法理为单位：还有城在前线就整片等下一趟，免得治所落到别的城上
                if ((existingPartitions.Count == 0 || partitionAsAdministration) && !region.Any(city => DeferPartition(city, atWar)))
                {
                    Realm created = CreateTerritorialPartition(region, title, partitionRegime,
                        partitionAsAdministration, grantee);
                    if (created != null) existingPartitions.Add(created);
                    if (created != null && grantee != null) PartialEnfeoffment.MarkGranted(this, created);
                }
            }
            if (existingPartitions.Count > 0)
                CompleteTitlePartition(title, existingPartitions, protectedCities, atWar);
        }

        AutoEstablishUntitledExclaveDivisions(regime, createAdministration, protectedCities, atWar);
    }

    public HashSet<long> GetProtectedDirectCityIds()
    {
        var protectedCities = new HashSet<long>();
        City capital = CoreKingdom?.Capital;
        if (capital != null && !capital.IsGone()) protectedCities.Add(capital.Key);

        // 开国法理和迁都后的首都法理都保留，不能只保护其中一个。
        foreach (KingdomTitle foundingTitle in new[] { CoreKingdom?.GetMainTitle(), capital?.GetTitle() }.Distinct())
        {
            if (foundingTitle == null || foundingTitle.IsGone()) continue;
            foreach (City city in GetPartitionTitleCities(foundingTitle))
                if (city != null && !city.IsGone()) protectedCities.Add(city.Key);
        }
        // 八旗、开局定下朝廷的征服王朝：本族老家留作直辖(见 CompositeEmpireService.HomelandCulture)
        string homeland = CompositeEmpireService.HomelandCulture(this);
        if (homeland != null && CoreKingdom?.Cities != null)
            foreach (City city in CoreKingdom.Cities)
                if (CompositeEmpireService.IsHomeland(city, homeland)) protectedCities.Add(city.Key);
        return protectedCities;
    }
#endif

    private bool IsDirectPartitionCandidate(City city, HashSet<long> protectedCities)
    {
        return city != null && !city.IsGone() && city.Realm == CoreKingdom &&
               !EnfeoffmentHelper.IsRealmCapital(city) &&
               (protectedCities == null || !protectedCities.Contains(city.Key));
    }

#if EC_PENDING
    private static List<City> GetPartitionTitleCities(KingdomTitle title)
    {
        if (title == null || title.IsGone()) return new List<City>();
        return (title.getCities() ?? Enumerable.Empty<City>())
            .Where(city => city != null && !city.IsGone()).Distinct().ToList();
    }

    private List<Realm> FindExistingTitlePartitions(KingdomTitle title)
    {
        var result = KingdomTitleRelationResolver.FindCurrentAdministrations(title)
            .Where(IsValidTerritorialPartition).ToList();
        Realm landed = GetLandedLegalTitleKingdom(title);
        if (IsValidTerritorialPartition(landed) && !result.Contains(landed)) result.Add(landed);
        return result.OrderBy(kingdom => kingdom.Key).ToList();
    }
#endif

    private bool IsValidTerritorialPartition(Realm kingdom)
    {
        return kingdom != null && !kingdom.IsGone() && kingdom != CoreKingdom &&
               !AncientWarfareCompatibility.Owns(kingdom) && kingdom.GetEmpire() == this;
    }

#if EC_PENDING
    private void CompleteTitlePartition(KingdomTitle title, List<Realm> targets,
        HashSet<long> protectedCities, bool atWar)
    {
        targets = targets?.Where(IsValidTerritorialPartition)
            .Where(target => !atWar || !EnfeoffmentHelper.Contested(target, CoreKingdom))
            .Distinct().ToList() ?? new List<Realm>();
        if (targets.Count == 0) return;

        foreach (City city in GetPartitionTitleCities(title))
        {
            if (city == null || city.IsGone() || protectedCities.Contains(city.Key) ||
                targets.Contains(city.Realm)) continue;
            Realm target = SelectNearestPartition(city, targets);
            if (target == null || !CanTransferPartitionCity(city, target)) continue;
            // 已设的行政区、封国补齐法理：前线的城先留在原处
            if (DeferPartition(city, atWar)) continue;
            city.JoinRealm(target);
            if (city.Realm == target) _partitionMoves++;
        }
        foreach (Realm target in targets)
            EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(target);
        title.RefreshAdministrativeDivisionNames();
    }
#endif

    // 治所隔着长城的行政区不归(见 FrontierWalls)：长城那边的城不并进这一侧的行政区
#if EC_PENDING
    private static Realm SelectNearestPartition(City city, List<Realm> targets)
    {
        if (city == null || targets == null) return null;
        return targets.Where(target => target?.Capital != null && !target.Capital.IsGone() &&
                                       !WorldMap.FrontierWalls.Separates(city, target.Capital))
            .OrderBy(target => Vector2.Distance(city.city_center, target.capital.city_center))
            .ThenBy(target => target.id).FirstOrDefault();
    }

    private bool CanTransferPartitionCity(City city, Realm target)
    {
        Realm source = city?.Realm;
        if (source == null || target == null || source == target || EnfeoffmentHelper.IsRealmCapital(city)) return false;
        if (source == CoreKingdom) return true;
        if (source.GetEmpire() != this || AncientWarfareCompatibility.Owns(source)) return false;
        // 郡国并行：郡的土地不能被分封出去
        if (source.GetKingdomType() == KingdomType.ZhouFeudalism_jun) return false;
        Regime sourceRegime = source.GetRegime();
        if (sourceRegime == null || sourceRegime.IsAllowDiplomacy()) return false;
        KingdomOpinion opinion = EmpireWorld.Current?.diplomacy?.getOpinion(source, CoreKingdom);
        return opinion != null && opinion.total > 0;
    }
#endif

    // grantee：指定的受封人(亲王封地、附庸王国的王，见 PartialEnfeoffment)；不合格或没给就照常挑
#if EC_PENDING
    private Realm CreateTerritorialPartition(List<City> region, KingdomTitle title, Regime regime,
        bool createAdministration, Person grantee = null)
    {
        HashSet<long> protectedCities = GetProtectedDirectCityIds();
        region = region?.Where(city => IsDirectPartitionCandidate(city, protectedCities))
            .Distinct().ToList() ?? new List<City>();
        if (region.Count == 0) return null;

        City capital = title?.title_capital != null && region.Contains(title.title_capital)
            ? title.title_capital
            : region.OrderByDescending(city => city.GetCityStrategicValue().Total)
                .ThenBy(city => city.Key).First();
        Person governor = grantee != null && !grantee.IsGone() && grantee.Alive && !grantee.IsRuler()
            ? grantee
            : FindPartitionGovernor(capital, regime, createAdministration);
        if (governor == null || governor.IsGone()) return null;

        Realm newKingdom = SetEnfeoff(capital, governor);
        if (newKingdom == null) return null;
        _partitionMoves++;
        foreach (City city in region)
            if (city != capital && IsDirectPartitionCandidate(city, protectedCities)) city.JoinRealm(newKingdom);

        newKingdom.SetCapital(capital);
        newKingdom.SetFiedTimestamp(GameClock.Now);
        join(newKingdom, true, true);
        newKingdom.SetRegimeType(regime.type);
        newKingdom.LoadRegime();
        Regime localRegime = newKingdom.GetRegime();
        if (createAdministration)
        {
            localRegime?.SetAllowDiplomacy(false);
            localRegime?.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            if (regime.type == RegimeType.Modern || regime.type == RegimeType.Republic)
                localRegime?.SetAllowArmy(false);
            newKingdom.RemoveMainTitle();
            if (title != null) newKingdom.SetAdministrativeTitle(title);
        }
        else
        {
            localRegime?.SetAllowDiplomacy(true);
            localRegime?.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
            localRegime?.SetAllowSupportCenterArmy(false);
            localRegime?.SetTaxLevel(TaxLevel.Low);
            if (title != null)
            {
                governor.AddOwnedTitle(title);
                newKingdom.SetMainTitle(title);
                newKingdom.ReconcileMainTitle(new[] { title });
            }
        }

        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(newKingdom);
        WorldLog.logNewKingdom(newKingdom);
        RecordAutomaticPartition(governor, newKingdom, title, createAdministration);
        return newKingdom;
    }

    private Person FindPartitionGovernor(City capital, Regime regime, bool createAdministration)
    {
        // 储君(连同剧本点名的继承人)不出去当封国国君：当了别国国君就不再是本国储君(以前清开局把储君福临封了出去，皇太极死后是豪格继位)
        long heir = CoreKingdom?.GetHeir()?.Key ?? -1L;
        List<long> scriptedHeirs = CoreKingdom?.GetOrCreate()?.scripted_heirs;
        bool IsEligible(Person actor) => actor != null && !actor.IsGone() && actor.isActor() &&
            actor.Alive && actor.IsAdult() && !actor.IsRuler() && actor.Key != (Emperor?.Key ?? -1L) &&
            actor.Key != heir && scriptedHeirs?.Contains(actor.Key) != true &&
            actor.IsFitToRule();

        if (createAdministration)
        {
            Person local = capital?.HasLeader() == true && IsEligible(capital.Leader)
                ? capital.Leader
                : capital?.getUnits()?.FirstOrDefault(IsEligible);
            if (local != null) return local;
        }

        Person royal = CoreKingdom?.getKingClan()?.getUnits()?.FirstOrDefault(IsEligible);
        if (royal != null) return royal;
        // 纯净地图下宗室多是虚拟族人(原版氏族的单位里没有)：只封宗室的政体以前常年封不出去。
        // 皇族世系里在本帝国的成年族人按亲疏(兄弟、皇子在前)、长幼挑一位落成
        if (EmpireSpecificClan != null && CityPopulationSystem.AbstractPopulationEnabled)
        {
            PersonalClanIdentity emperorIdentity = Emperor?.GetPersonalIdentity();
            var near = new HashSet<long>(KinRoster.Siblings(emperorIdentity).Concat(KinRoster.Children(emperorIdentity))
                .Select(person => person.id));
            royal = KinRoster.RealizeFirst(KinRoster.Lineage(EmpireSpecificClan)
                .Where(person => KinRoster.Virtual(person) && person.age >= KinRoster.AdultAge && !person.disinherited &&
                                 KinRoster.KingdomOf(person)?.GetEmpire() == this)
                .OrderByDescending(person => near.Contains(person.id)).ThenByDescending(person => person.age)
                .ThenBy(person => person.id), IsEligible, 2);
            if (royal != null) return royal;
        }
        if (!createAdministration && regime?.enfeoff_only_royal == true) return null;
        return getUnits().FirstOrDefault(IsEligible) ?? capital?.getUnits()?.FirstOrDefault(IsEligible);
    }

    private void RecordAutomaticPartition(Person ruler, Realm kingdom, KingdomTitle title,
        bool administrative)
    {
        if (!administrative && ruler?.GetSpecificClan() != null && SpecificClanManager.SameLineage(ruler.GetSpecificClan(), EmpireSpecificClan))
        {
            TranslateHelper.LogPeerageGranted(ruler, this,
                (title?.data?.name ?? kingdom?.data?.name ?? "") + LM.Get("default_peerages_2"));
            return;
        }
        // 改设行政区写"设某某，以某人主政"，封给非宗室写"封某人于某某"
        string division = kingdom?.GetKingdomFullName() ?? title?.data?.name ?? "";
        new WorldLogMessage(administrative ? EmpireCraftWorldLogLibrary.empire_division_log
            : EmpireCraftWorldLogLibrary.empire_fief_grant_log, name, division, ruler?.FullName() ?? LM.Get("label_none"))
        {
            location = CoreKingdom.location,
            color_special1 = CoreKingdom.getColor().getColorText(),
            color_special2 = kingdom?.getColor()?.getColorText() ?? CoreKingdom.getColor().getColorText(),
            color_special3 = ruler?.getColor()?.getColorText() ?? CoreKingdom.getColor().getColorText()
        }.RecordNationalHistoryIntoEmpire(this, ruler, kingdom);
    }

    private void AutoEstablishUntitledExclaveDivisions(Regime regime, bool createAdministration,
        HashSet<long> protectedCities, bool atWar)
    {
        var remaining = new HashSet<City>(CoreKingdom.Cities.ToList().Where(city =>
            IsDirectPartitionCandidate(city, protectedCities) && !city.hasTitle() &&
            !city.IsConnectedToKingdomCapital() && !DeferPartition(city, atWar)));
        while (remaining.Count > 0)
        {
            City seed = remaining.OrderByDescending(city => city.GetCityStrategicValue().DistanceToCore)
                .ThenBy(city => city.Key).First();
            var region = new List<City>();
            var queue = new Queue<City>();
            queue.Enqueue(seed);
            remaining.Remove(seed);
            while (queue.Count > 0 && region.Count < _avgCitiesPerKingdom)
            {
                City current = queue.Dequeue();
                region.Add(current);
                if (current.neighbours_cities == null) continue;
                foreach (City neighbour in current.neighbours_cities.OrderBy(city => city?.id ?? long.MaxValue))
                {
                    if (region.Count + queue.Count >= _avgCitiesPerKingdom) break;
                    if (neighbour == null || !remaining.Remove(neighbour)) continue;
                    queue.Enqueue(neighbour);
                }
            }
            CreateTerritorialPartition(region, null, regime, createAdministration);
        }
    }
#endif

    // detached：法理里隔着长城另设的那片(见 FrontierWalls)——设行政区就与法理首府那片共用这个法理，设封国就不带法理(一个法理只有一个持有国)
#if EC_PENDING
    public Realm EstablishTerritorialDivision(List<City> region, KingdomTitle title, bool detached = false)
    {
        // 战时只设不在前线的城，前线的留在核心直辖、挂起待下一趟(见 AutoEnfeoff)
        region = WithoutDeferred(region);
        if (region.Count == 0) return null;
        var regime = CoreKingdom?.GetRegime();
        if (regime == null) return null;
        bool administration = RegimeSupportsProvince(regime) || GraceEdictService.IsCommanderyKingdomActive(this) ||
            IsWesternCentralized() || regime.type == RegimeType.Modern || regime.type == RegimeType.Republic;
        Realm legalHolder = title?.FindRealmTitleHolder();
        if (!administration && (detached || legalHolder != null && legalHolder.GetEmpire() != this)) title = null;
        return CreateTerritorialPartition(region, title, regime, administration);
    }
#endif

    // 把一片已收归核心王国的城市设为行政区(郡县)；title 不为空时由该行政区接管这个法理。
    // 政体不支持行政区时返回 null，城市留在核心王国直辖。scripted：剧本开局照史实设(见 WorldMapScenario.Divide)，
    // 战时前线的城也一起划进去(开局就在打仗的帝国，前线的城以前全留在了直辖)
#if EC_PENDING
    public Realm EstablishAdministrativeDivision(List<City> region, KingdomTitle title, bool scripted = false)
    {
        region = scripted ? region?.Where(city => city != null).ToList() ?? new List<City>() : WithoutDeferred(region);
        if (region.Count == 0) return null;
        Regime regime = CoreKingdom?.GetRegime();
        // 政体没有行政区类型(比如还是分封制)时不建行政区，城市就留在王畿直辖；
        // 施行了郡国并行的分封制可以设郡
        if (regime == null || !(RegimeSupportsProvince(regime) || GraceEdictService.IsCommanderyKingdomActive(this) ||
                                IsWesternCentralized()))
            return null;
        return CreateTerritorialPartition(region, title, regime, createAdministration: true);
    }

    private List<City> WithoutDeferred(List<City> region)
    {
        bool atWar = HasTerritorialWar();
        return region?.Where(city => city != null && !DeferPartition(city, atWar)).ToList() ?? new List<City>();
    }
#endif

    // 西方封建制具备制度特性 direct_administration(中央集权君主制、神权国家等)后设辖区/教区
    // 游牧施行了"行省制度"(用户 2026-10-19 选"补……游牧行省"；以前这一步只解锁设置行政区，打下的地照旧分给宗王)
#if EC_PENDING
    public bool IsNomadProvincial() =>
        CoreKingdom?.GetRegime()?.type == RegimeType.YouMu &&
        InstitutionSystem.HasFeature(this, InstitutionFeatures.ProvincialAdministration);
#endif

    // 古典共和施行了"元首制"(用户 2026-10-19 选"补罗马元首制"；以前城邦共和直接封建化，没有元首、没有行省)：
    // 元首在家族里相传(见 OfficeSelector)，核心打下的城不再分成自治城邦，设行省派总督(见 CityStateService)
#if EC_PENDING
    public bool IsPrincipate() =>
        CoreKingdom?.GetRegime()?.type == RegimeType.ClassicalRepublic &&
        InstitutionSystem.HasFeature(this, InstitutionFeatures.Principate);

    public bool IsWesternCentralized() =>
        CoreKingdom?.GetRegime()?.type == RegimeType.Feudalism &&
        InstitutionSystem.HasFeature(this, InstitutionFeatures.DirectAdministration);
#endif

    private static bool RegimeSupportsProvince(Regime regime)
    {
        if (regime?.type == RegimeType.LvLing) return true;
        return regime?.bureau_config?.kingdoms?.Keys.Any(type =>
            type.ToString().EndsWith("_province", StringComparison.OrdinalIgnoreCase)) == true;
    }


#if EC_PENDING
    public Realm SetEnfeoff(City capital, Person king)
    {
        Realm pKingdom = capital?.Realm;
        // 先校验再拆城：拆完城后建国失败，城市会变成没有王国的孤城
        if (pKingdom == null || pKingdom.asset == null || capital.IsGone() ||
            EnfeoffmentHelper.IsRealmCapital(capital) || !king.CanFoundCivKingdom()) return null;
        king.ClearDisposedKingdom();
        capital.removeFromCurrentKingdom();
        capital.removeLeader();
        Realm kingdom = EmpireWorld.Current.LiveRealms.makeNewCivKingdom(king, pLog:false);
        kingdom.copyMetasFromOtherKingdom(pKingdom);
        kingdom.updateColor(pKingdom.getColor());
        capital.newForceKingdomEvent(base.units, capital._boats, kingdom, null);
        capital.setKingdom(kingdom);
        capital.switchedKingdom();
        kingdom.setCityMetas(capital);
        EmpireWorld.Current.zone_calculator.dirtyAndClear();
        return kingdom;
    }

    public void SelectAndInspect()
    {
        EmpireCraftMetaTypeLibrary.selected_empire = this;
        SelectedMetas.selected_kingdom = CoreKingdom;
        ScrollWindow.showWindow(nameof(EmpireWindow));
    }

    public override Person getRandomUnit()
    {
        return this.kingdoms_list.GetRandom<Realm>().getRandomUnit();
    }

    public Sprite GetBackgroundSprite()
    {
        return EmpireWorld.Current.alliances.getBackgroundsList()[this.data.banner_background_id];
    }

    public Sprite GetIconSprite()
    {
        return CoreKingdom.getSpriteIcon();
    }
    
    public bool isRekt()
    {
        return IsArchived()||!isAlive();
    }

    public override void Dispose()
    {
        this.kingdoms_list.Clear();
        this.kingdoms_hashset.Clear();
        this.given_Kingdoms.Clear();
        this.taken_Kingdoms.Clear();
        this.cities_list.Clear();
        this.CoreKingdom = null;
        if (!ModClass.ALL_HISTORY_DATA.ContainsKey(this.data.id))
        {
            ModClass.ALL_HISTORY_DATA.Add(this.data.id, this.data.history);
        }
        LogService.LogInfo("清空");
    }
#endif
}
}
