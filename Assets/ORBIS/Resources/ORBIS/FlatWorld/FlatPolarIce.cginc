#ifndef ORBIS_FLAT_POLAR_ICE
#define ORBIS_FLAT_POLAR_ICE
float3 FlatIceDirection(float2 uv)
{
 float lon=(uv.x-.5)*6.283185307,lat=(uv.y-.5)*3.141592654;
 return float3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));
}
float FlatPolarIceMask(float2 uv,float water,float climate)
{
 float3 p=FlatIceDirection(uv);float latitude=abs((uv.y-.5)*180);
 // Stable geographic edge, independent of camera, zoom, tile grid and time.
 float edge=2.4*sin(p.x*7+p.z*5)+1.1*sin(p.x*19-p.z*13);
 float boundary=(uv.y>.5?71:67)+water*2;
 // Existing EF ice-cap land climate reinforces the cap; temperate regions stay unchanged.
 float iceClimate=step(29.5,climate)*(1-water)*smoothstep(56,66,latitude);
 return max(iceClimate,smoothstep(boundary+edge,boundary+edge+6,latitude));
}
float FlatIceHash(float3 p)
{p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
float FlatIceNoise(float3 p)
{
 float3 i=floor(p),t=frac(p);t=t*t*(3-2*t);
 float4 a=float4(FlatIceHash(i),FlatIceHash(i+float3(1,0,0)),FlatIceHash(i+float3(0,1,0)),FlatIceHash(i+float3(1,1,0)));
 float4 b=float4(FlatIceHash(i+float3(0,0,1)),FlatIceHash(i+float3(1,0,1)),FlatIceHash(i+float3(0,1,1)),FlatIceHash(i+1));
 return lerp(lerp(lerp(a.x,a.y,t.x),lerp(a.z,a.w,t.x),t.y),lerp(lerp(b.x,b.y,t.x),lerp(b.z,b.w,t.x),t.y),t.z);
}
float3 FlatPolarIceColor(float2 uv,float water)
{
 float3 p=FlatIceDirection(uv);
 float grain=lerp(FlatIceNoise(p*24),FlatIceNoise(p*68),.25);
 float ridges=pow(saturate(1-abs(FlatIceNoise(p*42)-.5)*16),4);
 float3 snow=lerp(float3(.83,.90,.94),float3(.96,.97,.97),grain*.55+.25);
 float3 seaIce=lerp(float3(.76,.87,.90),float3(.90,.95,.96),grain);
 seaIce*=1-ridges*.06;
 return FlatColor(lerp(snow,seaIce,water));
}
#endif
