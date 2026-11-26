using System.Collections.Generic;

namespace EmpireCraft.Core
{
    public enum Sex : byte { Male, Female, None }   // None is never a person's sex; the ported code names it for "either"

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
        // The colour as a number, as the ported code keeps it to put back later.
        public int ColorId{get{return (int)Color;}set{if(value>=0)Color=(uint)value;}}
        public City Capital;
        public Person Ruler;
        public Clan RulingClan;         // the reigning house
        public Person PreviousRuler;    // last ruler, kept for succession
        public int FoundedMonth;
        public int EndedMonth=-1;
        public readonly List<City> Cities=new List<City>();
        public readonly List<War> Wars=new List<War>();   // running wars, kept by WorldOps and rebuilt on load
        public Alliance Alliance;                          // the alliance it stands in, kept by WorldOps and rebuilt on load
        public bool HasAlliance()=>Alliance!=null&&Alliance.Alive;
        public Alliance GetAlliance()=>HasAlliance()?Alliance:null;
        // Good will towards another realm: its opinion of it is not below zero.
        public bool IsOpinionGood(Realm other){var opinion=World==null?null:World.Diplomacy.GetOpinion(this,other);return opinion!=null&&opinion.Total>=0;}
        public override bool Alive=>EndedMonth<0;

        public override long Key=>Id;               // stable key for tables and saves of ported systems
        public Realm Record=>this;                  // "has a record": null only when the realm itself is
        public double FoundedStamp=>FoundedMonth;   // GameClock timestamp of the founding
        // Every realm here is a settled polity; unclaimed land is not a realm.
        public bool IsWild=>false;
        public bool IsNeutral()=>false;
        public bool IsCivilized()=>true;
        public override EntityKind KindOfEntity=>EntityKind.Realm;
        public Clan GetRulingClan()=>RulingClan;
        public long RulingClanKey=>RulingClan==null?-1L:RulingClan.Key;
        public List<City> GetCities()=>Cities;
        // Where the realm is on the map: its capital's seat.
        public EcVector2 Location=>Capital==null?default(EcVector2):Capital.Center;
        // The people of the realm. This world has one; the peoples of the game are decided later.
        public string SpeciesId=PersonKind.HumanId;
        public string GetSpecies()=>SpeciesId;

        // The realm's colours as the ported systems ask for them, from its map colour.
        Tint _tint;uint _tintOf;
        public Tint GetColor(){if(_tint==null||_tintOf!=Color){_tint=Tint.FromRgb(Color);_tintOf=Color;}return _tint;}
        public void UpdateColor(Tint tint){if(tint!=null)Color=tint.Main.Rgb;}
        public int BannerIconId,BannerBackgroundId;     // which emblem the realm flies; for the client
        RealmAi _ai;
        public RealmAi Ai=>_ai??(_ai=new RealmAi());
        public int CountCities()=>Cities.Count;
        public bool HasCities()=>Cities.Count>0;
        public int CountZones(){int total=0;foreach(var city in Cities)total+=city.Tiles.Count;return total;}
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
        public int CountUnits()=>People.Count;
        public int Warriors(){long total=0;foreach(var city in Cities)total+=city.Warriors();return (int)System.Math.Min(int.MaxValue,total);}
        // Years since the realm was founded.
        public int GetAge()=>World==null?0:System.Math.Max(0,(World.Month-FoundedMonth)/12);
        // When it last went to war (a world month), -1 for never. Kept by WorldOps, rebuilt from the wars on load.
        public double LastWarAt=-1d;
        // When it last took a city from an enemy (a world month), -1 for never. Not saved.
        public double NewConquestAt=-1d;
        // Attacking in some running war.
        public bool IsAttacker(){foreach(var war in Wars)if(war.IsAttacker(this))return true;return false;}
        // A realm's mood over a city won or lost: moods are not kept here.
        public void IncreaseHappinessFromNewCityCapture(){}
        public void DecreaseHappinessFromLostCityCapture(City city){}
        // How far apart two realms lie: from capital to capital, in cells.
        public static float DistanceBetween(Realm a,Realm b)=>a?.Capital==null||b?.Capital==null||a.World==null?float.MaxValue:a.World.Distance(a.Capital.Tile,b.Capital.Tile);
        // The realm's renown: its ruler's, and a tenth of each city leader's.
        public int GetRenown(){long total=Ruler!=null&&Ruler.Alive?Ruler.Renown:0;foreach(var city in Cities){var leader=city.Leader;if(leader!=null&&leader!=Ruler&&leader.Alive)total+=leader.Renown/10;}return (int)System.Math.Min(int.MaxValue,total);}
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
        public void SetLeader(Person leader,bool announce=false){Leader=leader!=null&&leader.Alive?leader:null;}
        public void RemoveLeader(){Leader=null;}
        public Tint GetColor()=>Realm==null?null:Realm.GetColor();   // a city wears its realm's colours
        CityStatus _status;
        public CityStatus Status=>_status??(_status=new CityStatus(this));
        public Language Language=>null;
        public int CountZones()=>Tiles.Count;
        public bool IsCapital()=>Realm!=null&&Realm.Capital==this;
        public override EntityKind KindOfEntity=>EntityKind.City;
        // What stands in the city is counted on a ledger; there are no building objects, so no library either.
        static readonly List<Building> NoBuildings=new List<Building>();
        public List<Building> Buildings=>NoBuildings;
        public bool HasBuildingType(string type)=>false;
        public Building GetBuildingWithBookSlot()=>null;
        // Boats and the people on them: none, nobody is on a map here.
        public List<Person> Boats=>new List<Person>();
        // A city is never left without a holder here.
        public bool IsNeutral()=>false;
        // Neighbours are worked out afresh whenever the map's territory changes.
        public void RecalculateNeighbourCities(){}
        // A living city of a realm may grow.
        public bool CanGrowZones()=>Alive&&Realm!=null&&Realm.Alive;
        // The cell is still worth taking: free land, settleable, next to this city's land.
        public bool IsZoneToClaimStillGood(Person claimer,Cell zone,Cell origin)
        {
            if(zone==null||World==null||!zone.CanBeClaimedByCity(this)||!World.Settleable(zone.Index))return false;
            foreach(var next in zone.Neighbours)if(next.City==this)return true;
            return false;
        }
        public void SetName(string name){if(!string.IsNullOrWhiteSpace(name))Name=name;}
        // A city keeps no army object (troops are numbers; corps belong to the realm) and no books.
        public Army Army=>null;
        public int CountBooks()=>0;
        public Book GetRandomBook()=>null;
        // Neighbouring cities of the same realm.
        public HashSet<City> NeighbourCitiesOfRealm{get{var found=new HashSet<City>();foreach(var other in NeighbourCities)if(other.Realm==Realm)found.Add(other);return found;}}
        // The garrison of a city that falls is gone.
        public void RemoveSoldiers(){if(World!=null&&Id<World.Ledger.Cities.Count)World.Ledger.Cities[Id].Garrison=0;}
        // Goods are not kept in store here: nothing to take out or put in.
        public int StorageVersion;
        public void TakeResource(string resource,int amount){}
        public void AddResourcesToRandomStockpile(string resource,int amount){}
        // Whether its taking is part of a rising: the taker and the city's realm fight a war of rebellion.
        public bool CheckRebelWar(Realm taker,IEnumerable<War> wars){if(taker==null||wars==null)return false;foreach(var war in wars)if(war!=null&&war.Alive&&war.Kind==WarKinds.Rebellion&&war.IsInWarWith(taker,Realm))return true;return false;}
        // Who receives a city taken in war: the taker itself (the ported capture rules then hand it on where they must).
        public Realm FindKingdomToJoinAfterCapture(Realm taker,IEnumerable<War> wars)=>taker;
        // Reachable by land from the other city (the same landmass).
        public bool ReachableFrom(City other){var a=SeatCell();var b=other?.SeatCell();return a!=null&&b!=null&&a.IsSameIsland(b);}
        // Buildings of a type, goods in store: none here (no building objects; goods are not kept yet).
        public int CountBuildingsType(string type,bool pOnlyFinished=true)=>0;
        public int GetResourcesAmount(string resource)=>0;
        public Building GetBuildingOfType(string type,bool pCountOnlyFinished=false,bool pRandom=false)=>null;
        // The cells of its territory that touch land of another city, or no city.
        HashSet<Cell> _border;int _borderAt=-1;
        public HashSet<Cell> BorderZones
        {
            get
            {
                if(World==null)return new HashSet<Cell>();
                if(_border!=null&&_borderAt==World.TerritoryRevision)return _border;
                var found=new HashSet<Cell>();
                foreach(var cell in Zones)foreach(var next in cell.Neighbours)if(next.City!=this){found.Add(cell);break;}
                _border=found;_borderAt=World.TerritoryRevision;return found;
            }
        }
        public bool HasBookSlots()=>false;
        public bool HasRealm()=>Realm!=null&&Realm.Alive;
        public string GetSpecies()=>Realm==null?PersonKind.HumanId:Realm.SpeciesId;
        public int PopulationCount()=>(int)System.Math.Min(int.MaxValue,Population);
        // The base game's units here are the persons on record (the population is the number above).
        public int CountUnits()=>People.Count;
        public int GetPopulationMaximum()=>World==null?0:(int)System.Math.Min(int.MaxValue,SkeletonRules.Capacity(World,this));
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
        public List<Cell> Zones
        {
            get
            {
                if(World==null)return new List<Cell>();
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
        public double BirthStamp{get{return BirthMonth;}set{BirthMonth=(int)System.Math.Floor(value);}}
        // The realm this person reigns over, kept by WorldOps (not saved; rebuilt on load).
        public Realm Throne;
        // A ruler belongs to the realm of the throne, anyone else to the realm of the home city.
        public Realm Realm=>Throne??City?.Realm;
        // The full name. Writing it keeps the house name and takes the rest as the given name.
        public string Name
        {
            get{return FullName();}
            set
            {
                string text=value??"";string house=Clan!=null?Clan.Surname??"":"";
                Given=house.Length>0&&text.StartsWith(house,System.StringComparison.Ordinal)&&text.Length>house.Length?text.Substring(house.Length):text;
            }
        }
        public string FullName()=>(Clan!=null?Clan.Surname:"")+Given;
        public bool IsRuler()=>Throne!=null;
        public bool IsCityLeader()=>City!=null&&City.Leader==this;
        // The plot this person is working on (one at a time; see PlotStore.newPlot). Not saved: the decision system raises it again.
        public Plot Plot;
        // Belongs to a living realm of settled people (every person here is of the one civilised people).
        public bool IsKingdomCiv()=>Alive&&Realm!=null&&Realm.Alive;
        public bool HasPlot()=>Plot!=null&&Plot.IsActive();
        // Put somewhere on the map: a person lives in the city that holds the cell.
        public void SetCurrentTilePosition(Cell cell){var city=cell?.City;if(city!=null&&city.Alive&&Alive)City=city;}
        public bool HasClan()=>Clan!=null;
        public int Age()=>(int)(((Alive?GameClock.Now:DeathMonth)-BirthMonth)/12);
        public bool IsAdult()=>Age()>=AdultAge;
        // Years added to someone's age. A birth month is the true birth here, so adding years moves it back and none are kept apart.
        public int AgeOvergrowth{get{return 0;}set{BirthMonth-=value*12;}}
        public void RefreshAge(){}
        // Parents by key, as the ported code stores them; writing one links the child both ways.
        public long ParentKey1{get{return Father==null?-1L:Father.Key;}set{Father=ParentByKey(value,Father);}}
        public long ParentKey2{get{return Mother==null?-1L:Mother.Key;}set{Mother=ParentByKey(value,Mother);}}
        Person ParentByKey(long key,Person current)
        {
            var world=EmpireWorld.Current;
            Person parent=world==null||key<0||key>=world.People.Count?null:world.People[(int)key];
            if(parent==this)parent=null;
            if(current!=null&&current!=parent)current.Children.Remove(this);
            if(parent!=null&&!parent.Children.Contains(this))parent.Children.Add(this);
            return parent;
        }
        public bool IsParentOf(Person other)=>other!=null&&(other.Father==this||other.Mother==this);
        public void SetClan(Clan clan){Clan=clan;}
        // Persons on record are not fought over on a map: they are hale, carry no spoils, and nobody follows them with a camera.
        public int GetHealth()=>100;
        public int GetMaxHealth()=>100;
        // Health as the ported code reads and writes it: persons on record are always hale (see GetHealth).
        public int Health{get{return 100;}set{}}
        public void SetHealth(int health){}
        // Persons on record do no work on a map, carry no soldier's duty as units, are never hungry and have no passing statuses.
        public void EndJob(){}
        public void StopBeingWarrior(){}
        public bool HasStatus(string status)=>false;
        public void FinishStatusEffect(string status){}
        public int Nutrition{get{return 100;}set{}}
        public int GetMaxNutrition()=>100;
        public int Loot;
        public bool IsFavorite()=>false;
        public bool IsActor()=>true;
        public bool IsCameraFollowingUnit()=>false;
        // What a person carries into battle: nothing, persons on record do not fight as units.
        public Equipment Equipment=>null;
        public EcVector2 Position{get{var cell=Cell;return cell==null?default(EcVector2):cell.PosV;}}
        public void RemoveFromArmy(){Army=null;}
        public Subspecies Subspecies=>null;
        // An ordinary subject: on record, but neither reigning nor governing.
        public bool IsProfessionCitizen=>Throne==null&&!IsCityLeader();
        // A person goes to live where the cell is, if a city holds it.
        public void GoTo(Cell cell){if(cell!=null&&Alive&&Throne==null){var city=cell.City;if(city!=null&&city.Alive)City=city;}}
        // Two unmarried persons of opposite sex are joined; anything else leaves both as they were.
        public void SetLover(Person other){var world=EmpireWorld.Current;if(world!=null&&other!=null&&other!=this&&Alive&&other.Alive&&Spouse==null&&other.Spouse==null&&Sex!=other.Sex)WorldOps.Marry(world,this,other);}
        // A name of the kind asked for, from the name lists this game has for now.
        public string GenerateName(EntityKind kind,long id,Sex sex=Sex.None)
        {
            var world=EmpireWorld.Current;if(world==null)return null;
            int subject=(int)(id*131+world.Month);
            switch(kind)
            {
                case EntityKind.Person:return Names.Given(world,sex==Sex.None?Sex:sex,subject);
                case EntityKind.Clan:case EntityKind.Family:return Names.Surname(world,subject);
                case EntityKind.City:case EntityKind.Realm:return Names.Place(world,subject);
                default:return null;
            }
        }
        // Households are not tracked; a person has a house (clan), not a family record.
        public Family Family=>null;
        public bool HasFamily()=>false;
        public override EntityKind KindOfEntity=>EntityKind.Person;
        public PersonKind Asset=>PersonKind.Human;
        public PersonKind GetActorAsset()=>PersonKind.Human;
        public Language Language=>null;
        public bool HasLanguage()=>false;
        public void SetLanguage(Language language){}
        public JobKind Job=>null;            // persons on record are not labourers
        // The army this person leads, if any. Soldiers are counted, so only a captain is ever in one.
        public Army Army;
        public bool HasArmy()=>Army!=null&&Army.Alive;
        public bool IsArmyCaptain=>HasArmy()&&Army.Captain==this;

        // Standing, purse and the four talents. Standing and purse start at zero and are earned; talents are drawn at
        // birth (WorldOps.Birth) and the ported systems may change them.
        public int Renown,Money;
        public int Stewardship,Warfare,Intelligence,Diplomacy;
        public void AddRenown(int amount){Renown=System.Math.Max(0,Renown+amount);}
        public void AddMoney(int amount){Money+=amount;}
        public void SpendMoney(int amount){Money=System.Math.Max(0,Money-System.Math.Max(0,amount));}
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
        // The traits as objects, in a fixed order, for the ported code that walks over them.
        public List<PersonTrait> TraitList{get{var found=new List<PersonTrait>();if(_traits!=null){var ids=new List<string>(_traits);ids.Sort(System.StringComparer.Ordinal);foreach(string id in ids)found.Add(new PersonTrait(id));}return found;}}
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
        public IEnumerable<Person> GetChildren(bool livingOnly){foreach(var child in Children)if(!livingOnly||child.Alive)yield return child;}
        public Tint GetColor(){var realm=Realm;return realm==null?null:realm.GetColor();}
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
        public override EntityKind KindOfEntity=>EntityKind.Clan;
        public string Name{get{return Surname;}set{Surname=value;}}
        public string SpeciesId=>PersonKind.HumanId;
        Tint _tint;
        public Tint GetColor()=>_tint??(_tint=Palette.Titles.ColorFor(Id+100000));
        // The living persons of this house.
        public List<Person> People{get{var found=new List<Person>();var world=EmpireWorld.Current;if(world!=null)foreach(var person in world.Living)if(person.Clan==this)found.Add(person);return found;}}
        // The head of the house: a member who reigns, else one who leads a city, else the eldest adult.
        public Person GetChief()
        {
            Person chief=null;int rank=-1;
            var world=EmpireWorld.Current;if(world==null)return null;
            foreach(var person in world.Living)
            {
                if(person.Clan!=this||!person.IsAdult())continue;
                int mine=person.IsRuler()?2:person.IsCityLeader()?1:0;
                if(mine>rank||mine==rank&&person.BirthMonth<chief.BirthMonth){chief=person;rank=mine;}
            }
            return chief;
        }
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
