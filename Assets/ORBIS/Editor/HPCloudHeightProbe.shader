Shader "Hidden/ORBIS/HPCloudHeightProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_SHARED_SCROLL_WEATHER
 #include "../Resources/ORBIS/OrbisCloudCommon.cginc"
 float4 frag(v2f_img i):SV_Target
 {
  float2 d=OrbisCloudDensities(float3(0,0,1+i.uv.x*.20),false);
  return float4(d,ORBIS_CLOUD_TOP,1);
 }
 ENDCG
 }}
}
