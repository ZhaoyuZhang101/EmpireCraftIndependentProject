Shader "Hidden/ORBIS/HPCloudColumn"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_SHARED_SCROLL_WEATHER
 #include "../OrbisCloudCommon.cginc"
 float4 frag(v2f_img i):SV_Target
 {
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
  float rain=tex2Dlod(_ScrollWeatherTex,float4(i.uv,0,0)).a;
  return float4(opacity,exp(-tau.x*.20),rain);
 }
 ENDCG
 }}
}
