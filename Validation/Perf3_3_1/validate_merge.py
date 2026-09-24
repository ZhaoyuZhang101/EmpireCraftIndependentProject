"""Integration/source checks and independent projection model. NOT Unity/C# compilation."""
from pathlib import Path
import re,json,math,hashlib,xml.etree.ElementTree as ET
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1];A=ROOT/'Assets';P=A/'ORBIS/Runtime/FlatWorld/Presentation';S=A/'ORBIS/Resources/ORBIS/FlatWorld'
checks=[]
def check(test,message):
 if not test:raise AssertionError(message)
 checks.append(message)
app=(P/'FlatWorldApp.cs').read_text();nav=(P/'FlatScrollNavigator.cs').read_text()
ui=ET.parse(S/'FlatWorld.uxml');ids={e.attrib['name']:e.tag.rsplit('}',1)[-1] for e in ui.iter() if 'name' in e.attrib}
for name in re.findall(r'Bind\("([^"]+)",',app):check(ids.get(name)=='Button','Button '+name)
for kind,name in re.findall(r'root.Q<([\w]+)>\("([^"]+)"\)',app):check(ids.get(name)==kind,'Bound control '+name)
for tab in ['World','Terrain','Civs','Files']:check(ids.get('tab'+tab)=='Button' and 'panel'+tab in ids,'Tab '+tab)
for stale in ['cloudOpacity','autoSun','sun','dayDuration','wave','swe','bathymetry','cloudShadows']:
 check(stale not in ids,'Removed redundant control '+stale)
check('Navigation { get { return navigation;' in app,'Editor navigation API exists')
for forbidden in ['map.Weather','maps.Weather','BindVisual(','FlatVisualCache','MapLayer.Ecosystems','World.Visual']:
 check(not any(forbidden in p.read_text() for p in P.glob('*.cs')),'No incompatible patch API '+forbidden)
for path in A.rglob('*.cs'):
 for name in re.findall(r'Resources.Load<[^>]+>\("([^"\n]+)"\)',path.read_text()):
  if name=='ORBIS/CloudNoiseExternal':continue # Baseline intentionally generates this optional texture at runtime.
  check(any((A/'ORBIS/Resources').glob(name+'.*')),'Resource '+name)
for path in A.rglob('*'):
 if path.suffix not in ['.shader','.cginc']:continue
 for name in re.findall(r'#include\s+"([^"]+)"',path.read_text()):
  if name in ['UnityCG.cginc','Lighting.cginc','AutoLight.cginc']:continue
  check((path.parent/name).is_file(),'Shader include '+name)
 for line in path.read_text().splitlines():
  check(not re.search(r'\S\s*#(?:ifdef|ifndef|endif|if |else)',line),'Preprocessor directive starts line')
guids={}
for path in A.rglob('*.meta'):
 m=re.search(r'^guid:\s*(\w+)',path.read_text(),re.M)
 if m:check(m[1] not in guids,'Unique GUID '+str(path.relative_to(A)));guids[m[1]]=str(path)
for path in A.rglob('*'):
 if path.is_file() and path.suffix!='.meta':check(path.with_name(path.name+'.meta').is_file(),'Asset metadata '+str(path.relative_to(A)))
# Independent sphere/plane mapping, tangent frame and inverse tile invariance.
def wrap(x,p):return x-math.floor(x/p)*p
def sphere(u,v):
 lon=(u-.5)*math.tau;lat=(v-.5)*math.pi
 return math.cos(lat)*math.sin(lon),math.sin(lat),-math.cos(lat)*math.cos(lon)
worst=0
for w in [64,256,512,1024]:
 h=w//2
 for x in [0,1,w//2,w-2,w-1]:
  for y in [0,1,h//2,h-2,h-1]:
   u=(x+.5)/w;v=(y+.5)/h;p=sphere(u,v)
   invu=wrap(math.atan2(p[0],-p[2])/math.tau+.5,1);invv=math.asin(p[1])/math.pi+.5
   worst=max(worst,abs(invu-u),abs(invv-v))
   check(int(invu*w)==x and int(invv*h)==y,'Sphere inverse tile')
   for laps in [-20,-3,0,3,20]:check(int(wrap(x+.5+w*laps,w))==x,'Longitude copy tile identity')
for w in [64,256,512,1024]:
 for start in [w-8,0,8]:
  for offset in [.25,7.5,15.75]:
   canonical=wrap(start+offset,w)
   check(abs(wrap(canonical-start,w)-offset)<1e-6,'SWE seam sample agreement')
check('pixels <= minimum * 1.01f' in nav,'Wheel cannot skip settled overview on exit')
check('flatGeometry ? flatMesh : lattice' in nav and 'UploadMeshData(true)' in nav,'Flat quad and immutable globe lattice')
check('routeVertices' in nav and 'var v = routeVertices' in nav,'Route staging buffers reused')
check('foreach(int i in farms)' in (A/'ORBIS/Runtime/FlatWorld/Core/FlatSimulation.cs').read_text(),'Indexed farm traversal')
check('GetBuffers(cw*ch)' in (P/'FlatMapRenderer.cs').read_text(),'Chunk staging buffers reused')
# Syntax parser checks all C# files, not only the changed files.
syntax='not available'
try:
 from tree_sitter import Language,Parser
 import tree_sitter_c_sharp as cs
 parser=Parser(Language(cs.language()))
 files=list(A.rglob('*.cs'))
 for path in files:check(not parser.parse(path.read_bytes()).root_node.has_error,'C# syntax '+str(path.relative_to(A)))
 syntax=str(len(files))+' C# files parsed; no semantic compilation'
except ImportError:pass
report={'status':'PASS','scope':__doc__,'checks':len(checks),'max_uv_roundtrip_error':worst,'syntax':syntax,'unity_editor_executed':False,'native_csharp_executed':False,'source_sha256':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in A.rglob('*') if p.suffix in ['.cs','.cginc','.shader','.uxml','.uss']}}
(HERE/'integration-report.json').write_text(json.dumps(report,indent=2))
print(json.dumps({k:v for k,v in report.items() if k!='source_sha256'},indent=2))
