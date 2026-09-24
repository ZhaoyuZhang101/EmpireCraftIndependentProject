#ifndef ORBIS_OPTICS
#define ORBIS_OPTICS
#include "UnityCG.cginc"
#include "OrbisCloudCommon.cginc"
#include "OrbisAtmosphereGLtracy.cginc"
float _Night,_Exposure;
#include "OrbisNightLighting.cginc"
float _GodRays,_ShadowStrength;
int _SurfaceDebug;
// Legacy entry points now delegate to the ACTIVE uploaded-GLSL port. All surface,
// water, cloud and atmospheric lighting therefore use the same optical profile.
float3 AirDensities(float3 p){return OrbisGLSLDensity(p);}
float3 AirExtinction(float3 rho){return OrbisGLSLExtinction(rho);}
float3 AirSunTransmittance(float3 p,float3 sun){return OrbisGLSLSunTransmittance(p,sun);}
float3 OrbisDaySkyFill(float3 p,float3 sun)
{
    // Preserve the IPCFix1 daytime/twilight fill, without a hard direct-light gate.
    float mu=dot(normalize(p),sun);
    float day=smoothstep(-.12,.24,mu),dusk=exp(-pow((mu+.035)/.10,2.0));
    return day*float3(.038,.061,.095)+dusk*float3(.009,.010,.014);
}
float3 OrbisSkyFill(float3 p,float3 sun)
{
    return OrbisDaySkyFill(p,sun)+OrbisNightAmbient(p,sun,0);
}
float3 OrbisSurfaceSkyFill(float3 p,float3 normal,float3 sun,float nightTau)
{
    float hemisphere=.16+.84*saturate(.5+.5*dot(normal,normalize(p)));
    return OrbisDaySkyFill(p,sun)+OrbisNightAmbient(p,sun,nightTau)*hemisphere;
}
float3 OrbisTextureAlbedo(float3 c)
{
    #if defined(UNITY_COLORSPACE_GAMMA)
    return GammaToLinearSpace(max(0.0,c));
    #else
    return max(0.0,c);
    #endif
}
float3 OrbisDisplay(float3 color)
{
    color=max(0.0,color)*clamp(_Exposure,.1,4.0);
    color=saturate((color*(2.51*color+.03))/(color*(2.43*color+.59)+.14));
    #if defined(UNITY_COLORSPACE_GAMMA)
    color=LinearToGammaSpace(color);
    #endif
    return color;
}
#endif
