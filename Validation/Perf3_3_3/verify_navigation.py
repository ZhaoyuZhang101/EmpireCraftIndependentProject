"""Execute the production Wheel/GoGlobe decision bodies translated to C++ with camera hooks stubbed."""
from pathlib import Path
import re,subprocess,json
H=Path(__file__).resolve().parent;R=H.parents[1]
s=(R/'Assets/ORBIS/Runtime/FlatWorld/Presentation/FlatScrollNavigator.cs').read_text()
def body(name):
 a=s.index('public void '+name+'(');start=s.index('{',a);depth=1;b=start+1
 while depth:depth+=(s[b]=='{')-(s[b]=='}');b+=1
 return s[a:b].replace('public ','').replace('Mathf.','M::')
cpp=r'''
#include <cmath>
#include <stdexcept>
#include <iostream>
struct Vector2{float x=0,y=0;};
struct M{static float Clamp(float x,float a,float b){return std::fmin(b,std::fmax(a,x));}
static float Min(float a,float b){return std::fmin(a,b);}static float Max(float a,float b){return std::fmax(a,b);}
static float Abs(float x){return std::fabs(x);}static float Exp(float x){return std::exp(x);}};
struct Nav {
 static constexpr float EndDistance=1.08f,StartDistance=1.6f;
 bool IsFlat=true,exitPending=false,zoomAnchor=false,focusZoom=false,landingLocked=true,atlasPending=false,overview=false;
 float pixels=48,targetPixels=-1,ReturnPixelsPerTile=8,distance=1.08f,targetDistance=1.08f;
 Vector2 zoomScreen;int pendingFocus=-1,exits=0,captures=0;
 void ReanchorForExit(double){++exits;targetPixels=-1;focusZoom=false;pendingFocus=-1;distance=targetDistance=EndDistance;}
 void CaptureLanding(Vector2,double){++captures;landingLocked=true;}
'''+body('Wheel')+'\n'+body('GoGlobe')+r'''
};
int checks=0;void require(bool ok){checks++;if(!ok)throw std::runtime_error("navigation check "+std::to_string(checks));}
int main(){
 for(float threshold : {.25f,2.f,8.f,16.f,32.f}){
  Nav n;n.ReturnPixelsPerTile=threshold;
  for(int i=0;i<4;i++)n.Wheel(24,Vector2(),0);
  require(n.exitPending&&n.exits==0&&n.targetPixels==threshold);
  n.Wheel(-1,Vector2(),0);require(!n.exitPending&&n.targetPixels>n.pixels); // cancel exit AND reverse zoom immediately
  n.pixels=threshold;n.targetPixels=-1;n.Wheel(1,Vector2(),0);
  require(n.exits==1&&n.targetDistance==3.1f); // no fit-to-world step
  n.Wheel(-1,Vector2(),0);require(n.targetDistance==Nav::EndDistance);
  n.pixels=threshold*.5f;n.targetPixels=-1;n.Wheel(-1,Vector2(),0);
  require(n.targetPixels>n.pixels&&n.targetPixels<threshold); // explicit wide atlas zoom-in is continuous
 }
 for(float zoom : {2.f,8.f,24.f,48.f,64.f}){
  Nav n;n.pixels=zoom;n.exitPending=true;n.GoGlobe(0);
  require(n.pixels==zoom&&n.exits==1&&n.targetPixels<0&&!n.exitPending&&n.targetDistance==3.1f);
 }
 Nav n;n.IsFlat=false;n.distance=1.2f;n.targetDistance=3.1f;n.Wheel(-1,Vector2(),0);
 require(n.targetDistance<n.distance); // reverse from current pose, not queued far-orbit destination
 n.distance=1.2f;n.targetDistance=1.08f;n.Wheel(1,Vector2(),0);require(n.targetDistance>n.distance);
 std::cout<<checks;
}
'''
(H/'navigation_probe.cpp').write_text(cpp);subprocess.run(['g++','-std=c++17','-O2',str(H/'navigation_probe.cpp'),'-o',str(H/'navigation_probe')],check=True)
count=int(subprocess.check_output([str(H/'navigation_probe')],text=True))
# The one-shot latch belongs to Tick, not the decision-body harness. Check wiring explicitly.
assert 'if(IsFlat&&exitPending&&targetPixels<0)' in s
commit=s.split('void CommitProjection()')[1].split('void UpdateSheets()')[0]
assert commit.index('ApplyProjection(surface)')<commit.index('UpdateSheets()')
assert 'CommitProjection();' in s.split('public void Tick(')[1].split('float TileScale')[0]
assert 'Tick(0,day);' in s.split('void ReanchorForExit(')[1].split('public void Tick(')[0]
report={'status':'PASS','scope':__doc__,'executed_decision_checks':count,'commit_wiring_checks':4,'unity_camera_executed':False,'native_csharp_executed':False}
(H/'navigation-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
