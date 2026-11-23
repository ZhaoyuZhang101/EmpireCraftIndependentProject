#ifndef ORBIS_FLAT_EARTH_RELIEF
#define ORBIS_FLAT_EARTH_RELIEF
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthPolarBed);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthPolarCoast);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthSurveyMask);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthLakeShore);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthLakeDetail);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthStraitShore);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthStraitDetail);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthOceanDirectory);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthOceanPages);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthMarineDepth);
float4 _EarthPolarBed_TexelSize,_EarthSurveyAtPoles;
float4 _EarthLakeShore_TexelSize,_EarthLakeDetail_TexelSize;
float4 _EarthStraitShore_TexelSize,_EarthStraitDetail_TexelSize;
float4 _EarthOceanDirectory_TexelSize,_EarthOceanPages_TexelSize;
float4 _EarthMarineDepth_TexelSize;
float _EarthPolarReady,_EarthLakeReady,_EarthStraitReady;
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthLandOverview);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthLandDirectory);
UNITY_DECLARE_TEX2D_NOSAMPLER(_EarthLandPages);
float4 _EarthLandOverview_TexelSize,_EarthLandPages_TexelSize;

// Page IDs are two separate bytes. Filtering either byte can redirect a whole
// geographic tile (including half of Taihu) to its neighbour's shoreline.
float FlatEarthLakePage(float2 pixel)
{
    #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
    int2 p=(int2)pixel;
    float2 bytes=float2(_EarthLakeDetail.Load(int3(p,0)).r,_EarthLakeDetail.Load(int3(p+int2(1,0),0)).r);
    #else
    float2 texel=_EarthLakeDetail_TexelSize.xy;
    float2 bytes=float2(tex2Dlod(_EarthLakeDetail,float4((pixel+.5)*texel,0,0)).r,
                        tex2Dlod(_EarthLakeDetail,float4((pixel+float2(1.5,.5))*texel,0,0)).r);
    #endif
    return dot(round(bytes*255),float2(1,256))-1;
}

float FlatEarthLakeCode(float2 address)
{
    float2 texel=_EarthLakeDetail_TexelSize.xy;
    #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
    return FlatPresentationSample(_EarthLakeDetail,address*texel,0).r;
    #else
    // Combined-sampler backends bind this packed texture with point filtering.
    // Interpolate only the geographic distance, including the shared page gutter.
    float2 pixel=floor(address-.5)+.5,t=frac(address-.5);
    float a=tex2Dlod(_EarthLakeDetail,float4(pixel*texel,0,0)).r;
    float b=tex2Dlod(_EarthLakeDetail,float4((pixel+float2(1,0))*texel,0,0)).r;
    float c=tex2Dlod(_EarthLakeDetail,float4((pixel+float2(0,1))*texel,0,0)).r;
    float d=tex2Dlod(_EarthLakeDetail,float4((pixel+1)*texel,0,0)).r;
    return lerp(lerp(a,b,t.x),lerp(c,d,t.x),t.y);
    #endif
}

float FlatEarthLakeDetail(float2 uv,float coarse,float footprint)
{
    [branch] if(_EarthLakeDetail_TexelSize.z<4096 || footprint>=1 || abs(coarse)>=3.75) return coarse;
    float2 location=float2(frac(uv.x)*1024,min(saturate(uv.y)*512,511.99997));
    float2 tile=floor(location);
    float index=2*(tile.y*1024+tile.x);
    float width=_EarthLakeDetail_TexelSize.z;
    float row=floor(index/width);
    float slot=FlatEarthLakePage(float2(index-row*width,row));
    [branch] if(slot<0)return coarse;
    float columns=floor(width/66);
    row=floor((slot+.5)/columns);
    float2 origin=float2((slot-row*columns)*66,1048576/width+row*66);
    float fine=(FlatEarthLakeCode(origin+1+frac(location)*64)*255-128)/32;
    float weight=(1-smoothstep(.22,1,footprint))*(1-smoothstep(2.5,3.75,abs(coarse)));
    return lerp(coarse,fine,weight);
}

// Distance is measured along the ground, in equatorial survey texels. Only
// the lookup uses longitude/latitude. A bank therefore keeps its physical
// width on a rotated local chart at high latitude.
float FlatEarthLakeAt(float2 uv,float kind,float strait,float lod,float footprint,inout float shore,out float weight)
{
    weight=0;
    if(_EarthLakeReady<.5)return 0;
    float distance=(FlatPresentationSample(_EarthLakeShore,uv,lod).r*255-128)*.25;
    if(distance<=-31)return 0;
    distance=FlatEarthLakeDetail(uv,distance,footprint);
    float2 mask=FlatPresentationSample(_EarthSurveyMask,uv,0).rg;
    // Keep an adjacent marine coastline on its own surveyed or modelled field.
    float straitSea=strait*smoothstep(-.5,0,shore*40030.2*_MapSize.z);
    float marine=max(straitSea,max(mask.g,step(.5,kind)*step(kind,3.5)));
    weight=mask.r*smoothstep(-31,-20,distance)*lerp(1,smoothstep(-1,.25,distance),marine);
    float cells=distance*_MapSize.x*_EarthLakeShore_TexelSize.x;
    // Between a lake and a surveyed marine bank, preserve the nearer shore
    // on dry ground. Replacing the marine distance with a faraway lake's
    // value at a semantic tile edge creates a spurious beach gradient.
    cells=lerp(cells,max(shore,cells),strait);
    shore=lerp(shore,cells,weight);
    return weight*smoothstep(-.5,.5,distance);
}

float FlatEarthLake(float2 uv,float kind,float strait,inout float shore)
{
    if(_EarthLakeReady<.5)return 0;
    float2 dx=ddx(uv),dy=ddy(uv);
    dx.x-=round(dx.x);dy.x-=round(dy.x);
    float texels=max(length(dx*_EarthLakeShore_TexelSize.zw),length(dy*_EarthLakeShore_TexelSize.zw));
    float lod=clamp(log2(max(1,texels))-.45,0,4);
    float2 metric=float2(max(.01,cos((uv.y-.5)*3.14159265)),.5)*8192;
    float weight;
    return FlatEarthLakeAt(uv,kind,strait,lod,max(length(dx*metric),length(dy*metric)),shore,weight);
}

void FlatEarthLakeOptics(float lake,float shore,inout float4 basin,inout float wetSupport)
{
    if(lake<=0)return;
    // Normalize over water that exists at this receiver. A land or river grid
    // tile contributes no basin optics, even where the surveyed lake is narrow.
    float lakeMix=lake/(lake+(1-lake)*saturate(wetSupport));
    float support=saturate(wetSupport*4)*saturate(basin.a-3);
    // A lake surface in an elevation survey often has exactly the same height
    // as its water level. FlatTerrainAnalysis gives those wet cells a 0.25 m
    // floor. Join them to unsampled lake areas with one optical shore profile.
    float shoreKm=max(0,shore)*40030.2/max(1,_MapSize.x);
    float opticalDepth=.35+2.1*(1-exp(-.28*shoreKm))+.10*min(110,shoreKm);
    float depthSupport=support*smoothstep(.25,.50,basin.r);
    basin.r=lerp(basin.r,opticalDepth,lakeMix*(1-depthSupport));
    basin.gb=lerp(basin.gb,float2(0,1),lakeMix*(1-support));
    basin.a=lerp(basin.a,4,lakeMix);
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

float FlatEarthOverviewHeight(float2 uv,float lod)
{
    return max(-512,FlatPresentationSample(_EarthLandOverview,uv,lod).r*65535-32768);
}

// x is height in metres. yz are geographic height differences across the
// selected sampling interval. Address the centres of all textures explicitly.
float3 FlatEarthOverviewLand(float2 uv,float lod)
{
    float2 offset=.5*exp2(lod)*_EarthLandOverview_TexelSize.xy;
    float east=FlatEarthOverviewHeight(uv+float2(offset.x,0),lod)-FlatEarthOverviewHeight(uv-float2(offset.x,0),lod);
    float north=FlatEarthOverviewHeight(uv+float2(0,offset.y),lod)-FlatEarthOverviewHeight(uv-float2(0,offset.y),lod);
    float perKm=_EarthLandOverview_TexelSize.z/(40030.2*exp2(lod));
    return float3(FlatEarthOverviewHeight(uv,lod),float2(east,north)*perKm);
}

bool FlatEarthLandPage(float2 uv,out float2 address,out float2 heightRange)
{
    address=0;heightRange=0;
    float2 region=float2(frac(uv.x)*360,clamp(uv.y*180-30,0,119.99999));
    float2 tile=floor(region);
    float4 entry=round(FlatPresentationSample(_EarthLandDirectory,(tile+.5)/float2(360,120),0)*255);
    float slot=entry.r+256*entry.g-1;
    if(slot<0)return false;
    float word=entry.b+256*entry.a,exponent=floor(word/8192);
    heightRange=float2(word-exponent*8192-1024,exp2(exponent)*255);
    // A degree contains 60 original samples plus one duplicate from each
    // neighbour. Both pages encode each duplicate at exactly the same height.
    float columns=floor(_EarthLandPages_TexelSize.z/62);
    float row=floor((slot+.5)/columns);
    address=(float2(slot-row*columns,row)*62+1+frac(region)*60)*_EarthLandPages_TexelSize.xy;
    return true;
}

float3 FlatEarthNativeLand(float2 uv,inout float weight)
{
    float2 address,heightRange;
    if(!FlatEarthLandPage(uv,address,heightRange)){weight=0;return 0;}
    float2 texel=_EarthLandPages_TexelSize.xy;
    float east=FlatPresentationSample(_EarthLandPages,address+float2(.5*texel.x,0),0).r-
               FlatPresentationSample(_EarthLandPages,address-float2(.5*texel.x,0),0).r;
    float north=FlatPresentationSample(_EarthLandPages,address+float2(0,.5*texel.y),0).r-
                FlatPresentationSample(_EarthLandPages,address-float2(0,.5*texel.y),0).r;
    return float3(heightRange.x+heightRange.y*FlatPresentationSample(_EarthLandPages,address,0).r,
        float2(east,north)*(heightRange.y*21600/40030.2));
}

// The water receiver is classified first. Only the rendered land height and
// lighting change; water optics and the physical field retain their own bed.
float FlatEarthLandRelief(float2 uv,float dry,inout float height,inout float2 gradient)
{
    if(_EarthPolarReady<.5 || _EarthLandOverview_TexelSize.z<3600)return 0;
    float2 dx=ddx(uv),dy=ddy(uv);
    dx.x-=round(dx.x);dy.x-=round(dy.x);
    float footprint=max(length(dx*float2(21600,10800)),length(dy*float2(21600,10800)));
    float latitude=(uv.y-.5)*180;
    float weight=saturate(dry)*(1-smoothstep(50,60,abs(latitude)));
    if(weight<=0)return 0;
    weight*=FlatPresentationSample(_EarthSurveyMask,uv,0).r;
    if(weight<=0)return 0;
    float fine=_EarthLandPages_TexelSize.z>=8192 ? 1-smoothstep(.8,3,footprint) : 0;
    float3 measurement=0;
    [branch] if(fine>0)measurement=FlatEarthNativeLand(uv,fine);
    [branch] if(fine<1)
    {
        float lod=clamp(log2(max(1,footprint/6)),0,10);
        measurement=lerp(FlatEarthOverviewLand(uv,lod),measurement,fine);
    }
    // The cartographic relief and exposure controls consume metre differences.
    // Use the same five kilometres of ground for every simulation resolution.
    float2 slope=measurement.yz*float2(5/cos(latitude*.01745329252),5);
    height=lerp(height,measurement.x,weight);
    gradient=lerp(gradient,slope,weight);
    return weight;
}

// Ground footprint as a fraction of the equatorial circumference. This also
// handles a local stereographic chart whose screen axes turn across longitude.
float FlatEarthGroundFootprint(float2 uv)
{
    float2 dx=ddx(uv),dy=ddy(uv);
    dx.x-=round(dx.x);dy.x-=round(dy.x);
    float2 metric=float2(cos((uv.y-.5)*3.14159265),.5);
    return max(length(dx*metric),length(dy*metric));
}

float2 FlatEarthStraitSlot(float slot,float columns)
{
    // All results are integer texel coordinates. frac(slot/columns)*columns
    // can drift far enough on D3D11 to interpolate a neighbouring page ID.
    float row=floor((slot+.5)/columns);
    return float2(slot-row*columns,row);
}

float FlatEarthStraitPage(float2 pixel)
{
    #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
    float2 bytes=round(255*_EarthStraitShore.Load(int3((int2)pixel,0)).rg);
    #else
    float2 bytes=round(255*FlatPresentationSample(_EarthStraitShore,(pixel+.5)*_EarthStraitShore_TexelSize.xy,0).rg);
    #endif
    return dot(bytes,float2(1,256))-1;
}

float2 FlatEarthRegionalStrait(float2 uv,float footprintKm)
{
    if(_EarthStraitShore_TexelSize.z<2048)return 0;
    float2 location=float2(frac(uv.x)*1440,min(saturate(uv.y)*720,719.9999));
    float2 tile=floor(location),phase=frac(location);
    float index=tile.y*1440+tile.x;
    float columns=_EarthStraitShore_TexelSize.z;
    float slot=FlatEarthStraitPage(FlatEarthStraitSlot(index,columns));
    [branch] if(slot<0)return 0;
    float headerRows=ceil((1440.0*720.0)/columns);
    float2 origin=FlatEarthStraitSlot(slot,floor(columns/18))*float2(18,19)+float2(0,headerRows);
    float2 data=FlatPresentationSample(_EarthStraitShore,(origin+float2(1,2)+phase*16)*_EarthStraitShore_TexelSize.xy,0).rg;
    float km=(data.r*255-128)*.5;
    float weight=data.g;
    [branch] if(weight<=0)return 0;
    float detail=smoothstep(.975,.999,data.g)*(1-smoothstep(2,6,abs(km)))*(1-smoothstep(1.2,4,footprintKm));
    [branch] if(detail>0)
    {
        float fine=FlatEarthStraitPage(origin);
        if(fine>=0 && _EarthStraitDetail_TexelSize.z>=130)
        {
            float2 page=FlatEarthStraitSlot(fine,floor(_EarthStraitDetail_TexelSize.z/130))*130;
            float code=FlatPresentationSample(_EarthStraitDetail,(page+1+phase*128)*_EarthStraitDetail_TexelSize.xy,0).r;
            km=lerp(km,(255*code-128)/16,detail);
        }
        else if(fine<0)
        {
            // River mouths use 64 samples per quarter degree. Two pages
            // share the regional texture's RG channels, packed from its last row.
            float medium=FlatEarthStraitPage(origin+float2(1,0));
            if(medium>=0)
            {
                float2 page=FlatEarthStraitSlot(floor(medium*.5),floor(columns/66))*66;
                page.y=_EarthStraitShore_TexelSize.w-page.y-66;
                float2 uv2=(page+1+phase*64)*_EarthStraitShore_TexelSize.xy;
                float2 codes=FlatPresentationSample(_EarthStraitShore,uv2,0).rg;
                float code=lerp(codes.r,codes.g,fmod(medium,2));
                km=lerp(km,(255*code-128)/16,detail);
            }
        }
    }
    return float2(km*_MapSize.x/40030.2,weight);
}

float2 FlatEarthOceanCoast(float2 uv)
{
    if(_EarthOceanDirectory_TexelSize.z<1440)return 0;
    float2 position=float2(frac(uv.x)*1440,min(saturate(uv.y)*720,719.9999));
    float2 tile=floor(position);
    #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
    float4 entry=round(255*_EarthOceanDirectory.Load(int3((int2)tile,0)));
    #else
    float4 entry=round(255*FlatPresentationSample(_EarthOceanDirectory,(tile+.5)*_EarthOceanDirectory_TexelSize.xy,0));
    #endif
    float slot=dot(entry.rg,float2(1,256))-1;
    float fine=(entry.a>=128?127:-127)/32.0;
    [branch] if(slot>=0)
    {
        float2 origin=32*FlatEarthStraitSlot(floor(slot*.25),128);
        float2 address=(origin+1+frac(position)*30)*_EarthOceanPages_TexelSize.xy;
        float4 codes=FlatPresentationSample(_EarthOceanPages,address,0);
        float4 channel=1-step(.5,abs(float4(0,1,2,3)-fmod(slot,4)));
        fine=(dot(codes,channel)*255-128)/32;
    }
    // A small island need not contain a directory centre. Its native sign
    // remains authoritative even where the kilometre-scale overview is open sea.
    float2 broadCodes=255*FlatPresentationSample(_EarthOceanDirectory,uv,0).ba;
    float broad=(dot(broadCodes,float2(1,256))-32768)/32;
    float km=sign(fine)*lerp(abs(fine),max(abs(fine),sign(fine)*broad),smoothstep(1.5,3.96,abs(fine)));
    float cellKm=40030.2*_MapSize.z;
    // Inland lakes, edited terrain and remote open water keep their own physical
    // fields. Cover enough ground to correct the footprint of even a 256 map cell.
    return float2(km/cellKm,1-smoothstep(1.5*cellKm+20,3*cellKm+45,abs(km)));
}

float2 FlatEarthStrait(float2 uv,float footprintKm)
{
    if(_EarthStraitReady<.5)return 0;
    float2 coast=FlatEarthRegionalStrait(uv,footprintKm);
    [branch] if(coast.y<.99999)
    {
        float2 worldwide=FlatEarthOceanCoast(uv);
        float fill=worldwide.y*(1-coast.y);
        float total=coast.y+fill;
        coast=float2((coast.x*coast.y+worldwide.x*fill)/max(.00001,total),total);
    }
    // Apply the edit halo once, after combining the independent chart scales.
    if(coast.y>0)coast.y*=FlatPresentationSample(_EarthSurveyMask,uv,0).r;
    return coast;
}

// One fixed geographic sample per tree root. The same bank applies to a
// crown, its shadow and its reflection at every camera scale.
float2 FlatEarthTreeBank(float2 uv,float kind,float gridShore)
{
    float2 strait=FlatEarthStrait(uv,0);
    float shore=lerp(gridShore,strait.x,strait.y);
    float lakeWeight;
    FlatEarthLakeAt(uv,kind,strait.y,0,0,shore,lakeWeight);
    return float2(shore,max(strait.y,lakeWeight));
}

float FlatEarthShoreAA(float2 uv,float aa,float strait)
{
    if(strait<=0)return aa;
    // A 0.008-cell floor would blur over a kilometre on a 256-wide map.
    float surveyed=clamp(.65*FlatEarthGroundFootprint(uv)*_MapSize.x,.0001,.28);
    return lerp(aa,surveyed,strait);
}

float FlatEarthBeachScale(float strait)
{
    // The surveyed distance is in equatorial cells. Limit a unit of beach /
    // breaking-wave artwork to 400 metres along these high-resolution banks.
    return lerp(1,max(1,(40030.2/.4)*_MapSize.z),strait);
}

float2 FlatEarthMarineTexel(float2 pixel)
{
    #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
    return _EarthMarineDepth.Load(int3((int2)pixel,0)).rg;
    #else
    return FlatPresentationSample(_EarthMarineDepth,(pixel+.5)*_EarthMarineDepth_TexelSize.xy,0).rg;
    #endif
}

float2 FlatEarthOpenCoastalDepth(float2 uv,float offshoreKm)
{
    if(_EarthOceanDirectory_TexelSize.z<1440 || _EarthLandOverview_TexelSize.z<3600)return float2(-1,0);
    float bed=FlatPresentationSample(_EarthLandOverview,uv,0).r*65535-32768;
    float latitude=abs(180*uv.y-90);
    float2 address,heightRange;
    [branch] if(latitude<60 && _EarthLandPages_TexelSize.z>=8192 && FlatEarthLandPage(uv,address,heightRange))
    {
        float z=heightRange.x+heightRange.y*FlatPresentationSample(_EarthLandPages,address,0).r;
        // The native land atlas retains shelf depths to -512 m. Match to the
        // unclipped NOAA overview only where that native encoding bottoms out.
        float shelf=lerp(z,min(z,bed),smoothstep(416,510,-z));
        bed=lerp(shelf,bed,smoothstep(58,60,latitude));
    }
    // A narrow tidal arm can be wet in the ocean polygon and dry in ETOPO.
    float shallow=clamp(.5+.8*max(0,offshoreKm),.5,12);
    return float2(max(-bed,lerp(.15,shallow,smoothstep(-.15,3,bed))),1);
}

float2 FlatEarthRegionalDepth(float2 uv)
{
    if(_EarthMarineDepth_TexelSize.z<2048)return float2(-1,0);
    float2 location=float2(frac(uv.x)*1440,min(saturate(uv.y)*720,719.9999));
    float2 tile=floor(location);
    float columns=_EarthMarineDepth_TexelSize.z;
    float2 index=FlatEarthStraitSlot(tile.y*1440+tile.x,columns);
    float2 bytes=round(255*FlatEarthMarineTexel(index));
    float slot=dot(bytes,float2(1,256))-1;
    if(slot<0)return float2(-1,0);
    float2 origin=FlatEarthStraitSlot(slot,floor(columns/17))*17+float2(0,ceil((1440.0*720.0)/columns));
    // Interpolate in the 17 x 17 source page. Normalizing the address against
    // different atlas shapes can change the hardware filter weights on steep beds.
    float2 local=.5+frac(location)*15;
    float2 pixel=origin+floor(local),phase=frac(local);
    float2 south=lerp(FlatEarthMarineTexel(pixel),FlatEarthMarineTexel(pixel+float2(1,0)),phase.x);
    float2 north=lerp(FlatEarthMarineTexel(pixel+float2(0,1)),FlatEarthMarineTexel(pixel+1),phase.x);
    float2 encoded=lerp(south,north,phase.y);
    // Only submerged native ETOPO nodes contribute near a mixed land/water pixel.
    return float2(exp2(14*encoded.r/max(encoded.g,1e-4))-1,smoothstep(.1,.85,encoded.g));
}

float2 FlatEarthCoastalDepth(float2 uv,float offshoreKm)
{
    float2 measured=FlatEarthRegionalDepth(uv);
    if(measured.x<0)measured=FlatEarthOpenCoastalDepth(uv,offshoreKm);
    return measured;
}

// Return how much of the chart and bed lighting should use this optical depth.
float FlatEarthMarineOptics(float2 uv,float2 coast,float bed,float strait,inout float4 optics,inout float wetSupport)
{
    float footprintKm=FlatEarthGroundFootprint(uv)*40030.2;
    float wet=max(0,coast.y-strait)*smoothstep(-.02,.10,coast.x);
    if(wet>0)
    {
        if(wetSupport<=1e-6)optics=float4(optics.r,0,0,3);
        optics.r=lerp(optics.r,max(.15,-bed),wet);
        wetSupport=max(wetSupport,wet);
    }
    if(strait<=0)return 0;
    float km=coast.x*40030.2*_MapSize.z;
    wet=strait*smoothstep(-.08,.16,km);
    if(wet<=0)return 0;
    // Retain available simulated marine depth/sediment. Over a dry grid
    // sample, supply a shallow optical bed for the surveyed saltwater receiver.
    // A surveyed channel can cross the coarse DEM's zero contour underwater.
    // Carry its shallow bed across that contour until the DEM resolves deeper water.
    float depth=max(-bed,clamp(.5+max(0,km)*5,.5,18));
    float marine=smoothstep(0,1,wetSupport)*saturate(4-optics.a);
    float fill=wet*(1-marine);
    if(wetSupport<=1e-6)optics=float4(depth,0,0,3);
    optics.rgb=lerp(optics.rgb,float3(depth,0,0),fill);
    float detail=wet*(1-smoothstep(2,8,footprintKm));
    if(detail>0)
    {
        float2 measured=FlatEarthCoastalDepth(uv,km);
        if(measured.x>=0)
        {
            optics.r=lerp(optics.r,max(.15,measured.x),detail*measured.y);
            // The finer coastline can classify a dry ETOPO node as water. Keep
            // its fallback depth on the same optical response as nearby measured
            // nodes. Switching the coarse chart shading back on creates dark dots.
            fill=lerp(fill,1,detail);
        }
    }
    optics.a=lerp(optics.a,3,wet);
    wetSupport=max(wetSupport,wet);
    return fill;
}

float FlatApplyEarthShore(float2 uv,float waterKind,inout float shore,inout float2 coast)
{
    if(abs(waterKind-4)<.5){coast.y=0;return 0;} // Preserve simulated inland lake levels and extents.
    shore=lerp(shore,coast.x,coast.y);
    float2 strait=FlatEarthStrait(uv,FlatEarthGroundFootprint(uv)*40030.2);
    if(strait.y>0)
    {
        shore=lerp(shore,strait.x,strait.y);
        coast=float2(shore,lerp(coast.y,1,strait.y));
    }
    return strait.y;
}
#endif
