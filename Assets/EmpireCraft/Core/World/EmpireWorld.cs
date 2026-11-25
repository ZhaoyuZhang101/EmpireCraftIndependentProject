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
        public readonly List<int> Living=new List<int>();   // living person ids, insertion order
        public readonly int[] TileCity;                        // city id per tile, -1 unclaimed
        public readonly Annals Annals=new Annals();
        public readonly RealmLedger Ledger=new RealmLedger();   // books kept by RealmAccounts
        public readonly ulong Seed;
        public int StartYear=1;
        public int Month;                                     // months since the start, 0-based

        public EmpireWorld(WorldData map,ulong seed)
        {
            Map=map??throw new ArgumentNullException(nameof(map));Seed=seed;
            TileCity=new int[map.Count];for(int i=0;i<TileCity.Length;i++)TileCity[i]=-1;
        }

        public int Year=>StartYear+Month/12;
        public int MonthOfYear=>Month%12+1;
        public int AgeOf(Person p)=>((p.Alive?Month:p.DeathMonth)-p.BirthMonth)/12;
        public int RealmOfTile(int tile){int c=TileCity[tile];return c<0?-1:Cities[c].Realm;}
        public string FullName(Person p)=>(p.Clan>=0?Clans[p.Clan].Surname:"")+p.Given;

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

    public enum AnnalKind : byte { RealmFounded, CityFounded, CityTransferred, RealmEnded, Accession, RulerDied, CapitalMoved, Secession, NewDynasty }

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
                case AnnalKind.NewDynasty: return when+"，"+R(e.A)+"易姓，"+P(e.B)+"立"+(e.C>=0&&e.C<w.Clans.Count?w.Clans[e.C].Surname:"")+"氏之朝。";
                default: return when+"。";
            }
        }
    }
}
