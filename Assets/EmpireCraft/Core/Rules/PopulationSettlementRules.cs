// Ported from the EmpireCraft mod: Scripts/Data/PopulationSettlementRules.cs @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public static class PopulationSettlementRules
{
    public static void SynchronizeMode(CityPopulationData data, bool enabled, double now, bool changed)
    {
        if (data == null) return;
        if (!enabled || changed || data.abstract_population_suspended)
            data.last_economy = data.last_growth = data.last_construction = now;
        data.abstract_population_suspended = !enabled;
    }

    // -1 表示没有已结算的需求。用实际吃到的粮食衡量，年产量不能冒充仓库库存。
    public static float FoodSatisfaction(CityPopulationData data)
    {
        if (data == null) return -1f;
        float eaten = Math.Max(0f, data.last_food_eaten);
        float shortage = Math.Max(0f, data.last_food_shortage);
        return eaten + shortage > 0f ? eaten / (eaten + shortage) : -1f;
    }
}
}
