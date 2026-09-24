"""Deterministic periodic 64³ RGBA volume; numerical density data, not terrain artwork."""
from pathlib import Path
import numpy as np
N=64
rng=np.random.default_rng(0x0B815)
z,y,x=np.indices((N,N,N),dtype=np.float32)

def value_noise(cells):
    lattice=rng.random((cells,cells,cells)).astype(np.float32)
    q=[a*(cells/N) for a in (z,y,x)]
    low=[np.floor(a).astype(np.int32) for a in q]
    f=[a-i for a,i in zip(q,low)]
    f=[a*a*a*(a*(a*6-15)+10) for a in f]
    result=np.zeros_like(x)
    for iz in (0,1):
      for iy in (0,1):
       for ix in (0,1):
        result+=lattice[(low[0]+iz)%cells,(low[1]+iy)%cells,(low[2]+ix)%cells]*(f[0] if iz else 1-f[0])*(f[1] if iy else 1-f[1])*(f[2] if ix else 1-f[2])
    return result

def cellular(cells):
    features=rng.random((cells,cells,cells,3)).astype(np.float32)
    q=np.stack((z,y,x),-1)*(cells/N)
    lo=np.floor(q).astype(np.int32)
    d=np.full((N,N,N),100,dtype=np.float32)
    for iz in (-1,0,1):
     for iy in (-1,0,1):
      for ix in (-1,0,1):
       candidate=lo+np.array((iz,iy,ix))
       feature=candidate+features[candidate[...,0]%cells,candidate[...,1]%cells,candidate[...,2]%cells]
       d=np.minimum(d,((q-feature)**2).sum(-1))
    return 1-np.minimum(1,np.sqrt(d)/1.15)

shape=value_noise(4)*.6+value_noise(8)*.28+value_noise(16)*.12
billow=cellular(6)
erosion=cellular(12)
fine=value_noise(24)
volume=np.stack((shape,billow,erosion,fine),-1)
out=Path(__file__).resolve().parents[1]/'Assets/ORBIS/Resources/ORBIS/CloudNoise.bytes'
out.write_bytes(np.rint(np.clip(volume,0,1)*255).astype(np.uint8).tobytes())
print(f'{out.name}: {out.stat().st_size} bytes, 64³ RGBA, periodic density and erosion channels')
