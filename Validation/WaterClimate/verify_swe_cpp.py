#!/usr/bin/env python3
"""Mechanical translation of the ACTUAL new CPU solver to C++17.
Numerical cross-check only: not C#/Unity compilation, no rendered image evidence.
Requirements: Python 3, g++. Compiles into a temporary directory, never into Assets.
"""
from pathlib import Path
import re, subprocess, tempfile, json, hashlib, datetime
root=Path(__file__).resolve().parents[2]
path=root/'Assets/ORBIS/Runtime/FlatWorld/Core/FlatShallowWater.cs'
src=path.read_text();s=src[src.index('    public sealed class'):src.rfind('\n}')]
s=s.replace('public sealed class FlatShallowWater','class FlatShallowWater')
s=s.replace('    {\n        public readonly', '    {\n    public:\n        public readonly',1)
s=s.replace('public readonly ','').replace('readonly ','').replace('public ','')
s=s.replace('float[] restDepth','const std::vector<float>& restDepth')
s=s.replace('double[] ','std::vector<double> ').replace('bool[] ','std::vector<bool> ')
s=re.sub(r'(double|int) (\w+) \{ get; private set; \}',r'\1 \2=0;',s)
s=s.replace('restDepth==null||','').replace('restDepth.Length','int(restDepth.size())')
s=s.replace('new double[Count]','std::vector<double>(Count)').replace('new bool[Count]','std::vector<bool>(Count)')
s=s.replace('Array.Copy(next,Depth,Count);','Depth=next;')
s=s.replace('Math.Max','mx').replace('Math.Min','mn').replace('Math.Abs','std::abs').replace('Math.Exp','std::exp').replace('Math.Sqrt','std::sqrt')
s=s.replace('double.IsNaN','std::isnan').replace('double.IsInfinity','std::isinf').replace('FMath.Finite','std::isfinite')
s=re.sub(r'throw new (ArgumentException|ArgumentOutOfRangeException|InvalidOperationException)', 'throw std::runtime_error',s)
s=s.replace('nameof(requestedSeconds)','"requestedSeconds"')
s=s.rstrip()+';\n'
header='''#include <vector>
#include <cmath>
#include <stdexcept>
#include <iostream>
#include <iomanip>
#include <algorithm>
#include <type_traits>
template<class A,class B> auto mx(A a,B b){using R=typename std::common_type<A,B>::type;return std::max(R(a),R(b));}
template<class A,class B> auto mn(A a,B b){using R=typename std::common_type<A,B>::type;return std::min(R(a),R(b));}
'''
test=r'''
int main(){
try{
int checks=0;double maximumError=0,maxRestError=0,minimumDepth=1e30;
auto check=[&](bool ok,const char* msg){checks++;if(!ok)throw std::runtime_error(msg);};
for(int variant=0;variant<4;variant++){
 const int W=32,H=32;std::vector<float> rest(W*H);
 for(int y=0;y<H;y++)for(int x=0;x<W;x++)rest[y*W+x]=(variant==2&&x==15)?0:((variant==0)?2.0f:((variant==3)?.001f: .4f+(x+y)%9*.3f));
 FlatShallowWater a(W,H,variant==3?.25:2,rest);
 for(int n=0;n<80;n++)a.Advance(.1);
 for(int i=0;i<W*H;i++){double err=std::abs(a.Depth[i]-rest[i]);maxRestError=std::max(maxRestError,err);check(err<1e-10,"lake at rest");}
 a.Ripple(8,16,.08);
 check(a.RelativeMassError()<1e-10,"balanced injected wave");
 for(int n=0;n<1200;n++){a.Advance(.1);for(int i=0;i<W*H;i++){check(std::isfinite(a.Depth[i])&&a.Depth[i]>=0,"finite nonnegative depth");minimumDepth=std::min(minimumDepth,a.Depth[i]);}maximumError=std::max(maximumError,a.RelativeMassError());}
 check(a.RelativeMassError()<1e-10,"mass after 120 seconds");
 if(variant==2)for(int y=0;y<H;y++)for(int x=15;x<W;x++)check(std::abs(a.Depth[y*W+x]-rest[y*W+x])<1e-10,"dry reflective separator");
 double previous=a.Elapsed;a.Advance(100,3);check(a.LastSubsteps<=3&&a.Elapsed-previous<=.2+1e-12,"capped work and time debt");
 bool rejected=false;try{a.Advance(-1);}catch(const std::exception&){rejected=true;}check(rejected,"reject negative timestep");
}
std::cout<<std::setprecision(17)<<"{\"status\":\"PASS\",\"assertions\":"<<checks<<",\"variants\":4,\"simulation_seconds_per_variant\":120,\"maximum_relative_volume_error\":"<<maximumError<<",\"maximum_lake_at_rest_error_m\":"<<maxRestError<<",\"minimum_depth_m\":"<<minimumDepth<<"}";
}catch(const std::exception& e){std::cerr<<e.what();return 1;}return 0;
}
'''
with tempfile.TemporaryDirectory() as t:
 d=Path(t);(d/'solver.cpp').write_text(header+s+test)
 result=subprocess.run(['g++','-std=c++17','-O2','-Wall','-Wextra',str(d/'solver.cpp'),'-o',str(d/'solver')],capture_output=True,text=True)
 if result.returncode:raise RuntimeError(result.stderr)
 run=subprocess.run([str(d/'solver')],capture_output=True,text=True,timeout=30)
 report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Mechanical C++17 translation of actual FlatShallowWater.cs numerical methods; no C# runtime or Unity/GPU execution.','source_sha256':hashlib.sha256(src.encode()).hexdigest(),'csharp_executed':False,'unity_editor_executed':False,'compiler_warnings':result.stderr,'result':json.loads(run.stdout) if run.returncode==0 else {'status':'FAIL','stderr':run.stderr}}
 (root/'Validation/WaterClimate/swe-cpp-report.json').write_text(json.dumps(report,indent=2,ensure_ascii=False))
 print(json.dumps(report,indent=2,ensure_ascii=False));raise SystemExit(run.returncode)
