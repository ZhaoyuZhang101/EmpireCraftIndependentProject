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
    float multipleBuild=1-exp(-.40*centreTau);
    float inverseDistance=.02/max(max(distance,width*.5),.02);
    return source*absorption*propagation*multipleBuild*inverseDistance*boundary;
}

float OrbisHPCompressDiffuse(float phi)
{
    return (1-exp(-max(0,phi)*.28*1.2))/1.2;
}
float2 OrbisHPLightSegment(float index,float count,float span)
{
    // Concentrates near-field light steps inside each cumulus billow (especially at q=1 Balanced 4-step
    // lighting) while keeping exact telescoping coverage = span across 0..count-1 for long-range shadows.
    float segBase=lerp(2.45,1.95,saturate((count-3.0)/5.0));
    float logBase=log2(segBase);
    float denominator=max(.001,exp2(count*logBase)-1.0);
    return span*float2(exp2(index*logBase)-1.0,exp2((index+1.0)*logBase)-1.0)/denominator;
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
    // PlanetVolumetricCloudsV3 + HPVolumeCloud frequency-separated density model:
    //   1) Broad, rounded macro/meso cloud body with smooth multi-step upper crown roll-off (no hard tabletop shelf).
    //   2) Strictly subtractive outer-rind detail erosion (silhouetteMask -> 0 inside cores: zero interior pockmarks
    //      or high-frequency roof dimples that would scatter LightField into bright speckles).
    float2 span=OrbisCloudVerticalSpan(type,coverage);
    float sc=(1-smoothstep(.05,.20,type))*.80;
    float invWorley=1.0-(detail.r*.66+detail.g*.34);
    float curlyWisp=detail.b*.78+detail.a*.22;

    float cbHigh=smoothstep(.42,.88,type);
    float mesoDome=smoothstep(.18,.86,cell);
    float domeField=lerp(saturate(shape*.58+cell*.42),saturate(shape*.22+mesoDome*.78),cbHigh);
    float topVar=lerp(.64,1.06,domeField);
    float localTop=min(12950.0,lerp(span.y,span.x+(span.y-span.x)*topVar,1.0-sc*.22));

    float density=0;
    // Crisp sculpted dome curve (PlanetVolumetricCloudsV3 style) -> clear cumulus lobes with smooth interior gradation
    float baseShape=pow(saturate(shape),.56);

    // 1. Primary cloud body (Marine Sc 560-2200m | Trade Cu 1020-3150m | Towering Cb 510-12400m)
    if(metres>span.x-100.0&&metres<localTop+380.0)
    {
        float localHeight=saturate((metres-span.x)/max(380.0,localTop-span.x));
        // Gentle LCL base ramp prevents abrupt luminance jump during vertical wheel cloud-base exit while keeping upper crowns crisp
        float lclRamp=smoothstep(-.04,.24,localHeight);
        // Sculpted PlanetVolumetricCloudsV3 upper-crown profile keeps cauliflower turrets well-defined
        float topCap=1.0-smoothstep(.52,.995,localHeight);
        float profile=lclRamp*topCap;

        // Glaciated cumulonimbus & typhoon eyewall tropopause anvil overhang (incus, ~9.0-12.3 km)
        float anvilFlare=smoothstep(.60,.92,type)*smoothstep(.62,.80,localHeight)*(1-smoothstep(.85,.995,localHeight));
        float anvilRelief=anvilFlare*lerp(.52,.78,mesoDome);
        profile=max(profile,anvilRelief);

        // Smooth cloud-base LCL cohesion without hard plateau shelves
        float bottomSmooth=pow(saturate(localHeight/.20),1.20);
        float softBase=smoothstep(.04,.88,baseShape);
        float baseCoherent=lerp(lerp(baseShape,softBase,.34*(1.0-sc)),baseShape,bottomSmooth);

        float overcastCore=smoothstep(.70,.96,coverage);
        float baseCohesion=overcastCore*(1.0-smoothstep(.30,.72,localHeight));
        float shapedCore=lerp(baseCoherent,saturate(baseCoherent*.78+.22),baseCohesion*.38);

        // Dry-air entrainment tapers cumulus towers upward, while glaciated anvils flare outward
        float entrain=smoothstep(.14,.86,localHeight)*(1.0-.64*anvilFlare)*(1.0-.65*sc)*(1.0-.40*baseCohesion);
        float scDense=sc*smoothstep(.52,.82,coverage);
        float scCellFloor=lerp(.40,.82,scDense);
        float effCov=saturate(lerp(coverage,coverage*(scCellFloor+(1.0-scCellFloor)*cell),sc)*(1.0-.42*entrain)+.12*anvilFlare*coverage);

        float bottomFade=pow(saturate(localHeight/.18),1.15);
        float upFactor=smoothstep(.08,.44,localHeight);
        float coreBody=shapedCore*profile;
        // PlanetVolumetricCloudsV3 crisp outer-rind Worley erosion:
        // carves sharp cauliflower florets along cloud silhouettes (< 0.42) while keeping deep cores (> 0.78) pockmark-free!
        float silhouetteMask=1.0-smoothstep(.36,.78,coreBody);
        float strength=lerp(.30,.46,saturate(type+.30*upFactor))*lerp(.24,1.0,bottomFade)*silhouetteMask;
        float billowDetail=lerp(curlyWisp*.36+invWorley*.64,invWorley,upFactor)*strength;
        float wispDetail=lerp(curlyWisp*.65+invWorley*.35,invWorley*.42+curlyWisp*.58,anvilFlare)*strength*1.04;

        float eroded=saturate((shapedCore-billowDetail)/max(.04,1-billowDetail))*profile;
        float erodedWispy=saturate((shapedCore-wispDetail)/max(.04,1-wispDetail))*profile;
        float threshold=1-effCov;

        // Crisp cumulus edge ramp on cloud body/crowns, gently softened only at the very bottom LCL base (<24%) for smooth crossing
        float coreWeight=smoothstep(.20,.76,coverage);
        float crownSharpen=coreWeight*smoothstep(.34,.80,localHeight);
        float billowWidth=lerp(.31,lerp(.21,.14,crownSharpen),smoothstep(.04,.26,localHeight));
        float billowy=smoothstep(threshold-.02,threshold+billowWidth,eroded);
        float wispyReach=lerp(.055,.032,sc);
        float wispyHeightMask=lerp(pow(saturate(1.0-localHeight*.56),1.25),.82,anvilFlare);
        float gauzeVeil=smoothstep(threshold-wispyReach,threshold+.22,erodedWispy)*wispyHeightMask;
        float cellLace=smoothstep(.12,.78,cell);
        float clip=lerp(1,lerp(lerp(.24,.80,scDense),1.0,pow(max(cellLace,.001),1.04)),sc);

        float veilTerm=gauzeVeil*lerp(.14,.28,anvilFlare)*(1.0-.32*sc);
        float coreTerm=billowy*lerp(.78,1.0,coreWeight);
        float vaporBody=lerp(veilTerm,coreTerm,smoothstep(0.0,.38,billowy));
        float domeRelief=lerp(.90,1.09,mesoDome);
        float denseTopMask=smoothstep(.36,.82,coverage)*smoothstep(.26,.72,type);
        float topFeather=1.0-smoothstep(.74,.995,localHeight)*denseTopMask*.58;
        density=vaporBody*domeRelief*topFeather*clip*coverage*lerp(.96,1.38,type);
    }

    // 2. Detached Mid-Tropospheric Altocumulus / Altostratus (Ac/As) Deck (3,850 - 6,050 m)
    float midGate=smoothstep(.30,.44,type)*(1.0-smoothstep(.56,.74,type));
    if(midGate>.01&&metres>3700.0&&metres<6250.0)
    {
        float midH=(metres-3850.0)/2200.0;
        float midBand=smoothstep(0,.30,midH)*(1.0-smoothstep(.66,1.0,midH));
        float midCell=saturate(cell*.72+(1.0-invWorley)*.28);
        float midCov=coverage*midGate*(.54+.46*midCell);
        float midSilhouette=1.0-smoothstep(.30,.66,midCell*midBand);
        float midEroded=saturate((pow(midCell,.74)-curlyWisp*.18*midSilhouette)/.82)*midBand;
        float midSheet=smoothstep(1.0-midCov-.12,1.0-midCov+.26,midEroded)*midCov*.78;
        density=max(density,midSheet);
    }
    return density;
}
float OrbisHPLowDensity(float metres,float coverage,float type,float shape,float erosion)
{return OrbisHPLowDensityDetailed(metres,coverage,type,shape,(1-erosion).xxxx,1);}
float OrbisHPHighDensity(float metres,float coverage,float type,float cell,float wisp)
{
    // High-tropospheric Cirrus / Cirrostratus / Typhoon Outflow Canopy (7,500 - 13,400 m):
    // Broad +/-750 m vertical feather prevents thin-sheet contour slicing in oblique views.
    if(coverage<.001)return 0;
    float coverHeight=pow(saturate(coverage),.65);
    float cellStrength=lerp(.20,.72,type),shaped=pow(max(cell,.001),1.25);
    float bottom=8350-1150*coverHeight;
    float top=9200+4050*coverHeight*lerp(1,shaped,cellStrength*.40);
    float band=smoothstep(bottom-750,bottom+750,metres)*(1-smoothstep(top-750,top+750,metres));
    float baseDensity=saturate((coverage-.020)/.32);
    return max(0,baseDensity*lerp(1,shaped,cellStrength)-wisp*wisp*.16*type)*band*coverage*.62;
}

float OrbisHPBoundaryConfidence(float density)
{
    return smoothstep(.014,.14,density);
}
#endif
