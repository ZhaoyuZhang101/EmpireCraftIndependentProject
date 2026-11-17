#ifndef ORBIS_FLAT_VEGETATION_WIND
#define ORBIS_FLAT_VEGETATION_WIND

UNITY_DECLARE_TEX2D_NOSAMPLER(_VegetationWindTex);
float _VegetationWindReady,_VegetationWindBlend;

// The existing monthly wind field supplies direction and strength. Spherical
// gust phases stay attached to the world through camera moves and date-line wraps.
float2 PDVegetationWind(float2 id,float seed)
{
    if(_VegetationWindReady<.5)return 0;
    float2 uv=float2(frac((id.x+.5)*_MapSize.z),(id.y+.5)*_MapSize.w);
    float4 months=FlatPresentationSample(_VegetationWindTex,uv,0);
    float2 flow=lerp(months.rg,months.ba,saturate(_VegetationWindBlend));
    flow*=min(1,1.5/max(.001,length(flow)));
    float phase=dot(FlatRadial(uv),float3(11,7,13));
    float gust=.62+.38*sin(_SkyClock*1.45-phase+seed*.9)
                   +.16*sin(_SkyClock*2.43+seed*6.283185307);
    float flutter=.18*sin(_SkyClock*2.1+phase+seed*6.283185307);
    float2 sway=flow*(1.4*gust)+float2(-flow.y,flow.x)*flutter;
    return sway*min(1,1.8/max(.001,length(sway)));
}

#endif
