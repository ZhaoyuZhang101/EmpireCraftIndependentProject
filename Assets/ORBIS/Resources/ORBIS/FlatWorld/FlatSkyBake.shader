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
    if(radius>=1.24)return;
    float3 east=normalize(cross(float3(0,1,0),axis));
    float3 north=cross(axis,east);
    float2 local=float2(dot(p,east),dot(p,north))/extent;
    float bearing=atan2(local.y,local.x+1e-7);
    float organized=smoothstep(.18,.72,centre.w);
    float2 downwind=steering*rsqrt(max(.04,dot(steering,steering)));
    float bearingInStorm=bearing-rotation;
    float2 body=float2(cos(bearingInStorm),sin(bearingInStorm))*radius;
    float3 seed=axis*3.1+float3(7,19,3);
    float distort=Noise3(float3(body*3.5,1)+seed)-.5;
    float r=radius*(1+.20*distort)-.09*dot(local,downwind);
    // Inverse advection winds convective cells into trailing rainbands. Phase is
    // the integral of the simulated vortex wind, including its hemisphere sign.
    float turn=lerp(.82,3.25,organized)*log(1+4.7*max(r,0));
    float phase=bearingInStorm+spin*turn+.83*distort;
    float2 flow=float2(cos(phase),sin(phase))*r;
    float cells=Fbm(float3(flow*5.7,1)+seed);
    float billows=Noise3(float3(body*19,3)+seed);
    float mainArm=smoothstep(.02,.93,cos(phase+1.55*(cells-.5)));
    float feeder=smoothstep(.26,.96,cos(phase+2.53+.48*sin(5.6*r)+2.4*(cells-.5)));
    feeder*=smoothstep(.20,.72,cells)*(.91-.36*smoothstep(.25,.86,r));
    float squalls=smoothstep(.46,.78,cells*.70+billows*.30);
    float bands=max(mainArm*lerp(.48,1.02,cells),feeder)+.21*squalls;
    float wall=(1-smoothstep(.12,.33,r))*(.81+.18*billows);
    float maturity=smoothstep(.58,.84,centre.w);
    float eye=lerp(1,smoothstep(.019,.071,r+.012*distort),maturity);
    float shield=1-smoothstep(.43,.97,r);
    float bodyCover=lerp(saturate(cells*.8+.28*billows),max(wall,bands),organized);
    float low=eye*saturate(.035+bodyCover*(.82+.22*billows));
    float convection=eye*saturate(bodyCover*(.36+.82*centre.w));
    float boundary=1-smoothstep(.61,1.19,r+.13*distort);
    float influence=boundary*smoothstep(.035,.48,centre.w);
    // Clear the dry slots as well as the mature eye. A cirrus canopy shears
    // downwind and extends past the lower rainbands without producing rainfall.
    float icePhase=phase-spin*(1.03+.43*r)+2.7*(cells-.5);
    float filaments=pow(.5+.5*cos(3*icePhase),3);
    float upper=eye*(.07+.36*filaments+.31*shield)*(1+.27*dot(local,downwind));
    weather=lerp(weather,float4(low,upper,.17+.83*convection,smoothstep(.46,.86,convection)),influence);
   }
   struct EraMorphology
   {
    float baseTex,honeycomb,mcsCluster,anvilShield,frontalLow,frontalDry,wcbCirrus;
   };
   EraMorphology SampleEraMorphology(
    float eraId,float age,float3 radial,float longitude,float latitude,
    float3 velocity,float3 windAxis,float windSpd,float polarLatRad,float3 jetAxis,float3 wander)
   {
    EraMorphology o;
    float3 p=normalize(radial-velocity*age*7.2);
    float3 seed=float3(Hash3(float3(eraId,4,7)),Hash3(float3(eraId,11,3)),Hash3(float3(eraId,5,19)))*15.0;
    float3 curl=float3(
     Noise3(p*4.8+wander+seed+17.0),
     Noise3(p*4.8+wander+seed+43.0),
     Noise3(p*4.8+wander+seed+71.0))-.5;

    // Anisotropic wind-sheared coordinate organizes clouds into realistic cloud streets & bands
    float tropTaper=smoothstep(.12,.38,abs(p.y));
    float3 pStreak=p-windAxis*dot(p,windAxis)*(lerp(.36,.58,tropTaper)*windSpd);
    float synoptic=Fbm(p*6.4+curl*1.20+wander+seed);
    float streets=Fbm(Turn(pStreak)*16.5+curl*.85+wander+seed);
    float cumulus=Noise3(Turn(p)*33.0+curl*.60+wander+seed);
    o.baseTex=synoptic*.50+streets*.32+cumulus*.18;
    o.honeycomb=synoptic*.46+(1.0-abs(2.0*Noise3(Turn(p)*25.0+curl*.48+seed)-1.0))*.54;

    // ITCZ mesoscale convective clusters (MCS) with clear radiative-convective subsidence moats
    // so tropical clouds form discrete cellular clusters rather than a giant zonal ribbon.
    float mcsEnv=smoothstep(.47,.81,synoptic*.52+streets*.48);
    o.mcsCluster=mcsEnv*(.24+.76*smoothstep(.42,.80,cumulus));
    float3 pAnvil=normalize(p-jetAxis*.046);
    o.anvilShield=smoothstep(.47,.83,Fbm(pAnvil*8.8+curl*.95+wander+seed))*(.40+.60*mcsEnv);

    // Mid-latitude baroclinic Rossby wave comma cyclones (exact integer harmonics -> zero dateline seam)
    float eraLon=6.2831853*Hash3(float3(eraId,9,2));
    float theta=5.0*longitude-1.10*age+eraLon+.40*sin(2.0*longitude+seed.x);
    float waveS=sin(theta),waveC=cos(theta);
    float waveFade=smoothstep(-.68,-.15,waveC);
    float dx=atan2(waveS,waveC)/5.0*max(.22,cos(latitude));
    float dy=(abs(latitude)-polarLatRad-.062*sin(3.0*longitude-.55*age+seed.y));

    // Comma head vortex wrapping cyclonically poleward + trailing cold front + dry-slot intrusion
    float2 vortVec=float2(dx,dy-.032);
    float rVort=length(vortVec)/.21;
    float vortAng=atan2(vortVec.y,vortVec.x+1e-6)+2.35*log(1.0+4.8*rVort)+1.10*(synoptic-.5);
    float commaHead=(1.0-smoothstep(.30,1.02,rVort))*smoothstep(-.16,.78,cos(vortAng))*smoothstep(.04,.20,rVort);

    float dxFront=.56*dy-.48*dy*dy;
    float dFront=dx-dxFront+.034*(streets-.5);
    float coldFront=exp(-pow(dFront/.035,2.0))*smoothstep(-.36,-.20,dy)*(1.0-smoothstep(.02,.13,dy))*(.58+.52*streets);
    float wcb=smoothstep(-.01,.09,dFront)*(1.0-smoothstep(.09,.24,dFront))*smoothstep(-.16,.14,dy)*(1.0-smoothstep(.14,.28,dy));
    float drySlot=exp(-pow((dFront+.056)/.042,2.0))*smoothstep(-.26,-.03,dy)*(1.0-smoothstep(.06,.18,dy));
    float coldCells=smoothstep(.08,.22,-dFront)*(1.0-smoothstep(.22,.36,-dFront))*smoothstep(-.30,.10,dy)*pow(cumulus,1.85)*.56;

    o.frontalLow=waveFade*saturate(max(commaHead*(.74+.34*streets),coldFront)+.44*wcb*synoptic+coldCells);
    o.frontalDry=waveFade*drySlot;
    o.wcbCirrus=waveFade*saturate(wcb*(.62+.48*synoptic)+commaHead*.65);
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
    // Rotate a sphere instead of cumulatively advecting a UV texture. Longitude
    // remains periodic and clouds cannot stretch into needles over long sessions.
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

    // Continuous two-era spherical advection (exact zero-jump handover at every 80s era boundary)
    float era=floor(_VisualTime*.0125),phase=frac(_VisualTime*.0125);
    float blend=phase*phase*(3-2*phase);
    float3 wander=float3(sin(_VisualTime*.007),cos(_VisualTime*.009),sin(_VisualTime*.004))*.62;
    EraMorphology m0=SampleEraMorphology(era,phase,radial,longitude,latitude,velocity,windAxis,windSpd,polarLatRad,jetAxis,wander);
    EraMorphology m1=SampleEraMorphology(era+1.0,phase-1.0,radial,longitude,latitude,velocity,windAxis,windSpd,polarLatRad,jetAxis,wander);

    float baseTex=lerp(m0.baseTex,m1.baseTex,blend);
    float honeycomb=lerp(m0.honeycomb,m1.honeycomb,blend);
    float mcsCluster=lerp(m0.mcsCluster,m1.mcsCluster,blend);
    float anvilShield=lerp(m0.anvilShield,m1.anvilShield,blend);
    float frontalLow=lerp(m0.frontalLow,m1.frontalLow,blend);
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

     // Physical condensation threshold suppressed over STHZ highs, deserts, foehn rain shadows &
     // Walker/equatorial Ekman subsidence clear slots (carried in leeShadow) so _ClimateTex's
     // zonal ocean rain cannot paint an unbroken 360-degree equatorial stratus band in any season.
     float effWet=wet*(1.0-.72*leeShadow);
     float support=saturate(.34*effWet+.36*synLift+.26*moistEnv-.48*leeShadow+.30*orogCrest);
     float threshold=lerp(.75,.33,support)-.088*response.x*(1.0-.62*leeShadow);
     float tropCellular=lerp(1.0,.36+.64*smoothstep(.36,.76,mcsCluster+.42*baseTex),tropBelt*(1.0-orogCrest));
     float airmassLow=smoothstep(threshold-.04,threshold+.23,baseTex)*tropCellular*(1.0-.58*tropBelt*leeShadow);
     // Baroclinic dry slots clear airmass clouds behind cold fronts while comma spirals & fronts build
     airmassLow*=1.0-.78*stormTrack*frontalDry;

     float frontalCloud=stormTrack*frontalLow*smoothstep(.16,.62,moistEnv+.45*effWet);
     float itczCloud=itczConv*mcsCluster*1.06;
     float scCloud=marineSc*smoothstep(.42,.76,honeycomb)*lerp(.78,.48,tropBelt);
     float orogCloud=orogCrest*smoothstep(.34,.72,baseTex)*.76;

     low=saturate(max(max(airmassLow,frontalCloud),max(itczCloud,max(scCloud,orogCloud))));
     // Ensure regional atmospheric observations modulate existing cloud decks bidirectionally
     low=saturate(low+.05*response.x*smoothstep(.01,.15,low+.15*support));

     float scShare=saturate(max(scCloud-max(airmassLow,max(frontalCloud,itczCloud))*.85,marineSc*.72-max(frontalCloud,itczCloud)*.95));
     float deepShare=saturate(max(itczCloud,frontalCloud*.82));
     // Require organized deep convective/frontal/orographic forcing for thunderheads & lightning;
     // ordinary humid trade-wind cumulus & equatorial cold-tongue Sc remain shallow (<2.5 km) without lightning.
     float deepTrigger=smoothstep(.16,.66,deepShare+.32*synLift*orogCrest+.22*synLift*smoothstep(.55,.90,effWet));
     storm=saturate(smoothstep(.22,.76,effWet)*smoothstep(.58,.94,low)*deepTrigger*(1+.35*response.y)*(1.0-.90*scShare));
     convection=lerp(saturate(.14+effWet*.18+deepShare*.46+storm*.42),0.08,scShare);

     // Upper-tropospheric Rossby jet streaks, frontal warm-conveyor shields & ITCZ anvil canopies:
     // Taper Rossby jet cirrus out of the equatorial Walker belt so high clouds on 0 deg come only
     // from active MCS anvil plumes instead of bridging clear ocean basins into a continuous ring.
     float3 upper=RotateY(radial,-_VisualTime*.0018);
     float3 crossJet=normalize(cross(radial,jetAxis)+float3(1e-5,0,0));
     float3 jetShear=upper+wander*.04;
     float jetStreak=Noise3((jetShear-jetAxis*dot(jetShear,jetAxis)*.92)*36.0+float3(9,4,2));
     float crossStreak=Noise3((jetShear-crossJet*dot(jetShear,crossJet)*.90)*42.0+float3(3,2,7));
     float poleTaper=smoothstep(0,.16,length(radial.xz));
     float wisps=lerp(crossStreak,jetStreak,.25+.70*poleTaper);
     float envJet=smoothstep(.34,.68,Fbm(upper*4.8+wander*.5+float3(12,2,8)));
     float eqJetTaper=lerp(.20,1.0,smoothstep(.10,.34,abs(radial.y)))*(1.0-.72*leeShadow);
     float jetCirrus=envJet*eqJetTaper*(.10+.62*smoothstep(.32,.78,wisps))*lerp(.35,1.0,saturate(.55*moistEnv+.45*effWet));
     float anvilCirrus=itczConv*anvilShield*(.45+.55*smoothstep(.25,.75,wisps));
     float frontCirrus=stormTrack*wcbCirrus*(.50+.50*smoothstep(.28,.76,wisps));
     high=saturate(max(jetCirrus,max(anvilCirrus,frontCirrus)));
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
