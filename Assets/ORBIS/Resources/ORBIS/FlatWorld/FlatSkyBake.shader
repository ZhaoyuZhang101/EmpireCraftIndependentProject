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
   float4 _CyclonePhase;
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
    float baseTex,flowBands,mesoCells,honeycomb,mcsCluster,anvilShield,frontalLow,frontalMid,frontalDry,wcbCirrus;
   };
   EraMorphology SampleEraMorphology(
    float eraId,float age,float3 radial,float longitude,float latitude,
    float3 east,float3 north,float3 velocity,float3 windAxis,float windSpd,float polarLatRad,float3 jetAxis,float3 wander)
   {
    EraMorphology o;
    float3 p=normalize(radial-velocity*age*7.2);
    float3 seed=float3(Hash3(float3(eraId,4,7)),Hash3(float3(eraId,11,3)),Hash3(float3(eraId,5,19)))*15.0;

    // Gentle diffeomorphic tangent-sphere flow warp (Jacobian > 0.68 everywhere -> zero concentric marble rings):
    float3 c1=float3(
     Noise3(p*4.0+wander+seed+17.0),
     Noise3(p*4.0+wander+seed+43.0),
     Noise3(p*4.0+wander+seed+71.0))-.5;
    c1-=p*dot(p,c1);
    float3 p1=normalize(p+c1*.078);

    float3 c2=float3(
     Noise3(p1*10.5+seed+11.0),
     Noise3(p1*10.5+seed+29.0),
     Noise3(p1*10.5+seed+53.0))-.5;
    c2-=p1*dot(p1,c2);
    float3 pFlow=normalize(p1+c2*.034);

    float streakAniso=lerp(.20,.40,windSpd);
    float3 pStreak=pFlow-windAxis*dot(pFlow,windAxis)*streakAniso;
    float synoptic=Fbm(pFlow*6.8+wander+seed);
    float flowBands=Fbm(Turn(pStreak)*17.5+wander+seed);
    float mesoCells=Fbm(Turn(pFlow)*36.0+seed);

    o.flowBands=flowBands;
    o.mesoCells=mesoCells;
    o.baseTex=synoptic*.36+flowBands*.36+mesoCells*.28;

    // Subtropical eastern-ocean marine stratocumulus: cellular Bernard honeycomb decks
    float cellA=1.0-abs(2.0*Noise3(Turn(pFlow)*28.0+seed)-1.0);
    float cellB=1.0-abs(2.0*Noise3(Turn(pFlow)*52.0+seed+9.0)-1.0);
    o.honeycomb=synoptic*.34+pow(cellA,1.30)*.42+cellB*.24;

    // Tropical/monsoon organized mesoscale convective clusters (MCS) + sweeping downwind cirrus anvils
    float mcsEnv=smoothstep(.44,.79,synoptic*.46+flowBands*.34+mesoCells*.20);
    o.mcsCluster=mcsEnv*(.22+.78*smoothstep(.42,.80,mesoCells));
    float3 pAnvilNear=normalize(pFlow-jetAxis*.034);
    float3 pAnvilFar=normalize(pFlow-jetAxis*.080);
    float anvilNear=Fbm(pAnvilNear*8.8+wander+seed);
    float anvilFar=Fbm(pAnvilFar*6.0+wander+seed);
    o.anvilShield=smoothstep(.44,.80,anvilNear*.62+anvilFar*.38)*(.34+.66*mcsEnv);

    // Strengthened mid-latitude Shapiro-Keyser / Norwegian 2-turn spiral comma cyclones & 3,800 km frontal ribbons
    float eraLon=6.2831853*Hash3(float3(eraId,9,2));
    float theta=4.0*longitude-.95*age+eraLon+.52*sin(longitude+seed.x)+.26*sin(2.0*longitude-.40*age+seed.z);
    float waveS=sin(theta),waveC=cos(theta);
    float waveFade=smoothstep(-.86,-.06,waveC);
    float dx=atan2(waveS,waveC)/4.0*max(.24,cos(latitude));
    float dy=(abs(latitude)-polarLatRad-.085*sin(2.0*longitude-.45*age+seed.y)-.042*cos(3.0*longitude+seed.x));

    // Tight 2-turn cyclonic logarithmic-spiral occluded comma head + bent-back warm seclusion hook
    float2 vortVec=float2(dx+.045,dy-.066);
    float rVort=length(vortVec)/.29;
    float vortAng=atan2(vortVec.y,vortVec.x+1e-6)+3.45*log(1.0+6.0*rVort)+.68*(flowBands-.5);
    float spiralPrimary=smoothstep(-.22,.78,cos(vortAng));
    float spiralSecondary=smoothstep(.16,.88,cos(2.0*vortAng+1.10))*.72;
    float commaShield=(1.0-smoothstep(.14,.64,rVort))*.62;
    float commaHead=(1.0-smoothstep(.38,1.14,rVort))*smoothstep(.022,.11,rVort)*max(max(spiralPrimary,spiralSecondary),commaShield);

    // Long curved cold-front squall ribbon + warm-front altostratus slope + crisp dry-slot clearing
    float3 frontTangent=normalize(east*.62+north*(latitude>=0?1.0:-1.0)*.78+1e-5);
    float3 pFrontShear=pFlow-frontTangent*dot(pFlow,frontTangent)*.76;
    float frontFilament=Fbm(Turn(pFrontShear)*22.0+seed);

    float dxFront=-.02+.78*(dy-.05)-.40*(dy-.05)*abs(dy-.05);
    float dFront=dx-dxFront+.025*(flowBands-.5)+.014*(frontFilament-.5);
    float coldFront=exp(-pow(dFront/.046,2.0))*smoothstep(-.48,-.24,dy)*(1.0-smoothstep(.08,.22,dy))*(.65+.45*frontFilament);

    float dWarm=(dy-(.066-.58*max(0.0,dx+.02)))+.022*(flowBands-.5);
    float warmFront=exp(-pow(dWarm/.064,2.0))*smoothstep(-.06,.04,dx)*(1.0-smoothstep(.20,.38,dx))*(.60+.45*frontFilament);

    float wcb=smoothstep(-.02,.09,dFront)*(1.0-smoothstep(.09,.30,dFront))*smoothstep(-.24,.18,dy)*(1.0-smoothstep(.18,.36,dy));
    float drySlot=exp(-pow((dFront+.076)/.054,2.0))*smoothstep(-.32,-.02,dy)*(1.0-smoothstep(.08,.22,dy));
    float coldCells=smoothstep(.09,.22,-dFront)*(1.0-smoothstep(.24,.42,-dFront))*smoothstep(-.34,.12,dy)*smoothstep(.44,.82,mesoCells)*.56;

    o.frontalLow=waveFade*saturate(max(max(commaHead*(.84+.24*frontFilament),coldFront),warmFront*.92)+.50*wcb*(.48+.52*frontFilament)+coldCells);
    o.frontalMid=waveFade*saturate(warmFront+.75*wcb+commaHead*.55);
    o.frontalDry=waveFade*drySlot;
    o.wcbCirrus=waveFade*saturate(wcb*(.65+.45*frontFilament)+commaHead*.82+warmFront*.68);
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

    float era=floor(_VisualTime*.0125),phase=frac(_VisualTime*.0125);
    float blend=phase*phase*(3-2*phase);
    float3 wander=float3(sin(_VisualTime*.007),cos(_VisualTime*.009),sin(_VisualTime*.004))*.62;
    EraMorphology m0=SampleEraMorphology(era,phase,radial,longitude,latitude,east,north,velocity,windAxis,windSpd,polarLatRad,jetAxis,wander);
    EraMorphology m1=SampleEraMorphology(era+1.0,phase-1.0,radial,longitude,latitude,east,north,velocity,windAxis,windSpd,polarLatRad,jetAxis,wander);

    float baseTex=lerp(m0.baseTex,m1.baseTex,blend);
    float flowBands=lerp(m0.flowBands,m1.flowBands,blend);
    float mesoCells=lerp(m0.mesoCells,m1.mesoCells,blend);
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
     float tropBelt=1.0-smoothstep(.16,.42,abs(radial.y));
     float effWet=wet*(1.0-.72*leeShadow);

     float highShare=saturate(.54*itczConv+.42*stormTrack+.18*moistEnv)*(1.0-.70*marineSc)*(1.0-.65*leeShadow);
     float climTarget=saturate(synLift+.11*response.x*(1.0-.50*leeShadow));
     float lowTarget=saturate(climTarget*(1.0-.28*highShare));
     float highTarget=saturate((climTarget-lowTarget)/max(.14,1.0-lowTarget)+.11*highShare*climTarget);

     float zBase=(baseTex-.506)*10.8;
     float zFlow=(flowBands*.62+mesoCells*.38-.506)*9.6;
     float zHoney=(honeycomb-.545)*7.6;
     float zMcs=.56*zBase+.48*zFlow+1.25*(mcsCluster-.20);
     // Strong vortex-dominated midlatitude comma cyclones & sharp frontal bands
     float zFront=.18*zFlow+4.35*(frontalLow-.21)-3.25*frontalDry;
     float stormWeight=smoothstep(.16,.66,stormTrack);

     float zLow=lerp(zBase,zFlow,.55);
     zLow=lerp(zLow,zMcs,saturate(tropBelt*(.42+.58*itczConv)));
     zLow=lerp(zLow,zHoney,saturate(marineSc*.88));
     zLow=lerp(zLow,zFront,stormWeight);
     zLow+=.45*orogCrest*max(0.0,zBase+.35);
     float uLow=rcp(1.0+exp(-1.702*clamp(zLow,-6.0,6.0)));

     float sharpOrg=saturate(max(stormWeight*frontalLow,itczConv*mcsCluster));
     float rampCap=lerp(.21,.13,sharpOrg);
     float wLow=max(1e-4,min(rampCap,min(lowTarget,1.0-lowTarget)));
     low=smoothstep(1.0-lowTarget-wLow,1.0-lowTarget+wLow,uLow);
     low=saturate(low+.04*response.x*smoothstep(.02,.18,low+.15*climTarget));

     // Explicit 5-tier cloud altitude classification in convection (weather.b):
     //   0.04..0.13 = Ultra-low Marine Stratocumulus (420-1,450 m)
     //   0.18..0.33 = Elevated Fair-Weather & Trade Cumulus (1,020-3,050 m)
     //   0.36..0.54 = Frontal / Monsoon-Margin Mid-Level Altocumulus & Altostratus (3,900-5,950 m)
     //   0.64..1.00 = Penetrative Towering Cumulonimbus & Cyclone Eyewall (520-12,600 m + Anvil)
     float scShare=saturate(marineSc*.86-max(stormWeight*frontalLow,itczConv*mcsCluster)*.95);
     float midShare=saturate(max(stormWeight*frontalMid*(1.0-.55*smoothstep(.65,.95,frontalLow)),
                                 (.35*itczConv+.45*orogCrest)*(1.0-smoothstep(.55,.88,mcsCluster)))*(1.0-scShare));
     float corePeak=smoothstep(.48,.88,mesoCells);
     float deepShare=saturate(max(itczConv*smoothstep(.45,.88,uLow)*(.45+.55*mcsCluster),
                                  stormWeight*smoothstep(.52,.92,frontalLow)*.88));
     float deepTrigger=smoothstep(.20,.68,deepShare*(.52+.48*corePeak)+.32*synLift*orogCrest+.20*itczConv*smoothstep(.55,.90,effWet));
     storm=saturate(smoothstep(.22,.76,effWet)*smoothstep(.60,.95,low)*deepTrigger*(1+.35*response.y)*(1.0-.92*scShare));

     float tradeType=.20+.11*smoothstep(.20,.75,flowBands);
     float midType=.40+.13*frontalMid;
     float deepType=saturate(.64+.20*deepShare+.22*storm);
     convection=tradeType;
     convection=lerp(convection,midType,saturate(midShare*1.15));
     convection=lerp(convection,deepType,saturate(deepTrigger*1.10+storm*.60));
     convection=lerp(convection,.06+.05*honeycomb,scShare);

     // High-altitude Rossby jet streaks, warm-conveyor cirrostratus & sheared ITCZ anvil canopies (8-13.2 km)
     float3 upper=RotateY(radial,-_VisualTime*.0018);
     float3 crossJet=normalize(cross(radial,jetAxis)+float3(1e-5,0,0));
     float3 jetShear=upper+wander*.04;
     float jetStreak=Fbm((jetShear-jetAxis*dot(jetShear,jetAxis)*.88)*18.0+float3(9,4,2));
     float crossStreak=Fbm((jetShear-crossJet*dot(jetShear,crossJet)*.86)*21.0+float3(3,2,7));
     float poleTaper=smoothstep(0,.16,length(radial.xz));
     float wisps=lerp(crossStreak,jetStreak,.22+.74*poleTaper);

     float zJet=(Fbm(upper*5.2+wander*.5+float3(12,2,8))-.50)*6.4+(wisps-.50)*5.2;
     float zAnvil=2.15*(anvilShield-.22)+(wisps-.50)*4.4;
     float zWcb=.34*zJet+3.35*(wcbCirrus-.16)+(wisps-.50)*2.6;
     float zHigh=lerp(zJet,zAnvil,saturate(itczConv*1.15));
     zHigh=lerp(zHigh,zWcb,stormWeight);
     float uHigh=rcp(1.0+exp(-1.702*clamp(zHigh,-6.0,6.0)));
     float wHigh=max(1e-4,min(.32,min(highTarget,1.0-highTarget)));
     high=smoothstep(1.0-highTarget-wHigh,1.0-highTarget+wHigh,uHigh)*(1.0-.65*tropBelt*leeShadow);
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
