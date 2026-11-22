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
#ifndef ORBIS_FLAT_NO_SWE
// Both simulation patches have the same bilinear/clamp sampling state.
UNITY_DECLARE_TEX2D(_SWEState);
UNITY_DECLARE_TEX2D_NOSAMPLER(_SWEPreviousState);
#endif
float4 _SWERect,_SWEPreviousRect;
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
            if(count<1.5)result=FlatBezierDistance(p,float2(.5,.5),b,(float2(.5,.5)+b)*.5,sourceRadius,rb,(sourceRadius+rb)*.5,bend*.35,bestTangent);
            [loop] [fastopt] for(int d=0;d<4;d++)
            {
                if(count<1.5||d==outlet||links[d]<.5)continue;
                float2 direction=float2(1,0);
                float2 a=FlatRiverEdge(d,meander,neighborMeanders[d],max(hydro.a,neighborWidths[d]));bool straight=(d<2&&outlet<2)||(d>=2&&outlet>=2);
                float candidate=FlatBezierDistance(p,a,b,straight?(a+b)*.5:float2(.5,.5),radii[d],rb,hydro.a,bend*(straight?1:.35),direction);
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
 [loop] for(int d=0;d<8;d++)
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
    float4 channel=float4(8,0,0,0);
    #if defined(ORBIS_RIVER_VECTORS) || defined(ORBIS_RIVER_RASTER)
    channel=FlatRiverBaseSample(root);
    #else
    [branch] if(FlatRiverFieldReady())channel=FlatRiverBaseSample(root);
    else
    {
        float2 uv=(floor(root)+.5)*_MapSize.zw;
        float4 hydro=tex2Dlod(_HydroA,float4(uv,0,0)),mixing=tex2Dlod(_HydroB,float4(uv,0,0));
        channel=FlatLegacyBase(root,uv,hydro,mixing,FlatVisualSample(float4(root*_MapSize.zw,0,0)));
    }
    #endif
    float radius=length(channel.ba),stage=FlatRiverStage(root);
    return channel.r+radius-FlatRiverDisplayRadius(FlatRiverActiveRadius(radius,stage),pixelsPerTile);
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
#ifndef ORBIS_FLAT_NO_SWE
float3 FlatPatchContribution(UNITY_DECLARE_TEX2D_NOSAMPLER(patchTexture),float4 rect,float ready,float2 cell)
{
    float2 delta=cell-rect.xy;
    delta.x-=floor(delta.x/max(1,_MapSize.x))*_MapSize.x;
    float2 uv=delta/max(float2(1,1),rect.zw);
    float inside=step(0,uv.x)*step(0,uv.y)*step(uv.x,1)*step(uv.y,1)*ready;
    float fade=smoothstep(0,.12,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
    float4 state=UNITY_SAMPLE_TEX2D_SAMPLER(patchTexture,_SWEState,saturate(uv));
    return float3((state.rg*255-128)/127*.18*state.a,state.b*.35)*inside*fade;
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
    float skyWeight;
    float visible;
    float lake;
};
float3 FlatWaterShade(float2 cell,float4 mixing,float4 field,float3 bottom,float3 light,
                     float3 localView,float3 localLight,float waveStrength,float basinKind,
                     FlatCoastalSediment coast,FlatRiverSection channel,out FlatWaterSurfaceData surface)
{
    float river=channel.influence,basinLake=saturate(basinKind-3);
    float lake=basinLake*(1-river),ocean=(1-basinLake)*(1-river);
    float2 flow=lerp(float2(.25,.13),channel.flow,river);
    float speed=lerp(.20,.065,lake);
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
    coast.coverage*=1-opening;
    float shoreVariation=.5+.5*sin(dot(artwork,float3(1.7,2.3,.9)));
    float shoreCrest=FlatCoastFoam(shoreDistance,_WaterClock,shoreVariation)*(1-opening)*(1-.62*coast.coverage*coast.mud);
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
            float streamLOD=max(0,log2(max(.0001,channelUV.footprint*max(_VendorStreamNormal_TexelSize.z,_VendorStreamNormal_TexelSize.w))));
            float2 stream0=FlatRiverSW3Normal(_VendorStreamNormal,channelUV,phase0*travel,streamLOD,right,up);
            float2 stream1=FlatRiverSW3Normal(_VendorStreamNormal,channelUV,phase1*travel,streamLOD,right,up);
            float2 n=lerp(stream0,stream1,blend);
            vendorSlope=n*(.22+.12*riffle)*river*channelUV.resolved;
            // Short capillary crests have a spread of orientations even in a
            // straight pool. Geographic SW3 detail resolves these specular
            // facets independently of the long waves aligned to the channel.
            float sharpLOD=max(0,log2(max(.0001,footprint*max(_VendorLakeNormal_TexelSize.z,_VendorLakeNormal_TexelSize.w)/12)));
            float2 fine0=FlatDetailNormal(_VendorLakeNormal,artwork*.5-drift*phase0*.70,weights,0,sharpLOD,right,up);
            float2 fine1=FlatDetailNormal(_VendorLakeNormal,artwork*.5-drift*phase1*.70,weights,0,sharpLOD,right,up);
            microSlopes=lerp(fine0,fine1,blend)*(.28+.10*riffle)*river;
        }
        if(river<.999)
        {
            float2 oceanSlope=0,lakeSlope=0,oceanMicro=0,lakeMicro=0;
            // Water classifications are wet-weighted fractions at a basin edge.
            // Blend decoded slopes so the original SW3 artwork changes smoothly.
            if(basinLake<1)
            {
                float2 ocean0=FlatDetailNormal(_VendorOceanNormal,baseUV-drift*phase0*.175,weights,0,lod,right,up);
                float2 ocean1=FlatDetailNormal(_VendorOceanNormal,baseUV-drift*phase1*.175,weights,0,lod,right,up);
                oceanSlope=lerp(ocean0,ocean1,blend)*.12;
                float2 fine0=FlatDetailNormal(_VendorOceanNormal,baseUV*2-drift*phase0*.35,weights,0,oceanFineLOD,right,up);
                float2 fine1=FlatDetailNormal(_VendorOceanNormal,baseUV*2-drift*phase1*.35,weights,0,oceanFineLOD,right,up);
                oceanMicro=lerp(fine0,fine1,blend)*.32;
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
                float2 main0=FlatDetailNormal(_VendorLakeSmoothNormal,lakeUV-drift*phase0*.34,weights,0,lakeLOD,right,up);
                float2 main1=FlatDetailNormal(_VendorLakeSmoothNormal,lakeUV-drift*phase1*.34,weights,0,lakeLOD,right,up);
                float2 broad0=FlatDetailNormal(_VendorLakeSmoothNormal,lakeUV*.5+drift*phase0*.272,weights,0,max(0,lakeLOD-1),right,up);
                float2 broad1=FlatDetailNormal(_VendorLakeSmoothNormal,lakeUV*.5+drift*phase1*.272,weights,0,max(0,lakeLOD-1),right,up);
                float2 mainWave=lerp(main0,main1,blend),broadWave=lerp(broad0,broad1,blend);
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
    float localFoam=0;
#ifndef ORBIS_FLAT_NO_SWE
    float3 localContribution=FlatPatchContribution(_SWEState,_SWERect,_SWEReady,cell);
    if(_SWEBlend<1)
        localContribution=lerp(FlatPatchContribution(_SWEPreviousState,_SWEPreviousRect,_SWEPreviousReady,cell),localContribution,smoothstep(0,1,_SWEBlend));
    if(_VendorWaterReady<.5){slopes+=localContribution.xy;localFoam=localContribution.z;}
#endif
    // Fade and ice suppress every animated surface contribution consistently.
    waveStrength=max(0,waveStrength);
    float3 normal=normalize(float3(-slopes*waveStrength,1));
    float nv=saturate(dot(normal,localView));
    float fresh=lerp(mixing.b,1,river);
    float3 absorption=lerp(float3(.24,.07,.035),float3(.09,.035,.018),fresh);
    float3 scattering=lerp(float3(.005,.025,.038),float3(.004,.019,.022),fresh);
    absorption=lerp(absorption,float3(.22,.076,.047),river);
    scattering=lerp(scattering,float3(.014,.041,.038),river);
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
    float clearance=lerp(max(0,shoreDistance)/beachScale,max(0,-channel.edge),river);
    float2 bedCell=cell;
    if(_FieldReady>.5)bedCell+=FlatBedRefraction(cell,localView,normal,depth,clearance);
    float3 bedPosition=FlatDetailPosition(bedCell);
    float2 depthCell=cell+FlatBedParallax(cell,localView,depth,clearance);
    float measuredDepth=depth,depthRelief=1;
    float gridBedWeight=ocean*(1-coast.surveyDepthWeight);
    if(_FieldReady>.5&&gridBedWeight>.001)
        depthRelief=lerp(1,FlatSampleSeabed(depthCell,localLight,measuredDepth),gridBedWeight);
    if(_FieldReady>.5&&lake<1)
    {
        // Focused light follows the submerged river or coastal bed.
        float3 sunView=normalize(float3(localLight.xy,max(.1,localLight.z)));
        float2 sunOffset=FlatBedRefraction(cell,sunView,normal,depth,clearance);
        float3 sunWarp=(right*sunOffset.x*max(.04,cos(lat))+up*sunOffset.y)*12;
        float focus=lerp(FlatBedCaustic(p0*.6+sunWarp),FlatBedCaustic(p1*.6+sunWarp),blend);
        float caustic=focus*saturate(localLight.z)*waveStrength;
        caustic*=exp(-depth*.12)*(1-exp(-depth*.7))*smoothstep(.03,.18,clearance)*(1-smoothstep(.2,1,footprint));
        bottom=FlatBedColor(bedCell,1-river)*lerp(.72,.85,river);
        if(depth<35&&footprint<1.5)bottom=lerp(bottom,FlatBedArtwork(bedPosition,bottom,footprint),1-smoothstep(18,35,depth));
        if(river>.001&&(abs(channel.bend)>.06||channel.grade>.001))
        {
            float bedAcross=channel.across+dot(bedCell-cell,channel.lateralGradient)/max(.0001,channel.width);
            bottom=lerp(bottom,FlatRiverSubstrate(bottom,bedPosition,channel,bedAcross,mixing.g,footprint/6),river);
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
        bottom*=(1+caustic*.65)*depthRelief;
    }
    float refractedCos=sqrt(max(.1,1-(1-nv*nv)/(1.333*1.333)));
    float3 transmission;
    float3 scatteringLight=OrbisHPScatter(light,absorption,scattering,depth/refractedCos,transmission);
    float fresnel=OrbisHPSchlick(nv);
    // The Clear preset uses a fourth-power reflection response. Retain the
    // air/water reflectance at normal incidence on the Built-in sky path.
    fresnel=lerp(fresnel,.02037+.97963*pow(1-nv,4),lake);
    float3 sky=light*float3(.34,.48,.58);
    float3 body=bottom*light*transmission+scatteringLight;
    if(lake>0)body=lerp(body,light*FlatLakeWaterColumn(depth/refractedCos,bedPosition,footprint/6,kilometresPerCell),lake);
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
    // Wider shelf and continental-slope colour intervals make submerged banks,
    // ridges and depressions legible after the natural optical signal is lost.
    float3 marineChart=lerp(FlatColor(float3(.28,.64,.64)),FlatColor(float3(.12,.52,.61)),smoothstep(1.6,7.4,logDepth));
    marineChart=lerp(marineChart,FlatColor(float3(.06,.33,.52)),smoothstep(7.1,10.35,logDepth));
    marineChart=lerp(marineChart,FlatColor(float3(.025,.15,.32)),smoothstep(10.15,11.97,logDepth));
    depthColor=lerp(depthColor,marineChart,ocean*saturate(_FieldReady));
    float lakeDepth=lerp(.30,.62,smoothstep(2,30,depth));
    float chartDepth=lerp(lerp(.70,lakeDepth,lake),.18,river);
    chartDepth*=1-.91*coast.coverage*ocean*(1-smoothstep(.5,24,depth));
    result=lerp(result,depthColor*light*depthRelief,saturate(_Bathymetry)*chartDepth);
    // At ordinary inland viewing angles the sharp solar glint rarely enters
    // the camera. Ripple-facing and shadowed facets still redistribute the
    // diffuse light on the resolved channel, especially over shallow beds.
    float rippleLight=clamp(-2.8*dot(slopes,localLight.xy),-.13,.13);
    float facets=rippleLight*(river*channelUV.resolved+lake*.24);
    result*=1+facets*saturate(waveStrength)*smoothstep(0,.20,localLight.z);
    float microFade=1-smoothstep(.8,2.8,footprint);
    float3 glitterNormal=normalize(float3(-(slopes+microSlopes*microFade)*waveStrength,1));
    float glint=FlatWaterSunSpecular(glitterNormal,localView,localLight,lerp(.026,.020,max(lake,river)));
    float sheen=FlatWaterSunSpecular(normal,localView,localLight,.13);
    float sunVisibility=smoothstep(0,.12,localLight.z);
    float sparkle=(glint*.78+sheen*.025)*sunVisibility*saturate(waveStrength);
    result+=light*FlatColor(float3(1,.98,.91))*sparkle;
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
            riverFoam=smoothstep(.36,.66,lerp(a,b,blend))*riffle*.57*river;
        }
        if(river<.999)
        {
            float a=FlatDetailTexture(_VendorOceanFoam,foamUV-drift*phase0*.15,weights,0,foamLOD);
            float b=FlatDetailTexture(_VendorOceanFoam,foamUV-drift*phase1*.15,weights,0,foamLOD);
            float brokenCrest=saturate(length(slopes)*4-.15);
            basinFoam=smoothstep(.3,.8,lerp(a,b,blend))*(shoreCrest*lerp(.34,.045,basinLake)*(1-river)+brokenCrest*.18*ocean);
        }
        foam+=(basinFoam+riverFoam)*waveStrength*(1-smoothstep(1,4,footprint));
    }
    surface.normal=normal;
    surface.lake=lake;
    surface.visible=1-saturate(foam);
    surface.skyWeight=fresnel*(1-saturate(_Bathymetry)*chartDepth)*surface.visible;
    return lerp(result,light*FlatColor(float3(.82,.90,.86)),saturate(foam));
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
