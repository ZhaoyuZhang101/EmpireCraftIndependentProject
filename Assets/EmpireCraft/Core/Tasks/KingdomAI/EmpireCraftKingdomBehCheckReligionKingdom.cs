// Generated from the EmpireCraft mod: Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckReligionKingdom.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftKingdomBehCheckReligionKingdom: GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult execute(Realm pKingdom)
    {
        var ked = KingdomExtension.GetOrCreate(pKingdom);
        if (ked != null && ked.last_religion_check_ts > 0)
        {
            if (GameClock.MonthsSince(ked.last_religion_check_ts) < 1) return BehResult.Continue;
        }
        var regime = pKingdom.GetRegime();
        if (regime == null) return BehResult.Continue;
        if (pKingdom.GetKingdomType() != KingdomType.Feudalism_papal_state) return BehResult.Continue;
        var saintCity = pKingdom.religion?.GetCity();
        if (saintCity == null)
        {
            regime.SetReligionLevel(ReligionLevel.Medium);
            if (ked != null) ked.last_religion_check_ts = GameClock.Now;
            return BehResult.Continue;
        }

        if (pKingdom.Cities.Contains(saintCity))
        {
            if (!saintCity.isCapitalCity())
            {
                pKingdom.SetCapital(saintCity);
            }
        }
        else
        {
            regime.SetReligionLevel(ReligionLevel.Medium);
        }
        if (ked != null) ked.last_religion_check_ts = GameClock.Now;
        return BehResult.Continue;
    }
#endif
}
}
