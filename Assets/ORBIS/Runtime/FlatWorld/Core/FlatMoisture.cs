using System;
using System.Threading;
using System.Threading.Tasks;
namespace Orbis.Flat
{
    // Monthly equilibrium columns on a bounded grid. mm of precipitable water,
    // conservative cell-area-weighted transport, prescribed (not solved) circulation.
    public sealed class FlatMoistureState
    {
        public readonly int Width,Height;
        // Column relative humidity, condensation (mm/day), normalized surface
        // wind components, and precipitation (mm per 30-day simulation month).
        public readonly float[] Humidity,Condensation,East,North,Rain;
        public readonly double[] BudgetError=new double[12];
        public FlatMoistureState(int width,int height)
        {Width=width;Height=height;int n=width*height*12;Humidity=new float[n];Condensation=new float[n];East=new float[n];North=new float[n];Rain=new float[n];}
        public FlatMoistureState Clone()
        {
            var s=new FlatMoistureState(Width,Height);Array.Copy(Humidity,s.Humidity,Humidity.Length);Array.Copy(Condensation,s.Condensation,Humidity.Length);
            Array.Copy(East,s.East,Humidity.Length);Array.Copy(North,s.North,Humidity.Length);Array.Copy(Rain,s.Rain,Humidity.Length);Array.Copy(BudgetError,s.BudgetError,12);return s;
        }
        public float Sample(float[] data,float u,float v,int month)
        {
            float px=u*Width-.5f,py=FMath.Clamp(v*Height-.5f,0,Height-1);int x=(int)Math.Floor(px),y=(int)py;
            float tx=px-x,ty=py-y;int a=(y*Width+FMath.Wrap(x,Width))*12+month,b=(y*Width+FMath.Wrap(x+1,Width))*12+month;
            int c=(Math.Min(y+1,Height-1)*Width+FMath.Wrap(x,Width))*12+month,d=(Math.Min(y+1,Height-1)*Width+FMath.Wrap(x+1,Width))*12+month;
            return FMath.Lerp(FMath.Lerp(data[a],data[b],tx),FMath.Lerp(data[c],data[d],tx),ty);
        }
    }
    public static class FlatMoisture
    {
        struct TransportWeights
        {
            public float Center,West,East,South,North;
        }
        // Marine thermal influence reaches inland from the coast. Distances
        // and fetch are both in native terrain tiles.
        internal static float Continentality(ushort coastDistance,int worldWidth)
        {return FMath.Clamp(coastDistance/(worldWidth*.09f),0,1);}
        internal static float[] RegionalContinentality(float[] land,int width,int height)
        {
            var region=new float[width*height];
            int radius=Math.Max(1,(int)Math.Round(width/64.0)),span=radius*2+1;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                float sum=0;
                for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++)
                    sum+=land[FMath.Clamp(y+dy,0,height-1)*width+FMath.Wrap(x+dx,width)];
                int i=y*width+x;region[i]=FMath.Lerp(land[i],sum/(span*span),.55f);
            }
            return region;
        }
        // Land heats faster than sea, so the convergence zone follows the
        // local thermal contrast rather than a permanently straight parallel.
        public static float ConvergenceLatitude(float month,float seaFraction,float thermalAnomaly)
        {
            float season=(float)Math.Cos((month-6)*Math.PI/6);
            float excursion=FMath.Lerp(32.5f,4f,FMath.Clamp(seaFraction,0,1));
            return FMath.Clamp(season*excursion+FMath.Clamp(thermalAnomaly,-5,5)*1.2f,-38,38);
        }
        public static float Capacity(float temperature)
        {return 1.3f*6.112f*(float)Math.Exp(17.67*Math.Max(-70,temperature)/(Math.Max(-70,temperature)+243.5));}
        public static FlatMoistureState Build(WorldData world,CancellationToken cancel,int workers=0,bool evaporate=true)
        {
            int width=Math.Min(128,world.Width),height=width/2,count=width*height;
            var state=new FlatMoistureState(width,height);var elevation=new float[count];var water=new float[count];var area=new float[count];
            var continentality=new float[count];var monthlyTemperature=new float[count*12];
            var westCell=new int[count];var eastCell=new int[count];var southCell=new int[count];var northCell=new int[count];
            // Integrate the overlap of equal-angle cells on the sphere. Imported
            // terrain dimensions can be valid without dividing the climate grid.
            var sourceLatitude=new double[world.Height+1];var targetLatitude=new double[height+1];
            for(int y=0;y<=world.Height;y++)sourceLatitude[y]=Math.Sin(((double)y/world.Height-.5)*Math.PI);
            for(int y=0;y<=height;y++)targetLatitude[y]=Math.Sin(((double)y/height-.5)*Math.PI);
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                if(x==0)cancel.ThrowIfCancellationRequested();
                int i=y*width+x;area[i]=(float)Math.Cos(((y+.5)/height-.5)*Math.PI);
                westCell[i]=y*width+FMath.Wrap(x-1,width);eastCell[i]=y*width+FMath.Wrap(x+1,width);
                southCell[i]=Math.Max(0,y-1)*width+x;northCell[i]=Math.Min(height-1,y+1)*width+x;
                int endY=((y+1)*world.Height+height-1)/height,endX=((x+1)*world.Width+width-1)/width;
                for(int sy=y*world.Height/height;sy<endY;sy++)
                {
                    double rowWeight=(Math.Min(sourceLatitude[sy+1],targetLatitude[y+1])-Math.Max(sourceLatitude[sy],targetLatitude[y]))
                        /(targetLatitude[y+1]-targetLatitude[y]);
                    for(int sx=x*world.Width/width;sx<endX;sx++)
                    {
                        int overlap=Math.Min((sx+1)*width,(x+1)*world.Width)-Math.Max(sx*width,x*world.Width);
                        float weight=(float)(rowWeight*overlap/world.Width);int n=sy*world.Width+sx;
                        elevation[i]+=weight*FlatClimate.SurfaceHeight(world,n);
                        if(FlatTerrainAnalysis.AreaWater(world.Water[n]))water[i]+=weight;
                        else continentality[i]+=weight*Continentality(world.CoastDistance[n],world.Width);
                        for(int m=0;m<12;m++)monthlyTemperature[i*12+m]+=weight*world.Temperature[n*12+m];
                    }
                }
                water[i]=FMath.Clamp(water[i],0,1);
            }
            var regionalLand=RegionalContinentality(continentality,width,height);
            // One reference cell is 360/128 degrees. A fixed physical wind or
            // diffusivity travels fewer grid cells on a coarser climate map.
            float gridScale=width/128f,eddy=.065f*gridScale*gridScale;
            const int steps=512,measure=128;const float dt=.25f;
            Parallel.For(0,12,FlatWorkerPolicy.Options(cancel,workers),month=>
            {
                var pet=new float[count];var cap=new float[count];var pressure=new float[count];var u=new float[count];var v=new float[count];
                var q=new float[count];var next=new float[count];var soil=new float[count];var lift=new float[count];
                var zonalTemp=new float[height];var weights=new TransportWeights[count];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int i=y*width+x;float temp=monthlyTemperature[i*12+month];
                    pet[i]=evaporate?Math.Max(.05f,2+temp*.10f)*dt:0;
                    cap[i]=Capacity(temp);q[i]=cap[i]*.55f;soil[i]=50*(1-water[i]);
                    zonalTemp[y]+=temp+elevation[i]*.0065f;
                }
                for(int y=0;y<height;y++)zonalTemp[y]/=width;
                var convergenceLatitude=new float[count];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int i=y*width+x;float lat=((y+.5f)/height-.5f)*180;
                    float seaRegion=1-regionalLand[i];
                    float thermal=monthlyTemperature[i*12+month]+elevation[i]*.0065f-zonalTemp[y];
                    float itcz=convergenceLatitude[i]=ConvergenceLatitude(month,seaRegion,thermal);
                    // The solstitial heating contrast reverses continuously
                    // through the equator and grows toward the tropics.
                    float hemisphere=FMath.Clamp((float)Math.Sin(lat*Math.PI/180)*2.5f,-1,1);
                    float seasonalHeat=regionalLand[i]*(float)Math.Cos((month-6)*Math.PI/6)*hemisphere*Math.Max(0,1-Math.Abs(lat)/75);
                    float subtropical=FMath.Lerp(30,28,seaRegion)+Math.Max(0,seasonalHeat)*3;
                    pressure[i]=1.7f*(float)Math.Exp(-Math.Pow((Math.Abs(lat)-subtropical)/12,2))
                        -1.2f*(float)Math.Exp(-Math.Pow((lat-itcz)/15,2))-seasonalHeat*.8f-thermal*.055f;
                }
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int i=y*width+x;float lat=((y+.5f)/height-.5f)*180,ab=Math.Abs(lat);
                    float gx=(pressure[eastCell[i]]-pressure[westCell[i]])*.5f*gridScale;
                    float gy=(pressure[northCell[i]]-pressure[southCell[i]])*.5f*gridScale;
                    float coriolis=(float)Math.Sin(lat*Math.PI/180),belt=(float)Math.Tanh((ab-30)/8)*(float)Math.Tanh((68-ab)/8);
                    float east=belt*.24f-gy*coriolis*.08f,north=gx*coriolis*.08f;
                    float surfaceEast=FMath.Clamp(east-gx*.12f,-.32f,.32f);
                    float surfaceNorth=y==0||y==height-1?0:FMath.Clamp(north-gy*.20f,-.14f,.14f);
                    state.East[i*12+month]=surfaceEast/.32f;state.North[i*12+month]=surfaceNorth/.32f;
                    // Only the boundary-layer share flows directly across isobars.
                    // Driving the full column with the surface inflow evacuates
                    // subtropical highs and piles their water into narrow rain belts.
                    const float boundaryShare=.45f;
                    u[i]=FMath.Clamp(east-gx*.12f*boundaryShare,-.32f,.32f)*gridScale;
                    v[i]=y==0||y==height-1?0:FMath.Clamp(north-gy*.20f*boundaryShare,-.14f,.14f)*gridScale;
                    int upstream=u[i]>=0?westCell[i]:eastCell[i];
                    float rise=Math.Max(0,elevation[i]-elevation[upstream])*Math.Abs(u[i]);
                    upstream=v[i]>=0?southCell[i]:northCell[i];rise+=Math.Max(0,elevation[i]-elevation[upstream])*Math.Abs(v[i]);
                    float convection=.065f*(float)Math.Exp(-Math.Pow((lat-convergenceLatitude[i])/16,2));
                    float fronts=.040f*(float)Math.Exp(-Math.Pow((ab-52)/16,2));
                    lift[i]=convection+fronts+Math.Min(.30f,rise/1400);
                }
                double initial=0,external=0,export=0;
                // Cache advection and transient exchange at each face once per
                // month. Gather incoming water in units of the receiving area.
                for(int i=0;i<count;i++)
                {
                    int w=westCell[i],e=eastCell[i],s=southCell[i],n=northCell[i];
                    float southArea=s==i?0:area[s]/area[i],northArea=n==i?0:area[n]/area[i];
                    float mixSouth=eddy*Math.Min(1,southArea),mixNorth=eddy*Math.Min(1,northArea);
                    weights[i]=new TransportWeights {
                        Center=1-Math.Abs(u[i])-Math.Abs(v[i])-2*eddy-mixSouth-mixNorth,
                        West=Math.Max(0,u[w])+eddy,East=Math.Max(0,-u[e])+eddy,
                        South=southArea*Math.Max(0,v[s])+mixSouth,North=northArea*Math.Max(0,-v[n])+mixNorth
                    };
                    initial+=(q[i]+soil[i])*area[i];
                }
                for(int step=0;step<steps;step++)
                {
                    cancel.ThrowIfCancellationRequested();
                    // Gather fixed outgoing fractions: each transfer has one equal donor
                    // and receiver term. No shared writes or parallel reduction ordering.
                    for(int i=0;i<count;i++)
                    {
                        var t=weights[i];
                        next[i]=q[i]*t.Center+q[westCell[i]]*t.West+q[eastCell[i]]*t.East
                            +q[southCell[i]]*t.South+q[northCell[i]]*t.North;
                    }
                    for(int i=0;i<count;i++)
                    {
                        float humidity=FMath.Clamp(next[i]/cap[i],0,1);
                        // Radiation supplies a wet surface even when the mean
                        // column is humid; the aerodynamic term follows its deficit.
                        float evaporation=pet[i]*(.60f+1.15f*(1-humidity));
                        float openWaterEvap=water[i]*evaporation;
                        float landEvap=Math.Min(soil[i],evaporation*.85f*(1-water[i]));
                        soil[i]-=landEvap;q[i]=next[i]+openWaterEvap+landEvap;external+=openWaterEvap*area[i];
                        // A monthly mean column contains intermittently saturated
                        // parcels, including trade-wind showers outside the ITCZ.
                        // Both cloud coverage and rain survival increase with RH.
                        float cloudFraction=FMath.Clamp((humidity-.40f)*2,0,1);
                        float condensed=Math.Max(0,q[i]-cap[i]*.90f)*.65f+
                            q[i]*Math.Min(.7f,(lift[i]+.055f*cloudFraction*cloudFraction)*dt);
                        float rain=Math.Min(q[i],condensed);q[i]-=rain;
                        float landRain=rain*(1-water[i]);soil[i]+=landRain;
                        float runoff=Math.Max(0,soil[i]-150*(1-water[i]));soil[i]-=runoff;
                        export+=(rain*water[i]+runoff)*area[i];
                        if(step>=steps-measure)
                        {
                            int a=i*12+month;state.Humidity[a]+=Math.Min(1,q[i]/cap[i])/measure;
                            state.Condensation[a]+=rain/(dt*measure);state.Rain[a]+=rain*30/(dt*measure);
                        }
                    }
                }
                double final=0;for(int i=0;i<count;i++)final+=(q[i]+soil[i])*area[i];
                state.BudgetError[month]=Math.Abs(initial+external-export-final)/Math.Max(1,initial+external);
            });
            return state;
        }
    }
}
