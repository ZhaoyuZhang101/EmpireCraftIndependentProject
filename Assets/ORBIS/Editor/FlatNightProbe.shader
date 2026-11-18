Shader "Hidden/ORBIS/FlatNightProbe"
{
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma target 3.0
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSurfaceField.cginc"
 #include "../Resources/ORBIS/FlatWorld/FlatSkyComposite.cginc"
 float _ProbeLightning;
 float _ProbePrecipitation,_ProbeWater,_ProbeIce;
 float4 _ProbeArea;
 float4 frag(v2f_img i):SV_Target
 {
  if(_ProbePrecipitation>.5)
  {
   float2 cell=_ProbeArea.xy+i.uv*_ProbeArea.zw;
   float2 uv=cell*_MapSize.zw;
   float4 cloud=FlatCloudColumn(uv);
   FlatPrecipitation weather=FlatSamplePrecipitation(uv,cell,cloud,48,1);
   if(_ProbePrecipitation>1.5)return float4(weather.streak,weather.spray,weather.ripple,weather.flake);
   float3 ground=float3(.20,.28,.35);
   ground=FlatRainOnSurface(ground,weather,_ProbeWater,_ProbeIce,1);
   return float4(FlatSkyComposite(ground,uv,cloud,48,weather,1),1);
  }
  if(_ProbeLightning>.5)return float4(FlatSkyComposite(0,i.uv,FlatCloudColumn(i.uv),48,(FlatPrecipitation)0,1),1);
  float3 color=FlatColor(float3(.45,.40,.30))*FlatDayLight(float3(0,0,1),float2(.5,.5));
  #ifdef UNITY_COLORSPACE_GAMMA
  color=GammaToLinearSpace(color);
  #endif
  return float4(color,1);
 }
 ENDCG
 }}
}
