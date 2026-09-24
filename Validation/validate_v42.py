#!/usr/bin/env python3
"""V4.2 source integration + mechanically translated numerical reference checks.
Requires Python 3, numpy, g++ (C++17). Does NOT compile C# or Unity shaders.
The generated C++ extracts the current C# equation bodies at run time; it is a
headless numerical cross-check, not a replacement for the Unity Editor tests.
"""
from pathlib import Path
import re, json, math, subprocess, hashlib, tempfile, sys
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Assets/ORBIS/Runtime/Weather/OrbisAtmosphereCore.cs'
RESULT=ROOT/'Documentation/V4_2/validation_headless.json'

def method(source,name):
    m=re.search(r'(?m)^        (?:public |static )*(?:void|double|bool) '+name+r'\((.*?)\)\s*\{',source,re.S)
    if not m: raise ValueError('No method '+name)
    a=m.end()-1;depth=1;b=a+1
    while depth:
        depth+=(source[b]=='{')-(source[b]=='}');b+=1
    return m.group(1), source[a:b]

def translate(name,args,body):
    # Keep formulas / loop ordering unchanged. Translate only the language glue.
    body=re.sub(r'//[^\n]*','',body)
    body=re.sub(r'throw new \w+Exception\([^;]*\);','throw std::runtime_error("invalid argument");',body)
    body=body.replace('Math.','M::').replace('AtmosVector.','AtmosVector::').replace('new AtmosVector(','AtmosVector(')
    body=body.replace('.Normalized','.Normalized()').replace('uint.MaxValue','UINT32_MAX')
    for x in ('Links','delta'):
        body=body.replace(x+'.Length',x+'.size()')
    body=re.sub(r'Array.Clear\((\w+),([^,]+),([^\)]+)\);',r'std::fill(\1.begin()+\2,\1.begin()+\2+\3,0.0);',body)
    body=body.replace('Array.Clear(delta,0,delta.size());','std::fill(delta.begin(),delta.end(),0.0);')
    body=body.replace('Array.Copy(pressureNext,PressureAnomaly,Count);','PressureAnomaly=pressureNext;')
    body=body.replace('double[] q=Water[s];','auto& q=Water[s];')
    body=body.replace('var bolt=new AtmosphereLightning{','AtmosphereLightning bolt; bolt.')
    if 'AtmosphereLightning bolt;' in body:
        a=body.index('AtmosphereLightning bolt;');b=body.index('};',a)
        sub=body[a:b]
        sub=re.sub(r',(Height|Strength|CloudToGround|Sequence|Time)=',r';bolt.\1=',sub)
        body=body[:a]+sub+';'+body[b+2:]
    body=body.replace('Lightning.Count','Lightning.size()').replace('Lightning.Dequeue()','Lightning.pop_front()').replace('Lightning.Enqueue(bolt)','Lightning.push_back(bolt)')
    body=body.replace('double.IsNaN(v)','std::isnan(v)').replace('double.IsInfinity(v)','std::isinf(v)')
    args=args.replace('bool ocean','bool ocean')
    rettype='void' if name in ['SetForcing','Initialize','Tick','UpdateDynamics','HorizontalTransport','Parcel','VerticalExchange','SurfaceExchange','SaturationAdjustment','ConvertHydrometeors','Sedimentation','Diagnose','AddWarmMoistBubble'] else 'bool' if name=='Finite' else 'double'
    return rettype+' '+name+'('+args+') '+body+'\n'

HEADER=r'''
#include <algorithm>
#include <cmath>
#include <vector>
#include <deque>
#include <cstdint>
#include <iostream>
#include <fstream>
#include <iomanip>
#include <stdexcept>
using std::vector;
namespace M {
 double Max(double a,double b){return std::max(a,b);} double Min(double a,double b){return std::min(a,b);}
 double Exp(double a){return exp(a);} double Log(double a){return log(a);} double Sqrt(double a){return sqrt(a);}
 double Abs(double a){return fabs(a);} double Pow(double a,double b){return pow(a,b);} double Sin(double a){return sin(a);} double Cos(double a){return cos(a);}
 const double PI=3.14159265358979323846;
}
struct AtmosVector {
 double X=0,Y=0,Z=0; AtmosVector(){} AtmosVector(double x,double y,double z):X(x),Y(y),Z(z){}
 double Length()const{return sqrt(X*X+Y*Y+Z*Z);}
 AtmosVector operator+(AtmosVector b)const{return {X+b.X,Y+b.Y,Z+b.Z};}
 AtmosVector operator-(AtmosVector b)const{return {X-b.X,Y-b.Y,Z-b.Z};}
 AtmosVector operator*(double b)const{return {X*b,Y*b,Z*b};}
 AtmosVector operator/(double b)const{return *this*(1/b);}
 AtmosVector Normalized()const{return *this/std::max(1e-20,Length());}
 static double Dot(AtmosVector a,AtmosVector b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
 static AtmosVector Cross(AtmosVector a,AtmosVector b){return {a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X};}
 AtmosVector Tangent(AtmosVector p)const{return *this-p*Dot(*this,p);}
 AtmosVector Limited(double m)const{return Length()>m?*this*(m/Length()):*this;}
 static AtmosVector Lerp(AtmosVector a,AtmosVector b,double t){return a+(b-a)*t;}
};
struct AtmosEdge {int A,B;AtmosVector Direction;double Length,Distance;};
struct AtmosphereLightning {int Cell;double Height,Strength,Time;bool CloudToGround;int64_t Sequence;};
struct Core {
 static constexpr int Layers=12,Species=6;
 static constexpr double PhysicalSecondsPerSecond=45;
 vector<double> Edges={0,300,800,1500,2000,3000,4000,5000,6000,8000,10000,12000,16000};
 vector<double> Height,AirMass,Pressure;
 int Count;
 vector<AtmosVector> Position,Wind,baseWind;vector<AtmosEdge> Links;
 vector<vector<double>> Water;
 vector<double> Area,Temperature,VerticalVelocity,SurfaceWater,Film,Wetness,RainRate,SnowRate,ColdPool,PressureAnomaly;
 vector<double> CAPE,CIN,LCL,CloudBase,CloudTop,Coverage,Charge,Updraft,StormAge;
 vector<int> Stage; std::deque<AtmosphereLightning> Lightning;
 double Time=0,ExternalWater=0,RunoffWater=0,InitialWater=0;int64_t LightningCount=0;
 bool TransportEnabled=true,MicrophysicsEnabled=true,SurfaceExchangeEnabled=true;
 vector<double> surfaceT,solar,soil,boost,terrain,pressureForcing,liftForcing,cooldown,gustLift,orographicLift;
 vector<bool> sea;vector<double> flux,outgoing,limit,delta,temporary,convergence,pressureNext;
 uint32_t random=42042;
 Core(const char* topology) {
  std::ifstream f(topology);int n,e;f>>n>>e;Count=n;Position.resize(n);Area.resize(n);Links.resize(e);
  for(int i=0;i<n;i++)f>>Position[i].X>>Position[i].Y>>Position[i].Z>>Area[i];
  for(auto& l:Links)f>>l.A>>l.B>>l.Direction.X>>l.Direction.Y>>l.Direction.Z>>l.Length>>l.Distance;
  if(!f)throw std::runtime_error("topology load");
  Water.assign(6,vector<double>(n*Layers,0));Temperature.resize(n*Layers);VerticalVelocity.resize(n*Layers);Wind.resize(n*Layers);
  for(auto v:{&SurfaceWater,&Film,&Wetness,&RainRate,&SnowRate,&ColdPool,&PressureAnomaly,&CAPE,&CIN,&LCL,&CloudBase,&CloudTop,&Coverage,&Charge,&Updraft,&StormAge,&surfaceT,&solar,&soil,&boost,&terrain,&pressureForcing,&liftForcing,&cooldown,&gustLift,&orographicLift,&outgoing,&limit,&convergence,&pressureNext})v->resize(n);
  baseWind.resize(n);sea.resize(n);Stage.resize(n);flux.resize(e);delta.resize(n*Layers);temporary.resize(n*Layers);
  Height.resize(Layers);AirMass.resize(Layers);Pressure.resize(Layers);
  for(int k=0;k<Layers;k++){Height[k]=(Edges[k]+Edges[k+1])*.5;Pressure[k]=101325*exp(-Height[k]/8000);AirMass[k]=101325/9.81*(exp(-Edges[k]/8000)-exp(-Edges[k+1]/8000));}
 }
'''
DRIVER=r'''
};
void forcing(Core& a){
 for(int i=0;i<a.Count;i++){
  auto p=a.Position[i];auto east=AtmosVector(p.Z,0,-p.X).Normalized();
  double temp=302-38*std::abs(p.Y);double sunlight=std::max(0.0,p.Z);
  auto wind=east*(.65-1.15*exp(-pow((std::abs(p.Y)-.22)/.18,2)))+AtmosVector(0,1,0).Tangent(p)*(-p.Y*.6);
  a.SetForcing(i,temp,sunlight,.65,p.X<.35,wind,1013,std::max(0.0,p.X-.6)*2200,0,0);
 }
}
AtmosVector centroid(Core& a,int k,int s=1){AtmosVector sum;for(int i=0;i<a.Count;i++)sum=sum+a.Position[i]*(a.Water[s][k*a.Count+i]*a.Area[i]);return sum.Normalized();}
double angle(AtmosVector a,AtmosVector b){return acos(std::clamp(AtmosVector::Dot(a,b),-1.0,1.0));}
void stats(Core& a,int step){
 double path=0,qmin=1e30,qmax=-1e30,tmin=1e30,tmax=-1e30,rain=0,wet=0,cape=0,up=0,top=0,ice=0,g=0,charge=0;int mature=0,cloudy=0;
 for(int i=0;i<a.Count;i++){rain=std::max(rain,a.RainRate[i]);wet=std::max(wet,a.Wetness[i]);cape=std::max(cape,a.CAPE[i]);up=std::max(up,a.Updraft[i]);top=std::max(top,a.CloudTop[i]);charge=std::max(charge,a.Charge[i]);mature+=a.Stage[i]==3;cloudy+=a.Coverage[i]>.2;}
 for(int j=0;j<a.Count*a.Layers;j++){tmin=std::min(tmin,a.Temperature[j]);tmax=std::max(tmax,a.Temperature[j]);ice=std::max(ice,a.Water[2][j]);g=std::max(g,a.Water[5][j]);for(int s=0;s<6;s++){qmin=std::min(qmin,a.Water[s][j]);qmax=std::max(qmax,a.Water[s][j]);}}
 std::cout<<"{\"step\":"<<step<<",\"budget_relative\":"<<a.WaterBudgetError()/a.InitialWater<<",\"min_water\":"<<qmin<<",\"max_water\":"<<qmax<<",\"min_T_K\":"<<tmin<<",\"max_T_K\":"<<tmax<<",\"max_rain_mm_h\":"<<rain<<",\"max_wetness\":"<<wet<<",\"max_CAPE\":"<<cape<<",\"max_updraft_m_s\":"<<up<<",\"max_top_m\":"<<top<<",\"max_ice_path\":"<<ice<<",\"max_graupel_path\":"<<g<<",\"max_charge\":"<<charge<<",\"mature_cells\":"<<mature<<",\"cloudy_cells\":"<<cloudy<<",\"lightning_count\":"<<a.LightningCount<<"}";
}
int main(int argc,char** argv){
 std::cout<<std::setprecision(16);const char* topology=argv[1];
 Core tracer(topology);forcing(tracer);tracer.Initialize();
 for(auto& q:tracer.Water)std::fill(q.begin(),q.end(),0);
 for(int i=0;i<tracer.Count;i++)for(int k=0;k<12;k++){
  auto p=tracer.Position[i];tracer.Water[1][k*tracer.Count+i]=p.Z>.94?1:0;
  tracer.Wind[k*tracer.Count+i]=AtmosVector(p.Z,0,-p.X)*(.018+k*.0012);
 }
 auto start=centroid(tracer,0);double total=tracer.TotalWater();
 for(int n=0;n<500;n++)tracer.HorizontalTransport(.1);
 double err=(tracer.TotalWater()-total)/total;
 std::cout<<"{\"tracer\":{\"relative_error\":"<<err<<",\"low_displacement_rad\":"<<angle(start,centroid(tracer,0))<<",\"high_displacement_rad\":"<<angle(start,centroid(tracer,11))<<"},\"natural\":[";
 Core a(topology);forcing(a);a.Initialize();vector<double> initial=a.Coverage;
 int duration=argc>2?atoi(argv[2]):1200;
 double maxRain=0,maxWet=0;int born=0,dissipated=0;vector<bool> births(a.Count,false),deaths(a.Count,false);
 for(int step=1;step<=duration;step++){
  a.Tick(.1);
  for(int i=0;i<a.Count;i++){maxRain=std::max(maxRain,a.RainRate[i]);maxWet=std::max(maxWet,a.Wetness[i]);births[i]=births[i]||(initial[i]<.02&&a.Coverage[i]>.15);deaths[i]=deaths[i]||(initial[i]>.08&&a.Coverage[i]<.025);}
  if(step%200==0){if(step>200)std::cout<<",";stats(a,step);}
 }
 for(int i=0;i<a.Count;i++){born+=births[i];dissipated+=deaths[i];}
 std::cout<<"],\"natural_summary\":{\"cloud_birth_cells\":"<<born<<",\"cloud_dissipation_cells\":"<<dissipated<<",\"peak_rain_mm_h\":"<<maxRain<<",\"peak_wetness\":"<<maxWet<<"},\"perturbed\":[";
 Core b(topology);forcing(b);b.Initialize();
 // An explicitly imposed initial warm/moist bubble; not a hidden normal-weather storm spawner.
 for(int i=0;i<b.Count;i++)if(b.Position[i].Z>.975)b.AddWarmMoistBubble(i);
 for(int step=1;step<=duration;step++){b.Tick(.1);if(step%200==0){if(step>200)std::cout<<",";stats(b,step);}}
 // Closed local microphysics tests deliberately supply condensate to isolate conversion.
 Core c(topology);forcing(c);c.Initialize();c.TransportEnabled=false;c.SurfaceExchangeEnabled=false;
 int id=0;for(int s=0;s<6;s++)for(int k=0;k<12;k++)c.Water[s][k*c.Count+id]=0;
 c.Water[1][3*c.Count+id]=2;double before=c.TotalWater();
 for(int step=0;step<300;step++){c.SaturationAdjustment(id,4.5);c.ConvertHydrometeors(id,4.5);c.Sedimentation(4.5,.1);}
 double microError=(c.TotalWater()-before)/before;
 // Wetness persistence / drying without deleting its backing surface reservoir.
 double wetStart=.8;c.Film[id]=wetStart;double wb=c.TotalWater();for(int j=0;j<400;j++)c.SurfaceExchange(id,4.5);double filmEnd=c.Film[id];
 std::cout<<"],\"microphysics\":{\"relative_error\":"<<microError<<"},\"drying\":{\"film_start_mm\":"<<wetStart<<",\"film_end_mm\":"<<filmEnd<<",\"relative_error\":"<<(c.TotalWater()-wb)/wb<<"}}\n";
}
'''

def topology(res,path):
    normals=np.array([[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]])
    corners=[];index={};tiles=[];positions=[];owners={};links=[]
    def norm(p):return p/np.linalg.norm(p)
    for n in normals:
        u=np.array([0,1,0]) if abs(n[0])==1 else np.array([1,0,0]);v=np.cross(n,u)
        for y in range(res):
            for x in range(res):
                ids=[]
                for dx,dy in [(0,0),(1,0),(1,1),(0,1)]:
                    key=tuple(n*res+u*(2*(x+dx)-res)+v*(2*(y+dy)-res))
                    if key not in index:
                        q=np.array(key)/res;xx,yy,zz=q*q
                        point=q*np.sqrt([1-yy/2-zz/2+yy*zz/3,1-zz/2-xx/2+zz*xx/3,1-xx/2-yy/2+xx*yy/3]);index[key]=len(corners);corners.append(norm(point))
                    ids.append(index[key])
                positions.append(norm(sum(corners[k] for k in ids)));tiles.append(ids)
    areas=[]
    for i,ids in enumerate(tiles):
        p=positions[i];area=0
        for j in range(4):
            a,b=ids[j],ids[(j+1)%4];va,vb=corners[a],corners[b]
            area+=2*math.atan2(abs(np.dot(p,np.cross(va,vb))),1+np.dot(p,va)+np.dot(va,vb)+np.dot(vb,p))
            key=tuple(sorted((a,b)))
            if key in owners:
                other=owners[key];d=norm(p-positions[other]);links.append([other,i,*d,math.acos(np.clip(np.dot(va,vb),-1,1)),math.acos(np.clip(np.dot(p,positions[other]),-1,1))])
            else:owners[key]=i
        areas.append(area)
    with open(path,'w') as f:
        f.write(f'{len(positions)} {len(links)}\n')
        for p,a in zip(positions,areas):f.write(' '.join(map(str,[*p,a]))+'\n')
        for l in links:f.write(' '.join(map(str,l))+'\n')
    return dict(cells=len(positions),edges=len(links),area_sum=sum(areas),area_error=abs(sum(areas)-4*math.pi))

def main():
    res=int(sys.argv[1]) if len(sys.argv)>1 else 8
    steps=int(sys.argv[2]) if len(sys.argv)>2 else 1200
    text=SOURCE.read_text()
    methods=['Clamp','Relax','Lerp','Rand','Saturation','SetForcing','Initialize','Tick','UpdateDynamics','HorizontalTransport','Parcel','VerticalExchange','SurfaceExchange','SaturationAdjustment','ConvertHydrometeors','Sedimentation','Diagnose','AddWarmMoistBubble','TotalWater','WaterBudgetError','Finite']
    cpp=HEADER+''.join(translate(name,*method(text,name)) for name in methods)+DRIVER
    with tempfile.TemporaryDirectory(prefix='orbis42_') as tmp:
        tmp=Path(tmp);src=tmp/'physics_port.cpp';src.write_text(cpp);topo=topology(res,tmp/'topology.txt')
        subprocess.run(['g++','-std=c++17','-O2','-Wall',str(src),'-o',str(tmp/'test')],check=True,capture_output=True,text=True)
        run=subprocess.run([str(tmp/'test'),str(tmp/'topology.txt'),str(steps)],check=True,capture_output=True,text=True,timeout=120)
        data=json.loads(run.stdout)
    all_stats=data['natural']+data['perturbed']
    checks={
     'sphere_area_4pi':topo['area_error']<1e-10,
     'transport_conserves_water':abs(data['tracer']['relative_error'])<1e-10,
     'macro_cloud_mass_moves':data['tracer']['low_displacement_rad']>.3,
     'height_dependent_advection':data['tracer']['high_displacement_rad']>data['tracer']['low_displacement_rad']+.2,
     'water_budget_with_surface_and_runoff':all(abs(x['budget_relative'])<1e-9 for x in all_stats),
     'water_positive':all(x['min_water']>=-1e-10 for x in all_stats),
     'temperature_finite_bounded':all(160<x['min_T_K']<=x['max_T_K']<355 for x in all_stats),
     'natural_cloud_birth':data['natural_summary']['cloud_birth_cells']>0,
     'natural_cloud_dissipation':data['natural_summary']['cloud_dissipation_cells']>0,
     'natural_rain_and_wet_ground':data['natural_summary']['peak_rain_mm_h']>.1 and data['natural_summary']['peak_wetness']>.1,
     'microphysics_conserves_water':abs(data['microphysics']['relative_error'])<1e-10,
     'wet_film_drains_without_water_loss':0<=data['drying']['film_end_mm']<data['drying']['film_start_mm'] and abs(data['drying']['relative_error'])<1e-10,
    }
    report={'method':'mechanically translated C# equation bodies executed as C++17; NOT Unity/C#/HLSL compilation','source_sha256':hashlib.sha256(text.encode()).hexdigest(),'resolution':res,'steps_per_run':steps,'topology':topo,'checks':checks,'data':data,'unity_editor_import':'NOT_RUN','unity_shader_compilation':'NOT_RUN','visual_acceptance':'NOT_RUN','device_performance':'NOT_RUN'}
    RESULT.parent.mkdir(parents=True,exist_ok=True);RESULT.write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n')
    print(json.dumps(report,ensure_ascii=False,indent=2))
    return 0 if all(checks.values()) else 1
if __name__=='__main__':
    try:sys.exit(main())
    except subprocess.CalledProcessError as e:print(e.stderr);raise
