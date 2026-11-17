#ifndef ORBIS_SCROLL_HEIGHT
#define ORBIS_SCROLL_HEIGHT
#include "FlatReliefProjection.cginc"
float2 _ScrollPoleHeights;
float ScrollSurfaceHeight(float4 field)
{
 return clamp(field.r+(field.g>0?field.b:0),0,4000);
}
float ScrollRadius(float height,float2 uv,float rows)
{
 float polar=lerp(_ScrollPoleHeights.x,_ScrollPoleHeights.y,step(.5,uv.y));
 float blend=smoothstep(0,1,min(uv.y,1-uv.y)*rows);
 return 1+lerp(polar,height,blend)*_ScrollHeightScale;
}
float ScrollInterpolatedRadius(float4 a,float4 b,float4 c,float4 d,float2 fraction,float2 uv,float rows)
{
 float h=lerp(lerp(ScrollSurfaceHeight(a),ScrollSurfaceHeight(b),fraction.x),
              lerp(ScrollSurfaceHeight(c),ScrollSurfaceHeight(d),fraction.x),fraction.y);
 return ScrollRadius(h,uv,rows);
}
#ifdef ORBIS_TERRAIN_PROJECTION
float ScrollTerrainRadius(float2 uv,float4 size)
{
 float2 pixel=uv*size.xy-.5,base=floor(pixel),fraction=frac(pixel);
 float2 centre=(base+.5)*size.zw,dx=float2(size.z,0),dy=float2(0,size.w);
 return ScrollInterpolatedRadius(FlatVisualSample(float4(centre,0,0)),FlatVisualSample(float4(centre+dx,0,0)),
                                 FlatVisualSample(float4(centre+dy,0,0)),FlatVisualSample(float4(centre+dx+dy,0,0)),fraction,uv,size.y);
}
#endif
#endif
