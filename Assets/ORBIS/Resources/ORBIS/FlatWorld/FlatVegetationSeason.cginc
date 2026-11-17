#ifndef ORBIS_FLAT_VEGETATION_SEASON
#define ORBIS_FLAT_VEGETATION_SEASON
UNITY_DECLARE_TEX2D_NOSAMPLER(_VegetationSeasonTex);
float _VegetationSeasonReady;
float4 _VegetationSeason;

float2 FlatVegetationMonth(float2 cell,float month)
{
    float2 size=_MapSize.xy+2;
    float2 origin=float2(month-floor(month/3)*3,floor(month/3))*size;
    float2 coordinate=float2(frac(cell.x*_MapSize.z)*_MapSize.x,clamp(cell.y,0,_MapSize.y))+1;
    return FlatPresentationSample(_VegetationSeasonTex,(origin+coordinate)/(size*float2(3,4)),0).rg;
}
float2 FlatVegetationSeason(float2 cell)
{
    if(_VegetationSeasonReady<.5)return 1;
    return lerp(FlatVegetationMonth(cell,_VegetationSeason.x),FlatVegetationMonth(cell,_VegetationSeason.y),_VegetationSeason.z);
}
float3 FlatSeasonGround(float3 color,float2 cell,float canopy)
{
    float growth=FlatVegetationSeason(cell).x;
    float3 dormant=FlatColor(float3(.46,.40,.25));
    return lerp(color,dormant,(1-growth)*smoothstep(.05,.55,canopy)*.58);
}
#endif
