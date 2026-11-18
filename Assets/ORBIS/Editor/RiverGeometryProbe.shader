Shader "Hidden/ORBIS/RiverGeometryProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_FLAT_NO_SWE
 #include "../Resources/ORBIS/FlatWorld/Water/FlatWater.cginc"
 float2 _ProbeOrigin;
 float _ProbeMouth;
 float4 frag(v2f_img i):SV_Target
 {
  float2 cell=_ProbeOrigin+i.uv*2,uv=(floor(cell)+.5)*_MapSize.zw;
  float4 h=tex2Dlod(_HydroA,float4(uv,0,0));
  float4 mixing=tex2Dlod(_HydroB,float4(uv,0,0));
  if(_ProbeMouth>.5)
  {
   float4 reach=FlatRiverReceiver(cell,uv,h,mixing,FlatField(cell));
   return float4(reach.r,i.uv,dot(reach.ba,float2(1,1)));
  }
  float2 tangent;float d=FlatRiverDistance(frac(cell),h,mixing.a,mixing.r,uv,tangent);
  return float4(d,i.uv,h.a);
 }
 ENDCG
 }}
}
