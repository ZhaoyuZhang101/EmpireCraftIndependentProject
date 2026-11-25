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
        static Realm Live(EmpireWorld w,Realm r){Require(r!=null&&r.Alive&&r.Id<w.Realms.Count&&w.Realms[r.Id]==r,"No such living realm");return r;}
        static City Live(EmpireWorld w,City c){Require(c!=null&&c.Alive&&c.Id<w.Cities.Count&&w.Cities[c.Id]==c,"No such living city");return c;}
        static Person Live(EmpireWorld w,Person p){Require(p!=null&&p.Alive&&p.Id<w.People.Count&&w.People[p.Id]==p,"No such living person");return p;}

        public static Clan NewClan(EmpireWorld w,string surname,Person founder=null)
        {
            Require(!string.IsNullOrEmpty(surname),"Clan needs a surname");
            var c=new Clan{Id=w.Clans.Count,Surname=surname,Founder=founder,FoundedMonth=w.Month};w.Clans.Add(c);return c;
        }

        public static Person Birth(EmpireWorld w,string given,Sex sex,Clan clan,City city,Person father=null,Person mother=null,int ageYears=0)
        {
            Require(clan!=null&&clan.Id<w.Clans.Count&&w.Clans[clan.Id]==clan,"Unknown clan");
            Require(city==null||city.Alive,"Birth city must be alive");
            if(father!=null)Require(father.Sex==Sex.Male,"Father must be male");
            if(mother!=null)Require(mother.Sex==Sex.Female,"Mother must be female");
            var p=new Person{Id=w.People.Count,Given=given,Sex=sex,Clan=clan,City=city,Father=father,Mother=mother,BirthMonth=w.Month-ageYears*12};
            w.People.Add(p);w.Living.Add(p);
            if(father!=null)father.Children.Add(p);
            if(mother!=null)mother.Children.Add(p);
            return p;
        }

        public static void Marry(EmpireWorld w,Person a,Person b)
        {
            Live(w,a);Live(w,b);
            Require(a.Sex!=b.Sex&&a.Spouse==null&&b.Spouse==null,"Marriage needs two unmarried people of opposite sex");
            a.Spouse=b;b.Spouse=a;
        }

        // Death clears the spouse link's survivor side and vacates any throne.
        public static void Death(EmpireWorld w,Person p)
        {
            Live(w,p);
            p.DeathMonth=w.Month;w.Living.Remove(p);
            if(p.Spouse!=null){p.Spouse.Spouse=null;p.Spouse=null;}
            foreach(var r in w.Realms)if(r.Alive&&r.Ruler==p){r.Ruler=null;r.PreviousRuler=p;w.Annals.Add(w,AnnalKind.RulerDied,r.Id,p.Id,w.AgeOf(p));}
            p.Throne=null;
        }

        public static void SetRuler(EmpireWorld w,Realm r,Person p)
        {
            Live(w,r);Live(w,p);
            foreach(var other in w.Realms)Require(!(other.Alive&&other!=r&&other.Ruler==p),"Person already rules another realm");
            bool newHouse=r.RulingClan!=null&&r.RulingClan!=p.Clan;
            if(r.Ruler!=null)r.Ruler.Throne=null;
            r.Ruler=p;p.Throne=r;r.RulingClan=p.Clan;
            if(newHouse)w.Annals.Add(w,AnnalKind.NewDynasty,r.Id,p.Id,p.Clan.IdOf());
            else w.Annals.Add(w,AnnalKind.Accession,r.Id,p.Id);
        }

        public static City FoundCity(EmpireWorld w,int tile,Realm r,string name)
        {
            Require(tile>=0&&tile<w.TileCity.Length&&w.Settleable(tile)&&w.TileCity[tile]<0,"City site must be free settleable land");
            Live(w,r);
            var c=new City{Id=w.Cities.Count,World=w,Name=name,Tile=tile,Realm=r,FoundedMonth=w.Month,Population=200};
            w.Cities.Add(c);w.TerritoryRevision++;c.Tiles.Add(tile);w.TileCity[tile]=c.Id;r.Cities.Add(c);
            if(r.Capital==null)r.Capital=c;
            w.Annals.Add(w,AnnalKind.CityFounded,c.Id,r.Id);
            return c;
        }

        // A new realm is founded at a city (taking it from its current realm, if any).
        public static Realm FoundRealm(EmpireWorld w,string name,uint color,City city,Person ruler)
        {
            Live(w,city);Live(w,ruler);
            foreach(var other in w.Realms)Require(!(other.Alive&&other.Ruler==ruler),"Ruler already rules a realm");
            var r=new Realm{Id=w.Realms.Count,World=w,Name=name,Color=color,FoundedMonth=w.Month};
            w.Realms.Add(r);w.LiveRealms.Add(r);
            Realm previous=city.Realm;
            if(previous!=null)Detach(city);
            city.Realm=r;r.Cities.Add(city);r.Capital=city;w.TerritoryRevision++;
            r.Ruler=ruler;ruler.Throne=r;r.RulingClan=ruler.Clan;
            w.Annals.Add(w,previous!=null?AnnalKind.Secession:AnnalKind.RealmFounded,r.Id,previous!=null?city.Id:ruler.Id,previous!=null?previous.Id:city.Id);
            if(previous!=null)AfterLoss(w,previous);
            return r;
        }

        // Founding a realm on a city site that has no city yet.
        public static Realm FoundRealmAt(EmpireWorld w,string name,uint color,int tile,string cityName,Person ruler)
        {
            Require(tile>=0&&tile<w.TileCity.Length&&w.Settleable(tile)&&w.TileCity[tile]<0,"Realm seat must be free settleable land");
            Live(w,ruler);
            var r=new Realm{Id=w.Realms.Count,World=w,Name=name,Color=color,FoundedMonth=w.Month,Ruler=ruler,RulingClan=ruler.Clan};
            w.Realms.Add(r);w.LiveRealms.Add(r);ruler.Throne=r;
            var c=new City{Id=w.Cities.Count,World=w,Name=cityName,Tile=tile,Realm=r,FoundedMonth=w.Month,Population=200};
            w.Cities.Add(c);w.TerritoryRevision++;c.Tiles.Add(tile);w.TileCity[tile]=c.Id;r.Cities.Add(c);r.Capital=c;
            if(ruler.City==null)ruler.City=c;
            w.Annals.Add(w,AnnalKind.RealmFounded,r.Id,ruler.Id,c.Id);
            return r;
        }

        public static void TransferCity(EmpireWorld w,City c,Realm to)
        {
            Live(w,c);Live(w,to);
            Require(c.Realm!=to,"City already belongs to that realm");
            Realm from=c.Realm;Detach(c);
            c.Realm=to;to.Cities.Add(c);if(to.Capital==null)to.Capital=c;w.TerritoryRevision++;
            w.Annals.Add(w,AnnalKind.CityTransferred,c.Id,from.IdOf(),to.Id);
            AfterLoss(w,from);
        }

        public static void MoveCapital(EmpireWorld w,Realm r,City c)
        {
            Live(w,r);Live(w,c);
            Require(c.Realm==r,"Capital must be one of the realm's cities");
            if(r.Capital==c)return;
            r.Capital=c;w.Annals.Add(w,AnnalKind.CapitalMoved,r.Id,c.Id);
        }

        public static void ClaimTile(EmpireWorld w,City c,int tile)
        {
            Live(w,c);
            Require(w.Claimable(tile)&&w.TileCity[tile]<0,"Tile must be free land");
            bool adjacent=false;
            for(int d=0;d<4&&!adjacent;d++){int n=w.Map.Neighbor(tile,d);adjacent=n>=0&&w.TileCity[n]==c.Id;}
            Require(adjacent,"A city grows only into tiles adjacent to its territory");
            w.TileCity[tile]=c.Id;c.Tiles.Add(tile);w.TerritoryRevision++;
        }

        // A city gives up a tile of its territory, never its seat.
        public static void ReleaseTile(EmpireWorld w,City c,int tile)
        {
            Live(w,c);
            Require(tile>=0&&tile<w.TileCity.Length&&w.TileCity[tile]==c.Id,"Tile is not this city's");
            Require(tile!=c.Tile,"A city keeps its seat");
            w.TileCity[tile]=-1;c.Tiles.Remove(tile);w.TerritoryRevision++;
        }

        // The ruler steps down or is removed; the throne stays empty until SetRuler.
        public static void Dethrone(EmpireWorld w,Realm r)
        {
            Live(w,r);
            if(r.Ruler==null)return;
            r.PreviousRuler=r.Ruler;r.Ruler.Throne=null;r.Ruler=null;
        }

        public static void EndRealm(EmpireWorld w,Realm r)
        {
            Live(w,r);
            Require(r.Cities.Count==0,"A realm ends only after losing its last city");
            foreach(var war in r.Wars.ToArray())LeaveWar(w,war,r);
            if(r.Alliance!=null&&r.Alliance.Alive)LeaveAlliance(w,r.Alliance,r);
            if(r.Ruler!=null)r.Ruler.Throne=null;
            r.EndedMonth=w.Month;r.Ruler=null;r.Capital=null;w.LiveRealms.Remove(r);
            w.Annals.Add(w,AnnalKind.RealmEnded,r.Id);
        }

        // War. The sides live on the war and on each realm's own list; nothing else may change them.
        static War LiveWar(EmpireWorld w,War war){Require(war!=null&&war.Alive&&war.Id<w.Wars.Count&&w.Wars[war.Id]==war,"No such running war");return war;}

        // Declares war, or returns the war the two already fight against each other.
        public static War StartWar(EmpireWorld w,Realm attacker,Realm defender,WarKind kind=null,string name=null)
        {
            Live(w,attacker);Live(w,defender);
            Require(attacker!=defender,"A realm cannot fight itself");
            Require(!(attacker.HasAlliance()&&attacker.Alliance==defender.Alliance),"Allies cannot go to war with each other");
            foreach(var running in attacker.Wars){var enemies=running.EnemiesOf(attacker);if(enemies!=null&&enemies.Contains(defender))return running;}
            var war=new War{Id=w.Wars.Count,Kind=kind??WarKinds.Normal,MainAttacker=attacker,MainDefender=defender,StartedMonth=w.Month,
                Name=name??attacker.Name+"与"+defender.Name+"之战"};
            war.Attackers.Add(attacker);war.Defenders.Add(defender);
            w.Wars.Add(war);w.LiveWars.Add(war);attacker.Wars.Add(war);defender.Wars.Add(war);
            w.Annals.Add(w,AnnalKind.WarStarted,war.Id,attacker.Id,defender.Id);
            return war;
        }

        public static void JoinWar(EmpireWorld w,War war,Realm r,bool asAttacker)
        {
            LiveWar(w,war);Live(w,r);
            Require(!war.HasRealm(r),"Realm already fights in this war");
            (asAttacker?war.Attackers:war.Defenders).Add(r);r.Wars.Add(war);
        }

        // A realm that leaves hands its side's lead on; a side left empty has lost.
        public static void LeaveWar(EmpireWorld w,War war,Realm r)
        {
            LiveWar(w,war);
            bool attacker=war.IsAttacker(r);
            Require(attacker||war.IsDefender(r),"Realm does not fight in this war");
            var side=attacker?war.Attackers:war.Defenders;
            side.Remove(r);(attacker?war.PastAttackers:war.PastDefenders).Add(r);r.Wars.Remove(war);
            if(side.Count==0){EndWar(w,war,attacker?WarOutcome.Defenders:WarOutcome.Attackers);return;}
            if(attacker&&war.MainAttacker==r)war.MainAttacker=side[0];
            if(!attacker&&war.MainDefender==r)war.MainDefender=side[0];
        }

        public static void EndWar(EmpireWorld w,War war,WarOutcome outcome)
        {
            LiveWar(w,war);
            war.EndedMonth=w.Month;war.Outcome=outcome;
            foreach(var r in war.Attackers)r.Wars.Remove(war);
            foreach(var r in war.Defenders)r.Wars.Remove(war);
            w.LiveWars.Remove(war);
            w.Diplomacy.WarEnded(war);
            w.Annals.Add(w,AnnalKind.WarEnded,war.Id,(int)outcome);
        }

        // Alliances. Membership lives on the alliance and on each realm; nothing else may change it.
        static Alliance LiveAlliance(EmpireWorld w,Alliance a){Require(a!=null&&a.Alive&&a.Id<w.Alliances.Count&&w.Alliances[a.Id]==a,"No such standing alliance");return a;}

        // Two realms with no alliance and no war between them found one.
        public static Alliance FormAlliance(EmpireWorld w,Realm a,Realm b,string name=null)
        {
            Live(w,a);Live(w,b);
            Require(a!=b,"A realm cannot ally with itself");
            Require(!a.HasAlliance()&&!b.HasAlliance(),"Both realms must be free of alliances");
            Require(!a.IsAtWarWith(b),"Realms at war with each other cannot ally");
            var alliance=new Alliance{Id=w.Alliances.Count,Name=name??a.Name+"与"+b.Name+"之盟",Founder=a,FoundedMonth=w.Month};
            alliance.Members.Add(a);alliance.Members.Add(b);a.Alliance=alliance;b.Alliance=alliance;
            w.Alliances.Add(alliance);w.LiveAlliances.Add(alliance);
            w.Annals.Add(w,AnnalKind.AllianceFormed,alliance.Id,a.Id,b.Id);
            return alliance;
        }

        public static void JoinAlliance(EmpireWorld w,Alliance alliance,Realm r)
        {
            LiveAlliance(w,alliance);Live(w,r);
            Require(!r.HasAlliance(),"Realm already stands in an alliance");
            foreach(var member in alliance.Members)Require(!member.IsAtWarWith(r),"A realm at war with a member cannot join");
            alliance.Members.Add(r);r.Alliance=alliance;
        }

        // An alliance of one is none: it ends when its second-last member leaves.
        public static void LeaveAlliance(EmpireWorld w,Alliance alliance,Realm r)
        {
            LiveAlliance(w,alliance);
            Require(alliance.Has(r),"Realm does not stand in this alliance");
            alliance.Members.Remove(r);alliance.PastMembers.Add(r);r.Alliance=null;
            if(alliance.Members.Count<2)DissolveAlliance(w,alliance);
        }

        public static void DissolveAlliance(EmpireWorld w,Alliance alliance)
        {
            LiveAlliance(w,alliance);
            foreach(var member in alliance.Members){member.Alliance=null;alliance.PastMembers.Add(member);}
            alliance.Members.Clear();alliance.EndedMonth=w.Month;w.LiveAlliances.Remove(alliance);
            w.Annals.Add(w,AnnalKind.AllianceEnded,alliance.Id);
        }

        static void Detach(City c)
        {
            Realm r=c.Realm;if(r==null)return;
            r.Cities.Remove(c);
            if(r.Capital==c)r.Capital=null;
        }

        // A realm that lost a city re-seats its capital on its most populous city,
        // or ends when nothing is left.
        static void AfterLoss(EmpireWorld w,Realm r)
        {
            if(r==null||!r.Alive)return;
            if(r.Cities.Count==0){EndRealm(w,r);return;}
            if(r.Capital!=null)return;
            City best=r.Cities[0];
            foreach(var c in r.Cities)if(c.Population>best.Population||(c.Population==best.Population&&c.Id<best.Id))best=c;
            r.Capital=best;w.Annals.Add(w,AnnalKind.CapitalMoved,r.Id,best.Id);
        }
    }

    // The same operations as members of the entities, acting on the current world:
    // the form the ported systems call them in.
    public static class WorldActions
    {
        static EmpireWorld World=>EmpireWorld.Current??throw new InvalidOperationException("No current world");
        public static void SetRuler(this Realm realm,Person person,bool fromLoad=false)=>WorldOps.SetRuler(World,realm,person);
        public static void RemoveRuler(this Realm realm)=>WorldOps.Dethrone(World,realm);
        public static void SetCapital(this Realm realm,City city)=>WorldOps.MoveCapital(World,realm,city);
        public static void JoinAttackers(this War war,Realm realm){if(war.Alive&&!realm.IsGone()&&!war.HasRealm(realm))WorldOps.JoinWar(World,war,realm,true);}
        public static void JoinDefenders(this War war,Realm realm){if(war.Alive&&!realm.IsGone()&&!war.HasRealm(realm))WorldOps.JoinWar(World,war,realm,false);}
        // Joining answers whether the realm is in afterwards; a realm that cannot join (gone, allied elsewhere, at war with a member) is not.
        public static bool Join(this Alliance alliance,Realm realm,bool recalculate=true,bool forced=false)
        {
            if(alliance==null||!alliance.Alive||realm.IsGone())return false;
            if(alliance.Has(realm))return true;
            if(realm.HasAlliance())return false;
            foreach(var member in alliance.Members)if(member.IsAtWarWith(realm))return false;
            WorldOps.JoinAlliance(World,alliance,realm);return true;
        }
        public static void Leave(this Alliance alliance,Realm realm,bool recalculate=true){if(alliance!=null&&alliance.Alive&&alliance.Has(realm))WorldOps.LeaveAlliance(World,alliance,realm);}
        public static void Leave(this War war,Realm realm){if(war.Alive&&war.HasRealm(realm))WorldOps.LeaveWar(World,war,realm);}
        // Territory by cell. A cell that is not free land next to the city is left alone.
        public static void AddZone(this City city,Cell cell)
        {
            if(city==null||cell==null||!city.Alive||World.TileCity[cell.Index]>=0||!World.Claimable(cell.Index))return;
            foreach(var near in cell.Neighbours)if(World.TileCity[near.Index]==city.Id){WorldOps.ClaimTile(World,city,cell.Index);return;}
        }
        public static void RemoveZone(this City city,Cell cell){if(city!=null&&cell!=null&&city.Alive&&cell.Index!=city.Tile&&World.TileCity[cell.Index]==city.Id)WorldOps.ReleaseTile(World,city,cell.Index);}
        // A person dies by a system's hand. The cause is for whoever writes the biography.
        public static void Die(this Person person,bool destroy=false,DeathCause cause=DeathCause.Other,bool countDeath=true,bool announce=true){if(person!=null&&person.Alive)WorldOps.Death(World,person);}
        // A person goes over to another realm: home becomes its capital. A reigning ruler stays where the throne is.
        public static void SetRealm(this Person person,Realm realm){if(person!=null&&person.Alive&&person.Throne==null&&!realm.IsGone()&&realm.Capital!=null&&person.Realm!=realm)person.City=realm.Capital;}
        // A city stands up as a realm of its own under a leader who rules nothing yet; it is named after the city.
        public static Realm MakeOwnRealm(this City city,Person leader,bool rebellion=false)
        {
            if(city==null||!city.Alive||leader==null||!leader.Alive||leader.Throne!=null)return null;
            unchecked
            {
                uint h=(uint)city.Id*2654435761u^(uint)World.Realms.Count*40503u;
                uint color=(((h&0x7F)+0x60)<<16)|((((h>>8)&0x7F)+0x60)<<8)|(((h>>16)&0x7F)+0x60);
                var realm=WorldOps.FoundRealm(World,city.Name,color,city,leader);
                leader.City=city;
                return realm;
            }
        }
        public static void JoinCity(this Person person,City city){if(person!=null&&person.Alive&&(city==null||city.Alive))person.City=city;}
        public static void JoinRealm(this City city,Realm realm,bool captured=false,bool rebellion=false){if(city.Realm!=realm)WorldOps.TransferCity(World,city,realm);}
    }
}
