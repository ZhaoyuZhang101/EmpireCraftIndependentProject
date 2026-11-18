#ifndef ORBIS_FLAT_RIVER_ATLAS
#define ORBIS_FLAT_RIVER_ATLAS
UNITY_DECLARE_TEX2D_NOSAMPLER(_RiverAtlas);
UNITY_DECLARE_TEX2D_NOSAMPLER(_RiverSeasonTex);
float4 _RiverAtlasLayout;
float _RiverAtlasReady,_RiverPreviewReady,_RiverSeasonReady;
float4 _RiverSeason;

bool FlatRiverFieldReady() { return max(_RiverAtlasReady,_RiverPreviewReady)>.5; }

// Three panels, each containing four monthly discharge ratios. A value of 60
// in the byte atlas denotes the mean flow. The sampler interpolates in space.
float FlatRiverMonthRatio(float2 uv,float month)
{
    float panel=floor(month*.25);
    float4 q=FlatPresentationSample(_RiverSeasonTex,float2(uv.x,(uv.y+panel)/3),0);
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

float4 FlatRiverAtlasHeader(float2 tile)
{
    tile.x-=floor(tile.x/_MapSize.x)*_MapSize.x;
    tile.y=clamp(tile.y,0,_MapSize.y-1);
    float index=tile.y*_MapSize.x+tile.x;
    float row=floor(index/_RiverAtlasLayout.x);
    float2 pixel=float2(index-row*_RiverAtlasLayout.x,row)+.5;
    return FlatPresentationSample(_RiverAtlas,pixel/_RiverAtlasLayout.xy,0);
}

float4 FlatRiverBaseSample(float2 cell)
{
    float4 result=float4(8,0,0,0);
    [branch] if(FlatRiverFieldReady())
    {
        cell.x-=floor(cell.x/_MapSize.x)*_MapSize.x;
        cell.y=clamp(cell.y,0,_MapSize.y-.0001);
        float4 header=FlatRiverAtlasHeader(floor(cell));
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

float4 FlatRiverAtlasSample(float2 cell)
{
    float4 river=FlatRiverBaseSample(cell);
    if(_RiverSeasonReady>.5&&river.r<1)
    {
        float stage=FlatRiverStage(cell),radius=length(river.ba);
        float current=min(.24,radius*FlatRiverWidthScale(stage));
        river.r+=radius-current;
        river.g*=FlatRiverDepthScale(stage);
        river.ba*=current/max(.000001,radius);
    }
    return river;
}

bool FlatRiverAtlasClear(float2 cell,float clearance)
{
    float4 raw=FlatRiverBaseSample(cell);
    float radius=length(raw.ba);
    if(_RiverSeasonReady<.5||raw.r>=clearance+.30*radius||raw.r<clearance-.42*radius)
        return raw.r>=clearance;
    float current=min(.24,radius*FlatRiverWidthScale(FlatRiverStage(cell)));
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
