/*
MIT License

Copyright (c) 2019 Dimas Leenman

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

#ifndef ORBIS_ATMOSPHERE_GLSL_PORT
#define ORBIS_ATMOSPHERE_GLSL_PORT
// Active HLSL adaptation of the USER-UPLOADED Dimas Leenman atmosphere.glsl.
// Original source + SHA256 and a function-by-function port map are in Documentation.
// Coordinate units: planet-local R; coefficients below have already been converted
// from the reference's metres by OrbisAtmosphereProfile.Bind. Do not mix units.
// The Rayleigh / Cornette-Shanks Mie phase and rational ozone profile are from
// calculate_scattering(). Cloud-aware, stable Beer integration is an adaptation.
float _Atmosphere,_AirDensity,_Haze,_SolarEnergy;
float4 _AtmoBetaRayleigh;   // xyz: scattering per R; w: Rayleigh scale height in R
float4 _AtmoBetaMie;        // xyz: scattering per R; w: aerosol scale height in R
float4 _AtmoBetaAbsorption; // xyz: absorption per R; w: ozone peak height in R
float4 _AtmoParameters;    // ozone width (R), Mie g, solar radiance, light sample budget
float2 _AtmoSunDisk;       // cos(angular radius), radiance
#define ORBIS_AIR_RADIUS 1.20

float3 OrbisGLSLDensity(float3 p)
{
    float h=length(p)-1.0;
    if(h<0.0||h>=ORBIS_AIR_RADIUS-1.0)return 0;
    float ray=exp(-h/max(1e-5,_AtmoBetaRayleigh.w));
    float mie=exp(-h/max(1e-5,_AtmoBetaMie.w));
    float denom=(_AtmoBetaAbsorption.w-h)/max(1e-5,_AtmoParameters.x);
    float ozone=ray/(1.0+denom*denom);
    // The reference's extended game-scale profile needs a smooth outer truncation.
    // This same profile is used on both sides of the boundary (no inside/outside fade).
    float top=1.0-smoothstep(.188,.20,h);
    return float3(ray,mie,ozone)*top*max(0.0,_AirDensity)*saturate(_Atmosphere);
}
float3 OrbisGLSLExtinction(float3 rho)
{
    return rho.x*_AtmoBetaRayleigh.xyz+rho.y*_AtmoBetaMie.xyz*max(0.0,_Haze)
          +rho.z*_AtmoBetaAbsorption.xyz;
}
float2 OrbisGLSLPhase(float cosine)
{
    float mu=clamp(cosine,-1.0,1.0),g=clamp(_AtmoParameters.y,-.95,.95);
    float mu2=mu*mu,gg=g*g;
    float ray=3.0/(16.0*ORBIS_PI)*(1.0+mu2);
    // Matches the uploaded GLSL's 3/(8*pi) Cornette-Shanks expression.
    // With rd camera->sample and sun sample->sun, POSITIVE g is forward scattering.
    float mie=3.0/(8.0*ORBIS_PI)*(1.0-gg)*(1.0+mu2)
             /((2.0+gg)*pow(max(1e-4,1.0+gg-2.0*g*mu),1.5));
    return float2(ray,mie);
}
float2 OrbisGLSLViewInterval(float3 ro,float3 rd,float maxDistance)
{
    float2 outer=OrbisSphere(ro,rd,ORBIS_AIR_RADIUS);
    float begin=max(0.0,outer.x),end=min(outer.y,maxDistance);
    // Scene depth clips mountains/buildings; this analytic fallback clips the globe
    // even where there is no depth-writing object. Never integrate inside the planet.
    float2 ground=OrbisSphere(ro,rd,1.0);
    if(ground.y>0.0&&ground.x>=0.0)end=min(end,ground.x);
    if(dot(ro,ro)<.999998)return float2(1,0);
    return float2(begin,end);
}
float3 OrbisGLSLSunTransmittance(float3 p,float3 sun)
{
    // The uploaded reference lacks an explicit solid-planet shadow in its light loop.
    // Keep the v4.1.1 outward-bias fix; hidden sunlight must not illuminate the night side.
    if(OrbisSunVisible(p,sun)<.5)return 0;
    if(_Atmosphere<.5||_AirDensity<=0.0)return 1;
    float2 outer=OrbisSphere(p,sun,ORBIS_AIR_RADIUS);
    float begin=max(0.0,outer.x),end=outer.y;
    if(end<=begin)return 1;
    float closest=clamp(-dot(p,sun),begin,end),total=end-begin;
    float3 tau=0;
    int budget=clamp((int)_AtmoParameters.w,4,24);
    // Split about closest approach and concentrate samples at the dense end.
    // This resolves grazing sunset paths without a large unrolled nested body.
    [loop] for(int side=0;side<2;side++)
    {
        float span=side==0?closest-begin:end-closest;
        if(span<1e-7)continue;
        int count=clamp((int)ceil(budget*span/max(total,1e-6)),4,24);
        [loop] for(int j=0;j<24;j++)
        {
            if(j>=count)break;
            float u0=(float)j/count,u1=(float)(j+1)/count;
            float a=span*u0*u0,b=span*u1*u1;
            float t=closest+(side==0?-1.0:1.0)*(.5*(a+b));
            tau+=OrbisGLSLExtinction(OrbisGLSLDensity(p+sun*t))*(b-a);
        }
    }
    return exp(-min(max(tau,0.0),60.0));
}
float3 OrbisGLSLSource(float3 rho,float2 phase,float3 sunT)
{
    return (rho.x*_AtmoBetaRayleigh.xyz*phase.x+
            rho.y*_AtmoBetaMie.xyz*max(0.0,_Haze)*phase.y)
            *sunT*max(0.0,_AtmoParameters.z)*max(0.0,_SolarEnergy);
}
void OrbisGLSLAccumulate(float3 source,float3 sigma,float ds,inout float3 light,inout float3 transmission)
{
    // Stable exact integral for constant coefficients in THIS segment. Unlike the
    // reference's end-of-step optical-depth sum, the near half-step isn't over-attenuated.
    // Air and cloud extinction are added BEFORE integration; no second blue overlay.
    float3 tau=max(sigma,0.0)*max(ds,0.0);
    float3 stepT=exp(-min(tau,60.0));
    float3 small=tau*(1.0-tau*.5+tau*tau/6.0);
    float3 removed=lerp(small,1.0-stepT,step(.001,tau));
    light+=transmission*source*(removed/max(sigma,1e-8));
    transmission*=stepT;
}
#endif
