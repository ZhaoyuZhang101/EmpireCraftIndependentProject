// Generated from the EmpireCraft mod: Scripts/Regimes/TemporaryFactions/TemporaryFaction.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace EmpireCraft.Core
{
[JsonConverter(typeof(TemporaryFactionConverter))]
public abstract class TemporaryFaction
{
    [JsonIgnore]
    public TemporaryFactionType type => ResolveType(GetType().Name);

    internal static TemporaryFactionType ResolveType(string name)
    {
        string key = name.StartsWith("KingdomMind_", StringComparison.Ordinal)
            ? "国_" + name.Substring("KingdomMind_".Length)
            : name.StartsWith("TempFac_", StringComparison.Ordinal) ? name.Substring("TempFac_".Length)
                : name.Split('_').Last();
        return Enum.TryParse(key, out TemporaryFactionType result) ? result : default;
    }

    public int Acc = 0;
    public bool Hide = false;
    public bool Active = true;
    public int CountDown = 0;
    // 施行经费(见 ClaimTuning)：朝廷推动的由国库出，地方推动的由该地出
    public virtual int Budget => ClaimTuning.Cost(GetEmpire(), type, IsLocallyPushed ? GetKingdom() : null);
    // 本轮经费是否已付、付了多少(没推进就作罢的退回)
    public bool BudgetPaid;
    public int PaidBudget;
    public List<long>  kingdoms = new List<long>();
    public string factionID = "";
    public bool ShowAsPlot = false;
#if EC_PENDING
    [JsonIgnore] 
    public AdvancedButton hideButton;
    [JsonIgnore] 
    public AdvancedButton localPushButton;
    [JsonIgnore] 
    public AdvancedButton activeButton;
    [JsonIgnore] 
    public AdvancedButton showButton;
#endif
    public long EmpireID = -1L;
    public long KingdomID = -1L;
    public long TargetID = -1L;
    public bool canBePushByLocal = false;
    public EntityKind pusherType = EntityKind.None;
    public EntityKind TargetType = EntityKind.None;
    // 神力指定的目标(用户 2026-11-11 "这里在发起诉求时应该给个目标""比如兴兵讨伐")：从派系窗口发起时选的目标另存一份。
    // 目标本来由朝廷自己挑(各决议的 CheckCondition)，从上奏到施行其间界面与 AI 每查一次条件就重挑一回；指定了的，重挑时照它
    // (见 PickRuleTarget)。施行完(End)、奏议没施行就收场(CourtSystem.Close)、议案没通过(CourtSystem.GodPropose)时清掉
    public long DesignatedID = -1L;
    public EntityKind DesignatedType = EntityKind.None;
    [JsonIgnore]
    public bool HasDesignated => DesignatedType != EntityKind.None && DesignatedID >= 0;
    [JsonIgnore]
    public FixedFaction ProposerFaction => GetFaction();
    [JsonIgnore]
    public bool IsLocallyPushed => pusherType == EntityKind.Realm && KingdomID > 0;
    [JsonIgnore]
    public Empire Empire => GetEmpire();
    public float progress = 0;
    public float progressMax = 60;
    [JsonIgnore]
    public float acceleration => (GetEmpire()?.data.additions.cabinet_acc??0)+(GetEmpire()?.data.additions.addition[OfficerPowerType.审核]??0)+Acc;
    private bool started = false;
    public double timestamp = -1L;
    public double countDownTimestamp = -1L;
    [JsonProperty("started")]
    public bool StartedState
    {
        get => started;
        set => started = value;
    }
    [JsonIgnore]
    public virtual bool RequireCrimeTarget => false;
    [JsonIgnore]
    public virtual bool RequireRenown => false;
    // Public reform campaigns keep their own eligibility checks outside the cabinet.
    public virtual bool CanProceedOutsideGovernment => false;
    [JsonIgnore]
    public string CompletionOutcome { get; protected set; }
    // 本轮是否真的执行了(进度满或谋划完成)；End 时记入 LastRunCompleted，朝堂据此结算奏议
    [Newtonsoft.Json.JsonIgnore] public bool ExecutedThisRun;
    [Newtonsoft.Json.JsonIgnore] public bool LastRunCompleted;

    // 执行诉求的统一入口(进度推满、谋划完成都走这里)
    public void RunExecute()
    {
        ExecutedThisRun = true;
        Execute();
        if (CooldownYears > 0) MarkDone();
    }

    // 施行后隔几年才能再提(0 = 不限)；各议题自己声明，CheckCondition 里用 OffCooldown 判断
    [JsonIgnore]
    public virtual int CooldownYears => 0;

    public bool OffCooldown()
    {
        Empire empire = GetEmpire();
        if (CooldownYears <= 0 || empire?.data?.claim_done_at == null || EmpireWorld.Current == null) return true;
        return !empire.data.claim_done_at.TryGetValue(type.ToString(), out double at) || at > GameClock.Now ||
               GameClock.YearsSince(at) >= CooldownYears;
    }

    private void MarkDone()
    {
        Empire empire = GetEmpire();
        if (empire?.data == null || EmpireWorld.Current == null) return;
        empire.data.claim_done_at ??= new Dictionary<string, double>();
        empire.data.claim_done_at[type.ToString()] = GameClock.Now;
    }
    public virtual float RequireRenownMultiplier => 1f;

    public bool OwnEnoughRenown(Realm target)
    {
        if ((GetEmpire().Emperor?.Renown??0f)==0f) return false;
        return (GetEmpire().Emperor?.Renown ?? 0f) >(target?.Ruler?.Renown ?? 0f) * RequireRenownMultiplier ;
    }
    public void CopyRuntimeStateFrom(TemporaryFaction other)
    {
        if (other == null) return;

        Acc = other.Acc;
        Hide = other.Hide;
        Active = other.Active;
        CountDown = other.CountDown;
        kingdoms = other.kingdoms != null ? new List<long>(other.kingdoms) : new List<long>();
        factionID = other.factionID;
        ShowAsPlot = other.ShowAsPlot;
        EmpireID = other.EmpireID;
        KingdomID = other.KingdomID;
        TargetID = other.TargetID;
        canBePushByLocal = other.canBePushByLocal;
        pusherType = other.pusherType;
        TargetType = other.TargetType;
        DesignatedID = other.DesignatedID;
        DesignatedType = other.DesignatedType;
        progress = other.progress;
        progressMax = other.progressMax;
        BudgetPaid = other.BudgetPaid;
        PaidBudget = other.PaidBudget;
        StartedState = other.StartedState;
        timestamp = other.timestamp;
        countDownTimestamp = other.countDownTimestamp;
    }
    public virtual void Init(FixedFaction faction)
    {
        factionID = faction.GetID();
        EmpireID    = faction.EmpireId;
        progressMax = ClaimTuning.Duration(type);
        if (timestamp < 0) timestamp = GameClock.Now;
        kingdoms ??= new List<long>();
        if (countDownTimestamp < 0) countDownTimestamp = GameClock.Now;
    }
    public abstract TemporaryFaction Clone(FixedFaction faction);
    public bool IsNeedToCountDown()
    {
        if (GameClock.YearsSince(countDownTimestamp) > 1)
        {
            countDownTimestamp =  GameClock.Now;
            return true;
        }
        return false;
    }
    public void SetEmpire(Empire pEmpire)
    {
        this.EmpireID = (pEmpire == null || pEmpire.IsGone() || pEmpire.IsArchived()) ? -1L : pEmpire.Key;
    }  
    public void SetKingdom(Realm pKingdom)
    {
        this.KingdomID = pKingdom?.Record == null || pKingdom.IsGone() ? -1L : pKingdom.Key;
    }    
    public Realm GetKingdom()
    {
        Realm kingdom = EmpireWorld.Current?.RealmByKey(KingdomID);
        return kingdom?.Record == null || kingdom.IsGone() ? null : kingdom;
    }    
    
    #region 目标规则与神力指定(数据在 ClaimRules.json 的 target，见 ClaimTargetRules；政体编辑器的决议页可改)

    // 这条决议的目标能不能由神力指定
    [JsonIgnore]
    public bool CanDesignate => ClaimTargetRules.Has(type);

    // 决议自己的前提(冷却、忙不过来、国库……，不含挑目标)：目标规则在数据里的决议把 CheckCondition 拆成这一步加 PickRuleTarget
    protected virtual bool TargetReady() => true;

    // 朝廷自己挑时走决议自带写法的(ClaimRules.json 里 auto = builtin)在这里挑、设好目标
    protected virtual bool PickBuiltinTarget() => false;

    // 决议自带的候选(ClaimRules.json 里 pool = builtin)：候选怎么来的判断太专门、数据里写不下的，在各决议里列。
    // 按朝廷自己挑的先后排(没有加分项时取第一个可选的)；可以自带缘由、中间一列的数，也可以列出不可选的并写明原因
    public virtual List<ClaimTargetOption> BuiltinCandidates() => null;

    // 这个成员国眼下有没有本决议用得上的罪名(不要罪名的决议恒为真；目标规则 has_crime)。
    // 要不要罪名有的决议看目标(撤销军府：世袭的才要)，所以先把目标临时换成它再问
    public bool TargetCrimeOk(Realm kingdom)
    {
        (long id, EntityKind type) old = (TargetID, TargetType);
        SetKingdomTarget(kingdom);
        bool ok = HasRequiredCrimeForCurrentTarget();
        TargetID = old.id;
        TargetType = old.type;
        return ok;
    }

    // 全部候选(连同不可选的与原因)；不能指定的返回 null，前提没过的返回空表
#if EC_PENDING
    public List<ClaimTargetOption> TargetOptions() =>
        !CanDesignate ? null : TargetReady() ? ClaimTargetRules.Options(this) : new List<ClaimTargetOption>();
#endif

    // 神力眼下有没有可指定的目标(前提过了、候选里有过关的)
#if EC_PENDING
    public bool HasSelectableTarget() => CanDesignate && TargetReady() && ClaimTargetRules.AnySelectable(this);
#endif

    public void Designate(ClaimTargetOption option)
    {
        DesignatedID = option?.Id ?? -1L;
        DesignatedType = option == null || DesignatedID < 0 ? EntityKind.None : option.Type;
    }

    public void ClearDesignated()
    {
        DesignatedID = -1L;
        DesignatedType = EntityKind.None;
    }

    // 按目标规则定目标：神力指定了的照指定的(它眼下不合规则就算条件不满足，不改挑别的)，否则朝廷自己挑。没有可选的返回 false
#if EC_PENDING
    protected bool PickRuleTarget()
    {
        if (!CanDesignate)
        {
            ClaimTargetRules.WarnMissing(type);
            return false;
        }
        if (HasDesignated)
        {
            foreach (ClaimTargetOption option in ClaimTargetRules.Options(this))
            {
                if (!option.Selectable || option.Type != DesignatedType || option.Id != DesignatedID) continue;
                ApplyTarget(option);
                return true;
            }
            return false;
        }
        if (ClaimTargetRules.BuiltinAuto(type)) return PickBuiltinTarget();
        ClaimTargetOption auto = ClaimTargetRules.Auto(this);
        if (auto == null) return false;
        ApplyTarget(auto);
        return true;
    }
#endif

    private void ApplyTarget(ClaimTargetOption option)
    {
        if (option.Type == EntityKind.Realm) SetKingdomTarget(option.Realm);
        else if (option.Type == EntityKind.City) SetCityTarget(option.City);
        else if (option.Type == EntityKind.Person) SetActorTarget(option.Person);
        else if (option.Type == EntityKind.Religion) SetReligionTarget(option.Religion);
        else SetTitleTarget(option.Title);
    }

    // 不指定时朝廷会挑谁(选择窗口里标出来)：照自己挑的规矩走一遍，目标与指定都还原
#if EC_PENDING
    public ClaimTargetOption AutoTarget()
    {
        if (!CanDesignate || !TargetReady()) return null;
        if (!ClaimTargetRules.BuiltinAuto(type)) return ClaimTargetRules.Auto(this);
        long designatedId = DesignatedID, targetId = TargetID;
        EntityKind designatedType = DesignatedType, targetType = TargetType;
        ClearDesignated();
        ClaimTargetOption picked = null;
        if (PickBuiltinTarget())
            picked = ClaimTargetRules.Options(this).FirstOrDefault(option => option.Type == TargetType && option.Id == TargetID);
        DesignatedID = designatedId;
        DesignatedType = designatedType;
        TargetID = targetId;
        TargetType = targetType;
        return picked;
    }
#endif

    // 临时把目标换成这个候选来估算(各派因目标而起的立场，见 TargetStance)；用完要 RestoreTarget
    public (long id, EntityKind type) SwapTarget(ClaimTargetOption option)
    {
        (long id, EntityKind type) old = (TargetID, TargetType);
        TargetID = option?.Id ?? -1L;
        TargetType = option == null ? EntityKind.None : option.Type;
        return old;
    }

    public void RestoreTarget((long id, EntityKind type) old)
    {
        TargetID = old.id;
        TargetType = old.type;
    }

    // 眼下的目标叫什么(没有为 null)
    public string TargetLabel() => TargetType switch
    {
        EntityKind.Realm => GetKingdomTarget()?.GetKingdomName(),
        EntityKind.City => GetCityTarget()?.GetCityName(),
        EntityKind.Person => GetActorTarget()?.FullName(),
        EntityKind.Religion => GetReligionTarget()?.data?.name,
        _ => TargetType == MetaTypeExtension.KingdomTitle ? GetTitleTarget()?.data?.name : null
    };

    #endregion

    // 统一入口：设定“国家目标”
    public void SetKingdomTarget(Realm k, string reason = "")
    {
        var id = k?.Record == null || k.IsGone() ? -1L : k.Key;
        TargetID = id;
        TargetType = EntityKind.Realm;
    }
    
    protected Realm GetKingdomTarget()
    {
        if (TargetType != EntityKind.Realm || TargetID < 0) return null;
        Realm kingdom = EmpireWorld.Current?.RealmByKey(TargetID);
        return kingdom?.Record == null || kingdom.IsGone() ? null : kingdom;
    }      
    
    // 统一入口：设定“头衔目标”
    protected void SetTitleTarget(KingdomTitle k, string reason = "")
    {
        var id = k?.data == null || k.IsGone() ? -1L : k.Key;
        TargetID = id;
        TargetType = MetaTypeExtension.KingdomTitle;
    }
    
    protected KingdomTitle GetTitleTarget()
    {
        if (TargetType != MetaTypeExtension.KingdomTitle || TargetID < 0) return null;
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER?.Get(TargetID);
        return title?.data == null || title.IsGone() ? null : title;
    }    
    
    // 统一入口：设定“国家目标”
    protected void SetCityTarget(City k, string reason = "")
    {
        var id = k?.Record == null || k.IsGone() ? -1L : k.Key;
        TargetID = id;
        TargetType = EntityKind.City;
    }
    
    protected City GetCityTarget()
    {
        if (TargetType != EntityKind.City || TargetID < 0) return null;
        City city = EmpireWorld.Current?.CityByKey(TargetID);
        return city?.Record == null || city.IsGone() ? null : city;
    }   
    
    // 统一入口：设定“宗教目标”
    protected void SetReligionTarget(Religion k, string reason = "")
    {
        var id = k?.data == null || k.IsGone() ? -1L : k.Key;
        TargetID = id;
        TargetType = EntityKind.Religion;
    }
    
    protected Religion GetReligionTarget()
    {
        if (TargetType != EntityKind.Religion || TargetID < 0) return null;
        Religion religion = EmpireWorld.Current?.Religions?.Get(TargetID);
        return religion?.data == null || religion.IsGone() ? null : religion;
    }
    
    protected void SetActorTarget(Person pActor)
    {
        this.TargetType = EntityKind.Person;
        this.TargetID = pActor?.Record == null || pActor.IsGone() ? -1L : pActor.Key;
    }

    protected Person GetActorTarget()
    {
        if (TargetType == EntityKind.Person)
        {
            Person actor = EmpireWorld.Current?.PersonByKey(TargetID);
            return actor?.Record == null || actor.IsGone() ? null : actor;
        }

        return null;
    }

    protected bool TrySetTarget(Realm kingdom, string reason = "")
    {
        if (kingdom?.Record == null || kingdom.IsGone()) return false;
        if (!CanUseCrimeRestrictedTarget(kingdom?.Ruler, kingdom))
        {
            return false;
        }

        SetKingdomTarget(kingdom, reason);
        return true;
    }

    protected bool TrySetTarget(Person actor)
    {
        if (actor?.Record == null || actor.IsGone()) return false;
        if (!CanUseCrimeRestrictedTarget(actor, actor?.Realm))
        {
            return false;
        }

        SetActorTarget(actor);
        return true;
    }

    protected bool TrySetTarget(City city, string reason = "")
    {
        if (city?.Record == null || city.IsGone()) return false;
        Person actor = city?.Leader ?? city?.Realm?.Ruler;
        Realm kingdom = city?.Realm;
        if (!CanUseCrimeRestrictedTarget(actor, kingdom))
        {
            return false;
        }

        SetCityTarget(city, reason);
        return true;
    }

    protected bool TrySetTarget(Religion religion, string reason = "")
    {
        if (religion?.data == null || religion.IsGone()) return false;
        if (RequireCrimeTarget)
        {
            return false;
        }

        SetReligionTarget(religion, reason);
        return true;
    }

    protected bool TrySetTarget(KingdomTitle title, string reason = "")
    {
        if (title?.data == null || title.IsGone()) return false;
        Realm kingdom = title?.title_capital?.Realm;
        if (!CanUseCrimeRestrictedTarget(kingdom?.Ruler, kingdom))
        {
            return false;
        }

        SetTitleTarget(title, reason);
        return true;
    }

    protected bool HasRequiredCrimeForCurrentTarget()
    {
        if (!RequireCrimeTarget)
        {
            return true;
        }

        GetCrimeScopeForCurrentTarget(out Person actor, out Realm kingdom);
        return CanUseCrimeRestrictedTarget(actor, kingdom);
    }

#if EC_PENDING
    protected bool TryEnforceCrimeForCurrentTarget()
    {
        if (!RequireCrimeTarget)
        {
            return true;
        }

        GetCrimeScopeForCurrentTarget(out Person actor, out Realm kingdom);
        if (!CanUseCrimeRestrictedTarget(actor, kingdom)) return false;
        return EmpireLawSystem.TryEnforceCrimeForClaim(actor, kingdom) != null;
    }
#endif

    public string GetCurrentTargetCrimeName()
    {
        if (!RequireCrimeTarget)
        {
            return null;
        }

        GetCrimeScopeForCurrentTarget(out Person actor, out Realm kingdom);
        if (!CanUseCrimeRestrictedTarget(actor, kingdom)) return null;
        return EmpireLawSystem.GetResolvableCrimeName(actor, kingdom);
    }

    private bool CanUseCrimeRestrictedTarget(Person actor, Realm kingdom)
    {
        if (!RequireCrimeTarget)
        {
            return true;
        }

        if (actor == null || actor.IsGone() || kingdom == null || kingdom.IsGone())
        {
            return false;
        }

        if (IsLocallyPushed && actor == GetEmpire()?.Emperor) return false;
        return EmpireLawSystem.CanEnforceLawInEmpireScope(actor, kingdom) &&
               EmpireLawSystem.HasResolvableCrimeRecord(actor, kingdom);
    }

    private void GetCrimeScopeForCurrentTarget(out Person actor, out Realm kingdom)
    {
        actor = null;
        kingdom = null;

        switch (TargetType)
        {
            case EntityKind.Realm:
                kingdom = GetKingdomTarget();
                actor = kingdom?.Ruler;
                return;
            case EntityKind.City:
                City city = GetCityTarget();
                kingdom = city?.Realm;
                actor = city?.Leader ?? kingdom?.Ruler;
                return;
            case EntityKind.Person:
                actor = GetActorTarget();
                kingdom = actor?.Realm;
                return;
            default:
                if (TargetType == MetaTypeExtension.KingdomTitle)
                {
                    kingdom = GetTitleTarget()?.title_capital?.Realm;
                    actor = kingdom?.Ruler;
                }

                return;
        }
    }

  

#if EC_PENDING
    protected bool CheckRebelling(Realm kingdom)
    {
        Empire empire = GetEmpire();
        if (kingdom == null || kingdom.IsGone() || empire == null || empire.IsGone() ||
            empire.CoreKingdom == null || empire.CoreKingdom.IsGone() || kingdom == empire.CoreKingdom ||
            !RebellionSystem.CanAttempt(kingdom) || !CityStabilitySystem.CanRise(kingdom.Capital)) return false;
        var targetFaction = kingdom?.Ruler?.GetFaction();
        //全部势力
        var cities = new List<City>();
        if (targetFaction != null)
        {
            var targetMembers = targetFaction.Members?.ToList() ?? new List<long>();
            if (targetFaction != GetFaction())
            {
                foreach (var a in targetMembers.ToList())
                {
                    if (targetMembers.Contains(a))
                    {
                        Person actor = EmpireWorld.Current.PersonByKey(a);
                        if (actor?.IsRuler() ?? false)
                        {
                            targetMembers.Remove(a);
                            foreach (var city in actor.Realm.Cities.Where(city => city != null && !city.IsGone()))
                            {
                                if (empire.CoreKingdom.Capital != city)
                                {
                                    cities.Add(city);
                                }
                                targetMembers.Remove(city.Leader?.Key ?? -1L);
                            }
                        }
                        else
                        {
                            if (actor?.IsCityLeader() ?? false)
                            {
                                if (empire.CoreKingdom.Capital != actor.City)
                                {
                                    cities.Add(actor.City);
                                }
                                targetMembers.Remove(a);
                            }
                        }
                    }
                }
            }
        }
        cities = cities.Where(city => city != null && !city.IsGone() &&
            city.Realm?.GetEmpire() == empire &&
            RebellionSystem.CanAttempt(city.Realm) && CityStabilitySystem.CanRise(city)).Distinct().ToList();
        var totalWarriors = cities.Sum(c => c.Warriors());
        if (totalWarriors > 0 && (totalWarriors >= empire.countWarriors() - totalWarriors ||
            empire.CoreKingdom.GetMoney() < 0))
        {
            var leader = targetFaction?.GetLeader()??kingdom?.Ruler;
            var royalMembers = targetFaction?.Members?
                .Select(id => EmpireWorld.Current.PersonByKey(id))
                .Where(actor => actor != null && !actor.IsGone() && SpecificClanManager.SameLineage(actor.GetSpecificClan(), empire.EmpireSpecificClan))
                .ToList();
            if (royalMembers?.Any()??false)
            {
                leader = royalMembers?.OrderByDescending(a => a.Age()).First();
            }

            if (leader != null)
            {
                var cause = RebellionSystem.Capture(kingdom, kingdom.Capital, "rebellion_reason_faction",
                    targetFaction?.Name ?? kingdom.GetOffice()?.GetName() ?? kingdom.GetKingdomName());
                var war = EmpireWorld.Current.StartWar(kingdom, empire.CoreKingdom,
                    WarKinds.Normal);
                if (war == null)
                {
                    return false;
                }
                kingdom.StartFactionRebelling(targetFaction);
                foreach (var c in cities)
                {
                    if (c == kingdom.Capital) continue;
                    c.JoinRealm(kingdom);
                }
                war.SetEmpireWarType(EmpireWarType.派系叛乱);
                RebellionSystem.Record(kingdom, war, cause);
                if (targetFaction != null)
                {
                    war.Name = targetFaction.Name + "\u200A" + "叛乱";
                }
                else
                {
                    war.Name = (kingdom.GetOffice()?.GetName() ?? kingdom.GetKingdomName()) + "\u200A" + "叛乱";
                }
                return true;
            }
        }
        return false;
    }
#endif

    protected FixedFaction GetFaction()
    {
        if (Empire != null)
        {
            Realm kingdom = Empire.CoreKingdom;
            Regime regime = kingdom.GetRegime();
            return regime?.GetPlayerFactions()?.Find(f => f.GetID() == factionID);
        }

        return null;
    }

    public Empire GetEmpire()
    {
        return ModClass.EMPIRE_MANAGER?.Get(EmpireID);
    }
#if EC_PENDING
    public void Start()
    {
        if (!Active || started || Empire == null ||
            (Empire.RunningTemporaryFaction?.IsStarted() == true && Empire.RunningTemporaryFaction != this)) return;
        if (CountDown <= 0)
        {
            bool shouldLogPreparing = !started;
            if (!started)
            {
                progressMax = ClaimTuning.Duration(type);
                // 经费不够不施行
                if (!TryPayBudget()) return;
                ExecutedThisRun = false;
            }
            started = true;
            CompletionOutcome = null;
            timestamp = GameClock.Now;
            if (countDownTimestamp < 0)
            {
                countDownTimestamp = timestamp;
            }
            Empire empire = Empire;
            if (empire != null)
            {
                empire.RunningTemporaryFaction = this;
                if (shouldLogPreparing && HasRequiredCrimeForCurrentTarget())
                {
                    TranslateHelper.LogTemporaryFactionPreparing(empire, type.ToString(),
                        TranslateHelper.GetTemporaryFactionTargetText(TargetType, TargetID),
                        GetCurrentTargetCrimeName());
                }
            }
            if (ShowAsPlot)
            {
                var plot = AssetManager.plots_library.get("empire_plots");
                var emperor = empire?.Emperor;
                if (emperor != null)
                {
                    if (!emperor.plot?.isSameType(plot) ?? true)
                    {
                        if (plot?.try_to_start_advanced(emperor, plot, true) != true) End();
                    } 
                }
                else End();
            }
        }
    }
#endif

    public bool IsStarted()
    {
        return started;
    }

    public void End()
    {
        if (started)
        {
            LastRunCompleted = ExecutedThisRun;
            // 一个月都没推进就作罢的(谋划没起头、目标当场消失)，经费退回
            if (BudgetPaid && !ExecutedThisRun && progress <= 0f && PaidBudget > 0)
                Payer()?.AddMoney(PaidBudget, TreasuryCategory.Policy);
            ExecutedThisRun = false;
        }
        BudgetPaid = false;
        PaidBudget = 0;
        TargetID = -1L;
        TargetType = EntityKind.None;
        ClearDesignated();
        kingdoms ??= new List<long>();
        kingdoms.Clear();
        started = false;
        progress = 0;
        Acc = 0;
        pusherType = EntityKind.None;
        KingdomID = -1L;
        if (Empire != null)
        {
            if ((!Empire.RunningTemporaryFaction?.IsStarted()) ?? true)
            {
                Empire.RunningTemporaryFaction = null;  
            }
        }
    }

    // 旧写法：施行完再付钱。开始时已付过就不再扣
    public void FinishedAction()
    {
        TryPayBudget();
    }

    private Realm Payer() => IsLocallyPushed ? GetKingdom() : GetEmpire()?.CoreKingdom;

    public bool CanAffordBudget()
    {
        if (BudgetPaid) return true;
        int cost = Budget;
        if (cost <= 0) return true;
        Realm payer = Payer();
        return payer != null && !payer.IsGone() && payer.GetMoney() >= cost;
    }

    public bool TryPayBudget()
    {
        if (BudgetPaid) return true;
        if (!CanAffordBudget()) return false;
        int cost = Budget;
        if (cost > 0) Payer().SubMoney(cost, TreasuryCategory.Policy);
        BudgetPaid = true;
        PaidBudget = Math.Max(0, cost);
        return true;
    }

    // 针对具体目标的诉求(如册立储君)：各派因目标而起的额外立场，加在议题立场上
    public virtual float TargetStance(FixedFaction faction) => 0f;

    public void JoinKingdom(Realm kingdom)
    {
        kingdoms.Add(kingdom.Key);
    }
    //更新：每年一次共计十年
#if EC_PENDING
    private void Update()
    {
        if (!CheckContinue() || (IsLocallyPushed && !CheckLocalContinue(GetKingdom())))
        {
            End();
            return;
        }
        if (started&&Active)
        {
            if (!CheckTarget())
            {
                End();
                return;
            }
            if (!IsLocallyPushed && !CanProceedOutsideGovernment && ParliamentSystem.HasParliament(GetEmpire()))
            {
                // 责任政府：只有执政一方(联合政府时为整个执政联盟)能继续推动诉求
                if (ParliamentSystem.HasResponsibleGovernment(GetEmpire()) &&
                    !ParliamentSystem.IsGoverningFaction(GetEmpire(), factionID))
                {
                    End();
                    return;
                }
            }
            // 朝堂博弈下奏议已经由皇帝准奏，不再要求首辅同派
            else if (!IsLocallyPushed && !CanProceedOutsideGovernment && GetEmpire().CoreKingdom.GetRegime().has_cabinet &&
                     !CourtSystem.Applies(GetEmpire()))
            {
                if (GetEmpire().CoreKingdom.GetRegime().type != RegimeType.Feudalism)
                {
                    if (GetEmpire().GetCabinetLeader()?.GetFaction()?.GetID() != factionID)
                    {
                        End();
                        return;
                    } 
                }
            }
            if (!ShowAsPlot)
            {
                float progressRate = MonthlyRate(null, out bool blocked);
                if (blocked)
                {
                    End();
                    return;
                }
                progress += progressRate;
                if (progress >= progressMax) RunExecute();
            }
        }
        else
        {
            End();
        }
    }
#endif

    // 每月进度 = 基础(审核加速) × 议程(政党、体制外的诉求) × 朝堂施行(议事会支持、掣肘)。议程判定已不能推进时 blocked。
    // factors 不为空时记下各项倍数，诉求天平页逐项列出(用户 2026-11-10 "推动诉求也要加天平页面")
#if EC_PENDING
    public float MonthlyRate(List<CourtScaleFactor> factors, out bool blocked)
    {
        blocked = false;
        float progressRate = 1 + ((acceleration < 0 ? 0 : acceleration) / 5);
        factors?.Add(new CourtScaleFactor { key = "claim_rate_base", value = progressRate, arg = (acceleration < 0 ? 0 : acceleration).ToString("0") });
        FixedFaction faction = GetFaction();
        if (faction?.IsParty == true || CanProceedOutsideGovernment)
        {
            ClaimAgendaView agenda = ClaimAgendaSystem.Evaluate(
                ClaimAgendaSystem.BuildContext(GetEmpire()), faction, this);
            if (!agenda.CanPropose)
            {
                blocked = true;
                return 0f;
            }
            progressRate *= agenda.ProgressMultiplier;
            factors?.Add(new CourtScaleFactor { key = "claim_rate_agenda", value = agenda.ProgressMultiplier });
        }
        if (!IsLocallyPushed) progressRate *= CourtSystem.ProgressMultiplier(GetEmpire(), this, factors);
        return progressRate;
    }
#endif

    public bool CheckTarget()
    {
        bool exists = TargetType switch
        {
            EntityKind.None => !RequireCrimeTarget,
            EntityKind.Person => GetActorTarget() != null && !GetActorTarget().IsGone(),
            EntityKind.Realm => GetKingdomTarget() != null && !GetKingdomTarget().IsGone(),
            EntityKind.City => GetCityTarget() != null && !GetCityTarget().IsGone(),
            EntityKind.Religion => GetReligionTarget() != null && !GetReligionTarget().IsGone(),
            _ => TargetType == MetaTypeExtension.KingdomTitle && GetTitleTarget() != null && !GetTitleTarget().IsGone()
        };
        return exists && HasRequiredCrimeForCurrentTarget();
    }
#if EC_PENDING
    public void CheckNeedToUpdate()
    {
        if (GameClock.MonthsSince(timestamp) < 1) return;
        Update();
        timestamp = GameClock.Now;
    }
#endif
    /// <summary>
    /// 触发条件成功后的执行动作
    /// </summary>
    public abstract void Execute();

    /// <summary>
    /// 检测本体条件是否满足
    /// </summary>
    /// <param name="actor">发起主体</param>
    /// <returns></returns>
#if EC_PENDING
    public virtual bool CheckLocalCondition(Realm actor)
    {
        return CheckLocalContinue(actor) && CheckCondition();
    }

    public virtual bool CheckLocalContinue(Realm actor)
    {
        var empire = GetEmpire();
        if (empire == null || empire.IsGone() || !Active || !canBePushByLocal) return false;
        // 与文化制度树、科技时代挂钩的前提(见 ClaimRules)：地方推动同样受限
        if (!EmpireCraft.Core.ClaimRules.IsAllowed(empire, type, out _)) return false;
        if (actor == null || actor.IsGone() || actor.Ruler == null || actor.Ruler.IsGone() ||
            actor == empire.CoreKingdom || actor.GetEmpire() != empire || actor.IsAtWarWith(empire.CoreKingdom)) return false;
        var actorFaction = actor.Ruler.GetFaction();
        if (actorFaction == null || actorFaction.Ban) return false;
        if (actorFaction != GetFaction()) return false;
        return true;
    }
#endif

    public virtual void LocalEnd(Realm actor)
    {
        actor.EndProgress();
    }
    
    /// <summary>
    /// 检查条件是否满足
    /// </summary>
    /// <returns>返回条件是否满足的结果</returns>
    public abstract bool CheckCondition();

    /// <summary>
    /// 检查条件是否满足
    /// </summary>
    /// <returns>返回条件是否满足的结果</returns>
    public virtual bool CheckContinue()
    {
        var empire = GetEmpire();
        if (empire == null || empire.IsGone() || !Active) return false;
        var emperor = empire.Emperor;
        if (ShowAsPlot && (emperor == null || emperor.IsGone()))
        {
            return false;
        }
        return true;
    }

    public List<Realm> GetMembers()
    {
        return kingdoms.Select(k=>EmpireWorld.Current.RealmByKey(k)).ToList();
    }
}
}
