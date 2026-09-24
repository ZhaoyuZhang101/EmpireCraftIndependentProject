#ifndef ORBIS_WATER
#define ORBIS_WATER
#include "FlatWorld/Water/HPWaterOptics.cginc"
// Wave family adapted from "Very fast procedural ocean", afl_ext (2017-2023), MIT.
// Shadertoy MdXyzX. Attribution and MIT terms: Documentation/ThirdPartyNotices.md.
// ORBIS changes: 3D spherical domain, analytic warp Jacobian, individually wrapped
// wave phases, existing-mesh rendering, river flow, depth absorption, shared cloud shadows.
float _WavePhases[10];
float _EnhancedWater,_WaveStrength,_WaterRoughness,_RiverPhase;
int _WaterWaveCount;
float3 OrbisWaterOpticalPosition(float3 p)
{
    // Chord interpolation can lie inside radius 1 although ALL vertices are on sea level.
    // Fix the surface caller, never relax OrbisSunVisible for volume/underground samples.
    float r=length(p);return p*(max(1.00002,r)/max(1e-6,r));
}

float4 OrbisWave(float3 position,float3 up,float3 riverFlow,float river)
{
    float3 q=position*24.0;
    float3 jx=float3(1,0,0),jy=float3(0,1,0),jz=float3(0,0,1);
    float frequency=1,weight=1,sum=0,weightSum=0;
    float3 gradient=0;
    float footprint=max(length(ddx(q)),length(ddy(q)));
    [loop] for(int k=0;k<10;k++)
    {
        if(k>=clamp(_WaterWaveCount,4,10))break;
        float angle=k*2.39996323;
        float3 direction=normalize(float3(sin(angle+.2),cos(angle*.73+1.4),cos(angle)));
        
        // Keep a non-zero 3D component so the sphere has no polar or UV seams.
        direction=normalize(direction+riverFlow*river*.85);
        float phase=dot(direction,q)*frequency-_WavePhases[k];
        float sn=sin(phase),cs=cos(phase),wave=exp(sn-1.0);
        float3 gradPhase=frequency*float3(dot(direction,jx),dot(direction,jy),dot(direction,jz));
        float aa=1-smoothstep(.7,2.7,footprint*frequency);
        sum+=wave*weight*aa;weightSum+=weight*aa;
        gradient+=wave*cs*gradPhase*weight*aa;
        // Analytic derivative through the domain warp, not just through the unwarped sine.
        float drag=-wave*cs*weight*.38;
        float3 gradDrag=wave*(sn-cs*cs)*gradPhase*weight*.38;
        q+=direction*drag;jx+=direction*gradDrag.x;jy+=direction*gradDrag.y;jz+=direction*gradDrag.z;
        frequency*=1.18;weight*=.8;
    }
    gradient/=max(.001,weightSum);gradient-=up*dot(up,gradient);
    return float4(gradient,sum/max(.001,weightSum));
}
// HPWater reference principles (not HDRP GBuffer/RenderGraph code): separate
// interface reflection, refracted path thickness and Beer absorption. This Built-in
// variant uses the existing V4-2 bathymetry proxy, NOT screen-space ray-traced refraction.
float3 OrbisWaterSky(float3 direction,float3 up,float3 sun,float daylight,float nightTau)
{
    float height=saturate(dot(direction,up));
    float horizon=pow(1-height,3);
    float3 sky=lerp(float3(.045,.16,.30),float3(.30,.43,.52),horizon);
    float3 probe=normalize(up+direction*.20);
    float cloud=saturate(tex2Dlod(_Weather,float4(OrbisWeatherUV(probe),0,0)).r*_Cover)*_Clouds;
    sky=lerp(sky,float3(.50,.55,.60),cloud*.32*height);
    float disk=pow(saturate(dot(direction,sun)),900);
    float3 solar=AirSunTransmittance(up*1.001,sun);
    // No hand-painted orange horizon; spectral attenuation owns sunset color.
    float3 nightReflection=OrbisNightAmbient(up*1.001,sun,nightTau);
    return sky*daylight+nightReflection*(.45+.55*horizon)+disk*solar*1.5*daylight;
}
float OrbisWaterFresnel(float cosine)
{
    const float eta=1.333;
    float ci=saturate(cosine),ct=sqrt(max(0,1-(1-ci*ci)/(eta*eta)));
    float rs=(ci-eta*ct)/max(1e-5,ci+eta*ct);
    float rp=(eta*ci-ct)/max(1e-5,eta*ci+ct);
    return saturate(.5*(rs*rs+rp*rp));
}
float3 OrbisShadeWater(float3 p,float3 geometricNormal,float3 viewDir,float3 sun,
                      float4 waterData,float3 flow,float shadow,float inspection)
{
    p=OrbisWaterOpticalPosition(p);
    float3 up=normalize(p);float river=saturate(waterData.y);
    geometricNormal=normalize(geometricNormal+up*1e-7);
    if(dot(geometricNormal,up)<0)geometricNormal=-geometricNormal;
    float3 baseNormal=normalize(lerp(up,geometricNormal,river*.6));
    float3 tangentFlow=flow-up*dot(up,flow);
    tangentFlow*=rsqrt(max(1e-8,dot(tangentFlow,tangentFlow)));
    float4 wave=_EnhancedWater>.5?OrbisWave(p,up,tangentFlow,river):float4(0,0,0,.5);
    [branch] if(river>.001 && _EnhancedWater>.5)
    {
    float pa=frac(_RiverPhase),pb=frac(_RiverPhase+.5),blend=abs(pa*2-1);
    float speed=max(.25,waterData.w),frequency=800;
    float3 q0=(p-tangentFlow*pa*.08*speed)*frequency,q1=(p-tangentFlow*pb*.08*speed)*frequency;
    float3 rip0=float3(cos(q0.x)*sin(q0.z),cos(q0.y)*sin(q0.x),cos(q0.z)*sin(q0.y));
    float3 rip1=float3(cos(q1.x)*sin(q1.z),cos(q1.y)*sin(q1.x),cos(q1.z)*sin(q1.y));
    float3 ripple=lerp(rip0,rip1,blend);ripple-=up*dot(up,ripple);
    float aa=1-smoothstep(.8,3,max(length(ddx(p*frequency)),length(ddy(p*frequency))));
    wave.xyz+=ripple*river*.28*aa*step(.5,_EnhancedWater);
    }
    float3 slope=wave.xyz*clamp(_WaveStrength,0,2)*lerp(.19,.09,river);
    slope-=baseNormal*dot(slope,baseNormal);
    slope*=min(1,.45/max(1e-5,length(slope)));
    // At grazing views, slopes may never invert the visible interface hemisphere.
    float geometricNV=max(0,dot(baseNormal,viewDir));
    slope*=smoothstep(.015,.28,geometricNV);
    float3 n=normalize(baseNormal-slope);
    float nv=max(.001,dot(n,viewDir)),nl=max(0,dot(n,sun));
    float variance=dot(ddx(n),ddx(n))+dot(ddy(n),ddy(n));
    float roughness=clamp(sqrt(pow(lerp(.20,.29,river)*_WaterRoughness,2)+min(.25,variance)*.35),.16,.72);
    float a2=pow(roughness,4);
    float3 h=normalize(sun+viewDir+up*1e-6);
    float nh=saturate(dot(n,h)),vh=saturate(dot(viewDir,h));
    float denominator=nh*nh*(a2-1)+1;
    float distribution=a2/(ORBIS_PI*max(1e-6,denominator*denominator));
    float ggxV=2*nv/(nv+sqrt(a2+(1-a2)*nv*nv));
    float ggxL=2*nl/max(1e-5,nl+sqrt(a2+(1-a2)*nl*nl));
    float F=OrbisWaterFresnel(nv),specF=OrbisWaterFresnel(vh);
    float spec=distribution*ggxV*ggxL*specF/max(1e-4,4*nv*max(.001,nl));
    float depth=max(0,waterData.x);
    // Snell's law bounds optical path length even as the above-water view grazes.
    float refractedCos=sqrt(max(.001,1-(1-geometricNV*geometricNV)/(1.333*1.333)));
    float opticalLength=depth/refractedCos;
    float3 deep=float3(.006,.047,.079),bottom=lerp(float3(.12,.32,.27),float3(.105,.16,.115),river);
    float3 absorption=float3(.16,.065,.032),scattering=absorption*deep/max(.001,1-deep);
    float3 transmission;
    // Same adapted HPWater segment integral as the new 2D river/lake material.
    float3 bodyLight=OrbisHPScatter(float3(1,1,1),absorption,scattering,opticalLength,transmission);
    float3 waterColor=bottom*transmission+bodyLight;
    float3 solar=AirSunTransmittance(p+up*.0002,sun)*max(0,_SolarEnergy);
    float daylight=smoothstep(-.06,.18,dot(up,sun));
    daylight=lerp(daylight,1,inspection*.65);
    float nightTau=OrbisNightWeight(p,sun)>1e-5?OrbisNightColumnTau(p):0;
    float3 skyFill=OrbisSurfaceSkyFill(p,baseNormal,sun,nightTau);
    float3 moonT=OrbisMoonIncident(p+up*.0002,sun,nightTau);
    float moonNL=max(0,dot(n,_OrbisMoon.xyz));
    float3 illumination=skyFill+solar*max(0,dot(up,sun))*shadow*1.25+
        moonT*max(0,dot(up,_OrbisMoon.xyz));
    illumination=lerp(illumination,max(illumination,float3(.40,.40,.40)),inspection);
    float3 reflected=reflect(-viewDir,n);
    // Prevent a shading-normal error from reflecting the lower hemisphere as sky.
    reflected=normalize(reflected+up*max(0,.005-dot(reflected,up)));
    float3 color=waterColor*illumination*(1-F)+OrbisWaterSky(reflected,up,sun,daylight,nightTau)*F;
    color+=spec*nl*shadow*solar;
    float3 moonH=normalize(_OrbisMoon.xyz+viewDir+up*1e-6);
    float moonNH=saturate(dot(n,moonH)),moonVH=saturate(dot(viewDir,moonH));
    float moonDenom=moonNH*moonNH*(a2-1)+1;
    float moonD=a2/(ORBIS_PI*max(1e-6,moonDenom*moonDenom));
    float moonG=2*moonNL/max(1e-5,moonNL+sqrt(a2+(1-a2)*moonNL*moonNL));
    float moonSpec=moonD*ggxV*moonG*OrbisWaterFresnel(moonVH)/max(1e-4,4*nv*max(.001,moonNL));
    color+=moonSpec*moonNL*moonT;
    // Continuous bathymetry-driven surf, not a checkerboard, tile UV or white ocean mask.
    // Depth is the existing gameplay proxy, not a surveyed depth in metres.
    float shore=(1-smoothstep(.25,4.5,depth))*(1-river);
    float shorePhase=depth*1.8+dot(up,float3(31,19,23))*.30+_RiverPhase*12.5663706;
    float crest=pow(saturate(.5+.5*cos(shorePhase)),8);
    float breakup=.62+.38*sin(dot(up,float3(397,271,359))+_WavePhases[0]);
    float crestAA=1-smoothstep(.65,2.0,fwidth(shorePhase));
    float wash=(1-smoothstep(.05,.65,depth))*(.30+.20*sin(_RiverPhase*12.5663706));
    float banks=river*pow(saturate(abs(waterData.z*2-1)),8);
    float ice=saturate(tex2Dlod(_GroundField,float4(OrbisWeatherUV(up),0,0)).g);
    float foam=saturate((shore*(crest*breakup*.78*crestAA+wash)+banks*smoothstep(.42,.76,wave.w)*.25)*(1-ice));
    color=lerp(color,float3(.62,.70,.70)*(skyFill+solar*nl*shadow+moonT*moonNL),foam);
    return max(0,color);
}
#endif
