"""Numeric projection/cover regression using shipped coefficients and actual lookup bytes.
This is an offline algorithm test, not Unity/C# execution.
"""
from pathlib import Path
import json,math,cmath,re
import numpy as np
R=Path(__file__).resolve().parents[2];O=Path(__file__).resolve().parent
s=(R/'Assets/ORBIS/Runtime/Planet/MarkleyProjection.cs').read_text()
def coefficients(name):return [eval(x,{'__builtins__':{}},{}) for x in re.search(r'double\[\] '+name+r'=\{(.*?)\}',s)[1].split(',')]
G,H=coefficients('G'),coefficients('H');faces=json.loads((R/'Assets/ORBIS/Resources/ORBIS/Markley/faces.json').read_text())['faces']
def lee(z):
 n=abs(z);w=complex(-.5,math.sqrt(3)/2);rot=max([1,w,w*w],key=lambda r:(r*z).real);h=k=0j
 if n>.3:
  y=1-rot*z;g=0j
  for c in G[::-1]:g=c+g*y
  k=(1.4021821053254548-cmath.sqrt(y)*g)*rot*rot
 if n<.5:
  for c in H[::-1]:h=c+h*z**3
  h*=z
 return h if n<.3 else k if n>.5 else k*((n-.3)/.2)+h*(1-(n-.3)/.2)
def vector(lon,lat):return np.array([math.cos(lat)*math.sin(lon),math.sin(lat),math.cos(lat)*math.cos(lon)])
def forward(lon,lat):
 n=vector(lon,lat);f=max(faces,key=lambda f:np.dot(f['matrix'][:3],n));a=np.array(f['matrix']).reshape(3,3)@n;q=lee(complex(a[1],a[2])*math.sqrt(2)/(1+a[0]));b=f['affine'];x=b[0]*q.real+b[1]*q.imag+b[2];y=b[3]*q.real+b[4]*q.imag+b[5]
 if y>=0:
  if x>3:x-=8
  y=-y
 else:x=4-x if x>1 else -4-x
 return np.array([((x+5)%8)/8,max(0,min(1,(y+2*math.sqrt(3))/(2*math.sqrt(3))))],dtype=np.float32).astype(float)
T=np.fromfile(R/'Assets/ORBIS/Resources/ORBIS/Markley/inverse.bytes',dtype='<f4').reshape(445,1025,4).astype(float)
def fold(u,v):
 row=math.floor(v);k=math.floor(row/2);odd=row-2*k;local=v-row
 return ((.75-u-k*.5 if odd else u+k*.5)%1,1-local if odd else local)
def inverse(u,v):
 u,v=fold(u,v);x=u*1024;y=v*444;ix=min(1023,int(x));iy=min(443,int(y));a=x-ix;b=y-iy
 n=(T[iy,ix,:3]*(1-a)+T[iy,ix+1,:3]*a)*(1-b)+(T[iy+1,ix,:3]*(1-a)+T[iy+1,ix+1,:3]*a)*b
 return n/np.linalg.norm(n)
refs=json.loads((R/'Tools/Markley/reference-points.json').read_text());forward_err=roundtrip=0
for lon,lat,u,v in refs:
 p=forward(lon,lat);du=(p[0]-u+.5)%1-.5;forward_err=max(forward_err,math.hypot(du,p[1]-v));roundtrip=max(roundtrip,np.linalg.norm(inverse(*p)-vector(lon,lat)))
rng=np.random.default_rng(35);deck=rebase=0
for u,v in rng.uniform(-100,100,(5000,2)):
 a=inverse(u,v);deck=max(deck,np.linalg.norm(a-inverse(u+1,v)),np.linalg.norm(a-inverse(u+.5,v+2)),np.linalg.norm(a-inverse(u,v+4)))
 k=math.floor(v/2);rebase=max(rebase,np.linalg.norm(a-inverse((u+k*.5)%1,v-2*k)))
edge=max(np.max(np.abs(T[:,0,:]-T[:,-1,:])),max(np.linalg.norm(inverse(u,0)-inverse(.25-u,0)) for u in np.linspace(0,1,1001)),max(np.linalg.norm(inverse(u,1)-inverse(.75-u,1)) for u in np.linspace(0,1,1001)))
assert forward_err<1e-6,forward_err
assert roundtrip<.00015,roundtrip
assert deck<1e-10 and rebase<1e-10 and edge<1e-10,(deck,rebase,edge)
report=dict(status='PASS',source_forward_points=len(refs),source_forward_max_uv_error=forward_err,table_max_unit_vector_error=roundtrip,deck_transform_max_error=deck,rebase_max_error=rebase,boundary_max_error=edge,unity_executed=False,native_csharp_executed=False)
(O/'markley-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
