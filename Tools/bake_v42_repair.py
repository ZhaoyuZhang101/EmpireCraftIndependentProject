"""Independent V4-2 lookup/noise generator. Requires Python 3 + NumPy.
No external executables, HDRP package, network requests or image generation services.
Outputs raw R/G density data, not sRGB images. Axes: [z,y,x,channel].
"""
from pathlib import Path
import numpy as np, struct, json, hashlib, time
P=Path(__file__).resolve().parents[1]
S=P/'Assets/ORBIS/Resources/ORBIS'
def column(r,mu,hr=.022,hm=.0064,count=64):
    r,mu=np.broadcast_arrays(r,mu); b=r*mu
    end=np.maximum(0,-b+np.sqrt(np.maximum(0,b*b-r*r+1.44)))
    closest=np.clip(-b,0,end); out=np.zeros(r.shape+(2,))
    for side in range(2):
        span=closest if side==0 else end-closest
        for j in range(count):
            a=span*(j/count)**2;z=span*((j+1)/count)**2
            t=closest+(-1 if side==0 else 1)*(a+z)*.5
            h=np.maximum(0,np.sqrt(np.maximum(0,r*r+2*b*t+t*t))-1)
            out[...,0]+=np.exp(-h/hr)*(z-a);out[...,1]+=np.exp(-h/hm)*(z-a)
    return out

def bake_lut():
    w,h=192,96;y,x=np.mgrid[:h,:w];r=1+.2*(y/(h-1))**2
    horizon=-np.sqrt(np.maximum(0,1-1/r**2));mu=horizon+(1-horizon)*(x/(w-1))**2
    data=column(r,mu).astype('<f4')
    raw=struct.pack('<4sHHff',b'OD42',w,h,.022,.0064)+data.tobytes()
    (S/'AtmosphereOpticalDepthV42.bytes').write_bytes(raw)
    return data

def hash01(x,y,z,period,seed):
    # Exactly periodic feature locations, no platform-dependent sine hash.
    x=np.mod(x,period).astype(np.uint32);y=np.mod(y,period).astype(np.uint32);z=np.mod(z,period).astype(np.uint32)
    with np.errstate(over='ignore'):
        h=x*np.uint32(374761393)+y*np.uint32(668265263)+z*np.uint32(2147483647)+np.uint32(seed)
        h=(h^(h>>13))*np.uint32(1274126177);h=h^(h>>16)
    return (h&np.uint32(0xffffff)).astype(np.float64)/16777215

def perlin(p,period,seed):
    xyz=np.floor(p).astype(int); f=p-xyz;u=f**3*(f*(f*6-15)+10);val=np.zeros(p.shape[:-1])
    for z in range(2):
      for y in range(2):
       for x in range(2):
        c=xyz+np.array([x,y,z]);g=np.stack([hash01(*np.moveaxis(c,-1,0),period,seed+s)*2-1 for s in (0,101,307)],-1)
        g/=np.maximum(1e-6,np.linalg.norm(g,axis=-1,keepdims=True))
        weight=(u[...,0] if x else 1-u[...,0])*(u[...,1] if y else 1-u[...,1])*(u[...,2] if z else 1-u[...,2])
        val+=np.sum(g*(f-np.array([x,y,z])),axis=-1)*weight
    return val

def worley(p,period,seed):
    xyz=np.floor(p).astype(int);best=np.full(p.shape[:-1],4.0)
    for z in range(-1,2):
      for y in range(-1,2):
       for x in range(-1,2):
        c=xyz+np.array([x,y,z]);jitter=np.stack([hash01(*np.moveaxis(c,-1,0),period,seed+s) for s in (0,101,307)],-1)
        best=np.minimum(best,np.sum((c+jitter-p)**2,axis=-1))
    return 1-np.clip(np.sqrt(best),0,1)

def noise_channels(uv):
    fbm=perlin(uv*4,4,19871)*.62+perlin(uv*8,8,21871)*.26+perlin(uv*16,16,23871)*.12
    per=np.clip(.5+fbm*.85,0,1)
    puff=worley(uv*4,4,37841)*.7+worley(uv*8,8,39841)*.3
    body=np.clip((per*.68+puff*.32)*1.25+.02,0,1)
    erosion=worley(uv*16,16,2781)*.7+worley(uv*24,24,3781)*.3
    return np.stack([body,erosion,per,np.ones_like(body)],-1)

def bake_noise():
    n=64;data=np.empty((n,n,n,4),dtype=np.uint8)
    for z in range(n):
        y,x=np.mgrid[:n,:n];uv=np.stack([(x+.5)/n,(y+.5)/n,np.full_like(x,(z+.5)/n,dtype=float)],-1)
        data[z]=np.round(noise_channels(uv)*255).astype(np.uint8)
    (S/'CloudNoiseV42.bytes').write_bytes(data.tobytes());return data

if __name__=='__main__':
    t=time.perf_counter();a=bake_lut();b=bake_noise()
    print(json.dumps({'lut_bytes':16+a.nbytes,'noise_bytes':b.nbytes,'seconds':time.perf_counter()-t,
      'noise_R_quantiles':np.quantile(b[...,0]/255,[0,.05,.5,.95,1]).tolist(),
      'lut_finite':bool(np.isfinite(a).all())},indent=2))
