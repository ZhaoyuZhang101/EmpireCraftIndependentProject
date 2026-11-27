namespace EmpireCraft.Core
{
    // Hand-written half of EmpireCraftPlotsAddition: init, the table of the mod's plots.
    //
    // The mod's init defines 57 kinds of plot at once (wars, alliances, vassals, rebellions,
    // empires, ruler's decrees ...) and cannot run until everything any of them touches is
    // ported; enfeoffing cities, raising armies and the war plots are not yet. Until then this
    // registers the kinds whose every part already runs, with the values the mod gives them,
    // and the mod's own init comes back (Port/seams.json entry removed) once it compiles whole.
    //
    // Registered now:
    //   - become_empire: a king proclaims an empire. Started by the realm's empire check
    //     (EmpireCraftKingdomBehCheckEmpire.CheckPossible) when the realm meets a founding route,
    //     prepared for 60 points of work, given up when the preparation fails, done by
    //     BecomeEmpireAndStartEnfeoff;
    //   - usurp_imperial_legitimacy: a realm holding a rival claim to an empire's seat
    //     challenges it (ImperialLegitimacyChallengeService).
    //   - empirecraft_war: a king takes his realm to war against a weaker neighbour (EmpireCraftPlotsAddition's
    //     own war plot). In the mod independent kingdoms used the base game's war plot and this one took over only
    //     where that was missing; there is no base game here, so it is registered as the mod does in that case.
    public static partial class EmpireCraftPlotsAddition
    {
        public static void init()
        {
            PlotKinds.Library.add(new PlotKind
            {
                Id = "become_empire",
                path_icon = "ui/icons/plots/plot_become_empire",
                group_id = "empirecraft_diplomacy",
                is_basic_plot = true,
                min_level = 0,
                money_cost = 0,
                progress_needed = 60f,
                can_be_done_by_king = true,
                check_is_possible = actor => false,
                check_can_be_forced = actor => !AncientWarfareCompatibility.BlocksEmpireFormation(actor?.Realm) &&
                                               EmpireFormationService.HasImperialRule(actor?.Realm),
                try_to_start_advanced = TryStartSimplePlot,
                check_should_continue = EmpireFormationService.ShouldContinuePreparation,
                action = BecomeEmpireAndStartEnfeoff
            });
            PlotKinds.Library.add(new PlotKind
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
            PlotKinds.Library.add(new PlotKind
            {
                Id = "empirecraft_war",
                is_basic_plot = true,
                path_icon = "plots/icons/plot_new_war",
                group_id = "empirecraft_diplomacy",
                min_level = 3,
                min_warfare = 6,
                money_cost = 10,
                min_renown_kingdom = 50,
                can_be_done_by_king = true,
                check_target_kingdom = true,
                requires_diplomacy = true,
                check_is_possible = actor => CanStartModWar(actor, null),
                try_to_start_advanced = TryStartModWar,
                check_should_continue = ShouldContinueModWar,
                action = ActModWar
            });
        }
    }
}
