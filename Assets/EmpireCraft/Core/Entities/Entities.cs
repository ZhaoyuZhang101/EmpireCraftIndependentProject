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
    public sealed class Realm : Entity
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
        public readonly List<War> Wars=new List<War>();   // running wars, kept by WorldOps and rebuilt on load
        public override bool Alive=>EndedMonth<0;

        public override long Key=>Id;               // stable key for tables and saves of ported systems
        public Realm Record=>this;                  // "has a record": null only when the realm itself is
        public double FoundedStamp=>FoundedMonth;   // GameClock timestamp of the founding
        // Every realm here is a settled polity; unclaimed land is not a realm.
        public bool IsWild=>false;
        public bool IsNeutral()=>false;
        public bool IsCivilized()=>true;

        // The world this realm is in; set when it is founded or loaded.
        public EmpireWorld World;
        // The faith and the culture held, if any; set by whichever system converts or assimilates.
        public Religion Religion;public Culture Culture;
        public bool HasReligion()=>Religion!=null&&Religion.Alive;
        public bool HasCulture()=>Culture!=null&&Culture.Alive;
        public void SetReligion(Religion faith){Religion=faith;}
        public void SetCulture(Culture culture){Culture=culture;}
        // The population is counted, not tracked: headcount and troops are numbers summed over the cities.
        public int PopulationCount(){double total=0;foreach(var city in Cities)if(city.Alive)total+=city.Population;return (int)System.Math.Min(int.MaxValue,total);}
        public int Warriors(){long total=0;foreach(var city in Cities)total+=city.Warriors();return (int)System.Math.Min(int.MaxValue,total);}
        // The persons on record who belong here (rulers, their houses, office holders). Not the population.
        public List<Person> People{get{var found=new List<Person>();if(World!=null)foreach(var person in World.Living)if(person.Realm==this)found.Add(person);return found;}}
    }

    public sealed class City : Entity
    {
        public int Id;
        public string Name;
        public int Tile;                // seat tile on the ORBIS grid
        public Realm Realm;
        public double Population;
        public int FoundedMonth;
        public int EndedMonth=-1;
        public readonly List<int> Tiles=new List<int>();
        public override bool Alive=>EndedMonth<0;

        public override long Key=>Id;
        public City Record=>this;
        public double FoundedStamp=>FoundedMonth;
        // The local governor. Not saved yet: nothing appoints one until the office systems are ported.
        public Person Leader;
        public bool HasLeader()=>Leader!=null&&Leader.Alive;
        public bool IsCapital()=>Realm!=null&&Realm.Capital==this;
        public int PopulationCount()=>(int)System.Math.Min(int.MaxValue,Population);
        // The faith and the culture held, if any; set by whichever system converts or assimilates.
        public Religion Religion;public Culture Culture;
        public bool HasReligion()=>Religion!=null&&Religion.Alive;
        public bool HasCulture()=>Culture!=null&&Culture.Alive;
        public void SetReligion(Religion faith){Religion=faith;}
        public void SetCulture(Culture culture){Culture=culture;}
        // Troops are counted too: the guards the books paid for this month.
        public int Warriors()=>World==null||Id>=World.Ledger.Cities.Count||!Alive?0:World.Ledger.Cities[Id].Garrison;
        // The persons on record whose home is here. Not the population, which is the number above.
        public List<Person> People{get{var found=new List<Person>();if(World!=null)foreach(var person in World.Living)if(person.City==this)found.Add(person);return found;}}

        // The world this city stands in; set when it is founded or loaded.
        public EmpireWorld World;
        List<Cell> _zones;HashSet<City> _nearCities;HashSet<Realm> _nearRealms;int _zonesAt=-1,_nearAt=-1;
        // The cells of its territory, the seat first.
        public IReadOnlyList<Cell> Zones
        {
            get
            {
                if(World==null)return System.Array.Empty<Cell>();
                if(_zones!=null&&_zonesAt==World.TerritoryRevision&&_zones.Count==Tiles.Count)return _zones;
                var cells=new List<Cell>(Tiles.Count);foreach(int tile in Tiles)cells.Add(World.CellAt(tile));
                _zones=cells;_zonesAt=World.TerritoryRevision;return cells;
            }
        }
        public Cell SeatCell()=>World==null?null:World.CellAt(Tile);
        public EcVector2 Center=>World==null?default(EcVector2):new EcVector2(Tile%World.Width,Tile/World.Width);
        // Cities and foreign realms whose land touches this city's.
        public HashSet<City> NeighbourCities{get{Near();return _nearCities;}}
        public HashSet<Realm> NeighbourRealms{get{Near();return _nearRealms;}}
        void Near()
        {
            if(_nearCities!=null&&World!=null&&_nearAt==World.TerritoryRevision)return;
            _nearCities=new HashSet<City>();_nearRealms=new HashSet<Realm>();
            if(World==null)return;
            foreach(int tile in Tiles)
                for(int d=0;d<4;d++)
                {
                    int n=World.Map.Neighbor(tile,d);if(n<0)continue;
                    int owner=World.TileCity[n];if(owner<0||owner==Id)continue;
                    var other=World.Cities[owner];_nearCities.Add(other);
                    if(other.Realm!=null&&other.Realm!=Realm)_nearRealms.Add(other.Realm);
                }
            _nearAt=World.TerritoryRevision;
        }
    }

    public sealed class Person : Entity
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
        public override bool Alive=>DeathMonth<0;

        public override long Key=>Id;
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

        // Standing, purse and the four talents. They start at zero; the ported systems that
        // hand them out (birth, office, deeds) set them once they run.
        public int Renown,Money;
        public int Stewardship,Warfare,Intelligence,Diplomacy;
        public void AddRenown(int amount){Renown=System.Math.Max(0,Renown+amount);}
        public void AddMoney(int amount){Money+=amount;}
        // Talents by the name the ported systems ask for; anything else reads as zero.
        public sealed class Talents
        {
            readonly Person _owner;internal Talents(Person owner){_owner=owner;}
            public float this[string name]
            {
                get
                {
                    switch(name)
                    {
                        case "stewardship":return _owner.Stewardship;
                        case "warfare":return _owner.Warfare;
                        case "intelligence":return _owner.Intelligence;
                        case "diplomacy":return _owner.Diplomacy;
                        default:return 0f;
                    }
                }
            }
        }
        Talents _talents;
        public Talents Stats=>_talents??(_talents=new Talents(this));

        // Traits are plain ids; what a trait means is up to the system that gives it.
        HashSet<string> _traits;
        public IEnumerable<string> Traits=>_traits??(IEnumerable<string>)System.Array.Empty<string>();
        public int TraitCount=>_traits==null?0:_traits.Count;
        public bool HasTrait(string id)=>id!=null&&_traits!=null&&_traits.Contains(id);
        public bool AddTrait(string id,bool removeOpposites=false){if(string.IsNullOrEmpty(id))return false;if(_traits==null)_traits=new HashSet<string>(System.StringComparer.Ordinal);return _traits.Add(id);}
        public bool RemoveTrait(string id)=>id!=null&&_traits!=null&&_traits.Remove(id);

        public bool IsMale()=>Sex==Sex.Male;
        public bool IsFemale()=>Sex==Sex.Female;
        public bool IsFitToRule()=>Alive&&IsAdult();
        public bool IsSapient()=>true;
        public bool IsWarrior()=>false;     // soldiers are counted, not tracked as people
        public bool HasRealm()=>Realm!=null;
        public bool HasCity()=>City!=null;
        public bool HasSpouse()=>Spouse!=null;
        public bool HasDied()=>!Alive;
        public IEnumerable<Person> GetChildren()=>Children;
        // The faith and the culture held, if any; set by whichever system converts or assimilates.
        public Religion Religion;public Culture Culture;
        public bool HasReligion()=>Religion!=null&&Religion.Alive;
        public bool HasCulture()=>Culture!=null&&Culture.Alive;
        public void SetReligion(Religion faith){Religion=faith;}
        public void SetCulture(Culture culture){Culture=culture;}
        // Where the person is: the seat of the home city, for a ruler without one the capital's.
        public Cell Cell{get{var home=City??Throne?.Capital;return home?.SeatCell();}}
        public IEnumerable<Person> GetParents(){if(Father!=null)yield return Father;if(Mother!=null)yield return Mother;}
    }

    public sealed class Clan : Entity
    {
        public int Id;
        public string Surname;
        public Person Founder;
        public int FoundedMonth;

        public override long Key=>Id;
        public override bool Alive=>true;   // a house is never struck from the record
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
        public static bool IsGone(this Entity x)=>x==null||!x.Alive;
        public static bool HasRuler(this Realm x)=>x.Ruler!=null;
        public static bool HasCapital(this Realm x)=>x.Capital!=null;
    }
}
