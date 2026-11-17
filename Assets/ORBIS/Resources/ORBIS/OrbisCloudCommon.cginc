#ifndef ORBIS_CLOUD_COMMON
#define ORBIS_CLOUD_COMMON
#include "GeographicCoordinates.cginc"
// Shared by view-ray clouds AND projected terrain/water shadows.
// CloudState controls existence; immutable volume noise only controls small-scale shape.
sampler2D _Weather, _CloudWind, _GroundField, _WeatherEffects;
sampler3D _AtmosLayers, _AtmosLayerWind;
float _LayeredWeather,_CloudHeightScale,_WetSurfaces,_CloudShellTop;
#include "OrbisLightning.cginc"
sampler3D _CloudNoise,_CloudDetailNoise;
float _FlowPhase,_Cover,_Density,_Clouds;
#define ORBIS_PI 3.14159265359
#define ORBIS_CLOUD_BASE 1.0003
#define ORBIS_CLOUD_TOP max(1.060,_CloudShellTop)
// Shorter geometric columns retain their optical depth; matches the C# profile.
#define ORBIS_EXTINCTION 720.0
#include "OrbisHPVolumeCloud.cginc"
float2 OrbisWeatherUV(float3 p)
{
    p=normalize(p);
    float2 uv=OrbisGeographicUV(p);
    if(abs(p.x)+abs(p.z)<1e-7)uv.x=.5;
    return uv;
}
float2 OrbisSphere(float3 origin,float3 direction,float radius)
{
    float b=dot(origin,direction),c=dot(origin,origin)-radius*radius;
    float d=b*b-c;
    if(d<0.0)return float2(1e8,-1e8);
    float h=sqrt(max(0.0,d));return float2(-b-h,-b+h);
}
float OrbisSunVisible(float3 p,float3 sun)
{
    // Offset OUTWARD, not along the sun ray: a light-directed offset can put
    // a night-side surface origin inside the sphere and incorrectly declare it lit.
    float radius=length(p);
    if(radius<.999999)return 0.0;
    float2 hit=OrbisSphere(p+(p/max(radius,1e-6))*.00001,sun,1.0);
    return (hit.y>0.0 && hit.x>=0.0)?0.0:1.0;
}
float OrbisNoiseAtAge(float3 p,float3 velocity,float age)
{
    float3 back=normalize(p-velocity*(age*.014))*length(p);
    float3 q=back*3.0+float3(.13,.71,.37);
    float body=tex3Dlod(_CloudNoise,float4(q,0)).r;
    float detail=tex3Dlod(_CloudNoise,float4(q*2.73,0)).g;
    return body-(1.0-detail)*.17;
}
// Non-uniform physical layer CENTRES, matching OrbisAtmosphereCore.Height.
float OrbisLayerIndex(float z)
{
 if(z<550) return saturate((z-150)/400);
 if(z<1150)return 1+(z-550)/600;
 if(z<1750)return 2+(z-1150)/600;
 if(z<2500)return 3+(z-1750)/750;
 if(z<3500)return 4+(z-2500)/1000;
 if(z<4500)return 5+(z-3500)/1000;
 if(z<5500)return 6+(z-4500)/1000;
 if(z<7000)return 7+(z-5500)/1500;
 if(z<9000)return 8+(z-7000)/2000;
 if(z<11000)return 9+(z-9000)/2000;
 return 10+saturate((z-11000)/3000);
}
float _ScrollCloudSource;
#ifdef ORBIS_SHARED_SCROLL_WEATHER
#include "FlatWorld/ScrollCloudSource.cginc"
#endif
float3 OrbisLightningLight(float3 p)
{
 float flash=OrbisLightningAmount(p)*_Clouds;
 float3 light=0;
 [branch] if(flash>0)
 {
  #ifdef ORBIS_SHARED_SCROLL_WEATHER
  if(_ScrollCloudSource>.5)
  {
   float4 storm=tex2Dlod(_ScrollWeatherTex,float4(OrbisWeatherUV(p),0,0));
   flash*=storm.a*smoothstep(.65,.92,storm.r);
  }
  #endif
  light=flash*float3(.60,.72,1.0);
 }
 return light;
}
float2 OrbisCloudDensities(float3 p,bool simpleMode)
{
 if(_Clouds<.5||length(p)<ORBIS_CLOUD_BASE||length(p)>ORBIS_CLOUD_TOP)return 0;
 #ifdef ORBIS_SHARED_SCROLL_WEATHER
 if(_ScrollCloudSource>.5)return ScrollClimateDensities(p,simpleMode);
 #endif
 if(_LayeredWeather<.5)return 0;
 float3 up=normalize(p);float2 uv=OrbisWeatherUV(up);
 float ground=tex2Dlod(_GroundField,float4(uv,0,0)).r;
 float z=(length(p)-max(1.0,ground))/max(1e-7,_CloudHeightScale);
 if(z<400||z>13500)return 0;
 float3 lookup=float3(uv,(OrbisLayerIndex(z)+.5)/12.0);
 float4 state=tex3Dlod(_AtmosLayers,float4(lookup,0));
 float3 wind=(tex3Dlod(_AtmosLayerWind,float4(lookup,0)).rgb-.5)*.13/.014;
 wind-=up*dot(up,wind);
 float pa=_FlowPhase,pb=frac(pa+.5),wa=.5-.5*cos(2*ORBIS_PI*pa);
 float3 a=normalize(p-wind*((pa-.5)*12*.014));
 float3 b=normalize(p-wind*((pb-.5)*12*.014));
 float shape=lerp(OrbisNoiseAtAge(a,wind,0),OrbisNoiseAtAge(b,wind,0),wa);
 float macro=lerp(tex3Dlod(_CloudNoise,float4(a*.72+float3(.7,.2,.4),0)).r,
                  tex3Dlod(_CloudNoise,float4(b*.72+float3(.7,.2,.4),0)).r,wa);
 float physical=state.r+state.g*.8;
 float cover=saturate(state.b*_Cover);
 float threshold=.50-cover*.15+(1-macro)*.10;
 float structure=smoothstep(threshold,threshold+.19,shape);
 float taper=smoothstep(400,850,z)*(1-smoothstep(10500,13500,z));
 float wispy=saturate(state.g/max(.001,physical));
 float density=physical*cover*structure*1.25*lerp(1,.62,wispy)*taper*_Density;
 // Keep the old broad silhouette while retaining separate ice-cloud lighting.
 float highFraction=wispy*.6;
 return density*float2(1-highFraction,highFraction);
}
float OrbisCloudDensity(float3 p) {float2 density=OrbisCloudDensities(p,false);return density.x+density.y;}
float OrbisCloudShadowDirect(float3 p,float3 sun)
{
 float2 outer=OrbisSphere(p,sun,ORBIS_CLOUD_TOP);
 float2 inner=OrbisSphere(p,sun,ORBIS_CLOUD_BASE);
 float begin=max(0.0,outer.x),end=max(0.0,outer.y),tau=0.0;
 if(end<=begin)return 1;
 float4 ranges=float4(begin,min(end,max(begin,inner.x)),max(begin,inner.y),end);
 if(inner.x>inner.y)ranges=float4(begin,end,end,end);
 // Preserve both shell intervals and the original 16 midpoint samples per interval.
 // Flatten 2 x 16 into one loop; do not remove the far-side or no-cache fallback.
 [loop] [fastopt] for(int shadowIndex=0;shadowIndex<32;shadowIndex++)
 {
  bool first=shadowIndex<16;
  float a=first?ranges.x:ranges.z,b=first?ranges.y:ranges.w;
  if(b<=a)continue;
  int j=first?shadowIndex:shadowIndex-16;
  float ds=(b-a)/16.0;
  tau+=OrbisCloudDensity(p+sun*(a+(j+.5)*ds))*ds*ORBIS_EXTINCTION;
 }
 return exp(-min(tau,16.0));
}
sampler2D _OrbisShadowAtlas;
float _OrbisShadowReady,_OrbisShadowResolution;
float3 _OrbisShadowRight,_OrbisShadowUp,_OrbisShadowSun;
float OrbisShadowSlice(float2 uv,float slice)
{
    float inset=.5/max(1.0,_OrbisShadowResolution);
    uv=clamp(uv,inset,1.0-inset);
    return tex2Dlod(_OrbisShadowAtlas,float4((uv.x+slice)/8.0,uv.y,0,0)).r;
}
float OrbisCloudShadow(float3 p,float3 sun)
{
 if(_Clouds<.5||OrbisSunVisible(p,sun)<.5)return 1.0;
 if(_OrbisShadowReady>=.5)
 {
  float2 xy=float2(dot(p,_OrbisShadowRight),dot(p,_OrbisShadowUp));
  float impact2=dot(xy,xy),outer2=ORBIS_CLOUD_TOP*ORBIS_CLOUD_TOP;
  if(impact2>=outer2)return 1.0;
  float top=sqrt(max(0.0,outer2-impact2));
  float inner2=ORBIS_CLOUD_BASE*ORBIS_CLOUD_BASE;
  float bottom=impact2<inner2?sqrt(max(0.0,inner2-impact2)):-top;
  float z=dot(p,_OrbisShadowSun);
  if(z>=top)return 1.0;
  bool farSide=z<0.0&&impact2<inner2&&length(p)>ORBIS_CLOUD_BASE;
  if(!farSide)
  {
   float layer=saturate((top-z)/max(1e-5,top-bottom))*7.0;
   float lo=floor(layer),hi=min(7.0,lo+1.0);
   float2 uv=xy/(2.0*ORBIS_CLOUD_TOP)+.5;
   float tau=lerp(OrbisShadowSlice(uv,lo),OrbisShadowSlice(uv,hi),layer-lo);
   return exp(-min(max(0.0,tau),16.0));
  }
 }
 // A SINGLE fallback call site serves both missing-atlas and exceptional far-side
 // cases. Previously the compiler had two separate copies of this heavy body.
 return OrbisCloudShadowDirect(p,sun);
}
#endif
