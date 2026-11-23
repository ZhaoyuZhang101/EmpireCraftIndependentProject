#ifndef ORBIS_FLAT_VEGETATION_SEASON
#define ORBIS_FLAT_VEGETATION_SEASON
UNITY_DECLARE_TEX2D_NOSAMPLER(_VegetationSeasonTex);
float _VegetationSeasonReady;
float4 _VegetationSeason;
float4 _ClimateYearHeat,_ClimateYearRain;

float4 FlatVegetationMonth(float2 cell,float month)
{
    if(_VegetationSeason.w>.5)
    {
        float panel=month-floor(month*.5)*2;
        float v=clamp(cell.y,.5,_MapSize.y-.5)*_MapSize.w;
        return FlatPresentationSample(_VegetationSeasonTex,float2(frac(cell.x*_MapSize.z),(panel+v)*.5),0);
    }
    float2 size=_MapSize.xy+2;
    float2 origin=float2(month-floor(month/3)*3,floor(month/3))*size;
    float2 coordinate=float2(frac(cell.x*_MapSize.z)*_MapSize.x,clamp(cell.y,0,_MapSize.y))+1;
    return FlatPresentationSample(_VegetationSeasonTex,(origin+coordinate)/(size*float2(3,4)),0);
}
float4 FlatSurfaceSeason(float2 cell,float3 radial)
{
    if(_VegetationSeasonReady<.5)return float4(1,1,0,.5);
    float4 state=lerp(FlatVegetationMonth(cell,_VegetationSeason.x),FlatVegetationMonth(cell,_VegetationSeason.y),_VegetationSeason.z);
    float heat=dot(_ClimateYearHeat,float4(radial,1)),rain=dot(_ClimateYearRain.xyz,radial);
    float balance=.8*rain-.035*heat;
    state.a+=balance*state.a*(1-.55*state.a);
    state.r+=1.7*balance*state.r*(1-state.r);
    state.g+=balance*state.g*(1-state.g);
    state.b+=(rain-.19*heat)*state.b*(1-state.b)*1.8;
    return saturate(state);
}
float2 FlatVegetationSeason(float2 cell)
{
    return FlatSurfaceSeason(cell,FlatRadial(cell*_MapSize.zw)).rg;
}
float3 FlatSeasonGround(float3 color,float canopy,float4 season)
{
    if(_VegetationSeasonReady<.5)return color;
    float cover=saturate(canopy*1.7+season.g*.30+season.r*.25);
    float dry=(1-season.a)*(1-season.r)*cover;
    color=lerp(color,FlatColor(float3(.56,.49,.32)),dry*.46);
    float green=season.r*season.a*cover;
    // Retain the source soil and rock texture under the live ground cover.
    return color*lerp(1,float3(.86,1.09,.91),green);
}
#endif
