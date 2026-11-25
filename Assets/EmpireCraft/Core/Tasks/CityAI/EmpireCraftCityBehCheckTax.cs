// Generated from the EmpireCraft mod: Scripts/AI/CityAI/EmpireCraftCityBehCheckTax.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftCityBehCheckTax : GameAICityBase
{
    public override Type OriginalBeh => GetType();

#if EC_PENDING
    public override BehResult execute(City pCity)
    {
        if (!pCity.IsNeedToSubmitTax()) return BehResult.Continue;
        if (pCity.getLoyalty() <= 0)
        {
            if (TreasurySystem.Enabled(pCity))
            {
                EmpireCraftActorCheckTax.SettleCity(pCity);
                pCity.RecordTaxTime();
            }
            return BehResult.Continue;
        }
        var pTaxRate = pCity.Realm.GetTaxRate();
        Realm pKingdom = pCity.Realm;
        int money = Math.Max(0, pCity.GetMoney());
        int num = (int)(money * Math.Max(0d, Math.Min(1d, pTaxRate)));
        int gold = (int)((float)num * 0.1f);
        num -= gold;
        int corruptedMoney = 0;
        if (pCity.HasLeader() && !pCity.Leader.IsGone() && pCity.Leader.Alive)
        {
            Person actor = pCity.Leader;
            var corruptionValue = actor.CalcCorruptionValue();
            pCity.AddCorruptionRate(CorruptionSystem.AnnualOfficeChange(actor, pKingdom, corruptionValue));
            corruptedMoney = (int)(num * Math.Max(0d, Math.Min(1d, corruptionValue / 2d)));
        }
        if (TreasurySystem.Enabled(pCity))
        {
            EmpireCraftActorCheckTax.SettleCity(pCity);
            pCity.RecordTaxTime();
            return BehResult.Continue;
        }
        int collected = (int)(num * Math.Max(0d, Math.Min(1d, 1d - pCity.GetCorruptionRate())));
        corruptedMoney = Math.Min(collected, corruptedMoney);
        if (corruptedMoney > 0 && pCity.HasLeader())
        {
            pCity.Leader.addMoney(corruptedMoney);
            pCity.Leader.RecordCrime(LawType.贪污, corruptedMoney);
        }
        pCity.SubMoney(collected);
        pKingdom.AddMoney(collected - corruptedMoney);
        pCity.addResourcesToRandomStockpile("gold", gold);
        pCity.RecordTaxTime();
        return BehResult.Continue;
    }
#endif
}
}
