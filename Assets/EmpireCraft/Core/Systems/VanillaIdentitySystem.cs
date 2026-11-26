// Generated from the EmpireCraft mod: Scripts/GeneralSystems/VanillaIdentitySystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

// 模组不需要反复派生原版语言与亚种；现有存档对象不合并、不删除。
// 保留每物种第一门语言(原版宗教创建依赖它)，后来的单位直接复用。
public static class VanillaIdentitySystem
{
    public static bool BlocksPlot(string id) => id is "language_divergence" or "clan_ascension";

#if EC_PENDING
    public static bool TryReuseLanguage(Person actor)
    {
        if (actor?.Asset?.Civ != true || actor.HasLanguage() ||
            actor.Subspecies?.has_advanced_communication != true || EmpireWorld.Current?.languages == null) return false;
        foreach (Language language in EmpireWorld.Current.languages)
        {
            if (language?.Record == null || language.IsGone() ||
                !string.Equals(language.species_id, actor.Asset.Id, StringComparison.Ordinal)) continue;
            actor.joinLanguage(language);
            return true;
        }
        return false;
    }

    public static void ApplyPlotPolicy(PlotKind plot)
    {
        if (plot == null) return;
        if (BlocksPlot(plot.Id))
        {
            plot.check_is_possible = _ => false;
            plot.check_should_continue = _ => false;
            plot.check_can_be_forced = _ => false;
            plot.try_to_start_advanced = (_, _, _) => false;
            plot.action = _ => false;
            plot.post_action = null;
        }
        else if (plot.Id == "new_language")
        {
            // 多支决议同时起步时，后完成的也复用先完成的语言，不再额外创建。
            var action = plot.action;
            plot.action = actor => actor?.hasLanguage() == true || TryReuseLanguage(actor) || action?.Invoke(actor) == true;
        }
    }
#endif
}
}
