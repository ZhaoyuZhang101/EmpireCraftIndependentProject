#!/usr/bin/env python3
"""Offline regression checks, NOT a C#/HLSL compiler or Unity/GPU test.
Requires Python 3.9+ and numpy. Run: python Validation/validate_v411.py
Numerical tests below are independent CPU references; they do not execute shader code.
"""
from pathlib import Path
import itertools, json, math, re, sys, time, hashlib
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
R=ROOT/'Assets/ORBIS/Resources/ORBIS'
REPORT={'version':'4.1.1','scope':'Source contracts and independent CPU references only',
 'unity_import':'NOT_RUN','csharp_compilation':'NOT_RUN','hlsl_compilation':'NOT_RUN',
 'gpu_rendering':'NOT_RUN','visual_acceptance':'NOT_RUN','cross_platform_build':'NOT_RUN','checks':{}}
def check(name,fn):
 t=time.perf_counter()
 try:
  result=fn() or {}
  REPORT['checks'][name]={'status':'PASS','seconds':round(time.perf_counter()-t,4),**result}
 except Exception as e: REPORT['checks'][name]={'status':'FAIL','error':repr(e)}
def strip(s):
 return re.sub(r'//[^\n]*|/\*.*?\*/','',s,flags=re.S)
def contracts():
 count=0
 for name in ['OrbisAtmosphereCommon.cginc','OrbisCloudCommon.cginc','OrbisOptics.cginc']:
  s=strip((R/name).read_text())
  assert '[unroll' not in s,name
  assert not re.search(r'\b(?:tex2D|tex3D|ddx|ddy)\s*\(',s),name
  assert not re.search(r'\bfloat(?:[234])?\s+\w+\s*\[',s),name
  for m in re.finditer(r'\bfor\s*\(',s):
   assert re.search(r'\[loop\]\s*$',s[:m.start()]),(name,s[m.start()-25:m.start()+30])
   count+=1
 v=(R/'OrbisAtmosphereCommon.cginc').read_text()
 assert 'int lightSteps=clamp(_LightSteps,1,6);' in v
 assert 'float ds=lengthToExit/lightSteps;' in v
 assert 'int sampleCount=clamp(steps,1,64);' in v
 assert 'float ds=(end-begin)/sampleCount;' in v
 c=(R/'CloudShadowCache.shader').read_text()
 assert 'int shadowSteps=clamp(_ShadowSteps,1,32);' in c and 'if(k>=shadowSteps)break;' in c
 return {'explicit_rolled_loops_in_shared_volume_call_graph':count,'implicit_derivative_texture_calls_in_shared_volume_includes':0,'dynamic_local_arrays_in_shared_volume_includes':0}
def sort4(v):
 # Matches the five-comparator network, evaluated at float32 precision.
 a=np.array([min(v[0],v[1]),max(v[0],v[1]),min(v[2],v[3]),max(v[2],v[3])],dtype=np.float32)
 b=np.array([min(a[0],a[2]),min(a[1],a[3]),max(a[0],a[2]),max(a[1],a[3])],dtype=np.float32)
 return np.array([b[0],min(b[1],b[2]),max(b[1],b[2]),b[3]],dtype=np.float32)
def sorting():
 tests=list(itertools.permutations(range(4)))+list(itertools.product([-1,0,1],repeat=4))
 rng=np.random.default_rng(411);tests+=list(rng.uniform(-1e3,1e3,(10000,4)).astype(np.float32))
 for v in tests: assert np.array_equal(sort4(v),np.sort(np.array(v,dtype=np.float32)))
 return {'distinct_permutations':24,'duplicate_cases':81,'random_float32_cases':10000}
def sphere(p,d,r):
 b=float(np.dot(p,d));disc=b*b-float(np.dot(p,p))+r*r
 return (1e8,-1e8) if disc<0 else (-b-math.sqrt(disc),-b+math.sqrt(disc))
def boundaries(p,d,dist,new):
 outer=sphere(p,d,1.2);begin=max(0,outer[0]);end=min(outer[1],dist)
 if end<=begin:return []
 vals=np.array([*sphere(p,d,1.132),*sphere(p,d,1.085)],dtype=np.float32)
 begin=np.float32(begin);end=np.float32(end);vals=np.clip(vals,begin,end)
 if new:return np.r_[begin,sort4(vals),end]
 # Original six-element compare/swap order.
 old=np.r_[begin,end,vals]
 for a in range(5):
  for b in range(a+1,6):
   if old[b]<old[a]:old[a],old[b]=old[b],old[a]
 return old

def intervals():
 rng=np.random.default_rng(4111);hit=0;nonempty=0
 for n in range(10000):
  p=rng.normal(size=3);p=p/np.linalg.norm(p)*rng.uniform(1.0001,3.0)
  d=rng.normal(size=3);d/=np.linalg.norm(d)
  dist=rng.uniform(.00001,5.0)
  ground=sphere(p,d,1)
  if ground[0]>0:dist=min(dist,ground[0])
  old=boundaries(p,d,dist,False);new=boundaries(p,d,dist,True)
  assert np.array_equal(old,new)
  if len(new):
   hit+=1;assert np.all(np.diff(new)>=0);nonempty+=int(np.count_nonzero(np.diff(new)>1e-7))
   assert abs(float(sum(np.diff(new)))-(new[-1]-new[0]))<1e-5
 return {'rays_compared':10000,'atmosphere_hits':hit,'nonempty_intervals':nonempty,'float32_old_new_boundaries_exact':True}
def diffuse(slots,ds,total):
 travel=0.0;result=0.0
 for k,tau in enumerate(slots):
  entry=max(0,total-travel-tau*.5)
  kernel=math.exp(-.05477*(travel+tau*.5))/(1+(k+.5)*ds*22)
  result+=kernel*math.exp(-.001*entry)*(1-math.exp(-.35*entry))*tau
  travel+=tau
 return result

def light_storage():
 rng=np.random.default_rng(412);cases=0
 for steps in range(1,7):
  for n in range(1000):
   data=rng.uniform(0,10,steps);old=np.zeros(6);old[:steps]=data
   first=np.zeros(4);last=np.zeros(2)
   for j,tau in enumerate(data):
    if j<4:first[j]=tau
    else:last[j-4]=tau
   selected=[first[k] if k<4 else last[k-4] for k in range(steps)]
   assert np.array_equal(old[:steps],selected)
   ds=float(rng.uniform(.00001,.15));total=float(sum(data))
   assert diffuse(old[:steps],ds,total)==diffuse(selected,ds,total);cases+=1
 return {'cases':cases,'supported_light_counts':[1,2,3,4,5,6],'storage_and_diffuse_reference_equivalent':True}
def bounds():
 for cap in [6,32,64]:
  for requested in [-100,-1,0,1,2,6,32,64,128,100000]:
   count=min(cap,max(1,requested));length=.231
   assert 1<=count<=cap
   assert math.isclose(sum([length/count]*count),length,rel_tol=1e-12)
 return {'bounded_light_shadow_view_counts':True,'count_and_step_divisor_consistent':True,'note':'A segment with nonpositive requested view steps is still skipped by the production early return.'}
def radial_geometry():
 for height in [1.0002,1.02,1.0849,1.085,1.10,1.132,1.19999,1.2,1.20001,2.0]:
  p=np.array([0.,height,0.]);d=np.array([0.,-1.,0.]);dist=max(0,height-1.0001)
  vals=boundaries(p,d,dist,True)
  if len(vals):
   assert vals[0]>=0 and vals[-1]<=dist+2e-7
   assert abs(float(vals[-1]-vals[0])-(min(height,1.2)-1.0001))<3e-7
 # A point at/above the atmospheric boundary facing out must see an empty interval.
 for radius in [1.2,1.2001,2.0]:assert len(boundaries(np.array([0.,radius,0.]),np.array([0.,1.,0.]),10,True))==0
 return {'camera_heights_tested':10,'boundary_outward_empty_cases':3,'surface_depth_clipping_retained':True}
def pass_wiring():
 c=(R/'Clouds.shader').read_text();runtime=(ROOT/'Assets/ORBIS/Runtime/OrbisAtmosphereRenderer.cs').read_text()
 assert c.count('#pragma target 3.5')==3
 assert re.findall(r'Name "(.*?)"',c)==['MRT_VOLUME','FULL_RES_VOLUME','MAP_VOLUME']
 assert c.count('OrbisMarch(i.uv)')==2 and c.count('OrbisMarchMap(i.uv)')==1
 assert 'Present(source,destination,m,2)' in runtime and 'm.SetPass(0)' in runtime and 'Present(source,destination,m,1)' in runtime
 assert 'float4(OrbisTone(tex2D(_MainTex,i.uv).rgb*r.transmission+r.light),1)' in c
 assert 'OrbisMarchRay(up*ORBIS_AIR_RADIUS,-up' in (R/'OrbisAtmosphereCommon.cginc').read_text()
 return {'cloud_pass_indices':[0,1,2],'shader_target':'3.5 unchanged','MRT_full_resolution_atlas_routes_retained':True}
def native_validator():
 s=(ROOT/'Assets/ORBIS/Editor/OrbisV411ShaderValidation.cs').read_text()
 for text in ['ShaderUtil.CompilePass(material,i,true)','ShaderUtil.IsPassCompiled(material,i)',
              'ShaderUtil.ShaderHasError(shader)','ShaderUtil.GetShaderMessages(shader)',
              'ShaderUtil.allowAsyncCompilation=oldAsync','GraphicsDeviceType.Null',
              'item.loaded&&item.supported&&!item.hasErrors&&everyPass']:
  assert text in s,text
 return {'force_synchronous_pass_compilation_requested_in_source':True,'native_check_actually_executed_here':False}
def meta_and_fonts():
 guids=[]
 for p in (ROOT/'Assets').rglob('*'):
  if p.suffix=='.meta':
   m=re.search(r'^guid: ([0-9a-f]{32})$',p.read_text(),re.M);assert m,p;guids.append(m[1])
  else:assert p.with_name(p.name+'.meta').exists(),p
 assert len(guids)==len(set(guids))
 assert not [p for p in ROOT.rglob('*') if p.suffix.lower() in {'.ttf','.otf','.ttc','.otc','.woff','.woff2'}]
 return {'unique_meta_guids':len(guids),'font_binaries':0}
for name,fn in [('volume_loop_source_contracts',contracts),('five_comparator_sort_reference',sorting),
 ('old_new_interval_equivalence',intervals),('six_slot_light_storage_reference',light_storage),
 ('sample_count_bounds',bounds),('ground_space_boundary_reference',radial_geometry),
 ('three_cloud_pass_source_wiring',pass_wiring),('native_validator_source_contract',native_validator),
 ('resource_meta_and_no_fonts',meta_and_fonts)]:check(name,fn)
REPORT['passed']=sum(c['status']=='PASS' for c in REPORT['checks'].values());REPORT['failed']=len(REPORT['checks'])-REPORT['passed']
(ROOT/'Documentation/Validation-V4_1_1.json').write_text(json.dumps(REPORT,ensure_ascii=False,indent=2))
print(json.dumps(REPORT,ensure_ascii=False,indent=2))
sys.exit(int(REPORT['failed']>0))
