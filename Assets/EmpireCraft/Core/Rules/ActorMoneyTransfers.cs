// Generated from the EmpireCraft mod: Scripts/GeneralSystems/ActorMoneyTransfers.cs. Do not edit by hand: Tools/PortTool/members.py rewrites it;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EmpireCraft.Core
{

// Main-thread wallet transfers. Known advances and refunds are not earned income.
// Thread-local markers prevent unrelated actors' receipts from being suppressed.
public static class ActorMoneyTransfers
{
    private sealed class ReferenceComparer : IEqualityComparer<Person>
    {
        public bool Equals(Person a, Person b) => ReferenceEquals(a, b);
        public int GetHashCode(Person actor) => RuntimeHelpers.GetHashCode(actor);
    }

    [ThreadStatic] private static HashSet<Person> _nonIncomeCredits;

    public static bool IsNonIncomeCredit(Person actor) => actor != null &&
        _nonIncomeCredits?.Contains(actor) == true;

#if EC_PENDING
    public static int CreditNonIncome(Person actor, int requested)
    {
        if (actor?.Record == null || requested <= 0) return 0;
        int before = actor.money;
        _nonIncomeCredits ??= new HashSet<Person>(new ReferenceComparer());
        bool ownsMarker = _nonIncomeCredits.Add(actor);
        try
        {
            actor.addMoney(requested);
            return Received(before, actor.money, requested);
        }
        catch
        {
            // addMoney may mutate the wallet before an effect/other callback throws.
            // Roll back only this advance, without firing another failing effect.
            if (actor.Record != null) actor.Record.money -= Received(before, actor.money, requested);
            throw;
        }
        finally
        {
            if (ownsMarker) _nonIncomeCredits.Remove(actor);
        }
    }
#endif

    public static int Received(int before, int after, int requested) =>
        (int)Math.Max(0L, Math.Min(Math.Max(0, requested), (long)after - before));

    // An asset purchase transfers existing cash, not occupational income. Commit
    // ownership only after both wallets settled the full agreed price.
#if EC_PENDING
    public static bool TryTransferNonIncome(Person payer, Person recipient, int amount)
    {
        if (payer?.Record == null || recipient?.Record == null || ReferenceEquals(payer, recipient) ||
            amount <= 0 || payer.money < amount || recipient.money < 0) return false;
        int before = payer.money;
        int credited = 0;
        bool committed = false;
        try
        {
            payer.addMoney(-amount);
            if ((long)before - payer.money != amount) return false;
            credited = CreditNonIncome(recipient, amount);
            committed = credited == amount;
            return committed;
        }
        finally
        {
            if (!committed)
            {
                // Vanilla effects can throw after mutating a wallet. Undo the
                // actual transfer without invoking those effects a second time.
                if (credited > 0 && recipient.Record != null) recipient.Record.money -= credited;
                if (payer.Record != null) payer.Record.money = before;
            }
        }
    }
#endif
}
}
