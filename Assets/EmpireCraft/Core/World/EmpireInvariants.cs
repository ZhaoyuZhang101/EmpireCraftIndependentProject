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
                if(c.Realm==null||c.Realm.Id>=w.Realms.Count||w.Realms[c.Realm.Id]!=c.Realm||!c.Realm.Alive)Fail("city "+c.Id+" belongs to no living realm");
                else if(!c.Realm.Cities.Contains(c))Fail("realm "+c.Realm.Id+" does not list city "+c.Id);
                if(!(c.Population>=0)||double.IsInfinity(c.Population))Fail("city "+c.Id+" population "+c.Population);
            }
            var rulers=new HashSet<Person>();
            for(int i=0;i<w.Realms.Count;i++)if(w.Realms[i].Id!=i)Fail("realm at "+i+" carries id "+w.Realms[i].Id);
            for(int i=0;i<w.Cities.Count;i++)if(w.Cities[i].Id!=i)Fail("city at "+i+" carries id "+w.Cities[i].Id);
            for(int i=0;i<w.People.Count;i++)if(w.People[i].Id!=i)Fail("person at "+i+" carries id "+w.People[i].Id);
            foreach(var r in w.Realms)
            {
                if(!r.Alive){if(r.Cities.Count>0||r.Ruler!=null)Fail("ended realm "+r.Id+" keeps cities or ruler");continue;}
                if(r.Cities.Count==0)Fail("living realm "+r.Id+" has no city");
                foreach(var c in r.Cities)if(c==null||c.Id>=w.Cities.Count||w.Cities[c.Id]!=c||c.Realm!=r||!c.Alive)Fail("realm "+r.Id+" lists foreign city "+c.IdOf());
                if(r.Capital==null||!r.Cities.Contains(r.Capital))Fail("realm "+r.Id+" capital "+r.Capital.IdOf()+" is not its city");
                if(r.Ruler==null||!r.Ruler.Alive||r.Ruler.Id>=w.People.Count||w.People[r.Ruler.Id]!=r.Ruler)Fail("realm "+r.Id+" has no living ruler");
                else if(!rulers.Add(r.Ruler))Fail("person "+r.Ruler.Id+" rules two realms");
                else if(r.Ruler.Clan!=r.RulingClan)Fail("realm "+r.Id+" ruling clan mismatch");
            }
            int alive=0;foreach(var r in w.Realms)if(r.Alive){if(alive>=w.LiveRealms.Count||w.LiveRealms[alive]!=r)Fail("living realm index misses realm "+r.Id);alive++;}
            if(alive!=w.LiveRealms.Count)Fail("living realm index "+w.LiveRealms.Count+" != "+alive);
            foreach(var r in w.Realms)if(r.Alive&&r.Ruler!=null&&r.Ruler.Throne!=r)Fail("ruler of realm "+r.Id+" does not hold its throne");
            foreach(var p in w.People)if(p.Throne!=null&&(!p.Alive||!p.Throne.Alive||p.Throne.Ruler!=p))Fail("person "+p.Id+" holds a throne that is not theirs");
            int running=0;
            foreach(var war in w.Wars)
            {
                if(!war.Alive)continue;
                if(running>=w.LiveWars.Count||w.LiveWars[running]!=war)Fail("running war index misses war "+war.Id);
                running++;
                if(war.Attackers.Count==0||war.Defenders.Count==0)Fail("war "+war.Id+" has an empty side");
                if(!war.Attackers.Contains(war.MainAttacker)||!war.Defenders.Contains(war.MainDefender))Fail("war "+war.Id+" is led from outside its sides");
                foreach(var r in war.Attackers)if(!r.Alive||!r.Wars.Contains(war)||war.Defenders.Contains(r))Fail("war "+war.Id+" attacker "+r.Id+" is out of step");
                foreach(var r in war.Defenders)if(!r.Alive||!r.Wars.Contains(war))Fail("war "+war.Id+" defender "+r.Id+" is out of step");
            }
            if(running!=w.LiveWars.Count)Fail("running war index "+w.LiveWars.Count+" != "+running);
            foreach(var r in w.Realms)foreach(var war in r.Wars)if(!r.Alive||!war.Alive||!war.HasRealm(r))Fail("realm "+r.Id+" lists a war it is not fighting");
            int living=0;
            foreach(var p in w.People)
            {
                if(p.Alive)living++;
                if(p.Spouse!=null){var s=p.Spouse;if(!p.Alive||!s.Alive||s.Spouse!=p)Fail("broken marriage "+p.Id+"-"+s.Id);}
                if(p.Father!=null&&!p.Father.Children.Contains(p))Fail("father of "+p.Id+" does not list the child");
                if(p.Mother!=null&&!p.Mother.Children.Contains(p))Fail("mother of "+p.Id+" does not list the child");
                if(p.Father!=null&&p.Father.BirthMonth>p.BirthMonth-12*12)Fail("father of "+p.Id+" younger than 12 at the birth");
                if(p.Clan==null||p.Clan.Id>=w.Clans.Count||w.Clans[p.Clan.Id]!=p.Clan)Fail("person "+p.Id+" without clan");
            }
            if(living!=w.Living.Count)Fail("living index "+w.Living.Count+" != "+living);
            // A faith or culture held is either on this world's record or has left it; never another world's.
            bool Foreign(Religion x)=>x!=null&&x.Alive&&w.Religions.Get(x.Key)!=x;
            bool Alien(Culture x)=>x!=null&&x.Alive&&w.Cultures.Get(x.Key)!=x;
            foreach(var r in w.Realms)if(Foreign(r.Religion)||Alien(r.Culture))Fail("realm "+r.Id+" holds a faith that is not on this world's record");
            foreach(var c in w.Cities)if(Foreign(c.Religion)||Alien(c.Culture))Fail("city "+c.Id+" holds a faith that is not on this world's record");
            foreach(var p in w.People)if(Foreign(p.Religion)||Alien(p.Culture))Fail("person "+p.Id+" holds a faith that is not on this world's record");
            return errors;
        }
    }
}
