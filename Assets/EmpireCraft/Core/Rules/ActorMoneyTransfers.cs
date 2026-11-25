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

    public static int CreditNonIncome(Person actor, int requested)
    {
        if (actor?.Record == null || requested <= 0) return 0;
        int before = actor.Money;
        _nonIncomeCredits ??= new HashSet<Person>(new ReferenceComparer());
        bool ownsMarker = _nonIncomeCredits.Add(actor);
        try
        {
            actor.AddMoney(requested);
            return Received(before, actor.Money, requested);
        }
        catch
        {
            // addMoney may mutate the wallet before an effect/other callback throws.
            // Roll back only this advance, without firing another failing effect.
            if (actor.Record != null) actor.Money -= Received(before, actor.Money, requested);
            throw;
        }
        finally
        {
            if (ownsMarker) _nonIncomeCredits.Remove(actor);
        }
    }

    public static int Received(int before, int after, int requested) =>
        (int)Math.Max(0L, Math.Min(Math.Max(0, requested), (long)after - before));

    // An asset purchase transfers existing cash, not occupational income. Commit
    // ownership only after both wallets settled the full agreed price.
    public static bool TryTransferNonIncome(Person payer, Person recipient, int amount)
    {
        if (payer?.Record == null || recipient?.Record == null || ReferenceEquals(payer, recipient) ||
            amount <= 0 || payer.Money < amount || recipient.Money < 0) return false;
        int before = payer.Money;
        int credited = 0;
        bool committed = false;
        try
        {
            payer.AddMoney(-amount);
            if ((long)before - payer.Money != amount) return false;
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
                if (credited > 0 && recipient.Record != null) recipient.Money -= credited;
                if (payer.Record != null) payer.Money = before;
            }
        }
    }
}
}
