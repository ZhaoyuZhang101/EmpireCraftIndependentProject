#ifndef ORBIS_NIGHT_LIGHTING_V42
#define ORBIS_NIGHT_LIGHTING_V42
// V4.2 Repair IPCFix1 extension. Included AFTER the active GLtracy optics and _Night.
// No loops, no 3D density fetches, no solar-shadow atlas reuse for the Moon.
float4 _OrbisNightParams;       // enabled, sky strength, airglow strength, cloud attenuation
float4 _OrbisMoon;              // sample->Moon, planet-local XYZ; phase-scaled strength W
float4 _OrbisMoonColor;         // linear RGB; illuminated fraction W (diagnostic)
float4 _OrbisNightSkyColor;     // art-scaled diffuse sky irradiance, linear RGB
float4 _OrbisAirglowColor;      // emitted radiance / planet-radius distance
float4 _OrbisAirglowShell;      // mean radius, Gaussian sigma; not physical Earth km

float OrbisNightWeight(float3 p,float3 sun)
{
    return saturate(_Night)*saturate(_OrbisNightParams.x)*
        (1-smoothstep(-.20,.06,dot(normalize(p),sun)));
}
float OrbisNightColumnTau(float3 p)
{
    if(_Clouds<.5||_LayeredWeather<.5||_OrbisNightParams.w<=0)return 0;
    float2 uv=OrbisWeatherUV(p);
    float4 weather=tex2Dlod(_Weather,float4(uv,0,0));
    float ground=max(1.0,tex2Dlod(_GroundField,float4(uv,0,0)).r);
    float thickness=max(.002,_CloudHeightScale*16000);
    float above=saturate((ground+thickness-length(p))/thickness);
    // Weather alpha is a liquid-water proxy; this is a COLUMN approximation, not
    // a second cloud ray trace or a claim of exact lunar cloud/self shadows.
    float coverage=saturate(weather.r*max(0,_Cover));
    return min(12.0,coverage*(.75+4*saturate(weather.a))*max(0,_Density)*
        above*max(0,_OrbisNightParams.w));
}
float3 OrbisNightAmbient(float3 p,float3 sun,float columnTau)
{
    float night=OrbisNightWeight(p,sun);
    float air=saturate(_Atmosphere)*saturate(max(0,_AirDensity));
    float altitude=1-smoothstep(1.08,ORBIS_AIR_RADIUS,length(p));
    float skyTransmission=.20+.80*exp(-max(0,columnTau)*.55);
    float3 sky=_OrbisNightSkyColor.rgb*max(0,_OrbisNightParams.y);
    // Starlight keeps a weak residual even with the atmosphere visualization off;
    // airglow disappears with atmosphere/air density disabled.
    sky*=lerp(.35,1.0,air);
    float3 glowDown=_OrbisAirglowColor.rgb*(.018*max(0,_OrbisNightParams.z)*air);
    return (sky+glowDown)*night*skyTransmission*lerp(.30,1.0,altitude);
}
float3 OrbisMoonAirTransmittance(float3 p)
{
    float3 moon=_OrbisMoon.xyz;
    if(OrbisSunVisible(p,moon)<.5)return 0; // generic planet visibility, NOT the Sun direction
    if(_Atmosphere<.5||_AirDensity<=0)return 1;
    float r=max(1.0,length(p));
    if(r>=ORBIS_AIR_RADIUS&&dot(p,moon)>=0)return 1;
    float mu=dot(p/r,moon),horizon=-sqrt(saturate(1-1/(r*r)));
    float2 st=float2(sqrt(saturate((mu-horizon)/max(1e-5,1-horizon))),
        sqrt(saturate((r-1)/.20)));
    float2 column;
    if(_AtmoLutReady>.5)
    {
        float2 uv=(st*(_AtmoLutSize.xy-1)+.5)*_AtmoLutSize.zw;
        column=tex2Dlod(_AtmoOpticalDepth,float4(uv,0,0)).rg;
    }
    else
    {
        // Preview-only algebraic fallback; NEVER instantiate the eight-step
        // solar fallback a second time inside the heavy view traversal.
        float2 scale=max(float2(1e-5,1e-5),float2(_AtmoBetaRayleigh.w,_AtmoBetaMie.w));
        column=scale*exp(-(r-1)/scale)/max(.12,mu);
    }
    float3 tau=(column.x*_AtmoBetaRayleigh.xyz+
        column.y*_AtmoBetaMie.xyz*max(0,_Haze)*1.1)*max(0,_AirDensity)*saturate(_Atmosphere);
    return exp(-min(max(0,tau),60));
}
float3 OrbisMoonIncident(float3 p,float3 sun,float columnTau)
{
    float night=OrbisNightWeight(p,sun);
    if(night<=1e-5||_OrbisMoon.w<=1e-6)return 0;
    float mu=dot(normalize(p),_OrbisMoon.xyz);
    float clouds=exp(-min(20.0,max(0,columnTau)/max(.18,mu)));
    return _OrbisMoonColor.rgb*max(0,_OrbisMoon.w)*night*clouds*OrbisMoonAirTransmittance(p);
}
float3 OrbisAirglowEmission(float3 p,float3 sun)
{
    float night=OrbisNightWeight(p,sun);
    if(night<=1e-5||_OrbisNightParams.z<=0||_Atmosphere<.5||_AirDensity<=0)return 0;
    float h=length(p);
    float x=(h-_OrbisAirglowShell.x)/max(.008,_OrbisAirglowShell.y);
    float shell=exp(-.5*x*x)*smoothstep(1.10,1.13,h)*(1-smoothstep(1.185,1.20,h));
    // Emission is independent of direct-Sun visibility AND _SolarEnergy. Existing
    // front-to-back RGB transmission naturally occludes the far side and clouded rays.
    // Keep limb emission deliberately below the night-sky floor. This avoids a saturated
    // planetary ring while preserving a faint upper-atmosphere halo on the dark limb.
    float limbSafe=shell*.42;
    return _OrbisAirglowColor.rgb*max(0,_OrbisNightParams.z)*limbSafe*night*
        saturate(_Atmosphere)*saturate(_AirDensity);
}
#endif
