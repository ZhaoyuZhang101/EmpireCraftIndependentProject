#ifndef ORBIS_FLAT_LAKE_ICE
#define ORBIS_FLAT_LAKE_ICE
#include "FlatPolarIce.cginc"

UNITY_DECLARE_TEX2D_NOSAMPLER(_LakeIceTex);
float4 _LakeIceTex_TexelSize,_LakeIceSeason;

// Evaluate before the surveyed lake changes the underlying basin kind. A
// river can cross a dry tile beside a lake: only the basin polygon freezes.
float FlatLakeIceReceiver(float kind,float wetSupport,float surveyLake,float shore,float aa)
{
    float modelLake=step(1e-6,wetSupport)*saturate(kind-3)*step(kind,4.001);
    return _LakeIceSeason.w*max(modelLake,surveyLake)*smoothstep(-aa,aa,shore);
}

float FlatLakeIceMonth(float2 uv,float month)
{
    float panel=floor(month*.25);
    // Clamp inside the selected four-month panel at both poles.
    float halfRow=1.5*_LakeIceTex_TexelSize.y;
    float v=clamp(uv.y,halfRow,1-halfRow);
    float4 months=FlatPresentationSample(_LakeIceTex,float2(uv.x,(v+panel)/3),0);
    return months[(int)(month-4*panel)];
}

float FlatLakeIceCover(float2 uv,float3 radial,float depth,float shore,float pixelKm)
{
    float a=FlatLakeIceMonth(uv,_LakeIceSeason.x),b=FlatLakeIceMonth(uv,_LakeIceSeason.y);
    float temperature=(255*lerp(a,b,_LakeIceSeason.z)-128)*.25+dot(_ClimateYearHeat,float4(radial,1));
    float cold=-temperature;
    if(cold<=.20)return 0;
    if(cold>=5.5)return 1;
    // Shallow margins lose their stored heat first. Use physical shore
    // distance so zooming or changing the grid resolution cannot move the ice.
    float offshore=smoothstep(.12,6,max(0,shore)*40030.2*_MapSize.z);
    float exposure=offshore*lerp(.32,1,smoothstep(1,38,depth));
    float irregular=(FlatIceNoise(radial*940)-.5)*.9*(1-smoothstep(1.4,6.8,pixelKm));
    float onset=max(.20,.55+2.15*exposure+irregular);
    return smoothstep(onset,onset+1.00+1.25*exposure,cold);
}

float3 FlatLakeIceColor(float3 radial,float pixelKm)
{
    // Static frost and muted fissures stay fixed to the shore while liquid
    // waves move in the remaining open water.
    float fine=1-smoothstep(.8,4,pixelKm);
    float frost=lerp(FlatIceNoise(radial*370),.5,smoothstep(4,17,pixelKm));
    float grain=lerp(.5,FlatIceNoise(radial*1600),fine);
    float crack=(1-smoothstep(.012,.048,abs(grain-.47-.06*(frost-.5))))*fine;
    float3 ice=lerp(float3(.58,.76,.81),float3(.91,.95,.94),.16+.51*frost+.33*grain);
    return FlatColor(ice*(1-.09*crack));
}
#endif
