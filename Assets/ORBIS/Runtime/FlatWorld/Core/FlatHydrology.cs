using System;
using System.Collections.Generic;
using System.Threading;
namespace Orbis.Flat
{
    internal sealed class FloodHeap
    {
        struct Entry { public int Id,Height; }
        readonly List<Entry> heap=new List<Entry>();
        public int Count { get { return heap.Count; } }
        static bool Less(Entry a,Entry b) { return a.Height<b.Height||(a.Height==b.Height&&a.Id<b.Id); }
        public void Push(int id,int height)
        {
            var e=new Entry{Id=id,Height=height};int i=heap.Count;heap.Add(e);
            while(i>0){int p=(i-1)/2;if(!Less(e,heap[p]))break;heap[i]=heap[p];i=p;}heap[i]=e;
        }
        public int Pop()
        {
            int result=heap[0].Id,last=heap.Count-1;var e=heap[last];heap.RemoveAt(last);if(last==0)return result;
            int i=0;while(i*2+1<last){int j=i*2+1;if(j+1<last&&Less(heap[j+1],heap[j]))j++;if(!Less(heap[j],e))break;heap[i]=heap[j];i=j;}heap[i]=e;return result;
        }
    }
    public static class FlatHydrology
    {
        public static void Build(WorldData w,CancellationToken cancel)
        {
            var visited=new bool[w.Count];var heap=new FloodHeap();int cursor=0;
            for(int i=0;i<w.Count;i++)
            {
                w.Downstream[i]=-1;w.WaterLevel[i]=w.Elevation[i];
                if(w.Water[i]>=WaterKind.DeepOcean&&w.Water[i]<=WaterKind.ShallowOcean)
                {visited[i]=true;w.WaterLevel[i]=0;heap.Push(i,0);}
                float rain=0;for(int m=0;m<12;m++)rain+=w.Precipitation[i*12+m];
                w.Runoff[i]=w.Water[i]>=WaterKind.DeepOcean&&w.Water[i]<=WaterKind.ShallowOcean?0:rain/1200f;
            }
            if(heap.Count==0)
            {
                int lowest=0;for(int i=1;i<w.Count;i++)if(w.Elevation[i]<w.Elevation[lowest])lowest=i;
                visited[lowest]=true;heap.Push(lowest,w.Elevation[lowest]);
            }
            while(heap.Count>0)
            {
                if((cursor&4095)==0)cancel.ThrowIfCancellationRequested();
                int i=heap.Pop();w.DrainOrder[cursor++]=i;
                for(int d=0;d<4;d++)
                {
                    int n=w.SphereNeighbor(i,d);if(n<0||visited[n])continue;
                    visited[n]=true;w.Downstream[n]=i;
                    // A parent is popped BEFORE its child, including perfectly flat plateaus.
                    // No epsilon slope leaks meters into the author's 20 elevation presets.
                    w.WaterLevel[n]=(short)Math.Max(w.Elevation[n],w.WaterLevel[i]);heap.Push(n,w.WaterLevel[n]);
                }
            }
            // World Atlas hydrology._flat_routing_distance: flood parents are NOT
            // physical flow directions. Build a separate distance-to-spill gradient.
            // Adapted to ORBIS's four shared tile faces and spherical adjacency.
            var spillDistance=new int[w.Count];var queue=new int[w.Count];int head=0,tail=0;
            for(int i=0;i<w.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                spillDistance[i]=-1;bool spill=w.Downstream[i]<0;
                for(int d=0;d<4;d++)
                    if(w.WaterLevel[w.SphereNeighbor(i,d)]<w.WaterLevel[i])spill=true;
                if(spill){spillDistance[i]=0;queue[tail++]=i;}
            }
            while(head<tail)
            {
                if((head&4095)==0)cancel.ThrowIfCancellationRequested();int i=queue[head++];
                for(int d=0;d<4;d++)
                {
                    int n=w.SphereNeighbor(i,d);
                    if(spillDistance[n]>=0||w.WaterLevel[n]!=w.WaterLevel[i])continue;
                    spillDistance[n]=spillDistance[i]+1;queue[tail++]=n;
                }
            }
            // Lower head, then shorter spill distance: this strict potential is acyclic.
            // Rebuild the downstream-first order; flood heap order is no longer valid.
            Array.Sort(w.DrainOrder,(a,b)=>
            {
                int c=w.WaterLevel[a].CompareTo(w.WaterLevel[b]);
                if(c==0)c=spillDistance[a].CompareTo(spillDistance[b]);
                return c==0?a.CompareTo(b):c;
            });
            var eastWestLength=new double[w.Height];
            for(int y=0;y<w.Height;y++)eastWestLength[y]=Math.Max(.01,Math.Cos(((y+.5)/w.Height-.5)*Math.PI));
            for(int i=0;i<w.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                if(w.Downstream[i]<0)continue;
                int best=-1;double bestSlope=-1;
                for(int d=0;d<4;d++)
                {
                    int n=w.SphereNeighbor(i,d),drop=w.WaterLevel[i]-w.WaterLevel[n];
                    if(drop<0||(drop==0&&spillDistance[n]>=spillDistance[i]))continue;
                    double slope=drop/(d<2?eastWestLength[i/w.Width]:1);
                    // Among equally short flat routes follow lower actual terrain,
                    // not the arbitrary row ID used by the priority queue.
                    if(best<0||slope>bestSlope+1e-9||(Math.Abs(slope-bestSlope)<1e-9&&w.Elevation[n]<w.Elevation[best]))
                    {best=n;bestSlope=slope;}
                }
                if(best<0)throw new InvalidOperationException("Missing hydrology spill at "+i);
                w.Downstream[i]=best;
            }
            RouteLakeBasins(w,cancel);
            SelectStreams(w,cancel);
        }
        static void RouteLakeBasins(WorldData w,CancellationToken cancel)
        {
            var rank=new int[w.Count];var visited=new bool[w.Count];var queue=new int[w.Count];
            for(int k=0;k<w.Count;k++)rank[w.DrainOrder[k]]=k;
            for(int i=0;i<w.Count;i++)
            {
                if(FlatTerrainAnalysis.Ocean(w.Water[i]))continue;
                w.Water[i]=w.Downstream[i]<0||w.WaterLevel[i]>w.Elevation[i]||w.Elevation[i]<=0?WaterKind.Lake:WaterKind.Land;
            }
            for(int start=0;start<w.Count;start++)
            {
                if((start&4095)==0)cancel.ThrowIfCancellationRequested();
                if(visited[start]||w.Water[start]!=WaterKind.Lake)continue;
                int head=0,tail=1,outlet=start;queue[0]=start;visited[start]=true;
                while(head<tail)
                {
                    int cell=queue[head++];
                    if(rank[cell]<rank[outlet])outlet=cell;
                    for(int direction=0;direction<4;direction++)
                    {
                        int next=w.SphereNeighbor(cell,direction);
                        if(visited[next]||w.Water[next]!=WaterKind.Lake||w.WaterLevel[next]!=w.WaterLevel[start])continue;
                        visited[next]=true;queue[tail++]=next;
                    }
                }
                // The earliest downstream node has an escape path outside this
                // basin. Choosing it cannot reconnect the lake into its own inflow.
                for(int i=0;i<tail;i++)visited[queue[i]]=false;
                head=0;tail=1;queue[0]=outlet;visited[outlet]=true;
                while(head<tail)
                {
                    int cell=queue[head++];
                    for(int direction=0;direction<4;direction++)
                    {
                        int next=w.SphereNeighbor(cell,direction);
                        if(visited[next]||w.Water[next]!=WaterKind.Lake||w.WaterLevel[next]!=w.WaterLevel[outlet])continue;
                        visited[next]=true;w.Downstream[next]=cell;queue[tail++]=next;
                    }
                }
            }
            // Lake interiors now have a common outlet rather than independent
            // flood parents. Rebuild the order used for discharge and sediment.
            var firstChild=new int[w.Count];var nextChild=new int[w.Count];
            for(int i=0;i<w.Count;i++)firstChild[i]=-1;
            int rootCount=0;
            for(int i=0;i<w.Count;i++)
            {
                int parent=w.Downstream[i];
                if(parent<0)queue[rootCount++]=i;
                else{nextChild[i]=firstChild[parent];firstChild[parent]=i;}
            }
            int cursor=0;
            for(int head=0,tail=rootCount;head<tail;head++)
            {
                if((head&4095)==0)cancel.ThrowIfCancellationRequested();
                int cell=queue[head];w.DrainOrder[cursor++]=cell;
                for(int child=firstChild[cell];child>=0;child=nextChild[child])queue[tail++]=child;
            }
            if(cursor!=w.Count)throw new InvalidOperationException("Lake routing created a drainage cycle.");
        }
        static void SelectStreams(WorldData w,CancellationToken cancel)
        {
            // World Atlas normalizes climate/upland source yield before extracting
            // streams. Keep selection weights separate from conserved rainfall runoff.
            int landCount=0;double sourceTotal=0;var supply=new float[w.Count];
            var heights=new List<short>();
            for(int i=0;i<w.Count;i++)if(!FlatTerrainAnalysis.Ocean(w.Water[i]))heights.Add(w.Elevation[i]);
            heights.Sort();landCount=heights.Count;
            float low=landCount>0?heights[landCount/10]:0,high=landCount>0?heights[landCount*9/10]:1;
            var valleyReach=new int[w.Count];
            for(int i=0;i<w.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                if(FlatTerrainAnalysis.Ocean(w.Water[i]))continue;
                float upland=FMath.Clamp((w.Elevation[i]-low)/Math.Max(1,high-low),0,1);
                supply[i]=(.35f+.65f*(float)Math.Pow(upland,1.5))*w.Runoff[i];sourceTotal+=supply[i];
                int x=i%w.Width,y=i/w.Width,count=0;float sum=0,min=w.Elevation[i],max=min;
                for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
                {
                    int n=w.Index(x+dx,y+dy);if(n<0||FlatTerrainAnalysis.Ocean(w.Water[n]))continue;
                    float h=w.Elevation[n];sum+=h;count++;min=Math.Min(min,h);max=Math.Max(max,h);
                }
                if(count>0&&(sum/count-w.Elevation[i])/Math.Max(1,max-min)>.12f)valleyReach[i]=3;
            }
            if(sourceTotal>0)for(int i=0;i<w.Count;i++)supply[i]*=(float)(landCount/sourceTotal);
            for(int k=w.Count-1;k>=0;k--)
            {
                int i=w.DrainOrder[k],n=w.Downstream[i];if(n<0)continue;
                supply[n]+=supply[i];valleyReach[n]=Math.Max(valleyReach[n],valleyReach[i]-1);
            }
            for(int k=w.Count-1;k>=0;k--){int i=w.DrainOrder[k],n=w.Downstream[i];if(n>=0)w.Runoff[n]+=w.Runoff[i];}
            // Atlas area-fraction / minimum-span rule; a smaller area fraction is
            // appropriate to the coarse strategic tile grid (not a DEM pixel grid).
            float threshold=Math.Max(6,Math.Max(landCount*.0015f,(float)Math.Sqrt(landCount)*.20f));
            var streams=new bool[w.Count];var lakeOutlet=new bool[w.Count];var outletRoute=new bool[w.Count];
            for(int i=0;i<w.Count;i++)
            {
                if(FlatTerrainAnalysis.Ocean(w.Water[i]))continue;
                if(w.Downstream[i]>=0&&w.Water[i]==WaterKind.Lake)lakeOutlet[w.Downstream[i]]=true;
                streams[i]=supply[i]>=threshold&&(valleyReach[i]>0||supply[i]>=threshold*4);
            }
            // Every filled lake has a spill route. Follow it through any short
            // dry-looking saddle, including low-discharge outlets, so the rendered
            // water network does not end a tile before the next lake or sea.
            for(int k=w.Count-1;k>=0;k--)
            {
                int i=w.DrainOrder[k],n=w.Downstream[i];
                outletRoute[i]|=lakeOutlet[i];
                streams[i]|=outletRoute[i];
                if(streams[i]&&n>=0)streams[n]=true;
                if(outletRoute[i]&&n>=0)outletRoute[n]=true;
            }
            for(int i=0;i<w.Count;i++)streams[i]&=w.Water[i]==WaterKind.Land&&w.Downstream[i]>=0;
            // Atlas minimum-headwater pruning, including short reaches into lakes.
            // Preserve lake-fed outlet reaches so pruning cannot sever a lake chain.
            int minimumLength=Math.Max(3,(int)Math.Ceiling(Math.Sqrt(landCount)*.012));
            var incoming=new int[w.Count];bool changed;
            do
            {
                cancel.ThrowIfCancellationRequested();Array.Clear(incoming,0,w.Count);
                for(int i=0;i<w.Count;i++)if(streams[i]&&streams[w.Downstream[i]])incoming[w.Downstream[i]]++;
                changed=false;var remove=new List<int>();
                for(int i=0;i<w.Count;i++)
                {
                    if(!streams[i]||incoming[i]>0||outletRoute[i])continue;
                    int n=i,length=0;
                    do{length++;n=w.Downstream[n];}while(n>=0&&streams[n]&&incoming[n]==1&&length<minimumLength);
                    if(length>=minimumLength)continue;
                    n=i;for(int j=0;j<length;j++){remove.Add(n);n=w.Downstream[n];}
                }
                foreach(int i in remove){streams[i]=false;changed=true;}
            }while(changed);
            for(int i=0;i<w.Count;i++)if(streams[i])w.Water[i]=WaterKind.River;
        }
    }
}
