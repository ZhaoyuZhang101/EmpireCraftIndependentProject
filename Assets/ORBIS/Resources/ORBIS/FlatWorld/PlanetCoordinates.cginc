#ifndef ORBIS_PLANET_COORDINATES
#define ORBIS_PLANET_COORDINATES
// Same canonical axes as PlanetCoordinateSystem.cs. Reflection/spin belong to presentation.
float3 PlanetSurfaceNormal(float2 geo)
{
 float lon=(geo.x-.5)*6.283185307179586,lat=(geo.y-.5)*3.141592653589793;
 return float3(cos(lat)*sin(lon),sin(lat),cos(lat)*cos(lon));
}
#endif
