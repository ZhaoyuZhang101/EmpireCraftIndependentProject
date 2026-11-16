Shader "Hidden/ORBIS/HPCloudColumn"
{
 Properties
 {
  _ScrollWeatherTex("Cloud mass, height and precipitation",2D)="black"{}
  _ScrollHeightTex("Ground height",2D)="black"{}
  _ScrollCloudWindTex("Cloud wind",2D)="black"{}
  _ClimateTex("Monthly precipitation and temperature",2D)="black"{}
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
 }
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_SHARED_SCROLL_WEATHER
 #include "../OrbisCloudCommon.cginc"
 sampler2D _ClimateTex;
 float _MonthBlend;
 float4 _ColumnUVTransform;
 float4 frag(v2f_img i):SV_Target
 {
  i.uv=i.uv*_ColumnUVTransform.zw+_ColumnUVTransform.xy;
  float lon=(i.uv.x-.5)*6.283185307,lat=(i.uv.y-.5)*3.141592654;
  float3 up=float3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));
  float2 tau=0;
  // Radial HP density integral cached at 5Hz, preserving map readability.
  [loop] for(int k=0;k<8;k++)
  {
   float metres=400+(k+.5)*1637.5;
   tau+=ScrollClimateDensities(up*(1+metres*_CloudHeightScale),false)*1637.5*_CloudHeightScale*ORBIS_EXTINCTION;
  }
  float2 opacity=1-exp(-tau);
  float4 weather=tex2Dlod(_ScrollWeatherTex,float4(i.uv,0,0));
  // Precipitation is visible only under deep, optically thick cloud columns.
  // A nonzero coarse weather rate alone must not paint rain into clear gaps.
  float4 climate=tex2Dlod(_ClimateTex,float4(i.uv,0,0));
  float temperature=lerp(climate.b,climate.a,_MonthBlend)*65-25;
  float frozen=1-smoothstep(-1,2,temperature);
  float rain=weather.a*(1-frozen)*smoothstep(.50,.78,weather.b)*smoothstep(1.3,3.2,tau.x);
  float snow=weather.a*frozen*smoothstep(.7,2.0,tau.x+tau.y);
  return float4(opacity,snow,rain);
 }
 ENDCG
 }}
}
