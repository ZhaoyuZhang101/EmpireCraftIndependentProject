"""Source-derived GLES seam/patch probes on synthetic data; NOT Unity screenshots or D3D11 tests."""
from pathlib import Path
import sys,json,re
H=Path(__file__).resolve().parent;R=H.parents[1];V=R/'Validation/Perf3_2'
sys.path.insert(0,str(V))
import generate_fragment_probes as g
for file,name in [('FlatWorld/FlatMap.shader','flat'),('FlatWorld/FlatSkyBake.shader','sky'),('Surface.shader','surface')]:
 (V/(name+'_fragment_probe.glsl')).write_text(g.trans(file))
p=V/'render_fragment_fixtures.py';ns={'__file__':str(p)};exec(compile(p.read_text().split('baseimage=render(')[0],str(p),'exec'),ns)
ns['OUTPUT']=H
probe=g.trans('FlatWorld/FlatMap.shader');np=ns['np']
def make(code):
 (V/'seam_fragment_probe.glsl').write_text(code)
 pr=ns['program']('seam');ns['current']=pr;ns['use'](pr)
 for unit,(key,obj) in enumerate(ns['textures'].items()):ns['bind'](key,obj,unit)
 ns['bind']('_SkyTex',ns['skytex'],8)
 ns['bind']('_SWEPreviousState',ns['textures']['_SWEState'],9)
 for key,values in ns['base'].items():ns['uniform'](key,*values)
 ns['uniform']('_SWEBlend',1);ns['uniform']('_DetailWeight',1);ns['uniform']('_PixelsPerTile',32)
def render():return ns['render']('seam_probe',512,256,False)
images=[]
for lap in [-1,0,1]:
 make(probe.replace('i.uv=vUV;',f'i.uv=vec2({lap}.0-.125+vUV.x*.25,.25+vUV.y*.25);'))
 images.append(render())
errors=[int(np.abs(a.astype(int)-images[1].astype(int)).max()) for a in images]
assert max(errors)<=2,errors
# Derivative footprint at longitude zero: the source filter must not see a W-tile jump.
water=(R/'Assets/ORBIS/Resources/ORBIS/FlatWorld/Water/FlatWater.cginc').read_text()
a=water.index('    float2 cellDx=');b=water.index('    slopes*=',a)
body=water[a:b].replace('float2','vec2').replace('ddx(','dFdx(').replace('ddy(','dFdy(')
body=body.replace('length(cellDx*6)','length(cellDx*6.0)').replace('length(cellDy*6)','length(cellDy*6.0)')
main='void main(){vec2 cell=vec2(fract(-.125+vUV.x*.25)*64.0,8.0+vUV.y*8.0);'+body+'targetColor=vec4(vec3(footprint),1.0);}'
make(probe[:probe.rindex('void main()')]+main);foot=render()[:,:,0]
assert int(foot.max())-int(foot.min())<=1 and int(foot.max())<255
# The exact SWE sampler must address equivalent rectangles identically.
base=probe[:probe.rindex('void main()')]
main='''void main(){vec2 cell=vec2(fract(-.125+vUV.x*.25)*64.0,8.0+vUV.y*8.0);
 vec3 value=FlatPatchContribution(_SWEState,_SWERect,1.0,cell);
 targetColor=vec4(vec2(.5)+value.xy,value.z,1.0);}'''
make(base+main)
state=np.zeros((64,64,4),np.uint8);state[:,:,:]=[210,65,200,255]
ns['bind']('_SWEState',ns['tex'](state,True),7)
ns['uniform']('_SWERect',60,4,16,16);right=render()
ns['uniform']('_SWERect',-4,4,16,16);left=render()
patchError=int(np.abs(right.astype(int)-left.astype(int)).max());assert patchError<=1
assert right[:,:,0].max()>145,'SWE contribution unexpectedly invisible'
report={'status':'PASS','scope':__doc__,'full_fragment_wrap_max_byte_errors':errors,'water_footprint_max_byte_variation':int(foot.max())-int(foot.min()),'SWE_rect_rebase_max_byte_error':patchError,'unity_executed':False,'d3d11_executed':False}
(H/'seam-gpu-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
