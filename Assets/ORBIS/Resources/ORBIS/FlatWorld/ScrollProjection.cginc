// One immutable UV lattice, two continuous endpoint positions. No scene swap or screenshot morph.
#ifndef ORBIS_SCROLL_PROJECTION
#define ORBIS_SCROLL_PROJECTION
#define ORBIS_TERRAIN_PROJECTION 1
#include "ScrollHeight.cginc"
#include "Markley.cginc"
float4 _ProjectionSize,_MarkleyCopy;
float4 _ScrollAnchor, _ScrollPan;
float3 _ScrollRight, _ScrollUp, _ScrollNormal;
float _ScrollMorph, _ScrollSpin, _ScrollCopy;
float3 ScrollRotate(float3 p)
{
 float s=sin(_ScrollSpin),c=cos(_ScrollSpin);
 return float3(c*p.x+s*p.z,p.y,-s*p.x+c*p.z);
}
float3 ScrollPosition(float2 meshUV,float shell,out float2 geo,out float clipWeight)
{
 clipWeight=1;
 geo=meshUV+_MarkleyCopy.xy;
 float scale=6.28318530718/_MapSize.x;
 float2 delta=(geo*_ProjectionSize.xy-_ScrollPan.xy)*scale;
 float3 plane=_ScrollNormal+_ScrollRight*delta.x+_ScrollUp*delta.y;
 plane+=_ScrollNormal*shell*.006;
 if(_ScrollMorph>=1&&_ScrollRelief.x<=0) return plane;
 // Presentation reflection is shared by the CPU tangent frame and native cloud transform.
 float3 radial=MarkleyDirection(geo);radial.z=-radial.z;
 float2 heightUV=MarkleyGeoUV(geo);
 // Every longitude converges to the same polar radius over the last source row.
 float radius=ScrollTerrainRadius(heightUV,_MapSize);
 clipWeight=FlatReliefClipWeight(radius-1,_ScrollMorph);
 float3 relief=FlatReliefPosition(delta,radius-1);
 plane+=_ScrollNormal*relief.z+_ScrollRight*(relief.x-delta.x)+_ScrollUp*(relief.y-delta.y);
 if(_ScrollMorph>=1)return plane;
 float3 sphere=ScrollRotate(radial)*(radius+shell*(1-_ScrollMorph));
 return lerp(sphere,plane,_ScrollMorph);
}
float3 ScrollPosition(float2 meshUV,float shell,out float2 geo)
{
 float clipWeight;return ScrollPosition(meshUV,shell,geo,clipWeight);
}
#endif
