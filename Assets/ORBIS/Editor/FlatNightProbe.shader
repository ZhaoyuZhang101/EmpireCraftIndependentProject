Shader "Hidden/ORBIS/FlatNightProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSkyComposite.cginc"
 float4 frag(v2f_img i):SV_Target
 {
  float3 color=FlatColor(float3(.45,.40,.30))*FlatDayLight(float3(0,0,1),float2(.5,.5));
  #ifdef UNITY_COLORSPACE_GAMMA
  color=GammaToLinearSpace(color);
  #endif
  return float4(color,1);
 }
 ENDCG
 }}
}
