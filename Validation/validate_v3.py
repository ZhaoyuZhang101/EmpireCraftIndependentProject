#!/usr/bin/env python3
"""Offline V3 structural/numerical checks. Does not compile C#/ShaderLab or launch Unity.
Run: python Validation/validate_v3.py    Requires numpy and scipy.
"""
from pathlib import Path
import json,re,math,time,hashlib,xml.etree.ElementTree as ET,sys
import numpy as np
from reference_geometry import grid,Sampler,norm,lexical_check
ROOT=Path(__file__).resolve().parents[1]
REPORT={'scope':'Independent Python numerical reference and source-wiring checks only',
 'unity_import':'NOT_RUN','csharp_compilation':'NOT_RUN','shader_compilation':'NOT_RUN',
 'play_mode':'NOT_RUN','gpu_profiling':'NOT_RUN','desktop_build':'NOT_RUN','wall_clock_soak':'NOT_RUN','checks':{}}
def test(name,fn):
 start=time.perf_counter()
 try:
  metrics=fn() or {};REPORT['checks'][name]={'status':'PASS','seconds':round(time.perf_counter()-start,4),**metrics}
 except Exception as e:
  REPORT['checks'][name]={'status':'FAIL','error':repr(e)}
  print(name,'FAIL',e)
def source():
 paths=[p for p in (ROOT/'Assets').rglob('*') if p.suffix in {'.cs','.shader','.cginc'}]
 for p in paths:
  try:lexical_check(p.read_text())
  except Exception as e:raise AssertionError(str(p.relative_to(ROOT))+': '+str(e))
  for name in re.findall(r'#include "(Orbis[^"\n]+)"',p.read_text()):assert (p.parent/name).is_file(),name
 return {'files':len(paths),'qualification':'Delimiter/include checks, NOT a C# or shader compiler'}
def ui():
 tree=ET.parse(ROOT/'Assets/ORBIS/Resources/ORBIS/Orbis.uxml');ids=[e.attrib['name'] for e in tree.iter() if 'name'in e.attrib]
 assert len(ids)==len(set(ids))
 hud=(ROOT/'Assets/ORBIS/Runtime/OrbisHud.cs').read_text()
 for n in re.findall(r'(?:Bind|Q(?:<[^>]+>)?)\("([^"+]+)"[),]',hud):assert n in ids,n
 for i in range(13):assert f'layer{i}' in ids
 for i in range(6):assert f'tool{i}' in ids
 assert 'NotoSansCJKtc-Regular.otf' not in (ROOT/'Assets/ORBIS/Resources/ORBIS/Orbis.uss').read_text()
 return {'named_elements':len(ids),'qualification':'XML and binding names only; USS/layout need Unity import'}
def resources():
 for p in (ROOT/'Assets').rglob('*.cs'):
  for a in re.findall(r'Resources.Load<[^>]+>\("([^"+]+)"\)',p.read_text()):
   q=ROOT/'Assets/ORBIS/Resources'/a
   assert any(q.parent.glob(q.name+'.*')),str(p)+': '+a
 names=['OrbisAtmosphereRenderer','OrbisTransportView','OrbisSurfaceLOD']
 runtime='\n'.join(p.read_text() for p in (ROOT/'Assets/ORBIS/Runtime').glob('*.cs'))
 for n in names:assert 'AddComponent<'+n+'>'in runtime
 assert 'SetUVs(2,water)'in runtime and'SetUVs(3,flows)'in runtime
 assert 'BindSurfaceSettings(material,clock,sun,true)'in runtime
 assert 'void SetWeatherUniforms' not in runtime
 return {'new_components_wired':names}
def metadata():
 guids=[]
 for p in (ROOT/'Assets').rglob('*.meta'):
  m=re.search(r'^guid: ([0-9a-f]{32})$',p.read_text(),re.M);assert m,str(p);guids.append(m[1])
 assert len(guids)==len(set(guids))
 scene=(ROOT/'Assets/ORBIS/Scenes/OrbisDemo.unity').read_text()
 for guid in re.findall(r'guid: ([0-9a-f]{32})',scene):assert guid in guids,guid
 assert not list(ROOT.rglob('*.ttf')) and not list(ROOT.rglob('*.otf')) and not list(ROOT.rglob('*.woff*'))
 return {'unique_guids':len(guids),'font_binaries':0}
def architecture():
 runtime=ROOT/'Assets/ORBIS/Runtime'
 world=(runtime/'OrbisWorld.cs').read_text();weather=(runtime/'OrbisWeather.cs').read_text();camera=(runtime/'OrbisDemo.cs').read_text()
 assert 'cloudDrift+='not in world and 'p*4.3f-c.cloudDrift'not in world
 assert 'if(Weather==null)tiles[i].cloud'in world and 'Weather?.Advance'in world
 assert 'Vector3.ProjectOnPlane(t.wind,p)'in weather and '(t.pressure-1013)/25f'in weather
 assert 'RaycastSurface(new Ray(origin,direction),out p)'in camera and 'altitude * Mathf.Exp'in camera
 assert '"orbis-demo-v3.json"'in camera and '"orbis-demo-v2.json"'in camera
 assert 'OrbisSaveMigration.Read'in camera
 migration=(runtime/'OrbisSaveMigration.cs').read_text()
 assert 'Has(json,"weatherTime")&&Has(json,"cloudWater")'in migration
 assert 'p.surfaceLOD=true'in migration
 surface=(ROOT/'Assets/ORBIS/Resources/ORBIS/Surface.shader').read_text()
 assert surface.count('OrbisPatchClip(')==2 and 'OrbisShadeWater('in surface
 volume=(ROOT/'Assets/ORBIS/Resources/ORBIS/OrbisAtmosphereCommon.cginc').read_text()
 for t in ['result.transmission*=stepT','source/max(sigma','float cuts[6]','result.sceneDepth*length(ray)']:
  assert t in volume,t
 return {'simulation_tiles':3456,'terrain_depth_and_color_use_same_LOD_mask':True}
def interpolation():
 p,t=grid(24);sample=Sampler(p,t);rng=np.random.default_rng(733)
 q=norm(rng.normal(size=(12000,3)));q=np.vstack([q,[[0,1,0],[0,-1,0]]])
 ids,w=sample.locate(q)
 assert w.min()>=0 and np.max(abs(w.sum(1)-1))<1e-12
 reconstructed=norm(np.einsum('nk,nkj->nj',w,p[ids]));err=float(np.max(np.linalg.norm(reconstructed-q,axis=1)))
 assert err<1e-9
 constant=np.ones(len(p))*1.234;assert np.max(abs((constant[ids]*w).sum(1)-1.234))<1e-12
 return {'directions':len(q),'vertices':len(p),'triangles':len(t),'max_direction_error_float64':err}
def transfer():
 rng=np.random.default_rng(184);v=rng.uniform(0,3,100000);c=rng.uniform(0,2,100000);s=rng.uniform(.16,1.35,100000);dt=.1
 original=v+c
 cond=np.maximum(0,v-s)*(1-np.exp(-dt*.9));v-=cond;c+=cond
 ev=np.minimum(c,np.maximum(0,s-v)*(1-np.exp(-dt*.55)));c-=ev;v+=ev
 age=c*(1-np.exp(-dt/75));c-=age;v+=age
 rain=np.maximum(0,c-.2)*(1-np.exp(-dt*.3));c-=rain
 err=float(np.max(abs(v+c+rain-original)));assert err<3e-15 and c.min()>=0 and v.min()>=0
 return {'samples':100000,'reservoir_balance_error':err,'qualification':'Only local reaction balance; advection is NOT globally mass-conservative'}
def bounded():
 clock=np.array([0,11.999999,12,720,10800,86400,1e8,1e12],dtype=np.float64)
 phase=(clock/12)%1;ages=(np.stack([phase,(phase+.5)%1],axis=1)-.5)*12
 assert np.max(abs(ages))<=6
 waves=np.remainder(clock[:,None]*2*1.07**np.arange(10)[None,:],2*np.pi)
 assert np.isfinite(waves).all() and waves.max()<2*np.pi
 # reset age has zero weight on its own reset boundary
 def blended(t):
  a=(t/12)%1;b=(a+.5)%1;w=.5-.5*np.cos(a*2*np.pi)
  return np.sin((b-.5)*12)*(1-w)+np.sin((a-.5)*12)*w
 error=abs(blended(12-1e-7)-blended(12+1e-7));assert error<3e-7
 return {'max_detail_age_seconds':float(np.max(abs(ages))),'reset_continuity_error':float(error),'max_clock_seconds':1e12}
def sphere_roots(o,d,r):
 b=float(o@d);disc=b*b-float(o@o)+r*r
 return (1e8,-1e8) if disc<0 else(-b-math.sqrt(max(0,disc)),-b+math.sqrt(max(0,disc)))
def segments():
 rng=np.random.default_rng(17);n=0;maxerror=0
 for radius in [1.003,1.08,1.10,1.15,1.20,3.6,6.5]:
  for _ in range(450):
   ro=norm(rng.normal(size=3))*radius;rd=norm(-ro+rng.normal(size=3)*.7)
   outer=sphere_roots(ro,rd,1.2);begin=max(0,outer[0]);end=min(outer[1],rng.uniform(.01,10))
   planet=sphere_roots(ro,rd,1)
   if planet[0]>0:end=min(end,planet[0])
   if end<=begin:continue
   cuts=sorted([begin,end,*np.clip(sphere_roots(ro,rd,1.132),begin,end),*np.clip(sphere_roots(ro,rd,1.085),begin,end)])
   assert min(cuts)>=0 and max(cuts)<=end+1e-12
   lengths=np.diff(cuts);assert lengths.min()>=0
   error=abs(sum(lengths)-(end-begin));maxerror=max(maxerror,error)
   assert error<1e-12
   for a,b in zip(cuts[:-1],cuts[1:]):
    if b-a<1e-7:continue
    radii=np.linalg.norm(ro+rd*np.array([a+(b-a)*.2,a+(b-a)*.8])[:,None],axis=1)
    cls=(radii>1.085)&(radii<1.132)
    assert cls[0]==cls[1],(ro,rd,cuts,radii)
   n+=1
 return {'random_rays_partitioned':n,'max_partition_error':maxerror,'includes_camera_inside_clouds':True}
def homogeneous():
 rng=np.random.default_rng(81);sigma=rng.uniform(.001,30,(10000,3));source=rng.uniform(0,8,(10000,3));length=rng.uniform(0,2,(10000,1))
 expected_t=np.exp(-sigma*length);expected_l=source/sigma*(1-expected_t)
 T=np.ones_like(sigma);L=np.zeros_like(sigma);dt=length/48;stepT=np.exp(-sigma*dt)
 for _ in range(48):L+=T*(source/sigma)*(1-stepT);T*=stepT
 error=float(max(abs(T-expected_t).max(),abs(L-expected_l).max()));assert error<1e-10
 assert T.min()>=0 and T.max()<=1
 return {'homogeneous_media':10000,'max_quadrature_error':error,'RGB_transmittance':True}
def tangent():
 # Gnomonic plane parameterization used by both mesh and local grid.
 normals=np.array([[0,1,0],[0,-1,0],[1,0,0],[0,0,1],norm(np.array([1,1,1]))]);max_error=0
 for n in normals:
  reference=np.array([0,0,1])if abs(n[1])>.93 else np.array([0,1,0]);right=norm(np.cross(reference,n));up=np.cross(n,right)
  for size in [.015,.03,.06,.12,.24]:
   q=np.linspace(-size,size,65);xx,yy=np.meshgrid(q,q);p=norm(n+xx[...,None]*right+yy[...,None]*up)
   denom=p@n;uv=np.stack([p@right/denom,p@up/denom],axis=-1)
   error=float(np.max(abs(uv-np.stack([xx,yy],-1))));max_error=max(max_error,error)
   assert error<1e-12
 # Sphere metric is not flat. Report distortion instead of asserting globally perfect squares.
 return {'tangent_frames':5,'footprints_per_frame':5,'inverse_error':max_error,'global_equal_squares_claim':False,
   'note':'Uniform local plane coordinates; projected spherical cells are only approximately undistorted near the center.'}
def dither():
 x,y=np.meshgrid(np.arange(320),np.arange(180));d=np.mod(52.9829189*np.mod(x*.06711056+y*.00583715,1),1)
 for weight in np.linspace(0,1,101):
  coarse=d>=weight;patch=d<weight
  assert not (coarse&patch).any()
  # The two predicates must cover every sample exactly once.
  holes=(~coarse&~patch).sum();assert holes==0
 return {'tested_pixels':int(d.size*101),'same_depth_color_rule':True}
def camera_safety():
 heights=np.linspace(.9983,1.086,10000);alt=np.geomspace(.003,5.5,10000)
 radius=heights+alt;near=np.clip(alt*.08,.00008,.01)
 assert np.all(radius>heights) and np.all(near<alt)
 # Reproduce positive near root failure of old code and new max(0,entry).
 entry,exit=sphere_roots(np.array([0.,0.,1.025]),np.array([0.,0.,-1.]),1.12)
 assert entry<0<exit and max(0,entry)==0
 return {'altitude_samples':10000,'inside_bounding_sphere_case_accepted':True,'minimum_altitude_R':.003}
for name,fn in [('source_structure',source),('ui_bindings',ui),('resources_and_components',resources),('asset_metadata',metadata),
 ('merge_architecture',architecture),('spherical_interpolation',interpolation),('reaction_balance',transfer),('bounded_phases',bounded),
 ('atmosphere_shell_partition',segments),('RGB_volume_integration',homogeneous),('local_tangent_grid',tangent),('LOD_complementarity',dither),('camera_altitude',camera_safety)]:test(name,fn)
REPORT['passed']=sum(x['status']=='PASS'for x in REPORT['checks'].values());REPORT['failed']=sum(x['status']=='FAIL'for x in REPORT['checks'].values())
(ROOT/'Documentation/Validation-V3.json').write_text(json.dumps(REPORT,ensure_ascii=False,indent=2))
print(json.dumps(REPORT,ensure_ascii=False,indent=2));sys.exit(1 if REPORT['failed'] else 0)
