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
        readonly float[] temperature,rain,humidity,east,north,ground,soil,regionalSea;
        readonly bool[] water;readonly AtmosVector[] tangentEast,tangentNorth;
        public FlatWeatherSimulation(WorldData world,int workers=0)
        {
            int count=Width*Height;var p=new AtmosVector[count];var area=new double[count];var links=new List<AtmosEdge>();
            temperature=new float[count*12];rain=new float[count*12];humidity=new float[count*12];east=new float[count*12];north=new float[count*12];
            ground=new float[count];soil=new float[count];regionalSea=new float[count];water=new bool[count];tangentEast=new AtmosVector[count];tangentNorth=new AtmosVector[count];
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int i=y*Width+x;double u=(x+.5)/Width,v=(y+.5)/Height,lon=(u-.5)*Math.PI*2,lat=(v-.5)*Math.PI;
                p[i]=Direction(u,v);area[i]=Math.PI*2/Width*(Math.Sin((v-.5+.5/Height)*Math.PI)-Math.Sin((v-.5-.5/Height)*Math.PI));
                tangentEast[i]=new AtmosVector(Math.Cos(lon),0,-Math.Sin(lon));tangentNorth[i]=AtmosVector.Cross(p[i],tangentEast[i]);
                int cell=world.Index((int)(u*world.Width),(int)(v*world.Height));ground[i]=Math.Max(0,(int)world.Elevation[cell]);water[i]=FlatTerrainAnalysis.AreaWater(world.Water[cell]);
                soil[i]=world.Facts==null?.5f:FMath.Clamp(world.Facts.Wetness[cell],0,1);
                for(int m=0;m<12;m++)
                {
                    int j=i*12+m;temperature[j]=world.Temperature[cell*12+m];rain[j]=world.Precipitation[cell*12+m];var s=world.Moisture;
                    humidity[j]=s==null?FMath.Clamp(.35f+rain[j]/400,0,1):s.Sample(s.Humidity,(float)u,(float)v,m);
                    east[j]=s==null?0:s.Sample(s.East,(float)u,(float)v,m);north[j]=s==null?0:s.Sample(s.North,(float)u,(float)v,m);
                }
            }
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int i=y*Width+x;float nearby=0;
                for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)nearby+=water[Math.Max(0,Math.Min(Height-1,y+dy))*Width+(x+dx+Width)%Width]?1:0;
                regionalSea[i]=FMath.Lerp(water[i]?1:0,nearby/25,.55f);
            }
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int a=y*Width+x,b=y*Width+(x+1)%Width;
                AddLink(links,p,a,b,Math.PI/Height);
                if(y+1<Height)AddLink(links,p,a,a+Width,Math.PI*2/Width*Math.Cos(((y+1.0)/Height-.5)*Math.PI));
            }
            Model=new OrbisAtmosphereCore(p,area,links.ToArray(),FMath.Seed(world.Seed+"|flat-weather"));Model.SetParallelism(FlatWorkerPolicy.Resolve(workers));
            var disturbances=new List<AtmosVector>();
            foreach(bool isNorth in new[]{true,false}){var c=FlatCycloneSelection.Find(world,world.Month,isNorth);if(c.Active)disturbances.Add(Direction((c.X+.5)/world.Width,(c.Y+.5)/world.Height));}
            var rh=new double[count];for(int i=0;i<count;i++)rh[i]=humidity[i*12+world.Month];
            Model.SetInitialClimate(disturbances.ToArray(),rh);Forcing(world.Day);Model.Initialize();
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
        void Forcing(double day)
        {
            double season=((day%360)+360)%360;int m=(int)(season/30),next=(m+1)%12;float f=(float)(season/30-m);
            var zonalTemp=new double[Height];
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int i=y*Width+x,a=i*12+m,b=i*12+next;
                zonalTemp[y]+=FMath.Lerp(temperature[a],temperature[b],f)+ground[i]*.0065;
            }
            for(int y=0;y<Height;y++)zonalTemp[y]/=Width;
            for(int i=0;i<Model.Count;i++)
            {
                int a=i*12+m,b=i*12+next;double temp=FMath.Lerp(temperature[a],temperature[b],f),rainfall=FMath.Lerp(rain[a],rain[b],f);
                double lat=Math.Asin(Model.Position[i].Y)*180/Math.PI;
                double thermal=temp+ground[i]*.0065-zonalTemp[i/Width];
                double itcz=FlatMoisture.ConvergenceLatitude((float)(season/30),regionalSea[i],(float)thermal);
                double subtropical=regionalSea[i]>0.5?28:31;
                double pressure=1013+7*Math.Exp(-Math.Pow((Math.Abs(lat)-subtropical)/12,2))
                    -6*Math.Exp(-Math.Pow((lat-itcz)/14,2))-Math.Max(-4,Math.Min(4,thermal))*.30;
                int x=i%Width,y=i/Width,west=y*Width+(x+Width-1)%Width,eastCell=y*Width+(x+1)%Width;
                int southCell=Math.Max(0,y-1)*Width+x,northCell=Math.Min(Height-1,y+1)*Width+x;
                double westT=FMath.Lerp(temperature[west*12+m],temperature[west*12+next],f)+ground[west]*.0065;
                double eastT=FMath.Lerp(temperature[eastCell*12+m],temperature[eastCell*12+next],f)+ground[eastCell]*.0065;
                double southT=FMath.Lerp(temperature[southCell*12+m],temperature[southCell*12+next],f)+ground[southCell]*.0065;
                double northT=FMath.Lerp(temperature[northCell*12+m],temperature[northCell*12+next],f)+ground[northCell]*.0065;
                double gradient=Math.Sqrt(Math.Pow((eastT-westT)*.5,2)+Math.Pow((northT-southT)*.5,2));
                double relativeHumidity=FMath.Lerp(humidity[a],humidity[b],f);
                double lift=SynopticLift(rainfall,relativeHumidity,temp,water[i],gradient);
                var velocity=tangentEast[i]*FMath.Lerp(east[a],east[b],f)+tangentNorth[i]*FMath.Lerp(north[a],north[b],f);
                double daylight=Math.Max(0,Math.Cos((day%1)*Math.PI*2+(i%Width+.5)*Math.PI*2/Width));
                Model.SetForcing(i,temp+273.15,daylight,soil[i],water[i],velocity,pressure,ground[i],lift,0);
            }
        }
        public void Step(double seconds,double calendarDay){Forcing(calendarDay);Model.TickParallel(seconds);}
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
