// Ported from the EmpireCraft mod: Scripts/GeneralSystems/ResourceTributeSystem.cs @ 64c66e30. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// 岁供(争夺资源之战的和约，见 WarObjective.SettleWin)：没打下产地而攻方占优议和的，败方每年从都城仓里送一份这种资源到胜方都城，
// 十年为期；仓里不够有多少送多少(不顺延)。牧场送皮革(马匹不是原版资源)。收方灭亡或两国再开战即作废。
public static class ResourceTributeSystem
{
    private const int Years = 10, Amount = 20;
    private const string PastureGoods = "leather";
    private static double _lastScan = -1d;

#if EC_PENDING
    public static void Impose(Realm debtor, Realm creditor, string resource)
    {
        if (debtor?.Record == null || creditor?.Record == null || debtor.IsGone() || creditor.IsGone() || debtor == creditor ||
            string.IsNullOrEmpty(resource)) return;
        string goods = resource == WarObjective.PastureResource ? PastureGoods : resource;
        (debtor.GetOrCreate().resource_tributes ??= new List<ResourceTribute>()).Add(new ResourceTribute
        {
            creditor_kingdom_id = creditor.Key, resource = goods, amount = Amount, years_left = Years,
            last_paid = EmpireWorld.Current?.Now() ?? -1d
        });
        TranslateHelper.LogEventMessage(string.Format(LM.Get("war_peace_resource_tribute"), debtor.GetKingdomName(),
            creditor.GetKingdomName(), WarCatalog.ResourceName(resource), Years), creditor);
    }
#endif

    // 每月看一次(挂 WarReparationsSystem 的月度扫描)，各笔满一年送一次
#if EC_PENDING
    public static void TryMonthlyScan()
    {
        if (EmpireWorld.Current?.LiveRealms == null) return;
        double now = GameClock.Now;
        if (_lastScan >= 0d && now >= _lastScan && GameClock.MonthsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Realm debtor in EmpireWorld.Current.LiveRealms)
        {
            List<ResourceTribute> list = debtor?.Record == null || debtor.IsGone() ? null : debtor.GetOrCreate().resource_tributes;
            if (list == null || list.Count == 0) continue;
            for (int i = 0; i < list.Count; i++)
            {
                ResourceTribute item = list[i];
                Realm creditor = EmpireWorld.Current.RealmByKey(item.creditor_kingdom_id);
                if (item.years_left <= 0 || creditor?.Record == null || creditor.IsGone() || debtor.isInWarWith(creditor))
                {
                    list.RemoveAt(i--);
                    continue;
                }
                if (item.last_paid >= 0d && now >= item.last_paid && GameClock.YearsSince(item.last_paid) < 1) continue;
                item.last_paid = now;
                item.years_left--;
                try
                {
                    City from = debtor.Capital, to = creditor.Capital;
                    if (from?.Record == null || to?.Record == null) continue;
                    int paid = Math.Min(item.amount, Math.Max(0, from.getResourcesAmount(item.resource)));
                    if (paid <= 0) continue;
                    from.takeResource(item.resource, paid);
                    to.addResourcesToRandomStockpile(item.resource, paid);
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][岁供] {debtor.Name} 送 {item.resource} 失败: {exception.Message}");
                }
            }
        }
    }
#endif
}
}
