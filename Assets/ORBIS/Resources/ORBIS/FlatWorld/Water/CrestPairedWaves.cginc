// Adapted from the owner's Crest 5.6.3 Gerstner.compute, ComputeGerstner.
// Original: Copyright (c) 2024 Wave Harmonic. Supplied Asset Store license applies.
// A derivative-only local evaluation uses four opposing wave pairs instead of a global FFT ocean.
#ifndef ORBIS_CREST_PAIRED_WAVES
#define ORBIS_CREST_PAIRED_WAVES
float2 OrbisCrestSlope(float3 sphere,float time,float depth)
{
 float4 dx=float4(.94,.70,-.35,.22),dz=float4(.34,-.71,.94,.98);
 float4 k=float4(320,610,1100,1900),a=float4(.00011,.00005,.000024,.000011);
 float4 phase=k*(sphere.x*dx+sphere.z*dz+ sphere.y*float4(.2,-.4,.1,.3));
 float4 omega=sqrt(9.81*k*.001);
 float4 slope=-a*k*(sin(phase-omega*time)+.38*sin(phase+omega*time+float4(1,3,5,2)));
 // Some D3D compilers lower tanh(x) to inf/inf at deep-water values.
 // This bounded exponential identity stays finite throughout the 0-4000m depth range.
 float kd=min(8,max(.01,depth)*.18);
 float shoal=sqrt(max(0,1-2/(exp(2*kd)+1)));
 return float2(dot(slope,dx),dot(slope,dz))*shoal;
}
#endif
