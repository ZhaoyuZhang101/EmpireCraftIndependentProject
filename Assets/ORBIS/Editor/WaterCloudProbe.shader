Shader "Hidden/ORBIS/WaterCloudProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_FLAT_NO_SWE
 #include "../Resources/ORBIS/FlatWorld/Water/FlatWater.cginc"
 #include "../Resources/ORBIS/OrbisHPVolumeCloud.cginc"
 float _ProbeKind,_WaterKind;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeKind<.5)
  {
   float density=OrbisHPLowDensity(i.uv.y*13500,i.uv.x,.5,.7,.65);
   float phi=OrbisHPCompressDiffuse(OrbisHPDiffuseStep(i.uv.x*24,i.uv.y*8,i.uv.y*.2,.001+i.uv.x*.1,1));
   float total=0;[loop] for(int k=0;k<6;k++){float2 s=OrbisHPLightSegment(k,6,1);total+=s.y-s.x;}
   return float4(density,phi,total,1);
  }
  if(_ProbeKind>1.5)
  {
   float relief=FlatFieldRelief(i.uv*_MapSize.xy,1,normalize(float3(-.45,.6,.75)));
   return float4(relief,relief,relief,1);
  }
  float depth=.1+i.uv.x*3999.9;
  float4 hydro=float4(depth,.7,.4,_WaterKind>4.5?.15:0);
  float4 mixing=float4(.2,.1,.8,0),field=float4(100,.2,depth,0);
  float3 col=FlatWaterShade(i.uv*4,hydro,mixing,field,float3(.3,.3,.2),float3(.8,.9,1),float3(0,0,1),normalize(float3(-.4,.5,.8)),.7,_WaterKind);
  return float4(col,1);
 }
 ENDCG
 }}
}
