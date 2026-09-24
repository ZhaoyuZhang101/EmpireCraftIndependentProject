"""Synthetic GLES shader illustration; NOT a screenshot from the Unity game."""
from pathlib import Path
import sys,json
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
sys.path.insert(0,str(ROOT/'Validation/Perf3_2'))
# Load only fixture setup, avoiding rerunning the historical render assertions.
p=ROOT/'Validation/Perf3_2/render_fragment_fixtures.py'
s=p.read_text().split("baseimage=render(")[0]
ns={'__file__':str(p)};exec(compile(s,str(p),'exec'),ns)
ns['OUTPUT']=HERE;ns['uniform']('_DetailWeight',1);ns['uniform']('_PixelsPerTile',64)
# Crop the same fragment, not a resampled screenshot. Ground UV spans 16x10 tiles.
name='detail';probe=(ROOT/'Validation/Perf3_2/flat_fragment_probe.glsl').read_text()
probe=probe.replace('i.uv=vUV;','i.uv=(vec2(22,10)+vUV*vec2(16,10))/vec2(64,32);')
# program() needs a path. Store derived shader for reproducibility.
(ROOT/'Validation/Perf3_2/detail_fragment_probe.glsl').write_text(probe)
pr=ns['program'](name);ns['current']=pr;ns['use'](pr)
for unit,(key,obj) in enumerate(ns['textures'].items()):ns['bind'](key,obj,unit)
ns['bind']('_SkyTex',ns['skytex'],8)
for key,values in ns['base'].items():ns['uniform'](key,*values)
# Add sample farmland and roads to synthetic test data (no game saves touched).
info=ns['info'].copy();info[13:16,25:28,2]=2;info[12,24:33,2]=3;info[13:17,29,2]=3
ns['bind']('_InfoTex',ns['tex'](info,False),2)
ns['uniform']('_DetailWeight',1);ns['uniform']('_PixelsPerTile',64)
arr=ns['render']('ALGORITHM_detail_layer',1024,640)
assert arr[:,:,:3].std()>15
ns['uniform']('_DetailWeight',0);base=ns['render']('ALGORITHM_detail_off',1024,640,False)
change=float(ns['np'].mean(ns['np'].abs(arr.astype(float)-base.astype(float))))
assert change>.5
(HERE/'detail-report.json').write_text(json.dumps({'status':'PASS','scope':__doc__,'mean_pixel_change':change,'unity_executed':False},indent=2))
print('Detail shader execution PASS; mean byte difference:',change)
