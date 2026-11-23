#ifndef ORBIS_SCROLL_CLOUD_SOURCE
#define ORBIS_SCROLL_CLOUD_SOURCE
#include "ScrollHeight.cginc"
#include "../OrbisHPVolumeCloud.cginc"
sampler2D _ScrollWeatherTex, _ScrollCloudWindTex;
UNITY_DECLARE_TEX2D(_ScrollHeightTex);
float4 _ScrollSourceSize;
float _ScrollWeatherTime;
float4 ScrollHeightAt(float2 uv)
{
 #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
 return UNITY_SAMPLE_TEX2D_LOD(_ScrollHeightTex,uv,0);
 #else
 return tex2Dlod(_ScrollHeightTex,float4(uv,0,0));
 #endif
}
float ScrollCloudGround(float2 uv)
{
 if(_ScrollHeightScale<=0)return 1;
 float2 pixel=uv*_ScrollSourceSize.xy-.5,base=floor(pixel),fraction=frac(pixel);
 float2 texel=1/max(float2(1,1),_ScrollSourceSize.xy);
 float2 centre=(base+.5)*texel,dx=float2(texel.x,0),dy=float2(0,texel.y);
 return ScrollInterpolatedRadius(ScrollHeightAt(centre),ScrollHeightAt(centre+dx),
                                 ScrollHeightAt(centre+dy),ScrollHeightAt(centre+dx+dy),fraction,uv,_ScrollSourceSize.y);
}
float ScrollCloudGroundFast(float2 uv)
{
 if(_ScrollHeightScale<=0)return 1;
 return ScrollRadius(ScrollSurfaceHeight(ScrollHeightAt(uv)),uv,_ScrollSourceSize.y);
}
// Uses the SAME spherical coordinates on the globe and every repeated 2D sheet.
float2 ScrollClimateDensities(float3 p,bool simpleMode)
{
 float2 uv=OrbisWeatherUV(p);
 float ground=simpleMode?ScrollCloudGroundFast(uv):ScrollCloudGround(uv);
 float z=(length(p)-ground)/max(1e-7,_CloudHeightScale);
 if(z<400||z>13500)return 0;
 float4 weather=tex2Dlod(_ScrollWeatherTex,float4(uv,0,0));
 if(weather.r+weather.g<.006)return 0;
 float3 up=normalize(p);
 float3 localWind=tex2Dlod(_ScrollCloudWindTex,float4(uv,0,0)).xyz;
 float phase=frac(_ScrollWeatherTime*.016);
 float coverage=weather.r,convection=weather.b;
 float low=0,high=0;
 if(coverage>.004&&z<11500)
 {
  float tower=smoothstep(.32,.95,convection);
  float height=450+(z-450)/lerp(.88,1.35,tower);
  // Exact vertical support bound for OrbisHPLowDensityDetailed: skip 3D volume fetches
  // when ray altitude lies strictly above the local Cu/Tcu/Cb profile ceiling.
  float hBound=(height-450)/6200;
  float covBound=.26+.74*(simpleMode?coverage:saturate(coverage+.23));
  float minLocalH=hBound/(1.16*(1.0+.55*covBound*max(0.0,hBound)));
  float profileCut=convection<=.001?.435:(convection<=.5?.635:.835);
  if(minLocalH<profileCut)
  {
   float3 a=normalize(up-localWind*phase*8),b=normalize(up+localWind*(1-phase)*8);
   // Sample successive radial slices through the HP body. Multi-scale spherical
   // frequencies prevent giant single-cell bubbles on the globe while preserving
   // mesoscale cloud turrets, wind-sheared convective towers and cellular decks.
   float altitude=z/7000;
   const float3x3 frame=float3x3(.36,.48,-.80,-.80,.60,0,.48,.64,.60);
   float3 offset=float3(.13,.71,.37)+mul(frame,localWind)*altitude*1.25;
   float macroScale=5.2+altitude*.24;
   float mesoScale=13.4+altitude*.55;
   float3 q0=mul(frame,a),q1=mul(frame,b);
   float4 macroBody=lerp(tex3Dlod(_CloudNoise,float4(q0*macroScale+offset,0)),tex3Dlod(_CloudNoise,float4(q1*macroScale+offset,0)),phase);
   float3 mesoWarp=(macroBody.gba-.5)*.20;
   float4 body=lerp(tex3Dlod(_CloudNoise,float4(q0*mesoScale+offset*1.63+mesoWarp,0)),tex3Dlod(_CloudNoise,float4(q1*mesoScale+offset*1.63+mesoWarp,0)),phase);
   float shape=saturate(macroBody.r*.44+body.r*.40+body.g*.20+coverage*.09);
   // Detail erosion only subtracts from pow(shape,0.62); skip 64^3 detail fetches if shape cannot condense.
   float maxBaseShape=pow(shape,.62);
   if(maxBaseShape>.89-covBound)
   {
    float4 erosion=.48;
    if(!simpleMode)
    {
     float3 curl=(body.gba-.5)*.68;
     erosion=lerp(tex3Dlod(_CloudDetailNoise,float4(q0*42.0+curl,0)),tex3Dlod(_CloudDetailNoise,float4(q1*42.0+curl,0)),phase);
     coverage=saturate(coverage+(erosion.r*.66+erosion.g*.34-.48)*.44*smoothstep(.02,.19,coverage));
    }
    low=OrbisHPLowDensityDetailed(height,.26+.74*coverage,convection,shape,erosion,.56+.44*body.g);
    low*=coverage*lerp(.50,1.16,tower);
   }
  }
 }
 if(weather.g>.004&&z>5750)
 {
  // Ice clouds have a separate silhouette and optical depth, sheared along local wind when present.
  float4 ice=tex2Dlod(_HPHighDetail,float4(uv,0,0));
  float s,c;sincos(_ScrollWeatherTime*.0018,s,c);
  float3 upper=float3(c*up.x-s*up.z,up.y,s*up.x+c*up.z);
  float wLen=length(localWind);
  float wActive=smoothstep(.0005,.004,wLen);
  float3 dynAlong=wLen>1e-5?(localWind/wLen):float3(.84,.25,.48);
  float3 dynCross=normalize(cross(up,dynAlong)+float3(1e-5,0,0));
  float3 axisAlong=normalize(lerp(float3(.84,.25,.48),dynAlong,wActive));
  float3 axisCross=normalize(lerp(float3(-.50,.05,.86),dynCross,wActive));
  float3 filament=float3(dot(upper,axisAlong)*3.1,dot(upper,axisCross)*12.5,upper.y*9.7);
  float fine=tex3Dlod(_CloudDetailNoise,float4(filament+(ice.b-.5)*.37,0)).g;
  float highCover=weather.g*(.68+.57*fine);
  high=OrbisHPHighDensity(z,.24+.63*highCover,.36,.66+.34*ice.r,ice.g)*highCover*.85;
 }
 return float2(low,high)*_Density*max(0,_Cover);
}
#endif
