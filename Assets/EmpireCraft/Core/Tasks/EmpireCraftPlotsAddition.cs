// Generated from the EmpireCraft mod: Scripts/AI/EmpireCraftPlotsAddition.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using Mathf = EmpireCraft.Core.EcMath;
#if EC_PENDING
using NCMS.Extensions;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using static EmpireCraft.Core.OverallHelperFunc;
using static System.Collections.Specialized.BitVector32;
using Random = System.Random;

namespace EmpireCraft.Core
{
    public static class EmpireCraftPlotsAddition
    {
        // 派系首领插手地方、国君邀别国入派花的名望(以前 50，邀人门槛 300；名望开销按身份年入的新名望重定(用户 2026-11-10 "名望开销也可以调整"；稳态皇帝八九百、一品大员五百上下、封君五百、五品一两百))
        private const int FactionLeaderLocalInfluenceCost = 30;
        private const int InviteToFactionCost = 30;
        private const int InviteToFactionMinRenown = 200;
        private static readonly HashSet<string> FailedPlotExecutions = new();
        public static PlotKind Rebellion;
        private static readonly HashSet<string> s_guardedPlotIds = new HashSet<string>();

        private static Realm FindLocalFactionInfluenceTarget(Person actor)
        {
            if (actor == null || actor.IsGone() || !actor.IsRuler() || !actor.HasFaction() ||
                actor.Renown < InviteToFactionCost) return null;
            Realm source = actor.Realm;
            Empire empire = source?.GetEmpire();
            if (empire == null || empire.IsArchived() || source == empire.CoreKingdom ||
                source.Ruler != actor || actor.GetFaction()?.Empire != empire) return null;
            Realm core = empire.CoreKingdom;
            return core != null && !core.IsGone() && core.GetEmpire() == empire ? core : null;
        }

        private static FixedFaction GetLeadingFaction(Person actor)
        {
            if (actor == null || actor.IsGone() || !actor.HasFaction()) return null;
            FixedFaction faction = actor.GetFaction();
            return faction?.GetLeader()?.Key == actor.Key ? faction : null;
        }

        private static bool CanFactionLeaderInfluenceLocalKingdom(Person actor, Realm target)
        {
            FixedFaction faction = GetLeadingFaction(actor);
            Empire empire = faction?.Empire;
            return actor?.Record != null && actor.Renown >= FactionLeaderLocalInfluenceCost &&
                   empire != null && !empire.IsGone() && !empire.IsArchived() &&
                   target != null && !target.IsGone() && target != empire.CoreKingdom &&
                   target.GetEmpire() == empire && target.GetFactionRatioValue(faction) < 100;
        }

        private static Realm FindFactionLeaderLocalInfluenceTarget(Person actor)
        {
            FixedFaction faction = GetLeadingFaction(actor);
            Empire empire = faction?.Empire;
            if (empire?.kingdoms_list == null || actor?.Record == null ||
                actor.Renown < FactionLeaderLocalInfluenceCost) return null;
            return empire.kingdoms_list
                .Where(target => CanFactionLeaderInfluenceLocalKingdom(actor, target))
                .OrderBy(target => target.GetFactionRatioValue(faction))
                .ThenByDescending(target => target.PopulationCount())
                .FirstOrDefault();
        }

        private static bool FindIndependentTitleCultureConversion(Person actor, out KingdomTitle title,
            out string candidate)
        {
            title = null;
            candidate = null;
            if (actor == null || actor.IsGone() || !actor.IsRuler()) return false;
            Realm kingdom = actor.Realm;
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.Ruler != actor || kingdom.IsInEmpire())
                return false;

            title = kingdom.GetMainTitle();
            if (title?.data == null || title.IsGone() || title.title_capital == null ||
                title.title_capital.IsGone() || title.title_capital.Realm != kingdom ||
                kingdom.Cities?.Contains(title.title_capital) != true)
            {
                title = null;
                return false;
            }

            return CultureService.FindTitleCultureConversionCandidate(title, out candidate);
        }

        private static bool FindCultureRestorationCity(Person actor, out City city, out string culture)
        {
            city = null;
            culture = null;
            if (actor == null || actor.IsGone() || !actor.IsRuler()) return false;
            Realm kingdom = actor.Realm;
            if (kingdom?.Record == null || kingdom.IsGone() || kingdom.Ruler != actor || kingdom.Cities == null)
                return false;

            foreach (City candidateCity in kingdom.Cities
                         .Where(item => item?.Record != null && !item.IsGone())
                         .OrderBy(item => item == kingdom.Capital ? 0 : 1)
                         .ThenBy(item => item.Key))
            {
                if (!CultureService.FindCultureRestorationCandidate(candidateCity, kingdom,
                        out string candidateCulture)) continue;
                city = candidateCity;
                culture = candidateCulture;
                return true;
            }
            return false;
        }

        // 世界规则"禁止原版结盟"(默认开启)：原版的建盟/入盟决议不再发起，也不再继续推进。
        // 模组自己建的同盟(共主联盟、城邦同盟、反帝同盟等)直接调用接口，不受影响。
#if EC_PENDING
        private static void BlockVanillaAlliancePlots()
        {
            foreach (PlotKind plot in new[]
                     { PlotsLibrary.alliance_create, AssetManager.plots_library.get("alliance_join") })
            {
                if (plot == null) continue;
                var possible = plot.check_is_possible;
                plot.check_is_possible = pActor =>
                    !IsVanillaAllianceBlocked() && (possible == null || possible(pActor));
                var shouldContinue = plot.check_should_continue;
                plot.check_should_continue = pActor =>
                    !IsVanillaAllianceBlocked() && (shouldContinue == null || shouldContinue(pActor));
                var canBeForced = plot.check_can_be_forced;
                if (canBeForced != null)
                    plot.check_can_be_forced = pActor =>
                        !IsVanillaAllianceBlocked() && canBeForced(pActor);
                var tryToStart = plot.try_to_start_advanced;
                if (tryToStart != null)
                    plot.try_to_start_advanced = (pActor, pPlotAsset, pForced) =>
                        !IsVanillaAllianceBlocked() && tryToStart(pActor, pPlotAsset, pForced);
                var action = plot.action;
                if (action != null)
                    plot.action = pActor => !IsVanillaAllianceBlocked() && action(pActor);
            }
        }
#endif

        private static bool IsVanillaAllianceBlocked() => ModAllianceService.IsVanillaAllianceBanned();

#if EC_PENDING
        private static void GuardAllPlotAssets()
        {
            foreach (var asset in AssetManager.plots_library.getList())
            {
                var plotAsset = (PlotKind)asset;
                GuardPlotAsset(plotAsset);
            }

            GuardPlotAsset(PlotsLibrary.alliance_create);
        }

        private static void GuardPlotAsset(PlotKind plotAsset)
        {
            if (plotAsset == null || string.IsNullOrWhiteSpace(plotAsset.Id))
            {
                return;
            }

            if (!s_guardedPlotIds.Add(plotAsset.Id))
            {
                return;
            }

            string plotId = plotAsset.Id;

            if (plotAsset.check_is_possible != null)
            {
                var original = plotAsset.check_is_possible;
                plotAsset.check_is_possible = pActor => SafePlotBoolCall(plotId, "check_is_possible", pActor, () => original(pActor));
            }

            if (plotAsset.check_should_continue != null)
            {
                var original = plotAsset.check_should_continue;
                plotAsset.check_should_continue = pActor => SafePlotBoolCall(plotId, "check_should_continue", pActor, () => original(pActor));
            }

            if (plotAsset.check_can_be_forced != null)
            {
                var original = plotAsset.check_can_be_forced;
                plotAsset.check_can_be_forced = pActor => SafePlotBoolCall(plotId, "check_can_be_forced", pActor, () => original(pActor));
            }

            if (plotAsset.try_to_start_advanced != null)
            {
                var original = plotAsset.try_to_start_advanced;
                plotAsset.try_to_start_advanced = (pActor, pPlotAsset, pForced) =>
                    SafePlotStartCall(plotId, pActor, pPlotAsset, pForced, () => original(pActor, pPlotAsset, pForced));
            }

            if (plotAsset.action != null)
            {
                var original = plotAsset.action;
                plotAsset.action = pActor => SafePlotBoolCall(plotId, "action", pActor, () => original(pActor));
            }
        }

        private static bool SafePlotBoolCall(string plotId, string stage, Person actor, Func<bool> callback)
        {
            if (actor == null || callback == null)
            {
                return false;
            }

            try
            {
                return callback();
            }
            catch (Exception ex)
            {
                LogPlotGuardException(plotId, stage, actor, ex);
                return false;
            }
        }

        private static bool SafePlotStartCall(string plotId, Person actor, PlotKind plotAsset, bool forced, Func<bool> callback)
        {
            if (actor == null || plotAsset == null || callback == null)
            {
                return false;
            }

            try
            {
                return callback();
            }
            catch (Exception ex)
            {
                LogPlotGuardException(plotId, $"try_to_start_advanced(forced={forced})", actor, ex);
                return false;
            }
        }

        private static bool TryStartSimplePlot(Person actor, PlotKind plotAsset, bool forced)
        {
            if (actor == null || actor.IsGone() || plotAsset == null || EmpireWorld.Current?.Plots == null ||
                actor.plot?.isActive() == true) return false;
            return EmpireWorld.Current.Plots.newPlot(actor, plotAsset, forced) != null;
        }

        private static void LogPlotGuardException(string plotId, string stage, Person actor, Exception ex)
        {
            string actorName = actor?.Name ?? "unknown_actor";
            // A failed plot may already have changed state; stop it instead of retrying it every AI tick.
            if (actor?.plot?.name == plotId)
            {
                actor.plot.setAlive(false);
            }

            string errorKey = $"{plotId}:{stage}";
            if (FailedPlotExecutions.Add(errorKey))
            {
                Debug.LogWarning($"[EmpireCraftPlotsAddition] plot '{plotId}' {stage} was stopped for actor '{actorName}': {ex}");
            }
        }
#endif

        // 便宜的先查(在不在打仗、能不能求法理)，可取的头衔(要逐个法理算)放最后
        private static bool CanRunKingdomGetTitlePlot(Person actor) => actor?.Realm != null &&
            actor.Realm.GetWarsCached(false).Count == 0 && CanContinueKingdomGetTitlePlot(actor);

        private static bool CanContinueKingdomGetTitlePlot(Person actor)
        {
            if (actor == null || actor.IsGone() || !actor.Alive || !actor.IsRuler() || !actor.HasRealm()) return false;
            Realm kingdom = actor.Realm;
            if (kingdom == null || kingdom.IsGone() || kingdom.Ruler != actor || !kingdom.CanPursueDeJureTitle()) return false;
            return actor.canTakeTitle();
        }

        private static Realm FindTitleReclamationTarget(Empire empire) => empire?.kingdoms_list.Find(kingdom =>
            empire.CanReclaimTitlesFrom(kingdom));

        private static Realm FindCrimeExposureTarget(Person actor)
        {
            Realm source = actor?.Realm;
            Empire empire = source?.GetEmpire();
            if (actor == null || actor.IsGone() || !actor.Alive || !actor.IsRuler() ||
                source == null || source.IsGone() || source.Ruler != actor || empire == null ||
                empire.IsGone() || empire.IsArchived() || !actor.HasFaction()) return null;
            return empire.kingdoms_list?.Find(target => target != null && !target.IsGone() &&
                target != source && target.GetEmpire() == empire && target.Ruler != null && target.Ruler.Alive &&
                EmpireLawSystem.CanEnforceLawInEmpireScope(target.Ruler, source) &&
                target.Ruler.GetFaction() != actor.GetFaction() && target.Ruler.Renown / 2 < actor.Renown &&
                target.Ruler.GetViolateValue() >= 0);
        }

#if EC_PENDING
        private static bool CanRunTributaryTitlePetition(Person actor)
        {
            if (actor == null || !actor.IsRuler() || !actor.HasRealm()) return false;
            Realm kingdom = actor.Realm;
            Empire empire = kingdom?.GetTakenAllianceEmpire();
            return empire?.GetTributaryPetitionTitle(kingdom) != null;
        }
#endif

        private static List<Realm> GetKingdomsRuledByActor(Person actor)
        {
            List<Realm> result = new List<Realm>();
            if (actor == null || EmpireWorld.Current?.LiveRealms == null)
            {
                return result;
            }

            foreach (Realm kingdom in EmpireWorld.Current.LiveRealms)
            {
                if (kingdom != null && kingdom.Ruler == actor)
                {
                    result.Add(kingdom);
                }
            }

            return result;
        }

#if EC_PENDING
        private static Empire FindTakenAllianceEmpireCandidate(Realm kingdom, bool requireGoodOpinion)
        {
            if (kingdom == null)
            {
                return null;
            }

            foreach (Empire empire in ModClass.EMPIRE_MANAGER)
            {
                // 接壤之外还要宗主直辖之地够得着(封国、附庸挨着的远国不来朝，见 TributaryRelations.InReach)
                if (empire == null || empire.IsArchived() || !empire.IsNeighbourWith(kingdom) || !TributaryRelations.InReach(kingdom, empire))
                {
                    continue;
                }

                if (!FeudalConquestService.HasTributeInstitution(empire))
                {
                    continue;
                }

                if (!empire.CanAcceptVoluntarySubmission() || kingdom.HasRebelledAgainst(empire) ||
                    !kingdom.CanVoluntarilyBecomeTributaryOf(empire) ||
                    !empire.MeetsTributaryPowerThreshold(kingdom))
                {
                    continue;
                }

                if (requireGoodOpinion && !kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom))
                {
                    continue;
                }

                return empire;
            }

            return null;
        }

        public static void init()
        {
            s_guardedPlotIds.Clear();
            AssetManager.plot_category_library.add(new PlotCategoryAsset()
            {
                id = "empirecraft_diplomacy",
                name = "plot_group_empirecraft_diplomacy",
                color = "#5EFFFF",
                show_counter = true,
                plot_retry_action = new PlotRetryAction(PlotCategoryLibrary.diplomacyRetryAction)
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "feudal_offer_vassalage",
                path_icon = "ui/icons/plots/plot_feudal_offer_vassalage",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                can_be_done_by_king = true,
                progress_needed = 15f,
                try_to_start_advanced = TryStartSimplePlot,
                check_is_possible = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.FindPeacefulSubmissionTarget(actor.kingdom) != null,
                check_should_continue = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.FindPeacefulSubmissionTarget(actor.kingdom) != null,
                action = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.Bind(actor.kingdom,
                        FeudalVassalService.FindPeacefulSubmissionTarget(actor.kingdom))
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "feudal_tighten_vassalage",
                path_icon = "ui/icons/plots/plot_feudal_tighten_vassalage",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                can_be_done_by_king = true,
                progress_needed = 10f,
                try_to_start_advanced = TryStartSimplePlot,
                check_is_possible = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.FindControlTarget(actor.kingdom) != null,
                check_should_continue = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.FindControlTarget(actor.kingdom) != null,
                action = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.IncreaseControl(actor.kingdom,
                        FeudalVassalService.FindControlTarget(actor.kingdom))
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "feudal_annex_vassal",
                path_icon = "ui/icons/plots/plot_feudal_annex_vassal",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                can_be_done_by_king = true,
                progress_needed = 25f,
                try_to_start_advanced = TryStartSimplePlot,
                check_is_possible = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.FindAnnexTarget(actor.kingdom) != null,
                check_should_continue = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.FindAnnexTarget(actor.kingdom) != null,
                action = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.Annex(actor.kingdom,
                        FeudalVassalService.FindAnnexTarget(actor.kingdom))
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "feudal_independence_war",
                path_icon = "ui/icons/plots/plot_feudal_independence_war",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                can_be_done_by_king = true,
                progress_needed = 20f,
                try_to_start_advanced = TryStartSimplePlot,
                check_is_possible = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.CanSeekIndependence(actor.kingdom),
                check_should_continue = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.CanSeekIndependence(actor.kingdom),
                action = actor => actor?.kingdom?.king == actor &&
                    FeudalVassalService.StartIndependenceWar(actor.kingdom)
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "become_empire",
                path_icon = "ui/icons/plots/plot_become_empire",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 0,
                money_cost = 0,
                progress_needed = 60f,
                can_be_done_by_king = true,
                check_is_possible = pActor => false,
                // 上帝强制也要本文化已推行称帝制度(见 EmpireFormationService.HasImperialRule)
                check_can_be_forced = pActor => !EmpireCraft.Core.AncientWarfareCompatibility.BlocksEmpireFormation(pActor?.kingdom) &&
                                                EmpireFormationService.HasImperialRule(pActor?.kingdom),
                try_to_start_advanced = TryStartSimplePlot,
                // 筹备期：打败仗、国库转负、被同文化别国超过、不再满足路线都会让称帝失败(进入冷却)
                check_should_continue = EmpireFormationService.ShouldContinuePreparation,
                action = BecomeEmpireAndStartEnfeoff
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "usurp_imperial_legitimacy",
                path_icon = "ui/icons/plots/plot_usurp_imperial_legitimacy",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 0,
                progress_needed = 60f,
                can_be_done_by_king = true,
                check_is_possible = ImperialLegitimacyChallengeService.CanChallenge,
                check_can_be_forced = ImperialLegitimacyChallengeService.CanContinueChallenge,
                try_to_start_advanced = TryStartSimplePlot,
                check_should_continue = ImperialLegitimacyChallengeService.CanContinueChallenge,
                action = ImperialLegitimacyChallengeService.StartChallenge
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "adopt_central_plains_institutions",
                path_icon = "ui/icons/plots/plot_adopt_central_plains_institutions",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 0,
                progress_needed = 30f,
                can_be_done_by_king = true,
                check_is_possible = CompositeEmpireService.CanAdoptCentralInstitutions,
                check_can_be_forced = CompositeEmpireService.CanAdoptCentralInstitutions,
                try_to_start_advanced = TryStartSimplePlot,
                check_should_continue = CompositeEmpireService.CanAdoptCentralInstitutions,
                action = CompositeEmpireService.AdoptCentralInstitutions
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "combine_kingdom",
                path_icon = "ui/icons/plots/plot_combine_kingdom",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 0,
                progress_needed = 60f,
                can_be_done_by_king = true,
                check_is_possible = delegate(Person pActor)
                {
                    if (!pActor.IsRuler()) return false;
                    var allKingdoms = GetKingdomsRuledByActor(pActor);
                    // 推恩令下不允许统合封国(一人本就不得兼领两国)
                    if (allKingdoms.Any(k => GraceEdictService.IsActive(k.GetEmpire()))) return false;
                    // 盟旗分治：外藩不许统合成大国(见 BannerSystem.Frontier)
                    if (allKingdoms.Any(BannerSystem.LeagueBound)) return false;
                    // 西方封建制与城邦无法整合土地，施行中央集权(直辖治理)后可以
                    if (allKingdoms.Any(k => PersonalUnionService.IsUnionRegime(k) && !FeudalVassalService.HasDirectAdministration(k)))
                        return false;
                    if (!pActor.HasTitle()) return false;
                    if (ModClass.KINGDOM_TITLE_MANAGER.Get(pActor.GetOwnedTitle()[0])?.title_capital?.Realm?.Ruler !=
                        pActor) return false;
                    return allKingdoms.Count > 1;
                },
                check_should_continue = delegate(Person pActor)
                {
                    if (!pActor.IsRuler()) return false;
                    var allKingdoms = GetKingdomsRuledByActor(pActor);
                    if (allKingdoms.Any(k => GraceEdictService.IsActive(k.GetEmpire()))) return false;
                    if (allKingdoms.Any(BannerSystem.LeagueBound)) return false;
                    // 西方封建制与城邦无法整合土地，施行中央集权(直辖治理)后可以
                    if (allKingdoms.Any(k => PersonalUnionService.IsUnionRegime(k) && !FeudalVassalService.HasDirectAdministration(k)))
                        return false;
                    if (!pActor.HasTitle()) return false;
                    return allKingdoms.Count > 1;
                },
                action = delegate(Person pActor)
                {
                    if (!pActor.IsRuler()) return false;
                    
                    var allKingdoms = GetKingdomsRuledByActor(pActor);
                    if (allKingdoms.Count < 2) return false;

                    Realm mainKingdom = null;
                    
                    // 1. Prioritize Empire Core
                    var empireCore = allKingdoms.Find(k => k.IsInEmpire() && k.GetEmpire()?.CoreKingdom == k);
                    if (empireCore != null)
                    {
                        mainKingdom = empireCore;
                    }
                    else
                    {
                        // 2. Fallback to Primary Title
                        if (pActor.HasTitle())
                        {
                            var title = ModClass.KINGDOM_TITLE_MANAGER.Get(pActor.GetOwnedTitle()[0]);
                            if (title?.title_capital?.Realm?.Ruler == pActor)
                            {
                                mainKingdom = title.title_capital.Realm;
                            }
                        }
                        
                        // 3. Fallback to current kingdom
                        if (mainKingdom == null)
                        {
                            mainKingdom = pActor.Realm;
                        }
                    }

                    if (mainKingdom == null) return false;

                    foreach (var kingdom in allKingdoms)
                    {
                        if (kingdom == mainKingdom) continue;
                        List<City> citiesSnapshot = new List<City>(kingdom.Cities);
                        for (int i = 0; i < citiesSnapshot.Count; i++)
                        {
                            citiesSnapshot[i]?.JoinRealm(mainKingdom);
                        }
                    }
                    TranslateHelper.LogCombineKingdom(pActor);
                    return true;
                }
            });            
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empire_plots",
                path_icon = "ui/icons/plots/plot_empire_plots",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 0,
                progress_needed = 60f,
                can_be_done_by_king = true,
                check_is_possible = pActor => false,
                check_can_be_forced = (Person pActor) => true,
                try_to_start_advanced = delegate(Person pActor, PlotKind pPlotAsset, bool pForced)
                {
                    var kingdom = pActor.Realm;
                    var regime = kingdom?.GetRegime();
                    if (regime == null) return false;
                    var run = kingdom.GetEmpire()?.RunningTemporaryFaction;
                    if (run == null) return false;
                    foreach (Plot plot3 in EmpireWorld.Current.Plots)
                    {
                        if (plot3 == pActor.plot && plot3.IsActive() && plot3.isSameType(pPlotAsset))
                        {
                            pActor.setPlot(plot3);
                            plot3._plot_asset.progress_needed = run.progressMax - run.acceleration;
                            plot3.setName(run.type.ToString());
                            return true;
                        }
                    }
                    var newPlot = EmpireWorld.Current.Plots.newPlot(pActor, pPlotAsset, pForced);
                    newPlot._plot_asset.progress_needed = run.progressMax - run.acceleration;
                    newPlot.setName(run.type.ToString());
                    return true;
                },
                check_should_continue = delegate(Person pActor)
                {
                    var kingdom = pActor.Realm;
                    var regime = kingdom?.GetRegime();
                    if (regime == null) return false;
                    var run = kingdom.GetEmpire()?.RunningTemporaryFaction;
                    if (run == null) return false;
                    if (!run.CheckContinue()) return false;
                    if (run.IsLocallyPushed && !run.CheckLocalContinue(run.GetKingdom())) return false;
                    if (!run.IsStarted()) return false;
                    if (!run.CheckTarget()) return false;
                    return true;
                },
                action = delegate(Person pActor)
                {
                    var kingdom = pActor.Realm;
                    var regime = kingdom?.GetRegime();
                    var run = kingdom?.GetEmpire()?.RunningTemporaryFaction;
                    if (run == null) return false;
                    if (!run.CheckContinue() || !run.CheckTarget() ||
                        (run.IsLocallyPushed && !run.CheckLocalContinue(run.GetKingdom())))
                    {
                        run.End();
                        return false;
                    }
                    // 推动类决议(见 ClaimRules)：执行前取好目标制度，执行后对它发起改革
                    Empire runEmpire = run.GetEmpire();
                    var pushTarget = ClaimRules.FindPushTarget(runEmpire, run.type);
                    string runFaction = run.factionID;
                    run.RunExecute();
                    ClaimRules.AfterExecute(runEmpire, run.type, pushTarget, runFaction);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
		    {
			    Id = "force_stop_war",
			    is_basic_plot = true,
			    path_icon = "ui/icons/plots/plot_force_stop_war",
			    group_id = "empirecraft_diplomacy",
			    min_level = 4,
			    money_cost = 0,
			    min_diplomacy = 8,
                show_for_unlockables_ui = false,
			    can_be_done_by_king = true,
			    check_target_war = true,
			    requires_diplomacy = false,
			    check_is_possible = (Person pActor) => false,
			    try_to_start_advanced = delegate(Person pActor, PlotKind pPlotAsset, bool pForced)
			    {
				    Realm kingdom = pActor.Realm;
				    War war = null;
                    var warsList1 = KingdomExtension.GetWarsCached(kingdom, true);
				    for (int wi = 0; wi < warsList1.Count; wi++)
				    {
                        var war3 = warsList1[wi];
					    if (war3.IsAttacker(kingdom)&&war3.DurationMonths()>=ModClass.WAR_END_YEAR&&
					        !EmpireCraft.Core.RulerTraitSystem.IsFounderUnificationWar(war3))
					    {
							war = war3;
							break;
					    }
				    }
				    if (war == null)
				    {
					    return false;
				    }
				    foreach (Plot plot3 in EmpireWorld.Current.Plots)
				    {
					    if (plot3.IsActive() && plot3.isSameType(pPlotAsset) && plot3.target_war == war)
					    {
						    pActor.setPlot(plot3);
						    return true;
					    }
				    }
				    Plot plot = EmpireWorld.Current.Plots.newPlot(pActor, pPlotAsset, pForced);
				    plot.target_war = war;
				    if (!plot.checkInitiatorAndTargets())
				    {
					    Debug.Log("tryPlotStopWar is missing start requirements");
					    return true;
				    }
				    return true;
			    },
			    check_can_be_forced = (Person pActor) => pActor.Realm.HasEnemies() ? true : false,
			    check_should_continue = delegate(Person pActor)
			    {
				    Plot plot = pActor.plot;
				    War targetWar = plot.target_war;
				    if (targetWar == null || targetWar.IsGone())
				    {
					    return false;
				    }
				    return !plot.target_war.hasEnded();
			    },
			    action = delegate(Person pActor)
			    {
				    Plot plot = pActor.plot;
				    if (plot.target_war.hasEnded())
				    {
					    return false;
				    }
				    EmpireWorld.Current.EndWar(plot.target_war, WarOutcome.Peace);
				    return true;
			    }
		    });
            // "还于旧都"：回到本朝建国时的旧都；或者延续政权(帝国核心历代里有同一宗族的前朝)、
            // 复国之君(中兴/复国特质)迁往核心首都。其余为"迁都"
            static bool IsReturningCapital(Empire empire, City target, Person ruler)
            {
                if (target == empire.OriginalCapital) return true;
                if (RulerTraitSystem.HasActiveTrait(ruler, RulerTraitSystem.Restorer)) return true;
                EmpireCore core = EmpireCoreManager.Get(empire);
                long clan = empire.data?.empire_specific_clan ?? -1L;
                if (core?.empire_history_ids == null || clan <= 0) return false;
                foreach (long id in core.empire_history_ids)
                {
                    if (id == empire.Key) continue;
                    Empire previous = ModClass.EMPIRE_MANAGER.Get(id);
                    if (previous?.data != null && previous.data.empire_specific_clan == clan) return true;
                }
                return false;
            }
            static City CapitalToMoveTo(Realm kingdom)
            {
                Empire empire = kingdom?.GetEmpire();
                if (empire == null || !kingdom.IsEmpire()) return null;
                City core = EmpireCoreManager.Get(empire)?.GetCoreCapital();
                if (core != null && !core.IsGone() && core.Realm == kingdom)
                    return core == kingdom.Capital ? null : core;
                City original = empire.OriginalCapital;
                if (original == null || original.IsGone() || original.Realm != kingdom || original == kingdom.Capital)
                    return null;
                return original;
            }
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empire_move_back_to_capital",
                path_icon = "ui/icons/plots/plot_empire_move_back_to_capital",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_king = true,
                // 迁都：建国后优先迁往帝国核心的核心首都(历代定都之地)，本国没占着核心首都时迁回建国时的旧都
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsEmperor()) return false;
                    if (!pActor.IsRuler()) return false;
                    if (!kingdom.IsEmpire()) return false;
                    return CapitalToMoveTo(kingdom) != null;
                },
                check_should_continue = delegate (Person actor)
                {
                    if (!actor.IsEmperor()) return false;
                    if (!actor.IsRuler()) return false;
                    return CapitalToMoveTo(actor.Realm) != null;
                },
                action = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    Empire empire = kingdom.GetEmpire();
                    City target = CapitalToMoveTo(kingdom);
                    if (empire == null || target == null) return false;
                    bool returning = IsReturningCapital(empire, target, pActor);
                    kingdom.SetCapital(target);
                    // 迁都营建宫室、官署，征发民力：记工役，算大兴土木；还于旧都宫室尚在，减半(见 CorveeSystem)
                    CorveeSystem.Add(kingdom, returning ? CorveeSystem.CapitalMove * 0.5f : CorveeSystem.CapitalMove, true);
                    EventRecorder.Record(empire, string.Format(LM.Get(returning ? "empire_return_capital_history"
                        : "empire_move_capital_history"), empire.GetEmpireFullName(), target.GetCityName()), pActor);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_petition_title",
                path_icon = "ui/icons/plots/plot_kingdom_petition_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = CanRunTributaryTitlePetition,
                check_should_continue = CanRunTributaryTitlePetition,
                action = delegate(Person pActor)
                {
                    if (!CanRunTributaryTitlePetition(pActor)) return false;
                    Realm kingdom = pActor.Realm;
                    Empire empire = kingdom.GetTakenAllianceEmpire();
                    KingdomTitle title = empire?.GetTributaryPetitionTitle(kingdom);
                    return empire?.GrantTributaryPetitionTitle(kingdom, title) == true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_start_join_taken_alliance",
                path_icon = "ui/icons/plots/plot_kingdom_start_join_taken_alliance",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (kingdom.IsInEmpire()) return false;
                    if (kingdom.HasTakenAlliance()) return false;
                    return FindTakenAllianceEmpireCandidate(kingdom, true) != null;
                },
                action = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    Empire empire = FindTakenAllianceEmpireCandidate(kingdom, false);
                    if (empire == null)
                    {
                        return false;
                    }
                    kingdom.JoinTakenAlliance(empire);
                    if (kingdom.GetTakenAllianceEmpire() != empire) return false;
                    TranslateHelper.LogJoinTakenAlliance(kingdom, empire);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_start_invite_to_faction",
                path_icon = "ui/icons/plots/plot_kingdom_start_invite_to_faction",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                min_renown_actor = InviteToFactionMinRenown,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null) return false;
                    if (!pActor.HasFaction()) return false;
                    return FindLocalFactionInfluenceTarget(pActor) != null;
                },
                check_should_continue = actor => FindLocalFactionInfluenceTarget(actor) != null,
                action = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    Realm target = FindLocalFactionInfluenceTarget(pActor);
                    if (target == null || !target.TryIncreaseFactionRatio(pActor.GetFaction(), 1)) return false;
                    pActor.Renown -= InviteToFactionCost;
                    TranslateHelper.LogInviteIntoFaction(kingdom, target, pActor.GetFaction());
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "faction_leader_influence_local_kingdom",
                path_icon = "ui/icons/plots/plot_faction_leader_influence_local_kingdom",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                min_renown_actor = FactionLeaderLocalInfluenceCost,
                can_be_done_by_king = true,
                can_be_done_by_leader = true,
                check_target_kingdom = true,
                check_is_possible = actor => FindFactionLeaderLocalInfluenceTarget(actor) != null,
                try_to_start_advanced = delegate(Person actor, PlotKind plotAsset, bool forced)
                {
                    Realm target = FindFactionLeaderLocalInfluenceTarget(actor);
                    if (target == null) return false;
                    Plot plot = EmpireWorld.Current.Plots.newPlot(actor, plotAsset, forced);
                    plot.TargetRealm = target;
                    plot.setName($"{actor.FullName()}正在扩大派系对{target.Name}的影响");
                    return true;
                },
                check_should_continue = actor => actor?.plot != null &&
                                                  CanFactionLeaderInfluenceLocalKingdom(actor, actor.plot.target_kingdom),
                action = delegate(Person actor)
                {
                    Realm target = actor?.plot?.target_kingdom;
                    FixedFaction faction = GetLeadingFaction(actor);
                    if (faction == null || !CanFactionLeaderInfluenceLocalKingdom(actor, target) ||
                        !target.TryIncreaseFactionRatio(faction, 1)) return false;
                    actor.editRenown(-FactionLeaderLocalInfluenceCost);
                    TranslateHelper.LogFactionLeaderInfluenceLocalKingdom(actor, target, faction);
                    return true;
                }
            });
            // 城市主流文化不再随占比自动切换：占比超过阈值后，由城主（或没有单独领主时的国王）
            // 发起这条决议来正式把挑战文化定为城市主流文化。
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empirecraft_city_culture_shift",
                path_icon = "ui/icons/plots/plot_empirecraft_city_culture_shift",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_leader = true,
                check_is_possible = delegate (Person pActor)
                {
                    City city = pActor?.City;
                    if (city == null || city.IsGone()) return false;
                    Person cityLeader = city.Leader ?? city.Realm?.Ruler;
                    if (cityLeader != pActor) return false;
                    return CultureService.FindCityCultureShiftCandidate(city, out _);
                },
                check_should_continue = delegate (Person pActor)
                {
                    City city = pActor?.City;
                    if (city == null || city.IsGone()) return false;
                    Person cityLeader = city.Leader ?? city.Realm?.Ruler;
                    return cityLeader == pActor && CultureService.FindCityCultureShiftCandidate(city, out _);
                },
                action = delegate (Person pActor)
                {
                    City city = pActor?.City;
                    if (city == null || city.IsGone()) return false;
                    Person cityLeader = city.Leader ?? city.Realm?.Ruler;
                    if (cityLeader != pActor) return false;
                    if (!CultureService.FindCityCultureShiftCandidate(city, out string candidate)) return false;
                    string previous = CultureService.GetMainCulture(city);
                    if (!CultureService.SetCityMainCulture(city, candidate)) return false;
                    TranslateHelper.LogCityCultureShift(city, previous, candidate);
                    return true;
                }
            });
            // 帝国内的法理文化转换由“文治”派系推动；独立国家没有帝国临时派系实例，
            // 因而由实际控制法理首都的国王自行发起同等条件的国家决议。
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empirecraft_independent_title_culture_conversion",
                path_icon = "ui/icons/plots/plot_empirecraft_independent_title_culture_conversion",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    return FindIndependentTitleCultureConversion(pActor, out _, out _);
                },
                check_should_continue = delegate (Person pActor)
                {
                    return FindIndependentTitleCultureConversion(pActor, out _, out _);
                },
                action = delegate (Person pActor)
                {
                    if (!FindIndependentTitleCultureConversion(pActor, out KingdomTitle title,
                            out string candidate)) return false;
                    return CultureService.ConvertTitleCulture(title, candidate);
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empirecraft_restore_native_culture",
                path_icon = "ui/icons/plots/plot_empirecraft_restore_native_culture",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    return FindCultureRestorationCity(pActor, out _, out _);
                },
                check_should_continue = delegate (Person pActor)
                {
                    return FindCultureRestorationCity(pActor, out _, out _);
                },
                action = delegate (Person pActor)
                {
                    if (!FindCultureRestorationCity(pActor, out City city, out _)) return false;
                    return CultureService.RestoreCityCulture(city, pActor.Realm);
                }
            });
            // 通路之约(见 RoadTreatySystem)：与有过友好时期的邻国议定修通两国之间的道路，造价各出一半
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empirecraft_road_treaty",
                path_icon = "ui/icons/plots/plot_empirecraft_road_treaty",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    return pActor.IsRuler() && RoadTreatySystem.Partner(pActor.Realm) != null;
                },
                check_should_continue = delegate (Person pActor)
                {
                    return pActor.IsRuler() && RoadTreatySystem.Partner(pActor.Realm) != null;
                },
                action = delegate (Person pActor)
                {
                    return pActor.IsRuler() && RoadTreatySystem.Sign(pActor.Realm);
                }
            });
            // 行政区（道/军/都护府，见 KingdomExtension.GetAdministrativeTitle）长官
            // 无法走"文治"派系的法理文化转化决议，但如果"文化同化"决议已经指定本行政区
            // 承担同化职责（cultural_assimilation_duty），长官可以自行发起这条决议。
            // 任务目标文化在皇帝发布时固定；每座城市每年最多推进一次，达到人口优势
            // 门槛后停止行政推动并进入自然巩固，最终由城市主流文化 Plot 正式承认。
            // 原本这里专指都护府（culture_explicit 判定），现已合并为通用的
            // "同化职责"逻辑，覆盖所有行政区类型，见 CultureService.
            // FindCulturalAssimilationDutyCandidate/ApplyCulturalAssimilationDuty。
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empirecraft_cultural_assimilation_duty",
                path_icon = "ui/icons/plots/plot_empirecraft_cultural_assimilation_duty",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_leader = true,
                check_is_possible = delegate (Person pActor)
                {
                    City city = pActor?.City;
                    if (city == null || city.IsGone()) return false;
                    Person cityLeader = city.Leader ?? city.Realm?.Ruler;
                    if (cityLeader != pActor) return false;
                    if (pActor.Record == null || pActor.Renown < CultureService.CulturalAssimilationDutyInfluenceCost) return false;
                    return CultureService.FindCulturalAssimilationDutyCandidate(city, out _);
                },
                check_should_continue = delegate (Person pActor)
                {
                    City city = pActor?.City;
                    if (city == null || city.IsGone()) return false;
                    Person cityLeader = city.Leader ?? city.Realm?.Ruler;
                    if (cityLeader != pActor) return false;
                    if (pActor.Record == null || pActor.Renown < CultureService.CulturalAssimilationDutyInfluenceCost) return false;
                    return CultureService.FindCulturalAssimilationDutyCandidate(city, out _);
                },
                action = delegate (Person pActor)
                {
                    City city = pActor?.City;
                    if (city == null || city.IsGone()) return false;
                    Person cityLeader = city.Leader ?? city.Realm?.Ruler;
                    if (cityLeader != pActor) return false;
                    if (pActor.Record == null || pActor.Renown < CultureService.CulturalAssimilationDutyInfluenceCost) return false;
                    if (!CultureService.FindCulturalAssimilationDutyCandidate(city, out string candidate)) return false;
                    if (!CultureService.ApplyCulturalAssimilationDuty(city, candidate)) return false;
                    pActor.Renown -= CultureService.CulturalAssimilationDutyInfluenceCost;
                    TranslateHelper.LogCulturalAssimilationDuty(city, candidate, CultureService.CulturalAssimilationDutyInfluenceCost);
                    return true;
                }
            });
            // 政体归化：政体类型（游牧/华夏等）目前只在建国时由开国者的文化决定一次，
            // 不会随着"文化同化"决议自动更新——那条决议只推高城市文化占比。
            // 这里补上后续的手动转变路径：当一个帝国成员国的王城和至少 2/3 的城市
            // 主流文化都已经变成帝国官方文化后，该国国王可以发起这条决议，
            // 正式把国家政体（以及 realm_culture）转变为帝国官方文化对应的政体。
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empirecraft_kingdom_regime_conversion",
                path_icon = "ui/icons/plots/plot_empirecraft_kingdom_regime_conversion",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    if (!pActor.IsRuler()) return false;
                    Realm kingdom = pActor.Realm;
                    if (kingdom == null || kingdom.IsGone()) return false;
                    return CultureService.FindRegimeConversionCandidate(kingdom, out _, out _);
                },
                check_should_continue = delegate (Person pActor)
                {
                    if (!pActor.IsRuler()) return false;
                    Realm kingdom = pActor.Realm;
                    if (kingdom == null || kingdom.IsGone()) return false;
                    return CultureService.FindRegimeConversionCandidate(kingdom, out _, out _);
                },
                action = delegate (Person pActor)
                {
                    if (!pActor.IsRuler()) return false;
                    Realm kingdom = pActor.Realm;
                    if (kingdom == null || kingdom.IsGone()) return false;
                    if (!CultureService.FindRegimeConversionCandidate(kingdom, out string culture, out RegimeType regimeType)) return false;
                    return CultureService.ConvertKingdomRegime(kingdom, culture, regimeType);
                }
            });
            // 易俗改制(如北魏孝文帝改革)：君主改从治下多数百姓之俗，王族改姓，政体随之更易(见 CultureReformSystem)
            AssetManager.plots_library.add(new PlotKind
            {
                Id = CultureReformSystem.Plot,
                path_icon = "ui/icons/plots/plot_" + CultureReformSystem.Plot,
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 30f,
                can_be_done_by_king = true,
                check_is_possible = CultureReformSystem.CanReform,
                check_can_be_forced = CultureReformSystem.CanReform,
                check_should_continue = CultureReformSystem.CanReform,
                action = CultureReformSystem.Enact
            });
            // 改建城池：文化变了，城市照旧按原风格建；君主下令出钱，各城才按现文化风格逐年拆旧建新(见 CityStyleSystem)
            AssetManager.plots_library.add(new PlotKind
            {
                Id = CityStyleSystem.Plot,
                path_icon = "ui/icons/plots/plot_" + CityStyleSystem.Plot,
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 30f,
                can_be_done_by_king = true,
                check_is_possible = CityStyleSystem.CanRebuild,
                check_can_be_forced = CityStyleSystem.CanRebuild,
                check_should_continue = CityStyleSystem.CanRebuild,
                action = CityStyleSystem.Enact
            });
            // 开发边地(秦戍五岭、汉魏屯田)：国君对一片本国前沿的雨林、大泽下诏，每年出钱，这一片开发加快(见 WildernessDevelopmentSystem)；
            // AI 在国库宽裕、前沿的城人多地少、科技够时下诏，玩家可从原版谋划强行发起(只看有没有够格的前沿、国库够不够一年)
            AssetManager.plots_library.add(new PlotKind
            {
                Id = WildernessDevelopmentSystem.Plot,
                path_icon = "ui/icons/plots/plot_" + WildernessDevelopmentSystem.Plot,
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 20f,
                can_be_done_by_king = true,
                check_is_possible = WildernessDevelopmentSystem.WantsDecree,
                check_can_be_forced = WildernessDevelopmentSystem.CanDecree,
                check_should_continue = WildernessDevelopmentSystem.CanDecree,
                action = WildernessDevelopmentSystem.Decree
            });
            // 成员向中央求援、驰援邻近成员(见 MemberWarAid)：中央与别的成员不再自动插手成员的仗
            AssetManager.plots_library.add(new PlotKind
            {
                Id = MemberWarAid.RequestPlot,
                path_icon = "ui/icons/plots/plot_" + MemberWarAid.RequestPlot,
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_target_war = true,
                try_to_start_advanced = MemberWarAid.StartRequest,
                check_is_possible = actor => actor?.kingdom?.king == actor && MemberWarAid.FindRequestWar(actor.kingdom) != null,
                check_can_be_forced = actor => actor?.kingdom?.king == actor && MemberWarAid.FindRequestWar(actor.kingdom) != null,
                check_should_continue = MemberWarAid.RequestContinues,
                action = MemberWarAid.AnswerRequest
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = MemberWarAid.NeighbourPlot,
                path_icon = "ui/icons/plots/plot_" + MemberWarAid.NeighbourPlot,
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                progress_needed = 20f,
                can_be_done_by_king = true,
                check_target_war = true,
                try_to_start_advanced = MemberWarAid.StartNeighbour,
                check_is_possible = actor => actor?.kingdom?.king == actor && MemberWarAid.FindNeighbourWar(actor.kingdom, out _, out _),
                check_can_be_forced = actor => actor?.kingdom?.king == actor && MemberWarAid.FindNeighbourWar(actor.kingdom, out _, out _),
                check_should_continue = MemberWarAid.NeighbourContinues,
                action = MemberWarAid.AidNeighbour
            });
            // 就地征粮：战时统帅的决议(见 LogisticsSystem)，由后勤月结在缺粮或大军深入敌境时发起
            AssetManager.plots_library.add(new PlotKind
            {
                Id = LogisticsSystem.ForagePlot,
                path_icon = "ui/icons/plots/plot_" + LogisticsSystem.ForagePlot,
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                progress_needed = 10f,
                can_be_done_by_king = true,
                can_be_done_by_leader = true,
                can_be_done_by_clan_member = true,
                check_is_possible = actor => LogisticsSystem.CanForage(actor, needy: true),
                check_can_be_forced = actor => LogisticsSystem.CanForage(actor, needy: false),
                check_should_continue = actor => LogisticsSystem.CanForage(actor, needy: false),
                action = LogisticsSystem.Forage
            });
            // 发兵勤王：中央与外敌交战吃紧时，成员发兵赴援(见 MemberWarAid、WarScopeSystem)
            AssetManager.plots_library.add(new PlotKind
            {
                Id = MemberWarAid.CenterPlot,
                path_icon = "ui/icons/plots/plot_" + MemberWarAid.CenterPlot,
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                progress_needed = 20f,
                can_be_done_by_king = true,
                check_target_war = true,
                try_to_start_advanced = MemberWarAid.StartCenter,
                check_is_possible = actor => actor?.kingdom?.king == actor && MemberWarAid.FindCenterWar(actor.kingdom) != null,
                check_can_be_forced = actor => actor?.kingdom?.king == actor && MemberWarAid.FindCenterWar(actor.kingdom, needy: false) != null,
                check_should_continue = MemberWarAid.CenterContinues,
                action = MemberWarAid.AidCenter
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_expose_crime",
                path_icon = "ui/icons/plots/plot_kingdom_expose_crime",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 5,
                min_renown_actor = 200,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = actor => FindCrimeExposureTarget(actor) != null,
                check_can_be_forced = actor => FindCrimeExposureTarget(actor) != null,
                check_should_continue = actor => FindCrimeExposureTarget(actor) != null,
                action = delegate (Person pActor)
                {
                    Realm kingdom = pActor?.Realm;
                    Empire empire = kingdom?.GetEmpire();
                    if (pActor == null || kingdom == null || empire == null || !pActor.HasFaction()) return false;
                    Realm target = FindCrimeExposureTarget(pActor);
                    if (target == null)
                    {
                        return false;
                    }
                    Person targetKing = target.Ruler;
                    pActor.Renown -= targetKing.Renown / 2;
                    targetKing.AddTyrantValue(30);
                    if (targetKing.GetViolateValue() >= 100)
                    {
                        var potentialCrimes = new List<LawType>()
                        {
                            LawType.叛国,
                            LawType.伪造货币,
                            LawType.滥用职权,
                            LawType.私通敌国,
                            LawType.玩忽职守,
                            LawType.走私
                        };
                        var applicableCrimes = potentialCrimes.FindAll(c => target.HasLaw(c));
                        if (applicableCrimes.Count == 0) return false;
                        var crime = applicableCrimes.GetRandom();
                        if (!targetKing.TryTriggerProbabilisticLaw(crime, 1f, _ => { })) return false;
                        bool crimeConfirmed = false;
                        double rebellingPossibility = 0.2f;
                        if (!target.isOpinionTowardsKingdomGood(empire.CoreKingdom))
                        {
                            rebellingPossibility = Math.Min(1d, target.Warriors() /
                                (double)Math.Max(1, empire.countWarriors())) * 0.5d +
                                Math.Max(0d, 100d - empire.Legitimacy) / 200d;
                        }
                        Random rand = new Random();
                        rebellingPossibility *= RebellionSystem.ChanceFactor(target);
                        if (RebellionSystem.CanAttempt(target) && CityStabilitySystem.CanRise(target.Capital) &&
                            rand.NextDouble() < rebellingPossibility)
                        {
                            RebellionCauseData cause = RebellionSystem.Capture(target, target.Capital,
                                "rebellion_reason_crime", crime.ToString());
                            var war = EmpireWorld.Current.LiveWars.newWar(target, kingdom, WarKinds.Normal);
                            if (war == null) return false;
                            war.SetEmpireWarType(EmpireWarType.地方叛乱, pre: kingdom.Name, nanoObject:empire, belongingFaction:target.Ruler?.GetFaction());
                            empire.leave(target, true, true);
                            target.StartLocalRebelling(EmpireWarType.地方叛乱);
                            RebellionSystem.Record(target, war, cause);
                            // 郡县多不许养兵，起兵时就地武装义军(见 RebellionStartupService)
                            RebellionStartupService.RaiseUprisingMilitia(target, 0.4f);
                        }
                        else
                        {
                            // 只立案不施刑：定罪(夷三族)还是开释由下面决定(以前先按律施了刑，开释也已受刑)
                            var context = EmpireLawSystem.PrepareEnforcement(targetKing, crime, kingdom);
                            if (context == null) return false;
                            //当影响力小于两百, 或者脱罪次数达到2的上限则进行惩罚
                            if (targetKing.Renown <= 200 && targetKing.EscapeFromPunishment(true))
                            {
                                var punishments = new List<PunishmentLevel>()
                                {
                                    PunishmentLevel.剥夺官职, PunishmentLevel.剥夺爵位, PunishmentLevel.夷三族
                                };
                                context.AppliedPunishments = punishments;
                                foreach (var p in punishments)
                                {
                                    EmpireLawSystem.ApplyPunishment(context, p);
                                }
                            }
                            else
                            {
                                targetKing.AddRenown(-200);
                                EmpireLawSystem.ApplyPunishment(context, PunishmentLevel.无罪);
                                targetKing.AddTyrantValue(-30);
                                context.AppliedPunishments = new List<PunishmentLevel>() {PunishmentLevel.无罪};
                                targetKing.EscapeFromPunishment();
                            }
                            TranslateHelper.LogLawEnforcement(context);
                            crimeConfirmed = context.AppliedPunishments.Any(p => p != PunishmentLevel.无罪);
                        }
                        TranslateHelper.LogExposeCrime(kingdom, target, crime.ToString());
                        // Attach a confirmed offence to its ruler, not to the accuser; acquittal is not a conviction.
                        if (crimeConfirmed && target.Ruler == targetKing && target.GetEmpire() == empire)
                            target.SetMainCrime(crime);
                        else if (!crimeConfirmed && target.Ruler == targetKing && target.HasMainCrime() &&
                                 target.GetMainCrime() == crime)
                            target.RemoveMainCrime();
                    }
                    else
                    {
                        TranslateHelper.LogExposeCrime(kingdom, target, "");
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_start_religion_war",
                path_icon = "ui/icons/plots/plot_kingdom_start_religion_war",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (kingdom.GetKingdomType() != KingdomType.Feudalism_papal_state) return false;
                    if (regime.religion_point<1000) return false;
                    if (kingdom.HasEnemies()) return false;
                    var warsList2 = KingdomExtension.GetWarsCached(kingdom, false);
                    for (int wi = 0; wi < warsList2.Count; wi++)
                    {
                        if (warsList2[wi].GetEmpireWarType() == EmpireWarType.神圣) return false;
                    }
                    if (regime.type != RegimeType.Feudalism || regime.GetReligionLevel() != ReligionLevel.High)
                        return false;
                    if (kingdom.HasReligion() && kingdom.Religion.GetCity() == kingdom.Capital && !ReligionSphere.IsBranch(kingdom.Religion))
                    {
                        Religion religion = kingdom.Religion;
                        
                        // 圣战对象要与本国(或本国所在帝国)接壤：以前只要与任一同教国家接壤就行，会隔着半张地图开战
                        if (EmpireWorld.Current.LiveRealms.ToList().Any(k =>
                                !religion.Realms.Contains(k) && HolyWarNeighbour(kingdom, k))) return true;
                    }
                    return false;
                },
                action = delegate (Person pActor)
                {                    
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (regime.type != RegimeType.Feudalism || regime.GetReligionLevel() != ReligionLevel.High)
                        return false;
                    if (kingdom.HasReligion() && kingdom.Religion.GetCity() == kingdom.Capital && !ReligionSphere.IsBranch(kingdom.Religion))
                    {
                        Religion religion = kingdom.Religion;

                        var target = EmpireWorld.Current.LiveRealms.ToList().Find(k =>
                            !religion.Realms.Contains(k) && HolyWarNeighbour(kingdom, k));
                        if (target == null) return false;
                        var war = EmpireWorld.Current.LiveWars.newWar(kingdom, target, WarKinds.Normal);
                        if (war == null) return false;
                        war.SetEmpireWarType(EmpireWarType.神圣, pre:religion.Name);
                        regime.religion_point -= 1000;
                        return true;
                    }
                    return false;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "new_empire_royal",
                path_icon = "ui/icons/plots/plot_new_empire_royal",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 5,
                money_cost = 30,
                progress_needed = 60f,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (!pActor.HasSpecificClan()) return false;
                    if (!kingdom.IsEmpire()) return false;
                    if (kingdom.HasEnemies()) return false;
                    if (ConstitutionalSuccessionSystem.ThroneBound(kingdom.GetEmpire())) return false;
                    if (!kingdom.GetEmpire().IsRoyalBeenChanged()) return false;
                    if (GameClock.YearsSince(kingdom.GetEmpire().data.original_royal_been_changed_timestamp)<=5) return false;
                    return true;
                },
                action = delegate (Person pActor)
                {
                    if (ConstitutionalSuccessionSystem.ThroneBound(pActor.Realm?.GetEmpire())) return false;
                    pActor.CheckSpecificClan();
                    Realm kingdom = pActor.Realm;
                    Empire empire = kingdom.GetEmpire();
                    empire.data.original_royal_been_changed = false;
                    empire.data.empire_specific_clan = pActor.GetSpecificClan().id;
                    string culture = kingdom.GetEmpireCraftCulture();
                    if (!string.IsNullOrWhiteSpace(culture))
                    {
                        // 华夏一系(华夏、中国、山海，同一天下标识)
                        if (CultureService.SameKin(culture, "Huaxia"))
                        {
                            // 帝国内改朝换代：用新皇族之主的封国名，没有封国用美号(见 RealmNamingSystem.Founding)；天下之名关着时照旧随机起名
                            if (!RealmNamingSystem.NameUsurper(empire, pActor))
                            {
                                string new_name = pActor.generateName(EntityKind.Realm, IdGenerator.NextId());
                                LogService.LogInfo($"New Empire Name：{new_name}，Original Empire Name：{empire.GetEmpireName()}");
                                string[] nameParts = new_name.SplitNameParts();
                                empire.SetEmpireName(nameParts.Length > 0 ? nameParts[0] : new_name);
                            }
                            LogService.LogInfo($"Empire Name has been changed to：{empire.GetEmpireName()}");
                            empire.data.dynasty_founder_actor_id = pActor.Key;
                            empire.data.currentHistory.is_first = true;
                            empire.data.currentHistory.empire_name = empire.GetEmpireName();
                        }
                    }
                    pActor.GetSpecificClan()?.RecordHistoryEmpire(empire, empire.CoreKingdom.Capital);
                    empire.data.created_time = GameClock.Now;
                    empire.CoreKingdom.SetRuler(pActor);
                    if (empire.HasYearName())
                    {
                        empire.RecordHistory(EmpireHistoryType.new_empire_history, new Dictionary<string, string>()
                        {
                            ["actor"] = pActor.FullName(),
                            ["place"] = empire.CoreKingdom.Capital.GetCityName(),
                            ["name"] = empire.GetEmpireName(),
                        });
                    }
                    else
                    {
                        empire.RecordHistory(EmpireHistoryType.new_empire_history_west, new Dictionary<string, string>()
                        {
                            ["actor"] = pActor.FullName(),
                            ["place"] = empire.CoreKingdom.Capital.GetCityName(),
                            ["name"] = empire.GetEmpireName(),
                        });
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "emperor_year_name",
                path_icon = "ui/icons/plots/plot_emperor_year_name",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (!kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (!kingdom.GetEmpire().IsAllowToMakeYearName()) return false;
                    // 已有年号的：距上次定年号满五年才能再改(见 Empire.CanChangeYearName)
                    if (!kingdom.GetEmpire().CanChangeYearName()) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    Empire empire = kingdom?.GetEmpire();
                    if (empire == null) return false;

                    if (!empire.CanChangeYearName()) return false;
                    empire.ChangeYearName();
                    TranslateHelper.LogEmperorNewYearName(pActor, empire);
                    EmpireLawSystem.OnEraNameChanged(empire); // 改元大赦
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_allow_army",
                path_icon = "ui/icons/plots/plot_kingdom_allow_army",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    if (empire.Legitimacy > 60) return false;
                    if (regime == null) return false;
                    if (regime.IsAllowArmy()) return false;
                    if (kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom)) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    kingdom.FinishedSelfPlot();
                    Regime regime = kingdom.GetRegime();
                    regime.SetAllowArmy(true);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_allow_diplomacy",
                path_icon = "ui/icons/plots/plot_kingdom_allow_diplomacy",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    if (empire.Legitimacy > 60) return false;
                    if (regime == null) return false;
                    if (!regime.IsAllowArmy()) return false;
                    if (regime.IsAllowDiplomacy()) return false;
                    // 律令制看去掉名分的向背(见 RebellionSystem.SecessionAllegiance)：同帝国、受封两项名分 +200 压着，
                    // 以前藩镇这一步正统跌到 30 以下才迈得出去(用户 2026-10-21 选"华夏有真乱世")
                    if (empire.CoreKingdom?.GetRegime()?.type == RegimeType.LvLing
                            ? RebellionSystem.SecessionAllegiance(kingdom, empire) >= 0
                            : kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom)) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    kingdom.FinishedSelfPlot();
                    Regime regime = kingdom.GetRegime();
                    regime.SetAllowDiplomacy(true);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_allow_succession",
                path_icon = "ui/icons/plots/plot_kingdom_allow_succession",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (!regime.IsAllowArmy()) return false;
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (regime.GetLeaderSelectMethod() == LeaderSelectMethod.Succession) return false;
                    if (kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom)) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    kingdom.FinishedSelfPlot();
                    Regime regime = kingdom.GetRegime();
                    regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_allow_self_army",
                path_icon = "ui/icons/plots/plot_kingdom_allow_self_army",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (!regime.IsAllowArmy()) return false;
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession) return false;
                    if (!regime.IsAllowSupportCenterArmy()) return false;
                    if (kingdom.isOpinionTowardsKingdomGood(empire.CoreKingdom)) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    kingdom.FinishedSelfPlot();
                    Regime regime = kingdom.GetRegime();
                    regime.SetAllowSupportCenterArmy(false);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_allow_independent",
                path_icon = "ui/icons/plots/plot_kingdom_allow_independent",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (kingdom.IsCountingSelfPlot()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    Empire empire = kingdom.GetEmpire();
                    Regime regime = kingdom.GetRegime();
                    // 正统门槛 40；汗国连吃败仗时抬高(兵败离心，见 NomadCycleSystem.SecessionGate)
                    if (empire.Legitimacy > NomadCycleSystem.SecessionGate(kingdom) || !RebellionSystem.CanAttempt(kingdom) ||
                        !CityStabilitySystem.CanRise(kingdom.Capital)) return false;
                    // 外藩有姻亲、或八旗战力尚在，不自立(见 BannerSystem.Frontier)
                    if (BannerSystem.LeagueRestrains(kingdom)) return false;
                    if (regime == null) return false;
                    // 哪些政体、哪类成员能拥兵自立读政体配置(members_can_secede 等，见 Scripts/Regimes/Configs/README.md)：
                    // 封建、阿拉伯的成员；律令制的藩镇(世袭节度使、都护)；游牧的兀鲁思(大汗弱时自立为汗国，各有自己的军队，
                    // 不受"支援中央军"所限，见 EnfeoffmentHelper.UlusPartition)
                    Regime template = RegimeManager.GetTemplate(regime.type);
                    if (template?.members_can_secede != true) return false;
                    if (template.secede_member_types?.Count > 0 &&
                        !template.secede_member_types.Contains(kingdom.GetKingdomType())) return false;
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (!regime.IsAllowArmy()) return false;
                    if (regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession) return false;
                    if (regime.IsAllowSupportCenterArmy() && !template.secede_ignores_central_army) return false;
                    // 向背：去掉同帝国、受封两项名分好感，正统按离自立门槛远近算，明显离心才自立(见 RebellionSystem.SecessionAllegiance)；
                    // 以前看整个好感，名分两项 +200 压着，正统掉到个位数也自立不了
                    if (RebellionSystem.SecessionAllegiance(kingdom, empire) >= RebellionSystem.SecessionThresholdFor(kingdom, empire)) return false;
                    return true;
                },
                check_should_continue = actor => actor?.isKing() == true &&
                    actor.kingdom.GetEmpire() is Empire current && current.Legitimacy <= NomadCycleSystem.SecessionGate(actor.kingdom) &&
                    RebellionSystem.CanAttempt(actor.kingdom) && CityStabilitySystem.CanRise(actor.kingdom.capital),
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    Empire empire = kingdom.GetEmpire();
                    if (empire == null || empire.IsGone() || empire.Legitimacy > NomadCycleSystem.SecessionGate(kingdom) ||
                        !RebellionSystem.CanAttempt(kingdom) || !CityStabilitySystem.CanRise(kingdom.Capital)) return false;
                    // 退出帝国、与中央开战(与汗位更替之乱共用，见 NomadCycleSystem.DeclareIndependence)
                    return NomadCycleSystem.DeclareIndependence(kingdom, empire, "rebellion_reason_secession");
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empire_take_back_title",
                path_icon = "ui/icons/plots/plot_empire_take_back_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!kingdom.IsEmpire()) return false;
                    var empire = kingdom.GetEmpire();
                    if (empire==null) return false;
                    var k = FindTitleReclamationTarget(empire);
                    return k != null;
                },
                check_should_continue = pActor => pActor?.isKing() == true && pActor.kingdom.IsEmpire() &&
                    FindTitleReclamationTarget(pActor.kingdom.GetEmpire()) != null,
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    if (!kingdom.IsEmpire()) return false;
                    var empire = kingdom.GetEmpire();
                    if (empire==null) return false;
                    var k = FindTitleReclamationTarget(empire);
                    if (k == null) return false;
                    var crime = k.GetMainCrime().ToString();
                    Person formerHolder = k.Ruler;
                    var titles = formerHolder.GetOwnedTitle();
                    var kingdomTitles = titles?.Select(t => ModClass.KINGDOM_TITLE_MANAGER.Get(t))?.ToList()
                        .FindAll(kt => kt != null && !kt.IsGone() && kt.owner == formerHolder);
                    if (kingdomTitles == null || kingdomTitles.Count == 0) return false;
                    string titleText = string.Join(",", kingdomTitles.Select(kt=>kt.Name));
                    if (empire.ReclaimTitles(k, kingdomTitles) <= 0) return false;
                    TranslateHelper.LogEmpireTakeBackTitle(formerHolder, titleText, crime);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "emperor_posthumous_name",
                path_icon = "ui/icons/plots/plot_emperor_posthumous_name",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (!kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (!kingdom.GetEmpire().IsNeedToSetPosthumous()) return false;
                    return true;
                },
                check_should_continue = delegate (Person pActor)
                {
                    if (!pActor.IsRuler()) return false;
                    if (!pActor.HasRealm()) return false;
                    Realm kingdom = pActor.Realm;
                    if (!kingdom.Alive) return false;
                    if (!kingdom.IsEmpire()) return false;
                    if (!kingdom.IsInEmpire()) return false;
                    if (kingdom.GetEmpire().Emperor == null) return false;
                    // 剧情进行中改制为共和或现代政体：不再追封
                    if (!kingdom.GetEmpire().AllowsPosthumousNames()) return false;
                    return true;
                },
                action = delegate(Person pActor)
                {
                    // 谥法：按先帝生平定庙号谥号(见 Empire.ConferPosthumousNames、PosthumousNameGenerator)
                    pActor.Realm.GetEmpire().ConferPosthumousNames(pActor, announceAll: true);
                    return true;
                }
            });
            // 增广尊谥(皇帝个人的决议)：本朝此后有庙号的先帝都得明式长谥，并为已有庙号的列祖列宗追加尊谥
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "emperor_extend_posthumous",
                path_icon = "ui/icons/plots/plot_emperor_extend_posthumous",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 30f,
                can_be_done_by_king = true,
                check_is_possible = PosthumousNameGenerator.CanExtend,
                check_should_continue = PosthumousNameGenerator.CanExtend,
                action = delegate (Person pActor)
                {
                    return PosthumousNameGenerator.Extend(pActor.Realm?.GetEmpire(), pActor);
                }
            });
            // 谥法从简(皇帝个人的决议)：此后先帝只用简谥；与增广尊谥二选一，一个帝国只定一次
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "emperor_simplify_posthumous",
                path_icon = "ui/icons/plots/plot_emperor_simplify_posthumous",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 30f,
                can_be_done_by_king = true,
                check_is_possible = PosthumousNameGenerator.CanSimplify,
                check_should_continue = PosthumousNameGenerator.CanSimplify,
                action = delegate (Person pActor)
                {
                    return PosthumousNameGenerator.Simplify(pActor.Realm?.GetEmpire(), pActor);
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "king_acquire_title",
                path_icon = "ui/icons/plots/plot_king_acquire_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (!pActor.CanAcquireTitle()) return false;
                    var warsList3 = kingdom.GetWarsCached(false);
                    if (warsList3.Count > 0) return false;
                    if (!kingdom.CanPursueDeJureTitle()) return false;
                    if (kingdom.IsEmpire()) return false;
                    return true;
                },
                try_to_start_advanced = delegate(Person pActor, PlotKind pPlotAsset, bool pForced)
                {
                    Realm kingdom = pActor.Realm;
                    List<KingdomTitle> titles = pActor.getAcquireTitle();
                    if (!titles.Any()) return false;
                    foreach (KingdomTitle title in titles)
                    {
                        if (title.IsGone()) continue;
                        Realm targetKingdom = title.FindRealmTitleHolder();
                        if (!kingdom.CanDeclareDeJureTitleWar(targetKingdom, title)) continue;
                        if (kingdom.isOpinionTowardsKingdomGood(targetKingdom)) continue;
                        foreach (Plot plot3 in EmpireWorld.Current.Plots)
                        {
                            if (plot3.IsActive() && plot3.isSameType(pPlotAsset))
                            {
                                pActor.setPlot(plot3);
                                plot3.TargetRealm = targetKingdom;
                                plot3.setName($"{kingdom.Name}试图索取{targetKingdom.Name}的{title.Name}法理");
                                return true;
                            }
                        }
                        var nPlot = EmpireWorld.Current.Plots.newPlot(pActor, pPlotAsset, pForced);
                        nPlot.target_kingdom = targetKingdom;
                        nPlot.setName($"{kingdom.Name}试图索取{targetKingdom.Name}的{title.Name}法理");
                        return true;
                    }
                    return false;
                },
                check_should_continue = delegate (Person pActor) {
                    if (!pActor.hasPlot()) return false;
                    if (!pActor.HasRealm()) return false;
                    if (pActor.Realm.HasTakenAlliance()) return false;
                    var plot = pActor.plot;
                    var targetKingdom = plot.target_kingdom;
                    if (targetKingdom == null || targetKingdom.IsGone()) return false;
                    if (!targetKingdom.HasRuler()) return false;
                    return pActor.getAcquireTitle().Any(title =>
                        pActor.Realm.CanDeclareDeJureTitleWar(targetKingdom, title));
                },
                action = delegate(Person pActor)
                {
                    if (!pActor.hasPlot()) return false;
                    if (!pActor.HasRealm()) return false;
                    var kingdom = pActor.Realm;
                    if (kingdom.HasTakenAlliance()) return false;
                    var plot = pActor.plot;
                    var targetKingdom = plot.target_kingdom;
                    List<KingdomTitle> titles = pActor.getAcquireTitle();
                    var finalTitle = titles.FirstOrDefault(title => !title.IsGone() &&
                        kingdom.CanDeclareDeJureTitleWar(targetKingdom, title));
                    if (finalTitle == null || CorpsSystem.CannotFundWar(kingdom)) return false;
                    War war = EmpireWorld.Current.StartWar(kingdom, targetKingdom, WarKinds.Normal);
                    if (war == null) return false;
                    war.SetEmpireWarType(EmpireWarType.索取法理, nanoObject:finalTitle);
                    TranslateHelper.LogKingdomAcquireTitle(kingdom, targetKingdom, finalTitle);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_destroy_title",
                path_icon = "ui/icons/plots/plot_kingdom_destroy_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (kingdom.IsDeJureTitleAcquisitionBlocked()) return false;
                    if (ModClass.KINGDOM_TITLE_FREEZE) return false;
                    if (!kingdom.HasMainTitle()) return false;
                    if (!pActor.titleCanBeDestroy().Any()) return false;
                    return true;
                },

                check_should_continue = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (!kingdom.Alive) return false;
                    if (!kingdom.HasMainTitle()) return false;
                    // 谋划半途开了冻结法理
                    if (ModClass.KINGDOM_TITLE_FREEZE) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    List<KingdomTitle> titles = pActor.titleCanBeDestroy();
                    foreach(KingdomTitle title in titles)
                    {

                        ModClass.KINGDOM_TITLE_MANAGER.dissolveTitle(title);
                        pActor.removeTitle(title);
                        if (kingdom.GetMainTitle()==title)
                        {
                            kingdom.RemoveMainTitle();
                        }
                        TranslateHelper.LogDestroyTitle(kingdom, title);
                    }
                    if (kingdom.HasMainTitle())
                    {
                        KingdomTitle title = kingdom.GetMainTitle();
                        foreach(City city in kingdom.Cities.ToList())
                        {
                            if (city.GetTitle()!=kingdom.GetMainTitle())
                            {
                                if (!KingdomTitleManager.HasRoomFor(title)) break;
                                title.addCity(city);
                                TranslateHelper.LogCityAddToTitle(city, title);
                            }
                        }
                    }else
                    {
                        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(kingdom.Capital);
                        if (title == null) return false;
                        kingdom.SetMainTitle(title);
                        foreach (City city in kingdom.Cities.ToList())
                        {
                            if (city!=kingdom.Capital)
                            {
                                if (!KingdomTitleManager.HasRoomFor(title)) break;
                                title.addCity(city);
                                TranslateHelper.LogCityAddToTitle(city, title);
                            }
                        }
                    }
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "emperor_reorganize_administration",
                path_icon = "ui/icons/plots/plot_empire_take_back_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = AdministrationReorganizationSystem.ShouldPropose,
                check_can_be_forced = AdministrationReorganizationSystem.CanPropose,
                check_should_continue = AdministrationReorganizationSystem.CanPropose,
                action = AdministrationReorganizationSystem.Begin
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_get_title",
                path_icon = "ui/icons/plots/plot_kingdom_get_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = CanRunKingdomGetTitlePlot,
                check_can_be_forced = CanRunKingdomGetTitlePlot,
                check_should_continue = CanContinueKingdomGetTitlePlot,
                action = delegate(Person pActor) 
                {
                    if (!CanContinueKingdomGetTitlePlot(pActor)) return false;
                    Realm kingdom = pActor.Realm;
                    List<KingdomTitle> titles = pActor.takeTitle();
                    foreach(KingdomTitle title in titles)
                    {
                        if (!title.IsGone())
                        {
                            TranslateHelper.LogKingTakeTitle(kingdom, title);
                        }
                    }
                    kingdom.ReconcileMainTitle(titles);
                    kingdom.GetEmpire()?.SynchronizeLandedLegalTitles(kingdom);
                    return titles.Count > 0;
                }
            }); 
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_change_capital_title",
                path_icon = "ui/icons/plots/plot_kingdom_change_capital_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    if (pActor == null) return false;
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (!kingdom.HasMainTitle()) return false;
                    if (kingdom.GetMainTitle()==null) return false;
                    if (kingdom.GetMainTitle().title_capital==null) return false;
                    if (kingdom.Capital == null) return false;
                    if (pActor.Realm.GetMainTitle().title_capital==kingdom.Capital) return false;
                    if (!kingdom.Cities.Contains(pActor.Realm.GetMainTitle().title_capital)) return false;
                    // 还于旧都后的首都锚定：不再迁回封号首府
                    if (EmpireCraft.Core.CoreCapitalRestorationSystem.IsAnchored(kingdom)) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    if (EmpireCraft.Core.CoreCapitalRestorationSystem.IsAnchored(kingdom)) return true;
                    kingdom.SetCapital(kingdom.GetMainTitle().title_capital);
                    // 迁都封号首府：记工役，算大兴土木(见 CorveeSystem)
                    CorveeSystem.Add(kingdom, CorveeSystem.CapitalMove, true);
                    TranslateHelper.LogKingdomChangeCapitalToTitle(kingdom, kingdom.GetMainTitle());
                    var emp = kingdom.GetEmpire();
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_join_empire",
                path_icon = "ui/icons/plots/plot_kingdom_join_empire",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    if (pActor == null) return false;
                    Realm kingdom = pActor.Realm;
                    if (kingdom == null) return false;
                    if (!pActor.IsRuler()) return false;
                    if (kingdom.IsEmpire()) return false;
                    if (kingdom.IsInEmpire()) return false;
                    if (!kingdom.GetEmpiresCanBeJoined().Any()) return false;
                    return true;
                },
                check_should_continue = delegate (Person pActor)
                {
                    if (pActor == null) return false;
                    Realm kingdom = pActor.Realm;
                    if (kingdom.IsInEmpire()) return false;
                    if (!kingdom.GetEmpiresCanBeJoined().Any()) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    if (!pActor.IsRuler() || kingdom.IsInEmpire() || !RealmDiplomacySystem.CanChooseOverlord(kingdom)) return false;
                    Empire destination = kingdom.GetEmpiresCanBeJoined().FirstOrDefault();
                    if (destination == null) return false;
                    destination.join(kingdom);
                    if (kingdom.GetEmpire() != destination) return false;
                    var warsList5 = KingdomExtension.GetWarsCached(kingdom, false);
                    for (int wi = 0; wi < warsList5.Count; wi++)
                    {
                        warsList5[wi].lostWar(kingdom);
                    }
                    TranslateHelper.LogKingdomJoinEmpire(kingdom, kingdom.GetEmpire());
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_create_title",
                path_icon = "ui/icons/plots/plot_kingdom_create_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (kingdom.Capital==null) return false;
                    if (!pActor.IsRuler()) return false;
                    if (!pActor.HasRealm()) return false; 
                    if (kingdom.HasTakenAlliance()) return false;
                    if (kingdom.IsDeJureTitleAcquisitionBlocked()) return false;
                    if (kingdom.Capital.hasTitle()) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    if (kingdom.HasTakenAlliance()) return false;
                    KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(kingdom.Capital);
                    if (title == null) return false;
                    TranslateHelper.LogCreateTitle(kingdom, title);
                    title.owner = pActor;
                    pActor.AddOwnedTitle(title);
                    KingdomTitleManager.AddUntitledCities(title, kingdom);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "kingdom_add_city_into_title",
                path_icon = "ui/icons/plots/plot_kingdom_add_city_into_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 15f,
                can_be_done_by_king = true,
                check_is_possible = delegate (Person pActor)
                {
                    Realm kingdom = pActor.Realm;
                    if (kingdom.Capital==null) return false;
                    if (!pActor.IsRuler()) return false;
                    if (!pActor.HasRealm()) return false; 
                    if (!kingdom.HasMainTitle()) return false;
                    if (kingdom.Cities.All(c=>c.hasTitle())) return false;
                    if (!KingdomTitleManager.HasRoomFor(kingdom.GetMainTitle())) return false;
                    return true;
                },
                action = delegate(Person pActor) 
                {
                    Realm kingdom = pActor.Realm;
                    var mainTitle = kingdom.GetMainTitle();
                    if (mainTitle == null) return false;
                    KingdomTitleManager.AddUntitledCities(mainTitle, kingdom);
                    return true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empress_dowager_install_son",
                path_icon = "ui/icons/plots/plot_empress_dowager_install_son",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 30f,
                can_be_done_by_leader = true,
                can_be_done_by_king = true,
                can_be_done_by_clan_member = true,
                requires_diplomacy = true,
                try_to_start_advanced = TryStartSimplePlot,
                check_is_possible = delegate (Person pActor)
                {
                    return pActor?.Realm?.GetEmpire()?.CanEmpressDowagerInstallSon(pActor) == true;
                },
                check_should_continue = delegate (Person pActor)
                {
                    return pActor?.Realm?.GetEmpire()?.CanEmpressDowagerInstallSon(pActor) == true;
                },
                action = delegate (Person pActor)
                {
                    return pActor?.Realm?.GetEmpire()?.CompleteEmpressDowagerInstallSon(pActor) == true;
                }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "minister_acquire_empire",
                path_icon = "ui/icons/plots/plot_minister_acquire_empire",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                money_cost = 30,
                progress_needed = 30f,
                can_be_done_by_leader = true,
                can_be_done_by_king = true,
                can_be_done_by_clan_member = true,
                requires_diplomacy = true,
                try_to_start_advanced = TryStartSimplePlot,
                check_is_possible = delegate (Person pActor)
                {
                    Empire empire = pActor?.Realm?.GetEmpire();
                    return empire?.CanPowerfulMinisterUsurp(pActor) == true;
                },
                check_should_continue = delegate (Person pActor)
                {
                    Empire empire = pActor?.Realm?.GetEmpire();
                    return empire?.CanPowerfulMinisterUsurp(pActor) == true;
                },
                action = minister_acquire_empire
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "minister_acquire_title",
                path_icon = "ui/icons/plots/plot_minister_acquire_title",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 30f,
                can_be_done_by_leader = true,
                can_be_done_by_king = true,
                can_be_done_by_clan_member = true,
                requires_diplomacy = true,
                try_to_start_advanced = TryStartSimplePlot,
                check_is_possible = delegate (Person pActor)
                {
                    Empire empire = pActor?.Realm?.GetEmpire();
                    return empire?.CanPowerfulMinisterSeekDukedom(pActor) == true;
                },
                check_should_continue = delegate (Person pActor)
                {
                    Empire empire = pActor?.Realm?.GetEmpire();
                    return empire?.CanPowerfulMinisterSeekDukedom(pActor) == true;
                },
                action = delegate (Person pActor) { return pActor?.Realm?.GetEmpire()?.TryGrantPowerfulMinisterDukedom(pActor) == true; }
            });
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "minister_receive_nine_bestowments",
                path_icon = "ui/icons/plots/plot_minister_receive_nine_bestowments",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 1,
                progress_needed = 30f,
                can_be_done_by_leader = true,
                can_be_done_by_king = true,
                can_be_done_by_clan_member = true,
                requires_diplomacy = true,
                try_to_start_advanced = TryStartSimplePlot,
                check_is_possible = delegate (Person pActor)
                {
                    Empire empire = pActor?.Realm?.GetEmpire();
                    return empire?.CanPowerfulMinisterReceiveNineBestowments(pActor) == true;
                },
                check_should_continue = delegate (Person pActor)
                {
                    Empire empire = pActor?.Realm?.GetEmpire();
                    return empire?.CanPowerfulMinisterReceiveNineBestowments(pActor) == true;
                },
                action = delegate (Person pActor)
                {
                    return pActor?.Realm?.GetEmpire()?.GrantPowerfulMinisterNineBestowments(pActor) == true;
                }
            });
            AssetManager.plots_library.list.RemoveAll(a => a.id == "rebellion");
            AssetManager.plots_library.basic_plots.RemoveAll(a=>a.id=="rebellion");
            // 字典里的旧条目也要拿掉，否则 add 会在日志里报一条"duplicate asset"错误
            AssetManager.plots_library.dict.Remove("rebellion");
            Rebellion = AssetManager.plots_library.add(new PlotKind
		    {
			    Id = "rebellion",
			    is_basic_plot = true,
			    path_icon = "plots/icons/plot_rebellion",
			    group_id = "empirecraft_diplomacy",
			    min_level = 2,
			    money_cost = 15,
			    min_stewardship = 6,
			    min_warfare = 5,
			    can_be_done_by_clan_member = true,
			    can_be_done_by_leader = true,
			    check_target_kingdom = true,
			    requires_diplomacy = true,
			    requires_rebellion = true,
                unlocked_with_achievement = false,
			    check_is_possible = delegate(Person pActor)
			    {
				    Realm kingdom = pActor.Realm;
				    if (pActor.IsRuler())
				    {
					    return false;
				    }
				    if (kingdom.CountCities() == 1&&!kingdom.IsInEmpire())
				    {
					    return false;
				    }
				    if (!kingdom.HasCapital())
				    {
					    return false;
				    }
				    if (kingdom.getAge() <= SimGlobals.m.rebellions_min_age)
				    {
					    return false;
				    }
				    if (pActor.HasTrait("pacifist"))
				    {
					    return false;
				    }
				    // 现代国家治理能力：大部分叛乱图谋起不来(见 ModernStability)
				    if (!ModernStability.PassRebellionGate(kingdom)) return false;
				    City city = pActor.City;
				    // 不在任何城里的人(流民、行军在外)起不了事
				    if (city?.Record == null || city.IsGone() || city.Realm == null) return false;
				    if (city.IsCapital()&&!city.Realm.IsInEmpire())
				    {
					    return false;
				    }

                    if (city.Realm.IsEmpire() && city.IsCapital())
                    {
                        return false;
                    }
                    CityValueSnapshot cityValue = city.GetCityStrategicValue();
                    if (city.isHappy() && (!CityValueRules.CanRebelWhileContent(cityValue) ||
                        city.getLoyalty() > CityValueRules.GetIsolationRebellionLoyaltyThreshold(cityValue)))
                    {
                        return false;
                    }
                    if (!kingdom.IsInEmpire())
                    {
                        float num = city.Warriors();
                        float num2 = kingdom.Capital.Warriors();
                        if (num < (float)SimGlobals.m.rebellions_min_warriors)
                        {
                            return false;
                        }
                        float num3 = (float)kingdom.countUnhappyCities() * SimGlobals.m.rebellions_unhappy_multiplier;
                        float num4 = num2 - num2 * num3;
                        if (!(num >= num4))
                        {
                            return false;
                        }
                    }
				    foreach (War war2 in kingdom.GetWars())
				    {
					    if (war2.GetKind() == WarKinds.Rebellion && war2.isMainDefender(kingdom))
					    {
						    return false;
					    }
				    }
				    int num5 = kingdom.CountCities();
                    if (!kingdom.IsInEmpire())
                    {
                        int maxCities = kingdom.getMaxCities();
                        if (num5 <= maxCities)
                        {
                            return false;
                        }
                    }
				    foreach (Plot plot2 in EmpireWorld.Current.Plots)
				    {
					    if (plot2.IsActive() && plot2.isSameType(Rebellion) && plot2.TargetRealm == kingdom)
					    {
						    return false;
					    }
				    }
				    return true;
			    },
			    try_to_start_advanced = delegate(Person pActor, PlotKind pPlotAsset, bool pForced)
			    {
				    Plot plot = EmpireWorld.Current.Plots.newPlot(pActor, pPlotAsset, pForced);
                    var regime = pActor.Realm.GetRegime();
                    var empire = pActor.Realm.GetEmpire();
				    plot.TargetRealm = empire!=null&&regime.type == RegimeType.LvLing?empire.CoreKingdom:pActor.Realm;
				    if (!plot.checkInitiatorAndTargets())
				    {
					    Debug.Log("tryPlotRebellion is missing start requirements");
                    }
				    return true;
			    },
			    check_other_plots = (pActor, pPlot) => (pActor.kingdom == pPlot.target_kingdom),
			    check_can_be_forced = delegate(Person pActor)
			    {
				    if (pActor.IsRuler())
				    {
					    return false;
				    }
				    Realm kingdom = pActor.Realm;
                    City city = pActor.City;
                    if (city.IsCapital()&&!city.Realm.IsInEmpire())
                    {
                        return false;
                    }

                    if (city.Realm.IsEmpire() && city.IsCapital())
                    {
                        return false;
                    }
				    return kingdom.CountCities() != 1&&!kingdom.IsInEmpire();
			    },
			    check_should_continue = delegate(Person pActor)
			    {
				    if (!pActor.HasCity())
				    {
					    return false;
				    }
				    if (!pActor.HasClan())
				    {
					    return false;
				    }
				    if (pActor.IsRuler())
				    {
					    return false;
				    }
                    City city = pActor.City;
                    if (city.IsCapital()&&!city.Realm.IsInEmpire())
                    {
                        return false;
                    }

                    if (city.Realm.IsEmpire() && city.IsCapital())
                    {
                        return false;
                    }
                    return RebellionSystem.CanContinueCityPlot(pActor, pActor.plot?.data.forced == true);
			    },
			    action = delegate(Person pActor)
			    {
                    Plot plot = pActor?.plot;
                    if (plot == null || !RebellionSystem.CanContinueCityPlot(pActor, plot.data.forced)) return false;
                    Realm origin = pActor.Realm;
                    City originCity = pActor.City;
                    RebellionCauseData cause = RebellionSystem.Capture(origin, originCity,
                        originCity.isHappy() ? "rebellion_reason_exclave" : "rebellion_reason_city_loyalty",
                        RebellionSystem.CityLoyaltyCauses(originCity));
				    bool pCheckForHappiness = !plot.data.forced;
				    DiplomacyHelpersRebellion.startRebellion(pActor, plot, pCheckForHappiness);
                    if (pActor.Realm == null || pActor.Realm == origin) return false;
                    Realm target = plot.TargetRealm;
#if EC_SERVER
                    // 旗下的单城行政区失去治所后已无城可守，由原帝国核心应战。
                    if (target?.hasCities() != true) target = origin.GetEmpire()?.CoreKingdom;
#endif
                    War war = null;
                    if (pActor.Realm.HasEnemies())
                    {
                        war = pActor.Realm.GetWars()?.FirstOrDefault(w => !w.HasEnded() &&
                            (w.IsAttacker(origin) || w.IsDefender(origin) ||
                             w.IsAttacker(target) || w.IsDefender(target)));
                    }
                    else
                    {
                        war = EmpireWorld.Current.StartWar(pActor.Realm, target, WarKinds.Rebellion);
                    }
#if EC_SERVER
                    if (war == null)
                    {
                        Realm rebel = pActor.kingdom;
                        foreach (City city in rebel.cities.ToArray())
                            if (city != originCity) city.joinAnotherKingdom(origin);
                        RebellionStartupService.RollbackCitySplit(originCity, origin, rebel);
                        return false;
                    }
#endif
                    if (war != null)
                    {
                        war.SetEmpireWarType(EmpireWarType.地方独立);
                        pActor.Realm.StartLocalRebelling(EmpireWarType.地方独立, pre: pActor.City.GetCityName());
                        RebellionSystem.Record(pActor.Realm, war, cause);
                        // 就地武装义军：以前城市起事走原版起义流程，无小人模式下城里几乎没有实体兵，
                        // 叛军一冒头手里没兵，集结期一过本省军府开到就收复了。民心越背离(忠诚越低)武装越多
                        float fervor = Mathf.Clamp(0.3f + Mathf.Max(0f, -(cause.loyalty ?? 0)) / 100f, 0.3f, 1f);
                        RebellionStartupService.RaiseUprisingMilitia(pActor.Realm, fervor);
                        // 成员的城市独立：中央不自动出兵，由成员长官求援(见 MemberWarAid)
                    }
				    return true;
			    }
		    }); 
            // 停战保护期(用户 2026-11-09 选"最短战期 3 年·停战保护期 5 年")：同一对国家议和后五年内不得再战。
            // 以前这里把原版的 5 年压到 3 年求"开战更频繁"，和"一开打就收岁币白和"叠在一起就成了几年一轮的空转
            // (华章 11-09："天下统一战争一开打就收岁币强制和平，然后代码限制接着开打，又接着白和了")。
            // 全仓的停战判定都读这一个值——WarGate.CanDeclare、WarlordUnificationSystem.InTruce(开国雄主的统一战争)、
            // WarTargetPatch、SteppeOrderSystem.Feuds、DiplomacyActions 与 DiplomacyLedger 的提示——改这一处即可覆盖。
            // 上帝视角不受此限：外交面板的宣战只提醒、不拦(见 DiplomacyActions.DeclareWarning)
            if (SimGlobals.m != null)
            {
                SimGlobals.m.diplomacy_years_war_timeout = 3;
                SimGlobals.m.minimum_years_between_wars = 5;
            }
            // 原版"攻方议和"不适用于同一法统内部的统一战争(内战打到底，见 WarlordEraSystem 统一进程)
            PlotKind stopWar = AssetManager.plots_library.get("attacker_stop_war");
            if (stopWar != null)
            {
                var stopWarContinue = stopWar.check_should_continue;
                stopWar.check_should_continue = actor => !WarlordEraSystem.IsCivilWar(actor?.plot?.target_war) &&
                                                         !RulerTraitSystem.IsFounderUnificationWar(actor?.plot?.target_war) &&
                                                         (stopWarContinue?.Invoke(actor) ?? true);
            }
            PlotKind vanillaWar = AssetManager.plots_library.get("new_war") ?? PlotsLibrary.new_war;
            // 独立核心的原版库也有同名占位，只有带原版行为的定义才能用来宣战。
            if (vanillaWar?.try_to_start_advanced == null || vanillaWar.action == null) vanillaWar = null;
            if (vanillaWar != null)
            {
                PlotsLibrary.new_war ??= vanillaWar;
                vanillaWar.money_cost = 10;
                var vanillaWarPossible = vanillaWar.check_is_possible;
                var vanillaWarForced = vanillaWar.check_can_be_forced;
                var vanillaWarStart = vanillaWar.try_to_start_advanced;
                var vanillaWarContinue = vanillaWar.check_should_continue;
                var vanillaWarAction = vanillaWar.action;
                vanillaWar.check_is_possible = actor => IsIndependentWarRealm(actor?.kingdom)
                    ? (vanillaWarPossible?.Invoke(actor) ?? false)
                    : CanStartModWar(actor, vanillaWar);
                if (vanillaWarForced != null)
                    vanillaWar.check_can_be_forced = actor => IsIndependentWarRealm(actor?.kingdom)
                        ? vanillaWarForced(actor)
                        : CanStartModWar(actor, vanillaWar);
                vanillaWar.try_to_start_advanced = (actor, asset, forced) =>
                    IsIndependentWarRealm(actor?.kingdom)
                        ? vanillaWarStart != null && vanillaWarStart(actor, asset, forced)
                        : TryStartModWar(actor, asset, forced);
                // 谋划期间(动员、陈兵边境要几个月)结成了不该打的关系(联姻、共事、统一战线……)就作罢，见 WarGate.Forbidden
                vanillaWar.check_should_continue = actor => IsIndependentWarRealm(actor?.kingdom)
                    ? (vanillaWarContinue?.Invoke(actor) ?? true) &&
                      !WarGate.Forbidden(actor.kingdom, actor.plot?.target_kingdom)
                    : ShouldContinueModWar(actor);
                vanillaWar.action = actor => IsIndependentWarRealm(actor?.kingdom)
                    ? (vanillaWarAction?.Invoke(actor) ?? false)
                    : ActModWar(actor);
            }
            else
            {
                LogService.LogWarning("Vanilla new_war plot is unavailable; EmpireCraft war plot will handle independent kingdoms.");
            }
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "empirecraft_war",
                is_basic_plot = vanillaWar == null,
                path_icon = "plots/icons/plot_new_war",
                group_id = "empirecraft_diplomacy",
                min_level = 3,
                min_warfare = 6,
                money_cost = 10,
                min_renown_kingdom = 50,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                requires_diplomacy = true,
                unlocked_with_achievement = false,
                check_is_possible = actor => vanillaWar == null && CanStartModWar(actor, vanillaWar),
                try_to_start_advanced = (actor, asset, forced) =>
                    vanillaWar == null && TryStartModWar(actor, asset, forced),
                check_should_continue = ShouldContinueModWar,
                action = ActModWar
            });
            if (vanillaWar != null)
                AssetManager.plots_library.basic_plots.RemoveAll(plot => plot.id == "empirecraft_war");

            PlotsLibrary.alliance_create.check_is_possible = delegate (Person pActor)
            {
                Realm kingdom = pActor.Realm;
                if (!FeudalVassalService.CanJoinAlliance(kingdom, null)) return false;
                if (kingdom.hasAlliance())
                {
                    return false;
                }
                if (kingdom.IsInEmpire())
                {
                    return false;
                }
                if (kingdom.HasEnemies())
                {
                    return false;
                }
                if (kingdom.isSupreme())
                {
                    return false;
                }
                if (GameClock.YearsSince(kingdom.Record.timestamp_alliance) < SimGlobals.m.alliance_timeout)
                {
                    return false;
                }
                return !EmpireWorld.Current.Plots.isPlotTypeAlreadyRunning(pActor, PlotsLibrary.alliance_create);
            };
            AssetManager.plots_library.list.RemoveAll(a => a.id == "alliance_join");
            AssetManager.plots_library.basic_plots.RemoveAll(a => a.id == "alliance_join");
            AssetManager.plots_library.dict.Remove("alliance_join");
            AssetManager.plots_library.add(new PlotKind
            {
                Id = "alliance_join",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_alliance_create",
                group_id = "empirecraft_diplomacy",
                min_level = 2,
                money_cost = 5,
                min_diplomacy = 5,
                min_renown_kingdom = 50,
                can_be_done_by_king = true,
                check_target_alliance = true,
                requires_diplomacy = true,
                check_is_possible = delegate (Person pActor)
                {
                    if (IsVanillaAllianceBlocked()) return false;
                    Realm kingdom = pActor.Realm;
                    if (!FeudalVassalService.CanJoinAlliance(kingdom, null)) return false;
                    Regime regime = kingdom.GetRegime();
                    if (regime == null) return false;
                    if (!regime.IsAllowDiplomacy()) return false;
                    if (kingdom.isSupreme())
                    {
                        return false;
                    }
                    if (kingdom.hasAlliance())
                    {
                        return false;
                    }
                    if (kingdom.IsInEmpire())
                    {
                        return false;
                    }
                    if (kingdom.HasEnemies())
                    {
                        return false;
                    }
                    if (GameClock.YearsSince(kingdom.Record.timestamp_alliance) < SimGlobals.m.alliance_timeout)
                    {
                        return false;
                    }
                    return EmpireWorld.Current.alliances.anyAlliances() ? true : false;
                },
                try_to_start_advanced = delegate (Person pActor, PlotKind pPlotAsset, bool pForced)
                {
                    if (IsVanillaAllianceBlocked() || pActor?.plot?.isActive() == true || pActor?.Realm == null ||
                        pActor.Realm.IsInEmpire() || pActor.Realm.hasAlliance() ||
                        !FeudalVassalService.CanJoinAlliance(pActor.Realm, null)) return false;
                    Realm kingdom = pActor.Realm;
                    _ = kingdom.power;
                    Alliance alliance = null;
                    foreach (var item3 in EmpireWorld.Current.alliances.list.LoopRandom())
                    {
                        if (!item3.hasWars())
                        {
                            if (pForced)
                            {
                                if (!FeudalVassalService.CanJoinAlliance(kingdom, item3)) continue;
                                alliance = item3;
                                break;
                            }

                            if (!FeudalVassalService.CanJoinAlliance(kingdom, item3) ||
                                !item3.canJoin(kingdom) || item3.hasSupremeKingdom()) continue;
                            _ = item3.power;
                            var flag = kingdom.Cities.Count <= 2 && !kingdom.hasNearbyKingdoms();
                            if (!flag && item3.hasSharedBordersWithKingdom(kingdom))
                            {
                                flag = true;
                            }
                            if (flag)
                            {
                                alliance = item3;
                            }
                        }
                    }
                    if (alliance == null)
                    {
                        return false;
                    }
                    Plot plot = EmpireWorld.Current.Plots.newPlot(pActor, pPlotAsset, pForced);
                    if (plot == null) return false;
                    plot.target_alliance = alliance;
                    if (!plot.checkInitiatorAndTargets())
                    {
                        Debug.Log("tryPlotJoinAlliance is missing start requirements");
                        plot.setAlive(false);
                        return false;
                    }
                    pActor.setPlot(plot);
                    return true;
                },
                check_other_plots = PlotsLibrary.alliance_create.check_other_plots,
                check_can_be_forced = (Person pActor) =>
                    !IsVanillaAllianceBlocked() && !pActor.Realm.hasAlliance() && !pActor.Realm.IsInEmpire() &&
                    FeudalVassalService.CanJoinAlliance(pActor.Realm, null),
                check_should_continue = delegate(Person pActor)
                {
                    if (IsVanillaAllianceBlocked()) return false;
                    Plot plot = pActor.plot;
                    if (pActor.Realm.hasAlliance() || pActor.Realm.IsInEmpire())
                    {
                        return false;
                    }
                    if (!plot.target_alliance.Alive)
                    {
                        return false;
                    }
                    if (!FeudalVassalService.CanJoinAlliance(pActor.Realm, plot.target_alliance)) return false;
                    if (!plot.target_alliance.canJoin(pActor.Realm))
                    {
                        return false;
                    }
                    if (pActor.Realm.HasEnemies())
                    {
                        return false;
                    }
                    return !plot.target_alliance.hasWars();
                },
                action = delegate(Person pActor)
                {
                    if (IsVanillaAllianceBlocked()) return false;
                    Plot plot = pActor.plot;
                    if (pActor.Realm.hasAlliance() || pActor.Realm.IsInEmpire())
                    {
                        return false;
                    }
                    if (!plot.target_alliance.Alive)
                    {
                        return false;
                    }
                    if (!FeudalVassalService.CanJoinAlliance(pActor.Realm, plot.target_alliance)) return false;
                    plot.target_alliance.join(pActor.Realm);
                    if (pActor.Realm.HasRuler())
                    {
                        pActor.Realm.Ruler.leavePlot();
                    }
                    return true;
                }
            });
            // 君主的大政：迁都、多京直隶、改制(只由人亲自发起、走决议天平，见 RulerDecrees)
            EmpireCraft.Core.RulerDecrees.Register();
            BlockVanillaAlliancePlots();
            foreach (var asset in AssetManager.plots_library.getList())
                VanillaIdentitySystem.ApplyPlotPolicy((PlotKind)asset);
            GuardAllPlotAssets();
            LogService.LogInfo($"Currently loaded{AssetManager.plots_library.getList().Count().ToString()} plots");
            AssetManager.plots_library.linkAssets();
        }

        private static bool minister_acquire_empire(Person pActor)
        {
            return pActor?.Realm?.GetEmpire()?.CompletePowerfulMinisterUsurpation(pActor) == true;
        }
        public static bool BecomeEmpireAndStartEnfeoff(Person pActor)
        {
            if (EmpireCraft.Core.AncientWarfareCompatibility.BlocksEmpireFormation(pActor?.Realm))
            {
                return false;
            }
            Realm kingdom = pActor?.Realm;
            if (kingdom == null || !kingdom.Alive)
            {
                return false;
            }
            if (kingdom.Ruler != pActor ||
                !EmpireCraftKingdomBehCheckEmpire.CanStartEmpireFormation(kingdom, true))
            {
                return false;
            }

            // 路线在称帝这一刻最终确定：定国号(SelectFoundingEmpireName 读取)、初始天命与诸国表态都按它来
            EmpireFormationRoute route = EmpireFormationService.GetBestRoute(kingdom);
            if (route == EmpireFormationRoute.None) route = kingdom.GetOrCreate().empire_formation_route;
            kingdom.GetOrCreate().empire_formation_route = route;
            Alliance foundingAlliance = kingdom.hasAlliance() ? kingdom.getAlliance() : null;
            List<Realm> allianceMembers = foundingAlliance?.kingdoms_hashset?.ToList() ?? new List<Realm>();
            List<Realm> coRuledRealms = PersonalUnionService.GetRealms(pActor)
                .Where(realm => realm != kingdom && !realm.IsEmpire() && !realm.IsInEmpire()).ToList();
            // 同文化可以有多个帝国并立；主法理在别国核心里的是僭越称帝，先用临时核心，正统之争胜出后再接管原核心
            Empire contested = EmpireFormationService.GetSeatCoreEmpire(kingdom);
            // 据有法理(华夏)：主法理所在帝国法理空闲先绑它，否则绑据三分之二的那个；那个还被旧朝占着就对它僭越(见 RealmNamingSystem)
            EmpireCore bindCore = null;
            bool forceNewCore = contested != null;
            if (route == EmpireFormationRoute.Dominion)
                contested = RealmNamingSystem.PlanDominionBinding(kingdom, contested, out bindCore, out forceNewCore);
            Empire empire = ModClass.EMPIRE_MANAGER?.NewEmpire(kingdom, allowCultureRival: true,
                forceNewCore: forceNewCore, bindCore: bindCore);
            if (empire == null)
            {
                kingdom.GetOrCreate().empire_formation_route = EmpireFormationRoute.None;
                return false;
            }

            EmpireFormationService.CompleteFormation(empire, kingdom, route, foundingAlliance,
                allianceMembers, coRuledRealms);
            if (contested != null) ImperialLegitimacyChallengeService.DeclareUsurpation(empire, contested);
            RealmNamingSystem.AfterProclaimed(empire);
            return true;
        }

        private static bool CanStartModWar(Person actor, PlotKind sharedWarPlot)
        {
            Realm kingdom = actor?.Realm;
            // 春秋战国：周室诸侯也可以自己开战(见 ZhouEraSystem)
            if (kingdom == null || kingdom.Ruler != actor ||
                !CanInitiateNormalWar(kingdom.IsEmpire(), kingdom.IsInEmpire()) && !ZhouEraSystem.VassalMayWage(kingdom) ||
                DiplomacyLaws.WarBlocked || CorpsSystem.CannotFundWar(kingdom) || !IsWarNeededForPlot(kingdom)) return false;
            // 帝国开战看皇帝的资质(见 RulerQualitySystem.WillsWarThisYear)；诸侯、附庸的君主不定资质，不在此列
            if (kingdom.IsEmpire() && !RulerQualitySystem.WillsWarThisYear(actor)) return false;
            Regime regime = kingdom.GetRegime();
            if (regime == null || !regime.IsAllowDiplomacy()) return false;
            if (actor.HasCulture() && actor.Culture.HasTrait("serenity_now") ||
                actor.HasTrait("pacifist")) return false;
            sharedWarPlot ??= AssetManager.plots_library.get("empirecraft_war");
            if (kingdom.hasAlliance())
            {
                foreach (Realm ally in kingdom.getAlliance().kingdoms_hashset)
                {
                    Person allyKing = ally == kingdom ? null : ally?.Ruler;
                    if (allyKing?.hasPlot() == true && sharedWarPlot != null &&
                        allyKing.plot.isSameType(sharedWarPlot)) return false;
                }
            }
            return GetWarTarget(kingdom) != null;
        }

        private static bool TryStartModWar(Person actor, PlotKind asset, bool forced)
        {
            if (actor?.Realm == null ||
                !CanInitiateNormalWar(actor.Realm.IsEmpire(), actor.Realm.IsInEmpire()) &&
                !ZhouEraSystem.VassalMayWage(actor.Realm)) return false;
            Realm target = GetWarTarget(actor?.Realm);
            if (target == null || EmpireWorld.Current?.Plots == null) return false;
            Plot plot = EmpireWorld.Current.Plots.newPlot(actor, asset, forced);
            if (plot == null) return false;
            plot.TargetRealm = target;
            if (plot.checkInitiatorAndTargets()) return true;
            plot.setAlive(false);
            return false;
        }

        private static bool ShouldContinueModWar(Person actor)
        {
            Realm kingdom = actor?.Realm;
            Realm target = actor?.plot?.target_kingdom;
            return kingdom != null && target != null && actor.plot.getAsset()?.isAllowedByWorldLaws() == true &&
                   !DiplomacyLaws.WarBlocked && IsWarNeededForPlot(kingdom) &&
                   IsValidModWarTarget(kingdom, target);
        }

        private static bool ActModWar(Person actor)
        {
            Realm kingdom = actor?.Realm;
            Realm target = actor?.plot?.target_kingdom;
            if (kingdom == null || target == null || !IsWarNeededForPlot(kingdom) || CorpsSystem.CannotFundWar(kingdom) ||
                !IsValidModWarTarget(kingdom, target) || EmpireWorld.Current?.diplomacy == null) return false;
            War war = EmpireWorld.Current.StartWar(kingdom, target, WarKinds.Normal);
            return war != null;
        }
#endif

        private static bool IsIndependentWarRealm(Realm kingdom) =>
            kingdom != null && UsesVanillaWarPlot(kingdom.IsEmpire(), kingdom.IsInEmpire(),
                FeudalVassalService.GetOverlord(kingdom) != null);

        // 开战谋划对君主本人的门槛(原版 PlotKind.checkIsPossible 先查：等级 3、武略 6、本人的钱、国家声望 50)。
        // 这几项都靠原版小人的日常攒：打仗、读书、逐年长进涨等级武略，城主向君主上贡攒钱，人丁出生涨国家声望。
        // 无小人模式把这些都抽象掉了——君主多是从人口里新补出来的(武略只有天生的三四点、等级一二级)，城主也不再上贡，
        // 部落、小国的君主几十年过不了门槛，独立王国一直没有开战的谋划([开战诊断]："君主武略 3 < 6"、"君主的钱不够")。
        // 无小人模式下独立王国不看君主本人这几项(见 PlotAssetPatch.checkIsPossible)，照原版看国家要不要打(isWarNeeded)、
        // 打不打得起(CorpsSystem.CannotFundWar)、有没有对象(getWarTarget)；好和平的君主与文化照旧不打。
        // 帝国、附庸、周室诸侯也一样免(用户 2026-10-30 转群友"外部帝国占了西南，华夏帝国几十年没有收复意图""分裂后周围好多
        // 比他弱的小国，基本就是不打"：以前只免独立王国，帝国照旧看君主本人，几乎从不谋划开战，只剩朝堂奏议这一条路)；
        // 帝国成员本来就不自己对外开战；兼容模组接管的国家照原版
        internal static bool SkipsWarPlotRulerGate(Person actor) =>
            actor?.Realm != null && CityPopulationSystem.AbstractPopulationEnabled &&
            !EmpireCraft.Core.AncientWarfareCompatibility.OwnsObject(actor) &&
            (CanInitiateNormalWar(actor.Realm.IsEmpire(), actor.Realm.IsInEmpire()) || ZhouEraSystem.VassalMayWage(actor.Realm));

        // 宗教战争的对象：与发起国(或其所在帝国)接壤的异教国家，不是本帝国的成员
        private static bool HolyWarNeighbour(Realm kingdom, Realm target) =>
            target != null && target != kingdom && !target.IsGone() && !target.IsInSameEmpire(kingdom) &&
            (kingdom.IsNeighbourWith(target) || kingdom.GetEmpire()?.IsNeighbourWith(target) == true);

        internal static bool UsesVanillaWarPlot(bool isEmpire, bool inEmpire, bool hasOverlord) =>
            !isEmpire && !inEmpire && !hasOverlord;

        internal static bool CanInitiateNormalWar(bool isEmpire, bool inEmpire) =>
            isEmpire || !inEmpire;

#if EC_PENDING
        public static Realm GetWarTarget(Realm pInitiatorKingdom)
        {
            if (pInitiatorKingdom == null || !pInitiatorKingdom.Alive ||
                EmpireWorld.Current?.LiveWars == null) return null;
            using ListPool<Realm> neutral = EmpireWorld.Current.LiveWars.getNeutralKingdoms(
                pInitiatorKingdom, false, false);
            return neutral.Where(target => IsValidModWarTarget(pInitiatorKingdom, target))
                // 先收法理：占着本国法理城的邻国排在前面(见 DeJureAgenda)
                .OrderByDescending(target => DeJureAgenda.HasClaimOn(pInitiatorKingdom, target))
                // 其次占着本国所在帝国法理里城的
                .ThenByDescending(target => DeJureAgenda.HoldsCoreOf(pInitiatorKingdom, target))
                // 有统一倾向的文化先打同一天下的(见 UnificationDrive)
                .ThenByDescending(target => UnificationDrive.Prefers(pInitiatorKingdom, target))
                // 再就近：通达层算好后按人员走过去的成本(翻山越岭的算远)，没算好时照旧量直线(见 ConnectivitySystem.WarTargetOrder)
                .ThenBy(target => ConnectivitySystem.WarTargetOrder(pInitiatorKingdom, target,
                    Realm.distanceBetweenKingdom(pInitiatorKingdom, target)))
                .ThenBy(target => EmpireWorld.Current.diplomacy?.getOpinion(pInitiatorKingdom, target)?.total ?? 0)
                .ThenBy(target => target.id)
                .FirstOrDefault();
        }

        private static bool IsValidModWarTarget(Realm initiator, Realm target)
        {
            // 民族统一战线期间，同一核心的各政府互不宣战(见 NationalSentimentSystem)
            if (NationalSentimentSystem.InSameUnitedFront(initiator, target)) return false;
            // 遗诏分治的兄弟之国互不征伐(见 EmpirePartitionSystem)
            if (EmpirePartitionSystem.InAmity(initiator, target)) return false;
            // 共主与共事它的部落互不征伐，共主只打不服的、脱离的(见 TribalFissionService.Bound)
            if (TribalFissionService.Bound(initiator, target)) return false;
            if (initiator?.Capital == null || target?.Capital == null || initiator == target ||
                !target.Alive ||
                !target.HasCities() || target.getAge() < SimGlobals.m.minimum_kingdom_age_for_attack ||
                !initiator.Capital.reachableFrom(target.Capital) && !StraitReach.Across(initiator, target))
                return false;
            double lastWarEnded = DiplomacyHelpers.diplomacy?.getRelation(initiator, target)
                ?.data?.timestamp_last_war_ended ?? -1d;
            if (lastWarEnded >= 0 &&
                GameClock.YearsSince(lastWarEnded) < SimGlobals.m.minimum_years_between_wars)
                return false;
            // 草原常态：没有雄主在位的游牧政权平时只打两城以内的小国(见 SteppeOrderSystem)
            if (!SteppeOrderSystem.PeacetimeWarAllowed(initiator, target)) return false;
            // 春秋战国：周室诸侯之间可以互相征伐(春秋不得攻王畿)
            bool zhouValid = ZhouEraSystem.IsValidVassalWarTarget(initiator, target, out bool zhouHandled);
            if (zhouHandled) return zhouValid;
            if (initiator.IsEmpire()) return IsValidImperialWarTarget(initiator, target);
            Empire empire = initiator.IsInEmpire() ? initiator.GetEmpire() : null;
            if (empire != null && (target.GetGivenAllianceEmpire() == empire ||
                                   target.GetTakenAllianceEmpire() == empire)) return false;
            if (initiator.HasGivenAlliance() &&
                target == initiator.GetGivenAllianceEmpire()?.CoreKingdom &&
                !initiator.NeedToRemoveGivenAlliance()) return false;
            // 法理相连且走得通，或隔海相望(见 WarReach.WarNear)；附庸同样只打比自己弱的
            bool neighbours = WarReach.WarNear(initiator, target);
            return neighbours && WarSituation.Power(initiator) > WarSituation.Power(target) &&
                   WarGate.CanDeclare(initiator, target);
        }

        private static bool IsValidImperialWarTarget(Realm initiator, Realm target)
        {
            if (NationalSentimentSystem.InSameUnitedFront(initiator, target)) return false;
            if (EmpirePartitionSystem.InAmity(initiator, target)) return false;
            if (TribalFissionService.Bound(initiator, target)) return false;
            if (initiator == null || target == null || initiator == target || !initiator.IsEmpire() ||
                !target.Alive || target.IsInSameEmpire(initiator)) return false;
            Empire empire = initiator.GetEmpire();
            if (empire == null || empire.IsArchived() || empire.IsGone() ||
                !WarReach.WarNear(initiator, target)) return false;
            if (target.GetGivenAllianceEmpire() == empire || target.GetTakenAllianceEmpire() == empire)
                return false;
            // 只打比自己弱的(国力连同各成员国比，见 WarSituation.Power)：帝国开战不再先看君主本人(见 SkipsWarPlotRulerGate)，
            // 原版挑目标那道"找比自己弱的"帝国这条路以前没有
            if (WarSituation.Power(initiator) <= WarSituation.Power(target)) return false;
            // 岁币、朝贡反方向(本帝国给对方送岁币、称臣)也不打，同盟不打(见 WarGate)
            return WarGate.CanDeclare(initiator, target);
        }

        private static bool IsWarNeededForPlot(Realm kingdom)
        {
            if (kingdom == null) return false;
            if (!kingdom.IsEmpire()) return DiplomacyHelpers.isWarNeeded(kingdom);
            Empire empire = kingdom.GetEmpire();
            if (empire == null || empire.IsArchived() || empire.IsGone() ||
                !kingdom.HasCities() || !kingdom.HasCapital()) return false;
            if (kingdom.Record.timestamp_last_war != -1d &&
                GameClock.YearsSince(kingdom.Record.timestamp_last_war) <= SimGlobals.m.diplomacy_years_war_timeout)
                return false;
            // 帝国本身(核心)手上不到两场仗、没被全面侵略就可以对外开战(以前在打任何一场仗——平叛也算——都不行)；
            // 成员国各自的战事不再拖住整个帝国
            if (WarSituation.TooBusyForWar(kingdom)) return false;
            // 兵力按战时能动员的算(帝国连同各成员国)：军团模式下兵都在军团里，原版数实体士兵总是零，帝国从不谋划开战
            int warriors = empire.countWarriors();
            if (CityPopulationSystem.AbstractPopulationEnabled && empire.kingdoms_list != null)
                warriors = Math.Max(warriors, empire.kingdoms_list.Where(member => member != null && !member.IsGone())
                    .Sum(member => CityPopulationSystem.WarPotential(member)));
            return warriors > SimGlobals.m.diplomacy_years_war_min_warriors;
        }
#endif

    }
}
