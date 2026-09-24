using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis
{
    public enum ZoneKind { Ocean, Coast, Lake, RiverValley, Highland, Ice, Desert, Grassland, Forest }
    [Serializable] public sealed class TerrainZone
    {
        public int id,chunkId;public ZoneKind kind;public string name;public int[] tileIds;
    }
    [Serializable] public sealed class TerrainChunk
    {
        public int id,landmassId;public bool ocean;public string name;public int[] tileIds,zoneIds;
        public Vector3 center;public float angularRadius;
    }
    // Geography is derived, never the authoritative owner of climate or population.
    // Tiles own simulation data; connected zones partition chunks; each chunk owns render batches.
    public sealed class OrbisGeography
    {
        public TerrainChunk[] chunks;public TerrainZone[] zones;public int Revision {get;private set;}
        public void Build(OrbisWorld world)
        {
            int count=world.tiles.Length;int[] mass=new int[count];for(int i=0;i<count;i++)mass[i]=-1;
            var components=new List<List<int>>();
            for(int start=0;start<count;start++)if(mass[start]<0){int id=components.Count;var tiles=new List<int>();var queue=new Queue<int>();bool ocean=world.tiles[start].h<=0;mass[start]=id;queue.Enqueue(start);
                while(queue.Count>0){int i=queue.Dequeue();tiles.Add(i);foreach(int j in world.grid.neighbors[i])if(mass[j]<0&&(world.tiles[j].h<=0)==ocean){mass[j]=id;queue.Enqueue(j);}}components.Add(tiles);}
            var allChunks=new List<TerrainChunk>();
            for(int m=0;m<components.Count;m++){
                List<int> component=components[m];bool ocean=world.tiles[component[0]].h<=0;
                // Natural features set traversal cost. Size controls granularity only: there are no x/y bins.
                int seedCount=Math.Max(1,(int)Math.Ceiling(component.Count/(ocean?220.0:110.0)));
                var seeds=new List<int>();int first=component[0];float bestFeature=float.MinValue;
                foreach(int i in component){Tile t=world.tiles[i];float feature=ocean?-t.h:t.RiverStrength*.035f+t.h;if(feature>bestFeature){bestFeature=feature;first=i;}}seeds.Add(first);
                for(int k=1;k<seedCount;k++){int best=-1;float farthest=-1;foreach(int i in component){float near=4;foreach(int s in seeds)near=Mathf.Min(near,1-Vector3.Dot(world.grid.points[i],world.grid.points[s]));float feature=near*(1+Mathf.Min(1,world.tiles[i].RiverStrength*.01f));if(feature>farthest){farthest=feature;best=i;}}if(best>=0)seeds.Add(best);}
                int[] owner=new int[count];float[] distance=new float[count];bool[] finalized=new bool[count];for(int i=0;i<count;i++){owner[i]=-1;distance[i]=float.PositiveInfinity;}
                var heap=new MinHeap();for(int k=0;k<seeds.Count;k++){int i=seeds[k];owner[i]=k;distance[i]=0;heap.Push(i,0);}
                // Multi-source Dijkstra assigns a tile only through a predecessor with the same owner.
                // Thus every chunk remains connected, including around coastlines and narrow isthmuses.
                while(heap.Count>0){int i=heap.Pop();if(finalized[i])continue;finalized[i]=true;foreach(int j in world.grid.neighbors[i]){if(mass[j]!=m||finalized[j])continue;float candidate=distance[i]+BoundaryCost(world,i,j,ocean);if(candidate<distance[j]){distance[j]=candidate;owner[j]=owner[i];heap.Push(j,candidate);}}}
                var groups=new List<int>[seeds.Count];for(int k=0;k<groups.Length;k++)groups[k]=new List<int>();foreach(int i in component)groups[owner[i]].Add(i);
                foreach(var group in groups){if(group.Count==0)continue;int id=allChunks.Count;Vector3 center=Vector3.zero;foreach(int i in group){world.tiles[i].chunkId=id;center+=world.grid.points[i];}center.Normalize();float radius=0;foreach(int i in group)radius=Mathf.Max(radius,Vector3.Angle(center,world.grid.points[i]));allChunks.Add(new TerrainChunk{id=id,landmassId=m,ocean=ocean,name=(ocean?"Ocean sector ":"Natural region ")+id,tileIds=group.ToArray(),center=center,angularRadius=radius});}
            }
            chunks=allChunks.ToArray();var allZones=new List<TerrainZone>();bool[] visited=new bool[count];
            foreach(var chunk in chunks){var zoneIds=new List<int>();foreach(int start in chunk.tileIds)if(!visited[start]){ZoneKind kind=Classify(world.tiles[start]);int id=allZones.Count;var group=new List<int>();var queue=new Queue<int>();queue.Enqueue(start);visited[start]=true;
                    while(queue.Count>0){int i=queue.Dequeue();world.tiles[i].zoneId=id;group.Add(i);foreach(int j in world.grid.neighbors[i])if(!visited[j]&&world.tiles[j].chunkId==chunk.id&&Classify(world.tiles[j])==kind){visited[j]=true;queue.Enqueue(j);}}
                    allZones.Add(new TerrainZone{id=id,chunkId=chunk.id,kind=kind,name=kind+" zone "+id,tileIds=group.ToArray()});zoneIds.Add(id);}
                chunk.zoneIds=zoneIds.ToArray();}
            zones=allZones.ToArray();Revision++;
        }
        static float BoundaryCost(OrbisWorld w,int a,int b,bool ocean)
        {
            Tile x=w.tiles[a],y=w.tiles[b];float cost=1+Mathf.Abs(x.h-y.h)*12;
            if(ocean)return cost+(x.plate==y.plate?0:2)+Mathf.Abs(x.coast-y.coast)*.1f;
            if(x.basin!=y.basin)cost+=3; // Watershed divides are natural large-region boundaries.
            cost+=Mathf.Max(0,Mathf.Max(x.h,y.h)-.45f)*10;
            if((x.lake>.018f)!=(y.lake>.018f))cost+=2;
            if(Classify(x)!=Classify(y))cost+=.7f;
            return cost;
        }
        public static ZoneKind Classify(Tile t)
        {
            if(t.h<=0)return t.h>-.08f?ZoneKind.Coast:ZoneKind.Ocean;
            if(t.lake>.018f)return ZoneKind.Lake;
            if(t.h>.58f)return ZoneKind.Highland;
            if(t.temp<0||t.snow>20)return ZoneKind.Ice;
            if(t.RiverStrength>3)return ZoneKind.RiverValley;
            if(t.soil<.3f)return ZoneKind.Desert;
            return t.soil>.62f?ZoneKind.Forest:ZoneKind.Grassland;
        }
        public void Restore(TerrainChunk[] savedChunks,TerrainZone[] savedZones,OrbisWorld world)
        {
            chunks=savedChunks;zones=savedZones;
            for(int k=0;k<chunks.Length;k++){
                TerrainChunk c=chunks[k];if(c==null||c.id!=k||c.tileIds==null||c.tileIds.Length==0||c.zoneIds==null)throw new ArgumentException("Invalid saved chunk");
                Vector3 center=Vector3.zero;foreach(int i in c.tileIds){if(i<0||i>=world.tiles.Length)throw new ArgumentException("Invalid chunk tile");world.tiles[i].chunkId=k;center+=world.grid.points[i];}
                c.center=center.normalized;c.angularRadius=0;foreach(int i in c.tileIds)c.angularRadius=Mathf.Max(c.angularRadius,Vector3.Angle(c.center,world.grid.points[i]));
                foreach(int z in c.zoneIds)if(z<0||z>=zones.Length)throw new ArgumentException("Invalid chunk zone");
            }
            for(int k=0;k<zones.Length;k++){var z=zones[k];if(z==null||(int)z.kind<0||(int)z.kind>8||z.id!=k||z.chunkId<0||z.chunkId>=chunks.Length||z.tileIds==null||z.tileIds.Length==0)throw new ArgumentException("Invalid saved zone");foreach(int i in z.tileIds){if(i<0||i>=world.tiles.Length)throw new ArgumentException("Invalid zone tile");world.tiles[i].zoneId=k;}}
            Validate(world);Revision++;
        }
        public void Validate(OrbisWorld world)
        {
            int n=world.tiles.Length;var tileSeen=new bool[n];var zoneSeen=new bool[zones.Length];
            foreach(var chunk in chunks){if(chunk.tileIds.Length==0)throw new Exception("Empty chunk");CheckConnected(world,chunk.tileIds);foreach(int i in chunk.tileIds){if(tileSeen[i]||world.tiles[i].chunkId!=chunk.id)throw new Exception("Chunk membership mismatch");tileSeen[i]=true;if((world.tiles[i].h<=0)!=chunk.ocean)throw new Exception("Chunk crosses a coastline");}foreach(int z in chunk.zoneIds){if(zoneSeen[z]||zones[z].chunkId!=chunk.id)throw new Exception("Zone ownership mismatch");zoneSeen[z]=true;}}
            foreach(bool seen in tileSeen)if(!seen)throw new Exception("Unassigned tile");foreach(bool seen in zoneSeen)if(!seen)throw new Exception("Unassigned zone");Array.Clear(tileSeen,0,n);
            foreach(var zone in zones){CheckConnected(world,zone.tileIds);foreach(int i in zone.tileIds){if(tileSeen[i]||world.tiles[i].zoneId!=zone.id||world.tiles[i].chunkId!=zone.chunkId)throw new Exception("Tile / zone mismatch");tileSeen[i]=true;}}
            foreach(bool seen in tileSeen)if(!seen)throw new Exception("Tile has no zone");
        }
        static void CheckConnected(OrbisWorld world,int[] ids){var set=new HashSet<int>(ids);var q=new Queue<int>();q.Enqueue(ids[0]);set.Remove(ids[0]);while(q.Count>0){int i=q.Dequeue();foreach(int j in world.grid.neighbors[i])if(set.Remove(j))q.Enqueue(j);}if(set.Count>0)throw new Exception("Disconnected geography region");}
    }
}
