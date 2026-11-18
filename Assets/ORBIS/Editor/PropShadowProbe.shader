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
 #include "../Resources/ORBIS/FlatWorld/Water/FlatWaterReflection.cginc"
 float2 _ProbeRoot;
 float _ProbeMode;
 float4 _ProbeReflectionRay;
 float2 _ProbeCaster;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeMode>4.5)
  {
   float2 source=_ProbeCaster,uv=(source+.5)*_MapSize.zw;
   float2 cell=source+float2(.5,-.35)+(i.uv-.5)*2;
   float3 ray=normalize(_ProbeReflectionRay.xyz);
   float4 actual=FlatWaterPropReflection(cell,0,ray,1);
   float4 placement=PDTreePlacement(source);
   float tree;
   float4 info=tex2Dlod(_InfoTex,float4(uv,0,0))*255;
   float4 ecology=FlatEcologyData(uv,info,tree);placement.w=ecology.a;
   float scale=FlatArtworkLatitudeScale(uv.y);
   float2 ground=((cell-source)*32-placement.xy)/placement.z;
   ground.y/=scale;
   float travel=-ground.y/ray.y;
   float elevation=max(0,FlatVisualSample(float4((source+placement.xy/32)*_MapSize.zw,0,0)).r);
   float height=-elevation*.000006*32/((6.283185307/_MapSize.x)*scale*placement.z)+ray.z*travel;
   float2 pixel=float2(ground.x+ray.x*travel,height*PDTreeUp.z);
   float4 expected=PDTreeArtwork(floor(pixel),source,tree,placement.w,1,1,float3(0,0,1));
   expected.a*=step(0,travel)*(1-smoothstep(44,58,length(ground)))*smoothstep(.03,.12,abs(ray.y));
   float negative=pixel.y<-.001&&PDTreeInDrawBounds(pixel);
   return float4(actual.a*negative,expected.a*negative,0,1);
  }
  if(_ProbeMode>3.5)
  {
   float scale=max(.02,cos(((_ProbeCaster.y+.5)*_MapSize.w-.5)*3.141592654));
   float2 q=floor((i.uv-.25)*64)+.5;
   float2 cell=_ProbeCaster+float2(q.x,16+(q.y-16)*scale)/32;
   return float4(PixelPropShadow(cell)/.27,PDSettlementArtwork(q,1).a,0,1);
  }
  if(_ProbeMode>2.5)
  {
   float scale=max(.02,cos(((_ProbeCaster.y+.5)*_MapSize.w-.5)*3.141592654));
   float2 q=(_ProbeRoot+i.uv*3-_ProbeCaster)*32;
   q.y=16+(q.y-16)/scale;
   float3 ray=normalize(_ProbeReflectionRay.xyz);
   float travel=(5-q.y)/ray.y;
   return travel>0?PDSettlementArtwork(floor(float2(q.x+ray.x*travel,5+ray.z*travel*PDSettlementHeightProjection)),1):0;
  }
  if(_ProbeMode>1.5)
   return float4(FlatWaterSkyReflection(_ProbeRoot+i.uv,normalize(_ProbeReflectionRay.xyz),float3(0,.2,.98),1,1),1);
  if(_ProbeMode>.5)
   return FlatWaterPropReflection(_ProbeRoot+i.uv*3,0,normalize(_ProbeReflectionRay.xyz),1);
  float shadow=PixelPropShadow(_ProbeRoot+.5+i.uv);
  return float4(shadow,shadow,shadow,1);
 }
 ENDCG
 }}
}
