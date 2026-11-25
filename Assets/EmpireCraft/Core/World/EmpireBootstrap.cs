using System;
using System.Collections.Generic;
using Orbis.Flat;

namespace EmpireCraft.Core
{
    // Opening placement on an ORBIS world: score land by fertility, fresh water and
    // coast, pick spaced city sites, group them into realms around far-apart
    // capitals on the same landmass, seat a ruling family in each realm.
    public static class EmpireBootstrap
    {
        const uint Stream=0x5345454Du;

        public static float SiteScore(EmpireWorld w,int t)
        {
            var m=w.Map;
            if(!w.Settleable(t)||m.Elevation[t]>2800||m.Fertility[t]<.12f)return -1;
            float score=(float)Math.Pow(m.Fertility[t],1.2);
            bool river=false;
            for(int d=0;d<4;d++){int n=m.Neighbor(t,d);if(n>=0&&(m.Water[n]==WaterKind.River||m.Water[n]==WaterKind.Lake))river=true;}
            if(river)score+=.30f;
            if(m.CoastDistance[t]<=1)score+=.15f;
            return score;
        }

        public static void Seed(EmpireWorld w,int cityCount,int realmCount)
        {
            if(w.Cities.Count>0)throw new InvalidOperationException("World already seeded");
            var m=w.Map;var candidates=new List<int>();var score=new float[m.Count];int land=0;
            for(int t=0;t<m.Count;t++){score[t]=SiteScore(w,t);if(score[t]>0){candidates.Add(t);}if(w.Settleable(t))land++;}
            candidates.Sort((a,b)=>score[a]!=score[b]?score[b].CompareTo(score[a]):a.CompareTo(b));
            var landmass=Landmasses(w);
            // Spaced sites: shrink the spacing until the target count is reached.
            var sites=new List<int>();float spacing=(float)Math.Sqrt(land/(cityCount*1.5));
            var taken=new bool[m.Count];
            while(sites.Count<cityCount&&spacing>=1.5f)
            {
                foreach(int t in candidates)
                {
                    if(sites.Count>=cityCount)break;if(taken[t])continue;
                    bool near=false;foreach(int s in sites)if(w.Distance(s,t)<spacing){near=true;break;}
                    if(near)continue;sites.Add(t);taken[t]=true;
                }
                spacing*=.85f;
            }
            // Capitals: farthest-point sampling weighted by site quality.
            var capitals=new List<int>();
            if(sites.Count>0)capitals.Add(0);
            var nearest=new float[sites.Count];for(int i=0;i<sites.Count;i++)nearest[i]=w.Distance(sites[0],sites[i]);
            while(capitals.Count<Math.Min(realmCount,sites.Count))
            {
                int best=-1;float bestValue=-1;
                for(int i=0;i<sites.Count;i++){float v=nearest[i]*(.6f+score[sites[i]]);if(v>bestValue){bestValue=v;best=i;}}
                if(best<0||nearest[best]<=0)break;capitals.Add(best);
                for(int i=0;i<sites.Count;i++)nearest[i]=Math.Min(nearest[i],w.Distance(sites[best],sites[i]));
            }
            // Every landmass with sites but no capital gets one of its own.
            var massHasCapital=new HashSet<int>();foreach(int c in capitals)massHasCapital.Add(landmass[sites[c]]);
            for(int i=0;i<sites.Count;i++)if(massHasCapital.Add(landmass[sites[i]]))capitals.Add(i);
            var realmOfSite=new Realm[sites.Count];
            foreach(int c in capitals)realmOfSite[c]=FoundHouse(w,sites[c]);
            for(int i=0;i<sites.Count;i++)
            {
                if(realmOfSite[i]!=null)continue;
                int best=-1;float bestDistance=float.MaxValue;
                foreach(int c in capitals)
                {
                    if(landmass[sites[c]]!=landmass[sites[i]])continue;
                    float d=w.Distance(sites[c],sites[i]);if(d<bestDistance){bestDistance=d;best=c;}
                }
                Realm realm=realmOfSite[best];
                realmOfSite[i]=realm;WorldOps.FoundCity(w,sites[i],realm,Names.Place(w,sites[i]));
            }
            // Initial hinterland: two rings, claimed round-robin so neighbours share fairly.
            for(int ring=0;ring<2;ring++)
            {
                var claims=new List<(City city,int tile)>();
                foreach(var c in w.Cities)
                    foreach(int t in c.Tiles.ToArray())
                        for(int d=0;d<4;d++){int n=m.Neighbor(t,d);if(n>=0&&w.Claimable(n)&&w.TileCity[n]<0)claims.Add((c,n));}
                foreach(var (city,tile) in claims)if(w.TileCity[tile]<0)WorldOps.ClaimTile(w,city,tile);
            }
            foreach(var c in w.Cities)
            {
                double fertility=0;foreach(int t in c.Tiles)fertility+=m.Fertility[t];
                c.Population=Math.Round(600+fertility*900*(.7+.6*w.Chance(Stream,c.Id,7)));
            }
        }

        // A realm founded at a site with a ruling family: ruler, spouse and children.
        static Realm FoundHouse(EmpireWorld w,int tile)
        {
            Clan clan=WorldOps.NewClan(w,Names.Surname(w,tile));
            int age=24+w.Pick(Stream,tile,24,1);
            Person ruler=WorldOps.Birth(w,Names.Given(w,Sex.Male,tile*7+1),Sex.Male,clan,null,null,null,age);
            clan.Founder=ruler;
            string capital=Names.Place(w,tile);
            Realm realm=WorldOps.FoundRealmAt(w,Names.RealmOf(capital),Names.Color(w,tile),tile,capital,ruler);
            City city=realm.Capital;ruler.City=city;
            Clan wifeClan=WorldOps.NewClan(w,Names.Surname(w,tile*7+2));
            Person wife=WorldOps.Birth(w,Names.Given(w,Sex.Female,tile*7+3),Sex.Female,wifeClan,city,null,null,Math.Max(16,age-2-w.Pick(Stream,tile,6,2)));
            WorldOps.Marry(w,ruler,wife);
            int children=w.Pick(Stream,tile,4,3);
            for(int k=0;k<children;k++)
            {
                int childAge=Math.Max(0,age-20-k*3);
                if(childAge>w.AgeOf(wife)-15)continue;
                var sex=w.Chance(Stream,tile,10+k)<.5?Sex.Male:Sex.Female;
                WorldOps.Birth(w,Names.Given(w,sex,tile*7+20+k),sex,clan,city,ruler,wife,childAge);
            }
            return realm;
        }

        static int[] Landmasses(EmpireWorld w)
        {
            var m=w.Map;var id=new int[m.Count];for(int i=0;i<id.Length;i++)id[i]=-1;
            var queue=new Queue<int>();int next=0;
            for(int i=0;i<m.Count;i++)
            {
                if(id[i]>=0||!w.Claimable(i))continue;id[i]=next;queue.Enqueue(i);
                while(queue.Count>0){int c=queue.Dequeue();for(int d=0;d<4;d++){int n=m.SphereNeighbor(c,d);if(n>=0&&id[n]<0&&w.Claimable(n)){id[n]=next;queue.Enqueue(n);}}}
                next++;
            }
            return id;
        }
    }
}
