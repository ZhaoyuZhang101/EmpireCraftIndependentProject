#ifndef ORBIS_FLAT_EARTH_RELIEF
#define ORBIS_FLAT_EARTH_RELIEF
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthPolarBed);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthPolarCoast);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthSurveyMask);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthLakeShore);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthLakeDetail);
float4 _EarthPolarBed_TexelSize,_EarthSurveyAtPoles;
float4 _EarthLakeShore_TexelSize,_EarthLakeDetail_TexelSize;
float _EarthPolarReady,_EarthLakeReady;

// Two R8 texels at exact texel centres encode each one-based detail page.
// Bilinear reads on a page use the identical geographic samples in its gutter.
float FlatEarthLakeDetail(float2 uv,float coarse,float footprint)
{
    [branch] if(_EarthLakeDetail_TexelSize.z<4096 || footprint>=1 || abs(coarse)>=3.75) return coarse;
    float2 location=float2(frac(uv.x)*1024,min(saturate(uv.y)*512,511.99997));
    float2 tile=floor(location);
    float index=2*(tile.y*1024+tile.x);
    float2 header=float2(fmod(index,4096),floor(index/4096))+.5;
    float2 texel=_EarthLakeDetail_TexelSize.xy;
    float lo=round(FlatPresentationSample(_EarthLakeDetail,header*texel,0).r*255);
    float hi=round(FlatPresentationSample(_EarthLakeDetail,(header+float2(1,0))*texel,0).r*255);
    float slot=lo+256*hi-1;
    [branch] if(slot<0)return coarse;
    float2 origin=float2(fmod(slot,62)*66,256+floor(slot/62)*66);
    float2 address=(origin+1+frac(location)*64)*texel;
    float fine=(FlatPresentationSample(_EarthLakeDetail,address,0).r*255-128)/32;
    float weight=(1-smoothstep(.22,1,footprint))*(1-smoothstep(2.5,3.75,abs(coarse)));
    return lerp(coarse,fine,weight);
}

// Distance is measured along the ground, in equatorial survey texels. Only
// the lookup uses longitude/latitude. A bank therefore keeps its physical
// width on a rotated local chart at high latitude.
float FlatEarthLake(float2 uv,float kind,inout float shore)
{
    if(_EarthLakeReady<.5)return 0;
    float2 dx=ddx(uv),dy=ddy(uv);
    dx.x-=round(dx.x);dy.x-=round(dy.x);
    float texels=max(length(dx*_EarthLakeShore_TexelSize.zw),length(dy*_EarthLakeShore_TexelSize.zw));
    float lod=clamp(log2(max(1,texels))-.45,0,4);
    float distance=(FlatPresentationSample(_EarthLakeShore,uv,lod).r*255-128)*.25;
    if(distance<=-31)return 0;
    float2 groundMetric=float2(max(.01,cos((uv.y-.5)*3.14159265)),.5)*8192;
    distance=FlatEarthLakeDetail(uv,distance,max(length(dx*groundMetric),length(dy*groundMetric)));
    float2 mask=FlatPresentationSample(_EarthSurveyMask,uv,0).rg;
    // Keep an adjacent marine coastline on its own surveyed or modelled field.
    float marine=max(mask.g,step(.5,kind)*step(kind,3.5));
    float weight=mask.r*smoothstep(-31,-20,distance)*lerp(1,smoothstep(-1,.25,distance),marine);
    float cells=distance*_MapSize.x*_EarthLakeShore_TexelSize.x;
    shore=lerp(shore,cells,weight);
    return weight*smoothstep(-.5,.5,distance);
}

void FlatEarthLakeOptics(float lake,float shore,inout float4 basin,inout float wetSupport)
{
    if(lake<=0)return;
    // Thin or sub-cell parts of a real lake may have no simulated water-depth
    // sample. Give only those pixels a shallow optical bed below the receiver.
    float surveyedOnly=1-saturate(wetSupport*4)*saturate(basin.a-3);
    basin.r=lerp(basin.r,clamp(.4+max(0,shore)*18,.4,18),lake*surveyedOnly);
    basin.gb=lerp(basin.gb,float2(0,1),lake*surveyedOnly);
    basin.a=lerp(basin.a,4,lake);
    wetSupport=max(wetSupport,lake);
}

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
