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
        public const int Version=1;
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

        public static byte[] Encode(EmpireWorld w)
        {
            using var stream=new MemoryStream();using var b=new BinaryWriter(stream,Encoding.UTF8);
            b.Write(Magic);b.Write(Version);
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
            Check(stream.Position==stream.Length,"Trailing bytes in EmpireCraft section");
            var errors=EmpireInvariants.Check(w,1);
            Check(errors.Count==0,"Inconsistent EmpireCraft section: "+(errors.Count>0?errors[0]:""));
            return w;
        }

        static void Check(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
        static int Count(BinaryReader b){int n=b.ReadInt32();Check(n>=0&&n<=MaxEntities,"Invalid count "+n);return n;}
        static int Id(BinaryReader b,int bound){int n=b.ReadInt32();Check(n>=0&&n<bound,"Invalid reference "+n);return n;}
        static string Text(BinaryReader b){string s=b.ReadString();Check(s.Length<=MaxString,"String too long");return s;}
    }
}
