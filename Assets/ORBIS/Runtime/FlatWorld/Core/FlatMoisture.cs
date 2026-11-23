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
        public GlebaClimateSupplement Gleba { get; internal set; }
        public ExoPlaSimSupplement ExoPlaSim { get; internal set; }
        internal FlatOceanCirculation OceanCurrents { get; set; }
        public FlatMoistureState(int width,int height)
        {Width=width;Height=height;int n=width*height*12;Humidity=new float[n];Condensation=new float[n];East=new float[n];North=new float[n];Rain=new float[n];}
        public FlatMoistureState Clone()
        {
            var s=new FlatMoistureState(Width,Height){Gleba=Gleba,ExoPlaSim=ExoPlaSim,OceanCurrents=OceanCurrents};Array.Copy(Humidity,s.Humidity,Humidity.Length);Array.Copy(Condensation,s.Condensation,Humidity.Length);
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
        internal delegate void MonthlyFluxObserver(int month,int width,int height,double[] east,double[] north);

        internal struct TransportWeights
        {
            public float Center,West,East,South,North;
        }
        sealed class MonthlyTransport
        {
            public readonly float[] PotentialEvaporation, WindExchange, SeaHumidityContrast, Capacity, Lift, Efficiency, Transpiration, DryLayer;
            public readonly TransportWeights[] Faces;
            public readonly TransportWeights[] DisturbanceFaces;
            public readonly float[] OrganizedFraction, Release, Rainout;
            public readonly float[] ConvectiveShare, StratiformCritRh, SolidSnowFrac, SeaBreezeResilience;
            public readonly float[] LandThermalActivity;
            public MonthlyTransport(int count)
            {
                PotentialEvaporation=new float[count]; WindExchange=new float[count]; SeaHumidityContrast=new float[count]; Capacity=new float[count]; Lift=new float[count];
                Efficiency=new float[count]; Transpiration=new float[count]; DryLayer=new float[count]; Faces=new TransportWeights[count];
                DisturbanceFaces=new TransportWeights[count]; OrganizedFraction=new float[count]; Release=new float[count]; Rainout=new float[count];
                ConvectiveShare=new float[count]; StratiformCritRh=new float[count]; SolidSnowFrac=new float[count]; SeaBreezeResilience=new float[count];
                LandThermalActivity=new float[count];
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
        static void ComputeContinentalCorridors(int width,int height,float[] ocean,float[] landMeanElev,float[] landCrest,
            MarineSectors[] sectors,GlebaClimateSupplement gleba,
            out float[] gulfCorridor,out float[] saczMonsoon,out float[] isolatedSubtropLee,
            out float[] subtropHighPlateau,out float[] interiorSteppe,out float[] orographicTower)
        {
            int count=width*height;
            gulfCorridor=new float[count];
            saczMonsoon=new float[count];
            isolatedSubtropLee=new float[count];
            subtropHighPlateau=new float[count];
            interiorSteppe=new float[count];
            orographicTower=new float[count];
            float kmRow=20015.115f/height;
            for(int y=0;y<height;y++)
            {
                float lat=((y+.5f)/height-.5f)*180f,ab=Math.Abs(lat);
                if(ab<4f || ab>57f)continue;
                float hemi=lat>=0f?1f:-1f;
                float kmCol=kmRow*Math.Max(.18f,(float)Math.Cos(lat*Math.PI/180.0));
                for(int x=0;x<width;x++)
                {
                    int i=y*width+x;
                    float distKm=gleba!=null?gleba.DistanceToSeaKm[i]:600f;
                    float wCordillera=0f;
                    for(int k=2;k<=9;k++)
                    {
                        float wx=x-(k*300f)/kmCol;
                        wCordillera=Math.Max(wCordillera,FractionAt(landCrest,width,height,wx,y));
                    }
                    float cordilleraGate=ExoPlaSimSupplement.SmoothStep(1050f,1850f,wCordillera);

                    // 1. Unblocked Subtropical-Gulf Extratropical Corridor (e.g. Gulf of Mexico -> Great Plains / Midwest / US South):
                    //    Western cordillera lee cyclogenesis + unobstructed lowland (<720 m crest) path to a subtropical gulf (21..29.8 deg)
                    //    bounded on its western shore by a coastal cordillera (>1250 m, e.g. Sierra Madre channeling return flow poleward).
                    if(cordilleraGate>0f && ab>=21f && ab<=51f)
                    {
                        float lowReceiver=ocean[i]>.5f?1f:ExoPlaSimSupplement.SmoothStep(1050f,500f,landMeanElev[i]);
                        if(lowReceiver>0f)
                        {
                            float bestSea=0f;
                            for(int ray=-1;ray<=1;ray++)
                            {
                                float skew=.15f*ray;
                                float maxBarrier=ocean[i]>.5f?0f:Math.Max(0f,landMeanElev[i]);
                                for(int step=1;step<=8;step++)
                                {
                                    float stepKm=290f*step;
                                    float sx=x+(skew*stepKm)/kmCol;
                                    float sy=y-hemi*stepKm/kmRow;
                                    float srcLat=Math.Abs(((FMath.Clamp(sy,0f,height-1f)+.5f)/height-.5f)*180f);
                                    float seaHere=FractionAt(ocean,width,height,sx,sy);
                                    if(seaHere<.50f)
                                        maxBarrier=Math.Max(maxBarrier,FractionAt(landCrest,width,height,sx,sy));
                                    if(srcLat>=21.0f && srcLat<=29.8f && seaHere>.55f)
                                    {
                                        float srcCol=kmRow*Math.Max(.25f,(float)Math.Cos(srcLat*Math.PI/180.0));
                                        float westWall=0f;
                                        for(int wk=1;wk<=6;wk++)
                                            westWall=Math.Max(westWall,FractionAt(landCrest,width,height,sx-(wk*275f)/srcCol,sy));
                                        float wallChannel=ExoPlaSimSupplement.SmoothStep(1150f,1650f,westWall);
                                        float unblocked=ExoPlaSimSupplement.SmoothStep(740f,360f,maxBarrier);
                                        float decay=1f/(1f+.07f*(step-1));
                                        bestSea=Math.Max(bestSea,seaHere*wallChannel*unblocked*decay);
                                    }
                                }
                            }
                            gulfCorridor[i]=cordilleraGate*lowReceiver*FMath.Clamp(bestSea*1.32f,0f,1f)
                                *ExoPlaSimSupplement.SmoothStep(20.5f,26.0f,ab)*ExoPlaSimSupplement.SmoothStep(51.5f,45.0f,ab);
                        }
                    }

                    // 2. Tropical-Equatorial Cordillera Monsoon / Convergence Zone (e.g. South American Monsoon / SACZ):
                    if(cordilleraGate>0f && ab>=4.5f && ab<=29.5f && ocean[i]<.5f)
                    {
                        float andesGate=ExoPlaSimSupplement.SmoothStep(1450f,2300f,wCordillera);
                        float elevGate=ExoPlaSimSupplement.SmoothStep(1120f,680f,landMeanElev[i]);
                        float eastReachGate=ab>20f?ExoPlaSimSupplement.SmoothStep(1350f,850f,distKm):1f;
                        if(andesGate>0f && elevGate>0f && eastReachGate>0f)
                        {
                            float eqWetLand=0f;
                            for(int step=1;step<=4;step++)
                            {
                                float dist=360f*step;
                                float sy=y-hemi*dist/kmRow;
                                float srcAb=Math.Abs(((FMath.Clamp(sy,0f,height-1f)+.5f)/height-.5f)*180f);
                                if(srcAb>14.5f)continue;
                                float sx0=x, sx1=x-280f/kmCol;
                                float land0=1f-FractionAt(ocean,width,height,sx0,sy);
                                float land1=1f-FractionAt(ocean,width,height,sx1,sy);
                                int idx0=FMath.Clamp((int)sy,0,height-1)*width+FMath.Wrap((int)Math.Round(sx0),width);
                                float tradeFeed=FMath.Clamp((sectors[idx0].TradeOcean-.22f)/.28f,0f,1f);
                                eqWetLand=Math.Max(eqWetLand,FMath.Clamp((.5f*(land0+land1)-.55f)/.30f,0f,1f)*tradeFeed);
                            }
                            float eastOcean=0f;
                            for(int k=1;k<=5;k++)
                                eastOcean=Math.Max(eastOcean,FractionAt(ocean,width,height,x+(k*380f)/kmCol,y));
                            float eastSeaGate=FMath.Clamp((eastOcean-.35f)/.35f,0f,1f);
                            saczMonsoon[i]=andesGate*elevGate*eastReachGate*eqWetLand*eastSeaGate
                                *ExoPlaSimSupplement.SmoothStep(4.5f,9.5f,ab)*ExoPlaSimSupplement.SmoothStep(29.5f,23.5f,ab);
                        }
                    }

                    // 3. Isolated Horse-Latitude Island-Continent (e.g. Outback Queensland/NSW inland of Great Dividing Range, >155 km):
                    //    Lacks BOTH a poleward midlatitude landmass (42..52 deg is ocean) AND an equatorial continent (3..11 deg is sea,
                    //    sparing Southern Africa which connects continuously to the equatorial Congo landmass!).
                    if(ab>=14f && ab<=31.8f && ocean[i]<.5f && saczMonsoon[i]<.2f && gulfCorridor[i]<.2f)
                    {
                        float poleMidLand=0f;
                        for(int p=0;p<3;p++)
                        {
                            float pLat=hemi*(42f+4.5f*p);
                            float py=(pLat/180f+.5f)*height-.5f;
                            poleMidLand+=((1f-FractionAt(ocean,width,height,x-250f/kmCol,py))
                                +(1f-FractionAt(ocean,width,height,x+250f/kmCol,py)))/6f;
                        }
                        float eqBandLand=0f;
                        for(int q=0;q<3;q++)
                        {
                            float eLat=hemi*(3.5f+3.5f*q);
                            float ey=(eLat/180f+.5f)*height-.5f;
                            eqBandLand+=((1f-FractionAt(ocean,width,height,x-250f/kmCol,ey))
                                +(1f-FractionAt(ocean,width,height,x+250f/kmCol,ey)))/6f;
                        }
                        float noPoleContinent=ExoPlaSimSupplement.SmoothStep(.32f,.10f,poleMidLand);
                        float noEqContinent=ExoPlaSimSupplement.SmoothStep(.45f,.20f,eqBandLand);
                        isolatedSubtropLee[i]=(1f-ocean[i])*noPoleContinent*noEqContinent*ExoPlaSimSupplement.SmoothStep(155f,430f,distKm)
                            *ExoPlaSimSupplement.SmoothStep(14.0f,18.5f,ab)*ExoPlaSimSupplement.SmoothStep(31.8f,27.2f,ab);
                    }

                    // 4. Topographic Prominence: Mountain Water Towers (Zagros, Caucasus, Tian Shan, Rockies, Qilian)
                    //    vs Enclosed / High Rain-Shadow Basins (Tarim, Great Basin, Mexican Interior Basin, Puna).
                    float regionalElev=0f;
                    if(ab>=16f && ab<=55f && ocean[i]<.5f)
                    {
                        for(int dir=0;dir<8;dir++)
                        {
                            double ang=dir*(Math.PI/4.0);
                            float ex=(float)Math.Cos(ang),ny=(float)Math.Sin(ang);
                            float e1=FractionAt(landMeanElev,width,height,x+ex*380f/kmCol,y+ny*380f/kmRow);
                            float e2=FractionAt(landMeanElev,width,height,x+ex*760f/kmCol,y+ny*760f/kmRow);
                            regionalElev+=(.45f*e1+.55f*e2)*.125f;
                        }
                        float prominence=landCrest[i]-regionalElev;
                        if(ab>=26.0f && prominence>360f)
                        {
                            orographicTower[i]=(1f-ocean[i])*ExoPlaSimSupplement.SmoothStep(360f,1020f,prominence)
                                *ExoPlaSimSupplement.SmoothStep(820f,1480f,landCrest[i])
                                *ExoPlaSimSupplement.SmoothStep(26.0f,30.5f,ab)*ExoPlaSimSupplement.SmoothStep(55.5f,49.5f,ab);
                        }
                    }

                    // 5. Subtropical / Midlatitude High Basin-Plateau Floor Adjacent to Western Ocean (spares prominent mountain water towers!):
                    if(ab>=16.5f && ab<=43.5f && ocean[i]<.5f && landMeanElev[i]>880f)
                    {
                        float westSeaNear=0f;
                        for(int k=1;k<=6;k++)
                            westSeaNear=Math.Max(westSeaNear,FractionAt(ocean,width,height,x-(k*280f)/kmCol,y));
                        float westOceanGate=FMath.Clamp((westSeaNear-.35f)/.35f,0f,1f);
                        float notShieldedAsia=1f-FMath.Clamp(sectors[i].ShieldedInflow*8f,0f,1f);
                        subtropHighPlateau[i]=(1f-ocean[i])*(1f-orographicTower[i])*westOceanGate*notShieldedAsia
                            *ExoPlaSimSupplement.SmoothStep(880f,1420f,landMeanElev[i])
                            *(1f-FMath.Clamp(2.5f*saczMonsoon[i]+2.5f*gulfCorridor[i],0f,1f))
                            *ExoPlaSimSupplement.SmoothStep(16.5f,20.5f,ab)*ExoPlaSimSupplement.SmoothStep(43.5f,37.5f,ab);
                    }

                    // 6. Deep Midlatitude Steppe Isolated from Open Westerly & Equatorward Oceans (spares mountain water towers!):
                    if(ab>=38.0f && ab<=54.0f && ocean[i]<.5f)
                    {
                        float westOpenSea=0f;
                        for(int k=4;k<=10;k++)
                            westOpenSea+=FractionAt(ocean,width,height,x-(k*300f)/kmCol,y)/7f;
                        float eqOpenSea=0f;
                        for(int k=2;k<=6;k++)
                            eqOpenSea+=FractionAt(ocean,width,height,x,y-hemi*(k*300f)/kmRow)/5f;
                        float noWestOcean=ExoPlaSimSupplement.SmoothStep(.22f,.08f,westOpenSea);
                        float noEqOcean=ExoPlaSimSupplement.SmoothStep(.18f,.05f,eqOpenSea);
                        float noEastFetch=1f-FMath.Clamp(3.0f*sectors[i].SummerMarineFetch+3.0f*sectors[i].EasternMargin+2.5f*gulfCorridor[i],0f,1f);
                        interiorSteppe[i]=(1f-ocean[i])*(1f-orographicTower[i])*noWestOcean*noEqOcean*noEastFetch
                            *ExoPlaSimSupplement.SmoothStep(38.0f,42.0f,ab)*ExoPlaSimSupplement.SmoothStep(54.0f,49.5f,ab);
                    }
                }
            }
        }

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
                    sectors[i].Monsoon=FMath.Clamp(upstream*polewardLand/144,0,1);
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
        // Trace three poleward trajectories from the ocean towards the sheltered
        // summer heat low. Crossing land progressively cuts off the marine supply.
        static float[] MonsoonFeeder(float[] ocean,MarineSectors[] sectors,int width,int height,int hemisphere)
        {
            var feed=new float[ocean.Length];
            var head=new float[ocean.Length];
            float kmY=20015.115f/height;
            for(int y=0;y<height;y++)
            {
                float poleward=hemisphere*((y+.5f)/height*180-90);
                float belt=FMath.Clamp((poleward-2)/8,0,1)*FMath.Clamp((38-poleward)/12,0,1);
                for(int x=0;x<width;x++) head[y*width+x]=belt*sectors[y*width+x].ShieldedInflow;
            }
            for(int y=0;y<height;y++)
            {
                float lat=(y+.5f)/height*180-90;
                if(hemisphere*lat < -22 || hemisphere*lat > 35) continue;
                float kmX=kmY*(float)Math.Cos(lat*Math.PI/180);
                for(int x=0;x<width;x++)
                {
                    for(int ray=0;ray<3;ray++)
                    {
                        float dx=(.35f+.5f*ray)*320/kmX,dy=hemisphere*320/kmY;
                        float marine=1,reach=0;
                        for(int k=1;k<=7;k++)
                        {
                            float xx=x+k*dx,yy=y+k*dy;
                            reach=Math.Max(reach,FractionAt(head,width,height,xx,yy)*marine/(1+.075f*k));
                            marine*=.25f+.75f*FractionAt(ocean,width,height,xx,yy);
                        }
                        feed[y*width+x]+=reach/3;
                    }
                }
            }
            return feed;
        }

        // A dry subcloud layer takes up a larger fraction of light precipitation.
        // Returned mm stay in the vapour column; only throughfall reaches the soil.
        internal static float SubcloudEvaporation(float gross,float air,float capacity,float dt,
            float wettedSurface,float ventilation)
        {
            float deficit=Math.Max(0,1-air/capacity)*(1-wettedSurface);
            float intensity=gross/dt;
            deficit/=1+intensity/Math.Max(1,.1f*capacity);
            float demand=1.1f*deficit*deficit*ventilation;
            return Math.Min(gross,Math.Min(Math.Max(0,capacity-air),demand*(float)Math.Sqrt(gross*dt*capacity/35f)));
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
            bool evaporate,FlatOceanCirculation currents,bool seasonalWinds=true,int maxColumns=128,
            MonthlyFluxObserver observeFlux=null)
        {
            int width=Math.Min(maxColumns,world.Width),height=width/2,count=width*height;
            var state=new FlatMoistureState(width,height){OceanCurrents=currents};var elevation=new float[count];var water=new float[count];var area=new float[count];
            var ocean=new float[count];
            var ridgeHeight=new float[count];
            var landMeanElev=new float[count];
            var landCrest=new float[count];
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
                double nonOceanWeight=0,nonOceanSum=0,nonOceanSq=0;
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
                        else
                        {
                            nonOceanWeight+=weight;
                            nonOceanSum+=weight*surface;
                            nonOceanSq+=weight*surface*surface;
                        }
                        for(int m=0;m<12;m++)monthlyTemperature[i*12+m]+=weight*world.Temperature[n*12+m];
                    }
                }
                water[i]=FMath.Clamp(water[i],0,1);
                ocean[i]=FMath.Clamp(ocean[i],0,water[i]);
                // Mean plus one within-cell standard deviation represents the
                // upper slopes of a partly flooded or unresolved coastal ridge.
                ridgeHeight[i]=elevation[i]+(float)Math.Sqrt(Math.Max(0,ridgeHeight[i]-elevation[i]*elevation[i]));
                if(nonOceanWeight>.08)
                {
                    double lMean=nonOceanSum/nonOceanWeight;
                    double lStd=Math.Sqrt(Math.Max(0,nonOceanSq/nonOceanWeight-lMean*lMean));
                    landMeanElev[i]=(float)lMean;
                    landCrest[i]=(float)(lMean+1.25*lStd);
                }
                else
                {
                    landMeanElev[i]=elevation[i];
                    landCrest[i]=ridgeHeight[i];
                }
            }
            var regionalLand=RegionalContinentality(continentality,width,height);
            var sectors=MonsoonAccess(ocean,elevation,ridgeHeight,width,height);
            var northFeed=MonsoonFeeder(ocean,sectors,width,height,1);
            var southFeed=MonsoonFeeder(ocean,sectors,width,height,-1);
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
            double totalSphereArea=0,totalOceanArea=0;
            for(int i=0;i<count;i++){totalSphereArea+=area[i];totalOceanArea+=area[i]*ocean[i];}
            float oceanShare=totalSphereArea>0?(float)(totalOceanArea/totalSphereArea):0f;
            float oceanContrast=seasonalWinds?FMath.Clamp(6f*oceanShare*(1f-oceanShare),0f,1f):0f;
            var gleba=seasonalWinds?(state.Gleba=GlebaClimateSupplement.BuildFromAggregated(width,height,water,ocean,elevation,ridgeHeight,options)):null;
            var planetCfg=world.PlanetClimate??ExoPlaSimPlanetConfig.DefaultEarth;
            var exo=seasonalWinds?(state.ExoPlaSim=new ExoPlaSimSupplement(width,height,planetCfg)):null;
            if(exo!=null && oceanContrast>0f)
            {
                var guard=ExoPlaSimSupplement.ComputeMaritimeSeaBreezeGuard(width,height,ocean,landMeanElev,annualTemperature,currents,gleba,planetCfg.MaritimeSeaBreezeGuard);
                Array.Copy(guard,exo.MaritimeSeaBreezeGuard,count);
            }
            var landSource=new float[count];
            var crestSource=new float[count];
            for(int i=0;i<count;i++)
            {
                // Continentality measures lack of marine/oceanic airmass influence; endorheic inland lakes (e.g. Caspian/Aral) are continental.
                landSource[i]=1f-ocean[i];
                crestSource[i]=(1f-.85f*ocean[i])*FMath.Clamp((landCrest[i]-520f)/1700f,0f,1f);
            }
            float[] gulfCorridor=null,saczMonsoon=null,isolatedSubtropLee=null,subtropHighPlateau=null,interiorSteppe=null,orographicTower=null;
            if(oceanContrast>0f)
                ComputeContinentalCorridors(width,height,ocean,landMeanElev,landCrest,sectors,gleba,
                    out gulfCorridor,out saczMonsoon,out isolatedSubtropLee,out subtropHighPlateau,out interiorSteppe,out orographicTower);
            Parallel.For(0,12,options,month=>
            {
                var column=months[month]=new MonthlyTransport(count);
                var pet=column.PotentialEvaporation;var cap=column.Capacity;
                var pressure=new float[count];var u=new float[count];var v=new float[count];
                var stormWest=new float[count];var stormPoleward=new float[count];
                var lift=column.Lift;var rainEfficiency=column.Efficiency;var transpiration=column.Transpiration;
                float solstice=(float)Math.Cos((month-6)*Math.PI/6);
                // Marine heating lags the land. The marine wind branch persists into late summer.
                float marineSeason=(float)Math.Cos((month-7)*Math.PI/6);
                int summerPole=marineSeason>=0?1:-1;
                var zonalTemp=new float[height];var weights=column.Faces;
                var zonalGlebaP=new float[height];
                if(gleba!=null && oceanContrast>0f)
                {
                    for(int y=0;y<height;y++)
                    {
                        double sumP=0;int row=y*width;
                        for(int x=0;x<width;x++)sumP+=gleba.PressureHpa[(row+x)*12+month];
                        zonalGlebaP[y]=(float)(sumP/width);
                    }
                }
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
                    if(gleba!=null && oceanContrast>0f && ocean[i]>0f)
                    {
                        float hemiSign=FMath.Clamp((float)Math.Sin(lat*Math.PI/180)*6f,-1f,1f);
                        float equatorward=-hemiSign*gleba.CurrentNorth[i*12+month];
                        coastalCurrent+=.16f*oceanContrast*ocean[i]*FMath.Clamp(equatorward*8f,-1f,1f)
                            *(float)Math.Exp(-Math.Pow((Math.Abs(lat)-24)/16,2));
                    }
                    // Nearshore tropical western-ocean upwelling off continuous eastern continental margins bounded by a poleward monsoon landmass
                    // & coastal rift highlands (Somali / Arabian coast; 0 on Amazon mouth & Indonesia!)
                    if(oceanContrast>0f && ocean[i]>.25f && Math.Abs(lat)<14.5f)
                    {
                        float kmR1=20015.115f/height, kmC1=kmR1*Math.Max(.25f,(float)Math.Cos(lat*Math.PI/180));
                        float cw1=1f-FractionAt(ocean,width,height,x-750f/kmC1,y);
                        float cw2=1f-FractionAt(ocean,width,height,x-1550f/kmC1,y);
                        float westRift=Math.Max(FractionAt(landCrest,width,height,x-450f/kmC1,y),FractionAt(landCrest,width,height,x-950f/kmC1,y));
                        float nMonsoonRow=(23f/180f+.5f)*height-.5f, sMonsoonRow=(-23f/180f+.5f)*height-.5f;
                        float poleBasinWall=Math.Max(
                            1f-.5f*(FractionAt(ocean,width,height,x+300f/kmC1,nMonsoonRow)+FractionAt(ocean,width,height,x+1200f/kmC1,nMonsoonRow)),
                            1f-.5f*(FractionAt(ocean,width,height,x+300f/kmC1,sMonsoonRow)+FractionAt(ocean,width,height,x+1200f/kmC1,sMonsoonRow)));
                        float somaliBasinGate=ExoPlaSimSupplement.SmoothStep(.28f,.55f,poleBasinWall)*ExoPlaSimSupplement.SmoothStep(700f,1300f,westRift);
                        float somaliSea=ocean[i]*somaliBasinGate*FMath.Clamp((Math.Min(cw1,cw2)-.55f)/.30f,0f,1f)*(float)Math.Exp(-Math.Pow(lat/11f,2));
                        coastalCurrent+=.95f*oceanContrast*somaliSea;
                    }
                    pet[i]*=1-.30f*coastalCurrent;
                    if(currents!=null && ocean[i]>0)
                    {
                        // Wind-driven surface currents change the saturation humidity
                        // at the sea skin. Air temperature already includes the marine
                        // heat carried inland, so only its difference from SST enters here.
                        float skinMinusAir=(currents.SeaAnomaly[i]*.56f-FlatClimate.MarineAirCoupling*currents.AirAnomaly[i]);
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
                    float glebaPressureAnom=gleba!=null && oceanContrast>0f
                        ?(gleba.PressureHpa[i*12+month]-zonalGlebaP[y])*.005f*oceanContrast:0f;
                    pressure[i]=1.7f*(float)Math.Exp(-Math.Pow((Math.Abs(lat)-subtropical)/12,2))
                        -1.2f*(float)Math.Exp(-Math.Pow((lat-itcz)/15,2))-seasonalHeat*.8f-thermal*.055f+glebaPressureAnom;
                }
                var surfEastArr=new float[count];
                var surfNorthArr=new float[count];
                var glebaWindX=gleba!=null && oceanContrast>0f?new float[count]:null;
                var glebaWindY=gleba!=null && oceanContrast>0f?new float[count]:null;
                for(int y=0;y<height;y++)
                {
                    float lat=((y+.5f)/height-.5f)*180,ab=Math.Abs(lat);
                    float coriolis=(float)Math.Sin(lat*Math.PI/180);
                    var winds=seasonalWinds?new FlatWindBelts(lat,solstice):default;
                    float referenceEast=seasonalWinds?0:(float)Math.Tanh((ab-30)/8)*(float)Math.Tanh((68-ab)/8)*.24f;
                    float hemi=FMath.Clamp(coriolis*8,-1,1);
                    float summer=solstice*hemi;
                    float spring=-(float)Math.Sin((month-6)*Math.PI/6)*hemi;
                    float polewardLimit=34+3.5f*summer;
                    float onshoreBelt=(float)((.5+.5*Math.Tanh((ab-23)/4))*(.5-.5*Math.Tanh((ab-polewardLimit)/3)))*(.9f+.1f*summer);
                    float heating=Math.Max(0,summer);
                    float frontLatitude=24+21*heating*heating*heating;
                    float advancingFront=.55f*FMath.Clamp(spring+summer,0,1)
                        *(float)Math.Exp(-Math.Pow((ab-frontLatitude)/7,2));
                    float coolingOutflow=Math.Max(0,-spring)*(float)Math.Exp(-Math.Pow((ab-43)/13,2));
                    onshoreBelt*=1-.55f*Math.Max(0,-spring);
                    float warmSea=Math.Max(0,.924f*summer-.383f*spring);
                    float coastalSeason=.8f*heating*warmSea*(1-onshoreBelt)
                        *FMath.Clamp((ab-30)/8,0,1)*FMath.Clamp((58-ab)/12,0,1);
                    float gradient=hemi*(zonalTemp[Math.Max(0,y-1)]-zonalTemp[Math.Min(height-1,y+1)])*gridScale;
                    float rotation=12.601f*coriolis;
                    float thermalResponse=.20f*(float)Math.Exp(-Math.Pow(ab/32,4))*gridScale/(1+rotation*rotation);
                    float stormFlux=.09f*hemi*FMath.Clamp(gradient/6,0,1)*
                        (float)Math.Exp(-Math.Pow((ab-52)/20,4))*(1-.45f*summer)*gridScale;
                    float westBelt=.75f*(float)Math.Exp(-Math.Pow((ab-37)/9,4));
                    float monsoonBelt=(float)Math.Exp(-Math.Pow((ab-20)/17,2));
                    float wetWindSeason=Math.Max(0,hemi*marineSeason);
                    float jetSeason=FMath.Lerp(heating,wetWindSeason,.6f);
                    float turn=FMath.Clamp((summerPole*lat+1)/15,0,1);
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
                        float westCoast=westBelt*sector.WesternMargin;
                        float subsidence=westCoast*Math.Max(0,summer),winterFront=westCoast*Math.Max(0,-summer);
                        float meanEast=referenceEast,returnFlow=0;
                        if(seasonalWinds)winds.Surface(convergenceLatitude[i],1-regionalLand[i],out meanEast,out returnFlow);
                        float east=meanEast-gy*coriolis*.08f+monsoon*summer*.27f-.5f*onshore+.23f*winterFront-.22f*subsidence;
                        float north=gx*coriolis*.08f+monsoon*solstice*.20f+.14f*onshore*hemi*summer+hemi*(.12f*winterFront-.17f*subsidence);
                        float summerJet=sector.ShieldedInflow*monsoonBelt*heating;
                        float momentumJet=sector.ShieldedInflow*monsoonBelt*jetSeason;
                        east+=.48f*momentumJet-.85f*sector.BarrierJet*monsoonBelt*jetSeason-.42f*coastFront+.12f*offshore;
                        north+=hemi*(.15f*momentumJet+.18f*coastFront-.15f*offshore);
                        east-=.18f*marineInflow;
                        north+=hemi*.24f*marineInflow;
                        float jetSpeed=1+.55f*momentumJet;
                        float landKm=3600*continentality[i]/Math.Max(.01f,1-water[i]);
                        float retained=(float)Math.Exp(-landKm/600-Math.Max(0,elevation[i])/1600);
                        float localFeed=.6f*sector.ShieldedInflow*monsoonBelt*FMath.Clamp((hemi*summerPole+1)/2,0,1);
                        float path=Math.Max(summerPole>0?northFeed[i]:southFeed[i],localFeed);
                        float supply=path*Math.Abs(marineSeason)*(ocean[i]+(1-ocean[i])*retained);
                        // Transfer low-level momentum to the existing conservative donor scheme.
                        float targetEast=.52f*turn, targetNorth=.27f*(1-.4f*turn);
                        float inflowEast=supply*Math.Max(0,targetEast-(east-gx*.054f)*jetSpeed);
                        float inflowNorth=supply*Math.Max(0,targetNorth-summerPole*(north-gy*.09f)*jetSpeed);
                        float bay=sector.BarrierJet*wetWindSeason*monsoonBelt;
                        float jetEast=1.2f*inflowEast-.5f*bay;
                        float jetNorth=1.2f*summerPole*inflowNorth-.09f*hemi*bay;
                        float surfaceEast=surfEastArr[i]=FMath.Clamp(east+.4f*jetEast-gx*.12f,-.32f,.32f);
                        float surfaceNorth=surfNorthArr[i]=y==0||y==height-1?0:FMath.Clamp(north+.4f*jetNorth+returnFlow-gy*.20f,-.14f,.14f);
                        column.WindExchange[i]=OpenWaterExchange(surfaceEast,surfaceNorth,stormLift/.10f);
                        if(seasonalWinds && gleba!=null && oceanContrast>0f)
                        {
                            const float radToDeg=57.29578f;
                            float gItcz=gleba.ItczLatRad[x*12+month]*radToDeg;
                            float gPfN=gleba.PolarNorthLatRad[x*12+month]*radToDeg;
                            float gPfS=gleba.PolarSouthLatRad[x*12+month]*radToDeg;
                            float pScale=width/128f;
                            float pgEastHpa=(gleba.PressureHpa[westCell[i]*12+month]-gleba.PressureHpa[eastCell[i]*12+month])*pScale;
                            float pgNorthHpa=(gleba.PressureHpa[southCell[i]*12+month]-gleba.PressureHpa[northCell[i]*12+month])*pScale*(2f*height/width);
                            float monsoonOvr=FMath.Clamp(1.8f*momentumJet+1.5f*sector.BarrierJet*monsoonBelt+1.4f*supply,0f,1f);
                            winds.SurfaceWithGleba(convergenceLatitude[i],1-regionalLand[i],gItcz,gPfN,gPfS,pgEastHpa,pgNorthHpa,oceanContrast,monsoonOvr,out float gSurfE,out float gSurfN);
                            float pubEast=FMath.Clamp(surfaceEast+(gSurfE-meanEast),-.32f,.32f);
                            float pubNorth=y==0||y==height-1?0:FMath.Clamp(north+.4f*jetNorth+gSurfN-gy*.20f,-.28f,.28f);
                            state.East[i*12+month]=pubEast/.32f;
                            state.North[i*12+month]=pubNorth/.32f;
                        }
                        else
                        {
                            state.East[i*12+month]=surfaceEast/.32f;state.North[i*12+month]=surfaceNorth/.32f;
                        }
                        if(glebaWindX!=null)
                        {
                            glebaWindX[i]=surfaceEast/.32f;
                            glebaWindY[i]=surfaceNorth/.32f;
                        }
                        const float boundaryShare=.45f;
                        // The low-level jet carries moisture faster than the
                        // frictional surface current. Leave a nonnegative share
                        // of each donor column for diffusion and local storage.
                        u[i]=FMath.Clamp(east-gx*.12f*boundaryShare,-.32f,.32f)*gridScale*jetSpeed;
                        float columnReturn=returnFlow*(.05f+.15f*ocean[i]);
                        float advectedNorth=FMath.Clamp(north+columnReturn-gy*.20f*boundaryShare,-.14f,.14f)*gridScale*jetSpeed;
                        var heatGradient=heatLow[i];
                        float towardsEast=.25f*heatGradient.East+solstice*heatGradient.SummerEast;
                        float towardsNorth=.25f*heatGradient.North+solstice*heatGradient.SummerNorth;
                        u[i]+=thermalResponse*(towardsEast+rotation*towardsNorth);
                        advectedNorth+=thermalResponse*(towardsNorth-rotation*towardsEast);
                        u[i]+=jetEast*gridScale;
                        advectedNorth+=jetNorth*gridScale;
                        float meridionalLimit=seasonalWinds?.28f:Math.Min(.28f,1-4*eddy-Math.Abs(u[i]));
                        v[i]=y==0||y==height-1?0:FMath.Clamp(advectedNorth+stormFlux,-meridionalLimit,meridionalLimit);
                        if(seasonalWinds)
                        {
                            float maxZonal=1-4*eddy-Math.Abs(v[i]);
                            u[i]=FMath.Clamp(u[i]/area[i],-maxZonal,maxZonal);
                        }
                        float wetSeason=monsoon*Math.Max(0,summer);
                        float corridor=sector.PolewardCorridor*corridorBelt
                            *FMath.Clamp((monthlyTemperature[i*12+month]-2)/16,0,1)
                            *(1-FMath.Clamp(2*sector.EasternMargin+wetSeason+summerJet,0,1));
                        u[i]*=1-.50f*corridor;
                        float available=Math.Max(0,Math.Min(.38f,1-4*eddy-Math.Abs(u[i])));
                        v[i]=FMath.Clamp(v[i]+hemi*corridor*.24f*gridScale,-available,available);
                        if(gulfCorridor!=null && gulfCorridor[i]>0f && y>0 && y<height-1)
                        {
                            float coolBaroclinic=FMath.Clamp(.78f-.72f*summer,0f,1f);
                            float gulfFlow=oceanContrast*gulfCorridor[i]*coolBaroclinic;
                            float availGulf=Math.Max(0,Math.Min(.38f,1-4*eddy-Math.Abs(u[i])));
                            v[i]=FMath.Clamp(v[i]+hemi*.095f*gulfFlow*gridScale,-availGulf,availGulf);
                        }
                    }
                }
                float[] advWindCont=null,advCrest=null;
                if(gleba!=null && oceanContrast>0f)
                {
                    GlebaHelmholtzSolver.TraceUpwindFetchAndShadow(
                        width,height,landSource,crestSource,glebaWindX,glebaWindY,
                        out advWindCont,out advCrest);
                }
                for(int y=0;y<height;y++)
                {
                    float lat=((y+.5f)/height-.5f)*180,ab=Math.Abs(lat);
                    float coriolis=(float)Math.Sin(lat*Math.PI/180);
                    float hemi=FMath.Clamp(coriolis*8,-1,1);
                    float summer=solstice*hemi;
                    float spring=-(float)Math.Sin((month-6)*Math.PI/6)*hemi;
                    float polewardLimit=34+3.5f*summer;
                    float onshoreBelt=(float)((.5+.5*Math.Tanh((ab-23)/4))*(.5-.5*Math.Tanh((ab-polewardLimit)/3)))*(.9f+.1f*summer);
                    float heating=Math.Max(0,summer);
                    float frontLatitude=24+21*heating*heating*heating;
                    float advancingFront=.55f*FMath.Clamp(spring+summer,0,1)
                        *(float)Math.Exp(-Math.Pow((ab-frontLatitude)/7,2));
                    onshoreBelt*=1-.55f*Math.Max(0,-spring);
                    float warmSea=Math.Max(0,.924f*summer-.383f*spring);
                    float coastalSeason=.8f*heating*warmSea*(1-onshoreBelt)
                        *FMath.Clamp((ab-30)/8,0,1)*FMath.Clamp((58-ab)/12,0,1);
                    float gradient=hemi*(zonalTemp[Math.Max(0,y-1)]-zonalTemp[Math.Min(height-1,y+1)])*gridScale;
                    float westBelt=.75f*(float)Math.Exp(-Math.Pow((ab-37)/9,4));
                    float tradeInversion=(float)Math.Exp(-Math.Pow((ab-23)/13,2));
                    float winterTropics=(float)Math.Exp(-Math.Pow((ab-13)/12,2))*Math.Max(0,-summer);
                    float coastalAscent=.5f+.7f*(1-summer)*FMath.Clamp(gradient/6,0,1);
                    float monsoonBelt=(float)Math.Exp(-Math.Pow((ab-20)/17,2));
                    float wetWindSeason=Math.Max(0,hemi*marineSeason);
                    float equatorial=(float)Math.Exp(-Math.Pow(ab/13,2));
                    float descent=(float)Math.Exp(-Math.Pow((ab-24)/14,2));
                    float tropicalFlank=(float)Math.Exp(-Math.Pow((ab-19)/9,2));
                    float fronts=.043f*(float)Math.Exp(-Math.Pow((ab-48-8*summer)/14,2));
                    float stormLift=.10f*(float)Math.Exp(-Math.Pow((ab-54)/21,4))*
                        (1-.65f*summer)*(.5f+.5f*FMath.Clamp(gradient/6,0,1));
                    float corridorBelt=(float)Math.Exp(-Math.Pow((ab-23)/22,4))*
                        FMath.Clamp((ab-6)/16,0,1)*(.70f+.30f*summer);
                    float winterMedGate=1f-.80f*FMath.Clamp(-summer,0f,1f)*FMath.Clamp((ab-27f)/8f,0f,1f);
                    // Deep continental dryness strictly targets subtropics & midlatitudes (ab >= 20 deg), leaving equatorial Amazon/Congo untouched
                    float inlandLatBand=FMath.Clamp((ab-19f)/7f,0f,1f)*FMath.Clamp((56f-ab)/10f,.08f,1f);
                    float coolSeason=FMath.Clamp(-summer,0f,1f);
                    float marginBelt=(float)Math.Exp(-Math.Pow((ab-34f)/8.5f,2));
                    // Monsoon depressions & tropical cyclones require planetary vorticity away from the equator (<7 deg), reaching full strength across Bay of Bengal (>=12.5 deg)
                    float coriolisGenesis=FMath.Clamp((ab-7.0f)/5.5f,0f,1f);
                    int summerSthzRow=FMath.Clamp((int)((summerPole*25f/180f+.5f)*height),0,height-1);
                    float kmRowPass2=20015.115f/height,kmColPass2=kmRowPass2*Math.Max(.15f,(float)Math.Cos(lat*Math.PI/180));
                    for(int x=0;x<width;x++)
                    {
                        int i=y*width+x;
                        var sector=sectors[i];
                        float surfaceEast=surfEastArr[i],surfaceNorth=surfNorthArr[i];
                        float windCont=advWindCont!=null?advWindCont[i]:0f;
                        float distOceanKm=gleba!=null?gleba.DistanceToSeaKm[i]:0f;
                        // Combine Gleba's wind-fetch continentality with distance to true ocean (>850 km inland, e.g. Central Asia, Sahara, Tarim)
                        float deepOceanDist=FMath.Clamp((distOceanKm-850f)/1450f,0f,1f);
                        float mappedCont=advWindCont!=null?Math.Max(
                            GlebaClimateConfig.EvaluateCurve(gleba.Config.ContinentalityFunction,windCont)*FMath.Clamp((distOceanKm-450f)/750f,0f,1f),
                            deepOceanDist*FMath.Clamp((windCont-.60f)/.28f,0f,1f)):0f;
                        float glebaImpact=gleba!=null?gleba.RainfallImpact[i*12+month]:0f;
                        float polewardImpact=gleba!=null?gleba.RainfallImpact[(summerSthzRow*width+x)*12+month]:0f;
                        // Damp monsoon trough only where poleward subtropics is a western-continent STHZ high (Sahara/Arabia/Thar-fringe/W.Australia, sparing shielded Indian core)
                        float sthzCap=oceanContrast*FMath.Clamp((-polewardImpact-.24f)/.52f,0f,1f)*(1f-FMath.Clamp(sector.ShieldedInflow*3.2f,0f,1f));
                        float monsoonTroughGate=1f-.52f*sthzCap;

                        // Coordinate-free tropical western-ocean-boundary divergence (Horn of Africa / Ethiopia / Kenya):
                        // requires continuous continent at -650 AND -1400 km west (100% across East Africa/Congo, 0% on New Guinea across Arafura/Banda Seas!)
                        // + open ocean +850..+1900 km east, active under offshore summer flow or continental winter outflow
                        float somaliDivergence=0f;
                        if(oceanContrast>0f && ab<17f)
                        {
                            float eastSea=.45f*FractionAt(ocean,width,height,x+850f/kmColPass2,y)+.55f*FractionAt(ocean,width,height,x+1850f/kmColPass2,y);
                            float w1=1f-FractionAt(ocean,width,height,x-650f/kmColPass2,y);
                            float w2=1f-FractionAt(ocean,width,height,x-1400f/kmColPass2,y);
                            float continuousWestContinent=Math.Min(w1,w2);
                            // Require a poleward subtropical landmass capping the eastern ocean basin (Arabia/Asia north of Indian Ocean = 0.8+,
                            // vs open North/South Atlantic off Amazon mouth = 0.0!) + coastal/rift highland chain (>750 m within 950 km west).
                            float nWallRow=(23f/180f+.5f)*height-.5f, sWallRow=(-23f/180f+.5f)*height-.5f;
                            float basinCapLand=Math.Max(
                                1f-.5f*(FractionAt(ocean,width,height,x+400f/kmColPass2,nWallRow)+FractionAt(ocean,width,height,x+1400f/kmColPass2,nWallRow)),
                                1f-.5f*(FractionAt(ocean,width,height,x+400f/kmColPass2,sWallRow)+FractionAt(ocean,width,height,x+1400f/kmColPass2,sWallRow)));
                            float riftCrest=Math.Max(landCrest[i],Math.Max(FractionAt(landCrest,width,height,x-450f/kmColPass2,y),FractionAt(landCrest,width,height,x-950f/kmColPass2,y)));
                            float monsoonalRiftMargin=ExoPlaSimSupplement.SmoothStep(.28f,.55f,basinCapLand)*ExoPlaSimSupplement.SmoothStep(680f,1250f,riftCrest);
                            float eastMarginTropics=monsoonalRiftMargin*FMath.Clamp((eastSea-.30f)/.35f,0f,1f)*FMath.Clamp((continuousWestContinent-.55f)/.30f,0f,1f)*(float)Math.Exp(-Math.Pow(ab/14f,2));
                            if(eastMarginTropics>0f)
                            {
                                float upMeridional=FMath.Clamp(-surfaceNorth/.018f,-1f,1f);
                                float upwindPoleLand=1f-.5f*(FractionAt(ocean,width,height,x+150f/kmColPass2,y+upMeridional*1200f/kmRowPass2)
                                    +FractionAt(ocean,width,height,x+500f/kmColPass2,y+upMeridional*1550f/kmRowPass2));
                                float offshoreMode=FMath.Clamp((surfaceEast+.03f)/.08f,0f,1f);
                                float outflowMode=Math.Abs(upMeridional)*FMath.Clamp((upwindPoleLand-.28f)/.32f,0f,1f)
                                    *FMath.Clamp((Math.Abs(lat-convergenceLatitude[i])-5f)/10f,0f,1f);
                                // Indian Ocean Walker descending branch keeps equatorial Somalia/Kenya/Turkana/Afar (<14.5 deg) semi-arid year-round,
                                // while coastal frictional divergence / Rift escarpment blocks westward export of un-rained vapour into Congo & Lake Chad!
                                float walkerDescend=.78f*ExoPlaSimSupplement.SmoothStep(15.0f,9.5f,ab);
                                somaliDivergence=oceanContrast*eastMarginTropics*FMath.Clamp(Math.Max(walkerDescend,Math.Max(offshoreMode,outflowMode)),0f,.94f);
                                cap[i]*=1f+.52f*somaliDivergence;
                                if(u[i]<0f) u[i]*=1f-.45f*somaliDivergence;
                            }
                        }

                        float gulfHere=gulfCorridor!=null?oceanContrast*gulfCorridor[i]:0f;
                        float gulfCool=gulfHere*FMath.Clamp(.82f-.75f*summer,0f,1f);
                        float saczHere=saczMonsoon!=null?oceanContrast*saczMonsoon[i]*(1f-somaliDivergence):0f;
                        float saczActive=saczHere*ExoPlaSimSupplement.SmoothStep(-.18f,.52f,summer);
                        float towerHere=orographicTower!=null?oceanContrast*orographicTower[i]:0f;
                        float nonMonsoonEastLee=isolatedSubtropLee!=null?oceanContrast*isolatedSubtropLee[i]*(1f-towerHere):0f;
                        float effectiveEastMargin=sector.EasternMargin*(1f-.78f*nonMonsoonEastLee);

                        float monsoon=sector.Monsoon*monsoonBelt*(1f-.56f*nonMonsoonEastLee), onshore=effectiveEastMargin*onshoreBelt;
                        float marineInflow=sector.SummerMarineFetch*coastalSeason;
                        float coastFront=effectiveEastMargin*advancingFront;
                        float westCoast=westBelt*sector.WesternMargin;
                        float subsidence=westCoast*Math.Max(0,summer),winterFront=westCoast*Math.Max(0,-summer);
                        float summerJet=sector.ShieldedInflow*monsoonBelt*heating;
                        float wetSeason=monsoon*Math.Max(0,summer)*monsoonTroughGate*(1f-.85f*somaliDivergence);
                        float corridor=sector.PolewardCorridor*corridorBelt
                            *FMath.Clamp((monthlyTemperature[i*12+month]-2)/16,0,1)
                            *(1-FMath.Clamp(2*effectiveEastMargin+wetSeason+summerJet,0,1));

                        // Warm-ocean western-margin winter storm track on moderate-width continents (Eastern US, SE South America)
                        // Spares wide Eurasia (windCont > 0.84 or distOceanKm > 1350 km) so North China retains its winter-dry monsoon regime
                        float moderateWidth=(1f-FMath.Clamp((windCont-.82f)/.12f,0f,1f))*(1f-FMath.Clamp((distOceanKm-1350f)/500f,0f,1f));
                        float warmMarginCyclones=oceanContrast*coolSeason*moderateWidth
                            *FMath.Clamp(1.9f*sector.EasternMargin+.35f*sector.Monsoon,0f,1f)*marginBelt;
                        if(warmMarginCyclones>0f && y>0 && y<height-1)
                        {
                            float avail=Math.Max(0,Math.Min(.38f,1-4*eddy-Math.Abs(u[i])));
                            v[i]=FMath.Clamp(v[i]+hemi*.065f*warmMarginCyclones*gridScale,-avail,avail);
                        }

                        int upX=u[i]>=0?westCell[i]:eastCell[i],upY=v[i]>=0?southCell[i]:northCell[i];
                        // Avoid fake uphill climb when stepping from a half-ocean coastal escarpment cell onto a lower/flat inland plateau (e.g. Ghats -> Deccan)
                        float baseUpX=oceanContrast>0f && ocean[i]<.15f && ocean[upX]>.12f && ocean[upX]<.88f
                            ?Math.Max(elevation[upX],landMeanElev[upX]):elevation[upX];
                        float baseUpY=oceanContrast>0f && ocean[i]<.15f && ocean[upY]>.12f && ocean[upY]<.88f
                            ?Math.Max(elevation[upY],landMeanElev[upY]):elevation[upY];
                        float rise=Math.Max(0,elevation[i]-baseUpX)*Math.Abs(u[i])
                            +Math.Max(0,elevation[i]-baseUpY)*Math.Abs(v[i]);
                        float ascendWeight=FMath.Clamp(rise/25f,.42f,.96f);
                        float localReceiver=FMath.Clamp((landMeanElev[i]-520f)/1700f,0f,1f)*ascendWeight;
                        float leeShadow=advCrest!=null?FMath.Clamp(advCrest[i]-localReceiver,0f,1f):0f;

                        // Dampen endorheic inland lake evaporation under intense midlatitude continental subsidence (e.g. Aral/Balkhash)
                        if(oceanContrast>0f && water[i]>ocean[i])
                        {
                            float lakeShare=(water[i]-ocean[i])/Math.Max(.01f,water[i]);
                            pet[i]*=1f-.50f*oceanContrast*mappedCont*inlandLatBand*lakeShare;
                        }

                        float moistLift=wetSeason+.28f*saczActive+(onshore+marineInflow)*coastalAscent+.65f*coastFront;
                        float effectiveTrade=sector.TradeOcean*(1f-.75f*somaliDivergence);
                        float continentalAir=tropicalFlank*(1-effectiveTrade)*(1-water[i]);

                        float itczDist=Math.Abs(lat-convergenceLatitude[i]);
                        float itczNear=(float)Math.Exp(-Math.Pow(itczDist/13.5,2));
                        float seaBreezePre=exo!=null?exo.MaritimeSeaBreezeGuard[i]:0f;
                        float oppositeHemiItcz=ExoPlaSimSupplement.SmoothStep(1f,-5f,lat*convergenceLatitude[i]);
                        float eqSolsticeDry=oceanContrast*(1f-ocean[i])*FMath.Clamp((regionalLand[i]-.38f)/.28f,0f,1f)
                            *(1f-FMath.Clamp(4f*seaBreezePre,0f,1f))*oppositeHemiItcz
                            *ExoPlaSimSupplement.SmoothStep(11.5f,17.5f,itczDist)
                            *ExoPlaSimSupplement.SmoothStep(2.2f,5.2f,ab)*ExoPlaSimSupplement.SmoothStep(13.5f,9.5f,ab);
                        // Keep full equatorial convection across Amazon core & Maritime Continent; allow solstice dry month on outer continental flanks
                        float dynamicEq=equatorial*(1f-.78f*somaliDivergence*(1f-.65f*itczNear))*(1f-.62f*eqSolsticeDry);

                        float convection=.12f*(float)Math.Exp(-Math.Pow((lat-convergenceLatitude[i])/13,2))+.11f*dynamicEq+.43f*moistLift;
                        float drySeason=FMath.Clamp(itczDist/22,0,1);
                        rainEfficiency[i]=FMath.Clamp(1-descent*(1-water[i])*(1.05f+.48f*drySeason)+3*moistLift
                            +tropicalFlank*effectiveTrade*.8f-.65f*continentalAir,.08f,2.6f);
                        lift[i]=rainEfficiency[i]*convection+(.4f+.6f*rainEfficiency[i])*(fronts+Math.Min(.30f,rise/1400));
                        float dryTropics=winterTropics*FMath.Clamp(itczDist/23,0,1);
                        // Opposing shores supply a warm marine boundary layer to a narrow island
                        // or peninsula. Its sea-breeze convergence and windward relief can break
                        // the continental winter inversion. A single continental shore keeps its
                        // inland dry-season forcing when the opposite flank is land.
                        float marineDry=dryTropics;
                        if(dryTropics>0 && ocean[i]<1 && monthlyTemperature[i*12+month]>16)
                        {
                            float eastSpeed=Math.Abs(surfaceEast), northSpeed=Math.Abs(surfaceNorth);
                            float speed=Math.Max(.01f,eastSpeed+northSpeed);
                            float islandGate=1f-oceanContrast*FMath.Clamp((regionalLand[i]-.22f)/.30f,0f,1f);
                            float oppositeSeas=islandGate*(eastSpeed*ocean[westCell[i]]*ocean[eastCell[i]]
                                +northSpeed*ocean[southCell[i]]*ocean[northCell[i]])/speed;
                            int fromX=surfaceEast>=0?westCell[i]:eastCell[i], fromY=surfaceNorth>=0?southCell[i]:northCell[i];
                            float windward=(eastSpeed*Math.Max(0,ridgeHeight[i]-Math.Max(0,ridgeHeight[fromX]))
                                +northSpeed*Math.Max(0,ridgeHeight[i]-Math.Max(0,ridgeHeight[fromY])))/speed;
                            // A shallow sea-breeze head contributes about 500 m of lift before the
                            // windward slope. Compare it with a 900 m marine mixed-layer depth.
                            float lifted=(float)(1-Math.Exp(-(500+windward)/900));
                            float warm=FMath.Clamp((monthlyTemperature[i*12+month]-16)/8,0,1);
                            marineDry*=1-FMath.Clamp(4*(1-ocean[i])*warm*oppositeSeas*lifted,0,1);
                        }
                        float marineInversion=1-Math.Min(.95f,tradeInversion*sector.WesternMargin);
                        lift[i]=(lift[i]*(1-.96f*marineDry)*marineInversion+.28f*winterFront)*(1-.94f*subsidence);
                        rainEfficiency[i]*=(1-.9f*marineDry)*marineInversion*(1-.96f*subsidence);
                        float warmCanopy=FMath.Clamp((monthlyTemperature[i*12+month]-8)/16,0,1);
                        transpiration[i]=.8f*(.85f+warmCanopy*(.98f*equatorial*(1f-.35f*eqSolsticeDry)+.35f*(wetSeason+saczActive)))*(1-.65f*dryTropics);
                        if(oceanContrast>0f)
                        {
                            // Moderate equatorial rainforest recycling (avoids over-piling vapour at the eastern foot of the Andes)
                            float eqBasin=(float)Math.Exp(-Math.Pow(ab/13f,2))*(1f-somaliDivergence);
                            transpiration[i]*=1f+.18f*oceanContrast*warmCanopy*eqBasin*(.45f+.55f*itczNear);
                        }
                        float climbEast=Math.Max(0,elevation[u[i]>=0?eastCell[i]:westCell[i]]-elevation[i]);
                        float climbNorth=Math.Max(0,elevation[v[i]>=0?northCell[i]:southCell[i]]-elevation[i]);
                        if(summerJet>0)
                        {
                            int downX=u[i]>=0?eastCell[i]:westCell[i],downY=v[i]>=0?northCell[i]:southCell[i];
                            float riseX=Math.Max(0,ridgeHeight[i]-ridgeHeight[upX])+Math.Max(0,ridgeHeight[downX]-ridgeHeight[i]);
                            float riseY=Math.Max(0,ridgeHeight[i]-ridgeHeight[upY])+Math.Max(0,ridgeHeight[downY]-ridgeHeight[i]);
                            float ascent=riseX*Math.Abs(u[i])+riseY*Math.Abs(v[i]);
                            lift[i]=lift[i]*(1-summerJet*ocean[i])+summerJet*Math.Min(3,ascent/(dt*650));
                            pet[i]*=1+.5f*summerJet*ocean[i];
                            float kmRow=20015.115f/height, kmCol=kmRow*area[i];
                            float dx=u[i]*kmCol,dy=v[i]*kmRow;
                            float length=(float)Math.Sqrt(dx*dx+dy*dy);
                            if(length>0)
                            {
                                dx*=300/(kmCol*length); dy*=300/(kmRow*length);
                                float lee=0;
                                for(int steps=1;steps<=3;steps++)
                                    lee=Math.Max(lee,(FractionAt(ridgeHeight,width,height,x-steps*dx,y-steps*dy)-elevation[i])/(1+.5f*(steps-1)));
                                float penGuard=exo!=null?exo.MaritimeSeaBreezeGuard[i]:0f;
                                float drying=(float)Math.Exp(-summerJet*lee*(1f-.78f*penGuard)/350);
                                lift[i]*=drying;
                                rainEfficiency[i]*=drying;
                            }
                        }
                        float frontalLift=stormLift*(1-sector.WesterlyShelter);
                        // Subsidence ventilates the subcloud layer away from the tropical
                        // convergence zone. Descending air in the lee of a ridge does the same.
                        float kmY=20015.115f/height,kmX=kmY*area[i];
                        float eastKm=u[i]*kmX,northKm=v[i]*kmY;
                        float windKm=(float)Math.Sqrt(eastKm*eastKm+northKm*northKm);
                        float leeDrop=0;
                        if(windKm>0)
                        {
                            float dx=250*u[i]/windKm,dy=250*v[i]/windKm;
                            for(int k=1;k<=4;k++)
                                leeDrop=Math.Max(leeDrop,(FractionAt(landCrest,width,height,x-k*dx,y-k*dy)-landMeanElev[i])/(1+.4f*(k-1)));
                        }
                        float dryDescent=descent*(1-(float)Math.Exp(-Math.Pow((lat-convergenceLatitude[i])/17,2)));
                        column.DryLayer[i]=.15f+.85f*Math.Min(1,dryDescent+1-(float)Math.Exp(-leeDrop/1200));
                        // In a warm monsoon trough, humid air organized over an adjacent
                        // sea can travel with a low pressure system against the mean wind.
                        // The track is westward and towards the summer pole in both hemispheres.
                        float receivingLand=1-FractionAt(ocean,width,height,x-800/kmX,y+summerPole*250/kmY);
                        float warmSource=FMath.Clamp((monthlyTemperature[i*12+month]-19)/8,0,1);
                        float genesisGate=oceanContrast>0f?(coriolisGenesis*(1f-.85f*somaliDivergence)):1f;
                        float source=wetWindSeason*monsoonBelt*ocean[i]*warmSource*receivingLand*genesisGate;
                        column.OrganizedFraction[i]=FMath.Clamp(source*(float)Math.Sqrt(sector.ShieldedInflow*sector.Monsoon),0,1);
                        float track=FMath.Clamp((45-ab)/12,0,1)*FMath.Clamp((lat*summerPole+5)/12,0,1)*(oceanContrast>0f?(coriolisGenesis*(1f-.80f*somaliDivergence)):1f);
                        float upland=Math.Max(0,elevation[westCell[i]]-elevation[i]);
                        stormPoleward[i]=Math.Min(1,.075f*gridScale*track);
                        stormWest[i]=Math.Min(1-stormPoleward[i],.36f*gridScale*track/(area[i]*(1+upland/1500)));
                        float lifetime=FMath.Lerp(.7f,FMath.Lerp(3.0f,5.0f,ocean[i]),track);
                        column.Release[i]=1-(float)Math.Exp(-dt/lifetime);
                        column.Rainout[i]=.4f+.24f*(1-water[i]);
                        lift[i]+=.24f*corridor+.60f*frontalLift+.16f*warmMarginCyclones+.15f*gulfCool+.12f*towerHere*(.45f+.55f*coolSeason);
                        float coastalRise=Math.Max(0,ridgeHeight[i]-ridgeHeight[upX])*Math.Abs(u[i])
                            +Math.Max(0,ridgeHeight[i]-ridgeHeight[upY])*Math.Abs(v[i]);
                        lift[i]+=.25f*marineInflow*Math.Min(2,coastalRise/(dt*220));
                        rainEfficiency[i]+=.8f*corridor+.40f*frontalLift/.055f+.62f*warmMarginCyclones+.65f*gulfCool+.58f*towerHere;

                        // Surgical Gleba + ExoPlaSim (`rainmod.f90` Convective vs Stratiform + Maritime Sea-Breeze Guard) coupling:
                        float tempNow=monthlyTemperature[i*12+month];
                        column.StratiformCritRh[i]=.90f;
                        column.SolidSnowFrac[i]=ExoPlaSimSupplement.SmoothStep(2.0f,-2.5f,tempNow);
                        float baseConv=rainEfficiency[i]*convection;
                        float baseStrat=(.4f+.6f*rainEfficiency[i])*(fronts+Math.Min(.30f,rise/1400))+.60f*frontalLift+.16f*warmMarginCyclones+.15f*gulfCool+.12f*towerHere;
                        column.ConvectiveShare[i]=FMath.Clamp((baseConv+.24f*corridor)/Math.Max(.005f,baseConv+baseStrat+.24f*corridor),0f,1f)*ExoPlaSimSupplement.SmoothStep(6f,19f,tempNow);

                        if(oceanContrast>0f)
                        {
                            float seaBreeze=exo!=null?exo.MaritimeSeaBreezeGuard[i]:0f;
                            column.SeaBreezeResilience[i]=seaBreeze;
                            float towerShield=1f-.78f*towerHere;
                            float nonIsland=FMath.Clamp((regionalLand[i]-.22f)/.35f,0f,1f)*(1f-.75f*seaBreeze);
                            float activeMonsoonGuard=1f-FMath.Clamp(wetSeason*4.0f+summerJet*2.5f+saczActive*2.4f+corridor*1.8f,0f,.90f);
                            float westOrInteriorSthz=FMath.Clamp((distOceanKm-450f)/450f+2.5f*Math.Max(0f,sector.WesternMargin-effectiveEastMargin),0f,1f);
                            float sthzDry=oceanContrast*Math.Max(0f,-glebaImpact)*winterMedGate*westOrInteriorSthz*activeMonsoonGuard*towerShield
                                *(.25f+.75f*(1f-ocean[i]))*(float)Math.Exp(-Math.Pow((ab-23f)/13f,2))*nonIsland;
                            float easternSeaboardGuard=1f-FMath.Clamp(2.8f*effectiveEastMargin+2.5f*sector.SummerMarineFetch+2.2f*saczActive+2.4f*gulfHere,0f,.86f);
                            float rawSteppe=interiorSteppe!=null?interiorSteppe[i]:0f;
                            float inlandDry=oceanContrast*Math.Max(mappedCont*inlandLatBand,.80f*rawSteppe)*easternSeaboardGuard*towerShield*(1f-ocean[i])*(1f-.85f*seaBreeze);
                            // Taper rain shadow across deep-equatorial maritime archipelagos (<10 deg), warm sea-breeze peninsulas, prominent mountain crests, and Gulf/SACZ corridors
                            float shadowLatGate=FMath.Clamp((ab-8.5f)/5.5f,.12f,1f)*(1f-.45f*FMath.Clamp(summerJet*2f,0f,1f))*(1f-.55f*seaBreeze)
                                *(1f-.78f*gulfHere)*(1f-.55f*saczActive)*(1f-.65f*towerHere);
                            float shadowDry=oceanContrast*shadowLatGate*FMath.Clamp(1.10f*leeShadow,0f,.55f)*(1f-.7f*ocean[i]);

                            // Subtropical / midlatitude high basin-plateau floor above marine boundary layer (spares prominent mountain water towers)
                            float subtropPlateau=subtropHighPlateau!=null?oceanContrast*subtropHighPlateau[i]*(1f-.90f*seaBreeze):0f;
                            // Blocked midlatitude steppe & Foehn lee: free-tropospheric advection above shallow surface inversion / adiabatic descent
                            float blockedSteppe=Math.Max(
                                inlandDry*ExoPlaSimSupplement.SmoothStep(38.0f,42.5f,ab)*ExoPlaSimSupplement.SmoothStep(54.0f,49.0f,ab)*(1f-FMath.Clamp(2.5f*gulfHere+3.0f*sector.SummerMarineFetch,0f,1f)),
                                .92f*oceanContrast*rawSteppe);
                            float foehnLee=shadowDry*ExoPlaSimSupplement.SmoothStep(33f,39f,ab)*(1f-FMath.Clamp(2.5f*gulfHere+2.0f*towerHere,0f,1f));
                            cap[i]*=1f+.48f*blockedSteppe*(.40f+.60f*coolSeason)+.50f*foehnLee+.38f*subtropPlateau+.18f*eqSolsticeDry;

                            // Harmattan / Intertropical Discontinuity dry-front cap on tropical desert-fringe steppes (Sahel 11..18 deg south of STHZ high)
                            float harmattanCap=oceanContrast*sthzCap*(1f-ocean[i])*(1f-.95f*seaBreeze)*towerShield
                                *ExoPlaSimSupplement.SmoothStep(10.8f,13.8f,ab)*ExoPlaSimSupplement.SmoothStep(21.5f,17.5f,ab)
                                *(1f-FMath.Clamp(2.5f*effectiveEastMargin+2.2f*saczActive+2.2f*gulfHere,0f,1f));

                            // Subtropical desert & dry-margin CIN lid (`ab >= 20.5 deg` ONLY so tropical Aw savannas at <20 deg stay 100% untouched!):
                            float subtropDesertGate=ExoPlaSimSupplement.SmoothStep(20.5f,25.5f,ab)*(float)Math.Exp(-Math.Pow((ab-28.0f)/12.5f,2));
                            float desertMarginLid=oceanContrast*sthzCap*(1f-ocean[i])*(1f-.95f*seaBreeze)*subtropDesertGate*towerShield
                                *(1f-FMath.Clamp(2.5f*effectiveEastMargin+2.2f*saczActive+2.2f*gulfHere,0f,1f));
                            float hyperAridCore=oceanContrast*(1f-ocean[i])*(1f-.95f*seaBreeze)*subtropDesertGate*towerShield
                                *ExoPlaSimSupplement.SmoothStep(.12f,.36f,sthzDry+.60f*sthzCap);

                            float combinedSuppress=(1f-.88f*somaliDivergence)*(1f-.43f*sthzDry)*(1f-.63f*inlandDry)*(1f-shadowDry)
                                *(1f-.34f*hyperAridCore)*(1f-.26f*desertMarginLid)*(1f-.36f*blockedSteppe)*(1f-.36f*foehnLee)
                                *(1f-.40f*subtropPlateau)*(1f-.34f*eqSolsticeDry)*(1f-.36f*harmattanCap);

                            // Soft-capped lowland equatorial + Maritime-Continent / Indochina diurnal sea-breeze convergence boost (0% SE Asia desert guarantee!)
                            float wallGuard=1f-FMath.Clamp((climbEast+climbNorth)/350f,0f,1f);
                            float lowlandGate=FMath.Clamp(1f-Math.Max(0f,landMeanElev[i]-220f)/650f,0f,1f)*wallGuard;
                            float unsaturatedPeak=FMath.Clamp(1f-lift[i]/.30f,0f,1f);
                            float warmBreeze=ExoPlaSimSupplement.SmoothStep(18.5f,25.0f,tempNow)*(1f-somaliDivergence);
                            // Monsoon peninsulas (>=10 deg, Indochina/Philippines) concentrate sea-breeze boost in warm wet season so winter stays true Aw!
                            float breezeSeason=FMath.Lerp(ExoPlaSimSupplement.SmoothStep(.10f,.58f,summer+.30f*warmSea),1f,ExoPlaSimSupplement.SmoothStep(10.5f,5.5f,ab));
                            float seaBreezeBoost=oceanContrast*seaBreeze*warmBreeze*breezeSeason*(.46f+.36f*itczNear);
                            float eqItczBoost=1f+oceanContrast*itczNear*lowlandGate*unsaturatedPeak*(1f-somaliDivergence)*(1f-.65f*eqSolsticeDry)*(1f-.70f*harmattanCap)
                                *(.20f*(float)Math.Exp(-Math.Pow(ab/12.5f,2))+.12f*Math.Max(0f,glebaImpact)*(float)Math.Exp(-Math.Pow(ab/20f,2))*(1f-mappedCont))
                                +seaBreezeBoost;

                            // Bergeron-Findeisen ice-phase stratiform condensation in cold non-monsoon boreal interiors (reduces Dfc -> Dwc without wetting East Asian winter outflow)
                            float nonEastMonsoon=1f-FMath.Clamp(3.5f*sector.EasternMargin+2.0f*sector.Monsoon,0f,1f);
                            float icePhase=oceanContrast*(1f-ocean[i])*nonEastMonsoon*ExoPlaSimSupplement.SmoothStep(52f,61f,ab)
                                *ExoPlaSimSupplement.SmoothStep(-4f,-16f,tempNow)*coolSeason;
                            float coldStratBoost=1f+.22f*icePhase;

                            lift[i]=lift[i]*combinedSuppress*eqItczBoost*coldStratBoost;
                            rainEfficiency[i]=Math.Max(.04f,rainEfficiency[i]*combinedSuppress*eqItczBoost*coldStratBoost);
                            column.Rainout[i]*=(1f-.42f*shadowDry-.42f*hyperAridCore-.28f*desertMarginLid-.80f*somaliDivergence-.32f*subtropPlateau-.30f*harmattanCap)
                                *(1f+.24f*seaBreeze*warmBreeze*breezeSeason);

                            // Western-Pacific typhoon moisture organization strictly on open Pacific margin (`sector.EasternMargin`), never Bay of Bengal -> Deccan!
                            float westernBasinGate=FMath.Clamp(3.0f*sector.EasternMargin,0f,1f);
                            float typhoonSeason=ExoPlaSimSupplement.SmoothStep(.28f,.72f,.75f*summer+.45f*warmSea);
                            float warmPoolMpi=ocean[i]*westernBasinGate*typhoonSeason*ExoPlaSimSupplement.SmoothStep(26.4f,28.6f,tempNow)*coriolisGenesis*(1f-.85f*somaliDivergence);
                            column.OrganizedFraction[i]=FMath.Clamp(column.OrganizedFraction[i]+.10f*oceanContrast*warmPoolMpi*receivingLand,0f,1f);

                            column.StratiformCritRh[i]=FMath.Clamp(.90f+.055f*hyperAridCore+.035f*desertMarginLid+.055f*blockedSteppe+.050f*foehnLee+.040f*subtropPlateau-.055f*icePhase-.035f*towerHere,.82f,.97f);
                            float extraDry=.30f*sthzDry+.52f*inlandDry+.40f*hyperAridCore+.30f*desertMarginLid+.36f*shadowDry+.35f*subtropPlateau+.30f*harmattanCap+1.15f*somaliDivergence;
                            column.DryLayer[i]=FMath.Clamp((column.DryLayer[i]+extraDry)*(1f-.40f*warmMarginCyclones-.42f*gulfCool)*(1f-.52f*towerHere)*(1f-.30f*seaBreeze*warmBreeze*breezeSeason),.08f,2.05f);
                        }

                        // Seasonal heating grows the continental mixed layer. A subsiding
                        // high inhibits thermals even when the surface is warm.
                        if (seasonalWinds)
                        {
                            float seasonalHeating = FMath.Clamp((tempNow - annualTemperature[i]) / 12f, 0f, 1f);
                            float warmBoundaryLayer = FMath.Clamp((tempNow - 10f) / 12f, 0f, 1f);
                            float inhibition = FMath.Clamp(-2f * glebaImpact, 0f, 1f);
                            column.LandThermalActivity[i] = seasonalHeating * warmBoundaryLayer * (1f - inhibition);
                        }

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
                    column.DisturbanceFaces[i]=new TransportWeights {
                        Center=1-stormWest[i]-stormPoleward[i],East=stormWest[e],
                        South=summerPole>0?southArea*stormPoleward[s]:0,
                        North=summerPole<0?northArea*stormPoleward[n]:0
                    };
                }
            });
            IntegrateYear(state,months,dt,water,area,inverseFieldWater,westCell,eastCell,southCell,northCell,options,observeFlux);
            if(exo!=null)
                ExoPlaSimSupplement.FinalizeDiagnostics(state,ocean,water,elevation,monthlyTemperature,planetCfg,options);
            return state;
        }

        static void IntegrateYear(FlatMoistureState state,MonthlyTransport[] months,float dt,
            float[] water,float[] area,float[] inverseFieldWater,int[] west,int[] east,int[] south,int[] north,
            ParallelOptions options,MonthlyFluxObserver observeFlux)
        {
            int count=water.Length, steps=(int)(30/dt);
            var exo=state.ExoPlaSim;
            var donorSum=observeFlux==null ? null : new double[count];
            var mobileSum=observeFlux==null ? null : new double[count];
            var q=new float[count];var next=new float[count];
            var shallowSoil=new float[count];var deepSoil=new float[count];
            var mobile=new float[count];var nextMobile=new float[count];
            int bands=Math.Max(1,state.Height/8);
            var incoming=new double[bands];var outgoing=new double[bands];
            // Start empty, with ocean and lake evaporation supplying the cycle.
            // Retain ambient vapour, organized moisture and both soil reservoirs through two spin-up
            // years and the reported third year, including the December-to-January boundary.
            for(int visit=0;visit<36;visit++)
            {
                int month=visit%12;bool record=visit>=24;
                var column=months[month];
                double initial=0,external=0,export=0;
                if(record)for(int i=0;i<count;i++)initial+=((double)q[i]+shallowSoil[i]+deepSoil[i]+mobile[i])*area[i];
                Array.Clear(incoming,0,bands);Array.Clear(outgoing,0,bands);
                if (record && donorSum!=null) {Array.Clear(donorSum,0,count);Array.Clear(mobileSum,0,count);}
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
                        var moving=column.DisturbanceFaces[i];
                        float carried=mobile[i]*moving.Center+mobile[east[i]]*moving.East
                            +mobile[south[i]]*moving.South+mobile[north[i]]*moving.North;
                        float released=carried*column.Release[i];
                        float stormCloud=released*column.Rainout[i];
                        float cap=column.Capacity[i], sea=water[i], land=1-sea;
                        float totalSoil=shallowSoil[i]+deepSoil[i];
                        float humidity=FMath.Clamp((air+carried)/cap,0,1);
                        float evaporation=column.PotentialEvaporation[i]*(.60f+1.15f*(1-humidity));
                        float openWaterEvap=sea*Math.Max(0,evaporation+column.PotentialEvaporation[i]*column.SeaHumidityContrast[i]);
                        openWaterEvap*=column.WindExchange[i];
                        float soilAvail=Math.Min(1f,(totalSoil+0.18f*column.SeaBreezeResilience[i]*deepSoil[i])*inverseFieldWater[i]);
                        float landEvap=Math.Min(totalSoil,evaporation*column.Transpiration[i]*land*soilAvail);
                        float skinDraw=Math.Min(shallowSoil[i],landEvap*(0.55f+0.25f*(1f-column.SeaBreezeResilience[i])));
                        float rootDraw=Math.Min(deepSoil[i],landEvap-skinDraw);
                        skinDraw=Math.Min(shallowSoil[i],landEvap-rootDraw);
                        landEvap=skinDraw+rootDraw;
                        float organized=column.OrganizedFraction[i]*(openWaterEvap+air*dt/2);
                        nextMobile[i]=carried-released+organized;
                        air+=openWaterEvap+landEvap+released-organized;
                        float cloudFraction=FMath.Clamp((humidity-.40f)*2,0,1);
                        float critRh=column.StratiformCritRh[i]>0f?column.StratiformCritRh[i]:.90f;
                        float largeScaleExcess=Math.Max(0,air-cap*critRh)*.65f;
                        float dynCond=air*Math.Min(.7f,(column.Lift[i]+column.Efficiency[i]*.055f*cloudFraction*cloudFraction)*dt);
                        // Moist thermals return a share of actual land evapotranspiration to
                        // convection. Credit convection already diagnosed from resolved lift.
                        float humidLayer = FMath.Clamp((humidity - .35f) / .4f, 0f, 1f);
                        float thermalMoisture = landEvap * column.LandThermalActivity[i] * humidLayer;
                        float landConvection = Math.Max(0f, thermalMoisture - dynCond * column.ConvectiveShare[i]);
                        float condensed=largeScaleExcess+dynCond+landConvection;
                        float gross=Math.Min(air,condensed+stormCloud);
                        float skinMax=Math.Max(1e-4f,30f*land), deepMax=Math.Max(1e-4f,120f*land);
                        float wettedSurface=sea+land*Math.Min(1,totalSoil*inverseFieldWater[i]);
                        float virga=SubcloudEvaporation(gross,air,cap,dt,wettedSurface,column.DryLayer[i]);
                        float rain=gross-virga;
                        next[i]=air-rain;
                        // Exact mass-conserving 2-layer soil percolation (30 mm shallow skin + 120 mm deep root zone = 150 mm total;
                        // gravity drainage only enters deep pore space so runoff strictly occurs when total profile exceeds 150 mm!)
                        float nextShallow=shallowSoil[i]-skinDraw+rain*land;
                        float baseDeep=deepSoil[i]-rootDraw;
                        float deepRoom=Math.Max(0f,120f*land-baseDeep);
                        float overflow=Math.Max(0f,nextShallow-30f*land);
                        float gravityDrain=Math.Min(deepRoom,Math.Min(Math.Max(0f,nextShallow-overflow),0.12f*dt*Math.Max(0f,nextShallow-overflow)));
                        float perc=overflow+gravityDrain;
                        shallowSoil[i]=nextShallow-perc;
                        float nextDeep=baseDeep+perc;
                        float runoff=Math.Max(0f,nextDeep-120f*land);
                        deepSoil[i]=nextDeep-runoff;
                        if(record)
                        {
                            added+=openWaterEvap*area[i];removed+=(rain*sea+runoff)*area[i];
                            int a=i*12+month;
                            state.Humidity[a]+=Math.Min(1,(next[i]+nextMobile[i])/cap)/steps;
                            state.Rain[a]+=rain;
                            state.Condensation[a]+=gross;
                            if(exo!=null)
                            {
                                float convRaw=landConvection+dynCond*column.ConvectiveShare[i]+stormCloud*(.35f+.65f*column.ConvectiveShare[i]);
                                float convFrac=gross>1e-6f?FMath.Clamp(convRaw/(condensed+stormCloud+1e-6f),0f,1f):0f;
                                exo.ConvectiveRain[a]+=rain*convFrac;
                                exo.StratiformRain[a]+=rain*(1f-convFrac);
                                exo.Snowfall[a]+=rain*column.SolidSnowFrac[i];
                                exo.VirgaReevap[a]+=virga;
                                exo.ShallowSoil[a]+=(land>1e-4f?FMath.Clamp(shallowSoil[i]/skinMax,0f,1f):1f)/steps;
                                exo.DeepSoil[a]+=(land>1e-4f?FMath.Clamp(deepSoil[i]/deepMax,0f,1f):1f)/steps;
                            }
                        }
                    }
                    incoming[band]+=added;outgoing[band]+=removed;
                }
                Action<int> advance=Advance;
                for(int step=0;step<steps;step++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    if (record && donorSum!=null)
                        for (int i=0;i<count;i++) { donorSum[i]+=q[i]; mobileSum[i]+=mobile[i]; }
                    if(options.MaxDegreeOfParallelism==1 || bands==1)
                        for(int band=0;band<bands;band++)advance(band);
                    else Parallel.For(0,bands,options,advance);
                    var swap=q;q=next;next=swap;
                    var previousMobile=mobile;mobile=nextMobile;nextMobile=previousMobile;
                }
                if(!record)continue;
                if (donorSum!=null)
                    ReportFlux(month,state.Width,state.Height,column.Faces,donorSum,steps,dt,observeFlux,column.DisturbanceFaces,mobileSum);
                for(int band=0;band<bands;band++){external+=incoming[band];export+=outgoing[band];}
                double final=0;
                for(int i=0;i<count;i++)
                {
                    final+=((double)q[i]+shallowSoil[i]+deepSoil[i]+mobile[i])*area[i];
                    state.Condensation[i*12+month]/=30;
                }
                state.BudgetError[month]=Math.Abs(initial+external-export-final)/Math.Max(1,initial+external);
            }
        }

        // Donor water is kg/m² and gather coefficients already include the ratio of
        // donor to receiver area. A shared face carries one signed mass transfer.
        internal static void ReportFlux(int month,int width,int height,TransportWeights[] faces,
            double[] donorSum,int steps,float dt,MonthlyFluxObserver observe,
            TransportWeights[] mobileFaces=null,double[] mobileSum=null)
        {
            var east=new double[donorSum.Length];
            var north=new double[donorSum.Length];
            AddFaceFlux(width,height,faces,donorSum,steps,dt,east,north);
            if(mobileFaces!=null) AddFaceFlux(width,height,mobileFaces,mobileSum,steps,dt,east,north);
            observe(month,width,height,east,north);
        }

        static void AddFaceFlux(int width,int height,TransportWeights[] faces,double[] donorSum,
            int steps,float dt,double[] east,double[] north)
        {
            const double radius=6371008.8;
            double dLon=2*Math.PI/width,dLat=Math.PI/height;
            double meridian=radius*dLat,seconds=steps*dt*86400.0;
            for (int y=0;y<height;y++)
            {
                double low=-Math.PI/2+y*dLat,high=low+dLat;
                double area=radius*radius*dLon*(Math.Sin(high)-Math.Sin(low));
                double nextArea=radius*radius*dLon*(Math.Sin(high+dLat)-Math.Sin(high));
                double parallel=radius*dLon*Math.Cos(high);
                for (int x=0;x<width;x++)
                {
                    int i=y*width+x,e=y*width+(x+1)%width;
                    east[i]+=area*(donorSum[i]*faces[e].West-donorSum[e]*faces[i].East)/(meridian*seconds);
                    if (y+1==height) continue;
                    int n=i+width;
                    north[i]+=(donorSum[i]*faces[n].South*nextArea-donorSum[n]*faces[i].North*area)/(parallel*seconds);
                }
            }
        }
    }
}
