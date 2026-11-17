Shader "Hidden/ORBIS/CloudPassageProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.5
 #pragma vertex vert_img
 #pragma fragment frag
 #define ORBIS_SHARED_SCROLL_WEATHER 1
 #include "../Resources/ORBIS/OrbisAtmosphereCommon.cginc"
 float _ProbeLightning;
 float3 _ProbePosition;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeLightning>.5)return float4(OrbisLightningLight(_ProbePosition),1);
  OrbisVolumeResult result=OrbisMarchMap(i.uv);return float4(result.transmission,1);
 }
 ENDCG
 }}
}
