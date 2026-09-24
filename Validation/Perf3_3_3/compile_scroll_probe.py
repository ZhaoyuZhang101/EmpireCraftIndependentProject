"""Compile source-derived scroll color and projection functions with Mesa GLES; not Unity HLSL."""
from pathlib import Path
import sys,re,json
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
sys.path.insert(0,str(ROOT/'Validation/Perf3_2'))
import generate_fragment_probes as g
from egl_harness import shader
source=(g.shaderroot/'FlatWorld/ScrollSurface.shader').read_text()
# Select the color pass BEFORE expanding includes, because includes are shared across passes.
color=source.split('CGPROGRAM')[2].split('ENDCG')[0]
def includes(text,path):
 def repl(m):
  name=m[1]
  if name=='UnityCG.cginc':return ''
  p=path/name
  return includes(p.read_text(),p.parent)
 return re.sub(r'#include\s+"([^"]+)"',repl,text)
expanded=includes(color,g.shaderroot/'FlatWorld')
old=g.include_file
g.include_file=lambda path:'CGPROGRAM'+expanded+'ENDCG'
try:probe=g.trans('FlatWorld/FlatMap.shader')
finally:g.include_file=old
(HERE/'scroll_fragment_probe.glsl').write_text(probe)
for linear in [False,True]:shader(0x8B30,probe.replace('#define UNITY_COLORSPACE_GAMMA','') if linear else probe)
# Color pass includes the exact ScrollPosition function also used by the depth pass.
# Compile its vertex invocation using real GLES transforms (identity clip only), no Unity macros.
projection=includes((g.shaderroot/'FlatWorld/ScrollProjection.cginc').read_text(),g.shaderroot/'FlatWorld')
projection=re.sub(r'\bfloat([234])\b',r'vec\1',projection).replace('tex2Dlod','hlslTextureLod').replace('lerp(','mix(')
projection=re.sub(r'\b(vec[234]|float)\s+(_[^;{}]+);',r'uniform \1 \2;',projection)
projection=re.sub(r'(?<![\w.])\d+(?![\w.])',lambda m:m[0]+'.0',projection)
# Repair preprocessor literals if present; all projection uniforms are floating-point.
header='''#version 300 es
precision highp float;
uniform sampler2D _VisualTex;uniform vec4 _MapSize;
vec4 hlslTextureLod(sampler2D s,vec4 uv){return textureLod(s,uv.xy,uv.w);}
'''
vertex=header+projection+'\nvoid main(){vec2 uv;gl_Position=vec4(ScrollPosition(vec2(0.5,0.5),0.0,uv),1.0);}'
(HERE/'scroll_vertex_probe.glsl').write_text(vertex);shader(0x8B31,vertex)
report={'status':'PASS','scope':__doc__,'compiled_color_variants':2,'compiled_projection_vertex':1,'unity_depth_macros_validated':False,'unity_executed':False}
(HERE/'scroll-shader-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
