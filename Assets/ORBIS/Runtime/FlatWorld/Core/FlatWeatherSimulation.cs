using System;
using System.Collections.Generic;
namespace Orbis.Flat
{
    // Immutable render publication; the background worker exclusively owns Model.
    public sealed class FlatWeatherFrame
    {
        public int Width,Height;public double Time,BudgetError;
        public float[] Clouds,Wind,Effects; // RGBA: low/high/type/convection; XYZ wind; rain/snow/wetness/reserved
        public AtmosphereLightning[] Lightning;
    }
    public sealed class FlatWeatherSimulation
    {
        public const int Width=64,Height=32;
        public readonly OrbisAtmosphereCore Model;
        readonly float[] temperature,rain,humidity,east,north,ground,soil,regionalLand;
        readonly float[] currentRain;
        readonly double[] seaLevelTemperature,zonalTemperature=new double[Height];
        readonly uint climateSeed;
        readonly bool[] water;readonly AtmosVector[] tangentEast,tangentNorth;
        double observedModelTime=double.NaN,precipitationWindowDays=double.NaN;
        long observedEpoch;
        public double LastCalendarDay { get; private set; } = double.NaN;
        internal bool IsWaterSurface(int cell) { return water[cell]; }
        // Called on the solver's owner thread after a complete step. A consumer
        // can copy immutable station observations without reading a worker mid-tick.
        public event Action<FlatWeatherSimulation,double> Stepped;
        public FlatWeatherSimulation(WorldData world,int workers=0)
        {
            int count=Width*Height;var p=new AtmosVector[count];var area=new double[count];var links=new List<AtmosEdge>();
            climateSeed=FMath.Seed(world.Seed);currentRain=new float[count];seaLevelTemperature=new double[count];
            temperature=new float[count*12];rain=new float[count*12];humidity=new float[count*12];east=new float[count*12];north=new float[count*12];
            ground=new float[count];soil=new float[count];var land=new float[count];water=new bool[count];tangentEast=new AtmosVector[count];tangentNorth=new AtmosVector[count];
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int i=y*Width+x;double u=(x+.5)/Width,v=(y+.5)/Height,lon=(u-.5)*Math.PI*2,lat=(v-.5)*Math.PI;
                p[i]=Direction(u,v);area[i]=Math.PI*2/Width*(Math.Sin((v-.5+.5/Height)*Math.PI)-Math.Sin((v-.5-.5/Height)*Math.PI));
                tangentEast[i]=new AtmosVector(Math.Cos(lon),0,-Math.Sin(lon));tangentNorth[i]=AtmosVector.Cross(p[i],tangentEast[i]);
                int cell=world.Index((int)(u*world.Width),(int)(v*world.Height));ground[i]=FlatClimate.SurfaceHeight(world,cell);water[i]=FlatTerrainAnalysis.AreaWater(world.Water[cell]);
                land[i]=water[i]?0:FlatMoisture.Continentality(world.CoastDistance[cell],world.Width);
                soil[i]=world.Facts==null?.5f:FMath.Clamp(world.Facts.Wetness[cell],0,1);
                for(int m=0;m<12;m++)
                {
                    int j=i*12+m;temperature[j]=world.Temperature[cell*12+m];rain[j]=world.Precipitation[cell*12+m];var s=world.Moisture;
                    humidity[j]=s==null?FMath.Clamp(.35f+rain[j]/400,0,1):s.Sample(s.Humidity,(float)u,(float)v,m);
                    east[j]=s==null?0:s.Sample(s.East,(float)u,(float)v,m);north[j]=s==null?0:s.Sample(s.North,(float)u,(float)v,m);
                }
            }
            regionalLand=FlatMoisture.RegionalContinentality(land,Width,Height);
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int a=y*Width+x,b=y*Width+(x+1)%Width;
                AddLink(links,p,a,b,Math.PI/Height);
                if(y+1<Height)AddLink(links,p,a,a+Width,Math.PI*2/Width*Math.Cos(((y+1.0)/Height-.5)*Math.PI));
            }
            Model=new OrbisAtmosphereCore(p,area,links.ToArray(),FMath.Seed(world.Seed+"|flat-weather"));Model.SetParallelism(FlatWorkerPolicy.Resolve(workers));
            var season=new FlatMonthlyForcing(world.Day);
            var rh=new double[count];for(int i=0;i<count;i++)rh[i]=season.Sample(humidity,i*12,ClimateRange.Fraction);
            // This independent layered solver already evolves pressure and convergence
            // from its wind and thermal forcing. Start it without artificial lows.
            Model.SetInitialClimate(Array.Empty<AtmosVector>(),rh);Forcing(world.Day);Model.Initialize();
        }
        static void AddLink(List<AtmosEdge> links,AtmosVector[] p,int a,int b,double length)
        {links.Add(new AtmosEdge{A=a,B=b,Direction=(p[b]-p[a]).Normalized,Length=length,Distance=Math.Acos(OrbisAtmosphereCore.Clamp(AtmosVector.Dot(p[a],p[b]),-1,1))});}
        public static AtmosVector Direction(double u,double v)
        {double lon=(u-.5)*Math.PI*2,lat=(v-.5)*Math.PI,c=Math.Cos(lat);return new AtmosVector(Math.Sin(lon)*c,Math.Sin(lat),Math.Cos(lon)*c);}
        public static double SynopticLift(double rainfall,double humidity,double celsius,bool water,double thermalGradient)
        {
            // Saturated marine boundary layers and horizontal thermal contrasts
            // can form mixed-phase / frontal clouds without tropical convection.
            double moist=FMath.Clamp((float)((humidity-.58)*2.5),0,1);
            double marine=water?moist*(.10+FMath.Clamp((float)((14-celsius)/35),0,1)*.24):0;
            double front=moist*Math.Min(.50,Math.Max(0,thermalGradient)*.10);
            return Math.Min(.85,Math.Max(0,rainfall)/1000+marine+front);
        }

        internal static double SeaLevelPressure(double latitudeDegrees,float seasonalMonth,float regionalLand,double thermal)
        {
            float axis=FlatMoisture.ConvergenceLatitude(seasonalMonth,1-regionalLand,0);
            double itcz=FlatMoisture.LocalConvergence((float)latitudeDegrees,axis,(float)thermal);
            double subtropical=28+3*regionalLand;
            return OrbisAtmosphereCore.SeaLevelReferenceHPa+7*Math.Exp(-Math.Pow((Math.Abs(latitudeDegrees)-subtropical)/12,2))
                -6*Math.Exp(-Math.Pow((latitudeDegrees-itcz)/14,2))-Math.Max(-4,Math.Min(4,thermal))*.30;
        }
        void Forcing(double day)
        {
            var season=new FlatMonthlyForcing(day);
            var year=FlatClimateYear.At(climateSeed,day);
            for(int y=0;y<Height;y++)
            {
                double row=0;
                for(int x=0;x<Width;x++)
                {
                    int i=y*Width+x,start=i*12;var p=Model.Position[i];
                    year.Offsets((float)p.X,(float)p.Y,(float)p.Z,out float degrees,out float rainFraction);
                    currentRain[i]=season.Sample(rain,start,ClimateRange.Nonnegative)*(1+rainFraction);
                    seaLevelTemperature[i]=season.Sample(temperature,start)+degrees+ground[i]*.0065;
                    row+=seaLevelTemperature[i];
                }
                zonalTemperature[y]=row/Width;
            }
            for(int i=0;i<Model.Count;i++)
            {
                int start=i*12;double temp=seaLevelTemperature[i]-ground[i]*.0065;
                double lat=Math.Asin(Model.Position[i].Y)*180/Math.PI;
                double thermal=seaLevelTemperature[i]-zonalTemperature[i/Width];
                double pressure=SeaLevelPressure(lat,season.SeasonalMonth,regionalLand[i],thermal);
                int x=i%Width,y=i/Width,west=y*Width+(x+Width-1)%Width,eastCell=y*Width+(x+1)%Width;
                int southCell=Math.Max(0,y-1)*Width+x,northCell=Math.Min(Height-1,y+1)*Width+x;
                double gradient=Math.Sqrt(Math.Pow((seaLevelTemperature[eastCell]-seaLevelTemperature[west])*.5,2)
                    +Math.Pow((seaLevelTemperature[northCell]-seaLevelTemperature[southCell])*.5,2));
                double relativeHumidity=season.Sample(humidity,start,ClimateRange.Fraction);
                double lift=SynopticLift(currentRain[i],relativeHumidity,temp,water[i],gradient);
                var velocity=tangentEast[i]*season.Sample(east,start)+tangentNorth[i]*season.Sample(north,start);
                double daylight=Math.Max(0,Math.Cos((day%1)*Math.PI*2+(i%Width+.5)*Math.PI*2/Width));
                Model.SetForcing(i,temp+273.15,daylight,soil[i],water[i],velocity,pressure,ground[i],lift,0);
            }
        }
        public void Step(double seconds,double calendarDay)
        {
            Forcing(calendarDay);
            Model.TickParallel(seconds);
            LastCalendarDay=calendarDay;
            observedModelTime=Model.Time;
            observedEpoch=Model.StateEpoch;
            precipitationWindowDays=Model.MicrophysicsEnabled?seconds*OrbisAtmosphereCore.PhysicalSecondsPerSecond/86400:double.NaN;
            Stepped?.Invoke(this,calendarDay);
        }

        // Lowest prognostic layer (0–300 m, midpoint 150 m). Pressure is
        // referenced to sea level; precipitation is a short-step flux at the
        // underlying surface. Called by the solver owner after Step.
        public FlatClimateObservation Instantaneous(int cell)
        {
            var result=FlatClimateObservation.Missing;
            if((uint)cell>=(uint)Model.Count || Model.Time!=observedModelTime || Model.StateEpoch!=observedEpoch)return result;
            result.Source=FlatClimateObservationSource.LayeredStep;
            result.Temperature=Model.Temperature[cell]-273.15;
            double saturation=OrbisAtmosphereCore.Saturation(Model.Temperature[cell],OrbisAtmosphereCore.Pressure[0])
                *OrbisAtmosphereCore.AirMass[0];
            result.Humidity=OrbisAtmosphereCore.Clamp(Model.Water[0][cell]/saturation,0,1);
            result.Pressure=OrbisAtmosphereCore.SeaLevelReferenceHPa+Model.PressureAnomaly[cell];
            result.WindSpeed=OrbisAtmosphereCore.SurfaceWindMetresPerSecond(Model.Wind[cell].Length);
            if(!double.IsNaN(precipitationWindowDays))
            {
                result.Precipitation=(Model.RainRate[cell]+Model.SnowRate[cell])*24;
                result.PrecipitationWindowDays=precipitationWindowDays;
            }
            return result;
        }
        public FlatWeatherFrame Capture()
        {
            var frame=new FlatWeatherFrame{Width=Width,Height=Height,Time=Model.Time,BudgetError=Model.WaterBudgetError(),Clouds=new float[Model.Count*4],Wind=new float[Model.Count*4],Effects=new float[Model.Count*4]};
            for(int i=0;i<Model.Count;i++)
            {
                double low=0,high=0;for(int k=0;k<OrbisAtmosphereCore.Layers;k++){int j=k*Model.Count+i;double q=Model.Water[1][j]+Model.Water[2][j];if(k<8)low+=q;else high+=q;}
                // Cold mixed-phase cloud paths hold less absolute water than
                // tropical columns; use temperature-aware optical conversion.
                double celsius=Model.Temperature[i]-273.15;
                double optical=1.8+5*FMath.Clamp((float)((8-celsius)/38),0,1);
                double saturation=OrbisAtmosphereCore.Saturation(Model.Temperature[i],OrbisAtmosphereCore.Pressure[0])*OrbisAtmosphereCore.AirMass[0];
                double rh=Model.Water[0][i]/Math.Max(1e-8,saturation);
                double subgrid=Math.Pow(FMath.Clamp((float)((rh-.74)/.24),0,1),1.5)*.38*
                    FMath.Clamp((float)((22-celsius)/32),0,1);
                int o=i*4;frame.Clouds[o]=(float)(1-(Math.Exp(-low*optical))*(1-subgrid));frame.Clouds[o+1]=(float)(1-Math.Exp(-high*optical));
                frame.Clouds[o+2]=(float)OrbisAtmosphereCore.Clamp((Model.CloudTop[i]-1500)/10000,.12,1);frame.Clouds[o+3]=(float)OrbisAtmosphereCore.Clamp(Model.Updraft[i]/12,0,1);
                var wind=Model.Wind[2*Model.Count+i];frame.Wind[o]=(float)wind.X;frame.Wind[o+1]=(float)wind.Y;frame.Wind[o+2]=(float)wind.Z;
                frame.Effects[o]=(float)(1-Math.Exp(-Model.RainRate[i]/8));frame.Effects[o+1]=(float)(1-Math.Exp(-Model.SnowRate[i]/5));frame.Effects[o+2]=(float)Model.Wetness[i];
            }
            frame.Lightning=Model.Lightning.ToArray();Model.Lightning.Clear();return frame;
        }
    }
}
