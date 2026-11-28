namespace EmpireCraft.Core
{
    // What the ported systems ask of a realm that this game answers by its own rule.
    //
    // - How many cities a realm can hold (MaxCities): the base game gave each people a base and the ruler's
    //   stewardship added to it, and the mod let land held by title not count against it. Here: three, one more
    //   for every five points of the ruler's stewardship (talents run 1 to 19), and the cities its titles exempt.
    // - The most warriors it keeps (MaxWarriors, the base game's countWarriorsMax): the guards its cities need, paid in
    //   full (the ledger pays Garrison = Required times what the treasury covers).
    public static class RealmAnswers
    {
        public static int MaxWarriors(this Realm realm)
        {
            if (realm?.World == null) return 0;
            long total = 0;
            foreach (City city in realm.Cities)
                if (city.Alive && city.Id < realm.World.Ledger.Cities.Count) total += realm.World.Ledger.Cities[city.Id].Required;
            return (int)System.Math.Min(int.MaxValue, total);
        }

        public static int MaxCities(this Realm realm)
        {
            if (realm == null) return 1;
            int max = 3;
            if (realm.HasRuler()) max += realm.Ruler.Stewardship / 5;
            return System.Math.Max(1, max) + realm.CountDeJureCapacityExemptCities();
        }
    }
}
