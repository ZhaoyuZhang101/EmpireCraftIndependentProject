#!/usr/bin/env python3
"""Lexical contracts + independent CPU scheduler regression. NOT a Unity/HLSL compiler."""
from pathlib import Path
import hashlib, json, math, random, re, sys
P=Path(__file__).resolve().parents[1]
S=P/'Assets/ORBIS/Resources/ORBIS'
REPORT=P/'Documentation/V4_2_Repair_IPCFix/static-and-numerical-report.json'
checks={}

def test(name,fn):
    try: checks[name]={'status':'PASS','detail':fn()}
    except Exception as e: checks[name]={'status':'FAIL','error':repr(e)}

def clean(s):
    return re.sub(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/','',s,flags=re.S)

def source_contracts():
    count=0
    for p in (P/'Assets').rglob('*'):
        if p.suffix not in ('.cs','.cginc','.shader'): continue
        text=clean(p.read_text()); stack=[]
        for ch in text:
            if ch in '([{':stack.append(ch)
            elif ch in ')]}':
                assert stack and stack.pop()==dict(zip(')]}','([{'))[ch],str(p)
        assert not stack,str(p); count+=1
        # Common HLSL keyword trap: these cannot be identifiers.
        if p.suffix in ('.shader','.cginc'):
            assert not re.search(r'\b(?:int|float|bool|float[234])\s+(?:sample|linear|centroid|nointerpolation)\s*[=;,)]',text),str(p)
    for p in S.glob('*'):
        if p.suffix not in ('.shader','.cginc'): continue
        for inc in re.findall(r'#include\s+"([^"]+)"',p.read_text()):
            assert inc.startswith('Unity') or (p.parent/inc).is_file(),(str(p),inc)
    guids=[]
    for p in (P/'Assets').rglob('*.meta'):
        m=re.search(r'^guid:\s*(\w+)',p.read_text(),re.M)
        if m: guids.append(m[1])
    assert len(set(guids))==len(guids)
    cloud=(S/'Clouds.shader').read_text()
    assert re.findall(r'Name\s+"([^"]+)"',cloud)==['MRT_VOLUME','FULL_RES_VOLUME','MAP_VOLUME']
    assert cloud.count('#pragma target 3.5')==3
    assert 'float4 light:SV_Target0;float4 transmission:SV_Target1;' in cloud
    assert '#include "OrbisAtmosphereGLtracy.cginc"' in (S/'OrbisOptics.cginc').read_text()
    common=(S/'OrbisAtmosphereCommon.cginc').read_text()
    shadows=(S/'OrbisCloudCommon.cginc').read_text()
    assert 'inout OrbisVolumeResult' not in common
    assert 'IntegrateSegment(' not in common
    assert 'traversal<325' in common and 'sampleCount=clamp' in common
    assert common.count('OrbisCloudShadow(p,sun)')==1
    assert shadows.count('return OrbisCloudShadowDirect(p,sun);')==1
    assert '[loop] [fastopt] for(int shadowIndex=0;shadowIndex<32;shadowIndex++)' in shadows
    assert not re.search(r'_OrbisLightning\[(?![0-4]\])',shadows)
    assert not re.search(r'\[unroll',common+shadows)
    assert not any(p.suffix.lower() in ('.ttf','.otf','.ttc','.woff','.woff2') for p in P.rglob('*'))
    runtime=(P/'Assets/ORBIS/Runtime/OrbisAtmosphereRenderer.cs').read_text()
    assert 'CloudSteps={32,48,64,64}' in runtime
    assert 'AirSteps={16,24,32,48}' in runtime
    assert 'LightSteps={2,3,4,6}' in runtime
    return {'balanced_code_files':count,'unique_GUIDs':len(guids),
        'pass_indices_and_MRT_layout_preserved':True,'quality_budgets_preserved':True,
        'shared_shadow_call_sites':1,'direct_fallback_call_sites':1,
        'qualification':'Lexical/source contracts, NOT HLSL or C# compilation.'}

def sphere(radius,mu,shell):
    b=radius*mu; c=radius*radius-shell*shell; d=b*b-c
    if d<0:return (1e8,-1e8)
    h=math.sqrt(max(0,d));return (-b-h,-b+h)

def clamp(x,a,b):return min(b,max(a,x))

def radial(r,mu,t):return math.sqrt(max(0,r*r+2*r*mu*t+t*t))

def intervals(r,mu,limit,cloud_on):
    outer=sphere(r,mu,1.2);a=max(0,outer[0]);b=min(outer[1],limit)
    ground=sphere(r,mu,1)
    if ground[1]>0 and ground[0]>=0:b=min(b,ground[0])
    if r*r<.999998 or b<=a:return []
    x=tuple(clamp(t,a,b) for shell in (1.190,1.0004) for t in sphere(r,mu,shell))
    pairs=(min(x[0],x[1]),max(x[0],x[1]),min(x[2],x[3]),max(x[2],x[3]))
    crossed=(min(pairs[0],pairs[2]),min(pairs[1],pairs[3]),max(pairs[0],pairs[2]),max(pairs[1],pairs[3]))
    cut=(crossed[0],min(crossed[1],crossed[2]),max(crossed[1],crossed[2]),crossed[3])
    assert cut==tuple(sorted(x))
    edges=(a,)+cut+(b,)
    return [(a,b,1.0004<radial(r,mu,(a+b)*.5)<1.190 and cloud_on)
            for a,b in zip(edges[:-1],edges[1:])]

def counts(seg,cloud_steps,air_steps,air_on):
    cl=sum(b-a for a,b,c in seg if c); al=sum(b-a for a,b,c in seg if not c)
    def budget(a,b,cloud):
        if b-a<1e-7 or (not cloud and not air_on):return 0
        total=cl if cloud else al
        return clamp(math.ceil(clamp(cloud_steps if cloud else air_steps,1,64)*(b-a)/max(total,1e-6)),1 if cloud else 6,64)
    return [budget(*i) for i in seg]

def attenuation(t,ds,cloud):
    # Synthetic medium solely for checking front-to-back early-out, not game rendering.
    sigma=(80 if cloud else 3)*(1.1+.8*math.sin(t*37))
    return math.exp(-max(0,sigma)*ds)

def original(seg,ns,early):
    out=[];T=1.
    for (a,b,cloud),n in zip(seg,ns):
        if n<=0:continue
        ds=(b-a)/n
        for j in range(n):
            if early and T<.002:break
            t=a+(j+.5)*ds;out.append((t,ds,cloud))
            T*=attenuation(t,ds,cloud)
    return out

def flat(seg,ns,early):
    out=[];T=1.;next_interval=0;j=0;n=0;a=0.;ds=0.;cloud=False;iterations=0
    if not seg:return out,0
    for traversal in range(325):
        iterations=traversal+1
        if early and T<.002:break
        if j>=n:
            if next_interval>=5:break
            new_a,b,new_cloud=seg[next_interval];new_n=ns[next_interval];next_interval+=1
            if new_n<=0:continue
            a=new_a;cloud=new_cloud;n=new_n;j=0;ds=(b-a)/n
        t=a+(j+.5)*ds;out.append((t,ds,cloud));j+=1
        T*=attenuation(t,ds,cloud)
    assert next_interval>=5 or (early and T<.002), 'Traversal cap truncated data'
    return out,iterations

def scheduler():
    rng=random.Random(424201);presets=[(32,16),(48,24),(64,32),(64,48),(0,0),(-5,-5),(512,512)]
    cases=[]
    for r in (.999,1.,1.0004,1.01,1.085,1.19,1.2,1.200001,3.,8.):
        mus=[-1.,0.,1.,-.01,.01]
        if r>=1:mus += [-math.sqrt(1-1/r**2),-math.sqrt(1-1/r**2)-1e-9]
        for mu in mus:
            for budget in presets:
                for cloud_on,air_on in ((True,True),(True,False),(False,True),(False,False)):
                    cases.append((r,clamp(mu,-1,1),20.,budget,cloud_on,air_on))
    for _ in range(10000):
        r=rng.choice([rng.uniform(.998,1.20001),rng.uniform(1.2,8.)])
        mu=rng.uniform(-1,1);limit=rng.choice([20.,rng.uniform(0,2.)])
        cases.append((r,mu,limit,rng.choice(presets),rng.random()>.2,rng.random()>.2))
    comparisons=0;total_samples=0;max_samples=0;max_iterations=0
    for r,mu,lim,budget,cloud_on,air_on in cases:
        seg=intervals(r,mu,lim,cloud_on);ns=counts(seg,*budget,air_on)
        for early in (False,True):
            expected=original(seg,ns,early);actual,iterations=flat(seg,ns,early)
            assert expected==actual,(r,mu,lim,budget,cloud_on,air_on,early)
            comparisons+=1;total_samples+=len(actual);max_samples=max(max_samples,len(actual));max_iterations=max(max_iterations,iterations)
    # Stress the 5*64 bound independent of spherical geometry.
    synthetic=[(float(i),float(i+1),True) for i in range(5)];expected=original(synthetic,[64]*5,False)
    actual,its=flat(synthetic,[64]*5,False);assert expected==actual and len(actual)==320
    return {'ray_cases':len(cases),'with_and_without_early_out_comparisons':comparisons,
        'compared_sample_records':total_samples,'max_ray_samples_in_cases':max_samples,
        'max_traversal_iterations_in_cases':max_iterations,'synthetic_upper_bound_samples':len(actual),
        'synthetic_upper_bound_iterations':its,'midpoint_order_ds_and_cloud_flags_match_exactly':True,
        'numeric_model':'CPU double precision; synthetic extinction for early-out. NOT GPU floating-point/visual equivalence.'}

def shadow_scheduler():
    rng=random.Random(424202);records=0;two_shell=0
    for k in range(12000):
        r=rng.uniform(.998,1.25);mu=rng.uniform(-1,1)
        outer=sphere(r,mu,1.190);inner=sphere(r,mu,1.0004)
        begin=max(0,outer[0]);end=max(0,outer[1])
        if end<=begin:continue
        ranges=(begin,min(end,max(begin,inner[0])),max(begin,inner[1]),end)
        if inner[0]>inner[1]:ranges=(begin,end,end,end)
        expected=[]
        for segment in range(2):
            a,b=ranges[2*segment:2*segment+2]
            if b<=a:continue
            ds=(b-a)/16
            expected.extend((a+(j+.5)*ds,ds) for j in range(16))
        actual=[]
        for i in range(32):
            first=i<16;a=ranges[0] if first else ranges[2];b=ranges[1] if first else ranges[3]
            if b<=a:continue
            ds=(b-a)/16;j=i if first else i-16;actual.append((a+(j+.5)*ds,ds))
        assert expected==actual
        records+=len(actual);two_shell+=len(actual)==32
    assert two_shell>0
    return {'random_shadow_rays':12000,'compared_sample_records':records,'rays_with_both_shells':two_shell,
        'original_16_plus_16_nodes_and_order_preserved':True}

def shadow_reuse():
    combinations=0
    # Branch conditions checked at the exact .5 thresholds as well as either side.
    for godrays in (0.,.49,.5,.5001,1.):
        for density in (0.,.0004,.0005,.0005001,.1):
            for ready in (0.,.49,.5,.5001,1.):
                for shadow in (0.,.01,.4,1.):
                    old_air=shadow if godrays>.5 else 1.
                    old_cloud=shadow if density>.0005 and ready>.5 else None
                    new_shadow=shadow if godrays>.5 or (density>.0005 and ready>.5) else 1.
                    new_air=new_shadow if godrays>.5 else 1.
                    new_cloud=new_shadow if density>.0005 and ready>.5 else None
                    assert (old_air,old_cloud)==(new_air,new_cloud);combinations+=1
    return {'threshold_combinations':combinations,'air_and_cloud_shadow_consumers_unchanged':True}

for name,fn in [('source_contracts',source_contracts),('view_scheduler_equivalence',scheduler),
                ('direct_shadow_scheduler_equivalence',shadow_scheduler),('shadow_reuse_thresholds',shadow_reuse)]:test(name,fn)
report={'patch':'V4.2 Repair IPC Fix 1','status':'PASS' if all(x['status']=='PASS' for x in checks.values()) else 'FAIL',
    'unity_shader_compilation_performed':False,'hlsl_compilation_performed':False,'csharp_compilation_performed':False,
    'unity_visual_test_performed':False,'fps_test_performed':False,
    'qualification':'PASS applies ONLY to listed source/numerical checks. Does not establish that Unity IPC crash is resolved.',
    'checks':checks,'source_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in S.iterdir() if p.name in
    ('Clouds.shader','OrbisCloudCommon.cginc','OrbisAtmosphereCommon.cginc','OrbisAtmosphereGLtracy.cginc')}}
REPORT.parent.mkdir(parents=True,exist_ok=True);REPORT.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(report,ensure_ascii=False,indent=2));sys.exit(0 if report['status']=='PASS' else 1)
