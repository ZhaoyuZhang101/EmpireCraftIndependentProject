// Generated from the EmpireCraft mod: Scripts/AI/KingdomAI/EmpireCraftKingdomBehCheckTax.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;

namespace EmpireCraft.Core
{

public class EmpireCraftKingdomBehCheckTax : GameAIKingdomBase
{
    public override Type OriginalBeh => GetType();

    public override BehResult Execute(Realm pKingdom)
    {
        pKingdom.CheckEmpire();
        if (!TreasurySystem.Enabled(pKingdom) && !pKingdom.IsInEmpire())
        {
            if (pKingdom.GetMoney() < 0)
            {
                pKingdom.AddMoney(100);
            }
        }
        if (!pKingdom.IsNeedToSubmitTax()) return BehResult.Continue;
        pKingdom.CountingFinishedSelfPlot();
        var pTaxRate = pKingdom.GetTaxRate();
        Realm pEmpireKingdom = null;
        if (pKingdom.IsInEmpire())
        {
            Empire empire = pKingdom.GetEmpire();
            if (empire != null && !empire.IsGone() && !empire.IsArchived())
            {
                pEmpireKingdom = empire.CoreKingdom;
            }
        }
        int money = Math.Max(0, pKingdom.GetMoney());
        int num = (int)(money * Math.Max(0d, Math.Min(1d, pTaxRate)));
        int corruptedMoney = 0;
        if (pKingdom.HasRuler() && !pKingdom.Ruler.IsGone() && pKingdom.Ruler.Alive)
        {
            Person actor = pKingdom.Ruler;
            var corruptionValue = actor.CalcCorruptionValue();
            pKingdom.AddCorruptionRate(CorruptionSystem.AnnualOfficeChange(actor, pKingdom, corruptionValue));
            corruptedMoney = (int)(num * Math.Max(0d, Math.Min(1d, corruptionValue / 2d)));
        }
        if (TreasurySystem.Enabled(pKingdom))
        {
            pKingdom.RecordTaxTime();
            return BehResult.Continue;
        }
        int collected = (int)(num * Math.Max(0d, Math.Min(1d, 1d - pKingdom.GetCorruptionRate())));
        corruptedMoney = Math.Min(collected, corruptedMoney);
        if (corruptedMoney > 0 && pKingdom.HasRuler())
        {
            pKingdom.Ruler.AddMoney(corruptedMoney);
            PrivyPurse.Note(pKingdom, PrivyPurse.Graft, corruptedMoney);
            pKingdom.Ruler.RecordCrime(LawType.贪污, corruptedMoney);
        }
        pKingdom.SubMoney(collected);
        if (pEmpireKingdom != null)
        {
            pEmpireKingdom.AddMoney(collected - corruptedMoney);
        }
        pKingdom.RecordTaxTime();
        return BehResult.Continue;
    }

}
}
