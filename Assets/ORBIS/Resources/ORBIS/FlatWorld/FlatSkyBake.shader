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
  [HideInInspector] _RegionalEvapReady("Use RegionalCloudResponse.a [0,1] evaporation signal",Float)=0
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
   float _VisualTime,_MonthBlend,_ClimateRows,_GlebaCloudEnable,_RegionalEvapReady;
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
   float2 FastSphereUV(float3 dir)
   {
    float3 d=normalize(dir);
    float u=frac(atan2(d.x,d.z)*.15915494309+.5);
    if(abs(d.x)+abs(d.z)<1e-6)u=.5;
    return float2(u,asin(clamp(d.y,-1.0,1.0))*.31830988618+.5);
   }
   float2 NormalizeUpperFlow(float2 uRaw,float itczConv)
   {
    // Plan B unified wind units (1.0 = 9.0 m/s): _UpperWindTex carries physical m/s Rossby jet
    // and tropical easterly outflow; linear /9.0 matches _ClimateWindTex without weak-wind inflation.
    return uRaw*(1.0/9.0);
   }
   float3 SphereTangentWind(float2 uv,float2 flow2)
   {
    float lon=(uv.x-.5)*6.283185307,lat=(uv.y-.5)*3.141592654;
    float sl,cl,sp,cp;sincos(lon,sl,cl);sincos(lat,sp,cp);
    float3 east=float3(cl,0,-sl);
    float3 north=float3(-sp*sl,cp,-sp*cl);
    return east*flow2.x+north*flow2.y;
   }
   // Two-substep sequential spherical flow-map tracer Phi(dt/2) o Phi(dt/2) along actual _ClimateWindTex:
   // preserves diffeomorphic continuity across ITCZ convergence zones (zero midpoint-overshoot folding/comb streaks)
   // while accurately following curving trades, monsoon turns, and Rossby troughs.
   float3 AdvectSurfaceRK2(float3 radial,float3 windVec0,float dtDays,float poleDamp)
   {
    float mag0=length(windVec0);
    if(mag0<1e-4||abs(dtDays)<1e-5)return radial;
    float stepSign=dtDays>=0?1.0:-1.0;
    float halfDays=abs(dtDays)*.5;
    float theta0=min(mag0*.122053*halfDays,.65)*poleDamp;
    float3 dir0=windVec0/mag0;
    float s0,c0;sincos(theta0*stepSign,s0,c0);
    float3 p1=normalize(radial*c0-dir0*s0);
    float2 uv1=FastSphereUV(p1);
    float4 w1=tex2Dlod(_ClimateWindTex,float4(uv1,0,0));
    float2 flow1=lerp(w1.rg,w1.ba,_MonthBlend);
    float3 v1=SphereTangentWind(uv1,flow1);
    float mag1=length(v1);
    if(mag1<1e-4)return p1;
    float3 dir1=v1/mag1;
    float theta1=min(mag1*.122053*halfDays,.65)*smoothstep(0,.10,length(p1.xz));
    float s1,c1;sincos(theta1*stepSign,s1,c1);
    return normalize(p1*c1-dir1*s1);
   }
   // Two-substep sequential upstream tracer for upper-tropospheric cirrus/jet plumes along physical _UpperWindTex.
   float3 AdvectUpperRK2(float3 radial,float3 upperVec0,float dtDays,float poleDamp,float itczConv)
   {
    float mag0=length(upperVec0);
    if(mag0<1e-4||abs(dtDays)<1e-5)return radial;
    float stepSign=dtDays>=0?1.0:-1.0;
    float halfDays=abs(dtDays)*.5;
    float theta0=min(mag0*.122053*halfDays,.85)*poleDamp;
    float3 dir0=upperVec0/mag0;
    float s0,c0;sincos(theta0*stepSign,s0,c0);
    float3 p1=normalize(radial*c0-dir0*s0);
    float2 uv1=FastSphereUV(p1);
    float4 w1=tex2Dlod(_ClimateWindTex,float4(uv1,0,0));
    float2 sFlow1=lerp(w1.rg,w1.ba,_MonthBlend);
    float2 uFlow1=sFlow1;
    if(_GlebaCloudEnable>.5)
    {
     #ifdef UNITY_SAMPLE_TEX2D_SAMPLER_LOD
     float4 uw1=UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_UpperWindTex,_ClimateTex,uv1,0);
     #else
     float4 uw1=UNITY_SAMPLE_TEX2D_SAMPLER(_UpperWindTex,_ClimateTex,uv1);
     #endif
     uFlow1=NormalizeUpperFlow(lerp(uw1.rg,uw1.ba,_MonthBlend),itczConv);
    }
    float3 v1=SphereTangentWind(uv1,lerp(sFlow1,uFlow1,.55));
    float mag1=length(v1);
    if(mag1<1e-4)return p1;
    float3 dir1=v1/mag1;
    float theta1=min(mag1*.122053*halfDays,.85)*smoothstep(0,.10,length(p1.xz));
    float s1,c1;sincos(theta1*stepSign,s1,c1);
    return normalize(p1*c1-dir1*s1);
   }
   struct EraMorphology
   {
    float planetary,synCluster,baseTex,flowBands,mesoCells,fineGrain,curvedBand,curvedWisp,tradeStreets,honeycomb,mcsCluster,anvilShield,polarCarve,jetWisps,jetBanner,jetMacro;
   };
   EraMorphology SampleEraMorphology(
    float eraId,float ageDays,float3 radial,float latitude,
    float3 windVec,float3 windAxis,float windSpd,float poleDamp,float polarLatRad,float3 jetAxis,float3 upperWindVec,float itczConv)
   {
    EraMorphology o;
    // Curved RK2 semi-Lagrangian upstream advection along real surface wind (zero arbitrary time-noise wander!).
    float3 p=AdvectSurfaceRK2(radial,windVec,ageDays,poleDamp);
    float3 seed=float3(Hash3(float3(eraId,4,7)),Hash3(float3(eraId,11,3)),Hash3(float3(eraId,5,19)))*15.0;

    // Flow-coupled shear & vorticity deformation: eddies gradually roll up and stretch along wind trajectories
    // strictly proportional to real wind speed and parcel age (stationary when wind == 0, zero global boil!).
    float shearAge=clamp(ageDays*.045,-.18,.18)*windSpd;
    float3 psi1=float3(
     Fbm(p*1.92+seed+17.0),
     Fbm(p*1.92+seed+43.0),
     Fbm(p*1.92+seed+71.0))-.5;
    float3 curl1=cross(p,psi1);
    float3 p1=normalize(p+curl1*(.23+.35*shearAge));

    float3 psi2=float3(
     Noise3(Turn(p1)*4.3+seed+11.0),
     Noise3(Turn(p1)*4.3+seed+29.0),
     Noise3(Turn(p1)*4.3+seed+53.0))-.5;
    float3 curl2=cross(p1,psi2);
    float3 pFlow=normalize(p1+curl2*(.09+.28*shearAge));

    // 1. Multi-scale TRUE spherical flow-combed fields aligned with local surface/jet wind arrows.
    float3 flowDir=normalize(lerp(windAxis,jetAxis,.26)+float3(1e-5,0,0));
    float licStep=lerp(.016,.032,windSpd);
    float3 pFlowP=normalize(pFlow+flowDir*licStep);
    float3 pFlowM=normalize(pFlow-flowDir*licStep);
    float synCluster=Fbm(p1*1.38+seed*.55+float3(11.3,2.7,8.4));
    float planetary=Fbm(p1*1.78+seed*.7+float3(3.1,7.4,1.9));
    float synoptic=(Noise3(Turn(pFlow)*4.2+seed)+
                    Noise3(Turn(pFlowP)*4.2+seed)+
                    Noise3(Turn(pFlowM)*4.2+seed))*(1.0/3.0);
    float3 pWindP=normalize(pFlow+windAxis*(licStep*.95));
    float3 pWindM=normalize(pFlow-windAxis*(licStep*.95));
    float flowBands=(Noise3(Turn(pFlow)*15.5+seed*1.4)+
                     Noise3(Turn(pWindP)*15.5+seed*1.4)+
                     Noise3(Turn(pWindM)*15.5+seed*1.4))*(1.0/3.0);
    float mesoCells=Fbm(Turn(pFlow)*32.0+seed*1.9);
    float fineGrain=Noise3(Turn(pFlow)*58.0+seed*2.7)*.54+
                    Noise3(Turn(normalize(pFlow+flowDir*.016))*58.0+seed*2.7)*.46;

    // 1b. Geostrophic Baroclinic Sweeping Fluid Bands using WIND-ADVECTED longitude (zero static longitude pinning!).
    float advLon=atan2(p.x,p.z);
    float eqWeight=smoothstep(.04,.44,length(radial.xz));
    float yStretch1=lerp(1.85,5.75,eqWeight);
    float yStretch2=lerp(2.20,8.05,eqWeight);
    float3 pWave=normalize(p+curl1*(.34+.32*shearAge)+curl2*.075);
    float hemiDiag=.28*(latitude/(abs(latitude)+.30));
    float baroY=pWave.y+hemiDiag*sin(advLon*2.0+psi1.x*2.4)*eqWeight*.18+.13*psi1.y;
    float3 qBand1=float3(pWave.x*1.35,baroY*yStretch1,pWave.z*1.35);
    float3 qBand2=float3(pWave.x*2.52,baroY*yStretch2,pWave.z*2.52);
    float bandField1=Fbm(qBand1+seed+float3(13.4,27.1,5.8));
    float bandField2=Noise3(qBand2+seed+float3(47.2,11.8,19.4))*.60+
                     Noise3(qBand2*1.85+seed+float3(19.1,5.7,31.4))*.40;
    float bandH=bandField1*.55+bandField2*.25+flowBands*.13+mesoCells*.04+fineGrain*.03;
    float bandVeil=smoothstep(.34,.74,bandH);
    float bandSpine=smoothstep(.52,.84,bandH*.72+flowBands*.18+fineGrain*.10);
    float ribbonSilk=.42+.58*smoothstep(.16,.84,flowBands*.58+fineGrain*.42);
    o.curvedBand=saturate(bandVeil*.56+bandSpine*.54*ribbonSilk);
    o.curvedWisp=saturate((bandVeil*.68+bandSpine*.48)*ribbonSilk);

    // 2. Subtropical Marine Stratocumulus Closed/Open-Cell Mesoscale Envelope & Trade Streets
    float streetRipple=(Noise3(Turn(pFlow)*46.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow+windAxis*.020))*46.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow-windAxis*.020))*46.0+seed+5.0))*(1.0/3.0);
    float mesoFoam=Noise3(Turn(pFlow)*66.0+seed+13.0);
    float streetEnv=smoothstep(.24,.64,planetary*.42+synoptic*.30+bandField1*.28);
    o.tradeStreets=saturate(planetary*.23+synoptic*.19+streetRipple*.24+smoothstep(.35,.72,mesoFoam)*.42*streetEnv);

    float scPocRift=smoothstep(.28,.72,Noise3(Turn(pFlow)*14.0+seed+29.0)*.62+mesoCells*.38);
    float scBreak=smoothstep(.30,.72,mesoFoam*.55+streetRipple*.45);
    float scMesoEnv=smoothstep(.22,.74,scPocRift*.48+flowBands*.32+synoptic*.20);
    o.honeycomb=saturate((.34+.66*scMesoEnv)*(.46+.32*scPocRift+.22*scBreak));

    o.planetary=planetary;
    o.synCluster=synCluster;
    o.flowBands=flowBands;
    o.mesoCells=mesoCells;
    o.fineGrain=fineGrain;
    o.baseTex=planetary*.32+synoptic*.30+bandField1*.20+flowBands*.12+mesoCells*.04+fineGrain*.02;

    // 3. Sinuous Continuous Pacific ITCZ / SPCZ Line using wind-advected advLon + downwind jet anvil plumes
    float3 qItcz=float3(pWave.x*1.35,(pWave.y+hemiDiag*.22*sin(advLon*1.8+psi1.x*2.0)+.07*psi1.z)*lerp(2.2,9.2,eqWeight),pWave.z*1.35);
    float itczField=Fbm(qItcz+seed+float3(5.3,1.7,8.9));
    float itczRibbon=(Noise3(Turn(pFlow)*15.0+seed+float3(5.3,1.7,8.9))+
                      Noise3(Turn(pFlowP)*15.0+seed+float3(5.3,1.7,8.9))+
                      Noise3(Turn(pFlowM)*15.0+seed+float3(5.3,1.7,8.9)))*(1.0/3.0);
    float itczSlender=smoothstep(.38,.74,itczField*.64+itczRibbon*.26+fineGrain*.10);
    float hotTowers=smoothstep(.36,.74,mesoCells*.50+mesoFoam*.34+fineGrain*.16);
    float mcsEnv=smoothstep(.22,.70,max(itczSlender*1.04,planetary*.24+synoptic*.28+itczRibbon*.48));
    o.mcsCluster=saturate(mcsEnv*(.38+.62*hotTowers));

    // Upper-level anvil & jet cirrus advected along ACTUAL upper-tropospheric wind (_UpperWindTex)!
    float3 upper=AdvectUpperRK2(radial,upperWindVec,ageDays,poleDamp,itczConv);
    float3 pAnvilNear=normalize(upper-jetAxis*.045);
    float3 pAnvilFar=normalize(upper-jetAxis*.135);
    float anvilNear=Fbm(pAnvilNear*4.6+seed);
    float anvilFar=Fbm(pAnvilFar*2.9+seed);
    float anvilSilk=(Noise3(Turn(pAnvilNear)*26.0+seed+4.0)+
                     Noise3(Turn(normalize(pAnvilNear+jetAxis*.022))*26.0+seed+4.0)+
                     Noise3(Turn(normalize(pAnvilNear-jetAxis*.022))*26.0+seed+4.0))*(1.0/3.0);
    o.anvilShield=smoothstep(.28,.70,anvilNear*.40+anvilFar*.32+anvilSilk*.28)*(.38+.62*mcsEnv);

    // Polar cap leads advected along surface wind (preserves polar clouds without static carving!)
    float3 hSeed=seed+(latitude>=0?float3(0,0,0):float3(19.7,41.3,13.9));
    float polarZoneStart=sin(clamp(polarLatRad*.98,.92,1.34));
    float polarZoneEnd=sin(clamp(polarLatRad*1.16,1.16,1.50));
    float highLatZone=smoothstep(polarZoneStart,polarZoneEnd,abs(radial.y));
    float polarTroughWave=Fbm(pWave*2.55+hSeed+float3(23.1,9.4,15.7));
    float polarSwirlLead=Noise3(p1*3.4+hSeed+float3(5.2,31.7,11.1));
    float polarLead=smoothstep(.48,.80,1.0-(polarTroughWave*.65+polarSwirlLead*.35)+.12*(1.0-bandField1));
    o.polarCarve=highLatZone*polarLead;

    // Upper-tropospheric Rossby jet streaks & cirrus banners advected strictly by physical upper wind!
    float3 crossJet=normalize(cross(upper,jetAxis)+float3(1e-5,0,0));
    float3 jetShear=upper;
    float jetStreak=(Noise3(Turn(jetShear)*15.0+seed*.35+float3(9,4,2))+
                     Noise3(Turn(normalize(jetShear+jetAxis*.024))*15.0+seed*.35+float3(9,4,2))+
                     Noise3(Turn(normalize(jetShear-jetAxis*.024))*15.0+seed*.35+float3(9,4,2)))*(1.0/3.0);
    float crossStreak=(Noise3(Turn(jetShear)*19.0+seed*.35+float3(3,2,7))+
                       Noise3(Turn(normalize(jetShear+crossJet*.018))*19.0+seed*.35+float3(3,2,7)))*.5;
    float poleTaper=smoothstep(0,.16,length(radial.xz));
    float wisps=lerp(crossStreak,jetStreak,.24+.72*poleTaper);
    float3 jetPsi=float3(
     Noise3(jetShear*1.95+seed*.25+float3(12,2,8)),
     Noise3(jetShear*1.95+seed*.25+float3(31,7,19)),
     Noise3(jetShear*1.95+seed*.25+float3(5,23,41)))-.5;
    float3 pJetWave=normalize(jetShear+cross(jetShear,jetPsi)*(.30+.25*shearAge));
    float yJetStretch=lerp(2.1,7.8,smoothstep(.04,.42,length(radial.xz)));
    float3 qJetBand=float3(pJetWave.x*1.50,(pJetWave.y+.11*jetPsi.y)*yJetStretch,pJetWave.z*1.50);
    float jetH=Fbm(qJetBand+seed*.25+float3(12,2,8))*.56+wisps*.28+fineGrain*.16;
    o.jetWisps=wisps;
    o.jetBanner=smoothstep(.44,.80,jetH)*(.36+.64*smoothstep(.20,.78,wisps*.64+fineGrain*.36));
    o.jetMacro=Fbm(upper*2.4+seed*.25+float3(12,2,8));
    return o;
   }
   struct FrontState
   {
    float frontalLow,frontalMid,frontalDry,postFrontCells,wcbCirrus;
   };
   // Hemispherically coherent storm-track zonal steering speed (constant in lon & lat inside each hemisphere!):
   // Prevents simDays * localWind(x,y) phase scrambling over multi-year runs while staying strictly zero when wind == 0,
   // reversing on retrograde worlds, and marching fronts smoothly across ocean basins with ZERO 3.2-day era resets!
   float SampleHemisphereStormSteering(float hemiSign,float polarLatRad)
   {
    float vTrack=saturate(.5+hemiSign*clamp(polarLatRad*.82,.45,1.25)*.318309886);
    float sumU=0;
    [unroll] for(int s=0;s<4;s++)
    {
     float2 uvS=float2(.125+s*.25,vTrack);
     float4 wS=tex2Dlod(_ClimateWindTex,float4(uvS,0,0));
     float sU=lerp(wS.r,wS.b,_MonthBlend);
     float uU=sU;
     if(_GlebaCloudEnable>.5)
     {
      #ifdef UNITY_SAMPLE_TEX2D_SAMPLER_LOD
      float4 uwS=UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_UpperWindTex,_ClimateTex,uvS,0);
      #else
      float4 uwS=UNITY_SAMPLE_TEX2D_SAMPLER(_UpperWindTex,_ClimateTex,uvS);
      #endif
      uU=NormalizeUpperFlow(lerp(uwS.r,uwS.b,_MonthBlend),0.0).x;
     }
     sumU+=lerp(sU,uU,.26);
    }
    return sumU*.25;
   }
   // Continuous wind-steered baroclinic Rossby wave & frontal cyclone system (ZERO 3.2-day era template resets!):
   // Fronts travel smoothly across entire ocean basins at actual physical hemispheric steering wind speed, curl/occlude,
   // intensify in baroclinic moist lift, and clear behind the cold-front dry intrusion slot!
   FrontState EvaluateContinuousFronts(
    float simDays,float3 radial,float longitude,float latitude,
    float hemiSteerU,float windSpd,float polarLatRad,float rossbyK,
    float planetary,float synCluster,float synoptic,float flowBands,float mesoCells,float fineGrain)
   {
    FrontState f;
    float hemiSign=latitude>=0?1.0:-1.0;
    float3 hSeed=latitude>=0?float3(3.7,11.2,7.4):float3(23.4,52.5,21.3);
    float k1=max(2.0,floor(rossbyK+.35+.5));
    float k2=k1+1.0;

    // Time-invariant baroclinic Rossby wave phase speed (~7.2 m/s, matching Earth storm-track phase speed):
    // strictly 0 when hemisphere wind is calm, reverses cleanly on retrograde planets, and stays free of
    // multi-year seasonal phase-rate drift (simDays * dU/dt) or spatial phase grating.
    float basePolarRad=(_GlebaCloudEnable>.5&&_GlebaCirculationParams.y>.2)?_GlebaCirculationParams.y:1.0821;
    float cosTrack=max(.35,cos(clamp(basePolarRad*.82,.45,1.25)));
    float hemiDir=sign(hemiSteerU)*smoothstep(.02,.14,abs(hemiSteerU));
    float advRate=clamp(hemiDir*.088/cosTrack,-.26,.26);
    float waveSpeedOn=abs(hemiDir);
    float phase1=fmod(simDays*advRate*k1,6.2831853);
    float phase2=fmod(simDays*(advRate*.74*k2+.045*hemiSign*waveSpeedOn),6.2831853);
    float packShift=fmod(simDays*(advRate*.58),6.2831853);

    float lon1=longitude-phase1/max(1.0,k1);
    float packetWave=.56*cos(longitude-packShift+1.1*hemiSign)+.44*cos(2.0*(longitude-packShift*.85)+phase2*.35+2.3);
    float packetEnv=smoothstep(-.48,.42,packetWave+(planetary*.65+synCluster*.35-.5)*1.05);

    float rossbyMeander=.125*sin(k1*lon1+hSeed.y)+.065*cos(k2*longitude-phase2+hSeed.z)+.05*(synCluster-.5);
    float stormLatRad=clamp(polarLatRad*.85+rossbyMeander,.54,1.32);
    float theta=k1*longitude-phase1+.58*sin(k2*longitude-phase2+1.7)+.30*sin(longitude-packShift+hSeed.x);
    float waveS=sin(theta),waveC=cos(theta);
    float waveFade=smoothstep(-.88,-.06,waveC)*(.44+.56*packetEnv);
    float dx=atan2(waveS,waveC)/max(1.4,k1+.14)*max(.24,cos(latitude));
    float dy=(abs(latitude)-stormLatRad);

    // Spiraling comma cyclone head & baroclinic dry intrusion slot
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

    // Fine frontal filament texture advected smoothly with surface wind
    float frontFilament=flowBands*.60+fineGrain*.40;
    float dxFront=-.01+1.00*(dy-.04)-.47*(dy-.04)*abs(dy-.04);
    float dFront=dx-dxFront+.036*(synoptic-.5)+.021*(frontFilament-.5);
    float frontWidth=dFront<0?.058:.092;
    float coldFront=exp(-pow(dFront/frontWidth,2.0))*smoothstep(-.62,-.18,dy)*(1.0-smoothstep(.16,.36,dy))*(.74+.42*frontFilament);
    float coldFrontApron=exp(-pow((dFront-.054)/.148,2.0))*smoothstep(-.58,-.12,dy)*(1.0-smoothstep(.18,.38,dy))*.64*(.52+.48*frontFilament);

    float dWarm=(dy-(.072-.53*max(0.0,dx+.02)))+.029*(synoptic-.5);
    float warmFront=exp(-pow(dWarm/.086,2.0))*smoothstep(-.10,.04,dx)*(1.0-smoothstep(.28,.52,dx))*(.68+.44*frontFilament);

    float wcb=smoothstep(-.02,.11,dFront)*(1.0-smoothstep(.12,.40,dFront))*smoothstep(-.36,.22,dy)*(1.0-smoothstep(.24,.46,dy));
    float drySlot=exp(-pow((dFront+.086)/.076,2.0))*smoothstep(-.50,-.04,dy)*(1.0-smoothstep(.16,.38,dy));

    float coldWake=smoothstep(.04,.17,-dFront)*(1.0-smoothstep(.40,.78,-dFront))*smoothstep(-.50,.25,dy);
    float wakeLoop=saturate(coldWake+spiralDryTongue*.84);
    float openCellPatch=smoothstep(.22,.64,flowBands*.58+mesoCells*.42);
    float openCellMeso=smoothstep(.34,.72,mesoCells*.60+fineGrain*.40);
    float postCells=wakeLoop*(.46+.54*openCellMeso)*(.50+.50*openCellPatch)*.90;

    f.frontalLow=waveFade*saturate(max(max(commaHead*(.92+.16*frontFilament),max(coldFront,coldFrontApron)),warmFront*.94)+.56*wcb*(.55+.45*frontFilament));
    f.postFrontCells=waveFade*postCells;
    f.frontalMid=waveFade*saturate(warmFront+.82*wcb+commaHead*.68+coldFrontApron*.54);
    f.frontalDry=waveFade*saturate(max(drySlot,max(spiralDryTongue*.96,vortexEyeClear)));
    f.wcbCirrus=waveFade*saturate(wcb*(.76+.34*frontFilament)+commaHead*.92+warmFront*.78+coldFront*.72+coldFrontApron*.60);
    return f;
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
    float poleDamp=smoothstep(0,.10,length(radial.xz));

    float4 forcing=float4(.5,0,0,wet);
    float4 aux=float4(0,0,0,.55);
    float3 jetAxis=normalize(float3(.84,.25,.48));
    float3 jetVec=0;
    float2 uFlow=0;
    if(_GlebaCloudEnable>.5)
    {
     forcing=UNITY_SAMPLE_TEX2D_SAMPLER(_CloudSynopticTex,_ClimateTex,i.uv);
     forcing=lerp(lerp(_GlebaForcingPoleSouth,_GlebaForcingPoleNorth,step(.5,i.uv.y)),forcing,poleBlend);
     aux=UNITY_SAMPLE_TEX2D_SAMPLER(_CloudSynopticAuxTex,_ClimateTex,i.uv);
     float4 uWind=UNITY_SAMPLE_TEX2D_SAMPLER(_UpperWindTex,_ClimateTex,i.uv);
     uFlow=NormalizeUpperFlow(lerp(uWind.rg,uWind.ba,_MonthBlend),forcing.g);
     jetVec=east*uFlow.x+north*uFlow.y;
     float jetLen=length(jetVec);
     if(jetLen>1e-3&&length(radial.xz)>.08)jetAxis=jetVec/jetLen;
    }
    float3 upperWindVec=lerp(windVec,jetVec,.55);
    float steerZonal=(_GlebaCloudEnable>.5?lerp(flow.x,uFlow.x,.25):flow.x)*poleDamp;
    // Decode un-truncated local polar-front latitude (30..88 deg) and synchronize with planetary circulation terminus
    float auxPolarRad=(30.0+58.0*aux.a)*.01745329;
    float polarLatRad=_GlebaCloudEnable>.5&&_GlebaCirculationParams.y>.2
        ? clamp(_GlebaCirculationParams.y+(auxPolarRad-_GlebaCirculationParams.y)*.45,.52,1.52)
        : auxPolarRad;
    float rossbyK=_GlebaCloudEnable>.5&&_GlebaCirculationParams.z>.5?_GlebaCirculationParams.z:2.6;
    float hadleyRad=_GlebaCloudEnable>.5&&_GlebaCirculationParams.x>.1?_GlebaCirculationParams.x:.5236;

    // Centered half-cycle dual-layer flow-map advection (C1-smooth sin^2/cos^2 weights, zero random time-wander!):
    // keeps |ageDays| <= 0.80d at 50/50 blend (1.60d only at weight==0), eliminating ITCZ convergence Jacobian folds
    // while large-scale fronts & weather lifecycle evolve continuously from simDays without era resets.
    float simDays=_VisualTime*.04;
    const float parcelPeriodDays=3.2;
    float cyc0=simDays/parcelPeriodDays;
    float cyc1=cyc0+.5;
    float era0=floor(cyc0),p0=frac(cyc0);
    float era1=floor(cyc1)+73.0,p1=frac(cyc1);
    float w0=.5-.5*cos(6.283185307*p0);
    float blend=1.0-w0;
    EraMorphology m0=(EraMorphology)0,m1=(EraMorphology)0;
    [loop] for(int ei=0;ei<2;ei++)
    {
     float eId=(ei==0)?era0:era1;
     float ageDays=((ei==0?p0:p1)-.5)*parcelPeriodDays;
     EraMorphology me=SampleEraMorphology(eId,ageDays,radial,latitude,windVec,windAxis,windSpd,poleDamp,polarLatRad,jetAxis,upperWindVec,forcing.g);
     if(ei==0)m0=me;else m1=me;
    }

    float invStd=rsqrt(max(.5,blend*blend+(1.0-blend)*(1.0-blend)));
    float planetary=saturate(.5+(lerp(m0.planetary,m1.planetary,blend)-.5)*lerp(1.0,invStd,.45));
    float synCluster=saturate(.5+(lerp(m0.synCluster,m1.synCluster,blend)-.5)*lerp(1.0,invStd,.45));
    float baseTex=saturate(.5+(lerp(m0.baseTex,m1.baseTex,blend)-.5)*lerp(1.0,invStd,.45));
    float flowBands=saturate(.5+(lerp(m0.flowBands,m1.flowBands,blend)-.5)*lerp(1.0,invStd,.50));
    float mesoCells=saturate(.5+(lerp(m0.mesoCells,m1.mesoCells,blend)-.5)*lerp(1.0,invStd,.45));
    float fineGrain=lerp(m0.fineGrain,m1.fineGrain,blend);
    float curvedBand=lerp(m0.curvedBand,m1.curvedBand,blend);
    float curvedWisp=lerp(m0.curvedWisp,m1.curvedWisp,blend);
    float tradeStreets=lerp(m0.tradeStreets,m1.tradeStreets,blend);
    float honeycomb=lerp(m0.honeycomb,m1.honeycomb,blend);
    float mcsCluster=lerp(m0.mcsCluster,m1.mcsCluster,blend);
    float anvilShield=lerp(m0.anvilShield,m1.anvilShield,blend);
    float polarCarve=lerp(m0.polarCarve,m1.polarCarve,blend);
    float jetWisps=lerp(m0.jetWisps,m1.jetWisps,blend);
    float jetBanner=lerp(m0.jetBanner,m1.jetBanner,blend);
    float jetMacro=lerp(m0.jetMacro,m1.jetMacro,blend);

    float synopticNow=saturate((baseTex-.32*planetary)/.68);
    float basePolarRad=(_GlebaCloudEnable>.5&&_GlebaCirculationParams.y>.2)?_GlebaCirculationParams.y:1.0821;
    float hemiSteerU=SampleHemisphereStormSteering(latitude>=0?1.0:-1.0,basePolarRad);
    FrontState fr=EvaluateContinuousFronts(simDays,radial,longitude,latitude,hemiSteerU,windSpd,polarLatRad,rossbyK,planetary,synCluster,synopticNow,flowBands,mesoCells,fineGrain);
    float frontalLow=fr.frontalLow;
    float postFrontCells=fr.postFrontCells;
    float frontalMid=fr.frontalMid;
    float frontalDry=fr.frontalDry;
    float wcbCirrus=fr.wcbCirrus;

    float4 regional=tex2D(_RegionalCloudResponse,i.uv);
    float2 pole=lerp(_RegionalCloudPoles.xy,_RegionalCloudPoles.zw,step(.5,i.uv.y));
    float regPoleW=smoothstep(0,.5,min(i.uv.y,1-i.uv.y)*_RegionalCloudResponse_TexelSize.w);
    float2 response=lerp(pole,float2(regional.r-regional.g,regional.b),regPoleW);
    // Carbon contract: RGB = existing regional response, A = [0,1] evaporation/subsidence dissipation (0 = no signal).
    // Guarded by _RegionalEvapReady until Core stops writing legacy alpha=255 / Texture2D.blackTexture (a=1).
    float regEvap=saturate(regional.a)*step(.5,_RegionalEvapReady)*regPoleW;

    float low,high,convection,storm;
    if(_GlebaCloudEnable>.5)
    {
     float synLift=forcing.r,itczConv=forcing.g,stormTrack=forcing.b,moistEnv=forcing.a;
     float marineSc=aux.r,orogCrest=aux.g,leeShadow=aux.b;
     // Lagrangian parcel history along real surface wind (-6h & -18h upstream) and unified upper jet (-8.4h upstream):
     // drives bounded moist-ascent condensation, dry-air advection/subsidence evaporation, post-rain boundary-layer
     // depletion, and wind-advected synoptic mesoscale dry breaks in persistent monsoon/ITCZ belts.
     float3 pUp6=AdvectSurfaceRK2(radial,windVec,.25,poleDamp);
     float2 uvUp6=FastSphereUV(pUp6);
     float4 fUp6=UNITY_SAMPLE_TEX2D_SAMPLER(_CloudSynopticTex,_ClimateTex,uvUp6);
     float4 aUp6=UNITY_SAMPLE_TEX2D_SAMPLER(_CloudSynopticAuxTex,_ClimateTex,uvUp6);
     float3 pUp18=AdvectSurfaceRK2(radial,windVec,.75,poleDamp);
     float2 uvUp18=FastSphereUV(pUp18);
     float4 fUp18=UNITY_SAMPLE_TEX2D_SAMPLER(_CloudSynopticTex,_ClimateTex,uvUp18);
     float3 pUpHi=AdvectUpperRK2(radial,upperWindVec,.35,poleDamp,itczConv);
     float2 uvUpHi=FastSphereUV(pUpHi);
     float4 fUpHi=UNITY_SAMPLE_TEX2D_SAMPLER(_CloudSynopticTex,_ClimateTex,uvUpHi);

     float parcelLiftGain=(synLift-fUp6.r)+.45*(orogCrest-aUp6.g)+.35*(synLift-fUp18.r);
     float parcelMoist=saturate(moistEnv*.74+wet*.26);
     // Headroom factor prevents condensation/carryover from pinning already-high forcing (e.g. East China Sea 0.88) at 1.00.
     float liftRoom=saturate(1.0-synLift*.82);
     float parcelCondense=smoothstep(0.0,.20,parcelLiftGain)*smoothstep(.18,.70,parcelMoist)*.17*(.35+.65*liftRoom);
     float parcelCarry=smoothstep(.30,.76,fUp6.r)*(1.0-leeShadow)*.065*smoothstep(.05,.35,windSpd)*liftRoom;
     float dryAdvect=smoothstep(.03,.26,moistEnv-fUp6.a)*smoothstep(.05,.35,windSpd)*.14;
     float parcelSubside=smoothstep(0.0,.20,-parcelLiftGain+.55*max(0.0,leeShadow-aUp6.b))*.18+dryAdvect;
     // Wind-advected synoptic mesoscale dry/subsidence wave (moves strictly with real _ClimateWindTex):
     // breaks permanent solid blankets in high-synLift monsoon/ITCZ zones with realistic thinning/clearing windows.
     float synWaveDry=1.0-smoothstep(.24,.66,synCluster*.58+planetary*.42);
     float upHeavyRain=smoothstep(.28,.72,fUp6.r*fUp6.a)*smoothstep(.22,.68,max(fUp6.g,fUp6.b));
     float convWakeBreak=smoothstep(.35,.75,itczConv)*synWaveDry;
     float postRainDecay=saturate(
      upHeavyRain*lerp(.18,.08,smoothstep(.55,.86,synLift*max(itczConv,stormTrack)))+
      upHeavyRain*convWakeBreak*.26+
      smoothstep(.35,.78,fUp18.r*fUp18.a*max(fUp18.g,fUp18.b))*(1.0-synLift*.70)*.10);
     float upperAnvilAdv=smoothstep(.24,.70,max(fUpHi.g,fUpHi.b*.85))*smoothstep(.22,.72,fUpHi.a);

     float hadleySin0=sin(clamp(hadleyRad*.38,.12,.72));
     float hadleySin1=sin(clamp(hadleyRad*.92,.28,.96));
     float tropBelt=1.0-smoothstep(hadleySin0,hadleySin1,abs(radial.y));
     float effWet=wet*(1.0-.72*leeShadow);

     // Protect ONLY true cold eastern-boundary upwelling marine Sc decks (Peru/Chile, California/Baja, Namibia: marineSc > 0.38!)
     // Never boost open warm subtropical oceans (marineSc ~0.15-0.30), keeping vast natural clear subtropical gyres with ZERO zonal banding!
     float scProtect=smoothstep(.38,.74,marineSc);
     // Keep the cold-core upwelling gradient above the deck's onset threshold.
     float scRamp=smoothstep(.38,.98,marineSc);
     float scStrength=scRamp*(.34+.66*smoothstep(.64,.98,marineSc));
     float lowLee=leeShadow*(1.0-.82*scProtect);

     // Unified physical dissipation / evaporation survival gates (applied BEFORE thresholding AND to ALL max() branches
     // so dry air, subsidence, post-rain depletion, or Regional.a evaporation can NEVER be refilled into solid overcast!).
     // When _RegionalEvapReady>0.5, CPU GlebaGeographySupplement already applies effEvap/blanketDryShutoff to synLift/leeShadow
     // and publishes the same budEvap into both Regional.g (-Cover) and Regional.a (Evaporation): de-overlap with max(),
     // disable redundant synthetic GPU monsoonBreak, and match CPU scShield (1 - 0.82*scProtect) for cold-upwelling Sc decks.
     float hasCpuEvap=step(.5,_RegionalEvapReady);
     float regSuppress=max(0.0,-response.x);
     float regBoost=max(0.0,response.x);
     float regDry=max(regEvap,regSuppress*.85)*(1.0-.82*scProtect);
     float moistSlack=saturate(1.0-smoothstep(.32,.82,moistEnv)*.55);
     float monsoonBreak=smoothstep(.56,.84,synLift)*synWaveDry*moistSlack*(1.0-.85*scProtect)*(1.0-hasCpuEvap);
     float dryDispersal=saturate(regDry*lerp(.65,.46,hasCpuEvap)+parcelSubside*lerp(.85,.45,hasCpuEvap)+
                                 postRainDecay*lerp(.95,.25,hasCpuEvap)*(1.0-.75*scProtect)+monsoonBreak*.52);
     float highDispersal=saturate(regDry*lerp(.55,.42,hasCpuEvap)+parcelSubside*lerp(.55,.35,hasCpuEvap)+
                                  postRainDecay*lerp(.55,.18,hasCpuEvap)+monsoonBreak*.54);
     float lowSurvive=saturate(1.0-dryDispersal);
     float highSurvive=saturate(1.0-highDispersal);

     // ISCCP joint optical-depth partition matching 79e6fa2 natural clear/veil/core balance + Lagrangian parcel lifecycle:
     float highShare=saturate(.58*itczConv+.52*stormTrack+.26*moistEnv)*(1.0-.78*marineSc)*(1.0-.66*leeShadow);
     float climBaseLift=synLift+.18*regBoost*(1.0-.50*lowLee)+parcelCondense+parcelCarry;
     float climTarget=saturate(climBaseLift*(1.0-lerp(.62,.36,hasCpuEvap)*dryDispersal)-.14*regSuppress*(1.0-hasCpuEvap));
     float lowTarget=saturate(climTarget*(1.0-.35*highShare));
     float highRawTarget=(climTarget-lowTarget)/max(.14,1.0-lowTarget)+.22*highShare*climTarget+.12*upperAnvilAdv*(1.0-.65*leeShadow);
     float highTarget=saturate(highRawTarget*(1.0-lerp(.60,.36,hasCpuEvap)*highDispersal));

     // Five authentic NASA DSCOVR EPIC & VIIRS continuous morphological regimes:
     // Very gentle synCluster preserves 79e6fa2 unbroken sweeping band continuity without carving bald gaps!
     float clusterBias=(synCluster-.495)*.18;
     float zBase=(baseTex-.496)*5.4+clusterBias;
     float zRibbon=(baseTex-.498)*3.4+clusterBias*.45+3.80*(curvedBand-.275)+1.08*(curvedWisp-.235);
     float zStreets=(tradeStreets-.496)*5.6+clusterBias*.35+2.25*(curvedBand-.26);
     float zHoney=(honeycomb-.54)*4.6+lerp(-1.25,.58,scStrength)+.22*(curvedBand-.30)*(1.0-.75*scProtect);
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
     // Gate continuous frontal storm track by local baroclinic westerly steering so weak/monsoonal NH summer
     // mid-latitudes track actual local _ClimateWindTex instead of a forced eastward conveyor!
     float localWestAlign=smoothstep(-.06,.36,steerZonal*sign(hemiSteerU+1e-5));
     float baroSteerGate=lerp(.22,1.0,localWestAlign)*smoothstep(.04,.30,windMag);
     float stormWeight=saturate(smoothstep(.08,.48,stormTrack+.18*midLatBoost)*(1.0-.75*leeShadow)*baroSteerGate);

     float zLow=lerp(zBase,zRibbon,.74);
     zLow=lerp(zLow,zStreets,.38*tropBelt);
     zLow=lerp(zLow,zMcs,saturate(tropBelt*(.30+.70*itczConv)));
     zLow=lerp(zLow,zHoney,saturate(scProtect*.98));
     zLow=lerp(zLow,zFront,stormWeight);
     float orogMoist=smoothstep(.16,.48,moistEnv);
     zLow+=.82*orogCrest*(.30+.70*smoothstep(.22,.78,baseTex))*orogMoist*lowSurvive;
     float uLow=rcp(1.0+exp(-1.19*clamp(zLow,-5.5,5.5)));

     // EPIC Continuous Three-Tier Atmospheric Vapor Distribution restoring 79e6fa2 full-disk balance:
     //   1. broadVeil: airy pearly translucent mist & soft thin-cloud gradients bridging sweeping cloud bands
     //   2. coherentBody: unbroken curved fronts, ITCZ/SPCZ ribbons & eastern-boundary marine Sc decks
     //   3. brightCore: snow-white convective knots, comma heads & windward orographic cap clouds
     float sharpOrg=saturate(max(stormWeight*frontalLow*1.06,itczConv*mcsCluster));
     float dryGuard=smoothstep(.10,.35,climTarget+.16*scStrength)*(1.0-.84*lowLee)*lowSurvive;
     float polarDryCarve=saturate(stormWeight*frontalDry*smoothstep(midSin0,midSin1,abs(radial.y))+polarCarve*.38);
     float blanketRelief=smoothstep(.60,.88,lowTarget)*synWaveDry*(1.0-.45*sharpOrg);
     float scCapWeight=scProtect*(1.0-stormWeight)*(1.0-saturate(itczConv*1.5))*(1.0-saturate(orogCrest*1.5));
     float baseLowTarget=lerp(lowTarget,lerp(.12,.40,scStrength),scCapWeight);
     float scTargetTerm=.12*scStrength*smoothstep(.24,.70,honeycomb)*saturate(1.0-regDry*.55);
     float effLowTarget=saturate(min(.86,baseLowTarget*(1.0-.58*polarDryCarve-lerp(.24,.08,hasCpuEvap)*blanketRelief)+scTargetTerm+
                           .18*orogCrest*orogMoist*(1.0-.70*leeShadow)*lowSurvive)+.045*smoothstep(.24,.60,climTarget)*(1.0-lowLee)*lowSurvive);
     float veilTarget=saturate(min(.86,effLowTarget*1.28+.102*dryGuard)*(1.0-.62*polarDryCarve));
     float wVeil=max(1e-4,min(.44,min(veilTarget,1.0-veilTarget)+.09));
     float rawVeil=smoothstep(1.0-veilTarget-wVeil,1.0-veilTarget+wVeil,uLow);
     float wCore=max(1e-4,min(.24,min(effLowTarget,1.0-effLowTarget)));
     float rawCore=smoothstep(1.0-effLowTarget-wCore,1.0-effLowTarget+wCore,uLow);

     // Wind-combed silky texture + organic eastern-boundary marine Sc decks + sweeping curved fronts
     // + post-frontal open-cell wakes + sinuous Pacific ITCZ / SPCZ + moisture-gated orographic windward cap clouds!
     // Every direct max() branch is gated by lowSurvive so dissipation/evaporation genuinely clears clouds and shadows!
     float silkTex=flowBands*.54+fineGrain*.30+mesoCells*.16;
     float veilLayer=rawVeil*lerp(.31,.56,smoothstep(.16,.84,silkTex))*dryGuard;
     float bodyLayer=rawCore*(.64+.36*smoothstep(.18,.82,silkTex))*lerp(.35,1.0,lowSurvive);
     float denseCore=smoothstep(.44,.82,rawCore)*(sharpOrg*.88+.12)*(.92+.08*smoothstep(.22,.78,mesoCells))*lowSurvive;
     float parcelLifecycle=saturate((1.0+parcelCondense*1.25)*lowSurvive);
     float frontDirect=stormWeight*smoothstep(.24,.72,frontalLow)*(1.0-.85*frontalDry)*smoothstep(.10,.38,climTarget+.12*midLatBoost)*.84*parcelLifecycle;
     float postWakeLace=stormWeight*postFrontCells*smoothstep(.08,.40,climTarget+.12*midLatBoost)*.82*lowSurvive;
     // Localized eastern-boundary marine stratocumulus sheet (closed-cell cobbles nearshore -> open-cell POCs seaward!)
     float scEvapKeep=saturate(1.0-regDry*.50-parcelSubside*.30);
     float scDeckDirect=scProtect*lerp(.16,.68,scStrength)*smoothstep(.24,.70,honeycomb)*
                        smoothstep(.18,.52,moistEnv+.28*scStrength)*(1.0-.72*leeShadow*(1.0-.55*scStrength))*scEvapKeep;
     float oceanTradeGate=smoothstep(.16,.42,climTarget)*smoothstep(.28,.58,moistEnv)*(1.0-.88*lowLee)*lowSurvive;
     float tradePatchEnv=smoothstep(.34,.67,planetary*.48+curvedBand*.52);
     float tradeFineField=smoothstep(.40,.68,tradeStreets*.64+honeycomb*.36);
     float marineTapestry=oceanTradeGate*tradePatchEnv*tradeFineField*lerp(.40,.64,saturate(marineSc+.38*tropBelt))*(1.0-.85*scCapWeight);
     float itczMoistScale=lerp(.52,1.0,smoothstep(.20,.68,moistEnv));
     float itczDirectLift=smoothstep(.12,.50,itczConv)*smoothstep(.36,.80,mcsCluster)*itczMoistScale*(1.0-.82*leeShadow)*.82*parcelLifecycle;
     float orogDirectCap=orogCrest*smoothstep(.16,.52,moistEnv)*(1.0-.78*leeShadow)*(.44+.56*smoothstep(.20,.78,flowBands*.55+mesoCells*.45))*.76*lowSurvive;
     float lowRaw=saturate(max(max(veilLayer,bodyLayer),max(max(max(denseCore,frontDirect),max(postWakeLace,orogDirectCap)),max(max(marineTapestry,scDeckDirect),itczDirectLift)))+
                  .04*regBoost*smoothstep(.02,.18,rawVeil+.15*climTarget));
     float finalEvapCutLow=hasCpuEvap>.5
      ?.44*smoothstep(.28,1.0,regDry)
      :smoothstep(.04,1.0,regEvap+regSuppress*.48+postRainDecay*.42*(1.0-.75*scProtect)+monsoonBreak*.32);
     low=lowRaw*(1.0-finalEvapCutLow);

     // Explicit 5-tier cloud altitude classification in convection (weather.b):
     // Route BOTH eastern-boundary marineSc AND post-frontal cold-air open cells cleanly into low cellular regime!
     float postOpenShare=saturate(postWakeLace*1.15*(1.0-smoothstep(.30,.65,frontalLow)));
     float scShare=saturate(marineSc*.92-max(stormWeight*frontalLow,itczConv*mcsCluster)*.92);
     float midShare=saturate(max(stormWeight*frontalMid*(1.0-.55*smoothstep(.65,.95,frontalLow)),
                                 (.35*itczConv+.45*orogCrest)*(1.0-smoothstep(.55,.88,mcsCluster)))*(1.0-scShare)*(1.0-postOpenShare));
     float corePeak=smoothstep(.46,.86,planetary*.45+mesoCells*.55);
     float deepShare=saturate(max(itczConv*smoothstep(.45,.88,uLow)*(.45+.55*mcsCluster),
                                  stormWeight*smoothstep(.50,.90,frontalLow)*.88))*lowSurvive;
     float deepTrigger=smoothstep(.20,.68,deepShare*(.52+.48*corePeak)+.32*synLift*orogCrest+.20*itczConv*smoothstep(.55,.90,effWet))*lowSurvive;
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

     // Upper-tropospheric Rossby jet streaks & downwind anvil/WCB plumes steered strictly by unified upper wind!
     float wisps=jetWisps;
     float curvedCirrus=saturate(max(jetBanner*1.04,curvedWisp*.42));

     float zJet=(jetMacro-.49)*3.0+(wisps-.48)*3.2+3.90*(curvedCirrus-.24)+.42*(uLow-.50);
     float zAnvil=2.90*(anvilShield-.18)+(wisps-.48)*2.9+2.65*(curvedCirrus-.24)+.40*(uLow-.45);
     float zWcb=.36*zJet+3.55*(wcbCirrus-.14)+2.35*(curvedCirrus-.24)+(wisps-.48)*2.1-1.05*polarCarve;
     float zHigh=lerp(zJet,zAnvil,saturate(itczConv*1.15+upperAnvilAdv*.65));
     zHigh=lerp(zHigh,zWcb,stormWeight);
     float uHigh=rcp(1.0+exp(-1.16*clamp(zHigh,-5.5,5.5)));
     float highEnvTarget=saturate(highTarget*1.20+.045*dryGuard)*(1.0-.48*polarDryCarve);
     float wHigh=max(1e-4,min(.42,min(highEnvTarget,1.0-highEnvTarget)+.06));
     float rawHigh=smoothstep(1.0-highEnvTarget-wHigh,1.0-highEnvTarget+wHigh,uHigh);
     float highSilk=.34+.66*smoothstep(.18,.84,wisps*.64+fineGrain*.36);
     float anvilOutflow=saturate(max(smoothstep(.16,.56,itczConv)*anvilShield*.70*itczMoistScale,upperAnvilAdv*anvilShield*.74))*highSurvive;
     float finalEvapCutHigh=hasCpuEvap>.5
      ?.42*smoothstep(.28,1.0,regDry)
      :smoothstep(.04,1.0,regEvap+regSuppress*.44+monsoonBreak*.36);
     high=smoothstep(.05,.92,saturate(max(rawHigh*highSilk*1.13*lerp(.35,1.0,highSurvive),anvilOutflow)))*
          (1.0-.78*tropBelt*leeShadow)*(1.0-finalEvapCutHigh);
    }
    else
    {
     float threshold=lerp(.57,.35,wet)-.095*response.x+.30*regEvap;
     low=smoothstep(threshold-.06,threshold+.24,baseTex)*(1.0-.92*regEvap);
     storm=saturate(smoothstep(.27,.78,wet)*smoothstep(.62,.96,low)*(1+.35*response.y));
     convection=saturate(.16+wet*.32+storm*.52);
     float3 upper=RotateY(radial,-_VisualTime*.0018);
     float envelope=smoothstep(.33,.67,Fbm(upper*4.3+float3(12,2,8)));
     float3 streakAxis=normalize(float3(.84,.25,.48));
     float3 crossAxis=normalize(float3(-.48,.12,.86));
     float3 ribbonPos=upper;
     float ribbonA=Noise3((ribbonPos-streakAxis*dot(ribbonPos,streakAxis)*.94)*35+float3(9,4,2));
     float ribbonB=Noise3((ribbonPos-crossAxis*dot(ribbonPos,crossAxis)*.92)*42+float3(3,2,7));
     float align=.5+.5*(abs(dot(windVec,streakAxis))-abs(dot(windVec,crossAxis)))*smoothstep(0,.16,length(radial.xz));
     float wisps=lerp(ribbonB,ribbonA,smoothstep(.30,.70,align));
     high=envelope*(.14+.70*smoothstep(.31,.78,wisps))*lerp(.65,1,wet)*(1.0-.90*regEvap);
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
