using System;
using System.Collections.Generic;
using System.Threading;
namespace Orbis.Flat
{
    internal sealed class FloodHeap
    {
        struct Entry { public int Id; public long Priority; }
        readonly List<Entry> heap=new List<Entry>();
        public int Count { get { return heap.Count; } }
        static bool Less(Entry a,Entry b) { return a.Priority<b.Priority||(a.Priority==b.Priority&&a.Id<b.Id); }
        public void Push(int id,long priority)
        {
            var e=new Entry{Id=id,Priority=priority};int i=heap.Count;heap.Add(e);
            while(i>0){int p=(i-1)/2;if(!Less(e,heap[p]))break;heap[i]=heap[p];i=p;}heap[i]=e;
        }
        public int Pop() { return Pop(out _); }
        public int Pop(out long priority)
        {
            int result=heap[0].Id,last=heap.Count-1;priority=heap[0].Priority;var e=heap[last];heap.RemoveAt(last);if(last==0)return result;
            int i=0;while(i*2+1<last){int j=i*2+1;if(j+1<last&&Less(heap[j+1],heap[j]))j++;if(!Less(heap[j],e))break;heap[i]=heap[j];i=j;}heap[i]=e;return result;
        }
    }
    public static class FlatHydrology
    {
        // Unit lengths are one meridional grid interval. Fixed-point spherical
        // distances make equal-cost plateau paths independent of heap insertion order.
        const int DistanceUnit=1000000;

        public static bool IsDrainageNeighbor(WorldData world,int from,int to)
        {
            if(from<0||to<0||from>=world.Count||to>=world.Count||from==to)return false;
            int row=from/world.Width,dy=Math.Abs(to/world.Width-row);
            int dx=Math.Abs(FlatTerrainAnalysis.WrappedDX(to%world.Width,from%world.Width,world.Width));
            return (dx<=1&&dy<=1)||(dy==0&&dx==world.Width/2&&(row==0||row==world.Height-1));
        }

        static int[] GroundLengths(WorldData world)
        {
            var lengths=new int[world.Height*8];
            double angle=Math.PI/world.Height,halfStep=Math.Sin(angle*.5);
            for(int y=0;y<world.Height;y++)
            {
                double latitude=(y+.5)*angle-Math.PI*.5;
                double cosine=Math.Cos(latitude);
                int start=y*8;
                lengths[start]=lengths[start+1]=(int)Math.Round(2*Math.Asin(cosine*halfStep)/angle*DistanceUnit);
                lengths[start+2]=lengths[start+3]=DistanceUnit;
                // A diagonal spanning these two latitude rings has the same
                // great-circle length from either end, including across longitude zero.
                if(y+1==world.Height)continue;
                double haversine=halfStep*halfStep*(1+cosine*Math.Cos(latitude+angle));
                int diagonal=(int)Math.Round(2*Math.Asin(Math.Sqrt(haversine))/angle*DistanceUnit);
                lengths[start+6]=lengths[start+7]=lengths[start+8+4]=lengths[start+8+5]=diagonal;
            }
            return lengths;
        }

        static int RoutingNeighbor(WorldData world,int cell,int direction)
        {
            if(direction<4)return world.SphereNeighbor(cell,direction);
            if(world.Water[cell]!=WaterKind.Land)return -1;
            int x=cell%world.Width,y=cell/world.Width;
            int nx=FMath.Wrap(x+((direction&1)==0?-1:1),world.Width);
            int ny=y+(direction<6?-1:1);
            if(ny<0||ny>=world.Height)return -1;
            int next=ny*world.Width+nx,across=y*world.Width+nx,along=ny*world.Width+x;
            // A lake or coastline receives flow across a shared face. A diagonal
            // between dry cells cannot bypass a shoreline in either side cell.
            if(world.Water[next]!=WaterKind.Land||world.Water[across]!=WaterKind.Land||
                world.Water[along]!=WaterKind.Land)return -1;
            // On the bilinear surface the endpoint grades along a diagonal have
            // the signs of (sideMean - start) and (end - sideMean). Require both
            // to descend. This excludes both a hidden ridge and a skipped trough.
            int sideSum=world.Elevation[across]+world.Elevation[along];
            int a=world.Elevation[cell],b=world.Elevation[next];
            return sideSum>=2*Math.Min(a,b)&&sideSum<=2*Math.Max(a,b)?next:-1;
        }

        static long[] SpillDistances(WorldData w,int[] lengths,CancellationToken cancel)
        {
            var distance=new long[w.Count];var flatCells=new List<int>();
            for(int i=0;i<w.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                if(w.Downstream[i]<0)continue;
                bool spill=false;
                for(int d=0;d<8&&!spill;d++)
                {
                    int n=RoutingNeighbor(w,i,d);
                    spill=n>=0&&w.WaterLevel[n]<w.WaterLevel[i];
                }
                if(!spill){distance[i]=long.MaxValue;flatCells.Add(i);}
            }
            // Seed only the inner fringe of each level area. Oceans and sloped
            // terrain resolve immediately and never enter this second heap.
            var heap=new FloodHeap();
            foreach(int i in flatCells)
            {
                int row=i/w.Width;
                for(int d=0;d<8;d++)
                {
                    int n=RoutingNeighbor(w,i,d);
                    if(n>=0&&distance[n]==0&&w.WaterLevel[n]==w.WaterLevel[i])
                        distance[i]=Math.Min(distance[i],lengths[row*8+d]);
                }
                if(distance[i]!=long.MaxValue)heap.Push(i,distance[i]);
            }
            int popped=0;
            while(heap.Count>0)
            {
                if((popped++&4095)==0)cancel.ThrowIfCancellationRequested();
                int i=heap.Pop(out long cost);
                if(cost!=distance[i])continue;
                for(int d=0;d<8;d++)
                {
                    int n=RoutingNeighbor(w,i,d);
                    if(n<0||w.WaterLevel[n]!=w.WaterLevel[i])continue;
                    long candidate=cost+lengths[(i/w.Width)*8+d];
                    if(candidate>=distance[n])continue;
                    distance[n]=candidate;heap.Push(n,candidate);
                }
            }
            return distance;
        }
        static float DrainageCorner(int x,int y,int period,uint seed)
        {
            unchecked{return FMath.Unit((uint)FMath.Wrap(x,period)*73428767u^(uint)y*912931u^seed);}
        }
        static float FlatRoutePriority(WorldData w,int cell,uint seed)
        {
            // A periodic, eight-tile field groups ambiguous routes into drainage
            // corridors. It only resolves exact ties in the physical spill gradient.
            int x=cell%w.Width,y=cell/w.Width,gx=x/8,gy=y/8,period=w.Width/8;
            float u=(x%8+.5f)/8,v=(y%8+.5f)/8;
            u=u*u*(3-2*u);v=v*v*(3-2*v);
            float south=FMath.Lerp(DrainageCorner(gx,gy,period,seed),DrainageCorner(gx+1,gy,period,seed),u);
            float north=FMath.Lerp(DrainageCorner(gx,gy+1,period,seed),DrainageCorner(gx+1,gy+1,period,seed),u);
            return FMath.Lerp(south,north,v);
        }
        public static void Build(WorldData w,CancellationToken cancel)
        {
            BuildDrainage(w,cancel);
            RouteRainfall(w,cancel);
        }
        internal static bool BuildDrainage(WorldData w,CancellationToken cancel)
        {
            var spill=(short[])w.Elevation.Clone();
            var visited=new bool[w.Count];var heap=new FloodHeap();int cursor=0;
            for(int i=0;i<w.Count;i++)
            {
                w.Downstream[i]=-1;
                if(FlatTerrainAnalysis.Ocean(w.Water[i])){visited[i]=true;spill[i]=0;heap.Push(i,0);}
            }
            int terminal=-1;
            if(heap.Count==0)
            {
                terminal=0;for(int i=1;i<w.Count;i++)if(w.Elevation[i]<w.Elevation[terminal])terminal=i;
                visited[terminal]=true;heap.Push(terminal,spill[terminal]);
            }
            while(heap.Count>0)
            {
                if((cursor&4095)==0)cancel.ThrowIfCancellationRequested();
                int i=heap.Pop();w.DrainOrder[cursor++]=i;
                for(int d=0;d<4;d++)
                {
                    int n=w.SphereNeighbor(i,d);if(visited[n])continue;
                    visited[n]=true;w.Downstream[n]=i;
                    spill[n]=Math.Max(w.Elevation[n],spill[i]);heap.Push(n,spill[n]);
                }
            }
            // A closed planet has no external spill level at its lowest point.
            // Its lowest connected floor can pool up to the first adjoining bank.
            if(terminal>=0)
            {
                Array.Clear(visited,0,w.Count);var floor=new List<int>{terminal};visited[terminal]=true;
                short bank=short.MaxValue;
                for(int k=0;k<floor.Count;k++)
                {
                    int i=floor[k];
                    for(int d=0;d<4;d++)
                    {
                        int n=w.SphereNeighbor(i,d);if(visited[n])continue;
                        if(w.Elevation[n]==w.Elevation[terminal]){visited[n]=true;floor.Add(n);}
                        else bank=Math.Min(bank,w.Elevation[n]);
                    }
                }
                if(bank<short.MaxValue)foreach(int i in floor)spill[i]=bank;
            }
            bool changed=FlatBasinStorage.Resolve(w,spill,cancel);
            SeedSurfaceOutlets(w,visited,cancel);
            var lengths=GroundLengths(w);
            var spillDistance=SpillDistances(w,lengths,cancel);
            uint routeSeed=FMath.Seed(w.Seed)^0x56a9d731u;
            for(int i=0;i<w.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                if(w.Downstream[i]<0)continue;
                int best=-1,bestDrop=0,bestLength=1,row=i/w.Width;
                for(int d=0;d<8;d++)
                {
                    int n=RoutingNeighbor(w,i,d);
                    if(n<0)continue;
                    int drop=w.WaterLevel[i]-w.WaterLevel[n],length=lengths[row*8+d];
                    if(drop<0||(drop==0&&spillDistance[i]-length!=spillDistance[n]))continue;
                    // Compare grades as exact ratios. On flat water the receiver
                    // is one full edge closer to the spill in ground distance.
                    long grade=(long)drop*bestLength-(long)bestDrop*length;
                    if(best<0||grade>0||(grade==0&&
                        (w.Elevation[n]<w.Elevation[best]||(w.Elevation[n]==w.Elevation[best]&&
                         FlatRoutePriority(w,n,routeSeed)<FlatRoutePriority(w,best,routeSeed)))))
                    {best=n;bestDrop=drop;bestLength=length;}
                }
                if(best<0)throw new InvalidOperationException("Missing hydrology spill at "+i);
                w.Downstream[i]=best;
            }
            RouteLakeBasins(w,spillDistance,cancel);
            return changed;
        }

        static void SeedSurfaceOutlets(WorldData w,bool[] visited,CancellationToken cancel)
        {
            Array.Clear(visited,0,w.Count);
            var queue=new int[w.Count];
            // Every level surface drains by a lower face, or contains exactly
            // one internal sink. A dry lake bed cannot route across a high rim.
            for(int i=0;i<w.Count;i++)
            {
                w.Downstream[i]=FlatTerrainAnalysis.Ocean(w.Water[i])?-1:0;
                visited[i]=w.Downstream[i]<0;
            }
            for(int start=0;start<w.Count;start++)
            {
                if((start&4095)==0)cancel.ThrowIfCancellationRequested();
                if(visited[start])continue;
                int root=start,tail=1;queue[0]=start;visited[start]=true;bool exits=false;
                for(int head=0;head<tail;head++)
                {
                    int i=queue[head];
                    if(w.Water[i]==WaterKind.Lake&&(w.Water[root]!=WaterKind.Lake||w.Elevation[i]<w.Elevation[root]))root=i;
                    for(int d=0;d<4;d++)
                    {
                        int n=w.SphereNeighbor(i,d);
                        if(w.WaterLevel[n]<w.WaterLevel[i]||(w.WaterLevel[n]==w.WaterLevel[i]&&FlatTerrainAnalysis.Ocean(w.Water[n])))exits=true;
                        if(visited[n]||w.WaterLevel[n]!=w.WaterLevel[i])continue;
                        visited[n]=true;queue[tail++]=n;
                    }
                }
                if(!exits)w.Downstream[root]=-1;
            }
        }
        static void RouteLakeBasins(WorldData w,long[] spillDistance,CancellationToken cancel)
        {
            var visited=new bool[w.Count];var queue=new int[w.Count];
            var shallowOutlets=new List<int>();
            for(int start=0;start<w.Count;start++)
            {
                if((start&4095)==0)cancel.ThrowIfCancellationRequested();
                if(visited[start]||w.Water[start]!=WaterKind.Lake)continue;
                int head=0,tail=1,outlet=start;queue[0]=start;visited[start]=true;
                while(head<tail)
                {
                    int cell=queue[head++];
                    if(spillDistance[cell]<spillDistance[outlet]||
                        (spillDistance[cell]==spillDistance[outlet]&&cell<outlet))outlet=cell;
                    for(int direction=0;direction<4;direction++)
                    {
                        int next=w.SphereNeighbor(cell,direction);
                        if(visited[next]||w.Water[next]!=WaterKind.Lake||w.WaterLevel[next]!=w.WaterLevel[start])continue;
                        visited[next]=true;queue[tail++]=next;
                    }
                }
                // A one- or two-cell positive-elevation pocket may be unresolved
                // channel relief. Deeper lakes, mapped basins and terminal sinks
                // always retain their standing surface.
                if(tail<=2&&w.Downstream[outlet]>=0)
                {
                    bool shallow=true;
                    for(int j=0;j<tail;j++)
                    {
                        int i=queue[j];
                        if(w.Elevation[i]<=0||w.WaterLevel[i]-w.Elevation[i]>12||
                            (w.WaterReference!=null&&w.WaterReference.At(i)>=w.Elevation[i]))shallow=false;
                    }
                    if(shallow)shallowOutlets.Add(outlet);
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
            ResolveShallowPockets(w,shallowOutlets,cancel);
        }
        static void ResolveShallowPockets(WorldData w,List<int> outlets,CancellationToken cancel)
        {
            if(outlets.Count==0)return;
            var areaByRow=new double[w.Height];
            for(int y=0;y<w.Height;y++)areaByRow[y]=FlatRiverDischarge.RelativeArea(y,w.Height);
            var catchment=new double[w.Count];
            for(int k=w.Count-1;k>=0;k--)
            {
                if((k&4095)==0)cancel.ThrowIfCancellationRequested();
                int i=w.DrainOrder[k],next=w.Downstream[i];
                catchment[i]+=areaByRow[i/w.Width];
                if(next>=0)catchment[next]+=catchment[i];
            }
            foreach(int outlet in outlets)
            {
                int other=-1;
                for(int d=0;d<4;d++)
                {
                    int n=w.SphereNeighbor(outlet,d);
                    if(w.Water[n]==WaterKind.Lake&&w.WaterLevel[n]==w.WaterLevel[outlet])other=n;
                }
                double area=areaByRow[outlet/w.Width],storage=area*(w.WaterLevel[outlet]-w.Elevation[outlet]);
                if(other>=0)
                {
                    double secondArea=areaByRow[other/w.Width];
                    area+=secondArea;storage+=secondArea*(w.WaterLevel[other]-w.Elevation[other]);
                }
                // If 100 mm of catchment runoff can fill the entire shallow
                // pocket, use its spill surface as a channel bed or an unfilled
                // depression. Actual climate yield still has to produce a stream.
                if(storage>.1*(catchment[outlet]-area))continue;
                w.Water[outlet]=WaterKind.Land;
                if(other>=0)w.Water[other]=WaterKind.Land;
            }
        }
        static bool HasRaisedBanks(WorldData w,int x,int y,int dx,int dy,float bankLevel)
        {
            int left=w.Index(x-dx,y-dy),right=w.Index(x+dx,y+dy);
            return left>=0&&right>=0&&!FlatTerrainAnalysis.AreaWater(w.Water[left])&&
                !FlatTerrainAnalysis.AreaWater(w.Water[right])&&
                w.Elevation[left]>=bankLevel&&w.Elevation[right]>=bankLevel;
        }
        internal static FlatRiverDischarge RouteRainfall(WorldData w,CancellationToken cancel)
        {
            float[] sources=FlatRiverDischarge.BuildSources(w,cancel,w.Runoff);
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
                float relief=Math.Max(1,max-min);
                float bankLevel=w.Elevation[i]+Math.Max(1,relief*.06f);
                if((count>0&&sum/count-w.Elevation[i]>.12f*relief)||
                    HasRaisedBanks(w,x,y,2,0,bankLevel)||HasRaisedBanks(w,x,y,0,2,bankLevel)||
                    HasRaisedBanks(w,x,y,2,2,bankLevel)||HasRaisedBanks(w,x,y,2,-2,bankLevel))
                    valleyReach[i]=3;
            }
            if(sourceTotal>0)for(int i=0;i<w.Count;i++)supply[i]*=(float)(landCount/sourceTotal);
            // A resolved convergent valley can initiate a smaller headwater
            // than a broad open slope. Use catchment and map span so this
            // distinction survives changes in terrain resolution.
            float span=(float)Math.Sqrt(landCount);
            float headwater=Math.Max(4,Math.Max(landCount*.00065f,span*.14f));
            float mainstem=4*Math.Max(6,Math.Max(landCount*.0015f,span*.20f));
            var branches=new byte[w.Count];
            for(int k=w.Count-1;k>=0;k--)
            {
                int i=w.DrainOrder[k],n=w.Downstream[i];if(n<0)continue;
                supply[n]+=supply[i];w.Runoff[n]+=w.Runoff[i];
                if(branches[i]>=2)branches[n]=2;
                else if(supply[i]>=headwater*.5f&&branches[n]<2)branches[n]++;
            }
            var streams=new bool[w.Count];var lakeOutlet=new bool[w.Count];var outletRoute=new bool[w.Count];
            for(int k=0;k<w.Count;k++)
            {
                int i=w.DrainOrder[k],n=w.Downstream[i];
                if(FlatTerrainAnalysis.Ocean(w.Water[i]))continue;
                if(n>=0&&w.Water[i]==WaterKind.Lake&&w.Runoff[i]>0)lakeOutlet[n]=true;
                // Give a hillside tributary the local valley it actually drains
                // into. Uniform parallel paths down a planar slope are sheet flow.
                if(n>=0)valleyReach[i]=Math.Max(valleyReach[i],valleyReach[n]-1);
                streams[i]=(valleyReach[i]>0&&supply[i]>=headwater)||(branches[i]>=2&&supply[i]>=mainstem);
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
            for(int i=0;i<w.Count;i++)streams[i]&=w.Water[i]==WaterKind.Land;
            // Atlas minimum-headwater pruning, including short reaches into lakes.
            // Preserve lake-fed outlet reaches so pruning cannot sever a lake chain.
            int minimumLength=Math.Max(3,(int)Math.Ceiling(Math.Sqrt(landCount)*.012));
            var incoming=new int[w.Count];bool changed;
            do
            {
                cancel.ThrowIfCancellationRequested();Array.Clear(incoming,0,w.Count);
                for(int i=0;i<w.Count;i++)
                {
                    int parent=w.Downstream[i];
                    if(streams[i]&&parent>=0&&streams[parent])incoming[parent]++;
                }
                changed=false;var remove=new List<int>();
                for(int i=0;i<w.Count;i++)
                {
                    if(!streams[i]||incoming[i]>0||outletRoute[i])continue;
                    int n=i,length=0;
                    do{length++;n=w.Downstream[n];}
                    while(n>=0&&streams[n]&&incoming[n]==1&&!outletRoute[n]&&length<minimumLength);
                    if(length>=minimumLength)continue;
                    n=i;for(int j=0;j<length;j++){remove.Add(n);n=w.Downstream[n];}
                }
                foreach(int i in remove){streams[i]=false;changed=true;}
            }while(changed);
            for(int i=0;i<w.Count;i++)if(streams[i])w.Water[i]=WaterKind.River;
            return FlatRiverDischarge.Route(w,sources,cancel);
        }
    }
}
