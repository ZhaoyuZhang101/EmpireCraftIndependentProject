using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // The only mutators of polity state. Each call either completes and leaves
    // EmpireInvariants satisfied (given a ruler is assigned before the tick ends)
    // or throws without changing anything.
    public static class WorldOps
    {
        static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        static Realm LiveRealm(EmpireWorld w,int id){Require(id>=0&&id<w.Realms.Count&&w.Realms[id].Alive,"No living realm "+id);return w.Realms[id];}
        static City LiveCity(EmpireWorld w,int id){Require(id>=0&&id<w.Cities.Count&&w.Cities[id].Alive,"No living city "+id);return w.Cities[id];}
        static Person LivePerson(EmpireWorld w,int id){Require(id>=0&&id<w.People.Count&&w.People[id].Alive,"No living person "+id);return w.People[id];}

        public static int NewClan(EmpireWorld w,string surname,int founder=-1)
        {
            Require(!string.IsNullOrEmpty(surname),"Clan needs a surname");
            var c=new Clan{Id=w.Clans.Count,Surname=surname,Founder=founder,FoundedMonth=w.Month};w.Clans.Add(c);return c.Id;
        }

        public static int Birth(EmpireWorld w,string given,Sex sex,int clan,int city,int father=-1,int mother=-1,int ageYears=0)
        {
            Require(clan>=0&&clan<w.Clans.Count,"Unknown clan");
            Require(city<0||(city<w.Cities.Count&&w.Cities[city].Alive),"Birth city must be alive");
            if(father>=0)Require(w.People[father].Sex==Sex.Male,"Father must be male");
            if(mother>=0)Require(w.People[mother].Sex==Sex.Female,"Mother must be female");
            var p=new Person{Id=w.People.Count,Given=given,Sex=sex,Clan=clan,City=city,Father=father,Mother=mother,BirthMonth=w.Month-ageYears*12};
            w.People.Add(p);w.Living.Add(p.Id);
            if(father>=0)w.People[father].Children.Add(p.Id);
            if(mother>=0)w.People[mother].Children.Add(p.Id);
            return p.Id;
        }

        public static void Marry(EmpireWorld w,int a,int b)
        {
            var x=LivePerson(w,a);var y=LivePerson(w,b);
            Require(x.Sex!=y.Sex&&x.Spouse<0&&y.Spouse<0,"Marriage needs two unmarried people of opposite sex");
            x.Spouse=b;y.Spouse=a;
        }

        // Death clears the spouse link's survivor side and vacates any throne.
        public static void Death(EmpireWorld w,int person)
        {
            var p=LivePerson(w,person);
            p.DeathMonth=w.Month;w.Living.Remove(person);
            if(p.Spouse>=0){w.People[p.Spouse].Spouse=-1;p.Spouse=-1;}
            foreach(var r in w.Realms)if(r.Alive&&r.Ruler==person){r.Ruler=-1;r.PreviousRuler=person;w.Annals.Add(w,AnnalKind.RulerDied,r.Id,person,w.AgeOf(p));}
        }

        public static void SetRuler(EmpireWorld w,int realm,int person)
        {
            var r=LiveRealm(w,realm);var p=LivePerson(w,person);
            foreach(var other in w.Realms)Require(!(other.Alive&&other.Id!=realm&&other.Ruler==person),"Person already rules another realm");
            bool newHouse=r.RulingClan>=0&&r.RulingClan!=p.Clan;
            r.Ruler=person;r.RulingClan=p.Clan;
            if(newHouse)w.Annals.Add(w,AnnalKind.NewDynasty,realm,person,p.Clan);
            else w.Annals.Add(w,AnnalKind.Accession,realm,person);
        }

        public static int FoundCity(EmpireWorld w,int tile,int realm,string name)
        {
            Require(tile>=0&&tile<w.TileCity.Length&&w.Settleable(tile)&&w.TileCity[tile]<0,"City site must be free settleable land");
            var r=LiveRealm(w,realm);
            var c=new City{Id=w.Cities.Count,Name=name,Tile=tile,Realm=realm,FoundedMonth=w.Month,Population=200};
            w.Cities.Add(c);c.Tiles.Add(tile);w.TileCity[tile]=c.Id;r.Cities.Add(c.Id);
            if(r.Capital<0)r.Capital=c.Id;
            w.Annals.Add(w,AnnalKind.CityFounded,c.Id,realm);
            return c.Id;
        }

        // A new realm is founded at a city (taking it from its current realm, if any).
        public static int FoundRealm(EmpireWorld w,string name,uint color,int capitalCity,int ruler)
        {
            var city=LiveCity(w,capitalCity);LivePerson(w,ruler);
            foreach(var other in w.Realms)Require(!(other.Alive&&other.Ruler==ruler),"Ruler already rules a realm");
            var r=new Realm{Id=w.Realms.Count,Name=name,Color=color,FoundedMonth=w.Month};
            w.Realms.Add(r);
            int previous=city.Realm;
            if(previous>=0)Detach(w,city);
            city.Realm=r.Id;r.Cities.Add(city.Id);r.Capital=city.Id;
            r.Ruler=ruler;r.RulingClan=w.People[ruler].Clan;
            w.Annals.Add(w,previous>=0?AnnalKind.Secession:AnnalKind.RealmFounded,r.Id,previous>=0?city.Id:ruler,previous>=0?previous:city.Id);
            if(previous>=0)AfterLoss(w,previous);
            return r.Id;
        }

        // Founding a realm on a city site that has no city yet.
        public static int FoundRealmAt(EmpireWorld w,string name,uint color,int tile,string cityName,int ruler)
        {
            Require(tile>=0&&tile<w.TileCity.Length&&w.Settleable(tile)&&w.TileCity[tile]<0,"Realm seat must be free settleable land");
            LivePerson(w,ruler);
            var r=new Realm{Id=w.Realms.Count,Name=name,Color=color,FoundedMonth=w.Month,Ruler=ruler,RulingClan=w.People[ruler].Clan};
            w.Realms.Add(r);
            var c=new City{Id=w.Cities.Count,Name=cityName,Tile=tile,Realm=r.Id,FoundedMonth=w.Month,Population=200};
            w.Cities.Add(c);c.Tiles.Add(tile);w.TileCity[tile]=c.Id;r.Cities.Add(c.Id);r.Capital=c.Id;
            if(w.People[ruler].City<0)w.People[ruler].City=c.Id;
            w.Annals.Add(w,AnnalKind.RealmFounded,r.Id,ruler,c.Id);
            return r.Id;
        }

        public static void TransferCity(EmpireWorld w,int city,int toRealm)
        {
            var c=LiveCity(w,city);var to=LiveRealm(w,toRealm);
            Require(c.Realm!=toRealm,"City already belongs to that realm");
            int from=c.Realm;Detach(w,c);
            c.Realm=toRealm;to.Cities.Add(city);if(to.Capital<0)to.Capital=city;
            w.Annals.Add(w,AnnalKind.CityTransferred,city,from,toRealm);
            AfterLoss(w,from);
        }

        public static void MoveCapital(EmpireWorld w,int realm,int city)
        {
            var r=LiveRealm(w,realm);var c=LiveCity(w,city);
            Require(c.Realm==realm,"Capital must be one of the realm's cities");
            if(r.Capital==city)return;
            r.Capital=city;w.Annals.Add(w,AnnalKind.CapitalMoved,realm,city);
        }

        public static void ClaimTile(EmpireWorld w,int city,int tile)
        {
            var c=LiveCity(w,city);
            Require(w.Claimable(tile)&&w.TileCity[tile]<0,"Tile must be free land");
            bool adjacent=false;
            for(int d=0;d<4&&!adjacent;d++){int n=w.Map.Neighbor(tile,d);adjacent=n>=0&&w.TileCity[n]==city;}
            Require(adjacent,"A city grows only into tiles adjacent to its territory");
            w.TileCity[tile]=city;c.Tiles.Add(tile);
        }

        public static void EndRealm(EmpireWorld w,int realm)
        {
            var r=LiveRealm(w,realm);
            Require(r.Cities.Count==0,"A realm ends only after losing its last city");
            r.EndedMonth=w.Month;r.Ruler=-1;r.Capital=-1;
            w.Annals.Add(w,AnnalKind.RealmEnded,realm);
        }

        static void Detach(EmpireWorld w,City c)
        {
            if(c.Realm<0)return;var r=w.Realms[c.Realm];
            r.Cities.Remove(c.Id);
            if(r.Capital==c.Id)r.Capital=-1;
        }

        // A realm that lost a city re-seats its capital on its most populous city,
        // or ends when nothing is left.
        static void AfterLoss(EmpireWorld w,int realm)
        {
            if(realm<0)return;var r=w.Realms[realm];if(!r.Alive)return;
            if(r.Cities.Count==0){EndRealm(w,realm);return;}
            if(r.Capital>=0)return;
            int best=r.Cities[0];
            foreach(int c in r.Cities)if(w.Cities[c].Population>w.Cities[best].Population||(w.Cities[c].Population==w.Cities[best].Population&&c<best))best=c;
            r.Capital=best;w.Annals.Add(w,AnnalKind.CapitalMoved,realm,best);
        }
    }
}
