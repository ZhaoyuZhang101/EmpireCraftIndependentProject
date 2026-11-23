using System;
using System.Collections.Generic;
namespace Orbis.Flat
{
    public static class FlatPathfinder
    {
        // Dijkstra; integer costs preserve heap determinism and avoid inadmissible wrap heuristics.
        // Called only on explicit route requests, not every frame.
        public static int[] Find(WorldData w,int start,int goal,int budget=100000)
        {
            if(!w.Walkable(start)||!w.Walkable(goal))return new int[0];
            var dist=new int[w.Count];var parent=new int[w.Count];var closed=new bool[w.Count];
            for(int i=0;i<w.Count;i++){dist[i]=int.MaxValue;parent[i]=-1;}
            var heap=new FloodHeap();dist[start]=0;heap.Push(start,0);
            while(heap.Count>0&&budget-->0)
            {
                int i=heap.Pop();if(closed[i])continue;closed[i]=true;
                if(i==goal){var p=new List<int>();for(int j=i;j>=0;j=parent[j])p.Add(j);p.Reverse();return p.ToArray();}
                for(int d=0;d<4;d++)
                {
                    int n=w.Neighbor(i,d);if(!w.Walkable(n)||closed[n]||!FlatGeographicSurface.GroundEdge(w,i,n))continue;
                    int cost=10+Math.Abs(w.Elevation[n]-w.Elevation[i])/60;
                    if(w.Water[n]==WaterKind.River&&w.Buildings[n]!=Structure.Bridge)cost+=25;
                    if(w.Buildings[n]==Structure.Road||w.Buildings[n]==Structure.Bridge)cost=5;
                    int value=dist[i]+cost;if(value<dist[n]){dist[n]=value;parent[n]=i;heap.Push(n,value);}
                }
            }
            return new int[0];
        }
    }
}
