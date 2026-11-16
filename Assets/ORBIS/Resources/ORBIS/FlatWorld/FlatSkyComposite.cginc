#ifndef ORBIS_FLAT_SKY_COMPOSITE
#define ORBIS_FLAT_SKY_COMPOSITE
#include "FlatCloudCover.cginc"
float3 _FlatSun;
float _SkyReady,_FlatClouds,_FlatCloudShadows,_FlatAtmosphere,_FlatDayNight,_CloudOpacity,_FlatRain,_FlatLightning,_FlatNightLights;
float _FlatNightAdaptation;
float4 _FlatLightningEvents[4];
// Same moon ephemeris and linear light colors as the spherical view.
float4 _OrbisNightParams,_OrbisMoon,_OrbisMoonColor,_OrbisNightSkyColor,_OrbisAirglowColor;
float FlatDayWeight(float3 radial)
{ return lerp(1,smoothstep(-.13,.18,dot(radial,normalize(_FlatSun+float3(0,1e-7,0)))),_FlatDayNight); }
float3 FlatDayLight(float3 radial,float2 uv)
{
    float day=FlatDayWeight(radial);
    float4 overhead=FlatCloudColumn(uv);
    float cover=saturate(overhead.r*.8+overhead.g*.25)*_FlatCloudShadows*_SkyReady;
    float moonAltitude=saturate(dot(radial,_OrbisMoon.xyz));
    float moonTransmission=exp(-cover*max(0,_OrbisNightParams.w)*2/max(.20,moonAltitude));
    // Gradual dark adaptation while unfolding; ambient light retains map readability
    // at new moon. Direct moonlight still vanishes below the local horizon.
    float adaptation=lerp(1,2.5,saturate(_FlatNightAdaptation));
    float3 sky=(_OrbisNightSkyColor.rgb*_OrbisNightParams.y*adaptation+
                _OrbisAirglowColor.rgb*_OrbisNightParams.z*.15)*(1-cover*.25);
    float3 moon=_OrbisMoonColor.rgb*_OrbisMoon.w*moonAltitude*moonTransmission;
    float3 nightLight=(sky+moon)*saturate(_OrbisNightParams.x);
    #ifdef UNITY_COLORSPACE_GAMMA
    // Flat map artwork is composed in display space, then converted to linear by
    // the scroll surface. Applying a linear 0.05 directly to sRGB albedo crushed
    // that light a second time. Encode the irradiance before that multiplication.
    nightLight=LinearToGammaSpace(max(0,nightLight));
    #endif
    return lerp(nightLight,float3(.94,.97,1),day);
}
float FlatRainHash(float2 p)
{return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float FlatRainStreaks(float2 pixel,float clock,float2 wind,float rain)
{
    float2 q=pixel/float2(19,48);
    q.y+=clock*7.5;q.x-=clock*wind.x*.18;
    float2 id=floor(q),f=frac(q);
    float seed=FlatRainHash(id),head=.15+.65*FlatRainHash(id+17.3);
    float side=.12+.76*FlatRainHash(id+43.9);
    float length=.15+.42*FlatRainHash(id+71.4);
    float slant=(f.y-head)*wind.x*.22;
    float stroke=1-smoothstep(.018,.050,abs(f.x-side-slant));
    float section=smoothstep(head-.04,head,f.y)*(1-smoothstep(head+length-.04,head+length,f.y));
    float first=stroke*section*step(1-rain*.58,seed);
    float2 q2=pixel/float2(29,67);q2.y+=clock*5.1;q2.x-=clock*wind.x*.12;
    float2 id2=floor(q2),f2=frac(q2);float seed2=FlatRainHash(id2+93.1);
    float x2=.12+.76*FlatRainHash(id2+4.7),y2=.12+.67*FlatRainHash(id2+67.2);
    float second=(1-smoothstep(.012,.038,abs(f2.x-x2-(f2.y-y2)*wind.x*.15)))*
        smoothstep(y2-.04,y2,f2.y)*(1-smoothstep(y2+.25,y2+.29,f2.y))*step(1-rain*.45,seed2);
    return saturate(first*.8+second*.55);
}
float FlatRainSplash(float2 cell,float clock,float rain)
{
    float2 q=cell*2,id=floor(q),f=frac(q);
    float seed=FlatRainHash(id+18.7),phase=frac(clock*1.9+seed);
    float2 centre=float2(.23+.54*FlatRainHash(id+5.1),.23+.54*FlatRainHash(id+8.4));
    float radius=phase*.22,ring=1-smoothstep(.012,.047,abs(length(f-centre)-radius));
    return ring*(1-phase)*step(1-rain*.65,seed);
}
float FlatSnowflakes(float2 pixel,float clock,float snow)
{
    float flakes=0;
    [unroll] for(int layer=0;layer<2;layer++)
    {
        float scale=lerp(32,47,layer),speed=lerp(.65,.38,layer);
        float2 q=pixel/scale+float2(clock*.13,clock*speed);
        float2 id=floor(q),f=frac(q);
        float seed=FlatRainHash(id+layer*81.7);
        float2 centre=float2(.27+.46*FlatRainHash(id+14.3),.27+.46*FlatRainHash(id+41.9));
        centre.x+=sin(clock*1.1+seed*6.283)*.13;
        float radius=lerp(.025,.060,FlatRainHash(id+3.4));
        float disc=1-smoothstep(radius*.35,radius,length(f-centre));
        flakes+=disc*step(1-snow*.72,seed)*lerp(.85,.6,layer);
    }
    return saturate(flakes);
}
float3 FlatSkyComposite(float3 col,float2 uv,float2 cell,float2 screenPixel,float waterMask,float pixelsPerTile)
{
    float3 radial=FlatRadial(uv);float day=FlatDayWeight(radial);
    float4 cloud=FlatCloudColumn(uv);
    // Black translucent cloud shadow, shared with the spherical terrain.
    float shade=FlatCloudShadowOpacity(uv,normalize(_FlatSun),_FlatCloudShadows*_SkyReady);
    col*=1-shade*.52*day;
    float atmosphereAmount=_FlatAtmosphere*lerp(.045,.015,smoothstep(2,16,pixelsPerTile));
    float3 haze=lerp(FlatColor(float3(.13,.19,.28)),FlatColor(float3(.53,.67,.77)),day);
    col=lerp(col,haze,atmosphereAmount);
    float dusk=exp(-abs(dot(radial,_FlatSun))/.065)*_FlatDayNight*_FlatAtmosphere;
    col+=FlatColor(float3(.36,.20,.12))*dusk*.025;
    float low=cloud.r,high=cloud.g;
    float alpha=(1-(1-low*.82)*(1-high*.35))*_CloudOpacity*_FlatClouds*_SkyReady;
    alpha*=1-smoothstep(16,56,pixelsPerTile)*.86;
    float3 cloudColor=lerp(FlatColor(float3(.32,.39,.48)),FlatColor(float3(.90,.94,.97)),day);
    cloudColor*=lerp(1,.74,low);
    col=lerp(col,cloudColor,saturate(alpha));
    float rain=cloud.a*smoothstep(.65,.90,low)*_FlatRain*_FlatClouds*_SkyReady*smoothstep(8,20,pixelsPerTile);
    float2 wind=float2(.28*sin(cell.y*.025+_SkyClock*.11),0);
    float streak=FlatRainStreaks(screenPixel,_SkyClock,wind,rain);
    col=lerp(col,haze,rain*.045+streak*rain*.42);
    float splash=FlatRainSplash(cell,_SkyClock,rain)*(1-waterMask);
    col=lerp(col,FlatColor(float3(.72,.81,.87)),splash*rain*.20);
    float snow=cloud.b*_FlatRain*_FlatClouds*_SkyReady*smoothstep(8,20,pixelsPerTile);
    float flakes=FlatSnowflakes(screenPixel,_SkyClock,snow);
    col=lerp(col,FlatColor(float3(.94,.97,1)),flakes*snow*.78);
    // Only physical lightning events emitted by mature, charged rain clouds.
    float flash=0;
    [unroll] for(int k=0;k<4;k++)
    {float3 d=radial-_FlatLightningEvents[k].xyz;flash+=_FlatLightningEvents[k].w*exp(-dot(d,d)/.00055);}
    col+=flash*_FlatLightning*_SkyReady*float3(.32,.39,.51);
    return col;
}
#endif
