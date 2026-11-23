#ifndef ORBIS_FLAT_TREE_PLACEMENT
#define ORBIS_FLAT_TREE_PLACEMENT
float PDHash(float2 p)
{
    p.x=p.x-floor(p.x/_MapSize.x)*_MapSize.x;
    return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453);
}
float4 PDTreePlacement(float2 id)
{
    float seed=PDHash(id);
    float2 anchor=float2(16+(seed-.5)*16,13+(PDHash(id+71)-.5)*14);
    return float4(floor(anchor)+.5,.78+.30*PDHash(id+float2(29,13)),seed);
}
#endif
