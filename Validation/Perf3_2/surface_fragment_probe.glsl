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
   // Engine transform/clip omitted in the fragment-only probe.
   // Surface never includes OrbisOptics/OrbisWater/CloudCommon. Atmospheric volume
   // stays in its camera pass. Nine samplers; no ray-march or nested atmosphere paths.
   #define ORBIS_FLAT_NO_SWE
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

   uniform sampler2D _FlatTerrain,_FlatOwners,_FlatInfo,_TerrainAtlas,_SkyTex;
   uniform float _FlatWorldReady,_FlatPolitical,_Natural,_Night,_SolarEnergy,_CloudShadow,_ShadowStrength;
   uniform float _TerrainAtlasReady,_SurfaceDetail,_Settlement,_Clouds,_SurfaceDebug;
   uniform float _SkyReady,_FlatCloudShadows,_FlatNightLights,_ShowContours,_ContourInterval;
   uniform vec3 _Sun;uniform vec4 _Tint,_FlatSize;
   uniform vec4 _OrbisNightParams,_OrbisMoon,_OrbisMoonColor,_OrbisNightSkyColor,_OrbisAirglowColor;
   struct appdata {vec4 vertex;vec3 normal;vec4 color;vec3 geo;vec4 climate;vec4 waterData;vec3 flow;};
   struct v2f {vec4 pos;vec3 local;vec3 normal;vec3 view;vec4 color;vec4 climate;vec4 waterData;};
   
   vec4 frag(v2f i)
   {
    OrbisPatchClip(i.local,i.pos.xy);
    vec3 radial=normalize(i.local),normal=normalize(i.normal),sun=normalize(_Sun+vec3(0.0,1e-7,0.0)),view=normalize(i.view);
    if(dot(normal,radial)<0.0)normal=-normal;
    vec2 uv=vec2(atan(radial.x,radial.z)/6.283185307+.5,asin(clamp(radial.y,-1.0,1.0))/3.141592654+.5);
    vec3 east=normalize(vec3(radial.z,0.0,-radial.x)+vec3(1e-6,0.0,0.0)),north=normalize(cross(radial,east));
    vec3 localLight=vec3(dot(sun,east),dot(sun,north),dot(sun,radial));
    vec3 localView=normalize(vec3(dot(view,east),dot(view,north),max(.02,dot(view,radial))));
    float pixelsPerTile=1.0/max(1e-5,max(length(dFdx(uv*_MapSize.xy)),length(dFdy(uv*_MapSize.xy))));
    float night=1.0-smoothstep(-.12,.16,dot(radial,sun));
    vec3 sky=_OrbisNightSkyColor.rgb*_OrbisNightParams.y+_OrbisAirglowColor.rgb*_OrbisNightParams.z;
    float moon=max(0.0,dot(normal,_OrbisMoon.xyz))*_OrbisMoon.w;
    vec3 illumination=mix(vec3(.055,.073,.10),sky,_OrbisNightParams.x)+moon*_OrbisMoonColor.rgb;
    float cloudShade=1.0;
    if(_FlatWorldReady>.5&&_SkyReady>.5)
       cloudShade=1.0-texture(_SkyTex,uv).r*.38*_FlatCloudShadows;
    illumination+=max(0.0,dot(normal,sun))*vec3(1.04,1.02,.96)*cloudShade*max(.1,_SolarEnergy)*1.35;
    illumination=mix(vec3(.95,.97,1.0),illumination,_Night);
    vec3 col=FlatColor(i.color.rgb)*_Tint.rgb;
    float water=smoothstep(.45,.9,i.color.a);
    vec4 field=vec4(0.0,-8.0,max(.2,i.waterData.x),0.0),hydro=vec4(max(.2,i.waterData.x),0.0,0.0,0.0),mixing=vec4(0.0),geo=vec4(0.0),own=vec4(0.0),info=vec4(0.0);
    if(_FlatWorldReady>.5)
    {
     vec2 cell=uv*_MapSize.xy,tileUV=(floor(cell)+.5)*_MapSize.zw;
     field=FlatField(cell);hydro=hlslTextureLod(_HydroA,vec4(tileUV,0.0,0.0));mixing=hlslTextureLod(_HydroB,vec4(tileUV,0.0,0.0));geo=hlslTextureLod(_GeoTex,vec4(tileUV,0.0,0.0));
     own=texture(_FlatOwners,tileUV);info=texture(_FlatInfo,tileUV)*255.0;
     col=texture(_FlatTerrain,mix(tileUV,FlatBlendUV(cell),_SmoothTerrain)).rgb;
     float aa=clamp(.65/max(1.0,pixelsPerTile),.008,.28),edgeDistance;vec2 riverFlow;
     water=FlatWaterMask(cell,tileUV,hydro,mixing,field,aa,edgeDistance,riverFlow);
     if(hydro.a>.01)hydro.gb=riverFlow;else hydro.gb=texture(_HydroA,uv).gb;
     mixing.gb=texture(_HydroB,uv).gb;
     col=FlatVegetation(col,cell,field.a,pixelsPerTile);
     col*=FlatFieldRelief(cell,0.0,vec3(-.45,.6,.75));
     float bank=(1.0-water)*(1.0-smoothstep(0.0,(hydro.a>.01?.075:.16)+aa,edgeDistance))*_ShowBanks;
     col=mix(col,FlatMaterialColor(floor(geo.g+.5)),bank*.58);
    }
    else if(_Natural>.5&&_TerrainAtlasReady>.5)
    {
     // Lightweight legacy scene compatibility; outer atmospheric camera effects remain.
     vec2 p=fract(uv*vec2(96.0,48.0));vec2 atlasUV=p*.488+.006;
     vec2 quadrant=i.climate.y>.5?vec2(.5,0.0):i.climate.z>.5?vec2(0.0,0.0):i.climate.x<.25?vec2(.5,.5):vec2(0.0,.5);
     col=mix(col,col*(.75+texture(_TerrainAtlas,atlasUV+quadrant).rgb*.5),saturate(_SurfaceDetail*.5)*(1.0-water));
    }
    if(_SurfaceDebug==1.0)return vec4(col,1.0);
    if(_SurfaceDebug==2.0)return vec4(vec3(saturate(dot(normal,sun))),1.0);
    if(_SurfaceDebug==4.0)return vec4(vec3(cloudShade),1.0);
    col*=illumination;
    if(_Natural>.5)
    {
     vec3 waterColor=FlatColor(vec3(.20,.43,.55))*illumination;
     if(_ShowWater>.5)waterColor=FlatWaterShade(uv*_MapSize.xy,hydro,mixing,field,FlatMaterialColor(floor(geo.g+.5))*.72,illumination,localView,localLight,_WaveAmount);
     col=mix(col,waterColor,water);
    }
    if(_FlatWorldReady>.5)
    {
     float major;float contour=FlatContour(field.r,_ContourInterval,major);
     float strength=_ShowContours*smoothstep(3.0,5.0,pixelsPerTile)*(1.0-water)*step(.01,field.r);
     col=mix(col,FlatColor(vec3(.31,.30,.24))*illumination,contour*strength*mix(.23,.42,major));
     vec2 f=fract(uv*_MapSize.xy),tileUV=(floor(uv*_MapSize.xy)+.5)*_MapSize.zw;
     vec4 adjacentE=texture(_FlatOwners,tileUV+vec2(_MapSize.z,0.0)),adjacentN=texture(_FlatOwners,tileUV+vec2(0.0,_MapSize.w));
     float ew=min(.22,1.0/max(1.0,pixelsPerTile));
     float border=max(step(.002,abs(own.a-adjacentE.a))*step(1.0-ew,f.x),step(.002,abs(own.a-adjacentN.a))*step(1.0-ew,f.y));
     col=mix(col,own.rgb*illumination,.34*_FlatPolitical*step(.001,own.a)*(1.0-water*.8));
     col=mix(col,FlatColor(vec3(.88,.80,.60))*max(illumination,.15),border*.70*_FlatPolitical*step(.001,own.a));
     float settlement=1.0-step(.5,abs(floor(info.b+.5)-1.0));
     col+=settlement*exp(-dot(f-.5,f-.5)*14.0)*night*_Night*_FlatNightLights*FlatColor(vec3(.82,.42,.13))*.75;
    }
    else col+=_Settlement*_Night*night*vec3(.9,.38,.09);
    return vec4(max(col,0.0),1.0);
   }
   
void main(){v2f i;float lon=(vUV.x-.5)*6.283185307,lat=(vUV.y-.5)*3.141592654;
        i.pos=gl_FragCoord;i.local=vec3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));i.normal=i.local;i.view=vec3(0,0,1);
        i.color=vec4(.6,.7,.4,0);i.climate=vec4(0);i.waterData=vec4(0);targetColor=frag(i);}