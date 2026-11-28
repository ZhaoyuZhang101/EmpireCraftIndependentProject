using System;
using System.Collections.Generic;
using System.Text;

namespace EmpireCraft.Core
{
    // Names from a people's own word lists: a person's given name, a clan's surname, a city's name. The lists and the way
    // they are put together are the people's rule file (Locales/Cultures/Culture_<people>/CultureRule.jsonc: Unit, Clan,
    // City, each a set of word lists and a rule), so what the regime editor changes there is how the next names come out.
    //
    // This game's reading of a rule (a sequence of tokens, as the rule files write them):
    //   - a divider separates alternatives; one is taken for each name;
    //   - a word list (group_1 ...) is followed by the checks to its right: sex_male / sex_female, only for that sex;
    //     coin_flip, one chance in two (two flips, one in four); the word goes in only if all of them pass;
    //   - space, hyphen and silent_space separate; a group that names no word list is written as it stands.
    // A clan's surname and a city's name are the part of the result at the rule's name_pos (the rest is a suffix such as
    // "clan"); a person's given name is all of it. In a people's earliest stage (PrehistoricClanSystem.Primordial) its
    // ancient lists come first where it has them (Unit.ancient_male_group / ancient_female_group, Clan.ancient_group).
    //
    // Words are picked by the world's hash of the subject, so the same world names the same way. A people without rules
    // (or a world without the rule data) gets nothing here, and the caller keeps the placeholder names (Names).
    public static class CultureNames
    {
        const uint Stream = 0x434E414D;   // "CNAM"
        static readonly Dictionary<(string, string), List<string>> Pools = new Dictionary<(string, string), List<string>>();

        // The word lists are read from Data when first asked for; a load of the rule data starts afresh (RuleData).
        public static void Reset() => Pools.Clear();

        public static string Given(EmpireWorld w, string people, Sex sex, int subject)
        {
            Setting setting = SettingOf(people);
            if (setting?.Unit == null) return null;
            if (PrehistoricClanSystem.Primordial(people))
            {
                string ancient = PickFrom(w, people, sex == Sex.Female ? setting.Unit.ancient_female_group : setting.Unit.ancient_male_group, subject, 1);
                if (!string.IsNullOrEmpty(ancient)) return ancient;
            }
            return Make(w, people, setting.Unit.groups, setting.Unit.rule, sex, subject, -1);
        }

        public static string Surname(EmpireWorld w, string people, int subject)
        {
            Setting setting = SettingOf(people);
            if (setting?.Clan == null) return null;
            if (PrehistoricClanSystem.Primordial(people))
            {
                string ancient = PickFrom(w, people, setting.Clan.ancient_group, subject, 2);
                if (!string.IsNullOrEmpty(ancient)) return ancient;
            }
            return Make(w, people, setting.Clan.groups, setting.Clan.rule, Sex.None, subject, setting.Clan.name_pos);
        }

        public static string Place(EmpireWorld w, string people, int subject)
        {
            Setting setting = SettingOf(people);
            return setting?.City == null ? null : Make(w, people, setting.City.groups, setting.City.rule, Sex.None, subject, setting.City.name_pos);
        }

        // The opening world is founded before its peoples are known (CultureGeography assigns them after): its cities, clans
        // and people are named again from their peoples' word lists, a realm named after its capital along with it. A city's
        // name is not given twice on one map where another try finds a free one. Nothing changes for a people without rules.
        public static void NameOpening(EmpireWorld w)
        {
            if (w == null || OnomasticsRule.ALL_CULTURE_RULE.Count == 0) return;
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (City city in w.Cities)
            {
                if (!city.Alive) continue;
                string people = Names.PeopleOf(city), old = city.Name, name = null;
                for (int attempt = 0; attempt < 8 && name == null; attempt++)
                {
                    string candidate = Place(w, people, city.Tile + attempt * 7919);
                    if (candidate != null && !taken.Contains(candidate)) name = candidate;
                }
                if (name == null) { taken.Add(old); continue; }
                taken.Add(name);
                city.Name = name;
                foreach (Realm realm in w.Realms)
                    if (realm.Alive && realm.Capital == city && realm.Name == Names.RealmOf(old)) realm.Name = Names.RealmOf(name);
            }
            foreach (Clan clan in w.Clans)
            {
                string surname = Surname(w, Names.PeopleOf(clan.Founder), clan.Id);
                if (surname != null) clan.Surname = surname;
            }
            foreach (Person person in w.People)
            {
                string given = Given(w, Names.PeopleOf(person), person.Sex, person.Id);
                if (given != null) person.Given = given;
            }
        }

        // Whether this people writes the given name before the surname (Unit.is_invert: "John Smith" rather than "Smith John").
        public static bool GivenFirst(string people) => SettingOf(people)?.Unit?.is_invert == true;

        static Setting SettingOf(string people) =>
            !string.IsNullOrEmpty(people) && OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(people, out Setting setting) ? setting : null;

        static List<string> Pool(string people, string group)
        {
            if (string.IsNullOrWhiteSpace(group)) return null;
            if (!Pools.TryGetValue((people, group), out List<string> pool))
                Pools[(people, group)] = pool = PartySystem.GetCultureWordPool(people, group) ?? new List<string>();
            return pool;
        }

        static string PickFrom(EmpireWorld w, string people, string group, int subject, int salt)
        {
            List<string> pool = Pool(people, group);
            return pool == null || pool.Count == 0 ? null : pool[w.Pick(Stream, subject, pool.Count, salt)];
        }

        static string Make(EmpireWorld w, string people, Dictionary<string, string> groups, OnomasticsType[] rule, Sex sex, int subject, int namePos)
        {
            if (groups == null || rule == null || rule.Length == 0) return null;
            // The alternatives, split at each divider; one is taken.
            var alternatives = new List<List<OnomasticsType>> { new List<OnomasticsType>() };
            foreach (OnomasticsType token in rule)
                if (token == OnomasticsType.divider) alternatives.Add(new List<OnomasticsType>());
                else alternatives[alternatives.Count - 1].Add(token);
            alternatives.RemoveAll(a => a.Count == 0);
            if (alternatives.Count == 0) return null;
            List<OnomasticsType> chosen = alternatives[w.Pick(Stream, subject, alternatives.Count, 3)];
            var name = new StringBuilder();
            int draw = 10;
            for (int i = 0; i < chosen.Count; i++)
            {
                OnomasticsType token = chosen[i];
                switch (token)
                {
                    case OnomasticsType.space: if (name.Length > 0 && name[name.Length - 1] != ' ') name.Append(' '); continue;
                    case OnomasticsType.silent_space: if (name.Length > 0 && name[name.Length - 1] != ',') name.Append(','); continue;
                    case OnomasticsType.hyphen: name.Append('-'); continue;
                }
                string key = token.ToString();
                if (!key.StartsWith("group_", StringComparison.Ordinal)) continue;
                // The checks to its right.
                bool include = true;
                int j = i + 1;
                for (; j < chosen.Count; j++)
                {
                    OnomasticsType check = chosen[j];
                    if (check == OnomasticsType.sex_male) include &= sex == Sex.Male;
                    else if (check == OnomasticsType.sex_female) include &= sex == Sex.Female;
                    else if (check == OnomasticsType.coin_flip) include &= w.Chance(Stream, subject, draw++) < 0.5;
                    else break;
                }
                i = j - 1;
                if (!include || !groups.TryGetValue(key, out string group) || group == null) continue;
                List<string> pool = Pool(people, group);
                name.Append(pool != null && pool.Count > 0 ? pool[w.Pick(Stream, subject, pool.Count, draw++)] : group);
            }
            string full = name.ToString().Trim();
            if (full.Length == 0) return null;
            if (namePos < 0) return full;
            // The part at name_pos; a hair space or an ordinary one separates the parts.
            string[] parts = full.Split(new[] { ' ', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? null : parts[Math.Min(Math.Max(0, namePos), parts.Length - 1)];
        }
    }
}
