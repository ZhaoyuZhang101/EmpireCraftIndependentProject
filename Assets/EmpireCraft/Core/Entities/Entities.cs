using System.Collections.Generic;

namespace EmpireCraft.Core
{
    public enum Sex : byte { Male, Female }

    // Core identity records. They refer to each other directly; Id is the index in
    // the world's store (never reused) and is what saves and annals write down.
    // Systems keep their own state in their own tables keyed by entity or id.
    //
    // The members below the core fields are what the ported systems ask of an
    // entity (see Port/identity-members.json for the mod name each one answers to).
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

        public long Key=>Id;                        // stable key for tables and saves of ported systems
        public Realm Record=>this;                  // "has a record": null only when the realm itself is
        public double FoundedStamp=>FoundedMonth;   // GameClock timestamp of the founding
        // Every realm here is a settled polity; unclaimed land is not a realm.
        public bool IsWild=>false;
        public bool IsNeutral()=>false;
        public bool IsCivilized()=>true;
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

        public long Key=>Id;
        public City Record=>this;
        public double FoundedStamp=>FoundedMonth;
        // The local governor. Not saved yet: nothing appoints one until the office systems are ported.
        public Person Leader;
        public bool HasLeader()=>Leader!=null&&Leader.Alive;
        public bool IsCapital()=>Realm!=null&&Realm.Capital==this;
        public int PopulationCount()=>(int)System.Math.Min(int.MaxValue,Population);
    }

    public sealed class Person
    {
        public const int AdultAge=16;
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

        public long Key=>Id;
        public Person Record=>this;
        public double BirthStamp=>BirthMonth;
        // The realm this person reigns over, kept by WorldOps (not saved; rebuilt on load).
        public Realm Throne;
        // A ruler belongs to the realm of the throne, anyone else to the realm of the home city.
        public Realm Realm=>Throne??City?.Realm;
        public string Name=>FullName();
        public string FullName()=>(Clan!=null?Clan.Surname:"")+Given;
        public bool IsRuler()=>Throne!=null;
        public bool IsCityLeader()=>City!=null&&City.Leader==this;
        public bool HasClan()=>Clan!=null;
        public int Age()=>(int)(((Alive?GameClock.Now:DeathMonth)-BirthMonth)/12);
        public bool IsAdult()=>Age()>=AdultAge;
    }

    public sealed class Clan
    {
        public int Id;
        public string Surname;
        public Person Founder;
        public int FoundedMonth;

        public long Key=>Id;
        public Clan Record=>this;
        public string Name=>Surname;
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
        public static bool IsGone(this Clan x)=>x==null;
        public static bool HasRuler(this Realm x)=>x.Ruler!=null;
        public static bool HasCapital(this Realm x)=>x.Capital!=null;
    }
}
