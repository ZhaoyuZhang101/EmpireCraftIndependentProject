#!/usr/bin/env python3
"""Short full-resolution C++ equation smoke test; NOT a Unity/C#/FPS test."""
import validate_v42_repair_core as repair
import json,hashlib,subprocess,tempfile
from pathlib import Path
b=repair.base
text=b.SOURCE.read_text()
methods=['Clamp','Relax','Lerp','Rand','Saturation','SetForcing','Initialize','Tick','UpdateDynamics','HorizontalTransport','Parcel','VerticalExchange','SurfaceExchange','SaturationAdjustment','ConvertHydrometeors','Sedimentation','Diagnose','AddWarmMoistBubble','TotalWater','WaterBudgetError','Finite']
driver=b.DRIVER.split('int main(')[0]+r'''
int main(int argc,char**argv){
 std::cout<<std::setprecision(16);Core a(argv[1]);forcing(a);a.Initialize();
 auto start=std::chrono::steady_clock::now();
 for(int i=0;i<32;i++)a.Tick(.1);
 double ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
 std::cout<<"{\"sample\":";stats(a,32);std::cout<<",\"total_equation_execution_ms\":"<<ms<<"}";
}
'''
with tempfile.TemporaryDirectory(prefix='orbis42_smoke_') as tmp:
 p=Path(tmp);cpp=b.HEADER+''.join(b.translate(n,*b.method(text,n)) for n in methods)+driver
 (p/'port.cpp').write_text('#include <chrono>\n'+cpp)
 topology=b.topology(24,p/'topo.txt')
 subprocess.run(['g++','-std=c++17','-O2',str(p/'port.cpp'),'-o',str(p/'test')],check=True,capture_output=True,text=True)
 run=subprocess.run([str(p/'test'),str(p/'topo.txt')],check=True,capture_output=True,text=True,timeout=40)
 data=json.loads(run.stdout)
s=data['sample'];checks={'finite_bounded_temperature':160<s['min_T_K']<=s['max_T_K']<355,'water_nonnegative':s['min_water']>=0,'water_budget':abs(s['budget_relative'])<1e-9}
report={'scope':'32 ticks / 3456 cells, current C# equation bodies translated to C++17. Not C#/Unity compilation; execution timing is not game FPS.','source_sha256':hashlib.sha256(text.encode()).hexdigest(),'topology':topology,'checks':checks,'data':data,'full_1200_step_run':'TIMEOUT_IN_THIS_ENVIRONMENT','unity_compilation':'NOT_RUN','device_fps':'NOT_RUN'}
out=b.ROOT/'Documentation/V4_2_Repair/validation_core_3456_smoke.json';out.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));raise SystemExit(not all(checks.values()))
