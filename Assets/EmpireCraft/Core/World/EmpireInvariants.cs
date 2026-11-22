using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Whole-world consistency checks. Run after a tick (when thrones are filled);
    // returns human-readable violations, empty when the world is consistent.
    public static class EmpireInvariants
    {
        public static List<string> Check(EmpireWorld w,int limit=20)
        {
            var errors=new List<string>();
            void Fail(string s){if(errors.Count<limit)errors.Add(s);}
            var tileCount=new int[w.Cities.Count];
            for(int t=0;t<w.TileCity.Length;t++)
            {
                int c=w.TileCity[t];if(c<0)continue;
                if(c>=w.Cities.Count||!w.Cities[c].Alive){Fail("tile "+t+" owned by dead/unknown city "+c);continue;}
                if(!w.Claimable(t))Fail("city "+c+" holds water tile "+t);
                tileCount[c]++;
            }
            foreach(var c in w.Cities)
            {
                if(!c.Alive){if(c.Tiles.Count>0)Fail("dead city "+c.Id+" keeps tiles");continue;}
                if(tileCount[c.Id]!=c.Tiles.Count)Fail("city "+c.Id+" tile list "+c.Tiles.Count+" != map "+tileCount[c.Id]);
                if(w.TileCity[c.Tile]!=c.Id)Fail("city "+c.Id+" does not hold its seat tile");
                if(c.Realm<0||c.Realm>=w.Realms.Count||!w.Realms[c.Realm].Alive)Fail("city "+c.Id+" belongs to no living realm");
                else if(!w.Realms[c.Realm].Cities.Contains(c.Id))Fail("realm "+c.Realm+" does not list city "+c.Id);
                if(!(c.Population>=0)||double.IsInfinity(c.Population))Fail("city "+c.Id+" population "+c.Population);
            }
            var rulers=new HashSet<int>();
            foreach(var r in w.Realms)
            {
                if(!r.Alive){if(r.Cities.Count>0||r.Ruler>=0)Fail("ended realm "+r.Id+" keeps cities or ruler");continue;}
                if(r.Cities.Count==0)Fail("living realm "+r.Id+" has no city");
                foreach(int c in r.Cities)if(c<0||c>=w.Cities.Count||w.Cities[c].Realm!=r.Id||!w.Cities[c].Alive)Fail("realm "+r.Id+" lists foreign city "+c);
                if(r.Capital<0||!r.Cities.Contains(r.Capital))Fail("realm "+r.Id+" capital "+r.Capital+" is not its city");
                if(r.Ruler<0||r.Ruler>=w.People.Count||!w.People[r.Ruler].Alive)Fail("realm "+r.Id+" has no living ruler");
                else if(!rulers.Add(r.Ruler))Fail("person "+r.Ruler+" rules two realms");
                else if(w.People[r.Ruler].Clan!=r.RulingClan)Fail("realm "+r.Id+" ruling clan mismatch");
            }
            int living=0;
            foreach(var p in w.People)
            {
                if(p.Alive)living++;
                if(p.Spouse>=0){var s=w.People[p.Spouse];if(!p.Alive||!s.Alive||s.Spouse!=p.Id)Fail("broken marriage "+p.Id+"-"+p.Spouse);}
                if(p.Father>=0&&!w.People[p.Father].Children.Contains(p.Id))Fail("father of "+p.Id+" does not list the child");
                if(p.Mother>=0&&!w.People[p.Mother].Children.Contains(p.Id))Fail("mother of "+p.Id+" does not list the child");
                if(p.Father>=0&&w.People[p.Father].BirthMonth>p.BirthMonth-12*12)Fail("father of "+p.Id+" younger than 12 at the birth");
                if(p.Clan<0||p.Clan>=w.Clans.Count)Fail("person "+p.Id+" without clan");
            }
            if(living!=w.Living.Count)Fail("living index "+w.Living.Count+" != "+living);
            return errors;
        }
    }
}
