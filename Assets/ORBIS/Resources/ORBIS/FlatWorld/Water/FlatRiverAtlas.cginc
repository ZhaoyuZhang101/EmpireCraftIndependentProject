#ifndef ORBIS_FLAT_RIVER_ATLAS
#define ORBIS_FLAT_RIVER_ATLAS
UNITY_DECLARE_TEX2D_NOSAMPLER(_RiverAtlas);
UNITY_DECLARE_TEX2D_NOSAMPLER(_RiverSeasonTex);
UNITY_DECLARE_TEX2D_NOSAMPLER(_RiverOverview);
float4 _RiverOverview_TexelSize;
float _RiverOverviewReady,_RiverSurvey,_RiverMap;
float4 _RiverAtlasLayout;
float _RiverAtlasReady,_RiverPreviewReady,_RiverSeasonReady;
float4 _RiverSeason;
float _RiverMinPixels,_RiverWidthGain,_RiverDisplayScale,_RiverGlobeNarrowing;

#if defined(ORBIS_RIVER_VECTORS)
#define FLAT_VECTOR_FIELD true
#elif defined(ORBIS_SURFACE_RIVER_VARIANTS)
#define FLAT_VECTOR_FIELD false
#else
#define FLAT_VECTOR_FIELD (_RiverAtlasLayout.z<0)
#endif

// Set by the globe presenter: 1 from orbit, where a river is narrower than a pixel and the
// raster rivers stand in for the analytic field.
float _OrbitSurfaceLod;
bool FlatRiverFieldReady() { return max(_RiverAtlasReady,_RiverPreviewReady)>.5&&_OrbitSurfaceLod<.5; }

float FlatRiverStrokeRadius(float radius,float minimumRadius,float cellsPerPixel)
{
    // Atlas radii already include the map scale. Apply it to the pixel
    // minimum and the search margin as well so subpixel streams widen too.
    float scale=max(1,_RiverDisplayScale);
    float stroke=max(radius,minimumRadius*scale);
    // Reduce the finished local stroke, including the subpixel minimum and
    // gain, by the same fraction as the globe overview drawn from these curves.
    return min(stroke+min(stroke*_RiverWidthGain,cellsPerPixel*scale),radius+.28*scale)*(1-_RiverGlobeNarrowing);
}

// Use the same geographic linework at both scales. The filtered overview spans
// more than one catchment per pixel; the sparse distance field resolves local banks.
float FlatRiverMapInk(float2 cell,float4 channel,out float closeView)
{
    float2 dx=ddx(cell),dy=ddy(cell);
    dx.x-=round(dx.x/_MapSize.x)*_MapSize.x;
    dy.x-=round(dy.x/_MapSize.x)*_MapSize.x;
    float2 metric=float2(max(.08,cos((cell.y*_MapSize.w-.5)*3.14159265)),1);
    float ground=max(.0001,max(length(dx*metric),length(dy*metric)));
    float radius=length(channel.ba);
    float physicalRadius=radius/max(1,_RiverDisplayScale);
    float importance=_RiverSurvey>.5?smoothstep(.08,2.2,physicalRadius*(40075*_MapSize.z)):
        saturate((physicalRadius-.012)/.14);
    float halfWidth=FlatRiverStrokeRadius(radius,min(.27,ground*lerp(.46,1.28,importance)),ground);
    float aa=clamp(ground*.58,.003,.18)*(1-_RiverGlobeNarrowing);
    float fine=(1-smoothstep(-aa,aa,channel.r+radius-halfWidth))*step(.0001,radius)*lerp(.58,1,importance);
    closeView=FlatRiverFieldReady()?1-smoothstep(.08,.38,ground):0;
    if(_RiverOverviewReady<.5)return fine;
    float texels=max(length(dx),length(dy))*_RiverOverview_TexelSize.z*_MapSize.z;
    float lod=log2(max(1,texels));
    float overview=FlatPresentationSample(_RiverOverview,cell*_MapSize.zw,lod).r;
    overview=saturate(overview*clamp(sqrt(max(1,ground*_RiverOverview_TexelSize.z*_MapSize.z)),1,2.4));
    return lerp(overview,fine,closeView);
}

// Three panels, each containing four monthly discharge ratios. A value of 60
// in the byte atlas denotes the mean flow. The sampler interpolates in space.
float FlatRiverMonthRatio(float2 uv,float month)
{
    float panel=floor(month*.25);
    float panels=_RiverSeason.w>.5?2:3;
    float4 q=FlatPresentationSample(_RiverSeasonTex,float2(uv.x,(uv.y+panel)/panels),0);
    int component=(int)(month-panel*4);
    return q[component]*4.25;
}
float FlatRiverStage(float2 cell)
{
    if(_RiverSeasonReady<.5)return 1;
    float2 uv=float2(frac(cell.x*_MapSize.z),clamp(cell.y,.5,_MapSize.y-.5)*_MapSize.w);
    return lerp(FlatRiverMonthRatio(uv,_RiverSeason.x),FlatRiverMonthRatio(uv,_RiverSeason.y),_RiverSeason.z);
}
float FlatRiverWidthScale(float stage) { return clamp(pow(clamp(stage,.1,4),.23),.58,1.30); }
float FlatRiverDepthScale(float stage) { return clamp(pow(clamp(stage,.1,4),.38),.42,1.62); }

float4 FlatRiverAtlasTexel(int index,int firstRow)
{
    int columns=(int)_RiverAtlasLayout.x,row=index/columns;
    int2 pixel=int2(index-row*columns,firstRow+row);
    // Page IDs are discrete bytes. On a 4096 x 4830 atlas even a nominal
    // texel-centre bilinear lookup can turn 209 into 208.375 and select a neighbour.
    #ifdef UNITY_SEPARATE_TEXTURE_SAMPLER
    return _RiverAtlas.Load(int3((int2)pixel,0));
    #else
    return FlatPresentationSample(_RiverAtlas,(pixel+.5)/_RiverAtlasLayout.xy,0);
    #endif
}

int FlatRiverPageAddress(float4 header)
{return (int)round(header.r)+2048*(int)round(header.g)-1;}

float4 FlatRiverAtlasHeader(float2 tile)
{
    tile.x-=floor(tile.x/_MapSize.x)*_MapSize.x;
    tile.y=clamp(tile.y,0,_MapSize.y-1);
    if(FLAT_VECTOR_FIELD)
    {
        int groupSize=(int)-_RiverAtlasLayout.z;
        int2 id=(int2)tile,group=id/groupSize;
        int columns=((int)_MapSize.x+groupSize-1)/groupSize;
        int page=FlatRiverPageAddress(FlatRiverAtlasTexel(group.y*columns+group.x,0));
        if(page<0)return 0;
        int2 local=id-group*groupSize;
        return FlatRiverAtlasTexel(page+local.y*groupSize+local.x,(int)_RiverAtlasLayout.w);
    }
    return FlatRiverAtlasTexel((int)tile.y*(int)_MapSize.x+(int)tile.x,0);
}

float4 FlatRiverGeometrySample(float2 cell,float4 header,out float2 bank)
{
    bank=0;
    float2 p=frac(cell);int body=(int)_RiverAtlasLayout.w;
    int start=FlatRiverPageAddress(header),count=(int)round(header.a);
    int records=-1;
    if(count<0)
    {
        records=start+count*count;
        int2 local=(int2)floor(p*(-count));
        header=FlatRiverAtlasTexel(start+local.y*(-count)+local.x,body);
        start=FlatRiverPageAddress(header);count=(int)round(header.a);
    }
    float4 result=float4(8,0,0,0);
    if(count<=0)return result;
    float2 rows=floor(cell.y)+float2(0,1);
    float2 scales=max(.08,cos((rows*_MapSize.w-.5)*3.14159265));
    float2 metric=float2(lerp(scales.x,scales.y,p.y),1);
    int winner=-1;float along=0,radius=0;float2 offset=0;float4 indices=0;
    [loop] [fastopt] for(int i=0;i<count;i++)
    {
        int address=start+3*i;
        if(records>=0)
        {
            int component=i%4;
            if(component==0)indices=FlatRiverAtlasTexel(start+i/4,body);
            address=records+3*(int)round(indices[component]);
        }
        float4 ends=FlatRiverAtlasTexel(address,body);
        float4 section=FlatRiverAtlasTexel(address+1,body);
        float2 from=(p-ends.xy)*metric,delta=(ends.zw-ends.xy)*metric;
        float t=saturate(dot(from,delta)/max(1e-12,dot(delta,delta)));
        float width=lerp(section.x,section.y,t);
        float edge=length(from-t*delta)-width;
        if(edge<result.r)
        {
            result.r=edge;result.g=lerp(section.z,section.w,t);
            winner=address+2;along=t;radius=width;offset=from-t*delta;
        }
    }
    if(winner>=0)
    {
        float4 flow=FlatRiverAtlasTexel(winner,body);
        float2 amplitude=float2(length(flow.xy),length(flow.zw));
        float2 tangent=lerp(flow.xy/max(.00001,amplitude.x),flow.zw/max(.00001,amplitude.y),along);
        result.ba=dot(tangent,tangent)>1e-12?normalize(tangent)*radius:float2(radius,0);
        tangent=normalize(result.ba*metric+float2(1e-12,0));
        bank=float2(dot(offset,float2(-tangent.y,tangent.x)),lerp(amplitude.x,amplitude.y,along)-2);
    }
    return result;
}

float4 FlatRiverBaseSample(float2 cell,out float2 bank)
{
    bank=0;
    float4 result=float4(8,0,0,0);
    [branch] if(FlatRiverFieldReady())
    {
        cell.x-=floor(cell.x/_MapSize.x)*_MapSize.x;
        cell.y=clamp(cell.y,0,_MapSize.y-.0001);
        float4 header=FlatRiverAtlasHeader(floor(cell));
        if(FLAT_VECTOR_FIELD)return FlatRiverGeometrySample(cell,header,bank);
        float slot=floor(header.r+.5)+floor(header.g+.5)*256-1;
        [branch] if(slot>=0)
        {
            float span=max(1,floor(header.a+.5));
            float2 local=(cell-floor(cell/span)*span)/span;
            float pitch=_RiverAtlasLayout.z,columns=floor(_RiverAtlasLayout.x/pitch);
            float row=floor(slot/columns);
            float2 pixel=float2((slot-row*columns)*pitch,_RiverAtlasLayout.w+row*pitch);
            pixel+=1.5+local*(pitch-3);
            result=FlatPresentationSample(_RiverAtlas,pixel/_RiverAtlasLayout.xy,0);
        }
    }
    return result;
}
float4 FlatRiverBaseSample(float2 cell)
{float2 bank;return FlatRiverBaseSample(cell,bank);}

float FlatRiverDisplayRadius(float radius,float pixelsPerTile)
{
    float weight=lerp(smoothstep(.03,.10,radius/max(1,_RiverDisplayScale)),.62,_RiverSurvey);
    float cellsPerPixel=1/max(1,pixelsPerTile);
    float wanted=min(.28,_RiverMinPixels*.5*cellsPerPixel)*weight*step(.000001,radius);
    return FlatRiverStrokeRadius(radius,wanted,cellsPerPixel);
}
float4 FlatRiverDisplaySample(float4 river,float pixelsPerTile)
{
    float radius=length(river.ba),display=FlatRiverDisplayRadius(radius,pixelsPerTile);
    river.r+=radius-display;river.ba*=display/max(.000001,radius);
    return river;
}

float FlatRiverActiveRadius(float radius,float stage)
{return lerp(radius,min(max(.24*max(1,_RiverDisplayScale),radius),radius*FlatRiverWidthScale(stage)),_RiverSeasonReady);}

float4 FlatRiverAtlasSample(float2 cell,out float2 bank)
{
    float4 river=FlatRiverBaseSample(cell,bank);
    if(_RiverSeasonReady>.5&&river.r<1)
    {
        float stage=FlatRiverStage(cell),radius=length(river.ba);
        float current=FlatRiverActiveRadius(radius,stage);
        river.r+=radius-current;
        river.g*=FlatRiverDepthScale(stage);
        river.ba*=current/max(.000001,radius);
    }
    return river;
}
float4 FlatRiverAtlasSample(float2 cell)
{float2 bank;return FlatRiverAtlasSample(cell,bank);}

bool FlatRiverAtlasClear(float2 cell,float clearance)
{
    float4 raw=FlatRiverBaseSample(cell);
    float radius=length(raw.ba);
    float current=FlatRiverActiveRadius(radius,FlatRiverStage(cell));
    return raw.r+radius-current>=clearance;
}

float FlatRiverAtlasSediment(float2 cell)
{
    if(!FlatRiverFieldReady())return 0;
    float2 base=floor(cell-.5),t=frac(cell-.5);t=t*t*(3-2*t);
    float4 weights=float4((1-t.x)*(1-t.y),t.x*(1-t.y),(1-t.x)*t.y,t.x*t.y);
    float sum=0,weight=0;
    [unroll] for(int k=0;k<4;k++)
    {
        float4 header=FlatRiverAtlasHeader(base+float2(k==1||k==3?1:0,k>=2?1:0));
        float valid=step(.5,header.r+header.g);
        float contribution=weights[k]*valid;
        sum+=header.b*contribution;weight+=contribution;
    }
    return sum/max(1e-6,weight);
}
#endif
