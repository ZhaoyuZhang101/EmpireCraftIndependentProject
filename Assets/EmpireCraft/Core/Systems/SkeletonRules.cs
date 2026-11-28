using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{
    // Phase-0 placeholder rules so the skeleton world turns: logistic population
    // on fertile land, hinterland growth, new cities, ageing, births, marriages,
    // succession and the occasional breakaway on a heirless death. Each part is
    // replaced by the ported EmpireCraft system for that area (population, clans,
    // succession, regimes). Kept deliberately simple and allocation-light.
    public static class SkeletonRules
    {
        const uint Stream=0x534B454Cu;

        public static void Month(EmpireWorld w)
        {
            w.Month++;
            if(!PopulationElsewhere)Population(w);
            if(w.Month%12==0)Year(w);
            // The year's step fills the thrones its own deaths empty. A throne emptied in mid-year by
            // another system (an execution, a coup) is filled the same month.
            else Thrones(w);
        }

        // How many people a city's land can carry: every tile a little, fertile tiles much more.
        public static double Capacity(EmpireWorld w,City c){var m=w.Map;double capacity=0;foreach(int t in c.Tiles)capacity+=150+2600*m.Fertility[t];return capacity;}

        // True while another book keeps the population (the ported population system, see PopulationBridge): the
        // skeleton's own growth then gives way; its other rules still change head counts, which the bridge carries over.
        public static bool PopulationElsewhere;
        // True while the ported city expansion takes free land (CityExpansionPatch, a cell a month): the skeleton's own
        // hinterland growth then gives way.
        public static bool TerritoryElsewhere;
        // True while the ported state settlement founds new cities (StateSettlementSystem): the skeleton's own daughter
        // cities then give way.
        public static bool SettlementElsewhere;

        static void Population(EmpireWorld w)
        {
            var m=w.Map;
            foreach(var c in w.Cities)
            {
                if(!c.Alive)continue;
                double capacity=Capacity(w,c);
                double growth=.018/12*c.Population*(1-c.Population/Math.Max(1,capacity));
                c.Population=Math.Max(50,c.Population+growth);
            }
        }

        static void Year(EmpireWorld w)
        {
            if(!TerritoryElsewhere)Hinterland(w);
            if(!SettlementElsewhere)NewCities(w);
            Ageing(w);
            Families(w);
            Thrones(w);
        }

        // A city near capacity takes its best free neighbouring tile, within reach of its seat.
        static void Hinterland(EmpireWorld w)
        {
            var m=w.Map;int count=w.Cities.Count;
            for(int id=0;id<count;id++)
            {
                var c=w.Cities[id];if(!c.Alive)continue;
                double capacity=Capacity(w,c);
                if(c.Population<capacity*.75)continue;
                int best=-1;float bestScore=-1;
                foreach(int t in c.Tiles)
                    for(int d=0;d<4;d++)
                    {
                        int n=m.Neighbor(t,d);if(n<0||w.TileCity[n]>=0||!w.Claimable(n))continue;
                        float distance=w.Distance(c.Tile,n);if(distance>7)continue;
                        float s=m.Fertility[n]-distance*.04f;if(s>bestScore||(s==bestScore&&n<best)){bestScore=s;best=n;}
                    }
                if(best>=0)WorldOps.ClaimTile(w,c,best);
            }
        }

        // A populous realm plants a daughter city on good free land beyond its frontier.
        static void NewCities(EmpireWorld w)
        {
            int realms=w.Realms.Count;
            for(int r=0;r<realms;r++)
            {
                var realm=w.Realms[r];if(!realm.Alive)continue;
                double people=0;foreach(var c in realm.Cities)people+=c.Population;
                if(people<realm.Cities.Count*2200||w.Chance(Stream,r,1)>.12)continue;
                int best=-1;float bestScore=0;
                foreach(var city in realm.Cities)
                {
                    foreach(int t in city.Tiles)
                        for(int d=0;d<4;d++)
                        {
                            int n=w.Map.Neighbor(t,d);if(n<0||w.TileCity[n]>=0)continue;
                            float s=EmpireBootstrap.SiteScore(w,n);if(s<=bestScore)continue;
                            bool crowded=false;foreach(var other in w.Cities)if(other.Alive&&w.Distance(other.Tile,n)<5){crowded=true;break;}
                            if(!crowded){bestScore=s;best=n;}
                        }
                }
                if(best<0)continue;
                City planted=WorldOps.FoundCity(w,best,realm,Names.Place(w,best+w.Month,Names.PeopleOf(realm)));
                // Settlers come from the capital.
                var capital=realm.Capital;double settlers=Math.Min(400,capital.Population*.1);
                capital.Population-=settlers;planted.Population=settlers;
            }
        }

        static void Ageing(EmpireWorld w)
        {
            var dead=new List<Person>();
            foreach(var p in w.Living)
            {
                int age=w.AgeOf(p);
                double q=age<5?.03:Math.Min(.85,.0006*Math.Exp(.082*age));
                if(w.Chance(Stream,p.Id,2)<q)dead.Add(p);
            }
            foreach(var p in dead)WorldOps.Death(w,p);
        }

        // Only reigning houses are tracked as people (capped per house); everyone else
        // stays part of a city's population number.
        const int HouseCap=24;
        static void Families(EmpireWorld w)
        {
            var reigning=new Dictionary<int,int>();   // clan id -> living members
            foreach(var r in w.Realms)if(r.Alive&&r.RulingClan!=null)reigning[r.RulingClan.Id]=0;
            foreach(var p in w.Living){int clan=p.Clan.Id;if(reigning.ContainsKey(clan))reigning[clan]++;}
            int snapshot=w.Living.Count;
            for(int k=0;k<snapshot;k++)
            {
                var p=w.Living[k];int age=w.AgeOf(p);
                if(p.Sex!=Sex.Male||p.Spouse!=null||age<18||age>45||!reigning.TryGetValue(p.Clan.Id,out int members)||members>=HouseCap)continue;
                if(w.Chance(Stream,p.Id,3)>=.35)continue;
                string people=Names.PeopleOf(p.City);
                Clan clan=WorldOps.NewClan(w,Names.Surname(w,p.Id*13+w.Month,people));
                int wifeAge=Math.Max(16,age-2-w.Pick(Stream,p.Id,8,4));
                Person wife=WorldOps.Birth(w,Names.Given(w,Sex.Female,p.Id*13+w.Month+1,people),Sex.Female,clan,p.City,null,null,wifeAge);
                WorldOps.Marry(w,p,wife);
            }
            snapshot=w.Living.Count;
            for(int k=0;k<snapshot;k++)
            {
                var mother=w.Living[k];
                if(mother.Sex!=Sex.Female||mother.Spouse==null)continue;
                int age=w.AgeOf(mother);if(age<16||age>40)continue;
                var father=mother.Spouse;if(w.AgeOf(father)<16)continue;
                if(!reigning.TryGetValue(father.Clan.Id,out int members)||members>=HouseCap)continue;
                if(w.Chance(Stream,mother.Id,5)>.22)continue;
                var sex=w.Chance(Stream,mother.Id,6)<.5?Sex.Male:Sex.Female;
                WorldOps.Birth(w,Names.Given(w,sex,mother.Id*31+w.Month,Names.PeopleOf(father)),sex,father.Clan,father.City,father,mother);
                reigning[father.Clan.Id]=members+1;
            }
        }

        // Vacant thrones: eldest living son, then the eldest adult male of the house,
        // then any living child. With no heir a large realm may break up; otherwise a
        // new house takes the throne.
        static void Thrones(EmpireWorld w)
        {
            int realms=w.Realms.Count;
            for(int r=0;r<realms;r++)
            {
                var realm=w.Realms[r];if(!realm.Alive||realm.Ruler!=null)continue;
                Person heir=Heir(w,realm);
                if(heir==null&&realm.Cities.Count>=6)Breakaway(w,realm);
                if(heir==null)heir=NewHouse(w,realm);
                WorldOps.SetRuler(w,realm,heir);
            }
        }

        static Person Heir(EmpireWorld w,Realm realm)
        {
            Person last=realm.PreviousRuler,best=null;
            if(last!=null)foreach(var c in last.Children)if(Eligible(w,c,true)&&(best==null||c.BirthMonth<best.BirthMonth))best=c;
            if(best!=null)return best;
            foreach(var p in w.Living)if(p.Clan==realm.RulingClan&&Eligible(w,p,true)&&w.AgeOf(p)>=16&&(best==null||p.BirthMonth<best.BirthMonth))best=p;
            if(best!=null)return best;
            if(last!=null)foreach(var c in last.Children)if(Eligible(w,c,false)&&(best==null||c.BirthMonth<best.BirthMonth))best=c;
            return best;
        }
        static bool Eligible(EmpireWorld w,Person p,bool maleOnly)
        {
            if(!p.Alive||(maleOnly&&p.Sex!=Sex.Male))return false;
            foreach(var r in w.Realms)if(r.Alive&&r.Ruler==p)return false;
            return true;
        }

        static Person NewHouse(EmpireWorld w,Realm realm)
        {
            string people=Names.PeopleOf(realm);
            Clan clan=WorldOps.NewClan(w,Names.Surname(w,realm.Id*17+w.Month,people));
            int age=28+w.Pick(Stream,realm.Id,20,8);
            Person ruler=WorldOps.Birth(w,Names.Given(w,Sex.Male,realm.Id*17+w.Month+1,people),Sex.Male,clan,realm.Capital,null,null,age);
            clan.Founder=ruler;return ruler;
        }

        // The largest city far from the capital leaves with its neighbours and founds a realm.
        static void Breakaway(EmpireWorld w,Realm realm)
        {
            var capital=realm.Capital;
            City seat=null;double best=0;
            foreach(var city in realm.Cities){if(city==capital)continue;double v=city.Population*w.Distance(capital.Tile,city.Tile);if(v>best){best=v;seat=city;}}
            if(seat==null)return;
            var group=new List<City>();
            foreach(var c in realm.Cities)if(c!=seat&&c!=capital&&w.Distance(c.Tile,seat.Tile)<w.Distance(c.Tile,capital.Tile)*.8f)group.Add(c);
            string people=Names.PeopleOf(seat);
            Clan clan=WorldOps.NewClan(w,Names.Surname(w,seat.Id*19+w.Month,people));
            Person lord=WorldOps.Birth(w,Names.Given(w,Sex.Male,seat.Id*19+w.Month+1,people),Sex.Male,clan,seat,null,null,30+w.Pick(Stream,seat.Id,15,9));
            clan.Founder=lord;
            Realm newRealm=WorldOps.FoundRealm(w,Names.RealmOf(seat.Name),Names.Color(w,seat.Id+w.Month),seat,lord);
            foreach(var c in group)if(c.Realm==realm&&realm.Cities.Count>1)WorldOps.TransferCity(w,c,newRealm);
        }
    }
}
