"""Local GLES shader compilation/rendering on Mesa EGL; NOT Unity/HLSL validation."""
import os,ctypes as ct, re,json,math
from pathlib import Path
os.environ['EGL_PLATFORM']='surfaceless';os.environ['LIBGL_ALWAYS_SOFTWARE']='1'
ROOT=Path(__file__).resolve().parents[2]
OUTPUT=ROOT/'Validation/Perf3_2/Artifacts';OUTPUT.mkdir(exist_ok=True)
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
surface=ef('eglCreatePbufferSurface',P,[P,P,ct.POINTER(I)])(D,cfg,(I*5)(0x3057,1280,0x3056,720,0x3038))
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
