Shader "ORBIS/FlatWorld/SkyBake"
{
 Properties
 {
  _ClimateTex("Monthly precipitation and temperature",2D)="black"{}
  _ClimateWindTex("Climate wind",2D)="black"{}
  _RegionalCloudResponse("Regional moist, dry and convective response",2D)="black"{}
  _GlebaCloudForcingTex("Gleba synoptic lift, ITCZ, storm track, moisture",2D)="black"{}
  _GlebaCloudAuxTex("Gleba marine Sc, orographic, rain shadow, polar front",2D)="black"{}
  _GlebaUpperWindTex("Gleba Rossby jet and anvil wind",2D)="black"{}
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
   sampler2D _GlebaCloudForcingTex,_GlebaCloudAuxTex,_GlebaUpperWindTex;
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
    float turn=lerp(.82,3.35,organized)*log(1+4.8*max(r,0));
    float phase=bearingInStorm+spin*turn+.72*distort;
    float2 flow=float2(cos(phase),sin(phase))*r;
    float cells=Fbm(float3(flow*5.4,1)+seed);
    float spiralRipple=.5+.5*cos(3.0*phase+1.8*(cells-.5));
    float gravWave=.5+.5*cos(24.0*r-2.0*phase+1.2*distort);

    float mainArm=smoothstep(-.04,.88,cos(phase+1.35*(cells-.5)));
    float secondaryArm=smoothstep(.12,.92,cos(2.0*phase+1.10+1.25*(cells-.5)))*(1-smoothstep(.54,1.02,r))*.84;
    float feeder=smoothstep(.20,.92,cos(phase+2.53+.44*sin(5.4*r)+2.0*(cells-.5)));
    feeder*=smoothstep(.18,.70,cells)*(.92-.32*smoothstep(.28,.92,r));
    float spiralApron=(1-smoothstep(.35,.96,r))*(.42+.58*cells)*.58;
    float bands=saturate(max(max(mainArm*lerp(.58,1.04,cells),secondaryArm),max(feeder,spiralApron)));

    // Organic swirling Central Dense Overcast (CDO) glaciated canopy + logarithmic spiral rainbands + crisp eye
    // (warps CDO radius with cells so the CDO forms a natural swirling pinwheel — zero circular pancake edge & zero gray spots!)
    float maturity=smoothstep(.44,.76,centre.w);
    float rCdo=r*(1.0+.28*(cells-.5)-.14*(spiralRipple-.5));
    float cdoDome=(1-smoothstep(.16,.52,rCdo))*lerp(.50,1.0,maturity)*(.84+.10*spiralRipple+.06*gravWave);
    float innerWall=(1-smoothstep(.11,.30,r))*(.92+.08*gravWave);
    float outerRing=exp(-pow((r-.24)/.062,2))*maturity*.76*(.80+.20*spiralRipple);
    float wall=max(max(innerWall,cdoDome),outerRing);
    float eye=lerp(1,smoothstep(.022,.078,r+.008*distort),maturity);

    float bodyCover=lerp(saturate(cells*.84+.20*spiralRipple),max(wall,bands),organized);
    float low=eye*saturate(.04+bodyCover*(.90+.12*spiralRipple));
    float convection=eye*saturate(max(wall*maturity*.98,bodyCover*(.44+.82*centre.w)));
    float stormCore=eye*smoothstep(.38,.80,convection);

    float lowBoundary=1-smoothstep(.58,1.18,r+.11*distort);
    float lowInfluence=lowBoundary*smoothstep(.035,.44,centre.w);

    // Organic swirling CDO cirrus shield transitioning smoothly into feathered anticyclonic outflow plumes
    // (avoids sharp bicycle-spoke radial ribs in low-orbit grazing views while preserving spiral outflow!)
    float icePhase=phase-spin*(1.12+.52*r)+2.1*(cells-.5);
    float filaments=pow(.5+.5*cos(3.0*icePhase),2.0);
    float radialRibs=pow(.5+.5*cos(6.0*bearingInStorm-spin*3.2*r+4.4*distort+2.4*(cells-.5)),1.8)*smoothstep(.28,.68,r);
    float innerCanopy=(1-smoothstep(.22,.56,rCdo))*.84;
    float shield=1-smoothstep(.42,1.12,r);
    float upper=eye*saturate(max(innerCanopy*maturity,(.12+.36*filaments+.13*radialRibs+.44*shield))*(1+.22*dot(local,downwind))*(.58+.52*organized));
    float outBoundary=1-smoothstep(.72,1.34,r+.10*distort);
    float outInfluence=outBoundary*smoothstep(.035,.46,centre.w);

    weather.rb=lerp(weather.rb,float2(low,.18+.82*convection),lowInfluence);
    weather.a=lerp(weather.a,stormCore,lowInfluence);
    weather.g=lerp(weather.g,max(weather.g*.35,upper),outInfluence);
   }
   struct EraMorphology
   {
    float planetary,synCluster,baseTex,flowBands,mesoCells,fineGrain,curvedBand,curvedWisp,tradeStreets,honeycomb,mcsCluster,anvilShield,frontalLow,frontalMid,frontalDry,postFrontCells,wcbCirrus,polarCarve;
   };
   EraMorphology SampleEraMorphology(
    float eraId,float age,float3 radial,float longitude,float latitude,
    float3 east,float3 north,float3 velocity,float3 windAxis,float windSpd,float polarLatRad,float rossbyK,float3 jetAxis,float3 wander)
   {
    EraMorphology o;
    float3 p=normalize(radial-velocity*age*7.2);
    float3 seed=float3(Hash3(float3(eraId,4,7)),Hash3(float3(eraId,11,3)),Hash3(float3(eraId,5,19)))*15.0;

    // Two-stage divergence-free spherical curl-streamfunction advection ("cream-in-coffee" geophysical fluid swirl!):
    // Stronger planetary-scale swirl (0.26) + synoptic eddy curl (0.10) bends cloud sheets into authentic EPIC
    // sweeping C-arcs, S-ribbons & hooked cyclonic eddies while gathering clouds into coherent synoptic clusters!
    float3 psi1=float3(
     Fbm(p*1.85+wander*.50+seed+17.0),
     Fbm(p*1.85+wander*.50+seed+43.0),
     Fbm(p*1.85+wander*.50+seed+71.0))-.5;
    float3 curl1=cross(p,psi1);
    float3 p1=normalize(p+curl1*.26);

    float3 psi2=float3(
     Noise3(Turn(p1)*4.2+seed+11.0),
     Noise3(Turn(p1)*4.2+seed+29.0),
     Noise3(Turn(p1)*4.2+seed+53.0))-.5;
    float3 curl2=cross(p1,psi2);
    float3 pFlow=normalize(p1+curl2*.10);

    // 1. Multi-scale TRUE spherical flow-combed fields (3-tap tangent LIC along local climate wind!).
    float3 flowDir=normalize(lerp(windAxis,jetAxis,.42)+float3(1e-5,0,0));
    float licStep=lerp(.016,.032,windSpd);
    float3 pFlowP=normalize(pFlow+flowDir*licStep);
    float3 pFlowM=normalize(pFlow-flowDir*licStep);
    // Planetary-scale synoptic high/low clustering field (creates broad coherent cloud complexes & natural quiet clear basins!)
    float synCluster=Fbm(p1*1.38+wander*.38+seed*.55+float3(11.3,2.7,8.4));
    float planetary=Fbm(p1*1.90+wander*.46+seed*.7+float3(3.1,7.4,1.9));
    float synoptic=(Noise3(Turn(pFlow)*4.2+wander*.75+seed)+
                    Noise3(Turn(pFlowP)*4.2+wander*.75+seed)+
                    Noise3(Turn(pFlowM)*4.2+wander*.75+seed))*(1.0/3.0);
    float3 pWindP=normalize(pFlow+windAxis*(licStep*.95));
    float3 pWindM=normalize(pFlow-windAxis*(licStep*.95));
    float flowBands=(Noise3(Turn(pFlow)*15.5+wander+seed*1.4)+
                     Noise3(Turn(pWindP)*15.5+wander+seed*1.4)+
                     Noise3(Turn(pWindM)*15.5+wander+seed*1.4))*(1.0/3.0);
    float mesoCells=Fbm(Turn(pFlow)*32.0+seed*1.9);
    float fineGrain=Noise3(Turn(pFlow)*58.0+seed*2.7)*.54+
                    Noise3(Turn(normalize(pFlow+jetAxis*.016))*58.0+seed*2.7)*.46;

    // 1b. Geostrophic Baroclinic Sweeping Fluid Bands (continuous planetary-scale curved sheets & graceful arcs!):
    // Moderate meridional stretch (4.6) avoids central-ocean parallel zebra-dashes while keeping smooth fluid ribbons!
    float eqWeight=smoothstep(.04,.44,length(radial.xz));
    float yStretch1=lerp(1.75,4.10,eqWeight);
    float yStretch2=lerp(2.05,4.85,eqWeight);
    float3 pWave=normalize(p+curl1*.36+curl2*.08);
    float hemiDiag=.28*(latitude/(abs(latitude)+.28));
    float baroY=pWave.y+hemiDiag*sin(longitude*2.0+psi1.x*2.4)*eqWeight*.19+.15*psi1.y;
    float3 qBand1=float3(pWave.x*1.28,baroY*yStretch1,pWave.z*1.28);
    float3 qBand2=float3(pWave.x*2.25,baroY*yStretch2,pWave.z*2.25);
    float bandField1=Fbm(qBand1+wander*.40+seed+float3(13.4,27.1,5.8));
    float bandField2=Noise3(qBand2+wander*.56+seed+float3(47.2,11.8,19.4))*.62+
                     Noise3(qBand2*1.75+wander*.68+seed+float3(19.1,5.7,31.4))*.38;
    float bandH=bandField1*.60+bandField2*.18+planetary*.10+flowBands*.08+fineGrain*.04;
    float bandVeil=smoothstep(.35,.74,bandH);
    float bandSpine=smoothstep(.53,.84,bandH*.72+flowBands*.18+fineGrain*.10);
    float ribbonSilk=.45+.55*smoothstep(.16,.84,flowBands*.58+fineGrain*.42);
    o.curvedBand=saturate(bandVeil*.54+bandSpine*.56*ribbonSilk);
    o.curvedWisp=saturate((bandVeil*.66+bandSpine*.50)*ribbonSilk);

    // 2. Regime A: Subtropical Marine Stratocumulus Closed/Open-Cell Mesoscale Envelope & Trade Streets
    // Keep 2D cache frequencies strictly below 1024x512 Nyquist (k<=66) so low-orbit marine Sc has ZERO aliasing;
    // crisp 7-26 km polygonal closed-cell cobblestone & open-cell actiniform rings are sculpted in full 3D Worley!
    float streetRipple=(Noise3(Turn(pFlow)*46.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow+windAxis*.020))*46.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow-windAxis*.020))*46.0+seed+5.0))*(1.0/3.0);
    float mesoFoam=Noise3(Turn(pFlow)*66.0+seed+13.0);
    float streetEnv=smoothstep(.26,.66,synCluster*.36+planetary*.34+bandField1*.30);
    o.tradeStreets=saturate(synCluster*.20+planetary*.20+synoptic*.18+streetRipple*.24+smoothstep(.38,.74,mesoFoam)*.32*streetEnv);

    // Coherent eastern-boundary marine Sc sheets with organic mesoscale Pockets of Open Cells (POCs)
    // Modulated strongly by synCluster so Sc forms natural meandering decks + clear ocean bays (NEVER a zonal band!)
    float scDeckMacro=smoothstep(.26,.68,synCluster*.44+planetary*.34+synoptic*.22);
    float scPocRift=smoothstep(.28,.72,Noise3(Turn(pFlow)*14.0+seed+29.0)*.62+mesoCells*.38);
    float scBreak=smoothstep(.30,.72,mesoFoam*.55+streetRipple*.45);
    o.honeycomb=saturate(scDeckMacro*(.50+.30*scPocRift+.22*scBreak));

    o.planetary=planetary;
    o.synCluster=synCluster;
    o.flowBands=flowBands;
    o.mesoCells=mesoCells;
    o.fineGrain=fineGrain;
    o.baseTex=synCluster*.26+planetary*.26+synoptic*.24+bandField1*.14+flowBands*.07+mesoCells*.03;

    // 3. Regime B: Slender, Sinuous Continuous Pacific ITCZ Line + Sweeping Diagonal SPCZ Multi-Strand Ribbon
    // studded with tight dazzling-white Cb hot-towers & long downwind brushed-silk cirrus anvil plumes!
    float3 qItcz=float3(pWave.x*1.35,(pWave.y+hemiDiag*.22*sin(longitude*1.8+psi1.x*2.0)+.07*psi1.z)*lerp(2.2,9.2,eqWeight),pWave.z*1.35);
    float itczField=Fbm(qItcz+wander*.44+seed+float3(5.3,1.7,8.9));
    float itczRibbon=(Noise3(Turn(pFlow)*15.0+wander*.65+seed+float3(5.3,1.7,8.9))+
                      Noise3(Turn(pFlowP)*15.0+wander*.65+seed+float3(5.3,1.7,8.9))+
                      Noise3(Turn(pFlowM)*15.0+wander*.65+seed+float3(5.3,1.7,8.9)))*(1.0/3.0);
    float itczSlender=smoothstep(.38,.74,itczField*.64+itczRibbon*.26+fineGrain*.10);
    float hotTowers=smoothstep(.36,.74,mesoCells*.50+mesoFoam*.34+fineGrain*.16);
    float mcsEnv=smoothstep(.24,.72,max(itczSlender*1.04,synCluster*.26+synoptic*.28+itczRibbon*.46));
    o.mcsCluster=saturate(mcsEnv*(.36+.64*hotTowers));
    float3 pAnvilNear=normalize(pFlow-jetAxis*.058);
    float3 pAnvilFar=normalize(pFlow-jetAxis*.155);
    float anvilNear=Fbm(pAnvilNear*4.6+wander*.65+seed);
    float anvilFar=Fbm(pAnvilFar*2.9+wander*.55+seed);
    float anvilSilk=(Noise3(Turn(pAnvilNear)*26.0+seed+4.0)+
                     Noise3(Turn(normalize(pAnvilNear+jetAxis*.022))*26.0+seed+4.0)+
                     Noise3(Turn(normalize(pAnvilNear-jetAxis*.022))*26.0+seed+4.0))*(1.0/3.0);
    o.anvilShield=smoothstep(.28,.70,anvilNear*.40+anvilFar*.32+anvilSilk*.28)*(.38+.62*mcsEnv);

    // 4. Regime C: NASA EPIC / VIIRS Sweeping Curved Mid-Latitude Frontal Banners + Bold 2.5-Turn Spiral Comma Cyclones
    // + Meandering Non-Zonal Rossby Storm Tracks + High-Latitude Polar Vortex Trough Openings + Post-Frontal Open Cells!
    float hemiSign=latitude>=0?1.0:-1.0;
    float3 hSeed=seed+(latitude>=0?float3(0,0,0):float3(19.7,41.3,13.9));
    float k1=max(2.0,floor(rossbyK+.35+.5));
    float k2=k1+1.0;
    float eraLon1=6.2831853*Hash3(float3(eraId,9.0+hemiSign*2.0,2));
    float eraLon2=6.2831853*Hash3(float3(eraId,15.0+hemiSign*3.0,6));
    float packPhase=6.2831853*Hash3(float3(eraId,23.0+hemiSign*5.0,11));

    // Strong Rossby wave-packet modulation concentrates energy into 2-3 dramatic curved cyclones per hemisphere
    // with wide, crisp dark-ocean anticyclonic clearings between storm packets (eliminates fixed zonal bands!)
    float packetWave=.56*cos(longitude-.35*age+packPhase)+.44*cos(2.0*longitude+.22*age+eraLon2);
    float packetEnv=smoothstep(-.42,.46,packetWave+(synCluster-.5)*1.25);

    // Meandering storm latitude swings +-10 deg with Rossby waves so fronts never march along a fixed parallel!
    float rossbyMeander=.155*sin(k1*longitude-.42*age+hSeed.y)+.085*cos(k2*longitude+.30*age+hSeed.z)+.08*(synCluster-.5);
    float stormLatRad=clamp(polarLatRad*.86+rossbyMeander,.52,.98);
    float theta=k1*longitude-.88*age+eraLon1+.62*sin(k2*longitude-1.12*age+eraLon2)+.34*sin(longitude+hSeed.x);
    float waveS=sin(theta),waveC=cos(theta);
    float waveFade=smoothstep(-.84,-.02,waveC)*(.34+.66*packetEnv);
    float dx=atan2(waveS,waveC)/max(1.4,k1+.12)*max(.24,cos(latitude));
    float dy=(abs(latitude)-stormLatRad);

    // Boldly wound 2.5-turn spiral occluded comma cyclone with dramatic dark-blue dry-intrusion channel
    float2 vortVec=float2(dx+.050+.032*(synoptic-.5),dy-.045+.028*(planetary-.5));
    float rVort=length(vortVec)/lerp(.31,.44,saturate(3.4-k1));
    float vortAng=atan2(vortVec.y,vortVec.x+1e-6)+4.75*log(1.0+8.2*rVort)+.62*(synoptic-.5)+.32*(flowBands-.5);
    float spiralWave=cos(vortAng);
    float spiralPrimary=smoothstep(-.10,.70,spiralWave);
    float spiralSecondary=smoothstep(.18,.86,cos(2.0*vortAng+1.15))*.72;
    float commaCollar=exp(-pow((rVort-.16)/.088,2.0))*.82;
    float commaHead=(1.0-smoothstep(.58,1.34,rVort))*smoothstep(.024,.088,rVort)*max(max(spiralPrimary,spiralSecondary),commaCollar);
    float spiralDryTongue=smoothstep(-.02,.64,-spiralWave)*(1.0-smoothstep(.16,1.26,rVort))*smoothstep(.016,.078,rVort);
    float vortexEyeClear=(1.0-smoothstep(.016,.076,rVort))*.94;

    // Sweeping 5,500 km curved cold-front banner reaching equatorward into subtropics with CRISP rear edge!
    float3 frontTangent=normalize(east*.66+north*hemiSign*.75+1e-5);
    float frontSilkA=(Noise3(Turn(pFlow)*22.0+hSeed)+
                      Noise3(Turn(normalize(pFlow+frontTangent*.028))*22.0+hSeed)+
                      Noise3(Turn(normalize(pFlow-frontTangent*.028))*22.0+hSeed))*(1.0/3.0);
    float frontSilkB=(Noise3(Turn(pFlow)*48.0+hSeed+7.0)+
                      Noise3(Turn(normalize(pFlow+frontTangent*.018))*48.0+hSeed+7.0))*.5;
    float frontFilament=frontSilkA*.62+frontSilkB*.38;

    float dxFront=-.01+1.04*(dy-.04)-.52*(dy-.04)*abs(dy-.04);
    float dFront=dx-dxFront+.034*(synoptic-.5)+.020*(frontFilament-.5);
    // Asymmetric cold front: crisp drop on cold/rear side (dFront < 0), graceful multi-strand veil on warm/front side (dFront > 0)
    float frontWidth=dFront<0?.046:.086;
    float coldFront=exp(-pow(dFront/frontWidth,2.0))*smoothstep(-.64,-.18,dy)*(1.0-smoothstep(.16,.36,dy))*(.76+.42*frontFilament);
    float coldFrontApron=exp(-pow((dFront-.060)/.138,2.0))*smoothstep(-.60,-.12,dy)*(1.0-smoothstep(.18,.38,dy))*.60*(.50+.50*frontFilament);

    float dWarm=(dy-(.072-.54*max(0.0,dx+.02)))+.028*(synoptic-.5);
    float warmFront=exp(-pow(dWarm/.080,2.0))*smoothstep(-.10,.04,dx)*(1.0-smoothstep(.28,.52,dx))*(.68+.44*frontFilament);

    float wcb=smoothstep(-.01,.11,dFront)*(1.0-smoothstep(.12,.38,dFront))*smoothstep(-.38,.22,dy)*(1.0-smoothstep(.24,.46,dy));
    float drySlot=exp(-pow((dFront+.082)/.076,2.0))*smoothstep(-.54,-.04,dy)*(1.0-smoothstep(.18,.40,dy));

    // Post-frontal cold-air wake envelope (triggers crisp 3D Worley open-cell cumulus lace in ScrollCloudSource!)
    float coldWake=smoothstep(.04,.16,-dFront)*(1.0-smoothstep(.42,.80,-dFront))*smoothstep(-.52,.26,dy);
    float wakeLoop=saturate(coldWake+spiralDryTongue*.85);
    float openCellPatch=smoothstep(.24,.66,Noise3(Turn(pFlow)*18.0+hSeed+17.0)*.58+mesoCells*.42);
    float openCellMeso=smoothstep(.34,.72,mesoFoam*.60+fineGrain*.40);
    float postCells=wakeLoop*(.44+.56*openCellMeso)*(.48+.52*openCellPatch)*.88;

    // High-latitude baroclinic wave-trough openings (46-84 deg): carves realistic dark-ocean/land leads
    // & curved swirling polar-vortex bands so Bering/Siberia/Arctic & Antarctic NEVER freeze into solid white slabs!
    float highLatZone=smoothstep(.66,.86,abs(radial.y));
    float polarTroughWave=Fbm(pWave*2.55+wander*.44+hSeed+float3(23.1,9.4,15.7));
    float polarSwirlLead=Noise3(p1*3.4+wander*.52+hSeed+float3(5.2,31.7,11.1));
    float polarLead=smoothstep(.42,.74,1.0-(polarTroughWave*.65+polarSwirlLead*.35)+.18*(1.0-bandField1));
    o.polarCarve=highLatZone*polarLead;

    o.frontalLow=waveFade*saturate(max(max(commaHead*(.92+.16*frontFilament),max(coldFront,coldFrontApron)),warmFront*.94)+.54*wcb*(.54+.46*frontFilament));
    o.postFrontCells=waveFade*postCells;
    o.frontalMid=waveFade*saturate(warmFront+.80*wcb+commaHead*.66+coldFrontApron*.52);
    o.frontalDry=waveFade*saturate(max(drySlot,max(spiralDryTongue*.96,vortexEyeClear)));
    o.wcbCirrus=waveFade*saturate(wcb*(.74+.36*frontFilament)+commaHead*.92+warmFront*.76+coldFront*.70+coldFrontApron*.58);
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
     forcing=tex2D(_GlebaCloudForcingTex,i.uv);
     forcing=lerp(lerp(_GlebaForcingPoleSouth,_GlebaForcingPoleNorth,step(.5,i.uv.y)),forcing,poleBlend);
     aux=tex2D(_GlebaCloudAuxTex,i.uv);
     float4 uWind=tex2D(_GlebaUpperWindTex,i.uv);
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
    float synCluster=lerp(m0.synCluster,m1.synCluster,blend);
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
    float polarCarve=lerp(m0.polarCarve,m1.polarCarve,blend);

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

     // Protect ONLY true cold eastern-boundary upwelling marine Sc decks (Peru/Chile, California/Baja, Namibia: marineSc > 0.38!)
     // Never boost open warm subtropical oceans (marineSc ~0.15-0.30), keeping vast natural clear subtropical gyres with ZERO zonal banding!
     float scProtect=smoothstep(.38,.74,marineSc);
     float lowLee=leeShadow*(1.0-.82*scProtect);

     // ISCCP joint optical-depth partition + natural global clustering & clear-sky breathing room:
     // Prevent high-lift polar & rainforest zones from saturating into solid chalky white caps/blankets!
     float highShare=saturate(.58*itczConv+.54*stormTrack+.24*moistEnv)*(1.0-.82*marineSc)*(1.0-.68*leeShadow);
     float climTarget=saturate(synLift+.11*response.x*(1.0-.50*lowLee));
     float lowTarget=saturate(climTarget*(1.0-.36*highShare));
     float highTarget=saturate((climTarget-lowTarget)/max(.14,1.0-lowTarget)+.20*highShare*climTarget);

     // Five authentic NASA DSCOVR EPIC & VIIRS continuous morphological regimes:
     // Planetary synCluster concentrates vapor into coherent synoptic systems while opening crisp quiet clear basins!
     float clusterBias=(synCluster-.49)*2.45;
     float zBase=(baseTex-.506)*5.6+clusterBias;
     float zRibbon=(baseTex-.506)*3.1+clusterBias*.75+3.95*(curvedBand-.29)+1.10*(curvedWisp-.25);
     float zStreets=(tradeStreets-.508)*5.8+clusterBias*.65+2.00*(curvedBand-.28);
     float zHoney=(honeycomb-.44)*5.4+clusterBias*.55+.95*(curvedBand-.28)+.85*scProtect;
     float zMcs=.38*zRibbon+.18*zStreets+2.90*(mcsCluster-.22)+clusterBias*.50;
     // Sweeping curved comma spirals + coiled dark-blue spiral dry slots + bold 5,500 km curved frontal banners
     float zFront=.20*zRibbon+5.95*(frontalLow-.14)-5.60*frontalDry-2.85*polarCarve;
     float midLatBoost=smoothstep(.30,.58,abs(radial.y))*(1.0-smoothstep(.90,.99,abs(radial.y)));
     float stormWeight=saturate(smoothstep(.05,.42,stormTrack+.28*midLatBoost)*(1.0-.75*leeShadow));

     float zLow=lerp(zBase,zRibbon,.74);
     zLow=lerp(zLow,zStreets,.36*tropBelt);
     zLow=lerp(zLow,zMcs,saturate(tropBelt*(.32+.68*itczConv)));
     zLow=lerp(zLow,zHoney,saturate(scProtect*.94));
     zLow=lerp(zLow,zFront,stormWeight);
     zLow+=.48*orogCrest*max(0.0,zBase+.35);
     float uLow=rcp(1.0+exp(-1.22*clamp(zLow,-5.5,5.5)));

     // EPIC Continuous Three-Tier Atmospheric Vapor Distribution with natural clearings:
     //   - Carve BOTH mid-latitude dry intrusions AND high-latitude polar-vortex leads so polar caps NEVER blank white
     //   - Protect sweeping curved fronts & comma spirals from blanketRelief while opening clean gaps in solid tropics!
     float sharpOrg=saturate(max(stormWeight*frontalLow*1.12,itczConv*mcsCluster));
     float dryGuard=smoothstep(.11,.36,climTarget+.16*scProtect)*(1.0-.84*lowLee);
     float polarDryCarve=saturate(stormWeight*frontalDry*smoothstep(.30,.60,abs(radial.y))+polarCarve*.90);
     float blanketRelief=smoothstep(.48,.82,lowTarget)*(1.0-smoothstep(.24,.72,synCluster*.52+planetary*.48))*(1.0-.86*sharpOrg);
     float effLowTarget=saturate(min(.74,lowTarget*(1.0-.62*polarDryCarve-.42*blanketRelief)+.16*scProtect*smoothstep(.28,.68,synCluster*.55+planetary*.45))+.04*smoothstep(.28,.66,climTarget)*(1.0-lowLee));
     float veilTarget=saturate(min(.74,effLowTarget*1.16+.07*dryGuard)*(1.0-.70*polarDryCarve));
     float wVeil=max(1e-4,min(.42,min(veilTarget,1.0-veilTarget)+.08));
     float rawVeil=smoothstep(1.0-veilTarget-wVeil,1.0-veilTarget+wVeil,uLow);
     float wCore=max(1e-4,min(.23,min(effLowTarget,1.0-effLowTarget)));
     float rawCore=smoothstep(1.0-effLowTarget-wCore,1.0-effLowTarget+wCore,uLow);

     // Wind-combed silky texture + organic eastern-boundary marine Sc decks (Peru/Chile, Baja, Namibia ONLY!)
     // + bold sweeping mid-latitude curved fronts + post-frontal open-cell wakes + sinuous Pacific ITCZ / SPCZ!
     float silkTex=flowBands*.54+fineGrain*.30+mesoCells*.16;
     float veilLayer=rawVeil*lerp(.26,.50,smoothstep(.16,.84,silkTex))*dryGuard;
     float bodyLayer=rawCore*(.64+.36*smoothstep(.18,.82,silkTex));
     float denseCore=smoothstep(.42,.80,rawCore)*(sharpOrg*.90+.10)*(.92+.08*smoothstep(.22,.78,mesoCells));
     float frontDirect=stormWeight*smoothstep(.24,.72,frontalLow)*(1.0-.85*frontalDry)*smoothstep(.10,.38,climTarget+.22*midLatBoost)*.86;
     float postWakeLace=stormWeight*postFrontCells*smoothstep(.08,.40,climTarget+.20*midLatBoost)*.82;
     // Localized eastern-boundary marine stratocumulus sheet (closed-cell cobbles nearshore -> open-cell POCs seaward!)
     float scDeckDirect=scProtect*smoothstep(.22,.64,honeycomb)*smoothstep(.18,.54,moistEnv+.28*scProtect)*.80;
     float oceanTradeGate=smoothstep(.18,.44,climTarget)*smoothstep(.32,.62,moistEnv)*(1.0-.88*lowLee);
     float tradePatchEnv=smoothstep(.42,.74,synCluster*.48+curvedBand*.52);
     float tradeFineField=smoothstep(.44,.70,tradeStreets*.62+honeycomb*.38);
     float marineTapestry=oceanTradeGate*tradePatchEnv*tradeFineField*lerp(.34,.54,saturate(scProtect+.30*tropBelt));
     float itczDirectLift=smoothstep(.14,.52,itczConv)*smoothstep(.38,.80,mcsCluster)*(1.0-.82*leeShadow)*.82;
     low=saturate(max(max(veilLayer,bodyLayer),max(max(max(denseCore,frontDirect),postWakeLace),max(max(marineTapestry,scDeckDirect),itczDirectLift)))+
                  .04*response.x*smoothstep(.02,.18,rawVeil+.15*climTarget));

     // Explicit 5-tier cloud altitude classification in convection (weather.b):
     // Route BOTH eastern-boundary marineSc AND post-frontal cold-air open cells cleanly into low cellular regime!
     float postOpenShare=saturate(postWakeLace*1.15*(1.0-smoothstep(.30,.65,frontalLow)));
     float scShare=saturate(marineSc*.92-max(stormWeight*frontalLow,itczConv*mcsCluster)*.92);
     float midShare=saturate(max(stormWeight*frontalMid*(1.0-.55*smoothstep(.65,.95,frontalLow)),
                                 (.35*itczConv+.45*orogCrest)*(1.0-smoothstep(.55,.88,mcsCluster)))*(1.0-scShare)*(1.0-postOpenShare));
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
     // Post-frontal cold-air open cells sit at type ~0.16 (1.2-2.2 km cold-air cumulus rings)
     convection=lerp(convection,.15+.04*mesoCells,postOpenShare*.85);
     // Eastern-boundary marine Sc sits at type ~0.06..0.11 (420-1,450 m inversion deck: closed cobbles + open POCs)
     convection=lerp(convection,.06+.05*honeycomb,scShare);

     // Upper-tropospheric Rossby jet streaks & downwind anvil/WCB plumes coupled to active synoptic systems
     float3 upper=RotateY(radial,-_VisualTime*.0018);
     float3 crossJet=normalize(cross(radial,jetAxis)+float3(1e-5,0,0));
     float3 jetShear=normalize(upper+wander*.035);
     float jetStreak=(Noise3(Turn(jetShear)*15.0+float3(9,4,2))+
                      Noise3(Turn(normalize(jetShear+jetAxis*.022))*15.0+float3(9,4,2))+
                      Noise3(Turn(normalize(jetShear-jetAxis*.022))*15.0+float3(9,4,2)))*(1.0/3.0);
     float crossStreak=(Noise3(Turn(jetShear)*19.0+float3(3,2,7))+
                        Noise3(Turn(normalize(jetShear+crossJet*.018))*19.0+float3(3,2,7)))*.5;
     float poleTaper=smoothstep(0,.16,length(radial.xz));
     float wisps=lerp(crossStreak,jetStreak,.24+.72*poleTaper);

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
     float zWcb=.28*zJet+3.75*(wcbCirrus-.15)+2.40*(curvedCirrus-.24)+(wisps-.48)*2.0-2.40*polarCarve;
     float zHigh=lerp(zJet,zAnvil,saturate(itczConv*1.15));
     zHigh=lerp(zHigh,zWcb,stormWeight);
     float uHigh=rcp(1.0+exp(-1.16*clamp(zHigh,-5.5,5.5)));
     float highEnvTarget=saturate(highTarget*1.14+.03*dryGuard)*(1.0-.58*polarDryCarve);
     float wHigh=max(1e-4,min(.40,min(highEnvTarget,1.0-highEnvTarget)+.06));
     float rawHigh=smoothstep(1.0-highEnvTarget-wHigh,1.0-highEnvTarget+wHigh,uHigh);
     float highSilk=.34+.66*smoothstep(.18,.84,wisps*.64+fineGrain*.36);
     float anvilOutflow=smoothstep(.16,.56,itczConv)*anvilShield*.70;
     high=smoothstep(.05,.92,saturate(max(rawHigh*highSilk*1.10,anvilOutflow)))*(1.0-.78*tropBelt*leeShadow);
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
