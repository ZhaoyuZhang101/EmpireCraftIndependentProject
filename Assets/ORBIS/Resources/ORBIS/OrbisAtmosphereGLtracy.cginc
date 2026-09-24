#ifndef ORBIS_GLTRACY_V42
#define ORBIS_GLTRACY_V42
// Active V4-2 port of the GLtracy shader supplied by the user, not the historical
// Dimas Leenman include. Original author credit retained; no license is inferred.
// ray_vs_sphere -> OrbisSphere (shared); density -> OrbisGLSLDensity;
// optic -> baked optical column LUT; in_scatter -> shared Beer integrator below.
// Compatibility entry-point names are retained to avoid breaking V4-2 callers.
// Explicit scale adaptation, sampling and provenance: Documentation/V4_2_Repair.md.
float _Atmosphere,_AirDensity,_Haze,_SolarEnergy;
float4 _AtmoBetaRayleigh,_AtmoBetaMie,_AtmoBetaAbsorption,_AtmoParameters;
float2 _AtmoSunDisk;
sampler2D _AtmoOpticalDepth;
float4 _AtmoLutSize;
float _AtmoLutReady;
#define ORBIS_AIR_RADIUS 1.20
float3 OrbisGLSLDensity(float3 p)
{
    float h=length(p)-1.0;
    if(h<0||h>=.20)return 0;
    float2 rho=exp(-h/max(float2(1e-5,1e-5),float2(_AtmoBetaRayleigh.w,_AtmoBetaMie.w)));
    return float3(rho,0)*max(0,_AirDensity)*saturate(_Atmosphere);
}
float3 OrbisGLSLExtinction(float3 rho)
{
    // GLtracy k_mie_ex = 1.1. Absorption is NOT added to scattering radiance.
    return rho.x*_AtmoBetaRayleigh.xyz+rho.y*_AtmoBetaMie.xyz*max(0,_Haze)*1.1;
}
float2 OrbisGLSLPhase(float cosine)
{
    // Original: g=-.78, c=dot(rd,-sun). Equivalent here: +.78 and dot(rd,sun).
    float mu=clamp(cosine,-1,1),g=clamp(_AtmoParameters.y,-.95,.95);
    float mu2=mu*mu,gg=g*g,b=max(1e-4,1+gg-2*g*mu);
    return float2(3*(1+mu2)/(16*ORBIS_PI),
        3*(1-gg)*(1+mu2)/(8*ORBIS_PI*(2+gg)*b*sqrt(b)));
}
float2 OrbisGLSLViewInterval(float3 ro,float3 rd,float maxDistance)
{
    float2 outer=OrbisSphere(ro,rd,ORBIS_AIR_RADIUS);
    float begin=max(0,outer.x),end=min(outer.y,maxDistance);
    float2 ground=OrbisSphere(ro,rd,1.0);
    if(ground.y>0&&ground.x>=0)end=min(end,ground.x);
    if(dot(ro,ro)<.999998)return float2(1,0);
    return float2(begin,end);
}
float3 OrbisGLSLSunTransmittance(float3 p,float3 sun)
{
    if(OrbisSunVisible(p,sun)<.5)return 0;
    if(_Atmosphere<.5||_AirDensity<=0)return 1;
    float r=max(1,length(p));
    if(r>=ORBIS_AIR_RADIUS&&dot(p,sun)>=0)return 1;
    float mu=dot(p/r,sun);
    float horizon=-sqrt(saturate(1-1/(r*r)));
    float2 st=float2(sqrt(saturate((mu-horizon)/max(1e-5,1-horizon))),
                    sqrt(saturate((r-1)/.20)));
    // The LUT stores density integrals (R), not baked color or sun direction.
    // Air/haze controls therefore do not need CPU rebakes each frame.
    float2 uv=(st*(_AtmoLutSize.xy-1)+.5)*_AtmoLutSize.zw;
    float2 column=tex2Dlod(_AtmoOpticalDepth,float4(uv,0,0)).rg;
    if(_AtmoLutReady<.5)
    {
        // Conservative non-nested fallback for editor material previews only.
        float ds=max(0,OrbisSphere(p,sun,ORBIS_AIR_RADIUS).y)/8;
        column=0;
        [loop] [fastopt] for(int i=0;i<8;i++)
        {
            float h=max(0,length(p+sun*((i+.5)*ds))-1);
            column+=exp(-h/max(float2(1e-5,1e-5),float2(_AtmoBetaRayleigh.w,_AtmoBetaMie.w)))*ds;
        }
    }
    float3 tau=(column.x*_AtmoBetaRayleigh.xyz+column.y*_AtmoBetaMie.xyz*max(0,_Haze)*1.1)
                *max(0,_AirDensity)*saturate(_Atmosphere);
    return exp(-min(max(tau,0),60));
}
float3 OrbisGLSLSource(float3 rho,float2 phase,float3 sunT)
{
    return (rho.x*_AtmoBetaRayleigh.xyz*phase.x+rho.y*_AtmoBetaMie.xyz*max(0,_Haze)*phase.y)
            *sunT*max(0,_AtmoParameters.z)*max(0,_SolarEnergy);
}
void OrbisGLSLAccumulate(float3 source,float3 sigma,float ds,inout float3 light,inout float3 transmission)
{
    float3 tau=max(sigma,0)*max(ds,0),stepT=exp(-min(tau,60));
    float3 small=tau*(1-tau*.5+tau*tau/6);
    float3 removed=lerp(small,1-stepT,step(.001,tau));
    light+=transmission*source*removed/max(sigma,1e-8);
    transmission*=stepT;
}
#endif
