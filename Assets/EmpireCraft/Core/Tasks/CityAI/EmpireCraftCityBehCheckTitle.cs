// Generated from the EmpireCraft mod: Scripts/AI/CityAI/EmpireCraftCityBehCheckTitle.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftCityBehCheckTitle: GameAICityBase
{
    public override Type OriginalBeh => GetType();
#if EC_PENDING
    public override BehResult execute(City pCity)
    {
        if (pCity.GetTitle() == null)
        {
            return BehResult.Continue;
        }
        KingdomTitle title = pCity.GetTitle();
        if (title.title_capital.IsGone())
        {
            title.title_capital = pCity;
        }
        return BehResult.Continue;
    }
#endif
}
}
