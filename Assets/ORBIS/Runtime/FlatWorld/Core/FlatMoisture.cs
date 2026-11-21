using System;
using System.Threading;
using System.Threading.Tasks;
namespace Orbis.Flat
{
    // Monthly normals from a repeating seasonal cycle. mm of precipitable water,
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
        sealed class MonthlyTransport
        {
            public readonly float[] PotentialEvaporation, Capacity, Lift, Efficiency, Transpiration;
            public readonly TransportWeights[] Faces;
            public MonthlyTransport(int count)
            {
                PotentialEvaporation=new float[count]; Capacity=new float[count]; Lift=new float[count];
                Efficiency=new float[count]; Transpiration=new float[count]; Faces=new TransportWeights[count];
            }
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

        static float FractionAt(float[] field,int width,int height,float x,float y)
        {
            y=FMath.Clamp(y,0,height-1);int x0=(int)Math.Floor(x),y0=(int)y;
            int west=FMath.Wrap(x0,width),east=FMath.Wrap(x0+1,width),next=Math.Min(y0+1,height-1);
            return FMath.Lerp(FMath.Lerp(field[y0*width+west],field[y0*width+east],x-x0),
                FMath.Lerp(field[next*width+west],field[next*width+east],x-x0),y-y0);
        }

        // Sample marine source regions and the opposing continent over broad sectors.
        // Distances are in km so the same coast works at every terrain resolution.
        static float[] MonsoonAccess(float[] ocean,float[] elevation,int width,int height,
            out float[] tradeOcean,out float[] easternMargin,out float[] westernMargin)
        {
            var access=new float[ocean.Length];
            tradeOcean=new float[ocean.Length];
            easternMargin=new float[ocean.Length];
            westernMargin=new float[ocean.Length];
            for(int y=0;y<height;y++)
            {
                double latitude=((y+.5)/height-.5)*Math.PI;
                if(Math.Abs(latitude)>Math.PI/3)continue;
                float kmPerRow=20015.115f/height, kmPerColumn=kmPerRow*(float)Math.Cos(latitude);
                float hemisphere=FMath.Clamp((float)Math.Sin(latitude)*8,-1,1);
                for(int x=0;x<width;x++)
                {
                    int i=y*width+x;
                    float upstream=0,polewardLand=0,trades=0,marineFetch=0,inlandFetch=0,outerOcean=0;
                    float westernSea=0,easternLand=0;
                    for(int side=-1;side<=1;side++)
                    {
                        float ridge=elevation[i];
                        float eastStep=700/kmPerColumn;
                        float southStep=(.45f+side*.5f)*hemisphere*500/kmPerRow;
                        for(int band=1;band<=4;band++)
                        {
                            float distance=band*500, dy=hemisphere*distance/kmPerRow;
                            float sx=x+side*distance*.65f/kmPerColumn;
                            upstream+=FractionAt(ocean,width,height,sx,y-dy);
                            polewardLand+=1-FractionAt(ocean,width,height,sx,y+dy*.65f);
                            // Tropical easterlies carry marine air inland. Their
                            // upstream sector is independent of the prime meridian.
                            trades+=FractionAt(ocean,width,height,x+distance*1.5f/kmPerColumn,
                                y+side*distance*.45f/kmPerRow);

                            // Western flank of an oceanic subtropical high: marine air
                            // approaches the continent from the east/equatorward side.
                            // An intervening ridge screens a low basin from that source.
                            float dx=band*eastStep, sy=band*southStep;
                            ridge=Math.Max(ridge,FractionAt(elevation,width,height,x+dx-eastStep*.5f,y-sy+southStep*.5f));
                            ridge=Math.Max(ridge,FractionAt(elevation,width,height,x+dx,y-sy));
                            float shelter=1+(ridge-elevation[i])/900;
                            float sea=FractionAt(ocean,width,height,x+dx,y-sy);
                            marineFetch+=(5-band)*sea/shelter;
                            if(band>=3)outerOcean+=sea;
                            inlandFetch+=1-FractionAt(ocean,width,height,x-dx,y+sy);
                            westernSea+=(5-band)*FractionAt(ocean,width,height,x-dx,y-sy);
                            easternLand+=1-FractionAt(ocean,width,height,x+dx,y+sy);
                        }
                    }
                    access[i]=upstream*polewardLand/144;
                    tradeOcean[i]=trades/12;
                    // The offshore sector must open onto a basin large enough to
                    // support the high. A narrow inland sea still evaporates normally.
                    easternMargin[i]=(marineFetch/30)*(inlandFetch/12)*(outerOcean/6);
                    westernMargin[i]=westernSea/30*(easternLand/12);
                }
            }
            return access;
        }
        public static FlatMoistureState Build(WorldData world,CancellationToken cancel,int workers=0,bool evaporate=true)
        {
            int width=Math.Min(128,world.Width),height=width/2,count=width*height;
            var state=new FlatMoistureState(width,height);var elevation=new float[count];var water=new float[count];var area=new float[count];
            var ocean=new float[count];
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
                        if(FlatTerrainAnalysis.Ocean(world.Water[n]))ocean[i]+=weight;
                        for(int m=0;m<12;m++)monthlyTemperature[i*12+m]+=weight*world.Temperature[n*12+m];
                    }
                }
                water[i]=FMath.Clamp(water[i],0,1);
            }
            var regionalLand=RegionalContinentality(continentality,width,height);
            var marineAccess=MonsoonAccess(ocean,elevation,width,height,out float[] tradeOcean,out float[] easternMargin,out float[] westernMargin);
            // Transpiration falls as the root zone dries. The soil reservoir is
            // stored per whole cell, so scale its stress threshold by land area.
            var inverseFieldWater=new float[count];
            for(int i=0;i<count;i++)inverseFieldWater[i]=1/Math.Max(.01f,45*(1-water[i]));
            // One reference cell is 360/128 degrees. A fixed physical wind or
            // diffusivity travels fewer grid cells on a coarser climate map.
            float gridScale=width/128f,eddy=.065f*gridScale*gridScale;
            const float dt=.25f; // days per transport step
            var annualTemperature=new float[count];
            for(int i=0;i<count;i++)
                for(int m=0;m<12;m++)annualTemperature[i]+=monthlyTemperature[i*12+m]/12;
            var months=new MonthlyTransport[12];
            var options=FlatWorkerPolicy.Options(cancel,workers);
            Parallel.For(0,12,options,month=>
            {
                var column=months[month]=new MonthlyTransport(count);
                var pet=column.PotentialEvaporation;var cap=column.Capacity;
                var pressure=new float[count];var u=new float[count];var v=new float[count];
                var lift=column.Lift;var rainEfficiency=column.Efficiency;var transpiration=column.Transpiration;
                float solstice=(float)Math.Cos((month-6)*Math.PI/6);
                var zonalTemp=new float[height];var weights=column.Faces;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int i=y*width+x;float temp=monthlyTemperature[i*12+month];
                    float lat=((y+.5f)/height-.5f)*180;
                    float tropicalEnergy=(float)Math.Exp(-Math.Pow(lat/18,2));
                    pet[i]=evaporate?Math.Max(.05f,2+temp*.10f)*(1+.26f*tropicalEnergy)*dt:0;
                    // The eastern boundary of a subtropical ocean is cooler
                    // under upwelling. Over land a shallow winter inversion is
                    // colder than the layer carrying frontal moisture overhead.
                    float coastalCurrent=(float)Math.Exp(-Math.Pow((Math.Abs(lat)-23)/14,2))
                        *(westernMargin[i]-.5f*easternMargin[i])*ocean[i];
                    pet[i]*=1-.30f*coastalCurrent;
                    float winterInversion=Math.Min(15,Math.Max(0,annualTemperature[i]-temp))*(1-water[i]);
                    cap[i]=Capacity(temp+.55f*winterInversion-2*coastalCurrent);
                    zonalTemp[y]+=temp+elevation[i]*.0065f;
                }
                for(int y=0;y<height;y++)zonalTemp[y]/=width;
                var convergenceLatitude=new float[count];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    int i=y*width+x;float lat=((y+.5f)/height-.5f)*180;
                    float seaRegion=1-regionalLand[i];
                    float thermal=monthlyTemperature[i*12+month]+elevation[i]*.0065f-zonalTemp[y];
                    float monsoon=marineAccess[i]*(float)Math.Exp(-Math.Pow((Math.Abs(lat)-20)/17,2));
                    float itcz=convergenceLatitude[i]=ConvergenceLatitude(month,seaRegion,thermal)+14*monsoon*solstice;
                    // The solstitial heating contrast reverses continuously
                    // through the equator and grows toward the tropics.
                    float hemisphere=FMath.Clamp((float)Math.Sin(lat*Math.PI/180)*2.5f,-1,1);
                    float seasonalHeat=regionalLand[i]*(float)Math.Cos((month-6)*Math.PI/6)*hemisphere*Math.Max(0,1-Math.Abs(lat)/75);
                    float subtropical=FMath.Lerp(30,28,seaRegion)+Math.Max(0,seasonalHeat)*3;
                    pressure[i]=1.7f*(float)Math.Exp(-Math.Pow((Math.Abs(lat)-subtropical)/12,2))
                        -1.2f*(float)Math.Exp(-Math.Pow((lat-itcz)/15,2))-seasonalHeat*.8f-thermal*.055f;
                }
                for(int y=0;y<height;y++)
                {
                    float lat=((y+.5f)/height-.5f)*180,ab=Math.Abs(lat);
                    float coriolis=(float)Math.Sin(lat*Math.PI/180),belt=(float)Math.Tanh((ab-30)/8)*(float)Math.Tanh((68-ab)/8);
                    float hemi=FMath.Clamp(coriolis*8,-1,1);
                    float summer=solstice*hemi;
                    // The poleward limit of the maritime flow advances in summer.
                    // Cool-season fronts can still lift marine air where the sea-level
                    // temperature decreases poleward. The gradient spans two grid rows.
                    float polewardLimit=34+3.5f*summer;
                    float onshoreBelt=(float)((.5+.5*Math.Tanh((ab-23)/4))*(.5-.5*Math.Tanh((ab-polewardLimit)/3)))*(.9f+.1f*summer);
                    float gradient=hemi*(zonalTemp[Math.Max(0,y-1)]-zonalTemp[Math.Min(height-1,y+1)])*gridScale;
                    // Moist warm sectors move poleward within midlatitude storms.
                    // This is an eddy moisture flux in the column, separate from
                    // the climatological wind used by the surface weather.
                    float stormFlux=.09f*hemi*FMath.Clamp(gradient/6,0,1)*
                        (float)Math.Exp(-Math.Pow((ab-52)/20,4))*(1-.45f*summer)*gridScale;
                    float westBelt=.75f*(float)Math.Exp(-Math.Pow((ab-37)/9,4));
                    float tradeInversion=(float)Math.Exp(-Math.Pow((ab-23)/13,2));
                    float winterTropics=(float)Math.Exp(-Math.Pow((ab-13)/12,2))*Math.Max(0,-summer);
                    float coastalAscent=.5f+.7f*(1-summer)*FMath.Clamp(gradient/6,0,1);
                    float monsoonBelt=(float)Math.Exp(-Math.Pow((ab-20)/17,2));
                    float equatorial=(float)Math.Exp(-Math.Pow(ab/13,2));
                    float descent=(float)Math.Exp(-Math.Pow((ab-24)/14,2));
                    float tropicalFlank=(float)Math.Exp(-Math.Pow((ab-19)/9,2));
                    float fronts=.043f*(float)Math.Exp(-Math.Pow((ab-48-8*summer)/14,2));
                    for(int x=0;x<width;x++)
                    {
                        int i=y*width+x;
                        float gx=(pressure[eastCell[i]]-pressure[westCell[i]])*.5f*gridScale;
                        float gy=(pressure[northCell[i]]-pressure[southCell[i]])*.5f*gridScale;
                        float monsoon=marineAccess[i]*monsoonBelt, onshore=easternMargin[i]*onshoreBelt;
                        // The eastern flank of an oceanic high has equatorward
                        // subsiding air in summer. Winter storms come ashore.
                        float westCoast=westBelt*westernMargin[i];
                        float subsidence=westCoast*Math.Max(0,summer),winterFront=westCoast*Math.Max(0,-summer);
                        float east=belt*.24f-gy*coriolis*.08f+monsoon*summer*.27f-.5f*onshore+.23f*winterFront-.22f*subsidence;
                        float north=gx*coriolis*.08f+monsoon*solstice*.20f+.14f*onshore*hemi*summer+hemi*(.12f*winterFront-.17f*subsidence);
                        float surfaceEast=FMath.Clamp(east-gx*.12f,-.32f,.32f);
                        float surfaceNorth=y==0||y==height-1?0:FMath.Clamp(north-gy*.20f,-.14f,.14f);
                        state.East[i*12+month]=surfaceEast/.32f;state.North[i*12+month]=surfaceNorth/.32f;
                        // Only the boundary-layer share flows directly across isobars.
                        const float boundaryShare=.45f;
                        u[i]=FMath.Clamp(east-gx*.12f*boundaryShare,-.32f,.32f)*gridScale;
                        float advectedNorth=FMath.Clamp(north-gy*.20f*boundaryShare,-.14f,.14f)*gridScale;
                        v[i]=y==0||y==height-1?0:FMath.Clamp(advectedNorth+stormFlux,-.28f,.28f);
                        int upstream=u[i]>=0?westCell[i]:eastCell[i];
                        float rise=Math.Max(0,elevation[i]-elevation[upstream])*Math.Abs(u[i]);
                        upstream=v[i]>=0?southCell[i]:northCell[i];rise+=Math.Max(0,elevation[i]-elevation[upstream])*Math.Abs(v[i]);
                        float wetSeason=monsoon*Math.Max(0,summer);
                        float moistLift=wetSeason+onshore*coastalAscent;
                        float continentalAir=tropicalFlank*(1-tradeOcean[i])*(1-water[i]);
                        float convection=.12f*(float)Math.Exp(-Math.Pow((lat-convergenceLatitude[i])/13,2))+.11f*equatorial+.43f*moistLift;
                        float drySeason=FMath.Clamp(Math.Abs(lat-convergenceLatitude[i])/22,0,1);
                        rainEfficiency[i]=FMath.Clamp(1-descent*(1-water[i])*(1.05f+.48f*drySeason)+3*moistLift
                            +tropicalFlank*tradeOcean[i]*.8f-.65f*continentalAir,.08f,2.6f);
                        lift[i]=rainEfficiency[i]*convection+(.4f+.6f*rainEfficiency[i])*(fronts+Math.Min(.30f,rise/1400));
                        float dryTropics=winterTropics*FMath.Clamp(Math.Abs(lat-convergenceLatitude[i])/23,0,1);
                        float marineInversion=1-Math.Min(.95f,tradeInversion*westernMargin[i]);
                        lift[i]=(lift[i]*(1-.96f*dryTropics)*marineInversion+.28f*winterFront)*(1-.94f*subsidence);
                        rainEfficiency[i]*=(1-.9f*dryTropics)*marineInversion*(1-.96f*subsidence);
                        float warmCanopy=FMath.Clamp((monthlyTemperature[i*12+month]-8)/16,0,1);
                        transpiration[i]=(.85f+warmCanopy*(.98f*equatorial+.35f*wetSeason))*(1-.65f*dryTropics);
                        // Moisture blocked by a rising face stays in the windward column.
                        float climbEast=Math.Max(0,elevation[u[i]>=0?eastCell[i]:westCell[i]]-elevation[i]);
                        float climbNorth=Math.Max(0,elevation[v[i]>=0?northCell[i]:southCell[i]]-elevation[i]);
                        u[i]/=1+climbEast*gridScale/850;
                        v[i]/=1+climbNorth*gridScale/850;
                    }
                }
                // Cache advection and transient exchange at each face once per
                // month. Gather incoming water in units of the receiving area.
                for(int i=0;i<count;i++)
                {
                    int w=westCell[i],e=eastCell[i],s=southCell[i],n=northCell[i];
                    float southArea=s==i?0:area[s]/area[i],northArea=n==i?0:area[n]/area[i];
                    float mixSouth=eddy*Math.Min(1,southArea),mixNorth=eddy*Math.Min(1,northArea);
                    // The same conductance on both sides of a mountain face
                    // limits cross-barrier mixing while conserving water mass.
                    float mixWest=eddy/(1+Math.Abs(elevation[i]-elevation[w])/2200);
                    float mixEast=eddy/(1+Math.Abs(elevation[i]-elevation[e])/2200);
                    mixSouth/=1+Math.Abs(elevation[i]-elevation[s])/2200;
                    mixNorth/=1+Math.Abs(elevation[i]-elevation[n])/2200;
                    weights[i]=new TransportWeights {
                        Center=1-Math.Abs(u[i])-Math.Abs(v[i])-mixWest-mixEast-mixSouth-mixNorth,
                        West=Math.Max(0,u[w])+mixWest,East=Math.Max(0,-u[e])+mixEast,
                        South=southArea*Math.Max(0,v[s])+mixSouth,North=northArea*Math.Max(0,-v[n])+mixNorth
                    };
                }
            });
            IntegrateYear(state,months,dt,water,area,inverseFieldWater,westCell,eastCell,southCell,northCell,options);
            return state;
        }

        static void IntegrateYear(FlatMoistureState state,MonthlyTransport[] months,float dt,
            float[] water,float[] area,float[] inverseFieldWater,int[] west,int[] east,int[] south,int[] north,ParallelOptions options)
        {
            int count=water.Length, steps=(int)(30/dt);
            var q=new float[count];var next=new float[count];var soil=new float[count];
            int bands=Math.Max(1,state.Height/8);
            var incoming=new double[bands];var outgoing=new double[bands];
            // Start empty, with ocean and lake evaporation supplying the cycle.
            // Retain both reservoirs through two spin-up years and the reported
            // third year, including the December-to-January boundary.
            for(int visit=0;visit<36;visit++)
            {
                int month=visit%12;bool record=visit>=24;
                var column=months[month];
                double initial=0,external=0,export=0;
                if(record)for(int i=0;i<count;i++)initial+=(q[i]+soil[i])*area[i];
                Array.Clear(incoming,0,bands);Array.Clear(outgoing,0,bands);
                // Keep the reduction order fixed across worker counts. Readers
                // see the completed previous step before any column advances.
                void Advance(int band)
                {
                    double added=0,removed=0;
                    int start=(band*state.Height/bands)*state.Width, end=((band+1)*state.Height/bands)*state.Width;
                    for(int i=start;i<end;i++)
                    {
                        var t=column.Faces[i];
                        float air=q[i]*t.Center+q[west[i]]*t.West+q[east[i]]*t.East
                            +q[south[i]]*t.South+q[north[i]]*t.North;
                        float cap=column.Capacity[i], sea=water[i], land=1-sea;
                        float humidity=FMath.Clamp(air/cap,0,1);
                        float evaporation=column.PotentialEvaporation[i]*(.60f+1.15f*(1-humidity));
                        float openWaterEvap=sea*evaporation;
                        float landEvap=Math.Min(soil[i],evaporation*column.Transpiration[i]*land*Math.Min(1,soil[i]*inverseFieldWater[i]));
                        air+=openWaterEvap+landEvap;
                        float cloudFraction=FMath.Clamp((humidity-.40f)*2,0,1);
                        float condensed=Math.Max(0,air-cap*.90f)*.65f+
                            air*Math.Min(.7f,(column.Lift[i]+column.Efficiency[i]*.055f*cloudFraction*cloudFraction)*dt);
                        float rain=Math.Min(air,condensed);
                        next[i]=air-rain;
                        float stored=soil[i]-landEvap+rain*land;
                        float runoff=Math.Max(0,stored-150*land);
                        soil[i]=stored-runoff;
                        if(record)
                        {
                            added+=openWaterEvap*area[i];removed+=(rain*sea+runoff)*area[i];
                            int a=i*12+month;
                            state.Humidity[a]+=Math.Min(1,next[i]/cap)/steps;
                            state.Rain[a]+=rain;
                        }
                    }
                    incoming[band]+=added;outgoing[band]+=removed;
                }
                Action<int> advance=Advance;
                for(int step=0;step<steps;step++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    if(options.MaxDegreeOfParallelism==1 || bands==1)
                        for(int band=0;band<bands;band++)advance(band);
                    else Parallel.For(0,bands,options,advance);
                    var swap=q;q=next;next=swap;
                }
                if(!record)continue;
                for(int band=0;band<bands;band++){external+=incoming[band];export+=outgoing[band];}
                double final=0;
                for(int i=0;i<count;i++)
                {
                    final+=(q[i]+soil[i])*area[i];
                    state.Condensation[i*12+month]=state.Rain[i*12+month]/30;
                }
                state.BudgetError[month]=Math.Abs(initial+external-export-final)/Math.Max(1,initial+external);
            }
        }
    }
}
