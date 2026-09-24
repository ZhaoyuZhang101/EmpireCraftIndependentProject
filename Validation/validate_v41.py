#!/usr/bin/env python3
"""V4.1 offline checks. Python reference/lint only, NOT Unity/C#/shader execution.
Run from any folder: python Validation/validate_v41.py
Dependencies: numpy, scipy, Pillow (same as inherited V4 checks).
"""
from pathlib import Path
import sys,json,re,math,time,xml.etree.ElementTree as ET
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
R=ROOT/'Assets/ORBIS/Resources/ORBIS';C=ROOT/'Assets/ORBIS/Runtime'
source=(ROOT/'Validation/validate_v4.py').read_text()
namespace={'__file__':str(ROOT/'Validation/validate_v4.py')}
exec(compile(source[:source.rindex('\nfor name,fn in [')],'inherited_v4_reference','exec'),namespace)
report={'scope':'Offline source contracts, fixtures, independent Python double-precision references. NOT execution of production C#/HLSL.',
'csharp_compilation':'NOT_RUN','shader_compilation':'NOT_RUN','unity_import':'NOT_RUN','play_mode':'NOT_RUN','gpu_profiling':'NOT_RUN','desktop_build':'NOT_RUN','visual_acceptance':'NOT_RUN','checks':{}}
def check(name,fn):
 start=time.perf_counter()
 try:result=fn() or {};report['checks'][name]={'status':'PASS','seconds':round(time.perf_counter()-start,3),**result}
 except Exception as e:report['checks'][name]={'status':'FAIL','error':repr(e)}
def resources():
 guids=[];used=[];optional=[]
 for p in (ROOT/'Assets').rglob('*'):
  if p.suffix=='.meta':
   m=re.search(r'^guid: ([0-9a-f]{32})$',p.read_text(),re.M);assert m,p;guids.append(m[1])
  else:assert p.with_name(p.name+'.meta').exists(),p
 assert len(guids)==len(set(guids))
 for p in (ROOT/'Assets').rglob('*.cs'):
  for name in re.findall(r'Resources.Load<[^>]+>\("([^"+]+)"\)',p.read_text()):
   if name=='ORBIS/CloudNoiseExternal':optional.append(name);continue
   q=ROOT/'Assets/ORBIS/Resources'/name
   assert any(x.suffix!='.meta' for x in q.parent.glob(q.name+'.*')),(p,name);used.append(name)
 for p in (ROOT/'Assets').rglob('*.unity'):
  for guid in re.findall(r'guid: ([0-9a-f]{32})',p.read_text()):assert guid in guids,(p,guid)
 assert not [p for p in ROOT.rglob('*') if p.suffix.lower() in {'.ttf','.otf','.woff','.woff2'}]
 return {'required_resource_references':len(used),'optional_external_noise_references':len(optional),'unique_guids':len(guids),'font_binaries':0}
def wiring():
 surf=(R/'Surface.shader').read_text();vol=(R/'OrbisAtmosphereCommon.cginc').read_text();opt=(R/'OrbisOptics.cginc').read_text()
 assert 'base=float4(1,1,1,0)' not in surf
 assert 'GammaToLinearSpace(max(0,base.rgb))' in surf
 assert '"LightMode"="ForwardBase"' in surf
 assert 'light*sunT*shadow' in surf and 'OrbisSkyFill(i.local,sun)' in surf
 assert 'OrbisCloudShadow(p,sun),_ShadowStrength)' in vol and '*sunT*airShadow*' in vol
 assert '#include "UnityCG.cginc"' in opt
 for name in ['Surface.shader','RiverWater.shader','AtmosphereComposite.shader','Display.shader','OrbisAtmosphereCommon.cginc']:
  assert '#include "OrbisOptics.cginc"' in (R/name).read_text(),name
 renderer=(C/'OrbisAtmosphereRenderer.cs').read_text();demo=(C/'OrbisDemo.cs').read_text()
 assert 'bool map=atlas&&atlas.isActiveAndEnabled&&cameraComponent.orthographic' in renderer
 assert 'Present(source,destination,m,2)' in renderer
 assert 'View.SyncVolumeSettings(VisualSun)' in demo and '>().atlas=Atlas' in demo
 assert 'OrbisMarchRay(up*ORBIS_AIR_RADIUS,-up' in vol
 assert 'world.VisualTime' in (C/'OrbisTransportView.cs').read_text()
 for field in ['godRays','mapClouds','exposure','shadowStrength','surfaceDebug']:
  assert f'Has(json,"{field}")' in (C/'OrbisSaveMigration.cs').read_text()
 assert (R/'Clouds.shader').read_text().count('#pragma fragment frag')==3
 assert 'covered=max(0.0,segmentTop-max(z,segmentTop-ds))' in (R/'CloudShadowCache.shader').read_text()
 return {'white_fallback_removed':True,'shared_optics':True,'map_volume_pass':2,'shared_air_cloud_shadow_source':True,'new_save_defaults':5}
def sphere(p,d,r):
 b=np.dot(p,d);disc=b*b-np.dot(p,p)+r*r
 if disc<0:return np.array([1e8,-1e8])
 return np.array([-b-math.sqrt(disc),-b+math.sqrt(disc)])
def visible(p,d):
 radius=np.linalg.norm(p)
 if radius<.999999:return 0.0
 hit=sphere(p+p/max(radius,1e-6)*.00001,d,1.0)
 return 0.0 if hit[1]>0 and hit[0]>=0 else 1.0
def air(p):
 h=max(0,np.linalg.norm(p)-1)
 ray=math.exp(-h/.025);mie=math.exp(-h/.006);oz=math.exp(-((h-.055)/.022)**2)*.12
 return np.array([3,7,16.5])*ray+mie*2.5+np.array([2.04,4.97,.195])*oz
def sun_trans(p,d):
 if not visible(p,d):return np.zeros(3)
 ds=max(0,sphere(p,d,1.2)[1])/8
 tau=sum((air(p+d*((i+.5)*ds))*ds for i in range(8)),start=np.zeros(3))
 return np.exp(-np.minimum(tau,40))
def sunlight():
 sun=np.array([0.,0.,1.]);up=np.array([0.,0.,1.]);night=-up
 day=sun_trans(up+up*.0002,sun);dark=sun_trans(night+night*.0002,sun)
 low=np.array([math.sqrt(1-.03*.03),0,.03]);dusk=sun_trans(low+low*.0002,sun)
 assert np.all((day>0)&(day<=1));assert np.all(dark==0)
 for radius in [1.0,1.000001,1.0002,1.085]:assert visible(night*radius,sun)==0
 assert dusk[0]>dusk[1]>dusk[2] and dusk[2]<day[2]
 rng=np.random.default_rng(314)
 for _ in range(300):
  p=rng.normal(size=3);p/=np.linalg.norm(p);p*=rng.uniform(1.0002,1.2)
  t=sun_trans(p,sun);assert np.all(np.isfinite(t)) and np.all((t>=0)&(t<=1))
 albedo=np.array([.06,.15,.035]);fill=np.array([.0007,.001,.0018])
 day_color=albedo*(fill+np.array([.038,.061,.095])+day*1.65);night_color=albedo*fill
 assert np.max(night_color)/np.max(day_color)<.002
 return {'zenith_RGB_transmittance':day.tolist(),'low_sun_RGB_transmittance':dusk.tolist(),'night_direct_RGB':dark.tolist(),'sampled_positions':300,'max_night_to_day_channel_ratio':float(np.max(night_color)/np.max(day_color))}
def beer_cache():
 rng=np.random.default_rng(789);minimum_difference=0.0
 for steps in [10,16,24,32]:
  for _ in range(1000):
   span=rng.uniform(.001,.12);ds=span/steps;rho=rng.uniform(0,2,size=steps)
   taus=[]
   for layer in range(8):
    depth=span*layer/7;covered=np.clip(depth-np.arange(steps)*ds,0,ds)
    taus.append(min(16,float(np.dot(rho,covered)*105)))
   assert taus[0]==0 and np.all(np.diff(taus)>=-1e-12)
   ts=np.exp(-np.array(taus));assert np.all((ts>0)&(ts<=1)) and np.all(np.diff(ts)<=1e-12)
   minimum_difference=min(minimum_difference,float(np.diff(taus).min()))
 return {'random_columns':4000,'depth_layers':8,'quadrature_nodes_shared_across_layers':True,'optical_depth_monotone':True,'transmittance_in_0_1':True}
def ray_column():
 rng=np.random.default_rng(451);error=0
 for _ in range(1000):
  up=rng.normal(size=3);up/=np.linalg.norm(up);ground=rng.uniform(1,1.078)
  ro=up*1.2;rd=-up
  cloud_outer=sphere(ro,rd,1.132);cloud_inner=sphere(ro,rd,1.085)
  end=1.2-ground
  assert cloud_outer[0]>=0 and cloud_inner[0]<end
  segment=cloud_inner[0]-cloud_outer[0];error=max(error,abs(segment-(1.132-1.085)))
  lon=math.atan2(up[0],up[2]);lat=math.asin(up[1]);xy=np.array([-lon,lat])
  reconstructed=np.array([math.sin(-xy[0])*math.cos(xy[1]),math.sin(xy[1]),math.cos(xy[0])*math.cos(xy[1])])
  assert np.max(abs(reconstructed-up))<2e-14
 assert error<1e-12
 return {'map_sphere_column_equivalences':1000,'max_shell_thickness_error_R':error,'qualification':'Radial-column geometry, not 3D perspective in the unfolded atlas'}
def tracers():
 def advance(p,v,dt):
  v=v-p*np.dot(v,p);speed=np.linalg.norm(v)
  if speed<1e-8:return p
  a=np.clip(speed*dt,-.025,.025);q=p*math.cos(a)+v/speed*math.sin(a);return q/np.linalg.norm(q)
 omega=np.array([0.,.007,0.]);p=np.array([1.,0.,0.]);dt=.18
 for i in range(5000):
  v=np.cross(omega,p);mid=advance(p,v,dt/2);p=advance(p,np.cross(omega,mid),dt)
  assert abs(np.linalg.norm(p)-1)<1e-12
 theta=np.linalg.norm(omega)*dt*5000;exact=np.array([math.cos(theta),0,-math.sin(theta)])
 error=np.linalg.norm(p-exact);assert error<1e-5
 s=(C/'OrbisTransportView.cs').read_text()
 assert 'Velocity(candidate,ref hint,kind,out _,out ocean)' in s
 assert 'mask=requested' in s and 'result|=4' in s and 'Respawn' in s
 return {'RK2_rotation_steps':5000,'sphere_radius_error_bound':1e-12,'analytic_rotation_position_error':float(error),'candidate_coast_check_wired':True,'qualification':'Synthetic rotating field; not a CFD accuracy test'}
def import_bridge():
 s=(ROOT/'Assets/ORBIS/Editor/OrbisCloudNoiseImport.cs').read_text()
 for token in ['OrderBy(Index)','data[24]!=8','data[25]!=2&&data[25]!=6','TextureFormat.RGBA32,false,true','EditorUtility.CopySerialized','slice.width!=width||slice.height!=height','if(slice)UnityEngine.Object.DestroyImmediate(slice)']:
  assert token in s,token
 weather=(C/'OrbisWeatherField.cs').read_text();assert 'ownsNoise=!noise' in weather and 'if(ownsNoise)UnityEngine.Object.Destroy(noise)' in weather
 return {'supported_input':'8-bit RGB/RGBA PNG numbered slices only','external_asset_owned_by_project':True,'runtime_fallback':True,'qualification':'Source contract, no native Texture3D serialization test'}
for name,fn in [('source_delimiters_and_limited_lint',namespace['source']),('ui_xml_and_bindings',namespace['ui']),('resources_metadata_fonts',resources),('v41_render_wiring',wiring),('v4_feature_contracts',namespace['architecture']),('unified_water_contracts',namespace['water']),('map_and_codec_fixtures',namespace['fixtures']),('raster_river_reference',namespace['river_reference']),('region_boundary_reference',namespace['selection_reference']),('raster_sphere_coordinates',namespace['coordinates']),('thermal_relaxation_reference',namespace['thermal']),('inherited_weather_and_camera_references',namespace['inherited']),('solar_transmittance_reference',sunlight),('layered_Beer_reference',beer_cache),('atlas_radial_column_reference',ray_column),('tracer_sphere_reference',tracers),('noise_import_source_contract',import_bridge)]:check(name,fn)
report['passed']=sum(c['status']=='PASS' for c in report['checks'].values());report['failed']=len(report['checks'])-report['passed']
(ROOT/'Documentation/Validation-V4_1.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print(json.dumps(report,ensure_ascii=False,indent=2));sys.exit(int(report['failed']>0))
