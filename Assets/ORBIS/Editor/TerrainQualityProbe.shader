Shader "Hidden/ORBIS/TerrainQualityProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 float4 _ProbeOrigin,_ProbeSpan,_ProbeGradient;
 float _ProbePixels,_ProbeFlip,_ProbeMode,_ProbeCoverage;
 float4 frag(v2f_img i):SV_Target
 {
  float2 sampleUV=i.uv;
  if(_ProbeFlip>.5)sampleUV.x=1-sampleUV.x;
  float2 span=_ProbeSpan.x>0?_ProbeSpan.xy:float2(2,1);
  float2 cell=_ProbeOrigin.xy+sampleUV*span;
  if(_ProbeMode>2.5)return float4(FlatElevationColor(FlatVisualSample(float4(cell*_MapSize.zw,0,0)).r),1);
  if(_ProbeMode>1.5)return float4(FlatTerrainSnow(_ProbeCoverage,cell,_ProbeGradient.xy,_ProbePixels).xxx,1);
  if(_ProbeMode>.5)return float4(FlatFieldRelief(cell,0,float3(-.45,.6,.75)).xxx,1);
  return float4(FlatTerrainFine(FlatColor(float3(.38,.47,.24)),cell,_ProbePixels,FlatFieldGradient(cell,0)),1);
 }
 ENDCG
 }}
}
