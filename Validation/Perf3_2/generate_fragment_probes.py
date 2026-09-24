#!/usr/bin/env python3
"""Mechanical HLSL-fragment to GLSL ES probes, NOT a Unity shader compiler.
Uses the shipped EGL harness, with engine transforms/clip stubbed explicitly.
"""
from pathlib import Path
import re
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Validation/Perf3_2';OUT.mkdir(exist_ok=True)
# Only reuse the EGL harness setup, not any historical shaders/screenshots.
old=(ROOT/'Validation/gles_preview_renderer.py').read_text();h=old[:old.index('html=(ROOT')]
h=h.replace("ROOT=Path(__file__).resolve().parents[1]","ROOT=Path(__file__).resolve().parents[2]").replace("OUTPUT=ROOT/'ValidationOutput';OUTPUT.mkdir(exist_ok=True)","OUTPUT=ROOT/'Validation/Perf3_2/Artifacts';OUTPUT.mkdir(exist_ok=True)")
h=h.replace('0x3057,1000,0x3056,650','0x3057,1280,0x3056,720')
(OUT/'egl_harness.py').write_text(h)
shaderroot=ROOT/'Assets/ORBIS/Resources/ORBIS'
def include_file(path,seen=None):
    if seen is None:seen=set()
    if path in seen:return ''
    seen.add(path)
    text=path.read_text()
    def match(m):
        name=m.group(1)
        if name in ['UnityCG.cginc','OrbisPatch.cginc']:return '// Engine transform/clip omitted in the fragment-only probe.'
        return include_file((path.parent/name).resolve(),seen)
    return re.sub(r'#include\s+"([^"]+)"',match,text)
def block_end(s,start):
    p=s.index('{',start);k=1;q=p+1
    while k:
        k+= (s[q]=='{')-(s[q]=='}');q+=1
    return q
def trans(shadername):
    path=shaderroot/shadername
    text=include_file(path);text=text[text.index('CGPROGRAM')+len('CGPROGRAM'):text.index('ENDCG')]
    text=re.sub(r'^\s*#pragma[^\n]*','',text,flags=re.M)
    # Vertex programs / engine structures are outside this fragment probe's scope.
    for sig in ['v2f vert(', 'depthOutput depthVert(']:
        if sig in text:
            start=text.index(sig);text=text[:start]+text[block_end(text,start):]
    text=re.sub(r':\s*(?:POSITION|NORMAL|COLOR|TEXCOORD\d+|SV_POSITION|SV_Target)\b','',text)
    text=re.sub(r'\[(?:loop|branch|unroll)(?:\([^)]*\))?\]','',text)
    text=re.sub(r'\bfloat([234])\b',r'vec\1',text)
    text=text.replace('tex2Dlod','hlslTextureLod').replace('tex2D','texture').replace('lerp(','mix(').replace('frac(','fract(').replace('fmod(','mod(').replace('rsqrt(','inversesqrt(').replace('ddx(','dFdx(').replace('ddy(','dFdy(')
    text=re.sub(r'\b(vec[234]|float|sampler2D)\s+(_[^;{}]+);',r'uniform \1 \2;',text)
    # HLSL scalar-to-vector initializer promotion.
    text=text.replace('mixing=0,geo=0,own=0,info=0','mixing=vec4(0),geo=vec4(0),own=vec4(0),info=vec4(0)')
    text=text.replace('return vec4(saturate(dot(normal,sun)).xxx,1);','return vec4(vec3(saturate(dot(normal,sun))),1);').replace('cloudShade.xxx','vec3(cloudShade)')
    header='''#version 300 es
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
'''
    # GLSL ES does not use all Cg implicit scalar promotions. Promote only numeric
    # arguments to overloads requiring floating point (not loop counters/indices).
    names=['max','min','clamp','step','smoothstep','mod','mix','pow','atan','exp','exp2','log2']
    for _ in range(3):
        text=re.sub(r'\b('+ '|'.join(names) +r')\(\s*(-?\d+)(?=\s*[,\)])',lambda m:m.group(1)+'('+m.group(2)+'.0',text)
    # Cg promotes integer scalar arithmetic; these loops and direction codes are
    # exact small integers. Float loop counters are equivalent in this ES probe.
    text=re.sub(r'\bint\b','float',text).replace('atan2(','atan(')
    text=re.sub(r'(?<![\w.])(?:\d+\.\d*|\.\d+|\d+)(?:[eE][+-]?\d+)?[fF]?',lambda m:m.group() if ('.' in m.group() or 'e' in m.group().lower()) else m.group()+'.0',text)
    text=re.sub(r'\[(outlet|d|faceIndex)\]',r'[int(\1)]',text)
    text=text.replace('max(0.0,scattering)','max(scattering,0.0)').replace('max(0.0,col)','max(col,0.0)')
    if 'FlatMap.shader' in shadername:
        main='void main(){v2f i;i.uv=vUV;i.pos=gl_FragCoord;targetColor=frag(i);}'
    elif 'FlatSkyBake' in shadername:
        main='void main(){v2f_img i;i.uv=vUV;targetColor=frag(i);}'
    else:
        main='''void main(){v2f i;float lon=(vUV.x-.5)*6.283185307,lat=(vUV.y-.5)*3.141592654;
        i.pos=gl_FragCoord;i.local=vec3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));i.normal=i.local;i.view=vec3(0,0,1);
        i.color=vec4(.6,.7,.4,0);i.climate=vec4(0);i.waterData=vec4(0);targetColor=frag(i);}'''
    return header+text+'\n'+main
if __name__=='__main__':
    for path,name in [('FlatWorld/FlatMap.shader','flat'),('FlatWorld/FlatSkyBake.shader','sky'),('Surface.shader','surface')]:
        (OUT/(name+'_fragment_probe.glsl')).write_text(trans(path))
    print('Wrote source-derived GLSL fragment probes (NOT Unity compilation)')
