#ifndef ORBIS_FLAT_VEGETATION_WIND
#define ORBIS_FLAT_VEGETATION_WIND

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

// Spherical gust phases stay attached through camera moves and date-line wraps.
float2 PDVegetationWind(float2 id,float seed)
{
    if(_VegetationWindReady<.5)return 0;
    float2 uv=float2(frac((id.x+.5)*_MapSize.z),(id.y+.5)*_MapSize.w);
    float2 flow=FlatSeasonalWind(uv);
    float phase=dot(FlatRadial(uv),float3(11,7,13));
    float gust=.62+.38*sin(_SkyClock*1.45-phase+seed*.9)
                   +.16*sin(_SkyClock*2.43+seed*6.283185307);
    float flutter=.18*sin(_SkyClock*2.1+phase+seed*6.283185307);
    float2 sway=flow*(1.4*gust)+float2(-flow.y,flow.x)*flutter;
    return sway*min(1,1.8/max(.001,length(sway)));
}

#endif
