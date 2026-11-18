using System;
using System.Collections.Generic;
using System.Threading;

namespace Orbis.Flat
{
    // One-off fluvial landscape evolution for a newly created bed. Raw plate and
    // noise relief is full of closed pits, so drainage used to fill hundreds of
    // shallow spill lakes and break rivers into short reaches. Implicit stream
    // power incision (Braun & Willett 2013) plus hillslope diffusion carves
    // dendritic valleys to the sea. Inland seas and large deep basins remain lakes.
    // The result is remapped onto the source hypsometry, so height bands, snow
    // lines and biome shares keep their proportions; only ridge/valley layout
    // changes. Runs at world creation only, never on load or brush rebuilds.
    public static class FlatErosion
    {
        public const int Iterations=80;
        const float Erodibility=.0035f,AreaExponent=.5f,Diffusion=.06f,MinimumSlope=.8f;
        const float KeepLakeDepth=300,KeepLakeArea=10,KeepLakeMaxArea=160,Jitter=60,Hardness=.75f,Epsilon=1e-3f;

        public static void Apply(WorldData w,CancellationToken cancel)
        {
            int n=w.Count,width=w.Width;
            var h=new float[n];var fixedCell=new bool[n];var surface=new float[n];
            for(int i=0;i<n;i++)h[i]=w.Elevation[i];
            var rowScale=new float[w.Height];
            var eastStride=new int[w.Height];var eastWeight=new float[w.Height];
            double tileAngle=Math.PI/w.Height;
            for(int y=0;y<w.Height;y++)
            {
                rowScale[y]=(float)Math.Cos(((y+.5)/w.Height-.5)*Math.PI);
                // A fixed physical diffusion radius in both tangent directions.
                // The arc between two centres on a parallel is a great-circle distance.
                double offset=2*Math.Asin(Math.Min(1,Math.Sin(tileAngle*.5)/rowScale[y]))/tileAngle;
                int dx=eastStride[y]=Math.Max(1,(int)Math.Round(offset));
                double lengthEW=2*Math.Asin(rowScale[y]*Math.Sin(dx*tileAngle*.5))/tileAngle;
                eastWeight[y]=(float)(1/(lengthEW*lengthEW));
            }
            // The ocean is the largest connected sub-sea-level region, as in water detection.
            var seen=new bool[n];var queue=new Queue<int>();var ocean=new List<int>();
            for(int i=0;i<n;i++)
            {
                if(seen[i]||h[i]>0)continue;
                var part=new List<int>();seen[i]=true;queue.Enqueue(i);
                while(queue.Count>0)
                {
                    int c=queue.Dequeue();part.Add(c);
                    for(int d=0;d<4;d++){int m=w.SphereNeighbor(c,d);if(m>=0&&!seen[m]&&h[m]<=0){seen[m]=true;queue.Enqueue(m);}}
                }
                if(part.Count>ocean.Count)ocean=part;
            }
            if(ocean.Count==0||ocean.Count==n)return;
            var isOcean=new bool[n];
            foreach(int i in ocean){isOcean[i]=fixedCell[i]=true;surface[i]=0;}
            // Inland seas and large deep basins stay at their spill surface. Every
            // other closed land basin is treated as sediment-filled.
            float areaScale=(width/512f)*(width/512f);
            var fill=Flood(w,h,fixedCell,surface,0);Array.Clear(seen,0,n);var basin=new List<int>();
            for(int i=0;i<n;i++)
            {
                if(seen[i]||fixedCell[i]||fill[i]-h[i]<=.5f)continue;
                basin.Clear();seen[i]=true;queue.Enqueue(i);float depth=0;double area=0;bool sea=false;
                while(queue.Count>0)
                {
                    int c=queue.Dequeue();basin.Add(c);sea|=h[c]<=0;depth=Math.Max(depth,fill[c]-h[c]);area+=rowScale[c/width];
                    for(int d=0;d<4;d++)
                    {
                        int m=w.SphereNeighbor(c,d);
                        if(m>=0&&!seen[m]&&!fixedCell[m]&&fill[m]-h[m]>.5f){seen[m]=true;queue.Enqueue(m);}
                    }
                }
                if(sea||(depth>=KeepLakeDepth&&area>=KeepLakeArea*areaScale&&area<=KeepLakeMaxArea*areaScale))foreach(int c in basin){fixedCell[c]=true;surface[c]=fill[c];}
            }
            var hypsometry=new List<float>();
            for(int i=0;i<n;i++)if(!fixedCell[i])hypsometry.Add(h[i]);
            hypsometry.Sort();
            for(int i=0;i<n;i++)if(fixedCell[i])h[i]=surface[i];
            // Variable rock strength and a little initial roughness let channels
            // wander instead of following the straight fall line of a smooth dome.
            uint seed=FMath.Seed(w.Seed)^0x5e0d1a7bu;var strength=new float[n];
            for(int i=0;i<n;i++)
            {
                if(fixedCell[i])continue;
                double lat=((i/width+.5)/w.Height-.5)*Math.PI,lon=((i%width+.5)/width-.5)*2*Math.PI;
                float radius=(float)(width/(2*Math.PI));
                float x=(float)Math.Cos(lon)*rowScale[i/width]*radius,y=(float)Math.Sin(lon)*rowScale[i/width]*radius,z=(float)Math.Sin(lat)*radius;
                strength[i]=(float)Math.Exp(Hardness*FlatTectonics.Fbm(x/16+.31f,y/16+.43f,z/16+.21f,seed,4));
                h[i]=Math.Max(1,h[i]+Jitter*FlatTectonics.Fbm(x/4+.37f,y/4+.19f,z/4+.53f,seed^0x9e3779b9u,4));
            }
            var receiver=new int[n];var length=new float[n];var area2=new float[n];var order=new int[n];
            var donors=new int[n];var donorStart=new int[n+1];var donorCount=new int[n];var smooth=new float[n];
            for(int iteration=0;iteration<Iterations;iteration++)
            {
                cancel.ThrowIfCancellationRequested();
                var filled=Flood(w,h,fixedCell,surface,Epsilon);
                for(int i=0;i<n;i++)if(!fixedCell[i])h[i]=filled[i];
                // Steepest admissible descent: the hydrology's own diagonal rule.
                for(int i=0;i<n;i++)
                {
                    receiver[i]=i;length[i]=1;if(fixedCell[i])continue;
                    int y=i/width;float best=0;
                    for(int d=0;d<8;d++)
                    {
                        int m=Neighbor(w,i,d);if(m<0)continue;
                        if(d>=4)
                        {
                            int across=y*width+m%width,along=(m/width)*width+i%width;float side=h[across]+h[along];
                            if(side<2*Math.Min(h[i],h[m])||side>2*Math.Max(h[i],h[m]))continue;
                        }
                        float run=d<2?rowScale[y]:d<4?1:(float)Math.Sqrt(rowScale[y]*rowScale[y]+1);
                        float slope=(h[i]-h[m])/run;
                        if(slope>best){best=slope;receiver[i]=m;length[i]=run;}
                    }
                }
                Array.Clear(donorCount,0,n);
                for(int i=0;i<n;i++)if(receiver[i]!=i)donorCount[receiver[i]]++;
                donorStart[0]=0;for(int i=0;i<n;i++)donorStart[i+1]=donorStart[i]+donorCount[i];
                Array.Clear(donorCount,0,n);
                for(int i=0;i<n;i++)if(receiver[i]!=i){int r=receiver[i];donors[donorStart[r]+donorCount[r]++]=i;}
                int count=0;
                for(int i=0;i<n;i++)if(receiver[i]==i)order[count++]=i;
                for(int k=0;k<count;k++){int c=order[k];for(int j=donorStart[c];j<donorStart[c+1];j++)order[count++]=donors[j];}
                for(int i=0;i<n;i++)area2[i]=fixedCell[i]?0:rowScale[i/width];
                for(int k=count-1;k>=0;k--){int c=order[k];if(receiver[c]!=c)area2[receiver[c]]+=area2[c];}
                // Implicit stream power, n=1: unconditionally stable for any step.
                for(int k=0;k<count;k++)
                {
                    int c=order[k],r=receiver[c];if(fixedCell[c]||r==c)continue;
                    float f=Erodibility*strength[c]*(float)Math.Pow(area2[c]*100,AreaExponent)/length[c];
                    h[c]=Math.Max((h[c]+f*h[r])/(1+f),Math.Max(1,h[r]+MinimumSlope*length[c]));
                }
                Array.Copy(h,smooth,n);
                for(int i=0;i<n;i++)
                {
                    if(fixedCell[i])continue;
                    int row=i/width,x=i%width,dx=eastStride[row];float ew=eastWeight[row];
                    float horizontal=smooth[w.Index(x-dx,row)]+smooth[w.Index(x+dx,row)];
                    float vertical=smooth[w.SphereNeighbor(i,2)]+smooth[w.SphereNeighbor(i,3)];
                    float average=(horizontal*ew+vertical)/(2*(1+ew));
                    h[i]=Math.Max(1,smooth[i]+Diffusion*(average-smooth[i]));
                }
            }
            // Remap onto the source hypsometry by rank. Lakes ride the same monotone
            // map: the surface follows its shore and the bed keeps its depth.
            var final=Flood(w,h,fixedCell,surface,Epsilon);var land=new List<int>();
            for(int i=0;i<n;i++)if(!fixedCell[i])land.Add(i);
            land.Sort((a,b)=>final[a]!=final[b]?final[a].CompareTo(final[b]):a.CompareTo(b));
            var level=new int[n];var ranked=new float[land.Count];
            for(int k=0;k<land.Count;k++){level[land[k]]=(int)Math.Round(hypsometry[k]);ranked[k]=final[land[k]];}
            var shift=new int[n];
            for(int i=0;i<n;i++)
            {
                if(!fixedCell[i])continue;
                if(isOcean[i]){level[i]=0;continue;}
                int k=Array.BinarySearch(ranked,surface[i]);if(k<0)k=~k;
                float mapped=land.Count>0?hypsometry[Math.Min(Math.Max(k,0),land.Count-1)]:surface[i];
                shift[i]=(int)Math.Round(mapped-surface[i]);level[i]=(int)Math.Ceiling(mapped);
            }
            Descend(w,level,fixedCell);
            for(int i=0;i<n;i++)
            {
                if(!fixedCell[i])w.Elevation[i]=(short)FMath.Clamp(level[i],1,4000);
                else if(shift[i]!=0)w.Elevation[i]=(short)FMath.Clamp(w.Elevation[i]+shift[i],-4000,4000);
            }
        }

        static int Neighbor(WorldData w,int i,int direction)
        {
            if(direction<4)return w.SphereNeighbor(i,direction);
            int x=i%w.Width,y=i/w.Width,ny=y+(direction<6?-1:1);
            if(ny<0||ny>=w.Height)return -1;
            return ny*w.Width+FMath.Wrap(x+((direction&1)==0?-1:1),w.Width);
        }

        // Priority flood from fixed water surfaces. eps>0 leaves a draining gradient.
        static float[] Flood(WorldData w,float[] h,bool[] fixedCell,float[] surface,float eps)
        {
            int n=w.Count;var f=new float[n];var done=new bool[n];var heap=new FloodHeap();
            for(int i=0;i<n;i++)if(fixedCell[i]){f[i]=surface[i];done[i]=true;}
            for(int i=0;i<n;i++)
            {
                if(!fixedCell[i])continue;
                for(int d=0;d<4;d++)
                {
                    int m=w.SphereNeighbor(i,d);if(m<0||done[m])continue;
                    done[m]=true;f[m]=Math.Max(h[m],surface[i]+eps);heap.Push(m,Key(f[m]));
                }
            }
            while(heap.Count>0)
            {
                int c=heap.Pop(out _);
                for(int d=0;d<4;d++)
                {
                    int m=w.SphereNeighbor(c,d);if(m<0||done[m])continue;
                    done[m]=true;f[m]=Math.Max(h[m],f[c]+eps);heap.Push(m,Key(f[m]));
                }
            }
            for(int i=0;i<n;i++)if(!done[i])f[i]=h[i];
            return f;
        }
        // Order-preserving fixed-point key (millimetres) for the shared flood heap.
        static long Key(float height) { return (long)Math.Round(height*1000.0); }

        // Integer metres: every dry cell gets a strictly lower 4-neighbour path to water.
        static void Descend(WorldData w,int[] h,bool[] fixedCell)
        {
            int n=w.Count;var done=new bool[n];var heap=new FloodHeap();
            for(int i=0;i<n;i++)
            {
                if(!fixedCell[i])continue;done[i]=true;
                for(int d=0;d<4;d++)
                {
                    int m=w.SphereNeighbor(i,d);if(m<0||fixedCell[m]||done[m])continue;
                    done[m]=true;h[m]=Math.Max(h[m],Math.Max(1,h[i]+1));heap.Push(m,h[m]);
                }
            }
            while(heap.Count>0)
            {
                int c=heap.Pop(out _);
                for(int d=0;d<4;d++)
                {
                    int m=w.SphereNeighbor(c,d);if(m<0||done[m])continue;
                    done[m]=true;h[m]=Math.Max(h[m],h[c]+1);heap.Push(m,h[m]);
                }
            }
        }
    }
}
