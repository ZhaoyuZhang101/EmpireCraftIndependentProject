// Generated from the EmpireCraft mod: Scripts/GameClassExtensions/ActorExtension.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
using Vector2 = EmpireCraft.Core.EcVector2;
using Vector3 = EmpireCraft.Core.EcVector3;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static EmpireCraft.Core.ActorExtension;
using static EmpireCraft.Core.ClanExtension;
using static EmpireCraft.Core.CityExtension;
using static EmpireCraft.Core.ExamSystem;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using static EmpireCraft.Core.OverallHelperFunc;
using System.Security.Principal;
#if EC_PENDING
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
#endif

namespace EmpireCraft.Core
{
public class Name
{
    public string firstName;
    public string familyName;
    public string cultureName;
    public bool has_sex_post;
    public bool use_local_as_family_name;
    public bool is_invert;
    public string sex_post_Male;
    public string sex_post_Female;
    public Sex sex;

    public bool has_whole_name(Person actor)
    {
        return firstName != "" && familyName != ""&&firstName!=null&&familyName!=null;
    }
    
    public bool hasFamilyName(Person actor)
    {
        return !string.IsNullOrEmpty(familyName);
    }
    public bool hasFirstName(Person actor)
    {
        return !string.IsNullOrEmpty(firstName);
    }
    public bool hasCulture(Person actor)
    {
        return actor.HasCulture();
    }

    public void Initialize(Setting setting, string culture)
    {
        if (setting?.Unit == null) return;
        this.has_sex_post = setting.Clan?.has_sex_post == true;
        this.use_local_as_family_name = setting.Clan?.use_local_as_lastname == true;
        this.is_invert = setting.Unit.is_invert;
        var language = LM.Language;
        int suffixIndex = language == "ch" ? 0 : language == "cz" ? 2 : 1;
        this.sex_post_Male = LocalizedSuffix(setting.Clan?.sex_post_Male, suffixIndex);
        this.sex_post_Female = LocalizedSuffix(setting.Clan?.sex_post_Female, suffixIndex);
        
        this.cultureName = culture;
    }

    private static string LocalizedSuffix(string[] suffixes, int index)
    {
        if (suffixes == null || suffixes.Length == 0) return "";
        return suffixes[index < suffixes.Length ? index : 0] ?? "";
    }

    public void SetName(Person actor)
    {
        if (actor == null) return;
        // Serialized Name flags may belong to an earlier culture or template version.
        // Refresh formatting at naming events, rather than scanning all actors every frame.
        actor.initializeActorName();
        sex = actor.IsMale() ? Sex.Male : Sex.Female;
        if (hasFamilyName(actor))
        {
            string real_family_name;
            string cityName = "";
            if (use_local_as_family_name)
            {
                if  (actor.HasCity())
                {
                    cityName = actor.City.Name;
                } else
                {
                    if (actor.Cell.HasCity())
                    {
                        cityName = actor.Cell.City.Name;
                    } else
                    {
                        if (actor.GetParents().Count()>0) 
                        {
                            if (actor.GetParents().Any(a=>a.HasCity()))
                            {
                                cityName = actor.GetParents().ToList().Find(p => p.HasCity()).City.Name;
                            }
                        }
                    }
                }
            }
            if (use_local_as_family_name && cityName != "")
            {
                real_family_name = cityName;
                familyName = real_family_name;
                if (actor.HasClan())
                {
                    actor.Clan.Name = OverallHelperFunc.JoinNameParts(real_family_name, LM.Get("Clan"));
                }
                if (actor.HasFamily())
                {
                    actor.Family.Record.Name = OverallHelperFunc.JoinNameParts(real_family_name, LM.Get("Family"));
                    actor.Family.SetFamilyCityPre(false);
                }
            }
            real_family_name = (use_local_as_family_name&&cityName!="") ? cityName : familyName;
            real_family_name ??= "";
            if (has_sex_post)
            {
                string post = (sex == Sex.Female ? sex_post_Female : sex_post_Male)??"";
                if (!real_family_name.Contains(post))
                {
                    real_family_name += post;
                }
            }
            // 华夏早期姓氏演变：无姓只称名、男子称氏、平民无氏(见 PrehistoricClanSystem)；族谱里的姓不变
            string shown = PrehistoricClanSystem.DisplaySurname(actor, real_family_name);
            actor.Name = string.IsNullOrEmpty(shown) ? firstName ?? ""
                : is_invert
                    ? OverallHelperFunc.JoinNameParts(firstName, shown)
                    : OverallHelperFunc.JoinNameParts(shown, firstName);
            if (actor.HasSpecificClan())
            {
                PersonalClanIdentity identity = actor.GetPersonalIdentity();
                if (identity != null)
                {
                    identity.name = actor.Name;
                    identity.given_name = firstName ?? "";
                    identity.surname = familyName ?? "";
                    SpecificClan sc = identity._specificClan;
                    if (sc != null)
                    {
                        if (identity.id == sc.founder)
                        {
                            sc.name = real_family_name;
                        }
                    }
                }

            }
        } else
        {
            if (hasFirstName(actor))
            {
                actor.Name = firstName;
            }
        }
        // 西方帝国皇帝的王号：名 + 序数(不带姓；中文"二世"，别的语言罗马数字，见 RegnalNameService.Display)
        string regnalSuffix = actor.GetOrCreate()?.regnal_suffix;
        if (!string.IsNullOrWhiteSpace(regnalSuffix) && hasFirstName(actor))
        {
            actor.Name = OverallHelperFunc.JoinNameParts(firstName, RegnalNameService.Display(regnalSuffix));
            PersonalClanIdentity regnalIdentity = actor.GetPersonalIdentity();
            if (regnalIdentity != null) regnalIdentity.name = actor.Name;
        }
        if (has_whole_name(actor))
        {
            actor.GetPersonalIdentity()?.BackfillRelatedHistoryRecords();
        }
    }

    public void RepairOrder(Person actor)
    {
        if (actor?.Record == null || !has_whole_name(actor)) return;
        if (!string.IsNullOrWhiteSpace(actor.GetOrCreate()?.regnal_suffix)) return;
        actor.initializeActorName();
        string surname = familyName;
        string post = actor.IsMale() ? sex_post_Male : sex_post_Female;
        if (has_sex_post && !string.IsNullOrEmpty(post) && !surname.Contains(post)) surname += post;
        string expected = is_invert ? OverallHelperFunc.JoinNameParts(firstName, surname)
            : OverallHelperFunc.JoinNameParts(surname, firstName);
        string reversed = is_invert ? OverallHelperFunc.JoinNameParts(surname, firstName)
            : OverallHelperFunc.JoinNameParts(firstName, surname);
        string current = actor.Name.UseLocalizedNameSeparator();
        // Only correct a recognized composed name; arbitrary player names stay intact.
        if (current != expected && current == reversed) SetName(actor);
    }
}
public class OfficeIdentity
{
    private long officeID { get; set; } = -1L;
    public int officialLevel { get; set; } = -1;
    public int meritLevel { get; set; }
    public int honoraryOfficial { get; set; }
    public PeerageType peerageType { get; set; }
    public double OfficePerformance { get; set; } = 100;
    private bool _is_cabinet { get; set; } = false;
    public double TotalPerformance { get; set; } = 0;
    public int ViolateLevel { get; set; } = 0;
    public PerformanceEvents performanceEvents { get; set; }
#if EC_PENDING
    [JsonIgnore]
    public AdvancedButton setLeaderButton { get; set; } 
#endif
    [JsonIgnore]
    public AutoHoriLayoutGroup memberCardSpace { get; set; }
    public List<EmpireExamLevel> empireExamLevels { get; set; } = new List<EmpireExamLevel>();
    public long actor_id;
    [JsonIgnore]
    public Person actor => EmpireWorld.Current.PersonByKey(actor_id);
    public void init (Person actor) 
    {
        actor_id = actor.Key;
        OfficePerformance = 100;
        empireExamLevels = new List<EmpireExamLevel>();

        if (officialLevel == -1)
        {
            performanceEvents = null;
        }
        else
        {
            performanceEvents = new PerformanceEvents();
            performanceEvents.init(actor);
        }
    }

    public string GetHonoraryOfficialName()
    {
        return OfficialRankSystem.NameFor(actor);
    }
    /// <summary>
    /// 进入内阁
    /// </summary>
    public void EnterCabinet()
    {
        _is_cabinet = true;
    }
    /// <summary>
    /// 离开内阁
    /// </summary>
    public void ExitCabinet()
    {
        _is_cabinet = false;
    }

    public bool IsCabinet()
    {
        return _is_cabinet;
    }
    public void SetOfficeId(long oid, Person actor)
    {
        performanceEvents = new PerformanceEvents();
        performanceEvents.init(actor);
        this.officeID = oid;
    }

    public bool HasOffice()
    {
        return officeID != -1L;
    }

    public void RemoveOffice()
    {
        if (OfficeManager.Offices.TryGetValue(this.GetOfficeId(), out var value))
        {
            value.RemoveActor();
        }
        performanceEvents = null;
        officialLevel = -1;
        officeID = -1L;
    }

    public long GetOfficeId()
    {
        return this.officeID;
    }
    
    public void ChangeOfficialLevel(int level)
    {
        Person actor = EmpireWorld.Current.PersonByKey(actor_id);
        officialLevel = level;
        if (level == -1)
        {
            performanceEvents = null;
        } else
        {
            performanceEvents = new PerformanceEvents();
            performanceEvents.init(actor);
        }
    }
}
public enum PerformanceEventType
{
    Irrigation_Project_Completed,
    Local_Rebellion_Suppressed,
    Virtuous_Talent_Recommended,
    Disaster_Relief_Conducted,
    Schools_Restored,
    Factionalism_Exposed,
    Negligence_in_Local_Affairs,
    Excessive_Taxation_Imposed,
    Corrupted_Performance_Reports,
    Refused_Imperial_Summons,
    Administrative_Reform,
    Legal_Code_Revision,
    Policy_Championed_at_Court,
    Talents_Recommended,
    Corruption_Exposed,
    Factional_Infighting,
    Examination_Manipulation,
    Neglect_of_Duty,
    Unpopular_Reforms,
    Misleading_the_Emperor,
    None
}
public static class ActorExtension
{
    // 单位仍指向已 Dispose 的王国(asset 为空)时，原版 joinKingdom 会在 kingdom.isCiv() 抛空引用，
    // 让 makeNewCivKingdom 只建了一半。建国前先把这种失效引用清掉。
#if EC_PENDING
    public static void ClearDisposedKingdom(this Person actor)
    {
        if (actor?.Realm != null && actor.Realm.asset == null) actor.Realm = null;
    }
#endif

    // 只有文明物种能建国：原版按 kingdom_id_civilization 取王国资产，非文明单位建出的王国没有资产
#if EC_PENDING
    public static bool CanFoundCivKingdom(this Person actor)
    {
        string id = actor?.Asset?.kingdom_id_civilization;
        return actor != null && !actor.IsGone() && actor.Alive && !string.IsNullOrEmpty(id) &&
               AssetManager.kingdoms.dict.TryGetValue(id, out KingdomAsset asset) && asset.civ;
    }
#endif

    public class ActorExtraData:ExtraDataBase
    {
        // 君主资质已定(见 RulerQualitySystem.Assess)
        public bool ruler_assessed;
        // 中兴功绩观察：按本人、当前帝国保存，旧档从首次观察起算，不把积弊高直接算成中兴。
        public long restorer_realm = -1L;
        public double restorer_since = -1d;
        public float restorer_malady = -1f;
        public double restorer_corruption = -1d;
        public int restorer_legitimacy = -1;
        public bool ShouldSerializerestorer_realm() => restorer_realm >= 0L;
        public bool ShouldSerializerestorer_since() => restorer_realm >= 0L;
        public bool ShouldSerializerestorer_malady() => restorer_realm >= 0L;
        public bool ShouldSerializerestorer_corruption() => restorer_realm >= 0L;
        public bool ShouldSerializerestorer_legitimacy() => restorer_realm >= 0L;
        // 君主、储君上次添孩子的时间(见 RoyalHouseSystem.CanBirth)
        public double last_royal_birth = -1d;
        // 真实年龄：成为君主、储君时的年纪与保底的天年、上回看到在位的时间(见 RealisticAgeSystem.RoyalLifespan)；-1 未定
        public int royal_anchor_age = -1;
        public int royal_anchor_floor = -1;
        public double royal_seen_at = -1d;
        public bool ShouldSerializeroyal_anchor_age() => royal_anchor_age >= 0;
        public bool ShouldSerializeroyal_anchor_floor() => royal_anchor_floor >= 0;
        public bool ShouldSerializeroyal_seen_at() => royal_seen_at >= 0d;
        // 爵位  
        [JsonConverter(typeof(StringEnumConverter))]
        public PeeragesLevel peeragesLevel;
        public PeerageType peerageType;
        public List<long> want_acuired_title = new List<long>();
        public List<long> owned_title = new List<long>();
        // 虚封仅保留名义爵位与封地，不改变城市、王国或法理的所有权。
        public bool virtual_enfeoff = false;
        public long virtual_enfeoff_title_id = -1L;
        public long virtual_enfeoff_empire_id = -1L;
        public string virtual_enfeoff_peerage_key = "";
        public string honorary_peerage_key = "";
        // 西方帝国皇帝的王号序数(罗马数字)，有值时名字显示为"名 序数"，如"腓特烈 II"
        public string regnal_suffix = "";
        public long honorary_peerage_empire_id = -1L;
        public string factionID = "";
        public Name name;
        public bool has_become_cleric = false;
        public SocialClass  socialClass = SocialClass.Peasant;
        // 无小人模式的征召兵：这个士兵代表的军团人数(含本人)，随生命值下降而阵亡的部分不再恢复
        public float legion_size;
        // 军团满编人数(征召时的人数)：回血时按生命值补员到这个数
        public float legion_full;
        public long legion_home_city_id = -1L;
        public SocialClass legion_home_social_class = SocialClass.Peasant;
        public List<LegionPopulationContribution> legion_population;
        // Mod-managed workshop employment. Tied to a city so migration ends the job.
        public long urban_employment_city_id = -1L;
        // Advanced urban housing creates a persistent citizen stratum. Migration ends the status.
        public long urban_citizen_city_id = -1L;
        public string personal_ideology = "";
        // Used by the yearly ideology pass to react when urbanisation changes an actor's class.
        public string last_ideology_social_class = "";
        // 商人不是职业，而是家庭长期收入形成的经济身份。收入按城市年度结算滚动。
        public long economic_income_current_year = 0;
        public long economic_income_previous_year = 0;
        public int merchant_high_income_years = 0;
        public int merchant_low_income_years = 0;
        public bool is_economic_merchant = false;
        public long pending_trade_origin_city_id = -1L;
        public long pending_trade_destination_city_id = -1L;
        public double pending_trade_timestamp = -1d;
        public bool pending_trade_foreign_kingdom = false;
        public OfficeIdentity officeIdentity { get; set; } = null;
        public double last_tax_timestamp = -1L;
        // 上次纳税后的存款：下次按新增的部分(收入)纳税，见 EmpireCraftActorCheckTax
        public long tax_money_baseline = -1L;
        public double last_fiscal_corruption_crime_timestamp = -1d;
        public double last_add_lover_timestamp = -1L;
        public double last_give_birth_timestamp = -1L;
        public long empire_id { get; set; } = -1L;
        public long OfficeId { get; set; } = -1L;
        public bool is_on_office = false;
        public int ChanceToGetKingdom = 0;
        public long personal_identity { get; set; } = -1L;
        public int escape_count { get; set; } = 0;
        public float death_rate = 0.0f;
        public List<long> banned_office_empire_ids = new List<long>();
        // 不在帝国里的国家株连子孙、禁锢出仕(见 EmpireLawSystem.IsBarredFromOffice)
        public List<long> banned_office_kingdom_ids;
        public bool ShouldSerializebanned_office_kingdom_ids() => banned_office_kingdom_ids != null && banned_office_kingdom_ids.Count > 0;
        public Dictionary<string, double> law_check_timestamps = new Dictionary<string, double>();
        public double target_tile_timestamp = -1;
        [JsonIgnore]
        public Cell target_tile = null;
    }
    /// <summary>
    /// 初始化执行
    /// </summary>
    /// <param name="a"></param>
    public static void InitialChangeToGetKingdom(this Person a)
    {
        
    }

    public static bool EscapeFromPunishment(this Person a, bool result = false)
    {
        if (!result)
        {
            a.GetOrCreate().escape_count = Mathf.Clamp(a.GetOrCreate().escape_count+1, 0, 2);
        }
        if (a.GetOrCreate().escape_count >= 2)
        {
            return true;
        }
        return false;
    }
    public static void SetSocialClass(this Person a, SocialClass socialClass)
    {
        a.GetOrCreate().socialClass = socialClass;
    }
    public static void RecordAddLoverTime(this Person a)
    {
        a.GetOrCreate().last_add_lover_timestamp = GameClock.Now;
    }

    public static bool IsSkeleton(this Person a)
    {
        if (a == null || a.IsGone())
        {
            return false;
        }

        if (a.Subspecies == null)
        {
            return false;
        }

        return a.Subspecies.SpeciesId == "skeleton";
    }

#if EC_PENDING
    public static void SetFrontLineMoveTarget(this Person a, Cell tile)
    {
        if (a == null)
        {
            return;
        }

        var data = a.GetOrCreate();

        if (tile == null)
        {
            data.target_tile = null;
            data.target_tile_timestamp = -1;
            return;
        }
        a.tile_target = tile;
        a.beh_tile_target = tile;
        data.target_tile = tile;
        data.target_tile_timestamp = GameClock.Now;
    }
#endif

    public static void ClearFrontLineMoveTarget(this Person a)
    {
        if (a == null)
        {
            return;
        }

        var data = a.GetOrCreate();
        data.target_tile = null;
    }

    public static Cell GetFrontLineMoveTarget(this Person a)
    {
        return a.GetOrCreate().target_tile;
    }

    public static bool HasFrontLineMoveTarget(this Person a)
    {
        if (a == null)
        {
            return false;
        }

        var data = a.GetOrCreate();
        return data.target_tile != null;
    }

    public static bool IsFrontLineMoveTarget(this Person a, Cell tile)
    {
        if (a == null || tile == null || !a.HasFrontLineMoveTarget())
        {
            return false;
        }

        var data = a.GetOrCreate();
        return data.target_tile == tile;
    }

    public static bool ShouldBlockGoToByFrontLine(this Person a, Cell requestedTile)
    {
        if (a == null || requestedTile == null)
        {
            return false;
        }

        if (!WorldLaws.SwitchOccupyMode.IsEnabled())
        {
            return false;
        }

        if (!a.HasArmy() || a.Realm == null || !a.Realm.HasEnemies())
        {
            return false;
        }

        if (a.IsNearbyEnemyAttackTarget(requestedTile))
        {
            return false;
        }

        var data = a.GetOrCreate();

        // Target selection belongs to EmpireCraftActorCheckWarriorMove. Creating a
        // second target here used the legacy planner and let two military AIs compete.
        if (data.target_tile == null)
        {
            return false;
        }

        Cell targetTile = data.target_tile;

        if (targetTile == null)
        {
            return false;
        }

        // 原版这次 moveTo 本来就是去目标 tile，则放行
        if (requestedTile == targetTile)
        {
            return false;
        }

        // 原版如果是在目标 zone 内移动，也放行，避免卡死寻路
        if (requestedTile.Zone != null && targetTile.Zone != null && requestedTile.Zone == targetTile.Zone)
        {
            return false;
        }

        // 其他乱走目标全部拦截
        return true;
    }

    public static bool HasNearbyEnemyAttackOpportunity(this Person a)
    {
        if (a == null || a.Realm == null || a.Cell?.Zone == null)
        {
            return false;
        }

        Cell currentZone = a.Cell.Zone;
        if (HasEnemyArmyInZone(a, currentZone))
        {
            return true;
        }

        if (currentZone.NeighboursAll == null)
        {
            return false;
        }

        for (int i = 0; i < currentZone.NeighboursAll.Length; i++)
        {
            Cell neighbour = currentZone.NeighboursAll[i];
            if (HasEnemyArmyInZone(a, neighbour))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNearbyEnemyAttackTarget(this Person a, Cell requestedTile)
    {
        Cell currentZone = a?.Cell?.Zone;
        Cell requestedZone = requestedTile?.Zone;
        if (currentZone == null || requestedZone == null)
        {
            return false;
        }

        bool isNearby = requestedZone == currentZone ||
                        (currentZone.NeighboursAll != null &&
                         currentZone.NeighboursAll.Contains(requestedZone));
        return isNearby && HasEnemyArmyInZone(a, requestedZone);
    }

    private static bool HasEnemyArmyInZone(Person actor, Cell zone)
    {
        if (actor == null || zone == null)
        {
            return false;
        }

        City city = zone.City;
        if (city?.People == null)
        {
            return false;
        }

        for (int i = 0; i < city.People.Count; i++)
        {
            Person other = city.People[i];
            if (other == null || other == actor || !other.HasArmy() || other.Cell?.Zone != zone)
            {
                continue;
            }

            if (other.Realm != null && actor.Realm.IsAtWarWith(other.Realm))
            {
                return true;
            }
        }

        return false;
    }
    public static bool IsNeedToAddLover(this Person a)
    {
        return a.GetOrCreate().last_add_lover_timestamp < 0 ||
               GameClock.YearsSince(a.GetOrCreate().last_add_lover_timestamp) > 2;
    }

    public static void RecordGiveBirthTime(this Person a)
    {
        a.GetOrCreate().last_give_birth_timestamp = GameClock.Now;
    }

    public static bool IsNeedToGiveBirth(this Person a)
    {
        return a.GetOrCreate().last_give_birth_timestamp < 0 ||
               GameClock.YearsSince(a.GetOrCreate().last_give_birth_timestamp) > 2;
    }
    public static void StartOffice(this Person a, OfficeObject o)
    {
        a.GetOrCreate().OfficeId = o.OfficeID;
        a.GetOrCreate().is_on_office = true;
    }

    public static bool HasChooseToBecomeCleric(this Person a)
    {
        return a.GetOrCreate().has_become_cleric;
    }

    public static bool FinishChooseToBecomeCleric(this Person a)
    {
        return a.GetOrCreate().has_become_cleric = true;
    }
    // 先查本国所在的那个帝国(派系成员几乎都在这里)，查不到再逐个帝国找。以前每次都把全部帝国的派系扫一遍，
    // 派系年度整理(FixedFaction.Update)对每个成员都调、每死一个文明人也调，帝国一多(部落时代)就是帝国数 × 人数
    // (用户 2026-10-26 嫌卡)。派系编号都是新生成的 GUID(见 FixedFaction.Clone、DeepClone 与各处新建)，各帝国不重，
    // 先查哪家结果都一样；没入派系的(编号为空串)对不上任何派系，直接返回
    public static FixedFaction GetFaction(this Person a)
    {
        string factionId = a.GetOrCreate()?.factionID;
        if (factionId == "" || ModClass.EMPIRE_MANAGER == null) return null;
        Empire own = a.Realm?.GetEmpire();
        if (own != null && !own.IsArchived() && FactionInEmpire(own, factionId) is FixedFaction mine) return mine;
        foreach (var empire in ModClass.EMPIRE_MANAGER)
        {
            if (empire == null || empire == own || empire.IsArchived()) continue;
            if (FactionInEmpire(empire, factionId) is FixedFaction faction) return faction;
        }
        return null;
    }

    private static FixedFaction FactionInEmpire(Empire empire, string factionId)
    {
        var regime = empire.CoreKingdom?.GetRegime();
        if (regime == null) return null;
        foreach (var faction in regime.GetPlayerFactions())
        {
            if (faction?.GetID() == factionId) return faction;
        }
        return null;
    }

    public static void SetFaction(this Person a, FixedFaction faction)
    {
        if (a == null) return;
        var lastFaction = a.GetFaction();
        if (lastFaction == faction) return;
        lastFaction?.RemoveMember(a);
        if (faction == null) return;
        a.RecordPersonalHistory(string.Format(LM.Get("personal_history_joined_faction"), faction.Name));
        faction.AddMember(a);
    }

    public static void RemoveFaction(this Person a)
    {
        a.GetOrCreate().factionID = "";
    }

    public static bool HasFaction(this Person a)
    {
        if (a == null)
        {
            return false;
        }
        return !string.IsNullOrEmpty(a.GetOrCreate().factionID);
    }

    public static void EndOffice(this Person a)
    {
        a.GetOrCreate().OfficeId = -1L;
        a.GetOrCreate().is_on_office = false;
    }

    public static OfficeObject GetOffice(this Person a)
    {
        return OfficeManager.Offices.TryGetValue(a.GetOrCreate().OfficeId,  out var value) ? value : null;
    }

    // 在任：官职还在、而且挂着的就是这个人(官职撤销、换人后残留的"在任"标记不算)
    public static bool IsOnOffice(this Person a)
    {
        var data = a.GetOrCreate();
        return data.is_on_office && OfficeManager.Offices.TryGetValue(data.OfficeId, out OfficeObject office) &&
               office != null && office.actor_id == a.Key;
    }

    public static void BanFromOffice(this Person a, long empireId)
    {
        if (a == null || empireId < 0) return;
        var bans = a.GetOrCreate().banned_office_empire_ids;
        if (bans == null)
        {
            bans = new List<long>();
            a.GetOrCreate().banned_office_empire_ids = bans;
        }
        if (!bans.Contains(empireId))
        {
            bans.Add(empireId);
        }
        EmpireCraft.Core.OfficeBans.Add(a.GetPersonalIdentity(), empireId, kingdom: false);
    }

    public static bool IsBannedFromOffice(this Person a, long empireId)
    {
        if (a == null || empireId < 0) return false;
        return IsBannedFromOffice(a, empireId, new HashSet<long>());
    }

    private static bool IsBannedFromOffice(this Person a, long empireId, HashSet<long> visited)
    {
        if (a == null) return false;
        var id = a.Key;
        if (!visited.Add(id)) return false;

        var bans = a.GetOrCreate().banned_office_empire_ids;
        if (bans != null && bans.Contains(empireId))
        {
            return true;
        }
        // 族谱上记的禁锢、族谱里的祖上(父母转成虚拟族人或已故，getParents 找不到)
        if (EmpireCraft.Core.OfficeBans.Banned(a.GetPersonalIdentity(), empireId, kingdom: false)) return true;

        var parents = a.GetParents();
        if (parents == null || !parents.Any())
        {
            return false;
        }

        foreach (var parent in parents)
        {
            if (parent.IsBannedFromOffice(empireId, visited))
            {
                return true;
            }
        }

        return false;
    }

    // WarBox 等模组的载具(坦克、飞机、炮艇)也是 Person，不能当官、入党、进宗族、当议员
    public static bool IsWarMachine(this Person a)
    {
        if (a == null) return false;
        // 原版的船只也是 Person：不能当君主、官员、作者，也不算人口
        if (a.Asset?.IsBoat == true) return true;
        if (a.HasTrait("warbox_unit")) return true;
        string id = a.Asset?.Id;
        return id != null && id.StartsWith("warbox_", StringComparison.Ordinal);
    }

    // 妃嫔(记为妾室的)或在位君主的配偶：不出任后宫以外的官职
    public static bool IsHaremOrConsort(this Person a)
    {
        if (a?.Record == null || a.IsGone() || a.IsRuler()) return false;
        if (a.GetPersonalIdentity()?.is_concubine == true) return true;
        Person spouse = a.Spouse;
        return spouse != null && !spouse.IsGone() && spouse.Alive && spouse.IsRuler();
    }

    public static bool CanServeOffice(this Person a, Realm kingdom)
    {
        if (a == null || a.IsGone() || !a.Alive || a.IsWarMachine()) return false;
        // 在押的不得出任官职；不在帝国里的国家按本国的株连禁锢(见 EmpireLawSystem.IsBarredFromOffice)
        if (EmpireCraft.Core.EmpireLawSystem.IsBarredFromOffice(a, kingdom)) return false;
        if (kingdom == null || !kingdom.IsInEmpire())
        {
            return true;
        }

        var empireId = kingdom.GetEmpireID();
        return empireId < 0 || !a.IsBannedFromOffice(empireId);
    }

    public static bool IsLawCheckDue(this Person a, string key, float years = 1f)
    {
        if (a == null || string.IsNullOrEmpty(key)) return false;
        Dictionary<string, double> timestamps = a.GetOrCreate().law_check_timestamps;
        if (timestamps == null)
        {
            timestamps = new Dictionary<string, double>();
            a.GetOrCreate().law_check_timestamps = timestamps;
        }

        double value;
        if (!timestamps.TryGetValue(key, out value))
        {
            return true;
        }

        if (value < 0)
        {
            return true;
        }

        return GameClock.YearsSince(value) >= years;
    }

    public static void RecordLawCheck(this Person a, string key)
    {
        if (a == null || string.IsNullOrEmpty(key)) return;
        Dictionary<string, double> timestamps = a.GetOrCreate().law_check_timestamps;
        if (timestamps == null)
        {
            timestamps = new Dictionary<string, double>();
            a.GetOrCreate().law_check_timestamps = timestamps;
        }

        timestamps[key] = GameClock.Now;
    }

    public static void SetTyrantValue(this Person a, int value)
    {
        if (a?.GetIdentity()==null) return;
        float previous = a.GetViolateValue();
        int next = Mathf.Clamp(value, 0, 100);
        a.GetIdentity().ViolateLevel = next;
        if (previous < 100f && next >= 100f)
        {
            a.OnTyrantValueFullReached();
        }
    }

    public static void AddTyrantValue(this Person a, int addition = 20)
    {
        if (a == null || addition == 0f) return;
        a.SetTyrantValue(a.GetViolateValue() + addition);
    }

    private static void OnTyrantValueFullReached(this Person a)
    {
        // TODO: 暴君值达到 100% 后，在这里接入皇帝暴政的具体效果。
    }
    public static bool NeedDead(this Person a)
    {
        if (a == null) return false;

        var data = a.GetOrCreate();
        float rate = Mathf.Clamp01(data.death_rate);

        // 边界：0 一定不死；1 一定会死
        if (rate <= 0f) return false;
        if (rate >= 1f) return true;

        // 概率判定（Unity）
        return EcRandom.value < rate;
    }
    public static double CalcCorruptionValue(this Person actor)
    {
        double result = 0f;
        double value = 0.5f-PerformanceEvents.GetPersonalPerformance(actor);
        if (value > 0)
        {
            result = value / 0.5f;
        }
        return result;
    }
    public static double GetLastTaxTime(this Person k)
    {
        return k.GetOrCreate().last_tax_timestamp;
    }
    public static void RecordTaxTime(this Person k)
    {
        k.GetOrCreate().last_tax_timestamp = GameClock.Now;
    }

    public static bool IsNeedToSubmitTax(this Person k)
    {
        if (!k.HasRealm()) return false;
        return GameClock.YearsSince(k.GetLastTaxTime()) >= 1;
    }
#if EC_PENDING
    public static void WarriorMoveTo(this Person a, Cell pTileTarget)
    {
        if (a == null || pTileTarget == null)
        {
            return;
        }

        if (a.Cell == null)
        {
            return;
        }

        float dist = Land.SquaredDist(a.Cell, pTileTarget);

        LogService.LogInfo("士兵移动距离: " + dist);

        a.setIsMoving();

        if (pTileTarget.isOnFire() && !a.Cell.isOnFire() && !a.isImmuneToFire())
        {
            a.cancelAllBeh();
            return;
        }

        a._next_step_tile = pTileTarget;

        if (dist > 4.0)
        {
            LogService.LogInfo("目标 tile 太远，不能直接移动");
            return;
        }

        a.setCurrentTile(a._next_step_tile);
        a.checkStepActionForTile(a.Cell);
        a.next_step_position = new Vector3(pTileTarget.posV3.x, pTileTarget.posV3.y);

        LogService.LogInfo("士兵移动成功");
    }
#endif
    public static void ChangeDeathRate(this Person a, float value)
    {
        a.GetOrCreate().death_rate += value;
        if (a.GetOrCreate().death_rate <= 0.0f)
        {
            a.GetOrCreate().death_rate = 0.0f;
        } else if (a.GetOrCreate().death_rate >= 1.0f)
        {
            a.GetOrCreate().death_rate = 1.0f;
        }
    }
    public static SpecificClan GetSpecificClan(this Person a)
    {
        if (a == null) return null;
        var identity = a.GetPersonalIdentity();
        return identity?._specificClan;
    }
    public static PersonalClanIdentity GetPersonalIdentity(this Person a)
    {
        if (a == null) return null;
        var ed = a.GetOrCreate();
        return SpecificClanManager.getPerson(ed.personal_identity);
    }
    public static void RecordPersonalHistory(this Person a, string content, string eventKey = "", long relatedActorId = -1L,
        long relatedPersonalIdentityId = -1L, bool major = false)
    {
        if (a == null || EmpireWorld.Current == null || ModClass.IS_CLEAR) return;
        PersonalClanIdentity identity = a.GetPersonalIdentity();
        Realm kingdom = a.Realm;
        Empire empire = kingdom?.GetEmpire();
        string historyDate = empire?.GetYearNameWithTime();
        identity.RecordPersonalHistory(content, historyDate, kingdom?.GetKingdomName(), empire?.Key ?? -1L, eventKey,
            relatedActorId, relatedPersonalIdentityId, major);
    }

    // 个人经历上限：族谱里连已故族人的经历也存档，不设上限存档会膨胀到几百 MB、读档内存不够直接闪退。
    // 超出时留第一条(通常是出生)、最近几条与大事，先删例行公事(见 BiographySystem.Trim)
    // 已故族人只留 12 条，族谱里绝大多数是已故的人
    public const int MaxPersonalHistory = 80;
    public const int MaxDeceasedPersonalHistory = 12;

    public static void TrimPersonalHistory(PersonalClanIdentity identity)
    {
        List<PersonalHistoryRecord> history = identity?.personal_history;
        bool dead = identity?.is_alive == false;
        int cap = dead ? MaxDeceasedPersonalHistory : MaxPersonalHistory;
        if (history == null || history.Count <= cap) return;
        BiographySystem.Trim(history, cap, 3, dead ? BiographySystem.MaxDeceasedMajor : BiographySystem.MaxMajor);
    }

    // 存档前把所有族人的经历压到上限(已故的人不再记新经历，只能在这里收)
    public static void TrimAllPersonalHistories()
    {
        foreach (SpecificClan clan in SpecificClanManager._specificClans ?? new List<SpecificClan>())
        foreach (PersonalClanIdentity person in clan?.SnapshotPeople() ?? Array.Empty<PersonalClanIdentity>())
            TrimPersonalHistory(person);
    }

    public static void RecordPersonalHistory(this PersonalClanIdentity identity, string content, string historyDate = null,
        string kingdomName = "", long empireId = -1L, string eventKey = "", long relatedActorId = -1L,
        long relatedPersonalIdentityId = -1L, bool major = false)
    {
        if (identity == null || EmpireWorld.Current == null || ModClass.IS_CLEAR || string.IsNullOrWhiteSpace(content)) return;
        double now = GameClock.Now;
        if (string.IsNullOrWhiteSpace(historyDate)) historyDate = GameClock.DateText(now);
        identity.personal_history ??= new List<PersonalHistoryRecord>();
        var record = new PersonalHistoryRecord
        {
            timestamp = now,
            date = historyDate,
            content = content,
            kingdom_name = kingdomName ?? "",
            empire_id = empireId,
            event_key = eventKey ?? "",
            related_actor_id = relatedActorId,
            related_personal_identity_id = relatedPersonalIdentityId,
            owner_personal_identity_id = identity.id,
            major = major || !BiographySystem.IsRoutine(content)
        };
        identity.personal_history.Add(record);
        TrimPersonalHistory(identity);
        PersonalClanIdentity relatedIdentity = relatedPersonalIdentityId > 0
            ? SpecificClanManager.getPerson(relatedPersonalIdentityId)
            : relatedActorId > 0 ? EmpireWorld.Current.PersonByKey(relatedActorId)?.GetPersonalIdentity() : null;
        relatedIdentity?.AddRelatedHistoryRecord(record);
    }
    public static void RemoveSpecificClan(this Person a)
    {
        if (a == null) return;
        var ed = a.GetOrCreate();
        ed.personal_identity = -1L;
    }
    public static void SetPersonalIdentity(this Person a, PersonalClanIdentity pci)
    {
        if (a == null) return;
        var ed = a.GetOrCreate();
        ed.personal_identity = pci.id;
    }
    public static void RemovePersonalIdentity(this Person a)
    {
        if (a == null) return;
        var ed = GetOrCreate(a);
        ed.personal_identity = -1L;
    }

    // 实际出生时间：原版成年落地(神力造人、自动文明)和无小人模式从人口落成的成年人，年龄记在"额外年龄"
    // (age_overgrowth)里，created_time 是落地那天，原版 getBirthday() 就把落地日当生日——族谱上一位四十岁的皇帝
    // 只比长子大几个月，像是五岁生子。按额外年龄往前推(原版新生儿额外年龄为 1，一年 60 秒)
    public static double TrueBirthTime(this Person a) =>
        a?.Record == null ? 0d : a.BirthStamp - Math.Max(0, a.Record.AgeOvergrowth - 1) * 12d;

    public static string TrueBirthday(this Person a) => GameClock.DateText(a.TrueBirthTime());

    public static PersonalClanIdentity InitialPersonalIdentity(this Person a, SpecificClan clan)
    {
        if (a == null) return null;
        var ed = a.GetOrCreate();
        PersonalClanIdentity pci = new PersonalClanIdentity();
        pci.newPersonalClanIdentity(clan, a);
        clan.Upsert(pci);
        SpecificClanManager._globalPersonLookup[pci.id] = pci;
        SpecificClanManager._actorToPersonLookup[pci.actor_id] = pci;
        a.SetPersonalIdentity (pci);
        if (a.HasSpouse())
        {
            pci.setLover(a.Spouse, recordHistory: false);
        }
        return pci;
    }

    public static Culture GetCulture(this Person a)
    {
        if (a == null) return null;
        if (a.HasCulture())
        {
            return a.Culture;
        } else
        {
            if (a.GetParents().Any())
            {
                foreach(Person parent in a.GetParents())
                {
                    if (parent.HasCulture())
                    {
                        return parent.Culture;
                    }
                }
            }
        }
        return null;
    }

    public static bool HasSpecificClan(this Person a)
    {
        if (a == null) return false;
        var ed = a.GetOrCreate();
        return ed.personal_identity != -1L;
    }

    public static void RemoveEmpire(this Person a)
    {
        GetOrCreate(a).empire_id = -1L;
    }

    public static bool AddViolateValue(this Person a, int value)
    {
        var officeIdentity =  a.GetOrCreate().officeIdentity;
        if (officeIdentity == null) return false;
        officeIdentity.ViolateLevel = (int)Math.Max(0L, Math.Min(100L, (long)officeIdentity.ViolateLevel + value));
        return true;
    }

    public static int GetViolateValue(this Person a)
    {
        var officeIdentity =  a.GetOrCreate().officeIdentity;
        if (officeIdentity == null) return -1;
        return officeIdentity.ViolateLevel;
    }

    public static Empire GetEmpire(this Person a)
    {
        if (GetOrCreate(a).empire_id==-1L)
        {
            return null;
        } else
        {
            return ModClass.EMPIRE_MANAGER.Get(GetOrCreate(a).empire_id);
        }
    }
    public static string HereditaryRealmRank(Person a)
    {
        Realm realm = a?.Realm;
        if (realm?.Record == null || realm.IsGone() || realm.Ruler != a || realm.IsEmpire() || !realm.IsInEmpire() ||
            realm.IsAdministrativeKingdomType()) return null;
        // 世袭(含原版默认继承)的才是封国；考选、推举、军功产生的长官不按封国定爵
        var method = realm.GetRegime()?.GetLeaderSelectMethod();
        if (method != EmpireCraft.Core.LeaderSelectMethod.Succession &&
            method != EmpireCraft.Core.LeaderSelectMethod.Default) return null;
        return realm.GetOffice()?.GetName(realm);
    }

    public static bool HasVirtualEnfeoff(this Person a, Empire empire = null)
    {
        if (a == null || (empire != null && empire.data == null)) return false;
        var data = GetOrCreate(a);
        return data.virtual_enfeoff && (empire == null || data.virtual_enfeoff_empire_id == empire.data.id);
    }
    public static bool HasHonoraryPeerage(this Person a, Empire empire = null)
    {
        if (a == null || (empire != null && empire.data == null)) return false;
        var data = GetOrCreate(a);
        return !string.IsNullOrEmpty(data.honorary_peerage_key) &&
               (empire == null || data.honorary_peerage_empire_id == empire.data.id);
    }
    public static bool GrantHonoraryPeerage(this Person a, Empire empire, string peerageKey)
    {
        if (a == null || a.IsGone() || empire == null || string.IsNullOrEmpty(peerageKey)) return false;
        Regime regime = empire.CoreKingdom?.GetRegime();
        if (regime?.type == RegimeType.Modern ||
            regime?.virtual_honorary_peerages?.Contains(peerageKey) != true || a.HasHonoraryPeerage() ||
            empire.IsHonoraryPeerageReserved(peerageKey)) return false;
        if (peerageKey == "tang_honorary_anle_gong" && empire.GetAnlePeeragePriority(a) <= 0) return false;
        a.CheckSpecificClan(false);
        var data = GetOrCreate(a);
        data.honorary_peerage_key = peerageKey;
        data.honorary_peerage_empire_id = empire.data.id;
        empire.data.honorary_peerage_holders ??= new Dictionary<string, long>();
        empire.data.honorary_peerage_holders[peerageKey] = a.Key;
        empire.RememberHonoraryPeerageHolder(peerageKey, a);
        TranslateHelper.LogHonoraryPeerageGranted(a, empire, peerageKey);
        return true;
    }
    public static void editRenown(this Person a, int value)
    {
        if (a?.Record == null) return;
        a.Renown = Math.Max(0, a.Renown + value);
    }

    public static int CalculateAnnualInfluenceGain(double annualPerformance)
    {
        // 年终政绩折名望：政绩的一半、每年至多 30(以前整份、至多 100；名望改身份年入、每年衰减一成后，
        // 整份折算会让内阁大员稳态多出上千、压过皇帝，见 RenownSystem)
        return Math.Max(0, Math.Min(30, (int)Math.Floor(annualPerformance / 2d)));
    }
    public static void AddOfficeExamLevel(this Person actor, EmpireExamLevel level)
    {
        if (GetOrCreate(actor).officeIdentity!=null)
        {
            GetOrCreate(actor).officeIdentity.empireExamLevels.Add(level);
            if (GetOrCreate(actor).officeIdentity.empireExamLevels.Count>4)
            {
                GetOrCreate(actor).officeIdentity.empireExamLevels.RemoveAt(0);
            }
            actor.JudgeOfficeLevel();
        }
    }

    public static void JudgeOfficeLevel(this Person actor)
    {
        var p = actor.GetIdentity().TotalPerformance;
        if (actor.Realm.GetRegime().type != RegimeType.LvLing || !OfficialRankSystem.UsesPerformanceLadder(actor.Realm))
        {
            OfficialRankSystem.OnPerformance(actor);
            return;
        }
        if (p > 1600)
        {
            actor.UpgradeOfficial(direct:0);
        }
        else if (p >1300)
        {
            actor.UpgradeOfficial(direct:1);
        }
        else if (p > 1000)
        {
            actor.UpgradeOfficial(direct:2);
        }
        else
        {
            var dir = 5 - (int)Math.Floor(5 * p / 1000.0f) + 3;
            actor.UpgradeOfficial(direct:dir);
        }
    }

    public static List<EmpireExamLevel> GetEmpireExamLevels(this Person actor)
    {
        List < EmpireExamLevel > levels= new List<EmpireExamLevel >();
        if (GetOrCreate(actor).officeIdentity != null)
        {
            levels = GetOrCreate(actor).officeIdentity.empireExamLevels;
        }
        return levels;
    }

    public static string GetEmpireExamLevelsString(this Person actor)
    {
        List<EmpireExamLevel> levels = actor.GetEmpireExamLevels();
        List<string> result = new List<string>();
        if (levels == null) return "";
        if (levels.Count<=0) return "";
        return String.Join(", ", levels.Select(e=>e.ToString()).ToList());
    }

    public static void ResetPerformance(this Person a)
    {
        OfficeIdentity identity = GetOrCreate(a).officeIdentity;
        if (identity != null) 
        {
            identity.OfficePerformance = 50;
        }
    }

    public static PeerageType GetPeerageType(this Person a)
    {
        return GetOrCreate(a).peerageType;
    }

    public static void SetPeerageType(this Person a, PeerageType type = PeerageType.Civil)
    {
        GetOrCreate(a).peerageType = type;
    }
    
    public static void initializeActorName(this Person a)
    {
        string culture_name = CultureService.GetFounderCulture(a);
        if (!CultureService.IsValidCulture(culture_name)) return;
        if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture_name, out Setting setting))
        {
            a.GetModName().Initialize(setting, culture_name);
        }
    }

    public static void SetFirstName(this Person a, string name)
    {
        if (a == null) return;
        if (name == null) return;
        if (name == "") return;
        a.GetOrCreate().name.firstName = name;
    }

    public static void SetFamilyName(this Person a, string name)
    {
        if (a == null) return;
        if (name == null) return;
        if (name == "") return;
        a.GetOrCreate().name.familyName = name;
    }

    public static bool HasOfficeIdentity(this Person a)
    {
        return a?.GetOrCreate().officeIdentity != null;
    }
    public static OfficeIdentity GetIdentity( this Person a)
    {
        if (!a.HasOfficeIdentity()) return null;
        if (GetOrCreate(a).officeIdentity.GetOfficeId()!=-1L)
        {
            if (GetOrCreate(a).officeIdentity.performanceEvents == null)
            {
                GetOrCreate(a).officeIdentity.performanceEvents = new PerformanceEvents();
                GetOrCreate(a).officeIdentity.performanceEvents.init(a);

            } else
            {
                GetOrCreate(a).officeIdentity.performanceEvents.init(a);
            }

        } 
        return GetOrCreate(a).officeIdentity;
    }

    public static void ChangeOfficialLevel(this Person a, int level)
    {
        if (a == null) return;
        GetOrCreate(a).officeIdentity.ChangeOfficialLevel(level);
    }

    public static void SetIdentityType(this Person a, PeerageType type=PeerageType.Civil)
    {
        if (a == null) return;
        GetOrCreate(a).officeIdentity.peerageType = type;
    }

    public static PeerageType GetIdentityType(this Person a)
    {
        return a == null ? PeerageType.Civil : GetOrCreate(a).officeIdentity.peerageType;
    }
    public static void SetIdentity(this Person a, OfficeIdentity identity, bool isInitial=false)
    {
        if (a == null) return;
        if (isInitial)
        {
            identity.honoraryOfficial = 8;
            identity.meritLevel = 10;
            identity.peerageType = PeerageType.Civil;
            OfficialRankSystem.CarryOver(GetOrCreate(a).officeIdentity, identity, a);
        }
        GetOrCreate(a).officeIdentity = identity;
    }

    public static void InitialIdentity(this Person a)
    {
        OfficeIdentity identity = new OfficeIdentity
        {
            actor_id = a.Key
        };
        a.SetIdentity(identity, true);
    }

    public static void RemoveIdentity(this Person a)
    {
        if (a != null) 
        {
            GetOrCreate(a).officeIdentity = null;
        }
    }
    public static bool isOfficer(this Person a)
    {
        if (a == null) return false;
        OfficeIdentity identity = GetOrCreate(a).officeIdentity;
        if (identity == null) return false;
        if (identity.officialLevel == -1) return false;
        return true;
    }
    public static bool isNoble(this Person a)
    {
        if (a == null) return false;
        return EmpireCaftActorJudgeClass.JudgeClass(a) == SocialClass.Noble;
    }

    public static bool CanGrabAlliance(this Person a)
    {

        return false;
    }


    public static bool CanAcquireTitle(this Person a)
    {
        return a != null && a.IsRuler() && a.Realm != null &&
               a.Realm.CanPursueDeJureTitle() && a.getAcquireTitle().Any();
    }

    public static void UpgradeOfficial(this Person a, bool merit = false, int direct = -1)
    {
        if (GetOrCreate(a).officeIdentity==null)
        {
            return;
        } else
        {
            OfficeIdentity identity = GetOrCreate(a).officeIdentity;
            if (merit)
            {
                if (identity.meritLevel <= 0)
                {
                    identity.meritLevel = 0;
                }
                else
                {
                    identity.meritLevel -= 1;
                }
                if (direct!=-1||direct>identity.meritLevel)
                {
                    identity.meritLevel = direct;
                }
            } else
            {
                if (direct != -1)
                {
                    // var original = identity.honoraryOfficial;
                    // if (original != direct)
                    // {
                    //     // //LogService.LogInfo("升官");
                    //     // if (direct <= 1)
                    //     // {
                    //     //     TranslateHelper.LogOfficeMove(a, identity.peerageType, direct);
                    //     // }
                    // }
                    identity.honoraryOfficial = direct;
                }
                else
                {
                    if (identity.honoraryOfficial <= 0)
                    {
                        identity.honoraryOfficial = 0;
                    }
                    else
                    {
                        identity.honoraryOfficial -= 1;
                    }
                }
                a.Renown += 5;
            }
            GetOrCreate(a).officeIdentity = identity;
        }
    }

    public static void DegradeOfficial(this Person a)
    {
        if (GetOrCreate(a).officeIdentity==null)
        {
            return;
        } else
        {
            OfficeIdentity identity = GetOrCreate(a).officeIdentity;

            if (identity.meritLevel >= 10)
            {
                identity.meritLevel = 10;
            }
            else
            {
                identity.meritLevel += 1;
            }
            if (identity.honoraryOfficial>=8)
            {
                identity.honoraryOfficial = 8;
            } else
            {
                identity.honoraryOfficial += 1;
            }
            // LogService.LogInfo("贬官");
            if(a.Renown>=5)
            {
                a.Renown -= 5;
            } else
            {
                a.Renown = 0;
            }
            GetOrCreate(a).officeIdentity = identity;
        }
    }

    public static string GetActorName(this Person a)
    {
        if (a == null) return null;
        if (string.IsNullOrEmpty(a.Name)) return null;
        string[] nameParts = a.Name.SplitNameParts();

        string culture = CultureService.GetActorCulture(a);
        if (CultureService.IsValidCulture(culture))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
            {
                if (nameParts.Length - 1 >= setting.Unit.name_pos)
                {
                    if (setting.Unit.is_invert)
                    {
                        return nameParts[setting.Unit.name_pos].Split(' ').Last();
                    } else
                    {
                        return nameParts[setting.Unit.name_pos].Split(' ').First();
                    }
                }
            }
        }
        return nameParts[0].Split(' ').Last();
    }

    public static List<KingdomTitle> getAcquireTitle(this Person a)
    {
        List<KingdomTitle> titles = new();
        if (a != null && a.IsRuler() && a.Realm != null)
        {
            Realm k = a.Realm;
            foreach (City city in k.Cities)
            {
                if (city.hasTitle())
                {
                    KingdomTitle title = city.GetTitle();
                    if (title?.data == null) continue;
                    Realm holder = title?.FindRealmTitleHolder();
                    if (title != null && !titles.Contains(title) &&
                        !a.GetOwnedTitle().Contains(title.data.id) &&
                        k.CanDeclareDeJureTitleWar(holder, title))
                    {
                        titles.Add(title);
                    }
                }
            }
        }
        return titles;
    }
    public static bool CanTakeCity(this Person pActor)
    {
        Realm kingdom = pActor.Realm;
        if (kingdom.IsGone()) return false;
        Empire empire = kingdom.GetEmpire();
        if (empire.IsGone()) return false;
        foreach (City city in kingdom.Cities)
        {
            if (city.IsGone()) continue;
            if (city.NeighbourCities.Count > 0)
            {
                foreach (City city2 in city.NeighbourCities)
                {
                    if (city2.Realm.IsInEmpire() && city2.Realm != kingdom && city2.Realm.IsEmpire() && !city2.IsCapital())
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
    public static string GetTitle(this Person a)
    {
        if (!a.HasTitle()) return "";
        if (!a.IsRuler()) return "";
        if (a.Realm == null) return "";
        var ownedTitles = a.GetOwnedTitle();
        if (ownedTitles == null) return "";
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.Get(ownedTitles.First());
        return title?.data?.name??"";
    }
    public static KingdomTitle GetMainTitle(this Person a)
    {
        var ownedTitles = a.GetOwnedTitle();
        if (ownedTitles == null) return null;
        if (ownedTitles.Count <= 0) return null;
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.Get(ownedTitles.First());
        return title;
    }
    public static bool SetMainTitle(this Person a, KingdomTitle title)
    {
        var ownedTitles = a.GetOwnedTitle();
        if (ownedTitles == null) return false;
        if (ownedTitles.Contains(title.Key))
        {
            var index = ownedTitles.IndexOf(title.Key);
            ownedTitles.Swap(index, 0);
        }
        else
        {
            ownedTitles.Insert(0, title.Key);
        }
        if (a.IsRuler())
        {
            if (a.Realm.Cities.Any(c => c.GetTitle() == title))
            {
                a.Realm.SetMainTitle(title);
            }
        }
        return true;
    }
    public static bool HasTitle(this Person a)
    {
        if(a == null) return false;
        if(GetOrCreate(a).owned_title==null) return false;
        return GetOrCreate(a).owned_title.Count>0;
    }

    public static bool HasCapitalTitle(this Person a)
    {
        if (a.Realm.Capital.hasTitle())
        {
            return a.GetOwnedTitle().Contains(a.Realm.Capital.GetTitle().data.id);
        }
        return false;
    }

    public static bool IsCapitalTitleBelongsToEmperor(this Person a)
    {
        if (!a.HasRealm()) return false;
        if (a.Realm.GetEmpire() == null) return false;
        if (a.Realm.GetEmpire().Emperor.GetOwnedTitle()==null) return false;
        if (a.Realm.Capital.GetTitle()==null) return false;
        if (a.Realm.IsInEmpire())
        {
            if (a.Realm.Capital.hasTitle())
            {
                return a.Realm.GetEmpire().Emperor.GetOwnedTitle().Contains(a.Realm.Capital.GetTitle().data.id);
            }
        }
        return false;
    }


    // 只问有没有：找到第一个就停(决议条件里每个君主都要问，不必把全部法理算完)
    public static bool canTakeTitle(this Person a)
    {
        return TakeableTitles(a).Any();
    }

    public static List<KingdomTitle> titleCanBeDestroy(this Person a)
    {
        List<KingdomTitle> titles = new List<KingdomTitle>();
        foreach(long id in a.GetOwnedTitle())
        {
            if (ModClass.KINGDOM_TITLE_MANAGER.checkTitleExist(id))
            {
                KingdomTitle kt = ModClass.KINGDOM_TITLE_MANAGER.Get(id);
                if (kt == null) continue;
                // 只销毁被本国完整控制满年限的法理(它的城市会全部并入主法理)。
                // 城市分属多国(时间戳为 -1)或由别国控制(如现代国家收归中央、由省州受托管理的法理)的不能销毁，
                // 否则别国手里的城市会被整片移出法理
                if (kt.control_kingdom == a.Realm && kt.data.timestamp_been_controlled > 0 &&
                    kt.GetTitleBeenControlledYear() >= ModClass.TITLE_BEEN_DESTROY_TIME && kt != a.Realm.GetMainTitle())
                {
                    titles.Add(kt);
                }
            }
        }
        // 法理扩张有城市上限：销毁一个法理意味着它在本国的城市要并进主法理，并不进去的法理就不销毁
        int max = KingdomTitleManager.MaxCitiesPerTitle;
        if (max <= 0) return titles;
        KingdomTitle mainTitle = a.Realm?.GetMainTitle();
        int room = max - (mainTitle != null ? KingdomTitleManager.CountTitleCities(mainTitle) : 1);
        var destroyable = new List<KingdomTitle>();
        foreach (KingdomTitle kt in titles)
        {
            int absorbed = kt.GetCities().Count(city => city != null && !city.IsGone() && city.Realm == a.Realm);
            if (absorbed > room) continue;
            room -= absorbed;
            destroyable.Add(kt);
        }
        return destroyable;
    }

    public static List<KingdomTitle> takeTitle(this Person a)
    {
        List<KingdomTitle> takedTitles = new List<KingdomTitle>();
        if (a == null || !a.IsRuler() || a.Realm == null) return takedTitles;

        Realm kingdom = a.Realm;
        if (kingdom.HasTakenAlliance()) return takedTitles;
        List<KingdomTitle> titles = GetTakeableTitles(a);
        foreach(KingdomTitle t in titles)
        {
            if (kingdom.CanAcquireDeJureTitleWithoutWar(t))
            {
                Realm previousRealm = t.FindRealmTitleHolder();
                if (kingdom.InheritRealmTitles(previousRealm, new[] { t.Key }).Contains(t))
                {
                    takedTitles.Add(t);
                    if (kingdom.GetMainTitle() == null) kingdom.SetMainTitle(t);
                }
                continue;
            }
            if(t.HasOwner()&&t.owner.IsEmperor())
            {
                a.AddAcquireTitle(t);
                takedTitles.Add(t);
                continue;
            }
            if (t.main_kingdom!=null)
            {
                t.main_kingdom.RemoveMainTitle();
                t.main_kingdom = null;
            }
            if (!a.GetOwnedTitle().Contains(t.Key))
            {
                takedTitles.Add(t);
                if (t.HasOwner())
                {
                    t.owner.removeTitle(t);
                }
                a.AddOwnedTitle(t);
            }
        }
        return takedTitles;
    }

    private static List<KingdomTitle> GetTakeableTitles(Person actor) => TakeableTitles(actor).ToList();

    // 可取的法理：本国控制的在前，其余不经战争就能取的在后(各算一次)，再按归属筛
    private static IEnumerable<KingdomTitle> TakeableTitles(Person actor)
    {
        if (actor == null || !actor.IsRuler() || actor.Realm == null || actor.Realm.IsGone()) yield break;

        Realm kingdom = actor.Realm;
        var seen = new HashSet<KingdomTitle>();
        List<long> ownedTitles = actor.GetOwnedTitle();
        List<long> acquiredTitles = null;
        foreach (KingdomTitle title in kingdom.GetControlledTitle())
        {
            if (title == null || title.IsGone() || title.data == null || !seen.Add(title)) continue;
            if (Takeable(title, kingdom.CanAcquireDeJureTitleWithoutWar(title))) yield return title;
        }
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            if (title == null || seen.Contains(title) || !kingdom.CanAcquireDeJureTitleWithoutWar(title)) continue;
            seen.Add(title);
            if (Takeable(title, true)) yield return title;
        }

        bool Takeable(KingdomTitle title, bool withoutWar)
        {
            if (ownedTitles.Contains(title.Key)) return false;
            if (kingdom.IsDeJureTitleHeldBySovereign(title)) return false;
            if (IsLvLingTitleProtected(title, actor, out _)) return false;
            if (withoutWar) return true;
            Realm realmHolder = title.FindRealmTitleHolder();
            if (realmHolder != null && realmHolder != kingdom) return false;
            if (!title.HasOwner() || !title.owner.IsEmperor()) return true;
            acquiredTitles ??= actor.GetAcquireTitle();
            return title.owner.Key != actor.Key && !acquiredTitles.Contains(title.Key);
        }
    }

    private static bool IsLvLingTitleProtected(KingdomTitle title, Person claimant, out Empire empire)
    {
        empire = null;
        EmpireCore core = title?.title_capital?.GetEmpireCore();
        if (core == null || claimant == null) return false;
        empire = EmpireCoreManager.GetEmpires(core).FirstOrDefault(e => e != null && !e.IsArchived());
        return empire != null && claimant.Realm.IsDeJureTitleAcquisitionBlocked(empire);
    }

    public static void AddAcquireTitle(this Person a, KingdomTitle title)
    {
        if (a == null || title?.data == null) return;
        var ed = GetOrCreate(a);
        ed.want_acuired_title ??= new List<long>();
        if (!ed.want_acuired_title.Contains(title.data.id)) ed.want_acuired_title.Add(title.data.id);
    }

    public static void AddOwnedTitle(this Person a, KingdomTitle title)
    {
        var ed = GetOrCreate(a);
        if (ed == null) return;
        if (title == null) return;
        if (ed.owned_title==null) ed.owned_title = new List<long> { 0 };
        if (!ed.owned_title.Contains(title.data.id))
        {
            ed.owned_title.Add(title.data.id);
            title.owner = a;
        }
        if (a.IsRuler() && a.Realm != null && a.Realm.IsTitleWithinRealm(title))
        {
            a.Realm.RegisterRealmTitle(title);
        }
    }

    public static void removeTitle(this Person a, KingdomTitle title)
    {
        if (title == null) return;
        if (a == null) return;
        if (title.data == null) return;
        if (a.Realm.IsGone()) return;
        if (a.Realm.Capital == null) return;
        var ed = GetOrCreate(a);
        if (a.GetOwnedTitle().Contains(title.data.id))
        {
            if (!a.IsEmperor()&&a.IsRuler())
            {
                if (a.Realm.GetKingdomName()==title.data.name)
                {
                    a.Realm.EmpireLeave();
                }
                if (a.Realm.GetMainTitle() == title)
                {
                    a.Realm.RemoveMainTitle();
                    EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(a.Realm);
                }
            }
            ed.owned_title.Remove(title.data.id);
            title.owner = null;
            if (a.IsRuler() && a.Realm != null)
            {
                a.Realm.UnregisterRealmTitle(title);
            }
        }
    }

    public static List<long> GetAcquireTitle(this Person a)
    {
        var ed = GetOrCreate(a);
        ed.want_acuired_title ??= new List<long>();
        return ed.want_acuired_title;
    }

    public static List<long> GetOwnedTitle(this Person a)
    {
        if (a == null) return null;
        var ed = GetOrCreate(a);
        return ed.owned_title;
    }
    public static Name GetModName(this Person a)
    {
        var ed = GetOrCreate(a);
        if (ed == null) return null;
        if (ed.name == null)
        {
            ed.name = new Name();
        }
        return ed.name;
    }

    public static void ClearTitle(this Person a)
    {
        var ed = GetOrCreate(a);
        ed.owned_title.Select(t=>ModClass.KINGDOM_TITLE_MANAGER.Get(t)!.owner=null);
        ed.owned_title.Clear();
        ed.want_acuired_title.Clear();
    }

    public static bool IsEmperor(this Person a)
    {
        if (a==null) return false;
        var empire = ModClass.EMPIRE_MANAGER.Get(GetOrCreate(a).empire_id);
        if (empire?.Emperor == a) return true;

        Empire kingdomEmpire = a.Realm?.GetEmpire();
        return kingdomEmpire?.CoreKingdom == a.Realm && kingdomEmpire.Emperor == a;
    }

    public static void SetEmpire(this Person a, Empire empire)
    {
        a.GetOrCreate().empire_id = empire.data.id;
    }

    public static ActorExtraData GetOrCreate(this Person a, bool isSave=false)
    {
        return a.GetOrCreate<Person, ActorExtraData>(isSave); ; 
    }
    public static PeeragesLevel GetPeeragesLevel(this Person a)
        => GetOrCreate(a).peeragesLevel;
    public static void SetPeeragesLevel(this Person a, PeeragesLevel lvl)
    {
        
        var data = GetOrCreate(a);
        data.id = a.Key;
        data.peeragesLevel = lvl;
    }

    public static string GetPeerageDisplayName(this Person a)
    {
        if (a == null) return "";
        var data = GetOrCreate(a);
        if (a.IsEmperor())
        {
            if (data.peeragesLevel != PeeragesLevel.peerages_0)
            {
                a.SetPeeragesLevel(PeeragesLevel.peerages_0);
            }
            string emperorSuffix = LM.Get("emperor_suffix") ?? LM.Get("default_peerages_0") ?? "";
            return a.GetTitle() + emperorSuffix;
        }

        // 世袭封国的国君：爵位就是封国的等级，以政体给这类封国定的官称为准(周制按宗室与规模定公侯伯子)，
        // 不再另按功勋等级或虚封爵名(唐制国公)显示，避免"官称湘侯、爵位湘国公"。
        // 受托管理的道、军等行政区长官没有封国，爵位仍按个人功勋/虚封
        string realmRank = HereditaryRealmRank(a);
        if (!string.IsNullOrWhiteSpace(realmRank)) return realmRank;

        bool honorary = a.HasHonoraryPeerage();
        // 封了王却没有实封法理的皇子(见 PrinceLineage.GrantPrinces)：爵位就是族谱记的王号
        if (!honorary && !a.HasVirtualEnfeoff() && data.peeragesLevel == PeeragesLevel.peerages_2 &&
            a.GetPersonalIdentity()?.prince_title is string princeTitle && !string.IsNullOrWhiteSpace(princeTitle))
            return princeTitle;
        string peerageKey = honorary ? data.honorary_peerage_key : data.virtual_enfeoff_peerage_key;
        if (a.HasVirtualEnfeoff() && string.IsNullOrWhiteSpace(peerageKey))
        {
            Empire empire = ModClass.EMPIRE_MANAGER.Get(data.virtual_enfeoff_empire_id);
            peerageKey = a.GetSpecificClan() != null && SpecificClanManager.SameLineage(a.GetSpecificClan(), empire?.EmpireSpecificClan)
                ? "default_peerages_2"
                : "tang_peerage_guogong";
            data.virtual_enfeoff_peerage_key = peerageKey;
            a.SetPeeragesLevel(peerageKey == "default_peerages_2"
                ? PeeragesLevel.peerages_2
                : PeeragesLevel.peerages_3);
        }
        string suffix = LM.Get(string.IsNullOrWhiteSpace(peerageKey) ? "default_" + a.GetPeeragesLevel() : peerageKey) ?? "";
        string prefix = "";
        if (!honorary && a.HasVirtualEnfeoff())
        {
            long titleId = data.virtual_enfeoff_title_id;
            prefix = titleId > 0 ? ModClass.KINGDOM_TITLE_MANAGER.Get(titleId)?.data?.name : null;
            prefix ??= a.City?.GetCityName() ?? "";
        }
        else if (!honorary && a.HasTitle())
        {
            prefix = a.GetTitle();
        }
        return JoinRealmRank(prefix, suffix);
    }

    // 单字国名接爵称要补“国”：湘国公、湘国侯、湘国伯；王号照旧(湘王)
    private static readonly HashSet<string> RealmRanks = new() { "公", "侯", "伯", "子", "男" };
    public static string JoinRealmRank(string prefix, string rank)
    {
        prefix ??= "";
        rank ??= "";
        return prefix.Length == 1 && RealmRanks.Contains(rank) ? prefix + "国" + rank : prefix + rank;
    }
}
}
