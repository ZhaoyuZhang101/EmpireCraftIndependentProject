using System.Collections.Generic;

namespace EmpireCraft.Core
{
    public enum Sex : byte { Male, Female }

    // Core identity records. They refer to each other directly; Id is the index in
    // the world's store (never reused) and is what saves and annals write down.
    // Systems keep their own state in their own tables keyed by entity or id.
    public sealed class Realm
    {
        public int Id;
        public string Name;
        public uint Color;              // 0xRRGGBB
        public City Capital;
        public Person Ruler;
        public Clan RulingClan;         // the reigning house
        public Person PreviousRuler;    // last ruler, kept for succession
        public int FoundedMonth;
        public int EndedMonth=-1;
        public readonly List<City> Cities=new List<City>();
        public bool Alive=>EndedMonth<0;
    }

    public sealed class City
    {
        public int Id;
        public string Name;
        public int Tile;                // seat tile on the ORBIS grid
        public Realm Realm;
        public double Population;
        public int FoundedMonth;
        public int EndedMonth=-1;
        public readonly List<int> Tiles=new List<int>();
        public bool Alive=>EndedMonth<0;
    }

    public sealed class Person
    {
        public int Id;
        public string Given;
        public Sex Sex;
        public int BirthMonth;
        public int DeathMonth=-1;
        public Person Father,Mother,Spouse;
        public Clan Clan;
        public City City;
        public readonly List<Person> Children=new List<Person>();
        public bool Alive=>DeathMonth<0;
    }

    public sealed class Clan
    {
        public int Id;
        public string Surname;
        public Person Founder;
        public int FoundedMonth;
    }

    public static class EntityIds
    {
        // The id a save or an annal writes for a reference; -1 for none.
        public static int IdOf(this Realm x)=>x==null?-1:x.Id;
        public static int IdOf(this City x)=>x==null?-1:x.Id;
        public static int IdOf(this Person x)=>x==null?-1:x.Id;
        public static int IdOf(this Clan x)=>x==null?-1:x.Id;
        // Safe on null: true for no entity and for one that has ended.
        public static bool IsGone(this Realm x)=>x==null||!x.Alive;
        public static bool IsGone(this City x)=>x==null||!x.Alive;
        public static bool IsGone(this Person x)=>x==null||!x.Alive;
        public static bool HasRuler(this Realm x)=>x.Ruler!=null;
        public static bool HasCapital(this Realm x)=>x.Capital!=null;
    }
}
