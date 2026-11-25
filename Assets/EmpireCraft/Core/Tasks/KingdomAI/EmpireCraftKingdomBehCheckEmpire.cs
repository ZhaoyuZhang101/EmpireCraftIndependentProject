// Generated from the EmpireCraft mod: Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckEmpire.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Linq;

namespace EmpireCraft.Core
{

public class EmpireCraftKingdomBehCheckEmpire:GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult execute(Realm pKingdom)
    {
        pKingdom.CheckEmpire();
        CheckPossible(pKingdom);
        if (pKingdom.IsEmpire())
        {
            SyncData(pKingdom);
            CalcMilitaryExpenditure(pKingdom);
            CheckCorruption(pKingdom);
        }

        if (pKingdom.IsNeedToTaken())
        {
            pKingdom.StartToTaken();
        }
        
        CheckEmpireAlliance(pKingdom);
        return BehResult.Continue;
    }
#endif
    /// <summary>
    /// 帝国破产后十年直接解散
    /// </summary>
    /// <param name="pKingdom"></param>
    public static void CheckCorruption(Realm pKingdom)
    {
        if (pKingdom.IsEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (pKingdom.GetMoney() < 0)
            {
                if (!pKingdom.IsStartCorrupting())
                {
                    pKingdom.StartCorrupting();
                }
            }
            else
            {
                pKingdom.EndCorrupting();
            }

            // 亏空的正统影响由治理来源/年度破产结算负责，不能每次 AI 检查都再扣一次。
        }
    }
    /// <summary>
    /// 帝国与核心国家同步数据
    /// </summary>
    /// <param name="pKingdom"></param>
#if EC_PENDING
    public void SyncData(Realm pKingdom)
    {
        //同步天子
        Empire empire = pKingdom.GetEmpire();
        if (empire == null || empire.IsGone() || empire.IsArchived())  return;
        foreach (var kingdom in empire.kingdoms_list)
        {
            if (kingdom.IsEmpire()) continue;
            kingdom.SetEmpireID(empire.Key);
        }
        if (string.IsNullOrEmpty(empire.GetEmpireName()))
        {
            if (pKingdom.HasRuler()&&pKingdom.Ruler.hasCulture())
            {
                empire.data.directPre = "";
                var nameEmpire = pKingdom.Ruler.culture.getOnomasticData(MetaType.Kingdom).generateName();
                empire.SetEmpireName(nameEmpire);
                // 有宗族编号不等于宗族还在(旧档里身份上的宗族可能已经没了)
                pKingdom.Ruler.GetSpecificClan()?.RecordHistoryEmpire(empire, pKingdom.Capital);
            }
        }

        if (pKingdom.HasRuler())
        {
            if (pKingdom.Ruler.HasSpecificClan())
            {
                var specificClan = pKingdom.Ruler.GetSpecificClan();
                if (specificClan != null && !specificClan.HasHistoryEmpire())
                {
                    specificClan.RecordHistoryEmpire(empire, pKingdom.Capital);
                }
            }
        }
        if (!pKingdom.IsEmpire())
        {
            pKingdom.GetRegime().GetPlayerFactions().ForEach(f=>f.BanFaction());
        }
        else
        {
            pKingdom.GetRegime().GetPlayerFactions().ForEach(f =>
            {
                f.Ban = false;
                f.Update();
            });
        }
    }
#endif
    /// <summary>
    /// 每年计算一次军费，使用最近十年的财政记录
    /// </summary>
    /// <param name="pKingdom"></param>
#if EC_PENDING
    public void CalcMilitaryExpenditure(Realm pKingdom)
    {
        Empire empire = pKingdom.GetEmpire();
        if (empire == null || empire.IsGone() || empire.IsArchived()) return;
        var core = empire.CoreKingdom;
        if (core == null || EmpireWorld.Current == null) return;
        double now = GameClock.Now;
        if (empire.data.last_military_expenditure_timestamp >= 0d &&
            now >= empire.data.last_military_expenditure_timestamp &&
            GameClock.YearsSince(empire.data.last_military_expenditure_timestamp) < 1) return;
        empire.data.last_military_expenditure_timestamp = now;
        if (TreasurySystem.Enabled(core))
        {
            // 城市月度驻军/野战军账单已实际扣款，年度只汇总，不能再扣旧公式军费。
            var fiscal = TreasurySystem.Consolidated(core);
            fiscal.expense_sources.TryGetValue(nameof(TreasuryCategory.Military), out long fieldArmy);
            fiscal.expense_sources.TryGetValue(nameof(TreasuryCategory.Garrison), out long garrisons);
            fiscal.expense_sources.TryGetValue(nameof(TreasuryCategory.War), out long war);
            empire.data.MilitaryExpenditure = (int)Math.Min(int.MaxValue, fieldArmy + garrisons + war);
            if (empire.IsNeedToGive()) empire.StartToGive();
            return;
        }
        //计算军费
        // 追加当年财政数据
        empire.data.PreviousYearsMoney.Add(empire.CurrentMoney);

        // 始终只保留最近十年
        while (empire.data.PreviousYearsMoney.Count > 10)
            empire.data.PreviousYearsMoney.RemoveAt(0);
        
        if (empire.data.PreviousYearsMoney.Count==10)
        {
            var years = empire.data.PreviousYearsMoney;   // 最近若干年的财政记录（单位同 CurrentMoney）
            var rate = empire.data.MilitaryExpenditureRate;
            //计算军费
            double avg4 = years.Average();
            double avg3 = years.Take(years.Count() - 1).Average();
            double growthAvg = Math.Max(0, avg4 - avg3);
            int militaryCost = (int)(growthAvg  * rate);
            empire.data.MilitaryExpenditure = militaryCost;
            core.SubMoney(militaryCost, TreasuryCategory.Military);
            if (core.HasEnemies())
            {
                // 战时军费：旧版 兵力/4×战争数 极易打到负债扣正统，现为 兵力/10，最多按两场战争计
                var warExpend = (empire.countWarriors() / 10) * Math.Min(2, core.GetWars().Count());
                core.SubMoney(warExpend, TreasuryCategory.Military);
            }

            var jiedushis = empire.kingdoms_list.FindAll(k => k.GetKingdomType() == KingdomType.LvLing_jiedushi);
            if (jiedushis.Any())
            {
                //军府维护金
                var junfuMoney = jiedushis.Sum(k => k.countTotalWarriors());
                core.SubMoney(junfuMoney, TreasuryCategory.Military);
            }
        }
        if (empire.IsNeedToGive())
        {
            empire.StartToGive();
        }
    }
#endif
    /// <summary>
    /// 判断是否称帝，同时检测出现错误的帝国并予以消灭
    /// </summary>
    /// <param name="pKingdom"></param>
    /// <returns></returns>
#if EC_PENDING
    public void CheckPossible(Realm pKingdom)
    {
        if (pKingdom.IsGone()) return;
        Empire empire = pKingdom.GetEmpire();
        if (empire != null)
        {
            var coreKingdom = empire.CoreKingdom;
            if (coreKingdom == null || coreKingdom.IsGone())
            {
                empire.TryRepairState("CheckPossible: core kingdom invalid");
                return;
            }
            if (!empire.kingdoms_list.Contains(coreKingdom))
            {
                empire.TryRepairState("CheckPossible: core kingdom missing from empire list");
                return; 
            }
            if (pKingdom == empire.CoreKingdom && pKingdom.HasRuler() &&
                CompositeEmpireService.CanAdoptCentralInstitutions(pKingdom.Ruler))
            {
                var adoptionPlot = AssetManager.plots_library.get("adopt_central_plains_institutions");
                if (adoptionPlot?.try_to_start_advanced?.Invoke(pKingdom.Ruler, adoptionPlot, true) == true)
                {
                    TranslateHelper.LogCompositeEmpireAdoptionStarted(empire,
                        CompositeEmpireService.GetAdoptionStatus(empire));
                    return;
                }
            }
        }
        // 互为正统对手(僭越称帝后并立)的帝国：较强一方可发起正统之争
        if (empire != null && pKingdom == empire.CoreKingdom &&
            ImperialLegitimacyChallengeService.TryStartRivalryWar(empire)) return;
        // Modern sovereign states enter the empire layer directly. This is state formation,
        // not a monarch's proclamation plot, and therefore ignores pre-modern rise routes.
        // 本文化已废除君主制：独立的君主国(不在帝国里)改行现代政体，之后按现代国家组建政府或为军阀，不再有称帝
        if (empire == null && !pKingdom.IsInEmpire() && CultureAbolishedMonarchy(pKingdom) &&
            RegimeManager.IsMonarchy(pKingdom.GetRegime()?.type))
            CultureService.ApplyCulturePoliticalSystem(pKingdom, CultureService.GetInstitutionCulture(pKingdom));
        if (empire == null && ModernStateFormationSystem.TryUpdate(pKingdom)) return;
        if (pKingdom.HasRuler() && ImperialLegitimacyChallengeService.TryFindTarget(pKingdom, out _))
        {
            var challengePlot = AssetManager.plots_library.get("usurp_imperial_legitimacy");
            if (challengePlot?.try_to_start_advanced?.Invoke(pKingdom.Ruler, challengePlot, true) == true)
            {
                pKingdom.GetRegime()?.SetAllowDiplomacy(true);
                return;
            }
        }
        // 已经在筹备(或进行别的剧情)时不要重新开始，否则会反复覆盖筹备开始时的记录
        if (!pKingdom.HasRuler() || pKingdom.Ruler.plot?.isActive() == true) return;
        if (!CanStartEmpireFormation(pKingdom, true)) return;

        var plot = AssetManager.plots_library.get("become_empire");
        if (plot == null) return;
        EmpireFormationService.BeginPreparation(pKingdom, EmpireFormationService.GetBestRoute(pKingdom));
        if (plot.try_to_start_advanced?.Invoke(pKingdom.Ruler, plot, true) == true)
        {
            pKingdom.GetRegime()?.SetAllowDiplomacy(true);
        }
        else
        {
            pKingdom.GetOrCreate().empire_formation_started_timestamp = -1d;
        }
    }
#endif

    // 按制度文化看(旧制犹存的看旧制所属的文化，见 LegacyInstitutionSystem)
#if EC_PENDING
    private static bool CultureAbolishedMonarchy(Realm kingdom) =>
        !TechnologySystem.PremodernLocked &&
        InstitutionSystem.GetFeature(CultureService.GetInstitutionCulture(kingdom), RepublicSystem.FeatureAbolishMonarchy) > 0f;
#endif

    // 称帝资格：基础条件 + 不在失败冷却期 + 至少满足一条称帝路线(见 EmpireFormationService)
#if EC_PENDING
    public static bool CanStartEmpireFormation(Realm pKingdom, bool repairMainTitle = false)
    {
        if (pKingdom == null || pKingdom.IsGone()) return false;
        // 现代政体没有称帝：政权只能经 ModernStateFormationSystem 组建政府(所有文化；华夏条件更苛刻)
        if (pKingdom.GetRegime()?.type == RegimeType.Modern) return false;
        // 穿越政体没有君主：元老院国家不称帝(见 TransmigrantSystem)
        if (pKingdom.GetRegime()?.type == RegimeType.Transmigrant) return false;
        // 殖民地(总督府、特许公司)、傀儡、宗主国不在图上的剧本属地(东亚半幅的英属印度)不称帝(见 WarlordEraSystem.Dependent)
        if (WarlordEraSystem.Dependent(pKingdom)) return false;
        // 原始政体的部落不称帝，只能打赢收部结成部落联盟(用户 2026-10-26 选"部落不再是帝国"，见 TribalFissionService)；
        // 先挡在这里，免得每回 AI 检查都去补立法理(RepairFoundingTitle)
        if (TribalFissionService.IsTribal(pKingdom)) return false;
        // 本文化已废除君主制：不再有人称帝
        if (CultureAbolishedMonarchy(pKingdom)) return false;
        // 周制下一个文化只有一个正统核心：本文化已有周制王朝时，别国不能称帝(只能受封为诸侯，战国称王)
        if (ZhouEraSystem.BlocksRivalEmpire(pKingdom)) return false;
        // 向别国称臣纳贡的不称帝：先脱离朝贡再说(用户 2026-11-09 明清鼎革：清的朝贡国朝鲜据有本国法理就称了帝，
        // 朝贡随之作废，应明的号召打清，后来改朝成"大靖"、开国雄主又从背后打满洲)
        if (pKingdom.HasTakenAlliance()) return false;
        // 万世一系：已有天皇在位，别家不另立天皇
        if (ConstitutionalSuccessionSystem.BlocksRivalThrone(pKingdom)) return false;
        if (repairMainTitle && pKingdom.HasRuler() && !pKingdom.HasMainTitle() &&
            !pKingdom.IsEmpire() && !pKingdom.IsInEmpire())
        {
            EmpireFormationService.RepairFoundingTitle(pKingdom);
        }
        // 共主兼领的王国(城邦同盟成员等)不单独称帝：称帝剧情由君主发起，筹备记录、失败冷却都记在
        // 君主本国(actor.kingdom)身上；若让兼领国发起，本国没有筹备记录会立刻判定"被超越"失败，
        // 而兼领国自己又不进冷却，于是每轮都重新筹备、反复失败
        if (pKingdom.Ruler == null || pKingdom.Ruler.Realm != pKingdom) return false;
        if (!EmpireFormationService.MeetsBaseRequirements(pKingdom)) return false;
        if (EmpireFormationService.IsInFailureCooldown(pKingdom)) return false;
        return EmpireFormationService.GetBestRoute(pKingdom) != EmpireFormationRoute.None;
    }

    public void CheckEmpireAlliance(Realm pKingdom)
    {
        // 有质子在上国的不自行退出朝贡(见 HostageSystem)
        if (pKingdom.NeedToRemoveTakenAlliance() && !EmpireCraft.Core.HostageSystem.Binds(pKingdom))
        {
            pKingdom.RemoveTakenAlliance();
        }
        else if (pKingdom.HasTakenAlliance())
        {
            // 朝贡国挂着自己与宗主各半的合成色(见 VassalColorService；旧版整个染成帝国色的它会先换回原色)。
            // 以前这里每次都 RestoreOriginalKingdomColor：合成色被抹掉、整图重画，两秒后 TickReassert 又套上、再整图重画，
            // 朝贡国颜色一直变来变去(用户 2026-10-27)
            Realm suzerain = pKingdom.GetTakenAllianceEmpire()?.CoreKingdom;
            if (suzerain != null) VassalColorService.Apply(pKingdom, suzerain);
        }
        if (pKingdom.NeedToRemoveGivenAlliance())
        {
            pKingdom.RemoveGivenAlliance();
        }
    }
#endif
}
}
