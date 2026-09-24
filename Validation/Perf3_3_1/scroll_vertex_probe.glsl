#version 300 es
precision highp float;
uniform sampler2D _VisualTex;uniform vec4 _MapSize;
vec4 hlslTextureLod(sampler2D s,vec4 uv){return textureLod(s,uv.xy,uv.w);}
// One immutable UV lattice, two continuous endpoint positions. No scene swap or screenshot morph.
#ifndef ORBIS_SCROLL_PROJECTION
#define ORBIS_SCROLL_PROJECTION
#include "ScrollHeight.cginc"
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
 geo=vec2(meshUV.x-.5+_ScrollAnchor.x+_ScrollCopy,meshUV.y);
 float scale=6.28318530718/_MapSize.x;
 vec2 delta=(geo*_MapSize.xy-_ScrollPan.xy)*scale;
 vec3 plane=_ScrollNormal+_ScrollRight*delta.x+_ScrollUp*delta.y;
 plane+=_ScrollNormal*shell*.006;
 if(_ScrollMorph>=1.0) return plane;
 float lon=(geo.x-.5)*6.28318530718,lat=(geo.y-.5)*3.14159265359;
 // Z reflection gives a north-up, east-right front face. The native volume uses the same transform.
 vec3 radial=vec3(cos(lat)*sin(lon),sin(lat),-cos(lat)*cos(lon));
 vec2 heightUV=geo;
 // Every longitude converges to the same polar radius over the last source row.
 vec4 field=hlslTextureLod(_VisualTex,vec4(heightUV,0.0,0.0));
 float radius=ScrollRadius(field,geo,_MapSize.y);
 vec3 sphere=ScrollRotate(radial)*(radius+shell*(1.0-_ScrollMorph));
 return mix(sphere,plane,_ScrollMorph);
}
#endif

void main(){vec2 uv;gl_Position=vec4(ScrollPosition(vec2(0.5,0.5),0.0,uv),1.0);}