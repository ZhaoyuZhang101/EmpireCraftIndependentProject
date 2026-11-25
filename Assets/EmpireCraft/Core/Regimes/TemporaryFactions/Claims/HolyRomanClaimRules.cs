// Generated from the EmpireCraft mod: Scripts/Regimes/TemporaryFactions/Claims/HolyRomanClaimRules.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Core
{

internal static class HolyRomanClaimRules
{
#if EC_PENDING
    public static bool IsEligible(Empire empire)
    {
        return empire != null && !empire.IsGone() && empire.CoreKingdom != null &&
               !empire.CoreKingdom.IsGone() &&
               empire.CoreKingdom.GetRegime()?.type == RegimeType.Feudalism;
    }

    public static IEnumerable<Realm> GetPrinces(Empire empire)
    {
        if (!IsEligible(empire)) return Enumerable.Empty<Realm>();
        return empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.IsGone() &&
                                                     kingdom != empire.CoreKingdom && kingdom.HasRuler());
    }
#endif

    // 世俗诸侯：自由城市、主教领本就不开战、不给中央出兵，治安令与诸侯特权都不涉及它们
#if EC_PENDING
    public static IEnumerable<Realm> GetSecularPrinces(Empire empire) =>
        GetPrinces(empire).Where(HolyRomanSystem.SecularPrince);

    public static bool HasElectoralCollege(Empire empire)
    {
        if (!IsEligible(empire)) return false;
        int validElectors = empire.GetCabinetMembers().Count(actor => actor != null && !actor.IsGone());
        return validElectors >= 2 && GetPrinces(empire).Count() >= 2;
    }
#endif
}
}
