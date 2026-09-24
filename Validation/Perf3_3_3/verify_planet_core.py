"""Compile mechanically translated production coordinate bodies to C++; NOT a C# semantic build."""
from pathlib import Path
import re,subprocess,json,hashlib
H=Path(__file__).resolve().parent;R=H.parents[1]
p=R/'Assets/ORBIS/Runtime/Planet/Core/PlanetCoordinateSystem.cs';source=p.read_text()
def end(s,start):
 depth=1;k=start+1
 while depth:
  depth+=(s[k]=='{')-(s[k]=='}');k+=1
 return k
bodies=[]
for m in re.finditer(r'public (?:static class|struct) (\w+)\s*\{',source):
 name=m[1];a=source.index('{',m.start());body=source[a+1:end(source,a)-1]
 body=body.replace('public static PlanetVector operator','friend PlanetVector operator')
 body=re.sub(r'\bpublic\s+','',body)
 body=body.replace('const double Tau','static constexpr double Tau')
 body=re.sub(r'new (\w+)\(',r'\1(',body)
 body=re.sub(r'throw (?:ArgumentOutOfRangeException|ArgumentException)\([^;]*\);',r'throw std::runtime_error("invalid coordinate");',body)
 def initializer(m):
  fields=re.findall(r'(Origin|Up|East|North)=(.*?)(?=,\s*(?:Origin|Up|East|North)=|$)',m[1],re.S)
  return 'PlanetTangentFrame frame;'+''.join('frame.'+k+'='+v.strip()+';' for k,v in fields)+'return frame;'
 body=re.sub(r'return new PlanetTangentFrame \{(.*?)\};',initializer,body,flags=re.S)
 for key,value in {'PI':'3.1415926535897932384626433832795','Cos':'std::cos','Sin':'std::sin','Sqrt':'std::sqrt','Atan2':'std::atan2','Floor':'std::floor','Max':'std::fmax','Min':'std::fmin'}.items():body=body.replace('Math.'+key,value)
 body=re.sub(r'\b(PlanetVector|PlanetCoordinateSystem)\.',r'\1::',body)
 if name=='PlanetVector':body='PlanetVector():X(0),Y(0),Z(0){}\n'+body
 bodies.append('struct '+name+'{'+body+'};')
cpp='#include <cmath>\n#include <stdexcept>\n#include <iostream>\n'+ '\n'.join(bodies)+r'''
using C=PlanetCoordinateSystem;
static int checks=0;
void require(bool v){++checks;if(!v)throw std::runtime_error("coordinate assertion "+std::to_string(checks));}
double error(PlanetVector a,PlanetVector b){auto d=a-b;return std::sqrt(PlanetVector::Dot(d,d));}
int main(){
 for(double lon : {-180.,-179.999,-90.,0.,90.,179.999,180.})
 for(double lat : {-90.,-89.999,-70.,0.,70.,89.999,90.}){
  PlanetLocation loc(lon*C::Tau/360,lat*C::Tau/360,3200);
  for(double far : {0.,1e12}){
   PlanetVector center(far,far,-far);auto frame=C::LocalFrame(loc,center,6371000);
   require(std::abs(PlanetVector::Dot(frame.East,frame.North))<1e-12);
   require(std::abs(PlanetVector::Dot(frame.Up,frame.North))<1e-12);
   require(std::abs(PlanetVector::Dot(frame.Up,frame.East))<1e-12);
   require(std::abs(PlanetVector::Dot(frame.Up,frame.Up)-1)<1e-12);
   auto inv=C::FromWorld(frame.Origin,center,6371000);
   require(error(C::ToWorld(inv,center,6371000),frame.Origin)<.001);
   PlanetVector local(120.345,-678.123,42.456);
   require(error(frame.ToLocal(frame.ToWorld(local)),local)<.001);
   require(error(frame.DirectionToLocal(frame.DirectionToWorld(local)),local)<1e-9);
   for(double laps : {-100.,-1.,0.,1.,100.}){
    auto uv=C::FromMapUV(C::MapU(loc)+laps,C::MapV(loc));
    require(error(C::SurfaceNormal(uv),C::SurfaceNormal(loc))<1e-12);
   }
  }
 }
 for(int w : {64,256,512,1024}){
  PeriodicMapPosition pos;pos.Set(w-.25,w/4,w,w/2);
  for(int i=0;i<100000;i++)pos.Move(.000003,0,w,w/2);
  require(std::abs(pos.X-.05)<1e-8); // Float accumulation loses these small steps at W=1024.
  for(int i=0;i<10000;i++){
   pos.Move(w+.125,0,w,w/2);
   double x=pos.RenderX(w*.67,w);
   require(std::abs(C::NearestOffset(x-pos.X,w))<1e-10);
   require(x>=w*.17-1e-10&&x<w*1.17+1e-10);
  }
 }
 bool rejected=false;try{C::FromWorld(PlanetVector(),PlanetVector(),1);}catch(...){rejected=true;}
 require(rejected);std::cout<<checks;
}
'''
(H/'planet_core_probe.cpp').write_text(cpp)
subprocess.run(['g++','-std=c++17','-O2',str(H/'planet_core_probe.cpp'),'-o',str(H/'planet_core_probe')],check=True)
result=subprocess.check_output([str(H/'planet_core_probe')],text=True)
report={'status':'PASS','assertions':int(result),'scope':__doc__,'source_sha256':hashlib.sha256(source.encode()).hexdigest(),'unity_executed':False,'native_csharp_executed':False}
(H/'planet-core-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
