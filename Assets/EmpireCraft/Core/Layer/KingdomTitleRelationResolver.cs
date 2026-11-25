// Generated from the EmpireCraft mod: Scripts/Layer/KingdomTitleRelationResolver.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

public sealed class KingdomTitleHolderRelation
{
    public Empire Empire;
    public Person Holder;
    public bool Unlanded;
}

public static class KingdomTitleRelationResolver
{
#if EC_PENDING
    public static List<KingdomTitleHolderRelation> ResolveCurrentHolders(KingdomTitle title)
    {
        var result = new List<KingdomTitleHolderRelation>();
        if (title == null || title.IsGone()) return result;

        EmpireCore core = title.title_capital?.GetEmpireCore();
        if (core != null)
        {
            foreach (Empire empire in EmpireCoreManager.GetEmpires(core)
                         .Where(empire => empire != null && !empire.IsArchived())
                         .OrderBy(empire => empire.GetEmpireName()))
            {
                Person holder = empire.GetLegalPeerageHolder(title);
                if (holder == null || holder.IsGone()) continue;
                Realm landedKingdom = empire.GetLandedLegalTitleKingdom(title);
                result.Add(new KingdomTitleHolderRelation
                {
                    Empire = empire,
                    Holder = holder,
                    Unlanded = landedKingdom == null || landedKingdom.Ruler != holder
                });
            }
        }

        Person directHolder = title.main_kingdom != null && !title.main_kingdom.IsGone()
            ? title.main_kingdom.Ruler
            : title.owner != null && !title.owner.IsGone() ? title.owner : null;
        if (directHolder != null && !result.Any(view => view.Holder?.Key == directHolder.Key))
        {
            Empire directEmpire = directHolder.Realm?.GetEmpire() ??
                                   directHolder.Realm?.GetTakenAllianceEmpire();
            result.Add(new KingdomTitleHolderRelation
            {
                Empire = directEmpire,
                Holder = directHolder,
                Unlanded = title.main_kingdom == null || title.main_kingdom.IsGone() ||
                           title.main_kingdom.Ruler != directHolder
            });
        }

        return result;
    }
#endif

    public static List<Realm> FindCurrentAdministrations(KingdomTitle title)
    {
        if (title == null || title.IsGone()) return new List<Realm>();
        return (title.city_list ?? new List<City>())
            .Where(city => city != null && !city.IsGone() && city.Realm != null)
            .Select(city => city.Realm)
            .Distinct()
            .Where(kingdom => !kingdom.IsGone() && kingdom.GetAdministrativeTitle() == title)
            .OrderBy(kingdom => kingdom.Key)
            .ToList();
    }
}
}
