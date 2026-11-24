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
    float planetary,baseTex,flowBands,mesoCells,fineGrain,curvedBand,curvedWisp,tradeStreets,honeycomb,mcsCluster,anvilShield,frontalLow,frontalMid,frontalDry,postFrontCells,wcbCirrus;
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
    float3 p1=normalize(p+curl1*.125);

    float3 psi2=float3(
     Noise3(Turn(p1)*5.8+seed+11.0),
     Noise3(Turn(p1)*5.8+seed+29.0),
     Noise3(Turn(p1)*5.8+seed+53.0))-.5;
    float3 curl2=cross(p1,psi2);
    float3 pFlow=normalize(p1+curl2*.048);

    // 1. Multi-scale TRUE spherical flow-combed fields (3-tap tangent LIC along local climate wind!).
    // NOTE: On a unit sphere dot(p, tangent) == 0 identically; true spherical tangent LIC genuinely combs
    // clouds 3x-5x along real curved planetary winds at every latitude with zero seams!
    float3 flowDir=normalize(lerp(windAxis,jetAxis,.38)+float3(1e-5,0,0));
    float licStep=lerp(.012,.024,windSpd);
    float3 pFlowP=normalize(pFlow+flowDir*licStep);
    float3 pFlowM=normalize(pFlow-flowDir*licStep);
    float planetary=Fbm(p1*2.05+wander*.50+seed*.7+float3(3.1,7.4,1.9));
    float synoptic=(Noise3(Turn(pFlow)*5.2+wander*.80+seed)+
                    Noise3(Turn(pFlowP)*5.2+wander*.80+seed)+
                    Noise3(Turn(pFlowM)*5.2+wander*.80+seed))*(1.0/3.0);
    float3 pWindP=normalize(pFlow+windAxis*(licStep*.85));
    float3 pWindM=normalize(pFlow-windAxis*(licStep*.85));
    float flowBands=(Noise3(Turn(pFlow)*18.0+wander+seed*1.4)+
                     Noise3(Turn(pWindP)*18.0+wander+seed*1.4)+
                     Noise3(Turn(pWindM)*18.0+wander+seed*1.4))*(1.0/3.0);
    float mesoCells=Fbm(Turn(pFlow)*38.0+seed*1.9);
    float fineGrain=Noise3(Turn(pFlow)*68.0+seed*2.7)*.55+
                    Noise3(Turn(normalize(pFlow+jetAxis*.012))*68.0+seed*2.7)*.45;

    // 1b. Geostrophic Baroclinic Curved-Band FBM (Authentic NASA slender sweeping curved cloud bands!):
    // Stretches cross-stream meridional axis (pWave.y) by 4.6x-6.1x relative to along-stream (x,z) AFTER warping
    // the unit sphere by large-scale divergence-free planetary curl1 (R_curv ~ 2,500-5,000 km) + smooth C-infinity
    // equatorial-continuous baroclinic Rossby tilt. Perturbed by crisp multi-scale cloud tufts & LIC filaments BEFORE
    // thresholding so EVERY slender curved arc has ragged, feathery, tattered NASA edges (zero smooth ovals/seams!).
    float eqWeight=smoothstep(.04,.44,length(radial.xz));
    float yStretch1=lerp(1.90,6.60,eqWeight);
    float yStretch2=lerp(2.25,8.80,eqWeight);
    float3 pWave=normalize(p+curl1*.33+curl2*.045);
    float hemiDiag=.24*(latitude/(abs(latitude)+.32));
    float baroY=pWave.y+hemiDiag*sin(longitude*2.0+psi1.x*2.4)*eqWeight*.16+.12*psi1.y;
    float3 qBand1=float3(pWave.x*1.45,baroY*yStretch1,pWave.z*1.45);
    float3 qBand2=float3(pWave.x*2.75,baroY*yStretch2,pWave.z*2.75);
    float bandField1=Fbm(qBand1+wander*.42+seed+float3(13.4,27.1,5.8));
    float bandField2=Noise3(qBand2+wander*.60+seed+float3(47.2,11.8,19.4))*.62+
                     Noise3(qBand2*1.9+wander*.72+seed+float3(19.1,5.7,31.4))*.38;
    float bandH=bandField1*.48+bandField2*.26+flowBands*.14+mesoCells*.08+fineGrain*.04;
    float bandCore=smoothstep(.48,.81,bandH);
    float fineStreamer=smoothstep(.51,.84,bandField2*.48+flowBands*.30+fineGrain*.22);
    float ribbonCloud=.34+.66*smoothstep(.20,.82,flowBands*.46+mesoCells*.32+fineGrain*.22);
    float ribbonSilk=.26+.74*smoothstep(.20,.84,flowBands*.54+fineGrain*.46);
    o.curvedBand=saturate((bandCore*.86+fineStreamer*.38*(1.0-.42*bandCore))*ribbonCloud);
    o.curvedWisp=saturate((bandCore*.68+fineStreamer*.66*(1.0-.32*bandCore))*ribbonSilk);

    // 2. Regime A: Parallel wind-aligned trade-wind cloud streets & fine marine Sc Rayleigh-Benard cellular lace
    // (true spherical LIC along windAxis -> genuine parallel trade streets!)
    float streetRipple=(Noise3(Turn(pFlow)*54.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow+windAxis*.012))*54.0+seed+5.0)+
                        Noise3(Turn(normalize(pFlow-windAxis*.012))*54.0+seed+5.0))*(1.0/3.0);
    float streetPuffs=Noise3(Turn(pFlow)*88.0+seed+13.0);
    o.tradeStreets=planetary*.32+synoptic*.22+streetRipple*.30+streetPuffs*.16;

    float cellClosed=1.0-abs(2.0*Noise3(Turn(pFlow)*46.0+seed)-1.0);
    float cellFine=1.0-abs(2.0*Noise3(Turn(pFlow)*88.0+seed+9.0)-1.0);
    float scMacro=planetary*.42+synoptic*.30;
    o.honeycomb=scMacro+pow(cellClosed,1.20)*.17+cellFine*.11;

    o.planetary=planetary;
    o.flowBands=flowBands;
    o.mesoCells=mesoCells;
    o.fineGrain=fineGrain;
    o.baseTex=planetary*.32+synoptic*.30+bandField1*.20+flowBands*.12+mesoCells*.04+fineGrain*.02;

    // 3. Regime B: Slender, sinuous curved ITCZ / SPCZ convective filaments studded with tight bright Cb hot-towers
    // + long downwind brushed-silk cirrus anvil plumes (NASA EPIC Pacific & ISS Borneo signature!)
    float3 qItcz=float3(pWave.x*2.1,(pWave.y+.06*(psi1.z))*lerp(2.2,7.8,eqWeight),pWave.z*2.1);
    float itczSlender=smoothstep(.48,.82,Fbm(qItcz+wander*.48+seed+float3(5.3,1.7,8.9)));
    float itczRibbon=(Noise3(Turn(pFlow)*12.5+wander*.7+seed+float3(5.3,1.7,8.9))+
                      Noise3(Turn(pFlowP)*12.5+wander*.7+seed+float3(5.3,1.7,8.9))+
                      Noise3(Turn(pFlowM)*12.5+wander*.7+seed+float3(5.3,1.7,8.9)))*(1.0/3.0);
    float hotTowers=smoothstep(.46,.82,mesoCells*.56+Noise3(Turn(pFlow)*78.0+seed+21.0)*.44);
    float mcsEnv=smoothstep(.34,.76,max(itczSlender*.86,planetary*.28+synoptic*.28+itczRibbon*.44));
    o.mcsCluster=mcsEnv*(.26+.74*hotTowers);
    float3 pAnvilNear=normalize(pFlow-jetAxis*.052);
    float3 pAnvilFar=normalize(pFlow-jetAxis*.135);
    float anvilNear=Fbm(pAnvilNear*5.4+wander*.7+seed);
    float anvilFar=Fbm(pAnvilFar*3.4+wander*.6+seed);
    float anvilSilk=(Noise3(Turn(pAnvilNear)*22.0+seed+4.0)+
                     Noise3(Turn(normalize(pAnvilNear+jetAxis*.018))*22.0+seed+4.0))*.5;
    o.anvilShield=smoothstep(.38,.76,anvilNear*.46+anvilFar*.32+anvilSilk*.22)*(.26+.74*mcsEnv);

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

    // Sweeping 4,800 km curved cold-front ribbon with true 3-tap spherical LIC fibrous shear streaks along the frontal axis
    float3 frontTangent=normalize(east*.66+north*hemiSign*.75+1e-5);
    float frontFilament=(Noise3(Turn(pFlow)*18.0+hSeed)+
                         Noise3(Turn(normalize(pFlow+frontTangent*.020))*18.0+hSeed)+
                         Noise3(Turn(normalize(pFlow-frontTangent*.020))*18.0+hSeed))*(1.0/3.0);

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

     // Five authentic NASA satellite morphological components:
     // Ordinary open-ocean & subtropical belts weave sweeping feathery curved cloud bands alongside convective clusters!
     float zBase=(baseTex-.502)*9.8;
     float zRibbon=(baseTex-.512)*6.2+4.85*(curvedBand-.22)+1.15*(curvedWisp-.18);
     float zStreets=(tradeStreets-.506)*9.6+2.65*(curvedBand-.20);
     float zHoney=(honeycomb-.532)*8.6+1.35*(curvedBand-.20);
     float zMcs=.44*zRibbon+.24*zStreets+2.15*(mcsCluster-.18);
     // Sweeping curved comma shields + coiled spiral dry-intrusion tongue + slender curved frontal bands
     float zFront=.28*zRibbon+4.95*(frontalLow-.17)-4.45*frontalDry;
     float stormWeight=smoothstep(.16,.62,stormTrack);

     float zLow=lerp(zBase,zRibbon,.78);
     zLow=lerp(zLow,zStreets,.34*tropBelt);
     zLow=lerp(zLow,zMcs,saturate(tropBelt*(.30+.70*itczConv)));
     zLow=lerp(zLow,zHoney,saturate(marineSc*.88));
     zLow=lerp(zLow,zFront,stormWeight);
     zLow+=.52*orogCrest*max(0.0,zBase+.35);
     float uLow=rcp(1.0+exp(-1.702*clamp(zLow,-6.0,6.0)));

     // Hierarchical ISCCP optical profile: keep crisp tattered cumulus & lacy silk edges on EVERY curved band
     // (only preserve dense continuous frontal cores — zero smooth cartoon pillow edges!)
     float sharpOrg=saturate(max(stormWeight*frontalLow,itczConv*mcsCluster));
     float frontPreserve=saturate(stormWeight*frontalLow*1.25);
     float rampCap=lerp(.35,.22,sharpOrg);
     float wLow=max(1e-4,min(rampCap,min(lowTarget,1.0-lowTarget)));
     float rawLow=smoothstep(1.0-lowTarget-wLow,1.0-lowTarget+wLow,uLow);
     float detailField=lerp(flowBands*.40+mesoCells*.36+fineGrain*.24,flowBands*.60+fineGrain*.24+mesoCells*.16,frontPreserve);
     float edgeBand=smoothstep(.01,.52,rawLow)*(1.0-smoothstep(.64,.96,rawLow));
     float erodeAmt=lerp(.58,.30,frontPreserve);
     float erodedEdge=saturate((rawLow-(1.0-detailField)*erodeAmt*edgeBand)/max(.24,1.0-.28*edgeBand));
     float veilToCore=pow(erodedEdge,lerp(1.26,1.08,frontPreserve))*(.70+.40*smoothstep(.26,.82,detailField));
     float postWakeLace=stormWeight*postFrontCells*smoothstep(.18,.58,climTarget)*.56;
     low=saturate(max(lerp(veilToCore,max(veilToCore,.93),smoothstep(.74,.96,rawLow)*(sharpOrg*.74+.26)),postWakeLace)+
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

     // Upper-tropospheric curved brushed-silk jet banners (stretched FBM + 3-tap spherical LIC along 250 hPa jetAxis!)
     float3 jetPsi=float3(
      Noise3(jetShear*2.1+wander*.35+float3(12,2,8)),
      Noise3(jetShear*2.1+wander*.35+float3(31,7,19)),
      Noise3(jetShear*2.1+wander*.35+float3(5,23,41)))-.5;
     float3 pJetWave=normalize(jetShear+cross(jetShear,jetPsi)*.32);
     float yJetStretch=lerp(2.0,7.6,smoothstep(.04,.42,length(radial.xz)));
     float3 qJetBand=float3(pJetWave.x*1.65,(pJetWave.y+.10*jetPsi.y)*yJetStretch,pJetWave.z*1.65);
     float jetH=Fbm(qJetBand+wander*.42+float3(12,2,8))*.54+wisps*.28+fineGrain*.18;
     float jetBanner=smoothstep(.50,.84,jetH)*smoothstep(.24,.80,wisps*.64+fineGrain*.36);
     float curvedCirrus=saturate(max(curvedWisp*.82,jetBanner));

     float zJet=(Fbm(upper*2.8+wander*.45+float3(12,2,8))-.51)*4.0+(wisps-.50)*4.2+4.35*(curvedCirrus-.24)+1.00*(uLow-.50);
     float zAnvil=2.55*(anvilShield-.20)+(wisps-.50)*3.8+3.10*(curvedCirrus-.24)+.95*(uLow-.45);
     float zWcb=.26*zJet+3.65*(wcbCirrus-.15)+2.45*(curvedCirrus-.24)+(wisps-.50)*2.4;
     float zHigh=lerp(zJet,zAnvil,saturate(itczConv*1.15));
     zHigh=lerp(zHigh,zWcb,stormWeight);
     float uHigh=rcp(1.0+exp(-1.702*clamp(zHigh,-6.0,6.0)));
     float wHigh=max(1e-4,min(.34,min(highTarget,1.0-highTarget)));
     float rawHigh=smoothstep(1.0-highTarget-wHigh,1.0-highTarget+wHigh,uHigh);
     // Carve feathery wind-combed silk edges & prune sub-visible <0.04 clear-sky haze (faster GPU empty-sky skip!)
     float highSilk=smoothstep(.16,.84,wisps*.62+fineGrain*.38);
     float highEdge=smoothstep(.02,.56,rawHigh);
     float highEroded=saturate((rawHigh-(1.0-highSilk)*.38*highEdge)/max(.35,1.0-.24*highEdge));
     high=smoothstep(.04,.96,highEroded)*(1.0-.68*tropBelt*leeShadow);
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
