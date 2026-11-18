#ifndef ORBIS_SCROLL_CLOUD_SOURCE
#define ORBIS_SCROLL_CLOUD_SOURCE
#include "ScrollHeight.cginc"
#include "../OrbisHPVolumeCloud.cginc"
sampler2D _ScrollWeatherTex, _ScrollHeightTex, _ScrollCloudWindTex;
float4 _ScrollSourceSize;
float _ScrollWeatherTime;
float ScrollCloudGround(float2 uv)
{
 if(_ScrollHeightScale<=0)return 1;
 float2 pixel=uv*_ScrollSourceSize.xy-.5,base=floor(pixel),fraction=frac(pixel);
 float2 texel=1/max(float2(1,1),_ScrollSourceSize.xy);
 float2 centre=(base+.5)*texel,dx=float2(texel.x,0),dy=float2(0,texel.y);
 return ScrollInterpolatedRadius(tex2Dlod(_ScrollHeightTex,float4(centre,0,0)),tex2Dlod(_ScrollHeightTex,float4(centre+dx,0,0)),
                                 tex2Dlod(_ScrollHeightTex,float4(centre+dy,0,0)),tex2Dlod(_ScrollHeightTex,float4(centre+dx+dy,0,0)),fraction,uv,_ScrollSourceSize.y);
}
// Uses the SAME spherical coordinates on the globe and every repeated 2D sheet.
float2 ScrollClimateDensities(float3 p,bool simpleMode)
{
 float2 uv=OrbisWeatherUV(p);
 float ground=ScrollCloudGround(uv);
 float z=(length(p)-ground)/max(1e-7,_CloudHeightScale);
 if(z<400||z>13500)return 0;
 float4 weather=tex2Dlod(_ScrollWeatherTex,float4(uv,0,0));
 if(weather.r+weather.g<.006)return 0;
 float3 up=normalize(p);
 float3 localWind=tex2Dlod(_ScrollCloudWindTex,float4(uv,0,0)).xyz;
 float phase=frac(_ScrollWeatherTime*.016);
 float3 a=normalize(up-localWind*phase*8),b=normalize(up+localWind*(1-phase)*8);
 float coverage=weather.r,convection=weather.b;
 float low=0,high=0;
 if(coverage>.004&&z<11500)
 {
  // Sample successive radial slices through the HP body. Low decks have shallow
  // tops while moist convective towers build well above them.
  float altitude=z/7000;
  float scale=3.1+altitude*.18;
  // Rotate the HP lattice off the map axes before sampling the cloud body.
  const float3x3 frame=float3x3(.36,.48,-.80,-.80,.60,0,.48,.64,.60);
  float3 offset=float3(.13,.71,.37)+mul(frame,localWind)*altitude*.8;
  float3 q0=mul(frame,a)*scale+offset,q1=mul(frame,b)*scale+offset;
  float4 body=lerp(tex3Dlod(_CloudNoise,float4(q0,0)),tex3Dlod(_CloudNoise,float4(q1,0)),phase);
  float shape=saturate(body.r*.82+body.g*.24+coverage*.09);
  float4 erosion=.48;
  if(!simpleMode)
  {
   float3 curl=(body.gba-.5)*.74;
   erosion=lerp(tex3Dlod(_CloudDetailNoise,float4(q0*5.7+curl,0)),tex3Dlod(_CloudDetailNoise,float4(q1*5.7+curl,0)),phase);
   coverage=saturate(coverage+(erosion.r*.66+erosion.g*.34-.48)*.46*smoothstep(.02,.19,coverage));
  }
  float tower=smoothstep(.32,.95,convection);
  float height=450+(z-450)/lerp(.88,1.33,tower);
  low=OrbisHPLowDensityDetailed(height,.26+.74*coverage,convection,shape,erosion,.58+.42*body.g);
  low*=coverage*lerp(.48,1.15,tower);
 }
 if(weather.g>.004&&z>5750)
 {
  // Ice clouds have a separate silhouette and optical depth. They can persist
  // over clear ocean without borrowing any of the cumulus body samples.
  float4 ice=tex2Dlod(_HPHighDetail,float4(uv,0,0));
  float s,c;sincos(_ScrollWeatherTime*.0018,s,c);
  float3 upper=float3(c*up.x-s*up.z,up.y,s*up.x+c*up.z);
  float3 filament=float3(dot(upper,float3(.84,.25,.48))*3.1,dot(upper,float3(-.50,.05,.86))*11,upper.y*9.7);
  float fine=tex3Dlod(_CloudDetailNoise,float4(filament+(ice.b-.5)*.37,0)).g;
  float highCover=weather.g*(.68+.57*fine);
  high=OrbisHPHighDensity(z,.24+.63*highCover,.36,.66+.34*ice.r,ice.g)*highCover*.85;
 }
 return float2(low,high)*_Density*max(0,_Cover);
}
#endif
