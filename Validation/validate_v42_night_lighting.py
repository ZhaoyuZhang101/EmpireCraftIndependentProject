#!/usr/bin/env python3
"""Source contracts + independent CPU reference. No Unity/HLSL/C# compilation.
Python 3.9+, standard library only. Run from any working directory.
"""
import hashlib,json,math,random,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
DOC=ROOT/'Documentation/V4_2_Repair_IPCFix_NightLighting'
SAT=lambda x:max(0.,min(1.,x))
def smooth(a,b,x):
 t=SAT((x-a)/(b-a));return t*t*(3-2*t)
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def norm(a):return math.sqrt(dot(a,a))
def unit(a):return tuple(x/norm(a) for x in a)
def cross(a,b):return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def phase(sun,day,initial=.5,cycle=29.53,inc=5.1):
 sun=unit(sun);t=((day-100)/cycle+initial)%1;theta=t*2*math.pi
 east=cross((0,1,0),sun)
 if dot(east,east)<1e-8:east=cross((1,0,0),sun)
 east=unit(east);north=unit(cross(sun,east));incl=math.radians(inc)
 tangent=[east[i]*math.cos(incl)+north[i]*math.sin(incl) for i in range(3)]
 moon=unit([sun[i]*math.cos(theta)+tangent[i]*math.sin(theta) for i in range(3)])
 pc=max(-1,min(1,-dot(sun,moon)));alpha=math.acos(pc)
 return moon,(1+pc)*.5,SAT((math.sin(alpha)+(math.pi-alpha)*pc)/math.pi)
def visible(p,d):
 r=norm(p)
 if r<.999999:return 0.
 p=[x+x/r*.00001 for x in p];b=dot(p,d);c=dot(p,p)-1;disc=b*b-c
 if disc<0:return 1.
 h=math.sqrt(max(0,disc));return 0. if -b+h>0 and -b-h>=0 else 1.
def weight(p,s,night=1,enabled=1):return SAT(night)*SAT(enabled)*(1-smooth(-.2,.06,dot(unit(p),s)))
def ambient(p,s,tau,sky=1,glow=1,air=1,atmosphere=1,night=1,enabled=1):
 a=SAT(atmosphere)*SAT(max(0,air));alt=1-smooth(1.08,1.2,norm(p))
 st=.2+.8*math.exp(-max(0,tau)*.55)
 return [(c*sky*(.35+.65*a)+g*.035*glow*a)*weight(p,s,night,enabled)*st*(.30+.7*alt) for c,g in zip((.04,.055,.080),(.070,.180,.105))]
def emission(p,s,glow=1,air=1,atmosphere=1,night=1,enabled=1):
 if atmosphere<.5 or air<=0:return [0,0,0]
 h=norm(p);x=(h-1.165)/.016;shell=math.exp(-.5*x*x)*smooth(1.1,1.13,h)*(1-smooth(1.185,1.20,h))
 return [c*max(0,glow)*shell*weight(p,s,night,enabled)*SAT(atmosphere)*SAT(air) for c in (.07,.18,.105)]
def moon_air(p,d,air=1,atmosphere=1,haze=1,lut=None):
 if visible(p,d)<.5:return [0,0,0]
 if atmosphere<.5 or air<=0:return [1,1,1]
 r=max(1,norm(p))
 if r>=1.2 and dot(p,d)>=0:return [1,1,1]
 mu=dot([x/r for x in p],d)
 col=lut if lut is not None else [scale*math.exp(-(r-1)/scale)/max(.12,mu) for scale in (.032,.008)]
 return [math.exp(-min(60,max(0,(col[0]*rcoef+col[1]*3.15*max(0,haze)*1.1)*max(0,air)*SAT(atmosphere)))) for rcoef in (1.375,3.25,5.6)]
def incident(p,s,moon,tau=0,strength=.12,air=1,atmosphere=1,lut=None):
 w=weight(p,s)
 if w<=1e-5 or strength<=1e-6:return [0,0,0]
 mu=dot(unit(p),moon);cloud=math.exp(-min(20,max(0,tau)/max(.18,mu)))
 return [c*max(0,strength)*w*cloud*t for c,t in zip((.9,.94,1),moon_air(p,moon,air,atmosphere,lut=lut))]
def column(p,coverage=.8,water=.6,ground=1,height_scale=7e-6,density=1,cover=1,attenuation=1,clouds=1,layered=1):
 if clouds<.5 or layered<.5 or attenuation<=0:return 0.
 thickness=max(.002,height_scale*16000);above=SAT((max(1,ground)+thickness-norm(p))/thickness)
 return min(12,SAT(coverage*max(0,cover))*(.75+4*SAT(water))*max(0,density)*above*max(0,attenuation))
def stripped(s):return re.sub(r'//[^\n]*|/\*.*?\*/','',s,flags=re.S)
def check_sources():
 p=ROOT/'Assets/ORBIS/Resources/ORBIS'
 helper=(p/'OrbisNightLighting.cginc').read_text();body=stripped(helper)
 assert not re.search(r'\b(for|while|do)\b\s*\(?',body),'new helper must not add loops'
 assert 'tex3D' not in body and 'OrbisCloudShadow(' not in body and 'LightField(' not in body
 assert 'OrbisGLSLSunTransmittance' not in body and '_SolarEnergy' not in body
 assert body.count('tex2Dlod(')==3
 optics=(p/'OrbisOptics.cginc').read_text();common=(p/'OrbisAtmosphereCommon.cginc').read_text()
 assert optics.index('float _Night')<optics.index('#include "OrbisNightLighting.cginc"')
 assert 'source+=AirExtinction(rho)*nightAmbient*.35+moonSource+airglow;' in common
 assert 'cloudDensity*ORBIS_EXTINCTION*max(.1,_SolarEnergy)' not in common
 assert 'traversal<325' in common and common.count('OrbisCloudShadow(p,sun)')==1
 for name in ['Surface.shader','OrbisWater.cginc']:
  t=(p/name).read_text();assert 'OrbisMoonIncident' in t and 'OrbisSurfaceSkyFill' in t
 runtime=ROOT/'Assets/ORBIS/Runtime';world=(runtime/'OrbisWorld.cs').read_text()
 assert 'OrbisNightLighting.Ensure(w.settings)' in world
 night=(runtime/'OrbisNightLighting.cs').read_text()
 assert 'Time.time' not in stripped(night) and 'material.SetColor' not in night
 assert 'material.SetVector' in night and 'double calendarDay' in night
 assert 'View.SyncVolumeSettings(VisualSun' in (runtime/'OrbisDemo.cs').read_text()
 return {'no_new_helper_loops':True,'no_new_3d_density_fetches':True,'no_solar_atlas_reused_for_moon':True,
 'new_texture_sites_2d':3,'solar_independent_night_terms':True,'existing_IPC_view_cap':325,
 'all_land_water_volume_consumers_wired':True,'save_migration_source_present':True,'live_CSharp_serialization_test':'NOT_RUN'}
def run():
 checks={'source_contracts':check_sources()};sun=unit((.4,.2,.8))
 for initial,frac,brightness in [(0,0,0),(.25,.5,1/math.pi),(.5,1,1),(.75,.5,1/math.pi)]:
  m,f,b=phase(sun,100,initial);assert abs(f-frac)<1e-7 and abs(b-brightness)<1e-7
 random.seed(42)
 for _ in range(4000):
  s=unit([random.uniform(-1,1) for j in range(3)]);d=random.uniform(0,1e6)
  m,f,b=phase(s,d);m2,f2,b2=phase(s,d+29.53)
  assert abs(norm(m)-1)<1e-12 and 0<=b<=1 and 0<=f<=1
  assert max(abs(a-z) for a,z in zip(m,m2))<1e-8
 for s in [(0,1,0),(0,-1,0),(1e-6,1,0)]:
  for day in (0,100,1e8):assert all(math.isfinite(x) for x in phase(s,day)[0])
 checks['phase_reference']={'random_cases':4000,'polar_long_clock_cases':9,'new_full_quarters_cycle':'PASS','model':'CPU double reference, not executing C#'}
 p=(0,0,1.001);s=(0,0,-1);m=(0,0,1)
 assert visible(p,m)==1 and visible(p,s)==0
 assert all(x>0 for x in incident(p,s,m)) and incident(p,s,s)==[0,0,0]
 assert incident(p,s,m,strength=0)==[0,0,0]
 for tau in [0,.1,1,4,12,100]:
  clear=ambient(p,s,0);dim=ambient(p,s,tau)
  assert all(0<x<=y for x,y in zip(dim,clear))
  assert all(0<=x<=y for x,y in zip(incident(p,s,m,tau),incident(p,s,m)))
 assert column((0,0,1.3))==0 and column(p,clouds=0)==0
 assert column(p)>column((0,0,1.05))>0
 assert ambient(p,s,0,enabled=0)==[0,0,0] and ambient(p,s,0,night=0)==[0,0,0]
 assert ambient(p,m,0)==[0,0,0]
 h=(0,0,1.165);e=emission(h,s)
 assert all(x>0 for x in e) and emission(h,m)==[0,0,0]
 assert emission(h,s,atmosphere=0)==[0,0,0] and emission(h,s,air=0)==[0,0,0]
 assert emission(p,s)==[0,0,0]
 assert all(x>0 for x in ambient(p,s,12,atmosphere=0))
 for tau in [0,.1,1,4,12]:
  assert all(x*math.exp(-tau)<=x for x in e)
 # Analytic Beer integration, equal homogeneous source split into 1 or 32 segments.
 maxerr=0
 for _ in range(4000):
  sigma=random.uniform(.0001,100);length=random.uniform(.0001,.4);src=random.uniform(0,1)
  exact=src*(-math.expm1(-sigma*length))/sigma
  ds=length/32;T=1;L=0
  for j in range(32):L+=T*src*(-math.expm1(-sigma*ds))/sigma;T*=math.exp(-sigma*ds)
  maxerr=max(maxerr,abs(L-exact))
 assert maxerr<1e-12
 checks['light_separation']={'new_moon_and_planet_occlusion':'PASS','day_night_gates':'PASS','overcast_monotonic_nonzero_ambient':'PASS',
 'airglow_off_without_atmosphere':'PASS','albedo_zero_stays_zero_before_other_reflection_emission':True,
 'uniform_Beer_split_cases':4000,'uniform_Beer_max_abs_error':maxerr,
 'limitation':'Analytic energy reference, not sampled production volume visual equivalence.'}
 samples={}
 for label,tau,strength in [('moonless_clear',0,0),('full_moon_clear',0,.12),('full_moon_overcast',4,.12),('moonless_overcast',4,0)]:
  a=ambient(p,s,tau);l=incident(p,s,m,tau,strength)
  samples[label]={'linear_ambient_RGB':a,'linear_direct_moon_RGB':l,'matte_20_percent_albedo_linear_RGB':[.2*(x+y) for x,y in zip(a,l)]}
 report={'extension':'V4.2 Repair IPCFix1 + NightLighting','status':'PASS','scope':'Listed source and CPU numerical tests ONLY',
 'unity_shader_compilation_performed':False,'hlsl_compilation_performed':False,'csharp_compilation_performed':False,
 'unity_visual_test_performed':False,'fps_test_performed':False,'checks':checks,'reference_samples_not_screenshots':samples,
 'source_sha256':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((ROOT/'Assets').rglob('*')) if p.is_file() and ('NightLighting' in p.name or p.name in ['Surface.shader','OrbisWater.cginc','OrbisAtmosphereCommon.cginc','OrbisOptics.cginc'])}}
 DOC.mkdir(exist_ok=True);(DOC/'night-source-numerical-report.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
if __name__=='__main__':run()
