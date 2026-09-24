#!/usr/bin/env python3
"""Extract CURRENT C# numerical method bodies, translate language glue to C++17,
and execute the reduced-order atmosphere. NOT a C# or Unity/shader compilation.
python validate_terrain_perf2_core.py --resolution 24 --steps 1200 [--baseline ROOT]
"""
from pathlib import Path
import argparse, json, sys, subprocess, tempfile, hashlib, re, time
import validate_v42 as base
ROOT=Path(__file__).resolve().parents[1]
METHODS=['Clamp','Relax','Lerp','Rand','Saturation','SaturationSlope','SetForcing','Initialize','Tick','UpdateDynamics','HorizontalTransport','Parcel','VerticalExchange','SurfaceExchange','SaturationAdjustment','ConvertHydrometeors','Sedimentation','Diagnose','ProcessLightning','AddWarmMoistBubble','TotalWater','WaterBudgetError','Finite']
def translated(source):
    h=base.HEADER.replace('const double PI=', 'double Tanh(double x){return tanh(x);} double Sign(double x){return (x>0)-(x<0);} const double PI=')
    h=h.replace('double Length()const','AtmosVector& operator+=(AtmosVector b){*this=*this+b;return *this;} double Length()const')
    h=h.replace('Position,Wind,baseWind;', 'Position,Wind,baseWind,pressureGradient,eastDirection,thermalGradient; vector<double> jetStrength,hemisphere,SynopticAscent,edgeDonorWeight,edgeReceiverWeight,externalStep,runoffStep,lightningRate,mixedIceScratch,graupelScratch,collisionScratch; vector<int> edgeDonor,edgeReceiver;')
    h=h.replace('baseWind.resize(n);', '''baseWind.resize(n);pressureGradient.resize(n);eastDirection.resize(n);thermalGradient.resize(n);jetStrength.resize(n);hemisphere.resize(n);SynopticAscent.resize(n);edgeDonor.resize(e);edgeReceiver.resize(e);edgeDonorWeight.resize(e);edgeReceiverWeight.resize(e);externalStep.resize(n);runoffStep.resize(n);lightningRate.resize(n);mixedIceScratch.resize(n);graupelScratch.resize(n);collisionScratch.resize(n);
      for(int i=0;i<n;i++){auto p=Position[i];eastDirection[i]=AtmosVector(p.Z,0,-p.X).Normalized();jetStrength[i]=exp(-pow((fabs(p.Y)-.60)/.22,2));hemisphere[i]=tanh(p.Y*5);}''')
    if 'double square=X*X+Y*Y+Z*Z' in source:
        h=h.replace('return Length()>m?*this*(m/Length()):*this;','double square=X*X+Y*Y+Z*Z;return square>m*m?*this*(m/sqrt(square)):*this;')
    methods=[]
    for name in METHODS:
        args,body=base.method(source,name)
        body=body.replace('var centers=new AtmosVector[6];var radii=new double[6];','vector<AtmosVector> centers(6);vector<double> radii(6);').replace('centers.Length','centers.size()')
        for arr in ('pressureGradient','thermalGradient'):
            body=body.replace(f'Array.Clear({arr},0,Count);',f'std::fill({arr}.begin(),{arr}.end(),AtmosVector());')
        body=body.replace('Wind[i].Length','Wind[i].Length()')
        translated_method=base.translate(name,args,body)
        if name=='ProcessLightning': translated_method=translated_method.replace('double ProcessLightning','void ProcessLightning',1)
        methods.append(translated_method)
    return h+''.join(methods)
# All scenarios are explicit tests, not hidden normal-weather injections.
DRIVER=base.DRIVER[:base.DRIVER.index('int main(')]+r'''
void bands(Core& a){
 double area[4]={},cloud[4]={},wet[4]={},path[4]={},ascent[4]={};
 for(int i=0;i<a.Count;i++){
  double lat=asin(std::abs(a.Position[i].Y))*180/M::PI;int b=lat<20?0:lat<35?1:lat<65?2:3;
  area[b]+=a.Area[i];cloud[b]+=a.Coverage[i]*a.Area[i];wet[b]+=(a.Coverage[i]>.15)*a.Area[i];ascent[b]+=std::abs(a.SynopticAscent[i])*a.Area[i];
  for(int k=0;k<12;k++)path[b]+=(a.Water[1][k*a.Count+i]+a.Water[2][k*a.Count+i])*a.Area[i];
 }
 std::cout<<"[";for(int b=0;b<4;b++){if(b)std::cout<<",";std::cout<<"{\"band\":"<<b<<",\"mean_cover\":"<<cloud[b]/area[b]<<",\"area_cloudy\":"<<wet[b]/area[b]<<",\"mean_cloud_path_kg_m2\":"<<path[b]/area[b]<<",\"mean_abs_synoptic_m_s\":"<<ascent[b]/area[b]<<"}";}std::cout<<"]";
}
double path(Core& a,int cell){double p=0;for(int k=0;k<12;k++)p+=a.Water[1][k*a.Count+cell]+a.Water[2][k*a.Count+cell];return p;}
int main(int argc,char** argv){
 std::cout<<std::setprecision(16);const char* topo=argv[1];int steps=atoi(argv[2]);
 Core a(topo);forcing(a);a.Initialize();auto initial=a.Coverage;vector<bool> born(a.Count,false),died(a.Count,false);
 std::cout<<"{\"initial_bands\":";bands(a);std::cout<<",\"history\":[";
 for(int step=1;step<=steps;step++){
  a.Tick(.1);
  for(int i=0;i<a.Count;i++){born[i]=born[i]||(initial[i]<.02&&a.Coverage[i]>.15);died[i]=died[i]||(initial[i]>.15&&a.Coverage[i]<.02);}
  if(step%200==0){if(step>200)std::cout<<",";std::cout<<"{\"stats\":";stats(a,step);std::cout<<",\"bands\":";bands(a);std::cout<<"}";}
 }
 int births=0,deaths=0;for(int i=0;i<a.Count;i++){births+=born[i];deaths+=died[i];}
 std::cout<<"],\"births\":"<<births<<",\"dissipations\":"<<deaths;
 // Pure horizontal tracer transport on nonuniform spherical cell areas.
 Core t(topo);forcing(t);t.Initialize();for(auto& q:t.Water)std::fill(q.begin(),q.end(),0);
 for(int i=0;i<t.Count;i++)for(int k=0;k<12;k++){auto p=t.Position[i];t.Water[1][k*t.Count+i]=p.Z>.94?1:0;t.Wind[k*t.Count+i]=AtmosVector(p.Z,0,-p.X)*(.018+k*.0012);}
 auto start=centroid(t,0);double before=t.TotalWater();for(int n=0;n<500;n++)t.HorizontalTransport(.1);
 std::cout<<",\"transport\":{\"relative_error\":"<<(t.TotalWater()-before)/before<<",\"low_displacement\":"<<angle(start,centroid(t,0))<<",\"high_displacement\":"<<angle(start,centroid(t,11))<<"}";
 // Controlled nonconvective ascent vs the same stable, nearly saturated column.
 Core lift(topo),control(topo);forcing(lift);forcing(control);lift.Initialize();control.Initialize();int id=0;
 for(auto c:{&lift,&control}){
  c->SetForcing(id,283.15,0,.7,true,AtmosVector(),1013,0,0,0);c->Updraft[id]=0;c->ColdPool[id]=0;c->gustLift[id]=0;c->orographicLift[id]=0;c->convergence[id]=0;
  for(int k=0;k<12;k++){int j=k*c->Count+id;c->Temperature[j]=std::max(210.,283.15-.004*c->Height[k]);for(int s=0;s<6;s++)c->Water[s][j]=0;c->Water[0][j]=c->Saturation(c->Temperature[j],c->Pressure[k])*c->AirMass[k]*.98;}
 }
 lift.SynopticAscent[id]=.30;control.SynopticAscent[id]=0;double lb=lift.TotalWater(),cb=control.TotalWater();
 for(int step=0;step<400;step++){lift.Parcel(id,.1);lift.SaturationAdjustment(id,4.5);control.Parcel(id,.1);control.SaturationAdjustment(id,4.5);}
 std::cout<<",\"controlled_synoptic\":{\"lift_path\":"<<path(lift,id)<<",\"control_path\":"<<path(control,id)<<",\"lift_updraft\":"<<lift.Updraft[id]<<",\"control_updraft\":"<<control.Updraft[id]<<",\"lift_water_error\":"<<(lift.TotalWater()-lb)/lb<<",\"control_water_error\":"<<(control.TotalWater()-cb)/cb<<"}";
 // Marine humidity exchange in darkness; wind changes, same finite source reservoir.
 Core low(topo),high(topo);forcing(low);forcing(high);low.Initialize();high.Initialize();
 for(auto c:{&low,&high}){c->SetForcing(0,288,0,.7,true,AtmosVector(),1013,0,0,0);c->Water[0][0]=c->Saturation(288,c->Pressure[0])*c->AirMass[0]*.65;c->SurfaceWater[0]=20;}
 low.Wind[0]=AtmosVector();high.Wind[0]=AtmosVector(.028,0,0);double beforeLow=low.TotalWater(),beforeHigh=high.TotalWater(),sl=low.SurfaceWater[0],sh=high.SurfaceWater[0];low.SurfaceExchange(0,4.5);high.SurfaceExchange(0,4.5);
 std::cout<<",\"night_evaporation\":{\"calm_mm\":"<<sl-low.SurfaceWater[0]<<",\"windy_mm\":"<<sh-high.SurfaceWater[0]<<",\"water_error\":"<<std::max(std::abs(low.TotalWater()-beforeLow),std::abs(high.TotalWater()-beforeHigh))<<"}}\n";
}
'''
def run(root,res,steps):
    source=(root/'Assets/ORBIS/Runtime/Weather/OrbisAtmosphereCore.cs').read_text()
    cpp=translated(source)+DRIVER
    with tempfile.TemporaryDirectory(prefix='orbis_perf2_') as temp:
        temp=Path(temp);(temp/'core.cpp').write_text(cpp)
        topology=base.topology(res,temp/'topology.txt')
        build=subprocess.run(['g++','-std=c++17','-O2','-Wall',str(temp/'core.cpp'),'-o',str(temp/'core')],capture_output=True,text=True)
        if build.returncode:raise RuntimeError(build.stderr)
        start=time.perf_counter();result=subprocess.run([str(temp/'core'),str(temp/'topology.txt'),str(steps)],capture_output=True,text=True,check=True,timeout=150)
        elapsed=time.perf_counter()-start
    return {'source_sha256':hashlib.sha256(source.encode()).hexdigest(),'translated_method_count':len(METHODS),'scope':'Mechanically translated current equation bodies; C++ scaffold, NOT C#/Unity compilation. Idealized forcing, not actual Europe or ORBIS save.','topology':topology,'steps':steps,'elapsed_seconds_including_all_scenarios':elapsed,'data':json.loads(result.stdout)}
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--resolution',type=int,default=8);parser.add_argument('--steps',type=int,default=1200);parser.add_argument('--baseline',type=Path);args=parser.parse_args()
    out=run(ROOT,args.resolution,args.steps)
    d=out['data'];checks={
        'finite_numerics':all(abs(s['stats']['budget_relative'])<1e-10 and s['stats']['min_water']>=0 and 180<=s['stats']['min_T_K']<=s['stats']['max_T_K']<=345 for s in d['history']),
        'transport_water_conservation':abs(d['transport']['relative_error'])<1e-10,
        'cloud_tracer_moves_and_shears':d['transport']['low_displacement']>.1 and d['transport']['high_displacement']>d['transport']['low_displacement'],
        'nonconvective_lifting_makes_more_condensate':d['controlled_synoptic']['lift_path']>d['controlled_synoptic']['control_path']+1e-5 and d['controlled_synoptic']['lift_updraft']<1e-8,
        'synoptic_uses_existing_water':abs(d['controlled_synoptic']['lift_water_error'])<1e-10,
        'wind_driven_evaporation_in_darkness':d['night_evaporation']['windy_mm']>d['night_evaporation']['calm_mm']>0,
        'night_evaporation_closed_water':d['night_evaporation']['water_error']<1e-8,
        'midlatitude_condensate_not_permanently_zero':any(s['bands'][2]['mean_cloud_path_kg_m2']>1e-5 for s in d['history']),
    }
    out['checks']=checks;out['all_passed']=all(checks.values())
    if args.baseline:out['baseline']=run(args.baseline,args.resolution,args.steps)
    target=ROOT/f'Documentation/TerrainPerf2/core-r{args.resolution}-s{args.steps}.json';target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps(out,ensure_ascii=False,indent=2))
    print(target);print(json.dumps(checks,indent=2));return 0 if out['all_passed'] else 1
if __name__=='__main__':sys.exit(main())
