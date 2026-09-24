"""Execute the shipped cloud density and water mask in Mesa GLES using synthetic inputs.
The shipped light quadrature is also executed; camera and final lighting are simplified; NOT Unity screenshots.
"""
from pathlib import Path
import sys,re,json,ctypes as ct
import numpy as np
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1];S=ROOT/'Assets/ORBIS/Resources/ORBIS'
sys.path.insert(0,str(ROOT/'Validation/Perf3_2'))
import generate_fragment_probes as g
p=ROOT/'Validation/Perf3_2/render_fragment_fixtures.py'
ns={'__file__':str(p)};exec(compile(p.read_text().split('baseimage=render(')[0],str(p),'exec'),ns)
ns['OUTPUT']=HERE
# Real SkyBake source, sufficient density-map resolution for the volume probe.
ns['current']=ns['programs']['sky'];ns['use'](ns['current']);ns['bind']('_ClimateTex',ns['climatetex'],0)
ns['uniform']('_VisualTime',18);ns['uniform']('_MonthBlend',.4)
weather=ns['render']('ALGORITHM_weather_density',512,256)
weathertex=ns['tex'](weather)
common=(S/'OrbisCloudCommon.cginc').read_text()
a=common.index('float2 OrbisWeatherUV(');b=common.index('float2 OrbisSphere(',a)
source=(S/'FlatWorld/ScrollCloudSource.cginc').read_text().replace('#include "ScrollHeight.cginc"',(S/'FlatWorld/ScrollHeight.cginc').read_text())
native=(S/'OrbisAtmosphereCommon.cginc').read_text()
light=native[native.index('float OrbisLightTau('):native.index('// HPVolumeCloud-inspired reduced phi_fwd')]
geometry=common[common.index('float2 OrbisSphere('):common.index('float OrbisNoiseAtAge(')]
hlsl='''sampler3D _CloudNoise;float _Density,_Cover,_ProbeMode,_ProbeOffset,_ProbeAlpha;
#define ORBIS_PI 3.14159265359
'''+common[a:b]+geometry+source+'''
float _ScrollCloudSource,_LightSteps,_CloudMS;
#define ORBIS_CLOUD_TOP 1.190
#define ORBIS_CLOUD_BASE 1.0004
#define ORBIS_EXTINCTION 135.0
float OrbisCloudDensity(float3 p){return ScrollClimateDensity(p);}
'''+light+'''
struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
float4 frag(v2f i):SV_Target
{
 float2 uv=i.uv+float2(_ProbeOffset,0);
 float3 up,ro,rd;float begin=0,end=.20;
 float3 bg=float3(.07,.20,.28);
 if(_ProbeMode<.5)
 {
  float lon=(uv.x-.5)*6.283185307,lat=(uv.y-.5)*3.141592654;
  up=float3(sin(lon)*cos(lat),sin(lat),cos(lon)*cos(lat));
  ro=up*1.20;rd=-up;end=1.20-ScrollCloudGround(uv);
 }
 else
 {
  ro=float3(0,0,3.1);rd=normalize(float3((uv-.5)*2.5,-2.3));
  float b=dot(ro,rd),disc=b*b-dot(ro,ro)+1.2*1.2;
  if(disc<0)return float4(0,0,0,1);
  begin=-b-sqrt(disc);end=-b+sqrt(disc);
  float groundDisc=b*b-dot(ro,ro)+1.024*1.024;
  if(groundDisc>0)end=min(end,-b-sqrt(groundDisc));
  bg=groundDisc>0?float3(.10,.27,.18):float3(0,0,0);
 }
 float stepSize=(end-begin)/64,transmission=1;float3 light=float3(0,0,0);
 float3 sun=normalize(float3(.4,.5,.8));
 [loop] for(int j=0;j<64;j++)
 {
  float3 at=ro+rd*(begin+(j+.5)*stepSize);
  float density=ScrollClimateDensity(at);
  float shadow=LightField(at,sun).x;
  float phase=.24+.76*shadow*smoothstep(-.15,.2,dot(normalize(at),sun));
  float removed=1-exp(-density*135*stepSize);
  light+=transmission*float3(.90,.94,1)*phase*removed;transmission*=1-removed;
 }
 if(_ProbeAlpha>.5)return float4(float3(1-transmission,1-transmission,1-transmission),1);
 return float4(bg*transmission+light,1);
}
'''
old=g.include_file;g.include_file=lambda path:'CGPROGRAM'+hlsl+'ENDCG'
try:probe=g.trans('FlatWorld/FlatMap.shader')
finally:g.include_file=old
probe=probe.replace('sampler3D _CloudNoise;','uniform sampler3D _CloudNoise;').replace('precision highp int;','precision highp int;\nprecision highp sampler3D;')
probe=probe.replace('[fastopt]', '')
probe=probe.replace('vec4 tauFirst=0.0','vec4 tauFirst=vec4(0.0)').replace('vec2 tauLast=0.0','vec2 tauLast=vec2(0.0)')
probe=probe.replace('tex3Dlod(', 'hlslTexture3DLod(').replace('uniform sampler3D _CloudNoise;', 'uniform sampler3D _CloudNoise;\nvec4 hlslTexture3DLod(sampler3D s,vec4 uv){return textureLod(s,uv.xyz,uv.w);}')
path=ROOT/'Validation/Perf3_2/cloud_volume_fragment_probe.glsl';path.write_text(probe)
pr=ns['program']('cloud_volume');ns['current']=pr;ns['use'](pr)
ns['bind']('_ScrollWeatherTex',weathertex,0);ns['bind']('_ScrollHeightTex',ns['textures']['_VisualTex'],1)
f=ns['f'];U=ns['U'];I=ns['I'];P=ns['P']
noise=np.frombuffer((S/'CloudNoiseV42.bytes').read_bytes(),dtype=np.uint8).copy()
obj=U();f('glGenTextures',None,[I,ct.POINTER(U)])(1,ct.byref(obj));f('glActiveTexture',None,[U])(0x84C2);f('glBindTexture',None,[U,U])(0x806F,obj)
for param,value in [(0x2801,0x2601),(0x2800,0x2601),(0x2802,0x2901),(0x2803,0x2901),(0x8072,0x2901)]:f('glTexParameteri',None,[U,U,I])(0x806F,param,value)
f('glTexImage3D',None,[U,I,I,I,I,I,I,U,U,P])(0x806F,0,0x8058,64,64,64,0,0x1908,0x1401,noise.ctypes.data)
ns['ui'](ns['getloc'](pr,b'_CloudNoise'),2)
for k,v in {'_ScrollCloudSource':(1,),'_LightSteps':(3,),'_CloudMS':(1,),'_Density':(1,),'_Cover':(1,),'_ScrollWeatherTime':(18,),'_ScrollSourceSize':(64,32,0,0),'_ScrollHeightScale':(.000006,),'_ScrollPoleHeights':(0,0),'_ProbeMode':(0,),'_ProbeOffset':(0,),'_ProbeAlpha':(0,)}.items():ns['uniform'](k,*v)
ns['render']('ALGORITHM_volume_atlas',512,256)
ns['uniform']('_ProbeAlpha',1);alpha=ns['render']('volume_alpha',256,128,False)
ns['uniform']('_ProbeOffset',1);seam=ns['render']('volume_wrap',256,128,False)
wrap_error=int(np.abs(alpha.astype(int)-seam.astype(int)).max());assert wrap_error<=3,wrap_error
assert (alpha[:,:,0]>60).mean()>.02,'Cloud layer invisible'
assert (alpha[:,:,0]<5).mean()>.05,'Cloud cover blankets all geography'
ns['uniform']('_Density',0);off=ns['render']('volume_off',256,128,False);assert off[:,:,:3].max()==0
ns['uniform']('_Density',1);ns['uniform']('_ProbeOffset',0);ns['uniform']('_ProbeAlpha',0);ns['uniform']('_ProbeMode',1)
ns['render']('ALGORITHM_volume_globe',512,512)
# A coast whose smoothed zero contour retreats inside the first sea tile.
# Old river mask stopped at the face, leaving a dry plug before the zero contour.
visual=ns['visual'].copy();visual[:,:,0]=100;visual[:,:,1]=-.9;visual[:,:,2]=0;visual[:,:,3]=.6
visual[:,32:,0]=-100;visual[:,32:,1]=1.8;visual[:,32:,2]=100;visual[:,32,1]=.15
hydro=np.zeros_like(ns['hydro']);mixing=np.zeros_like(hydro);hydro[16,31]=[2,1,0,.18];mixing[16,31,3]=3
maskprobe=g.trans('FlatWorld/FlatMap.shader').replace('i.uv=vUV;','i.uv=(vec2(29,14)+vUV*vec2(7,5))/vec2(64,32);')
mark='vec3 illumination=';idx=maskprobe.index(mark);maskprobe=maskprobe[:idx]+'return vec4(vec3(waterMask),1.0);\n'+maskprobe[idx:]
(ROOT/'Validation/Perf3_2/mouth_mask_fragment_probe.glsl').write_text(maskprobe)
pr=ns['program']('mouth_mask');ns['current']=pr;ns['use'](pr)
for unit,(key,obj) in enumerate(ns['textures'].items()):ns['bind'](key,obj,unit)
tv=ns['tex'](visual);th=ns['tex'](hydro);tm=ns['tex'](mixing)
ns['bind']('_VisualTex',tv,6);ns['bind']('_HydroA',th,3);ns['bind']('_HydroB',tm,4)
for key,values in ns['base'].items():ns['uniform'](key,*values)
ns['uniform']('_PixelsPerTile',64)
mask=ns['render']('ALGORITHM_estuary_coverage',448,320)
# Midline from river centre (31.5,16.5) to sea centre (32.5,16.5).
minimum=int(mask[159:161,160:224,0].min());assert minimum>=250,minimum
report={'status':'PASS','scope':__doc__,'unity_executed':False,'cloud_opaque_fraction':float((alpha[:,:,0]>60).mean()),'clear_fraction':float((alpha[:,:,0]<5).mean()),'wrap_max_byte_error':wrap_error,'cloud_disable_max_byte':int(off[:,:,:3].max()),'estuary_corridor_min_coverage_byte':minimum}
(HERE/'cloud-water-gles-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
