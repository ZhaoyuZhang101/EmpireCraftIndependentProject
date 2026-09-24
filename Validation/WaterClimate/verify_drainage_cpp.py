#!/usr/bin/env python3
"""Translate the actual drainage and classification method bodies; no Unity/C# execution."""
from pathlib import Path
import re, subprocess, tempfile, hashlib, json, datetime
R=Path(__file__).resolve().parents[2];C=R/'Assets/ORBIS/Runtime/FlatWorld/Core'
hyd=(C/'FlatHydrology.cs').read_text();facts=(C/'FlatTerrainAnalysis.cs').read_text();types=(C/'FlatTypes.cs').read_text()
def block(s,signature):
 p=s.index(signature);p=s.index('{',p);n=1;q=p+1
 while n:
  if s[q]=='{':n+=1
  elif s[q]=='}':n-=1
  q+=1
 return s[p:q]
def trans(s):
 for e in ['WaterKind','Koppen','Biome','Landform']:s=s.replace(e+'.',e+'::')
 s=s.replace('Math.Max','mx').replace('Math.Min','mn')
 return re.sub(r"(?<![\w.])(\d+)f\b",r"\1.0f",s)
b=trans(block(hyd,'public static void Build'))
b=b.replace('var visited=new bool[w.Count];var heap=new FloodHeap();','std::vector<bool> visited(w.Count);FloodHeap heap;').replace('var rank=new int[w.Count];','std::vector<int> rank(w.Count);').replace('heap.Count','heap.Count()')
header='''#include <vector>
#include <queue>
#include <cmath>
#include <stdexcept>
#include <iostream>
#include <algorithm>
#include <random>
#include <type_traits>
using byte=unsigned char;
template<class A,class B>auto mx(A a,B b){using T=typename std::common_type<A,B>::type;return std::max(T(a),T(b));}
template<class A,class B>auto mn(A a,B b){using T=typename std::common_type<A,B>::type;return std::min(T(a),T(b));}
'''
for name,source in [('WaterKind',types),('Koppen',types),('Biome',types),('Landform',facts)]:
 header+='enum class '+name+' {'+re.search(r'enum '+name+r'\s*:\s*byte\s*\{([^}]+)',source).group(1)+'};\n'
header+='''struct CancellationToken{void ThrowIfCancellationRequested(){}};
struct FloodHeap{
std::priority_queue<std::pair<int,int>,std::vector<std::pair<int,int>>,std::greater<std::pair<int,int>>> q;
int Count(){return int(q.size());}void Push(int id,int height){q.push({height,id});}int Pop(){int id=q.top().second;q.pop();return id;}};
struct WorldData{
int Width,Height,Count;std::vector<int> Downstream,DrainOrder;std::vector<short> Elevation,WaterLevel;std::vector<WaterKind> Water;std::vector<float> Runoff,Precipitation;
WorldData(int w,int h):Width(w),Height(h),Count(w*h),Downstream(Count),DrainOrder(Count),Elevation(Count),WaterLevel(Count),Water(Count),Runoff(Count),Precipitation(Count*12){}
int Neighbor(int i,int d){int x=i%Width,y=i/Width;if(d==0)x=(x+Width-1)%Width;if(d==1)x=(x+1)%Width;if(d==2)y--;if(d==3)y++;return y<0||y>=Height?-1:y*Width+x;}};
'''
code=header+'void Build(WorldData& w,CancellationToken cancel)'+b+'\n'
code+='Landform ClassifyForm(float elevation,float relief,float gradient,float hand,bool fresh,bool water,bool depression)'+trans(block(facts,'public static Landform ClassifyForm'))+'\n'
code+='Biome ClassifyBiome(Koppen k,float elevation,float temperature,float annualRain,byte wetMonths,float gradient,float hand,bool nearFresh,bool water)'+trans(block(facts,'public static Biome ClassifyBiome'))+'\n'
code+=r'''
int main(){try{int assertions=0,riverCount=0,lakeCount=0;double maxMassError=0;auto check=[&](bool ok,const char* msg){assertions++;if(!ok)throw std::runtime_error(msg);};
std::mt19937 random(314159);
for(int variant=0;variant<8;variant++){
 WorldData w(64,32);double input=0;
 for(int y=0;y<w.Height;y++)for(int x=0;x<w.Width;x++){
 int i=y*w.Width+x;int h=100+int(random()%30)*100;
 if(variant==1)h=100;if(variant==2)h=-100;if(variant==3)h=2000-y*50;
 if(variant==4)h=(x-32)*(x-32)+(y-16)*(y-16)+100;
 if(variant==5)h=(x==31?4000:100);
 if(variant==6)h=std::min(x,63-x)*100+100;
 if(variant==7)h=(x%8)*200+100;
 if((variant!=1&&variant!=4&&variant!=5)&&(y==0||x==0))h=-100;
 w.Elevation[i]=h;w.Water[i]=h<0?WaterKind::ShallowOcean:WaterKind::Land;
 float total=0;for(int m=0;m<12;m++){float p=variant==7?0:10+float(random()%220);w.Precipitation[i*12+m]=p;total+=p;}
 if(h>=0)input+=total/1200.f;
 }
 Build(w,CancellationToken{});std::vector<int> rank(w.Count),seen(w.Count);double output=0;
 for(int k=0;k<w.Count;k++){int i=w.DrainOrder[k];check(i>=0&&i<w.Count,"order bounds");check(!seen[i],"unique order");seen[i]=1;rank[i]=k;}
 for(int i=0;i<w.Count;i++){
 int p=w.Downstream[i];if(p>=0){check(rank[p]<rank[i],"acyclic");check(w.WaterLevel[p]<=w.WaterLevel[i],"non-uphill water head");}
 else output+=w.Runoff[i];
 check(std::isfinite(w.Runoff[i])&&w.Runoff[i]>=0,"valid runoff");
 if(w.Water[i]==WaterKind::River){riverCount++;check(w.WaterLevel[i]==w.Elevation[i],"exposed river cannot traverse unfilled basin");}
 if(w.Water[i]==WaterKind::Lake){lakeCount++;check(w.WaterLevel[i]>=w.Elevation[i],"lake above bed");}
 }
 double err=std::abs(input-output)/std::max(1.0,input);maxMassError=std::max(maxMassError,err);check(err<1e-5,"runoff accumulation mass balance");
}
check(ClassifyForm(1500,200,100,800,false,false,false)==Landform::Plateau,"plateau");
check(ClassifyForm(400,450,150,400,false,false,false)==Landform::Hills,"hills");
check(ClassifyForm(1800,1200,600,700,false,false,false)==Landform::Mountains,"mountains");
check(ClassifyForm(100,80,50,50,true,false,false)==Landform::Floodplain,"floodplain");
check(ClassifyBiome(Koppen::Af,100,26,1800,12,50,50,false,false)==Biome::Rainforest,"rainforest");
check(ClassifyBiome(Koppen::Af,100,26,1800,12,50,50,true,false)==Biome::Wetland,"wetland");
check(ClassifyBiome(Koppen::Af,500,26,1800,12,400,50,true,false)==Biome::Rainforest,"steep not swamp");
check(ClassifyBiome(Koppen::BWh,100,26,100,0,50,500,false,false)==Biome::Desert,"desert");
check(ClassifyBiome(Koppen::BSk,400,10,400,0,50,500,false,false)==Biome::Steppe,"steppe");
check(ClassifyBiome(Koppen::EF,100,-10,200,0,0,0,false,false)==Biome::Snow,"ice");
std::cout<<"{\"status\":\"PASS\",\"assertions\":"<<assertions<<",\"drainage_variants\":8,\"classification_cases\":10,\"river_cells_checked\":"<<riverCount<<",\"lake_cells_checked\":"<<lakeCount<<",\"maximum_relative_runoff_error\":"<<maxMassError<<"}";
}catch(const std::exception&e){std::cerr<<e.what();return 1;}return 0;}
'''
with tempfile.TemporaryDirectory() as t:
 d=Path(t);(d/'check.cpp').write_text(code)
 cp=subprocess.run(['g++','-std=c++17','-O2',str(d/'check.cpp'),'-o',str(d/'check')],text=True,capture_output=True)
 if cp.returncode:raise RuntimeError(cp.stderr)
 run=subprocess.run([str(d/'check')],text=True,capture_output=True)
 report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Actual C# FlatHydrology.Build and ClassifyForm/ClassifyBiome bodies mechanically translated to C++17. The heap/world wrappers are equivalent test harnesses; C#/Unity execution is NOT tested.','source_sha256':{'FlatHydrology.cs':hashlib.sha256(hyd.encode()).hexdigest(),'FlatTerrainAnalysis.cs':hashlib.sha256(facts.encode()).hexdigest()},'csharp_executed':False,'unity_editor_executed':False,'result':json.loads(run.stdout) if run.returncode==0 else {'status':'FAIL','stderr':run.stderr}}
 (R/'Validation/WaterClimate/drainage-cpp-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False,indent=2));raise SystemExit(run.returncode)
