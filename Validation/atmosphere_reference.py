"""Independent float64 reference of the uploaded-GLSL port. Not execution of HLSL."""
import numpy as np
import math
RAY=np.array([1.375,3.25,5.6]);MIE=np.full(3,3.15);OZONE=np.array([10.2,24.85,.975])

def sphere(p,d,r):
 b=float(np.dot(p,d));disc=b*b-float(np.dot(p,p))+r*r
 if disc<0:return np.array([1e8,-1e8])
 return np.array([-b-math.sqrt(disc),-b+math.sqrt(disc)])

def visible(p,sun):
 r=np.linalg.norm(p)
 if r<.999999:return False
 hit=sphere(p+p/max(r,1e-6)*.00001,sun,1)
 return not(hit[1]>0 and hit[0]>=0)

def density(p,air=1):
 p=np.asarray(p);h=np.linalg.norm(p,axis=-1)-1
 ray=np.exp(-np.maximum(0,h)/.032);mie=np.exp(-np.maximum(0,h)/.008)
 ozone=ray/(1+((.06-h)/.008)**2)
 t=np.clip((h-.188)/.012,0,1);top=1-t*t*(3-2*t)
 return np.stack([ray,mie,ozone],axis=-1)*(top*air*((h>=0)&(h<.2)))[...,None]

def extinction(p,air=1,haze=1):
 r=density(p,air);return r[...,0,None]*RAY+r[...,1,None]*MIE*haze+r[...,2,None]*OZONE

def phase(mu,g=.7):
 mu=np.clip(mu,-1,1);g=np.clip(g,-.95,.95)
 return np.stack([3/(16*np.pi)*(1+mu*mu),3/(8*np.pi)*(1-g*g)*(1+mu*mu)/((2+g*g)*np.maximum(1e-4,1+g*g-2*g*mu)**1.5)],axis=-1)

def sun_trans(p,sun,budget=16,air=1,haze=1):
 p=np.asarray(p,dtype=float);sun=np.asarray(sun,dtype=float)
 if not visible(p,sun):return np.zeros(3)
 if air<=0:return np.ones(3)
 outer=sphere(p,sun,1.2);begin=max(0,outer[0]);end=outer[1]
 if end<=begin:return np.ones(3)
 closest=np.clip(-np.dot(p,sun),begin,end);tau=np.zeros(3)
 for side,span in enumerate([closest-begin,end-closest]):
  if span<1e-7:continue
  count=max(4,math.ceil(budget*span/max(end-begin,1e-6)))
  u=np.arange(count+1,dtype=float)/count;a=span*u[:-1]**2;b=span*u[1:]**2
  t=closest+(-1 if side==0 else 1)*.5*(a+b)
  tau+=(extinction(p[None,:]+sun[None,:]*t[:,None],air,haze)*(b-a)[:,None]).sum(axis=0)
 return np.exp(-np.clip(tau,0,60))

def interval(ro,rd,maximum=100):
 outer=sphere(ro,rd,1.2);begin=max(0,outer[0]);end=min(outer[1],maximum)
 g=sphere(ro,rd,1)
 if g[1]>0 and g[0]>=0:end=min(end,g[0])
 if np.dot(ro,ro)<.999998:return 1.,0.
 return begin,end

def march(ro,rd,sun,steps=64,air=1):
 ro=np.asarray(ro,dtype=float);rd=np.asarray(rd,dtype=float);sun=np.asarray(sun,dtype=float)
 a,b=interval(ro,rd);L=np.zeros(3);T=np.ones(3)
 if b<=a:return L,T
 ds=(b-a)/steps;ph=phase(np.dot(rd,sun))
 for j in range(steps):
  p=ro+rd*(a+(j+.5)*ds);rho=density(p,air);sig=extinction(p,air);st=sun_trans(p,sun,air=air)
  source=(rho[0]*RAY*ph[0]+rho[1]*MIE*ph[1])*st*12
  removed=-np.expm1(-np.minimum(sig*ds,60))
  L+=T*source*removed/np.maximum(sig,1e-8);T*=1-removed
 return L,T
