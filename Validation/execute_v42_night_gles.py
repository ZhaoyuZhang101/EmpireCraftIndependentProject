#!/usr/bin/env python3
"""Execute a mechanically translated night helper on Mesa EGL, NOT Unity/HLSL.
Linux, libEGL.so.1 + GLES3 with RGBA32F rendering required. Standard library Python.
Fixture 1x1 weather/LUT textures test math and branches, not actual scene textures.
"""
import os,ctypes as ct,re,json,math,hashlib
from pathlib import Path
import validate_v42_night_lighting as ref
ROOT=Path(__file__).resolve().parents[1]
DOC=ROOT/'Documentation/V4_2_Repair_IPCFix_NightLighting'
os.environ['EGL_PLATFORM']='surfaceless';os.environ['LIBGL_ALWAYS_SOFTWARE']='1'
E=ct.CDLL('libEGL.so.1');P=ct.c_void_p;I=ct.c_int;U=ct.c_uint;F=ct.c_float

def ef(name,restype,args):
 f=getattr(E,name);f.restype=restype;f.argtypes=args;return f
D=ef('eglGetDisplay',P,[P])(None);major=I();minor=I()
assert ef('eglInitialize',U,[P,ct.POINTER(I),ct.POINTER(I)])(D,ct.byref(major),ct.byref(minor))
attr=(I*13)(0x3033,1,0x3040,0x40,0x3024,8,0x3023,8,0x3022,8,0x3021,8,0x3038);cfg=P();n=I()
assert ef('eglChooseConfig',U,[P,ct.POINTER(I),ct.POINTER(P),I,ct.POINTER(I)])(D,attr,ct.byref(cfg),1,ct.byref(n)) and n.value
assert ef('eglBindAPI',U,[U])(0x30A0)
ctx=ef('eglCreateContext',P,[P,P,P,ct.POINTER(I)])(D,cfg,None,(I*3)(0x3098,3,0x3038));assert ctx
surface=ef('eglCreatePbufferSurface',P,[P,P,ct.POINTER(I)])(D,cfg,(I*5)(0x3057,1,0x3056,1,0x3038));assert surface
assert ef('eglMakeCurrent',U,[P,P,P,P])(D,surface,surface,ctx)
getproc=ef('eglGetProcAddress',P,[ct.c_char_p]);functions={}
def f(name,restype,args):
 if name not in functions:
  addr=getproc(name.encode());assert addr,name;functions[name]=ct.CFUNCTYPE(restype,*args)(addr)
 return functions[name]
def shader(t,s):
 o=f('glCreateShader',U,[U])(t);buf=ct.c_char_p(s.encode());f('glShaderSource',None,[U,I,ct.POINTER(ct.c_char_p),P])(o,1,ct.byref(buf),None)
 f('glCompileShader',None,[U])(o);ok=I();f('glGetShaderiv',None,[U,U,ct.POINTER(I)])(o,0x8B81,ct.byref(ok))
 if not ok.value:
  log=ct.create_string_buffer(20000);f('glGetShaderInfoLog',None,[U,I,P,P])(o,20000,None,log);raise RuntimeError(log.value.decode())
 return o
p=ROOT/'Assets/ORBIS/Resources/ORBIS';original=(p/'OrbisNightLighting.cginc').read_text()
cloud=(p/'OrbisCloudCommon.cginc').read_text();geometry=cloud[cloud.index('float2 OrbisWeatherUV'):cloud.index('float OrbisNoiseAtAge')]
translated=geometry+'\n'+original
for old,new in [('float4','vec4'),('float3','vec3'),('float2','vec2'),('lerp','mix'),('atan2','atan')]:translated=re.sub(r'\b'+old+r'\b',new,translated)
translated=re.sub(r'^(vec4 _Orbis[^;]+;)',r'uniform \1',translated,flags=re.M)
# GLSL requires explicit vector constructors for HLSL's scalar broadcast returns.
pos=0
while True:
 m=re.search(r'\bvec3\s+\w+\([^\)]*\)\s*\{',translated[pos:])
 if not m:break
 start=pos+m.end();depth=1;end=start
 while depth:
  if translated[end]=='{':depth+=1
  if translated[end]=='}':depth-=1
  end+=1
 body=translated[start:end];body=re.sub(r'return ([01]);',r'return vec3(\1.0);',body)
 translated=translated[:start]+body+translated[end:];pos=start+len(body)
# Every numeric literal in these helper functions is floating point in HLSL.
# GLSL ES disallows implicit int/float arithmetic and scalar-first vector max.
translated=translated.replace('max(0,tau)', 'max(tau,0.0)')
translated=re.sub(r'(?<![\w.])(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?(?![\w.])',
    lambda m: m.group() if any(c in m.group() for c in '.eE') else m.group()+'.0',translated)
pre='''#version 300 es
precision highp float; precision highp int;
#define ORBIS_PI 3.14159265359
#define ORBIS_AIR_RADIUS 1.20
uniform float _Night,_Clouds,_LayeredWeather,_CloudHeightScale,_Cover,_Density;
uniform float _Atmosphere,_AirDensity,_Haze,_AtmoLutReady;
uniform vec4 _AtmoLutSize,_AtmoBetaRayleigh,_AtmoBetaMie;
uniform highp sampler2D _Weather,_GroundField,_AtmoOpticalDepth;
float saturate(float x){return clamp(x,0.0,1.0);}
vec2 saturate(vec2 x){return clamp(x,0.0,1.0);}
vec3 saturate(vec3 x){return clamp(x,0.0,1.0);}
vec4 tex2Dlod(highp sampler2D t,vec4 uv){return textureLod(t,uv.xy,uv.w);}
'''
main='''
uniform vec3 testPoint,testSun;uniform float testTau;uniform int testMode;
out vec4 color;
void main(){
if(testMode==0)color=vec4(OrbisNightAmbient(testPoint,testSun,testTau),1);
else if(testMode==1)color=vec4(OrbisAirglowEmission(testPoint,testSun),1);
else if(testMode==2)color=vec4(OrbisMoonAirTransmittance(testPoint),1);
else if(testMode==3)color=vec4(OrbisMoonIncident(testPoint,testSun,testTau),1);
else if(testMode==4)color=vec4(OrbisNightColumnTau(testPoint),0,0,1);
else color=vec4(OrbisNightWeight(testPoint,testSun),0,0,1);
}
'''
frag=pre+translated+main
vs='#version 300 es\nvoid main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);gl_Position=vec4(p*2.0-1.0,0,1);}'
pr=f('glCreateProgram',U,[])();at=f('glAttachShader',None,[U,U]);at(pr,shader(0x8B31,vs));at(pr,shader(0x8B30,frag));f('glLinkProgram',None,[U])(pr)
ok=I();f('glGetProgramiv',None,[U,U,ct.POINTER(I)])(pr,0x8B82,ct.byref(ok))
if not ok.value:
 log=ct.create_string_buffer(20000);f('glGetProgramInfoLog',None,[U,I,P,P])(pr,20000,None,log);raise RuntimeError(log.value.decode())
f('glUseProgram',None,[U])(pr)
getloc=f('glGetUniformLocation',I,[U,ct.c_char_p])
uni={j:f('glUniform%df'%j,None,[I]+[F]*j) for j in range(1,5)};ui=f('glUniform1i',None,[I,I])
def uniform(name,*v):uni[len(v)](getloc(pr,name.encode()),*v)
for k,v in {'_Night':1,'_Clouds':1,'_LayeredWeather':1,'_CloudHeightScale':7e-6,'_Cover':1,'_Density':1,'_Atmosphere':1,'_AirDensity':1,'_Haze':1,'_AtmoLutReady':0}.items():uniform(k,v)
for k,v in {'_OrbisNightParams':(1,1,1,1),'_OrbisMoon':(0,0,1,.12),'_OrbisMoonColor':(.9,.94,1,1),
 '_OrbisNightSkyColor':(.04,.055,.08,0),'_OrbisAirglowColor':(.07,.18,.105,0),'_OrbisAirglowShell':(1.165,.016,0,0),
 '_AtmoLutSize':(1,1,1,1),'_AtmoBetaRayleigh':(1.375,3.25,5.6,.032),'_AtmoBetaMie':(3.15,3.15,3.15,.008)}.items():uniform(k,*v)
textures=[]
def texture(unit,rgba=None):
 tex=U();f('glActiveTexture',None,[U])(0x84C0+unit);f('glGenTextures',None,[I,ct.POINTER(U)])(1,ct.byref(tex));f('glBindTexture',None,[U,U])(0x0DE1,tex)
 data=(F*4)(*rgba) if rgba else None
 f('glTexImage2D',None,[U,I,I,I,I,I,U,U,P])(0x0DE1,0,0x8814,1,1,0,0x1908,0x1406,data)
 for target in [0x2801,0x2800]:f('glTexParameteri',None,[U,U,I])(0x0DE1,target,0x2600)
 for target in [0x2802,0x2803]:f('glTexParameteri',None,[U,U,I])(0x0DE1,target,0x812F)
 textures.append(tex);return tex
for i,(name,rgba) in enumerate([('_Weather',(.8,0,0,.6)),('_GroundField',(1,0,0,0)),('_AtmoOpticalDepth',(.032,.008,0,0))]):
 texture(i,rgba);ui(getloc(pr,name.encode()),i)
out=texture(3)
fb=U();f('glGenFramebuffers',None,[I,ct.POINTER(U)])(1,ct.byref(fb));f('glBindFramebuffer',None,[U,U])(0x8D40,fb)
f('glFramebufferTexture2D',None,[U,U,U,U,I])(0x8D40,0x8CE0,0x0DE1,out,0)
assert f('glCheckFramebufferStatus',U,[U])(0x8D40)==0x8CD5
vao=U();f('glGenVertexArrays',None,[I,ct.POINTER(U)])(1,ct.byref(vao));f('glBindVertexArray',None,[U])(vao)
f('glViewport',None,[I,I,I,I])(0,0,1,1)
read=f('glReadPixels',None,[I,I,I,I,U,U,P]);draw=f('glDrawArrays',None,[U,I,I]);buf=(F*4)()
counts={};errors={}
def compare(label,mode,p,s,tau,expected):
 ui(getloc(pr,b'testMode'),mode);uniform('testPoint',*p);uniform('testSun',*s);uniform('testTau',tau)
 draw(4,0,3);read(0,0,1,1,0x1908,0x1406,buf);assert not f('glGetError',U,[])()
 value=list(buf)[:3];assert all(math.isfinite(x) for x in value)
 err=max(abs(a-b) for a,b in zip(value,expected));counts[label]=counts.get(label,0)+1;errors[label]=max(errors.get(label,0),err)
 assert err<2e-4,(label,value,expected,err)
s=(0,0,-1)
for i in range(61):
 h=1+i*.004;p=(0,0,h)
 for tau in [0,1,4,12]:compare('ambient',0,p,s,tau,ref.ambient(p,s,tau))
 compare('airglow',1,p,s,0,ref.emission(p,s));compare('column',4,p,s,0,[ref.column(p),0,0])
for lutready in (0,1):
 uniform('_AtmoLutReady',lutready)
 for h in (1.001,1.02,1.1,1.199,1.3):
  for deg in range(-90,91,5):
   d=(math.cos(math.radians(deg)),0,math.sin(math.radians(deg)));p=(0,0,h);uniform('_OrbisMoon',*d,.12)
   lut=(.032,.008) if lutready else None
   compare('moon_air_lut' if lutready else 'moon_air_fallback',2,p,s,0,ref.moon_air(p,d,lut=lut))
   compare('moon_incident',3,p,s,2,ref.incident(p,s,d,2,lut=lut))
for deg in range(-90,91):
 d=(math.cos(math.radians(deg)),0,math.sin(math.radians(deg)))
 compare('night_gate',5,(0,0,1.01),d,0,[ref.weight((0,0,1.01),d),0,0])
uniform('_Atmosphere',0)
compare('atmosphere_off',1,(0,0,1.165),s,0,[0,0,0])
compare('atmosphere_off',0,(0,0,1.001),s,4,ref.ambient((0,0,1.001),s,4,atmosphere=0))
report={'status':'PASS','scope':'Mechanically translated NEW helper + original sphere/UV visibility, executed on software GLES; NOT Unity/HLSL/C# compile or scene screenshot',
 'renderer':f('glGetString',ct.c_char_p,[U])(0x1F01).decode(),'api':f('glGetString',ct.c_char_p,[U])(0x1F02).decode(),
 'case_counts':counts,'total_cases':sum(counts.values()),'max_absolute_error_vs_CPU_double':errors,
 'test_textures':'1x1 RGBA32F constant fixtures. Does not validate production LUT content or world texture sampling.',
 'unity_compile':'NOT_RUN','hlsl_compile':'NOT_RUN','unity_visual_validation':'NOT_RUN','hardware_fps':'NOT_RUN',
 'helper_sha256':hashlib.sha256(original.encode()).hexdigest(),'translated_fragment_sha256':hashlib.sha256(frag.encode()).hexdigest()}
DOC.mkdir(exist_ok=True);(DOC/'night-helper-gles-report.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
