Shader "Hidden/ORBIS/AmbientLifeProbe"
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
 #include "../Resources/ORBIS/FlatWorld/FlatAmbientLife.cginc"

 float4 _ProbeCellRect;
 float _ProbeMode,_ProbeSpecies,_ProbeVariant,_ProbeSeed;

 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeMode>6.5)
   return PDAmbientParticles((i.uv-.5)*float2(36,18),float4(36,24,_ProbeSeed,1),_ProbeSpecies,_ProbeVariant);
  if(_ProbeMode>2.5&&_ProbeMode<3.5)
  {
   float cover=min(13,floor(i.uv.x*14));
   float species=PDAmbientSpecies(float4(cover,0,.5,_ProbeVariant),_ProbeSeed);
   return float4(species,cover,0,1);
  }
  if(_ProbeMode>.5&&_ProbeMode<1.5)
  {
   float4 pose=float4(0,0,1,_WaterClock+1.33);
   return PDAmbientArt((i.uv-.5)*float2(36,18),_ProbeSpecies,pose,_ProbeVariant);
  }
  float2 cell=_ProbeCellRect.xy+i.uv*_ProbeCellRect.zw;
  if(_ProbeMode>4.5&&_ProbeMode<5.5)
  {
   float clear=PDAmbientHomeClear(PDAmbientHome(cell),true)?1:0;
   return clear.xxxx;
  }
  if(_ProbeMode>1.5&&_ProbeMode<2.5)
  {
   float4 home=PDAmbientHome(cell);
   float4 pose=PDAmbientPose(home);
   return float4(home.xy,pose.xy);
  }
  float2 uv=(floor(cell)+.5)*_MapSize.zw;
  float4 info=floor(tex2Dlod(_InfoTex,float4(uv,0,0))*255+.5);
  float4 field=FlatField(cell);
  float waterMask=step(-.01,field.g);
  float3 background=_ProbeMode>3.5&&_ProbeMode<5.5?float3(.6,.6,.6):0;
  float3 col=PixelAmbientLife(background,cell,field,waterMask,info,1);
  if(_ProbeMode>5.5)
  {
   float4 home=PDAmbientHome(cell),pose=PDAmbientPose(home);
   float2 delta=cell-pose.xy;
   delta.x-=floor(delta.x/_MapSize.x+.5)*_MapSize.x;
   float2 pixel=delta*float2(32,32/home.w);
   float4 ecology=FlatEcologyData((floor(home.xy)+.5)*_MapSize.zw,info);
   float species=PDAmbientSpecies(ecology,home.z);
   col*=1-PDAmbientArt(pixel,species,pose,ecology.a).a;
   return float4(col,step(.00001,max(col.r,max(col.g,col.b))));
  }
  float alpha=_ProbeMode>3.5?1:step(.00001,max(col.r,max(col.g,col.b)));
  return float4(col,alpha);
 }
 ENDCG
 }}
}
