#ifndef ORBIS_HP_VOLUME_CLOUD
#define ORBIS_HP_VOLUME_CLOUD

// ORBIS adaptation of HanPi Volume Cloud's phi_fwd isotropic diffuse field.
// Source: HPVolumeCloud/VolumetricClouds.hlsl, HanPi Volume Cloud (AshenOneArt).
float OrbisHPDiffuseStep(float entryTau,float localTau,float distance,float width,float boundary)
{
    const float omega=0.999;
    const float kappa=0.05477225575; // sqrt(3 * (1 - omega))
    float centreTau=entryTau+localTau*.5;
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

// Physical cloud-type altitude bounds (metres above local ground/ocean):
//   type ~ 0.06 (Marine Sc/St) : base =  420 m, top =  1,450 m (ultra-low inversion sheet)
//   type ~ 0.25 (Trade Cu)     : base = 1,040 m, top =  3,050 m (elevated LCL cauliflower puffs)
//   type ~ 0.45 (Congestus/Ac) : base =  860 m, top =  4,600 m (+ detached 3,950-5,950 m Ac/As deck)
//   type ~ 0.85..1.0 (Cb/Typh) : base =  520 m, top = 12,400 m (8.5x taller than Sc + tropopause anvil!)
float2 OrbisCloudVerticalSpan(float type,float coverage)
{
    float sc=1.0-smoothstep(.05,.18,type);
    float cu=smoothstep(.10,.22,type)*(1.0-smoothstep(.48,.70,type));
    float cb=smoothstep(.56,.96,type);
    float baseM=lerp(760.0,420.0,sc);
    baseM=lerp(baseM,1060.0,cu*.88);
    baseM=lerp(baseM,510.0,cb);

    float lowReach=lerp(.65,1.0,smoothstep(.18,.85,coverage));
    float topM=lerp(1380.0,2950.0,smoothstep(.06,.34,type));
    topM=lerp(topM,5200.0,smoothstep(.34,.60,type));
    topM=lerp(topM,12350.0,cb);
    topM=baseM+(topM-baseM)*lerp(lowReach,1.0,cb*.55);
    return float2(baseM,topM);
}

float OrbisHPLowDensityDetailed(float metres,float coverage,float type,float shape,float4 detail,float cell)
{
    // True multi-altitude cloud stratification + conical entrainment dome taper + inverted-Worley
    // cauliflower billows + detached mid-level Altocumulus/Altostratus (Ac/As) deck + Cb tropopause anvil!
    float2 span=OrbisCloudVerticalSpan(type,coverage);
    float sc=(1-smoothstep(.05,.20,type))*.80;
    float domeField=saturate(shape*.58+cell*.42);
    float topVar=lerp(.64,1.16,domeField);
    float localTop=lerp(span.y,span.x+(span.y-span.x)*topVar,1.0-sc*.75);

    float density=0;
    float invWorley=1.0-(detail.r*.62+detail.g*.38);
    float curlyWisp=detail.b*.62+detail.a*.38;
    float baseShape=pow(saturate(shape),.68);

    // 1. Primary cloud body (Marine Sc 420-1450m | Trade Cu 1060-3050m | Towering Cb 510-12400m)
    if(metres>span.x-80.0&&metres<localTop+350.0)
    {
        float localHeight=saturate((metres-span.x)/max(350.0,localTop-span.x));
        float lclRamp=smoothstep(0,.09,localHeight);
        float topCap=1.0-smoothstep(.76,.99,localHeight);
        float profile=lclRamp*topCap;

        // Glaciated cumulonimbus & typhoon eyewall tropopause anvil shelf (incus, ~9.0-12.2 km)
        float anvilFlare=smoothstep(.64,.94,type)*smoothstep(.68,.80,localHeight)*(1-smoothstep(.88,.99,localHeight));
        profile=max(profile,anvilFlare*.72);

        // Cohesive synoptic storm bases without flattening 3D cauliflower billow crowns!
        float overcastCore=smoothstep(.68,.95,coverage);
        float baseCohesion=overcastCore*(1.0-smoothstep(.35,.78,localHeight));
        float shapedCore=lerp(baseShape,max(baseShape,.62+.38*baseShape),baseCohesion*.55);

        // Dry-air entrainment tapers lateral width upward -> rounded cauliflower domes on cumulus & crisp tops on shields
        float entrain=smoothstep(.14,.84,localHeight)*(1.0-.58*anvilFlare)*(1.0-.65*sc)*(1.0-.42*baseCohesion);
        float effCov=lerp(coverage,coverage*(.36+.64*cell),sc)*(1.0-.46*entrain);

        float bottomFade=pow(saturate(localHeight/.15),1.25);
        float upFactor=smoothstep(.10,.46,localHeight);
        float strength=lerp(.22,.40,saturate(type+.28*upFactor))*lerp(.35,1.0,bottomFade);
        float billowDetail=lerp(curlyWisp*.55,invWorley,upFactor)*strength;
        float wispDetail=lerp(curlyWisp,invWorley*.58+curlyWisp*.42,anvilFlare)*strength*1.10;

        float eroded=saturate((shapedCore-billowDetail)/max(.02,1-billowDetail))*profile;
        float erodedWispy=saturate((shapedCore-wispDetail)/max(.02,1-wispDetail))*profile;
        float threshold=1-effCov;
        float billowy=smoothstep(threshold,threshold+.22,eroded);
        float wispy=smoothstep(threshold-.09,threshold+.12,erodedWispy)*lerp(1.0-.65*localHeight,.88,anvilFlare);
        float clip=lerp(1,pow(max(cell,.001),1.35),sc);
        density=lerp(wispy,billowy,smoothstep(0,.22,billowy)*(1-.45*anvilFlare))*clip*coverage*lerp(.86,1.52,type);
    }

    // 2. Detached Mid-Tropospheric Altocumulus / Altostratus (Ac/As) Deck (3,950 - 5,950 m):
    // Floats with a clean ~1,000 m clear air gap above low cumulus and ~2,000 m below high cirrus!
    float midGate=smoothstep(.30,.44,type)*(1.0-smoothstep(.56,.74,type));
    if(midGate>.01&&metres>3800.0&&metres<6150.0)
    {
        float midH=(metres-3950.0)/1950.0;
        float midBand=smoothstep(0,.18,midH)*(1.0-smoothstep(.76,1.0,midH));
        // Mackerel-sky cellular floccus ripples
        float midCell=saturate(cell*.62+(1.0-invWorley)*.38);
        float midCov=coverage*midGate*(.52+.48*midCell);
        float midEroded=saturate((pow(midCell,.72)-curlyWisp*.22)/.78)*midBand;
        float midSheet=smoothstep(1.0-midCov-.08,1.0-midCov+.18,midEroded)*midCov*.92;
        density=max(density,midSheet);
    }
    return density;
}
float OrbisHPLowDensity(float metres,float coverage,float type,float shape,float erosion)
{return OrbisHPLowDensityDetailed(metres,coverage,type,shape,(1-erosion).xxxx,1);}
float OrbisHPHighDensity(float metres,float coverage,float type,float cell,float wisp)
{
    // High-tropospheric Cirrus / Cirrostratus / Typhoon Outflow Canopy (7,800 - 13,200 m):
    // Cleanly elevated above both low cumulus (1-3 km) and mid-level altocumulus (4-6 km)!
    if(coverage<.001)return 0;
    float coverHeight=pow(saturate(coverage),.65);
    float cellStrength=lerp(.25,.85,type),shaped=pow(max(cell,.001),1.35);
    float bottom=8350-1150*coverHeight;
    float top=9100+4050*coverHeight*lerp(1,shaped,cellStrength*.45);
    float band=smoothstep(bottom-350,bottom+350,metres)*(1-smoothstep(top-350,top+350,metres));
    float baseDensity=saturate((coverage-.022)/.30);
    return max(0,baseDensity*lerp(1,shaped,cellStrength)-wisp*wisp*.22*type)*band*coverage*.66;
}

float OrbisHPBoundaryConfidence(float density)
{
    return smoothstep(.018,.16,density);
}
#endif
