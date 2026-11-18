Shader "ORBIS/FlatWorld/SkyBake"
{
 Properties
 {
  _ClimateTex("Monthly precipitation and temperature",2D)="black"{}
  _ClimateWindTex("Climate wind",2D)="black"{}
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
   #pragma target 3.0
   #include "UnityCG.cginc"
   #include "FlatCycloneWind.cginc"
   sampler2D _ClimateTex,_ClimateWindTex;
   float _VisualTime,_MonthBlend,_ClimateRows;
   float4 _CyclonePhase;
   float4 _ClimatePoleSouth,_ClimatePoleNorth;
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
   float4 frag(v2f_img i):SV_Target
   {
    float4 monthly=tex2D(_ClimateTex,i.uv);
    if(_ClimateRows>0)monthly=lerp(lerp(_ClimatePoleSouth,_ClimatePoleNorth,step(.5,i.uv.y)),monthly,smoothstep(0,1,min(i.uv.y,1-i.uv.y)*_ClimateRows));
    float wet=lerp(monthly.r,monthly.g,_MonthBlend);
    float longitude=(i.uv.x-.5)*6.283185307,latitude=(i.uv.y-.5)*3.141592654;
    float3 radial=float3(sin(longitude)*cos(latitude),sin(latitude),cos(longitude)*cos(latitude));
    // Rotate a sphere instead of cumulatively advecting a UV texture. Longitude
    // remains periodic and clouds cannot stretch into needles over long sessions.
    float4 wind=tex2D(_ClimateWindTex,i.uv);
    float2 flow=lerp(wind.rg,wind.ba,_MonthBlend);
    float3 east=float3(cos(longitude),0,-sin(longitude));
    float3 north=float3(-sin(latitude)*sin(longitude),cos(latitude),-sin(latitude)*cos(longitude));
    float3 velocity=(east*flow.x+north*flow.y)*(.014*smoothstep(0,.10,length(radial.xz)));
    velocity*=min(1,.025/max(.0001,length(velocity)));
    // Visual cloud evolution uses the existing seasonal wind and two smoothly
    // changing spherical noise fields. It has no running water-vapour solver.
    float era=floor(_VisualTime*.0125),phase=frac(_VisualTime*.0125);
    float blend=phase*phase*(3-2*phase);
    float3 p0=normalize(radial-velocity*phase*7);
    float3 p1=normalize(radial+velocity*(1-phase)*7);
    float3 seed0=float3(Hash3(float3(era,4,7)),Hash3(float3(era,11,3)),Hash3(float3(era,5,19)))*13;
    float3 seed1=float3(Hash3(float3(era+1,4,7)),Hash3(float3(era+1,11,3)),Hash3(float3(era+1,5,19)))*13;
    float3 wander=float3(sin(_VisualTime*.007),cos(_VisualTime*.009),sin(_VisualTime*.004))*.65;
    float3 distortion=float3(Noise3(radial*3.4+wander+17),Noise3(radial*3.4+wander+43),Noise3(radial*3.4+wander+71))-.5;
    float broad=lerp(Fbm(p0*4.6+distortion*1.55+wander+seed0),Fbm(p1*4.6+distortion*1.55+wander+seed1),blend);
    float cells=lerp(Noise3(Turn(p0)*15.5+distortion+wander+seed0),Noise3(Turn(p1)*15.5+distortion+wander+seed1),blend);
    float threshold=lerp(.56,.35,wet);
    float low=smoothstep(threshold-.06,threshold+.25,broad*.76+cells*.24);
    float storm=smoothstep(.27,.78,wet)*smoothstep(.62,.96,low);
    // Upper-level flow carries long ice filaments even above gaps in the low deck.
    float3 upper=RotateY(radial,-_VisualTime*.0018);
    float envelope=smoothstep(.33,.67,Fbm(upper*4.3+wander*.5+float3(12,2,8)));
    float3 streakAxis=normalize(float3(.84,.25,.48));
    float3 crossAxis=normalize(float3(-.48,.12,.86));
    float3 shear=upper+distortion*.038;
    float ribbonA=Noise3((shear-streakAxis*dot(shear,streakAxis)*.94)*35+float3(9,4,2));
    float ribbonB=Noise3((shear-crossAxis*dot(shear,crossAxis)*.92)*42+float3(3,2,7));
    float align=.5+.5*(abs(dot(east*flow.x+north*flow.y,streakAxis))-abs(dot(east*flow.x+north*flow.y,crossAxis)))*smoothstep(0,.16,length(radial.xz));
    float wisps=lerp(ribbonB,ribbonA,smoothstep(.30,.70,align));
    float high=envelope*(.14+.70*smoothstep(.31,.78,wisps))*lerp(.65,1,wet);
    float4 weather=float4(low,high,saturate(.16+wet*.32+storm*.52),storm);
    Cyclone(radial,_CycloneNorth,1,_CyclonePhase.x,flow,weather);
    Cyclone(radial,_CycloneSouth,-1,_CyclonePhase.y,flow,weather);
    return weather;
   }
   ENDCG
  }
 }
 Fallback Off
}
