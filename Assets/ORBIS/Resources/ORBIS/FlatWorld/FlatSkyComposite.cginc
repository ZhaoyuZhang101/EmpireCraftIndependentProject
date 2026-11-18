#ifndef ORBIS_FLAT_SKY_COMPOSITE
#define ORBIS_FLAT_SKY_COMPOSITE
#include "FlatCloudCover.cginc"
#include "../OrbisLightning.cginc"
float3 _FlatSun;
float _SkyReady,_FlatClouds,_FlatCloudShadows,_FlatAtmosphere,_FlatDayNight,_CloudOpacity,_FlatRain,_FlatLightning,_FlatNightLights;
float _FlatNightAdaptation;
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
#include "FlatPrecipitation.cginc"
float3 FlatSkyComposite(float3 col,float2 uv,float4 cloud,float pixelsPerTile,FlatPrecipitation weather,float3 light)
{
    float3 radial=FlatRadial(uv);float day=FlatDayWeight(radial);
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
    float3 cloudColor=lerp(FlatColor(float3(.32,.39,.48)),FlatColor(float3(.90,.94,.97)),day);
    cloudColor*=lerp(1,.74,low);
    col=lerp(col,cloudColor,saturate(alpha));
    // Only airborne precipitation is drawn in front of trees and buildings.
    col=lerp(col,haze,weather.rain*.075);
    col=lerp(col,FlatColor(float3(.75,.84,.91))*light,weather.streak*.63);
    col=lerp(col,FlatColor(float3(.93,.96,1))*light,weather.flake*.85);
    // Brief visual lightning is limited to dense precipitating columns.
    float flash=OrbisLightningAmount(radial)*cloud.a*smoothstep(.65,.92,cloud.r);
    col+=flash*_FlatLightning*_FlatClouds*_SkyReady*float3(.32,.39,.51);
    return col;
}
#endif
