#!/usr/bin/env python3
"""Execute mechanical translations of actual new C# method bodies, NOT C#/Unity."""
from pathlib import Path
import re,subprocess,tempfile,hashlib,json,datetime
R=Path(__file__).resolve().parents[2];C=R/'Assets/ORBIS/Runtime/FlatWorld/Core'
sampling=(C/'FlatSurfaceSampling.cs').read_text();geometry=(C/'FlatWaterGeometry.cs').read_text();eco=(C/'FlatEcology.cs').read_text();types=(C/'FlatTypes.cs').read_text()
def block(s,signature):
 p=s.index('{',s.index(signature));q=p+1;n=1
 while n:n+=(s[q]=='{')-(s[q]=='}');q+=1
 return s[p:q]
def trans(s):
 s=s.replace('Math.Min','mn').replace('Math.Max','mx').replace('Math.Abs','std::abs').replace('Math.Sqrt','std::sqrt').replace('Math.Floor','std::floor').replace('Math.Sign','sgn')
 s=s.replace('var current=(float[])raw.Clone();','std::vector<float> current=raw;').replace('raw.Length','int(raw.size())').replace('current.Length','int(current.size())').replace('var swap=current;','auto swap=current;')
 s=s.replace('FMath.','FMath::').replace('seeds==null||','').replace('seeds.Length','int(seeds.size())')
 s=s.replace('throw new ArgumentException','throw std::runtime_error').replace('double.NegativeInfinity','-INFINITY').replace('double.PositiveInfinity','INFINITY')
 s=re.sub(r'var (\w+)=new (float|double|int)\[([^;]+)\];',r'std::vector<\2> \1(\3);',s)
 s=s.replace('var f=w.Facts;if(f==null||','auto& f=w.Facts;if(').replace('var f=w.Facts;','auto& f=w.Facts;').replace('w.Facts==null||','')
 s=re.sub(r'\bout (?=[a-zA-Z])','',s)
 for en in ['Biome','Koppen','VegetationCover']:s=s.replace(en+'.',en+'::')
 return re.sub(r'(?<![\w.])(?:\d+\.\d*|\.\d+|\d+)(?:[eE][+-]?\d+)?f\b',lambda m:m.group() if ('.' in m.group() or 'e' in m.group().lower()) else m.group()[:-1]+'.0f',s)
header=r'''#include <vector>
#include <cmath>
#include <algorithm>
#include <random>
#include <iostream>
#include <stdexcept>
#include <type_traits>
#include <iomanip>
template<class A,class B> auto mn(A a,B b){using T=typename std::common_type<A,B>::type;return std::min(T(a),T(b));}
template<class A,class B> auto mx(A a,B b){using T=typename std::common_type<A,B>::type;return std::max(T(a),T(b));}
float sgn(float a){return (a>0)-(a<0);}
namespace FMath{template<class A,class B,class C>auto Clamp(A x,B a,C b){return mx(a,mn(x,b));}int Wrap(int x,int n){return (x%n+n)%n;}float Lerp(float a,float b,float t){return a+(b-a)*t;}}
struct CancellationToken{void ThrowIfCancellationRequested(){}};
struct Facts{std::vector<float> HalfWidth,FlowX,FlowY,Depth,ShoreDistance;std::vector<int>Connections;Facts(int n):HalfWidth(n),FlowX(n),FlowY(n),Depth(n),ShoreDistance(n,-8),Connections(n){}};
struct WorldData{int Width=64,Height=32,Count=2048;struct Facts Facts;WorldData():Facts(2048){}int Index(int x,int y){return y<0||y>=Height?-1:y*Width+FMath::Wrap(x,Width);}int Neighbor(int i,int d){return Index(i%Width+(d==0?-1:d==1?1:0),i/Width+(d==2?-1:d==3?1:0));}};
constexpr int CurveSteps=8;
'''
for en,src in [('Biome',types),('Koppen',types),('VegetationCover',eco)]:header+='enum class '+en+'{'+re.search(r'enum '+en+r'\s*:\s*byte\s*\{([^}]+)',src).group(1)+'};\n'
code=header
methods=[
('static float Minmod','float Minmod(float a,float b)',sampling),
('public static float Cubic','float Cubic(float p0,float p1,float p2,float p3,float t)',sampling),
('static float Row','float Row(const std::vector<float>& values,int w,int h,int x,int y,float t)',sampling),
('public static float Sample','float Sample(const std::vector<float>& values,int width,int height,float x,float y)',sampling),
('static void Transform1D','void Transform1D(const std::vector<double>& source,std::vector<double>& target,int count,std::vector<int>& sites,std::vector<double>& breaks)',sampling),
('public static float[] DistanceSquared','std::vector<float> DistanceSquared(const std::vector<bool>& seeds,int width,int height,CancellationToken cancel)',sampling),
('public static float[] SmoothShore','std::vector<float> SmoothShore(const std::vector<float>& raw,const std::vector<bool>& wet,int width,int height)',sampling),
('static float Smooth','float Smooth(float a,float b,float x)',geometry),
('static void Edge','void Edge(int direction,float& x,float& y)',geometry),
('static float EdgeWidth','float EdgeWidth(WorldData& w,int i,int direction)',geometry),
('public static float BezierDistance','float BezierDistance(float x,float y,float ax,float ay,float bx,float by,float cx,float cy,float ra,float rb,float rc)',geometry),
('public static float RiverDistance','float RiverDistance(WorldData& w,int i,float x,float y)',geometry),
('public static float MouthDistance','float MouthDistance(WorldData& w,int i,float x,float y)',geometry),
('public static VegetationCover Classify','VegetationCover Classify(Biome biome,Koppen climate,float temperature,float rain,float gradient)',eco),
('public static float Density','float Density(VegetationCover cover)',eco)]
for signature,cpp,src in methods:code+=cpp+trans(block(src,signature))+'\n'
code+=r'''
int main(){try{std::mt19937 random(320912);int assertions=0;auto check=[&](bool ok,const char*message){assertions++;if(!ok)throw std::runtime_error(message);};
for(int k=0;k<10000;k++){float a=int(random()%8001)-4000,b=int(random()%8001)-4000,c=int(random()%8001)-4000,d=int(random()%8001)-4000;
check(Cubic(a,b,c,d,0)==b,"exact left endpoint");check(Cubic(a,b,c,d,1)==c,"exact right endpoint");
for(int j=0;j<=64;j++){float value=Cubic(a,b,c,d,j/64.f);check(value>=mn(b,c)-.02&&value<=mx(b,c)+.02,"monotone cubic no overshoot");}}
std::vector<float> rawShore(2048,-8);std::vector<bool> wetShore(2048,false);wetShore[101]=true;rawShore[101]=.5f;
auto smoothShore=SmoothShore(rawShore,wetShore,64,32);for(int i=0;i<2048;i++)check((smoothShore[i]>0)==wetShore[i],"shore smoothing centre classification");check(rawShore[101]==.5f,"raw shore immutable");
float maxEdtError=0;for(int variant=0;variant<8;variant++){int width=19,height=11;std::vector<bool> seeds(width*height);
for(int i=0;i<width*height;i++)seeds[i]=variant==1||(variant>2&&random()%10==0);if(variant==2)seeds[0]=true;
auto distances=DistanceSquared(seeds,width,height,CancellationToken{});
for(int y=0;y<height;y++)for(int x=0;x<width;x++){float exact=1e12f;for(int i=0;i<width*height;i++)if(seeds[i]){int dx=std::abs(x-i%width),dy=y-i/width;dx=mn(dx,width-dx);exact=mn(exact,dx*dx+dy*dy);}float error=std::abs(distances[y*width+x]-exact);maxEdtError=mx(maxEdtError,error);check(error<.01,"exact periodic Euclidean distance");}}
std::vector<float> field(2048);for(auto& v:field)v=random()%4000;
for(int y=0;y<32;y++){check(Sample(field,64,32,0,y+.5)==Sample(field,64,32,64,y+.5),"periodic sampling seam");for(int x=0;x<64;x++)check(std::abs(Sample(field,64,32,x+.5,y+.5)-field[y*64+x])<.01,"2D exact centres");}
check(Sample(field,64,32,5.5,-4)==field[5],"latitude does not wrap");
check(BezierDistance(.375,.625,0,.5,.5,1,.5,.5,.07,.07,.07)<-.06,"curve through arc not right angle");
check(BezierDistance(.5,.5,0,.5,.5,1,.5,.5,.07,.07,.07)>0,"centre does not generate square river blob");
for(int a=0;a<4;a++)for(int b=0;b<4;b++)if(a!=b){float ax,ay,bx,by;Edge(a,ax,ay);Edge(b,bx,by);for(int j=0;j<=8;j++){float t=j/8.f,s=1-t;check(BezierDistance(s*s*ax+2*s*t*.5+t*t*bx,s*s*ay+2*s*t*.5+t*t*by,ax,ay,bx,by,.5,.5,.07,.07,.07)<0,"arc samples all wet");}}
int covers=0;for(int b=0;b<12;b++)for(int k=0;k<31;k++)for(int rain:{300,700,1600}){auto cover=Classify(Biome(b),Koppen(k),12,rain,100);float density=Density(cover);check(density>=0&&density<=1,"cover density bounded");covers|=1<<int(cover);}
check(covers==(1<<14)-1,"all fourteen covers attainable");
WorldData w;int left=12*64+20,right=left+1;w.Facts.Connections[left]=3;w.Facts.Connections[right]=9;w.Facts.FlowX[left]=1;w.Facts.FlowY[right]=1;
float maxSeam=0;for(int k=0;k<200;k++){w.Facts.HalfWidth[left]=.07+float(random()%330)/1000;w.Facts.HalfWidth[right]=.07+float(random()%330)/1000;
for(int j=0;j<=100;j++){float y=j/100.f,a=RiverDistance(w,left,1,y),b=RiverDistance(w,right,0,y);if(mn(std::abs(a),std::abs(b))<.03)maxSeam=mx(maxSeam,std::abs(a-b));}}
check(maxSeam<1e-5,"shared river face cross-section exact");

int mouths=0;
for(int direction=0;direction<4;direction++)for(float width:{.07f,.18f,.4f})for(int seam=0;seam<2;seam++){
 WorldData estuary;int land=16*64+(seam?(direction==0?0:63):20),sea=estuary.Neighbor(land,direction);
 estuary.Facts.HalfWidth[land]=width;estuary.Facts.Connections[land]=1<<direction;
 estuary.Facts.ShoreDistance[sea]=.15f;
 estuary.Facts.FlowX[land]=direction==0?-1:direction==1?1:0;
 estuary.Facts.FlowY[land]=direction==2?-1:direction==3?1:0;
 float ax,ay;Edge(direction,ax,ay);float bx=1-ax,by=1-ay;
 for(int j=0;j<=200;j++){
  float t=j/200.f,x=FMath::Lerp(bx,.5f,t),y=FMath::Lerp(by,.5f,t);
  check(MouthDistance(estuary,sea,x,y)<-.06f,"river mouth has uninterrupted wet corridor to area-water centre");mouths++;
 }
 for(int j=0;j<=100;j++){
  float cross=j/100.f;
  float x=direction<2?ax:cross,y=direction<2?cross:ay;
  float nx=direction<2?bx:cross,ny=direction<2?cross:by;
  float a=RiverDistance(estuary,land,x,y),b=MouthDistance(estuary,sea,nx,ny);
  if(std::min(std::abs(a),std::abs(b))<.02f)check(std::abs(a-b)<1e-5,"estuary face agrees on land and sea sides");
 }
 estuary.Facts.Connections[land]=0;check(MouthDistance(estuary,sea,.5,.5)==8,"unconnected coast stays closed");
 estuary.Facts.Connections[land]=1<<direction;estuary.Facts.ShoreDistance[sea]=-.15;
 check(MouthDistance(estuary,sea,.5,.5)==8,"mouth never cuts unrelated land");
}
std::cout<<std::setprecision(10)<<"{\"status\":\"PASS\",\"assertions\":"<<assertions<<",\"random_cubic_cases\":10000,\"edt_cases\":8,\"maximum_edt_error\":"<<maxEdtError<<",\"river_boundary_distance_disagreement_tiles\":"<<maxSeam<<",\"cover_mask\":"<<covers<<"}";
return 0;}catch(std::exception&e){std::cerr<<e.what()<<std::endl;return 1;}}
'''
with tempfile.TemporaryDirectory() as directory:
 p=Path(directory);(p/'surface.cpp').write_text(code)
 result=subprocess.run(['g++','-std=c++17','-O2',str(p/'surface.cpp'),'-o',str(p/'surface')],capture_output=True,text=True)
 if result.returncode:raise RuntimeError(result.stderr)
 run=subprocess.run([str(p/'surface')],capture_output=True,text=True)
 if run.returncode:raise RuntimeError(run.stderr)
 result=json.loads(run.stdout)
report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Actual method bodies translated to C++17; array/world wrappers are test scaffolding. Not native C#, Unity or HLSL.', 'source_sha256':{n:hashlib.sha256((C/n).read_bytes()).hexdigest() for n in ['FlatSurfaceSampling.cs','FlatWaterGeometry.cs','FlatEcology.cs']},'csharp_executed':False,'unity_editor_executed':False,'result':result}
(R/'Validation/Perf3_3_2/surface-cpp-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
