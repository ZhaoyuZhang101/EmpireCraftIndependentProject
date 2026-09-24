"""Local GLES shader compilation/rendering on Mesa EGL; NOT Unity/HLSL validation."""
import os,ctypes as ct, re,json,math
from pathlib import Path
os.environ['EGL_PLATFORM']='surfaceless';os.environ['LIBGL_ALWAYS_SOFTWARE']='1'
ROOT=Path(__file__).resolve().parents[1]
OUTPUT=ROOT/'ValidationOutput';OUTPUT.mkdir(exist_ok=True)
E=ct.CDLL('libEGL.so.1')
def ef(name,restype,args):
 f=getattr(E,name);f.restype=restype;f.argtypes=args;return f
P=ct.c_void_p;I=ct.c_int;U=ct.c_uint;F=ct.c_float
getdisplay=ef('eglGetDisplay',P,[P]);init=ef('eglInitialize',U,[P,ct.POINTER(I),ct.POINTER(I)])
D=getdisplay(None);major=I();minor=I()
assert init(D,ct.byref(major),ct.byref(minor)),hex(E.eglGetError())
choose=ef('eglChooseConfig',U,[P,ct.POINTER(I),ct.POINTER(P),I,ct.POINTER(I)])
attr=(I*13)(0x3033,1,0x3040,0x40,0x3024,8,0x3023,8,0x3022,8,0x3021,8,0x3038);cfg=P();n=I()
assert choose(D,attr,ct.byref(cfg),1,ct.byref(n)) and n.value
assert ef('eglBindAPI',U,[U])(0x30A0)
ctx=ef('eglCreateContext',P,[P,P,P,ct.POINTER(I)])(D,cfg,None,(I*3)(0x3098,3,0x3038))
assert ctx
surface=ef('eglCreatePbufferSurface',P,[P,P,ct.POINTER(I)])(D,cfg,(I*5)(0x3057,1000,0x3056,650,0x3038))
assert surface
assert ef('eglMakeCurrent',U,[P,P,P,P])(D,surface,surface,ctx)
getproc=ef('eglGetProcAddress',P,[ct.c_char_p])
def gf(name,restype,args):
 a=getproc(name.encode());assert a,name
 return ct.CFUNCTYPE(restype,*args)(a)
gl={}
def f(name,restype,args):
 if name not in gl:gl[name]=gf(name,restype,args)
 return gl[name]
getstr=f('glGetString',ct.c_char_p,[U]);print('GLES:',getstr(0x1F02).decode(),'Renderer:',getstr(0x1F01).decode())
create=f('glCreateShader',U,[U]);source=f('glShaderSource',None,[U,I,ct.POINTER(ct.c_char_p),P]);compile_=f('glCompileShader',None,[U]);getiv=f('glGetShaderiv',None,[U,U,ct.POINTER(I)]);getlog=f('glGetShaderInfoLog',None,[U,I,P,P])
def shader(t,s):
 o=create(t);buf=ct.c_char_p(s.encode());source(o,1,ct.byref(buf),None);compile_(o);ok=I();getiv(o,0x8B81,ct.byref(ok))
 if not ok.value:
  log=ct.create_string_buffer(100000);getlog(o,100000,None,log);raise RuntimeError(log.value.decode())
 return o
html=(ROOT/'Preview/AtmosphereTransit.html').read_text()
frag=re.search(r'<script id="fragment"[^>]*>(.*?)</script>',html,re.S).group(1)
vs='#version 300 es\nvoid main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);gl_Position=vec4(p*2.0-1.0,0,1);}'
program=f('glCreateProgram',U,[])();attach=f('glAttachShader',None,[U,U]);attach(program,shader(0x8B31,vs));attach(program,shader(0x8B30,frag));f('glLinkProgram',None,[U])(program)
ok=I();f('glGetProgramiv',None,[U,U,ct.POINTER(I)])(program,0x8B82,ct.byref(ok))
if not ok.value:
 log=ct.create_string_buffer(100000);f('glGetProgramInfoLog',None,[U,I,P,P])(program,100000,None,log);raise RuntimeError(log.value.decode())
f('glUseProgram',None,[U])(program)
getloc=f('glGetUniformLocation',I,[U,ct.c_char_p]);u1=f('glUniform1f',None,[I,F]);u2=f('glUniform2f',None,[I,F,F]);u3=f('glUniform3f',None,[I,F,F,F]);u4=f('glUniform4f',None,[I,F,F,F,F]);ui=f('glUniform1i',None,[I,I])
def uniform(k,*v):
 loc=getloc(program,k.encode());[None,u1,u2,u3,u4][len(v)](loc,*v)
for k,v in {'_Atmosphere':1,'_AirDensity':1,'_Haze':1,'_SolarEnergy':1,'uTime':0,'uExposure':1}.items():uniform(k,v)
uniform('_AtmoBetaRayleigh',1.375,3.25,5.6,.032);uniform('_AtmoBetaMie',3.15,3.15,3.15,.008);uniform('_AtmoBetaAbsorption',10.2,24.85,.975,.06);uniform('_AtmoParameters',.008,.7,12,16);uniform('_AtmoSunDisk',math.cos(.53*math.pi/180),3)
from PIL import Image
import numpy as np
width,height=900,620
uniform('uResolution',width,height);ui(getloc(program,b'uSteps'),64);f('glViewport',None,[I,I,I,I])(0,0,width,height)
vao=U();f('glGenVertexArrays',None,[I,ct.POINTER(U)])(1,ct.byref(vao));f('glBindVertexArray',None,[U])(vao)
def render(name,alt,tilt,sun,clouds):
 a=math.radians(tilt);e=math.radians(sun)
 uniform('uCamera',0,0,1+alt);uniform('uForward',0,math.sin(a),-math.cos(a));uniform('uUp',0,math.cos(a),math.sin(a));uniform('uRight',1,0,0);uniform('uSun',0,math.cos(e),math.sin(e));uniform('uClouds',int(clouds))
 f('glDrawArrays',None,[U,I,I])(4,0,3);f('glFinish',None,[])()
 error=f('glGetError',U,[])();assert not error,hex(error)
 data=(ct.c_ubyte*(width*height*4))();f('glReadPixels',None,[I,I,I,I,U,U,P])(0,0,width,height,0x1908,0x1401,data)
 img=Image.frombytes('RGBA',(width,height),bytes(data)).transpose(Image.Transpose.FLIP_TOP_BOTTOM).convert('RGB');img.save(OUTPUT/(name+'.png'));print(name,np.asarray(img).mean(axis=(0,1)).tolist())
 return img
if __name__=='__main__':
 render('atmo-preview-space',2.65,0,30,True)
 render('atmo-preview-ground',.006,82,30,False)
 render('atmo-preview-dusk',.006,88,.7,False)
 (ROOT/'Documentation/GLES-Preview-Render-V4_1_2.json').write_text(json.dumps({'renderer':getstr(0x1F01).decode(),'api':getstr(0x1F02).decode(),'glsl_translated_core':'COMPILED_AND_RENDERED','unity_compilation':'NOT_RUN','hlsl_compilation':'NOT_RUN'},indent=2))
