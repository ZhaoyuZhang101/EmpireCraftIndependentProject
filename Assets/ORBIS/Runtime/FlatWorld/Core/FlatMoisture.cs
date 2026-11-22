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
            public readonly float[] PotentialEvaporation, WindExchange, SeaHumidityContrast, Capacity, Lift, Efficiency, Transpiration;
            public readonly TransportWeights[] Faces;
            public MonthlyTransport(int count)
            {
                PotentialEvaporation=new float[count]; WindExchange=new float[count]; SeaHumidityContrast=new float[count]; Capacity=new float[count]; Lift=new float[count];
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
        // Latitude of the seasonal heat low, with any northward thermal
        // displacement already signed by the caller (degrees C equivalent).
        public static float ConvergenceLatitude(float month,float seaFraction,float thermalAnomaly)
        {
            float season=(float)Math.Cos((month-6)*Math.PI/6);
            float excursion=FMath.Lerp(32.5f,4f,FMath.Clamp(seaFraction,0,1));
            return FMath.Clamp(season*excursion+FMath.Clamp(thermalAnomaly,-5,5)*1.2f,-38,38);
        }

        // A warm column attracts the pressure trough towards its latitude.
        // Taper across the width of the trough so heating on its axis cannot
        // move the axis, and reflected continents have reflected responses.
        internal static float LocalConvergence(float latitude,float axis,float thermalAnomaly)
        {
            float flank=FMath.Clamp((latitude-axis)/13,-1,1);
            return axis+1.2f*FMath.Clamp(thermalAnomaly,-5,5)*flank;
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

        struct MarineSectors
        {
            public float Monsoon,TradeOcean,EasternMargin,WesternMargin,ShieldedInflow,BarrierJet,PolewardCorridor,WesterlyShelter;
            public float SummerMarineFetch;
        }

        // A long meridional range can turn low-level inflow poleward along its
        // eastern slopes. A second range across the tropical approach blocks it.
        static float PolewardCorridor(float[] bed,int width,int height,int x,int y,float kmX,float kmY,float hemisphere)
        {
            float local=bed[y*width+x],barrier=0;
            for(int band=1;band<=5;band++)
            {
                float west=x-band*300/kmX,dy=350/kmY;
                float crest=FractionAt(bed,width,height,west,y);
                float ends=FractionAt(bed,width,height,west,y+dy)+FractionAt(bed,width,height,west,y-dy);
                float rise=.5f*crest+.25f*ends-local;
                barrier=Math.Max(barrier,FMath.Clamp((rise-350)/1550,0,1)/(1+.22f*(band-1)));
            }
            float upstreamRise=0;
            for(int k=1;k<=3;k++)
                upstreamRise=Math.Max(upstreamRise,FractionAt(bed,width,height,x,y-hemisphere*k*320/kmY)-local);
            return barrier*(float)Math.Exp(-upstreamRise/900);
        }

        // Sample marine source regions and the opposing continent over broad sectors.
        // Distances are in km so the same coast works at every terrain resolution.
        static MarineSectors[] MonsoonAccess(float[] ocean,float[] elevation,float[] ridgeHeight,int width,int height)
        {
            var sectors=new MarineSectors[ocean.Length];
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
                    float westernSea=0,easternLand=0,blockedFront=0;
                    float sourceSea=0,barrierAcrossFlow=0;
                    float baySea=0,nearRange=0,marineFetchAlongCoast=0;
                    for(int side=-1;side<=1;side++)
                    {
                        float ridge=elevation[i],westernRidge=elevation[i];
                        float downstreamBarrier=0;
                        float foothillBarrier=0;
                        float eastStep=700/kmPerColumn;
                        float southStep=(.45f+side*.5f)*hemisphere*500/kmPerRow;
                        float jetStepX=(.5f+side*.6f)*700/kmPerColumn,jetStepY=hemisphere*700/kmPerRow;
                        float lowland=Math.Min(elevation[i],FractionAt(elevation,width,height,x-jetStepX*.5f,y-jetStepY*.5f));
                        float bayStepX=(.65f+side*.55f)*350/kmPerColumn, bayStepY=hemisphere*350/kmPerRow;
                        float bayStepKm=350*(float)Math.Sqrt(1+Math.Pow(.65f+side*.55f,2));
                        float fetchCrest=0, overlandKm=0, previousLand=1-ocean[i];
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
                            westernRidge=Math.Max(westernRidge,FractionAt(elevation,width,height,x-dx+eastStep*.5f,y-sy+southStep*.5f));
                            westernRidge=Math.Max(westernRidge,FractionAt(elevation,width,height,x-dx,y-sy));
                            westernSea+=(5-band)*FractionAt(ocean,width,height,x-dx,y-sy)/(1+(westernRidge-elevation[i])/900);
                            easternLand+=1-FractionAt(ocean,width,height,x+dx,y+sy);

                            // A poleward range insulates the summer lowlands from
                            // cool extratropical air. Inflow from the warm ocean
                            // turns east as it travels poleward in either hemisphere.
                            float alongX=band*jetStepX,alongY=band*jetStepY;
                            sourceSea+=FractionAt(ocean,width,height,x-alongX,y-alongY);
                            downstreamBarrier=Math.Max(downstreamBarrier,FractionAt(elevation,width,height,x+alongX,y+alongY)-lowland);
                            float nearKm=350*band;
                            float bayX=x+band*bayStepX,bayY=y-band*bayStepY;
                            float marine=FractionAt(ocean,width,height,bayX,bayY);
                            baySea+=(5-band)*marine;
                            // A warm-season boundary layer can feed the poleward coast
                            // beyond the subtropical rain belt. Ridges above sea level
                            // intercept that layer; moisture also decays over dry land.
                            fetchCrest=Math.Max(fetchCrest,FractionAt(ridgeHeight,width,height,bayX-.5f*bayStepX,bayY+.5f*bayStepY));
                            overlandKm+=.5f*(previousLand+1-marine)*bayStepKm;
                            previousLand=1-marine;
                            marineFetchAlongCoast+=(5-band)*marine*(float)Math.Exp(-fetchCrest/900-overlandKm/650);
                            float rangeRise=FractionAt(elevation,width,height,x+side*.5f*nearKm/kmPerColumn,y+hemisphere*nearKm/kmPerRow)-lowland;
                            // A shallow blocked current stays within a few
                            // hundred kilometres of the windward foothills.
                            float blocking=FMath.Clamp((rangeRise-800)/1800,0,1)/(1+.6f*(band-1));
                            foothillBarrier=Math.Max(foothillBarrier,blocking);
                        }
                        // Air can spill around a low divide. Count the fraction
                        // of each sector's lower-tropospheric layer intercepted
                        // above a typical 1.5 km mixed layer.
                        barrierAcrossFlow+=FMath.Clamp((downstreamBarrier-1500)/2000,0,1);
                        blockedFront+=1-(float)Math.Exp(-(westernRidge-elevation[i])/1200);
                        nearRange+=foothillBarrier;
                    }
                    sectors[i].Monsoon=upstream*polewardLand/144;
                    sectors[i].PolewardCorridor=PolewardCorridor(elevation,width,height,x,y,kmPerColumn,kmPerRow,hemisphere);
                    sectors[i].TradeOcean=trades/12;
                    // The offshore sector must open onto a basin large enough to
                    // support the high. A narrow inland sea still evaporates normally.
                    sectors[i].EasternMargin=(marineFetch/30)*(inlandFetch/12)*(outerOcean/6);
                    sectors[i].SummerMarineFetch=(marineFetchAlongCoast/30)*(polewardLand/12)*(outerOcean/6);
                    sectors[i].WesternMargin=westernSea/30*(easternLand/12);
                    sectors[i].WesterlyShelter=blockedFront/3;
                    float insulation=barrierAcrossFlow/3;
                    sectors[i].ShieldedInflow=sourceSea/12*insulation*insulation;
                    // Moist air piling against the equatorward face of a range
                    // feeds a barrier-parallel easterly from an adjacent bay.
                    sectors[i].BarrierJet=baySea/30*(nearRange/3)*insulation;
                }
            }
            return sectors;
        }
        internal struct ThermalPressureGradient
        {
            public float East, North, SummerEast, SummerNorth;
        }

        // Sample continental heating at 375–1500 km. Land warms more than the
        // sea each day. Its seasonal contribution scales with the fundamental
        // of the simulated temperature cycle (a 7 K amplitude gives full strength).
        internal static ThermalPressureGradient[] RegionalHeatLow(float[] water,float[] temperature,
            float[] annualTemperature,int width,int height)
        {
            int count=width*height;
            var gradients=new ThermalPressureGradient[count];
            var seasonalHeating=new float[count];
            var cosine=new float[12];var sine=new float[12];
            for(int m=0;m<12;m++)
            {
                double phase=(m-6)*Math.PI/6;
                cosine[m]=(float)Math.Cos(phase);sine[m]=(float)Math.Sin(phase);
            }
            for(int i=0;i<count;i++)
            {
                float c=0,s=0;
                for(int m=0;m<12;m++)
                {
                    float anomaly=temperature[i*12+m]-annualTemperature[i];
                    c+=anomaly*cosine[m]/6;s+=anomaly*sine[m]/6;
                }
                seasonalHeating[i]=(1-water[i])*Math.Min(1,(float)Math.Sqrt(c*c+s*s)/7);
            }
            float kmRow=20015.1f/height;
            for(int y=0;y<height;y++)
            {
                float latitude=((y+.5f)/height-.5f)*180;
                if(Math.Abs(latitude)>45)continue;
                float kmColumn=kmRow*(float)Math.Cos(latitude*Math.PI/180);
                for(int x=0;x<width;x++)
                {
                    var gradient=new ThermalPressureGradient();
                    for(int direction=0;direction<8;direction++)
                    {
                        float east=(float)Math.Cos(direction*Math.PI/4);
                        float north=(float)Math.Sin(direction*Math.PI/4);
                        for(int ring=1;ring<=4;ring++)
                        {
                            float px=x+east*(375*ring)/kmColumn,py=y+north*(375*ring)/kmRow;
                            float land=1-FractionAt(water,width,height,px,py);
                            float sourceLatitude=latitude+north*(375*ring)/kmRow*180/height;
                            float summer=FractionAt(seasonalHeating,width,height,px,py)
                                *FMath.Clamp(sourceLatitude/12,-1,1);
                            gradient.East+=land*east/16;gradient.North+=land*north/16;
                            gradient.SummerEast+=summer*east/16;gradient.SummerNorth+=summer*north/16;
                        }
                    }
                    gradients[y*width+x]=gradient;
                }
            }
            return gradients;
        }

        // Turbulent evaporation depends on speed even when eddies cancel in
        // the monthly mean wind. A reference-grid step per six hours is 14.5 m/s.
        // Add a 4 m/s background RMS wind and the variance of transient fronts.
        internal static float OpenWaterExchange(float eastStep,float northStep,float frontalActivity)
        {
            float meanSquare=14.5f*14.5f*(eastStep*eastStep+northStep*northStep);
            return (float)Math.Sqrt(16+16*frontalActivity+meanSquare)/6;
        }

        public static FlatMoistureState Build(WorldData world,CancellationToken cancel,int workers=0,bool evaporate=true)
            => BuildWithCirculation(world,cancel,workers,evaporate,
                FlatOceanCirculation.Build(world,FlatWorkerPolicy.Options(cancel,workers)));

        internal static FlatMoistureState BuildWithCirculation(WorldData world,CancellationToken cancel,int workers,
            bool evaporate,FlatOceanCirculation currents,bool seasonalWinds=true,int maxColumns=128)
        {
            int width=Math.Min(maxColumns,world.Width),height=width/2,count=width*height;
            var state=new FlatMoistureState(width,height);var elevation=new float[count];var water=new float[count];var area=new float[count];
            var ocean=new float[count];
            var ridgeHeight=new float[count];
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
                        float surface=FlatClimate.SurfaceHeight(world,n);
                        elevation[i]+=weight*surface;
                        ridgeHeight[i]+=weight*surface*surface;
                        if(FlatTerrainAnalysis.AreaWater(world.Water[n]))water[i]+=weight;
                        else continentality[i]+=weight*Continentality(world.CoastDistance[n],world.Width);
                        if(FlatTerrainAnalysis.Ocean(world.Water[n]))ocean[i]+=weight;
                        for(int m=0;m<12;m++)monthlyTemperature[i*12+m]+=weight*world.Temperature[n*12+m];
                    }
                }
                water[i]=FMath.Clamp(water[i],0,1);
                // Mean plus one within-cell standard deviation represents the
                // upper slopes of a partly flooded or unresolved coastal ridge.
                ridgeHeight[i]=elevation[i]+(float)Math.Sqrt(Math.Max(0,ridgeHeight[i]-elevation[i]*elevation[i]));
            }
            var regionalLand=RegionalContinentality(continentality,width,height);
            var sectors=MonsoonAccess(ocean,elevation,ridgeHeight,width,height);
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
            var heatLow=RegionalHeatLow(water,monthlyTemperature,annualTemperature,width,height);
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
                        *(sectors[i].WesternMargin-.5f*sectors[i].EasternMargin)*ocean[i];
                    pet[i]*=1-.30f*coastalCurrent;
                    if(currents!=null && ocean[i]>0)
                    {
                        // Wind-driven surface currents change the saturation humidity
                        // at the sea skin. Air temperature already includes the marine
                        // heat carried inland, so only its difference from SST enters here.
                        float skinMinusAir=currents.SeaAnomaly[i]-FlatClimate.MarineAirCoupling*currents.AirAnomaly[i];
                        float seaShare=ocean[i]/Math.Max(.0001f,water[i]);
                        column.SeaHumidityContrast[i]=1.15f*seaShare*(Capacity(temp+skinMinusAir)/Capacity(temp)-1);
                    }
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
                    float monsoon=sectors[i].Monsoon*(float)Math.Exp(-Math.Pow((Math.Abs(lat)-20)/17,2));
                    float axis=ConvergenceLatitude(month,seaRegion,0)+14*monsoon*solstice;
                    float itcz=convergenceLatitude[i]=LocalConvergence(lat,axis,thermal);
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
                    float coriolis=(float)Math.Sin(lat*Math.PI/180);
                    var winds=seasonalWinds?new FlatWindBelts(lat,solstice):default;
                    float referenceEast=seasonalWinds?0:(float)Math.Tanh((ab-30)/8)*(float)Math.Tanh((68-ab)/8)*.24f;
                    float hemi=FMath.Clamp(coriolis*8,-1,1);
                    float summer=solstice*hemi;
                    float spring=-(float)Math.Sin((month-6)*Math.PI/6)*hemi;
                    // The poleward limit of the maritime flow advances in summer.
                    // Cool-season fronts can still lift marine air where the sea-level
                    // temperature decreases poleward. The gradient spans two grid rows.
                    float polewardLimit=34+3.5f*summer;
                    float onshoreBelt=(float)((.5+.5*Math.Tanh((ab-23)/4))*(.5-.5*Math.Tanh((ab-polewardLimit)/3)))*(.9f+.1f*summer);
                    // The rain front advances poleward as the continental cold
                    // air retreats. The return of offshore flow in autumn
                    // curtails the marine supply on an eastern seaboard.
                    float heating=Math.Max(0,summer);
                    float frontLatitude=24+21*heating*heating*heating;
                    float advancingFront=.55f*FMath.Clamp(spring+summer,0,1)
                        *(float)Math.Exp(-Math.Pow((ab-frontLatitude)/7,2));
                    float coolingOutflow=Math.Max(0,-spring)*(float)Math.Exp(-Math.Pow((ab-43)/13,2));
                    onshoreBelt*=1-.55f*Math.Max(0,-spring);
                    // The adjacent sea retains summer heat into late summer. Extend
                    // only the part of the midlatitude inflow outside the existing belt.
                    float warmSea=Math.Max(0,.924f*summer-.383f*spring);
                    float coastalSeason=.8f*heating*warmSea*(1-onshoreBelt)
                        *FMath.Clamp((ab-30)/8,0,1)*FMath.Clamp((58-ab)/12,0,1);
                    float gradient=hemi*(zonalTemp[Math.Max(0,y-1)]-zonalTemp[Math.Min(height-1,y+1)])*gridScale;
                    // Moist warm sectors move poleward within midlatitude storms.
                    // This is an eddy moisture flux in the column, separate from
                    // the climatological wind used by the surface weather.
                    // The direct tropical response weakens beyond the subtropical ridge.
                    // A one-day friction time gives f*tau = 12.60*sin(latitude).
                    // .20 reference cells per step corresponds to about 2.9 m/s.
                    float rotation=12.601f*coriolis;
                    float thermalResponse=.20f*(float)Math.Exp(-Math.Pow(ab/32,4))*gridScale/(1+rotation*rotation);
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
                    // Transient fronts lift moist air even when the monthly mean
                    // surface wind is small. Their activity follows baroclinicity.
                    float stormLift=.10f*(float)Math.Exp(-Math.Pow((ab-54)/21,4))*
                        (1-.65f*summer)*(.5f+.5f*FMath.Clamp(gradient/6,0,1));
                    float corridorBelt=(float)Math.Exp(-Math.Pow((ab-23)/22,4))*
                        FMath.Clamp((ab-6)/16,0,1)*(.70f+.30f*summer);
                    for(int x=0;x<width;x++)
                    {
                        int i=y*width+x;
                        float gx=(pressure[eastCell[i]]-pressure[westCell[i]])*.5f*gridScale;
                        if(seasonalWinds)gx/=Math.Max(.1f,area[i]);
                        float gy=(pressure[northCell[i]]-pressure[southCell[i]])*.5f*gridScale;
                        var sector=sectors[i];
                        float monsoon=sector.Monsoon*monsoonBelt, onshore=sector.EasternMargin*onshoreBelt;
                        float marineInflow=sector.SummerMarineFetch*coastalSeason;
                        float coastFront=sector.EasternMargin*advancingFront;
                        float offshore=sector.EasternMargin*coolingOutflow;
                        // The eastern flank of an oceanic high has equatorward
                        // subsiding air in summer. Winter storms come ashore.
                        float westCoast=westBelt*sector.WesternMargin;
                        float subsidence=westCoast*Math.Max(0,summer),winterFront=westCoast*Math.Max(0,-summer);
                        float meanEast=referenceEast,returnFlow=0;
                        if(seasonalWinds)winds.Surface(convergenceLatitude[i],1-regionalLand[i],out meanEast,out returnFlow);
                        float east=meanEast-gy*coriolis*.08f+monsoon*summer*.27f-.5f*onshore+.23f*winterFront-.22f*subsidence;
                        float north=gx*coriolis*.08f+monsoon*solstice*.20f+.14f*onshore*hemi*summer+hemi*(.12f*winterFront-.17f*subsidence);
                        float summerJet=sector.ShieldedInflow*monsoonBelt*heating;
                        east+=.48f*summerJet-.85f*sector.BarrierJet*monsoonBelt*heating-.42f*coastFront+.12f*offshore;
                        north+=hemi*(.15f*summerJet+.18f*coastFront-.15f*offshore);
                        east-=.18f*marineInflow;
                        north+=hemi*.24f*marineInflow;
                        float surfaceEast=FMath.Clamp(east-gx*.12f,-.32f,.32f);
                        float surfaceNorth=y==0||y==height-1?0:FMath.Clamp(north+returnFlow-gy*.20f,-.14f,.14f);
                        column.WindExchange[i]=OpenWaterExchange(surfaceEast,surfaceNorth,stormLift/.10f);
                        state.East[i*12+month]=surfaceEast/.32f;state.North[i*12+month]=surfaceNorth/.32f;
                        // Only the boundary-layer share flows directly across isobars.
                        const float boundaryShare=.45f;
                        // The low-level jet carries moisture faster than the
                        // frictional surface current. Leave a nonnegative share
                        // of each donor column for diffusion and local storage.
                        float jetSpeed=1+.55f*summerJet;
                        u[i]=FMath.Clamp(east-gx*.12f*boundaryShare,-.32f,.32f)*gridScale*jetSpeed;
                        // Most of the surface overturning has an opposing flow
                        // aloft. More of the residual vapour flux is carried by
                        // the moist marine boundary layer than by inland air.
                        float columnReturn=returnFlow*(.05f+.15f*ocean[i]);
                        float advectedNorth=FMath.Clamp(north+columnReturn-gy*.20f*boundaryShare,-.14f,.14f)*gridScale*jetSpeed;
                        // The mixed-layer flow towards heated land turns to its
                        // right in the north and to its left in the south.
                        // Daily heating supplies a weaker year-round component.
                        var heatGradient=heatLow[i];
                        float towardsEast=.25f*heatGradient.East+solstice*heatGradient.SummerEast;
                        float towardsNorth=.25f*heatGradient.North+solstice*heatGradient.SummerNorth;
                        u[i]+=thermalResponse*(towardsEast+rotation*towardsNorth);
                        advectedNorth+=thermalResponse*(towardsNorth-rotation*towardsEast);
                        float meridionalLimit=seasonalWinds?.28f:Math.Min(.28f,1-4*eddy-Math.Abs(u[i]));
                        v[i]=y==0||y==height-1?0:FMath.Clamp(advectedNorth+stormFlux,-meridionalLimit,meridionalLimit);
                        if(seasonalWinds)
                        {
                            // A longitude cell narrows towards the poles. Limit
                            // its Courant number to the available donor mass.
                            float maxZonal=1-4*eddy-Math.Abs(v[i]);
                            u[i]=FMath.Clamp(u[i]/area[i],-maxZonal,maxZonal);
                        }
                        float wetSeason=monsoon*Math.Max(0,summer);
                        float corridor=sector.PolewardCorridor*corridorBelt
                            *FMath.Clamp((monthlyTemperature[i*12+month]-2)/16,0,1)
                            *(1-FMath.Clamp(2*sector.EasternMargin+wetSeason+summerJet,0,1));
                        // The barrier jet shares the moist layer with maritime and
                        // monsoon inflows. Redirect its flux before the slope test.
                        u[i]*=1-.50f*corridor;
                        float available=Math.Max(0,Math.Min(.38f,1-4*eddy-Math.Abs(u[i])));
                        v[i]=FMath.Clamp(v[i]+hemi*corridor*.24f*gridScale,-available,available);
                        int upX=u[i]>=0?westCell[i]:eastCell[i],upY=v[i]>=0?southCell[i]:northCell[i];
                        float rise=Math.Max(0,elevation[i]-elevation[upX])*Math.Abs(u[i])
                            +Math.Max(0,elevation[i]-elevation[upY])*Math.Abs(v[i]);
                        float moistLift=wetSeason+(onshore+marineInflow)*coastalAscent+.65f*coastFront;
                        float continentalAir=tropicalFlank*(1-sector.TradeOcean)*(1-water[i]);
                        float convection=.12f*(float)Math.Exp(-Math.Pow((lat-convergenceLatitude[i])/13,2))+.11f*equatorial+.43f*moistLift;
                        float drySeason=FMath.Clamp(Math.Abs(lat-convergenceLatitude[i])/22,0,1);
                        rainEfficiency[i]=FMath.Clamp(1-descent*(1-water[i])*(1.05f+.48f*drySeason)+3*moistLift
                            +tropicalFlank*sector.TradeOcean*.8f-.65f*continentalAir,.08f,2.6f);
                        lift[i]=rainEfficiency[i]*convection+(.4f+.6f*rainEfficiency[i])*(fronts+Math.Min(.30f,rise/1400));
                        float dryTropics=winterTropics*FMath.Clamp(Math.Abs(lat-convergenceLatitude[i])/23,0,1);
                        float marineInversion=1-Math.Min(.95f,tradeInversion*sector.WesternMargin);
                        lift[i]=(lift[i]*(1-.96f*dryTropics)*marineInversion+.28f*winterFront)*(1-.94f*subsidence);
                        rainEfficiency[i]*=(1-.9f*dryTropics)*marineInversion*(1-.96f*subsidence);
                        float warmCanopy=FMath.Clamp((monthlyTemperature[i*12+month]-8)/16,0,1);
                        transpiration[i]=.8f*(.85f+warmCanopy*(.98f*equatorial+.35f*wetSeason))*(1-.65f*dryTropics);
                        // Moisture blocked by a rising face stays in the windward column.
                        float climbEast=Math.Max(0,elevation[u[i]>=0?eastCell[i]:westCell[i]]-elevation[i]);
                        float climbNorth=Math.Max(0,elevation[v[i]>=0?northCell[i]:southCell[i]]-elevation[i]);
                        // A fast marine current transports vapour across the
                        // ocean. Its moist layer ascends on the approach to a
                        // ridge as well as on the windward slope itself.
                        if(summerJet>0)
                        {
                            int downX=u[i]>=0?eastCell[i]:westCell[i],downY=v[i]>=0?northCell[i]:southCell[i];
                            float riseX=Math.Max(0,ridgeHeight[i]-ridgeHeight[upX])+Math.Max(0,ridgeHeight[downX]-ridgeHeight[i]);
                            float riseY=Math.Max(0,ridgeHeight[i]-ridgeHeight[upY])+Math.Max(0,ridgeHeight[downY]-ridgeHeight[i]);
                            float ascent=riseX*Math.Abs(u[i])+riseY*Math.Abs(v[i]);
                            lift[i]=lift[i]*(1-summerJet*ocean[i])+summerJet*Math.Min(3,ascent/(dt*650));
                            pet[i]*=1+.5f*summerJet*ocean[i];
                            // Descent behind an upstream divide warms and dries
                            // the boundary layer. Search along the actual wind
                            // over a roughly one-day marine trajectory.
                            float kmRow=20015.115f/height, kmCol=kmRow*area[i];
                            float dx=u[i]*kmCol,dy=v[i]*kmRow;
                            float length=(float)Math.Sqrt(dx*dx+dy*dy);
                            if(length>0)
                            {
                                dx*=300/(kmCol*length); dy*=300/(kmRow*length);
                                float lee=0;
                                for(int steps=1;steps<=3;steps++)
                                    lee=Math.Max(lee,(FractionAt(ridgeHeight,width,height,x-steps*dx,y-steps*dy)-elevation[i])/(1+.5f*(steps-1)));
                                float drying=(float)Math.Exp(-summerJet*lee/350);
                                lift[i]*=drying;
                                rainEfficiency[i]*=drying;
                            }
                        }
                        float frontalLift=stormLift*(1-sector.WesterlyShelter);
                        lift[i]+=.24f*corridor+.60f*frontalLift;
                        float coastalRise=Math.Max(0,ridgeHeight[i]-ridgeHeight[upX])*Math.Abs(u[i])
                            +Math.Max(0,ridgeHeight[i]-ridgeHeight[upY])*Math.Abs(v[i]);
                        lift[i]+=.25f*marineInflow*Math.Min(2,coastalRise/(dt*220));
                        // The remaining frontal cloud condenses in proportion to
                        // the humid cloud fraction squared in IntegrateYear.
                        rainEfficiency[i]+=.8f*corridor+.40f*frontalLift/.055f;
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
                        float openWaterEvap=sea*Math.Max(0,evaporation+column.PotentialEvaporation[i]*column.SeaHumidityContrast[i]);
                        openWaterEvap*=column.WindExchange[i];
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
