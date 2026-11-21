Shader "Hidden/ORBIS/WaterCoastProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #define ORBIS_FLAT_NO_SWE
 #include "../Resources/ORBIS/FlatWorld/Water/FlatWater.cginc"
 sampler2D _InfoTex;
 float _ProbeMode;
 float4 _CoastBasin,_CoastCondition,_CoastOrigin,_BedSun;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeMode>11.5)
  {
   float2 uv=_CoastOrigin.xy*_MapSize.zw;
   float4 grid=FlatPolarField(FlatVisualSample(float4(uv,0,0)),uv.y);
   float depth,shade=FlatSampleSeabed(_CoastOrigin.xy,normalize(_BedSun.xyz),depth);
   return float4(depth,grid.r+grid.b,shade,1);
  }
  if(_ProbeMode>10.5)
  {
   float2 cell=_CoastOrigin.xy;
   float2 gradient=FlatFieldGradient(cell,0);
   float height=FlatVisualSample(float4(cell*_MapSize.zw,0,0)).r;
   float3 e=float3(.8,0,.6),n=e.yxz;
   float3 view=float3(0,0,1);
   float a=FlatTerrainRelief(cell,height,gradient,float4(e,1),view,0).r;
   float b=FlatTerrainRelief(cell,height,gradient,float4(n,1),view,0).r;
   float c=FlatTerrainRelief(cell,height,gradient,float4(-e.xy,e.z,1),view,0).r;
   float d=FlatTerrainRelief(cell,height,gradient,float4(-n.xy,n.z,1),view,0).r;
   float depth;
   return float4(c-a,d-b,FlatSampleSeabed(cell,e,depth)-FlatSampleSeabed(cell,float3(-e.xy,e.z),depth),
                         FlatSampleSeabed(cell,n,depth)-FlatSampleSeabed(cell,float3(-n.xy,n.z),depth));
  }
  if(_ProbeMode>5.5)
  {
   float2 cell=_CoastOrigin.xy+float2(i.uv.x*1.4,(i.uv.y-.5)*.70);
   if(_ProbeMode>7.5&&_ProbeMode<8.5)
   {
    float metres;
    float relief=FlatSampleSeabed(cell,normalize(_BedSun.xyz),metres);
    return float4(relief,metres,0,1);
   }
   float shore=cell.y-128;
   float4 field=float4(-shore*_CoastCondition.x,shore,_CoastBasin.r,0);
   FlatCoastalSediment coast=FlatCoastAt(cell,field,_CoastBasin,_CoastCondition.w,-shore+_CoastCondition.z,
                                        _CoastCondition.y,_WaterClock);
   if(_ProbeMode>8.5)
   {
    FlatWaterSurfaceData surface;
    float3 colour=FlatWaterShade(cell,float4(_CoastBasin.r,0,0,0),float4(0,0,0,0),field,float3(.3,.3,.2),
     float3(1,1,1),float3(0,0,1),normalize(float3(-.4,.45,.8)),0,_CoastBasin.a,-shore,coast,surface);
    return float4(colour,coast.coverage);
   }
   if(_ProbeMode>6.5)return float4(coast.coverage,coast.mud,coast.bar,coast.shore);
   return float4(coast.albedo,coast.coverage);
  }
  if(_ProbeMode>4.5)
  {
   float channel=i.uv.x<.5?0:.12;
   float kind=3+step(.5,i.uv.y);
   float3 colour=FlatWaterShade(float2(183,150.7),float4(1,0,0,channel),float4(0,0,0,0),
    float4(0,.3,40,0),float3(.3,.3,.2),float3(1,1,1),float3(0,0,1),normalize(float3(-.4,.5,.8)),0,kind,-.3);
   return float4(colour,1);
  }
  if(_ProbeMode>3.5)
  {
   float3 substrate=FlatBedColor(float2(i.uv.x<.5?182.9999:183.0001,150.7));
   return float4(substrate,1);
  }
  if(_ProbeMode>2.5)
  {
   float2 cell=float2((.875+i.uv.x*.75)*_MapSize.x,90.35+i.uv.y*35);
   return float4(FlatShoreDetail(cell),cell,1);
  }
  if(_ProbeMode>1.5)
  {
   float crest=FlatCoastFoam(-.2+i.uv.x*1.2,_WaterClock,.5);
   return float4(crest,crest,crest,1);
  }
  float2 cell=float2(i.uv.x<.5?182.9999:183.0001,150.7);
  float kind=floor(tex2Dlod(_InfoTex,float4((floor(cell)+.5)*_MapSize.zw,0,0)).g*255+.5);
  if(_ProbeMode>.5){float snow=FlatLandSnow(cell,_InfoTex);return float4(snow,snow,snow,1);}
  kind=FlatAreaWaterKind(cell,_InfoTex,kind);
  float3 c=FlatWaterShade(cell,float4(15,0,0,0),float4(0,0,0,0),float4(0,.18,15,0),float3(.3,.3,.2),float3(1,1,1),float3(0,0,1),normalize(float3(-.4,.5,.8)),1,kind,-.18);
  return float4(c,1);
 }
 ENDCG
 }}
}
