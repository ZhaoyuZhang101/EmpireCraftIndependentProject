Shader "Hidden/ORBIS/TerrainQualityProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 float4 _ProbeOrigin;
 float _ProbePixels,_ProbeFlip;
 float4 frag(v2f_img i):SV_Target
 {
  float2 sampleUV=i.uv;
  if(_ProbeFlip>.5)sampleUV.x=1-sampleUV.x;
  float2 cell=_ProbeOrigin.xy+sampleUV*float2(2,1);
  return float4(FlatTerrainFine(FlatColor(float3(.38,.47,.24)),cell,_ProbePixels,FlatFieldGradient(cell,0)),1);
 }
 ENDCG
 }}
}
