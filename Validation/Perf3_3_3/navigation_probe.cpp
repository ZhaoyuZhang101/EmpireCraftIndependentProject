
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
void Wheel(float wheel, Vector2 screen, double day)
        {
            wheel = M::Clamp(wheel, -24, 24);if(M::Abs(wheel)<.00001f)return;
            if(exitPending&&wheel<0)targetPixels=pixels;
            exitPending=false;
            if (IsFlat)
            {
                if(wheel<0)targetDistance=EndDistance;
                float minimum = ReturnPixelsPerTile;
                float currentTarget=targetPixels>0?targetPixels:pixels;
                float desired=currentTarget*M::Exp(-wheel*.075f);
                if(wheel>0 && desired<=minimum)
                {
                    zoomAnchor=false;focusZoom=false;pendingFocus=-1;
                    // Already at/beyond the landing scale: fold at THIS centre and zoom.
                    if(pixels<=minimum*1.001f)
                    { ReanchorForExit(day);targetDistance=3.1f;return; }
                    // Cross the threshold without needing another wheel notch after easing settles.
                    targetPixels=minimum;exitPending=true;return;
                }
                // Explicit atlas mode may be wider than the return threshold; zooming IN
                // from there stays continuous instead of snapping back to the landing scale.
                targetPixels=M::Clamp(desired,M::Min(minimum,pixels),M::Max(64,minimum));
                zoomScreen=screen;zoomAnchor=true;focusZoom=false;
            }
            else
            {
                float from=((wheel<0&&targetDistance>distance)||(wheel>0&&targetDistance<distance))?distance:targetDistance;
                float desired = M::Clamp(from * M::Exp(wheel * .035f), EndDistance, 6);
                if (desired < StartDistance && !landingLocked) CaptureLanding(screen, day);
                targetDistance = desired;
            }
        }
void GoGlobe(double day)
        {
            // M returns immediately from the current view. ReanchorForExit derives
            // the perspective end gap from the actual orthographic size, preserving scale.
            exitPending=false;
            if (IsFlat) ReanchorForExit(day);
            atlasPending = false; overview = false; targetPixels=-1;pendingFocus=-1;focusZoom=false;targetDistance = 3.1f;
        }
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
