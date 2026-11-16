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
 // V4.2 Repair's broad cloud body and soft silhouette, driven by the current
 // prognostic weather rather than its old static coverage map. Sample and blend
 // both flow phases; blending their coordinates cancels advection halfway.
 // Sample an isotropic spherical volume. Translating height along global Y
 // tilted every cloud column toward the poles and stretched its silhouette.
 float3 offset=float3(.13,.71,.37);
 float3 q0=a*length(p)*3+offset,q1=b*length(p)*3+offset;
 float shape0=tex3Dlod(_CloudNoise,float4(q0,0)).r-(1-tex3Dlod(_CloudNoise,float4(q0*2.73,0)).g)*.17;
 float shape1=tex3Dlod(_CloudNoise,float4(q1,0)).r-(1-tex3Dlod(_CloudNoise,float4(q1*2.73,0)).g)*.17;
 float shape=lerp(shape0,shape1,phase);
 float macro=lerp(tex3Dlod(_CloudNoise,float4(a*.72+float3(.7,.2,.4),0)).r,
                  tex3Dlod(_CloudNoise,float4(b*.72+float3(.7,.2,.4),0)).r,phase);
 float coverage=saturate(weather.r);
 float threshold=.42-coverage*.17+(1-macro)*.08;
 float fine=.5;
 if(!simpleMode)
 {
  float3 detail=lerp(tex3Dlod(_CloudDetailNoise,float4(q0*6.1,0)).rgb,
                     tex3Dlod(_CloudDetailNoise,float4(q1*6.1,0)).rgb,phase);
  fine=dot(detail,float3(.625,.25,.125));
 }
 float structure=smoothstep(threshold+.07*(.5-fine),threshold+.18,shape)*lerp(.78,1.06,fine);
 float top=lerp(4200,11500,saturate(weather.b));
 float lower=smoothstep(400,1050,z)*(1-smoothstep(top*.70,top,z));
 float upper=smoothstep(5500,7600,z)*(1-smoothstep(10500,13500,z));
 return float2(coverage*structure*lower*1.85,weather.g*structure*upper*.55)*_Density*max(0,_Cover);
}
#endif
