Shader "ORBIS/FlatWorld/SkyBake"
{
 Properties
 {
  _ClimateTex("Monthly precipitation and temperature",2D)="black"{}
  _MoistureTex("Column moisture",2D)="black"{}
  _ClimateWindTex("Climate wind",2D)="black"{}
  _LiveCloudTex("Evolving cloud mass",2D)="black"{}
  _LiveWindTex("Evolving wind",2D)="black"{}
  _LiveEffectTex("Precipitation",2D)="black"{}
  _MoistureReady("Moisture ready",Float)=0
  _DynamicReady("Weather ready",Float)=0
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
   sampler2D _ClimateTex,_MoistureTex,_ClimateWindTex,_LiveCloudTex,_LiveWindTex,_LiveEffectTex;
   float _MoistureReady,_DynamicReady;
   float _VisualTime,_MonthBlend,_ClimateRows;
   float4 _CycloneNorth,_CycloneSouth;
   float4 _ClimatePoleSouth,_ClimatePoleNorth;
   float Hash3(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
   float Noise3(float3 p)
   {
    float3 b=floor(p),f=frac(p);f=f*f*(3-2*f);
    float a=lerp(Hash3(b),Hash3(b+float3(1,0,0)),f.x);
    float c=lerp(Hash3(b+float3(0,1,0)),Hash3(b+float3(1,1,0)),f.x);
    float d=lerp(Hash3(b+float3(0,0,1)),Hash3(b+float3(1,0,1)),f.x);
    float e=lerp(Hash3(b+float3(0,1,1)),Hash3(b+float3(1,1,1)),f.x);
    return lerp(lerp(a,c,f.y),lerp(d,e,f.y),f.z);
   }
   float Fbm(float3 p)
   {
    // Fixed, small octaves, on a low-resolution cache only; not per terrain pixel.
    float a=Noise3(p)*.54;
    a+=Noise3(p*2.03+float3(17,3,11))*.28;
    a+=Noise3(p*4.07+float3(4,29,5))*.13;
    return a+Noise3(p*8.13+float3(8,6,13))*.05;
   }
   float3 RotateY(float3 p,float angle)
   {float s=sin(angle),c=cos(angle);return float3(c*p.x+s*p.z,p.y,-s*p.x+c*p.z);}
   float2 Cyclone(float3 p,float4 centre,float spin)
   {
    if(centre.w<.16)return 0;
    float3 axis=normalize(centre.xyz);
    float radius=acos(clamp(dot(p,axis),-1,1));
    if(radius>.36)return 0;
    float3 east=normalize(cross(float3(0,1,0),axis));
    float3 north=cross(axis,east);
    float angle=atan2(dot(p,east),dot(p,north));
    float arms=.5+.5*sin(angle*2+radius*43+spin*_VisualTime*.007);
    float band=smoothstep(.033,.075,radius)*(1-smoothstep(.25,.35,radius));
    float eyewall=exp(-pow((radius-.055)/.018,2));
    float eye=1-smoothstep(.025,.048,radius);
    return float2(centre.w*(band*(.16+.75*pow(arms,4))+eyewall*.36),centre.w*eye);
   }
   float4 frag(v2f_img i):SV_Target
   {
    float4 monthly=tex2D(_ClimateTex,i.uv);
    if(_ClimateRows>0)monthly=lerp(lerp(_ClimatePoleSouth,_ClimatePoleNorth,step(.5,i.uv.y)),monthly,smoothstep(0,1,min(i.uv.y,1-i.uv.y)*_ClimateRows));
    float wet=lerp(monthly.r,monthly.g,_MonthBlend),temperature=lerp(monthly.b,monthly.a,_MonthBlend);
    float longitude=(i.uv.x-.5)*6.283185307,latitude=(i.uv.y-.5)*3.141592654;
    float3 radial=float3(sin(longitude)*cos(latitude),sin(latitude),cos(longitude)*cos(latitude));
    // Rotate a sphere instead of cumulatively advecting a UV texture. Longitude
    // remains periodic and clouds cannot stretch into needles over long sessions.
    float4 moisture=tex2D(_MoistureTex,i.uv),wind=tex2D(_ClimateWindTex,i.uv);
    float humidity=lerp(moisture.r,moisture.g,_MonthBlend);
    float condensation=lerp(moisture.b,moisture.a,_MonthBlend);
    float2 flow=lerp(wind.rg,wind.ba,_MonthBlend);
    float3 east=float3(cos(longitude),0,-sin(longitude));
    float3 north=float3(-sin(latitude)*sin(longitude),cos(latitude),-sin(latitude)*cos(longitude));
    float3 velocity=east*flow.x+north*flow.y;
    float3 liveVelocity=tex2D(_LiveWindTex,i.uv).xyz;
    // Evolve two successive noise fields while backtracing through live wind.
    // Blending the *coordinates* here cancels the displacement at mid-cycle;
    // blend field values instead, with matching endpoints across eras.
    float era=floor(_VisualTime*.0125),phase=frac(_VisualTime*.0125);
    float blend=phase*phase*(3-2*phase);
    float3 p0=normalize(radial-liveVelocity*phase*7);
    float3 p1=normalize(radial+liveVelocity*(1-phase)*7);
    float3 seed0=float3(Hash3(float3(era,4,7)),Hash3(float3(era,11,3)),Hash3(float3(era,5,19)))*13;
    float3 seed1=float3(Hash3(float3(era+1,4,7)),Hash3(float3(era+1,11,3)),Hash3(float3(era+1,5,19)))*13;
    float3 p=p0;
    float3 wander=float3(sin(_VisualTime*.007),cos(_VisualTime*.009),sin(_VisualTime*.004))*.65;
    float3 distortion=float3(Noise3(p*4+17),Noise3(p*4+43),Noise3(p*4+71))-.5;
    float broad=lerp(Fbm(p0*5.5+distortion*2+wander+seed0),Fbm(p1*5.5+distortion*2+wander+seed1),blend);
    float warp=Noise3(p*2.7+float3(8,21,4));
    float cells=lerp(Noise3(p0*(23+warp*5)+wander*.7+seed0),Noise3(p1*(23+warp*5)+wander*.7+seed1),blend);
    float available=lerp(saturate(wet*.85+.10),saturate((humidity-.42)*1.8+condensation*.40),_MoistureReady);
    float threshold=lerp(.78,.40,available);
    float low=smoothstep(threshold-.04,threshold+.10,broad*.62+cells*.38);
    float erosion=Noise3(p*72+wander);
    low=saturate(low*(.78+erosion*.28))*smoothstep(.04,.20,available);
    // Climatological warm, wet ocean candidates come from ORBIS's monthly
    // temperature/precipitation fields. Spherical bands have no date-line seam.
    float2 cycloneN=Cyclone(radial,_CycloneNorth,1);
    float2 cycloneS=Cyclone(radial,_CycloneSouth,-1);
    float cyclone=max(cycloneN.x,cycloneS.x)*(1-_DynamicReady);
    low=max(low*(1-max(cycloneN.y,cycloneS.y)*.88),cyclone*saturate((wet-.08)*4));
    float3 upper=RotateY(radial,-_VisualTime*.0035);
    float highNoise=Fbm(upper*float3(25,10,25)+wander*.4+float3(13,7,2));
    float high=max(smoothstep(.58,.75,highNoise)*(.60*available),cyclone*.14);
    float brightness=saturate(.5+cells*.45+erosion*.12-low*.22);
    float storm=max(smoothstep(.12,.60,lerp(wet,condensation,_MoistureReady))*smoothstep(.62,.9,low)*smoothstep(.28,.65,temperature),cyclone*.85);
    float cloudType=saturate(.28+condensation*.8+storm*.35);
    float4 live=tex2D(_LiveCloudTex,i.uv);
    float4 effects=tex2D(_LiveEffectTex,i.uv);
    // Weather determines how much condensate exists; multi-scale shape noise
    // carves holes and textured edges within that evolving cloud mass.
    float ragged=smoothstep(.38,.60,broad*.62+cells*.38);
    low=lerp(low,saturate(live.r*(.65+ragged*.35)*(.88+erosion*.12)),_DynamicReady);
    high=lerp(high,saturate(live.g*(.45+.55*smoothstep(.48,.64,highNoise))),_DynamicReady);
    cloudType=lerp(cloudType,live.b,_DynamicReady);
    storm=lerp(storm,effects.r,_DynamicReady);
    return float4(low,high,cloudType,storm);
   }
   ENDCG
  }
 }
 Fallback Off
}
