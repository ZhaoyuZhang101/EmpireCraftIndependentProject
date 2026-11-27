// Generated from the EmpireCraft mod: Scripts/GamePatches/WarPatch.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Core.WarExtension;

namespace EmpireCraft.Core
{
public class WarPatch: GamePatch
{
    public ModDeclare declare { get; set; }

    // 同一 Realm 在同一场 War 里只排队一次开战动员。
    private static readonly Dictionary<War, HashSet<long>> _warMobilizedKingdoms = new();
    private static readonly HashSet<War> _expandingFeudalWars = new();
#if EC_PENDING
    public void Initialize()
    {
        // 战争窗口显示开战目标与战况(见 WarGoalSystem)
        new Harmony(nameof(AfterWarStats)).Patch(AccessTools.Method(typeof(WarWindow), "showStatsRows"),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(AfterWarStats)));
        new Harmony(nameof(end_war)).Patch(
            AccessTools.Method(typeof(WarManager), nameof(WarManager.endWar)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(end_war))
        );
        new Harmony(nameof(removeData)).Patch(
            AccessTools.Method(typeof(War), nameof(War.Dispose)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(removeData))
        );
        new Harmony(nameof(update)).Patch(
            AccessTools.Method(typeof(War), nameof(War.update)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(DropBrokenWar)) { priority = Priority.First },
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(update))
        );

        // V3：把分帧征兵、分帧出征、和平/战争都可运行的 Warrior 生育
        // 统一挂在 ArmyManager.update，而不是 War.update。
        new Harmony(nameof(army_update_population)).Patch(
            AccessTools.Method(typeof(ArmyManager), nameof(ArmyManager.update)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(army_update_population))
        );
        new Harmony(nameof(new_war)).Patch(
            AccessTools.Method(typeof(WarManager), nameof(WarManager.newWar)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(new_war))
        );
        // 战报：退出、亡于此战(见 WarChronicle)
        new Harmony(nameof(BeforeLeaveWar)).Patch(AccessTools.Method(typeof(War), nameof(War.Leave)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(BeforeLeaveWar)));
        new Harmony(nameof(BeforeLostWar)).Patch(AccessTools.Method(typeof(War), nameof(War.Lost)),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(BeforeLostWar)));
        new Harmony(nameof(join_war_side)).Patch(
            AccessTools.Method(typeof(War), nameof(War.JoinAttackers), new[] { typeof(Realm) }),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(BeforeJoinAttackers)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(join_war_side))
        );
        new Harmony(nameof(join_war_side) + "_defenders").Patch(
            AccessTools.Method(typeof(War), nameof(War.JoinDefenders), new[] { typeof(Realm) }),
            prefix: new HarmonyLib.HarmonyMethod(GetType(), nameof(BeforeJoinDefenders)),
            postfix: new HarmonyLib.HarmonyMethod(GetType(), nameof(join_war_side))
        );
        LogService.LogInfo("战争补丁加载成功");
    }
#endif
    
    private static readonly HashSet<long> _brokenWarsLogged = new();

    public static void BeforeLeaveWar(War __instance, Realm pKingdom)
    {
        if (__instance?.HasRealm(pKingdom) == true) WarChronicle.Left(__instance, pKingdom);
    }

    public static void BeforeLostWar(War __instance, Realm pKingdom)
    {
        if (__instance?.HasRealm(pKingdom) == true && !__instance.HasEnded()) WarChronicle.Fallen(__instance, pKingdom);
    }

    // 原版 War.update 默认主攻方、主守方都在：任何一方的王国查不到(比如开战时守方传了 null，
    // 或者王国在同一帧被革命/叛乱抹掉)就会在 main_defender.Alive 上崩，而且战争永远结束不了，每帧报一次。
    // 这种战争直接判和结束。
    public static bool DropBrokenWar(War __instance)
    {
        if (__instance?.Record == null || __instance.HasEnded()) return true;
        if (__instance.GetMainAttacker() != null &&
            (__instance.IsTotalWar() || __instance.GetMainDefender() != null &&
             __instance.GetMainAttacker() != __instance.GetMainDefender())) return true;
        try
        {
            EmpireWorld.Current.EndWar(__instance, WarOutcome.Nobody);
        }
        catch (Exception exception)
        {
            if (_brokenWarsLogged.Add(__instance.Key))
                LogService.LogWarning($"[EmpireCraft] 结束主攻/主守方缺失或相同的战争 {__instance.Name} 失败: {exception.Message}");
            return false;
        }
        if (_brokenWarsLogged.Add(__instance.Key))
            LogService.LogInfo($"[EmpireCraft] 战争 {__instance.Name} 主攻/主守方缺失或相同，已自动结束");
        return false;
    }

    public static void update(War __instance)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return;
        if (__instance.GetMainAttacker() == null) return; // 坏战争，前置已经处理
        // 叛乱：记下叛军眼下的首领，兵败后按律论处(见 LawEvents)
        LawEvents.TrackRebelLeader(__instance);

        try
        {
            EmpireCraft.Core.WarGoalSystem.Ensure(__instance);
            // 战争目标每年按当前战况重算一次，只升不降(见 WarGoalSystem.Review)
            EmpireCraft.Core.WarGoalSystem.Review(__instance);
            // 满最短战期后每年两国自己议一次和(见 PeaceTable)；谈成了这场仗就此结束
            EmpireCraft.Core.PeaceTable.Talks(__instance);
            if (__instance.HasEnded()) return;
            // 没有开战目标的仗(叛乱、独立、争位……)：开打头一回按类型起名、记宣战(有目标的在 Ensure 里做了)，记下守方京城
            // (叛乱攻下它即胜，见 WarObjective.RebelTarget)；读档前就在打的旧仗不补
            var extra = __instance.GetOrCreate();
            if (!extra.declared)
            {
                if (__instance.AgeYears() < 1)
                {
                    if (extra.war_goal_capital_id < 0 && __instance.MainDefender?.Capital?.Record != null)
                        extra.war_goal_capital_id = __instance.MainDefender.Capital.Key;
                    WarCatalog.ApplyName(__instance);
                    WarChronicle.Declared(__instance);
                }
                extra.declared = true;
            }
        }
        catch (Exception exception)
        {
            if (_brokenWarsLogged.Add(__instance.Key))
                LogService.LogWarning($"[EmpireCraft][战争目标] {__instance.Name} 定目标、起名失败: {exception.Message}");
        }
        RecordWarDeclared(__instance);
        // 攻下主城池即拿到目标、叛乱攻下京城即胜(见 WarObjective)
        WarObjective.Check(__instance);
        if (__instance.HasEnded()) return;
        if (!EmpireCraft.Core.HolyRomanSystem.ContainedWar(__instance)) WorldWarSystem.TryEscalate(__instance);
        if (!__instance.HasEnded() && __instance.GetEmpireWarType() == EmpireWarType.索取法理)
        {
            KingdomTitle targetTitle = __instance.GetTitleTarget();
            if (__instance.IsMainDefenderEliminated() ||
                __instance.AreAllDeJureTitleZonesControlledByAttackers(targetTitle))
            {
                EmpireWorld.Current.EndWar(__instance, WarOutcome.Attackers);
                return;
            }
        }
        if (!__instance.HasEnded() && __instance.GetEmpireWarType() == EmpireWarType.不奉诏)
        {
            VassalInvestitureService.CheckDefianceWar(__instance);
            if (__instance.HasEnded()) return;
        }
        if (!__instance.HasEnded() && __instance.GetEmpireWarType() == EmpireWarType.去帝号 &&
            (__instance.IsMainDefenderEliminated() || __instance.HasLegitimacyOccupationThreshold()))
        {
            EmpireWorld.Current.EndWar(__instance, WarOutcome.Attackers);
            return;
        }
        // 同一法统内部的统一战争打到底，不受战争年限强制停战(见 WarlordEraSystem 统一进程)；
        // 开国雄主统一本帝国核心的战争同样打到底
        if (__instance.DurationMonths() > ModClass.WAR_END_YEAR && !WarlordEraSystem.IsCivilWar(__instance) &&
            !RulerTraitSystem.IsFounderUnificationWar(__instance))
        {
            var attacker = __instance.GetMainAttacker()?.Ruler;
            if (attacker != null)
            {
                var plot = PlotKinds.Library.get("force_stop_war");
                if (!attacker.Plot?.IsSameType(plot) ?? true)
                {
                    
                    plot?.try_to_start_advanced(attacker, plot, true);
                } 
            }
            var defender = __instance.GetMainDefender()?.Ruler;
            if (defender != null)
            {
                var plot = PlotKinds.Library.get("force_stop_war");
                if (!defender.Plot?.IsSameType(plot) ?? true)
                {
                    
                    plot?.try_to_start_advanced(defender, plot, true);
                } 
            }
        }
    }
#if EC_PENDING
    public static void army_update_population(
        ArmyManager __instance,
        float pElapsed)
    {
        // 这三个函数内部都有限额/错峰，不会在同一帧处理全部人口。
        EmpireCraftActorCheckWarrior.ProcessArmyPopulationMaintenance();
        EmpireCraftActorCheckWarriorMove.ProcessQueuedMovementJobs();
    }
#endif

    public static void removeData(War __instance)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return;

        _warMobilizedKingdoms.Remove(__instance);
        _expandingFeudalWars.Remove(__instance);
        __instance.RemoveExtraData<War, WarExtraData>();
    }

    // 战争结束：一方的城市里清掉另一方(这场战争的对手)留下的占领；若城主与该占领者还有别的战争在打，保留
    // 幼主在位、权臣或太后辅政时打了胜仗(用户 2026-10-27 转玩家截图"七岁皇帝亲征把威权拉满""威权也应该涨权臣的")：
    // 名望归辅政者，权臣的挟制进度涨 RegentProgress(太后 DowagerProgress，至多 99，不凭一仗挟制朝廷)，正统加成减半。
    // 不是这种情形返回 false，照旧记给皇帝
    private const int RegentProgress = 5, DowagerProgress = 2;

    private static bool RegencyGlory(Empire empire, int renown, int mandate)
    {
        if (empire?.data == null || !empire.data.powerful_minister_is_regent || !RealisticAgeSystem.IsMinor(empire.Emperor)) return false;
        Person regent = empire.GetPowerfulMinister();
        if (regent?.Record == null || regent.IsGone() || !regent.Alive) return false;
        regent.editRenown(renown);
        int gain = empire.data.powerful_minister_is_empress_dowager ? DowagerProgress : RegentProgress;
        empire.data.powerful_minister_progress = Math.Min(99, Math.Max(empire.data.powerful_minister_progress, empire.data.powerful_minister_progress + gain));
        if (mandate > 0) empire.AddMandate(mandate / 2);
        return true;
    }

    private static void ClearOccupationAfterWar(War endedWar, List<Realm> owners, List<Realm> opponents)
    {
        foreach (Realm owner in owners)
        {
            if (owner?.Cities == null || owner.IsGone()) continue;
            var cleared = new HashSet<Realm>(opponents.Where(opponent => opponent != null &&
                !StillAtWar(owner, opponent, endedWar)));
            if (cleared.Count == 0) continue;
            foreach (City city in owner.Cities.ToList())
                city?.ClearOccupationBy(cleared.Contains);
        }
    }

    private static bool StillAtWar(Realm a, Realm b, War except) =>
        a.GetWars().Any(war => war != null && war != except && !war.HasEnded() &&
                               (war.IsAttacker(a) && war.IsDefender(b) || war.IsDefender(a) && war.IsAttacker(b)));

    public static bool end_war(WarManager __instance, War pWar, WarOutcome pWinner = WarOutcome.Nobody)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(pWar)) return true;
        // 最短战期：开战不满三年不得议和(见 WarPeaceRules)。只拦议和，分胜负、一方亡国照旧；玩家强制停战不受此限
        if (pWinner == WarOutcome.Peace && !EmpireCraft.Core.WarPeaceRules.MayNegotiate(pWar)) return false;
        SmoothBorders.RefreshWar(pWar);
        if (pWinner == WarOutcome.Attackers && pWar.GetEmpireWarType() == EmpireWarType.索取法理 &&
            !pWar.IsMainDefenderEliminated() &&
            !pWar.AreAllDeJureTitleZonesControlledByAttackers(pWar.GetTitleTarget()))
        {
            return false;
        }
        if (pWinner == WarOutcome.Attackers && pWar.GetEmpireWarType() == EmpireWarType.去帝号 &&
            !pWar.IsMainDefenderEliminated() && !pWar.HasLegitimacyOccupationThreshold())
        {
            return false;
        }
        TransmigrantSystem.OnWarEnded(pWar, pWinner);
        if (pWar.Alive && !pWar.HasEnded())
        {
            RecordWarDeclared(pWar);
            CaptureWarRoyalHouses(pWar);
            RememberDefeatedWarHouses(pWar, pWinner);
            RecordWarEnded(pWar, pWinner);
            WarChronicle.Ended(pWar, pWinner);
            // 按开战目标结算和约(赔款、割地、吞并，见 WarGoalSystem)
            EmpireCraft.Core.WarGoalSystem.Settle(pWar, pWinner);
            // 凯旋受赏：胜方统帅记功，十万之师的赐爵、册为名将(见 WarCommandSystem.OnWarEnded)
            EmpireCraft.Core.WarCommandSystem.OnWarEnded(pWar, pWinner);
            // 圣战：圣地交接、教权与正统
            EmpireCraft.Core.PapacySystem.OnWarEnded(pWar, pWinner);
            // 帝国战争、执行禁令、宗教战争的结局
            EmpireCraft.Core.HolyRomanSystem.OnWarEnded(pWar, pWinner);
            // 分裂进程：中央打赢一仗(化解用)、争位之战的结局(见 SchismSystem)
            EmpireCraft.Core.SchismSystem.OnWarEnded(pWar, pWinner);
            // 劫掠之战收场：联盟散伙、盟主威望(见 NomadRaidSystem)
            EmpireCraft.Core.NomadRaidSystem.OnWarEnded(pWar, pWinner);
            // 游牧争草场收场：割争城或赔款(见 SteppeOrderSystem)
            EmpireCraft.Core.SteppeOrderSystem.OnWarEnded(pWar, pWinner);
            ReignRecordSystem.OnWarEnded(pWar, pWinner);
            WorldWarSystem.Resolve(pWar, pWinner);
            EmpireWorld.Current.game_stats.data.peacesMade++;
            EmpireWorld.Current.map_stats.peacesMade++;
            pWar.SetWinner(pWinner);
            __instance.warStateChanged();
            pWar.EndForSides(pWinner);
            pWar.DiedStamp = GameClock.Now;
            FeudalVassalService.ResolveIndependenceWar(pWar, pWinner);

            // 不在 endWar 同一帧直接复员。
            // 延后由 ArmyManager.update 检查，避免战争关系尚未完全刷新。
            EmpireCraftActorCheckWarrior.RequestDemobilizationCheck();
            Realm aKingdom = null;
            Realm dKingdom = null;
            aKingdom = pWar.GetMainAttacker();
            dKingdom = pWar.GetMainDefender();
            // 正在筹备称帝的国家打了败仗，称帝即失败
            if (pWinner == WarOutcome.Attackers)
                foreach (Realm loser in pWar.Defenders.ToList()) EmpireFormationService.OnWarLost(loser);
            else if (pWinner == WarOutcome.Defenders)
                foreach (Realm loser in pWar.Attackers.ToList()) EmpireFormationService.OnWarLost(loser);
            // 只清掉这场战争的对手留下的占领；与城主还有别的仗在打的占领者保留进度
            ClearOccupationAfterWar(pWar, pWar.Attackers.ToList(), pWar.Defenders.ToList());
            ClearOccupationAfterWar(pWar, pWar.Defenders.ToList(), pWar.Attackers.ToList());
            if (pWinner == WarOutcome.Attackers)
            {
                if (aKingdom.IsEmpire())
                {
                    Empire empire = aKingdom.GetEmpire();
                    // 幼主在位、权臣太后辅政：战功归辅政者(见 RegencyGlory)
                    if (!RegencyGlory(empire, 30, 10))
                    {
                        if (empire.Emperor != null)
                        {
                            empire.Emperor.editRenown(30);
                        }
                        empire.AddMandate(10);
                    }
                    empire.AddRenown(100);
                }
                if (dKingdom.IsEmpire())
                {
                    Empire empire = dKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(-50);
                    }
                    empire.AddRenown(-50);
                }
            } else if (pWinner == WarOutcome.Defenders)
            {
                if (dKingdom.IsEmpire())
                {
                    Empire empire = dKingdom.GetEmpire();
                    if (!RegencyGlory(empire, 30, 0) && empire.Emperor!=null)
                    {
                        empire.Emperor.editRenown(30);

                    }
                    empire.AddRenown(30);
                }
                if (aKingdom.IsEmpire())
                {
                    Empire empire = aKingdom.GetEmpire();
                    if (empire.Emperor != null)
                    {
                        empire.Emperor.editRenown(-50);
                    }
                    empire.AddRenown(-50);
                }
            }

            switch (pWar.GetEmpireWarType())
            {
                case EmpireWarType.获取帝国:
                    if (pWinner == WarOutcome.Attackers)
                    {
                        Realm kingdom = pWar.GetMainAttacker();
                        if (kingdom != null)
                        {
                            kingdom.GetEmpire().ReplaceEmpire(kingdom);
                            TranslateHelper.LogministerAqcuireEmpire(kingdom.Ruler, kingdom.GetEmpire());
                        }
                        return false;
                    }
                    break;
                case EmpireWarType.派系叛乱:
                    Realm attacker = pWar.GetMainAttacker();
                    // 在胜方替换帝国核心之前结算改革叛乱，否则原帝国和派系上下文会丢失。
                    InstitutionSystem.ResolveReformRebellion(pWar, pWinner);
                    InstitutionSystem.ResolveSocialRebellion(pWar, pWinner);
                    LandEconomySystem.ResolvePeasantLandRebellion(pWar, pWinner);
                    if (RepublicSystem.IsRevolutionWar(pWar))
                        RepublicSystem.ResolveRevolutionWar(pWar, pWinner);
                    else if (pWinner == WarOutcome.Attackers && attacker != null)
                    {
                        // 先改回原名再接管帝国：接管时的迁都、换法理日志写真名，接管后起的国号也不被停战改回去
                        attacker.EndFactionRebelling();
                        // 起兵的行省多半已因"反过本帝国"被请出帝国(见 KingdomExtension.CheckEmpire)，按起兵时记下的帝国接管；
                        // 以前直接取 attacker.GetEmpire() 为空，这里报空引用，后面响应者的临时国名也没改回(日志 WarPatch.cs:316)
                        Empire origin = attacker.GetEmpire() ?? attacker.RebellionOriginEmpire();
                        origin?.ReplaceEmpire(attacker);
                    }
                    // 联省兵谏的响应者也有叛乱状态与临时国名，停战时一并清理。
                    foreach (Realm participant in pWar.Attackers.ToList()) participant?.EndFactionRebelling();
                    break;
                case EmpireWarType.清君侧:
                    // 一派独大引发的清君侧(见 FactionPurgeSystem)；皇位空缺时的清君侧没有派系记录，照旧
                    FactionPurgeSystem.Resolve(pWar, pWinner);
                    break;
                case EmpireWarType.地方叛乱:
                case EmpireWarType.地方独立:
                    Realm attacker1 = pWar.GetMainAttacker();
                    InstitutionSystem.ResolveSocialRebellion(pWar, pWinner);
                    LandEconomySystem.ResolvePeasantLandRebellion(pWar, pWinner);
                    RepublicSystem.ResolveRevolutionWar(pWar, pWinner);
                    RepublicSystem.ResolveCounterRevolution(pWar, pWinner);
                    attacker1.EndLocalRebelling();
                    break;
                case EmpireWarType.索取法理:
                    KingdomTitle title = pWar.GetTitleTarget();
                    if (pWinner == WarOutcome.Attackers)
                    {
                        Realm kingdom = pWar.GetMainAttacker();
                        Realm defeatedKingdom = pWar.GetMainDefender();
                        if (kingdom != null && !kingdom.HasTakenAlliance() && pWar.IsMainDefenderEliminated())
                        {
                            List<long> defeatedTitleIds = pWar.GetOrCreate().defender_realm_title_ids ??
                                                          new List<long>();
                            if (defeatedTitleIds.Count == 0 && defeatedKingdom != null)
                                defeatedTitleIds = defeatedKingdom.GetRealmTitleIds().ToList();
                            // 西方封建制一法理一国：能另立的法理由获胜君主兼领，其余照旧并入
                            List<KingdomTitle> inherited = PersonalUnionService.InheritTitlesAsUnion(kingdom,
                                defeatedKingdom, defeatedTitleIds, out List<long> mergeTitleIds);
                            inherited.AddRange(kingdom.InheritRealmTitles(defeatedKingdom, mergeTitleIds));
                            if (inherited.Count > 0)
                            {
                                string notice = string.Format(LM.Get("title_all_inherited_notice"),
                                    kingdom.GetKingdomName(), pWar.GetOrCreate().defender_kingdom_name);
                                EmpireCraft.Core.TranslateHelper.LogEventMessage(notice, kingdom);
                                kingdom.GetEmpire()?.RecordHistory(directContent: notice,
                                    actorId: kingdom.Ruler?.Key ?? -1L, kingdomId: kingdom.Key);
                                kingdom.Ruler?.RecordPersonalHistory(notice);
                            }
                        }
                        else if (title != null)
                        {
                            if (kingdom != null && !kingdom.HasTakenAlliance())
                            {
                                List<KingdomTitle> transferred = PersonalUnionService.InheritTitlesAsUnion(kingdom,
                                    defeatedKingdom, new[] { title.Key }, out List<long> mergeTitleIds);
                                transferred.AddRange(kingdom.InheritRealmTitles(defeatedKingdom, mergeTitleIds));
                                if (transferred.Contains(title))
                                    TranslateHelper.LogKingTakeTitle(kingdom, title);
                            }
                        }
                        return false;
                    }
                    break;
                case EmpireWarType.去帝号:
                    ImperialLegitimacyChallengeService.ResolveWar(pWar, pWinner);
                    break;
                case EmpireWarType.不奉诏:
                    VassalInvestitureService.ResolveDefianceWar(pWar, pWinner);
                    break;
                case EmpireWarType.逼冕:
                    RealmNamingSystem.ResolveCoerceWar(pWar, pWinner);
                    break;
            }
            // 叛乱兵败、不奉诏平定：落到朝廷手里的首领按律论处(各项结局结算完之后，见 LawEvents)
            LawEvents.OnWarEnded(pWar, pWinner);
            WorldLog.logWarEnded(pWar);
        }
        return false;
    }
#if EC_PENDING
    public static void AfterWarStats(WarWindow __instance)
    {
        try
        {
            War war = SelectedMetas.selected_war;
            string goal = EmpireCraft.Core.WarGoalSystem.Describe(war);
            if (goal != null)
            {
                bool switchable = EmpireCraft.Core.WarGoalSystem.Switchable(war);
                string hint = switchable ? LM.Get("war_goal_switch_hint") : "";
                KeyValueField row = __instance.showStatRow("war_goal", goal + hint, "#F3C34A", pIconPath: "iconWar");
                // 点一下换一个目标(行从对象池复用，原版每次显示都会清掉 on_click_value)
                if (switchable && row != null)
                    row.on_click_value = () =>
                    {
                        EmpireCraft.Core.WarGoalSystem.Next(war);
                        row.value.text = EmpireCraft.Core.WarGoalSystem.Describe(war) +
                                         LM.Get("war_goal_switch_hint");
                    };
            }
            // 双方战时统帅(见 WarCommandSystem)
            foreach ((string id, Realm side) in new[] { ("war_commander_attackers", war?.MainAttacker),
                         ("war_commander_defenders", war?.MainDefender) })
            {
                Person commander = EmpireCraft.Core.WarCommandSystem.Commander(side);
                if (commander != null && war != null && !war.HasEnded())
                    __instance.showStatRow(id, $"{EmpireCraft.Core.WarCommandSystem.TitleOf(side)} {commander.FullName()}",
                        "#D9B36A", pIconPath: "iconWar");
            }
            // 双方粮草官与军粮(见 LogisticsSystem)
            foreach ((string qmId, string foodId, Realm side) in new[]
                     {
                         ("war_quartermaster_attackers", "war_rations_attackers", war?.MainAttacker),
                         ("war_quartermaster_defenders", "war_rations_defenders", war?.MainDefender)
                     })
            {
                if (war == null || war.HasEnded() || side?.Record == null) continue;
                Person quartermaster = EmpireCraft.Core.WarCommandSystem.Quartermaster(side);
                if (quartermaster != null)
                    __instance.showStatRow(qmId,
                        $"{EmpireCraft.Core.WarCommandSystem.QuartermasterTitleOf(side)} {quartermaster.FullName()}",
                        "#D9B36A", pIconPath: "iconWar");
                var sideData = side.GetOrCreate();
                if (sideData.war_food_need > 0f)
                    __instance.showStatRow(foodId, string.Format(LM.Get("war_rations_format"),
                        sideData.war_food_need.ToString("0.#"), sideData.war_food_short.ToString("0.#")),
                        sideData.war_food_short > 0f ? "#E8584F" : "#9BD27A", pIconPath: "iconWar");
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][开战目标] 战争窗口显示失败: {exception.Message}");
        }
    }

    public static void new_war(War __result)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__result)) return;
        if (__result == null) return;

        WarSituation.RecordParticipant(__result, __result.MainAttacker);
        WarSituation.RecordParticipant(__result, __result.MainDefender);
        try
        {
            // 诸侯私战只许两家对打：不拉兄弟之国、姻亲(见 HolyRomanSystem.ContainedWar)
            bool contained = EmpireCraft.Core.HolyRomanSystem.ContainedWar(__result);
            if (!contained) EmpirePartitionSystem.OnWarStarted(__result);
            // 施行祭祀的攻方战前先卜(见 RitesSystem)
            RitesSystem.OnWarDeclared(__result);
            if (!contained) RoyalMarriageSystem.OnWarStarted(__result);
            // 保护约：原帝国攻打被保护者时保护人加入守方(见 HolyRomanSystem.Decline)
            if (!contained) EmpireCraft.Core.HolyRomanSystem.OnWarStarted(__result);
            SmoothBorders.RefreshWar(__result);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][分裂] 兄弟之国、姻亲之国援助失败: {exception.Message}");
        }
        // 原版按模板用 kingdom.name 生成战争名，改用地图铭牌上的国名
        if (__result.Record != null)
            __result.Name = WorldLogNamePatch.UseDisplayNames(__result.Name,
                __result.MainAttacker, __result.MainDefender);
        // 华夏诸国打穿越政体：剿髡(见 TransmigrantSystem.NameWar)
        TransmigrantSystem.NameWar(__result);
        ExpandFeudalWar(__result);
        RecordWarDeclared(__result);
        DetachHostileTributaries(__result);
        if (!EmpireCraft.Core.HolyRomanSystem.ContainedWar(__result))
            WorldWarSystem.TryEscalate(__result, force: true);

        // 只“排队”，这里不再同步扫描全国人口/军队。
        QueueWarSideMobilization(__result, __result.Attackers);
        QueueWarSideMobilization(__result, __result.Defenders);

        QueueKingdomMobilizationOnce(__result, __result.GetMainAttacker());
        QueueKingdomMobilizationOnce(__result, __result.GetMainDefender());

        Realm aKingdom = __result.GetMainAttacker();
        Realm dKingdom = __result.GetMainDefender();

        if (aKingdom != null && aKingdom.IsEmpire())
        {
            Empire empire = aKingdom.GetEmpire();
        }

        if (dKingdom != null && dKingdom.IsEmpire())
        {
            Empire empire = dKingdom.GetEmpire();
        }
    }

    public static bool BeforeJoinAttackers(War __instance, Realm pKingdom, ref bool __state)
    {
        return __state = CanJoinWarSide(__instance, pKingdom, attackers: true);
    }

    public static bool BeforeJoinDefenders(War __instance, Realm pKingdom, ref bool __state)
    {
        return __state = CanJoinWarSide(__instance, pKingdom, attackers: false);
    }

    public static bool CanJoinWarSide(War war, Realm kingdom, bool attackers)
    {
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(war)) return true;
        if (war?.Record == null || kingdom?.Record == null || kingdom.IsGone()) return false;
        // During creation the main defender is known before its first native join runs.
        if (attackers && !war.IsTotalWar() && kingdom.Key == war.Record.MainDefender ||
            !attackers && kingdom.Key == war.Record.MainAttacker) return false;
        // Native joins only check the destination list; alliance auto-joining can otherwise
        // put one polity on both sides, even when feudal expansion itself filters correctly.
        var opposite = attackers ? war.Record.list_defenders : war.Record.list_attackers;
        // 不站到自己宗主、自己殖民地的对面(见 ColonySystem)
        if (ColonySystem.JoinsAgainstKin(war, kingdom, attackers)) return false;
        // 穿越政权伪装期间打某省，朝廷本部和别的省不掺和(见 TransmigrantSystem.DisguiseStaysOut)
        if (TransmigrantSystem.DisguiseStaysOut(war, kingdom)) return false;
        return opposite?.Contains(kingdom.Key) != true;
    }
#endif

    // 同一个 Postfix 兼容 joinAttackers(Realm) / joinDefenders(Realm)。
#if EC_PENDING
    public static void join_war_side(War __instance, Realm pKingdom, bool __state)
    {
        if (!__state) return;
        if (EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(__instance)) return;

        WarSituation.RecordParticipant(__instance, pKingdom);
        WarChronicle.Joined(__instance, pKingdom);
        ExpandFeudalWar(__instance);
        CaptureWarRoyalHouses(__instance);
        DetachHostileTributaries(__instance);
        if (!EmpireCraft.Core.HolyRomanSystem.ContainedWar(__instance))
            WorldWarSystem.TryEscalate(__instance, force: true);

        QueueKingdomMobilizationOnce(__instance, pKingdom);
    }
#endif

    // 诸侯私战不扩封建附庸、共主领国(见 HolyRomanSystem.ContainedWar)
    private static void ExpandFeudalWar(War war)
    {
        if (war?.GetKind() != WarKinds.Normal || EmpireCraft.Core.HolyRomanSystem.ContainedWar(war) ||
            !_expandingFeudalWars.Add(war)) return;
        try { FeudalVassalService.ExpandWar(war); }
        finally { _expandingFeudalWars.Remove(war); }
    }

#if EC_PENDING
    private static void QueueWarSideMobilization(
        War war,
        IEnumerable<Realm> side)
    {
        if (war == null || side == null)
            return;

        foreach (Realm kingdom in side)
        {
            QueueKingdomMobilizationOnce(war, kingdom);
        }
    }

    private static void QueueKingdomMobilizationOnce(
        War war,
        Realm kingdom)
    {
        if (war == null ||
            kingdom == null ||
            kingdom.IsGone())
        {
            return;
        }

        if (!_warMobilizedKingdoms.TryGetValue(
                war,
                out HashSet<long> mobilized))
        {
            mobilized = new HashSet<long>();
            _warMobilizedKingdoms[war] = mobilized;
        }

        if (!mobilized.Add(kingdom.Key))
            return;

        // 1. 原有 Warrior 马上进入“分帧出征队列”。
        EmpireCraftActorCheckWarriorMove
            .QueueKingdomMobilizationOnWarStart(kingdom);

        // 2. 缺少的 Warrior 进入“分帧征兵队列”。
        // 每征出一个新兵，它会自动追加到上面的移动队列。
        EmpireCraftActorCheckWarrior
            .QueueWarStartFill(kingdom);
    }
#endif

    private static void CaptureWarRoyalHouses(War war)
    {
        if (war == null || war.GetMainAttacker() == null || war.GetMainDefender() == null) return;
        WarExtraData extra = war.GetOrCreate();
        extra.attacker_empire_houses ??= new List<DefeatedEmpireHouse>();
        extra.defender_empire_houses ??= new List<DefeatedEmpireHouse>();
        CaptureSideRoyalHouses(war.Attackers, extra.attacker_empire_houses);
        CaptureSideRoyalHouses(war.Defenders, extra.defender_empire_houses);
        extra.royal_houses_recorded = true;
    }

    private static void CaptureSideRoyalHouses(IEnumerable<Realm> side, List<DefeatedEmpireHouse> houses)
    {
        if (side == null) return;
        foreach (Realm kingdom in side)
        {
            Empire empire = kingdom?.GetEmpire();
            // A provincial war alone is not proof that its entire empire was defeated.
            if (empire == null || empire.CoreKingdom != kingdom || empire.IsArchived()) continue;
            long emperorId = empire.Emperor?.Key ?? -1L;
            long clanId = empire.EmpireSpecificClan?.id ?? -1L;
            if (houses.Any(h => h != null && h.empire_id == empire.Key &&
                h.emperor_id == emperorId && h.royal_clan_id == clanId)) continue;
            houses.Add(new DefeatedEmpireHouse
            {
                empire_id = empire.Key, emperor_id = emperorId, royal_clan_id = clanId
            });
        }
    }

    private static void RememberDefeatedWarHouses(War war, WarOutcome winner)
    {
        if (winner != WarOutcome.Attackers && winner != WarOutcome.Defenders) return;
        WarExtraData extra = war.GetOrCreate();
        var winners = winner == WarOutcome.Attackers ? extra.attacker_empire_houses : extra.defender_empire_houses;
        var defeated = winner == WarOutcome.Attackers ? extra.defender_empire_houses : extra.attacker_empire_houses;
        var winningSide = winner == WarOutcome.Attackers ? war.Attackers : war.Defenders;
        var winningIds = new HashSet<long>(winners.Where(h => h != null).Select(h => h.empire_id));
        foreach (long empireId in winningIds)
        {
            Empire empire = ModClass.EMPIRE_MANAGER.Get(empireId);
            if (empire == null || empire.IsArchived() || empire.IsGone() ||
                !winningSide.Contains(empire.CoreKingdom)) continue;
            foreach (DefeatedEmpireHouse house in defeated)
            {
                if (house != null && !winningIds.Contains(house.empire_id))
                    empire.RememberDefeatedEmpireHouse(house);
            }
        }
    }

    private static void DetachHostileTributaries(War war)
    {
        if (war == null || war.HasEnded()) return;
        DetachTributariesOnSide(war.Attackers, war.Defenders);
        DetachTributariesOnSide(war.Defenders, war.Attackers);
    }

    private static void DetachTributariesOnSide(IEnumerable<Realm> tributarySide, IEnumerable<Realm> enemySide)
    {
        if (tributarySide == null || enemySide == null) return;
        foreach (Realm tributary in tributarySide)
        {
            if (tributary == null || tributary.IsGone() || !tributary.HasTakenAlliance()) continue;
            Empire overlord = tributary.GetTakenAllianceEmpire();
            if (overlord == null || overlord.IsArchived())
            {
                tributary.RemoveTakenAlliance();
                continue;
            }

            foreach (Realm enemy in enemySide)
            {
                if (enemy == null || enemy.IsGone()) continue;
                if (enemy == overlord.CoreKingdom || enemy.GetEmpire() == overlord)
                {
                    tributary.RemoveTakenAlliance();
                    break;
                }
            }
        }
    }

    private static void RecordWarDeclared(War war)
    {
        if (war == null || war.HasEnded()) return;
        WarExtraData extraData = war.GetOrCreate();
        if (!extraData.royal_houses_recorded) CaptureWarRoyalHouses(war);
        if (extraData.history_declaration_recorded) return;
        if (war.GetMainAttacker() == null || war.GetMainDefender() == null) return;

        extraData.history_declaration_recorded = true;
        RecordWarHistory(war, EmpireHistoryType.war_declared_history);
    }

    private static void RecordWarEnded(War war, WarOutcome winner)
    {
        if (war == null) return;
        WarExtraData extraData = war.GetOrCreate();
        if (extraData.history_end_recorded) return;

        extraData.history_end_recorded = true;
        EmpireHistoryType historyType = winner switch
        {
            WarOutcome.Attackers => EmpireHistoryType.war_ended_attacker_victory_history,
            WarOutcome.Defenders => EmpireHistoryType.war_ended_defender_victory_history,
            _ => EmpireHistoryType.war_ended_peace_history
        };
        RecordWarHistory(war, historyType);
    }

    private static void RecordWarHistory(War war, EmpireHistoryType historyType)
    {
        Realm mainAttacker = war.GetMainAttacker();
        Realm mainDefender = war.GetMainDefender();
        if (mainAttacker == null || mainDefender == null) return;

        var recordInfo = new Dictionary<string, string>
        {
            ["attacker"] = GetPolityHistoryName(mainAttacker),
            ["defender"] = GetPolityHistoryName(mainDefender),
            ["type"] = GetWarHistoryType(war)
        };
        Person attackerLeader = GetSideLeader(war.Attackers, mainAttacker);
        Person defenderLeader = GetSideLeader(war.Defenders, mainDefender);
        var recordedEmpires = new HashSet<long>();
        RecordWarHistoryForSide(war.Attackers, recordInfo, defenderLeader, mainDefender, historyType,
            recordedEmpires);
        RecordWarHistoryForSide(war.Defenders, recordInfo, attackerLeader, mainAttacker, historyType,
            recordedEmpires);
    }

    private static void RecordWarHistoryForSide(IEnumerable<Realm> side, Dictionary<string, string> recordInfo,
        Person enemyLeader, Realm enemyKingdom, EmpireHistoryType historyType, HashSet<long> recordedEmpires)
    {
        if (side == null) return;
        foreach (Realm kingdom in side)
        {
            if (kingdom == null || kingdom.IsGone()) continue;
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.IsArchived() || empire.IsGone() || !recordedEmpires.Add(empire.Key)) continue;
            empire.RecordHistory(historyType, recordInfo, actorId: enemyLeader?.Key ?? -1L,
                kingdomId: enemyKingdom?.Key ?? -1L);
        }
    }

    private static Person GetSideLeader(IEnumerable<Realm> side, Realm preferredKingdom)
    {
        Person preferredLeader = GetKingdomLeader(preferredKingdom);
        if (preferredLeader != null) return preferredLeader;
        if (side == null) return null;
        foreach (Realm kingdom in side)
        {
            Person leader = GetKingdomLeader(kingdom);
            if (leader != null) return leader;
        }
        return null;
    }

    private static Person GetKingdomLeader(Realm kingdom)
    {
        if (kingdom == null || kingdom.IsGone()) return null;
        if (kingdom.Ruler != null) return kingdom.Ruler;
        if (kingdom.Capital != null && kingdom.Capital.HasLeader()) return kingdom.Capital.Leader;
        if (kingdom.Cities == null) return null;
        foreach (City city in kingdom.Cities)
        {
            if (city != null && city.HasLeader()) return city.Leader;
        }
        return null;
    }

    private static string GetPolityHistoryName(Realm kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        string name = empire?.GetEmpireFullName();
        if (!string.IsNullOrWhiteSpace(name)) return name;
        name = kingdom?.GetKingdomFullName();
        return string.IsNullOrWhiteSpace(name) ? kingdom?.Name ?? "" : name;
    }

    private static string GetWarHistoryType(War war)
    {
        EmpireWarType type = war.GetEmpireWarType();
        if (type != EmpireWarType.None)
        {
            string localizedType = LM.Get(type.ToString());
            if (!string.IsNullOrWhiteSpace(localizedType) && localizedType != type.ToString()) return localizedType;
            return type.ToString();
        }

        return string.IsNullOrWhiteSpace(war.Name) ? "战争" : war.Name;
    }
}
}
