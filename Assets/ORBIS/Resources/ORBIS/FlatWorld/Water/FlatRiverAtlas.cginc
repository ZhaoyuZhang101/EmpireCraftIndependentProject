#ifndef ORBIS_FLAT_RIVER_ATLAS
#define ORBIS_FLAT_RIVER_ATLAS
UNITY_DECLARE_TEX2D_NOSAMPLER(_RiverAtlas);
float4 _RiverAtlasLayout;
float _RiverAtlasReady;

float4 FlatRiverAtlasHeader(float2 tile)
{
    tile.x-=floor(tile.x/_MapSize.x)*_MapSize.x;
    tile.y=clamp(tile.y,0,_MapSize.y-1);
    float index=tile.y*_MapSize.x+tile.x;
    float row=floor(index/_RiverAtlasLayout.x);
    float2 pixel=float2(index-row*_RiverAtlasLayout.x,row)+.5;
    return FlatPresentationSample(_RiverAtlas,pixel/_RiverAtlasLayout.xy,0);
}

float4 FlatRiverAtlasSample(float2 cell)
{
    float4 result=float4(8,0,0,0);
    [branch] if(_RiverAtlasReady>.5)
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

float FlatRiverAtlasSediment(float2 cell)
{
    if(_RiverAtlasReady<.5)return 0;
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
