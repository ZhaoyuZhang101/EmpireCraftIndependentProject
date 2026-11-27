using System;
using System.Collections.Generic;
using Orbis.Flat;

namespace EmpireCraft.Core
{
    // The EmpireCraft world: entity stores and tile ownership over an ORBIS map.
    // ORBIS owns terrain, water and climate; this layer owns every polity fact.
    // Mutate only through WorldOps so the invariants hold after every call.
    public sealed class EmpireWorld
    {
        public readonly WorldData Map;
        public readonly List<Realm> Realms=new List<Realm>();
        public readonly List<City> Cities=new List<City>();
        public readonly List<Person> People=new List<Person>();
        public readonly List<Clan> Clans=new List<Clan>();
        public readonly List<Realm> LiveRealms=new List<Realm>(); // living realms in founding order, kept by WorldOps
        public readonly List<War> Wars=new List<War>();          // every war, by id
        public readonly List<War> LiveWars=new List<War>();      // running wars, kept by WorldOps
        public readonly List<Alliance> Alliances=new List<Alliance>();     // every alliance, by id
        public readonly List<Alliance> LiveAlliances=new List<Alliance>(); // standing alliances, kept by WorldOps
        public readonly Diplomacy Diplomacy;                     // relations and opinions between realms
        public readonly List<Person> Living=new List<Person>(); // living people, insertion order
        public readonly int[] TileCity;                        // city id per tile, -1 unclaimed
        public readonly Annals Annals=new Annals();
        public readonly RealmLedger Ledger=new RealmLedger();   // books kept by RealmAccounts
        public readonly NewsLog News=new NewsLog();            // recent announcements of the ported systems (a feed, not saved)
        public readonly ulong Seed;
        public int StartYear=1;
        public int Month;                                     // months since the start, 0-based

        public EmpireWorld(WorldData map,ulong seed)
        {
            Map=map??throw new ArgumentNullException(nameof(map));Seed=seed;
            TileCity=new int[map.Count];for(int i=0;i<TileCity.Length;i++)TileCity[i]=-1;
            Diplomacy=new Diplomacy(this);
            Religions=new ReligionStore(this);Cultures=new CultureStore(this);Books=new BookStore(this);Plots=new PlotStore(this);
        }

        public int Year=>StartYear+Month/12;
        public int MonthOfYear=>Month%12+1;
        public int AgeOf(Person p)=>((p.Alive?Month:p.DeathMonth)-p.BirthMonth)/12;
        public City CityAt(int tile){int c=TileCity[tile];return c<0?null:Cities[c];}
        public Realm RealmAt(int tile){int c=TileCity[tile];return c<0?null:Cities[c].Realm;}
        public int RealmOfTile(int tile)=>RealmAt(tile).IdOf();
        public string FullName(Person p)=>(p.Clan!=null?p.Clan.Surname:"")+p.Given;

        // The world the ported systems act on. They are static, as in the mod, and
        // reach the world through this; EmpireSimulation sets it before every step.
        public static EmpireWorld Current;
        // The record stores of the ported layer objects, one set per world.
        public readonly EmpireManager Empires=new EmpireManager();
        public readonly KingdomTitleManager Titles=new KingdomTitleManager();
        public readonly ReligionStore Religions;                // faiths on record (see Beliefs.cs)
        public readonly CultureStore Cultures;
        public readonly BookStore Books;                        // written works and plots on record (see Works.cs); empty until a system fills them
        public readonly PlotStore Plots;
        // Makes this the world the static ported systems act on.
        public void Activate(){if(!ReferenceEquals(Current,this))WorldSwitch.Leaving(this);Current=this;ModClass.EMPIRE_MANAGER=Empires;ModClass.KINGDOM_TITLE_MANAGER=Titles;ModClass.IS_CLEAR=false;}

        // Lookups by the key a ported system stored; null for none and for an entity that has ended.
        public Realm RealmByKey(long key){var x=key>=0&&key<Realms.Count?Realms[(int)key]:null;return x!=null&&x.Alive?x:null;}
        public City CityByKey(long key){var x=key>=0&&key<Cities.Count?Cities[(int)key]:null;return x!=null&&x.Alive?x:null;}
        public Person PersonByKey(long key){var x=key>=0&&key<People.Count?People[(int)key]:null;return x!=null&&x.Alive?x:null;}
        public Clan ClanByKey(long key)=>key>=0&&key<Clans.Count?Clans[(int)key]:null;
        public double Now()=>Month;   // GameClock timestamp of this world

        // The grid as the ported systems see it (see Land.cs). Cells are made on first use.
        public int Width=>Map.Width;
        public int Height=>Map.Height;
        // Keys for the records the ported systems make (empires, titles, offices ...). The mod took them from the
        // wall clock; here they count up per world, so the same world makes the same records under the same keys.
        public long NextRecordId=1;
        public long TakeRecordId()=>NextRecordId++;
        public int TerritoryRevision;   // counts every change of who holds which tile or city; caches key on it
        Cell[] _cells;ZoneGrid _grid;int[] _landmass;
        public Cell CellAt(int index){if(index<0||index>=TileCity.Length)return null;var cells=_cells??(_cells=new Cell[TileCity.Length]);return cells[index]??(cells[index]=new Cell(this,index));}
        public Cell CellAt(int x,int y){int index=Map.Index(x,y);return index<0?null:CellAt(index);}
        public ZoneGrid Zones=>_grid??(_grid=new ZoneGrid(this));
        Cell[] _allCells;
        // Every cell as an array, made on first use.
        public Cell[] AllCells{get{if(_allCells==null)_allCells=Zones.All.ToArray();return _allCells;}}
        // Which stretch of connected land a tile is on; -1 for sea. Worked out once per world.
        public int LandmassOf(int tile){if(_landmass==null)_landmass=EmpireBootstrap.Landmasses(this);return tile<0||tile>=_landmass.Length?-1:_landmass[tile];}
        public War WarByKey(long key){var x=key>=0&&key<Wars.Count?Wars[(int)key]:null;return x!=null&&x.Alive?x:null;}
        public bool HasWars()=>LiveWars.Count>0;
        public bool HasWars(Realm realm)=>realm!=null&&realm.Wars.Count>0;
        public List<War> WarsOf(Realm realm,bool random=false)=>realm==null?new List<War>():new List<War>(realm.Wars);
        // A person founds a house of their own and belongs to it from then on.
        public Clan NewClan(Person founder,bool announce=false)
        {
            if(founder==null)return null;
            var clan=WorldOps.NewClan(this,Names.Surname(this,founder.Id*31+Month),founder);founder.Clan=clan;return clan;
        }
        // Null when there can be no such war: one of them is gone, they are the same realm, or they stand in one alliance.
        public War StartWar(Realm attacker,Realm defender,WarKind kind=null,bool log=true)
        {
            if(attacker.IsGone()||defender.IsGone()||attacker==defender||(attacker.HasAlliance()&&attacker.Alliance==defender.Alliance))return null;
            return WorldOps.StartWar(this,attacker,defender,kind);
        }
        public Alliance AllianceByKey(long key){var x=key>=0&&key<Alliances.Count?Alliances[(int)key]:null;return x!=null&&x.Alive?x:null;}
        public bool AnyAlliances()=>LiveAlliances.Count>0;
        // Null when the two cannot ally: one is gone, they are the same, at war with each other, or already in an alliance.
        public Alliance NewAlliance(Realm a,Realm b)
        {
            if(a.IsGone()||b.IsGone()||a==b||a.HasAlliance()||b.HasAlliance()||a.IsAtWarWith(b))return null;
            return WorldOps.FormAlliance(this,a,b);
        }
        public void DissolveAlliance(Alliance alliance){if(alliance!=null&&alliance.Alive)WorldOps.DissolveAlliance(this,alliance);}
        // Running counts of the world's history, as the ported code keeps them.
        public readonly WorldStats game_stats=new WorldStats();
        public readonly WorldStatsData map_stats=new WorldStatsData();
        // One object per landmass, for code that tells islands apart by reference.
        readonly Dictionary<int,IslandInfo> _islands=new Dictionary<int,IslandInfo>();
        public IslandInfo IslandObject(int tile)
        {
            if(tile<0||tile>=TileCity.Length)return null;
            int mass=LandmassOf(tile);
            if(mass<0)
            {
                var water=Map.Water[tile];
                // Open sea is one big body; a lake or a river is a small one of its own.
                if(water==Orbis.Flat.WaterKind.DeepOcean||water==Orbis.Flat.WaterKind.MiddleOcean||water==Orbis.Flat.WaterKind.ShallowOcean)return _sea??(_sea=new IslandInfo(TileCity.Length));
                return new IslandInfo(1);
            }
            if(!_islands.TryGetValue(mass,out var island))
            {
                int size=0;for(int t=0;t<TileCity.Length;t++)if(LandmassOf(t)==mass)size++;
                _islands[mass]=island=new IslandInfo(size);
            }
            return island;
        }
        IslandInfo _sea;
        // Realms a realm could turn on: every other living realm it is neither at war with nor allied to.
        public ListPool<Realm> NeutralRealms(Realm realm,bool onlyWithoutWars=false,bool onlyCivs=false)
        {
            var found=new ListPool<Realm>();if(realm==null)return found;
            foreach(var other in LiveRealms)
            {
                if(other==realm||realm.IsAtWarWith(other)||realm.HasAlliance()&&realm.Alliance==other.Alliance)continue;
                if(onlyWithoutWars&&other.Wars.Count>0)continue;
                found.Add(other);
            }
            return found;
        }
        // Some member of the alliance is at war.
        public bool HasWars(Alliance alliance){if(alliance==null||!alliance.Alive)return false;foreach(var realm in alliance.Members)if(HasWars(realm))return true;return false;}
        // The two end up in one alliance: one joins the other's, or they found one. Null when they cannot (at war with each other, gone, the same).
        public Alliance ForceAlliance(Realm a,Realm b)
        {
            if(a.IsGone()||b.IsGone()||a==b||a.IsAtWarWith(b))return null;
            if(a.HasAlliance()&&b.HasAlliance())return a.Alliance==b.Alliance?a.Alliance:null;
            if(b.HasAlliance()){WorldOps.JoinAlliance(this,b.Alliance,a);return b.Alliance;}
            if(a.HasAlliance()){WorldOps.JoinAlliance(this,a.Alliance,b);return a.Alliance;}
            return WorldOps.FormAlliance(this,a,b);
        }
        public void EndWar(War war,WarOutcome outcome=WarOutcome.Peace){if(war!=null&&war.Alive)WorldOps.EndWar(this,war,outcome);}

        // Stateless deterministic randomness: the same world seed, stream, month and
        // subject always give the same draw, independent of iteration order or saves.
        public double Chance(uint stream,int subject,int salt=0)
        {
            unchecked
            {
                ulong h=Seed^((ulong)stream*0x9E3779B97F4A7C15UL)^((ulong)(uint)Month<<32)^(uint)subject^((ulong)(uint)salt*0xC2B2AE3D27D4EB4FUL);
                h^=h>>30;h*=0xBF58476D1CE4E5B9UL;h^=h>>27;h*=0x94D049BB133111EBUL;h^=h>>31;
                return (h>>11)*(1.0/9007199254740992.0);
            }
        }
        public int Pick(uint stream,int subject,int count,int salt=0)=>count<=0?-1:Math.Min(count-1,(int)(Chance(stream,subject,salt)*count));

        // Great-circle-ish tile distance in meridional tile units (longitude wraps).
        public float Distance(int a,int b)
        {
            int w=Map.Width;int ax=a%w,ay=a/w,bx=b%w,by=b/w;
            int dx=Math.Abs(ax-bx);dx=Math.Min(dx,w-dx);
            float lat=(float)(((ay+by)*.5+.5)/Map.Height-.5)*(float)Math.PI;
            float sx=dx*(float)Math.Cos(lat),sy=ay-by;
            return (float)Math.Sqrt(sx*sx+sy*sy);
        }

        // Land a city can hold: dry ground that is not permanent ice or high summit.
        public bool Settleable(int tile)=>Map.Habitable(tile)&&Map.Water[tile]==WaterKind.Land;
        public bool Claimable(int tile)=>tile>=0&&(Map.Water[tile]==WaterKind.Land||Map.Water[tile]==WaterKind.River);
    }

    public enum AnnalKind : byte { RealmFounded, CityFounded, CityTransferred, RealmEnded, Accession, RulerDied, CapitalMoved, Secession, NewDynasty, WarStarted, WarEnded, AllianceFormed, AllianceEnded }

    public readonly struct AnnalEntry
    {
        public readonly int Month;public readonly AnnalKind Kind;public readonly int A,B,C;
        public AnnalEntry(int month,AnnalKind kind,int a,int b,int c){Month=month;Kind=kind;A=a;B=b;C=c;}
    }

    public sealed class Annals
    {
        public readonly List<AnnalEntry> Entries=new List<AnnalEntry>();
        public void Add(EmpireWorld w,AnnalKind kind,int a=-1,int b=-1,int c=-1)=>Entries.Add(new AnnalEntry(w.Month,kind,a,b,c));

        public static string Format(EmpireWorld w,AnnalEntry e)
        {
            string when=(w.StartYear+e.Month/12)+"年"+(e.Month%12+1)+"月";
            string R(int id)=>id>=0&&id<w.Realms.Count?w.Realms[id].Name:"?";
            string C(int id)=>id>=0&&id<w.Cities.Count?w.Cities[id].Name:"?";
            string P(int id)=>id>=0&&id<w.People.Count?w.FullName(w.People[id]):"?";
            string W(int id)=>id>=0&&id<w.Wars.Count?w.Wars[id].Name:"?";
            switch(e.Kind)
            {
                case AnnalKind.RealmFounded: return when+"，"+P(e.B)+"据"+C(e.C)+"立"+R(e.A)+"。";
                case AnnalKind.CityFounded: return when+"，"+R(e.B)+"筑"+C(e.A)+"城。";
                case AnnalKind.CityTransferred: return when+"，"+C(e.A)+"自"+R(e.B)+"归于"+R(e.C)+"。";
                case AnnalKind.RealmEnded: return when+"，"+R(e.A)+"亡。";
                case AnnalKind.Accession: return when+"，"+P(e.B)+"继"+R(e.A)+"之位。";
                case AnnalKind.RulerDied: return when+"，"+R(e.A)+"之主"+P(e.B)+"卒，年"+e.C+"。";
                case AnnalKind.CapitalMoved: return when+"，"+R(e.A)+"迁都"+C(e.B)+"。";
                case AnnalKind.Secession: return when+"，"+C(e.B)+"脱离"+R(e.C)+"，自立为"+R(e.A)+"。";
                case AnnalKind.WarStarted: return when+"，"+R(e.B)+"兴兵伐"+R(e.C)+"。";
                case AnnalKind.WarEnded: return when+"，"+W(e.A)+"罢兵。";
                case AnnalKind.AllianceFormed: return when+"，"+R(e.B)+"与"+R(e.C)+"结盟。";
                case AnnalKind.AllianceEnded: return when+"，"+(e.A>=0&&e.A<w.Alliances.Count?w.Alliances[e.A].Name:"?")+"散。";
                case AnnalKind.NewDynasty: return when+"，"+R(e.A)+"易姓，"+P(e.B)+"立"+(e.C>=0&&e.C<w.Clans.Count?w.Clans[e.C].Surname:"")+"氏之朝。";
                default: return when+"。";
            }
        }
    }
}
