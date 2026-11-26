namespace EmpireCraft.Core
{
    // Hand-written halves of ported classes: the few members that mean something else in
    // this game. Tools/PortTool switches the mod's own version of each off (Port/seams.json)
    // and the rest of the ported code calls these.
    //
    // CityPopulationSystem.SpawnFromGroup - "make one particular person out of a population group".
    // In the mod this put a unit on the map, fed it, armed it. Here the population is a number
    // and only persons on record exist, so:
    //   - a soldier is not a person: asking for one returns nobody, and the caller's troop
    //     numbers stay on the books;
    //   - anyone else becomes a person on record: an adult of a house of their own, at home
    //     in the city, of the city's faith, and of the culture the group names when a
    //     culture of that family is on record (the city's otherwise).
    public static partial class CityPopulationSystem
    {
        const uint SpawnStream = 0x5350574Eu;

        public static Person SpawnFromGroup(City city, PopGroup group, bool soldier)
        {
            var world = EmpireWorld.Current;
            if (soldier || world == null || city == null || !city.Alive || city.Realm == null || group == null) return null;
            int subject = world.People.Count * 131 + city.Id;
            Sex sex = world.Chance(SpawnStream, subject) < 0.5 ? Sex.Male : Sex.Female;
            Clan house = WorldOps.NewClan(world, Names.Surname(world, subject));
            int age = 25 + world.Pick(SpawnStream, subject, 31, 1);
            Person person = WorldOps.Birth(world, Names.Given(world, sex, subject + 1), sex, house, city, null, null, age);
            house.Founder = person;
            if (city.HasReligion()) person.SetReligion(city.Religion);
            person.SetCulture(Cultures.OfFamily(world, group.culture) ?? city.Culture);
            return person;
        }
    }

    // CityPopulationSystem.ExecuteFoldPhase - one step of a city's month (the mod ran twenty: floor, census, levies,
    // marching off, five steps of fields, economy, market, siege surrender, the town's growth on the map, building,
    // scorched earth, migration, plague, reading, growth). Here the population is the mod's own (seeded from this
    // game's head counts, see PopulationBridge), but the steps that work through things this game does not keep yet are
    // left out for now, and the books stay as they are:
    //   - fields, economy and market (crops on map tiles, jobs in buildings, goods in store): the realm's taxes and a
    //     city's food still come from this game's ledger (RealmAccounts, CityAnswers), so nothing is taxed twice;
    //   - siege surrender, the town's growth on the map, building and scorched earth (buildings and town plans);
    //   - the workforce prepared for the economy.
    // The steps kept are the mod's own, in its order. Steps come back as the parts they need are brought over.
    public static partial class CityPopulationSystem
    {
        // A month of cities still being worked through (see PortedSystems).
        internal static bool MonthPending => _foldCity != null || PendingFolds.Count > 0 || PriorityFolds.Count > 0 || AwaitingGrowth.Count > 0;

        private static bool ExecuteFoldPhase(City city, CityPopulationData data, double now, int phase)
        {
            switch (phase)
            {
                case 0:
                    _foldHarvest = 0f;
                    _foldMonthDue = data.last_economy >= 0d && now >= data.last_economy && GameClock.MonthsSince(data.last_economy) > 0;
                    _foldEconomyDue = _foldMonthDue && !EconomyHeld(data, now, _foldStretch);
                    EnsureFloor(city);
                    break;
                case 1:
                    FoldCommoners(city);
                    Census(city, data, keepBackground: true);
                    break;
                case 2: RaiseLevies(city); Census(city, data, keepBackground: true); break;
                case 3: MarchToFront(city); break;
                case 15: Migrate(city, data, now); break;
                case 16: Plague(city, data); break;
                case 17: ReadBooks(city); break;
                case 18: GrowBackground(city, data, now); break;
                // The town takes a cell of free land a month (the mod did it when the economy step had marked the month).
                case 12: EmpireCraft.Core.CityExpansionPatch.GrowVirtualCity(city); break;
                case >= 4 and <= 14: break;
                default:
                    if (!PopulationParallelSystem.TryCompleteGrowth(city))
                    {
                        if (AwaitingGrowth.Count < MaxAwaitingGrowth)
                        {
                            if (!AwaitingGrowth.Contains((city, _foldOwner))) AwaitingGrowth.Add((city, _foldOwner));
                            return true;
                        }
                        _foldPhase = 19; _waitingForMath = true; return false;
                    }
                    Census(city, data, keepBackground: true);
                    return true;
            }
            return false;
        }
    }
}
