#ifndef ORBIS_FLAT_SKY_COMPOSITE
#define ORBIS_FLAT_SKY_COMPOSITE
#include "FlatCloudCover.cginc"
float3 _FlatSun;
float _SkyReady,_FlatClouds,_FlatCloudShadows,_FlatAtmosphere,_FlatDayNight,_CloudOpacity,_FlatRain,_FlatLightning,_FlatNightLights;
float _FlatNightAdaptation;
float4 _FlatLightningEvents[4];
// Same moon ephemeris and linear light colors as the spherical view.
float4 _OrbisNightParams,_OrbisMoon,_OrbisMoonColor,_OrbisNightSkyColor,_OrbisAirglowColor;
float FlatDayWeight(float3 radial)
{ return lerp(1,smoothstep(-.13,.18,dot(radial,normalize(_FlatSun+float3(0,1e-7,0)))),_FlatDayNight); }
float3 FlatDayLight(float3 radial,float2 uv)
{
    float day=FlatDayWeight(radial);
    float4 overhead=FlatCloudColumn(uv);
    float cover=saturate(overhead.r*.8+overhead.g*.25)*_FlatCloudShadows*_SkyReady;
    float moonAltitude=saturate(dot(radial,_OrbisMoon.xyz));
    float moonTransmission=exp(-cover*max(0,_OrbisNightParams.w)*2/max(.20,moonAltitude));
    // Gradual dark adaptation while unfolding; ambient light retains map readability
    // at new moon. Direct moonlight still vanishes below the local horizon.
    float adaptation=lerp(1,2.5,saturate(_FlatNightAdaptation));
    float3 sky=(_OrbisNightSkyColor.rgb*_OrbisNightParams.y*adaptation+
                _OrbisAirglowColor.rgb*_OrbisNightParams.z*.15)*(1-cover*.25);
    float3 moon=_OrbisMoonColor.rgb*_OrbisMoon.w*moonAltitude*moonTransmission;
    float3 nightLight=(sky+moon)*saturate(_OrbisNightParams.x);
    #ifdef UNITY_COLORSPACE_GAMMA
    // Flat map artwork is composed in display space, then converted to linear by
    // the scroll surface. Applying a linear 0.05 directly to sRGB albedo crushed
    // that light a second time. Encode the irradiance before that multiplication.
    nightLight=LinearToGammaSpace(max(0,nightLight));
    #endif
    return lerp(nightLight,float3(.94,.97,1),day);
}
float3 FlatSkyComposite(float3 col,float2 uv,float2 cell,float waterMask,float pixelsPerTile)
{
    float3 radial=FlatRadial(uv);float day=FlatDayWeight(radial);
    float4 cloud=FlatCloudColumn(uv);
    // Black translucent cloud shadow, shared with the spherical terrain.
    float shade=FlatCloudShadowOpacity(uv,normalize(_FlatSun),_FlatCloudShadows*_SkyReady);
    col*=1-shade*.52*day;
    float atmosphereAmount=_FlatAtmosphere*lerp(.045,.015,smoothstep(2,16,pixelsPerTile));
    float3 haze=lerp(FlatColor(float3(.13,.19,.28)),FlatColor(float3(.53,.67,.77)),day);
    col=lerp(col,haze,atmosphereAmount);
    float dusk=exp(-abs(dot(radial,_FlatSun))/.065)*_FlatDayNight*_FlatAtmosphere;
    col+=FlatColor(float3(.36,.20,.12))*dusk*.025;
    float low=cloud.r,high=cloud.g;
    float alpha=(1-(1-low*.82)*(1-high*.35))*_CloudOpacity*_FlatClouds*_SkyReady;
    alpha*=1-smoothstep(16,56,pixelsPerTile)*.86;
    float3 cloudColor=lerp(FlatColor(float3(.22,.29,.41)),FlatColor(float3(.90,.94,.97)),day);
    cloudColor*=lerp(.74,1,cloud.b);
    col=lerp(col,cloudColor,saturate(alpha));
    #ifdef ORBIS_SCROLL_SURFACE
    // The camera crosses the HP cloud shell during globe-to-map unfolding.
    // Fog follows local cloud column density and clears at both endpoints.
    float passage=sin(3.141592654*saturate((_ScrollMorph-.28)/.64));
    passage=passage*passage*saturate(low+high*.35)*_FlatClouds*_SkyReady;
    col=lerp(col,FlatColor(float3(.75,.81,.84)),passage*.32);
    #endif
    float rain=cloud.a*_FlatRain*_SkyReady*smoothstep(3,8,pixelsPerTile);
    float2 drop=cell*float2(17,4)+float2(_SkyClock*.45,_SkyClock*8);
    float streak=(1-smoothstep(.035,.12,abs(frac(drop.x)-.5)))*smoothstep(.45,.95,frac(drop.y));
    col=lerp(col,haze,streak*rain*.20);
    // Only physical lightning events emitted by mature, charged rain clouds.
    float flash=0;
    [unroll] for(int k=0;k<4;k++)
    {float3 d=radial-_FlatLightningEvents[k].xyz;flash+=_FlatLightningEvents[k].w*exp(-dot(d,d)/.00055);}
    col+=flash*_FlatLightning*_SkyReady*float3(.32,.39,.51);
    return col;
}
#endif
