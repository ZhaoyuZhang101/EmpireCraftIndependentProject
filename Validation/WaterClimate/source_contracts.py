#!/usr/bin/env python3
"""Source-level regressions. Does not replace Unity import or HLSL backend compilation."""
from pathlib import Path
import re, json, datetime, xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[2];issues=[];checks=0

def check(ok,msg):
 global checks
 checks+=1
 if not ok:issues.append(msg)

def uncomment(s):return re.sub(r'/\*[\s\S]*?\*/|//[^\n]*','',s)
# The real failure survives balanced-bracket checks: a reserved word used as an identifier.
for path in (ROOT/'Assets').rglob('*'):
 if path.suffix not in {'.shader','.hlsl','.cginc'}:continue
 text=uncomment(path.read_text())
 for match in re.finditer(r'\b(?:float|half|fixed|int|uint|bool)(?:[1-4](?:x[1-4])?)?\s+(line|point|triangle|linear|centroid|sample)\b',text):
  check(False,f'Reserved HLSL identifier {match.group(1)} in {path.relative_to(ROOT)}')
 for match in re.finditer(r'\bstep\s*\(',text):
  level=1;i=match.end();commas=0;nonspace=False
  while i<len(text) and level:
   c=text[i]
   if c=='(':level+=1
   elif c==')':level-=1
   elif c==',' and level==1:commas+=1
   if level and not c.isspace():nonspace=True
   i+=1
  check(commas==1 and nonspace,f'step must have two arguments: {path.relative_to(ROOT)} at offset {match.start()}')
ui=ROOT/'Assets/ORBIS/Resources/ORBIS/FlatWorld/FlatWorld.uxml'
ids={n.attrib['name'] for n in ET.parse(ui).iter() if 'name' in n.attrib}
for tab in ['World','Terrain','Water','Climate','Civs','Files']:
 check('tab'+tab in ids and 'panel'+tab in ids,'Missing dynamic tab pair '+tab)
for e in ET.parse(ui).iter():check('style' not in e.attrib,'Inline UXML style introduced')
uss=ui.with_suffix('.uss').read_text();check(bool(re.search(r'\.is-hidden\s*\{\s*display:\s*none',uss)),'Tab hiding style missing')
app=(ROOT/'Assets/ORBIS/Runtime/FlatWorld/Presentation/FlatWorldApp.cs').read_text()
check('Bind("tab"+name,()=>ShowTab(selectedTab))' in app,'Dynamic tab click binding missing')
check('map.LocalWater.Tick(' in app,'Local solver disconnected from renderer')
check('SetTexture("_HydroA"' in (ROOT/'Assets/ORBIS/Runtime/FlatWorld/Presentation/FlatGlobePresenter.cs').read_text(),'Globe not sharing new data textures')
water=(ROOT/'Assets/ORBIS/Resources/ORBIS/FlatWorld/Water/FlatWater.cginc').read_text()
check('OrbisHPScatter(' in water,'HPWater optical function disconnected')
check('FlatRiverDistance' in water and 'FlatWaterMask' in water,'No procedural river coverage')
check('LineRenderer' not in (ROOT/'Assets/ORBIS/Runtime/FlatWorld/Presentation/FlatMapRenderer.cs').read_text(),'Whole river outlines introduced')
check('#define ORBIS_FLAT_NO_SWE' in (ROOT/'Assets/ORBIS/Resources/ORBIS/Surface.shader').read_text(),'Local SWE sampler leaked into globe')
check('HDRP' not in (ROOT/'Packages/manifest.json').read_text() and 'render-pipelines.high-definition' not in (ROOT/'Packages/manifest.json').read_text(),'HDRP dependency introduced')
check('m_EditorVersion: 6000.0.47f1' in (ROOT/'ProjectSettings/ProjectVersion.txt').read_text(),'Unity baseline version changed')
check('FlatWaterSelfTests.Run()' in (ROOT/'Assets/ORBIS/Runtime/FlatWorld/Core/FlatSelfTests.cs').read_text(),'New managed regressions not wired')
report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Source contracts: reserved HLSL tokens, step arity, dynamic tabs, data/solver/optics wiring, baseline version. NOT C# or shader backend compilation.','checks':checks,'status':'PASS' if not issues else 'FAIL','issues':issues,'unity_editor_executed':False}
(ROOT/'Validation/WaterClimate/source-contracts-report.json').write_text(json.dumps(report,indent=2,ensure_ascii=False))
print(json.dumps(report,indent=2,ensure_ascii=False));raise SystemExit(bool(issues))
