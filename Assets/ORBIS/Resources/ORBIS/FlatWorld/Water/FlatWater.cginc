#ifndef ORBIS_FLAT_WATER_INCLUDED
#define ORBIS_FLAT_WATER_INCLUDED
#include "HPWaterOptics.cginc"
#include "../FlatSurfaceField.cginc"
#include "FlatRiverAtlas.cginc"
#include "../FlatPolarIce.cginc"
sampler2D _HydroA,_HydroB,_GeoTex;
sampler2D _VendorStreamNormal,_VendorOceanNormal;float _VendorWaterReady;
sampler2D _VendorLakeNormal,_VendorLakeSmoothNormal,_VendorOceanFoam,_VendorRiverFoam;
float4 _VendorStreamNormal_TexelSize,_VendorOceanNormal_TexelSize,_VendorLakeNormal_TexelSize,_VendorRiverFoam_TexelSize;
float4 _VendorLakeSmoothNormal_TexelSize;
UNITY_DECLARE_TEX2D_NOSAMPLER(_VendorCaustics);
float _VendorCausticsReady;
#ifndef ORBIS_FLAT_NO_SWE
// Both simulation patches have the same bilinear/clamp sampling state.
UNITY_DECLARE_TEX2D(_SWEState);
UNITY_DECLARE_TEX2D_NOSAMPLER(_SWEPreviousState);
#endif
float4 _SWERect,_SWEPreviousRect,_SWEParams;
float _SWEPreviousReady,_SWEBlend;
float _SWEReady,_WaterClock,_ShowBanks,_ShowWater,_WaveAmount;
float _PixelsPerTile;
#include "FlatCoastalSediment.cginc"
#include "FlatRiverOptics.cginc"
float FlatLandSnow(float2 cell,sampler2D semantic)
{
    float2 p=cell-.5,b=floor(p),t=frac(p);t=t*t*(3-2*t);
    float4 weights=float4((1-t.x)*(1-t.y),t.x*(1-t.y),(1-t.x)*t.y,t.x*t.y);
    float snow=0,land=0;
    [unroll] for(int k=0;k<4;k++)
    {
        float2 uv=(b+float2(k==1||k==3?1.5:.5,k>=2?1.5:.5))*_MapSize.zw;
        float4 info=floor(tex2Dlod(semantic,float4(uv,0,0))*255+.5);
        float dry=1-step(.5,info.g)*step(info.g,4.5);
        float material=tex2Dlod(_GeoTex,float4(uv,0,0)).g;
        float weight=weights[k]*dry;
        snow+=max(step(4.5,material),step(29.5,info.a))*weight;land+=weight;
    }
    return snow/max(1e-6,land);
}
float FlatAreaWaterKind(float2 cell,sampler2D semantic,float originalKind)
{
    // Extend only wet classifications through the reconstructed coastline. A land
    // texel is not an ocean texel; averaging categorical IDs would invent water types.
    float2 p=cell-.5,b=floor(p),t=frac(p);t=t*t*(3-2*t);
    float4 kinds=float4(tex2Dlod(semantic,float4((b+.5)*_MapSize.zw,0,0)).g,
        tex2Dlod(semantic,float4((b+float2(1.5,.5))*_MapSize.zw,0,0)).g,
        tex2Dlod(semantic,float4((b+float2(.5,1.5))*_MapSize.zw,0,0)).g,
        tex2Dlod(semantic,float4((b+1.5)*_MapSize.zw,0,0)).g)*255;
    kinds=floor(kinds+.5);
    float4 weights=float4((1-t.x)*(1-t.y),t.x*(1-t.y),(1-t.x)*t.y,t.x*t.y);
    weights*=step(.5,kinds)*step(kinds,4.5);
    float wet=dot(weights,float4(1,1,1,1));
    float lake=dot(weights,1-step(.5,abs(kinds-4)))/max(1e-6,wet);
    return wet>1e-6?3+lake:originalKind;
}
float FlatSurveyBasinShore(float2 uv,float2 cell,sampler2D semantic,float kind,inout float shore,inout float2 coast)
{
    float original=shore;
    float strait=FlatApplyEarthShore(uv,kind,shore,coast);
    // Small simulated lakes can reach across their last semantic tile onto a
    // dry receiver. Preserve that bank where the marine survey finds dry land.
    if(strait>0 && shore<0 && original>max(shore,-.28) && (kind<.5||kind>4.5))
    {
        float neighbour=FlatAreaWaterKind(cell,semantic,kind);
        if(abs(neighbour-4)<.5){shore=original;coast.y=0;return 0;}
    }
    return strait;
}
float _KoppenMap;
float4 _OrbisKoppenColours[32]; // 31 simulated classes, then the climate map ocean colour.
float3 FlatKoppenCoast(float3 display,float4 semantic,float shore,float aa,float coverage)
{
    if(coverage<=0)return display;
    bool wasOcean=semantic.g>.5 && semantic.g<3.5;
    float3 land=wasOcean ? FlatColor(_OrbisKoppenColours[(int)clamp(round(semantic.a),0,30)].rgb) : display;
    float3 sea=wasOcean ? display : FlatColor(_OrbisKoppenColours[31].rgb);
    return lerp(display,lerp(land,sea,smoothstep(-aa,aa,shore)),coverage);
}

float3 FlatRiverMapColor(float2 cell,sampler2D semantic,float kind,float4 field,float aa,float ink,float surveyLake)
{
    float sea=smoothstep(-aa,aa,field.g);
    float lake=max(surveyLake,saturate(FlatAreaWaterKind(cell,semantic,kind)-3));
    float3 land=lerp(FlatColor(float3(.91,.90,.83)),FlatColor(float3(.75,.72,.62)),saturate(field.r/4000));
    float3 basin=lerp(FlatColor(float3(.63,.72,.77)),FlatColor(float3(.51,.73,.79)),lake);
    // Charted tidal channels continue over the estuary polygon to their
    // surveyed mouth. Open lake interiors retain their separate lake symbol.
    float channelInk=ink*(1-sea*lerp(1,lake,_RiverSurvey));
    return lerp(lerp(land,basin,sea),FlatColor(float3(.055,.31,.49)),channelInk);
}
float4 FlatAreaWaterOptics(float2 cell,sampler2D semantic,float4 fallback,out float wetSupport)
{
    float2 p=cell-.5,b=floor(p),t=frac(p);t=t*t*(3-2*t);
    float4 weights=float4((1-t.x)*(1-t.y),t.x*(1-t.y),(1-t.x)*t.y,t.x*t.y);
    float4 optics=0;float wet=0;
    [unroll] for(int k=0;k<4;k++)
    {
        float2 uv=(b+float2(k==1||k==3?1.5:.5,k>=2?1.5:.5))*_MapSize.zw;
        float kind=floor(tex2Dlod(semantic,float4(uv,0,0)).g*255+.5);
        float weight=weights[k]*step(.5,kind)*step(kind,4.5);
        float depth=tex2Dlod(_HydroA,float4(uv,0,0)).r;
        float4 mixing=tex2Dlod(_HydroB,float4(uv,0,0));
        optics+=float4(max(.15,depth),saturate(mixing.g),saturate(mixing.b),step(3.5,kind))*weight;
        wet+=weight;
    }
    // A reconstructed shoreline extends wet metadata across its dry semantic
    // cells. Normalization keeps land zeros and river depths out of basin optics.
    wetSupport=wet;
    if(wet<=1e-6)return fallback;
    optics/=wet;optics.a+=3;
    return optics;
}
float4 FlatAreaWaterOptics(float2 cell,sampler2D semantic,float4 fallback)
{
    float wetSupport;
    return FlatAreaWaterOptics(cell,semantic,fallback,wetSupport);
}
float2 FlatEdge(int direction)
{
    return direction==0?float2(0,.5):direction==1?float2(1,.5):direction==2?float2(.5,0):float2(.5,1);
}
float2 FlatRiverEdge(int direction,float meander,float neighborMeander,float maxWidth)
{
    // Reserve the radius, the bounded interior S-bend and a filtered bank margin.
    float limit=max(0,min(.24,.5-.055-maxWidth-max(abs(meander),abs(neighborMeander))*.60));
    float cross=.5+clamp((meander+neighborMeander)*4,-limit,limit);
    float2 edge=FlatEdge(direction);
    if(direction<2)edge.y=cross;else edge.x=cross;
    return edge;
}
float FlatBezierDistance(float2 p,float2 a,float2 b,float2 c,float ra,float rb,float rc,float bend,out float2 tangent)
{
    float result=8;float2 previous=a;tangent=normalize(b-a+float2(1e-7,0));float previousRadius=ra;
    float2 perpendicular=float2(-(b-a).y,(b-a).x)*rsqrt(max(dot(b-a,b-a),1e-10));
    // Small fixed loop with NO texture sampling. Do not unroll a ray marcher here.
    [loop] [fastopt] for(int k=1;k<=8;k++)
    {
        float t=k*.125,s=1-t;
        float2 q=s*s*a+2*s*t*c+t*t*b;
        q+=perpendicular*(bend*32*t*t*s*s*(2*t-1));
        float radius=s*s*ra+2*s*t*rc+t*t*rb;
        float2 delta=q-previous;float den=dot(delta,delta);
        float along=saturate(dot(p-previous,delta)/max(den,1e-10));
        float candidate=length(p-previous-along*delta)-lerp(previousRadius,radius,along);
        if(candidate<result){result=candidate;tangent=delta*rsqrt(max(den,1e-10));}
        previous=q;previousRadius=radius;
    }
    return result;
}
float FlatLegacyFaceDistance(float2 p,float4 hydro,float connections,float meander,float2 uv,out float2 tangent)
{
    tangent=hydro.gb;
    float result=8;
    if(hydro.a>=.01)
    {
        float4 links=fmod(floor(connections/float4(1,2,4,8)),2);
        float count=dot(links,float4(1,1,1,1));
        if(count<.5)result=length(p-.5)-hydro.a;
        else
        {
            int outlet=0;float best=-2;
            if(links.x>.5){best=-hydro.g;outlet=0;}
            if(links.y>.5&&hydro.g>best){best=hydro.g;outlet=1;}
            if(links.z>.5&&-hydro.b>best){best=-hydro.b;outlet=2;}
            if(links.w>.5&&hydro.b>best){best=hydro.b;outlet=3;}
            float4 neighborWidths=float4(tex2Dlod(_HydroA,float4(uv-float2(_MapSize.z,0),0,0)).a,
                tex2Dlod(_HydroA,float4(uv+float2(_MapSize.z,0),0,0)).a,
                tex2Dlod(_HydroA,float4(uv-float2(0,_MapSize.w),0,0)).a,
                tex2Dlod(_HydroA,float4(uv+float2(0,_MapSize.w),0,0)).a);
            float4 radii=lerp(hydro.aaaa,(neighborWidths+hydro.a)*.5,step(.01,neighborWidths));
            float4 neighborMeanders=float4(tex2Dlod(_HydroB,float4(uv-float2(_MapSize.z,0),0,0)).r,
                tex2Dlod(_HydroB,float4(uv+float2(_MapSize.z,0),0,0)).r,
                tex2Dlod(_HydroB,float4(uv-float2(0,_MapSize.w),0,0)).r,
                tex2Dlod(_HydroB,float4(uv+float2(0,_MapSize.w),0,0)).r);
            float2 b=FlatRiverEdge(outlet,meander,neighborMeanders[outlet],max(hydro.a,neighborWidths[outlet]));float rb=radii[outlet];
            float bend=meander;
            float2 bestTangent=tangent;
            float sourceRadius=max(.003,hydro.a*.18);
            bool singleLink=count<1.5;
            [loop] [fastopt] for(int d=0;d<4;d++)
            {
                if(singleLink?(d!=outlet):(d==outlet||links[d]<.5))continue;
                float2 direction=float2(1,0);
                float2 a=singleLink?float2(.5,.5):FlatRiverEdge(d,meander,neighborMeanders[d],max(hydro.a,neighborWidths[d]));
                bool straight=!singleLink&&((d<2&&outlet<2)||(d>=2&&outlet>=2));
                float2 mid=singleLink?((float2(.5,.5)+b)*.5):(straight?(a+b)*.5:float2(.5,.5));
                float ra=singleLink?sourceRadius:radii[d];
                float rc=singleLink?((sourceRadius+rb)*.5):hydro.a;
                float candidate=FlatBezierDistance(p,a,b,mid,ra,rb,rc,bend*(straight?1:.35),direction);
                if(candidate<result){result=candidate;bestTangent=direction;}
            }
            float closest=.10,section=0;float2 faceDirection=bestTangent;
            [loop] [fastopt] for(int faceIndex=0;faceIndex<4;faceIndex++)
            {
                if(links[faceIndex]<.5)continue;
                float face=faceIndex==0?p.x:faceIndex==1?1-p.x:faceIndex==2?p.y:1-p.y;
                float2 edge=FlatRiverEdge(faceIndex,meander,neighborMeanders[faceIndex],max(hydro.a,neighborWidths[faceIndex]));
                float across=abs((faceIndex<2?p.y:p.x)-(faceIndex<2?edge.y:edge.x));
                if(face<closest)
                {
                    closest=face;section=across-radii[faceIndex];
                    faceDirection=(FlatEdge(faceIndex)-.5)*2*(faceIndex==outlet?1:-1);
                }
            }
            if(closest<.10)
            {
                float weight=smoothstep(0,.10,closest);result=lerp(section,result,weight);
                bestTangent=normalize(lerp(faceDirection,bestTangent,weight)+float2(1e-7,0));
            }
            tangent=bestTangent;
        }
    }
    return result;
}
float FlatLegacyLink(float connections,int d)
{
    return fmod(floor(connections/exp2((float)d)),2);
}
int FlatDiagonalDirection(float2 offset)
{
    return 4+(offset.x>0?1:0)+(offset.y>0?2:0);
}
float4 FlatLegacySegment(float2 p,float2 a,float2 b,float2 widths,float2 depths,float signFlow)
{
    float2 delta=b-a;float lengthSquared=max(.000001,dot(delta,delta));
    float t=saturate(dot(p-a,delta)/lengthSquared),width=lerp(widths.x,widths.y,t);
    return float4(length(p-a-t*delta)-width,lerp(depths.x,depths.y,t),
                  delta*(rsqrt(lengthSquared)*width*signFlow));
}
// All four tiles at a diagonal basin crossing sample the same reach. The river's
// recorded downstream neighbour disambiguates an inlet from a lake spill.
float4 FlatLegacyBasinDiagonal(float2 p,float2 riverCentre,float2 toBasin,float4 hydro)
{
    float2 difference=toBasin-hydro.gb;
    return FlatLegacySegment(p,riverCentre,riverCentre+toBasin,hydro.aa,hydro.rr,
                             dot(difference,difference)<.1?1:-1);
}
// Only used until a curve atlas is ready. Cardinal-only channels retain their
// individual meanders. Mixed junctions join their full D8 branches at the centre.
float4 FlatLegacyRiverSample(float2 p,float4 hydro,float connections,float meander,float2 uv)
{
    float4 result=float4(8,hydro.r,0,0);
    [branch] if(hydro.a>=.01&&connections>=16)
    {
        [loop] [fastopt] for(int d=0;d<8;d++)
        {
            if(FlatLegacyLink(connections,d)<.5)continue;
            float2 offset=d<4?(FlatEdge(d)-.5)*2:float2(fmod((float)d,2)*2-1,d<6?-1:1);
            float2 neighborUV=uv+offset*_MapSize.zw;
            float4 neighbor=tex2Dlod(_HydroA,float4(neighborUV,0,0));
            if(d>=4&&neighbor.a<.01&&FlatVisualSample(float4(neighborUV,0,0)).g>0)
            {
                float4 mouth=FlatLegacyBasinDiagonal(p,float2(.5,.5),offset,hydro);
                if(mouth.r<result.r)result=mouth;
                continue;
            }
            float2 edge=.5+offset*.5;
            if(d<4)edge=FlatRiverEdge(d,meander,tex2Dlod(_HydroB,float4(neighborUV,0,0)).r,max(hydro.a,neighbor.a));
            float rb=neighbor.a>=.01?(hydro.a+neighbor.a)*.5:hydro.a;
            float depth=neighbor.a>=.01?(hydro.r+neighbor.r)*.5:hydro.r;
            float2 difference=offset-hydro.gb;
            float4 reach=FlatLegacySegment(p,float2(.5,.5),edge,float2(hydro.a,rb),float2(hydro.r,depth),dot(difference,difference)<.1?1:-1);
            if(d<4)
            {
                float face=d==0?p.x:d==1?1-p.x:d==2?p.y:1-p.y;
                float cross=abs(d<2?p.y-edge.y:p.x-edge.x);
                reach.r=lerp(cross-rb,reach.r,smoothstep(0,.1,face));
            }
            if(reach.r<result.r)result=reach;
        }
    }
    else if(hydro.a>=.01)
    {
        float2 tangent;
        result.r=FlatLegacyFaceDistance(p,hydro,connections,meander,uv,tangent);
        result.ba=(dot(tangent,tangent)<1e-8?float2(1,0):tangent)*hydro.a;
    }
    float2 corner=step(.5,p),side=corner*2-1;
    if(hydro.a<.01&&FlatLegacyLink(connections,FlatDiagonalDirection(side))<.5)return result;
    if(dot(abs(p-corner),float2(1,1))>.68)return result;
    // The two cells beside a diagonal share its wet corner. Its bit is
    // published on each dry side tile so other terrain incurs no neighbor reads.
    float2 auv=uv+float2(side.x*_MapSize.z,0),buv=uv+float2(0,side.y*_MapSize.w);
    if(buv.y<0||buv.y>1)return result;
    float2 direction=float2(-side.x,side.y);
    float4 a=tex2Dlod(_HydroA,float4(auv,0,0)),b=tex2Dlod(_HydroA,float4(buv,0,0));
    if(max(a.a,b.a)<.01)return result;
    bool aRiver=a.a>=.01;
    float links=floor(tex2Dlod(_HydroB,float4(aRiver?auv:buv,0,0)).a+.5);
    if(FlatLegacyLink(links,FlatDiagonalDirection(aRiver?direction:-direction))<.5)return result;
    if(min(a.a,b.a)<.01)
    {
        if(FlatVisualSample(float4(aRiver?buv:auv,0,0)).g<=0)return result;
        float2 origin=.5+(aRiver?float2(side.x,0):float2(0,side.y));
        float4 mouth=FlatLegacyBasinDiagonal(p,origin,aRiver?direction:-direction,aRiver?a:b);
        return mouth.r<result.r?mouth:result;
    }
    float2 difference=direction-a.gb;
    float4 bank=FlatLegacySegment(p,.5+float2(side.x,0),.5+float2(0,side.y),
        float2(a.a,b.a),float2(a.r,b.r),dot(difference,difference)<.1?1:-1);
    return bank.r<result.r?bank:result;
}
float FlatLegacyRiverDistance(float2 p,float4 hydro,float connections,float meander,float2 uv,out float2 tangent)
{
    float4 river=FlatLegacyRiverSample(p,hydro,connections,meander,uv);
    tangent=river.ba/max(.000001,length(river.ba));
    return river.r;
}
float3 FlatMaterialColor(float code)
{
    // Material IDs MUST be sampled at the authoritative texel centre.
    if(code<.5)return FlatColor(float3(.61,.50,.36));
    if(code<1.5)return FlatColor(float3(.83,.74,.54));
    if(code<2.5)return FlatColor(float3(.55,.47,.34));
    if(code<3.5)return FlatColor(float3(.62,.63,.57));
    if(code<4.5)return FlatColor(float3(.54,.55,.54));
    return FlatColor(float3(.87,.91,.88));
}
float3 FlatBedColor(float2 cell,float areaWater)
{
    float2 base=floor(cell-.5),t=frac(cell-.5);t=t*t*(3-2*t);
    float2 uv=(base+.5)*_MapSize.zw,dx=float2(_MapSize.z,0),dy=float2(0,_MapSize.w);
    // Blend sampled colours. Interpolating material IDs would invent substrates.
    float3 a=FlatMaterialColor(floor(tex2Dlod(_GeoTex,float4(uv,0,0)).g+.5));
    float3 b=FlatMaterialColor(floor(tex2Dlod(_GeoTex,float4(uv+dx,0,0)).g+.5));
    float3 c=FlatMaterialColor(floor(tex2Dlod(_GeoTex,float4(uv+dy,0,0)).g+.5));
    float3 d=FlatMaterialColor(floor(tex2Dlod(_GeoTex,float4(uv+dx+dy,0,0)).g+.5));
    float4 weights=float4((1-t.x)*(1-t.y),t.x*(1-t.y),(1-t.x)*t.y,t.x*t.y);
    if(areaWater>.001)
    {
        float4 h0=tex2Dlod(_HydroA,float4(uv,0,0)),h1=tex2Dlod(_HydroA,float4(uv+dx,0,0));
        float4 h2=tex2Dlod(_HydroA,float4(uv+dy,0,0)),h3=tex2Dlod(_HydroA,float4(uv+dx+dy,0,0));
        float4 wet=step(.001,float4(h0.r,h1.r,h2.r,h3.r))*(1-step(.01,float4(h0.a,h1.a,h2.a,h3.a)));
        float4 wetWeights=weights*wet;
        float coverage=dot(wetWeights,float4(1,1,1,1));
        // Legacy probes without basin metadata retain their smooth colour blend.
        if(coverage>1e-6)weights=lerp(weights,wetWeights/coverage,areaWater);
    }
    return a*weights.x+b*weights.y+c*weights.z+d*weights.w;
}
float3 FlatBedColor(float2 cell)
{
    return FlatBedColor(cell,0);
}
float2 FlatBedRefraction(float2 cell,float3 view,float3 normal,float depth,float boundary)
{
    const float airToWater=1/1.333;
    float3 flatRay=refract(-view,float3(0,0,1),airToWater);
    float3 bentRay=refract(-view,normal,airToWater);
    float2 offset=(bentRay.xy/max(.1,-bentRay.z)-flatRay.xy/max(.1,-flatRay.z))*min(depth,20)*.03;
    offset+=flatRay.xy/max(.1,-flatRay.z)*min(depth,20)*.01*_ScrollRelief.x;
    float latitude=(cell.y*_MapSize.w-.5)*3.141592654;
    offset.x/=max(.04,cos(latitude));
    // Limit the optical displacement inside the local wet cross-section.
    float limit=min(.08,max(0,boundary)*.25);
    offset*=min(1,limit/max(1e-6,length(offset)));
    return offset;
}
float2 FlatBedParallax(float2 cell,float3 view,float depth,float boundary)
{
    float3 ray=refract(-view,float3(0,0,1),1/1.333);
    float2 offset=ray.xy/max(.1,-ray.z)*min(depth,4000)*_ScrollHeightScale*_MapSize.x/6.283185307*_ScrollRelief.x;
    float latitude=(cell.y*_MapSize.w-.5)*3.141592654;
    offset.x/=max(.04,cos(latitude));
    float limit=min(.35,max(0,boundary)*.35);
    return offset*min(1,limit/max(1e-6,length(offset)));
}
float3 FlatBedArtwork(float3 position,float3 colour,float footprint)
{
    float sand=FlatDetailNoise(position*3.5);
    float gravel=FlatDetailNoise(position*8.5+17);
    float pebble=smoothstep(.54,.72,gravel);
    float bevel=.72+.28*gravel;
    float3 sandColour=colour*(.70+.55*sand+.04*sin(dot(position,float3(13,5,8))+sand*4));
    float3 stoneColour=lerp(colour,FlatColor(float3(.48,.47,.40)),.45)*bevel;
    float stones=1-smoothstep(.2,.7,footprint);
    float3 bed=lerp(sandColour,stoneColour,pebble*stones*.85);
    return lerp(colour,bed,1-smoothstep(.45,1.5,footprint));
}
float FlatBedCaustic(float3 position)
{
    float3 waves=sin(position+sin(position.yzx*.83+position.zxy*.31)*1.8);
    float bandDistance=min(abs(waves.x+waves.y),min(abs(waves.y+waves.z),abs(waves.z+waves.x)));
    return pow(saturate(1-bandDistance*1.6),8);
}
float2 FlatCausticMirrorUV(float2 uv)
{
    return 1-abs(frac(uv*.5)*2-1);
}
float FlatRiverSW3Caustics(float3 bedPos,float3 sunWarp,float3 drift,float phase0,float phase1,float blend,float footprint)
{
    float3 p=bedPos*2.6+sunWarp*.35;
    if(_VendorCausticsReady>.5)
    {
        float2 baseUV=float2(dot(p,float3(.85,.12,-.51)),dot(p,float3(.31,.88,.36)))*.34;
        float2 flowStep=float2(dot(drift,float3(.85,.12,-.51)),dot(drift,float3(.31,.88,.36)))*.24;
        float lod=clamp(log2(max(1,footprint*38)),0,5);
        float c1=lerp(FlatPresentationSample(_VendorCaustics,FlatCausticMirrorUV(baseUV-flowStep*phase0),lod).g,
                      FlatPresentationSample(_VendorCaustics,FlatCausticMirrorUV(baseUV-flowStep*phase1),lod).g,blend);
        float2 counterUV=baseUV*.80+float2(.37,.61);
        float c2=lerp(FlatPresentationSample(_VendorCaustics,FlatCausticMirrorUV(counterUV+flowStep*phase0*.72),lod).g,
                      FlatPresentationSample(_VendorCaustics,FlatCausticMirrorUV(counterUV+flowStep*phase1*.72),lod).g,blend);
        #ifdef UNITY_COLORSPACE_GAMMA
        c1=pow(max(0,c1),2.2);c2=pow(max(0,c2),2.2);
        #endif
        return max(0,min(c1,c2)*1.85);
    }
    return lerp(FlatBedCaustic(p*2.4-drift*phase0*2),FlatBedCaustic(p*2.4-drift*phase1*2),blend);
}
float FlatRiverCobbleRelief(float3 p,float cobbleFade,float pebbleFade)
{
    float bar=FlatDetailNoise(p*4.2+5);
    float cobble=smoothstep(.44,.76,FlatDetailNoise(p*13.5+23))*cobbleFade;
    float pebble=smoothstep(.48,.78,FlatDetailNoise(p*28.0+41))*pebbleFade;
    return bar*.36+cobble*.42+pebble*.22;
}
float3 FlatRiverClearBed(float3 baseBed,float3 bedPos,FlatRiverSection r,float3 right,float3 up,float2 lightXY,float footprint)
{
    float cobbleFade=1-smoothstep(.24,1.05,footprint);
    float pebbleFade=1-smoothstep(.08,.36,footprint);
    float barFade=1-smoothstep(.65,2.2,footprint);
    float hBase=FlatRiverCobbleRelief(bedPos,cobbleFade,pebbleFade);
    float stoneShade=.76+.46*hBase;
    // Sunlit 3D bevel on submerged river cobbles.
    float4 gradNoise=FlatDetailNoiseWithGradient(bedPos*13.5+23);
    float2 stoneGrad=float2(dot(gradNoise.yzw,right),dot(gradNoise.yzw,up));
    float bevel=clamp(1-dot(stoneGrad,lightXY)*.26*cobbleFade,.78,1.24);
    float section=FlatRiverDepthProfile(r);
    float shallowShelf=1-smoothstep(.08,.68,section);
    float thalweg=smoothstep(.54,.98,section);
    float bedLuma=dot(baseBed,float3(.299,.587,.114));
    float3 washedBase=lerp(baseBed,FlatColor(float3(.38,.54,.54))*(.45+.75*bedLuma),.55);
    float3 shelfSand=FlatColor(float3(.68,.71,.60));
    float3 midGravel=lerp(washedBase,FlatColor(float3(.44,.54,.53)),.52);
    float3 deepSlate=FlatColor(float3(.22,.43,.50));
    float3 sorted=lerp(midGravel,shelfSand,shallowShelf*.66);
    sorted=lerp(sorted,deepSlate,thalweg*.62);
    float ripple=.5+.5*sin(dot(bedPos,float3(15,9,-12))+hBase*3.2);
    sorted*=1+(ripple-.5)*.12*shallowShelf*barFade;
    return lerp(washedBase,sorted*stoneShade*bevel,barFade);
}

float3 FlatLakeWaterColumn(float path,float3 bedPosition,float groundPixel,float kilometresPerCell)
{
    // Continuous shallow-to-deep coloration for still freshwater. The land
    // substrate IDs under a surveyed lake describe the coarse simulation grid.
    float3 shallow=FlatColor(float3(.32,.60,.62));
    float3 deep=FlatColor(float3(.067,.31,.44));
    float3 water=lerp(deep,shallow,exp(-path*float3(.52,.29,.17)));
    // Silt is visible through very shallow water. Filter its ground footprint
    // before evaluating detail so a distant lake cannot acquire giant pebbles.
    float frequency=lerp(16,kilometresPerCell/.12,saturate(_EarthLakeReady));
    float visible=(1-smoothstep(.45,2.3,path))*(1-smoothstep(.18,.62,groundPixel*frequency));
    if(visible>.001)water*=1+(FlatDetailNoise(bedPosition*frequency+17)-.5)*.48*visible;
    return water;
}
float4 FlatLegacyMouth(float2 cell,float2 uv,float4 field)
{
 float4 result=float4(8,0,0,0);
 if(abs(field.g)>1.25)return result;
 float4 centre=FlatVisualSample(float4(uv,0,0));
 if(centre.g<=0||field.g>1.25)return result;
 float centreMeander=tex2Dlod(_HydroB,float4(uv,0,0)).r;
 [loop] [fastopt] for(int d=0;d<8;d++)
 {
  float2 offset=d<4?(FlatEdge(d)-.5)*2:float2(fmod((float)d,2)*2-1,d<6?-1:1);
  float2 nextUV=uv+offset*_MapSize.zw;
  if(nextUV.y<0||nextUV.y>1)continue;
  float4 h=tex2Dlod(_HydroA,float4(nextUV,0,0));
  float4 riverData=tex2Dlod(_HydroB,float4(nextUV,0,0));
  float connections=floor(riverData.a+.5);
  int opposite=d==0?1:d==1?0:d==2?3:d==3?2:FlatDiagonalDirection(-offset);
  if(h.a<.01||FlatLegacyLink(connections,opposite)<.5)continue;
  if(d>=4)
  {
   float4 reach=FlatLegacyBasinDiagonal(frac(cell),.5+offset,-offset,h);
   if(reach.r<result.r)result=reach;
   continue;
  }
  float2 edge=FlatRiverEdge(d,centreMeander,riverData.r,h.a);
  float2 delta=float2(.5,.5)-edge;
  float t=saturate(dot(frac(cell)-edge,delta)/max(.0001,dot(delta,delta)));
  float radius=lerp(h.a,min(.24,h.a*1.18),t);
  float distance=length(frac(cell)-edge-delta*t)-radius;
  // Exact inlet section matches the river side of the shared face.
  float face=d==0?frac(cell.x):d==1?1-frac(cell.x):d==2?frac(cell.y):1-frac(cell.y);
  float across=abs((d<2?frac(cell.y):frac(cell.x))-(d<2?edge.y:edge.x));
  if(face<.10)distance=lerp(across-h.a,distance,smoothstep(0,.10,face));
  float2 diff=-offset-h.gb;
  if(distance<result.r)result=float4(distance,h.r,offset*(dot(diff,diff)<.1?-radius:radius));
 }
 return result;
}
float FlatLegacyRiverWidthDelta(float radius,float stage)
{
    return min(.24,radius*FlatRiverWidthScale(stage))-radius;
}
float4 FlatLegacyBase(float2 cell,float2 uv,float4 hydro,float4 mixing,float4 field)
{
    float4 channel=FlatLegacyRiverSample(frac(cell),hydro,floor(mixing.a+.5),mixing.r,uv);
    float4 mouth=FlatLegacyMouth(cell,uv,field);
    if(mouth.r<channel.r)channel=mouth;
    // Once a surveyed world has been identified, its overview owns the
    // transition until the matching vector atlas finishes uploading.
    return lerp(channel,float4(8,0,0,0),step(.5,_RiverSurvey));
}
float4 FlatRiverReceiver(float2 cell,float2 uv,float4 hydro,float4 mixing,float4 field,out float2 bank)
{
    bank=0;
    #if defined(ORBIS_RIVER_VECTORS) || defined(ORBIS_RIVER_RASTER)
    return FlatRiverAtlasSample(cell,bank);
    #else
    [branch] if(FlatRiverFieldReady())return FlatRiverAtlasSample(cell,bank);
    float4 channel=FlatLegacyBase(cell,uv,hydro,mixing,field);
    float stage=FlatRiverStage(cell),radius=length(channel.ba);
    float current=FlatRiverActiveRadius(radius,stage);
    channel.r+=radius-current;channel.ba*=current/max(.000001,radius);
    channel.g*=FlatRiverDepthScale(stage);
    return channel;
    #endif
}
float4 FlatRiverReceiver(float2 cell,float2 uv,float4 hydro,float4 mixing,float4 field)
{float2 bank;return FlatRiverReceiver(cell,uv,hydro,mixing,field,bank);}

float FlatPlantRiverEdge(float2 root,float pixelsPerTile)
{
    float2 edgeRadius=float2(8,0);
    #if defined(ORBIS_RIVER_VECTORS) || defined(ORBIS_RIVER_RASTER)
    edgeRadius=FlatRiverBaseEdgeRadius(root);
    #else
    [branch] if(FlatRiverFieldReady())edgeRadius=FlatRiverBaseEdgeRadius(root);
    else if(_RiverSurvey<.5)
    {
        // Compact pre-atlas plant-root channel clearance (avoids inlining the 4-loop legacy
        // Bezier + ocean-mouth water solver 5x into every tree/grass/reflection prop loop).
        // Physical river half-width hydro.a lies in [0.01, 0.25]; >= 0.49 indicates an unbound fallback texture.
        float2 uv=(floor(root)+.5)*_MapSize.zw;
        float4 hydro=tex2Dlod(_HydroA,float4(uv,0,0));
        if(hydro.a>=.01&&hydro.a<.49)
        {
            float2 p=frac(root)-.5;
            float2 dir=dot(hydro.gb,hydro.gb)>1e-6?normalize(hydro.gb):float2(1,0);
            float along=clamp(dot(p,dir),-.5,.5);
            float dist=length(p-dir*along)-hydro.a;
            edgeRadius=float2(dist,hydro.a);
        }
    }
    #endif
    float radius=edgeRadius.y,stage=FlatRiverStage(root);
    return edgeRadius.x+radius-FlatRiverDisplayRadius(FlatRiverActiveRadius(radius,stage),pixelsPerTile);
}

float3 FlatVegetation(float3 col,float2 cell,float canopy,float pixelsPerTile)
{
    if(_Vegetation<.5)return col;
    float4 plant=FlatCanopyPlacement(cell);
    float2 offset=(cell-plant.xy)*2;
    offset.y/=lerp(1,FlatArtworkLatitudeScale(plant.y*_MapSize.w),_LocalPhysicalArtwork);
    float edge=FlatPlantRiverEdge(plant.xy,pixelsPerTile);
    return FlatCanopyColor(col,cell,canopy,pixelsPerTile,plant.zw,offset,edge);
}
float FlatRiverDistance(float2 p,float4 hydro,float connections,float meander,float2 uv,out float2 tangent)
{
    float2 cell=floor(uv*_MapSize.xy)+p;
    float4 channel=float4(8,0,0,0);
    [branch] if(FlatRiverFieldReady())channel=FlatRiverAtlasSample(cell);
    else
    {
        channel=FlatLegacyRiverSample(p,hydro,connections,meander,uv);
        channel.r-=FlatLegacyRiverWidthDelta(length(channel.ba),FlatRiverStage(cell));
    }
    tangent=channel.ba/max(.000001,length(channel.ba));
    return channel.r;
}
float FlatWaterMask(float channelEdge,float shore,float aa,out float edge)
{
    edge=min(channelEdge,-shore);
    return 1-smoothstep(-aa,aa,edge);
}
float FlatDisplayedWaterMask(float channelEdge,float shore,float aa,float pixelsPerTile,out float edge)
{
    edge=min(channelEdge,-shore);
    float channelAA=_RiverSurvey>.5?clamp(.65/max(1,pixelsPerTile),.00002,.28):aa;
    channelAA*=1-_RiverGlobeNarrowing;
    return max(1-smoothstep(-channelAA,channelAA,channelEdge),smoothstep(-aa,aa,shore));
}
float FlatReachSediment(float2 cell,FlatRiverSection channel,float shore,float fallback)
{
    float sediment=fallback;
    [branch] if(channel.width>.0001&&channel.edge<.22&&shore<.48&&FlatRiverFieldReady())
        sediment=FlatRiverAtlasSediment(cell);
    return sediment;
}
float3 FlatWaterBanks(float2 cell,float3 land,float3 relief,FlatRiverSection channel,
                      float sediment,float shore,float dry,float aa,float footprint,float beachScale)
{
    float basinBank=dry*(1-smoothstep(0,.16/beachScale+aa,-shore));
    [branch] if(dry>.001&&(basinBank>.001||(channel.width>.0001&&channel.edge<.20+aa&&shore<.12)))
    {
        float3 bed=FlatBedColor(cell);
        land=lerp(land,bed*relief,basinBank*.58);
        if(channel.width>.0001&&channel.edge<.20+aa)
            land=FlatRiverBank(land,bed,relief,FlatDetailPosition(cell),channel,sediment,footprint,dry,aa,shore);
    }
    return land;
}
float FlatPeriodicX(float x,float desiredFrequency)
{
    float periods=max(1,floor(_MapSize.x*desiredFrequency/6.2831853+.5));
    return x*6.2831853*periods/max(1,_MapSize.x);
}
struct FlatSWEField
{
    float weight;
    float deltaEta;
    float2 velocity;
    float2 slope;
    float foam;
    float hazardKind;
    float turbidity;
};
#ifndef ORBIS_FLAT_NO_SWE
float4 FlatReadSWEState(UNITY_DECLARE_TEX2D_NOSAMPLER(patchTexture),float2 uv)
{
#if defined(UNITY_SEPARATE_TEXTURE_SAMPLER)
    return UNITY_SAMPLE_TEX2D_SAMPLER_LOD(patchTexture,_SWEState,saturate(uv),0);
#else
    return tex2Dlod(patchTexture,float4(saturate(uv),0,0));
#endif
}
FlatSWEField FlatSamplePatchTexture(UNITY_DECLARE_TEX2D_NOSAMPLER(patchTexture),float4 rect,float ready,float2 cell)
{
    FlatSWEField swe=(FlatSWEField)0;
    if(ready<.5)return swe;
    float2 delta=cell-rect.xy;
    delta.x-=floor(delta.x/max(1,_MapSize.x))*_MapSize.x;
    float2 uv=delta/max(float2(1,1),rect.zw);
    float inside=step(0,uv.x)*step(0,uv.y)*step(uv.x,1)*step(uv.y,1)*ready;
    if(inside<.5)return swe;
    float fade=smoothstep(0,.14,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
    float2 texel=saturate(uv)*64.0-.5;
    float2 fuv=frac(texel);
    float2 smoothUV=(floor(texel)+fuv*fuv*(3.0-2.0*fuv)+.5)*(1.0/64.0);
    float4 state=FlatReadSWEState(patchTexture,smoothUV);
    float du=1.0/64.0;
    float ex1=FlatReadSWEState(patchTexture,smoothUV+float2(du,0)).r;
    float ex0=FlatReadSWEState(patchTexture,smoothUV-float2(du,0)).r;
    float ey1=FlatReadSWEState(patchTexture,smoothUV+float2(0,du)).r;
    float ey0=FlatReadSWEState(patchTexture,smoothUV-float2(0,du)).r;
    float dxMeters=max(4.0,_SWEParams.z);
    swe.weight=inside*fade;
    swe.deltaEta=state.r*swe.weight;
    swe.velocity=state.gb*swe.weight;
    swe.slope=clamp(float2(ex1-ex0,ey1-ey0)/(2.0*dxMeters)*18.0,-.42,.42)*swe.weight;
    swe.foam=saturate(state.a)*swe.weight;
    swe.hazardKind=_SWEParams.x;
    swe.turbidity=saturate(_SWEParams.y)*swe.weight;
    return swe;
}
FlatSWEField FlatSampleSWE(float2 cell)
{
    FlatSWEField cur=FlatSamplePatchTexture(_SWEState,_SWERect,_SWEReady,cell);
    if(_SWEBlend<1&&_SWEPreviousReady>.5)
    {
        FlatSWEField prev=FlatSamplePatchTexture(_SWEPreviousState,_SWEPreviousRect,_SWEPreviousReady,cell);
        float t=smoothstep(0,1,_SWEBlend);
        cur.weight=lerp(prev.weight,cur.weight,t);
        cur.deltaEta=lerp(prev.deltaEta,cur.deltaEta,t);
        cur.velocity=lerp(prev.velocity,cur.velocity,t);
        cur.slope=lerp(prev.slope,cur.slope,t);
        cur.foam=lerp(prev.foam,cur.foam,t);
        cur.turbidity=lerp(prev.turbidity,cur.turbidity,t);
    }
    return cur;
}
#endif
float FlatCoastFoam(float shoreDistance,float clock,float variation)
{
    float band=smoothstep(-.025,.015,shoreDistance)*(1-smoothstep(.12,.50,shoreDistance));
    // Increasing phase carries each crest toward the zero-distance shoreline.
    float phase=frac(shoreDistance*2.4+clock*.18+variation*.10);
    float crest=1-smoothstep(.045,.17,abs(phase-.5));
    return band*crest;
}
float FlatWaterSunSpecular(float3 normal,float3 view,float3 sun,float roughness)
{
    float3 halfDirection=normalize(view+sun+float3(0,0,1e-5));
    float nl=saturate(dot(normal,sun));
    float nh=saturate(dot(normal,halfDirection));
    float3 normalDx=ddx(normal),normalDy=ddy(normal);
    // A normalized Blinn-Phong profile gives resolved crests a defined edge.
    // Filter its angular footprint to keep small moving highlights stable.
    float variance=max(dot(normalDx,normalDx),dot(normalDy,normalDy));
    float baseWidth=max(.0001,roughness*roughness);
    float filteredWidth=baseWidth+min(.003,variance*.035);
    float exponent=max(16,12/filteredWidth-2);
    float profile=pow(nh,exponent)*sqrt(baseWidth/filteredWidth);
    float fresnel=OrbisHPSchlick(saturate(dot(view,halfDirection)));
    return profile*nl*lerp(2.4,3.8,fresnel);
}
struct FlatWaterSurfaceData
{
    float3 normal;
    float3 reflectionNormal;
    float skyWeight;
    float visible;
    float lake;
    float river;
};
float2 FlatDetailNormalDual(sampler2D tex,float3 basePos,float3 driftStep,float phase0,float phase1,float blend,float3 weights,float lod,float3 right,float3 up)
{
    float2 n0=0,n1=0;
    [loop] [fastopt] for(int k=0;k<2;k++)
    {
        float2 sampleN=FlatDetailNormal(tex,basePos+driftStep*(k==0?phase0:phase1),weights,0,lod,right,up);
        if(k==0)n0=sampleN;else n1=sampleN;
    }
    return lerp(n0,n1,blend);
}
float2 FlatRiverSW3NormalDual(sampler2D tex,FlatRiverWaveUV channelUV,float phase0,float phase1,float blend,float travel,float lod,float3 right,float3 up)
{
    float2 n0=0,n1=0;
    [loop] [fastopt] for(int k=0;k<2;k++)
    {
        float2 sampleN=FlatRiverSW3Normal(tex,channelUV,(k==0?phase0:phase1)*travel,lod,right,up);
        if(k==0)n0=sampleN;else n1=sampleN;
    }
    return lerp(n0,n1,blend);
}
float3 FlatWaterShadeCore(float2 cell,float4 mixing,float4 field,float3 bottom,float3 light,
                     float3 localView,float3 localLight,float waveStrength,float basinKind,
                     FlatCoastalSediment coast,FlatRiverSection channel,FlatSWEField swe,float inundated,
                     out FlatWaterSurfaceData surface)
{
    float3 submergedGround=bottom;
    float river=channel.influence*(1-inundated),basinLake=saturate(basinKind-3);
    float lake=basinLake*(1-river),ocean=(1-basinLake)*(1-river);
    float lakeOptics=lake*(1-inundated);
    float2 flow=lerp(float2(.25,.13),channel.flow,river);
    float speed=lerp(.20,.065,lake);
    float sweSpeed=length(swe.velocity);
    float2 sweDir=swe.velocity/max(.05,sweSpeed);
    float flowOverride=saturate(max(inundated,smoothstep(.20,1.50,sweSpeed))*swe.weight);
    flow=lerp(flow,sweDir*clamp(sweSpeed*.34,.18,1.25),flowOverride);
    speed=lerp(speed,clamp(.18+sweSpeed*.08,.18,.62),flowOverride);
    float phase0=frac(_WaterClock*speed),phase1=frac(_WaterClock*speed+.5),blend=abs(phase0*2-1);
    blend=blend*blend*(3-2*blend);
    float2 geo=cell*_MapSize.zw;float lon=(geo.x-.5)*6.28318530718,lat=(geo.y-.5)*3.14159265359;
    float3 sphere=float3(cos(lat)*sin(lon),sin(lat),cos(lat)*cos(lon));
    float3 artwork=FlatDetailPosition(cell),weights=FlatDetailWeights(sphere);
    float3 artDx=ddx(artwork),artDy=ddy(artwork);
    float kilometresPerCell=40030.2/max(1,_MapSize.x);
    float riffle=FlatRiverRiffle(channel,artwork);
    float travel=.21+.20*saturate(channel.depth/4)+riffle*.36;
    // The callers express view/light in east, north, up coordinates. A screen
    // derivative basis rotates with the camera and gives incompatible normals.
    float3 right=float3(cos(lon),0,-sin(lon));
    float3 up=float3(-sin(lat)*sin(lon),cos(lat),-sin(lat)*cos(lon));
    float2 tangentFlow=float2(flow.x*lerp(1,max(.02,cos(lat)),river),flow.y);
    float3 drift=right*tangentFlow.x+up*tangentFlow.y;
    drift*=length(flow)/max(.0001,length(drift));
    drift*=lerp(1,.60+.45*saturate(channel.depth/4),river);
    FlatRiverWaveUV channelUV=FlatRiverWaveCoordinates(channel,artwork,drift,weights,max(length(artDx),length(artDy)));
    float3 p0=(artwork-drift*phase0*3)*6;
    float3 p1=(artwork-drift*phase1*3)*6;
    float shoreDistance=coast.shore;
    float beachScale=max(1,coast.beachScale);
    // The basin's zero contour crosses the river under open water. Breakers
    // and beach material belong on the two banks, not across the thalweg.
    float opening=smoothstep(0,.75,-channel.edge/max(.0001,channel.width));
    coast.coverage*=(1-opening)*(1-inundated);
    float shoreVariation=.5+.5*sin(dot(artwork,float3(1.7,2.3,.9)));
    float shoreCrest=FlatCoastFoam(shoreDistance,_WaterClock,shoreVariation)*(1-opening)*(1-inundated)*(1-.62*coast.coverage*coast.mud);
    float2 slopes=0,microSlopes=0;
    float footprint=max(length(artDx*6),length(artDy*6));
    if(_VendorWaterReady>.5&&footprint<4)
    {
        float frequency=max(1,floor(_MapSize.x*.125))/max(1,_MapSize.x);
        float3 baseUV=artwork*frequency;
        float oceanFootprint=footprint*max(_VendorOceanNormal_TexelSize.z,_VendorOceanNormal_TexelSize.w)*frequency/6;
        float lod=max(0,log2(max(.0001,oceanFootprint)));
        float oceanFineLOD=max(0,log2(max(.0001,oceanFootprint*2)));
        float2 vendorSlope=0;
        if(river>.001)
        {
            // SW3 Realistic dual-layer isotropic ripples (RoughWaves + SharpWaves)
            // with zero extra texture samples, keeping calm reaches limpid and stripe-free.
            float streamLOD=max(0,log2(max(.0001,channelUV.footprint*max(_VendorStreamNormal_TexelSize.z,_VendorStreamNormal_TexelSize.w))));
            float2 n=FlatRiverSW3NormalDual(_VendorStreamNormal,channelUV,phase0,phase1,blend,travel,streamLOD,right,up);
            float sharpLOD=max(0,log2(max(.0001,footprint*max(_VendorLakeNormal_TexelSize.z,_VendorLakeNormal_TexelSize.w)/12)));
            float2 sharp=FlatDetailNormalDual(_VendorLakeNormal,artwork*.5,-drift*.70,phase0,phase1,blend,weights,sharpLOD,right,up);
            vendorSlope=(n*.76+sharp*.24)*(.155+.145*riffle)*river*channelUV.resolved;
            microSlopes=sharp*(.26+.11*riffle)*river;
        }
        if(river<.999)
        {
            float2 oceanSlope=0,lakeSlope=0,oceanMicro=0,lakeMicro=0;
            // Water classifications are wet-weighted fractions at a basin edge.
            // Blend decoded slopes so the original SW3 artwork changes smoothly.
            if(basinLake<1)
            {
                oceanSlope=FlatDetailNormalDual(_VendorOceanNormal,baseUV,-drift*.175,phase0,phase1,blend,weights,lod,right,up)*.12;
                oceanMicro=FlatDetailNormalDual(_VendorOceanNormal,baseUV*2,-drift*.35,phase0,phase1,blend,weights,oceanFineLOD,right,up)*.32;
            }
            if(basinLake>0)
            {
                // SW3 Clear uses SmoothWaves twice: the second layer is half
                // the tiling and travels at -0.8 of the primary layer's speed.
                // A repeat spans 3 km on Earth, independent of simulation width.
                float lakeFrequency=lerp(.9,kilometresPerCell/3,saturate(_EarthLakeReady));
                float3 lakeUV=artwork*lakeFrequency;
                float lakePixel=footprint/6*lakeFrequency*max(_VendorLakeSmoothNormal_TexelSize.z,_VendorLakeSmoothNormal_TexelSize.w);
                float lakeLOD=log2(max(1,lakePixel));
                float2 mainWave=FlatDetailNormalDual(_VendorLakeSmoothNormal,lakeUV,-drift*.34,phase0,phase1,blend,weights,lakeLOD,right,up);
                float2 broadWave=FlatDetailNormalDual(_VendorLakeSmoothNormal,lakeUV*.5,drift*.272,phase0,phase1,blend,weights,max(0,lakeLOD-1),right,up);
                lakeSlope=(mainWave*.7+broadWave*.3)*.223;
                lakeMicro=mainWave*.22;
            }
            vendorSlope+=lerp(oceanSlope,lakeSlope,basinLake)*(1-river);
            microSlopes+=lerp(oceanMicro,lakeMicro,basinLake)*(1-river);
        }
        slopes=vendorSlope;
    }
    else if(_VendorWaterReady<.5)
    {
        // Only retain an analytic fallback for projects missing the supplied art.
        float3 g=lerp(cos(p0)*sin(p0.yzx),cos(p1)*sin(p1.yzx),blend);
        slopes=float2(dot(g,right),dot(g,up))*.045;
        microSlopes=slopes;
    }
    slopes*=lerp(1,.65,lake);
    slopes*=1-smoothstep(1,4,footprint);
    float localFoam=0,currentStreak=0;
    if(swe.weight>.001)
    {
        float waveGain=swe.hazardKind>.5?lerp(2.8,4.6,step(2.5,swe.hazardKind)):1.0;
        slopes+=swe.slope*waveGain;
        if(sweSpeed>.18&&footprint<3.6)
        {
            float3 flowAxis=right*sweDir.x+up*sweDir.y;
            float3 crossAxis=-right*sweDir.y+up*sweDir.x;
            float alongCoord=dot(artwork,flowAxis)*22.0-_WaterClock*(1.5+sweSpeed*.55);
            float crossCoord=dot(artwork,crossAxis)*64.0;
            float currentRipple=sin(alongCoord+.65*sin(crossCoord*.32));
            float flowFade=smoothstep(.18,2.2,sweSpeed)*(1-smoothstep(1.2,3.6,footprint));
            slopes+=sweDir*(currentRipple*.095*flowFade);
            currentStreak=smoothstep(.66,.96,.5+.5*sin(crossCoord)*cos(alongCoord*.28))*smoothstep(.55,3.2,sweSpeed)*.30*flowFade;
        }
        float frontBand=inundated*smoothstep(.025,.08,swe.deltaEta)*(1-smoothstep(.12,.36,swe.deltaEta))*smoothstep(.28,1.45,sweSpeed);
        localFoam=saturate(swe.foam*.82+frontBand*.46+currentStreak*.85);
    }
    // Fade and ice suppress every animated surface contribution consistently.
    waveStrength=max(0,waveStrength);
    float3 normal=normalize(float3(-slopes*waveStrength,1));
    float nv=saturate(dot(normal,localView));
    float fresh=lerp(mixing.b,1,max(river,inundated*step(.5,1.5-swe.hazardKind)));
    float3 absorption=lerp(float3(.24,.07,.035),float3(.09,.035,.018),fresh);
    float3 scattering=lerp(float3(.005,.025,.038),float3(.004,.019,.022),fresh);
    // Crystal-clear rivers: pure-water red attenuation creates natural aqua-cyan
    // depth cues while ultra-high green/blue transmission and minimal scatter
    // expose submerged cobbles, sandbars and prismatic caustics clearly.
    absorption=lerp(absorption,float3(.224,.026,.011),river);
    scattering=lerp(scattering,float3(.002,.010,.016),river);
    if(swe.turbidity>.001)
    {
        float isRainFlood=step(.5,swe.hazardKind)*step(swe.hazardKind,1.5);
        float isTyphoon=step(1.5,swe.hazardKind)*step(swe.hazardKind,2.5);
        float3 hazAbs=isRainFlood>.5?float3(.27,.15,.19):(isTyphoon>.5?float3(.20,.09,.072):float3(.18,.065,.040));
        float3 hazScat=isRainFlood>.5?float3(.052,.056,.032):(isTyphoon>.5?float3(.024,.050,.046):float3(.014,.044,.058));
        float optMix=saturate(swe.turbidity*max(inundated,smoothstep(.15,1.6,abs(swe.deltaEta))*.65));
        absorption=lerp(absorption,hazAbs,optMix);
        scattering=lerp(scattering,hazScat,optMix);
    }
    float opticalShore=field.g*beachScale;
    float channelDepth=max(.12,channel.depth*FlatRiverDepthProfile(channel)*(1-.23*riffle));
    // The bank shoals to the waterline. In the opening, start the shelf profile
    // at the submerged channel bed and carry it into the receiving basin.
    float entranceDepth=lerp(.15,channelDepth,opening);
    // On a low-resolution Earth, an entire lake can fit in the ocean shelf's
    // 1.5-cell shore fade. Keep its littoral band tied to ground distance.
    float lakeShore=field.g*lerp(1,kilometresPerCell,saturate(_EarthLakeReady));
    float bankProfile=lerp(smoothstep(0,1.5,opticalShore),smoothstep(0,.65,lakeShore),basinLake);
    float basinDepth=lerp(entranceDepth,max(.15,field.b),bankProfile);
    float depth=lerp(basinDepth,channelDepth,river);
    // A small depositional bar modulates only the optical path through the
    // very shallow sandy apron. Inlet depths and the shoreline mask stay exact.
    float shoal=coast.bar*coast.coverage*ocean*(1-smoothstep(4,15,depth));
    depth-=min(depth*.26,1.15)*shoal;
    if(swe.weight>.001)
        depth=lerp(max(.08,depth+swe.deltaEta),max(.05,swe.deltaEta),inundated);
    float clearance=lerp(lerp(max(0,shoreDistance)/beachScale,max(0,-channel.edge),river),saturate(swe.deltaEta*.6),inundated);
    float2 bedCell=cell,refractOffset=0;
    if(_FieldReady>.5)
    {
        refractOffset=FlatBedRefraction(cell,localView,normal,depth,clearance);
        bedCell+=refractOffset;
    }
    float3 bedPosition=FlatDetailPosition(bedCell);
    float2 depthCell=cell+FlatBedParallax(cell,localView,depth,clearance);
    float measuredDepth=depth,depthRelief=1;
    float gridBedWeight=ocean*(1-coast.surveyDepthWeight)*(1-inundated);
    if(_FieldReady>.5&&gridBedWeight>.001)
        depthRelief=lerp(1,FlatSampleSeabed(depthCell,localLight,measuredDepth),gridBedWeight);
    if(_FieldReady>.5&&lakeOptics<1)
    {
        // Focused light follows the submerged river or coastal bed.
        float3 sunView=normalize(float3(localLight.xy,max(.1,localLight.z)));
        float2 sunOffset=FlatBedRefraction(cell,sunView,normal,depth,clearance);
        float3 sunWarp=(right*sunOffset.x*max(.04,cos(lat))+up*sunOffset.y)*12;
        float focus=lerp(FlatBedCaustic(p0*.6+sunWarp),FlatBedCaustic(p1*.6+sunWarp),blend);
        float caustic=focus*saturate(localLight.z)*waveStrength;
        caustic*=exp(-depth*.12)*(1-exp(-depth*.7))*smoothstep(.03,.18,clearance)*(1-smoothstep(.2,1,footprint));
        bottom=FlatBedColor(bedCell,1-river)*lerp(.72,.96,river);
        if(depth<35&&footprint<1.5)bottom=lerp(bottom,FlatBedArtwork(bedPosition,bottom,footprint),1-smoothstep(18,35,depth));
        float riverCaustic=caustic;
        if(river>.001)
        {
            bottom=lerp(bottom,FlatRiverClearBed(bottom,bedPosition,channel,right,up,localLight.xy,footprint/2.4),river);
            float causticFade=exp(-depth*.11)*(1-exp(-depth*1.1))*smoothstep(.02,.16,clearance)*(1-smoothstep(.18,.78,footprint));
            riverCaustic=FlatRiverSW3Caustics(bedPosition,sunWarp,drift,phase0*travel,phase1*travel,blend,footprint/6)*saturate(localLight.z)*saturate(waveStrength)*causticFade;
        }
        if(river>.001&&(abs(channel.bend)>.06||channel.grade>.001))
        {
            float bedAcross=channel.across+dot(bedCell-cell,channel.lateralGradient)/max(.0001,channel.width);
            bottom=lerp(bottom,FlatRiverSubstrate(bottom,bedPosition,channel,bedAcross,mixing.g,footprint/6),river*.68);
        }
        float3 sediment=coast.albedo;
        if(coast.coverage*ocean>.001&&depth<32)
        {
            // Evaluate the same shore material at the refracted bed point. Its
            // local tangent extends the measured shoreline over the short ray.
            float bedShore=coast.shore+dot(bedPosition-artwork,coast.seaward)*beachScale;
            sediment=FlatCoastArtwork(bedPosition,coast.seaward,bedShore,0,coast.mud,footprint/6,_WaterClock).albedo;
        }
        bottom=lerp(bottom,sediment*(1+.13*shoal),coast.coverage*ocean);
        float causticGain=lerp(caustic*.65,riverCaustic*.90,river);
        bottom=(bottom*(1+causticGain)+riverCaustic*.05*river)*depthRelief;
        // Preserve drowned fields, vegetation and terrain relief beneath shallow floodwater via HPWaterOptics transmission.
        bottom=lerp(bottom,submergedGround*(.80+.40*caustic),inundated*(1-smoothstep(1.6,4.8,depth)));
    }
    float refractedCos=sqrt(max(.1,1-(1-nv*nv)/(1.333*1.333)));
    float3 transmission;
    float opticalDepth=lerp(depth,depth*2.6,inundated);
    float3 scatteringLight=OrbisHPScatter(light,absorption,scattering,opticalDepth/refractedCos,transmission);
    float fresnel=OrbisHPSchlick(nv);
    // The Clear preset uses a fourth-power reflection response. Retain the
    // air/water reflectance at normal incidence on lakes and clear rivers.
    float clearFresnel=max(lakeOptics,river*(1-inundated)*(1-saturate(swe.turbidity*1.5)));
    fresnel=lerp(fresnel,.02037+.97963*pow(1-nv,4),clearFresnel);
    float3 sky=light*float3(.34,.48,.58);
    float3 body=bottom*light*transmission+scatteringLight;
    if(lakeOptics>0)body=lerp(body,light*FlatLakeWaterColumn(depth/refractedCos,bedPosition,footprint/6,kilometresPerCell),lakeOptics);
    float3 result=body*(1-fresnel)+sky*fresnel;
    // Physically, 80m and 2200m are both optically opaque. Optional cartographic
    // bathymetry makes the true data depth readable; it is NOT deep-water transparency.
    float chartMetres=depth;
    if(_FieldReady>.5&&_ScrollRelief.x>0)
        chartMetres=lerp(depth,max(.15,measuredDepth),_ScrollRelief.x*smoothstep(.10,.70,field.g)*gridBedWeight);
    float logDepth=log2(1+chartMetres);
    float3 depthColor=lerp(FlatColor(float3(.20,.65,.65)),FlatColor(float3(.045,.34,.54)),smoothstep(1,7.5,logDepth));
    depthColor=lerp(depthColor,FlatColor(float3(.015,.10,.25)),smoothstep(7.5,11.97,logDepth));
    depthColor=lerp(depthColor,FlatColor(float3(.19,.48,.48)),fresh*.12);
    depthColor=lerp(depthColor,FlatColor(float3(.15,.56,.66)),river*.36);
    // Wider shelf and continental-slope colour intervals make submerged banks,
    // ridges and depressions legible after the natural optical signal is lost.
    float3 marineChart=lerp(FlatColor(float3(.28,.64,.64)),FlatColor(float3(.12,.52,.61)),smoothstep(1.6,7.4,logDepth));
    marineChart=lerp(marineChart,FlatColor(float3(.06,.33,.52)),smoothstep(7.1,10.35,logDepth));
    marineChart=lerp(marineChart,FlatColor(float3(.025,.15,.32)),smoothstep(10.15,11.97,logDepth));
    depthColor=lerp(depthColor,marineChart,ocean*saturate(_FieldReady));
    float lakeDepth=lerp(.30,.62,smoothstep(2,30,depth));
    float riverChart=lerp(.015,.22,smoothstep(4.0,16.0,depth))*lerp(1.0,.32,channelUV.resolved);
    float chartDepth=lerp(lerp(.70,lakeDepth,lake),riverChart,river)*(1-inundated)+inundated*.32*smoothstep(.38,2.3,depth);
    chartDepth*=1-.91*coast.coverage*ocean*(1-smoothstep(.5,24,depth));
    result=lerp(result,depthColor*light*depthRelief,saturate(_Bathymetry)*chartDepth);
    // At ordinary inland viewing angles the sharp solar glint rarely enters
    // the camera. Ripple-facing and shadowed facets still redistribute the
    // diffuse light on the resolved channel, especially over shallow beds.
    float rippleLight=clamp(-2.8*dot(slopes,localLight.xy),-.13,.13);
    float facets=rippleLight*(river*channelUV.resolved+lake*.24);
    result*=1+facets*saturate(waveStrength)*smoothstep(0,.20,localLight.z);
    if(swe.weight>.001)
    {
        float waveFace=clamp(-2.6*dot(swe.slope,localLight.xy),-.18,.24);
        result*=1+waveFace*saturate(waveStrength)*smoothstep(0,.15,localLight.z);
    }
    float microFade=1-smoothstep(.8,2.8,footprint);
    float3 glitterNormal=normalize(float3(-(slopes+microSlopes*microFade)*waveStrength,1));
    float glint=FlatWaterSunSpecular(glitterNormal,localView,localLight,lerp(.026,.020,max(lake,river)));
    float sheen=FlatWaterSunSpecular(normal,localView,localLight,.13);
    float sunVisibility=smoothstep(0,.12,localLight.z);
    float sparkle=(glint*.78+sheen*.025)*sunVisibility*saturate(waveStrength);
    result+=light*FlatColor(float3(.96,.99,1))*sparkle;
    float foam=localFoam*waveStrength;
    if(_VendorWaterReady<.5)
    {
        foam=max(foam,shoreCrest*(1-river)*lerp(.20,.04,lake)*waveStrength);
        float2 bands=float2(dot(p0,float3(.51,.37,.72)),dot(p1,float3(.51,.37,.72)));
        float2 crests=smoothstep(.79,1,abs(sin(bands)));
        float foamGrain=lerp(crests.x,crests.y,blend);
        foam+=river*riffle*foamGrain*.22*waveStrength*(1-smoothstep(1,4,footprint));
    }
    if(_VendorWaterReady>.5&&footprint<4)
    {
        // SW3 foam artwork follows the same two-phase flow as its normal layers.
        float3 foamUV=artwork*.25;
        float foamLOD=max(0,log2(max(1,footprint*16)));
        float riverFoam=0,basinFoam=0;
        if(river>.001&&riffle>.001)
        {
            float channelLOD=max(0,log2(max(.0001,channelUV.footprint*max(_VendorRiverFoam_TexelSize.z,_VendorRiverFoam_TexelSize.w))));
            float a=FlatRiverSW3Foam(_VendorRiverFoam,channelUV,phase0*travel,channelLOD);
            float b=FlatRiverSW3Foam(_VendorRiverFoam,channelUV,phase1*travel,channelLOD);
            riverFoam=smoothstep(.28,.54,lerp(a,b,blend))*riffle*.48*river;
        }
        if(river<.999)
        {
            float a=FlatDetailTexture(_VendorOceanFoam,foamUV-drift*phase0*.15,weights,0,foamLOD);
            float b=FlatDetailTexture(_VendorOceanFoam,foamUV-drift*phase1*.15,weights,0,foamLOD);
            float foamTex=lerp(a,b,blend);
            float brokenCrest=saturate(length(slopes)*4-.15);
            basinFoam=smoothstep(.3,.8,foamTex)*(shoreCrest*lerp(.34,.045,basinLake)*(1-river)+brokenCrest*.18*ocean);
            if(localFoam>.001)
                foam=localFoam*(.62+.38*smoothstep(.20,.78,foamTex))*waveStrength;
        }
        foam+=(basinFoam+riverFoam)*waveStrength*(1-smoothstep(1,4,footprint));
    }
    surface.normal=normal;
    surface.reflectionNormal=normalize(float3(-(slopes+microSlopes*microFade*.38)*waveStrength,1));
    surface.lake=lakeOptics;
    surface.river=river*(1-inundated)*(1-saturate(swe.turbidity*1.5));
    surface.visible=1-saturate(foam);
    surface.skyWeight=fresnel*(1-saturate(_Bathymetry)*chartDepth)*surface.visible;
    return lerp(result,light*FlatColor(float3(.82,.90,.86)),saturate(foam));
}
float3 FlatWaterShade(float2 cell,float4 mixing,float4 field,float3 bottom,float3 light,
                     float3 localView,float3 localLight,float waveStrength,float basinKind,
                     FlatCoastalSediment coast,FlatRiverSection channel,out FlatWaterSurfaceData surface)
{
    FlatSWEField zeroSWE=(FlatSWEField)0;
    return FlatWaterShadeCore(cell,mixing,field,bottom,light,localView,localLight,waveStrength,basinKind,coast,channel,zeroSWE,0,surface);
}
float3 FlatWaterShade(float2 cell,float4 hydro,float4 mixing,float4 field,float3 bottom,float3 light,
                     float3 localView,float3 localLight,float waveStrength,float waterKind,float waterEdge,
                     FlatCoastalSediment coast,out FlatWaterSurfaceData surface)
{
    float river=step(4.5,waterKind);
    float4 data=river>.5?float4(waterEdge,hydro.r,hydro.gb*(hydro.a/max(.0001,length(hydro.gb)))):float4(8,0,0,0);
    // Standalone material users can supply an explicit channel. At a measured
    // inlet both freshwater and basin calls resolve the same submerged reach.
    float inlet=FlatRiverFieldReady()&&abs(field.g)<.48;
    float2 bank=0;
    if(inlet>.5)data=FlatRiverAtlasSample(cell,bank);
    FlatRiverSection channel=FlatRiverSectionAt(cell,data,field,bank);
    if(inlet<.5)channel.influence=river;
    return FlatWaterShade(cell,mixing,field,bottom,light,localView,localLight,waveStrength,min(waterKind,4),coast,channel,surface);
}
float3 FlatWaterShade(float2 cell,float4 hydro,float4 mixing,float4 field,float3 bottom,float3 light,
                     float3 localView,float3 localLight,float waveStrength,float waterKind,float waterEdge,out FlatWaterSurfaceData surface)
{
    FlatCoastalSediment coast=(FlatCoastalSediment)0;
    float2 seaward;
    coast.shore=FlatMetricShore(cell,field.g,seaward);
    return FlatWaterShade(cell,hydro,mixing,field,bottom,light,localView,localLight,waveStrength,waterKind,waterEdge,coast,surface);
}
float3 FlatWaterShade(float2 cell,float4 hydro,float4 mixing,float4 field,float3 bottom,float3 light,
                     float3 localView,float3 localLight,float waveStrength,float waterKind,float waterEdge)
{
    FlatWaterSurfaceData surface;
    return FlatWaterShade(cell,hydro,mixing,field,bottom,light,localView,localLight,waveStrength,waterKind,waterEdge,surface);
}
#endif
