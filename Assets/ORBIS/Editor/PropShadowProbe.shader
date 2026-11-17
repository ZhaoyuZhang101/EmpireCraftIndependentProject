Shader "Hidden/ORBIS/PropShadowProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_FLAT_NO_SWE
 #include "../Resources/ORBIS/FlatWorld/Water/FlatWater.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSkyComposite.cginc"
 sampler2D _InfoTex;
 #include "../Resources/ORBIS/FlatWorld/FlatPixelDetail.cginc"
 float2 _ProbeRoot;
 float4 frag(v2f_img i):SV_Target
 {
  float shadow=PixelPropShadow(_ProbeRoot+.5+i.uv);
  return float4(shadow,shadow,shadow,1);
 }
 ENDCG
 }}
}
