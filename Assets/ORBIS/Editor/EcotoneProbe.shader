Shader "Hidden/ORBIS/EcotoneProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 3.0
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 float _ProbeShore;
 float4 frag(v2f_img i):SV_Target
 {
  float2 warped=FlatEcotoneCell(i.uv*_MapSize.xy,_ProbeShore);
  float3 a=FlatRadial(FlatEcotoneCell(float2(0,i.uv.y*_MapSize.y),_ProbeShore)*_MapSize.zw);
  float3 b=FlatRadial(FlatEcotoneCell(float2(_MapSize.x,i.uv.y*_MapSize.y),_ProbeShore)*_MapSize.zw);
  float motion=length(FlatRadial(warped*_MapSize.zw)-FlatRadial(i.uv));
  return float4(motion,length(a-b),0,1);
 }
 ENDCG
 }}
}
