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
 float _ProbeKind,_WaterKind,_ProbeFlip;
 float _ProbeDepth,_ProbeSediment,_ProbeWaves,_ProbeWrap;
 float4 _ProbeView,_ProbeCell,_ProbeSun;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeKind<.5)
  {
   float density=OrbisHPLowDensity(i.uv.y*13500,i.uv.x,.5,.7,.65);
   float phi=OrbisHPCompressDiffuse(OrbisHPDiffuseStep(i.uv.x*24,i.uv.y*8,i.uv.y*.2,.001+i.uv.x*.1,1));
   float total=0;[loop] for(int k=0;k<6;k++){float2 s=OrbisHPLightSegment(k,6,1);total+=s.y-s.x;}
   return float4(density,phi,total,1);
  }
  if(_ProbeKind>4.5)
  {
   float2 cell=_ProbeCell.xy+i.uv*float2(2,1);float boundary=i.uv.x*.2;
   float3 normal=normalize(float3(.3,-.2,1));
   float2 offset=FlatBedRefraction(cell,normalize(_ProbeView.xyz),normal,_ProbeDepth,boundary);
   return float4(offset,min(.08,boundary*.25),1);
  }
  if(_ProbeKind>3.5)
  {
   float2 sampleUV=i.uv;if(_ProbeFlip>.5)sampleUV.x=1-sampleUV.x;
   float2 cell=_ProbeCell.xy+sampleUV*float2(2,1);
   if(_ProbeWrap>.5)cell.x=frac(cell.x*_MapSize.z)*_MapSize.x;
   float width=_WaterKind>4.5?.15:0;
   float3 colour=FlatWaterShade(cell,float4(_ProbeDepth,.7,.4,width),float4(0,_ProbeSediment,1,0),
    float4(-_ProbeDepth,2,_ProbeDepth,0),float3(.3,.3,.2),float3(.8,.9,1),normalize(_ProbeView.xyz),
    normalize(_ProbeSun.xyz),_ProbeWaves,_WaterKind,-.12);
   return float4(colour,1);
  }
  if(_ProbeKind>1.5&&_ProbeKind<2.5)
  {
   float relief=FlatFieldRelief(i.uv*_MapSize.xy,1,normalize(float3(-.45,.6,.75)));
   return float4(relief,relief,relief,1);
  }
  float2 sampleUV=i.uv;
  if(_ProbeFlip>.5)sampleUV.x=1-sampleUV.x;
  float depth=.1+sampleUV.x*3999.9;
  float4 hydro=float4(depth,.7,.4,_WaterKind>4.5?.15:0);
  float4 mixing=float4(.2,.1,.8,0),field=float4(100,.2,depth,0);
  float waterEdge=_WaterKind>4.5?(abs(sampleUV.y-.5)*2-1)*hydro.a:-field.g;
  float3 col=FlatWaterShade(sampleUV*4,hydro,mixing,field,float3(.3,.3,.2),float3(.8,.9,1),float3(0,0,1),normalize(float3(-.4,.5,.8)),.7,_WaterKind,waterEdge);
  return float4(col,1);
 }
 ENDCG
 }}
}
