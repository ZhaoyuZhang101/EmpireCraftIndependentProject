#ifndef ORBIS_PATCH
#define ORBIS_PATCH
float4x4 _WorldToPlanet;
float _Map,_PatchActive,_PatchLayer,_PatchRadius,_LocalGrid,_GridSpacing;
float3 _PatchCenter,_PatchRight,_PatchUp;
float2 OrbisPatchUV(float3 p)
{
 float3 n=normalize(p);return float2(dot(n,_PatchRight),dot(n,_PatchUp))/max(.001,dot(n,_PatchCenter));
}
void OrbisPatchClip(float3 p,float2 pixel)
{
 if(_Map>.5||_PatchActive<.5||_PatchLayer>1.5)return;
 float facing=dot(normalize(p),_PatchCenter);
 float radius=length(OrbisPatchUV(p))/max(.001,_PatchRadius);
 float weight=(1-smoothstep(.65,.94,radius))*step(0.0,facing);
 // The SAME screen-space threshold is used by the old mesh and its replacement, including depth.
 float dither=frac(52.9829189*frac(dot(floor(pixel),float2(.06711056,.00583715))));
 if(_PatchLayer<.5)clip(dither<weight?-1.0:1.0);else clip(dither>=weight?-1.0:1.0);
}
float OrbisLocalGrid(float3 p)
{
 if(_Map>.5||_LocalGrid<.5||_PatchActive<.5||_PatchLayer>1.5)return 0;
 float2 q=OrbisPatchUV(p)/max(.00001,_GridSpacing);
 float2 width=max(fwidth(q),.0001);
 float2 grid=abs(frac(q-.5)-.5)/width;
 float aa=1-saturate(min(grid.x,grid.y));
 float fade=1-smoothstep(.35,.9,length(OrbisPatchUV(p))/max(.001,_PatchRadius));
 return aa*fade*step(0.0,dot(normalize(p),_PatchCenter));
}
#endif
