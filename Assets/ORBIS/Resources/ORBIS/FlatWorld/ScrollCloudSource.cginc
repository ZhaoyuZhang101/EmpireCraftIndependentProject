#ifndef ORBIS_SCROLL_CLOUD_SOURCE
#define ORBIS_SCROLL_CLOUD_SOURCE
#include "ScrollHeight.cginc"
#include "../OrbisHPVolumeCloud.cginc"
sampler2D _ScrollWeatherTex, _ScrollHeightTex, _ScrollCloudWindTex;
float4 _ScrollSourceSize;
float _ScrollWeatherTime;
float ScrollCloudGround(float2 uv)
{
 return ScrollRadius(tex2Dlod(_ScrollHeightTex,float4(uv,0,0)),uv,_ScrollSourceSize.y);
}
// Uses the SAME spherical coordinates on the globe and every repeated 2D sheet.
float2 ScrollClimateDensities(float3 p,bool simpleMode)
{
 float2 uv=OrbisWeatherUV(p);
 float ground=ScrollCloudGround(uv);
 float z=(length(p)-ground)/max(1e-7,_CloudHeightScale);
 if(z<400||z>13500)return 0;
 float4 weather=tex2Dlod(_ScrollWeatherTex,float4(uv,0,0));
 if(weather.r+weather.g<.015)return 0;
 float3 up=normalize(p);
 float3 localWind=tex2Dlod(_ScrollCloudWindTex,float4(uv,0,0)).xyz;
 float phase=frac(_ScrollWeatherTime*.016);
 float3 a=normalize(up-localWind*phase*8),b=normalize(up+localWind*(1-phase)*8);
 float3 advected=normalize(lerp(a,b,phase));
 float3 q=advected*float3(9,9,9)+float3(.13,.71,.37)+up*(z*.00028);
 float shape=tex3Dlod(_CloudNoise,float4(q,0)).r;
 float4 detail=simpleMode?float4(0,0,0,0):1-tex3Dlod(_CloudDetailNoise,float4(q*2.73+float3(.2,.1,.7),0));
 float4 cells=OrbisHPHighDetail(OrbisWeatherUV(advected));
 // Preserve the HP profile's erosion range. Mapping the cached weather almost
 // directly to one produced broad, fully opaque white plates on the globe.
 float coverage=saturate((weather.r-.12)*.90);
 float low=OrbisHPLowDensityDetailed(z,coverage,saturate(weather.b),shape,detail,cells.r);
 float high=OrbisHPHighDensity(z,weather.g,saturate(weather.b),cells.r,cells.g);
 return float2(low,high)*_Density*max(0,_Cover);
}
#endif
