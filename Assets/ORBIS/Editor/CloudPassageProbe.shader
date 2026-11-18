Shader "Hidden/ORBIS/CloudPassageProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.5
 #pragma vertex vert_img
 #pragma fragment frag
 #define ORBIS_SHARED_SCROLL_WEATHER 1
 #include "../Resources/ORBIS/OrbisAtmosphereCommon.cginc"
 float _ProbeLightning,_ProbeProjection;
 float3 _ProbePosition,_ProbeRawDepths;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeProjection>.5)
  {
   float raw=i.uv.y<1.0/3.0?_ProbeRawDepths.x:(i.uv.y<2.0/3.0?_ProbeRawDepths.y:_ProbeRawDepths.z);
   float eye=OrbisEyeDepth(raw);
   return float4(OrbisViewOrigin(i.uv)+eye*OrbisViewRay(i.uv),eye);
  }
  if(_ProbeLightning>.5)return float4(OrbisLightningLight(_ProbePosition),1);
  OrbisVolumeResult result=OrbisMarchMap(i.uv);return float4(result.transmission,1);
 }
 ENDCG
 }}
}
