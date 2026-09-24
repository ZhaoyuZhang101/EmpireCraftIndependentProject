#version 300 es
precision highp float;
precision highp int;
#define UNITY_COLORSPACE_GAMMA
in vec2 vUV;
out vec4 targetColor;
struct v2f_img {vec2 uv;};
vec3 GammaToLinearSpace(vec3 s){return s*(s*(s*.305306011+.682171111)+.012522878);}
float saturate(float x){return clamp(x,0.0,1.0);}
vec2 saturate(vec2 x){return clamp(x,0.0,1.0);}
vec3 saturate(vec3 x){return clamp(x,0.0,1.0);}
vec4 saturate(vec4 x){return clamp(x,0.0,1.0);}
vec4 hlslTextureLod(sampler2D s,vec4 uv){return textureLod(s,uv.xy,uv.w);}
void OrbisPatchClip(vec3 p,vec2 q){}



   // Engine transform/clip omitted in the fragment-only probe.
   #ifndef ORBIS_FLAT_WATER_INCLUDED
#define ORBIS_FLAT_WATER_INCLUDED
// SPDX-License-Identifier: MPL-2.0
// Adapted from AshenOneArt / HPWater, HPWaterVolumetrics.hlsl,
// CaculateScatteredLight and FresnelSchlick; source retrieved 2026.0-09.0-12.
// https://github.com/AshenOneArt/HPWater
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// ORBIS modifications: componentwise safe extinction, bounded path,
// no HDRP dependencies, homogeneous optical segment rather than ray marching.
#ifndef ORBIS_HP_WATER_OPTICS
#define ORBIS_HP_WATER_OPTICS
vec3 OrbisHPScatter(vec3 incident, vec3 absorption, vec3 scattering,
                      float pathMeters, out vec3 transmission)
{
    vec3 extinction=max(vec3(1e-6,1e-6,1e-6),absorption+scattering);
    transmission=exp(-extinction*max(0.0,pathMeters));
    return incident*(1.0-transmission)*max(scattering,0.0)/extinction;
}
float OrbisHPSchlick(float cosine)
{
    float facing=1.0-saturate(cosine);
    return .02037+.97963*facing*facing*facing*facing*facing;
}
#endif

#ifndef ORBIS_FLAT_SURFACE_FIELD
#define ORBIS_FLAT_SURFACE_FIELD
uniform sampler2D _VisualTex;
uniform vec4 _MapSize;
uniform float _FieldReady,_SmoothTerrain,_Vegetation,_Bathymetry,_Relief;
vec4 FlatMinmod(vec4 a,vec4 b)
{
    return .5*(sign(a)+sign(b))*min(abs(a),abs(b));
}
vec4 FlatCubic(vec4 p0,vec4 p1,vec4 p2,vec4 p3,float t)
{
    vec4 m1=FlatMinmod(p1-p0,p2-p1),m2=FlatMinmod(p2-p1,p3-p2);
    float t2=t*t,t3=t2*t;
    return (2.0*t3-3.0*t2+1.0)*p1+(t3-2.0*t2+t)*m1+(-2.0*t3+3.0*t2)*p2+(t3-t2)*m2;
}
vec4 FlatFieldRow(vec2 centre,float t)
{
    vec2 dx=vec2(_MapSize.z,0.0);
    return FlatCubic(hlslTextureLod(_VisualTex,vec4(centre-dx,0.0,0.0)),hlslTextureLod(_VisualTex,vec4(centre,0.0,0.0)),
                     hlslTextureLod(_VisualTex,vec4(centre+dx,0.0,0.0)),hlslTextureLod(_VisualTex,vec4(centre+dx*2.0,0.0,0.0)),t);
}
// R metres; G signed Euclidean shoreline (positive in area water); B metres depth;
// A climate-derived canopy density. No biome/owner/material ID is interpolated.
vec4 FlatField(vec2 cell)
{
    vec2 p=cell-.5,baseCell=floor(p),t=fract(p),centre=(baseCell+.5)*_MapSize.zw,dy=vec2(0.0,_MapSize.w);
    return FlatCubic(FlatFieldRow(centre-dy,t.x),FlatFieldRow(centre,t.x),
                     FlatFieldRow(centre+dy,t.x),FlatFieldRow(centre+dy*2.0,t.x),t.y);
}
vec2 FlatBlendUV(vec2 cell)
{
    vec2 p=cell-.5,t=fract(p);t=t*t*(3.0-2.0*t);
    return (floor(p)+.5+t)*_MapSize.zw;
}
vec3 FlatColor(vec3 srgb)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return srgb;
#else
    return GammaToLinearSpace(srgb);
#endif
}
vec3 FlatRadial(vec2 uv)
{
    float lon=(uv.x-.5)*6.283185307,lat=(uv.y-.5)*3.141592654,c=cos(lat);
    return vec3(sin(lon)*c,sin(lat),cos(lon)*c);
}
float FlatHash(vec2 p)
{
    // Integer number of canopy cells around the complete map.
    p.x=mod(p.x+_MapSize.x*4.0,_MapSize.x*2.0);
    return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);
}
vec2 FlatHeightGradient(float h,vec2 cell)
{
    vec2 dx=dFdx(cell),dy=dFdy(cell);
    dx.x-=round(dx.x/max(1.0,_MapSize.x))*_MapSize.x;dy.x-=round(dy.x/max(1.0,_MapSize.x))*_MapSize.x;
    float det=dx.x*dy.y-dx.y*dy.x;
    float safeDet=(det<0.0?-1.0:1.0)*max(abs(det),1e-8);
    return clamp(vec2(dFdx(h)*dy.y-dFdy(h)*dx.y,dFdy(h)*dx.x-dFdx(h)*dy.x)/safeDet,-4000.0,4000.0);
}
float FlatRelief(float h,vec2 cell,vec3 localLight)
{
    vec2 g=FlatHeightGradient(h,cell);
    vec3 n=normalize(vec3(-g/450.0,1.0));
    float shade=.64+.48*saturate(dot(n,normalize(localLight)));
    return mix(1.0,clamp(shade,.55,1.12),_Relief);
}
// Shading uses a wider reconstruction stencil than contour placement. This
// avoids a checkerboard of derivative lobes on quantised 100.0m authoring terraces.
float FlatFieldRelief(vec2 cell,float useDepth,vec3 localLight)
{
    if(_FieldReady<.5||_Relief<.01)return 1.0;
    vec2 uv=cell*_MapSize.zw,dx=vec2(_MapSize.z,0.0),dy=vec2(0.0,_MapSize.w);
    vec4 west=hlslTextureLod(_VisualTex,vec4(uv-dx,0.0,0.0)),east=hlslTextureLod(_VisualTex,vec4(uv+dx,0.0,0.0));
    vec4 south=hlslTextureLod(_VisualTex,vec4(uv-dy,0.0,0.0)),north=hlslTextureLod(_VisualTex,vec4(uv+dy,0.0,0.0));
    vec2 gradient=vec2(east.r-west.r,north.r-south.r)*.5;
    if(useDepth>.5)gradient=vec2(log2(1.0+max(0.0,east.b))-log2(1.0+max(0.0,west.b)),log2(1.0+max(0.0,north.b))-log2(1.0+max(0.0,south.b)))*37.5;
    vec3 normal=normalize(vec3(-gradient/450.0,1.0));
    return mix(1.0,clamp(.64+.48*saturate(dot(normal,normalize(localLight))),.55,1.12),_Relief);
}
vec3 FlatVegetation(vec3 col,vec2 cell,float canopy,float pixelsPerTile)
{
    if(_Vegetation<.5)return col; // Uniform branch: no divergent derivative lanes.
    float detailFade=smoothstep(3.0,9.0,pixelsPerTile)*_Vegetation;
    vec2 p=cell*2.0,unitId=floor(p),q=fract(p)-.5;
    float seed=FlatHash(unitId),seed2=FlatHash(unitId+vec2(71.0,19.0));
    vec2 centre=(vec2(seed,seed2)-.5)*.45;
    float radius=mix(.12,.27,seed2),distanceToCrown=length((q-centre)*vec2(1.0,1.14));
    float aa=max(fwidth(distanceToCrown),.015);
    float occupied=1.0-step(saturate((canopy-.24)*1.5),seed);
    float crown=(1.0-smoothstep(radius-aa,radius+aa,distanceToCrown))*occupied;
    float shadow=(1.0-smoothstep(radius-aa,radius+aa,length((q-centre-vec2(.075,-.09))*vec2(1.0,1.14))))*occupied;
    float crownLight=mix(.70,1.23,saturate(.55+dot(q-centre,vec2(-1.4,1.8))));
    col*=1.0-shadow*.16*detailFade;
    col=mix(col,col*crownLight,crown*detailFade);
    vec3 radial=FlatRadial(cell*_MapSize.zw);
    float grain=.5+.5*sin(radial.x*970.0+sin(radial.y*650.0))*sin(radial.z*1170.0);
    float footprint=max(length(dFdx(radial*1170.0)),length(dFdy(radial*1170.0)));
    float grainFade=(1.0-smoothstep(.6,2.2,footprint))*_Vegetation;
    return col*mix(1.0,.95+.10*grain,grainFade);
}
float FlatContour(float metres,float interval,out float major)
{
    float pixelHeight=fwidth(metres),baseInterval=max(25.0,interval);
    float multiplier=exp2(ceil(log2(max(1.0,pixelHeight*5.0/baseInterval))));
    float scaled=metres/(baseInterval*multiplier),aa=max(fwidth(scaled),1e-6);
    float distanceToContour=abs(fract(scaled+.5)-.5);
    float nearest=floor(scaled+.5);
    major=1.0-step(.01,abs(fract(nearest*.2+.5)-.5));
    return (1.0-smoothstep(aa*.25,aa*.85,distanceToContour))*smoothstep(.001,.02,pixelHeight);
}
#endif

uniform sampler2D _HydroA,_HydroB,_GeoTex;
#ifndef ORBIS_FLAT_NO_SWE
uniform sampler2D _SWEState,_SWEPreviousState;
#endif
uniform vec4 _SWERect,_SWEPreviousRect;
uniform float _SWEPreviousReady,_SWEBlend;
uniform float _SWEReady,_WaterClock,_ShowBanks,_ShowWater,_ShowPlumes,_WaveAmount;
vec2 FlatEdge(float direction)
{
    return direction==0.0?vec2(0.0,.5):direction==1.0?vec2(1.0,.5):direction==2.0?vec2(.5,0.0):vec2(.5,1.0);
}
float FlatBezierDistance(vec2 p,vec2 a,vec2 b,vec2 c,float ra,float rb,float rc,out vec2 tangent)
{
    float result=8.0;vec2 previous=a;tangent=normalize(b-a+vec2(1e-7,0.0));float previousRadius=ra;
    // Small fixed loop with NO texture sampling. Do not unroll a ray marcher here.
     for(float k=1.0;k<=8.0;k++)
    {
        float t=k*.125,s=1.0-t;
        vec2 q=s*s*a+2.0*s*t*c+t*t*b;
        float radius=s*s*ra+2.0*s*t*rc+t*t*rb;
        vec2 delta=q-previous;float den=dot(delta,delta);
        float along=saturate(dot(p-previous,delta)/max(den,1e-10));
        float candidate=length(p-previous-along*delta)-mix(previousRadius,radius,along);
        if(candidate<result){result=candidate;tangent=delta*inversesqrt(max(den,1e-10));}
        previous=q;previousRadius=radius;
    }
    return result;
}
float FlatRiverDistance(vec2 p,vec4 hydro,float connections,vec2 uv,out vec2 tangent)
{
    tangent=hydro.gb;
     if(hydro.a<.01)return 8.0;
    vec4 links=mod(floor(connections/vec4(1.0,2.0,4.0,8.0)),2.0);
    float count=dot(links,vec4(1.0,1.0,1.0,1.0));
    if(count<.5)return length(p-.5)-hydro.a;
    float outlet=0.0;float best=-2.0;
    if(links.x>.5){best=-hydro.g;outlet=0.0;}
    if(links.y>.5&&hydro.g>best){best=hydro.g;outlet=1.0;}
    if(links.z>.5&&-hydro.b>best){best=-hydro.b;outlet=2.0;}
    if(links.w>.5&&hydro.b>best){best=hydro.b;outlet=3.0;}
    vec4 neighborWidths=vec4(hlslTextureLod(_HydroA,vec4(uv-vec2(_MapSize.z,0.0),0.0,0.0)).a,
        hlslTextureLod(_HydroA,vec4(uv+vec2(_MapSize.z,0.0),0.0,0.0)).a,
        hlslTextureLod(_HydroA,vec4(uv-vec2(0.0,_MapSize.w),0.0,0.0)).a,
        hlslTextureLod(_HydroA,vec4(uv+vec2(0.0,_MapSize.w),0.0,0.0)).a);
    vec4 radii=mix(hydro.aaaa,(neighborWidths+hydro.a)*.5,step(.01,neighborWidths));
    vec2 b=FlatEdge(outlet);float rb=radii[int(outlet)];
    float result=8.0;vec2 bestTangent=tangent;
    if(count<1.5)result=FlatBezierDistance(p,vec2(.5,.5),b,(vec2(.5,.5)+b)*.5,hydro.a,rb,(hydro.a+rb)*.5,bestTangent);
     for(float d=0.0;d<4.0;d++)
    {
        if(count<1.5||d==outlet||links[int(d)]<.5)continue;
        vec2 direction;
        float candidate=FlatBezierDistance(p,FlatEdge(d),b,vec2(.5,.5),radii[int(d)],rb,hydro.a,direction);
        if(candidate<result){result=candidate;bestTangent=direction;}
    }
     for(float faceIndex=0.0;faceIndex<4.0;faceIndex++)
    {
        if(links[int(faceIndex)]<.5)continue;
        float face=faceIndex==0.0?p.x:faceIndex==1.0?1.0-p.x:faceIndex==2.0?p.y:1.0-p.y;
        float across=abs((faceIndex<2.0?p.y:p.x)-.5);
        if(face<.10&&across<.45)
        {
            float weight=smoothstep(0.0,.10,face);
            result=mix(across-radii[int(faceIndex)],result,weight);
            vec2 direction=(FlatEdge(faceIndex)-.5)*2.0*(faceIndex==outlet?1.0:-1.0);
            bestTangent=normalize(mix(direction,bestTangent,weight)+vec2(1e-7,0.0));
        }
    }
    tangent=bestTangent;return result;
}
vec3 FlatMaterialColor(float code)
{
    // Material IDs MUST be sampled at the authoritative texel centre.
    if(code<.5)return FlatColor(vec3(.61,.50,.36));
    if(code<1.5)return FlatColor(vec3(.83,.74,.54));
    if(code<2.5)return FlatColor(vec3(.55,.47,.34));
    if(code<3.5)return FlatColor(vec3(.62,.63,.57));
    if(code<4.5)return FlatColor(vec3(.54,.55,.54));
    return FlatColor(vec3(.87,.91,.88));
}
float FlatMouthDistance(vec2 cell,vec2 uv,vec4 field,out vec2 flow)
{
 float result=8.0;flow=vec2(0.0,0.0);
 if(abs(field.g)>1.25)return result;
 vec4 centre=hlslTextureLod(_VisualTex,vec4(uv,0.0,0.0));
 if(centre.g<=0.0||field.g>1.25)return result;
  for(float d=0.0;d<4.0;d++)
 {
  vec2 edge=FlatEdge(d),offset=(edge-.5)*2.0;
  vec2 nextUV=uv+offset*_MapSize.zw;
  if(nextUV.y<0.0||nextUV.y>1.0)continue;
  vec4 h=hlslTextureLod(_HydroA,vec4(nextUV,0.0,0.0));
  float connections=floor(hlslTextureLod(_HydroB,vec4(nextUV,0.0,0.0)).a+.5);
  float opposite=d==0.0?2.0:d==1.0?1.0:d==2.0?8.0:4.0;
  if(h.a<.01||mod(floor(connections/opposite),2.0)<.5)continue;
  vec2 delta=vec2(.5,.5)-edge;
  float t=saturate(dot(fract(cell)-edge,delta)/max(.0001,dot(delta,delta)));
  float radius=mix(h.a,min(.46,h.a*1.45),t);
  float distance=length(fract(cell)-edge-delta*t)-radius;
  // Exact inlet section matches the river side of the shared face.
  float face=d==0.0?fract(cell.x):d==1.0?1.0-fract(cell.x):d==2.0?fract(cell.y):1.0-fract(cell.y);
  float across=abs((d<2.0?fract(cell.y):fract(cell.x))-.5);
  if(face<.10&&across<.45)distance=mix(across-h.a,distance,smoothstep(0.0,.10,face));
  if(distance<result){result=distance;flow=-offset;}
 }
 return result;
}
float FlatWaterMask(vec2 cell,vec2 uv,vec4 hydro,vec4 mixing,vec4 field,float aa,
                    out float signedDistance,out vec2 riverFlow)
{
    float riverDistance=FlatRiverDistance(fract(cell),hydro,floor(mixing.a+.5),uv,riverFlow);
    vec2 mouthFlow;float mouthDistance=FlatMouthDistance(cell,uv,field,mouthFlow);
    if(mouthDistance<riverDistance){riverDistance=mouthDistance;riverFlow=mouthFlow;}
    signedDistance=min(riverDistance,-field.g);
    return 1.0-smoothstep(-aa,aa,signedDistance);
}
float FlatPeriodicX(float x,float desiredFrequency)
{
    float periods=max(1.0,floor(_MapSize.x*desiredFrequency/6.2831853+.5));
    return x*6.2831853*periods/max(1.0,_MapSize.x);
}
#ifndef ORBIS_FLAT_NO_SWE
vec3 FlatPatchContribution(sampler2D patchTexture,vec4 rect,float ready,vec2 cell)
{
    vec2 delta=cell-rect.xy;
    delta.x-=floor(delta.x/max(1.0,_MapSize.x))*_MapSize.x;
    vec2 uv=delta/max(vec2(1.0,1.0),rect.zw);
    float inside=step(0.0,uv.x)*step(0.0,uv.y)*step(uv.x,1.0)*step(uv.y,1.0)*ready;
    float fade=smoothstep(0.0,.12,min(min(uv.x,uv.y),min(1.0-uv.x,1.0-uv.y)));
    vec4 state=texture(patchTexture,saturate(uv));
    return vec3((state.rg*255.0-128.0)/127.0*.18*state.a,state.b*.35)*inside*fade;
}
#endif
vec3 FlatWaterShade(vec2 cell,vec4 hydro,vec4 mixing,vec4 field,vec3 bottom,vec3 light,
                     vec3 localView,vec3 localLight,float waveStrength)
{
    vec2 flow=hydro.gb;
    float phase0=fract(_WaterClock*.20),phase1=fract(_WaterClock*.20+.5),blend=abs(phase0*2.0-1.0);
    blend=blend*blend*(3.0-2.0*blend);
    vec2 p0=vec2(FlatPeriodicX(cell.x-flow.x*phase0*3.0,6.0),(cell.y-flow.y*phase0*3.0)*6.0);
    vec2 p1=vec2(FlatPeriodicX(cell.x-flow.x*phase1*3.0,6.0),(cell.y-flow.y*phase1*3.0)*6.0);
    vec2 slope0=vec2(cos(p0.x)*sin(p0.y*.83),sin(p0.x)*cos(p0.y));
    vec2 slope1=vec2(cos(p1.x)*sin(p1.y*.83),sin(p1.x)*cos(p1.y));
    vec2 slopes=mix(slope0,slope1,blend)*.045;
    slopes+=vec2(sin(FlatPeriodicX(cell.x,2.1)+cell.y*1.3-_WaterClock*1.8),cos(FlatPeriodicX(cell.x,1.2)-cell.y*1.8-_WaterClock*1.6))*.025;
    vec2 cellDx=dFdx(cell),cellDy=dFdy(cell);
    cellDx.x-=floor(cellDx.x/_MapSize.x+.5)*_MapSize.x;
    cellDy.x-=floor(cellDy.x/_MapSize.x+.5)*_MapSize.x;
    float footprint=max(length(cellDx*6.0),length(cellDy*6.0));
    slopes*=1.0-smoothstep(1.0,4.0,footprint);
    float localFoam=0.0;
#ifndef ORBIS_FLAT_NO_SWE
    vec3 localContribution=FlatPatchContribution(_SWEState,_SWERect,_SWEReady,cell);
    if(_SWEBlend<1.0)
        localContribution=mix(FlatPatchContribution(_SWEPreviousState,_SWEPreviousRect,_SWEPreviousReady,cell),localContribution,smoothstep(0.0,1.0,_SWEBlend));
    slopes+=localContribution.xy;localFoam=localContribution.z;
#endif
    vec3 normal=normalize(vec3(-slopes*waveStrength,1.0));
    float nv=saturate(dot(normal,localView)),nl=saturate(dot(normal,localLight));
    float sediment=mixing.g*_ShowPlumes,fresh=mixing.b;
    vec3 absorption=mix(vec3(.24,.07,.035),vec3(.13,.065,.033),fresh);
    vec3 scattering=mix(vec3(.005,.025,.038),vec3(.018,.042,.047),fresh);
    absorption=mix(absorption,vec3(.18,.29,.46),sediment);
    scattering=mix(scattering,vec3(.45,.34,.17),sediment);
    float depth=hydro.a>.01?max(.15,hydro.r):max(.15,field.b);
    if(hydro.a<.01)depth=mix(.15,depth,smoothstep(0.0,1.5,field.g));
    float refractedCos=sqrt(max(.1,1.0-(1.0-nv*nv)/(1.333*1.333)));
    vec3 transmission;
    vec3 scatteringLight=OrbisHPScatter(light,absorption,scattering,depth/refractedCos,transmission);
    float fresnel=OrbisHPSchlick(nv);
    vec3 sky=light*vec3(.34,.48,.58);
    vec3 result=(bottom*light*transmission+scatteringLight)*(1.0-fresnel)+sky*fresnel;
    // Physically, 80.0m and 2200.0m are both optically opaque. Optional cartographic
    // bathymetry makes the true data depth readable; it is NOT deep-water transparency.
    float logDepth=log2(1.0+depth);
    vec3 depthColor=mix(FlatColor(vec3(.24,.61,.63)),FlatColor(vec3(.08,.36,.55)),smoothstep(1.0,9.23,logDepth));
    depthColor=mix(depthColor,FlatColor(vec3(.025,.13,.30)),smoothstep(9.23,11.97,logDepth));
    depthColor=mix(depthColor,FlatColor(vec3(.26,.48,.47)),fresh*.22);
    depthColor=mix(depthColor,FlatColor(vec3(.53,.47,.30)),sediment*.70);
    float depthRelief=FlatFieldRelief(cell,1.0,vec3(-.45,.6,.75));
    result=mix(result,depthColor*light*depthRelief,saturate(_Bathymetry)*.70);
    vec3 halfDir=normalize(localView+localLight+vec3(0.0,0.0,1e-5));
    float spec=pow(saturate(dot(normal,halfDir)),90.0);
    result+=light*spec*nl*.14*waveStrength;
    float crestPhase=field.g*8.0-_WaterClock*2.1+sin(FlatPeriodicX(cell.x,1.7)+cell.y*2.3);
    float crest=smoothstep(.70,.98,.5+.5*sin(crestPhase));
    float surf=(1.0-smoothstep(.05,.75,field.g))*step(0.0,field.g)*(1.0-step(.01,hydro.a))*crest*.20;
    float foam=max(surf,localFoam)*waveStrength;
    return mix(result,light*FlatColor(vec3(.82,.90,.86)),saturate(foam));
}
#endif

   #ifndef ORBIS_FLAT_SKY_COMPOSITE
#define ORBIS_FLAT_SKY_COMPOSITE
uniform sampler2D _SkyTex;
uniform vec3 _FlatSun;
uniform float _SkyReady,_FlatClouds,_FlatCloudShadows,_FlatAtmosphere,_FlatDayNight,_CloudOpacity,_FlatRain,_FlatLightning,_FlatNightLights;
uniform float _SkyClock,_FlatMoon;
float FlatDayWeight(vec3 radial)
{ return mix(1.0,smoothstep(-.13,.18,dot(radial,normalize(_FlatSun+vec3(0.0,1e-7,0.0)))),_FlatDayNight); }
vec3 FlatDayLight(vec3 radial,vec2 uv)
{
    float day=FlatDayWeight(radial);
    float lon=(uv.x-.5)*6.283185307,lat=(uv.y-.5)*3.141592654;
    vec3 east=vec3(cos(lon),0.0,-sin(lon));
    vec3 north=vec3(-sin(lat)*sin(lon),cos(lat),-sin(lat)*cos(lon));
    float altitude=.018/max(.22,dot(radial,_FlatSun));
    vec2 shadowUV=uv+vec2(dot(east,_FlatSun)/(6.283185307*max(.12,cos(lat))),dot(north,_FlatSun)/3.141592654)*altitude;
    vec4 cloud=texture(_SkyTex,shadowUV);
    float shadow=1.0-saturate(cloud.r*.38+cloud.g*.16)*_FlatCloudShadows*_SkyReady;
    // Desaturated sky + illustrative moon phase. Never absolute black on the night side.
    vec3 nightLight=vec3(.085,.115,.16)+vec3(.065,.068,.075)*_FlatMoon;
    return mix(nightLight,vec3(.94,.97,1.0)*shadow,day);
}
vec3 FlatSkyComposite(vec3 col,vec2 uv,vec2 cell,float waterMask,float pixelsPerTile)
{
    vec3 radial=FlatRadial(uv);float day=FlatDayWeight(radial);
    vec4 cloud=texture(_SkyTex,uv);
    float atmosphereAmount=_FlatAtmosphere*mix(.045,.015,smoothstep(2.0,16.0,pixelsPerTile));
    vec3 haze=mix(FlatColor(vec3(.13,.19,.28)),FlatColor(vec3(.53,.67,.77)),day);
    col=mix(col,haze,atmosphereAmount);
    float dusk=exp(-abs(dot(radial,_FlatSun))/.065)*_FlatDayNight*_FlatAtmosphere;
    col+=FlatColor(vec3(.36,.20,.12))*dusk*.025;
    float low=cloud.r,high=cloud.g;
    float alpha=(1.0-(1.0-low*.82)*(1.0-high*.35))*_CloudOpacity*_FlatClouds*_SkyReady;
    vec3 cloudColor=mix(FlatColor(vec3(.22,.29,.41)),FlatColor(vec3(.90,.94,.97)),day);
    cloudColor*=mix(.74,1.0,cloud.b);
    col=mix(col,cloudColor,saturate(alpha));
    float rain=cloud.a*_FlatRain*_SkyReady*smoothstep(3.0,8.0,pixelsPerTile);
    vec2 drop=cell*vec2(17.0,4.0)+vec2(_SkyClock*.45,_SkyClock*8.0);
    float streak=(1.0-smoothstep(.035,.12,abs(fract(drop.x)-.5)))*smoothstep(.45,.95,fract(drop.y));
    col=mix(col,haze,streak*rain*.20);
    // Infrequent local decorative flashes, not simulated instantaneous precipitation.
    float eventId=floor(_SkyClock/9.0),phase=fract(_SkyClock/9.0);
    float flash=(1.0-smoothstep(.003,.016,phase))*step(.76,cloud.a)*_FlatLightning*_SkyReady;
    float spot=step(.91,FlatHash(floor(cell/4.0)+eventId*vec2(13.0,7.0)));
    col+=flash*spot*vec3(.18,.20,.24);
    return col;
}
#endif

   uniform sampler2D _MainTex,_OwnerTex,_InfoTex;
   uniform vec4 _Selected;
   uniform float _Grid,_PixelsPerTile,_Natural,_Political,_WaterView,_ShowContours,_ContourInterval,_DrawHeight;
   struct appdata {vec4 vertex;vec2 uv;};
   struct v2f {vec4 pos;vec2 uv;};
   
   #ifndef ORBIS_FLAT_PIXEL_DETAIL
#define ORBIS_FLAT_PIXEL_DETAIL
// Presentation only: 32.0 artwork pixels per authoritative Tile. No new simulation cells.
uniform float _DetailWeight;
float PDBox(vec2 p,vec2 lo,vec2 hi)
{return step(lo.x,p.x)*step(lo.y,p.y)*(1.0-step(hi.x,p.x))*(1.0-step(hi.y,p.y));}
float PDHash(vec2 p)
{
 p.x=p.x-floor(p.x/_MapSize.x)*_MapSize.x;
 return fract(sin(dot(p,vec2(12.9898,78.233)))*43758.5453);
}
vec3 PDPaint(vec3 col,vec3 ink,float mask){return mix(col,FlatColor(ink),saturate(mask));}
vec3 PixelGround(vec3 baseColor,vec2 cell,vec4 field,float waterMask)
{
 vec2 id=floor(cell),q=floor(fract(cell)*32.0),uv=(id+.5)*_MapSize.zw;
 float rand=PDHash(id),grain=PDHash(id+floor(q/3.0)*.013);
 vec3 col=floor(baseColor*28.0+.5)/28.0*(.90+.17*grain);
 float green=step(.26,field.a)*step(0.0,field.r);
 float blades=step(.88,grain)*step(1.0,mod(q.y,5.0))*step(mod(q.x,5.0),1.0);
 col=PDPaint(col,vec3(.35,.43,.19),blades*green*.4);
 // Continuous height terraces: no cell-edge bars or needles. Only steep,
 // south-facing slopes expose a rock face; water later cuts through the face.
 vec2 dx=vec2(_MapSize.z,0.0),dy=vec2(0.0,_MapSize.w),at=cell*_MapSize.zw;
 float gx=(hlslTextureLod(_VisualTex,vec4(at+dx,0.0,0.0)).r-hlslTextureLod(_VisualTex,vec4(at-dx,0.0,0.0)).r)*.5;
 float gy=(hlslTextureLod(_VisualTex,vec4(at+dy,0.0,0.0)).r-hlslTextureLod(_VisualTex,vec4(at-dy,0.0,0.0)).r)*.5;
 float slope=length(vec2(gx,gy));
 float band=fract(max(0.0,field.r)/180.0);
 float face=smoothstep(110.0,230.0,slope)*smoothstep(35.0,110.0,gy)*(1.0-smoothstep(.10,.27,band));
 face*=step(.01,field.r)*(1.0-waterMask)*step(.20,-field.g);
 float strata=step(.42,PDHash(floor(cell*vec2(16.0,32.0))));
 vec3 rock=mix(vec3(.31,.29,.23),vec3(.48,.44,.34),strata*.5+band);
 col=PDPaint(col,rock,face*.85);
 float shore=step(-.16,field.g)*(1.0-step(.22,field.g))*(1.0-waterMask);
 col=PDPaint(col,mix(vec3(.47,.36,.21),vec3(.66,.53,.30),step(.45,grain)),shore*.8);
 return col;
}
vec3 PixelProps(vec3 col,vec2 cell,vec4 field,float waterMask,float structure,vec3 light)
{
 vec2 id=floor(cell),q=floor(fract(cell)*32.0);
  for(float row=0.0;row>=-1.0;row--)
 {
  vec2 treeId=id+vec2(0.0,row),rootUV=(treeId+vec2(.5,.25))*_MapSize.zw;
  if(treeId.y<0.0||treeId.y>=_MapSize.y)continue;
  vec2 centreUV=(treeId+.5)*_MapSize.zw;
  vec4 rootField=hlslTextureLod(_VisualTex,vec4(rootUV,0.0,0.0));
  vec4 rootHydro=hlslTextureLod(_HydroA,vec4(centreUV,0.0,0.0));
  float links=hlslTextureLod(_HydroB,vec4(centreUV,0.0,0.0)).a;
  vec4 rootInfo=hlslTextureLod(_InfoTex,vec4(centreUV,0.0,0.0))*255.0;
  float rootStructure=rootInfo.b;
  float seed=PDHash(treeId);
  vec2 rootFlow;
  float rootRiver=FlatRiverDistance(vec2(.5,.25),rootHydro,floor(links+.5),centreUV,rootFlow);
  float occupied=step(seed,saturate((rootField.a-.20)*1.25))*step(.18,-rootField.g)*step(.15,rootRiver);
  if(rootStructure>.5||occupied<.5||(rootInfo.g>.5&&rootInfo.g<4.5))continue;
  vec2 tq=floor((cell-treeId)*32.0);
  vec2 crown=tq-vec2(13.0+floor(seed*7.0),20.0+floor(PDHash(treeId+71.0)*6.0));
  float size=.86+.15*PDHash(treeId+vec2(29.0,13.0));
  vec2 leafPoint=crown/size;
  // Four overlapping leaf clusters give a scalloped outline at the 32.0px art grid.
  float shape=min(length((leafPoint-vec2(-4.0,-1.0))*vec2(1.0,1.08))-7.0,
                  length((leafPoint-vec2(4.0,0.0))*vec2(1.0,1.10))-7.0);
  shape=min(shape,length((leafPoint-vec2(-1.0,5.0))*vec2(1.04,1.0))-7.0);
  shape=min(shape,length((leafPoint-vec2(0.0,-4.0))*vec2(1.0,.9))-7.0);
  float crownMask=1.0-step(.1,shape),inner=1.0-step(-1.2,shape);
  float shadow=PDBox(tq,vec2(8.0,4.0),vec2(29.0,15.0));col*=1.0-shadow*.19;
  float trunk=PDBox(tq,vec2(14.0,5.0),vec2(18.0,17.0));
  col=PDPaint(col,vec3(.23,.16,.105)*light,trunk);
  col=PDPaint(col,vec3(.44,.30,.16)*light,PDBox(tq,vec2(15.0,6.0),vec2(17.0,16.0)));
  float cluster=PDHash(treeId+floor(tq/3.0)*.07);
  float sunward=saturate(.48+dot(leafPoint,vec2(-.035,.045)));
  vec3 dark=mix(vec3(.10,.20,.10),vec3(.13,.23,.14),seed);
  vec3 bright=mix(vec3(.34,.45,.17),vec3(.27,.43,.22),seed);
  vec3 foliage=mix(dark,bright,floor(sunward*4.0)/4.0)*(.88+.20*step(.38,cluster));
  // Continental cold climates use layered conifer silhouettes.
  float conifer=step(16.5,rootInfo.a)*step(rootInfo.a,28.5);
  if(conifer>.5)
  {
   float coniferEdgeDistance=max(abs(leafPoint.x)-min(10.5,max(0.0,8.0-leafPoint.y*.35)),abs(leafPoint.y-1.0)-13.0);
   crownMask=1.0-step(0.0,coniferEdgeDistance);inner=1.0-step(-1.0,coniferEdgeDistance);
   float tier=fract((leafPoint.y+14.0)/7.0);
   foliage=mix(vec3(.09,.19,.15),vec3(.24,.37,.23),tier*.6+sunward*.4);
  }
  col=PDPaint(col,vec3(.055,.115,.065)*light,crownMask);
  col=PDPaint(col,foliage*light,inner);

 }
 if(structure>.5 && structure<1.5)
 {
  float shadow=PDBox(q,vec2(6.0,2.0),vec2(30.0,27.0));col*=1.0-shadow*.3;
  float wall=PDBox(q,vec2(7.0,5.0),vec2(26.0,16.0));
  float board=step(1.0,mod(q.y,4.0));col=PDPaint(col,mix(vec3(.30,.20,.12),vec3(.59,.43,.25),board)*light,wall);
  vec2 roof=q-vec2(16.0,21.0);float roofMask=step(abs(roof.x),13.0)*step(abs(roof.y),7.0);
  col=PDPaint(col,vec3(.19,.16,.10)*light,roofMask);
  float interior=PDBox(q,vec2(4.0,16.0),vec2(28.0,27.0));
  float shingle=step(1.0,mod(q.y,3.0))*step(1.0,mod(q.x+floor(q.y/3.0)*3.0,6.0));
  col=PDPaint(col,mix(vec3(.42,.28,.12),vec3(.69,.48,.21),shingle)*light,interior);
  col=PDPaint(col,vec3(.82,.65,.32)*light,PDBox(q,vec2(4.0,26.0),vec2(28.0,28.0)));
  col=PDPaint(col,vec3(.13,.12,.10)*light,PDBox(q,vec2(14.0,5.0),vec2(19.0,13.0)));
  col=PDPaint(col,vec3(.92,.73,.33)*light,PDBox(q,vec2(9.0,9.0),vec2(12.0,12.0))+PDBox(q,vec2(22.0,9.0),vec2(25.0,12.0)));
 }
 else if(structure>1.5 && structure<2.5)
 {
  float plot=PDBox(q,vec2(2.0,2.0),vec2(30.0,30.0));
  col=PDPaint(col,mix(vec3(.28,.19,.105),vec3(.43,.30,.16),step(1.0,mod(q.y,5.0)))*light,plot);
  float crop=step(1.0,mod(q.x,6.0))*(1.0-step(4.0,mod(q.x,6.0)))*step(2.0,mod(q.y,5.0))*(1.0-step(4.0,mod(q.y,5.0)));
  col=PDPaint(col,vec3(.36,.50,.16)*light,crop*plot);
  float fence=(PDBox(q,vec2(1.0,1.0),vec2(31.0,2.0))+PDBox(q,vec2(1.0,29.0),vec2(31.0,30.0)));
  float posts=(1.0-step(2.0,mod(q.x,8.0)))*(PDBox(q,vec2(0.0,0.0),vec2(32.0,5.0))+PDBox(q,vec2(0.0,27.0),vec2(32.0,32.0)));
  col=PDPaint(col,vec3(.65,.50,.27)*light,saturate(fence+posts));
 }
 else if(structure>2.5)
 {
  vec2 uv=(id+.5)*_MapSize.zw;
  float left=hlslTextureLod(_InfoTex,vec4(uv-vec2(_MapSize.z,0.0),0.0,0.0)).b*255.0;
  float right=hlslTextureLod(_InfoTex,vec4(uv+vec2(_MapSize.z,0.0),0.0,0.0)).b*255.0;
  float up=hlslTextureLod(_InfoTex,vec4(uv+vec2(0.0,_MapSize.w),0.0,0.0)).b*255.0;
  float down=hlslTextureLod(_InfoTex,vec4(uv-vec2(0.0,_MapSize.w),0.0,0.0)).b*255.0;
  float west=step(2.5,left),east=step(2.5,right),north=step(2.5,up),south=step(2.5,down);
  float horizontal=max(west,east),vertical=max(north,south);
  float path=PDBox(q,vec2(11.0,11.0),vec2(21.0,21.0));
  path=max(path,PDBox(q,vec2(0.0,11.0),vec2(16.0,21.0))*west);
  path=max(path,PDBox(q,vec2(16.0,11.0),vec2(32.0,21.0))*east);
  path=max(path,PDBox(q,vec2(11.0,16.0),vec2(21.0,32.0))*north);
  path=max(path,PDBox(q,vec2(11.0,0.0),vec2(21.0,16.0))*south);
  if(horizontal+vertical<.5)path=PDBox(q,vec2(11.0,0.0),vec2(21.0,32.0));
  float plank=step(1.0,mod(mix(q.y,q.x,horizontal),4.0));
  vec3 road=structure>3.5?mix(vec3(.29,.20,.12),vec3(.62,.46,.25),plank):vec3(.65,.48,.28)*(.9+PDHash(id+q*.02)*.15);
  col=PDPaint(col,road*light,path);
 }
 return col;
}
#endif

      vec4 frag(v2f i)
   {
    vec2 cell=vec2(fract(i.uv.x),clamp(i.uv.y,0.0,1.0-_MapSize.w*.0001))*_MapSize.xy,id=floor(cell),f=fract(cell),uv=(id+.5)*_MapSize.zw;
    vec2 detailCell=(floor(cell*32.0)+.5)/32.0;
    float naturalView=max(_Natural,_Political);
    vec2 colorUV=mix(uv,FlatBlendUV(cell),_SmoothTerrain*naturalView);
    vec3 col=texture(_MainTex,colorUV).rgb;
    vec4 own=texture(_OwnerTex,uv),info=texture(_InfoTex,uv)*255.0;
    vec4 hydro=hlslTextureLod(_HydroA,vec4(uv,0.0,0.0)),mixing=hlslTextureLod(_HydroB,vec4(uv,0.0,0.0)),geo=hlslTextureLod(_GeoTex,vec4(uv,0.0,0.0));
    vec4 field=FlatField(cell);
    float edgeDistance;vec2 riverFlow;
    float aa=clamp(.65/max(1.0,_PixelsPerTile),.008,.28);
    float waterMask=FlatWaterMask(cell,uv,hydro,mixing,field,aa,edgeDistance,riverFlow);
    vec3 illumination=mix(vec3(1.0,1.0,1.0),FlatDayLight(FlatRadial(i.uv),i.uv),naturalView);
    if(naturalView>.5)
    {
     col=mix(FlatVegetation(col,cell,field.a,_PixelsPerTile),col,_DetailWeight);
     col*=FlatFieldRelief(cell,0.0,vec3(-.45,.6,.75));
     if(_DetailWeight>0.0)col=mix(col,PixelGround(col,detailCell,field,waterMask),_DetailWeight);
    }
    if(_WaterView>.5)
    {
     vec4 smoothMix=texture(_HydroB,i.uv);mixing.gb=smoothMix.gb;
     if(hydro.a>.01)hydro.gb=riverFlow;else hydro.gb=texture(_HydroA,i.uv).gb;
     float bankWidth=hydro.a>.01?.075:.16;
     float bankMask=(1.0-waterMask)*(1.0-smoothstep(0.0,bankWidth+aa,edgeDistance))*_ShowBanks;
     vec3 bankColor=FlatMaterialColor(floor(geo.g+.5));
     bankColor=mix(bankColor,FlatColor(vec3(.83,.75,.55)),step(-1.4,field.g)*step(geo.g,.5));
     col=mix(col,bankColor,bankMask*.58);
     col*=illumination;
     vec3 bottom=FlatMaterialColor(floor(geo.g+.5))*.72;
     vec3 waterColor=FlatColor(vec3(.20,.43,.55))*illumination;
     // The uniform switch skips optics without exposing padded land under water.
     if(_ShowWater>.5)waterColor=FlatWaterShade(cell,hydro,mixing,field,bottom,illumination,vec3(0.0,0.0,1.0),normalize(vec3(-.4,.5,.8)),_WaveAmount);
     col=mix(col,waterColor,waterMask);
    }
    else col*=illumination;
    float major;float contour=FlatContour(field.r,_ContourInterval,major);
    float contourStrength=(1.0-_DetailWeight)*_ShowContours*smoothstep(3.0,5.0,_PixelsPerTile)*(1.0-waterMask)*step(.01,field.r)*max(naturalView,_DrawHeight);
    col=mix(col,FlatColor(vec3(.31,.30,.24))*illumination,contour*contourStrength*mix(.23,.42,major));
    if(_Political>.5)
    {
     vec4 east=texture(_OwnerTex,uv+vec2(_MapSize.z,0.0)),north=texture(_OwnerTex,uv+vec2(0.0,_MapSize.w));
     float eastEdge=step(.002,abs(own.a-east.a)),northEdge=step(.002,abs(own.a-north.a));
     float edgeWidth=min(.22,1.0/max(1.0,_PixelsPerTile));
     float border=max(eastEdge*step(1.0-edgeWidth,f.x),northEdge*step(1.0-edgeWidth,f.y));
     col=mix(col,own.rgb*illumination,.34*step(.001,own.a)*(1.0-waterMask*.8));
     col=mix(col,FlatColor(vec3(.87,.79,.57)),border*.70*step(.001,own.a));
    }

    if(naturalView>.5)
    {
     float structure=floor(info.b+.5);
     if(structure>.5&&_PixelsPerTile>=4.0)
     {
      vec2 q=abs(f-.5);float mark=0.0;
      if(structure<1.5)mark=(1.0-step(.22,q.x))*(1.0-step(.22,q.y));
      else if(structure<2.5)mark=(1.0-step(.34,q.x))*(1.0-step(.30,q.y))*step(.45,fract(f.y*5.0));
      else if(structure<3.5)mark=1.0-step(.075,q.x);
      else mark=(1.0-step(.16,q.x))*(1.0-step(.42,q.y));
      col=mix(col,FlatColor(structure<1.5?vec3(.96,.83,.53):vec3(.73,.66,.46)),mark*.9*(1.0-_DetailWeight));
     }
     if(_DetailWeight>0.0)col=mix(col,PixelProps(col,detailCell,field,waterMask,structure,illumination),_DetailWeight);
     float night=1.0-FlatDayWeight(FlatRadial(i.uv));
     float town=1.0-step(.5,abs(structure-1.0));
     col+=town*exp(-dot(f-.5,f-.5)*14.0)*night*_FlatNightLights*FlatColor(vec3(.82,.42,.13))*.75;
    }
    // Composite weather below navigation/selection marks. Data-only layers stay exact.
    if(naturalView>.5)
    {
     #ifdef ORBIS_SCROLL_SURFACE
     col=mix(col,FlatSkyComposite(col,i.uv,cell,waterMask,_PixelsPerTile),smoothstep(.08,.50,_ScrollMorph)*(1.0-smoothstep(.55,1.0,_ScrollMorph)));
     #else
     col=FlatSkyComposite(col,i.uv,cell,waterMask,_PixelsPerTile);
     #endif
    }
    if(_Grid>.5&&_PixelsPerTile>=4.0)
    {
     vec2 z=fract(cell/4.0);float zoneWidth=min(.035,1.0/max(4.0,_PixelsPerTile*4.0));
     float zoneMask=max(1.0-step(zoneWidth,z.x),1.0-step(zoneWidth,z.y));
     col=mix(col,FlatColor(vec3(.20,.24,.21)),zoneMask*.24*(1.0-waterMask));
     if(_PixelsPerTile>=16.0){float tileMask=max(1.0-step(.5/_PixelsPerTile,f.x),1.0-step(.5/_PixelsPerTile,f.y));col*=1.0-tileMask*.10*(1.0-waterMask);}
    }
    float selected=1.0-step(.5,max(abs(id.x-_Selected.x),abs(id.y-_Selected.y)));
    float selectionWidth=min(.20,1.5/max(1.0,_PixelsPerTile));
    float rim=1.0-step(selectionWidth,min(min(f.x,f.y),min(1.0-f.x,1.0-f.y)));
    col=mix(col,FlatColor(vec3(1.0,.91,.61)),selected*rim*.95);
    vec2 selectedZone=cell-_Selected.zw;
    float inZone=step(0.0,selectedZone.x)*step(0.0,selectedZone.y)*step(selectedZone.x,4.0)*step(selectedZone.y,4.0);
    float zoneRim=1.0-step(selectionWidth,min(min(selectedZone.x,selectedZone.y),min(4.0-selectedZone.x,4.0-selectedZone.y)));
    col=mix(col,FlatColor(vec3(.94,.84,.53)),inZone*zoneRim*.7);
    #if defined(ORBIS_SCROLL_SURFACE) && defined(UNITY_COLORSPACE_GAMMA)
    // Native volume/display always consumes linear scene radiance, including its flat endpoint.
    col=GammaToLinearSpace(max(col,0.0));
    #endif
    return vec4(max(col,0.0),1.0);
   }

   
void main(){v2f i;i.uv=vUV;i.pos=gl_FragCoord;targetColor=frag(i);}