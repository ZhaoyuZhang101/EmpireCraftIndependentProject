#ifndef ORBIS_FLAT_RIVER_OPTICS
#define ORBIS_FLAT_RIVER_OPTICS

struct FlatRiverSection
{
    float edge,width,depth;
    float2 flow,lateralGradient;
    float across,bend,grade; // Signed fraction of half-width; turns to the left are positive.
    float influence;
};

FlatRiverSection FlatRiverSectionAt(float2 cell,float4 channel,float4 field,float2 bankProfile)
{
    FlatRiverSection r;
    r.edge=channel.r;r.width=length(channel.ba);r.depth=channel.g;
    r.flow=channel.ba/max(.0001,r.width);
    float longitudeScale=lerp(1,max(.08,cos((cell.y*_MapSize.w-.5)*3.14159265)),saturate(FlatRiverFieldReady()));
    float2 metric=float2(longitudeScale,1);
    float2 tangent=r.flow*metric;tangent/=max(.0001,length(tangent));
    float2 left=float2(-tangent.y,tangent.x);
    r.lateralGradient=left*metric;
    // Project screen derivatives into the original curve's tile coordinates.
    // This works through chart rotation/morph and the geographic longitude seam.
    float2 dx=ddx(cell),dy=ddy(cell);
    dx.x-=round(dx.x/max(1,_MapSize.x))*_MapSize.x;
    dy.x-=round(dy.x/max(1,_MapSize.x))*_MapSize.x;
    float det=dx.x*dy.y-dx.y*dy.x;
    float inverse=(det<0?-1:1)/max(1e-12,abs(det));
    float2 mapTangent=tangent/metric,mapLeft=left/metric;
    float2 along=float2(mapTangent.x*dy.y-mapTangent.y*dy.x,dx.x*mapTangent.y-dx.y*mapTangent.x)*inverse;
    float2 crosswise=float2(mapLeft.x*dy.y-mapLeft.y*dy.x,dx.x*mapLeft.y-dx.y*mapLeft.x)*inverse;
    float centreDistance=max(0,r.edge+r.width);
    float side=dot(float2(ddx(centreDistance),ddy(centreDistance)),crosswise);
    float footprint=max(length(dx*metric),length(dy*metric));
    float resolved=smoothstep(.45,1.8,r.width/max(.0001,footprint));
    r.across=clamp(side,-1,1)*min(2.6,centreDistance/max(.0001,r.width));
    bool analytic=FlatRiverFieldReady()&&FLAT_VECTOR_FIELD;
    if(analytic)r.across=clamp(bankProfile.x/max(.0001,r.width),-2.6,2.6);
    float turn=dot(ddx(tangent)*along.x+ddy(tangent)*along.y,left);
    [branch] if(!FLAT_VECTOR_FIELD&&FlatRiverFieldReady()&&resolved>.03&&r.width>.0001&&r.edge<.21&&field.g<.48)
    {
        // A bend sorts sediment over a length of channel. The derivative of a
        // bilinear atlas tangent changes abruptly at an individual grid line.
        float reach=.25+.80*r.width;
        float2 centre=cell-mapLeft*r.across*r.width;
        float4 upstream=FlatRiverBaseSample(centre-mapTangent*reach);
        float4 downstream=FlatRiverBaseSample(centre+mapTangent*reach);
        float2 a=upstream.ba*metric,b=downstream.ba*metric;
        a/=max(.0001,length(a));b/=max(.0001,length(b));
        // At confluences give downstream-aligned samples of the same channel
        // priority. Unsupported ends fade back to the local estimate.
        float2 support=smoothstep(.12,.72,float2(dot(a,tangent),dot(b,tangent)))*
                       (1-smoothstep(.04,.23,float2(upstream.r,downstream.r)));
        float sum=support.x+support.y;
        float measured=(dot(tangent-a,left)*support.x+dot(b-tangent,left)*support.y)/(reach*max(.001,sum));
        float confidence=smoothstep(.05,.84,sum);
        // Survey vertices are discrete. A screen derivative at a vertex or
        // confluence would lay a stripe of cut-bank material across the river.
        turn=lerp(turn,measured,confidence);
    }
    r.bend=(analytic?bankProfile.y:clamp(turn*.82,-1,1))*resolved;
    float drop=-dot(float2(ddx(field.r),ddy(field.r)),along);
    r.grade=smoothstep(8,115,drop*(_MapSize.x/512));
    float bank=1-smoothstep(.001,.045+r.width*.85,r.edge);
    // The basin field is compressed around small lakes. Measure the blend
    // across the shoreline in ground units, in proportion to this reach.
    float2 seaward;
    float shore=FlatMetricShore(cell,field.g,seaward);
    float mouthSpan=clamp(.12+1.35*r.width,.14,.42);
    // An outgoing current carries river waves beyond the coast. Along a beach
    // or at an inland outlet the transition stays close to the channel.
    float outgoing=smoothstep(.05,.75,dot(tangent,seaward));
    float offshore=lerp(.65*mouthSpan,min(1.2,max(1.45*mouthSpan,4*r.width)),outgoing);
    float upstream=-mouthSpan*(1-.5*outgoing);
    r.influence=step(.0001,r.width)*bank*(1-smoothstep(upstream,offshore,shore));
    return r;
}
FlatRiverSection FlatRiverSectionAt(float2 cell,float4 channel,float4 field)
{return FlatRiverSectionAt(cell,channel,field,0);}

float FlatRiverDepthProfile(FlatRiverSection r)
{
    float section=saturate(-r.edge/max(.0001,r.width));
    // At the cut bank the deepest thread sits off centre. The long gravel
    // slope on the inside of the same turn rises into the point bar.
    return section*(2-section)*(1-.87*r.bend*clamp(r.across,-1,1));
}

float FlatRiverPointBar(FlatRiverSection r,float across)
{
    return smoothstep(.06,.82,r.bend*clamp(across,-1,1))*(1-.72*r.grade);
}

float FlatRiverRiffle(FlatRiverSection r,float3 position)
{
    // Alternating reaches remain fixed to the bed while the surface advects.
    // Crossovers and falling reaches break the surface; the outside pool stays quiet.
    float shoal=.5+.5*sin(dot(position,float3(1.7,2.2,-1.1))+1.25*sin(dot(position,float3(-.87,1.27,1.41))));
    float shallow=1-smoothstep(.45,6.5,r.depth*FlatRiverDepthProfile(r));
    float crossing=1-saturate(abs(r.bend)*.75);
    return r.grade*lerp(.10,1,smoothstep(.13,.85,shoal))*(.23+.77*shallow)*crossing;
}

struct FlatRiverWaveUV
{
    float3 position,drift,weights;
    float footprint,resolved;
};

FlatRiverWaveUV FlatRiverWaveCoordinates(FlatRiverSection r,float3 artwork,float3 drift,float3 weights,float groundPixel)
{
    FlatRiverWaveUV waves;
    // The SW3 image contains several large ripples per repeat. A 7 km
    // repeat on Earth resolves those bands at local-map scale. Keep all
    // phases anchored on the sphere; only the short advection vector turns.
    float frequency=lerp(.85,40030/(7*max(1,_MapSize.x)),saturate(_RiverSurvey));
    waves.position=artwork*frequency;waves.drift=drift;waves.weights=weights;
    waves.footprint=max(.000001,groundPixel*frequency);
    waves.resolved=smoothstep(.60,2.3,r.width/max(.00001,groundPixel));
    return waves;
}

float2 FlatRiverSW3Normal(sampler2D artwork,FlatRiverWaveUV waves,float phase,float lod,float3 right,float3 up)
{
    return FlatDetailNormal(artwork,waves.position-waves.drift*phase,waves.weights,0,lod,right,up);
}

float FlatRiverSW3Foam(sampler2D artwork,FlatRiverWaveUV waves,float phase,float lod)
{
    return FlatDetailTexture(artwork,waves.position-waves.drift*phase,waves.weights,0,lod);
}

float3 FlatRiverSubstrate(float3 localBed,float3 position,FlatRiverSection r,float across,float sediment,float footprint)
{
    float bar=FlatRiverPointBar(r,across);
    float grain=FlatDetailNoise(position*19+7)-.5;
    float patch=FlatDetailNoise(position*2.7+13);
    float grainVisibility=1-smoothstep(.025,.11,footprint);
    // Fine deposits collect on the inside of a bend. Sorted bands follow the
    // actual bank, including where its reach crosses a dry simulation tile.
    float3 sand=lerp(float3(.76,.71,.57),float3(.59,.54,.43),saturate(sediment*1.5));
    sand*=1+grain*.23*grainVisibility+(patch-.5)*.10;
    float3 gravel=FlatColor(float3(.54,.54,.47)*(1+grain*.32*grainVisibility));
    return lerp(lerp(localBed,gravel,r.grade*.46),FlatColor(sand),bar*.86);
}

float3 FlatRiverBank(float3 land,float3 bed,float3 relief,float3 position,FlatRiverSection r,
                     float sediment,float footprint,float dry,float aa,float basinShore)
{
    float bar=FlatRiverPointBar(r,r.across);
    float variation=FlatDetailNoise(position*3.1+21);
    float viewScale=1-_RiverGlobeNarrowing;
    float width=min(.19*viewScale,r.width*lerp(.21,.87,bar))*lerp(.82,1.15,variation);
    aa*=viewScale;
    float coverage=(1-smoothstep(0,width+aa,r.edge))*dry*(1-smoothstep(-.15,.12,basinShore));
    float3 material=FlatRiverSubstrate(bed,position,r,r.across,sediment,footprint);
    float damp=1-smoothstep(0,width*.76+aa*.25,r.edge);
    return lerp(land,material*relief*(1-.14*damp),coverage*lerp(.36,.75,bar));
}
#endif
