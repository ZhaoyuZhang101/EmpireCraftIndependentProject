"""Execute the production Markley.cginc lookup in Mesa GLES, compare repeated cover images.
RGBA8 direction encoding is used, not a Unity screenshot or performance benchmark.
"""
from pathlib import Path
import sys,re,json,ctypes as ct
import numpy as np
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
sys.path.insert(0,str(ROOT/'Validation/Perf3_2'))
from egl_harness import *
src=(ROOT/'Assets/ORBIS/Resources/ORBIS/FlatWorld/Markley.cginc').read_text()
src=re.sub(r'\bfloat([234])\b',r'vec\1',src).replace('frac(','fract(').replace('atan2(','atan(').replace('tex2Dlod(','sampleLod(')
src=src.replace('sampler2D _','uniform sampler2D _').replace('vec4 _','uniform vec4 _')
src=re.sub(r'(?<![\w.])\d+(?![\w.])',lambda m:m[0]+'.0',src)
vs='#version 300 es\nout vec2 vUV;void main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);vUV=p;gl_Position=vec4(p*2.0-1.0,0,1);}'
fs='#version 300 es\nprecision highp float;\nin vec2 vUV;out vec4 color;uniform vec2 offset;vec4 sampleLod(sampler2D s,vec4 uv){return textureLod(s,uv.xy,uv.w);}\n'+src+'\nvoid main(){color=vec4(MarkleyDirection(vUV*vec2(3,4)+offset)*.5+.5,1);}'
pr=f('glCreateProgram',U,[])();f('glAttachShader',None,[U,U])(pr,shader(0x8B31,vs));f('glAttachShader',None,[U,U])(pr,shader(0x8B30,fs));f('glLinkProgram',None,[U])(pr)
ok=I();f('glGetProgramiv',None,[U,U,ct.POINTER(I)])(pr,0x8B82,ct.byref(ok));assert ok.value
f('glUseProgram',None,[U])(pr);getloc=f('glGetUniformLocation',I,[U,ct.c_char_p])
vao=U();f('glGenVertexArrays',None,[I,ct.POINTER(U)])(1,ct.byref(vao));f('glBindVertexArray',None,[U])(vao)
t=np.fromfile(ROOT/'Assets/ORBIS/Resources/ORBIS/Markley/inverse.bytes',dtype='<f4').reshape(445,1025,4)
obj=U();f('glGenTextures',None,[I,ct.POINTER(U)])(1,ct.byref(obj));f('glBindTexture',None,[U,U])(0x0DE1,obj)
for p,v in [(0x2801,0x2601),(0x2800,0x2601),(0x2802,0x812F),(0x2803,0x812F)]:f('glTexParameteri',None,[U,U,I])(0x0DE1,p,v)
f('glTexImage2D',None,[U,I,I,I,I,I,U,U,P])(0x0DE1,0,0x8814,1025,445,0,0x1908,0x1406,t.ctypes.data)
f('glUniform1i',None,[I,I])(getloc(pr,b'_MarkleyDirections'),0)
f('glUniform4f',None,[I,F,F,F,F])(getloc(pr,b'_MarkleyTableSize'),1025,445,1/1025,1/445)
def render(x,y):
 f('glUniform2f',None,[I,F,F])(getloc(pr,b'offset'),x,y);w,h=768,512
 f('glViewport',None,[I,I,I,I])(0,0,w,h);f('glDrawArrays',None,[U,I,I])(4,0,3)
 data=(ct.c_ubyte*(w*h*4))();f('glReadPixels',None,[I,I,I,I,U,U,P])(0,0,w,h,0x1908,0x1401,data);assert f('glGetError',U,[])()==0
 return np.frombuffer(data,dtype=np.uint8).copy().reshape(h,w,4)
a=render(-1,-1);errors={}
for name,offset in [('left_right',(0,-1)),('north_south',(-.5,1)),('full_vertical',(-1,3))]:
 b=render(*offset);errors[name]=int(np.max(np.abs(a.astype(int)-b.astype(int))));assert errors[name]<=1,errors
assert np.std(a[:,:,:3])>30,'direction texture absent or uniform'
r=dict(status='PASS',scope=__doc__,max_rgba8_errors=errors,unity_executed=False);(HERE/'gpu-wrap-report.json').write_text(json.dumps(r,indent=2));print(r)
