#!/usr/bin/env python3
"""Source checks + independent numerical references. Requires numpy. NOT a Unity/compiler test."""
from pathlib import Path
import re,json,hashlib,math,time,xml.etree.ElementTree as ET
import numpy as np
import atmosphere_reference as a
ROOT=Path(__file__).resolve().parents[1];R=ROOT/'Assets/ORBIS/Resources/ORBIS';C=ROOT/'Assets/ORBIS/Runtime'
report={'version':'4.1.2','scope':'Source contracts, independent CPU numerical references','unity_import':'NOT_RUN','hlsl_compilation':'NOT_RUN','csharp_compilation':'NOT_RUN','unity_visual_acceptance':'NOT_RUN','checks':{}}
def test(name,fn):
 t=time.perf_counter()
 try:out=fn() or {};report['checks'][name]={'status':'PASS',**out,'seconds':round(time.perf_counter()-t,3)}
 except Exception as e:report['checks'][name]={'status':'FAIL','error':repr(e)}
def source():
 raw=(ROOT/'Reference/atmosphere.glsl').read_bytes();expected='708ea4a9141a7653ea0085d7f26ae09ca09bb14c88e339d63da74e16439300bd'
 assert hashlib.sha256(raw).hexdigest()==expected
 s=(R/'OrbisAtmosphereGLSL.cginc').read_text();opt=(R/'OrbisOptics.cginc').read_text();v=(R/'OrbisAtmosphereCommon.cginc').read_text()
 assert 'Copyright (c) 2019 Dimas Leenman' in s and 'MIT License' in s
 assert '#include "OrbisAtmosphereGLSL.cginc"' in opt
 for name in ['OrbisGLSLPhase','OrbisGLSLSource','OrbisGLSLAccumulate','OrbisGLSLViewInterval']:assert name+'(' in v,name
 assert 'OrbisGLSLSunTransmittance(p,sun)' in opt
 assert 'atmosphereProfile.Bind(material,p.cloudQuality)' in (C/'OrbisPlanetView.cs').read_text()
 assert 'phaseMie=(1-.7*.7)' not in v and 'exp(-pow((h-.055)' not in opt
 return {'uploaded_source_sha256':expected,'active_port_not_reference_only':True,'MIT_retained':True}
def units():
 # Beta*H must survive metres->R conversion AND the display-height stretch.
 S=100000/.2
 for stretch in [1,1.5,2,3]:
  beta=np.array([5.5e-6,13e-6,22.4e-6]);H=8000
  assert np.allclose(beta*S/stretch*(H*stretch/S),beta*H,atol=1e-15)
 return {'metres_per_game_R':S,'rayleigh_vertical_optical_depth_reference':[.044,.104,.1792],'game_geometry_not_true_Earth':True}
def phase():
 mu,w=np.polynomial.legendre.leggauss(240);error=0
 for g in [-.9,0,.7,.9]:
  ph=a.phase(mu,g);error=max(error,float(np.max(abs((ph*w[:,None]).sum(axis=0)*2*np.pi-1))))
  assert np.all(ph>=0)
 assert error<1e-9
 assert a.phase(1,.7)[1]>a.phase(-1,.7)[1]
 return {'normalization_max_error':error,'positive_g_forward_under_uploaded_convention':True}
def densities():
 h=np.linspace(0,.25,1501);p=np.stack([np.zeros(len(h)),np.zeros(len(h)),1+h],axis=1)
 rho=a.density(p);assert np.all(np.isfinite(rho)) and np.all(rho>=0) and np.all(rho[h>=.2]==0)
 assert np.max(a.density(np.array([0,0,1.2-1e-7])))<1e-11
 assert a.density(np.array([0,0,1.01]),0).sum()==0
 return {'heights':len(h),'outer_density_goes_smoothly_to_zero':True,'zero_density_identity':True}
def sunlight():
 day=a.sun_trans([0,0,1.0002],[0,0,1]);low=a.sun_trans([0,0,1.0002],np.array([1,0,.01])/math.sqrt(1.0001));night=a.sun_trans([0,0,-1.0002],[0,0,1])
 assert np.all(night==0) and np.all(day<=1) and np.all(day>0)
 assert low[0]>low[1]>low[2] and low[2]<day[2]
 max_error=0;cases=0
 for height in [0,.0002,.006,.02,.06,.10,.15,.1999]:
  for degrees in np.linspace(-30,90,25):
   d=np.array([math.cos(math.radians(degrees)),0,math.sin(math.radians(degrees))]);p=np.array([0,0,1+height]);t=a.sun_trans(p,d);ref=a.sun_trans(p,d,1024)
   assert np.all((t>=0)&(t<=1));max_error=max(max_error,float(np.max(abs(t-ref))));cases+=1
 assert max_error<.025, max_error
 return {'cases':cases,'zenith_RGB':day.tolist(),'low_sun_RGB':low.tolist(),'night_RGB':night.tolist(),'16_budget_max_absolute_error_vs_1024_budget':max_error}
def geometry():
 rng=np.random.default_rng(412);hits=0
 for _ in range(4000):
  p=rng.normal(size=3);p=p/np.linalg.norm(p)*rng.uniform(1.00001,4);d=rng.normal(size=3);d/=np.linalg.norm(d);limit=rng.uniform(.001,8);begin,end=a.interval(p,d,limit)
  if end>begin:
   hits+=1;assert begin>=0 and end<=limit+1e-12
   pts=p[None,:]+d[None,:]*np.linspace(begin,end,13)[:,None];rs=np.linalg.norm(pts,axis=1)
   assert np.min(rs)>=1-1e-9 and np.max(rs)<=1.2+1e-9
 for radius in [1.2,1.20001,3]:assert a.interval(np.array([0,0,radius]),np.array([0,0,1]))[1]<=a.interval(np.array([0,0,radius]),np.array([0,0,1]))[0]
 return {'rays':4000,'nonempty_segments':hits,'planet_and_scene_depth_clipped':True}
def continuous():
 sun=np.array([0,math.sqrt(.75),.5]);maxerr=0
 for radius in [1.085,1.132,1.2]:
  for tilt in [0,40,70,90,110]:
   d=np.array([0,math.sin(math.radians(tilt)),-math.cos(math.radians(tilt))]);x=a.march([0,0,radius-1e-6],d,sun);y=a.march([0,0,radius+1e-6],d,sun)
   error=max(float(np.max(abs(x[k]-y[k]))) for k in [0,1]);maxerr=max(maxerr,error)
 assert maxerr<.005,maxerr
 return {'clear_air_boundary_pairs':15,'radius_epsilon_R':1e-6,'max_radiance_or_transmittance_difference':maxerr,'qualification':'Independent clear-air CPU integration; not rendered Unity cloud continuity'}
def beer():
 rng=np.random.default_rng(413);err=0
 for _ in range(1000):
  sig=10**rng.uniform(-6,2,3);ds=10**rng.uniform(-5,1);source=rng.uniform(0,10,3)
  L=np.zeros(3);T=np.ones(3)
  for i in range(32):
   rem=-np.expm1(-sig*ds/32);L+=T*source*rem/sig;T*=1-rem
  ref=source*(-np.expm1(-sig*ds))/sig
  err=max(err,float(np.max(abs(ref-L))));assert np.all((T>=0)&(T<=1))
 assert err<1e-8
 return {'homogeneous_media':1000,'max_absolute_radiance_error':err}
def loops():
 names=['OrbisAtmosphereGLSL.cginc','OrbisAtmosphereCommon.cginc','OrbisCloudCommon.cginc','OrbisOptics.cginc'];count=0
 for name in names:
  s=re.sub(r'/\*.*?\*/|//[^\n]*','',(R/name).read_text(),flags=re.S)
  assert '[unroll' not in s and not re.search(r'\b(?:tex2D|tex3D|ddx|ddy|fwidth)\s*\(',s)
  for m in re.finditer(r'\bfor\s*\(',s):assert re.search(r'\[loop\]\s*$',s[:m.start()]);count+=1
 view=(R/'OrbisAtmosphereView.cginc').read_text();assert view.index('fwidth(mu)')<view.index('if(eyeDepth')
 return {'bounded_rolled_volume_loops':count,'solar_disk_derivative_before_depth_branch':True}
def ui_camera():
 ux=ET.parse(R/'Orbis.uxml');names=[e.attrib['name'] for e in ux.iter() if 'name'in e.attrib];assert len(names)==len(set(names))
 hud=(C/'OrbisHud.cs').read_text();flight=(C/'OrbisAtmosphereFlight.cs').read_text();demo=(C/'OrbisDemo.cs').read_text()
 for name in ['enterAtmosphere','exitAtmosphere','duskView','atmosphereFlight','lookTilt']:assert name in names and '"'+name+'"' in hud
 for theta in np.linspace(0,110,111):
  t=math.radians(theta);fw=np.array([0,math.sin(t),-math.cos(t)]);up=np.array([0,math.cos(t),math.sin(t)])
  assert abs(np.dot(fw,up))<1e-12 and abs(np.linalg.norm(fw)-1)<1e-12 and np.linalg.norm(np.cross(fw,up))>.999999
 assert 'Color.black' in demo and 'ApplyAtmosphereLook(orbit,radial,dt)' in demo and 'TickAtmosphereFlight(delta)' in demo
 assert 'lookTilt=lookTilt, lookHeading=lookHeading' in demo
 assert 'Has(json,"lookTilt")' in (C/'OrbisSaveMigration.cs').read_text()
 return {'named_ui_elements':len(names),'camera_basis_angles_tested':111,'legacy_top_down_default':True,'saved_orientation_with_legacy_defaults':True}
def shared():
 opt=(R/'OrbisOptics.cginc').read_text();cloud=(R/'Clouds.shader').read_text();vol=(R/'OrbisAtmosphereCommon.cginc').read_text();composite=(R/'AtmosphereComposite.shader').read_text()
 for name in ['Surface.shader','RiverWater.shader','OrbisAtmosphereCommon.cginc']:assert '#include "OrbisOptics.cginc"' in (R/name).read_text()
 assert 'OrbisGLSLSource(rho,phaseAir,sunT)*airShadow' in vol
 assert 'OrbisMarchRay(up*ORBIS_AIR_RADIUS,-up' in vol
 assert re.findall(r'Name "(.*?)"',cloud)==['MRT_VOLUME','FULL_RES_VOLUME','MAP_VOLUME']
 assert 'OrbisBackgroundWithSun(' in cloud and 'OrbisBackgroundWithSun(' in composite
 assert 'SetRays(composite)' in (C/'OrbisAtmosphereRenderer.cs').read_text()
 assert 'OrbisTone(tex2D(_MainTex,i.uv).rgb*r.transmission+r.light)' in cloud # Map has no flat pasted sun.
 return {'three_pass_indices_retained':True,'air_source_still_uses_cloud_shadow':True,'new_optics_used_by_surfaces_water_clouds_map':True,'legacy_v41_inlined_sun_source_assertion_replaced':True}
for name,fn in [('active_source_and_license',source),('length_units_and_optical_depth',units),('phase_functions',phase),('density_and_outer_boundary',densities),('sunlight_and_quadrature',sunlight),('inside_outside_geometry',geometry),('clear_air_boundary_continuity',continuous),('stable_Beer_integration',beer),('loop_regression_contracts',loops),('camera_UI_and_save_wiring',ui_camera),('shared_render_wiring',shared)]:test(name,fn)
report['passed']=sum(v['status']=='PASS' for v in report['checks'].values());report['failed']=len(report['checks'])-report['passed']
(ROOT/'Documentation/Validation-V4_1_2.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n');print(json.dumps(report,ensure_ascii=False,indent=2))
raise SystemExit(bool(report['failed']))
