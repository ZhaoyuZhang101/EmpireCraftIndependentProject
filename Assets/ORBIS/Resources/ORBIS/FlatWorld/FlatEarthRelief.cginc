#ifndef ORBIS_FLAT_EARTH_RELIEF
#define ORBIS_FLAT_EARTH_RELIEF
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthPolarBed);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthPolarCoast);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthSurveyMask);
float4 _EarthPolarBed_TexelSize,_EarthSurveyAtPoles;
float _EarthPolarReady;

float FlatSurveyBed(float2 atlas,float lod,float4 limits)
{
    return FlatPresentationSample(_EarthPolarBed,clamp(atlas,limits.xy,limits.zw),lod).r*65535-32768;
}

// The two stereographic charts are baked from ETOPO. q ranges from -tan(pi/8)
// to +tan(pi/8) at the middle of a side. Both axes have the same ground scale.
float FlatEarthRelief(float2 uv,inout float bed,inout float2 gradient,out float2 coast)
{
    coast=0;
    [branch] if(_EarthPolarReady<.5)return 0;
    const float span=.828427124746;
    float longitude=(uv.x-.5)*6.283185307,latitude=(uv.y-.5)*3.141592654;
    float sl,cl,sp,cp;sincos(longitude,sl,cl);sincos(latitude,sp,cp);
    float denominator=1+abs(sp);
    float2 meridian=float2(sl,cl);
    float2 chart=.5+meridian*(cp/(denominator*span));
    float faceStart=step(0,sp)*.5;
    float2 atlas=float2(chart.x*.5+faceStart,chart.y);
    float2 dx=ddx(atlas)*_EarthPolarBed_TexelSize.zw,dy=ddy(atlas)*_EarthPolarBed_TexelSize.zw;
    float lod=clamp(.5*log2(max(1,max(dot(dx,dx),dot(dy,dy)))),0,5);
    float weight=smoothstep(.70710678,.86602540,abs(sp)); // 45 to 60 degrees.
    [branch] if(weight<=0)return 0;
    float2 mask=FlatPresentationSample(_EarthSurveyMask,uv,0).rg;
    float pole=sp<0?_EarthSurveyAtPoles.x:_EarthSurveyAtPoles.y;
    weight*=lerp(pole,mask.r,OrbisPolarRingWeight(uv.y,_MapSize.y));
    [branch] if(weight<=0)return 0;

    float2 texel=exp2(lod)*_EarthPolarBed_TexelSize.xy;
    // At the outer fade, a coarse mip footprint must still stay inside its own hemisphere.
    float4 limits=float4(float2(faceStart,0)+2*texel,float2(faceStart+.5,1)-2*texel);
    float distance=(FlatPresentationSample(_EarthPolarCoast,clamp(atlas,limits.xy,limits.zw),lod).r*255-128)*.25;
    coast.x=distance*(_EarthPolarBed_TexelSize.y*span*denominator)/(6.283185307*_MapSize.z);
    coast.y=weight*smoothstep(0,.35,mask.g)*(1-smoothstep(24,31,abs(distance)));
    float dhx=FlatSurveyBed(atlas+float2(texel.x,0),lod,limits)-FlatSurveyBed(atlas-float2(texel.x,0),lod,limits);
    float dhz=FlatSurveyBed(atlas+float2(0,texel.y),lod,limits)-FlatSurveyBed(atlas-float2(0,texel.y),lod,limits);
    // Transform the chart gradient into metres per equatorial cell along local
    // east and north. The meridian reverses across the two polar charts.
    float2 chartGradient=float2(dhx,dhz)/(2*texel.y*span);
    float2 surveyGradient=float2(dot(chartGradient,float2(cl,-sl)),-sign(sp)*dot(chartGradient,meridian));
    gradient=lerp(gradient,surveyGradient*(6.283185307*_MapSize.z/denominator),weight);
    bed=lerp(bed,FlatSurveyBed(atlas,lod,limits),weight);
    return weight;
}

float FlatEarthRelief(float2 uv,inout float bed,inout float2 gradient)
{
    float2 coast;
    return FlatEarthRelief(uv,bed,gradient,coast);
}

void FlatEarthMarineOptics(float2 coast,float bed,inout float4 optics,inout float wetSupport)
{
    float wet=coast.y*smoothstep(-.02,.10,coast.x);
    if(wet<=0)return;
    if(wetSupport<=1e-6)optics=float4(optics.r,0,0,3);
    optics.r=lerp(optics.r,max(.15,-bed),wet);
    wetSupport=max(wetSupport,wet);
}

void FlatApplyEarthShore(float waterKind,inout float shore,inout float2 coast)
{
    if(abs(waterKind-4)<.5)coast.y=0; // Simulated lake level and extent take precedence.
    shore=lerp(shore,coast.x,coast.y);
}
#endif
