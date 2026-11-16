using System;
namespace Orbis.Flat
{
    public static class FlatWaterGeometry
    {
        public const int CurveSteps=8;
        static float Smooth(float a,float b,float x) { float t=FMath.Clamp((x-a)/(b-a),0,1);return t*t*(3-2*t); }
        static void Edge(int direction,out float x,out float y)
        {x=direction==0?0:direction==1?1:.5f;y=direction==2?0:direction==3?1:.5f;}
        static void RiverEdge(WorldData w,int i,int direction,out float x,out float y)
        {
            Edge(direction,out x,out y);
            int tx=i%w.Width,ty=i/w.Width,cycles=Math.Max(1,(int)Math.Floor(w.Width/12f+.5f));
            float frequency=(float)(2*Math.PI)*cycles/w.Width;
            float phase=direction<2?FMath.Wrap(tx+(direction==1?1:0),w.Width)*frequency+ty*.37f:(ty+(direction==3?1:0))*.52f+tx*frequency;
            float cross=.5f+.13f*(float)Math.Sin(phase);
            if(direction<2)y=cross;else x=cross;
        }
        static float EdgeWidth(WorldData w,int i,int direction)
        {
            int n=w.Neighbor(i,direction);float r=w.Facts.HalfWidth[i];
            return n>=0&&w.Facts.HalfWidth[n]>.01f?(r+w.Facts.HalfWidth[n])*.5f:r;
        }
        // Distance to eight short chords of a quadratic Bezier. The convex control
        // polygon stays inside the same authoritative tile: no spline cutting hills.
        public static float BezierDistance(float x,float y,float ax,float ay,float bx,float by,
                                           float cx,float cy,float ra,float rb,float rc,float bend=0)
        {
            float distance=8,previousX=ax,previousY=ay,previousRadius=ra;
            float length=(float)Math.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay));
            float nx=length>1e-8f?-(by-ay)/length:0,ny=length>1e-8f?(bx-ax)/length:0;
            for(int k=1;k<=CurveSteps;k++)
            {
                float t=k/(float)CurveSteps,s=1-t;
                float qx=s*s*ax+2*s*t*cx+t*t*bx,qy=s*s*ay+2*s*t*cy+t*t*by;
                // Bounded S-bend with zero displacement and derivative at shared faces.
                float meander=bend*32*t*t*s*s*(2*t-1);qx+=nx*meander;qy+=ny*meander;
                float radius=s*s*ra+2*s*t*rc+t*t*rb;
                float dx=qx-previousX,dy=qy-previousY,den=dx*dx+dy*dy;
                float along=den>1e-10f?FMath.Clamp(((x-previousX)*dx+(y-previousY)*dy)/den,0,1):0;
                float ex=x-previousX-along*dx,ey=y-previousY-along*dy;
                distance=Math.Min(distance,(float)Math.Sqrt(ex*ex+ey*ey)-FMath.Lerp(previousRadius,radius,along));
                previousX=qx;previousY=qy;previousRadius=radius;
            }
            return distance;
        }
        public static float RiverDistance(WorldData w,int i,float x,float y)
        {
            var f=w.Facts;if(f==null||f.HalfWidth[i]<.01f)return 8;
            int mask=f.Connections[i],count=0,outlet=-1;float best=-2;
            for(int d=0;d<4;d++)
            {
                if((mask&(1<<d))==0)continue;count++;
                float alignment=d==0?-f.FlowX[i]:d==1?f.FlowX[i]:d==2?-f.FlowY[i]:f.FlowY[i];
                if(alignment>best){best=alignment;outlet=d;}
            }
            float r=f.HalfWidth[i];
            if(count==0)return (float)Math.Sqrt((x-.5f)*(x-.5f)+(y-.5f)*(y-.5f))-r;
            float bx,by;RiverEdge(w,i,outlet,out bx,out by);float rb=EdgeWidth(w,i,outlet);
            float bend=f.Meander[i];
            float sourceRadius=Math.Max(.003f,r*.18f);
            float result=count==1?BezierDistance(x,y,.5f,.5f,bx,by,(.5f+bx)*.5f,(.5f+by)*.5f,sourceRadius,rb,(sourceRadius+rb)*.5f,bend*.35f):8;
            if(count>1)for(int d=0;d<4;d++)
            {
                if(d==outlet||(mask&(1<<d))==0)continue;
                float ax,ay;RiverEdge(w,i,d,out ax,out ay);bool straight=(d^1)==outlet;
                result=Math.Min(result,BezierDistance(x,y,ax,ay,bx,by,straight?(ax+bx)*.5f:.5f,straight?(ay+by)*.5f:.5f,EdgeWidth(w,i,d),rb,r,bend*(straight?1:.35f)));
            }
            // Enforce the SAME cross-section at a shared tile face. A varying-width
            // capsule can otherwise bulge from only one side of a clipped curve.
            float closest=.10f,section=0;
            for(int d=0;d<4;d++)if((mask&(1<<d))!=0)
            {
                float face=d==0?x:d==1?1-x:d==2?y:1-y;
                float ex,ey;RiverEdge(w,i,d,out ex,out ey);float cross=Math.Abs((d<2?y:x)-(d<2?ey:ex));
                if(face<closest){closest=face;section=cross-EdgeWidth(w,i,d);}
            }
            if(closest<.10f)result=FMath.Lerp(section,result,Smooth(0,.10f,closest));
            return result;
        }
        public static float MouthDistance(WorldData w,int i,float x,float y)
        {
            if(w.Facts.ShoreDistance[i]<=0)return 8;
            float result=8;
            for(int d=0;d<4;d++)
            {
                int n=w.Neighbor(i,d),opposite=d^1;
                if(n<0||w.Facts.HalfWidth[n]<.01f||(w.Facts.Connections[n]&(1<<opposite))==0)continue;
                float ax,ay;RiverEdge(w,i,d,out ax,out ay);float dx=.5f-ax,dy=.5f-ay;
                float t=FMath.Clamp(((x-ax)*dx+(y-ay)*dy)/(dx*dx+dy*dy),0,1);
                float radius=FMath.Lerp(w.Facts.HalfWidth[n],Math.Min(.24f,w.Facts.HalfWidth[n]*1.18f),t);
                float ex=x-ax-dx*t,ey=y-ay-dy*t;
                float distance=(float)Math.Sqrt(ex*ex+ey*ey)-radius;
                float face=d==0?x:d==1?1-x:d==2?y:1-y,cross=Math.Abs((d<2?y:x)-(d<2?ay:ax));
                if(face<.10f)distance=FMath.Lerp(cross-w.Facts.HalfWidth[n],distance,Smooth(0,.10f,face));
                result=Math.Min(result,distance);
            }
            return result;
        }
        // Identical signed field, interpolation and Bezier chords to the rendering shader.
        public static float Coverage(WorldData w,float x,float y,out float depth)
        {
            depth=0;if(w.Facts==null||y<0||y>=w.Height)return 0;
            int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y),i=w.Index(ix,iy);if(i<0)return 0;
            float sd=FlatSurfaceSampling.SampleShore(w,x,y);
            float area=Smooth(-.025f,.025f,sd);
            float distance=RiverDistance(w,i,x-ix,y-iy);
            if(sd<=1.25f)distance=Math.Min(distance,MouthDistance(w,i,x-ix,y-iy));
            float river=1-Smooth(-.025f,.025f,distance);
            depth=river>area?w.Facts.Depth[i]:Math.Max(.15f,FlatSurfaceSampling.Sample(w.Facts.Depth,w.Width,w.Height,x,y));
            if(area>.01f)depth*=FMath.Lerp(.05f,1,Smooth(0,1.5f,sd));
            return Math.Max(area,river);
        }
    }
}
