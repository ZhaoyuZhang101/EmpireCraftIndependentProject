Shader "Hidden/ORBIS/WaterOpticsProbe"
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
 float4 _ProbeCell;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbeMode>5.5)
  {
   // Both sides are under one surveyed shore. Change only the coarse
   // support beneath it: a dry tile, a river channel or a measured basin.
   float4 basin=i.uv.x<.5 ? float4(0,0,0,0) : float4(12,.7,0,_ProbeCell.y);
   float support=i.uv.x<.5 ? 0 : _ProbeCell.z;
   FlatEarthLakeOptics(_ProbeCell.x,.008,basin,support);
   return basin;
  }
  if(_ProbeMode>4.5)
  {
   float shoreKm=i.uv.x<.5?.2:8;
   float support=_ProbeCell.w;
   float4 basin=float4(_ProbeCell.z,0,1,4);
   FlatEarthLakeOptics(1,shoreKm*_MapSize.x/40030.2,basin,support);
   return basin;
  }
  if(_ProbeMode>3.5)
  {
   float2 cell=float2(8,6)+i.uv*float2(4,2);
   return float4(FlatWaterShade(cell,float4(3,0,0,0),float4(0,0,1,0),
    float4(-3,2,3,0),float3(.4,.35,.3),float3(1,1,1),float3(0,0,1),
    normalize(float3(-.8,.5,.3)),0,4,-2),1);
  }
  if(_ProbeMode>2.5)
  {
   float shore=i.uv.x<.5?-.0001:.0001;
   return float4(FlatWaterShade(float2(16,8),float4(3,1,0,.2),float4(0,0,1,0),
    float4(0,shore,3,0),float3(.3,.3,.2),float3(1,1,1),float3(0,0,1),
    normalize(float3(-.4,.5,.8)),0,shore<0?5:4,-.1),1);
  }
  if(_ProbeMode>1.5)
  {
   float2 sampleCell=_ProbeCell.xy+float2(i.uv.x<.5?-.0001:.0001,0);
   float2 uv=(floor(sampleCell)+.5)*_MapSize.zw;
   float4 hydro=tex2Dlod(_HydroA,float4(uv,0,0)),mixing=tex2Dlod(_HydroB,float4(uv,0,0));
   float kind=floor(tex2Dlod(_InfoTex,float4(uv,0,0)).g*255+.5);
   return FlatAreaWaterOptics(sampleCell,_InfoTex,float4(hydro.r,mixing.g,mixing.b,kind));
  }
  float2 cell=float2(16,8);
  float kind=4,waves=0;
  float3 sun=normalize(float3(-.4,.5,.8));
  if(_ProbeMode<.5)
  {
   // Hold geography constant so only the interpolated water classification varies.
   kind=i.uv.x<.5?3.4999:3.5001;
   waves=1;
   sun=normalize(float3(.52,0,1));
  }
  float3 colour=FlatWaterShade(cell,float4(.7,0,0,0),float4(0,0,1,0),
   float4(-.7,.18,.7,0),float3(.3,.3,.2),float3(1,1,1),float3(0,0,1),sun,waves,kind,-.18);
  return float4(colour,1);
 }
 ENDCG
 }}
}
