"""Execute the mechanically translated optical core on EGL GLES float targets.
CPU llvmpipe, NOT Unity ShaderLab/HLSL or hardware GPU validation.
"""
import sys,ctypes as ct,math,json,hashlib
from pathlib import Path
import numpy as np
import gles_preview_renderer as e
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'Validation'));import atmosphere_reference as a
f=e.f;I=e.I;U=e.U;P=e.P
core=(root/'Validation/glsl_port_core.glsl').read_text()
frag='#version 300 es\nprecision highp float; precision highp int;\n'+core+'\nuniform vec3 testPoint,testSun; uniform int testMode;out vec4 color;void main(){if(testMode==0)color=vec4(OrbisGLSLSunTransmittance(testPoint,testSun),1);else if(testMode==1)color=vec4(OrbisGLSLDensity(testPoint),1);else color=vec4(OrbisGLSLPhase(testPoint.x),0,1);}'
pr=f('glCreateProgram',U,[])();at=f('glAttachShader',None,[U,U]);at(pr,e.shader(0x8B31,e.vs));at(pr,e.shader(0x8B30,frag));f('glLinkProgram',None,[U])(pr);ok=I();f('glGetProgramiv',None,[U,U,ct.POINTER(I)])(pr,0x8B82,ct.byref(ok));assert ok.value
f('glUseProgram',None,[U])(pr);e.program=pr
for k,v in {'_Atmosphere':1,'_AirDensity':1,'_Haze':1,'_SolarEnergy':1}.items():e.uniform(k,v)
e.uniform('_AtmoBetaRayleigh',1.375,3.25,5.6,.032);e.uniform('_AtmoBetaMie',3.15,3.15,3.15,.008);e.uniform('_AtmoBetaAbsorption',10.2,24.85,.975,.06);e.uniform('_AtmoParameters',.008,.7,12,16)
tex=U();f('glGenTextures',None,[I,ct.POINTER(U)])(1,ct.byref(tex));f('glBindTexture',None,[U,U])(0x0DE1,tex);f('glTexImage2D',None,[U,I,I,I,I,I,U,U,P])(0x0DE1,0,0x8814,1,1,0,0x1908,0x1406,None)
f('glTexParameteri',None,[U,U,I])(0x0DE1,0x2801,0x2600);f('glTexParameteri',None,[U,U,I])(0x0DE1,0x2800,0x2600)
fb=U();f('glGenFramebuffers',None,[I,ct.POINTER(U)])(1,ct.byref(fb));f('glBindFramebuffer',None,[U,U])(0x8D40,fb);f('glFramebufferTexture2D',None,[U,U,U,U,I])(0x8D40,0x8CE0,0x0DE1,tex,0);assert f('glCheckFramebufferStatus',U,[U])(0x8D40)==0x8CD5
f('glViewport',None,[I,I,I,I])(0,0,1,1)
read=f('glReadPixels',None,[I,I,I,I,U,U,P]);draw=f('glDrawArrays',None,[U,I,I]);buf=(ct.c_float*4)()
def eval_(mode,p,s=(0,0,1)):
 e.ui(e.getloc(pr,b'testMode'),mode);e.uniform('testPoint',*p);e.uniform('testSun',*s);draw(4,0,3);read(0,0,1,1,0x1908,0x1406,buf);assert not f('glGetError',U,[])();return np.array(buf[:3])
errs={'sun_transmission':0.,'density':0.,'phase':0.};counts={'sun_transmission':0,'density':0,'phase':0}
for h in [0,.0002,.006,.02,.06,.1,.15,.1999]:
 for deg in np.linspace(-30,90,25):
  sun=np.array([math.cos(math.radians(deg)),0,math.sin(math.radians(deg))]);p=np.array([0,0,1+h]);value=eval_(0,p,sun);ref=a.sun_trans(p,sun)
  errs['sun_transmission']=max(errs['sun_transmission'],float(max(abs(value-ref))));counts['sun_transmission']+=1
for h in np.linspace(.00001,.22,100):
 p=np.array([0,0,1+h]);v=eval_(1,p);ref=a.density(p);errs['density']=max(errs['density'],float(max(abs(v-ref))));counts['density']+=1
for mu in np.linspace(-1,1,101):
 v=eval_(2,[mu,0,0]);ref=a.phase(mu);errs['phase']=max(errs['phase'],float(max(abs(v[:2]-ref))));counts['phase']+=1
assert max(errs.values())<2e-4,errs
r={'scope':'Mechanically translated HLSL optical core -> GLSL ES float-target execution; not Unity or HLSL compilation','renderer':e.getstr(0x1F01).decode(),'api':e.getstr(0x1F02).decode(),'counts':counts,'max_absolute_float32_vs_float64_error':errs,'result':'PASS','core_sha256':hashlib.sha256(core.encode()).hexdigest(),'unity_compile':'NOT_RUN','webgl_browser_runtime':'UNAVAILABLE_CONTEXT'}
(root/'Documentation/GLES-Core-Execution-V4_1_2.json').write_text(json.dumps(r,indent=2)+'\n');print(json.dumps(r,indent=2))
