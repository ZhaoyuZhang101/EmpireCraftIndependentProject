// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at
// https://mozilla.org/MPL/2.0/.
//
// Gully-and-ridge erosion detail for the close map views: an HLSL port of the
// "Advanced Terrain Erosion Filter" by Rune Skovbo Johansen (runevision), released
// by him under the MPL v2 (https://www.shadertoy.com/view/wXcfWn,
// https://blog.runevision.com/2026/03/fast-and-gorgeous-erosion-filter.html), via
// the MPL-2.0 WGSL port in bevy_erosion_filter. Copyright 2025 Rune Skovbo Johansen;
// earlier technique lineage Copyright 2023 Fewes, Copyright 2020 Clay John. The
// gradient-noise hash is Inigo Quilez's (MIT). Changes here: HLSL, map units
// (tiles and metres), an octave fade by on-screen size, and a land/shore weight.
//
// The filter only shapes the lighting slope of the close views; heights, drainage
// and rivers are untouched. Global _ErosionDetail: 0 off, 1 full.
#ifndef ORBIS_FLAT_EROSION_DETAIL
#define ORBIS_FLAT_EROSION_DETAIL

float _ErosionDetail;

// Inigo Quilez 2D hash in [-1,1]. Grid points are folded into a 512 cell period
// first: the raw product overflows float precision at world-scale coordinates.
float2 EDHash2(float2 x)
{
    x-=512*floor(x/512);
    const float2 k=float2(.3183099,.3678794);
    x=x*k+k.yx;
    return -1+2*frac(16*k*frac(x.x*x.y*(x.x+x.y)));
}

// Phacelle noise (runevision): a stripe pattern aligned with dir, blended over a
// jittered 4x4 cell window. x cosine, y sine, zw the side direction × freq × 2π.
float4 EDPhacelle(float2 p,float2 dir,float freq,float offsetCycles,float normalization)
{
    const float TAU=6.28318530717959;
    float2 side=dir.yx*float2(-1,1)*freq*TAU;
    float offset=offsetCycles*TAU;
    float2 pInt=floor(p),pFrac=frac(p);
    float2 phase=0;float weight=0;
    [unroll] for(int i=-1;i<=2;i++)
    [unroll] for(int j=-1;j<=2;j++)
    {
        float2 g=float2(i,j);
        float2 v=pFrac-g-EDHash2(pInt+g)*.5;
        float w=max(0,exp(-dot(v,v)*2)-.01111);
        weight+=w;
        float s,c;sincos(dot(v,side)+offset,s,c);
        phase+=float2(c,s)*w;
    }
    float2 interpolated=phase/weight;
    float magnitude=max(1-normalization,length(interpolated));
    return float4(interpolated/magnitude,side);
}

float EDEaseOut(float t){float v=1-saturate(t);return 1-v*v;}
float EDSmoothStart(float t,float smoothing){return t>=smoothing?t-.5*smoothing:.5*t*t/max(1e-6,smoothing);}
float EDPowInv(float t,float power){return 1-pow(1-saturate(t),power);}
float2 EDNormalize(float2 n){float l=length(n);return l>1e-10?n/l:n;}

// runevision's ErosionFilter with his demo defaults. hs: (height, d/dx, d/dy) in the
// filter's units. octaveFade(i) scales each octave (used to drop sub-pixel octaves).
// Returns the change to add to hs.
float3 EDErosionFilter(float2 p,float3 hs,float fadeTarget,float pixelsPerUnit)
{
    const float scale=.15,strengthBase=.22,gullyWeight=.5,detail=1.5,cellScale=.7,normalization=.5,lacunarity=2,gain=.5;
    const float4 rounding=float4(.1,0,.1,2),onset=float4(1.25,1.25,2.8,1.5);
    const float2 assumedSlope=float2(.7,1);
    float strength=strengthBase*scale;
    fadeTarget=clamp(fadeTarget,-1,1);
    float3 input=hs;
    float freq=1/(scale*cellScale);
    float slopeLength=max(length(hs.yz),1e-10);
    float roundingMult=1;
    float roundingForInput=lerp(rounding.y,rounding.x,saturate(fadeTarget+.5))*rounding.z;
    float combiMask=EDEaseOut(EDSmoothStart(slopeLength*onset.x,roundingForInput*onset.x));
    float2 gullySlope=lerp(hs.yz,hs.yz/slopeLength*assumedSlope.x,assumedSlope.y);
    [unroll] for(int i=0;i<5;i++)
    {
        // An octave whose stripes are under a few pixels apart only flickers: fade it out.
        float octave=smoothstep(2.5,7,pixelsPerUnit/freq);
        float4 ph=EDPhacelle(p*freq,EDNormalize(gullySlope),cellScale,.25,normalization);
        float2 pzw=ph.zw*(-freq);
        float sloping=abs(ph.y);
        gullySlope+=sign(ph.y)*pzw*strength*gullyWeight*octave;
        float3 octaveHs=float3(ph.x,ph.y*pzw.x,ph.y*pzw.y);
        float3 faded=lerp(float3(fadeTarget,0,0),octaveHs*gullyWeight,combiMask);
        hs+=faded*strength*octave;
        fadeTarget=faded.x;
        float roundingForOctave=lerp(rounding.y,rounding.x,saturate(ph.x+.5))*roundingMult;
        float newMask=EDEaseOut(EDSmoothStart(sloping*onset.y,roundingForOctave*onset.y));
        combiMask=EDPowInv(combiMask,detail)*newMask;
        strength*=gain;freq*=lacunarity;roundingMult*=rounding.w;
    }
    return hs-input;
}

// Map units (globals; 0 = default): one filter unit spans _ErosionTiles tiles across
// and _ErosionMetres in height. Gullies appear only on slopes steeper than
// _ErosionSlope metres per tile, at full strength from three times that.
float _ErosionTiles,_ErosionMetres,_ErosionSlope;

// Lighting slope (metres per tile) with gullies and ridges added. landWeight fades the
// detail out on water and along the shore.
float2 FlatErosionGradient(float2 cell,float height,float2 gradient,float pixelsPerTile,float landWeight)
{
    float tiles=_ErosionTiles>0?_ErosionTiles:12,metres=_ErosionMetres>0?_ErosionMetres:1200,onset=_ErosionSlope>0?_ErosionSlope:50;
    float steep=smoothstep(onset,onset*3,length(gradient));
    float weight=saturate(_ErosionDetail)*saturate(landWeight)*smoothstep(3,8,pixelsPerTile)*steep;
    if(weight<=0)return gradient;
    float4 west,east,south,north;FlatGroundSamples(cell*_MapSize.zw,2,west,east,south,north);
    // Ridges bias up and hollows down: local relief against the two-tile neighbourhood.
    float fadeTarget=(height-(west.r+east.r+south.r+north.r)*.25)/120;
    float3 hs=float3(height/metres,gradient*(tiles/metres));
    float3 delta=EDErosionFilter(cell/tiles,hs,fadeTarget,pixelsPerTile*tiles);
    return gradient+delta.yz*(metres/tiles)*weight;
}

#endif
