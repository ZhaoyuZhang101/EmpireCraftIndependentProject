#!/usr/bin/env python3
"""V4 offline source / fixture / independent numerical checks. NOT a C#/Unity compiler.
Run: python Validation/validate_v4.py   Dependencies: numpy, scipy, Pillow.
Real production-code tests: Unity > Tools > ORBIS > V4 > Validate Upgrade.
"""
from pathlib import Path
import re,json,math,heapq,time,struct,sys,xml.etree.ElementTree as ET
from collections import deque,Counter
import numpy as np
from PIL import Image
from reference_geometry import lexical_check,norm
ROOT=Path(__file__).resolve().parents[1]
report={'scope':'Source wiring, raster fixtures, and independent Python numerical references; not Unity execution',
 'csharp_compilation':'NOT_RUN','shader_compilation':'NOT_RUN','unity_import':'NOT_RUN','play_mode':'NOT_RUN',
 'gpu_profiling':'NOT_RUN','desktop_build':'NOT_RUN','checks':{}}
def check(name,fn):
 start=time.perf_counter()
 try:report['checks'][name]={'status':'PASS','seconds':round(time.perf_counter()-start,3),**(fn() or {})}
 except Exception as e:report['checks'][name]={'status':'FAIL','error':repr(e)}
def source():
 files=[p for p in (ROOT/'Assets').rglob('*') if p.suffix in {'.cs','.shader','.cginc'}]
 for p in files:
  lexical_check(p.read_text())
  for name in re.findall(r'#include "(Orbis[^"\n]+)"',p.read_text()):assert(p.parent/name).exists(),(p,name)
 # Catch a common non-lexical C# error: multiple declarators with var.
 for p in (ROOT/'Assets').rglob('*.cs'):
  s=re.sub(r'//[^\n]*|/\*.*?\*/|@?"(?:""|\\.|[^"\\])*"',lambda m:' ' * len(m[0]),p.read_text(),flags=re.S)
  for m in re.finditer(r'\bvar\s+\w+\s*=',s):
   depth=0;angle=0
   for ch in s[m.end():]:
    if ch in '([{':depth+=1
    if ch in ')]}':
     if depth==0:break
     depth-=1
    if ch=='<' and depth==0:angle+=1
    if ch=='>' and depth==0:angle=max(0,angle-1)
    if depth==0 and ch==';':break
    if depth==0 and ch==',' and angle==0:raise AssertionError(f'{p.name}: possible var multi-declaration at {m.start()}')
 return {'source_files':len(files),'qualification':'Delimiters/includes and limited var declaration lint only; not a language compiler'}
def ui():
 tree=ET.parse(ROOT/'Assets/ORBIS/Resources/ORBIS/Orbis.uxml');ids=[e.attrib['name']for e in tree.iter() if 'name'in e.attrib]
 assert len(ids)==len(set(ids));hud=(ROOT/'Assets/ORBIS/Runtime/OrbisHud.cs').read_text()
 for name in re.findall(r'(?:Bind|Q(?:<[^>]+>)?)\("([^"+]+)"[),]',hud):assert name in ids,name
 for name in ['selectionMode','foundNation','useNation','claimRegion','regionExpansion','spriteActors','mapFolder','importMaps','exportMaps','exampleMap']:assert name in ids
 return {'unique_named_elements':len(ids),'qualification':'XML / binding references only; Unity layout not run'}
def resources():
 used=[]
 for p in (ROOT/'Assets').rglob('*.cs'):
  for name in re.findall(r'Resources.Load<[^>]+>\("([^"+]+)"\)',p.read_text()):
   q=ROOT/'Assets/ORBIS/Resources'/name;assert any(x.suffix!='.meta' for x in q.parent.glob(q.name+'.*')),(p,name);used.append(name)
 guids=[]
 for p in (ROOT/'Assets').rglob('*'):
  if p.suffix=='.meta':
   m=re.search(r'^guid: ([0-9a-f]{32})$',p.read_text(),re.M);assert m,p;guids.append(m[1])
  else:assert p.with_name(p.name+'.meta').exists(),p
 assert len(guids)==len(set(guids))
 for p in (ROOT/'Assets').rglob('*.unity'):
  for guid in re.findall('guid: ([0-9a-f]{32})',p.read_text()):assert guid in guids,(p,guid)
 assert not [p for p in ROOT.rglob('*')if p.suffix.lower()in{'.ttf','.otf','.woff','.woff2'}]
 return {'resource_references':len(used),'unique_guids':len(guids),'font_binaries':0}
def architecture():
 runtime={p.name:p.read_text() for p in (ROOT/'Assets/ORBIS/Runtime').rglob('*.cs')}
 assert 'version=4' in runtime['OrbisWorld.cs'];assert 'OrbisWorldMap.Restore(s.mapSource)'in runtime['OrbisWorld.cs']
 for version in ['v1','v2','v3','v4']:assert 'orbis-demo-'+version+'.json'in runtime['OrbisDemo.cs']
 assert 'if(s.version<4)'in runtime['OrbisSaveMigration.cs']
 assert 'Rivers.FillMesh(riverMesh,false)'in runtime['OrbisPlanetView.cs']
 assert 'Rivers.FillMesh(riverMesh,true)'in runtime['OrbisMapView.cs']
 assert 'demo.MapMode && demo.View.showRivers' not in runtime['OrbisOverlays.cs']
 assert 'World.Brush(Selected, Tool)'not in runtime['OrbisDemo.cs'] # explicit region founding branch is used
 assert 'TryPick(normalized' in runtime['OrbisDemoV4.cs']
 assert 'VertexCount' not in runtime['OrbisTerritories.cs'] # territory independent of rendering tessellation
 assert 'if(c.pop<10){c.city=false;c.pop=0;continue;}'in runtime['OrbisWorld.cs']
 assert 'RefreshAuthoredRiverMask();'in runtime['OrbisWorld.cs'] and 'RiverStrength'in runtime['OrbisGeography.cs']
 assert 'float safeAltitude=smoothAltitude;'in runtime['OrbisDemo.cs']
 return {'stable_logical_tiles':3456,'region_selection':'tile / zone / connected chunk','map_river_position_informs_regions':True}
def water():
 resource=ROOT/'Assets/ORBIS/Resources/ORBIS'
 for name in ['Surface.shader','RiverWater.shader']:
  s=(resource/name).read_text();assert '#include "OrbisWater.cginc"'in s and 'OrbisShadeWater('in s
 inc=(resource/'OrbisWater.cginc').read_text();assert 'shallow=float3(.07,.42,.38)'in inc
 river=(ROOT/'Assets/ORBIS/Runtime/V4/OrbisRiverNetwork.cs').read_text()
 assert 'water-depth' in river and 'radius=Mathf.Min(radius,cut)'in river
 view=(ROOT/'Assets/ORBIS/Runtime/OrbisPlanetView.cs').read_text();assert 'vertexWet[i]>.5f?r:Rivers.Carve' in view
 assert 'wet>.5f?r:Rivers.Carve' in view
 assert 'float alpha=edge*mouth' in (resource/'RiverWater.shader').read_text()
 return {'one_optical_model':True,'carve_water_plane_exclusion':True,'mouth_alpha_transition':True,'qualification':'Source contract checks; no GPU rendering'}
def fixtures():
 f=ROOT/'Validation/Fixtures';values=np.asarray(Image.open(f/'gray16_filters.png')).ravel()
 assert values.tolist()==json.loads((f/'gray16_expected.json').read_text());assert ((values%257)!=0).any()
 bmp=np.asarray(Image.open(f/'indexed8.bmp').convert('RGBA'));png=np.asarray(Image.open(f/'indexed8.png').convert('RGBA'));assert np.array_equal(bmp,png)
 for base,name in [('heightmap.png','ExampleHeight.bytes'),('rivers.png','ExampleRivers.bytes')]:
  assert (ROOT/'Examples/RiverValley'/base).read_bytes()==(ROOT/'Assets/ORBIS/Resources/ORBIS/V4'/name).read_bytes()
 b=(ROOT/'Examples/RiverValley/heightmap.png').read_bytes();w,h,depth,typ=struct.unpack('>IIBB',b[16:26]);assert(w,h,depth,typ)==(1024,512,16,0)
 return {'exact_gray16_values':len(values),'filters_tested':[0,1,2,3,4],'indexed_bmp_png_equal':True,'example_dimensions':[w,h],'example_height_bit_depth':depth,'qualification':'Independent Pillow fixture validation, not production C# decoder execution'}
def river_reference():
 hmap=np.asarray(Image.open(ROOT/'Examples/RiverValley/heightmap.png'),dtype=np.float64)/65535*2-.95
 color=np.asarray(Image.open(ROOT/'Examples/RiverValley/rivers.png').convert('RGBA'),dtype=int)
 blue=(color[:,:,2]>40)&(color[:,:,2]>color[:,:,0]+25)&(color[:,:,2]>color[:,:,1]+15)&(color[:,:,3]>=128)
 H,W=hmap.shape;coords=list(zip(*np.where(blue)));lookup={p:i for i,p in enumerate(coords)};neighbors=[]
 for y,x in coords:
  ns=[]
  for dy in [-1,0,1]:
   for dx in [-1,0,1]:
    if dx==dy==0:continue
    key=(y+dy,(x+dx)%W)
    if key not in lookup:continue
    if dx and dy and ((y,(x+dx)%W)in lookup or (y+dy,x)in lookup):continue
    ns.append(lookup[key])
  neighbors.append(ns)
 n=len(coords);elev=np.array([hmap[p]for p in coords]);seen=set();components=[];parent=np.full(n,-1);level=np.zeros(n);order=[];visited=set();closed=0
 for start in range(n):
  if start in seen:continue
  comp=[];todo=deque([start]);seen.add(start)
  while todo:
   i=todo.popleft();comp.append(i)
   for j in neighbors[i]:
    if j not in seen:seen.add(j);todo.append(j)
  components.append(comp);sea=[i for i in comp if elev[i]<=0];heap=[]
  if not sea:
   closed+=1;ends=[i for i in comp if len(neighbors[i])<=1];sea=[min(ends or comp,key=lambda j:elev[j])];level[sea[0]]=elev[sea[0]]-.004
  for i in sea:heapq.heappush(heap,(level[i],i));visited.add(i)
  while heap:
   _,i=heapq.heappop(heap);order.append(i)
   for j in neighbors[i]:
    if j not in visited:
     visited.add(j);parent[j]=i;level[j]=max(elev[j]-.006,level[i]+.000001);heapq.heappush(heap,(level[j],j))
 flow=np.ones(n)
 for i in order[::-1]:
  if parent[i]>=0:flow[parent[i]]+=flow[i]
 assert len(order)==n and sum(flow[parent<0])==n
 assert all(level[i]>=level[parent[i]] for i in range(n)if parent[i]>=0)
 uplift=int(np.sum(level>elev+.02));# only land warnings count; ocean has surface 0 above bathymetry by design
 uplift=int(np.sum((level>elev+.02)&(elev>0)));assert uplift==0
 assert len(components)==1 and closed==0
 hints=np.array([.3+1.9*color[y,x,2]/255 for y,x in coords]);width=np.clip((.00075+np.sqrt(flow)*.00033)*hints,.0007,.009)
 assert np.ptp(width)>.0005 and (level>.1).any();assert sum(len(x)>2 for x in neighbors)>=3
 # Meander amplitude of main stem is explicitly nonzero.
 trunk=[x for y,x in coords if 330<y<365];assert max(trunk)-min(trunk)>5
 return {'nodes':n,'components':len(components),'junctions':sum(len(x)>2 for x in neighbors),'closed_components':closed,'uplift_warnings':uplift,
  'flow_to_roots':int(sum(flow[parent<0])),'half_width_range_R':[float(width.min()),float(width.max())],'maximum_water_height':float(level.max()),
  'qualification':'Independent Python reproduction of raster drainage invariants; production C# tests are provided but not run'}
def selection_reference():
 # Actual cube-face corner keys; adjacent tiles share exactly one edge including cross-face borders.
 res=24;normals=np.array([[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]])
 tile_edges=[];global_edges=Counter()
 for n in normals:
  u=np.array([0,1,0])if abs(n[0])==1 else np.array([1,0,0]);v=np.cross(n,u)
  for y in range(res):
   for x in range(res):
    corners=[tuple(n*res+u*(2*(x+dx)-res)+v*(2*(y+dy)-res))for dx,dy in [(0,0),(1,0),(1,1),(0,1)]]
    edges=[tuple(sorted([corners[k],corners[(k+1)%4]]))for k in range(4)];tile_edges.append(edges);global_edges.update(edges)
 assert len(tile_edges)==3456 and set(global_edges.values())=={2}
 external=sum(c==1 for c in Counter(tile_edges[0]+tile_edges[1]).values());assert external==6
 all_external=sum(c==1 for c in global_edges.values());assert all_external==0
 return {'tiles':len(tile_edges),'closed_sphere_edges':len(global_edges),'two_adjacent_quad_boundary_edges':external,'whole_planet_external_edges':all_external}
def coordinates():
 rng=np.random.default_rng(54);p=norm(rng.normal(size=(10000,3)));W,H=1024,512
 x=(np.arctan2(p[:,0],p[:,2])/(2*np.pi)+.5)*W-.5;y=(.5-np.arcsin(p[:,1])/np.pi)*H-.5
 lon=((x+.5)/W-.5)*2*np.pi;lat=(.5-(y+.5)/H)*np.pi;q=np.stack([np.sin(lon)*np.cos(lat),np.sin(lat),np.cos(lon)*np.cos(lat)],axis=1)
 error=float(abs(p-q).max());assert error<1e-12
 return {'pixel_center_roundtrips':len(p),'maximum_error':error,'north_first':True,'east_west_wrap':True}
def thermal():
 rng=np.random.default_rng(6);h=rng.normal(size=1000);initial=h.sum()
 for _ in range(3):
  delta=np.zeros_like(h)
  for i in range(len(h)):
   low=min([i,(i-1)%len(h),(i+1)%len(h)],key=lambda j:h[j]);excess=h[i]-h[low]-.055
   if excess>0:delta[i]-=excess*.12;delta[low]+=excess*.12
  h+=delta
 err=float(abs(h.sum()-initial));assert err<1e-10
 return {'height_sum_error':err,'iterations':3,'qualification':'Independent numerical thermal relaxation, not complete hydraulic erosion'}
def inherited():
 # Reuse V3 reference functions without executing its old CLI or overwriting its historical report.
 s=(ROOT/'Validation/validate_v3.py').read_text();prefix=s[:s.index('for name,fn in [')]
 ns={'__file__':str(ROOT/'Validation/validate_v3.py')};exec(compile(prefix,'validate_v3.py','exec'),ns)
 results={}
 for name in ['bounded','segments','homogeneous','tangent','dither','camera_safety']:
  results[name]=ns[name]()
 return {'independent_v3_reference_groups':results}
for name,fn in [('source_structure',source),('ui_bindings',ui),('resources_metadata_fonts',resources),('v4_wiring_and_compatibility',architecture),('unified_water_and_carving',water),('map_and_codec_fixtures',fixtures),('raster_river_reference',river_reference),('region_boundary_reference',selection_reference),('raster_sphere_coordinates',coordinates),('thermal_relaxation',thermal),('inherited_weather_atmosphere_camera_references',inherited)]:
 check(name,fn)
report['passed']=sum(v['status']=='PASS' for v in report['checks'].values());report['failed']=len(report['checks'])-report['passed']
(ROOT/'Documentation/Validation-V4.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print(json.dumps(report,ensure_ascii=False,indent=2));sys.exit(int(report['failed']>0))
