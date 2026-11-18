#ifndef ORBIS_FLAT_RIVER_OPTICS
#define ORBIS_FLAT_RIVER_OPTICS

struct FlatRiverSection
{
    float edge,width,depth;
    float2 flow,lateralGradient;
    float across,bend,grade; // Signed fraction of half-width; turns to the left are positive.
    float influence;
};

FlatRiverSection FlatRiverSectionAt(float2 cell,float4 channel,float4 field)
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
    float turn=dot(ddx(tangent)*along.x+ddy(tangent)*along.y,left);
    [branch] if(FlatRiverFieldReady()&&resolved>.03&&r.width>.0001&&r.edge<.21&&field.g<.48)
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
        turn=lerp(turn,measured,smoothstep(.05,.84,sum));
    }
    r.bend=clamp(turn*.82,-1,1)*resolved;
    float drop=-dot(float2(ddx(field.r),ddy(field.r)),along);
    r.grade=smoothstep(8,115,drop*(_MapSize.x/512));
    float bank=1-smoothstep(.001,.045+r.width*.85,r.edge);
    // The basin field is compressed around small lakes. Measure the blend
    // across the shoreline in ground units, in proportion to this reach.
    float2 seaward;
    float shore=FlatMetricShore(cell,field.g,seaward);
    float mouthSpan=clamp(.12+1.35*r.width,.14,.42);
    r.influence=step(.0001,r.width)*bank*(1-smoothstep(-mouthSpan,.65*mouthSpan,shore));
    return r;
}

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
    float4 uv; // Two downstream projections share the same cross-channel coordinate.
    float weight;
};

FlatRiverWaveUV FlatRiverWaveCoordinates(FlatRiverSection r,float2 cell)
{
    FlatRiverWaveUV waves;
    float across=r.across*r.width*3;
    float frequency=max(1,round(_MapSize.x*.58))/max(1,_MapSize.x);
    waves.uv=float4(across,cell.x*frequency*sign(r.flow.x),across+.37,cell.y*.58*sign(r.flow.y)+.41);
    float2 weight=r.flow*r.flow;weight*=weight;
    waves.weight=weight.x/max(.0001,weight.x+weight.y);
    return waves;
}

float2 FlatRiverSW3Normal(sampler2D artwork,FlatRiverWaveUV waves,float scale,float phase,float lod)
{
    float4 uv=waves.uv*scale-float4(0,phase,0,phase);
    float2 a=UnpackNormal(tex2Dlod(artwork,float4(uv.xy,0,lod))).xy;
    float2 b=UnpackNormal(tex2Dlod(artwork,float4(uv.zw,0,lod))).xy;
    return lerp(b,a,waves.weight);
}

float FlatRiverSW3Foam(sampler2D artwork,FlatRiverWaveUV waves,float phase,float lod)
{
    float4 uv=waves.uv-float4(0,phase,0,phase);
    return lerp(tex2Dlod(artwork,float4(uv.zw,0,lod)).r,tex2Dlod(artwork,float4(uv.xy,0,lod)).r,waves.weight);
}

float3 FlatRiverSubstrate(float3 localBed,float3 position,FlatRiverSection r,float across,float sediment,float footprint)
{
    float bar=FlatRiverPointBar(r,across);
    float grain=FlatDetailNoise(position*19+7)-.5;
    float patch=FlatDetailNoise(position*2.7+13);
    float grainVisibility=1-smoothstep(.025,.11,footprint);
    // Fine deposits collect on the inside of a bend. Sorted bands follow the
    // actual bank, including where its reach crosses a dry simulation tile.
    float sorted=sin(across*15+(patch-.5)*5);
    float3 sand=lerp(float3(.76,.71,.57),float3(.59,.54,.43),saturate(sediment*1.5));
    sand*=1+(grain*.23+sorted*.045)*grainVisibility+(patch-.5)*.10;
    float3 gravel=FlatColor(float3(.54,.54,.47)*(1+grain*.32*grainVisibility));
    return lerp(lerp(localBed,gravel,r.grade*.46),FlatColor(sand),bar*.86);
}

float3 FlatRiverBank(float3 land,float3 bed,float3 relief,float3 position,FlatRiverSection r,
                     float sediment,float footprint,float dry,float aa,float basinShore)
{
    float bar=FlatRiverPointBar(r,r.across);
    float variation=FlatDetailNoise(position*3.1+21);
    float width=clamp(r.width*lerp(.26,1.34,bar),.009,.19)*lerp(.66,1.25,variation);
    float coverage=(1-smoothstep(0,width+aa,r.edge))*dry*(1-smoothstep(-.15,.12,basinShore));
    float3 material=FlatRiverSubstrate(bed,position,r,r.across,sediment,footprint);
    float damp=1-smoothstep(0,width*.76+aa*.25,r.edge);
    return lerp(land,material*relief*(1-.14*damp),coverage*lerp(.46,.91,bar));
}
#endif
