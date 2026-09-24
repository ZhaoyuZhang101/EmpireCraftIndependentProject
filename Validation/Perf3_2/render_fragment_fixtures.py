#!/usr/bin/env python3
"""GLES rendering of source-derived fragment probes with controlled synthetic data.
NOT Unity screenshots, a Unity build, an IPC reproduction, or a hardware performance test.
"""
from pathlib import Path
import ctypes as ct,sys,numpy as np,json,datetime,hashlib
from PIL import Image
from egl_harness import *
HERE=Path(__file__).resolve().parent
vs='''#version 300 es
out vec2 vUV;void main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);vUV=p;gl_Position=vec4(p*2.0-1.0,0,1);}'''
def program(name):
 pr=f('glCreateProgram',U,[])();f('glAttachShader',None,[U,U])(pr,shader(0x8B31,vs));f('glAttachShader',None,[U,U])(pr,shader(0x8B30,(HERE/(name+'_fragment_probe.glsl')).read_text()));f('glLinkProgram',None,[U])(pr)
 ok=I();f('glGetProgramiv',None,[U,U,ct.POINTER(I)])(pr,0x8B82,ct.byref(ok))
 if not ok.value:
  log=ct.create_string_buffer(100000);f('glGetProgramInfoLog',None,[U,I,P,P])(pr,100000,None,log);raise RuntimeError(log.value.decode())
 return pr
programs={name:program(name) for name in ['flat','sky','surface']}
# Also compile the source-derived linear-colour-space paths.
for name in programs:shader(0x8B30,(HERE/(name+'_fragment_probe.glsl')).read_text().replace('#define UNITY_COLORSPACE_GAMMA',''))
getloc=f('glGetUniformLocation',I,[U,ct.c_char_p]);u1=f('glUniform1f',None,[I,F]);u2=f('glUniform2f',None,[I,F,F]);u3=f('glUniform3f',None,[I,F,F,F]);u4=f('glUniform4f',None,[I,F,F,F,F]);ui=f('glUniform1i',None,[I,I]);use=f('glUseProgram',None,[U]);current=0
vao=U();f('glGenVertexArrays',None,[I,ct.POINTER(U)])(1,ct.byref(vao));f('glBindVertexArray',None,[U])(vao)
def uniform(name,*values):[None,u1,u2,u3,u4][len(values)](getloc(current,name.encode()),*values)
def tex(data,linear=True):
 data=np.ascontiguousarray(data);obj=U();f('glGenTextures',None,[I,ct.POINTER(U)])(1,ct.byref(obj));f('glBindTexture',None,[U,U])(0x0DE1,obj.value)
 for param,value in [(0x2801,0x2601 if linear else 0x2600),(0x2800,0x2601 if linear else 0x2600),(0x2802,0x2901),(0x2803,0x812F)]:f('glTexParameteri',None,[U,U,I])(0x0DE1,param,value)
 float_data=data.dtype==np.float32
 f('glTexImage2D',None,[U,I,I,I,I,I,U,U,P])(0x0DE1,0,0x8814 if float_data else 0x8058,data.shape[1],data.shape[0],0,0x1908,0x1406 if float_data else 0x1401,data.ctypes.data)
 return obj.value
def bind(name,obj,unit):f('glActiveTexture',None,[U])(0x84C0+unit);f('glBindTexture',None,[U,U])(0x0DE1,obj);ui(getloc(current,name.encode()),unit)
def render(name,width=1024,height=512,save=True):
 f('glViewport',None,[I,I,I,I])(0,0,width,height);f('glDrawArrays',None,[U,I,I])(4,0,3);f('glFinish',None,[])();error=f('glGetError',U,[])();assert error==0,hex(error)
 data=(ct.c_ubyte*(width*height*4))();f('glReadPixels',None,[I,I,I,I,U,U,P])(0,0,width,height,0x1908,0x1401,data)
 error=f('glGetError',U,[])();assert error==0,hex(error)
 image=np.frombuffer(bytes(data),dtype=np.uint8).reshape(height,width,4).copy()
 if save:Image.fromarray(image[::-1]).convert('RGB').save(OUTPUT/(name+'.png'))
 return image
# 64x32 fixture: one island, an inland lake, a connected variable-width river,
# discrete vegetation regions and quantised authoring heights.
w,h=64,32;Y,X=np.mgrid[:h,:w];x=X+.5;y=Y+.5
oval=((x-30)/24)**2+((y-16)/12)**2;lake=((x-38)/5.4)**2+((y-17)/3.8)**2<1
wet=(oval>1)|lake
coords=np.stack([X.ravel(),Y.ravel()],axis=1)
def squared_distance(seeds):
 if not seeds.any():return np.full((h,w),1e12,np.float32)
 points=coords[seeds.ravel()];dx=np.abs(coords[:,None,0]-points[None,:,0]);dx=np.minimum(dx,w-dx);dy=coords[:,None,1]-points[None,:,1]
 return (dx*dx+dy*dy).min(axis=1).reshape(h,w).astype(np.float32)
shore=np.clip(np.where(wet,np.sqrt(squared_distance(~wet))-.5,-np.sqrt(squared_distance(wet))+.5),-8,8)
for _ in range(2):
 padded=np.pad(shore,((1,1),(0,0)),mode='edge');smooth=np.zeros_like(shore)
 for dy,wy in [(-1,1),(0,2),(1,1)]:
  for dx,wx in [(-1,1),(0,2),(1,1)]:smooth+=np.roll(padded[1+dy:1+dy+h],dx,axis=1)*wx*wy/16
 shore=smooth
shore=np.where(wet,np.maximum(.15,shore),np.minimum(-.15,shore))
elevation=np.maximum(100,np.round((200+600*(1-oval)+1800*np.exp(-((x-35)**2+(y-25)**2)/14))/100)*100)
elevation=np.where(oval>1,-np.minimum(3900,80+np.maximum(oval-1,0)*1600+150*(np.sin(x*.3)+1)),elevation)
elevation=np.where(lake,200+100*np.cos((x-38)*.5)*np.cos((y-17)*.5),elevation)
depth=np.where(oval>1,-elevation,np.where(lake,600-elevation,0))
canopy=np.where(x<25,.2,np.where(x<35,.74,np.where(x<46,.94,.1)));canopy=np.where(wet,0,canopy)
colors=np.zeros((h,w,4),np.uint8);colors[:,:,:3]=[143,166,106]
colors[x>=25,:3]=[86,128,91];colors[x>=35,:3]=[40,93,70];colors[x>=46,:3]=[178,165,112];colors[elevation>1700,:3]=[155,160,145];colors[wet,:3]=[30,77,116];colors[:,:,3]=255
for iy,ix in np.argwhere(wet&(shore<=2)):
 candidates=[((dx*dx+dy*dy),(ix+dx)%w,iy+dy) for dy in range(-2,3) for dx in range(-2,3) if 0<=iy+dy<h and not wet[iy+dy,(ix+dx)%w]]
 if candidates:_,xx,yy=min(candidates);colors[iy,ix,:3]=colors[yy,xx,:3]
hydro=np.zeros((h,w,4),np.float32);hydro[:,:,0]=depth;hydro[wet,1]=.15;hydro[wet,2]=-.06
mixing=np.zeros_like(hydro);mixing[:,:,0]=shore;mixing[lake,2]=1
geo=np.zeros_like(hydro);geo[:,:,0]=elevation;geo[:,:,1]=np.where(np.abs(shore)<1.1,1,0)
info=np.zeros((h,w,4),np.uint8);info[:,:,1]=np.where(wet,3,0);info[16,28,2]=1;info[18,32,2]=1
path=[];xx,yy=28,23
while xx>4:
 path.append((xx,yy));xx-=1;path.append((xx,yy));xx-=1
 if yy>15:path.append((xx,yy));yy-=1
path.append((xx,yy))
for j,(xx,yy) in enumerate(path):
 if wet[yy,xx]:continue
 hydro[yy,xx,3]=min(.4,.07+j*.012);hydro[yy,xx,0]=.6+j*.19;depth[yy,xx]=hydro[yy,xx,0];info[yy,xx,1]=5;mixing[yy,xx,2]=1
 for point in ([path[j-1]] if j>0 else [])+([path[j+1]] if j+1<len(path) else []):
  dx=point[0]-xx;dy=point[1]-yy;direction=0 if dx<0 else 1 if dx>0 else 2 if dy<0 else 3;mixing[yy,xx,3]+=1<<direction
 if j+1<len(path):hydro[yy,xx,1:3]=np.array(path[j+1])-np.array((xx,yy))
visual=np.stack([elevation,shore,depth,canopy],axis=2).astype(np.float32)
owner=np.zeros((h,w,4),np.uint8);owner[x<30,:]=[195,147,85,1];owner[x>=30,:]=[65,143,165,2]
blank=np.zeros((2,2,4),np.uint8);blank[:,:,3]=255;swe=blank.copy();swe[:,:,:2]=128
climate=np.ones((h,w,4),np.float32)*.68;climate[:,:,0]=climate[:,:,1]=.35+.3*np.sin(x*.22)*np.sin(y*.19)
textures={name:tex(data,linear) for name,data,linear in [('_MainTex',colors,True),('_OwnerTex',owner,False),('_InfoTex',info,False),('_HydroA',hydro,True),('_HydroB',mixing,True),('_GeoTex',geo,True),('_VisualTex',visual,True),('_SWEState',swe,True)]}
climatetex=tex(climate);current=programs['sky'];use(current);bind('_ClimateTex',climatetex,0);uniform('_VisualTime',18);uniform('_MonthBlend',.4);sky18=render('ALGORITHM_cloud_cache_t18',256,128);skytex=tex(sky18)
uniform('_VisualTime',78);sky78=render('ALGORITHM_cloud_cache_t78',256,128);movement=float(np.mean(np.abs(sky18.astype(float)-sky78.astype(float))));assert movement>.5,'Clouds did not change over one minute'
current=programs['flat'];use(current)
for unit,(name,obj) in enumerate(textures.items()):bind(name,obj,unit)
bind('_SkyTex',skytex,8)
base={'_MapSize':(w,h,1/w,1/h),'_Selected':(-100,-100,-100,-100),'_SWEReady':(0,),'_FieldReady':(1,),'_SmoothTerrain':(1,),'_Vegetation':(1,),'_Bathymetry':(1,),'_Relief':(1,),'_PixelsPerTile':(16,),'_WaterView':(1,),'_Natural':(1,),'_Political':(0,),'_ShowWater':(1,),'_ShowBanks':(1,),'_ShowPlumes':(1,),'_WaveAmount':(.85,),'_WaterClock':(12,),'_ShowContours':(0,),'_ContourInterval':(100,),'_Grid':(0,),'_SkyReady':(1,),'_FlatSun':(0,.2,1),'_FlatDayNight':(0,),'_FlatClouds':(0,),'_FlatCloudShadows':(0,),'_FlatAtmosphere':(0,),'_CloudOpacity':(.55,),'_FlatMoon':(.5,),'_SkyClock':(18,),'_FlatRain':(0,),'_FlatLightning':(0,),'_FlatNightLights':(1,)}
for key,values in base.items():uniform(key,*values)
baseimage=render('ALGORITHM_continuous_map')
uniform('_ShowContours',1);contours=render('ALGORITHM_adaptive_contours');uniform('_ShowContours',0)
uniform('_Bathymetry',0);without_depth=render('ALGORITHM_depth_enhancement_off');uniform('_Bathymetry',1)
uniform('_FlatClouds',1);uniform('_FlatCloudShadows',1);uniform('_FlatAtmosphere',1);clouds=render('ALGORITHM_clouds_on');uniform('_FlatDayNight',1);dusk=render('ALGORITHM_day_night')
uniform('_FlatDayNight',0);uniform('_FlatClouds',0);uniform('_FlatCloudShadows',0);uniform('_FlatAtmosphere',0);uniform('_ShowWater',0);simplewater=render('ALGORITHM_simple_water_switch');uniform('_ShowWater',1)
uniform('_Natural',0);uniform('_WaterView',0);diagnostic=render('ALGORITHM_discrete_data_layer',save=False)
expected=np.repeat(np.repeat(colors,16,axis=0),16,axis=1);diagnostic_error=int(np.abs(diagnostic.astype(int)-expected.astype(int)).max());assert diagnostic_error<=1,diagnostic_error
# Surface uses the SAME fragment water code but a sphere-radial lighting harness.
current=programs['surface'];use(current)
for unit,(name,obj) in enumerate(textures.items()):
 remap={'_MainTex':'_FlatTerrain','_OwnerTex':'_FlatOwners','_InfoTex':'_FlatInfo'}
 if name!='_SWEState':bind(remap.get(name,name),obj,unit)
bind('_SkyTex',skytex,8)
for key,values in base.items():uniform(key,*values)
uniform('_FlatWorldReady',1);uniform('_Tint',1,1,1,1);uniform('_Night',1);uniform('_SolarEnergy',1);uniform('_Sun',0,.2,1);uniform('_FlatCloudShadows',1);uniform('_OrbisNightParams',0,0,0,0);uniform('_OrbisMoon',0,1,0,.035);uniform('_OrbisMoonColor',.9,.94,1,1)
surface=render('ALGORITHM_surface_fragment_atlas_probe',save=False)
assert surface[:,:,:3].std()>10
for array in [baseimage,contours,clouds,dusk,simplewater,surface]:assert not np.all(array[:,:,:3]==np.array([255,0,255]))
report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'PASS','api':getstr(0x1F02).decode(),'renderer':getstr(0x1F01).decode(),'scope':'Source-derived fragment programs compiled in Gamma and Linear GLSL ES and executed on synthetic data. Engine transforms and clipping are stubs. NOT Unity/HLSL/D3D compilation or game screenshots.','visual_colour_space':'Gamma; Linear fragment variants compile-only','compiled_fragment_variants':6,'rendered_cases':10,'cloud_change_t18_to_t78_mean_byte':movement,'diagnostic_discrete_layer_max_byte_error':diagnostic_error,'bathymetry_switch_mean_byte_change':float(np.mean(np.abs(baseimage.astype(float)-without_depth.astype(float)))),'contour_switch_mean_byte_change':float(np.mean(np.abs(baseimage.astype(float)-contours.astype(float)))),'cloud_switch_mean_byte_change':float(np.mean(np.abs(baseimage.astype(float)-clouds.astype(float)))),'unity_editor_executed':False,'d3d11_executed':False,'source_hlsl_sha256':{str(f.relative_to(HERE.parents[1])):hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted((HERE.parents[1]/'Assets/ORBIS/Resources/ORBIS').rglob('*')) if f.suffix in {'.shader','.cginc'}},'source_probe_sha256':{n:hashlib.sha256((HERE/(n+'_fragment_probe.glsl')).read_bytes()).hexdigest() for n in programs}}
(HERE/'gles-fragment-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
