"""Targeted HLSL reserved-identifier guard, not a shader compiler."""
from pathlib import Path
import re,json
H=Path(__file__).resolve().parent;R=H.parents[1]
reserved=set('triangle triangleadj line lineadj point sample linear centroid noperspective nointerpolation vector matrix texture sampler register packoffset cbuffer tbuffer struct class interface namespace typedef technique technique10 technique11 pass compile discard in out inout uniform shared groupshared static const volatile row_major column_major precise patch'.split())
def violations(source):
 source=re.sub(r'/\*.*?\*/|//[^\n]*','',source,flags=re.S)
 # Numeric declaration/function/parameter names, including subsequent initialized declarators.
 names=re.findall(r'\b(?:float|half|fixed|int|uint|bool)(?:[1-4](?:x[1-4])?)?\s+([A-Za-z_]\w*)',source)
 names+=re.findall(r',\s*([A-Za-z_]\w*)\s*=',source)
 return sorted(set(names)&reserved)
assert violations('float triangle=1; float a=0, sample=2;')==['sample','triangle']
assert not violations('float coniferEdgeDistance=1;')
files=[p for p in (R/'Assets/ORBIS').rglob('*') if p.suffix in ['.cginc','.shader']]
for p in files:
 bad=violations(p.read_text());assert not bad,(str(p),bad)
report={'status':'PASS','scope':__doc__,'files_checked':len(files),'d3d11_compiled':False,'reference':'https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-appendix-keywords'}
(H/'shader-identifiers-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
