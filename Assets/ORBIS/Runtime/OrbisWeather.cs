using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis
{
    /// <summary>Fixed-step Unity adapter for the conservative 12-layer atmosphere.</summary>
    public sealed class OrbisWeather
    {
        public const double FixedStep=.1;
        public const float FlowPeriod=12f,AngularSpeed=.014f;
        public double Clock {get;private set;}
        public double Accumulator {get;private set;}
        public long Steps {get;private set;}
        public int DroppedSteps {get;private set;}
        public readonly OrbisSphericalSampler sampler;
        public readonly OrbisAtmosphereCore Atmosphere;
        readonly OrbisWorld world;
        public OrbisWeather(OrbisWorld owner,bool initialize=true,double clock=0,double accumulator=0,OrbisAtmosphereState saved=null)
        {
            world=owner;sampler=new OrbisSphericalSampler(world.grid);
            Clock=Math.Max(0,clock);Accumulator=Math.Max(0,Math.Min(.8,accumulator));
            var grid=world.grid;int n=grid.points.Length;var p=new AtmosVector[n];var area=new double[n];
            var links=new List<AtmosEdge>();var owners=new Dictionary<long,int>();
            for(int i=0;i<n;i++)p[i]=ToAtmos(grid.points[i]);
            for(int i=0;i<n;i++)
            {
                int[] corners=grid.tileCorners[i];
                for(int k=0;k<corners.Length;k++)
                {
                    int a=corners[k],b=corners[(k+1)%corners.Length];
                    AtmosVector va=ToAtmos(grid.corners[a]),vb=ToAtmos(grid.corners[b]);
                    area[i]+=TriangleArea(p[i],va,vb);
                    long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                    if(owners.TryGetValue(key,out int other))
                    {
                        links.Add(new AtmosEdge{A=other,B=i,Direction=(p[i]-p[other]).Normalized,
                            Length=Math.Acos(OrbisAtmosphereCore.Clamp(AtmosVector.Dot(va,vb),-1,1)),
                            Distance=Math.Acos(OrbisAtmosphereCore.Clamp(AtmosVector.Dot(p[other],p[i]),-1,1))});
                    }
                    else owners[key]=i;
                }
            }
            Atmosphere=new OrbisAtmosphereCore(p,area,links.ToArray(),RandomStream.Hash(world.seed+"|moist-atmosphere-42"));
            Atmosphere.SetParallelism(world.settings!=null?world.settings.climateWorkerLimit:0);
            Forcing();Atmosphere.Initialize();
            if(saved!=null)Atmosphere.Restore(saved);
            Publish();
        }
        static double TriangleArea(AtmosVector a,AtmosVector b,AtmosVector c)
        {
            return 2*Math.Atan2(Math.Abs(AtmosVector.Dot(a,AtmosVector.Cross(b,c))),
                1+AtmosVector.Dot(a,b)+AtmosVector.Dot(b,c)+AtmosVector.Dot(c,a));
        }
        public static AtmosVector ToAtmos(Vector3 p){return new AtmosVector(p.x,p.y,p.z);}
        public static Vector3 ToUnity(AtmosVector p){return new Vector3((float)p.X,(float)p.Y,(float)p.Z);}
        // Legacy diagnostic/save API retained. Rendering now samples actual cloud layers.
        public static float Saturation(float temperature)=>Mathf.Clamp(.38f*Mathf.Exp(Mathf.Clamp(temperature,-65,65)*.019f),.16f,1.35f);
        public static float Coverage(float water)=>1-Mathf.Exp(-Mathf.Max(0,water)*5f);
        public static void React(ref float qv,ref float qc,float saturation,float dt,out float rainfall)
        {
            float cond=Mathf.Max(0,qv-saturation)*(1-Mathf.Exp(-dt*.9f));qv-=cond;qc+=cond;
            float evap=Mathf.Min(qc,Mathf.Max(0,saturation-qv)*(1-Mathf.Exp(-dt*.55f)));qc-=evap;qv+=evap;
            rainfall=Mathf.Max(0,qc-.20f)*(1-Mathf.Exp(-dt*.3f));qc-=rainfall;
        }
        void Forcing()
        {
            Vector3 sun=world.SunLocal;
            for(int i=0;i<world.tiles.Length;i++)
            {
                Tile t=world.tiles[i];
                // Legacy latitude/ocean circulation provides boundary forcing, NOT cloud coverage.
                Atmosphere.SetForcing(i,t.Temperature+273.15,Mathf.Max(0,Vector3.Dot(world.grid.points[i],sun)),t.soil,
                    t.h<=0||t.lake>.018f,ToAtmos(t.wind),t.pressure,Mathf.Max(0,t.h)*7000,0,t.boost);
            }
        }
        public void Advance(double seconds,int maxSteps=32)
        {
            if(!world.settings.dynamicWeather||seconds<=0)return;
            if(double.IsNaN(seconds)||double.IsInfinity(seconds))throw new ArgumentException("Invalid weather timestep.");
            double accepted=Math.Min(seconds,3.2);
            DroppedSteps+=(int)Math.Min(int.MaxValue-DroppedSteps,Math.Max(0,(seconds-accepted)/FixedStep));
            Accumulator+=accepted;int budget=Math.Max(1,Math.Min(32,maxSteps)),executed=0;
            while(Accumulator+1e-10>=FixedStep&&budget-->0)
            {Forcing();Atmosphere.TickParallel(FixedStep);UpdateSnow(FixedStep);Clock+=FixedStep;Steps++;executed++;Accumulator=Math.Max(0,Accumulator-FixedStep);}
            if(Accumulator>.8){int dropped=(int)((Accumulator-.8)/FixedStep)+1;DroppedSteps+=dropped;Accumulator-=dropped*FixedStep;}
            if(executed>0)Publish();
        }
        void UpdateSnow(double dt)
        {
            // Tile snow is a visual snow-water-equivalent diagnostic. The physical water
            // already entered SurfaceWater in Sedimentation; do NOT add it a second time.
            double hours=dt*OrbisAtmosphereCore.PhysicalSecondsPerSecond/3600.0;
            for(int i=0;i<world.tiles.Length;i++)
            {
                Tile t=world.tiles[i];if(t.h<=0)continue;
                double melt=Math.Max(0,t.Temperature)*2.5*hours/24.0;
                t.snow=(float)Math.Max(0,Math.Min(240,t.snow+Atmosphere.SnowRate[i]*hours-melt));
            }
        }
        public void Publish()
        {
            var a=Atmosphere;
            for(int i=0;i<world.tiles.Length;i++)
            {
                Tile t=world.tiles[i];
                t.cloud=(float)a.Coverage[i];t.cloudWater=(float)Math.Min(2,-Math.Log(Math.Max(1e-8,1-a.Coverage[i]))/5);
                double rh=a.Water[0][i]/OrbisAtmosphereCore.AirMass[0]/OrbisAtmosphereCore.Saturation(a.Temperature[i],OrbisAtmosphereCore.Pressure[0]);
                t.moist=(float)OrbisAtmosphereCore.Clamp(rh,0,1);t.vapor=Saturation(t.Temperature)*t.moist;
                t.cloudWind=ToUnity(a.Wind[2*a.Count+i]/AngularSpeed);t.convection=(float)OrbisAtmosphereCore.Clamp(a.Updraft[i]/12,0,1);
                t.precipitationMmH=(float)a.RainRate[i];t.snowfallMmH=(float)a.SnowRate[i];t.cloudRain=t.precipitationMmH/3600;
                t.rain=Mathf.Clamp(t.precipitationMmH*8+t.snowfallMmH*4,0,1800); // legacy hydrology UI proxy
                t.surfaceWetness=(float)a.Wetness[i];t.cloudBaseMeters=(float)a.CloudBase[i];t.cloudTopMeters=(float)a.CloudTop[i];
                t.stormStage=a.Stage[i];t.cape=(float)a.CAPE[i];t.updraft=(float)a.Updraft[i];
                t.pressure=1013+(float)a.PressureAnomaly[i];
            }
        }
    }
}
