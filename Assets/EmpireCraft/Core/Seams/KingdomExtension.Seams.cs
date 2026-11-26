namespace EmpireCraft.Core
{
    // Hand-written half of KingdomExtension: a realm's treasury.
    //
    // The mod kept a realm's money and its books (income and spending by month and category) on
    // the realm's extra data. This game already keeps both in the realm's account on the world's
    // ledger (RealmAccounts: taxes, upkeep, arrears), and two treasuries for one realm would let
    // a realm pay from one and earn into the other. So there is one: the ledger's.
    //   - the balance the ported systems read, add to and spend from is the account's Treasury;
    //   - the books they record into are the account's Books (the realm's extra data is pointed
    //     at them when it is made, so reports, reserves and transfers read the same book);
    //   - a realm of no world (not founded yet, or gone from the record) has no money.
    public static partial class KingdomExtension
    {
        static RealmAccount Account(Realm realm)
        {
            var world = realm?.World;
            return world == null || realm.Id < 0 ? null : world.Ledger.Realm(realm.Id);
        }

        public static KingdomExtraData GetOrCreate(this Realm a, bool isSave = false)
        {
            var ed = a.GetOrCreate<Realm, KingdomExtraData>(isSave);
            var account = Account(a);
            if (ed != null && account != null && !ReferenceEquals(ed.treasury, account.Books)) ed.treasury = account.Books;
            return ed;
        }

        public static long GetTreasuryBalance(this Realm k) => k.IsGone() ? 0L : Account(k)?.Treasury ?? 0L;

        public static void AddMoney(this Realm k, int money) => AddMoney(k, money, TreasuryCategory.Other);

        public static void AddMoney(this Realm k, int money, TreasuryCategory category)
        {
            var account = Account(k);
            if (account == null) return;
            long change = TreasuryRules.ChangeBalance(ref account.Treasury, money);
            TreasurySystem.Record(k, change, category);
        }

        public static void SubMoney(this Realm k, int money) => SubMoney(k, money, TreasuryCategory.Other);

        public static void SubMoney(this Realm k, int money, TreasuryCategory category)
        {
            var account = Account(k);
            if (account == null) return;
            long before = account.Treasury;
            long change = TreasuryRules.ChangeBalance(ref account.Treasury, -(long)money);
            TreasurySystem.Record(k, change, category);
            FiscalDiagnostics.LargeDrain(k, before, money, category);
        }
    }
}
