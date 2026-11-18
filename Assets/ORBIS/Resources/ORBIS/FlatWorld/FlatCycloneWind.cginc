#ifndef ORBIS_FLAT_CYCLONE_WIND
#define ORBIS_FLAT_CYCLONE_WIND
// XYZ is the pressure centre on the unit sphere, W the simulated wind strength.
float4 _CycloneNorth,_CycloneSouth;
float FlatCycloneRadius(float strength)
{
 return lerp(.115,.218,sqrt(saturate(strength)));
}
float3 FlatCycloneSurfaceWind(float3 up,float4 centre,float spin)
{
 float distance=length(up-centre.xyz);
 float extent=FlatCycloneRadius(centre.w);
 if(centre.w<=.025||distance>=extent*1.15)return 0;
 float r=distance/extent;
 float3 inward=centre.xyz-up*dot(up,centre.xyz);
 inward*=rsqrt(max(1e-7,dot(inward,inward)));
 float speed=centre.w*smoothstep(.015,.12,r)*(1-smoothstep(.16,1.15,r));
 return (cross(inward,up)*spin+inward*.23)*speed*1.9;
}
#endif
