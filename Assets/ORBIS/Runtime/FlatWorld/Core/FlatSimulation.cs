using System;
using System.Collections.Generic;
namespace Orbis.Flat
{
    // Only this class advances civilization time. Presentation caches never call Step.
    public sealed class FlatSimulation
    {
        public readonly WorldData World;
        readonly List<int> farms=new List<int>();
        readonly double[] production=new double[WorldData.MaxNations],need=new double[WorldData.MaxNations];
        readonly List<int> populated=new List<int>();
        readonly List<HashSet<int>> frontiers=new List<HashSet<int>>();
        static readonly uint[] Colors={0xe9aa58,0x52bba6,0xad86da,0xdf7885,0x70a7e0,0xc6cf71,0xe994c4,0x85bd7b,0xcd8760,0x69c6d1,0xbdad8e,0xb07ad0};
        public FlatSimulation(WorldData world) { World=world;Reindex(); }
        public void Reindex()
        {
            populated.Clear();farms.Clear();frontiers.Clear();for(int c=0;c<World.Nations.Count;c++)frontiers.Add(new HashSet<int>());
            for(int i=0;i<World.Count;i++){if(World.Population[i]>0)populated.Add(i);if(World.Buildings[i]==Structure.Farm&&World.Owner[i]>=0)farms.Add(i);}
            for(int z=0;z<World.ZoneCount;z++)if(World.ZoneOwner[z]>=0)AddFrontier(z,World.ZoneOwner[z]);
        }
        public int Found(int tile)
        {
            var w=World;if(tile<0||tile>=w.Count||w.Nations.Count>=WorldData.MaxNations)return -1;
            int zone=w.ZoneOf(tile);if(w.ZoneOwner[zone]>=0)return -1;
            int capital=BestLand(zone);if(capital<0)return -1;
            int id=w.Nations.Count;w.Nations.Add(new Nation{Name="文明 "+(id+1),Capital=capital,Color=Colors[id]});frontiers.Add(new HashSet<int>());
            Claim(zone,id);w.Population[capital]=500;w.Buildings[capital]=Structure.Settlement;w.Touch(capital);populated.Add(capital);return id;
        }
        public int SeedNations(int count)
        {
            int founded=0;uint seed=FMath.Seed(World.Seed);
            for(int attempt=0;attempt<World.ZoneCount*3&&founded<count;attempt++)
            {
                int z=(int)(FMath.Hash(seed+(uint)attempt*97u)%(uint)World.ZoneCount),i=BestLand(z);if(i<0||World.Fertility[i]<.25f)continue;
                bool close=false;foreach(var c in World.Nations)if(c.Capital>=0)
                {int dx=Math.Abs(c.Capital%World.Width-i%World.Width);dx=Math.Min(dx,World.Width-dx);int dy=Math.Abs(c.Capital/World.Width-i/World.Width);if(dx+dy<World.Width/7)close=true;}
                if(!close&&Found(i)>=0)founded++;
            }
            return founded;
        }
        int BestLand(int zone)
        {
            int best=-1;for(int p=0;p<16;p++){int i=World.ZoneTile(zone,p);if(World.Habitable(i)&&(best<0||World.Fertility[i]>World.Fertility[best]))best=i;}return best;
        }
        int ZoneNeighbor(int z,int d)
        {
            int x=z%World.ZoneWidth,y=z/World.ZoneWidth;
            switch(d){case 0:x--;break;case 1:x++;break;case 2:y--;break;default:y++;break;}
            return y<0||y>=World.ZoneHeight?-1:y*World.ZoneWidth+FMath.Wrap(x,World.ZoneWidth);
        }
        bool ConnectedByLand(int from,int to)
        {
            for(int p=0;p<16;p++)
            {
                int i=World.ZoneTile(from,p);if(!World.Walkable(i))continue;
                for(int d=0;d<4;d++){int j=World.Neighbor(i,d);if(j>=0&&World.ZoneOf(j)==to&&World.Walkable(j))return true;}
            }
            return false;
        }
        void AddFrontier(int z,int owner)
        {
            for(int d=0;d<4;d++){int n=ZoneNeighbor(z,d);if(n>=0&&World.ZoneOwner[n]<0&&ConnectedByLand(z,n)&&BestLand(n)>=0)frontiers[owner].Add(n);}
        }
        void Claim(int z,int owner)
        {
            World.ZoneOwner[z]=owner;
            for(int p=0;p<16;p++){int i=World.ZoneTile(z,p);World.Owner[i]=owner;World.Touch(i);}
            foreach(var set in frontiers)set.Remove(z);AddFrontier(z,owner);
        }
        public bool Annex(int tile,int owner)
        {
            if(tile<0||tile>=World.Count||owner<0||owner>=World.Nations.Count)return false;
            int z=World.ZoneOf(tile);if(!frontiers[owner].Contains(z)||World.ZoneOwner[z]>=0||World.Nations[owner].Food<60)return false;
            Claim(z,owner);World.Nations[owner].Food-=60;Settle(z,owner);return true;
        }
        void Settle(int zone,int owner)
        {
            int target=BestLand(zone),source=-1;
            foreach(int i in populated)if(World.Owner[i]==owner&&World.Population[i]>110&&(source<0||World.Population[i]>World.Population[source]))source=i;
            if(source<0||target<0)return;
            float settlers=Math.Min(60,World.Population[source]*.15f);World.Population[source]-=settlers;World.Population[target]+=settlers;
            World.Buildings[target]=Structure.Settlement;populated.Add(target);World.Touch(source);World.Touch(target);
        }
        public bool Build(int tile,Structure structure)
        {
            if(tile<0||tile>=World.Count||World.Owner[tile]<0||structure==Structure.None||structure==Structure.Settlement)return false;
            if(World.Buildings[tile]==Structure.Settlement)return false;
            if(structure==Structure.Bridge)
            {if(World.Water[tile]!=WaterKind.River&&World.Water[tile]!=WaterKind.Lake)return false;if(World.WaterLevel[tile]-World.Elevation[tile]>100)return false;}
            else if(!World.Habitable(tile))return false;
            if(World.Buildings[tile]==structure)return false;
            var nation=World.Nations[World.Owner[tile]];double cost=structure==Structure.Bridge?40:15;
            if(nation.Food<cost)return false;nation.Food-=cost;if(World.Buildings[tile]==Structure.Farm)farms.Remove(tile);if(structure==Structure.Farm)farms.Add(tile);World.Buildings[tile]=structure;World.Touch(tile);return true;
        }
        public void Step(float days)
        {
            if(!FMath.Finite(days)||days<=0||days>30)throw new ArgumentOutOfRangeException(nameof(days));
            var w=World;w.Day+=days;
            Array.Clear(production,0,w.Nations.Count);Array.Clear(need,0,w.Nations.Count);
            foreach(int i in populated)
            {
                int owner=w.Owner[i];if(owner<0||w.Population[i]<=0)continue;
                float temp=w.Temperature[i*12+w.Month];float season=FMath.Clamp((temp+2)/22,0,1);
                float capacity=150+850*w.Fertility[i];
                float growth=w.Population[i]*(1-w.Population[i]/capacity)*.0009f*days;
                if(w.Nations[owner].Food<=0)growth-=w.Population[i]*.003f*days;
                w.Population[i]=Math.Max(0,w.Population[i]+growth);
                production[owner]+=w.Population[i]*(.010+.020*w.Fertility[i]*season)*days;
                need[owner]+=w.Population[i]*.016*days;
            }
            // Farm effects evaluated on owned zones, not per-frame GameObjects.
            foreach(int i in farms)
            {
                int owner=w.Owner[i];if(owner>=0&&w.Buildings[i]==Structure.Farm)
                    production[owner]+=w.Fertility[i]*3*FMath.Clamp((w.Temperature[i*12+w.Month]+3)/24,0,1)*days;
            }
            for(int c=0;c<w.Nations.Count;c++)
            {
                var n=w.Nations[c];n.Food=Math.Max(0,Math.Min(1000000,n.Food+production[c]-need[c]));n.ExpansionClock+=days;
                if(n.ExpansionClock<15)continue;n.ExpansionClock%=15;
                if(n.Food<90)continue;
                int best=-1;float score=-1;
                foreach(int z in frontiers[c])
                {
                    if(w.ZoneOwner[z]>=0)continue;int land=BestLand(z);if(land<0)continue;
                    float s=w.Fertility[land]+FMath.Unit((uint)z^FMath.Seed(w.Seed))*.15f;
                    if(s>score||(s==score&&z<best)){score=s;best=z;}
                }
                if(best>=0)Annex(w.ZoneTile(best,0),c);
            }
            w.Revision++;
        }
        public double TotalPopulation()
        { double p=0;foreach(int i in populated)p+=World.Population[i];foreach(var n in World.Nations)p+=n.Refuges;return p; }
    }
}
