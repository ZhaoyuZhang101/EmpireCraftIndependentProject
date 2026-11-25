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
    if(_CycloneNorth.w+_CycloneSouth.w<=.025)return 0;
    float3 up,east,north;
    FlatWindBasis(uv,up,east,north);
    float3 circulation=FlatCycloneSurfaceWind(up,_CycloneNorth,1)+FlatCycloneSurfaceWind(up,_CycloneSouth,-1);
    return float2(dot(circulation,east),dot(circulation,north));
}

#ifdef ORBIS_USE_FRAG_CYCLONE_WIND
static float2 gFragCycloneWind;
#endif

// Reuses the once-per-fragment cyclone wind inside LocalSurface/ScrollSurface/FlatMap loops
// (cyclone radius is ~18 tiles vs +/-1.5 tile local neighborhood), keeping standalone probes exact.
float2 FlatCurrentCycloneWind(float2 uv)
{
#ifdef ORBIS_USE_FRAG_CYCLONE_WIND
    return gFragCycloneWind;
#else
    return FlatCycloneLocalWind(uv);
#endif
}

// Combined seasonal + published cyclone wind for vegetation, settlements, rain and wildlife.
float2 FlatSurfaceWindField(float2 uv,out float stormIntensity)
{
    float2 seasonal=FlatSeasonalWind(uv);
    float2 cyclone=FlatCurrentCycloneWind(uv);
    stormIntensity=smoothstep(.10,1.25,length(cyclone));
    return seasonal+cyclone;
}

// Spherical gust phases stay attached through camera moves and date-line wraps.
// Branchless storm blend preserves exact 27369b5 calm math with zero extra sin() or CFG splits.
float2 PDVegetationWind(float2 id,float seed)
{
    float2 uv=float2(frac((id.x+.5)*_MapSize.z),(id.y+.5)*_MapSize.w);
    float2 cyclone=FlatCurrentCycloneWind(uv);
    float storm=smoothstep(.10,1.25,length(cyclone));
    if(_VegetationWindReady<.5&&storm<.001)return 0;
    float2 flow=FlatSeasonalWind(uv)+cyclone;
    float phase=dot(FlatRadial(uv),float3(11,7,13));
    float gust=lerp(.62,.98,storm)+lerp(.38,.26,storm)*sin(_SkyClock*lerp(1.45,3.8,storm)-phase*(1+storm)+seed*.9)
               +.16*sin(_SkyClock*lerp(2.43,5.2,storm)+seed*6.283185307);
    float flutter=lerp(.18,.30,storm)*sin(_SkyClock*lerp(2.1,4.7,storm)+phase+seed*6.283185307);
    float2 sway=flow*(lerp(1.4,1.92,storm)*gust)+float2(-flow.y,flow.x)*flutter;
    return sway*min(1,lerp(1.8,2.85,storm)/max(.001,length(sway)));
}

// Compact settlement wind shares the exact published cyclone field without redundant trig inside loops.
float2 PDSettlementWind(float2 id,float seed,out float storm)
{
    float2 uv=float2(frac((id.x+.5)*_MapSize.z),(id.y+.5)*_MapSize.w);
    float2 flow=FlatSurfaceWindField(uv,storm);
    if(_VegetationWindReady<.5&&storm<.001)return 0;
    float cap=lerp(1.5,2.6,storm);
    return flow*min(1,cap/max(.001,length(flow)));
}

#endif


