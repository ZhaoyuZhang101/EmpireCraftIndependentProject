#ifndef ORBIS_FLAT_WATER_INCLUDED
#define ORBIS_FLAT_WATER_INCLUDED
#include "HPWaterOptics.cginc"
#include "../FlatSurfaceField.cginc"
#include "../FlatPolarIce.cginc"
sampler2D _HydroA,_HydroB,_GeoTex;
sampler2D _VendorStreamNormal,_VendorOceanNormal;float _VendorWaterReady;
sampler2D _VendorLakeNormal,_VendorOceanFoam,_VendorRiverFoam;
float4 _VendorStreamNormal_TexelSize,_VendorOceanNormal_TexelSize;
#ifndef ORBIS_FLAT_NO_SWE
sampler2D _SWEState,_SWEPreviousState;
#endif
float4 _SWERect,_SWEPreviousRect;
float _SWEPreviousReady,_SWEBlend;
float _SWEReady,_WaterClock,_ShowBanks,_ShowWater,_ShowPlumes,_WaveAmount;
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
float2 FlatEdge(int direction)
{
    return direction==0?float2(0,.5):direction==1?float2(1,.5):direction==2?float2(.5,0):float2(.5,1);
}
float2 FlatRiverEdge(int direction,float2 tile)
{
    tile.x=tile.x-floor(tile.x/_MapSize.x)*_MapSize.x;
    float frequency=6.283185307*max(1,floor(_MapSize.x/12+.5))/_MapSize.x;
    float faceX=tile.x+(direction==1?1:0);faceX-=floor(faceX/_MapSize.x)*_MapSize.x;
    float phase=direction<2?faceX*frequency+tile.y*.37:(tile.y+(direction==3?1:0))*.52+tile.x*frequency;
    float cross=.5+.13*sin(phase);float2 edge=FlatEdge(direction);
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
float FlatRiverDistance(float2 p,float4 hydro,float connections,float meander,float2 uv,out float2 tangent)
{
    tangent=hydro.gb;
    [branch] if(hydro.a<.01)return 8;
    float4 links=fmod(floor(connections/float4(1,2,4,8)),2);
    float count=dot(links,float4(1,1,1,1));
    if(count<.5)return length(p-.5)-hydro.a;
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
    float2 tile=floor(uv*_MapSize.xy);
    tile.x=tile.x-floor(tile.x/_MapSize.x)*_MapSize.x;tile.y=clamp(tile.y,0,_MapSize.y-1);
    float2 b=FlatRiverEdge(outlet,tile);float rb=radii[outlet];
    float bend=meander;
    float result=8;float2 bestTangent=tangent;
    float sourceRadius=max(.003,hydro.a*.18);
    if(count<1.5)result=FlatBezierDistance(p,float2(.5,.5),b,(float2(.5,.5)+b)*.5,sourceRadius,rb,(sourceRadius+rb)*.5,bend*.35,bestTangent);
    [loop] [fastopt] for(int d=0;d<4;d++)
    {
        if(count<1.5||d==outlet||links[d]<.5)continue;
        float2 direction=float2(1,0);
        float2 a=FlatRiverEdge(d,tile);bool straight=(d<2&&outlet<2)||(d>=2&&outlet>=2);
        float candidate=FlatBezierDistance(p,a,b,straight?(a+b)*.5:float2(.5,.5),radii[d],rb,hydro.a,bend*(straight?1:.35),direction);
        if(candidate<result){result=candidate;bestTangent=direction;}
    }
    float closest=.10,section=0;float2 faceDirection=bestTangent;
    [loop] [fastopt] for(int faceIndex=0;faceIndex<4;faceIndex++)
    {
        if(links[faceIndex]<.5)continue;
        float face=faceIndex==0?p.x:faceIndex==1?1-p.x:faceIndex==2?p.y:1-p.y;
        float2 edge=FlatRiverEdge(faceIndex,tile);
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
    tangent=bestTangent;return result;
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
float3 FlatBedColor(float2 cell)
{
    float2 base=floor(cell-.5),t=frac(cell-.5);t=t*t*(3-2*t);
    float2 uv=(base+.5)*_MapSize.zw,dx=float2(_MapSize.z,0),dy=float2(0,_MapSize.w);
    // Blend sampled colours. Interpolating material IDs would invent substrates.
    float3 a=FlatMaterialColor(floor(tex2Dlod(_GeoTex,float4(uv,0,0)).g+.5));
    float3 b=FlatMaterialColor(floor(tex2Dlod(_GeoTex,float4(uv+dx,0,0)).g+.5));
    float3 c=FlatMaterialColor(floor(tex2Dlod(_GeoTex,float4(uv+dy,0,0)).g+.5));
    float3 d=FlatMaterialColor(floor(tex2Dlod(_GeoTex,float4(uv+dx+dy,0,0)).g+.5));
    return lerp(lerp(a,b,t.x),lerp(c,d,t.x),t.y);
}
float FlatMouthDistance(float2 cell,float2 uv,float4 field,out float2 flow)
{
 float result=8;flow=float2(0,0);
 if(abs(field.g)>1.25)return result;
 float4 centre=tex2Dlod(_VisualTex,float4(uv,0,0));
 if(centre.g<=0||field.g>1.25)return result;
 [loop] for(int d=0;d<4;d++)
 {
  float2 edge=FlatRiverEdge(d,floor(uv*_MapSize.xy)),offset=(FlatEdge(d)-.5)*2;
  float2 nextUV=uv+offset*_MapSize.zw;
  if(nextUV.y<0||nextUV.y>1)continue;
  float4 h=tex2Dlod(_HydroA,float4(nextUV,0,0));
  float connections=floor(tex2Dlod(_HydroB,float4(nextUV,0,0)).a+.5);
  float opposite=d==0?2:d==1?1:d==2?8:4;
  if(h.a<.01||fmod(floor(connections/opposite),2)<.5)continue;
  float2 delta=float2(.5,.5)-edge;
  float t=saturate(dot(frac(cell)-edge,delta)/max(.0001,dot(delta,delta)));
  float radius=lerp(h.a,min(.24,h.a*1.18),t);
  float distance=length(frac(cell)-edge-delta*t)-radius;
  // Exact inlet section matches the river side of the shared face.
  float face=d==0?frac(cell.x):d==1?1-frac(cell.x):d==2?frac(cell.y):1-frac(cell.y);
  float across=abs((d<2?frac(cell.y):frac(cell.x))-(d<2?edge.y:edge.x));
  if(face<.10)distance=lerp(across-h.a,distance,smoothstep(0,.10,face));
  if(distance<result){result=distance;flow=-offset;}
 }
 return result;
}
float FlatWaterMask(float2 cell,float2 uv,float4 hydro,float4 mixing,float4 field,float aa,
                    out float signedDistance,out float2 riverFlow)
{
    float riverDistance=FlatRiverDistance(frac(cell),hydro,floor(mixing.a+.5),mixing.r,uv,riverFlow);
    float2 mouthFlow;float mouthDistance=FlatMouthDistance(cell,uv,field,mouthFlow);
    if(mouthDistance<riverDistance){riverDistance=mouthDistance;riverFlow=mouthFlow;}
    signedDistance=min(riverDistance,-field.g);
    return 1-smoothstep(-aa,aa,signedDistance);
}
float FlatPeriodicX(float x,float desiredFrequency)
{
    float periods=max(1,floor(_MapSize.x*desiredFrequency/6.2831853+.5));
    return x*6.2831853*periods/max(1,_MapSize.x);
}
#ifndef ORBIS_FLAT_NO_SWE
float3 FlatPatchContribution(sampler2D patchTexture,float4 rect,float ready,float2 cell)
{
    float2 delta=cell-rect.xy;
    delta.x-=floor(delta.x/max(1,_MapSize.x))*_MapSize.x;
    float2 uv=delta/max(float2(1,1),rect.zw);
    float inside=step(0,uv.x)*step(0,uv.y)*step(uv.x,1)*step(uv.y,1)*ready;
    float fade=smoothstep(0,.12,min(min(uv.x,uv.y),min(1-uv.x,1-uv.y)));
    float4 state=tex2D(patchTexture,saturate(uv));
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
float3 FlatWaterShade(float2 cell,float4 hydro,float4 mixing,float4 field,float3 bottom,float3 light,
                     float3 localView,float3 localLight,float waveStrength,float waterKind,float waterEdge)
{
    // WaterKind, not salinity: freshwater plumes in the sea are still ocean waves.
    float river=step(4.5,waterKind),lake=saturate(waterKind-3)*(1-river),ocean=1-river-lake;
    float2 flow=lerp(float2(.25,.13),hydro.gb,river);
    float speed=lerp(.20,.065,lake);
    float phase0=frac(_WaterClock*speed),phase1=frac(_WaterClock*speed+.5),blend=abs(phase0*2-1);
    blend=blend*blend*(3-2*blend);
    float2 geo=cell*_MapSize.zw;float lon=(geo.x-.5)*6.28318530718,lat=(geo.y-.5)*3.14159265359;
    float3 sphere=float3(cos(lat)*sin(lon),sin(lat),cos(lat)*cos(lon));
    float3 artwork=FlatDetailPosition(cell),weights=FlatDetailWeights(sphere);
    float3 artDx=ddx(artwork),artDy=ddy(artwork);
    // The callers express view/light in east, north, up coordinates. A screen
    // derivative basis rotates with the camera and gives incompatible normals.
    float3 right=float3(cos(lon),0,-sin(lon));
    float3 up=float3(-sin(lat)*sin(lon),cos(lat),-sin(lat)*cos(lon));
    float2 tangentFlow=float2(flow.x*lerp(1,max(.02,cos(lat)),river),flow.y);
    float3 drift=right*tangentFlow.x+up*tangentFlow.y;
    drift*=length(flow)/max(.0001,length(drift));
    drift*=lerp(1,.60+.45*saturate(hydro.r/4),river);
    float3 p0=(artwork-drift*phase0*3)*6;
    float3 p1=(artwork-drift*phase1*3)*6;
    float2 shoreGradient=FlatHeightGradient(field.g,cell);
    shoreGradient.x/=max(.04,cos(lat));
    float shoreDistance=field.g/max(.25,length(shoreGradient));
    float shoreVariation=.5+.5*sin(dot(artwork,float3(1.7,2.3,.9)));
    float shoreCrest=FlatCoastFoam(shoreDistance,_WaterClock,shoreVariation);
    float2 slopes=0;
    float footprint=max(length(artDx*6),length(artDy*6));
    if(_VendorWaterReady>.5&&footprint<4)
    {
        float frequency=max(1,floor(_MapSize.x*.125))/_MapSize.x;
        float3 baseUV=artwork*frequency;
        float lod=max(0,log2(max(.0001,footprint*max(_VendorOceanNormal_TexelSize.z*frequency,_VendorOceanNormal_TexelSize.w*.125)/6)));
        float2 vendorSlope;
        if(river>.5)
        {
            float streamLOD=max(0,log2(max(.0001,footprint*max(_VendorStreamNormal_TexelSize.z,_VendorStreamNormal_TexelSize.w)/24)));
            float2 stream0=FlatDetailNormal(_VendorStreamNormal,artwork*.25-drift*phase0*.35,weights,0,streamLOD,right,up);
            float2 stream1=FlatDetailNormal(_VendorStreamNormal,artwork*.25-drift*phase1*.35,weights,0,streamLOD,right,up);
            vendorSlope=lerp(stream0,stream1,blend)*.16;
        }
        else if(lake>.5)
        {
            float2 lake0=FlatDetailNormal(_VendorLakeNormal,baseUV*2-drift*phase0*.10,weights,0,lod,right,up);
            float2 lake1=FlatDetailNormal(_VendorLakeNormal,baseUV*2-drift*phase1*.10,weights,0,lod,right,up);
            float fetch=smoothstep(.02,1.8,shoreDistance);
            vendorSlope=lerp(lake0,lake1,blend)*lerp(.055,.10,fetch);
        }
        else
        {
            float2 ocean0=FlatDetailNormal(_VendorOceanNormal,baseUV-drift*phase0*.175,weights,0,lod,right,up);
            float2 ocean1=FlatDetailNormal(_VendorOceanNormal,baseUV-drift*phase1*.175,weights,0,lod,right,up);
            vendorSlope=lerp(ocean0,ocean1,blend)*.12;
        }
        slopes=vendorSlope;
    }
    else if(_VendorWaterReady<.5)
    {
        // Only retain an analytic fallback for projects missing the supplied art.
        float3 g=lerp(cos(p0)*sin(p0.yzx),cos(p1)*sin(p1.yzx),blend);
        slopes=float2(dot(g,right),dot(g,up))*.045;
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
    float nv=saturate(dot(normal,localView)),nl=saturate(dot(normal,localLight));
    float sediment=mixing.g*_ShowPlumes,fresh=mixing.b;
    float3 absorption=lerp(float3(.24,.07,.035),float3(.09,.035,.018),fresh);
    float3 scattering=lerp(float3(.005,.025,.038),float3(.004,.019,.022),fresh);
    absorption=lerp(absorption,float3(.18,.29,.46),sediment);
    scattering=lerp(scattering,float3(.45,.34,.17),sediment);
    // Reconstructed area water may overlap a river tile at a mouth or lake bank.
    // Its depth follows the basin bed rather than the tile's narrow channel.
    float depth=max(.15,lerp(field.b,hydro.r,river));
    if(river<.5)depth=lerp(.15,depth,smoothstep(0,1.5,field.g));
    if(river>.5)
    {
        float section=saturate(-waterEdge/max(.01,hydro.a));
        depth=max(.12,depth*section*(2-section));
    }
    if(_FieldReady>.5)bottom=FlatBedColor(cell)*.72;
    // Stable bed grain remains visible through shallow clear water.
    bottom*=.94+.06*sin(dot(artwork,float3(7.1,4.3,5.7)))*sin(dot(artwork,float3(-3.9,8.1,2.3)));
    float refractedCos=sqrt(max(.1,1-(1-nv*nv)/(1.333*1.333)));
    float3 transmission;
    float3 scatteringLight=OrbisHPScatter(light,absorption,scattering,depth/refractedCos,transmission);
    float fresnel=OrbisHPSchlick(nv);
    float3 sky=light*float3(.34,.48,.58);
    float3 result=(bottom*light*transmission+scatteringLight)*(1-fresnel)+sky*fresnel;
    // Physically, 80m and 2200m are both optically opaque. Optional cartographic
    // bathymetry makes the true data depth readable; it is NOT deep-water transparency.
    float logDepth=log2(1+depth);
    float3 depthColor=lerp(FlatColor(float3(.24,.61,.63)),FlatColor(float3(.08,.36,.55)),smoothstep(1,9.23,logDepth));
    depthColor=lerp(depthColor,FlatColor(float3(.025,.13,.30)),smoothstep(9.23,11.97,logDepth));
    depthColor=lerp(depthColor,FlatColor(float3(.26,.48,.47)),fresh*.22);
    depthColor=lerp(depthColor,FlatColor(float3(.53,.47,.30)),sediment*.70);
    float depthRelief=FlatFieldRelief(cell,1,float3(-.45,.6,.75));
    float chartDepth=lerp(.70,.12,saturate(lake+river));
    result=lerp(result,depthColor*light*depthRelief,saturate(_Bathymetry)*chartDepth);
    float3 halfDir=normalize(localView+localLight+float3(0,0,1e-5));
    float spec=pow(saturate(dot(normal,halfDir)),90);
    result+=light*spec*nl*.14*waveStrength;
    float foam=localFoam*waveStrength;
    if(_VendorWaterReady<.5)
    {
        foam=max(foam,shoreCrest*(1-river)*lerp(.20,.04,lake)*waveStrength);
    }
    if(_VendorWaterReady>.5&&footprint<4)
    {
        // SW3 foam artwork follows the same two-phase flow as its normal layers.
        float3 foamUV=artwork*.25;
        float foamLOD=max(0,log2(max(1,footprint*16)));
        float a,b;
        if(river>.5)
        {
            a=FlatDetailTexture(_VendorRiverFoam,foamUV-drift*phase0*.35,weights,0,foamLOD);
            b=FlatDetailTexture(_VendorRiverFoam,foamUV-drift*phase1*.35,weights,0,foamLOD);
        }
        else
        {
            a=FlatDetailTexture(_VendorOceanFoam,foamUV-drift*phase0*.15,weights,0,foamLOD);
            b=FlatDetailTexture(_VendorOceanFoam,foamUV-drift*phase1*.15,weights,0,foamLOD);
        }
        float pattern=smoothstep(.3,.8,lerp(a,b,blend));
        float brokenCrest=saturate(length(slopes)*4-.15)*ocean;
        float riverBank=1-smoothstep(max(.003,hydro.a*.12),max(.01,hydro.a*.70),-waterEdge);
        float riverFoam=river*(.025+.12*riverBank)*saturate(length(flow))/(1+depth*.25);
        foam+=pattern*(shoreCrest*lerp(.34,.045,lake)*(1-river)+brokenCrest*.18+riverFoam)*waveStrength*(1-smoothstep(1,4,footprint));
    }
    return lerp(result,light*FlatColor(float3(.82,.90,.86)),saturate(foam));
}
#endif
