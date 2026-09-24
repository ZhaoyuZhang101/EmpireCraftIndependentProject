#version 300 es
precision highp float;
uniform sampler2D _VisualTex;uniform vec4 _MapSize;
vec4 hlslTextureLod(sampler2D s,vec4 uv){return textureLod(s,uv.xy,uv.w);}
// One immutable UV lattice, two continuous endpoint positions. No scene swap or screenshot morph.
#ifndef ORBIS_SCROLL_PROJECTION
#define ORBIS_SCROLL_PROJECTION
#ifndef ORBIS_SCROLL_HEIGHT
#define ORBIS_SCROLL_HEIGHT
uniform vec2 _ScrollPoleHeights;
uniform float _ScrollHeightScale;
float ScrollRadius(vec4 field,vec2 uv,float rows)
{
 float h=clamp(field.r+(field.g>0.0?field.b:0.0),0.0,4000.0);
 float polar=mix(_ScrollPoleHeights.x,_ScrollPoleHeights.y,step(.5,uv.y));
 float blend=smoothstep(0.0,1.0,min(uv.y,1.0-uv.y)*rows);
 return 1.0+mix(polar,h,blend)*_ScrollHeightScale;
}
#endif

#ifndef ORBIS_MARKLEY_INCLUDED
#define ORBIS_MARKLEY_INCLUDED
uniform sampler2D _MarkleyDirections;
uniform vec4 _MarkleyTableSize;
vec2 MarkleyFold(vec2 uv){float row=floor(uv.y),k=floor(row*.5),odd=row-2.0*k;return vec2(fract(odd>.5?.75-uv.x-k*.5:uv.x+k*.5),odd>.5?1.0-fract(uv.y):fract(uv.y));}
vec3 MarkleyDirection(vec2 uv){uv=MarkleyFold(uv);uv=(uv*(_MarkleyTableSize.xy-1.0)+.5)*_MarkleyTableSize.zw;return normalize(hlslTextureLod(_MarkleyDirections,vec4(uv,0.0,0.0)).xyz);}
vec2 MarkleyGeoUV(vec2 uv){vec3 n=MarkleyDirection(uv);return vec2(atan(n.x,n.z)/6.28318530718+.5,asin(clamp(n.y,-1.0,1.0))/3.14159265359+.5);}
#endif

uniform vec4 _ProjectionSize,_MarkleyCopy;
uniform vec4 _ScrollAnchor, _ScrollPan;
uniform vec3 _ScrollRight, _ScrollUp, _ScrollNormal;
uniform float _ScrollMorph, _ScrollSpin, _ScrollCopy;
vec3 ScrollRotate(vec3 p)
{
 float s=sin(_ScrollSpin),c=cos(_ScrollSpin);
 return vec3(c*p.x+s*p.z,p.y,-s*p.x+c*p.z);
}
vec3 ScrollPosition(vec2 meshUV,float shell,out vec2 geo)
{
 geo=meshUV+_MarkleyCopy.xy;
 float scale=6.28318530718/_MapSize.x;
 vec2 delta=(geo*_ProjectionSize.xy-_ScrollPan.xy)*scale;
 vec3 plane=_ScrollNormal+_ScrollRight*delta.x+_ScrollUp*delta.y;
 plane+=_ScrollNormal*shell*.006;
 if(_ScrollMorph>=1.0) return plane;
 // Presentation reflection is shared by the CPU tangent frame and native cloud transform.
 vec3 radial=MarkleyDirection(geo);radial.z=-radial.z;
 vec2 heightUV=MarkleyGeoUV(geo);
 // Every longitude converges to the same polar radius over the last source row.
 vec4 field=hlslTextureLod(_VisualTex,vec4(heightUV,0.0,0.0));
 float radius=ScrollRadius(field,heightUV,_MapSize.y);
 vec3 sphere=ScrollRotate(radial)*(radius+shell*(1.0-_ScrollMorph));
 return mix(sphere,plane,_ScrollMorph);
}
#endif

void main(){vec2 uv;gl_Position=vec4(ScrollPosition(vec2(0.5,0.5),0.0,uv),1.0);}