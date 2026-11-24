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
    float planetary,baseTex,flowBands,mesoCells,fineGrain,tradeStreets,honeycomb,mcsCluster,anvilShield,frontalLow,frontalMid,frontalDry,postFrontCells,wcbCirrus;
   };
   EraMorphology SampleEraMorphology(
    float eraId,float age,float3 radial,float longitude,float latitude,
    float3 east,float3 north,float3 velocity,float3 windAxis,float windSpd,float polarLatRad,float rossbyK,float3 jetAxis,float3 wander)
   {
    EraMorphology o;
    float3 p=normalize(radial-velocity*age*7.2);
    float3 seed=float3(Hash3(float3(eraId,4,7)),Hash3(float3(eraId,11,3)),Hash3(float3(eraId,5,19)))*15.0;

    // Gentle divergence-free spherical curl-streamfunction warp (cross(p, gradPsi)):
    // Gives organic synoptic curvature without folding contours into wood-grain loops!
    float3 psi1=float3(
     Fbm(p*2.2+wander*.55+seed+17.0),
     Fbm(p*2.2+wander*.55+seed+43.0),
     Fbm(p*2.2+wander*.55+seed+71.0))-.5;
    float3 curl1=cross(p,psi1);
    float3 p1=normalize(p+curl1*.090);

    float3 psi2=float3(
     Noise3(Turn(p1)*6.2+seed+11.0),
     Noise3(Turn(p1)*6.2+seed+29.0),
     Noise3(Turn(p1)*6.2+seed+53.0))-.5;
    float3 curl2=cross(p1,psi2);
    float3 pFlow=normalize(p1+curl2*.038);

    // 1. Multi-scale streamline-combed flow fields (anisotropically sheared at BOTH synoptic & meso scales
    // so weather systems sweep into curved ribbons & diagonal bands instead of round blobs — NASA EPIC signature!)
    float3 flowDir=normalize(lerp(windAxis,jetAxis,.35)+float3(1e-5,0,0));
    float synAniso=lerp(.44,.68,windSpd);
    float3 pSynStreak=pFlow-flowDir*dot(pFlow,flowDir)*synAniso;
    float streakAniso=lerp(.68,.88,windSpd);
    float3 pStreak=pFlow-windAxis*dot(pFlow,windAxis)*streakAniso;
    float3 pJetStreak=pFlow-jetAxis*dot(pFlow,jetAxis)*.88;
    float planetary=Fbm(p1*2.05+wander*.50+seed*.7+float3(3.1,7.4,1.9));
    float synoptic=Fbm(Turn(pSynStreak)*4.9+wander*.80+seed);
    float flowBands=Fbm(Turn(pStreak)*17.5+wander+seed*1.4);
    float mesoCells=Fbm(Turn(pFlow)*38.0+seed*1.9);
    float fineGrain=Fbm(Turn(pJetStreak)*68.0+seed*2.7);

    // 2. Regime A: Parallel wind-aligned trade-wind cloud streets & fine marine Sc Rayleigh-Benard cellular lace
    // (seen off Baja, Peru, Namibia & across central subtropical oceans in NASA EPIC & Blue Marble East!)
    float3 crossWind=normalize(cross(radial,windAxis)+float3(1e-5,0,0));
    float3 pStreet=pFlow-windAxis*dot(pFlow,windAxis)*.88;
    float streetRipple=Noise3(Turn(pStreet)*64.0+seed+5.0);
    float streetPuffs=Noise3(Turn(pFlow)*92.0+seed+13.0);
    o.tradeStreets=planetary*.34+synoptic*.22+streetRipple*.26+streetPuffs*.18;

    float cellClosed=1.0-abs(2.0*Noise3(Turn(pFlow)*46.0+seed)-1.0);
    float cellFine=1.0-abs(2.0*Noise3(Turn(pFlow)*88.0+seed+9.0)-1.0);
    float scMacro=planetary*.42+synoptic*.30;
    o.honeycomb=scMacro+pow(cellClosed,1.20)*.17+cellFine*.11;

    o.planetary=planetary;
    o.flowBands=flowBands;
    o.mesoCells=mesoCells;
    o.fineGrain=fineGrain;
    o.baseTex=planetary*.38+synoptic*.34+flowBands*.18+mesoCells*.06+fineGrain*.04;

    // 3. Regime B: Narrow, sinuous ITCZ / SPCZ convective filaments studded with tight bright Cb hot-towers
    // + long downwind brushed-silk cirrus anvil plumes (NASA EPIC Pacific & ISS Borneo signature!)
    float3 pZonalRibbon=pFlow-flowDir*dot(pFlow,flowDir)*.84;
    float itczRibbon=Fbm(Turn(pZonalRibbon)*13.0+wander*.7+seed+float3(5.3,1.7,8.9));
    float hotTowers=smoothstep(.48,.84,mesoCells*.55+Noise3(Turn(pFlow)*78.0+seed+21.0)*.45);
    float mcsEnv=smoothstep(.42,.78,planetary*.34+synoptic*.32+itczRibbon*.34);
    o.mcsCluster=mcsEnv*(.24+.76*hotTowers);
    float3 pAnvilNear=normalize(pFlow-jetAxis*.052);
    float3 pAnvilFar=normalize(pFlow-jetAxis*.135);
    float anvilNear=Fbm(pAnvilNear*5.4+wander*.7+seed);
    float anvilFar=Fbm(pAnvilFar*3.4+wander*.6+seed);
    float anvilSilk=Fbm((pAnvilNear-jetAxis*dot(pAnvilNear,jetAxis)*.88)*24.0+seed+4.0);
    o.anvilShield=smoothstep(.40,.78,anvilNear*.50+anvilFar*.32+anvilSilk*.18)*(.26+.74*mcsEnv);

    // 4. Regime C: Extratropical Sweeping 4,500 km Curved Frontal Arcs + Tightly-Wound 2.2-Turn Spiral Comma Vortices
    // + Peppered Open-Cell Cold-Air Cumulus ("fishnet speckle" behind every cold front in Apollo 17 & EPIC!)
    float hemiSign=latitude>=0?1.0:-1.0;
    float3 hSeed=seed+(latitude>=0?float3(0,0,0):float3(19.7,41.3,13.9));
    float k1=max(1.0,floor(rossbyK-.35+.5));
    float k2=k1+1.0;
    float eraLon1=6.2831853*Hash3(float3(eraId,9.0+hemiSign*2.0,2));
    float eraLon2=6.2831853*Hash3(float3(eraId,15.0+hemiSign*3.0,6));
    float packPhase=6.2831853*Hash3(float3(eraId,23.0+hemiSign*5.0,11));

    float packetWave=.62*cos(longitude-.35*age+packPhase)+.38*cos(2.0*longitude+.22*age+eraLon2);
    float packetEnv=smoothstep(-.34,.46,packetWave+(planetary-.5)*1.10);

    float theta=k1*longitude-.88*age+eraLon1+.68*sin(k2*longitude-1.12*age+eraLon2)+.34*sin(longitude+hSeed.x);
    float waveS=sin(theta),waveC=cos(theta);
    float waveFade=smoothstep(-.80,.02,waveC)*(.30+.70*packetEnv);
    float dx=atan2(waveS,waveC)/max(1.3,k1+.35)*max(.24,cos(latitude));
    float dy=(abs(latitude)-polarLatRad-.105*sin(k1*longitude-.42*age+hSeed.y)-.058*cos(k2*longitude+hSeed.z));

    // Gracefully wound 2.2-turn spiral occluded comma vortex with coiled dark-blue dry-intrusion tongue
    // curling right into the vortex center (matches NASA Apollo 17 & DSCOVR EPIC Southern/North Pacific storms!)
    float2 vortVec=float2(dx+.048+.032*(synoptic-.5),dy-.062+.026*(planetary-.5));
    float rVort=length(vortVec)/lerp(.36,.52,saturate(2.2-k1));
    float vortAng=atan2(vortVec.y,vortVec.x+1e-6)+3.95*log(1.0+6.8*rVort)+.72*(synoptic-.5)+.38*(flowBands-.5);
    float spiralWave=cos(vortAng);
    float spiralPrimary=smoothstep(-.05,.82,spiralWave);
    float spiralSecondary=smoothstep(.24,.90,cos(2.0*vortAng+1.15))*.62;
    float commaCollar=exp(-pow((rVort-.16)/.09,2.0))*.68;
    float commaHead=(1.0-smoothstep(.52,1.26,rVort))*smoothstep(.032,.11,rVort)*max(max(spiralPrimary,spiralSecondary),commaCollar);
    // Coiled dark-blue dry-intrusion spiral channel wrapping 1.5 turns into the cyclone core!
    float spiralDryTongue=smoothstep(.08,.76,-spiralWave)*(1.0-smoothstep(.16,1.08,rVort))*smoothstep(.02,.10,rVort);
    float vortexEyeClear=(1.0-smoothstep(.02,.085,rVort))*.85;

    // Sweeping 4,800 km curved cold-front ribbon with fine fibrous shear streaks along the frontal axis
    float3 frontTangent=normalize(east*.66+north*hemiSign*.75+1e-5);
    float3 pFrontShear=pFlow-frontTangent*dot(pFlow,frontTangent)*.86;
    float frontFilament=Fbm(Turn(pFrontShear)*17.5+hSeed);

    float dxFront=-.02+.88*(dy-.05)-.46*(dy-.05)*abs(dy-.05);
    float dFront=dx-dxFront+.034*(synoptic-.5)+.018*(frontFilament-.5);
    float coldFront=exp(-pow(dFront/.050,2.0))*smoothstep(-.60,-.22,dy)*(1.0-smoothstep(.10,.26,dy))*(.74+.38*frontFilament);

    float dWarm=(dy-(.068-.56*max(0.0,dx+.02)))+.028*(synoptic-.5);
    float warmFront=exp(-pow(dWarm/.072,2.0))*smoothstep(-.08,.05,dx)*(1.0-smoothstep(.24,.46,dx))*(.66+.40*frontFilament);

    float wcb=smoothstep(-.025,.095,dFront)*(1.0-smoothstep(.10,.34,dFront))*smoothstep(-.30,.20,dy)*(1.0-smoothstep(.20,.40,dy));
    float drySlot=exp(-pow((dFront+.078)/.062,2.0))*smoothstep(-.42,-.02,dy)*(1.0-smoothstep(.10,.26,dy));

    // Signature NASA Apollo 17 / EPIC Post-Frontal Open-Cell Cold-Air Cumulus ("lacy fishnet speckle" behind cold front!)
    float coldWake=smoothstep(.08,.22,-dFront)*(1.0-smoothstep(.28,.54,-dFront))*smoothstep(-.44,.14,dy);
    float openCellRim=smoothstep(.42,.78,abs(2.0*Noise3(Turn(pFlow)*84.0+hSeed)-1.0));
    float openCellPatch=smoothstep(.34,.72,Noise3(Turn(pFlow)*32.0+hSeed+17.0));
    float postCells=coldWake*openCellRim*(.35+.65*openCellPatch)*.72;

    o.frontalLow=waveFade*saturate(max(max(commaHead*(.88+.18*frontFilament),coldFront),warmFront*.94)+.54*wcb*(.54+.46*frontFilament));
    o.postFrontCells=waveFade*postCells;
    o.frontalMid=waveFade*saturate(warmFront+.80*wcb+commaHead*.62);
    o.frontalDry=waveFade*saturate(max(drySlot,max(spiralDryTongue*.92,vortexEyeClear)));
    o.wcbCirrus=waveFade*saturate(wcb*(.72+.36*frontFilament)+commaHead*.88+warmFront*.74+coldFront*.60);
    return o;
   }
   // Mid-latitude cyclone train for organized storm-track cloud. k systems per hemisphere
   // drift east along the storm track; each keeps its identity (index) while it travels.
   // Local frame per system: X east (-1..1 across one spacing), Y poleward, both in units
   // of half the system spacing, so every planet's wave number gives the same anatomy:
   // comma head round the low, a long cold front trailing equatorward and west, a
   // warm-front shield ahead of it, a dry slot wrapping in behind the front and open
   // cumulus cells in the cold air.
   struct MidlatCloud{float low,mid,dry,cirrus,front,cells,edge,env,intensity;};
   MidlatCloud MidlatSystems(float longitude,float latitude,float3 radial,float k,float trackLat,float time)
   {
    MidlatCloud o;
    float hs=latitude>=0?0.0:37.0;
    float theta=k*longitude-time*.011+6.2831853*Hash3(float3(k,hs,3))+.9*sin(longitude+hs)+.5*sin(2.0*longitude+1.7+hs);
    float cell=floor(theta/6.2831853);
    float s=theta/6.2831853-cell-.5;
    float idA=Hash3(float3(cell,hs,1)),idB=Hash3(float3(cell,hs,2)),idC=Hash3(float3(cell,hs,5));
    float intensity=saturate(.30+.70*(.5+.5*sin(6.2831853*(time*.0009+idA))));
    float size=lerp(.82,1.18,idB);
    float halfSpacing=3.14159265/k*max(.3,cos(trackLat));
    float3 drift=float3(hs,time*.0016,cell*.37);
    float X=s*2.0+.20*(Noise3(radial*3.6+drift)-.5);
    float Y=(abs(latitude)-trackLat-(idC-.5)*.10)/halfSpacing+.16*(Noise3(radial*3.6+drift+float3(9,4,1))-.5);
    X/=size;Y/=size;

    float t=Y-.05;
    float Xf=.10+.55*t+.20*t*t+.07*(Noise3(radial*15.0+drift)-.5);
    float along=smoothstep(-1.35,-1.0,t)*(1.0-smoothstep(0.0,.12,t));
    float coldFront=exp(-pow((X-Xf)/(.10+.05*saturate(-t)),2.0))*along;
    float Yw=-.02-.32*X;
    float dW=Y-Yw;
    float warmShield=smoothstep(-.12,.02,X)*(1.0-smoothstep(.55,.95,X))*smoothstep(-.08,.06,dW)*(1.0-smoothstep(.30,.62,dW));
    float2 c=float2(X+.02,Y-.10);
    float r=length(c)/.56;
    float psi=atan2(c.y,c.x)-3.2*log(1.0+4.0*r);
    float arms=smoothstep(-.20,.60,cos(psi+.6));
    float head=(1.0-smoothstep(.70,1.05,r))*lerp(arms,1.0,.35*(1.0-smoothstep(.2,.6,r)))*smoothstep(.04,.12,r);
    float drySlot=exp(-pow((X-(Xf-.17))/.085,2.0))*smoothstep(-1.0,-.55,t)*(1.0-smoothstep(-.05,.12,t));
    float dryTongue=(1.0-arms)*(1.0-smoothstep(.55,.95,r))*smoothstep(.06,.18,r);
    float cold=smoothstep(.05,.30,(Xf-.14)-X)*smoothstep(-1.7,-1.1,t)*(1.0-smoothstep(.25,.65,Y));
    float warmSector=smoothstep(.02,.12,X-Xf)*smoothstep(.02,.12,Yw-Y)*smoothstep(-1.4,-.9,t);

    // Overcast on the cold side of the low, poleward of the comma head.
    float polewardDeck=smoothstep(.0,.35,Y)*(1.0-smoothstep(1.0,1.45,Y))*smoothstep(-1.0,-.6,X)*(1.0-smoothstep(.7,1.0,X));
    o.dry=saturate(max(drySlot,dryTongue*.9));
    float body=max(max(coldFront,warmShield),head);
    o.low=saturate(max(max(smoothstep(.10,.55,body),polewardDeck*.68),max(cold*.70,warmSector*.48)))*(1.0-.95*o.dry);
    o.front=coldFront;o.cells=cold;
    o.mid=saturate(warmShield*.9+head*.6);
    o.cirrus=saturate(warmShield*.9+head*.7+coldFront*.5)*(1.0-.8*o.dry);
    // Between two systems both sides blend into the same post-frontal cell field; the
    // track envelope uses the unshifted latitude so it is continuous from system to system.
    o.edge=smoothstep(.55,1.0,abs(s*2.0));
    float Y0=(abs(latitude)-trackLat)/halfSpacing;
    o.env=smoothstep(-1.9,-1.4,Y0)*(1.0-smoothstep(.9,1.5,Y0));
    o.intensity=intensity;
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
    float4 aux=float4(0,0,0,.68);
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
    float polarLatRad=(30.0+45.0*aux.a)*.01745329;
    float rossbyK=_GlebaCloudEnable>.5&&_GlebaCirculationParams.z>.5?_GlebaCirculationParams.z:2.6;
    float hadleyRad=_GlebaCloudEnable>.5&&_GlebaCirculationParams.x>.1?_GlebaCirculationParams.x:.5236;

    float era=floor(_VisualTime*.0125),phase=frac(_VisualTime*.0125);
    float blend=phase*phase*(3-2*phase);
    float3 wander=float3(sin(_VisualTime*.007),cos(_VisualTime*.009),sin(_VisualTime*.004))*.62;
    EraMorphology m0=SampleEraMorphology(era,phase,radial,longitude,latitude,east,north,velocity,windAxis,windSpd,polarLatRad,rossbyK,jetAxis,wander);
    EraMorphology m1=SampleEraMorphology(era+1.0,phase-1.0,radial,longitude,latitude,east,north,velocity,windAxis,windSpd,polarLatRad,rossbyK,jetAxis,wander);

    float planetary=lerp(m0.planetary,m1.planetary,blend);
    float baseTex=lerp(m0.baseTex,m1.baseTex,blend);
    float flowBands=lerp(m0.flowBands,m1.flowBands,blend);
    float mesoCells=lerp(m0.mesoCells,m1.mesoCells,blend);
    float fineGrain=lerp(m0.fineGrain,m1.fineGrain,blend);
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

     // Organized cloud systems. The climatological fraction (synLift) is only reached
     // on average: each regime decides WHERE the cloud is in a snapshot (cyclone train,
     // ITCZ clusters, cold-current decks, fine trade cumulus), so cloud gathers into
     // systems with clear air between them instead of an even speckle.
     float stormWeight=smoothstep(.12,.45,stormTrack);
     float zBase=(baseTex-.502)*10.6;
     float uLow=rcp(1.0+exp(-1.702*clamp(zBase+.52*orogCrest*max(0.0,zBase+.35),-6.0,6.0)));
     float detail=flowBands*.40+mesoCells*.34+fineGrain*.26;
     // Background: large masses with crisp edges and a thin textured rim.
     float rampB=.12;
     float background=smoothstep(1.0-lowTarget-rampB,1.0-lowTarget+rampB,uLow);
     background=saturate(background-(1.0-detail)*.22*background*(1.0-background)*4.0);
     // Optical depth follows how cloudy the climate is: thin veils where cloud is rare.
     background*=.55+.45*saturate(climTarget*1.3);

     // Mid-latitude storm track: cyclone train.
     float k=clamp(floor(rossbyK*2.3+.5),2.0,12.0);
     float trackLat=max(.35,polarLatRad-.10);
     MidlatCloud mc=MidlatSystems(longitude,latitude,radial,k,trackLat,_VisualTime);
     float stormBackground=smoothstep(1.0-climTarget-rampB,1.0-climTarget+rampB,uLow);
     float cellLace=smoothstep(.28,.82,abs(2.0*Noise3(radial*58.0+float3(3,7,1))-1.0))*(.55+.45*smoothstep(.35,.70,mesoCells));
     // Open cells leave clean gaps; the cells themselves are moderately thick.
     float openCells=saturate((cellLace-.20)*1.5)*.68;
     float stormLow=max(mc.low*(1.0-mc.cells*.55),mc.cells*openCells*.85);
     stormLow=lerp(lerp(stormBackground*.6,stormLow,mc.intensity),openCells,mc.edge);
     float stormMix=stormWeight*mc.env;
     float sysMix=stormMix*(1.0-mc.edge);
     float lowStorm=lerp(stormBackground,stormLow,stormMix);

     // Deep tropical convection: ITCZ, monsoon and rain-forest clusters, zonally elongated.
     float trop=saturate(max(itczConv*1.45,smoothstep(.22,.60,wet)*tropBelt*(1.0-.55*leeShadow)*.85));
     float3 zonal=normalize(east+float3(1e-5,0,0));
     float3 pc=RotateY(radial,-_VisualTime*.0012);
     float clusters=Fbm((pc-zonal*dot(pc,zonal)*.70)*9.0+float3(5,9,2))*.62+mcsCluster*.38;
     float tropCover=.10+.82*trop;
     float lowTrop=smoothstep(1.0-tropCover-.11,1.0-tropCover+.11,clusters+.30*(trop-.5))*smoothstep(.05,.25,trop)*(.58+.42*smoothstep(.45,.85,clusters+.35*trop));

     // Cold-current stratocumulus: a closed deck with honeycomb cells, breaking up at its edge.
     float closed=1.0-abs(2.0*Noise3(RotateY(radial,-_VisualTime*.0006)*44.0+float3(2,5,8))-1.0);
     float deckField=marineSc+.22*(planetary-.5)+.10*(honeycomb-.5);
     float noDeck=(1.0-smoothstep(.25,.60,trop))*(1.0-stormWeight*.7);
     float deck=smoothstep(.44,.60,deckField)*noDeck;
     float lowDeck=deck*(.68+.22*smoothstep(.15,.65,closed));
     // Weaker Sc signal: broken cells that thin westward into trade cumulus.
     float brokenCover=saturate(.20+.55*smoothstep(.25,.62,deckField));
     float lowBroken=smoothstep(1.0-brokenCover-.12,1.0-brokenCover+.12,closed*.55+mesoCells*.45)*.62*noDeck*smoothstep(.20,.40,marineSc);

     // Trade cumulus: fine streets and puffs in mesoscale fields, never solid.
     float3 ps=RotateY(radial,-_VisualTime*.0009);
     float streets=Noise3((ps-windAxis*dot(ps,windAxis)*.85)*46.0+float3(1,3,7))*.60+Noise3(ps*74.0+float3(7,1,4))*.40;
     float tradeEnv=smoothstep(.36,.66,Fbm(ps*8.5+float3(4,4,9)));
     float tradeCover=saturate(lowTarget*.85)*(.45+.55*tradeEnv);
     float lowTrade=smoothstep(1.0-tradeCover-.11,1.0-tradeCover+.11,streets)*.55;
     float tradeWeight=tropBelt*(1.0-stormWeight);

     low=lerp(background,lowTrade,tradeWeight*(1.0-trop));
     low=lerp(low,lowStorm,stormWeight);
     low=max(low,lowTrop*(1.0-stormWeight*.5));
     low=max(low,max(lowDeck,lowBroken*(1.0-stormWeight)));
     low=saturate(low+.20*orogCrest*background+.04*response.x*low);

     // Cloud type in convection (weather.b): Sc .06, trade Cu .22, frontal/stratiform .45, deep .75+.
     float deepShare=saturate(max(lowTrop*trop,sysMix*mc.front*.85));
     float deepTrigger=smoothstep(.25,.70,deepShare*(.55+.45*mcsCluster)+.20*itczConv*smoothstep(.55,.90,effWet));
     storm=saturate(smoothstep(.22,.76,effWet)*smoothstep(.60,.95,low)*deepTrigger*(1+.35*response.y)*(1.0-.92*deck));
     float midShare=saturate(sysMix*mc.mid+.45*orogCrest*background);
     convection=.20+.11*smoothstep(.20,.75,flowBands);
     convection=lerp(convection,.40+.13*mc.mid,saturate(midShare*1.15));
     convection=lerp(convection,saturate(.64+.20*deepShare+.22*storm),saturate(deepTrigger*1.10+storm*.60));
     convection=lerp(convection,.09,lowBroken*(1.0-stormWeight)*.8);
     convection=lerp(convection,.06+.05*closed,deck);
     wcbCirrus=lerp(wcbCirrus*.4,mc.cirrus,sysMix*mc.intensity);

     // Upper-tropospheric Rossby jet streaks & downwind anvil/WCB plumes coupled to active synoptic systems
     // (clears subsiding dry gyres while trailing sweeping feathery cirrus plumes downwind of storms!)
     float3 upper=RotateY(radial,-_VisualTime*.0018);
     float3 crossJet=normalize(cross(radial,jetAxis)+float3(1e-5,0,0));
     float3 jetShear=upper+wander*.04;
     float jetStreak=Fbm((jetShear-jetAxis*dot(jetShear,jetAxis)*.90)*14.0+float3(9,4,2));
     float crossStreak=Fbm((jetShear-crossJet*dot(jetShear,crossJet)*.86)*18.0+float3(3,2,7));
     float poleTaper=smoothstep(0,.16,length(radial.xz));
     float wisps=lerp(crossStreak,jetStreak,.24+.72*poleTaper);

     float zJet=(Fbm(upper*2.8+wander*.45+float3(12,2,8))-.50)*5.2+(wisps-.50)*4.2+1.35*(uLow-.50);
     float zAnvil=2.55*(anvilShield-.20)+(wisps-.50)*3.8+1.10*(uLow-.45);
     float zWcb=.28*zJet+3.65*(wcbCirrus-.15)+(wisps-.50)*2.4;
     float zHigh=lerp(zJet,zAnvil,saturate(itczConv*1.15));
     zHigh=lerp(zHigh,zWcb,stormWeight);
     float uHigh=rcp(1.0+exp(-1.702*clamp(zHigh,-6.0,6.0)));
     float wHigh=max(1e-4,min(.36,min(highTarget,1.0-highTarget)));
     high=smoothstep(1.0-highTarget-wHigh,1.0-highTarget+wHigh,uHigh)*(1.0-.68*tropBelt*leeShadow);
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
