using System;
using System.IO;
using System.Text;
using Orbis.Flat;

namespace EmpireCraft.Core
{
    // Versioned binary section for EmpireCraft state, saved beside the ORBIS world
    // save (whose format stays untouched). It is bound to its map by size, seed and
    // a bed fingerprint. Derived indices (tile owners, living list) are rebuilt.
    public static class EmpireSave
    {
        public const int Version=2;
        const uint Magic=0x44574345; // "ECWD"
        const int MaxEntities=4_000_000,MaxString=256;

        public static ulong MapFingerprint(WorldData map)
        {
            unchecked
            {
                ulong h=1469598103934665603UL;
                void Mix(ulong v){h^=v;h*=1099511628211UL;}
                Mix((ulong)map.Width);Mix((ulong)map.Height);
                for(int i=0;i<map.Count;i++)Mix((ushort)map.Elevation[i]);
                return h;
            }
        }

        // `version` below the current one writes that older layout (used to check old sections still load).
        public static byte[] Encode(EmpireWorld w,int version=Version)
        {
            using var stream=new MemoryStream();using var b=new BinaryWriter(stream,Encoding.UTF8);
            if(version<1||version>Version)throw new ArgumentOutOfRangeException(nameof(version));
            b.Write(Magic);b.Write(version);
            b.Write(w.Map.Seed??"");b.Write(w.Map.Width);b.Write(w.Map.Height);b.Write(MapFingerprint(w.Map));
            b.Write(w.Seed);b.Write(w.StartYear);b.Write(w.Month);
            b.Write(w.Clans.Count);
            foreach(var c in w.Clans){b.Write(c.Surname);b.Write(c.Founder);b.Write(c.FoundedMonth);}
            b.Write(w.People.Count);
            foreach(var p in w.People)
            {
                b.Write(p.Given);b.Write((byte)p.Sex);b.Write(p.BirthMonth);b.Write(p.DeathMonth);
                b.Write(p.Father);b.Write(p.Mother);b.Write(p.Spouse);b.Write(p.Clan);b.Write(p.City);
                b.Write(p.Children.Count);foreach(int c in p.Children)b.Write(c);
            }
            b.Write(w.Cities.Count);
            foreach(var c in w.Cities)
            {
                b.Write(c.Name);b.Write(c.Tile);b.Write(c.Realm);b.Write(c.Population);b.Write(c.FoundedMonth);b.Write(c.EndedMonth);
                b.Write(c.Tiles.Count);foreach(int t in c.Tiles)b.Write(t);
            }
            b.Write(w.Realms.Count);
            foreach(var r in w.Realms)
            {
                b.Write(r.Name);b.Write(r.Color);b.Write(r.Capital);b.Write(r.Ruler);b.Write(r.RulingClan);b.Write(r.PreviousRuler);
                b.Write(r.FoundedMonth);b.Write(r.EndedMonth);
                b.Write(r.Cities.Count);foreach(int c in r.Cities)b.Write(c);
            }
            b.Write(w.Annals.Entries.Count);
            foreach(var e in w.Annals.Entries){b.Write(e.Month);b.Write((byte)e.Kind);b.Write(e.A);b.Write(e.B);b.Write(e.C);}
            if(version<2){b.Flush();return stream.ToArray();}
            // Version 2: the books kept by RealmAccounts, one account per city and realm id.
            w.Ledger.Ensure(w.Cities.Count,w.Realms.Count);
            foreach(var c in w.Ledger.Cities){b.Write(c.Stability);b.Write(c.Corruption);b.Write(c.TaxCarry);b.Write(c.GovernanceCarry);b.Write(c.GarrisonCarry);b.Write(c.Garrison);b.Write(c.UnrestMonths);}
            foreach(var r in w.Ledger.Realms)
            {
                b.Write(r.Treasury);b.Write(r.Dynasty);b.Write(r.DynastyStart);b.Write(r.Pressure);b.Write(r.SaturatedYears);b.Write(r.Corruption);
                b.Write(r.Legitimacy);b.Write(r.InArrears);b.Write(r.Unpaid);
                b.Write(r.Books.started);var periods=r.Books.periods??new System.Collections.Generic.List<TreasuryPeriod>();b.Write(periods.Count);
                foreach(var p in periods){b.Write(p.timestamp);Amounts(b,p.income);Amounts(b,p.expense);Amounts(b,p.tax_transfer_income);}
            }
            b.Flush();return stream.ToArray();
        }

        public static EmpireWorld Decode(byte[] bytes,WorldData map)
        {
            using var stream=new MemoryStream(bytes);using var b=new BinaryReader(stream,Encoding.UTF8);
            Check(b.ReadUInt32()==Magic,"Not an EmpireCraft section");
            int version=b.ReadInt32();Check(version>=1&&version<=Version,"Unsupported EmpireCraft save version "+version);
            string mapSeed=b.ReadString();int width=b.ReadInt32(),height=b.ReadInt32();ulong fingerprint=b.ReadUInt64();
            Check(width==map.Width&&height==map.Height&&mapSeed==(map.Seed??"")&&fingerprint==MapFingerprint(map),"EmpireCraft section belongs to a different map");
            var w=new EmpireWorld(map,b.ReadUInt64()){StartYear=b.ReadInt32(),Month=b.ReadInt32()};
            int clans=Count(b);
            for(int i=0;i<clans;i++)w.Clans.Add(new Clan{Id=i,Surname=Text(b),Founder=b.ReadInt32(),FoundedMonth=b.ReadInt32()});
            int people=Count(b);
            for(int i=0;i<people;i++)
            {
                var p=new Person{Id=i,Given=Text(b),Sex=(Sex)b.ReadByte(),BirthMonth=b.ReadInt32(),DeathMonth=b.ReadInt32(),
                    Father=b.ReadInt32(),Mother=b.ReadInt32(),Spouse=b.ReadInt32(),Clan=b.ReadInt32(),City=b.ReadInt32()};
                int children=Count(b);for(int k=0;k<children;k++)p.Children.Add(Id(b,people));
                w.People.Add(p);if(p.Alive)w.Living.Add(i);
            }
            int cities=Count(b);
            for(int i=0;i<cities;i++)
            {
                var c=new City{Id=i,Name=Text(b),Tile=b.ReadInt32(),Realm=b.ReadInt32(),Population=b.ReadDouble(),FoundedMonth=b.ReadInt32(),EndedMonth=b.ReadInt32()};
                int tiles=Count(b);
                for(int k=0;k<tiles;k++){int t=Id(b,map.Count);Check(w.TileCity[t]<0,"Tile claimed twice");w.TileCity[t]=i;c.Tiles.Add(t);}
                w.Cities.Add(c);
            }
            int realms=Count(b);
            for(int i=0;i<realms;i++)
            {
                var r=new Realm{Id=i,Name=Text(b),Color=b.ReadUInt32(),Capital=b.ReadInt32(),Ruler=b.ReadInt32(),RulingClan=b.ReadInt32(),PreviousRuler=b.ReadInt32(),
                    FoundedMonth=b.ReadInt32(),EndedMonth=b.ReadInt32()};
                int list=Count(b);for(int k=0;k<list;k++)r.Cities.Add(Id(b,cities));
                w.Realms.Add(r);
            }
            int entries=Count(b);
            for(int i=0;i<entries;i++)
            {
                int month=b.ReadInt32();byte kind=b.ReadByte();Check(kind<=(byte)AnnalKind.NewDynasty,"Unknown annal kind");
                w.Annals.Entries.Add(new AnnalEntry(month,(AnnalKind)kind,b.ReadInt32(),b.ReadInt32(),b.ReadInt32()));
            }
            if(version>=2)
            {
                w.Ledger.Ensure(cities,realms);
                foreach(var c in w.Ledger.Cities){c.Stability=b.ReadSingle();c.Corruption=b.ReadSingle();c.TaxCarry=b.ReadDouble();c.GovernanceCarry=b.ReadDouble();c.GarrisonCarry=b.ReadDouble();c.Garrison=b.ReadInt32();c.UnrestMonths=b.ReadInt32();}
                foreach(var r in w.Ledger.Realms)
                {
                    r.Treasury=b.ReadInt64();r.Dynasty=b.ReadInt32();r.DynastyStart=b.ReadInt32();r.Pressure=b.ReadSingle();r.SaturatedYears=b.ReadSingle();r.Corruption=b.ReadSingle();
                    r.Legitimacy=b.ReadInt32();r.InArrears=b.ReadBoolean();r.Unpaid=b.ReadInt64();
                    r.Books.started=b.ReadDouble();int periods=Count(b);Check(periods<=64,"Too many treasury periods");
                    for(int k=0;k<periods;k++)
                    {
                        var p=new TreasuryPeriod{timestamp=b.ReadDouble()};Amounts(b,p.income);Amounts(b,p.expense);Amounts(b,p.tax_transfer_income);
                        r.Books.periods.Add(p);
                    }
                }
            }
            Check(stream.Position==stream.Length,"Trailing bytes in EmpireCraft section");
            var errors=EmpireInvariants.Check(w,1);
            Check(errors.Count==0,"Inconsistent EmpireCraft section: "+(errors.Count>0?errors[0]:""));
            return w;
        }

        static void Check(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
        // Category totals in key order, so equal books always encode to equal bytes.
        static void Amounts(BinaryWriter b,System.Collections.Generic.Dictionary<string,long> amounts)
        {
            if(amounts==null){b.Write(0);return;}
            var keys=new System.Collections.Generic.List<string>(amounts.Keys);keys.Sort(StringComparer.Ordinal);
            b.Write(keys.Count);foreach(string key in keys){b.Write(key);b.Write(amounts[key]);}
        }
        static void Amounts(BinaryReader b,System.Collections.Generic.Dictionary<string,long> amounts)
        {
            int n=Count(b);Check(n<=256,"Too many treasury categories");
            for(int i=0;i<n;i++){string key=Text(b);amounts[key]=b.ReadInt64();}
        }
        static int Count(BinaryReader b){int n=b.ReadInt32();Check(n>=0&&n<=MaxEntities,"Invalid count "+n);return n;}
        static int Id(BinaryReader b,int bound){int n=b.ReadInt32();Check(n>=0&&n<bound,"Invalid reference "+n);return n;}
        static string Text(BinaryReader b){string s=b.ReadString();Check(s.Length<=MaxString,"String too long");return s;}
    }
}
