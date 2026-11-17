Shader "Hidden/ORBIS/VegetationArtProbe"
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
 float _ProbeMode;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeMode>1.5)
  {
   float2 uv=(_ProbeRoot+.5)*_MapSize.zw;
   float4 info=tex2Dlod(_InfoTex,float4(uv,0,0))*255;
   float tree;
   float4 ecology=FlatEcologyData(uv,info,tree);
   return float4(PDTreeSpecies(ecology,info,0,tree),ecology.g,tree,ecology.a);
  }
  float scale=max(.02,cos(((_ProbeRoot.y+.5)*_MapSize.w-.5)*3.141592654));
  float2 cell=_ProbeRoot+PDTreePlacement(_ProbeRoot).xy/32+(i.uv-.5)*float2(2,2*scale);
  if(_ProbeMode>.5)return PixelPropShadow(cell).xxxx;
  float3 col=PixelProps(0,cell,float4(10,-3,0,1),0,0,1);
  return float4(col,step(.001,max(col.r,max(col.g,col.b))));
 }
 ENDCG
 }}
}
