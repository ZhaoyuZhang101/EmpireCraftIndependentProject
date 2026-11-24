Shader "ORBIS/FlatWorld/SkyBake"
{
 Properties
 {
  _ClimateTex("Monthly precipitation and temperature",2D)="black"{}
  _ClimateWindTex("Climate wind",2D)="black"{}
  _RegionalCloudResponse("Regional moist, dry and convective response",2D)="black"{}
  _CloudSynopticTex("Gleba synoptic lift, ITCZ, storm track, moisture",2D)="black"{}
  _CloudSynopticAuxTex("Gleba marine Sc, orographic, rain shadow, polar front",2D)="black"{}
  _UpperWindTex("Gleba Rossby jet and anvil wind",2D)="black"{}
  [HideInInspector] _GlebaCloudEnable("Enable Gleba synoptic cloud physics",Float)=0
  [HideInInspector] _GlebaCirculationParams("Hadley rad, Polar rad, Rossby k, Regime",Vector)=(0.5236,1.0821,2.6,0)
  [HideInInspector] _GlebaForcingPoleSouth("South pole forcing",Vector)=(0.35,0,0.25,0.35)
  [HideInInspector] _GlebaForcingPoleNorth("North pole forcing",Vector)=(0.35,0,0.25,0.35)
  [HideInInspector] _RegionalCloudPoles("South and north cloud responses",Vector)=(0,0,0,0)
  [HideInInspector] _CycloneNorth("Northern pressure centre and wind strength",Vector)=(0,0,0,0)
  [HideInInspector] _CycloneSouth("Southern pressure centre and wind strength",Vector)=(0,0,0,0)
  [HideInInspector] _CyclonePhase("Simulated circulation phase in radians",Vector)=(0,0,0,0)
 }
 SubShader
 {
  Cull Off ZWrite Off ZTest Always
  Pass
  {
   Name "CLIMATE_CLOUD_CACHE_PERF32"
   CGPROGRAM
   #pragma vertex vert_img
   #pragma fragment frag
   #pragma target 3.5
   #include "UnityCG.cginc"
   #include "FlatCycloneWind.cginc"
   sampler2D _ClimateTex,_ClimateWindTex,_RegionalCloudResponse;
   sampler2D _CloudSynopticTex,_CloudSynopticAuxTex,_UpperWindTex;
   float4 _RegionalCloudResponse_TexelSize,_RegionalCloudPoles;
   float _VisualTime,_MonthBlend,_ClimateRows,_GlebaCloudEnable;
   float4 _CyclonePhase,_GlebaCirculationParams;
   float4 _ClimatePoleSouth,_ClimatePoleNorth,_GlebaForcingPoleSouth,_GlebaForcingPoleNorth;
   float Hash3(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
   float Corner(float3 cell,float3 offset)
   {
    float id=floor(12*Hash3(cell));
    float2 gradient=2*float2(fmod(id,2),fmod(floor(id*.5),2))-1;
    float2 axes=id<4?offset.xy:(id<8?offset.xz:offset.yz);
    float weight=max(0,.6-dot(offset,offset));
    weight*=weight;
    return weight*weight*dot(gradient,axes);
   }
   // A smooth 3D simplex field has no faces parallel to a latitude or a map tile.
   float Noise3(float3 p)
   {
    float3 cell=floor(p+dot(p,float3(1,1,1))/3);
    float3 x=p-cell+dot(cell,float3(1,1,1))/6;
    float3 order=step(x.yzx,x),other=1-order;
    float3 a=min(order,other.zxy),b=max(order,other.zxy);
    return saturate(.5+16*(Corner(cell,x)+Corner(cell+a,x-a+1.0/6)
            +Corner(cell+b,x-b+1.0/3)+Corner(cell+1,x-.5)));
   }
   float3 Turn(float3 p)
   {return float3(dot(p,float3(.36,.48,-.80)),dot(p,float3(-.80,.60,0)),dot(p,float3(.48,.64,.60)));}
   float Fbm(float3 p)
   {
    float a=Noise3(p)*.57;
    p=Turn(p)*2.07+float3(17,3,11);a+=Noise3(p)*.29;
    return a+Noise3(Turn(p)*2.13+float3(4,29,5))*.14;
   }
   float3 RotateY(float3 p,float angle)
   {float s=sin(angle),c=cos(angle);return float3(c*p.x+s*p.z,p.y,-s*p.x+c*p.z);}
   void Cyclone(float3 p,float4 centre,float spin,float rotation,float2 steering,inout float4 weather)
   {
    if(centre.w<=.035)return;
    float3 axis=centre.xyz;
    float extent=FlatCycloneRadius(centre.w);
    float radius=length(p-axis)/extent;
    if(radius>=1.36)return;
    float3 east=normalize(cross(float3(0,1,0),axis));
    float3 north=cross(axis,east);
    float2 local=float2(dot(p,east),dot(p,north))/extent;
    float bearing=atan2(local.y,local.x+1e-7);
    float organized=smoothstep(.16,.68,centre.w);
    float2 downwind=steering*rsqrt(max(.04,dot(steering,steering)));
    float bearingInStorm=bearing-rotation;
    float2 body=float2(cos(bearingInStorm),sin(bearingInStorm))*radius;
    float3 seed=axis*3.1+float3(7,19,3);
    float distort=Noise3(float3(body*3.5,1)+seed)-.5;
    float r=radius*(1+.18*distort)-.08*dot(local,downwind);

    // Inverse advection winds convective cells into trailing logarithmic spiral rainbands
    // (exact hemispheric chirality: counter-clockwise in NH, clockwise in SH).
    float turn=lerp(.82,3.25,organized)*log(1+4.7*max(r,0));
    float phase=bearingInStorm+spin*turn+.78*distort;
    float2 flow=float2(cos(phase),sin(phase))*r;
    float cells=Fbm(float3(flow*5.8,1)+seed);
    float billows=Noise3(float3(body*21,3)+seed);

    float mainArm=smoothstep(.02,.92,cos(phase+1.45*(cells-.5)));
    float secondaryArm=smoothstep(.18,.94,cos(2.0*phase+1.10+1.35*(cells-.5)))*(1-smoothstep(.52,.98,r))*.82;
    float feeder=smoothstep(.24,.95,cos(phase+2.53+.48*sin(5.6*r)+2.2*(cells-.5)));
    feeder*=smoothstep(.20,.72,cells)*(.92-.34*smoothstep(.25,.88,r));
    float squalls=smoothstep(.44,.76,cells*.68+billows*.32);
    float bands=max(max(mainArm*lerp(.52,1.04,cells),secondaryArm),feeder)+.22*squalls;

    // Intense Central Dense Overcast (CDO) eyewall ring + secondary eyewall moat + crisp pinpoint eye
    float maturity=smoothstep(.46,.78,centre.w);
    float innerWall=(1-smoothstep(.11,.32,r))*(.86+.18*billows);
    float outerRing=exp(-pow((r-.24)/.055,2))*maturity*.72*(.75+.25*cells);
    float wall=max(innerWall,outerRing);
    float eye=lerp(1,smoothstep(.020,.076,r+.010*distort),maturity);

    float bodyCover=lerp(saturate(cells*.8+.28*billows),max(wall,bands),organized);
    float low=eye*saturate(.035+bodyCover*(.85+.22*billows));
    float convection=eye*saturate(max(wall*maturity*.98,bodyCover*(.40+.86*centre.w)));
    float stormCore=eye*smoothstep(.40,.82,convection);

    float lowBoundary=1-smoothstep(.58,1.16,r+.12*distort);
    float lowInfluence=lowBoundary*smoothstep(.035,.44,centre.w);

    // Anticyclonic upper-tropospheric cirrus outflow canopy & transverse radial cirrus ribs
    float icePhase=phase-spin*(1.12+.52*r)+2.4*(cells-.5);
    float filaments=pow(.5+.5*cos(3.0*icePhase),2.6);
    float radialRibs=pow(.5+.5*cos(14.0*bearingInStorm-spin*4.2*r+3.5*distort),2.2)*smoothstep(.14,.55,r);
    float shield=1-smoothstep(.38,1.08,r);
    float upper=eye*saturate((.10+.38*filaments+.22*radialRibs+.42*shield)*(1+.26*dot(local,downwind))*(.55+.55*organized));
    float outBoundary=1-smoothstep(.72,1.34,r+.10*distort);
    float outInfluence=outBoundary*smoothstep(.035,.46,centre.w);

    weather.rb=lerp(weather.rb,float2(low,.18+.82*convection),lowInfluence);
    weather.a=lerp(weather.a,stormCore,lowInfluence);
    weather.g=lerp(weather.g,max(weather.g*.35,upper),outInfluence);
   }
   struct EraMorphology
   {
    float planetary,baseTex,flowBands,mesoCells,fineGrain,curvedBand,curvedWisp,tradeStreets,honeycomb,mcsCluster,anvilShield,frontalLow,frontalMid,frontalDry,postFrontCells,wcbCirrus;
   };
   EraMorphology SampleEraMorphology(
    float eraId,float age,float3 radial,float longitude,float latitude,
    float3 east,float3 north,float3 velocity,float3 windAxis,float windSpd,float polarLatRad,float rossbyK,float3 jetAxis,float3 wander)
   {
    EraMorphology o;
    float3 p=normalize(radial-velocity*age*7.2);
    float3 seed=float3(Hash3(float3(eraId,4,7)),Hash3(float3(eraId,11,3)),Hash3(float3(eraId,5,19)))*15.0;

    // Two-stage divergence-free spherical curl-streamfunction advection ("cream-in-coffee" geophysical fluid swirl!):
    // Stronger planetary-scale swirl (0.24) + synoptic eddy curl (0.095) bends cloud sheets into authentic EPIC
    // sweeping C-arcs, S-ribbons & hooked cyclonic eddies instead of static fragmented patches!
    float3 psi1=float3(
     Fbm(p*1.95+wander*.50+seed+17.0),
     Fbm(p*1.95+wander*.50+seed+43.0),
     Fbm(p*1.95+wander*.50+seed+71.0))-.5;
    float3 curl1=cross(p,psi1);
    float3 p1=normalize(p+curl1*.24);

    float3 psi2=float3(
     Noise3(Turn(p1)*4.4+seed+11.0),
     Noise3(Turn(p1)*4.4+seed+29.0),
     Noise3(Turn(p1)*4.4+seed+53.0))-.5;
    float3 curl2=cross(p1,psi2);
    float3 pFlow=normalize(p1+curl2*.095);

    // 1. Multi-scale TRUE spherical flow-combed fields (3-tap tangent LIC along local climate wind!).
    float3 flowDir=normalize(lerp(windAxis,jetAxis,.42)+float3(1e-5,0,0));
    float licStep=lerp(.016,.032,windSpd);
    float3 pFlowP=normalize(pFlow+flowDir*licStep);
    float3 pFlowM=normalize(pFlow-flowDir*licStep);
    float planetary=Fbm(p1*1.75+wander*.46+seed*.7+float3(3.1,7.4,1.9));
    float synoptic=(Noise3(Turn(pFlow)*4.2+wander*.75+seed)+
                    Noise3(Turn(pFlowP)*4.2+wander*.75+seed)+
                    Noise3(Turn(pFlowM)*4.2+wander*.75+seed))*(1.0/3.0);
    float3 pWindP=normalize(pFlow+windAxis*(licStep*.95));
    float3 pWindM=normalize(pFlow-windAxis*(licStep*.95));
    float flowBands=(Noise3(Turn(pFlow)*15.5+wander+seed*1.4)+
                     Noise3(Turn(pWindP)*15.5+wander+seed*1.4)+
                     Noise3(Turn(pWindM)*15.5+wander+seed*1.4))*(1.0/3.0);
    float mesoCells=Fbm(Turn(pFlow)*34.0+seed*1.9);
    float fineGrain=Noise3(Turn(pFlow)*64.0+seed*2.7)*.52+
                    Noise3(Turn(normalize(pFlow+jetAxis*.016))*64.0+seed*2.7)*.48;

    // 1b. Geostrophic Baroclinic Sweeping Fluid Bands (continuous planetary-scale curved sheets & tapered ribbons!):
    // Lower longitudinal k (1.35) + strong planetary curl1 (0.36) creates continuous 3,000-6,000 km sweeping arcs
    // with broad translucent vapor aprons surrounding brighter fibrous spines (kills disconnected dandruff scraps!).
    float eqWeight=smoothstep(.04,.44,length(radial.xz));
    float yStretch1=lerp(1.85,5.80,eqWeight);
    float yStretch2=lerp(2.20,8.20,eqWeight);
    float3 pWave=normalize(p+curl1*.36+curl2*.075);
    float hemiDiag=.28*(latitude/(abs(latitude)+.30));
    float baroY=pWave.y+hemiDiag*sin(longitude*2.0+psi1.x*2.4)*eqWeight*.18+.13*psi1.y;
    float3 qBand1=float3(pWave.x*1.35,baroY*yStretch1,pWave.z*1.35);
    float3 qBand2=float3(pWave.x*2.55,baroY*yStretch2,pWave.z*2.55);
    float bandField1=Fbm(qBand1+wander*.40+seed+float3(13.4,27.1,5.8));
    float bandField2=Noise3(qBand2+wander*.56+seed+float3(47.2,11.8,19.4))*.60+
                     Noise3(qBand2*1.85+wander*.68+seed+float3(19.1,5.7,31.4))*.40;
    float bandH=bandField1*.54+bandField2*.26+flowBands*.13+mesoCells*.04+fineGrain*.03;
    float bandVeil=smoothstep(.34,.74,bandH);
    float bandSpine=smoothstep(.52,.84,bandH*.72+flowBands*.18+fineGrain*.10);
    float ribbonSilk=.42+.58*smoothstep(.16,.84,flowBands*.58+fineGrain*.42);
    o.curvedBand=saturate(bandVeil*.56+bandSpine*.54*ribbonSilk);
    o.curvedWisp=saturate((bandVeil*.68+bandSpine*.48)*ribbonSilk);

    // 2. Regime A: EPIC Subtropical Marine Cumulus Speckling, Trade-Wind Streaks & Fine Cellular Lace
    // (ultra-fine 15-40 km cellular sea-foam & streaky trade fields across Central/East Pacific, Baja & Peru!)
    float streetRipple=(Noise3(Turn(pFlow)*58.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow+windAxis*.018))*58.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow-windAxis*.018))*58.0+seed+5.0))*(1.0/3.0);
    float microCell1=Noise3(Turn(pFlow)*136.0+seed+13.0);
    float microCell2=Noise3(Turn(pFlow)*218.0+seed+31.0);
    float fineSpeckle=smoothstep(.42,.74,microCell1*.54+microCell2*.46);
    float fineOpenFoam=smoothstep(.44,.76,abs(2.0*microCell2-1.0)*.58+microCell1*.42);
    float streetEnv=smoothstep(.24,.64,planetary*.42+synoptic*.32+bandField1*.26);
    o.tradeStreets=saturate(planetary*.22+synoptic*.18+streetRipple*.24+max(fineSpeckle,fineOpenFoam*.85)*.46*streetEnv);

    float cellClosed=1.0-abs(2.0*Noise3(Turn(pFlow)*62.0+seed)-1.0);
    float cellFine=1.0-abs(2.0*Noise3(Turn(pFlow)*132.0+seed+9.0)-1.0);
    float cellMicro=smoothstep(.36,.74,microCell2*.62+microCell1*.38);
    float scMacro=planetary*.32+synoptic*.24+bandField1*.16;
    o.honeycomb=saturate(scMacro+pow(cellClosed,1.15)*.12+cellFine*.13+cellMicro*.18);

    o.planetary=planetary;
    o.flowBands=flowBands;
    o.mesoCells=mesoCells;
    o.fineGrain=fineGrain;
    o.baseTex=planetary*.32+synoptic*.30+bandField1*.20+flowBands*.12+mesoCells*.04+fineGrain*.02;

    // 3. Regime B: Slender, Sinuous Continuous Pacific ITCZ Line + Sweeping Diagonal SPCZ Multi-Strand Ribbon
    // studded with tight dazzling-white Cb hot-towers & long downwind brushed-silk cirrus anvil plumes!
    float3 qItcz=float3(pWave.x*1.35,(pWave.y+hemiDiag*.22*sin(longitude*1.8+psi1.x*2.0)+.07*psi1.z)*lerp(2.2,9.2,eqWeight),pWave.z*1.35);
    float itczField=Fbm(qItcz+wander*.44+seed+float3(5.3,1.7,8.9));
    float itczRibbon=(Noise3(Turn(pFlow)*15.0+wander*.65+seed+float3(5.3,1.7,8.9))+
                      Noise3(Turn(pFlowP)*15.0+wander*.65+seed+float3(5.3,1.7,8.9))+
                      Noise3(Turn(pFlowM)*15.0+wander*.65+seed+float3(5.3,1.7,8.9)))*(1.0/3.0);
    float itczSlender=smoothstep(.38,.74,itczField*.64+itczRibbon*.26+fineGrain*.10);
    float hotTowers=smoothstep(.38,.76,mesoCells*.46+microCell1*.34+Noise3(Turn(pFlow)*82.0+seed+21.0)*.20);
    float mcsEnv=smoothstep(.22,.70,max(itczSlender*1.04,planetary*.24+synoptic*.28+itczRibbon*.48));
    o.mcsCluster=saturate(mcsEnv*(.38+.62*hotTowers));
    float3 pAnvilNear=normalize(pFlow-jetAxis*.058);
    float3 pAnvilFar=normalize(pFlow-jetAxis*.155);
    float anvilNear=Fbm(pAnvilNear*4.6+wander*.65+seed);
    float anvilFar=Fbm(pAnvilFar*2.9+wander*.55+seed);
    float anvilSilk=(Noise3(Turn(pAnvilNear)*28.0+seed+4.0)+
                     Noise3(Turn(normalize(pAnvilNear+jetAxis*.024))*28.0+seed+4.0)+
                     Noise3(Turn(normalize(pAnvilNear-jetAxis*.024))*28.0+seed+4.0))*(1.0/3.0);
    o.anvilShield=smoothstep(.28,.70,anvilNear*.40+anvilFar*.32+anvilSilk*.28)*(.38+.62*mcsEnv);

    // 4. Regime C: EPIC Extratropical Sweeping 5,500 km Curved Frontal Banners + Bold 2.4-Turn Spiral Comma Cyclones
    // + Peppered Open-Cell Cold-Air Cumulus ("fishnet foam speckle" curling behind every cold front in DSCOVR EPIC!)
    float hemiSign=latitude>=0?1.0:-1.0;
    float3 hSeed=seed+(latitude>=0?float3(0,0,0):float3(19.7,41.3,13.9));
    // Rossby wavenumber 3-4 across mid-latitudes places 2-3 dramatic linked cyclones across every visible hemisphere!
    float k1=max(2.0,floor(rossbyK+.35+.5));
    float k2=k1+1.0;
    float eraLon1=6.2831853*Hash3(float3(eraId,9.0+hemiSign*2.0,2));
    float eraLon2=6.2831853*Hash3(float3(eraId,15.0+hemiSign*3.0,6));
    float packPhase=6.2831853*Hash3(float3(eraId,23.0+hemiSign*5.0,11));

    float packetWave=.58*cos(longitude-.35*age+packPhase)+.42*cos(2.0*longitude+.22*age+eraLon2);
    float packetEnv=smoothstep(-.48,.42,packetWave+(planetary-.5)*1.05);

    // Center storm belt right in visible mid-latitudes (~42-48 deg) so Southern Ocean & North Pacific spirals shine!
    float stormLatRad=clamp(polarLatRad*.84,.58,.86);
    float theta=k1*longitude-.88*age+eraLon1+.58*sin(k2*longitude-1.12*age+eraLon2)+.30*sin(longitude+hSeed.x);
    float waveS=sin(theta),waveC=cos(theta);
    float waveFade=smoothstep(-.88,-.06,waveC)*(.42+.58*packetEnv);
    float dx=atan2(waveS,waveC)/max(1.4,k1+.15)*max(.26,cos(latitude));
    float dy=(abs(latitude)-stormLatRad-.115*sin(k1*longitude-.42*age+hSeed.y)-.055*cos(k2*longitude+hSeed.z));

    // Boldly wound 2.4-turn spiral occluded comma cyclone with dramatic dark-blue dry-intrusion channel
    // curling deep into the vortex eye (matches DSCOVR EPIC Southern Ocean & Aleutian/North Pacific storms!)
    float2 vortVec=float2(dx+.055+.035*(synoptic-.5),dy-.050+.028*(planetary-.5));
    float rVort=length(vortVec)/lerp(.32,.46,saturate(3.4-k1));
    float vortAng=atan2(vortVec.y,vortVec.x+1e-6)+4.45*log(1.0+7.8*rVort)+.65*(synoptic-.5)+.35*(flowBands-.5);
    float spiralWave=cos(vortAng);
    float spiralPrimary=smoothstep(-.18,.68,spiralWave);
    float spiralSecondary=smoothstep(.12,.84,cos(2.0*vortAng+1.15))*.72;
    float commaCollar=exp(-pow((rVort-.17)/.10,2.0))*.78;
    float commaHead=(1.0-smoothstep(.60,1.40,rVort))*smoothstep(.026,.095,rVort)*max(max(spiralPrimary,spiralSecondary),commaCollar);
    // Coiled dark-blue dry-intrusion channel carving cleanly through polar & mid-latitude cloud blankets!
    float spiralDryTongue=smoothstep(.02,.68,-spiralWave)*(1.0-smoothstep(.18,1.22,rVort))*smoothstep(.018,.085,rVort);
    float vortexEyeClear=(1.0-smoothstep(.018,.080,rVort))*.92;

    // Sweeping 5,500 km curved cold-front banner reaching equatorward to ~24 deg with 2-octave combed silk striations
    float3 frontTangent=normalize(east*.68+north*hemiSign*.73+1e-5);
    float frontSilkA=(Noise3(Turn(pFlow)*24.0+hSeed)+
                      Noise3(Turn(normalize(pFlow+frontTangent*.028))*24.0+hSeed)+
                      Noise3(Turn(normalize(pFlow-frontTangent*.028))*24.0+hSeed))*(1.0/3.0);
    float frontSilkB=(Noise3(Turn(pFlow)*54.0+hSeed+7.0)+
                      Noise3(Turn(normalize(pFlow+frontTangent*.018))*54.0+hSeed+7.0))*.5;
    float frontFilament=frontSilkA*.62+frontSilkB*.38;

    float dxFront=-.01+.95*(dy-.04)-.42*(dy-.04)*abs(dy-.04);
    float dFront=dx-dxFront+.038*(synoptic-.5)+.022*(frontFilament-.5);
    float coldFront=exp(-pow(dFront/.074,2.0))*smoothstep(-.54,-.18,dy)*(1.0-smoothstep(.14,.32,dy))*(.70+.44*frontFilament);
    float coldFrontApron=exp(-pow((dFront-.048)/.152,2.0))*smoothstep(-.52,-.14,dy)*(1.0-smoothstep(.18,.38,dy))*.64*(.54+.46*frontFilament);

    float dWarm=(dy-(.072-.52*max(0.0,dx+.02)))+.030*(synoptic-.5);
    float warmFront=exp(-pow(dWarm/.090,2.0))*smoothstep(-.10,.04,dx)*(1.0-smoothstep(.28,.52,dx))*(.66+.44*frontFilament);

    float wcb=smoothstep(-.03,.11,dFront)*(1.0-smoothstep(.12,.42,dFront))*smoothstep(-.34,.22,dy)*(1.0-smoothstep(.24,.46,dy));
    float drySlot=exp(-pow((dFront+.092)/.078,2.0))*smoothstep(-.46,-.04,dy)*(1.0-smoothstep(.14,.34,dy));

    // Signature NASA DSCOVR EPIC / Apollo 17 Post-Frontal Open-Cell Cold-Air Cumulus ("lacy fishnet foam" behind cold front!)
    float coldWake=smoothstep(.04,.18,-dFront)*(1.0-smoothstep(.38,.76,-dFront))*smoothstep(-.48,.24,dy);
    float wakeLoop=saturate(coldWake+spiralDryTongue*.82);
    float openCellRim=smoothstep(.40,.76,abs(2.0*Noise3(Turn(pFlow)*138.0+hSeed)-1.0));
    float openCellMicro=smoothstep(.38,.74,microCell2*.62+microCell1*.38);
    float openCellPatch=smoothstep(.20,.64,Noise3(Turn(pFlow)*22.0+hSeed+17.0));
    float postCells=wakeLoop*(openCellRim*.64+openCellMicro*.36)*(.52+.48*openCellPatch)*.96;

    o.frontalLow=waveFade*saturate(max(max(commaHead*(.92+.16*frontFilament),max(coldFront,coldFrontApron)),warmFront*.94)+.58*wcb*(.56+.44*frontFilament));
    o.postFrontCells=waveFade*postCells;
    o.frontalMid=waveFade*saturate(warmFront+.84*wcb+commaHead*.68+coldFrontApron*.55);
    o.frontalDry=waveFade*saturate(max(drySlot,max(spiralDryTongue*.96,vortexEyeClear)));
    o.wcbCirrus=waveFade*saturate(wcb*(.76+.34*frontFilament)+commaHead*.92+warmFront*.78+coldFront*.72+coldFrontApron*.62);
    return o;
   }
   float4 frag(v2f_img i):SV_Target
   {
    float4 monthly=tex2D(_ClimateTex,i.uv);
    float poleBlend=_ClimateRows>0?smoothstep(0,1,min(i.uv.y,1-i.uv.y)*_ClimateRows):1;
    if(_ClimateRows>0)monthly=lerp(lerp(_ClimatePoleSouth,_ClimatePoleNorth,step(.5,i.uv.y)),monthly,poleBlend);
    float wet=lerp(monthly.r,monthly.g,_MonthBlend);
    float longitude=(i.uv.x-.5)*6.283185307,latitude=(i.uv.y-.5)*3.141592654;
    float3 radial=float3(sin(longitude)*cos(latitude),sin(latitude),cos(longitude)*cos(latitude));
    float4 wind=tex2D(_ClimateWindTex,i.uv);
    float2 flow=lerp(wind.rg,wind.ba,_MonthBlend);
    float3 east=float3(cos(longitude),0,-sin(longitude));
    float3 north=float3(-sin(latitude)*sin(longitude),cos(latitude),-sin(latitude)*cos(longitude));
    float3 windVec=east*flow.x+north*flow.y;
    float windMag=length(windVec);
    float3 windAxis=windMag>1e-4?(windVec/windMag):east;
    float windSpd=saturate(windMag/.85)*smoothstep(0,.12,length(radial.xz));
    float3 velocity=windVec*(.014*smoothstep(0,.10,length(radial.xz)));
    velocity*=min(1,.025/max(.0001,length(velocity)));

    float4 forcing=float4(.5,0,0,wet);
    float4 aux=float4(0,0,0,.68);
    float3 jetAxis=normalize(float3(.84,.25,.48));
    if(_GlebaCloudEnable>.5)
    {
     forcing=tex2D(_CloudSynopticTex,i.uv);
     forcing=lerp(lerp(_GlebaForcingPoleSouth,_GlebaForcingPoleNorth,step(.5,i.uv.y)),forcing,poleBlend);
     aux=tex2D(_CloudSynopticAuxTex,i.uv);
     float4 uWind=tex2D(_UpperWindTex,i.uv);
     float2 uFlow=lerp(uWind.rg,uWind.ba,_MonthBlend);
     float3 jetVec=east*uFlow.x+north*uFlow.y;
     float jetLen=length(jetVec);
     if(jetLen>1e-3&&length(radial.xz)>.08)jetAxis=jetVec/jetLen;
    }
    float polarLatRad=(30.0+45.0*aux.a)*.01745329;
    float rossbyK=_GlebaCloudEnable>.5&&_GlebaCirculationParams.z>.5?_GlebaCirculationParams.z:2.6;
    float hadleyRad=_GlebaCloudEnable>.5&&_GlebaCirculationParams.x>.1?_GlebaCirculationParams.x:.5236;

    float era=floor(_VisualTime*.0125),phase=frac(_VisualTime*.0125);
    float blend=phase*phase*(3-2*phase);
    float3 wander=float3(sin(_VisualTime*.007),cos(_VisualTime*.009),sin(_VisualTime*.004))*.62;
    EraMorphology m0=(EraMorphology)0,m1=(EraMorphology)0;
    [loop] for(int ei=0;ei<2;ei++)
    {
     EraMorphology me=SampleEraMorphology(era+ei,phase-ei,radial,longitude,latitude,east,north,velocity,windAxis,windSpd,polarLatRad,rossbyK,jetAxis,wander);
     if(ei==0)m0=me;else m1=me;
    }

    float planetary=lerp(m0.planetary,m1.planetary,blend);
    float baseTex=lerp(m0.baseTex,m1.baseTex,blend);
    float flowBands=lerp(m0.flowBands,m1.flowBands,blend);
    float mesoCells=lerp(m0.mesoCells,m1.mesoCells,blend);
    float fineGrain=lerp(m0.fineGrain,m1.fineGrain,blend);
    float curvedBand=lerp(m0.curvedBand,m1.curvedBand,blend);
    float curvedWisp=lerp(m0.curvedWisp,m1.curvedWisp,blend);
    float tradeStreets=lerp(m0.tradeStreets,m1.tradeStreets,blend);
    float honeycomb=lerp(m0.honeycomb,m1.honeycomb,blend);
    float mcsCluster=lerp(m0.mcsCluster,m1.mcsCluster,blend);
    float anvilShield=lerp(m0.anvilShield,m1.anvilShield,blend);
    float frontalLow=lerp(m0.frontalLow,m1.frontalLow,blend);
    float postFrontCells=lerp(m0.postFrontCells,m1.postFrontCells,blend);
    float frontalMid=lerp(m0.frontalMid,m1.frontalMid,blend);
    float frontalDry=lerp(m0.frontalDry,m1.frontalDry,blend);
    float wcbCirrus=lerp(m0.wcbCirrus,m1.wcbCirrus,blend);

    float3 regional=tex2D(_RegionalCloudResponse,i.uv).rgb;
    float2 pole=lerp(_RegionalCloudPoles.xy,_RegionalCloudPoles.zw,step(.5,i.uv.y));
    float2 response=lerp(pole,float2(regional.r-regional.g,regional.b),
       smoothstep(0,.5,min(i.uv.y,1-i.uv.y)*_RegionalCloudResponse_TexelSize.w));

    float low,high,convection,storm;
    if(_GlebaCloudEnable>.5)
    {
     float synLift=forcing.r,itczConv=forcing.g,stormTrack=forcing.b,moistEnv=forcing.a;
     float marineSc=aux.r,orogCrest=aux.g,leeShadow=aux.b;
     float hadleySin0=sin(clamp(hadleyRad*.38,.12,.72));
     float hadleySin1=sin(clamp(hadleyRad*.92,.28,.96));
     float tropBelt=1.0-smoothstep(hadleySin0,hadleySin1,abs(radial.y));
     float effWet=wet*(1.0-.72*leeShadow);

     // ISCCP joint optical-depth partition: broad gossamer upper/mid cloud veils
     // + concentrated, punchy snow-white convective & frontal cores embedded inside the veils!
     float highShare=saturate(.58*itczConv+.52*stormTrack+.26*moistEnv)*(1.0-.76*marineSc)*(1.0-.65*leeShadow);
     float climTarget=saturate(synLift+.11*response.x*(1.0-.50*leeShadow));
     float lowTarget=saturate(climTarget*(1.0-.35*highShare));
     float highTarget=saturate((climTarget-lowTarget)/max(.14,1.0-lowTarget)+.22*highShare*climTarget);

     // Five authentic NASA DSCOVR EPIC continuous morphological regimes:
     // Gentle continuous logistic slope keeps a visible 0.24-0.58 translucent vapor & fine cellular continuum
     // connecting sweeping fronts & luminous snow-white cores (while Sahara/Outback/dry gyre centers stay clear!).
     float zBase=(baseTex-.496)*5.4;
     float zRibbon=(baseTex-.498)*3.4+3.75*(curvedBand-.28)+1.05*(curvedWisp-.24);
     float zStreets=(tradeStreets-.496)*5.6+2.25*(curvedBand-.26);
     float zHoney=(honeycomb-.512)*5.8+1.25*(curvedBand-.26);
     float zMcs=.46*zRibbon+.22*zStreets+2.55*(mcsCluster-.18);
     // Sweeping curved comma spirals + coiled dark-blue spiral dry slots + bold 5,500 km curved frontal banners
     float zFront=.26*zRibbon+5.15*(frontalLow-.16)-5.20*frontalDry;
     float midLatBoost=smoothstep(.38,.66,abs(radial.y))*(1.0-smoothstep(.88,.98,abs(radial.y)));
     float stormWeight=saturate(smoothstep(.10,.52,stormTrack+.18*midLatBoost)*(1.0-.75*leeShadow));

     float zLow=lerp(zBase,zRibbon,.74);
     zLow=lerp(zLow,zStreets,.38*tropBelt);
     zLow=lerp(zLow,zMcs,saturate(tropBelt*(.30+.70*itczConv)));
     zLow=lerp(zLow,zHoney,saturate(marineSc*.88));
     zLow=lerp(zLow,zFront,stormWeight);
     zLow+=.48*orogCrest*max(0.0,zBase+.35);
     float uLow=rcp(1.0+exp(-1.18*clamp(zLow,-5.5,5.5)));

     // EPIC Continuous Three-Tier Atmospheric Vapor Distribution:
     //   1. broadVeil (0.28..0.58): airy pearly translucent sea-mist & fine marine cellular tapestry!
     //   2. coherentBody (0.50..0.84): sweeping continuous curved bands, trade decks & frontal arms
     //   3. brightCore (0.86..1.00): dazzling snow-white convective knots, SPCZ/ITCZ cores & spiral comma heads!
     //   + Carve high-latitude polar cap with frontalDry so swirling dark-ocean openings reach 70 deg like EPIC!
     float sharpOrg=saturate(max(stormWeight*frontalLow,itczConv*mcsCluster));
     float dryGuard=smoothstep(.10,.36,climTarget)*(1.0-.84*leeShadow);
     float polarDryCarve=stormWeight*frontalDry*smoothstep(.38,.68,abs(radial.y));
     float effLowTarget=saturate(lowTarget*(1.0-.62*polarDryCarve)+.05*smoothstep(.28,.66,climTarget)*(1.0-leeShadow));
     float veilTarget=saturate(min(.78,effLowTarget*1.22+.09*dryGuard)*(1.0-.72*polarDryCarve));
     float wVeil=max(1e-4,min(.44,min(veilTarget,1.0-veilTarget)+.09));
     float rawVeil=smoothstep(1.0-veilTarget-wVeil,1.0-veilTarget+wVeil,uLow);
     float wCore=max(1e-4,min(.24,min(effLowTarget,1.0-effLowTarget)));
     float rawCore=smoothstep(1.0-effLowTarget-wCore,1.0-effLowTarget+wCore,uLow);

     // Wind-combed silky texture + EPIC subtropical ocean fine speckled marine cumulus tapestry
     // (crisp 15-40 km cellular foam peaks inside sweeping ocean patches while keeping clear Gulf/deserts crystal clear!)
     float silkTex=flowBands*.54+fineGrain*.30+mesoCells*.16;
     float veilLayer=rawVeil*lerp(.30,.54,smoothstep(.16,.84,silkTex))*dryGuard;
     float bodyLayer=rawCore*(.62+.38*smoothstep(.18,.82,silkTex));
     float denseCore=smoothstep(.46,.84,rawCore)*(sharpOrg*.86+.14)*(.92+.08*smoothstep(.22,.78,mesoCells));
     float postWakeLace=stormWeight*postFrontCells*smoothstep(.08,.42,climTarget+.18*midLatBoost)*.84;
     float oceanTradeGate=smoothstep(.16,.42,climTarget)*smoothstep(.30,.60,moistEnv)*(1.0-.88*leeShadow);
     float tradePatchEnv=smoothstep(.36,.68,planetary*.48+curvedBand*.52);
     float tradeFineField=smoothstep(.43,.70,tradeStreets*.60+honeycomb*.40);
     float marineTapestry=oceanTradeGate*tradePatchEnv*tradeFineField*lerp(.40,.64,saturate(marineSc+.38*tropBelt));
     // Narrow sinuous ITCZ & diagonal SPCZ convective ribbon lift (keeps central Pacific EPIC signature unmistakable!)
     float itczDirectLift=smoothstep(.12,.50,itczConv)*smoothstep(.36,.80,mcsCluster)*(1.0-.82*leeShadow)*.82;
     low=saturate(max(max(veilLayer,bodyLayer),max(max(denseCore,postWakeLace),max(marineTapestry,itczDirectLift)))+
                  .04*response.x*smoothstep(.02,.18,rawVeil+.15*climTarget));

     // Explicit 5-tier cloud altitude classification in convection (weather.b):
     float scShare=saturate(marineSc*.86-max(stormWeight*frontalLow,itczConv*mcsCluster)*.95);
     float midShare=saturate(max(stormWeight*frontalMid*(1.0-.55*smoothstep(.65,.95,frontalLow)),
                                 (.35*itczConv+.45*orogCrest)*(1.0-smoothstep(.55,.88,mcsCluster)))*(1.0-scShare));
     float corePeak=smoothstep(.46,.86,planetary*.45+mesoCells*.55);
     float deepShare=saturate(max(itczConv*smoothstep(.45,.88,uLow)*(.45+.55*mcsCluster),
                                  stormWeight*smoothstep(.50,.90,frontalLow)*.88));
     float deepTrigger=smoothstep(.20,.68,deepShare*(.52+.48*corePeak)+.32*synLift*orogCrest+.20*itczConv*smoothstep(.55,.90,effWet));
     storm=saturate(smoothstep(.22,.76,effWet)*smoothstep(.60,.95,low)*deepTrigger*(1+.35*response.y)*(1.0-.92*scShare));

     float tradeType=.20+.11*smoothstep(.20,.75,flowBands);
     float midType=.40+.13*frontalMid;
     float deepType=saturate(.64+.20*deepShare+.22*storm);
     convection=tradeType;
     convection=lerp(convection,midType,saturate(midShare*1.15));
     convection=lerp(convection,deepType,saturate(deepTrigger*1.10+storm*.60));
     convection=lerp(convection,.06+.05*honeycomb,scShare);

     // Upper-tropospheric Rossby jet streaks & downwind anvil/WCB plumes coupled to active synoptic systems
     // Uses true 3-tap spherical LIC along 250 hPa jetAxis -> genuine sweeping brushed-silk curved cirrus ribbons!
     float3 upper=RotateY(radial,-_VisualTime*.0018);
     float3 crossJet=normalize(cross(radial,jetAxis)+float3(1e-5,0,0));
     float3 jetShear=normalize(upper+wander*.035);
     float jetStreak=(Noise3(Turn(jetShear)*15.0+float3(9,4,2))+
                      Noise3(Turn(normalize(jetShear+jetAxis*.024))*15.0+float3(9,4,2))+
                      Noise3(Turn(normalize(jetShear-jetAxis*.024))*15.0+float3(9,4,2)))*(1.0/3.0);
     float crossStreak=(Noise3(Turn(jetShear)*19.0+float3(3,2,7))+
                        Noise3(Turn(normalize(jetShear+crossJet*.020))*19.0+float3(3,2,7)))*.5;
     float poleTaper=smoothstep(0,.16,length(radial.xz));
     float wisps=lerp(crossStreak,jetStreak,.24+.72*poleTaper);

     // Upper-tropospheric multi-strand brushed-silk cirrus banners (stretched FBM + 3-tap spherical LIC along 250 hPa jetAxis!)
     float3 jetPsi=float3(
      Noise3(jetShear*1.95+wander*.35+float3(12,2,8)),
      Noise3(jetShear*1.95+wander*.35+float3(31,7,19)),
      Noise3(jetShear*1.95+wander*.35+float3(5,23,41)))-.5;
     float3 pJetWave=normalize(jetShear+cross(jetShear,jetPsi)*.36);
     float yJetStretch=lerp(2.1,8.2,smoothstep(.04,.42,length(radial.xz)));
     float3 qJetBand=float3(pJetWave.x*1.50,(pJetWave.y+.11*jetPsi.y)*yJetStretch,pJetWave.z*1.50);
     float jetH=Fbm(qJetBand+wander*.40+float3(12,2,8))*.56+wisps*.28+fineGrain*.16;
     float jetBanner=smoothstep(.44,.80,jetH)*(.36+.64*smoothstep(.20,.78,wisps*.64+fineGrain*.36));
     float curvedCirrus=saturate(max(curvedWisp*.88,jetBanner));

     float zJet=(Fbm(upper*2.4+wander*.42+float3(12,2,8))-.49)*2.6+(wisps-.48)*3.0+3.75*(curvedCirrus-.24)+.95*(uLow-.50);
     float zAnvil=2.65*(anvilShield-.18)+(wisps-.48)*2.8+2.85*(curvedCirrus-.24)+.85*(uLow-.45);
     float zWcb=.28*zJet+3.65*(wcbCirrus-.14)+2.40*(curvedCirrus-.24)+(wisps-.48)*2.0;
     float zHigh=lerp(zJet,zAnvil,saturate(itczConv*1.15));
     zHigh=lerp(zHigh,zWcb,stormWeight);
     float uHigh=rcp(1.0+exp(-1.16*clamp(zHigh,-5.5,5.5)));
     float highEnvTarget=saturate(highTarget*1.18+.04*dryGuard)*(1.0-.48*polarDryCarve);
     float wHigh=max(1e-4,min(.42,min(highEnvTarget,1.0-highEnvTarget)+.06));
     float rawHigh=smoothstep(1.0-highEnvTarget-wHigh,1.0-highEnvTarget+wHigh,uHigh);
     // Multi-strand brushed-silk cirrus veil (crisp clear Gulf/deserts + faster GPU empty-sky prune <0.05!)
     float highSilk=.34+.66*smoothstep(.18,.84,wisps*.64+fineGrain*.36);
     float anvilOutflow=smoothstep(.16,.56,itczConv)*anvilShield*.70;
     high=smoothstep(.05,.92,saturate(max(rawHigh*highSilk*1.12,anvilOutflow)))*(1.0-.78*tropBelt*leeShadow);
    }
    else
    {
     float threshold=lerp(.57,.35,wet)-.095*response.x;
     low=smoothstep(threshold-.06,threshold+.24,baseTex);
     storm=saturate(smoothstep(.27,.78,wet)*smoothstep(.62,.96,low)*(1+.35*response.y));
     convection=saturate(.16+wet*.32+storm*.52);
     float3 upper=RotateY(radial,-_VisualTime*.0018);
     float envelope=smoothstep(.33,.67,Fbm(upper*4.3+wander*.5+float3(12,2,8)));
     float3 streakAxis=normalize(float3(.84,.25,.48));
     float3 crossAxis=normalize(float3(-.48,.12,.86));
     float3 ribbonPos=upper+wander*.035;
     float ribbonA=Noise3((ribbonPos-streakAxis*dot(ribbonPos,streakAxis)*.94)*35+float3(9,4,2));
     float ribbonB=Noise3((ribbonPos-crossAxis*dot(ribbonPos,crossAxis)*.92)*42+float3(3,2,7));
     float align=.5+.5*(abs(dot(windVec,streakAxis))-abs(dot(windVec,crossAxis)))*smoothstep(0,.16,length(radial.xz));
     float wisps=lerp(ribbonB,ribbonA,smoothstep(.30,.70,align));
     high=envelope*(.14+.70*smoothstep(.31,.78,wisps))*lerp(.65,1,wet);
    }

    float4 weather=float4(low,high,convection,storm);
    Cyclone(radial,_CycloneNorth,1,_CyclonePhase.x,flow,weather);
    Cyclone(radial,_CycloneSouth,-1,_CyclonePhase.y,flow,weather);
    return weather;
   }
   ENDCG
  }
 }
 Fallback Off
}
