using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // What kind of thing an entity is, for the ported code that takes "any entity" and branches on it.
    public enum EntityKind : byte { None, Realm, City, Person, Clan, Family, Culture, Religion, Alliance, War, Army }

    // What a decision routine reports back to whoever ran it.
    public enum BehResult : byte { Continue, Stop }

    // A body of troops as the ported systems refer to one: where it is raised, who leads it,
    // how many it counts. Soldiers are a number, never persons; the only person in an army is
    // its captain. Nothing raises armies yet: the mod's own corps system will.
    public sealed class Army : Entity
    {
        public long Id = -1L;
        public string Name;
        public City City;            // where it is raised and paid
        public Person Captain;
        public int Soldiers;
        public bool Disbanded;
        public override long Key => Id;
        public override bool Alive => !Disbanded && City != null && City.Alive;
        public override EntityKind KindOfEntity => EntityKind.Army;
        public Realm Realm => City == null ? null : City.Realm;
        public Realm GetRealm() => Realm;
        public Person GetCaptain() => Captain;
        public bool HasCaptain() => Captain != null && Captain.Alive;
        // The persons on record in it: the captain, if any.
        public List<Person> People { get { var found = new List<Person>(); if (HasCaptain()) found.Add(Captain); return found; } }
    }

    // What sort of being a person on record is. Every person here is a human of a settled people;
    // the questions the ported systems ask (is it a ship, is it civilised) have one answer each.
    public sealed class PersonKind
    {
        public readonly string Id;
        public readonly bool IsBoat, Civ;
        PersonKind(string id, bool boat, bool civ) { Id = id; IsBoat = boat; Civ = civ; }
        public const string HumanId = "human";
        public static readonly PersonKind Human = new PersonKind(HumanId, false, true);
    }

    // Whether a trait counts for or against its bearer. Traits are plain ids; a system that
    // hands out traits says here which way each one counts. Unlisted traits count neither way.
    public enum TraitType : byte { Other, Positive, Negative }

    // A trait as the ported code walks over a person's traits: its id and which way it counts.
    public sealed class PersonTrait
    {
        public readonly string Id;
        public PersonTrait(string id) { Id = id; }
        public TraitType Kind => Id.TraitKindOf();
        public override string ToString() => Id;
    }

    // A branch of a people. This world has one people and no branches.
    public sealed class Subspecies
    {
        public string SpeciesId = PersonKind.HumanId;
    }

    public static class TraitKinds
    {
        static readonly Dictionary<string, TraitType> Known = new Dictionary<string, TraitType>(StringComparer.Ordinal);
        public static void Set(string trait, TraitType kind) { if (!string.IsNullOrEmpty(trait)) Known[trait] = kind; }
        public static void Clear() => Known.Clear();
        public static TraitType TraitKindOf(this string trait) => trait != null && Known.TryGetValue(trait, out TraitType kind) ? kind : TraitType.Other;
    }

    // Name-making data a culture could carry. This game names things from the culture
    // rules instead, so a culture has none and the ported code falls back to those.
    public sealed class NameParts
    {
        public string GenerateName() => null;
    }

    // What the ported systems ask about a culture beyond its record.
    public static class Cultures
    {
        // The family of rules a culture follows ("Huaxia" ...), empty when it has none.
        public static string FamilyOf(Culture culture) => culture == null || !culture.Alive || culture.Family == null ? "" : culture.Family;
        public static void SetFamily(Culture culture, string family, bool addTraits = true) { if (culture != null && culture.data != null) culture.Family = string.IsNullOrWhiteSpace(family) ? null : family; }
        public static NameParts NameParts(Culture culture, EntityKind kind) => null;
        // A culture is shown under the name of its family of rules, in the loaded language.
        public static bool SyncName(Culture culture, string family = null)
        {
            if (culture == null || culture.data == null) return false;
            if (string.IsNullOrWhiteSpace(family)) family = culture.Family;
            if (string.IsNullOrWhiteSpace(family)) return false;
            culture.Name = LM.Has(family) ? LM.Get(family) : family;
            return true;
        }
        // The culture on record that follows this family of rules, if there is one.
        public static Culture OfFamily(EmpireWorld world, string family)
        {
            if (world == null || string.IsNullOrEmpty(family)) return null;
            foreach (var culture in world.Cultures.list) if (culture.Alive && culture.Family == family) return culture;
            return null;
        }
    }
}
