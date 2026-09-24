using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orbis
{
    /// <summary>Standalone, reduced-order moist atmosphere. No Unity dependency.
    /// Water is kg/m2 in each layer, not an unweighted mixing ratio. Area is steradians.
    /// Rates use physical seconds (45 per game weather second). Horizontal speeds are
    /// deliberately game-scaled radians/weather second. Not a weather forecast model.</summary>
    public sealed class OrbisAtmosphereCore
    {
        public const int Layers=12, Species=6;
        public const double PhysicalSecondsPerSecond=45;
        public static readonly double[] Edges={0,300,800,1500,2000,3000,4000,5000,6000,8000,10000,12000,16000};
        public static readonly double[] Height=new double[Layers], AirMass=new double[Layers], Pressure=new double[Layers];
        public readonly int Count;
        public readonly double[] Area;
        public readonly AtmosVector[] Position;
        public readonly AtmosEdge[] Links;
        public readonly double[][] Water=new double[Species][]; // qv,qc,qi,qr,qs,qg: layer water paths
        public readonly double[] Temperature,VerticalVelocity;
        public readonly AtmosVector[] Wind;
        public readonly double[] SurfaceWater,Film,Wetness,RainRate,SnowRate,ColdPool,PressureAnomaly;
        public readonly double[] CAPE,CIN,LCL,CloudBase,CloudTop,Coverage,Charge,Updraft,StormAge;
        public readonly double[] SynopticAscent; // Diagnosed every tick, signed m/s; independent of CAPE.
        public readonly int[] Stage; // 0 clear,1 shallow,2 developing,3 mature,4 dissipating
        public readonly Queue<AtmosphereLightning> Lightning=new Queue<AtmosphereLightning>();
        public double Time {get;private set;}
        public double ExternalWater {get;private set;}
        public double RunoffWater {get;private set;}
        public double InitialWater {get;private set;}
        public long LightningCount {get;private set;}
        public bool TransportEnabled=true, MicrophysicsEnabled=true, SurfaceExchangeEnabled=true;
        readonly double[] surfaceT,solar,soil,boost,terrain,pressureForcing,liftForcing,cooldown,gustLift,orographicLift;
        readonly bool[] sea;
        readonly AtmosVector[] baseWind,pressureGradient,eastDirection,thermalGradient;
        readonly double[] jetStrength,hemisphere;
        readonly int[] edgeDonor,edgeReceiver;
        readonly double[] edgeDonorWeight,edgeReceiverWeight;
        readonly double[] flux,outgoing,limit,delta,temporary,convergence,pressureNext;
        readonly double[] externalStep,runoffStep,lightningRate,mixedIceScratch,graupelScratch,collisionScratch;
        readonly ParallelOptions parallelOptions=new ParallelOptions();
        public bool ParallelColumnPhysics=true;
        public int MaxParallelism { get { return parallelOptions.MaxDegreeOfParallelism; } }
        uint random;
        AtmosVector[] initialDisturbances;
        double[] initialHumidity;
        // Optional boundary data for the flat world's seasonal climate adapter.
        // Applied once, before Initialize; the prognostic water then evolves freely.
        public void SetInitialClimate(AtmosVector[] disturbances,double[] humidity)
        {
            if(humidity==null||humidity.Length!=Count)throw new ArgumentException("Initial humidity dimensions");
            foreach(double h in humidity)if(!Finite(h)||h<0||h>1)throw new ArgumentException("Initial humidity range");
            initialHumidity=(double[])humidity.Clone();initialDisturbances=(AtmosVector[])(disturbances??new AtmosVector[0]).Clone();
        }
        static OrbisAtmosphereCore()
        {
            for(int k=0;k<Layers;k++)
            {
                Height[k]=(Edges[k]+Edges[k+1])*.5;
                Pressure[k]=101325*Math.Exp(-Height[k]/8000);
                AirMass[k]=101325/9.81*(Math.Exp(-Edges[k]/8000)-Math.Exp(-Edges[k+1]/8000));
            }
        }
        public OrbisAtmosphereCore(AtmosVector[] positions,double[] areas,AtmosEdge[] links,uint seed)
        {
            if(positions==null||areas==null||links==null||positions.Length!=areas.Length||positions.Length<4)
                throw new ArgumentException("Invalid atmosphere topology.");
            Count=positions.Length;Position=positions;Area=areas;Links=links;random=seed==0?1:seed;
            int n=Count*Layers;
            for(int s=0;s<Species;s++)Water[s]=new double[n];
            Temperature=new double[n];VerticalVelocity=new double[n];Wind=new AtmosVector[n];
            SurfaceWater=new double[Count];Film=new double[Count];Wetness=new double[Count];RainRate=new double[Count];SnowRate=new double[Count];
            ColdPool=new double[Count];PressureAnomaly=new double[Count];CAPE=new double[Count];CIN=new double[Count];LCL=new double[Count];
            CloudBase=new double[Count];CloudTop=new double[Count];Coverage=new double[Count];Charge=new double[Count];Updraft=new double[Count];Stage=new int[Count];StormAge=new double[Count];
            surfaceT=new double[Count];solar=new double[Count];soil=new double[Count];boost=new double[Count];terrain=new double[Count];sea=new bool[Count];baseWind=new AtmosVector[Count];
            pressureForcing=new double[Count];liftForcing=new double[Count];cooldown=new double[Count];gustLift=new double[Count];orographicLift=new double[Count];
            flux=new double[links.Length];outgoing=new double[Count];limit=new double[Count];delta=new double[n];temporary=new double[n];
            convergence=new double[Count];pressureNext=new double[Count];
            externalStep=new double[Count];runoffStep=new double[Count];
            lightningRate=new double[Count];mixedIceScratch=new double[Count];graupelScratch=new double[Count];collisionScratch=new double[Count];
            parallelOptions.MaxDegreeOfParallelism=Math.Max(1,Environment.ProcessorCount-2); // leave CPU headroom for civilization/gameplay
            pressureGradient=new AtmosVector[Count];eastDirection=new AtmosVector[Count];jetStrength=new double[Count];
            thermalGradient=new AtmosVector[Count];hemisphere=new double[Count];SynopticAscent=new double[Count];
            edgeDonor=new int[links.Length];edgeReceiver=new int[links.Length];edgeDonorWeight=new double[links.Length];edgeReceiverWeight=new double[links.Length];
            for(int i=0;i<Count;i++)
            {
                AtmosVector p=Position[i];eastDirection[i]=new AtmosVector(p.Z,0,-p.X).Normalized;
                jetStrength[i]=Math.Exp(-Math.Pow((Math.Abs(p.Y)-.60)/.22,2));hemisphere[i]=Math.Tanh(p.Y*5);
            }
            for(int i=0;i<Count;i++)if(!(Area[i]>0))throw new ArgumentException("Cell area must be positive.");
        }
        public static double Clamp(double x,double lo,double hi){return Math.Max(lo,Math.Min(hi,x));}
        static double Relax(double dt,double tau){return 1-Math.Exp(-dt/tau);}
        static double Lerp(double a,double b,double t){return a+(b-a)*t;}
        double Rand(){random^=random<<13;random^=random>>17;random^=random<<5;return random/(double)uint.MaxValue;}
        public static double Saturation(double t,double p)
        {
            double c=Clamp(t-273.15,-85,55);
            double e=611.2*Math.Exp(17.67*c/(c+243.5));
            return Clamp(.622*e/Math.Max(1000,p-e),.000001,.09);
        }
        public void SetForcing(int i,double kelvin,double sunlight,double soilMoisture,bool ocean,
            AtmosVector tangentWind,double pressureHPa,double terrainMeters,double synopticLift,double waterBrush)
        {
            surfaceT[i]=Clamp(kelvin,195,330);solar[i]=Clamp(sunlight,0,1);soil[i]=Clamp(soilMoisture,0,1);sea[i]=ocean;
            baseWind[i]=tangentWind.Tangent(Position[i]);pressureForcing[i]=Clamp(pressureHPa,960,1050);terrain[i]=Math.Max(0,terrainMeters);
            liftForcing[i]=Clamp(synopticLift,0,2);boost[i]=Clamp(waterBrush,0,3);
        }
        public void Initialize()
        {
            // Correlated INITIAL conditions only. No permanent geographic cloud/noise mask.
            double phase=Rand()*6.28;
            // Six INITIAL low-pressure perturbations; no latitude cloud mask and no
            // continuously respawned cyclone texture. Subsequent evolution is prognostic.
            var centers=initialDisturbances??new AtmosVector[6];var radii=new double[centers.Length];
            for(int v=0;v<centers.Length;v++)
            {
                if(initialDisturbances==null){double y=(v%2==0?1:-1)*(.25+Rand()*.45),lon=Rand()*Math.PI*2;
                double c=Math.Sqrt(1-y*y);centers[v]=new AtmosVector(Math.Sin(lon)*c,y,Math.Cos(lon)*c);}
                radii[v]=.13+Rand()*.10;
            }
            for(int i=0;i<Count;i++)
            {
                AtmosVector p=Position[i];double pattern=Math.Sin(p.X*9+p.Z*5+phase)*Math.Cos(p.Y*7-p.X*4-phase);
                SurfaceWater[i]=sea[i]?1200:25+soil[i]*95;
                double low=0;AtmosVector vortex=new AtmosVector();
                for(int v=0;v<centers.Length;v++)
                {
                    double d2=Math.Max(0,2-2*AtmosVector.Dot(p,centers[v]));
                    double envelope=Math.Exp(-d2/(2*radii[v]*radii[v]));
                    low+=envelope;
                    vortex+=AtmosVector.Cross(centers[v],p)*(Math.Sign(centers[v].Y)*.024*envelope/radii[v]);
                }
                PressureAnomaly[i]=Clamp(pattern*4-low*14,-22,12);
                cooldown[i]=15+Rand()*40;
                for(int k=0;k<Layers;k++)
                {
                    int j=k*Count+i;double z=Height[k];
                    Temperature[j]=Clamp(surfaceT[i]-.0069*z+(k>8?(z-9000)*.002:0)+pattern*.65,185,330);
                    double verticalPattern=Math.Sin(k*.55+p.X*4-p.Z*6+phase);
                    double baseRH=initialHumidity==null?.72:initialHumidity[i];
                    double rh=Clamp(baseRH-.020*k+pattern*.31*baseRH+verticalPattern*.08*baseRH+Math.Min(1,low)*.33,0,1.13);
                    Water[0][j]=Saturation(Temperature[j],Pressure[k])*AirMass[k]*rh;
                    Wind[j]=(baseWind[i]*(.014*(.65+k*.105))+vortex*(1-k*.035)).Tangent(p).Limited(.065);
                }
            }
            double pressureMean=0,totalArea=0;
            for(int i=0;i<Count;i++){pressureMean+=PressureAnomaly[i]*Area[i];totalArea+=Area[i];}
            pressureMean/=totalArea;
            for(int i=0;i<Count;i++)PressureAnomaly[i]-=pressureMean;
            InitialWater=TotalWater();
            for(int i=0;i<Count;i++){SaturationAdjustment(i,1);Diagnose(i,0);ProcessLightning(i,0);}
        }
        public void SetParallelism(int maxWorkers)
        {
            // 0 means auto. Reserve two logical cores by default so civilization AI, rendering
            // preparation and input are not starved by the secondary climate simulation.
            parallelOptions.MaxDegreeOfParallelism=maxWorkers<=0?Math.Max(1,Environment.ProcessorCount-2):Math.Max(1,maxWorkers);
        }
        void ForEachCell(Action<int> body)
        {
            if(ParallelColumnPhysics&&parallelOptions.MaxDegreeOfParallelism>1&&Count>=256)
                Parallel.For(0,Count,parallelOptions,i=>body(i));
            else for(int i=0;i<Count;i++)body(i);
        }
        public void Tick(double dt)
        {
            // Single-threaded reference path retained for deterministic validation and low-core systems.
            if(!Finite(dt)||dt<=0||dt>.100001)throw new ArgumentOutOfRangeException("dt","Use fixed steps <= 0.1 weather seconds.");
            UpdateDynamics(dt);
            if(TransportEnabled)HorizontalTransport(dt);
            double seconds=dt*PhysicalSecondsPerSecond;
            if(MicrophysicsEnabled)
            {
                Array.Clear(externalStep,0,Count);Array.Clear(runoffStep,0,Count);
                for(int i=0;i<Count;i++)
                {
                    Parcel(i,dt);VerticalExchange(i,seconds);
                    if(SurfaceExchangeEnabled)SurfaceExchange(i,seconds);
                    SaturationAdjustment(i,seconds);ConvertHydrometeors(i,seconds);
                }
                if(SurfaceExchangeEnabled)for(int i=0;i<Count;i++){ExternalWater+=externalStep[i];RunoffWater+=runoffStep[i];}
                Sedimentation(seconds,dt);
            }
            for(int i=0;i<Count;i++){Diagnose(i,dt);ProcessLightning(i,dt);}
            Time+=dt;
        }
        public void TickParallel(double dt)
        {
            if(!ParallelColumnPhysics||parallelOptions.MaxDegreeOfParallelism<=1||Count<256){Tick(dt);return;}
            if(!Finite(dt)||dt<=0||dt>.100001)throw new ArgumentOutOfRangeException("dt","Use fixed steps <= 0.1 weather seconds.");
            UpdateDynamics(dt);
            if(TransportEnabled)HorizontalTransport(dt);
            double seconds=dt*PhysicalSecondsPerSecond;
            if(MicrophysicsEnabled)
            {
                Array.Clear(externalStep,0,Count);Array.Clear(runoffStep,0,Count);
                ForEachCell(i=>
                {
                    Parcel(i,dt);VerticalExchange(i,seconds);
                    if(SurfaceExchangeEnabled)SurfaceExchange(i,seconds);
                    SaturationAdjustment(i,seconds);ConvertHydrometeors(i,seconds);
                });
                if(SurfaceExchangeEnabled)for(int i=0;i<Count;i++){ExternalWater+=externalStep[i];RunoffWater+=runoffStep[i];}
                Sedimentation(seconds,dt);
            }
            ForEachCell(i=>Diagnose(i,dt));
            // RNG and lightning event queue stay serial so saves remain deterministic.
            for(int i=0;i<Count;i++)ProcessLightning(i,dt);
            Time+=dt;
        }
        void UpdateDynamics(double dt)
        {
            Array.Clear(convergence,0,Count);Array.Clear(orographicLift,0,Count);
            Array.Clear(thermalGradient,0,Count);
            double gustDecay=Math.Exp(-dt/12);
            for(int i=0;i<Count;i++)gustLift[i]*=gustDecay;
            for(int e=0;e<Links.Length;e++)
            {
                AtmosEdge link=Links[e];int a=link.A,b=link.B;
                double f=AtmosVector.Dot((Wind[a]+Wind[b])*.5,link.Direction)*link.Length;
                flux[e]=f*dt;
                convergence[a]-=f/Area[a];convergence[b]+=f/Area[b];
                double slope=(terrain[b]-terrain[a])/Math.Max(.01,link.Distance);
                double upslope=AtmosVector.Dot((Wind[a]+Wind[b])*.5,link.Direction)*slope/45;
                if(upslope>0)orographicLift[b]+=Math.Min(2,upslope);else orographicLift[a]+=Math.Min(2,-upslope);
                // A moving temperature gradient, NOT a latitude-specific cloud quota.
                double gradient=(Temperature[2*Count+b]-Temperature[2*Count+a])/Math.Max(.01,link.Distance);
                AtmosVector g=link.Direction*(gradient*.5);
                thermalGradient[a]+=g;thermalGradient[b]+=g;
            }
            for(int i=0;i<Count;i++)
            {
                double forcing=-(Temperature[i]-surfaceT[i])*.016+ColdPool[i]*.12;
                // Follow the geography-dependent seasonal sea-level pressure
                // slowly; prognostic storms and gust fronts can still depart from it.
                double target=pressureForcing[i]-1013;
                pressureNext[i]=Clamp(PressureAnomaly[i]+dt*(convergence[i]*12+forcing+(target-PressureAnomaly[i])*.065-PressureAnomaly[i]/100),-22,22);
            }
            for(int e=0;e<Links.Length;e++)
            {
                AtmosEdge link=Links[e];int a=link.A,b=link.B;
                // Diffuse pressure with equal area-integrated transfers on nonuniform cells.
                double mix=(PressureAnomaly[b]-PressureAnomaly[a])*dt*.03*Math.Min(Area[a],Area[b]);
                pressureNext[a]+=mix/Area[a];pressureNext[b]-=mix/Area[b];
                int donor=flux[e]>=0?a:b,receiver=flux[e]>=0?b:a;
                double factor=Math.Min(.12,Math.Abs(flux[e])/Area[receiver]);
                pressureNext[receiver]+=(PressureAnomaly[donor]-PressureAnomaly[receiver])*factor;
            }
            double mean=0,area=0;
            for(int i=0;i<Count;i++){mean+=pressureNext[i]*Area[i];area+=Area[i];}
            mean/=area;
            for(int i=0;i<Count;i++)PressureAnomaly[i]=Clamp(pressureNext[i]-mean,-22,22);
            Array.Clear(pressureGradient,0,Count);
            for(int e=0;e<Links.Length;e++)
            {
                AtmosEdge link=Links[e];
                double gradient=(PressureAnomaly[link.B]-PressureAnomaly[link.A])/Math.Max(.01,link.Distance);
                AtmosVector g=link.Direction*(gradient*.5);
                pressureGradient[link.A]+=g;pressureGradient[link.B]+=g;
            }
            double windRelax=Relax(dt,28);
            for(int i=0;i<Count;i++)
            {
                AtmosVector p=Position[i],gradient=pressureGradient[i].Tangent(p);
                thermalGradient[i]=thermalGradient[i].Tangent(p);
                AtmosVector balanced=AtmosVector.Cross(p,gradient)*(.00011*hemisphere[i]);
                AtmosVector thermalShear=AtmosVector.Cross(p,thermalGradient[i])*(.000028*hemisphere[i]);
                AtmosVector target0=baseWind[i]*(.014*.65)+balanced;
                AtmosVector shear=baseWind[i]*(.014*.105)+(eastDirection[i]*(jetStrength[i]*.009)+thermalShear)/11;
                // Signed warm advection / convergence drives broad lifting even with CAPE=0.
                double warmLift=Clamp(-AtmosVector.Dot(Wind[2*Count+i],thermalGradient[i])*.22,-.35,.45);
                SynopticAscent[i]=Clamp(convergence[i]*60+warmLift+orographicLift[i]*.20,-.50,.80);
                for(int k=0;k<Layers;k++)
                {
                    int j=k*Count+i;AtmosVector target=target0+shear*k;
                    Wind[j]=AtmosVector.Lerp(Wind[j],target,windRelax);
                    Wind[j]=(Wind[j]-gradient*(.000032*dt)-AtmosVector.Cross(p,Wind[j])*(dt*.09*p.Y)).Tangent(p).Limited(.065);
                }
            }
        }
        public void HorizontalTransport(double dt)
        {
            for(int k=0;k<Layers;k++)
            {
                int offset=k*Count;Array.Clear(outgoing,0,Count);
                for(int e=0;e<Links.Length;e++)
                {
                    AtmosEdge link=Links[e];double f=AtmosVector.Dot((Wind[offset+link.A]+Wind[offset+link.B])*.5,link.Direction)*link.Length*dt;
                    flux[e]=f;outgoing[f>=0?link.A:link.B]+=Math.Abs(f);
                }
                for(int i=0;i<Count;i++)limit[i]=Math.Min(1,.65*Area[i]/Math.Max(1e-20,outgoing[i]));
                for(int e=0;e<Links.Length;e++)
                {
                    AtmosEdge link=Links[e];int donor=flux[e]>=0?link.A:link.B,receiver=flux[e]>=0?link.B:link.A;
                    double transported=Math.Abs(flux[e])*limit[donor];edgeDonor[e]=donor;edgeReceiver[e]=receiver;
                    edgeDonorWeight[e]=transported/Area[donor];edgeReceiverWeight[e]=transported/Area[receiver];
                }
                for(int s=0;s<Species;s++)
                {
                    double[] q=Water[s];Array.Clear(delta,offset,Count);
                    for(int e=0;e<Links.Length;e++)
                    {
                        int donor=edgeDonor[e],receiver=edgeReceiver[e];double source=q[offset+donor];
                        delta[offset+donor]-=source*edgeDonorWeight[e];delta[offset+receiver]+=source*edgeReceiverWeight[e];
                    }
                    for(int i=0;i<Count;i++)q[offset+i]=Math.Max(0,q[offset+i]+delta[offset+i]);
                }
                // Temperature is an intensive scalar: advect with bounded upstream mixing.
                Array.Clear(delta,offset,Count);
                for(int e=0;e<Links.Length;e++)
                {
                    int donor=edgeDonor[e],receiver=edgeReceiver[e];
                    double factor=Math.Min(.2,edgeReceiverWeight[e]);
                    delta[offset+receiver]+=(Temperature[offset+donor]-Temperature[offset+receiver])*factor;
                }
                for(int i=0;i<Count;i++)Temperature[offset+i]=Clamp(Temperature[offset+i]+delta[offset+i],180,345);
            }
        }
        void Parcel(int i,double dt)
        {
            double t0=Temperature[i]+solar[i]*(sea[i]?.8:2.1)-ColdPool[i]*.18;
            double q0=Water[0][i]/AirMass[0];double e=q0*Pressure[0]/(.622+q0);
            double ln=Math.Log(Math.Max(1,e)/611.2),dew=243.5*ln/(17.67-ln)+273.15;
            LCL[i]=Clamp(125*(t0-dew),150,14000);
            double parcel=t0,previous=0,cape=0,cin=0;
            for(int k=0;k<Layers;k++)
            {
                double z=Height[k],dz=z-previous;
                double sat=Saturation(parcel,Pressure[k]);
                double moist=9.81*(1+2500000*sat/(287*parcel))/(1004+2500000.0*2500000.0*sat*.622/(287*parcel*parcel));
                double below=Math.Max(0,Math.Min(z,LCL[i])-previous);
                parcel-=.0098*below+Clamp(moist,.003,.0098)*(dz-below);
                int j=k*Count+i;double environment=Temperature[j]*(1+.61*Water[0][j]/AirMass[k]);
                double buoyancy=9.81*(parcel*(1+.61*Math.Min(q0,sat))-environment)/environment;
                if(buoyancy>0&&z>=LCL[i])cape+=buoyancy*dz;else if(z<Math.Max(3000,LCL[i]))cin-=Math.Min(0,buoyancy)*dz;
                previous=z;
            }
            CAPE[i]=Clamp(cape,0,6500);CIN[i]=Clamp(cin,0,1500);
            double rh=q0/Saturation(Temperature[i],Pressure[0]);
            double thermalLift=Math.Max(0,Temperature[i]-surfaceT[i])*.10*solar[i];
            double trigger=Clamp(Math.Max(0,convergence[i]-.002)*18+thermalLift+(liftForcing[i]+gustLift[i]+orographicLift[i])*.35,0,1);
            // Neighbor gradients are read from OLD cold pools; no random storm spawner.
            double available=Clamp((rh-.48)/.4,0,1)*Math.Exp(-CIN[i]/180);
            double target=Math.Sqrt(2*CAPE[i])*.24*trigger*available;
            target=Clamp(target-ColdPool[i]*.45,0,32);
            Updraft[i]=Lerp(Updraft[i],target,Relax(dt,target>Updraft[i]?3:8));
            double temperatureRelax=Relax(dt,sea[i]?350:210);
            // Snapshot the vertical profile: no upward loop-order feedback in cooling.
            for(int k=0;k<Layers;k++)temporary[k*Count+i]=Temperature[k*Count+i];
            for(int k=0;k<Layers;k++)
            {
                int j=k*Count+i;double z=Height[k];
                double plume=Updraft[i]*Math.Sin(Math.PI*Clamp(z/16000,0,1));
                double profile=Math.Sin(Math.PI*Clamp((z+900)/16900,0,1));
                double synoptic=SynopticAscent[i]*profile;
                double lift=synoptic+(liftForcing[i]+gustLift[i]+orographicLift[i])*.5;
                VerticalVelocity[j]=Clamp(plume+lift-ColdPool[i]*.18*(1-z/16000),-6,32);
                int lower=Math.Max(0,k-1),upper=Math.Min(Layers-1,k+1);
                double lapse=(temporary[upper*Count+i]-temporary[lower*Count+i])/(Height[upper]-Height[lower]);
                // Eulerian dry thermodynamic tendency: -w*(dT/dz + g/cp).
                // Condensation/latent heating is handled by SaturationAdjustment below;
                // do not inject qc or multiply a latitude cloud mask here.
                double cooling=synoptic*Clamp(.0098+lapse,0,.015)*dt*PhysicalSecondsPerSecond;
                double targetT=Clamp(surfaceT[i]-.0069*z+(k>8?(z-9000)*.002:0),185,335);
                Temperature[j]=Clamp(Lerp(Temperature[j],targetT,temperatureRelax)-Clamp(cooling,-.8,.8),185,335);
            }
            Temperature[i]+=dt*(solar[i]*(sea[i]?.015:.060)*(1-Coverage[i]*.65)-ColdPool[i]*.009);
        }
        void VerticalExchange(int i,double seconds)
        {
            // Pairwise exchange of equal dry-air mass. Both donor AND receiving reservoirs
            // are updated; unlike copying qc upwards this does not manufacture cloud water.
            for(int k=0;k<Layers-1;k++)
            {
                int a=k*Count+i,b=(k+1)*Count+i;double dz=Height[k+1]-Height[k];
                double fraction=Clamp((Math.Abs(VerticalVelocity[a])+.05)*seconds/dz,0,.22);
                double dry=Math.Min(AirMass[k],AirMass[k+1])*fraction;
                for(int s=0;s<Species;s++)
                {
                    double exchange=dry*(Water[s][a]/AirMass[k]-Water[s][b]/AirMass[k+1]);
                    Water[s][a]-=exchange;Water[s][b]+=exchange;
                }
                double heat=(Temperature[a]-.0098*dz-Temperature[b])*dry;
                Temperature[a]-=heat/AirMass[k];Temperature[b]+=heat/AirMass[k+1];
            }
        }
        void SurfaceExchange(int i,double seconds)
        {
            double deficit=Math.Max(0,Saturation(surfaceT[i],Pressure[0])-Water[0][i]/AirMass[0]);
            // Surface reservoir is finite, including the ocean reservoir. Brush water is a
            // reported external source; rainfall is never a source disconnected from cloud water.
            double added=boost[i]*.00035*seconds;SurfaceWater[i]+=added;externalStep[i]=added*Area[i];
            // Bulk aerodynamic exchange: wind/gustiness and humidity deficit, not sun-only gating.
            // Winds are game-scaled; this documented mapping is not a forecast calibration.
            double windMeters=Clamp(Wind[i].Length/.014*10,0,35);
            double speed=Math.Sqrt(windMeters*windMeters+9);
            double availability=sea[i]?1:Clamp(SurfaceWater[i]/40,0,1);
            double transfer=1.2*(sea[i]?.0018:.0011)*speed;
            double evap=Math.Min(SurfaceWater[i],deficit*transfer*seconds*availability);
            SurfaceWater[i]-=evap;Water[0][i]+=evap;Film[i]=Math.Max(0,Film[i]-evap);
            Temperature[i]-=2490*evap/AirMass[0];
            if(SurfaceWater[i]>180&&!sea[i])
            {
                double runoff=(SurfaceWater[i]-180)*Relax(seconds,16000);SurfaceWater[i]-=runoff;runoffStep[i]=runoff*Area[i];
            }
            // Water infiltrated from film remains in SurfaceWater; it is not destroyed.
            Film[i]*=Math.Exp(-seconds/3600);Film[i]=Math.Min(Film[i],SurfaceWater[i]);
            Wetness[i]=1-Math.Exp(-Film[i]/.35);
        }
        static double SaturationSlope(double t,double p,double q)
        {
            double c=Clamp(t-273.15,-85,55);
            if(t-273.15<=-85||t-273.15>=55||q<=.000001||q>=.09)return 0;
            double e=q*p/(.622+q),denom=p-e;
            double derivativeLog=17.67*243.5/((c+243.5)*(c+243.5));
            return denom>1000?q*derivativeLog*p/denom:q*derivativeLog;
        }
        void SaturationAdjustment(int i,double seconds)
        {
            double relaxation=Relax(seconds,10);
            for(int k=0;k<Layers;k++)
            {
                int j=k*Count+i;double mass=AirMass[k];
                for(int iteration=0;iteration<3;iteration++)
                {
                    double sat=Saturation(Temperature[j],Pressure[k]);
                    double derivative=SaturationSlope(Temperature[j],Pressure[k],sat);
                    double amount=(Water[0][j]-sat*mass)/(1+2490*derivative)*relaxation;
                    if(amount>=0){amount=Math.Min(amount,Water[0][j]);Water[0][j]-=amount;Water[1][j]+=amount;Temperature[j]+=2490*amount/mass;}
                    else
                    {
                        double liquid=Math.Min(-amount,Water[1][j]);Water[1][j]-=liquid;Water[0][j]+=liquid;Temperature[j]-=2490*liquid/mass;
                        double ice=Math.Min(Math.Max(0,-amount-liquid),Water[2][j]);Water[2][j]-=ice;Water[0][j]+=ice;Temperature[j]-=2830*ice/mass;
                    }
                }
            }
        }
        void ConvertHydrometeors(int i,double seconds)
        {
            double cooling=0;
            for(int k=0;k<Layers;k++)
            {
                int j=k*Count+i;double mass=AirMass[k],t=Temperature[j];
                double freeze=Water[1][j]*Clamp((268.15-t)/23,0,1)*Relax(seconds,800);
                Water[1][j]-=freeze;Water[2][j]+=freeze;Temperature[j]+=333*freeze/mass;
                double rain=Math.Max(0,Water[1][j]-.00035*mass)*Relax(seconds,750);
                double accrete=Math.Min(Water[1][j]-rain,Water[1][j]*Water[3][j]/mass*seconds*1.6);
                Water[1][j]-=rain+accrete;Water[3][j]+=rain+accrete;
                double snow=Math.Max(0,Water[2][j]-.000025*mass)*Relax(seconds,1400);
                Water[2][j]-=snow;Water[4][j]+=snow;
                double mixed=Clamp((273.15-t)/10,0,1)*Clamp((t-235)/15,0,1);
                double riming=Math.Min(Water[1][j],(Water[2][j]+Water[4][j])*.004*seconds*Clamp(Updraft[i]/6,0,2)*mixed);
                Water[1][j]-=riming;Water[5][j]+=riming;Temperature[j]+=333*riming/mass;
                if(t>273.15)
                {
                    for(int s=2;s<Species;s++)if(s!=3)
                    {
                        double melt=Water[s][j]*Relax(seconds,Math.Max(40,700/(t-272.15)));
                        Water[s][j]-=melt;Water[s==2?1:3][j]+=melt;Temperature[j]-=333*melt/mass;
                    }
                }
                double deficit=Math.Max(0,Saturation(Temperature[j],Pressure[k])*mass-Water[0][j]);
                double evaporated=Math.Min(Water[3][j]*Relax(seconds,240),deficit*.18);
                Water[3][j]-=evaporated;Water[0][j]+=evaporated;Temperature[j]-=2490*evaporated/mass;
                if(k<4)cooling+=2490*evaporated/mass;
            }
            ColdPool[i]=Clamp(ColdPool[i]*Math.Exp(-seconds/2700)+cooling*.30,0,12);
        }
        void Sedimentation(double seconds,double dt)
        {
            Array.Clear(RainRate,0,Count);Array.Clear(SnowRate,0,Count);
            for(int s=3;s<Species;s++)
            {
                Array.Clear(delta,0,delta.Length);double speed=s==3?6:s==4?1.0:4.5;
                for(int k=0;k<Layers;k++)for(int i=0;i<Count;i++)
                {
                    int j=k*Count+i;double fall=Water[s][j]*Clamp(speed*seconds/(Edges[k+1]-Edges[k]),0,.7);
                    delta[j]-=fall;
                    if(k>0)delta[j-Count]+=fall;
                    else
                    {
                        SurfaceWater[i]+=fall;
                        bool liquid=s==3||Temperature[i]>274;
                        if(liquid){RainRate[i]+=fall/seconds*3600;Film[i]=Math.Min(4,Film[i]+fall);}
                        else SnowRate[i]+=fall/seconds*3600;
                    }
                }
                for(int j=0;j<delta.Length;j++)Water[s][j]=Math.Max(0,Water[s][j]+delta[j]);
            }
            // Cold-pool edges lift neighbouring warm air; temperature anomalies diffuse.
            Array.Clear(temporary,0,Count);
            for(int e=0;e<Links.Length;e++)
            {
                AtmosEdge link=Links[e];double spread=(ColdPool[link.A]-ColdPool[link.B])*dt*.13;
                temporary[link.A]-=spread;temporary[link.B]+=spread;
                if(spread>0)gustLift[link.B]=Math.Max(gustLift[link.B],Math.Min(1,spread/dt*.8));
                else gustLift[link.A]=Math.Max(gustLift[link.A],Math.Min(1,-spread/dt*.8));
            }
            for(int i=0;i<Count;i++){ColdPool[i]=Clamp(ColdPool[i]+temporary[i],0,12);Wetness[i]=1-Math.Exp(-Film[i]/.35);}
        }
        void Diagnose(int i,double dt)
        {
            double path=0,mixedIce=0,graupel=0,collision=0;CloudBase[i]=16000;CloudTop[i]=0;
            for(int k=0;k<Layers;k++)
            {
                int j=k*Count+i;double cloud=Water[1][j]+Water[2][j];path+=cloud;
                if(cloud/(Edges[k+1]-Edges[k])>.000015)
                {CloudBase[i]=Math.Min(CloudBase[i],Edges[k]);CloudTop[i]=Edges[k+1];}
                if(Temperature[j]>238&&Temperature[j]<268)
                {
                    mixedIce+=Water[2][j];graupel+=Water[5][j];
                    collision+=Math.Min(Water[1][j],Water[2][j])*Water[5][j];
                }
            }
            Coverage[i]=1-Math.Exp(-path*1.8);
            int previous=Stage[i];
            bool deep=CloudTop[i]>=6000&&path>.15;
            Stage[i]=path<.018?0:deep&&RainRate[i]>.25&&Updraft[i]>.65?3:Updraft[i]>1.3?2:RainRate[i]>.08&&Updraft[i]<.8?4:1;
            StormAge[i]=previous==Stage[i]?StormAge[i]+dt:0;
            cooldown[i]=Math.Max(0,cooldown[i]-dt);
            // Charge is a gameplay mixed-phase proxy, not an electrification solver.
            double rate=Clamp(collision*Math.Max(0,Updraft[i]-2)*.6,0,.075);
            Charge[i]=Math.Max(0,Charge[i]*Math.Exp(-dt/100)+rate*dt);
            lightningRate[i]=rate;mixedIceScratch[i]=mixedIce;graupelScratch[i]=graupel;collisionScratch[i]=collision;
        }
        void ProcessLightning(int i,double dt)
        {
            if(dt<=0)return;
            double hazard=(.006+.006*Clamp(CAPE[i]/2500,0,1))*Clamp(Area[i]/.0035,.2,2);
            if(Stage[i]==3&&StormAge[i]>3&&CAPE[i]>600&&Updraft[i]>3&&RainRate[i]>.5
                &&Charge[i]>=1&&cooldown[i]<=0&&mixedIceScratch[i]>.018&&graupelScratch[i]>.005&&collisionScratch[i]>.00005
                &&Rand()<1-Math.Exp(-hazard*dt))
            {
                Charge[i]*=.18;cooldown[i]=22+Rand()*43;LightningCount++;
                var bolt=new AtmosphereLightning{Cell=i,Height=Math.Min(CloudTop[i]*.72,10500),Strength=Clamp(.6+lightningRate[i]*2,.6,1.8),CloudToGround=Rand()<.38,Sequence=LightningCount,Time=Time};
                // Drop oldest unconsumed visual events; physics cannot allocate unbounded queues.
                while(Lightning.Count>=64)Lightning.Dequeue();Lightning.Enqueue(bolt);
            }
        }
        /// <summary>Explicit user experiment: perturb heat and vapour, never rain/lightning.
        /// Draw moisture from the local surface reservoir so the water budget still closes.</summary>
        public void AddWarmMoistBubble(int i)
        {
            if(i<0||i>=Count)throw new ArgumentOutOfRangeException("i");
            for(int k=0;k<Layers;k++)
            {
                int j=k*Count+i;
                Temperature[j]=Clamp(Temperature[j]+(k<2?5:k<9?-3:0),180,335);
                if(k<3)
                {
                    double need=Math.Max(0,Saturation(Temperature[j],Pressure[k])*AirMass[k]*1.06-Water[0][j]);
                    double transfer=Math.Min(SurfaceWater[i],need);SurfaceWater[i]-=transfer;Water[0][j]+=transfer;
                }
            }
            gustLift[i]=1.4;
        }
        public double TotalWater()
        {
            double sum=0;for(int i=0;i<Count;i++)sum+=SurfaceWater[i]*Area[i];
            for(int s=0;s<Species;s++)for(int k=0;k<Layers;k++)for(int i=0;i<Count;i++)sum+=Water[s][k*Count+i]*Area[i];
            return sum;
        }
        public double WaterBudgetError(){return TotalWater()+RunoffWater-InitialWater-ExternalWater;}
        public static bool Finite(double v){return !double.IsNaN(v)&&!double.IsInfinity(v);}
        public OrbisAtmosphereState Save()
        {
            return new OrbisAtmosphereState{version=1,count=Count,time=Time,random=random,initial=InitialWater,external=ExternalWater,runoff=RunoffWater,lightningCount=LightningCount,
                qv=(double[])Water[0].Clone(),qc=(double[])Water[1].Clone(),qi=(double[])Water[2].Clone(),qr=(double[])Water[3].Clone(),qs=(double[])Water[4].Clone(),qg=(double[])Water[5].Clone(),
                temperature=(double[])Temperature.Clone(),wind=(AtmosVector[])Wind.Clone(),surface=(double[])SurfaceWater.Clone(),film=(double[])Film.Clone(),coldPool=(double[])ColdPool.Clone(),
                pressure=(double[])PressureAnomaly.Clone(),charge=(double[])Charge.Clone(),cooldown=(double[])cooldown.Clone(),updraft=(double[])Updraft.Clone(),stormAge=(double[])StormAge.Clone(),stage=(int[])Stage.Clone(),rain=(double[])RainRate.Clone(),snow=(double[])SnowRate.Clone(),vertical=(double[])VerticalVelocity.Clone(),gust=(double[])gustLift.Clone(),cape=(double[])CAPE.Clone(),cin=(double[])CIN.Clone(),lcl=(double[])LCL.Clone()};
        }
        public void Restore(OrbisAtmosphereState state)
        {
            Validate(state,Count);double[][] src={state.qv,state.qc,state.qi,state.qr,state.qs,state.qg};
            for(int s=0;s<Species;s++)Array.Copy(src[s],Water[s],Water[s].Length);
            Array.Copy(state.temperature,Temperature,Temperature.Length);Array.Copy(state.wind,Wind,Wind.Length);
            Copy(state.surface,SurfaceWater);Copy(state.film,Film);Copy(state.coldPool,ColdPool);Copy(state.pressure,PressureAnomaly);
            Copy(state.charge,Charge);Copy(state.cooldown,cooldown);Copy(state.updraft,Updraft);Copy(state.stormAge,StormAge);Array.Copy(state.stage,Stage,Count);
            Copy(state.rain,RainRate);Copy(state.snow,SnowRate);Copy(state.vertical,VerticalVelocity);Copy(state.gust,gustLift);Copy(state.cape,CAPE);Copy(state.cin,CIN);Copy(state.lcl,LCL);
            Time=state.time;random=state.random;InitialWater=state.initial;ExternalWater=state.external;RunoffWater=state.runoff;LightningCount=state.lightningCount;
            Lightning.Clear();for(int i=0;i<Count;i++){Wetness[i]=1-Math.Exp(-Film[i]/.35);Diagnose(i,0);}
        }
        static void Copy(double[] a,double[] b){Array.Copy(a,b,b.Length);}
        public static void Validate(OrbisAtmosphereState s,int n)
        {
            if(s==null||s.version!=1||s.count!=n||!Finite(s.time)||s.time<0||!Finite(s.initial)||!Finite(s.external)||!Finite(s.runoff)||s.initial<0||s.external<0||s.runoff<0||s.lightningCount<0)throw new ArgumentException("Invalid layered atmosphere header.");
            double[][] fields={s.qv,s.qc,s.qi,s.qr,s.qs,s.qg,s.temperature,s.surface,s.film,s.coldPool,s.pressure,s.charge,s.cooldown,s.updraft,s.stormAge,s.rain,s.snow,s.vertical,s.gust,s.cape,s.cin,s.lcl};
            for(int f=0;f<fields.Length;f++)
            {
                double[] a=fields[f];int length=f<=6||f==17?n*Layers:n;
                if(a==null||a.Length!=length)throw new ArgumentException("Invalid layered atmosphere array.");
                foreach(double v in a)if(!Finite(v)||Math.Abs(v)>1e6||(f!=10&&f!=17&&v<0))throw new ArgumentException("Invalid layered atmosphere number.");
            }
            if(s.wind==null||s.wind.Length!=n*Layers||s.stage==null||s.stage.Length!=n)throw new ArgumentException("Invalid atmosphere vectors.");
            foreach(int stage in s.stage)if(stage<0||stage>4)throw new ArgumentException("Invalid storm stage.");
            foreach(var v in s.wind)if(!Finite(v.X)||!Finite(v.Y)||!Finite(v.Z)||v.Length>.2)throw new ArgumentException("Invalid atmosphere wind.");
        }
    }
    [Serializable] public struct AtmosVector
    {
        public double X,Y,Z;
        public AtmosVector(double x,double y,double z){X=x;Y=y;Z=z;}
        public double Length {get{return Math.Sqrt(X*X+Y*Y+Z*Z);}}
        public AtmosVector Normalized {get{return this/Math.Max(1e-20,Length);}}
        public AtmosVector Tangent(AtmosVector p){return this-p*Dot(this,p);}
        public AtmosVector Limited(double m){double square=X*X+Y*Y+Z*Z;return square>m*m?this*(m/Math.Sqrt(square)):this;}
        public static double Dot(AtmosVector a,AtmosVector b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
        public static AtmosVector Cross(AtmosVector a,AtmosVector b){return new AtmosVector(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);}
        public static AtmosVector Lerp(AtmosVector a,AtmosVector b,double t){return a+(b-a)*t;}
        public static AtmosVector operator +(AtmosVector a,AtmosVector b){return new AtmosVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
        public static AtmosVector operator -(AtmosVector a,AtmosVector b){return new AtmosVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
        public static AtmosVector operator *(AtmosVector a,double b){return new AtmosVector(a.X*b,a.Y*b,a.Z*b);}
        public static AtmosVector operator /(AtmosVector a,double b){return a*(1/b);}
    }
    public struct AtmosEdge {public int A,B;public AtmosVector Direction;public double Length,Distance;}
    public struct AtmosphereLightning {public int Cell;public double Height,Strength,Time;public bool CloudToGround;public long Sequence;}
    [Serializable] public sealed class OrbisAtmosphereState
    {
        public int version=1,count;public uint random;public double time,initial,external,runoff;public long lightningCount;
        public double[] qv,qc,qi,qr,qs,qg,temperature,surface,film,coldPool,pressure,charge,cooldown,updraft,stormAge;
        public double[] rain,snow,vertical,gust,cape,cin,lcl;
        public AtmosVector[] wind;public int[] stage;
    }
}
