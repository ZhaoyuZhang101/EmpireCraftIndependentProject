Shader "Hidden/ORBIS/RiverAtlasProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.5
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 #include "../Resources/ORBIS/FlatWorld/Water/FlatRiverAtlas.cginc"
 sampler2D _ProbeCells;
 float _ProbePoints,_ProbeStage;
 float4 _ProbeOrigin,_ProbeSpan;
 float4 frag(v2f_img i):SV_Target
 {
  float2 cell=_ProbeOrigin.xy+i.uv*_ProbeSpan.xy;
  if(_ProbePoints>.5)cell=tex2Dlod(_ProbeCells,float4(i.uv,0,0)).rg;
  if(_ProbeStage>.5)return FlatRiverStage(cell).xxxx;
  return FlatRiverAtlasSample(cell);
 }
 ENDCG
 }}
}
