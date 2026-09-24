#ifndef ORBIS_ATMOSPHERE_VIEW
#define ORBIS_ATMOSPHERE_VIEW
float3 _CameraLocal,_RayBL,_RayBR,_RayTL,_RayTR,_Sun;
sampler2D _OrbisSunTex,_OrbisMoonTex;
float _OrbisCelestialReady;
float3 OrbisViewRay(float2 uv)
{
    return lerp(lerp(_RayBL,_RayBR,uv.x),lerp(_RayTL,_RayTR,uv.x),uv.y);
}
float3 OrbisBackgroundWithSun(float3 scene,float2 uv,float eyeDepth)
{
    // Derivatives are only used in this final full-screen operation, outside all loops.
    // In-front geometry is decided by the actual depth buffer, not by turning Mie off.
    float3 rd=normalize(OrbisViewRay(uv));
    float mu=dot(rd,normalize(_Sun)),edge=max(fwidth(mu),1e-6);
    if(eyeDepth<_ProjectionParams.z*.999)return scene;
    float2 planet=OrbisSphere(_CameraLocal,rd,1.0);
    if(planet.y>0.0&&planet.x>=0.0)return scene;
    float disk=smoothstep(_AtmoSunDisk.x-edge,_AtmoSunDisk.x+edge,mu);
    if(_OrbisCelestialReady<.5)
        return scene+disk*max(0.0,_AtmoSunDisk.y)*max(0.0,_SolarEnergy);

    // The supplied SolarSystem art colors the existing optical disc. ORBIS still
    // owns the Sun's direction, angular radius, and atmospheric transmittance.
    float3 sun=normalize(_Sun);
    float3 sunRight=normalize(cross(abs(sun.y)>.98?float3(1,0,0):float3(0,1,0),sun));
    float3 sunUp=cross(sun,sunRight);
    float sunRadius=max(.0001,sqrt(1-_AtmoSunDisk.x*_AtmoSunDisk.x));
    float2 sunXY=float2(dot(rd,sunRight),dot(rd,sunUp))/sunRadius;
    float sunR2=dot(sunXY,sunXY);
    float3 sunNormal=normalize(float3(sunXY,sqrt(max(0,1-sunR2))));
    float2 sunUV=float2(atan2(sunNormal.x,sunNormal.z)/(2*UNITY_PI)+.5,
                        asin(clamp(sunNormal.y,-1,1))/UNITY_PI+.5);
    float3 sunArt=tex2D(_OrbisSunTex,sunUV).rgb;
    // The supplied texture is an orange false-colour photosphere. Use its
    // granulation as gentle luminance detail; the daylight Sun is white-hot.
    float granulation=dot(sunArt,float3(.2126,.7152,.0722));
    float3 sunColor=float3(1.0,.97,.91)*lerp(.92,1.08,saturate(granulation));
    scene+=sunColor*disk*max(0.0,_AtmoSunDisk.y)*max(0.0,_SolarEnergy);

    // The Moon direction and phase are the same values used by night lighting.
    float3 moon=normalize(_OrbisMoon.xyz);
    float3 moonRight=normalize(cross(abs(moon.y)>.98?float3(1,0,0):float3(0,1,0),moon));
    float3 moonUp=cross(moon,moonRight);
    float2 moonXY=float2(dot(rd,moonRight),dot(rd,moonUp))/.00465;
    float moonR2=dot(moonXY,moonXY);
    float moonEdge=max(fwidth(moonR2),.0001);
    float moonDisk=(1-smoothstep(1-moonEdge,1+moonEdge,moonR2))*step(0,dot(rd,moon));
    float3 moonNormal=-moon*sqrt(max(0,1-moonR2))+moonRight*moonXY.x+moonUp*moonXY.y;
    float2 moonUV=float2(atan2(moonXY.x,sqrt(max(0,1-moonR2)))/(2*UNITY_PI)+.5,
                         asin(clamp(moonXY.y,-1,1))/UNITY_PI+.5);
    float moonLit=saturate(dot(normalize(moonNormal),sun));
    float3 moonColor=tex2D(_OrbisMoonTex,moonUV).rgb;
    scene=lerp(scene,moonColor*(.025+2.1*moonLit)*_OrbisMoonColor.rgb,moonDisk);
    return scene;
}
#endif
