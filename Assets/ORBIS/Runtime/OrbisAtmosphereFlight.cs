using UnityEngine;
namespace Orbis
{
    // This is the SAME camera and world, not a second scene or a video overlay.
    public sealed partial class OrbisDemo
    {
        float lookTilt,smoothLookTilt,lookTiltVelocity;
        float lookHeading,smoothLookHeading,lookHeadingVelocity;
        float flightTime,flightStartAltitude,flightStartTilt;
        bool atmosphereFlight;
        public bool AtmosphereFlightActive=>atmosphereFlight;
        public float LookTilt=>lookTilt;
        public float CameraRadius=>View&&PlanetCamera?View.transform.InverseTransformPoint(PlanetCamera.transform.position).magnitude:1+smoothAltitude;
        void ApplyAtmosphereLook(Quaternion orbit,Vector3 radial,float dt)
        {
            smoothLookTilt=Mathf.SmoothDamp(smoothLookTilt,lookTilt,ref lookTiltVelocity,.22f,Mathf.Infinity,dt);
            smoothLookHeading=Mathf.SmoothDampAngle(smoothLookHeading,lookHeading,ref lookHeadingVelocity,.22f,Mathf.Infinity,dt);
            float theta=smoothLookTilt*Mathf.Deg2Rad,heading=smoothLookHeading*Mathf.Deg2Rad;
            Vector3 tangent=(orbit*Vector3.up)*Mathf.Cos(heading)+(orbit*Vector3.right)*Mathf.Sin(heading);
            Vector3 forward=-radial*Mathf.Cos(theta)+tangent*Mathf.Sin(theta);
            Vector3 up=tangent*Mathf.Cos(theta)+radial*Mathf.Sin(theta);
            // Orthonormal even at 90 degrees; LookAt(center,worldUp) has a horizon singularity.
            PlanetCamera.transform.rotation=Quaternion.LookRotation(forward,up);
        }
        public void SetAtmosphereLook(float degrees)
        {
            StopAtmosphereFlight();lookTilt=Mathf.Clamp(degrees,0,110);
        }
        public void StopAtmosphereFlight(){atmosphereFlight=false;}
        void PrepareAtmosphereView()
        {
            if(MapMode)ToggleMap();
            AutoOrbit=false;World.settings.atmosphere=true;World.settings.surfaceDebug=0;
            View.layer=0;View.UpdateColors(true);
            PlanetCamera.GetComponent<OrbisAtmosphereRenderer>().cloudDebug=0;
            hud.SyncSettings();
        }
        public void EnterAtmosphere()
        {
            StopAtmosphereFlight();PrepareAtmosphereView();altitude=.006f;lookTilt=82;
            Notice="進入低層大氣。滾輪可繼續進出；仰俯角 0° 看地面、90° 看切線方向。原世界與天氣保持不變。";hud.Refresh();
        }
        public void ExitAtmosphere()
        {
            StopAtmosphereFlight();PrepareAtmosphereView();altitude=2.65f;lookTilt=0;
            Notice="返回太空。仍使用同一相機、同一散射積分，不切換天空貼圖。";hud.Refresh();
        }
        public void ToggleAtmosphereFlight()
        {
            if(atmosphereFlight){StopAtmosphereFlight();hud.Refresh();return;}
            PrepareAtmosphereView();flightStartAltitude=Mathf.Clamp(smoothAltitude,.003f,5.5f);
            flightStartTilt=smoothLookTilt;flightTime=0;atmosphereFlight=true;
            Notice="穿越演示：下降 → 近地觀察 → 返回軌道。拖曳、滾輪、改仰俯角或切地圖可立即接管。";hud.Refresh();
        }
        void TickAtmosphereFlight(float dt)
        {
            if(!atmosphereFlight||MapMode)return;
            flightTime+=dt;
            if(flightTime<12)
            {
                float t=Mathf.SmoothStep(0,1,flightTime/12);
                altitude=Mathf.Exp(Mathf.Lerp(Mathf.Log(flightStartAltitude),Mathf.Log(.006f),t));
                lookTilt=Mathf.Lerp(flightStartTilt,82,t);
            }
            else if(flightTime<16){altitude=.006f;lookTilt=82;}
            else if(flightTime<30)
            {
                float t=Mathf.SmoothStep(0,1,(flightTime-16)/14);
                altitude=Mathf.Exp(Mathf.Lerp(Mathf.Log(.006f),Mathf.Log(2.65f),t));lookTilt=Mathf.Lerp(82,0,t);
            }
            else{altitude=2.65f;lookTilt=0;StopAtmosphereFlight();hud.Refresh();}
        }
        public void ObserveAtmosphereDusk()
        {
            StopAtmosphereFlight();PrepareAtmosphereView();
            // Move ONLY the observer near the current terminator. No time, climate,
            // rotation, civilizations or actual sunlight are edited for this preset.
            Vector3 sun=VisualSun.normalized;
            Vector3 current=View.transform.InverseTransformPoint(PlanetCamera.transform.position).normalized;
            Vector3 side=Vector3.ProjectOnPlane(current,sun);
            if(side.sqrMagnitude<.001f)side=Vector3.Cross(sun,Mathf.Abs(sun.y)>.9f?Vector3.right:Vector3.up);
            Vector3 local=(side.normalized+sun*.012f).normalized;
            Vector3 radial=View.transform.TransformDirection(local).normalized;
            yaw=Mathf.Atan2(radial.x,radial.z)*Mathf.Rad2Deg;pitch=-Mathf.Asin(Mathf.Clamp(radial.y,-1,1))*Mathf.Rad2Deg;
            Quaternion orbit=Quaternion.Euler(pitch,yaw,0);
            Vector3 solarWorld=View.transform.TransformDirection(sun);
            lookHeading=Mathf.Atan2(Vector3.Dot(solarWorld,orbit*Vector3.right),Vector3.Dot(solarWorld,orbit*Vector3.up))*Mathf.Rad2Deg;
            altitude=.006f;lookTilt=88;
            Notice="晨昏觀測：只移動相機到當前晨昏線附近並朝向太陽；不修改世界時間。";hud.Refresh();
        }
    }
}
