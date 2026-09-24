"""Compile source-derived cloud density/light and Markley column mapping in GLES; no Unity engine macros."""
from pathlib import Path
import sys,re,json
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1];S=ROOT/'Assets/ORBIS/Resources/ORBIS'
sys.path.insert(0,str(ROOT/'Validation/Perf3_2'))
import generate_fragment_probes as g
from egl_harness import shader
common=(S/'OrbisCloudCommon.cginc').read_text();native=(S/'OrbisAtmosphereCommon.cginc').read_text()
source=(S/'FlatWorld/ScrollCloudSource.cginc').read_text().replace('#include "ScrollHeight.cginc"',(S/'FlatWorld/ScrollHeight.cginc').read_text())
a=common.index('float2 OrbisWeatherUV(');b=common.index('float2 OrbisSphere(',a)
geometry=common[b:common.index('float OrbisNoiseAtAge(')]
light=native[native.index('float OrbisLightTau('):native.index('// HPVolumeCloud-inspired reduced phi_fwd')]
hlsl='''sampler3D _CloudNoise;float _Density,_Cover;
#define ORBIS_PI 3.14159265359
'''+common[a:b]+geometry+source+'''
float _ScrollCloudSource,_LightSteps,_CloudMS;
#define ORBIS_CLOUD_TOP 1.190
#define ORBIS_CLOUD_BASE 1.0004
#define ORBIS_EXTINCTION 135.0
float OrbisCloudDensity(float3 p){return ScrollClimateDensity(p);}
'''+light+(S/'FlatWorld/Markley.cginc').read_text()+'''
struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
float4 frag(v2f i):SV_Target{float3 up=MarkleyDirection(i.uv);float d=ScrollClimateDensity(up*1.02);float light=LightField(up*1.02,normalize(float3(.4,.5,.8))).x;return float4(d,light,0,1);}
'''
old=g.include_file;g.include_file=lambda p:'CGPROGRAM'+hlsl+'ENDCG'
try:probe=g.trans('FlatWorld/FlatMap.shader')
finally:g.include_file=old
probe=re.sub(r'(?<!uniform )sampler3D (_\w+);',r'uniform sampler3D \1;',probe)
probe=probe.replace('precision highp int;','precision highp int;\nprecision highp sampler3D;\nvec4 hlslTexture3DLod(sampler3D s,vec4 uv){return textureLod(s,uv.xyz,uv.w);}')
probe=probe.replace('[fastopt]','').replace('tex3Dlod(','hlslTexture3DLod(').replace('vec4 tauFirst=0.0','vec4 tauFirst=vec4(0.0)').replace('vec2 tauLast=0.0','vec2 tauLast=vec2(0.0)')
(HERE/'cloud_markley_probe.glsl').write_text(probe);shader(0x8B30,probe)
r=dict(status='PASS',scope=__doc__,unity_executed=False);(HERE/'cloud-shader-report.json').write_text(json.dumps(r,indent=2));print(r)
