Shader "Hidden/ORBIS/PolarDetailProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 float _Latitude;
 float4 frag(v2f_img i):SV_Target
 {
  float lat=_Latitude*.01745329252;float2 xy=(i.uv-.5)*.001;
  float3 dir=normalize(float3(xy.x,sin(lat)+cos(lat)*xy.y,cos(lat)-sin(lat)*xy.y));
  float2 uv=float2(atan2(dir.x,dir.z)/6.283185307+.5,atan2(dir.y,length(dir.xz))/3.141592654+.5);
  float3 detail=FlatDetailPosition(uv*_MapSize.xy);
  return float4(length(ddx(detail))*128000,length(ddy(detail))*128000,0,1);
 }
 ENDCG
 }}
}
