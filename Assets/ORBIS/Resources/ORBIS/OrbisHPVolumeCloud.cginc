#ifndef ORBIS_HP_VOLUME_CLOUD
#define ORBIS_HP_VOLUME_CLOUD

// ORBIS adaptation of HanPi Volume Cloud (HPVolumeCloud) phi_fwd isotropic diffuse field,
// wispy soft-edge erosion, and multi-altitude cloud vertical profiles.
// Upstream: HanPi Volume Cloud © AshenOneArt (https://github.com/AshenOneArt/HPVolumeCloud), MIT License.
// phi_fwd strictly contributes additive diffuse radiance and never writes to cloud density or transmittance/alpha.
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
//   type ~ 0.06 (Marine Sc/St) : base =  560 m, top =  2,200 m (low inversion deck, smoothly resolved by raymarcher!)
//   type ~ 0.25 (Trade Cu)     : base = 1,020 m, top =  3,150 m (elevated LCL cauliflower puffs)
//   type ~ 0.45 (Congestus/Ac) : base =  860 m, top =  4,700 m (+ detached 3,950-5,950 m Ac/As deck)
//   type ~ 0.85..1.0 (Cb/Typh) : base =  510 m, top = 12,400 m (towering Cb + tropopause anvil!)
float2 OrbisCloudVerticalSpan(float type,float coverage)
{
    float sc=1.0-smoothstep(.05,.18,type);
    float cu=smoothstep(.10,.22,type)*(1.0-smoothstep(.48,.70,type));
    float cb=smoothstep(.50,.96,type);
    float baseM=lerp(760.0,560.0,sc);
    baseM=lerp(baseM,1020.0,cu*.88);
    baseM=lerp(baseM,510.0,cb);

    float lowReach=lerp(.76,1.0,smoothstep(.14,.82,coverage));
    float topM=lerp(2200.0,3150.0,smoothstep(.06,.34,type));
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
    float invWorley=1.0-(detail.r*.62+detail.g*.38);
    float curlyWisp=detail.b*.62+detail.a*.38;
    float turretField=saturate(detail.r*.58+detail.g*.42);
    // Keep localTop strictly phase-locked between primary pass and simpleMode (LightField/probe) by
    // driving dome height exclusively from smooth in-phase shape & meso cell fields (never uncorrelated
    // high-frequency detail), producing soft puffy 3D cumulus billows with zero sharp confetti artifacts!
    float cbHigh=smoothstep(.42,.88,type);
    float mesoDome=smoothstep(.22,.84,cell);
    float domeField=lerp(saturate(shape*.56+cell*.44),saturate(shape*.18+mesoDome*.82),cbHigh);
    float topVar=lerp(lerp(.64,.62,cbHigh),lerp(1.08,1.05,cbHigh),domeField);
    // Keep 3D dome relief on marine Sc cobblestone pillows (1.0 - sc * 0.25) instead of flattening them into a tabletop!
    float localTop=min(12950.0,lerp(span.y,span.x+(span.y-span.x)*topVar,1.0-sc*.25));

    float density=0;
    float baseShape=pow(saturate(shape),.68);

    // 1. Primary cloud body (Marine Sc 420-1450m | Trade Cu 1060-3050m | Towering Cb 510-12400m)
    if(metres>span.x-80.0&&metres<localTop+350.0)
    {
        float localHeight=saturate((metres-span.x)/max(350.0,localTop-span.x));
        float lclRamp=smoothstep(0,.18,localHeight);
        float topCap=1.0-smoothstep(.62,.99,localHeight);
        float profile=lclRamp*topCap;

        // Glaciated cumulonimbus & typhoon eyewall tropopause anvil overhang (incus, ~9.0-12.3 km):
        // Soft billowing tropopause canopy using phase-locked meso domes so tops stay pillowy and natural.
        float anvilFlare=smoothstep(.60,.92,type)*smoothstep(.64,.80,localHeight)*(1-smoothstep(.86,.99,localHeight));
        float anvilRelief=anvilFlare*lerp(.56,.82,mesoDome);
        profile=max(profile,anvilRelief);

        // Cohesive synoptic storm bases without flattening 3D cauliflower billow crowns!
        float overcastCore=smoothstep(.70,.96,coverage);
        float baseCohesion=overcastCore*(1.0-smoothstep(.32,.72,localHeight));
        float shapedCore=lerp(baseShape,max(baseShape,.62+.38*baseShape),baseCohesion*.50);

        // Gentle upper-crown relief zone on dense convective tops: recovers soft billowing dimples
        // while preserving fluffy volumetric vapor ramps on thin clouds, marine Sc, and cloud bases.
        float crownZone=smoothstep(.40,.80,localHeight)*smoothstep(.46,.88,coverage)*(1.0-sc*.80);

        // Dry-air entrainment tapers cumulus towers upward, while glaciated anvils flare outward
        float entrain=smoothstep(.14,.84,localHeight)*(1.0-.64*anvilFlare)*(1.0-.65*sc)*(1.0-.42*baseCohesion);
        float effCov=saturate(lerp(coverage,coverage*(.36+.64*cell),sc)*(1.0-.46*entrain)+.12*anvilFlare*coverage);
        effCov=saturate(effCov*(1.0-crownZone*(.05+.11*invWorley)));

        float bottomFade=pow(saturate(localHeight/.16),1.15);
        float upFactor=smoothstep(.08,.44,localHeight);
        float strength=(lerp(.24,.42,saturate(type+.28*upFactor))+.035*crownZone)*lerp(.35,1.0,bottomFade);
        float billowDetail=lerp(curlyWisp*.52,invWorley,upFactor)*strength;
        float wispDetail=lerp(curlyWisp,invWorley*.46+curlyWisp*.54,anvilFlare)*strength*1.06;

        float eroded=saturate((shapedCore-billowDetail)/max(.02,1-billowDetail))*profile;
        float erodedWispy=saturate((shapedCore-wispDetail)/max(.02,1-wispDetail))*profile;
        float threshold=1-effCov;
        // Soft HanPi wispy-reach & translucent cottony vapor ramp: wide feathered rim + rounded puffy core
        float coreWeight=smoothstep(.25,.76,coverage);
        float crownSharpen=coreWeight*smoothstep(.36,.76,localHeight);
        float billowWidth=lerp(.26,.20,crownSharpen);
        float billowy=smoothstep(threshold-.01,threshold+billowWidth,eroded);
        // HPVolumeCloud-style wispy threshold skirt: feathers outer edges smoothly without bleeding haze into clear gyres
        float wispyReach=lerp(.12,.08,sc);
        float gauzeVeil=smoothstep(threshold-wispyReach,threshold+.27,erodedWispy)*lerp(1.0-.40*localHeight,.85,anvilFlare);
        float cellLace=smoothstep(.12,.78,cell);
        float clip=lerp(1,lerp(.20,1.0,pow(max(cellLace,.001),1.08)),sc);
        float veilTerm=gauzeVeil*lerp(.28,.41,anvilFlare)*(1.0-.34*sc)*lerp(1.0,.82,crownSharpen);
        float coreTerm=billowy*lerp(.58,1.0,coreWeight);
        float vaporBody=saturate(veilTerm+coreTerm*(1.0-veilTerm*.48));
        float crownTurret=lerp(1.0,lerp(.84,1.10,saturate(turretField*.55+mesoDome*.45)),crownZone*.38);
        // Full volumetric upper-crown density feather applied AFTER billowy on tall dense convective towers:
        // tapers smoothly toward 0 across upper crown so 3-5 volumetric steps resolve fluffy sunlit cloud depth
        // with zero solid-clay shell or contour rings while preserving 79e6fa2 full-disk albedo & cloud shadows.
        float denseTopMask=smoothstep(.38,.82,coverage)*smoothstep(.32,.74,type);
        float topFeather=1.0-smoothstep(.58,.99,localHeight)*denseTopMask*.96;
        density=lerp(vaporBody,billowy,coreWeight*.34*(1-.52*anvilFlare))*crownTurret*topFeather*clip*coverage*lerp(.92,1.38,type);
    }

    // 2. Detached Mid-Tropospheric Altocumulus / Altostratus (Ac/As) Deck (3,850 - 6,050 m):
    // Soft vertical envelope avoids oblique raymarch slicing while keeping clear air gap from low/high decks.
    float midGate=smoothstep(.30,.44,type)*(1.0-smoothstep(.56,.74,type));
    if(midGate>.01&&metres>3700.0&&metres<6250.0)
    {
        float midH=(metres-3850.0)/2200.0;
        float midBand=smoothstep(0,.28,midH)*(1.0-smoothstep(.68,1.0,midH));
        // Mackerel-sky cellular floccus ripples
        float midCell=saturate(cell*.62+(1.0-invWorley)*.38);
        float midCov=coverage*midGate*(.52+.48*midCell);
        float midEroded=saturate((pow(midCell,.72)-curlyWisp*.22)/.78)*midBand;
        float midSheet=smoothstep(1.0-midCov-.11,1.0-midCov+.22,midEroded)*midCov*.84;
        density=max(density,midSheet);
    }
    return density;
}
float OrbisHPLowDensity(float metres,float coverage,float type,float shape,float erosion)
{return OrbisHPLowDensityDetailed(metres,coverage,type,shape,(1-erosion).xxxx,1);}
float OrbisHPHighDensity(float metres,float coverage,float type,float cell,float wisp)
{
    // High-tropospheric Cirrus / Cirrostratus / Typhoon Outflow Canopy (7,500 - 13,400 m):
    // Wide +/-650 m vertical feather prevents thin-sheet contour slicing in low-orbit oblique views.
    if(coverage<.001)return 0;
    float coverHeight=pow(saturate(coverage),.65);
    float cellStrength=lerp(.25,.85,type),shaped=pow(max(cell,.001),1.35);
    float bottom=8350-1150*coverHeight;
    float top=9200+4050*coverHeight*lerp(1,shaped,cellStrength*.45);
    float band=smoothstep(bottom-650,bottom+650,metres)*(1-smoothstep(top-650,top+650,metres));
    float baseDensity=saturate((coverage-.022)/.30);
    return max(0,baseDensity*lerp(1,shaped,cellStrength)-wisp*wisp*.22*type)*band*coverage*.66;
}

float OrbisHPBoundaryConfidence(float density)
{
    return smoothstep(.018,.16,density);
}
#endif
