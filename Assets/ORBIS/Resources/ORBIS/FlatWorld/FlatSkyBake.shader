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
    float planetary,baseTex,flowBands,mesoCells,fineGrain,honeycomb,mcsCluster,anvilShield,frontalLow,frontalMid,frontalDry,wcbCirrus;
   };
   EraMorphology SampleEraMorphology(
    float eraId,float age,float3 radial,float longitude,float latitude,
    float3 east,float3 north,float3 velocity,float3 windAxis,float windSpd,float polarLatRad,float rossbyK,float3 jetAxis,float3 wander)
   {
    EraMorphology o;
    float3 p=normalize(radial-velocity*age*7.2);
    float3 seed=float3(Hash3(float3(eraId,4,7)),Hash3(float3(eraId,11,3)),Hash3(float3(eraId,5,19)))*15.0;

    // Gentle divergence-free spherical curl-streamfunction warp (cross(p, gradPsi)):
    // Gives organic fluid curvature without folding contours into wood-grain / marble loops!
    float3 psi1=float3(
     Fbm(p*2.3+wander*.60+seed+17.0),
     Fbm(p*2.3+wander*.60+seed+43.0),
     Fbm(p*2.3+wander*.60+seed+71.0))-.5;
    float3 curl1=cross(p,psi1);
    float3 p1=normalize(p+curl1*.095);

    float3 psi2=float3(
     Noise3(Turn(p1)*6.4+seed+11.0),
     Noise3(Turn(p1)*6.4+seed+29.0),
     Noise3(Turn(p1)*6.4+seed+53.0))-.5;
    float3 curl2=cross(p1,psi2);
    float3 pFlow=normalize(p1+curl2*.042);

    // Low-level wind & upper-level jet shear stretching for organic, fibrous satellite cloud bands
    float streakAniso=lerp(.28,.54,windSpd);
    float3 pStreak=pFlow-windAxis*dot(pFlow,windAxis)*streakAniso;
    float3 pJetStreak=pFlow-jetAxis*dot(pFlow,jetAxis)*.48;
    float planetary=Fbm(p1*2.10+wander*.55+seed*.7+float3(3.1,7.4,1.9));
    float synoptic=Fbm(Turn(pFlow)*4.8+wander*.85+seed);
    float flowBands=Fbm(Turn(pStreak)*14.5+wander+seed*1.4);
    float mesoCells=Fbm(Turn(pFlow)*34.0+seed*1.9);
    float fineGrain=Fbm(Turn(pJetStreak)*64.0+seed*2.7);

    o.planetary=planetary;
    o.flowBands=flowBands;
    o.mesoCells=mesoCells;
    o.fineGrain=fineGrain;
    // Macro-synoptic organization (concentrated 3,000-8,000 km regimes) + ragged multi-scale satellite texture
    o.baseTex=planetary*.42+synoptic*.30+flowBands*.16+mesoCells*.08+fineGrain*.04;

    // Subtropical eastern-ocean marine stratocumulus: cohesive macro coastal sheet with fine Bernard cellular texture
    float cellA=1.0-abs(2.0*Noise3(Turn(pFlow)*22.0+seed)-1.0);
    float cellB=1.0-abs(2.0*Noise3(Turn(pFlow)*44.0+seed+9.0)-1.0);
    o.honeycomb=planetary*.38+synoptic*.28+pow(cellA,1.25)*.22+cellB*.12;

    // Concentrated tropical/monsoon MCS superclusters + long downwind jet-sheared cirrus anvils
    float mcsEnv=smoothstep(.43,.78,planetary*.44+synoptic*.36+flowBands*.20);
    o.mcsCluster=mcsEnv*(.30+.70*smoothstep(.38,.78,synoptic*.55+mesoCells*.45));
    float3 pAnvilNear=normalize(pFlow-jetAxis*.048);
    float3 pAnvilFar=normalize(pFlow-jetAxis*.115);
    float anvilNear=Fbm(pAnvilNear*4.8+wander*.7+seed);
    float anvilFar=Fbm(pAnvilFar*3.2+wander*.6+seed);
    o.anvilShield=smoothstep(.43,.78,anvilNear*.58+anvilFar*.42)*(.28+.72*mcsEnv);

    // Asymmetric, non-repeating Rossby wave packets (adapts dynamically to DoubleCell k=1..2 vs ThreeCell k=2..3;
    // independent Northern & Southern Hemisphere seeds so NH and SH never mirror each other!):
    float hemiSign=latitude>=0?1.0:-1.0;
    float3 hSeed=seed+(latitude>=0?float3(0,0,0):float3(19.7,41.3,13.9));
    float k1=max(1.0,floor(rossbyK-.35+.5));
    float k2=k1+1.0;
    float eraLon1=6.2831853*Hash3(float3(eraId,9.0+hemiSign*2.0,2));
    float eraLon2=6.2831853*Hash3(float3(eraId,15.0+hemiSign*3.0,6));
    float packPhase=6.2831853*Hash3(float3(eraId,23.0+hemiSign*5.0,11));

    // Longitudinal wave-packet envelope: concentrates baroclinic storms into 1-2 dominant ocean-basin systems
    // with wide clear high-pressure ridges in between, rather than 4 identical clones around a latitude circle!
    float packetWave=.64*cos(longitude-.35*age+packPhase)+.36*cos(2.0*longitude+.22*age+eraLon2);
    float packetEnv=smoothstep(-.32,.48,packetWave+(planetary-.5)*1.15);

    float theta=k1*longitude-.88*age+eraLon1+.68*sin(k2*longitude-1.12*age+eraLon2)+.34*sin(longitude+hSeed.x);
    float waveS=sin(theta),waveC=cos(theta);
    float waveFade=smoothstep(-.78,.04,waveC)*(.28+.72*packetEnv);
    float dx=atan2(waveS,waveC)/max(1.3,k1+.35)*max(.24,cos(latitude));
    float dy=(abs(latitude)-polarLatRad-.105*sin(k1*longitude-.42*age+hSeed.y)-.058*cos(k2*longitude+hSeed.z));

    // Organic asymmetric Shapiro-Keyser comma shield + bent-back occlusion hook
    float2 vortVec=float2(dx+.048+.035*(synoptic-.5),dy-.064+.028*(planetary-.5));
    float rVort=length(vortVec)/lerp(.33,.46,saturate(2.2-k1));
    float vortAng=atan2(vortVec.y,vortVec.x+1e-6)+3.15*log(1.0+5.4*rVort)+.85*(synoptic-.5)+.45*(flowBands-.5);
    float spiralPrimary=smoothstep(-.26,.74,cos(vortAng));
    float spiralSecondary=smoothstep(.18,.88,cos(2.0*vortAng+1.15))*.68;
    float commaShield=(1.0-smoothstep(.18,.74,rVort))*.78;
    float commaHead=(1.0-smoothstep(.42,1.18,rVort))*smoothstep(.018,.095,rVort)*max(max(spiralPrimary,spiralSecondary),commaShield);

    // Sweeping 3,800 km diagonal cold-front ribbon + broad warm-front shield + deep dry-intrusion clearing slot
    float3 frontTangent=normalize(east*.64+north*hemiSign*.77+1e-5);
    float3 pFrontShear=pFlow-frontTangent*dot(pFlow,frontTangent)*.80;
    float frontFilament=Fbm(Turn(pFrontShear)*14.5+hSeed);

    float dxFront=-.02+.82*(dy-.05)-.42*(dy-.05)*abs(dy-.05);
    float dFront=dx-dxFront+.038*(synoptic-.5)+.022*(frontFilament-.5);
    float coldFront=exp(-pow(dFront/.058,2.0))*smoothstep(-.54,-.22,dy)*(1.0-smoothstep(.10,.26,dy))*(.68+.42*frontFilament);

    float dWarm=(dy-(.068-.56*max(0.0,dx+.02)))+.030*(synoptic-.5);
    float warmFront=exp(-pow(dWarm/.078,2.0))*smoothstep(-.08,.05,dx)*(1.0-smoothstep(.22,.44,dx))*(.64+.42*frontFilament);

    float wcb=smoothstep(-.03,.10,dFront)*(1.0-smoothstep(.11,.36,dFront))*smoothstep(-.28,.20,dy)*(1.0-smoothstep(.20,.40,dy));
    float drySlot=exp(-pow((dFront+.088)/.068,2.0))*smoothstep(-.38,-.02,dy)*(1.0-smoothstep(.10,.26,dy));
    float coldCells=smoothstep(.11,.25,-dFront)*(1.0-smoothstep(.26,.46,-dFront))*smoothstep(-.36,.12,dy)*smoothstep(.48,.84,mesoCells)*.42;

    o.frontalLow=waveFade*saturate(max(max(commaHead*(.86+.20*frontFilament),coldFront),warmFront*.94)+.56*wcb*(.55+.45*frontFilament)+coldCells);
    o.frontalMid=waveFade*saturate(warmFront+.78*wcb+commaHead*.60);
    o.frontalDry=waveFade*drySlot;
    o.wcbCirrus=waveFade*saturate(wcb*(.70+.38*frontFilament)+commaHead*.86+warmFront*.72);
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
    EraMorphology m0=SampleEraMorphology(era,phase,radial,longitude,latitude,east,north,velocity,windAxis,windSpd,polarLatRad,rossbyK,jetAxis,wander);
    EraMorphology m1=SampleEraMorphology(era+1.0,phase-1.0,radial,longitude,latitude,east,north,velocity,windAxis,windSpd,polarLatRad,rossbyK,jetAxis,wander);

    float planetary=lerp(m0.planetary,m1.planetary,blend);
    float baseTex=lerp(m0.baseTex,m1.baseTex,blend);
    float flowBands=lerp(m0.flowBands,m1.flowBands,blend);
    float mesoCells=lerp(m0.mesoCells,m1.mesoCells,blend);
    float fineGrain=lerp(m0.fineGrain,m1.fineGrain,blend);
    float honeycomb=lerp(m0.honeycomb,m1.honeycomb,blend);
    float mcsCluster=lerp(m0.mcsCluster,m1.mcsCluster,blend);
    float anvilShield=lerp(m0.anvilShield,m1.anvilShield,blend);
    float frontalLow=lerp(m0.frontalLow,m1.frontalLow,blend);
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

     float highShare=saturate(.54*itczConv+.44*stormTrack+.16*moistEnv)*(1.0-.72*marineSc)*(1.0-.65*leeShadow);
     float climTarget=saturate(synLift+.11*response.x*(1.0-.50*leeShadow));
     float lowTarget=saturate(climTarget*(1.0-.26*highShare));
     float highTarget=saturate((climTarget-lowTarget)/max(.14,1.0-lowTarget)+.12*highShare*climTarget);

     // Macro-concentrated weather regimes (eliminates uniform salt-and-pepper ocean confetti!):
     float zBase=(baseTex-.502)*10.6;
     float zFlow=(planetary*.42+flowBands*.42+mesoCells*.16-.502)*9.8;
     float zHoney=(honeycomb-.535)*8.2;
     float zMcs=.68*zBase+.22*zFlow+1.55*(mcsCluster-.18);
     // Concentrated vortex-dominated comma shields + sweeping fronts + wide dry-slot clearing
     float zFront=.30*zBase+4.65*(frontalLow-.18)-3.65*frontalDry;
     float stormWeight=smoothstep(.16,.64,stormTrack);

     float zLow=lerp(zBase,zFlow,.32);
     zLow=lerp(zLow,zMcs,saturate(tropBelt*(.46+.54*itczConv)));
     zLow=lerp(zLow,zHoney,saturate(marineSc*.86));
     zLow=lerp(zLow,zFront,stormWeight);
     zLow+=.48*orogCrest*max(0.0,zBase+.35);
     float uLow=rcp(1.0+exp(-1.702*clamp(zLow,-6.0,6.0)));

     // Hierarchical multiplicative edge & interior cloud lace: clear basins stay 100% clean deep blue,
     // while active weather systems break into ragged fibrous wisps, cellular streets & bright convective cores!
     float sharpOrg=saturate(max(stormWeight*frontalLow,itczConv*mcsCluster));
     float rampCap=lerp(.30,.20,sharpOrg);
     float wLow=max(1e-4,min(rampCap,min(lowTarget,1.0-lowTarget)));
     float rawLow=smoothstep(1.0-lowTarget-wLow,1.0-lowTarget+wLow,uLow);
     float detailField=flowBands*.38+mesoCells*.36+fineGrain*.26;
     float edgeBand=smoothstep(.02,.35,rawLow)*(1.0-smoothstep(.58,.96,rawLow));
     float erodedEdge=saturate((rawLow-(1.0-detailField)*.48*edgeBand)/max(.22,1.0-.32*edgeBand));
     float texturedCore=erodedEdge*(.76+.34*smoothstep(.28,.80,detailField));
     low=saturate(lerp(texturedCore,max(texturedCore,.90),smoothstep(.78,.98,rawLow)*(sharpOrg*.65+.35))+
                  .04*response.x*smoothstep(.02,.18,rawLow+.15*climTarget));

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
