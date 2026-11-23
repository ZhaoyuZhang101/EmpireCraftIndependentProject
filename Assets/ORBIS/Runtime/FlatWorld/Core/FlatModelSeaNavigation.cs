using System;
using System.Collections.Generic;
using System.Threading;

namespace Orbis.Flat
{
    // The continuous area-water field on a generated map also resolves the
    // shoulders of a model island, bay or lake inside a single simulation cell.
    internal static class FlatModelSeaNavigation
    {
        static FlatSeaPoint Point(WorldData w,double x,double y) => new FlatSeaPoint(
            FlatGeographicSurface.Wrap(x*360/w.Width-180),Math.Max(-90,Math.Min(90,y*180/w.Height-90)));

        internal static bool Segment(WorldData w,FlatSeaPoint a,FlatSeaPoint b,bool sea=true,bool bridge=false)
        {
            double dx=FlatGeographicSurface.Wrap(b.Longitude-a.Longitude)*w.Width/360;
            double dy=(b.Latitude-a.Latitude)*w.Height/180;
            double x=(a.Longitude+180)*w.Width/360,y=(a.Latitude+90)*w.Height/180;
            double length=Math.Sqrt(dx*dx+dy*dy);
            bool Pass(double t)
            {
                var point=Point(w,x+dx*t,y+dy*t);
                double shore;WaterKind kind;
                if(w.GeographicSurface!=null)
                { kind=w.GeographicSurface.At(w,point,out double km,out _);shore=km*w.Width/40030.2; }
                else kind=FlatGeographicSurface.ModelAt(w,point,out shore);
                if(sea && w.Facts==null)
                {
                    int tile=w.Index((int)((point.Longitude+180)*w.Width/360),Math.Min(w.Height-1,(int)((point.Latitude+90)*w.Height/180)));
                    if(!FlatSeaNavigation.IsMarineCell(w,tile))return false;
                }
                // Exact water picks may end close to a shore. Use a clearance
                // between them to reject a line that grazes a dry corner.
                double margin=Math.Min(.028,Math.Min(t,1-t)*length*.5);
                return sea ? FlatTerrainAnalysis.Ocean(kind) && shore>margin :
                    (kind==WaterKind.Land||kind==WaterKind.River) && -shore>=margin || bridge && kind==WaterKind.Lake;
            }
            if(!Pass(0)||!Pass(1))return false;
            int n=Math.Max(1,(int)Math.Ceiling(Math.Max(Math.Abs(dx),Math.Abs(dy))*96));
            for(int i=1;i<n;i++)if(!Pass((double)i/n))return false;
            return true;
        }
        internal static bool GroundEdge(WorldData w,int from,int to,bool bridge)
        {
            var a=FlatGeographicSurface.Center(w,from);var b=FlatGeographicSurface.Center(w,to);
            if(from/w.Width!=to/w.Width || Math.Abs(from-to)!=w.Width/2)return Segment(w,a,b,false,bridge);
            double pole=a.Latitude<0?-90:90;
            return Segment(w,a,new FlatSeaPoint(a.Longitude,pole),false,bridge) &&
                Segment(w,new FlatSeaPoint(b.Longitude,pole),b,false,bridge);
        }
        internal static bool TryRoute(WorldData world,FlatSeaPoint start,FlatSeaPoint end,out FlatSeaPoint[] path,CancellationToken cancel)
        {
            path=Array.Empty<FlatSeaPoint>();
            if(Segment(world,start,end)){path=new[]{start,end};return true;}
            double ax=(start.Longitude+180)*world.Width/360,ay=(start.Latitude+90)*world.Height/180;
            double bx=ax+FlatGeographicSurface.Wrap(end.Longitude-start.Longitude)*world.Width/360,by=(end.Latitude+90)*world.Height/180;
            if(Math.Max(Math.Abs(ax-bx),Math.Abs(ay-by))>6)return false;
            int x0=(int)Math.Floor(Math.Min(ax,bx))-2,x1=(int)Math.Ceiling(Math.Max(ax,bx))+2;
            int y0=Math.Max(0,(int)Math.Floor(Math.Min(ay,by))-2),y1=Math.Min(world.Height,(int)Math.Ceiling(Math.Max(ay,by))+2);
            // Each rectangle spans at most eleven simulation cells per axis.
            // Refine only when the coarser lattice misses a connected narrow inlet.
            for(int resolution=8;resolution<=32;resolution*=2)
            {
                cancel.ThrowIfCancellationRequested();
                int width=(x1-x0)*resolution,height=(y1-y0)*resolution;
                var distance=new int[width*height];var parent=new int[distance.Length];
                Array.Fill(distance,int.MaxValue);
                FlatSeaPoint At(int i)=>Point(world,x0+(i%width+.5)/resolution,y0+(i/width+.5)/resolution);
                int Attach(double x,double y,FlatSeaPoint p)
                {
                    int cx=(int)Math.Floor((x-x0)*resolution),cy=(int)Math.Floor((y-y0)*resolution);
                    for(int r=0;r<=3;r++)for(int j=-r;j<=r;j++)for(int i=-r;i<=r;i++)
                    {
                        if(Math.Max(Math.Abs(i),Math.Abs(j))!=r)continue;
                        int col=cx+i,row=cy+j;if(col<0||col>=width||row<0||row>=height)continue;
                        int n=row*width+col;if(Segment(world,p,At(n)))return n;
                    }
                    return -1;
                }
                int first=Attach(ax,ay,start),last=Attach(bx,by,end);
                if(first<0||last<0)continue;
                int Estimate(int n)=>10*(Math.Abs(n%width-last%width)+Math.Abs(n/width-last/width));
                var heap=new FloodHeap();distance[first]=0;parent[first]=-1;heap.Push(first,Estimate(first));
                int visits=0;
                while(heap.Count>0)
                {
                    if((visits++&255)==0)cancel.ThrowIfCancellationRequested();
                    int i=heap.Pop(out long rank);if(rank!=distance[i]+Estimate(i))continue;
                    if(i==last)break;
                    for(int d=0;d<4;d++)
                    {
                        int col=i%width+(d==0?-1:d==1?1:0),row=i/width+(d==2?-1:d==3?1:0);
                        if(col<0||col>=width||row<0||row>=height)continue;
                        int j=row*width+col,cost=distance[i]+10;
                        if(cost>=distance[j]||!Segment(world,At(i),At(j)))continue;
                        distance[j]=cost;parent[j]=i;heap.Push(j,cost+Estimate(j));
                    }
                }
                if(distance[last]==int.MaxValue)continue;
                var raw=new List<FlatSeaPoint>{end};
                for(int i=last;i>=0;i=parent[i])raw.Add(At(i));
                raw.Add(start);raw.Reverse();
                var result=new List<FlatSeaPoint>{start};
                for(int i=0;i+1<raw.Count;)
                {
                    cancel.ThrowIfCancellationRequested();
                    int best=i+1;
                    while(best+1<raw.Count && Segment(world,raw[i],raw[best+1]))best++;
                    result.Add(raw[best]);i=best;
                }
                path=result.ToArray();return true;
            }
            return false;
        }
    }
}
