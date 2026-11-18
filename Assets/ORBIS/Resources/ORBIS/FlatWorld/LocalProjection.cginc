#ifndef ORBIS_LOCAL_PROJECTION
#define ORBIS_LOCAL_PROJECTION
#define ORBIS_TERRAIN_PROJECTION 1
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
 return OrbisGeographicUV(LocalDirection(xy));
}
float3 ScrollPosition(float2 meshUV,float shell,out float2 xy,out float clipWeight)
{
 xy=meshUV*_LocalRadius;float3 radial=LocalDirection(xy);float2 uv=OrbisGeographicUV(radial);
 float radius=ScrollTerrainRadius(uv,_MapSize);
 clipWeight=FlatReliefClipWeight(radius-1,_ScrollMorph);
 radial.z=-radial.z;float s=sin(_ScrollSpin),c=cos(_ScrollSpin);
 float3 sphere=float3(c*radial.x+s*radial.z,radial.y,-s*radial.x+c*radial.z)*(radius+shell);
 float3 relief=FlatReliefPosition(xy,radius-1);
 float3 plane=_ScrollNormal*(1+relief.z)+_ScrollRight*relief.x+_ScrollUp*relief.y;
 return lerp(sphere,plane,_ScrollMorph);
}
float3 ScrollPosition(float2 meshUV,float shell,out float2 xy)
{
 float clipWeight;return ScrollPosition(meshUV,shell,xy,clipWeight);
}
#endif
