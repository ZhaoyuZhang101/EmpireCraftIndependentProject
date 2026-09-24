#!/usr/bin/env python3
"""Source wiring/structural contracts only. NOT a C#/HLSL/Unity compiler."""
from pathlib import Path
import re,json,datetime,xml.etree.ElementTree as ET,sys,hashlib
ROOT=Path(__file__).resolve().parents[2];HERE=Path(__file__).resolve().parent
A=ROOT/'Assets/ORBIS';R=A/'Resources/ORBIS';C=A/'Runtime/FlatWorld/Core';P=A/'Runtime/FlatWorld/Presentation'
issues=[];checks=0
def check(ok,label):
 global checks
 checks+=1
 if not ok:issues.append(label)
ui=ET.parse(R/'FlatWorld/FlatWorld.uxml'); ids={e.get('name'):e.tag.rsplit('}',1)[-1] for e in ui.iter() if e.get('name')}
app=(P/'FlatWorldApp.cs').read_text()
for control in ['smoothTerrain','vegetation','bathymetry','relief','clouds','cloudShadows','atmosphere','dayNight','rainEffects','lightning','nightLights','autoSun']:
 check(ids.get(control)=='Toggle','UXML Toggle '+control)
 check('"'+control+'"' in app,'App binding '+control)
for control,kind in [('cloudQuality','DropdownField'),('cloudOpacity','Slider'),('skyStatus','Label')]:check(ids.get(control)==kind,'UXML type '+control)
for tab in ['World','Terrain','Water','Climate','Civs','Files']:
 check('tab'+tab in ids and 'panel'+tab in ids,'Dynamic tab/panel '+tab)
check('map.Sky.Tick(' in app and 'BindSky(map.Material' in app and 'BindSky(globe.View.SurfaceMaterial' in app,'Sky connections')
check('Simulation.Step' not in (P/'FlatGlobePresenter.cs').read_text(),'Globe does not step simulation')
check('Snapshot.day=(float)maps.World.Day' in (P/'FlatGlobePresenter.cs').read_text(),'double-to-float calendar cast')
check('WorldData' in (P/'FlatSkyRenderer.cs').read_text() and 'new WorldData' not in (P/'FlatSkyRenderer.cs').read_text(),'Sky shares authoritative data')
check('!animate&&rendered&&!climateChanged' in (P/'FlatSkyRenderer.cs').read_text(),'Paused cloud-cache work skip')
for f in ['FlatSurfaceSampling.cs','FlatWaterGeometry.cs','FlatEcology.cs','FlatRenderSelfTests.cs']:
 check((C/f).exists() and (C/(f+'.meta')).exists(),'Core resource '+f)
check('FlatRenderSelfTests.Run()' in (C/'FlatSelfTests.cs').read_text(),'Native core regression wired')
clone=(C/'FlatTerrainAnalysis.cs').read_text().replace(' ','')
check('Array.Copy(Vegetation,f.Vegetation,Forms.Length)' in clone and 'Array.Copy(Canopy,f.Canopy,Forms.Length)' in clone,'Vegetation/canopy deep clone')
# Extract native enum declarations, not string occurrences in documentation.
for filename,enum,expected in [('FlatTypes.cs','Biome',12),('FlatEcology.cs','VegetationCover',14)]:
 text=(C/filename).read_text();m=re.search(r'enum\s+'+enum+r'\s*(?::[^{}]+)?\{([^}]+)\}',text,re.S)
 body=re.sub(r'//[^\n]*|/\*.*?\*/','',m.group(1),flags=re.S) if m else ''
 check(len([x for x in body.split(',') if x.strip()])==expected,enum+' count remains '+str(expected))
check('Version=1' in (C/'FlatSave.cs').read_text().replace(' ',''),'Save format v1')
# Expand known source includes, ignoring Unity-supplied headers; inspect selected pass.
def expand(path,seen=None):
 seen=seen or set()
 if path in seen:return ''
 seen.add(path);s=path.read_text()
 def repl(m):
  child=path.parent/m.group(1)
  return expand(child.resolve(),seen) if child.exists() else '// Unity include omitted from inventory'
 return re.sub(r'#include\s+"([^"]+)"',repl,s)
def inventory(path,no_swe=False):
 text=expand(path);text=text.split('CGPROGRAM',1)[1].split('ENDCG',1)[0]
 if no_swe:text=re.sub(r'#ifndef ORBIS_FLAT_NO_SWE\b.*?#endif','',text,flags=re.S)
 return sorted({n.strip() for group in re.findall(r'\bsampler2D\s+([^;]+);',text) for n in group.split(',')})
samplers={'Surface_lit':inventory(R/'Surface.shader',True),'FlatMap':inventory(R/'FlatWorld/FlatMap.shader'),'FlatSkyBake':inventory(R/'FlatWorld/FlatSkyBake.shader')}
check(len(samplers['Surface_lit'])==9,'Surface nine source samplers')
check(len(samplers['FlatMap'])==9,'FlatMap nine source samplers')
check(len(samplers['FlatSkyBake'])==1,'Sky cache one source sampler')
surface=(R/'Surface.shader').read_text(); includes=re.findall(r'#include\s+"([^"]+)"',surface)
check(not any(x in includes for x in ['OrbisOptics.cginc','OrbisWater.cginc','OrbisCloudCommon.cginc']),'Heavy surface includes separated')
check('SURFACE_LIT_PERF32' in surface and 'SURFACE_DEPTH_PERF32' in surface,'Named surface passes')
check((R/'FlatWorld/SurfaceFallback.shader').exists(),'Fallback resource exists')
planet=list(A.rglob('OrbisPlanetView.cs'))[0].read_text()
check('UsingSurfaceFallback' in planet and 'FlatWorld/SurfaceFallback' in planet and 'isSupported' in planet,'Explicit fallback wiring')
water=(R/'FlatWorld/Water/FlatWater.cginc').read_text();field=(R/'FlatWorld/FlatSurfaceField.cginc').read_text()
check('#include "HPWaterOptics.cginc"' in water,'HPWater optics actively included')
check('FlatBezierDistance' in water and 'FlatField(' in field,'Curve / field shader connections')
check('SmoothShore' in (C/'FlatSurfaceSampling.cs').read_text(),'Constrained display-field smoothing')
check('_Vegetation<.5' in field,'Uniform vegetation skip')
for fn in ['Surface.shader','FlatWorld/FlatMap.shader']:
 check('if(_ShowWater>.5)' in (R/fn).read_text(),'Uniform water optics skip '+fn)
for suffix in ['.ttf','.otf','.woff','.woff2']:
 check(not list(ROOT.rglob('*'+suffix)),'No font binary '+suffix)
report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':__doc__,'status':'FAIL' if issues else 'PASS','checks':checks,'issues':issues,'sampler_source_inventory':samplers,'unity_editor_executed':False,'native_csharp_executed':False,'native_hlsl_executed':False}
(HERE/'source-contracts-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2));sys.exit(bool(issues))
