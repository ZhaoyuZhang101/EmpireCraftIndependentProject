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
            Population(w);
            if(w.Month%12==0)Year(w);
        }

        static void Population(EmpireWorld w)
        {
            var m=w.Map;
            foreach(var c in w.Cities)
            {
                if(!c.Alive)continue;
                double capacity=0;foreach(int t in c.Tiles)capacity+=150+2600*m.Fertility[t];
                double growth=.018/12*c.Population*(1-c.Population/Math.Max(1,capacity));
                c.Population=Math.Max(50,c.Population+growth);
            }
        }

        static void Year(EmpireWorld w)
        {
            Hinterland(w);
            NewCities(w);
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
                double capacity=0;foreach(int t in c.Tiles)capacity+=150+2600*m.Fertility[t];
                if(c.Population<capacity*.75)continue;
                int best=-1;float bestScore=-1;
                foreach(int t in c.Tiles)
                    for(int d=0;d<4;d++)
                    {
                        int n=m.Neighbor(t,d);if(n<0||w.TileCity[n]>=0||!w.Claimable(n))continue;
                        float distance=w.Distance(c.Tile,n);if(distance>7)continue;
                        float s=m.Fertility[n]-distance*.04f;if(s>bestScore||(s==bestScore&&n<best)){bestScore=s;best=n;}
                    }
                if(best>=0)WorldOps.ClaimTile(w,id,best);
            }
        }

        // A populous realm plants a daughter city on good free land beyond its frontier.
        static void NewCities(EmpireWorld w)
        {
            int realms=w.Realms.Count;
            for(int r=0;r<realms;r++)
            {
                var realm=w.Realms[r];if(!realm.Alive)continue;
                double people=0;foreach(int c in realm.Cities)people+=w.Cities[c].Population;
                if(people<realm.Cities.Count*2200||w.Chance(Stream,r,1)>.12)continue;
                int best=-1;float bestScore=0;
                foreach(int c in realm.Cities)
                {
                    var city=w.Cities[c];
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
                int city2=WorldOps.FoundCity(w,best,r,Names.Place(w,best+w.Month));
                // Settlers come from the capital.
                var capital=w.Cities[realm.Capital];double settlers=Math.Min(400,capital.Population*.1);
                capital.Population-=settlers;w.Cities[city2].Population=settlers;
            }
        }

        static void Ageing(EmpireWorld w)
        {
            var dead=new List<int>();
            foreach(int id in w.Living)
            {
                int age=w.AgeOf(w.People[id]);
                double q=age<5?.03:Math.Min(.85,.0006*Math.Exp(.082*age));
                if(w.Chance(Stream,id,2)<q)dead.Add(id);
            }
            foreach(int id in dead)WorldOps.Death(w,id);
        }

        // Only reigning houses are tracked as people (capped per house); everyone else
        // stays part of a city's population number.
        const int HouseCap=24;
        static void Families(EmpireWorld w)
        {
            var reigning=new Dictionary<int,int>();
            foreach(var r in w.Realms)if(r.Alive&&r.RulingClan>=0)reigning[r.RulingClan]=0;
            foreach(int id in w.Living){int clan=w.People[id].Clan;if(reigning.ContainsKey(clan))reigning[clan]++;}
            int snapshot=w.Living.Count;
            for(int k=0;k<snapshot;k++)
            {
                var p=w.People[w.Living[k]];int age=w.AgeOf(p);
                if(p.Sex!=Sex.Male||p.Spouse>=0||age<18||age>45||!reigning.TryGetValue(p.Clan,out int members)||members>=HouseCap)continue;
                if(w.Chance(Stream,p.Id,3)>=.35)continue;
                int clan=WorldOps.NewClan(w,Names.Surname(w,p.Id*13+w.Month));
                int wifeAge=Math.Max(16,age-2-w.Pick(Stream,p.Id,8,4));
                int wife=WorldOps.Birth(w,Names.Given(w,Sex.Female,p.Id*13+w.Month+1),Sex.Female,clan,p.City,-1,-1,wifeAge);
                WorldOps.Marry(w,p.Id,wife);
            }
            snapshot=w.Living.Count;
            for(int k=0;k<snapshot;k++)
            {
                var mother=w.People[w.Living[k]];
                if(mother.Sex!=Sex.Female||mother.Spouse<0)continue;
                int age=w.AgeOf(mother);if(age<16||age>40)continue;
                var father=w.People[mother.Spouse];if(w.AgeOf(father)<16)continue;
                if(!reigning.TryGetValue(father.Clan,out int members)||members>=HouseCap)continue;
                if(w.Chance(Stream,mother.Id,5)>.22)continue;
                var sex=w.Chance(Stream,mother.Id,6)<.5?Sex.Male:Sex.Female;
                WorldOps.Birth(w,Names.Given(w,sex,mother.Id*31+w.Month),sex,father.Clan,father.City,father.Id,mother.Id);
                reigning[father.Clan]=members+1;
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
                var realm=w.Realms[r];if(!realm.Alive||realm.Ruler>=0)continue;
                int heir=Heir(w,realm);
                if(heir<0&&realm.Cities.Count>=6)Breakaway(w,r);
                if(heir<0)heir=NewHouse(w,realm);
                WorldOps.SetRuler(w,r,heir);
            }
        }

        static int Heir(EmpireWorld w,Realm realm)
        {
            int last=realm.PreviousRuler,best=-1;
            if(last>=0)foreach(int c in w.People[last].Children)if(Eligible(w,c,true)&&(best<0||w.People[c].BirthMonth<w.People[best].BirthMonth))best=c;
            if(best>=0)return best;
            foreach(int id in w.Living)if(w.People[id].Clan==realm.RulingClan&&Eligible(w,id,true)&&w.AgeOf(w.People[id])>=16&&(best<0||w.People[id].BirthMonth<w.People[best].BirthMonth))best=id;
            if(best>=0)return best;
            if(last>=0)foreach(int c in w.People[last].Children)if(Eligible(w,c,false)&&(best<0||w.People[c].BirthMonth<w.People[best].BirthMonth))best=c;
            return best;
        }
        static bool Eligible(EmpireWorld w,int person,bool maleOnly)
        {
            var p=w.People[person];if(!p.Alive||(maleOnly&&p.Sex!=Sex.Male))return false;
            foreach(var r in w.Realms)if(r.Alive&&r.Ruler==person)return false;
            return true;
        }

        static int NewHouse(EmpireWorld w,Realm realm)
        {
            int clan=WorldOps.NewClan(w,Names.Surname(w,realm.Id*17+w.Month));
            int age=28+w.Pick(Stream,realm.Id,20,8);
            int ruler=WorldOps.Birth(w,Names.Given(w,Sex.Male,realm.Id*17+w.Month+1),Sex.Male,clan,realm.Capital,-1,-1,age);
            w.Clans[clan].Founder=ruler;return ruler;
        }

        // The largest city far from the capital leaves with its neighbours and founds a realm.
        static void Breakaway(EmpireWorld w,int realmId)
        {
            var realm=w.Realms[realmId];var capital=w.Cities[realm.Capital];
            int seat=-1;double best=0;
            foreach(int c in realm.Cities){if(c==realm.Capital)continue;var city=w.Cities[c];double v=city.Population*w.Distance(capital.Tile,city.Tile);if(v>best){best=v;seat=c;}}
            if(seat<0)return;
            var group=new List<int>();var seatCity=w.Cities[seat];
            foreach(int c in realm.Cities)if(c!=seat&&c!=realm.Capital&&w.Distance(w.Cities[c].Tile,seatCity.Tile)<w.Distance(w.Cities[c].Tile,capital.Tile)*.8f)group.Add(c);
            int clan=WorldOps.NewClan(w,Names.Surname(w,seat*19+w.Month));
            int lord=WorldOps.Birth(w,Names.Given(w,Sex.Male,seat*19+w.Month+1),Sex.Male,clan,seat,-1,-1,30+w.Pick(Stream,seat,15,9));
            w.Clans[clan].Founder=lord;
            int newRealm=WorldOps.FoundRealm(w,Names.RealmOf(seatCity.Name),Names.Color(w,seat+w.Month),seat,lord);
            foreach(int c in group)if(w.Cities[c].Realm==realmId&&w.Realms[realmId].Cities.Count>1)WorldOps.TransferCity(w,c,newRealm);
        }
    }
}
