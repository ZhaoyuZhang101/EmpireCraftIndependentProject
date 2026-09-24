using System;

namespace Orbis.Flat
{
    // Algorithmic CPU adaptation of the height/pressure-flux pipeline described by
    // AshenOneArt/Unity-SWE (MIT). No HDRP or Unity objects in this solver.
    // This solves LOCAL surface perturbations about the authoritative river/lake head:
    // the head is subtracted, Bed=-restDepth, eta=Depth+Bed. Global runoff/routing is
    // owned exclusively by FlatHydrology. It does not simulate flood inundation.
    // Differences from upstream: signed shared face fluxes, double precision,
    // donor-volume limiting, reflective dry borders, adaptive CFL step, no blur
    // of conserved height, no MacCormack velocity advection or GPU dependency.
    public sealed class FlatShallowWater
    {
        public readonly int Width,Height,Count;
        public readonly double Dx;
        public readonly double[] Bed,Depth,VelocityX,VelocityY,Foam;
        readonly bool[] wet;
        readonly double[] east,north,scale,next;
        public double InitialVolume { get; private set; }
        public double Elapsed { get; private set; }
        public int LastSubsteps { get; private set; }
        public FlatShallowWater(int width,int height,double cellMeters,float[] restDepth)
        {
            if(width<2||height<2||width>256||height>256||restDepth==null||restDepth.Length!=width*height||cellMeters<=0||double.IsInfinity(cellMeters)||double.IsNaN(cellMeters))
                throw new ArgumentException("Invalid local shallow-water grid.");
            Width=width;Height=height;Count=width*height;Dx=cellMeters;
            Bed=new double[Count];Depth=new double[Count];VelocityX=new double[Count];VelocityY=new double[Count];Foam=new double[Count];
            wet=new bool[Count];east=new double[Count];north=new double[Count];scale=new double[Count];next=new double[Count];
            for(int i=0;i<Count;i++)
            {
                if(!FMath.Finite(restDepth[i])||restDepth[i]<0||restDepth[i]>10000)throw new ArgumentException("Invalid water depth.");
                wet[i]=restDepth[i]>.0001f;Depth[i]=restDepth[i];Bed[i]=-Depth[i];
            }
            InitialVolume=Volume();
        }
        public bool IsWet(int i) { return i>=0&&i<Count&&wet[i]; }
        public double Volume() { double sum=0;for(int i=0;i<Count;i++)sum+=Depth[i];return sum*Dx*Dx; }
        public double RelativeMassError() { return Math.Abs(Volume()-InitialVolume)/Math.Max(1,InitialVolume); }
        // An exactly balanced free-surface disturbance. No water is created by this button.
        public void Ripple(int cx,int cy,double amplitude)
        {
            if(cx<0||cx>=Width||cy<0||cy>=Height||!wet[cy*Width+cx])return;
            amplitude=Math.Max(0,Math.Min(.08,amplitude));double removed=0;
            int center=cy*Width+cx;
            for(int y=Math.Max(0,cy-4);y<=Math.Min(Height-1,cy+4);y++)
            for(int x=Math.Max(0,cx-4);x<=Math.Min(Width-1,cx+4);x++)
            {
                int i=y*Width+x;if(i==center||!wet[i])continue;
                double amount=Math.Min(Depth[i]*.015,amplitude*Math.Exp(-((x-cx)*(x-cx)+(y-cy)*(y-cy))*.3)*.08);
                Depth[i]-=amount;removed+=amount;
            }
            Depth[center]+=removed;
        }
        public void Advance(double requestedSeconds,int maxSubsteps=12)
        {
            if(double.IsNaN(requestedSeconds)||double.IsInfinity(requestedSeconds)||requestedSeconds<0)throw new ArgumentOutOfRangeException(nameof(requestedSeconds));
            double remaining=Math.Min(.2,requestedSeconds);LastSubsteps=0;
            while(remaining>1e-9&&LastSubsteps<Math.Max(1,Math.Min(64,maxSubsteps)))
            {
                double maximum=0;
                for(int i=0;i<Count;i++)if(wet[i])maximum=Math.Max(maximum,Math.Sqrt(9.81*Depth[i])+Math.Abs(VelocityX[i])+Math.Abs(VelocityY[i]));
                double dt=Math.Min(remaining,Math.Min(.04,.30*Dx/Math.Max(.1,maximum)));
                Step(dt);remaining-=dt;Elapsed+=dt;LastSubsteps++;
            }
            // Drop excess presentation time rather than replaying an unbounded frame backlog.
        }
        void Step(double dt)
        {
            double damping=Math.Exp(-.18*dt);
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int i=y*Width+x;double eta=Depth[i]+Bed[i];
                if(x+1<Width&&wet[i]&&wet[i+1])
                {
                    double h=Math.Max(0,Math.Min(Depth[i],Depth[i+1]));
                    east[i]=east[i]*damping+9.81*h*(eta-Depth[i+1]-Bed[i+1])*dt/Dx;
                }
                else east[i]=0;
                if(y+1<Height&&wet[i]&&wet[i+Width])
                {
                    double h=Math.Max(0,Math.Min(Depth[i],Depth[i+Width]));
                    north[i]=north[i]*damping+9.81*h*(eta-Depth[i+Width]-Bed[i+Width])*dt/Dx;
                }
                else north[i]=0;
            }
            // Per-donor availability, then one shared limited flux on every face.
            // This prevents negative water WITHOUT independently clipping receiving cells.
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int i=y*Width+x;double outward=Math.Max(0,east[i])+Math.Max(0,north[i]);
                if(x>0)outward+=Math.Max(0,-east[i-1]);
                if(y>0)outward+=Math.Max(0,-north[i-Width]);
                scale[i]=outward>0?Math.Min(1,Depth[i]*Dx/(dt*outward)):1;
            }
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int i=y*Width+x;
                if(x+1<Width)east[i]*=east[i]>=0?scale[i]:scale[i+1];
                if(y+1<Height)north[i]*=north[i]>=0?scale[i]:scale[i+Width];
            }
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int i=y*Width+x;double west=x>0?east[i-1]:0,south=y>0?north[i-Width]:0;
                next[i]=Depth[i]+dt/Dx*(west+south-east[i]-north[i]);
                if(next[i]<-1e-9||double.IsNaN(next[i])||double.IsInfinity(next[i]))throw new InvalidOperationException("Shallow water left its numerical range.");
                next[i]=Math.Max(0,next[i]); // roundoff only; volume error is reported.
                double inverse=wet[i]?1/Math.Max(.02,next[i]):0;
                VelocityX[i]=(east[i]+west)*.5*inverse;VelocityY[i]=(north[i]+south)*.5*inverse;
                double convergence=Math.Abs(west+south-east[i]-north[i])/Dx;
                Foam[i]=Math.Min(1,Foam[i]*Math.Exp(-dt*1.2)+convergence*dt*.18);
            }
            Array.Copy(next,Depth,Count);
        }
    }
}
