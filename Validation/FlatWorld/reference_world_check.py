#!/usr/bin/env python3
"""Independent NumPy reference of the design; NOT execution of C# or a Unity screenshot.
Requires numpy + Pillow. Flood routing mirrors the documented parent-before-child rule.
The reference uses NumPy float64 and may differ near height/climate thresholds from C# float32.
"""
import math,heapq,json,re,datetime,hashlib
from pathlib import Path
from collections import deque
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]; CORE=ROOT/'Assets/ORBIS/Runtime/FlatWorld/Core'
BANDS=np.array(list(map(int,re.search(r'int\[\] Upper=\{([^}]+)',(CORE/'FlatTypes.cs').read_text()).group(1).split(','))))
MASK=np.uint64(0xffffffff)
def hsh(x):
    v=np.asarray(x,dtype=np.uint64)&MASK;v^=v>>np.uint64(16);v=(v*np.uint64(0x7feb352d))&MASK;v^=v>>np.uint64(15);v=(v*np.uint64(0x846ca68b))&MASK;return v^(v>>np.uint64(16))
def seed(text):
    s=2166136261
    for c in text: s=((s^ord(c))*16777619)&0xffffffff
    return s
def unit(v): return (hsh(v)&np.uint64(0xffffff)).astype(float)/16777215

def cell(x,y,period,s):return unit(np.asarray(x%period,dtype=np.uint64)*np.uint64(73428767)^np.asarray(y,dtype=np.int64).astype(np.uint64)*np.uint64(912931)^np.uint64(s))*2-1

def noise(x,y,period,s):
    ix=np.floor(x).astype(np.int64);iy=np.floor(y).astype(np.int64);u=x-ix;v=y-iy;u=u*u*(3-2*u);v=v*v*(3-2*v)
    a=cell(ix,iy,period,s)*(1-u)+cell(ix+1,iy,period,s)*u;b=cell(ix,iy+1,period,s)*(1-u)+cell(ix+1,iy+1,period,s)*u
    return a*(1-v)+b*v

def fbm(u,v,f,s,octaves=5):
    total=0;weight=1;norm=0
    for o in range(octaves):total+=noise(u*f,v*f,f,(s+o*971)&0xffffffff)*weight;norm+=weight;weight*=.5;f*=2
    return total/norm

def generate(width,preset,text='ORBIS-2D-2026'):
    height=width//2;u=(np.arange(width)[None,:]+.5)/width;v=(np.arange(height)[:,None]+.5)/height;s=seed(text)
    broad=fbm(u,v*.5,4,s);detail=fbm(u,v*.5,18,s^19287,4);c=broad*.8+detail*.12+.015
    if preset==1:c=fbm(u,v*.5,12,s,4)*.65-.045
    if preset==2:c=broad*.3+np.cos((u-.5)*math.tau)*.25+.02
    if preset==3:c=broad*.24+np.sqrt(((u-.5)*2)**2+((v-.5)*2)**2)*.44-.25
    ridge=1-np.abs(fbm(u,v*.5,10,s^91231,4));mountain=np.clip((ridge-.55)/.45,0,1)**3*2700
    meters=np.clip(np.where(c>0,c*3500+mountain*np.clip(c*7,0,1),c*11000-15),-4000,4000)
    elevation=np.where(meters>0,BANDS[np.clip(np.searchsorted(BANDS,meters),0,19)],np.rint(meters)).astype(np.int16)
    return elevation

def neighbors(i,w,h):
    x=i%w;y=i//w
    yield y*w+(x-1)%w;yield y*w+(x+1)%w
    if y>0:yield i-w
    if y<h-1:yield i+w

def build(elevation):
    h,w=elevation.shape;n=w*h;e=elevation.ravel();seen=np.zeros(n,bool);water=np.zeros(n,np.uint8);best=[]
    for start in np.flatnonzero(e<=0):
        start=int(start)
        if seen[start]:continue
        part=[];q=deque([start]);seen[start]=True
        while q:
            i=q.popleft();part.append(i)
            for j in neighbors(i,w,h):
                if not seen[j] and e[j]<=0:seen[j]=True;q.append(j)
        if len(part)>len(best):best=part
    water[e<=0]=4
    for i in best:water[i]=1 if e[i]<-1000 else 2 if e[i]<-200 else 3
    ocean=(water>=1)&(water<=3);coast=np.full(n,65535,np.int32);coast[ocean]=0;q=deque(best)
    while q:
        i=q.popleft();d=coast[i]+1
        for j in neighbors(i,w,h):
            if coast[j]>d:coast[j]=d;q.append(j)
    co=coast.reshape(h,w);el=elevation.astype(float);wat=water.reshape(h,w)
    lat=((np.arange(h)+.5)/h-.5)*180;ab=np.abs(lat);rad=lat*math.pi/180
    inland=np.clip(co/(w*.09),0,1)
    ocean_shift=np.sin((np.arange(w)[None,:]+.5)/w*math.tau+rad[:,None])*np.minimum(1,ab[:,None]/50)*2.8
    annual=28-48*(ab[:,None]/90)**1.65-np.maximum(0,el)*.0065+ocean_shift*(1-inland)
    amplitude=(3+20*inland)*np.sin(np.abs(rad[:,None]))**.7
    temp=np.empty((h,w,12));rain=np.empty_like(temp)
    for m in range(12):
        season=np.cos((m-6)*math.tau/12)*np.where(lat>=0,1,-1)
        temp[:,:,m]=np.clip(annual+amplitude*season[:,None],-85,55)
        itcz=10*np.cos((m-6)*math.tau/12);belt=12+170*np.exp(-((lat-itcz)/17)**2)+100*np.exp(-((ab-49)/14)**2)
        direction=np.where((ab>=30)&(ab<65),1,-1);vapor=np.full(h,.8);previous=np.zeros(h);rows=np.arange(h)
        for k in range(w*3):
            x=np.where(direction>0,k%w,w-1-k%w);height=np.maximum(0,el[rows,x]);iswater=wat[rows,x]!=0
            warmth=np.clip((temp[rows,x,m]+18)/48,.15,1.3)
            vapor=np.where(iswater,vapor*.8+(.8+.4*warmth)*.2,vapor)
            rise=np.maximum(0,height-previous);fall=np.maximum(0,previous-height)
            monsoon=np.where((ab>8)&(ab<34),1+.75*season,1)
            p=np.clip(belt*vapor*(1+np.minimum(2.8,rise/650))*np.exp(-fall/1500)*monsoon,0,850)
            if k>=w*2:rain[rows,x,m]=p
            vapor=np.clip(vapor*np.where(iswater,.996,.988)-p*.0008+.008,.055,1.5);previous=height
    filled=e.copy();filled[ocean]=0;visited=ocean.copy();heap=[(0,int(i)) for i in best];heapq.heapify(heap);parent=np.full(n,-1,np.int32);order=[]
    if not heap:
        lowest=int(np.argmin(e));visited[lowest]=True;heap=[(int(e[lowest]),lowest)]
    while heap:
        _,i=heapq.heappop(heap);order.append(i)
        for j in neighbors(i,w,h):
            if visited[j]:continue
            visited[j]=True;parent[j]=i;filled[j]=max(e[j],filled[i]);heapq.heappush(heap,(int(filled[j]),j))
    runoff=rain.sum(2).ravel()/1200;runoff[ocean]=0;inputs=float(runoff.sum())
    for i in reversed(order):
        if parent[i]>=0:runoff[parent[i]]+=runoff[i]
    islake=(filled.astype(int)-e.astype(int)>=100)|(e<=0)
    water[~ocean&islake]=4;water[~ocean&~islake&((runoff>=max(18,n/2600))&(parent>=0))]=5;water[~ocean&~islake&~((runoff>=max(18,n/2600))&(parent>=0))]=0
    rank=np.empty(n,np.int32);rank[order]=np.arange(n);mask=parent>=0
    assert len(order)==n and len(set(order))==n
    assert np.all(rank[parent[mask]]<rank[mask]) and np.all(filled[parent[mask]]<=filled[mask])
    assert abs(runoff[parent<0].sum()-inputs)<max(.001,inputs*.0001)
    assert np.isfinite(temp).all() and np.isfinite(rain).all() and rain.min()>=0
    assert all(v in BANDS for v in np.unique(e[e>0]))
    # PNG integer-height roundtrip check including the zero sea-level boundary.
    samples=np.array([-4000,-2200,-1000,-600,-200,-80,-1,0,*BANDS]);gray=np.rint((samples+4000)/8000*65535).astype(np.uint16)
    decoded=np.rint(gray.astype(float)/65535*8000-4000).astype(int);assert np.array_equal(samples,decoded)
    # South-first data <-> north-first authoring image must roundtrip exactly.
    assert np.array_equal(elevation,np.flipud(np.flipud(elevation)))
    report={'width':w,'height':h,'tiles':n,'zones':n//16,'water_counts':{str(int(k)):int(v) for k,v in zip(*np.unique(water,return_counts=True))},'land_band_levels_present':len(np.unique(e[e>0])),'min_elevation':int(e.min()),'max_elevation':int(e.max()),'monthly_temperature_min_max':[float(temp.min()),float(temp.max())],'monthly_rain_min_max':[float(rain.min()),float(rain.max())],'drainage_dag':True,'runoff_mass_error':float(runoff[parent<0].sum()-inputs),'height16_integer_roundtrip':True,'csharp_executed':False}
    return report,water.reshape(h,w),temp,rain

def main():
    reports=[];out=ROOT/'Examples/FlatWorld';out.mkdir(parents=True,exist_ok=True)
    for preset in range(4):
        elevation=generate(256,preset);assert np.array_equal(elevation,generate(256,preset));report,water,temp,rain=build(elevation);report['preset']=preset;reports.append(report)
        print('reference',preset,report['water_counts'],flush=True)
        if preset==0:
            gray=np.rint((elevation.astype(float)+4000)/8000*65535).astype(np.uint16)
            Image.fromarray(np.flipud(gray)).save(out/'reference-continents-height16.png')
            color=np.zeros((*elevation.shape,3),np.uint8)
            colors=[0x738e5d,0x183950,0x245c78,0x449295,0x599ba7,0x74b0bd]
            for k,h in enumerate(colors):color[water==k]=[(h>>16)&255,(h>>8)&255,h&255]
            land=water==0;factor=np.clip(1-elevation.astype(float)/11000,.62,1)
            color[land]=(color[land]*factor[land,None]).astype(np.uint8)
            Image.fromarray(np.flipud(color)).resize((1024,512),Image.Resampling.NEAREST).save(out/'reference-water-terrain.png')
    # Corner cases: no ocean, all ocean, exact level and isolated below-sea freshwater.
    for name,elevation in [('all_land',np.full((32,64),100,np.int16)),('all_ocean',np.full((32,64),-80,np.int16))]:
        r,*_=build(elevation);r['preset']=name;reports.append(r)
    final={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Independent NumPy float64 reference design check; not execution of Unity/C# source. Preview is a reference raster, not a Unity screenshot.','status':'PASS','worlds':reports,'source_sha256':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in CORE.glob('*.cs') if f.name in ['FlatTypes.cs','FlatGenerator.cs','FlatHydrology.cs','FlatClimate.cs']},'unity_editor_executed':False}
    (ROOT/'Validation/FlatWorld/reference-world-report.json').write_text(json.dumps(final,ensure_ascii=False,indent=2))
    (out/'README.md').write_text('''# 离线参考图与高度图\n\n这些图来自 Validation/FlatWorld/reference_world_check.py 的独立 NumPy 参考模型，**不是 Unity 实机截图，也不承诺与 C# float32 在边界逐格一致**。\n\nreference-continents-height16.png 为 256×128、北向在上、16-bit 灰度的完整地床图，可把绝对路径填入新场景“高度图路径”，点击“导入为新世界”。黑色 −4000m，白色 +4000m。\n\nreference-water-terrain.png 仅用于查看高度/排水流程是否产生海洋、大陆与淡水分布；不包含最终 UI、文明、细节材质或星球渲染。\n\n导入重新生成气候、排水与文明，不导入该图的颜色或水体语义。原生完整状态应使用 .orbis 存档。\n''',encoding='utf-8')
    print('PASS reference model: 4 presets + all-land + all-ocean; drainage, conservation, bands, seed, image orientation/roundtrip.')
if __name__=='__main__':main()
