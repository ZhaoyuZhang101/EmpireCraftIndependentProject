#ifndef ORBIS_HP_VOLUME_CLOUD
#define ORBIS_HP_VOLUME_CLOUD

// ORBIS adaptation of HanPi Volume Cloud's phi_fwd isotropic diffuse field.
// Source: HPVolumeCloud/VolumetricClouds.hlsl, HanPi Volume Cloud (AshenOneArt).
// The supplied source is an HDRP reading edition, not a compilable renderer.
// ORBIS supplies spherical coordinates, weather coverage and replacement noise assets.
// Scene radius is 1, so the geometric 1/r kernel is bounded in these art units.
float OrbisHPDiffuseStep(float entryTau,float localTau,float distance,float width,float boundary)
{
    const float omega=0.999;
    const float kappa=0.05477225575; // sqrt(3 * (1 - omega))
    float centreTau=entryTau+localTau*.5;
    // Q / D in the supplied phi_fwd integral: scattering deposition times
    // transport extinction. .02 converts the radius-1 art scale to a stable kernel.
    float invD=localTau/max(width,.00001)*.02;
    float source=localTau*omega*invD;
    float absorption=exp(-(1-omega)*entryTau);
    float propagation=exp(-kappa*centreTau);
    float multipleBuild=1-exp(-.35*centreTau);
    float inverseDistance=.02/max(max(distance,width*.5),.02);
    return source*absorption*propagation*multipleBuild*inverseDistance*boundary;
}

float OrbisHPCompressDiffuse(float phi)
{
    return (1-exp(-max(0,phi)*.28*1.2))/1.2;
}
float2 OrbisHPLightSegment(float index,float count,float span)
{
    // HanPi geometric cone widths, ratio 2. Full interval coverage at every quality.
    float denominator=max(.001,exp2(count)-1);
    return span*float2(exp2(index)-1,exp2(index+1)-1)/denominator;
}
sampler2D _HPHighDetail;
float4 OrbisHPHighDetail(float2 uv)
{
    float2 warp=(tex2Dlod(_HPHighDetail,float4(uv,0,1)).ba*2-1)*.008;
    uv+=warp;uv.y=clamp(uv.y,.004,.996);
    return tex2Dlod(_HPHighDetail,float4(uv,0,0));
}
float OrbisHPLowDensityDetailed(float metres,float coverage,float type,float shape,float4 detail,float cell)
{
    // HP coverage-driven cloud-top extension, anchored base and bottom noise fade.
    // The source's absent profile LUT is represented by analytic Cu/Tcu/Cb profiles.
    float h=(metres-450)/6200;
    float topScale=lerp(1,1.55,coverage);
    // Vary cloud tops with the same 3D body, rather than extruding one flat slab.
    float topVariation=lerp(.76,1.16,saturate(shape*.75+cell*.25));
    float localHeight=h/(topVariation*(1+(topScale-1)*max(0,h)));
    float sc=(1-smoothstep(.05,.35,type))*.7;
    localHeight=lerp(localHeight,saturate(h/.32),sc);
    coverage=lerp(coverage,coverage*cell,sc);
    float3 profiles=smoothstep(0,.10,localHeight)*(1-smoothstep(float3(.30,.44,.58),float3(.43,.63,.83),localHeight));
    float profile=type<.5?lerp(profiles.r,profiles.g,type*2):lerp(profiles.g,profiles.b,(type-.5)*2);
    float bottomFade=pow(saturate(localHeight/.14),1.5);
    // Keep shape support at the base too; filling it with 1 made every
    // weather patch a solid, flat-bottomed plate with little internal breakup.
    float baseShape=pow(saturate(shape),.62);
    float strength=lerp(.15,.30,type)*bottomFade;
    float billowDetail=(detail.b*.65+detail.a*.35)*strength;
    float wispDetail=(detail.r*.65+detail.g*.35)*strength;
    float eroded=saturate((baseShape-billowDetail)/max(.01,1-billowDetail))*profile;
    float erodedWispy=saturate((baseShape-wispDetail)/max(.01,1-wispDetail))*profile;
    float threshold=1-coverage;
    float billowy=smoothstep(threshold,threshold+.24,eroded);
    float wispy=smoothstep(threshold-.10,threshold+.10,erodedWispy)*pow(1-saturate((h-.5)/.5),2);
    float clip=lerp(1,pow(max(cell,.001),1.4),sc);
    return lerp(wispy,billowy,smoothstep(0,.2,billowy))*clip*coverage*lerp(.8,1.5,type);
}
float OrbisHPLowDensity(float metres,float coverage,float type,float shape,float erosion)
{return OrbisHPLowDensityDetailed(metres,coverage,type,shape,(1-erosion).xxxx,1);}
float OrbisHPHighDensity(float metres,float coverage,float type,float cell,float wisp)
{
    // HP Ac/As: coverage drives both band thickness and cell-modulated density.
    if(coverage<.001)return 0;
    float coverHeight=pow(saturate(coverage),.65);
    float cellStrength=lerp(.25,.85,type),shaped=pow(max(cell,.001),1.35);
    float bottom=7600-1600*coverHeight;
    float top=7600+4600*coverHeight*lerp(1,shaped,cellStrength*.5);
    float band=smoothstep(bottom-350,bottom+350,metres)*(1-smoothstep(top-350,top+350,metres));
    float baseDensity=saturate((coverage-.025)/.32);
    return max(0,baseDensity*lerp(1,shaped,cellStrength)-wisp*wisp*.18*type)*band*coverage*.60;
}

float OrbisHPBoundaryConfidence(float density)
{
    // Diffusion is valid in cloud interiors, not at optically thin edges.
    return smoothstep(.018,.16,density);
}
#endif
