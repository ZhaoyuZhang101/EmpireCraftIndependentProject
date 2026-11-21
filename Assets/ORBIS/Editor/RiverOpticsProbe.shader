Shader "Hidden/ORBIS/RiverOpticsProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_FLAT_NO_SWE
 #include "../Resources/ORBIS/FlatWorld/Water/FlatWater.cginc"
 float _TestBend,_TestGrade,_TestWaves,_TestSediment,_TestShore,_TestOutput;
 float _TestHeading,_TestWorldOffset;
 float4 frag(v2f_img i):SV_Target
 {
  if(_TestOutput==6)
  {
   float2 cell=float2((112.632+180)/360.0,(29.751+90)/180.0)*_MapSize.xy+(i.uv-.5)*1.35;
   cell.x+=_TestWorldOffset*_MapSize.x;
   FlatRiverSection section=(FlatRiverSection)0;
   section.flow=normalize(float2(_TestHeading,1));section.depth=4;section.width=.05;
   section.edge=-section.width;section.influence=1;
   FlatCoastalSediment coast=(FlatCoastalSediment)0;coast.shore=-2;
   FlatWaterSurfaceData surface;
   float3 water=FlatWaterShade(cell,0,float4(80,-2,4,0),FlatColor(float3(.50,.48,.41)),1,float3(0,0,1),
       normalize(float3(-.46,.29,.85)),_TestWaves,3,coast,section,surface);
   return float4(water,1);
  }
  // Two banks of one curved watercourse at equal distances from the centre.
  float2 p=(i.uv-.5)*float2(.9,.6);
  float2 flow=normalize(float2(1,_TestBend*p.x));
  float across=(p.y-.5*_TestBend*p.x*p.x)*flow.x;
  float2 cell=_MapSize.xy*.5+float2(1,0)+p;
  float4 hydro=float4(3,flow,.2);
  float4 field=float4(100-_TestGrade*p.x,_TestShore,12,0);
  float edge=abs(across)-hydro.a;
  float4 data=float4(edge,hydro.r,flow*hydro.a);
  FlatRiverSection channel=FlatRiverSectionAt(cell,data,field);
  float3 position=FlatDetailPosition(cell);
  if(_TestOutput==1)return float4(channel.across,channel.depth*FlatRiverDepthProfile(channel),FlatRiverPointBar(channel,channel.across),channel.bend);
  if(_TestOutput==2)return float4(channel.grade,FlatRiverRiffle(channel,position),channel.influence,1);
  if(_TestOutput==3)
  {
   float3 substrate=FlatRiverSubstrate(FlatColor(float3(.56,.54,.46)),position,channel,channel.across,_TestSediment,.006);
   return float4(substrate,1);
  }
  if(_TestOutput==5)
  {
   float aa=.005;
   float dry=smoothstep(-aa,aa,channel.edge);
   float3 land=FlatColor(float3(.20,.33,.18)),bed=FlatColor(float3(.56,.54,.46));
   return float4(FlatRiverBank(land,bed,1,position,channel,_TestSediment,.006,dry,aa,field.g),1);
  }
  FlatWaterSurfaceData surface;
  if(_TestOutput==4)
  {
   FlatCoastalSediment coast=(FlatCoastalSediment)0;coast.shore=field.g;
   float3 mouth=FlatWaterShade(cell,float4(0,_TestSediment,0,0),field,FlatColor(float3(.60,.55,.40)),1,
     float3(0,0,1),normalize(float3(-.4,.5,.8)),_TestWaves,3,coast,channel,surface);
   return float4(mouth,surface.visible);
  }
  float3 colour=FlatWaterShade(cell,hydro,float4(0,_TestSediment,1,0),field,
     FlatColor(float3(.60,.55,.40)),float3(1,1,1),float3(0,0,1),
     normalize(float3(-.4,.5,.8)),_TestWaves,5,edge,surface);
  return float4(colour,i.uv.y);
 }
 ENDCG
 }}
}
