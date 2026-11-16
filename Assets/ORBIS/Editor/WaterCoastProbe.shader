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
 float4 frag(v2f_img i):SV_Target
 {
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
