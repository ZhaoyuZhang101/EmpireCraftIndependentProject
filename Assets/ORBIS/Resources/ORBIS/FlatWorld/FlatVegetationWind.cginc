#ifndef ORBIS_FLAT_VEGETATION_WIND
#define ORBIS_FLAT_VEGETATION_WIND
#include "FlatCycloneWind.cginc"

UNITY_DECLARE_TEX2D_NOSAMPLER(_VegetationWindTex);
float _VegetationWindReady,_VegetationWindBlend;

// East/north flow in the current month pair, shared by rain and vegetation.
float2 FlatSeasonalWind(float2 uv)
{
    if(_VegetationWindReady<.5)return 0;
    float4 months=FlatPresentationSample(_VegetationWindTex,float2(frac(uv.x),saturate(uv.y)),0);
    float2 flow=lerp(months.rg,months.ba,saturate(_VegetationWindBlend));
    return flow*min(1,1.5/max(.001,length(flow)));
}

// Compute unit sphere radial + local east/north basis with a single sincos pair.
void FlatWindBasis(float2 uv,out float3 up,out float3 east,out float3 north)
{
    float sLon,cLon,sLat,cLat;
    sincos((uv.x-.5)*6.283185307,sLon,cLon);
    sincos((uv.y-.5)*3.141592654,sLat,cLat);
    up=float3(sLon*cLat,sLat,cLon*cLat);
    east=float3(cLon,0,-sLon);
    north=float3(-sLat*sLon,cLat,-sLat*cLon);
}

// Published surface cyclone flow projected into local east/north coordinates (zero samplers).
float2 FlatCycloneLocalWind(float2 uv)
{
    [branch] if(_CycloneNorth.w+_CycloneSouth.w<=.025)return 0;
    float3 up,east,north;
    FlatWindBasis(uv,up,east,north);
    float3 circulation=FlatCycloneSurfaceWind(up,_CycloneNorth,1)+FlatCycloneSurfaceWind(up,_CycloneSouth,-1);
    return float2(dot(circulation,east),dot(circulation,north));
}

// Combined seasonal + published cyclone wind for vegetation, settlements, rain and wildlife.
float2 FlatSurfaceWindField(float2 uv,out float stormIntensity,out float3 radial)
{
    float2 seasonal=FlatSeasonalWind(uv);
    float3 east,north;
    FlatWindBasis(uv,radial,east,north);
    float2 cyclone=0;
    [branch] if(_CycloneNorth.w+_CycloneSouth.w>.025)
    {
        float3 circulation=FlatCycloneSurfaceWind(radial,_CycloneNorth,1)+FlatCycloneSurfaceWind(radial,_CycloneSouth,-1);
        cyclone=float2(dot(circulation,east),dot(circulation,north));
    }
    stormIntensity=smoothstep(.10,1.25,length(cyclone));
    return seasonal+cyclone;
}

float2 FlatSurfaceWindField(float2 uv,out float stormIntensity)
{
    float3 radial;
    return FlatSurfaceWindField(uv,stormIntensity,radial);
}

// Spherical gust phases stay attached through camera moves and date-line wraps.
float2 PDVegetationWind(float2 id,float seed)
{
    bool hasCyclone=(_CycloneNorth.w+_CycloneSouth.w)>.025;
    [branch] if(_VegetationWindReady<.5&&!hasCyclone)return 0;
    float2 uv=float2(frac((id.x+.5)*_MapSize.z),(id.y+.5)*_MapSize.w);
    float storm;float3 radial;
    float2 flow=FlatSurfaceWindField(uv,storm,radial);
    [branch] if(dot(flow,flow)<1e-6)return 0;
    float phase=dot(radial,float3(11,7,13));
    float gust=.62+.38*sin(_SkyClock*1.45-phase+seed*.9)
                   +.16*sin(_SkyClock*2.43+seed*6.283185307);
    float flutter=.18*sin(_SkyClock*2.1+phase+seed*6.283185307);
    [branch] if(storm>.001)
    {
        // Traveling squall bands surge downwind during landfalling storms and converge cleanly when calm.
        float squallPhase=dot(radial,float3(29,17,23))-_SkyClock*(3.2+2.2*storm);
        float squall=.98+.26*sin(squallPhase+seed*1.9)+.14*sin(squallPhase*1.79+seed*6.283185307);
        float crossBuffet=.30*sin(_SkyClock*4.7+phase*1.3+seed*6.283185307);
        gust=lerp(gust,squall,storm);
        flutter=lerp(flutter,crossBuffet,storm);
    }
    float2 sway=flow*(lerp(1.4,1.92,storm)*gust)+float2(-flow.y,flow.x)*flutter;
    float maxSway=lerp(1.8,2.85,storm);
    return sway*min(1,maxSway/max(.001,length(sway)));
}

// Settlement architectural wind shares the exact published cyclone field with faster fabric flutter.
float2 PDSettlementWind(float2 id,float seed,out float storm)
{
    storm=0;
    bool hasCyclone=(_CycloneNorth.w+_CycloneSouth.w)>.025;
    [branch] if(_VegetationWindReady<.5&&!hasCyclone)return 0;
    float2 uv=float2(frac((id.x+.5)*_MapSize.z),(id.y+.5)*_MapSize.w);
    float3 radial;
    float2 flow=FlatSurfaceWindField(uv,storm,radial);
    float mag=length(flow);
    [branch] if(mag<1e-4)return 0;
    float phase=dot(radial,float3(17,11,19))+seed*6.283185307;
    float gust=.78+.22*sin(_SkyClock*(1.8+2.6*storm)-phase)+.14*sin(_SkyClock*(4.1+3.2*storm)+phase*1.7);
    float2 dir=flow/mag;
    float2 perp=float2(-dir.y,dir.x);
    float2 combined=flow*gust+perp*(mag*storm*.22*sin(_SkyClock*6.3+phase));
    float cap=lerp(1.5,2.6,storm);
    return combined*min(1,cap/max(.001,length(combined)));
}

#endif
