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
   UNITY_DECLARE_TEX2D(_ClimateTex);
   sampler2D _ClimateWindTex,_RegionalCloudResponse;
   // All three forcing grids use the climate texture's bilinear, repeat-U/clamp-V state.
   UNITY_DECLARE_TEX2D_NOSAMPLER(_CloudSynopticTex);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_CloudSynopticAuxTex);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_UpperWindTex);
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
    if(radius>=1.58)return;
    float3 east=normalize(cross(float3(0,1,0),axis));
    float3 north=cross(axis,east);
    float2 local=float2(dot(p,east),dot(p,north))/extent;
    float bearing=atan2(local.y,local.x+1e-7);
    float organized=smoothstep(.14,.66,centre.w);
    float maturity=smoothstep(.42,.74,centre.w);

    // Steering / vertical-shear asymmetry basis:
    //   downwind   : along steering/shear vector (broader outflow cirrus fan & longer sweeping rainbands)
    //   rightFront : moisture-rich inflow quadrant (right-of-motion in NH, left-of-motion in SH)
    float2 downwind=steering*rsqrt(max(.04,dot(steering,steering)));
    float2 rightFront=float2(-downwind.y,downwind.x)*spin;
    float2 dirLocal=local/max(radius,.06);
    float alongFlow=dot(dirLocal,downwind);
    float acrossFlow=dot(dirLocal,rightFront);
    float inflowBias=saturate(.52+.36*alongFlow+.24*acrossFlow);
    float upwindDry=saturate(-.46*alongFlow-.28*acrossFlow);

    float bearingInStorm=bearing-rotation;
    float2 body=float2(cos(bearingInStorm),sin(bearingInStorm))*radius;
    float3 seed=axis*3.1+float3(7,19,3);
    float distortA=Noise3(float3(body*2.4,1)+seed)-.5;
    float distortB=Noise3(float3(body*5.6,3.7)+seed)-.5;
    float distort=distortA*.70+distortB*.30;
    // Asymmetric radial stretch breaks circularity: extends downwind/inflow flank, tightens upwind dry flank
    float asymScale=1.0-.17*alongFlow-.09*acrossFlow;
    float r=radius*asymScale*(1.0+.20*distort);

    // Natural NASA VIIRS logarithmic spiral crossing pitch (~0.85-1.05 sweeping turns from eyewall to outer tail):
    // Broad graceful sweeping arcs wrapping into the eyewall — zero concentric cinnamon-roll rings!
    // Exact hemispheric chirality (CCW inward in NH spin=+1, CW inward in SH spin=-1).
    float turn=lerp(.95,3.25,organized)*log(1.0+4.4*max(r,0.0));
    float phase=bearingInStorm+spin*turn+.68*distortA+.28*distortB;

    // Multi-scale spiral-following convective cloud structure (fluffy cauliflower clusters + feathery outer filaments!)
    float2 flow=float2(cos(phase),sin(phase))*r;
    float cells=Fbm(float3(flow*3.8,1)+seed);
    float mesoCells=Fbm(float3(body*7.2+flow*2.6,2.1)+seed);
    float fineCloud=Noise3(float3(body*14.5+flow*4.5,4.3)+seed);
    float convClusters=smoothstep(.18,.84,cells*.52+mesoCells*.34+fineCloud*.14);
    float spiralRipple=.5+.5*cos(2.0*phase+1.8*(cells-.5));
    float gravWave=.5+.5*cos(18.0*r-2.0*phase+1.4*distort);
    float warpAng=1.08*(cells-.5)+.46*(mesoCells-.5);

    // Four distinct, organically ragged, unequal-length/width/thickness spiral rainbands + feathered outer wisps:
    // Crisp concave inner edge facing the spiral moat, broad feathery multi-strand convex outer flank -> VIIRS Hagibis!
    float rTaper=lerp(1.0,.82,smoothstep(.28,1.32,r));
    float clusterBreak=lerp(.56,1.12,convClusters);
    float outerBreak=lerp(1.0,smoothstep(.12,.74,cells*.50+mesoCells*.34+fineCloud*.16),smoothstep(.62,1.34,r));
    float edgeWisp=smoothstep(.16,.80,mesoCells*.48+fineCloud*.34+cells*.18);

    // Arm 1: Dominant primary principal rainband (widest, longest r=0.06..1.48, brightest convective ridge)
    float a1=phase+warpAng;
    float d1=atan2(sin(a1),cos(a1));
    float w1=(d1<0?.54:1.04)*rTaper*(.84+.36*cells);
    float arm1Env=(1.0-smoothstep(.94+inflowBias*.32,1.46+inflowBias*.16,r));
    float arm1=exp(-pow(d1/w1,2.0))*arm1Env*clusterBreak*outerBreak;
    float flank1=exp(-pow(d1/(w1*(d1<0?1.26:1.72)),2.0))*arm1Env*edgeWisp*.46;

    // Arm 2: Secondary principal rainband (+148 deg asymmetric offset, medium length r=0.07..1.24, feathered outer flank)
    float a2=phase+2.58+warpAng*1.12+.24*sin(3.4*r);
    float d2=atan2(sin(a2),cos(a2));
    float w2=(d2<0?.38:.80)*rTaper*(.80+.40*mesoCells);
    float arm2Env=(1.0-smoothstep(.68+inflowBias*.26,1.22+inflowBias*.16,r));
    float arm2=exp(-pow(d2/w2,2.0))*arm2Env*.94*lerp(.50,1.12,convClusters)*outerBreak;
    float flank2=exp(-pow(d2/(w2*(d2<0?1.22:1.68)),2.0))*arm2Env*edgeWisp*.42;

    // Arm 3: Outer asymmetric convergence feeder tail (+256 deg offset, long outer inflow sweep r=0.24..1.52!)
    float a3=phase+4.47+warpAng*1.18-.24*cos(3.0*r);
    float d3=atan2(sin(a3),cos(a3));
    float w3=(d3<0?.36:.72)*lerp(1.0,.84,smoothstep(.45,1.35,r))*(.78+.42*cells);
    float arm3Env=smoothstep(.16,.38,r)*(1.0-smoothstep(.96+inflowBias*.28,1.50+inflowBias*.12,r))*(.56+.54*inflowBias);
    float arm3=exp(-pow(d3/w3,2.0))*arm3Env*.86*smoothstep(.14,.76,cells*.52+mesoCells*.48);
    float flank3=exp(-pow(d3/(w3*(d3<0?1.22:1.66)),2.0))*arm3Env*edgeWisp*.38;

    // Arm 4: Inner comma CDO spiral rib (+66 deg, r=0.06..0.64 -> merges with Arm 1 into an asymmetric comma CDO!)
    float a4=phase+1.15+warpAng*.85;
    float d4=atan2(sin(a4),cos(a4));
    float w4=(d4<0?.40:.70)*(.86+.28*cells);
    float arm4Env=(1.0-smoothstep(.36,.68,r));
    float arm4=exp(-pow(d4/w4,2.0))*arm4Env*.90*lerp(.68,1.06,cells);
    float flank4=exp(-pow(d4/(w4*(d4<0?1.20:1.56)),2.0))*arm4Env*edgeWisp*.44;

    // Subtle secondary sub-filament spurs peeling off convex outer rainband flanks (prevents single-snake outer arm!)
    float subSpur=pow(.5+.5*cos(2.0*phase+1.7+1.4*(mesoCells-.5)),2.4)*smoothstep(.34,.78,cells)*smoothstep(.26,.54,r)*(1.0-smoothstep(.84,1.34,r))*.58;
    float bandCore=saturate(max(max(max(arm1,arm2),max(arm3,arm4)),subSpur));
    float bandSkirt=saturate(max(max(flank1,flank2),max(flank3,flank4)));
    float bandInner=saturate(max(arm1,max(arm2*.90,arm4*.94)));

    // Asymmetric comma-wrapped inner CDO + organically warped eyewall collar:
    // Smoothly transitions from the 3D stadium eyewall into the swirling comma CDO (ZERO circular sticker donut ring!)
    float rCdo=r*(1.0+.28*(cells-.5)-.24*bandInner-.13*alongFlow+.15*upwindDry);
    float rWall=r*(1.0+.16*(cells-.5)-.10*bandInner);
    float innerSwirlRelief=lerp(1.0,.82+.18*saturate(bandInner*.85+spiralRipple*.35),smoothstep(.12,.28,r));
    float cdoComma=(1.0-smoothstep(.15,.41,rCdo))*lerp(.48,1.0,maturity)*innerSwirlRelief*(.90+.10*gravWave);
    float innerWall=(1.0-smoothstep(.080,.245,rWall))*(.90+.10*gravWave);
    float wall=max(innerWall,cdoComma);
    float eye=lerp(1.0,smoothstep(.022,.076,r+.007*distort),maturity);

    // Natural inter-band spiral moats: soft feathered rainband skirts, curled boundary-layer cumulus streets (1.2-2.8 km),
    // and gentle misty inflow veil softening close-up canyon walls while preserving crisp spiral channels from orbit!
    float moatMask=saturate(1.0-smoothstep(.14,.68,max(wall,bandCore)));
    float innerCrease=(1.0-smoothstep(.22,.84,r))*lerp(.23,.50,smoothstep(.19,.82,convClusters*.65+fineCloud*.35))*moatMask*(1.0-.42*upwindDry);
    float streetWave=.5+.5*cos(5.0*phase+2.0*(fineCloud-.5)+1.2*(mesoCells-.5));
    float moatStreets=smoothstep(.23,.73,mesoCells*.43+fineCloud*.36+streetWave*.21);
    float moatHaze=moatMask*(1.0-smoothstep(.42,1.36,r))*(.09+.23*smoothstep(.24,.76,cells*.55+fineCloud*.45))*lerp(.88,.44,upwindDry);
    float moatCumulus=moatMask*(1.0-smoothstep(.56,1.38,r))*moatStreets*lerp(.55,.28,upwindDry);
    float lowMoat=max(max(innerCrease,moatCumulus),max(moatHaze,bandSkirt*.90));

    float organizedBody=max(max(wall,bandCore),lowMoat);
    float bodyCover=lerp(saturate(cells*.76+.24*bandCore),organizedBody,organized);
    float low=eye*saturate(bodyCover*(.94+.08*spiralRipple));

    // Distinct 3D height hierarchy: towering 11-12 km Cb ridges on eyewall & principal rainbands,
    // mid-height convective shelves along feathered outer band flanks, shallow 1.2-2.8 km cumulus streets in moats!
    float flankConv=bandSkirt*(.30+.18*cells);
    float bandConv=max(max(wall*maturity*.98,max(arm1*.95,max(arm2*.86,max(arm3*.76,max(arm4*.88,subSpur*.62))))*(.52+.56*centre.w)),flankConv);
    float convection=eye*saturate(lerp(bandConv,.16+.16*moatStreets,moatMask*.78));
    float stormCore=eye*smoothstep(.42,.82,convection)*smoothstep(.34,.74,low);

    // Asymmetric storm envelope: tighter upwind/dry flank, sweeping downwind/inflow rainband tails
    float lowOuter=lerp(1.12,1.52,inflowBias);
    float lowBoundary=1.0-smoothstep(.58*lowOuter,lowOuter,r+.14*distort);
    float bandReach=saturate(max(bandCore*1.16,bandSkirt*.95))*(1.0-smoothstep(1.06,1.54,r));
    float lowInfluence=saturate(max(lowBoundary*(.72+.28*organized),bandReach))*smoothstep(.035,.44,centre.w);

    // Spiral-aligned feathery anticyclonic cirrus outflow fan:
    // Caps the eyewall, comma CDO & towering rainband crests, then fans smoothly outward along the storm's spin
    // into silky translucent downwind plumes while preserving readable inter-band spiral channels!
    float outflowTurn=turn+1.15*pow(max(r-.16,0.0),1.26);
    float icePhase=bearingInStorm+spin*outflowTurn+.92*(cells-.5)+.45*distortA;
    float2 outFlowPos=float2(cos(icePhase),sin(icePhase))*(r*1.15);
    float outflowWisps=Fbm(float3(outFlowPos*4.2,3.1)+seed);
    float outflowRibbonA=pow(.5+.5*cos(2.0*icePhase+1.2*(outflowWisps-.5)),1.7)*smoothstep(.20,.74,outflowWisps);
    float outflowRibbonB=pow(.5+.5*cos(3.0*icePhase+1.6+1.1*(fineCloud-.5)),2.0)*smoothstep(.26,.78,outflowWisps)*.78;
    float outflowPlumes=max(outflowRibbonA,outflowRibbonB)*smoothstep(.14,.42,r)*(1.0-smoothstep(.92,1.54,r));
    // Silky translucent anticyclonic cirrus outflow fan: keeps lush swirling storm canopy while
    // allowing curved convective rainband billows and inter-band low clouds to read cleanly beneath.
    float cdoIce=(1.0-smoothstep(.16,.42,rCdo))*(.76+.16*saturate(outflowRibbonA*.60+spiralRipple*.40))*innerSwirlRelief;
    float bandIce=saturate(arm1*.88+arm2*.76+arm3*.56+arm4*.80+subSpur*.54+bandSkirt*.38);
    float fanAsym=.58+.52*inflowBias*(1.0-.38*upwindDry);
    float moatCirrusKeep=lerp(1.0,.48,moatMask*smoothstep(.22,.56,r));
    float upper=eye*saturate(max(cdoIce*maturity,max(bandIce*.83,outflowPlumes*.81*fanAsym))*moatCirrusKeep*(.60+.48*organized));
    float outOuter=lerp(1.22,1.58,inflowBias);
    float outBoundary=1.0-smoothstep(.70*outOuter,outOuter,r+.14*distort);
    float outInfluence=saturate(max(outBoundary,bandReach*.90))*smoothstep(.035,.46,centre.w);

    weather.rb=lerp(weather.rb,float2(low,.14+.86*convection),lowInfluence);
    weather.a=lerp(weather.a,stormCore,lowInfluence);
    // Preserve readable spiral moats while blending outer storm cirrus smoothly into ambient upper clouds
    weather.g=lerp(weather.g,max(weather.g*(.20+.26*(1.0-moatMask)),upper),outInfluence);
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
    // Planetary-scale swirl (0.25) + synoptic eddy curl (0.095) bends cloud sheets into authentic EPIC
    // sweeping C-arcs, S-ribbons & hooked cyclonic eddies without over-fragmenting continuous bands!
    float3 psi1=float3(
     Fbm(p*1.92+wander*.50+seed+17.0),
     Fbm(p*1.92+wander*.50+seed+43.0),
     Fbm(p*1.92+wander*.50+seed+71.0))-.5;
    float3 curl1=cross(p,psi1);
    float3 p1=normalize(p+curl1*.25);

    float3 psi2=float3(
     Noise3(Turn(p1)*4.3+seed+11.0),
     Noise3(Turn(p1)*4.3+seed+29.0),
     Noise3(Turn(p1)*4.3+seed+53.0))-.5;
    float3 curl2=cross(p1,psi2);
    float3 pFlow=normalize(p1+curl2*.095);

    // 1. Multi-scale TRUE spherical flow-combed fields (3-tap tangent LIC along local climate wind!).
    float3 flowDir=normalize(lerp(windAxis,jetAxis,.42)+float3(1e-5,0,0));
    float licStep=lerp(.016,.032,windSpd);
    float3 pFlowP=normalize(pFlow+flowDir*licStep);
    float3 pFlowM=normalize(pFlow-flowDir*licStep);
    // Subtle planetary-scale synoptic clustering modulation (gently shapes clear basins without carving bald holes!)
    float synCluster=Fbm(p1*1.38+wander*.38+seed*.55+float3(11.3,2.7,8.4));
    float planetary=Fbm(p1*1.78+wander*.46+seed*.7+float3(3.1,7.4,1.9));
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

    // 1b. Geostrophic Baroclinic Sweeping Fluid Bands (restores 79e6fa2 continuous 3,000-6,000 km sweeping arcs
    // with broad translucent vapor aprons surrounding luminous fibrous spines!):
    float eqWeight=smoothstep(.04,.44,length(radial.xz));
    float yStretch1=lerp(1.85,5.75,eqWeight);
    float yStretch2=lerp(2.20,8.05,eqWeight);
    float3 pWave=normalize(p+curl1*.36+curl2*.075);
    float hemiDiag=.28*(latitude/(abs(latitude)+.30));
    float baroY=pWave.y+hemiDiag*sin(longitude*2.0+psi1.x*2.4)*eqWeight*.18+.13*psi1.y;
    float3 qBand1=float3(pWave.x*1.35,baroY*yStretch1,pWave.z*1.35);
    float3 qBand2=float3(pWave.x*2.52,baroY*yStretch2,pWave.z*2.52);
    float bandField1=Fbm(qBand1+wander*.40+seed+float3(13.4,27.1,5.8));
    float bandField2=Noise3(qBand2+wander*.56+seed+float3(47.2,11.8,19.4))*.60+
                     Noise3(qBand2*1.85+wander*.68+seed+float3(19.1,5.7,31.4))*.40;
    float bandH=bandField1*.55+bandField2*.25+flowBands*.13+mesoCells*.04+fineGrain*.03;
    float bandVeil=smoothstep(.34,.74,bandH);
    float bandSpine=smoothstep(.52,.84,bandH*.72+flowBands*.18+fineGrain*.10);
    float ribbonSilk=.42+.58*smoothstep(.16,.84,flowBands*.58+fineGrain*.42);
    o.curvedBand=saturate(bandVeil*.56+bandSpine*.54*ribbonSilk);
    o.curvedWisp=saturate((bandVeil*.68+bandSpine*.48)*ribbonSilk);

    // 2. Regime A: Subtropical Marine Stratocumulus Closed/Open-Cell Mesoscale Envelope & Trade Streets
    // Keep 2D cache frequencies strictly below 1024x512 Nyquist (k<=66) so low-orbit marine Sc has ZERO aliasing;
    // crisp 7-26 km polygonal closed-cell cobblestone & open-cell actiniform rings are sculpted in full 3D Worley!
    float streetRipple=(Noise3(Turn(pFlow)*46.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow+windAxis*.020))*46.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow-windAxis*.020))*46.0+seed+5.0))*(1.0/3.0);
    float mesoFoam=Noise3(Turn(pFlow)*66.0+seed+13.0);
    float streetEnv=smoothstep(.24,.64,planetary*.42+synoptic*.30+bandField1*.28);
    o.tradeStreets=saturate(planetary*.23+synoptic*.19+streetRipple*.24+smoothstep(.35,.72,mesoFoam)*.42*streetEnv);

    // Coherent eastern-boundary marine Sc sheets with organic mesoscale Pockets of Open Cells (POCs)
    float scDeckMacro=smoothstep(.24,.64,synCluster*.26+planetary*.44+synoptic*.30);
    float scPocRift=smoothstep(.28,.72,Noise3(Turn(pFlow)*14.0+seed+29.0)*.62+mesoCells*.38);
    float scBreak=smoothstep(.30,.72,mesoFoam*.55+streetRipple*.45);
    o.honeycomb=saturate(scDeckMacro*(.52+.28*scPocRift+.22*scBreak));

    o.planetary=planetary;
    o.synCluster=synCluster;
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
    float hotTowers=smoothstep(.36,.74,mesoCells*.50+mesoFoam*.34+fineGrain*.16);
    float mcsEnv=smoothstep(.22,.70,max(itczSlender*1.04,planetary*.24+synoptic*.28+itczRibbon*.48));
    o.mcsCluster=saturate(mcsEnv*(.38+.62*hotTowers));
    float3 pAnvilNear=normalize(pFlow-jetAxis*.058);
    float3 pAnvilFar=normalize(pFlow-jetAxis*.155);
    float anvilNear=Fbm(pAnvilNear*4.6+wander*.65+seed);
    float anvilFar=Fbm(pAnvilFar*2.9+wander*.55+seed);
    float anvilSilk=(Noise3(Turn(pAnvilNear)*26.0+seed+4.0)+
                     Noise3(Turn(normalize(pAnvilNear+jetAxis*.022))*26.0+seed+4.0)+
                     Noise3(Turn(normalize(pAnvilNear-jetAxis*.022))*26.0+seed+4.0))*(1.0/3.0);
    o.anvilShield=smoothstep(.28,.70,anvilNear*.40+anvilFar*.32+anvilSilk*.28)*(.38+.62*mcsEnv);

    // 4. Regime C: NASA EPIC / VIIRS Sweeping Curved Mid-Latitude Frontal Banners + Bold 2.5-Turn Spiral Comma Cyclones
    // + Planetary-Scaled Rossby Storm Tracks + Gentle Polar Cap Leads + Post-Frontal Open Cells!
    float hemiSign=latitude>=0?1.0:-1.0;
    float3 hSeed=seed+(latitude>=0?float3(0,0,0):float3(19.7,41.3,13.9));
    float k1=max(2.0,floor(rossbyK+.35+.5));
    float k2=k1+1.0;
    float eraLon1=6.2831853*Hash3(float3(eraId,9.0+hemiSign*2.0,2));
    float eraLon2=6.2831853*Hash3(float3(eraId,15.0+hemiSign*3.0,6));
    float packPhase=6.2831853*Hash3(float3(eraId,23.0+hemiSign*5.0,11));

    // Coherent Rossby wave-packet modulation matching 79e6fa2 continuity while preserving anticyclonic clear basins
    float packetWave=.58*cos(longitude-.35*age+packPhase)+.42*cos(2.0*longitude+.22*age+eraLon2);
    float packetEnv=smoothstep(-.48,.42,packetWave+(planetary*.65+synCluster*.35-.5)*1.05);

    // Un-truncated storm track latitude follows planetary polarLatRad (supports slow-rotator 0.45x double-cell up to ~75 deg!)
    float rossbyMeander=.125*sin(k1*longitude-.42*age+hSeed.y)+.065*cos(k2*longitude+.30*age+hSeed.z)+.05*(synCluster-.5);
    float stormLatRad=clamp(polarLatRad*.85+rossbyMeander,.54,1.32);
    float theta=k1*longitude-.88*age+eraLon1+.60*sin(k2*longitude-1.12*age+eraLon2)+.32*sin(longitude+hSeed.x);
    float waveS=sin(theta),waveC=cos(theta);
    float waveFade=smoothstep(-.88,-.06,waveC)*(.44+.56*packetEnv);
    float dx=atan2(waveS,waveC)/max(1.4,k1+.14)*max(.24,cos(latitude));
    float dy=(abs(latitude)-stormLatRad);

    // Boldly wound 2.5-turn spiral occluded comma cyclone with dramatic dark-blue dry-intrusion channel
    float2 vortVec=float2(dx+.052+.032*(synoptic-.5),dy-.048+.028*(planetary-.5));
    float rVort=length(vortVec)/lerp(.32,.45,saturate(3.4-k1));
    float vortAng=atan2(vortVec.y,vortVec.x+1e-6)+4.60*log(1.0+8.0*rVort)+.62*(synoptic-.5)+.32*(flowBands-.5);
    float spiralWave=cos(vortAng);
    float spiralPrimary=smoothstep(-.14,.69,spiralWave);
    float spiralSecondary=smoothstep(.15,.85,cos(2.0*vortAng+1.15))*.72;
    float commaCollar=exp(-pow((rVort-.16)/.092,2.0))*.80;
    float commaHead=(1.0-smoothstep(.60,1.38,rVort))*smoothstep(.024,.090,rVort)*max(max(spiralPrimary,spiralSecondary),commaCollar);
    float spiralDryTongue=smoothstep(.00,.66,-spiralWave)*(1.0-smoothstep(.18,1.24,rVort))*smoothstep(.018,.082,rVort);
    float vortexEyeClear=(1.0-smoothstep(.018,.078,rVort))*.92;

    // Sweeping 5,500 km curved cold-front banner reaching equatorward into subtropics
    float3 frontTangent=normalize(east*.67+north*hemiSign*.74+1e-5);
    float frontSilkA=(Noise3(Turn(pFlow)*22.0+hSeed)+
                      Noise3(Turn(normalize(pFlow+frontTangent*.028))*22.0+hSeed)+
                      Noise3(Turn(normalize(pFlow-frontTangent*.028))*22.0+hSeed))*(1.0/3.0);
    float frontSilkB=(Noise3(Turn(pFlow)*48.0+hSeed+7.0)+
                      Noise3(Turn(normalize(pFlow+frontTangent*.018))*48.0+hSeed+7.0))*.5;
    float frontFilament=frontSilkA*.62+frontSilkB*.38;

    float dxFront=-.01+1.00*(dy-.04)-.47*(dy-.04)*abs(dy-.04);
    float dFront=dx-dxFront+.036*(synoptic-.5)+.021*(frontFilament-.5);
    // Asymmetric cold front: crisp drop on cold/rear side (dFront < 0), graceful multi-strand veil on warm/front side (dFront > 0)
    float frontWidth=dFront<0?.058:.092;
    float coldFront=exp(-pow(dFront/frontWidth,2.0))*smoothstep(-.62,-.18,dy)*(1.0-smoothstep(.16,.36,dy))*(.74+.42*frontFilament);
    float coldFrontApron=exp(-pow((dFront-.054)/.148,2.0))*smoothstep(-.58,-.12,dy)*(1.0-smoothstep(.18,.38,dy))*.64*(.52+.48*frontFilament);

    float dWarm=(dy-(.072-.53*max(0.0,dx+.02)))+.029*(synoptic-.5);
    float warmFront=exp(-pow(dWarm/.086,2.0))*smoothstep(-.10,.04,dx)*(1.0-smoothstep(.28,.52,dx))*(.68+.44*frontFilament);

    float wcb=smoothstep(-.02,.11,dFront)*(1.0-smoothstep(.12,.40,dFront))*smoothstep(-.36,.22,dy)*(1.0-smoothstep(.24,.46,dy));
    float drySlot=exp(-pow((dFront+.086)/.076,2.0))*smoothstep(-.50,-.04,dy)*(1.0-smoothstep(.16,.38,dy));

    // Post-frontal cold-air wake envelope (triggers crisp 3D Worley open-cell cumulus lace in ScrollCloudSource!)
    float coldWake=smoothstep(.04,.17,-dFront)*(1.0-smoothstep(.40,.78,-dFront))*smoothstep(-.50,.25,dy);
    float wakeLoop=saturate(coldWake+spiralDryTongue*.84);
    float openCellPatch=smoothstep(.22,.64,Noise3(Turn(pFlow)*18.0+hSeed+17.0)*.58+mesoCells*.42);
    float openCellMeso=smoothstep(.34,.72,mesoFoam*.60+fineGrain*.40);
    float postCells=wakeLoop*(.46+.54*openCellMeso)*(.50+.50*openCellPatch)*.90;

    // Gentle high-latitude polar-cap leads strictly poleward of the storm track (scales with polarLatRad so
    // mid-latitude Pacific/Southern Ocean frontal banners and 0.45x slow-rotator polar fronts are NEVER truncated!)
    float polarZoneStart=sin(clamp(polarLatRad*.98,.92,1.34));
    float polarZoneEnd=sin(clamp(polarLatRad*1.16,1.16,1.50));
    float highLatZone=smoothstep(polarZoneStart,polarZoneEnd,abs(radial.y));
    float polarTroughWave=Fbm(pWave*2.55+wander*.44+hSeed+float3(23.1,9.4,15.7));
    float polarSwirlLead=Noise3(p1*3.4+wander*.52+hSeed+float3(5.2,31.7,11.1));
    float polarLead=smoothstep(.48,.80,1.0-(polarTroughWave*.65+polarSwirlLead*.35)+.12*(1.0-bandField1));
    o.polarCarve=highLatZone*polarLead;

    o.frontalLow=waveFade*saturate(max(max(commaHead*(.92+.16*frontFilament),max(coldFront,coldFrontApron)),warmFront*.94)+.56*wcb*(.55+.45*frontFilament));
    o.postFrontCells=waveFade*postCells;
    o.frontalMid=waveFade*saturate(warmFront+.82*wcb+commaHead*.68+coldFrontApron*.54);
    o.frontalDry=waveFade*saturate(max(drySlot,max(spiralDryTongue*.96,vortexEyeClear)));
    o.wcbCirrus=waveFade*saturate(wcb*(.76+.34*frontFilament)+commaHead*.92+warmFront*.78+coldFront*.72+coldFrontApron*.60);
    return o;
   }
   float4 frag(v2f_img i):SV_Target
   {
    float4 monthly=UNITY_SAMPLE_TEX2D(_ClimateTex,i.uv);
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
    float4 aux=float4(0,0,0,.55);
    float3 jetAxis=normalize(float3(.84,.25,.48));
    if(_GlebaCloudEnable>.5)
    {
     forcing=UNITY_SAMPLE_TEX2D_SAMPLER(_CloudSynopticTex,_ClimateTex,i.uv);
     forcing=lerp(lerp(_GlebaForcingPoleSouth,_GlebaForcingPoleNorth,step(.5,i.uv.y)),forcing,poleBlend);
     aux=UNITY_SAMPLE_TEX2D_SAMPLER(_CloudSynopticAuxTex,_ClimateTex,i.uv);
     float4 uWind=UNITY_SAMPLE_TEX2D_SAMPLER(_UpperWindTex,_ClimateTex,i.uv);
     float2 uFlow=lerp(uWind.rg,uWind.ba,_MonthBlend);
     float3 jetVec=east*uFlow.x+north*uFlow.y;
     float jetLen=length(jetVec);
     if(jetLen>1e-3&&length(radial.xz)>.08)jetAxis=jetVec/jetLen;
    }
    // Decode un-truncated local polar-front latitude (30..88 deg) and synchronize with planetary circulation terminus
    float auxPolarRad=(30.0+58.0*aux.a)*.01745329;
    float polarLatRad=_GlebaCloudEnable>.5&&_GlebaCirculationParams.y>.2
        ? clamp(_GlebaCirculationParams.y+(auxPolarRad-_GlebaCirculationParams.y)*.45,.52,1.52)
        : auxPolarRad;
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

     // ISCCP joint optical-depth partition matching 79e6fa2 natural clear/veil/core balance:
     float highShare=saturate(.58*itczConv+.52*stormTrack+.26*moistEnv)*(1.0-.78*marineSc)*(1.0-.66*leeShadow);
     float climTarget=saturate(synLift+.11*response.x*(1.0-.50*lowLee));
     float lowTarget=saturate(climTarget*(1.0-.35*highShare));
     float highTarget=saturate((climTarget-lowTarget)/max(.14,1.0-lowTarget)+.22*highShare*climTarget);

     // Five authentic NASA DSCOVR EPIC & VIIRS continuous morphological regimes:
     // Very gentle synCluster preserves 79e6fa2 unbroken sweeping band continuity without carving bald gaps!
     float clusterBias=(synCluster-.495)*.18;
     float zBase=(baseTex-.496)*5.4+clusterBias;
     float zRibbon=(baseTex-.498)*3.4+clusterBias*.45+3.80*(curvedBand-.275)+1.08*(curvedWisp-.235);
     float zStreets=(tradeStreets-.496)*5.6+clusterBias*.35+2.25*(curvedBand-.26);
     float zHoney=(honeycomb-.46)*5.4+clusterBias*.30+1.15*(curvedBand-.26)+.75*scProtect;
     float zMcs=.45*zRibbon+.21*zStreets+2.60*(mcsCluster-.185)+clusterBias*.25;
     // Sweeping curved comma spirals + coiled dark-blue spiral dry slots + bold 5,500 km curved frontal banners
     float zFront=.24*zRibbon+5.45*(frontalLow-.15)-5.30*frontalDry-1.15*polarCarve;
     // Hadley/polar-scaled & moisture-gated mid-latitude storm band (never fabricates fronts in dry zones or fixed latitudes!)
     float midSin0=sin(clamp(hadleyRad*.74,.28,.88));
     float midSin1=sin(clamp(hadleyRad*1.24,.52,.96));
     float polCap0=sin(clamp(polarLatRad*1.08,1.10,1.46));
     float polCap1=sin(clamp(polarLatRad*1.20,1.32,1.54));
     float midMoistGate=smoothstep(.16,.48,moistEnv)*smoothstep(.14,.42,climTarget);
     float midLatBoost=smoothstep(midSin0,midSin1,abs(radial.y))*(1.0-smoothstep(polCap0,polCap1,abs(radial.y)))*midMoistGate;
     float stormWeight=saturate(smoothstep(.08,.48,stormTrack+.18*midLatBoost)*(1.0-.75*leeShadow));

     float zLow=lerp(zBase,zRibbon,.74);
     zLow=lerp(zLow,zStreets,.38*tropBelt);
     zLow=lerp(zLow,zMcs,saturate(tropBelt*(.30+.70*itczConv)));
     zLow=lerp(zLow,zHoney,saturate(scProtect*.92));
     zLow=lerp(zLow,zFront,stormWeight);
     float orogMoist=smoothstep(.16,.48,moistEnv);
     zLow+=.82*orogCrest*(.30+.70*smoothstep(.22,.78,baseTex))*orogMoist;
     float uLow=rcp(1.0+exp(-1.19*clamp(zLow,-5.5,5.5)));

     // EPIC Continuous Three-Tier Atmospheric Vapor Distribution restoring 79e6fa2 full-disk balance:
     //   1. broadVeil: airy pearly translucent mist & soft thin-cloud gradients bridging sweeping cloud bands
     //   2. coherentBody: unbroken curved fronts, ITCZ/SPCZ ribbons & eastern-boundary marine Sc decks
     //   3. brightCore: snow-white convective knots, comma heads & windward orographic cap clouds
     float sharpOrg=saturate(max(stormWeight*frontalLow*1.06,itczConv*mcsCluster));
     float dryGuard=smoothstep(.10,.35,climTarget+.16*scProtect)*(1.0-.84*lowLee);
     float polarDryCarve=saturate(stormWeight*frontalDry*smoothstep(midSin0,midSin1,abs(radial.y))+polarCarve*.38);
     float blanketRelief=smoothstep(.68,.92,lowTarget)*(1.0-smoothstep(.26,.66,synCluster*.35+planetary*.65))*(1.0-.90*sharpOrg);
     float effLowTarget=saturate(min(.86,lowTarget*(1.0-.58*polarDryCarve-.08*blanketRelief)+.14*scProtect*smoothstep(.24,.64,synCluster*.30+planetary*.70)+.18*orogCrest*orogMoist*(1.0-.70*leeShadow))+.045*smoothstep(.24,.60,climTarget)*(1.0-lowLee));
     float veilTarget=saturate(min(.86,effLowTarget*1.28+.102*dryGuard)*(1.0-.62*polarDryCarve));
     float wVeil=max(1e-4,min(.44,min(veilTarget,1.0-veilTarget)+.09));
     float rawVeil=smoothstep(1.0-veilTarget-wVeil,1.0-veilTarget+wVeil,uLow);
     float wCore=max(1e-4,min(.24,min(effLowTarget,1.0-effLowTarget)));
     float rawCore=smoothstep(1.0-effLowTarget-wCore,1.0-effLowTarget+wCore,uLow);

     // Wind-combed silky texture + organic eastern-boundary marine Sc decks + sweeping curved fronts
     // + post-frontal open-cell wakes + sinuous Pacific ITCZ / SPCZ + moisture-gated orographic windward cap clouds!
     float silkTex=flowBands*.54+fineGrain*.30+mesoCells*.16;
     float veilLayer=rawVeil*lerp(.31,.56,smoothstep(.16,.84,silkTex))*dryGuard;
     float bodyLayer=rawCore*(.64+.36*smoothstep(.18,.82,silkTex));
     float denseCore=smoothstep(.44,.82,rawCore)*(sharpOrg*.88+.12)*(.92+.08*smoothstep(.22,.78,mesoCells));
     float frontDirect=stormWeight*smoothstep(.24,.72,frontalLow)*(1.0-.85*frontalDry)*smoothstep(.10,.38,climTarget+.12*midLatBoost)*.84;
     float postWakeLace=stormWeight*postFrontCells*smoothstep(.08,.40,climTarget+.12*midLatBoost)*.82;
     // Localized eastern-boundary marine stratocumulus sheet (closed-cell cobbles nearshore -> open-cell POCs seaward!)
     float scDeckDirect=scProtect*smoothstep(.20,.62,honeycomb)*smoothstep(.18,.52,moistEnv+.28*scProtect)*(1.0-.72*leeShadow*(1.0-.55*scProtect))*.80;
     float oceanTradeGate=smoothstep(.16,.42,climTarget)*smoothstep(.28,.58,moistEnv)*(1.0-.88*lowLee);
     float tradePatchEnv=smoothstep(.34,.67,planetary*.48+curvedBand*.52);
     float tradeFineField=smoothstep(.40,.68,tradeStreets*.64+honeycomb*.36);
     float marineTapestry=oceanTradeGate*tradePatchEnv*tradeFineField*lerp(.40,.64,saturate(marineSc+.38*tropBelt));
     float itczDirectLift=smoothstep(.12,.50,itczConv)*smoothstep(.36,.80,mcsCluster)*(1.0-.82*leeShadow)*.82;
     float orogDirectCap=orogCrest*smoothstep(.16,.52,moistEnv)*(1.0-.78*leeShadow)*(.44+.56*smoothstep(.20,.78,flowBands*.55+mesoCells*.45))*.76;
     low=saturate(max(max(veilLayer,bodyLayer),max(max(max(denseCore,frontDirect),max(postWakeLace,orogDirectCap)),max(max(marineTapestry,scDeckDirect),itczDirectLift)))+
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
     float zWcb=.28*zJet+3.70*(wcbCirrus-.14)+2.40*(curvedCirrus-.24)+(wisps-.48)*2.0-1.05*polarCarve;
     float zHigh=lerp(zJet,zAnvil,saturate(itczConv*1.15));
     zHigh=lerp(zHigh,zWcb,stormWeight);
     float uHigh=rcp(1.0+exp(-1.16*clamp(zHigh,-5.5,5.5)));
     float highEnvTarget=saturate(highTarget*1.20+.045*dryGuard)*(1.0-.48*polarDryCarve);
     float wHigh=max(1e-4,min(.42,min(highEnvTarget,1.0-highEnvTarget)+.06));
     float rawHigh=smoothstep(1.0-highEnvTarget-wHigh,1.0-highEnvTarget+wHigh,uHigh);
     float highSilk=.34+.66*smoothstep(.18,.84,wisps*.64+fineGrain*.36);
     float anvilOutflow=smoothstep(.16,.56,itczConv)*anvilShield*.70;
     high=smoothstep(.05,.92,saturate(max(rawHigh*highSilk*1.13,anvilOutflow)))*(1.0-.78*tropBelt*leeShadow);
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
