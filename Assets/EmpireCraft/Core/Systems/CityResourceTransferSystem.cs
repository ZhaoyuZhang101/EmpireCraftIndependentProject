// Generated from the EmpireCraft mod: Scripts/GeneralSystems/CityResourceTransferSystem.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

// Main-thread physical transfers. Vanilla addResources returns a storage balance,
// not the delivery quantity. Refund cargo is owned stock, never newly produced goods.
public static class CityResourceTransferSystem
{
    public static int Move(City seller, City buyer, string resource, int requested)
    {
        if (seller?.Record == null || buyer?.Record == null || seller.IsGone() || buyer.IsGone() || seller == buyer ||
            string.IsNullOrEmpty(resource) || requested <= 0) return 0;
        int sellerBefore = seller.GetResourcesAmount(resource);
        int buyerBefore = buyer.GetResourcesAmount(resource);
        // A saturated native quantity query may hide extra escrow stock. Do not
        // infer a delivery from a capped counter or overflow the receiver query.
        if (sellerBefore <= 0 || sellerBefore == int.MaxValue || buyerBefore < 0) return 0;
        int amount = Math.Min(requested, Math.Min(sellerBefore, int.MaxValue - buyerBefore));
        if (amount == 0) return 0;
        // Reserve the recovery container before touching any inventory. Its
        // separate field must not mark an established city as a new settlement.
        CityPopulationData owner = seller.GetOrCreate().population ??= new CityPopulationData();
        var cargo = owner.resource_returns ??= new Dictionary<string, int>();
        using var scope = PopulationEconomySystem.PrivateUse();
        try { seller.TakeResource(resource, amount); }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][货物转移] 出库异常，按实际数量结算: {exception.Message}");
        }
        int removed = Delta(seller.GetResourcesAmount(resource), sellerBefore, amount);
        if (removed == 0) return 0;
        try { buyer.AddResourcesToRandomStockpile(resource, removed); }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][货物转移] 入库异常，保留未送达货物: {exception.Message}");
        }
        int delivered = Delta(buyerBefore, buyer.GetResourcesAmount(resource), removed);
        int returned = removed - delivered;
        if (returned > 0)
        {
            // Reusing a random stockpile can fail again when it is full. Keep
            // undelivered goods with the original owner, persisted and usable
            // through the resource hooks until a real warehouse has room.
            cargo.TryGetValue(resource, out int have);
            cargo[resource] = checked(Math.Max(0, have) + returned);
            seller.StorageVersion++;
        }
        return delivered;
    }

    private static int Delta(int before, int after, int maximum) =>
        (int)Math.Max(0L, Math.Min(Math.Max(0, maximum), (long)after - before));
}
}
