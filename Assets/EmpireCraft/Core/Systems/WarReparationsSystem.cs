// Generated from the EmpireCraft mod: Scripts/GeneralSystems/WarReparationsSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 战争赔款(和约)：由开战目标结算决定谁赔给谁(见 WarGoalSystem.Settle)——
//   立即赔付国库现款的四分之一，另按月经常收入的两成分 36 个月支付(挂在败方 KingdomExtraData.war_reparations)。
//   付不起的月份顺延(不滚息、不另记违约)；收款方灭亡或双方再度开战(撕毁和约)时终止。
//   输了几场仗、几份和约叠在一起：每月合计最多付月经常收入的 MaxIncomeShare，超出的那几份这个月顺延，不至于赔到国库见底。
public static class WarReparationsSystem
{
    private const double ImmediateShare = 0.25d;
    private const double MonthlyIncomeShare = 0.2d;
    private const int Months = 36;
    private const double MaxIncomeShare = 0.3d;
    private static double _lastScan = -1d;

    // 败方(loser)向胜方(victor)赔款
    public static void Impose(Realm loser, Realm victor) => Impose(loser, victor, ImmediateShare, true);

    // 议和台上的赔款档(见 PeaceTermRules.IndemnityTiers)：当场付国库的 immediateShare，installments 另按月分付三年
    public static void Impose(Realm loser, Realm victor, double immediateShare, bool installments)
    {
        try
        {
            if (loser?.Record == null || loser.IsGone() || victor?.Record == null || victor.IsGone() || loser == victor) return;
            if (!TreasurySystem.Enabled(loser) || !TreasurySystem.Enabled(victor)) return;
            // 立即赔付：败方国库现款的几成(默认四分之一)
            long immediate = (long)(Math.Max(0L, loser.GetTreasuryBalance()) * immediateShare);
            MoveMoney(loser, victor, immediate);
            if (!installments)
            {
                FiscalDiagnostics.Count("战争赔款");
                TranslateHelper.LogEventMessage(string.Format(LM.Get("war_reparations_signed_once"),
                    loser.GetKingdomName(), victor.GetKingdomName(), MoneyDisplay.Format(immediate)), victor);
                return;
            }
            // 月付：败方月经常收入的两成，36 个月
            long monthly = Monthly(loser);
            var reparations = loser.GetOrCreate().war_reparations ??= new List<WarReparation>();
            reparations.Add(new WarReparation
            {
                creditor_kingdom_id = victor.Key, monthly = monthly, months_left = Months
            });
            FiscalDiagnostics.Count("战争赔款");
            TranslateHelper.LogEventMessage(string.Format(LM.Get("war_reparations_signed"),
                loser.GetKingdomName(), victor.GetKingdomName(), MoneyDisplay.Format(immediate),
                MoneyDisplay.Format(monthly), Months), victor);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][赔款] 和约结算失败: {exception.Message}");
        }
    }

    // 每月一次(挂 CityPatch 的世界扫描轮转)
    public static void TryMonthlyScan()
    {
        if (EmpireWorld.Current?.LiveRealms == null || ModClass.IS_CLEAR) return;
        ResourceTributeSystem.TryMonthlyScan();
        double now = GameClock.Now;
        if (_lastScan >= 0d && now >= _lastScan && GameClock.MonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Realm debtor in EmpireWorld.Current.LiveRealms)
        {
            List<WarReparation> reparations = debtor?.Record == null ? null : debtor.GetOrCreate().war_reparations;
            if (reparations == null || reparations.Count == 0 || debtor.IsGone()) continue;
            try
            {
                Service(debtor, reparations);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][赔款] {debtor.Name} 支付失败: {exception.Message}");
            }
        }
    }

    private static void Service(Realm debtor, List<WarReparation> reparations)
    {
        long room = Math.Max(1L, (long)(StateDebtSystem.AnnualStableIncome(debtor) / 12d * MaxIncomeShare));
        // 先签的先付(列表按签约先后排)
        for (int i = 0; i < reparations.Count; i++)
        {
            WarReparation item = reparations[i];
            Realm creditor = EmpireWorld.Current.RealmByKey(item.creditor_kingdom_id);
            // 收款方灭亡或双方再度开战：和约作废
            if (item.months_left <= 0 || creditor == null || creditor.IsGone() ||
                debtor.IsAtWarWith(creditor))
            {
                reparations.RemoveAt(i--);
                continue;
            }
            long due = Math.Min(item.monthly, room);
            long paid = Math.Min(due, Math.Max(0L, debtor.GetTreasuryBalance()));
            room -= paid;
            if (paid > 0L) MoveMoney(debtor, creditor, paid);
            // 这个月该付的(额度内)付足了才消一期(超出额度的部分免了)；付不起、额度被先签的用完了就顺延
            if (due > 0L && paid >= due) item.months_left--;
            if (item.months_left <= 0) reparations.RemoveAt(i--);
        }
    }

    public static (long monthly, int monthsLeft) Owed(Realm debtor)
    {
        long monthly = 0L;
        int months = 0;
        List<WarReparation> reparations = debtor?.Record == null ? null : debtor.GetOrCreate().war_reparations;
        if (reparations != null)
            foreach (WarReparation item in reparations)
            {
                monthly += item.monthly;
                months = Math.Max(months, item.months_left);
            }
        return (monthly, months);
    }

    private static long Monthly(Realm loser) =>
        Math.Max(1L, (long)(StateDebtSystem.AnnualStableIncome(loser) / 12d * MonthlyIncomeShare));

    // 三年分付合计多少(议和台给赔款折价用)
    public static long InstallmentTotal(Realm loser) =>
        loser?.Record == null || loser.IsGone() ? 0L : Monthly(loser) * Months;

    private static void MoveMoney(Realm from, Realm to, long amount)
    {
        while (amount > 0L)
        {
            int chunk = (int)Math.Min(int.MaxValue, amount);
            from.SubMoney(chunk, TreasuryCategory.Reparations);
            to.AddMoney(chunk, TreasuryCategory.Reparations);
            amount -= chunk;
        }
    }
}
}
