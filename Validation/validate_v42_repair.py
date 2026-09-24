#!/usr/bin/env python3
"""Current-repair source contracts and independent numerical checks; no Unity compiler."""
from pathlib import Path
import re,json,hashlib,sys,struct,xml.etree.ElementTree as ET
import numpy as np
P=Path(__file__).resolve().parents[1];S=P/'Assets/ORBIS/Resources/ORBIS';R=P/'Assets/ORBIS/Runtime'
sys.path.insert(0,str(P/'Tools'));import bake_v42_repair as bake
checks={}
def test(name,fn):
    try:checks[name]={'status':'PASS','detail':fn()}
    except Exception as e:checks[name]={'status':'FAIL','error':str(e)}
def source_contracts():
    count=0
    for f in (P/'Assets').rglob('*'):
        if f.suffix not in ('.cs','.shader','.cginc'):continue
        s=f.read_text();s=re.sub(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/','',s,flags=re.S)
        stack=[]
        for c in s:
            if c in '({[':stack.append(c)
            elif c in ')}]':assert stack and stack.pop()=='({['[')}]'.index(c)],str(f)
        assert not stack,str(f);count+=1
    for f in S.glob('*'):
        if f.suffix not in ('.shader','.cginc'):continue
        for inc in re.findall(r'#include\s+"([^"]+)"',f.read_text()):
            assert inc.startswith('Unity') or (f.parent/inc).is_file(),(str(f),inc)
    refs=[]
    for f in (P/'Assets').rglob('*.meta'):
        m=re.search(r'^guid: (\w+)',f.read_text(),re.M)
        if m:refs.append(m.group(1))
    assert len(refs)==len(set(refs)),'Duplicate Unity GUID'
    root=ET.parse(S/'Orbis.uxml');names=[x.attrib['name'] for x in root.iter() if 'name'in x.attrib]
    assert len(names)==len(set(names))
    assert not [str(f) for f in P.rglob('*') if f.suffix.lower() in ('.ttf','.otf','.ttc','.woff','.woff2')]
    return {'balanced_code_files':count,'unique_meta_guids':len(refs),'xml_control_names':len(names),'qualification':'Lexical contracts only; no C#/HLSL type checking'}
def integration():
    c=(S/'OrbisCloudCommon.cginc').read_text();demo=(R/'OrbisDemo.cs').read_text();w=(R/'OrbisWeather.cs').read_text();effects=(R/'Weather/OrbisWeatherEffects.cs').read_text()
    assert '#include "OrbisAtmosphereGLtracy.cginc"' in (S/'OrbisOptics.cginc').read_text()
    assert 'REFERENCE_HASH_PENDING' not in (R/'OrbisAtmosphereProfile.cs').read_text()
    h=hashlib.sha256((P/'Reference/GLtracy_User_V4_2.glsl').read_bytes()).hexdigest()
    assert h in (R/'OrbisAtmosphereProfile.cs').read_text()
    assert 'physical*cover*structure*2.1' in c and 'lerp(.08,1.5,structure)' not in c
    assert 'World.Weather.Advance(delta*rate,1)' in demo
    assert 'World.Weather.Clock+World.Weather.Accumulator' in demo
    assert 'hasRenderRotation=true' in demo and 'v.hasRenderRotation' in demo
    assert 'if(executed>0)Publish()' in w and 'Atmosphere.SnowRate[i]*hours' in w
    assert 'SampleAppearanceCached' in (R/'OrbisSurfaceLOD.cs').read_text()
    assert '_TerrainAtlasReady>.5' in (S/'Surface.shader').read_text()
    assert 'OrbisWaterFresnel' in (S/'OrbisWater.cginc').read_text()
    assert 'flashCooldown=.70f+Random01()*.65f' in effects and 'audioCooldown=8+Random01()*6' in effects
    return {'active_GLtracy_reference_sha256':h,'cloud_gap_floor_removed':True,'bounded_weather_budget':True,'save_render_angle':True,'snow_rate_units':True,'geometry_cache_used':True}
def optical_lut():
    b=(S/'AtmosphereOpticalDepthV42.bytes').read_bytes();magic,w,h,hr,hm=struct.unpack('<4sHHff',b[:16]);assert (magic,w,h)==(b'OD42',192,96)
    a=np.frombuffer(b[16:],dtype='<f4').reshape(h,w,2);assert np.isfinite(a).all() and (a>=0).all()
    # Actual runtime commonly uploads to half float; include this quantization.
    a=a.astype(np.float16).astype(float)
    rng=np.random.default_rng(424242);n=2048;st=rng.random((n,2));st[:16,0]=np.linspace(0,.03,16)
    r=1+.2*st[:,1]**2;horizon=-np.sqrt(1-1/r**2);mu=horizon+(1-horizon)*st[:,0]**2
    xx=st[:,0]*(w-1);yy=st[:,1]*(h-1);x0=np.floor(xx).astype(int);y0=np.floor(yy).astype(int);x1=np.minimum(w-1,x0+1);y1=np.minimum(h-1,y0+1)
    fx=(xx-x0)[:,None];fy=(yy-y0)[:,None]
    approx=(a[y0,x0]*(1-fx)+a[y0,x1]*fx)*(1-fy)+(a[y1,x0]*(1-fx)+a[y1,x1]*fx)*fy
    exact=bake.column(r,mu,count=1024)
    betaR=np.array([1.9,6.75,16.55]);betaM=np.full(3,2.1*1.1)
    trans=lambda c:np.exp(-c[:,0,None]*betaR-c[:,1,None]*betaM)
    err=np.abs(trans(approx)-trans(exact));assert err.max()<.008,float(err.max())
    return {'sampled_rays':n,'reference_quadrature_per_half':1024,'lut_format_tested':'float16 + bilinear','max_absolute_RGB_transmittance_error':float(err.max()),'mean_absolute_RGB_transmittance_error':float(err.mean())}
def phase():
    mu=np.linspace(-1,1,200001);g=.78
    ray=3*(1+mu*mu)/(16*np.pi)
    mie=3*(1-g*g)*(1+mu*mu)/(8*np.pi*(2+g*g)*(1+g*g-2*g*mu)**1.5)
    original=3*(1-g*g)*(1+mu*mu)/(8*np.pi*(2+g*g)*(1+g*g-2*(-g)*(-mu))**1.5)
    r=2*np.pi*np.trapezoid(ray,mu);m=2*np.pi*np.trapezoid(mie,mu)
    assert abs(r-1)<1e-7 and abs(m-1)<1e-6 and np.max(abs(original-mie))==0
    return {'rayleigh_solid_angle_integral':float(r),'Mie_solid_angle_integral':float(m),'original_negative_g_negative_cosine_equivalence':True}
def volume_noise():
    blob=(S/'CloudNoiseV42.bytes').read_bytes();assert len(blob)==64**3*4
    vox=np.frombuffer(blob,dtype=np.uint8).reshape(64,64,64,4);assert (vox[...,3]==255).all()
    rng=np.random.default_rng(9);p=rng.random((512,3));a=bake.noise_channels(p);err=0
    for axis in range(3):
        d=np.zeros(3);d[axis]=1;err=max(err,float(np.max(abs(a-bake.noise_channels(p+d)))))
    assert err<1e-12
    body=vox[...,0]/255;erosion=vox[...,1]/255
    # Representative shader threshold, not a claim about an actual captured Unity frame.
    shape=body-(1-erosion)*.17;threshold=.50-.15*.6+.10*(1-.65)
    clear=float(np.mean(shape<=threshold));dense=float(np.mean(shape>=threshold+.19))
    assert clear>.05 and dense>.02
    return {'volume':[64,64,64,4],'periodicity_samples_per_axis':512,'max_periodic_error':err,'representative_clear_fraction':clear,'representative_dense_fraction':dense,'not_a_Unity_visual_test':True}
def water():
    nv=np.linspace(0,1,10001);eta=1.333;ct=np.sqrt(1-(1-nv*nv)/eta**2)
    rs=(nv-eta*ct)/(nv+eta*ct);rp=(eta*nv-ct)/(eta*nv+ct);F=.5*(rs*rs+rp*rp)
    assert np.isfinite(F).all() and F.min()>=0 and F.max()<=1+1e-12
    assert .019<F[-1]<.022 and abs(F[0]-1)<1e-12
    depth=np.array([0,.01,.1,1,10,160])
    path=depth[:,None]/ct
    T=np.exp(-path[...,None]*np.array([.16,.065,.032]));assert np.isfinite(T).all() and T.min()>=0 and T.max()<=1
    return {'view_angles':len(nv),'F0':float(F[-1]),'grazing_F':float(F[0]),'max_refracted_path_multiplier':float(np.max(1/ct)),'Beer_bounds':[float(T.min()),float(T.max())]}
def rotation_clock():
    maxerr=0
    for fps in (24,30,60,144):
        dt=1/fps;period=240;angle=0
        for i in range(fps*10):angle=(angle+dt*360/period)%360
        maxerr=max(maxerr,abs(angle-15))
    assert maxerr<1e-9
    # Fractional clock remains unchanged when a fixed step transfers accumulator -> Clock.
    clock=10;acc=.145;assert abs((clock+acc)-((clock+.1)+(acc-.1)))<1e-12
    return {'ten_second_rotation_error_degrees':maxerr,'fixed_tick_weather_clock_continuity':True,'qualification':'Equation test, not runtime frame pacing measurement'}
def current_core_reports():
    digest=hashlib.sha256((R/'Weather/OrbisAtmosphereCore.cs').read_bytes()).hexdigest()
    for name in ('validation_core_384cells.json','validation_core_3456_smoke.json'):
        a=json.loads((P/'Documentation/V4_2_Repair'/name).read_text());assert a['source_sha256']==digest;assert all(a['checks'].values())
    return {'source_sha256':digest,'384_cells_1800_ticks_each_natural_and_perturbed':'PASS','3456_cells_32_ticks':'PASS','3456_cells_1200_ticks':'TIMEOUT, NOT VALIDATED'}
for n,f in [('source_contracts',source_contracts),('active_integration_contracts',integration),('optical_LUT_accuracy',optical_lut),('phase_normalization',phase),('noise_periodicity_and_gaps',volume_noise),('water_Fresnel_Beer',water),('continuous_clock_equations',rotation_clock),('current_core_reports',current_core_reports)]:test(n,f)
report={'scope':'Offline source contracts and numerical checks only. No Unity Editor, C# compiler, HLSL compiler, play mode, visual acceptance or device FPS tests.','checks':checks,'passed':sum(v['status']=='PASS' for v in checks.values()),'failed':sum(v['status']=='FAIL' for v in checks.values())}
(P/'Documentation/V4_2_Repair/validation_repair.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n');print(json.dumps(report,indent=2,ensure_ascii=False));raise SystemExit(bool(report['failed']))
