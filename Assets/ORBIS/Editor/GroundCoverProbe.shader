Shader "Hidden/ORBIS/GroundCoverProbe"
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
 float4 _ProbeOrigin,_ProbeSpan;
 float _ProbeWater,_ProbeMode,_ProbeCover;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeMode>.5)
  {
   float2 id=floor(i.uv*float2(16,8));
   float4 ecology=float4(_ProbeCover,3,.7,i.uv.x);
   float4 placement=PDTreePlacement(id);placement.w=ecology.a;
   float species=PDTreeSpecies(ecology,0,0);
   float available=PDTreeAvailable(id,placement,0,ecology)?1:0;
   return float4(species,available,ecology.a,1);
  }
  float2 cell=_ProbeOrigin.xy+i.uv*max(float2(2,1),_ProbeSpan.xy);
  float2 uv=(floor(cell)+.5)*_MapSize.zw;
  return float4(PixelGround(FlatColor(float3(.36,.45,.25)),cell,float4(120,-3,0,.6),_ProbeWater,0,FlatEcologyData(uv,0)),1);
 }
 ENDCG
 }}
}
