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
}
