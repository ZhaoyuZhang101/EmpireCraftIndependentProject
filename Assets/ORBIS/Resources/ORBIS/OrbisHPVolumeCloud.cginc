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
    // HPVolumeCloud pow(0.58) creates fuller, rounder, plumper cumulus domes before silhouette erosion
    float baseShape=pow(saturate(shape),.58);

    // 1. Primary cloud body (Marine Sc 560-2200m | Trade Cu 1020-3150m | Towering Cb 510-12400m)
    if(metres>span.x-80.0&&metres<localTop+350.0)
    {
        float localHeight=saturate((metres-span.x)/max(350.0,localTop-span.x));
        float lclRamp=smoothstep(0,.14,localHeight);
        float topCap=1.0-smoothstep(.52,.99,localHeight);
        float profile=lclRamp*topCap;

        // Glaciated cumulonimbus & typhoon eyewall tropopause anvil overhang (incus, ~9.0-12.3 km):
        // Soft billowing tropopause canopy using phase-locked meso domes so tops stay pillowy and natural.
        float anvilFlare=smoothstep(.60,.92,type)*smoothstep(.64,.80,localHeight)*(1-smoothstep(.86,.99,localHeight));
        float anvilRelief=anvilFlare*lerp(.56,.82,mesoDome);
        profile=max(profile,anvilRelief);

        // HPVolumeCloud-style cloud-base smoothing: keeps cumulus undersides smooth & readable (LCL base)
        // while letting mid/upper domes puff freely into round 3D cauliflower pillows!
        float bottomSmooth=pow(saturate(localHeight/.18),1.25);
        float baseCoherent=lerp(lerp(baseShape,max(baseShape,.76),.50*(1.0-sc)),baseShape,bottomSmooth);

        // Cohesive synoptic storm bases without flattening 3D cauliflower billow crowns!
        float overcastCore=smoothstep(.70,.96,coverage);
        float baseCohesion=overcastCore*(1.0-smoothstep(.32,.72,localHeight));
        float shapedCore=lerp(baseCoherent,max(baseCoherent,.62+.38*baseCoherent),baseCohesion*.48);

        // Gentle upper-crown relief zone on dense convective tops: recovers soft billowing dimples
        // while preserving fluffy volumetric vapor ramps on thin clouds, marine Sc, and cloud bases.
        float crownZone=smoothstep(.38,.78,localHeight)*smoothstep(.46,.88,coverage)*(1.0-sc*.80);

        // Dry-air entrainment tapers cumulus towers upward, while glaciated anvils flare outward
        float entrain=smoothstep(.14,.84,localHeight)*(1.0-.64*anvilFlare)*(1.0-.65*sc)*(1.0-.42*baseCohesion);
        // On high-coverage solid marine Sc decks (>0.52), keep a continuous cobblestone floor to prevent blue pinholes
        // while keeping full lacy cellular separation on broken/open-cell Sc (coverage < 0.52)!
        float scDense=sc*smoothstep(.52,.82,coverage);
        float scCellFloor=lerp(.38,.80,scDense);
        float effCov=saturate(lerp(coverage,coverage*(scCellFloor+(1.0-scCellFloor)*cell),sc)*(1.0-.44*entrain)+.12*anvilFlare*coverage);
        effCov=saturate(effCov*(1.0-crownZone*(.05+.12*invWorley)));

        float bottomFade=pow(saturate(localHeight/.18),1.20);
        float upFactor=smoothstep(.08,.44,localHeight);
        // High-frequency detail trims outer silhouettes & upper crowns without punching holes through plump interior cores!
        float silhouetteMask=lerp(1.0,.42,smoothstep(.58,.90,shapedCore*profile));
        float strength=(lerp(.32,.50,saturate(type+.34*upFactor))+.04*crownZone)*lerp(.22,1.0,bottomFade)*silhouetteMask;
        float billowDetail=lerp(curlyWisp*.46,invWorley,upFactor)*strength;
        float wispDetail=lerp(curlyWisp,invWorley*.42+curlyWisp*.58,anvilFlare)*strength*1.06;

        float eroded=saturate((shapedCore-billowDetail)/max(.02,1-billowDetail))*profile;
        float erodedWispy=saturate((shapedCore-wispDetail)/max(.02,1-wispDetail))*profile;
        float threshold=1-effCov;
        // HPVolumeCloud soft wispy-reach & gradual optical-depth rim transition:
        // outer rim feathers softly by optical thickness while plump cores retain full weather-driven cloud cover!
        float coreWeight=smoothstep(.24,.76,coverage);
        float crownSharpen=coreWeight*smoothstep(.36,.78,localHeight);
        float billowWidth=lerp(.28,.20,crownSharpen);
        float billowy=smoothstep(threshold-.01,threshold+billowWidth,eroded);
        float wispyReach=lerp(.14,.08,sc);
        float gauzeVeil=smoothstep(threshold-wispyReach,threshold+.28,erodedWispy)*lerp(pow(1.0-localHeight*.50,1.15),.86,anvilFlare);
        float cellLace=smoothstep(.12,.78,cell);
        float clip=lerp(1,lerp(lerp(.22,.78,scDense),1.0,pow(max(cellLace,.001),1.04)),sc);
        float veilTerm=gauzeVeil*lerp(.32,.42,anvilFlare)*(1.0-.30*sc)*lerp(1.0,.86,crownSharpen);
        float billowyByShape=smoothstep(0.0,.38,billowy);
        float coreTerm=billowy*lerp(.68,1.0,coreWeight);
        float vaporBody=lerp(veilTerm,max(veilTerm*.76,coreTerm),billowyByShape);
        vaporBody*=lerp(.68+.32*vaporBody,1.0,smoothstep(.15,.52,vaporBody));
        float crownTurret=lerp(1.0,lerp(.84,1.12,saturate(turretField*.55+mesoDome*.45)),crownZone*.42);
        // Upper-rim volumetric feather (top 26% only) keeps cumulus & typhoon eyewall sides plump and sculptural
        float denseTopMask=smoothstep(.38,.82,coverage)*smoothstep(.30,.72,type);
        float topFeather=1.0-smoothstep(.74,.99,localHeight)*denseTopMask*.72;
        density=lerp(vaporBody,billowy,coreWeight*.28*(1-.55*anvilFlare))*crownTurret*topFeather*clip*coverage*lerp(.94,1.38,type);
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
