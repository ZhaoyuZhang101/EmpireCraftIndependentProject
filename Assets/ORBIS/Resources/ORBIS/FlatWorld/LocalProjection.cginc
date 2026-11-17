#ifndef ORBIS_LOCAL_PROJECTION
#define ORBIS_LOCAL_PROJECTION
#include "ScrollHeight.cginc"
#include "../GeographicCoordinates.cginc"
float3 _LocalCenter,_LocalEast,_LocalNorth;
float3 _ScrollNormal,_ScrollRight,_ScrollUp;
float _LocalRadius,_ScrollMorph,_ScrollSpin;
float3 LocalDirection(float2 xy)
{
 return OrbisLocalDirection(_LocalCenter,_LocalEast,_LocalNorth,xy,_ScrollMorph);
}
float2 LocalGeoUV(float2 xy)
{
 float3 p=LocalDirection(xy);
 return OrbisGeographicUV(p);
}
float3 ScrollPosition(float2 meshUV,float shell,out float2 xy)
{
 xy=meshUV*_LocalRadius;float3 radial=LocalDirection(xy);float2 uv=LocalGeoUV(xy);
 float4 field=FlatVisualSample(float4(uv,0,0));float radius=ScrollRadius(field,uv,_MapSize.y);
 radial.z=-radial.z;float s=sin(_ScrollSpin),c=cos(_ScrollSpin);
 float3 sphere=float3(c*radial.x+s*radial.z,radial.y,-s*radial.x+c*radial.z)*(radius+shell);
 float3 plane=_ScrollNormal+_ScrollRight*xy.x+_ScrollUp*xy.y;
 return lerp(sphere,plane,_ScrollMorph);
}
#endif
