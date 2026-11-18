using System;
using System.Threading;

namespace Orbis.Flat
{
    /// <summary>Presentation-only reconstruction. Never writes authoring heights or drainage.</summary>
    public static class FlatSurfaceSampling
    {
        static float Minmod(float a,float b)
        { return a*b<=0?0:Math.Sign(a)*Math.Min(Math.Abs(a),Math.Abs(b)); }

        // Uniform-grid Hermite interpolation with minmod tangents. Interpolatory,
        // bounded on each interval, and C1 across tile centres. Same formula in HLSL.
        public static float Cubic(float p0,float p1,float p2,float p3,float t)
        {
            float m1=Minmod(p1-p0,p2-p1),m2=Minmod(p2-p1,p3-p2),t2=t*t,t3=t2*t;
            return (2*t3-3*t2+1)*p1+(t3-2*t2+t)*m1+(-2*t3+3*t2)*p2+(t3-t2)*m2;
        }
        public static float Sample(float[] values,int width,int height,float x,float y)
        {
            int bx=(int)Math.Floor(x-.5f),by=(int)Math.Floor(y-.5f);
            float tx=x-.5f-bx,ty=y-.5f-by;
            float a=Row(values,width,height,bx,by-1,tx),b=Row(values,width,height,bx,by,tx);
            float c=Row(values,width,height,bx,by+1,tx),d=Row(values,width,height,bx,by+2,tx);
            return Cubic(a,b,c,d,ty);
        }
        public static float ShoreDetail(float x,float y,int width,int height)
        {
            float fx=x-(float)Math.Floor(x)-.5f,fy=y-(float)Math.Floor(y)-.5f;
            float anchor=FMath.Clamp((fx*fx+fy*fy)/.08f,0,1);anchor=anchor*anchor*(3-2*anchor);
            double longitude=(x/width-.5)*Math.PI*2,latitude=(y/height-.5)*Math.PI;
            double px=Math.Sin(longitude)*Math.Cos(latitude),py=Math.Sin(latitude),pz=Math.Cos(longitude)*Math.Cos(latitude);
            double detail=.65*Math.Sin(px*157+pz*93+Math.Sin(py*83))+.35*Math.Sin(px*311-pz*193+py*137);
            return (float)detail*.045f*anchor;
        }
        public static float SampleShore(WorldData world,float x,float y)
        { return SampleShore(world,world.Facts,x,y); }
        internal static float SampleShore(WorldData world,FlatTerrainFacts facts,float x,float y)
        {
            float distance=Sample(facts.ShoreDistance,world.Width,world.Height,x,y);
            float fade=FMath.Clamp((Math.Abs(distance)-.2f)/.7f,0,1);fade=fade*fade*(3-2*fade);
            return distance+ShoreDetail(x,y,world.Width,world.Height)*(1-fade);
        }
        static float Row(float[] values,int w,int h,int x,int y,float t)
        {
            int row=FMath.Clamp(y,0,h-1)*w;
            return Cubic(values[row+FMath.Wrap(x-1,w)],values[row+FMath.Wrap(x,w)],
                         values[row+FMath.Wrap(x+1,w)],values[row+FMath.Wrap(x+2,w)],t);
        }

        // Squared Euclidean distance transform: lower envelope of parabolas.
        // Three horizontal copies give an exact longitude-periodic result;
        // latitude is not periodic. Finite sentinel also handles all-land/all-water.
        public static float[] DistanceSquared(bool[] seeds,int width,int height,CancellationToken cancel)
        {
            if(seeds==null||seeds.Length!=width*height||width<1||height<1)throw new ArgumentException("Invalid distance field.");
            var vertical=new float[seeds.Length];var result=new float[seeds.Length];
            int extent=Math.Max(height,width*3);
            var input=new double[extent];var output=new double[extent];
            var sites=new int[extent];var breaks=new double[extent+1];
            for(int x=0;x<width;x++)
            {
                if((x&31)==0)cancel.ThrowIfCancellationRequested();
                for(int y=0;y<height;y++)input[y]=seeds[y*width+x]?0:1e12;
                Transform1D(input,output,height,sites,breaks);
                for(int y=0;y<height;y++)vertical[y*width+x]=(float)output[y];
            }
            for(int y=0;y<height;y++)
            {
                if((y&15)==0)cancel.ThrowIfCancellationRequested();
                for(int x=0;x<width*3;x++)input[x]=vertical[y*width+x%width];
                Transform1D(input,output,width*3,sites,breaks);
                for(int x=0;x<width;x++)result[y*width+x]=(float)output[x+width];
            }
            return result;
        }
        static void Transform1D(double[] source,double[] target,int count,int[] sites,double[] breaks)
        {
            int k=0;sites[0]=0;breaks[0]=double.NegativeInfinity;breaks[1]=double.PositiveInfinity;
            for(int q=1;q<count;q++)
            {
                double intersection;
                while(true)
                {
                    int p=sites[k];
                    intersection=((source[q]+(double)q*q)-(source[p]+(double)p*p))/(2*(q-p));
                    if(intersection>breaks[k]||k==0)break;
                    k--;
                }
                k++;sites[k]=q;breaks[k]=intersection;breaks[k+1]=double.PositiveInfinity;
            }
            k=0;
            for(int q=0;q<count;q++)
            {
                while(breaks[k+1]<q)k++;
                double delta=q-sites[k];target[q]=delta*delta+source[sites[k]];
            }
        }
        public static void BuildShore(WorldData world,FlatTerrainFacts facts,CancellationToken cancel)
        {
            var wet=new bool[world.Count];var dry=new bool[world.Count];
            for(int i=0;i<world.Count;i++){wet[i]=FlatTerrainAnalysis.AreaWater(world.Water[i]);dry[i]=!wet[i];}
            float[] toWet=DistanceSquared(wet,world.Width,world.Height,cancel);
            float[] toDry=DistanceSquared(dry,world.Width,world.Height,cancel);
            for(int i=0;i<world.Count;i++)
                facts.ShoreDistance[i]=wet[i]?Math.Min(8,(float)Math.Sqrt(toDry[i])-.5f):-Math.Min(8,(float)Math.Sqrt(toWet[i])-.5f);
            // Smooth quantisation of the binary boundary, not the world or its IDs.
            // A constrained 2-pass [1 2 1] filter reduces tile staircases; retaining
            // the sign at EVERY tile centre prevents deleting one-tile islands/lakes.
            // The resulting display field is no longer an exact metric distance.
            float[] filtered=SmoothShore(facts.ShoreDistance,wet,world.Width,world.Height);
            Array.Copy(filtered,facts.ShoreDistance,world.Count);
            for(int i=0;i<world.Count;i++)
            {
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                float edge=1;bool boundary=false;
                for(int direction=0;direction<4;direction++)
                {
                    int next=world.Neighbor(i,direction);
                    if(next<0||wet[next]==wet[i])continue;
                    int water=wet[i]?i:next,land=wet[i]?next:i;
                    float depth=Math.Max(.25f,world.WaterLevel[water]-world.Elevation[water]);
                    float bank=Math.Max(1,world.Elevation[land]-world.WaterLevel[water]);
                    float crossing=FMath.Clamp(depth/(depth+bank),.10f,.90f);
                    edge=Math.Min(edge,wet[i]?crossing:1-crossing);boundary=true;
                }
                // A horizontal water surface intersects the sloping bed, which
                // need not be halfway between the wet and dry cell centres.
                if(boundary)facts.ShoreDistance[i]=wet[i]?edge:-edge;
            }
        }
        public static float[] SmoothShore(float[] raw,bool[] wet,int width,int height)
        {
            var current=(float[])raw.Clone();var temp=new float[raw.Length];
            for(int pass=0;pass<2;pass++)
            {
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    float total=0;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {
                        int n=FMath.Clamp(y+dy,0,height-1)*width+FMath.Wrap(x+dx,width);
                        total+=current[n]*(dx==0?2:1)*(dy==0?2:1);
                    }
                    temp[y*width+x]=total/16;
                }
                var swap=current;current=temp;temp=swap;
            }
            for(int i=0;i<current.Length;i++)current[i]=wet[i]?Math.Max(.15f,current[i]):Math.Min(-.15f,current[i]);
            return current;
        }
    }
}
