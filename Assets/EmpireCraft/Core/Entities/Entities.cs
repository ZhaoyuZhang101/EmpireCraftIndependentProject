using System.Collections.Generic;

namespace EmpireCraft.Core
{
    public enum Sex : byte { Male, Female }

    // Core identity records. Systems keep their own state in their own tables
    // keyed by these ids (ids are list indices and are never reused).
    public sealed class Realm
    {
        public int Id;
        public string Name;
        public uint Color;              // 0xRRGGBB
        public int Capital=-1;          // city id
        public int Ruler=-1;            // person id
        public int RulingClan=-1;       // clan id of the reigning house
        public int PreviousRuler=-1;    // last ruler, kept for succession
        public int FoundedMonth;
        public int EndedMonth=-1;
        public readonly List<int> Cities=new List<int>();
        public bool Alive=>EndedMonth<0;
    }

    public sealed class City
    {
        public int Id;
        public string Name;
        public int Tile;                // seat tile on the ORBIS grid
        public int Realm=-1;
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
        public int Father=-1,Mother=-1,Spouse=-1;
        public int Clan=-1;
        public int City=-1;
        public readonly List<int> Children=new List<int>();
        public bool Alive=>DeathMonth<0;
    }

    public sealed class Clan
    {
        public int Id;
        public string Surname;
        public int Founder=-1;
        public int FoundedMonth;
    }
}
