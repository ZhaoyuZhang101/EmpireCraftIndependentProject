Shader "Hidden/ORBIS/LocalPlantHeights"
{
 SubShader { Pass {
 Cull Off ZWrite Off ZTest Always
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "FlatVisualTexture.cginc"
 float4 _MapSize,_PlantLattice;
 #include "LocalProjection.cginc"
 float frag(v2f_img i):SV_Target
 {
  float2 index=floor(i.uv*_PlantLattice.xy);
  float sn,cs;sincos(index.x*6.283185307/_PlantLattice.x,sn,cs);
  float2 chart=float2(cs,sn)*(index.y/(_PlantLattice.y-1))*_LocalRadius;
  return ScrollTerrainRadius(LocalGeoUV(chart),_MapSize)-1;
 }
 ENDCG
 }
 Pass {
 Cull Off ZWrite Off ZTest Always
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "FlatVisualTexture.cginc"
 float4 _MapSize,_PlantLattice;
 #include "ScrollProjection.cginc"
 float frag(v2f_img i):SV_Target
 {
  float2 vertex=floor(i.uv*_PlantLattice.xy)/(_PlantLattice.xy-1);
  return ScrollTerrainRadius(MarkleyGeoUV(vertex),_MapSize)-1;
 }
 ENDCG
 }}
}
