#ifndef ORBIS_LIGHTNING_INCLUDED
#define ORBIS_LIGHTNING_INCLUDED
float4 _OrbisLightning[4];
float OrbisLightningTerm(float3 position,float4 strike)
{
    float3 radial=position*rsqrt(max(1e-8,dot(position,position)));
    float3 centre=strike.xyz*rsqrt(max(1e-8,dot(strike.xyz,strike.xyz)));
    float3 distance=radial-centre;
    return clamp(strike.w,0,2)*exp(-dot(distance,distance)/.00055);
}
float OrbisLightningAmount(float3 position)
{
    float activity=max(0,_OrbisLightning[0].w)+max(0,_OrbisLightning[1].w)+max(0,_OrbisLightning[2].w)+max(0,_OrbisLightning[3].w);
    [branch] if(activity<=0)return 0;
    return OrbisLightningTerm(position,_OrbisLightning[0])+OrbisLightningTerm(position,_OrbisLightning[1])+
           OrbisLightningTerm(position,_OrbisLightning[2])+OrbisLightningTerm(position,_OrbisLightning[3]);
}
#endif
