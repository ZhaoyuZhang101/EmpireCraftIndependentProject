Shader "Hidden/ORBIS/HPCloudColumn"
{
 Properties
 {
  _ScrollWeatherTex("Cloud mass, height and precipitation",2D)="black"{}
  _ScrollHeightTex("Ground height",2D)="black"{}
  _ScrollCloudWindTex("Cloud wind",2D)="black"{}
  [HideInInspector] _ScrollUpperWindTex("Upper cloud wind",2D)="black"{}
  [HideInInspector] _ScrollUpperWindReady("Upper cloud wind ready",Float)=0
  [HideInInspector] _ScrollMonthBlend("Upper cloud wind month blend",Float)=0
  _ClimateTex("Monthly precipitation and temperature",2D)="black"{}
  [HideInInspector] _RegionalPrecipDiagTex("Regional 6h precip diagnostic (kg/m2/6h)",2D)="black"{}
  [HideInInspector] _RegionalPrecipPoles("Regional 6h precip pole means",Vector)=(0,0,0,0)
  [HideInInspector] _RegionalPrecipReady("Use RegionalPrecipDiagTex surface fallout",Float)=0
  _MonthBlend("Seasonal blend",Float)=0
  _CloudNoise("Cloud shape",3D)="white"{}
  _CloudDetailNoise("Cloud erosion",3D)="white"{}
  _HPHighDetail("High clouds",2D)="black"{}
  _Density("Density",Float)=1
  _Cover("Coverage",Float)=1
  _CloudHeightScale("Height scale",Float)=0.0000015
  _ScrollHeightScale("Ground scale",Float)=0
  _ScrollSourceSize("Source size",Vector)=(512,256,0,0)
  _ColumnUVTransform("Slice offset and scale",Vector)=(0,0,1,1)
  [HideInInspector] _OrbisCycloneNorth("Staged northern cyclone",Vector)=(0,0,0,0)
  [HideInInspector] _OrbisCycloneSouth("Staged southern cyclone",Vector)=(0,0,0,0)
  [HideInInspector] _OrbisCyclonePhase("Staged cyclone phase",Vector)=(0,0,0,0)
 }
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_SHARED_SCROLL_WEATHER
 #include "../OrbisCloudCommon.cginc"
 UNITY_DECLARE_TEX2D(_ClimateTex);
 UNITY_DECLARE_TEX2D_NOSAMPLER(_RegionalPrecipDiagTex);
 float4 _RegionalPrecipDiagTex_TexelSize;
 float4 _RegionalPrecipPoles;
 float _RegionalPrecipReady;
 float _MonthBlend;
 float4 _ColumnUVTransform;
 float4 frag(v2f_img i):SV_Target
 {
  i.uv=i.uv*_ColumnUVTransform.zw+_ColumnUVTransform.xy;
  float lon=(i.uv.x-.5)*6.283185307,lat=(i.uv.y-.5)*3.141592654;
  float3 up=float3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));
  float2 tau=0;
  // Radial HP density integral cached at 5Hz, preserving map readability and ground cloud-shadow sync.
  // Pass the macro orbital column angular footprint (matching orbital Clouds.shader) rather than 0.0,
  // avoiding close-up 3.4x cuLwcBoost tau inflation across the global column cache.
  const float colFootprint=0.0018;
  [loop] for(int k=0;k<8;k++)
  {
   float metres=400+(k+.5)*1637.5;
   tau+=ScrollClimateDensitiesMip(up*(1+metres*_CloudHeightScale),false,colFootprint,0.0)*1637.5*_CloudHeightScale*ORBIS_EXTINCTION;
  }
  float2 opacity=1-exp(-tau);
  float4 weather=tex2Dlod(_ScrollWeatherTex,float4(i.uv,0,0));
  // Precipitation is visible only under deep, optically thick cloud columns.
  // A nonzero coarse weather rate alone must not paint rain into clear gaps.
  #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
  float4 climate=UNITY_SAMPLE_TEX2D_LOD(_ClimateTex,i.uv,0);
  #else
  float4 climate=tex2Dlod(_ClimateTex,float4(i.uv,0,0));
  #endif
  float temperature=lerp(climate.b,climate.a,_MonthBlend)*65-25;
  float frozen=1-smoothstep(-1,2,temperature);
  float rain,snow;
  if(_RegionalPrecipReady>.5)
  {
   // Carbon 64x32 RGBAHalf diagnostic: R=in-cloud fallout, G=surface-reaching precip (kg/m2/6h = mm/6h),
   // B=high-ice fallout share, A=cloud water; _RegionalPrecipPoles=(southR, southG, northR, northG).
   // Share _ClimateTex's sampler to stay well within D3D11 single-pass sampler limits.
   #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
   float4 regPrecip=UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_RegionalPrecipDiagTex,_ClimateTex,i.uv,0);
   #else
   float4 regPrecip=tex2Dlod(_RegionalPrecipDiagTex,float4(i.uv,0,0));
   #endif
   float poleRows=max(32.0,_RegionalPrecipDiagTex_TexelSize.w);
   float poleW=smoothstep(0,.5,min(i.uv.y,1-i.uv.y)*poleRows);
   float poleSfc=lerp(_RegionalPrecipPoles.y,_RegionalPrecipPoles.w,step(.5,i.uv.y));
   float sfcPrecipMm6h=max(0.0,lerp(poleSfc,regPrecip.g,poleW));
   // Strict zero surface rain/snow when G<=0 (including virga R>0, G==0).
   // Monotonic Beer-Lambert saturation maps mm/6h into relative visual intensity [0,1]
   // (e-folding 2.4 mm/6h liquid rain; 0.9 mm/6h SWE cold snow).
   float rainSignal=sfcPrecipMm6h>0.0?(1.0-exp(-sfcPrecipMm6h/2.4)):0.0;
   float snowSignal=sfcPrecipMm6h>0.0?(1.0-exp(-sfcPrecipMm6h/0.9)):0.0;
   // Gate strictly by actual radial cloud optical thickness (zero clear-sky rain/snow),
   // without letting legacy convective storm weather.a / weather.b suppress valid frontal rain.
   float rainColGate=smoothstep(.55,1.95,tau.x);
   float snowColGate=smoothstep(.50,1.75,tau.x+tau.y);
   rain=rainSignal*(1-frozen)*rainColGate;
   snow=snowSignal*frozen*snowColGate;
  }
  else
  {
   rain=weather.a*(1-frozen)*smoothstep(.50,.78,weather.b)*smoothstep(1.3,3.2,tau.x);
   snow=weather.a*frozen*smoothstep(.7,2.0,tau.x+tau.y);
  }
  return float4(opacity,snow,rain);
 }
 ENDCG
 }}
}
